using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgTradeConfirm
{
    public const uint OpcodeId = 0x20019;

    public byte Mode;
    public byte Flag;
}
