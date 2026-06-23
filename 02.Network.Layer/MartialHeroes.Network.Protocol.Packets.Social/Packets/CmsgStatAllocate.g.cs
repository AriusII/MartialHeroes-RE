using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgStatAllocate
{
    public const uint OpcodeId = 0x2001d;

    public const int Size = 20;

    public uint Str;
    public uint Int;
    public uint Agi;
    public uint Dex;
    public uint Con;
}
