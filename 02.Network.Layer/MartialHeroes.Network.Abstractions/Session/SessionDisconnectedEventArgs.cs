namespace MartialHeroes.Network.Abstractions.Session;

/// <summary>
///     Event data delivered when a <see cref="IConnectionSession" /> transitions to
///     <see cref="ConnectionState.Disconnected" /> or <see cref="ConnectionState.Faulted" />.
/// </summary>
/// <param name="SessionId">The identity of the session that disconnected.</param>
/// <param name="Reason">Why the session ended.</param>
/// <param name="Exception">
///     The exception that caused the disconnect, if any; <see langword="null" /> for clean or
///     server-initiated disconnects.
/// </param>
public sealed record SessionDisconnectedEventArgs(
    SessionId SessionId,
    DisconnectReason Reason,
    Exception? Exception = null);