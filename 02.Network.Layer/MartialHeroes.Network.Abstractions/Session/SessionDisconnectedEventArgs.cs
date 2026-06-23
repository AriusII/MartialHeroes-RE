namespace MartialHeroes.Network.Abstractions.Session;

public sealed record SessionDisconnectedEventArgs(
    SessionId SessionId,
    DisconnectReason Reason,
    Exception? Exception = null);