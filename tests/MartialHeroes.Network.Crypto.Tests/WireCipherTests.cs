using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Network.Crypto.Tests;

/// <summary>
/// Round-trip and invariant tests for the wire byte cipher.
/// NOTE: these are self-consistency tests (Decrypt∘Encrypt == identity, determinism, the
/// length-driven countdown counter). Capture-derived plaintext/ciphertext vectors are PENDING a
/// live .pcapng — the spec is capture_verified: false (Docs/RE/specs/crypto.md §1, §8.2).
/// </summary>
public sealed class WireCipherTests
{
    public static IEnumerable<object[]> Lengths()
    {
        // 0 = header-only pass-through; 1 = single byte; odd / even / large; the countdown counter
        // wraps at 256, so include lengths around and above 256 to exercise the 8-bit p wrap.
        int[] lengths = [0, 1, 2, 3, 7, 8, 15, 16, 31, 63, 64, 100, 127, 128, 255, 256, 257, 511, 1024, 11680];
        foreach (int n in lengths)
        {
            yield return [n];
        }
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Decrypt_Of_Encrypt_Is_Identity(int length)
    {
        byte[] original = MakePayload(length, seed: 1234);
        byte[] working = (byte[])original.Clone();

        WireCipher.EncryptInPlace(working);
        WireCipher.DecryptInPlace(working);

        Assert.Equal(original, working);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Encrypt_Is_Deterministic(int length)
    {
        byte[] a = MakePayload(length, seed: 99);
        byte[] b = (byte[])a.Clone();

        WireCipher.EncryptInPlace(a);
        WireCipher.EncryptInPlace(b);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Empty_Payload_Is_Passthrough()
    {
        Span<byte> empty = Span<byte>.Empty;
        WireCipher.EncryptInPlace(empty); // must not throw
        WireCipher.DecryptInPlace(empty);
        Assert.Equal(0, empty.Length);
    }

    [Fact]
    public void Single_Byte_Round_Trips_For_Every_Value()
    {
        for (int value = 0; value <= 0xFF; value++)
        {
            byte[] buffer = [(byte)value];
            WireCipher.EncryptInPlace(buffer);
            WireCipher.DecryptInPlace(buffer);
            Assert.Equal((byte)value, buffer[0]);
        }
    }

    [Fact]
    public void Encrypt_Generally_Changes_The_Payload()
    {
        // Sanity: the transform is non-trivial for non-empty input (not a no-op).
        byte[] original = MakePayload(64, seed: 7);
        byte[] working = (byte[])original.Clone();
        WireCipher.EncryptInPlace(working);
        Assert.NotEqual(original, working);
    }

    [Fact]
    public void Length_Affects_Ciphertext_Of_Identical_Prefix()
    {
        // The countdown counter p starts at the payload length, so the same leading bytes encipher
        // differently under different total lengths. This guards against the classic "p = index" bug.
        byte[] shortBuf = new byte[8];
        byte[] longBuf = new byte[16];
        // Identical first 8 bytes.
        for (int i = 0; i < 8; i++)
        {
            shortBuf[i] = (byte)(i + 1);
            longBuf[i] = (byte)(i + 1);
        }

        WireCipher.EncryptInPlace(shortBuf);
        WireCipher.EncryptInPlace(longBuf);

        Assert.NotEqual(shortBuf.AsSpan(0, 8).ToArray(), longBuf.AsSpan(0, 8).ToArray());
    }

    private static byte[] MakePayload(int length, int seed)
    {
        var rng = new Random(seed);
        byte[] buffer = new byte[length];
        rng.NextBytes(buffer);
        return buffer;
    }
}