namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct CombatStats
{
    public int Str { get; init; }
    public int Dex { get; init; }
    public int Vital { get; init; }
    public int Inte { get; init; }
    public int Agil { get; init; }
    public int MaxLife { get; init; }
    public int MaxEnergy { get; init; }
    public int AttackRating { get; init; }
    public int HitRating { get; init; }
    public static CombatStats Empty => default;
}