namespace MartialHeroes.Assets.Parsers.Texture.Models;

public sealed class BgTextureCatalog
{
    private readonly Dictionary<int, string> _byIndex;

    public BgTextureCatalog(Dictionary<int, string> byIndex)
    {
        _byIndex = byIndex;
    }

    public int Count => _byIndex.Count;

    public string? GetRelPath(int poolIndex)
    {
        return _byIndex.TryGetValue(poolIndex, out var rel) ? rel : null;
    }
}