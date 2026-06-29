namespace MartialHeroes.Assets.Parsers.Character.Models;

public sealed record SameEmoticonEntry(
    int EmoteId,
    string Alias);

public sealed class SameEmoticonTable
{
    private readonly Dictionary<string, int> _byAlias;

    internal SameEmoticonTable(IReadOnlyList<SameEmoticonEntry> entries)
    {
        Entries = entries;
        _byAlias = new Dictionary<string, int>(entries.Count);
        foreach (var entry in entries)
            _byAlias.TryAdd(entry.Alias, entry.EmoteId);
    }

    public int Count => Entries.Count;

    public IReadOnlyList<SameEmoticonEntry> Entries { get; }

    public bool TryResolveAlias(string alias, out int emoteId)
    {
        return _byAlias.TryGetValue(alias, out emoteId);
    }
}