using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace MartialHeroes.Network.Crypto;

/// <summary>
/// Session key-exchange handshake (reserved opcode major 0 / minor 0 inbound, with the Auth reply on
/// major 1 / minor 4 outbound). This is a <b>custom big-integer public-key exchange</b> (textbook
/// RSA encryption of the staged login credential toward a server-supplied public key) that is
/// entirely separate from the wire byte cipher and does <b>not</b> key it.
/// <para>
/// Two pieces are implemented from the pinned spec: (1) parsing the server's 62-byte 0/0 KeyExchange
/// payload into a runtime <see cref="KeyExchange"/> (modulus, exponent, two scalars) — every value is
/// read LIVE from the wire, nothing is hardcoded; and (2) building the 1/4 Auth reply body
/// (PKCS#1 v1.5 type-2 pad → modexp → serialize → per-dword whitening). The reply body is then handed
/// to the normal outbound send path (byte cipher + LZ4), which callers already own.
/// </para>
/// <para>
/// Capture-only items that remain notes (NOT blockers for this build per §8.2): the concrete server
/// <c>n</c>/<c>e</c> values and the individual <c>L1</c>/<c>L2</c> split (server wire data, read live
/// here), the semantics of the two server scalars, and whether the client inbound path generalizes to
/// "no inverse cipher" across multiple packet types (§5). None of these block the algorithm below.
/// </para>
/// spec: Docs/RE/specs/crypto.md §6, §8.1.
/// </summary>
public static class SessionHandshake
{
    /// <summary>
    /// Total 0/0 key-exchange payload size (after inbound LZ4-decompress, no inverse cipher):
    /// 54-byte key blob + two 4-byte scalars.
    /// spec: Docs/RE/specs/crypto.md §6.2, §8.1.
    /// </summary>
    public const int KeyExchangePayloadSize = 62;

    /// <summary>
    /// Size the 54-byte key blob asserts on the wire.
    /// spec: Docs/RE/specs/crypto.md §6.2, §8.1.
    /// </summary>
    public const int KeyBlobSize = 54;

    /// <summary>
    /// Fixed sum of the two bignum digit-array lengths inside the key blob (modulus + exponent).
    /// The individual <c>L1</c>/<c>L2</c> split is server wire data, not a client constant.
    /// spec: Docs/RE/specs/crypto.md §6.2.1, §8.1 (L1 + L2 = 42).
    /// </summary>
    public const int ModulusPlusExponentByteLength = 42;

    // Per-value 2-byte serialization tags (header A / header B). Opaque — stored but never read; they
    // do not drive bignum reconstruction or the reply. spec: Docs/RE/specs/crypto.md §6.2.1, §6.2.2.
    private const int HeaderTagBytes = 2;

    // u32 length prefixes (L1, L2, and the reply length) are little-endian.
    // spec: Docs/RE/specs/crypto.md §6.2.3.
    private const int LengthPrefixBytes = sizeof(uint);

    // Trailing two 4-byte server scalars inside the 0/0 payload.
    // spec: Docs/RE/specs/crypto.md §6.2.
    private const int ScalarBytes = sizeof(uint);

    // PKCS#1 v1.5 block-type-2 marker. spec: Docs/RE/specs/crypto.md §6.3 (0x02 ‖ PS ‖ 0x00 ‖ M).
    private const byte BlockType2Marker = 0x02;

    // Single zero separator between PS and the message. spec: Docs/RE/specs/crypto.md §6.3.
    private const byte PaddingSeparator = 0x00;

    // Minimum padding-string length. The build fails if fewer than this many PS bytes remain.
    // spec: Docs/RE/specs/crypto.md §6.3 (PS ≥ 8).
    private const int MinPaddingStringLength = 8;

    /// <summary>
    /// Parsed result of the server's 0/0 KeyExchange payload. <see cref="Modulus"/> and
    /// <see cref="Exponent"/> are the big-endian digit arrays reconstructed as non-negative
    /// <see cref="BigInteger"/> values; <see cref="ModulusByteLength"/> is the wire byte width
    /// <c>L1</c> of the modulus (the block size <c>k</c> the PKCS#1 padding targets). The two scalars
    /// are stored verbatim — the client does not interpret them.
    /// spec: Docs/RE/specs/crypto.md §6.2.
    /// </summary>
    public readonly struct KeyExchange
    {
        public KeyExchange(BigInteger modulus, BigInteger exponent, int modulusByteLength, uint scalar1, uint scalar2)
        {
            Modulus = modulus;
            Exponent = exponent;
            ModulusByteLength = modulusByteLength;
            Scalar1 = scalar1;
            Scalar2 = scalar2;
        }

        /// <summary>Server RSA modulus <c>n</c> (reconstructed from big-endian digits).</summary>
        public BigInteger Modulus { get; }

        /// <summary>Server RSA public exponent <c>e</c> (reconstructed from big-endian digits).</summary>
        public BigInteger Exponent { get; }

        /// <summary>Wire byte width of the modulus digit array (<c>L1</c>); the PKCS#1 block size <c>k</c>.</summary>
        public int ModulusByteLength { get; }

        /// <summary>First trailing 4-byte server scalar (token/nonce/session-class). Stored, not interpreted.</summary>
        public uint Scalar1 { get; }

        /// <summary>Second trailing 4-byte server scalar. Stored, not interpreted.</summary>
        public uint Scalar2 { get; }
    }

    /// <summary>
    /// Parses the 62-byte server 0/0 KeyExchange payload (already LZ4-decompressed, no inverse cipher
    /// per §5) into its modulus, exponent, and two scalars. Every value is read live from the wire —
    /// no <c>n</c>, <c>e</c>, or <c>L1</c>/<c>L2</c> is hardcoded; the only enforced invariant is the
    /// fixed sum <c>L1 + L2 = 42</c>.
    /// <para>
    /// Layout (spec §6.2, §6.2.1): <c>headerA(2) ‖ headerB(2) ‖ [u32 LE L1] ‖ modulus[L1] (big-endian)
    /// ‖ [u32 LE L2] ‖ exponent[L2] (big-endian) ‖ scalar1(4) ‖ scalar2(4)</c>. The two 2-byte headers
    /// are opaque tags — stored nowhere, ignored (§6.2.2).
    /// </para>
    /// spec: Docs/RE/specs/crypto.md §6.2.
    /// </summary>
    /// <exception cref="ArgumentException">If the payload is malformed (wrong size, lengths that do not honor L1 + L2 = 42, or out-of-range fields).</exception>
    public static KeyExchange ParseKeyExchange(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != KeyExchangePayloadSize)
        {
            throw new ArgumentException(
                $"0/0 KeyExchange payload must be {KeyExchangePayloadSize} bytes, got {payload.Length}.",
                nameof(payload));
        }

        // header A(2) ‖ header B(2): opaque per-value tags, ignored. spec §6.2.1, §6.2.2.
        int cursor = HeaderTagBytes + HeaderTagBytes;

        // [u32 LE L1] ‖ modulus[L1] (big-endian digits). spec §6.2.1, §6.2.3.
        int l1 = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, LengthPrefixBytes)));
        cursor += LengthPrefixBytes;
        if (l1 < 0 || cursor + l1 > payload.Length)
        {
            throw new ArgumentException($"0/0 modulus length L1={l1} overruns the payload.", nameof(payload));
        }

        BigInteger modulus = ReadBigEndianUnsigned(payload.Slice(cursor, l1));
        cursor += l1;

        // [u32 LE L2] ‖ exponent[L2] (big-endian digits). spec §6.2.1, §6.2.3.
        int l2 = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, LengthPrefixBytes)));
        cursor += LengthPrefixBytes;
        if (l2 < 0 || cursor + l2 > payload.Length)
        {
            throw new ArgumentException($"0/0 exponent length L2={l2} overruns the payload.", nameof(payload));
        }

        BigInteger exponent = ReadBigEndianUnsigned(payload.Slice(cursor, l2));
        cursor += l2;

        // Hard invariant: the blob consumes exactly 54 bytes ⇒ L1 + L2 = 42. spec §6.2.1.
        if (l1 + l2 != ModulusPlusExponentByteLength)
        {
            throw new ArgumentException(
                $"0/0 key blob requires L1 + L2 = {ModulusPlusExponentByteLength}, got {l1} + {l2} = {l1 + l2}.",
                nameof(payload));
        }

        // Two trailing 4-byte server scalars. spec §6.2.
        if (cursor + ScalarBytes + ScalarBytes != payload.Length)
        {
            throw new ArgumentException("0/0 payload does not end on the two 4-byte server scalars.", nameof(payload));
        }

        uint scalar1 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, ScalarBytes));
        cursor += ScalarBytes;
        uint scalar2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, ScalarBytes));

        return new KeyExchange(modulus, exponent, l1, scalar1, scalar2);
    }

    /// <summary>
    /// Builds the 1/4 Auth reply <b>body</b> for the given staged credential, returning a freshly
    /// allocated buffer ready to hand to the normal outbound send pipeline (byte cipher + LZ4 — both
    /// owned by the caller). The body is: <c>whiten_dwords( [u32 LE len(c)] ‖ BE_digits(c) )</c>
    /// where <c>c = PKCS1v15_type2(credential, k − 1) ^ e mod n</c> and <c>k</c> is the modulus byte
    /// width from <paramref name="keyExchange"/>.
    /// <para>
    /// This is a handshake reply builder, not a hot path; it allocates one result buffer plus scratch.
    /// The per-packet cipher remains allocation-free and is untouched.
    /// </para>
    /// spec: Docs/RE/specs/crypto.md §6.3, §6.4, §8.1.
    /// </summary>
    /// <param name="keyExchange">The parsed server 0/0 KeyExchange (live n, e, and modulus width k).</param>
    /// <param name="credential">The staged login credential (password) plaintext M. Not random, not derived.</param>
    /// <param name="paddingRng">
    /// RNG used to fill the PKCS#1 type-2 padding string with non-zero bytes. Injected so tests are
    /// deterministic; pass <see cref="RandomNumberGenerator"/>-backed randomness in production.
    /// </param>
    /// <returns>The whitened reply body, ready for the standard cipher + LZ4 send path.</returns>
    /// <exception cref="ArgumentException">If the credential is too long to leave PS ≥ 8 bytes (§6.3).</exception>
    public static byte[] BuildAuthReply(in KeyExchange keyExchange, ReadOnlySpan<byte> credential,
        IPaddingRandom paddingRng)
    {
        ArgumentNullException.ThrowIfNull(paddingRng);

        // Step 1 — PKCS#1 v1.5 type-2 padding to a block of size k − 1. spec §6.3.
        int blockLength = keyExchange.ModulusByteLength - 1;
        byte[] paddedBlock = BuildType2Block(blockLength, credential, paddingRng);

        // Step 2 — RSA public-key exponentiation: c = m^e mod n, m = padded block as big-endian int.
        // spec §6.3.
        BigInteger m = ReadBigEndianUnsigned(paddedBlock);
        BigInteger c = BigInteger.ModPow(m, keyExchange.Exponent, keyExchange.Modulus);

        // Step 3 — serialize: [u32 LE length] ‖ big-endian digits of c. spec §6.3, §6.2.3.
        byte[] cipherDigits = ToBigEndianUnsigned(c);
        byte[] reply = new byte[LengthPrefixBytes + cipherDigits.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(reply, (uint)cipherDigits.Length);
        cipherDigits.CopyTo(reply.AsSpan(LengthPrefixBytes));

        // Step 4 — per-dword XOR 0x29 whitening over the whole dword-aligned body. spec §6.4.
        HandshakeWhitening.XorWhitenDwords(reply);

        // Step 5 (caller) — hand `reply` to the normal send pipeline: byte cipher then LZ4, with the
        // 8-byte plaintext header carrying opcode major 1 / minor 4. spec §6.3 step 5.
        return reply;
    }

    /// <summary>
    /// Builds the <c>0x02 ‖ PS ‖ 0x00 ‖ M</c> PKCS#1 v1.5 type-2 block of exactly
    /// <paramref name="blockLength"/> bytes (which is <c>k − 1</c>, the modulus width minus its leading
    /// 0x00 octet). PS is filled with random non-zero bytes; the build fails if PS would be shorter
    /// than 8 bytes. spec: Docs/RE/specs/crypto.md §6.3.
    /// </summary>
    private static byte[] BuildType2Block(int blockLength, ReadOnlySpan<byte> message, IPaddingRandom paddingRng)
    {
        // len(PS) = (k − 1) − 1 (marker) − 1 (separator) − len(M). spec §6.3.
        int psLength = blockLength - 1 - 1 - message.Length;
        if (psLength < MinPaddingStringLength)
        {
            throw new ArgumentException(
                $"Credential too long for modulus: PKCS#1 type-2 padding string would be {psLength} bytes, " +
                $"below the required minimum of {MinPaddingStringLength}.",
                nameof(message));
        }

        byte[] block = new byte[blockLength];
        block[0] = BlockType2Marker;
        FillNonZeroPadding(block.AsSpan(1, psLength), paddingRng);
        block[1 + psLength] = PaddingSeparator;
        message.CopyTo(block.AsSpan(1 + psLength + 1));
        return block;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with random <b>non-zero</b> bytes (PKCS#1 type-2 PS rule).
    /// Draws from the injected RNG and replaces any zero byte with a fresh draw until non-zero.
    /// spec: Docs/RE/specs/crypto.md §6.3 (PS = random, guaranteed-nonzero).
    /// </summary>
    private static void FillNonZeroPadding(Span<byte> destination, IPaddingRandom paddingRng)
    {
        paddingRng.Fill(destination);
        for (int i = 0; i < destination.Length; i++)
        {
            while (destination[i] == 0)
            {
                Span<byte> one = stackalloc byte[1];
                paddingRng.Fill(one);
                destination[i] = one[0];
            }
        }
    }

    /// <summary>
    /// Reconstructs a non-negative <see cref="BigInteger"/> from big-endian digit bytes (first byte is
    /// most significant), matching the importer's <c>acc = acc * 256 + next</c> reconstruction.
    /// spec: Docs/RE/specs/crypto.md §6.2.3.
    /// </summary>
    private static BigInteger ReadBigEndianUnsigned(ReadOnlySpan<byte> bigEndianDigits)
        => new BigInteger(bigEndianDigits, isUnsigned: true, isBigEndian: true);

    /// <summary>
    /// Emits a non-negative <see cref="BigInteger"/> as its minimal big-endian digit array (no sign
    /// byte, no leading zero), matching the outbound serializer. A zero value yields a single 0x00
    /// byte. spec: Docs/RE/specs/crypto.md §6.2.3, §6.3.
    /// </summary>
    private static byte[] ToBigEndianUnsigned(BigInteger value)
        => value.IsZero
            ? [0x00]
            : value.ToByteArray(isUnsigned: true, isBigEndian: true);
}