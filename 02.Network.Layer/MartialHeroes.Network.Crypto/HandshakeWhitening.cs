using System.Buffers.Binary;

namespace MartialHeroes.Network.Crypto;

/// <summary>
/// Per-dword XOR whitening applied to the handshake Auth reply (opcode major 1 / minor 4) just
/// before it enters the normal send pipeline (byte cipher + LZ4). This is a separate, light step
/// from the wire byte cipher and is its own inverse (XOR), so the same method decodes it.
/// spec: Docs/RE/specs/crypto.md §6.4, §8.1.
/// </summary>
public static class HandshakeWhitening
{
    // The 32-bit XOR key applied to each dword of the reply payload.
    // spec: Docs/RE/specs/crypto.md §6.4, §8.1 (XOR key 0x29).
    private const byte XorKeyByte = 0x29;

    // Selector feeding the complement test below.
    // spec: Docs/RE/specs/crypto.md §6.4, §8.1 (selector 0x40).
    private const byte Selector = 0x40;

    // Complement test: (selector & key & 0x1F) == 1 selects the one's-complement of the key.
    // spec: Docs/RE/specs/crypto.md §6.4 — for this client 0x40 & 0x29 & 0x1F = 0 ≠ 1, so the key
    // is used as-is. We compute it rather than hardcode the outcome so a server handling other
    // (selector, key) pairs stays correct.
    private const byte ComplementMask = 0x1F;
    private const byte ComplementTriggerValue = 1;

    /// <summary>
    /// XOR every 32-bit dword of <paramref name="authReplyPayload"/> with the whitening key, covering
    /// the WHOLE payload — the final partial dword (1–3 trailing bytes) is processed too, NOT skipped.
    /// In place, zero-allocation, and an involution.
    /// <para>
    /// The original sizes the loop by the header-inclusive wire size <c>(8 + payload) &gt;&gt; 2</c>
    /// dwords starting at the payload, so it whitens every payload byte (overrunning into unsent
    /// scratch). On an exact-length buffer we reproduce the SENT effect by whitening all aligned dwords
    /// and then the trailing partial dword's bytes. spec: Docs/RE/specs/crypto.md §6.4 — CORRECTED
    /// CYCLE 4 (live oracle): leaving the trailing 1–3 bytes un-whitened corrupted the last ciphertext
    /// byte on the server → password-independent <c>1/4</c> login rejection.
    /// </para>
    /// </summary>
    public static void XorWhitenDwords(Span<byte> authReplyPayload)
    {
        uint key = ResolveDwordKey();
        int length = authReplyPayload.Length;

        int full = length & ~3; // aligned dword bytes. spec §6.4
        for (int offset = 0; offset < full; offset += sizeof(uint))
        {
            Span<byte> slot = authReplyPayload.Slice(offset, sizeof(uint));
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(slot);
            BinaryPrimitives.WriteUInt32LittleEndian(slot, value ^ key);
        }

        // Trailing partial dword (1–3 bytes): XOR each remaining byte with the matching little-endian
        // key byte. For the recovered key 0x00000029 only the low byte (the final multiple-of-4 byte)
        // flips; this matches the original whitening the last payload byte. spec: crypto.md §6.4.
        for (int offset = full; offset < length; offset++)
        {
            authReplyPayload[offset] ^= (byte)(key >> (8 * (offset - full)));
        }
    }

    /// <summary>
    /// Builds the 32-bit XOR key (key byte replicated to a dword: pattern <c>29 00 00 00</c> in
    /// little-endian terms means the low byte carries the key), honoring the complement test.
    /// spec: Docs/RE/specs/crypto.md §6.4.
    /// </summary>
    private static uint ResolveDwordKey()
    {
        byte keyByte = (Selector & XorKeyByte & ComplementMask) == ComplementTriggerValue
            // Complement branch — UNREACHABLE for this client (0x40 & 0x29 & 0x1F = 0 ≠ 1, so the key
            // is used as-is). The exact 32-bit complement convention a future server would apply when
            // the test fires (byte-replicated vs. full-dword complement) is NOT recovered — §6.4 states
            // no other (selector, key) pair occurs in this build, so this low-byte complement is a
            // documented placeholder, not a verified server form (capture/debugger-pending).
            // spec: Docs/RE/specs/crypto.md §6.4.
            ? unchecked((byte)~XorKeyByte)
            : XorKeyByte; // used as-is for this client

        // Little-endian dword pattern "key 00 00 00": the key occupies the low byte of each dword.
        // spec: Docs/RE/specs/crypto.md §6.4 ("little-endian byte pattern 29 00 00 00").
        return keyByte;
    }
}