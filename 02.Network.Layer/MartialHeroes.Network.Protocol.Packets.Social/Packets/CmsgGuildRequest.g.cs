using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGuildRequest
{
    public const uint OpcodeId = 0x20050;

    public const int Size = 48;

    public byte Subcode;
    public Pad0Buffer Pad0;
    public uint ContextId;
    public AmountQBuffer AmountQ;
    public UnionBuffer Union;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct AmountQBuffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct UnionBuffer
    {
        private byte _element0;
    }

}
