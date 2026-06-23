using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgHudCodeValue
{
    public const uint OpcodeId = 0x2002f;

    public const int Size = 8;

    public byte Code;
    public Pad0Buffer Pad0;
    public uint Value;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
