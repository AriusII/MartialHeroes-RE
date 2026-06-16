using System.Buffers.Binary;

namespace MartialHeroes.Network.Crypto;

/// <summary>
/// Composes the complete, debugger-verified secure <c>1/4</c> Auth-reply payload (opcode major 1 /
/// minor 4): a plaintext <c>0x2B</c> pre-image (login sub-opcode, length-prefixed account, and an
/// optional length-prefixed PIN) followed by the RSA ciphertext of the staged password, with the
/// per-dword <c>0x29</c> whitening applied over the <b>whole</b> payload before it enters the normal
/// outbound pipeline (byte cipher + LZ4).
/// <para>
/// On-wire <c>1/4</c> payload, before whitening and the normal send pipeline:
/// </para>
/// <code>
/// [u8 0x2B] [u32 LE account_len] [account..]  ([u32 LE pin_len] [pin..])   &lt;- plaintext pre-image
/// [u32 LE ciphertext_len] [big-endian RSA digits]                            &lt;- the RSA half (§6.3)
/// </code>
/// <para>
/// The password is NOT in the pre-image: it is the staged RSA plaintext <c>M</c> (the fixed 17-byte
/// zero-padded buffer of <see cref="CredentialPlaintext"/>), consumed in full by the RSA build.
/// </para>
/// spec: Docs/RE/specs/crypto.md §6.6, §6.3, §6.4, §6b; packets/login.yaml (CmsgLoginCredential).
/// </summary>
public static class LoginCredentialReply
{
    /// <summary>
    /// Login sub-opcode that leads the plaintext pre-image (first payload byte).
    /// spec: packets/login.yaml (SubOpcode = 0x2B, DEBUGGER-VERIFIED); Docs/RE/specs/crypto.md §6.6, §6b.
    /// </summary>
    public const byte LoginSubOpcode = 0x2B;

    // u32 little-endian length prefixes for the account and PIN strings (NUL-inclusive).
    // spec: packets/login.yaml (AccountLength/PinLength are u32 LE, = strlen+1).
    private const int LengthPrefixBytes = sizeof(uint);

    // Single byte for the leading sub-opcode. spec: packets/login.yaml (off 0x00, u8).
    private const int SubOpcodeBytes = 1;

    /// <summary>
    /// Minimum account-name character count the login-packet builder accepts. The client-side gate
    /// requires the account (like the password) to be at least 2 characters before any 0/0 is sent.
    /// spec: Docs/RE/specs/crypto.md §6.1 ("both the account and the password to be at least 2
    /// characters long"); packets/login.yaml ("AccountLength ... >= 2").
    /// </summary>
    public const int MinAccountLength = 2;

    /// <summary>
    /// Exclusive upper bound on the account name's NUL-inclusive length (AccountLength = strlen + 1).
    /// login.yaml declares <c>AccountLength &gt;= 2, &lt; 20</c>; this is the field's fixed buffer cap
    /// and is itself capture/static (not capture-verified end to end).
    /// spec: packets/login.yaml ("AccountLength ... >= 2, < 20"); Docs/RE/specs/crypto.md §6.1.
    /// </summary>
    public const int MaxAccountLengthExclusive = 20;

    /// <summary>
    /// Builds the whole whitened <c>1/4</c> reply payload from its structured parts.
    /// </summary>
    /// <param name="keyExchange">The parsed server 0/0 KeyExchange (live n, e, modulus width k). spec §6.2.</param>
    /// <param name="account">
    /// Account-name bytes, <b>without</b> the trailing NUL (this method appends the NUL and prefixes the
    /// NUL-inclusive length). Already charset-encoded by the caller. spec: packets/login.yaml.
    /// </param>
    /// <param name="pin">
    /// Optional PIN bytes, <b>without</b> the trailing NUL. Pass an empty span and
    /// <paramref name="includePin"/> = <c>false</c> when no PIN was entered (the PIN region is then
    /// omitted entirely — the a7-gate is inactive). spec: packets/login.yaml (PIN GATE).
    /// </param>
    /// <param name="includePin">
    /// Whether the PIN length-prefixed pair is present (the a7 / second-password gate). spec: login.yaml.
    /// </param>
    /// <param name="stagedPassword">
    /// The zero-padded RSA plaintext <c>M</c> (see <see cref="CredentialPlaintext"/>), whose width is the
    /// password-field cap (caller-supplied; 17 observed). The RSA build consumes the whole buffer.
    /// spec: Docs/RE/specs/crypto.md §6.1, §6.6 (width = password-field cap, not a literal).
    /// </param>
    /// <param name="paddingRng">RNG for the PKCS#1 type-2 padding (the only randomness, §6.3).</param>
    /// <returns>
    /// The whitened <c>1/4</c> payload, ready to hand to the normal outbound send path (byte cipher +
    /// LZ4 + 8-byte plaintext header carrying opcode 1/4).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// If the staged password is shorter than the minimum field cap, the account length is out of range
    /// (§6.1 / login.yaml), or the credential is too long to leave PS ≥ 8 in PKCS#1 (§6.3).
    /// </exception>
    public static byte[] Build(
        in SessionHandshake.KeyExchange keyExchange,
        ReadOnlySpan<byte> account,
        ReadOnlySpan<byte> pin,
        bool includePin,
        ReadOnlySpan<byte> stagedPassword,
        IPaddingRandom paddingRng)
    {
        ArgumentNullException.ThrowIfNull(paddingRng);

        // The staged M width is parameter-driven (the password-field cap), not a literal — it must hold
        // at least a 2-char password plus one trailing zero. spec: crypto.md §6.6, §8.1, §9.2 point 2.
        if (stagedPassword.Length < CredentialPlaintext.MinPasswordLength + 1)
        {
            throw new ArgumentException(
                $"Staged RSA plaintext M must be at least {CredentialPlaintext.MinPasswordLength + 1} bytes " +
                $"(the password-field cap; use CredentialPlaintext.StagePassword), got {stagedPassword.Length}.",
                nameof(stagedPassword));
        }

        // Client-side account-length gate: >= 2 chars and below the declared field cap (NUL-inclusive
        // length strictly < 20). A faithful client reproduces the same accept/reject behavior.
        // spec: Docs/RE/specs/crypto.md §6.1; packets/login.yaml (AccountLength >= 2, < 20).
        if (account.Length < MinAccountLength || account.Length + 1 >= MaxAccountLengthExclusive)
        {
            throw new ArgumentException(
                $"Account length {account.Length} is out of range: must be >= {MinAccountLength} chars and " +
                $"its NUL-inclusive length < {MaxAccountLengthExclusive}.",
                nameof(account));
        }

        // RSA half first (un-whitened): [u32 LE len(c)] ‖ BE digits of c, c = M_padded^e mod n.
        // spec: Docs/RE/specs/crypto.md §6.3 (steps 1–3), §6.6.
        byte[] ciphertextRegion = SessionHandshake.EncryptCredential(in keyExchange, stagedPassword, paddingRng);

        // Plaintext pre-image: [0x2B] [u32 LE account_len][account][NUL] ([u32 LE pin_len][pin][NUL]).
        // Length prefixes are strlen+1 (NUL counted). spec: packets/login.yaml.
        int accountLen = account.Length + 1; // + trailing NUL
        int preImageLength = SubOpcodeBytes + LengthPrefixBytes + accountLen;

        int pinLen = 0;
        if (includePin)
        {
            pinLen = pin.Length + 1; // + trailing NUL
            preImageLength += LengthPrefixBytes + pinLen;
        }

        // Whole 1/4 payload = pre-image ‖ ciphertext region. spec §6.6.
        byte[] payload = new byte[preImageLength + ciphertextRegion.Length];
        Span<byte> cursor = payload;

        // [u8 0x2B] sub-opcode. spec: login.yaml off 0x00.
        cursor[0] = LoginSubOpcode;
        cursor = cursor[SubOpcodeBytes..];

        // [u32 LE account_len][account][NUL]. spec: login.yaml off 0x01..0x05.
        BinaryPrimitives.WriteUInt32LittleEndian(cursor, (uint)accountLen);
        cursor = cursor[LengthPrefixBytes..];
        account.CopyTo(cursor);
        cursor[account.Length] = 0x00; // trailing NUL (counted in account_len)
        cursor = cursor[accountLen..];

        // OPTIONAL [u32 LE pin_len][pin][NUL] — a7-gated. spec: login.yaml (PIN GATE).
        if (includePin)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(cursor, (uint)pinLen);
            cursor = cursor[LengthPrefixBytes..];
            pin.CopyTo(cursor);
            cursor[pin.Length] = 0x00; // trailing NUL (counted in pin_len)
            cursor = cursor[pinLen..];
        }

        // Append the RSA ciphertext region after the pre-image at the same cursor. spec §6.6.
        ciphertextRegion.CopyTo(cursor);

        // Per-dword XOR 0x29 whitening over the WHOLE 1/4 payload (pre-image + ciphertext). spec §6.4, §6.6.
        HandshakeWhitening.XorWhitenDwords(payload);

        return payload;
    }
}