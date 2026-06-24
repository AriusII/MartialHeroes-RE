using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 90)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPvpCounters
{
    public const uint OpcodeId = Opcodes.SmsgPvpCounters;

    public const int WireSize = 32;

    public readonly byte Sort;

    public readonly GapABuffer GapA;

    public readonly uint Id;

    public readonly uint Counter1;

    public readonly uint Counter2;

    public readonly uint Counter3;

    public readonly uint Counter4;

    public readonly uint Counter5;

    public readonly uint Counter6;

    [InlineArray(3)]
    public struct GapABuffer
    {
        private byte _element0;
    }
}
