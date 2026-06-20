namespace MartialHeroes.Network.Crypto;

/// <summary>
///     Deliberately-empty cipher state, kept only for API symmetry with keyed/stateful ciphers.
///     <para>
///         The Martial Heroes wire byte cipher is <b>keyless and stateless across packets</b>: it carries
///         no key, no per-connection seed, and no rolling-key global. Every packet is enciphered identically
///         and independently of every other. Therefore this struct holds <b>no fields</b> and is never
///         required by <see cref="WireCipher.EncryptInPlace" /> / <see cref="WireCipher.DecryptInPlace" />.
///     </para>
///     <para>
///         The keyless/stateless reading is <b>RESOLVED [confirmed]</b>: §4 is explicit there is no key-schedule
///         object and no seed plumbing, and §5 proves the cipher has a single send-side cross-reference (no
///         inbound inverse). This struct therefore carries no fields and is never required by a call site; it
///         is API-symmetry scaffolding only. Do not add fields without a spec update revealing per-session keying.
///     </para>
///     spec: Docs/RE/specs/crypto.md §4 (keyless and stateless — explicit), §5 (single send-side caller, RESOLVED).
/// </summary>
public readonly struct CipherState
{
    /// <summary>The single shared state value. As specified, it carries nothing.</summary>
    public static CipherState None => default;
}