using System.Buffers.Binary;
using System.Numerics;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Network.Crypto.Tests;

/// <summary>
/// Byte-exact composition of the secure <c>1/4</c> Auth-reply payload: the <c>0x2B</c> plaintext
/// pre-image (account + optional PIN, NUL-inclusive length prefixes) followed by the RSA ciphertext of
/// the fixed 17-byte staged password M, whitened over the WHOLE payload.
/// spec: Docs/RE/specs/crypto.md §6.6, §6.3, §6.4; packets/login.yaml (CmsgLoginCredential).
/// <para>
/// The RSA keypair, account/PIN/password bytes here are synthetic test fixtures, not capture data.
/// </para>
/// </summary>
public sealed class LoginCredentialReplyTests
{
    // Synthetic test primes giving a modulus n that is EXACTLY 39 bytes and exponent e (65537) EXACTLY
    // 3 bytes -> L1 + L2 = 42 (spec §6.2.1). Arbitrary, NOT recovered server values.
    private static readonly BigInteger P = BigInteger.Parse("75377541258354731458810898159183352769326586247");
    private static readonly BigInteger Q = BigInteger.Parse("48710038997288231143179367274763024050866548859");

    private static (SessionHandshake.KeyExchange Key, BigInteger D, BigInteger N) MakeKey()
    {
        BigInteger n = P * Q;
        BigInteger phi = (P - 1) * (Q - 1);
        BigInteger e = 65537;
        BigInteger d = ModInverse(e, phi);
        int modulusBytes = n.ToByteArray(isUnsigned: true, isBigEndian: true).Length;
        var key = new SessionHandshake.KeyExchange(n, e, modulusBytes, scalar1: 0, scalar2: 0);
        return (key, d, n);
    }

    [Fact]
    public void Build_Lays_Out_PreImage_Byte_Exact_Without_Pin()
    {
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        ReadOnlySpan<byte> account = "acct"u8; // 4 bytes -> account_len = 5 (NUL incl.)

        byte[] payload = LoginCredentialReply.Build(
            in key, account, pin: default, includePin: false, staged, new ConstPaddingRandom(0x5A));

        // De-whiten to inspect the plaintext pre-image. spec §6.4 (involution).
        byte[] plain = (byte[])payload.Clone();
        HandshakeWhitening.XorWhitenDwords(plain);

        int i = 0;
        Assert.Equal(0x2B, plain[i++]); // sub-opcode. spec login.yaml off 0x00.
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i)));
        i += 4; // account_len = strlen+1
        Assert.True(plain.AsSpan(i, 4).SequenceEqual(account));
        i += 4;
        Assert.Equal(0x00, plain[i++]); // account trailing NUL
        // No PIN region; the ciphertext region follows immediately.
        uint cipherLen = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i));
        i += 4;
        Assert.Equal((uint)(plain.Length - i), cipherLen);
    }

    [Fact]
    public void Build_Includes_Optional_Pin_Region_When_Gated()
    {
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        ReadOnlySpan<byte> account = "abc"u8; // account_len = 4
        ReadOnlySpan<byte> pin = "1234"u8; // pin_len = 5

        byte[] payload = LoginCredentialReply.Build(
            in key, account, pin, includePin: true, staged, new ConstPaddingRandom(0x33));

        byte[] plain = (byte[])payload.Clone();
        HandshakeWhitening.XorWhitenDwords(plain);

        int i = 0;
        Assert.Equal(0x2B, plain[i++]);
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i)));
        i += 4;
        Assert.True(plain.AsSpan(i, 3).SequenceEqual(account));
        i += 3;
        Assert.Equal(0x00, plain[i++]);
        // PIN region (a7-gated).
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i)));
        i += 4;
        Assert.True(plain.AsSpan(i, 4).SequenceEqual(pin));
        i += 4;
        Assert.Equal(0x00, plain[i++]);
        // Then the ciphertext region.
        uint cipherLen = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i));
        i += 4;
        Assert.Equal((uint)(plain.Length - i), cipherLen);
    }

    [Fact]
    public void Build_Matches_The_Debugger_Example_Sizes_With_Pin()
    {
        // login.yaml example: AccountLength = 8 (7-char + NUL), PinLength = 5 (4-char + NUL).
        // Pre-image written = 1 + 4 + 8 + 4 + 5 = 22 bytes (payload offset). spec login.yaml notes.
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        byte[] account = "account"u8.ToArray(); // 7 chars -> account_len 8
        byte[] pin = "1234"u8.ToArray(); // 4 chars -> pin_len 5

        byte[] payload = LoginCredentialReply.Build(
            in key, account, pin, includePin: true, staged, new ConstPaddingRandom(0x11));

        byte[] plain = (byte[])payload.Clone();
        HandshakeWhitening.XorWhitenDwords(plain);

        // pre-image length = 22; ciphertext region = 4 + L. The ciphertext region begins at offset 22.
        uint cipherLen = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(22));
        Assert.Equal((uint)(plain.Length - 22 - 4), cipherLen);
    }

    [Fact]
    public void Build_Whitens_The_Whole_Payload_And_Whitening_Is_Involution()
    {
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);

        byte[] payload = LoginCredentialReply.Build(
            in key, "acct"u8, default, includePin: false, staged, new ConstPaddingRandom(0x77));

        byte[] dewhitened = (byte[])payload.Clone();
        HandshakeWhitening.XorWhitenDwords(dewhitened);

        // Whitening actually changed the bytes (0x2B ^ 0x29 = 0x02 in the very first byte).
        Assert.NotEqual(payload, dewhitened);
        Assert.Equal((byte)(0x2B ^ 0x29), payload[0]); // first dword low byte whitened. spec §6.4.

        // Re-applying whitening restores the original (XOR involution). spec §6.4.
        HandshakeWhitening.XorWhitenDwords(dewhitened);
        Assert.Equal(payload, dewhitened);
    }

    [Fact]
    public void Build_RsaHalf_Independently_Decrypts_To_The_Full_17_Byte_M()
    {
        (SessionHandshake.KeyExchange key, BigInteger d, BigInteger n) = MakeKey();

        ReadOnlySpan<byte> password = "secret"u8; // 6 bytes
        byte[] staged = CredentialPlaintext.StagePassword(password); // 17-byte M

        byte[] payload = LoginCredentialReply.Build(
            in key, "acct"u8, default, includePin: false, staged, new ConstPaddingRandom(0x42));

        // De-whiten, skip the pre-image, read [u32 LE len][BE ciphertext].
        byte[] plain = (byte[])payload.Clone();
        HandshakeWhitening.XorWhitenDwords(plain);

        int preImage = 1 + 4 + (4 + 1); // 0x2B + account_len + ("acct" + NUL)
        uint cipherLen = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(preImage));
        var c = new BigInteger(plain.AsSpan(preImage + 4, (int)cipherLen), isUnsigned: true, isBigEndian: true);

        // RSA-decrypt with the private exponent, restore the k-1 block, strip PKCS#1 type-2.
        BigInteger m = BigInteger.ModPow(c, d, n);
        int blockLength = key.ModulusByteLength - 1;
        byte[] block = ToFixedBigEndian(m, blockLength);
        byte[] recoveredM = StripPkcs1Type2(block);

        // The recovered plaintext is the FULL 17-byte M: password then zero padding. spec §6.1, §6.6.
        Assert.Equal(17, recoveredM.Length);
        Assert.True(recoveredM.AsSpan(0, password.Length).SequenceEqual(password));
        for (int i = password.Length; i < 17; i++)
        {
            Assert.Equal(0, recoveredM[i]);
        }
    }

    [Fact]
    public void Build_Rejects_A_Staged_Password_Smaller_Than_The_Minimum_Field_Cap()
    {
        // The staged M width is parameter-driven (the password-field cap), but must still hold a 2-char
        // password plus a trailing zero (>= 3 bytes). A 2-byte M is rejected. spec §6.6, §8.1.
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        Assert.Throws<ArgumentException>(() => LoginCredentialReply.Build(
            in key, "acct"u8, default, includePin: false, new byte[2], new ConstPaddingRandom(1)));
    }

    [Fact]
    public void Build_Accepts_A_Non_Default_Field_Cap_Staged_Password()
    {
        // A caller-supplied 16-byte field cap is valid: width is parameter-driven, not fixed at 17. §6.6.
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8, fieldCap: 16);
        byte[] payload = LoginCredentialReply.Build(
            in key, "acct"u8, default, includePin: false, staged, new ConstPaddingRandom(0x42));
        Assert.NotEmpty(payload);
    }

    [Fact]
    public void Build_Rejects_An_Account_Shorter_Than_Two_Characters()
    {
        // Client-side account gate: >= 2 chars. spec: crypto.md §6.1; login.yaml (AccountLength >= 2).
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        Assert.Throws<ArgumentException>(() => LoginCredentialReply.Build(
            in key, "a"u8, default, includePin: false, staged, new ConstPaddingRandom(1)));
    }

    [Fact]
    public void Build_Rejects_An_Account_At_Or_Over_The_Field_Cap()
    {
        // NUL-inclusive account length must be < 20. A 19-char account => length 20, rejected.
        // spec: crypto.md §6.1; login.yaml (AccountLength < 20).
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        byte[] account = new byte[19]; // 19 chars -> NUL-inclusive length 20 == MaxAccountLengthExclusive
        Array.Fill(account, (byte)'a');
        Assert.Throws<ArgumentException>(() => LoginCredentialReply.Build(
            in key, account, default, includePin: false, staged, new ConstPaddingRandom(1)));
    }

    [Fact]
    public void Build_Rejects_A_Pin_At_Or_Over_The_Field_Cap()
    {
        // PIN entry is capped to four digits; the emitted length prefix then includes the NUL (5).
        // spec: login_flow.md §4.2a / §7; packets/login.yaml PinLength.
        (SessionHandshake.KeyExchange key, _, _) = MakeKey();
        byte[] staged = CredentialPlaintext.StagePassword("pw"u8);
        Assert.Throws<ArgumentException>(() => LoginCredentialReply.Build(
            in key, "acct"u8, "12345"u8, includePin: true, staged, new ConstPaddingRandom(1)));
    }

    // --- helpers (synthetic; not capture data) ----------------------------------------------------

    private sealed class ConstPaddingRandom(byte value) : IPaddingRandom
    {
        // Constant non-zero byte -> no nonzero-retry loop, fully deterministic PS.
        public void Fill(Span<byte> destination) => destination.Fill(value == 0 ? (byte)1 : value);
    }

    private static byte[] ToFixedBigEndian(BigInteger value, int length)
    {
        byte[] minimal = value.IsZero ? [0] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] result = new byte[length];
        minimal.CopyTo(result.AsSpan(length - minimal.Length));
        return result;
    }

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
}