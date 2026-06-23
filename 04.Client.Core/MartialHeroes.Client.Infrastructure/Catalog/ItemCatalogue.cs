using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class ItemCatalogue
{
    private readonly Dictionary<uint, ItemCatalogueRecord> _byId;

    public ItemCatalogue(IReadOnlyList<ItemsScrRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _byId = new Dictionary<uint, ItemCatalogueRecord>(records.Count);

        foreach (var row in records)
        {
            var record = new ItemCatalogueRecord
            {
                Name = row.ItemName,

                ItemId = row.ItemUid,

                Description = row.ItemDesc,

                ModelRefKey = row.ModelRefKey,

                AnimRefKey = row.AnimRefKey,

                RecordDiscriminator = row.RecordDiscriminator,

                EffectCount = row.EffectCount
            };

            _byId[row.ItemUid] = record;
        }
    }

    public int Count => _byId.Count;

    public static ItemCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new ItemCatalogue(loader.LoadItemsScr());
    }

    public ItemCatalogueRecord? TryGet(uint itemId)
    {
        return _byId.TryGetValue(itemId, out var r) ? r : null;
    }
}

public sealed record ItemCatalogueRecord
{
    public required string Name { get; init; }

    public required uint ItemId { get; init; }

    public required string Description { get; init; }

    public required uint ModelRefKey { get; init; }

    public required uint AnimRefKey { get; init; }

    public required byte RecordDiscriminator { get; init; }

    public required byte EffectCount { get; init; }
}