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
    // spec: Docs/RE/specs/skinning.md §3.5.1 / §3.5.4 — the body is overlay slot 3.
    private const int BodySlot = 3;

    // spec: Docs/RE/specs/frontend_scenes.md §3.7.5 — the four starter bodies live in the base
    // (category 0) appearance group with no part-id remainder (col3 == 0).
    private const int BaseCategory = 0;
    private const int NoRemainder = 0;

    // (category, slot, classKey, remainder) -> row, for the (slot=3, model_class_id) body lookup.
    private readonly Dictionary<(int Category, int Slot, int ClassKey, int Remainder), SkinTxtEntry> _byBodyKey;

    private readonly Dictionary<int, SkinTxtEntry> _byMeshGid;

    internal SkinTxtCatalog(IReadOnlyList<SkinTxtEntry> entries)
    {
        Entries = entries;
        _byMeshGid = new Dictionary<int, SkinTxtEntry>(entries.Count);
        _byBodyKey = new Dictionary<(int, int, int, int), SkinTxtEntry>(entries.Count);
        foreach (var entry in entries)
        {
            _byMeshGid.TryAdd(entry.MeshGid, entry);
            // col0 = category (Category), col1 = class/appearance key = the IdB for body rows
            // (HundredsGroup), col2 = overlay slot (MillionsGroup), col3 = reduced-gid remainder
            // (LowRemainder). spec: skinning.md §3.5.3 (catalogue row columns).
            _byBodyKey.TryAdd((entry.Category, entry.MillionsGroup, entry.HundredsGroup, entry.LowRemainder), entry);
        }
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

    /// <summary>
    ///     Resolves the per-class BODY mesh gid via the §3.5.3 appearance-catalogue body lookup:
    ///     the base-category (col0 == 0), body-slot (col2 == 3), no-remainder (col3 == 0) row whose
    ///     class/appearance key (col1) equals <paramref name="modelClassId" /> — the
    ///     <c>model_class_id = 5·(class + 4·variant) − 24 ∈ {1, 11, 16, 26}</c> (the IdB). Returns the
    ///     row's col4 mesh gid, or <c>null</c> when no such body row exists (caller LOGS + reports a
    ///     data gap; NEVER substitutes a wrong-class body).
    ///     <para>
    ///         This is the corrected body resolver:
    ///         <c>
    ///             {1→g202110001, 11→g202130001, 16→g202140001,
    ///             26→g202220001}
    ///         </c>
    ///         are four DISTINCT bodies, retiring the prior wrong-key path that read
    ///         the col2={4,6,11} class-1-family outfit rows (all col1==1 / Musa) and collapsed every
    ///         class onto a class-1 body.
    ///     </para>
    ///     spec: Docs/RE/specs/skinning.md §3.5.3 (catalogue key (slot, model_class_id)) / §3.5.1
    ///     (body == slot 3); Docs/RE/specs/frontend_scenes.md §3.7.5 (per-class IdB body table).
    /// </summary>
    /// <param name="modelClassId">The appearance/skeleton key (IdB) ∈ {1, 11, 16, 26}.</param>
    public int? GetBodyMeshGid(int modelClassId)
    {
        return _byBodyKey.TryGetValue((BaseCategory, BodySlot, modelClassId, NoRemainder), out var entry)
               && entry.MeshGid > 0
            ? entry.MeshGid
            : null;
    }
}