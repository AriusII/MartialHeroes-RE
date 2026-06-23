using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgHudContextAction39
{
    public const uint OpcodeId = 0x20027;

    public const int Size = 12;

    public PrefixBuffer Prefix;
    public byte CodeA;
    public byte CodeB;
    public byte CodeC;
    public TailBuffer Tail;

    [InlineArray(6)]
    public struct PrefixBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct TailBuffer
    {
        private byte _element0;
    }

}
