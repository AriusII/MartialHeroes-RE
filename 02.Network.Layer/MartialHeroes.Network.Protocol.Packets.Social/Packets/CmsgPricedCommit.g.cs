using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgPricedCommit
{
    public const uint OpcodeId = 0x2003a;

    public const int Size = 24;

    public HeaderBuffer Header;
    public byte SlotIndex;
    public Pad0Buffer Pad0;
    public uint Amount;
    public RecordTailBuffer RecordTail;

    [InlineArray(4)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(12)]
    public struct RecordTailBuffer
    {
        private byte _element0;
    }

}
