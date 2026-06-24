using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 11)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgRankXpGain
{
    public const uint OpcodeId = Opcodes.SmsgRankXpGain;

    public const int WireSize = 20;

    public readonly uint ActorSort;

    public readonly uint ActorId;

    public readonly ulong Amount;

    public readonly byte Mode;

    public readonly Pad0Buffer Pad0;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}