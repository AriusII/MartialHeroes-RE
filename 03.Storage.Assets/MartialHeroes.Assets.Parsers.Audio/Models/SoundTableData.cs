using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Audio.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Sound table format — .bgm / .bge / .eff / .wlk / .run
//  spec: Docs/RE/formats/sound_tables.md
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     Identifies the per-map sound table file extension variant.
///     The extension determines the audio directory used for sound-ID resolution.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sound_tables.md §Semantic mapping of the five sound-table extensions
/// </remarks>
public enum SoundTableExtension : byte
{
    /// <summary>Background music zones; IDs resolve under <c>data/sound/2d/</c>.</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .bgm → data/sound/2d/: SAMPLE-VERIFIED</remarks>
    Bgm,

    /// <summary>Looped ambient sound effects; IDs resolve under <c>data/sound/2d/</c> (category 0, 2D). SAMPLE-VERIFIED.</summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — .bge → data/sound/2d/: SAMPLE-VERIFIED
    ///     (2026-06-14)
    /// </remarks>
    Bge,

    /// <summary>Triggered 3-D point-source sound events; IDs resolve under <c>data/sound/3d/</c>.</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .eff (sound table variant) → data/sound/3d/: SAMPLE-VERIFIED</remarks>
    Eff,

    /// <summary>Walk footstep sounds; directory UNDETERMINED (all observed entries are null).</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .wlk → directory UNDETERMINED: SAMPLE-VERIFIED</remarks>
    Wlk,

    /// <summary>Run footstep sounds; directory UNDETERMINED (all observed entries are null).</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .run → directory UNDETERMINED: SAMPLE-VERIFIED</remarks>
    Run
}

/// <summary>
///     Inline-array value type holding exactly 24 per-in-game-hour enable bytes.
///     Eliminates the heap allocation that <c>byte[24]</c> would incur per entry (256 × per Parse call).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sound_tables.md §Per-record layout — tod_enable u8×24 @ +0x04:
///     CONFIRMED structure. Semantics CYCLE 7 control-flow-confirmed (2026-06-21): the ambient driver
///     computes <c>hour = time_of_day_ms / 3600</c> (clamped 0..23) and tests <c>record[+0x04 + hour]</c>;
///     a zero byte suppresses/stops the cue for that hour, non-zero (re)starts it. This is the per-hour-of-day
///     enable bitmap that gates whether the row plays at the current in-game hour.
///     <c>[InlineArray(24)]</c> lets the runtime store 24 bytes as a direct struct field,
///     accessed via <see cref="this[int]" /> or <see cref="AsSpan" />/<see cref="AsReadOnlySpan" />.
///     <c>foreach</c> over an instance works natively in C# 12 (compiler lowers to span iteration).
/// </remarks>
[InlineArray(SoundTableData.HoursPerDay)] // spec: tod_enable u8×24 @ +0x04 — Docs/RE/formats/sound_tables.md
public struct HourSchedule24
{
    // Single private field required by [InlineArray].
    // The runtime expands this to 24 consecutive bytes.
    private byte _e0;

    /// <summary>
    ///     Always 24 — the fixed number of in-game hours per day.
    ///     Exposed as a convenience so callers can write <c>schedule.Length</c> instead
    ///     of the magic constant.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — tod_enable u8×24 @ +0x04: CONFIRMED</remarks>
    public int Length => SoundTableData.HoursPerDay; // 24

    /// <summary>
    ///     Returns a writable span over all 24 hour bytes.
    ///     Use to fill the schedule during parsing (zero-copy, no allocation).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — tod_enable u8×24 @ +0x04: CONFIRMED</remarks>
    public Span<byte> AsSpan()
    {
        return MemoryMarshal.CreateSpan(ref _e0, SoundTableData.HoursPerDay);
    }

    /// <summary>
    ///     Returns a read-only span over all 24 hour bytes.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — tod_enable u8×24 @ +0x04: CONFIRMED</remarks>
    public ReadOnlySpan<byte> AsReadOnlySpan()
    {
        return MemoryMarshal.CreateReadOnlySpan(ref _e0, SoundTableData.HoursPerDay);
    }
}

/// <summary>
///     One 48-byte on-disk entry in a per-map sound table file.
///     Entry index 0 is always the null/unassigned sentinel (sound_entry_id = 0).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sound_tables.md §Per-record layout (48 bytes on disk, little-endian throughout)
///     <para>
///         Two-witness stride correction (2026-06-15): on-disk stride is <b>48 bytes</b> (CONFIRMED).
///         The loader advances 0x30 bytes per record, iterates 256 records, reads exactly 12288 bytes,
///         and leaves a 1024-byte unread trailer at the end of the 13312-byte file.
///         The prior 52-byte reading (2026-06-14) is REFUTED; the per-record `tail_unknown` field at +0x30
///         does NOT exist — it belongs to the file-level unread trailer.
///         spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure — loader stride reconciliation
///         (two-witness).
///     </para>
///     Byte-level field map (quick reference):
///     <code>
/// [+0x00..+0x03]  sound_entry_id   u32 LE    (0 = null/unassigned)
/// [+0x04..+0x1B]  tod_enable       u8 × 24   (per-hour-of-day enable bitmap, hour 0..23; CYCLE 7 control-flow-confirmed)
/// [+0x1C..+0x1F]  weight           f32 LE    (1.0f for BGM/BGE; semantic UNVERIFIED)
/// [+0x20..+0x23]  pos_x            f32 LE    (3D world X; read; EFF records only, else 0.0)
/// [+0x24..+0x27]  unlabeled_24     4 bytes   (NOT read by the loader; meaning UNRESOLVED)
/// [+0x28..+0x2B]  pos_z            f32 LE    (3D world Z; read; EFF records only, else 0.0)
/// [+0x2C..+0x2F]  radius           f32 LE    (3D audibility radius; read; EFF records only, else 0.0)
/// --- end of 48-byte record ---
/// </code>
/// </remarks>
public sealed class SoundTableEntry
{
    /// <summary>
    ///     Numeric resource key; 0 = empty/unassigned slot.
    ///     Active samples carry 9-digit decimal values.
    ///     The plain decimal string of this value forms the audio filename stem (no zero-padding).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md — sound_entry_id u32 @ +0x00: CONFIRMED
    /// </remarks>
    public required uint SoundEntryId { get; init; }

    /// <summary>
    ///     Per-in-game-hour enable bitmap (<c>tod_enable</c> on disk); exactly
    ///     <see cref="SoundTableData.HoursPerDay" /> = 24 bytes.
    ///     <c>HourSchedule[h]</c> non-zero → sound active during in-game hour h,
    ///     where h = time_of_day_ms / 3600 (integer division, 0..23).
    ///     CYCLE 7 control-flow-confirmed (2026-06-21): the ambient driver reads this field at
    ///     <c>record[+0x04 + hour]</c> — a zero byte suppresses the cue for that hour; non-zero (re)starts it.
    ///     All-0x01 in simple/null samples means "enabled every hour"; per-hour patterns gate specific hours.
    ///     Stored as an <see cref="HourSchedule24" /> inline-array struct to avoid allocating
    ///     a <c>byte[24]</c> per entry.
    ///     Use <see cref="HourSchedule24.AsReadOnlySpan" /> or direct indexing for access.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §Per-record layout — tod_enable u8×24 @ +0x04:
    ///     CONFIRMED structure; hour-of-day enable semantics CYCLE 7 control-flow-confirmed (2026-06-21,
    ///     3600 divisor + +0x04 base explicit in the ambient driver). Formerly labelled hour_schedule;
    ///     the semantics were DBG-pending — now RESOLVED as the per-hour-of-day enable bitmap.
    /// </remarks>
    public required HourSchedule24 HourSchedule { get; init; }

    /// <summary>
    ///     Blend weight or attenuation scalar; 1.0f in BGM/BGE records.
    ///     Semantic unverified.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-VERIFIED type/value; semantic UNVERIFIED
    /// </remarks>
    public required float Weight { get; init; }

    /// <summary>
    ///     World-space X coordinate of the DirectSound 3D source.
    ///     Populated (non-zero) only in <c>.eff</c> records; 0.0 for BGM/BGE.
    ///     Passed to IDirectSound3DBuffer::SetPosition as the X argument.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md — pos_x f32 @ +0x20: CONFIRMED (runtime semantic); EFF-only population
    ///     SAMPLE-VERIFIED
    /// </remarks>
    public required float PosX { get; init; }

    /// <summary>
    ///     4 bytes at record offset +0x24 that are NOT read by the loader on any path.
    ///     The earlier <c>pos_y</c> label is WITHDRAWN — no read site assigns a meaning to this offset.
    ///     Preserved verbatim for round-trip fidelity; do not interpret.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §Per-record layout — unlabeled_24 @ +0x24: NOT-READ by loader; meaning
    ///     UNRESOLVED.
    /// </remarks>
    public required uint Unlabeled24 { get; init; }

    /// <summary>
    ///     World-space Z coordinate of the DirectSound 3D source.
    ///     Populated only in <c>.eff</c> records; 0.0 for BGM/BGE.
    ///     Passed to IDirectSound3DBuffer::SetPosition as the Z argument.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md — pos_z f32 @ +0x28: CONFIRMED (runtime semantic); EFF-only population
    ///     SAMPLE-VERIFIED
    /// </remarks>
    public required float PosZ { get; init; }

    /// <summary>
    ///     Audibility radius of the 3D source.
    ///     Populated only in <c>.eff</c> records; 0.0 for BGM/BGE.
    ///     For BGM, the runtime applies a 0.7 volume scaling at a separate stage.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md — radius f32 @ +0x2C: CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED
    ///     area 001.
    /// </remarks>
    public required float Radius { get; init; }

    /// <summary>Returns true when this entry represents a real (non-null) sound assignment.</summary>
    public bool IsAssigned => SoundEntryId != 0;
}

/// <summary>
///     Decoded result of a per-map sound table file (.bgm / .bge / .eff / .wlk / .run).
///     Contains exactly 256 entries. The loader reads 256 × 48 = 12288 bytes; the remaining 1024 bytes
///     of the fixed 13312-byte on-disk file are an unread trailer.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400), confirmed across 12 real samples
///     and ~300 census tables"
///     spec: Docs/RE/formats/sound_tables.md §Entry count — "256 entries fixed; no count field in file"
///     spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 48 bytes. CONFIRMED (two-witness: loader
///     advance + file measurement)."
///     <para>
///         Two-witness stride correction (2026-06-15): stride is 48 bytes (not 52). The loader advances
///         0x30 = 48 bytes per record, reads 256 × 48 = 12288 bytes, and the remaining 1024 bytes are an
///         unread trailer. The prior 52-byte reading (2026-06-14) is REFUTED.
///         spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure — loader stride reconciliation
///         (two-witness).
///     </para>
/// </remarks>
public sealed class SoundTableData
{
    // ─── layout constants (all spec-cited) ────────────────────────────────────

    /// <summary>
    ///     Fixed total file size: 13312 bytes (0x3400).
    ///     Confirmed across 12 real-file samples and re-confirmed across ~300 tables by VFS census (2026-06-14).
    ///     The loader reads only the first 12288 bytes (256 × 48); the remaining 1024 are an unread trailer.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED</remarks>
    public const int FixedFileSize = 0x3400; // 13312

    /// <summary>
    ///     Fixed entry count: always 256. No count field exists in the file.
    ///     Entry index 0 is the null sentinel.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED</remarks>
    public const int EntryCount = 256;

    /// <summary>
    ///     Byte stride of one on-disk record: 48 bytes (0x30).
    ///     The loader advances 48 bytes per record and reads 256 × 48 = 12288 bytes total.
    ///     The remaining 1024 bytes of the 13312-byte file are an unread trailer.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 48 bytes. CONFIRMED (two-witness: loader
    ///     advance + file measurement)."
    ///     spec: Docs/RE/formats/sound_tables.md §File layout — "Record table (read): 12288 bytes (0x3000); Unread trailer:
    ///     1024 bytes (0x0400)".
    ///     Two-witness correction (2026-06-15): the prior 52-byte stride reading (2026-06-14) is REFUTED.
    /// </remarks>
    public const int EntryStride = 48; // 0x30 — spec: sound_tables.md §File layout (two-witness, 2026-06-15)

    /// <summary>
    ///     Total bytes read by the loader from the start of the file: 256 × 48 = 12288 (0x3000).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §File layout — "Record table (read): 12288 bytes (0x3000)": CONFIRMED</remarks>
    public const int ReadSize = 0x3000; // 12288

    /// <summary>
    ///     Size of the loader-ignored trailer at the end of the file: 1024 bytes (0x0400).
    ///     The loader never reads this region. Identified as a <c>u32[256]</c> per-slot present-flag table:
    ///     one dword per record index, value 1 where the matching record is populated, else 0.
    ///     Almost certainly an authoring/editor artifact — the runtime loader never touches it.
    ///     (File offset 0x3000 .. 0x33FF.)
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §File layout — "Present-flag trailer (unread): 1024 bytes (0x0400)":
    ///     CONFIRMED layout; CYCLE 7 Known unknown #1 — identified as u32[256] present-flag table.
    /// </remarks>
    public const int TrailerSize = 0x0400; // 1024

    /// <summary>
    ///     Number of per-hour enable bytes per record (= 24 in-game hours).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — tod_enable u8×24 @ +0x04: CONFIRMED</remarks>
    public const int HoursPerDay = 24;

    // ─── decoded payload ───────────────────────────────────────────────────────

    /// <summary>
    ///     The file-extension variant that was decoded.
    ///     Callers use this to determine the audio directory for sound ID resolution.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — directory depends on table extension: SAMPLE-VERIFIED
    /// </remarks>
    public required SoundTableExtension Extension { get; init; }

    /// <summary>
    ///     All 256 sound table entries.
    ///     Entry 0 is always the null/unassigned sentinel.
    ///     Length is always <see cref="EntryCount" /> = 256.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED</remarks>
    public required SoundTableEntry[] Entries { get; init; }

    /// <summary>
    ///     Returns the canonical audio directory path prefix for this table's extension.
    ///     Returns <see langword="null" /> for extensions whose directory is UNDETERMINED in the spec
    ///     (.wlk, .run — all observed entries are null in samples).
    ///     <c>.bge</c> resolves to <c>data/sound/2d/</c> (SAMPLE-VERIFIED 2026-06-14).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — directory table: SAMPLE-VERIFIED
    /// </remarks>
    public string? AudioDirectory => Extension switch
    {
        // spec: sound_tables.md §Sound ID semantics — .bgm → data/sound/2d/: SAMPLE-VERIFIED
        SoundTableExtension.Bgm => "data/sound/2d/",
        // spec: sound_tables.md §Sound ID semantics — .bge → data/sound/2d/: SAMPLE-VERIFIED (2026-06-14)
        // BGE IDs confirmed present under data/sound/2d/ (category 0, 2D same as BGM).
        SoundTableExtension.Bge => "data/sound/2d/",
        // spec: sound_tables.md §Sound ID semantics — .eff (sound table) → data/sound/3d/: SAMPLE-VERIFIED
        SoundTableExtension.Eff => "data/sound/3d/",
        // spec: sound_tables.md §Sound ID semantics — .wlk, .run → UNDETERMINED (all observed entries are null)
        _ => null
    };
}