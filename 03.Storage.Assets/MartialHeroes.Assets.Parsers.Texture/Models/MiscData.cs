namespace MartialHeroes.Assets.Parsers.Texture.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  mobinfo.mi
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>data/ui/mobinfo.mi</c>. Stride: 28 bytes (7 × u32 LE).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/mi.md §Container layout — SAMPLE-VERIFIED (count header + fixed-stride records).
///     spec: Docs/RE/formats/mi.md §Per-record layout — stride 28 bytes = 7 × u32 LE: SAMPLE-VERIFIED.
///     <para>
///         IMPORTANT: the shipping client has NO loader for this file (CONFIRMED NOT READ, build 263bd994).
///         This record is documented for archival/interoperability completeness only.
///         A faithful 1:1 port must NOT load mobinfo.mi at runtime.
///         spec: Docs/RE/formats/mi.md §Loader — "CONFIRMED NOT READ in build 263bd994".
///         spec: Docs/RE/formats/mi.md §Implications for a faithful port.
///     </para>
///     <para>
///         Field semantics are SINGLE-SAMPLE / OUT-OF-CLIENT-SCOPE: there is no client consumer
///         to confirm meanings. The field *widths* (u32 per slot, 28 bytes total) are SAMPLE-VERIFIED;
///         the roles below are provisional from a single-sample reading.
///         spec: Docs/RE/formats/mi.md §Per-record layout — "field roles SINGLE-SAMPLE / OUT-OF-CLIENT-SCOPE".
///     </para>
///     <para>
///         NOTE: the earlier "PortraitRes3" label (field +0x18 / +24) is WITHDRAWN per CYCLE 7.
///         See spec: Docs/RE/formats/mi.md §Per-record layout — "any PortraitRes3 labeling WITHDRAWN".
///     </para>
/// </remarks>
public sealed class MobInfoRecord
{
    /// <summary>
    ///     Dense sequential entry id — monotonic 101, 102, …, 121 across the 21 records (the row key).
    ///     spec: Docs/RE/formats/mi.md §Per-record layout — "+0: entry_id — dense sequential: SAMPLE-VERIFIED".
    /// </summary>
    public required uint EntryId { get; init; }

    /// <summary>
    ///     Caption message-catalogue id (~20000 band) — primary caption/name string.
    ///     spec: Docs/RE/formats/mi.md §Per-record layout — "+4: caption_msg_id — SINGLE-SAMPLE".
    /// </summary>
    public required uint CaptionMsgId { get; init; }

    /// <summary>
    ///     Description message-catalogue id (~20000 band); 0xFFFFFFFF = absent (null sentinel).
    ///     spec: Docs/RE/formats/mi.md §Per-record layout — "+8: description_msg_id — SINGLE-SAMPLE".
    /// </summary>
    public required uint DescriptionMsgId { get; init; }

    /// <summary>
    ///     Small sub-id / category code (observed values: low tens to low hundreds).
    ///     spec: Docs/RE/formats/mi.md §Per-record layout — "+12: small_param — SINGLE-SAMPLE".
    /// </summary>
    public required uint SmallParam { get; init; }

    /// <summary>
    ///     Decimal-packed code A of the form <c>group × 10000 + index</c>. NOT a code pointer.
    ///     spec: Docs/RE/formats/mi.md §Per-record layout — "+16: packed_code_a — SINGLE-SAMPLE".
    ///     spec: Docs/RE/formats/mi.md — "coincidence trap: +16/+20 are NOT .text code pointers".
    /// </summary>
    public required uint PackedCodeA { get; init; }

    /// <summary>
    ///     Decimal-packed code B — related pair to <see cref="PackedCodeA" />; delta NOT universally 1.
    ///     Final record carries 0xFFFFFFFF here.
    ///     spec: Docs/RE/formats/mi.md §Per-record layout — "+20: packed_code_b — SINGLE-SAMPLE".
    /// </summary>
    public required uint PackedCodeB { get; init; }

    /// <summary>
    ///     Auxiliary optional small id/index; 0xFFFFFFFF = none. Role is MOOT (no consumer read-site).
    ///     spec: Docs/RE/formats/mi.md §Per-record layout — "+24: aux_field — SINGLE-SAMPLE / HYPOTHESIS".
    ///     spec: Docs/RE/formats/mi.md — "field6 role MOOT; any portrait_res_3 label WITHDRAWN (CYCLE 7)".
    /// </summary>
    public required uint AuxField { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  .tol  Terrain Tile Obstacle layer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     Decoded result of a <c>.tol</c> terrain tile walkability bitmap.
///     Header: 16 bytes (4 × u32). Body: width_tiles × height_tiles bytes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §3 .tol: sample_verified (header layout and tile-grid stride).
///     world_origin semantics PARTIAL (÷256 sub-tile scale factor inferred).
///     Row-major tile ordering (y × W + x): HIGH (assumed; not loader-confirmed).
///     Tile values: 0 = walkable, 1 = blocked. Only 0 and 1 observed.
/// </remarks>
public sealed class TolMapData
{
    /// <summary>
    ///     World-space X origin of the grid.
    ///     Divides exactly by 256 in observed samples.
    ///     spec: Docs/RE/formats/misc_data.md §3 — world_origin_x u32 @ +0: PARTIAL.
    /// </summary>
    public required uint WorldOriginX { get; init; }

    /// <summary>
    ///     World-space Y origin of the grid.
    ///     spec: Docs/RE/formats/misc_data.md §3 — world_origin_y u32 @ +4: PARTIAL.
    /// </summary>
    public required uint WorldOriginY { get; init; }

    /// <summary>Grid width in tiles; power of two. spec: §3 — width_tiles: HIGH.</summary>
    public required uint WidthTiles { get; init; }

    /// <summary>Grid height in tiles; power of two. spec: §3 — height_tiles: HIGH.</summary>
    public required uint HeightTiles { get; init; }

    /// <summary>
    ///     Flat tile array, row-major (index = y × W + x).
    ///     0 = walkable, 1 = blocked. Length = WidthTiles × HeightTiles.
    ///     spec: Docs/RE/formats/misc_data.md §3 — tile_grid: HIGH.
    /// </summary>
    public required ReadOnlyMemory<byte> TileGrid { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  discript.sc  UI Descriptor Script Table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>discript.sc</c>. Stride: 68 bytes (0x44).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §5 discript.sc: sample_verified true.
///     display_name field is CP949-encoded Korean text (30 bytes, null-padded).
///     keyboard_shortcut is ASCII, null-padded to 3 bytes.
///     reserved (27 bytes) is all zero in known sample.
/// </remarks>
public sealed class DescriptorRecord
{
    /// <summary>Unique integer identifier. spec: §5 — descriptor_id u32 @ +0: HIGH.</summary>
    public required uint DescriptorId { get; init; }

    /// <summary>
    ///     Category code. See known values: 3=party, 102=hotkey, 103=currency, 105=faction. spec: §5 — category u32 @ +4:
    ///     HIGH.
    /// </summary>
    public required uint Category { get; init; }

    /// <summary>
    ///     Display name (CP949, null-padded to 30 bytes). Decoded to string via CP949.
    ///     spec: Docs/RE/formats/misc_data.md §5 — display_name char[30] CP949 @ +8: HIGH.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    ///     Keyboard shortcut (ASCII, null-padded to 3 bytes).
    ///     For category=102: "(X)" form. Others: "0" placeholder.
    ///     spec: Docs/RE/formats/misc_data.md §5 — keyboard_shortcut char[3] @ +38: HIGH.
    /// </summary>
    public required string KeyboardShortcut { get; init; }

    /// <summary>
    ///     Reserved 27 bytes at +41. All zero in known sample. Purpose unknown.
    ///     spec: Docs/RE/formats/misc_data.md §5 — reserved u8[27] @ +41: LOW.
    /// </summary>
    public required ReadOnlyMemory<byte> Reserved { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  mapsetting.scr  Zone bounding-box and fog table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>data/script/mapsetting.scr</c>. Stride: 84 bytes (0x54).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §7.1 mapsetting.scr: SAMPLE-VERIFIED.
///     No header; record count = file_size / 84. Known sample: 52 records (4 368 bytes).
/// </remarks>
public sealed class MapZoneRecord
{
    /// <summary>
    ///     Zone identifier (lookup key). Non-contiguous; not the array index.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — zone_id i32 @ 0x00: SAMPLE-VERIFIED.
    /// </summary>
    public required int ZoneId { get; init; }

    /// <summary>
    ///     CP949-encoded zone name, NUL-terminated within the 36-byte field.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — zone_name char[36] CP949 @ 0x04: SAMPLE-VERIFIED.
    /// </summary>
    public required string ZoneName { get; init; }

    /// <summary>
    ///     World-space X lower bound of the zone bounding box.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — world_min_x i32 @ 0x28: PLAUSIBLE.
    /// </summary>
    public required int WorldMinX { get; init; }

    /// <summary>
    ///     World-space Z lower bound.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — world_min_z i32 @ 0x2C: PLAUSIBLE.
    /// </summary>
    public required int WorldMinZ { get; init; }

    /// <summary>
    ///     World-space X upper bound.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — world_max_x i32 @ 0x30: PLAUSIBLE.
    /// </summary>
    public required int WorldMaxX { get; init; }

    /// <summary>
    ///     World-space Z upper bound.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — world_max_z i32 @ 0x34: PLAUSIBLE.
    /// </summary>
    public required int WorldMaxZ { get; init; }

    /// <summary>
    ///     Per-zone fog density. Observed: 1.30 (interior), 1.50 (rare), 1.70 (outdoor).
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — fog_density f32 @ 0x40: PLAUSIBLE.
    /// </summary>
    public required float FogDensity { get; init; }

    /// <summary>
    ///     Unknown packed flags at 0x38. 0x012C0001 in 50/52 records.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — flags_a i32 @ 0x38: UNKNOWN.
    /// </summary>
    public required int FlagsA { get; init; }

    /// <summary>
    ///     Unknown flags at 0x3C. Usually 0x00000001.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — flags_b i32 @ 0x3C: UNKNOWN.
    /// </summary>
    public required int FlagsB { get; init; }

    /// <summary>
    ///     Unknown field at 0x44. First record = 1, all others = 0.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x44 i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x44 { get; init; }

    /// <summary>
    ///     Unknown field at 0x48. Typically 0 or -1.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x48 i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x48 { get; init; }

    /// <summary>
    ///     Unknown field at 0x4C. High byte 0x64 constant; low 24 bits vary.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x4C i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x4C { get; init; }

    /// <summary>
    ///     Unknown field at 0x50. Always 0 in all 52 observed records.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x50 i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x50 { get; init; }
}