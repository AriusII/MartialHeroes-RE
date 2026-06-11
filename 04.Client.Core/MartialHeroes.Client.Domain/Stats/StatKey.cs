namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// The recovered per-character "stat key" small integers used by the modifier-slot scan and by the
/// per-stat aggregation accessors. A modifier slot is matched by one of these keys; when present its
/// value is added to the running total for the matching stat.
/// spec: Docs/RE/specs/combat.md §2.2 ("Per-character modifier slots (stat key → value)").
/// </summary>
/// <remarks>
/// <para>
/// The underlying type is <see cref="int"/> because the spec describes these as "stable small
/// integers" read from a fixed-size per-character modifier table. Only the keys touched by the
/// analysed accessors are known; the spec flags the enumeration as <b>partial</b> (UNVERIFIED #6),
/// so unknown keys are valid and simply do not match a named member here.
/// </para>
/// <para>
/// Every value below cites <c>Docs/RE/specs/combat.md §2.2</c>. We intentionally do <b>not</b> invent
/// any key that the spec does not list.
/// </para>
/// </remarks>
public enum StatKey
{
    /// <summary>STR source. spec: Docs/RE/specs/combat.md §2.2 (key 70).</summary>
    Str = 70,

    /// <summary>AGI source. spec: Docs/RE/specs/combat.md §2.2 (key 71).</summary>
    Agi = 71,

    /// <summary>DEX source. spec: Docs/RE/specs/combat.md §2.2 (key 72).</summary>
    Dex = 72,

    /// <summary>INT source. spec: Docs/RE/specs/combat.md §2.2 (key 73).</summary>
    Int = 73,

    /// <summary>CON / vitality source. spec: Docs/RE/specs/combat.md §2.2 (key 74).</summary>
    Con = 74,

    /// <summary>
    /// Shared all-stats add — added by every primary-stat accessor.
    /// spec: Docs/RE/specs/combat.md §2.2 (key 93).
    /// </summary>
    AllStats = 93,

    /// <summary>Max-HP flat add. spec: Docs/RE/specs/combat.md §2.2 (key 7).</summary>
    MaxHpFlatA = 7,

    /// <summary>Max-HP flat add. spec: Docs/RE/specs/combat.md §2.2 (key 2).</summary>
    MaxHpFlatB = 2,

    /// <summary>
    /// %HP buff — value / 100 added to the HP percentage multiplier.
    /// spec: Docs/RE/specs/combat.md §2.2 (key 81).
    /// </summary>
    HpPercent = 81,

    /// <summary>Hit-rating term. spec: Docs/RE/specs/combat.md §2.2 (key 15).</summary>
    HitTermA = 15,

    /// <summary>Hit-rating term. spec: Docs/RE/specs/combat.md §2.2 (key 94).</summary>
    HitTermB = 94,

    /// <summary>Hit-rating term. spec: Docs/RE/specs/combat.md §2.2 (key 5).</summary>
    HitTermC = 5,

    /// <summary>
    /// Hit % multiplier — applied as a <c>(value − 100) %</c> adjustment to the running hit total.
    /// spec: Docs/RE/specs/combat.md §2.2 (key 83).
    /// </summary>
    HitPercentMultiplier = 83,

    /// <summary>
    /// Hit-rating final flat add (when present).
    /// spec: Docs/RE/specs/combat.md §2.2 (key 61).
    /// </summary>
    HitFlatFinal = 61,

    /// <summary>Accuracy-rating term. spec: Docs/RE/specs/combat.md §2.2 (key 16).</summary>
    AccuracyTermA = 16,

    /// <summary>Accuracy-rating term. spec: Docs/RE/specs/combat.md §2.2 (key 20).</summary>
    AccuracyTermB = 20,
}