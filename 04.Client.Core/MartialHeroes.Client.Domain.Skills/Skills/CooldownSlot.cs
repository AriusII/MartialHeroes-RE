using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct CooldownSlot
{
    public SkillId Skill { get; init; }

    public long SetTimeMs { get; init; }

    public int DurationMs { get; init; }

    public int RemainingMs { get; init; }

    public bool Armed { get; init; }

    public static CooldownSlot Empty => default;

    public bool IsReady => !Armed || RemainingMs <= 0;

    public CooldownSlot Arm(long now)
    {
        return this with
        {
            SetTimeMs = now,
            RemainingMs = DurationMs,
            Armed = DurationMs > 0
        };
    }

    public CooldownSlot Tick(long now)
    {
        if (!Armed) return this;

        var remaining = SetTimeMs + DurationMs - now;
        if (remaining <= 0)
            return this with { RemainingMs = 0, Armed = false };

        return this with { RemainingMs = (int)remaining };
    }
}