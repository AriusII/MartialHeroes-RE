using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class NpcCatalogue
{
    private readonly Dictionary<uint, NpcCatalogueRecord> _byId;

    public NpcCatalogue(NpcScrRecord[] records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _byId = new Dictionary<uint, NpcCatalogueRecord>(records.Length);

        foreach (var row in records)
        {
            var record = new NpcCatalogueRecord
            {
                Id = row.Id,

                Kind = row.Kind,

                Job = row.Job,

                NameSlots = row.NameSlots
            };

            _byId[row.Id] = record;
        }
    }

    public int Count => _byId.Count;

    public NpcCatalogueRecord? GetById(uint id)
    {
        return _byId.GetValueOrDefault(id);
    }

    public static NpcCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new NpcCatalogue(loader.LoadNpcScr());
    }
}

public sealed record NpcCatalogueRecord
{
    public required uint Id { get; init; }

    public required byte Kind { get; init; }

    public required short Job { get; init; }

    public required string[] NameSlots { get; init; }
}