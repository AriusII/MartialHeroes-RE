using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;
using MartialHeroes.Network.Protocol.Core.Packets;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 32)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgLevelUp
{
    public const uint OpcodeId = Opcodes.SmsgLevelUp;

    public const int WireSize = 48;

    public readonly byte Sort;

    private readonly byte _pad0_0;

    private readonly byte _pad0_1;
    private readonly byte _pad0_2;

    public readonly uint ActorId;

    public readonly ushort NewLevel;

    private readonly byte _pad1_0;

    private readonly byte _pad1_1;

    public readonly int RemainingStatPoints;

    public readonly int Value;

    public readonly long HpMpPacked;

    public readonly int Stamina;

    public readonly Tail20Buffer Tail20;

    public readonly long RankXpWithin;

    public readonly RankXpTailHiBuffer RankXpTailHi;

    public ActorSort SortKind => (ActorSort)Sort;

    [InlineArray(4)]
    public struct Tail20Buffer
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct RankXpTailHiBuffer
    {
        private byte _element0;
    }
}