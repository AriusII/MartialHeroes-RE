using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 79)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCraftingResult
{
    public const uint OpcodeId = Opcodes.SmsgCraftingResult;

    public const int WireSize = 52;

    public readonly LeadingBuffer Leading;

    public readonly byte SuccessFlag;

    public readonly byte ErrorCode;

    public readonly byte ResultSubtype;

    public readonly byte Pad0;

    public readonly uint ResultValueA;

    public readonly uint ResultValueB;

    public readonly uint ResultValueC;

    public readonly GapBuffer Gap0;

    public readonly byte ProducedSlot;

    public readonly Pad1Buffer Pad1;

    public readonly uint ProducedItem0;

    public readonly uint ProducedItem1;

    public readonly uint ProducedItem2;

    public readonly uint ProducedItem3;

    [InlineArray(8)]
    public struct LeadingBuffer
    {
        private byte _element0;
    }

    [InlineArray(8)]
    public struct GapBuffer
    {
        private byte _element0;
    }

    [InlineArray(3)]
    public struct Pad1Buffer
    {
        private byte _element0;
    }
}
