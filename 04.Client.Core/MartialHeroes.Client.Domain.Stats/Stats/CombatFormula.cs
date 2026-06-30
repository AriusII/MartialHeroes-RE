namespace MartialHeroes.Client.Domain.Stats.Stats;

public static class CombatFormula
{
    private const float AttackWeightStr = 2.5f;
    private const float AttackWeightDex = 2.0f;
    private const float AttackWeightAgi = 2.299999952316284f;
    private const float AttackWeightCon = 1.0f;
    private const float AttackWeightInt = 1.0f;
    private const float SecondaryWeightStr = 1.399999976158142f;
    private const float SecondaryWeightDex = 2.650000095367432f;
    private const float SecondaryWeightAgi = 1.5f;
    private const float SecondaryWeightCon = 2.099999904632568f;
    private const float SecondaryWeightInt = 1.100000023841858f;
    private const float BaseScale = 0.20000000298023224f;
    private const double WeaponGradeScale = 0.1;
    private const double LevelTermScale = 0.5;
    private const double GradeBonus = 2.0;
    private const int GradeBonusThreshold = 8;
    private const double HitAccuracyBaseline = 300.0;
    private const double HitPercentPivot = 100.0;

    public static double AttackBase(in PrimaryStats stats)
    {
        var weighted =
            stats.Str * (double)AttackWeightStr +
            stats.Dex * (double)AttackWeightDex +
            stats.Agi * (double)AttackWeightAgi +
            stats.Con * (double)AttackWeightCon +
            stats.Int * (double)AttackWeightInt;

        return weighted * BaseScale;
    }

    public static double SecondaryBase(in PrimaryStats stats)
    {
        var weighted =
            stats.Str * (double)SecondaryWeightStr +
            stats.Dex * (double)SecondaryWeightDex +
            stats.Agi * (double)SecondaryWeightAgi +
            stats.Con * (double)SecondaryWeightCon +
            stats.Int * (double)SecondaryWeightInt;

        return weighted * BaseScale;
    }


    public static int WeaponProficiencyPenaltyPercent(int proficiencyKey)
    {
        if (proficiencyKey >= 76) return 100;

        if (proficiencyKey >= 31) return 75;

        if (proficiencyKey >= 11) return 50;

        if (proficiencyKey >= 4) return 25;

        return 0;
    }

    public static double ApplyHitPenalty(double value, int penaltyPercent)
    {
        return value * (1.0 - penaltyPercent / 100.0);
    }

    public static int AttackRating(in AttackRatingInputs inputs)
    {
        double total =
            (double)inputs.Slot15 + inputs.Slot15 +
            inputs.Slot94 +
            inputs.Slot5 + inputs.Slot5 +
            inputs.Slot223;

        var stats = inputs.Stats;
        total += inputs.WeaponTerm;
        total += AttackBase(in stats);
        total += inputs.WeaponGrade * WeaponGradeScale;
        total += inputs.DamageEquipSum;
        total += inputs.LevelTerm * LevelTermScale;

        if (inputs.GradeByte >= GradeBonusThreshold) total += GradeBonus;

        total = ApplyHitPercentMultiplier(total, inputs.Slot83);

        total += inputs.Slot61;

        return FloorToInt(total);
    }

    public static int HitRating(in HitRatingInputs inputs)
    {
        double total =
            (double)inputs.Slot16 + inputs.Slot16 + inputs.Slot20 + inputs.Slot16 +
            inputs.Slot223;

        var stats = inputs.Stats;
        total += inputs.WeaponTerm;
        total += SecondaryBase(in stats);
        total += inputs.WeaponGrade * WeaponGradeScale;
        total += inputs.AccuracyEquipSum;
        total += inputs.LevelTerm * LevelTermScale;

        if (inputs.RankProgressGate) total += HitAccuracyBaseline;

        if (inputs.GradeByte >= GradeBonusThreshold) total += GradeBonus;

        total = ApplyHitPenalty(total, inputs.ProficiencyPenaltyPercent);

        total += HitAccuracyBaseline;

        return FloorToInt(total);
    }

    public static double ApplyHitPercentMultiplier(double total, int slot83Value)
    {
        if (slot83Value == 0) return total;

        return total * (slot83Value / HitPercentPivot);
    }

    private static int FloorToInt(double value)
    {
        if (value <= 0.0) return 0;

        var floored = Math.Floor(value);
        return floored >= int.MaxValue ? int.MaxValue : (int)floored;
    }
}