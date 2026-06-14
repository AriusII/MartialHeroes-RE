namespace MartialHeroes.Network.Crypto;

/// <summary>
/// Stages the RSA plaintext <c>M</c> for the secure <c>1/4</c> Auth reply: a <b>fixed 17-byte
/// zero-padded</b> buffer holding the password bytes followed by zero padding to a constant width.
/// The encrypt step consumes the <b>full 17 bytes</b> as <c>M</c> regardless of the actual password
/// length, so the trailing zeros are part of <c>M</c> — the server expects a fixed-width 17-byte
/// password field.
/// <para>
/// The password is <b>not</b> part of the plaintext pre-image (the <c>0x2B</c> region) — it travels
/// only as this staged RSA plaintext (see <see cref="LoginCredentialReply"/> / §6.6).
/// </para>
/// spec: Docs/RE/specs/crypto.md §6.1, §6.6, §6b (DEBUGGER-VERIFIED 17-byte M); packets/login.yaml
/// (STAGED RSA PLAINTEXT M).
/// </summary>
public static class CredentialPlaintext
{
    /// <summary>
    /// Fixed width of the staged RSA plaintext <c>M</c> buffer. The password is copied in (no NUL) and
    /// the remainder is zero-filled to this width; the encrypt step consumes all of it.
    /// spec: Docs/RE/specs/crypto.md §6.1, §6.6 ("a fixed 17-byte zero-padded buffer"); login.yaml.
    /// </summary>
    public const int StagedPasswordLength = 17;

    /// <summary>
    /// Minimum password length the login form accepts. spec: packets/login.yaml ("length >= 2 and &lt; 17").
    /// </summary>
    public const int MinPasswordLength = 2;

    /// <summary>
    /// Exclusive upper bound on password length: the password must fit inside the 17-byte buffer with
    /// at least one trailing zero, so its length is strictly less than 17.
    /// spec: packets/login.yaml ("length >= 2 and &lt; 17").
    /// </summary>
    public const int MaxPasswordLengthExclusive = StagedPasswordLength;

    /// <summary>
    /// Copies <paramref name="password"/> into a fresh fixed 17-byte zero-filled buffer and returns it
    /// as the staged RSA plaintext <c>M</c>. The trailing zeros are deliberate and part of <c>M</c>.
    /// spec: Docs/RE/specs/crypto.md §6.1, §6.6.
    /// </summary>
    /// <param name="password">The password bytes (already charset-encoded by the caller; no NUL appended).</param>
    /// <returns>A 17-byte buffer: password bytes then zero padding.</returns>
    /// <exception cref="ArgumentException">
    /// If the password is shorter than <see cref="MinPasswordLength"/> or not strictly shorter than
    /// <see cref="StagedPasswordLength"/> (it would leave no room for the zero padding).
    /// </exception>
    public static byte[] StagePassword(ReadOnlySpan<byte> password)
    {
        byte[] staged = new byte[StagedPasswordLength];
        StagePassword(password, staged);
        return staged;
    }

    /// <summary>
    /// Writes <paramref name="password"/> followed by zero padding into <paramref name="destination"/>,
    /// which must be exactly <see cref="StagedPasswordLength"/> bytes. Zero-allocation variant for a
    /// caller that owns the buffer. The destination is fully overwritten (password then zeros).
    /// spec: Docs/RE/specs/crypto.md §6.1, §6.6.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If <paramref name="destination"/> is not 17 bytes, or the password length is out of range.
    /// </exception>
    public static void StagePassword(ReadOnlySpan<byte> password, Span<byte> destination)
    {
        if (destination.Length != StagedPasswordLength)
        {
            throw new ArgumentException(
                $"Staged M buffer must be exactly {StagedPasswordLength} bytes, got {destination.Length}.",
                nameof(destination));
        }

        // length >= 2 and < 17 — the password must fit with at least one trailing zero. spec: login.yaml.
        if (password.Length < MinPasswordLength || password.Length >= MaxPasswordLengthExclusive)
        {
            throw new ArgumentException(
                $"Password length {password.Length} is out of range [{MinPasswordLength}, {MaxPasswordLengthExclusive}).",
                nameof(password));
        }

        destination.Clear(); // zero-fill, then overwrite the password prefix; the tail stays zero. §6.1
        password.CopyTo(destination);
    }
}