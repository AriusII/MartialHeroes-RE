using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 26)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgLocalPlayerRelationSlot
{
    public const uint OpcodeId = Opcodes.SmsgLocalPlayerRelationSlot;

    public const int WireSize = 28;

    public readonly int Sort;

    public readonly int ActorId;

    public readonly byte SlotIndex;

    public readonly Pad9Buffer Pad9;

    public readonly int Field0;

    public readonly int Field1;

    public readonly int Field2;

    public readonly int Field3;

    [InlineArray(3)]
    public struct Pad9Buffer
    {
        private byte _element0;
    }
}