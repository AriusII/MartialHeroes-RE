using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgDeliveryClaim
{
    public const uint OpcodeId = 0x20047;

    public const int Size = 4;

    public uint Selector;
}
