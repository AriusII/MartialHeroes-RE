using MartialHeroes.Client.Infrastructure.Cache;
using MartialHeroes.Shared.Kernel.Ids;
using Xunit;

namespace MartialHeroes.Client.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="SqliteItemCacheStore"/> using in-memory SQLite.
/// </summary>
public sealed class ItemCacheStoreTests
{
    private static string InMemoryConnectionString(string testName) =>
        $"Data Source=file:{testName}?mode=memory&cache=shared";

    [Fact]
    public async Task UpsertAndGet_RoundTripsItemCorrectly()
    {
        await using var store = new SqliteItemCacheStore(InMemoryConnectionString("item_upsert"));
        await store.InitialiseAsync();

        var item = new CachedItemDto(
            Id:           new ItemId(42u),
            Name:         "Iron Sword",
            Description:  "A standard iron blade.",
            ItemTypeCode: 1);

        await store.UpsertAsync(item);

        var loaded = await store.TryGetAsync(new ItemId(42u));

        Assert.NotNull(loaded);
        Assert.Equal(new ItemId(42u), loaded.Id);
        Assert.Equal("Iron Sword",          loaded.Name);
        Assert.Equal("A standard iron blade.", loaded.Description);
        Assert.Equal((byte)1,               loaded.ItemTypeCode);
    }

    [Fact]
    public async Task TryGet_MissingItem_ReturnsNull()
    {
        await using var store = new SqliteItemCacheStore(InMemoryConnectionString("item_miss"));
        await store.InitialiseAsync();

        var result = await store.TryGetAsync(new ItemId(9999u));

        Assert.Null(result);
    }

    [Fact]
    public async Task Upsert_OverwritesExistingEntry()
    {
        await using var store = new SqliteItemCacheStore(InMemoryConnectionString("item_overwrite"));
        await store.InitialiseAsync();

        var id = new ItemId(7u);
        await store.UpsertAsync(new CachedItemDto(id, "Old Name", "Old Desc", 0));
        await store.UpsertAsync(new CachedItemDto(id, "New Name", "New Desc", 2));

        var loaded = await store.TryGetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal("New Name", loaded.Name);
        Assert.Equal("New Desc", loaded.Description);
        Assert.Equal((byte)2,    loaded.ItemTypeCode);
    }

    [Fact]
    public async Task BulkUpsert_InsertsAllItems()
    {
        await using var store = new SqliteItemCacheStore(InMemoryConnectionString("item_bulk"));
        await store.InitialiseAsync();

        var items = Enumerable.Range(1, 100)
            .Select(i => new CachedItemDto(
                new ItemId((uint)i),
                $"Item {i}",
                $"Desc {i}",
                (byte)(i % 5)))
            .ToList();

        await store.BulkUpsertAsync(items);

        for (var i = 1; i <= 100; i++)
        {
            var loaded = await store.TryGetAsync(new ItemId((uint)i));
            Assert.NotNull(loaded);
            Assert.Equal($"Item {i}", loaded.Name);
        }
    }

    [Fact]
    public async Task BulkUpsert_OverwritesExistingEntries()
    {
        await using var store = new SqliteItemCacheStore(InMemoryConnectionString("item_bulk_overwrite"));
        await store.InitialiseAsync();

        var id = new ItemId(1u);
        await store.UpsertAsync(new CachedItemDto(id, "Original", "Orig Desc", 0));

        await store.BulkUpsertAsync([new CachedItemDto(id, "Updated", "Upd Desc", 3)]);

        var loaded = await store.TryGetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated", loaded.Name);
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        await using var store = new SqliteItemCacheStore(InMemoryConnectionString("item_clear"));
        await store.InitialiseAsync();

        await store.BulkUpsertAsync([
            new CachedItemDto(new ItemId(1u), "A", "", 0),
            new CachedItemDto(new ItemId(2u), "B", "", 0),
        ]);

        await store.ClearAsync();

        Assert.Null(await store.TryGetAsync(new ItemId(1u)));
        Assert.Null(await store.TryGetAsync(new ItemId(2u)));
    }

    [Fact]
    public async Task ItemId_MaxUint_RoundTrips()
    {
        // Verifies the int64 cast logic handles uint.MaxValue without sign corruption.
        await using var store = new SqliteItemCacheStore(InMemoryConnectionString("item_maxuint"));
        await store.InitialiseAsync();

        var id = new ItemId(uint.MaxValue);
        await store.UpsertAsync(new CachedItemDto(id, "Max Item", "", 0));

        var loaded = await store.TryGetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal(id, loaded.Id);
    }
}
