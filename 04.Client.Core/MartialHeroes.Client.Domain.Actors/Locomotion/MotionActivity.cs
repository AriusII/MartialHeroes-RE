using MartialHeroes.Client.Domain.Actors.Actors;

namespace MartialHeroes.Client.Domain.Actors.Locomotion;

public static class MotionActivity
{
    public const int Idle = 1;

    public const int Walk = 2;

    public const int Run = 3;

    public const int Motion = 4;

    public const int Special = 17;

    public static bool IsMotionActive(int stateCode)
    {
        return stateCode == Walk || stateCode == Run || stateCode == Motion || stateCode == Special;
    }

    public static LifecycleState ResolveLocomotion(bool dead, byte runFlag)
    {
        if (dead) return LifecycleState.Dead;

        return runFlag == 1 ? LifecycleState.Running : LifecycleState.Walking;
    }
}
