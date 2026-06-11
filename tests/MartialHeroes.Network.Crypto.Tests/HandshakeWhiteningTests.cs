using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Network.Crypto.Tests;

/// <summary>
/// Handshake reply per-dword XOR whitening (Docs/RE/specs/crypto.md §6.4, §8.1).
/// Self-consistency only; capture-derived vectors PENDING a live .pcapng.
/// </summary>
public sealed class HandshakeWhiteningTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]  // sub-dword: nothing whitened
    [InlineData(3)]  // sub-dword
    [InlineData(4)]  // one dword
    [InlineData(7)]  // one dword + 3 trailing untouched
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(1001)]
    public void XorWhiten_Is_An_Involution(int length)
    {
        byte[] original = MakePayload(length, seed: 808);
        byte[] working = (byte[])original.Clone();

        HandshakeWhitening.XorWhitenDwords(working);
        HandshakeWhitening.XorWhitenDwords(working);

        Assert.Equal(original, working);
    }

    [Fact]
    public void Trailing_Bytes_Below_A_Dword_Are_Left_Untouched()
    {
        // 6 bytes = 1 dword whitened + 2 trailing bytes unchanged. spec §6.4.
        byte[] buffer = [1, 2, 3, 4, 5, 6];
        byte[] expectedTrailing = [buffer[4], buffer[5]];

        HandshakeWhitening.XorWhitenDwords(buffer);

        Assert.Equal(expectedTrailing, new[] { buffer[4], buffer[5] });
        // First dword must have changed (key 0x29 in the low byte → byte[0] flips).
        Assert.NotEqual<byte>(1, buffer[0]);
    }

    [Fact]
    public void Key_Is_Applied_To_Low_Byte_Of_Each_Dword()
    {
        // Key pattern is 29 00 00 00 (LE): only the low byte of each dword changes by 0x29.
        // spec: Docs/RE/specs/crypto.md §6.4, §8.1.
        byte[] buffer = new byte[8]; // two zero dwords
        HandshakeWhitening.XorWhitenDwords(buffer);

        Assert.Equal(0x29, buffer[0]);
        Assert.Equal(0x00, buffer[1]);
        Assert.Equal(0x00, buffer[2]);
        Assert.Equal(0x00, buffer[3]);
        Assert.Equal(0x29, buffer[4]);
        Assert.Equal(0x00, buffer[5]);
        Assert.Equal(0x00, buffer[6]);
        Assert.Equal(0x00, buffer[7]);
    }

    private static byte[] MakePayload(int length, int seed)
    {
        var rng = new Random(seed);
        byte[] buffer = new byte[length];
        rng.NextBytes(buffer);
        return buffer;
    }
}
