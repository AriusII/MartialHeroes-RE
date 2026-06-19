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
//  vehicle.xdb — Vehicle / mount catalogue (stride: 52 bytes)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/script/vehicle.xdb</c>.  Stride: 52 bytes.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/xdb_tables.md §4 — vehicle.xdb: stride 52 bytes, 58 records: CONFIRMED.
/// No file header; record count = file_size / 52 (must be exact).
/// </remarks>
public sealed class VehicleXdbRecord
{
    /// <summary>
    /// Vehicle id, 1-based sequential (1..58).
    /// spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0: CONFIRMED.
    /// </summary>
    public required uint VehicleId { get; init; }

    /// <summary>
    /// Item id in <c>items.scr</c>; consecutive block starting at 3108.
    /// spec: Docs/RE/formats/xdb_tables.md §4 — item_id u32LE @ +4: CONFIRMED.
    /// </summary>
    public required uint ItemId { get; init; }

    /// <summary>
    /// Unknown 8-byte run at record +8.  Identical across the head records.
    /// UNVERIFIED — could be two u32 sub-fields with fixed values, a serialised handle,
    /// or a debug artefact.
    /// spec: Docs/RE/formats/xdb_tables.md §4 — unknown_8b u8[8] @ +8: UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> Unknown8b { get; init; }

    /// <summary>
    /// 36-byte region at record +16.  All-zero in the head records; likely multiple typed
    /// fields that are zero for the base vehicle entries.
    /// spec: Docs/RE/formats/xdb_tables.md §4 — zero_region u8[36] @ +16:
    ///   CONFIRMED (value=0 in head); layout UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> ZeroRegion { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  creature_item.xdb — Creature attached-item table (stride: 48 bytes)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One record from <c>data/script/creature_item.xdb</c>.  Stride: 48 bytes.
/// This is a creature HELD-ITEM VISUAL attachment table — it places a visual item on the creature.
/// It is NOT a loot/drop table.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/xdb_tables.md §5 — creature_item.xdb: stride 48 bytes, 921 records: CONFIRMED.
/// spec: Docs/RE/formats/xdb_tables.md §5 — CYCLE 1 RELABEL: creature held-item VISUAL attachment, NOT loot table.
/// spec: Docs/RE/specs/assembly_graph.md §3 — "creature_item.xdb → held-item visual, not a loot table".
/// No file header; record count = file_size / 48 (must be exact).
/// <para>
/// Keyed by <see cref="CreatureKey"/> (record +0, from the creature actor's appearance/visual-key field).
/// At creature spawn the row's <see cref="ItemId"/> (+4) is spawned and attached as a visual, placed by
/// the three XZ offset pairs (+8..+31) rotated into world-space by the creature's facing (Y forced 0),
/// with <see cref="UnknownU1"/> (+36, <c>visual_scale</c>) carried into the spawned descriptor.
/// Per-tick: gate flags (+40, +43) + <see cref="Probability"/> (+44, millisecond tick-interval = cadence).
/// The earlier "drop / loot / 100%-probability" framing is WITHDRAWN — see spec §5 CYCLE 1.
/// </para>
/// </remarks>
public sealed class CreatureItemXdbRecord
{
    /// <summary>
    /// Large u32 compound key, sequential-by-1.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — creature_key u32LE @ +0: CONFIRMED (pattern).
    /// </summary>
    public required uint CreatureKey { get; init; }

    /// <summary>
    /// Attached item id (e.g. 3001).
    /// spec: Docs/RE/formats/xdb_tables.md §5 — item_id u32LE @ +4: CONFIRMED.
    /// </summary>
    public required uint ItemId { get; init; }

    /// <summary>
    /// First attachment float; negative model-local offset.  Axis UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — attach_f0 f32LE @ +8: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF0 { get; init; }

    /// <summary>
    /// Second attachment float.  Axis UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — attach_f1 f32LE @ +12: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF1 { get; init; }

    /// <summary>
    /// Third attachment float.  Axis UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — attach_f2 f32LE @ +16: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF2 { get; init; }

    /// <summary>
    /// Fourth attachment float.  Axis UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — attach_f3 f32LE @ +20: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF3 { get; init; }

    /// <summary>
    /// Fifth attachment float.  Axis UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — attach_f4 f32LE @ +24: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF4 { get; init; }

    /// <summary>
    /// Sixth attachment float.  Axis UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — attach_f5 f32LE @ +28: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF5 { get; init; }

    /// <summary>
    /// Notably larger than the six attachment floats (head value 8.0); may be a scale or
    /// collision radius.  Semantic UNVERIFIED; carried through as raw value.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — scale_or_radius f32LE @ +32:
    ///   CONFIRMED (present); semantic UNVERIFIED.
    /// </summary>
    public required float ScaleOrRadius { get; init; }

    /// <summary>
    /// Zero in the head records.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — unknown_u1 u32LE @ +36:
    ///   CONFIRMED (value=0 in head); UNVERIFIED.
    /// </summary>
    public required uint UnknownU1 { get; init; }

    /// <summary>
    /// Flag byte 0 at +40.  Zero in the head records.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — flag_0 u8 @ +40: UNVERIFIED.
    /// </summary>
    public required byte Flag0 { get; init; }

    /// <summary>
    /// Flag byte 1 at +41.  Value 1 in every head record.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — flag_1 u8 @ +41: UNVERIFIED.
    /// </summary>
    public required byte Flag1 { get; init; }

    /// <summary>
    /// Flag byte 2 at +42.  Alternates 0/1 across consecutive head records
    /// (toggle — e.g. left/right or main/off-hand slot).  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — flag_2 u8 @ +42: UNVERIFIED.
    /// </summary>
    public required byte Flag2 { get; init; }

    /// <summary>
    /// Flag byte 3 at +43.  Zero in the head records.  Semantic UNVERIFIED.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — flag_3 u8 @ +43: UNVERIFIED.
    /// </summary>
    public required byte Flag3 { get; init; }

    /// <summary>
    /// Millisecond tick-interval at +44. Constant 100 (0x64) in all 921 records.
    /// Consumer-confirmed as the cadence interval the per-tick gate uses to re-validate
    /// the attached creature-item's pickup/effect — NOT a drop/probability percentage.
    /// The earlier "probability" framing is WITHDRAWN.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — tick_interval u32LE @ +44:
    ///   CONFIRMED (constant 100; cadence interval). Formerly named Probability — framing withdrawn.
    /// </summary>
    public required uint
        Probability
    {
        get;
        init;
    } // kept as "Probability" to avoid breaking callers; role = tick_interval cadence, NOT a drop probability
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
/// One record from <c>data/mapNNN/regiontableNNN.bin</c>.
/// Stride: <b>48 bytes</b> (0x30). Fixed 32 records = 1,536 bytes total.
/// </summary>
/// <remarks>
/// <para>
/// Record layout (48 bytes):
/// <code>
///   +0x00  char[40]  zoneName   NUL-terminated CP949 zone display-name (minimap sub-zone caption)
///   +0x28  u32 LE    zoneType   Zone-type enum {0=Safe, 1=OpenPvP, 2=Closed}
///   +0x2C  u32 LE    _tail      No reader found; UNVERIFIED meaning
/// </code>
/// </para>
/// <para>
/// <b>Stride correction:</b> an earlier revision of this record used a 32-byte stride. That
/// figure is REFUTED. The confirmed stride is <b>48 bytes</b>, which is the only value that
/// reconciles 32 records to the 1,536-byte table block.
/// spec: Docs/RE/formats/region_grid.md §regiontable — "stride 48 bytes — CONFIRMED (RE-AFFIRMED)".
/// spec: Docs/RE/formats/region_grid.md §regiontable — "Stride is 48 not 32 (conflation note)".
/// </para>
/// </remarks>
public sealed class RegionTableRecord
{
    /// <summary>
    /// Index of this record in the table (0..31), equal to the region id.
    /// spec: Docs/RE/formats/region_grid.md §regiontable — "indexed directly by region id (0..31)".
    /// </summary>
    public required int RegionId { get; init; }

    /// <summary>
    /// NUL-terminated CP949 zone display-name string (minimap sub-zone caption).
    /// Read from the 40-byte field starting at record offset +0x00.
    /// spec: Docs/RE/formats/region_grid.md §regiontable — "zoneName char[40] @ +0x00": HIGH.
    /// </summary>
    public required string ZoneName { get; init; }

    /// <summary>
    /// Zone-type enum value: 0 = Safe, 1 = OpenPvP, 2 = Closed.
    /// Read from record offset +0x28 (= +40).
    /// The only numeric field any region-gating path reads.
    /// spec: Docs/RE/formats/region_grid.md §regiontable zoneType enum — CONFIRMED.
    /// </summary>
    public required uint ZoneType { get; init; }

    /// <summary>
    /// Opaque trailing dword at record offset +0x2C (= +44). No reader found.
    /// spec: Docs/RE/formats/region_grid.md §regiontable — "_tail u32 @ +0x2C: UNVERIFIED".
    /// </summary>
    public required uint TailOpaque { get; init; }
}