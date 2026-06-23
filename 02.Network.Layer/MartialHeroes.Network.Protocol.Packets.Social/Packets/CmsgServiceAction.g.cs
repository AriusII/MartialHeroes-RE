using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgServiceAction
{
    public const uint OpcodeId = 0x20011;

    public const int Size = 8;

    public byte B0;
    public byte Mode;
    public Pad0Buffer Pad0;
    public uint Arg;

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
