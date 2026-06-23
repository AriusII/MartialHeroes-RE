using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 22)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgItemSlotStateAck
{
    public const uint OpcodeId = Opcodes.SmsgItemSlotStateAck;

    public const int WireSize = 36;

    public readonly HeaderBuffer Header;

    public readonly byte Result;

    private readonly byte _pad9;

    public readonly byte FromSlot;

    public readonly byte ToSlot;

    public readonly uint FlagC;

    public readonly uint Flag10;

    private readonly uint _gap14;

    public readonly int BonusField1;

    public readonly int BonusField2;

    public readonly int BonusField3;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }
}