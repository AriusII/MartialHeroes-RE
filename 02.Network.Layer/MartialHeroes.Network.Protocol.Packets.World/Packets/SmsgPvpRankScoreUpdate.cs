using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 131)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPvpRankScoreUpdate
{
    public const uint OpcodeId = Opcodes.SmsgPvpRankScoreUpdate;

    public const int WireSize = 8;

    public readonly uint ScoreA;

    public readonly uint ScoreB;
}
