// World/RealWorldRenderer.cs
//
// PASSIVE rendering node for real client assets (activated when the VFS is available).
// Activated when ClientPathResolver.RealAssetsEnabled returns true (i.e. a valid client dir is resolved).
//
// SERVER-DRIVEN: Initialise() only opens the VFS and sets up the camera — it builds NO area content.
// The world is built solely on the server's 4/1 world-entry (OnWorldEntered), keyed to the
// server-supplied AreaId. There is no offline demo area, no config "area=" default, no demo character.
//
// What this node does (all passive, no game logic):
//   1. On 4/1 (OnWorldEntered): streams the server area's terrain sectors (SectorStreamingService),
//      loads its building geometry (.bud → ArrayMesh via BudMeshBuilder — NO GltfDocument), loads its
//      static NPC scenery (mob/npc .arr), wires environment, and applies diffuse textures (DDS/PNG/BMP).
//   2. The local player + dynamic actors arrive from the server (3/7 spawn, 4/4 actor stream) and are
//      placed by ActorRegistry — this node renders no character itself.
//   3. Positions a CameraController; SetLocalPlayer (called by GameLoop on 3/7) makes it follow the
//      live player.
//   4. PLAYER-FOLLOWING TERRAIN STREAMING: each frame, once a live player is registered, if the player
//      crosses a cell boundary (Chebyshev ≥ 1 from the anchor, with hysteresis) the streaming ring
//      re-centres on the player's cell. New sectors load asynchronously off the main thread; sectors
//      > 2 cells from the new anchor are evicted (Chebyshev eviction).
//      spec: Docs/RE/specs/resource_pipeline.md §4.3 (streamer thread); terrain.md §9.2 / §9.3 (eviction).
//
// GltfDocument.AppendFromBuffer is NOT used anywhere in this file. The native Godot GLB importer
// was removed because it caused a native crash on our generated GLBs (no managed stack trace).
// BudMeshBuilder builds Godot ArrayMesh directly from parsed model data.
//
// Threading: all Godot node creation happens on the main thread (_Ready or CallDeferred).
// The sector streaming call goes through SectorStreamingService.UpdateCenterAsync which
// is called from a background task (fire-and-forget) — TerrainNode reacts via the event bus.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/terrain.md §1.1–1.4 (path, manifest, ted, world bounds).
// spec: Docs/RE/formats/terrain.md §12.2 (5×5 streaming ring at StreamQuality.High, configurable via ring_radius= in client_dir.cfg).
// spec: Docs/RE/formats/terrain_scene.md (bud scene).
// spec: Docs/RE/formats/mesh.md (skn/bnd).
// spec: Docs/RE/formats/texture.md (png/bmp/dds/tga).

using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Passive rendering node for real client assets.
///     Spawned and started by <see cref="GameLoop._Ready" /> when <see cref="IsEnabled" /> is true.
///     <para>
///         <b>Server-driven world build (no offline demo).</b> <see cref="Initialise" /> only opens the
///         VFS and sets up the camera; it builds NO area content. The world is built solely on the
///         server's 4/1 world-entry packet via <see cref="OnWorldEntered" />, keyed to the
///         server-supplied AreaId — terrain, buildings, and the area's static NPC scenery all load from
///         the VFS for that area. The local player and dynamic actors arrive from the server
///         (3/7 spawn, 4/4 actor stream); this node renders no demo character.
///         spec: Docs/RE/specs/world_entry.md §2.3/§3.1.
///     </para>
///     NOTE: GltfDocument.AppendFromBuffer is NOT called by this class. All geometry is built
///     as Godot ArrayMesh directly via BudMeshBuilder.
/// </summary>
public sealed partial class RealWorldRenderer : Node3D
{
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
    ///     Recenter only when the player moves ≥ this many cells (Chebyshev) from the current anchor.
    ///     Prevents thrash when the player walks along a cell boundary.
    ///     spec: Docs/RE/specs/resource_pipeline.md §4.3 — streaming ring follows the player cell. CODE-CONFIRMED.
    /// </summary>
    private const int HysteresisThresholdCells = 1;

    // Set tracking which cells have already had their composed buildings spawned (avoids duplicate nodes).
    private readonly HashSet<(int MapX, int MapZ)> _composedBuildingsSpawned = new();

    /// <summary>
    ///     Assembled cell cache keyed by biased cell coordinates (MapX, MapZ).
    ///     Populated when <see cref="_composeRender" /> is on via <see cref="OnCellAssembled" />.
    ///     Used by the composer-driven TextureResolver (A.2) and SlotRenderer (A.3).
    ///     All mutations happen on the Godot main thread (inside <see cref="OnCellAssembled" /> which
    ///     is called from <see cref="GameLoop.DispatchEvent" /> → <see cref="GameLoop._Process" />).
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — assembled cell carries 9 slots + ResolvedTexturePaths.
    /// </summary>
    private readonly Dictionary<(int MapX, int MapZ), AssembledCell>
        _composedCells = new();

    /// <summary>
    ///     Loaded texture cache for the composer path (A.2): maps VFS DDS path → Godot ImageTexture.
    ///     All mutations on the main thread.
    /// </summary>
    private readonly Dictionary<string, ImageTexture?> _composerTexCache = new();

    // Node-lifetime cancellation: cancelled in _ExitTree so the fire-and-forget streaming task
    // stops touching this node (and skips its completion prints) once the node leaves the tree.
    private readonly CancellationTokenSource _lifetimeCts = new();

    // True once the server area's static content (buildings + .arr NPC scenery) has been built for the
    // first server world-entry (4/1) on the legacy (non-compose) path. Guards OnWorldEntered against
    // rebuilding the single-cell content on every 4/1. spec: server 4/1 is the sole world-build trigger.
    private bool _areaContentBuilt;

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    // Texture-resolution inputs, loaded once in Initialise after the target cell is resolved.
    // The confirmed two-hop chain is: cell/building 1-based index → the cell .map's per-section
    // TEXTURES[idx-1].intTexId (1-based pool slot) → bgtexture pool[intTexId-1] → data/map000/texture/<rel>.dds.
    // The runtime form is the BINARY bgtexture.lst (bgtexture.txt is absent from a real packed VFS);
    // the .txt mirror is only a dev/loose-tree fallback.
    // spec: Docs/RE/specs/asset_pipeline.md §3 chain B (runtime = bgtexture.lst; .txt absent) +
    //       Docs/RE/formats/bgtexture_lst.md §Cross-file join (.map intTexId-1 → .lst slot) +
    //       Docs/RE/formats/terrain.md §3.5 (.map TEXTURES) + §5.6 (render-domain idx). CONFIRMED.
    private BgTextureCatalog? _bgTextures; // global pool: intTexId → relPath

    // Kept for PlayerController → camera position update wiring (set in SpawnCamera).
    private CameraController? _cameraController;
    private MapDescriptor? _cellMap; // the target cell's .map (TERRAIN/BUILDING TEXTURES lists)

    // Tracks the area id for which composer actors have been placed (anti-double-spawn guard).
    // -1 = not yet placed. spec: assembly_graph.md §1 (AreaAssemblyHandoff is idempotent once per area).
    private int _composerActorsAreaId = -1;

    // -------------------------------------------------------------------------
    // Composer-driven rendering (CYCLE 2 Phase 2-A)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     When true, terrain and buildings are rendered FROM the assembled cell produced by the
    ///     CYCLE-1 AreaComposer pipeline, rather than the legacy direct-from-VFS path.
    ///     Default: false — the legacy path is byte-identical to the pre-2-A build (zero regression).
    ///     Set to true via client_dir.cfg key "compose_render=1" or env "MH_COMPOSE_RENDER=1".
    ///     This flag is the only gate between the old and new path; no code is removed while both
    ///     paths coexist. spec: Docs/RE/specs/assembly_graph.md §1 — AreaComposer owns the assembled cell.
    /// </summary>
    private bool _composeRender;

    private ClientContext? _ctx;

    // True once TriggerTerrainStreaming has set _streamAnchor (arm signal for _Process).
    private bool _followAnchorArmed;

    // True while a streaming update is in-flight (fire-and-forget Task still running).
    // Guards against overlapping recenter tasks that would double-load the same sectors.
    private volatile bool _isRecentering;

    // Live local-player node (the 3/7-spawned VisualActor), set by GameLoop via SetLocalPlayer once the
    // server spawns the local player. Drives the camera-follow and the player-following terrain streaming.
    // Null until the server's 3/7 LocalPlayerSpawnedEvent arrives — there is no local player to follow
    // before then (strictly server-driven; no offline demo character).
    private Node3D? _localPlayerNode;

    // Current streaming anchor in biased cell coordinates (mapX, mapZ).
    // Initialised from the spawn-density peak in TriggerTerrainStreaming; updated each recenter.
    // spec: Docs/RE/formats/terrain.md §Overview — origin bias 10000, cell size 1024. CONFIRMED.
    private (int MapX, int MapZ) _streamAnchor;

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
    private TerrainNode? _terrainNode;

    // Re-entrance guard: prevents concurrent OnWorldEntered calls (e.g. duplicate 4/1 packets).
    private volatile bool _worldEntryInProgress;
    // -------------------------------------------------------------------------
    // Configuration (set before Initialise)
    // -------------------------------------------------------------------------

    /// <summary>Area to load. Default 0 (map000). spec: Docs/RE/formats/terrain.md §1.1.</summary>
    public int TargetAreaId { get; set; } = 0;

    /// <summary>
    ///     Cell X coordinate (world-origin cell = 10000).
    ///     spec: Docs/RE/formats/terrain.md §Overview (bias 10000). CONFIRMED.
    /// </summary>
    public int TargetMapX { get; set; } = 10000;

    /// <summary>Cell Z coordinate. Default 10000. spec: Docs/RE/formats/terrain.md §Overview. CONFIRMED.</summary>
    public int TargetMapZ { get; set; } = 10000;

    /// <summary>
    ///     VFS path for the character .skn.  Null = auto-discover first available.
    ///     spec: Docs/RE/formats/mesh.md §.skn.
    /// </summary>
    public string? SknVirtualPath { get; set; }

    /// <summary>
    ///     VFS path for the character .bnd.  Null = derive from .skn filename or null (single-bone).
    ///     spec: Docs/RE/formats/mesh.md §.bnd.
    /// </summary>
    public string? BndVirtualPath { get; set; }

    // -------------------------------------------------------------------------
    // Static activation check
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns true when the real-asset rendering path should be activated.
    ///     Delegates to <see cref="ClientPathResolver" />:
    ///     - Resolves the client directory (MH_CLIENT_DIR env → config → auto-detect).
    ///     - Returns true whenever a valid directory is found; false when none is.
    ///     There is no dev toggle to force synthetic mode — the client always uses the real VFS when
    ///     present, and renders nothing when it is absent.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            var clientDir = ClientPathResolver.ResolveClientDir();
            return ClientPathResolver.RealAssetsEnabled(clientDir);
        }
    }

    /// <summary>
    ///     Read-only access to the already-open VFS handle so sibling presentation nodes (e.g. the in-world
    ///     local-player avatar build) can resolve assets through the SAME handle instead of opening a second
    ///     memory-mapped archive. Null when offline / real assets disabled. Main-thread only.
    ///     spec: World/PlayerAvatarResolver.cs — local player skin/bind/idle resolution reuses this handle.
    /// </summary>
    public RealClientAssets? Assets { get; private set; }

    // -------------------------------------------------------------------------
    // Live local-player wiring (server-driven)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Registers the server-spawned local-player node (the 3/7 <see cref="VisualActor" />) so the
    ///     camera-follow and the player-following terrain streaming track the real player. Called by
    ///     <see cref="GameLoop" /> from the <c>LocalPlayerSpawnedEvent</c> (3/7) handler. There is no
    ///     offline demo character: until the server spawns the local player this is never called and the
    ///     camera simply holds its spawn-cell framing.
    ///     spec: Docs/RE/specs/login_flow.md §3.5 / §5.3 (3/7 spawns the local player).
    ///     spec: Docs/RE/specs/resource_pipeline.md §4.3 (streamer follows the player cell).
    /// </summary>
    public void SetLocalPlayer(Node3D playerNode)
    {
        _localPlayerNode = playerNode;
        GD.Print("[RealWorldRenderer] Local player registered — camera + terrain-streaming now follow the " +
                 "live (3/7) player. spec: login_flow.md §3.5 / resource_pipeline.md §4.3.");
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public override void _ExitTree()
    {
        // Cancel the per-streaming-call CTS first (stops any in-flight streaming task immediately)
        // then cancel the node-lifetime CTS (belt+suspenders). Both cancellations are safe to
        // issue before Dispose.
        // spec: Docs/RE/formats/terrain.md §12.3 (node exit cancels the streaming task).
        try
        {
            _streamingCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _streamingCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

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

        Assets?.Dispose();
        Assets = null;
    }

    /// <summary>
    ///     Each frame:
    ///     1. Push the player avatar's confirmed Godot position to the CameraController
    ///     so the Third-person orbit always follows the character.
    ///     2. Check whether the player has crossed a cell boundary. If the Chebyshev distance
    ///     from the current streaming anchor to the player's current cell is ≥
    ///     <see cref="HysteresisThresholdCells" />, and no recenter is already in-flight,
    ///     kick off an async <see cref="SectorStreamingService.UpdateCenterAsync" /> call with
    ///     the player's cell as the new anchor. This is the player-following streaming loop:
    ///     as the player walks south the southern cells load and the northern cells are evicted.
    ///     Coordinate conversion (Godot → legacy world):
    ///     The player's Godot position has Z negated relative to legacy world Z.
    ///     legacyX = godotX  (X is unchanged)
    ///     legacyZ = -godotZ  (negate back; spec: WorldCoordinates.ToGodot negate Z. CONFIRMED.)
    ///     Cell: SectorGrid.WorldToSector(legacyX, legacyZ)
    ///     spec: Docs/RE/formats/terrain.md §2 — cell key formula. CONFIRMED.
    ///     spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer follows the player cell. CODE-CONFIRMED.
    ///     Eviction: SectorStreamingService.UpdateCenterAsync → EvictOutOfRange evicts all resident
    ///     sectors with Chebyshev distance > 2 from the new anchor automatically.
    ///     spec: Docs/RE/formats/terrain.md §9.3 — eviction radius 2. CONFIRMED.
    ///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "strictly passive rendering".
    /// </summary>
    public override void _Process(double delta)
    {
        if (_localPlayerNode is not null && IsInstanceValid(_localPlayerNode) && _cameraController is not null)
            _cameraController.PlayerGodotPosition = _localPlayerNode.GlobalPosition;

        // ---- Player-following streaming ----
        // Only runs when the follow anchor is armed (TriggerTerrainStreaming succeeded) and a
        // ClientContext + player controller are available. No game logic here — pure presentation.
        if (!_followAnchorArmed || _ctx is null || _localPlayerNode is null || !IsInstanceValid(_localPlayerNode))
            return;

        // Read the live player's Godot-space position (X unchanged, Z negated relative to legacy).
        // spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z). CONFIRMED.
        var godotPos = _localPlayerNode.GlobalPosition;
        var legacyX = godotPos.X;
        var legacyZ = -godotPos.Z; // negate back to legacy world Z

        // Map to the biased (mapX, mapZ) cell coordinate.
        // spec: Docs/RE/formats/terrain.md §2 — cell key formula: CONFIRMED.
        var (playerMapX, playerMapZ) = SectorGrid.WorldToSector(legacyX, legacyZ);

        // Compute Chebyshev distance from the current streaming anchor.
        var distX = Math.Abs(playerMapX - _streamAnchor.MapX);
        var distZ = Math.Abs(playerMapZ - _streamAnchor.MapZ);
        var chebyshev = Math.Max(distX, distZ);

        // Hysteresis: only recenter when the player is ≥ HysteresisThresholdCells away and
        // no async recenter is currently in-flight.
        // spec: Docs/RE/specs/resource_pipeline.md §4.3 — streamer thread follows the player. CODE-CONFIRMED.
        if (chebyshev < HysteresisThresholdCells || _isRecentering)
            return;

        // Capture locals for the closure (avoid capturing 'this' state that changes each frame).
        var newAnchorX = playerMapX;
        var newAnchorZ = playerMapZ;
        (int OldX, int OldZ) oldAnchor = _streamAnchor;

        // Update the anchor BEFORE launching the task so subsequent frames see the new anchor
        // and do not queue another recenter for the same cell.
        _streamAnchor = (newAnchorX, newAnchorZ);
        _isRecentering = true;

        GD.Print($"[RealWorldRenderer] StreamFollow: recenter anchor " +
                 $"({oldAnchor.OldX},{oldAnchor.OldZ}) → ({newAnchorX},{newAnchorZ}) " +
                 $"(Chebyshev={chebyshev}, player legacyXZ=({legacyX:F0},{legacyZ:F0})).");

        var lifetime = _lifetimeCts.Token;
        var ctxCapture = _ctx;

        _ = Task.Run(async () =>
        {
            try
            {
                await ctxCapture.StreamingService.UpdateCenterAsync(newAnchorX, newAnchorZ, lifetime)
                    .ConfigureAwait(false);

                if (lifetime.IsCancellationRequested) return;

                var resident = ctxCapture.StreamingService.ResidentCount;
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
}