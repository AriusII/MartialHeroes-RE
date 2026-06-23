namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct BuffDebuff
{
    public int EffectCode { get; init; }

    public int DurationTicks { get; init; }

    public int Param { get; init; }

    public ushort Magnitude { get; init; }

    public static BuffDebuff Empty => default;

    public bool IsActive => DurationTicks > 0;

    public (BuffDebuff Next, bool Expired) TickOnce()
    {
        if (DurationTicks <= 0) return (DurationTicks < 0 ? this with { DurationTicks = 0 } : this, false);

        var next = DurationTicks - 1;
        var expired = next == 0;
        return (this with { DurationTicks = next }, expired);
    }
}