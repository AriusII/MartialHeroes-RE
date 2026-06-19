using MartialHeroes.Client.Application.Events;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Application.Scene;

// spec: Docs/RE/specs/client_runtime.md §7 — the faithful 8-case (0..7) master scene machine.
public sealed class SceneStateMachine
{
    private readonly IClientEventBus _eventBus;

    /// <summary>The live engine-state struct — the sole source of truth for the active scene.</summary>
    public GameState Current { get; private set; }

    private bool _loadIsReload;

    // spec: Docs/RE/specs/client_runtime.md §7.3 — OPENNING/SKIP gates Load: true → 2→4, false → 2→3.
    public bool SkipOpening { get; set; }

    // spec: resource_pipeline.md §2.5 — reload marker; only tells LoadOrchestrator to skip the msg.xdb pre-load.
    public bool LoadIsReload => _loadIsReload;

    // spec: Docs/RE/specs/client_runtime.md §7.3 — true after Quit/Error fell through the field-0 == 8 exit tail.
    public bool HasExited { get; private set; }

    /// <summary>Creates the machine at the constructor-default boot state (0 Init, sub 8).</summary>
    public SceneStateMachine(IClientEventBus eventBus, GameState? initial = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Current = initial ?? GameState.Initial;
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.1 — case-body next-state write: 0→1→(2→3/4)→4→5→4, 6/7→teardown.
    public bool AdvanceScene() => Current.State switch
    {
        EngineSceneState.Init => Commit(Current.To(EngineSceneState.Login)), // 0 → 1
        EngineSceneState.Login => Commit(Current.To(EngineSceneState.Load)), // 1 → 2
        EngineSceneState.Load => AdvanceLoadScene(),
        EngineSceneState.Opening => Commit(Current.To(EngineSceneState.Select)), // 3 → 4
        EngineSceneState.Select => Commit(Current.To(EngineSceneState.InGame)), // 4 → 5
        EngineSceneState.InGame => Commit(Current.To(EngineSceneState.Select)), // 5 → 4 (default return)
        EngineSceneState.Quit or EngineSceneState.Error => ExitTail(),
        _ => false,
    };

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — EnterGameAck (3/5) forces Load (state 2), state-agnostic.
    public bool OnEnterGameAck() => Commit(Current.To(EngineSceneState.Load));

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — CharacterList (3/1) on Load/Select forces a Select re-entry (sub 8).
    public bool OnCharacterListReceived()
    {
        if (Current.State is EngineSceneState.Load or EngineSceneState.Select)
        {
            return Commit(Current.To(EngineSceneState.Select, GameState.SubStateNone), forceReentry: true);
        }

        return false;
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — 4/1 with no local player materializable = spawn-failure fallback 5→4.
    public bool OnGameStateTickNoLocalPlayer() =>
        Current.State == EngineSceneState.InGame && Commit(Current.To(EngineSceneState.Select));

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — 3/100 SmsgCharActionResult result-code table (NOT 3/7).
    public bool OnCharActionResult(int result, bool hasLocalPlayer)
    {
        if (Current.State == EngineSceneState.Select && !hasLocalPlayer)
        {
            return result switch
            {
                0 => Commit(Current.To(EngineSceneState.Quit, GameState.SubStateNone)), // → 6/8
                1 or 2 or 3 or 4 or 7 => Commit(Current.ToError(5, result)), // → 7/5
                202 or 203 or 232 => CommitReloadLoad(), // → 2 reload
                _ => Commit(Current.ToError(GameState.SubStateNone, result)), // → 7/8
            };
        }

        if (Current.State == EngineSceneState.InGame && hasLocalPlayer)
        {
            return result == 0
                ? Commit(Current.To(EngineSceneState.Quit, GameState.SubStateNone)) // → 6/8
                : Commit(Current.ToError(GameState.SubStateNone, result)); // → 7/8
        }

        return false;
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.2 — disconnect: load-time (state 2) → 7/2, otherwise → 7/8.
    public bool OnDisconnected()
    {
        if (IsTerminal)
        {
            return false;
        }

        int sub = Current.State == EngineSceneState.Load ? 2 : GameState.SubStateNone;
        return Commit(Current.ToError(sub, 0));
    }

    // spec: Docs/RE/specs/client_runtime.md §7.5.3 — scene-aware quit router → Quit (6).
    public bool RequestQuit()
    {
        if (IsTerminal || HasExited)
        {
            return false;
        }

        return Current.State switch
        {
            EngineSceneState.Login or EngineSceneState.Load => Commit(Current.To(EngineSceneState.Quit, 2)),
            EngineSceneState.Select or EngineSceneState.InGame => Commit(Current.To(EngineSceneState.Quit,
                GameState.SubStateNone)),
            _ => false,
        };
    }

    // spec: Docs/RE/specs/client_runtime.md §7.3 — Quit/Error converge on the shared exit tail (field-0 → 8).
    public bool IsTerminal => Current.State is EngineSceneState.Quit or EngineSceneState.Error;

    // spec: resource_pipeline.md §2.5 — Load re-reads OPENNING/SKIP unconditionally; reload marker only feeds LoadOrchestrator.
    private bool AdvanceLoadScene()
    {
        _loadIsReload = false;
        return Commit(Current.To(SkipOpening ? EngineSceneState.Select : EngineSceneState.Opening));
    }

    private bool CommitReloadLoad()
    {
        _loadIsReload = true;
        return Commit(Current.To(EngineSceneState.Load));
    }

    private bool ExitTail()
    {
        if (HasExited)
        {
            return false;
        }

        HasExited = true;
        return true;
    }

    private bool Commit(GameState next, bool forceReentry = false)
    {
        GameState previous = Current;
        if (previous == next && !forceReentry)
        {
            return false;
        }

        Current = next;
        if (next.State is not (EngineSceneState.Quit or EngineSceneState.Error))
        {
            HasExited = false;
        }

        _eventBus.Publish(new SceneStateChangedEvent(previous, next));
        return true;
    }
}