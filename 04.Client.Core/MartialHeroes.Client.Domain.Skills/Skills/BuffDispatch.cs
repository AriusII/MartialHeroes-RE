namespace MartialHeroes.Client.Domain.Skills.Skills;

public static class BuffDispatch
{
    public const int EnterStanceId = 43;

    public const int StealthId = 44;

    public const int LocalStatusFlagId = 45;

    public const int TransformBId = 46;

    public const int MovementRestrictId = 47;

    public const int DispelId = 48;

    public const int CloneSummonId = 57;

    public const int ThresholdFlagId = 64;

    public const int TransformRevertId = 131;

    public const int StancePoseEnter = 11;

    public const int StancePoseTransformB = 12;

    public const int StancePoseRevert = 13;

    public const int StanceDefault = 1;

    public const int ThresholdMagnitudeLimit = 100;

    public static bool IsPeriodicVisual(int effectCode)
    {
        return effectCode == MovementRestrictId;
    }

    public static int MotionStateFor(int effectCode)
    {
        return effectCode switch
        {
            EnterStanceId => StancePoseEnter,
            TransformBId => StancePoseTransformB,
            TransformRevertId => StancePoseRevert,
            _ => BuffEffectState.NoMotionState
        };
    }
}