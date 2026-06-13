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
/// spec: Docs/RE/formats/sound_tables.md §Per-entry layout —
///   hour_schedule u8×24 @ +0x04: CONFIRMED (structure and access pattern);
///   value variation UNOBSERVED in samples (all 12 real samples have every byte = 0x01).
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
/// One 48-byte entry in a per-map sound table file.
/// Entry index 0 is always the null/unassigned sentinel (sound_entry_id = 0).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/sound_tables.md §Per-entry layout (48 bytes, little-endian throughout)
/// Byte-level field map (quick reference):
/// <code>
/// [+0x00..+0x03]  sound_entry_id   u32 LE
/// [+0x04..+0x1B]  hour_schedule    u8 × 24
/// [+0x1C..+0x1F]  weight           f32 LE
/// [+0x20..+0x23]  pos_x            f32 LE
/// [+0x24..+0x27]  unknown_36       u32 LE
/// [+0x28..+0x2B]  pos_z            f32 LE
/// [+0x2C..+0x2F]  volume_factor    f32 LE
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
    /// All 12 real-file samples contain every byte = 0x01 (unconditionally active).
    ///
    /// Stored as an <see cref="HourSchedule24"/> inline-array struct to avoid allocating
    /// a <c>byte[24]</c> per entry (256 × per Parse call — eliminated).
    /// Use <see cref="HourSchedule24.AsReadOnlySpan"/> or direct indexing for access.
    /// <c>foreach (byte h in HourSchedule)</c> works natively (C# 12 inline-array foreach).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — hour_schedule u8×24 @ +0x04: CONFIRMED (structure and access pattern);
    ///   value variation UNOBSERVED in samples.
    /// </remarks>
    public required HourSchedule24 HourSchedule { get; init; }

    /// <summary>
    /// Blend weight or priority scalar; always 1.0f in all observed samples.
    /// Semantic unverified — not accessed in the observed runtime playback path.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — weight f32 @ +0x1C: SAMPLE-CONFIRMED as 1.0f; semantic UNVERIFIED
    /// </remarks>
    public required float Weight { get; init; }

    /// <summary>
    /// World-space X coordinate of the DirectSound3D source.
    /// Passed directly to IDirectSound3DBuffer::SetPosition as the X argument.
    /// 0.0f in all observed samples.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — pos_x f32 @ +0x20: CONFIRMED (runtime semantic); observed value 0.0f
    /// </remarks>
    public required float PosX { get; init; }

    /// <summary>
    /// Unknown field at entry offset +0x24; not accessed in the observed runtime playback path.
    /// Observed values: 0x00000000, 0x00000001, and the MSVC debug-fill pattern 0xCCCCCCCC (editor artifact).
    /// Purpose UNRESOLVED.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — unknown_36 u32 @ +0x24: UNRESOLVED
    /// </remarks>
    public required uint Unknown36 { get; init; }

    /// <summary>
    /// World-space Z coordinate of the DirectSound3D source.
    /// Passed to IDirectSound3DBuffer::SetPosition as the Z argument.
    /// The Y for SetPosition comes from the player's current world Y, not from this table.
    /// 0.0f in all observed samples.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — pos_z f32 @ +0x28: CONFIRMED (runtime semantic); observed value 0.0f
    /// </remarks>
    public required float PosZ { get; init; }

    /// <summary>
    /// Volume multiplier: scaled by 0.7 before being passed to the DirectSound volume control.
    /// 0.0f in all observed samples (consistent with unassigned/null slots).
    /// Active localized sounds would carry a positive value.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md — volume_factor f32 @ +0x2C: CONFIRMED (f32 type and ×0.7 scaling factor);
    ///   observed value 0.0f in all samples.
    /// </remarks>
    public required float VolumeFactor { get; init; }

    /// <summary>Returns true when this entry represents a real (non-null) sound assignment.</summary>
    public bool IsAssigned => SoundEntryId != 0;
}

/// <summary>
/// Decoded result of a per-map sound table file (.bgm / .bge / .eff / .wlk / .run).
/// Contains exactly 256 entries and the extension-specific metadata needed to resolve audio paths.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400), confirmed across 12 real samples"
/// spec: Docs/RE/formats/sound_tables.md §Entry count — "256 entries fixed; no count field in file"
/// spec: Docs/RE/formats/sound_tables.md §Per-entry layout — "48 bytes per entry"
/// Only the first 12288 (0x3000) bytes are consumed at runtime; the trailing 1024-byte editor
/// metadata region is preserved in <see cref="RawEditorMetadata"/> but is never required for playback.
/// </remarks>
public sealed class SoundTableData
{
    // ─── layout constants (all spec-cited) ────────────────────────────────────

    /// <summary>
    /// Fixed total file size: 13312 bytes (0x3400).
    /// Confirmed across 12 real-file samples.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §File layout — "fixed 13312 bytes (0x3400)": CONFIRMED</remarks>
    public const int FixedFileSize = 0x3400; // 13312

    /// <summary>
    /// Size of the sound-entry table region in bytes: 12288 (0x3000).
    /// This is the only region read by the runtime loader.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Overall structure — "Sound entry table … 12288 (0x3000)": CONFIRMED</remarks>
    public const int EntryTableSize = 0x3000; // 12288

    /// <summary>
    /// Size of the trailing editor metadata region in bytes: 1024 (0x400).
    /// Written by the map editor tool; ignored at runtime.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Overall structure — "Editor metadata … 1024 (0x400)": CONFIRMED</remarks>
    public const int EditorMetadataSize = 0x400; // 1024

    /// <summary>
    /// Fixed entry count: always 256. No count field exists in the file.
    /// Entry index 0 is the null sentinel.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries": CONFIRMED</remarks>
    public const int EntryCount = 256;

    /// <summary>
    /// Byte stride of one entry: 48 bytes.
    /// 256 × 48 = 12288 = <see cref="EntryTableSize"/>. Confirmed.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/sound_tables.md §Per-entry layout — "Entry stride: 48 bytes. Confirmed.": CONFIRMED</remarks>
    public const int EntryStride = 48;

    /// <summary>
    /// Number of per-hour schedule bytes per entry.
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
    /// Raw bytes of the trailing 1024-byte editor metadata region (bytes 0x3000–0x33FF).
    /// Not used at runtime; preserved for tooling / round-trip use.
    /// May be empty if the file was exactly <see cref="EntryTableSize"/> bytes (tolerated).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/sound_tables.md §Editor metadata region — "bytes 12288–13311, 1024 bytes": CONFIRMED
    /// </remarks>
    public required ReadOnlyMemory<byte> RawEditorMetadata { get; init; }

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