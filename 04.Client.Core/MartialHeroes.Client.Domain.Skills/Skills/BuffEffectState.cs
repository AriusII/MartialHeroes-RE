namespace MartialHeroes.Client.Domain.Skills.Skills;

public readonly record struct BuffEffectState
{
    public const int NoMotionState = 0;

    public int MotionState { get; init; }

    public bool Stealth { get; init; }

    public bool MovementRestricted { get; init; }

    public bool ThresholdFlag { get; init; }

    public bool LocalStatusFlag { get; init; }

    public int CloneCount { get; init; }

    public static BuffEffectState None => default;
}