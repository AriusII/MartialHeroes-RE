using System.IO.Pipelines;

namespace MartialHeroes.Network.Abstractions.Session;

/// <summary>
/// Represents a live, transport-agnostic connection to the game server.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by <c>Transport.Pipelines</c> (TCP socket via <see cref="PipeReader"/>/<see cref="PipeWriter"/>)
/// and by any in-memory offline simulation transport used in headless testing.
/// </para>
/// <para>
/// The session is the primary outbound send surface. Callers in <c>Client.Application</c>
/// obtain a session from <see cref="ITransport"/> and call <see cref="SendAsync"/> with an
/// already-encrypted, already-framed <see cref="ReadOnlyMemory{T}"/> payload — no managed
/// string, no intermediate array.
/// </para>
/// <para>
/// State-change notifications are delivered via <see cref="Disconnected"/>; there is no
/// separate "Connected" event because the session is only exposed to consumers after it has
/// reached at least <see cref="ConnectionState.Connecting"/>.
/// </para>
/// </remarks>
public interface IConnectionSession : IAsyncDisposable
{
    /// <summary>
    /// Stable identity for this connection attempt. A new <see cref="SessionId"/> is issued for
    /// every new physical connection.
    /// </summary>
    SessionId Id { get; }

    /// <summary>
    /// The current lifecycle state of the connection. Read-only from the consumer's perspective;
    /// only the transport implementation drives state transitions.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Raised when the session transitions to <see cref="ConnectionState.Disconnected"/> or
    /// <see cref="ConnectionState.Faulted"/>. May be raised on any thread; subscribers must be
    /// thread-safe and must not block or perform long work on the callback.
    /// </summary>
    event Action<SessionDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Asynchronously writes a single outbound <paramref name="frame"/> to the wire.
    /// The <paramref name="frame"/> must already be framed (8-byte header prepended) and
    /// encrypted by <c>Network.Crypto</c> before this call.
    /// </summary>
    /// <param name="frame">
    /// The complete, framed, encrypted frame. The memory must remain valid until the returned
    /// <see cref="ValueTask"/> completes.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the pending write.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes when the bytes have been handed to the
    /// underlying transport buffer. Completion does not guarantee TCP-level delivery.
    /// </returns>
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exposes the inbound byte stream as a <see cref="PipeReader"/> so that
    /// <c>Transport.Pipelines</c> can perform zero-copy framing and hand slices to
    /// <c>Network.Crypto</c> for in-place decryption.
    /// </summary>
    /// <remarks>
    /// Consumers above the transport layer (i.e. <c>Network.Protocol</c> and
    /// <c>Client.Application</c>) never call this directly; they receive decoded frames via
    /// <see cref="IFrameSink"/>. This surface exists so the transport can wire up the pipeline
    /// reader loop.
    /// </remarks>
    PipeReader Input { get; }

    /// <summary>
    /// Initiates a clean, graceful disconnect. After this call the session transitions to
    /// <see cref="ConnectionState.Disconnected"/> and <see cref="Disconnected"/> is raised.
    /// Calling this on an already-disconnected or faulted session is a no-op.
    /// </summary>
    /// <param name="reason">The application-layer reason for the disconnect.</param>
    /// <param name="cancellationToken">Token to abort the shutdown sequence if needed.</param>
    ValueTask DisconnectAsync(
        DisconnectReason reason = DisconnectReason.LocalClose,
        CancellationToken cancellationToken = default);
}
