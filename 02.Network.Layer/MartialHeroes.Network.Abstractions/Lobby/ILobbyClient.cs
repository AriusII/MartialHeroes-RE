namespace MartialHeroes.Network.Abstractions.Lobby;

/// <summary>
///     Contract for the lobby discovery surface: fetching the server list and resolving a
///     game-server endpoint for a selected server, both from the lobby mini-protocol on port 10000.
/// </summary>
/// <remarks>
///     <para>
///         The lobby is a <b>separate, synchronous, plaintext-framed</b> TCP surface, distinct from the
///         persistent game connection. It uses the 8-byte frame wrapper and inbound LZ4 decompression
///         but has <b>no</b> <c>(major:minor)</c> dispatch and <b>no</b> byte cipher.
///         <br />
///         spec: Docs/RE/packets/lobby.yaml §LOGIN-SERVER vs GAME-SERVER SPLIT;
///         Docs/RE/specs/login_flow.md §2.
///     </para>
///     <para>
///         <b>Implemented by:</b> <c>Network.Transport.Pipelines</c> (the concrete <c>LobbyClient</c>
///         class that opens blocking throwaway sockets on port 10000 / <c>10000 + serverId</c>). The
///         methods are declared <c>Task</c>-returning so that the application layer can always
///         <c>await</c> them — even though the legacy client used synchronous blocking, the transport
///         engineer is free to wrap the blocking socket I/O in a <c>Task.Run</c> or to replace it with
///         a genuine async implementation. The application layer must never call <see langword="this" />
///         from a UI thread without awaiting.
///     </para>
///     <para>
///         <b>Consumed by:</b> <c>Client.Application</c> (login use-case), which injects this contract
///         without taking a compile-time dependency on <c>Network.Transport.Pipelines</c>. The app
///         layer never references the concrete transport class.
///     </para>
///     <para>
///         The lobby base port is <b>10000</b>. The channel query port is
///         <c>10000 + <paramref name="serverId" /></c>, where <paramref name="serverId" /> is the numeric
///         id from the chosen <see cref="LobbyServerRecord.ServerId" /> (values 1..40, corresponding to
///         ports 10001..10040).
///         <br />
///         spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B coupling note;
///         Docs/RE/specs/login_flow.md §7 (lobby base port constant).
///     </para>
/// </remarks>
public interface ILobbyClient
{
    /// <summary>
    ///     Fetches the server list from the lobby (port 10000) and returns the decoded records.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The lobby wrapper's <c>major</c> field carries the record count; the decompressed payload
    ///         is <c>count</c> × 8-byte little-endian <see cref="LobbyServerRecord" /> entries.
    ///         <br />
    ///         spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE A — SERVER-LIST record;
    ///         Docs/RE/specs/login_flow.md §2.1.
    ///     </para>
    ///     <para>
    ///         On connect failure the legacy client records a sentinel count of <c>-1</c>. Implementations
    ///         should either throw (allowing the caller to handle) or return an empty list; they must
    ///         not return null.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    ///     A read-only list of <see cref="LobbyServerRecord" /> values decoded from the lobby
    ///     response. The list is empty when the server reports a count of zero; it is never null.
    /// </returns>
    /// <exception cref="System.Net.Sockets.SocketException">
    ///     The implementing transport may throw if the lobby TCP connect fails.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken" /> is cancelled.
    /// </exception>
    Task<IReadOnlyList<LobbyServerRecord>> FetchServerListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fetches the game-server endpoint for the chosen server from the lobby channel port
    ///     (<c>10000 + serverId</c>) and returns the decoded <see cref="LobbyChannelEndpoint" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The channel thread zero-fills a 30-byte endpoint field, then copies the first 30 (0x1E)
    ///         bytes of the decompressed payload verbatim as a fixed ASCII endpoint token. The 30 source
    ///         bytes are <b>not</b> guaranteed NUL-terminated. The legacy client consumed this token
    ///         opaquely (it did not itself parse host/port out of it in the channel thread); the exact
    ///         internal delimiter (e.g. <c>"host:port"</c> vs <c>"host port"</c> vs
    ///         <c>"host\0port"</c> vs fixed sub-fields) is <b>NEEDS-CAPTURE</b> — not statically
    ///         determinable. A whitespace-split is a reasonable implementation choice pending a live
    ///         capture that confirms the delimiter.
    ///         <br />
    ///         spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B — CHANNEL-ENDPOINT text
    ///         [CODE-CONFIRMED: 30 bytes copied, consumed opaquely; delimiter = NEEDS-CAPTURE].
    ///     </para>
    ///     <para>
    ///         The returned <see cref="LobbyChannelEndpoint" /> names the <b>game server</b>, not the
    ///         lobby. After obtaining this record the caller hands the host and port to
    ///         <c>ITransport.ConnectAsync</c> to open the persistent game connection.
    ///     </para>
    /// </remarks>
    /// <param name="serverId">
    ///     The numeric server id from the chosen <see cref="LobbyServerRecord.ServerId" /> (values
    ///     1..40). Added directly to 10000 to form the channel TCP port.
    ///     <br />
    ///     spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE B coupling note;
    ///     Docs/RE/specs/login_flow.md §2.2.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    ///     The <see cref="LobbyChannelEndpoint" /> (host + port) of the game server to connect to.
    /// </returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    ///     The implementing transport should validate that <paramref name="serverId" /> is in range
    ///     (1..40) and throw if not.
    /// </exception>
    /// <exception cref="System.Net.Sockets.SocketException">
    ///     The implementing transport may throw if the lobby TCP connect fails.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken" /> is cancelled.
    /// </exception>
    Task<LobbyChannelEndpoint> FetchChannelEndpointAsync(
        ushort serverId,
        CancellationToken cancellationToken = default);
}