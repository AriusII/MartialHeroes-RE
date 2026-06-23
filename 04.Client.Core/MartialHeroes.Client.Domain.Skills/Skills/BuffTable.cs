namespace MartialHeroes.Client.Domain.Skills.Skills;

public sealed class BuffTable
{
    public const int SlotCount = 31;

    private readonly BuffDebuff[] _slots = new BuffDebuff[SlotCount];

    public BuffDebuff this[int slotIndex] => _slots[slotIndex];

    public int Count => _slots.Length;

    public bool IsRooted => HasActiveEffect(BuffEffectCode.RootSnare);

    public void Apply(int slotIndex, int effectCode, int durationTicks, int param, ushort magnitude)
    {
        if ((uint)slotIndex >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(slotIndex));

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
            Magnitude = magnitude
        };
    }

    public void Clear(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length) throw new ArgumentOutOfRangeException(nameof(slotIndex));

        _slots[slotIndex] = BuffDebuff.Empty;
    }

    public int Tick()
    {
        var expired = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            var (next, slotExpired) = _slots[i].TickOnce();
            _slots[i] = next;
            if (slotExpired) expired++;
        }

        return expired;
    }

    public void Dispel()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            var code = _slots[i].EffectCode;
            if (code == (int)BuffEffectCode.EnterStance
                || code == (int)BuffEffectCode.AppearanceSwap
                || code == (int)BuffEffectCode.RootSnare)
                _slots[i] = BuffDebuff.Empty;
        }
    }

    public bool HasActiveEffect(BuffEffectCode effect)
    {
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i].IsActive && _slots[i].EffectCode == (int)effect)
                return true;

        return false;
    }

    public void ClearAll()
    {
        Array.Clear(_slots);
    }
}