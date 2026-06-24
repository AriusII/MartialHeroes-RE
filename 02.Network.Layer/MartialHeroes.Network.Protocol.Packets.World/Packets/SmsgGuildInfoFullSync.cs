using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Core.Opcodes;

namespace MartialHeroes.Network.Protocol.Packets.World.Packets;

[PacketOpcode(4, 65)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct SmsgGuildInfoFullSync
{
    public const uint OpcodeId = Opcodes.SmsgGuildInfoFullSync;

    public const int WireSize = 1812;

    public readonly HeaderPad0Buffer HeaderPad0;

    public readonly byte Subtype;

    public readonly byte ReservedA;

    public readonly GuildTitleBuffer GuildTitle;

    public readonly short GuildId;

    public readonly byte RankByte;

    public readonly byte ReservedB;

    public readonly uint CountField;

    public readonly uint PointsLow;

    public readonly uint PointsHigh;

    public readonly ulong GuildFunds;

    public readonly byte FlagByte;

    public readonly ReservedCBuffer ReservedC;

    public readonly MemberIdsBuffer MemberIds;

    public readonly OnlineFlagsABuffer OnlineFlagsA;

    public readonly MemberNamesBuffer MemberNames;

    public readonly OnlineFlagsBBuffer OnlineFlagsB;

    public readonly ReservedDBuffer ReservedD;

    public readonly PointsContribBuffer PointsContrib;

    public readonly DwordFBuffer DwordF;

    public readonly DwordGBuffer DwordG;

    [InlineArray(8)]
    public struct HeaderPad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(18)]
    public struct GuildTitleBuffer
    {
        private byte _element0;
    }

    [InlineArray(7)]
    public struct ReservedCBuffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct MemberIdsBuffer
    {
        private byte _element0;
    }

    [InlineArray(50)]
    public struct OnlineFlagsABuffer
    {
        private byte _element0;
    }

    [InlineArray(850)]
    public struct MemberNamesBuffer
    {
        private byte _element0;
    }

    [InlineArray(50)]
    public struct OnlineFlagsBBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct ReservedDBuffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct PointsContribBuffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct DwordFBuffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct DwordGBuffer
    {
        private byte _element0;
    }
}
