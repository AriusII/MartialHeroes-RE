using System.Net;
using System.Net.Sockets;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Abstractions.Transport;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
///     <see cref="ITransport" /> implementation that dials TCP connections via
///     <see cref="System.Net.Sockets.Socket" /> and returns <see cref="SocketConnection" /> sessions
///     backed by <see cref="System.IO.Pipelines" />.
/// </summary>
/// <remarks>
///     <para>
///         Constructed with an <see cref="IFrameSink" /> that receives all inbound frames for every
///         session this transport creates.  If sessions need independent sinks (e.g. login vs. world
///         connections), create one <see cref="TcpTransport" /> per sink.
///     </para>
///     <para>
///         This class is thread-safe: multiple concurrent calls to <see cref="ConnectAsync" /> are
///         supported; each produces an independent <see cref="SocketConnection" />.
///     </para>
/// </remarks>
public sealed class TcpTransport : ITransport
{
    private readonly InboundDecompressDelegate? _decompress;
    private readonly IFrameSink _frameSink;
    private volatile bool _disposed;

    /// <summary>
    ///     Initialises a new <see cref="TcpTransport" /> that delivers inbound frames to
    ///     <paramref name="frameSink" />.
    /// </summary>
    /// <param name="frameSink">
    ///     The frame sink that receives all decoded frames for every session produced by this
    ///     transport.
    /// </param>
    /// <param name="decompress">
    ///     Optional LZ4 raw-block decompression delegate applied to every non-empty inbound payload
    ///     before it is handed to <paramref name="frameSink" />. Supply
    ///     <c>PayloadCompression.DecompressPayload</c> from <c>Network.Crypto</c> at the composition
    ///     root. When <see langword="null" /> payloads are forwarded raw (useful for tests or when the
    ///     inbound path is known to be uncompressed).
    ///     spec: Docs/RE/specs/crypto.md §5 — inbound is compressed-only (no inverse cipher).
    /// </param>
    public TcpTransport(
        IFrameSink frameSink,
        InboundDecompressDelegate? decompress = null)
    {
        ArgumentNullException.ThrowIfNull(frameSink);
        _frameSink = frameSink;
        _decompress = decompress;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">Thrown if this transport has been disposed.</exception>
    /// <exception cref="SocketException">
    ///     Propagated from <see cref="Socket.ConnectAsync(EndPoint, CancellationToken)" /> if the
    ///     TCP-level connect fails (unreachable host, port refused, etc.).
    /// </exception>
    public async ValueTask<IConnectionSession> ConnectAsync(
        EndpointDescriptor endpoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(endpoint);

        // Determine address family from the endpoint.
        var af = endpoint.EndPoint switch
        {
            IPEndPoint ip => ip.AddressFamily,
            _ => AddressFamily.InterNetwork
        };

        // Nagle MUST stay ENABLED on the game connection (NoDelay = FALSE). CYCLE-4 LIVE-PROVEN
        // (x32dbg + NetSmoke vs the live replica): with NoDelay = true the server REJECTS the enter-game
        // 1/9 (replies 3/100, no world snapshot) even though the 1/9 is byte-identical to the real
        // client's; with NoDelay = false (Nagle on) the same 1/9 is ACCEPTED and the server sends the
        // 4/1 SmsgGameStateTick world snapshot. The original "disable Nagle, latency-sensitive" reading
        // was wrong — the server requires the default Nagle behaviour. Override to true only via
        // MH_TCP_NODELAY=1 (diagnostics).
        // spec: Docs/RE/specs/net_contracts.md §1.1 (Nagle coalescing — client never sets TCP_NODELAY).
        var noDelay = Environment.GetEnvironmentVariable("MH_TCP_NODELAY") == "1";
        var socket = new Socket(af, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = noDelay
        };

        try
        {
            await socket.ConnectAsync(endpoint.EndPoint, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        var connection = new SocketConnection(socket, _frameSink, _decompress);
        connection.Start();
        return connection;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}