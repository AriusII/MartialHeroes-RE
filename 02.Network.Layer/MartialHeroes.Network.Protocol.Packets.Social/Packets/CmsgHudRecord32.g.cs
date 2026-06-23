using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgHudRecord32
{
    public const uint OpcodeId = 0x20037;

    public const int Size = 32;

    public RecordBuffer Record;

    [InlineArray(32)]
    public struct RecordBuffer
    {
        private byte _element0;
    }

}
