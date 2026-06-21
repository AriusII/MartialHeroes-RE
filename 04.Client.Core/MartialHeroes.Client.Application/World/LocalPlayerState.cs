using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Domain.Skills.Skills;
using MartialHeroes.Client.Domain.Stats.Stats;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Application.World;

/// <summary>
///     The application-owned holder for the local player's combat / skill subsystems that have no place on
///     the <see cref="Actor" /> aggregate: the 240-slot skill hotbar, the
///     parallel cooldown ("recast") table, the 31-slot buff/status table, the single cast state machine, and
///     the most-recent recomputed combat-stat aggregate.
/// </summary>
/// <remarks>
///     <para>
///         The Domain owns the deterministic <em>rules</em> for each of these (<see cref="CooldownTable" />,
///         <see cref="BuffTable" />, <see cref="SkillCastState" />, <see cref="StatAggregation" />); this holder is
///         pure orchestration plumbing — it groups the live instances so the inbound handlers can mutate them and
///         the <see cref="MartialHeroes.Client.Application.Engine.GameEngineLoop" /> can tick them once per fixed
///         tick. spec: Docs/RE/specs/skills.md §4 (cooldown), §6 (buff table), §2/§5 (cast state).
///     </para>
///     <para>
///         <b>Threading.</b> Like <see cref="ClientWorld" />, this is mutated only by the single network-reader /
///         loop logical owner; it is deliberately lock-free.
///     </para>
/// </remarks>
public sealed class LocalPlayerState
{
    /// <summary>The 240-slot skill hotbar (parallel to the cooldown table). spec: skills.md §4 / structs/skill.md (240).</summary>
    public const int HotbarSlotCount = 240;

    private readonly SkillId[] _hotbar = new SkillId[HotbarSlotCount];
    private readonly short[] _hotbarPoints = new short[HotbarSlotCount];

    /// <summary>The 240-slot cooldown table keyed by hotbar slot index. spec: skills.md §4.</summary>
    public CooldownTable Cooldowns { get; } = new();

    /// <summary>The 31-slot per-actor buff/status table for the local player. spec: skills.md §6.1.</summary>
    public BuffTable Buffs { get; } = new();

    /// <summary>The local player's single cast state machine. spec: skills.md §2 / §5.</summary>
    public SkillCastState CastState { get; set; } = SkillCastState.Idle;

    /// <summary>The most recently recomposed derived combat-stat aggregate. spec: combat.md §1 / §2.</summary>
    public CombatStats Combat { get; set; } = CombatStats.Empty;

    /// <summary>The skill currently occupying hotbar <paramref name="slot" /> (<see cref="SkillId.None" /> when empty).</summary>
    public SkillId HotbarSkill(int slot)
    {
        return _hotbar[slot];
    }

    /// <summary>The skill-point allocation for hotbar <paramref name="slot" />.</summary>
    public short HotbarPoints(int slot)
    {
        return _hotbarPoints[slot];
    }

    /// <summary>
    ///     Writes the skill + points + cooldown duration into hotbar <paramref name="slot" /> and mirrors the
    ///     skill id and duration into the parallel cooldown table (leaving the slot ready). Mirrors the 5/33
    ///     authoritative server overwrite + the §4 duration-table rebuild. spec: skills.md §4 / structs/skill.md (5/33).
    /// </summary>
    public void SetHotbarSlot(int slot, SkillId skill, short points, int cooldownDurationMs)
    {
        if ((uint)slot >= (uint)_hotbar.Length) throw new ArgumentOutOfRangeException(nameof(slot));

        _hotbar[slot] = skill;
        _hotbarPoints[slot] = points;
        Cooldowns.SetSlot(slot, skill, cooldownDurationMs);
    }
}