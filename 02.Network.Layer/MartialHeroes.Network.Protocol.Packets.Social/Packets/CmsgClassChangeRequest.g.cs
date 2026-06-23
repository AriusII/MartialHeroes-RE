using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgClassChangeRequest
{
    public const uint OpcodeId = 0x20040;

    public const int Size = 8;

    public uint ClassSelector;
    public uint Param;
}
