// World/RealWorldRenderer.ModelLoader.cs
//
// .bud/.skn model loading + mesh instancing, environment wiring, camera placement.
// Part of the RealWorldRenderer partial class split.

using Godot;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Godot.Adapters;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    // -------------------------------------------------------------------------
    // Environment + water wiring
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Assembles the area's environment (sky/fog/light) into a <see cref="EnvironmentNode" />.
    ///     The environment is read+parsed from the per-area bins via
    ///     <see cref="VfsEnvironmentSource" /> (the same VFS-read+parse adapter pattern as terrain).
    ///     Water rendering visuals are a free engineering choice — the legacy client has no water
    ///     renderer (RESOLVED-NEGATIVE). RECONCILED Campaign 5: <c>map_option%d.bin</c> carries NO water
    ///     field, so the map_option water path (<see cref="WaterPlacement.FromMapOption" />) is always
    ///     disabled; per-cell water presence is detected separately from <c>.map</c> FX texture names.
    ///     spec: Docs/RE/specs/environment.md §3 (assembly) + §4 (water RESOLVED-NEGATIVE).
    ///     spec: Docs/RE/formats/environment_bins.md §1.1 (NO water fields in map_option).
    /// </summary>
    private void WireEnvironmentAndWater()
    {
        if (Assets is null) return;

        // ---- Environment (D3) ----
        // Resolve the World scene's OWN WorldEnvironment + DirectionalLight3D (defined in World.tscn)
        // and pass them explicitly into EnvironmentNode so it drives them in place instead of
        // creating duplicates. This renderer is a direct child of the World scene root (GameLoop),
        // so the scene's env/light are our SIBLINGS — found via our parent. Under boot_flow=login the
        // scene tree is /root → Boot → World → RealWorldRenderer, and a generic top-of-tree walk lands
        // on Boot (whose direct children exclude these) → that was the duplicate-sun bug.
        var worldSceneRoot = GetParent();
        var sceneWorldEnv = FindDirectChildOfType<WorldEnvironment>(worldSceneRoot);
        var sceneDirLight = FindDirectChildOfType<DirectionalLight3D>(worldSceneRoot);
        GD.Print($"[RealWorldRenderer] Scene env nodes under '{worldSceneRoot.Name}': " +
                 $"WorldEnvironment={sceneWorldEnv is not null} DirectionalLight3D={sceneDirLight is not null}.");

        var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
        AddChild(envNode);
        envNode.Configure(Assets, TargetAreaId, sceneWorldEnv, sceneDirLight);

        // ---- Water (D4) ----
        // RECONCILED Campaign 5: map_option has NO water field, so this path is always disabled.
        // Per-cell water presence is detected from the .map FX texture names (CellHasWater), not here.
        // spec: Docs/RE/formats/environment_bins.md §1.1 (no water fields) + §1.4 (RESOLVED-NEGATIVE).
        var env = VfsEnvironmentSource.Load(Assets, TargetAreaId);
        var water = WaterPlacement.FromMapOption(env.MapOption);

        if (!water.Enabled)
            // spec: environment_bins.md §1.1 — map_option carries no water plane (always the case).
            GD.Print($"[Water] area={TargetAreaId} no map_option-driven water plane — checking per-cell FX textures.");

        // ---- Per-cell water detection (D4 / Campaign 5) ----
        // map_option has no water field, so water presence is detected from the .map FX texture names.
        // CellHasWater enumerates FX1–FX7 sections; resolves each TexId via bgtexture.txt; checks for
        // "_water", "_sea", "_wateredge" substrings in the relative path.
        // spec: Docs/RE/formats/terrain.md §3.5 — .map FX sections carry water overlays. CONFIRMED.
        // spec: WaterRenderer.CellHasWater — detection rule; VFS-confirmed texture rel-paths. 2026-06-12.
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

        // Centre the plane on the resolved cell, sized to the loaded streaming ring so it covers
        // the visible terrain. Ring radius cells × 1024 wu, +1 cell of slop for the borders.
        // spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 wu. CONFIRMED.
        var ringRadius = ReadRingRadiusFromConfig();
        var ringCells = 2 * ringRadius + 1 + 1f; // (2r+1) ring + 1 cell slop
        var size = ringCells * 1024f;

        var centreX = (TargetMapX - 10000) * 1024f + 512f;
        var centreZ = (TargetMapZ - 10000) * 1024f + 512f;
        var waterY = WaterRenderer.WaterSurfaceY(_cellMap);
        // spec: WorldCoordinates.ToGodot — negate Z; Y is the data-driven water_y (unchanged).
        var centre = new Vector3(centreX, waterY, -centreZ);

        var waterNode = new WaterRenderer { Name = "WaterRenderer" };
        AddChild(waterNode);
        waterNode.Configure(centre, size, waterY);

        GD.Print($"[Water] cell has water → plane configured at Y={waterY:F1} " +
                 $"area={TargetAreaId} size={size:F0}u centre=({centre.X:F0},{centre.Y:F1},{centre.Z:F0}).");
    }

    /// <summary>First direct child of <paramref name="parent" /> assignable to T, or null.</summary>
    private static T? FindDirectChildOfType<T>(Node parent) where T : Node
    {
        foreach (var child in parent.GetChildren())
            if (child is T match)
                return match;
        return null;
    }

    // -------------------------------------------------------------------------
    // BUD scene loading — ArrayMesh path (no GltfDocument)
    // -------------------------------------------------------------------------

    private void LoadAndSpawnBudScene()
    {
        if (Assets is null) return;

        // Get the BUILDING DATAFILE path from the .map descriptor.
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

        // Build ArrayMesh directly via BudMeshBuilder (NO GltfDocument).
        // BUD coordinates are absolute world-space (no cell-relative offset needed).
        // spec: Docs/RE/formats/terrain_scene.md §Coordinate system — "positions are pre-baked
        //       into absolute world-space": CONFIRMED.
        Node3D budRoot;
        try
        {
            // Wire a texture resolver for BUD objects: maps a 1-based tex_id to a real ImageTexture
            // via the .map BUILDING section's TEXTURES list and the bgtexture pool (same two-hop
            // chain as the terrain). spec: Docs/RE/formats/terrain.md §3.5 + §4.2. CONFIRMED.
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

    // -------------------------------------------------------------------------
    // Camera placement
    // -------------------------------------------------------------------------

    private void SpawnCamera()
    {
        // Compute the Godot-space centre of the resolved terrain cell.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        var centreX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        var centreZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;
        var godotZ = -centreZ; // negate Z: spec WorldCoordinates.ToGodot.
        // spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z. CONFIRMED.

        var cellCentre = new Vector3(centreX, 0f, godotZ);

        // Replace any existing static Camera3D with the spec-faithful CameraController.
        var existing = GetViewport()?.GetCamera3D();
        if (existing is not null && existing is not CameraController)
        {
            existing.GetParent()?.RemoveChild(existing);
            existing.QueueFree();
        }

        var cam = new CameraController { Name = "CameraController" };
        AddChild(cam);

        // Wire terrain ground-height delegate so the camera can do vertical collision.
        // The delegate accepts LEGACY world coordinates (legacyX, legacyZ).
        // spec: Docs/RE/specs/camera_movement.md §A.6 — terrain height clamp (Third only). CODE-CONFIRMED.
        if (_terrainNode is not null)
        {
            var terrainCapture = _terrainNode;
            cam.GroundHeightFunc = (lx, lz) => terrainCapture.GetGroundHeight(lx, lz);
        }

        cam.Configure(cellCentre, 1024f); // spec: terrain.md §1.4 — cell size 1024. CONFIRMED.
        cam.MakeCurrent();

        _cameraController = cam;

        GD.Print($"[RealWorldRenderer] CameraController spawned (spec-faithful Third-person orbit). " +
                 $"Cell centre ({centreX:F0}, 0, {godotZ:F0}). " +
                 "RMB=orbit, wheel=elevation, ESC=reset-to-Third, Tab=devFreeFly.");
    }
}