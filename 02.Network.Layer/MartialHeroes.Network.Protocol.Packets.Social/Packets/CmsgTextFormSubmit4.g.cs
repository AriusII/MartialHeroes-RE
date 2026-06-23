using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgTextFormSubmit4
{
    public const uint OpcodeId = 0x20067;

    public const int Size = 196;

    public Line0Buffer Line0;
    public Line1Buffer Line1;
    public Line2Buffer Line2;
    public Line3Buffer Line3;

    [InlineArray(49)]
    public struct Line0Buffer
    {
        private byte _element0;
    }

    [InlineArray(49)]
    public struct Line1Buffer
    {
        private byte _element0;
    }

    [InlineArray(49)]
    public struct Line2Buffer
    {
        private byte _element0;
    }

    [InlineArray(49)]
    public struct Line3Buffer
    {
        private byte _element0;
    }

}
