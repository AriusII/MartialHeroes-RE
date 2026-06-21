namespace MartialHeroes.Client.Domain.Stats.Stats;

/// <summary>
///     Pure, deterministic implementation of the recovered <b>derived combat-stat</b> formulas: the
///     stat-weighted attack/secondary bases (§3.1/§3.2), the attack-rating and hit/accuracy-rating
///     composition (§3.3/§3.4), and the weapon-proficiency hit penalty table (§4).
///     spec: Docs/RE/specs/combat.md.
/// </summary>
/// <remarks>
///     <para>
///         <b>This is a display / server-parity model, not a damage roll.</b> The legacy client is
///         server-authoritative for HP deltas: it never rolls per-hit damage. What it computes locally is the
///         combat-stat mirror surfaced on the character sheet and held for tooltips/parity. We re-implement
///         <em>that</em> mirror. spec: Docs/RE/specs/combat.md "Status header" / §8 "Implementation guidance".
///     </para>
///     <para>
///         <b>Confidence and injected inputs.</b> The §3.1/§3.2 stat-weight coefficients are HIGH confidence
///         (bit-exact 32-bit float literals recovered from the client). The §4 proficiency banding is MEDIUM
///         (one boundary band ambiguous — see <see cref="WeaponProficiencyPenaltyPercent" />). Per the spec,
///         the per-item / per-slot magnitudes that feed §3.3/§3.4 (the modifier slots, the equipment sums,
///         the per-weapon <c>weapon_term</c>, the level/grade bytes) are <b>data</b>, not constants in this
///         spec — they are <see cref="AttackRatingInputs" /> / <see cref="HitRatingInputs" /> fields injected by
///         callers. The Domain does not parse items. spec: Docs/RE/specs/combat.md §2, §8.
///     </para>
///     <para>
///         <b>Float parity.</b> The §3.1/§3.2 weights are stored as <see cref="float" /> literals and widened
///         to <see cref="double" /> for accumulation, matching the vitals-formula parity guidance
///         (spec: combat.md §3.2). The two getters return <see cref="int" /> via floor (truncation toward zero
///         of the non-negative running total), matching the "returns int" note on §3.3/§3.4.
///     </para>
/// </remarks>
public static class CombatFormula
{
    // -------------------------------------------------------------------------
    // §3.1 Physical attack base — stat weights (bit-exact 32-bit float literals).
    // spec: Docs/RE/specs/combat.md §3.1.
    // -------------------------------------------------------------------------

    private const float AttackWeightStr = 2.5f; // spec: combat.md §3.1
    private const float AttackWeightDex = 2.0f; // spec: combat.md §3.1
    private const float AttackWeightAgi = 2.299999952316284f; // spec: combat.md §3.1 (2.3 literal)
    private const float AttackWeightCon = 1.0f; // spec: combat.md §3.1
    private const float AttackWeightInt = 1.0f; // spec: combat.md §3.1

    // -------------------------------------------------------------------------
    // §3.2 Secondary base — stat weights (bit-exact 32-bit float literals).
    // spec: Docs/RE/specs/combat.md §3.2.
    // -------------------------------------------------------------------------

    private const float SecondaryWeightStr = 1.399999976158142f; // spec: combat.md §3.2 (1.4 literal)
    private const float SecondaryWeightDex = 2.650000095367432f; // spec: combat.md §3.2 (2.65 literal)
    private const float SecondaryWeightAgi = 1.5f; // spec: combat.md §3.2
    private const float SecondaryWeightCon = 2.099999904632568f; // spec: combat.md §3.2 (2.1 literal)
    private const float SecondaryWeightInt = 1.100000023841858f; // spec: combat.md §3.2 (1.1 literal)

    /// <summary>Shared ×0.2 scale on both bases (bit-exact literal). spec: combat.md §3.1/§3.2.</summary>
    private const float BaseScale = 0.20000000298023224f; // spec: combat.md §3.1/§3.2 (×0.2 literal)

    // -------------------------------------------------------------------------
    // §3.3/§3.4 flat composition constants.
    // -------------------------------------------------------------------------

    /// <summary>Weapon-grade helper contributes <c>grade × 0.1</c>. spec: combat.md §3.3/§3.4.</summary>
    private const double WeaponGradeScale = 0.1; // spec: combat.md §3.3/§3.4 (weapon_grade * 0.1)

    /// <summary>Level/grade byte contributes <c>level × 0.5</c>. spec: combat.md §3.3/§3.4.</summary>
    private const double LevelTermScale = 0.5; // spec: combat.md §3.3/§3.4 (level_term * 0.5)

    /// <summary>Flat <c>+2.0</c> when the class/grade byte ≥ 8. spec: combat.md §3.3/§3.4.</summary>
    private const double GradeBonus = 2.0; // spec: combat.md §3.3/§3.4 (+2.0 if grade byte >= 8)

    /// <summary>Class/grade byte threshold for the <c>+2.0</c> bonus. spec: combat.md §3.3/§3.4.</summary>
    private const int GradeBonusThreshold = 8; // spec: combat.md §3.3/§3.4 (>= 8)

    /// <summary>Each large flat accuracy baseline term in the hit-rating formula. spec: combat.md §3.4.</summary>
    private const double HitAccuracyBaseline = 300.0; // spec: combat.md §3.4 (+300.0 flat terms)

    /// <summary>The slot-83 hit % multiplier is applied as <c>(value − 100) %</c>. spec: combat.md §2.2/§3.3.</summary>
    private const double HitPercentPivot = 100.0; // spec: combat.md §2.2 (key 83: (value - 100) %)

    // -------------------------------------------------------------------------
    // §3.1 / §3.2 — the two stat-weighted bases (returns float).
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Physical attack base: <c>(STR*2.5 + DEX*2.0 + AGI*2.3 + CON*1.0 + INT*1.0) * 0.2</c>.
    ///     spec: Docs/RE/specs/combat.md §3.1.
    /// </summary>
    /// <remarks>HIGH confidence — bit-exact 32-bit-literal weights (spec: combat.md §3.1).</remarks>
    public static double AttackBase(in PrimaryStats stats)
    {
        var weighted =
            stats.Str * (double)AttackWeightStr +
            stats.Dex * (double)AttackWeightDex +
            stats.Agi * (double)AttackWeightAgi +
            stats.Con * (double)AttackWeightCon +
            stats.Int * (double)AttackWeightInt;

        return weighted * BaseScale;
    }

    /// <summary>
    ///     Secondary base (magic/ki or accuracy school):
    ///     <c>(STR*1.4 + DEX*2.65 + AGI*1.5 + CON*2.1 + INT*1.1) * 0.2</c>.
    ///     spec: Docs/RE/specs/combat.md §3.2.
    /// </summary>
    /// <remarks>HIGH confidence — bit-exact 32-bit-literal weights (spec: combat.md §3.2).</remarks>
    public static double SecondaryBase(in PrimaryStats stats)
    {
        var weighted =
            stats.Str * (double)SecondaryWeightStr +
            stats.Dex * (double)SecondaryWeightDex +
            stats.Agi * (double)SecondaryWeightAgi +
            stats.Con * (double)SecondaryWeightCon +
            stats.Int * (double)SecondaryWeightInt;

        return weighted * BaseScale;
    }

    // -------------------------------------------------------------------------
    // §4 — weapon-proficiency hit penalty table.
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the weapon-proficiency hit penalty as a whole percent for a proficiency key, per the
    ///     recovered banding. Applied to hit rating as <c>hit *= (1 − penalty/100)</c> (§3.4 / §4).
    ///     spec: Docs/RE/specs/combat.md §4.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Banding (spec: combat.md §4): <c>4..10 → 25</c>, <c>11..30 → 50</c>, <c>≥75 → 100</c>.
    ///         Keys below 4 and the <c>31..74</c> band yield <b>0</b> here (the unpenalised path).
    ///     </para>
    ///     <para>
    ///         <b>MEDIUM confidence / UNVERIFIED (spec §4, §7 #10).</b> The <c>31..74</c> band has two
    ///         recovered exit paths (return 0 vs. return 75); we model the unpenalised path (0). A caller that
    ///         must reproduce the 75 variant should override via <see cref="WeaponProficiencyPenaltyPercent(int, int)" />.
    ///         It is also UNVERIFIED whether the keying byte is a weapon mastery level or a weapon-type id; we
    ///         take it as an opaque key. Do not bind penalties to specific weapon classes without a capture.
    ///     </para>
    /// </remarks>
    public static int WeaponProficiencyPenaltyPercent(int proficiencyKey)
    {
        return WeaponProficiencyPenaltyPercent(proficiencyKey, 0);
    }

    /// <summary>
    ///     As <see cref="WeaponProficiencyPenaltyPercent(int)" />, but lets the caller select the penalty
    ///     for the UNVERIFIED <c>31..74</c> band (spec: combat.md §4, §7 #10 — recovered exits are 0 or
    ///     75). Default callers should use the parameterless overload (0).
    /// </summary>
    /// <param name="proficiencyKey">Opaque weapon-proficiency / weapon-type key.</param>
    /// <param name="midBandPenalty">Penalty (%) for the ambiguous <c>31..74</c> band (0 or 75).</param>
    public static int WeaponProficiencyPenaltyPercent(int proficiencyKey, int midBandPenalty)
    {
        // spec: Docs/RE/specs/combat.md §4 (penalty table).
        if (proficiencyKey >= 75) return 100; // spec: combat.md §4 (75 and above → 100)

        if (proficiencyKey >= 31) return midBandPenalty; // spec: combat.md §4 (31..74 → 0 or 75; UNVERIFIED #10)

        if (proficiencyKey >= 11) return 50; // spec: combat.md §4 (11..30 → 50)

        if (proficiencyKey >= 4) return 25; // spec: combat.md §4 (4..10 → 25)

        return 0; // below the banded range: no penalty.
    }

    /// <summary>
    ///     Applies a percentage penalty to a running hit total: <c>value × (1 − penalty/100)</c>.
    ///     spec: Docs/RE/specs/combat.md §4.
    /// </summary>
    public static double ApplyHitPenalty(double value, int penaltyPercent)
    {
        return value * (1.0 - penaltyPercent / 100.0);
    }

    // -------------------------------------------------------------------------
    // §3.3 — attack-rating getter (returns int).
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Composes the full attack rating from the §3.1 base plus injected slot / equipment / weapon /
    ///     level / grade terms, applying the slot-83 percentage multiplier, then flooring to int.
    ///     spec: Docs/RE/specs/combat.md §3.3.
    /// </summary>
    /// <remarks>
    ///     Order (spec: combat.md §3.3):
    ///     <c>
    ///         slot15 + slot94(if≠0) + slot5 + weapon_term + attack_base + grade*0.1 + damage_equip_sum
    ///         + level*0.5 + (+2.0 if grade≥8)
    ///     </c>
    ///     , then apply <c>(slot83 − 100) %</c> on the running total,
    ///     then <c>+ slot61</c> flat. The result is floored (truncation toward zero) to int.
    /// </remarks>
    public static int AttackRating(in AttackRatingInputs inputs)
    {
        // slot[15] + slot[94] + slot[5]. The §3.3 "94 if nonzero" note is a no-op for an additive
        // integer slot (a zero slot contributes 0), so slot94 is added unconditionally. spec: combat.md §3.3 / §2.2.
        double total =
            inputs.Slot15 +
            inputs.Slot94 +
            inputs.Slot5;

        var stats = inputs.Stats;
        total += inputs.WeaponTerm; // per-weapon integer lookup; spec §3.3
        total += AttackBase(in stats); // spec §3.1
        total += inputs.WeaponGrade * WeaponGradeScale; // weapon_grade * 0.1; spec §3.3
        total += inputs.DamageEquipSum; // two-field attack accumulator; spec §2.3/§3.3
        total += inputs.LevelTerm * LevelTermScale; // level_term * 0.5; spec §3.3

        if (inputs.GradeByte >= GradeBonusThreshold) total += GradeBonus; // +2.0 if grade byte >= 8; spec §3.3

        // Apply (slot[83] − 100) % as a multiplier on the running total. spec: combat.md §3.3 / §2.2.
        total = ApplyHitPercentMultiplier(total, inputs.Slot83);

        total += inputs.Slot61; // final flat add (when present); spec §3.3

        return FloorToInt(total);
    }

    // -------------------------------------------------------------------------
    // §3.4 — hit / accuracy-rating getter (returns int).
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Composes the full hit / accuracy rating from the §3.2 secondary base plus injected accuracy
    ///     slots / equipment / weapon / level / grade terms, the two flat <c>+300.0</c> baselines, the
    ///     optional rank-progress <c>+300.0</c>, the <c>+2.0</c> grade bonus, and the weapon-proficiency
    ///     percentage penalty (§4), then floors to int. spec: Docs/RE/specs/combat.md §3.4.
    /// </summary>
    /// <remarks>
    ///     Order (spec: combat.md §3.4):
    ///     <c>
    ///         slot16 + slot20 + weapon_term + secondary_base + grade*0.1 + accuracy_equip_sum + level*0.5
    ///         + 300.0 + (300.0 if rank-progress gate set) + (2.0 if grade≥8)
    ///     </c>
    ///     , then apply the proficiency
    ///     penalty <c>×(1 − pen/100)</c> on the running total, then a second <c>+300.0</c>. Floored to int.
    /// </remarks>
    public static int HitRating(in HitRatingInputs inputs)
    {
        // slot[16] + slot[20]. spec: combat.md §3.4 / §2.2.
        double total = inputs.Slot16 + inputs.Slot20;

        var stats = inputs.Stats;
        total += inputs.WeaponTerm; // per-weapon integer lookup; spec §3.4
        total += SecondaryBase(in stats); // spec §3.2
        total += inputs.WeaponGrade * WeaponGradeScale; // weapon_grade * 0.1; spec §3.4
        total += inputs.AccuracyEquipSum; // defence-family equip accumulator; spec §2.3/§3.4
        total += inputs.LevelTerm * LevelTermScale; // level_term * 0.5; spec §3.4
        total += HitAccuracyBaseline; // +300.0 flat baseline; spec §3.4

        if (inputs.RankProgressGate) total += HitAccuracyBaseline; // +300.0 if rank-progress gate set; spec §3.4

        if (inputs.GradeByte >= GradeBonusThreshold) total += GradeBonus; // +2.0 if grade byte >= 8; spec §3.4

        // − proficiency_penalty % on the running total. spec: combat.md §3.4 / §4.
        total = ApplyHitPenalty(total, inputs.ProficiencyPenaltyPercent);

        total += HitAccuracyBaseline; // second +300.0 flat baseline; spec §3.4

        return FloorToInt(total);
    }

    /// <summary>
    ///     Applies the slot-83 hit % multiplier as a <c>(value − 100) %</c> adjustment to a running total:
    ///     <c>total × (slot83 / 100)</c>, treating slot83 = 0 (absent) as "no change" (×1.0).
    ///     spec: Docs/RE/specs/combat.md §2.2 (key 83) / §3.3.
    /// </summary>
    public static double ApplyHitPercentMultiplier(double total, int slot83Value)
    {
        if (slot83Value == 0) return total; // slot absent: (value − 100) % adjustment is undefined → no-op.

        // (value − 100) % adjustment: total * (1 + (slot83 − 100)/100) = total * (slot83 / 100).
        return total * (slot83Value / HitPercentPivot);
    }

    /// <summary>
    ///     Floors a non-negative running combat total to an <see cref="int" /> (truncation toward zero),
    ///     matching the "returns int" getters in §3.3/§3.4. Negative totals (malformed inputs) floor to 0.
    ///     spec: Docs/RE/specs/combat.md §3.3/§3.4.
    /// </summary>
    private static int FloorToInt(double value)
    {
        if (value <= 0.0) return 0;

        var floored = Math.Floor(value);
        return floored >= int.MaxValue ? int.MaxValue : (int)floored;
    }
}