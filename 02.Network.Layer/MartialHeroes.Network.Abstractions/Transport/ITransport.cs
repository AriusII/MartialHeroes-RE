using MartialHeroes.Network.Abstractions.Session;

namespace MartialHeroes.Network.Abstractions.Transport;

public interface ITransport : IAsyncDisposable
{
    ValueTask<IConnectionSession> ConnectAsync(
        EndpointDescriptor endpoint,
        CancellationToken cancellationToken = default);
}