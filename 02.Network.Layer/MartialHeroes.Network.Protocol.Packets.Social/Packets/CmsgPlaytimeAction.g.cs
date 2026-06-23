using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgPlaytimeAction
{
    public const uint OpcodeId = 0x2008f;

    public const int Size = 4;

    public byte Mode;
    public byte SlotOrFlag;
    public byte Param1;
    public byte Param2;
}
