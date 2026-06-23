using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgFriendAddRemove
{
    public const uint OpcodeId = 0x20031;

    public const int Size = 19;

    public byte Tag;
    public byte Index;
    public NameBuffer Name;
    public Pad0Buffer Pad0;

    [InlineArray(16)]
    public struct NameBuffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

}
