using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgKeepalive
{
    public const uint OpcodeId = 0x22710;

    public const int Size = 4;

    public uint Padding;
}
