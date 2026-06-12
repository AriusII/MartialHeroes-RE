using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// One entry of the 240-slot cooldown ("recast") table, keyed by hotbar slot index. Tracks the
/// skill occupying the slot, when its cooldown was armed, the full duration and the remaining time.
/// spec: Docs/RE/specs/skills.md §4 ("240 parallel slots", per-slot value table).
/// </summary>
/// <remarks>
/// This is a deterministic value type: all timing is in caller-supplied milliseconds (<c>now</c>,
/// <c>setTime</c>, <c>durationMs</c>) — no ambient clock. Immutable; arm/tick return a new slot.
/// spec: skills.md §4 (tick-all, arm, check-ready operations).
/// </remarks>
public readonly record struct CooldownSlot
{
    /// <summary>The skill id occupying the slot (mirrors the hotbar). <see cref="SkillId.None"/> when empty. spec: skills.md §4 (skill-id mirror).</summary>
    public SkillId Skill { get; init; }

    /// <summary>When the cooldown was last armed, in ms. spec: skills.md §4 (set timestamp).</summary>
    public long SetTimeMs { get; init; }

    /// <summary>Full cooldown length in ms (= cooldown_centiseconds × 100). spec: skills.md §4 (duration).</summary>
    public int DurationMs { get; init; }

    /// <summary>Time left in ms; 0 when ready. spec: skills.md §4 (remaining).</summary>
    public int RemainingMs { get; init; }

    /// <summary>Non-zero while cooling. spec: skills.md §4 (active / armed flag).</summary>
    public bool Armed { get; init; }

    /// <summary>An empty, ready slot.</summary>
    public static CooldownSlot Empty => default;

    /// <summary>True when the slot is ready to cast (not armed / no remaining time). spec: skills.md §4 (check-ready).</summary>
    public bool IsReady => !Armed || RemainingMs <= 0;

    /// <summary>
    /// Arms this slot's cooldown: stamps <paramref name="now"/> as the set time and copies the slot's
    /// <see cref="DurationMs"/> into the remaining time. spec: skills.md §4 ("Arm a cooldown").
    /// </summary>
    public CooldownSlot Arm(long now) => this with
    {
        SetTimeMs = now,
        RemainingMs = DurationMs,
        Armed = DurationMs > 0,
    };

    /// <summary>
    /// Ticks this slot to <paramref name="now"/>: <c>remaining = set_time + duration − now</c>, clamped
    /// to 0 on expiry (and the armed flag cleared). spec: skills.md §4 ("Tick-all (per frame)").
    /// </summary>
    public CooldownSlot Tick(long now)
    {
        if (!Armed)
        {
            return this;
        }

        long remaining = SetTimeMs + DurationMs - now;
        if (remaining <= 0)
        {
            // Expired: clear remaining to 0 and disarm. spec: skills.md §4 (underflow → clear).
            return this with { RemainingMs = 0, Armed = false };
        }

        return this with { RemainingMs = (int)remaining };
    }
}