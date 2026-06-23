using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGuildOp
{
    public const uint OpcodeId = 0x2001e;

    public const int Size = 8;

    public uint Op;
    public uint Id;
}
