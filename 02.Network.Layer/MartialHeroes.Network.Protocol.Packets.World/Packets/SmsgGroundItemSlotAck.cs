using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 14)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGroundItemSlotAck
{
    public const uint OpcodeId = Opcodes.SmsgGroundItemSlotAck;

    public const int WireSize = 20;

    public readonly HeaderBuffer Header;

    public readonly byte Result;

    public readonly Pad0Buffer Pad0;

    public readonly byte Mode;

    public readonly byte Slot;

    public readonly int Count;

    public readonly int Opaque;

    [InlineArray(8)]
    public struct HeaderBuffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}