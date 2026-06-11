namespace MartialHeroes.Client.Application.Events;

/// <summary>
/// The high-level client lifecycle state, as driven by the application-layer FSM.
/// </summary>
/// <remarks>
/// <para>
/// This is the application/use-case view of the lifecycle. It is deliberately coarser than the
/// transport-level <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState"/>: a
/// single transport state (e.g. <c>Authenticated</c>) can host several application states
/// (character selection, loading). The mapping is documented on each member and applied by
/// <see cref="ClientStateMachine"/>.
/// </para>
/// <para>
/// State transitions are deterministic and event-driven; the FSM never blocks, never sleeps, and
/// takes no wall-clock input.
/// </para>
/// </remarks>
public enum ClientState : byte
{
    /// <summary>
    /// Not yet connected / authenticating. Maps to transport
    /// <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState.Connecting"/> /
    /// <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState.Handshaking"/>.
    /// </summary>
    Login = 0,

    /// <summary>
    /// Credentials accepted; the player is choosing a character. Maps to transport
    /// <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState.Authenticated"/>.
    /// </summary>
    CharacterSelection = 1,

    /// <summary>
    /// A character has been selected and the world is loading (enter-game in flight). Still maps to
    /// transport <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState.Authenticated"/>
    /// until the 3/5 ack arrives. spec: Docs/RE/opcodes.md (3/5 SmsgEnterGameAck).
    /// </summary>
    Loading = 2,

    /// <summary>
    /// The character is in the game world; gameplay packets flow. Maps to transport
    /// <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState.InWorld"/>.
    /// spec: Docs/RE/opcodes.md (3/5 transitions the client into the in-world game state).
    /// </summary>
    World = 3,

    /// <summary>
    /// A clean or faulted disconnect occurred; the session must be torn down. Maps to transport
    /// <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState.Disconnected"/> /
    /// <see cref="MartialHeroes.Network.Abstractions.Session.ConnectionState.Faulted"/>.
    /// </summary>
    Disconnected = 4,
}