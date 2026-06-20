// OpcodeCatalog — reflects the [PacketOpcode]-tagged wire structs across the Network.Protocol packet
// families into a (major:minor) → struct map. This is the SAME metadata the source-generated router
// consumes, so the catalogue can never drift from the actual wire structs (unlike a hand-kept list).
//
// Reflection is used deliberately: this is a build/diagnostic tool, not the zero-alloc hot path.

using System.Reflection;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Tools.PacketInspect;

/// <summary>One catalogued packet opcode and the wire struct that decodes it.</summary>
internal sealed record OpcodeEntry(
    ushort Major,
    ushort Minor,
    uint Packed,
    Type StructType,
    string Name,
    int? WireSize,
    string SizeKind, // "WireSize" | "HeaderSize" | "Size" | "—"
    string Direction); // "C2S" | "S2C" | "?"

internal static class OpcodeCatalog
{
    // Assembly simple-names == project names (no custom AssemblyName), so Assembly.Load by name is stable.
    private static readonly string[] PacketAssemblies =
    [
        "MartialHeroes.Network.Protocol.Core",
        "MartialHeroes.Network.Protocol.Packets.Login",
        "MartialHeroes.Network.Protocol.Packets.World",
        "MartialHeroes.Network.Protocol.Packets.Social"
    ];

    /// <summary>Every [PacketOpcode]-tagged struct, sorted by packed opcode (first wins on a clash).</summary>
    public static IReadOnlyList<OpcodeEntry> All { get; } = Build();

    /// <summary>Resolves the entry for a packed (major&lt;&lt;16|minor) opcode, or null if unknown.</summary>
    public static OpcodeEntry? Find(uint packed)
    {
        foreach (var e in All)
            if (e.Packed == packed)
                return e;
        return null;
    }

    private static IReadOnlyList<OpcodeEntry> Build()
    {
        var byPacked = new Dictionary<uint, OpcodeEntry>();

        foreach (var asmName in PacketAssemblies)
        {
            Assembly asm;
            try
            {
                asm = Assembly.Load(asmName);
            }
            catch
            {
                continue;
            } // a family that fails to load is simply skipped

            foreach (var t in SafeGetTypes(asm))
            {
                if (!t.IsValueType)
                    continue;

                var attr = t.GetCustomAttribute<PacketOpcodeAttribute>();
                if (attr is null)
                    continue;

                var packed = ((uint)attr.Major << 16) | attr.Minor;
                if (byPacked.ContainsKey(packed))
                    continue; // first struct for an opcode wins, matching the router's behaviour

                var (size, kind) = ReadSizeConst(t);
                var dir = t.Name.StartsWith("Cmsg", StringComparison.Ordinal) ? "C2S"
                    : t.Name.StartsWith("Smsg", StringComparison.Ordinal) ? "S2C"
                    : "?";

                byPacked[packed] = new OpcodeEntry(
                    attr.Major, attr.Minor, packed, t, t.Name, size, kind, dir);
            }
        }

        var list = new List<OpcodeEntry>(byPacked.Values);
        list.Sort(static (a, b) => a.Packed.CompareTo(b.Packed));
        return list;
    }

    // Wire-size const: WireSize (fixed) > HeaderSize (variable header) > Size (fallback).
    private static (int?, string) ReadSizeConst(Type t)
    {
        foreach (var name in (string[])["WireSize", "HeaderSize", "Size"])
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f is { IsLiteral: true } && f.GetRawConstantValue() is int v)
                return (v, name);
        }

        return (null, "—");
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static t => t is not null)!;
        }
    }
}