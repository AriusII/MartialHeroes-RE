// World/RealWorldRenderer.cs
//
// PASSIVE rendering node that replaces SyntheticWorldFeeder when real client assets are available.
// Activated when ClientPathResolver.RealAssetsEnabled returns true (client dir résolu + real_assets != false).
//
// What this node does (all passive, no game logic):
//   1. Uses SectorStreamingService to load a 3×3 ring of real terrain sectors.
//   2. Loads building geometry (.bud → ArrayMesh via BudMeshBuilder — NO GltfDocument).
//   3. Loads a skinned character (.skn + .bnd + idle .mot → CPU-skinned, animated ArrayMesh via
//      SkinnedCharacterBuilder / SkinnedCharacterNode — NO GltfDocument).
//   4. Applies diffuse textures (PNG/BMP/DDS via AssetPassthrough → ImageTexture).
//   5. Positions a camera over the terrain.
//   6. PLAYER-FOLLOWING TERRAIN STREAMING: each frame, if the player crosses a cell boundary
//      (Chebyshev distance ≥ 1 from the current streaming anchor with hysteresis), the streaming
//      ring is re-centred on the player's current cell. The initial anchor is always the spawn-
//      density peak (boot behaviour unchanged). New sectors are loaded asynchronously off the main
//      thread; sectors > 2 cells from the new anchor are evicted by SectorStreamingService
//      (Chebyshev eviction policy). spec: Docs/RE/specs/resource_pipeline.md §4.3 (streamer thread).
//      spec: Docs/RE/formats/terrain.md §9.2 / §9.3 (ring eviction).
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
// spec: Docs/RE/formats/terrain.md §12.2 (5×5 streaming ring at StreamQuality.High, configurable via ring_radius= in client_dir.cfg).
// spec: Docs/RE/formats/terrain_scene.md (bud scene).
// spec: Docs/RE/formats/mesh.md (skn/bnd).
// spec: Docs/RE/formats/texture.md (png/bmp/dds/tga).

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Client.Godot.Adapters;
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
    // Composer-driven rendering (CYCLE 2 Phase 2-A)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When true, terrain and buildings are rendered FROM the assembled cell produced by the
    /// CYCLE-1 AreaComposer pipeline, rather than the legacy direct-from-VFS path.
    ///
    /// Default: false — the legacy path is byte-identical to the pre-2-A build (zero regression).
    /// Set to true via client_dir.cfg key "compose_render=1" or env "MH_COMPOSE_RENDER=1".
    ///
    /// This flag is the only gate between the old and new path; no code is removed while both
    /// paths coexist. spec: Docs/RE/specs/assembly_graph.md §1 — AreaComposer owns the assembled cell.
    /// </summary>
    private bool _composeRender;

    /// <summary>
    /// Assembled cell cache keyed by biased cell coordinates (MapX, MapZ).
    /// Populated when <see cref="_composeRender"/> is on via <see cref="OnCellAssembled"/>.
    /// Used by the composer-driven TextureResolver (A.2) and SlotRenderer (A.3).
    ///
    /// All mutations happen on the Godot main thread (inside <see cref="OnCellAssembled"/> which
    /// is called from <see cref="GameLoop.DispatchEvent"/> → <see cref="GameLoop._Process"/>).
    /// spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries 9 slots + ResolvedTexturePaths.
    /// </summary>
    private readonly Dictionary<(int MapX, int MapZ), global::MartialHeroes.Assets.Mapping.AssembledCell>
        _composedCells = new();

    /// <summary>
    /// Loaded texture cache for the composer path (A.2): maps VFS DDS path → Godot ImageTexture.
    /// All mutations on the main thread.
    /// </summary>
    private readonly Dictionary<string, ImageTexture?> _composerTexCache = new();

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    private RealClientAssets? _assets;
    private TerrainNode? _terrainNode;
    private ClientContext? _ctx;

    /// <summary>
    /// Read-only access to the already-open VFS handle so sibling presentation nodes (e.g. the in-world
    /// local-player avatar build) can resolve assets through the SAME handle instead of opening a second
    /// memory-mapped archive. Null when offline / real assets disabled. Main-thread only.
    /// spec: World/PlayerAvatarResolver.cs — local player skin/bind/idle resolution reuses this handle.
    /// </summary>
    public RealClientAssets? Assets => _assets;

    // Node-lifetime cancellation: cancelled in _ExitTree so the fire-and-forget streaming task
    // stops touching this node (and skips its completion prints) once the node leaves the tree.
    private readonly CancellationTokenSource _lifetimeCts = new();

    // Per-streaming-call cancellation: cancelled by TriggerTerrainStreaming before it launches
    // a new UpdateCenterAsync task. This prevents the OLD area's streaming task from continuing
    // to publish SectorLoadedEvents AFTER the source has been rebound to a new area.
    //
    // Combined with the ExpectedBakeAreaId guard in ClientContext.CellBake, this is the two-pronged
    // fix for the CYCLE 6 area-rebind streaming race (Lane D):
    //   (a) CancelAndResetStreamingCts()  — stops future SectorLoadedEvent publishes from the old task.
    //   (b) ctx.SetExpectedBakeArea(...)  — drops already-published stale events at drain time.
    //
    // _streamingCts is a LinkedTokenSource (linked to _lifetimeCts) so node tear-down (_ExitTree)
    // still cancels any in-flight streaming task via _lifetimeCts alone (no double-cancel path needed).
    // Main-thread only — never accessed from background tasks.
    // spec: Docs/RE/formats/terrain.md §12.2 (5×5 ring streaming). CONFIRMED.
    // spec: Docs/RE/specs/assembly_graph.md §1/§4 (area rebind before streaming starts). CONFIRMED.
    private CancellationTokenSource? _streamingCts;

    // Player controller reference: used to push TargetForCamera → CameraController each frame.
    private PlayerController? _playerController;

    // -------------------------------------------------------------------------
    // Player-following streaming state
    //
    // The streaming ring re-anchors when the player crosses a cell boundary, measured using
    // Chebyshev distance from the current anchor.  Hysteresis: recenter only when the player
    // is ≥ HysteresisThresholdCells cells away from the current anchor (avoids thrash when
    // the player stands on a cell boundary and oscillates between two cells).
    //
    // Eviction policy: SectorStreamingService.UpdateCenterAsync already calls EvictOutOfRange
    // which evicts all resident sectors with Chebyshev distance > 2 from the new anchor.
    // For a 5×5 ring (radius 2) this bounds the resident set to at most 25 sectors, with a
    // worst-case of 25 + up to (new ring - overlap) transient in-flight loads per recenter.
    // spec: Docs/RE/formats/terrain.md §9.3 — eviction radius 2 (Chebyshev > 2). CONFIRMED.
    // spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer thread follows the player: CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Recenter only when the player moves ≥ this many cells (Chebyshev) from the current anchor.
    /// Prevents thrash when the player walks along a cell boundary.
    /// spec: Docs/RE/specs/resource_pipeline.md §4.3 — streaming ring follows the player cell. CODE-CONFIRMED.
    /// </summary>
    private const int HysteresisThresholdCells = 1;

    // Current streaming anchor in biased cell coordinates (mapX, mapZ).
    // Initialised from the spawn-density peak in TriggerTerrainStreaming; updated each recenter.
    // spec: Docs/RE/formats/terrain.md §Overview — origin bias 10000, cell size 1024. CONFIRMED.
    private (int MapX, int MapZ) _streamAnchor;

    // True while a streaming update is in-flight (fire-and-forget Task still running).
    // Guards against overlapping recenter tasks that would double-load the same sectors.
    private volatile bool _isRecentering;

    // True once TriggerTerrainStreaming has set _streamAnchor (arm signal for _Process).
    private bool _followAnchorArmed;

    // Texture-resolution inputs, loaded once in Initialise after the target cell is resolved.
    // The confirmed two-hop chain is: cell/building 1-based index → the cell .map's per-section
    // TEXTURES[idx-1].intTexId (1-based pool slot) → bgtexture pool[intTexId-1] → data/map000/texture/<rel>.dds.
    // The runtime form is the BINARY bgtexture.lst (bgtexture.txt is absent from a real packed VFS);
    // the .txt mirror is only a dev/loose-tree fallback.
    // spec: Docs/RE/specs/asset_pipeline.md §3 chain B (runtime = bgtexture.lst; .txt absent) +
    //       Docs/RE/formats/bgtexture_lst.md §Cross-file join (.map intTexId-1 → .lst slot) +
    //       Docs/RE/formats/terrain.md §3.5 (.map TEXTURES) + §5.6 (render-domain idx). CONFIRMED.
    private global::MartialHeroes.Assets.Mapping.BgTextureCatalog? _bgTextures; // global pool: intTexId → relPath
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

        // Read the compose_render flag BEFORE any asset loading so derived paths can gate on it.
        // Default OFF: the legacy direct-from-VFS path runs unchanged (zero regression).
        _composeRender = ReadComposeRenderFlag();
        GD.Print($"[RealWorldRenderer] compose_render={_composeRender} " +
                 "(set compose_render=1 in client_dir.cfg or MH_COMPOSE_RENDER=1 to enable composer path).");

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

        // Initialise the cel-shading session: loads toonramp.bmp from the VFS and caches it.
        // All subsequent SkinnedCharacterBuilder / SkinnedCharacterNode builds pick up the ramp.
        // spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp 1-D N·L ramp, stage 1.
        // spec: Docs/RE/specs/rendering.md §5.2 — dotoonshading path = skinned character only.
        GD.Print("[RealWorldRenderer] Initialise: loading cel toon ramp");
        CelShadeMaterialFactory.InitSession(_assets);

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

        // Load the texture-resolution inputs once: the global bgtexture pool (runtime bgtexture.lst,
        // with the .txt mirror as dev/loose-tree fallback) and the cell's .map (per-section TEXTURES
        // lists). spec: asset_pipeline.md §3 chain B + terrain.md §3.5. CONFIRMED.
        LoadTextureResolutionInputs();

        // Atmosphere (EnvironmentNode): assemble the area's sky/fog/light from the parsed per-area
        // environment bins (map_option/fog/light/material). RECONCILED Campaign 5: map_option carries
        // no water field, so no map_option-driven water plane is placed (per-cell water presence is
        // detected separately from .map FX texture names).
        // spec: Docs/RE/specs/environment.md §3 (assembly) + §4 (water RESOLVED-NEGATIVE).
        GD.Print("[RealWorldRenderer] Initialise: wiring environment + water");
        try
        {
            WireEnvironmentAndWater();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] WireEnvironmentAndWater failed: {ex.Message}");
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

        // Kick off 5×5 terrain streaming via SectorStreamingService.
        // spec: Docs/RE/formats/terrain.md §12.2 (5×5 ring, StreamQuality.High).
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
            // FLAG-GATE (CYCLE 2 Phase 2-A): when compose_render is ON, buildings are placed from
            // CellAssembledEvent → OnCellAssembled → SlotRenderer.RenderSlot1Buildings for each
            // streamed cell. Suppress the legacy single-cell LoadAndSpawnBudScene to avoid double-
            // spawning the target cell's buildings (legacy once + compose once = 2×).
            // The legacy path runs unchanged when compose_render is OFF (zero regression).
            // spec: Docs/RE/specs/assembly_graph.md §1 — slot 1 buildings come from the assembled cell.
            if (_composeRender)
            {
                GD.Print("[RealWorldRenderer] Initialise: compose_render=ON — LoadAndSpawnBudScene " +
                         "SUPPRESSED (buildings placed per-cell from CellAssembledEvent via OnCellAssembled). " +
                         "spec: assembly_graph.md §1.");
            }
            else
            {
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
            }

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

            // Populate the area with monsters/NPCs from mob*.arr + npc*.arr (static characters,
            // resolved via the mob_id -> actormotion -> skin chain). Areas with no spawns are no-ops.
            //
            // FLAG-GATE (CYCLE 2 Phase 2-A.5): when compose_render is ON, actor placement is driven
            // by the AreaAssembledEvent → OnAreaAssembled composer path (see below). In that case,
            // NpcRenderer.PopulateFromArea is SUPPRESSED to avoid double-spawning the same actors.
            // When compose_render is OFF, the legacy NpcRenderer path runs unchanged (zero regression).
            // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — spawns reach layer-05 via event bus).
            if (_composeRender)
            {
                GD.Print("[RealWorldRenderer] Initialise: compose_render=ON — NpcRenderer.PopulateFromArea " +
                         "SUPPRESSED (actors placed from AreaAssembledEvent.Spawns via OnAreaAssembled). " +
                         "spec: assembly_graph.md §1/§4.");
            }
            else
            {
                GD.Print("[RealWorldRenderer] Initialise: NpcRenderer.PopulateFromArea start");
                try
                {
                    var npcRenderer = new NpcRenderer { Name = "NpcRenderer" };
                    // Sample terrain height (legacy worldX/worldZ); falls back to 26 until sectors load.
                    npcRenderer.GroundYFunc = (lx, lz) => _terrainNode?.GetGroundHeight(lx, lz, 26f) ?? 26f;
                    // TryGroundYFunc returns false (not just a fallback constant) when the sector is absent —
                    // used by the pending-snap mechanism to snap only when real data is available.
                    // spec: TerrainNode.TryGetGroundHeight — returns false when cell absent: CONFIRMED.
                    if (_terrainNode is not null)
                    {
                        TerrainNode terrainCapture = _terrainNode;
                        npcRenderer.TryGroundYFunc = (float lx, float lz, out float hy) =>
                            terrainCapture.TryGetGroundHeight(lx, lz, out hy);
                    }

                    AddChild(npcRenderer);
                    npcRenderer.PopulateFromArea(_assets, TargetAreaId);

                    // Wire the sector-resident notification so actors are re-grounded as soon as each
                    // cell's heightmap arrives — eliminates the fallback-Y race (D2).
                    // TerrainNode fires SectorBecameResident on the Godot main thread (GameLoop._Process)
                    // after the cell enters its height-lookup cache.
                    // spec: TerrainNode.SectorBecameResident — fired after _cellCache updated: CONFIRMED.
                    if (_terrainNode is not null)
                        _terrainNode.SectorBecameResident += npcRenderer.OnSectorBecameResident;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] NpcRenderer.PopulateFromArea failed: {ex.Message}");
                }

                GD.Print("[RealWorldRenderer] Initialise: NpcRenderer.PopulateFromArea done");
            }
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

    // -------------------------------------------------------------------------
    // Server-driven area cold-start (called from GameLoop on InGameWorldBootstrappedEvent)
    // -------------------------------------------------------------------------

    // Re-entrance guard: prevents concurrent OnWorldEntered calls (e.g. duplicate 4/1 packets).
    private volatile bool _worldEntryInProgress;

    /// <summary>
    /// Re-targets the renderer to <paramref name="areaId"/> and re-runs the area cold-start
    /// (texture resolver, environment, terrain streaming) for the new area.
    ///
    /// Called by <see cref="GameLoop"/> when <c>InGameWorldBootstrappedEvent</c> arrives (the 4/1
    /// world-entry carrier). This makes the area cold-start <b>server-driven</b>: the AreaId at
    /// 4/1 body offset 12 selects the on-disk area, and the config "area=" key becomes an
    /// OFFLINE-DEMO fallback only (Initialise still uses it when no live 4/1 supplies a different
    /// id — preserving the demo path exactly as before).
    ///
    /// Behaviour:
    /// <list type="bullet">
    ///   <item><see cref="IClientEvent">areaId</see> ≤ 0 → no-op (spec: 4/1 AreaId checked ≠ 0).
    ///     spec: Docs/RE/specs/world_entry.md §2.3.</item>
    ///   <item><c>_assets</c> null (offline, Initialise bailed) → log and return; the offline
    ///     demo area is preserved exactly as before.</item>
    ///   <item><c>areaId == TargetAreaId</c> AND already initialised → no-op (avoid double-init /
    ///     races; logs "already on area N").</item>
    ///   <item>Otherwise → retarget, re-resolve the spawn-density cell for the new area, re-run
    ///     texture resolver + environment + streaming for the new area. The camera is NOT
    ///     re-spawned (it is already in the scene tree from Initialise). Actor placement for the
    ///     new area's NPCs/mobs comes through the 4/4 ActorSpawnedEvent stream handled by
    ///     ActorRegistry — NpcRenderer is NOT re-run here.</item>
    /// </list>
    ///
    /// spec: Docs/RE/specs/world_entry.md §2.3 — 4/1 reads AreaId, cold-starts the area by its
    ///        3-digit decimal directory; area cold-start happens INSIDE the 4/1 handler.
    /// spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → zero-padded 3-digit dir → &lt;id&gt;.lst.
    /// </summary>
    public void OnWorldEntered(int areaId, MartialHeroes.Shared.Kernel.Numerics.Vector3Fixed position)
    {
        // spec: Docs/RE/specs/world_entry.md §2.3 — AreaId is checked != 0 before use.
        if (areaId <= 0)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: areaId={areaId} ≤ 0 — no-op. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3.");
            return;
        }

        if (_assets is null)
        {
            // Offline path (Initialise bailed with no VFS): the offline demo area is preserved.
            // spec: Docs/RE/specs/world_entry.md §2.3 (server AreaId drives cold-start; config is fallback).
            GD.Print($"[RealWorldRenderer] OnWorldEntered: areaId={areaId} — no real assets (offline demo); " +
                     "offline demo area kept. spec: Docs/RE/specs/world_entry.md §2.3.");
            return;
        }

        if (areaId == TargetAreaId && _followAnchorArmed)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: already on area {areaId} — no-op.");
            return;
        }

        if (_worldEntryInProgress)
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: area={areaId} — re-entry while in-progress; skipped.");
            return;
        }

        _worldEntryInProgress = true;
        try
        {
            GD.Print($"[RealWorldRenderer] OnWorldEntered: retargeting to server area {areaId}. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3/§3.1.");

            // Override TargetAreaId with the server-supplied value BEFORE re-resolving the cell,
            // so ResolveTargetCell reads the correct area instead of the config area.
            // spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → 3-digit dir → cold-start.
            TargetAreaId = areaId;

            // Derive the streaming anchor from the 4/1 spawn position.
            // spec: Docs/RE/specs/world_entry.md §2.3 — SpawnX/SpawnZ read from the 4/1 body;
            //       §3.1 — first-ring load CENTRED ON THE SPAWN POSITION from the same packet.
            //
            // Convention: position is a Vector3Fixed in LEGACY WORLD SPACE (not Godot-space).
            // ToVector3Float() gives raw legacy floats (x, y, z); SectorGrid.WorldToSector expects
            // legacy XZ directly — NO Z-negation here (the negate-Z convention only applies when
            // converting TO Godot-space for node placement; spec: Helpers/WorldCoordinates.ToGodot).
            // spec: Docs/RE/formats/terrain.md §1.4 — cell = floor(coord/1024) + 10000. CONFIRMED.
            bool spawnCellApplied = false;
            try
            {
                var (spawnLegacyX, _, spawnLegacyZ) = position.ToVector3Float();

                // Compute the biased cell from the server spawn position.
                // Reuses the same formula ResolveTargetCell / _Process streaming use for consistency.
                // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu.
                (int spawnCellX, int spawnCellZ) = SectorGrid.WorldToSector(spawnLegacyX, spawnLegacyZ);
                // spec: Docs/RE/specs/world_entry.md §2.3/§3.1 — spawn position seeds terrain anchor.
                // spec: Docs/RE/formats/terrain.md §1.4 — cell = floor(coord/1024)+10000. CONFIRMED.

                // Prefer the spawn-derived cell only when it is a valid cell for the area on the VFS.
                // Fall back to ResolveTargetCellForServerArea's VFS-enumeration pick otherwise.
                List<(int MapX, int MapZ)>? areaCells = null;
                if (_assets is not null)
                    areaCells = _assets.EnumerateTerrainCells(areaId);

                bool spawnCellValid = areaCells is not null &&
                                      areaCells.Any(c => c.MapX == spawnCellX && c.MapZ == spawnCellZ);

                if (spawnCellValid)
                {
                    TargetMapX = spawnCellX;
                    TargetMapZ = spawnCellZ;
                    spawnCellApplied = true;
                    GD.Print(
                        $"[RealWorldRenderer] OnWorldEntered area={areaId} spawn-cell=({TargetMapX},{TargetMapZ}) " +
                        $"from 4/1 spawn (legacy XZ=({spawnLegacyX:F0},{spawnLegacyZ:F0})). " +
                        "spec: world_entry.md §2.3/§3.1.");
                }
                else
                {
                    GD.Print($"[RealWorldRenderer] OnWorldEntered: spawn cell ({spawnCellX},{spawnCellZ}) " +
                             $"not in VFS for area {areaId} (legacy XZ=({spawnLegacyX:F0},{spawnLegacyZ:F0})) " +
                             "— falling back to VFS-enumeration anchor. " +
                             "spec: world_entry.md §2.3/§3.1.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: spawn-cell derivation failed: {ex.Message} " +
                            "— falling back to VFS-enumeration anchor.");
            }

            // Re-resolve the spawn-density cell for the new area (fallback when spawn cell is absent).
            // When the spawn cell was valid and already applied above, this is still called to
            // confirm/refresh the area's cell list state, but TargetMapX/Z has already been set.
            // spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → 3-digit dir; on-disk area must have cells.
            if (!spawnCellApplied)
            {
                try
                {
                    ResolveTargetCellForServerArea(areaId);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: ResolveTargetCellForServerArea failed: " +
                                $"{ex.Message} — keeping defaults ({TargetMapX},{TargetMapZ}).");
                }
            }

            GD.Print($"[RealWorldRenderer] OnWorldEntered: area={areaId} cell=({TargetMapX},{TargetMapZ}).");

            // Re-load texture-resolution inputs for the new area.
            // spec: Docs/RE/specs/asset_pipeline.md §3 chain B (runtime = bgtexture.lst).
            try
            {
                LoadTextureResolutionInputs();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: LoadTextureResolutionInputs failed: {ex.Message}");
            }

            // Re-wire the environment (sky/fog/light) for the new area.
            // spec: Docs/RE/specs/environment.md §3 (assembly).
            try
            {
                WireEnvironmentAndWater();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: WireEnvironmentAndWater failed: {ex.Message}");
            }

            // Re-wire terrain texture resolver for the new area.
            // spec: Docs/RE/formats/terrain.md §5.6 — 1-based TextureIndexGrid → texture path.
            if (_terrainNode is not null)
            {
                try
                {
                    WireTerrainTextureResolver(_terrainNode);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: WireTerrainTextureResolver failed: {ex.Message}");
                }
            }

            // Kick off terrain streaming centred on the new area's spawn-density cell.
            // spec: Docs/RE/formats/terrain.md §12.2 (5×5 ring streaming).
            // spec: Docs/RE/specs/world_entry.md §3.1 — terrain first-ring load centred on spawn.
            if (_ctx is not null)
            {
                try
                {
                    TriggerTerrainStreaming(_ctx);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[RealWorldRenderer] OnWorldEntered: TriggerTerrainStreaming failed: {ex.Message}");
                }
            }
            else
            {
                GD.PrintErr("[RealWorldRenderer] OnWorldEntered: _ctx is null — terrain streaming not triggered.");
            }

            GD.Print($"[RealWorldRenderer] OnWorldEntered: cold-start complete for area {areaId}. " +
                     "spec: Docs/RE/specs/world_entry.md §2.3.");
        }
        finally
        {
            _worldEntryInProgress = false;
        }
    }

    /// <summary>
    /// Like <see cref="ResolveTargetCell"/> but uses the given <paramref name="serverAreaId"/>
    /// directly instead of reading "area=" from config. Called by <see cref="OnWorldEntered"/>
    /// so the server-supplied AreaId drives cell discovery rather than the config fallback.
    ///
    /// Resolution:
    ///   1. Enumerate .ted entries for <paramref name="serverAreaId"/>.
    ///   2. If cells found, compute spawn-density anchor + pick ring centre (same as Initialise).
    ///   3. If no cells for that area, fall back to terrain centroid or default coordinates.
    ///
    /// spec: Docs/RE/specs/world_entry.md §3.1 — AreaId → 3-digit dir; on-disk area must have cells.
    /// spec: Docs/RE/formats/terrain.md §1.3 — per-cell path pattern. CONFIRMED.
    /// </summary>
    private void ResolveTargetCellForServerArea(int serverAreaId)
    {
        if (_assets is null) return;

        int ringRadius = ReadRingRadiusFromConfig();
        List<(int MapX, int MapZ)> cells = _assets.EnumerateTerrainCells(serverAreaId);
        if (cells.Count > 0)
        {
            const int DensityRadius = 1; // always 3×3 neighbourhood for anchor detection.
            (double anchorX, double anchorZ) = ComputeSpawnAnchor(_assets, serverAreaId, cells, DensityRadius);
            (int mx, int mz, bool fullRing) = PickRingCenter(cells, anchorX, anchorZ, ringRadius);
            TargetAreaId = serverAreaId;
            TargetMapX = mx;
            TargetMapZ = mz;
            GD.Print($"[RealWorldRenderer] Server area {serverAreaId}: {cells.Count} cells → " +
                     $"cell ({TargetMapX},{TargetMapZ}) " +
                     $"(anchor=({anchorX:F1},{anchorZ:F1}), fullRing={fullRing}). " +
                     "spec: Docs/RE/specs/world_entry.md §3.1.");
        }
        else
        {
            // No cells on disk for this area — keep whatever Initialise resolved (or defaults).
            // spec: Docs/RE/formats/terrain.md §12.3 — absent cells load empty without crash. CONFIRMED.
            GD.PrintErr($"[RealWorldRenderer] Server area {serverAreaId} has no .ted cells in VFS. " +
                        $"Keeping previous cell ({TargetMapX},{TargetMapZ}). " +
                        "spec: Docs/RE/specs/world_entry.md §3.1.");
        }
    }

    public override void _ExitTree()
    {
        // Cancel the per-streaming-call CTS first (stops any in-flight streaming task immediately)
        // then cancel the node-lifetime CTS (belt+suspenders). Both cancellations are safe to
        // issue before Dispose.
        // spec: Docs/RE/formats/terrain.md §12.3 (node exit cancels the streaming task).
        try { _streamingCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { _streamingCts?.Dispose(); } catch (ObjectDisposedException) { }
        _streamingCts = null;

        // Cancel the node-lifetime token so any background task still referencing _lifetimeCts stops.
        try
        {
            _lifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed — nothing to do.
        }

        _lifetimeCts.Dispose();

        _assets?.Dispose();
        _assets = null;
    }

    /// <summary>
    /// Each frame:
    ///   1. Push the player avatar's confirmed Godot position to the CameraController
    ///      so the Third-person orbit always follows the character.
    ///   2. Check whether the player has crossed a cell boundary. If the Chebyshev distance
    ///      from the current streaming anchor to the player's current cell is ≥
    ///      <see cref="HysteresisThresholdCells"/>, and no recenter is already in-flight,
    ///      kick off an async <see cref="SectorStreamingService.UpdateCenterAsync"/> call with
    ///      the player's cell as the new anchor. This is the player-following streaming loop:
    ///      as the player walks south the southern cells load and the northern cells are evicted.
    ///
    /// Coordinate conversion (Godot → legacy world):
    ///   The player's Godot position has Z negated relative to legacy world Z.
    ///   legacyX = godotX  (X is unchanged)
    ///   legacyZ = -godotZ  (negate back; spec: WorldCoordinates.ToGodot negate Z. CONFIRMED.)
    ///   Cell: SectorGrid.WorldToSector(legacyX, legacyZ)
    ///   spec: Docs/RE/formats/terrain.md §2 — cell key formula. CONFIRMED.
    ///   spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer follows the player cell. CODE-CONFIRMED.
    ///
    /// Eviction: SectorStreamingService.UpdateCenterAsync → EvictOutOfRange evicts all resident
    ///   sectors with Chebyshev distance > 2 from the new anchor automatically.
    ///   spec: Docs/RE/formats/terrain.md §9.3 — eviction radius 2. CONFIRMED.
    ///
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "strictly passive rendering".
    /// </summary>
    public override void _Process(double delta)
    {
        if (_playerController is not null && _cameraController is not null)
            _cameraController.PlayerGodotPosition = _playerController.TargetForCamera;

        // ---- Player-following streaming ----
        // Only runs when the follow anchor is armed (TriggerTerrainStreaming succeeded) and a
        // ClientContext + player controller are available. No game logic here — pure presentation.
        if (!_followAnchorArmed || _ctx is null || _playerController is null)
            return;

        // Read the player's Godot-space position (X unchanged, Z negated relative to legacy).
        // spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z). CONFIRMED.
        Vector3 godotPos = _playerController.TargetForCamera;
        float legacyX = godotPos.X;
        float legacyZ = -godotPos.Z; // negate back to legacy world Z

        // Map to the biased (mapX, mapZ) cell coordinate.
        // spec: Docs/RE/formats/terrain.md §2 — cell key formula: CONFIRMED.
        (int playerMapX, int playerMapZ) = SectorGrid.WorldToSector(legacyX, legacyZ);

        // Compute Chebyshev distance from the current streaming anchor.
        int distX = Math.Abs(playerMapX - _streamAnchor.MapX);
        int distZ = Math.Abs(playerMapZ - _streamAnchor.MapZ);
        int chebyshev = Math.Max(distX, distZ);

        // Hysteresis: only recenter when the player is ≥ HysteresisThresholdCells away and
        // no async recenter is currently in-flight.
        // spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer thread follows the player. CODE-CONFIRMED.
        if (chebyshev < HysteresisThresholdCells || _isRecentering)
            return;

        // Capture locals for the closure (avoid capturing 'this' state that changes each frame).
        int newAnchorX = playerMapX;
        int newAnchorZ = playerMapZ;
        (int OldX, int OldZ) oldAnchor = _streamAnchor;

        // Update the anchor BEFORE launching the task so subsequent frames see the new anchor
        // and do not queue another recenter for the same cell.
        _streamAnchor = (newAnchorX, newAnchorZ);
        _isRecentering = true;

        GD.Print($"[RealWorldRenderer] StreamFollow: recenter anchor " +
                 $"({oldAnchor.OldX},{oldAnchor.OldZ}) → ({newAnchorX},{newAnchorZ}) " +
                 $"(Chebyshev={chebyshev}, player legacyXZ=({legacyX:F0},{legacyZ:F0})).");

        CancellationToken lifetime = _lifetimeCts.Token;
        ClientContext ctxCapture = _ctx;

        _ = Task.Run(async () =>
        {
            try
            {
                await ctxCapture.StreamingService.UpdateCenterAsync(newAnchorX, newAnchorZ, lifetime)
                    .ConfigureAwait(false);

                if (lifetime.IsCancellationRequested) return;

                int resident = ctxCapture.StreamingService.ResidentCount;
                GD.Print($"[RealWorldRenderer] StreamFollow: recenter to ({newAnchorX},{newAnchorZ}) " +
                         $"complete — resident={resident} sectors.");
            }
            catch (OperationCanceledException)
            {
                // Expected on node exit — silent.
            }
            catch (Exception ex)
            {
                if (!lifetime.IsCancellationRequested)
                    GD.PrintErr($"[RealWorldRenderer] StreamFollow: UpdateCenterAsync error: {ex.Message}");
            }
            finally
            {
                // Always clear the in-flight flag so the next cell crossing can trigger a recenter.
                _isRecentering = false;
            }
        }, lifetime);
    }

    // -------------------------------------------------------------------------
    // Environment + water wiring
    // -------------------------------------------------------------------------

    /// <summary>
    /// Assembles the area's environment (sky/fog/light) into a <see cref="EnvironmentNode"/>.
    ///
    /// The environment is read+parsed from the per-area bins via
    /// <see cref="VfsEnvironmentSource"/> (the same VFS-read+parse adapter pattern as terrain).
    /// Water rendering visuals are a free engineering choice — the legacy client has no water
    /// renderer (RESOLVED-NEGATIVE). RECONCILED Campaign 5: <c>map_option%d.bin</c> carries NO water
    /// field, so the map_option water path (<see cref="WaterPlacement.FromMapOption"/>) is always
    /// disabled; per-cell water presence is detected separately from <c>.map</c> FX texture names.
    ///
    /// spec: Docs/RE/specs/environment.md §3 (assembly) + §4 (water RESOLVED-NEGATIVE).
    /// spec: Docs/RE/formats/environment_bins.md §1.1 (NO water fields in map_option).
    /// </summary>
    private void WireEnvironmentAndWater()
    {
        if (_assets is null) return;

        // ---- Environment (D3) ----
        // Resolve the World scene's OWN WorldEnvironment + DirectionalLight3D (defined in World.tscn)
        // and pass them explicitly into EnvironmentNode so it drives them in place instead of
        // creating duplicates. This renderer is a direct child of the World scene root (GameLoop),
        // so the scene's env/light are our SIBLINGS — found via our parent. Under boot_flow=login the
        // scene tree is /root → Boot → World → RealWorldRenderer, and a generic top-of-tree walk lands
        // on Boot (whose direct children exclude these) → that was the duplicate-sun bug.
        Node worldSceneRoot = GetParent();
        WorldEnvironment? sceneWorldEnv = FindDirectChildOfType<WorldEnvironment>(worldSceneRoot);
        DirectionalLight3D? sceneDirLight = FindDirectChildOfType<DirectionalLight3D>(worldSceneRoot);
        GD.Print($"[RealWorldRenderer] Scene env nodes under '{worldSceneRoot.Name}': " +
                 $"WorldEnvironment={(sceneWorldEnv is not null)} DirectionalLight3D={(sceneDirLight is not null)}.");

        var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
        AddChild(envNode);
        envNode.Configure(_assets, TargetAreaId, sceneWorldEnv, sceneDirLight);

        // ---- Water (D4) ----
        // RECONCILED Campaign 5: map_option has NO water field, so this path is always disabled.
        // Per-cell water presence is detected from the .map FX texture names (CellHasWater), not here.
        // spec: Docs/RE/formats/environment_bins.md §1.1 (no water fields) + §1.4 (RESOLVED-NEGATIVE).
        AreaEnvironment env = VfsEnvironmentSource.Load(_assets, TargetAreaId);
        WaterPlacement water = WaterPlacement.FromMapOption(env.MapOption);

        if (!water.Enabled)
        {
            // spec: environment_bins.md §1.1 — map_option carries no water plane (always the case).
            GD.Print($"[Water] area={TargetAreaId} no map_option-driven water plane — checking per-cell FX textures.");
        }

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

        bool hasWater = WaterRenderer.CellHasWater(_cellMap, _bgTextures);
        GD.Print($"[Water] area={TargetAreaId} per-cell FX detection: hasWater={hasWater}.");

        if (!hasWater)
        {
            GD.Print($"[Water] area={TargetAreaId} cell has no water FX textures — no water plane.");
            return;
        }

        // Centre the plane on the resolved cell, sized to the loaded streaming ring so it covers
        // the visible terrain. Ring radius cells × 1024 wu, +1 cell of slop for the borders.
        // spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 wu. CONFIRMED.
        int ringRadius = ReadRingRadiusFromConfig();
        float ringCells = 2 * ringRadius + 1 + 1f; // (2r+1) ring + 1 cell slop
        float size = ringCells * 1024f;

        float centreX = (TargetMapX - 10000) * 1024f + 512f;
        float centreZ = (TargetMapZ - 10000) * 1024f + 512f;
        float waterY = WaterRenderer.WaterSurfaceY(_cellMap);
        // spec: WorldCoordinates.ToGodot — negate Z; Y is the data-driven water_y (unchanged).
        var centre = new Vector3(centreX, waterY, -centreZ);

        var waterNode = new WaterRenderer { Name = "WaterRenderer" };
        AddChild(waterNode);
        waterNode.Configure(centre, size, waterY);

        GD.Print($"[Water] cell has water → plane configured at Y={waterY:F1} " +
                 $"area={TargetAreaId} size={size:F0}u centre=({centre.X:F0},{centre.Y:F1},{centre.Z:F0}).");
    }

    /// <summary>First direct child of <paramref name="parent"/> assignable to T, or null.</summary>
    private static T? FindDirectChildOfType<T>(Node parent) where T : Node
    {
        foreach (Node child in parent.GetChildren())
            if (child is T match)
                return match;
        return null;
    }

    // -------------------------------------------------------------------------
    // Terrain texture resolver wiring
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads the inputs for the two-hop terrain/building texture resolution: the global
    /// background-texture pool and the target cell's <c>.map</c> descriptor.
    ///
    /// The pool is loaded from the BINARY <c>data/map000/texture/bgtexture.lst</c> — the runtime
    /// form the original client actually consumes — via <see cref="global::MartialHeroes.Assets.Mapping.BgTextureCatalog.FromLst"/>.
    /// The human-readable <c>bgtexture.txt</c> mirror is ABSENT from a real packed <c>data.vfs</c>,
    /// so it is used only as a dev / loose-tree fallback when the <c>.lst</c> is missing. The pool +
    /// the texture <c>.dds</c> are GLOBAL under <c>map000</c> for ALL areas (there is no per-area pool).
    /// spec: Docs/RE/specs/asset_pipeline.md §3 chain B — runtime opens <c>bgtexture.lst</c>;
    ///       <c>bgtexture.txt</c> absent from the image (the <c>.lst</c> wins). CONFIRMED.
    /// spec: Docs/RE/formats/bgtexture_lst.md — u32 count, 48-byte records → index-keyed pool. CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §3.5 (.map TEXTURES). CONFIRMED.
    /// </summary>
    private void LoadTextureResolutionInputs()
    {
        if (_assets is null) return;
        string tag = AreaTag(TargetAreaId);

        try
        {
            // Runtime form FIRST: the binary bgtexture.lst. spec: asset_pipeline.md §3 chain B —
            // the loader opens data/map000/texture/bgtexture.lst (global map000 pool). CONFIRMED.
            const string lstPath = "data/map000/texture/bgtexture.lst";
            const string txtPath = "data/map000/texture/bgtexture.txt";

            if (_assets.Contains(lstPath))
            {
                // spec: Docs/RE/formats/bgtexture_lst.md — binary index-keyed pool. CONFIRMED.
                _bgTextures = global::MartialHeroes.Assets.Mapping.BgTextureCatalog.FromLst(_assets.GetRaw(lstPath));
                GD.Print(
                    $"[RealWorldRenderer] bgtexture pool loaded from bgtexture.lst: {_bgTextures.SlotCount} slots.");
            }
            else if (_assets.Contains(txtPath))
            {
                // Dev / loose-tree fallback only: the .txt mirror is absent from a real packed VFS.
                // spec: Docs/RE/specs/asset_pipeline.md §3 chain B — .txt is the human-readable mirror,
                //       used only when the runtime .lst is missing. CONFIRMED.
                _bgTextures = global::MartialHeroes.Assets.Mapping.BgTextureCatalog.FromTxt(_assets.GetRaw(txtPath));
                GD.Print(
                    $"[RealWorldRenderer] bgtexture.lst absent — fell back to bgtexture.txt mirror: {_bgTextures.SlotCount} slots.");
            }
            else
            {
                GD.Print(
                    $"[RealWorldRenderer] bgtexture.lst absent ({lstPath}) and no .txt mirror — terrain/buildings stay untextured.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] bgtexture pool load failed: {ex.Message}");
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
    /// Resolves a 1-based <c>.ted</c> per-cell byte (from <see cref="TerrainNode"/>) or a BUD object to
    /// a Godot <see cref="ImageTexture"/> via the IDA-confirmed chain (263bd994): the byte is clamped to
    /// <c>[1, count]</c> (both <c>&lt;1</c> and <c>&gt;count</c> → 1), then <c>TEXTURES[byte-1].intTexId</c>
    /// (the ONLY <c>-1</c>, on the byte — IDA 0x44b296), then the <c>bgtexture.lst</c> pool indexed
    /// DIRECTLY by <c>intTexId</c> (0-based, NO <c>-1</c> — IDA 0x445833 / 0x44a46d) →
    /// <c>data/map000/texture/&lt;rel&gt;.dds</c>.
    /// spec: Docs/RE/formats/terrain.md §3.5 + §5.6; Docs/RE/formats/bgtexture_lst.md §Cross-file join. CONFIRMED.
    /// </summary>
    /// <param name="sectionKeyword">The .map section to read the TEXTURES list from (e.g. "TERRAIN", "BUILDING").</param>
    /// <param name="oneBasedIndex">The 1-based index into that section's TEXTURES list.</param>
    private ImageTexture? ResolveSectionTexture(string sectionKeyword, int oneBasedIndex)
    {
        if (_assets is null || _bgTextures is null || _cellMap is null) return null;

        (int Flag, int TexId)[]? list = GetSectionTextures(sectionKeyword);
        if (list is null || list.Length == 0) return null;

        // The .ted per-cell byte is 1-based into the cell texture list and clamped to [1, count]:
        // BOTH a byte < 1 and a byte > count resolve to slot 1 (perCellTexList[0]) — there is NO
        // no-texture sentinel. This clamp + the byte-1 below are the ONE legitimate -1 in the chain
        // (it is on the .ted byte, NOT on the intTexId).
        // spec: Docs/RE/formats/terrain.md §5.6 — Ted_ResolvePatchTextures (IDA 0x44b296: <1→1, >count→1).
        int b = (oneBasedIndex < 1 || oneBasedIndex > list.Length) ? 1 : oneBasedIndex;
        int intTexId = list[b - 1].TexId;

        // The .map intTexId is the 0-based bgtexture pool slot, used DIRECTLY (NO -1): the pool accessor
        // reads pool[0]+stride*intTexId. spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join
        // (IDA-corrected 263bd994: 0x445833 / 0x44a46d / store 0x44b267). Textures live under the GLOBAL
        // map000 pool for all areas. spec: terrain.md §3.5. CONFIRMED.
        string? ddsPath = _bgTextures.ResolveTexturePath(intTexId);
        if (ddsPath is null) return null;

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
    /// Calls <see cref="SectorStreamingService.UpdateCenterAsync"/> for the initial spawn-anchor
    /// cell and arms the player-following streaming loop.
    ///
    /// Boot behaviour: the initial anchor is the spawn-density peak (already resolved by
    /// <see cref="ResolveTargetCell"/> before this method is called). This is unchanged from the
    /// pre-follow behaviour.
    ///
    /// Follow behaviour: after boot, <see cref="_Process"/> checks the player position each frame
    /// and recenter the streaming ring whenever the player moves ≥ <see cref="HysteresisThresholdCells"/>
    /// cells (Chebyshev) from <see cref="_streamAnchor"/>. Each recenter calls
    /// <see cref="SectorStreamingService.UpdateCenterAsync"/> which also evicts sectors that drifted
    /// more than 2 cells from the new anchor (Chebyshev eviction).
    ///
    /// spec: Docs/RE/formats/terrain.md §12.2 — "5×5 ring (High quality) of sectors centred on the player cell". CONFIRMED.
    /// spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer thread follows the player: CODE-CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §9.3 — eviction at Chebyshev distance > 2: CONFIRMED.
    /// </summary>
    private void TriggerTerrainStreaming(ClientContext ctx)
    {
        // ── CYCLE 6 Lane D: area-rebind streaming race fix ──────────────────────
        //
        // Cancel the PREVIOUS streaming task before rebinding the sources.
        //
        // Problem: Initialise fires a Task.Run(UpdateCenterAsync(configArea)) then immediately
        // returns. OnWorldEntered then calls TriggerTerrainStreaming again for the serverArea.
        // The OLD task is still running on the thread pool; it has already published (or will
        // publish) SectorLoadedEvents for configArea cells into the ClientEventBus channel.
        // When _Process drains those events, areaSource.AreaId == serverArea → ComposeCell builds
        // data/map<serverArea>/dat/d<serverArea>x<configArea_mapX>z<configArea_mapZ>... (missing)
        // → empty cell → IsResolved=false → the ~36% cell miss.
        //
        // Fix part (a): cancel the old streaming task so it stops publishing NEW SectorLoadedEvents.
        //   - _streamingCts is a LinkedTokenSource: cancelling it also cancels any UpdateCenterAsync
        //     call using that token; the background thread-pool thread exits cleanly via
        //     OperationCanceledException from _source.LoadSectorAsync.
        //   - Already-published events may still be in the channel (see fix part b below).
        //
        // spec: Docs/RE/formats/terrain.md §12.2 (streaming ring). CONFIRMED.
        // spec: Docs/RE/specs/assembly_graph.md §1/§4 (area rebind before streaming starts). CONFIRMED.
        if (_streamingCts is not null)
        {
            try { _streamingCts.Cancel(); } catch (ObjectDisposedException) { }
            try { _streamingCts.Dispose(); } catch (ObjectDisposedException) { }
            _streamingCts = null;
            GD.Print($"[RealWorldRenderer] TriggerTerrainStreaming: previous streaming task cancelled " +
                     $"(area rebind to {TargetAreaId}). spec: assembly_graph.md §1 (race fix lane D).");
        }

        // Create a new per-call CTS linked to _lifetimeCts.
        // This token is passed to UpdateCenterAsync AND the player-follow recenter tasks.
        // Cancelling _streamingCts stops THIS area's streaming; cancelling _lifetimeCts (_ExitTree)
        // stops ALL streaming regardless of which CTS the task holds.
        _streamingCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        CancellationToken streamingToken = _streamingCts.Token;

        // Point BOTH streaming sources at the resolved area BEFORE streaming starts:
        //   (a) StreamingService.SetArea — rebinds VfsTerrainSectorSource to the correct .ted paths.
        //   (b) AreaAssemblySource.SetArea — rebinds RebindableAreaAssemblySource so the
        //       CellAssemblyHandoff bake lambda calls AreaComposer.ComposeCell with the correct
        //       data/map<NNN>/dat/... paths (Phase 2-B.1 fix: was hard-coded to area 0 → .map open
        //       failed for every non-zero area → ComposeCell early-exit → zero geometry).
        // Both must be rebound before UpdateCenterAsync fires SectorLoadedEvent callbacks.
        // spec: Docs/RE/formats/terrain.md §1.1 (per-area path tag) + §1.2 (per-area manifest).
        // spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives AreaComposer paths.
        ctx.StreamingService.SetArea(TargetAreaId);
        ctx.AreaAssemblySource?.SetArea(TargetAreaId);

        // ── CYCLE 6 Lane D: area-rebind streaming race fix part (b) ────────────
        //
        // Stamp ExpectedBakeAreaId so the CellBake lambda drops stale events that were already
        // published into the ClientEventBus by the OLD streaming task before we cancelled it.
        //
        // Events already in the channel (produced by the configArea task) will be drained in _Process
        // AFTER this rebind. At drain time areaSource.AreaId == serverArea (rebound just above).
        // Without the guard, ComposeCell would use the server-area source with configArea coords →
        // path miss. The guard reads ExpectedBakeAreaId vs areaSource.AreaId at drain time and
        // drops any event where they differ — making the race observable only as a "stale event
        // dropped" log line rather than an empty-cell silently written into the _composedCells cache.
        //
        // spec: Docs/RE/specs/assembly_graph.md §1/§4 (IAreaAssemblySource drives AreaComposer paths).
        // spec: Docs/RE/formats/area_inventory.md §1A (membership gate before any cell streams).
        ctx.SetExpectedBakeArea(TargetAreaId);

        // CYCLE 2 Phase 2-A.5: compose + publish the area ONCE per area-enter.
        // OnAreaBound is idempotent (no-op if called again with the same area id).
        // This closes the gap where AreaAssembledEvent was never published and
        // RealWorldRenderer.OnAreaAssembled never fired.
        // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4.
        ctx.AreaAssemblyHandoff?.OnAreaBound(TargetAreaId);

        // Initialise the streaming anchor to the resolved spawn-density peak.
        // _Process will update this as the player moves.
        // spec: Docs/RE/formats/terrain.md §Overview — origin bias 10000, cell size 1024. CONFIRMED.
        _streamAnchor = (TargetMapX, TargetMapZ);

        // Launch the initial ring load on the thread pool using the per-call token.
        // spec: Docs/RE/formats/terrain.md §12.2 — 5×5 ring (StreamQuality.High). CONFIRMED.
        _ = Task.Run(async () =>
        {
            try
            {
                await ctx.StreamingService.UpdateCenterAsync(TargetMapX, TargetMapZ, streamingToken)
                    .ConfigureAwait(false);

                // Skip the completion print if this streaming session was cancelled (superseded
                // by a newer TriggerTerrainStreaming call) or the node left the tree.
                if (streamingToken.IsCancellationRequested) return;

                int residentCount = ctx.StreamingService.ResidentCount;
                GD.Print($"[RealWorldRenderer] 5×5 terrain ring streaming complete " +
                         $"(area {TargetAreaId}, resident={residentCount} sectors).");
            }
            catch (OperationCanceledException)
            {
                // Expected: (a) node left the tree (_lifetimeCts); (b) area was superseded
                // (TriggerTerrainStreaming cancelled _streamingCts). Silent.
            }
            catch (Exception ex)
            {
                if (!streamingToken.IsCancellationRequested)
                    GD.PrintErr($"[RealWorldRenderer] Terrain streaming error: {ex.Message}");
            }
        }, streamingToken);

        // Arm the follow-streaming loop (enables the _Process recenter checks).
        // Done AFTER _streamAnchor is set so _Process never sees a zero anchor.
        // spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer thread follows the player. CODE-CONFIRMED.
        _followAnchorArmed = true;

        GD.Print($"[RealWorldRenderer] Terrain streaming requested for centre ({TargetMapX},{TargetMapZ}). " +
                 $"[StreamFollow] Player-following terrain streaming ARMED — anchor=({TargetMapX},{TargetMapZ}), " +
                 $"hysteresis={HysteresisThresholdCells} cell(s). " +
                 $"As the player moves, the ring will recenter and the 17 south NPCs will ground " +
                 $"via the pending-snap mechanism as their sectors stream in.");
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
            // Render the SKINNED + animated character via faithful CPU linear-blend skinning
            // (SkinnedCharacterNode). The legacy bind/inverse-bind/LBS pipeline is now recovered
            // (Docs/RE/specs/skinning.md), so the mesh deforms correctly and the idle .mot plays.
            // A single unified handedness conversion (world Z-negate) is applied to bones+verts+
            // keyframes, preserving the rest-pose cancellation that previously exploded the mesh.
            // spec: Docs/RE/specs/skinning.md §0 (cancellation), §8(b) (single conversion).
            SkinnedCharacterBuilder.ForceSkinned = true;
            charRoot = SkinnedCharacterBuilder.Build(skinnedMesh, skeleton, clip, albedo: albedo);
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

        // The mesh is rendered SKINNED + animated (CPU LBS) in the single unified handedness
        // conversion; no corrective rotation is applied (the conversion handles handedness).
        // spec: Docs/RE/specs/skinning.md §8(b).
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
            // Follow the terrain each frame: convert the avatar's Godot position to legacy world XZ
            // (worldZ = -godotZ) and sample the heightmap. Falls back to 26 until sectors stream in.
            playerController.GroundHeightFunc = gp => _terrainNode?.GetGroundHeight(gp.X, -gp.Z, 26f) ?? 26f;
            // Store reference so _Process can push TargetForCamera → CameraController each frame.
            _playerController = playerController;
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

            AnimationClip? clip = AnimationParser.Parse(data); // CS8600: Parse returns nullable
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
    /// the idle-peace motion id is column 15 (0-based), record offset +0x40.
    /// IDB-confirmed operand-for-operand. Any read of cols[16] for idle is an off-by-one.
    /// spec: Docs/RE/formats/actormotion.md — col15 (0-based) = idle motion id @ +0x40: IDB-CONFIRMED.
    /// spec: Docs/RE/formats/animation.md — .mot idle resolution.
    /// Returns <c>data/char/mot/g{id}.mot</c> or null.
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
            // Need at least 16 columns (indices 0..15) for the idle field at col15.
            // spec: Docs/RE/formats/actormotion.md — col15 = idle motion id (0-based); IDB-CONFIRMED.
            if (cols.Length <= 15) continue;
            if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != actorClassId) continue;

            // col15 (0-based) = motion_ids_a[0] = idle-peace motion id @ record offset +0x40.
            // spec: Docs/RE/formats/actormotion.md — col15=+0x40=idle: IDB-CONFIRMED.
            string idle = cols[15].Trim();
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
    // CYCLE 2 Phase 2-A — composer-driven event handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by <see cref="GameLoop.DispatchEvent"/> on the main thread each frame when a
    /// <see cref="CellAssembledEvent"/> arrives. When <see cref="_composeRender"/> is off this
    /// is a no-op (zero regression). When on, logs the cell key and slot counts for headless
    /// verification (A.1); future increments extend to actual rendering (A.2+).
    ///
    /// The concrete <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell"/> is reached by
    /// casting to <see cref="global::MartialHeroes.Client.Godot.Adapters.AssembledCellViewAdapter"/>
    /// (the public adapter the composition root uses). Layer 05 owns Assets.Mapping and may use
    /// the concrete type directly.
    /// spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries the 9 slot model.
    /// </summary>
    public void OnCellAssembled(IAssembledCellView cellView)
    {
        if (!_composeRender) return;

        // Reach the concrete AssembledCell via the layer-05 public adapter.
        // AssembledCellViewAdapter is in Adapters/ (same layer-05 project) and is not file-local.
        global::MartialHeroes.Assets.Mapping.AssembledCell? cell =
            (cellView as global::MartialHeroes.Client.Godot.Adapters.AssembledCellViewAdapter)?.ConcreteCell;

        int slot1Count = cell?.Slot1BuildingObjectGrid?.Objects.Length ?? -1;
        int hasSlot0 = cell?.Slot0GroundTexGrid is not null ? 1 : 0;
        int fxSlots = 0;
        if (cell is not null)
        {
            if (cell.Slot2Fx1 is not null) fxSlots++;
            if (cell.Slot3Fx2 is not null) fxSlots++;
            if (cell.Slot4Fx3 is not null) fxSlots++;
            if (cell.Slot5Fx4 is not null) fxSlots++;
            if (cell.Slot6Fx5 is not null) fxSlots++;
            if (cell.Slot7Fx6 is not null) fxSlots++;
            if (cell.Slot8Fx7 is not null) fxSlots++;
        }

        GD.Print($"[RealWorldRenderer][ComposeRender] CellAssembled: cell=({cellView.MapX},{cellView.MapZ}) " +
                 $"resolved={cellView.IsResolved} slot0={hasSlot0} slot1Buildings={slot1Count} fxSlots={fxSlots}. " +
                 "spec: assembly_graph.md §1.");

        // A.2: store the assembled cell and wire the composer-driven terrain texture resolver.
        if (cell is not null)
        {
            _composedCells[(cellView.MapX, cellView.MapZ)] = cell;
            GD.Print($"[RealWorldRenderer][ComposeRender] Cell ({cellView.MapX},{cellView.MapZ}) cached " +
                     $"({_composedCells.Count} total). spec: assembly_graph.md §1.");

            // A.2: wire the terrain texture resolver from the assembled cell's pre-baked path array.
            // Only wire for the target cell (the one that matches the loaded streaming anchor) — the
            // TerrainNode resolver is shared across all sectors, so we use the cell whose texByte→path
            // mapping covers the visible streaming ring. The pre-baked ResolvedTexturePaths array
            // is indexed by patch position (0..255), where each entry is the resolved DDS path for that
            // patch. Since all patches sharing the same texByte resolve to the same path, we scan the
            // array to find the canonical path for each distinct texByte value.
            // spec: Docs/RE/specs/assembly_graph.md §1 — ResolvedTexturePaths baked by AreaComposer.
            // spec: Docs/RE/formats/terrain.md §5.6 — texByte is 1-based, clamp [1,count].
            if (_terrainNode is not null && cell.ResolvedTexturePaths is not null
                                         && cell.Slot0GroundTexGrid is not null)
            {
                WireComposerTerrainResolver(cell);
            }

            // A.4: render slots 2-8 (FX overlays) from the assembled cell.
            // Missing FX slots are silently skipped (no crash). Effect sub-offsets negate Z port-side.
            // spec: Docs/RE/structs/terrain-manager.md slots 2-8 — FX overlays: CONFIRMED.
            // spec: WorldCoordinates.ToGodot — world geometry negates Z (port-side): CONFIRMED.
            {
                // SW corner of the cell in Godot space (Z negated from legacy).
                // spec: terrain.md §1.4 — worldX_min = (mapX-10000)×1024; cell size 1024 wu: CONFIRMED.
                float cellLegacyX = (cellView.MapX - 10000) * 1024f; // spec: terrain.md §1.4
                float cellLegacyZ = (cellView.MapZ - 10000) * 1024f; // spec: terrain.md §1.4
                // spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z): CONFIRMED.
                var cellOriginGodot = new Vector3(cellLegacyX, 0f, -cellLegacyZ);

                SlotRenderer.RenderFxSlots(
                    parent: this,
                    cell: cell,
                    cellWorldOriginGodot: cellOriginGodot,
                    cellMapXZ: (cellView.MapX, cellView.MapZ));
            }

            // A.3: render slot 1 buildings from the assembled cell using SlotRenderer.
            // The building texture resolver uses the legacy _cellMap+_bgTextures two-hop chain
            // (same as LoadAndSpawnBudScene) so buildings get their correct textures.
            // For non-target cells (streaming ring), _cellMap may belong to the target cell;
            // this is acceptable for A.3 (single-cell demo parity). A per-cell .map cache
            // is a follow-on improvement.
            // spec: Docs/RE/structs/terrain-manager.md slot 1 — "mass/building-object placement grid (.bud)": CONFIRMED.
            if (cell.Slot1BuildingObjectGrid is not null)
            {
                // Guard against duplicate spawning (the same cell may arrive multiple times if the
                // streaming ring reloads it; track by cell key).
                var cellKey = (cellView.MapX, cellView.MapZ);
                if (!_composedBuildingsSpawned.Contains(cellKey))
                {
                    _composedBuildingsSpawned.Add(cellKey);

                    // Use cached building texture resolver (same lambda as legacy path).
                    var budTexCache = new Dictionary<uint, ImageTexture?>();
                    Func<uint, ImageTexture?> budTexResolver = texId =>
                    {
                        if (budTexCache.TryGetValue(texId, out ImageTexture? cached)) return cached;
                        ImageTexture? tex = ResolveSectionTexture("BUILDING", (int)texId);
                        budTexCache[texId] = tex;
                        return tex;
                    };

                    SlotRenderer.RenderSlot1Buildings(
                        parent: this,
                        cell: cell,
                        budTexResolver: budTexResolver,
                        cellMapXZ: (cellView.MapX, cellView.MapZ));
                }
                else
                {
                    GD.Print($"[RealWorldRenderer][ComposeRender] Cell ({cellView.MapX},{cellView.MapZ}) " +
                             "slot 1 buildings already spawned — skipping duplicate.");
                }
            }
        }
    }

    // Set tracking which cells have already had their composed buildings spawned (avoids duplicate nodes).
    private readonly HashSet<(int MapX, int MapZ)> _composedBuildingsSpawned = new();

    /// <summary>
    /// Wires <see cref="TerrainNode.TextureResolver"/> using the assembled cell's pre-baked
    /// <see cref="global::MartialHeroes.Assets.Mapping.AssembledCell.ResolvedTexturePaths"/> array.
    ///
    /// For each distinct raw texByte value in the <c>.ted</c> TextureIndexGrid, the resolver
    /// scans the pre-baked path array to find the canonical resolved DDS path (the path baked by
    /// <see cref="global::MartialHeroes.Assets.Mapping.AreaComposer.ComposeCell"/> via the same
    /// idx-1 finalize + bgtexture.lst chain), then loads it as a Godot ImageTexture.
    ///
    /// All patches with the same texByte value share the same resolved path (the resolution is a
    /// function of texByte only, not position), so the scan terminates on the first match.
    ///
    /// spec: Docs/RE/specs/assembly_graph.md §1 — ResolvedTexturePaths[256] baked by AreaComposer.
    /// spec: Docs/RE/formats/terrain.md §CORRECTED CYCLE 1 — idx-1 finalize on texByte only: CONFIRMED.
    /// spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join — intTexId IS the 0-based pool slot, NO −1: CONFIRMED.
    /// </summary>
    private void WireComposerTerrainResolver(
        global::MartialHeroes.Assets.Mapping.AssembledCell cell)
    {
        if (_assets is null || _terrainNode is null) return;
        if (cell.ResolvedTexturePaths is null || cell.Slot0GroundTexGrid is null) return;

        string?[] resolvedPaths = cell.ResolvedTexturePaths;
        byte[] textureIndexGrid = cell.Slot0GroundTexGrid.TextureIndexGrid;

        // Build a texByte → (patchIndex of first patch with that byte) map to allow O(1) lookup.
        // spec: terrain.md §5.6 — texByte 1-based; all patches with same byte share same resolved path.
        var byteToFirstPatchIndex = new Dictionary<byte, int>(256);
        for (int i = 0; i < textureIndexGrid.Length; i++)
        {
            byte b = textureIndexGrid[i];
            if (!byteToFirstPatchIndex.ContainsKey(b))
                byteToFirstPatchIndex[b] = i;
        }

        RealClientAssets assetsCapture = _assets;

        _terrainNode.TextureResolver = texByte =>
        {
            if (_composerTexCache.TryGetValue($"tb:{texByte}", out ImageTexture? cached))
                return cached;

            // Clamp texByte to [1, count] then apply the idx-1 finalize.
            // spec: terrain.md §5.6 — clamp <1 to 1; >count to count: CONFIRMED.
            int gridLen = textureIndexGrid.Length;
            byte clamped = texByte < 1 ? (byte)1 : (byte)Math.Min(texByte, 255);

            // Find the first patch index whose texByte (after clamping) matches.
            // Since all patches with the same byte resolve to the same path, any match works.
            if (!byteToFirstPatchIndex.TryGetValue(clamped, out int patchIdx))
            {
                // Byte not found in this cell — fall back to legacy resolver if available.
                _composerTexCache[$"tb:{texByte}"] = null;
                return null;
            }

            // Guard patch index bounds.
            if (patchIdx < 0 || patchIdx >= resolvedPaths.Length)
            {
                _composerTexCache[$"tb:{texByte}"] = null;
                return null;
            }

            string? ddsPath = resolvedPaths[patchIdx];
            ImageTexture? tex = null;
            if (ddsPath is not null)
            {
                // Cache by VFS path to share instances across texByte values that resolve to the same DDS.
                if (!_composerTexCache.TryGetValue(ddsPath, out tex))
                {
                    tex = assetsCapture.Contains(ddsPath) ? assetsCapture.LoadTexture(ddsPath) : null;
                    _composerTexCache[ddsPath] = tex;
                }
            }

            _composerTexCache[$"tb:{texByte}"] = tex;
            return tex;
        };

        GD.Print($"[RealWorldRenderer][ComposeRender] Terrain TextureResolver wired from assembled cell " +
                 $"({cell.MapX},{cell.MapZ}) pre-baked paths ({resolvedPaths.Length} slots). " +
                 "spec: assembly_graph.md §1 — ResolvedTexturePaths baked by AreaComposer.");
    }

    /// <summary>
    /// Called by <see cref="GameLoop.DispatchEvent"/> on the main thread each frame when an
    /// <see cref="AreaAssembledEvent"/> arrives. No-op when <see cref="_composeRender"/> is off.
    ///
    /// When <see cref="_composeRender"/> is ON: places actors from <paramref name="areaView"/>.Spawns
    /// via <see cref="NpcRenderer.PopulateFromSpawns"/>. No-double-spawn: the composer path was
    /// already guarded in <see cref="Initialise"/> so <see cref="NpcRenderer.PopulateFromArea"/>
    /// was skipped; this handler places actors exactly once.
    ///
    /// Actor Y is snapped to terrain after each sector loads (TerrainNode.SectorBecameResident → the
    /// NpcRenderer's pending-snap mechanism) — same deferred grounding as the legacy path.
    ///
    /// spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns).
    /// spec: Helpers/WorldCoordinates.cs — ToGodot negate Z. CONFIRMED.
    /// </summary>
    public void OnAreaAssembled(IAssembledAreaView areaView)
    {
        if (!_composeRender) return;

        GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: area={areaView.AreaId} " +
                 $"cellCount={areaView.CellKeyCount} spawns={areaView.Spawns.Count}. " +
                 "spec: assembly_graph.md §1.");

        // Avoid double-spawn: guard with a flag so a re-fire of AreaAssembledEvent for the same
        // area is a no-op. (AreaAssemblyHandoff guarantees exactly-once publish per distinct area id,
        // but an additional defence is cheap.) spec: assembly_graph.md §1 (idempotent).
        if (_composerActorsAreaId == areaView.AreaId)
        {
            GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: area={areaView.AreaId} " +
                     "actors already placed — skipping duplicate.");
            return;
        }

        _composerActorsAreaId = areaView.AreaId;

        if (_assets is null)
        {
            GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: assets null — cannot place actors.");
            return;
        }

        // Build and add a NpcRenderer to the scene, then drive it from the composer spawn list.
        // This reuses all of NpcRenderer's skin-lookup, TryBuildFromMobId, and pending-snap machinery.
        // NpcRenderer.PopulateFromArea is suppressed in Initialise when _composeRender is ON, so
        // this is the only placement call — no double-spawn possible.
        // spec: Docs/RE/specs/assembly_graph.md §1 — spawns placed via PopulateFromSpawns.
        try
        {
            var npcRenderer = new NpcRenderer { Name = "NpcRendererComposer" };

            // Inject the same ground-Y delegates as the legacy path (fallback 26 until terrain loads).
            npcRenderer.GroundYFunc = (lx, lz) => _terrainNode?.GetGroundHeight(lx, lz, 26f) ?? 26f;
            if (_terrainNode is not null)
            {
                TerrainNode terrainCapture = _terrainNode;
                npcRenderer.TryGroundYFunc = (float lx, float lz, out float hy) =>
                    terrainCapture.TryGetGroundHeight(lx, lz, out hy);
            }

            AddChild(npcRenderer);

            // Drive placement from the composer spawn list (not from the VFS .arr files directly).
            // spec: assembly_graph.md §1 — "spawns: npc.arr / mob.arr (position/facing/static metadata)".
            npcRenderer.PopulateFromSpawns(_assets, areaView.Spawns);

            // Wire pending-snap so actors snap to real terrain as cells stream in.
            // spec: TerrainNode.SectorBecameResident — fired on the main thread after heightmap cached.
            if (_terrainNode is not null)
                _terrainNode.SectorBecameResident += npcRenderer.OnSectorBecameResident;

            GD.Print($"[RealWorldRenderer][ComposeRender] AreaAssembled: NpcRendererComposer placed " +
                     $"from {areaView.Spawns.Count} composer spawns for area={areaView.AreaId}. " +
                     "spec: assembly_graph.md §1/§4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer][ComposeRender] AreaAssembled actor placement failed: {ex.Message}");
        }
    }

    // Tracks the area id for which composer actors have been placed (anti-double-spawn guard).
    // -1 = not yet placed. spec: assembly_graph.md §1 (AreaAssemblyHandoff is idempotent once per area).
    private int _composerActorsAreaId = -1;

    /// <summary>
    /// Reads the "compose_render" bool flag from <c>client_dir.cfg</c> or the env
    /// variable <c>MH_COMPOSE_RENDER</c>. Default OFF so the legacy path is unchanged.
    /// </summary>
    private static bool ReadComposeRenderFlag()
    {
        string? envVal = System.Environment.GetEnvironmentVariable("MH_COMPOSE_RENDER");
        if (envVal is "1" or "true" or "yes")
            return true;

        try
        {
            string absPath = global::Godot.ProjectSettings.GlobalizePath("res://client_dir.cfg");
            if (!File.Exists(absPath)) return false;

            foreach (string rawLine in File.ReadLines(absPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                if (k.Equals("compose_render", StringComparison.OrdinalIgnoreCase))
                    return v is "1" or "true" or "yes";
            }
        }
        catch
        {
            // I/O error → default false.
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Camera placement
    // -------------------------------------------------------------------------

    // Kept for PlayerController → camera position update wiring (set in SpawnCamera).
    private CameraController? _cameraController;

    private void SpawnCamera()
    {
        // Compute the Godot-space centre of the resolved terrain cell.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        float centreX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        float centreZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;
        float godotZ = -centreZ; // negate Z: spec WorldCoordinates.ToGodot.
        // spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z. CONFIRMED.

        var cellCentre = new Vector3(centreX, 0f, godotZ);

        // Replace any existing static Camera3D with the spec-faithful CameraController.
        Camera3D? existing = GetViewport()?.GetCamera3D();
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
            TerrainNode terrainCapture = _terrainNode;
            cam.GroundHeightFunc = (lx, lz) => terrainCapture.GetGroundHeight(lx, lz, 0f);
        }

        cam.Configure(cellCentre, 1024f); // spec: terrain.md §1.4 — cell size 1024. CONFIRMED.
        cam.MakeCurrent();

        _cameraController = cam;

        GD.Print($"[RealWorldRenderer] CameraController spawned (spec-faithful Third-person orbit). " +
                 $"Cell centre ({centreX:F0}, 0, {godotZ:F0}). " +
                 "RMB=orbit, wheel=elevation, ESC=reset-to-Third, Tab=devFreeFly.");
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
    ///   3. If at least one cell is found for the requested area, read the spawn files (mob/npc
    ///      .arr) to compute a spawn-weighted anchor cell, then call <see cref="PickRingCenter"/>
    ///      with that anchor so the ring centers on where the game content actually is.
    ///      Falls back to terrain centroid when no spawn data is available.
    ///   4. If the requested area has NO cells, try areas 0..20 in order and pick the first
    ///      area+cell pair that exists.
    ///   5. If no cells are found in any area, fall back to the configured defaults and log a
    ///      warning — streaming will silently produce empty sectors but won't crash.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.3 — per-cell path pattern. CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §1.1 — area id digit decomposition. CONFIRMED.
    /// spec: Docs/RE/formats/npc_spawns.md — world_x f32@4, world_z f32@8: CONFIRMED.
    /// </summary>
    private void ResolveTargetCell()
    {
        if (_assets is null) return;

        // Read area= from config. Default 0. Silently ignore missing key.
        int configArea = ReadAreaFromConfig();
        GD.Print($"[RealWorldRenderer] Config area={configArea} (from client_dir.cfg or default).");

        // Read ring_radius= from config. Default 2 (5×5 ring, High quality).
        // spec: Docs/RE/formats/terrain.md §12.2 — High quality = radius 2 (5×5). CONFIRMED.
        int ringRadius = ReadRingRadiusFromConfig();
        GD.Print($"[RealWorldRenderer] Ring radius={ringRadius} ({(2 * ringRadius + 1)}×{(2 * ringRadius + 1)} ring) " +
                 $"(from client_dir.cfg ring_radius= or default 2).");

        // Try to get cells for the configured area first.
        List<(int MapX, int MapZ)> cells = _assets.EnumerateTerrainCells(configArea);
        if (cells.Count > 0)
        {
            // Compute a spawn-weighted anchor from mob/npc .arr files so the ring centers on
            // game content (the walled town, NPC clusters) rather than the terrain centroid.
            // spec: Docs/RE/formats/terrain.md §1.4 — cell key formula. CONFIRMED.
            // spec: Docs/RE/formats/npc_spawns.md — world_x f32@4, world_z f32@8. CONFIRMED.
            // The spawn-anchor density is always computed with the smallest ring (radius=1 / 3×3
            // neighbourhood) to find the tightest content cluster — not the streaming ring radius.
            // A 5×5 neighbourhood would smear the density peak outward and miss tight clusters.
            // The found anchor is then passed to PickRingCenter which selects a streaming center
            // covering the anchor with the actual ringRadius.
            // spec: Docs/RE/formats/terrain.md §12.2 — density anchor at radius 1; stream at radius 2. CONFIRMED.
            const int DensityRadius = 1; // always 3×3 neighbourhood for anchor detection.
            (double anchorX, double anchorZ) = ComputeSpawnAnchor(_assets, configArea, cells, DensityRadius);
            (int mx, int mz, bool fullRing) = PickRingCenter(cells, anchorX, anchorZ, ringRadius);
            TargetAreaId = configArea;
            TargetMapX = mx;
            TargetMapZ = mz;
            GD.Print($"[RealWorldRenderer] Area {configArea}: {cells.Count} cells found — " +
                     $"selected ({TargetMapX},{TargetMapZ}) " +
                     $"(anchor=({anchorX:F1},{anchorZ:F1}), " +
                     $"full {(2 * ringRadius + 1)}×{(2 * ringRadius + 1)} ring={fullRing}).");
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
                const int DensityRadiusAuto = 1; // same fixed radius for auto-select.
                (double anchorX, double anchorZ) = ComputeSpawnAnchor(_assets, area, areaCells, DensityRadiusAuto);
                (int mx, int mz, bool fullRing) = PickRingCenter(areaCells, anchorX, anchorZ, ringRadius);
                TargetAreaId = area;
                TargetMapX = mx;
                TargetMapZ = mz;
                GD.Print($"[RealWorldRenderer] Auto-selected area {area}: {areaCells.Count} cells — " +
                         $"cell ({TargetMapX},{TargetMapZ}) " +
                         $"(full {(2 * ringRadius + 1)}×{(2 * ringRadius + 1)} ring={fullRing}).");
                return;
            }
        }

        // No cells found anywhere — keep configured defaults but warn clearly.
        GD.PrintErr($"[RealWorldRenderer] WARNING: no .ted cells found in any area 0..20. " +
                    $"Keeping defaults ({TargetMapX},{TargetMapZ}) — streaming will produce empty sectors.");
    }

    /// <summary>
    /// Computes a spawn-density anchor by reading the area's <c>mob{tag}.arr</c> and
    /// <c>npc{tag}.arr</c> files and finding the cell that maximises the number of spawn
    /// records that fall within a neighbourhood of <paramref name="ringRadius"/> cells
    /// (matching the streaming ring size).
    ///
    /// Using the density-peak cell (rather than the simple centroid) handles the common case
    /// where the game content (NPC clusters, the walled town) is concentrated in one corner
    /// of the area's spawn grid.  The centroid can be pulled toward a sparse but large
    /// peripheral region and land far from the actual player-visible cluster.
    ///
    /// Falls back to the terrain centroid when no spawn data is available.
    ///
    /// Cell key formula (matches <see cref="TerrainNode.TryGetGroundHeight"/>):
    ///   mapX = floor(worldX / 1024) + 10000
    ///   mapZ = floor(worldZ / 1024) + 10000
    /// spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024. CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (ring radius 2). CONFIRMED.
    /// spec: Docs/RE/formats/npc_spawns.md — world_x f32@4, world_z f32@8. CONFIRMED.
    /// spec: MISSION B — mob{tag}.arr world_x f32@4, world_z f32@8. CONFIRMED.
    /// </summary>
    private static (double AnchorMapX, double AnchorMapZ) ComputeSpawnAnchor(
        RealClientAssets assets,
        int areaId,
        List<(int MapX, int MapZ)> terrainCells,
        int ringRadius = 2)
    {
        // Fallback: terrain centroid.
        long sumX = 0, sumZ = 0;
        foreach ((int x, int z) in terrainCells)
        {
            sumX += x;
            sumZ += z;
        }

        double fallbackX = sumX / (double)terrainCells.Count;
        double fallbackZ = sumZ / (double)terrainCells.Count;

        if (areaId == 0)
        {
            // Area 0 has no spawn data.
            // spec: Docs/RE/formats/npc_spawns.md §Anomaly: map 000 — 0 records: CONFIRMED.
            return (fallbackX, fallbackZ);
        }

        string tag = AreaTag(areaId);

        // Accumulate per-cell spawn count in a dictionary.
        // Key: (cellMapX, cellMapZ). Value: number of spawn records in that cell.
        // spec: terrain.md §1.4 — cellMapX = floor(worldX/1024)+10000. CONFIRMED.
        var cellCounts = new Dictionary<(int, int), int>(64);

        // ── mob{tag}.arr ──────────────────────────────────────────────────────
        // 20-byte records; world_x f32@4, world_z f32@8.
        // spec: MISSION B — mob record layout; world_x @4, world_z @8. CONFIRMED.
        string mobPath = $"data/map{tag}/mob{tag}.arr";
        if (assets.Contains(mobPath))
        {
            try
            {
                ReadOnlyMemory<byte> raw = assets.GetRaw(mobPath);
                var mobRecords = MobSpawnParser.Parse(raw);
                foreach (var rec in mobRecords)
                {
                    int cx = (int)Math.Floor(rec.WorldX / 1024.0) + 10000;
                    int cz = (int)Math.Floor(rec.WorldZ / 1024.0) + 10000;
                    cellCounts.TryGetValue((cx, cz), out int existing);
                    cellCounts[(cx, cz)] = existing + 1;
                }
            }
            catch
            {
                // Parse failure: ignore, use what we have.
            }
        }

        // ── npc{tag}.arr ──────────────────────────────────────────────────────
        // 28-byte records; world_x f32@4, world_z f32@8.
        // spec: Docs/RE/formats/npc_spawns.md — world_x @4, world_z @8. CONFIRMED.
        string npcPath = $"data/map{tag}/npc{tag}.arr";
        if (assets.Contains(npcPath))
        {
            try
            {
                ReadOnlyMemory<byte> raw = assets.GetRaw(npcPath);
                NpcSpawnArray npcArray = NpcSpawnParser.Parse(raw);
                foreach (var rec in npcArray.Records)
                {
                    if (rec.MobId == 0) continue;
                    int cx = (int)Math.Floor(rec.WorldX / 1024.0) + 10000;
                    int cz = (int)Math.Floor(rec.WorldZ / 1024.0) + 10000;
                    cellCounts.TryGetValue((cx, cz), out int existing);
                    cellCounts[(cx, cz)] = existing + 1;
                }
            }
            catch
            {
                // Parse failure: ignore, use what we have.
            }
        }

        if (cellCounts.Count == 0)
        {
            // No usable spawn data — fall back to terrain centroid.
            return (fallbackX, fallbackZ);
        }

        // Find the cell whose (2r+1)×(2r+1) neighbourhood (the streaming ring at ringRadius)
        // covers the most spawns. This is an O(spawnerCells × (2r+1)²) pass — small for typical areas.
        // spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (ringRadius=2). CONFIRMED.
        int bestNeighbourCount = -1;
        (int BestCX, int BestCZ) bestDensityCell = (0, 0);
        foreach ((int cx, int cz) in cellCounts.Keys)
        {
            int neighbourhood = 0;
            for (int dz = -ringRadius; dz <= ringRadius; dz++)
            {
                for (int dx = -ringRadius; dx <= ringRadius; dx++)
                {
                    cellCounts.TryGetValue((cx + dx, cz + dz), out int n);
                    neighbourhood += n;
                }
            }

            if (neighbourhood > bestNeighbourCount)
            {
                bestNeighbourCount = neighbourhood;
                bestDensityCell = (cx, cz);
            }
        }

        int totalCount = cellCounts.Values.Sum();
        GD.Print($"[RealWorldRenderer] Spawn density anchor for area {areaId}: " +
                 $"({bestDensityCell.BestCX},{bestDensityCell.BestCZ}) " +
                 $"neighbourhood={bestNeighbourCount}/{totalCount} spawns " +
                 $"(terrain centroid was ({fallbackX:F2},{fallbackZ:F2})).");
        return (bestDensityCell.BestCX, bestDensityCell.BestCZ);
    }

    /// <summary>
    /// Picks the cell to centre the streaming ring on, given every <c>.ted</c> cell present in an
    /// area.
    ///
    /// Strategy (two-pass):
    ///   Pass 1 — full-ring preference: find the complete-ring candidate (all neighbours at
    ///   Chebyshev radius <paramref name="ringRadius"/> present) nearest to the anchor.
    ///   A full ring guarantees all (2r+1)² sectors render without holes.
    ///   Pass 2 — fallback to any cell: if no full-ring cell exists, OR if the nearest full-ring
    ///   cell is more than <c>MaxFullRingFallbackDistance</c> cells away from the anchor (meaning
    ///   the NPC/spawn cluster lives outside all complete-ring areas), pick the available cell that
    ///   is simply nearest to the anchor, even if its ring is incomplete.
    ///
    ///   The fallback matters when spawn data is dense in a region where the terrain edge cells
    ///   don't have enough neighbours to form a complete ring (e.g. the walled town is near the
    ///   edge of the map grid).  In that case it is better to centre the stream on the actual
    ///   content and accept a few missing border sectors than to centre it on a geometrically-
    ///   perfect but content-empty region far away.
    ///   spec: Docs/RE/formats/terrain.md §12.3 — eviction: absent keys yield empty loads, not crashes.
    ///
    /// The anchor point is the spawn-weighted centroid (from <see cref="ComputeSpawnAnchor"/>)
    /// so the ring centers on where the game content actually is.
    ///
    /// spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (ringRadius=2). CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §1.3 (per-cell path). CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 wu. CONFIRMED.
    /// </summary>
    /// <param name="cells">All cell coordinates available for the area (may be unsorted).</param>
    /// <param name="anchorMapX">Target mapX to stay near (e.g. spawn centroid cell X).</param>
    /// <param name="anchorMapZ">Target mapZ to stay near (e.g. spawn centroid cell Z).</param>
    /// <param name="ringRadius">
    /// Chebyshev radius of the streaming ring (1 = 3×3, 2 = 5×5).
    /// spec: Docs/RE/formats/terrain.md §12.2 — High quality → radius 2. CONFIRMED.
    /// </param>
    /// <returns>The chosen centre cell and whether its full ring exists.</returns>
    private static (int MapX, int MapZ, bool FullRing) PickRingCenter(
        List<(int MapX, int MapZ)> cells,
        double anchorMapX,
        double anchorMapZ,
        int ringRadius = 2)
    {
        // When the nearest full-ring cell exceeds this Chebyshev distance from the anchor,
        // we prefer a partial-ring cell that is actually near the content.
        // A value of 2 means: "the full-ring center is more than 2 cells away from the NPC
        // cluster — prefer proximity to content over a perfect ring".
        // spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 wu per cell. CONFIRMED.
        const double MaxFullRingFallbackDistance = 2.0;

        // Deterministic order so a tie resolves the same way every run.
        cells.Sort((a, b) => a.MapX != b.MapX ? a.MapX.CompareTo(b.MapX) : a.MapZ.CompareTo(b.MapZ));

        var present = new HashSet<(int, int)>(cells.Count);
        foreach ((int x, int z) in cells)
        {
            present.Add((x, z));
        }

        // ── Pass 1: nearest full-ring candidate ───────────────────────────────
        bool bestFullFound = false;
        (int MapX, int MapZ) bestFull = cells[cells.Count / 2];
        double bestFullDist = double.MaxValue;

        // ── Pass 2: nearest any-cell candidate ───────────────────────────────
        (int MapX, int MapZ) bestAny = cells[cells.Count / 2];
        double bestAnyDist = double.MaxValue;

        foreach ((int cx, int cz) in cells)
        {
            double ddx = cx - anchorMapX;
            double ddz = cz - anchorMapZ;
            double dist = ddx * ddx + ddz * ddz; // squared distance in cell units

            // Any-cell pass: always track the nearest cell regardless of ring completeness.
            if (dist < bestAnyDist)
            {
                bestAny = (cx, cz);
                bestAnyDist = dist;
            }

            // Full-ring pass: a full ring requires all (2r+1)² cells at Chebyshev radius r.
            // For r=2 (5×5) that is 25 cells including the centre itself.
            // spec: Docs/RE/formats/terrain.md §12.2 — High quality = 5×5 ring (r=2). CONFIRMED.
            bool full = true;
            for (int dz = -ringRadius; dz <= ringRadius && full; dz++)
            {
                for (int dx = -ringRadius; dx <= ringRadius; dx++)
                {
                    if (!present.Contains((cx + dx, cz + dz)))
                    {
                        full = false;
                        break;
                    }
                }
            }

            if (!full) continue;

            if (!bestFullFound || dist < bestFullDist)
            {
                bestFullFound = true;
                bestFull = (cx, cz);
                bestFullDist = dist;
            }
        }

        // ── Decision: use full-ring if it is close enough to the anchor ───────
        // If the best full-ring center is within MaxFullRingFallbackDistance cells of the anchor,
        // it is likely covering the content region too — use it for the perfect terrain ring.
        // If it is farther away, the content (NPCs/spawns) lives outside the complete-ring area;
        // use the nearest available cell so the streaming ring at least overlaps the content.
        // spec: Docs/RE/formats/terrain.md §12.3 — absent cells load empty without crash: CONFIRMED.
        double bestFullChebyshev = bestFullFound
            ? Math.Max(Math.Abs(bestFull.MapX - anchorMapX), Math.Abs(bestFull.MapZ - anchorMapZ))
            : double.MaxValue;

        bool useFullRing = bestFullFound && bestFullChebyshev <= MaxFullRingFallbackDistance;
        (int MapX, int MapZ) chosen = useFullRing ? bestFull : bestAny;

        return (chosen.MapX, chosen.MapZ, useFullRing);
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

    /// <summary>
    /// Reads the "ring_radius=" integer key from client_dir.cfg.
    ///
    /// Valid values: 1 (3×3 ring, Medium quality) or 2 (5×5 ring, High quality).
    /// Returns 2 (the high-quality default) when the key is absent, out-of-range, or unparseable.
    ///
    /// spec: Docs/RE/formats/terrain.md §12.2 — High quality = ring radius 2 (5×5). CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §12.2 — Medium/Low quality = ring radius 1 (3×3). CONFIRMED.
    /// </summary>
    private static int ReadRingRadiusFromConfig()
    {
        const int DefaultRingRadius = 2; // spec: terrain.md §12.2 — High quality = radius 2 (5×5). CONFIRMED.
        try
        {
            string absPath = global::Godot.ProjectSettings.GlobalizePath("res://client_dir.cfg");
            if (!File.Exists(absPath)) return DefaultRingRadius;

            foreach (string rawLine in File.ReadLines(absPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line[..eq].Trim();
                string v = line[(eq + 1)..].Trim();
                if (k.Equals("ring_radius", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(v, out int parsed) &&
                    parsed >= 1 && parsed <= 2)
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // Any I/O error → default radius 2.
        }

        return DefaultRingRadius;
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