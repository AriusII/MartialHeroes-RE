using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 24)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgUserTradeSlotUpdate
{
    public const uint OpcodeId = Opcodes.SmsgUserTradeSlotUpdate;

    public const int WireSize = 44;

    public readonly HeadBuffer Head;

    public readonly byte Result;

    public readonly byte Subtype;

    public readonly byte Category;

    public readonly byte SlotFlag;

    public readonly uint ItemId;

    public readonly uint ItemField2;

    public readonly uint ItemField3;

    public readonly uint ItemField4;

    public readonly Pad1Buffer Pad1;

    public readonly byte SlotIndex;

    public readonly byte SlotFlag2;

    public readonly byte SlotFlag3;

    public readonly Pad2Buffer Pad2;

    public readonly uint OwnerId;

    [InlineArray(8)]
    public struct HeadBuffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad2Buffer
    {
        private byte _element0;
    }
}
