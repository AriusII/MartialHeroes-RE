using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 44)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgActorTickTableOpA
{
    public const uint OpcodeId = Opcodes.SmsgActorTickTableOpA;

    public const int WireSize = 16;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly byte Op;

    public readonly byte Padding;

    public readonly byte SubOp;

    public readonly Padding2Buffer Padding2;

    [InlineArray(5)]
    public struct Padding2Buffer
    {
        private byte _element0;
    }
}
