namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct HitRatingInputs
{
    public PrimaryStats Stats { get; init; }
    public int Slot16 { get; init; }
    public int Slot20 { get; init; }
    public int WeaponTerm { get; init; }
    public int WeaponGrade { get; init; }
    public int AccuracyEquipSum { get; init; }
    public int LevelTerm { get; init; }
    public bool RankProgressGate { get; init; }
    public int GradeByte { get; init; }
    public int ProficiencyPenaltyPercent { get; init; }
}