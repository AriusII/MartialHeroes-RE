using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgLetterRequest
{
    public const uint OpcodeId = 0x2003c;

    public const int Size = 8;

    public uint LetterId;
    public uint Mode;
}
