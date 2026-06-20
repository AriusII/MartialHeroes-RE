using System.Net;

namespace MartialHeroes.Network.Abstractions.Transport;

/// <summary>
///     Immutable description of a remote server endpoint that <see cref="ITransport" /> should
///     connect to. Wraps a standard <see cref="System.Net.EndPoint" /> so that the abstraction
///     works for both IPv4/IPv6 TCP (the common case) and any other addressable transport.
/// </summary>
/// <param name="EndPoint">
///     The target endpoint. For the game server this will be an <see cref="IPEndPoint" />.
///     For the in-memory offline simulation transport it may be any synthetic endpoint type.
/// </param>
/// <param name="DisplayName">
///     Human-readable label used in diagnostics and log output (e.g. <c>"game-auth-01"</c>).
///     Never sent on the wire.
/// </param>
public sealed record EndpointDescriptor(EndPoint EndPoint, string DisplayName = "")
{
    /// <inheritdoc />
    public override string ToString()
    {
        return DisplayName.Length > 0 ? $"{DisplayName} ({EndPoint})" : EndPoint.ToString()!;
    }
}