using System.Net;
using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.Assets;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Input;
using MartialHeroes.Client.Infrastructure.Catalog;
using MartialHeroes.Client.Infrastructure.Lobby;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Abstractions.Transport;
using MartialHeroes.Network.Crypto;
using MartialHeroes.Network.Transport.Pipelines;

namespace MartialHeroes.Client.Godot.Autoload;

/// <summary>
/// Composition root / autoload singleton for the presentation layer.
///
/// Constructs the entire Client.Application object graph and exposes:
///   - <see cref="EventBus"/>: the ChannelReader the presentation drains each frame.
///   - <see cref="UseCases"/>: the use-case facade Godot nodes call for input intents.
///   - <see cref="Dispatcher"/>: the inbound frame dispatcher (allows synthetic test frames).
///   - <see cref="InputBus"/>: the input chain-of-responsibility bus (UI first, world second).
///   - <see cref="EngineLoop"/>: the fixed-tick 30 Hz simulation loop.
///   - <see cref="ItemCatalogue"/>: item definitions from data/script/items.scr, the runtime
///     master the shipping client loads (CP949 names). items.csv is an authoring/dev export the
///     shipping client never reads. spec: Docs/RE/formats/items_csv.md §6, items_scr.md §4.
///   - <see cref="SkillCatalogue"/>: skill definitions from skills.scr.
///   - <see cref="MobCatalogue"/>: mob definitions from mobs.scr.
///
/// Threading contract: this node is created once on the Godot main thread. The constructed
/// services are thread-safe (channel-based); Godot node mutation must still happen on the
/// main thread (enforced in GameLoop via CallDeferred).
///
/// VFS / assets:
///   The composition root resolves the VFS archive path via <see cref="MartialHeroes.Client.Godot.Dev.ClientPathResolver"/>:
///   env override (MH_CLIENT_DIR), then client_dir.cfg, then auto-detection of common paths.
///   The client requires the real VFS — a missing or unresolvable VFS is a hard failure.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — composition root.
/// spec: Docs/RE/specs/game_loop.md §6 — fixed-tick GameEngineLoop wired here.
/// spec: Docs/RE/specs/input_ui.md §6 — InputBus with UI-first handlers.
/// </summary>
public sealed partial class ClientContext : Node
{
    // -------------------------------------------------------------------------
    // Exposed Application surface
    // -------------------------------------------------------------------------

    /// <summary>The outbound UI event stream. Drain once per frame in GameLoop.</summary>
    public IClientEventBus EventBus { get; private set; } = null!;

    /// <summary>The use-case facade. Godot input nodes call these; never apply game logic here.</summary>
    public IApplicationUseCases UseCases { get; private set; } = null!;

    /// <summary>
    /// The inbound frame dispatcher. Used by <see cref="MartialHeroes.Client.Godot.Debug.SyntheticWorldFeeder"/>
    /// to inject synthetic Application events without touching any game rule.
    /// </summary>
    public InboundFrameDispatcher Dispatcher { get; private set; } = null!;

    /// <summary>
    /// The faithful 8-state scene machine — the SOLE state machine. Owns the live
    /// <see cref="MartialHeroes.Shared.Kernel.State.GameState"/>; the <see cref="SceneHost"/> listens
    /// for <see cref="MartialHeroes.Client.Application.Scene.SceneStateChangedEvent"/> and swaps the
    /// live scene to match. spec: Docs/RE/specs/client_runtime.md §7.
    /// </summary>
    public SceneStateMachine SceneMachine { get; private set; } = null!;

    /// <summary>
    /// Engine-free state-2 load worker/orchestrator. Godot renders its progress and calls into the
    /// scene spine only after this worker completes; transition policy remains in Application.
    /// spec: Docs/RE/specs/resource_pipeline.md §2; Docs/RE/specs/client_runtime.md §7.3.
    /// </summary>
    public LoadOrchestrator LoadOrchestrator { get; private set; } = null!;

    /// <summary>
    /// The input bus: UI handlers first, then world handler.
    /// spec: Docs/RE/specs/input_ui.md §3 / §6 — UI before world.
    /// </summary>
    public InputBus InputBus { get; private set; } = null!;

    /// <summary>
    /// The fixed-tick simulation loop (30 Hz). Runs on a background Task started in
    /// <see cref="_Ready"/>. Stopped in <see cref="_ExitTree"/>.
    /// spec: Docs/RE/specs/game_loop.md §6 ("Fixed-rate logic tick").
    /// </summary>
    public GameEngineLoop EngineLoop { get; private set; } = null!;

    // -------------------------------------------------------------------------
    // Catalogue surface (read-only; UI nodes call these for display names)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Item catalogue parsed from data/script/items.scr. Provides CP949-decoded item names
    /// and stats via <see cref="ItemCatalogue.TryGet"/>.
    /// spec: Docs/RE/formats/items_scr.md §4.
    /// </summary>
    public ItemCatalogue ItemCatalogue { get; private set; } = null!;

    /// <summary>
    /// Skill catalogue parsed from data/script/skills.scr. Provides skill definitions
    /// via <see cref="SkillCatalogue.TryGet"/>.
    /// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr.
    /// </summary>
    public SkillCatalogue SkillCatalogue { get; private set; } = null!;

    /// <summary>
    /// Mob catalogue parsed from data/script/mobs.scr. Provides mob records
    /// via <see cref="MobCatalogue.TryGet"/>.
    /// spec: Docs/RE/formats/config_tables.md §2.9 mobs.scr.
    /// </summary>
    public MobCatalogue MobCatalogue { get; private set; } = null!;

    /// <summary>
    /// The terrain streaming service. Call <see cref="SectorStreamingService.UpdateCenterAsync"/>
    /// whenever the local player's sector changes to stream the 3×3 ring of sectors.
    /// spec: Docs/RE/formats/terrain.md §9.2 (3×3 ring at StreamQuality.Medium).
    /// </summary>
    public SectorStreamingService StreamingService { get; private set; } = null!;

    /// <summary>
    /// The two startup UI data catalogs:
    ///   - UiTex manifest (data/ui/UiTex.txt)  → <see cref="UiCatalogs.GetTexture"/>
    ///   - Msg catalog (data/script/msg.xdb)   → <see cref="UiCatalogs.GetMessage"/>
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §1 (uitex.txt, PARSER-CONFIRMED grammar).
    /// spec: Docs/RE/formats/misc_data.md §6 (msg.xdb, CODE-CONFIRMED loader).
    /// spec: Docs/RE/specs/ui_system.md §8.5 (HUD uitex integer binding contract).
    /// </summary>
    public UiCatalogs UiCatalogs { get; private set; } = null!;

    /// <summary>
    /// Skill icon catalog: resolves per-skill 23×23 AtlasTextures from the active class-stance
    /// .do table and the skillicon.txt sheet manifest.
    ///
    /// Populated from:
    ///   - <c>data/ui/skillicon/skillicon.txt</c> (sheet DDS paths per (job,kind))
    ///   - <c>data/script/musajung.do</c> (Musa-jung demo stance; per-skill iconSrcX/Y)
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §2.6 (23×23 cell model: CODE-CONFIRMED).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 (.do record layout: CODE-CONFIRMED + SAMPLE-VERIFIED).
    /// </summary>
    public IconCatalogs IconCatalogs { get; private set; } = null!;

    /// <summary>
    /// Item icon catalog: resolves per-item <see cref="Godot.ImageTexture"/> values from
    /// <c>data/item/texturelist.txt</c> for display in the InventoryWindow.
    ///
    /// Each item icon is a whole-texture DDS blit — no atlas sub-rect.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §10 (texturelist.txt: CODE-CONFIRMED).
    /// spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
    /// </summary>
    public ItemIconCatalog ItemIconCatalog { get; private set; } = null!;

    /// <summary>
    /// The audio service façade. Handles BGM streaming, UI click SFX, world-entry SFX, and
    /// per-area ambient BGM from the .bgm sound table.
    ///
    /// Null when audio initialisation failed (headless / no audio device).
    /// Added as a child node in <see cref="_Ready"/> so it participates in the scene lifecycle.
    /// spec: Docs/RE/specs/sound.md §12.1 (Godot reimplementation guidance).
    /// spec: Docs/RE/names.yaml runtime_constants (UI_CLICK_SFX_ID, SPAWN_SFX_ID, ENTRY_BGM_CUE_ID).
    /// </summary>
    public AudioService? Audio { get; private set; }

    /// <summary>
    /// The HUD event hub: the single facade through which packet handlers publish per-frame
    /// HUD events (chat lines, buff states, combat texts, target changes, XP/level, stat views)
    /// and HUD widgets subscribe (one ChannelReader per family).
    ///
    /// Constructed once in <see cref="BuildApplicationGraph"/>; exposed as <see cref="IHudEventHub"/>
    /// so no consumer sees the mutable concrete type.
    ///
    /// spec: MartialHeroes.Client.Application.Hud — IHudEventHub / HudEventHub.
    /// </summary>
    public IHudEventHub HudEventHub { get; private set; } = null!;

    /// <summary>
    /// Buff icon catalog: resolves per-buff AtlasTextures from the shared stateicon.dds atlas,
    /// keyed by buff_id via buff_icon_position.xdb.
    ///
    /// spec: Docs/RE/formats/misc_data.md §1.3 (atlas path, record layout).
    /// spec: Docs/RE/formats/misc_data.md §1.6 (cell sizes 23/25 px, 30-slot bar).
    /// </summary>
    public BuffIconCatalog BuffIconCatalog { get; private set; } = null!;

    /// <summary>
    /// Zone catalog: resolves the display name of the zone (and optionally a nearby sub-zone
    /// label) for a given legacy world XZ position, sourced from mapsetting.scr.
    ///
    /// spec: Docs/RE/formats/misc_data.md §7.1 (mapsetting.scr — 52 zone records).
    /// spec: Docs/RE/specs/minimap.md §6.3 (zone names live in mapsetting.scr, not msg.xdb).
    /// </summary>
    public ZoneCatalog ZoneCatalog { get; private set; } = null!;

    /// <summary>
    /// Shared HUD atlas library for the new Ui/Scenes substrate.
    /// Maps VFS paths and uitex.txt tex_ids → Godot <see cref="Godot.Texture2D"/> objects,
    /// with AtlasTexture sub-rect slicing.
    ///
    /// Uses the same <c>_uiAssets</c> handle as <see cref="UiCatalogs"/>;
    /// no additional VFS archive is opened.
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §1 — uitex.txt grammar.
    /// spec: Docs/RE/specs/ui_system.md §1.3 — "atlas pixels map 1:1 to screen pixels".
    /// </summary>
    public MartialHeroes.Client.Godot.Ui.Assets.HudAtlasLibrary HudAtlas { get; private set; } = null!;

    /// <summary>
    /// Shared HUD text library for the new Ui/Scenes substrate.
    /// Loads data/script/msg.xdb and provides CP949-decoded caption lookup by integer id.
    ///
    /// Uses the same <c>_uiAssets</c> handle as <see cref="UiCatalogs"/>;
    /// no additional VFS archive is opened.
    ///
    /// spec: Docs/RE/formats/msg_xdb.md — 516-byte records, ascending unsigned id order.
    /// spec: Docs/RE/specs/ui_system.md §8 — notice column msg ids 4001–4022.
    /// </summary>
    public MartialHeroes.Client.Godot.Ui.Assets.HudTextLibrary HudText { get; private set; } = null!;

    /// <summary>
    /// The region service: resolves the local player's world position to a
    /// <see cref="MartialHeroes.Shared.Kernel.Enums.ZoneType"/> and publishes
    /// <see cref="MartialHeroes.Client.Application.Hud.ZoneChangedEvent"/> when the zone changes.
    ///
    /// Call <c>LoadAreaAsync</c> when the area is set, then <c>UpdatePosition</c> each frame.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16.
    /// </summary>
    public MartialHeroes.Client.Application.World.RegionService RegionService { get; private set; } = null!;

    /// <summary>
    /// The cell-assembly handoff: subscribes to <see cref="SectorLoadedEvent"/> and re-publishes
    /// assembled cells as <see cref="CellAssembledEvent"/>. The <see cref="GameLoop"/> drains this
    /// each frame via <see cref="CellAssemblyHandoff.OnSectorLoaded"/> on every received sector.
    ///
    /// Null when the terrain VFS could not be opened (offline mode).
    /// spec: Docs/RE/specs/assembly_graph.md §1/§4 — the handoff wires the AreaComposer byte→cell bake
    ///   into the streaming pipeline; the bake callback is bound here in the composition root.
    /// </summary>
    public CellAssemblyHandoff? CellAssemblyHandoff { get; private set; }

    /// <summary>
    /// The area-assembly handoff: composes the full area ONCE per area-enter and publishes it as an
    /// <see cref="AreaAssembledEvent"/> (carrying the spawn list so the composer-driven actor
    /// placement reaches layer-05). The layer-05 root (here) binds the area bake to
    /// <c>AreaComposer.ComposeArea</c>, projected onto the layer-04 <see cref="IAssembledAreaView"/>
    /// via <see cref="MartialHeroes.Client.Godot.Adapters.AssembledAreaViewAdapter"/>.
    ///
    /// CYCLE 2 Phase 2-A.5: closes the gap where <see cref="AreaAssembledEvent"/> was never
    /// published — the spawn list never reached layer-05 and <c>OnAreaAssembled</c> never fired.
    ///
    /// Call <see cref="AreaAssemblyHandoff.OnAreaBound"/> from
    /// <see cref="World.RealWorldRenderer.TriggerTerrainStreaming"/> alongside the existing
    /// <c>StreamingService.SetArea</c> + <c>AreaAssemblySource.SetArea</c> calls.
    ///
    /// Null when the terrain VFS could not be opened (offline mode).
    /// spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4.
    /// </summary>
    public AreaAssemblyHandoff? AreaAssemblyHandoff { get; private set; }

    /// <summary>
    /// The rebindable area assembly source used by the <see cref="CellAssemblyHandoff"/> bake callback.
    /// Call <see cref="MartialHeroes.Client.Godot.Adapters.RebindableAreaAssemblySource.SetArea"/>
    /// before triggering terrain streaming to ensure the AreaComposer builds paths for the correct area.
    ///
    /// Phase 2-B.1 fix: the original code hard-coded <c>areaId: 0</c> at construction time. This
    /// property exposes the rebindable wrapper so <see cref="World.RealWorldRenderer"/> can call
    /// <c>SetArea(TargetAreaId)</c> alongside <see cref="SectorStreamingService.SetArea"/> — both must
    /// be rebound to the same area before streaming starts.
    ///
    /// Null when the terrain VFS could not be opened (offline mode).
    /// spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives AreaComposer paths.
    /// spec: Docs/RE/formats/terrain.md §1.1 — areaId digit decomposition.
    /// </summary>
    public MartialHeroes.Client.Godot.Adapters.RebindableAreaAssemblySource? AreaAssemblySource { get; private set; }

    // Cancellation source for the engine loop Task.
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    // The late-binding outbound sink shared between ApplicationUseCases and LoginHandshakeDriver.
    // SetTarget is called in OpenGameConnectionAsync once the game TCP connection is established.
    private RelayOutboundPacketSink? _relaySink;

    // The live game connection session.  null until OpenGameConnectionAsync succeeds.
    // Guarded by a volatile bool to prevent double-open without taking a lock on the hot path.
    private IConnectionSession? _gameConnection;
    private volatile int _gameConnectionOpened; // 0 = not open, 1 = open (Interlocked flag)

    // VfsCatalogueLoader owns the VFS archive lifecycle for catalogue loading.
    // It is disposed in _ExitTree alongside the loop cancellation.
    private VfsCatalogueLoader? _catalogueLoader;

    // RealClientAssets for the UI catalogs (UiTex + msg.xdb).  Disposed in _ExitTree.
    // Kept separate from the terrain VFS so the two archives have independent lifecycles.
    private RealClientAssets? _uiAssets;

    // The MappedVfsArchive opened by OpenVfsForTerrain() and passed into VfsTerrainSectorSource.
    // VfsTerrainSectorSource stores the reference but does NOT implement IDisposable and therefore
    // does NOT dispose the archive — ownership stays here. Disposed in _ExitTree.
    // spec: Docs/RE/formats/pak.md §Two-file scheme (MappedVfsArchive = memory-mapped handle).
    private MappedVfsArchive? _terrainVfs;

    // VFS resource pipeline used by the state-2 LoadOrchestrator. Disposed in _ExitTree.
    // spec: Docs/RE/specs/resource_pipeline.md §1 / §2.
    private VfsResourcePipeline? _loadVfsPipeline;

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
    }

    /// <summary>
    /// Builds the full Application object graph.
    /// </summary>
    private void BuildApplicationGraph()
    {
        // 1. Event bus — bounded 1024 capacity, DropOldest backpressure.
        //    spec: ClientEventBus default policy.
        var bus = new ClientEventBus(ClientEventBus.DefaultCapacity);

        // 2. Faithful 8-state scene machine — the sole state machine. Boots at state 0 (Init).
        //    spec: Docs/RE/specs/client_runtime.md §7.1.
        var sceneMachine = new SceneStateMachine(bus);

        // 2c. State-2 load orchestrator: engine-free load worker, OPENNING/SKIP reader, and
        //     loading cue sink. The presentation only observes progress/completion.
        //     spec: Docs/RE/specs/resource_pipeline.md §2; client_runtime.md §7.3.
        _loadVfsPipeline = MountLoadResourcePipeline();
        var loadOrchestrator = new LoadOrchestrator(
            sceneMachine,
            new VfsLoadResourceSource(_loadVfsPipeline),
            new OpeningSkipIniReader(ResolveOpeningSkipCfgPath()),
            new GodotLoadingSoundSink());

        // 3. Domain world registry.
        var world = new ClientWorld();

        // 4. Unhandled opcode sink — count-only for now.
        IUnhandledOpcodeSink opcodeSink = new CountingUnhandledOpcodeSink();

        // 5. Relay outbound sink: initially no-op; SetTarget wires the live crypto sink in
        //    OpenGameConnectionAsync once the TCP game connection is established.
        //    Both ApplicationUseCases and LoginHandshakeDriver share the SAME instance so the
        //    secure 1/4 reply goes to the real socket once the connection is open.
        var relaySink = new RelayOutboundPacketSink();
        _relaySink = relaySink;
        IOutboundPacketSink noopSink = relaySink; // alias — ApplicationUseCases receives the relay

        // 6. Session id — starts as None; updated when the game connection is opened.
        SessionId sessionId = SessionId.None;

        // 7. Login credential store.
        //    spec: Docs/RE/specs/crypto.md §6.1.
        var credentialStore = new LoginCredentialStore();

        // 8. Login handshake driver — answers the inbound 0/0 KeyExchange with the secure 1/4 Auth
        //    reply carrying the staged credential (account + optional PIN pre-image + RSA(M)). It shares
        //    the SAME relay sink as ApplicationUseCases; the reply reaches the live socket once
        //    OpenGameConnectionAsync installs the crypto sink. The credential store and session id are
        //    the same instances handed to ApplicationUseCases.
        //    spec: Docs/RE/specs/crypto.md §6.1/§6.6; packets/login.yaml (CmsgLoginCredential).
        ILoginHandshakeDriver loginDriver =
            new LoginHandshakeDriver(relaySink, credentialStore, sessionId);

        // 9. InputBus — UI handler first, world handler wired after InputRouter is created.
        //    The HudInputHandler starts as a pass-through (hitTest: null); the live GameHud.HitTest
        //    is wired later via SetHudHitTest() once GameLoop initialises the HUD node.
        //    F1 fix: store in _hudInputHandler so SetHudHitTest can reach it.
        //    spec: Docs/RE/specs/input_ui.md §3 / §6 (UI before world).
        var hudHandler = new HudInputHandler(hitTest: null);
        _hudInputHandler = hudHandler; // F1: stored for late SetHitTest wiring. spec: input_ui.md §3/§6.

        // The world handler is created by InputRouter, so we build InputBus with only the
        // HUD handler for now; the world handler is appended via a late-binding relay.
        // spec: Docs/RE/specs/input_ui.md §3 — UI before world.
        var worldRelay = new RelayInputHandler(); // placeholder; InputRouter sets the target.
        var inputBus = new InputBus(hudHandler, worldRelay);

        // 10. VFS catalogue loader — resolved via ClientPathResolver (config file / env / auto-detect).
        //     Used for item/skill/mob/stat catalogues (displayed by HUD).
        //     spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules (never hardcode path).
        _catalogueLoader = BuildCatalogueLoader();

        // 11. Real stat catalogue — from userlevel.scr via VfsCatalogueLoader.
        //     spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr.
        ScrStatCatalogue scrStatCatalogue = ScrStatCatalogue.FromLoader(_catalogueLoader);
        GD.Print(
            $"[ClientContext] ScrStatCatalogue loaded (HP curve entries={scrStatCatalogue.GetHpBaseCurve().Count}, " +
            $"MP curve entries={scrStatCatalogue.GetMpBaseCurve().Count}).");

        // 12. Catalogue items / skills / mobs for UI display names (CP949).
        //     Items come from the runtime master data/script/items.scr (NOT items.csv).
        //     spec: Docs/RE/formats/items_scr.md §4 + config_tables.md §2.8 / §2.9.
        ItemCatalogue = ItemCatalogue.FromLoader(_catalogueLoader);
        SkillCatalogue = SkillCatalogue.FromLoader(_catalogueLoader);
        MobCatalogue = MobCatalogue.FromLoader(_catalogueLoader);
        GD.Print($"[ClientContext] Catalogues loaded: {ItemCatalogue.Count} items, " +
                 $"{SkillCatalogue.Count} skills, {MobCatalogue.Count} mobs.");

        // 13. UI data catalogs: uitex manifest (data/ui/UiTex.txt) and msg.xdb string catalog.
        //     Opens a second RealClientAssets handle; lazy texture loading stays on the main thread.
        //     spec: Docs/RE/formats/ui_manifests.md §1 / misc_data.md §6 / ui_system.md §8.5.
        _uiAssets = RealClientAssets.TryOpen();
        UiCatalogs = new UiCatalogs(_uiAssets);
        GD.Print($"[ClientContext] UiCatalogs: {UiCatalogs.UiTexEntryCount} uitex entries, " +
                 $"{UiCatalogs.MsgRecordCount} msg records.");

        // 13-B. HUD atlas + text libraries for the new Ui/Scenes substrate.
        //     Reuse the same _uiAssets handle; no additional VFS archive opened.
        //     spec: Docs/RE/formats/ui_manifests.md §1 / Docs/RE/formats/msg_xdb.md.
        HudAtlas = new MartialHeroes.Client.Godot.Ui.Assets.HudAtlasLibrary(_uiAssets);
        HudText = new MartialHeroes.Client.Godot.Ui.Assets.HudTextLibrary(_uiAssets);
        GD.Print("[ClientContext] HudAtlasLibrary + HudTextLibrary constructed (Ui/Scenes substrate).");

        // 14. Skill icon catalog: skillicon.txt + musajung.do stance table (lazy).
        //     spec: Docs/RE/formats/ui_manifests.md §2.6 / §2.7.
        IconCatalogs = new IconCatalogs(_uiAssets);
        GD.Print($"[ClientContext] IconCatalogs: {IconCatalogs.DoRecordCount} musajung.do records. " +
                 "spec: Docs/RE/formats/ui_manifests.md §2.7.");

        // 15. Item icon catalog: data/item/texturelist.txt (lazy, whole-texture blit).
        //     spec: Docs/RE/formats/ui_manifests.md §10 / §10.5.
        ItemIconCatalog = new ItemIconCatalog(_uiAssets);
        GD.Print($"[ClientContext] ItemIconCatalog: {ItemIconCatalog.ManifestCount} texturelist.txt entries. " +
                 "spec: Docs/RE/formats/ui_manifests.md §10.");

        // 16-A. HUD event hub: the single facade for all per-frame HUD channels.
        //     Constructed once here; widgets subscribe via IHudEventHub.
        //     spec: MartialHeroes.Client.Application.Hud — IHudEventHub / HudEventHub.
        var hudHub = new HudEventHub();
        HudEventHub = hudHub;
        GD.Print("[ClientContext] HudEventHub constructed (6 typed channels).");

        // 16-B. Buff icon catalog (stateicon.dds + buff_icon_position.xdb).
        //     spec: Docs/RE/formats/misc_data.md §1.3 / §1.6.
        BuffIconCatalog = new BuffIconCatalog(_uiAssets);
        GD.Print($"[ClientContext] BuffIconCatalog initialised ({BuffIconCatalog.TableCount} entries).");

        // 16-C. Zone catalog (mapsetting.scr — 52 zone records, CP949 names).
        //     spec: Docs/RE/formats/misc_data.md §7.1 / Docs/RE/specs/minimap.md §6.3.
        ZoneCatalog = new ZoneCatalog(RealClientAssets.TryOpen());
        GD.Print($"[ClientContext] ZoneCatalog initialised ({ZoneCatalog.AllZones.Count} zone records.");

        // 16. VFS archive for terrain (separate from catalogue loader — same paths).
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        //     Store in _terrainVfs so _ExitTree can dispose it (VfsTerrainSectorSource does not own/dispose).
        MappedVfsArchive vfs = OpenVfsForTerrain();
        _terrainVfs = vfs;

        // 16-D. Region service (Ch. 16 — 256-unit PvP/safe/closed zone grid).
        //     spec: Docs/RE/specs/world_systems.md Ch. 16.
        var regionSource = new MartialHeroes.Client.Godot.Adapters.VfsRegionSource(vfs);
        RegionService = new MartialHeroes.Client.Application.World.RegionService(regionSource, hudHub);
        GD.Print("[ClientContext] RegionService constructed. spec: Docs/RE/specs/world_systems.md Ch. 16.");

        // 16-E. AreaComposer + CellAssemblyHandoff — Phase 6a composition seam.
        //   The AreaComposer lives in layer-03 (Assets.Mapping); the CellAssemblyHandoff is the
        //   layer-04 seam that bridges SectorLoadedEvent → CellAssembledEvent. The bake callback
        //   captures the VfsAreaAssemblySource (a layer-05 adapter) and calls AreaComposer.ComposeCell.
        //   The resulting AssembledCell is wrapped in a thin IAssembledCellView adapter so the layer-04
        //   CellAssemblyHandoff can publish it without importing Assets.Mapping.
        //   spec: Docs/RE/specs/assembly_graph.md §1/§4 — AreaComposer + CellAssemblyHandoff contract.
        try
        {
            // Phase 2-B.1 fix: use RebindableAreaAssemblySource (starts at area 0; rebound to
            // the configured area by RealWorldRenderer.TriggerTerrainStreaming via
            // ctx.AreaAssemblySource.SetArea(TargetAreaId) before streaming starts).
            // This ensures AreaComposer.ComposeCell builds data/map<NNN>/dat/... paths for the
            // ACTIVE area rather than always using area 0 (map000).
            // spec: Docs/RE/specs/assembly_graph.md §1/§4 — IAreaAssemblySource drives cell paths.
            // spec: Docs/RE/formats/terrain.md §1.1 — areaId digit decomposition.
            var areaSource =
                new MartialHeroes.Client.Godot.Adapters.RebindableAreaAssemblySource(vfs, initialAreaId: 0);
            AreaAssemblySource = areaSource; // expose for RealWorldRenderer to call SetArea.

            var areaComposer = new global::MartialHeroes.Assets.Mapping.AreaComposer();

            CellAssemblyHandoff = new CellAssemblyHandoff(bus,
                (mapX, mapZ, _) =>
                {
                    // Payload bytes from SectorLoadedEvent are the .ted bytes already loaded by
                    // VfsTerrainSectorSource. The AreaComposer re-reads all cell files via the
                    // RebindableAreaAssemblySource (which has full VFS access and is rebound to the
                    // active area before streaming starts). The payload is unused here.
                    // spec: Docs/RE/specs/assembly_graph.md §4 — bake callback is pure/deterministic.
                    try
                    {
                        var cell = areaComposer.ComposeCell(areaSource, mapX, mapZ);
                        return new AssembledCellViewAdapter(cell);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[ClientContext] AreaComposer.ComposeCell({mapX},{mapZ}) failed: {ex.Message}");
                        return null;
                    }
                });

            GD.Print(
                "[ClientContext] AreaComposer + CellAssemblyHandoff wired (RebindableAreaAssemblySource). spec: assembly_graph.md §1/§4.");

            // CYCLE 2 Phase 2-A.5 — AreaAssemblyHandoff.
            // Binds the area bake to AreaComposer.ComposeArea(areaSource) and wraps the result in
            // AssembledAreaViewAdapter so the layer-04 seam never references Assets.Mapping.
            // AreaAssemblyHandoff.OnAreaBound is called from RealWorldRenderer.TriggerTerrainStreaming
            // alongside SetArea() — exactly once per area-enter (idempotent on re-bind).
            // The bake callback re-uses the SAME areaComposer + areaSource instances already captured
            // by the CellBake delegate above, so no second VFS handle is opened.
            // spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — area load → spawns) / §4.
            AreaAssemblyHandoff = new AreaAssemblyHandoff(bus,
                areaId =>
                {
                    try
                    {
                        // Ensure the assembly source is bound to this area before composing.
                        // RealWorldRenderer.TriggerTerrainStreaming also calls areaSource.SetArea,
                        // but we defensively call it here too so the bake is always area-coherent.
                        // spec: Docs/RE/formats/terrain.md §1.1 (per-area path tag). CONFIRMED.
                        areaSource.SetArea(areaId);
                        var area = areaComposer.ComposeArea(areaSource);
                        return new MartialHeroes.Client.Godot.Adapters.AssembledAreaViewAdapter(area);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[ClientContext] AreaComposer.ComposeArea(area={areaId}) failed: {ex.Message}");
                        return null; // unresolved area → nothing published. spec: assembly_graph.md §1.
                    }
                });

            GD.Print("[ClientContext] AreaAssemblyHandoff wired (CYCLE 2 Phase 2-A.5). spec: assembly_graph.md §1/§4.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] CellAssemblyHandoff wiring failed (non-fatal): {ex.Message}");
            CellAssemblyHandoff = null;
            AreaAssemblySource = null;
            AreaAssemblyHandoff = null;
        }

        // 17. Terrain sector source — backed by VFS.
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        //     Area 0 is the default starting area. TODO: update on enter-game.
        var terrainSource = new VfsTerrainSectorSource(vfs, areaId: 0);

        // 18. Terrain streaming service — high quality (5×5 ring, radius=2).
        //     spec: Docs/RE/formats/terrain.md §12.2 — High quality → 5×5 ring (ring-radius cells = 5).
        //     The 5×5 ring loads up to 25 sectors around the player, covering all spawn-filled cells
        //     in large walled areas (e.g. area 2) that exceed the 3×3 ring footprint.
        var streamingService = new SectorStreamingService(terrainSource, bus, StreamQuality.High);

        // 17. Packet handler — orchestrates Domain mutation and event publishing.
        //     Wire the catalogue vitals resolver (real stat curves) at construction.
        //     spec: CatalogueVitalsResolver.Create — builds the seam from the catalogue.
        //     spec: Docs/RE/formats/config_tables.md §2.4.
        var handler = new GamePacketHandler(world, bus, opcodeSink, loginDriver, sceneStateMachine: sceneMachine)
        {
            VitalsResolver = CatalogueVitalsResolver.Create(scrStatCatalogue)
        };

        // 18. Inbound frame dispatcher — channel-backed; synthetic feeder uses this.
        var dispatcher = new InboundFrameDispatcher(handler);

        // 19. Lobby stack: host resolver → lobby client + last-server store.
        //     These are constructed here (online mode) so that FetchServerListAsync / SelectServerAsync
        //     work in ApplicationUseCases.  All three degrade gracefully when the client dir is absent.
        //     spec: Docs/RE/specs/login_flow.md §2.0 — three-tier host resolution (ip.txt → list.dat → fallback).
        string? clientDirForLobby = ClientPathResolver.ResolveClientDir();
        var lobbyHostResolver = new LobbyHostResolver(clientDirForLobby);
        string lobbyHost = lobbyHostResolver.Resolve();
        GD.Print($"[ClientContext] Lobby host resolved to '{lobbyHost}'. spec: login_flow.md §2.0.");

        var lobbyClient = new LobbyClient(lobbyHost, PayloadCompression.DecompressPayload);
        var lastServerStore = new RegistryLastServerStore();

        // 19b. Version token — pass `default` (empty span) so ApplicationUseCases derives it via
        //     DefaultClientVersionSource.Instance: token = 10 × 2114 + 9 = 21149 (sample_verified).
        //     An explicit zero-filled span would OVERRIDE the derivation to all-zeros. Passing default
        //     activates the branch that calls ClientVersionToken.Derive() and stamps "21149\0" into the
        //     33-byte buffer. spec: Docs/RE/specs/login_flow.md §3.3 / §7.
        //     spec: Docs/RE/packets/1-9_enter_game_request.yaml (VersionToken 0x01, 33 bytes).
        ReadOnlySpan<byte> versionToken = default; // empty → derives via IClientVersionSource

        // 20. Use-case facade — presentation calls these for input intents.
        //     versionSource = null → DefaultClientVersionSource.Instance → field 2114 → token 21149.
        //     spec: Docs/RE/specs/login_flow.md §3.3 / §7 (token = 10 × versionField + 9 = 21149).
        //     eventBus, lobbyClient, lastServerStore wired so FetchServerListAsync / SelectServerAsync
        //     publish ServerListReceivedEvent and persist Lastserver.
        var useCases = new ApplicationUseCases(noopSink, world, credentialStore, sessionId,
            versionToken: versionToken, versionSource: null, sceneStateMachine: sceneMachine,
            eventBus: bus, lobbyClient: lobbyClient, lastServerStore: lastServerStore);
        GD.Print(
            $"[ClientContext] Version token derived: {ClientVersionToken.Derive(DefaultClientVersionSource.Instance.VersionField)}" +
            " (= 10 × 2114 + 9; sample_verified). spec: login_flow.md §3.3 / §7.");

        // 21. Fixed-tick GameEngineLoop — 30 Hz.
        //     spec: Docs/RE/specs/game_loop.md §6 ("e.g. 30 Hz via a PeriodicTimer"). CONFIRMED.
        var engineLoop = new GameEngineLoop(world, bus, inputBus, GameEngineLoop.DefaultTickRateHz);

        // Publish to fields.
        EventBus = bus;
        UseCases = useCases;
        Dispatcher = dispatcher;
        SceneMachine = sceneMachine;
        LoadOrchestrator = loadOrchestrator;
        InputBus = inputBus;
        EngineLoop = engineLoop;
        StreamingService = streamingService;

        // Store the relay setter so InputRouter can set the real handler later.
        _setWorldHandler = worldRelay.SetTarget;

        // Start the fixed-tick loop on a background task.
        // spec: Docs/RE/specs/game_loop.md §6 — "Fixed-rate logic tick … via a PeriodicTimer".
        _loopCts = new CancellationTokenSource();
        PeriodicGameClock clock = engineLoop.CreateRealtimeClock();

        // Store the RAW loop task so _ExitTree can drain it (wait for the loop to actually exit)
        // before disposing the CancellationTokenSource — avoiding a use-after-free of _loopCts.
        Task rawLoop = engineLoop.RunAsync(clock, _loopCts.Token);
        _loopTask = rawLoop;

        // Observe background faults so they do NOT silently kill the process. The continuation runs
        // only if the loop faults, logs, and does NOT rethrow (fire-and-forget; not the drain handle).
        _ = rawLoop.ContinueWith(
            t => GD.PrintErr($"[ClientContext] EngineLoop faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        GD.Print("[ClientContext] Application graph constructed. EventBus ready. EngineLoop started at 30 Hz.");
    }

    // Stored so InputRouter can wire the world handler after initialisation.
    // Typed as Action<IInputHandler> to avoid exposing the file-local type in the member signature.
    private Action<IInputHandler>? _setWorldHandler;

    // Stored so GameLoop can wire the live GameHud.HitTest after the HUD node is initialised.
    // This is the fix for F1: "UI is the gate" hit-test is never wired.
    // spec: Docs/RE/specs/input_ui.md §3 / §6.
    private HudInputHandler? _hudInputHandler;

    /// <summary>
    /// Called by InputRouter after it is ready: wires the world input handler into the bus.
    /// spec: Docs/RE/specs/input_ui.md §3 — world handler registered after UI handler.
    /// </summary>
    public void SetWorldInputHandler(IInputHandler worldHandler)
    {
        _setWorldHandler?.Invoke(worldHandler);
    }

    /// <summary>
    /// Called by GameLoop after GameHud is initialised: wires the live HUD hit-test so
    /// pointer events over HUD panels are consumed before the world handler sees them.
    /// Fixes F1: "UI is the gate" was dead because HudInputHandler was constructed with null.
    /// spec: Docs/RE/specs/input_ui.md §3 / §6 — "UI hit-test always before world interaction".
    /// </summary>
    public void SetHudHitTest(Func<int, int, bool> hitTest)
    {
        _hudInputHandler?.SetHitTest(hitTest);
    }

    /// <summary>
    /// Opens the TCP game connection, installs a <see cref="CryptoOutboundPacketSink"/> over it,
    /// and activates the relay sink so subsequent <see cref="IOutboundPacketSink.SendAsync"/> calls
    /// go to the real wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent: a second call while the connection is already open is a no-op.
    /// </para>
    /// <para>
    /// Called by <c>LoginScene.SelectServerAsync</c> once the lobby has resolved the game-server
    /// host:port via <c>SelectServerAsync</c>.
    /// </para>
    /// <para>
    /// The inbound path: <c>TcpTransport</c> is constructed with a <see cref="DispatcherFrameSink"/>
    /// adapter that enqueues each decoded frame into the existing <see cref="Dispatcher"/> channel,
    /// which the engine loop already drains.  No second reader is added to the event bus.
    /// </para>
    /// <para>
    /// Decompression: <see cref="PayloadCompression.DecompressPayload"/> is injected as the
    /// <c>InboundDecompressDelegate</c>.
    /// spec: Docs/RE/specs/crypto.md §5 — inbound is LZ4-decompress only (no inverse cipher).
    /// </para>
    /// </remarks>
    /// <param name="host">Dotted IPv4 literal or DNS hostname returned by the lobby.</param>
    /// <param name="port">Game-server port returned by the lobby.</param>
    public async Task OpenGameConnectionAsync(string host, int port)
    {
        // Idempotency guard — only the first caller proceeds.
        if (Interlocked.CompareExchange(ref _gameConnectionOpened, 1, 0) != 0)
        {
            GD.Print($"[ClientContext] OpenGameConnectionAsync({host}:{port}): connection already open — skipped.");
            return;
        }

        try
        {
            // Resolve host (may be a name or dotted quad).
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                GD.PrintErr(
                    $"[ClientContext] OpenGameConnectionAsync: DNS resolution returned no addresses for '{host}'.");
                Interlocked.Exchange(ref _gameConnectionOpened, 0); // allow a retry
                return;
            }

            var endpoint = new EndpointDescriptor(
                new IPEndPoint(addresses[0], port),
                DisplayName: $"game-server({host}:{port})");

            // Build the inbound frame sink that feeds the existing Dispatcher channel.
            // DispatcherFrameSink is a file-local adapter defined at the bottom of this file.
            var frameSink = new DispatcherFrameSink(Dispatcher);

            // TcpTransport → ConnectAsync → SocketConnection (receive + frame loops start immediately).
            // spec: Docs/RE/specs/crypto.md §5 — inbound = LZ4 decompress only; no inverse cipher.
            var transport = new TcpTransport(frameSink, PayloadCompression.DecompressPayload);
            IConnectionSession session = await transport
                .ConnectAsync(endpoint, CancellationToken.None)
                .ConfigureAwait(false);

            _gameConnection = session;

            // Install the crypto outbound sink and activate the relay.
            // spec: Docs/RE/specs/crypto.md §3.1 / §3.2 — outbound: cipher in-place then LZ4 compress.
            var cryptoSink = new CryptoOutboundPacketSink(
                session,
                WireCipher.EncryptInPlace,
                PayloadCompression.CompressPayload);

            _relaySink?.SetTarget(cryptoSink);

            GD.Print($"[ClientContext] Game connection OPEN to {endpoint}. Outbound relay active. " +
                     "spec: crypto.md §3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] OpenGameConnectionAsync({host}:{port}) FAILED: {ex.Message}");
            Interlocked.Exchange(ref _gameConnectionOpened, 0); // allow a retry on the next server select
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
        _loopCts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Expected: the loop faulted/cancelled — the inner OperationCanceledException is benign.
        }

        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;

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
        // Dispose the skill icon catalog (no archive owned — just clears internal state).
        // spec: Docs/RE/formats/ui_manifests.md §2.6 / §2.7.
        IconCatalogs?.Dispose();
        // Dispose the item icon catalog (clears texture cache; no archive owned).
        // spec: Docs/RE/formats/ui_manifests.md §10 / §10.5.
        ItemIconCatalog?.Dispose();
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

    // -------------------------------------------------------------------------
    // VFS / catalogue helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="VfsCatalogueLoader"/> using <see cref="ClientPathResolver.ResolveClientDir"/>.
    /// Throws <see cref="InvalidOperationException"/> when no valid client directory is found;
    /// the client requires the real VFS.
    ///
    /// Path resolution is delegated entirely to <see cref="ClientPathResolver"/> (env-var override,
    /// then client_dir.cfg, then auto-detection). No direct environment-variable read here.
    ///
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules (user supplies originals).
    /// spec: Docs/RE/formats/pak.md §Two-file scheme.
    /// </summary>
    private static VfsCatalogueLoader BuildCatalogueLoader()
    {
        string clientDir = ClientPathResolver.ResolveClientDir()
                           ?? throw new InvalidOperationException(
                               "[ClientContext] No VFS client directory found. " +
                               "Set MH_CLIENT_DIR, create client_dir.cfg, or install the client at a known path.");

        string infPath = Path.Combine(clientDir, "data.inf");
        string vfsPath = Path.Combine(clientDir, "data", "data.vfs");
        GD.Print($"[ClientContext] CatalogueLoader: using resolved client dir '{clientDir}'.");
        return new VfsCatalogueLoader(infPath, vfsPath);
    }

    /// <summary>
    /// Opens the VFS archive for terrain sector streaming using <see cref="ClientPathResolver.ResolveClientDir"/>.
    /// Throws when no valid client directory is found; the client requires the real VFS.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
    /// </summary>
    private static MappedVfsArchive OpenVfsForTerrain()
    {
        string clientDir = ClientPathResolver.ResolveClientDir()
                           ?? throw new InvalidOperationException(
                               "[ClientContext] No VFS client directory found — cannot open terrain VFS. " +
                               "Set MH_CLIENT_DIR, create client_dir.cfg, or install the client at a known path.");

        string infPath = Path.Combine(clientDir, "data.inf");
        string vfsPath = Path.Combine(clientDir, "data", "data.vfs");
        MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
        GD.Print($"[ClientContext] Terrain VFS opened from '{clientDir}' ({archive.EntryCount} entries).");
        return archive;
    }

    private static VfsResourcePipeline MountLoadResourcePipeline()
    {
        string clientDir = ClientPathResolver.ResolveClientDir()
                           ?? throw new InvalidOperationException(
                               "[ClientContext] No VFS client directory found — cannot mount load resource pipeline. " +
                               "Set MH_CLIENT_DIR, create client_dir.cfg, or install the client at a known path.");

        string infPath = Path.Combine(clientDir, "data.inf");
        string vfsPath = Path.Combine(clientDir, "data", "data.vfs");
        VfsResourcePipeline pipeline = VfsResourcePipeline.Mount(infPath, vfsPath);
        pipeline.TrackingEnabled = true;
        GD.Print($"[ClientContext] Load VFS pipeline mounted from '{clientDir}'. spec: resource_pipeline.md §2.");
        return pipeline;
    }

    private static string ResolveOpeningSkipCfgPath()
    {
        try
        {
            // spec: Docs/RE/specs/resource_pipeline.md §2.5 — OPENNING/SKIP is read from the
            // per-account/config-singleton INI path, not the dev VFS locator client_dir.cfg.
            return ProjectSettings.GlobalizePath(MartialHeroes.Client.Godot.Ui.Scenes.Opening.OpeningWindow
                .SkipCfgPath);
        }
        catch
        {
            return "options.cfg";
        }
    }
}

// -------------------------------------------------------------------------
// File-local helpers
// -------------------------------------------------------------------------

/// <summary>
/// A late-binding <see cref="IInputHandler"/> relay whose target can be set after construction.
/// Used to defer the world input handler assignment until InputRouter is initialised.
/// spec: Docs/RE/specs/input_ui.md §3 — world handler registered after UI in the bus.
/// </summary>
file sealed class RelayInputHandler : IInputHandler
{
    private volatile IInputHandler? _target;

    /// <summary>Sets the delegate handler. Thread-safe (volatile write).</summary>
    public void SetTarget(IInputHandler target) => _target = target;

    /// <inheritdoc />
    public bool TryHandle(in MartialHeroes.Client.Application.Input.InputEvent e)
        => _target?.TryHandle(in e) ?? false;
}

/// <summary>
/// Adapter that bridges the transport-layer <see cref="IFrameSink"/> contract to the
/// application-layer <see cref="InboundFrameDispatcher.Enqueue"/> method.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InboundFrameDispatcher"/> does not implement <see cref="IFrameSink"/> directly
/// because it operates on a full frame (header + payload) while <see cref="IFrameSink.OnFrame"/>
/// receives only the payload with the opcode separated. This adapter packs the opcode back into an
/// 8-byte wire header ahead of the payload before enqueuing, which is what
/// <see cref="InboundFrameDispatcher.Enqueue"/> expects (the channel contains full frames).
/// </para>
/// <para>
/// The packed header written here is the same 8-byte layout the FrameSplitter already parsed:
///   +0 u32 LE totalSize, +4 u16 LE major, +6 u16 LE minor.
/// spec: Docs/RE/specs/crypto.md §2 — wire frame header layout.
/// </para>
/// </remarks>
file sealed class DispatcherFrameSink : IFrameSink
{
    private readonly InboundFrameDispatcher _dispatcher;

    public DispatcherFrameSink(InboundFrameDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public void OnFrame(SessionId sessionId, uint packedOpcode, ReadOnlySpan<byte> payload)
    {
        // Reconstruct the 8-byte header + payload frame that InboundFrameDispatcher.Enqueue expects.
        // spec: Docs/RE/specs/crypto.md §2 — +0 u32 LE size (total incl. header), +4 u16 major, +6 u16 minor.
        int totalSize = 8 + payload.Length; // spec: crypto.md §2 — size includes the 8-byte header
        Span<byte> frame = stackalloc byte[totalSize <= 256 ? totalSize : 0]; // stack for small frames
        byte[]? heapFrame = totalSize > 256 ? new byte[totalSize] : null;
        Span<byte> dest = heapFrame is not null ? heapFrame.AsSpan() : frame;

        // +0: u32 LE total size. spec: Docs/RE/specs/crypto.md §2 [CODE-CONFIRMED u32].
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dest[0..], (uint)totalSize);
        // +4: u16 LE major opcode. spec: Docs/RE/opcodes.md.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], (ushort)(packedOpcode >> 16));
        // +6: u16 LE minor opcode. spec: Docs/RE/opcodes.md.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(dest[6..], (ushort)(packedOpcode & 0xFFFF));
        if (!payload.IsEmpty)
            payload.CopyTo(dest[8..]);

        _dispatcher.Enqueue(dest);
    }
}

file sealed class VfsLoadResourceSource : ILoadResourceSource
{
    private readonly VfsResourcePipeline? _pipeline;

    public VfsLoadResourceSource(VfsResourcePipeline? pipeline)
    {
        _pipeline = pipeline;
    }

    public ValueTask<long> LoadAsync(string logicalPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pipeline is null)
        {
            return ValueTask.FromResult(0L);
        }

        try
        {
            ReadOnlyMemory<byte> bytes = _pipeline.OpenRead(logicalPath);
            return ValueTask.FromResult((long)bytes.Length);
        }
        catch (Exception ex)
        {
            GD.Print($"[ClientContext] Load resource skipped '{logicalPath}': {ex.Message}");
            return ValueTask.FromResult(0L);
        }
    }
}

// Note: AssembledCellViewAdapter (the public layer-05 adapter used by the bake delegate below) is
// defined in Adapters/AssembledCellViewAdapter.cs. It was moved out of this file in CYCLE 2 Phase 2-A
// so that RealWorldRenderer can down-cast to the concrete type for 9-slot access without reflection.
// spec: Docs/RE/specs/assembly_graph.md §4 — layer-05 composition root adapts AssembledCell as IAssembledCellView.

file sealed class GodotLoadingSoundSink : ILoadingSoundSink
{
    public void PlayLooping(int soundCueId)
    {
        // Loading cue 920100100 is a category-0 looping BGM. Route through AudioService's BGM slot
        // so the next front-end BGM replaces it cleanly.
        // spec: Docs/RE/specs/sound.md §15.6a; frontend_scenes.md §9.1.
        if (AudioService.Instance is { } audio)
        {
            audio.CallDeferred(AudioService.MethodName.StartBgm, (uint)soundCueId);
            GD.Print($"[ClientContext] Loading sound sink requested looping cue {soundCueId}.");
            return;
        }

        GD.Print($"[ClientContext] Loading sound sink: AudioService unavailable; cue {soundCueId} skipped.");
    }
}