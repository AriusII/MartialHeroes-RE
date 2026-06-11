using System.Text;

namespace MartialHeroes.Client.Application.UseCases;

/// <summary>
/// Holds the login credential (password) staged at login-form time so the inbound 0/0 KeyExchange
/// handler can RSA-encrypt it into the 1/4 Auth reply. spec: Docs/RE/specs/crypto.md §6.1 (the client
/// pre-stages the password before any 0/0 packet arrives; the staged buffer is the PKCS#1 plaintext
/// <c>M</c>). The username is staged too but is not part of the crypto (§6.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Single logical owner.</b> Staged by the login use-case and consumed by the login handler — both
/// on the application's single logical owner; no locking. The buffer is zeroed on <see cref="Clear"/>
/// (called after the 1/4 reply is built, per §6.1: "the staged credential buffer is zeroed and freed").
/// </para>
/// <para>
/// The credential is encoded as raw bytes here; the wire charset is unconfirmed (spec marks chat/text
/// charsets UNKNOWN). UTF-8 is used as a documented PROVISIONAL default for the credential bytes.
/// </para>
/// </remarks>
public sealed class LoginCredentialStore
{
    private byte[] _credential = [];

    /// <summary>The staged account name (not part of the crypto; spec: crypto.md §6.1).</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>True while a credential is staged and not yet cleared.</summary>
    public bool HasStagedCredential => _credential.Length > 0;

    /// <summary>
    /// Stages the username and password. The password is encoded to bytes (UTF-8, PROVISIONAL charset)
    /// as the RSA plaintext <c>M</c>. spec: Docs/RE/specs/crypto.md §6.1.
    /// </summary>
    public void Stage(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        Username = username;
        Clear(); // wipe any previous staging before re-staging
        _credential = Encoding.UTF8.GetBytes(password);
    }

    /// <summary>The staged credential bytes (RSA plaintext <c>M</c>); empty when nothing is staged.</summary>
    public ReadOnlySpan<byte> Credential => _credential;

    /// <summary>
    /// Zeroes and releases the staged credential buffer. spec: Docs/RE/specs/crypto.md §6.1 (zeroed and
    /// freed after the 1/4 reply is built).
    /// </summary>
    public void Clear()
    {
        if (_credential.Length > 0)
        {
            Array.Clear(_credential);
            _credential = [];
        }
    }
}