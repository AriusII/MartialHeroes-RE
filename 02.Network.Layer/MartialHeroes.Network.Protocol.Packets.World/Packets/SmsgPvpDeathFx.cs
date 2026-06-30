using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 80)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPvpDeathFx
{
    public const uint OpcodeId = Opcodes.SmsgPvpDeathFx;

    public const int WireSize = 16;

    public readonly uint LeadingDword;

    public readonly uint ActorKey;

    public readonly byte Gate;

    public readonly byte Pad1;

    public readonly byte Mode;

    public readonly byte Pad2;

    public readonly uint OpponentKey;
}
