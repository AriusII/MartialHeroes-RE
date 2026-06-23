using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Network.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CmsgNpcSell
{
    public const uint OpcodeId = 0x20014;

    public const int Size = 12;

    public uint Id;
    public byte SlotMode;
    public byte SubSel;
    public ReservedBuffer Reserved;
    public uint Quantity;

    [InlineArray(2)]
    public struct ReservedBuffer
    {
        private byte _element0;
    }

}
