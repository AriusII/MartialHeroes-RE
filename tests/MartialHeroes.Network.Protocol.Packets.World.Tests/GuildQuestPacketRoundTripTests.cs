using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgGuildStateChangeResultTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgGuildStateChangeResult.WireSize, Unsafe.SizeOf<SmsgGuildStateChangeResult>());
    }

    [Fact]
    public void RoundTrip_ApplyGate_NonZero_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgGuildStateChangeResult.WireSize];
        raw[0x08] = 1;
        raw[0x09] = 3;
        raw[0x0A] = 1;
        raw[0x0B] = 4;
        raw[0x0C] = (byte)'G';
        raw[0x0D] = (byte)'u';
        raw[0x0E] = (byte)'i';
        raw[0x0F] = (byte)'l';
        raw[0x10] = (byte)'d';
        raw[0x1D] = 7;
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x20), 0x000012345678L);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x28), (ushort)0x001F);
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x2C), 0x0000ABCDEF00L);

        var pkt = MemoryMarshal.Read<SmsgGuildStateChangeResult>(raw);

        Assert.Equal((byte)1, pkt.ApplyGate);
        Assert.Equal((byte)3, pkt.Result);
        Assert.Equal((byte)1, pkt.Action);
        Assert.Equal((byte)4, pkt.Grade);

        ReadOnlySpan<byte> nameSpan = pkt.GuildName;
        Assert.Equal(16, nameSpan.Length);
        Assert.Equal((byte)'G', nameSpan[0]);
        Assert.Equal((byte)'u', nameSpan[1]);
        Assert.Equal((byte)'i', nameSpan[2]);
        Assert.Equal((byte)'l', nameSpan[3]);
        Assert.Equal((byte)'d', nameSpan[4]);

        Assert.Equal((byte)7, pkt.SortOrSlot);
        Assert.Equal(0x000012345678L, pkt.Value64A);
        Assert.Equal((ushort)0x001F, pkt.GuildId);
        Assert.Equal(0x0000ABCDEF00L, pkt.MoneyBalance64);

        byte[] out2 = new byte[SmsgGuildStateChangeResult.WireSize];
        MemoryMarshal.Write<SmsgGuildStateChangeResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ApplyGate_Zero_FeedbackBranch_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgGuildStateChangeResult.WireSize];
        raw[0x08] = 0;
        raw[0x09] = 2;
        raw[0x0A] = 4;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x28), (ushort)0x00FF);

        var pkt = MemoryMarshal.Read<SmsgGuildStateChangeResult>(raw);

        Assert.Equal((byte)0, pkt.ApplyGate);
        Assert.Equal((byte)2, pkt.Result);
        Assert.Equal((byte)4, pkt.Action);
        Assert.Equal((ushort)0x00FF, pkt.GuildId);

        byte[] out2 = new byte[SmsgGuildStateChangeResult.WireSize];
        MemoryMarshal.Write<SmsgGuildStateChangeResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void GuildName_Buffer_Length_Is16()
    {
        var pkt = default(SmsgGuildStateChangeResult);
        ReadOnlySpan<byte> nameSpan = pkt.GuildName;
        Assert.Equal(16, nameSpan.Length);
    }

    [Fact]
    public void Grade_IsAt_Offset11_IndependentOfGuildName()
    {
        byte[] raw = new byte[SmsgGuildStateChangeResult.WireSize];
        raw[0x0B] = 0xAB;
        raw[0x0C] = 0xCD;

        var pkt = MemoryMarshal.Read<SmsgGuildStateChangeResult>(raw);

        Assert.Equal((byte)0xAB, pkt.Grade);
        ReadOnlySpan<byte> nameSpan = pkt.GuildName;
        Assert.Equal((byte)0xCD, nameSpan[0]);
    }
}

public sealed class SmsgGuildInfoFullSyncTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgGuildInfoFullSync.WireSize, Unsafe.SizeOf<SmsgGuildInfoFullSync>());
    }

    [Fact]
    public void RoundTrip_Gate1_FullSync_PreservesKeyFields()
    {
        byte[] raw = new byte[SmsgGuildInfoFullSync.WireSize];
        raw[0x08] = 1;
        raw[0x09] = 0;
        raw[0x0A] = (byte)'A';
        raw[0x0B] = (byte)'B';
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0x1C), 42);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(0x2C), 999_000UL);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x3C), 0x00001111u);

        var pkt = MemoryMarshal.Read<SmsgGuildInfoFullSync>(raw);

        Assert.Equal((byte)1, pkt.Gate);
        Assert.Equal((short)42, pkt.GuildId);
        Assert.Equal(999_000UL, pkt.GuildFunds);

        ReadOnlySpan<byte> nameSpan = pkt.GuildName;
        Assert.Equal(18, nameSpan.Length);
        Assert.Equal((byte)'A', nameSpan[0]);
        Assert.Equal((byte)'B', nameSpan[1]);

        byte[] out2 = new byte[SmsgGuildInfoFullSync.WireSize];
        MemoryMarshal.Write<SmsgGuildInfoFullSync>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Gate0_Leave_PreservesGate()
    {
        byte[] raw = new byte[SmsgGuildInfoFullSync.WireSize];
        raw[0x08] = 0;

        var pkt = MemoryMarshal.Read<SmsgGuildInfoFullSync>(raw);

        Assert.Equal((byte)0, pkt.Gate);

        byte[] out2 = new byte[SmsgGuildInfoFullSync.WireSize];
        MemoryMarshal.Write<SmsgGuildInfoFullSync>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void MemberNames_Buffer_Length_Is850()
    {
        var pkt = default(SmsgGuildInfoFullSync);
        ReadOnlySpan<byte> span = pkt.MemberNames;
        Assert.Equal(850, span.Length);
    }
}

public sealed class SmsgQuestCompleteTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgQuestComplete.WireSize, Unsafe.SizeOf<SmsgQuestComplete>());
    }

    [Fact]
    public void RoundTrip_Apply1_Grant_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgQuestComplete.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x08), 1u);
        raw[0x0C] = 1;
        raw[0x0D] = 0xAB;

        var pkt = MemoryMarshal.Read<SmsgQuestComplete>(raw);

        Assert.Equal(1u, pkt.Apply);
        Assert.Equal((byte)1, pkt.RewardState);

        ReadOnlySpan<byte> bodySpan = pkt.BodyRemainder;
        Assert.Equal(331, bodySpan.Length);
        Assert.Equal((byte)0xAB, bodySpan[0]);

        byte[] out2 = new byte[SmsgQuestComplete.WireSize];
        MemoryMarshal.Write<SmsgQuestComplete>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Apply1_Deny_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgQuestComplete.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x08), 1u);
        raw[0x0C] = 2;

        var pkt = MemoryMarshal.Read<SmsgQuestComplete>(raw);

        Assert.Equal(1u, pkt.Apply);
        Assert.Equal((byte)2, pkt.RewardState);

        byte[] out2 = new byte[SmsgQuestComplete.WireSize];
        MemoryMarshal.Write<SmsgQuestComplete>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Apply0_HandlerSkips_StillRoundTrips()
    {
        byte[] raw = new byte[SmsgQuestComplete.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x08), 0u);
        raw[0x0C] = 99;

        var pkt = MemoryMarshal.Read<SmsgQuestComplete>(raw);

        Assert.Equal(0u, pkt.Apply);
        Assert.Equal((byte)99, pkt.RewardState);

        byte[] out2 = new byte[SmsgQuestComplete.WireSize];
        MemoryMarshal.Write<SmsgQuestComplete>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void BodyRemainder_Buffer_Length_Is331()
    {
        var pkt = default(SmsgQuestComplete);
        ReadOnlySpan<byte> bodySpan = pkt.BodyRemainder;
        Assert.Equal(331, bodySpan.Length);
    }
}
