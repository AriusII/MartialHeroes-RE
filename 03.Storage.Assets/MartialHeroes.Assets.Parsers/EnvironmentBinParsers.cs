using System.Buffers.Binary;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parsers for the per-area environment binary family under <c>data/sky/dat/</c>:
/// <c>map_option%d.bin</c>, <c>fog%d.bin</c>, <c>material%d.bin</c>, <c>light%d.bin</c>,
/// and (tier-2) <c>stardome%d.bin</c>, <c>clouddome%d.bin</c>, <c>cloud_cycle%d.bin</c>.
/// </summary>
/// <remarks>
/// <para>
/// All files are little-endian, have no magic number, and have no version field.
/// spec: Docs/RE/formats/environment_bins.md — overview paragraph.
/// spec: Docs/RE/specs/environment.md §1 Overview — file family and activation conditions.
/// ZERO rendering/engine dependencies.
/// </para>
/// <para>
/// <b>Sibling tolerance — default-tolerate absent siblings (LOADER-RESOLVED).</b>
/// The environment hub does NOT abort the area load when any one of the per-area sibling
/// files is absent. It leaves that subsystem at its built-in default and proceeds.
/// A faithful C# port must therefore use the <c>TryParse*</c> overloads rather than
/// <c>Parse*</c> at call sites that might see a missing file — those overloads return
/// <see langword="null"/> when the buffer is empty and always skip-and-default rather than
/// throwing.
/// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance — LOADER-RESOLVED.
/// </para>
/// <para>
/// <b><c>weather%d_rain.bin</c> — NO LOADER (dead editor data).</b>
/// The shipping client has NO loader for these files. A cross-reference scan finds no
/// read-site that opens or parses them. Rain is generated at runtime from hard-coded
/// constants and a RNG; the 33 <c>_rain.bin</c> files present in the VFS are dead editor
/// data. A faithful 1:1 port must NOT load <c>weather%d_rain.bin</c>; no parser exists
/// for it and none should be created.
/// spec: Docs/RE/formats/environment_bins.md §8 — NO LOADER: LOADER-RESOLVED.
/// </para>
/// </remarks>
public static class EnvironmentBinParsers
{
    // ─── map_option%d.bin ────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>map_option%d.bin</c> per-area master-flags file.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <returns>Decoded <see cref="MapOptionBin"/>.</returns>
    /// <exception cref="InvalidDataException">Buffer is not exactly 40 bytes.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §1 — "exactly 40 bytes (10 × u32 LE)": CONFIRMED
    /// </remarks>
    public static MapOptionBin ParseMapOption(ReadOnlyMemory<byte> data) =>
        ParseMapOption(data.Span);

    /// <inheritdoc cref="ParseMapOption(ReadOnlyMemory{byte})"/>
    public static MapOptionBin ParseMapOption(ReadOnlySpan<byte> span)
    {
        // Fixed size: 40 bytes exactly.
        // spec: Docs/RE/formats/environment_bins.md §1 — "exactly 40 bytes": CONFIRMED
        if (span.Length != MapOptionBin.FixedSize)
            throw new InvalidDataException(
                $"map_option*.bin parse error: expected {MapOptionBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §1.");

        // RECONCILED Campaign 5: the 10 u32 words are MOVE_DUNGEON, SIGHT_FIX, LENSFLARE, STARDOME,
        // CLOUDDOME, SUN, MOON, SKYBOX, MAPHIDE, reserved — NOT water_enable/water_y (an IDA-name
        // misread, disproved by the .txt↔.bin cross-reference over 64 area pairs).
        // spec: Docs/RE/formats/environment_bins.md §1.1 (field table, .txt↔.bin cross-referenced).

        // MOVE_DUNGEON u32 @ 0x00. spec: §1.1 — is_dungeon u32 @ 0x00: CONFIRMED
        uint isDungeon = BinaryPrimitives.ReadUInt32LittleEndian(span[0x00..]);
        // SIGHT_FIX u32 @ 0x04. spec: §1.1 — sight_distance u32 @ 0x04: CONFIRMED
        uint sightDistance = BinaryPrimitives.ReadUInt32LittleEndian(span[0x04..]);
        // LENSFLARE u32 @ 0x08. spec: §1.1 — lensflare_enable u32 @ 0x08: CONFIRMED
        uint lensFlareEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);
        // STARDOME u32 @ 0x0C. spec: §1.1 — stardome_enable u32 @ 0x0C: CONFIRMED
        uint starDomeEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0C..]);
        // CLOUDDOME u32 @ 0x10. spec: §1.1 — clouddome_enable u32 @ 0x10: CONFIRMED
        uint cloudDomeEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x10..]);
        // SUN u32 @ 0x14. spec: §1.1 — sun_enable u32 @ 0x14: CONFIRMED
        uint sunEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x14..]);
        // MOON u32 @ 0x18. spec: §1.1 — moon_enable u32 @ 0x18 (its own word, distinct from SUN): CONFIRMED
        uint moonEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x18..]);
        // SKYBOX u32 @ 0x1C. spec: §1.1 — skybox_enable u32 @ 0x1C: CONFIRMED (always 0)
        uint skyboxEnable = BinaryPrimitives.ReadUInt32LittleEndian(span[0x1C..]);
        // MAPHIDE u32 @ 0x20. spec: §1.1 — indoor_flag u32 @ 0x20: CONFIRMED
        uint indoorFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[0x20..]);
        // _reserved_ u32 @ 0x24. spec: §1.1 — _reserved_ u32 @ 0x24: SAMPLE-VERIFIED (always 0)
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(span[0x24..]);

        return new MapOptionBin
        {
            IsDungeon = isDungeon,
            SightDistance = sightDistance,
            LensFlareEnable = lensFlareEnable,
            StarDomeEnable = starDomeEnable,
            CloudDomeEnable = cloudDomeEnable,
            SunEnable = sunEnable,
            MoonEnable = moonEnable,
            SkyboxEnable = skyboxEnable,
            IndoorFlag = indoorFlag,
            Reserved = reserved,
        };
    }

    /// <summary>
    /// Tolerant sibling overload: returns <see langword="null"/> if the buffer is empty or absent,
    /// rather than throwing. Used when the hub default-tolerates a missing <c>map_option%d.bin</c>.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance —
    ///   "default-tolerate a missing sibling: skip-and-default, never throw": LOADER-RESOLVED.
    /// </remarks>
    public static MapOptionBin? TryParseMapOption(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) return null;
        return ParseMapOption(data.Span);
    }

    // ─── fog%d.bin ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>fog%d.bin</c> per-area fog parameters file.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <returns>Decoded <see cref="FogBin"/>.</returns>
    /// <exception cref="InvalidDataException">Buffer is not exactly 204 bytes.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §2 — "exactly 204 bytes": CONFIRMED
    /// Layout: f32 start_dist @ 0x00, f32 end_dist @ 0x04, u32 data_load_flag @ 0x08,
    ///         u8[192] fog_colors[48] @ 0x0C.
    /// </remarks>
    public static FogBin ParseFog(ReadOnlyMemory<byte> data) =>
        ParseFog(data.Span);

    /// <inheritdoc cref="ParseFog(ReadOnlyMemory{byte})"/>
    public static FogBin ParseFog(ReadOnlySpan<byte> span)
    {
        // Fixed size: 204 bytes exactly.
        // spec: Docs/RE/formats/environment_bins.md §2 — "exactly 204 bytes": CONFIRMED
        if (span.Length != FogBin.FixedSize)
            throw new InvalidDataException(
                $"fog*.bin parse error: expected {FogBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §2.");

        // start_dist f32 @ 0x00. spec: §2.1 — start_dist f32 @ 0x00: CONFIRMED
        float startDist = BinaryPrimitives.ReadSingleLittleEndian(span[0x00..]);
        // end_dist f32 @ 0x04. spec: §2.1 — end_dist f32 @ 0x04: CONFIRMED
        float endDist = BinaryPrimitives.ReadSingleLittleEndian(span[0x04..]);
        // data_load_flag u32 @ 0x08. spec: §2.1 — data_load_flag u32 @ 0x08: CONFIRMED
        uint dataLoadFlag = BinaryPrimitives.ReadUInt32LittleEndian(span[0x08..]);

        // fog_colors[48] u8[192] @ 0x0C — 48 BGRA entries, 4 bytes each.
        // spec: §2.1 — fog_colors[48] u8[192] @ 0x0C: CONFIRMED
        // spec: §2.2 — BGRA byte order: B@[0], G@[1], R@[2], A@[3]: CONFIRMED
        var colors = new BgraColor[FogBin.KeyframeCount];
        int colorBase = 0x0C;
        for (int i = 0; i < FogBin.KeyframeCount; i++)
        {
            int off = colorBase + i * 4;
            colors[i] = new BgraColor(
                B: span[off],
                G: span[off + 1],
                R: span[off + 2],
                A: span[off + 3]);
        }

        return new FogBin
        {
            StartDist = startDist,
            EndDist = endDist,
            DataLoadFlag = dataLoadFlag,
            FogColors = colors,
        };
    }

    /// <summary>
    /// Tolerant sibling overload: returns <see langword="null"/> if the buffer is empty or absent.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance —
    ///   "default-tolerate a missing sibling: skip-and-default, never throw": LOADER-RESOLVED.
    /// </remarks>
    public static FogBin? TryParseFog(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) return null;
        return ParseFog(data.Span);
    }

    // ─── material%d.bin ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>material%d.bin</c> sun/sky material colour-table file.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <returns>Decoded <see cref="MaterialBin"/>.</returns>
    /// <exception cref="InvalidDataException">Buffer is not exactly 9792 bytes.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §3 — "exactly 9792 bytes (48 × 51 × 4)": CONFIRMED
    /// Layout: f32[48][51] row-major, row k at byte k × 204.
    /// </remarks>
    public static MaterialBin ParseMaterial(ReadOnlyMemory<byte> data) =>
        ParseMaterial(data.Span);

    /// <inheritdoc cref="ParseMaterial(ReadOnlyMemory{byte})"/>
    public static MaterialBin ParseMaterial(ReadOnlySpan<byte> span)
    {
        // Fixed size: 9792 bytes exactly.
        // spec: Docs/RE/formats/environment_bins.md §3 — "exactly 9792 bytes": CONFIRMED
        if (span.Length != MaterialBin.FixedSize)
            throw new InvalidDataException(
                $"material*.bin parse error: expected {MaterialBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §3.");

        // color_table f32[48][51] @ 0x0000. Row k starts at byte k × 204.
        // spec: §3.1 — color_table f32[48][51] row-major @ 0x0000: CONFIRMED
        // Row stride = 51 × 4 = 204 bytes.
        const int rowStride = MaterialBin.ValuesPerKeyframe * 4; // 204
        var table = new float[MaterialBin.KeyframeCount][];
        for (int k = 0; k < MaterialBin.KeyframeCount; k++)
        {
            int rowOffset = k * rowStride;
            var row = new float[MaterialBin.ValuesPerKeyframe];
            for (int j = 0; j < MaterialBin.ValuesPerKeyframe; j++)
            {
                // f32 LE at row_offset + j × 4.
                // spec: §3.1 — f32[48][51] LE, row-major: CONFIRMED
                row[j] = BinaryPrimitives.ReadSingleLittleEndian(span[(rowOffset + j * 4)..]);
            }

            table[k] = row;
        }

        return new MaterialBin { ColorTable = table };
    }

    /// <summary>
    /// Tolerant sibling overload: returns <see langword="null"/> if the buffer is empty or absent.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance —
    ///   "default-tolerate a missing sibling: skip-and-default, never throw": LOADER-RESOLVED.
    /// </remarks>
    public static MaterialBin? TryParseMaterial(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) return null;
        return ParseMaterial(data.Span);
    }

    // ─── light%d.bin ─────────────────────────────────────────────────────────

    // Section offsets (revised, sample-verified).
    // spec: Docs/RE/formats/environment_bins.md §9.1 Revised section layout: CONFIRMED

    // Section A — Directional light keyframes: 0x0000–0x08FF (2304 bytes = 48 × 48 bytes).
    private const int LightSectionAOffset = 0x0000; // spec: §9.1 Section A @ 0x0000: CONFIRMED
    private const int LightSectionASize = 2304;

    // Gap A (48 bytes, all zeros): 0x0900–0x092F.
    // spec: §9.1 — Gap A @ 0x0900 (48 bytes, all zeros): SAMPLE-VERIFIED

    // Section B — Ambient light keyframes: 0x0930–0x122F (2304 bytes = 48 × 48 bytes).
    private const int LightSectionBOffset = 0x0930; // spec: §9.1 Section B @ 0x0930: CONFIRMED
    private const int LightSectionBSize = 2304;

    // Gap B (48 bytes, all zeros): 0x1230–0x125F.
    // spec: §9.1 — Gap B @ 0x1230 (48 bytes, all zeros): SAMPLE-VERIFIED

    // Section C — Fog-distance scalars: 0x1260–0x131F (192 bytes = 48 × f32).
    private const int LightSectionCOffset = 0x1260; // spec: §9.1 Section C @ 0x1260: CONFIRMED
    private const int LightSectionCCount = 48;

    // Section D — Secondary fog scalars: 0x1320–0x13DF (192 bytes = 48 × f32).
    private const int LightSectionDOffset = 0x1320; // spec: §9.1 Section D @ 0x1320: SAMPLE-VERIFIED
    private const int LightSectionDCount = 48;

    // Section E — Reserved f32 array: 0x13E0–0x14A7 (200 bytes, all zeros).
    private const int LightSectionEOffset = 0x13E0; // spec: §9.1 Section E @ 0x13E0: SAMPLE-VERIFIED (all zeros)
    private const int LightSectionESize = 200;

    // Padding (8 bytes, all zeros): 0x14A8–0x14AF.
    // spec: §9.1 — Padding @ 0x14A8 (8 bytes, all zeros): SAMPLE-VERIFIED

    // Fallback directional light: 0x14B0–0x14BF (16 bytes = 4 × f32).
    private const int LightFallbackOffset = 0x14B0; // spec: §9.1 Fallback light @ 0x14B0: CONFIRMED

    // Keyframe stride within sections A and B: 48 bytes = 3 × float4.
    // spec: §9.2 — 48 bytes per keyframe slot: CONFIRMED
    private const int LightKeyframeStride = 48;

    /// <summary>
    /// Parses a <c>light%d.bin</c> sky-lighting keyframe file.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <returns>Decoded <see cref="LightBin"/>.</returns>
    /// <exception cref="InvalidDataException">Buffer is not exactly 5312 bytes.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §9.1 Revised section layout: CONFIRMED
    /// spec: Docs/RE/formats/environment_bins.md §9.2 Keyframe structure: CONFIRMED
    /// spec: Docs/RE/formats/environment_bins.md §9.3 Section C: CONFIRMED
    /// spec: Docs/RE/formats/environment_bins.md §9.4 Fallback light: CONFIRMED
    /// Total: 2304 + 48 + 2304 + 48 + 192 + 192 + 200 + 8 + 16 = 5312 bytes.
    /// </remarks>
    public static LightBin ParseLight(ReadOnlyMemory<byte> data) =>
        ParseLight(data.Span, data);

    /// <inheritdoc cref="ParseLight(ReadOnlyMemory{byte})"/>
    public static LightBin ParseLight(ReadOnlySpan<byte> span)
    {
        return ParseLight(span, ReadOnlyMemory<byte>.Empty);
    }

    private static LightBin ParseLight(ReadOnlySpan<byte> span, ReadOnlyMemory<byte> backing)
    {
        // Fixed size: 5312 bytes exactly.
        // spec: Docs/RE/formats/environment_bins.md §9.1 — 5312 bytes total: CONFIRMED
        if (span.Length != LightBin.FixedSize)
            throw new InvalidDataException(
                $"light*.bin parse error: expected {LightBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §9.1.");

        // Section A — 48 directional-light keyframes @ 0x0000.
        // spec: §9.1 Section A Directional light @ 0x0000 (2304 bytes, 48 × 48 bytes): CONFIRMED
        var dirKf = ReadLightKeyframes(span, LightSectionAOffset, LightBin.KeyframeCount);

        // Section B — 48 ambient-light keyframes @ 0x0930.
        // spec: §9.1 Section B Ambient light @ 0x0930 (2304 bytes, 48 × 48 bytes): CONFIRMED
        var ambKf = ReadLightKeyframes(span, LightSectionBOffset, LightBin.KeyframeCount);

        // Section C — 48 fog-distance scalars (f32) @ 0x1260.
        // spec: §9.1 Section C Fog-distance scalar @ 0x1260 (192 bytes, 48 × f32): CONFIRMED
        var fogScalars = new float[LightSectionCCount];
        for (int i = 0; i < LightSectionCCount; i++)
            fogScalars[i] = BinaryPrimitives.ReadSingleLittleEndian(
                span[(LightSectionCOffset + i * 4)..]);

        // Section D — 48 secondary fog scalars (f32) @ 0x1320.
        // spec: §9.1 Section D Secondary fog scalar @ 0x1320 (192 bytes, 48 × f32): SAMPLE-VERIFIED
        var secScalars = new float[LightSectionDCount];
        for (int i = 0; i < LightSectionDCount; i++)
            secScalars[i] = BinaryPrimitives.ReadSingleLittleEndian(
                span[(LightSectionDOffset + i * 4)..]);

        // Section E — reserved f32 array @ 0x13E0 (200 bytes, all zeros in samples).
        // spec: §9.1 Section E Reserved f32 array @ 0x13E0 (200 bytes): SAMPLE-VERIFIED (all zeros)
        ReadOnlyMemory<byte> rawSectionE = backing.IsEmpty
            ? span.Slice(LightSectionEOffset, LightSectionESize).ToArray()
            : backing.Slice(LightSectionEOffset, LightSectionESize);

        // Fallback directional light @ 0x14B0 (4 × f32).
        // spec: §9.4 — scale @ 0x14B0, dir_X @ 0x14B4, dir_Y @ 0x14B8, dir_Z @ 0x14BC: CONFIRMED
        float fallbackScale = BinaryPrimitives.ReadSingleLittleEndian(span[LightFallbackOffset..]);
        float fallbackDirX = BinaryPrimitives.ReadSingleLittleEndian(span[(LightFallbackOffset + 4)..]);
        float fallbackDirY = BinaryPrimitives.ReadSingleLittleEndian(span[(LightFallbackOffset + 8)..]);
        float fallbackDirZ = BinaryPrimitives.ReadSingleLittleEndian(span[(LightFallbackOffset + 12)..]);

        return new LightBin
        {
            DirectionalKeyframes = dirKf,
            AmbientKeyframes = ambKf,
            FogDistanceScalars = fogScalars,
            SecondaryFogScalars = secScalars,
            RawSectionE = rawSectionE,
            FallbackScale = fallbackScale,
            FallbackDirX = fallbackDirX,
            FallbackDirY = fallbackDirY,
            FallbackDirZ = fallbackDirZ,
        };
    }

    /// <summary>
    /// Tolerant sibling overload: returns <see langword="null"/> if the buffer is empty or absent.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance —
    ///   "default-tolerate a missing sibling: skip-and-default, never throw": LOADER-RESOLVED.
    /// </remarks>
    public static LightBin? TryParseLight(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) return null;
        return ParseLight(data.Span, data);
    }

    private static LightingKeyframe[] ReadLightKeyframes(
        ReadOnlySpan<byte> span, int sectionOffset, int count)
    {
        // Each slot: 48 bytes = color_A (f32×4) + color_B (f32×4) + color_C (f32×4).
        // spec: Docs/RE/formats/environment_bins.md §9.2 — 3 × float4 per slot: CONFIRMED
        var kf = new LightingKeyframe[count];
        for (int i = 0; i < count; i++)
        {
            int slotBase = sectionOffset + i * LightKeyframeStride;

            // color_A f32×4 @ slot+0x00. spec: §9.2 — color_A RGBA @ slot+0x00: CONFIRMED
            var colorA = new float[4];
            colorA[0] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x00)..]);
            colorA[1] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x04)..]);
            colorA[2] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x08)..]);
            colorA[3] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x0C)..]);

            // color_B f32×4 @ slot+0x10. spec: §9.2 — color_B RGBA @ slot+0x10: CODE-CONFIRMED
            var colorB = new float[4];
            colorB[0] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x10)..]);
            colorB[1] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x14)..]);
            colorB[2] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x18)..]);
            colorB[3] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x1C)..]);

            // color_C f32×4 @ slot+0x20.
            // PRESENT-BUT-UNREAD (LOADER-RESOLVED): the loader/time-update read-sequence touches
            // color_A (+0x00) and color_B (+0x10) and stops — there is NO read-site for the
            // third float4 group at +0x20. All zeros in all sampled data.
            // A faithful parser surfaces the bytes (done here for completeness) but must NOT
            // feed color_C to any lighting math; the original ignores it entirely.
            // spec: Docs/RE/formats/environment_bins.md §9.2 — color_C f32×4 @ slot+0x20:
            //   "Present-but-UNREAD; no read-site; unconsumed": LOADER-RESOLVED.
            var colorC = new float[4];
            colorC[0] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x20)..]);
            colorC[1] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x24)..]);
            colorC[2] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x28)..]);
            colorC[3] = BinaryPrimitives.ReadSingleLittleEndian(span[(slotBase + 0x2C)..]);

            kf[i] = new LightingKeyframe
            {
                ColorA = colorA,
                ColorB = colorB,
                ColorC = colorC,
            };
        }

        return kf;
    }

    // ─── stardome%d.bin (Tier 2) ─────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>stardome%d.bin</c> star colour grid file.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <returns>Decoded <see cref="StarDomeBin"/>.</returns>
    /// <exception cref="InvalidDataException">Buffer is not exactly 9216 bytes.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §4 — "exactly 9216 bytes (12 × 192 × 4)": CONFIRMED
    /// </remarks>
    public static StarDomeBin ParseStarDome(ReadOnlyMemory<byte> data) =>
        ParseStarDome(data.Span);

    /// <inheritdoc cref="ParseStarDome(ReadOnlyMemory{byte})"/>
    public static StarDomeBin ParseStarDome(ReadOnlySpan<byte> span)
    {
        // Fixed size: 9216 bytes exactly.
        // spec: Docs/RE/formats/environment_bins.md §4 — "exactly 9216 bytes": CONFIRMED
        if (span.Length != StarDomeBin.FixedSize)
            throw new InvalidDataException(
                $"stardome*.bin parse error: expected {StarDomeBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §4.");

        // star_colors u8[12][192][4] @ 0x0000.
        // spec: §4.1 — star_colors u8[12][192][4] @ 0x0000: CONFIRMED
        // spec: §4.2 — BGRA byte order: CONFIRMED
        var starColors = new BgraColor[StarDomeBin.KeyframeCount][];
        int offset = 0;
        for (int k = 0; k < StarDomeBin.KeyframeCount; k++)
        {
            var frame = new BgraColor[StarDomeBin.StarsPerKeyframe];
            for (int s = 0; s < StarDomeBin.StarsPerKeyframe; s++)
            {
                frame[s] = new BgraColor(
                    B: span[offset],
                    G: span[offset + 1],
                    R: span[offset + 2],
                    A: span[offset + 3]);
                offset += 4;
            }

            starColors[k] = frame;
        }

        return new StarDomeBin { StarColors = starColors };
    }

    /// <summary>
    /// Tolerant sibling overload: returns <see langword="null"/> if the buffer is empty or absent.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance —
    ///   "default-tolerate a missing sibling: skip-and-default, never throw": LOADER-RESOLVED.
    /// </remarks>
    public static StarDomeBin? TryParseStarDome(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) return null;
        return ParseStarDome(data.Span);
    }

    // ─── clouddome%d.bin (Tier 2) ────────────────────────────────────────────

    /// <summary>
    /// Parses a <c>clouddome%d.bin</c> cloud-dome colour grid file.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <returns>Decoded <see cref="CloudDomeBin"/>.</returns>
    /// <exception cref="InvalidDataException">Buffer is not exactly 23040 bytes.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §5 — "exactly 23040 bytes (2 × 11520)": CONFIRMED
    /// Section 1 @ 0x0000 (11520 bytes = 12 × 240 × 4): cloud_layer1_colors.
    /// Section 2 @ 0x2D00 (11520 bytes = 12 × 240 × 4): cloud_layer2_colors.
    /// </remarks>
    public static CloudDomeBin ParseCloudDome(ReadOnlyMemory<byte> data) =>
        ParseCloudDome(data.Span);

    /// <inheritdoc cref="ParseCloudDome(ReadOnlyMemory{byte})"/>
    public static CloudDomeBin ParseCloudDome(ReadOnlySpan<byte> span)
    {
        // Fixed size: 23040 bytes exactly.
        // spec: Docs/RE/formats/environment_bins.md §5 — "exactly 23040 bytes": CONFIRMED
        if (span.Length != CloudDomeBin.FixedSize)
            throw new InvalidDataException(
                $"clouddome*.bin parse error: expected {CloudDomeBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §5.");

        // Section 1 — cloud_layer1_colors u8[12][240][4] @ 0x0000.
        // spec: §5.1 — cloud_layer1_colors u8[12][240][4] @ 0x0000: CONFIRMED
        var layer1 = ReadCloudDomeLayer(span, offset: 0x0000);

        // Section 2 — cloud_layer2_colors u8[12][240][4] @ 0x2D00.
        // spec: §5.1 — cloud_layer2_colors u8[12][240][4] @ 0x2D00: CONFIRMED
        var layer2 = ReadCloudDomeLayer(span, offset: 0x2D00);

        return new CloudDomeBin
        {
            Layer1Colors = layer1,
            Layer2Colors = layer2,
        };
    }

    /// <summary>
    /// Tolerant sibling overload: returns <see langword="null"/> if the buffer is empty or absent.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance —
    ///   "default-tolerate a missing sibling: skip-and-default, never throw": LOADER-RESOLVED.
    /// </remarks>
    public static CloudDomeBin? TryParseCloudDome(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) return null;
        return ParseCloudDome(data.Span);
    }

    private static BgraColor[][] ReadCloudDomeLayer(ReadOnlySpan<byte> span, int offset)
    {
        // One layer: 12 keyframes × 240 vertices × 4 bytes BGRA.
        // spec: Docs/RE/formats/environment_bins.md §5.1 — 12 × 240 × 4 per layer: CONFIRMED
        // spec: §5.2 — BGRA byte order: CONFIRMED
        var layer = new BgraColor[CloudDomeBin.KeyframeCount][];
        for (int k = 0; k < CloudDomeBin.KeyframeCount; k++)
        {
            var frame = new BgraColor[CloudDomeBin.VerticesPerKeyframe];
            for (int v = 0; v < CloudDomeBin.VerticesPerKeyframe; v++)
            {
                frame[v] = new BgraColor(
                    B: span[offset],
                    G: span[offset + 1],
                    R: span[offset + 2],
                    A: span[offset + 3]);
                offset += 4;
            }

            layer[k] = frame;
        }

        return layer;
    }

    // ─── cloud_cycle%d.bin (Tier 2) ──────────────────────────────────────────

    /// <summary>
    /// Parses a <c>cloud_cycle%d.bin</c> cloud animation schedule file.
    /// </summary>
    /// <param name="data">Raw file bytes.</param>
    /// <returns>Decoded <see cref="CloudCycleBin"/>.</returns>
    /// <exception cref="InvalidDataException">Buffer is not exactly 70 bytes.</exception>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §6 — "exactly 70 bytes (10 × 7 u8)": CONFIRMED
    /// </remarks>
    public static CloudCycleBin ParseCloudCycle(ReadOnlyMemory<byte> data) =>
        ParseCloudCycle(data.Span);

    /// <inheritdoc cref="ParseCloudCycle(ReadOnlyMemory{byte})"/>
    public static CloudCycleBin ParseCloudCycle(ReadOnlySpan<byte> span)
    {
        // Fixed size: 70 bytes exactly.
        // spec: Docs/RE/formats/environment_bins.md §6 — "exactly 70 bytes": CONFIRMED
        if (span.Length != CloudCycleBin.FixedSize)
            throw new InvalidDataException(
                $"cloud_cycle*.bin parse error: expected {CloudCycleBin.FixedSize} bytes, " +
                $"got {span.Length}. " +
                "spec: Docs/RE/formats/environment_bins.md §6.");

        // 10 rows × 7 bytes each.
        // spec: §6.1 — 10 rows, 7 u8 values per row: CONFIRMED
        var rows = new CloudCycleRow[CloudCycleBin.RowCount];
        for (int r = 0; r < CloudCycleBin.RowCount; r++)
        {
            int rowBase = r * CloudCycleBin.BytesPerRow;
            // speed u8 @ col[0]. spec: §6.1 — speed u8 @ row×7+0: CONFIRMED
            // cloud1_id_0to12h u8 @ col[1]. spec: §6.1 — cloud1_id_0to12h @ row×7+1: CONFIRMED
            // cloud1_id_12to24h u8 @ col[2]. spec: §6.1 — cloud1_id_12to24h @ row×7+2: CONFIRMED
            // cloud2_id_0to6h u8 @ col[3]. spec: §6.1 — cloud2_id_0to6h @ row×7+3: CONFIRMED
            // cloud2_id_6to12h u8 @ col[4]. spec: §6.1 — cloud2_id_6to12h @ row×7+4: CONFIRMED
            // cloud2_id_12to18h u8 @ col[5]. spec: §6.1 — cloud2_id_12to18h @ row×7+5: CONFIRMED
            // cloud2_id_18to24h u8 @ col[6]. spec: §6.1 — cloud2_id_18to24h @ row×7+6: CONFIRMED
            rows[r] = new CloudCycleRow(
                Speed: span[rowBase + 0],
                Cloud1Id0To12H: span[rowBase + 1],
                Cloud1Id12To24H: span[rowBase + 2],
                Cloud2Id0To6H: span[rowBase + 3],
                Cloud2Id6To12H: span[rowBase + 4],
                Cloud2Id12To18H: span[rowBase + 5],
                Cloud2Id18To24H: span[rowBase + 6]);
        }

        return new CloudCycleBin { Rows = rows };
    }

    /// <summary>
    /// Tolerant sibling overload: returns <see langword="null"/> if the buffer is empty or absent.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §Overview Sibling tolerance —
    ///   "default-tolerate a missing sibling: skip-and-default, never throw": LOADER-RESOLVED.
    /// </remarks>
    public static CloudCycleBin? TryParseCloudCycle(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) return null;
        return ParseCloudCycle(data.Span);
    }
}