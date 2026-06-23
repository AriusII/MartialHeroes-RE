namespace MartialHeroes.Client.Domain.Stats.Stats;

public readonly struct StatBaseCurve
{
    private readonly IReadOnlyList<long>? _baseByLevel;

    public StatBaseCurve(IReadOnlyList<long>? baseByLevel)
    {
        _baseByLevel = baseByLevel is { Count: > 0 } ? baseByLevel : null;
    }

    public static StatBaseCurve Empty => default;

    public bool IsEmpty => _baseByLevel is null;

    public int Count => _baseByLevel?.Count ?? 0;

    public long BaseForLevel(int level)
    {
        var table = _baseByLevel;
        if (table is null) return 0L;

        var index = level - 1;
        if (index < 0)
            index = 0;
        else if (index >= table.Count) index = table.Count - 1;

        return table[index];
    }
}