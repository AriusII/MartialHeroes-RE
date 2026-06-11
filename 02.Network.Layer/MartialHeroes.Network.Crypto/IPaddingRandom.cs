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

/// <summary>
/// Adapts an arbitrary <see cref="Action{T}"/> fill delegate to <see cref="IPaddingRandom"/>. Lets a
/// test inject a deterministic byte source without a concrete RNG type.
/// </summary>
public sealed class DelegatePaddingRandom : IPaddingRandom
{
    private readonly FillBytes _fill;

    /// <summary>Span-filling delegate signature (cannot use <c>Action&lt;Span&lt;byte&gt;&gt;</c> — ref struct generics).</summary>
    public delegate void FillBytes(Span<byte> destination);

    public DelegatePaddingRandom(FillBytes fill) => _fill = fill ?? throw new ArgumentNullException(nameof(fill));

    public void Fill(Span<byte> destination) => _fill(destination);
}