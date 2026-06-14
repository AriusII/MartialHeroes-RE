using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture-based tests for <see cref="EnvironmentBinParsers"/>.
/// All synthetic fixtures are built in-memory from the spec; no real game bytes are committed.
/// spec: Docs/RE/formats/environment_bins.md
/// spec: Docs/RE/specs/environment.md
/// </summary>
public sealed class EnvironmentBinParserTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static void WriteU32LE(byte[] buf, int offset, uint v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), v);

    private static void WriteF32LE(byte[] buf, int offset, float v) =>
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4), v);

    // =========================================================================
    // MapOptionBin tests
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 40-byte map_option fixture.
    /// RECONCILED Campaign 5: the 10 u32 words are MOVE_DUNGEON, SIGHT_FIX, LENSFLARE, STARDOME,
    /// CLOUDDOME, SUN, MOON, SKYBOX, MAPHIDE, reserved — there are NO water fields.
    /// spec: Docs/RE/formats/environment_bins.md §1.1 Field table: CONFIRMED
    /// </summary>
    private static byte[] BuildMapOption(
        uint isDungeon = 0, uint sightFix = 0, uint lensFlare = 1,
        uint starDome = 1, uint cloudDome = 1, uint sun = 1,
        uint moon = 1, uint skybox = 0, uint indoor = 0, uint reserved = 0)
    {
        // Fixed: 10 × u32 LE = 40 bytes.
        // spec: §1 — "exactly 40 bytes (10 × u32 LE)": CONFIRMED
        var buf = new byte[40];
        WriteU32LE(buf, 0x00, isDungeon); // MOVE_DUNGEON @ 0x00
        WriteU32LE(buf, 0x04, sightFix); // SIGHT_FIX @ 0x04
        WriteU32LE(buf, 0x08, lensFlare); // LENSFLARE @ 0x08
        WriteU32LE(buf, 0x0C, starDome); // STARDOME @ 0x0C
        WriteU32LE(buf, 0x10, cloudDome); // CLOUDDOME @ 0x10
        WriteU32LE(buf, 0x14, sun); // SUN @ 0x14
        WriteU32LE(buf, 0x18, moon); // MOON @ 0x18
        WriteU32LE(buf, 0x1C, skybox); // SKYBOX @ 0x1C
        WriteU32LE(buf, 0x20, indoor); // MAPHIDE / indoor_flag @ 0x20
        WriteU32LE(buf, 0x24, reserved); // _reserved_ @ 0x24
        return buf;
    }

    [Fact]
    public void MapOption_Parse_TenU32_LayoutInOrder()
    {
        // Asserts the full 10 × u32 layout from a synthetic 40-byte buffer, one distinct value per
        // word, so the field-to-offset mapping is pinned exactly to the reconciled spec.
        // spec: Docs/RE/formats/environment_bins.md §1.1 Field table: CONFIRMED
        byte[] data = BuildMapOption(
            isDungeon: 1, sightFix: 300, lensFlare: 2,
            starDome: 3, cloudDome: 4, sun: 5,
            moon: 6, skybox: 7, indoor: 8, reserved: 9);

        MapOptionBin r = EnvironmentBinParsers.ParseMapOption(data.AsSpan());

        Assert.Equal(1u, r.IsDungeon); // 0x00 MOVE_DUNGEON
        Assert.Equal(300u, r.SightDistance); // 0x04 SIGHT_FIX
        Assert.Equal(2u, r.LensFlareEnable); // 0x08 LENSFLARE
        Assert.Equal(3u, r.StarDomeEnable); // 0x0C STARDOME
        Assert.Equal(4u, r.CloudDomeEnable); // 0x10 CLOUDDOME
        Assert.Equal(5u, r.SunEnable); // 0x14 SUN
        Assert.Equal(6u, r.MoonEnable); // 0x18 MOON
        Assert.Equal(7u, r.SkyboxEnable); // 0x1C SKYBOX
        Assert.Equal(8u, r.IndoorFlag); // 0x20 MAPHIDE
        Assert.Equal(9u, r.Reserved); // 0x24 reserved
    }

    [Fact]
    public void MapOption_Parse_StandardOutdoorPattern()
    {
        // Pattern [0,0,1,1,1,1,1,0,0,0] — standard outdoor area (areas 0,1,2,3,…):
        //   lensflare + stardome + clouddome + sun + moon on; not a dungeon; outdoor.
        // spec: Docs/RE/formats/environment_bins.md §1.2 Observed flag patterns — CONFIRMED
        byte[] data = BuildMapOption(
            isDungeon: 0, sightFix: 0, lensFlare: 1,
            starDome: 1, cloudDome: 1, sun: 1,
            moon: 1, skybox: 0, indoor: 0, reserved: 0);

        MapOptionBin result = EnvironmentBinParsers.ParseMapOption(data.AsSpan());

        Assert.Equal(0u, result.IsDungeon);
        Assert.Equal(0u, result.SightDistance);
        Assert.Equal(1u, result.LensFlareEnable);
        Assert.Equal(1u, result.StarDomeEnable);
        Assert.Equal(1u, result.CloudDomeEnable);
        Assert.Equal(1u, result.SunEnable);
        Assert.Equal(1u, result.MoonEnable);
        Assert.Equal(0u, result.SkyboxEnable);
        Assert.Equal(0u, result.IndoorFlag);
        Assert.Equal(0u, result.Reserved);
    }

    [Fact]
    public void MapOption_Parse_IndoorPattern_AllSkyOff()
    {
        // Pattern [0,0,0,0,0,0,0,0,1,0] — indoor/dungeon areas 5, 17, 20, etc.
        // spec: Docs/RE/formats/environment_bins.md §1.2 — indoor/dungeon — CONFIRMED
        byte[] data = BuildMapOption(
            isDungeon: 0, sightFix: 0, lensFlare: 0,
            starDome: 0, cloudDome: 0, sun: 0,
            moon: 0, skybox: 0, indoor: 1, reserved: 0);

        MapOptionBin result = EnvironmentBinParsers.ParseMapOption(data.AsSpan());

        Assert.Equal(1u, result.IndoorFlag);
        Assert.Equal(0u, result.SunEnable);
        Assert.Equal(0u, result.MoonEnable);
        Assert.Equal(0u, result.LensFlareEnable);
    }

    [Fact]
    public void MapOption_Parse_DungeonSightClampPattern()
    {
        // Pattern [1,300,0,0,0,0,0,0,1,0] — dungeon, sight clamped to 300, indoor lighting, sky off
        // (areas 11, 15, 16, …). RECONCILED Campaign 5: 0x00 is the dungeon flag, 0x04 is the
        // sight-clamp distance — NOT water_enable / water_y.
        // spec: Docs/RE/formats/environment_bins.md §1.2 — dungeon + SIGHT_FIX=300: CONFIRMED
        byte[] data = BuildMapOption(
            isDungeon: 1, sightFix: 300, lensFlare: 0,
            starDome: 0, cloudDome: 0, sun: 0,
            moon: 0, skybox: 0, indoor: 1, reserved: 0);

        MapOptionBin result = EnvironmentBinParsers.ParseMapOption(data.AsSpan());

        Assert.Equal(1u, result.IsDungeon);
        Assert.Equal(300u, result.SightDistance);
        Assert.Equal(1u, result.IndoorFlag);
    }

    [Fact]
    public void MapOption_Parse_FixedSize_Is40Bytes()
    {
        // spec: Docs/RE/formats/environment_bins.md §1 — "exactly 40 bytes": CONFIRMED
        Assert.Equal(40, MapOptionBin.FixedSize);
        byte[] data = BuildMapOption();
        Assert.Equal(MapOptionBin.FixedSize, data.Length);
    }

    [Fact]
    public void MapOption_Parse_WrongSize_ThrowsInvalidData()
    {
        // spec: §1 — size validation.
        byte[] tooShort = new byte[39];
        byte[] tooLong = new byte[41];
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseMapOption(tooShort.AsSpan()));
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseMapOption(tooLong.AsSpan()));
    }

    [Fact]
    public void MapOption_Parse_MemoryOverload_Works()
    {
        byte[] data = BuildMapOption(isDungeon: 1, sightFix: 700);
        MapOptionBin result = EnvironmentBinParsers.ParseMapOption(new ReadOnlyMemory<byte>(data));
        Assert.Equal(1u, result.IsDungeon);
        Assert.Equal(700u, result.SightDistance);
    }

    // =========================================================================
    // FogBin tests
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 204-byte fog fixture.
    /// spec: Docs/RE/formats/environment_bins.md §2.1 Field table: CONFIRMED
    /// Total: 4 + 4 + 4 + 192 = 204 bytes.
    /// </summary>
    private static byte[] BuildFog(
        float startDist = 0.5f, float endDist = 0.9f, uint dataLoadFlag = 0,
        BgraColor[]? fogColors = null)
    {
        // spec: §2 — "exactly 204 bytes": CONFIRMED
        var buf = new byte[204];

        // start_dist f32 @ 0x00. spec: §2.1 CONFIRMED
        WriteF32LE(buf, 0x00, startDist);
        // end_dist f32 @ 0x04. spec: §2.1 CONFIRMED
        WriteF32LE(buf, 0x04, endDist);
        // data_load_flag u32 @ 0x08. spec: §2.1 CONFIRMED
        WriteU32LE(buf, 0x08, dataLoadFlag);

        // fog_colors[48] BGRA @ 0x0C. spec: §2.1 CONFIRMED; §2.2 BGRA order CONFIRMED
        var colors = fogColors ?? new BgraColor[48];
        for (int i = 0; i < 48; i++)
        {
            int off = 0x0C + i * 4;
            buf[off + 0] = colors.Length > i ? colors[i].B : (byte)0;
            buf[off + 1] = colors.Length > i ? colors[i].G : (byte)0;
            buf[off + 2] = colors.Length > i ? colors[i].R : (byte)0;
            buf[off + 3] = colors.Length > i ? colors[i].A : (byte)0;
        }

        return buf;
    }

    [Fact]
    public void Fog_Parse_StartEnd_RoundTrip()
    {
        // spec: Docs/RE/formats/environment_bins.md §2.1 — start_dist f32 @ 0x00: CONFIRMED
        // spec: §2.1 — end_dist f32 @ 0x04: CONFIRMED
        byte[] data = BuildFog(startDist: 0.5f, endDist: 0.9f);
        FogBin result = EnvironmentBinParsers.ParseFog(data.AsSpan());

        Assert.Equal(0.5f, result.StartDist, precision: 5);
        Assert.Equal(0.9f, result.EndDist, precision: 5);
    }

    [Fact]
    public void Fog_Parse_DataLoadFlag_Zero()
    {
        // spec: Docs/RE/formats/environment_bins.md §2.1 — data_load_flag u32 @ 0x08: CONFIRMED
        // All sampled areas use value 0.
        byte[] data = BuildFog(dataLoadFlag: 0);
        FogBin result = EnvironmentBinParsers.ParseFog(data.AsSpan());

        Assert.Equal(0u, result.DataLoadFlag);
    }

    [Fact]
    public void Fog_Parse_FogColors_Count_Is48()
    {
        // spec: §2.1 — fog_colors[48]: CONFIRMED
        byte[] data = BuildFog();
        FogBin result = EnvironmentBinParsers.ParseFog(data.AsSpan());

        Assert.Equal(48, result.FogColors.Length);
        Assert.Equal(FogBin.KeyframeCount, result.FogColors.Length);
    }

    [Fact]
    public void Fog_Parse_FogColor_BGRA_Order()
    {
        // spec: Docs/RE/formats/environment_bins.md §2.2 — BGRA byte order: CONFIRMED
        // Blue @ [0], Green @ [1], Red @ [2], Alpha @ [3].
        var colors = new BgraColor[48];
        colors[0] = new BgraColor(B: 10, G: 20, R: 30, A: 0);
        colors[24] = new BgraColor(B: 57, G: 101, R: 155, A: 0); // noon

        byte[] data = BuildFog(fogColors: colors);
        FogBin result = EnvironmentBinParsers.ParseFog(data.AsSpan());

        // keyframe 0 (midnight)
        Assert.Equal(10, result.FogColors[0].B);
        Assert.Equal(20, result.FogColors[0].G);
        Assert.Equal(30, result.FogColors[0].R);
        Assert.Equal(0, result.FogColors[0].A);

        // keyframe 24 (noon)
        Assert.Equal(57, result.FogColors[24].B);
        Assert.Equal(101, result.FogColors[24].G);
        Assert.Equal(155, result.FogColors[24].R);
    }

    [Fact]
    public void Fog_Parse_FixedSize_Is204Bytes()
    {
        // spec: §2 — "exactly 204 bytes": CONFIRMED
        Assert.Equal(204, FogBin.FixedSize);
    }

    [Fact]
    public void Fog_Parse_WrongSize_ThrowsInvalidData()
    {
        byte[] tooShort = new byte[203];
        byte[] tooLong = new byte[205];
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseFog(tooShort.AsSpan()));
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseFog(tooLong.AsSpan()));
    }

    // =========================================================================
    // MaterialBin tests
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 9792-byte material fixture.
    /// spec: Docs/RE/formats/environment_bins.md §3.1 — 48 × 51 × 4 bytes: CONFIRMED
    /// </summary>
    private static byte[] BuildMaterial(float fillValue = 0.0f)
    {
        // 9792 bytes = 48 × 51 × 4. spec: §3 — "exactly 9792 bytes": CONFIRMED
        var buf = new byte[MaterialBin.FixedSize];
        if (fillValue != 0.0f)
        {
            for (int i = 0; i < MaterialBin.KeyframeCount * MaterialBin.ValuesPerKeyframe; i++)
                WriteF32LE(buf, i * 4, fillValue);
        }

        return buf;
    }

    [Fact]
    public void Material_Parse_FixedSize_Is9792Bytes()
    {
        // spec: §3 — "exactly 9792 bytes": CONFIRMED
        Assert.Equal(9792, MaterialBin.FixedSize);
    }

    [Fact]
    public void Material_Parse_TableDimensions_48x51()
    {
        // spec: §3.1 — f32[48][51]: CONFIRMED
        byte[] data = BuildMaterial();
        MaterialBin result = EnvironmentBinParsers.ParseMaterial(data.AsSpan());

        Assert.Equal(48, result.ColorTable.Length);
        Assert.Equal(51, result.ColorTable[0].Length);
    }

    [Fact]
    public void Material_Parse_FloatValues_RoundTrip()
    {
        // spec: §3.1 — row k at byte k × 204, f32 LE: CONFIRMED
        // Write distinct float values at known positions and check round-trip.
        var buf = new byte[MaterialBin.FixedSize];

        // Row 0, index 4 (sun_color.R) → spec: §3.2 [4..7] sun_color RGBA: CODE-CONFIRMED
        WriteF32LE(buf, 0 * 204 + 4 * 4, 1.25f);
        // Row 24, index 29 (ambient_sky_color.R) → spec: §3.2 [29..32]: CODE-CONFIRMED
        WriteF32LE(buf, 24 * 204 + 29 * 4, 0.42f);
        // Row 47 (last), index 50 → PROPOSED reserved slot
        WriteF32LE(buf, 47 * 204 + 50 * 4, 3.14f);

        MaterialBin result = EnvironmentBinParsers.ParseMaterial(buf.AsSpan());

        Assert.Equal(1.25f, result.ColorTable[0][4], precision: 5);
        Assert.Equal(0.42f, result.ColorTable[24][29], precision: 5);
        Assert.Equal(3.14f, result.ColorTable[47][50], precision: 4);
    }

    [Fact]
    public void Material_Parse_WrongSize_ThrowsInvalidData()
    {
        byte[] tooShort = new byte[9791];
        byte[] tooLong = new byte[9793];
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseMaterial(tooShort.AsSpan()));
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseMaterial(tooLong.AsSpan()));
    }

    // =========================================================================
    // LightBin tests
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 5312-byte light fixture.
    /// spec: Docs/RE/formats/environment_bins.md §9.1 Revised section layout: CONFIRMED
    /// Total: 2304 + 48 + 2304 + 48 + 192 + 192 + 200 + 8 + 16 = 5312 bytes.
    /// </summary>
    private static byte[] BuildLight(
        float dirColorAR = 0.0f, float ambColorAR = 0.0f,
        float fogScalarKf0 = 0.0f, float secScalarKf0 = 0.0f,
        float fallbackScale = 1.0f,
        float fallbackDirX = -7.0f, float fallbackDirY = 7.0f, float fallbackDirZ = 20.0f)
    {
        // spec: §9.1 — 5312 bytes total: CONFIRMED
        var buf = new byte[LightBin.FixedSize]; // 5312

        // Section A — Directional keyframes @ 0x0000 (2304 bytes, 48 × 48 bytes).
        // spec: §9.1 Section A @ 0x0000: CONFIRMED
        // Write color_A.R (f32 @ slot+0x00) for keyframe 0 only.
        // spec: §9.2 — color_A RGBA @ slot+0x00: CONFIRMED
        WriteF32LE(buf, 0x0000, dirColorAR); // kf0 color_A[0] = R

        // Section B — Ambient keyframes @ 0x0930 (2304 bytes, 48 × 48 bytes).
        // spec: §9.1 Section B @ 0x0930: CONFIRMED
        WriteF32LE(buf, 0x0930, ambColorAR); // kf0 color_A[0] = R

        // Section C — Fog scalars @ 0x1260 (192 bytes, 48 × f32).
        // spec: §9.1 Section C @ 0x1260: CONFIRMED
        WriteF32LE(buf, 0x1260, fogScalarKf0); // kf0 fog scalar

        // Section D — Secondary fog scalars @ 0x1320 (192 bytes, 48 × f32).
        // spec: §9.1 Section D @ 0x1320: SAMPLE-VERIFIED
        WriteF32LE(buf, 0x1320, secScalarKf0); // kf0 secondary scalar

        // Section E (200 bytes) and padding (8 bytes) remain all-zero by default.
        // spec: §9.1 Section E @ 0x13E0 (200 bytes, all zeros): SAMPLE-VERIFIED (all zeros)
        // spec: §9.1 Padding @ 0x14A8 (8 bytes, all zeros): SAMPLE-VERIFIED (all zeros)

        // Fallback light @ 0x14B0 (4 × f32).
        // spec: §9.4 — scale @ 0x14B0, dir_X @ 0x14B4, dir_Y @ 0x14B8, dir_Z @ 0x14BC: CONFIRMED
        WriteF32LE(buf, 0x14B0, fallbackScale);
        WriteF32LE(buf, 0x14B4, fallbackDirX);
        WriteF32LE(buf, 0x14B8, fallbackDirY);
        WriteF32LE(buf, 0x14BC, fallbackDirZ);

        return buf;
    }

    [Fact]
    public void Light_Parse_FixedSize_Is5312Bytes()
    {
        // spec: §9.1 — 5312 bytes total: CONFIRMED
        Assert.Equal(5312, LightBin.FixedSize);
    }

    [Fact]
    public void Light_Parse_KeyframeCount_Is48()
    {
        // spec: §9.1 — 48 keyframes in sections A and B: CONFIRMED
        byte[] data = BuildLight();
        LightBin result = EnvironmentBinParsers.ParseLight(data.AsSpan());

        Assert.Equal(48, result.DirectionalKeyframes.Length);
        Assert.Equal(48, result.AmbientKeyframes.Length);
    }

    [Fact]
    public void Light_Parse_DirectionalKeyframe_ColorA_RoundTrip()
    {
        // spec: §9.2 — color_A RGBA @ slot+0x00: CONFIRMED (Section A = directional)
        byte[] data = BuildLight(dirColorAR: 0.40f);
        LightBin result = EnvironmentBinParsers.ParseLight(data.AsSpan());

        // kf[0] color_A[0] = R channel written at 0x0000
        Assert.Equal(0.40f, result.DirectionalKeyframes[0].ColorA[0], precision: 5);
        Assert.Equal(4, result.DirectionalKeyframes[0].ColorA.Length);
        Assert.Equal(4, result.DirectionalKeyframes[0].ColorB.Length);
        Assert.Equal(4, result.DirectionalKeyframes[0].ColorC.Length);
    }

    [Fact]
    public void Light_Parse_AmbientKeyframe_ColorA_RoundTrip()
    {
        // spec: §9.2 — color_A RGBA @ slot+0x00 for Section B = ambient: CONFIRMED
        byte[] data = BuildLight(ambColorAR: 0.21f);
        LightBin result = EnvironmentBinParsers.ParseLight(data.AsSpan());

        Assert.Equal(0.21f, result.AmbientKeyframes[0].ColorA[0], precision: 5);
    }

    [Fact]
    public void Light_Parse_FogDistanceScalars_Count_Is48()
    {
        // spec: §9.1 Section C — 48 × f32 fog-distance scalars: CONFIRMED
        byte[] data = BuildLight(fogScalarKf0: 8.5f);
        LightBin result = EnvironmentBinParsers.ParseLight(data.AsSpan());

        Assert.Equal(48, result.FogDistanceScalars.Length);
        Assert.Equal(8.5f, result.FogDistanceScalars[0], precision: 5);
    }

    [Fact]
    public void Light_Parse_SecondaryFogScalars_Count_Is48()
    {
        // spec: §9.1 Section D — 48 × f32 secondary fog scalars: SAMPLE-VERIFIED
        byte[] data = BuildLight(secScalarKf0: 0.001f);
        LightBin result = EnvironmentBinParsers.ParseLight(data.AsSpan());

        Assert.Equal(48, result.SecondaryFogScalars.Length);
        Assert.Equal(0.001f, result.SecondaryFogScalars[0], precision: 5);
    }

    [Fact]
    public void Light_Parse_FallbackVector_Canonical()
    {
        // Canonical fallback: scale=1.0, dir=(−7, 7, 20).
        // spec: Docs/RE/formats/environment_bins.md §9.4 — CONFIRMED values
        byte[] data = BuildLight(
            fallbackScale: 1.0f, fallbackDirX: -7.0f, fallbackDirY: 7.0f, fallbackDirZ: 20.0f);
        LightBin result = EnvironmentBinParsers.ParseLight(data.AsSpan());

        Assert.Equal(1.0f, result.FallbackScale, precision: 5);
        Assert.Equal(-7.0f, result.FallbackDirX, precision: 5);
        Assert.Equal(7.0f, result.FallbackDirY, precision: 5);
        Assert.Equal(20.0f, result.FallbackDirZ, precision: 5);
    }

    [Fact]
    public void Light_Parse_SectionE_Size_Is200Bytes()
    {
        // spec: §9.1 Section E — 200 bytes: SAMPLE-VERIFIED (all zeros)
        byte[] data = BuildLight();
        LightBin result = EnvironmentBinParsers.ParseLight(new ReadOnlyMemory<byte>(data));

        Assert.Equal(200, result.RawSectionE.Length);
    }

    [Fact]
    public void Light_Parse_WrongSize_ThrowsInvalidData()
    {
        byte[] tooShort = new byte[5311];
        byte[] tooLong = new byte[5313];
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseLight(tooShort.AsSpan()));
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseLight(tooLong.AsSpan()));
    }

    [Fact]
    public void Light_Parse_SectionB_Starts_At_0x0930()
    {
        // Verify section A and B are independent by writing distinct values to each.
        // Section A kf0 color_A[0] @ 0x0000, Section B kf0 color_A[0] @ 0x0930.
        // spec: §9.1 Section A @ 0x0000, Section B @ 0x0930: CONFIRMED
        var buf = new byte[LightBin.FixedSize];
        WriteF32LE(buf, 0x0000, 0.111f); // Section A kf0 color_A[0]
        WriteF32LE(buf, 0x0930, 0.222f); // Section B kf0 color_A[0]
        // Fallback
        WriteF32LE(buf, 0x14B0, 1.0f);

        LightBin result = EnvironmentBinParsers.ParseLight(buf.AsSpan());
        Assert.Equal(0.111f, result.DirectionalKeyframes[0].ColorA[0], precision: 5);
        Assert.Equal(0.222f, result.AmbientKeyframes[0].ColorA[0], precision: 5);
    }

    [Fact]
    public void Light_Parse_AllKeyframes_LastSlot_Readable()
    {
        // Verify the last (48th) keyframe slots in both sections are reachable.
        // Section A kf47 @ 0x0000 + 47×48 = 0x0000 + 2256 = 0x08D0.
        // Section B kf47 @ 0x0930 + 47×48 = 0x0930 + 2256 = 0x1200.
        // spec: §9.1 — 48 keyframes × 48 bytes per section: CONFIRMED
        var buf = new byte[LightBin.FixedSize];
        int sectionAKf47 = 0x0000 + 47 * 48;
        int sectionBKf47 = 0x0930 + 47 * 48;
        WriteF32LE(buf, sectionAKf47, 0.47f); // Section A kf47 color_A[0]
        WriteF32LE(buf, sectionBKf47, 0.74f); // Section B kf47 color_A[0]
        WriteF32LE(buf, 0x14B0, 1.0f); // fallback

        LightBin result = EnvironmentBinParsers.ParseLight(buf.AsSpan());
        Assert.Equal(0.47f, result.DirectionalKeyframes[47].ColorA[0], precision: 5);
        Assert.Equal(0.74f, result.AmbientKeyframes[47].ColorA[0], precision: 5);
    }

    // =========================================================================
    // StarDomeBin tests (Tier 2)
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 9216-byte stardome fixture.
    /// spec: Docs/RE/formats/environment_bins.md §4.1 — 12 × 192 × 4 bytes: CONFIRMED
    /// </summary>
    private static byte[] BuildStarDome(BgraColor? kf0Color = null, BgraColor? kf11Color = null)
    {
        // spec: §4 — "exactly 9216 bytes": CONFIRMED
        var buf = new byte[StarDomeBin.FixedSize];

        if (kf0Color.HasValue)
        {
            // All 192 stars in kf0 share same colour (observed pattern).
            // spec: §4.3 — uniform tint across all stars per keyframe: SAMPLE-VERIFIED
            for (int s = 0; s < 192; s++)
            {
                int off = s * 4;
                buf[off + 0] = kf0Color.Value.B;
                buf[off + 1] = kf0Color.Value.G;
                buf[off + 2] = kf0Color.Value.R;
                buf[off + 3] = kf0Color.Value.A;
            }
        }

        if (kf11Color.HasValue)
        {
            // kf11 starts at offset 11 × 192 × 4 = 8448.
            int kf11Offset = 11 * 192 * 4;
            for (int s = 0; s < 192; s++)
            {
                int off = kf11Offset + s * 4;
                buf[off + 0] = kf11Color.Value.B;
                buf[off + 1] = kf11Color.Value.G;
                buf[off + 2] = kf11Color.Value.R;
                buf[off + 3] = kf11Color.Value.A;
            }
        }

        return buf;
    }

    [Fact]
    public void StarDome_Parse_FixedSize_Is9216()
    {
        // spec: §4 — "exactly 9216 bytes": CONFIRMED
        Assert.Equal(9216, StarDomeBin.FixedSize);
    }

    [Fact]
    public void StarDome_Parse_Dimensions_12x192()
    {
        // spec: §4.1 — 12 keyframes × 192 star instances: CONFIRMED
        byte[] data = BuildStarDome();
        StarDomeBin result = EnvironmentBinParsers.ParseStarDome(data.AsSpan());

        Assert.Equal(12, result.StarColors.Length);
        Assert.Equal(192, result.StarColors[0].Length);
    }

    [Fact]
    public void StarDome_Parse_Kf0_BGRA_Order()
    {
        // spec: §4.2 — BGRA byte order: CONFIRMED
        var kf0Color = new BgraColor(B: 50, G: 60, R: 70, A: 0);
        byte[] data = BuildStarDome(kf0Color: kf0Color);
        StarDomeBin result = EnvironmentBinParsers.ParseStarDome(data.AsSpan());

        Assert.Equal(50, result.StarColors[0][0].B);
        Assert.Equal(60, result.StarColors[0][0].G);
        Assert.Equal(70, result.StarColors[0][0].R);
        Assert.Equal(0, result.StarColors[0][0].A);
    }

    [Fact]
    public void StarDome_Parse_Kf11_LastFrame_Accessible()
    {
        // Verify the last keyframe (kf11) is accessible.
        // kf11 offset = 11 × 192 × 4 = 8448.
        // spec: §4.1 — 12 keyframes: CONFIRMED
        var kf11Color = new BgraColor(B: 200, G: 180, R: 160, A: 0);
        byte[] data = BuildStarDome(kf11Color: kf11Color);
        StarDomeBin result = EnvironmentBinParsers.ParseStarDome(data.AsSpan());

        Assert.Equal(200, result.StarColors[11][0].B);
        Assert.Equal(180, result.StarColors[11][0].G);
        Assert.Equal(160, result.StarColors[11][0].R);
    }

    [Fact]
    public void StarDome_Parse_WrongSize_ThrowsInvalidData()
    {
        byte[] tooShort = new byte[9215];
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseStarDome(tooShort.AsSpan()));
    }

    // =========================================================================
    // CloudDomeBin tests (Tier 2)
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 23040-byte clouddome fixture.
    /// spec: Docs/RE/formats/environment_bins.md §5.1 — 2 × 11520 bytes: CONFIRMED
    /// </summary>
    private static byte[] BuildCloudDome(
        BgraColor? layer1Kf0 = null, BgraColor? layer2Kf0 = null)
    {
        // spec: §5 — "exactly 23040 bytes": CONFIRMED
        var buf = new byte[CloudDomeBin.FixedSize];

        if (layer1Kf0.HasValue)
        {
            // Layer 1 kf0 vertex 0 @ offset 0x0000.
            // spec: §5.1 — cloud_layer1_colors @ 0x0000: CONFIRMED
            buf[0] = layer1Kf0.Value.B;
            buf[1] = layer1Kf0.Value.G;
            buf[2] = layer1Kf0.Value.R;
            buf[3] = layer1Kf0.Value.A;
        }

        if (layer2Kf0.HasValue)
        {
            // Layer 2 kf0 vertex 0 @ offset 0x2D00.
            // spec: §5.1 — cloud_layer2_colors @ 0x2D00: CONFIRMED
            int layer2Start = 0x2D00; // 11520
            buf[layer2Start + 0] = layer2Kf0.Value.B;
            buf[layer2Start + 1] = layer2Kf0.Value.G;
            buf[layer2Start + 2] = layer2Kf0.Value.R;
            buf[layer2Start + 3] = layer2Kf0.Value.A;
        }

        return buf;
    }

    [Fact]
    public void CloudDome_Parse_FixedSize_Is23040()
    {
        // spec: §5 — "exactly 23040 bytes": CONFIRMED
        Assert.Equal(23040, CloudDomeBin.FixedSize);
    }

    [Fact]
    public void CloudDome_Parse_Dimensions_12x240()
    {
        // spec: §5.1 — 12 × 240 per layer: CONFIRMED
        byte[] data = BuildCloudDome();
        CloudDomeBin result = EnvironmentBinParsers.ParseCloudDome(data.AsSpan());

        Assert.Equal(12, result.Layer1Colors.Length);
        Assert.Equal(240, result.Layer1Colors[0].Length);
        Assert.Equal(12, result.Layer2Colors.Length);
        Assert.Equal(240, result.Layer2Colors[0].Length);
    }

    [Fact]
    public void CloudDome_Parse_Layer1_Kf0_BGRA()
    {
        // spec: §5.1 — cloud_layer1_colors @ 0x0000: CONFIRMED
        // spec: §5.2 — BGRA byte order: CONFIRMED
        var color = new BgraColor(B: 11, G: 22, R: 33, A: 0);
        byte[] data = BuildCloudDome(layer1Kf0: color);
        CloudDomeBin result = EnvironmentBinParsers.ParseCloudDome(data.AsSpan());

        Assert.Equal(11, result.Layer1Colors[0][0].B);
        Assert.Equal(22, result.Layer1Colors[0][0].G);
        Assert.Equal(33, result.Layer1Colors[0][0].R);
    }

    [Fact]
    public void CloudDome_Parse_Layer2_Kf0_Starts_At_0x2D00()
    {
        // Section 2 starts at byte 0x2D00 = 11520.
        // spec: §5.1 — cloud_layer2_colors @ 0x2D00: CONFIRMED
        var color = new BgraColor(B: 44, G: 55, R: 66, A: 0);
        byte[] data = BuildCloudDome(layer2Kf0: color);
        CloudDomeBin result = EnvironmentBinParsers.ParseCloudDome(data.AsSpan());

        Assert.Equal(44, result.Layer2Colors[0][0].B);
        Assert.Equal(55, result.Layer2Colors[0][0].G);
        Assert.Equal(66, result.Layer2Colors[0][0].R);
    }

    [Fact]
    public void CloudDome_Parse_WrongSize_ThrowsInvalidData()
    {
        byte[] tooShort = new byte[23039];
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseCloudDome(tooShort.AsSpan()));
    }

    // =========================================================================
    // CloudCycleBin tests (Tier 2)
    // =========================================================================

    /// <summary>
    /// Builds a synthetic 70-byte cloud_cycle fixture.
    /// spec: Docs/RE/formats/environment_bins.md §6.1 File layout: CONFIRMED
    /// Total: 10 × 7 = 70 bytes.
    /// </summary>
    private static byte[] BuildCloudCycle(byte[][]? rows = null)
    {
        // spec: §6 — "exactly 70 bytes": CONFIRMED
        var buf = new byte[CloudCycleBin.FixedSize];
        if (rows is not null)
        {
            for (int r = 0; r < Math.Min(rows.Length, 10); r++)
            {
                byte[] row = rows[r];
                int rowBase = r * 7;
                for (int c = 0; c < Math.Min(row.Length, 7); c++)
                    buf[rowBase + c] = row[c];
            }
        }

        return buf;
    }

    [Fact]
    public void CloudCycle_Parse_FixedSize_Is70()
    {
        // spec: §6 — "exactly 70 bytes": CONFIRMED
        Assert.Equal(70, CloudCycleBin.FixedSize);
    }

    [Fact]
    public void CloudCycle_Parse_RowCount_Is10()
    {
        // spec: §6.1 — 10 rows: CONFIRMED
        byte[] data = BuildCloudCycle();
        CloudCycleBin result = EnvironmentBinParsers.ParseCloudCycle(data.AsSpan());
        Assert.Equal(10, result.Rows.Length);
    }

    [Fact]
    public void CloudCycle_Parse_Row0_AllFields()
    {
        // spec: §6.1 — row fields: speed, cloud1_id_0to12h, cloud1_id_12to24h,
        //   cloud2_id_0to6h, cloud2_id_6to12h, cloud2_id_12to18h, cloud2_id_18to24h: CONFIRMED
        var rows = new byte[10][];
        for (int r = 0; r < 10; r++)
            rows[r] = new byte[7];
        // Row 0: speed=5, cloud1_ids=2,3, cloud2_ids=4,5,6,7
        rows[0] = new byte[] { 5, 2, 3, 4, 5, 6, 7 };

        byte[] data = BuildCloudCycle(rows: rows);
        CloudCycleBin result = EnvironmentBinParsers.ParseCloudCycle(data.AsSpan());

        Assert.Equal(5, result.Rows[0].Speed);
        Assert.Equal(2, result.Rows[0].Cloud1Id0To12H);
        Assert.Equal(3, result.Rows[0].Cloud1Id12To24H);
        Assert.Equal(4, result.Rows[0].Cloud2Id0To6H);
        Assert.Equal(5, result.Rows[0].Cloud2Id6To12H);
        Assert.Equal(6, result.Rows[0].Cloud2Id12To18H);
        Assert.Equal(7, result.Rows[0].Cloud2Id18To24H);
    }

    [Fact]
    public void CloudCycle_Parse_Row9_LastRow_Accessible()
    {
        // spec: §6.1 — row 9 at byte offset 63 (9 × 7): CONFIRMED
        var rows = new byte[10][];
        for (int r = 0; r < 10; r++)
            rows[r] = new byte[7];
        rows[9] = new byte[] { 3, 10, 10, 11, 11, 12, 12 };

        byte[] data = BuildCloudCycle(rows: rows);
        CloudCycleBin result = EnvironmentBinParsers.ParseCloudCycle(data.AsSpan());

        Assert.Equal(3, result.Rows[9].Speed);
        Assert.Equal(10, result.Rows[9].Cloud1Id0To12H);
    }

    [Fact]
    public void CloudCycle_Parse_WrongSize_ThrowsInvalidData()
    {
        byte[] tooShort = new byte[69];
        byte[] tooLong = new byte[71];
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseCloudCycle(tooShort.AsSpan()));
        Assert.Throws<InvalidDataException>(() =>
            EnvironmentBinParsers.ParseCloudCycle(tooLong.AsSpan()));
    }

    // =========================================================================
    // Real-VFS smoke tests (skipped when clientdata absent)
    // =========================================================================

    /// <summary>
    /// Absolute path to the local clientdata directory (VFS root).
    /// This directory is NOT committed to the repository — it is user-supplied.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
    /// </summary>
    private const string ClientDataDir =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata";

    private static readonly string InfPath = Path.Combine(ClientDataDir, "data.inf");
    private static readonly string VfsPath = Path.Combine(ClientDataDir, "data", "data.vfs");

    private static bool ClientDataAvailable() =>
        File.Exists(InfPath) && File.Exists(VfsPath);

    [Fact]
    public void Smoke_Area2_MapOption_ParsesWithCorrectFlags()
    {
        // Gated: only runs when real VFS is present.
        if (!ClientDataAvailable())
            return;

        // Area 2 should match standard outdoor pattern [0,0,1,1,1,1,1,0,0,0].
        // spec: Docs/RE/specs/environment.md §6.4 — area 2 flags: CONFIRMED
        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/sky/dat/map_option2.bin");

        MapOptionBin result = EnvironmentBinParsers.ParseMapOption(data);

        // Area 2: standard outdoor pattern [0,0,1,1,1,1,1,0,0,0].
        Assert.Equal(0u, result.IsDungeon); // not a dungeon
        Assert.Equal(0u, result.SightDistance); // free sight range
        Assert.Equal(1u, result.StarDomeEnable);
        Assert.Equal(1u, result.CloudDomeEnable);
        Assert.Equal(1u, result.LensFlareEnable);
        Assert.Equal(1u, result.SunEnable);
        Assert.Equal(1u, result.MoonEnable);
        Assert.Equal(0u, result.IndoorFlag); // outdoor
    }

    [Fact]
    public void Smoke_Area2_Fog_ParsesCorrectStructure()
    {
        // Gated: only runs when real VFS is present.
        if (!ClientDataAvailable())
            return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/sky/dat/fog2.bin");

        FogBin result = EnvironmentBinParsers.ParseFog(data);

        // Basic structural assertions — exact values depend on the real file.
        // spec: §2 — "exactly 204 bytes": CONFIRMED
        Assert.Equal(48, result.FogColors.Length);
        // start_dist in [0.0, 1.0] range per spec.
        Assert.True(result.StartDist >= 0.0f && result.StartDist <= 1.0f,
            $"start_dist {result.StartDist} out of expected [0,1] range");
        Assert.True(result.EndDist >= 0.0f && result.EndDist <= 1.0f,
            $"end_dist {result.EndDist} out of expected [0,1] range");
        // data_load_flag expected 0 per all sampled areas.
        // spec: §2.1 — "Area 0 and area 1 both have value 0": CONFIRMED
        Assert.Equal(0u, result.DataLoadFlag);
    }

    [Fact]
    public void Smoke_Area2_Material_ParsesCorrectDimensions()
    {
        // Gated: only runs when real VFS is present.
        if (!ClientDataAvailable())
            return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/sky/dat/material2.bin");

        MaterialBin result = EnvironmentBinParsers.ParseMaterial(data);

        Assert.Equal(48, result.ColorTable.Length);
        Assert.Equal(51, result.ColorTable[0].Length);
    }

    [Fact]
    public void Smoke_Area2_Light_ParsesWithCanonicalFallback()
    {
        // Gated: only runs when real VFS is present.
        if (!ClientDataAvailable())
            return;

        using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
        ReadOnlyMemory<byte> data = archive.GetFileContent("data/sky/dat/light2.bin");

        LightBin result = EnvironmentBinParsers.ParseLight(data);

        Assert.Equal(48, result.DirectionalKeyframes.Length);
        Assert.Equal(48, result.AmbientKeyframes.Length);
        Assert.Equal(48, result.FogDistanceScalars.Length);
        Assert.Equal(48, result.SecondaryFogScalars.Length);
        Assert.Equal(200, result.RawSectionE.Length);

        // Canonical fallback: spec: §9.4 — scale=1.0, dir=(−7, 7, 20): CONFIRMED
        Assert.Equal(1.0f, result.FallbackScale, precision: 5);
        Assert.Equal(-7.0f, result.FallbackDirX, precision: 5);
        Assert.Equal(7.0f, result.FallbackDirY, precision: 5);
        Assert.Equal(20.0f, result.FallbackDirZ, precision: 5);
    }
}