using MartialHeroes.Assets.Parsers.Character.Models;

namespace MartialHeroes.Client.Infrastructure.Catalog;

public sealed class EmoticonCatalogue
{
    private readonly Dictionary<string, int> _aliasToEmoteId;
    private readonly EmoticonTable _emotes;

    public EmoticonCatalogue(EmoticonTable emotes, SameEmoticonTable aliases)
    {
        ArgumentNullException.ThrowIfNull(emotes);
        ArgumentNullException.ThrowIfNull(aliases);

        _emotes = emotes;
        _aliasToEmoteId = new Dictionary<string, int>(aliases.Count);
        foreach (var alias in aliases.Entries)
            _aliasToEmoteId.TryAdd(alias.Alias, alias.EmoteId);
    }

    public int Count => _emotes.Count;

    public int AliasCount => _aliasToEmoteId.Count;

    public IReadOnlyList<EmoticonEntry> Entries => _emotes.Entries;

    public EmoticonEntry? GetByEmoteId(int emoteId)
    {
        return _emotes.GetByEmoteId(emoteId);
    }

    public EmoticonEntry? GetByIndex(int index)
    {
        return (uint)index < (uint)_emotes.Entries.Count ? _emotes.Entries[index] : null;
    }

    public bool TryResolveAlias(string alias, out int emoteId)
    {
        return _aliasToEmoteId.TryGetValue(alias, out emoteId);
    }

    public static EmoticonCatalogue FromLoader(VfsCatalogueLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        return new EmoticonCatalogue(loader.LoadEmoticon(), loader.LoadSameEmoticon());
    }
}