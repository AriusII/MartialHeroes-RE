namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct VitalStats(uint MaxHp, uint MaxMp, uint MaxStamina)
{
    public static readonly VitalStats Zero = new(0, 0, 0);

    public VitalStats(
        uint baseHp, uint baseMp, uint baseStamina,
        uint equipmentHpBonus, uint equipmentMpBonus, uint equipmentStaminaBonus)
        : this(
            SaturatingAdd(baseHp, equipmentHpBonus),
            SaturatingAdd(baseMp, equipmentMpBonus),
            SaturatingAdd(baseStamina, equipmentStaminaBonus))
    {
    }

    public static VitalStats FromFormula(in VitalFormulaInputs inputs, uint maxStamina = 0)
    {
        var (maxHp, maxMp) = VitalFormula.Compute(in inputs);
        return new VitalStats(ClampToCapacity(maxHp), ClampToCapacity(maxMp), maxStamina);
    }

    public static VitalStats FromResolved(
        uint baseHp, uint baseMp, uint baseStamina,
        uint equipmentHpBonus = 0, uint equipmentMpBonus = 0, uint equipmentStaminaBonus = 0)
    {
        return new VitalStats(
            SaturatingAdd(baseHp, equipmentHpBonus),
            SaturatingAdd(baseMp, equipmentMpBonus),
            SaturatingAdd(baseStamina, equipmentStaminaBonus));
    }

    private static uint ClampToCapacity(long value)
    {
        if (value <= 0) return 0u;

        return value >= uint.MaxValue ? uint.MaxValue : (uint)value;
    }

    private static uint SaturatingAdd(uint a, uint b)
    {
        var sum = unchecked(a + b);
        return sum < a ? uint.MaxValue : sum;
    }
}