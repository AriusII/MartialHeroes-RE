using System.Text.Json;
using System.Text.Json.Serialization;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Serializes a <see cref="XeffData"/> (.xeff particle effect descriptor) to a neutral JSON
/// representation using System.Text.Json.
///
/// All fields that are PARSER-CONFIRMED or CONFIRMED are emitted under their documented names.
/// Fields marked SAMPLE-UNVERIFIED or UNRESOLVED are emitted as-is under their parser property
/// names so that downstream tooling can access the raw data without further decoding here.
///
/// No engine/rendering dependency; this is a pure data serialization step.
/// spec: Docs/RE/formats/effects.md §A.2 File Header: VERIFIED (3 real samples).
/// spec: Docs/RE/formats/effects.md §A.3 Element Array: PARSER-CONFIRMED.
/// </summary>
public static class XeffJsonConverter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serializes <paramref name="effect"/> to the UTF-8 JSON output stream.
    /// The stream does not need to be seekable.
    /// </summary>
    /// <param name="effect">Parsed .xeff data from <c>XeffParser</c>.</param>
    /// <param name="output">Destination stream.</param>
    public static void WriteJson(XeffData effect, Stream output)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(output);

        XeffJsonRoot root = MapToRoot(effect);
        JsonSerializer.Serialize(output, root, SerializerOptions);
    }

    /// <summary>
    /// Serializes <paramref name="effect"/> to a UTF-8 JSON byte array.
    /// Useful for tests and in-memory scenarios.
    /// </summary>
    public static byte[] WriteJsonBytes(XeffData effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        XeffJsonRoot root = MapToRoot(effect);
        return JsonSerializer.SerializeToUtf8Bytes(root, SerializerOptions);
    }

    // -------------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------------

    private static XeffJsonRoot MapToRoot(XeffData effect)
    {
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id @ 0x00: VERIFIED.
        // spec: Docs/RE/formats/effects.md §A.2 — element_count @ 0x04: VERIFIED.
        return new XeffJsonRoot(
            EffectId: effect.EffectId,
            Elements: MapElements(effect.Elements));
    }

    private static XeffJsonElement[] MapElements(XeffElement[] elements)
    {
        var result = new XeffJsonElement[elements.Length];
        for (int i = 0; i < elements.Length; i++)
            result[i] = MapElement(elements[i]);
        return result;
    }

    private static XeffJsonElement MapElement(XeffElement el)
    {
        // spec: Docs/RE/formats/effects.md §A.3.1 Group A — Emitter identity: PARSER-CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.3.2 Group B — Texture sub-array: PARSER-CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.3.3 Group C — Alpha keyframes: PARSER-CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.3.4 Group D — Scale channels: PARSER-CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.3.5 Group E — Animation timing: PARSER-CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.3.6 Group F — Keyframe/static-state: PARSER-CONFIRMED.
        return new XeffJsonElement(
            EmitterType: el.EmitterType,
            EmitterSubtype: el.EmitterSubtype,
            AnimFlag: el.AnimFlag,
            TexCount: el.TexCount,
            // FieldUnknownA — purpose UNRESOLVED; emitted raw.
            // spec: Docs/RE/formats/effects.md §A.3.1 — field_unknown_a: PARSER-CONFIRMED (raw value).
            FieldUnknownA: el.FieldUnknownA,
            TextureNames: el.TextureNames,
            AlphaKeyframes: el.AlphaKeyframes,
            ScaleX: el.ScaleX,
            ScaleY: el.ScaleY,
            ScaleZ: el.ScaleZ,
            AnimLoop: el.AnimLoop,
            AnimStride: el.AnimStride,
            AnimBaseTime: el.AnimBaseTime,
            AnimKeyframes: el.AnimKeyframes is { Length: > 0 }
                ? MapKeyframes(el.AnimKeyframes)
                : null,
            StaticState: el.StaticState is not null
                ? MapStaticState(el.StaticState)
                : null);
    }

    private static XeffJsonKeyframe[] MapKeyframes(XeffKeyframe[] kfs)
    {
        var result = new XeffJsonKeyframe[kfs.Length];
        for (int i = 0; i < kfs.Length; i++)
        {
            XeffKeyframe kf = kfs[i];
            // spec: Docs/RE/formats/effects.md §A.3.6 Branch A — kf_index, params, rot_x/y/z_deg: PARSER-CONFIRMED.
            // Params 0-5 purpose UNRESOLVED — emitted raw.
            result[i] = new XeffJsonKeyframe(
                KfIndex: kf.KfIndex,
                Params: kf.Params,
                RotXDeg: kf.RotXDeg,
                RotYDeg: kf.RotYDeg,
                RotZDeg: kf.RotZDeg);
        }

        return result;
    }

    private static XeffJsonStaticState MapStaticState(XeffStaticState s)
    {
        // spec: Docs/RE/formats/effects.md §A.3.6 Branch B — 6 params + optional rot (emitter_type==2): PARSER-CONFIRMED.
        // Params purpose UNRESOLVED — emitted raw.
        return new XeffJsonStaticState(
            Params: s.Params,
            RotXDeg: s.RotXDeg,
            RotYDeg: s.RotYDeg,
            RotZDeg: s.RotZDeg);
    }

    // -------------------------------------------------------------------------
    // JSON data-transfer objects (internal, immutable records)
    // -------------------------------------------------------------------------

    // Root object
    private sealed record XeffJsonRoot(
        uint EffectId,
        XeffJsonElement[] Elements);

    // One element / emitter
    private sealed record XeffJsonElement(
        uint EmitterType,
        uint EmitterSubtype,
        uint AnimFlag,
        uint TexCount,
        /// <summary>UNRESOLVED field; emitted raw for downstream tooling.</summary>
        uint FieldUnknownA,
        string[] TextureNames,
        float[] AlphaKeyframes,
        float[] ScaleX,
        float[] ScaleY,
        float[] ScaleZ,
        byte AnimLoop,
        uint AnimStride,
        uint AnimBaseTime,
        XeffJsonKeyframe[]? AnimKeyframes,
        XeffJsonStaticState? StaticState);

    // Animated keyframe
    private sealed record XeffJsonKeyframe(
        uint KfIndex,
        /// <summary>6 float params; purpose UNRESOLVED — emitted raw.</summary>
        float[] Params,
        float RotXDeg,
        float RotYDeg,
        float RotZDeg);

    // Static emitter state
    private sealed record XeffJsonStaticState(
        /// <summary>6 float params; purpose UNRESOLVED — emitted raw.</summary>
        float[] Params,
        float? RotXDeg,
        float? RotYDeg,
        float? RotZDeg);
}