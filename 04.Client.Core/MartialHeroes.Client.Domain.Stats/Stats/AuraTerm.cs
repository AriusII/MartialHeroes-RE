namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct AuraTerm(bool IsActive, byte Kind, int PercentValue)
{
    public static readonly AuraTerm None = new(false, 0, 0);

    public static AuraTerm Hp(int percentValue)
    {
        return new AuraTerm(true, VitalFormula.HpAuraKind, percentValue);
    }

    public static AuraTerm Mp(int percentValue)
    {
        return new AuraTerm(true, VitalFormula.MpAuraKind, percentValue);
    }
}