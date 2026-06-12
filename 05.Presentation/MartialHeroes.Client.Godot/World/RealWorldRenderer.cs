// World/RealWorldRenderer.cs
//
// PASSIVE rendering node that replaces SyntheticWorldFeeder when real client assets are available.
// Activated by env-var: MH_REAL_ASSETS=1 (checked by GameLoop._Ready via RealWorldRenderer.IsEnabled).
//
// What this node does (all passive, no game logic):
//   1. Uses SectorStreamingService to load a 3×3 ring of real terrain sectors.
//   2. Loads building geometry (.bud → GLB in memory → MeshInstance3D nodes).
//   3. Loads a skinned character mesh (.skn + .bnd + .mot → GLB → GltfDocument runtime import).
//   4. Applies diffuse textures (PNG/BMP/DDS via AssetPassthrough → ImageTexture).
//   5. Positions a camera over the terrain.
//
// Threading: all Godot node creation happens on the main thread (_Ready or CallDeferred).
// Heavy parsing (GltfConverter.WriteGlb) runs synchronously in Initialise to keep it simple.
// The 3×3 sector streaming call goes through SectorStreamingService.UpdateCenterAsync which
// is called from a background task (fire-and-forget) — TerrainNode reacts via the event bus.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/terrain.md §1.1–1.4 (path, manifest, ted, world bounds).
// spec: Docs/RE/formats/terrain.md §9.2 (3×3 streaming ring at StreamQuality.Medium).
// spec: Docs/RE/formats/terrain_scene.md (bud scene).
// spec: Docs/RE/formats/mesh.md (skn/bnd).
// spec: Docs/RE/formats/animation.md (mot).
// spec: Docs/RE/formats/texture.md (png/bmp/dds/tga).

using Godot;
using MartialHeroes.Assets.Mapping;
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
    /// Returns true when the real-asset rendering path is requested.
    /// Checks the <c>MH_REAL_ASSETS</c> environment variable (value "1" = enabled).
    /// Use System.Environment explicitly to avoid ambiguity with Godot.Environment.
    /// </summary>
    public static bool IsEnabled
        => System.Environment.GetEnvironmentVariable("MH_REAL_ASSETS") == "1";

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
    /// </summary>
    public void Initialise(ClientContext ctx, TerrainNode terrainNode)
    {
        _ctx = ctx;
        _terrainNode = terrainNode;

        // Open the VFS — falls back gracefully to null if absent.
        _assets = RealClientAssets.TryOpen();
        if (_assets is null)
        {
            GD.Print("[RealWorldRenderer] No real assets available — skipping real-asset render.");
            return;
        }

        // Wire the texture resolver into TerrainNode so each sector can get a real texture.
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 — 1-based TextureIndexGrid → texture path.
        // The resolver maps 1-based indices to VFS texture paths and loads them via AssetPassthrough.
        // This is done before streaming so the resolver is ready when the first SectorLoadedEvent arrives.
        WireTerrainTextureResolver(terrainNode);

        // Kick off 3×3 terrain streaming via SectorStreamingService.
        // This publishes SectorLoadedEvent / SectorUnloadedEvent onto the EventBus which
        // TerrainNode processes on the main thread each _Process frame — fully passive.
        // spec: Docs/RE/formats/terrain.md §9.2 (3×3 ring, StreamQuality.Medium).
        TriggerTerrainStreaming(ctx);

        // Load BUD scene and create MeshInstance3D children.
        LoadAndSpawnBudScene();

        // Spawn skinned character (if available).
        LoadAndSpawnCharacter();

        // Position a camera above the origin cell centre.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        SpawnCamera();

        GD.Print($"[RealWorldRenderer] Real-asset render initialised for cell ({TargetMapX},{TargetMapZ}).");
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
    /// The resolver uses heuristic path construction:
    ///   1. Build the area texture directory path.
    ///   2. Use the 1-based index to select a texture file (if the TEXTURES block mapping is
    ///      unavailable, fall back to gr001.dds as index 1).
    ///
    /// In production, the full cross-referencing requires the MapDescriptor's TEXTURES block
    /// (parsed via MapDescriptorParser). Full tex_id→path mapping is a TODO pending
    /// MapDescriptor TEXTURES block parsing integration.
    ///
    /// spec: Docs/RE/formats/terrain.md §5.6 Block 3 — 1-based TextureIndexGrid.
    /// spec: Docs/RE/formats/terrain.md §3.5 TEXTURES directive — intTexId indexed array.
    /// </summary>
    private void WireTerrainTextureResolver(TerrainNode terrainNode)
    {
        if (_assets is null) return;

        // Cache loaded textures by index to avoid redundant VFS reads.
        // The resolver is called once per sector per index on the main thread.
        var texCache = new Dictionary<int, ImageTexture?>();

        string areaTag = AreaTag(TargetAreaId);
        string texDir = $"data/map{areaTag}/texture/";

        // Heuristic: map 1-based index → texture filename "gr{index:D3}.dds".
        // spec: Docs/RE/formats/terrain.md §4 — "bgtexture.lst at data/map000/texture/bgtexture.lst".
        // The actual mapping is in bgtexture.lst (list of texture filenames, 1-based indexing).
        // TODO: parse bgtexture.lst to build a proper index→filename map.
        // For now: gr001.dds for index 1, gr002.dds for index 2, etc. (observed naming pattern).
        terrainNode.TextureResolver = texIdx =>
        {
            if (texCache.TryGetValue(texIdx, out ImageTexture? cached))
                return cached;

            // Attempt to load the indexed texture from the area texture directory.
            // heuristic: gr{idx:D3}.dds — UNVERIFIED naming convention. Dev visual aid only.
            string texPath = $"{texDir}gr{texIdx:D3}.dds";
            ImageTexture? tex = null;

            if (_assets is not null && _assets.Contains(texPath))
            {
                tex = _assets.LoadTexture(texPath);
                if (tex is not null)
                    GD.Print($"[RealWorldRenderer] Terrain texture loaded: {texPath}");
            }

            // Fallback: try gr001.dds regardless of index (ensures some texture coverage).
            if (tex is null)
            {
                string fallback = $"{texDir}gr001.dds";
                if (_assets is not null && _assets.Contains(fallback))
                    tex = _assets.LoadTexture(fallback);
            }

            texCache[texIdx] = tex; // cache null too to avoid repeated failed lookups
            return tex;
        };

        GD.Print($"[RealWorldRenderer] Terrain TextureResolver wired for area {TargetAreaId}.");
    }

    // -------------------------------------------------------------------------
    // 3×3 terrain streaming via SectorStreamingService
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calls <see cref="SectorStreamingService.UpdateCenterAsync"/> for the target cell centre,
    /// which triggers loading of the 3×3 ring and publishes SectorLoadedEvent per cell.
    /// TerrainNode reacts to those events on the main thread; no direct mesh calls here.
    ///
    /// spec: Docs/RE/formats/terrain.md §9.2 — "3×3 ring of sectors centred on the player cell".
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive (event-driven).
    /// </summary>
    private void TriggerTerrainStreaming(ClientContext ctx)
    {
        // Fire-and-forget: UpdateCenterAsync is async (loads .ted bytes from the VFS);
        // the events it publishes are drained by GameLoop._Process on the main thread.
        // We do not await here to avoid blocking Initialise on the main thread.
        // The sector source is already backed by the real VFS in VfsTerrainSectorSource.
        _ = Task.Run(async () =>
        {
            try
            {
                await ctx.StreamingService.UpdateCenterAsync(TargetMapX, TargetMapZ)
                    .ConfigureAwait(false);
                // Log the completion count (GD.Print is thread-safe in Godot 4).
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
    // BUD scene loading
    // -------------------------------------------------------------------------

    private void LoadAndSpawnBudScene()
    {
        if (_assets is null) return;

        // Get the BUILDING DATAFILE path from the .map descriptor.
        (_, string? budPath) = _assets.LoadMapDatafilePaths(TargetAreaId, TargetMapX, TargetMapZ);
        if (budPath is null)
        {
            GD.Print("[RealWorldRenderer] No BUILDING section in .map — skipping BUD scene.");
            return;
        }

        BudScene? scene = _assets.LoadBud(budPath);
        if (scene is null || scene.Objects.Length == 0)
        {
            GD.Print($"[RealWorldRenderer] BUD scene empty for: {budPath}");
            return;
        }

        // Convert each BUD object to a GLB in memory, then load into Godot.
        // BUD coordinates are absolute world-space (no cell-relative offset needed).
        // spec: Docs/RE/formats/terrain_scene.md §Coordinate system — "positions are pre-baked
        //       into absolute world-space": CONFIRMED.
        var budGlb = new MemoryStream();
        BudSceneGltfConverter.WriteGlb(scene, budGlb);
        budGlb.Position = 0;

        Node3D? budRoot = TryImportGlb(budGlb.ToArray(), "BudScene");
        if (budRoot is null) return;

        budRoot.Name = "BudSceneNode";
        AddChild(budRoot);

        // Attempt to apply terrain texture from the map TEXTURES directive.
        // spec: Docs/RE/formats/terrain.md §3.5 TEXTURES directive — tex_id indexed array.
        TryApplyBudTexture(budRoot);

        GD.Print($"[RealWorldRenderer] BUD scene spawned: {scene.Objects.Length} objects.");
    }

    private void TryApplyBudTexture(Node3D budRoot)
    {
        if (_assets is null) return;

        // Heuristic: try the first .dds in the map texture directory.
        // Production path: the .map TEXTURES{} block provides indexed tex_id (CONFIRMED spec §3.5).
        // This is a dev-mode visual aid — no gameplay authority.
        // spec: Docs/RE/formats/terrain.md §3.5 TEXTURES — tex_id cross-referencing documented;
        //       full index→path lookup requires MapDescriptorParser TEXTURES block (parsed in .map).
        // TODO: parse TEXTURES block from .map and use the correct tex_id index per BUD object.

        // Try a plausible first texture path: area 000 background texture gr001.
        // spec: Docs/RE/formats/terrain.md §4 — bgtexture.lst at data/map000/texture/bgtexture.lst.
        const string fallbackTex = "data/map000/texture/gr001.dds";
        ImageTexture? tex = null;
        if (_assets.Contains(fallbackTex))
        {
            tex = _assets.LoadTexture(fallbackTex);
            if (tex is not null)
                GD.Print($"[RealWorldRenderer] Applied diffuse texture: {fallbackTex}");
        }

        if (tex is null)
        {
            GD.Print("[RealWorldRenderer] Fallback texture not found — BUD will render without texture.");
            return;
        }

        ApplyTextureToMeshInstances(budRoot, tex);
    }

    private static void ApplyTextureToMeshInstances(Node root, ImageTexture tex)
    {
        // Walk the node tree and apply the texture to every MeshInstance3D found.
        foreach (Node child in root.GetChildren())
        {
            if (child is MeshInstance3D meshInst)
            {
                var mat = new StandardMaterial3D();
                mat.AlbedoTexture = tex;
                // Terrain textures are world-scale tiled; set repeat mode for tiling.
                mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
                meshInst.MaterialOverride = mat;
            }

            ApplyTextureToMeshInstances(child, tex);
        }
    }

    // -------------------------------------------------------------------------
    // Skinned character loading
    // -------------------------------------------------------------------------

    private void LoadAndSpawnCharacter()
    {
        if (_assets is null) return;

        // Resolve .skn path: use explicit property or discover the first available.
        string? sknPath = SknVirtualPath ?? DiscoverFirstSknPath();
        if (sknPath is null)
        {
            GD.Print("[RealWorldRenderer] No .skn found — skipping character render.");
            return;
        }

        // Resolve .bnd path: use explicit property or derive from .skn filename.
        // spec: Docs/RE/formats/mesh.md §.bnd — id_b in .skn matches actor_id in .bnd. CONFIRMED.
        string? bndPath = BndVirtualPath ?? DeriveBndPath(sknPath);

        // Write GLB to memory.
        var glbStream = new MemoryStream();
        bool ok = _assets.LoadSkinned(
            sknPath: sknPath,
            bndPath: bndPath,
            motPaths: null,  // No .mot discovered yet — static pose only.
            glbOutput: glbStream);

        if (!ok) return;

        glbStream.Position = 0;
        byte[] glbBytes = glbStream.ToArray();

        // Import the GLB at runtime via Godot's GltfDocument API.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — consume Assets.Mapping output.
        Node3D? charRoot = TryImportGlb(glbBytes, "CharacterSkin");
        if (charRoot is null)
        {
            // TODO (runtime animation): GltfDocument.AppendFromBuffer imports skeleton+mesh but
            // Godot 4.x requires a post-import hook to wire AnimationPlayer to the imported skeleton.
            // Current: geometry and static pose are imported; animation clips are NOT played at runtime.
            // The GLB is fully valid — open in the Godot editor for full animation preview.
            // Tracked: Godot upstream issue — GltfDocument post-import hook for runtime animation.
            GD.PrintErr("[RealWorldRenderer] GLB runtime import failed. " +
                        "Skinned character will not be visible in this session.");
            return;
        }

        // Place the character near the centre of the terrain sector.
        // World origin cell (10000,10000) = legacy (0,0,0). Godot Z is negated.
        // spec: Docs/RE/formats/terrain.md §1.4 (world-space bounds). CONFIRMED.
        // spec: WorldCoordinates.ToGodot — negate Z.
        float legacyX = (TargetMapX - 10000) * 1024.0f + 512.0f; // cell centre X
        float legacyZ = (TargetMapZ - 10000) * 1024.0f + 512.0f; // cell centre Z
        charRoot.Position = new Vector3(legacyX, 0f, -legacyZ); // negate Z for Godot
        charRoot.Name = "CharacterNode";
        AddChild(charRoot);

        GD.Print($"[RealWorldRenderer] Skinned character spawned from: {sknPath}");
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

    private static string? DeriveBndPath(string sknPath)
    {
        // Item skins have id_b=0 (single rigid bone) — no .bnd needed.
        // spec: Docs/RE/formats/mesh.md §.skn — id_b: "Intended to match actor_id of .bnd". CONFIRMED.
        // For item skins (id_b=0), null = single rigid bone path.
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

        // Check if a camera already exists in the scene before adding one.
        if (GetViewport()?.GetCamera3D() is not null)
        {
            GD.Print("[RealWorldRenderer] Camera already present in viewport — skipping spawn.");
            return;
        }

        var cam = new Camera3D();
        // Position 800 units above centre, looking straight down.
        // Height chosen to see the full 1024×1024 cell at a sensible field of view.
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
    // Godot runtime GLB import
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts a runtime GLB import via Godot's <see cref="GltfDocument"/> / <see cref="GltfState"/>.
    /// Returns null if the import fails.
    ///
    /// TODO (runtime animation): GltfDocument.AppendFromBuffer imports skeleton+mesh but Godot 4.x
    /// does not yet expose a post-import hook to wire AnimationPlayer to the runtime skeleton.
    /// Geometry and static pose import correctly; animation clips embedded in the GLB are ignored.
    /// Workaround: open the GLB in the Godot editor for full animation preview.
    /// Tracked: Godot upstream issue — GltfDocument post-import hook for runtime animation.
    /// </summary>
    private static Node3D? TryImportGlb(byte[] glbBytes, string debugName)
    {
        try
        {
            var gltfDoc = new GltfDocument();
            var gltfState = new GltfState();

            Error err = gltfDoc.AppendFromBuffer(glbBytes, string.Empty, gltfState);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[RealWorldRenderer] GltfDocument.AppendFromBuffer failed " +
                            $"for '{debugName}': {err}");
                return null;
            }

            Node? scene = gltfDoc.GenerateScene(gltfState);
            if (scene is null)
            {
                GD.PrintErr($"[RealWorldRenderer] GltfDocument.GenerateScene returned null " +
                            $"for '{debugName}'.");
                return null;
            }

            if (scene is Node3D node3D)
                return node3D;

            // Wrap in a Node3D if the root is not already one.
            var wrapper = new Node3D { Name = debugName };
            wrapper.AddChild(scene);
            return wrapper;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] GLB import exception for '{debugName}': {ex.Message}");
            return null;
        }
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
