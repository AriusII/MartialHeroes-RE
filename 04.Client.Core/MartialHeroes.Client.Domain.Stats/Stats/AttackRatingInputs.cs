namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct AttackRatingInputs
{
    public PrimaryStats Stats { get; init; }

    public int Slot15 { get; init; }

    public int Slot94 { get; init; }

    public int Slot5 { get; init; }

    public int WeaponTerm { get; init; }

    public int WeaponGrade { get; init; }

    public int DamageEquipSum { get; init; }

    public int LevelTerm { get; init; }

    public int GradeByte { get; init; }

    public int Slot83 { get; init; }

    public int Slot61 { get; init; }

    public static AttackRatingInputs Empty => new() { Stats = PrimaryStats.Zero };
}