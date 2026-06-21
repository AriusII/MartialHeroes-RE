namespace MartialHeroes.Client.Domain.Skills.Skills;

/// <summary>
///     The caster-side gate inputs the cast pipeline reads from the local player's state, in the order the
///     §2.1 ordered gate chain consults them. These are injected by the caller (Application) — the Domain
///     holds no player state of its own. spec: Docs/RE/specs/skills.md §2.1.
/// </summary>
/// <remarks>
///     Each flag corresponds to one ordered gate; <see cref="SkillCastValidator" /> short-circuits on the
///     first failure and returns that gate's <see cref="SkillCastResult" />. The booleans are phrased as the
///     <em>allowing</em> condition (e.g. <see cref="IsAlive" /> true = pass) so the gate chain reads as a
///     sequence of "must be true / must be clear" predicates matching the spec. spec: skills.md §2.1.
/// </remarks>
public readonly record struct CasterState
{
    /// <summary>
    ///     Gate 1: every referenced party/relation actor is allied. false → <see cref="SkillCastResult.PartyRelation" />.
    ///     spec: skills.md §2.1 gate 1.
    /// </summary>
    public bool PartyRelationAllied { get; init; }

    /// <summary>
    ///     Gate 2: billing / rank cap passes. false → <see cref="SkillCastResult.BillingOrRank" />. spec: skills.md §2.1
    ///     gate 2.
    /// </summary>
    public bool BillingRankOk { get; init; }

    /// <summary>
    ///     Gate 3: not already casting / busy. true (busy) → <see cref="SkillCastResult.AlreadyCasting" />. spec:
    ///     skills.md §2.1 gate 3.
    /// </summary>
    public bool IsBusyCasting { get; init; }

    /// <summary>
    ///     Gate 4: a mount-state actor is present. true → <see cref="SkillCastResult.MountedOrUnresolved" />. spec:
    ///     skills.md §2.1 gate 4.
    /// </summary>
    public bool IsMounted { get; init; }

    /// <summary>
    ///     Gate 5: the current scene mode forbids casting. true → <see cref="SkillCastResult.MapModeForbidden" />. spec:
    ///     skills.md §2.1 gate 5.
    /// </summary>
    public bool MapModeForbidsCasting { get; init; }

    /// <summary>
    ///     Gate 6: stunned / silenced. true → <see cref="SkillCastResult.StunnedOrSilenced" />. spec: skills.md §2.1 gate
    ///     6.
    /// </summary>
    public bool IsStunnedOrSilenced { get; init; }

    /// <summary>
    ///     Gate 7: alive / can-act word set. false → <see cref="SkillCastResult.NotAlive" />. spec: skills.md §2.1 gate
    ///     7.
    /// </summary>
    public bool IsAlive { get; init; }

    /// <summary>
    ///     Gate 8: generic action-lock flag set. true → <see cref="SkillCastResult.ActionLocked" />. spec: skills.md §2.1
    ///     gate 8.
    /// </summary>
    public bool IsActionLocked { get; init; }

    /// <summary>
    ///     Gate 9: the selected target is in the blocking hostile state. true →
    ///     <see cref="SkillCastResult.TargetHostileState" />. spec: skills.md §2.1 gate 9.
    /// </summary>
    public bool TargetInBlockingHostileState { get; init; }

    /// <summary>
    ///     Gate 11: the worn weapon satisfies the skill's weapon/stance requirement. false →
    ///     <see cref="SkillCastResult.WeaponRequirement" />. spec: skills.md §2.1 gate 11.
    /// </summary>
    public bool WeaponRequirementSatisfied { get; init; }

    /// <summary>
    ///     Gate 12: self-cast eligible. false → <see cref="SkillCastResult.SelfCastIneligible" />. spec: skills.md §2.1
    ///     gate 12.
    /// </summary>
    public bool SelfCastEligible { get; init; }

    /// <summary>
    ///     Gate 14: available MP for the affordability check (MP gate fails when MP &lt; 100 × mp_cost_factor). spec:
    ///     skills.md §2.1 gate 14.
    /// </summary>
    public int AvailableMp { get; init; }

    /// <summary>
    ///     Gate 17: the cast-window timers are within the warm-up / valid window. false →
    ///     <see cref="SkillCastResult.CastWindowTiming" />. spec: skills.md §2.1 gate 17.
    /// </summary>
    public bool CastWindowOpen { get; init; }

    /// <summary>
    ///     Gate 18: at least one of the ally / enemy target arrays was populated. false →
    ///     <see cref="SkillCastResult.NoTargets" />. spec: skills.md §2.1 gate 18.
    /// </summary>
    public bool HasTargets { get; init; }

    /// <summary>
    ///     A state with every gate passing and ample resources — the all-clear baseline for tests and for
    ///     building a real caster state by overriding only the relevant gates with <c>with</c>.
    /// </summary>
    public static CasterState AllClear => new()
    {
        PartyRelationAllied = true,
        BillingRankOk = true,
        IsBusyCasting = false,
        IsMounted = false,
        MapModeForbidsCasting = false,
        IsStunnedOrSilenced = false,
        IsAlive = true,
        IsActionLocked = false,
        TargetInBlockingHostileState = false,
        WeaponRequirementSatisfied = true,
        SelfCastEligible = true,
        AvailableMp = int.MaxValue,
        CastWindowOpen = true,
        HasTargets = true
    };
}