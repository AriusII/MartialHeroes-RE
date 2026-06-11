namespace MartialHeroes.Client.Domain.Stats;

/// <summary>
/// Resolved vital capacities for an actor: the final maximum HP / MP / stamina the rest of the
/// domain caps current values against. This is the single home of "Max" computation for an actor;
/// <see cref="MartialHeroes.Client.Domain.Actors.Actor"/>'s <c>MaxHp</c>/<c>MaxMp</c>/<c>MaxStamina</c>
/// are derived values fed by this type.
/// </summary>
/// <remarks>
/// <para>
/// <b>HP / MP — the recovered formula.</b> Maximum HP and MP are produced by the recovered
/// three-stage stat/equipment/aura formula (<see cref="VitalFormula"/>), not a placeholder
/// <c>base + bonus</c> aggregation. spec: Docs/RE/structs/stats.md. Use <see cref="FromFormula"/> to
/// build resolved capacities from a <see cref="VitalFormulaInputs"/>. The legacy client computes
/// max HP/MP on demand and does not store them as actor fields (spec: stats.md), so this struct is a
/// cheap, recomputable value, not authoritative wire state.
/// </para>
/// <para>
/// <b>Stamina — modeling choice (ours, not documented).</b> The recovered spec
/// (Docs/RE/structs/stats.md) covers HP and MP only; it publishes no stamina growth curve. Stamina
/// is therefore carried as an already-resolved maximum supplied by the caller, with no formula here.
/// When/if a stamina spec is recovered, replace the stamina resolution that feeds this value.
/// </para>
/// <para>
/// Results from the formula are clamped into the non-negative <see cref="uint"/> capacity range used
/// across the actor model. A formula result is non-negative in normal play (the sentinel class id 0
/// yields a 0 HP multiplier — spec: stats.md), but the clamp guards malformed inputs.
/// </para>
/// </remarks>
public readonly record struct VitalStats(uint MaxHp, uint MaxMp, uint MaxStamina)
{
    /// <summary>A zeroed capacity block (no max HP/MP/stamina).</summary>
    public static readonly VitalStats Zero = new(0, 0, 0);

    /// <summary>
    /// Compatibility constructor for callers that hold already-summed base-plus-bonus capacities
    /// (the pre-formula aggregation shape). Delegates to <see cref="FromResolved"/>; for the
    /// recovered HP/MP formula use <see cref="FromFormula"/> instead.
    /// </summary>
    public VitalStats(
        uint baseHp, uint baseMp, uint baseStamina,
        uint equipmentHpBonus, uint equipmentMpBonus, uint equipmentStaminaBonus)
        : this(
            SaturatingAdd(baseHp, equipmentHpBonus),
            SaturatingAdd(baseMp, equipmentMpBonus),
            SaturatingAdd(baseStamina, equipmentStaminaBonus))
    {
    }

    /// <summary>
    /// Builds resolved capacities by running the recovered max-HP / max-MP formula
    /// (<see cref="VitalFormula"/>) over <paramref name="inputs"/> and pairing the result with the
    /// caller-supplied resolved <paramref name="maxStamina"/> (no formula — see type remarks).
    /// spec: Docs/RE/structs/stats.md.
    /// </summary>
    /// <remarks>
    /// HP/MP results are <b>provisional</b> whenever the formula's external bases
    /// (<see cref="VitalFormulaInputs.LevelBaseHp"/> etc.) are left at their default <c>0</c>: the
    /// structure is correct but the absolute numbers are incomplete until catalog/server data exists
    /// (spec: stats.md "External inputs (UNVERIFIED)").
    /// </remarks>
    public static VitalStats FromFormula(in VitalFormulaInputs inputs, uint maxStamina = 0)
    {
        (long maxHp, long maxMp) = VitalFormula.Compute(in inputs);
        return new VitalStats(ClampToCapacity(maxHp), ClampToCapacity(maxMp), maxStamina);
    }

    /// <summary>
    /// Builds resolved capacities from already-summed base-plus-bonus numbers, saturating each at
    /// <see cref="uint.MaxValue"/>. This is a compatibility/aggregation entry point for callers that
    /// already hold resolved capacities (e.g. seeding from wire-reported current values); it is
    /// <b>not</b> the recovered HP/MP formula — use <see cref="FromFormula"/> for that.
    /// </summary>
    public static VitalStats FromResolved(
        uint baseHp, uint baseMp, uint baseStamina,
        uint equipmentHpBonus = 0, uint equipmentMpBonus = 0, uint equipmentStaminaBonus = 0)
    {
        return new VitalStats(
            SaturatingAdd(baseHp, equipmentHpBonus),
            SaturatingAdd(baseMp, equipmentMpBonus),
            SaturatingAdd(baseStamina, equipmentStaminaBonus));
    }

    /// <summary>Clamps a formula result (which may be negative for malformed inputs) into the capacity range.</summary>
    private static uint ClampToCapacity(long value)
    {
        if (value <= 0)
        {
            return 0u;
        }

        return value >= uint.MaxValue ? uint.MaxValue : (uint)value;
    }

    /// <summary>Adds two unsigned values, clamping at <see cref="uint.MaxValue"/> instead of wrapping.</summary>
    private static uint SaturatingAdd(uint a, uint b)
    {
        uint sum = unchecked(a + b);
        return sum < a ? uint.MaxValue : sum;
    }
}