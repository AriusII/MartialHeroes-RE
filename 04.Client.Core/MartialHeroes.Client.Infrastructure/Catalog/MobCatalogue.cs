using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class MobCatalogue
{
    private readonly Dictionary<ushort, MobRecord> _byId;

    public MobCatalogue(MobCatalogEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _byId = new Dictionary<ushort, MobRecord>(entries.Length);

        foreach (var e in entries)
        {
            var record = new MobRecord
            {
                Id = e.Id,

                Type = e.Type,

                Level = e.MobLevel,

                SpawnTimerSeconds = e.SpawnTimer,

                Raw = e.Raw
            };

            _byId[e.Id] = record;
        }
    }

    public int Count => _byId.Count;

    public static MobCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new MobCatalogue(loader.LoadMobsScr());
    }
}

public sealed record MobRecord
{
    public required ushort Id { get; init; }

    public required byte Type { get; init; }

    public required int Level { get; init; }

    public required uint SpawnTimerSeconds { get; init; }

    public bool IsBoss => Type == 11;

    public required ReadOnlyMemory<byte> Raw { get; init; }
}