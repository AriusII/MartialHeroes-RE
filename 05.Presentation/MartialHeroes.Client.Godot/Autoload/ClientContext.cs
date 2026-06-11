using Godot;
using MartialHeroes.Client.Application.Diagnostics;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Handlers;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Login;
using MartialHeroes.Client.Application.StateMachine;
using MartialHeroes.Client.Application.UseCases;
using MartialHeroes.Client.Application.World;
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
///
/// Threading contract: this node is created once on the Godot main thread. The constructed
/// services are thread-safe (channel-based); Godot node mutation must still happen on the
/// main thread (enforced in GameLoop via CallDeferred).
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — composition root.
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
    /// The inbound frame dispatcher. Primarily used by <see cref="MartialHeroes.Client.Godot.Debug.SyntheticWorldFeeder"/>
    /// to inject synthetic Application events without touching any game rule.
    /// </summary>
    public InboundFrameDispatcher Dispatcher { get; private set; } = null!;

    /// <summary>The FSM (exposed so the HUD can read the current lifecycle state).</summary>
    public ClientStateMachine StateMachine { get; private set; } = null!;

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

        // 4. Unhandled opcode sink — count-only for now; no logging infrastructure wired yet.
        IUnhandledOpcodeSink opcodeSink = new CountingUnhandledOpcodeSink();

        // 5. No-op outbound sink: no live transport this session.
        //    When Transport.Pipelines is wired, replace with the real implementation.
        IOutboundPacketSink noopSink = new NoOpOutboundPacketSink();

        // 6. Session id — fixed/default for offline composition.
        //    Real session id is assigned by the transport when a live connection is established.
        SessionId sessionId = SessionId.None;

        // 7. Login credential store — staged at login-form time; consumed by the handshake driver.
        //    spec: Docs/RE/specs/crypto.md §6.1.
        var credentialStore = new LoginCredentialStore();

        // 8. Login handshake driver — null for offline/synthetic-feed mode; the GamePacketHandler
        //    accepts null and silently counts 0/0 frames as unhandled when no driver is wired.
        //    When a live transport session is established, replace with:
        //      new LoginHandshakeDriver(noopSink, credentialStore, sessionId)
        //    No Network.Crypto ProjectReference is needed here: LoginHandshakeDriver lives in
        //    Client.Application, which arrives transitively. The null path avoids instantiating
        //    SessionHandshake/CryptoPaddingRandom in the offline composition root.
        ILoginHandshakeDriver? loginDriver = null;

        // 9. Packet handler — orchestrates Domain mutation and event publishing.
        var handler = new GamePacketHandler(world, bus, fsm, opcodeSink, loginDriver);

        // 10. Inbound frame dispatcher — channel-backed; synthetic feeder uses this.
        var dispatcher = new InboundFrameDispatcher(handler);

        // 11. Version token — 33 bytes, zero-filled (PROVISIONAL; real token is unrecovered).
        //     spec: Docs/RE/packets/1-9_enter_game_request.yaml (VersionToken 0x01, 33 bytes, UNKNOWN).
        ReadOnlySpan<byte> versionToken = stackalloc byte[ApplicationUseCases.VersionTokenLength]; // zero-filled

        // 12. Use-case facade — presentation calls these for input intents.
        var useCases = new ApplicationUseCases(noopSink, fsm, world, credentialStore, sessionId, versionToken);

        // Publish to fields.
        EventBus = bus;
        UseCases = useCases;
        Dispatcher = dispatcher;
        StateMachine = fsm;

        GD.Print("[ClientContext] Application graph constructed. EventBus ready.");
    }

    public override void _ExitTree()
    {
        // Signal the event channel so any async drainers stop cleanly.
        EventBus?.Complete();
        GD.Print("[ClientContext] EventBus completed.");
    }
}

/// <summary>
/// Stub <see cref="IOutboundPacketSink"/> that silently discards every send.
/// Used until a live transport session is available.
/// Resides in Autoload because it is purely a composition-root concern.
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
        // Intentional no-op: no live transport this session.
        // Replace with the real Transport.Pipelines-backed sink when available.
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}