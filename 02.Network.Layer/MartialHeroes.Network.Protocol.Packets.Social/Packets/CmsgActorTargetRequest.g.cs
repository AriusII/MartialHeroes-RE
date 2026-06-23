using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgActorTargetRequest
{
    public const uint OpcodeId = 0x2004f;

    public const int Size = 16;

    public byte Selector;
    public Reserved0Buffer Reserved0;
    public byte TableIndex;
    public RecordTailBuffer RecordTail;

    [InlineArray(7)]
    public struct Reserved0Buffer
    {
        private byte _element0;
    }

    [InlineArray(7)]
    public struct RecordTailBuffer
    {
        private byte _element0;
    }

}
