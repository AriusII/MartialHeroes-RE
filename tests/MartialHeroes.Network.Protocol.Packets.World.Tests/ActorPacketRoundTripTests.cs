using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgActorVitalsAndPairStateTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgActorVitalsAndPairState.WireSize, Unsafe.SizeOf<SmsgActorVitalsAndPairState>());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgActorVitalsAndPairState.WireSize];
        raw[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x00001234u);
        raw[10] = 1;
        raw[11] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), 0x00005678u);
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(16), 75000L);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(24), 500);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(28), 100);

        var pkt = MemoryMarshal.Read<SmsgActorVitalsAndPairState>(raw);

        Assert.Equal((byte)1, pkt.Sort);
        Assert.Equal(0x00001234u, pkt.ActorId);
        Assert.Equal((byte)1, pkt.RelationState);
        Assert.Equal((byte)0, pkt.RelationState2);
        Assert.Equal(0x00005678u, pkt.PartnerActorId);
        Assert.Equal(75000L, pkt.CurrentHp);
        Assert.Equal(500, pkt.CurrentMp);
        Assert.Equal(100, pkt.CurrentStamina);

        byte[] out2 = new byte[SmsgActorVitalsAndPairState.WireSize];
        MemoryMarshal.Write<SmsgActorVitalsAndPairState>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void CurrentHp_SingleI64_NegativePreserved()
    {
        byte[] raw = new byte[SmsgActorVitalsAndPairState.WireSize];
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(16), -1L);

        var pkt = MemoryMarshal.Read<SmsgActorVitalsAndPairState>(raw);

        Assert.Equal(-1L, pkt.CurrentHp);
    }
}

public sealed class SmsgActorStateEventTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgActorStateEvent.WireSize, Unsafe.SizeOf<SmsgActorStateEvent>());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgActorStateEvent.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x00002345u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), 0x00003456u);
        for (int i = 0; i < 20; i++)
            raw[12 + i] = (byte)(i + 1);

        var pkt = MemoryMarshal.Read<SmsgActorStateEvent>(raw);

        Assert.Equal(1u, pkt.TargetSort);
        Assert.Equal(0x00002345u, pkt.TargetId);
        Assert.Equal(0x00003456u, pkt.ActorId);

        ReadOnlySpan<byte> nameSpan = pkt.Name;
        for (int i = 0; i < 20; i++)
            Assert.Equal((byte)(i + 1), nameSpan[i]);

        byte[] out2 = new byte[SmsgActorStateEvent.WireSize];
        MemoryMarshal.Write<SmsgActorStateEvent>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void NameBuffer_Length_Is20()
    {
        var pkt = default(SmsgActorStateEvent);
        ReadOnlySpan<byte> nameSpan = pkt.Name;
        Assert.Equal(20, nameSpan.Length);
    }
}

public sealed class SmsgActorVisualFlagsSetTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgActorVisualFlagsSet.WireSize, Unsafe.SizeOf<SmsgActorVisualFlagsSet>());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgActorVisualFlagsSet.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x00007890u);
        raw[8] = 0x04;

        var pkt = MemoryMarshal.Read<SmsgActorVisualFlagsSet>(raw);

        Assert.Equal(2u, pkt.ActorSort);
        Assert.Equal(0x00007890u, pkt.ActorId);
        Assert.Equal((byte)0x04, pkt.VisualFlags);

        byte[] out2 = new byte[SmsgActorVisualFlagsSet.WireSize];
        MemoryMarshal.Write<SmsgActorVisualFlagsSet>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgStealthToggleTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgStealthToggle.WireSize, Unsafe.SizeOf<SmsgStealthToggle>());
    }

    [Fact]
    public void RoundTrip_StealthOn_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgStealthToggle.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x0000ABCDu);
        raw[8] = 1;

        var pkt = MemoryMarshal.Read<SmsgStealthToggle>(raw);

        Assert.Equal(1u, pkt.ActorSort);
        Assert.Equal(0x0000ABCDu, pkt.ActorId);
        Assert.Equal((byte)1, pkt.StealthFlag);

        byte[] out2 = new byte[SmsgStealthToggle.WireSize];
        MemoryMarshal.Write<SmsgStealthToggle>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_StealthOff_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgStealthToggle.WireSize];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0x0000ABCDu);
        raw[8] = 0;

        var pkt = MemoryMarshal.Read<SmsgStealthToggle>(raw);

        Assert.Equal((byte)0, pkt.StealthFlag);

        byte[] out2 = new byte[SmsgStealthToggle.WireSize];
        MemoryMarshal.Write<SmsgStealthToggle>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}
