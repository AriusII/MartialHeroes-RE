using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class JointEffectCatalogue
{
    private static readonly JointEffectEntry[] Empty = [];

    private readonly Dictionary<uint, JointEffectEntry[]> _byMapKey;
    private readonly JointEffectEntry[] _entries;

    public JointEffectCatalogue(JointEffectEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;

        var groups = new Dictionary<uint, List<JointEffectEntry>>(entries.Length);
        foreach (var entry in entries)
        {
            if (!groups.TryGetValue(entry.MapKey, out var list))
            {
                list = new List<JointEffectEntry>(1);
                groups[entry.MapKey] = list;
            }

            list.Add(entry);
        }

        _byMapKey = new Dictionary<uint, JointEffectEntry[]>(groups.Count);
        foreach (var pair in groups)
            _byMapKey[pair.Key] = pair.Value.ToArray();
    }

    public int Count => _entries.Length;

    public int KeyCount => _byMapKey.Count;

    public IReadOnlyList<JointEffectEntry> Entries => _entries;

    public bool TryGet(uint mapKey, out JointEffectEntry entry)
    {
        if (_byMapKey.TryGetValue(mapKey, out var group) && group.Length > 0)
        {
            entry = group[0];
            return true;
        }

        entry = default;
        return false;
    }

    public IReadOnlyList<JointEffectEntry> GetAll(uint mapKey)
    {
        return _byMapKey.TryGetValue(mapKey, out var group) ? group : Empty;
    }

    public static JointEffectCatalogue Load(MappedVfsArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (!archive.Contains(ItemJointEffectCatalogueParser.VfsPath))
            return new JointEffectCatalogue([]);

        var data = archive.GetFileContent(ItemJointEffectCatalogueParser.VfsPath);
        return new JointEffectCatalogue(ItemJointEffectCatalogueParser.Parse(data));
    }
}
