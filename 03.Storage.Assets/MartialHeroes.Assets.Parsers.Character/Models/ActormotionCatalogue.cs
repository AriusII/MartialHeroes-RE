namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed class ActormotionCatalogue
{
    private readonly Dictionary<int, ActormotionEntry> _byIntraOffset;
    private readonly Dictionary<uint, ActormotionEntry> _byMotionKey;
    private readonly Dictionary<int, ActormotionEntry> _bySkinClass;

    internal ActormotionCatalogue(Dictionary<uint, ActormotionEntry> byMotionKey)
    {
        _byMotionKey = byMotionKey;

        _byIntraOffset = new Dictionary<int, ActormotionEntry>(byMotionKey.Count);
        foreach (var entry in byMotionKey.Values)
            _byIntraOffset.TryAdd(entry.Col1RawOffset, entry);

        _bySkinClass = new Dictionary<int, ActormotionEntry>(byMotionKey.Count);
        foreach (var entry in byMotionKey.Values)
            _bySkinClass.TryAdd(entry.IntA, entry);
    }

    public int Count => _byMotionKey.Count;

    public IEnumerable<ActormotionEntry> AllEntries => _byMotionKey.Values;

    public ActormotionEntry? GetByMotionKey(uint motionKey)
    {
        return _byMotionKey.TryGetValue(motionKey, out var e) ? e : null;
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