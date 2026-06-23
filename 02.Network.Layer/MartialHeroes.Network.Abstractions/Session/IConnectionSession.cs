using System.IO.Pipelines;

namespace MartialHeroes.Network.Abstractions.Session;

public interface IConnectionSession : IAsyncDisposable
{
    SessionId Id { get; }

    ConnectionState State { get; }

    PipeReader Input { get; }

    event Action<SessionDisconnectedEventArgs>? Disconnected;

    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken = default);

    ValueTask DisconnectAsync(
        DisconnectReason reason = DisconnectReason.LocalClose,
        CancellationToken cancellationToken = default);
}