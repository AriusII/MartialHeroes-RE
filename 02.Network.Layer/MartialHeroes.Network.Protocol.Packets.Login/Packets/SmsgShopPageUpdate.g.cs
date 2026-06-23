using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets;

[PacketOpcode(3, 8)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SmsgShopPageUpdate
{
    public const uint OpcodeId = 0x30008;

    public const int Size = 4;

    public uint Money;
}
