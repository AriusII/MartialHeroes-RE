using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class CmsgPartyInviteRoundTripTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgPartyInvite.Size, Unsafe.SizeOf<CmsgPartyInvite>());
    }

    [Fact]
    public void RoundTrip_InviteMode_PreservesAllFields()
    {
        byte[] raw = new byte[CmsgPartyInvite.Size];
        raw[0] = 0x02;
        raw[1] = 0x00;
        raw[2] = 0x00;
        raw[3] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x12345678u);

        var pkt = MemoryMarshal.Read<CmsgPartyInvite>(raw);

        Assert.Equal((byte)0x02, pkt.Mode);
        Assert.Equal(0x12345678u, pkt.TargetId);

        byte[] out2 = new byte[CmsgPartyInvite.Size];
        MemoryMarshal.Write<CmsgPartyInvite>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_AcceptMode_PreservesMode()
    {
        byte[] raw = new byte[CmsgPartyInvite.Size];
        raw[0] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0xDEADBEEFu);

        var pkt = MemoryMarshal.Read<CmsgPartyInvite>(raw);

        Assert.Equal((byte)0x00, pkt.Mode);
        Assert.Equal(0xDEADBEEFu, pkt.TargetId);

        byte[] out2 = new byte[CmsgPartyInvite.Size];
        MemoryMarshal.Write<CmsgPartyInvite>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class CmsgPartyLeaveKickRoundTripTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgPartyLeaveKick.Size, Unsafe.SizeOf<CmsgPartyLeaveKick>());
    }

    [Fact]
    public void RoundTrip_KickMode_PreservesAllFields()
    {
        byte[] raw = new byte[CmsgPartyLeaveKick.Size];
        raw[0] = 0x01;
        raw[1] = 0x00;
        raw[2] = 0x00;
        raw[3] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0xABCD0001u);

        var pkt = MemoryMarshal.Read<CmsgPartyLeaveKick>(raw);

        Assert.Equal((byte)0x01, pkt.Mode);
        Assert.Equal(0xABCD0001u, pkt.Id);

        byte[] out2 = new byte[CmsgPartyLeaveKick.Size];
        MemoryMarshal.Write<CmsgPartyLeaveKick>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_SelfLeaveMode_PreservesAllFields()
    {
        byte[] raw = new byte[CmsgPartyLeaveKick.Size];
        raw[0] = 0x00;
        raw[1] = 0x00;
        raw[2] = 0x00;
        raw[3] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x00000042u);

        var pkt = MemoryMarshal.Read<CmsgPartyLeaveKick>(raw);

        Assert.Equal((byte)0x00, pkt.Mode);
        Assert.Equal(0x00000042u, pkt.Id);

        byte[] out2 = new byte[CmsgPartyLeaveKick.Size];
        MemoryMarshal.Write<CmsgPartyLeaveKick>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class CmsgPartyLeaderOpRoundTripTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgPartyLeaderOp.Size, Unsafe.SizeOf<CmsgPartyLeaderOp>());
    }

    [Fact]
    public void RoundTrip_DelegateLeader_PreservesAllFields()
    {
        byte[] raw = new byte[CmsgPartyLeaderOp.Size];
        raw[0] = 0x00;
        raw[1] = 0x00;
        raw[2] = 0x00;
        raw[3] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x00010203u);

        var pkt = MemoryMarshal.Read<CmsgPartyLeaderOp>(raw);

        Assert.Equal((byte)0x00, pkt.Mode);
        Assert.Equal(0x00010203u, pkt.TargetId);

        byte[] out2 = new byte[CmsgPartyLeaderOp.Size];
        MemoryMarshal.Write<CmsgPartyLeaderOp>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class CmsgTradeRequestRoundTripTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgTradeRequest.Size, Unsafe.SizeOf<CmsgTradeRequest>());
    }

    [Fact]
    public void RoundTrip_RequestMode_PreservesAllFields()
    {
        byte[] raw = new byte[CmsgTradeRequest.Size];
        raw[0] = 0x02;
        raw[1] = 0x00;
        raw[2] = 0x00;
        raw[3] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x87654321u);

        var pkt = MemoryMarshal.Read<CmsgTradeRequest>(raw);

        Assert.Equal((byte)0x02, pkt.Mode);
        Assert.Equal(0x87654321u, pkt.Value);

        byte[] out2 = new byte[CmsgTradeRequest.Size];
        MemoryMarshal.Write<CmsgTradeRequest>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_DeclineMode_PreservesAllFields()
    {
        byte[] raw = new byte[CmsgTradeRequest.Size];
        raw[0] = 0x01;
        raw[1] = 0x00;
        raw[2] = 0x00;
        raw[3] = 0x00;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x11223344u);

        var pkt = MemoryMarshal.Read<CmsgTradeRequest>(raw);

        Assert.Equal((byte)0x01, pkt.Mode);
        Assert.Equal(0x11223344u, pkt.Value);

        byte[] out2 = new byte[CmsgTradeRequest.Size];
        MemoryMarshal.Write<CmsgTradeRequest>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgPartyRosterEventRoundTripTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgPartyRosterEvent.WireSize, Unsafe.SizeOf<SmsgPartyRosterEvent>());
    }

    [Fact]
    public void RoundTrip_MemberJoined_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgPartyRosterEvent.WireSize];
        raw[0] = 0x00;
        raw[1] = 0x03;

        var pkt = MemoryMarshal.Read<SmsgPartyRosterEvent>(raw);

        Assert.Equal((byte)0x00, pkt.Event);
        Assert.Equal((byte)0x03, pkt.MemberSlot);

        byte[] out2 = new byte[SmsgPartyRosterEvent.WireSize];
        MemoryMarshal.Write<SmsgPartyRosterEvent>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_PartyDisbanded_PreservesEventCode()
    {
        byte[] raw = new byte[SmsgPartyRosterEvent.WireSize];
        raw[0] = 0x03;
        raw[1] = 0x00;

        var pkt = MemoryMarshal.Read<SmsgPartyRosterEvent>(raw);

        Assert.Equal((byte)0x03, pkt.Event);
        Assert.Equal((byte)0x00, pkt.MemberSlot);

        byte[] out2 = new byte[SmsgPartyRosterEvent.WireSize];
        MemoryMarshal.Write<SmsgPartyRosterEvent>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void Event_IsAtPayloadOffset0()
    {
        byte[] raw = new byte[SmsgPartyRosterEvent.WireSize];
        raw[0] = 0x02;

        var pkt = MemoryMarshal.Read<SmsgPartyRosterEvent>(raw);

        Assert.Equal((byte)0x02, pkt.Event);
    }

    [Fact]
    public void MemberSlot_IsAtPayloadOffset1()
    {
        byte[] raw = new byte[SmsgPartyRosterEvent.WireSize];
        raw[1] = 0x07;

        var pkt = MemoryMarshal.Read<SmsgPartyRosterEvent>(raw);

        Assert.Equal((byte)0x07, pkt.MemberSlot);
    }
}

public sealed class SmsgPartyMemberStatsRoundTripTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgPartyMemberStats.WireSize, Unsafe.SizeOf<SmsgPartyMemberStats>());
    }

    [Fact]
    public void RoundTrip_FullBuffer_PreservesAllBytes()
    {
        byte[] raw = new byte[SmsgPartyMemberStats.WireSize];
        for (int i = 0; i < raw.Length; i++)
            raw[i] = (byte)(i & 0xFF);

        var pkt = MemoryMarshal.Read<SmsgPartyMemberStats>(raw);

        byte[] out2 = new byte[SmsgPartyMemberStats.WireSize];
        MemoryMarshal.Write<SmsgPartyMemberStats>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_AllZeros_PreservesAllBytes()
    {
        byte[] raw = new byte[SmsgPartyMemberStats.WireSize];

        var pkt = MemoryMarshal.Read<SmsgPartyMemberStats>(raw);

        byte[] out2 = new byte[SmsgPartyMemberStats.WireSize];
        MemoryMarshal.Write<SmsgPartyMemberStats>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_AllOnes_PreservesAllBytes()
    {
        byte[] raw = new byte[SmsgPartyMemberStats.WireSize];
        for (int i = 0; i < raw.Length; i++)
            raw[i] = 0xFF;

        var pkt = MemoryMarshal.Read<SmsgPartyMemberStats>(raw);

        byte[] out2 = new byte[SmsgPartyMemberStats.WireSize];
        MemoryMarshal.Write<SmsgPartyMemberStats>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}
