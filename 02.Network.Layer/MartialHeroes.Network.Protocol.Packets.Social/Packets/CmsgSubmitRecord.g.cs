using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgSubmitRecord
{
    public const uint OpcodeId = 0x20028;

    public const int Size = 72;

    public RecordBodyBuffer RecordBody;
    public byte SelectorIndex;
    public TailBuffer Tail;

    [InlineArray(68)]
    public struct RecordBodyBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct TailBuffer
    {
        private byte _element0;
    }

}
