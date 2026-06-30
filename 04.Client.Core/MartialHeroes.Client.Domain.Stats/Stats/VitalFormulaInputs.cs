namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct VitalFormulaInputs
{
    public PrimaryStats Stats { get; init; }
    public byte ClassId { get; init; }
    public long EquipmentHpFlat { get; init; }
    public long EquipmentMpFlat { get; init; }
    public long SetBonusHp { get; init; }
    public long SetBonusMp { get; init; }
    public bool IsSetComplete { get; init; }
    public long LevelBaseHp { get; init; }
    public long LevelBaseMp { get; init; }
    public int Level { get; init; }
    public StatBaseCurve LevelBaseHpCurve { get; init; }
    public StatBaseCurve LevelBaseMpCurve { get; init; }
    public long ServerBaseHp { get; init; }
    public long ServerBaseMp { get; init; }
    public long ThirdBarFlat { get; init; }
    public int HpPercentBuffPercent { get; init; }
    public AuraTerm Aura0 { get; init; }
    public AuraTerm Aura1 { get; init; }

    public static VitalFormulaInputs Empty => new()
    {
        Stats = PrimaryStats.Zero,
        ClassId = 0,
        Aura0 = AuraTerm.None,
        Aura1 = AuraTerm.None,
        LevelBaseHpCurve = StatBaseCurve.Empty,
        LevelBaseMpCurve = StatBaseCurve.Empty
    };

    public long ResolveLevelBaseHp()
    {
        return LevelBaseHpCurve.BaseForLevel(Level) + LevelBaseHp;
    }

    public long ResolveLevelBaseMp()
    {
        return LevelBaseMpCurve.BaseForLevel(Level) + LevelBaseMp;
    }
}