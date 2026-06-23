namespace MartialHeroes.Network.Protocol.Core.Opcodes;

[AttributeUsage(AttributeTargets.Struct)]
public sealed class PacketOpcodeAttribute(ushort major, ushort minor) : Attribute
{
    public ushort Major { get; } = major;

    public ushort Minor { get; } = minor;

    public PacketOpcode Opcode => new(Major, Minor);
}