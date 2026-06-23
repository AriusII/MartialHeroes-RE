using MartialHeroes.Shared.Kernel.Ids;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct SkillCastState
{
    public const int DefaultCastTimeMs = 550;

    public SkillCastPhase Phase { get; init; }

    public SkillId ActiveSkill { get; init; }

    public long CastEndMs { get; init; }

    public long CooldownEndMs { get; init; }

    public static SkillCastState Idle => default;

    public bool IsIdle => Phase == SkillCastPhase.Idle;

    public (SkillCastState Next, SkillCastResult Result) TryBeginCast(
        in SkillDefinition skill,
        in CasterState caster,
        CooldownTable cooldowns,
        ISkillTargetingQuery targeting,
        in Vector3Fixed aimPoint,
        long now,
        int castTimeMs = DefaultCastTimeMs)
    {
        if (Phase != SkillCastPhase.Idle) return (this, SkillCastResult.AlreadyCasting);

        if (castTimeMs < 0)
            throw new ArgumentOutOfRangeException(nameof(castTimeMs), "Cast time must be non-negative.");

        var result =
            SkillCastValidator.Validate(in skill, in caster, cooldowns, targeting, in aimPoint, now);
        if (result != SkillCastResult.Ok) return (this, result);

        var next = new SkillCastState
        {
            Phase = SkillCastPhase.Casting,
            ActiveSkill = skill.Id,
            CastEndMs = now + castTimeMs,
            CooldownEndMs = 0
        };
        return (next, SkillCastResult.Ok);
    }

    public SkillCastState ConfirmCast(in SkillDefinition skill, CooldownTable cooldowns, long now)
    {
        ArgumentNullException.ThrowIfNull(cooldowns);

        if (Phase != SkillCastPhase.Casting || ActiveSkill != skill.Id) return this;

        var cooldownEnd = now;

        if (!skill.IsCooldownExempt)
        {
            cooldowns.Arm(skill.Id, now);
            cooldownEnd = now + skill.CooldownMs;
        }

        if (cooldownEnd <= now) return Idle;

        return new SkillCastState
        {
            Phase = SkillCastPhase.Cooldown,
            ActiveSkill = skill.Id,
            CastEndMs = 0,
            CooldownEndMs = cooldownEnd
        };
    }

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

    public SkillCastState Cancel()
    {
        return Phase == SkillCastPhase.Casting ? Idle : this;
    }
}