using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Application.Contracts.Scene;

public sealed class SceneStateMachine
{
    private readonly IClientEventBus _eventBus;

    public SceneStateMachine(IClientEventBus eventBus, GameState? initial = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Current = initial ?? GameState.Initial;
    }

    public GameState Current { get; private set; }

    public bool SkipOpening { get; set; }

    public bool LoadIsReload { get; private set; }

    public bool EnteringWorld { get; private set; }

    public bool HasExited { get; private set; }

    public bool IsTerminal => Current.State is EngineSceneState.Quit or EngineSceneState.Error;

    public bool AdvanceScene()
    {
        return Current.State switch
        {
            EngineSceneState.Init => Commit(Current.To(EngineSceneState.Login)),
            EngineSceneState.Login => Commit(Current.To(EngineSceneState.Load)),
            EngineSceneState.Load => AdvanceLoadScene(),
            EngineSceneState.Opening => Commit(Current.To(EngineSceneState.Select)),
            EngineSceneState.Select => Commit(Current.To(EngineSceneState.InGame)),
            EngineSceneState.InGame => Commit(Current.To(EngineSceneState.Select)),
            EngineSceneState.Quit or EngineSceneState.Error => ExitTail(),
            _ => false
        };
    }

    public bool OnEnterGameAck(bool enterRequestPending)
    {
        if (enterRequestPending) EnteringWorld = true;
        return Commit(Current.To(EngineSceneState.Load));
    }

    public bool OnCharacterListReceived()
    {
        if (Current.State is EngineSceneState.Load or EngineSceneState.Select)
        {
            EnteringWorld = false;
            return Commit(Current.To(EngineSceneState.Select, GameState.SubStateNone), true);
        }

        return false;
    }

    public bool OnGameStateTickNoLocalPlayer()
    {
        return Current.State == EngineSceneState.InGame && Commit(Current.To(EngineSceneState.Select));
    }

    public bool OnWorldEntryConfirmed(bool enterRequestPending)
    {
        if (!enterRequestPending) return false;

        EnteringWorld = false;
        if (Current.State is EngineSceneState.Select or EngineSceneState.Load)
            return Commit(Current.To(EngineSceneState.InGame));

        return false;
    }

    public bool OnCharActionResult(int result, bool hasLocalPlayer)
    {
        if (Current.State == EngineSceneState.Load && EnteringWorld)
        {
            EnteringWorld = false;
            return Commit(Current.To(EngineSceneState.Select, GameState.SubStateNone), true);
        }

        if (Current.State == EngineSceneState.Select && !hasLocalPlayer)
            return result switch
            {
                0 => Commit(Current.To(EngineSceneState.Quit, GameState.SubStateNone)),
                1 or 2 or 3 or 4 or 5 or 7 or 22 or 23 => Commit(Current.ToError(5, result)),
                202 or 203 or 232 => CommitReloadLoad(),
                10 or 11 or 16 or 200 or 201 => false,
                >= 204 and <= 211 => false,
                >= 220 and <= 227 => false,
                >= 212 and <= 219 => false,
                >= 228 and <= 231 => false,
                _ => Commit(Current.ToError(GameState.SubStateNone, result))
            };

        if (hasLocalPlayer)
            return result == 0
                ? Commit(Current.To(EngineSceneState.Quit, GameState.SubStateNone))
                : Commit(Current.ToError(GameState.SubStateNone, result));

        return false;
    }

    public bool OnDisconnected()
    {
        if (IsTerminal) return false;

        var sub = Current.State == EngineSceneState.Load ? 2 : GameState.SubStateNone;
        return Commit(Current.ToError(sub, 0));
    }

    public bool RequestQuit()
    {
        if (IsTerminal || HasExited) return false;

        return Current.State switch
        {
            EngineSceneState.Login or EngineSceneState.Load => Commit(Current.To(EngineSceneState.Quit, 2)),
            EngineSceneState.Select or EngineSceneState.InGame => Commit(Current.To(EngineSceneState.Quit,
                GameState.SubStateNone)),
            _ => false
        };
    }

    private bool AdvanceLoadScene()
    {
        LoadIsReload = false;

        if (EnteringWorld)
        {
            EnteringWorld = false;
            return Commit(Current.To(EngineSceneState.InGame));
        }

        return Commit(Current.To(SkipOpening ? EngineSceneState.Select : EngineSceneState.Opening));
    }

    private bool CommitReloadLoad()
    {
        LoadIsReload = true;
        return Commit(Current.To(EngineSceneState.Load));
    }

    private bool ExitTail()
    {
        if (HasExited) return false;

        HasExited = true;
        return true;
    }

    private bool Commit(GameState next, bool forceReentry = false)
    {
        var previous = Current;
        if (previous == next && !forceReentry) return false;

        Current = next;
        if (next.State is EngineSceneState.Quit or EngineSceneState.Error)
            EnteringWorld = false;
        else
            HasExited = false;

        _eventBus.Publish(new SceneStateChangedEvent(previous, next));
        return true;
    }
}