using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Network.Crypto.Tests;

/// <summary>
/// LZ4 stage + full outbound/inbound pipeline round-trips.
/// NOTE: self-consistency only; capture-derived vectors PENDING a live .pcapng
/// (Docs/RE/specs/crypto.md §1, §8.2). The inbound-cipher presence is itself unverified (§5);
/// these tests model the symmetric reference path (cipher then LZ4, reversed to recover plaintext).
/// </summary>
public sealed class PayloadPipelineTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(100)]
    [InlineData(512)]
    [InlineData(4096)]
    [InlineData(11680)]
    public void Lz4_Compress_Then_Decompress_Is_Identity(int length)
    {
        byte[] original = MakePayload(length, seed: 555);

        using var compressed = PayloadCompression.CompressPayload(original, out int compLen);
        using var restored = PayloadCompression.DecompressPayload(compressed.Memory.Span[..compLen], out int decLen);

        Assert.Equal(length, decLen);
        Assert.Equal(original, restored.Memory.Span[..decLen].ToArray());
    }

    [Fact]
    public void Lz4_Highly_Compressible_Payload_Shrinks()
    {
        byte[] zeros = new byte[4096]; // all zeros compress strongly
        using var compressed = PayloadCompression.CompressPayload(zeros, out int compLen);
        Assert.True(compLen < zeros.Length);
    }

    [Fact]
    public void Decompress_Into_Caller_Buffer_Round_Trips()
    {
        byte[] original = MakePayload(2000, seed: 4242);
        using var compressed = PayloadCompression.CompressPayload(original, out int compLen);

        byte[] dest = new byte[original.Length];
        int written = PayloadCompression.DecompressPayloadInto(compressed.Memory.Span[..compLen], dest);

        Assert.Equal(original.Length, written);
        Assert.Equal(original, dest);
    }

    [Fact]
    public void Inbound_Cap_Constant_Matches_Spec()
    {
        // spec: Docs/RE/specs/crypto.md §3.2, §8.1 (0x2DA0 = 11680).
        Assert.Equal(0x2DA0, PayloadCompression.InboundMaxDecompressedSize);
        Assert.Equal(11680, PayloadCompression.InboundMaxDecompressedSize);
    }

    [Fact]
    public void Decompress_Rejects_Malformed_Block()
    {
        // Garbage that is not a valid LZ4 raw block should fault rather than silently produce data.
        byte[] garbage = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var owner = PayloadCompression.DecompressPayload(garbage, out _);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(256)]
    [InlineData(4000)]
    public void Full_Outbound_Then_Reverse_Recovers_Plaintext(int length)
    {
        // Reference symmetric pipeline: plaintext → cipher → LZ4 ; reverse: LZ4 → inverse cipher.
        byte[] plaintext = MakePayload(length, seed: 31337);

        // Outbound.
        byte[] ciphered = (byte[])plaintext.Clone();
        WireCipher.EncryptInPlace(ciphered);
        using var onWire = PayloadCompression.CompressPayload(ciphered, out int wireLen);

        // Inbound (server-side / reference): decompress, then inverse cipher.
        using var decompressed = PayloadCompression.DecompressPayload(onWire.Memory.Span[..wireLen], out int decLen);
        byte[] recovered = decompressed.Memory.Span[..decLen].ToArray();
        WireCipher.DecryptInPlace(recovered);

        Assert.Equal(plaintext, recovered);
    }

    private static byte[] MakePayload(int length, int seed)
    {
        var rng = new Random(seed);
        byte[] buffer = new byte[length];
        rng.NextBytes(buffer);
        return buffer;
    }
}