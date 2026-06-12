using MartialHeroes.Client.Application.Events;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Client.Application.StateMachine;

/// <summary>
/// Deterministic, event-driven FSM over the client lifecycle:
/// <see cref="ClientState.Login"/> -&gt; <see cref="ClientState.CharacterSelection"/> -&gt;
/// <see cref="ClientState.Loading"/> -&gt; <see cref="ClientState.World"/>, with a
/// <see cref="ClientState.Disconnected"/> escape reachable from any state.
/// </summary>
/// <remarks>
/// <para>
/// The FSM owns no I/O and no timers; transitions are triggered explicitly by handlers and use
/// cases. Each accepted transition publishes a <see cref="ClientStateChangedEvent"/> so the
/// presentation layer can react. Illegal transitions are rejected (return <see langword="false"/>)
/// without side effects, keeping behaviour total and testable.
/// </para>
/// <para>
/// The mapping to the transport-level
/// <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState"/> is advisory (see
/// <see cref="ToConnectionState"/>): the transport drives its own state; this is the application's
/// coarser view layered on top. spec: Docs/RE/opcodes.md (3/5 SmsgEnterGameAck drives Loading -&gt;
/// World).
/// </para>
/// </remarks>
public sealed class ClientStateMachine
{
    private readonly IClientEventBus _eventBus;

    /// <summary>The current lifecycle state.</summary>
    public ClientState Current { get; private set; }

    /// <summary>Creates the FSM in <paramref name="initial"/> (defaults to <see cref="ClientState.Login"/>).</summary>
    public ClientStateMachine(IClientEventBus eventBus, ClientState initial = ClientState.Login)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Current = initial;
    }

    /// <summary>Login accepted: move to character selection.</summary>
    public bool OnAuthenticated() => TryTransition(ClientState.Login, ClientState.CharacterSelection);

    /// <summary>A character was selected and enter-game requested: move to loading.</summary>
    public bool OnCharacterSelected() =>
        TryTransition(ClientState.CharacterSelection, ClientState.Loading);

    /// <summary>
    /// 3/1 character-select list received: ensure the client is on the character-selection screen. spec:
    /// Docs/RE/opcodes.md (3/1 "switches to the select screen"). Accepted from
    /// <see cref="ClientState.Login"/>; a no-op (no event) when already on the select screen or further
    /// along, so an in-world 3/1 (e.g. a roster refresh) does not regress the lifecycle.
    /// </summary>
    public bool OnCharacterListReceived() =>
        Current == ClientState.Login && SetState(ClientState.CharacterSelection);

    /// <summary>
    /// 3/5 enter-game ack received: enter the world. spec: Docs/RE/opcodes.md (3/5 transitions the
    /// client into the in-world game state). Accepted from <see cref="ClientState.Loading"/> and,
    /// defensively, directly from <see cref="ClientState.CharacterSelection"/> (server may ack
    /// without an observed intermediate state).
    /// </summary>
    public bool OnEnterWorld()
    {
        if (Current is ClientState.Loading or ClientState.CharacterSelection)
        {
            return SetState(ClientState.World);
        }

        return false;
    }

    /// <summary>
    /// A clean or faulted disconnect: collapse to <see cref="ClientState.Disconnected"/> from any
    /// state. Idempotent (no event if already disconnected).
    /// </summary>
    public bool OnDisconnected()
    {
        if (Current == ClientState.Disconnected)
        {
            return false;
        }

        return SetState(ClientState.Disconnected);
    }

    /// <summary>
    /// Resets to <see cref="ClientState.Login"/> for a fresh connection attempt. Only valid from
    /// <see cref="ClientState.Disconnected"/>.
    /// </summary>
    public bool Reset() => TryTransition(ClientState.Disconnected, ClientState.Login);

    /// <summary>
    /// Advisory projection of the application state onto the transport
    /// <see cref="ConnectionState"/>. spec: Docs/RE/opcodes.md / ConnectionState contract.
    /// </summary>
    public static ConnectionState ToConnectionState(ClientState state) => state switch
    {
        ClientState.Login => ConnectionState.Handshaking,
        ClientState.CharacterSelection => ConnectionState.Authenticated,
        ClientState.Loading => ConnectionState.Authenticated,
        ClientState.World => ConnectionState.InWorld,
        ClientState.Disconnected => ConnectionState.Disconnected,
        _ => ConnectionState.Disconnected,
    };

    private bool TryTransition(ClientState from, ClientState to)
    {
        if (Current != from)
        {
            return false;
        }

        return SetState(to);
    }

    private bool SetState(ClientState to)
    {
        ClientState previous = Current;
        if (previous == to)
        {
            return false;
        }

        Current = to;
        _eventBus.Publish(new ClientStateChangedEvent(previous, to));
        return true;
    }
}