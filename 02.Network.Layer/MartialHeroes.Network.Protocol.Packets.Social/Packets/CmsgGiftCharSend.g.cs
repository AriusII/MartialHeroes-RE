using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGiftCharSend
{
    public const uint OpcodeId = 0x2007a;

    public const int Size = 12;

    public uint TargetId;
    public byte GroupByte;
    public CodeBuffer Code;
    public byte CodeNul;
    public Pad0ABuffer Pad0A;

    [InlineArray(4)]
    public struct CodeBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad0ABuffer
    {
        private byte _element0;
    }

}
