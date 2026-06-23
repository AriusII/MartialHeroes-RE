using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgPartyLeaveKick
{
    public const uint OpcodeId = 0x20024;

    public const int Size = 8;

    public byte Mode;
    public Pad0Buffer Pad0;
    public uint Id;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
