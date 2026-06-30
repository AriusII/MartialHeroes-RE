using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgActorDeathStateTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgActorDeathState.WireSize, Unsafe.SizeOf<SmsgActorDeathState>());
    }

    [Fact]
    public void RoundTrip_Mode1_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgActorDeathState.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x00), 0x00000000u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04), 0x00001234u);
        raw[0x08] = 1;
        raw[0x09] = 0;
        raw[0x0A] = 0;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 3u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), 0x00005678u);

        var pkt = MemoryMarshal.Read<SmsgActorDeathState>(raw);

        Assert.Equal(0x00000000u, pkt.LeadingDword);
        Assert.Equal(0x00001234u, pkt.ActorKey);
        Assert.Equal((byte)1, pkt.Mode);
        Assert.Equal(3u, pkt.SubSelector);
        Assert.Equal(0x00005678u, pkt.KillerKey);

        byte[] out2 = new byte[SmsgActorDeathState.WireSize];
        MemoryMarshal.Write<SmsgActorDeathState>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Mode3_ClearRevive_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgActorDeathState.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x00), 0x0000000Au);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04), 0x0000ABCDu);
        raw[0x08] = 3;
        raw[0x09] = 0;
        raw[0x0A] = 0;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), 0u);

        var pkt = MemoryMarshal.Read<SmsgActorDeathState>(raw);

        Assert.Equal(0x0000000Au, pkt.LeadingDword);
        Assert.Equal(0x0000ABCDu, pkt.ActorKey);
        Assert.Equal((byte)3, pkt.Mode);
        Assert.Equal(0u, pkt.SubSelector);
        Assert.Equal(0u, pkt.KillerKey);

        byte[] out2 = new byte[SmsgActorDeathState.WireSize];
        MemoryMarshal.Write<SmsgActorDeathState>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgPvpDeathFxTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgPvpDeathFx.WireSize, Unsafe.SizeOf<SmsgPvpDeathFx>());
    }

    [Fact]
    public void RoundTrip_Mode1_Engage_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgPvpDeathFx.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x00), 0x00000000u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04), 0x00001234u);
        raw[0x08] = 1;
        raw[0x09] = 0;
        raw[0x0A] = 1;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 0x00005678u);

        var pkt = MemoryMarshal.Read<SmsgPvpDeathFx>(raw);

        Assert.Equal(0x00000000u, pkt.LeadingDword);
        Assert.Equal(0x00001234u, pkt.ActorKey);
        Assert.Equal((byte)1, pkt.Gate);
        Assert.Equal((byte)0, pkt.Pad1);
        Assert.Equal((byte)1, pkt.Mode);
        Assert.Equal((byte)0, pkt.Pad2);
        Assert.Equal(0x00005678u, pkt.OpponentKey);

        byte[] out2 = new byte[SmsgPvpDeathFx.WireSize];
        MemoryMarshal.Write<SmsgPvpDeathFx>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Mode6_Disengage_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgPvpDeathFx.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x00), 0x00000000u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04), 0x0000ABCDu);
        raw[0x08] = 1;
        raw[0x09] = 0;
        raw[0x0A] = 6;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 0x00009999u);

        var pkt = MemoryMarshal.Read<SmsgPvpDeathFx>(raw);

        Assert.Equal(0x0000ABCDu, pkt.ActorKey);
        Assert.Equal((byte)1, pkt.Gate);
        Assert.Equal((byte)6, pkt.Mode);
        Assert.Equal(0x00009999u, pkt.OpponentKey);

        byte[] out2 = new byte[SmsgPvpDeathFx.WireSize];
        MemoryMarshal.Write<SmsgPvpDeathFx>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}
