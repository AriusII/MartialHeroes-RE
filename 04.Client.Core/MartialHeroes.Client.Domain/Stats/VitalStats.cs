namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// Resolved vital capacities for an actor: the inputs from which maximum HP / MP / stamina are
/// computed on demand.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> The legacy client does <b>not</b> store max HP/MP as actor fields;
/// they are computed from base stats plus equipment bonuses, and the vitals path caps current HP
/// against that computed maximum. spec: Docs/RE/structs/actor.md ("max_hp / max_mp are NOT stored
/// as fields. They are computed on demand from base stats plus equipment bonuses").
/// </para>
/// <para>
/// <b>Modeling choice (ours, not documented from the original game).</b> The actor spec does not
/// publish the base-stat-to-max-vital growth curve or class coefficients (the equipment / stat
/// block at descriptor +0xD4 is explicitly unmapped — spec: Docs/RE/structs/actor.md "Unverified /
/// open questions"). Rather than invent coefficients, this type holds the already-resolved
/// <see cref="BaseHp"/>/<see cref="BaseMp"/>/<see cref="BaseStamina"/> together with flat equipment
/// bonuses, and defines maximum as <c>base + bonus</c>. When the real growth formula is documented,
/// replace the resolution that produces these base values; the <c>Max*</c> definitions here are a
/// deliberate, transparent placeholder for the aggregation step, not an original-game formula.
/// </para>
/// </remarks>
public readonly record struct VitalStats(
    uint BaseHp,
    uint BaseMp,
    uint BaseStamina,
    uint EquipmentHpBonus,
    uint EquipmentMpBonus,
    uint EquipmentStaminaBonus)
{
    /// <summary>A zeroed stats block (no capacity).</summary>
    public static readonly VitalStats Zero = new(0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Computed maximum hit points: resolved base HP plus flat equipment HP bonus.
    /// Modeling choice (see type remarks); not an original-game coefficient.
    /// </summary>
    public uint MaxHp => SaturatingAdd(BaseHp, EquipmentHpBonus);

    /// <summary>
    /// Computed maximum mana / ki points: resolved base MP plus flat equipment MP bonus.
    /// Modeling choice (see type remarks); not an original-game coefficient.
    /// </summary>
    public uint MaxMp => SaturatingAdd(BaseMp, EquipmentMpBonus);

    /// <summary>
    /// Computed maximum stamina: resolved base stamina plus flat equipment stamina bonus.
    /// Modeling choice (see type remarks); not an original-game coefficient.
    /// </summary>
    public uint MaxStamina => SaturatingAdd(BaseStamina, EquipmentStaminaBonus);

    /// <summary>Adds two unsigned values, clamping at <see cref="uint.MaxValue"/> instead of wrapping.</summary>
    private static uint SaturatingAdd(uint a, uint b)
    {
        uint sum = unchecked(a + b);
        return sum < a ? uint.MaxValue : sum;
    }
}
