namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct VitalStats(uint MaxHp, uint MaxMp, uint MaxStamina)
{
    public static readonly VitalStats Zero = new(0, 0, 0);

    public static VitalStats FromFormula(in VitalFormulaInputs inputs, uint maxStamina = 0)
    {
        var (maxHp, maxMp) = VitalFormula.Compute(in inputs);
        return new VitalStats(ClampToCapacity(maxHp), ClampToCapacity(maxMp), maxStamina);
    }

    private static uint ClampToCapacity(long value)
    {
        if (value <= 0) return 0u;

        return value >= uint.MaxValue ? uint.MaxValue : (uint)value;
    }
}