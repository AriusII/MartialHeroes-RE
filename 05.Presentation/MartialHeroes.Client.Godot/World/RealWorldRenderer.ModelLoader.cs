
using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Godot.Adapters;

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

        if (_mapXEffectScheduler is null)
        {
            _mapXEffectScheduler = new MapXEffectScheduler { Name = "MapXEffectScheduler" };
            AddChild(_mapXEffectScheduler);
            GD.Print("[RealWorldRenderer] MapXEffectScheduler created + added to tree. " +
                     "spec: Docs/RE/specs/effect-scheduling.md (ambient scheduler; self-drives in _Process).");
        }

        var env = VfsEnvironmentSource.Load(Assets, TargetAreaId);
        var water = WaterPlacement.FromMapOption(env.MapOption);

        if (!water.Enabled)
            GD.Print($"[Water] area={TargetAreaId} no map_option-driven water plane — checking per-cell FX textures.");

        if (_cellMap is null || _bgTextures is null)
        {
            GD.Print($"[Water] area={TargetAreaId} cell .map or bgtexture catalog not loaded — no water plane.");
            return;
        }

        var hasWater = WaterRenderer.CellHasWater(_cellMap, _bgTextures);
        GD.Print($"[Water] area={TargetAreaId} per-cell FX detection: hasWater={hasWater}.");

        if (!hasWater)
        {
            GD.Print($"[Water] area={TargetAreaId} cell has no water FX textures — no water plane.");
            return;
        }

        var ringRadius = ReadRingRadiusFromConfig();
        var ringCells = 2 * ringRadius + 1 + 1f;
        var size = ringCells * 1024f;

        var centreX = (TargetMapX - 10000) * 1024f + 512f;
        var centreZ = (TargetMapZ - 10000) * 1024f + 512f;
        var waterY = WaterRenderer.WaterSurfaceY(_cellMap);
        var centre = new Vector3(centreX, waterY, -centreZ);

        var waterNode = new WaterRenderer { Name = "WaterRenderer" };
        AddChild(waterNode);
        waterNode.Configure(centre, size, waterY);

        GD.Print($"[Water] cell has water → plane configured at Y={waterY:F1} " +
                 $"area={TargetAreaId} size={size:F0}u centre=({centre.X:F0},{centre.Y:F1},{centre.Z:F0}).");
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