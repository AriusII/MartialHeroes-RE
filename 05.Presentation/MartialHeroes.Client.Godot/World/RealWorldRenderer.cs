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

    // Texture-resolution inputs, loaded once in Initialise after the target cell is resolved.
    // The confirmed two-hop chain is: cell/building 1-based index → the cell .map's per-section
    // TEXTURES[idx-1].intTexId → bgtexture pool[intTexId] → data/map{tag}/texture/<rel>.dds.
    // spec: Docs/RE/formats/terrain.md §4.2 (bgtexture.txt) + §3.5 (.map TEXTURES) + §5.6. CONFIRMED.
    private BgTextureCatalog? _bgTextures; // global pool: intTexId → relPath (from bgtexture.txt)
    private MapDescriptor? _cellMap; // the target cell's .map (TERRAIN/BUILDING TEXTURES lists)

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

        // Resolve the target area and cell.
        // The area id is read from client_dir.cfg (key "area="), defaulting to 0.
        // The cell is discovered by enumerating real VFS entries for that area.
        // If the configured area has no cells, we auto-select the first area that does.
        // This ensures we NEVER target a non-existent cell (fixing the (10000,10000) bug).
        GD.Print("[RealWorldRenderer] Initialise: resolving target cell");
        try
        {
            ResolveTargetCell();
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[RealWorldRenderer] ResolveTargetCell failed: {ex.Message} — keeping default ({TargetMapX},{TargetMapZ}).");
        }

        GD.Print($"[RealWorldRenderer] Target cell resolved to ({TargetMapX},{TargetMapZ}) for area {TargetAreaId}.");

        // Load the texture-resolution inputs once: the global bgtexture pool (text companion)
        // and the cell's .map (per-section TEXTURES lists). spec: terrain.md §4.2 + §3.5. CONFIRMED.
        LoadTextureResolutionInputs();

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
    /// Loads the inputs for the two-hop terrain/building texture resolution: the global
    /// <c>bgtexture.txt</c> pool and the target cell's <c>.map</c> descriptor.
    /// spec: Docs/RE/formats/terrain.md §4.2 (bgtexture.txt) + §3.5 (.map TEXTURES). CONFIRMED.
    /// </summary>
    private void LoadTextureResolutionInputs()
    {
        if (_assets is null) return;
        string tag = AreaTag(TargetAreaId);

        try
        {
            // spec: Docs/RE/formats/terrain.md §4.2 — bgtexture.txt path. CONFIRMED.
            // The bgtexture pool + texture .dds are GLOBAL under map000 for ALL areas (there is no
            // per-area bgtexture.txt). spec: Docs/RE/formats/terrain.md §4.2 — global map000 pool. CONFIRMED.
            string txtPath = "data/map000/texture/bgtexture.txt";
            if (_assets.Contains(txtPath))
            {
                _bgTextures = BgTextureTxtParser.Parse(_assets.GetRaw(txtPath));
                GD.Print($"[RealWorldRenderer] bgtexture pool loaded: {_bgTextures.Count} entries.");
            }
            else
            {
                GD.Print($"[RealWorldRenderer] bgtexture.txt absent ({txtPath}) — terrain/buildings stay untextured.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] bgtexture.txt load failed: {ex.Message}");
        }

        try
        {
            // spec: Docs/RE/formats/terrain.md §1.3 per-cell path; §3.5 .map TEXTURES. CONFIRMED.
            string mapPath = $"data/map{tag}/dat/d{tag}x{TargetMapX}z{TargetMapZ}.map";
            if (_assets.Contains(mapPath))
            {
                _cellMap = MapDescriptorParser.Parse(_assets.GetRaw(mapPath));
                GD.Print($"[RealWorldRenderer] cell .map loaded: {_cellMap.Sections.Length} sections.");
            }
            else
            {
                GD.Print($"[RealWorldRenderer] cell .map absent ({mapPath}).");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] cell .map load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves a 1-based texture index from a cell patch (<see cref="TerrainNode"/>) or a BUD
    /// object to a Godot <see cref="ImageTexture"/> via the confirmed two-hop chain:
    /// <c>index-1</c> → the cell <c>.map</c> section's <c>TEXTURES[idx].intTexId</c> →
    /// <c>bgtexture</c> pool → <c>data/map{tag}/texture/&lt;rel&gt;.dds</c>.
    /// spec: Docs/RE/formats/terrain.md §3.5 + §4.2 + §5.6. CONFIRMED.
    /// </summary>
    /// <param name="sectionKeyword">The .map section to read the TEXTURES list from (e.g. "TERRAIN", "BUILDING").</param>
    /// <param name="oneBasedIndex">The 1-based index into that section's TEXTURES list.</param>
    private ImageTexture? ResolveSectionTexture(string sectionKeyword, int oneBasedIndex)
    {
        if (_assets is null || _bgTextures is null || _cellMap is null) return null;
        if (oneBasedIndex <= 0) return null;

        (int Flag, int TexId)[]? list = GetSectionTextures(sectionKeyword);
        if (list is null) return null;

        int li = oneBasedIndex - 1;
        if ((uint)li >= (uint)list.Length) return null;

        string? rel = _bgTextures.GetRelPath(list[li].TexId);
        if (rel is null) return null;

        // Texture .dds live under the GLOBAL map000 pool for all areas. spec: terrain.md §4.2. CONFIRMED.
        string ddsPath = $"data/map000/texture/{rel}.dds";
        return _assets.Contains(ddsPath) ? _assets.LoadTexture(ddsPath) : null;
    }

    /// <summary>Returns the TEXTURES list of the named <c>.map</c> section, or null if absent.</summary>
    private (int Flag, int TexId)[]? GetSectionTextures(string keyword)
    {
        if (_cellMap is null) return null;
        foreach (var section in _cellMap.Sections)
        {
            if (string.Equals(section.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
                return section.Textures;
        }

        return null;
    }

    /// <summary>
    /// Wires a <see cref="TerrainNode.TextureResolver"/> delegate that maps a 1-based cell texture
    /// byte (from TextureIndexGrid) to a real Godot ImageTexture via <see cref="ResolveSectionTexture"/>
    /// reading the cell <c>.map</c> <c>TERRAIN</c> section.
    /// spec: Docs/RE/formats/terrain.md §5.6 Block 3 + §3.5 + §4.2. CONFIRMED.
    /// </summary>
    private void WireTerrainTextureResolver(TerrainNode terrainNode)
    {
        if (_assets is null) return;

        var texCache = new Dictionary<int, ImageTexture?>();
        bool loggedOnce = false;

        terrainNode.TextureResolver = texByte =>
        {
            if (texCache.TryGetValue(texByte, out ImageTexture? cached)) return cached;

            // spec: Docs/RE/formats/terrain.md §3.5 — terrain patches index the .map TERRAIN TEXTURES list.
            ImageTexture? tex = ResolveSectionTexture("TERRAIN", texByte);
            if (tex is not null && !loggedOnce)
            {
                GD.Print($"[RealWorldRenderer] Terrain texture resolved for byte {texByte} (area {TargetAreaId}).");
                loggedOnce = true;
            }

            texCache[texByte] = tex;
            return tex;
        };

        GD.Print($"[RealWorldRenderer] Terrain TextureResolver wired (2-hop bgtexture chain) for area {TargetAreaId}.");
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
        // Point the streaming source at the resolved area BEFORE streaming. The composition root
        // constructs the source bound to area 0; ResolveTargetCell may have picked another area, so
        // we rebind (reloads the area .lst manifest) — otherwise non-zero areas stream empty.
        // spec: Docs/RE/formats/terrain.md §1.1 (per-area path tag) + §1.2 (per-area manifest).
        ctx.StreamingService.SetArea(TargetAreaId);

        _ = Task.Run(async () =>
        {
            try
            {
                await ctx.StreamingService.UpdateCenterAsync(TargetMapX, TargetMapZ)
                    .ConfigureAwait(false);
                int residentCount = ctx.StreamingService.ResidentCount;
                GD.Print($"[RealWorldRenderer] 3×3 terrain ring streaming complete " +
                         $"(area {TargetAreaId}, resident={residentCount} sectors).");
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
            // Wire a texture resolver for BUD objects: maps a 1-based tex_id to a real ImageTexture
            // via the .map BUILDING section's TEXTURES list and the bgtexture pool (same two-hop
            // chain as the terrain). spec: Docs/RE/formats/terrain.md §3.5 + §4.2. CONFIRMED.
            var budTexCache = new Dictionary<uint, ImageTexture?>();

            Func<uint, ImageTexture?> budTexResolver = texId =>
            {
                if (budTexCache.TryGetValue(texId, out ImageTexture? cached)) return cached;
                ImageTexture? tex = ResolveSectionTexture("BUILDING", (int)texId);
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
                 $"{skinnedMesh.FaceCount} faces, {skinnedMesh.Positions.Length} verts — building character node.");

        // Build a live character node (Skeleton3D + skinned mesh + optional AnimationPlayer).
        // The skeleton (.bnd) and animation (.mot) are optional — a missing one yields a static
        // pose. spec: Docs/RE/formats/mesh.md (.skn/.bnd); animation.md (.mot). NO GltfDocument.
        Skeleton? skeleton = TryLoadSkeleton(skinnedMesh);
        AnimationClip? clip = TryLoadAnimation(skinnedMesh);

        Node3D charRoot;
        try
        {
            // Resolve the character's diffuse texture from skin.txt (mesh.IdA -> tex id -> PNG).
            // spec: Docs/RE/formats/mesh.md §.skn texture binding via data/char/skin.txt. CONFIRMED.
            ImageTexture? albedo = CharacterTextureResolver.Resolve(_assets, skinnedMesh);
            // Render the STATIC upright mesh (no skeleton): the up-axis fix in SkinnedCharacterBuilder
            // stands the raw mesh correctly, but the skinned/animated path still explodes the mesh
            // (the Skin inverse-bind matrices are wrong — a dedicated skinning fix is tracked). Passing
            // the skeleton+clip is what triggers the explosion, so we omit them until skinning is fixed.
            _ = skeleton; _ = clip; // intentionally unused until the skinning fix lands
            charRoot = SkinnedCharacterBuilder.Build(skinnedMesh, null, null, albedo: albedo);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SkinnedCharacterBuilder.Build failed: {ex.Message}");
            return;
        }

        // Place the character on the terrain in an OPEN spot, offset ~350 units toward -Z (in
        // front of the building cluster, which sits near the cell centre) so it is not spawned
        // inside a building. Y is lifted onto the (flat) terrain surface (~26).
        // spec: Docs/RE/formats/terrain.md §1.4 (world-space bounds); WorldCoordinates.ToGodot (negate Z).
        float legacyX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        float legacyZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;

        // NOTE: the legacy humanoid mesh currently renders lying down (SkinnedCharacterBuilder
        // up-axis/recentre/skinning needs a dedicated fix — tracked separately). Rotation left at
        // zero until that fix lands so the corrective angle can be derived from a recentred mesh.
        charRoot.RotationDegrees = CharacterUprightRotationDeg;
        charRoot.Position = new Vector3(legacyX, 26f, -legacyZ - 350f);
        charRoot.Scale = Vector3.One * CharacterScale;
        charRoot.Name = "CharacterNode";
        AddChild(charRoot);

        GD.Print($"[RealWorldRenderer] Character spawned from '{sknPath}' " +
                 $"(skeleton={(skeleton is not null)}, anim={(clip is not null)}, scale={CharacterScale:F1}).");

        // Attach a player controller so the avatar can be moved (left-click to move / WASD).
        // Strictly visual/local for now (no game-rule authority).
        try
        {
            var playerController = new PlayerController { Name = "PlayerController" };
            AddChild(playerController);
            playerController.SetAvatar(charRoot);
            playerController.SetGroundY(26f);
            GD.Print("[RealWorldRenderer] PlayerController attached (left-click to move / WASD).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] PlayerController attach failed: {ex.Message}");
        }
    }

    /// <summary>Display scale applied to the spawned character node (legacy unit → Godot). Tuned visually.</summary>
    private const float CharacterScale = 5.0f;

    /// <summary>Corrective rotation (degrees) that stands the legacy character mesh upright. Tuned visually.</summary>
    private static readonly Vector3 CharacterUprightRotationDeg = Vector3.Zero;

    /// <summary>
    /// Attempts to load the character's <c>.bnd</c> skeleton. The <c>.skn</c> <c>id_b</c> matches
    /// the <c>.bnd</c> <c>actor_id</c>; the skeleton path is <c>data/char/bind/g{id_b}.bnd</c>.
    /// Returns null (static pose) when it cannot be resolved — never throws.
    /// spec: Docs/RE/formats/mesh.md §.skn header (id_b → .bnd actor_id) + §.bnd actor_id. CONFIRMED.
    /// </summary>
    private Skeleton? TryLoadSkeleton(SkinnedMesh mesh)
    {
        if (_assets is null) return null;
        try
        {
            string bndPath = BndVirtualPath ?? $"data/char/bind/g{mesh.IdB}.bnd";
            if (mesh.IdB == 0 || !_assets.Contains(bndPath))
            {
                GD.Print($"[RealWorldRenderer] No skeleton for IdB={mesh.IdB} ({bndPath}) — static pose.");
                return null;
            }

            ReadOnlyMemory<byte> data = _assets.GetRaw(bndPath);
            if (data.IsEmpty) return null;

            Skeleton skel = BndParser.Parse(data);
            GD.Print($"[RealWorldRenderer] Skeleton loaded: {bndPath} ({skel.Bones.Length} bones).");
            return skel;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] .bnd load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Attempts to load the character's looped idle animation <c>.mot</c> via
    /// <c>actormotion.txt</c>. Returns null (static rest pose) when none is resolved — never throws.
    /// spec: Docs/RE/formats/animation.md §.mot; Docs/RE/formats/mesh.md §actormotion.txt. CONFIRMED.
    /// </summary>
    private AnimationClip? TryLoadAnimation(SkinnedMesh mesh)
    {
        if (_assets is null || mesh.IdB == 0) return null;
        try
        {
            string? motPath = ResolveIdleMotPath(mesh.IdB);
            if (motPath is null || !_assets.Contains(motPath))
            {
                GD.Print($"[RealWorldRenderer] No idle .mot for IdB={mesh.IdB} — static rest pose.");
                return null;
            }

            ReadOnlyMemory<byte> data = _assets.GetRaw(motPath);
            if (data.IsEmpty) return null;

            AnimationClip clip = AnimationParser.Parse(data);
            GD.Print($"[RealWorldRenderer] Animation loaded: {motPath}.");
            return clip;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] .mot load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves the looped idle motion id for an actor class via <c>data/char/actormotion.txt</c>:
    /// the row whose column 2 equals <paramref name="actorClassId"/> (= the skin/skeleton id_b);
    /// the idle-peace motion id is column 16. Returns <c>data/char/mot/g{id}.mot</c> or null.
    /// spec: Docs/RE/formats/mesh.md §actormotion.txt (TAB-separated; col2=class, col16=idle). CONFIRMED.
    /// </summary>
    private string? ResolveIdleMotPath(uint actorClassId)
    {
        if (_assets is null) return null;
        const string tablePath = "data/char/actormotion.txt";
        if (!_assets.Contains(tablePath)) return null;

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        string text = System.Text.Encoding.GetEncoding(949).GetString(_assets.GetRaw(tablePath).Span);

        foreach (string rawLine in text.Split('\n'))
        {
            string[] cols = rawLine.Replace("\r", string.Empty).Split('\t');
            if (cols.Length <= 16) continue;
            if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != actorClassId) continue;

            string idle = cols[16].Trim();
            if (idle.Length == 0 || idle == "0") return null;
            return $"data/char/mot/g{idle}.mot";
        }

        return null;
    }

    private string? DiscoverFirstSknPath()
    {
        if (_assets is null) return null;

        // Prefer a real HUMANOID player-class skin (Musa class, IdB=1) over the spider/item props,
        // so the default avatar is a recognisable character. spec: Docs/RE/formats/mesh.md §.skn.
        string? humanoid = CharacterTextureResolver.PickHumanoidPlayerSkin(_assets);
        if (humanoid is not null) return humanoid;

        // Fallback: other character body skins (data/char/skin/g{id}.skn) then item-prop skins.
        // spec: Docs/RE/formats/mesh.md §.skn — "data/char/skin/": CONFIRMED.
        string[] candidates =
        [
            "data/char/skin/g200002620.skn",
            "data/char/skin/g200003000.skn",
            "data/char/skin/g200002630.skn",
            // item-prop fallbacks (single-bone) if no character skin is present:
            "data/item/skin/gi213050001.skn",
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
        // Compute the Godot-space centre of the resolved terrain cell.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        float centreX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        float centreZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;
        float godotZ = -centreZ; // negate Z: spec WorldCoordinates.ToGodot.

        var cellCentre = new Vector3(centreX, 0f, godotZ);

        // Replace any existing static Camera3D with a free/orbital CameraController so the user
        // can explore the world (orbit/zoom/pan, Tab → free-fly WASD). Configure() reproduces the
        // previous oblique aerial framing as the initial view.
        Camera3D? existing = GetViewport()?.GetCamera3D();
        if (existing is not null && existing is not CameraController)
        {
            existing.GetParent()?.RemoveChild(existing);
            existing.QueueFree();
        }

        var cam = new CameraController { Name = "CameraController" };
        AddChild(cam);
        cam.Configure(cellCentre, 1024f); // spec: terrain.md §1.4 — cell size 1024. CONFIRMED.
        cam.MakeCurrent();

        GD.Print($"[RealWorldRenderer] CameraController spawned, framing cell centre ({centreX:F0}, 0, {godotZ:F0}). " +
                 "Controls: RMB orbit, wheel zoom, MMB pan, Tab=free-fly (WASD/QE, Shift fast), Esc release.");
    }

    // -------------------------------------------------------------------------
    // Target cell discovery
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves <see cref="TargetAreaId"/>, <see cref="TargetMapX"/> and <see cref="TargetMapZ"/>
    /// by enumerating real VFS entries instead of using a hard-coded coordinate.
    ///
    /// Resolution order:
    ///   1. Read "area=" key from client_dir.cfg (defaults to 0).
    ///   2. Enumerate .ted entries in the VFS for that area via
    ///      <see cref="RealClientAssets.EnumerateTerrainCells"/>.
    ///   3. If at least one cell is found for the requested area, pick the first (sorted by
    ///      ascending mapX then mapZ — typically the top-left cell).
    ///   4. If the requested area has NO cells, try areas 0..20 in order and pick the first
    ///      area+cell pair that exists. This avoids targeting an empty area entirely.
    ///   5. If no cells are found in any area, fall back to the configured defaults and log a
    ///      warning — streaming will silently produce empty sectors but won't crash.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.3 — per-cell path pattern. CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §1.1 — area id digit decomposition. CONFIRMED.
    /// </summary>
    private void ResolveTargetCell()
    {
        if (_assets is null) return;

        // Read area= from config. Default 0. Silently ignore missing key.
        int configArea = ReadAreaFromConfig();
        GD.Print($"[RealWorldRenderer] Config area={configArea} (from client_dir.cfg or default).");

        // Try to get cells for the configured area first.
        List<(int MapX, int MapZ)> cells = _assets.EnumerateTerrainCells(configArea);
        if (cells.Count > 0)
        {
            (int mx, int mz, bool fullRing) = PickRingCenter(cells);
            TargetAreaId = configArea;
            TargetMapX = mx;
            TargetMapZ = mz;
            GD.Print($"[RealWorldRenderer] Area {configArea}: {cells.Count} cells found — " +
                     $"selected ({TargetMapX},{TargetMapZ}) (full 3×3 ring={fullRing}).");
            return;
        }

        GD.Print($"[RealWorldRenderer] Area {configArea} has no .ted cells — scanning areas 0..20.");

        // Auto-select: try areas 0 through 20 and take the first that has cells.
        for (int area = 0; area <= 20; area++)
        {
            if (area == configArea) continue; // already tried
            List<(int MapX, int MapZ)> areaCells = _assets.EnumerateTerrainCells(area);
            if (areaCells.Count > 0)
            {
                (int mx, int mz, bool fullRing) = PickRingCenter(areaCells);
                TargetAreaId = area;
                TargetMapX = mx;
                TargetMapZ = mz;
                GD.Print($"[RealWorldRenderer] Auto-selected area {area}: {areaCells.Count} cells — " +
                         $"cell ({TargetMapX},{TargetMapZ}) (full 3×3 ring={fullRing}).");
                return;
            }
        }

        // No cells found anywhere — keep configured defaults but warn clearly.
        GD.PrintErr($"[RealWorldRenderer] WARNING: no .ted cells found in any area 0..20. " +
                    $"Keeping defaults ({TargetMapX},{TargetMapZ}) — streaming will produce empty sectors.");
    }

    /// <summary>
    /// Picks the cell to centre the 3×3 streaming ring on, given every <c>.ted</c> cell present in an
    /// area. Prefers a cell whose full 3×3 ring of neighbours all exist (so all nine sectors render),
    /// choosing the complete-ring candidate nearest the area centroid; falls back to the middle
    /// element of the sorted list when no cell has a complete ring (e.g. a 1- or 2-cell area).
    /// spec: Docs/RE/formats/terrain.md §9.2 (3×3 ring) + §1.3 (per-cell path). CONFIRMED.
    /// </summary>
    /// <param name="cells">All cell coordinates available for the area (may be unsorted).</param>
    /// <returns>The chosen centre cell and whether its full 3×3 ring exists.</returns>
    private static (int MapX, int MapZ, bool FullRing) PickRingCenter(List<(int MapX, int MapZ)> cells)
    {
        // Deterministic order so a tie resolves the same way every run.
        cells.Sort((a, b) => a.MapX != b.MapX ? a.MapX.CompareTo(b.MapX) : a.MapZ.CompareTo(b.MapZ));

        var present = new HashSet<(int, int)>(cells.Count);
        long sumX = 0, sumZ = 0;
        foreach ((int x, int z) in cells)
        {
            present.Add((x, z));
            sumX += x;
            sumZ += z;
        }

        // Area centroid — the relief showcase looks best near the middle of the populated region.
        double centroidX = sumX / (double)cells.Count;
        double centroidZ = sumZ / (double)cells.Count;

        bool best = false;
        (int MapX, int MapZ) bestCell = cells[cells.Count / 2];
        double bestDist = double.MaxValue;

        foreach ((int cx, int cz) in cells)
        {
            // A full ring requires all eight neighbours (Chebyshev radius 1) to be present.
            bool full = true;
            for (int dz = -1; dz <= 1 && full; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!present.Contains((cx + dx, cz + dz)))
                    {
                        full = false;
                        break;
                    }
                }
            }

            if (!full)
            {
                continue;
            }

            double ddx = cx - centroidX;
            double ddz = cz - centroidZ;
            double dist = ddx * ddx + ddz * ddz;
            if (!best || dist < bestDist)
            {
                best = true;
                bestCell = (cx, cz);
                bestDist = dist;
            }
        }

        return (bestCell.MapX, bestCell.MapZ, best);
    }

    /// <summary>
    /// Reads the "area=" integer key from client_dir.cfg.
    /// Returns 0 (the default) when the key is absent or unparseable.
    /// </summary>
    private static int ReadAreaFromConfig()
    {
        try
        {
            // Reuse ClientPathResolver's internal config reader by re-opening the same file.
            // Duplicate the minimal read logic here to keep the coupling narrow.
            // Fully-qualify to avoid ambiguity with the MartialHeroes namespace. spec: Godot API.
            string absPath = global::Godot.ProjectSettings.GlobalizePath("res://client_dir.cfg");
            if (!File.Exists(absPath)) return 0;

            foreach (string rawLine in File.ReadLines(absPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                if (k.Equals("area", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(v, out int parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // Any I/O error → default 0.
        }

        return 0;
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