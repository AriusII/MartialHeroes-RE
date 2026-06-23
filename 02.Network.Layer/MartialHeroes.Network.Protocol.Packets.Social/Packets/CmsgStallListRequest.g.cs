using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgStallListRequest
{
    public const uint OpcodeId = 0x2004a;

    public const int Size = 32;

    public byte Flag;
    public NameBuffer Name;
    public byte Tail;

    [InlineArray(30)]
    public struct NameBuffer
    {
        private byte _element0;
    }

}
