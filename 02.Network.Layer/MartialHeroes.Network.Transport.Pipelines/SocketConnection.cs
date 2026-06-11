using System.IO.Pipelines;
using System.Net.Sockets;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using MartialHeroes.Network.Abstractions.Transport;

namespace MartialHeroes.Network.Transport.Pipelines;

/// <summary>
/// Live TCP session backed by a <see cref="Socket"/> and two <see cref="System.IO.Pipelines.Pipe"/>
/// instances (one inbound, one outbound).
/// </summary>
/// <remarks>
/// <para>
/// The inbound path: the socket-receive loop feeds bytes into a <see cref="PipeWriter"/>;
/// the framing loop reads from the corresponding <see cref="PipeReader"/> via
/// <see cref="FrameSplitter.RunAsync"/> and dispatches complete frames to the
/// <see cref="IFrameSink"/> supplied at construction time.
/// </para>
/// <para>
/// The outbound path: <see cref="SendAsync"/> writes pre-framed bytes directly to the socket
/// (the frame is already assembled and encrypted by layers above; this layer only transmits).
/// A <see cref="SemaphoreSlim"/> serialises concurrent sends without allocating.
/// </para>
/// <para>
/// Backpressure: the inbound <see cref="System.IO.Pipelines.Pipe"/> is configured with pause/resume
/// thresholds (<see cref="FramingConstants.PipePauseThreshold"/> /
/// <see cref="FramingConstants.PipeResumeThreshold"/>). If the framing consumer stalls, the
/// socket-receive loop blocks on <see cref="PipeWriter.FlushAsync"/> rather than growing memory
/// unboundedly.
/// </para>
/// <para>
/// Lifecycle: both the receive and framing loops run as background <see cref="Task"/>s.  When
/// either loop exits (clean EOF, cancellation, or framing error) the session transitions to
/// <see cref="ConnectionState.Disconnected"/> or <see cref="ConnectionState.Faulted"/>
/// respectively, and fires <see cref="Disconnected"/>.
/// </para>
/// </remarks>
internal sealed class SocketConnection : IConnectionSession
{
    private readonly Socket _socket;
    private readonly IFrameSink _frameSink;
    private readonly Pipe _inboundPipe;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private volatile ConnectionState _state = ConnectionState.Connecting;
    private readonly CancellationTokenSource _shutdownCts = new();

    // Background loop tasks — initialised in StartAsync, never null after that.
    private Task _receiveLoopTask = Task.CompletedTask;
    private Task _frameLoopTask = Task.CompletedTask;

    private static long _sessionCounter; // incremented via Interlocked; cast to ulong for SessionId

    /// <inheritdoc/>
    public SessionId Id { get; }

    /// <inheritdoc/>
    public ConnectionState State => _state;

    /// <inheritdoc/>
    public event Action<SessionDisconnectedEventArgs>? Disconnected;

    /// <inheritdoc/>
    /// <remarks>
    /// Exposed so the framing loop can be driven externally (e.g. from tests using an
    /// in-memory <see cref="Pipe"/> instead of a real socket).
    /// </remarks>
    public PipeReader Input => _inboundPipe.Reader;

    internal SocketConnection(Socket socket, IFrameSink frameSink)
    {
        _socket = socket;
        _frameSink = frameSink;

        ulong id = (ulong)Interlocked.Increment(ref _sessionCounter);
        Id = new SessionId(id);

        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: FramingConstants.PipePauseThreshold,
            resumeWriterThreshold: FramingConstants.PipeResumeThreshold,
            useSynchronizationContext: false);
        _inboundPipe = new Pipe(pipeOptions);
    }

    /// <summary>
    /// Starts the background receive and framing loops.  Must be called once immediately
    /// after the socket is connected.
    /// </summary>
    internal void Start()
    {
        _state = ConnectionState.Handshaking;
        _receiveLoopTask = RunReceiveLoopAsync(_shutdownCts.Token);
        _frameLoopTask = RunFrameLoopAsync(_shutdownCts.Token);
    }

    // ------------------------------------------------------------------
    // IConnectionSession
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _state is ConnectionState.Disconnected or ConnectionState.Faulted, this);

        // Serialise sends so that multiple concurrent callers don't interleave frames.
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(frame, SocketFlags.None, cancellationToken)
                         .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisconnectAsync(
        DisconnectReason reason = DisconnectReason.LocalClose,
        CancellationToken cancellationToken = default)
    {
        if (_state is ConnectionState.Disconnected or ConnectionState.Faulted)
        {
            return;
        }

        await TeardownAsync(reason, exception: null).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await TeardownAsync(DisconnectReason.LocalClose, exception: null)
            .ConfigureAwait(false);

        _shutdownCts.Dispose();
        _sendLock.Dispose();
    }

    // ------------------------------------------------------------------
    // Receive loop: socket -> PipeWriter
    // ------------------------------------------------------------------

    private async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        PipeWriter writer = _inboundPipe.Writer;
        Exception? fault = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Request at least MinReceiveBufferSize bytes from the pipe's memory pool.
                Memory<byte> buffer = writer.GetMemory(FramingConstants.MinReceiveBufferSize);

                int bytesRead;
                try
                {
                    bytesRead = await _socket
                        .ReceiveAsync(buffer, SocketFlags.None, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    fault = ex;
                    break;
                }

                if (bytesRead == 0)
                {
                    // Clean remote EOF.
                    break;
                }

                writer.Advance(bytesRead);

                // Flush the pipe; honour backpressure — if the framing consumer is slow,
                // FlushAsync will block here until thresholds drop below the resume level.
                FlushResult flushResult;
                try
                {
                    flushResult = await writer.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (flushResult.IsCompleted || flushResult.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            fault = ex;
        }
        finally
        {
            // Complete the writer so the framing reader loop sees EOF.
            await writer.CompleteAsync(fault).ConfigureAwait(false);

            DisconnectReason reason = fault switch
            {
                SocketException => DisconnectReason.NetworkError,
                null => DisconnectReason.RemoteClose,
                _ => DisconnectReason.NetworkError,
            };

            // Only transition if we haven't already been torn down by the frame loop.
            await TeardownAsync(reason, fault).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // Frame loop: PipeReader -> IFrameSink
    // ------------------------------------------------------------------

    private async Task RunFrameLoopAsync(CancellationToken ct)
    {
        Exception? fault = null;
        DisconnectReason reason = DisconnectReason.RemoteClose;

        try
        {
            FrameSplitter.FrameLoopResult loopResult =
                await FrameSplitter.RunAsync(_inboundPipe.Reader, Id, _frameSink, ct)
                                   .ConfigureAwait(false);

            if (loopResult == FrameSplitter.FrameLoopResult.FramingError)
            {
                reason = DisconnectReason.FramingError;
            }
        }
        catch (OperationCanceledException)
        {
            reason = DisconnectReason.LocalClose;
        }
        catch (Exception ex)
        {
            fault = ex;
            reason = DisconnectReason.NetworkError;
        }
        finally
        {
            await _inboundPipe.Reader.CompleteAsync(fault).ConfigureAwait(false);
            await TeardownAsync(reason, fault).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // Shared teardown
    // ------------------------------------------------------------------

    // Guards against calling teardown more than once (race between the two loops).
    private int _teardownFlag;

    private async ValueTask TeardownAsync(DisconnectReason reason, Exception? exception)
    {
        // Ensure teardown runs at most once.
        if (Interlocked.Exchange(ref _teardownFlag, 1) != 0)
        {
            return;
        }

        ConnectionState finalState = reason is DisconnectReason.FramingError
                                     or DisconnectReason.NetworkError
                                     or DisconnectReason.CryptoError
            ? ConnectionState.Faulted
            : ConnectionState.Disconnected;

        _state = finalState;

        // Cancel the receive loop so it stops trying to read after a framing error.
        await _shutdownCts.CancelAsync().ConfigureAwait(false);

        // Shut down the socket cleanly.
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException) { /* already disconnected */ }
        catch (ObjectDisposedException) { /* already disposed */ }

        _socket.Close();

        // Wait for both loops to finish so resources are not released under their feet.
        try
        {
            await Task.WhenAll(_receiveLoopTask, _frameLoopTask).ConfigureAwait(false);
        }
        catch { /* exceptions were already captured per-loop */ }

        // Raise the disconnect event on a thread-pool thread to avoid re-entering callers.
        SessionDisconnectedEventArgs args = new(Id, reason, exception);
        _ = Task.Run(() => Disconnected?.Invoke(args));
    }
}
