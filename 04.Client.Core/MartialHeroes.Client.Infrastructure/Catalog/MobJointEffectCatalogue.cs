using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Assets.Vfs;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class MobJointEffectCatalogue
{
    private static readonly JointEffectEntry[] Empty = [];

    private readonly Dictionary<long, JointEffectEntry[]> _byKey;
    private readonly MobJointEffectEntry[] _entries;

    public MobJointEffectCatalogue(MobJointEffectEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;

        var groups = new Dictionary<long, List<JointEffectEntry>>(entries.Length);
        foreach (var entry in entries)
        {
            var key = ComposeKey(entry.ClassToken, entry.OffsetToken);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<JointEffectEntry>(1);
                groups[key] = list;
            }

            list.Add(entry.Effect);
        }

        _byKey = new Dictionary<long, JointEffectEntry[]>(groups.Count);
        foreach (var pair in groups)
            _byKey[pair.Key] = pair.Value.ToArray();
    }

    public int Count => _entries.Length;

    public int KeyCount => _byKey.Count;

    public IReadOnlyList<MobJointEffectEntry> Entries => _entries;

    public bool TryGet(int classToken, int offsetToken, out JointEffectEntry entry)
    {
        if (_byKey.TryGetValue(ComposeKey(classToken, offsetToken), out var group) && group.Length > 0)
        {
            entry = group[0];
            return true;
        }

        entry = default;
        return false;
    }

    public IReadOnlyList<JointEffectEntry> GetAll(int classToken, int offsetToken)
    {
        return _byKey.TryGetValue(ComposeKey(classToken, offsetToken), out var group) ? group : Empty;
    }

    private static long ComposeKey(int classToken, int offsetToken)
    {
        return ((long)classToken << 32) | (uint)offsetToken;
    }

    public static MobJointEffectCatalogue Load(MappedVfsArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (!archive.Contains(MobJointEffectCatalogueParser.VfsPath))
            return new MobJointEffectCatalogue([]);

        var data = archive.GetFileContent(MobJointEffectCatalogueParser.VfsPath);
        return new MobJointEffectCatalogue(MobJointEffectCatalogueParser.Parse(data));
    }
}