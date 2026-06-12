// World/RealWorldRenderer.cs
//
// PASSIVE rendering node that replaces SyntheticWorldFeeder when real client assets are available.
// Activated when ClientPathResolver.RealAssetsEnabled returns true (client dir résolu + real_assets != false).
//
// What this node does (all passive, no game logic):
//   1. Uses SectorStreamingService to load a 3×3 ring of real terrain sectors.
//   2. Loads building geometry (.bud → ArrayMesh via BudMeshBuilder — NO GltfDocument).
//   3. Loads a skinned character mesh (.skn → static-pose ArrayMesh via SknMeshBuilder — NO GltfDocument).
//   4. Applies diffuse textures (PNG/BMP/DDS via AssetPassthrough → ImageTexture).
//   5. Positions a camera over the terrain.
//
// GltfDocument.AppendFromBuffer is NOT used anywhere in this file. The native Godot GLB importer
// was removed because it caused a native crash on our generated GLBs (no managed stack trace).
// BudMeshBuilder and SknMeshBuilder build Godot ArrayMesh directly from parsed model data.
//
// Threading: all Godot node creation happens on the main thread (_Ready or CallDeferred).
// Heavy parsing runs synchronously in Initialise to keep it simple.
// The 3×3 sector streaming call goes through SectorStreamingService.UpdateCenterAsync which
// is called from a background task (fire-and-forget) — TerrainNode reacts via the event bus.
//
// load_models flag:
//   Set load_models=false in client_dir.cfg to skip .bud and .skn loading (terrain only).
//   Default: true. Read via ClientPathResolver.LoadModelsEnabled.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/terrain.md §1.1–1.4 (path, manifest, ted, world bounds).
// spec: Docs/RE/formats/terrain.md §9.2 (3×3 streaming ring at StreamQuality.Medium).
// spec: Docs/RE/formats/terrain_scene.md (bud scene).
// spec: Docs/RE/formats/mesh.md (skn/bnd).
// spec: Docs/RE/formats/texture.md (png/bmp/dds/tga).

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive rendering node for real client assets.
/// Spawned and started by <see cref="GameLoop._Ready"/> when <see cref="IsEnabled"/> is true.
///
/// Default cell rendered: area 000, cell (10000, 10000) — the world-origin cell.
/// Override via <see cref="TargetAreaId"/>, <see cref="TargetMapX"/>, <see cref="TargetMapZ"/>.
///
/// Character rendered: the first .skn found under data/item/skin/ (best-effort).
/// Override <see cref="SknVirtualPath"/> / <see cref="BndVirtualPath"/> before
/// <see cref="Initialise"/> is called.
///
/// NOTE: GltfDocument.AppendFromBuffer is NOT called by this class. All geometry is built
/// as Godot ArrayMesh directly via BudMeshBuilder / SknMeshBuilder.
/// </summary>
public sealed partial class RealWorldRenderer : Node3D
{
    // -------------------------------------------------------------------------
    // Configuration (set before Initialise)
    // -------------------------------------------------------------------------

    /// <summary>Area to load. Default 0 (map000). spec: Docs/RE/formats/terrain.md §1.1.</summary>
    public int TargetAreaId { get; set; } = 0;

    /// <summary>
    /// Cell X coordinate (world-origin cell = 10000).
    /// spec: Docs/RE/formats/terrain.md §Overview (bias 10000). CONFIRMED.
    /// </summary>
    public int TargetMapX { get; set; } = 10000;

    /// <summary>Cell Z coordinate. Default 10000. spec: Docs/RE/formats/terrain.md §Overview. CONFIRMED.</summary>
    public int TargetMapZ { get; set; } = 10000;

    /// <summary>
    /// VFS path for the character .skn.  Null = auto-discover first available.
    /// spec: Docs/RE/formats/mesh.md §.skn.
    /// </summary>
    public string? SknVirtualPath { get; set; }

    /// <summary>
    /// VFS path for the character .bnd.  Null = derive from .skn filename or null (single-bone).
    /// spec: Docs/RE/formats/mesh.md §.bnd.
    /// </summary>
    public string? BndVirtualPath { get; set; }

    // -------------------------------------------------------------------------
    // Static activation check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when the real-asset rendering path should be activated.
    ///
    /// Delegates to <see cref="ClientPathResolver"/>:
    ///   - Resolves the client directory (env → config → auto-detect).
    ///   - Returns true by default when a valid directory is found.
    ///   - Returns false when real_assets=false (config) or MH_REAL_ASSETS=0 (env) forces
    ///     synthetic mode, or when no valid client directory is found at all.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            string? clientDir = ClientPathResolver.ResolveClientDir();
            return ClientPathResolver.RealAssetsEnabled(clientDir);
        }
    }

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    private RealClientAssets? _assets;
    private TerrainNode? _terrainNode;
    private ClientContext? _ctx;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by GameLoop._Ready. Performs synchronous BUD/character loading and node creation;
    /// kicks off the async 3×3 terrain-ring streaming in a fire-and-forget task.
    ///
    /// Each step is individually guarded: a failure in one step is logged and skipped;
    /// subsequent steps still run. This ensures the window always opens even if asset
    /// loading partially fails on real data.
    ///
    /// IMPORTANT: GltfDocument.AppendFromBuffer is NOT called anywhere in this method or
    /// its callees. All geometry is built as ArrayMesh via BudMeshBuilder / SknMeshBuilder.
    /// </summary>
    public void Initialise(ClientContext ctx, TerrainNode terrainNode)
    {
        GD.Print("[RealWorldRenderer] Initialise: start");

        _ctx = ctx;
        _terrainNode = terrainNode;

        // Open the VFS — falls back gracefully to null if absent.
        GD.Print("[RealWorldRenderer] Initialise: opening VFS");
        try
        {
            _assets = RealClientAssets.TryOpen();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] RealClientAssets.TryOpen threw: {ex.Message}");
            _assets = null;
        }

        if (_assets is null)
        {
            GD.Print("[RealWorldRenderer] No real assets available — skipping real-asset render.");
            return;
        }

        // Wire the texture resolver into TerrainNode so each sector can get a real texture.
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — 1-based TextureIndexGrid → texture path.
        GD.Print("[RealWorldRenderer] Initialise: wiring terrain texture resolver");
        try
        {
            WireTerrainTextureResolver(terrainNode);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] WireTerrainTextureResolver failed: {ex.Message}");
        }

        // Kick off 3×3 terrain streaming via SectorStreamingService.
        // spec: Docs/RE/formats/terrain.md §9.2 (3×3 ring, StreamQuality.Medium).
        GD.Print("[RealWorldRenderer] Initialise: triggering terrain streaming");
        try
        {
            TriggerTerrainStreaming(ctx);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] TriggerTerrainStreaming failed: {ex.Message}");
        }

        // Check load_models flag before loading .bud and .skn.
        // Set load_models=false in client_dir.cfg to render terrain only (safe fallback).
        bool loadModels = ClientPathResolver.LoadModelsEnabled();
        GD.Print($"[RealWorldRenderer] Initialise: load_models={loadModels}");

        if (loadModels)
        {
            // Load BUD scene and create MeshInstance3D children via ArrayMesh (no GltfDocument).
            GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnBudScene start");
            try
            {
                LoadAndSpawnBudScene();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] LoadAndSpawnBudScene failed: {ex.Message}");
            }

            GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnBudScene done");

            // Spawn skinned character static pose (if available) via ArrayMesh (no GltfDocument).
            GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnCharacter start");
            try
            {
                LoadAndSpawnCharacter();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] LoadAndSpawnCharacter failed: {ex.Message}");
            }

            GD.Print("[RealWorldRenderer] Initialise: LoadAndSpawnCharacter done");
        }
        else
        {
            GD.Print("[RealWorldRenderer] Initialise: load_models=false — skipping BUD and character.");
        }

        // Position a camera above the origin cell centre.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        GD.Print("[RealWorldRenderer] Initialise: SpawnCamera start");
        try
        {
            SpawnCamera();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SpawnCamera failed: {ex.Message}");
        }

        GD.Print($"[RealWorldRenderer] Initialise: complete for cell ({TargetMapX},{TargetMapZ}).");
    }

    public override void _ExitTree()
    {
        _assets?.Dispose();
        _assets = null;
    }

    // -------------------------------------------------------------------------
    // Terrain texture resolver wiring
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wires a <see cref="TerrainNode.TextureResolver"/> delegate that maps a 1-based
    /// texture index (from TextureIndexGrid) to a Godot ImageTexture loaded from the VFS.
    ///
    /// spec: Docs/RE/formats/terrain.md §5.6 Block 3 — 1-based TextureIndexGrid.
    /// spec: Docs/RE/formats/terrain.md §3.5 TEXTURES directive — intTexId indexed array.
    /// </summary>
    private void WireTerrainTextureResolver(TerrainNode terrainNode)
    {
        if (_assets is null) return;

        // Cache loaded textures by index to avoid redundant VFS reads.
        var texCache = new Dictionary<int, ImageTexture?>();

        string areaTag = AreaTag(TargetAreaId);
        string texDir = $"data/map{areaTag}/texture/";

        // Heuristic: map 1-based index → texture filename "gr{index:D3}.dds".
        // spec: Docs/RE/formats/terrain.md §4 — "bgtexture.lst at data/map000/texture/bgtexture.lst".
        // TODO: parse bgtexture.lst to build a proper index→filename map.
        terrainNode.TextureResolver = texIdx =>
        {
            if (texCache.TryGetValue(texIdx, out ImageTexture? cached))
                return cached;

            string texPath = $"{texDir}gr{texIdx:D3}.dds";
            ImageTexture? tex = null;

            if (_assets is not null && _assets.Contains(texPath))
            {
                tex = _assets.LoadTexture(texPath);
                if (tex is not null)
                    GD.Print($"[RealWorldRenderer] Terrain texture loaded: {texPath}");
            }

            // Fallback: try gr001.dds regardless of index.
            if (tex is null)
            {
                string fallback = $"{texDir}gr001.dds";
                if (_assets is not null && _assets.Contains(fallback))
                    tex = _assets.LoadTexture(fallback);
            }

            texCache[texIdx] = tex;
            return tex;
        };

        GD.Print($"[RealWorldRenderer] Terrain TextureResolver wired for area {TargetAreaId}.");
    }

    // -------------------------------------------------------------------------
    // 3×3 terrain streaming via SectorStreamingService
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calls <see cref="SectorStreamingService.UpdateCenterAsync"/> for the target cell centre.
    /// spec: Docs/RE/formats/terrain.md §9.2 — "3×3 ring of sectors centred on the player cell".
    /// </summary>
    private void TriggerTerrainStreaming(ClientContext ctx)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ctx.StreamingService.UpdateCenterAsync(TargetMapX, TargetMapZ)
                    .ConfigureAwait(false);
                int residentCount = ctx.StreamingService.ResidentCount;
                GD.Print($"[RealWorldRenderer] 3×3 terrain ring streaming complete " +
                         $"(resident={residentCount} sectors).");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] Terrain streaming error: {ex.Message}");
            }
        });

        GD.Print($"[RealWorldRenderer] Terrain streaming requested for centre ({TargetMapX},{TargetMapZ}).");
    }

    // -------------------------------------------------------------------------
    // BUD scene loading — ArrayMesh path (no GltfDocument)
    // -------------------------------------------------------------------------

    private void LoadAndSpawnBudScene()
    {
        if (_assets is null) return;

        // Get the BUILDING DATAFILE path from the .map descriptor.
        (string? _, string? budPath) = (null, null);
        try
        {
            (_, budPath) = _assets.LoadMapDatafilePaths(TargetAreaId, TargetMapX, TargetMapZ);
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
            scene = _assets.LoadBud(budPath);
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
            // Wire a texture resolver for BUD objects: maps 1-based tex_id → ImageTexture.
            // Uses the same fallback heuristic as the terrain texture resolver.
            string areaTag = AreaTag(TargetAreaId);
            string texDir = $"data/map{areaTag}/texture/";
            var budTexCache = new Dictionary<uint, ImageTexture?>();

            Func<uint, ImageTexture?> budTexResolver = texId =>
            {
                if (budTexCache.TryGetValue(texId, out ImageTexture? cached)) return cached;

                // Heuristic: tex_id 1-based → gr{texId:D3}.dds
                string texPath = $"{texDir}gr{texId:D3}.dds";
                ImageTexture? tex = null;
                if (_assets is not null && _assets.Contains(texPath))
                    tex = _assets.LoadTexture(texPath);

                if (tex is null)
                {
                    string fallback = $"{texDir}gr001.dds";
                    if (_assets is not null && _assets.Contains(fallback))
                        tex = _assets.LoadTexture(fallback);
                }

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

        GD.Print($"[RealWorldRenderer] BUD scene spawned: {scene.Objects.Length} objects (ArrayMesh, no GltfDocument).");
    }

    // -------------------------------------------------------------------------
    // Skinned character loading — static pose ArrayMesh (no GltfDocument)
    // -------------------------------------------------------------------------

    private void LoadAndSpawnCharacter()
    {
        if (_assets is null) return;

        // Resolve .skn path.
        string? sknPath = null;
        try
        {
            sknPath = SknVirtualPath ?? DiscoverFirstSknPath();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] DiscoverFirstSknPath failed: {ex.Message}");
            return;
        }

        if (sknPath is null)
        {
            GD.Print("[RealWorldRenderer] No .skn found — skipping character render.");
            return;
        }

        GD.Print($"[RealWorldRenderer] Loading .skn: {sknPath}");

        // Parse .skn directly (no GLB conversion needed — SknMeshBuilder works on SkinnedMesh).
        // spec: Docs/RE/formats/mesh.md §.skn.
        SkinnedMesh? skinnedMesh = null;
        try
        {
            ReadOnlyMemory<byte> sknData = _assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[RealWorldRenderer] .skn file not found in VFS: {sknPath}");
                return;
            }

            skinnedMesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SknParser.Parse failed for '{sknPath}': {ex.Message}");
            return;
        }

        GD.Print($"[RealWorldRenderer] .skn parsed: '{skinnedMesh.Name}', " +
                 $"{skinnedMesh.FaceCount} faces, {skinnedMesh.Positions.Length} verts — building static ArrayMesh.");

        // Build static-pose ArrayMesh via SknMeshBuilder (NO GltfDocument).
        // TODO: runtime animation via Skeleton/AnimationPlayer (future work).
        MeshInstance3D? charMesh;
        try
        {
            charMesh = SknMeshBuilder.Build(skinnedMesh, albedoTexture: null);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SknMeshBuilder.Build failed: {ex.Message}");
            return;
        }

        if (charMesh is null)
        {
            GD.PrintErr("[RealWorldRenderer] SknMeshBuilder returned null — character skipped.");
            return;
        }

        // Place the character near the centre of the terrain sector.
        // World origin cell (10000,10000) = legacy (0,0,0). Godot Z is negated.
        // spec: Docs/RE/formats/terrain.md §1.4 (world-space bounds). CONFIRMED.
        // spec: WorldCoordinates.ToGodot — negate Z.
        float legacyX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        float legacyZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;

        var charRoot = new Node3D();
        charRoot.Position = new Vector3(legacyX, 0f, -legacyZ); // negate Z: spec WorldCoordinates.ToGodot.
        charRoot.Name = "CharacterNode";
        charRoot.AddChild(charMesh);
        AddChild(charRoot);

        GD.Print($"[RealWorldRenderer] Character spawned from '{sknPath}' (static pose ArrayMesh, no GltfDocument).");
    }

    private string? DiscoverFirstSknPath()
    {
        if (_assets is null) return null;

        // Try common character skin paths.
        // spec: Docs/RE/formats/mesh.md §.skn — "data/char/skin/" or "data/item/skin/": CONFIRMED.
        string[] candidates =
        [
            "data/item/skin/gi213050001.skn",
            "data/item/skin/gi213062382.skn",
            "data/item/skin/gi292020105.skn",
        ];

        foreach (string candidate in candidates)
        {
            if (_assets.Contains(candidate))
                return candidate;
        }

        GD.Print("[RealWorldRenderer] None of the known .skn candidates found in VFS.");
        return null;
    }

    // -------------------------------------------------------------------------
    // Camera placement
    // -------------------------------------------------------------------------

    private void SpawnCamera()
    {
        // Place a camera looking down at the centre of the target cell from above.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        float centreX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        float centreZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;
        float godotZ = -centreZ; // negate Z: spec WorldCoordinates.ToGodot.

        if (GetViewport()?.GetCamera3D() is not null)
        {
            GD.Print("[RealWorldRenderer] Camera already present in viewport — skipping spawn.");
            return;
        }

        var cam = new Camera3D();
        cam.Position = new Vector3(centreX, 800f, godotZ);
        cam.LookAt(new Vector3(centreX, 0f, godotZ), Vector3.Forward);
        cam.Fov = 60f;
        cam.Near = 1f;
        cam.Far = 5000f;
        cam.Name = "RealWorldCamera";
        AddChild(cam);
        cam.MakeCurrent();

        GD.Print($"[RealWorldRenderer] Camera placed at ({centreX:F0}, 800, {godotZ:F0}).");
    }

    // -------------------------------------------------------------------------
    // Path helpers
    // -------------------------------------------------------------------------

    private static string AreaTag(int areaId)
    {
        // spec: Docs/RE/formats/terrain.md §1.1 — digit decomposition. CONFIRMED.
        int d0 = areaId / 100;
        int d1 = (areaId / 10) % 10;
        int d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}
