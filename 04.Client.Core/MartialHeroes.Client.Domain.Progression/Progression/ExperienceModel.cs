namespace MartialHeroes.Client.Domain.Progression.Progression;

public readonly record struct ExperienceModel
{
    public long CurrentXp { get; init; }

    public long LifetimeXp { get; init; }

    public ExperienceModel AddExperience(long amount)
    {
        return this with { CurrentXp = unchecked(CurrentXp + amount), LifetimeXp = unchecked(LifetimeXp + amount) };
    }

    public static (long ShownBase, long Bonus) SplitBonus(long amount, long ratePercent)
    {
        var denominator = ratePercent + 100L;
        if (denominator <= 0L) return (amount, 0L);

        var shownBase = checked(100L * amount) / denominator;
        return (shownBase, amount - shownBase);
    }
}