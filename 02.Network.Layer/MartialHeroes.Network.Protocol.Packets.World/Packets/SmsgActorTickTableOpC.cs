using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 46)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorTickTableOpC
{
    public const uint OpcodeId = Opcodes.SmsgActorTickTableOpC;

    public const int WireSize = 16;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly byte Op;

    public readonly PaddingBuffer Padding;

    [InlineArray(7)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }
}