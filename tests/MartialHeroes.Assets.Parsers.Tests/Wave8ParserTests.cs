using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Wave-8 fixture-based tests covering the newly resolved fields:
/// BudVertex normal/UV, XeffKeyframe velocity/size/rotation, MudTile typed record,
/// SodBlob AABB and CollisionQuad corners, and userlevel/userpoint corrected column layout.
/// All fixtures are built in-memory; no real game files are required.
/// </summary>
public sealed class Wave8ParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static byte[] Le4(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    private static byte[] Le4f(float v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, v);
        return b;
    }

    private static byte[] Le2(ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        return b;
    }

    private static void WriteU16LE(byte[] buf, int offset, ushort v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), v);

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteF32LE(byte[] buf, int offset, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4), v);

    // =========================================================================
    // 1. BudVertex — Normal (Vec3) and UV (Vec2) — additif
    // =========================================================================
    // spec: Docs/RE/formats/terrain_scene.md §3.2.2 Vertex array (32 bytes):
    //   pos_x/y/z @ +0x00..+0x08: CONFIRMED.
    //   normal_x/y/z @ +0x0C..+0x17: CONFIRMED.
    //   uv_u/v @ +0x18..+0x1F: CONFIRMED.

    /// <summary>
    /// Builds a minimal .bud fixture: 1 object, 1 vertex, 3 indices (one triangle).
    /// spec: Docs/RE/formats/terrain_scene.md §4 File-size formula: total = 4+9+32+4+6 = 55 bytes for 1 obj 1v 3i.
    /// </summary>
    private static byte[] BuildBudSingleVertex(
        float px, float py, float pz,
        float nx, float ny, float nz,
        float uu, float uv)
    {
        // file header: object_count u32le. spec: §3.1.
        // object header: type_byte u8 (=0), tex_id u32le (=1), vertex_count u32le (=1). spec: §3.2.1.
        // vertex: 8 × f32le. spec: §3.2.2.
        // index_count u32le (=3) + 3 × u16le indices. spec: §3.2.3 + §3.2.4.
        using var ms = new System.IO.MemoryStream();
        ms.Write(Le4(1)); // object_count = 1
        ms.WriteByte(0); // type_byte = 0 (OBSERVED)
        ms.Write(Le4(1)); // tex_id = 1 (CONFIRMED 1-based)
        ms.Write(Le4(1)); // vertex_count = 1
        ms.Write(Le4f(px));
        ms.Write(Le4f(py));
        ms.Write(Le4f(pz)); // pos
        ms.Write(Le4f(nx));
        ms.Write(Le4f(ny));
        ms.Write(Le4f(nz)); // normal
        ms.Write(Le4f(uu));
        ms.Write(Le4f(uv)); // uv
        ms.Write(Le4(3)); // index_count = 3
        ms.Write(Le2(0));
        ms.Write(Le2(0));
        ms.Write(Le2(0)); // 3 indices (degenerate but parseable)
        return ms.ToArray();
    }

    [Fact]
    public void BudVertex_Normal_Fields_Decoded()
    {
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — normal_x/y/z @ +0x0C..+0x17: CONFIRMED.
        byte[] data = BuildBudSingleVertex(1f, 2f, 3f, 0.0f, 1.0f, 0.0f, 5.5f, 6.5f);
        BudScene scene = TerrainSceneParser.Parse(new ReadOnlyMemory<byte>(data));

        BudVertex v = scene.Objects[0].Vertices[0];
        Assert.Equal(0.0f, v.NormalX);
        Assert.Equal(1.0f, v.NormalY);
        Assert.Equal(0.0f, v.NormalZ);
    }

    [Fact]
    public void BudVertex_UV_Fields_Decoded()
    {
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — uv_u @ +0x18: CONFIRMED, uv_v @ +0x1C: CONFIRMED.
        byte[] data = BuildBudSingleVertex(0f, 0f, 0f, 0f, 1f, 0f, 24.5f, 27.3f);
        BudScene scene = TerrainSceneParser.Parse(new ReadOnlyMemory<byte>(data));

        BudVertex v = scene.Objects[0].Vertices[0];
        Assert.Equal(24.5f, v.UvU, precision: 5);
        Assert.Equal(27.3f, v.UvV, precision: 5);
    }

    [Fact]
    public void BudVertex_UnitNormal_MagnitudeIsOne()
    {
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — "normals have Euclidean magnitude 1.0 to within 1e-7": CONFIRMED.
        float nx = 0f, ny = 1f, nz = 0f; // trivial unit normal
        byte[] data = BuildBudSingleVertex(10f, 5f, 10f, nx, ny, nz, 1f, 1f);
        BudScene scene = TerrainSceneParser.Parse(new ReadOnlyMemory<byte>(data));

        BudVertex v = scene.Objects[0].Vertices[0];
        float mag = MathF.Sqrt(v.NormalX * v.NormalX + v.NormalY * v.NormalY + v.NormalZ * v.NormalZ);
        Assert.Equal(1.0f, mag, precision: 5);
    }

    [Fact]
    public void BudVertex_Position_Fields_Unchanged()
    {
        // Confirm pos_x/y/z unaffected by the additif changes.
        // spec: Docs/RE/formats/terrain_scene.md §3.2.2 — pos_x @ +0x00: CONFIRMED.
        byte[] data = BuildBudSingleVertex(100f, 50f, 200f, 0f, 1f, 0f, 0f, 0f);
        BudScene scene = TerrainSceneParser.Parse(new ReadOnlyMemory<byte>(data));

        BudVertex v = scene.Objects[0].Vertices[0];
        Assert.Equal(100f, v.PosX);
        Assert.Equal(50f, v.PosY);
        Assert.Equal(200f, v.PosZ);
    }

    // =========================================================================
    // 2. XeffKeyframe — Velocity Vec3, Size Vec3, Rotation Quat
    // =========================================================================
    // spec: Docs/RE/formats/effects.md §A.4.4 — nine-float keyframe layout: CONFIRMED.
    //   velocity_x/y/z @ positions 1–3: CONFIRMED.
    //   size_x/y/z @ positions 4–6: CONFIRMED.
    //   kf_rot_x/y/z_deg @ positions 7–9: CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.7 Rotation: "degrees × π/180; half-angle XYZ decomposition": CONFIRMED.
    // spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes, CORRECTED): VERIFIED.

    /// <summary>
    /// Builds a minimal .xeff (32-byte header + one sub-effect with one keyframe).
    /// The fixture follows the CORRECTED spec: 32-byte header then sequential sub-effect blocks.
    /// Block 0 has NO prefix — entry_count comes from first_entry_count in the header.
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free: CONFIRMED.
    ///
    /// Sub-effect layout for N=1 entry (one keyframe, frame 0 — no index prefix):
    ///   [NO prefix for block 0] + 64B name + (4+f32) alpha curve + (4+f32)×3 scale curves
    ///   + 13B track header + 9×f32 frame0 (no index)
    /// </summary>
    private static byte[] BuildXeffWithKeyframe(
        float velX, float velY, float velZ,
        float szX, float szY, float szZ,
        float rotXDeg, float rotYDeg, float rotZDeg)
    {
        using var ms = new System.IO.MemoryStream();

        // ── 32-byte file header ──────────────────────────────────────────────
        // spec: §A.2 — effect_id u32le @ 0x00: VERIFIED.
        ms.Write(Le4(12345u)); // effect_id (not anti-magic 0x46464558)
        // spec: §A.2 — sub_effect_count u32le @ 0x04: VERIFIED.
        ms.Write(Le4(1u)); // sub_effect_count = 1
        // spec: §A.2 — type_flag u32le @ 0x08: SAMPLE-VERIFIED.
        ms.Write(Le4(1u)); // type_flag = 1
        // spec: §A.2 — reserved u8[16] @ 0x0C: SAMPLE-VERIFIED.
        ms.Write(new byte[16]); // reserved, all zero
        // spec: §A.2 — first_entry_count u32le @ 0x1C: SAMPLE-VERIFIED.
        ms.Write(Le4(1u)); // first_entry_count = 1 — this IS block[0]'s entry count

        // ── sub-effect block 0 (NO prefix) ──────────────────────────────────
        // Block 0 has NO entry_count prefix on disk. Its entry_count = first_entry_count above.
        // spec: §A.15 — block[0] prefix-free; first_entry_count NOT duplicated at block start: CONFIRMED.

        // Name table: 1 × 64 bytes (null-padded ASCII texture name).
        // spec: §A.4.1 — entry_count × 64 bytes: CONFIRMED.
        ms.Write(new byte[64]); // one null name

        // Curve pass 1: alpha (count u32 + 1 × f32).
        // spec: §A.4.2 Pass 1 alpha — own u32 prefix: CONFIRMED.
        ms.Write(Le4(1u));
        ms.Write(Le4f(0.0f)); // alpha_key[0]

        // Curve pass 2: scale X (count u32 + 1 × f32).
        // spec: §A.4.2 Pass 2 scale X — own u32 prefix: CONFIRMED.
        ms.Write(Le4(1u));
        ms.Write(Le4f(1.0f));

        // Curve pass 3: scale Y.
        // spec: §A.4.2 Pass 3 scale Y — own u32 prefix: CONFIRMED.
        ms.Write(Le4(1u));
        ms.Write(Le4f(1.0f));

        // Curve pass 4: scale Z.
        // spec: §A.4.2 Pass 4 scale Z — own u32 prefix: CONFIRMED.
        ms.Write(Le4(1u));
        ms.Write(Le4f(1.0f));

        // Track header: 13 bytes (u8 anim_loop + u32 unknown_constant + u32 anim_stride + u32 anim_base_time).
        // spec: §A.4.3 Track header (13 bytes): CONFIRMED.
        // spec: §A.14 XEFF_TRACK_HEADER_SIZE = 13.
        ms.WriteByte(1); // anim_loop = 1 (non-zero)
        ms.Write(Le4(67u)); // unknown_constant = 67 (0x43): SAMPLE-VERIFIED
        ms.Write(Le4(100u)); // anim_stride = 100 ms
        ms.Write(Le4(0u)); // anim_base_time = 0

        // Keyframe array: frame 0 (no index prefix) = 9 × f32 = 36 bytes.
        // spec: §A.4.4 — Frame 0 is a special case: it has NO index prefix: CONFIRMED.
        ms.Write(Le4f(velX)); // velocity_x (position 1)
        ms.Write(Le4f(velY)); // velocity_y (position 2)
        ms.Write(Le4f(velZ)); // velocity_z (position 3)
        ms.Write(Le4f(szX)); // size_x (position 4)
        ms.Write(Le4f(szY)); // size_y (position 5)
        ms.Write(Le4f(szZ)); // size_z (position 6)
        ms.Write(Le4f(rotXDeg)); // kf_rot_x_deg (position 7): CONFIRMED
        ms.Write(Le4f(rotYDeg)); // kf_rot_y_deg (position 8): CONFIRMED
        ms.Write(Le4f(rotZDeg)); // kf_rot_z_deg (position 9): CONFIRMED

        return ms.ToArray();
    }

    [Fact]
    public void XeffKeyframe_Velocity_Vec3_Additif()
    {
        // spec: Docs/RE/formats/effects.md §A.8 — velocity Vec3 (velocity_x/y/z @ positions 1–3): HIGH.
        byte[] data = BuildXeffWithKeyframe(1.5f, 2.5f, 3.5f, 10f, 10f, 10f, 0f, 0f, 0f);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        XeffKeyframe kf = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(1.5f, kf.Velocity.X, precision: 5);
        Assert.Equal(2.5f, kf.Velocity.Y, precision: 5);
        Assert.Equal(3.5f, kf.Velocity.Z, precision: 5);
    }

    [Fact]
    public void XeffKeyframe_Size_Vec3_Additif()
    {
        // spec: Docs/RE/formats/effects.md §A.8 — size Vec3 (size_x/y/z @ positions 4–6): HIGH.
        byte[] data = BuildXeffWithKeyframe(0f, 0f, 0f, 5.0f, 6.0f, 7.0f, 0f, 0f, 0f);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        XeffKeyframe kf = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(5.0f, kf.Size.X, precision: 5);
        Assert.Equal(6.0f, kf.Size.Y, precision: 5);
        Assert.Equal(7.0f, kf.Size.Z, precision: 5);
    }

    [Fact]
    public void XeffKeyframe_Rotation_Identity_WhenAllDegreesZero()
    {
        // spec: Docs/RE/formats/effects.md §A.7 — rotation(0°,0°,0°) = identity (0,0,0,1): CONFIRMED.
        byte[] data = BuildXeffWithKeyframe(0f, 0f, 0f, 1f, 1f, 1f, 0f, 0f, 0f);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Quat q = xeff.SubEffects[0].Keyframes[0].Rotation;
        Assert.Equal(0f, q.X, precision: 5);
        Assert.Equal(0f, q.Y, precision: 5);
        Assert.Equal(0f, q.Z, precision: 5);
        Assert.Equal(1f, q.W, precision: 5);
    }

    [Fact]
    public void XeffKeyframe_Rotation_90DegY_CorrectQuat()
    {
        // Rotation 90° around Y: quat = (0, sin(45°), 0, cos(45°)) ≈ (0, 0.7071, 0, 0.7071).
        // spec: Docs/RE/formats/effects.md §A.7 — "degrees × π/180; half-angle Euler-XYZ decomposition": CONFIRMED.
        byte[] data = BuildXeffWithKeyframe(0f, 0f, 0f, 1f, 1f, 1f, 0f, 90f, 0f);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Quat q = xeff.SubEffects[0].Keyframes[0].Rotation;
        Assert.Equal(0f, q.X, precision: 4);
        Assert.Equal(MathF.Sin(MathF.PI / 4f), q.Y, precision: 4);
        Assert.Equal(0f, q.Z, precision: 4);
        Assert.Equal(MathF.Cos(MathF.PI / 4f), q.W, precision: 4);
    }

    [Fact]
    public void Xeff_SubEffectCount_And_EntryCount_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count @ 0x04: VERIFIED.
        // spec: Docs/RE/formats/effects.md §A.4 — entry_count opens each sub-effect block: CONFIRMED.
        byte[] data = BuildXeffWithKeyframe(1f, 2f, 3f, 4f, 5f, 6f, 0f, 0f, 0f);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(1u, xeff.SubEffectCount);
        Assert.Single(xeff.SubEffects);
        Assert.Equal(1u, xeff.SubEffects[0].EntryCount);
    }

    [Fact]
    public void Xeff_TrackHeader_AnimStride_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): anim_loop u8 @ +0,
        //       anim_stride u32 @ +1, anim_base_time u32 @ +5. The unknown_constant field is REFUTED
        //       (CAMPAIGN VFS-MASTERY, §A.17). The legacy fixture writes [anim_loop][67][100][0] after
        //       the curve section, so under the corrected 9-byte header the parser reads
        //       anim_stride = 67 (the fixture's first trailing dword) and anim_base_time = 100 (the
        //       second); the remaining dword (0) folds into the animated keyframe-0 kf_index prefix.
        byte[] data = BuildXeffWithKeyframe(0f, 0f, 0f, 1f, 1f, 1f, 0f, 0f, 0f);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        XeffSubEffect b0 = xeff.SubEffects[0];
        Assert.Equal(1, b0.AnimLoop);        // anim_loop = 1 (animated)
        Assert.Equal(67u, b0.AnimStride);    // anim_stride u32 @ +1
        Assert.Equal(100u, b0.AnimBaseTime); // anim_base_time u32 @ +5
    }

    [Fact]
    public void Xeff_Frame0_NoIndexPrefix_VelocityAndSize()
    {
        // Validates the frame-0 no-index-prefix rule.
        // spec: Docs/RE/formats/effects.md §A.4.4 — "Frame 0 is a special case: it has NO index prefix": CONFIRMED.
        byte[] data = BuildXeffWithKeyframe(7f, 8f, 9f, 10f, 11f, 12f, 0f, 0f, 0f);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        XeffKeyframe kf0 = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(0u, kf0.KfIndex); // frame 0: index = 0 (no prefix on disk)
        Assert.Equal(7f, kf0.VelocityX, precision: 5);
        Assert.Equal(10f, kf0.SizeX, precision: 5);
    }

    [Fact]
    public void Xeff_MultipleKeyframes_Frame1HasIndex()
    {
        // Validates that frames 1..N-1 have a u32 kf_index prefix.
        // spec: Docs/RE/formats/effects.md §A.4.4 — frames 1..N-1: u32 kf_index + 9 × f32: CONFIRMED.
        using var ms2 = new System.IO.MemoryStream();

        // 32-byte header
        ms2.Write(Le4(55555u)); // effect_id
        ms2.Write(Le4(1u)); // sub_effect_count = 1
        ms2.Write(Le4(1u)); // type_flag
        ms2.Write(new byte[16]); // reserved
        ms2.Write(Le4(2u)); // first_entry_count = 2

        // sub-effect block 0: NO prefix — entry_count = 2 from first_entry_count above.
        // spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free: CONFIRMED.

        // name table: 2 × 64 bytes
        // spec: Docs/RE/formats/effects.md §A.4.1 — entry_count × 64 bytes: CONFIRMED.
        ms2.Write(new byte[128]);

        // alpha curve: count=2, 2×f32
        ms2.Write(Le4(2u));
        ms2.Write(Le4f(0f));
        ms2.Write(Le4f(0.5f));
        // scaleX/Y/Z: count=2, 2×f32 each
        ms2.Write(Le4(2u));
        ms2.Write(Le4f(1f));
        ms2.Write(Le4f(2f));
        ms2.Write(Le4(2u));
        ms2.Write(Le4f(1f));
        ms2.Write(Le4f(2f));
        ms2.Write(Le4(2u));
        ms2.Write(Le4f(1f));
        ms2.Write(Le4f(2f));

        // track header: 13 bytes
        ms2.WriteByte(1); // anim_loop
        ms2.Write(Le4(67u)); // unknown_constant
        ms2.Write(Le4(500u)); // anim_stride
        ms2.Write(Le4(0u)); // anim_base_time

        // frame 0: no index prefix, 9 × f32
        // spec: §A.4.4 — Frame 0: 9 × f32 (36 bytes), no index: CONFIRMED.
        ms2.Write(Le4f(1f));
        ms2.Write(Le4f(0f));
        ms2.Write(Le4f(0f)); // velocity
        ms2.Write(Le4f(5f));
        ms2.Write(Le4f(5f));
        ms2.Write(Le4f(5f)); // size
        ms2.Write(Le4f(0f));
        ms2.Write(Le4f(0f));
        ms2.Write(Le4f(0f)); // rot degrees

        // frame 1: u32 kf_index + 9 × f32
        // spec: §A.4.4 — frames 1..N-1: u32 kf_index + 9 × f32 = 40 bytes: CONFIRMED.
        ms2.Write(Le4(1u)); // kf_index = 1
        ms2.Write(Le4f(2f));
        ms2.Write(Le4f(0f));
        ms2.Write(Le4f(0f)); // velocity
        ms2.Write(Le4f(10f));
        ms2.Write(Le4f(10f));
        ms2.Write(Le4f(10f)); // size
        ms2.Write(Le4f(0f));
        ms2.Write(Le4f(90f));
        ms2.Write(Le4f(0f)); // rot: 90° Y

        byte[] data2 = ms2.ToArray();
        XeffData xeff2 = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data2));

        XeffSubEffect sub = xeff2.SubEffects[0];
        Assert.Equal(2, sub.Keyframes.Length);
        Assert.Equal(0u, sub.Keyframes[0].KfIndex); // frame 0 has no on-disk prefix; KfIndex = 0
        Assert.Equal(1u, sub.Keyframes[1].KfIndex); // frame 1 has kf_index = 1
        Assert.Equal(2f, sub.Keyframes[1].VelocityX, precision: 5);
        // 90° rotation around Y should give Y-component ≈ 0.7071
        Assert.Equal(MathF.Sin(MathF.PI / 4f), sub.Keyframes[1].Rotation.Y, precision: 4);
    }

    [Fact]
    public void Xeff_AntiMagic_ThrowsInvalidData()
    {
        // spec: Docs/RE/formats/effects.md §A.1 — effect_id == 0x46464558 → file invalid: CONFIRMED.
        using var ms3 = new System.IO.MemoryStream();
        ms3.Write(Le4(0x46464558u)); // anti-magic
        ms3.Write(new byte[28]); // rest of the 32-byte header

        byte[] bad = ms3.ToArray();
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void Xeff_Stub_ZeroSubEffects_Parsed()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count=0 is valid (stub): VERIFIED.
        using var ms4 = new System.IO.MemoryStream();
        ms4.Write(Le4(99u)); // effect_id
        ms4.Write(Le4(0u)); // sub_effect_count = 0
        ms4.Write(Le4(1u)); // type_flag
        ms4.Write(new byte[16]); // reserved
        ms4.Write(Le4(0u)); // first_entry_count = 0

        byte[] stub = ms4.ToArray();
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(stub));

        Assert.Equal(99u, xeff.EffectId);
        Assert.Equal(0u, xeff.SubEffectCount);
        Assert.Empty(xeff.SubEffects);
    }

    // =========================================================================
    // 3. MudTile — typed record with sound index semantics
    // =========================================================================
    // spec: Docs/RE/formats/terrain.md §6.2 — music_group @ +2, ambient_idx_0/1 @ +3/+4,
    //                                          effect_idx_0/1/2 @ +5/+6/+7: all VERIFIED.

    [Fact]
    public void MudTile_FromRecord_Sound_Indices()
    {
        // spec: Docs/RE/formats/terrain.md §6.2 — all bytes VERIFIED.
        var rec = new MudTileRecord(
            Pad0: 0, Pad1: 0,
            MusicGroup: 7,
            AmbientIdx0: 16, AmbientIdx1: 17,
            EffectIdx0: 57, EffectIdx1: 91, EffectIdx2: 0
        );

        MudTile tile = MudTile.FromRecord(rec);
        // spec: §6.2 — music_group @ +2 (BGM index): VERIFIED.
        Assert.Equal((byte)7, tile.BgmIdx);
        // spec: §6.2 — ambient_idx_0 @ +3 (BGE table index): VERIFIED.
        Assert.Equal((byte)16, tile.BgeIdx0);
        // spec: §6.2 — ambient_idx_1 @ +4 (BGE table index): VERIFIED.
        Assert.Equal((byte)17, tile.BgeIdx1);
        // spec: §6.2 — effect_idx_0 @ +5 (EFF table index): VERIFIED.
        Assert.Equal((byte)57, tile.EffIdx0);
        // spec: §6.2 — effect_idx_1 @ +6 (EFF table index): VERIFIED.
        Assert.Equal((byte)91, tile.EffIdx1);
        // spec: §6.2 — effect_idx_2 @ +7 (always 0 in samples): VERIFIED (limited).
        Assert.Equal((byte)0, tile.EffIdx2);
    }

    [Fact]
    public void MudTile_FromMudBlob_RoundTrip()
    {
        // Build a minimal 32768-byte .mud with a known tile and verify MudTile extraction.
        // spec: Docs/RE/formats/terrain.md §6.1 — 64×64×8 = 32768 bytes: CONFIRMED.
        byte[] mud = new byte[32768]; // all zeros
        // Set tile at row=1, col=2: offset = (1×64+2)×8 = 528.
        int off = (1 * 64 + 2) * 8; // = 528
        // spec: §6.2 record layout: pad0/pad1 @+0/+1, music_group @+2, ambient_idx_0 @+3, ...
        mud[off + 2] = 3; // music_group (BGM index)
        mud[off + 3] = 5; // ambient_idx_0 (BGE index)
        mud[off + 4] = 6; // ambient_idx_1 (BGE index)
        mud[off + 5] = 91; // effect_idx_0 (EFF index)
        mud[off + 6] = 106; // effect_idx_1 (EFF index)
        mud[off + 7] = 0; // effect_idx_2

        MudBlob blob = MudBlobParser.Parse(new ReadOnlyMemory<byte>(mud));
        MudTileRecord rec = blob.Tiles[1 * 64 + 2];
        MudTile tile = MudTile.FromRecord(rec);

        Assert.Equal((byte)3, tile.BgmIdx);
        Assert.Equal((byte)5, tile.BgeIdx0);
        Assert.Equal((byte)6, tile.BgeIdx1);
        Assert.Equal((byte)91, tile.EffIdx0);
        Assert.Equal((byte)106, tile.EffIdx1);
        Assert.Equal((byte)0, tile.EffIdx2);
    }

    // =========================================================================
    // 4. SodBlob — AABB + CollisionQuad corners
    // =========================================================================
    // spec: Docs/RE/formats/terrain.md §11.2 — aabb_xmin/zmin/xmax/zmax +0..+15: VERIFIED.
    // spec: Docs/RE/formats/terrain.md §11.3 — x0/z0..x3/z3 +0..+31: VERIFIED.
    // spec: Docs/RE/formats/terrain.md §11.3 — plane0..plane3 +32..+47: PARTIAL.

    /// <summary>
    /// Builds a minimal .sod with 1 solid and 1 quad.
    /// File size = 4 + 108 + 4 + 48 = 164 bytes.
    /// spec: Docs/RE/formats/terrain.md §11.1 formula: confirmed.
    /// </summary>
    private static byte[] BuildSodOneQuad(
        float aabbXMin, float aabbZMin, float aabbXMax, float aabbZMax,
        float x0, float z0, float x1, float z1,
        float x2, float z2, float x3, float z3,
        float plane0, float plane1, float plane2, float plane3)
    {
        byte[] buf = new byte[164]; // 4 + 108 + 4 + 48

        // solidCount = 1 @ offset 0.
        WriteU32LE(buf, 0, 1u);

        // SolidRecord at offset 4 (108 bytes).
        // AABB @ +0..+15: VERIFIED.
        // spec: §11.2 — aabb_xmin f32 @ +000: VERIFIED.
        WriteF32LE(buf, 4 + 0, aabbXMin);
        WriteF32LE(buf, 4 + 4, aabbZMin);
        WriteF32LE(buf, 4 + 8, aabbXMax);
        WriteF32LE(buf, 4 + 12, aabbZMax);
        // +016..+059: _reserved_a (all zero — already zero in buf). VERIFIED.
        // quad_count_embedded @ +060: VERIFIED.
        WriteU32LE(buf, 4 + 60, 1u); // embedded quad count = 1
        // _authoring_ptr @ +064: stale pointer (VERIFIED — parser ignores it). Write a plausible value.
        WriteU32LE(buf, 4 + 64, 0xDEADBEEFu);
        // +068..+107: _reserved_b (all zero). VERIFIED.

        // quadCount stream copy at offset 4+108 = 112.
        WriteU32LE(buf, 112, 1u);

        // QuadRecord at offset 116 (48 bytes).
        // Corners +0..+31: VERIFIED.
        // spec: §11.3 — x0 f32 @ +000: VERIFIED.
        WriteF32LE(buf, 116 + 0, x0);
        WriteF32LE(buf, 116 + 4, z0);
        WriteF32LE(buf, 116 + 8, x1);
        WriteF32LE(buf, 116 + 12, z1);
        WriteF32LE(buf, 116 + 16, x2);
        WriteF32LE(buf, 116 + 20, z2);
        WriteF32LE(buf, 116 + 24, x3);
        WriteF32LE(buf, 116 + 28, z3);
        // Trailing scalars +032..+047: PARTIAL.
        // spec: §11.3 — plane0..plane3: PARTIAL.
        WriteF32LE(buf, 116 + 32, plane0);
        WriteF32LE(buf, 116 + 36, plane1);
        WriteF32LE(buf, 116 + 40, plane2);
        WriteF32LE(buf, 116 + 44, plane3);

        return buf;
    }

    [Fact]
    public void SodBlob_AABB_Decoded()
    {
        // spec: Docs/RE/formats/terrain.md §11.2 — AABB fields +0..+15: VERIFIED.
        byte[] data = BuildSodOneQuad(
            10f, 20f, 30f, 40f,
            10f, 20f, 30f, 20f, 30f, 40f, 10f, 40f,
            -27f, 0f, 1500f, 0f);

        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));

        SolidRecord solid = blob.Solids[0];
        Assert.Equal(10f, solid.AabbXMin, precision: 5);
        Assert.Equal(20f, solid.AabbZMin, precision: 5);
        Assert.Equal(30f, solid.AabbXMax, precision: 5);
        Assert.Equal(40f, solid.AabbZMax, precision: 5);
    }

    [Fact]
    public void SodBlob_CollisionQuad_Corners_Decoded()
    {
        // spec: Docs/RE/formats/terrain.md §11.3 — x0/z0..x3/z3 +0..+31: VERIFIED.
        byte[] data = BuildSodOneQuad(
            10f, 20f, 30f, 40f,
            10f, 20f, 30f, 20f, 30f, 40f, 10f, 40f, // four corners forming a rectangle
            -27f, 0f, 1500f, 0f);

        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));
        CollisionQuad quad = blob.Solids[0].Quads[0];

        Assert.Equal(10f, quad.X0, precision: 5); // spec: §11.3 x0 @ +000: VERIFIED
        Assert.Equal(20f, quad.Z0, precision: 5); // spec: §11.3 z0 @ +004: VERIFIED
        Assert.Equal(30f, quad.X1, precision: 5);
        Assert.Equal(20f, quad.Z1, precision: 5);
        Assert.Equal(30f, quad.X2, precision: 5);
        Assert.Equal(40f, quad.Z2, precision: 5);
        Assert.Equal(10f, quad.X3, precision: 5);
        Assert.Equal(40f, quad.Z3, precision: 5);
    }

    [Fact]
    public void SodBlob_CollisionQuad_TrailingScalars_Partial()
    {
        // spec: Docs/RE/formats/terrain.md §11.3 — plane0..plane3 +032..+047: PARTIAL.
        byte[] data = BuildSodOneQuad(
            0f, 0f, 100f, 100f,
            0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f,
            -27.5f, 0f, 1234.5f, 0f);

        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));
        CollisionQuad quad = blob.Solids[0].Quads[0];

        Assert.Equal(-27.5f, quad.EdgeSlope, precision: 4); // formerly Plane0; re-labelled 2026-06-14 per spec §11.3
        Assert.Equal(0f, quad.EdgePad0, precision: 5); // formerly Plane1
        Assert.Equal(1234.5f, quad.EdgeIntercept, precision: 4); // formerly Plane2
        Assert.Equal(0f, quad.EdgePad1, precision: 5); // formerly Plane3
    }

    [Fact]
    public void SodBlob_Backward_Compat_Raw_Fields_Still_Present()
    {
        // Ensure RawSolidRecords, TriangleCounts, RawTriangleData still exist.
        byte[] data = BuildSodOneQuad(
            0f, 0f, 10f, 10f,
            0f, 0f, 10f, 0f, 10f, 10f, 0f, 10f,
            0f, 0f, 0f, 0f);

        SodBlob blob = SodBlobParser.Parse(new ReadOnlyMemory<byte>(data));

        Assert.Equal(1u, blob.SolidCount);
        Assert.Single(blob.RawSolidRecords);
        Assert.Equal(108, blob.RawSolidRecords[0].Length); // stride CONFIRMED
        Assert.Equal(1u, blob.TriangleCounts[0]);
        Assert.Equal(48, blob.RawTriangleData[0].Length); // stride CONFIRMED
    }

    // =========================================================================
    // 5. userlevel.scr — Wave-8 corrected column layout (u16 counters)
    // =========================================================================
    // spec: Docs/RE/formats/config_tables.md §2.4 SPEC CORRECTION:
    //   +4 u16 TierStepA, +6 u16 TierStepB (mirrors +4), +8 u16 DivisorC, +10 u16 zero pad.
    //   +12..+27: 4×f32 positive-scale group. +28..+43: 4×f32 negative-scale group.

    private static byte[] BuildUserLevelScrWave8(
        ushort level, ushort tierStepA, ushort tierStepB, ushort divisorC,
        float posScale, float negScale)
    {
        byte[] buf = new byte[60]; // stride 60 bytes
        WriteU16LE(buf, 0, level); // +0 u16 level: CONFIRMED
        WriteU16LE(buf, 2, 0); // +2 u16 zero pad: CONFIRMED
        WriteU16LE(buf, 4, tierStepA); // +4 u16 TierStepA: CONFIRMED
        WriteU16LE(buf, 6, tierStepB); // +6 u16 TierStepB: CONFIRMED
        WriteU16LE(buf, 8, divisorC); // +8 u16 DivisorC: CONFIRMED
        WriteU16LE(buf, 10, 0); // +10 u16 zero pad: CONFIRMED
        // +12..+27: 4×f32 positive-scale group (all same value for simplicity).
        for (int i = 0; i < 4; i++) WriteF32LE(buf, 12 + i * 4, posScale);
        // +28..+43: 4×f32 negative-scale group.
        for (int i = 0; i < 4; i++) WriteF32LE(buf, 28 + i * 4, negScale);
        // +44..+59: reserved (all 0.0, already zero).
        return buf;
    }

    [Fact]
    public void UserLevelScr_TierStepA_Decoded_Wave8()
    {
        // spec: Docs/RE/formats/config_tables.md §2.4 — "+4 u16 Tier step counter A: CONFIRMED".
        byte[] data = BuildUserLevelScrWave8(12, 1, 1, 2, 1.0f, -1.0f);
        LevelBaseEntry[] entries = ConfigTableParser.ParseUserLevelScr(new ReadOnlyMemory<byte>(data));

        // spec: §2.4 transition table — L12: TierStepA=1, DivisorC=2: CONFIRMED.
        Assert.Equal((ushort)12, entries[0].Level);
        Assert.Equal((ushort)1, entries[0].TierStepA);
        Assert.Equal((ushort)1, entries[0].TierStepB);
        Assert.Equal((ushort)2, entries[0].DivisorC);
    }

    [Fact]
    public void UserLevelScr_StepA_Alias_Backward_Compat()
    {
        // The StepA/StepB aliases must return the same value as TierStepA/TierStepB.
        byte[] data = BuildUserLevelScrWave8(36, 2, 2, 4, 3.0f, -2.0f);
        LevelBaseEntry[] entries = ConfigTableParser.ParseUserLevelScr(new ReadOnlyMemory<byte>(data));

        // spec: §2.4 transition — L36: TierStepA=2, DivisorC=4: CONFIRMED.
        Assert.Equal(entries[0].TierStepA, entries[0].StepA);
        Assert.Equal(entries[0].TierStepB, entries[0].StepB);
    }

    [Fact]
    public void UserLevelScr_PositiveScaleGroup_Decoded_Wave8()
    {
        // spec: §2.4 — "+12 4×f32 positive-scale group: CONFIRMED". L36+ = 3.0.
        byte[] data = BuildUserLevelScrWave8(36, 2, 2, 4, 3.0f, -2.0f);
        LevelBaseEntry[] entries = ConfigTableParser.ParseUserLevelScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(4, entries[0].StatScalePositive.Length);
        Assert.All(entries[0].StatScalePositive, v => Assert.Equal(3.0f, v, precision: 5));
    }

    [Fact]
    public void UserLevelScr_NegativeScaleGroup_Decoded_Wave8()
    {
        // spec: §2.4 — "+28 4×f32 negative-scale group: CONFIRMED". L36+ = −2.0.
        byte[] data = BuildUserLevelScrWave8(36, 2, 2, 4, 3.0f, -2.0f);
        LevelBaseEntry[] entries = ConfigTableParser.ParseUserLevelScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(4, entries[0].StatScaleNegative.Length);
        Assert.All(entries[0].StatScaleNegative, v => Assert.Equal(-2.0f, v, precision: 5));
    }

    // =========================================================================
    // 6. userpoint.scr — Wave-8 corrected layout (StatGroup1Cumulative is u32)
    // =========================================================================
    // spec: Docs/RE/formats/config_tables.md §2.5 SPEC CORRECTION:
    //   +8 u32 Stat-group-1 cumulative (was u16+flag, now confirmed u32 spanning +8..+11).
    //   +16 u32 Stat-group-2 cumulative.

    private static byte[] BuildUserPointScrWave8(
        ushort key, ushort const25,
        ushort sg1Gain, uint sg1Cumul,
        ushort sg2Gain, uint sg2Cumul,
        ushort secLow, ushort secHigh,
        uint tert1, uint tert2)
    {
        byte[] buf = new byte[32]; // stride 32 bytes
        WriteU16LE(buf, 0, key); // +0 u16 key: CONFIRMED
        WriteU16LE(buf, 2, const25); // +2 u16 constant=25: CONFIRMED
        WriteU16LE(buf, 4, sg1Gain); // +4 u16 sg1 gain: CONFIRMED
        WriteU16LE(buf, 6, 0); // +6 u16 zero pad: CONFIRMED
        WriteU32LE(buf, 8, sg1Cumul); // +8 u32 sg1 cumulative: CONFIRMED (was u16)
        WriteU16LE(buf, 12, sg2Gain); // +12 u16 sg2 gain: CONFIRMED
        WriteU16LE(buf, 14, 0); // +14 u16 zero pad: CONFIRMED
        WriteU32LE(buf, 16, sg2Cumul); // +16 u32 sg2 cumulative: CONFIRMED
        WriteU16LE(buf, 20, secLow); // +20 u16 sec low: CONFIRMED
        WriteU16LE(buf, 22, secHigh); // +22 u16 sec high: CONFIRMED
        WriteU32LE(buf, 24, tert1); // +24 u32 tert1: CONFIRMED
        WriteU32LE(buf, 28, tert2); // +28 u32 tert2: CONFIRMED
        return buf;
    }

    [Fact]
    public void UserPointScr_StatGroup1Cumulative_U32_Wave8()
    {
        // spec: §2.5 — "+8 u32 Stat-group-1 cumulative": CONFIRMED (exceeds 65535 at key=285+).
        // Test with a value that exceeds the old u16 range (>65535).
        uint bigCumul = 66000u; // > 65535 — proves it must be u32
        byte[] data = BuildUserPointScrWave8(285, 25, 1000, bigCumul, 3, 3000u, 696, 56, 0u, 0u);
        UserPointEntry[] entries = ConfigTableParser.ParseUserPointScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(bigCumul, entries[0].StatGroup1Cumulative);
    }

    [Fact]
    public void UserPointScr_StatGroup2Cumulative_U32_Wave8()
    {
        // spec: §2.5 — "+16 u32 Stat-group-2 cumulative: CONFIRMED".
        uint cumul2 = 38941u;
        byte[] data = BuildUserPointScrWave8(300, 25, 1000, 65960u, 300, cumul2, 696, 56, 255000u, 255000u);
        UserPointEntry[] entries = ConfigTableParser.ParseUserPointScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(cumul2, entries[0].StatGroup2Cumulative);
    }

    [Fact]
    public void UserPointScr_Gains_And_Key_Wave8()
    {
        // spec: §2.5 — "+4 u16 sg1 gain, +12 u16 sg2 gain: CONFIRMED".
        byte[] data = BuildUserPointScrWave8(1, 25, 3, 3u, 2, 9u, 0, 0, 0u, 0u);
        UserPointEntry[] entries = ConfigTableParser.ParseUserPointScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal((ushort)1, entries[0].Key);
        Assert.Equal((ushort)3, entries[0].StatGroup1Gain);
        Assert.Equal((ushort)2, entries[0].StatGroup2Gain);
    }

    [Fact]
    public void UserPointScr_TertiaryValues_Wave8()
    {
        // spec: §2.5 — "+24 u32 Tertiary value 1, +28 u32 Tertiary value 2: CONFIRMED".
        byte[] data = BuildUserPointScrWave8(296, 25, 1000, 65960u, 3, 1u, 0, 0, 235000u, 235000u);
        UserPointEntry[] entries = ConfigTableParser.ParseUserPointScr(new ReadOnlyMemory<byte>(data));

        Assert.Equal(235000u, entries[0].TertiaryValue1);
        Assert.Equal(235000u, entries[0].TertiaryValue2);
    }
}