namespace MartialHeroes.Network.Abstractions.Session;

/// <summary>
///     Lifecycle states of a <see cref="IConnectionSession" />. Transitions flow strictly in the
///     direction listed; a session may jump directly to <see cref="Faulted" /> from any state.
/// </summary>
/// <remarks>
///     Implementors: <c>Transport.Pipelines</c> drives state transitions as the TCP/UDP handshake
///     and protocol-level authentication handshake complete.
/// </remarks>
public enum ConnectionState : byte
{
    /// <summary>
    ///     No underlying connection exists. This is the initial state and the state after a clean
    ///     or faulted disconnect.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    ///     The transport-level connection is being established (e.g. TCP SYN/ACK in flight).
    /// </summary>
    Connecting = 1,

    /// <summary>
    ///     Transport is connected; the application-level handshake (key exchange, challenge/response)
    ///     is in progress. The application-level RSA key-exchange (crypto.md §6) completes here; the
    ///     per-packet byte cipher stays keyless and stateless (crypto.md §4 — it carries no
    ///     per-connection seed and is a pure function of (payload, length)).
    /// </summary>
    Handshaking = 2,

    /// <summary>
    ///     Credentials have been verified by the server. The session may now send authenticated
    ///     requests but the player character has not yet entered a game world.
    /// </summary>
    Authenticated = 3,

    /// <summary>
    ///     A character has entered a game world and the session is fully operational. All gameplay
    ///     packets flow while in this state.
    /// </summary>
    InWorld = 4,

    /// <summary>
    ///     An unrecoverable error has occurred (e.g. framing violation, decryption failure, forced
    ///     server disconnect). The session must be torn down and a new one created to reconnect.
    /// </summary>
    Faulted = 5
}