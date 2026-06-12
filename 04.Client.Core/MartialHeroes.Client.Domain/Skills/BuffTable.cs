using MartialHeroes.Client.Domain.Stats;

namespace MartialHeroes.Client.Domain.Skills;

/// <summary>
/// The 31-slot per-actor status (buff / debuff) table with refresh-by-slot apply, single-decrement
/// tick, dispel, and a bridge to <see cref="StatAggregation"/> (buffs are stat contributions).
/// spec: Docs/RE/specs/skills.md §6.1 (31 slots, 12-byte stride) / §6.2 / §6.3.
/// </summary>
/// <remarks>
/// A fixed-size <see cref="BuffDebuff"/> array indexed by slot (0..30). Apply overwrites the target
/// slot (refresh, not additive); a value of 0 clears it. The tick decrements every active slot by 1.
/// All operations are deterministic and engine-free; no per-tick heap allocation. spec: skills.md §6.
/// </remarks>
public sealed class BuffTable
{
    /// <summary>Number of per-actor buff slots (index 0..30). spec: skills.md §6.1 ("31 slots (index 0..30)").</summary>
    public const int SlotCount = 31;

    private readonly BuffDebuff[] _slots = new BuffDebuff[SlotCount];

    /// <summary>Reads the slot at <paramref name="slotIndex"/>.</summary>
    public BuffDebuff this[int slotIndex] => _slots[slotIndex];

    /// <summary>Number of slots (<see cref="SlotCount"/>).</summary>
    public int Count => _slots.Length;

    /// <summary>
    /// Applies (sets) the 12-byte status entry at <paramref name="slotIndex"/> — a refresh that
    /// overwrites the slot (no stack counter). A <paramref name="durationTicks"/> of 0 clears the slot
    /// (a §6.1 "clear" — value == 0 zeros the effect-code dword). spec: skills.md §6.1 / §6.3.
    /// </summary>
    public void Apply(int slotIndex, int effectCode, int durationTicks, int param, ushort magnitude)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        // Clear on value == 0. spec: skills.md §6.1 ("a clear (value == 0) zeros the effect-code dword").
        if (durationTicks == 0)
        {
            _slots[slotIndex] = BuffDebuff.Empty with { Param = param };
            return;
        }

        _slots[slotIndex] = new BuffDebuff
        {
            EffectCode = effectCode,
            DurationTicks = durationTicks < 0 ? 0 : durationTicks,
            Param = param,
            Magnitude = magnitude,
        };
    }

    /// <summary>Clears the slot at <paramref name="slotIndex"/>. spec: skills.md §6.1 (clear).</summary>
    public void Clear(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        _slots[slotIndex] = BuffDebuff.Empty;
    }

    /// <summary>
    /// Runs one §6.3 buff tick over the whole table: each active slot's duration decrements by 1; a slot
    /// that reaches 0 expires (becomes inactive). Returns how many slots expired this tick.
    /// spec: Docs/RE/specs/skills.md §6.3.
    /// </summary>
    public int Tick()
    {
        int expired = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            (BuffDebuff next, bool slotExpired) = _slots[i].TickOnce();
            _slots[i] = next;
            if (slotExpired)
            {
                expired++;
            }
        }

        return expired;
    }

    /// <summary>
    /// Applies the §6.2 dispel / cleanse (effect code 48): clears the effect-code 43, 46 and 47 slots
    /// across the table and resets stance to the default. spec: Docs/RE/specs/skills.md §6.2 (48 / 0x30).
    /// </summary>
    public void Dispel()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            int code = _slots[i].EffectCode;
            if (code == (int)BuffEffectCode.EnterStance
                || code == (int)BuffEffectCode.AppearanceSwap
                || code == (int)BuffEffectCode.RootSnare)
            {
                _slots[i] = BuffDebuff.Empty;
            }
        }
    }

    /// <summary>
    /// True when any active slot carries the §6.2 root/snare effect (code 47): the actor is movement /
    /// control restricted. spec: Docs/RE/specs/skills.md §6.2 (47 / 0x2F).
    /// </summary>
    public bool IsRooted => HasActiveEffect(BuffEffectCode.RootSnare);

    /// <summary>True when any active slot carries <paramref name="effect"/>. spec: skills.md §6.2.</summary>
    public bool HasActiveEffect(BuffEffectCode effect)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].IsActive && _slots[i].EffectCode == (int)effect)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Clears every slot.</summary>
    public void ClearAll() => Array.Clear(_slots);
}