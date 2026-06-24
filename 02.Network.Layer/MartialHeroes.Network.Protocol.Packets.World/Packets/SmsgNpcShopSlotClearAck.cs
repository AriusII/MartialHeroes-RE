using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 21)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgNpcShopSlotClearAck
{
    public const uint OpcodeId = Opcodes.SmsgNpcShopSlotClearAck;

    public const int WireSize = 12;

    public readonly SlotRecordBuffer SlotRecord;

    public readonly int Result;

    [InlineArray(8)]
    public struct SlotRecordBuffer
    {
        private byte _element0;
    }
}