using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 31)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgBuffSlotUpdate
{
    public const uint OpcodeId = Opcodes.SmsgBuffSlotUpdate;

    public const int WireSize = 56;

    public readonly uint ActorKeyA;

    public readonly uint ActorKeyB;

    public readonly int SlotIndex;

    public readonly ushort EffectCode;

    public readonly Pad0Buffer Pad0;

    public readonly uint EffectValue;

    public readonly uint EffectExtra;

    public readonly TrailerBuffer Trailer;

    [InlineArray(2)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(32)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}
