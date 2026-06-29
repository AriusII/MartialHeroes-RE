using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class MobSwordLightCatalogue
{
    private readonly SwordLightEntry[] _entries;
    private readonly Dictionary<uint, int> _keyToIndex;

    public MobSwordLightCatalogue(SwordLightEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;

        _keyToIndex = new Dictionary<uint, int>(entries.Length);
        for (var i = 0; i < entries.Length; i++)
            _keyToIndex.TryAdd(entries[i].Key, i);
    }

    public int Count => _entries.Length;

    public IReadOnlyList<SwordLightEntry> Entries => _entries;

    public bool TryGetByIndex(int index, out SwordLightEntry entry)
    {
        if ((uint)index < (uint)_entries.Length)
        {
            entry = _entries[index];
            return true;
        }

        entry = default;
        return false;
    }

    public bool TryGetByKey(uint mobId, out SwordLightEntry entry)
    {
        if (_keyToIndex.TryGetValue(mobId, out var index))
        {
            entry = _entries[index];
            return true;
        }

        entry = default;
        return false;
    }

    public static MobSwordLightCatalogue Load(MappedVfsArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (!archive.Contains(SwordLightDescriptorParser.MobVfsPath))
            return new MobSwordLightCatalogue([]);

        var data = archive.GetFileContent(SwordLightDescriptorParser.MobVfsPath);
        return new MobSwordLightCatalogue(SwordLightDescriptorParser.Parse(data, "mobswordlight.txt"));
    }
}