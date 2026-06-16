namespace MartialHeroes.Client.Domain.Progression;

/// <summary>
/// The separate rank / honor XP channel driven by <c>5/11 RankXpGain</c> — distinct accumulators from
/// character XP, performing no HP/MP/level math. Pure, deterministic, engine-free; every mutation
/// returns a new state. spec: Docs/RE/specs/progression.md §4 (the shared rank-XP accumulation routine,
/// the two i64-stride per-level tables, the cap of 25, the "leveltable error" on divisor 0) / §11.
/// </summary>
/// <remarks>
/// <para>
/// State is two pieces: a <see cref="RankAccumulator"/> and a <see cref="WithinRank"/> remainder.
/// </para>
/// <para>
/// <b>Mode 2 = direct add.</b> When the <c>5/11</c> mode byte is 2 the amount is added straight to the
/// rank accumulator with no level math. Otherwise the amount is run through the per-level rank-XP table
/// (§4): <c>rank_acc += (remainder + amount) / divisor[idx]</c> and <c>within = (remainder + amount) %
/// divisor[idx]</c>, where the index is the local-player <em>level cache</em>. The progression caps at 25.
/// </para>
/// <para>
/// <b>Tables are server/config DATA — injected, never invented.</b> The divisor ("level") table and the
/// cap table contents are server-authored and not present in the client binary as constants (spec §12 Q6).
/// The caller supplies them; the Domain only applies the documented arithmetic. A divisor of 0 for a level
/// is the documented "leveltable error" condition (spec §4) — surfaced here as a thrown
/// <see cref="LevelTableException"/> so the application layer can log it, mirroring the client diagnostic.
/// </para>
/// </remarks>
public readonly record struct RankXpModel
{
    /// <summary>The §4 rank-progression cap (the level-cache value at which the per-level table is bounded). spec: progression.md §4.</summary>
    public const int RankCap = 25;

    /// <summary>The rank / honor accumulator. spec: progression.md §4.</summary>
    public long RankAccumulator { get; init; }

    /// <summary>The within-rank remainder value. spec: progression.md §4.</summary>
    public long WithinRank { get; init; }

    /// <summary>
    /// Applies a <c>5/11</c> rank-XP gain. <paramref name="mode"/> <c>2</c> adds <paramref name="amount"/>
    /// directly to the rank accumulator (no level math); any other mode runs the per-level table routine
    /// (§4) using <paramref name="levelCache"/> as the table index. The per-level <paramref name="divisorTable"/>
    /// and <paramref name="capTable"/> are server/config DATA supplied by the caller.
    /// spec: Docs/RE/specs/progression.md §4 / §4.1.
    /// </summary>
    /// <param name="amount">The rank-XP amount (wire <c>u64</c>; modelled as <see cref="long"/>).</param>
    /// <param name="mode">The <c>5/11</c> mode byte (2 = direct add, no level math).</param>
    /// <param name="levelCache">The local-player level cache — the index into both tables and the value
    /// the cap special-case tests against 25.</param>
    /// <param name="divisorTable">Per-level XP-per-rank-step divisors (injected DATA). Indexed by level.</param>
    /// <param name="capTable">Per-level within-rank cap values (injected DATA). Indexed by level. May be
    /// <c>null</c>/empty when the caller does not bound the remainder.</param>
    /// <exception cref="LevelTableException">Thrown when the divisor for <paramref name="levelCache"/> is 0
    /// (the client's "leveltable error"). spec: progression.md §4.</exception>
    public RankXpModel ApplyRankGain(
        long amount,
        byte mode,
        int levelCache,
        IReadOnlyList<long>? divisorTable,
        IReadOnlyList<long>? capTable)
    {
        // spec: progression.md §4 — mode 2 is a direct add to the rank accumulator, no level math.
        if (mode == 2)
        {
            return this with { RankAccumulator = unchecked(RankAccumulator + amount) };
        }

        // spec: progression.md §4 — the index is the level cache, clamped at the cap of 25.
        int index = levelCache;
        if (index > RankCap)
        {
            index = RankCap;
        }

        long divisor = LookupOrZero(divisorTable, index);
        if (divisor == 0L)
        {
            // spec: progression.md §4 — divisor 0 fires the "leveltable error" diagnostic.
            throw new LevelTableException(index);
        }

        long total = unchecked(WithinRank + amount);
        long ranksGained = total / divisor;
        long remainder = total % divisor;

        long cap = LookupOrZero(capTable, index);
        if (cap > 0L && remainder > cap)
        {
            remainder = cap;
        }

        return this with
        {
            RankAccumulator = unchecked(RankAccumulator + ranksGained),
            WithinRank = remainder,
        };
    }

    /// <summary>
    /// Overwrites the rank-XP pair from an authoritative source (<c>5/32 LevelUp</c> rewrites the rank-XP
    /// pair for the local player). spec: Docs/RE/specs/progression.md §5.
    /// </summary>
    public RankXpModel Resync(long rankAccumulator, long withinRank) =>
        this with { RankAccumulator = rankAccumulator, WithinRank = withinRank };

    private static long LookupOrZero(IReadOnlyList<long>? table, int index)
    {
        if (table is null || index < 0 || index >= table.Count)
        {
            return 0L;
        }

        return table[index];
    }
}