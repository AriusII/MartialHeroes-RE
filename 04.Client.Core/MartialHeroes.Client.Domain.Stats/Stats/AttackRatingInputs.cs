namespace MartialHeroes.Client.Domain.Stats.Stats;

/// <summary>
///     Explicit input set for the attack-rating getter (<see cref="CombatFormula.AttackRating" />).
///     spec: Docs/RE/specs/combat.md §3.3.
/// </summary>
/// <remarks>
///     <para>
///         The <b>shape</b> (which terms compose, and in what order) is CONFIRMED by the spec; the numeric
///         <b>magnitudes</b> of the modifier slots, the equipment accumulator, the per-weapon
///         <c>weapon_term</c>, and the level/grade bytes are <b>data</b> the Domain does not own. The Domain
///         does not parse items; Application/Assets resolve these and inject them here. spec: combat.md §2, §8.
///     </para>
///     <para>
///         The slot names mirror the §2.2 stat keys: <see cref="Slot15" />/<see cref="Slot94" />/<see cref="Slot5" />
///         are the hit/attack modifier slots (keys 15, 94, 5); <see cref="Slot83" /> is the hit % multiplier
///         (key 83, applied as <c>(value − 100) %</c>); <see cref="Slot61" /> is the final flat add (key 61).
///         spec: combat.md §2.2 / §3.3.
///     </para>
/// </remarks>
public readonly record struct AttackRatingInputs
{
    /// <summary>The five effective primary stats feeding the attack base (§3.1). spec: combat.md §3.3.</summary>
    public PrimaryStats Stats { get; init; }

    /// <summary>Modifier slot key 15 (hit/attack term). spec: combat.md §2.2/§3.3.</summary>
    public int Slot15 { get; init; }

    /// <summary>Modifier slot key 94 (hit/attack term; added only when non-zero). spec: combat.md §2.2/§3.3.</summary>
    public int Slot94 { get; init; }

    /// <summary>Modifier slot key 5 (hit/attack term). spec: combat.md §2.2/§3.3.</summary>
    public int Slot5 { get; init; }

    /// <summary>
    ///     Per-equipped-weapon integer lookup added inside the formula. UNVERIFIED source table
    ///     (spec: combat.md §7 #9); injected. spec: combat.md §3.3.
    /// </summary>
    public int WeaponTerm { get; init; }

    /// <summary>
    ///     Weapon-grade helper value; contributes <c>WeaponGrade × 0.1</c>. The spec example notes the
    ///     helper returns 1.0 → +0.1 (spec: combat.md §3.3). Injected.
    /// </summary>
    public int WeaponGrade { get; init; }

    /// <summary>
    ///     The two-field attack accumulator (base-attack + weapon-attack), summed over worn gear by the
    ///     caller. spec: combat.md §2.3/§3.3 (damage_equip_sum). Injected.
    /// </summary>
    public int DamageEquipSum { get; init; }

    /// <summary>Level/grade byte; contributes <c>LevelTerm × 0.5</c>. spec: combat.md §3.3. Injected.</summary>
    public int LevelTerm { get; init; }

    /// <summary>
    ///     Class/grade byte; a flat <c>+2.0</c> is added when this is ≥ 8. spec: combat.md §3.3. Injected.
    /// </summary>
    public int GradeByte { get; init; }

    /// <summary>
    ///     Modifier slot key 83 (hit % multiplier); applied as a <c>(value − 100) %</c> adjustment to the
    ///     running total. 0 means absent (no adjustment). spec: combat.md §2.2/§3.3. Injected.
    /// </summary>
    public int Slot83 { get; init; }

    /// <summary>
    ///     Modifier slot key 61 (final flat add, when present). spec: combat.md §2.2/§3.3. Injected.
    /// </summary>
    public int Slot61 { get; init; }

    /// <summary>All-zero inputs (no slots/equip/weapon/level terms; zero stats).</summary>
    public static AttackRatingInputs Empty => new() { Stats = PrimaryStats.Zero };
}