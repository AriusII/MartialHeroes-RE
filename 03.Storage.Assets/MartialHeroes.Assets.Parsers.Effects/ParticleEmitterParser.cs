using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers.Effects.Models;

namespace MartialHeroes.Assets.Parsers.Effects;

/// <summary>
///     Parser for the <c>particleEmitter.eff</c> GPU particle emitter descriptor table.
/// </summary>
/// <remarks>
///     File path (VFS): <c>data/effect/particle/particleEmitter.eff</c>
///     (VFS-lowercased to <c>data/effect/particle/particleemitter.eff</c>).
///     Layout: variable-length entry sequence with NO file header, NO magic, NO count prefix.
///     Each entry = 28-byte header + num_frames × 52-byte sub-record + 64-byte texture name.
///     Read loop terminates when an entry header's num_frames == 0 (sentinel) or &lt; 28 bytes remain.
///     spec: Docs/RE/formats/effects.md §E.2 File layout — variable-length entry sequence: CONFIRMED.
///     spec: Docs/RE/formats/effects.md §E.1 Identification — no magic comparison: CONFIRMED.
///     spec: Docs/RE/formats/effects.md §E.0 Correction — flat-table model RETIRED: CONFIRMED.
///     Runtime selection: a .xeff element's resource_id ≥ 10000 selects an entry by RAW entry_id equality.
///     There is NO −10000 subtraction. The ≥ 10000 threshold is a dispatch gate only.
///     spec: Docs/RE/formats/effects.md §E.4 Runtime selection — raw-id equality: CONFIRMED.
///     ZERO rendering/engine dependencies. Output is a neutral <see cref="ParticleEmitterTable" />.
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
    // All 19 fields CODE-CONFIRMED 2026-06-21. spec: Docs/RE/formats/effects.md §E.2.2.
    private const int SubRecLifeBonusOffset = 0; // +0x00 u16 LE CODE-CONFIRMED
    private const int SubRecLifetimeOffset = 2; // +0x02 u16 LE CODE-CONFIRMED
    private const int SubRecSpawnDelayOffset = 4; // +0x04 u16 LE CODE-CONFIRMED
    private const int SubRecSizeInitOffset = 6; // +0x06 u16 LE CODE-CONFIRMED
    private const int SubRecColorROffset = 8; // +0x08 u8  CONFIRMED
    private const int SubRecColorGOffset = 9; // +0x09 u8  CONFIRMED
    private const int SubRecColorBOffset = 10; // +0x0A u8  CONFIRMED
    private const int SubRecColorAOffset = 11; // +0x0B u8  CONFIRMED (genuine alpha)
    private const int SubRecSpawnPosXOffset = 12; // +0x0C f32 LE CODE-CONFIRMED
    private const int SubRecSpawnPosYOffset = 16; // +0x10 f32 LE CODE-CONFIRMED
    private const int SubRecSpawnPosZOffset = 20; // +0x14 f32 LE CODE-CONFIRMED
    private const int SubRecSizeRateOffset = 24; // +0x18 f32 LE CODE-CONFIRMED
    private const int SubRecColorRRateOffset = 28; // +0x1C i16 LE CODE-CONFIRMED
    private const int SubRecColorGRateOffset = 30; // +0x1E i16 LE CODE-CONFIRMED
    private const int SubRecColorBRateOffset = 32; // +0x20 i16 LE CODE-CONFIRMED
    private const int SubRecColorARateOffset = 34; // +0x22 i16 LE CODE-CONFIRMED
    private const int SubRecVelocityXOffset = 36; // +0x24 f32 LE CODE-CONFIRMED
    private const int SubRecVelocityYOffset = 40; // +0x28 f32 LE CODE-CONFIRMED
    private const int SubRecVelocityZOffset = 44; // +0x2C f32 LE CODE-CONFIRMED
    private const int SubRecVelocityDampOffset = 48; // +0x30 f32 LE CODE-CONFIRMED

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
    ///     Parses the raw bytes of a <c>particleEmitter.eff</c> file into a <see cref="ParticleEmitterTable" />.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <returns>Decoded table of GPU particle emitter entries.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown when a declared sub-record block would read past the end of the buffer
    ///     (truncated entry). A num_frames==0 sentinel or normal EOF terminates the loop gracefully.
    /// </exception>
    public static ParticleEmitterTable Parse(ReadOnlyMemory<byte> data)
    {
        return Parse(data.Span, data);
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})" />
    public static ParticleEmitterTable Parse(ReadOnlySpan<byte> data)
    {
        return Parse(data, ReadOnlyMemory<byte>.Empty);
    }

    private static ParticleEmitterTable Parse(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // CP949 for texture names (may contain Korean text).
        // spec: Docs/RE/formats/effects.md §E.2.3 — texture_name NUL-padded ASCII/CP949: CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var entries = new List<ParticleEmitterEntry>();
        var offset = 0;

        // Read loop: terminates on num_frames == 0 or < 28 bytes remaining.
        // spec: Docs/RE/formats/effects.md §E.2 — "read loop stops when num_frames is 0 or < 28 bytes remain": CONFIRMED.
        while (offset + EntryHeaderSize <= span.Length)
        {
            var hdr = span.Slice(offset, EntryHeaderSize);

            // entry_id u32 LE @ 0x00.
            // spec: Docs/RE/formats/effects.md §E.2.1 — entry_id u32 LE @ 0x00: CONFIRMED.
            var entryId = BinaryPrimitives.ReadUInt32LittleEndian(hdr[..]);

            // num_frames u32 LE @ 0x04 — loop terminator if 0.
            // spec: Docs/RE/formats/effects.md §E.2.1 — num_frames u32 LE @ 0x04 AND loop terminator: CONFIRMED.
            var numFrames = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrNumFramesOffset..]);

            if (numFrames == 0)
                break; // Terminator entry — stop reading.

            // Remaining header fields.
            // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_x f32 LE @ 0x08: HIGH.
            var spriteSizeX = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeXOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_y f32 LE @ 0x0C: HIGH.
            var spriteSizeY = BinaryPrimitives.ReadSingleLittleEndian(hdr[HdrSpriteSizeYOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — max_particles u32 LE @ 0x10: HIGH.
            var maxParticles = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrMaxParticlesOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — tex_handle_slot u32 LE @ 0x14: MEDIUM (disk value unused at runtime).
            var rawTexHandleSlot = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrTexHandleSlotOffset..]);
            // spec: Docs/RE/formats/effects.md §E.2.1 — subrecord_array_ptr u32 LE @ 0x18: MEDIUM (disk value unused at runtime).
            var rawSubrecArrayPtr = BinaryPrimitives.ReadUInt32LittleEndian(hdr[HdrSubrecArrayPtrOffset..]);

            offset += EntryHeaderSize;

            // Sub-record array: numFrames × 52 bytes.
            // spec: Docs/RE/formats/effects.md §E.2.2 — num_frames × 52-byte sub-records: CONFIRMED.
            var subRecordBlockSize = (long)numFrames * SubRecordStride;
            var totalEntryRemaining = subRecordBlockSize + TextureNameSize;
            if (offset + totalEntryRemaining > span.Length)
                throw new InvalidDataException(
                    $"particleEmitter.eff parse error: entry id={entryId} at offset {offset - EntryHeaderSize} " +
                    $"declares num_frames={numFrames}, requiring {totalEntryRemaining} more bytes " +
                    $"(sub-records + texture name), but only {span.Length - offset} bytes remain. " +
                    "spec: Docs/RE/formats/effects.md §E.2.");

            var subRecords = new ParticleSubRecord[numFrames];
            for (uint f = 0; f < numFrames; f++)
            {
                var srOffset = offset + (int)f * SubRecordStride;
                var sr = span.Slice(srOffset, SubRecordStride);

                // All 19 fields CODE-CONFIRMED (2026-06-21). spec: Docs/RE/formats/effects.md §E.2.2.

                // +0x00 : 4 × u16 LE timer/size fields.
                var lifeBonus = BinaryPrimitives.ReadUInt16LittleEndian(sr[..]);
                var lifetime = BinaryPrimitives.ReadUInt16LittleEndian(sr[SubRecLifetimeOffset..]);
                var spawnDelay = BinaryPrimitives.ReadUInt16LittleEndian(sr[SubRecSpawnDelayOffset..]);
                var sizeInit = BinaryPrimitives.ReadUInt16LittleEndian(sr[SubRecSizeInitOffset..]);

                // +0x08 : RGBA8 colour quad (genuine alpha — NOT a sentinel).
                // spec: Docs/RE/formats/effects.md §E.2.2 — color_a is genuine initial alpha: CONFIRMED.
                var colorR = sr[SubRecColorROffset];
                var colorG = sr[SubRecColorGOffset];
                var colorB = sr[SubRecColorBOffset];
                var colorA = sr[SubRecColorAOffset];

                // +0x0C : spawn position xyz + size_rate (4 × f32 LE).
                var spawnPosX = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSpawnPosXOffset..]);
                var spawnPosY = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSpawnPosYOffset..]);
                var spawnPosZ = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSpawnPosZOffset..]);
                var sizeRate = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecSizeRateOffset..]);

                // +0x1C : 4 × signed i16 LE colour-rate fields (signed per-second deltas).
                var colorRRate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorRRateOffset..]);
                var colorGRate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorGRateOffset..]);
                var colorBRate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorBRateOffset..]);
                var colorARate = BinaryPrimitives.ReadInt16LittleEndian(sr[SubRecColorARateOffset..]);

                // +0x24 : velocity xyz + velocity_damp (4 × f32 LE).
                var velocityX = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityXOffset..]);
                var velocityY = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityYOffset..]);
                var velocityZ = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityZOffset..]);
                var velocityDamp = BinaryPrimitives.ReadSingleLittleEndian(sr[SubRecVelocityDampOffset..]);

                subRecords[f] = new ParticleSubRecord
                {
                    LifeBonus = lifeBonus,
                    Lifetime = lifetime,
                    SpawnDelay = spawnDelay,
                    SizeInit = sizeInit,
                    ColorR = colorR,
                    ColorG = colorG,
                    ColorB = colorB,
                    ColorA = colorA,
                    SpawnPosX = spawnPosX,
                    SpawnPosY = spawnPosY,
                    SpawnPosZ = spawnPosZ,
                    SizeRate = sizeRate,
                    ColorRRate = colorRRate,
                    ColorGRate = colorGRate,
                    ColorBRate = colorBRate,
                    ColorARate = colorARate,
                    VelocityX = velocityX,
                    VelocityY = velocityY,
                    VelocityZ = velocityZ,
                    VelocityDamp = velocityDamp
                };
            }

            offset += (int)subRecordBlockSize;

            // Trailing texture name: 64 bytes NUL-padded ASCII/CP949.
            // spec: Docs/RE/formats/effects.md §E.2.3 — texture_name char[64]: CONFIRMED.
            var nameSpan = span.Slice(offset, TextureNameSize);
            var textureName = ReadNulTerminatedCp949(nameSpan, cp949);
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
                TextureName = textureName
            });
        }

        return new ParticleEmitterTable(entries.ToArray());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Reads a NUL-terminated CP949 string from a fixed-width byte field, stopping at the first
    ///     NUL byte or at <paramref name="nameSpan" />.Length.
    /// </summary>
    private static string ReadNulTerminatedCp949(ReadOnlySpan<byte> nameSpan, Encoding cp949)
    {
        var len = nameSpan.IndexOf((byte)0);
        if (len < 0)
            len = nameSpan.Length;
        if (len == 0)
            return string.Empty;
        // Decode CP949 bytes.
        return cp949.GetString(nameSpan[..len]);
    }
}