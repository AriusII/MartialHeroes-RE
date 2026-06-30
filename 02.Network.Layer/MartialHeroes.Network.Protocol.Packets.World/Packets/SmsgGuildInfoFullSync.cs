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

    public readonly byte Gate;

    public readonly byte Pad9;

    public readonly GuildNameBuffer GuildName;

    public readonly short GuildId;

    public readonly byte GuildRankByte;

    public readonly byte Pad31;

    public readonly uint CountOrLevel;

    public readonly uint PointsLow;

    public readonly uint PointsHigh;

    public readonly ulong GuildFunds;

    public readonly byte NoticeFlagByte;

    public readonly Pad53Buffer Pad53;

    public readonly MemberIdsBuffer MemberIds;

    public readonly MemberRanksBuffer MemberRanks;

    public readonly MemberNamesBuffer MemberNames;

    public readonly MemberOnlineBuffer MemberOnline;

    public readonly Pad1210Buffer Pad1210;

    public readonly MemberPointsBuffer MemberPoints;

    public readonly MemberContribBuffer MemberContrib;

    public readonly MemberLoginTimeBuffer MemberLoginTime;

    [InlineArray(8)]
    public struct HeaderPad0Buffer
    {
        private byte _element0;
    }

    [InlineArray(18)]
    public struct GuildNameBuffer
    {
        private byte _element0;
    }

    [InlineArray(7)]
    public struct Pad53Buffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct MemberIdsBuffer
    {
        private byte _element0;
    }

    [InlineArray(50)]
    public struct MemberRanksBuffer
    {
        private byte _element0;
    }

    [InlineArray(850)]
    public struct MemberNamesBuffer
    {
        private byte _element0;
    }

    [InlineArray(50)]
    public struct MemberOnlineBuffer
    {
        private byte _element0;
    }

    [InlineArray(2)]
    public struct Pad1210Buffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct MemberPointsBuffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct MemberContribBuffer
    {
        private byte _element0;
    }

    [InlineArray(200)]
    public struct MemberLoginTimeBuffer
    {
        private byte _element0;
    }
}
