using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgGatedSelfAction114
{
    public const uint OpcodeId = 0x20072;

    public const int Size = 4;

    public uint PlayerField;
}
