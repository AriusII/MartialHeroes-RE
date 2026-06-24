using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 121)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCharPropertySet
{
    public const uint OpcodeId = Opcodes.SmsgCharPropertySet;

    public const int WireSize = 8;

    public readonly uint ActorId;

    public readonly uint PropertyValue;
}
