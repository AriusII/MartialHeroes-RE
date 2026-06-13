using Godot;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Input;
using MartialHeroes.Client.Infrastructure.Catalog;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

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
///   - <see cref="ItemCatalogue"/>: item definitions from items.csv (CP949 names).
///   - <see cref="SkillCatalogue"/>: skill definitions from skills.scr.
///   - <see cref="MobCatalogue"/>: mob definitions from mobs.scr.
///
/// Threading contract: this node is created once on the Godot main thread. The constructed
/// services are thread-safe (channel-based); Godot node mutation must still happen on the
/// main thread (enforced in GameLoop via CallDeferred).
///
/// VFS / assets:
///   The composition root resolves the VFS archive path via <see cref="MartialHeroes.Client.Godot.Dev.ClientPathResolver"/>:
///   env override (MH_CLIENT_DIR), then client_dir.cfg, then auto-detection of common paths,
///   finally empty-catalogue offline mode. No environment variable is required.
///   On failure, all catalogues are empty and the run continues with synthetic data only.
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

    /// <summary>The FSM (exposed so the HUD can read the current lifecycle state).</summary>
    public ClientStateMachine StateMachine { get; private set; } = null!;

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
    /// Item catalogue parsed from data/script/items.csv. Provides CP949-decoded item names
    /// and stats via <see cref="ItemCatalogue.TryGet"/>. Empty when VFS is unavailable.
    /// spec: Docs/RE/formats/config_tables.md §4 items.csv.
    /// </summary>
    public ItemCatalogue ItemCatalogue { get; private set; } = null!;

    /// <summary>
    /// Skill catalogue parsed from data/script/skills.scr. Provides skill definitions
    /// via <see cref="SkillCatalogue.TryGet"/>. Empty when VFS is unavailable.
    /// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr.
    /// </summary>
    public SkillCatalogue SkillCatalogue { get; private set; } = null!;

    /// <summary>
    /// Mob catalogue parsed from data/script/mobs.scr. Provides mob records
    /// via <see cref="MobCatalogue.TryGet"/>. Empty when VFS is unavailable.
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
    /// Both degrade gracefully when no VFS is available (offline mode).
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
    /// Degrades gracefully when VFS is unavailable (all methods return null → placeholders).
    /// spec: Docs/RE/formats/ui_manifests.md §2.6 (23×23 cell model: CODE-CONFIRMED).
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 (.do record layout: CODE-CONFIRMED + SAMPLE-VERIFIED).
    /// </summary>
    public IconCatalogs IconCatalogs { get; private set; } = null!;

    /// <summary>
    /// Item icon catalog: resolves per-item <see cref="Godot.ImageTexture"/> values from
    /// <c>data/item/texturelist.txt</c> for display in the InventoryWindow.
    ///
    /// Each item icon is a whole-texture DDS blit — no atlas sub-rect.
    /// Degrades gracefully when VFS is unavailable (all methods return null → placeholders).
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
    /// Degrades gracefully when VFS is unavailable (all methods return null → placeholders).
    /// spec: Docs/RE/formats/misc_data.md §1.3 (atlas path, record layout).
    /// spec: Docs/RE/formats/misc_data.md §1.6 (cell sizes 23/25 px, 30-slot bar).
    /// </summary>
    public BuffIconCatalog BuffIconCatalog { get; private set; } = null!;

    /// <summary>
    /// Zone catalog: resolves the display name of the zone (and optionally a nearby sub-zone
    /// label) for a given legacy world XZ position, sourced from mapsetting.scr.
    ///
    /// Degrades gracefully when VFS is unavailable (lookups return empty strings).
    /// spec: Docs/RE/formats/misc_data.md §7.1 (mapsetting.scr — 52 zone records).
    /// spec: Docs/RE/specs/minimap.md §6.3 (zone names live in mapsetting.scr, not msg.xdb).
    /// </summary>
    public ZoneCatalog ZoneCatalog { get; private set; } = null!;

    // Cancellation source for the engine loop Task.
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    // VfsCatalogueLoader owns the VFS archive lifecycle for catalogue loading.
    // It is disposed in _ExitTree alongside the loop cancellation.
    private VfsCatalogueLoader? _catalogueLoader;

    // RealClientAssets for the UI catalogs (UiTex + msg.xdb).  Disposed in _ExitTree.
    // Kept separate from the terrain VFS so the two archives have independent lifecycles.
    private RealClientAssets? _uiAssets;

    // The MappedVfsArchive opened by TryOpenVfsForTerrain() and passed into VfsTerrainSectorSource.
    // VfsTerrainSectorSource stores the reference but does NOT implement IDisposable and therefore
    // does NOT dispose the archive — ownership stays here. Disposed in _ExitTree.
    // spec: Docs/RE/formats/pak.md §Two-file scheme (MappedVfsArchive = memory-mapped handle).
    private MappedVfsArchive? _terrainVfs;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // The entire composition-root body is wrapped in a defensive try/catch.
        // Any exception in catalogue loading, VFS opening, or service construction is caught
        // here; the run continues in a degraded-but-safe state so the Godot window still opens.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — composition root.
        try
        {
            BuildApplicationGraph();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] FATAL during composition-root construction: {ex}");
            GD.PrintErr("[ClientContext] Falling back to minimal offline state. " +
                        "The window will open but real assets and the engine loop are unavailable.");

            // Ensure all properties are non-null so child nodes never null-ref the context.
            EnsureMinimalFallbackState();
        }

        // Wire the AudioService as a child node so it participates in the scene lifecycle.
        // Added after the application graph (and its fallback) so the EventBus/StateMachine are
        // available when AudioService._Ready runs. This is the minimal wiring for audio.
        // spec: Docs/RE/specs/sound.md §12.1 (Godot reimplementation guidance).
        try
        {
            var audio = new AudioService { Name = "AudioService" };
            AddChild(audio);
            Audio = audio;
            GD.Print("[ClientContext] AudioService added as child node.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] AudioService init failed: {ex.Message} — no audio.");
        }
    }

    /// <summary>
    /// Builds the full Application object graph.  Separated from _Ready so the
    /// defensive catch in _Ready remains clean.
    /// </summary>
    private void BuildApplicationGraph()
    {
        // 1. Event bus — bounded 1024 capacity, DropOldest backpressure.
        //    spec: ClientEventBus default policy.
        var bus = new ClientEventBus(ClientEventBus.DefaultCapacity);

        // 2. FSM — starts at Login.
        var fsm = new ClientStateMachine(bus, ClientState.Login);

        // 3. Domain world registry.
        var world = new ClientWorld();

        // 4. Unhandled opcode sink — count-only for now.
        IUnhandledOpcodeSink opcodeSink = new CountingUnhandledOpcodeSink();

        // 5. No-op outbound sink: no live transport this session.
        IOutboundPacketSink noopSink = new NoOpOutboundPacketSink();

        // 6. Session id — fixed/default for offline composition.
        SessionId sessionId = SessionId.None;

        // 7. Login credential store.
        //    spec: Docs/RE/specs/crypto.md §6.1.
        var credentialStore = new LoginCredentialStore();

        // 8. Login handshake driver — null for offline/synthetic-feed mode.
        ILoginHandshakeDriver? loginDriver = null;

        // 9. InputBus — UI handler first, world handler wired after InputRouter is created.
        //    The HudInputHandler is a pass-through until a real UI hit-test is wired.
        //    spec: Docs/RE/specs/input_ui.md §3 / §6 (UI before world).
        var hudHandler = new HudInputHandler(hitTest: null); // HUD hit-test: TODO real bridge.

        // The world handler is created by InputRouter, so we build InputBus with only the
        // HUD handler for now; the world handler is appended via a late-binding relay.
        // spec: Docs/RE/specs/input_ui.md §3 — UI before world.
        var worldRelay = new RelayInputHandler(); // placeholder; InputRouter sets the target.
        var inputBus = new InputBus(hudHandler, worldRelay);

        // 10. VFS catalogue loader — resolved via ClientPathResolver (config file / env / auto-detect).
        //     Used for item/skill/mob/stat catalogues (displayed by HUD).
        //     spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules (never hardcode path).
        _catalogueLoader = TryBuildCatalogueLoader();

        // 11. Real stat catalogue — from userlevel.scr via VfsCatalogueLoader.
        //     Replaces the former ScrStatCatalogueSource stub with the real Implementation.
        //     spec: Docs/RE/formats/config_tables.md §2.4 userlevel.scr.
        ScrStatCatalogue scrStatCatalogue;
        try
        {
            scrStatCatalogue = ScrStatCatalogue.FromLoader(_catalogueLoader);
            GD.Print(
                $"[ClientContext] ScrStatCatalogue loaded (HP curve entries={scrStatCatalogue.GetHpBaseCurve().Count}, " +
                $"MP curve entries={scrStatCatalogue.GetMpBaseCurve().Count}).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] ScrStatCatalogue load failed: {ex.Message} — using empty catalogue.");
            // Empty array constructor → both curves are StatBaseCurve.Empty (zero-alloc fallback).
            scrStatCatalogue = new ScrStatCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.LevelBaseEntry>());
        }

        // 12. Catalogue items / skills / mobs for UI display names (CP949).
        //     spec: Docs/RE/formats/config_tables.md §4 items.csv / §2.8 skills.scr / §2.9 mobs.scr.
        try
        {
            ItemCatalogue = ItemCatalogue.FromLoader(_catalogueLoader);
            SkillCatalogue = SkillCatalogue.FromLoader(_catalogueLoader);
            MobCatalogue = MobCatalogue.FromLoader(_catalogueLoader);
            GD.Print($"[ClientContext] Catalogues loaded: {ItemCatalogue.Count} items, " +
                     $"{SkillCatalogue.Count} skills, {MobCatalogue.Count} mobs.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] Catalogue load failed: {ex.Message} — using empty catalogues.");
            // Use empty-array constructors so the properties are non-null and Count returns 0.
            ItemCatalogue ??= new ItemCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.ItemCsvRow>());
            SkillCatalogue ??= new SkillCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.SkillCatalogEntry>());
            MobCatalogue ??= new MobCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.MobCatalogEntry>());
        }

        // 13. UI data catalogs: uitex manifest (data/ui/UiTex.txt) and msg.xdb string catalog.
        //     Opens a second RealClientAssets handle backed by the same VFS archive path so that
        //     UiCatalogs' lazy texture loading (which calls Godot Image APIs) stays on the main
        //     thread independently of the terrain streaming pipeline.
        //     spec: Docs/RE/formats/ui_manifests.md §1 (uitex.txt: 35 DDS entries, PARSER-CONFIRMED).
        //     spec: Docs/RE/formats/misc_data.md §6 (msg.xdb: 2644 records, CODE-CONFIRMED).
        //     spec: Docs/RE/specs/ui_system.md §8.5 (HUD uitex integer binding contract).
        try
        {
            _uiAssets = RealClientAssets.TryOpen(); // path via ClientPathResolver (same chain as catalogue loader)
            UiCatalogs = new UiCatalogs(_uiAssets);
            // Force-load both catalogs now so sizes appear in the boot log.
            // EnsureUiTex / EnsureMsg are lazy but we trigger them here for diagnostics.
            GD.Print($"[ClientContext] UiCatalogs: {UiCatalogs.UiTexEntryCount} uitex entries, " +
                     $"{UiCatalogs.MsgRecordCount} msg records.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] UiCatalogs load failed: {ex.Message} — using empty UI catalogs.");
            _uiAssets = null;
            UiCatalogs ??= new UiCatalogs(null);
        }

        // 14. Skill icon catalog: skillicon.txt + musajung.do stance table.
        //     Uses the same _uiAssets handle as UiCatalogs so no additional VFS handle is opened.
        //     Lazy — both files are parsed on first GetIcon() / GetFirstSlots() call.
        //     spec: Docs/RE/formats/ui_manifests.md §2.6 (23×23 cell model: CODE-CONFIRMED).
        //     spec: Docs/RE/formats/ui_manifests.md §2.7 (musajung.do, SAMPLE-VERIFIED presence).
        try
        {
            IconCatalogs = new IconCatalogs(_uiAssets);
            // Trigger lazy load now so the boot log shows the record count.
            int doCount = IconCatalogs.DoRecordCount;
            GD.Print($"[ClientContext] IconCatalogs: {doCount} musajung.do records loaded. " +
                     "spec: Docs/RE/formats/ui_manifests.md §2.7 CODE-CONFIRMED + SAMPLE-VERIFIED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] IconCatalogs load failed: {ex.Message} — skill icons will use placeholders.");
            IconCatalogs ??= new IconCatalogs(null);
        }

        // 15. Item icon catalog: data/item/texturelist.txt — maps tex_id to DDS icon paths.
        //     Uses the same _uiAssets handle as UiCatalogs (no additional VFS handle opened).
        //     Lazy — the manifest is parsed on first GetIcon() / GetDemoIcons() call.
        //     spec: Docs/RE/formats/ui_manifests.md §10 (flat newline-delimited list: CODE-CONFIRMED).
        //     spec: Docs/RE/formats/ui_manifests.md §10.5 — "whole-texture blit, no sub-rect": CODE-CONFIRMED.
        try
        {
            ItemIconCatalog = new ItemIconCatalog(_uiAssets);
            // Trigger lazy load now so the boot log shows the manifest entry count.
            int itemIconCount = ItemIconCatalog.ManifestCount;
            GD.Print($"[ClientContext] ItemIconCatalog: {itemIconCount} texturelist.txt entries loaded. " +
                     "spec: Docs/RE/formats/ui_manifests.md §10 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[ClientContext] ItemIconCatalog load failed: {ex.Message} — item icons will use placeholders.");
            ItemIconCatalog ??= new ItemIconCatalog(null);
        }

        // 16-A. HUD event hub: the single facade for all per-frame HUD channels.
        //     Constructed once here; widgets subscribe via IHudEventHub.
        //     spec: MartialHeroes.Client.Application.Hud — IHudEventHub / HudEventHub.
        var hudHub = new HudEventHub();
        HudEventHub = hudHub;
        GD.Print("[ClientContext] HudEventHub constructed (6 typed channels).");

        // 16-B. Buff icon catalog (stateicon.dds + buff_icon_position.xdb).
        //     Uses the same _uiAssets handle (no additional VFS archive opened).
        //     spec: Docs/RE/formats/misc_data.md §1.3 / §1.6.
        try
        {
            BuffIconCatalog = new BuffIconCatalog(_uiAssets);
            GD.Print($"[ClientContext] BuffIconCatalog initialised ({BuffIconCatalog.TableCount} entries).");
        }
        catch (Exception ex)
        {
            GD.PrintErr(
                $"[ClientContext] BuffIconCatalog init failed: {ex.Message} — buff icons will be placeholders.");
            BuffIconCatalog ??= new BuffIconCatalog(null);
        }

        // 16-C. Zone catalog (mapsetting.scr — 52 zone records, CP949 names).
        //     Uses a fresh RealClientAssets handle for the zone-data VFS path.
        //     spec: Docs/RE/formats/misc_data.md §7.1 / Docs/RE/specs/minimap.md §6.3.
        try
        {
            ZoneCatalog = new ZoneCatalog(RealClientAssets.TryOpen());
            GD.Print($"[ClientContext] ZoneCatalog initialised ({ZoneCatalog.AllZones.Count} zone records).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] ZoneCatalog init failed: {ex.Message} — zone names unavailable.");
            ZoneCatalog ??= new ZoneCatalog(null);
        }

        // 16. VFS archive for terrain (separate from catalogue loader — same paths).
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        //     Store in _terrainVfs so _ExitTree can dispose it (VfsTerrainSectorSource does not own/dispose).
        MappedVfsArchive? vfs = TryOpenVfsForTerrain();
        _terrainVfs = vfs;

        // 17. Terrain sector source — backed by VFS (or empty if offline).
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
        var handler = new GamePacketHandler(world, bus, fsm, opcodeSink, loginDriver)
        {
            VitalsResolver = CatalogueVitalsResolver.Create(scrStatCatalogue)
        };

        // 18. Inbound frame dispatcher — channel-backed; synthetic feeder uses this.
        var dispatcher = new InboundFrameDispatcher(handler);

        // 19. Version token — pass `default` (empty span) so ApplicationUseCases derives it via
        //     DefaultClientVersionSource.Instance: token = 10 × 2114 + 9 = 21149 (sample_verified).
        //     An explicit zero-filled span would OVERRIDE the derivation to all-zeros. Passing default
        //     activates the branch that calls ClientVersionToken.Derive() and stamps "21149\0" into the
        //     33-byte buffer. spec: Docs/RE/specs/login_flow.md §3.3 / §7.
        //     spec: Docs/RE/packets/1-9_enter_game_request.yaml (VersionToken 0x01, 33 bytes).
        ReadOnlySpan<byte> versionToken = default; // empty → derives via IClientVersionSource

        // 20. Use-case facade — presentation calls these for input intents.
        //     versionSource = null → DefaultClientVersionSource.Instance → field 2114 → token 21149.
        //     spec: Docs/RE/specs/login_flow.md §3.3 / §7 (token = 10 × versionField + 9 = 21149).
        var useCases = new ApplicationUseCases(noopSink, fsm, world, credentialStore, sessionId,
            versionToken: versionToken, versionSource: null);
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
        StateMachine = fsm;
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

    /// <summary>
    /// Builds the absolute minimum set of non-null properties so child nodes that access
    /// <c>ClientContext</c> never encounter a null reference, even when
    /// <see cref="BuildApplicationGraph"/> threw.
    ///
    /// All services are hollow/no-op implementations that do nothing but are non-null.
    /// </summary>
    private void EnsureMinimalFallbackState()
    {
        if (EventBus is null)
        {
            var bus = new ClientEventBus(ClientEventBus.DefaultCapacity);
            var fsm = new ClientStateMachine(bus, ClientState.Login);
            var world = new ClientWorld();
            var noopSink = new NoOpOutboundPacketSink();
            var hudHandler = new HudInputHandler(hitTest: null);
            var worldRelay = new RelayInputHandler();
            var inputBus = new InputBus(hudHandler, worldRelay);
            var credentialStore = new LoginCredentialStore();
            SessionId sessionId = SessionId.None;
            // Pass default (empty span) so the derivation via DefaultClientVersionSource runs.
            // spec: Docs/RE/specs/login_flow.md §3.3 / §7 (token = 10 × 2114 + 9 = 21149).
            var useCases = new ApplicationUseCases(noopSink, fsm, world, credentialStore, sessionId,
                versionToken: default, versionSource: null);
            var opcodeSink = new CountingUnhandledOpcodeSink();
            var handler = new GamePacketHandler(world, bus, fsm, opcodeSink, null);
            var dispatcher = new InboundFrameDispatcher(handler);
            var terrainSource = new VfsTerrainSectorSource(null, areaId: 0);
            // spec: Docs/RE/formats/terrain.md §12.2 — High quality → 5×5 ring (ring-radius cells = 5).
            var streamingService = new SectorStreamingService(terrainSource, bus, StreamQuality.High);
            var engineLoop = new GameEngineLoop(world, bus, inputBus, GameEngineLoop.DefaultTickRateHz);

            EventBus = bus;
            UseCases = useCases;
            Dispatcher = dispatcher;
            StateMachine = fsm;
            InputBus = inputBus;
            EngineLoop = engineLoop;
            StreamingService = streamingService;
            _setWorldHandler = worldRelay.SetTarget;

            // Start the loop (no assets, no network — just keeps the bus alive).
            _loopCts = new CancellationTokenSource();
            PeriodicGameClock clock = engineLoop.CreateRealtimeClock();
            // Store the RAW loop task so _ExitTree drains it before disposing _loopCts (Fix 3).
            Task rawLoop = engineLoop.RunAsync(clock, _loopCts.Token);
            _loopTask = rawLoop;
            _ = rawLoop.ContinueWith(
                t => GD.PrintErr($"[ClientContext] Fallback EngineLoop faulted: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        ItemCatalogue ??= new ItemCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.ItemCsvRow>());
        SkillCatalogue ??= new SkillCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.SkillCatalogEntry>());
        MobCatalogue ??= new MobCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.MobCatalogEntry>());

        // UiCatalogs — null-safe offline fallback (no VFS).
        // spec: Docs/RE/formats/ui_manifests.md §1 / Docs/RE/formats/misc_data.md §6.
        UiCatalogs ??= new UiCatalogs(null);

        // IconCatalogs — null-safe offline fallback (no VFS).
        // spec: Docs/RE/formats/ui_manifests.md §2.6 / §2.7.
        IconCatalogs ??= new IconCatalogs(null);

        // ItemIconCatalog — null-safe offline fallback (no VFS).
        // spec: Docs/RE/formats/ui_manifests.md §10 / §10.5.
        ItemIconCatalog ??= new ItemIconCatalog(null);

        // HudEventHub — null-safe offline fallback.
        // spec: MartialHeroes.Client.Application.Hud — IHudEventHub / HudEventHub.
        HudEventHub ??= new HudEventHub();

        // BuffIconCatalog — null-safe offline fallback (no VFS).
        // spec: Docs/RE/formats/misc_data.md §1.3 / §1.6.
        BuffIconCatalog ??= new BuffIconCatalog(null);

        // ZoneCatalog — null-safe offline fallback (no VFS).
        // spec: Docs/RE/formats/misc_data.md §7.1 / Docs/RE/specs/minimap.md §6.3.
        ZoneCatalog ??= new ZoneCatalog(null);

        GD.Print("[ClientContext] Minimal fallback state initialised.");
    }

    // Stored so InputRouter can wire the world handler after initialisation.
    // Typed as Action<IInputHandler> to avoid exposing the file-local type in the member signature.
    private Action<IInputHandler>? _setWorldHandler;

    /// <summary>
    /// Called by InputRouter after it is ready: wires the world input handler into the bus.
    /// spec: Docs/RE/specs/input_ui.md §3 — world handler registered after UI handler.
    /// </summary>
    public void SetWorldInputHandler(IInputHandler worldHandler)
    {
        _setWorldHandler?.Invoke(worldHandler);
    }

    public override void _ExitTree()
    {
        // Signal the event channel so any async drainers stop cleanly.
        EventBus?.Complete();

        // Complete the HUD event hub so all widget channel-readers finish cleanly.
        // spec: MartialHeroes.Client.Application.Hud — IHudEventHub.Complete().
        HudEventHub?.Complete();

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

        GD.Print(
            "[ClientContext] EventBus completed. EngineLoop drained + stopped. CatalogueLoader + UiCatalogs + TerrainVfs disposed.");
    }

    // -------------------------------------------------------------------------
    // VFS / catalogue helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="VfsCatalogueLoader"/> using <see cref="ClientPathResolver.ResolveClientDir"/>.
    /// Returns an empty-catalogue loader when no valid client directory is found.
    ///
    /// Path resolution is delegated entirely to <see cref="ClientPathResolver"/> (env-var override,
    /// then client_dir.cfg, then auto-detection). No direct environment-variable read here.
    ///
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules (user supplies originals).
    /// spec: Docs/RE/formats/pak.md §Two-file scheme.
    /// </summary>
    private static VfsCatalogueLoader TryBuildCatalogueLoader()
    {
        string? clientDir = ClientPathResolver.ResolveClientDir();
        if (clientDir is not null)
        {
            string infPath = Path.Combine(clientDir, "data.inf");
            string vfsPath = Path.Combine(clientDir, "data", "data.vfs");
            GD.Print($"[ClientContext] CatalogueLoader: using resolved client dir '{clientDir}'.");
            return new VfsCatalogueLoader(infPath, vfsPath);
        }

        // Offline / no VFS available — loader returns empty arrays for all catalogues.
        GD.Print("[ClientContext] CatalogueLoader: no VFS found — all catalogues empty (offline mode).");
        return new VfsCatalogueLoader(); // empty constructor → no archive
    }

    /// <summary>
    /// Opens the VFS archive for terrain sector streaming using <see cref="ClientPathResolver.ResolveClientDir"/>.
    /// Returns null gracefully when no valid client directory is found (offline mode).
    ///
    /// spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
    /// </summary>
    private static MappedVfsArchive? TryOpenVfsForTerrain()
    {
        string? clientDir = ClientPathResolver.ResolveClientDir();
        if (clientDir is null)
        {
            GD.Print("[ClientContext] Terrain VFS not found — running offline (no terrain assets).");
            return null;
        }

        string infPath = Path.Combine(clientDir, "data.inf");
        string vfsPath = Path.Combine(clientDir, "data", "data.vfs");
        try
        {
            MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
            GD.Print($"[ClientContext] Terrain VFS opened from '{clientDir}' ({archive.EntryCount} entries).");
            return archive;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] Terrain VFS open failed from '{clientDir}': {ex.Message}");
            return null;
        }
    }
}

// -------------------------------------------------------------------------
// File-local helpers
// -------------------------------------------------------------------------

/// <summary>
/// Stub <see cref="IOutboundPacketSink"/> that silently discards every send.
/// Used until a live transport session is available.
/// </summary>
file sealed class NoOpOutboundPacketSink : IOutboundPacketSink
{
    public ValueTask SendAsync(
        SessionId sessionId,
        ushort majorOpcode,
        ushort minorOpcode,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

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