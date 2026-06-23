using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgRelationNamedRequest
{
    public const uint OpcodeId = 0x2003e;

    public const int Size = 19;

    public NameBuffer Name;
    public ushort Type;
    public byte Flag;

    [InlineArray(16)]
    public struct NameBuffer
    {
        private byte _element0;
    }

}
