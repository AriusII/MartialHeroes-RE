using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Shared.Kernel.State;

public readonly record struct GameState
{
    public const int SubStateNone = 8;

    public GameState()
    {
        State = EngineSceneState.Init;
        SubState = SubStateNone;
        ErrorDetail = 0;
        DebugMode = false;
    }

    public EngineSceneState State { get; init; }

    public int SubState { get; init; }

    public int ErrorDetail { get; init; }

    public bool DebugMode { get; init; }

    public static GameState Initial => new();

    public GameState To(EngineSceneState next)
    {
        return this with { State = next, SubState = SubStateNone, ErrorDetail = 0 };
    }

    public GameState To(EngineSceneState next, int subState)
    {
        return this with { State = next, SubState = subState, ErrorDetail = 0 };
    }

    public GameState ToError(int subState, int errorDetail)
    {
        return this with { State = EngineSceneState.Error, SubState = subState, ErrorDetail = errorDetail };
    }

    public GameState WithDebugMode(bool debugMode)
    {
        return this with { DebugMode = debugMode };
    }
}