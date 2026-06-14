using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parses per-map sound table files: <c>.bgm</c>, <c>.bge</c>, <c>.eff</c> (sound-table
/// variant only — NOT <c>data/effect/obj/*.eff</c>), <c>.wlk</c>, and <c>.run</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/sound_tables.md
///
/// All five variants share an identical binary layout:
///   256 records × 52 bytes = 13312 bytes total (the entire file).
/// spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure —
///   "Record stride: 52 bytes on disk. SAMPLE-VERIFIED."
/// <para>
/// On-disk stride correction (2026-06-14): the on-disk stride is 52 bytes, NOT 48.
/// The prior "48-byte record table + 1024-byte editor-metadata" model is SUPERSEDED.
/// The on-disk file is a flat 256 × 52 = 13312 byte table with no trailing region.
/// spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure — stride 52: SAMPLE-VERIFIED.
/// </para>
///
/// Zero rendering/engine dependencies.  Span-based reading, no LINQ, no per-entry boxing.
///
/// CRITICAL DISAMBIGUATION: the <c>.eff</c> extension is reused for two unrelated types.
/// This parser handles ONLY the sound-table variant found at
/// <c>data/map&lt;id&gt;/soundtable&lt;id&gt;.eff</c>.
/// Do NOT feed <c>data/effect/obj/*.eff</c> geometry shape files to this parser.
/// spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION
/// </remarks>
public static class SoundTableParser
{
    // ─── per-record field offsets (all cited from spec) ────────────────────────

    // sound_entry_id u32 @ entry+0x00
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — sound_entry_id u32 @ +0x00: CONFIRMED
    private const int OffSoundEntryId = 0x00;

    // hour_schedule u8×24 @ entry+0x04
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — hour_schedule u8×24 @ +0x04: CONFIRMED
    private const int OffHourSchedule = 0x04;

    // weight f32 @ entry+0x1C
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — weight f32 @ +0x1C: SAMPLE-VERIFIED
    private const int OffWeight = 0x1C;

    // pos_x f32 @ entry+0x20
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_x f32 @ +0x20: CONFIRMED
    private const int OffPosX = 0x20;

    // pos_y f32 @ entry+0x24 — formerly labelled 'unknown_36'; resolved 2026-06-14.
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_y f32 @ +0x24:
    //   CONFIRMED for EFF (SAMPLE-VERIFIED area 001); UNRESOLVED for non-EFF.
    private const int OffPosY = 0x24;

    // pos_z f32 @ entry+0x28
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_z f32 @ +0x28: CONFIRMED
    private const int OffPosZ = 0x28;

    // radius f32 @ entry+0x2C — formerly labelled 'volume_factor'; resolved 2026-06-14.
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — radius f32 @ +0x2C:
    //   CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001.
    private const int OffRadius = 0x2C;

    // tail_unknown 4B @ entry+0x30. Purpose UNRESOLVED; observed 0 in non-EFF records.
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — tail_unknown @ +0x30: UNRESOLVED.
    private const int OffTailUnknown = 0x30;

    // ─── public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a per-map sound table from a <see cref="ReadOnlyMemory{T}"/> buffer.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS. Must be exactly 13312 bytes.</param>
    /// <param name="extension">
    /// The sound table extension variant, which determines the audio directory used for
    /// sound-ID path resolution.  Must be derived from the VFS path, not guessed.
    /// </param>
    /// <returns>Decoded <see cref="SoundTableData"/> with all 256 entries.</returns>
    /// <exception cref="InvalidDataException">
    /// Buffer is not exactly 13312 bytes (the fixed on-disk file size).
    /// spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)".
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md §File layout — 256 records × 52 bytes on disk: SAMPLE-VERIFIED.
    /// </remarks>
    public static SoundTableData Parse(ReadOnlyMemory<byte> data, SoundTableExtension extension) =>
        Parse(data.Span, extension);

    /// <summary>
    /// Parses a per-map sound table from a <see cref="ReadOnlySpan{T}"/> buffer.
    /// </summary>
    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte}, SoundTableExtension)"/>
    public static SoundTableData Parse(ReadOnlySpan<byte> span, SoundTableExtension extension)
    {
        // Validate exact file size: 256 × 52 = 13312 (0x3400).
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED.
        // spec: Docs/RE/formats/sound_tables.md §Overall structure — "256 records × 52 bytes = 13312": SAMPLE-VERIFIED.
        if (span.Length != SoundTableData.FixedFileSize)
            throw new InvalidDataException(
                $"Sound table parse error: buffer is {span.Length} bytes; " +
                $"expected exactly {SoundTableData.FixedFileSize} (0x{SoundTableData.FixedFileSize:X4}) bytes " +
                $"(= {SoundTableData.EntryCount} records × {SoundTableData.EntryStride} bytes). " +
                "spec: Docs/RE/formats/sound_tables.md §File layout.");

        // Decode all 256 entries. stride = 52.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 52 bytes on disk": SAMPLE-VERIFIED.
        var entries = new SoundTableEntry[SoundTableData.EntryCount];
        for (int i = 0; i < SoundTableData.EntryCount; i++)
        {
            // Entry i starts at byte i × 52.
            // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Entry stride: 52 bytes": SAMPLE-VERIFIED.
            int entryBase = i * SoundTableData.EntryStride;
            entries[i] = DecodeEntry(span, entryBase);
        }

        return new SoundTableData
        {
            Extension = extension,
            Entries = entries,
        };
    }

    // ─── core implementation ──────────────────────────────────────────────────

    private static SoundTableEntry DecodeEntry(ReadOnlySpan<byte> span, int entryBase)
    {
        // sound_entry_id u32 LE @ entry+0x00
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — sound_entry_id u32 @ +0x00: CONFIRMED
        uint soundEntryId = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffSoundEntryId)..]);

        // hour_schedule u8×24 @ entry+0x04
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        var hourSchedule = new HourSchedule24();
        span.Slice(entryBase + OffHourSchedule, SoundTableData.HoursPerDay)
            .CopyTo(hourSchedule.AsSpan());

        // weight f32 LE @ entry+0x1C
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — weight f32 @ +0x1C: SAMPLE-VERIFIED as 1.0f
        float weight = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffWeight)..]);

        // pos_x f32 LE @ entry+0x20
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_x f32 @ +0x20: CONFIRMED
        float posX = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosX)..]);

        // pos_y f32 LE @ entry+0x24 (formerly 'unknown_36'; resolved 2026-06-14)
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_y f32 @ +0x24:
        //   CONFIRMED for EFF (SAMPLE-VERIFIED area 001); UNRESOLVED for non-EFF.
        float posY = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosY)..]);

        // pos_z f32 LE @ entry+0x28
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_z f32 @ +0x28: CONFIRMED
        float posZ = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosZ)..]);

        // radius f32 LE @ entry+0x2C (formerly 'volume_factor'; resolved 2026-06-14)
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — radius f32 @ +0x2C:
        //   CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001.
        float radius = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffRadius)..]);

        // tail_unknown u32 LE @ entry+0x30. Purpose UNRESOLVED.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — tail_unknown @ +0x30: UNRESOLVED.
        uint tailUnknown = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffTailUnknown)..]);

        return new SoundTableEntry
        {
            SoundEntryId = soundEntryId,
            HourSchedule = hourSchedule,
            Weight = weight,
            PosX = posX,
            PosY = posY,
            PosZ = posZ,
            Radius = radius,
            TailUnknown = tailUnknown,
        };
    }

    // ─── helper: extension from file path ─────────────────────────────────────

    /// <summary>
    /// Infers the <see cref="SoundTableExtension"/> from a VFS path or filename.
    /// Throws <see cref="ArgumentException"/> if the extension is not one of the five known sound-table
    /// extensions, or if the path matches the geometry-shape <c>data/effect/obj/*.eff</c> pattern.
    /// </summary>
    /// <param name="vfsPath">
    /// The VFS-relative path, e.g. <c>data/map002/soundtable2.bgm</c>.
    /// </param>
    /// <returns>The corresponding <see cref="SoundTableExtension"/> enum value.</returns>
    /// <exception cref="ArgumentException">
    /// The extension is not a known sound-table extension, or the path is the geometry-shape
    /// <c>data/effect/obj/*.eff</c> variant.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION — .eff path patterns.
    /// spec: Docs/RE/formats/sound_tables.md §Identification — Extensions.
    /// </remarks>
    public static SoundTableExtension ExtensionFromPath(string vfsPath)
    {
        if (string.IsNullOrEmpty(vfsPath))
            throw new ArgumentException("VFS path must not be null or empty.", nameof(vfsPath));

        // Guard against the geometry-shape .eff variant.
        // spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION
        //   "data/effect/obj/*.eff" → 3D triangle mesh shape, NOT a sound table.
        string normalised = vfsPath.Replace('\\', '/');
        if (normalised.Contains("data/effect/obj/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"The path '{vfsPath}' points to a 3D geometry shape file (data/effect/obj/*.eff), " +
                "which is NOT a sound table. Do not parse it with SoundTableParser. " +
                "spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION.",
                nameof(vfsPath));

        string ext = Path.GetExtension(vfsPath).ToLowerInvariant();
        return ext switch
        {
            // spec: Docs/RE/formats/sound_tables.md §Identification — .bgm: CONFIRMED
            ".bgm" => SoundTableExtension.Bgm,
            // spec: Docs/RE/formats/sound_tables.md §Identification — .bge: CONFIRMED
            ".bge" => SoundTableExtension.Bge,
            // spec: Docs/RE/formats/sound_tables.md §Identification — .eff (sound table variant): CONFIRMED
            ".eff" => SoundTableExtension.Eff,
            // spec: Docs/RE/formats/sound_tables.md §Identification — .wlk: CONFIRMED
            ".wlk" => SoundTableExtension.Wlk,
            // spec: Docs/RE/formats/sound_tables.md §Identification — .run: CONFIRMED
            ".run" => SoundTableExtension.Run,
            _ => throw new ArgumentException(
                $"The extension '{ext}' (from path '{vfsPath}') is not a known sound-table extension. " +
                "Known extensions: .bgm, .bge, .eff (map path only), .wlk, .run. " +
                "spec: Docs/RE/formats/sound_tables.md §Identification.",
                nameof(vfsPath)),
        };
    }
}