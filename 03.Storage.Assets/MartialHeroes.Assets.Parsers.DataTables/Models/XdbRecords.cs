namespace MartialHeroes.Assets.Parsers.DataTables.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .xdb  Script Data Binary files
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
///     One record from <c>actor_size.xdb</c>. Stride: 12 bytes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §1.2 actor_size.xdb: sample_verified true.
///     actor_class_id u32 @ +0: HIGH. scale_xz f32 @ +4: HIGH. scale_y f32 @ +8: HIGH.
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
///     One record from <c>buff_icon_position.xdb</c>. Stride: 12 bytes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §1.3 buff_icon_position.xdb: sample_verified true; CODE-CONFIRMED.
///     buff_id u32 @ +0: CODE-CONFIRMED. atlas_x i32 @ +4: CODE-CONFIRMED. atlas_y i32 @ +8: CODE-CONFIRMED.
///     SPEC CORRECTION 2026-06-13: atlas_x / atlas_y are signed i32LE (not u32); the resolver returns them as
///     a signed coordinate pair. Raw pixel values stored in the file; never inferred from a grid formula.
///     spec: Docs/RE/formats/misc_data.md §1.3 — "(corrected 2026-06-13: atlas_x / atlas_y are signed i32LE)".
/// </remarks>
public sealed class BuffIconPositionRecord
{
    /// <summary>
    ///     Buff-effect identifier (the lookup key); non-sequential; range 1–1103 observed.
    ///     spec: Docs/RE/formats/misc_data.md §1.3 — buff_id u32 @ +0: CODE-CONFIRMED.
    /// </summary>
    public required uint BuffId { get; init; }

    /// <summary>
    ///     Pixel X of icon cell top-left corner within stateicon.dds.
    ///     Signed (i32); some observed values fall off any regular 25-pixel grid.
    ///     spec: Docs/RE/formats/misc_data.md §1.3 — atlas_x i32 @ +4: CODE-CONFIRMED
    ///     "(corrected 2026-06-13: i32LE, not u32LE)".
    /// </summary>
    public required int AtlasX { get; init; }

    /// <summary>
    ///     Pixel Y of icon cell top-left corner within stateicon.dds. Signed (i32).
    ///     spec: Docs/RE/formats/misc_data.md §1.3 — atlas_y i32 @ +8: CODE-CONFIRMED
    ///     "(corrected 2026-06-13: i32LE, not u32LE)".
    /// </summary>
    public required int AtlasY { get; init; }
}

/// <summary>
///     One record from <c>effectscale.xdb</c>. Stride: 8 bytes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/misc_data.md §1.4 effectscale.xdb: sample_verified true.
///     object_id u32 @ +0: HIGH. scale f32 @ +4: HIGH. Observed values: 2.0, 3.0.
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
///     One record from <c>data/script/vehicle.xdb</c>.  Stride: 52 bytes.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xdb_tables.md §4 — vehicle.xdb: stride 52 bytes, 58 records: CONFIRMED.
///     No file header; record count = file_size / 52 (must be exact).
/// </remarks>
public sealed class VehicleXdbRecord
{
    /// <summary>
    ///     Vehicle id, 1-based sequential (1..58).
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0: CONFIRMED.
    /// </summary>
    public required uint VehicleId { get; init; }

    /// <summary>
    ///     Item id in <c>items.scr</c>; consecutive block starting at 3108.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — item_id u32LE @ +4: CONFIRMED.
    /// </summary>
    public required uint ItemId { get; init; }

    /// <summary>
    ///     Unknown 8-byte run at record +8.  Identical across the head records.
    ///     UNVERIFIED — could be two u32 sub-fields with fixed values, a serialised handle,
    ///     or a debug artefact.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — unknown_8b u8[8] @ +8: UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> Unknown8b { get; init; }

    /// <summary>
    ///     36-byte region at record +16.  All-zero in the head records; likely multiple typed
    ///     fields that are zero for the base vehicle entries.
    ///     spec: Docs/RE/formats/xdb_tables.md §4 — zero_region u8[36] @ +16:
    ///     CONFIRMED (value=0 in head); layout UNVERIFIED.
    /// </summary>
    public required ReadOnlyMemory<byte> ZeroRegion { get; init; }
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
///         with <see cref="UnknownU1" /> (+36, <c>visual_scale</c>) carried into the spawned descriptor.
///         Per-tick: gate flags (+40, +43) + <see cref="Probability" /> (+44, millisecond tick-interval = cadence).
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
    ///     Notably larger than the six attachment floats (head value 8.0); may be a scale or
    ///     collision radius.  Semantic UNVERIFIED; carried through as raw value.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — scale_or_radius f32LE @ +32:
    ///     CONFIRMED (present); semantic UNVERIFIED.
    /// </summary>
    public required float ScaleOrRadius { get; init; }

    /// <summary>
    ///     Zero in the head records.  Semantic UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — unknown_u1 u32LE @ +36:
    ///     CONFIRMED (value=0 in head); UNVERIFIED.
    /// </summary>
    public required uint UnknownU1 { get; init; }

    /// <summary>
    ///     Flag byte 0 at +40.  Zero in the head records.  Semantic UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — flag_0 u8 @ +40: UNVERIFIED.
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
    ///     Flag byte 3 at +43.  Zero in the head records.  Semantic UNVERIFIED.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — flag_3 u8 @ +43: UNVERIFIED.
    /// </summary>
    public required byte Flag3 { get; init; }

    /// <summary>
    ///     Millisecond tick-interval at +44. Constant 100 (0x64) in all 921 records.
    ///     Consumer-confirmed as the cadence interval the per-tick gate uses to re-validate
    ///     the attached creature-item's pickup/effect — NOT a drop/probability percentage.
    ///     The earlier "probability" framing is WITHDRAWN.
    ///     spec: Docs/RE/formats/xdb_tables.md §5 — tick_interval u32LE @ +44:
    ///     CONFIRMED (constant 100; cadence interval). Formerly named Probability — framing withdrawn.
    /// </summary>
    public required uint
        Probability
    {
        get;
        init;
    } // kept as "Probability" to avoid breaking callers; role = tick_interval cadence, NOT a drop probability
}