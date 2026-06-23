using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGiftPresent
{
    public const uint OpcodeId = 0x20052;

    public const int Size = 28;

    public uint GiftIdOrProxy;
    public uint ParamFromUI;
    public FriendNameBuffer FriendName;
    public PaddingBuffer Padding;

    [InlineArray(17)]
    public struct FriendNameBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }

}
