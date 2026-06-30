using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 100)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCubeGambleReelUpdate
{
    public const uint OpcodeId = Opcodes.SmsgCubeGambleReelUpdate;

    public const int WireSize = 188;

    public readonly HeadOpaqueBuffer HeadOpaque;

    public readonly byte Phase;

    public readonly byte SpinSubKind;

    public readonly byte ReelDigitPack;

    private readonly byte _pad0B;

    public readonly uint ThrowValue;

    public readonly ulong SettledMoney;

    public readonly ReelHistoryBuffer ReelHistoryPack;

    public readonly ReelIndexBuffer ReelIndexPack;

    private readonly PadAlignBuffer _pad22;

    public readonly BoardStateBuffer BoardState;

    [InlineArray(8)]
    public struct HeadOpaqueBuffer
    {
        private byte _element0;
    }

    [InlineArray(5)]
    public struct ReelHistoryBuffer
    {
        private byte _element0;
    }

    [InlineArray(5)]
    public struct ReelIndexBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct PadAlignBuffer
    {
        private byte _element0;
    }

    [InlineArray(152)]
    public struct BoardStateBuffer
    {
        private byte _element0;
    }
}
