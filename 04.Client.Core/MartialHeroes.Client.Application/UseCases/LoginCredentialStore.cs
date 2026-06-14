using System.Text;
using MartialHeroes.Network.Crypto;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Holds the full login credential staged at login-form time so the inbound 0/0 KeyExchange handler
/// can build the complete secure 1/4 Auth reply via <see cref="LoginCredentialReply.Build"/>.
/// <para>
/// Staged state (all required by the 1/4 builder — spec: packets/login.yaml §CmsgLoginCredential):
/// <list type="bullet">
/// <item><description><see cref="Username"/> — the account name string (also drives the 0x2B
///   plaintext pre-image). Not part of the RSA crypto; encodes to the account bytes in the
///   pre-image. spec: crypto.md §6.1 / §6.6.</description></item>
/// <item><description><see cref="AccountBytes"/> — UTF-8-encoded account bytes (PROVISIONAL charset),
///   WITHOUT the trailing NUL (the builder adds it). spec: login.yaml AccountLength.</description></item>
/// <item><description><see cref="StagedPasswordM"/> — the fixed 17-byte zero-padded RSA plaintext
///   <c>M</c> produced by <see cref="CredentialPlaintext.StagePassword"/>. The password is NOT in
///   the plaintext pre-image; it travels only here. spec: crypto.md §6.1, §6.6, §6b
///   (DEBUGGER-VERIFIED 17-byte M).</description></item>
/// <item><description><see cref="PinBytes"/> — optional PIN bytes, without the trailing NUL.
///   Present only when <see cref="IncludePin"/> is <see langword="true"/> (a7-gated).
///   spec: login.yaml PIN GATE.</description></item>
/// <item><description><see cref="IncludePin"/> — whether the PIN region is appended to the pre-image
///   (the a7 second-password gate). spec: packets/login.yaml (PIN GATE).</description></item>
/// </list>
/// </para>
/// spec: Docs/RE/specs/crypto.md §6.1, §6.6; packets/login.yaml (CmsgLoginCredential).
/// </summary>
/// <remarks>
/// <para>
/// <b>Single logical owner.</b> Staged by the login use-case and consumed by the login handler — both
/// on the application's single logical owner; no locking. The buffer is zeroed on <see cref="Clear"/>
/// (called after the 1/4 reply is built, per §6.1: "the staged credential buffer is zeroed and freed").
/// </para>
/// <para>
/// The credential charset is unconfirmed (spec marks chat/text charsets UNKNOWN). UTF-8 is used as a
/// documented PROVISIONAL default for account/PIN bytes. The 17-byte staged-password M is produced by
/// <see cref="CredentialPlaintext.StagePassword"/> which encodes raw password bytes with zero padding.
/// </para>
/// </remarks>
public sealed class LoginCredentialStore
{
    // The 17-byte zero-padded RSA plaintext M. spec: crypto.md §6.1, §6.6, §6b (DEBUGGER-VERIFIED).
    private byte[] _stagedPasswordM = [];

    // Account bytes (UTF-8 PROVISIONAL), WITHOUT trailing NUL. spec: login.yaml AccountLength = strlen+1.
    private byte[] _accountBytes = [];

    // Optional PIN bytes (UTF-8 PROVISIONAL), WITHOUT trailing NUL. spec: login.yaml PIN GATE.
    private byte[] _pinBytes = [];

    /// <summary>The staged account name string (not part of the crypto). spec: crypto.md §6.1.</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>True while a credential is staged and not yet cleared.</summary>
    public bool HasStagedCredential => _stagedPasswordM.Length > 0;

    /// <summary>
    /// Whether the optional PIN region is appended to the 1/4 plaintext pre-image (the a7 gate).
    /// spec: packets/login.yaml (PIN GATE: region present only when nonzero / gate active).
    /// </summary>
    public bool IncludePin { get; private set; }

    /// <summary>
    /// The account-name bytes, WITHOUT the trailing NUL (the 1/4 builder adds it and prefixes the
    /// NUL-inclusive length). UTF-8 PROVISIONAL. spec: login.yaml AccountLength / Account field.
    /// </summary>
    public ReadOnlySpan<byte> AccountBytes => _accountBytes;

    /// <summary>
    /// The fixed 17-byte zero-padded RSA plaintext <c>M</c>. This is the ONLY place the password
    /// travels; it is NOT in the 0x2B plaintext pre-image. The trailing zeros are deliberate and
    /// part of M. spec: Docs/RE/specs/crypto.md §6.1, §6.6 ("a fixed 17-byte zero-padded buffer");
    /// login.yaml (STAGED RSA PLAINTEXT M).
    /// </summary>
    public ReadOnlySpan<byte> StagedPasswordM => _stagedPasswordM;

    /// <summary>
    /// The optional PIN bytes, WITHOUT the trailing NUL. Empty when <see cref="IncludePin"/> is
    /// <see langword="false"/>. spec: login.yaml PIN GATE.
    /// </summary>
    public ReadOnlySpan<byte> PinBytes => _pinBytes;

    /// <summary>
    /// Stages the full login credential before any 0/0 KeyExchange arrives:
    /// <list type="bullet">
    /// <item><description>Encodes the account to bytes (UTF-8 PROVISIONAL).</description></item>
    /// <item><description>Produces the 17-byte zero-padded <c>M</c> buffer via
    ///   <see cref="CredentialPlaintext.StagePassword"/> so the RSA half of the 1/4 reply can
    ///   consume the correct fixed-width plaintext directly. spec: crypto.md §6.1, §6.6.</description></item>
    /// <item><description>Optionally encodes the PIN (a7-gated) to bytes.
    ///   spec: login.yaml PIN GATE.</description></item>
    /// </list>
    /// spec: Docs/RE/specs/crypto.md §6.1; packets/login.yaml (CmsgLoginCredential).
    /// </summary>
    /// <param name="username">The account name. Encoded to the 0x2B pre-image account field.</param>
    /// <param name="password">
    /// The password. Encoded to bytes (UTF-8 PROVISIONAL) and staged as the 17-byte zero-padded
    /// RSA plaintext <c>M</c>. spec: crypto.md §6.1, §6b (DEBUGGER-VERIFIED 17-byte M); login.yaml.
    /// </param>
    /// <param name="pin">
    /// Optional PIN (second-password). When non-null and non-empty, the PIN region is appended to
    /// the 0x2B plaintext pre-image and <see cref="IncludePin"/> is set to <see langword="true"/>.
    /// spec: login.yaml PIN GATE.
    /// </param>
    public void Stage(string username, string password, string? pin = null)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        Clear(); // wipe any previous staging before re-staging
        Username = username;

        // Encode account to bytes (UTF-8 PROVISIONAL; wire charset UNKNOWN). WITHOUT trailing NUL.
        // spec: login.yaml AccountLength = strlen(account)+1 (NUL counted by the builder).
        _accountBytes = Encoding.UTF8.GetBytes(username);

        // Encode password to bytes then stage the fixed 17-byte zero-padded RSA plaintext M.
        // spec: crypto.md §6.1, §6.6, §6b (DEBUGGER-VERIFIED: the server expects a fixed-width
        // 17-byte field; trailing zeros are part of M; password capacity < 17).
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        _stagedPasswordM = CredentialPlaintext.StagePassword(passwordBytes);
        Array.Clear(passwordBytes); // immediately zero the intermediate password bytes off the heap

        // Optional PIN (a7-gated). spec: login.yaml PIN GATE.
        if (!string.IsNullOrEmpty(pin))
        {
            _pinBytes = Encoding.UTF8.GetBytes(pin);
            IncludePin = true;
        }
        else
        {
            _pinBytes = [];
            IncludePin = false;
        }
    }

    /// <summary>
    /// Zeroes and releases all staged credential buffers. Called after the 1/4 reply is built.
    /// spec: Docs/RE/specs/crypto.md §6.1 (staged M zeroed and freed after the reply is sent);
    /// crypto.md §6a (secure-context teardown zeros the staged M and embedded buffers).
    /// </summary>
    public void Clear()
    {
        if (_stagedPasswordM.Length > 0)
        {
            Array.Clear(_stagedPasswordM);
            _stagedPasswordM = [];
        }

        if (_accountBytes.Length > 0)
        {
            Array.Clear(_accountBytes);
            _accountBytes = [];
        }

        if (_pinBytes.Length > 0)
        {
            Array.Clear(_pinBytes);
            _pinBytes = [];
        }

        IncludePin = false;
        Username = string.Empty;
    }
}