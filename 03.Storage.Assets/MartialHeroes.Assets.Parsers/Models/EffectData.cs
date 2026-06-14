namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  .xeff  Particle Effect Descriptor
//  spec: Docs/RE/formats/effects.md §Section A
//
//  HEADER CORRECTED 2026-06-14:
//  The file header is exactly 8 bytes (effect_id u32 + sub_effect_count u32).
//  The former type_flag/reserved/first_entry_count fields are NOT header fields;
//  they are the leading bytes of sub-effect block 0's element fixed head (A.4.0).
//  spec: Docs/RE/formats/effects.md §A.2 XEFF_HEADER_SIZE = 8 (0x08): VERIFIED.
//  spec: Docs/RE/formats/effects.md §A.17 Correction history.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One sub-effect block within a <c>.xeff</c> particle effect descriptor.
/// Every block — including block 0 — is parsed by the same element read sequence:
/// a 24-byte fixed head (A.4.0) followed by a variable body (name table, curve section,
/// track header, keyframe array).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED by sample byte-walkthrough.
/// spec: Docs/RE/formats/effects.md §A.4.5 — every block (block 0 included) uses the same element read sequence: CONFIRMED.
/// Block 0 starts at file offset 0x08 (immediately after the 8-byte file header); no prefix.
/// Blocks 1..N-1 follow sequentially, parsed by the same element read sequence.
/// spec: Docs/RE/formats/effects.md §A.14 XEFF_ELEMENT_FIXED_HEAD = 24 (0x18): per-element on-disk fixed head.
/// </remarks>
public sealed class XeffSubEffect
{
    // ─── Element fixed head (24 bytes — A.4.0) ──────────────────────────────

    /// <summary>
    /// Emitter type for this sub-effect element.
    /// 0 = billboard, 1 = mesh-particle, 2 = directional billboard.
    /// For block 0 this is the byte at file offset 0x08 (formerly mislabelled as header type_flag).
    /// spec: Docs/RE/formats/effects.md §A.4.0 — emitter_type u32 @ element+0x00: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.12 emitter_type enum: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_EMITTER_BILLBOARD=0, XEFF_EMITTER_MESH=1, XEFF_EMITTER_DIRECTIONAL=2.
    /// </summary>
    public required uint EmitterType { get; init; }

    /// <summary>
    /// Resource selector: &lt; 10000 = shared mesh index; ≥ 10000 = GPU particle id.
    /// spec: Docs/RE/formats/effects.md §A.4.0 — resource_id u32 @ element+0x04: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_RESOURCE_PARTICLE_THRESHOLD = 10000.
    /// </summary>
    public required uint ResourceId { get; init; }

    /// <summary>
    /// Animation flag (boolean — non-zero is true).
    /// spec: Docs/RE/formats/effects.md §A.4.0 — anim_flag u32 @ element+0x08: CONFIRMED.
    /// </summary>
    public required uint AnimFlag { get; init; }

    /// <summary>
    /// Element flags. Semantics UNRESOLVED; no read-site found.
    /// spec: Docs/RE/formats/effects.md §A.4.0 — field_unknown_a u32 @ element+0x0C: UNRESOLVED.
    /// </summary>
    public required uint FieldUnknownA { get; init; }

    /// <summary>
    /// In-memory dword written ahead of tex_count (reverse of file order). Role beyond that ordering UNRESOLVED.
    /// spec: Docs/RE/formats/effects.md §A.4.0 — element_dword2 u32 @ element+0x10: UNRESOLVED.
    /// </summary>
    public required uint ElementDword2 { get; init; }

    // ─── Name table ─────────────────────────────────────────────────────────

    /// <summary>
    /// Number of entries in this sub-effect (tex_count). Drives the name table, curve passes,
    /// and keyframe array. For block 0 this is the value at file offset 0x1C (formerly mislabelled
    /// as header first_entry_count). Observed range: 1–41.
    /// spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.2 — "the value at file offset 0x1C is block 0's own tex_count": CONFIRMED.
    /// </summary>
    public required uint EntryCount { get; init; }

    /// <summary>
    /// Texture base names (null-padded ASCII/CP949, 64 bytes each on disk).
    /// Full VFS path: <c>data/effect/texture/&lt;name&gt;.tga</c>.
    /// Length == EntryCount.
    /// spec: Docs/RE/formats/effects.md §A.4.1 Name table — tex_count × 64 bytes: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_TEX_NAME_LEN = 64 (0x40).
    /// </summary>
    public required string[] TextureNames { get; init; }

    // ─── Curve section (4 passes) ────────────────────────────────────────────

    /// <summary>
    /// Alpha channel keyframe values. Stored inverted: file 0.0 = opaque, file 1.0 = transparent.
    /// spec: Docs/RE/formats/effects.md §A.4.2 Curve pass 1 (alpha) — stored as 1.0−opacity: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.6 Alpha Inversion Convention: CONFIRMED.
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
    /// spec: Docs/RE/formats/effects.md §A.4.3 — unknown_constant u32 @ +1: SAMPLE-VERIFIED (value), semantics UNRESOLVED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_UNKNOWN_CONSTANT = 67.
    /// </summary>
    public required uint UnknownConstant { get; init; }

    /// <summary>
    /// Duration of one animation frame in milliseconds.
    /// spec: Docs/RE/formats/effects.md §A.4.3 Track header — anim_stride u32 @ +5 (ms): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_TIME_UNIT = milliseconds.
    /// </summary>
    public required uint AnimStride { get; init; }

    /// <summary>
    /// Base time offset in milliseconds. Observed value: 0 in all samples.
    /// spec: Docs/RE/formats/effects.md §A.4.3 Track header — anim_base_time u32 @ +9 (ms): CONFIRMED.
    /// </summary>
    public required uint AnimBaseTime { get; init; }

    // ─── Keyframe array ──────────────────────────────────────────────────────

    /// <summary>
    /// Keyframe array (length == EntryCount in the animated path; length == 1 in the static path).
    /// Animated path (anim_loop != 0): Frame 0 has no index prefix on disk; frames 1..N-1 each have a u32 kf_index prefix.
    /// Static path (anim_loop == 0): exactly one entry; size depends on emitter_type (see A.4.6).
    /// spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array — frame 0 special case (no index): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.4.6 emitter_type-dependent static branch: CONFIRMED.
    /// </summary>
    public required XeffKeyframe[] Keyframes { get; init; }
}

/// <summary>
/// One keyframe within a <c>.xeff</c> sub-effect block.
/// Animated path — Frame 0 carries no index prefix on disk (9 × f32 = 36 bytes).
/// Animated path — Frames 1..N-1 each carry a u32 kf_index prefix (u32 + 9 × f32 = 40 bytes).
/// Static path — a single entry; 6 × f32 (24 B) if emitter_type != 2, 9 × f32 (36 B) if emitter_type == 2.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.4.6 emitter_type == 2 adds +12 bytes Euler rotation in static branch: CONFIRMED.
/// The 9-float layout: velocity Vec3 (1–3) + size Vec3 (4–6) + rotation degrees Euler-XYZ (7–9).
/// spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.8 Resolved semantics: HIGH.
/// </remarks>
public sealed class XeffKeyframe
{
    /// <summary>
    /// Frame index (0-based). Zero for frame 0 (no prefix on disk in animated path).
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
    /// In the static path, only present when emitter_type == 2.
    /// spec: Docs/RE/formats/effects.md §A.4.4 kf_rot_x_deg @ position 7 (degrees): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.7 Rotation Encoding Note — π/180, half-angle Euler-XYZ: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.4.6 — emitter_type == 2 reads extra 3 Euler floats in static branch: CONFIRMED.
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
/// File header is 8 bytes (0x08): effect_id u32 + sub_effect_count u32. Anti-magic: effect_id must NOT equal 0x46464558.
/// spec: Docs/RE/formats/effects.md §A.2 File Header (8 bytes): VERIFIED (2026-06-14 correction from prior 32-byte misreading).
/// spec: Docs/RE/formats/effects.md §A.1 Anti-magic 0x46464558: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.14 XEFF_HEADER_SIZE = 8 (0x08).
/// spec: Docs/RE/formats/effects.md §A.17 Correction history.
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
    /// Number of sub-effect blocks in the file. Zero is valid (stub/empty effect). Observed range: 0–68.
    /// spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count u32 @ 0x04: VERIFIED.
    /// </summary>
    public required uint SubEffectCount { get; init; }

    /// <summary>
    /// Sub-effect blocks decoded from the file body.
    /// Length == SubEffectCount. Each block is parsed by the same element read sequence (A.4).
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.4.5 — every block (block 0 included) uses the same read sequence: CONFIRMED.
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

// ─────────────────────────────────────────────────────────────────────────────
//  .xobj ASCII primitive meshes (data/effect/xobj/)
//  spec: Docs/RE/formats/effects.md §A.11
//  spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh
//
//  These are plain-text CRLF files. They define emitter shapes for emitter_type == 1
//  (mesh-particle objects) via the shared mesh table indexed by resource_id (< 10000).
//  In-memory vertex layout: 24 bytes per vertex (POSITION12 + DIFFUSE4 + TEXCOORD8).
//  spec: Docs/RE/formats/effects.md §A.11 — "shared mesh table, 24-byte stride": CONFIRMED.
//  spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes per vertex: CONFIRMED.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One vertex in a <c>.xobj</c> mesh-particle shape, as laid out in the 24-byte shared mesh table.
/// Layout: POSITION12 (3 × f32) + DIFFUSE4 (1 × u32 padding / uninitialised) + TEXCOORD8 (2 × f32).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.11 — "shared mesh table, 24-byte stride": CONFIRMED.
/// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes per vertex (6 × f32):
///   pos_x @ +0, pos_y @ +4, pos_z @ +8, DIFFUSE/padding @ +12 (uninitialised/zero), tex_u @ +16, tex_v @ +20.
///   Normals are read from disk and discarded (not carried in memory).
/// V-flip applied to tex_v: in-memory tex_v = 1.0 − disk_tex_v.
/// spec: Docs/RE/formats/mesh.md §Vertex list — tex_v: "engine transforms it to 1.0 - tex_v in-memory": CONFIRMED.
/// DIFFUSE4 field: byte layout is uninitialised by the loader (default zero). Retained here for faithful
/// 24-byte stride representation consistent with what the shared mesh table stores.
/// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — offset +12 "(uninitialised / padding)": CONFIRMED.
/// </remarks>
public readonly record struct XobjVertex(
    /// <summary>Position X. spec: mesh.md §In-memory vertex layout pos_x @ +0: CONFIRMED.</summary>
    float PosX,
    /// <summary>Position Y. spec: mesh.md §In-memory vertex layout pos_y @ +4: CONFIRMED.</summary>
    float PosY,
    /// <summary>Position Z. spec: mesh.md §In-memory vertex layout pos_z @ +8: CONFIRMED.</summary>
    float PosZ,
    /// <summary>
    /// DIFFUSE4 / padding dword at in-memory offset +12. Not written by the loader (stays zero).
    /// Exposed for faithful 24-byte stride representation.
    /// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — offset +12 "(uninitialised / padding)": CONFIRMED.
    /// </summary>
    uint Diffuse,
    /// <summary>
    /// Texture U coordinate. V-coordinate is already flipped (1.0 − disk value).
    /// spec: mesh.md §In-memory vertex layout tex_u @ +16: CONFIRMED.
    /// </summary>
    float TexU,
    /// <summary>
    /// Texture V coordinate (stored as 1.0 − disk_tex_v).
    /// spec: mesh.md §In-memory vertex layout tex_v @ +20 (1.0 − disk): CONFIRMED.
    /// </summary>
    float TexV);

/// <summary>
/// Decoded result of a <c>.xobj</c> ASCII mesh file for use as a mesh-particle emitter shape.
/// Feeds the shared mesh table indexed by a .xeff element's resource_id (&lt; 10000).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.11 — .xobj files define emitter shapes for emitter_type == 1
///   via the shared mesh table (24-byte stride, indexed by resource_id &lt; 10000): CONFIRMED.
/// spec: Docs/RE/formats/mesh.md §Format: .xobj — ASCII static mesh: CONFIRMED (3 samples).
/// File is plain text (CRLF). No binary header, no magic.
/// Read order: slot_id (discard), face_count, face_count×3 indices, vertex_count, vertex_count×8-token vertices.
/// spec: Docs/RE/formats/mesh.md §Preamble + §Index list + §Vertex count + §Vertex data rows: CONFIRMED.
/// Normals are read from disk and discarded; not present in this model.
/// spec: Docs/RE/formats/mesh.md §Vertex list — norm_x/y/z: "read then discarded; not kept in memory": CONFIRMED.
/// </remarks>
public sealed class XobjMeshData
{
    /// <summary>
    /// Triangle index list (u16, 0-based). Length always divisible by 3.
    /// spec: Docs/RE/formats/mesh.md §Index list — face_count × 3 indices: CONFIRMED.
    /// </summary>
    public required ushort[] Indices { get; init; }

    /// <summary>
    /// Vertex buffer with the 24-byte per-vertex layout (POSITION12 + DIFFUSE4 + TEXCOORD8).
    /// spec: Docs/RE/formats/effects.md §A.11 — shared mesh table 24-byte stride: CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §In-memory vertex layout — 24 bytes per vertex: CONFIRMED.
    /// </summary>
    public required XobjVertex[] Vertices { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  particleEmitter.eff  GPU particle emitter descriptor table
//  spec: Docs/RE/formats/effects.md §Section E
//
//  Layout: variable-length entry sequence (no file header, no magic, no count).
//  Entry = 28-byte entry header + num_frames × 52-byte sub-record + 64-byte texture name.
//  Termination: entry whose num_frames == 0, or < 28 bytes remain.
//  Runtime selection: raw entry_id equality against a .xeff element's resource_id (no −10000 subtraction).
//  spec: Docs/RE/formats/effects.md §E.2 File layout: CONFIRMED.
//  spec: Docs/RE/formats/effects.md §E.4 Runtime selection — raw-id map lookup: CONFIRMED.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One sub-record (52 bytes) within a <c>particleEmitter.eff</c> entry.
/// Only the 4-byte colour-like quad at +0x08 has an identified read-site.
/// The remaining bytes are UNRESOLVED and must not be invented.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §E.2.2 Sub-record (52 bytes / 0x34): CONFIRMED stride.
/// spec: Docs/RE/formats/effects.md §E.3 Known unknowns — 52-byte sub-record inner fields: UNRESOLVED except colour quad.
/// Sub-record stride: 52 bytes (CONFIRMED — loader allocates num_frames × 52).
/// Only +0x08..+0x0B colour-like quad has a confirmed read-site (default constructor).
/// </remarks>
public sealed class ParticleSubRecord
{
    // spec: Docs/RE/formats/effects.md §E.2.2 — +0x00 8 bytes unresolved_lead: UNRESOLVED.
    /// <summary>
    /// First 8 bytes of the sub-record. No isolated read-site found.
    /// // UNRESOLVED: no semantic has been traced for these bytes.
    /// spec: Docs/RE/formats/effects.md §E.2.2 — _unresolved_lead_ @ +0x00: UNRESOLVED.
    /// </summary>
    public required ReadOnlyMemory<byte> UnresolvedLead { get; init; }

    // spec: Docs/RE/formats/effects.md §E.2.2 — color_r u8 @ +0x08: MEDIUM.
    /// <summary>
    /// Colour R byte at sub-record +0x08. Default constructor zeroes this.
    /// spec: Docs/RE/formats/effects.md §E.2.2 — color_r u8 @ +0x08: MEDIUM.
    /// </summary>
    public required byte ColorR { get; init; }

    // spec: Docs/RE/formats/effects.md §E.2.2 — color_g u8 @ +0x09: MEDIUM.
    /// <summary>
    /// Colour G byte at sub-record +0x09. Default constructor zeroes this.
    /// spec: Docs/RE/formats/effects.md §E.2.2 — color_g u8 @ +0x09: MEDIUM.
    /// </summary>
    public required byte ColorG { get; init; }

    // spec: Docs/RE/formats/effects.md §E.2.2 — color_b u8 @ +0x0A: MEDIUM.
    /// <summary>
    /// Colour B byte at sub-record +0x0A. Default constructor zeroes this.
    /// spec: Docs/RE/formats/effects.md §E.2.2 — color_b u8 @ +0x0A: MEDIUM.
    /// </summary>
    public required byte ColorB { get; init; }

    // spec: Docs/RE/formats/effects.md §E.2.2 — color_a u8 @ +0x0B: MEDIUM (active sentinel 0xFF).
    /// <summary>
    /// Colour A byte (or active-sentinel) at sub-record +0x0B.
    /// Default constructor sets this byte to 0xFF — looks like alpha channel or "active = 0xFF" sentinel.
    /// spec: Docs/RE/formats/effects.md §E.2.2 — color_a (active sentinel) u8 @ +0x0B: MEDIUM.
    /// </summary>
    public required byte ColorA { get; init; }

    // spec: Docs/RE/formats/effects.md §E.2.2 — +0x0C 40 bytes unresolved_tail: UNRESOLVED.
    /// <summary>
    /// Remaining 40 bytes at sub-record +0x0C..+0x33. No read-sites captured.
    /// Likely per-particle velocity/size/time fields by analogy, but NOT proven.
    /// // UNRESOLVED: do not branch on or assign meaning to these bytes without spec confirmation.
    /// spec: Docs/RE/formats/effects.md §E.2.2 — _unresolved_tail_ @ +0x0C: UNRESOLVED.
    /// </summary>
    public required ReadOnlyMemory<byte> UnresolvedTail { get; init; }
}

/// <summary>
/// One entry in the <c>particleEmitter.eff</c> GPU particle emitter descriptor table.
/// Entry = 28-byte header + <see cref="NumFrames"/> × 52-byte sub-records + 64-byte texture name.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §E.2.1 Entry header (28 bytes / 0x1C): CONFIRMED.
/// spec: Docs/RE/formats/effects.md §E.2.2 Sub-record (52 bytes / 0x34): CONFIRMED stride.
/// spec: Docs/RE/formats/effects.md §E.2.3 Trailing texture name (64 bytes / 0x40): CONFIRMED.
///
/// Entry-id lookup: a .xeff element whose resource_id ≥ 10000 selects this entry by
/// raw entry_id equality (NO −10000 subtraction).
/// spec: Docs/RE/formats/effects.md §E.4 Runtime selection — raw-id equality: CONFIRMED.
/// </remarks>
public sealed class ParticleEmitterEntry
{
    /// <summary>
    /// Map key for this entry. A GPU-particle id in the ≥ 10000 space (first observed = 10001).
    /// Selected at spawn by raw equality to a .xeff element's resource_id (E.4).
    /// spec: Docs/RE/formats/effects.md §E.2.1 — entry_id u32 LE @ 0x00: CONFIRMED.
    /// </summary>
    public required uint EntryId { get; init; }

    /// <summary>
    /// Sub-record count AND loop terminator (0 ends the read loop).
    /// spec: Docs/RE/formats/effects.md §E.2.1 — num_frames u32 LE @ 0x04: CONFIRMED.
    /// </summary>
    public required uint NumFrames { get; init; }

    /// <summary>
    /// Per-emitter sprite size X. Fed to the GPU particle buffer's sprite-size setter.
    /// "x then y" axis naming is HIGH, not CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_x f32 LE @ 0x08: HIGH.
    /// </summary>
    public required float SpriteSizeX { get; init; }

    /// <summary>
    /// Per-emitter sprite size Y. Second sprite-size float.
    /// spec: Docs/RE/formats/effects.md §E.2.1 — sprite_size_y f32 LE @ 0x0C: HIGH.
    /// </summary>
    public required float SpriteSizeY { get; init; }

    /// <summary>
    /// Particle cap for this emitter. Asserted non-zero by the loader.
    /// Sizes the per-particle state and the vertex/index buffers.
    /// spec: Docs/RE/formats/effects.md §E.2.1 — max_particles u32 LE @ 0x10: HIGH.
    /// </summary>
    public required uint MaxParticles { get; init; }

    // The two trailing dwords of the header (tex_handle_slot @ 0x14, subrecord_array_ptr @ 0x18)
    // are OVERWRITTEN at load with resolved texture handle / sub-record array pointer.
    // Their on-disk values are ignored; the authoritative texture source is the 64-byte trailing name.
    // spec: Docs/RE/formats/effects.md §E.2.1 — tex_handle_slot @ 0x14 and subrecord_array_ptr @ 0x18:
    //   MEDIUM (disk value unused); on-disk values stored here for faithful byte-round-trip only.
    /// <summary>
    /// On-disk value of header bytes 0x14 (tex_handle_slot). IGNORED at runtime; overwritten with
    /// resolved texture handle. Authoritative texture is <see cref="TextureName"/>.
    /// spec: Docs/RE/formats/effects.md §E.2.1 — tex_handle_slot u32 LE @ 0x14: MEDIUM (disk value unused).
    /// </summary>
    public required uint RawTexHandleSlot { get; init; }

    /// <summary>
    /// On-disk value of header bytes 0x18 (subrecord_array_ptr). IGNORED at runtime; overwritten with
    /// allocated sub-record array pointer.
    /// spec: Docs/RE/formats/effects.md §E.2.1 — subrecord_array_ptr u32 LE @ 0x18: MEDIUM (disk value unused).
    /// </summary>
    public required uint RawSubrecordArrayPtr { get; init; }

    /// <summary>
    /// Sub-records: <see cref="NumFrames"/> × 52-byte records.
    /// Length == NumFrames.
    /// spec: Docs/RE/formats/effects.md §E.2.2 Sub-record (52 bytes): CONFIRMED stride.
    /// </summary>
    public required ParticleSubRecord[] SubRecords { get; init; }

    /// <summary>
    /// Trailing texture name (64-byte NUL-padded ASCII/CP949).
    /// Authoritative texture source; resolved by name through the texture manager.
    /// Full path: data/effect/texture/&lt;TextureName&gt;.tga (PLAUSIBLE by analogy with xeff §A.4.1).
    /// spec: Docs/RE/formats/effects.md §E.2.3 — texture_name char[64] @ end of sub-record array: CONFIRMED.
    /// </summary>
    public required string TextureName { get; init; }
}

/// <summary>
/// Decoded result of a <c>particleEmitter.eff</c> file.
/// Contains all non-terminator entries keyed by <see cref="ParticleEmitterEntry.EntryId"/>.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §E.2 File layout — variable-length entry sequence: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §E.1 Path: data/effect/particle/particleEmitter.eff.
///
/// No file header, no magic, no count. Read loop terminates at num_frames == 0 or &lt; 28 bytes remaining.
/// Entries are keyed by raw entry_id for O(1) lookup.
/// spec: Docs/RE/formats/effects.md §E.4 Runtime selection — raw-id map lookup: CONFIRMED.
/// </remarks>
public sealed class ParticleEmitterTable
{
    /// <summary>
    /// All entries decoded from the file (excluding the terminator entry).
    /// In on-disk order.
    /// spec: Docs/RE/formats/effects.md §E.2 — read loop: CONFIRMED.
    /// </summary>
    public ParticleEmitterEntry[] Entries { get; }

    /// <summary>
    /// O(1) lookup of a <see cref="ParticleEmitterEntry"/> by raw <c>entry_id</c>.
    /// A .xeff element's <c>resource_id ≥ 10000</c> selects by raw equality — NO −10000 subtraction.
    /// Returns <see langword="null"/> on a miss (the effect element renders nothing).
    /// spec: Docs/RE/formats/effects.md §E.4 — raw-id equality lookup, miss = no particle system: CONFIRMED.
    /// </summary>
    public ParticleEmitterEntry? TryGetById(uint entryId) =>
        _byId.TryGetValue(entryId, out var e) ? e : null;

    private readonly Dictionary<uint, ParticleEmitterEntry> _byId;

    /// <summary>
    /// Constructs the table from a decoded entry array. Builds the internal lookup dictionary.
    /// Duplicate entry_ids are silently overwritten (last wins) — consistent with the unresolved
    /// duplicate-resolution open question.
    /// spec: Docs/RE/formats/effects.md §E.4 — keyed by raw entry_id: CONFIRMED.
    /// </summary>
    public ParticleEmitterTable(ParticleEmitterEntry[] entries)
    {
        Entries = entries;
        _byId = new Dictionary<uint, ParticleEmitterEntry>(entries.Length);
        foreach (var e in entries)
            _byId[e.EntryId] = e;
    }
}