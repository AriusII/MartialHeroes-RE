using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// The port the cast pipeline uses to probe the world for the §2.2 range / line-of-sight / target-state
/// tests. The Domain defines the <b>contract</b>; the implementation (against actor positions, terrain
/// solids and target state) lives in Application / Assets, never here.
/// spec: Docs/RE/specs/skills.md §2.2 / §3.1.
/// </summary>
/// <remarks>
/// All inputs and outputs are deterministic and engine-free. The range test is expressed in terms the
/// Domain owns (squared planar distance vs. effective-range squared), so a pure test double can satisfy
/// the contract without any world. spec: skills.md §2.2 ("squared planar (XZ) distance").
/// </remarks>
public interface ISkillTargetingQuery
{
    /// <summary>
    /// The caster's body radius, added to the skill's <c>base_range</c> when computing effective range.
    /// spec: skills.md §2.2 ("effective range = base_range + the caster's body radius + a per-buff range bonus").
    /// </summary>
    float CasterBodyRadius { get; }

    /// <summary>
    /// A per-buff range bonus read from a buff slot, added to the effective range.
    /// spec: skills.md §2.2 ("+ a per-buff range bonus (read from a buff slot)").
    /// </summary>
    float BuffRangeBonus { get; }

    /// <summary>
    /// The squared planar (XZ) distance from the caster to the aim point. Compared against the effective
    /// range squared by the validator. spec: skills.md §2.2 ("squared planar (XZ) distance").
    /// </summary>
    float SquaredPlanarDistanceToAim(in Vector3Fixed aimPoint);

    /// <summary>
    /// True when the terrain / line-of-sight to <paramref name="aimPoint"/> is clear (not blocked).
    /// false → <see cref="SkillCastResult.LineOfSightBlocked"/>. spec: skills.md §2.2 (LoS test, code 9).
    /// </summary>
    bool HasLineOfSight(in Vector3Fixed aimPoint);

    /// <summary>
    /// True when the resolved target passes the §3.1 target-state validation for this skill (alive/dead
    /// for revive, not using a tool, not an untargetable mob). Skipped by the caller for the
    /// ground/point mode. false → <see cref="SkillCastResult.InvalidTarget"/>. spec: skills.md §2.2 (code 10) / §3.1.
    /// </summary>
    bool IsTargetStateValid(bool isReviveSkill);
}
