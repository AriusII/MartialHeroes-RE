using System.Buffers.Binary;
using System.Numerics;

namespace MartialHeroes.Network.Crypto;

public static class SessionHandshake
{
    public const int KeyExchangePayloadSize = 62;

    public const int KeyBlobSize = 54;

    public const int ModulusPlusExponentByteLength = 42;

    private const int HeaderTagBytes = 2;

    private const int LengthPrefixBytes = sizeof(uint);

    private const int ScalarBytes = sizeof(uint);

    private const byte BlockType2Marker = 0x02;

    private const byte PaddingSeparator = 0x00;

    private const int MinPaddingStringLength = 8;

    public static KeyExchange ParseKeyExchange(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != KeyExchangePayloadSize)
            throw new ArgumentException(
                $"0/0 KeyExchange payload must be {KeyExchangePayloadSize} bytes, got {payload.Length}.",
                nameof(payload));

        int blockSizeK = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(HeaderTagBytes, HeaderTagBytes));
        var cursor = HeaderTagBytes + HeaderTagBytes;

        var l1 = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, LengthPrefixBytes)));
        cursor += LengthPrefixBytes;
        if (l1 < 0 || cursor + l1 > payload.Length)
            throw new ArgumentException($"0/0 modulus length L1={l1} overruns the payload.", nameof(payload));

        var modulus = ReadBigEndianUnsigned(payload.Slice(cursor, l1));
        cursor += l1;

        var l2 = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, LengthPrefixBytes)));
        cursor += LengthPrefixBytes;
        if (l2 < 0 || cursor + l2 > payload.Length)
            throw new ArgumentException($"0/0 exponent length L2={l2} overruns the payload.", nameof(payload));

        var exponent = ReadBigEndianUnsigned(payload.Slice(cursor, l2));
        cursor += l2;

        if (l1 + l2 != ModulusPlusExponentByteLength)
            throw new ArgumentException(
                $"0/0 key blob requires L1 + L2 = {ModulusPlusExponentByteLength}, got {l1} + {l2} = {l1 + l2}.",
                nameof(payload));

        if (cursor + ScalarBytes + ScalarBytes != payload.Length)
            throw new ArgumentException("0/0 payload does not end on the two 4-byte server scalars.", nameof(payload));

        var scalar1 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, ScalarBytes));
        cursor += ScalarBytes;
        var scalar2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(cursor, ScalarBytes));

        return new KeyExchange(modulus, exponent, l1, blockSizeK, scalar1, scalar2);
    }

    public static byte[] BuildAuthReply(in KeyExchange keyExchange, ReadOnlySpan<byte> credential,
        IPaddingRandom paddingRng)
    {
        var reply = EncryptCredential(in keyExchange, credential, paddingRng);

        HandshakeWhitening.XorWhitenDwords(reply);

        return reply;
    }

    public static byte[] EncryptCredential(in KeyExchange keyExchange, ReadOnlySpan<byte> credential,
        IPaddingRandom paddingRng)
    {
        ArgumentNullException.ThrowIfNull(paddingRng);

        var blockLength = keyExchange.BlockSizeK - 1;
        var paddedBlock = BuildType2Block(blockLength, credential, paddingRng);

        var m = ReadBigEndianUnsigned(paddedBlock);
        var c = BigInteger.ModPow(m, keyExchange.Exponent, keyExchange.Modulus);

        var cipherDigits = ToBigEndianUnsigned(c);
        var region = new byte[LengthPrefixBytes + cipherDigits.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(region, (uint)cipherDigits.Length);
        cipherDigits.CopyTo(region.AsSpan(LengthPrefixBytes));
        return region;
    }

    private static byte[] BuildType2Block(int blockLength, ReadOnlySpan<byte> message, IPaddingRandom paddingRng)
    {
        var psLength = blockLength - 1 - 1 - message.Length;
        if (psLength < MinPaddingStringLength)
            throw new ArgumentException(
                $"Credential too long for modulus: PKCS#1 type-2 padding string would be {psLength} bytes, " +
                $"below the required minimum of {MinPaddingStringLength}.",
                nameof(message));

        var block = new byte[blockLength];
        block[0] = BlockType2Marker;
        FillNonZeroPadding(block.AsSpan(1, psLength), paddingRng);
        block[1 + psLength] = PaddingSeparator;
        message.CopyTo(block.AsSpan(1 + psLength + 1));
        return block;
    }

    private static void FillNonZeroPadding(Span<byte> destination, IPaddingRandom paddingRng)
    {
        paddingRng.Fill(destination);
        Span<byte> one = stackalloc byte[1];
        for (var i = 0; i < destination.Length; i++)
            while (destination[i] == 0)
            {
                paddingRng.Fill(one);
                destination[i] = one[0];
            }
    }

    private static BigInteger ReadBigEndianUnsigned(ReadOnlySpan<byte> bigEndianDigits)
    {
        return new BigInteger(bigEndianDigits, true, true);
    }

    private static byte[] ToBigEndianUnsigned(BigInteger value)
    {
        return value.IsZero
            ? [0x00]
            : value.ToByteArray(true, true);
    }

    public readonly struct KeyExchange
    {
        public KeyExchange(BigInteger modulus, BigInteger exponent, int modulusByteLength, int blockSizeK, uint scalar1,
            uint scalar2)
        {
            Modulus = modulus;
            Exponent = exponent;
            ModulusByteLength = modulusByteLength;
            BlockSizeK = blockSizeK;
            Scalar1 = scalar1;
            Scalar2 = scalar2;
        }

        public BigInteger Modulus { get; }

        public BigInteger Exponent { get; }

        public int ModulusByteLength { get; }

        public int BlockSizeK { get; }

        public uint Scalar1 { get; }

        public uint Scalar2 { get; }
    }
}