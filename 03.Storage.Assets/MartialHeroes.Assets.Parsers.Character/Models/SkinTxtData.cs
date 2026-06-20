namespace MartialHeroes.Assets.Parsers.Character.Models;

/// <summary>
///     One six-integer row from <c>data/char/skin.txt</c>.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/text_tables.md §skin.txt — count-prefixed, 6 integer tokens per record.
///     spec: Docs/RE/specs/skinning.md §3.5.3 — col4 is mesh gid and col5 is texture id.
/// </remarks>
public sealed record SkinTxtEntry(
    int Category,
    int HundredsGroup,
    int MillionsGroup,
    int LowRemainder,
    int MeshGid,
    int TextureId);

/// <summary>
///     Decoded <c>data/char/skin.txt</c> appearance catalogue rows.
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/texture.md §The skin chain — <c>.skn</c> <c>IdA</c> joins to
///     <c>skin.txt</c> column 4, yielding column 5 <c>tex_id</c>.
/// </remarks>
public sealed class SkinTxtCatalog
{
    private readonly Dictionary<int, SkinTxtEntry> _byMeshGid;

    internal SkinTxtCatalog(IReadOnlyList<SkinTxtEntry> entries)
    {
        Entries = entries;
        _byMeshGid = new Dictionary<int, SkinTxtEntry>(entries.Count);
        foreach (var entry in entries)
            _byMeshGid.TryAdd(entry.MeshGid, entry);
    }

    /// <summary>All parsed rows in file order.</summary>
    public IReadOnlyList<SkinTxtEntry> Entries { get; }

    /// <summary>Total parsed row count.</summary>
    public int Count => Entries.Count;

    /// <summary>
    ///     Returns the first row whose column-4 mesh gid equals <paramref name="meshGid" />.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/texture.md §The skin chain — <c>.skn</c> <c>IdA</c> →
    ///     <c>skin.txt</c> col4 → col5 <c>tex_id</c>.
    /// </remarks>
    public SkinTxtEntry? GetByMeshGid(int meshGid)
    {
        return _byMeshGid.TryGetValue(meshGid, out var entry) ? entry : null;
    }
}