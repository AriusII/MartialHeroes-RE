namespace MartialHeroes.Network.Crypto;

/// <summary>
/// Session key-exchange handshake (reserved opcode major 0 / minor 0, with the Auth reply on
/// major 1 / minor 4). This is a <b>custom big-integer public-key exchange</b> that is entirely
/// separate from the wire byte cipher and does <b>not</b> key it.
/// <para>
/// This subsystem is <b>not implemented</b>: the modulus/exponent L1/L2 split and the meaning of the
/// two per-bignum 2-byte headers are capture-unresolved (only the constraint <c>L1 + L2 = 42</c> is a
/// recoverable client constant). Fabricating the byte-exact layout from prose would be a clean-room
/// and correctness failure, so it is left as a documented stub. The only fully-specified piece — the
/// per-dword reply whitening — lives in <see cref="HandshakeWhitening"/> and is implemented.
/// </para>
/// spec: Docs/RE/specs/crypto.md §6, §8.1, §8.2.
/// </summary>
public static class SessionHandshake
{
    /// <summary>
    /// Total 0/0 key-exchange payload size before compression/cipher: 54-byte key blob + two 4-byte
    /// scalars. spec: Docs/RE/specs/crypto.md §6.1, §8.1.
    /// </summary>
    public const int KeyExchangePayloadSize = 62;

    /// <summary>
    /// Size the 54-byte key blob asserts on the wire.
    /// spec: Docs/RE/specs/crypto.md §6.2, §8.1.
    /// </summary>
    public const int KeyBlobSize = 54;

    /// <summary>
    /// Fixed sum of the two bignum digit-array lengths inside the key blob (modulus + exponent).
    /// The individual <c>L1</c>/<c>L2</c> split is server wire data, not a client constant.
    /// spec: Docs/RE/specs/crypto.md §6.2, §8.2 (L1 + L2 = 42).
    /// </summary>
    public const int ModulusPlusExponentByteLength = 42;

    // TODO: spec-blocked (needs live 0/0 capture).
    // The full bignum RSA handshake is intentionally NOT implemented:
    //   * L1/L2 (modulus/exponent) split is server wire data — only L1 + L2 = 42 is a client constant.
    //   * The two 2-byte per-bignum headers (header A / header B) are not bit-decoded.
    //   * PKCS#1 v1.5 type-2 padding to (modulus_bytes − 1) and the modular-exponentiation step need a
    //     byte-exact handshake spec, which is gated on a live capture.
    // Do not fabricate the layout. When a capture closes §8.2, parse the 0/0 blob and build the 1/4
    // reply here, reusing HandshakeWhitening.XorWhitenDwords for the per-dword whitening step.
}
