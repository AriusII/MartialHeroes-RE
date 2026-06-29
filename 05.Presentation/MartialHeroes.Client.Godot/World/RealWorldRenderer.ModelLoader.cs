using System.Globalization;
using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    private void WireEnvironmentAndWater()
    {
        if (Assets is null) return;

        var worldSceneRoot = GetParent();
        var sceneWorldEnv = FindDirectChildOfType<WorldEnvironment>(worldSceneRoot);
        var sceneDirLight = FindDirectChildOfType<DirectionalLight3D>(worldSceneRoot);
        GD.Print($"[RealWorldRenderer] Scene env nodes under '{worldSceneRoot.Name}': " +
                 $"WorldEnvironment={sceneWorldEnv is not null} DirectionalLight3D={sceneDirLight is not null}.");

        var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
        AddChild(envNode);
        envNode.Configure(Assets, TargetAreaId, sceneWorldEnv, sceneDirLight);

        _environmentNode = envNode;
        GD.Print("[RealWorldRenderer] EnvironmentNode configured — clock awaits server 4/1 Hour/Minute fields. " +
                 "spec: Docs/RE/specs/environment.md §2, packets/4-1_game_state_tick.yaml §fields.Hour/Minute.");

        if (_mapXEffectScheduler is null)
        {
            _mapXEffectScheduler = new MapXEffectScheduler { Name = "MapXEffectScheduler" };
            AddChild(_mapXEffectScheduler);
            GD.Print("[RealWorldRenderer] MapXEffectScheduler created + added to tree. " +
                     "spec: Docs/RE/specs/effect-scheduling.md (ambient scheduler; self-drives in _Process).");
        }

        GD.Print(
            $"[Water] map_option*.bin carries no water data (water.md RESOLVED-NEGATIVE — no water renderer, geometry, or asset loader exists in doida.exe). " +
            $"Water placement is oracle-gated and OFF by default. " +
            $"Populate res://water_oracle.cfg with 'areaId,mapX,mapZ,Y' lines (one per cell) to enable water per maintainer visual review.");

        var waterEntry = ReadWaterOracle(TargetAreaId, TargetMapX, TargetMapZ);

        if (!waterEntry.Enabled)
        {
            GD.Print(
                $"[Water] area={TargetAreaId} cell=({TargetMapX},{TargetMapZ}) not in water_oracle.cfg — no water plane rendered (faithful default: area is dry).");
            return;
        }

        var ringRadius = ReadRingRadiusFromConfig();
        var ringCells = 2 * ringRadius + 1 + 1f;
        var size = ringCells * 1024f;

        var centreX = (TargetMapX - 10000) * 1024f + 512f;
        var centreZ = (TargetMapZ - 10000) * 1024f + 512f;
        var centre = new Vector3(centreX, waterEntry.WorldY, -centreZ);

        var waterNode = new WaterRenderer { Name = "WaterRenderer" };
        AddChild(waterNode);
        waterNode.Configure(centre, size, waterEntry.WorldY);

        GD.Print($"[Water] oracle entry found → plane configured at Y={waterEntry.WorldY:F1} " +
                 $"area={TargetAreaId} cell=({TargetMapX},{TargetMapZ}) size={size:F0}u centre=({centre.X:F0},{centre.Y:F1},{centre.Z:F0}).");
    }

    private static WaterPlacement ReadWaterOracle(int areaId, int mapX, int mapZ)
    {
        try
        {
            var absPath = ProjectSettings.GlobalizePath("res://water_oracle.cfg");
            if (!File.Exists(absPath)) return new WaterPlacement(false, 0f);

            foreach (var rawLine in File.ReadLines(absPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var parts = line.Split(',');
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[0].Trim(), out var oa)) continue;
                if (!int.TryParse(parts[1].Trim(), out var ox)) continue;
                if (!int.TryParse(parts[2].Trim(), out var oz)) continue;
                if (!float.TryParse(parts[3].Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var oy)) continue;
                if (oa == areaId && ox == mapX && oz == mapZ)
                    return new WaterPlacement(true, oy);
            }
        }
        catch
        {
        }

        return new WaterPlacement(false, 0f);
    }

    private static T? FindDirectChildOfType<T>(Node parent) where T : Node
    {
        foreach (var child in parent.GetChildren())
            if (child is T match)
                return match;
        return null;
    }

    private void LoadAndSpawnBudScene()
    {
        if (Assets is null) return;

        (string? _, string? budPath) = (null, null);
        try
        {
            (_, budPath) = Assets.LoadMapDatafilePaths(TargetAreaId, TargetMapX, TargetMapZ);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] LoadMapDatafilePaths failed: {ex.Message}");
            return;
        }

        if (budPath is null)
        {
            GD.Print("[RealWorldRenderer] No BUILDING section in .map — skipping BUD scene.");
            return;
        }

        GD.Print($"[RealWorldRenderer] Loading BUD scene: {budPath}");

        BudScene? scene = null;
        try
        {
            scene = Assets.LoadBud(budPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] LoadBud failed: {ex.Message}");
            return;
        }

        if (scene is null || scene.Objects.Length == 0)
        {
            GD.Print($"[RealWorldRenderer] BUD scene empty for: {budPath}");
            return;
        }

        GD.Print($"[RealWorldRenderer] BUD scene parsed: {scene.Objects.Length} objects — building ArrayMesh.");

        Node3D budRoot;
        try
        {
            var budTexCache = new Dictionary<uint, ImageTexture?>();

            Func<uint, ImageTexture?> budTexResolver = texId =>
            {
                if (budTexCache.TryGetValue(texId, out var cached)) return cached;
                var tex = ResolveSectionTexture("BUILDING", (int)texId);
                budTexCache[texId] = tex;
                return tex;
            };

            budRoot = BudMeshBuilder.Build(scene, budTexResolver);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] BudMeshBuilder.Build failed: {ex.Message}");
            return;
        }

        budRoot.Name = "BudSceneNode";
        AddChild(budRoot);

        GD.Print(
            $"[RealWorldRenderer] BUD scene spawned: {scene.Objects.Length} objects (ArrayMesh, no GltfDocument).");
    }

    private void SpawnCamera()
    {
        var centreX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        var centreZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;
        var godotZ = -centreZ;

        var cellCentre = new Vector3(centreX, 0f, godotZ);

        var existing = GetViewport()?.GetCamera3D();
        if (existing is not null && existing is not CameraController)
        {
            existing.GetParent()?.RemoveChild(existing);
            existing.QueueFree();
        }

        var cam = new CameraController { Name = "CameraController" };
        AddChild(cam);

        if (_terrainNode is not null)
        {
            var terrainCapture = _terrainNode;
            cam.GroundHeightFunc = (lx, lz) => terrainCapture.GetGroundHeight(lx, lz);
        }

        cam.Configure(cellCentre, 1024f);
        cam.MakeCurrent();

        _cameraController = cam;

        GD.Print($"[RealWorldRenderer] CameraController spawned (spec-faithful Third-person orbit). " +
                 $"Cell centre ({centreX:F0}, 0, {godotZ:F0}). " +
                 "RMB=orbit, wheel=elevation, ESC=reset-to-Third, Tab=devFreeFly.");
    }
}