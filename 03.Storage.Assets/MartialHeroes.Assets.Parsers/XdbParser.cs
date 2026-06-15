using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parsers for <c>.xdb</c> script data binary files:
/// <c>actor_size.xdb</c> (12 B — DEAD, no runtime consumer), <c>buff_icon_position.xdb</c> (12 B),
/// <c>effectscale.xdb</c> (8 B), <c>vehicle.xdb</c> (52 B), <c>creature_item.xdb</c> (48 B).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/xdb_tables.md
/// Common structure: no header; record count = file_size / stride; stride must divide evenly.
/// ZERO rendering/engine dependencies.
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

    /// <summary>
    /// Parses <c>data/script/actor_size.xdb</c>.
    /// Record count = file_size / 12 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// <b>WARNING — DEAD IN THIS BUILD.</b> The shipped client has zero runtime consumers for
    /// this file. This parser exists for archival completeness only; a faithful port must NOT
    /// call this at runtime.
    /// spec: Docs/RE/formats/xdb_tables.md §1 — "DEAD IN THIS BUILD — DO NOT LOAD (loader-resolved)".
    /// </remarks>
    public static ActorSizeRecord[] ParseActorSizeXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, ActorSizeStride, "actor_size.xdb", "Docs/RE/formats/xdb_tables.md §1");
        int count = span.Length / ActorSizeStride;
        var results = new ActorSizeRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * ActorSizeStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, ActorSizeStride);

            // actor_kind_id u32le @ +0. Sequential 0-based (0..14). CONFIRMED (on disk); table unread at runtime.
            // spec: Docs/RE/formats/xdb_tables.md §1 — "actor_kind_id u32 @ +0: CONFIRMED (on disk); table unread at runtime".
            uint actorClassId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // scale_a f32le @ +4. Proposed horizontal/radial scale. CONFIRMED (value); INFERRED (axis); table unread at runtime.
            // spec: Docs/RE/formats/xdb_tables.md §1 — "scale_a f32 @ +4: CONFIRMED value; INFERRED axis; table unread at runtime".
            float scaleXz = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // scale_b f32le @ +8. Proposed vertical/height scale. CONFIRMED (value); INFERRED (axis); table unread at runtime.
            // spec: Docs/RE/formats/xdb_tables.md §1 — "scale_b f32 @ +8: CONFIRMED value; INFERRED axis; table unread at runtime".
            float scaleY = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);

            results[i] = new ActorSizeRecord
            {
                ActorClassId = actorClassId,
                ScaleXz = scaleXz,
                ScaleY = scaleY,
            };
        }

        return results;
    }

    // =========================================================================
    // buff_icon_position.xdb — Buff-effect icon sprite-sheet positions (stride: 12 bytes)
    // =========================================================================

    // Stride: 12 bytes. CONFIRMED (1,608 bytes = 134 records, 1608/12=134 exact).
    // spec: Docs/RE/formats/xdb_tables.md §2 — "stride 12 bytes, 134 records": CONFIRMED.
    private const int BuffIconStride = 12;

    // Render cell geometry (CONFIRMED-variable):
    //   DrawCellSize  = 21×21 pixels (the blitted icon footprint)
    //   OriginSpacing = 27 pixels (stride between successive sprite_x/sprite_y origins)
    // spec: Docs/RE/formats/xdb_tables.md §2 — "render cell 21×21, origin spacing 27": CONFIRMED-variable.
    // NOTE: sprite_y = 401 is a DATA-SIDE BLANK-TILE CONVENTION (not a code sentinel).
    //   No loader branch tests for 401; it is an authored Y-origin pointing at a deliberately empty
    //   tile on the sprite sheet. Treat it as any other sprite_y — do NOT add a hardcoded sentinel check.
    // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 data-side blank-tile convention, not a code sentinel": CONFIRMED.
    // NOTE: buff_id is NON-CONTIGUOUS (head happens to begin 1,2,3,… but spans 1..1103 with only
    //   134 entries populated). Index by stored buff_id, never by row position.
    // spec: Docs/RE/formats/xdb_tables.md §2 — "buff_id non-contiguous: CONFIRMED".

    /// <summary>
    /// Parses <c>data/script/buff_icon_position.xdb</c>.
    /// Record count = file_size / 12 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/xdb_tables.md §2: sample_verified true.
    /// <para>
    /// <c>sprite_y = 401</c> is a data-side blank-tile convention — treat it as any other pixel-Y
    /// origin. Do NOT add a hardcoded sentinel check.
    /// Render cell is 21×21 pixels; origin spacing between cells is 27 pixels.
    /// </para>
    /// </remarks>
    public static BuffIconPositionRecord[] ParseBuffIconPositionXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, BuffIconStride, "buff_icon_position.xdb", "Docs/RE/formats/xdb_tables.md §2");
        int count = span.Length / BuffIconStride;
        var results = new BuffIconPositionRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * BuffIconStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, BuffIconStride);

            // buff_id u32le @ +0. Sparse non-contiguous range 1..1103 (only 134 slots populated).
            // Index the table by stored buff_id, never by row position.
            // spec: Docs/RE/formats/xdb_tables.md §2 — "buff_id u32 @ +0: CONFIRMED (non-contiguous)".
            uint buffId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // sprite_x u32le @ +4. Pixel X origin on the buff-icon sprite sheet.
            // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_x u32 @ +4: CONFIRMED".
            uint spriteX = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // sprite_y u32le @ +8. Pixel Y origin on the sprite sheet.
            // Value 401 is a DATA-SIDE BLANK-TILE CONVENTION (not a code sentinel) — treat as
            // any other Y origin; no hardcoded branch on 401.
            // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y u32 @ +8: CONFIRMED-variable".
            // spec: Docs/RE/formats/xdb_tables.md §2 — "sprite_y=401 blank-tile convention, not sentinel": CONFIRMED.
            uint spriteY = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

            results[i] = new BuffIconPositionRecord
            {
                BuffId = buffId,
                AtlasX = (int)spriteX,
                AtlasY = (int)spriteY,
            };
        }

        return results;
    }

    // =========================================================================
    // effectscale.xdb — Per-effect overall size multiplier (stride: 8 bytes)
    // =========================================================================

    // Stride: 8 bytes. CONFIRMED (file_size exact multiple of 8 enforced).
    // spec: Docs/RE/formats/xdb_tables.md §3 — "stride 8 bytes": CONFIRMED.
    private const int EffectScaleStride = 8;

    /// <summary>
    /// Parses <c>data/script/effectscale.xdb</c>.
    /// Record count = file_size / 8 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/xdb_tables.md §3: sample_verified true.
    /// <para>
    /// <c>scale_factor</c> is the per-effect overall size multiplier applied uniformly on spawn
    /// (scales the entire effect in all axes). CONFIRMED two-witness.
    /// spec: Docs/RE/formats/xdb_tables.md §3 — "scale_factor = per-effect overall size multiplier (CONFIRMED)".
    /// </para>
    /// </remarks>
    public static EffectScaleRecord[] ParseEffectScaleXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, EffectScaleStride, "effectscale.xdb", "Docs/RE/formats/xdb_tables.md §3");
        int count = span.Length / EffectScaleStride;
        var results = new EffectScaleRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * EffectScaleStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, EffectScaleStride);

            // effect_id u32le @ +0. Key identifying the effect object.
            // spec: Docs/RE/formats/xdb_tables.md §3 — "effect_id u32 @ +0: CONFIRMED".
            uint objectId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // scale_factor f32le @ +4. Per-effect OVERALL SIZE MULTIPLIER applied uniformly on spawn.
            // Scales the entire effect in all axes simultaneously. CONFIRMED two-witness.
            // spec: Docs/RE/formats/xdb_tables.md §3 — "scale_factor f32 @ +4 = per-effect overall size multiplier: CONFIRMED".
            float scale = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            results[i] = new EffectScaleRecord
            {
                ObjectId = objectId,
                Scale = scale,
            };
        }

        return results;
    }

    // =========================================================================
    // vehicle.xdb — Vehicle / mount catalogue (stride: 52 bytes)
    // =========================================================================

    // Stride: 52 bytes. CONFIRMED (3,016 bytes = 58 records, 3016 / 52 = 58, exact).
    // spec: Docs/RE/formats/xdb_tables.md §4 — "stride 52 bytes, 58 records": CONFIRMED.
    private const int VehicleStride = 52;

    // Unknown 8-byte region starts at record +8. UNVERIFIED layout.
    // spec: Docs/RE/formats/xdb_tables.md §4 — unknown_8b u8[8] @ +8: UNVERIFIED.
    private const int VehicleUnknown8bOffset = 8;
    private const int VehicleUnknown8bLen = 8;

    // 36-byte zero region starts at record +16. Layout UNVERIFIED; all-zero in head records.
    // spec: Docs/RE/formats/xdb_tables.md §4 — zero_region u8[36] @ +16: layout UNVERIFIED.
    private const int VehicleZeroRegionOffset = 16;
    private const int VehicleZeroRegionLen = 36;

    // NOTE: tag_a (if encountered in earlier spec readings) is tool-side authoring metadata.
    // REFUTED as a runtime discriminator: the shipped client does NOT read or branch on this field
    // at runtime. Do NOT implement any runtime logic keyed on tag_a.
    // spec: Docs/RE/formats/xdb_tables.md §4 — "tag_a: tool-only, REFUTED as runtime field (loader-resolved)".

    /// <summary>
    /// Parses <c>data/script/vehicle.xdb</c>.
    /// Record count = file_size / 52 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/xdb_tables.md §4 vehicle.xdb: sample_verified true.
    /// <para>
    /// Fields beyond <c>vehicle_id</c> and <c>item_id</c> are UNVERIFIED in their sub-layout;
    /// carried through as raw bytes. The <c>tag_a</c> field mentioned in authoring tooling is
    /// tool-side only — REFUTED as a runtime discriminator.
    /// spec: Docs/RE/formats/xdb_tables.md §4 — "tag_a: tool-only, REFUTED as runtime field".
    /// </para>
    /// </remarks>
    public static VehicleXdbRecord[] ParseVehicleXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, VehicleStride, "vehicle.xdb", "Docs/RE/formats/xdb_tables.md §4");
        int count = span.Length / VehicleStride;
        var results = new VehicleXdbRecord[count];

        for (int i = 0; i < count; i++)
        {
            int recBase = i * VehicleStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, VehicleStride);

            // vehicle_id u32LE @ +0. Sequential 1-based (1..58). CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — vehicle_id u32LE @ +0: CONFIRMED.
            uint vehicleId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // item_id u32LE @ +4. Consecutive block (id 1 → 3108, id 2 → 3109, …). CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — item_id u32LE @ +4: CONFIRMED.
            uint itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // unknown_8b u8[8] @ +8. Identical 8-byte run in head records. UNVERIFIED layout.
            // NOTE: includes the authoring-only "tag_a" region; tag_a is REFUTED as runtime field.
            // spec: Docs/RE/formats/xdb_tables.md §4 — unknown_8b u8[8] @ +8: UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — "tag_a: tool-only, REFUTED as runtime field".
            // Carried through as raw bytes without interpretation.
            ReadOnlyMemory<byte> unknown8b =
                data.Slice(recBase + VehicleUnknown8bOffset, VehicleUnknown8bLen);

            // zero_region u8[36] @ +16. All-zero in head records; layout UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §4 — zero_region u8[36] @ +16: layout UNVERIFIED.
            // Carried through as raw bytes without interpretation.
            ReadOnlyMemory<byte> zeroRegion =
                data.Slice(recBase + VehicleZeroRegionOffset, VehicleZeroRegionLen);

            results[i] = new VehicleXdbRecord
            {
                VehicleId = vehicleId,
                ItemId = itemId,
                Unknown8b = unknown8b,
                ZeroRegion = zeroRegion,
            };
        }

        return results;
    }

    // =========================================================================
    // creature_item.xdb — Creature attached-item table (stride: 48 bytes)
    // =========================================================================

    // Stride: 48 bytes. CONFIRMED (44,208 bytes = 921 records, 44208 / 48 = 921, exact).
    // spec: Docs/RE/formats/xdb_tables.md §5 — "stride 48 bytes, 921 records": CONFIRMED.
    private const int CreatureItemStride = 48;

    // Attachment float layout (CONFIRMED two-witness):
    //   The six f32 values at +8..+28 are THREE XZ OFFSET PAIRS in the creature's facing frame.
    //   Layout: (offset0_x, offset0_z), (offset1_x, offset1_z), (offset2_x, offset2_z)
    //   Y is always forced to 0 by the runtime — no Y component is stored.
    //   These are NOT bone indices; they are world-space XZ offsets from the creature origin,
    //   rotated into the creature's current facing direction before apply.
    // spec: Docs/RE/formats/xdb_tables.md §5 — "six floats = three XZ offset pairs in facing frame, Y forced 0: CONFIRMED two-witness".

    /// <summary>
    /// Parses <c>data/script/creature_item.xdb</c>.
    /// Record count = file_size / 48 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/xdb_tables.md §5 creature_item.xdb: sample_verified true.
    /// <para>
    /// The six attachment floats (+8..+28) encode three XZ offset pairs in the creature's facing
    /// frame: <c>(off0X, off0Z), (off1X, off1Z), (off2X, off2Z)</c>. Y is forced to 0 by the
    /// runtime — no Y component is stored in the file. These are NOT bone indices.
    /// spec: Docs/RE/formats/xdb_tables.md §5 — "three XZ offset pairs in facing frame, Y forced 0: CONFIRMED two-witness".
    /// </para>
    /// </remarks>
    public static CreatureItemXdbRecord[] ParseCreatureItemXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, CreatureItemStride, "creature_item.xdb", "Docs/RE/formats/xdb_tables.md §5");
        int count = span.Length / CreatureItemStride;
        var results = new CreatureItemXdbRecord[count];

        for (int i = 0; i < count; i++)
        {
            int recBase = i * CreatureItemStride;
            ReadOnlySpan<byte> rec = span.Slice(recBase, CreatureItemStride);

            // creature_key u32LE @ +0. Large sequential-by-1 compound key. CONFIRMED (pattern).
            // spec: Docs/RE/formats/xdb_tables.md §5 — creature_key u32LE @ +0: CONFIRMED (pattern).
            uint creatureKey = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // item_id u32LE @ +4. Attached item id (e.g. 3001). CONFIRMED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — item_id u32LE @ +4: CONFIRMED.
            uint itemId = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // Three XZ offset pairs (attach_f0..attach_f5) f32LE @ +8..+28.
            // LAYOUT: (off0X, off0Z), (off1X, off1Z), (off2X, off2Z) — in creature facing frame.
            // Y is forced to 0 by the runtime; no Y component is stored here.
            // CONFIRMED two-witness. These are NOT bone indices.
            // spec: Docs/RE/formats/xdb_tables.md §5 — "six floats = three XZ offset pairs in facing frame, Y=0: CONFIRMED two-witness".
            float f0 = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);   // off0X
            float f1 = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);  // off0Z
            float f2 = BinaryPrimitives.ReadSingleLittleEndian(rec[16..]);  // off1X
            float f3 = BinaryPrimitives.ReadSingleLittleEndian(rec[20..]);  // off1Z
            float f4 = BinaryPrimitives.ReadSingleLittleEndian(rec[24..]);  // off2X
            float f5 = BinaryPrimitives.ReadSingleLittleEndian(rec[28..]);  // off2Z

            // scale_or_radius f32LE @ +32. Head value 8.0; semantic UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — scale_or_radius f32LE @ +32: CONFIRMED (present); semantic UNVERIFIED.
            float scaleOrRadius = BinaryPrimitives.ReadSingleLittleEndian(rec[32..]);

            // unknown_u1 u32LE @ +36. Zero in head records. UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — unknown_u1 u32LE @ +36: CONFIRMED (value=0 in head); UNVERIFIED.
            uint unknownU1 = BinaryPrimitives.ReadUInt32LittleEndian(rec[36..]);

            // flag_0..flag_3 u8 @ +40..+43. Semantics UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — flag_0..flag_3 u8 @ +40..+43: UNVERIFIED.
            byte flag0 = rec[40];
            byte flag1 = rec[41];
            byte flag2 = rec[42];
            byte flag3 = rec[43];

            // probability u32LE @ +44. Value 100 in head records; likely integer percent. Semantic UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — probability u32LE @ +44: CONFIRMED (value=100 in head); semantic UNVERIFIED.
            uint probability = BinaryPrimitives.ReadUInt32LittleEndian(rec[44..]);

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
                UnknownU1 = unknownU1,
                Flag0 = flag0,
                Flag1 = flag1,
                Flag2 = flag2,
                Flag3 = flag3,
                Probability = probability,
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