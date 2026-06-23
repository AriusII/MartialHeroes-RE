using System.IO.Pipelines;
using System.Net.Sockets;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Transport.Pipelines;

internal sealed class SocketConnection : IConnectionSession
{
    private static long _sessionCounter;
    private readonly InboundDecompressDelegate? _decompress;
    private readonly IFrameSink _frameSink;
    private readonly Pipe _inboundPipe;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Socket _socket;
    private Task _frameLoopTask = Task.CompletedTask;

    private Task _receiveLoopTask = Task.CompletedTask;

    private volatile ConnectionState _state = ConnectionState.Connecting;


    private int _teardownFlag;

    internal SocketConnection(
        Socket socket,
        IFrameSink frameSink,
        InboundDecompressDelegate? decompress = null)
    {
        _socket = socket;
        _frameSink = frameSink;
        _decompress = decompress;

        var id = (ulong)Interlocked.Increment(ref _sessionCounter);
        Id = new SessionId(id);

        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: FramingConstants.PipePauseThreshold,
            resumeWriterThreshold: FramingConstants.PipeResumeThreshold,
            useSynchronizationContext: false);
        _inboundPipe = new Pipe(pipeOptions);
    }

    public SessionId Id { get; }

    public ConnectionState State => _state;

    public event Action<SessionDisconnectedEventArgs>? Disconnected;

    public PipeReader Input => _inboundPipe.Reader;


    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _state is ConnectionState.Disconnected or ConnectionState.Faulted, this);

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

    public async ValueTask DisconnectAsync(
        DisconnectReason reason = DisconnectReason.LocalClose,
        CancellationToken cancellationToken = default)
    {
        if (_state is ConnectionState.Disconnected or ConnectionState.Faulted) return;

        await TeardownAsync(reason, null).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await TeardownAsync(DisconnectReason.LocalClose, null)
            .ConfigureAwait(false);

        _shutdownCts.Dispose();
        _sendLock.Dispose();
    }

    internal void Start()
    {
        _state = ConnectionState.Handshaking;
        _receiveLoopTask = RunReceiveLoopAsync(_shutdownCts.Token);
        _frameLoopTask = RunFrameLoopAsync(_shutdownCts.Token);
    }


    private async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        var writer = _inboundPipe.Writer;
        Exception? fault = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var buffer = writer.GetMemory(FramingConstants.MinReceiveBufferSize);

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
                    break;

                writer.Advance(bytesRead);

                FlushResult flushResult;
                try
                {
                    flushResult = await writer.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (flushResult.IsCompleted || flushResult.IsCanceled) break;
            }
        }
        catch (Exception ex)
        {
            fault = ex;
        }
        finally
        {
            await writer.CompleteAsync(fault).ConfigureAwait(false);

            var reason = fault switch
            {
                SocketException => DisconnectReason.NetworkError,
                null => DisconnectReason.RemoteClose,
                _ => DisconnectReason.NetworkError
            };

            await TeardownAsync(reason, fault).ConfigureAwait(false);
        }
    }


    private async Task RunFrameLoopAsync(CancellationToken ct)
    {
        Exception? fault = null;
        var reason = DisconnectReason.RemoteClose;

        try
        {
            var loopResult =
                await FrameSplitter.RunAsync(_inboundPipe.Reader, Id, _frameSink, ct, _decompress)
                    .ConfigureAwait(false);

            if (loopResult == FrameSplitter.FrameLoopResult.FramingError) reason = DisconnectReason.FramingError;
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

    private async ValueTask TeardownAsync(DisconnectReason reason, Exception? exception)
    {
        if (Interlocked.Exchange(ref _teardownFlag, 1) != 0) return;

        var finalState = reason is DisconnectReason.FramingError
            or DisconnectReason.NetworkError
            or DisconnectReason.CryptoError
            ? ConnectionState.Faulted
            : ConnectionState.Disconnected;

        _state = finalState;

        await _shutdownCts.CancelAsync().ConfigureAwait(false);

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        _socket.Close();

        try
        {
            await Task.WhenAll(_receiveLoopTask, _frameLoopTask).ConfigureAwait(false);
        }
        catch
        {
        }

        SessionDisconnectedEventArgs args = new(Id, reason, exception);
        _ = Task.Run(() => Disconnected?.Invoke(args));
    }
}