using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgListSelect
{
    public const uint OpcodeId = 0x20015;

    public const int Size = 2;

    public byte Mode;
    public byte Index;
}
