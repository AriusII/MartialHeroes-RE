using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Domain.Skills.Skills;

public sealed class CooldownTable
{
    public const int SlotCount = 240;

    private readonly CooldownSlot[] _slots = new CooldownSlot[SlotCount];

    public void SetSlot(int slotIndex, SkillId skill, int durationMs)
    {
        if ((uint)slotIndex >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(slotIndex));

        if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be non-negative.");

        _slots[slotIndex] = new CooldownSlot
        {
            Skill = skill,
            DurationMs = durationMs,
            SetTimeMs = 0,
            RemainingMs = 0,
            Armed = false
        };
    }

    public void TickAll(long now)
    {
        for (var i = 0; i < _slots.Length; i++) _slots[i] = _slots[i].Tick(now);
    }

    public bool CheckReady(SkillId skill, long now)
    {
        TickAll(now);
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i].Skill == skill)
                return _slots[i].IsReady;

        return true;
    }
}