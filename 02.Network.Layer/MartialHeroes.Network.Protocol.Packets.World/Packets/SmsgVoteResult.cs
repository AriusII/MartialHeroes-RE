using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 94)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgVoteResult
{
    public const uint OpcodeId = Opcodes.SmsgVoteResult;

    public const int WireSize = 16;

    public readonly byte ActorSort;

    public readonly GapABuffer GapA;

    public readonly uint ActorId;

    public readonly byte VoteSide;

    public readonly byte ByteB;

    public readonly byte ByteC;

    public readonly Pad0Buffer Pad0;

    public readonly uint VoteResult;

    [InlineArray(3)]
    public struct GapABuffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}