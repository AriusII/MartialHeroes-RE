using System.Security.Cryptography;

namespace MartialHeroes.Network.Crypto;

/// <summary>
/// Source of randomness for the PKCS#1 v1.5 type-2 padding string of the handshake Auth reply.
/// Injected so the reply build is deterministic under test (a fixed RNG) while production uses a
/// cryptographic RNG. The padding is the <b>only</b> randomness in the handshake (the plaintext is the
/// staged credential, not a nonce). spec: Docs/RE/specs/crypto.md §6, §6.3.
/// </summary>
public interface IPaddingRandom
{
    /// <summary>Fills the whole span with random bytes (zeros allowed; the caller enforces non-zero).</summary>
    void Fill(Span<byte> destination);
}

/// <summary>
/// Default <see cref="IPaddingRandom"/> backed by <see cref="RandomNumberGenerator"/> — the
/// production source of cryptographically strong PKCS#1 padding.
/// spec: Docs/RE/specs/crypto.md §6.3 (random nonzero PS).
/// </summary>
public sealed class CryptoPaddingRandom : IPaddingRandom
{
    /// <summary>Shared, thread-safe instance.</summary>
    public static readonly CryptoPaddingRandom Shared = new();

    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}