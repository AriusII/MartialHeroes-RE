using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgStorageOp
{
    public const uint OpcodeId = 0x2008e;

    public const int Size = 16;

    public int Target;
    public byte Op;
    public Pad0Buffer Pad0;
    public long Amount;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
