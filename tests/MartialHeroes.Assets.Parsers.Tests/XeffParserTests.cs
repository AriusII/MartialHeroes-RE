using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based regression tests for <see cref="XeffParser"/>.
/// All fixtures are built in-memory from scratch — no real game files required.
///
/// Layout rules encoded in these fixtures (confirmed against spec A.17 correction history):
///   File header: EXACTLY 8 bytes — effect_id u32 + sub_effect_count u32.
///   Every block (block 0 included): 24-byte element fixed head (6 × u32) + variable body.
///   Body: tex_count × 64 name table + 4 curve passes + 9-byte track header + keyframes.
///   Block 0 starts at file offset 0x08 (immediately after the 8-byte header) — NO special prefix.
///   Track header is 9 bytes (anim_loop u8 + anim_stride u32 + anim_base_time u32).
///   EVERY animated keyframe (including frame 0) has a u32 kf_index prefix + 9 × f32 = 40 bytes.
///
/// spec: Docs/RE/formats/effects.md §A.2 File Header (8 bytes): VERIFIED (2026-06-14 correction).
/// spec: Docs/RE/formats/effects.md §A.4.0 Element fixed head (24 bytes): CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): CONFIRMED (CAMPAIGN VFS-MASTERY).
/// spec: Docs/RE/formats/effects.md §A.4.5 — every block (block 0 included) uses the same read sequence: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.4.4 — EVERY animated frame (including frame 0) has u32 kf_index + 9×f32 = 40 bytes: CONFIRMED (CAMPAIGN VFS-MASTERY).
/// spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 9, XEFF_KEYFRAME_ONDISK_STRIDE = 40.
/// spec: Docs/RE/formats/effects.md §A.17 Correction history — header is 8 bytes, not 32.
/// </summary>
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
    /// File layout (8-byte header; every block uses the same 24-byte fixed head):
    ///   8-byte file header: effect_id u32 + sub_effect_count u32.
    ///   Blocks 0..N-1: 24-byte element fixed head + tex_count×64 names
    ///                  + 4 curve passes + 13-byte track header + keyframes.
    ///
    /// All blocks — including block 0 — are parsed by the same element read sequence.
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (8 bytes): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.4.0 Element fixed head (24 bytes): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.4.5 — every block (block 0 included): CONFIRMED.
    /// </summary>
    private static byte[] BuildXeff(
        uint effectId,
        uint subEffectCount,
        uint entriesPerSubEffect,
        uint emitterType = 0u,
        byte animLoop = 1)
    {
        using var ms = new MemoryStream();

        // ── 8-byte file header ───────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32le @ 0x00: VERIFIED.
        ms.Write(Le4(effectId)); // 0x00 effect_id
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count u32le @ 0x04: VERIFIED.
        ms.Write(Le4(subEffectCount)); // 0x04 sub_effect_count
        // Header ends here at 0x08; block 0 starts immediately.

        // ── sub-effect blocks 0..N-1 ─────────────────────────────────────────
        // Every block parsed by the same element read sequence (A.4.0 fixed head + body).
        // spec: Docs/RE/formats/effects.md §A.4.5 — blocks follow sequentially: CONFIRMED.
        for (uint s = 0; s < subEffectCount; s++)
        {
            WriteSubEffectBlock(ms, s, entriesPerSubEffect, emitterType, animLoop);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Writes one sub-effect block: 24-byte element fixed head (A.4.0) + body.
    /// Every block (block 0 included) uses this same layout.
    /// spec: Docs/RE/formats/effects.md §A.4.0 Element fixed head (24 bytes): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.4.5 — block 0 is NOT special: CONFIRMED.
    /// </summary>
    private static void WriteSubEffectBlock(
        MemoryStream ms, uint blockIndex, uint n,
        uint emitterType, byte animLoop)
    {
        // ── 24-byte element fixed head (A.4.0) ───────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.0 — emitter_type u32 @ element+0x00: CONFIRMED.
        ms.Write(Le4(emitterType)); // emitter_type
        // spec: Docs/RE/formats/effects.md §A.4.0 — resource_id u32 @ element+0x04: CONFIRMED.
        ms.Write(Le4(blockIndex + 100u)); // resource_id (arbitrary but distinct per block)
        // spec: Docs/RE/formats/effects.md §A.4.0 — anim_flag u32 @ element+0x08: CONFIRMED.
        ms.Write(Le4(1u)); // anim_flag = 1
        // spec: Docs/RE/formats/effects.md §A.4.0 — field_unknown_a u32 @ element+0x0C: UNRESOLVED.
        ms.Write(Le4(0u)); // field_unknown_a = 0
        // spec: Docs/RE/formats/effects.md §A.4.0 — element_dword2 u32 @ element+0x10: UNRESOLVED.
        ms.Write(Le4(0u)); // element_dword2 = 0
        // spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14: CONFIRMED.
        ms.Write(Le4(n)); // tex_count / entry count

        // ── Body: name table (tex_count × 64 bytes) ─────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.1 — tex_count × 64 bytes: CONFIRMED.
        ms.Write(new byte[(int)(n * 64)]);

        // ── Body: curve pass 1 (alpha, count=n) ─────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 1 alpha — own u32 prefix + count × f32: CONFIRMED.
        ms.Write(Le4(n));
        for (uint i = 0; i < n; i++) ms.Write(Le4f(0.5f));

        // ── Body: curve passes 2–4 (scale X/Y/Z, count=0) ───────────────────
        // spec: Docs/RE/formats/effects.md §A.4.2 Passes 2–4 scale X/Y/Z — own u32 prefix: CONFIRMED.
        ms.Write(Le4(0u)); // scaleX count=0
        ms.Write(Le4(0u)); // scaleY count=0
        ms.Write(Le4(0u)); // scaleZ count=0

        // ── Body: track header (9 bytes) ─────────────────────────────────────
        // CORRECTED CAMPAIGN VFS-MASTERY: track header is 9 bytes (no "unknown_constant").
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): CONFIRMED (CAMPAIGN VFS-MASTERY).
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 9.
        ms.WriteByte(animLoop); // anim_loop u8 @ +0
        ms.Write(Le4(469u)); // anim_stride u32 @ +1
        ms.Write(Le4(0u)); // anim_base_time u32 @ +5

        // ── Body: keyframes ──────────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array: CONFIRMED.
        if (n == 0) return;

        if (animLoop != 0)
        {
            // Animated path: EVERY frame has u32 kf_index + 9 × f32 = 40 bytes (including frame 0).
            // CORRECTED CAMPAIGN VFS-MASTERY: frame 0 is NOT a special no-index case.
            // spec: Docs/RE/formats/effects.md §A.4.4 — "every animated keyframe carries a u32 index prefix": CONFIRMED.
            // spec: Docs/RE/formats/effects.md §A.14 XEFF_KEYFRAME_ONDISK_STRIDE = 40 (u32 + 9×f32), every frame.
            for (uint k = 0; k < n; k++)
            {
                WriteNineFloats(ms, k,
                    velocityX: (float)(k == 0 ? 1u : k), velocityY: 0f, velocityZ: 0f,
                    sizeX: 1f, sizeY: 1f, sizeZ: 1f,
                    rotX: 0f, rotY: 0f, rotZ: 0f,
                    includeIndex: true); // ALL frames have an index prefix
            }
        }
        else
        {
            // Static path: exactly one entry.
            // emitter_type != 2: 6×f32 (24 B); emitter_type == 2: 9×f32 (36 B).
            // spec: Docs/RE/formats/effects.md §A.4.6 — static path: CONFIRMED.
            bool hasRotation = emitterType == 2u;
            ms.Write(Le4f(2f)); // velocity_x
            ms.Write(Le4f(3f)); // velocity_y
            ms.Write(Le4f(4f)); // velocity_z
            ms.Write(Le4f(5f)); // size_x
            ms.Write(Le4f(6f)); // size_y
            ms.Write(Le4f(7f)); // size_z
            if (hasRotation)
            {
                ms.Write(Le4f(10f)); // rot_x_deg
                ms.Write(Le4f(20f)); // rot_y_deg
                ms.Write(Le4f(30f)); // rot_z_deg
            }
        }
    }

    /// <summary>
    /// Writes a single keyframe's nine floats (with optional u32 index prefix).
    /// spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
    /// </summary>
    private static void WriteNineFloats(MemoryStream ms, uint kfIndex,
        float velocityX, float velocityY, float velocityZ,
        float sizeX, float sizeY, float sizeZ,
        float rotX, float rotY, float rotZ,
        bool includeIndex)
    {
        if (includeIndex)
            ms.Write(Le4(kfIndex));
        ms.Write(Le4f(velocityX));
        ms.Write(Le4f(velocityY));
        ms.Write(Le4f(velocityZ));
        ms.Write(Le4f(sizeX));
        ms.Write(Le4f(sizeY));
        ms.Write(Le4f(sizeZ));
        ms.Write(Le4f(rotX));
        ms.Write(Le4f(rotY));
        ms.Write(Le4f(rotZ));
    }

    // ─── tests: 8-byte header ──────────────────────────────────────────────────

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
    public void Header_Is8Bytes_Block0StartsAt0x08()
    {
        // The 8-byte file header ends at offset 0x08.
        // Block 0 starts immediately at 0x08 with a 24-byte element fixed head.
        // The byte at 0x08 is block 0's emitter_type, NOT a header type_flag.
        // spec: Docs/RE/formats/effects.md §A.2 — XEFF_HEADER_SIZE = 8 (0x08): VERIFIED.
        // spec: Docs/RE/formats/effects.md §A.17 — type_flag name RETIRED: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u, emitterType: 1u); // emitterType=1 (mesh)
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(1u, xeff.SubEffects[0].EmitterType); // NOT a header field
        Assert.Equal(1u, xeff.SubEffectCount);
    }

    // ─── tests: anti-magic ─────────────────────────────────────────────────────

    [Fact]
    public void AntiMagic_ThrowsInvalidDataException()
    {
        // effect_id == 0x46464558 → file invalid.
        // spec: Docs/RE/formats/effects.md §A.1 — XEFF_INVALID_MAGIC = 0x46464558: CONFIRMED.
        using var ms = new MemoryStream();
        ms.Write(Le4(0x46464558u)); // effect_id = anti-magic sentinel
        ms.Write(Le4(0u)); // sub_effect_count = 0
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray())));
    }

    [Fact]
    public void TruncatedHeader_ThrowsInvalidDataException()
    {
        // Buffer too short for the 8-byte header.
        // spec: Docs/RE/formats/effects.md §A.2 — header is 8 bytes: VERIFIED.
        byte[] truncated = new byte[4];
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(truncated)));
    }

    // ─── tests: stub (zero sub-effects) ────────────────────────────────────────

    [Fact]
    public void StubEffect_ZeroSubEffects_Parsed()
    {
        // sub_effect_count = 0 is valid (stub/empty effect). 8 bytes total.
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count = 0 valid: VERIFIED.
        byte[] data = BuildXeff(42u, 0u, 0u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(42u, xeff.EffectId);
        Assert.Equal(0u, xeff.SubEffectCount);
        Assert.Empty(xeff.SubEffects);
    }

    // ─── tests: element fixed head (A.4.0) ────────────────────────────────────

    [Fact]
    public void Block0_ElementFixedHead_EmitterType_Decoded()
    {
        // Block 0 element fixed head — emitter_type u32 @ element+0x00 = file offset 0x08.
        // spec: Docs/RE/formats/effects.md §A.4.0 — emitter_type u32 @ element+0x00: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u, emitterType: 2u); // directional billboard
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        // animLoop=1 in BuildXeff default: animated path, so tex_count=1 frame
        // But emitterType=2 + animLoop=1 → animated path (emitterType only matters in static branch)
        // We just assert EmitterType is decoded correctly.
        Assert.Equal(2u, xeff.SubEffects[0].EmitterType);
    }

    [Fact]
    public void Block0_ElementFixedHead_ResourceId_Decoded()
    {
        // resource_id u32 @ element+0x04.
        // spec: Docs/RE/formats/effects.md §A.4.0 — resource_id u32 @ element+0x04: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        // BuildXeff writes blockIndex+100 = 0+100 = 100 for block 0.
        Assert.Equal(100u, xeff.SubEffects[0].ResourceId);
    }

    [Fact]
    public void Block0_ElementFixedHead_AnimFlag_Decoded()
    {
        // anim_flag u32 @ element+0x08.
        // spec: Docs/RE/formats/effects.md §A.4.0 — anim_flag u32 @ element+0x08: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(1u, xeff.SubEffects[0].AnimFlag); // BuildXeff writes 1
    }

    [Fact]
    public void Block0_ElementFixedHead_FieldUnknownA_Decoded()
    {
        // field_unknown_a u32 @ element+0x0C.  Semantics UNRESOLVED per spec.
        // spec: Docs/RE/formats/effects.md §A.4.0 — field_unknown_a u32 @ element+0x0C: UNRESOLVED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(0u, xeff.SubEffects[0].FieldUnknownA); // BuildXeff writes 0
    }

    [Fact]
    public void Block0_ElementFixedHead_ElementDword2_Decoded()
    {
        // element_dword2 u32 @ element+0x10.  Role UNRESOLVED per spec.
        // spec: Docs/RE/formats/effects.md §A.4.0 — element_dword2 u32 @ element+0x10: UNRESOLVED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(0u, xeff.SubEffects[0].ElementDword2); // BuildXeff writes 0
    }

    [Fact]
    public void Block0_ElementFixedHead_EntryCount_Decoded()
    {
        // tex_count u32 @ element+0x14 — drives name table, keyframe count, curve count.
        // For block 0 this is the value at file offset 0x1C (formerly mislabelled first_entry_count).
        // spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.2 — "the value at file offset 0x1C is block 0's tex_count": CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 5u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(5u, xeff.SubEffects[0].EntryCount);
    }

    // ─── tests: name table (A.4.1) ────────────────────────────────────────────

    [Fact]
    public void NameTable_NullPadded_EmptyString()
    {
        // Null-padded 64-byte slots decoded as empty string when all bytes are 0.
        // spec: Docs/RE/formats/effects.md §A.4.1 — tex_name char[64] null-padded: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_TEX_NAME_LEN = 64 (0x40).
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal("", xeff.SubEffects[0].TextureNames[0]);
    }

    [Fact]
    public void NameTable_Length_MatchesEntryCount()
    {
        // Name table length == EntryCount.
        // spec: Docs/RE/formats/effects.md §A.4.1 — tex_count × 64 bytes: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 3u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(3, xeff.SubEffects[0].TextureNames.Length);
    }

    // ─── tests: curve section (A.4.2) ─────────────────────────────────────────

    [Fact]
    public void AlphaKeys_Decoded()
    {
        // Alpha curve pass 1: each value = 0.5f in BuildXeff.
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 1 alpha: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.6 Alpha Inversion Convention: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        float[] alpha = xeff.SubEffects[0].AlphaKeys;
        Assert.Equal(2, alpha.Length);
        Assert.Equal(0.5f, alpha[0], precision: 5);
        Assert.Equal(0.5f, alpha[1], precision: 5);
    }

    [Fact]
    public void ScaleCurves_EmptyWhenCountZero()
    {
        // All three scale curve passes have count=0 in BuildXeff.
        // spec: Docs/RE/formats/effects.md §A.4.2 Passes 2–4 scale X/Y/Z: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Empty(xeff.SubEffects[0].ScaleX);
        Assert.Empty(xeff.SubEffects[0].ScaleY);
        Assert.Empty(xeff.SubEffects[0].ScaleZ);
    }

    // ─── tests: track header (A.4.3) ──────────────────────────────────────────

    [Fact]
    public void TrackHeader_AllFields_Decoded()
    {
        // Track header is 9 bytes: anim_loop u8 @ +0, anim_stride u32 @ +1, anim_base_time u32 @ +5.
        // CORRECTED CAMPAIGN VFS-MASTERY: the "unknown_constant" field is DELETED (no read-site).
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): CONFIRMED (CAMPAIGN VFS-MASTERY).
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 9.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffSubEffect sub = xeff.SubEffects[0];
        Assert.Equal(1, sub.AnimLoop); // anim_loop = 1 in BuildXeff
        // UnknownConstant field no longer exists — it was REFUTED (spec A.4.3 / A.17).
        Assert.Equal(469u, sub.AnimStride); // 469 ms per frame (now at +1)
        Assert.Equal(0u, sub.AnimBaseTime); // base time = 0 ms (now at +5)
    }

    // ─── tests: animated keyframe array (A.4.4) ────────────────────────────────

    [Fact]
    public void AnimatedPath_Frame0_HasKfIndexPrefix_IndexIsZero()
    {
        // Frame 0 has a u32 kf_index prefix on disk (like all other frames). kf_index for frame 0 = 0.
        // CORRECTED CAMPAIGN VFS-MASTERY: frame 0 is NOT a special no-index case.
        // spec: Docs/RE/formats/effects.md §A.4.4 — "every animated keyframe carries a u32 index prefix": CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.14 XEFF_KEYFRAME_ONDISK_STRIDE = 40 — every frame incl. frame 0.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffKeyframe kf0 = xeff.SubEffects[0].Keyframes[0];
        // Frame 0 kf_index = 0 (written by BuildXeff).
        Assert.Equal(0u, kf0.KfIndex);
    }

    [Fact]
    public void AnimatedPath_Frame0_VelocityX_Decoded()
    {
        // Frame 0: velocity_x at position 1 of 9-float layout.
        // spec: Docs/RE/formats/effects.md §A.4.4 velocity_x @ position 1: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(1f, xeff.SubEffects[0].Keyframes[0].VelocityX, precision: 5);
    }

    [Fact]
    public void AnimatedPath_TwoEntries_BothFramesHaveKfIndex()
    {
        // ALL frames (0 and 1): u32 kf_index + 9 × f32 = 40 bytes each.
        // CORRECTED CAMPAIGN VFS-MASTERY: frame 0 is NOT a special no-index case.
        // spec: Docs/RE/formats/effects.md §A.4.4 — "every animated keyframe carries a u32 index prefix": CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffSubEffect sub = xeff.SubEffects[0];
        Assert.Equal(2, sub.Keyframes.Length);
        Assert.Equal(0u, sub.Keyframes[0].KfIndex); // frame 0: kf_index = 0 (written by BuildXeff)
        Assert.Equal(1u, sub.Keyframes[1].KfIndex); // frame 1: kf_index = 1 (written by BuildXeff)
        Assert.Equal(1f, sub.Keyframes[1].VelocityX, precision: 5); // velocityX = (float)k = 1.0
    }

    [Fact]
    public void AnimatedPath_NineFloatLayout_AllFieldsDecoded()
    {
        // The 9-float layout: velocity Vec3 (1–3) + size Vec3 (4–6) + rot degrees XYZ (7–9).
        // spec: Docs/RE/formats/effects.md §A.4.4 nine-float layout: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.8 velocity Vec3 and size Vec3 semantics: HIGH.
        using var ms = new MemoryStream();
        // 8-byte header
        ms.Write(Le4(55u)); // effect_id
        ms.Write(Le4(1u)); // sub_effect_count
        // element fixed head (24 bytes)
        ms.Write(Le4(0u)); // emitter_type = 0 (billboard)
        ms.Write(Le4(50u)); // resource_id
        ms.Write(Le4(1u)); // anim_flag
        ms.Write(Le4(0u)); // field_unknown_a
        ms.Write(Le4(0u)); // element_dword2
        ms.Write(Le4(1u)); // tex_count = 1
        // name table: 1 × 64 = 64 bytes (all zero)
        ms.Write(new byte[64]);
        // alpha curve: count=1, value=0.1
        ms.Write(Le4(1u));
        ms.Write(Le4f(0.1f));
        // scale curves: count=0 each
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        // track header (9 bytes): anim_loop=1, anim_stride=100, anim_base_time=0.
        // CORRECTED CAMPAIGN VFS-MASTERY: 9 bytes (no "unknown_constant").
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): CONFIRMED.
        ms.WriteByte(1);    // anim_loop @ +0
        ms.Write(Le4(100u)); // anim_stride @ +1
        ms.Write(Le4(0u));   // anim_base_time @ +5
        // frame 0: u32 kf_index + 9 × f32 = 40 bytes.
        // CORRECTED: frame 0 HAS an index prefix (kf_index = 0).
        // spec: Docs/RE/formats/effects.md §A.4.4 — "every animated frame has u32 kf_index prefix": CONFIRMED.
        ms.Write(Le4(0u)); // kf_index = 0
        ms.Write(Le4f(1.1f)); // velocity_x
        ms.Write(Le4f(2.2f)); // velocity_y
        ms.Write(Le4f(3.3f)); // velocity_z
        ms.Write(Le4f(4.4f)); // size_x
        ms.Write(Le4f(5.5f)); // size_y
        ms.Write(Le4f(6.6f)); // size_z
        ms.Write(Le4f(10f)); // rot_x_deg
        ms.Write(Le4f(20f)); // rot_y_deg
        ms.Write(Le4f(30f)); // rot_z_deg

        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray()));
        XeffKeyframe kf = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(1.1f, kf.VelocityX, precision: 5);
        Assert.Equal(2.2f, kf.VelocityY, precision: 5);
        Assert.Equal(3.3f, kf.VelocityZ, precision: 5);
        Assert.Equal(4.4f, kf.SizeX, precision: 5);
        Assert.Equal(5.5f, kf.SizeY, precision: 5);
        Assert.Equal(6.6f, kf.SizeZ, precision: 5);
        Assert.Equal(10f, kf.RotXDeg, precision: 5);
        Assert.Equal(20f, kf.RotYDeg, precision: 5);
        Assert.Equal(30f, kf.RotZDeg, precision: 5);
    }

    [Fact]
    public void AnimatedPath_KeyframeCount_EqualsEntryCount()
    {
        // In the animated path (anim_loop != 0), keyframe count == tex_count.
        // spec: Docs/RE/formats/effects.md §A.4.4 — animated path: tex_count keyframe entries: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 5u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(5, xeff.SubEffects[0].Keyframes.Length);
    }

    // ─── tests: static path (A.4.6) ───────────────────────────────────────────

    [Fact]
    public void StaticPath_EmitterType0_6Floats_NoRotation()
    {
        // Static path (anim_loop == 0) + emitter_type == 0: 6 × f32, rotation = 0.
        // spec: Docs/RE/formats/effects.md §A.4.6 — static path emitter_type 0/1: 6 × f32: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u, emitterType: 0u, animLoop: 0);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffSubEffect sub = xeff.SubEffects[0];
        Assert.Single(sub.Keyframes); // exactly one static entry
        XeffKeyframe kf = sub.Keyframes[0];
        Assert.Equal(2f, kf.VelocityX, precision: 5);
        Assert.Equal(3f, kf.VelocityY, precision: 5);
        Assert.Equal(4f, kf.VelocityZ, precision: 5);
        Assert.Equal(5f, kf.SizeX, precision: 5);
        Assert.Equal(6f, kf.SizeY, precision: 5);
        Assert.Equal(7f, kf.SizeZ, precision: 5);
        // No rotation read → rotation fields default to 0.
        Assert.Equal(0f, kf.RotXDeg, precision: 5);
        Assert.Equal(0f, kf.RotYDeg, precision: 5);
        Assert.Equal(0f, kf.RotZDeg, precision: 5);
    }

    [Fact]
    public void StaticPath_EmitterType1_6Floats_NoRotation()
    {
        // Static path + emitter_type == 1 (mesh-particle): also 6 × f32, no rotation.
        // spec: Docs/RE/formats/effects.md §A.4.6 — emitter_type 0 or 1: 6 × f32: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u, emitterType: 1u, animLoop: 0);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffKeyframe kf = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(0f, kf.RotXDeg, precision: 5);
        Assert.Equal(0f, kf.RotYDeg, precision: 5);
        Assert.Equal(0f, kf.RotZDeg, precision: 5);
    }

    [Fact]
    public void StaticPath_EmitterType2_9Floats_WithRotation()
    {
        // Static path + emitter_type == 2 (directional billboard): 9 × f32 including Euler rotation.
        // spec: Docs/RE/formats/effects.md §A.4.6 — emitter_type == 2: 9 × f32 (adds Euler XYZ): CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u, emitterType: 2u, animLoop: 0);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffKeyframe kf = xeff.SubEffects[0].Keyframes[0];
        // BuildXeff writes 10f/20f/30f for rot_x/y/z when hasRotation is true.
        Assert.Equal(10f, kf.RotXDeg, precision: 5);
        Assert.Equal(20f, kf.RotYDeg, precision: 5);
        Assert.Equal(30f, kf.RotZDeg, precision: 5);
        // velocity and size still decoded correctly.
        Assert.Equal(2f, kf.VelocityX, precision: 5);
        Assert.Equal(5f, kf.SizeX, precision: 5);
    }

    // ─── tests: multiple sub-effects ───────────────────────────────────────────

    [Fact]
    public void MultipleSubEffects_EachParsedByElementReadSequence()
    {
        // Every block — block 0 included — is parsed by the same element read sequence.
        // No block has a special prefix; each carries its own 24-byte element fixed head.
        // spec: Docs/RE/formats/effects.md §A.4.5 — blocks follow sequentially: CONFIRMED.
        byte[] data = BuildXeff(99u, 3u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(3, xeff.SubEffects.Length);
        for (int s = 0; s < 3; s++)
        {
            Assert.Equal(1u, xeff.SubEffects[s].EntryCount);
            Assert.Single(xeff.SubEffects[s].Keyframes);
        }
    }

    [Fact]
    public void MultipleSubEffects_ResourceId_DistinctPerBlock()
    {
        // Each block gets resourceId = blockIndex + 100, so they are distinct.
        // spec: Docs/RE/formats/effects.md §A.4.0 — resource_id u32 @ element+0x04: CONFIRMED.
        byte[] data = BuildXeff(7u, 3u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(100u, xeff.SubEffects[0].ResourceId); // 0 + 100
        Assert.Equal(101u, xeff.SubEffects[1].ResourceId); // 1 + 100
        Assert.Equal(102u, xeff.SubEffects[2].ResourceId); // 2 + 100
    }

    [Fact]
    public void MultipleSubEffects_AnimFlag_DecodedForEachBlock()
    {
        // anim_flag written as 1 in every block by BuildXeff.
        // spec: Docs/RE/formats/effects.md §A.4.0 — anim_flag u32 @ element+0x08: CONFIRMED.
        byte[] data = BuildXeff(5u, 4u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.All(xeff.SubEffects, sub => Assert.Equal(1u, sub.AnimFlag));
    }

    // ─── tests: regression fixtures (real .xeff dimensions) ───────────────────

    /// <summary>
    /// Regression fixture for char_select-u.xeff (68 sub-effects, block-0 tex_count=2).
    /// Block 0's tex_count comes from its own element fixed head at element+0x14.
    /// spec: Docs/RE/formats/effects.md §A.15 — char_select-u.xeff regression (68 sub-effects, tex_count=2): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.4.5 — block 0 is parsed by the same element read sequence: CONFIRMED.
    /// </summary>
    [Fact]
    public void Regression_CharSelectU_68SubEffects_TexCount2_ParsesWithoutException()
    {
        byte[] data = BuildXeff(effectId: 380003000u, subEffectCount: 68u, entriesPerSubEffect: 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(68u, xeff.SubEffectCount);
        Assert.Equal(68, xeff.SubEffects.Length);
        // All blocks have tex_count=2 (from their own element fixed head).
        Assert.Equal(2u, xeff.SubEffects[0].EntryCount); // block 0
        Assert.Equal(2u, xeff.SubEffects[1].EntryCount); // block 1
        Assert.Equal(2u, xeff.SubEffects[67].EntryCount); // last block
        // Each block has 2 keyframes (animated path with tex_count=2).
        Assert.All(xeff.SubEffects, sub => Assert.Equal(2, sub.Keyframes.Length));
    }

    /// <summary>
    /// Regression fixture for zone_sel_u.xeff (11 sub-effects, block-0 tex_count=20).
    /// spec: Docs/RE/formats/effects.md §A.15 — zone_sel_u.xeff regression (11 sub-effects, tex_count=20): CONFIRMED.
    /// </summary>
    [Fact]
    public void Regression_ZoneSelU_11SubEffects_TexCount20_ParsesWithoutException()
    {
        byte[] data = BuildXeff(effectId: 380000000u, subEffectCount: 11u, entriesPerSubEffect: 20u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(11u, xeff.SubEffectCount);
        Assert.Equal(11, xeff.SubEffects.Length);
        Assert.Equal(20u, xeff.SubEffects[0].EntryCount);
        Assert.Equal(20u, xeff.SubEffects[1].EntryCount);
        Assert.All(xeff.SubEffects, sub =>
        {
            Assert.Equal(20, sub.Keyframes.Length);
            Assert.Equal(20, sub.AlphaKeys.Length);
            Assert.Equal(20, sub.TextureNames.Length);
        });
    }

    /// <summary>
    /// Byte-math verification: 1 sub-effect, N=5 entries, animated path, scale curves empty.
    /// File-size formula per spec A.2 (CORRECTED CAMPAIGN VFS-MASTERY):
    ///   8                   header (effect_id + sub_effect_count)
    ///   + 24                element fixed head (A.4.0): 6 × u32
    ///   + N×64              name table (A.4.1)
    ///   + (4 + N×4)         alpha curve (A.4.2)
    ///   + 3×4               scaleX/Y/Z count-only prefixes (A.4.2)
    ///   + 9                 track header (A.4.3) — CORRECTED: 9 bytes (no unknown_constant)
    ///   + N×(4+9×4)         N keyframes, each u32 kf_index + 9×f32 = 40 bytes (ALL frames incl. frame 0)
    /// With N=5: 8 + 24 + 320 + 24 + 12 + 9 + 5×40 = 597 bytes.
    /// (Same total as the old formula because the 4 bytes dropped from the track header
    ///  are now the frame-0 kf_index prefix — the total is unchanged.)
    /// spec: Docs/RE/formats/effects.md §A.2 File-size formula: VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_HEADER_SIZE = 8 (0x08): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_ELEMENT_FIXED_HEAD = 24 (0x18): CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 9: CONFIRMED (CAMPAIGN VFS-MASTERY).
    /// spec: Docs/RE/formats/effects.md §A.14 XEFF_KEYFRAME_ONDISK_STRIDE = 40: CONFIRMED (CAMPAIGN VFS-MASTERY).
    /// </summary>
    [Fact]
    public void FileSizeFormula_N5_SingleSubEffect_MatchesSpec()
    {
        const int N = 5;
        int expectedSize =
            8 // header (A.2 XEFF_HEADER_SIZE = 8)
            + 24 // element fixed head (A.4.0 XEFF_ELEMENT_FIXED_HEAD = 24)
            + N * 64 // name table (A.4.1 XEFF_TEX_NAME_LEN = 64 per entry)
            + (4 + N * 4) // alpha curve (A.4.2)
            + 3 * 4 // scaleX/Y/Z count prefixes (A.4.2)
            + 9 // track header (A.4.3 XEFF_TRACK_HEADER_SIZE = 9, CORRECTED)
            + N * (4 + 36); // N keyframes each 40 bytes: u32 kf_index + 9×f32 (ALL frames, CORRECTED)

        byte[] data = BuildXeff(1u, 1u, (uint)N);
        Assert.Equal(expectedSize, data.Length);
    }

    [Fact]
    public void FileSizeFormula_N5_TwoSubEffects_MatchesSpec()
    {
        // Two sub-effects, each N=5 entries: the element fixed head is part of each block.
        const int N = 5;
        int blockSize =
            24 // element fixed head (A.4.0)
            + N * 64 // name table (A.4.1)
            + (4 + N * 4) // alpha curve (A.4.2)
            + 3 * 4 // scale curves count prefixes (A.4.2)
            + 9 // track header (A.4.3 CORRECTED: 9 bytes)
            + N * (4 + 36); // N keyframes each 40 bytes (ALL frames incl. frame 0, CORRECTED)
        int expectedSize = 8 + 2 * blockSize; // 8-byte header + 2 identical blocks

        byte[] data = BuildXeff(1u, 2u, (uint)N);
        Assert.Equal(expectedSize, data.Length);
    }

    // ─── tests: rotation calculation (A.7) ────────────────────────────────────

    [Fact]
    public void Keyframe_Rotation_90DegY_CorrectQuat()
    {
        // 90° around Y axis: quat = (0, sin(45°), 0, cos(45°)) ≈ (0, 0.7071, 0, 0.7071).
        // spec: Docs/RE/formats/effects.md §A.7 — "π/180 × degrees; half-angle Euler-XYZ decomposition": CONFIRMED.
        using var ms = new MemoryStream();

        // 8-byte header
        ms.Write(Le4(1u)); // effect_id
        ms.Write(Le4(1u)); // sub_effect_count = 1

        // element fixed head (24 bytes)
        ms.Write(Le4(0u)); // emitter_type = 0 (billboard)
        ms.Write(Le4(10u)); // resource_id
        ms.Write(Le4(1u)); // anim_flag = 1
        ms.Write(Le4(0u)); // field_unknown_a
        ms.Write(Le4(0u)); // element_dword2
        ms.Write(Le4(1u)); // tex_count = 1

        // name table: 1 × 64 bytes (all zero)
        ms.Write(new byte[64]);

        // alpha curve: count=1, value=0.5
        ms.Write(Le4(1u));
        ms.Write(Le4f(0.5f));

        // scale curves: count=0 each
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));

        // track header (9 bytes): anim_loop=1, anim_stride=100, anim_base_time=0.
        // CORRECTED: 9 bytes (no "unknown_constant").
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes): CONFIRMED.
        ms.WriteByte(1);     // anim_loop @ +0
        ms.Write(Le4(100u)); // anim_stride @ +1
        ms.Write(Le4(0u));   // anim_base_time @ +5

        // frame 0: u32 kf_index + 9 × f32 = 40 bytes.
        // CORRECTED: frame 0 HAS a kf_index prefix (= 0).
        // spec: Docs/RE/formats/effects.md §A.4.4 — "every animated frame has u32 kf_index prefix": CONFIRMED.
        ms.Write(Le4(0u)); // kf_index = 0
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f)); // velocity_x/y/z
        ms.Write(Le4f(1f));
        ms.Write(Le4f(1f));
        ms.Write(Le4f(1f)); // size_x/y/z
        ms.Write(Le4f(0f));
        ms.Write(Le4f(90f));
        ms.Write(Le4f(0f)); // rot_x/y/z deg

        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray()));
        Quat q = xeff.SubEffects[0].Keyframes[0].Rotation;

        Assert.Equal(0f, q.X, precision: 4);
        Assert.Equal(MathF.Sin(MathF.PI / 4f), q.Y, precision: 4);
        Assert.Equal(0f, q.Z, precision: 4);
        Assert.Equal(MathF.Cos(MathF.PI / 4f), q.W, precision: 4);
    }

    [Fact]
    public void Keyframe_Rotation_Identity_ZeroDegrees()
    {
        // Zero rotation: quat = (0, 0, 0, 1) — identity.
        // spec: Docs/RE/formats/effects.md §A.7 — identity quaternion (0,0,0,1) before conversion: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u); // all rot degrees = 0 in BuildXeff frame 0
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Quat q = xeff.SubEffects[0].Keyframes[0].Rotation;
        Assert.Equal(0f, q.X, precision: 5);
        Assert.Equal(0f, q.Y, precision: 5);
        Assert.Equal(0f, q.Z, precision: 5);
        Assert.Equal(1f, q.W, precision: 5);
    }

    // ─── tests: velocity/size Vec3 convenience properties ─────────────────────

    [Fact]
    public void Keyframe_Velocity_Vec3_MatchesComponents()
    {
        // spec: Docs/RE/formats/effects.md §A.8 velocity Vec3 semantics: HIGH.
        using var ms = new MemoryStream();
        ms.Write(Le4(1u));
        ms.Write(Le4(1u));
        // element fixed head
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(1u)); // tex_count = 1
        ms.Write(new byte[64]); // name table
        ms.Write(Le4(0u)); // alpha count=0
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u)); // scale counts
        // track header (9 bytes — CORRECTED)
        ms.WriteByte(1);     // anim_loop @ +0
        ms.Write(Le4(100u)); // anim_stride @ +1
        ms.Write(Le4(0u));   // anim_base_time @ +5
        // frame 0: u32 kf_index + 9×f32 (CORRECTED — frame 0 has index prefix)
        ms.Write(Le4(0u)); // kf_index = 0
        ms.Write(Le4f(9f));
        ms.Write(Le4f(8f));
        ms.Write(Le4f(7f)); // velocity
        ms.Write(Le4f(6f));
        ms.Write(Le4f(5f));
        ms.Write(Le4f(4f)); // size
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f)); // rot

        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray()));
        XeffKeyframe kf = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(kf.VelocityX, kf.Velocity.X);
        Assert.Equal(kf.VelocityY, kf.Velocity.Y);
        Assert.Equal(kf.VelocityZ, kf.Velocity.Z);
        Assert.Equal(9f, kf.Velocity.X, precision: 5);
    }

    [Fact]
    public void Keyframe_Size_Vec3_MatchesComponents()
    {
        // spec: Docs/RE/formats/effects.md §A.8 size Vec3 semantics: HIGH.
        using var ms = new MemoryStream();
        ms.Write(Le4(1u));
        ms.Write(Le4(1u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(1u)); // tex_count
        ms.Write(new byte[64]);
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        // track header (9 bytes — CORRECTED)
        ms.WriteByte(1);     // anim_loop @ +0
        ms.Write(Le4(100u)); // anim_stride @ +1
        ms.Write(Le4(0u));   // anim_base_time @ +5
        // frame 0: u32 kf_index + 9×f32 (CORRECTED — frame 0 has index prefix)
        ms.Write(Le4(0u)); // kf_index = 0
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f)); // velocity
        ms.Write(Le4f(3f));
        ms.Write(Le4f(4f));
        ms.Write(Le4f(5f)); // size
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0f));

        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray()));
        XeffKeyframe kf = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(kf.SizeX, kf.Size.X);
        Assert.Equal(kf.SizeY, kf.Size.Y);
        Assert.Equal(kf.SizeZ, kf.Size.Z);
        Assert.Equal(3f, kf.Size.X, precision: 5);
    }

    // ─── tests: truncation safety ──────────────────────────────────────────────

    [Fact]
    public void TruncatedAtElementFixedHead_ThrowsInvalidDataException()
    {
        // 8-byte header present but no room for the 24-byte element fixed head.
        using var ms = new MemoryStream();
        ms.Write(Le4(1u)); // effect_id
        ms.Write(Le4(1u)); // sub_effect_count = 1
        ms.Write(new byte[8]); // only 8 bytes of the 24-byte fixed head
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray())));
    }

    [Fact]
    public void TruncatedAtNameTable_ThrowsInvalidDataException()
    {
        // Full element fixed head but name table is cut short.
        using var ms = new MemoryStream();
        ms.Write(Le4(1u)); // effect_id
        ms.Write(Le4(1u)); // sub_effect_count = 1
        // element fixed head (24 bytes)
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(5u)); // tex_count = 5 → needs 5×64=320 bytes
        ms.Write(new byte[32]); // only 32 of 320 bytes
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray())));
    }

    [Fact]
    public void TruncatedAtKeyframes_ThrowsInvalidDataException()
    {
        // Complete up to track header, but keyframe data is missing.
        using var ms = new MemoryStream();
        ms.Write(Le4(1u)); // effect_id
        ms.Write(Le4(1u)); // sub_effect_count = 1
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(2u)); // tex_count = 2
        ms.Write(new byte[128]); // name table 2×64
        ms.Write(Le4(0u)); // alpha count=0
        ms.Write(Le4(0u));
        ms.Write(Le4(0u));
        ms.Write(Le4(0u)); // scale counts
        // track header (9 bytes — CORRECTED: no unknown_constant)
        ms.WriteByte(1);     // anim_loop @ +0  spec: Docs/RE/formats/effects.md §A.14 XEFF_TRACK_HEADER_SIZE = 9
        ms.Write(Le4(100u)); // anim_stride @ +1
        ms.Write(Le4(0u));   // anim_base_time @ +5
        // missing: frame 0 (40 bytes = u32 kf_index + 9×f32) + frame 1 (40 bytes) — triggers truncation
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray())));
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
    public void ParseXeff_ReadOnlySpan_Overload_Works()
    {
        byte[] data = BuildXeff(2u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlySpan<byte>(data));
        Assert.Equal(2u, xeff.EffectId);
    }

    // ─── tests: .eff effect-object shape (Section B) ──────────────────────────

    [Fact]
    public void ParseEff_ValidTriangle_CorrectIndexAndVertexCounts()
    {
        // spec: Docs/RE/formats/effects.md §B.2 File-size formula: 4+(3×2)+4+(3×32)=110 bytes: VERIFIED.
        using var ms = new MemoryStream();
        ms.Write(Le4(3u)); // index_count = 3
        ms.Write(BitConverter.GetBytes((ushort)0));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(BitConverter.GetBytes((ushort)2));
        ms.Write(Le4(3u)); // vert_count = 3
        for (int v = 0; v < 3; v++)
        for (int f = 0; f < 8; f++)
            ms.Write(Le4f((float)(v * 10 + f)));

        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));
        Assert.Equal(3, shape.Indices.Length);
        Assert.Equal(3, shape.Vertices.Length);
    }

    [Fact]
    public void ParseEff_VertexRecord_AllFields_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §B.4.1 VertexRecord (32 bytes): VERIFIED.
        // pos_x/y/z @ +0/+4/+8; normal_x/y/z @ +12/+16/+20; tex_u @ +24, tex_v @ +28.
        using var ms = new MemoryStream();
        ms.Write(Le4(3u));
        ms.Write(BitConverter.GetBytes((ushort)0));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(BitConverter.GetBytes((ushort)2));
        ms.Write(Le4(3u)); // vert_count = 3
        // vertex 0
        ms.Write(Le4f(10f)); // pos_x
        ms.Write(Le4f(20f)); // pos_y
        ms.Write(Le4f(30f)); // pos_z
        ms.Write(Le4f(0f)); // normal_x
        ms.Write(Le4f(1f)); // normal_y
        ms.Write(Le4f(0f)); // normal_z
        ms.Write(Le4f(0.5f)); // tex_u
        ms.Write(Le4f(0.25f)); // tex_v
        ms.Write(new byte[64]); // vertices 1 and 2 (2 × 32 = 64 bytes)

        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));
        EffVertex v0 = shape.Vertices[0];
        Assert.Equal(10f, v0.PosX, precision: 5);
        Assert.Equal(20f, v0.PosY, precision: 5);
        Assert.Equal(30f, v0.PosZ, precision: 5);
        Assert.Equal(0f, v0.NormalX, precision: 5);
        Assert.Equal(1f, v0.NormalY, precision: 5);
        Assert.Equal(0f, v0.NormalZ, precision: 5);
        Assert.Equal(0.5f, v0.TexU, precision: 5);
        Assert.Equal(0.25f, v0.TexV, precision: 5);
    }

    [Fact]
    public void ParseEff_Truncated_ThrowsInvalidDataException()
    {
        // spec: Docs/RE/formats/effects.md §B.3 — buffer too short for index_count: VERIFIED.
        byte[] truncated = new byte[3];
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseEff(new ReadOnlyMemory<byte>(truncated)));
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
        ms.Write(new byte[96]);
        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));
        Assert.Equal(3, shape.Indices.Length);
    }

    // ─── tests: large sub-effect counts (library regression) ──────────────────

    [Fact]
    public void Regression_LargeSubEffectCount68_ParsesWithoutException()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count observed range 0–68: VERIFIED.
        byte[] data = BuildXeff(effectId: 9999u, subEffectCount: 68u, entriesPerSubEffect: 5u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(68u, xeff.SubEffectCount);
        Assert.Equal(68, xeff.SubEffects.Length);
        Assert.All(xeff.SubEffects, sub =>
        {
            Assert.Equal(5u, sub.EntryCount);
            Assert.Equal(5, sub.Keyframes.Length);
        });
    }

    [Fact]
    public void Regression_LargeSubEffectCount11_ParsesWithoutException()
    {
        byte[] data = BuildXeff(effectId: 8888u, subEffectCount: 11u, entriesPerSubEffect: 3u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(11u, xeff.SubEffectCount);
        Assert.Equal(11, xeff.SubEffects.Length);
        Assert.All(xeff.SubEffects, sub => Assert.Equal(3, sub.Keyframes.Length));
    }

    // ─── live VFS integration test (skipped if clientdata absent) ─────────────

    /// <summary>
    /// Real-file integration: extracts char_select-u.xeff from the VFS and parses it
    /// via the actual XeffParser. Validates byte-exact known values from the file header
    /// and block 0 (confirmed by hexdump trace on 2026-06-14).
    ///
    /// Skipped when the VFS pair (data.inf + data/data.vfs) is absent — the test suite
    /// must remain runnable without real game files (mandated design constraint).
    ///
    /// spec: Docs/RE/formats/effects.md §A.15 — char_select-u.xeff: effect_id=380003000,
    ///   sub_effect_count=68, file_size=75372 bytes; zero residual after parsing all 68 blocks.
    ///   Block 0: emitter_type=1 (mesh-particle), tex_count=2, anim_loop=1, anim_stride=0,
    ///   texture name[0]="lflare-l-yellow-01".
    ///   All confirmed by hexdump trace (2026-06-14).
    /// spec: Docs/RE/formats/pak.md — VFS layout (name@+0 100B, pad@+100 4B, dataOffset i64@+104,
    ///   dataSize i64@+112). CONFIRMED.
    /// </summary>
    [Fact]
    public void LiveVfs_CharSelectU_ParsesCorrectly_IfVfsAvailable()
    {
        // Resolve the VFS pair using the same priority order as ClientPathResolver:
        // project-local clientdata/ → D:/MartialHeroesClient (no env/cfg scan in tests).
        // spec: Docs/RE/formats/pak.md — data.inf + data/data.vfs pair: CONFIRMED.
        const string RelInf = @"05.Presentation/MartialHeroes.Client.Godot/clientdata/data.inf";
        const string RelVfs = @"05.Presentation/MartialHeroes.Client.Godot/clientdata/data/data.vfs";

        // Try to find the repo root (go up from the test assembly's location).
        string? repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        string? infPath = null;
        string? vfsPath = null;

        if (repoRoot != null)
        {
            string candidate = Path.Combine(repoRoot, RelInf);
            if (File.Exists(candidate))
            {
                infPath = candidate;
                vfsPath = Path.Combine(repoRoot, RelVfs);
            }
        }

        // Fallback: D:/MartialHeroesClient
        if (infPath == null)
        {
            const string ExtInf = @"D:\MartialHeroesClient\data.inf";
            const string ExtVfs = @"D:\MartialHeroesClient\data\data.vfs";
            if (File.Exists(ExtInf))
            {
                infPath = ExtInf;
                vfsPath = ExtVfs;
            }
        }

        if (infPath == null || vfsPath == null || !File.Exists(infPath) || !File.Exists(vfsPath!))
        {
            // VFS not present in this environment — skip gracefully.
            // spec: Design constraint — no real .pak required for unit tests.
            return;
        }

        // Minimal inline VFS lookup (replicates VfsDirectory.cs / VfsEntry.cs logic).
        // spec: Docs/RE/formats/pak.md — header 24 bytes, entry_count @ +12 u32 LE: CONFIRMED.
        // spec: Docs/RE/formats/pak.md — record 144 bytes (name[100] @ +0, pad @ +100, dataOffset i64 @ +104, dataSize i64 @ +112): CONFIRMED.
        byte[] infBytes = File.ReadAllBytes(infPath);
        uint entryCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(infBytes.AsSpan(12, 4));

        const int HeaderSize = 24;
        const int RecordSize = 144;
        const int NameCap = 100;
        const string TargetName = "data/effect/xeff/char_select-u.xeff";

        long dataOffset = -1, dataSize = -1;
        for (int i = 0; i < (int)entryCount; i++)
        {
            int rOff = HeaderSize + i * RecordSize;
            var nameSpan = infBytes.AsSpan(rOff, NameCap);
            int nullIdx = nameSpan.IndexOf((byte)0);
            string name = System.Text.Encoding.ASCII
                .GetString(nullIdx >= 0 ? nameSpan[..nullIdx] : nameSpan)
                .ToLowerInvariant();
            if (name != TargetName) continue;
            dataOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(infBytes.AsSpan(rOff + 104, 8));
            dataSize = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(infBytes.AsSpan(rOff + 112, 8));
            break;
        }

        if (dataOffset < 0)
        {
            // File absent from this VFS build — skip.
            return;
        }

        // Extract raw bytes from data.vfs.
        byte[] raw = new byte[dataSize];
        using (var vfs = File.OpenRead(vfsPath!))
        {
            vfs.Seek(dataOffset, SeekOrigin.Begin);
            vfs.ReadExactly(raw);
        }

        // ─── Parse and assert known byte-exact values ────────────────────────────
        // All values confirmed by hexdump trace on 2026-06-14 (see dirty-room notes).
        // spec: Docs/RE/formats/effects.md §A.15 — char_select-u.xeff byte-confirmed values: VERIFIED.
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(raw));

        // File-level header assertions.
        // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32 @ 0x00, sub_effect_count u32 @ 0x04: VERIFIED.
        Assert.Equal(380003000u, xeff.EffectId); // 0x16A662B8 in LE bytes b8 62 a6 16
        Assert.Equal(68u, xeff.SubEffectCount); // 0x00000044 in LE bytes 44 00 00 00
        Assert.Equal(75372, raw.Length); // file size CONFIRMED by extraction
        Assert.Equal(68, xeff.SubEffects.Length);

        // Block 0 assertions (element fixed head @ file offset 0x08).
        // spec: Docs/RE/formats/effects.md §A.4.0 Element fixed head (24 bytes): CONFIRMED.
        XeffSubEffect b0 = xeff.SubEffects[0];
        Assert.Equal(1u, b0.EmitterType); // emitter_type = 1 (mesh-particle) @ 0x08: CONFIRMED
        Assert.Equal(0u, b0.ResourceId); // resource_id = 0 @ 0x0C: CONFIRMED
        Assert.Equal(0u, b0.AnimFlag); // anim_flag = 0 @ 0x10: CONFIRMED
        Assert.Equal(2u, b0.EntryCount); // tex_count = 2 @ 0x1C: CONFIRMED
        Assert.Equal(2, b0.TextureNames.Length);
        // spec: Docs/RE/formats/effects.md §A.4.1 Name table @ 0x20: CONFIRMED.
        Assert.Equal("lflare-l-yellow-01", b0.TextureNames[0]); // name[0] @ 0x20: CONFIRMED
        Assert.Equal("lflare-l-yellow-01", b0.TextureNames[1]); // name[1] @ 0x60: CONFIRMED

        // Block 0 curve section.
        // spec: Docs/RE/formats/effects.md §A.4.2 Curve section: CONFIRMED.
        Assert.Equal(2, b0.AlphaKeys.Length); // alpha count = 2 @ 0xA0: CONFIRMED
        Assert.Equal(2, b0.ScaleX.Length); // scaleX count = 2 @ 0xAC: CONFIRMED
        Assert.Equal(2, b0.ScaleY.Length); // scaleY count = 2 @ 0xB8: CONFIRMED
        Assert.Equal(2, b0.ScaleZ.Length); // scaleZ count = 2 @ 0xC4: CONFIRMED

        // Block 0 track header.
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (9 bytes) @ 0xD0: CONFIRMED (CAMPAIGN VFS-MASTERY).
        // CORRECTED: unknown_constant field REFUTED (no read-site) — spec §A.17.
        Assert.Equal(1, b0.AnimLoop); // anim_loop = 1 (animated) @ 0xD0: CONFIRMED
        Assert.Equal(67u, b0.AnimStride); // anim_stride = 67 ms @ 0xD1 (the live value formerly misread as "unknown_constant"; 9-byte header): CONFIRMED (CAMPAIGN VFS-MASTERY)
        Assert.Equal(0u, b0.AnimBaseTime); // anim_base_time = 0 ms @ 0xD5: CONFIRMED (9-byte header)

        // Block 0 keyframes: animated path (anim_loop=1), tex_count=2 → 2 keyframes.
        // spec: Docs/RE/formats/effects.md §A.4.4 — animated: tex_count keyframes, ALL have u32 kf_index prefix: CONFIRMED.
        Assert.Equal(2, b0.Keyframes.Length);
        Assert.Equal(0u, b0.Keyframes[0].KfIndex); // frame 0: u32 kf_index prefix present, value=0: CONFIRMED (CAMPAIGN VFS-MASTERY)

        // Block 65 (emitter_type=2, directional billboard, tex_count=21).
        // spec: Docs/RE/formats/effects.md §A.4.6 emitter_type==2 + animated → same as emitter_type 0/1: CONFIRMED.
        XeffSubEffect b65 = xeff.SubEffects[65];
        Assert.Equal(2u, b65.EmitterType); // emitter_type=2 (directional billboard): CONFIRMED
        Assert.Equal(21u, b65.EntryCount); // tex_count=21: CONFIRMED
        Assert.Equal(1, b65.AnimLoop); // animated path: CONFIRMED
        Assert.Equal(21, b65.Keyframes.Length);
    }

    /// <summary>
    /// Walks up the directory tree from <paramref name="startDir"/> to find the repo root
    /// (identified by the presence of MartialHeroes.slnx).
    /// Returns null if not found within 8 levels.
    /// </summary>
    private static string? FindRepoRoot(string startDir)
    {
        string? dir = startDir;
        for (int i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "MartialHeroes.slnx")))
                return dir;
        }

        return null;
    }
}