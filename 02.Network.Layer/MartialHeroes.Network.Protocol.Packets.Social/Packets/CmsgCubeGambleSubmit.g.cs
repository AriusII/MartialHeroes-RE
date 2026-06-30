using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgCubeGambleSubmit
{
    public const uint OpcodeId = 0x2008d;

    public const int Size = 76;

    public BetAmountsBuffer BetAmounts;
    public BetLinesBuffer BetLines;
    public byte DealerTableIndex;
    public PadBuffer Pad;

    [InlineArray(56)]
    public struct BetAmountsBuffer
    {
        private byte _element0;
    }

    [InlineArray(16)]
    public struct BetLinesBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct PadBuffer
    {
        private byte _element0;
    }
}
