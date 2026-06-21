namespace MartialHeroes.Assets.Parsers.Texture.Models;

/// <summary>
///     Decoded background-texture pool from <c>data/map{area}/texture/bgtexture.txt</c>.
///     Maps a 0-based pool index (the value referenced by a <c>.map</c> <c>TEXTURES{}</c>
///     <c>intTexId</c>) to a texture path relative to the area texture directory, WITHOUT the
///     <c>.dds</c> extension — e.g. index <c>116</c> → <c>"terrain/g3"</c> →
///     <c>data/map000/texture/terrain/g3.dds</c>.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/terrain.md §4.2 — bgtexture.txt text companion. CONFIRMED (observed).
///     ZERO rendering/engine dependencies.
/// </remarks>
public sealed class BgTextureCatalog
{
    private readonly Dictionary<int, string> _byIndex;

    public BgTextureCatalog(Dictionary<int, string> byIndex)
    {
        _byIndex = byIndex;
    }

    /// <summary>Number of entries in the pool.</summary>
    public int Count => _byIndex.Count;

    /// <summary>
    ///     Texture path relative to <c>data/map{area}/texture/</c> (no extension), or
    ///     <see langword="null" /> when the pool index is absent. The caller prepends the area
    ///     texture directory and appends <c>.dds</c>.
    ///     spec: Docs/RE/formats/terrain.md §4.2. CONFIRMED.
    /// </summary>
    public string? GetRelPath(int poolIndex)
    {
        return _byIndex.TryGetValue(poolIndex, out var rel) ? rel : null;
    }
}