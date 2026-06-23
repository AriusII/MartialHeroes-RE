using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgNpcOpenReq
{
    public const uint OpcodeId = 0x20064;

    public const int Size = 1;

    public byte Request;
}
