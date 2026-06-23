using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgTradeSlotAdd
{
    public const uint OpcodeId = 0x20018;

    public const int Size = 20;

    public byte Category;
    public byte SlotIndex;
    public Pad0Buffer Pad0;
    public int ValueA;
    public int ValueB;
    public int ValueC;
    public byte FlagA;
    public byte FlagB;
    public byte FlagC;
    public Pad1Buffer Pad1;

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }

}
