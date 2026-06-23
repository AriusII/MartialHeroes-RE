using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgNpcSearchById
{
    public const uint OpcodeId = 0x20086;

    public const int Size = 24;

    public uint SearchId;
    public NameFilterBuffer NameFilter;
    public PaddingBuffer Padding;

    [InlineArray(17)]
    public struct NameFilterBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }

}
