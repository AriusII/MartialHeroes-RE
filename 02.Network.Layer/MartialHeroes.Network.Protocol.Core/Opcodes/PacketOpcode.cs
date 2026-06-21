// spec: Docs/RE/opcodes.md — "Opcode encoding in this catalog"
// Encoding: a (major:minor) tuple packed as a single 32-bit value `major << 16 | minor`.
// CAPTURE-UNVERIFIED layouts: routing (major:minor -> handler) is confirmed against the
// client's dispatch table, but every linked field layout is a static inference. See opcodes.md.

namespace MartialHeroes.Network.Protocol.Core.Opcodes;

/// <summary>
///     A wire opcode: the <c>(major:minor)</c> pair that selects a handler. There is no separate
///     opcode field on the wire — the frame header's <c>major</c>/<c>minor</c> words ARE the opcode.
/// </summary>
/// <remarks>
///     spec: Docs/RE/opcodes.md. The packed form is <c>(major &lt;&lt; 16) | minor</c>, matching the
///     catalog's <c>Opcode</c> hex column (e.g. 5/13 -&gt; <c>0x5000d</c>).
/// </remarks>
public readonly record struct PacketOpcode(ushort Major, ushort Minor)
{
    /// <summary>The packed 32-bit form: <c>(Major &lt;&lt; 16) | Minor</c>. spec: Docs/RE/opcodes.md.</summary>
    public uint Packed => ((uint)Major << 16) | Minor;

    /// <summary>Reconstructs a <see cref="PacketOpcode" /> from its packed 32-bit form.</summary>
    public static PacketOpcode FromPacked(uint packed)
    {
        return new PacketOpcode((ushort)(packed >> 16), (ushort)(packed & 0xFFFF));
    }

    public override string ToString()
    {
        return $"{Major}:{Minor} (0x{Packed:x})";
    }
}