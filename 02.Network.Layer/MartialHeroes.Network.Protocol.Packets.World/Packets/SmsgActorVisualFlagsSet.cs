using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 124)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorVisualFlagsSet
{
    public const uint OpcodeId = Opcodes.SmsgActorVisualFlagsSet;

    public const int WireSize = 12;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly byte VisualFlags;

    public readonly PaddingBuffer Padding;

    [InlineArray(3)]
    public struct PaddingBuffer
    {
        private byte _element0;
    }
}