using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgDeliveryRecordRoundTripTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgDeliveryRecord.Size, Unsafe.SizeOf<SmsgDeliveryRecord>());
    }

    [Fact]
    public void RoundTrip_AllZeros_PreservesBytes()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);
        byte[] out2 = new byte[SmsgDeliveryRecord.Size];
        MemoryMarshal.Write<SmsgDeliveryRecord>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void ResultCode_IsAtPayloadOffset0()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        raw[0x00] = 1;

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal((byte)1, pkt.ResultCode);
    }

    [Fact]
    public void SubAction_IsAtPayloadOffset2()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        raw[0x02] = 3;

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal((byte)3, pkt.SubAction);
    }

    [Fact]
    public void Money_IsAtPayloadOffset0x14()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), 9_999_999L);

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal(9_999_999L, pkt.Money);
    }

    [Fact]
    public void EntryKey_IsAtPayloadOffset0x80()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x80), 0x01020304);

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal(0x01020304, pkt.EntryKey);
    }

    [Fact]
    public void RoundTrip_ResultOk_SubAction0_PreservesAllBytes()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        raw[0x00] = 1;
        raw[0x02] = 0;
        raw[0x03] = (byte)'T';
        raw[0x04] = (byte)'e';
        raw[0x05] = (byte)'s';
        raw[0x06] = (byte)'t';
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), 200_000L);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x80), 42);

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal((byte)1, pkt.ResultCode);
        Assert.Equal((byte)0, pkt.SubAction);
        Assert.Equal(200_000L, pkt.Money);
        Assert.Equal(42, pkt.EntryKey);

        byte[] out2 = new byte[SmsgDeliveryRecord.Size];
        MemoryMarshal.Write<SmsgDeliveryRecord>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ResultFail_NoMutation()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        raw[0x00] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x80), 99);

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal((byte)0, pkt.ResultCode);
        Assert.Equal(99, pkt.EntryKey);

        byte[] out2 = new byte[SmsgDeliveryRecord.Size];
        MemoryMarshal.Write<SmsgDeliveryRecord>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_SubAction1_CreditMoney_PreservesAllBytes()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        raw[0x00] = 1;
        raw[0x02] = 1;
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), 50_000L);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x80), 7);

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal((byte)1, pkt.ResultCode);
        Assert.Equal((byte)1, pkt.SubAction);
        Assert.Equal(50_000L, pkt.Money);
        Assert.Equal(7, pkt.EntryKey);

        byte[] out2 = new byte[SmsgDeliveryRecord.Size];
        MemoryMarshal.Write<SmsgDeliveryRecord>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_SubAction4_DebitMoney_PreservesAllBytes()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        raw[0x00] = 1;
        raw[0x02] = 4;
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), 1_000L);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x80), 3);

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal((byte)4, pkt.SubAction);
        Assert.Equal(1_000L, pkt.Money);
        Assert.Equal(3, pkt.EntryKey);

        byte[] out2 = new byte[SmsgDeliveryRecord.Size];
        MemoryMarshal.Write<SmsgDeliveryRecord>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void Money_NegativeValueRoundTrips()
    {
        byte[] raw = new byte[SmsgDeliveryRecord.Size];
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0x14), -1L);

        var pkt = MemoryMarshal.Read<SmsgDeliveryRecord>(raw);

        Assert.Equal(-1L, pkt.Money);

        byte[] out2 = new byte[SmsgDeliveryRecord.Size];
        MemoryMarshal.Write<SmsgDeliveryRecord>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class CmsgCarrierPigeonSendRoundTripTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgCarrierPigeonSend.Size, Unsafe.SizeOf<CmsgCarrierPigeonSend>());
    }

    [Fact]
    public void RoundTrip_AllZeros_PreservesBytes()
    {
        byte[] raw = new byte[CmsgCarrierPigeonSend.Size];
        var pkt = MemoryMarshal.Read<CmsgCarrierPigeonSend>(raw);
        byte[] out2 = new byte[CmsgCarrierPigeonSend.Size];
        MemoryMarshal.Write<CmsgCarrierPigeonSend>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void SendMode_IsAtOffset0()
    {
        byte[] raw = new byte[CmsgCarrierPigeonSend.Size];
        raw[0x00] = 1;

        var pkt = MemoryMarshal.Read<CmsgCarrierPigeonSend>(raw);

        Assert.Equal((byte)1, pkt.SendMode);
    }

    [Fact]
    public void MoneyLow_IsAtOffset0x14()
    {
        byte[] raw = new byte[CmsgCarrierPigeonSend.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x14), 0xDEADBEEFu);

        var pkt = MemoryMarshal.Read<CmsgCarrierPigeonSend>(raw);

        Assert.Equal(0xDEADBEEFu, pkt.MoneyLow);
    }

    [Fact]
    public void MoneyHigh_IsAtOffset0x18()
    {
        byte[] raw = new byte[CmsgCarrierPigeonSend.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x18), 0x00000001u);

        var pkt = MemoryMarshal.Read<CmsgCarrierPigeonSend>(raw);

        Assert.Equal(0x00000001u, pkt.MoneyHigh);
    }

    [Fact]
    public void RoundTrip_Mode0_WithRecipientAndMoney_PreservesAllBytes()
    {
        byte[] raw = new byte[CmsgCarrierPigeonSend.Size];
        raw[0x00] = 0;
        raw[0x01] = (byte)'A';
        raw[0x02] = (byte)'l';
        raw[0x03] = (byte)'i';
        raw[0x04] = (byte)'c';
        raw[0x05] = (byte)'e';
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x14), 5_000u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x18), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x1C), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x20), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x24), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x28), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x2C), 0xFFFFFFFFu);
        raw[0x30] = (byte)'H';
        raw[0x31] = (byte)'i';

        var pkt = MemoryMarshal.Read<CmsgCarrierPigeonSend>(raw);

        Assert.Equal((byte)0, pkt.SendMode);
        Assert.Equal(5_000u, pkt.MoneyLow);
        Assert.Equal(0u, pkt.MoneyHigh);

        byte[] out2 = new byte[CmsgCarrierPigeonSend.Size];
        MemoryMarshal.Write<CmsgCarrierPigeonSend>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Mode4_NoAttachment_PreservesAllBytes()
    {
        byte[] raw = new byte[CmsgCarrierPigeonSend.Size];
        raw[0x00] = 4;
        raw[0x01] = (byte)'B';
        raw[0x02] = (byte)'o';
        raw[0x03] = (byte)'b';

        var pkt = MemoryMarshal.Read<CmsgCarrierPigeonSend>(raw);

        Assert.Equal((byte)4, pkt.SendMode);
        Assert.Equal(0u, pkt.MoneyLow);
        Assert.Equal(0u, pkt.MoneyHigh);

        byte[] out2 = new byte[CmsgCarrierPigeonSend.Size];
        MemoryMarshal.Write<CmsgCarrierPigeonSend>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_AllOnes_PreservesBytes()
    {
        byte[] raw = new byte[CmsgCarrierPigeonSend.Size];
        for (int i = 0; i < raw.Length; i++) raw[i] = 0xFF;

        var pkt = MemoryMarshal.Read<CmsgCarrierPigeonSend>(raw);
        byte[] out2 = new byte[CmsgCarrierPigeonSend.Size];
        MemoryMarshal.Write<CmsgCarrierPigeonSend>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class CmsgDeliveryClaimRoundTripTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgDeliveryClaim.Size, Unsafe.SizeOf<CmsgDeliveryClaim>());
    }

    [Fact]
    public void RoundTrip_Index0_Open_PreservesSelector()
    {
        var pkt = new CmsgDeliveryClaim { Selector = 0u };
        byte[] raw = new byte[CmsgDeliveryClaim.Size];
        MemoryMarshal.Write<CmsgDeliveryClaim>(raw, in pkt);

        var pkt2 = MemoryMarshal.Read<CmsgDeliveryClaim>(raw);
        Assert.Equal(0u, pkt2.Selector);

        byte[] raw2 = new byte[CmsgDeliveryClaim.Size];
        MemoryMarshal.Write<CmsgDeliveryClaim>(raw2, in pkt2);
        Assert.Equal(raw, raw2);
    }

    [Fact]
    public void RoundTrip_Index3_Claim_PreservesSelector()
    {
        var pkt = new CmsgDeliveryClaim { Selector = 3u };
        byte[] raw = new byte[CmsgDeliveryClaim.Size];
        MemoryMarshal.Write<CmsgDeliveryClaim>(raw, in pkt);

        var pkt2 = MemoryMarshal.Read<CmsgDeliveryClaim>(raw);
        Assert.Equal(3u, pkt2.Selector);

        byte[] raw2 = new byte[CmsgDeliveryClaim.Size];
        MemoryMarshal.Write<CmsgDeliveryClaim>(raw2, in pkt2);
        Assert.Equal(raw, raw2);
    }

    [Fact]
    public void Selector_FieldLayout_LittleEndian()
    {
        var pkt = new CmsgDeliveryClaim { Selector = 0x01020304u };
        byte[] raw = new byte[CmsgDeliveryClaim.Size];
        MemoryMarshal.Write<CmsgDeliveryClaim>(raw, in pkt);

        Assert.Equal((byte)0x04, raw[0]);
        Assert.Equal((byte)0x03, raw[1]);
        Assert.Equal((byte)0x02, raw[2]);
        Assert.Equal((byte)0x01, raw[3]);
    }

    [Fact]
    public void RoundTrip_MaxSlotIndex_PreservesSelector()
    {
        var pkt = new CmsgDeliveryClaim { Selector = 7u };
        byte[] raw = new byte[CmsgDeliveryClaim.Size];
        MemoryMarshal.Write<CmsgDeliveryClaim>(raw, in pkt);

        var pkt2 = MemoryMarshal.Read<CmsgDeliveryClaim>(raw);
        Assert.Equal(7u, pkt2.Selector);
    }
}
