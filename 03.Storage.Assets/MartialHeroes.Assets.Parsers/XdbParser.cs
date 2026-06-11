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

            // buff_id u32le @ +0. HIGH.
            // spec: Docs/RE/formats/misc_data.md §1.3 — "buff_id u32 @ 0: HIGH".
            uint buffId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..]);

            // atlas_x u32le @ +4. HIGH.
            // spec: Docs/RE/formats/misc_data.md §1.3 — "atlas_x u32 @ 4: HIGH".
            uint atlasX = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..]);

            // atlas_y u32le @ +8. HIGH.
            // spec: Docs/RE/formats/misc_data.md §1.3 — "atlas_y u32 @ 8: HIGH".
            uint atlasY = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);

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

    // ─── helper ───────────────────────────────────────────────────────────────

    private static void EnsureExactStride(ReadOnlySpan<byte> span, int stride, string fileName, string specRef)
    {
        if (span.Length % stride != 0)
            throw new InvalidDataException(
                $"{fileName} parse error: buffer length {span.Length} is not an exact multiple of " +
                $"stride {stride}. spec: {specRef}.");
    }
}
