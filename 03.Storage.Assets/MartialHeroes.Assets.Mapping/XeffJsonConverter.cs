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
        // spec: Docs/RE/formats/effects.md §A.3.3 Group C — Alpha keyframes (inverted, stored as opacity): CONFIRMED.
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
            // AlphaKeyframes emitted with named opacity; the parser has already applied 1 - file_value.
            // spec: Docs/RE/formats/effects.md §A.3.3 — "in-memory value is opacity": CONFIRMED.
            AlphaKeyframes: MapAlphaKeyframes(el.AlphaKeyframes),
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
            // spec: Docs/RE/formats/effects.md §A.3.7 — velocity Vec3 (Params[0..2]): HIGH.
            // spec: Docs/RE/formats/effects.md §A.3.7 — size Vec3 (Params[3..5]): HIGH.
            // spec: Docs/RE/formats/effects.md §A.4 — Rotation quaternion (Euler degrees → quat): CONFIRMED.
            // Params array is kept for backward-compat / fallback for fields not yet resolved.
            Vec3 vel = kf.Velocity;
            Vec3 sz = kf.Size;
            Quat rot = kf.Rotation;
            result[i] = new XeffJsonKeyframe(
                KfIndex: kf.KfIndex,
                Params: kf.Params,
                Velocity: new float[] { vel.X, vel.Y, vel.Z },
                Size: new float[] { sz.X, sz.Y, sz.Z },
                Rotation: new float[] { rot.X, rot.Y, rot.Z, rot.W },
                RotXDeg: kf.RotXDeg,
                RotYDeg: kf.RotYDeg,
                RotZDeg: kf.RotZDeg);
        }

        return result;
    }

    private static XeffJsonStaticState MapStaticState(XeffStaticState s)
    {
        // spec: Docs/RE/formats/effects.md §A.3.6 Branch B — 6 params + optional rot (emitter_type==2): PARSER-CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.3.7 — velocity Vec3 (Params[0..2]): HIGH.
        // spec: Docs/RE/formats/effects.md §A.3.7 — size Vec3 (Params[3..5]): HIGH.
        // spec: Docs/RE/formats/effects.md §A.4 — Rotation quaternion (identity when emitter_type!=2): CONFIRMED.
        // Params array kept as fallback.
        Vec3 vel = s.Velocity;
        Vec3 sz = s.Size;
        Quat rot = s.Rotation;
        return new XeffJsonStaticState(
            Params: s.Params,
            Velocity: new float[] { vel.X, vel.Y, vel.Z },
            Size: new float[] { sz.X, sz.Y, sz.Z },
            Rotation: new float[] { rot.X, rot.Y, rot.Z, rot.W },
            RotXDeg: s.RotXDeg,
            RotYDeg: s.RotYDeg,
            RotZDeg: s.RotZDeg);
    }

    /// <summary>
    /// Converts per-keyframe alpha values (stored inverted in the file) to opacity values.
    /// spec: Docs/RE/formats/effects.md §A.3.3 — "Stored inverted: 0.0 = opaque, 1.0 = transparent.
    ///   In-memory value is opacity (0.0 = transparent, 1.0 = opaque)." CONFIRMED.
    /// The parser already stores the inverted (in-memory = opacity) form in AlphaKeyframes.
    /// We emit both the raw (file) value and the opacity for consumer clarity.
    /// </summary>
    private static XeffJsonAlpha[] MapAlphaKeyframes(float[] keyframes)
    {
        var result = new XeffJsonAlpha[keyframes.Length];
        for (int i = 0; i < keyframes.Length; i++)
        {
            // AlphaKeyframes is already the in-memory opacity = (1 - file_value).
            // spec: Docs/RE/formats/effects.md §A.3.3 — "loads as 1.0 − file_value": CONFIRMED.
            float opacity = keyframes[i]; // already converted by parser
            result[i] = new XeffJsonAlpha(Opacity: opacity);
        }

        return result;
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
        // Alpha keyframes emitted as {opacity} objects (parser already holds 1-file_value).
        // spec: Docs/RE/formats/effects.md §A.3.3 — "in-memory value is opacity": CONFIRMED.
        XeffJsonAlpha[] AlphaKeyframes,
        float[] ScaleX,
        float[] ScaleY,
        float[] ScaleZ,
        byte AnimLoop,
        uint AnimStride,
        uint AnimBaseTime,
        XeffJsonKeyframe[]? AnimKeyframes,
        XeffJsonStaticState? StaticState);

    /// <summary>
    /// One alpha keyframe entry. Emits <c>opacity</c> (the in-memory / render-time value).
    /// spec: Docs/RE/formats/effects.md §A.3.3 — "in-memory value is opacity (0=transparent,1=opaque)": CONFIRMED.
    /// </summary>
    private sealed record XeffJsonAlpha(float Opacity);

    // Animated keyframe — velocity/size/rotation are now named; params kept as fallback.
    // spec: Docs/RE/formats/effects.md §A.3.7 — velocity Vec3 (Params[0..2]): HIGH.
    // spec: Docs/RE/formats/effects.md §A.3.7 — size Vec3 (Params[3..5]): HIGH.
    // spec: Docs/RE/formats/effects.md §A.4  — rotation quaternion (from Euler degrees): CONFIRMED.
    private sealed record XeffJsonKeyframe(
        uint KfIndex,
        /// <summary>6 float params; kept as raw fallback.</summary>
        float[] Params,
        /// <summary>Emission velocity Vec3 [X,Y,Z]. spec: §A.3.7: HIGH.</summary>
        float[] Velocity,
        /// <summary>Billboard/particle size Vec3 [X,Y,Z]. spec: §A.3.7: HIGH.</summary>
        float[] Size,
        /// <summary>Rotation quaternion [X,Y,Z,W] derived from Euler degrees. spec: §A.4: CONFIRMED.</summary>
        float[] Rotation,
        float RotXDeg,
        float RotYDeg,
        float RotZDeg);

    // Static emitter state — velocity/size/rotation are now named; params kept as fallback.
    // spec: Docs/RE/formats/effects.md §A.3.7 — velocity Vec3 (Params[0..2]): HIGH.
    // spec: Docs/RE/formats/effects.md §A.3.7 — size Vec3 (Params[3..5]): HIGH.
    // spec: Docs/RE/formats/effects.md §A.4  — rotation quaternion: CONFIRMED.
    private sealed record XeffJsonStaticState(
        /// <summary>6 float params; kept as raw fallback.</summary>
        float[] Params,
        /// <summary>Static emission velocity Vec3 [X,Y,Z]. spec: §A.3.7: HIGH.</summary>
        float[] Velocity,
        /// <summary>Static billboard/particle size Vec3 [X,Y,Z]. spec: §A.3.7: HIGH.</summary>
        float[] Size,
        /// <summary>Rotation quaternion [X,Y,Z,W] (identity when emitter_type!=2). spec: §A.4: CONFIRMED.</summary>
        float[] Rotation,
        float? RotXDeg,
        float? RotYDeg,
        float? RotZDeg);
}