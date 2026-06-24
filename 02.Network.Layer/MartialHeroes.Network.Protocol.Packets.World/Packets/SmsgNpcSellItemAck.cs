using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 20)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgNpcSellItemAck
{
    public const uint OpcodeId = Opcodes.SmsgNpcSellItemAck;

    public const int WireSize = 24;

    public readonly uint Word0;

    public readonly uint EntityKey;

    public readonly byte Result;

    public readonly byte Filler;

    public readonly byte SubFlag;

    public readonly TrailerBuffer Trailer;

    [InlineArray(13)]
    public struct TrailerBuffer
    {
        private byte _element0;
    }
}
