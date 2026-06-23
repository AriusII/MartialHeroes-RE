using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgProductBuy
{
    public const uint OpcodeId = 0x20097;

    public const int Size = 1;

    public byte Selector;
}
