using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Infrastructure.Cache;

public sealed record CachedItemDto(ItemId Id, string Name, string Description, byte ItemTypeCode);

public interface IItemCacheStore : IAsyncDisposable
{
    Task InitialiseAsync(CancellationToken cancellationToken = default);

    Task<CachedItemDto?> TryGetAsync(ItemId itemId, CancellationToken cancellationToken = default);

    Task UpsertAsync(CachedItemDto item, CancellationToken cancellationToken = default);

    Task BulkUpsertAsync(IEnumerable<CachedItemDto> items, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}