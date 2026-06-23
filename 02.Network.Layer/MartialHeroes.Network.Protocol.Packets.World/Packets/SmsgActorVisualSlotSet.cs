
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 12)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorVisualSlotSet
{
    public const uint OpcodeId = Opcodes.SmsgActorVisualSlotSet;

    public const int WireSize = 20;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly uint ItemId;

    public readonly uint ItemUpgrade;

    public readonly byte SlotIndex;

    public readonly PaddingBuffer Padding;

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }
}