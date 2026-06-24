using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 100)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgCubeGambleReelUpdate
{
    public const uint OpcodeId = Opcodes.SmsgCombatAttackUpdate;

    public const int WireSize = 188;

    public readonly HeadOpaqueBuffer HeadOpaque;

    public readonly byte Phase;

    public readonly ReservedABuffer ReservedA;

    public readonly byte SubKind;

    public readonly ReservedBBuffer ReservedB;

    public readonly uint Value;

    public readonly OpaqueTailBuffer OpaqueTail;

    [InlineArray(8)]
    public struct HeadOpaqueBuffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct ReservedABuffer
    {
        private byte _element0;
    }

    [InlineArray(1)]
    public struct ReservedBBuffer
    {
        private byte _element0;
    }

    [InlineArray(172)]
    public struct OpaqueTailBuffer
    {
        private byte _element0;
    }
}