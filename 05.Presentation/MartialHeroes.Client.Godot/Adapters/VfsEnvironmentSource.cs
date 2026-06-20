// Adapters/VfsEnvironmentSource.cs
//
// Loads and parses the per-area environment binary family from the VFS via RealClientAssets,
// using the Assets.Parsers EnvironmentBinParsers. This is a thin read+parse adapter: it owns NO
// rendering and NO game-rule logic — it only turns VFS bytes into the parsed *Bin model objects
// that EnvironmentNode/WaterRenderer consume.
//
// Pattern: mirrors Adapters/VfsTerrainSectorSource.cs (read bytes from the VFS, parse with an
// Assets.Parsers parser, swallow absence/errors and return null so the offline run never breaks).
//
// Files loaded (all under data/sky/dat/, bare-decimal area id, no zero-padding — VFS-confirmed):
//   map_option{areaId}.bin  (40 B)   — master flags (dungeon/sight + per-subsystem sky enables, indoor)
//   fog{areaId}.bin          (204 B)  — fog start/end ratios + 48 BGRA fog colours
//   light{areaId}.bin        (5312 B) — 48 directional + 48 ambient keyframes + fallback dir
//   material{areaId}.bin     (9792 B) — sun/sky material colour table (optional, sky tint)
//
// spec: Docs/RE/specs/environment.md §3.1 — load sequence (map_option → fog → material → light).
// spec: Docs/RE/formats/environment_bins.md — file family path pattern data/sky/dat/<name><id>.bin.
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive (parse-only adapter).

using Godot;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
///     The decoded environment for one area: the parsed bins the renderer needs.
///     Any field may be <see langword="null" /> when the corresponding file is absent or unreadable;
///     the renderer applies its documented fallbacks (spec: environment.md §7).
///     Tier-2 dome fields (StarDome, CloudDome, CloudCycle) are populated when
///     map_option stardome_enable / clouddome_enable = 1 AND the files are present.
///     spec: Docs/RE/specs/environment.md §3.1 steps 4–5 — gated by per-area flags.
/// </summary>
internal sealed record AreaEnvironment(
    MapOptionBin? MapOption,
    FogBin? Fog,
    LightBin? Light,
    MaterialBin? Material,
    StarDomeBin? StarDome = null,
    CloudDomeBin? CloudDome = null,
    CloudCycleBin? CloudCycle = null);

/// <summary>
///     Reads and parses the per-area environment binary family from the VFS.
///     All reads go through <see cref="RealClientAssets.GetRaw" />; every parse is guarded so an
///     absent/corrupt file yields <see langword="null" /> rather than an exception.
/// </summary>
internal static class VfsEnvironmentSource
{
    /// <summary>
    ///     Loads the area's environment bins per the spec load sequence. Never throws.
    ///     Falls back to area 0 for fog/light/material when the requested area's file is absent
    ///     (map_option is NOT cross-area fallbacked — its flags are area-specific).
    ///     spec: Docs/RE/specs/environment.md §3.1 — load sequence; §7 — fallback values.
    /// </summary>
    public static AreaEnvironment Load(RealClientAssets assets, int areaId)
    {
        // 1. map_option{id}.bin — master flags (no cross-area fallback: per-area gates).
        // spec: Docs/RE/specs/environment.md §3.1 step 1.
        var mapOption = TryParse(assets, $"data/sky/dat/map_option{areaId}.bin",
            EnvironmentBinParsers.ParseMapOption, "map_option");

        // 2. fog{id}.bin — always read. Fallback to area 0.
        // spec: Docs/RE/specs/environment.md §3.1 step 2.
        var fog = TryParse(assets, $"data/sky/dat/fog{areaId}.bin",
                      EnvironmentBinParsers.ParseFog, "fog")
                  ?? TryParse(assets, "data/sky/dat/fog0.bin",
                      EnvironmentBinParsers.ParseFog, "fog(fallback area0)");

        // 6. light{id}.bin — always read. Fallback to area 0; final fallback is the built-in
        //    constants applied in EnvironmentNode when this is null.
        // spec: Docs/RE/specs/environment.md §3.1 step 6.
        var light = TryParse(assets, $"data/sky/dat/light{areaId}.bin",
                        EnvironmentBinParsers.ParseLight, "light")
                    ?? TryParse(assets, "data/sky/dat/light0.bin",
                        EnvironmentBinParsers.ParseLight, "light(fallback area0)");

        // 3. material{id}.bin — sky material tint, loaded for outdoor areas (indoor suppresses sky).
        //    RECONCILED Campaign 5: map_option has no sky_gate master flag; sky subsystems are gated
        //    individually and the whole sky is suppressed indoors. Load the sky-tint table when the
        //    area is outdoor (indoor_flag = 0). spec: Docs/RE/formats/environment_bins.md §1.1 (MAPHIDE).
        // spec: Docs/RE/specs/environment.md §3.1 step 3 — optional sky tint.
        MaterialBin? material = null;
        if (mapOption is null || mapOption.IndoorFlag == 0)
            material = TryParse(assets, $"data/sky/dat/material{areaId}.bin",
                           EnvironmentBinParsers.ParseMaterial, "material")
                       ?? TryParse(assets, "data/sky/dat/material0.bin",
                           EnvironmentBinParsers.ParseMaterial, "material(fallback area0)");

        // 4. stardome{id}.bin — gated by stardome_enable.
        // spec: Docs/RE/specs/environment.md §3.1 step 4 — gated by stardome_enable.
        StarDomeBin? starDome = null;
        if (mapOption is null || mapOption.StarDomeEnable == 1)
            starDome = TryParse(assets, $"data/sky/dat/stardome{areaId}.bin",
                           EnvironmentBinParsers.ParseStarDome, "stardome")
                       ?? TryParse(assets, "data/sky/dat/stardome0.bin",
                           EnvironmentBinParsers.ParseStarDome, "stardome(fallback area0)");

        // 5. clouddome{id}.bin + cloud_cycle{id}.bin — gated by clouddome_enable.
        // spec: Docs/RE/specs/environment.md §3.1 step 5 — gated by clouddome_enable.
        CloudDomeBin? cloudDome = null;
        CloudCycleBin? cloudCycle = null;
        if (mapOption is null || mapOption.CloudDomeEnable == 1)
        {
            cloudDome = TryParse(assets, $"data/sky/dat/clouddome{areaId}.bin",
                            EnvironmentBinParsers.ParseCloudDome, "clouddome")
                        ?? TryParse(assets, "data/sky/dat/clouddome0.bin",
                            EnvironmentBinParsers.ParseCloudDome, "clouddome(fallback area0)");

            cloudCycle = TryParse(assets, $"data/sky/dat/cloud_cycle{areaId}.bin",
                             EnvironmentBinParsers.ParseCloudCycle, "cloud_cycle")
                         ?? TryParse(assets, "data/sky/dat/cloud_cycle0.bin",
                             EnvironmentBinParsers.ParseCloudCycle, "cloud_cycle(fallback area0)");
        }

        return new AreaEnvironment(mapOption, fog, light, material, starDome, cloudDome, cloudCycle);
    }

    /// <summary>
    ///     Reads <paramref name="path" /> from the VFS and parses it with <paramref name="parse" />.
    ///     Returns <see langword="null" /> when the path is absent, empty, or the parser throws
    ///     (e.g. a size mismatch — the parsers validate fixed sizes).
    /// </summary>
    private static T? TryParse<T>(
        RealClientAssets assets,
        string path,
        Func<ReadOnlyMemory<byte>, T> parse,
        string label)
        where T : class
    {
        try
        {
            if (!assets.Contains(path)) return null;
            var raw = assets.GetRaw(path);
            if (raw.IsEmpty) return null;
            return parse(raw);
        }
        catch (Exception ex)
        {
            // Absent/corrupt/size-mismatch → treat as missing, never propagate.
            GD.PrintErr($"[VfsEnvironmentSource] {label} parse failed ({path}): {ex.Message}");
            return null;
        }
    }
}