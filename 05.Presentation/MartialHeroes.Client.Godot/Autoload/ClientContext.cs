using Godot;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.Assets;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Application.Contracts.Scene;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.Net;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation.Simulation;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Infrastructure.Catalog;
using MartialHeroes.Client.Presentation.Adapters;
using MartialHeroes.Client.Presentation.Input;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Client.Godot.Autoload;

/// <summary>
///     Composition root / autoload singleton for the presentation layer.
///     Constructs the entire Client.Application object graph and exposes:
///     - <see cref="EventBus" />: the ChannelReader the presentation drains each frame.
///     - <see cref="UseCases" />: the use-case facade Godot nodes call for input intents.
///     - <see cref="Dispatcher" />: the inbound frame dispatcher (routes live-socket frames to the handler).
///     - <see cref="InputBus" />: the input chain-of-responsibility bus (UI first, world second).
///     - <see cref="EngineLoop" />: the fixed-tick 30 Hz simulation loop.
///     - <see cref="ItemCatalogue" />: item definitions from data/script/items.scr, the runtime
///     master the shipping client loads (CP949 names). items.csv is an authoring/dev export the
///     shipping client never reads. spec: Docs/RE/formats/items_csv.md §6, items_scr.md §4.
///     - <see cref="SkillCatalogue" />: skill definitions from skills.scr.
///     - <see cref="MobCatalogue" />: mob definitions from mobs.scr.
///     Threading contract: this node is created once on the Godot main thread. The constructed
///     services are thread-safe (channel-based); Godot node mutation must still happen on the
///     main thread (enforced in GameLoop via CallDeferred).
///     VFS / assets:
///     The composition root resolves the VFS archive path via
///     <see cref="MartialHeroes.Client.Godot.Dev.ClientPathResolver" />:
///     env override (MH_CLIENT_DIR), then client_dir.cfg, then auto-detection of common paths.
///     The client requires the real VFS — a missing or unresolvable VFS is a hard failure.
///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — composition root.
///     spec: Docs/RE/specs/game_loop.md §6 — fixed-tick GameEngineLoop wired here.
///     spec: Docs/RE/specs/input_ui.md §6 — InputBus with UI-first handlers.
/// </summary>
public sealed partial class ClientContext : Node
{
    // VfsCatalogueLoader owns the VFS archive lifecycle for catalogue loading.
    // It is disposed in _ExitTree alongside the loop cancellation.
    private VfsCatalogueLoader? _catalogueLoader;

    // The live game connection session.  null until OpenGameConnectionAsync succeeds.
    // Guarded by a volatile bool to prevent double-open without taking a lock on the hot path.
    private IConnectionSession? _gameConnection;
    private volatile int _gameConnectionOpened; // 0 = not open, 1 = open (Interlocked flag)

    // Stored so GameLoop can wire the live GameHud.HitTest after the HUD node is initialised.
    // This is the fix for F1: "UI is the gate" hit-test is never wired.
    // spec: Docs/RE/specs/input_ui.md §3 / §6.
    private HudInputHandler? _hudInputHandler;

    // The inbound frame dispatcher's reader-loop task. Started at construction, drained in _ExitTree.
    private Task? _inboundTask;

    // VFS resource pipeline used by the state-2 LoadOrchestrator. Disposed in _ExitTree.
    // spec: Docs/RE/specs/resource_pipeline.md §1 / §2.
    private VfsResourcePipeline? _loadVfsPipeline;

    // Cancellation source for the engine loop Task.
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    // The late-binding outbound sink shared between ApplicationUseCases and LoginHandshakeDriver.
    // SetTarget is called in OpenGameConnectionAsync once the game TCP connection is established.
    private RelayOutboundPacketSink? _relaySink;

    // Stored so InputRouter can wire the world handler after initialisation.
    // Typed as Action<IInputHandler> to avoid exposing the file-local type in the member signature.
    private Action<IInputHandler>? _setWorldHandler;

    // The MappedVfsArchive opened by OpenVfsForTerrain() and passed into VfsTerrainSectorSource.
    // VfsTerrainSectorSource stores the reference but does NOT implement IDisposable and therefore
    // does NOT dispose the archive — ownership stays here. Disposed in _ExitTree.
    // spec: Docs/RE/formats/pak.md §Two-file scheme (MappedVfsArchive = memory-mapped handle).
    private MappedVfsArchive? _terrainVfs;

    // RealClientAssets for the UI catalogs (UiTex + msg.xdb).  Disposed in _ExitTree.
    // Kept separate from the terrain VFS so the two archives have independent lifecycles.
    private RealClientAssets? _uiAssets;
    // -------------------------------------------------------------------------
    // Exposed Application surface
    // -------------------------------------------------------------------------

    /// <summary>The outbound UI event stream. Drain once per frame in GameLoop.</summary>
    public IClientEventBus EventBus { get; private set; } = null!;

    /// <summary>The use-case facade. Godot input nodes call these; never apply game logic here.</summary>
    public IApplicationUseCases UseCases { get; private set; } = null!;

    /// <summary>
    ///     The inbound frame dispatcher. Fed by <c>DispatcherFrameSink</c> (the live socket); routes each
    ///     decrypted inbound frame through the dispatch tables to the packet handler.
    /// </summary>
    public InboundFrameDispatcher Dispatcher { get; private set; } = null!;

    /// <summary>
    ///     The faithful 8-state scene machine — the SOLE state machine. Owns the live
    ///     <see cref="MartialHeroes.Shared.Kernel.State.GameState" />; the <see cref="SceneHost" /> listens
    ///     for <see cref="MartialHeroes.Client.Application.Contracts.Scene.SceneStateChangedEvent" /> and swaps the
    ///     live scene to match. spec: Docs/RE/specs/client_runtime.md §7.
    /// </summary>
    public SceneStateMachine SceneMachine { get; private set; } = null!;

    /// <summary>
    ///     Engine-free state-2 load worker/orchestrator. Godot renders its progress and calls into the
    ///     scene spine only after this worker completes; transition policy remains in Application.
    ///     spec: Docs/RE/specs/resource_pipeline.md §2; Docs/RE/specs/client_runtime.md §7.3.
    /// </summary>
    public LoadOrchestrator LoadOrchestrator { get; private set; } = null!;

    /// <summary>
    ///     The input bus: UI handlers first, then world handler.
    ///     spec: Docs/RE/specs/input_ui.md §3 / §6 — UI before world.
    /// </summary>
    public InputBus InputBus { get; private set; } = null!;

    /// <summary>
    ///     The fixed-tick simulation loop (30 Hz). Runs on a background Task started in
    ///     <see cref="_Ready" />. Stopped in <see cref="_ExitTree" />.
    ///     spec: Docs/RE/specs/game_loop.md §6 ("Fixed-rate logic tick").
    /// </summary>
    public GameEngineLoop EngineLoop { get; private set; } = null!;

    // -------------------------------------------------------------------------
    // Catalogue surface (read-only; UI nodes call these for display names)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Item catalogue parsed from data/script/items.scr. Provides CP949-decoded item names
    ///     and stats via <see cref="ItemCatalogue.TryGet" />.
    ///     spec: Docs/RE/formats/items_scr.md §4.
    /// </summary>
    public ItemCatalogue ItemCatalogue { get; private set; } = null!;

    /// <summary>
    ///     Skill catalogue parsed from data/script/skills.scr. Provides skill definitions
    ///     via <see cref="SkillCatalogue.TryGet" />.
    ///     spec: Docs/RE/formats/config_tables.md §2.8 skills.scr.
    /// </summary>
    public SkillCatalogue SkillCatalogue { get; private set; } = null!;

    /// <summary>
    ///     Mob catalogue parsed from data/script/mobs.scr. Provides mob records
    ///     via <see cref="MobCatalogue.TryGet" />.
    ///     spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr.
    /// </summary>
    public MobCatalogue MobCatalogue { get; private set; } = null!;

    /// <summary>
    ///     The terrain streaming service. Call <see cref="SectorStreamingService.UpdateCenterAsync" />
    ///     whenever the local player's sector changes to stream the 3×3 ring of sectors.
    ///     spec: Docs/RE/formats/terrain.md §9.2 (3×3 ring at StreamQuality.Medium).
    /// </summary>
    public SectorStreamingService StreamingService { get; private set; } = null!;

    /// <summary>
    ///     The two startup UI data catalogs:
    ///     - UiTex manifest (data/ui/UiTex.txt)  → <see cref="Adapters.UiCatalogs.GetTexture" />
    ///     - Msg catalog (data/script/msg.xdb)   → <see cref="Adapters.UiCatalogs.GetMessage" />
    ///     spec: Docs/RE/formats/ui_manifests.md §1 (uitex.txt, PARSER-CONFIRMED grammar).
    ///     spec: Docs/RE/formats/misc_data.md §6 (msg.xdb, CODE-CONFIRMED loader).
    ///     spec: Docs/RE/specs/ui_system.md §8.5 (HUD uitex integer binding contract).
    /// </summary>
    public UiCatalogs UiCatalogs { get; private set; } = null!;


    /// <summary>
    ///     The audio service façade. Handles BGM streaming, UI click SFX, world-entry SFX, and
    ///     per-area ambient BGM from the .bgm sound table.
    ///     Null when audio initialisation failed (headless / no audio device).
    ///     Added as a child node in <see cref="_Ready" /> so it participates in the scene lifecycle.
    ///     spec: Docs/RE/specs/sound.md §12.1 (Godot reimplementation guidance).
    ///     spec: Docs/RE/names.yaml runtime_constants (UI_CLICK_SFX_ID, SPAWN_SFX_ID, ENTRY_BGM_CUE_ID).
    /// </summary>
    public AudioService? Audio { get; private set; }

    /// <summary>
    ///     The HUD event hub: the single facade through which packet handlers publish per-frame
    ///     HUD events (chat lines, buff states, combat texts, target changes, XP/level, stat views)
    ///     and HUD widgets subscribe (one ChannelReader per family).
    ///     Constructed once in <see cref="BuildApplicationGraph" />; exposed as <see cref="IHudEventHub" />
    ///     so no consumer sees the mutable concrete type.
    ///     spec: MartialHeroes.Client.Application.Hud — IHudEventHub / HudEventHub.
    /// </summary>
    public IHudEventHub HudEventHub { get; private set; } = null!;

    /// <summary>
    ///     Buff icon catalog: resolves per-buff AtlasTextures from the shared stateicon.dds atlas,
    ///     keyed by buff_id via buff_icon_position.xdb.
    ///     spec: Docs/RE/formats/misc_data.md §1.3 (atlas path, record layout).
    ///     spec: Docs/RE/formats/misc_data.md §1.6 (cell sizes 23/25 px, 30-slot bar).
    /// </summary>
    public BuffIconCatalog BuffIconCatalog { get; private set; } = null!;

    /// <summary>
    ///     Zone catalog: resolves the display name of the zone (and optionally a nearby sub-zone
    ///     label) for a given legacy world XZ position, sourced from mapsetting.scr.
    ///     spec: Docs/RE/formats/misc_data.md §7.1 (mapsetting.scr — 52 zone records).
    ///     spec: Docs/RE/specs/minimap.md §6.3 (zone names live in mapsetting.scr, not msg.xdb).
    /// </summary>
    public ZoneCatalog ZoneCatalog { get; private set; } = null!;

    /// <summary>
    ///     Shared HUD atlas library for the new Ui/Scenes substrate.
    ///     Maps VFS paths and uitex.txt tex_ids → Godot <see cref="Godot.Texture2D" /> objects,
    ///     with AtlasTexture sub-rect slicing.
    ///     Uses the same <c>_uiAssets</c> handle as <see cref="UiCatalogs" />;
    ///     no additional VFS archive is opened.
    ///     spec: Docs/RE/formats/ui_manifests.md §1 — uitex.txt grammar.
    ///     spec: Docs/RE/specs/ui_system.md §1.3 — "atlas pixels map 1:1 to screen pixels".
    /// </summary>
    public HudAtlasLibrary HudAtlas { get; private set; } = null!;

    /// <summary>
    ///     Shared HUD text library for the new Ui/Scenes substrate.
    ///     Loads data/script/msg.xdb and provides CP949-decoded caption lookup by integer id.
    ///     Uses the same <c>_uiAssets</c> handle as <see cref="UiCatalogs" />;
    ///     no additional VFS archive is opened.
    ///     spec: Docs/RE/formats/msg_xdb.md — 516-byte records, ascending unsigned id order.
    ///     spec: Docs/RE/specs/ui_system.md §8 — notice column msg ids 4001–4022.
    /// </summary>
    public HudTextLibrary HudText { get; private set; } = null!;

    /// <summary>
    ///     The region service: resolves the local player's world position to a
    ///     <see cref="MartialHeroes.Shared.Kernel.Enums.ZoneType" /> and publishes
    ///     <see cref="MartialHeroes.Client.Application.Contracts.Hud.ZoneChangedEvent" /> when the zone changes.
    ///     Call <c>LoadAreaAsync</c> when the area is set, then <c>UpdatePosition</c> each frame.
    ///     spec: Docs/RE/specs/world_systems.md Ch. 16.
    /// </summary>
    public RegionService RegionService { get; private set; } = null!;

    /// <summary>
    ///     The session-scoped character-selection store, shared between <c>GamePacketHandler</c>
    ///     (which fills it from 3/1) and <c>ApplicationUseCases</c> (which reads it on
    ///     <c>SelectCharacterAsync</c> / the 3/14 spawn seam).
    ///     spec: Docs/RE/specs/login_flow.md §3.5 — "caches the chosen slot's record locally … consumed on 3/14".
    /// </summary>
    public CharacterSelectionStore? CharacterSelection { get; private set; }

    /// <summary>
    ///     The cell-assembly handoff: subscribes to <see cref="SectorLoadedEvent" /> and re-publishes
    ///     assembled cells as <see cref="CellAssembledEvent" />. The <see cref="GameLoop" /> drains this
    ///     each frame via <see cref="CellAssemblyHandoff.OnSectorLoaded" /> on every received sector.
    ///     Null when the terrain VFS could not be opened (offline mode).
    ///     spec: Docs/RE/specs/assembly_graph.md §1/§4 — the handoff wires the AreaComposer byte→cell bake
    ///     into the streaming pipeline; the bake callback is bound here in the composition root.
    /// </summary>
    public CellAssemblyHandoff? CellAssemblyHandoff { get; private set; }

    /// <summary>
    ///     The area-assembly handoff: composes the full area ONCE per area-enter and publishes it as an
    ///     <see cref="AreaAssembledEvent" /> (carrying the spawn list so the composer-driven actor
    ///     placement reaches layer-05). The layer-05 root (here) binds the area bake to
    ///     <c>AreaComposer.ComposeArea</c>, projected onto the layer-04 <see cref="IAssembledAreaView" />
    ///     via <see cref="MartialHeroes.Client.Godot.Adapters.AssembledAreaViewAdapter" />.
    ///     CYCLE 2 Phase 2-A.5: closes the gap where <see cref="AreaAssembledEvent" /> was never
    ///     published — the spawn list never reached layer-05 and <c>OnAreaAssembled</c> never fired.
    ///     Call <see cref="AreaAssemblyHandoff.OnAreaBound" /> from
    ///     <see cref="World.RealWorldRenderer.TriggerTerrainStreaming" /> alongside the existing
    ///     <c>StreamingService.SetArea</c> + <c>AreaAssemblySource.SetArea</c> calls.
    ///     Null when the terrain VFS could not be opened (offline mode).
    ///     spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4.
    /// </summary>
    public AreaAssemblyHandoff? AreaAssemblyHandoff { get; private set; }

    /// <summary>
    ///     The rebindable area assembly source used by the <see cref="CellAssemblyHandoff" /> bake callback.
    ///     Call <see cref="MartialHeroes.Client.Godot.Adapters.RebindableAreaAssemblySource.SetArea" />
    ///     before triggering terrain streaming to ensure the AreaComposer builds paths for the correct area.
    ///     Phase 2-B.1 fix: the original code hard-coded <c>areaId: 0</c> at construction time. This
    ///     property exposes the rebindable wrapper so <see cref="World.RealWorldRenderer" /> can call
    ///     <c>SetArea(TargetAreaId)</c> alongside <see cref="SectorStreamingService.SetArea" /> — both must
    ///     be rebound to the same area before streaming starts.
    ///     Null when the terrain VFS could not be opened (offline mode).
    ///     spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives AreaComposer paths.
    ///     spec: Docs/RE/formats/terrain.md §1.1 — areaId digit decomposition.
    /// </summary>
    public RebindableAreaAssemblySource? AreaAssemblySource { get; private set; }

    /// <summary>
    ///     The area id the CellBake lambda is currently authorised to compose for.
    ///     Stamped by <see cref="SetExpectedBakeArea" /> each time
    ///     <see cref="World.RealWorldRenderer.TriggerTerrainStreaming" /> rebinds the assembly source
    ///     to a new area (alongside <see cref="RebindableAreaAssemblySource.SetArea" />).
    ///     The CellBake lambda reads this value at drain-time (Godot main thread) and silently drops
    ///     any SectorLoadedEvent whose cell coordinates belong to a previously-bound area — i.e. events
    ///     that were published into the ClientEventBus by the OLD streaming task before the rebind, but
    ///     drained AFTER the rebind. Without this guard those stale events would call
    ///     AreaComposer.ComposeCell with the new (wrong) area source, building paths like
    ///     data/map&lt;newArea&gt;/dat/d&lt;newArea&gt;x&lt;oldCell&gt;... which do not exist → empty cell →
    ///     IsResolved=false → the ~36% cell miss.
    ///     Threading: both writes (SetExpectedBakeArea) and reads (CellBake lambda) happen on the
    ///     Godot main thread (_Process drain loop). No lock needed.
    ///     spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives AreaComposer paths.
    ///     spec: Docs/RE/formats/area_inventory.md §1A — membership gate before any cell streams.
    /// </summary>
    public int ExpectedBakeAreaId { get; private set; } = -1; // -1 = uninitialised (no area bound yet)

    /// <summary>
    ///     The durable 4/1 world-entry state (area id + spawn) — survives the SingleReader channel
    ///     handoff so the InGame scene can recover the area cold-start even when the transient
    ///     InGameWorldBootstrappedEvent was drained by an earlier front-end scene.
    ///     spec: Docs/RE/specs/world_entry.md §2.3 / §3.1 — durable world-entry seam.
    /// </summary>
    public WorldEntryState WorldEntry { get; private set; } = null!;

    /// <summary>
    ///     The in-session keepalive driver: emits the idle <c>2/10000</c> heartbeat (≈20 s of outbound
    ///     silence) and the <c>2/112</c> world enter/leave toggle so the LIVE link is not dropped for
    ///     outbound silence after world-enter. Ticked from <see cref="_Process" /> and armed/disarmed by
    ///     polling <see cref="WorldEntry" />.<see cref="WorldEntryState.IsActive" />.
    ///     spec: Docs/RE/specs/world_entry.md §2.5 / §3.2; net_contracts.md §1.3 / §2.15.
    /// </summary>
    public KeepaliveDriver Keepalive { get; private set; } = null!;

    /// <summary>
    ///     The universal deferred timed-event queue (the "10001" queue). Drained each frame via
    ///     <see cref="Domain.Simulation.Simulation.TimedEventQueue.Drain" /> (two-pass full-tree sweep); flushed by
    ///     <see cref="InGameScene._ExitTree" /> on world → front-end scene transition so stale deferred
    ///     triggers never fire into the next scene.
    ///     Instantiated here (composition root) so layer-05 can call Drain/Flush without needing to
    ///     own the queue's implementation. The queue itself is engine-free (Domain.Simulation).
    ///     spec: Docs/RE/specs/effect-scheduling.md §5A (10001 deferred timed-event queue).
    ///     spec: Docs/RE/specs/effect-scheduling.md §5A.3 — two-pass full-tree sweep; FlushOnSceneTransition.
    /// </summary>
    public TimedEventQueue TimedEventQueue { get; private set; } = null!;

    /// <summary>
    ///     Stamps the <see cref="ExpectedBakeAreaId" /> so the CellBake lambda rejects stale events
    ///     from the previous streaming session.
    ///     Called by <see cref="World.RealWorldRenderer.TriggerTerrainStreaming" /> immediately after
    ///     <see cref="RebindableAreaAssemblySource.SetArea" /> so both the source rebind and the bake
    ///     guard advance atomically (both on the main thread).
    ///     spec: Docs/RE/specs/assembly_graph.md §1 — area rebind before streaming starts.
    /// </summary>
    public void SetExpectedBakeArea(int areaId)
    {
        if (ExpectedBakeAreaId == areaId) return;
        GD.Print($"[ClientContext] SetExpectedBakeArea: {ExpectedBakeAreaId} → {areaId}. " +
                 "spec: assembly_graph.md §1/§4 (area rebind guard).");
        ExpectedBakeAreaId = areaId;
    }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Build the full application object graph. A failed VFS / catalogue load propagates
        // as a hard failure — the client requires the real VFS.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — composition root.
        BuildApplicationGraph();

        // Wire the AudioService as a child node so it participates in the scene lifecycle.
        // spec: Docs/RE/specs/sound.md §12.1 (Godot reimplementation guidance).
        var audio = new AudioService { Name = "AudioService" };
        AddChild(audio);
        Audio = audio;
        GD.Print("[ClientContext] AudioService added as child node.");

        // Start the env-gated verification harness if MH_LOGIN_ID / MH_LOGIN_PW are set.
        // INERT when env vars are absent — interactive UI path is completely unchanged.
        // spec: Docs/RE/specs/login_flow.md §1 (same flow as LoginScene, driven via use-cases).
        MaybeStartEnvLogin();
    }

    /// <summary>
    ///     Per-frame keepalive pump (CYCLE-10 fix 1). Drives the in-session heartbeat with a wall-clock
    ///     millisecond stamp and arms/disarms the driver on the world enter/exit edge (polled via
    ///     <see cref="WorldEntry" />.<see cref="WorldEntryState.IsActive" />). The driver no-ops until
    ///     in-world, so this runs harmlessly on every front-end scene too. Sends are fire-and-forget
    ///     (faults logged, never crash the frame). spec: world_entry.md §2.5 / §3.2; net_contracts.md §2.15.
    /// </summary>
    public override void _Process(double delta)
    {
        var keepalive = Keepalive;
        if (keepalive is null) return; // graph not built yet.

        var nowMs = (long)Time.GetTicksMsec();
        var inWorld = WorldEntry is { IsActive: true };

        if (inWorld && !keepalive.IsInWorld)
            // World-enter edge: 2/112 ENABLE + begin the idle heartbeat. spec: world_entry.md §2.5.
            FireAndForget(keepalive.OnWorldEnteredAsync(nowMs));
        else if (!inWorld && keepalive.IsInWorld)
            // World-leave edge: 2/112 DISABLE; idle heartbeat stops. spec: world_exit.md §1.2.
            FireAndForget(keepalive.OnWorldExitedAsync());
        else
            // Steady state: fire 2/10000 after the idle interval (no-op until in-world / not yet idle).
            FireAndForget(keepalive.Tick(nowMs));
    }

    /// <summary>
    ///     Observes a fire-and-forget keepalive <see cref="ValueTask" /> so a faulted outbound send is
    ///     logged rather than left unobserved (which could surface on the finalizer). Runs on the Godot
    ///     main thread (called from <see cref="_Process" />).
    /// </summary>
    private static async void FireAndForget(ValueTask sendTask)
    {
        try
        {
            await sendTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] keepalive send faulted: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        // Signal the event channel so any async drainers stop cleanly.
        EventBus?.Complete();

        // Complete the HUD event hub so all widget channel-readers finish cleanly.
        // spec: MartialHeroes.Client.Application.Hud — IHudEventHub.Complete().
        HudEventHub?.Complete();

        // Tear down the live game connection (if one was opened).
        if (_gameConnection is { } conn)
        {
            _gameConnection = null;
            conn.DisconnectAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        // Cancel and DRAIN the engine loop task BEFORE disposing the cancellation source.
        // Disposing _loopCts while the loop task is still observing its token is a use-after-free
        // (the running RunAsync may touch the disposed CTS). Wait bounded so a stuck loop cannot
        // hang the editor/headless shutdown; the AggregateException wrapping the expected
        // OperationCanceledException is swallowed. spec: Docs/RE/specs/game_loop.md §6 (loop teardown).
        // Complete the inbound dispatcher channel so its reader drains queued frames and exits cleanly.
        Dispatcher?.Complete();

        _loopCts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
            _inboundTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Expected: the loop faulted/cancelled — the inner OperationCanceledException is benign.
        }

        // Drain the env-login harness task (_loopCts already cancelled above).
        // spec: Docs/RE/specs/login_flow.md §1 — harness shares _loopCts lifecycle.
        DrainEnvLogin();

        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
        _inboundTask = null;

        // Dispose the catalogue loader (releases the VFS memory-mapped archive).
        _catalogueLoader?.Dispose();
        _catalogueLoader = null;

        // Dispose the UI catalog assets handle (releases its VFS memory-mapped archive).
        // spec: Docs/RE/formats/ui_manifests.md §1 (UiTex.txt loaded via this handle).
        // spec: Docs/RE/formats/misc_data.md §6 (msg.xdb loaded via this handle).
        UiCatalogs?.Dispose();
        // Dispose the Ui/Scenes substrate libraries (share _uiAssets handle — dispose before it).
        // spec: Docs/RE/formats/ui_manifests.md §1 / Docs/RE/formats/msg_xdb.md.
        HudAtlas?.Dispose();
        HudText?.Dispose();
        _uiAssets?.Dispose();
        _uiAssets = null;

        // Dispose the buff icon catalog (clears texture cache; no archive owned).
        // spec: Docs/RE/formats/misc_data.md §1.3 / §1.6.
        BuffIconCatalog?.Dispose();

        // Dispose the terrain VFS archive (memory-mapped handle).
        // VfsTerrainSectorSource stores the reference but does NOT dispose — ownership is here.
        // spec: Docs/RE/formats/pak.md §Two-file scheme (MappedVfsArchive = memory-mapped handle).
        _terrainVfs?.Dispose();
        _terrainVfs = null;

        // Dispose the state-2 load resource pipeline (separate archive handle).
        // spec: Docs/RE/specs/resource_pipeline.md §1 / §2.
        _loadVfsPipeline?.Dispose();
        _loadVfsPipeline = null;

        GD.Print(
            "[ClientContext] EventBus completed. EngineLoop drained + stopped. CatalogueLoader + UiCatalogs + TerrainVfs disposed.");
    }

    /// <summary>
    ///     Called by InputRouter after it is ready: wires the world input handler into the bus.
    ///     spec: Docs/RE/specs/input_ui.md §3 — world handler registered after UI handler.
    /// </summary>
    public void SetWorldInputHandler(IInputHandler worldHandler)
    {
        _setWorldHandler?.Invoke(worldHandler);
    }

    /// <summary>
    ///     Called by GameLoop after GameHud is initialised: wires the live HUD hit-test so
    ///     pointer events over HUD panels are consumed before the world handler sees them.
    ///     Fixes F1: "UI is the gate" was dead because HudInputHandler was constructed with null.
    ///     spec: Docs/RE/specs/input_ui.md §3 / §6 — "UI hit-test always before world interaction".
    /// </summary>
    public void SetHudHitTest(Func<int, int, bool> hitTest)
    {
        _hudInputHandler?.SetHitTest(hitTest);
    }
}