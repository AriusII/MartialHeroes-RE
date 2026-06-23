using System.Net;
using System.Net.Sockets;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Abstractions.Transport;

namespace MartialHeroes.Network.Transport.Pipelines;

public sealed class TcpTransport : ITransport
{
    private readonly InboundDecompressDelegate? _decompress;
    private readonly IFrameSink _frameSink;
    private volatile bool _disposed;

    public TcpTransport(
        IFrameSink frameSink,
        InboundDecompressDelegate? decompress = null)
    {
        ArgumentNullException.ThrowIfNull(frameSink);
        _frameSink = frameSink;
        _decompress = decompress;
    }

    public async ValueTask<IConnectionSession> ConnectAsync(
        EndpointDescriptor endpoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(endpoint);

        var af = endpoint.EndPoint switch
        {
            IPEndPoint ip => ip.AddressFamily,
            _ => AddressFamily.InterNetwork
        };

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

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}