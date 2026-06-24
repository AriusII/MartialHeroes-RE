using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 71)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgResponseSlot71
{
    public const uint OpcodeId = Opcodes.SmsgResponseSlot71;

    public const int WireSize = 1092;

    public readonly HeaderPad0Buffer HeaderPad0;

    public readonly byte Subtype;

    public readonly ReservedABuffer ReservedA;

    public readonly IdArrayBuffer IdArray;

    public readonly StatusArrayBuffer StatusArray;

    public readonly ReservedGap1Buffer ReservedGap1;

    public readonly NameCellRegion1Buffer NameCellRegion1;

    public readonly ReservedGap2Buffer ReservedGap2;

    public readonly NameCellRegion2Buffer NameCellRegion2;

    public readonly PairArrayBuffer PairArray;

    public readonly SlotRecordArrayBuffer SlotRecordArray;

    [InlineArray(8)]
    public struct HeaderPad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct ReservedABuffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct IdArrayBuffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct StatusArrayBuffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct ReservedGap1Buffer
    {
        private byte _element0;
    }

    [InlineArray(136)]
    public struct NameCellRegion1Buffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct ReservedGap2Buffer
    {
        private byte _element0;
    }

    [InlineArray(136)]
    public struct NameCellRegion2Buffer
    {
        private byte _element0;
    }

    [InlineArray(64)]
    public struct PairArrayBuffer
    {
        private byte _element0;
    }

    [InlineArray(640)]
    public struct SlotRecordArrayBuffer
    {
        private byte _element0;
    }
}