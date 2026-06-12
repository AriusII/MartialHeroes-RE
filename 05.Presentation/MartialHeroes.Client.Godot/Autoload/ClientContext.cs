using Godot;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Domain.Simulation;
using MartialHeroes.Client.Godot.Adapters;
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

    // Cancellation source for the engine loop Task.
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    // VfsCatalogueLoader owns the VFS archive lifecycle for catalogue loading.
    // It is disposed in _ExitTree alongside the loop cancellation.
    private VfsCatalogueLoader? _catalogueLoader;

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
            GD.Print($"[ClientContext] ScrStatCatalogue loaded (HP curve entries={scrStatCatalogue.GetHpBaseCurve().Count}, " +
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

        // 13. VFS archive for terrain (separate from catalogue loader — same paths).
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        MappedVfsArchive? vfs = TryOpenVfsForTerrain();

        // 14. Terrain sector source — backed by VFS (or empty if offline).
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        //     Area 0 is the default starting area. TODO: update on enter-game.
        var terrainSource = new VfsTerrainSectorSource(vfs, areaId: 0);

        // 15. Terrain streaming service — medium quality (3×3 ring).
        //     spec: Docs/RE/formats/terrain.md §9.2.
        var streamingService = new SectorStreamingService(terrainSource, bus, StreamQuality.Medium);

        // 16. Packet handler — orchestrates Domain mutation and event publishing.
        //     Wire the catalogue vitals resolver (real stat curves) at construction.
        //     spec: CatalogueVitalsResolver.Create — builds the seam from the catalogue.
        //     spec: Docs/RE/formats/config_tables.md §2.4.
        var handler = new GamePacketHandler(world, bus, fsm, opcodeSink, loginDriver)
        {
            VitalsResolver = CatalogueVitalsResolver.Create(scrStatCatalogue)
        };

        // 17. Inbound frame dispatcher — channel-backed; synthetic feeder uses this.
        var dispatcher = new InboundFrameDispatcher(handler);

        // 18. Version token — 33 bytes, zero-filled (PROVISIONAL).
        //     spec: Docs/RE/packets/1-9_enter_game_request.yaml (VersionToken 0x01, 33 bytes, UNKNOWN).
        ReadOnlySpan<byte> versionToken = stackalloc byte[ApplicationUseCases.VersionTokenLength];

        // 19. Use-case facade — presentation calls these for input intents.
        var useCases = new ApplicationUseCases(noopSink, fsm, world, credentialStore, sessionId, versionToken);

        // 20. Fixed-tick GameEngineLoop — 30 Hz.
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

        // Observe background faults so they do NOT silently kill the process.
        // The ContinueWith runs only if the task faults, logs the exception, and does NOT rethrow.
        Task rawLoop = engineLoop.RunAsync(clock, _loopCts.Token);
        _loopTask = rawLoop.ContinueWith(
            t => GD.PrintErr($"[ClientContext] EngineLoop faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        // Keep the raw task alive for await in _ExitTree so the loop can drain.
        _ = rawLoop; // intentionally not awaited here; fault is observed above.

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
            ReadOnlySpan<byte> versionToken = stackalloc byte[ApplicationUseCases.VersionTokenLength];
            var useCases = new ApplicationUseCases(noopSink, fsm, world, credentialStore, sessionId, versionToken);
            var opcodeSink = new CountingUnhandledOpcodeSink();
            var handler = new GamePacketHandler(world, bus, fsm, opcodeSink, null);
            var dispatcher = new InboundFrameDispatcher(handler);
            var terrainSource = new VfsTerrainSectorSource(null, areaId: 0);
            var streamingService = new SectorStreamingService(terrainSource, bus, StreamQuality.Medium);
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
            Task rawLoop = engineLoop.RunAsync(clock, _loopCts.Token);
            _loopTask = rawLoop.ContinueWith(
                t => GD.PrintErr($"[ClientContext] Fallback EngineLoop faulted: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            _ = rawLoop;
        }

        ItemCatalogue ??= new ItemCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.ItemCsvRow>());
        SkillCatalogue ??= new SkillCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.SkillCatalogEntry>());
        MobCatalogue ??= new MobCatalogue(Array.Empty<MartialHeroes.Assets.Parsers.Models.MobCatalogEntry>());
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

        // Cancel and wait for the engine loop task.
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;

        // Dispose the catalogue loader (releases the VFS memory-mapped archive).
        _catalogueLoader?.Dispose();
        _catalogueLoader = null;

        GD.Print("[ClientContext] EventBus completed. EngineLoop stopped. CatalogueLoader disposed.");
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
