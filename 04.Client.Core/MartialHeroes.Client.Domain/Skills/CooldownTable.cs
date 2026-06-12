using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// The flat 240-slot cooldown ("recast") table keyed by hotbar slot index. Implements the §4 cooldown
/// subsystem operations: tick-all, arm, check-ready, and duration-table rebuild.
/// spec: Docs/RE/specs/skills.md §4 ("240 parallel slots").
/// </summary>
/// <remarks>
/// A single <see cref="CooldownSlot"/> array (struct-of-arrays flattened to array-of-structs) keyed by
/// hotbar slot index. All timing is in caller-supplied milliseconds — no ambient clock. Mutations are
/// in place on the fixed-size backing array; no per-tick heap allocation. spec: skills.md §4
/// (implementation note).
/// </remarks>
public sealed class CooldownTable
{
    /// <summary>The number of cooldown / hotbar slots. spec: skills.md §4 ("240 parallel slots").</summary>
    public const int SlotCount = 240;

    private readonly CooldownSlot[] _slots = new CooldownSlot[SlotCount];

    /// <summary>Reads the slot at <paramref name="index"/>.</summary>
    public CooldownSlot this[int index] => _slots[index];

    /// <summary>Number of slots (<see cref="SlotCount"/>).</summary>
    public int Count => _slots.Length;

    /// <summary>
    /// Sets the skill id occupying <paramref name="slotIndex"/> and its cooldown duration (ms), leaving
    /// it ready. Mirrors the hotbar assignment / duration-table rebuild. spec: skills.md §4 (rebuild).
    /// </summary>
    public void SetSlot(int slotIndex, SkillId skill, int durationMs)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be non-negative.");
        }

        _slots[slotIndex] = new CooldownSlot
        {
            Skill = skill,
            DurationMs = durationMs,
            SetTimeMs = 0,
            RemainingMs = 0,
            Armed = false,
        };
    }

    /// <summary>Clears the slot at <paramref name="slotIndex"/>.</summary>
    public void ClearSlot(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        _slots[slotIndex] = CooldownSlot.Empty;
    }

    /// <summary>
    /// Ticks every armed slot to <paramref name="now"/>. spec: skills.md §4 ("Tick-all (per frame)").
    /// </summary>
    public void TickAll(long now)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = _slots[i].Tick(now);
        }
    }

    /// <summary>
    /// Arms the cooldown for <paramref name="skill"/>: linear-searches the slots for the matching id and
    /// arms the first match with <paramref name="now"/>. spec: skills.md §4 ("Arm a cooldown").
    /// </summary>
    /// <returns>The armed slot index, or -1 if the skill is not in the table.</returns>
    public int Arm(SkillId skill, long now)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Skill == skill)
            {
                _slots[i] = _slots[i].Arm(now);
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Ticks all slots to <paramref name="now"/>, then returns whether <paramref name="skill"/> is ready
    /// (not armed / no remaining time). A skill not present in the table is treated as ready.
    /// spec: skills.md §4 ("Check ready (cast gate)").
    /// </summary>
    public bool CheckReady(SkillId skill, long now)
    {
        TickAll(now);
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Skill == skill)
            {
                return _slots[i].IsReady;
            }
        }

        return true; // not on the hotbar → nothing to cool. spec: skills.md §4.
    }
}