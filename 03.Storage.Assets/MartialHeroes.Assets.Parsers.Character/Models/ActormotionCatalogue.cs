namespace MartialHeroes.Assets.Parsers.Character.Models;

/// <summary>
///     Queryable catalogue of all records from <c>data/char/actormotion.txt</c>.
///     Keyed by the computed <c>motion_key</c> field
///     (<c>= col1 + base_table[(uint8)(col0 + 1)]</c>).
/// </summary>
/// <remarks>
///     spec: Docs/RE/formats/actormotion.md §File structure —
///     "Records are inserted into an ordered map keyed by the computed motion_key field."
///     <para>
///         This catalogue exposes three lookup modes to support all documented consumer patterns:
///         <list type="bullet">
///             <item>
///                 <description>
///                     <see cref="GetByMotionKey" /> — canonical O(1) lookup by the computed key
///                     (the key the client's own ordered map uses).
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <see cref="GetByIntraOffset" /> — looks up by raw col1 value (= mob_id
///                     for mob/NPC entries) when no external base table is available. Correct when the base
///                     table contribution is 0 (col0 category 0).
///                 </description>
///             </item>
///             <item>
///                 <description><see cref="AllEntries" /> — enumerate all records.</description>
///             </item>
///         </list>
///     </para>
///     <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class ActormotionCatalogue
{
    private readonly Dictionary<int, ActormotionEntry> _byIntraOffset;
    private readonly Dictionary<uint, ActormotionEntry> _byMotionKey;
    private readonly Dictionary<int, ActormotionEntry> _bySkinClass;

    internal ActormotionCatalogue(Dictionary<uint, ActormotionEntry> byMotionKey)
    {
        _byMotionKey = byMotionKey;

        // Build a secondary index keyed by col1 (intra-category offset = mob_id for mob records).
        // First-occurrence wins when two records share the same col1 (different categories).
        _byIntraOffset = new Dictionary<int, ActormotionEntry>(byMotionKey.Count);
        foreach (var entry in byMotionKey.Values)
            _byIntraOffset.TryAdd(entry.Col1RawOffset, entry);

        // Build a secondary index keyed by col2 (= int_a @ 0x04 = skin_class = SkinClassId).
        // First-occurrence wins, mirroring the _byIntraOffset pattern, so the lookup returns the
        // SAME first-in-file-order row a linear col2-match scan would return.
        // spec: Docs/RE/formats/actormotion.md §Per-record layout — int_a @ 0x04, col2 = skin_class
        // spec: Docs/RE/specs/skinning.md §8(e)/§10 (idle = actormotion col16, keyed by id_b/skin_class)
        _bySkinClass = new Dictionary<int, ActormotionEntry>(byMotionKey.Count);
        foreach (var entry in byMotionKey.Values)
            _bySkinClass.TryAdd(entry.IntA, entry);
    }

    /// <summary>Total number of parsed records.</summary>
    public int Count => _byMotionKey.Count;

    /// <summary>All parsed entries (enumeration order is insertion order).</summary>
    public IEnumerable<ActormotionEntry> AllEntries => _byMotionKey.Values;

    /// <summary>
    ///     Looks up a record by its computed <c>motion_key</c>.
    ///     spec: Docs/RE/formats/actormotion.md §Computed lookup key.
    /// </summary>
    /// <param name="motionKey">The computed key <c>= col1 + base_table[(uint8)(col0+1)]</c>.</param>
    /// <returns>The matching entry, or <see langword="null" /> when absent.</returns>
    public ActormotionEntry? GetByMotionKey(uint motionKey)
    {
        return _byMotionKey.TryGetValue(motionKey, out var e) ? e : null;
    }

    /// <summary>
    ///     Looks up a record by its raw col1 value (intra-category offset).
    ///     For mob/NPC entries this equals the <c>mob_id</c> from a spawn record.
    /// </summary>
    /// <remarks>
    ///     This secondary index is useful when no external base table is available
    ///     (base contribution = 0, so motion_key = col1). When multiple categories share
    ///     the same col1, the first-parsed occurrence is returned.
    ///     spec: Docs/RE/formats/actormotion.md — col1 = mob_id for mob/NPC entries.
    /// </remarks>
    /// <param name="col1Value">The raw col1 value (mob_id).</param>
    /// <returns>The first matching entry, or <see langword="null" /> when absent.</returns>
    public ActormotionEntry? GetByIntraOffset(int col1Value)
    {
        return _byIntraOffset.TryGetValue(col1Value, out var e) ? e : null;
    }

    /// <summary>
    ///     Looks up a record by its <c>skin_class</c> value (col2 = <see cref="ActormotionEntry.IntA" />
    ///     = <see cref="ActormotionEntry.SkinClassId" />). For the player preview path this is the
    ///     appearance-slot <c>id_b</c> key. When multiple records share the same skin_class the
    ///     first-parsed (first-in-file-order) occurrence is returned — identical to a linear
    ///     "first row whose col2 == skinClass" scan.
    /// </summary>
    /// <remarks>
    ///     spec: Docs/RE/formats/actormotion.md §Per-record layout — int_a @ 0x04, col2 = skin_class.
    ///     spec: Docs/RE/specs/skinning.md §8(e)/§10 — idle = actormotion col16, keyed by id_b/skin_class.
    /// </remarks>
    /// <param name="skinClass">The skin_class / SkinClassId (col2) to match.</param>
    /// <returns>The first matching entry, or <see langword="null" /> when absent.</returns>
    /// <remarks>
    ///     LEGACY/COINCIDENTAL path. The authoritative player idle key is
    ///     <see cref="GetByMotionKey" />(model_class_id): with the recovered CategoryBase
    ///     <c>{ 0, 0, 10000, 1000 }</c> wired into the parser, the four player appearance keys
    ///     {1,11,16,26} ARE the motion_key for their rows (col0=0 → base 0 → motion_key = col1, and the
    ///     four player rows carry col1 ∈ {1,11,16,26}). This col2-keyed lookup happens to agree for the
    ///     four players only because col2 == SkinClassId there; prefer <see cref="GetByMotionKey" /> for
    ///     the appearance-key idle. Kept for back-compat and existing callers/tests.
    /// </remarks>
    public ActormotionEntry? GetBySkinClass(int skinClass)
    {
        return _bySkinClass.TryGetValue(skinClass, out var e) ? e : null;
    }
}