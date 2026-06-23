using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgCarrierPigeonSend
{
    public const uint OpcodeId = 0x20046;

    public const int Size = 132;

    public byte SendMode;
    public RecipientBuffer Recipient;
    public Pad12Buffer Pad12;
    public uint MoneyLow;
    public uint MoneyHigh;
    public ItemHandlesBuffer ItemHandles;
    public MessageBodyBuffer MessageBody;

    [InlineArray(17)]
    public struct RecipientBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad12Buffer
    {
        private byte _element0;
    }

    [InlineArray(20)]
    public struct ItemHandlesBuffer
    {
        private byte _element0;
    }

    [InlineArray(84)]
    public struct MessageBodyBuffer
    {
        private byte _element0;
    }

}
