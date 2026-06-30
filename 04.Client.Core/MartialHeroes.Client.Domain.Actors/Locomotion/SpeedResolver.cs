namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public static class SpeedResolver
{
    public const int SlowFractionBits = 16;

    public const int SlowUnity = 1 << SlowFractionBits;

    public static long Resolve(
        long baseRateRawPerSecond,
        ReadOnlySpan<long> candidateRatesRawPerSecond,
        int slowFractionRaw)
    {
        var best = baseRateRawPerSecond;
        foreach (var candidate in candidateRatesRawPerSecond)
            if (candidate > best)
                best = candidate;

        if (best < 0) best = 0;
        if (slowFractionRaw <= 0) return best;

        var keep = SlowUnity - slowFractionRaw;
        if (keep <= 0) return 0;

        return (long)((Int128)best * keep / SlowUnity);
    }
}
