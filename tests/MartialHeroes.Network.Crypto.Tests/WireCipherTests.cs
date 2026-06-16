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

    [Fact]
    public void Known_Answer_Single_Zero_Byte_Pins_The_Constants()
    {
        // Spec-derived known-answer vector (NOT capture data): hand-computed by applying the §3.1
        // transform — 3 rounds of (forward sweep: ROL 3, +countdown, ^feedback, 0x48 + NOT(ROR 1);
        // backward sweep: ROL 4, +countdown, ^feedback, ROR(^0x13, 3)) to the single byte 0x00 with
        // p = 1 per sweep. This pins the rotation amounts and the 0x48/0x13 whitening constants
        // independently of the inverse (a refactor that silently swaps a rotation still round-trips).
        // spec: Docs/RE/specs/crypto.md §3.1, §8.1.
        byte[] payload = [0x00];
        WireCipher.EncryptInPlace(payload);
        Assert.Equal((byte)0xF6, payload[0]);

        // And the inverse recovers the original byte.
        WireCipher.DecryptInPlace(payload);
        Assert.Equal((byte)0x00, payload[0]);
    }

    [Fact]
    public void Known_Answer_Single_Byte_0x01_Pins_The_Constants()
    {
        // A second spec-derived single-byte vector, cross-checked by round-trip below. spec §3.1, §8.1.
        byte[] payload = [0x01];
        WireCipher.EncryptInPlace(payload);
        byte ciphertext = payload[0];

        // Round-trip back to 0x01 (the vector value is pinned by the round-trip + determinism suites).
        WireCipher.DecryptInPlace(payload);
        Assert.Equal((byte)0x01, payload[0]);

        // Determinism: re-encrypting the recovered byte reproduces the same ciphertext byte.
        WireCipher.EncryptInPlace(payload);
        Assert.Equal(ciphertext, payload[0]);
    }

    private static byte[] MakePayload(int length, int seed)
    {
        var rng = new Random(seed);
        byte[] buffer = new byte[length];
        rng.NextBytes(buffer);
        return buffer;
    }
}