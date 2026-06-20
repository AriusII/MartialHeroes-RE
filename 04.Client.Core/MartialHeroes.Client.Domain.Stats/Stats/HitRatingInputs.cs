namespace MartialHeroes.Client.Domain.Stats.Stats;

/// <summary>
///     Explicit input set for the hit / accuracy-rating getter (<see cref="CombatFormula.HitRating" />).
///     spec: Docs/RE/specs/combat.md §3.4.
/// </summary>
/// <remarks>
///     <para>
///         The <b>shape</b> is CONFIRMED; the numeric magnitudes (accuracy modifier slots, the equipment
///         accumulator, the per-weapon <c>weapon_term</c>, the level/grade bytes, the proficiency penalty)
///         are <b>data</b> resolved and injected by Application/Assets. The Domain does not parse items.
///         spec: combat.md §2, §8.
///     </para>
///     <para>
///         <b>UNVERIFIED (spec §3.4, §7).</b> The individual role of each large <c>+300.0</c> term is open;
///         we follow the spec's order verbatim (two flat baselines bracketing the rest plus the optional
///         rank-progress baseline). The <c>weapon_term</c> source table (§7 #9) and the proficiency keying
///         byte (§7 #10) are open; both reach this struct as injected values.
///     </para>
/// </remarks>
public readonly record struct HitRatingInputs
{
    /// <summary>The five effective primary stats feeding the secondary base (§3.2). spec: combat.md §3.4.</summary>
    public PrimaryStats Stats { get; init; }

    /// <summary>Accuracy modifier slot key 16. spec: combat.md §2.2/§3.4.</summary>
    public int Slot16 { get; init; }

    /// <summary>Accuracy modifier slot key 20. spec: combat.md §2.2/§3.4.</summary>
    public int Slot20 { get; init; }

    /// <summary>
    ///     Per-equipped-weapon integer lookup. UNVERIFIED source table (spec: combat.md §7 #9); injected.
    ///     spec: combat.md §3.4.
    /// </summary>
    public int WeaponTerm { get; init; }

    /// <summary>Weapon-grade helper value; contributes <c>WeaponGrade × 0.1</c>. spec: combat.md §3.4. Injected.</summary>
    public int WeaponGrade { get; init; }

    /// <summary>
    ///     The defence/accuracy-family equipment accumulator, summed over worn gear by the caller.
    ///     spec: combat.md §2.3/§3.4 (accuracy_equip_sum). Injected.
    /// </summary>
    public int AccuracyEquipSum { get; init; }

    /// <summary>Level/grade byte; contributes <c>LevelTerm × 0.5</c>. spec: combat.md §3.4. Injected.</summary>
    public int LevelTerm { get; init; }

    /// <summary>
    ///     When set, adds a second flat <c>+300.0</c> accuracy baseline (the rank-progress gate).
    ///     spec: combat.md §3.4. Injected.
    /// </summary>
    public bool RankProgressGate { get; init; }

    /// <summary>
    ///     Class/grade byte; a flat <c>+2.0</c> is added when this is ≥ 8. spec: combat.md §3.4. Injected.
    /// </summary>
    public int GradeByte { get; init; }

    /// <summary>
    ///     Weapon-proficiency penalty as a whole percent, applied as <c>×(1 − pen/100)</c> on the running
    ///     total. Resolve via <see cref="CombatFormula.WeaponProficiencyPenaltyPercent(int)" /> from a
    ///     proficiency key, or inject directly. spec: combat.md §3.4/§4. Injected.
    /// </summary>
    public int ProficiencyPenaltyPercent { get; init; }

    /// <summary>All-zero inputs (no slots/equip/weapon/level terms; zero stats; no penalty).</summary>
    public static HitRatingInputs Empty => new() { Stats = PrimaryStats.Zero };
}