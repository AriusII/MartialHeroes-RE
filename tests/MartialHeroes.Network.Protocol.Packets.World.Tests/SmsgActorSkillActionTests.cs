using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgActorSkillActionTargetRecordTests
{
    [Fact]
    public void Stride_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgActorSkillAction.TargetRecord.Stride, Unsafe.SizeOf<SmsgActorSkillAction.TargetRecord>());
    }

    [Fact]
    public void HeaderSize_Is24()
    {
        Assert.Equal(24, SmsgActorSkillAction.HeaderSize);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgActorSkillAction.TargetRecord.Stride];
        raw[0x00] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04), 0x0000DEADu);
        for (int i = 0; i < 12; i++)
            raw[0x08 + i] = (byte)(i + 1);
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), -12345678L);
        for (int i = 0; i < 8; i++)
            raw[0x1C + i] = (byte)(0xA1 + i);

        var rec = MemoryMarshal.Read<SmsgActorSkillAction.TargetRecord>(raw);

        Assert.Equal((byte)0x03, rec.TargetSubKey);
        Assert.Equal(0x0000DEADu, rec.TargetKey);
        Assert.Equal(-12345678L, rec.HitMagnitude);

        byte[] out2 = new byte[SmsgActorSkillAction.TargetRecord.Stride];
        MemoryMarshal.Write<SmsgActorSkillAction.TargetRecord>(out2, in rec);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void HitMagnitude_Negative_RoundTrips()
    {
        byte[] raw = new byte[SmsgActorSkillAction.TargetRecord.Stride];
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), -1L);

        var rec = MemoryMarshal.Read<SmsgActorSkillAction.TargetRecord>(raw);

        Assert.Equal(-1L, rec.HitMagnitude);

        byte[] out2 = new byte[SmsgActorSkillAction.TargetRecord.Stride];
        MemoryMarshal.Write<SmsgActorSkillAction.TargetRecord>(out2, in rec);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void HitMagnitude_LargePositive_RoundTrips()
    {
        byte[] raw = new byte[SmsgActorSkillAction.TargetRecord.Stride];
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), 9_999_999_999L);

        var rec = MemoryMarshal.Read<SmsgActorSkillAction.TargetRecord>(raw);

        Assert.Equal(9_999_999_999L, rec.HitMagnitude);

        byte[] out2 = new byte[SmsgActorSkillAction.TargetRecord.Stride];
        MemoryMarshal.Write<SmsgActorSkillAction.TargetRecord>(out2, in rec);
        Assert.Equal(raw, out2);
    }
}
