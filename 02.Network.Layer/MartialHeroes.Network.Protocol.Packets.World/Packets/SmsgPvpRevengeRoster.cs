using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(5, 89)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgPvpRevengeRoster
{
    public const uint OpcodeId = Opcodes.SmsgPvpRevengeRoster;

    public const int WireSize = 188;

    public readonly byte RevengeSort;

    public readonly uint RevengeId;

    public readonly GapABuffer GapA;

    public readonly byte RevengeTeam;

    public readonly byte RevengeTeamGuild;

    public readonly NameArrayBuffer NameArray;

    public readonly DefaultPropertyMaxBuffer DefaultPropertyMax;

    public readonly RandomPropertyMaxBuffer RandomPropertyMax;

    public readonly TailPadBuffer TailPad;

    [InlineArray(3)]
    public struct GapABuffer
    {
        private byte _element0;
    }

    [InlineArray(170)]
    public struct NameArrayBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct DefaultPropertyMaxBuffer
    {
        private byte _element0;
    }

    [InlineArray(4)]
    public struct RandomPropertyMaxBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct TailPadBuffer
    {
        private byte _element0;
    }
}
