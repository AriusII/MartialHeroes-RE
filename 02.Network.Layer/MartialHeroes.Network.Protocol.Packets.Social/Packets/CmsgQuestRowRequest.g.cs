using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgQuestRowRequest
{
    public const uint OpcodeId = 0x20098;

    public const int Size = 8;

    public uint Arg1;
    public uint Arg2;
}
