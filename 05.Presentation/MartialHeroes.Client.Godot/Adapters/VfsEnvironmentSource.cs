using Godot;
using MartialHeroes.Assets.Parsers.Texture;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.Adapters;

internal sealed record AreaEnvironment(
    MapOptionBin? MapOption,
    FogBin? Fog,
    LightBin? Light,
    MaterialBin? Material,
    StarDomeBin? StarDome = null,
    CloudDomeBin? CloudDome = null,
    CloudCycleBin? CloudCycle = null,
    PointLightBin? PointLights = null,
    WeatherBin? Weather = null);

internal static class VfsEnvironmentSource
{
    public static AreaEnvironment Load(RealClientAssets assets, int areaId)
    {
        var mapOption = TryParse(assets, $"data/sky/dat/map_option{areaId}.bin",
            EnvironmentBinParsers.ParseMapOption, "map_option");

        var fog = TryParse(assets, $"data/sky/dat/fog{areaId}.bin",
                      EnvironmentBinParsers.ParseFog, "fog")
                  ?? TryParse(assets, "data/sky/dat/fog0.bin",
                      EnvironmentBinParsers.ParseFog, "fog(fallback area0)");

        var light = TryParse(assets, $"data/sky/dat/light{areaId}.bin",
                        EnvironmentBinParsers.ParseLight, "light")
                    ?? TryParse(assets, "data/sky/dat/light0.bin",
                        EnvironmentBinParsers.ParseLight, "light(fallback area0)");

        MaterialBin? material = null;
        if (mapOption is null || mapOption.IndoorFlag == 0)
            material = TryParse(assets, $"data/sky/dat/material{areaId}.bin",
                           EnvironmentBinParsers.ParseMaterial, "material")
                       ?? TryParse(assets, "data/sky/dat/material0.bin",
                           EnvironmentBinParsers.ParseMaterial, "material(fallback area0)");

        StarDomeBin? starDome = null;
        if (mapOption is null || mapOption.StarDomeEnable == 1)
            starDome = TryParse(assets, $"data/sky/dat/stardome{areaId}.bin",
                           EnvironmentBinParsers.ParseStarDome, "stardome")
                       ?? TryParse(assets, "data/sky/dat/stardome0.bin",
                           EnvironmentBinParsers.ParseStarDome, "stardome(fallback area0)");

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

        var pointLights = TryParse(assets, $"data/sky/dat/point_light{areaId}.bin",
                              EnvironmentBinParsers.ParsePointLight, "point_light")
                          ?? TryParse(assets, "data/sky/dat/point_light0.bin",
                              EnvironmentBinParsers.ParsePointLight, "point_light(fallback area0)");

        var weather = TryParse(assets, $"data/sky/dat/weather{areaId}.bin",
                          EnvironmentBinParsers.ParseWeather, "weather")
                      ?? TryParse(assets, "data/sky/dat/weather0.bin",
                          EnvironmentBinParsers.ParseWeather, "weather(fallback area0)");

        return new AreaEnvironment(mapOption, fog, light, material, starDome, cloudDome, cloudCycle,
            pointLights, weather);
    }

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
            GD.PrintErr($"[VfsEnvironmentSource] {label} parse failed ({path}): {ex.Message}");
            return null;
        }
    }
}