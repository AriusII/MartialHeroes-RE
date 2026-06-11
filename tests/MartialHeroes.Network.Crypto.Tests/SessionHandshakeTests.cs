using System.Buffers.Binary;
using System.Numerics;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Network.Crypto.Tests;

/// <summary>
/// End-to-end handshake build/parse round-trip (Docs/RE/specs/crypto.md §6, §8.1). No capture needed:
/// we synthesize a small RSA keypair, serialize a 0/0 payload, parse it back, build the 1/4 reply with
/// a deterministic RNG, then independently RSA-decrypt with the private exponent and strip the PKCS#1
/// type-2 padding to recover the exact credential bytes. This proves the whole handshake build.
/// <para>
/// The keypair, payload bytes, and credential here are <b>synthetic test fixtures</b>, not capture data.
/// Concrete server n/e remain capture-only (§8.2) but the algorithm is fully exercised.
/// </para>
/// </summary>
public sealed class SessionHandshakeTests
{
    // A fixed small RSA keypair built from two primes, chosen so the modulus n is EXACTLY 39 bytes and
    // the exponent e (65537) is EXACTLY 3 bytes — giving L1 + L2 = 39 + 3 = 42, the spec's hard
    // invariant (§6.2.1), and a ~312-bit envelope mirroring §6.3's ~40-byte modulus. These are
    // arbitrary test primes, NOT recovered server values. spec §6.3, §8.2.
    private static readonly BigInteger P = BigInteger.Parse("75377541258354731458810898159183352769326586247");
    private static readonly BigInteger Q = BigInteger.Parse("48710038997288231143179367274763024050866548859");

    private static (BigInteger N, BigInteger E, BigInteger D) MakeKeyPair()
    {
        BigInteger n = P * Q;
        BigInteger phi = (P - 1) * (Q - 1);
        BigInteger e = 65537;
        BigInteger d = ModInverse(e, phi);
        return (n, e, d);
    }

    [Fact]
    public void Build_Then_Independently_Decrypt_Recovers_The_Exact_Credential()
    {
        (BigInteger n, BigInteger e, BigInteger d) = MakeKeyPair();

        byte[] credential = "hunter2-secret"u8.ToArray();

        // --- Synthesize a 0/0 payload from (n, e), then PARSE it and assert n/e/scalars recovered. ---
        byte[] nDigits = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] eDigits = e.ToByteArray(isUnsigned: true, isBigEndian: true);
        uint scalar1 = 0xDEADBEEF;
        uint scalar2 = 0x0BADF00D;

        byte[] payload = BuildKeyExchangePayload(nDigits, eDigits, scalar1, scalar2);

        SessionHandshake.KeyExchange parsed = SessionHandshake.ParseKeyExchange(payload);

        Assert.Equal(n, parsed.Modulus);
        Assert.Equal(e, parsed.Exponent);
        Assert.Equal(nDigits.Length, parsed.ModulusByteLength);
        Assert.Equal(scalar1, parsed.Scalar1);
        Assert.Equal(scalar2, parsed.Scalar2);

        // --- BUILD the 1/4 reply with a fixed (deterministic) RNG. ---
        var rng = new SequentialPaddingRandom(start: 1); // non-zero stream → no nonzero retries
        byte[] reply = SessionHandshake.BuildAuthReply(in parsed, credential, rng);

        // --- Undo the whitening (involution) to get [u32 LE len(c)] ‖ BE digits of c. ---
        byte[] body = (byte[])reply.Clone();
        HandshakeWhitening.XorWhitenDwords(body); // re-apply XOR to de-whiten. spec §6.4.

        uint cipherLen = BinaryPrimitives.ReadUInt32LittleEndian(body);
        Assert.Equal((uint)(body.Length - 4), cipherLen);

        BigInteger c = new BigInteger(body.AsSpan(4, (int)cipherLen), isUnsigned: true, isBigEndian: true);

        // --- Independently RSA-decrypt with the private exponent: m = c^d mod n. ---
        BigInteger m = BigInteger.ModPow(c, d, n);

        // m is the padded block as a big-endian integer; restore it to k − 1 bytes and strip PKCS#1.
        int blockLength = parsed.ModulusByteLength - 1;
        byte[] recoveredBlock = ToFixedBigEndian(m, blockLength);

        byte[] recoveredCredential = StripPkcs1Type2(recoveredBlock);

        Assert.Equal(credential, recoveredCredential);
    }

    [Fact]
    public void Reply_Is_Whitened_Whitening_Is_An_Involution()
    {
        (BigInteger n, BigInteger e, _) = MakeKeyPair();
        byte[] payload = BuildKeyExchangePayload(
            n.ToByteArray(isUnsigned: true, isBigEndian: true),
            e.ToByteArray(isUnsigned: true, isBigEndian: true),
            0, 0);
        SessionHandshake.KeyExchange parsed = SessionHandshake.ParseKeyExchange(payload);

        var rng = new SequentialPaddingRandom(start: 7);
        byte[] reply = SessionHandshake.BuildAuthReply(in parsed, "pw"u8, rng);

        // De-whiten then re-whiten returns the same bytes (XOR involution). spec §6.4.
        byte[] once = (byte[])reply.Clone();
        HandshakeWhitening.XorWhitenDwords(once);
        Assert.NotEqual(reply, once); // whitening actually changed the body (length prefix low byte ^ 0x29)
        HandshakeWhitening.XorWhitenDwords(once);
        Assert.Equal(reply, once);
    }

    [Fact]
    public void Build_Fails_When_Credential_Too_Long_For_The_Modulus()
    {
        (BigInteger n, BigInteger e, _) = MakeKeyPair();
        byte[] nDigits = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] payload = BuildKeyExchangePayload(nDigits, e.ToByteArray(isUnsigned: true, isBigEndian: true), 0, 0);
        SessionHandshake.KeyExchange parsed = SessionHandshake.ParseKeyExchange(payload);

        // Leaving < 8 PS bytes must fail. Block length is k − 1; max credential = (k-1) - 2 - 8.
        int maxCredential = (parsed.ModulusByteLength - 1) - 2 - 8;
        byte[] tooLong = new byte[maxCredential + 1];

        Assert.Throws<ArgumentException>(() =>
            SessionHandshake.BuildAuthReply(in parsed, tooLong, new SequentialPaddingRandom(1)));
    }

    [Fact]
    public void Parse_Rejects_Wrong_Payload_Size()
    {
        Assert.Throws<ArgumentException>(() => SessionHandshake.ParseKeyExchange(new byte[61]));
        Assert.Throws<ArgumentException>(() => SessionHandshake.ParseKeyExchange(new byte[63]));
    }

    [Fact]
    public void Parse_Rejects_Blob_Where_L1_Plus_L2_Is_Not_42()
    {
        // Hand-craft a 62-byte payload with L1=20, L2=20 (sum 40 ≠ 42) but otherwise sized to fit.
        byte[] payload = new byte[SessionHandshake.KeyExchangePayloadSize];
        int cursor = 4; // skip headerA + headerB
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), 20); cursor += 4 + 20;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), 20); cursor += 4 + 20;
        // cursor now = 4 + 4 + 20 + 4 + 20 = 52, leaving 10 bytes — not the 8 scalar bytes, so this
        // throws on the scalar tail check; either way it must reject. spec §6.2.1.
        Assert.Throws<ArgumentException>(() => SessionHandshake.ParseKeyExchange(payload));
    }

    // --- Test helpers (synthetic; not capture data) -------------------------------------------------

    /// <summary>Serializes a synthetic 0/0 payload per §6.2.1: headerA(2)‖headerB(2)‖[LE L1]‖n‖[LE L2]‖e‖s1‖s2.</summary>
    private static byte[] BuildKeyExchangePayload(byte[] nDigits, byte[] eDigits, uint scalar1, uint scalar2)
    {
        byte[] payload = new byte[SessionHandshake.KeyExchangePayloadSize];
        // header A / header B opaque tags — arbitrary bytes; the parser ignores them. §6.2.2.
        payload[0] = 0xAB; payload[1] = 0xCD; payload[2] = 0xEF; payload[3] = 0x01;

        int cursor = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), (uint)nDigits.Length);
        cursor += 4;
        nDigits.CopyTo(payload.AsSpan(cursor));
        cursor += nDigits.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), (uint)eDigits.Length);
        cursor += 4;
        eDigits.CopyTo(payload.AsSpan(cursor));
        cursor += eDigits.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), scalar1);
        cursor += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(cursor), scalar2);
        return payload;
    }

    /// <summary>Renders a non-negative BigInteger as exactly <paramref name="length"/> big-endian bytes (left zero-padded).</summary>
    private static byte[] ToFixedBigEndian(BigInteger value, int length)
    {
        byte[] minimal = value.IsZero ? [0] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (minimal.Length > length)
        {
            throw new InvalidOperationException("value wider than the block");
        }

        byte[] result = new byte[length];
        minimal.CopyTo(result.AsSpan(length - minimal.Length));
        return result;
    }

    /// <summary>Strips PKCS#1 v1.5 type-2 padding from a (k − 1)-byte block: 0x02 ‖ PS ‖ 0x00 ‖ M → M. §6.3.</summary>
    private static byte[] StripPkcs1Type2(ReadOnlySpan<byte> block)
    {
        Assert.Equal(0x02, block[0]);
        int sep = -1;
        for (int i = 1; i < block.Length; i++)
        {
            if (block[i] == 0x00)
            {
                sep = i;
                break;
            }
        }

        Assert.True(sep >= 1 + 8, "PS must be at least 8 non-zero bytes before the 0x00 separator");
        return block[(sep + 1)..].ToArray();
    }

    /// <summary>Extended-Euclid modular inverse for building the test private exponent d = e^-1 mod phi.</summary>
    private static BigInteger ModInverse(BigInteger a, BigInteger modulus)
    {
        BigInteger t = 0, newT = 1;
        BigInteger r = modulus, newR = a;
        while (newR != 0)
        {
            BigInteger quotient = r / newR;
            (t, newT) = (newT, t - quotient * newT);
            (r, newR) = (newR, r - quotient * newR);
        }

        if (t < 0)
        {
            t += modulus;
        }

        return t;
    }

    /// <summary>
    /// Deterministic <see cref="IPaddingRandom"/>: emits a fixed, repeatable non-zero byte stream so the
    /// PKCS#1 padding (and therefore the whole reply) is reproducible across test runs.
    /// </summary>
    private sealed class SequentialPaddingRandom(byte start) : IPaddingRandom
    {
        private byte _next = start;

        public void Fill(Span<byte> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                if (_next == 0)
                {
                    _next = 1;
                }

                destination[i] = _next++;
            }
        }
    }
}
