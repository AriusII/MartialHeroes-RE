using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgProductConfirm
{
    public const uint OpcodeId = 0x20099;

    public const int Size = 4;

    public byte SlotA;
    public byte SlotB;
    public byte ListSlot;
    public byte ProductionNpcIndex;
}
