namespace MartialHeroes.Network.Crypto;

/// <summary>
///     Stages the RSA plaintext <c>M</c> for the secure <c>1/4</c> Auth reply: a zero-padded buffer holding
///     the password bytes followed by zero padding to the <b>password-field cap</b>. The encrypt step
///     consumes the <b>full buffer</b> as <c>M</c> regardless of the actual password length, so the trailing
///     zeros are part of <c>M</c> — the server expects a fixed-width password field.
///     <para>
///         <b>The staged-<c>M</c> width is parameter-driven, not a literal.</b> Statically it is sized to the
///         password-field cap (a caller-supplied argument); the concrete cap was <b>17 bytes</b> in the
///         debugger-observed session (§6b), so <see cref="DefaultPasswordFieldCap" /> exposes that observed value
///         as the default. An implementer should treat the staged-<c>M</c> width as "the password-field cap".
///     </para>
///     <para>
///         The password is <b>not</b> part of the plaintext pre-image (the <c>0x2B</c> region) — it travels
///         only as this staged RSA plaintext (see <see cref="LoginCredentialReply" /> / §6.6).
///     </para>
///     spec: Docs/RE/specs/crypto.md §6.1, §6.6 ("width is a caller-supplied argument, not a literal"),
///     §8.1 ("Parameter-driven = the password-field cap"), §9.2 point 2; §6b (DEBUGGER-VERIFIED 17-byte M);
///     packets/login.yaml (STAGED RSA PLAINTEXT M).
/// </summary>
public static class CredentialPlaintext
{
    /// <summary>
    ///     Debugger-observed default password-field cap (the staged <c>M</c> width). Statically the width is
    ///     a caller-supplied argument; <b>17</b> is only the runtime cap seen in the debugger-observed session
    ///     (§6b) — callers that know the live server's password-field cap should pass it explicitly.
    ///     spec: Docs/RE/specs/crypto.md §6.6, §8.1, §9.2 point 2, §6b (17 = debugger-observed runtime cap).
    /// </summary>
    public const int DefaultPasswordFieldCap = 17;

    /// <summary>
    ///     Minimum password length the login form accepts (each field must be at least 2 characters).
    ///     spec: Docs/RE/specs/crypto.md §6.1; packets/login.yaml ("length >= 2").
    /// </summary>
    public const int MinPasswordLength = 2;

    /// <summary>
    ///     Copies <paramref name="password" /> into a fresh zero-filled buffer of the password-field cap and
    ///     returns it as the staged RSA plaintext <c>M</c>. The trailing zeros are deliberate and part of
    ///     <c>M</c>. The width defaults to the debugger-observed <see cref="DefaultPasswordFieldCap" /> (17);
    ///     pass <paramref name="fieldCap" /> when the live server's cap differs.
    ///     spec: Docs/RE/specs/crypto.md §6.1, §6.6 (width = password-field cap, caller-supplied).
    /// </summary>
    /// <param name="password">The password bytes (already charset-encoded by the caller; no NUL appended).</param>
    /// <param name="fieldCap">
    ///     The password-field cap = the staged <c>M</c> width. Defaults to <see cref="DefaultPasswordFieldCap" />
    ///     (the debugger-observed 17). spec §6.6, §8.1.
    /// </param>
    /// <returns>A <paramref name="fieldCap" />-byte buffer: password bytes then zero padding.</returns>
    /// <exception cref="ArgumentException">
    ///     If <paramref name="fieldCap" /> is not at least <see cref="MinPasswordLength" /> + 1, or the password
    ///     is shorter than <see cref="MinPasswordLength" /> or not strictly shorter than the cap (it would
    ///     leave no room for the zero padding).
    /// </exception>
    public static byte[] StagePassword(ReadOnlySpan<byte> password, int fieldCap = DefaultPasswordFieldCap)
    {
        if (fieldCap < MinPasswordLength + 1)
            throw new ArgumentOutOfRangeException(
                nameof(fieldCap),
                fieldCap,
                $"Password-field cap must be at least {MinPasswordLength + 1} (room for a {MinPasswordLength}-char password plus one trailing zero).");

        var staged = new byte[fieldCap];
        StagePassword(password, staged);
        return staged;
    }

    /// <summary>
    ///     Writes <paramref name="password" /> followed by zero padding into <paramref name="destination" />,
    ///     whose length <b>is</b> the password-field cap (the staged <c>M</c> width). Zero-allocation variant
    ///     for a caller that owns the buffer. The destination is fully overwritten (password then zeros).
    ///     spec: Docs/RE/specs/crypto.md §6.1, §6.6 (width = password-field cap, caller-supplied).
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     If <paramref name="destination" /> is shorter than <see cref="MinPasswordLength" /> + 1, or the
    ///     password length is out of range for the destination's cap.
    /// </exception>
    public static void StagePassword(ReadOnlySpan<byte> password, Span<byte> destination)
    {
        // The destination length IS the password-field cap (caller-supplied width). §6.6, §8.1.
        var fieldCap = destination.Length;
        if (fieldCap < MinPasswordLength + 1)
            throw new ArgumentException(
                $"Staged M buffer (password-field cap) must be at least {MinPasswordLength + 1} bytes, got {fieldCap}.",
                nameof(destination));

        // length >= 2 and < fieldCap — the password must fit with at least one trailing zero.
        // spec: crypto.md §6.1 (each field >= 2 chars and below its cap); login.yaml.
        if (password.Length < MinPasswordLength || password.Length >= fieldCap)
            throw new ArgumentException(
                $"Password length {password.Length} is out of range [{MinPasswordLength}, {fieldCap}).",
                nameof(password));

        destination.Clear(); // zero-fill, then overwrite the password prefix; the tail stays zero. §6.1
        password.CopyTo(destination);
    }
}