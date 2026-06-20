namespace MartialHeroes.Network.Abstractions.Session;

/// <summary>
///     Describes why a <see cref="IConnectionSession" /> transitioned to
///     <see cref="ConnectionState.Disconnected" /> or <see cref="ConnectionState.Faulted" />.
/// </summary>
/// <remarks>
///     Carried by <see cref="SessionDisconnectedEventArgs" /> so that higher layers
///     (<c>Client.Application</c>) can display appropriate UI feedback or schedule a reconnect
///     without inspecting exception types.
/// </remarks>
public enum DisconnectReason : byte
{
    /// <summary>No reason specified (default; not yet set).</summary>
    None = 0,

    /// <summary>The local side requested a clean shutdown via <see cref="IConnectionSession.DisconnectAsync" />.</summary>
    LocalClose = 1,

    /// <summary>The remote server closed the connection cleanly (FIN/EOF).</summary>
    RemoteClose = 2,

    /// <summary>The server sent an explicit kick/ban/logout packet.</summary>
    ServerKick = 3,

    /// <summary>A network I/O error occurred (socket reset, timeout, etc.).</summary>
    NetworkError = 4,

    /// <summary>The framing layer detected a malformed or oversized frame.</summary>
    FramingError = 5,

    /// <summary>The crypto layer detected a decryption or MAC verification failure.</summary>
    CryptoError = 6,

    /// <summary>The session idle timeout elapsed without activity.</summary>
    Timeout = 7
}