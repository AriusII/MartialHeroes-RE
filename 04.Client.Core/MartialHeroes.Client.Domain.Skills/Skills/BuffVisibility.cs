namespace MartialHeroes.Client.Domain.Skills.Skills;

public static class BuffVisibility
{
    public const int HiddenBandLow = 80;

    public const int HiddenBandHigh = 130;

    public const int HiddenSingletonId = 59;

    public static bool IsHidden(int buffId)
    {
        if (buffId == HiddenSingletonId) return true;

        return buffId >= HiddenBandLow && buffId <= HiddenBandHigh;
    }

    public static bool IsVisible(in BuffDebuff slot)
    {
        if (slot.EffectCode <= 0) return false;

        if (slot.DurationTicks == 0) return false;

        return !IsHidden(slot.EffectCode);
    }
}