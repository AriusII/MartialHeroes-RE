using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgPartyRequest
{
    public const uint OpcodeId = 0x2003d;

    public const int Size = 36;

    public byte Subcode;
    public byte SubFlag;
    public Reserved0Buffer Reserved0;
    public uint ContextId;
    public AmountQBuffer AmountQ;
    public NameBuffer Name;
    public TailBuffer Tail;

    [InlineArray(2)]
    public struct Reserved0Buffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct AmountQBuffer
    {
        private byte _element0;
    }

    [InlineArray(17)]
    public struct NameBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct TailBuffer
    {
        private byte _element0;
    }

}
