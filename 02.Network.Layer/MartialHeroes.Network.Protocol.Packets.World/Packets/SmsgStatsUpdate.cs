using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 67)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgStatsUpdate
{
    public const uint OpcodeId = Opcodes.SmsgStatsUpdate;

    public const int WireSize = 36;

    public readonly byte Sort;

    public readonly Pad0Buffer Pad0;

    public readonly uint ActorId;

    public readonly uint StatA;

    public readonly uint StatB;

    public readonly long CurrentXp;

    public readonly uint StatC;

    public readonly uint StatD;

    public readonly uint StatE;

    [InlineArray(3)]
    public struct Pad0Buffer
    {
        private byte _element0;
    }
}