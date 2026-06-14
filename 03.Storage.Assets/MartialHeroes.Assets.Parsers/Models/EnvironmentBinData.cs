namespace MartialHeroes.Assets.Parsers.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  map_option%d.bin  — Per-area master flags
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of <c>map_option%d.bin</c>.
/// Fixed size: 40 bytes (10 × u32 LE, no magic, no version).
/// Per-area master flags: dungeon/sight gating plus which sky subsystems are enabled.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §1.1 Field table: CONFIRMED
/// RECONCILED Campaign 5: this file holds NO water field. The old <c>WaterEnable</c>/<c>WaterY</c>
/// at 0x00/0x04 were an IDA-name misread of <c>MOVE_DUNGEON</c> / <c>SIGHT_FIX</c>, disproved by a
/// <c>.txt</c>↔<c>.bin</c> cross-reference over 64 area pairs. spec: §1 ⚠ conflict-reconciled note.
/// </remarks>
public sealed class MapOptionBin
{
    /// <summary>Fixed file size: 40 bytes (10 × u32 LE).</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1 — "exactly 40 bytes": CONFIRMED</remarks>
    public const int FixedSize = 40;

    /// <summary>
    /// <c>MOVE_DUNGEON</c>: 0 = outdoor / field; 1 = dungeon-type movement zone.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — is_dungeon u32 @ 0x00: CONFIRMED</remarks>
    public required uint IsDungeon { get; init; }

    /// <summary>
    /// <c>SIGHT_FIX</c>: camera sight-clamp distance. 0 = free range; otherwise a fixed clamp
    /// (observed e.g. 300, 800). NOT a water surface Y.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — sight_distance u32 @ 0x04: CONFIRMED</remarks>
    public required uint SightDistance { get; init; }

    /// <summary>
    /// <c>LENSFLARE</c>: 1 = sun lens-flare screen-space sprites rendered.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — lensflare_enable u32 @ 0x08: CONFIRMED</remarks>
    public required uint LensFlareEnable { get; init; }

    /// <summary>
    /// <c>STARDOME</c>: 1 = star-dome point sprites rendered.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — stardome_enable u32 @ 0x0C: CONFIRMED</remarks>
    public required uint StarDomeEnable { get; init; }

    /// <summary>
    /// <c>CLOUDDOME</c>: 1 = cloud-dome hemisphere rendered.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — clouddome_enable u32 @ 0x10: CONFIRMED</remarks>
    public required uint CloudDomeEnable { get; init; }

    /// <summary>
    /// <c>SUN</c>: 1 = sun billboard. Stored separately from <see cref="MoonEnable"/>.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — sun_enable u32 @ 0x14: CONFIRMED</remarks>
    public required uint SunEnable { get; init; }

    /// <summary>
    /// <c>MOON</c>: 1 = moon billboard. Stored as its own u32 word, distinct from
    /// <see cref="SunEnable"/> at 0x14 (both carry the same value in every observed area).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — moon_enable u32 @ 0x18: CONFIRMED (as stored)</remarks>
    public required uint MoonEnable { get; init; }

    /// <summary>
    /// <c>SKYBOX</c>: 1 = load skybox mesh from sky%d.box. Always 0 — no .box files present in VFS.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — skybox_enable u32 @ 0x1C: CONFIRMED (value always 0)</remarks>
    public required uint SkyboxEnable { get; init; }

    /// <summary>
    /// <c>MAPHIDE</c>: 0 = outdoor; 1 = indoor / dungeon lighting (suppresses most sky subsystems).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — indoor_flag u32 @ 0x20: CONFIRMED</remarks>
    public required uint IndoorFlag { get; init; }

    /// <summary>
    /// Reserved; always 0 in all sampled areas.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §1.1 — _reserved_ u32 @ 0x24: SAMPLE-VERIFIED (value always 0)</remarks>
    public required uint Reserved { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  fog%d.bin  — Per-area fog parameters
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One 4-byte BGRA fog colour entry from <c>fog%d.bin</c>.
/// Blue at byte [0], Green at byte [1], Red at byte [2], Alpha at byte [3].
/// Alpha is always 0 in all sampled data.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §2.2 BGRA colour encoding: CONFIRMED
/// </remarks>
public readonly record struct BgraColor(byte B, byte G, byte R, byte A);

/// <summary>
/// Decoded result of <c>fog%d.bin</c>.
/// Fixed size: 204 bytes (no magic, no version).
/// Contains fog start/end distances and a 48-keyframe BGRA colour table.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §2.1 Field table: CONFIRMED
/// Total: 4 + 4 + 4 + 192 = 204 bytes.
/// </remarks>
public sealed class FogBin
{
    /// <summary>Fixed file size: 204 bytes.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §2 — "exactly 204 bytes": CONFIRMED</remarks>
    public const int FixedSize = 204;

    /// <summary>Number of day/night keyframes in the fog colour table.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §2.1 — fog_colors[48]: CONFIRMED</remarks>
    public const int KeyframeCount = 48;

    /// <summary>
    /// Fog start distance. Observed range 0.0–1.0; fraction of the configured view range.
    /// Area 0 sample: 0.5.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §2.1 — start_dist f32 @ 0x00: CONFIRMED</remarks>
    public required float StartDist { get; init; }

    /// <summary>
    /// Fog end distance. Same scale as <see cref="StartDist"/>. Area 0 sample: 0.9.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §2.1 — end_dist f32 @ 0x04: CONFIRMED</remarks>
    public required float EndDist { get; init; }

    /// <summary>
    /// 0 = derive fog colour from the material colour table at runtime;
    /// 1 = use <see cref="FogColors"/> array directly.
    /// All sampled areas use value 0.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §2.1 — data_load_flag u32 @ 0x08: CONFIRMED</remarks>
    public required uint DataLoadFlag { get; init; }

    /// <summary>
    /// 48 BGRA fog colour entries, one per day/night keyframe.
    /// Keyframe 0 = midnight; keyframe 24 = noon.
    /// Length is always <see cref="KeyframeCount"/> = 48.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §2.1 — fog_colors[48] u8[192] @ 0x0C: CONFIRMED</remarks>
    public required BgraColor[] FogColors { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  material%d.bin  — Sun/sky material colour table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of <c>material%d.bin</c>.
/// Fixed size: 9792 bytes (48 × 51 × 4, no magic, no version).
/// Contains a flat f32 colour table: 48 keyframes × 51 float32 values per keyframe.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §3.1 File layout: CONFIRMED
/// Row k starts at byte k × 204. Each row is 51 f32 = 204 bytes.
/// </remarks>
public sealed class MaterialBin
{
    /// <summary>Fixed file size: 9792 bytes (48 × 51 × 4).</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §3 — "exactly 9792 bytes": CONFIRMED</remarks>
    public const int FixedSize = 9792;

    /// <summary>Number of day/night keyframes.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §3.1 — 48 keyframes: CONFIRMED</remarks>
    public const int KeyframeCount = 48;

    /// <summary>Number of f32 values per keyframe row.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §3.1 — 51 floats per row: CONFIRMED</remarks>
    public const int ValuesPerKeyframe = 51;

    /// <summary>
    /// Colour table: <c>color_table[keyframe][index]</c>.
    /// Dimensions: [48][51]. Row-major, f32 LE.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §3.1 — color_table f32[48][51] @ 0x0000: CONFIRMED
    /// Index assignments per row (see §3.2):
    ///   [0..3]   sky_haze RGBA — CODE-CONFIRMED
    ///   [4..7]   sun_color RGBA — CODE-CONFIRMED
    ///   [12..15] secondary_sky_color RGBA — CODE-CONFIRMED
    ///   [17..20] cloud_color_A RGBA — CODE-CONFIRMED
    ///   [21..24] cloud_color_B RGBA — CODE-CONFIRMED
    ///   [29..32] ambient_sky_color RGBA — CODE-CONFIRMED
    ///   [34..36] emissive_sky RGB — CODE-CONFIRMED
    ///   [38..40] specular_sky RGB — CODE-CONFIRMED
    ///   Remaining indices: loaded but usage not traced — PROPOSED
    /// </remarks>
    public required float[][] ColorTable { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  light%d.bin  — Directional + ambient lighting keyframes
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One 48-byte lighting keyframe slot from <c>light%d.bin</c>.
/// Contains three float4 colour groups (color_A, color_B, color_C).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §9.2 Keyframe structure: CONFIRMED
/// Slot stride: 48 bytes = 3 × float4 = 12 × f32 LE.
/// </remarks>
public sealed class LightingKeyframe
{
    /// <summary>
    /// Primary colour (RGBA f32×4) at slot offset +0x00.
    /// Diffuse for section A (directional); ambient for section B.
    /// Alpha is always 0 in all sampled data.
    /// Length: 4.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.2 — color_A RGBA @ slot+0x00: CONFIRMED</remarks>
    public required float[] ColorA { get; init; }

    /// <summary>
    /// Secondary colour (RGBA f32×4) at slot offset +0x10.
    /// Possibly specular for section A.
    /// Length: 4.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.2 — color_B RGBA @ slot+0x10: CODE-CONFIRMED</remarks>
    public required float[] ColorB { get; init; }

    /// <summary>
    /// Third colour group (RGBA f32×4) at slot offset +0x20.
    /// All zeros in all sampled data. Reserved or unused.
    /// Length: 4.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.2 — color_C RGBA @ slot+0x20: SAMPLE-VERIFIED (all zeros)</remarks>
    public required float[] ColorC { get; init; }
}

/// <summary>
/// Decoded result of <c>light%d.bin</c>.
/// Fixed size: 5312 bytes (0x14C0). No magic, no version.
/// Contains 48 directional keyframes, 48 ambient keyframes,
/// fog-distance scalars, secondary fog scalars, a reserved f32 array,
/// padding, and a fallback directional-light vector.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §9.1 Revised section layout: CONFIRMED
/// Total: 2304 + 48 + 2304 + 48 + 192 + 192 + 200 + 8 + 16 = 5312 bytes.
/// </remarks>
public sealed class LightBin
{
    /// <summary>Fixed file size: 5312 bytes (0x14C0).</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.1 — 5312 bytes total: CONFIRMED</remarks>
    public const int FixedSize = 5312;

    /// <summary>Number of keyframes in sections A and B.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.1 — 48 keyframes: CONFIRMED</remarks>
    public const int KeyframeCount = 48;

    /// <summary>
    /// 48 directional-light keyframe slots.
    /// Section A: bytes 0x0000–0x08FF (2304 bytes, 48 × 48 bytes).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.1 — Section A Directional light @ 0x0000: CONFIRMED</remarks>
    public required LightingKeyframe[] DirectionalKeyframes { get; init; }

    /// <summary>
    /// 48 ambient-light keyframe slots.
    /// Section B: bytes 0x0930–0x122F (2304 bytes, 48 × 48 bytes).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.1 — Section B Ambient light @ 0x0930: CONFIRMED</remarks>
    public required LightingKeyframe[] AmbientKeyframes { get; init; }

    /// <summary>
    /// 48 fog-distance scalars (f32 each, world units).
    /// Section C: bytes 0x1260–0x131F (192 bytes, 48 × 4 bytes).
    /// Sampled range for area 1: approximately 8–43 world units.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.3 — Section C fog-distance scalar @ 0x1260: CONFIRMED</remarks>
    public required float[] FogDistanceScalars { get; init; }

    /// <summary>
    /// 48 secondary fog scalars (f32 each).
    /// Section D: bytes 0x1320–0x13DF (192 bytes, 48 × 4 bytes).
    /// Values near 0.0 in all sampled areas (haze intensity).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.1 — Section D Secondary fog scalar @ 0x1320: SAMPLE-VERIFIED</remarks>
    public required float[] SecondaryFogScalars { get; init; }

    /// <summary>
    /// Reserved f32 array.
    /// Section E: bytes 0x13E0–0x14A7 (200 bytes).
    /// All zeros in all sampled data.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.1 — Section E Reserved f32 array @ 0x13E0: SAMPLE-VERIFIED (all zeros)</remarks>
    public required ReadOnlyMemory<byte> RawSectionE { get; init; }

    /// <summary>
    /// Fallback directional-light vector.
    /// Bytes 0x14B0–0x14BF (16 bytes = 4 × f32).
    /// Observed values: scale=1.0, dir_X=−7.0, dir_Y=7.0, dir_Z=20.0.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.4 — Fallback light @ 0x14B0: CONFIRMED</remarks>
    public required float FallbackScale { get; init; }

    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.4 — dir_X f32 @ 0x14B4: CONFIRMED (value −7.0)</remarks>
    public required float FallbackDirX { get; init; }

    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.4 — dir_Y f32 @ 0x14B8: CONFIRMED (value 7.0)</remarks>
    public required float FallbackDirY { get; init; }

    /// <remarks>spec: Docs/RE/formats/environment_bins.md §9.4 — dir_Z f32 @ 0x14BC: CONFIRMED (value 20.0)</remarks>
    public required float FallbackDirZ { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  stardome%d.bin  — Star colour grid  (Tier 2)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of <c>stardome%d.bin</c>.
/// Fixed size: 9216 bytes (12 × 192 × 4). No magic, no version.
/// Gated by <c>map_option%d.bin</c> stardome_enable flag.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §4.1 File layout: CONFIRMED
/// 12 keyframes × 192 star instances × 4 bytes BGRA each.
/// </remarks>
public sealed class StarDomeBin
{
    /// <summary>Fixed file size: 9216 bytes.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §4 — "exactly 9216 bytes": CONFIRMED</remarks>
    public const int FixedSize = 9216;

    /// <summary>Number of keyframes (12-frame, 7200 ms per step).</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §4.1 — 12 keyframes: CONFIRMED</remarks>
    public const int KeyframeCount = 12;

    /// <summary>Number of star instances per keyframe.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §4.1 — 192 star instances: CONFIRMED</remarks>
    public const int StarsPerKeyframe = 192;

    /// <summary>
    /// Star colours: <c>star_colors[keyframe][star_index]</c>.
    /// Dimensions: [12][192]. BGRA byte order (Blue, Green, Red, Alpha).
    /// Alpha is always 0.
    /// In all sampled data all 192 instances per keyframe share the same BGRA value.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/environment_bins.md §4.1 — star_colors u8[12][192][4] @ 0x0000: CONFIRMED
    /// spec: Docs/RE/formats/environment_bins.md §4.2 — BGRA byte order: CONFIRMED
    /// </remarks>
    public required BgraColor[][] StarColors { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  clouddome%d.bin  — Cloud dome colour grid  (Tier 2)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Decoded result of <c>clouddome%d.bin</c>.
/// Fixed size: 23040 bytes (2 × 11520). No magic, no version.
/// Gated by <c>map_option%d.bin</c> clouddome_enable flag.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §5.1 File layout: CONFIRMED
/// Two equal-sized sections (inner and outer cloud layer), each 12 × 240 × 4 bytes.
/// </remarks>
public sealed class CloudDomeBin
{
    /// <summary>Fixed file size: 23040 bytes.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §5 — "exactly 23040 bytes": CONFIRMED</remarks>
    public const int FixedSize = 23040;

    /// <summary>Number of keyframes (12-frame, 7200 ms per step).</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §5.3 — 12 keyframes: CONFIRMED</remarks>
    public const int KeyframeCount = 12;

    /// <summary>Number of per-vertex dome tints per keyframe.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §5.4 — 240 vertices per keyframe: CONFIRMED</remarks>
    public const int VerticesPerKeyframe = 240;

    /// <summary>
    /// Inner cloud layer per-vertex tints: <c>cloud_layer1_colors[keyframe][vertex]</c>.
    /// Dimensions: [12][240]. BGRA byte order. Alpha always 0.
    /// Section 1: bytes 0x0000–0x2CFF (11520 bytes).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §5.1 — cloud_layer1_colors u8[12][240][4] @ 0x0000: CONFIRMED</remarks>
    public required BgraColor[][] Layer1Colors { get; init; }

    /// <summary>
    /// Outer/haze cloud layer per-vertex tints: <c>cloud_layer2_colors[keyframe][vertex]</c>.
    /// Dimensions: [12][240]. BGRA byte order. Alpha always 0.
    /// Section 2: bytes 0x2D00–0x59FF (11520 bytes).
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §5.1 — cloud_layer2_colors u8[12][240][4] @ 0x2D00: CONFIRMED</remarks>
    public required BgraColor[][] Layer2Colors { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  cloud_cycle%d.bin  — Cloud animation schedule  (Tier 2)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One row (day pattern) from <c>cloud_cycle%d.bin</c>.
/// 7 bytes per row.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §6.1 File layout: CONFIRMED
/// </remarks>
public readonly record struct CloudCycleRow(
    /// <summary>Cloud drift speed integer, range 1–10.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — speed u8 @ col[0]: CONFIRMED</remarks>
    byte Speed,
    /// <summary>Cloud texture ID for layer 1 during simulated hours 0–12.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — cloud1_id_0to12h u8 @ col[1]: CONFIRMED</remarks>
    byte Cloud1Id0To12H,
    /// <summary>Cloud texture ID for layer 1 during hours 12–24.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — cloud1_id_12to24h u8 @ col[2]: CONFIRMED</remarks>
    byte Cloud1Id12To24H,
    /// <summary>Cloud texture ID for layer 2 during hours 0–6.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — cloud2_id_0to6h u8 @ col[3]: CONFIRMED</remarks>
    byte Cloud2Id0To6H,
    /// <summary>Cloud texture ID for layer 2 during hours 6–12.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — cloud2_id_6to12h u8 @ col[4]: CONFIRMED</remarks>
    byte Cloud2Id6To12H,
    /// <summary>Cloud texture ID for layer 2 during hours 12–18.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — cloud2_id_12to18h u8 @ col[5]: CONFIRMED</remarks>
    byte Cloud2Id12To18H,
    /// <summary>Cloud texture ID for layer 2 during hours 18–24.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — cloud2_id_18to24h u8 @ col[6]: CONFIRMED</remarks>
    byte Cloud2Id18To24H);

/// <summary>
/// Decoded result of <c>cloud_cycle%d.bin</c>.
/// Fixed size: 70 bytes (10 rows × 7 u8 values). No magic, no version.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/environment_bins.md §6.1 File layout: CONFIRMED
/// Total: 10 × 7 = 70 bytes.
/// </remarks>
public sealed class CloudCycleBin
{
    /// <summary>Fixed file size: 70 bytes.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6 — "exactly 70 bytes": CONFIRMED</remarks>
    public const int FixedSize = 70;

    /// <summary>Number of day-pattern rows.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — 10 rows: CONFIRMED</remarks>
    public const int RowCount = 10;

    /// <summary>Number of bytes per row.</summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — 7 bytes per row: CONFIRMED</remarks>
    public const int BytesPerRow = 7;

    /// <summary>
    /// 10 day-pattern rows. Each row defines cloud texture IDs and drift speed for one day.
    /// </summary>
    /// <remarks>spec: Docs/RE/formats/environment_bins.md §6.1 — rows 0–9: CONFIRMED</remarks>
    public required CloudCycleRow[] Rows { get; init; }
}