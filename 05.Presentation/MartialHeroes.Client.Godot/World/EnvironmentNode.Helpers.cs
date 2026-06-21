// World/EnvironmentNode.Helpers.cs
//
// Partial class — colour conversion helpers, scene node resolution, and diagnostics.
// Extracted from EnvironmentNode.Configurator.cs to keep each file ≤ ~400 lines.
// See EnvironmentNode.cs for the full file description and all spec cites.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/specs/environment.md — runtime environment model.
// spec: Docs/RE/formats/environment_bins.md — file byte layouts.

using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EnvironmentNode
{
    // -------------------------------------------------------------------------
    // Colour helpers
    // -------------------------------------------------------------------------

    /// <summary>color_A (RGBA f32) of a lighting keyframe → clamped Godot Color (alpha forced 1).</summary>
    private static Color ColorAOf(LightingKeyframe kf)
    {
        var c = kf.ColorA;
        // spec: environment_bins.md §9.2 — color_A RGBA, R at lowest index; alpha always 0 → use 1.
        return ClampColor(new Color(SafeF(c, 0), SafeF(c, 1), SafeF(c, 2)));
    }

    /// <summary>material ambient_sky_color [29..32] (RGBA f32) → clamped Godot Color.</summary>
    private static Color MaterialSkyColor(float[] row)
    {
        // spec: environment_bins.md §3.2 — ambient_sky_color RGBA at indices [29..32].
        // spec: environment.md §6.2 — material colours are float RGBA; clamp >1 to non-HDR.
        return ClampColor(new Color(SafeF(row, 29), SafeF(row, 30), SafeF(row, 31)));
    }

    /// <summary>
    ///     material sky_haze [0..3] (RGBA f32) → clamped Godot Color.
    ///     Used as a legibility fallback when ambient_sky_color [29..32] is near-black.
    ///     spec: environment_bins.md §3.2 — sky_haze RGBA at indices [0..3].
    ///     Aesthetic: this fallback path is a port-side legibility choice (not spec-dictated behaviour).
    /// </summary>
    private static Color SkyHazeColor(float[] row)
    {
        // spec: environment_bins.md §3.2 — sky_haze RGBA at indices [0..3].
        return ClampColor(new Color(SafeF(row, 0), SafeF(row, 1), SafeF(row, 2)));
    }

    /// <summary>Fog colour for fractional position between kf and kfNext (BGRA → RGB).</summary>
    private static Color LerpFogColor(FogBin fog, int kf, int kfNext, float frac)
    {
        var a = BgraToColor(fog.FogColors[Math.Clamp(kf, 0, fog.FogColors.Length - 1)]);
        var b = BgraToColor(fog.FogColors[Math.Clamp(kfNext, 0, fog.FogColors.Length - 1)]);
        return a.Lerp(b, frac);
    }

    /// <summary>BGRA u8 → Godot Color. spec: environment.md §6.2 — r=bgra[2], g=bgra[1], b=bgra[0].</summary>
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

    // -------------------------------------------------------------------------
    // Scene node resolution
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Resolves exactly one <see cref="WorldEnvironment" /> and one <see cref="DirectionalLight3D" />
    ///     to drive, NEVER creating a duplicate when the scene already provides them.
    ///     Resolution order (most reliable first):
    ///     1. Explicit references passed by the owner (RealWorldRenderer owns the World scene root).
    ///     2. A RECURSIVE scene search (descends past intermediate nodes — fixes the boot_flow=login
    ///     parent-walk miss where the scene env/light live two levels below the resolved root).
    ///     3. Create our own child only if neither yields one.
    ///     Logs the found/created counts so headless runs can prove exactly one of each is active.
    /// </summary>
    private void ResolveOrCreateSceneNodes(WorldEnvironment? sceneWorldEnv, DirectionalLight3D? sceneDirLight)
    {
        var sceneRoot = GetSceneRoot();

        // 1) explicit (owner-provided) → 2) recursive search → 3) create.
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

        // One-line proof of exactly-one resolution (counts the live nodes in the scene subtree so a
        // duplicate would show up immediately as >1).
        var envCount = CountDescendantsOfType<WorldEnvironment>(sceneRoot);
        var lightCount = CountDescendantsOfType<DirectionalLight3D>(sceneRoot);
        GD.Print($"[Environment] node resolution: WorldEnvironment {(envFromScene ? "reused scene" : "created own")}" +
                 $" (live in subtree={envCount}); DirectionalLight3D {(lightFromScene ? "reused scene" : "created own")}" +
                 $" (live in subtree={lightCount}). created(env={envCreated},light={lightCreated}).");

        // Create the single Environment resource we own and mutate in place. Replaces whatever the
        // scene shipped (e.g. the ProceduralSky default) with our data-driven environment, and gives
        // the per-frame cycle a stable instance to mutate (no per-tick allocation — Fix 2).
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

    /// <summary>Recursive depth-first search for the first descendant of type T (root included).</summary>
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

    /// <summary>Counts every live descendant of type T in the subtree (root included). Diagnostic only.</summary>
    private static int CountDescendantsOfType<T>(Node root) where T : Node
    {
        var count = root is T ? 1 : 0;
        foreach (var child in root.GetChildren())
            count += CountDescendantsOfType<T>(child);
        return count;
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

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
            // Note: AmbientKeyframes (§B) are inert in original (K_ambient=0.0 — spec §6.2a).
            // The actual device ambient = OPTION_BRIGHT/100 floor = OptionBrightFloor.
            // spec: Docs/RE/specs/environment.md §6.2a
            // Sun applied RAW at energy=1.0 (no floor, no hue-normalise, no lum×4).
            // spec: Docs/RE/specs/environment.md §6.2a.
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

        GD.Print($"[Environment] area={_areaId} keyframe={kf}(noon) {skyGate} " +
                 $"material={_env?.Material is not null} cycle={CycleEnabled}@{CycleSpeed:F0}ms/s " +
                 $"tonemap=Linear/1.0 glow=Screen | fog: {fogStr} | light: {lightStr} | sunDirGodot={sunDir}");
    }
}