using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Audio.Models;

namespace MartialHeroes.Assets.Parsers.Audio;

/// <summary>
///     Parses per-map sound table files: <c>.bgm</c>, <c>.bge</c>, <c>.eff</c> (sound-table
///     variant only — NOT <c>data/effect/obj/*.eff</c>), <c>.wlk</c>, and <c>.run</c>.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sound_tables.md
///     All five variants share an identical binary layout:
///     256 records × 48 bytes = 12288 bytes read by the loader, followed by a 1024-byte unread
///     trailer, totalling 13312 bytes on disk.
///     spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure — loader stride reconciliation (two-witness):
///     "Record stride: 48 bytes. CONFIRMED (two-witness: loader advance + file measurement)."
///     <para>
///         Two-witness stride correction (2026-06-15): the loader advances 48 bytes (0x30) per record,
///         iterates 256 records, reads 12288 bytes, and leaves a 1024-byte unread trailer.
///         The prior 52-byte stride reading (2026-06-14) is REFUTED — the per-record tail_unknown field
///         at +0x30 does NOT exist; those bytes belong to the file-level unread trailer.
///         spec: Docs/RE/formats/sound_tables.md §Provenance note — stride correction (preserved for audit).
///     </para>
///     Zero rendering/engine dependencies.  Span-based reading, no LINQ, no per-entry boxing.
///     CRITICAL DISAMBIGUATION: the <c>.eff</c> extension is reused for two unrelated types.
///     This parser handles ONLY the sound-table variant found at
///     <c>data/map&lt;id&gt;/soundtable&lt;id&gt;.eff</c>.
///     Do NOT feed <c>data/effect/obj/*.eff</c> geometry shape files to this parser.
///     spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION
/// </remarks>
public static class SoundTableParser
{
    // ─── per-record field offsets (all cited from spec) ────────────────────────

    // sound_entry_id u32 @ entry+0x00
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — sound_entry_id u32 @ +0x00: CONFIRMED
    private const int OffSoundEntryId = 0x00;

    // tod_enable u8×24 @ entry+0x04 — per-hour-of-day enable bitmap, hour 0..23
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — tod_enable u8×24 @ +0x04: CONFIRMED;
    // hour-of-day enable semantics CYCLE 7 control-flow-confirmed (2026-06-21, 3600 divisor + +0x04 base).
    private const int OffTodEnable = 0x04;

    // weight f32 @ entry+0x1C
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — weight f32 @ +0x1C: SAMPLE-VERIFIED type/value; semantic UNVERIFIED
    private const int OffWeight = 0x1C;

    // pos_x f32 @ entry+0x20
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_x f32 @ +0x20: CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED
    private const int OffPosX = 0x20;

    // +0x24 (4 bytes) — NOT read by the loader on any path. Meaning UNRESOLVED.
    // The earlier 'pos_y' label is WITHDRAWN.
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ; meaning UNRESOLVED.
    private const int OffUnlabeled24 = 0x24;

    // pos_z f32 @ entry+0x28
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_z f32 @ +0x28: CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED
    private const int OffPosZ = 0x28;

    // radius f32 @ entry+0x2C
    // spec: Docs/RE/formats/sound_tables.md §Per-record layout — radius f32 @ +0x2C: CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001.
    private const int OffRadius = 0x2C;

    // ─── public API ───────────────────────────────────────────────────────────

    /// <summary>
    ///     Parses a per-map sound table from a <see cref="ReadOnlyMemory{T}" /> buffer.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS. Must be exactly 13312 bytes.</param>
    /// <param name="extension">
    ///     The sound table extension variant, which determines the audio directory used for
    ///     sound-ID path resolution.  Must be derived from the VFS path, not guessed.
    /// </param>
    /// <returns>Decoded <see cref="SoundTableData" /> with all 256 entries.</returns>
    /// <exception cref="InvalidDataException">
    ///     Buffer is not exactly 13312 bytes (the fixed on-disk file size).
    ///     spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)".
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §File layout — 256 records × 48 bytes read; 1024-byte unread trailer:
    ///     CONFIRMED (two-witness).
    /// </remarks>
    public static SoundTableData Parse(ReadOnlyMemory<byte> data, SoundTableExtension extension)
    {
        return Parse(data.Span, extension);
    }

    /// <summary>
    ///     Parses a per-map sound table from a <see cref="ReadOnlySpan{T}" /> buffer.
    /// </summary>
    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte}, SoundTableExtension)" />
    public static SoundTableData Parse(ReadOnlySpan<byte> span, SoundTableExtension extension)
    {
        // Validate exact file size: 13312 (0x3400) bytes.
        // The loader reads the first 12288 bytes (256 × 48); the final 1024 bytes are an unread trailer.
        // spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED.
        // spec: Docs/RE/formats/sound_tables.md §File layout — "Record stride: 48 bytes. CONFIRMED (two-witness)."
        // NOTE: a relaxation to ">= 12288 (the read region)" is spec-justified by sound.md §6.3 (the
        // loader reads only the first 0x3000 bytes), but it is DEFERRED here because it must be applied
        // together with SoundTableParserTests.cs (another lane's file), which pins the exact-13312
        // contract (Parse_BufferLargerThanFixedFileSize / Parse_LengthNotExactly13312).
        if (span.Length != SoundTableData.FixedFileSize)
            throw new InvalidDataException(
                $"Sound table parse error: buffer is {span.Length} bytes; " +
                $"expected exactly {SoundTableData.FixedFileSize} (0x{SoundTableData.FixedFileSize:X4}) bytes " +
                $"(= {SoundTableData.EntryCount} records × {SoundTableData.EntryStride} bytes + " +
                $"{SoundTableData.TrailerSize} bytes unread trailer). " +
                "spec: Docs/RE/formats/sound_tables.md §File layout.");

        // Decode all 256 entries. Stride = 48 bytes (0x30) per record.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 48 bytes. CONFIRMED (two-witness)."
        var entries = new SoundTableEntry[SoundTableData.EntryCount];
        for (var i = 0; i < SoundTableData.EntryCount; i++)
        {
            // Entry i starts at byte i × 48.
            // spec: Docs/RE/formats/sound_tables.md §Per-record layout — stride 48 bytes: CONFIRMED (two-witness, 2026-06-15).
            var entryBase = i * SoundTableData.EntryStride;
            entries[i] = DecodeEntry(span, entryBase);
        }

        return new SoundTableData
        {
            Extension = extension,
            Entries = entries
        };
    }

    // ─── core implementation ──────────────────────────────────────────────────

    private static SoundTableEntry DecodeEntry(ReadOnlySpan<byte> span, int entryBase)
    {
        // sound_entry_id u32 LE @ entry+0x00
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — sound_entry_id u32 @ +0x00: CONFIRMED
        var soundEntryId = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffSoundEntryId)..]);

        // tod_enable u8×24 @ entry+0x04 — per-hour-of-day enable bitmap
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — tod_enable u8×24 @ +0x04: CONFIRMED;
        // hour-of-day enable semantics CYCLE 7 control-flow-confirmed (2026-06-21): ambient driver reads
        // record[+0x04 + hour] (hour = time_of_day_ms / 3600); zero suppresses, non-zero (re)starts.
        var todEnable = new HourSchedule24();
        span.Slice(entryBase + OffTodEnable, SoundTableData.HoursPerDay)
            .CopyTo(todEnable.AsSpan());

        // weight f32 LE @ entry+0x1C
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — weight f32 @ +0x1C: SAMPLE-VERIFIED type/value; semantic UNVERIFIED
        var weight = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffWeight)..]);

        // pos_x f32 LE @ entry+0x20
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_x f32 @ +0x20: CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED
        var posX = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosX)..]);

        // +0x24 (4 bytes) — read verbatim for round-trip fidelity but NOT consumed by the loader.
        // The earlier 'pos_y' label is WITHDRAWN. Meaning UNRESOLVED.
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ by loader; meaning UNRESOLVED.
        var unlabeled24 = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffUnlabeled24)..]);

        // pos_z f32 LE @ entry+0x28
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — pos_z f32 @ +0x28: CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED
        var posZ = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosZ)..]);

        // radius f32 LE @ entry+0x2C
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — radius f32 @ +0x2C: CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001.
        var radius = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffRadius)..]);

        // End of 48-byte record at entryBase+0x2F. The next record begins at entryBase+0x30.
        // There is NO tail field after +0x2C in the 48-byte record — the +0x30 bytes belong
        // to the file-level 1024-byte unread trailer (after the 256th record).
        // spec: Docs/RE/formats/sound_tables.md §Per-record layout — "end of 48-byte record; next record begins at +0x30": CONFIRMED.

        return new SoundTableEntry
        {
            SoundEntryId = soundEntryId,
            HourSchedule = todEnable,
            Weight = weight,
            PosX = posX,
            Unlabeled24 = unlabeled24,
            PosZ = posZ,
            Radius = radius
        };
    }

    // ─── helper: extension from file path ─────────────────────────────────────

    /// <summary>
    ///     Infers the <see cref="SoundTableExtension" /> from a VFS path or filename.
    ///     Throws <see cref="ArgumentException" /> if the extension is not one of the five known sound-table
    ///     extensions, or if the path matches the geometry-shape <c>data/effect/obj/*.eff</c> pattern.
    /// </summary>
    /// <param name="vfsPath">
    ///     The VFS-relative path, e.g. <c>data/map002/soundtable2.bgm</c>.
    /// </param>
    /// <returns>The corresponding <see cref="SoundTableExtension" /> enum value.</returns>
    /// <exception cref="ArgumentException">
    ///     The extension is not a known sound-table extension, or the path is the geometry-shape
    ///     <c>data/effect/obj/*.eff</c> variant.
    /// </exception>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION — .eff path patterns.
    ///     spec: Docs/RE/formats/sound_tables.md §Identification — Extensions.
    /// </remarks>
    public static SoundTableExtension ExtensionFromPath(string vfsPath)
    {
        if (string.IsNullOrEmpty(vfsPath))
            throw new ArgumentException("VFS path must not be null or empty.", nameof(vfsPath));

        // Guard against the geometry-shape .eff variant.
        // spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION
        //   "data/effect/obj/*.eff" → 3D triangle mesh shape, NOT a sound table.
        var normalised = vfsPath.Replace('\\', '/');
        if (normalised.Contains("data/effect/obj/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"The path '{vfsPath}' points to a 3D geometry shape file (data/effect/obj/*.eff), " +
                "which is NOT a sound table. Do not parse it with SoundTableParser. " +
                "spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION.",
                nameof(vfsPath));

        var ext = Path.GetExtension(vfsPath).ToLowerInvariant();
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
                nameof(vfsPath))
        };
    }
}