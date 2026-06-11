using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Abstractions.Transport;

/// <summary>
/// Factory and lifecycle manager for <see cref="IConnectionSession"/> instances.
/// Abstracts over the underlying duplex byte stream so that the rest of the stack is
/// agnostic to whether data flows over TCP, reliable UDP, or an in-memory simulation pipe.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by <c>Transport.Pipelines</c> using <c>System.IO.Pipelines</c> and
/// <c>System.Net.Sockets.Socket</c>. The in-memory test double implements the same interface
/// without touching any OS socket API.
/// </para>
/// <para>
/// A single <see cref="ITransport"/> instance may manage multiple concurrent sessions
/// (e.g. login session + world session). Each call to <see cref="ConnectAsync"/> issues a
/// new <see cref="IConnectionSession"/> with a unique <see cref="SessionId"/>.
/// </para>
/// </remarks>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Asynchronously establishes a new connection to <paramref name="endpoint"/> and returns
    /// the live session. The returned session is in at least <see cref="ConnectionState.Connecting"/>
    /// state; it advances to <see cref="ConnectionState.Handshaking"/> once the transport
    /// handshake completes.
    /// </summary>
    /// <param name="endpoint">The server endpoint to connect to.</param>
    /// <param name="cancellationToken">Token to cancel the connection attempt.</param>
    /// <returns>
    /// A <see cref="ValueTask{T}"/> that completes with the new session once the transport
    /// layer is ready. The caller must await <see cref="IConnectionSession.DisconnectAsync"/>
    /// or dispose the session to release resources.
    /// </returns>
    /// <exception cref="System.Net.Sockets.SocketException">
    /// Thrown (by the concrete implementation) if the transport-level connect fails.
    /// </exception>
    ValueTask<IConnectionSession> ConnectAsync(
        EndpointDescriptor endpoint,
        CancellationToken cancellationToken = default);
}
