namespace MartialHeroes.Client.Domain.Actors.Actors;

public enum MotionIntent
{
    Idle = 0,

    Walk = 1,

    Run = 2,

    Snap = 3
}

public static class MotionIntentMap
{
    public const byte SnapMotionCode = 5;

    public static MotionIntent Resolve(byte motionCode, byte runFlag)
    {
        return motionCode switch
        {
            SnapMotionCode => MotionIntent.Snap,

            _ => runFlag != 0 ? MotionIntent.Run : MotionIntent.Walk
        };
    }
}