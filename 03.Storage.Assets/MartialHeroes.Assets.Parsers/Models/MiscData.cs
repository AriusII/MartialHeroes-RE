namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .xdb  Script Data Binary files
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>actor_size.xdb</c>. Stride: 12 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §1.2 actor_size.xdb: sample_verified true.
/// actor_class_id u32 @ +0: HIGH. scale_xz f32 @ +4: HIGH. scale_y f32 @ +8: HIGH.
/// </remarks>
public sealed class ActorSizeRecord
{
    /// <summary>spec: §1.2 — actor_class_id u32 @ +0: HIGH.</summary>
    public required uint ActorClassId { get; init; }

    /// <summary>XZ-plane scale; range 0.10–2.00 observed. spec: §1.2 — scale_xz f32 @ +4: HIGH.</summary>
    public required float ScaleXz { get; init; }

    /// <summary>Y-axis scale; range 1.00–1.50 observed. spec: §1.2 — scale_y f32 @ +8: HIGH.</summary>
    public required float ScaleY { get; init; }
}

/// <summary>
/// One record from <c>buff_icon_position.xdb</c>. Stride: 12 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §1.3 buff_icon_position.xdb: sample_verified true; CODE-CONFIRMED.
/// buff_id u32 @ +0: CODE-CONFIRMED. atlas_x i32 @ +4: CODE-CONFIRMED. atlas_y i32 @ +8: CODE-CONFIRMED.
/// SPEC CORRECTION 2026-06-13: atlas_x / atlas_y are signed i32LE (not u32); the resolver returns them as
/// a signed coordinate pair. Raw pixel values stored in the file; never inferred from a grid formula.
/// spec: Docs/RE/formats/misc_data.md §1.3 — "(corrected 2026-06-13: atlas_x / atlas_y are signed i32LE)".
/// </remarks>
public sealed class BuffIconPositionRecord
{
    /// <summary>
    /// Buff-effect identifier (the lookup key); non-sequential; range 1–1103 observed.
    /// spec: Docs/RE/formats/misc_data.md §1.3 — buff_id u32 @ +0: CODE-CONFIRMED.
    /// </summary>
    public required uint BuffId { get; init; }

    /// <summary>
    /// Pixel X of icon cell top-left corner within stateicon.dds.
    /// Signed (i32); some observed values fall off any regular 25-pixel grid.
    /// spec: Docs/RE/formats/misc_data.md §1.3 — atlas_x i32 @ +4: CODE-CONFIRMED
    ///   "(corrected 2026-06-13: i32LE, not u32LE)".
    /// </summary>
    public required int AtlasX { get; init; }

    /// <summary>
    /// Pixel Y of icon cell top-left corner within stateicon.dds. Signed (i32).
    /// spec: Docs/RE/formats/misc_data.md §1.3 — atlas_y i32 @ +8: CODE-CONFIRMED
    ///   "(corrected 2026-06-13: i32LE, not u32LE)".
    /// </summary>
    public required int AtlasY { get; init; }
}

/// <summary>
/// One record from <c>effectscale.xdb</c>. Stride: 8 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §1.4 effectscale.xdb: sample_verified true.
/// object_id u32 @ +0: HIGH. scale f32 @ +4: HIGH. Observed values: 2.0, 3.0.
/// </remarks>
public sealed class EffectScaleRecord
{
    /// <summary>spec: §1.4 — object_id u32 @ +0: HIGH.</summary>
    public required uint ObjectId { get; init; }

    /// <summary>Float scale multiplier. spec: §1.4 — scale f32 @ +4: HIGH.</summary>
    public required float Scale { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  mobinfo.mi
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>mobinfo.mi</c>. Stride: 28 bytes (7 × u32).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §2 mobinfo.mi: sample_verified (header + stride).
/// Field semantics: PARTIAL (string-table IDs; portrait resource IDs).
/// </remarks>
public sealed class MobInfoRecord
{
    /// <summary>spec: §2 — mob_class_id u32 @ +0: HIGH.</summary>
    public required uint MobClassId { get; init; }

    /// <summary>String-table reference for primary display name; 0xFFFFFFFF = none. spec: §2 — PARTIAL.</summary>
    public required uint NameStrId { get; init; }

    /// <summary>String-table reference for alternate name; 0xFFFFFFFF = none. spec: §2 — PARTIAL.</summary>
    public required uint AltNameStrId { get; init; }

    /// <summary>UI sprite index for mob icon; range 55–173 observed. spec: §2 — icon_index: HIGH.</summary>
    public required uint IconIndex { get; init; }

    /// <summary>Portrait resource 1; 0xFFFFFFFF = none. spec: §2 — PARTIAL.</summary>
    public required uint PortraitRes1 { get; init; }

    /// <summary>Portrait resource 2; 0xFFFFFFFF = none. spec: §2 — PARTIAL.</summary>
    public required uint PortraitRes2 { get; init; }

    /// <summary>Portrait resource 3; 0xFFFFFFFF = none. spec: §2 — PARTIAL.</summary>
    public required uint PortraitRes3 { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  .tol  Terrain Tile Obstacle layer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of a <c>.tol</c> terrain tile walkability bitmap.
/// Header: 16 bytes (4 × u32). Body: width_tiles × height_tiles bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §3 .tol: sample_verified (header layout and tile-grid stride).
/// world_origin semantics PARTIAL (÷256 sub-tile scale factor inferred).
/// Row-major tile ordering (y × W + x): HIGH (assumed; not loader-confirmed).
/// Tile values: 0 = walkable, 1 = blocked. Only 0 and 1 observed.
/// </remarks>
public sealed class TolMapData
{
    /// <summary>
    /// World-space X origin of the grid.
    /// Divides exactly by 256 in observed samples.
    /// spec: Docs/RE/formats/misc_data.md §3 — world_origin_x u32 @ +0: PARTIAL.
    /// </summary>
    public required uint WorldOriginX { get; init; }

    /// <summary>
    /// World-space Y origin of the grid.
    /// spec: Docs/RE/formats/misc_data.md §3 — world_origin_y u32 @ +4: PARTIAL.
    /// </summary>
    public required uint WorldOriginY { get; init; }

    /// <summary>Grid width in tiles; power of two. spec: §3 — width_tiles: HIGH.</summary>
    public required uint WidthTiles { get; init; }

    /// <summary>Grid height in tiles; power of two. spec: §3 — height_tiles: HIGH.</summary>
    public required uint HeightTiles { get; init; }

    /// <summary>
    /// Flat tile array, row-major (index = y × W + x).
    /// 0 = walkable, 1 = blocked. Length = WidthTiles × HeightTiles.
    /// spec: Docs/RE/formats/misc_data.md §3 — tile_grid: HIGH.
    /// </summary>
    public required ReadOnlyMemory<byte> TileGrid { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  discript.sc  UI Descriptor Script Table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>discript.sc</c>. Stride: 68 bytes (0x44).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §5 discript.sc: sample_verified true.
/// display_name field is CP949-encoded Korean text (30 bytes, null-padded).
/// keyboard_shortcut is ASCII, null-padded to 3 bytes.
/// reserved (27 bytes) is all zero in known sample.
/// </remarks>
public sealed class DescriptorRecord
{
    /// <summary>Unique integer identifier. spec: §5 — descriptor_id u32 @ +0: HIGH.</summary>
    public required uint DescriptorId { get; init; }

    /// <summary>Category code. See known values: 3=party, 102=hotkey, 103=currency, 105=faction. spec: §5 — category u32 @ +4: HIGH.</summary>
    public required uint Category { get; init; }

    /// <summary>
    /// Display name (CP949, null-padded to 30 bytes). Decoded to string via CP949.
    /// spec: Docs/RE/formats/misc_data.md §5 — display_name char[30] CP949 @ +8: HIGH.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Keyboard shortcut (ASCII, null-padded to 3 bytes).
    /// For category=102: "(X)" form. Others: "0" placeholder.
    /// spec: Docs/RE/formats/misc_data.md §5 — keyboard_shortcut char[3] @ +38: HIGH.
    /// </summary>
    public required string KeyboardShortcut { get; init; }

    /// <summary>
    /// Reserved 27 bytes at +41. All zero in known sample. Purpose unknown.
    /// spec: Docs/RE/formats/misc_data.md §5 — reserved u8[27] @ +41: LOW.
    /// </summary>
    public required ReadOnlyMemory<byte> Reserved { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  mapsetting.scr  Zone bounding-box and fog table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/script/mapsetting.scr</c>. Stride: 84 bytes (0x54).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §7.1 mapsetting.scr: SAMPLE-VERIFIED.
/// No header; record count = file_size / 84. Known sample: 52 records (4 368 bytes).
/// </remarks>
public sealed class MapZoneRecord
{
    /// <summary>
    /// Zone identifier (lookup key). Non-contiguous; not the array index.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — zone_id i32 @ 0x00: SAMPLE-VERIFIED.
    /// </summary>
    public required int ZoneId { get; init; }

    /// <summary>
    /// CP949-encoded zone name, NUL-terminated within the 36-byte field.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — zone_name char[36] CP949 @ 0x04: SAMPLE-VERIFIED.
    /// </summary>
    public required string ZoneName { get; init; }

    /// <summary>
    /// World-space X lower bound of the zone bounding box.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — world_min_x i32 @ 0x28: PLAUSIBLE.
    /// </summary>
    public required int WorldMinX { get; init; }

    /// <summary>
    /// World-space Z lower bound.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — world_min_z i32 @ 0x2C: PLAUSIBLE.
    /// </summary>
    public required int WorldMinZ { get; init; }

    /// <summary>
    /// World-space X upper bound.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — world_max_x i32 @ 0x30: PLAUSIBLE.
    /// </summary>
    public required int WorldMaxX { get; init; }

    /// <summary>
    /// World-space Z upper bound.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — world_max_z i32 @ 0x34: PLAUSIBLE.
    /// </summary>
    public required int WorldMaxZ { get; init; }

    /// <summary>
    /// Per-zone fog density. Observed: 1.30 (interior), 1.50 (rare), 1.70 (outdoor).
    /// spec: Docs/RE/formats/misc_data.md §7.1 — fog_density f32 @ 0x40: PLAUSIBLE.
    /// </summary>
    public required float FogDensity { get; init; }

    /// <summary>
    /// Unknown packed flags at 0x38. 0x012C0001 in 50/52 records.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — flags_a i32 @ 0x38: UNKNOWN.
    /// </summary>
    public required int FlagsA { get; init; }

    /// <summary>
    /// Unknown flags at 0x3C. Usually 0x00000001.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — flags_b i32 @ 0x3C: UNKNOWN.
    /// </summary>
    public required int FlagsB { get; init; }

    /// <summary>
    /// Unknown field at 0x44. First record = 1, all others = 0.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x44 i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x44 { get; init; }

    /// <summary>
    /// Unknown field at 0x48. Typically 0 or -1.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x48 i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x48 { get; init; }

    /// <summary>
    /// Unknown field at 0x4C. High byte 0x64 constant; low 24 bits vary.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x4C i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x4C { get; init; }

    /// <summary>
    /// Unknown field at 0x50. Always 0 in all 52 observed records.
    /// spec: Docs/RE/formats/misc_data.md §7.1 — unknown_0x50 i32: UNKNOWN.
    /// </summary>
    public required int Unknown0x50 { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  regiontableNNN.bin  Per-area sub-zone label table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/mapNNN/regiontableNNN.bin</c>. Stride: 32 bytes (0x20).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §7.2 regiontableNNN.bin: SAMPLE-VERIFIED (stride and name field).
/// No header; record count = file_size / 32. Known: 52 records per area (1 664 bytes).
/// Coordinate fields: PLAUSIBLE (may contain garbage for some sub-types — validate before use).
/// </remarks>
public sealed class RegionTableRecord
{
    /// <summary>
    /// World-space X of sub-zone label. PLAUSIBLE — validate within area bounding box before use.
    /// spec: Docs/RE/formats/misc_data.md §7.2 — center_x f32 @ 0x00: PLAUSIBLE.
    /// </summary>
    public required float CenterX { get; init; }

    /// <summary>
    /// World-space Z of sub-zone label. PLAUSIBLE.
    /// spec: Docs/RE/formats/misc_data.md §7.2 — center_z f32 @ 0x04: PLAUSIBLE.
    /// </summary>
    public required float CenterZ { get; init; }

    /// <summary>
    /// Unknown 8 bytes at 0x08. Zero in all observed records.
    /// spec: Docs/RE/formats/misc_data.md §7.2 — unknown_0x08 u8[8] @ 0x08: UNKNOWN.
    /// </summary>
    public required ReadOnlyMemory<byte> Unknown0x08 { get; init; }

    /// <summary>
    /// CP949-encoded sub-zone landmark name, NUL-terminated within the 16-byte field.
    /// spec: Docs/RE/formats/misc_data.md §7.2 — sub_zone_name char[16] CP949 @ 0x10: PLAUSIBLE.
    /// </summary>
    public required string SubZoneName { get; init; }
}