using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Sound table format — .bgm / .bge / .eff / .wlk / .run
//  spec: Docs/RE/formats/sound_tables.md
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Identifies the per-map sound table file extension variant.
/// The extension determines the audio directory used for sound-ID resolution.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/sound_tables.md §Semantic mapping of the five sound-table extensions
/// </remarks>
public enum SoundTableExtension : byte
{
    /// <summary>Background music zones; IDs resolve under <c>data/sound/2d/</c>.</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .bgm → data/sound/2d/: SAMPLE-VERIFIED</remarks>
    Bgm,

    /// <summary>Looped ambient sound effects; directory UNDETERMINED (all observed entries are null).</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .bge → directory UNDETERMINED: SAMPLE-VERIFIED</remarks>
    Bge,

    /// <summary>Triggered 3-D point-source sound events; IDs resolve under <c>data/sound/3d/</c>.</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .eff (sound table variant) → data/sound/3d/: SAMPLE-VERIFIED</remarks>
    Eff,

    /// <summary>Walk footstep sounds; directory UNDETERMINED (all observed entries are null).</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .wlk → directory UNDETERMINED: SAMPLE-VERIFIED</remarks>
    Wlk,

    /// <summary>Run footstep sounds; directory UNDETERMINED (all observed entries are null).</summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — .run → directory UNDETERMINED: SAMPLE-VERIFIED</remarks>
    Run,
}

/// <summary>
/// Inline-array value type holding exactly 24 per-hour activity bytes.
/// Eliminates the heap allocation that <c>byte[24]</c> would incur per entry (256 × per Parse call).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/sound_tables.md §Per-record layout —
///   hour_schedule u8×24 @ +0x04: CONFIRMED (structure and access pattern);
///   value-pattern semantics (hour mask vs. sub-area/weather/time-of-day filter) UNVERIFIED.
///
/// <c>[InlineArray(24)]</c> lets the runtime store 24 bytes as a direct struct field,
/// accessed via <see cref="this[int]"/> or <see cref="AsSpan"/>/<see cref="AsReadOnlySpan"/>.
/// <c>foreach</c> over an instance works natively in C# 12 (compiler lowers to span iteration).
/// </remarks>
[InlineArray(SoundTableData.HoursPerDay)] // spec: hour_schedule u8×24 @ +0x04
public struct HourSchedule24
{
    // Single private field required by [InlineArray].
    // The runtime expands this to 24 consecutive bytes.
    private byte _e0;

    /// <summary>
    /// Always 24 — the fixed number of in-game hours per day.
    /// Exposed as a convenience so callers can write <c>schedule.Length</c> instead
    /// of the magic constant.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — hour_schedule u8×24: CONFIRMED</remarks>
    public int Length => SoundTableData.HoursPerDay; // 24

    /// <summary>
    /// Returns a writable span over all 24 hour bytes.
    /// Use to fill the schedule during parsing (zero-copy, no allocation).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — hour_schedule u8×24 @ +0x04: CONFIRMED</remarks>
    public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref _e0, SoundTableData.HoursPerDay);

    /// <summary>
    /// Returns a read-only span over all 24 hour bytes.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — hour_schedule u8×24 @ +0x04: CONFIRMED</remarks>
    public ReadOnlySpan<byte> AsReadOnlySpan() =>
        MemoryMarshal.CreateReadOnlySpan(ref _e0, SoundTableData.HoursPerDay);
}

/// <summary>
/// One 52-byte on-disk entry in a per-map sound table file.
/// Entry index 0 is always the null/unassigned sentinel (sound_entry_id = 0).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/sound_tables.md §Per-record layout (52 bytes on disk, little-endian throughout)
/// <para>
/// Stride correction (2026-06-14): on-disk stride is <b>52 bytes</b> (SAMPLE-VERIFIED).
/// The earlier (2026-06-11) interpretation of 48 bytes per record plus a trailing 1024-byte
/// "editor metadata" region is superseded; both agree on the total file size (13312) and on the
/// first 0x2C bytes of every record, but the on-disk parser must use stride 52.
/// spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure — 52-byte stride: SAMPLE-VERIFIED.
/// </para>
/// Byte-level field map (quick reference):
/// <code>
/// [+0x00..+0x03]  sound_entry_id   u32 LE    (0 = null/unassigned)
/// [+0x04..+0x1B]  hour_schedule    u8 × 24   (per-byte 0x00/0x01 mask)
/// [+0x1C..+0x1F]  weight           f32 LE    (1.0f for BGM/BGE)
/// [+0x20..+0x23]  pos_x            f32 LE    (3D world X; EFF only, else 0.0)
/// [+0x24..+0x27]  pos_y            f32 LE    (3D world Y; EFF only, else 0.0)
/// [+0x28..+0x2B]  pos_z            f32 LE    (3D world Z; EFF only, else 0.0)
/// [+0x2C..+0x2F]  radius           f32 LE    (3D audibility radius; EFF only, else 0.0)
/// [+0x30..+0x33]  tail_unknown     4 bytes   (uncharacterised; observed 0 in non-EFF records)
/// </code>
/// </remarks>
public sealed class SoundTableEntry
{
    /// <summary>
    /// Numeric resource key; 0 = empty/unassigned slot.
    /// Active samples carry 9-digit decimal values.
    /// The plain decimal string of this value forms the audio filename stem (no zero-padding).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — sound_entry_id u32 @ +0x00: CONFIRMED
    /// </remarks>
    public required uint SoundEntryId { get; init; }

    /// <summary>
    /// Per-in-game-hour activity flags; exactly <see cref="SoundTableData.HoursPerDay"/> = 24 bytes.
    /// <c>HourSchedule[h]</c> non-zero → sound active during game hour h,
    /// where h = game_clock_seconds / 3600 (integer division).
    /// Value-pattern semantics (hour mask vs. sub-area/weather/time-of-day filter) UNVERIFIED.
    ///
    /// Stored as an <see cref="HourSchedule24"/> inline-array struct to avoid allocating
    /// a <c>byte[24]</c> per entry.
    /// Use <see cref="HourSchedule24.AsReadOnlySpan"/> or direct indexing for access.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — hour_schedule u8×24 @ +0x04: CONFIRMED (structure and access pattern);
    ///   value-pattern semantics UNVERIFIED.
    /// </remarks>
    public required HourSchedule24 HourSchedule { get; init; }

    /// <summary>
    /// Blend weight or attenuation scalar; 1.0f in BGM/BGE records.
    /// Semantic unverified.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-VERIFIED type/value; semantic UNVERIFIED
    /// </remarks>
    public required float Weight { get; init; }

    /// <summary>
    /// World-space X coordinate of the DirectSound 3D source.
    /// Populated (non-zero) only in <c>.eff</c> records; 0.0 for BGM/BGE.
    /// Passed to IDirectSound3DBuffer::SetPosition as the X argument.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — pos_x f32 @ +0x20: CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED
    /// </remarks>
    public required float PosX { get; init; }

    /// <summary>
    /// World-space Y coordinate of the DirectSound 3D source.
    /// Populated only in <c>.eff</c> records; 0.0 (or editor-uninitialized fill) for BGM/BGE.
    /// (Previously labelled <c>unknown_36</c> — resolved 2026-06-14 via EFF field census.)
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — pos_y f32 @ +0x24: CONFIRMED for EFF (SAMPLE-VERIFIED area 001); UNRESOLVED for non-EFF.
    /// The earlier 'unknown_36' label at this offset has been superseded.
    /// </remarks>
    public required float PosY { get; init; }

    /// <summary>
    /// World-space Z coordinate of the DirectSound 3D source.
    /// Populated only in <c>.eff</c> records; 0.0 for BGM/BGE.
    /// Passed to IDirectSound3DBuffer::SetPosition as the Z argument.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — pos_z f32 @ +0x28: CONFIRMED (runtime semantic); EFF-only population SAMPLE-VERIFIED
    /// </remarks>
    public required float PosZ { get; init; }

    /// <summary>
    /// Audibility radius of the 3D source (formerly labelled <c>volume_factor</c>).
    /// Populated only in <c>.eff</c> records; 0.0 for BGM/BGE.
    /// For BGM, the runtime applies a 0.7 volume scaling at a separate stage.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — radius f32 @ +0x2C: CONFIRMED f32 type; EFF radius role SAMPLE-VERIFIED area 001.
    /// The earlier 'volume_factor' label at this offset has been superseded.
    /// </remarks>
    public required float Radius { get; init; }

    /// <summary>
    /// Trailing 4 bytes of the 52-byte on-disk slot (+0x30..+0x33).
    /// Not characterised by either analysis; observed 0 in non-EFF records. Purpose UNRESOLVED.
    /// Preserved verbatim for round-trip fidelity.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — tail_unknown 4 bytes @ +0x30: UNRESOLVED.
    /// </remarks>
    public required uint TailUnknown { get; init; }

    /// <summary>Returns true when this entry represents a real (non-null) sound assignment.</summary>
    public bool IsAssigned => SoundEntryId != 0;
}

/// <summary>
/// Decoded result of a per-map sound table file (.bgm / .bge / .eff / .wlk / .run).
/// Contains exactly 256 entries. All 256 records occupy the full 13312-byte file (256 × 52 bytes).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400), confirmed across 12 real samples and ~300 census tables"
/// spec: Docs/RE/formats/sound_tables.md §Entry count — "256 entries fixed; no count field in file"
/// spec: Docs/RE/formats/sound_tables.md §Per-record layout — "52 bytes per record on disk (SAMPLE-VERIFIED)"
/// <para>
/// On-disk stride correction (2026-06-14): stride is 52 bytes (not 48). There is NO separate
/// trailing "editor metadata" region — the 256 × 52 = 13312 accounts for the entire file.
/// The old <c>EntryTableSize</c> / <c>EditorMetadataSize</c> split is no longer used.
/// spec: Docs/RE/formats/sound_tables.md §File layout §Overall structure — stride 52: SAMPLE-VERIFIED.
/// </para>
/// </remarks>
public sealed class SoundTableData
{
    // ─── layout constants (all spec-cited) ────────────────────────────────────

    /// <summary>
    /// Fixed total file size: 13312 bytes (0x3400).
    /// Confirmed across 12 real-file samples and re-confirmed across ~300 tables by VFS census (2026-06-14).
    /// The entire file is the record table: 256 records × 52 bytes = 13312.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED</remarks>
    public const int FixedFileSize = 0x3400; // 13312

    /// <summary>
    /// Fixed entry count: always 256. No count field exists in the file.
    /// Entry index 0 is the null sentinel.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED</remarks>
    public const int EntryCount = 256;

    /// <summary>
    /// Byte stride of one on-disk record: 52 bytes.
    /// 256 × 52 = 13312 = <see cref="FixedFileSize"/>. SAMPLE-VERIFIED.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md §Per-record layout — "Record stride: 52 bytes on disk. SAMPLE-VERIFIED."
    /// The earlier (2026-06-11) value of 48 corresponded to the estimated in-memory record size; the
    /// on-disk parser must use 52. The old 48 + 1024 "editor metadata" split is superseded.
    /// </remarks>
    public const int
        EntryStride = 52; // CORRECTED from 48 — spec: sound_tables.md §File layout §Overall structure (2026-06-14)

    /// <summary>
    /// Number of per-hour schedule bytes per record.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md — hour_schedule u8×24 @ +0x04: CONFIRMED</remarks>
    public const int HoursPerDay = 24;

    // ─── decoded payload ───────────────────────────────────────────────────────

    /// <summary>
    /// The file-extension variant that was decoded.
    /// Callers use this to determine the audio directory for sound ID resolution.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — directory depends on table extension: SAMPLE-VERIFIED
    /// </remarks>
    public required SoundTableExtension Extension { get; init; }

    /// <summary>
    /// All 256 sound table entries.
    /// Entry 0 is always the null/unassigned sentinel.
    /// Length is always <see cref="EntryCount"/> = 256.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED</remarks>
    public required SoundTableEntry[] Entries { get; init; }

    /// <summary>
    /// Returns the canonical audio directory path prefix for this table's extension.
    /// Returns <see langword="null"/> for extensions whose directory is UNDETERMINED in the spec
    /// (.bge, .wlk, .run — all observed entries are null in samples).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md §Sound ID semantics — directory table: SAMPLE-VERIFIED
    /// </remarks>
    public string? AudioDirectory => Extension switch
    {
        // spec: .bgm → data/sound/2d/: SAMPLE-VERIFIED
        SoundTableExtension.Bgm => "data/sound/2d/",
        // spec: .eff (sound table) → data/sound/3d/: SAMPLE-VERIFIED
        SoundTableExtension.Eff => "data/sound/3d/",
        // spec: .bge, .wlk, .run → UNDETERMINED
        _ => null,
    };
}