namespace MartialHeroes.Client.Application.Engine;

public interface IGameClock
{
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default);
}