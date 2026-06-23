using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgHudRecord16
{
    public const uint OpcodeId = 0x20032;

    public const int Size = 16;

    public RecordBuffer Record;

    [InlineArray(16)]
    public struct RecordBuffer
    {
        private byte _element0;
    }

}
