namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed class ActormotionCatalogue
{
    private readonly Dictionary<int, ActormotionEntry> _byIntraOffset;
    private readonly Dictionary<uint, ActormotionEntry>? _byMotionKey;
    private readonly Dictionary<int, ActormotionEntry> _bySkinClass;

    internal ActormotionCatalogue(IReadOnlyList<ActormotionEntry> entries, bool motionKeysResolved)
    {
        AllEntries = entries;

        _byIntraOffset = new Dictionary<int, ActormotionEntry>(entries.Count);
        _bySkinClass = new Dictionary<int, ActormotionEntry>(entries.Count);
        foreach (var entry in entries)
        {
            _byIntraOffset.TryAdd(entry.Col1RawOffset, entry);
            _bySkinClass.TryAdd(entry.IntA, entry);
        }

        if (motionKeysResolved)
        {
            _byMotionKey = new Dictionary<uint, ActormotionEntry>(entries.Count);
            foreach (var entry in entries)
                _byMotionKey.TryAdd(entry.MotionKey, entry);
        }
    }

    public int Count => AllEntries.Count;

    public IReadOnlyList<ActormotionEntry> AllEntries { get; }

    public bool MotionKeysResolved => _byMotionKey is not null;

    public ActormotionEntry? GetByMotionKey(uint motionKey)
    {
        return _byMotionKey is not null && _byMotionKey.TryGetValue(motionKey, out var e) ? e : null;
    }

    public ActormotionEntry? GetByIntraOffset(int col1Value)
    {
        return _byIntraOffset.TryGetValue(col1Value, out var e) ? e : null;
    }

    public ActormotionEntry? GetBySkinClass(int skinClass)
    {
        return _bySkinClass.TryGetValue(skinClass, out var e) ? e : null;
    }
}