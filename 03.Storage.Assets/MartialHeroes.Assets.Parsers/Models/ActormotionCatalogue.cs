namespace MartialHeroes.Assets.Parsers.Models;

/// <summary>
/// Queryable catalogue of all records from <c>data/char/actormotion.txt</c>.
/// Keyed by the computed <c>motion_key</c> field
/// (<c>= col1 + base_table[(uint8)(col0 + 1)]</c>).
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/actormotion.md §File structure —
/// "Records are inserted into an ordered map keyed by the computed motion_key field."
/// <para>
/// This catalogue exposes three lookup modes to support all documented consumer patterns:
/// <list type="bullet">
/// <item><description><see cref="GetByMotionKey"/> — canonical O(1) lookup by the computed key
///   (the key the client's own ordered map uses).</description></item>
/// <item><description><see cref="GetByIntraOffset"/> — looks up by raw col1 value (= mob_id
///   for mob/NPC entries) when no external base table is available. Correct when the base
///   table contribution is 0 (col0 category 0).</description></item>
/// <item><description><see cref="AllEntries"/> — enumerate all records.</description></item>
/// </list>
/// </para>
/// <para>ZERO rendering/engine dependencies.</para>
/// </remarks>
public sealed class ActormotionCatalogue
{
    private readonly Dictionary<uint, ActormotionEntry> _byMotionKey;
    private readonly Dictionary<int, ActormotionEntry> _byIntraOffset;

    internal ActormotionCatalogue(Dictionary<uint, ActormotionEntry> byMotionKey)
    {
        _byMotionKey = byMotionKey;

        // Build a secondary index keyed by col1 (intra-category offset = mob_id for mob records).
        // First-occurrence wins when two records share the same col1 (different categories).
        _byIntraOffset = new Dictionary<int, ActormotionEntry>(byMotionKey.Count);
        foreach (var entry in byMotionKey.Values)
            _byIntraOffset.TryAdd(entry.Col1RawOffset, entry);
    }

    /// <summary>Total number of parsed records.</summary>
    public int Count => _byMotionKey.Count;

    /// <summary>
    /// Looks up a record by its computed <c>motion_key</c>.
    /// spec: Docs/RE/formats/actormotion.md §Computed lookup key.
    /// </summary>
    /// <param name="motionKey">The computed key <c>= col1 + base_table[(uint8)(col0+1)]</c>.</param>
    /// <returns>The matching entry, or <see langword="null"/> when absent.</returns>
    public ActormotionEntry? GetByMotionKey(uint motionKey)
        => _byMotionKey.TryGetValue(motionKey, out var e) ? e : null;

    /// <summary>
    /// Looks up a record by its raw col1 value (intra-category offset).
    /// For mob/NPC entries this equals the <c>mob_id</c> from a spawn record.
    /// </summary>
    /// <remarks>
    /// This secondary index is useful when no external base table is available
    /// (base contribution = 0, so motion_key = col1). When multiple categories share
    /// the same col1, the first-parsed occurrence is returned.
    /// spec: Docs/RE/formats/actormotion.md — col1 = mob_id for mob/NPC entries.
    /// </remarks>
    /// <param name="col1Value">The raw col1 value (mob_id).</param>
    /// <returns>The first matching entry, or <see langword="null"/> when absent.</returns>
    public ActormotionEntry? GetByIntraOffset(int col1Value)
        => _byIntraOffset.TryGetValue(col1Value, out var e) ? e : null;

    /// <summary>All parsed entries (enumeration order is insertion order).</summary>
    public IEnumerable<ActormotionEntry> AllEntries => _byMotionKey.Values;
}