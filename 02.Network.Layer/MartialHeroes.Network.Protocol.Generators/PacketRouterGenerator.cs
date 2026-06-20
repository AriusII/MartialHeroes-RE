// spec: Docs/RE/opcodes.md — compile-time source generator for the PacketRouter switch.
//
// Scans structs carrying [PacketOpcode(major, minor)] in the compilation, then emits a
// partial PacketRouter with a generated routing switch. Each arm:
//   (a) validates payload.Length >= <T>.WireSize (or HeaderSize/Size for variable-length packets),
//   (b) reinterprets the payload in place via MemoryMarshal.AsRef<T>,
//   (c) calls handler.Handle(in view) ONLY when IPacketHandler has a matching Handle(in T) overload.
//
// If no Handle(in T) overload exists for a tagged struct, the arm is NOT emitted — it falls through
// to the default arm (OnUnhandled), preserving the current runtime behaviour.
//
// Reflection-free generated code: no Activator, no Dictionary, no LINQ in the emitted switch.

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

#nullable enable

namespace MartialHeroes.Network.Protocol.Generators;

[Generator]
public sealed class PacketRouterGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect all struct declarations that have at least one attribute.
        IncrementalValuesProvider<StructDeclarationSyntax> structs = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is StructDeclarationSyntax s
                                               && s.AttributeLists.Count > 0,
                transform: static (ctx, _) => (StructDeclarationSyntax)ctx.Node)
            .Where(static s => s is not null);

        // Combine the compilation with the collected struct nodes so we can do semantic analysis.
        IncrementalValueProvider<(Compilation Compilation, ImmutableArray<StructDeclarationSyntax> Structs)> combined =
            context.CompilationProvider.Combine(structs.Collect());

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            Compilation compilation = pair.Compilation;
            ImmutableArray<StructDeclarationSyntax> structNodes = pair.Structs;

            // Build the list of [PacketOpcode]-tagged packet structs.
            var packets = new System.Collections.Generic.List<PacketInfo>();
            var seenOpcodes = new System.Collections.Generic.HashSet<uint>();

            foreach (StructDeclarationSyntax structDecl in structNodes)
            {
                SemanticModel model = compilation.GetSemanticModel(structDecl.SyntaxTree);
                if (model.GetDeclaredSymbol(structDecl) is not INamedTypeSymbol typeSymbol)
                    continue;

                // Find [PacketOpcode(major, minor)] by attribute simple name.
                AttributeData? attr = null;
                foreach (AttributeData a in typeSymbol.GetAttributes())
                {
                    string? attrName = a.AttributeClass?.Name;
                    if (attrName == "PacketOpcodeAttribute" || attrName == "PacketOpcode")
                    {
                        attr = a;
                        break;
                    }
                }

                if (attr is null || attr.ConstructorArguments.Length < 2)
                    continue;

                object? majorObj = attr.ConstructorArguments[0].Value;
                object? minorObj = attr.ConstructorArguments[1].Value;
                if (majorObj is null || minorObj is null)
                    continue;

                ushort major = System.Convert.ToUInt16(majorObj);
                ushort minor = System.Convert.ToUInt16(minorObj);
                uint packed = ((uint)major << 16) | minor;

                // Skip duplicate opcodes — first struct definition wins.
                if (!seenOpcodes.Add(packed))
                    continue;

                // Use the minimal (non-global-prefixed) fully qualified name for generated code.
                // SymbolDisplayFormat.FullyQualifiedFormat produces "global::Ns.Name" which doesn't
                // compile in files that lack a top-level global:: alias resolution. We emit the
                // namespace-qualified name without the global:: prefix instead.
                string fqn = typeSymbol.ToDisplayString(
                    new SymbolDisplayFormat(
                        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle
                            .NameAndContainingTypesAndNamespaces));
                string name = typeSymbol.Name;

                // Detect the size constant: WireSize (fixed) > HeaderSize (var header) > Size (fallback).
                string? sizeConst = null;
                foreach (ISymbol member in typeSymbol.GetMembers())
                {
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
                }

                if (sizeConst is null)
                    continue; // cannot emit a safe length check

                packets.Add(new PacketInfo(packed, major, minor, fqn, name, sizeConst));
            }

            // Resolve IPacketHandler to know which Handle(in T) overloads exist.
            INamedTypeSymbol? handlerInterface = compilation.GetTypeByMetadataName(
                "MartialHeroes.Network.Protocol.Routing.IPacketHandler");

            var handleOverloadFqns = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.Ordinal);

            if (handlerInterface is not null)
            {
                foreach (ISymbol member in handlerInterface.GetMembers())
                {
                    if (member is IMethodSymbol method && method.Name == "Handle"
                                                       && method.Parameters.Length == 1)
                    {
                        IParameterSymbol param = method.Parameters[0];
                        if (param.RefKind == RefKind.In)
                        {
                            // Must match the FQN format used when scanning tagged structs.
                            string typeFqn = param.Type.ToDisplayString(
                                new SymbolDisplayFormat(
                                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle
                                        .NameAndContainingTypesAndNamespaces));
                            handleOverloadFqns.Add(typeFqn);
                        }
                    }
                }
            }

            // Sort by packed opcode for deterministic output.
            packets.Sort(static (a, b) => a.PackedOpcode.CompareTo(b.PackedOpcode));

            // Emit the generated source.
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//   Generated by PacketRouterGenerator.");
            sb.AppendLine("//   spec: Docs/RE/opcodes.md — compile-time (major:minor) -> handler dispatch.");
            sb.AppendLine("//   DO NOT EDIT by hand — this file is regenerated on every build.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System.Runtime.InteropServices;");
            sb.AppendLine("using MartialHeroes.Network.Protocol.Packets;");
            sb.AppendLine();
            sb.AppendLine("namespace MartialHeroes.Network.Protocol.Routing;");
            sb.AppendLine();
            sb.AppendLine("public static partial class PacketRouter");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Source-generated dispatch switch over all [PacketOpcode]-tagged structs.");
            sb.AppendLine("    /// Arms are emitted only for structs with a Handle(in T) overload in IPacketHandler.");
            sb.AppendLine("    /// All other opcodes reach OnUnhandled via the default arm. spec: Docs/RE/opcodes.md.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine(
                "    private static bool RouteGenerated(uint packedOpcode, System.ReadOnlySpan<byte> payload, IPacketHandler handler)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (packedOpcode)");
            sb.AppendLine("        {");

            int armsEmitted = 0;
            foreach (PacketInfo packet in packets)
            {
                // Only emit a typed arm when IPacketHandler.Handle(in T) exists for this struct.
                if (!handleOverloadFqns.Contains(packet.FullyQualifiedName))
                    continue;

                armsEmitted++;
                sb.AppendLine($"            // {packet.Major}/{packet.Minor} {packet.Name} — spec: Docs/RE/opcodes.md");
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

            // Default arm — routes unknown / untyped opcodes to OnUnhandled.
            sb.AppendLine("            default:");
            sb.AppendLine("                handler.OnUnhandled(packedOpcode, payload);");
            sb.AppendLine("                return false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            spc.AddSource("PacketRouter.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }

    private sealed class PacketInfo
    {
        public readonly uint PackedOpcode;
        public readonly ushort Major;
        public readonly ushort Minor;
        public readonly string FullyQualifiedName;
        public readonly string Name;
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