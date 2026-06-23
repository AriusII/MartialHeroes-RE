using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgRosterAssemble
{
    public const uint OpcodeId = 0x2004b;

    public const int Size = 80;

    public HeaderBuffer Header;
    public ActorIdsBuffer ActorIds;
    public TailBuffer Tail;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(40)]
    public struct ActorIdsBuffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct TailBuffer
    {
        private byte _element0;
    }

}
