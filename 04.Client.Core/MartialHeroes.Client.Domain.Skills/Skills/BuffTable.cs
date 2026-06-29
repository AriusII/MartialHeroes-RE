namespace MartialHeroes.Client.Domain.Skills.Skills;

public sealed class BuffTable
{
    public const int SlotCount = 31;

    public const long TickIntervalMs = 4000;

    private readonly BuffDebuff[] _slots = new BuffDebuff[SlotCount];
    private readonly BuffPeriodicVisual[] _visuals = new BuffPeriodicVisual[SlotCount];
    private long _lastTickMs;
    private bool _initialized;

    public ReadOnlySpan<BuffDebuff> Slots => _slots;

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

    public ReadOnlySpan<BuffPeriodicVisual> Tick(long now)
    {
        if (!_initialized)
        {
            _initialized = true;
            _lastTickMs = now;
            return ReadOnlySpan<BuffPeriodicVisual>.Empty;
        }

        if (now - _lastTickMs < TickIntervalMs) return ReadOnlySpan<BuffPeriodicVisual>.Empty;

        _lastTickMs = now;

        var visualCount = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];

            if (BuffDispatch.IsPeriodicVisual(slot.EffectCode) && slot.DurationTicks > 0)
                _visuals[visualCount++] = new BuffPeriodicVisual(i, slot.EffectCode, slot.Param);

            if (slot.DurationTicks > 1)
            {
                _slots[i] = slot with { DurationTicks = slot.DurationTicks - 1 };
                continue;
            }

            if (slot.DurationTicks == 0)
            {
                _slots[i] = BuffDebuff.Empty;
                continue;
            }

            if (slot.DurationTicks < 0) _slots[i] = BuffDebuff.Empty;
        }

        return new ReadOnlySpan<BuffPeriodicVisual>(_visuals, 0, visualCount);
    }

    public bool IsSlotVisible(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length) return false;

        return BuffVisibility.IsVisible(_slots[slotIndex]);
    }

    public BuffEffectState ResolveEffectState()
    {
        var motion = BuffEffectState.NoMotionState;
        var stealth = false;
        var restricted = false;
        var threshold = false;
        var local = false;
        var dispel = false;

        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (!slot.IsActive) continue;

            switch (slot.EffectCode)
            {
                case BuffDispatch.EnterStanceId:
                case BuffDispatch.TransformBId:
                case BuffDispatch.TransformRevertId:
                    motion = BuffDispatch.MotionStateFor(slot.EffectCode);
                    break;
                case BuffDispatch.StealthId:
                    stealth = true;
                    break;
                case BuffDispatch.MovementRestrictId:
                    restricted = true;
                    break;
                case BuffDispatch.LocalStatusFlagId:
                    local = true;
                    break;
                case BuffDispatch.ThresholdFlagId:
                    if (slot.Magnitude < BuffDispatch.ThresholdMagnitudeLimit) threshold = true;
                    break;
                case BuffDispatch.DispelId:
                    dispel = true;
                    break;
            }
        }

        if (dispel)
        {
            motion = motion == BuffEffectState.NoMotionState
                ? BuffEffectState.NoMotionState
                : BuffDispatch.StanceDefault;
            restricted = false;
        }

        return new BuffEffectState
        {
            MotionState = motion,
            Stealth = stealth,
            MovementRestricted = restricted,
            ThresholdFlag = threshold,
            LocalStatusFlag = local
        };
    }

    public void ClearAll()
    {
        Array.Clear(_slots);
        _initialized = false;
        _lastTickMs = 0;
    }
}
