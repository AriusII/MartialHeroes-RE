using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgHudFlagsTarget
{
    public const uint OpcodeId = 0x2002d;

    public const int Size = 8;

    public byte B0;
    public byte B1;
    public byte B2;
    public byte B3;
    public uint Value;
}
