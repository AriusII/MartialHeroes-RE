using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for the <c>particleEmitter.eff</c> GPU particle emitter descriptor table.
/// </summary>
/// <remarks>
/// File path (VFS): <c>data/effect/particle/particleEmitter.eff</c>
/// (VFS-lowercased to <c>data/effect/particle/particleemitter.eff</c>).
///
/// Layout: variable-length entry sequence with NO file header, NO magic, NO count prefix.
/// Each entry = 28-byte header + num_frames × 52-byte sub-record + 64-byte texture name.
/// Read loop terminates when an entry header's num_frames == 0 (sentinel) or &lt; 28 bytes remain.
/// spec: Docs/RE/formats/effects.md §E.2 File layout — variable-length entry sequence: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §E.1 Identification — no magic comparison: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §E.0 Correction — flat-table model RETIRED: CONFIRMED.
///
/// Runtime selection: a .xeff element's resource_id ≥ 10000 selects an entry by RAW entry_id equality.
/// There is NO −10000 subtraction. The ≥ 10000 threshold is a dispatch gate only.
/// spec: Docs/RE/formats/effects.md §E.4 Runtime selection — raw-id equality: CONFIRMED.
///
/// ZERO rendering/engine dependencies. Output is a neutral <see cref="ParticleEmitterTable"/>.
/// </remarks>
public static class ParticleEmitterParser
{
    // ── Sizes (all CONFIRMED from loader analysis) ──────────────────────────
    // spec: Docs/RE/formats/effects.md §E.2.1 Entry header size: 28 bytes (0x1C): CONFIRMED.
    private const int EntryHeaderSize = 28;

    // spec: Docs/RE/formats/effects.md §E.2.2 Sub-record stride: 52 bytes (0x34): CONFIRMED.
    private const int SubRecordStride = 52;

    // spec: Docs/RE/formats/effects.md §E.2.3 Trailing texture name: 64 bytes (0x40): CONFIRMED.
    private const int TextureNameSize = 64;

    // Sub-record inner field offsets (relative to start of sub-record):
    // spec: Docs/RE/formats/effects.md §E.2.2.
    private const int SubRecUnresolvedLeadSize = 8; // +0x00..+0x07: UNRESOLVED
    private const int SubRecColorROffset = 8; // +0x08: MEDIUM
    private const int SubRecColorGOffset = 9; // +0x09: MEDIUM
    private const int SubRecColorBOffset = 10; // +0x0A: MEDIUM
    private const int SubRecColorAOffset = 11; // +0x0B: MEDIUM (active sentinel 0xFF)
    private const int SubRecUnresolvedTailOffset = 12; // +0x0C..+0x33: UNRESOLVED
    private const int SubRecUnresolvedTailSize = 40; // 40 bytes UNRESOLVED

    // Entry header field offsets (all relative to start of entry header):
    // spec: Docs/RE/formats/effects.md §E.2.1.
    private const int HdrEntryIdOffset = 0; // 0x00: u32 LE CONFIRMED
    private const int HdrNumFramesOffset = 4; // 0x04: u32 LE CONFIRMED
    private const int HdrSpriteSizeXOffset = 8; // 0x08: f32 LE HIGH
    private const int HdrSpriteSizeYOffset = 12; // 0x0C: f32 LE HIGH
    private const int HdrMaxParticlesOffset = 16; // 0x10: u32 LE HIGH
    private const int HdrTexHandleSlotOffset = 20; // 0x14: u32 LE MEDIUM (overwritten at load)
    private const int HdrSubrecArrayPtrOffset = 24; // 0x18: u32 LE MEDIUM (overwritten at load)

    /// <summary>
    /// Parses the raw bytes of a <c>particleEmitter.eff</c> file into a <see cref="ParticleEmitterTable"/>.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Decoded table of GPU particle emitter entries.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a declared sub-record block would read past the end of the buffer
    /// (truncated entry). A num_frames==0 sentinel or normal EOF terminates the loop gracefully.
    /// </exception>
    public static ParticleEmitterTable Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span, data);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static ParticleEmitterTable Parse(ReadOnlySpan<byte> data)
        => Parse(data, ReadOnlyMemory<byte>.Empty);

    private static ParticleEmitterTable Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // CP949 for texture names (may contain Korean text).
        // spec: Docs/RE/formats/effects.md §E.2.3 — texture_name NUL-padded ASCII/CP949: CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp949 = Encoding.GetEncoding(949);

        var entries = new List<ParticleEmitterEntry>();
        int offset = 0;

        // Read loop: terminates on num_frames == 0 or < 28 bytes remaining.
        // spec: Docs/RE/formats/effects.md §E.2 — "read loop stops when num_frames is 0 or < 28 bytes remain": CONFIRMED.
        while (offset + EntryHeaderSize <= span.Length)
        {
            ReadOnlySpan<byte> hdr = span.Slice(offset, EntryHeaderSize);

            // entry_id u32 LE @ 0x00.
            // spec: Docs/RE/formats/effects.md §E.2.1 — entry_id u32 LE @ 0x00: CONFIRMED.
            uint entryId = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrEntryIdOffset..]);

            // num_frames u32 LE @ 0x04 — loop terminator if 0.
            // spec: Docs/RE/formats/effects.md §E.2.1 — num_frames u32 LE @ 0x04 AND loop terminator: CONFIRMED.
            uint numFrames = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrNumFramesOffset..]);

            if (numFrames == 0)
                break; // Terminator entry — stop reading.

            // Remaining header fields.
            // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_x f32 LE @ 0x08: HIGH.
            float spriteSizeX = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeXOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_y f32 LE @ 0x0C: HIGH.
            float spriteSizeY = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeYOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — max_particles u32 LE @ 0x10: HIGH.
            uint maxParticles = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrMaxParticlesOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — tex_handle_slot u32 LE @ 0x14: MEDIUM (disk value unused at runtime).
            uint rawTexHandleSlot = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrTexHandleSlotOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — subrecord_array_ptr u32 LE @ 0x18: MEDIUM (disk value unused at runtime).
            uint rawSubrecArrayPtr = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrSubrecArrayPtrOffset..]);

            offset += EntryHeaderSize;

            // Sub-record array: numFrames × 52 bytes.
            // spec: Docs/RE/formats/effects.md §E.2.2 — num_frames × 52-byte sub-records: CONFIRMED.
            long subRecordBlockSize = (long)numFrames * SubRecordStride;
            long totalEntryRemaining = subRecordBlockSize + TextureNameSize;
            if (offset + totalEntryRemaining > span.Length)
                throw new InvalidDataException(
                    $"particleEmitter.eff parse error: entry id={entryId} at offset {offset - EntryHeaderSize} " +
                    $"declares num_frames={numFrames}, requiring {totalEntryRemaining} more bytes " +
                    $"(sub-records + texture name), but only {span.Length - offset} bytes remain. " +
                    "spec: Docs/RE/formats/effects.md §E.2.");

            var subRecords = new ParticleSubRecord[numFrames];
            for (uint f = 0; f < numFrames; f++)
            {
                int srOffset = offset + (int)f * SubRecordStride;
                ReadOnlySpan<byte> sr = span.Slice(srOffset, SubRecordStride);

                // Unresolved lead: +0x00..+0x07 (8 bytes). UNRESOLVED.
                // spec: Docs/RE/formats/effects.md §E.2.2 — _unresolved_lead_ @ +0x00: UNRESOLVED.
                ReadOnlyMemory<byte> unresolvedLead = backing.IsEmpty
                    ? sr[0..SubRecUnresolvedLeadSize].ToArray()
                    : backing.Slice(srOffset, SubRecUnresolvedLeadSize);

                // Colour quad: +0x08..+0x0B. MEDIUM confidence.
                // spec: Docs/RE/formats/effects.md §E.2.2 — color_r/g/b/a @ +0x08..+0x0B: MEDIUM.
                byte colorR = sr[SubRecColorROffset];
                byte colorG = sr[SubRecColorGOffset];
                byte colorB = sr[SubRecColorBOffset];
                byte colorA = sr[SubRecColorAOffset];

                // Unresolved tail: +0x0C..+0x33 (40 bytes). UNRESOLVED.
                // spec: Docs/RE/formats/effects.md §E.2.2 — _unresolved_tail_ @ +0x0C: UNRESOLVED.
                ReadOnlyMemory<byte> unresolvedTail = backing.IsEmpty
                    ? sr[SubRecUnresolvedTailOffset..(SubRecUnresolvedTailOffset + SubRecUnresolvedTailSize)].ToArray()
                    : backing.Slice(srOffset + SubRecUnresolvedTailOffset, SubRecUnresolvedTailSize);

                subRecords[f] = new ParticleSubRecord
                {
                    UnresolvedLead = unresolvedLead,
                    ColorR = colorR,
                    ColorG = colorG,
                    ColorB = colorB,
                    ColorA = colorA,
                    UnresolvedTail = unresolvedTail,
                };
            }

            offset += (int)subRecordBlockSize;

            // Trailing texture name: 64 bytes NUL-padded ASCII/CP949.
            // spec: Docs/RE/formats/effects.md §E.2.3 — texture_name char[64]: CONFIRMED.
            ReadOnlySpan<byte> nameSpan = span.Slice(offset, TextureNameSize);
            string textureName = ReadNulTerminatedCp949(nameSpan, cp949);
            offset += TextureNameSize;

            entries.Add(new ParticleEmitterEntry
            {
                EntryId = entryId,
                NumFrames = numFrames,
                SpriteSizeX = spriteSizeX,
                SpriteSizeY = spriteSizeY,
                MaxParticles = maxParticles,
                RawTexHandleSlot = rawTexHandleSlot,
                RawSubrecordArrayPtr = rawSubrecArrayPtr,
                SubRecords = subRecords,
                TextureName = textureName,
            });
        }

        return new ParticleEmitterTable(entries.ToArray());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a NUL-terminated CP949 string from a fixed-width byte field, stopping at the first
    /// NUL byte or at <paramref name="nameSpan"/>.Length.
    /// </summary>
    private static string ReadNulTerminatedCp949(ReadOnlySpan<byte> nameSpan, Encoding cp949)
    {
        int len = nameSpan.IndexOf((byte)0);
        if (len < 0)
            len = nameSpan.Length;
        if (len == 0)
            return string.Empty;
        // Decode CP949 bytes.
        return cp949.GetString(nameSpan[0..len]);
    }
}