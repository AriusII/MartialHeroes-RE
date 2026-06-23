using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgItemMove
{
    public const uint OpcodeId = 0x2002e;

    public const int Size = 12;

    public byte B0;
    public byte B1;
    public byte B2;
    public byte B3;
    public byte B4;
    public byte B5;
    public Pad0Buffer Pad0;
    public uint Context;

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
