namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed record EmoticonEntry(
    int EmoteId,
    string EmoteName,
    int EnterState,
    int NextState,
    IReadOnlyList<int> AnimIds);

public sealed class EmoticonTable
{
    private readonly Dictionary<int, EmoticonEntry> _byEmoteId;

    internal EmoticonTable(IReadOnlyList<EmoticonEntry> entries)
    {
        Entries = entries;
        _byEmoteId = new Dictionary<int, EmoticonEntry>(entries.Count);
        foreach (var entry in entries)
            _byEmoteId.TryAdd(entry.EmoteId, entry);
    }

    public int Count => Entries.Count;

    public IReadOnlyList<EmoticonEntry> Entries { get; }

    public EmoticonEntry? GetByEmoteId(int emoteId)
    {
        return _byEmoteId.TryGetValue(emoteId, out var entry) ? entry : null;
    }
}