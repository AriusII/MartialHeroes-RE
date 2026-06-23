using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgNpcBuyOrAcquire
{
    public const uint OpcodeId = 0x20013;

    public const int Size = 12;

    public uint Id;
    public byte Slot;
    public byte SubSel;
    public byte FlagA;
    public byte FlagB;
    public uint Quantity;
}
