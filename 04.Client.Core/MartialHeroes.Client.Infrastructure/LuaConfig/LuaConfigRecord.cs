namespace MartialHeroes.Client.Infrastructure.LuaConfig;

public sealed record LuaConfigRecord
{
    public int VfsMode { get; init; } = 1;

    public int Launcher { get; init; } = 1;

    public int DebugMode { get; init; } = 1;

    public int AddictionWarningTiming { get; init; } = 1;


    public int NewServerIndex { get; init; } = 1;


    public int DisplayGlowRangeX { get; init; } = 2;

    public int DisplayGlowRangeY { get; init; } = 2;

    public int ShowFpsCounter { get; init; }


    public float DisplayBaseBrightMulti { get; init; } = 1.0f;

    public float DisplayGlowBrightMulti { get; init; } = 1.0f;

    public float DisplayLightRatio { get; init; } = 1.0f;

    public int DisplayPower { get; init; } = 1;


    public string DisplayPowerShader { get; init; } = "data/shader/power1dx8.psh";
}