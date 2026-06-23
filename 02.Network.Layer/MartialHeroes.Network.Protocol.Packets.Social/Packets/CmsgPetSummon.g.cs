using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgPetSummon
{
    public const uint OpcodeId = 0x20099;

    public const int Size = 4;

    public byte Mode;
    public byte Param1;
    public byte SlotFlag;
    public byte NpcSlot;
}
