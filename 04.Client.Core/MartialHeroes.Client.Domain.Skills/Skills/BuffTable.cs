namespace MartialHeroes.Client.Domain.Skills.Skills;

public sealed class BuffTable
{
    public const int SlotCount = 31;
    private readonly BuffDebuff[] _slots = new BuffDebuff[SlotCount];

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

    public void Tick()
    {
        var expired = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            var (next, slotExpired) = _slots[i].TickOnce();
            _slots[i] = next;
            if (slotExpired) expired++;
        }
    }

    public void ClearAll()
    {
        Array.Clear(_slots);
    }
}