using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Infrastructure.Cache;

/// <summary>
/// Cached static item data record. This is the client-side presentation view of
/// an item catalog entry — name, description, and item type code sufficient to
/// render inventory tooltips without a server round-trip.
/// </summary>
/// <param name="Id">The strongly-typed item instance / catalog id.</param>
/// <param name="Name">Localised display name.</param>
/// <param name="Description">Short tooltip description. May be empty but not null.</param>
/// <param name="ItemTypeCode">
/// Raw item-type byte from the server. Interpretation is handled by
/// <c>Client.Domain</c>; stored here as opaque data so the cache does not
/// take on game-rule knowledge.
/// </param>
public sealed record CachedItemDto(ItemId Id, string Name, string Description, byte ItemTypeCode);

/// <summary>
/// Offline cache for static item catalog data.
/// <para>
/// This is a <em>cache</em>: the server (and <c>Client.Domain</c>) remains the
/// authority once connected. The cache merely avoids re-querying the network for
/// static data across sessions.
/// </para>
/// <para>
/// All I/O is async with <see cref="CancellationToken"/> support.
/// Implementations must support an in-memory or temp-file SQLite path for tests.
/// </para>
/// </summary>
public interface IItemCacheStore : IAsyncDisposable
{
    /// <summary>
    /// Ensures the schema is up to date and the store is ready.
    /// Must be called once before any other method.
    /// </summary>
    Task InitialiseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached entry for <paramref name="itemId"/>, or
    /// <see langword="null"/> if the item is not in the cache.
    /// </summary>
    Task<CachedItemDto?> TryGetAsync(ItemId itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a single item entry. Safe to call repeatedly; later calls win on conflict.
    /// </summary>
    Task UpsertAsync(CachedItemDto item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-upserts a collection of items in a single transaction.
    /// Entries already present are overwritten with the new values.
    /// </summary>
    Task BulkUpsertAsync(IEnumerable<CachedItemDto> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all entries from the item cache. Use when the server signals a
    /// full catalog version change.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}