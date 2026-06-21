// spec: Docs/RE/opcodes.md — tags a wire struct with its (major:minor) opcode.

namespace MartialHeroes.Network.Protocol.Core.Opcodes;

/// <summary>
///     Tags a packet wire struct with the <c>(major:minor)</c> opcode it decodes. Consumed by the
///     (future source-generated) router to build a compile-time opcode-&gt;handler switch with no
///     reflection on the hot path. spec: Docs/RE/opcodes.md.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class PacketOpcodeAttribute(ushort major, ushort minor) : Attribute
{
    /// <summary>Opcode high part / message family. spec: Docs/RE/opcodes.md.</summary>
    public ushort Major { get; } = major;

    /// <summary>Opcode low part / message id. spec: Docs/RE/opcodes.md.</summary>
    public ushort Minor { get; } = minor;

    /// <summary>The packed <c>(major:minor)</c> opcode. spec: Docs/RE/opcodes.md.</summary>
    public PacketOpcode Opcode => new(Major, Minor);
}