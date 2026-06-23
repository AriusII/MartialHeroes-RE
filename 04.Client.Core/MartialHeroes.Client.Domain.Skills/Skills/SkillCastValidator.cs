using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public static class SkillCastValidator
{
    public static SkillCastResult Validate(
        in SkillDefinition skill,
        in CasterState caster,
        CooldownTable cooldowns,
        ISkillTargetingQuery targeting,
        in Vector3Fixed aimPoint,
        long now)
    {
        ArgumentNullException.ThrowIfNull(cooldowns);
        ArgumentNullException.ThrowIfNull(targeting);

        if (!caster.PartyRelationAllied) return SkillCastResult.PartyRelation;

        if (!caster.BillingRankOk) return SkillCastResult.BillingOrRank;

        if (caster.IsBusyCasting) return SkillCastResult.AlreadyCasting;

        if (caster.IsMounted) return SkillCastResult.MountedOrUnresolved;

        if (caster.MapModeForbidsCasting) return SkillCastResult.MapModeForbidden;

        if (caster.IsStunnedOrSilenced) return SkillCastResult.StunnedOrSilenced;

        if (!caster.IsAlive) return SkillCastResult.NotAlive;

        if (caster.IsActionLocked) return SkillCastResult.ActionLocked;

        if (caster.TargetInBlockingHostileState) return SkillCastResult.TargetHostileState;

        if (skill.WeaponReqActive && !caster.WeaponRequirementSatisfied) return SkillCastResult.WeaponRequirement;

        if (!caster.SelfCastEligible) return SkillCastResult.SelfCastIneligible;

        if (!skill.IsCastGateCooldownExempt && !cooldowns.CheckReady(skill.Id, now)) return SkillCastResult.OnCooldown;

        var mpRequired = (long)SkillDefinition.MpGateMultiplier * skill.MpCostFactor;
        if (caster.AvailableMp < mpRequired) return SkillCastResult.NotEnoughMp;

        var rangeResult = CheckRangeAndLineOfSight(in skill, targeting, in aimPoint);
        if (rangeResult != SkillCastResult.Ok) return rangeResult;

        if (!caster.CastWindowOpen) return SkillCastResult.CastWindowTiming;

        if (!caster.HasTargets && !IsImplicitlyTargeted(skill.TargetMode)) return SkillCastResult.NoTargets;

        return SkillCastResult.Ok;
    }

    public static SkillCastResult CheckRangeAndLineOfSight(
        in SkillDefinition skill,
        ISkillTargetingQuery targeting,
        in Vector3Fixed aimPoint)
    {
        ArgumentNullException.ThrowIfNull(targeting);

        var effectiveRange = EffectiveRange(in skill, targeting);
        var effectiveRangeSquared = effectiveRange * effectiveRange;

        var squaredDistance = targeting.SquaredPlanarDistanceToAim(in aimPoint);
        if (squaredDistance > effectiveRangeSquared) return SkillCastResult.MoveCloser;

        if (!targeting.HasLineOfSight(in aimPoint)) return SkillCastResult.LineOfSightBlocked;

        if (skill.TargetMode != SkillTargetMode.GroundPoint
            && !targeting.IsTargetStateValid(skill.IsRevive))
            return SkillCastResult.InvalidTarget;

        return SkillCastResult.Ok;
    }

    public static float EffectiveRange(in SkillDefinition skill, ISkillTargetingQuery targeting)
    {
        ArgumentNullException.ThrowIfNull(targeting);
        var range = skill.BaseRange + targeting.CasterBodyRadius + targeting.BuffRangeBonus;
        return range < SkillDefinition.MinEffectiveRange ? SkillDefinition.MinEffectiveRange : range;
    }

    private static bool IsImplicitlyTargeted(SkillTargetMode mode)
    {
        return mode is SkillTargetMode.SingleSelfOrPrimary
            or SkillTargetMode.GroundPoint
            or SkillTargetMode.SelfOnly;
    }
}