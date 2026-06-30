using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgEquipItemResultTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgEquipItemResult.WireSize, Unsafe.SizeOf<SmsgEquipItemResult>());
    }

    [Fact]
    public void RoundTrip_ResultOk_ToSlot15_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgEquipItemResult.WireSize];
        raw[0x00] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04), 0x0000ABCDu);
        raw[0x08] = 0x01;
        raw[0x0A] = 0x05;
        raw[0x0B] = 0x00;
        raw[0x0C] = 0x0F;

        var pkt = MemoryMarshal.Read<SmsgEquipItemResult>(raw);

        Assert.Equal((byte)0x01, pkt.Guard);
        Assert.Equal(0x0000ABCDu, pkt.ActorSortKey);
        Assert.Equal((byte)0x01, pkt.Result);
        Assert.Equal((byte)0x05, pkt.FromSlot);
        Assert.Equal((byte)0x0F, pkt.ToSlot);

        byte[] out2 = new byte[SmsgEquipItemResult.WireSize];
        MemoryMarshal.Write<SmsgEquipItemResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ResultZero_NoApply_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgEquipItemResult.WireSize];
        raw[0x00] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x04), 0x00001111u);
        raw[0x08] = 0x00;
        raw[0x0A] = 0x03;
        raw[0x0C] = 0x07;

        var pkt = MemoryMarshal.Read<SmsgEquipItemResult>(raw);

        Assert.Equal((byte)0x00, pkt.Result);
        Assert.Equal((byte)0x03, pkt.FromSlot);
        Assert.Equal((byte)0x07, pkt.ToSlot);

        byte[] out2 = new byte[SmsgEquipItemResult.WireSize];
        MemoryMarshal.Write<SmsgEquipItemResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgEquipChangeResultTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgEquipChangeResult.WireSize, Unsafe.SizeOf<SmsgEquipChangeResult>());
    }

    [Fact]
    public void RoundTrip_TruthyResult_RefreshGateZero_Slot15_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgEquipChangeResult.WireSize];
        raw[0x08] = 0x01;
        raw[0x0A] = 0x00;
        raw[0x0B] = 0x0F;

        var pkt = MemoryMarshal.Read<SmsgEquipChangeResult>(raw);

        Assert.Equal((byte)0x01, pkt.Result);
        Assert.Equal((byte)0x00, pkt.SlotKind);
        Assert.Equal((byte)0x0F, pkt.SlotIndex);

        byte[] out2 = new byte[SmsgEquipChangeResult.WireSize];
        MemoryMarshal.Write<SmsgEquipChangeResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ResultZero_Error_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgEquipChangeResult.WireSize];
        raw[0x08] = 0x00;
        raw[0x0A] = 0x01;
        raw[0x0B] = 0x0E;

        var pkt = MemoryMarshal.Read<SmsgEquipChangeResult>(raw);

        Assert.Equal((byte)0x00, pkt.Result);
        Assert.Equal((byte)0x01, pkt.SlotKind);
        Assert.Equal((byte)0x0E, pkt.SlotIndex);

        byte[] out2 = new byte[SmsgEquipChangeResult.WireSize];
        MemoryMarshal.Write<SmsgEquipChangeResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgItemSlotStateAckTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgItemSlotStateAck.WireSize, Unsafe.SizeOf<SmsgItemSlotStateAck>());
    }

    [Fact]
    public void RoundTrip_ResultOk_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgItemSlotStateAck.WireSize];
        raw[0x08] = 0x01;
        raw[0x0A] = 0x02;
        raw[0x0B] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 0x0000000Au);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), 0x0000000Bu);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x18), 100);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x1C), 200);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x20), 300);

        var pkt = MemoryMarshal.Read<SmsgItemSlotStateAck>(raw);

        Assert.Equal((byte)0x01, pkt.Result);
        Assert.Equal((byte)0x02, pkt.FromSlot);
        Assert.Equal((byte)0x03, pkt.ToSlot);
        Assert.Equal(0x0000000Au, pkt.FlagC);
        Assert.Equal(0x0000000Bu, pkt.Flag10);
        Assert.Equal(100, pkt.BonusField1);
        Assert.Equal(200, pkt.BonusField2);
        Assert.Equal(300, pkt.BonusField3);

        byte[] out2 = new byte[SmsgItemSlotStateAck.WireSize];
        MemoryMarshal.Write<SmsgItemSlotStateAck>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ResultZero_NoApply_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgItemSlotStateAck.WireSize];
        raw[0x08] = 0x00;
        raw[0x0A] = 0x00;
        raw[0x0B] = 0x09;

        var pkt = MemoryMarshal.Read<SmsgItemSlotStateAck>(raw);

        Assert.Equal((byte)0x00, pkt.Result);
        Assert.Equal((byte)0x09, pkt.ToSlot);

        byte[] out2 = new byte[SmsgItemSlotStateAck.WireSize];
        MemoryMarshal.Write<SmsgItemSlotStateAck>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class CmsgStorageOpTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgStorageOp.Size, Unsafe.SizeOf<CmsgStorageOp>());
    }

    [Fact]
    public void RoundTrip_Op0_PreservesAllFields()
    {
        var pkt = new CmsgStorageOp
        {
            ContextId = 0x00001234u,
            Op = 0,
            Value = 500L
        };

        byte[] raw = new byte[CmsgStorageOp.Size];
        MemoryMarshal.Write<CmsgStorageOp>(raw, in pkt);

        var decoded = MemoryMarshal.Read<CmsgStorageOp>(raw);

        Assert.Equal(pkt.ContextId, decoded.ContextId);
        Assert.Equal(pkt.Op, decoded.Op);
        Assert.Equal(pkt.Value, decoded.Value);

        byte[] rebuf = new byte[CmsgStorageOp.Size];
        MemoryMarshal.Write<CmsgStorageOp>(rebuf, in decoded);
        Assert.Equal(raw, rebuf);
    }

    [Fact]
    public void RoundTrip_Op1_LargeValue_PreservesAllFields()
    {
        var pkt = new CmsgStorageOp
        {
            ContextId = 0xDEADBEEFu,
            Op = 1,
            Value = 9_999_999_990_000_000L
        };

        byte[] raw = new byte[CmsgStorageOp.Size];
        MemoryMarshal.Write<CmsgStorageOp>(raw, in pkt);

        var decoded = MemoryMarshal.Read<CmsgStorageOp>(raw);

        Assert.Equal(pkt.ContextId, decoded.ContextId);
        Assert.Equal(pkt.Op, decoded.Op);
        Assert.Equal(pkt.Value, decoded.Value);

        byte[] rebuf = new byte[CmsgStorageOp.Size];
        MemoryMarshal.Write<CmsgStorageOp>(rebuf, in decoded);
        Assert.Equal(raw, rebuf);
    }

    [Fact]
    public void ContextId_FieldLayout_ByteExact()
    {
        var pkt = new CmsgStorageOp { ContextId = 0x01020304u };
        byte[] raw = new byte[CmsgStorageOp.Size];
        MemoryMarshal.Write<CmsgStorageOp>(raw, in pkt);

        Assert.Equal(0x04, raw[0x00]);
        Assert.Equal(0x03, raw[0x01]);
        Assert.Equal(0x02, raw[0x02]);
        Assert.Equal(0x01, raw[0x03]);
    }

    [Fact]
    public void Value_FieldLayout_ByteExact()
    {
        var pkt = new CmsgStorageOp { Value = 1L };
        byte[] raw = new byte[CmsgStorageOp.Size];
        MemoryMarshal.Write<CmsgStorageOp>(raw, in pkt);

        Assert.Equal(0x01, raw[0x08]);
        for (int i = 0x09; i <= 0x0F; i++)
            Assert.Equal(0x00, raw[i]);
    }
}
