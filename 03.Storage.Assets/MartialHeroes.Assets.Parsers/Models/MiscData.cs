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
/// spec: Docs/RE/formats/misc_data.md §1.3 buff_icon_position.xdb: sample_verified true.
/// buff_id u32 @ +0: HIGH. atlas_x u32 @ +4: HIGH. atlas_y u32 @ +8: HIGH.
/// Icon cell size: 25 × 25 pixels. Origin convention: 1-based.
/// </remarks>
public sealed class BuffIconPositionRecord
{
    /// <summary>spec: §1.3 — buff_id u32 @ +0: HIGH.</summary>
    public required uint BuffId { get; init; }
    /// <summary>Pixel X of icon top-left corner within atlas. spec: §1.3 — atlas_x u32 @ +4: HIGH.</summary>
    public required uint AtlasX { get; init; }
    /// <summary>Pixel Y of icon top-left corner within atlas. spec: §1.3 — atlas_y u32 @ +8: HIGH.</summary>
    public required uint AtlasY { get; init; }
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
