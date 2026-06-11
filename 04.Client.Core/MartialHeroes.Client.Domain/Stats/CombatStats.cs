namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// The local player's single <b>derived combat-stat aggregate</b>: the client's mirror of the
/// server's combat math, surfaced on the character sheet and held for tooltips / server-parity. It is
/// fully cleared and re-accumulated whenever inputs change (per the ~1 s recompute / input-change
/// events). spec: Docs/RE/specs/combat.md §1.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a damage resolution.</b> The client never rolls damage; it carries these fields for
/// display and server parity. The field names mirror the developers' own recovered debug labels.
/// spec: combat.md "Status header" / §1.
/// </para>
/// <para>
/// <b>Distinct fields kept distinct (spec, forward-compat).</b> <see cref="CriticalRate"/> vs.
/// <see cref="CriticalHit"/> (which is "chance" vs. "severity" is UNVERIFIED #2) and the PvE/PvP rate
/// pairs (<see cref="HuntDamageRate0"/>/<see cref="HuntDamageRate1"/> vs.
/// <see cref="PvpDamageRate0"/>/<see cref="PvpDamageRate1"/>; index roles UNVERIFIED #3) are modelled
/// separately and never collapsed. <see cref="OrderSpecial0"/>..<see cref="OrderSpecial3"/> are four
/// element/school buckets (index→element UNVERIFIED #4). spec: combat.md §1.
/// </para>
/// <para>
/// The <see cref="AttackRating"/> / <see cref="HitRating"/> integer ratings (§3.3/§3.4) are stored
/// alongside the §1 fields for the character-sheet view; they are computed by
/// <see cref="CombatFormula"/>.
/// </para>
/// </remarks>
public readonly record struct CombatStats
{
    // --- §1 integer-accumulated fields ---

    /// <summary>Aggregated STR contribution. spec: combat.md §1.</summary>
    public int Str { get; init; }

    /// <summary>Aggregated DEX contribution. spec: combat.md §1.</summary>
    public int Dex { get; init; }

    /// <summary>Aggregated CON / vitality contribution. spec: combat.md §1 (Vital).</summary>
    public int Vital { get; init; }

    /// <summary>Aggregated INT contribution. spec: combat.md §1 (Inte).</summary>
    public int Inte { get; init; }

    /// <summary>Aggregated AGI contribution. spec: combat.md §1 (Agil).</summary>
    public int Agil { get; init; }

    /// <summary>Max-stamina contribution (16-bit in the source aggregate). spec: combat.md §1.</summary>
    public short MaxStamina { get; init; }

    /// <summary>Critical <b>damage</b> bonus (flat extra damage on a crit). spec: combat.md §1 (CriticalValue).</summary>
    public int CriticalValue { get; init; }

    /// <summary>Minimum weapon/attack damage. spec: combat.md §1 (MinDamage).</summary>
    public int MinDamage { get; init; }

    /// <summary>Maximum weapon/attack damage. spec: combat.md §1 (MaxDamage).</summary>
    public int MaxDamage { get; init; }

    /// <summary>Flat damage / attack-power add. spec: combat.md §1 (Damage).</summary>
    public int Damage { get; init; }

    /// <summary>Flat defence (armour) value. spec: combat.md §1 (Defence).</summary>
    public int Defence { get; init; }

    /// <summary>PvE damage-rate term [0] (vs. monsters). Index role UNVERIFIED #3. spec: combat.md §1.</summary>
    public int HuntDamageRate0 { get; init; }

    /// <summary>PvE damage-rate term [1] (vs. monsters). Index role UNVERIFIED #3. spec: combat.md §1.</summary>
    public int HuntDamageRate1 { get; init; }

    /// <summary>PvP damage-rate term [0] (vs. players). Index role UNVERIFIED #3. spec: combat.md §1.</summary>
    public int PvpDamageRate0 { get; init; }

    /// <summary>PvP damage-rate term [1] (vs. players). Index role UNVERIFIED #3. spec: combat.md §1.</summary>
    public int PvpDamageRate1 { get; init; }

    /// <summary>Max-HP flat contribution. spec: combat.md §1 (MaxLife) / structs/stats.md.</summary>
    public int MaxLife { get; init; }

    /// <summary>Max-MP flat contribution. spec: combat.md §1 (MaxEnergy).</summary>
    public int MaxEnergy { get; init; }

    // --- §1 float-accumulated fields ---

    /// <summary>Max-HP percentage multiplier add. spec: combat.md §1 (MaxLifeRate).</summary>
    public float MaxLifeRate { get; init; }

    /// <summary>Max-MP percentage multiplier add. spec: combat.md §1 (MaxEnergyRate).</summary>
    public float MaxEnergyRate { get; init; }

    /// <summary>Critical <b>chance</b> rate (probability term). UNVERIFIED #2 vs. CriticalHit. spec: combat.md §1.</summary>
    public float CriticalRate { get; init; }

    /// <summary>Hit / accuracy rate. spec: combat.md §1 (HitRate).</summary>
    public float HitRate { get; init; }

    /// <summary>Defence rate (mitigation %). spec: combat.md §1 (DefenceRate).</summary>
    public float DefenceRate { get; init; }

    /// <summary>Second, distinct critical float. UNVERIFIED #2 vs. CriticalRate. spec: combat.md §1.</summary>
    public float CriticalHit { get; init; }

    /// <summary>Element/school special-rate bucket 0 (each buff adds value/100). Index→element UNVERIFIED #4. spec: combat.md §1.</summary>
    public float OrderSpecial0 { get; init; }

    /// <summary>Element/school special-rate bucket 1. Index→element UNVERIFIED #4. spec: combat.md §1.</summary>
    public float OrderSpecial1 { get; init; }

    /// <summary>Element/school special-rate bucket 2. Index→element UNVERIFIED #4. spec: combat.md §1.</summary>
    public float OrderSpecial2 { get; init; }

    /// <summary>Element/school special-rate bucket 3. Index→element UNVERIFIED #4. spec: combat.md §1.</summary>
    public float OrderSpecial3 { get; init; }

    /// <summary>Attack range. spec: combat.md §1 (Range).</summary>
    public float Range { get; init; }

    // --- §3.3/§3.4 character-sheet ratings (derived via CombatFormula) ---

    /// <summary>Composed attack rating (§3.3). spec: combat.md §3.3.</summary>
    public int AttackRating { get; init; }

    /// <summary>Composed hit / accuracy rating (§3.4). spec: combat.md §3.4.</summary>
    public int HitRating { get; init; }

    /// <summary>A fully-zeroed combat-stat aggregate (cleared state before re-accumulation). spec: combat.md §1/§2.</summary>
    public static CombatStats Empty => default;
}