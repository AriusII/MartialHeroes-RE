using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 93)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorMobIdChange
{
    public const uint OpcodeId = Opcodes.SmsgActorMobIdChange;

    public const int WireSize = 16;

    public readonly uint Sort;

    public readonly uint ActorId;

    public readonly uint OldMobId;

    public readonly uint NewMobId;
}