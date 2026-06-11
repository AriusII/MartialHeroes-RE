namespace MartialHeroes.Network.Crypto;

/// <summary>
/// Deliberately-empty cipher state, kept only for API symmetry with keyed/stateful ciphers.
/// <para>
/// The Martial Heroes wire byte cipher is <b>keyless and stateless across packets</b>: it carries
/// no key, no per-connection seed, and no rolling-key global. Every packet is enciphered identically
/// and independently of every other. Therefore this struct holds <b>no fields</b> and is never
/// required by <see cref="WireCipher.EncryptInPlace"/> / <see cref="WireCipher.DecryptInPlace"/>.
/// </para>
/// <para>
/// It exists so that, should a future capture reveal per-session keying (currently unverified), the
/// state can be added here without changing call sites. Do not add fields without a spec update.
/// </para>
/// spec: Docs/RE/specs/crypto.md §4 (keyless and stateless — explicit).
/// </summary>
public readonly struct CipherState
{
    /// <summary>The single shared state value. As specified, it carries nothing.</summary>
    public static CipherState None => default;
}
