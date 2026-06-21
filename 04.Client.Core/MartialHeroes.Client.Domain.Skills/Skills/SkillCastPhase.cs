namespace MartialHeroes.Client.Domain.Skills.Skills;

/// <summary>
///     The cast lifecycle phase of a skill cast: idle → casting → cooldown.
///     spec: Docs/RE/specs/skills.md §2 (cast request), §5.2 (cast confirm + cooldown arm), §4 (cooldown).
/// </summary>
/// <remarks>
///     <b>Modeling choice (ours).</b> The legacy client splits this across the cast pipeline (§2), the
///     cast-confirm path (§5.2) and the recast table (§4); it does not expose a single named three-phase
///     enum. We model the observable lifecycle — idle before a cast, casting during the warm-up window
///     (the §5.2 ≈ 550 ms cast time-out), cooldown while the recast slot is armed — as one explicit,
///     total state machine for deterministic re-implementation. spec: skills.md §2, §4, §5.2.
/// </remarks>
public enum SkillCastPhase
{
    /// <summary>No active cast; ready to begin one (subject to the §2.1 gates). spec: skills.md §2.</summary>
    Idle = 0,

    /// <summary>A cast was accepted and the cast-time window is counting down. spec: skills.md §5.2 (cast time-out).</summary>
    Casting = 1,

    /// <summary>The cast completed and the recast cooldown is armed / counting down. spec: skills.md §4 / §5.2.</summary>
    Cooldown = 2
}