using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class SwordLightCatalogue
{
    public const string TexturePrefix = "data/effect/texture/";

    private readonly Dictionary<uint, SwordLightEntry> _byItemId;
    private readonly SwordLightEntry[] _entries;

    public SwordLightCatalogue(SwordLightEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;

        _byItemId = new Dictionary<uint, SwordLightEntry>(entries.Length);
        foreach (var entry in entries)
            _byItemId.TryAdd(entry.Key, entry);
    }

    public int Count => _entries.Length;

    public IReadOnlyList<SwordLightEntry> Entries => _entries;

    public bool TryGet(uint itemId, out SwordLightEntry entry)
    {
        return _byItemId.TryGetValue(itemId, out entry);
    }

    public static SwordLightCatalogue Load(MappedVfsArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (!archive.Contains(SwordLightDescriptorParser.ItemVfsPath))
            return new SwordLightCatalogue([]);

        var data = archive.GetFileContent(SwordLightDescriptorParser.ItemVfsPath);
        return new SwordLightCatalogue(SwordLightDescriptorParser.Parse(data, "itemswordlight.txt"));
    }
}
