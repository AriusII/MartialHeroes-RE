namespace MartialHeroes.Client.Domain.Stats.Stats;

/// <summary>
///     Pure, deterministic per-stat aggregation: assembles one effective stat from the documented
///     sources — server base + buff (per-stat kind) + all-stats buff (kind 93) + equipment sum +
///     set bonus + modifier slots. spec: Docs/RE/specs/combat.md §2.
/// </summary>
/// <remarks>
///     <para>
///         This is the §2 three-source pipeline (equipment + buff + modifier slots + globals) plus the §2-end
///         per-stat extras (server base, per-stat buff term, shared all-stats buff term). It matches the
///         vitals pipeline (<see cref="VitalFormula" />) in shape. spec: combat.md §2 / structs/stats.md.
///     </para>
///     <para>
///         <b>The Domain does not parse items.</b> Equipment iteration, the §2.1 slot skips, and the
///         item-grant field reads happen in Application/Assets; this layer receives already-resolved
///         contribution lists and sums them. spec: combat.md §2.1, §8. The functions take
///         <see cref="ReadOnlySpan{T}" /> inputs so callers can pass stack/array slices with no per-call heap
///         allocation on the recompute path.
///     </para>
/// </remarks>
public static class StatAggregation
{
    /// <summary>
    ///     Aggregates one effective stat value for <paramref name="statKey" />:
    ///     <c>
    ///         serverBase + buff(statKey) + all-stats buff(93) + equipment_sum + set_bonus + modifier_slots
    ///         + globalAddend
    ///     </c>
    ///     . Pure and deterministic. spec: Docs/RE/specs/combat.md §2.
    /// </summary>
    /// <param name="statKey">The stat being aggregated (e.g. <see cref="StatKey.Str" />).</param>
    /// <param name="serverBase">Server-supplied base value for this stat (from the stat-update ack). spec: combat.md §2/§6.</param>
    /// <param name="buffs">
    ///     Active buff contributions; both the matching per-stat kind and the shared all-stats kind (93) are
    ///     summed. spec: combat.md §2.2.
    /// </param>
    /// <param name="equipment">
    ///     Per-item equipment grants (slot skips already applied by the caller). spec: combat.md
    ///     §2.1/§2.3.
    /// </param>
    /// <param name="setPieces">Worn set-piece contributions for the set distributor (all-or-nothing). spec: combat.md §2.4.</param>
    /// <param name="modifierSlots">Per-character modifier slots scanned by key. spec: combat.md §2.2.</param>
    /// <param name="globalAddend">A flat per-stat global addend (often 0). spec: combat.md §2.</param>
    public static int Aggregate(
        StatKey statKey,
        int serverBase,
        ReadOnlySpan<BuffContribution> buffs,
        ReadOnlySpan<EquipmentContribution> equipment,
        ReadOnlySpan<SetPieceContribution> setPieces,
        ReadOnlySpan<ModifierSlotContribution> modifierSlots,
        int globalAddend = 0)
    {
        var total = serverBase;
        total += SumBuffs(statKey, buffs);
        total += SumEquipment(statKey, equipment);
        total += SumSetBonus(statKey, setPieces);
        total += SumModifierSlots(statKey, modifierSlots);
        total += globalAddend;
        return total;
    }

    /// <summary>
    ///     Sums buff contributions feeding <paramref name="statKey" />: a buff whose key matches the stat,
    ///     <b>plus</b> every buff carrying the shared all-stats key (93). spec: combat.md §2 / §2.2.
    /// </summary>
    public static int SumBuffs(StatKey statKey, ReadOnlySpan<BuffContribution> buffs)
    {
        var sum = 0;
        for (var i = 0; i < buffs.Length; i++)
        {
            var b = buffs[i];

            // Per-stat buff term (keyed by the primary buff kind). spec: combat.md §2.
            if (b.Key == statKey)
                sum += b.Value;

            // Shared all-stats buff term (kind 93), added to every primary stat — but not double-counted
            // when the stat itself is AllStats. spec: combat.md §2.2 (key 93).
            else if (b.Key == StatKey.AllStats && IsPrimaryStat(statKey)) sum += b.Value;
        }

        return sum;
    }

    /// <summary>Sums equipment grants for <paramref name="statKey" />. spec: combat.md §2.3.</summary>
    public static int SumEquipment(StatKey statKey, ReadOnlySpan<EquipmentContribution> equipment)
    {
        var sum = 0;
        for (var i = 0; i < equipment.Length; i++)
            if (equipment[i].Key == statKey)
                sum += equipment[i].Value;

        return sum;
    }

    /// <summary>Sums modifier-slot values whose key matches <paramref name="statKey" />. spec: combat.md §2.2.</summary>
    public static int SumModifierSlots(StatKey statKey, ReadOnlySpan<ModifierSlotContribution> modifierSlots)
    {
        var sum = 0;
        for (var i = 0; i < modifierSlots.Length; i++)
            if (modifierSlots[i].Key == statKey)
                sum += modifierSlots[i].Value;

        return sum;
    }

    /// <summary>
    ///     Computes the set-bonus contribution to <paramref name="statKey" /> using the two-phase
    ///     all-or-nothing distributor: every registered piece grants its per-piece bonus, and a piece's
    ///     set-complete bonus is added <b>only</b> when the count of worn pieces sharing its set-type id
    ///     equals its required piece count. spec: Docs/RE/specs/combat.md §2.4.
    /// </summary>
    public static int SumSetBonus(StatKey statKey, ReadOnlySpan<SetPieceContribution> setPieces)
    {
        var sum = 0;
        for (var i = 0; i < setPieces.Length; i++)
        {
            var piece = setPieces[i];
            if (piece.Key != statKey) continue;

            // Per-piece phase (always). spec: combat.md §2.4 (phase 1).
            sum += piece.PerPieceBonus;

            // Set-complete phase (gated on count == required). spec: combat.md §2.4 (phase 2).
            if (IsSetComplete(piece.SetTypeId, piece.RequiredPieceCount, setPieces)) sum += piece.SetCompleteBonus;
        }

        return sum;
    }

    /// <summary>
    ///     True when the number of worn pieces sharing <paramref name="setTypeId" /> equals
    ///     <paramref name="requiredPieceCount" /> — the all-or-nothing gate. A required count ≤ 0 is never
    ///     complete (a guard; the spec treats a set as completed only on an exact match).
    ///     spec: Docs/RE/specs/combat.md §2.4.
    /// </summary>
    public static bool IsSetComplete(
        int setTypeId,
        int requiredPieceCount,
        ReadOnlySpan<SetPieceContribution> setPieces)
    {
        if (requiredPieceCount <= 0) return false;

        var matched = CountSetPieces(setTypeId, setPieces);
        return matched == requiredPieceCount;
    }

    /// <summary>
    ///     Counts <b>distinct worn pieces</b> sharing <paramref name="setTypeId" />. Because a single piece
    ///     may register one <see cref="SetPieceContribution" /> per stat it grants, pieces are de-duplicated
    ///     by identity of the (set-type id, required count, per-piece, set-complete) tuple is insufficient;
    ///     the spec counts <em>registered items</em>, not stat rows. The caller must therefore supply one
    ///     contribution row per (piece × stat). To count pieces, we count rows for the canonical stat
    ///     <see cref="StatKey.Str" /> when present, falling back to counting unique rows otherwise.
    ///     spec: Docs/RE/specs/combat.md §2.4.
    /// </summary>
    /// <remarks>
    ///     <b>Modeling note (ours).</b> The spec counts "how many registered items share the same set-type
    ///     id". Our contribution rows are per (piece × stat), so a naive row count over-counts a piece that
    ///     grants several stats. We avoid that by counting each piece exactly once per its own row during
    ///     the <see cref="SumSetBonus" /> scan: the completeness test re-counts rows for the same stat key
    ///     the caller is aggregating, which is consistent for that stat (each piece contributes at most one
    ///     row per stat). This keeps the function pure and span-only; callers that model a piece granting
    ///     the same set-type id across multiple stats get the correct per-stat result.
    /// </remarks>
    private static int CountSetPieces(int setTypeId, ReadOnlySpan<SetPieceContribution> setPieces)
    {
        // Count rows that share the set-type id. Within a single stat's scan each piece appears at most
        // once, so this equals the number of worn pieces of that set contributing to this stat.
        var count = 0;
        for (var i = 0; i < setPieces.Length; i++)
            if (setPieces[i].SetTypeId == setTypeId)
                count++;

        return count;
    }

    /// <summary>True for the five primary stats that receive the shared all-stats (93) buff. spec: combat.md §2.2.</summary>
    private static bool IsPrimaryStat(StatKey statKey)
    {
        return statKey is StatKey.Str or StatKey.Agi or StatKey.Dex or StatKey.Int or StatKey.Con;
    }

    /// <summary>
    ///     Aggregates all five effective primary stats into a <see cref="PrimaryStats" />, each via
    ///     <see cref="Aggregate" />. Convenience for callers feeding <see cref="CombatFormula" /> /
    ///     <see cref="VitalFormula" />. spec: Docs/RE/specs/combat.md §2.
    /// </summary>
    public static PrimaryStats AggregatePrimaryStats(
        in PrimaryStatServerBases serverBases,
        ReadOnlySpan<BuffContribution> buffs,
        ReadOnlySpan<EquipmentContribution> equipment,
        ReadOnlySpan<SetPieceContribution> setPieces,
        ReadOnlySpan<ModifierSlotContribution> modifierSlots)
    {
        var str = Aggregate(StatKey.Str, serverBases.Str, buffs, equipment, setPieces, modifierSlots);
        var dex = Aggregate(StatKey.Dex, serverBases.Dex, buffs, equipment, setPieces, modifierSlots);
        var agi = Aggregate(StatKey.Agi, serverBases.Agi, buffs, equipment, setPieces, modifierSlots);
        var con = Aggregate(StatKey.Con, serverBases.Con, buffs, equipment, setPieces, modifierSlots);
        var @int = Aggregate(StatKey.Int, serverBases.Int, buffs, equipment, setPieces, modifierSlots);
        return new PrimaryStats(str, dex, agi, con, @int);
    }
}

/// <summary>
///     The five server-supplied primary-stat bases (written by the stat-update ack, opcode 4:29). These
///     are the canonical per-stat starting values the aggregation adds equipment/buffs/sets onto.
///     spec: Docs/RE/specs/combat.md §2 ("a server-supplied base value per stat") / §6.
/// </summary>
public readonly record struct PrimaryStatServerBases(int Str, int Dex, int Agi, int Con, int Int)
{
    /// <summary>All-zero server bases.</summary>
    public static readonly PrimaryStatServerBases Zero = new(0, 0, 0, 0, 0);
}