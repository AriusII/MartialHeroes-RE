using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based regression tests for <see cref="XeffParser"/>.
/// All fixtures are built in-memory from scratch — no real game files required.
/// Tests cover the corrected 32-byte header, sequential sub-effect block layout,
/// frame-0 no-index-prefix rule, and large sub_effect_count (regression for the
/// "truncated reading Group D scaleX values" failure on char_select-u.xeff / zone_sel_u.xeff).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes, CORRECTED): VERIFIED.
/// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.4.4 — Frame 0: no index prefix; frames 1..N-1: u32+9×f32: CONFIRMED.
/// </remarks>
public sealed class XeffParserTests
{
    // ─── byte helpers ──────────────────────────────────────────────────────────

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

    // ─── fixture builder ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal syntactically-correct .xeff byte buffer with the given
    /// number of sub-effects, each containing the given number of entries.
    ///
    /// File layout:
    ///   32-byte header (effect_id, sub_effect_count, type_flag, reserved[16], first_entry_count)
    ///   Then for each sub-effect:
    ///     entry_count u32
    ///     entry_count × 64B name table
    ///     (4 + entry_count × 4) alpha curve  [count prefix + values]
    ///     (4 + 0) scaleX curve  [count=0]
    ///     (4 + 0) scaleY curve
    ///     (4 + 0) scaleZ curve
    ///     13-byte track header  (animLoop=1, unknownConstant=67, animStride=100, animBaseTime=0)
    ///     frame 0: 9 × f32 = 36 bytes (no index prefix)
    ///     frames 1..N-1: each (u32 kf_index + 9 × f32) = 40 bytes
    ///
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array — frame 0 special case: CONFIRMED.
    /// </summary>
    private static byte[] BuildXeff(uint effectId, uint subEffectCount, uint entriesPerSubEffect)
    {
        using var ms = new MemoryStream();

        // ── 32-byte file header ──────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32le @ 0x00: VERIFIED.
        ms.Write(Le4(effectId)); // 0x00 effect_id
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count u32le @ 0x04: VERIFIED.
        ms.Write(Le4(subEffectCount)); // 0x04 sub_effect_count
        // spec: Docs/RE/formats/effects.md §A.2 — type_flag u32le @ 0x08: SAMPLE-VERIFIED.
        ms.Write(Le4(1u)); // 0x08 type_flag = 1
        // spec: Docs/RE/formats/effects.md §A.2 — reserved u8[16] @ 0x0C: SAMPLE-VERIFIED.
        ms.Write(new byte[16]); // 0x0C reserved
        // spec: Docs/RE/formats/effects.md §A.2 — first_entry_count u32le @ 0x1C: SAMPLE-VERIFIED.
        ms.Write(Le4(entriesPerSubEffect)); // 0x1C first_entry_count

        // ── sub-effect blocks (sequential) ──────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.5 — blocks follow sequentially, no offset table: CONFIRMED.
        for (uint s = 0; s < subEffectCount; s++)
        {
            WriteSubEffect(ms, entriesPerSubEffect);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Writes one sub-effect block with N=<paramref name="n"/> entries.
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
    /// </summary>
    private static void WriteSubEffect(MemoryStream ms, uint n)
    {
        // entry_count u32le — opens each sub-effect block.
        // spec: §A.4 — entry_count u32 opens each sub-effect block: CONFIRMED.
        ms.Write(Le4(n));

        // Name table: n × 64 bytes (null-padded).
        // spec: §A.4.1 — entry_count × 64 bytes: CONFIRMED.
        ms.Write(new byte[(int)(n * 64)]);

        // Curve pass 1: alpha (count = n, n floats).
        // spec: §A.4.2 Pass 1 alpha — own u32 prefix + count × f32: CONFIRMED.
        ms.Write(Le4(n));
        for (uint i = 0; i < n; i++) ms.Write(Le4f(0.5f));

        // Curve passes 2–4: scale X/Y/Z (count = 0 each — simplest valid case).
        // spec: §A.4.2 Passes 2–4 scale X/Y/Z — own u32 prefix + count × f32: CONFIRMED.
        ms.Write(Le4(0u)); // scaleX count=0
        ms.Write(Le4(0u)); // scaleY count=0
        ms.Write(Le4(0u)); // scaleZ count=0

        // Track header: 13 bytes.
        // spec: §A.4.3 Track header (13 bytes): CONFIRMED.
        // spec: §A.14 XEFF_TRACK_HEADER_SIZE = 13 (1+4+4+4).
        ms.WriteByte(1); // anim_loop u8 @ +0 (non-zero)
        ms.Write(Le4(67u)); // unknown_constant u32 @ +1 (observed value 67): SAMPLE-VERIFIED
        ms.Write(Le4(469u)); // anim_stride u32 @ +5 (ms)
        ms.Write(Le4(0u)); // anim_base_time u32 @ +9

        // Keyframe array.
        // spec: §A.4.4 Keyframe array: CONFIRMED.
        if (n == 0) return; // zero entries → zero keyframes (valid stub)

        // Frame 0: NO index prefix — 9 × f32 = 36 bytes.
        // spec: §A.4.4 — "Frame 0 is a special case: it has NO index prefix": CONFIRMED.
        WriteNineFloats(ms, 0u, velocityX: 1f, velocityY: 0f, velocityZ: 0f,
            sizeX: 1f, sizeY: 1f, sizeZ: 1f, rotX: 0f, rotY: 0f, rotZ: 0f,
            includeIndex: false);

        // Frames 1..N-1: u32 kf_index + 9 × f32 = 40 bytes each.
        // spec: §A.4.4 — frames 1..N-1: u32 kf_index + 9 × f32: CONFIRMED.
        for (uint k = 1; k < n; k++)
        {
            WriteNineFloats(ms, k, velocityX: (float)k, velocityY: 0f, velocityZ: 0f,
                sizeX: 1f, sizeY: 1f, sizeZ: 1f, rotX: 0f, rotY: 0f, rotZ: 0f,
                includeIndex: true);
        }
    }

    /// <summary>
    /// Writes a single keyframe's fields to <paramref name="ms"/>.
    /// If <paramref name="includeIndex"/> is true, writes kf_index u32 first (frames 1..N-1).
    /// spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
    /// </summary>
    private static void WriteNineFloats(MemoryStream ms, uint kfIndex,
        float velocityX, float velocityY, float velocityZ,
        float sizeX, float sizeY, float sizeZ,
        float rotX, float rotY, float rotZ,
        bool includeIndex)
    {
        if (includeIndex)
            ms.Write(Le4(kfIndex)); // u32 kf_index — only for frames 1..N-1

        ms.Write(Le4f(velocityX)); // position 1
        ms.Write(Le4f(velocityY)); // position 2
        ms.Write(Le4f(velocityZ)); // position 3
        ms.Write(Le4f(sizeX)); // position 4
        ms.Write(Le4f(sizeY)); // position 5
        ms.Write(Le4f(sizeZ)); // position 6
        ms.Write(Le4f(rotX)); // position 7 (kf_rot_x_deg)
        ms.Write(Le4f(rotY)); // position 8 (kf_rot_y_deg)
        ms.Write(Le4f(rotZ)); // position 9 (kf_rot_z_deg)
    }

    // ─── tests: header ─────────────────────────────────────────────────────────

    [Fact]
    public void Header_EffectId_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32 @ 0x00: VERIFIED.
        byte[] data = BuildXeff(331110711u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(331110711u, xeff.EffectId);
    }

    [Fact]
    public void Header_SubEffectCount_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count u32 @ 0x04: VERIFIED.
        byte[] data = BuildXeff(12345u, 3u, 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(3u, xeff.SubEffectCount);
        Assert.Equal(3, xeff.SubEffects.Length);
    }

    [Fact]
    public void Header_TypeFlag_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — type_flag u32 @ 0x08: SAMPLE-VERIFIED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(1u, xeff.TypeFlag);
    }

    [Fact]
    public void Header_FirstEntryCount_MatchesFirstSubEffect()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — first_entry_count u32 @ 0x1C: SAMPLE-VERIFIED.
        // The value at 0x1C must match sub-effect[0].entry_count.
        byte[] data = BuildXeff(1u, 2u, 5u); // 2 sub-effects, each with 5 entries
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(5u, xeff.FirstEntryCount);
        Assert.Equal(5u, xeff.SubEffects[0].EntryCount);
    }

    [Fact]
    public void Header_Reserved_IsPresent()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — reserved u8[16] @ 0x0C: SAMPLE-VERIFIED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(16, xeff.Reserved.Length);
        Assert.All(xeff.Reserved, b => Assert.Equal(0, b));
    }

    // ─── tests: anti-magic ─────────────────────────────────────────────────────

    [Fact]
    public void AntiMagic_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/effects.md §A.1 — effect_id == 0x46464558 → file invalid: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_INVALID_MAGIC = 0x46464558.
        using var ms = new MemoryStream();
        ms.Write(Le4(0x46464558u)); // anti-magic
        ms.Write(new byte[28]); // rest of header (to reach 32 bytes)

        byte[] bad = ms.ToArray();
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(bad)));
    }

    [Fact]
    public void TruncatedHeader_ThrowsInvalidDataException()
    {
        // Buffer too short for the 32-byte header.
        // spec: Docs/RE/formats/effects.md §A.2 — header is 32 bytes: VERIFIED.
        byte[] truncated = new byte[16]; // too short
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(truncated)));
    }

    // ─── tests: stub (zero sub-effects) ────────────────────────────────────────

    [Fact]
    public void StubEffect_ZeroSubEffects_Parsed()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count=0 valid (8 stubs observed): VERIFIED.
        byte[] data = BuildXeff(42u, 0u, 0u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(42u, xeff.EffectId);
        Assert.Equal(0u, xeff.SubEffectCount);
        Assert.Empty(xeff.SubEffects);
    }

    // ─── tests: single sub-effect, single entry ────────────────────────────────

    [Fact]
    public void SingleSubEffect_OneEntry_EntryCountDecoded()
    {
        // spec: Docs/RE/formats/effects.md §A.4 — entry_count u32 opens each sub-effect block: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(1u, xeff.SubEffects[0].EntryCount);
        Assert.Single(xeff.SubEffects[0].Keyframes);
    }

    [Fact]
    public void SingleSubEffect_OneEntry_TrackHeader_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (13 bytes): CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.4.3 — unknown_constant u32 @ +1: SAMPLE-VERIFIED (67).
        // spec: Docs/RE/formats/effects.md §A.4.3 — anim_stride u32 @ +5: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffSubEffect sub = xeff.SubEffects[0];

        Assert.Equal(1, sub.AnimLoop); // non-zero
        Assert.Equal(67u, sub.UnknownConstant); // SAMPLE-VERIFIED value
        Assert.Equal(469u, sub.AnimStride); // set in fixture
        Assert.Equal(0u, sub.AnimBaseTime);
    }

    [Fact]
    public void SingleSubEffect_OneEntry_Frame0_NoIndexPrefix_VelocityX()
    {
        // Frame 0 has no index prefix on disk. Its KfIndex is always 0.
        // spec: Docs/RE/formats/effects.md §A.4.4 — "Frame 0 is a special case: it has NO index prefix": CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        XeffKeyframe kf0 = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(0u, kf0.KfIndex); // frame 0: KfIndex = 0 (no on-disk prefix)
        Assert.Equal(1f, kf0.VelocityX, precision: 5); // velocity_x set to 1.0 in fixture
    }

    [Fact]
    public void SingleSubEffect_TwoEntries_Frame1_HasKfIndex()
    {
        // Frame 1 carries a u32 kf_index prefix on disk.
        // spec: Docs/RE/formats/effects.md §A.4.4 — frames 1..N-1: u32 kf_index + 9 × f32: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        XeffSubEffect sub = xeff.SubEffects[0];
        Assert.Equal(2, sub.Keyframes.Length);
        Assert.Equal(0u, sub.Keyframes[0].KfIndex); // frame 0
        Assert.Equal(1u, sub.Keyframes[1].KfIndex); // frame 1 kf_index written by fixture
        // Frame 1 velocity_x = (float)1 = 1.0 (set by fixture loop)
        Assert.Equal(1f, sub.Keyframes[1].VelocityX, precision: 5);
    }

    [Fact]
    public void SingleSubEffect_AlphaKeys_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 1 alpha — own u32 prefix + count × f32: CONFIRMED.
        // Fixture writes N=2 alpha values = 0.5 each.
        byte[] data = BuildXeff(1u, 1u, 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        float[] alpha = xeff.SubEffects[0].AlphaKeys;
        Assert.Equal(2, alpha.Length);
        Assert.Equal(0.5f, alpha[0], precision: 5);
        Assert.Equal(0.5f, alpha[1], precision: 5);
    }

    [Fact]
    public void SingleSubEffect_ScaleCurves_EmptyWhenCountZero()
    {
        // spec: Docs/RE/formats/effects.md §A.4.2 Passes 2–4 scale X/Y/Z — own u32 prefix: CONFIRMED.
        // Fixture writes count=0 for all three scale passes.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Empty(xeff.SubEffects[0].ScaleX);
        Assert.Empty(xeff.SubEffects[0].ScaleY);
        Assert.Empty(xeff.SubEffects[0].ScaleZ);
    }

    [Fact]
    public void SingleSubEffect_TextureNames_NullPadded_EmptyString()
    {
        // spec: Docs/RE/formats/effects.md §A.4.1 — tex_name char[64] null-padded: CONFIRMED.
        // Fixture writes all-zero 64-byte name → decoded as empty string.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        string name = xeff.SubEffects[0].TextureNames[0];
        Assert.Equal("", name);
    }

    // ─── tests: multiple sub-effects ───────────────────────────────────────────

    [Fact]
    public void MultipleSubEffects_ThreeSubEffects_EachParsedIndependently()
    {
        // spec: Docs/RE/formats/effects.md §A.4.5 Multi-sub-effect files — blocks follow sequentially: CONFIRMED.
        byte[] data = BuildXeff(99u, 3u, 1u); // 3 sub-effects, 1 entry each
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(3, xeff.SubEffects.Length);
        for (int s = 0; s < 3; s++)
        {
            Assert.Equal(1u, xeff.SubEffects[s].EntryCount);
            Assert.Single(xeff.SubEffects[s].Keyframes);
        }
    }

    /// <summary>
    /// Regression test for the "truncated reading Group D scaleX values" crash
    /// that affected char_select-u.xeff (sub_effect_count=68) and
    /// zone_sel_u.xeff / zone_sel2-u.xeff (sub_effect_count=11).
    ///
    /// Root cause: the parser used an 8-byte header (wrong) and a completely different
    /// element structure (Group A/B/C/D/E/F from an obsolete spec). The corrected
    /// spec §A.2 has a 32-byte header and sub-effect blocks with entry_count + name
    /// table + 4 curve passes + 13-byte track header + keyframe array.
    ///
    /// This test builds a fixture with 68 sub-effects (matching char_select-u.xeff
    /// sub_effect_count) and verifies the parser completes without throwing.
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes, CORRECTED): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.4.5 — blocks follow sequentially: CONFIRMED.
    /// </summary>
    [Fact]
    public void Regression_LargeSubEffectCount68_ParsesWithoutException()
    {
        // Matches char_select-u.xeff sub_effect_count=68 (the problematic file).
        byte[] data = BuildXeff(effectId: 9999u, subEffectCount: 68u, entriesPerSubEffect: 5u);

        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(68u, xeff.SubEffectCount);
        Assert.Equal(68, xeff.SubEffects.Length);
        // Each sub-effect has 5 entries (= 5 keyframes: frame0 no-prefix + frames 1–4 with prefix)
        Assert.All(xeff.SubEffects, sub =>
        {
            Assert.Equal(5u, sub.EntryCount);
            Assert.Equal(5, sub.Keyframes.Length);
        });
    }

    /// <summary>
    /// Regression for zone_sel_u.xeff / zone_sel2-u.xeff (sub_effect_count=11).
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes): VERIFIED.
    /// </summary>
    [Fact]
    public void Regression_LargeSubEffectCount11_ParsesWithoutException()
    {
        // Matches zone_sel_u.xeff / zone_sel2-u.xeff sub_effect_count=11.
        byte[] data = BuildXeff(effectId: 8888u, subEffectCount: 11u, entriesPerSubEffect: 3u);

        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(11u, xeff.SubEffectCount);
        Assert.Equal(11, xeff.SubEffects.Length);
        Assert.All(xeff.SubEffects, sub => Assert.Equal(3, sub.Keyframes.Length));
    }

    // ─── tests: .eff effect-object shape ───────────────────────────────────────

    [Fact]
    public void ParseEff_ValidTriangle_CorrectIndexAndVertexCounts()
    {
        // Builds a minimal .eff with 3 indices (1 triangle) and 3 vertices.
        // spec: Docs/RE/formats/effects.md §B.2 File-size formula: 4+(3×2)+4+(3×32) = 4+6+4+96 = 110 bytes: VERIFIED.
        using var ms = new MemoryStream();
        ms.Write(Le4(3u)); // index_count = 3
        ms.Write(BitConverter.GetBytes((ushort)0)); // index[0]
        ms.Write(BitConverter.GetBytes((ushort)1)); // index[1]
        ms.Write(BitConverter.GetBytes((ushort)2)); // index[2]
        ms.Write(Le4(3u)); // vert_count = 3
        for (int v = 0; v < 3; v++)
        {
            // 8 × f32 = 32 bytes per vertex (pos, normal, uv)
            // spec: §B.4.1 VertexRecord (32 bytes): VERIFIED.
            for (int f = 0; f < 8; f++)
                ms.Write(Le4f((float)(v * 10 + f)));
        }

        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));

        Assert.Equal(3, shape.Indices.Length);
        Assert.Equal(3, shape.Vertices.Length);
    }

    [Fact]
    public void ParseEff_VertexRecord_PosAndUv_Decoded()
    {
        // Validates pos_x and tex_u from a single vertex.
        // spec: Docs/RE/formats/effects.md §B.4.1 — pos_x @ +0, tex_u @ +24: VERIFIED.
        using var ms = new MemoryStream();
        ms.Write(Le4(3u)); // index_count = 3 (1 triangle)
        ms.Write(BitConverter.GetBytes((ushort)0));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(BitConverter.GetBytes((ushort)2));
        ms.Write(Le4(3u)); // vert_count = 3
        // vertex 0: px=10, py=20, pz=30, nx=0, ny=1, nz=0, u=0.5, v=0.25
        ms.Write(Le4f(10f));
        ms.Write(Le4f(20f));
        ms.Write(Le4f(30f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(1f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0.5f));
        ms.Write(Le4f(0.25f));
        // vertices 1 and 2: zeros
        ms.Write(new byte[64]);

        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));

        EffVertex v0 = shape.Vertices[0];
        Assert.Equal(10f, v0.PosX, precision: 5); // spec: §B.4.1 pos_x @ +0: VERIFIED
        Assert.Equal(20f, v0.PosY, precision: 5);
        Assert.Equal(30f, v0.PosZ, precision: 5);
        Assert.Equal(0.5f, v0.TexU, precision: 5); // spec: §B.4.1 tex_u @ +24: VERIFIED
        Assert.Equal(0.25f, v0.TexV, precision: 5); // spec: §B.4.1 tex_v @ +28: VERIFIED
    }

    [Fact]
    public void ParseEff_Truncated_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/effects.md §B.3 — "must not read past the buffer": bound check.
        byte[] truncated = new byte[3]; // too short even for index_count
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseEff(new ReadOnlyMemory<byte>(truncated)));
    }

    // ─── tests: ReadOnlyMemory<byte> overloads ─────────────────────────────────

    [Fact]
    public void ParseXeff_ReadOnlyMemory_Overload_Works()
    {
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(1u, xeff.EffectId);
    }

    [Fact]
    public void ParseEff_ReadOnlyMemory_Overload_Works()
    {
        using var ms = new MemoryStream();
        ms.Write(Le4(3u));
        ms.Write(BitConverter.GetBytes((ushort)0));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(BitConverter.GetBytes((ushort)2));
        ms.Write(Le4(3u));
        ms.Write(new byte[96]); // 3 × 32-byte vertex records

        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));
        Assert.Equal(3, shape.Indices.Length);
    }

    // ─── tests: byte-math from spec §A.2 file-size formula ────────────────────

    [Fact]
    public void SingleSubEffect_N5_FileSize_MatchesSpecFormula()
    {
        // spec: Docs/RE/formats/effects.md §A.2 File-size formula (1 sub-effect, N entries, scale curves empty):
        //   32           — file header (§A.2 XEFF_HEADER_SIZE=32)
        //   + 4          — entry_count u32 that opens the sub-effect block (§A.4)
        //   + N×64       — name table (§A.4.1)
        //   + (4+N×4)    — alpha curve: count u32 + N×f32 (§A.4.2)
        //   + 3×4        — scaleX/Y/Z curve passes (count=0 each, only the count prefix) (§A.4.2)
        //   + 13         — track header (§A.4.3 / §A.14 XEFF_TRACK_HEADER_SIZE=13)
        //   + 9×4        — frame 0: 9×f32, NO index prefix (§A.4.4)
        //   + (N−1)×40   — frames 1..N-1: u32 kf_index + 9×f32 each (§A.4.4)
        // With N=5: 32 + 4 + 320 + 24 + 12 + 13 + 36 + 160 = 601 bytes.
        const int N = 5;
        int expectedSize = 32 // header
                           + 4 // entry_count u32 — spec: §A.4
                           + N * 64 // name table — spec: §A.4.1
                           + (4 + N * 4) // alpha curve — spec: §A.4.2
                           + 3 * 4 // scaleX/Y/Z count prefixes (count=0) — spec: §A.4.2
                           + 13 // track header — spec: §A.14 XEFF_TRACK_HEADER_SIZE=13
                           + 36 // frame 0: 9×f32 no-index — spec: §A.4.4
                           + (N - 1) * 40; // frames 1..N-1 — spec: §A.4.4

        byte[] data = BuildXeff(1u, 1u, (uint)N);
        Assert.Equal(expectedSize, data.Length);
    }

    // ─── tests: rotation calculation ───────────────────────────────────────────

    [Fact]
    public void Keyframe_Rotation_90DegY_CorrectQuat()
    {
        // 90° around Y: quat = (0, sin(45°), 0, cos(45°)) ≈ (0, 0.7071, 0, 0.7071).
        // spec: Docs/RE/formats/effects.md §A.7 — "π/180 × degrees; half-angle Euler-XYZ decomposition": CONFIRMED.
        // We build a custom fixture with rotYDeg=90 in frame 0.
        using var ms = new MemoryStream();

        // 32-byte header
        ms.Write(Le4(1u)); // effect_id
        ms.Write(Le4(1u)); // sub_effect_count
        ms.Write(Le4(1u)); // type_flag
        ms.Write(new byte[16]); // reserved
        ms.Write(Le4(1u)); // first_entry_count

        // sub-effect, N=1
        ms.Write(Le4(1u)); // entry_count
        ms.Write(new byte[64]); // name table (1×64)
        ms.Write(Le4(1u));
        ms.Write(Le4f(0.5f)); // alpha curve
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u)); // scale passes (count=0)

        // track header (13 bytes)
        ms.WriteByte(1);
        ms.Write(Le4(67u));
        ms.Write(Le4(100u));
        ms.Write(Le4(0u));

        // frame 0 (no index prefix): rotation = 0, 90, 0 degrees
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f)); // velocity
        ms.Write(Le4f(1f));
        ms.Write(Le4f(1f));
        ms.Write(Le4f(1f)); // size
        ms.Write(Le4f(0f));
        ms.Write(Le4f(90f));
        ms.Write(Le4f(0f)); // rot deg

        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray()));
        Quat q = xeff.SubEffects[0].Keyframes[0].Rotation;

        Assert.Equal(0f, q.X, precision: 4);
        Assert.Equal(MathF.Sin(MathF.PI / 4f), q.Y, precision: 4);
        Assert.Equal(0f, q.Z, precision: 4);
        Assert.Equal(MathF.Cos(MathF.PI / 4f), q.W, precision: 4);
    }
}