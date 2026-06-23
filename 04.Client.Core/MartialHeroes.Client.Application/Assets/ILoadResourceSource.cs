namespace MartialHeroes.Client.Application.Assets;

public interface ILoadResourceSource
{
    ValueTask<long> LoadAsync(string logicalPath, CancellationToken cancellationToken = default);
}