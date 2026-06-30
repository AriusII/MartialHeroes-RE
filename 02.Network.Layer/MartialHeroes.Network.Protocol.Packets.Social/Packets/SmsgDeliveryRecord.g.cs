using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgDeliveryRecord
{
    public const uint OpcodeId = 0x40046;

    public const int Size = 132;

    public byte ResultCode;
    public byte Pad1;
    public byte SubAction;
    public SenderNameBuffer SenderName;
    public long Money;
    public Pad20Buffer Pad20;
    public AttachedItemsBuffer AttachedItems;
    public int EntryKey;

    [InlineArray(17)]
    public struct SenderNameBuffer
    {
        private byte _element0;
    }

    [InlineArray(20)]
    public struct Pad20Buffer
    {
        private byte _element0;
    }

    [InlineArray(80)]
    public struct AttachedItemsBuffer
    {
        private byte _element0;
    }
}
