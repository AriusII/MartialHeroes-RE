using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgActionToggle
{
    public const uint OpcodeId = 0x20063;

    public const int Size = 3;

    public byte ActionCode;
    public byte Category;
    public byte LocalFlag;
}
