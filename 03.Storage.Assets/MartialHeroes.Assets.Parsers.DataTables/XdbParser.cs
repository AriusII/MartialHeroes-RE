using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

/// <summary>
///     Parsers for <c>.xdb</c> script data binary files:
///     <c>actor_size.xdb</c> (12 B — DEAD, no runtime consumer), <c>buff_icon_position.xdb</c> (12 B),
///     <c>effectscale.xdb</c> (8 B), <c>vehicle.xdb</c> (52 B), <c>creature_item.xdb</c> (48 B).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/xdb_tables.md
///     Common structure: no header; record count = file_size / stride; stride must divide evenly.
///     ZERO rendering/engine dependencies.
/// </remarks>
public static class XdbParser
{
    // =========================================================================
    // actor_size.xdb — Per-actor-class scale override (stride: 12 bytes)
    // =========================================================================

    // Stride: 12 bytes. CONFIRMED (180 bytes = 15 records).
    // spec: Docs/RE/formats/xdb_tables.md §1 — "stride 12 bytes, 15 records": CONFIRMED.
    //
    // *** DEAD IN THIS BUILD — DO NOT LOAD ***
    // The shipped client has ZERO runtime consumers for actor_size.xdb.
    // A faithful 1:1 port must NOT load this table — doing so would add behaviour the
    // original never had. This parser is kept for archival completeness ONLY.
    // spec: Docs/RE/formats/xdb_tables.md §1 — "DEAD IN THIS BUILD — DO NOT LOAD (loader-resolved)".
    private const int ActorSizeStride = 12;

    // =========================================================================
    // buff_icon_position.xdb — Buff-effect icon sprite-sheet positions (stride: 12 bytes)
    // =========================================================================

    // Stride: 12 bytes. CONFIRMED (1,608 bytes = 134 records, 1608/12=134 exact).
    // spec: Docs/RE/formats/xdb_tables.md §2 — "stride 12 bytes, 134 records": CONFIRMED.
    private const int BuffIconStride = 12;

    // =========================================================================
    // effectscale.xdb — Per-effect overall size multiplier (stride: 8 bytes)
    // =========================================================================

    // Stride: 8 bytes. CONFIRMED (file_size exact multiple of 8 enforced).
    // spec: Docs/RE/formats/xdb_tables.md §3 — "stride 8 bytes": CONFIRMED.
    private const int EffectScaleStride = 8;

    // =========================================================================
    // vehicle.xdb — Vehicle / mount catalogue (stride: 52 bytes)
    // =========================================================================

    // Stride: 52 bytes. CONFIRMED (3,016 bytes = 58 records, 3016 / 52 = 58, exact).
    // spec: Docs/RE/formats/xdb_tables.md §4 — "stride 52 bytes, 58 records": CONFIRMED.
    private const int VehicleStride = 52;

    // vehicle.xdb named field offsets — CONFIRMED (CYCLE 1, 2026-06-21 consumer re-read).
    // spec: Docs/RE/formats/xdb_tables.md §4 — full field table.
    // tag_a u32LE @ +8: takes 3 distinct values across 58 records (family discriminator metadata).
    //   NOT READ by either mount-path consumer (CYCLE 1, static-confirmed). Parse but do NOT branch on it.
    // spec: Docs/RE/formats/xdb_tables.md §4 — "tag_a u32LE @ +8: not read by mount-path consumers (CYCLE 1)".
    private const int VehicleTagAOffset = 8;

    // tag_b u32LE @ +12: CONSTANT 0x1575A3E4 in all 58 records. A table-type stamp; no per-vehicle info.
    // NOT READ by either consumer.
    // spec: Docs/RE/formats/xdb_tables.md §4 — "tag_b u32LE @ +12: CONSTANT 0x1575A3E4 (table_stamp)": CONFIRMED.
    private const int VehicleTagBOffset = 12;
    private const uint VehicleTagBExpected = 0x1575A3E4u; // table-type stamp; CONSTANT in all 58 records

    // float params start at +16. param_0..param_8 = nine f32 LE = 36 bytes total (+16..+51=stride end).
    // Per-facing seat-Y floats at byte offsets +0x24/+0x28/+0x2C/+0x30 = param_2/3/4 and first of param_5..8.
    // Consumer reads float-array-as-a-whole at index (facing+8) for facing 1..4 → indices 9,10,11,12
    //   = byte offsets +0x24, +0x28, +0x2C, +0x30.
    // spec: Docs/RE/formats/xdb_tables.md §4 — "per-facing seat-Y floats: float indices 9..12 = byte offsets +0x24/+0x28/+0x2C/+0x30 (facing 1..4)".
    private const int VehicleParam0Offset = 16; // f32 param_0: lateral rider mount-point X. INFERRED.
    private const int VehicleParam1Offset = 20; // f32 param_1: ALWAYS 0.0 across all 58 records.

    private const int
        VehicleParam2Offset = 24; // f32 param_2: facing-1 seat-Y / rider Z offset. CONFIRMED seat-Y (facing=1).

    private const int VehicleParam3Offset = 28; // f32 param_3: facing-2 seat-Y. CONFIRMED seat-Y (facing=2).

    private const int VehicleParam4Offset = 32; // f32 param_4: facing-3 seat-Y. CONFIRMED seat-Y (facing=3).

    // param_5..8 = f32[4] @ +36..+51: last 4 float slots; facing-4 seat-Y is at +36 (param_5).
    // spec: Docs/RE/formats/xdb_tables.md §4 — param_5..8 f32[4] @ +36: float indices 9(+0x24)..12(+0x30).
    private const int VehicleParam5to8Offset = 36; // f32[4] = param_5, param_6, param_7, param_8.

    // =========================================================================
    // creature_item.xdb — Creature attached-item table (stride: 48 bytes)
    // =========================================================================

    // Stride: 48 bytes. CONFIRMED (44,208 bytes = 921 records, 44208 / 48 = 921, exact).
    // spec: Docs/RE/formats/xdb_tables.md §5 — "stride 48 bytes, 921 records": CONFIRMED.
    private const int CreatureItemStride = 48;

    /// <summary>
    ///     Parses <c>data/script/actor_size.xdb</c>.
    ///     Record count = file_size / 12 (must be exact multiple).
    /// </summary>
    /// <remarks>
    ///     <b>WARNING — DEAD IN THIS BUILD.</b> The shipped client has zero runtime consumers for
    ///     this file. This parser exists for archival completeness only; a faithful port must NOT
    ///     call this at runtime.
    ///     spec: Docs/RE/formats/xdb_tables.md §1 — "DEAD IN THIS BUILD — DO NOT LOAD (loader-resolved)".
    /// </remarks>
    public static ActorSizeRecord[] ParseActorSizeXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, ActorSizeStride, "actor_size.xdb", "Docs/RE/formats/xdb_tables.md §1");
        var count = span.Length / ActorSizeStride;
        var results = new ActorSizeRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * ActorSizeStride;
            var rec = span.Slice(offset, ActorSizeStride);

            // actor_kind_id u32le @ +0. Sequential 0-based (0..14). CONFIRMED (on disk); table unread at runtime.
            // spec: Docs/RE/formats/xdb_tables.md §1 — "actor_kind_id u32 @ +0: CONFIRMED (on disk); table unread at runtime".
            var actorClassId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // scale_a f32le @ +4. Proposed horizontal/radial scale. CONFIRMED (value); INFERRED (axis); table unread at runtime.
            // spec: Docs/RE/formats/xdb_tables.md §1 — "scale_a f32 @ +4: CONFIRMED value; INFERRED axis; table unread at runtime".
            var scaleXz = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // scale_b f32le @ +8. Proposed vertical/height scale. CONFIRMED (value); INFERRED (axis); table unread at runtime.
            // spec: Docs/RE/formats/xdb_tables.md §1 — "scale_b f32 @ +8: CONFIRMED value; INFERRED axis; table unread at runtime".
            var scaleY = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            results[i] = new ActorSizeRecord
            {
                ActorClassId = actorClassId,
                ScaleXz = scaleXz,
                ScaleY = scaleY
            };
        }

        return results;
    }

    // Render cell geometry (SAMPLE-VERIFIED):
    //   OriginSpacing = 25 pixels (stride between successive sprite_x/sprite_y origins — CORRECTED 27→25, sample-verified)
    //   DrawCellSize  = 21×21 pixels (the blitted icon footprint — sprite-sheet-pending; only OriginSpacing=25 is CONFIRMED)
    // spec: Docs/RE/formats/xdb_tables.md §2 — "origin spacing 25 (CORRECTED, sample-verified; prior '27' is REFUTED)".
    // spec: Docs/RE/formats/xdb_tables.md §2 — "draw-cell size (21×21) needs the sprite sheet to confirm; only the 25-unit origin step is CONFIRMED".
    // NOTE: sprite_y = 401 is a DATA-SIDE BLANK-TILE CONVENTION (not a code sentinel).
    //   No loader branch tests for 401; it is an authored Y-origin pointing at a deliberately empty
    //   tile on the sprite sheet. Treat it as any other sprite_y — do NOT add a hardcoded sentinel check.
    // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 data-side blank-tile convention, not a code sentinel": CONFIRMED.
    // NOTE: buff_id is NON-CONTIGUOUS (head happens to begin 1,2,3,… but spans 1..1103 with only
    //   134 entries populated). Index by stored buff_id, never by row position.
    // spec: Docs/RE/formats/xdb_tables.md §2 — "buff_id non-contiguous: CONFIRMED".

    /// <summary>
    ///     Parses <c>data/script/buff_icon_position.xdb</c>.
    ///     Record count = file_size / 12 (must be exact multiple).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/xdb_tables.md §2: sample_verified true.
    ///     <para>
    ///         <c>sprite_y = 401</c> is a data-side blank-tile convention — treat it as any other pixel-Y
    ///         origin. Do NOT add a hardcoded sentinel check.
    ///         Origin spacing between cells is <b>25</b> pixels (CORRECTED from 27; sample-verified).
    ///         Draw-cell size (21×21) needs the sprite sheet to confirm.
    ///         spec: Docs/RE/formats/xdb_tables.md §2 — "origin spacing 25 CONFIRMED; prior '27' REFUTED".
    ///     </para>
    /// </remarks>
    public static BuffIconPositionRecord[] ParseBuffIconPositionXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, BuffIconStride, "buff_icon_position.xdb", "Docs/RE/formats/xdb_tables.md §2");
        var count = span.Length / BuffIconStride;
        var results = new BuffIconPositionRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * BuffIconStride;
            var rec = span.Slice(offset, BuffIconStride);

            // buff_id u32le @ +0. Sparse non-contiguous range 1..1103 (only 134 slots populated).
            // Index the table by stored buff_id, never by row position.
            // spec: Docs/RE/formats/xdb_tables.md §2 — "buff_id u32 @ +0: CONFIRMED (non-contiguous)".
            var buffId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // sprite_x u32le @ +4. Pixel X origin on the buff-icon sprite sheet.
            // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_x u32 @ +4: CONFIRMED".
            var spriteX = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // sprite_y u32le @ +8. Pixel Y origin on the sprite sheet.
            // Value 401 is a DATA-SIDE BLANK-TILE CONVENTION (not a code sentinel) — treat as
            // any other Y origin; no hardcoded branch on 401.
            // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y u32 @ +8: CONFIRMED-variable".
            // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 blank-tile convention, not sentinel": CONFIRMED.
            var spriteY = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            results[i] = new BuffIconPositionRecord
            {
                BuffId = buffId,
                AtlasX = (int)spriteX,
                AtlasY = (int)spriteY
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses <c>data/script/effectscale.xdb</c>.
    ///     Record count = file_size / 8 (must be exact multiple).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/xdb_tables.md §3: sample_verified true.
    ///     <para>
    ///         <c>scale_factor</c> is the per-effect overall size multiplier applied uniformly on spawn
    ///         (scales the entire effect in all axes). CONFIRMED two-witness.
    ///         spec: Docs/RE/formats/xdb_tables.md §3 — "scale_factor = per-effect overall size multiplier (CONFIRMED)".
    ///     </para>
    /// </remarks>
    public static EffectScaleRecord[] ParseEffectScaleXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, EffectScaleStride, "effectscale.xdb", "Docs/RE/formats/xdb_tables.md §3");
        var count = span.Length / EffectScaleStride;
        var results = new EffectScaleRecord[count];

        for (var i = 0; i < count; i++)
        {
            var offset = i * EffectScaleStride;
            var rec = span.Slice(offset, EffectScaleStride);

            // effect_id u32le @ +0. Key identifying the effect object.
            // spec: Docs/RE/formats/xdb_tables.md §3 — "effect_id u32 @ +0: CONFIRMED".
            var objectId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // scale_factor f32le @ +4. Per-effect OVERALL SIZE MULTIPLIER applied uniformly on spawn.
            // Scales the entire effect in all axes simultaneously. CONFIRMED two-witness.
            // spec: Docs/RE/formats/xdb_tables.md §3 — "scale_factor f32 @ +4 = per-effect overall size multiplier: CONFIRMED".
            var scale = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            results[i] = new EffectScaleRecord
            {
                ObjectId = objectId,
                Scale = scale
            };
        }

        return results;
    }

    /// <summary>
    ///     Parses <c>data/script/vehicle.xdb</c> — mount/seat catalogue, keyed by vehicle_id.
    ///     Record count = file_size / 52 (must be exact multiple).
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/xdb_tables.md §4 vehicle.xdb: sample_verified true.
    ///     <para>
    ///         Runtime use CONFIRMED (CYCLE 1): keyed by <c>vehicle_id</c> (+0) on the mount path.
    ///         Two consumers: (1) mount-attachment refresh; (2) per-frame rider Y-placement, reading
    ///         per-facing seat-Y from float indices 9..12 = byte offsets +0x24/+0x28/+0x2C/+0x30.
    ///         <c>tag_a</c> (+8) and <c>tag_b</c> (+12) are NOT read by either consumer (parse, do not branch).
    ///         spec: Docs/RE/formats/xdb_tables.md §4 — "runtime consumers: vehicle_id key + per-facing seat-Y floats;
    ///         tag_a/tag_b not read".
    ///     </para>
    /// </remarks>
    public static VehicleXdbRecord[] ParseVehicleXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, VehicleStride, "vehicle.xdb", "Docs/RE/formats/xdb_tables.md §4");
        var count = span.Length / VehicleStride;
        var results = new VehicleXdbRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * VehicleStride;
            var rec = span.Slice(recBase, VehicleStride);

            // vehicle_id u32LE @ +0. Sequential 1-based (1..58). Runtime lookup key. CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0: CONFIRMED (lookup key).
            var vehicleId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // item_id u32LE @ +4. Consecutive block (id 1 → 3108, id 2 → 3109, …). CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — item_id u32LE @ +4: CONFIRMED.
            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // tag_a u32LE @ +8. Three distinct values across 58 records (vehicle-family discriminator).
            // NOT READ by either mount-path consumer (CYCLE 1, static-confirmed). Parse but do NOT branch.
            // spec: Docs/RE/formats/xdb_tables.md §4 — "tag_a u32LE @ +8: not read by consumers (CYCLE 1)".
            var tagA = BinaryPrimitives.ReadUInt32LittleEndian(rec[VehicleTagAOffset..]);

            // tag_b u32LE @ +12. CONSTANT 0x1575A3E4 in all 58 records (table-type stamp).
            // NOT READ by either consumer.
            // spec: Docs/RE/formats/xdb_tables.md §4 — "tag_b u32LE @ +12: CONSTANT 0x1575A3E4 (table_stamp)": CONFIRMED.
            var tagB = BinaryPrimitives.ReadUInt32LittleEndian(rec[VehicleTagBOffset..]);

            // param_0 f32LE @ +16. Lateral rider mount-point X (most records zero). INFERRED axis.
            // spec: Docs/RE/formats/xdb_tables.md §4 — param_0 f32LE @ +16: HIGH (present); INFERRED (X offset).
            var param0 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam0Offset..]);

            // param_1 f32LE @ +20. ALWAYS 0.0 across all 58 records (constrained axis).
            // spec: Docs/RE/formats/xdb_tables.md §4 — param_1 f32LE @ +20: CONFIRMED (always 0); INFERRED (constrained axis).
            var param1 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam1Offset..]);

            // param_2 f32LE @ +24 (byte offset +0x18, float index 6). INFERRED rider Z offset.
            // Also: per-facing seat-Y for facing=1 (float index 9 from record start = byte +0x24).
            // Wait — float index within record = byte offset / 4. +0x24 = 36 / 4 = 9th float (0-based).
            // Record floats 0..12: [vehicleId, itemId, tagA, tagB, param0, param1, param2, param3, param4, param5, param6, param7, param8]
            // Index 9 = param_5 at byte +36; Index 10 = param_6 at +40; etc.
            // Per-facing seat-Y: float indices 9..12 = byte offsets +0x24/+0x28/+0x2C/+0x30
            // = the consumer reads rec[as float array][facing+8] for facing 1..4.
            // +0x24 = byte 36 = param_5 slot (NOT param_2). The labels below use the spec's param names.
            // spec: Docs/RE/formats/xdb_tables.md §4 — "per-facing seat-Y floats at float indices 9..12 = byte offsets +0x24/+0x28/+0x2C/+0x30".
            var param2 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam2Offset..]); // @ +24
            var param3 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam3Offset..]); // @ +28
            var param4 = BinaryPrimitives.ReadSingleLittleEndian(rec[VehicleParam4Offset..]); // @ +32

            // param_5..8: f32[4] @ +36..+51. Float indices 9..12 when viewed as float array.
            // Per-facing seat-Y for facings 1..4 maps to these indices (float index = facing+8).
            // facing=1 → index 9 → +0x24 = byte 36 = param_5[0]
            // facing=2 → index 10 → +0x28 = byte 40 = param_5[1]
            // facing=3 → index 11 → +0x2C = byte 44 = param_5[2]
            // facing=4 → index 12 → +0x30 = byte 48 = param_5[3]
            // spec: Docs/RE/formats/xdb_tables.md §4 — "param_5..8 f32[4] @ +36..+51 (facing seat-Y, indices 9..12)".
            var seatYFacing1 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 0)..]);
            var seatYFacing2 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 4)..]);
            var seatYFacing3 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 8)..]);
            var seatYFacing4 = BinaryPrimitives.ReadSingleLittleEndian(rec[(VehicleParam5to8Offset + 12)..]);

            results[i] = new VehicleXdbRecord
            {
                VehicleId = vehicleId,
                ItemId = itemId,
                TagA = tagA,
                TagB = tagB,
                Param0 = param0,
                Param1 = param1,
                Param2 = param2,
                Param3 = param3,
                Param4 = param4,
                SeatYFacing1 = seatYFacing1,
                SeatYFacing2 = seatYFacing2,
                SeatYFacing3 = seatYFacing3,
                SeatYFacing4 = seatYFacing4
            };
        }

        return results;
    }

    // Attachment float layout (CONFIRMED two-witness):
    //   The six f32 values at +8..+28 are THREE XZ OFFSET PAIRS in the creature's facing frame.
    //   Layout: (offset0_x, offset0_z), (offset1_x, offset1_z), (offset2_x, offset2_z)
    //   Y is always forced to 0 by the runtime — no Y component is stored.
    //   These are NOT bone indices; they are world-space XZ offsets from the creature origin,
    //   rotated into the creature's current facing direction before apply.
    // spec: Docs/RE/formats/xdb_tables.md §5 — "six floats = three XZ offset pairs in facing frame, Y forced 0: CONFIRMED two-witness".

    /// <summary>
    ///     Parses <c>data/script/creature_item.xdb</c> — the creature HELD-ITEM VISUAL attachment table.
    ///     Record count = file_size / 48 (must be exact multiple).
    ///     This is NOT a loot/drop table.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/xdb_tables.md §5 creature_item.xdb: sample_verified true.
    ///     <para>
    ///         <b>Resolved role (CYCLE 1, STATIC-CONFIRMED):</b> <c>creature_item.xdb</c> is a creature
    ///         held/worn-item VISUAL attachment table — it places a visual item ON the creature at spawn.
    ///         Keyed by <c>creature_key</c> (record +0, from the creature actor's appearance/visual-key field).
    ///         Two consumers: (1) spawn-attach — spawns and attaches <c>item_id</c> (+4) as a visual, placed by
    ///         the three XZ offset pairs (+8..+31) rotated into world-space by the creature's facing (Y forced 0),
    ///         with <c>visual_scale</c> (+36) carried into the spawned descriptor; (2) per-tick gate — validates
    ///         pickup/effect cadence using flag bytes (+40, +43) and the millisecond <c>tick_interval</c> (+44).
    ///         The earlier "drop / loot / 100%-probability" framing is WITHDRAWN.
    ///         spec: Docs/RE/formats/xdb_tables.md §5 — CYCLE 1 RELABEL: creature held-item VISUAL attachment, NOT loot table.
    ///         spec: Docs/RE/specs/assembly_graph.md §3 — "creature_item.xdb → held-item visual, not a loot table".
    ///     </para>
    ///     <para>
    ///         The six attachment floats (+8..+28) encode three XZ offset pairs in the creature's facing
    ///         frame: <c>(off0X, off0Z), (off1X, off1Z), (off2X, off2Z)</c>. Y is forced to 0 by the
    ///         runtime — no Y component is stored in the file. These are NOT bone indices.
    ///         spec: Docs/RE/formats/xdb_tables.md §5 — "three XZ offset pairs in facing frame, Y forced 0: CONFIRMED
    ///         two-witness".
    ///     </para>
    /// </remarks>
    public static CreatureItemXdbRecord[] ParseCreatureItemXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, CreatureItemStride, "creature_item.xdb", "Docs/RE/formats/xdb_tables.md §5");
        var count = span.Length / CreatureItemStride;
        var results = new CreatureItemXdbRecord[count];

        for (var i = 0; i < count; i++)
        {
            var recBase = i * CreatureItemStride;
            var rec = span.Slice(recBase, CreatureItemStride);

            // creature_key u32LE @ +0. Large sequential-by-1 compound key. CONFIRMED (pattern).
            // spec: Docs/RE/formats/xdb_tables.md §5 — creature_key u32LE @ +0: CONFIRMED (pattern).
            var creatureKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[..]);

            // item_id u32LE @ +4. Attached item id (e.g. 3001). CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — item_id u32LE @ +4: CONFIRMED.
            var itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // Three XZ offset pairs (attach_f0..attach_f5) f32LE @ +8..+28.
            // LAYOUT: (off0X, off0Z), (off1X, off1Z), (off2X, off2Z) — in creature facing frame.
            // Y is forced to 0 by the runtime; no Y component is stored here.
            // CONFIRMED two-witness. These are NOT bone indices.
            // spec: Docs/RE/formats/xdb_tables.md §5 — "six floats = three XZ offset pairs in facing frame, Y=0: CONFIRMED two-witness".
            var f0 = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]); // off0X
            var f1 = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]); // off0Z
            var f2 = BinaryPrimitives.ReadSingleLittleEndian(rec[16..]); // off1X
            var f3 = BinaryPrimitives.ReadSingleLittleEndian(rec[20..]); // off1Z
            var f4 = BinaryPrimitives.ReadSingleLittleEndian(rec[24..]); // off2X
            var f5 = BinaryPrimitives.ReadSingleLittleEndian(rec[28..]); // off2Z

            // scale_or_radius f32LE @ +32. Head value 8.0; semantic UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — scale_or_radius f32LE @ +32: CONFIRMED (present); semantic UNVERIFIED.
            var scaleOrRadius = BinaryPrimitives.ReadSingleLittleEndian(rec[32..]);

            // visual_scale f32LE @ +36. CONSUMER-CONFIRMED: copied into the spawned visual item's descriptor.
            // 716 of 921 records are 0.0; 205 records carry clean fractional values (0.1, 0.2, 0.3, 0.4, 0.7, 0.8, 1.0, 1.8, 2.0, 2.2).
            // 0.0 = "use default scale"; non-zero = per-entry visual scale for the attached item.
            // Earlier "unknown_u1 u32" framing is WITHDRAWN — field is a float, confirmed by consumer read.
            // spec: Docs/RE/formats/xdb_tables.md §5 — "visual_scale f32LE @ +36: CONFIRMED (f32, non-zero in 205/921; copied into spawned visual)".
            var unknownU1 = BinaryPrimitives.ReadSingleLittleEndian(rec[36..]);

            // flag_0..flag_3 u8 @ +40..+43. Semantics UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — flag_0..flag_3 u8 @ +40..+43: UNVERIFIED.
            var flag0 = rec[40];
            var flag1 = rec[41];
            var flag2 = rec[42];
            var flag3 = rec[43];

            // tick_interval u32LE @ +44. Constant 100 (0x64) in all 921 records.
            // Consumer-confirmed as a millisecond tick-interval value — the cadence at which the attachment
            // re-validates pickup/effect. NOT an integer-percent drop probability.
            // spec: Docs/RE/formats/xdb_tables.md §5 — tick_interval u32LE @ +44: CONFIRMED (constant 100; cadence interval).
            // spec: Docs/RE/formats/xdb_tables.md §5 — "the earlier 'probability' framing is withdrawn".
            var probability = BinaryPrimitives.ReadUInt32LittleEndian(rec[44..]);

            results[i] = new CreatureItemXdbRecord
            {
                CreatureKey = creatureKey,
                ItemId = itemId,
                AttachF0 = f0,
                AttachF1 = f1,
                AttachF2 = f2,
                AttachF3 = f3,
                AttachF4 = f4,
                AttachF5 = f5,
                ScaleOrRadius = scaleOrRadius,
                VisualScale = unknownU1,
                Flag0 = flag0,
                Flag1 = flag1,
                Flag2 = flag2,
                Flag3 = flag3,
                Probability = probability
            };
        }

        return results;
    }

    // ─── helper ───────────────────────────────────────────────────────────────

    private static void EnsureExactStride(ReadOnlySpan<byte> span, int stride, string fileName, string specRef)
    {
        if (span.Length % stride != 0)
            throw new InvalidDataException(
                $"{fileName} parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {stride}. spec: {specRef}.");
    }
}