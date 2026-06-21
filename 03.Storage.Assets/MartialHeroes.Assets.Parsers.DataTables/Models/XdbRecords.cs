namespace MartialHeroes.Assets.Parsers.DataTables.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .xdb  Script Data Binary files
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>actor_size.xdb</c>. Stride: 12 bytes.
///     NEVER LOADED IN THIS BUILD — no loader function exists; the path string is present only as a data-side
///     pointer-table slot.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xdb_tables.md §1 actor_size.xdb: NEVER LOADED (no loader; path constant present in pointer
///     table only).
///     actor_kind_id u32 @ +0: CONFIRMED (on disk); table unread at runtime.
///     scale_a f32 @ +4: CONFIRMED (value); INFERRED (horizontal axis); table unread at runtime.
///     scale_b f32 @ +8: CONFIRMED (value); INFERRED (vertical axis); table unread at runtime.
/// </remarks>
public sealed class ActorSizeRecord
{
    /// <summary>
    ///     Sequential 0-based index (0..14). CONFIRMED (on disk); table never read at runtime.
    ///     spec: Docs/RE/formats/xdb_tables.md §1 — actor_kind_id u32 @ +0: CONFIRMED (on disk); table unread at runtime.
    /// </summary>
    public required uint ActorClassId { get; init; }

    /// <summary>
    ///     Horizontal/radial scale (XZ footprint). Range 0.10–2.00 observed. Axis INFERRED.
    ///     Table never read at runtime; axis question is moot for the port.
    ///     spec: Docs/RE/formats/xdb_tables.md §1 — scale_a f32 @ +4: CONFIRMED (value); INFERRED (horizontal axis); table
    ///     unread at runtime.
    /// </summary>
    public required float ScaleXz { get; init; }

    /// <summary>
    ///     Vertical/height scale. Range 0.88–1.50 observed. Axis INFERRED.
    ///     spec: Docs/RE/formats/xdb_tables.md §1 — scale_b f32 @ +8: CONFIRMED (value); INFERRED (vertical axis); table
    ///     unread at runtime.
    /// </summary>
    public required float ScaleY { get; init; }
}

/// <summary>
///     One record from <c>buff_icon_position.xdb</c>. Stride: 12 bytes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xdb_tables.md §2 buff_icon_position.xdb: sample_verified true; CODE-CONFIRMED.
///     buff_id u32 @ +0: CONFIRMED (non-contiguous). sprite_x u32 @ +4: CONFIRMED (steps 25, sample-verified). sprite_y
///     u32 @ +8: CONFIRMED-variable.
///     Origin spacing = 25 pixels (CORRECTED from 27; sample-verified). Draw-cell size (21×21) is sprite-sheet-pending.
///     The value 401 in sprite_y is a data-side blank-tile convention, not a code sentinel — treat as any other Y origin.
///     spec: Docs/RE/formats/xdb_tables.md §2 — "origin spacing 25 (CORRECTED, sample-verified; prior '27' is REFUTED)".
///     spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 blank-tile convention, not sentinel: CONFIRMED".
/// </remarks>
public sealed class BuffIconPositionRecord
{
    /// <summary>
    ///     Buff-effect identifier (the lookup key); non-sequential (sparse); range 1–1103 observed.
    ///     Always index by stored buff_id, never by row position.
    ///     spec: Docs/RE/formats/xdb_tables.md §2 — buff_id u32 @ +0: CONFIRMED (non-contiguous).
    /// </summary>
    public required uint BuffId { get; init; }

    /// <summary>
    ///     Pixel X origin of icon cell on the buff-icon sprite sheet. Advances by 25 per column, wraps after 8 per row.
    ///     Stored as u32 LE on disk; exposed as int for signed coordinates.
    ///     spec: Docs/RE/formats/xdb_tables.md §2 — sprite_x u32 @ +4: CONFIRMED (step 25, sample-verified).
    /// </summary>
    public required int AtlasX { get; init; }

    /// <summary>
    ///     Pixel Y origin of icon cell on the sprite sheet. Advances by 25 per row when sprite_x wraps.
    ///     Value 401 is a blank-tile convention (not a sentinel — do NOT add a hardcoded check for it).
    ///     spec: Docs/RE/formats/xdb_tables.md §2 — sprite_y u32 @ +8: CONFIRMED-variable.
    ///     spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 blank-tile convention, not sentinel: CONFIRMED".
    /// </summary>
    public required int AtlasY { get; init; }
}

/// <summary>
///     One record from <c>effectscale.xdb</c>. Stride: 8 bytes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xdb_tables.md §3 effectscale.xdb: sample_verified true.
///     effect_key u32 @ +0: MEDIUM (high-16=type-tag, low-16=per-effect index; only 2 records).
///     scale_factor f32 @ +4: CONFIRMED (per-effect overall size multiplier — uniform scale on spawn).
///     Observed values: record 0 = 3.0, record 1 = 2.0.
/// </remarks>
public sealed class EffectScaleRecord
{
    /// <summary>
    ///     Large u32 key; high-16 bits = shared type-tag, low-16 bits = per-effect index.
    ///     spec: Docs/RE/formats/xdb_tables.md §3 — effect_key u32 @ +0: MEDIUM (only 2 records; split NARROWED but not
    ///     CONFIRMED).
    /// </summary>
    public required uint ObjectId { get; init; }

    /// <summary>
    ///     Per-effect overall size multiplier — uniform scale applied to the entire effect on spawn.
    ///     Record 0 = 3.0, record 1 = 2.0. CONFIRMED by consumer (overwrites the .xeff base-scale at parse time).
    ///     spec: Docs/RE/formats/xdb_tables.md §3 — scale_factor f32 @ +4: CONFIRMED (per-effect overall size multiplier).
    /// </summary>
    public required float Scale { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  vehicle.xdb — Vehicle / mount catalogue (stride: 52 bytes)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>data/script/vehicle.xdb</c>. Stride: 52 bytes.
///     Mount/seat catalogue keyed by <see cref="VehicleId" /> on the mount attachment and rider Y-placement paths.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xdb_tables.md §4 — vehicle.xdb: stride 52 bytes, 58 records: CONFIRMED.
///     No file header; record count = file_size / 52 (must be exact).
///     <para>
///         Runtime use CONFIRMED (CYCLE 1, 2026-06-19): two consumers on the mount path —
///         (1) mount-attachment refresh: spawns mount visual keyed by <see cref="VehicleId" />;
///         (2) per-frame rider Y-placement: reads the per-facing seat-Y floats at float indices
///         9..12 (byte offsets +0x24/+0x28/+0x2C/+0x30 = <see cref="SeatYFacing1" />..
///         <see cref="SeatYFacing4" />) and adds them to the rider's world Y.
///         <see cref="TagA" /> and <see cref="TagB" /> are NOT read by either consumer.
///         spec: Docs/RE/formats/xdb_tables.md §4 — "runtime consumers: vehicle_id key + per-facing seat-Y floats;
///         tag_a/tag_b not read".
///     </para>
/// </remarks>
public sealed class VehicleXdbRecord
{
    /// <summary>
    ///     Vehicle id, 1-based sequential (1..58). The runtime lookup key.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0: CONFIRMED (lookup key).
    /// </summary>
    public required uint VehicleId { get; init; }

    /// <summary>
    ///     Item id in <c>items.scr</c>; early records are a consecutive block starting at 3108;
    ///     later records are non-consecutive.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — item_id u32LE @ +4: CONFIRMED.
    /// </summary>
    public required uint ItemId { get; init; }

    /// <summary>
    ///     Vehicle-family discriminator: takes 3 distinct values across 58 records.
    ///     NOT read by either mount-path consumer (CYCLE 1, static-confirmed). Parse but do NOT branch on it.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — "tag_a u32LE @ +8: not read by consumers (CYCLE 1)".
    /// </summary>
    public required uint TagA { get; init; }

    /// <summary>
    ///     Table-type stamp: CONSTANT <c>0x1575A3E4</c> in all 58 records. Carries no per-vehicle info.
    ///     NOT read by either consumer.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — "tag_b u32LE @ +12: CONSTANT 0x1575A3E4 (table_stamp)": CONFIRMED.
    /// </summary>
    public required uint TagB { get; init; }

    /// <summary>
    ///     Lateral rider mount-point X offset (most records zero). Axis INFERRED.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — param_0 f32LE @ +16: HIGH (present); INFERRED (X offset).
    /// </summary>
    public required float Param0 { get; init; }

    /// <summary>
    ///     ALWAYS 0.0 across all 58 records — a constrained/unused axis.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — param_1 f32LE @ +20: CONFIRMED (always 0); INFERRED (constrained axis).
    /// </summary>
    public required float Param1 { get; init; }

    /// <summary>
    ///     Rider mount-point Z offset (forward/back). Range ≈ -4.0..+2.5. Zero for most records. Axis INFERRED.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — param_2 f32LE @ +24: HIGH (present); INFERRED (Z offset).
    /// </summary>
    public required float Param2 { get; init; }

    /// <summary>
    ///     Range ≈ 0.0..+5.0; likely a bounding/scale parameter. Zero for most records. INFERRED.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — param_3 f32LE @ +28: INFERRED.
    /// </summary>
    public required float Param3 { get; init; }

    /// <summary>
    ///     0.0 except in the 2-record special family (large negative offset ≈ -22/-33). INFERRED.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — param_4 f32LE @ +32: INFERRED.
    /// </summary>
    public required float Param4 { get; init; }

    /// <summary>
    ///     Per-facing seat-Y offset for facing = 1 (byte offset +0x24 = float index 9 from record start).
    ///     Added to the rider's world Y by the per-frame rider Y-placement consumer.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — "per-facing seat-Y float index 9 @ +0x24 (facing=1)": CONFIRMED.
    /// </summary>
    public required float SeatYFacing1 { get; init; }

    /// <summary>
    ///     Per-facing seat-Y offset for facing = 2 (byte offset +0x28 = float index 10 from record start).
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — "per-facing seat-Y float index 10 @ +0x28 (facing=2)": CONFIRMED.
    /// </summary>
    public required float SeatYFacing2 { get; init; }

    /// <summary>
    ///     Per-facing seat-Y offset for facing = 3 (byte offset +0x2C = float index 11 from record start).
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — "per-facing seat-Y float index 11 @ +0x2C (facing=3)": CONFIRMED.
    /// </summary>
    public required float SeatYFacing3 { get; init; }

    /// <summary>
    ///     Per-facing seat-Y offset for facing = 4 (byte offset +0x30 = float index 12 from record start).
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — "per-facing seat-Y float index 12 @ +0x30 (facing=4)": CONFIRMED.
    /// </summary>
    public required float SeatYFacing4 { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  creature_item.xdb — Creature attached-item table (stride: 48 bytes)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>data/script/creature_item.xdb</c>.  Stride: 48 bytes.
///     This is a creature HELD-ITEM VISUAL attachment table — it places a visual item on the creature.
///     It is NOT a loot/drop table.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xdb_tables.md §5 — creature_item.xdb: stride 48 bytes, 921 records: CONFIRMED.
///     spec: Docs/RE/formats/xdb_tables.md §5 — CYCLE 1 RELABEL: creature held-item VISUAL attachment, NOT loot table.
///     spec: Docs/RE/formats/specs/assembly_graph.md §3 — "creature_item.xdb → held-item visual, not a loot table".
///     No file header; record count = file_size / 48 (must be exact).
///     <para>
///         Keyed by <see cref="CreatureKey" /> (record +0, from the creature actor's appearance/visual-key field).
///         At creature spawn the row's <see cref="ItemId" /> (+4) is spawned and attached as a visual, placed by
///         the three XZ offset pairs (+8..+31) rotated into world-space by the creature's facing (Y forced 0),
///         with <see cref="VisualScale" /> (+36, f32) carried into the spawned visual descriptor.
///         Per-tick: gate flags <see cref="Flag0" /> (+40) and <see cref="Flag3" /> (+43) + <see cref="Probability" />
///         (+44, millisecond tick-interval = cadence).
///         The earlier "drop / loot / 100%-probability" framing is WITHDRAWN — see spec §5 CYCLE 1.
///     </para>
/// </remarks>
public sealed class CreatureItemXdbRecord
{
    /// <summary>
    ///     Large u32 compound key, sequential-by-1.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — creature_key u32LE @ +0: CONFIRMED (pattern).
    /// </summary>
    public required uint CreatureKey { get; init; }

    /// <summary>
    ///     Attached item id (e.g. 3001).
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — item_id u32LE @ +4: CONFIRMED.
    /// </summary>
    public required uint ItemId { get; init; }

    /// <summary>
    ///     First attachment float; negative model-local offset.  Axis UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — attach_f0 f32LE @ +8: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF0 { get; init; }

    /// <summary>
    ///     Second attachment float.  Axis UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — attach_f1 f32LE @ +12: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF1 { get; init; }

    /// <summary>
    ///     Third attachment float.  Axis UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — attach_f2 f32LE @ +16: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF2 { get; init; }

    /// <summary>
    ///     Fourth attachment float.  Axis UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — attach_f3 f32LE @ +20: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF3 { get; init; }

    /// <summary>
    ///     Fifth attachment float.  Axis UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — attach_f4 f32LE @ +24: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF4 { get; init; }

    /// <summary>
    ///     Sixth attachment float.  Axis UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — attach_f5 f32LE @ +28: CONFIRMED (present); axis UNVERIFIED.
    /// </summary>
    public required float AttachF5 { get; init; }

    /// <summary>
    ///     Takes only two values across all 921 records: <c>3.0</c> or <c>8.0</c>.
    ///     Proposed name <c>collision_radius</c>: the narrow two-level spread (small=3.0, large=8.0)
    ///     points to a collision-sphere radius rather than a free-form scale. Semantic INFERRED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — "scale_or_radius f32LE @ +32: CONFIRMED (two values 3.0/8.0); INFERRED
    ///     (collision radius)".
    /// </summary>
    public required float ScaleOrRadius { get; init; }

    /// <summary>
    ///     Visual scale for the attached item (<c>f32 LE</c> at +36).
    ///     CONSUMER-CONFIRMED: the spawn-attach consumer copies this field into the spawned visual
    ///     item's descriptor. 716 of 921 records are <c>0.0</c> (use default); 205 carry clean
    ///     fractional values (0.1, 0.2, 0.3, 0.4, 0.7, 0.8, 1.0, 1.8, 2.0, 2.2).
    ///     Formerly named <c>UnknownU1</c> / tagged as <c>u32</c> — both are WITHDRAWN (CYCLE 1).
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — "visual_scale f32LE @ +36: CONFIRMED (f32; copied into spawned visual);
    ///     earlier 'unknown_u1 u32' framing WITHDRAWN".
    /// </summary>
    public required float VisualScale { get; init; }

    /// <summary>
    ///     Flag byte 0 at +40.  Binary flag: ~272 zero, ~649 one across 921 records.
    ///     CONSUMER-CONFIRMED as a per-tick gate flag for pickup/effect cadence validation.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — flag_0 u8 @ +40: CONFIRMED (independent u8 gate flag).
    /// </summary>
    public required byte Flag0 { get; init; }

    /// <summary>
    ///     Flag byte 1 at +41.  Value 1 in every head record.  Semantic UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — flag_1 u8 @ +41: UNVERIFIED.
    /// </summary>
    public required byte Flag1 { get; init; }

    /// <summary>
    ///     Flag byte 2 at +42.  Alternates 0/1 across consecutive head records
    ///     (toggle — e.g. left/right or main/off-hand slot).  Semantic UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — flag_2 u8 @ +42: UNVERIFIED.
    /// </summary>
    public required byte Flag2 { get; init; }

    /// <summary>
    ///     Flag byte 3 at +43. Mostly 0 (824 of 921); set to 1 in ~97 records.
    ///     CONSUMER-CONFIRMED as a per-tick gate flag alongside <see cref="Flag0" />.
    ///     Does NOT directly correlate with the 205 non-zero <see cref="VisualScale" /> records.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — flag_3 u8 @ +43: CONFIRMED (independent u8 gate flag).
    /// </summary>
    public required byte Flag3 { get; init; }

    /// <summary>
    ///     Millisecond tick-interval at +44. Constant <c>100</c> (<c>0x64</c>) in all 921 records.
    ///     CONSUMER-CONFIRMED as a cadence interval: the per-tick gate uses this column to rate-limit
    ///     pickup/effect re-validation (fires every 100 ms). NOT a drop/probability percentage.
    ///     The earlier "probability" framing is WITHDRAWN (CYCLE 1).
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — "tick_interval u32LE @ +44: CONFIRMED (constant 100; cadence interval);
    ///     earlier 'probability' framing WITHDRAWN".
    ///     Named <c>Probability</c> for API back-compatibility; role = tick_interval cadence.
    /// </summary>
    public required uint
        Probability
    {
        get;
        init;
    } // kept as "Probability" to avoid breaking callers; role = tick_interval cadence, NOT a drop probability
}