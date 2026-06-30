using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MartialHeroes.Network.Protocol.Packets;
using MartialHeroes.Network.Protocol.Packets.World.Packets;
using Xunit;

namespace MartialHeroes.Network.Protocol.Packets.World.Tests;

public sealed class SmsgCubeGambleResultTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgCubeGambleResult.WireSize, Unsafe.SizeOf<SmsgCubeGambleResult>());
    }

    [Fact]
    public void RoundTrip_BetEcho_SubKind1()
    {
        byte[] raw = new byte[SmsgCubeGambleResult.WireSize];
        raw[0x08] = 1;
        raw[0x09] = 0;
        raw[0x0A] = 15;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 500u);

        var pkt = MemoryMarshal.Read<SmsgCubeGambleResult>(raw);

        Assert.Equal((byte)1, pkt.SubKind);
        Assert.Equal((byte)0, pkt.ResultCode);
        Assert.Equal((byte)15, pkt.BetType);
        Assert.Equal(500u, pkt.Wager);

        byte[] out2 = new byte[SmsgCubeGambleResult.WireSize];
        MemoryMarshal.Write<SmsgCubeGambleResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void RoundTrip_ResultAnnounce_SubKindNot1()
    {
        byte[] raw = new byte[SmsgCubeGambleResult.WireSize];
        raw[0x08] = 2;
        raw[0x09] = 3;
        raw[0x0A] = 0;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 0u);

        var pkt = MemoryMarshal.Read<SmsgCubeGambleResult>(raw);

        Assert.Equal((byte)2, pkt.SubKind);
        Assert.Equal((byte)3, pkt.ResultCode);
        Assert.Equal((byte)0, pkt.BetType);
        Assert.Equal(0u, pkt.Wager);

        byte[] out2 = new byte[SmsgCubeGambleResult.WireSize];
        MemoryMarshal.Write<SmsgCubeGambleResult>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}

public sealed class SmsgCubeGambleReelUpdateTests
{
    [Fact]
    public void WireSize_EqualsUnsafeSizeOf()
    {
        Assert.Equal(SmsgCubeGambleReelUpdate.WireSize, Unsafe.SizeOf<SmsgCubeGambleReelUpdate>());
    }

    [Fact]
    public void RoundTrip_Phase5_SettlesMoneyAndBoardState()
    {
        byte[] raw = new byte[SmsgCubeGambleReelUpdate.WireSize];
        raw[0x08] = 5;
        raw[0x09] = 0;
        raw[0x0A] = 0x35;
        raw[0x0B] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x0C), 7u);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(0x10), 1_000_000uL);
        for (int i = 0; i < 5; i++) raw[0x18 + i] = (byte)(i + 1);
        for (int i = 0; i < 5; i++) raw[0x1D + i] = (byte)(i + 10);
        for (int i = 0; i < 152; i++) raw[0x24 + i] = (byte)(i & 0xFF);

        var pkt = MemoryMarshal.Read<SmsgCubeGambleReelUpdate>(raw);

        Assert.Equal((byte)5, pkt.Phase);
        Assert.Equal((byte)0, pkt.SpinSubKind);
        Assert.Equal((byte)0x35, pkt.ReelDigitPack);
        Assert.Equal(7u, pkt.ThrowValue);
        Assert.Equal(1_000_000uL, pkt.SettledMoney);

        ReadOnlySpan<byte> histSpan = pkt.ReelHistoryPack;
        for (int i = 0; i < 5; i++) Assert.Equal((byte)(i + 1), histSpan[i]);

        ReadOnlySpan<byte> idxSpan = pkt.ReelIndexPack;
        for (int i = 0; i < 5; i++) Assert.Equal((byte)(i + 10), idxSpan[i]);

        ReadOnlySpan<byte> boardSpan = pkt.BoardState;
        for (int i = 0; i < 152; i++) Assert.Equal((byte)(i & 0xFF), boardSpan[i]);

        byte[] out2 = new byte[SmsgCubeGambleReelUpdate.WireSize];
        MemoryMarshal.Write<SmsgCubeGambleReelUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void ReelDigitPack_ResetSentinel_0xFF_PreservedInRoundTrip()
    {
        byte[] raw = new byte[SmsgCubeGambleReelUpdate.WireSize];
        raw[0x08] = 3;
        raw[0x0A] = 0xFF;

        var pkt = MemoryMarshal.Read<SmsgCubeGambleReelUpdate>(raw);

        Assert.Equal((byte)3, pkt.Phase);
        Assert.Equal((byte)0xFF, pkt.ReelDigitPack);

        byte[] out2 = new byte[SmsgCubeGambleReelUpdate.WireSize];
        MemoryMarshal.Write<SmsgCubeGambleReelUpdate>(out2, in pkt);
        Assert.Equal(raw, out2);
    }

    [Fact]
    public void BoardState_Length_Is152()
    {
        var pkt = default(SmsgCubeGambleReelUpdate);
        ReadOnlySpan<byte> boardSpan = pkt.BoardState;
        Assert.Equal(152, boardSpan.Length);
    }
}

public sealed class CmsgCubeGambleSubmitTests
{
    [Fact]
    public void Size_EqualsUnsafeSizeOf()
    {
        Assert.Equal(CmsgCubeGambleSubmit.Size, Unsafe.SizeOf<CmsgCubeGambleSubmit>());
    }

    [Fact]
    public void RoundTrip_BetSheet_PreservesAllRegions()
    {
        byte[] raw = new byte[CmsgCubeGambleSubmit.Size];
        for (int i = 0; i < 56; i++) raw[i] = (byte)(i + 1);
        for (int i = 0; i < 16; i++) raw[56 + i] = (byte)(i + 100);
        raw[72] = 3;

        var pkt = MemoryMarshal.Read<CmsgCubeGambleSubmit>(raw);

        ReadOnlySpan<byte> amtSpan = pkt.BetAmounts;
        for (int i = 0; i < 56; i++) Assert.Equal((byte)(i + 1), amtSpan[i]);

        ReadOnlySpan<byte> lineSpan = pkt.BetLines;
        for (int i = 0; i < 16; i++) Assert.Equal((byte)(i + 100), lineSpan[i]);

        Assert.Equal((byte)3, pkt.DealerTableIndex);

        byte[] out2 = new byte[CmsgCubeGambleSubmit.Size];
        MemoryMarshal.Write<CmsgCubeGambleSubmit>(out2, in pkt);
        Assert.Equal(raw, out2);
    }
}
