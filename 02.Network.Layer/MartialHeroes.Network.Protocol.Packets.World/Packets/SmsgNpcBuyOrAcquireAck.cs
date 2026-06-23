
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 19)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgNpcBuyOrAcquireAck
{
    public const uint OpcodeId = Opcodes.SmsgNpcBuyOrAcquireAck;

    public const int WireSize = 56;

    public readonly HeaderBuffer Header;

    public readonly int ActorId;

    public readonly int GoldLo;

    public readonly int GoldHi;

    public readonly byte Result;

    public readonly byte ReasonCode;

    public readonly byte BagSlotIndex;

    public readonly Gap13Buffer Gap13;

    public readonly uint RepairVal1;

    public readonly uint RepairVal2;

    public readonly int ItemQuadA;

    public readonly int ItemQuadB;

    public readonly int ItemQuadC;

    public readonly int ItemQuadD;

    [InlineArray(4)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(13)]
    public struct Gap13Buffer
    {
        private byte _element0;
    }
}