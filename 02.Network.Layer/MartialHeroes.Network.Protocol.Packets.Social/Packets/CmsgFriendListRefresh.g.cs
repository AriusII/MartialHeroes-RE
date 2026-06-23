using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgFriendListRefresh
{
    public const uint OpcodeId = 0x20036;

    public const int Size = 1;

    public byte Flag;
}
