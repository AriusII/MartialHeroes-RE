using System.Text.Json;
using System.Text.Json.Serialization;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Mapping;

/// <summary>
/// Serializes a <see cref="XeffData"/> (.xeff particle effect descriptor) to a neutral JSON
/// representation using System.Text.Json.
///
/// All fields that are CONFIRMED or SAMPLE-VERIFIED are emitted under their documented names.
/// Fields marked UNRESOLVED are emitted as-is under their parser property names so that
/// downstream tooling can access the raw data without further decoding here.
///
/// No engine/rendering dependency; this is a pure data serialization step.
/// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes, CORRECTED): VERIFIED.
/// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
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
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count @ 0x04: VERIFIED.
        // spec: Docs/RE/formats/effects.md §A.2 — type_flag @ 0x08: SAMPLE-VERIFIED.
        return new XeffJsonRoot(
            EffectId: effect.EffectId,
            SubEffectCount: effect.SubEffectCount,
            TypeFlag: effect.TypeFlag,
            FirstEntryCount: effect.FirstEntryCount,
            SubEffects: MapSubEffects(effect.SubEffects));
    }

    private static XeffJsonSubEffect[] MapSubEffects(XeffSubEffect[] subEffects)
    {
        var result = new XeffJsonSubEffect[subEffects.Length];
        for (int i = 0; i < subEffects.Length; i++)
            result[i] = MapSubEffect(subEffects[i]);
        return result;
    }

    private static XeffJsonSubEffect MapSubEffect(XeffSubEffect sub)
    {
        // spec: Docs/RE/formats/effects.md §A.4.1 Name table: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.4.2 Curve section (alpha, scaleX/Y/Z): CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (anim_loop, unknown_constant, anim_stride, anim_base_time): CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array (frame-0 no-index special case): CONFIRMED.
        return new XeffJsonSubEffect(
            EntryCount: sub.EntryCount,
            TextureNames: sub.TextureNames,
            AlphaKeys: MapAlphaKeys(sub.AlphaKeys),
            ScaleX: sub.ScaleX,
            ScaleY: sub.ScaleY,
            ScaleZ: sub.ScaleZ,
            AnimLoop: sub.AnimLoop,
            // unknown_constant — value emitted raw; purpose UNRESOLVED.
            // spec: Docs/RE/formats/effects.md §A.4.3 — unknown_constant u32 @ +1: SAMPLE-VERIFIED (value), semantics UNRESOLVED.
            UnknownConstant: sub.UnknownConstant,
            AnimStride: sub.AnimStride,
            AnimBaseTime: sub.AnimBaseTime,
            Keyframes: MapKeyframes(sub.Keyframes));
    }

    private static XeffJsonKeyframe[] MapKeyframes(XeffKeyframe[] kfs)
    {
        var result = new XeffJsonKeyframe[kfs.Length];
        for (int i = 0; i < kfs.Length; i++)
        {
            XeffKeyframe kf = kfs[i];
            // spec: Docs/RE/formats/effects.md §A.4.4 — kf_index, velocity Vec3, size Vec3, rotation degrees: CONFIRMED.
            // spec: Docs/RE/formats/effects.md §A.8 Resolved semantics: HIGH.
            // spec: Docs/RE/formats/effects.md §A.7 Rotation Encoding Note: CONFIRMED.
            Vec3 vel = kf.Velocity;
            Vec3 sz = kf.Size;
            Quat rot = kf.Rotation;
            result[i] = new XeffJsonKeyframe(
                KfIndex: kf.KfIndex,
                Velocity: [vel.X, vel.Y, vel.Z],
                Size: [sz.X, sz.Y, sz.Z],
                Rotation: [rot.X, rot.Y, rot.Z, rot.W],
                RotXDeg: kf.RotXDeg,
                RotYDeg: kf.RotYDeg,
                RotZDeg: kf.RotZDeg);
        }

        return result;
    }

    /// <summary>
    /// Converts per-curve alpha values (stored inverted in the file: 0.0=opaque, 1.0=transparent)
    /// to explicit opacity/file-value pairs for consumer clarity.
    /// spec: Docs/RE/formats/effects.md §A.4.2 Pass 1 alpha — stored as 1.0−opacity: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.6 Alpha Inversion Convention: CONFIRMED.
    /// Note: the parser emits the raw file value (not pre-inverted). Inversion is a mapping concern.
    /// </summary>
    private static XeffJsonAlpha[] MapAlphaKeys(float[] keys)
    {
        var result = new XeffJsonAlpha[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            // File value is stored inverted: file=0.0 → opaque, file=1.0 → transparent.
            // spec: Docs/RE/formats/effects.md §A.6 — "file value 0.0 means fully opaque; 1.0 means fully transparent": CONFIRMED.
            float fileValue = keys[i];
            result[i] = new XeffJsonAlpha(FileValue: fileValue, Opacity: 1f - fileValue);
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // JSON data-transfer objects (internal, immutable records)
    // -------------------------------------------------------------------------

    // Root object
    private sealed record XeffJsonRoot(
        uint EffectId,
        uint SubEffectCount,
        uint TypeFlag,
        uint FirstEntryCount,
        XeffJsonSubEffect[] SubEffects);

    // One sub-effect block
    private sealed record XeffJsonSubEffect(
        uint EntryCount,
        string[] TextureNames,
        XeffJsonAlpha[] AlphaKeys,
        float[] ScaleX,
        float[] ScaleY,
        float[] ScaleZ,
        byte AnimLoop,
        /// <summary>Constant u32 at track header +1. Observed: 67. Purpose UNRESOLVED — emitted raw.
        /// spec: Docs/RE/formats/effects.md §A.4.3 — unknown_constant: SAMPLE-VERIFIED value, semantics UNRESOLVED.</summary>
        uint UnknownConstant,
        uint AnimStride,
        uint AnimBaseTime,
        XeffJsonKeyframe[] Keyframes);

    /// <summary>
    /// One alpha key entry. Emits both the raw file value and the derived opacity.
    /// spec: Docs/RE/formats/effects.md §A.6 Alpha Inversion Convention — file 0.0=opaque, 1.0=transparent: CONFIRMED.
    /// </summary>
    private sealed record XeffJsonAlpha(float FileValue, float Opacity);

    // One keyframe — velocity/size/rotation are named.
    // spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.8 velocity Vec3 + size Vec3: HIGH.
    // spec: Docs/RE/formats/effects.md §A.7 rotation quaternion (Euler degrees → quat): CONFIRMED.
    private sealed record XeffJsonKeyframe(
        uint KfIndex,
        /// <summary>Emission velocity Vec3 [X,Y,Z]. spec: §A.8: HIGH.</summary>
        float[] Velocity,
        /// <summary>Billboard/particle size Vec3 [X,Y,Z]. spec: §A.8: HIGH.</summary>
        float[] Size,
        /// <summary>Rotation quaternion [X,Y,Z,W] derived from Euler degrees. spec: §A.7: CONFIRMED.</summary>
        float[] Rotation,
        float RotXDeg,
        float RotYDeg,
        float RotZDeg);
}