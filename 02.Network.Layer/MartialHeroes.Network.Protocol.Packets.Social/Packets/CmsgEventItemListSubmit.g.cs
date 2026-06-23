using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgEventItemListSubmit
{
    public const uint OpcodeId = 0x20078;

    public byte Flag;
    public uint Count;
    public uint RecordId;
    public byte RecordA;
    public byte RecordB;
    public RecordPadBuffer RecordPad;

    [InlineArray(6)]
    public struct RecordPadBuffer
    {
        private byte _element0;
    }

}
