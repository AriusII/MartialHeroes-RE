namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .xeff  Particle Effect Descriptor
//  spec: Docs/RE/formats/effects.md §Section A
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One sub-effect block within a <c>.xeff</c> particle effect descriptor.
/// Each block contains a name table, four float-curve passes, a track header,
/// and a keyframe array.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED by sample byte-walkthrough.
/// The file header (§A.2) is 32 bytes. After the header, <c>sub_effect_count</c> sub-effect blocks follow.
/// Block 0 has NO prefix — its entry_count comes from the file header's first_entry_count field.
/// Blocks 1..N-1 each begin with a 24-byte prefix: u32 sub_id + u32[4] zeros + u32 entry_count.
/// spec: Docs/RE/formats/effects.md §A.4 §A.15 — block[0] prefix-free, blocks[1..N-1] 24-byte prefix: CONFIRMED.
/// </remarks>
public sealed class XeffSubEffect
{
    // ─── Name table ─────────────────────────────────────────────────────────
    /// <summary>
    /// Sub-effect identifier present in the 24-byte prefix of blocks 1..N-1 (u32 at prefix offset +0).
    /// Always 0 for block 0 (no prefix).
    /// spec: Docs/RE/formats/effects.md §A.4 — sub_id u32 @ prefix+0 (blocks 1..N-1 only): CONFIRMED.
    /// </summary>
    public required uint SubId { get; init; }

    /// <summary>
    /// Number of entries in this sub-effect (drives name table, curve passes, and keyframe array).
    /// For block 0, sourced from the file header's first_entry_count (§A.2). For blocks 1..N-1, read
    /// from the 24-byte block prefix at offset +20.
    /// spec: Docs/RE/formats/effects.md §A.4 — entry_count in header for block[0]; prefix+20 for blocks 1..N-1: CONFIRMED.
    /// Observed range: 1–41.
    /// </summary>
    public required uint EntryCount { get; init; }

    /// <summary>
    /// Texture base names (null-padded ASCII/CP949, 64 bytes each on disk).
    /// Full VFS path: <c>data/effect/texture/&lt;name&gt;.tga</c>.
    /// Length == EntryCount.
    /// spec: Docs/RE/formats/effects.md §A.4.1 Name table — entry_count × 64 bytes: CONFIRMED.
    /// </summary>
    public required string[] TextureNames { get; init; }

    // ─── Curve section (4 passes) ────────────────────────────────────────────
    /// <summary>
    /// Alpha channel keyframe values. Stored inverted: file 0.0 = opaque, file 1.0 = transparent.
    /// spec: Docs/RE/formats/effects.md §A.4.2 Curve pass 1 (alpha) — stored as 1.0−opacity: CONFIRMED.
    /// </summary>
    public required float[] AlphaKeys { get; init; }

    /// <summary>
    /// Scale X channel keyframe values.
    /// spec: Docs/RE/formats/effects.md §A.4.2 Curve pass 2 (scale X): CONFIRMED.
    /// </summary>
    public required float[] ScaleX { get; init; }

    /// <summary>
    /// Scale Y channel keyframe values.
    /// spec: Docs/RE/formats/effects.md §A.4.2 Curve pass 3 (scale Y): CONFIRMED.
    /// </summary>
    public required float[] ScaleY { get; init; }

    /// <summary>
    /// Scale Z channel keyframe values.
    /// spec: Docs/RE/formats/effects.md §A.4.2 Curve pass 4 (scale Z): CONFIRMED.
    /// </summary>
    public required float[] ScaleZ { get; init; }

    // ─── Track header (13 bytes) ─────────────────────────────────────────────
    /// <summary>
    /// Non-zero enables animated (multi-keyframe) path.
    /// spec: Docs/RE/formats/effects.md §A.4.3 Track header — anim_loop u8 @ +0: CONFIRMED.
    /// </summary>
    public required byte AnimLoop { get; init; }

    /// <summary>
    /// Constant u32 at track header offset +1. Observed value: 67 (0x43) in all samples.
    /// Purpose UNRESOLVED — do not branch on this value.
    /// spec: Docs/RE/formats/effects.md §A.4.3 Track header — unknown_constant u32 @ +1: SAMPLE-VERIFIED (value), semantics UNRESOLVED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_UNKNOWN_CONSTANT = 67.
    /// </summary>
    public required uint UnknownConstant { get; init; }

    /// <summary>
    /// Duration of one animation frame in milliseconds.
    /// spec: Docs/RE/formats/effects.md §A.4.3 Track header — anim_stride u32 @ +5 (ms): CONFIRMED.
    /// </summary>
    public required uint AnimStride { get; init; }

    /// <summary>
    /// Base time offset in milliseconds. Observed value: 0 in all samples.
    /// spec: Docs/RE/formats/effects.md §A.4.3 Track header — anim_base_time u32 @ +9 (ms): CONFIRMED.
    /// </summary>
    public required uint AnimBaseTime { get; init; }

    // ─── Keyframe array ──────────────────────────────────────────────────────
    /// <summary>
    /// Keyframe array (length == EntryCount).
    /// Frame 0 has no index prefix on disk; frames 1..N-1 each have a u32 kf_index prefix.
    /// spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array — frame 0 special case (no index): CONFIRMED.
    /// </summary>
    public required XeffKeyframe[] Keyframes { get; init; }
}

/// <summary>
/// One keyframe within a <c>.xeff</c> sub-effect block.
/// Frame 0 carries no index prefix on disk (9 × f32 = 36 bytes).
/// Frames 1..N-1 each carry a u32 kf_index prefix (u32 + 9 × f32 = 40 bytes).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array: CONFIRMED.
/// The 9-float layout: velocity Vec3 (1–3) + size Vec3 (4–6) + rotation degrees Euler-XYZ (7–9).
/// spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.8 Resolved semantics: HIGH.
/// </remarks>
public sealed class XeffKeyframe
{
    /// <summary>
    /// Frame index (0-based). Zero for frame 0 (no prefix on disk).
    /// spec: Docs/RE/formats/effects.md §A.4.4 — kf_index u32 (frames 1..N-1 only): CONFIRMED.
    /// </summary>
    public required uint KfIndex { get; init; }

    /// <summary>
    /// Emission velocity / displacement X (position 1 in 9-float layout).
    /// At render time: rotated by instance world orientation, scaled, added to world origin.
    /// spec: Docs/RE/formats/effects.md §A.4.4 velocity_x @ position 1: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.8 velocity Vec3 semantics: HIGH.
    /// </summary>
    public required float VelocityX { get; init; }

    /// <summary>Emission velocity Y. spec: §A.4.4 velocity_y @ position 2: CONFIRMED.</summary>
    public required float VelocityY { get; init; }

    /// <summary>Emission velocity Z. spec: §A.4.4 velocity_z @ position 3: CONFIRMED.</summary>
    public required float VelocityZ { get; init; }

    /// <summary>
    /// Billboard / particle size X (position 4 in 9-float layout).
    /// For billboard types: half-extent X. For mesh type: scales mesh X axis.
    /// spec: Docs/RE/formats/effects.md §A.4.4 size_x @ position 4: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.8 size Vec3 semantics: HIGH.
    /// </summary>
    public required float SizeX { get; init; }

    /// <summary>Billboard/particle size Y. spec: §A.4.4 size_y @ position 5: CONFIRMED.</summary>
    public required float SizeY { get; init; }

    /// <summary>Billboard/particle size Z. spec: §A.4.4 size_z @ position 6: CONFIRMED.</summary>
    public required float SizeZ { get; init; }

    /// <summary>
    /// Euler X rotation in degrees (position 7). Converted to quaternion at load via π/180 half-angle XYZ.
    /// spec: Docs/RE/formats/effects.md §A.4.4 kf_rot_x_deg @ position 7 (degrees): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.7 Rotation Encoding Note — π/180, half-angle Euler-XYZ: CONFIRMED.
    /// </summary>
    public required float RotXDeg { get; init; }

    /// <summary>Euler Y rotation degrees (position 8). spec: §A.4.4 kf_rot_y_deg @ position 8: CONFIRMED.</summary>
    public required float RotYDeg { get; init; }

    /// <summary>Euler Z rotation degrees (position 9). spec: §A.4.4 kf_rot_z_deg @ position 9: CONFIRMED.</summary>
    public required float RotZDeg { get; init; }

    /// <summary>
    /// Velocity as a Vec3 convenience accessor.
    /// spec: Docs/RE/formats/effects.md §A.8 velocity Vec3 (velocity_x/y/z): HIGH.
    /// </summary>
    public Vec3 Velocity => new(VelocityX, VelocityY, VelocityZ);

    /// <summary>
    /// Size as a Vec3 convenience accessor.
    /// spec: Docs/RE/formats/effects.md §A.8 size Vec3 (size_x/y/z): HIGH.
    /// </summary>
    public Vec3 Size => new(SizeX, SizeY, SizeZ);

    /// <summary>
    /// Rotation as a quaternion derived from on-disk Euler degrees (XYZ half-angle decomposition).
    /// Identity (0,0,0,1) before conversion (init value per spec).
    /// spec: Docs/RE/formats/effects.md §A.7 — multiply by π/180; half-angle Euler-XYZ: CONFIRMED.
    /// </summary>
    public Quat Rotation => ComputeQuat(RotXDeg, RotYDeg, RotZDeg);

    /// <summary>
    /// Shared Euler-degrees → quaternion conversion. Half-angle Euler-XYZ decomposition.
    /// Q = Qx * Qy * Qz
    /// spec: Docs/RE/formats/effects.md §A.7 — "π/180 × degrees; half-angle Euler-XYZ decomposition": CONFIRMED.
    /// </summary>
    internal static Quat ComputeQuat(float xDeg, float yDeg, float zDeg)
    {
        const float DegToRad = MathF.PI / 180f; // spec: §A.7 — multiply by π/180: CONFIRMED
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
/// Decoded result of a <c>.xeff</c> particle effect descriptor file.
/// </summary>
/// <remarks>
/// File header is 32 bytes (0x20). Anti-magic: effect_id must NOT equal 0x46464558.
/// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes, CORRECTED): VERIFIED.
/// spec: Docs/RE/formats/effects.md §A.1 Anti-magic 0x46464558: CONFIRMED.
/// </remarks>
public sealed class XeffData
{
    /// <summary>
    /// Numeric effect identifier. Must not equal 0x46464558 (ASCII "XEFF" LE).
    /// For numeric-named files the value matches the decimal filename.
    /// spec: Docs/RE/formats/effects.md §A.2 — effect_id u32 @ 0x00: VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.1 — Anti-magic 0x46464558: CONFIRMED.
    /// </summary>
    public required uint EffectId { get; init; }

    /// <summary>
    /// Number of sub-effect blocks (was labelled element_count in prior spec revision).
    /// Zero is valid (stub/empty effect). Observed range: 0–16.
    /// spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count u32 @ 0x04: VERIFIED.
    /// </summary>
    public required uint SubEffectCount { get; init; }

    /// <summary>
    /// Type flag. Observed values: 1 and 2. Semantics UNRESOLVED.
    /// spec: Docs/RE/formats/effects.md §A.2 — type_flag u32 @ 0x08: SAMPLE-VERIFIED (values 1/2), semantics UNRESOLVED.
    /// </summary>
    public required uint TypeFlag { get; init; }

    /// <summary>
    /// 16 reserved bytes at header offset 0x0C. Zero in all samples.
    /// spec: Docs/RE/formats/effects.md §A.2 — reserved u8[16] @ 0x0C: SAMPLE-VERIFIED.
    /// </summary>
    public required byte[] Reserved { get; init; } // length 16

    /// <summary>
    /// Entry count for the first sub-effect block (stored in header at offset 0x1C).
    /// Block 0 has NO entry_count prefix on disk — this header field is the sole source of block 0's count.
    /// spec: Docs/RE/formats/effects.md §A.2 — first_entry_count u32 @ 0x1C: SAMPLE-VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free; first_entry_count NOT duplicated at block start: CONFIRMED.
    /// </summary>
    public required uint FirstEntryCount { get; init; }

    /// <summary>
    /// Sub-effect blocks decoded from the file body.
    /// Length == SubEffectCount.
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
    /// </summary>
    public required XeffSubEffect[] SubEffects { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  .eff Effect-Object Shape (data/effect/obj/)
//  spec: Docs/RE/formats/effects.md §Section B
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
/// Dispatch by directory: data/effect/obj/*.eff ONLY — not sound nor particle paths.
/// spec: Docs/RE/formats/effects.md §Disambiguation.
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