using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgServiceListAction
{
    public const uint OpcodeId = 0x20016;

    public const int Size = 8;

    public uint Mode;
    public byte ActorIndex;
    public TailBuffer Tail;

    [InlineArray(3)]
    public struct TailBuffer
    {
        private byte _element0;
    }

}
