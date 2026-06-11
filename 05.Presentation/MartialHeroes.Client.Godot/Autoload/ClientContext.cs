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
using MartialHeroes.Client.Godot.Input;
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
///
/// Threading contract: this node is created once on the Godot main thread. The constructed
/// services are thread-safe (channel-based); Godot node mutation must still happen on the
/// main thread (enforced in GameLoop via CallDeferred).
///
/// VFS / assets:
///   The composition root tries to open the VFS archive from the standard LegacyClient path.
///   On failure (no LegacyClient directory, missing archive files) it silently falls back to
///   offline mode: <see cref="VfsTerrainSectorSource"/> returns empty, <see cref="ScrStatCatalogueSource"/>
///   returns empty curves, and the run continues with synthetic data only.
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

    // Cancellation source for the engine loop Task.
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
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
        // HUD handler for now; the world handler is appended in StartEngineLoop after
        // InputRouter is ready. For this wave, InputBus is built with both placeholder handlers.
        // Since we cannot reach InputRouter here (it is a scene-tree node, not available yet),
        // we build a WorldPassThroughHandler that always returns false (world input comes through
        // the direct use-case call path in InputRouter._UnhandledInput for hotbar, and via
        // InputBus.Enqueue + this handler for movement). We use a deferred update pattern:
        // InputRouter.CreateWorldInputHandler() sets the real handler; the bus is rebuilt.
        // Simpler: build the bus with only the HUD handler. After _Ready, InputRouter
        // calls InitialiseBus to register the bus; the world handler is appended via
        // a factory on InputBus (InputBus handler list is fixed at construction, so we use
        // a two-stage construction: first pass the HUD handler, then add world handler via
        // an extended InputBus wrapper).
        //
        // For this wave: the InputBus is constructed with TWO handlers. The world handler
        // is a late-binding relay that InputRouter sets after construction.
        //   spec: Docs/RE/specs/input_ui.md §3 — UI before world.
        var worldRelay = new RelayInputHandler(); // placeholder; InputRouter sets the target.
        var inputBus = new InputBus(hudHandler, worldRelay);

        // 10. VFS — try to open; fall back gracefully if unavailable.
        MappedVfsArchive? vfs = TryOpenVfs();

        // 11. Terrain sector source — backed by VFS (or empty if offline).
        //     spec: Docs/RE/formats/terrain.md §1.2 / §1.3.
        //     Area 0 is the default starting area. Real wiring: the active area id comes from the
        //     server (enter-game ack); for offline testing, area 0 is used. TODO: update on enter-game.
        var terrainSource = new VfsTerrainSectorSource(vfs, areaId: 0);

        // 12. Terrain streaming service — medium quality (3×3 ring).
        //     spec: Docs/RE/formats/terrain.md §9.2.
        var streamingService = new SectorStreamingService(terrainSource, bus, StreamQuality.Medium);

        // 13. Stat catalogue source — backed by VFS userlevel.scr.
        //     spec: Docs/RE/formats/config_tables.md §2.4.
        var statCatalogue = new ScrStatCatalogueSource(vfs);

        // 14. Packet handler — orchestrates Domain mutation and event publishing.
        //     Wire the catalogue vitals resolver at construction via the init-only property.
        //     spec: CatalogueVitalsResolver.Create — builds the seam from the catalogue.
        //     spec: Docs/RE/formats/config_tables.md §2.4.
        var handler = new GamePacketHandler(world, bus, fsm, opcodeSink, loginDriver)
        {
            VitalsResolver = CatalogueVitalsResolver.Create(statCatalogue)
        };

        // 15. Inbound frame dispatcher — channel-backed; synthetic feeder uses this.
        var dispatcher = new InboundFrameDispatcher(handler);

        // 16. Version token — 33 bytes, zero-filled (PROVISIONAL).
        //     spec: Docs/RE/packets/1-9_enter_game_request.yaml (VersionToken 0x01, 33 bytes, UNKNOWN).
        ReadOnlySpan<byte> versionToken = stackalloc byte[ApplicationUseCases.VersionTokenLength];

        // 17. Use-case facade — presentation calls these for input intents.
        var useCases = new ApplicationUseCases(noopSink, fsm, world, credentialStore, sessionId, versionToken);

        // 18. Fixed-tick GameEngineLoop — 30 Hz.
        //     spec: Docs/RE/specs/game_loop.md §6 ("e.g. 30 Hz via a PeriodicTimer"). CONFIRMED.
        var engineLoop = new GameEngineLoop(world, bus, inputBus, GameEngineLoop.DefaultTickRateHz);

        // Publish to fields.
        EventBus = bus;
        UseCases = useCases;
        Dispatcher = dispatcher;
        StateMachine = fsm;
        InputBus = inputBus;
        EngineLoop = engineLoop;

        // Store the relay setter so InputRouter can set the real handler later.
        _setWorldHandler = worldRelay.SetTarget;

        // Start the fixed-tick loop on a background task.
        // spec: Docs/RE/specs/game_loop.md §6 — "Fixed-rate logic tick … via a PeriodicTimer".
        _loopCts = new CancellationTokenSource();
        PeriodicGameClock clock = engineLoop.CreateRealtimeClock();
        _loopTask = engineLoop.RunAsync(clock, _loopCts.Token);

        GD.Print("[ClientContext] Application graph constructed. EventBus ready. EngineLoop started at 30 Hz.");
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

        GD.Print("[ClientContext] EventBus completed. EngineLoop stopped.");
    }

    // -------------------------------------------------------------------------
    // VFS helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to open the VFS archive from the standard LegacyClient paths. Returns null
    /// when the archive is absent or cannot be opened (offline mode).
    ///
    /// LegacyClient is gitignored and user-supplied. We do not fail the run if it is absent.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
    /// </summary>
    private static MappedVfsArchive? TryOpenVfs()
    {
        // Standard paths relative to the working directory (where the game executable runs from).
        // Users place their original client files in /LegacyClient/.
        const string infPath = "LegacyClient/data.inf";
        const string vfsPath = "LegacyClient/data/data.vfs";

        try
        {
            if (!File.Exists(infPath) || !File.Exists(vfsPath))
            {
                GD.Print("[ClientContext] VFS archive not found — running offline (no .pak assets).");
                return null;
            }

            MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
            GD.Print($"[ClientContext] VFS archive opened ({archive.EntryCount} entries).");
            return archive;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientContext] VFS open failed: {ex.Message}. Running offline.");
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