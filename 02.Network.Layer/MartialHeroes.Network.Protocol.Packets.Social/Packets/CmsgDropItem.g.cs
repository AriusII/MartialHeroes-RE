using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgDropItem
{
    public const uint OpcodeId = 0x2000e;

    public const int Size = 8;

    public byte Mode;
    public byte Slot;
    public Pad0Buffer Pad0;
    public uint Count;

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
