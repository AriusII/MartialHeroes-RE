using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parsers for <c>.xdb</c> script data binary files:
/// <c>actor_size.xdb</c> (12 B), <c>buff_icon_position.xdb</c> (12 B), <c>effectscale.xdb</c> (8 B).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/misc_data.md §1. .xdb Script Data Binary files
/// Common structure: no header; record count = file_size / stride; stride must divide evenly.
/// ZERO rendering/engine dependencies.
/// </remarks>
public static class XdbParser
{
    // =========================================================================
    // actor_size.xdb — Per-actor-class scale override (stride: 12 bytes)
    // =========================================================================

    // Stride: 12 bytes. CONFIRMED (180 bytes = 15 records).
    // spec: Docs/RE/formats/misc_data.md §1.2 — "stride 12 bytes": CONFIRMED.
    private const int ActorSizeStride = 12;

    /// <summary>
    /// Parses <c>data/script/actor_size.xdb</c>.
    /// Record count = file_size / 12 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §1.2: sample_verified true.
    /// </remarks>
    public static ActorSizeRecord[] ParseActorSizeXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, ActorSizeStride, "actor_size.xdb", "Docs/RE/formats/misc_data.md §1.2");
        int count = span.Length / ActorSizeStride;
        var results = new ActorSizeRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * ActorSizeStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, ActorSizeStride);

            // actor_class_id u32le @ +0. HIGH.
            // spec: Docs/RE/formats/misc_data.md §1.2 — "actor_class_id u32 @ 0: HIGH".
            uint actorClassId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // scale_xz f32le @ +4. Range 0.10–2.00. HIGH.
            // spec: Docs/RE/formats/misc_data.md §1.2 — "scale_xz f32 @ 4: HIGH".
            float scaleXz = BinaryPrimitives.ReadSingleLittleEndian(rec[4..]);

            // scale_y f32le @ +8. Range 1.00–1.50. HIGH.
            // spec: Docs/RE/formats/misc_data.md §1.2 — "scale_y f32 @ 8: HIGH".
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
    // buff_icon_position.xdb — Buff-effect icon atlas coordinates (stride: 12 bytes)
    // =========================================================================

    // Stride: 12 bytes. CONFIRMED (1608 bytes = 134 records).
    // spec: Docs/RE/formats/misc_data.md §1.3 — "stride 12 bytes": CONFIRMED.
    private const int BuffIconStride = 12;

    /// <summary>
    /// Parses <c>data/script/buff_icon_position.xdb</c>.
    /// Record count = file_size / 12 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §1.3: sample_verified true.
    /// </remarks>
    public static BuffIconPositionRecord[] ParseBuffIconPositionXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, BuffIconStride, "buff_icon_position.xdb", "Docs/RE/formats/misc_data.md §1.3");
        int count = span.Length / BuffIconStride;
        var results = new BuffIconPositionRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * BuffIconStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, BuffIconStride);

            // buff_id u32le @ +0. CODE-CONFIRMED.
            // spec: Docs/RE/formats/misc_data.md §1.3 — "buff_id u32 @ 0: CODE-CONFIRMED".
            uint buffId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // atlas_x i32le @ +4. CODE-CONFIRMED.
            // SPEC CORRECTION 2026-06-13: signed i32LE, not u32LE — resolver returns a signed coordinate pair.
            // spec: Docs/RE/formats/misc_data.md §1.3 — "atlas_x i32 @ 4: CODE-CONFIRMED (corrected 2026-06-13)".
            int atlasX = BinaryPrimitives.ReadInt32LittleEndian(rec[4..]);

            // atlas_y i32le @ +8. CODE-CONFIRMED.
            // spec: Docs/RE/formats/misc_data.md §1.3 — "atlas_y i32 @ 8: CODE-CONFIRMED (corrected 2026-06-13)".
            int atlasY = BinaryPrimitives.ReadInt32LittleEndian(rec[8..]);

            results[i] = new BuffIconPositionRecord
            {
                BuffId = buffId,
                AtlasX = atlasX,
                AtlasY = atlasY,
            };
        }

        return results;
    }

    // =========================================================================
    // effectscale.xdb — Per-effect-object scale (stride: 8 bytes)
    // =========================================================================

    // Stride: 8 bytes. CONFIRMED (16 bytes = 2 records in sample).
    // spec: Docs/RE/formats/misc_data.md §1.4 — "stride 8 bytes": CONFIRMED.
    private const int EffectScaleStride = 8;

    /// <summary>
    /// Parses <c>data/script/effectscale.xdb</c>.
    /// Record count = file_size / 8 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/misc_data.md §1.4: sample_verified true.
    /// </remarks>
    public static EffectScaleRecord[] ParseEffectScaleXdb(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        EnsureExactStride(span, EffectScaleStride, "effectscale.xdb", "Docs/RE/formats/misc_data.md §1.4");
        int count = span.Length / EffectScaleStride;
        var results = new EffectScaleRecord[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * EffectScaleStride;
            ReadOnlySpan<byte> rec = span.Slice(offset, EffectScaleStride);

            // object_id u32le @ +0. HIGH.
            // spec: Docs/RE/formats/misc_data.md §1.4 — "object_id u32 @ 0: HIGH".
            uint objectId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // scale f32le @ +4. HIGH. Observed values: 2.0, 3.0.
            // spec: Docs/RE/formats/misc_data.md §1.4 — "scale f32 @ 4: HIGH".
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

    /// <summary>
    /// Parses <c>data/script/vehicle.xdb</c>.
    /// Record count = file_size / 52 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/xdb_tables.md §4 vehicle.xdb: sample_verified true.
    /// Fields beyond vehicle_id and item_id are UNVERIFIED; carried through as raw bytes.
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
            // spec: Docs/RE/formats/xdb_tables.md §4 — unknown_8b u8[8] @ +8: UNVERIFIED.
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

    /// <summary>
    /// Parses <c>data/script/creature_item.xdb</c>.
    /// Record count = file_size / 48 (must be exact multiple).
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/xdb_tables.md §5 creature_item.xdb: sample_verified true.
    /// The six attachment floats (+8..+31) likely encode a 3D attachment transform;
    /// axis mapping is UNVERIFIED — carried through as raw float values.
    /// spec: Docs/RE/formats/xdb_tables.md §5 Known unknowns —
    ///   "axis mapping of the six attachment floats: UNVERIFIED".
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

            // attach_f0..attach_f5 f32LE @ +8..+28. Attachment transform; axis UNVERIFIED.
            // spec: Docs/RE/formats/xdb_tables.md §5 — attach_f0..f5 @ +8..+28: CONFIRMED (present); axis UNVERIFIED.
            float f0 = BinaryPrimitives.ReadSingleLittleEndian(rec[8..]);
            float f1 = BinaryPrimitives.ReadSingleLittleEndian(rec[12..]);
            float f2 = BinaryPrimitives.ReadSingleLittleEndian(rec[16..]);
            float f3 = BinaryPrimitives.ReadSingleLittleEndian(rec[20..]);
            float f4 = BinaryPrimitives.ReadSingleLittleEndian(rec[24..]);
            float f5 = BinaryPrimitives.ReadSingleLittleEndian(rec[28..]);

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