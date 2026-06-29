using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MartialHeroes.Network.Protocol.Generators;

[Generator]
public sealed class PacketRouterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) =>
        {
            var opcodeAttrSymbol = compilation.GetTypeByMetadataName(
                "MartialHeroes.Network.Protocol.Core.Opcodes.PacketOpcodeAttribute");
            var packets = new List<PacketInfo>();
            var seenOpcodes = new HashSet<uint>();
            var globalNs = compilation.GlobalNamespace;
            foreach (var topLevel in globalNs.GetNamespaceMembers())
            {
                if (topLevel.Name != "MartialHeroes")
                    continue;

                CollectTaggedStructs(topLevel, opcodeAttrSymbol, packets, seenOpcodes, compilation);
            }

            var handlerInterface = compilation.GetTypeByMetadataName(
                "MartialHeroes.Network.Protocol.Routing.Routing.IPacketHandler");

            var handleOverloadFqns = new HashSet<string>(
                StringComparer.Ordinal);

            if (handlerInterface is not null)
                foreach (var member in handlerInterface.GetMembers())
                    if (member is IMethodSymbol method && method.Name == "Handle"
                                                       && method.Parameters.Length == 1)
                    {
                        var param = method.Parameters[0];
                        if (param.RefKind == RefKind.In)
                        {
                            var typeFqn = param.Type.ToDisplayString(
                                new SymbolDisplayFormat(
                                    SymbolDisplayGlobalNamespaceStyle.Omitted,
                                    SymbolDisplayTypeQualificationStyle
                                        .NameAndContainingTypesAndNamespaces));
                            handleOverloadFqns.Add(typeFqn);
                        }
                    }

            packets.Sort(static (a, b) => a.PackedOpcode.CompareTo(b.PackedOpcode));

            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using MartialHeroes.Network.Protocol.Packets;");
            sb.AppendLine();
            sb.AppendLine("namespace MartialHeroes.Network.Protocol.Routing.Routing;");
            sb.AppendLine();
            sb.AppendLine("public static partial class PacketRouter");
            sb.AppendLine("{");
            sb.AppendLine(
                "    private static bool RouteGenerated(uint packedOpcode, System.ReadOnlySpan<byte> payload, IPacketHandler handler)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (packedOpcode)");
            sb.AppendLine("        {");

            var armsEmitted = 0;
            foreach (var packet in packets)
            {
                if (!handleOverloadFqns.Contains(packet.FullyQualifiedName))
                    continue;

                armsEmitted++;
                sb.AppendLine($"            case 0x{packet.PackedOpcode:X}:");
                sb.AppendLine($"                if (payload.Length < {packet.FullyQualifiedName}.{packet.SizeConst})");
                sb.AppendLine("                {");
                sb.AppendLine("                    throw new System.ArgumentOutOfRangeException(");
                sb.AppendLine("                        nameof(payload), payload.Length,");
                sb.AppendLine(
                    $"                        $\"Payload too small for {packet.Name}: need {{{packet.FullyQualifiedName}.{packet.SizeConst}}} bytes.\");");
                sb.AppendLine("                }");
                sb.AppendLine(
                    $"                handler.Handle(in MemoryMarshal.AsRef<{packet.FullyQualifiedName}>(payload));");
                sb.AppendLine("                return true;");
                sb.AppendLine();
            }

            sb.AppendLine("            default:");
            sb.AppendLine("                handler.OnUnhandled(packedOpcode, payload);");
            sb.AppendLine("                return false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            spc.AddSource("PacketRouter.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }

    private static void CollectTaggedStructs(
        INamespaceSymbol ns,
        INamedTypeSymbol? opcodeAttrSymbol,
        List<PacketInfo> packets,
        HashSet<uint> seenOpcodes,
        Compilation compilation)
    {
        foreach (var type in ns.GetTypeMembers())
            InspectType(type, opcodeAttrSymbol, packets, seenOpcodes, compilation);

        foreach (var child in ns.GetNamespaceMembers())
            CollectTaggedStructs(child, opcodeAttrSymbol, packets, seenOpcodes, compilation);
    }

    private static void InspectType(
        INamedTypeSymbol type,
        INamedTypeSymbol? opcodeAttrSymbol,
        List<PacketInfo> packets,
        HashSet<uint> seenOpcodes,
        Compilation compilation)
    {
        if (type.TypeKind != TypeKind.Struct)
            return;

        AttributeData? attr = null;
        foreach (var a in type.GetAttributes())
        {
            var attrClass = a.AttributeClass;
            if (attrClass is null)
                continue;

            var matched = opcodeAttrSymbol is not null
                ? SymbolEqualityComparer.Default.Equals(attrClass, opcodeAttrSymbol)
                : attrClass.Name == "PacketOpcodeAttribute" || attrClass.Name == "PacketOpcode";

            if (matched)
            {
                attr = a;
                break;
            }
        }

        if (attr is null || attr.ConstructorArguments.Length < 2)
            return;

        var majorObj = attr.ConstructorArguments[0].Value;
        var minorObj = attr.ConstructorArguments[1].Value;
        if (majorObj is null || minorObj is null)
            return;

        var major = Convert.ToUInt16(majorObj);
        var minor = Convert.ToUInt16(minorObj);
        var packed = ((uint)major << 16) | minor;

        if (!seenOpcodes.Add(packed))
            return;

        var fqn = type.ToDisplayString(
            new SymbolDisplayFormat(
                SymbolDisplayGlobalNamespaceStyle.Omitted,
                SymbolDisplayTypeQualificationStyle
                    .NameAndContainingTypesAndNamespaces));
        var name = type.Name;

        string? sizeConst = null;
        foreach (var member in type.GetMembers())
            if (member is IFieldSymbol field && field.IsConst)
            {
                if (field.Name == "WireSize")
                {
                    sizeConst = "WireSize";
                    break;
                }

                if (field.Name == "HeaderSize" && sizeConst is null)
                    sizeConst = "HeaderSize";
                else if (field.Name == "Size" && sizeConst is null)
                    sizeConst = "Size";
            }

        if (sizeConst is null)
            return;

        packets.Add(new PacketInfo(packed, major, minor, fqn, name, sizeConst));
    }

    private sealed class PacketInfo
    {
        public readonly string FullyQualifiedName;
        public readonly ushort Major;
        public readonly ushort Minor;
        public readonly string Name;
        public readonly uint PackedOpcode;
        public readonly string SizeConst;

        public PacketInfo(uint packed, ushort major, ushort minor,
            string fqn, string name, string sizeConst)
        {
            PackedOpcode = packed;
            Major = major;
            Minor = minor;
            FullyQualifiedName = fqn;
            Name = name;
            SizeConst = sizeConst;
        }
    }
}