namespace MartialHeroes.Client.Domain.Skills.Skills;

/// <summary>
///     Target-resolution / area shape for a skill, selected by the skill-data <c>target_mode</c> field.
///     spec: Docs/RE/specs/skills.md §3 (target_mode at +1308, table of shapes).
/// </summary>
/// <remarks>
///     <para>
///         Values are the exact <c>target_mode</c> bytes the cast pipeline switches on. Only the modes the
///         subsystem was observed handling are enumerated; modes 8 and 12+ were not observed and are
///         intentionally absent (an unseen value is <c>UNVERIFIED</c> — reject at the decode boundary).
///         spec: skills.md §3 ("Modes 8 and 12+ were not observed ... treat any unseen mode value as
///         UNVERIFIED").
///     </para>
/// </remarks>
public enum SkillTargetMode : byte
{
    /// <summary>Single (self / primary): uses the primary target only. spec: skills.md §3 (mode 0).</summary>
    SingleSelfOrPrimary = 0,

    /// <summary>Single target with faction + team-id gate. spec: skills.md §3 (mode 1).</summary>
    SingleTarget = 1,

    /// <summary>Single enemy / heal: friendly target is a heal target, else an enemy. spec: skills.md §3 (mode 2).</summary>
    SingleEnemyOrHeal = 2,

    /// <summary>Chain / nearby AoE around the caster (radius from aoe_radius). spec: skills.md §3 (mode 3).</summary>
    ChainNearbyAoe = 3,

    /// <summary>Cone / forward-line AoE (length from base_range). spec: skills.md §3 (mode 4).</summary>
    ConeForwardAoe = 4,

    /// <summary>Ground / point: no actor targets resolved (blink / ground-target). spec: skills.md §3 (mode 5).</summary>
    GroundPoint = 5,

    /// <summary>Party AoE: walks the party roster. spec: skills.md §3 (mode 6).</summary>
    PartyAoe = 6,

    /// <summary>Faction / group-gated single (style / team match). spec: skills.md §3 (mode 7).</summary>
    FactionGatedSingle = 7,

    /// <summary>PK-gated single (team-byte gate). spec: skills.md §3 (mode 9).</summary>
    PkGatedSingle = 9,

    /// <summary>Radial AoE, both factions. spec: skills.md §3 (mode 10 / 0x0A).</summary>
    RadialAoeBothFactions = 10,

    /// <summary>Self-only: caster as the single target. spec: skills.md §3 (mode 11 / 0x0B).</summary>
    SelfOnly = 11
}