namespace MartialHeroes.Client.Application.World;

public static class WorldSceneContract
{
    public const int Case5BuildStepCount = 17;

    public const int FrameLoopPhaseCount = 4;

    public const int ViewPlatformCount = 5;

    public const int LayerNodeCount = 5;

    public const float StreamRadiusFarPlaneClamp = 1000f;

    public static ReadOnlySpan<int> LayerNodeMessageIds =>
        [2006, 2004, 2005, 2148, 2148];

    public static float ClampStreamRadius(float candidateRadius)
    {
        if (candidateRadius < 0f) return 0f;
        return candidateRadius > StreamRadiusFarPlaneClamp
            ? StreamRadiusFarPlaneClamp
            : candidateRadius;
    }
}