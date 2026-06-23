using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgShopBuy
{
    public const uint OpcodeId = 0x20073;

    public const int Size = 8;

    public uint NpcId;
    public uint RowX2;
}
