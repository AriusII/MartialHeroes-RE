using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 57)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgTrackedPanelSlotUpdate
{
    public const uint OpcodeId = Opcodes.SmsgTrackedPanelSlotUpdate;

    public const int WireSize = 24;

    public readonly HeaderBuffer Header;

    public readonly byte Op;

    public readonly byte SlotIndex;

    public readonly byte ByteA;

    public readonly byte ByteB;

    public readonly short Value16;

    public readonly Pad0Buffer Pad0;

    public readonly int Value32A;

    public readonly uint Value32B;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}
