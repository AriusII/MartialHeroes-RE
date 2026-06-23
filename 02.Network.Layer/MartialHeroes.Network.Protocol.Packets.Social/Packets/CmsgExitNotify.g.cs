using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgExitNotify
{
    public const uint OpcodeId = 0x20089;

    public const int Size = 16;

    public byte Mode;
    public byte Flag;
    public ushort Reserved;
    public float CoordX;
    public float CoordY;
    public byte Extra;
    public PaddingBuffer Padding;

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }

}
