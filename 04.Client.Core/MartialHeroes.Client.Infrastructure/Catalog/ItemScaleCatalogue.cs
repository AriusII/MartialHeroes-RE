using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class ItemScaleCatalogue
{
    private readonly Dictionary<uint, float> _byItemId;

    public ItemScaleCatalogue(ItemScaleRecord[] records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _byItemId = new Dictionary<uint, float>(records.Length);

        foreach (var row in records)
            _byItemId[row.ItemId] = row.Scale;
    }

    public int Count => _byItemId.Count;

    public bool TryGetScale(uint itemId, out float scale)
    {
        return _byItemId.TryGetValue(itemId, out scale);
    }

    public static ItemScaleCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new ItemScaleCatalogue(loader.LoadItemScaleScr());
    }
}