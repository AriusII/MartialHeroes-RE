using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// The deterministic cast state machine for a single caster: <c>idle → casting → cooldown → idle</c>,
/// with cast validation delegated to <see cref="SkillCastValidator"/> and an explicit, total
/// millisecond tick. Pure and engine-free: all time is caller-supplied, no ambient clock.
/// spec: Docs/RE/specs/skills.md §2 (cast pipeline), §5.2 (cast confirm), §4 (cooldown).
/// </summary>
/// <remarks>
/// <para>
/// This is an immutable value type; every transition returns a new state. The <see cref="Phase"/>
/// transitions are total — a given (phase, event) pair either advances deterministically or is
/// explicitly rejected (a begin attempt while not idle returns <see cref="SkillCastResult.AlreadyCasting"/>).
/// spec: skills.md §2.1 gate 3 (busy / already-casting).
/// </para>
/// <para>
/// <b>Cast-time window.</b> The §5.2 local cast confirm sets a UI cast time-out of about
/// <see cref="DefaultCastTimeMs"/> (≈ now + 550 ms). We model that window as the casting phase; its
/// exact value is injected per begin so a caller can supply a per-skill cast time without baking the
/// 550 ms in. spec: skills.md §5.2 ("Set a UI cast time-out (≈ now + 550 ms)").
/// </para>
/// </remarks>
public readonly record struct SkillCastState
{
    /// <summary>The ≈550 ms UI cast time-out from the §5.2 local cast confirm. spec: skills.md §5.2.</summary>
    public const int DefaultCastTimeMs = 550;

    /// <summary>Current cast lifecycle phase. spec: skills.md §2 / §4 / §5.2.</summary>
    public SkillCastPhase Phase { get; init; }

    /// <summary>The skill currently casting / cooling down (<see cref="SkillId.None"/> when idle).</summary>
    public SkillId ActiveSkill { get; init; }

    /// <summary>When the casting phase ends, in ms (the §5.2 cast time-out). 0 when idle. spec: skills.md §5.2.</summary>
    public long CastEndMs { get; init; }

    /// <summary>When the cooldown phase ends, in ms. 0 when not on cooldown. spec: skills.md §4.</summary>
    public long CooldownEndMs { get; init; }

    /// <summary>The idle starting state.</summary>
    public static SkillCastState Idle => default;

    /// <summary>True when a new cast may begin (idle phase). spec: skills.md §2.</summary>
    public bool IsIdle => Phase == SkillCastPhase.Idle;

    /// <summary>
    /// Attempts to begin a cast. Runs the full §2.1 gate chain via <see cref="SkillCastValidator"/>;
    /// on <see cref="SkillCastResult.Ok"/> transitions to <see cref="SkillCastPhase.Casting"/> with a
    /// cast window of <paramref name="castTimeMs"/>. Rejects a begin while not idle.
    /// spec: Docs/RE/specs/skills.md §2.1 / §5.2.
    /// </summary>
    /// <returns>
    /// The (next state, result). On a non-<see cref="SkillCastResult.Ok"/> result the state is unchanged.
    /// A begin while not idle returns <see cref="SkillCastResult.AlreadyCasting"/> (gate 3).
    /// </returns>
    public (SkillCastState Next, SkillCastResult Result) TryBeginCast(
        in SkillDefinition skill,
        in CasterState caster,
        CooldownTable cooldowns,
        ISkillTargetingQuery targeting,
        in Vector3Fixed aimPoint,
        long now,
        int castTimeMs = DefaultCastTimeMs)
    {
        // Total transition: only idle may begin a cast. spec: skills.md §2.1 gate 3 (already casting).
        if (Phase != SkillCastPhase.Idle)
        {
            return (this, SkillCastResult.AlreadyCasting);
        }

        if (castTimeMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(castTimeMs), "Cast time must be non-negative.");
        }

        SkillCastResult result =
            SkillCastValidator.Validate(in skill, in caster, cooldowns, targeting, in aimPoint, now);
        if (result != SkillCastResult.Ok)
        {
            return (this, result);
        }

        var next = new SkillCastState
        {
            Phase = SkillCastPhase.Casting,
            ActiveSkill = skill.Id,
            CastEndMs = now + castTimeMs,
            CooldownEndMs = 0,
        };
        return (next, SkillCastResult.Ok);
    }

    /// <summary>
    /// Completes the casting phase (the §5.2 cast confirm): consumes resources, arms the recast slot
    /// for the skill unless its category is exempt, and transitions to <see cref="SkillCastPhase.Cooldown"/>.
    /// No-op (returns unchanged) unless currently casting <paramref name="skill"/>.
    /// spec: Docs/RE/specs/skills.md §5.2 / §4.
    /// </summary>
    /// <returns>The next state after the cast confirms.</returns>
    public SkillCastState ConfirmCast(in SkillDefinition skill, CooldownTable cooldowns, long now)
    {
        ArgumentNullException.ThrowIfNull(cooldowns);

        // Total transition: confirm only applies to the skill we are casting. spec: skills.md §5.2.
        if (Phase != SkillCastPhase.Casting || ActiveSkill != skill.Id)
        {
            return this;
        }

        long cooldownEnd = now;

        // Arm the cooldown unless the category is exempt (1 basic-attack, 5). spec: skills.md §4 / §5.2.
        if (!skill.IsCooldownExempt)
        {
            cooldowns.Arm(skill.Id, now);
            cooldownEnd = now + skill.CooldownMs;
        }

        // No cooldown to wait on (exempt or zero-duration): go straight back to idle. spec: skills.md §4.
        if (cooldownEnd <= now)
        {
            return Idle;
        }

        return new SkillCastState
        {
            Phase = SkillCastPhase.Cooldown,
            ActiveSkill = skill.Id,
            CastEndMs = 0,
            CooldownEndMs = cooldownEnd,
        };
    }

    /// <summary>
    /// Advances the state machine to <paramref name="now"/>: a casting window whose end has elapsed
    /// is <b>not</b> auto-confirmed here (the server skill-action drives confirm, §5.2); a cooldown
    /// whose end has elapsed returns to <see cref="SkillCastPhase.Idle"/>. Deterministic and total.
    /// spec: Docs/RE/specs/skills.md §4 (tick) / §5.2.
    /// </summary>
    /// <returns>The advanced state.</returns>
    public SkillCastState Tick(long now)
    {
        switch (Phase)
        {
            case SkillCastPhase.Cooldown when now >= CooldownEndMs:
                return Idle;

            case SkillCastPhase.Idle:
            case SkillCastPhase.Casting:
            case SkillCastPhase.Cooldown:
            default:
                return this;
        }
    }

    /// <summary>
    /// Cancels an in-progress cast (a continuation / cancel from §5.1: a leading active flag of 0 resets
    /// the caster's motion). Returns to idle without arming a cooldown. Total: a no-op when idle.
    /// spec: Docs/RE/specs/skills.md §5.1 ("a leading active flag byte of 0 means cancel / idle").
    /// </summary>
    public SkillCastState Cancel() => Phase == SkillCastPhase.Casting ? Idle : this;
}
