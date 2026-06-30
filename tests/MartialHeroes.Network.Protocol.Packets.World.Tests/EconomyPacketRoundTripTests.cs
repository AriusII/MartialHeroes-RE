using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgItemWorldPickupAckTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgItemWorldPickupAck.WireSize, Unsafe.SizeOf<SmsgItemWorldPickupAck>());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgItemWorldPickupAck.WireSize];
        raw[8] = 1;
        raw[9] = 101;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), 0xDEADBEEFu);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(16), 0x00000007u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(24), 0x12345678u);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(28), 5);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(32), 99);

        var pkt = MemoryMarshal.Read<SmsgItemWorldPickupAck>(raw);

        Assert.Equal((byte)1, pkt.Result);
        Assert.Equal((byte)101, pkt.Subtype);
        Assert.Equal(0xDEADBEEFu, pkt.WorldKey);
        Assert.Equal(0x00000007u, pkt.SlotParam);
        Assert.Equal(0x12345678u, pkt.ItemId);
        Assert.Equal(5, pkt.Count);
        Assert.Equal(99, pkt.Opaque);

        byte[] out2 = new byte[SmsgItemWorldPickupAck.WireSize];
        MemoryMarshal.Write<SmsgItemWorldPickupAck>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ResultZero_FailurePath()
    {
        byte[] raw = new byte[SmsgItemWorldPickupAck.WireSize];
        raw[8] = 0;
        raw[9] = 5;

        var pkt = MemoryMarshal.Read<SmsgItemWorldPickupAck>(raw);

        Assert.Equal((byte)0, pkt.Result);
        Assert.Equal((byte)5, pkt.Subtype);

        byte[] out2 = new byte[SmsgItemWorldPickupAck.WireSize];
        MemoryMarshal.Write<SmsgItemWorldPickupAck>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgActionErrorResultTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgActionErrorResult.WireSize, Unsafe.SizeOf<SmsgActionErrorResult>());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgActionErrorResult.WireSize];
        raw[8] = 0x01;
        raw[9] = 0x05;

        var pkt = MemoryMarshal.Read<SmsgActionErrorResult>(raw);

        Assert.Equal((byte)0x01, pkt.Status);
        Assert.Equal((byte)0x05, pkt.Error);

        byte[] out2 = new byte[SmsgActionErrorResult.WireSize];
        MemoryMarshal.Write<SmsgActionErrorResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgPlayerGoldBalanceUpdateTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgPlayerGoldBalanceUpdate.WireSize, Unsafe.SizeOf<SmsgPlayerGoldBalanceUpdate>());
    }

    [Fact]
    public void RoundTrip_PreservesGold()
    {
        byte[] raw = new byte[SmsgPlayerGoldBalanceUpdate.WireSize];
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(8), 9_999_999_990_000_000L);

        var pkt = MemoryMarshal.Read<SmsgPlayerGoldBalanceUpdate>(raw);

        Assert.Equal(9_999_999_990_000_000L, pkt.Gold);

        byte[] out2 = new byte[SmsgPlayerGoldBalanceUpdate.WireSize];
        MemoryMarshal.Write<SmsgPlayerGoldBalanceUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ZeroGold()
    {
        byte[] raw = new byte[SmsgPlayerGoldBalanceUpdate.WireSize];

        var pkt = MemoryMarshal.Read<SmsgPlayerGoldBalanceUpdate>(raw);

        Assert.Equal(0L, pkt.Gold);

        byte[] out2 = new byte[SmsgPlayerGoldBalanceUpdate.WireSize];
        MemoryMarshal.Write<SmsgPlayerGoldBalanceUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgItemShopBalanceUpdateTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgItemShopBalanceUpdate.WireSize, Unsafe.SizeOf<SmsgItemShopBalanceUpdate>());
    }

    [Fact]
    public void RoundTrip_SuccessPath_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgItemShopBalanceUpdate.WireSize];
        raw[8] = 1;
        raw[9] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(12), 500_000L);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(20), 1_200u);

        var pkt = MemoryMarshal.Read<SmsgItemShopBalanceUpdate>(raw);

        Assert.Equal((byte)1, pkt.Success);
        Assert.Equal((byte)0, pkt.FailCode);
        Assert.Equal(500_000L, pkt.Gold);
        Assert.Equal(1_200u, pkt.Points);

        byte[] out2 = new byte[SmsgItemShopBalanceUpdate.WireSize];
        MemoryMarshal.Write<SmsgItemShopBalanceUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_FailurePath_PreservesFailCode()
    {
        byte[] raw = new byte[SmsgItemShopBalanceUpdate.WireSize];
        raw[8] = 0;
        raw[9] = 3;

        var pkt = MemoryMarshal.Read<SmsgItemShopBalanceUpdate>(raw);

        Assert.Equal((byte)0, pkt.Success);
        Assert.Equal((byte)3, pkt.FailCode);

        byte[] out2 = new byte[SmsgItemShopBalanceUpdate.WireSize];
        MemoryMarshal.Write<SmsgItemShopBalanceUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgBillingBalanceUpdateTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgBillingBalanceUpdate.WireSize, Unsafe.SizeOf<SmsgBillingBalanceUpdate>());
    }

    [Fact]
    public void RoundTrip_Mode1_SetsBillingPoints()
    {
        byte[] raw = new byte[SmsgBillingBalanceUpdate.WireSize];
        raw[8] = 1;
        raw[9] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), 850u);

        var pkt = MemoryMarshal.Read<SmsgBillingBalanceUpdate>(raw);

        Assert.Equal((byte)1, pkt.Mode);
        Assert.Equal((byte)0, pkt.Submode);
        Assert.Equal(850u, pkt.BillingPoints);

        byte[] out2 = new byte[SmsgBillingBalanceUpdate.WireSize];
        MemoryMarshal.Write<SmsgBillingBalanceUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Mode0_InfoPanel_PreservesSubmode()
    {
        byte[] raw = new byte[SmsgBillingBalanceUpdate.WireSize];
        raw[8] = 0;
        raw[9] = 2;

        var pkt = MemoryMarshal.Read<SmsgBillingBalanceUpdate>(raw);

        Assert.Equal((byte)0, pkt.Mode);
        Assert.Equal((byte)2, pkt.Submode);

        byte[] out2 = new byte[SmsgBillingBalanceUpdate.WireSize];
        MemoryMarshal.Write<SmsgBillingBalanceUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class CmsgProductBuyTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgProductBuy.Size, Unsafe.SizeOf<CmsgProductBuy>());
    }

    [Fact]
    public void RoundTrip_Selector0_RegularShop()
    {
        var pkt = new CmsgProductBuy { Selector = 0 };
        byte[] raw = new byte[CmsgProductBuy.Size];
        MemoryMarshal.Write<CmsgProductBuy>(raw, in pkt);
        Assert.Equal((byte)0, raw[0]);

        var pkt2 = MemoryMarshal.Read<CmsgProductBuy>(raw);
        Assert.Equal((byte)0, pkt2.Selector);
    }

    [Fact]
    public void RoundTrip_Selector200_CashShop()
    {
        var pkt = new CmsgProductBuy { Selector = 200 };
        byte[] raw = new byte[CmsgProductBuy.Size];
        MemoryMarshal.Write<CmsgProductBuy>(raw, in pkt);
        Assert.Equal((byte)200, raw[0]);

        var pkt2 = MemoryMarshal.Read<CmsgProductBuy>(raw);
        Assert.Equal((byte)200, pkt2.Selector);
    }
}

public sealed class CmsgProductConfirmTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgProductConfirm.Size, Unsafe.SizeOf<CmsgProductConfirm>());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var pkt = new CmsgProductConfirm
        {
            SlotA = 1,
            SlotB = 2,
            ListSlot = 3,
            ProductionNpcIndex = 0xFF
        };
        byte[] raw = new byte[CmsgProductConfirm.Size];
        MemoryMarshal.Write<CmsgProductConfirm>(raw, in pkt);

        Assert.Equal((byte)1, raw[0]);
        Assert.Equal((byte)2, raw[1]);
        Assert.Equal((byte)3, raw[2]);
        Assert.Equal((byte)0xFF, raw[3]);

        var pkt2 = MemoryMarshal.Read<CmsgProductConfirm>(raw);
        Assert.Equal((byte)1, pkt2.SlotA);
        Assert.Equal((byte)2, pkt2.SlotB);
        Assert.Equal((byte)3, pkt2.ListSlot);
        Assert.Equal((byte)0xFF, pkt2.ProductionNpcIndex);
    }

    [Fact]
    public void RoundTrip_NoNpcIndex_0xFF_MapsToNone()
    {
        var pkt = new CmsgProductConfirm
        {
            SlotA = 0,
            SlotB = 0,
            ListSlot = 0,
            ProductionNpcIndex = 0xFF
        };
        byte[] raw = new byte[CmsgProductConfirm.Size];
        MemoryMarshal.Write<CmsgProductConfirm>(raw, in pkt);

        var pkt2 = MemoryMarshal.Read<CmsgProductConfirm>(raw);
        Assert.Equal((byte)0xFF, pkt2.ProductionNpcIndex);
    }
}

public sealed class SmsgCraftingResultTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgCraftingResult.WireSize, Unsafe.SizeOf<SmsgCraftingResult>());
    }

    [Fact]
    public void RoundTrip_Success_PreservesAllFields()
    {
        byte[] raw = new byte[SmsgCraftingResult.WireSize];
        raw[8]  = 1;
        raw[9]  = 0;
        raw[10] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(12), 0x00001111u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(16), 0x00002222u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(20), 0x00003333u);
        raw[32] = 5;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(36), 0x00004444u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(40), 0x00005555u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(44), 8u);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(48), 0x00006666u);

        var pkt = MemoryMarshal.Read<SmsgCraftingResult>(raw);

        Assert.Equal((byte)1, pkt.SuccessFlag);
        Assert.Equal((byte)0, pkt.ErrorCode);
        Assert.Equal((byte)1, pkt.ResultSubtype);
        Assert.Equal(0x00001111u, pkt.ResultValueA);
        Assert.Equal(0x00002222u, pkt.ResultValueB);
        Assert.Equal(0x00003333u, pkt.ResultValueC);
        Assert.Equal((byte)5, pkt.ProducedSlot);
        Assert.Equal(0x00004444u, pkt.ProducedItem0);
        Assert.Equal(0x00005555u, pkt.ProducedItem1);
        Assert.Equal(8u, pkt.ProducedItem2);
        Assert.Equal(0x00006666u, pkt.ProducedItem3);

        byte[] out2 = new byte[SmsgCraftingResult.WireSize];
        MemoryMarshal.Write<SmsgCraftingResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Failure_PreservesErrorCode()
    {
        byte[] raw = new byte[SmsgCraftingResult.WireSize];
        raw[8] = 0;
        raw[9] = 3;

        var pkt = MemoryMarshal.Read<SmsgCraftingResult>(raw);

        Assert.Equal((byte)0, pkt.SuccessFlag);
        Assert.Equal((byte)3, pkt.ErrorCode);

        byte[] out2 = new byte[SmsgCraftingResult.WireSize];
        MemoryMarshal.Write<SmsgCraftingResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgItemShopPurchaseResultTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgItemShopPurchaseResult.WireSize, Unsafe.SizeOf<SmsgItemShopPurchaseResult>());
    }

    [Fact]
    public void RoundTrip_Success_PreservesFields()
    {
        byte[] raw = new byte[SmsgItemShopPurchaseResult.WireSize];
        raw[8] = 1;
        raw[9] = 0;

        var pkt = MemoryMarshal.Read<SmsgItemShopPurchaseResult>(raw);

        Assert.Equal((byte)1, pkt.Success);
        Assert.Equal((byte)0, pkt.ResultCode);

        byte[] out2 = new byte[SmsgItemShopPurchaseResult.WireSize];
        MemoryMarshal.Write<SmsgItemShopPurchaseResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_Failure_PreservesResultCode()
    {
        byte[] raw = new byte[SmsgItemShopPurchaseResult.WireSize];
        raw[8] = 0;
        raw[9] = 2;

        var pkt = MemoryMarshal.Read<SmsgItemShopPurchaseResult>(raw);

        Assert.Equal((byte)0, pkt.Success);
        Assert.Equal((byte)2, pkt.ResultCode);

        byte[] out2 = new byte[SmsgItemShopPurchaseResult.WireSize];
        MemoryMarshal.Write<SmsgItemShopPurchaseResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgShopPageUpdateTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgShopPageUpdate.Size, Unsafe.SizeOf<SmsgShopPageUpdate>());
    }

    [Fact]
    public void RoundTrip_PreservesMoneyField()
    {
        var pkt = new SmsgShopPageUpdate { Money = 0x000F4240u };
        byte[] raw = new byte[SmsgShopPageUpdate.Size];
        MemoryMarshal.Write<SmsgShopPageUpdate>(raw, in pkt);

        var pkt2 = MemoryMarshal.Read<SmsgShopPageUpdate>(raw);
        Assert.Equal(0x000F4240u, pkt2.Money);
    }
}
