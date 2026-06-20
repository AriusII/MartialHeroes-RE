using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Skills.Skills;

/// <summary>
///     The pure, deterministic cast-pipeline gate chain: given a skill definition, the caster's gate
///     state, the cooldown table and a targeting-query port, returns the §2.3 result code. <c>0</c>
///     (<see cref="SkillCastResult.Ok" />) means every gate passed and the use-skill request would be sent.
///     spec: Docs/RE/specs/skills.md §2 ("Cast pipeline").
/// </summary>
/// <remarks>
///     The gates run in the exact §2.1 order, short-circuiting on the first failure. Range / line-of-sight
///     / target-state (§2.2) are delegated to the injected <see cref="ISkillTargetingQuery" />. The Domain
///     owns the squared-range math (effective range from base_range + body radius + buff bonus, clamped to
///     a 1.0 minimum, compared as squared planar distance). spec: skills.md §2.1, §2.2.
/// </remarks>
public static class SkillCastValidator
{
    /// <summary>
    ///     Runs the ordered gate chain. spec: Docs/RE/specs/skills.md §2.1 (gates 1..19) / §2.2.
    /// </summary>
    /// <param name="skill">The skill being cast (already resolved; a basic attack must be category 1). spec: §2.1 gate 10.</param>
    /// <param name="caster">The caster gate inputs (one flag per ordered gate). spec: §2.1.</param>
    /// <param name="cooldowns">The 240-slot cooldown table (gate 13). spec: §2.1 gate 13 / §4.</param>
    /// <param name="targeting">The range / LoS / target-state port (§2.2). spec: §2.2.</param>
    /// <param name="aimPoint">The aim point (XZ) for the range / LoS test. spec: §2.2.</param>
    /// <param name="now">Caller-supplied millisecond clock for the cooldown tick. spec: §4.</param>
    /// <returns>The §2.3 result code; <see cref="SkillCastResult.Ok" /> on success.</returns>
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

        // Gate 1: party / relation. spec: §2.1 gate 1 (code 17).
        if (!caster.PartyRelationAllied) return SkillCastResult.PartyRelation;

        // Gate 2: billing / rank. spec: §2.1 gate 2 (code 1).
        if (!caster.BillingRankOk) return SkillCastResult.BillingOrRank;

        // Gate 3: busy / already casting. spec: §2.1 gate 3 (code 13).
        if (caster.IsBusyCasting) return SkillCastResult.AlreadyCasting;

        // Gate 4: mounted / vehicle. spec: §2.1 gate 4 (code 4).
        if (caster.IsMounted) return SkillCastResult.MountedOrUnresolved;

        // Gate 5: map / zone-mode. spec: §2.1 gate 5 (code 16).
        if (caster.MapModeForbidsCasting) return SkillCastResult.MapModeForbidden;

        // Gate 6: stun / silence. spec: §2.1 gate 6 (code 19).
        if (caster.IsStunnedOrSilenced) return SkillCastResult.StunnedOrSilenced;

        // Gate 7: alive. spec: §2.1 gate 7 (code 2).
        if (!caster.IsAlive) return SkillCastResult.NotAlive;

        // Gate 8: action-lock. spec: §2.1 gate 8 (code 20).
        if (caster.IsActionLocked) return SkillCastResult.ActionLocked;

        // Gate 9: current-target hostile-state. spec: §2.1 gate 9 (code 3).
        if (caster.TargetInBlockingHostileState) return SkillCastResult.TargetHostileState;

        // Gate 11: weapon / stance requirement (only when the check is active). spec: §2.1 gate 11 (code 18).
        if (skill.WeaponReqActive && !caster.WeaponRequirementSatisfied) return SkillCastResult.WeaponRequirement;

        // Gate 12: self-cast eligibility. spec: §2.1 gate 12 (code 5).
        if (!caster.SelfCastEligible) return SkillCastResult.SelfCastIneligible;

        // Gate 13: cooldown. All cooldowns are ticked, then the skill's recast state is checked; if
        // still cooling AND not in the cast-gate exempt category (category 1 ONLY — narrower than the
        // arm-path exemption, which also covers category 5), block. spec: §2.1 gate 13 / §4 / §5.2.
        if (!skill.IsCastGateCooldownExempt && !cooldowns.CheckReady(skill.Id, now)) return SkillCastResult.OnCooldown;

        // Gate 14: MP affordability — fails when available MP < 100 × mp_cost_factor. spec: §2.1 gate 14 (code 6).
        var mpRequired = (long)SkillDefinition.MpGateMultiplier * skill.MpCostFactor;
        if (caster.AvailableMp < mpRequired) return SkillCastResult.NotEnoughMp;

        // Gate 16: range / line-of-sight / target-state (§2.2). Ground/point mode resolves no actor
        // targets and skips the target-state test. spec: §2.1 gate 15/16 / §2.2 / §3 (mode 5).
        var rangeResult = CheckRangeAndLineOfSight(in skill, targeting, in aimPoint);
        if (rangeResult != SkillCastResult.Ok) return rangeResult;

        // Gate 17: cast-window timing. spec: §2.1 gate 17 (code 11).
        if (!caster.CastWindowOpen) return SkillCastResult.CastWindowTiming;

        // Gate 18: at least one target array populated. spec: §2.1 gate 18 (code 12).
        // Self / ground modes always have the caster (or no actor target) and are treated as populated.
        if (!caster.HasTargets && !IsImplicitlyTargeted(skill.TargetMode)) return SkillCastResult.NoTargets;

        // Gate 19: success. spec: §2.1 gate 19 (code 0).
        return SkillCastResult.Ok;
    }

    /// <summary>
    ///     Computes the effective range and runs the §2.2 squared-distance, LoS and target-state tests.
    ///     spec: Docs/RE/specs/skills.md §2.2.
    /// </summary>
    public static SkillCastResult CheckRangeAndLineOfSight(
        in SkillDefinition skill,
        ISkillTargetingQuery targeting,
        in Vector3Fixed aimPoint)
    {
        ArgumentNullException.ThrowIfNull(targeting);

        var effectiveRange = EffectiveRange(in skill, targeting);
        var effectiveRangeSquared = effectiveRange * effectiveRange;

        // Distance test: squared planar (XZ) distance vs. effective_range². Beyond range → move-closer
        // (code 8, no toast). spec: §2.2 ("not an error toast ... returns code 8").
        var squaredDistance = targeting.SquaredPlanarDistanceToAim(in aimPoint);
        if (squaredDistance > effectiveRangeSquared) return SkillCastResult.MoveCloser;

        // Terrain / LoS test → code 9 if blocked. spec: §2.2.
        if (!targeting.HasLineOfSight(in aimPoint)) return SkillCastResult.LineOfSightBlocked;

        // Target-state test → code 10. Skipped for the ground/point target mode. spec: §2.2 / §3 (mode 5).
        if (skill.TargetMode != SkillTargetMode.GroundPoint
            && !targeting.IsTargetStateValid(skill.IsRevive))
            return SkillCastResult.InvalidTarget;

        return SkillCastResult.Ok;
    }

    /// <summary>
    ///     Effective range = <c>max(1.0, base_range + caster body radius + buff range bonus)</c>.
    ///     spec: Docs/RE/specs/skills.md §2.2.
    /// </summary>
    public static float EffectiveRange(in SkillDefinition skill, ISkillTargetingQuery targeting)
    {
        ArgumentNullException.ThrowIfNull(targeting);
        var range = skill.BaseRange + targeting.CasterBodyRadius + targeting.BuffRangeBonus;
        return range < SkillDefinition.MinEffectiveRange ? SkillDefinition.MinEffectiveRange : range;
    }

    /// <summary>
    ///     True for target modes that resolve the caster (or no actor) as an implicit target, so the gate-18
    ///     "no targets" check does not apply. spec: skills.md §3 (mode 0 self/primary, 5 ground/point, 11 self-only).
    /// </summary>
    private static bool IsImplicitlyTargeted(SkillTargetMode mode)
    {
        return mode is SkillTargetMode.SingleSelfOrPrimary
            or SkillTargetMode.GroundPoint
            or SkillTargetMode.SelfOnly;
    }
}