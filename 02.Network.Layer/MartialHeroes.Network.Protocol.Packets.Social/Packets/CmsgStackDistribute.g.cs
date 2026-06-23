using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgStackDistribute
{
    public const uint OpcodeId = 0x20039;

    public byte OpType;
    public byte SourceIndex;
    public byte DestSlot;
    public Pad0Buffer Pad0;
    public ushort Quantity;
    public uint ValueA;
    public uint ValueB;
    public byte TailCount;
    public Pad1Buffer Pad1;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }

}
