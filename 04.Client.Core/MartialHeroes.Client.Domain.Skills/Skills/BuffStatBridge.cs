using MartialHeroes.Client.Domain.Stats.Stats;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public static class BuffStatBridge
{
    public static int BuildContributions(
        ReadOnlySpan<BuffStatGrant> source,
        Span<BuffContribution> destination)
    {
        var written = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var grant = source[i];
            if (!grant.Buff.IsActive) continue;

            if (written >= destination.Length)
                throw new ArgumentException(
                    "Destination span is too small for the active contributions.", nameof(destination));

            destination[written++] = new BuffContribution(grant.StatKey, grant.Value);
        }

        return written;
    }
}

public readonly record struct BuffStatGrant(BuffDebuff Buff, StatKey StatKey, int Value);