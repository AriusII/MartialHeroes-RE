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
///   • Sound entry table: 256 × 48 bytes = 12288 bytes @ offset 0x0000 (runtime region)
///   • Editor metadata: 1024 bytes @ offset 0x3000 (ignored at runtime)
///   • Total on disk: 13312 bytes (0x3400), confirmed across 12 real-file samples.
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
    // ─── entry field offsets (all cited from spec) ────────────────────────────

    // sound_entry_id u32 @ entry+0x00
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — sound_entry_id u32 @ +0x00: CONFIRMED
    private const int OffSoundEntryId = 0x00;

    // hour_schedule u8×24 @ entry+0x04
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — hour_schedule u8×24 @ +0x04: CONFIRMED
    private const int OffHourSchedule = 0x04;

    // weight f32 @ entry+0x1C
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — weight f32 @ +0x1C: SAMPLE-CONFIRMED
    private const int OffWeight = 0x1C;

    // pos_x f32 @ entry+0x20
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — pos_x f32 @ +0x20: CONFIRMED
    private const int OffPosX = 0x20;

    // unknown_36 u32 @ entry+0x24
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — unknown_36 u32 @ +0x24: UNRESOLVED
    private const int OffUnknown36 = 0x24;

    // pos_z f32 @ entry+0x28
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — pos_z f32 @ +0x28: CONFIRMED
    private const int OffPosZ = 0x28;

    // volume_factor f32 @ entry+0x2C
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — volume_factor f32 @ +0x2C: CONFIRMED
    private const int OffVolumeFactor = 0x2C;

    // ─── public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a per-map sound table from a <see cref="ReadOnlyMemory{T}"/> buffer.
    /// </summary>
    /// <param name="data">Raw file bytes from the VFS.</param>
    /// <param name="extension">
    /// The sound table extension variant, which determines the audio directory used for
    /// sound-ID path resolution.  Must be derived from the VFS path, not guessed.
    /// </param>
    /// <returns>Decoded <see cref="SoundTableData"/> with all 256 entries.</returns>
    /// <exception cref="InvalidDataException">
    /// Buffer is shorter than the minimum required 12288-byte entry-table region,
    /// or any other structural constraint fails.
    /// </exception>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md §File layout — runtime loader reads exactly 12288 bytes.
    /// Files of exactly 13312 bytes (full disk format) are accepted and the trailing editor-metadata
    /// region is preserved verbatim.  Files between 12288 and 13312 bytes are also accepted (the
    /// metadata slice will be whatever remains, possibly empty).  Files shorter than 12288 bytes
    /// are rejected with <see cref="InvalidDataException"/>.
    /// </remarks>
    public static SoundTableData Parse(ReadOnlyMemory<byte> data, SoundTableExtension extension) =>
        Parse(data.Span, extension, data);

    /// <summary>
    /// Parses a per-map sound table from a <see cref="ReadOnlySpan{T}"/> buffer.
    /// The trailing editor-metadata region (if present) will be copied to a new allocation.
    /// </summary>
    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte}, SoundTableExtension)"/>
    public static SoundTableData Parse(ReadOnlySpan<byte> span, SoundTableExtension extension) =>
        Parse(span, extension, ReadOnlyMemory<byte>.Empty);

    // ─── core implementation ──────────────────────────────────────────────────

    private static SoundTableData Parse(
        ReadOnlySpan<byte> span,
        SoundTableExtension extension,
        ReadOnlyMemory<byte> backing)
    {
        // Minimum required: exactly the entry-table region (12288 = 0x3000 bytes).
        // The runtime loader reads only this region.
        // spec: Docs/RE/formats/sound_tables.md §Overall structure — "runtime loader reads exactly 12288 bytes": CONFIRMED
        if (span.Length < SoundTableData.EntryTableSize)
            throw new InvalidDataException(
                $"Sound table parse error: buffer is {span.Length} bytes; " +
                $"minimum required is {SoundTableData.EntryTableSize} (the entry-table region). " +
                "spec: Docs/RE/formats/sound_tables.md §Overall structure.");

        // Validate the full-file size when buffer is larger than the minimum.
        // Accept exactly EntryTableSize (runtime-only slice) or FixedFileSize (full disk file).
        // Also tolerate any size in between (partial metadata region).
        if (span.Length > SoundTableData.FixedFileSize)
            throw new InvalidDataException(
                $"Sound table parse error: buffer is {span.Length} bytes, " +
                $"which exceeds the expected maximum file size {SoundTableData.FixedFileSize} (0x{SoundTableData.FixedFileSize:X4}). " +
                "spec: Docs/RE/formats/sound_tables.md §File layout — \"fixed 13312 bytes (0x3400)\".");

        // Decode all 256 entries.
        // spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED
        var entries = new SoundTableEntry[SoundTableData.EntryCount];
        for (int i = 0; i < SoundTableData.EntryCount; i++)
        {
            // Entry i starts at byte i × 48.
            // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — "Entry stride: 48 bytes. Confirmed.": CONFIRMED
            int entryBase = i * SoundTableData.EntryStride;

            entries[i] = DecodeEntry(span, entryBase);
        }

        // Preserve editor-metadata region (bytes 0x3000–0x33FF) when present.
        // spec: Docs/RE/formats/sound_tables.md §Editor metadata region — "bytes 12288–13311, 1024 bytes": CONFIRMED
        ReadOnlyMemory<byte> metadata;
        int metadataStart = SoundTableData.EntryTableSize; // 0x3000 = 12288
        int metadataAvailable = span.Length - metadataStart;
        if (metadataAvailable <= 0)
        {
            metadata = ReadOnlyMemory<byte>.Empty;
        }
        else if (!backing.IsEmpty)
        {
            // Slice from the original backing memory (zero-copy).
            metadata = backing.Slice(metadataStart, metadataAvailable);
        }
        else
        {
            // No backing memory; copy the metadata slice.
            metadata = span.Slice(metadataStart, metadataAvailable).ToArray();
        }

        return new SoundTableData
        {
            Extension = extension,
            Entries = entries,
            RawEditorMetadata = metadata,
        };
    }

    private static SoundTableEntry DecodeEntry(ReadOnlySpan<byte> span, int entryBase)
    {
        // sound_entry_id u32 LE @ entry+0x00
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — sound_entry_id u32 @ +0x00: CONFIRMED
        uint soundEntryId = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffSoundEntryId)..]);

        // hour_schedule u8×24 @ entry+0x04
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — hour_schedule u8×24 @ +0x04: CONFIRMED
        // Fill the inline-array value type via its AsSpan() — zero heap allocation per entry.
        // HourSchedule24 is an [InlineArray(24)] struct; AsSpan() wraps the first field as a Span<byte>.
        var hourSchedule = new HourSchedule24();
        span.Slice(entryBase + OffHourSchedule, SoundTableData.HoursPerDay)
            .CopyTo(hourSchedule.AsSpan());

        // weight f32 LE @ entry+0x1C
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — weight f32 @ +0x1C: SAMPLE-CONFIRMED as 1.0f
        float weight = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffWeight)..]);

        // pos_x f32 LE @ entry+0x20
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — pos_x f32 @ +0x20: CONFIRMED
        float posX = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosX)..]);

        // unknown_36 u32 LE @ entry+0x24
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — unknown_36 u32 @ +0x24: UNRESOLVED
        uint unknown36 = BinaryPrimitives.ReadUInt32LittleEndian(
            span[(entryBase + OffUnknown36)..]);

        // pos_z f32 LE @ entry+0x28
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — pos_z f32 @ +0x28: CONFIRMED
        float posZ = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffPosZ)..]);

        // volume_factor f32 LE @ entry+0x2C
        // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — volume_factor f32 @ +0x2C: CONFIRMED
        float volumeFactor = BinaryPrimitives.ReadSingleLittleEndian(
            span[(entryBase + OffVolumeFactor)..]);

        return new SoundTableEntry
        {
            SoundEntryId = soundEntryId,
            HourSchedule = hourSchedule,
            Weight = weight,
            PosX = posX,
            Unknown36 = unknown36,
            PosZ = posZ,
            VolumeFactor = volumeFactor,
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
        // Normalise separators before the prefix check.
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