namespace MartialHeroes.Client.Domain.Stats.Stats;

public static class VitalFormula
{
    private const float HpWeightStr = 2.2f;
    private const float HpWeightDex = 2.5f;
    private const float HpWeightAgi = 2.4f;
    private const float HpWeightCon = 1.5f;
    private const float HpWeightInt = 1.6f;

    private const float MpWeightStr = 1.4f;
    private const float MpWeightDex = 1.5f;
    private const float MpWeightAgi = 1.7f;
    private const float MpWeightCon = 1.5f;
    private const float MpWeightInt = 3.5f;

    private const double ScoreConstant = 30.0;

    public const byte HpAuraKind = 1;

    public const byte MpAuraKind = 2;

    public static (long MaxHp, long MaxMp) Compute(in VitalFormulaInputs inputs)
    {
        return (ComputeMaxHp(in inputs), ComputeMaxMp(in inputs));
    }

    public static long ComputeMaxHp(in VitalFormulaInputs inputs)
    {
        var s = inputs.Stats;

        var score =
            s.Str * (double)HpWeightStr +
            s.Dex * (double)HpWeightDex +
            s.Agi * (double)HpWeightAgi +
            s.Con * (double)HpWeightCon +
            s.Int * (double)HpWeightInt +
            ScoreConstant;

        var baseHp =
            (long)Math.Floor(score) +
            inputs.EquipmentHpFlat +
            (inputs.IsSetComplete ? inputs.SetBonusHp : 0) +
            inputs.ResolveLevelBaseHp() +
            inputs.ServerBaseHp;

        var pctMult = ClassHpTable.MultiplierFor(inputs.ClassId);
        pctMult += inputs.HpPercentBuffPercent / 100.0;
        pctMult += SumAuraPercent(inputs.Aura0, inputs.Aura1, HpAuraKind);

        return (long)Math.Floor(baseHp * pctMult);
    }

    public static long ComputeMaxMp(in VitalFormulaInputs inputs)
    {
        var s = inputs.Stats;

        var score =
            s.Str * (double)MpWeightStr +
            s.Dex * (double)MpWeightDex +
            s.Agi * (double)MpWeightAgi +
            s.Con * (double)MpWeightCon +
            s.Int * (double)MpWeightInt +
            ScoreConstant;

        var baseMp =
            (long)Math.Floor(score) +
            inputs.EquipmentMpFlat +
            (inputs.IsSetComplete ? inputs.SetBonusMp : 0) +
            inputs.ResolveLevelBaseMp() +
            inputs.ServerBaseMp;

        var pctMult = 1.0 + SumAuraPercent(inputs.Aura0, inputs.Aura1, MpAuraKind);

        return (long)Math.Floor(baseMp * pctMult);
    }

    private static double SumAuraPercent(in AuraTerm a0, in AuraTerm a1, byte kind)
    {
        var sum = 0.0;
        if (a0.IsActive && a0.Kind == kind) sum += a0.PercentValue / 100.0;

        if (a1.IsActive && a1.Kind == kind) sum += a1.PercentValue / 100.0;

        return sum;
    }
}