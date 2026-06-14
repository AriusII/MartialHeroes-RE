using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based regression tests for <see cref="XeffParser"/>.
/// All fixtures are built in-memory from scratch — no real game files required.
///
/// Layout rules encoded in these fixtures (confirmed against real bytes):
///   Block 0 : NO prefix — entry_count comes from file header first_entry_count @ 0x1C.
///   Blocks 1..N-1 : 24-byte prefix = u32 sub_id + u32[4] zeros + u32 entry_count.
///   Each block body: entry_count×64 names + 4×(u32+count×f32) curves + 13-byte track + keyframes.
///
/// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes): VERIFIED.
/// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free; blocks[1..N-1] 24-byte prefix: CONFIRMED.
/// spec: Docs/RE/formats/effects.md §A.4.4 — Frame 0: no index prefix; frames 1..N-1: u32+9×f32: CONFIRMED.
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
    /// File layout:
    ///   32-byte file header
    ///   Block 0  : entry_count×64 names + 4 curve passes + 13-byte track + keyframes (NO prefix).
    ///   Blocks 1..N-1 : 24-byte prefix (sub_id + 4×u32 zeros + entry_count) + same body.
    ///
    /// spec: Docs/RE/formats/effects.md §A.2 File Header (32 bytes): VERIFIED.
    /// spec: Docs/RE/formats/effects.md §A.4 Sub-Effect Block Structure: CONFIRMED.
    /// spec: Docs/RE/formats/effects.md §A.15 — block[0] no prefix, blocks[1..N-1] 24-byte prefix: CONFIRMED.
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

        // ── sub-effect blocks ────────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.5 — blocks follow sequentially: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free; blocks[1..N-1] 24-byte prefix: CONFIRMED.
        for (uint s = 0; s < subEffectCount; s++)
        {
            WriteSubEffectBlock(ms, s, entriesPerSubEffect);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Writes one sub-effect block.
    /// Block 0 (blockIndex==0): NO prefix — body starts immediately with name table.
    /// Blocks 1..N-1: 24-byte prefix (sub_id u32 + four zero u32s + entry_count u32) then body.
    /// spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free; blocks[1..N-1] 24-byte prefix: CONFIRMED.
    /// </summary>
    private static void WriteSubEffectBlock(MemoryStream ms, uint blockIndex, uint n)
    {
        if (blockIndex > 0)
        {
            // 24-byte prefix for blocks 1..N-1.
            // spec: Docs/RE/formats/effects.md §A.4 §A.15 — blocks[1..N-1]: u32 sub_id + u32[4] zeros + u32 entry_count: CONFIRMED.
            ms.Write(Le4(blockIndex)); // sub_id = block ordinal (u32 @ prefix+0)
            ms.Write(Le4(0u)); // u32[0] zeros @ prefix+4
            ms.Write(Le4(0u)); // u32[1] zeros @ prefix+8
            ms.Write(Le4(0u)); // u32[2] zeros @ prefix+12
            ms.Write(Le4(0u)); // u32[3] zeros @ prefix+16
            ms.Write(Le4(n)); // entry_count u32 @ prefix+20
        }
        // For block 0, no prefix — entry_count comes from header first_entry_count.
        // spec: Docs/RE/formats/effects.md §A.15 — block[0] has NO entry_count prefix: CONFIRMED.

        // ── Body: name table ────────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.1 — entry_count × 64 bytes: CONFIRMED.
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

        // ── Body: track header (13 bytes) ────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (13 bytes): CONFIRMED.
        ms.WriteByte(1); // anim_loop u8 @ +0
        ms.Write(Le4(67u)); // unknown_constant u32 @ +1
        ms.Write(Le4(469u)); // anim_stride u32 @ +5
        ms.Write(Le4(0u)); // anim_base_time u32 @ +9

        // ── Body: keyframes ──────────────────────────────────────────────────
        // spec: Docs/RE/formats/effects.md §A.4.4 Keyframe array: CONFIRMED.
        if (n == 0) return;

        // Frame 0: NO index prefix (9 × f32 = 36 bytes).
        // spec: Docs/RE/formats/effects.md §A.4.4 — frame 0: 9 × f32 (no index): CONFIRMED.
        WriteNineFloats(ms, 0u, velocityX: 1f, velocityY: 0f, velocityZ: 0f,
            sizeX: 1f, sizeY: 1f, sizeZ: 1f, rotX: 0f, rotY: 0f, rotZ: 0f,
            includeIndex: false);

        // Frames 1..N-1: u32 kf_index + 9 × f32 = 40 bytes each.
        // spec: Docs/RE/formats/effects.md §A.4.4 — frames 1..N-1: u32 kf_index + 9 × f32: CONFIRMED.
        for (uint k = 1; k < n; k++)
        {
            WriteNineFloats(ms, k, velocityX: (float)k, velocityY: 0f, velocityZ: 0f,
                sizeX: 1f, sizeY: 1f, sizeZ: 1f, rotX: 0f, rotY: 0f, rotZ: 0f,
                includeIndex: true);
        }
    }

    /// <summary>
    /// Writes a single keyframe's nine floats.
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
        // Block 0's entry_count comes from first_entry_count (no prefix on disk).
        // spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free; first_entry_count NOT duplicated: CONFIRMED.
        byte[] data = BuildXeff(1u, 2u, 5u);
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
        using var ms = new MemoryStream();
        ms.Write(Le4(0x46464558u));
        ms.Write(new byte[28]);
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(ms.ToArray())));
    }

    [Fact]
    public void TruncatedHeader_ThrowsInvalidDataException()
    {
        // Buffer too short for the 32-byte header.
        // spec: Docs/RE/formats/effects.md §A.2 — header is 32 bytes: VERIFIED.
        byte[] truncated = new byte[16];
        Assert.Throws<InvalidDataException>(() => XeffParser.ParseXeff(new ReadOnlyMemory<byte>(truncated)));
    }

    // ─── tests: stub (zero sub-effects) ────────────────────────────────────────

    [Fact]
    public void StubEffect_ZeroSubEffects_Parsed()
    {
        // spec: Docs/RE/formats/effects.md §A.2 — sub_effect_count=0 valid: VERIFIED.
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
        // spec: Docs/RE/formats/effects.md §A.4 — entry_count for block[0] from header first_entry_count: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(1u, xeff.SubEffects[0].EntryCount);
        Assert.Single(xeff.SubEffects[0].Keyframes);
    }

    [Fact]
    public void SingleSubEffect_OneEntry_TrackHeader_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.4.3 Track header (13 bytes): CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffSubEffect sub = xeff.SubEffects[0];
        Assert.Equal(1, sub.AnimLoop);
        Assert.Equal(67u, sub.UnknownConstant);
        Assert.Equal(469u, sub.AnimStride);
        Assert.Equal(0u, sub.AnimBaseTime);
    }

    [Fact]
    public void SingleSubEffect_OneEntry_Frame0_NoIndexPrefix_VelocityX()
    {
        // spec: Docs/RE/formats/effects.md §A.4.4 — frame 0: 9 × f32 (no index prefix): CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffKeyframe kf0 = xeff.SubEffects[0].Keyframes[0];
        Assert.Equal(0u, kf0.KfIndex);
        Assert.Equal(1f, kf0.VelocityX, precision: 5);
    }

    [Fact]
    public void SingleSubEffect_TwoEntries_Frame1_HasKfIndex()
    {
        // spec: Docs/RE/formats/effects.md §A.4.4 — frames 1..N-1: u32 kf_index + 9 × f32: CONFIRMED.
        byte[] data = BuildXeff(1u, 1u, 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        XeffSubEffect sub = xeff.SubEffects[0];
        Assert.Equal(2, sub.Keyframes.Length);
        Assert.Equal(0u, sub.Keyframes[0].KfIndex);
        Assert.Equal(1u, sub.Keyframes[1].KfIndex);
        Assert.Equal(1f, sub.Keyframes[1].VelocityX, precision: 5);
    }

    [Fact]
    public void SingleSubEffect_AlphaKeys_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §A.4.2 Pass 1 alpha: CONFIRMED.
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
        // spec: Docs/RE/formats/effects.md §A.4.2 Passes 2–4 scale X/Y/Z: CONFIRMED.
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
        byte[] data = BuildXeff(1u, 1u, 1u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal("", xeff.SubEffects[0].TextureNames[0]);
    }

    // ─── tests: multiple sub-effects ───────────────────────────────────────────

    [Fact]
    public void MultipleSubEffects_ThreeSubEffects_EachParsedIndependently()
    {
        // spec: Docs/RE/formats/effects.md §A.4.5 — blocks follow sequentially: CONFIRMED.
        // spec: Docs/RE/formats/effects.md §A.15 — block[0] no prefix; blocks 1..N-1: 24-byte prefix: CONFIRMED.
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
    public void MultipleSubEffects_Block1Plus_SubIdDecoded()
    {
        // Blocks 1..N-1 carry a sub_id in the 24-byte prefix. Verify it's decoded correctly.
        // spec: Docs/RE/formats/effects.md §A.4 §A.15 — sub_id u32 @ prefix+0 for blocks 1..N-1: CONFIRMED.
        byte[] data = BuildXeff(7u, 3u, 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));
        Assert.Equal(0u, xeff.SubEffects[0].SubId); // block 0: no prefix → SubId = 0
        Assert.Equal(1u, xeff.SubEffects[1].SubId); // block 1: sub_id = 1
        Assert.Equal(2u, xeff.SubEffects[2].SubId); // block 2: sub_id = 2
    }

    // ─── tests: first_entry_count = 2 (regression for char_select-u.xeff) ──────

    /// <summary>
    /// Regression fixture for char_select-u.xeff (68 sub-effects, first_entry_count=2).
    /// Confirms the parser uses first_entry_count for block[0] without reading a prefix u32.
    /// spec: Docs/RE/formats/effects.md §A.15 — block[0] prefix-free; first_entry_count=2 case: CONFIRMED.
    /// </summary>
    [Fact]
    public void FirstEntryCount2_Block0_ParsesCorrectly()
    {
        // first_ec=2: block[0] has 2 entries, name table = 2×64=128 bytes, no prefix.
        byte[] data = BuildXeff(effectId: 1u, subEffectCount: 1u, entriesPerSubEffect: 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(2u, xeff.FirstEntryCount);
        Assert.Equal(2u, xeff.SubEffects[0].EntryCount);
        Assert.Equal(2, xeff.SubEffects[0].Keyframes.Length);
        Assert.Equal(2, xeff.SubEffects[0].AlphaKeys.Length);
        // Frame 0: no index, velocityX=1.0
        Assert.Equal(0u, xeff.SubEffects[0].Keyframes[0].KfIndex);
        Assert.Equal(1f, xeff.SubEffects[0].Keyframes[0].VelocityX, precision: 5);
        // Frame 1: kfIndex=1
        Assert.Equal(1u, xeff.SubEffects[0].Keyframes[1].KfIndex);
    }

    /// <summary>
    /// Synthetic fixture matching char_select-u.xeff dimensions: 68 sub-effects, first_entry_count=2.
    /// Verifies the parser walks all 68 blocks without exception or OOB read.
    /// spec: Docs/RE/formats/effects.md §A.15 — char_select-u.xeff regression (68 sub-effects, ec=2): CONFIRMED.
    /// </summary>
    [Fact]
    public void Regression_CharSelectU_FirstEntryCount2_68SubEffects_ParsesWithoutException()
    {
        // Matches char_select-u.xeff dimensions.
        byte[] data = BuildXeff(effectId: 9999u, subEffectCount: 68u, entriesPerSubEffect: 2u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(68u, xeff.SubEffectCount);
        Assert.Equal(68, xeff.SubEffects.Length);
        Assert.Equal(2u, xeff.SubEffects[0].EntryCount); // block[0]: from header
        Assert.Equal(2u, xeff.SubEffects[1].EntryCount); // block[1]: from 24-byte prefix
        Assert.Equal(2u, xeff.SubEffects[67].EntryCount); // last block
        // Each block has 2 keyframes
        Assert.All(xeff.SubEffects, sub => Assert.Equal(2, sub.Keyframes.Length));
    }

    // ─── tests: first_entry_count = 20 (regression for zone_sel_u.xeff) ────────

    /// <summary>
    /// Regression fixture for zone_sel_u.xeff (11 sub-effects, first_entry_count=20).
    /// Verifies the parser correctly allocates a name table of 20×64=1280 bytes for block[0].
    /// spec: Docs/RE/formats/effects.md §A.15 — zone_sel_u.xeff regression (11 sub-effects, ec=20): CONFIRMED.
    /// </summary>
    [Fact]
    public void FirstEntryCount20_Block0_ParsesCorrectly()
    {
        // first_ec=20: block[0] name table = 20×64=1280 bytes.
        byte[] data = BuildXeff(effectId: 2u, subEffectCount: 1u, entriesPerSubEffect: 20u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(20u, xeff.FirstEntryCount);
        Assert.Equal(20u, xeff.SubEffects[0].EntryCount);
        Assert.Equal(20, xeff.SubEffects[0].Keyframes.Length);
        Assert.Equal(20, xeff.SubEffects[0].AlphaKeys.Length);
        Assert.Equal(20, xeff.SubEffects[0].TextureNames.Length);
    }

    /// <summary>
    /// Synthetic fixture matching zone_sel_u.xeff dimensions: 11 sub-effects, first_entry_count=20.
    /// Verifies all 11 blocks parse without exception.
    /// spec: Docs/RE/formats/effects.md §A.15 — zone_sel_u.xeff regression (11 sub-effects, ec=20): CONFIRMED.
    /// </summary>
    [Fact]
    public void Regression_ZoneSelU_FirstEntryCount20_11SubEffects_ParsesWithoutException()
    {
        // Matches zone_sel_u.xeff dimensions.
        byte[] data = BuildXeff(effectId: 8888u, subEffectCount: 11u, entriesPerSubEffect: 20u);
        XeffData xeff = XeffParser.ParseXeff(new ReadOnlyMemory<byte>(data));

        Assert.Equal(11u, xeff.SubEffectCount);
        Assert.Equal(11, xeff.SubEffects.Length);
        Assert.Equal(20u, xeff.SubEffects[0].EntryCount);
        Assert.Equal(20u, xeff.SubEffects[1].EntryCount);
        Assert.All(xeff.SubEffects, sub => Assert.Equal(20, sub.Keyframes.Length));
    }

    // ─── tests: large sub-effect counts (kept as library regression) ──────────

    [Fact]
    public void Regression_LargeSubEffectCount68_ParsesWithoutException()
    {
        // Matches char_select-u.xeff sub_effect_count=68 (arbitrary entry_count=5).
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
        // Matches zone_sel_u.xeff sub_effect_count=11 (arbitrary entry_count=3).
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
        // spec: Docs/RE/formats/effects.md §B.2 File-size formula: 4+(3×2)+4+(3×32)=110 bytes: VERIFIED.
        using var ms = new MemoryStream();
        ms.Write(Le4(3u));
        ms.Write(BitConverter.GetBytes((ushort)0));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(BitConverter.GetBytes((ushort)2));
        ms.Write(Le4(3u));
        for (int v = 0; v < 3; v++)
        for (int f = 0; f < 8; f++)
            ms.Write(Le4f((float)(v * 10 + f)));

        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));
        Assert.Equal(3, shape.Indices.Length);
        Assert.Equal(3, shape.Vertices.Length);
    }

    [Fact]
    public void ParseEff_VertexRecord_PosAndUv_Decoded()
    {
        // spec: Docs/RE/formats/effects.md §B.4.1 — pos_x @ +0, tex_u @ +24: VERIFIED.
        using var ms = new MemoryStream();
        ms.Write(Le4(3u));
        ms.Write(BitConverter.GetBytes((ushort)0));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(BitConverter.GetBytes((ushort)2));
        ms.Write(Le4(3u));
        ms.Write(Le4f(10f));
        ms.Write(Le4f(20f));
        ms.Write(Le4f(30f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(1f));
        ms.Write(Le4f(0f));
        ms.Write(Le4f(0.5f));
        ms.Write(Le4f(0.25f));
        ms.Write(new byte[64]); // vertices 1 and 2

        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));
        EffVertex v0 = shape.Vertices[0];
        Assert.Equal(10f, v0.PosX, precision: 5);
        Assert.Equal(20f, v0.PosY, precision: 5);
        Assert.Equal(30f, v0.PosZ, precision: 5);
        Assert.Equal(0.5f, v0.TexU, precision: 5);
        Assert.Equal(0.25f, v0.TexV, precision: 5);
    }

    [Fact]
    public void ParseEff_Truncated_ThrowsInvalidDataException()
    {
        byte[] truncated = new byte[3];
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
        ms.Write(new byte[96]);
        EffObjectShape shape = XeffParser.ParseEff(new ReadOnlyMemory<byte>(ms.ToArray()));
        Assert.Equal(3, shape.Indices.Length);
    }

    // ─── tests: file-size formula ─────────────────────────────────────────────

    [Fact]
    public void SingleSubEffect_N5_FileSize_MatchesSpecFormula()
    {
        // spec: Docs/RE/formats/effects.md §A.2 File-size formula (1 sub-effect, N entries, scale curves empty):
        //   32               — file header (§A.2 XEFF_HEADER_SIZE=32)
        //   block[0] (no prefix):
        //   + N×64           — name table (§A.4.1)
        //   + (4+N×4)        — alpha curve (§A.4.2)
        //   + 3×4            — scaleX/Y/Z count-only prefixes (§A.4.2)
        //   + 13             — track header (§A.4.3 XEFF_TRACK_HEADER_SIZE=13)
        //   + 9×4            — frame 0: no index (§A.4.4)
        //   + (N−1)×40       — frames 1..N-1 (§A.4.4)
        // With N=5: 32 + 320 + 24 + 12 + 13 + 36 + 160 = 597 bytes (no prefix for single block[0]).
        const int N = 5;
        int expectedSize = 32 // header
                           + N * 64 // name table — §A.4.1
                           + (4 + N * 4) // alpha curve — §A.4.2
                           + 3 * 4 // scaleX/Y/Z count prefixes — §A.4.2
                           + 13 // track header — §A.14
                           + 36 // frame 0 — §A.4.4
                           + (N - 1) * 40; // frames 1..N-1 — §A.4.4
        // Note: no entry_count prefix for single-block file (block[0] is prefix-free).
        // spec: Docs/RE/formats/effects.md §A.15 — block[0] has NO entry_count prefix: CONFIRMED.

        byte[] data = BuildXeff(1u, 1u, (uint)N);
        Assert.Equal(expectedSize, data.Length);
    }

    // ─── tests: rotation calculation ───────────────────────────────────────────

    [Fact]
    public void Keyframe_Rotation_90DegY_CorrectQuat()
    {
        // 90° around Y: quat = (0, sin(45°), 0, cos(45°)) ≈ (0, 0.7071, 0, 0.7071).
        // spec: Docs/RE/formats/effects.md §A.7 — "π/180 × degrees; half-angle Euler-XYZ decomposition": CONFIRMED.
        using var ms = new MemoryStream();

        // 32-byte header
        ms.Write(Le4(1u)); // effect_id
        ms.Write(Le4(1u)); // sub_effect_count
        ms.Write(Le4(1u)); // type_flag
        ms.Write(new byte[16]); // reserved
        ms.Write(Le4(1u)); // first_entry_count = 1

        // Block[0] body (no prefix) with N=1:
        ms.Write(new byte[64]); // name table 1×64
        ms.Write(Le4(1u)); // alpha count=1
        ms.Write(Le4f(0.5f)); // alpha[0]
        ms.Write(Le4(0u)); // scaleX count=0
        ms.Write(Le4(0u)); // scaleY count=0
        ms.Write(Le4(0u)); // scaleZ count=0
        // track header (13 bytes)
        ms.WriteByte(1);
        ms.Write(Le4(67u));
        ms.Write(Le4(100u));
        ms.Write(Le4(0u));
        // frame 0 (no index): rotation 0, 90, 0 degrees
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