namespace MartialHeroes.Client.Domain.Stats.Stats;

public struct SkillDamageAccumulator
{
    private long _sum;
    private int _recordCount;

    public readonly long RawSum => _sum;

    public readonly int RecordCount => _recordCount;

    public readonly long DisplayTotal => unchecked(-_sum);

    public readonly bool HasDisplayDamage => _recordCount > 0 && DisplayTotal != 0L;

    public void Add(long hitMagnitude)
    {
        _sum = unchecked(_sum + hitMagnitude);
        _recordCount++;
    }

    public void Add(uint low, uint high)
    {
        Add(unchecked((long)(((ulong)high << 32) | low)));
    }

    public readonly long ApplyTo(long currentHp)
    {
        var next = unchecked(currentHp + _sum);
        return next < 0L ? 0L : next;
    }
}
