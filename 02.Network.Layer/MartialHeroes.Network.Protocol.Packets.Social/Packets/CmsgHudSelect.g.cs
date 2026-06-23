using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgHudSelect
{
    public const uint OpcodeId = 0x20029;

    public const int Size = 12;

    public uint KeyA;
    public uint KeyB;
    public uint KeyC;
}
