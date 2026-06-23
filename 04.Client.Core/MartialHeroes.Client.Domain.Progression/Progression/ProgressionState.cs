namespace MartialHeroes.Client.Domain.Progression.Progression;

public readonly record struct ProgressionState
{
    public ExperienceModel Experience { get; init; }

    public RankXpModel RankXp { get; init; }

    public ProgressionState AddExperience(long amount)
    {
        return this with { Experience = Experience.AddExperience(amount) };
    }

    public ProgressionState AddRankXp(
        long amount,
        byte mode,
        int levelCache,
        IReadOnlyList<long>? divisorTable,
        IReadOnlyList<long>? capTable)
    {
        return this with { RankXp = RankXp.ApplyRankGain(amount, mode, levelCache, divisorTable, capTable) };
    }
}