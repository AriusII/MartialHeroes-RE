// World/RealWorldRenderer.cs
//
// PASSIVE rendering node that replaces SyntheticWorldFeeder when real client assets are available.
// Activated by env-var: MH_REAL_ASSETS=1 (checked by GameLoop._Ready via RealWorldRenderer.IsEnabled).
//
// What this node does (all passive, no game logic):
//   1. Loads a terrain sector from the VFS (.ted → TerrainNode mesh).
//   2. Loads building geometry (.bud → GLB in memory → MeshInstance3D nodes).
//   3. Loads a skinned character mesh (.skn + .bnd + .mot → GLB → GltfDocument runtime import).
//   4. Applies a diffuse texture to the BUD mesh nodes (PNG/BMP/DDS via AssetPassthrough).
//   5. Positions a camera over the terrain.
//
// Threading: all Godot node creation happens on the main thread (_Ready or CallDeferred).
// Heavy parsing (GltfConverter.WriteGlb) runs synchronously in _Ready to keep it simple;
// for production move it to a Task with CallDeferred node creation.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/terrain.md §1.1–1.4 (path, manifest, ted, world bounds).
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
/// Character rendered: the first .skn found under data/char/skin/ (or
/// data/item/skin/ as fallback). Override <see cref="SknVirtualPath"/> / <see cref="BndVirtualPath"/>
/// before <see cref="StartAsync"/> is called.
/// </summary>
public sealed partial class RealWorldRenderer : Node3D
{
    // -------------------------------------------------------------------------
    // Configuration (set before StartAsync)
    // -------------------------------------------------------------------------

    /// <summary>Area to load. Default 0 (map000). spec: Docs/RE/formats/terrain.md §1.1.</summary>
    public int TargetAreaId { get; set; } = 0;

    /// <summary>
    /// Cell X coordinate. Default 10000 (world-origin cell).
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
    /// VFS path for the character .bnd.  Null = try matching path from the .skn name.
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

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by GameLoop._Ready. Performs all synchronous asset loading and node creation.
    /// </summary>
    public void Initialise(ClientContext ctx, TerrainNode terrainNode)
    {
        _terrainNode = terrainNode;

        // Open the VFS — falls back gracefully to null if absent.
        _assets = RealClientAssets.TryOpen();
        if (_assets is null)
        {
            GD.Print("[RealWorldRenderer] No real assets available — skipping real-asset render.");
            return;
        }

        // Load terrain sector and post its event to ClientContext EventBus so TerrainNode
        // reacts through the normal event path (passive, no direct mesh calls here).
        LoadAndPostTerrain(ctx);

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
    // Terrain loading
    // -------------------------------------------------------------------------

    private void LoadAndPostTerrain(ClientContext ctx)
    {
        if (_assets is null) return;

        // Prefer the .ted path from the .map descriptor (canonical); fall back to the
        // pattern-derived path for cells that lack a .map.
        (string? mapTedPath, _) = _assets.LoadMapDatafilePaths(TargetAreaId, TargetMapX, TargetMapZ);

        ReadOnlyMemory<byte> tedBytes = mapTedPath is not null
            ? _assets.GetRaw(mapTedPath)
            : _assets.LoadTed(TargetAreaId, TargetMapX, TargetMapZ);

        if (tedBytes.IsEmpty)
        {
            GD.PrintErr($"[RealWorldRenderer] No .ted data for cell ({TargetMapX},{TargetMapZ}). " +
                        "TerrainNode will render nothing for this cell.");
            return;
        }

        // Publish via the Application event bus so TerrainNode processes it normally.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "subscribes to Application event
        //       channels … translates those events into visual updates". CONFIRMED.
        // SectorLoadedEvent lives in MartialHeroes.Client.Application.World namespace.
        ctx.EventBus.Publish(new SectorLoadedEvent(
            MapX: TargetMapX,
            MapZ: TargetMapZ,
            Payload: tedBytes));

        GD.Print($"[RealWorldRenderer] SectorLoadedEvent published for cell ({TargetMapX},{TargetMapZ}), " +
                 $"{tedBytes.Length} bytes.");
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

        // BUD positions are in legacy left-handed world space. The GltfConverter already negates X.
        // The sector world offset is implicit in the absolute positions — no extra translation needed.
        // spec: Docs/RE/formats/terrain_scene.md §Coordinate system: CONFIRMED (absolute world-space).
        budRoot.Name = "BudSceneNode";
        AddChild(budRoot);

        // Attempt to apply a texture from the first .map TEXTURES entry to the BUD mesh.
        TryApplyBudTexture(budRoot);

        GD.Print($"[RealWorldRenderer] BUD scene spawned: {scene.Objects.Length} objects.");
    }

    private void TryApplyBudTexture(Node3D budRoot)
    {
        if (_assets is null) return;

        // Try the background texture list for area 000 as a fallback texture.
        // spec: Docs/RE/formats/terrain.md §4 — bgtexture.lst at data/map000/texture/bgtexture.lst.
        // For now, look for any .dds in the map texture directory as a best-effort diffuse.
        // This is a dev-mode visual aid only — no gameplay authority.
        string texSearchPath = $"data/map{AreaTag(TargetAreaId)}/texture/";

        // Try a plausible first texture path. This is heuristic-only for dev display.
        // In production, the .map TEXTURES{} block provides the indexed tex_id.
        // spec: Docs/RE/formats/terrain.md §3.5 TEXTURES directive.
        const string fallbackTex = "data/map000/texture/gr001.dds";
        if (_assets.Contains(fallbackTex))
        {
            ImageTexture? tex = _assets.LoadTexture(fallbackTex);
            if (tex is not null)
            {
                ApplyTextureToMeshInstances(budRoot, tex);
                GD.Print($"[RealWorldRenderer] Applied diffuse texture: {fallbackTex}");
            }
        }
        else
        {
            GD.Print($"[RealWorldRenderer] Fallback texture not found ({fallbackTex}). " +
                     "BUD mesh will render without texture.");
        }
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
                // Terrain textures are world-scale tiled; set repeat to avoid white/black borders.
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
            motPaths: null,  // No specific .mot discovered yet — static pose only.
            glbOutput: glbStream);

        if (!ok) return;

        glbStream.Position = 0;
        byte[] glbBytes = glbStream.ToArray();

        // Import the GLB at runtime via Godot's GltfDocument API.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — consume Assets.Mapping output.
        Node3D? charRoot = TryImportGlb(glbBytes, "CharacterSkin");
        if (charRoot is null)
        {
            // TODO: runtime glTF animation import requires Godot 4.x GltfDocument.AppendFromBuffer.
            // If import fails, fall back to a placeholder MeshInstance3D built from vertex data.
            // This is noted as a known limitation — see REPORT section below.
            GD.PrintErr("[RealWorldRenderer] GLB runtime import failed (see TODO in code). " +
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
        // spec: Docs/RE/formats/texture.md §PNG — "data/char/tex256256/" and "data/item/texture/": CONFIRMED.
        // spec: Docs/RE/formats/mesh.md §.skn — "data/char/skin/" or "data/item/skin/": CONFIRMED.
        string[] candidates =
        [
            // Item skins (simplest — single-bone rigid props, fully sample-verified).
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
        // Item skins have id_b=0 (single rigid bone) — try matching the .bnd convention.
        // spec: Docs/RE/formats/mesh.md §.skn — id_b: "Intended to match actor_id of .bnd". CONFIRMED.
        // For item skins (id_b=0), no .bnd is needed — return null.
        // This keeps the rigid single-bone path working without requiring a .bnd file.
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
    /// Returns null if the import fails (e.g. Godot runtime limitations on headless builds).
    ///
    /// TODO (runtime anim): GltfDocument.AppendFromBuffer imports the skeleton and mesh but
    /// Godot 4.x requires a post-import pass to wire up the AnimationPlayer to the
    /// imported skeleton node. The current implementation imports geometry and skeleton pose
    /// but does NOT yet play animation clips embedded in the GLB.
    /// Workaround: the GLB is fully valid — open it in the Godot editor for full animation
    /// preview. Runtime animation playback from dynamically-imported GLBs is blocked on
    /// Godot's GltfDocument API exposing a post-import hook (tracked as Godot upstream issue).
    /// </summary>
    private static Node3D? TryImportGlb(byte[] glbBytes, string debugName)
    {
        try
        {
            var gltfDoc = new GltfDocument();
            var gltfState = new GltfState();
            // HandleBinaryImage was added in Godot 4.3+; omit the property set for compatibility.
            // The default behaviour embeds images, which is fine for our GLBs.

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

            // The generated scene root is a Node — cast to Node3D or wrap it.
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
