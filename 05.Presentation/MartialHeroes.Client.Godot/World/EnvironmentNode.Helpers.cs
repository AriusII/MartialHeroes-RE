using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EnvironmentNode
{
    private static Color ColorAOf(LightingKeyframe kf)
    {
        var c = kf.ColorA;
        return ClampColor(new Color(SafeF(c, 0), SafeF(c, 1), SafeF(c, 2)));
    }

    private static Color MaterialSkyColor(float[] row)
    {
        return ClampColor(new Color(SafeF(row, 29), SafeF(row, 30), SafeF(row, 31)));
    }

    private static Color SkyHazeColor(float[] row)
    {
        return ClampColor(new Color(SafeF(row, 0), SafeF(row, 1), SafeF(row, 2)));
    }

    private static Color LerpFogColor(FogBin fog, int kf, int kfNext, float frac)
    {
        var a = BgraToColor(fog.FogColors[Math.Clamp(kf, 0, fog.FogColors.Length - 1)]);
        var b = BgraToColor(fog.FogColors[Math.Clamp(kfNext, 0, fog.FogColors.Length - 1)]);
        return a.Lerp(b, frac);
    }

    private static Color BgraToColor(BgraColor c)
    {
        return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    private static Color ClampColor(Color c)
    {
        return new Color(Math.Clamp(c.R, 0f, 1f), Math.Clamp(c.G, 0f, 1f), Math.Clamp(c.B, 0f, 1f));
    }

    private static float SafeF(float[] arr, int i)
    {
        return (uint)i < (uint)arr.Length ? arr[i] : 0f;
    }


    private void ResolveOrCreateSceneNodes(WorldEnvironment? sceneWorldEnv, DirectionalLight3D? sceneDirLight)
    {
        var sceneRoot = GetSceneRoot();

        var envFromScene = false;
        var envCreated = false;
        _worldEnv = sceneWorldEnv ?? FindDescendantOfType<WorldEnvironment>(sceneRoot);
        if (_worldEnv is not null)
        {
            envFromScene = true;
        }
        else
        {
            _worldEnv = new WorldEnvironment { Name = "EnvironmentNode_WorldEnv" };
            AddChild(_worldEnv);
            envCreated = true;
        }

        var lightFromScene = false;
        var lightCreated = false;
        _dirLight = sceneDirLight ?? FindDescendantOfType<DirectionalLight3D>(sceneRoot);
        if (_dirLight is not null)
        {
            lightFromScene = true;
        }
        else
        {
            _dirLight = new DirectionalLight3D { Name = "EnvironmentNode_SunLight", ShadowEnabled = true };
            AddChild(_dirLight);
            lightCreated = true;
        }

        var envCount = CountDescendantsOfType<WorldEnvironment>(sceneRoot);
        var lightCount = CountDescendantsOfType<DirectionalLight3D>(sceneRoot);
        GD.Print($"[Environment] node resolution: WorldEnvironment {(envFromScene ? "reused scene" : "created own")}" +
                 $" (live in subtree={envCount}); DirectionalLight3D {(lightFromScene ? "reused scene" : "created own")}" +
                 $" (live in subtree={lightCount}). created(env={envCreated},light={lightCreated}).");

        _environment = new Environment();
        _worldEnv.Environment = _environment;
    }

    private Node GetSceneRoot()
    {
        Node current = this;
        while (current.GetParent() is { } parent)
        {
            current = parent;
            if (current.GetParent() == GetTree().Root) break;
        }

        return current;
    }

    private static T? FindDescendantOfType<T>(Node root) where T : Node
    {
        if (root is T self) return self;
        foreach (var child in root.GetChildren())
        {
            var found = FindDescendantOfType<T>(child);
            if (found is not null) return found;
        }

        return null;
    }

    private static int CountDescendantsOfType<T>(Node root) where T : Node
    {
        var count = root is T ? 1 : 0;
        foreach (var child in root.GetChildren())
            count += CountDescendantsOfType<T>(child);
        return count;
    }


    private void PrintSummary(int kf)
    {
        var mo = _env?.MapOption;
        var fog = _env?.Fog;
        var light = _env?.Light;

        var fogStr = fog is not null
            ? $"start={fog.StartDist:F3}(×{ViewRange:F0}={fog.StartDist * ViewRange:F0}u) " +
              $"end={fog.EndDist:F3}(={fog.EndDist * ViewRange:F0}u) " +
              $"noonColor={BgraToColor(fog.FogColors[kf])}"
            : "none(disabled)";

        string lightStr;
        if (light is not null && light.DirectionalKeyframes.Length == KeyframeCount)
        {
            var sun = ColorAOf(light.DirectionalKeyframes[kf]);
            lightStr = $"sunColorA={sun} (energy=1.0 RAW) " +
                       $"ambFloor(OPTION_BRIGHT/100)={OptionBrightFloor:F2} [§B keyframes inert, K_ambient=0] " +
                       $"fallbackDir=({light.FallbackDirX:F0},{light.FallbackDirY:F0},{light.FallbackDirZ:F0})";
        }
        else
        {
            lightStr = $"none(fallback: dir(-7,7,20), ambFloor={OptionBrightFloor:F2})";
        }

        var skyGate = mo is not null
            ? $"indoor={mo.IndoorFlag} sun={mo.SunEnable} moon={mo.MoonEnable} " +
              $"lensflare={mo.LensFlareEnable} stardome={mo.StarDomeEnable} clouddome={mo.CloudDomeEnable}"
            : "no map_option";
        var sunDir = _hasSunDir ? $"{_sunDirGodot.Normalized()}" : "default";

        GD.Print($"[Environment] area={_areaId} keyframe={kf} {skyGate} " +
                 $"material={_env?.Material is not null} cycle={CycleEnabled}@{CycleSpeed:F0}ms/s " +
                 $"tonemap=Linear/1.0 glow=Screen | fog: {fogStr} | light: {lightStr} | sunDirGodot={sunDir}");
    }
}