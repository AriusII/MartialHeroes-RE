namespace MartialHeroes.Client.Domain.Progression.Progression;

public readonly record struct RankXpModel
{
    public const int RankCap = 25;

    public long RankAccumulator { get; init; }

    public long WithinRank { get; init; }

    public RankXpModel ApplyRankGain(
        long amount,
        byte mode,
        int levelCache,
        IReadOnlyList<long>? divisorTable,
        IReadOnlyList<long>? capTable)
    {
        if (mode == 2) return this with { RankAccumulator = unchecked(RankAccumulator + amount) };

        var index = levelCache;
        if (index > RankCap) index = RankCap;

        var divisor = LookupOrZero(divisorTable, index);
        if (divisor == 0L)
            throw new LevelTableException(index);

        var total = unchecked(WithinRank + amount);
        var ranksGained = total / divisor;
        var remainder = total % divisor;

        var cap = LookupOrZero(capTable, index);
        if (cap > 0L && remainder > cap) remainder = cap;

        return this with
        {
            RankAccumulator = unchecked(RankAccumulator + ranksGained),
            WithinRank = remainder
        };
    }

    public RankXpModel Resync(long rankAccumulator, long withinRank)
    {
        return this with { RankAccumulator = rankAccumulator, WithinRank = withinRank };
    }

    private static long LookupOrZero(IReadOnlyList<long>? table, int index)
    {
        if (table is null || index < 0 || index >= table.Count) return 0L;

        return table[index];
    }
}