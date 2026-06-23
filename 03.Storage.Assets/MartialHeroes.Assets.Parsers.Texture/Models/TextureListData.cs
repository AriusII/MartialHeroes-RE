namespace MartialHeroes.Assets.Parsers.Texture.Models;


public sealed record TextureListEntry(int TexId, string VfsPath);

public sealed class TextureListManifest
{
    private readonly Dictionary<int, TextureListEntry> _byTexId;

    internal TextureListManifest(IReadOnlyList<TextureListEntry> entries)
    {
        Entries = entries;
        _byTexId = new Dictionary<int, TextureListEntry>(entries.Count);
        foreach (var e in entries)
            _byTexId[e.TexId] = e;
    }

    public IReadOnlyList<TextureListEntry> Entries { get; }

    public int Count => Entries.Count;

    public TextureListEntry? GetById(int texId)
    {
        return _byTexId.TryGetValue(texId, out var e) ? e : null;
    }
}