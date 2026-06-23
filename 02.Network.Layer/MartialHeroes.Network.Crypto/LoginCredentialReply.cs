using System.Buffers.Binary;

namespace MartialHeroes.Network.Crypto;

public static class LoginCredentialReply
{
    public const byte LoginSubOpcode = 0x2B;

    private const int LengthPrefixBytes = sizeof(uint);

    private const int SubOpcodeBytes = 1;

    public const int MinAccountLength = 2;

    public const int MaxAccountLengthExclusive = 20;

    public const int MaxPinLengthExclusive = 5;

    public static byte[] Build(
        in SessionHandshake.KeyExchange keyExchange,
        ReadOnlySpan<byte> account,
        ReadOnlySpan<byte> pin,
        bool includePin,
        ReadOnlySpan<byte> stagedPassword,
        IPaddingRandom paddingRng)
    {
        ArgumentNullException.ThrowIfNull(paddingRng);

        if (stagedPassword.Length < CredentialPlaintext.MinPasswordLength + 1)
            throw new ArgumentException(
                $"Staged RSA plaintext M must be at least {CredentialPlaintext.MinPasswordLength + 1} bytes " +
                $"(the password-field cap; use CredentialPlaintext.StagePassword), got {stagedPassword.Length}.",
                nameof(stagedPassword));

        if (account.Length < MinAccountLength || account.Length + 1 >= MaxAccountLengthExclusive)
            throw new ArgumentException(
                $"Account length {account.Length} is out of range: must be >= {MinAccountLength} chars and " +
                $"its NUL-inclusive length < {MaxAccountLengthExclusive}.",
                nameof(account));

        var ciphertextRegion = SessionHandshake.EncryptCredential(in keyExchange, stagedPassword, paddingRng);

        var accountLen = account.Length + 1;
        var preImageLength = SubOpcodeBytes + LengthPrefixBytes + accountLen;

        var pinLen = 0;
        if (includePin)
        {
            if (pin.Length >= MaxPinLengthExclusive)
                throw new ArgumentException(
                    $"PIN length {pin.Length} is out of range: must be < {MaxPinLengthExclusive} bytes (≤ 4 digits).",
                    nameof(pin));

            pinLen = pin.Length + 1;
            preImageLength += LengthPrefixBytes + pinLen;
        }

        var payload = new byte[preImageLength + ciphertextRegion.Length];
        Span<byte> cursor = payload;

        cursor[0] = LoginSubOpcode;
        cursor = cursor[SubOpcodeBytes..];

        BinaryPrimitives.WriteUInt32LittleEndian(cursor, (uint)accountLen);
        cursor = cursor[LengthPrefixBytes..];
        account.CopyTo(cursor);
        cursor[account.Length] = 0x00;
        cursor = cursor[accountLen..];

        if (includePin)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(cursor, (uint)pinLen);
            cursor = cursor[LengthPrefixBytes..];
            pin.CopyTo(cursor);
            cursor[pin.Length] = 0x00;
            cursor = cursor[pinLen..];
        }

        ciphertextRegion.CopyTo(cursor);

        HandshakeWhitening.XorWhitenDwords(payload);

        return payload;
    }
}