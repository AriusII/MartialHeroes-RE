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
    /// XOR every aligned 32-bit dword of <paramref name="authReplyPayload"/> with the whitening key.
    /// Operates on <c>floor(length / 4)</c> dwords (i.e. <c>length &amp; ~3</c> bytes); any trailing
    /// 1–3 bytes are left untouched. In place, zero-allocation, and an involution.
    /// spec: Docs/RE/specs/crypto.md §6.4 (whitened span = whole dword-aligned payload, no cap).
    /// </summary>
    public static void XorWhitenDwords(Span<byte> authReplyPayload)
    {
        uint key = ResolveDwordKey();

        int dwordBytes = authReplyPayload.Length & ~3; // size >> 2 dwords, in bytes. §6.4
        for (int offset = 0; offset < dwordBytes; offset += sizeof(uint))
        {
            Span<byte> slot = authReplyPayload.Slice(offset, sizeof(uint));
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(slot);
            BinaryPrimitives.WriteUInt32LittleEndian(slot, value ^ key);
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
            ? unchecked((byte)~XorKeyByte) // complement branch — not taken for this client's recovered values
            : XorKeyByte; // used as-is

        // Little-endian dword pattern "key 00 00 00": the key occupies the low byte of each dword.
        // spec: Docs/RE/specs/crypto.md §6.4 ("little-endian byte pattern 29 00 00 00").
        return keyByte;
    }
}