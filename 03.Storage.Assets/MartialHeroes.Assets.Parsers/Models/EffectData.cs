namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .xeff  Particle Effect Descriptor
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One element record within a <c>.xeff</c> particle effect descriptor.
/// Element stride: 104 bytes in memory (derived from allocation formula).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.3 Element Array — stride 104 bytes: PARSER-CONFIRMED.
/// All element body fields are PARSER-CONFIRMED or SAMPLE-UNVERIFIED.
/// </remarks>
public sealed class XeffElement
{
    // ─── Group A — Emitter identity (20 bytes) ──────────────────────────────
    /// <summary>emitter_type u32 @ element+0. Known: 2=directional. spec: §A.3.1 PARSER-CONFIRMED.</summary>
    public required uint EmitterType { get; init; }

    /// <summary>emitter_subtype u32 @ element+4. Semantics UNRESOLVED. spec: §A.3.1 PARSER-CONFIRMED.</summary>
    public required uint EmitterSubtype { get; init; }

    /// <summary>anim_flag u32 @ element+8. Non-zero enables animated path. spec: §A.3.1 PARSER-CONFIRMED.</summary>
    public required uint AnimFlag { get; init; }

    /// <summary>tex_count u32 @ element+12. Drives texture sub-array and keyframe count. spec: §A.3.1 PARSER-CONFIRMED.</summary>
    public required uint TexCount { get; init; }

    /// <summary>field_unknown_a u32 @ element+16. Purpose UNRESOLVED. spec: §A.3.1 PARSER-CONFIRMED.</summary>
    public required uint FieldUnknownA { get; init; }

    // ─── Group B — Texture sub-array ────────────────────────────────────────
    /// <summary>
    /// Texture base names (null-padded ASCII, 64 bytes each).
    /// Full path: data/effect/texture/&lt;name&gt;.tga.
    /// spec: §A.3.2 — tex_count × 64 bytes, PARSER-CONFIRMED.
    /// </summary>
    public required string[] TextureNames { get; init; }

    // ─── Group C — Alpha keyframes ──────────────────────────────────────────
    /// <summary>
    /// Alpha keyframe values (stored inverted: 0.0 = opaque, 1.0 = transparent).
    /// spec: §A.3.3 — alpha_key_count × f32, PARSER-CONFIRMED.
    /// </summary>
    public required float[] AlphaKeyframes { get; init; }

    // ─── Group D — Scale channels ───────────────────────────────────────────
    /// <summary>X-scale keyframe values. spec: §A.3.4 PARSER-CONFIRMED.</summary>
    public required float[] ScaleX { get; init; }

    /// <summary>Y-scale keyframe values. spec: §A.3.4 PARSER-CONFIRMED.</summary>
    public required float[] ScaleY { get; init; }

    /// <summary>Z-scale keyframe values. spec: §A.3.4 PARSER-CONFIRMED.</summary>
    public required float[] ScaleZ { get; init; }

    // ─── Group E — Animation timing (9 bytes) ──────────────────────────────
    /// <summary>anim_loop u8 (single byte). Non-zero selects animated keyframe branch. spec: §A.3.5 CONFIRMED.</summary>
    public required byte AnimLoop { get; init; }

    /// <summary>anim_stride u32. Per-frame duration; unit UNRESOLVED. spec: §A.3.5 PARSER-CONFIRMED.</summary>
    public required uint AnimStride { get; init; }

    /// <summary>anim_base_time u32. Base time offset. spec: §A.3.5 PARSER-CONFIRMED.</summary>
    public required uint AnimBaseTime { get; init; }

    // ─── Group F — Keyframe / static-state block ─────────────────────────────
    /// <summary>
    /// Animated keyframes (Branch A, when anim_loop != 0).
    /// Each record: kf_index u32 + 6 × f32 params + 3 × f32 rotation degrees = 40 bytes.
    /// spec: §A.3.6 Branch A: PARSER-CONFIRMED, SAMPLE-UNVERIFIED.
    /// </summary>
    public required XeffKeyframe[]? AnimKeyframes { get; init; }

    /// <summary>
    /// Static state (Branch B, when anim_loop == 0).
    /// 6 params + (3 rotation floats if emitter_type == 2).
    /// spec: §A.3.6 Branch B: PARSER-CONFIRMED, SAMPLE-UNVERIFIED.
    /// </summary>
    public required XeffStaticState? StaticState { get; init; }
}

/// <summary>
/// One animated keyframe within a <c>.xeff</c> element (Branch A).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.3.6 Branch A: PARSER-CONFIRMED, SAMPLE-UNVERIFIED.
/// 10 fields × 4 bytes = 40 bytes on disk.
/// spec: Docs/RE/formats/effects.md §A.3.7 Resolved semantics of the six float parameters: HIGH.
///   Params[0..2] = velocity Vec3 (emission displacement). spec: §A.3.6 kf_velocity_x @ +0x04, y @ +0x08, z @ +0x0C.
///   Params[3..5] = size Vec3 (billboard/particle dimensions). spec: §A.3.6 kf_size_x @ +0x10, y @ +0x14, z @ +0x18.
/// Rotation (Euler degrees) stored on disk; quaternion conversion is a load-time concern (see §A.4).
/// </remarks>
public sealed class XeffKeyframe
{
    /// <summary>kf_index u32 @ +0x00. 0-based frame slot. spec: §A.3.6 PARSER-CONFIRMED.</summary>
    public required uint KfIndex { get; init; }

    /// <summary>
    /// Raw 6-float parameter array [velocity_x, velocity_y, velocity_z, size_x, size_y, size_z].
    /// Kept for backward compatibility; prefer <see cref="Velocity"/> and <see cref="Size"/>.
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch A reads 2–7 (6 × f32): PARSER-CONFIRMED.
    /// </summary>
    public required float[] Params { get; init; } // length 6

    /// <summary>
    /// Emission velocity / displacement Vec3 (Params[0..2]).
    /// At render time: rotated by the instance's world orientation, scaled, added to world origin.
    /// spec: Docs/RE/formats/effects.md §A.3.7 velocity Vec3 (kf_velocity_x/y/z @ +0x04..+0x0F): HIGH.
    /// </summary>
    public Vec3 Velocity => new(Params[0], Params[1], Params[2]);

    /// <summary>
    /// Billboard / particle size Vec3 (Params[3..5]).
    /// Type 0/2: size_x/size_y as half-extents. Type 1 (mesh): scales each mesh axis.
    /// spec: Docs/RE/formats/effects.md §A.3.7 size Vec3 (kf_size_x/y/z @ +0x10..+0x1B): HIGH.
    /// </summary>
    public Vec3 Size => new(Params[3], Params[4], Params[5]);

    /// <summary>
    /// Rotation in Euler degrees (X, Y, Z) stored on disk — kf_rot_x_deg @ +0x1C (file read 8).
    /// spec: Docs/RE/formats/effects.md §A.3.6 kf_rot_x_deg/y/z in degrees: CONFIRMED.
    /// </summary>
    public required float RotXDeg { get; init; }

    /// <summary>Euler Y rotation degrees (file read 9). spec: §A.3.6 CONFIRMED.</summary>
    public required float RotYDeg { get; init; }

    /// <summary>Euler Z rotation degrees (file read 10). spec: §A.3.6 CONFIRMED.</summary>
    public required float RotZDeg { get; init; }

    /// <summary>
    /// Rotation as a quaternion derived from the on-disk Euler degrees (XYZ half-angle conversion).
    /// Conversion: multiply each degree by π/180, then apply standard half-angle Euler-XYZ decomposition.
    /// Constructor-initialized to identity (0,0,0,1) before conversion (see §A.4).
    /// spec: Docs/RE/formats/effects.md §A.4 Rotation Encoding Note: CONFIRMED.
    /// </summary>
    public Quat Rotation => EulerDegreesToQuat(RotXDeg, RotYDeg, RotZDeg);

    // Internal Euler→Quat conversion matching §A.4 half-angle Euler-XYZ decomposition.
    // spec: Docs/RE/formats/effects.md §A.4 — "π/180 × degrees; half-angle Euler-XYZ": CONFIRMED.
    private static Quat EulerDegreesToQuat(float xDeg, float yDeg, float zDeg)
        => ComputeQuat(xDeg, yDeg, zDeg);

    /// <summary>
    /// Shared Euler-degrees → quaternion helper used by both <see cref="XeffKeyframe"/> and
    /// <see cref="XeffStaticState"/>.
    /// Implements the half-angle Euler-XYZ decomposition described in §A.4.
    /// spec: Docs/RE/formats/effects.md §A.4 — "π/180 × degrees; half-angle Euler-XYZ": CONFIRMED.
    /// </summary>
    internal static Quat ComputeQuat(float xDeg, float yDeg, float zDeg)
    {
        const float DegToRad = MathF.PI / 180f; // spec: §A.4 — multiply by π/180: CONFIRMED
        float cx = MathF.Cos(xDeg * DegToRad * 0.5f);
        float sx = MathF.Sin(xDeg * DegToRad * 0.5f);
        float cy = MathF.Cos(yDeg * DegToRad * 0.5f);
        float sy = MathF.Sin(yDeg * DegToRad * 0.5f);
        float cz = MathF.Cos(zDeg * DegToRad * 0.5f);
        float sz = MathF.Sin(zDeg * DegToRad * 0.5f);
        // XYZ Euler → Quat: Q = Qx * Qy * Qz
        return new Quat(
            X: sx * cy * cz + cx * sy * sz,
            Y: cx * sy * cz - sx * cy * sz,
            Z: cx * cy * sz + sx * sy * cz,
            W: cx * cy * cz - sx * sy * sz
        );
    }
}

/// <summary>
/// Static emitter state within a <c>.xeff</c> element (Branch B, when <c>anim_loop == 0</c>).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.3.6 Branch B: PARSER-CONFIRMED, SAMPLE-UNVERIFIED.
/// 6 floats always; 3 rotation floats only when emitter_type == 2 (directional billboard).
/// spec: Docs/RE/formats/effects.md §A.3.7 Resolved semantics of the six float parameters: HIGH.
///   Params[0..2] = static_velocity Vec3. Params[3..5] = static_size Vec3.
/// </remarks>
public sealed class XeffStaticState
{
    /// <summary>
    /// Raw 6-float parameter array [velocity_x, velocity_y, velocity_z, size_x, size_y, size_z].
    /// Kept for backward compatibility; prefer <see cref="Velocity"/> and <see cref="Size"/>.
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch B reads 1–6 (6 × f32): PARSER-CONFIRMED.
    /// </summary>
    public required float[] Params { get; init; } // length 6

    /// <summary>
    /// Static emission velocity / displacement Vec3 (Params[0..2]).
    /// spec: Docs/RE/formats/effects.md §A.3.7 static_velocity Vec3 (reads 1–3): HIGH.
    /// </summary>
    public Vec3 Velocity => new(Params[0], Params[1], Params[2]);

    /// <summary>
    /// Static billboard / particle size Vec3 (Params[3..5]).
    /// spec: Docs/RE/formats/effects.md §A.3.7 static_size Vec3 (reads 4–6): HIGH.
    /// </summary>
    public Vec3 Size => new(Params[3], Params[4], Params[5]);

    /// <summary>
    /// Euler X rotation degrees — only present when emitter_type == 2 (directional billboard).
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch B — static_rot_x_deg (read 7, emitter_type=2 only): CONFIRMED.
    /// </summary>
    public required float? RotXDeg { get; init; }

    /// <summary>
    /// Euler Y rotation degrees — only when emitter_type == 2.
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch B — static_rot_y_deg (read 8): CONFIRMED.
    /// </summary>
    public required float? RotYDeg { get; init; }

    /// <summary>
    /// Euler Z rotation degrees — only when emitter_type == 2.
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch B — static_rot_z_deg (read 9): CONFIRMED.
    /// </summary>
    public required float? RotZDeg { get; init; }

    /// <summary>
    /// Rotation quaternion derived from on-disk Euler degrees (when emitter_type == 2).
    /// Identity (0,0,0,1) when no rotation floats are present (emitter_type != 2).
    /// spec: Docs/RE/formats/effects.md §A.4 Rotation Encoding Note: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.3.6 Branch B in-memory layout +0x1C..+0x2B rotation_quat: PARSER-CONFIRMED.
    /// </summary>
    public Quat Rotation
    {
        get
        {
            if (RotXDeg is null)
                return
                    new Quat(0f, 0f, 0f, 1f); // identity — spec: §A.3.6 "initialized to identity (0,0,0,1)": CONFIRMED
            return XeffKeyframe.ComputeQuat(RotXDeg.Value, RotYDeg!.Value, RotZDeg!.Value);
        }
    }
}

/// <summary>
/// Decoded result of a <c>.xeff</c> particle effect descriptor file.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.2 File Header: VERIFIED (3 real samples).
/// Header: effect_id u32 @ 0x00, element_count u32 @ 0x04.
/// Anti-magic: effect_id must NOT equal 0x46464558.
/// spec: Docs/RE/formats/effects.md §A.1 — "effect_id == 0x46464558 → file invalid": CONFIRMED.
/// </remarks>
public sealed class XeffData
{
    /// <summary>
    /// Numeric effect identifier.
    /// Must not equal 0x46464558 (ASCII "XEFF" LE) — that value is the invalid-file sentinel.
    /// spec: Docs/RE/formats/effects.md §A.2 — effect_id @ 0x00: VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.1 — Anti-magic 0x46464558: CONFIRMED.
    /// </summary>
    public required uint EffectId { get; init; }

    /// <summary>
    /// Elements decoded from the element array.
    /// spec: Docs/RE/formats/effects.md §A.2 — element_count @ 0x04: VERIFIED.
    /// </summary>
    public required XeffElement[] Elements { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  .eff Effect-Object Shape (data/effect/obj/)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One vertex in an <c>.eff</c> effect-object shape mesh (32 bytes on disk).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §B.4.1 VertexRecord (32 bytes): VERIFIED.
/// pos_x/y/z @ +0/+4/+8: VERIFIED. normal_x/y/z @ +12/+16/+20: VERIFIED.
/// tex_u @ +24, tex_v @ +28: VERIFIED.
/// Note: tringle.eff has non-unit normals (exporter bug). Parser tolerates this.
/// spec: Docs/RE/formats/effects.md §B.5 Normal Encoding Note.
/// </remarks>
public readonly record struct EffVertex(
    float PosX,
    float PosY,
    float PosZ,
    float NormalX,
    float NormalY,
    float NormalZ,
    float TexU,
    float TexV);

/// <summary>
/// Decoded result of an <c>.eff</c> effect-object shape file from <c>data/effect/obj/</c>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §B.2 File Layout Overview: VERIFIED.
/// File-size formula: 4 + index_count × 2 + 4 + vert_count × 32.
/// index_count always divisible by 3 (pure triangle list).
/// </remarks>
public sealed class EffObjectShape
{
    /// <summary>
    /// Index buffer (u16, 0-based). Always divisible by 3 — pure triangle list.
    /// spec: Docs/RE/formats/effects.md §B.3 Index Section — index_count u32 @ 0x00: VERIFIED.
    /// </summary>
    public required ushort[] Indices { get; init; }

    /// <summary>
    /// Vertex buffer. Each vertex is 32 bytes on disk.
    /// spec: Docs/RE/formats/effects.md §B.4 Vertex Section — vert_count u32: VERIFIED.
    /// </summary>
    public required EffVertex[] Vertices { get; init; }
}