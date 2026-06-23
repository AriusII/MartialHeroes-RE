namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly record struct AuraTerm(bool IsActive, byte Kind, int PercentValue)
{
    public static readonly AuraTerm None = new(false, 0, 0);
}