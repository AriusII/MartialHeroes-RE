using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 16)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorVisualSlotClear
{
    public const uint OpcodeId = Opcodes.SmsgActorVisualSlotClear;

    public const int WireSize = 16;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly byte Mode;

    public readonly byte SlotIndex;

    public readonly PaddingBuffer Padding;

    [InlineArray(6)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }
}