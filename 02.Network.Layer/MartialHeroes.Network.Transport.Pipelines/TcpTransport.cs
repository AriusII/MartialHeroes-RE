using System.Net;
using System.Net.Sockets;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Abstractions.Transport;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
/// <see cref="ITransport"/> implementation that dials TCP connections via
/// <see cref="System.Net.Sockets.Socket"/> and returns <see cref="SocketConnection"/> sessions
/// backed by <see cref="System.IO.Pipelines"/>.
/// </summary>
/// <remarks>
/// <para>
/// Constructed with an <see cref="IFrameSink"/> that receives all inbound frames for every
/// session this transport creates.  If sessions need independent sinks (e.g. login vs. world
/// connections), create one <see cref="TcpTransport"/> per sink.
/// </para>
/// <para>
/// This class is thread-safe: multiple concurrent calls to <see cref="ConnectAsync"/> are
/// supported; each produces an independent <see cref="SocketConnection"/>.
/// </para>
/// </remarks>
public sealed class TcpTransport : ITransport
{
    private readonly IFrameSink _frameSink;
    private volatile bool _disposed;

    /// <summary>
    /// Initialises a new <see cref="TcpTransport"/> that delivers inbound frames to
    /// <paramref name="frameSink"/>.
    /// </summary>
    /// <param name="frameSink">
    /// The frame sink that receives all decoded frames for every session produced by this
    /// transport.
    /// </param>
    public TcpTransport(IFrameSink frameSink)
    {
        ArgumentNullException.ThrowIfNull(frameSink);
        _frameSink = frameSink;
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">Thrown if this transport has been disposed.</exception>
    /// <exception cref="SocketException">
    /// Propagated from <see cref="Socket.ConnectAsync(EndPoint, CancellationToken)"/> if the
    /// TCP-level connect fails (unreachable host, port refused, etc.).
    /// </exception>
    public async ValueTask<IConnectionSession> ConnectAsync(
        EndpointDescriptor endpoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(endpoint);

        // Determine address family from the endpoint.
        AddressFamily af = endpoint.EndPoint switch
        {
            IPEndPoint ip => ip.AddressFamily,
            _ => AddressFamily.InterNetwork,
        };

        var socket = new Socket(af, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true, // disable Nagle — game traffic is latency-sensitive
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

        var connection = new SocketConnection(socket, _frameSink);
        connection.Start();
        return connection;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}