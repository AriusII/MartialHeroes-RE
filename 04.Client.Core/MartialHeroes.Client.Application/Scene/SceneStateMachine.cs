using MartialHeroes.Client.Application.Events;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.State;

namespace MartialHeroes.Client.Application.Scene;

/// <summary>
/// The faithful port of the legacy client's master scene machine — the application-layer model of
/// the application entry point's bounds-checked <c>switch</c> over the engine-state field. It owns
/// the live <see cref="GameState"/> and applies the complete transition table; the presentation
/// host listens for <see cref="SceneStateChangedEvent"/> and swaps the live scene to match.
/// </summary>
/// <remarks>
/// <para>
/// The original entry point <b>is</b> the state machine: an infinite dispatch loop over a single
/// integer, a <c>switch</c> with 8 cases (states 0..7) plus a <c>default</c> arm. A scene "finishes"
/// when something writes the next state and clears the run-flag, returning control to the dispatch
/// loop which re-dispatches the new state. This class models that commit mechanism: every accepted
/// transition updates <see cref="Current"/> and publishes one <see cref="SceneStateChangedEvent"/>;
/// rejected transitions are total no-ops (return <see langword="false"/>), keeping the machine
/// testable. spec: Docs/RE/specs/client_runtime.md §7.2 / §7.3.
/// </para>
/// <para>
/// This class carries no I/O, no timers, and no engine main loop — those live in the presentation
/// host (Godot) and the network reader. It is the pure, deterministic transition authority only.
/// </para>
/// </remarks>
public sealed class SceneStateMachine
{
    private readonly IClientEventBus _eventBus;

    /// <summary>The live engine-state struct — the sole source of truth for the active scene.</summary>
    public GameState Current { get; private set; }

    private bool _loadIsReload;

    /// <summary>
    /// The <c>OPENNING/SKIP</c> INI decision consulted once when the Load scene (state 2) advances:
    /// <see langword="true"/> → skip straight to Select (2 → 4); <see langword="false"/>/absent →
    /// run the Opening intro first (2 → 3). The host sets this from the INI before advancing Load.
    /// spec: Docs/RE/specs/client_runtime.md §7.3 ("OPENNING/SKIP" INI key), §7.5.1.
    /// </summary>
    public bool SkipOpening { get; set; }

    /// <summary>
    /// True only for the state-2 re-entry produced by character-management refreshes
    /// (<c>SmsgCharActionResult</c> 3/100 codes 202/203/232). Its <b>sole</b> remaining purpose is
    /// to signal the <see cref="LoadOrchestrator"/> to skip the case-1-only <c>msg.xdb</c> pre-load;
    /// it does <b>not</b> alter the post-load destination (the reload re-reads <c>OPENNING/SKIP</c>
    /// unconditionally, just like a fresh load).
    /// spec: Docs/RE/specs/resource_pipeline.md §2.5 (CAMPAIGN 16); client_runtime.md §7.10 item 2.
    /// </summary>
    public bool LoadIsReload => _loadIsReload;

    /// <summary>
    /// True after the terminal Quit/Error case has fallen through to the shared field-0 == 8 exit tail.
    /// This deliberately is not an <see cref="EngineSceneState"/> value; state 8 is the default/teardown
    /// tail, not a ninth scene. spec: Docs/RE/specs/client_runtime.md §7.3 / §7.5.1.
    /// </summary>
    public bool HasExited { get; private set; }

    /// <summary>Creates the machine at the constructor-default boot state (0 Init, sub 8).</summary>
    public SceneStateMachine(IClientEventBus eventBus, GameState? initial = null)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        Current = initial ?? GameState.Initial;
    }

    // ── §7.5.1 Engine-internal (case-body) transitions ──────────────────────────────────────────

    /// <summary>
    /// Advances out of the current scene the way each <c>switch</c> case body does once its engine
    /// main loop returns normally — the engine-internal next-state write. Drives the default scene
    /// spine: 0 → 1 → (2 → 3/4 by <see cref="SkipOpening"/>) → 4 → 5 → 4, and 6/7 → teardown.
    /// Returns <see langword="false"/> from a terminal/teardown state with nothing further to do.
    /// spec: Docs/RE/specs/client_runtime.md §7.5.1.
    /// </summary>
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

    /// <summary>
    /// Login pre-loop failure: window configuration failed → Error 7 / sub 1.
    /// spec: Docs/RE/specs/client_runtime.md §7.5.1.
    /// </summary>
    public bool OnLoginWindowConfigFailed() =>
        Current.State == EngineSceneState.Login && Commit(Current.ToError(1, 0));

    /// <summary>
    /// Login pre-loop failure: device / secondary init failed → Error 7 / sub 3.
    /// spec: Docs/RE/specs/client_runtime.md §7.5.1.
    /// </summary>
    public bool OnLoginDeviceInitFailed() =>
        Current.State == EngineSceneState.Login && Commit(Current.ToError(3, 0));

    // ── §7.5.2 Network-driven transitions ───────────────────────────────────────────────────────

    /// <summary>
    /// EnterGameAck (3/5) received: auth OK → begin Load. <b>State-agnostic</b> — the 3/5 handler
    /// forces engine state 2 unconditionally, regardless of the live scene; it is observed arriving
    /// during Login (hence the "1 → 2" framing), but the binary does not guard on the current state.
    /// <see cref="Commit"/> still no-ops if already on Load. spec: Docs/RE/specs/client_runtime.md
    /// §7.5.2 (EnterGameAck is state-agnostic, re-confirmed CAMPAIGN 16).
    /// </summary>
    public bool OnEnterGameAck() => Commit(Current.To(EngineSceneState.Load));

    /// <summary>
    /// CharacterList (3/1) received: (re)enter Select with a fresh roster (→ 4, sub 8). Accepted
    /// while on Load or Select. The Select → Select case is a genuine <b>re-entry</b> — the
    /// engine tears down and rebuilds the SelectWindow with the new roster — so it fires even though
    /// the engine-state value is unchanged (forced re-entry).
    /// spec: Docs/RE/specs/client_runtime.md §7.5.2 ("4/2 Select/Load … CharacterList → 4, sub 8").
    /// </summary>
    public bool OnCharacterListReceived()
    {
        if (Current.State is EngineSceneState.Load or EngineSceneState.Select)
        {
            return Commit(Current.To(EngineSceneState.Select, GameState.SubStateNone), forceReentry: true);
        }

        return false;
    }

    /// <summary>
    /// GameStateTick (4/1) arrived in-game: world-state pre-spawn <b>spawn-failure</b> fallback
    /// (5 → 4). Reached only when the local-player object is absent <em>and</em> the re-spawn from
    /// the descriptor returned null; on a successful spawn the handler builds the world and does not
    /// change state. spec: Docs/RE/specs/client_runtime.md §7.5.2 (4/1 = spawn-failure fallback).
    /// </summary>
    public bool OnGameStateTickNoLocalPlayer() =>
        Current.State == EngineSceneState.InGame && Commit(Current.To(EngineSceneState.Select));

    /// <summary>
    /// CharActionResult (<b>opcode 3/100</b>, <c>SmsgCharActionResult</c>) result code received —
    /// the table-driven engine-state transition handler. Applies the §7.5.2 mapping, which depends
    /// on the live scene and whether a local player exists. <b>NOT 3/7</b>: the separate
    /// <c>SmsgCharManageResult</c> (3/7) is a Character-Select UI result and writes no scene state.
    /// spec: Docs/RE/specs/client_runtime.md §7.5.2; Docs/RE/opcodes.md (3/7 vs 3/100).
    /// </summary>
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

    /// <summary>
    /// Connection/handshake error. During Load (state 2) this is a load-time disconnect (→ 7, sub 2);
    /// from any other live scene it is a generic disconnect (→ 7, sub 8).
    /// spec: Docs/RE/specs/client_runtime.md §7.5.2.
    /// </summary>
    public bool OnDisconnected()
    {
        if (IsTerminal)
        {
            return false;
        }

        int sub = Current.State == EngineSceneState.Load ? 2 : GameState.SubStateNone;
        return Commit(Current.ToError(sub, 0));
    }

    // ── §7.5.3 User-action transitions ──────────────────────────────────────────────────────────

    /// <summary>
    /// The case-4 SelectWindow post-tick state write: sets state 5 (In-game) as the engine-internal
    /// <b>no-network default</b> when the "enter world confirmed" flag is detected. In the <b>online</b>
    /// path the server's 3/5 EnterGameAck (<see cref="OnEnterGameAck"/>) immediately overwrites this
    /// with state 2 (Load), so the live enter-world path is <b>4 → 2 (via 3/5) → … → 5</b>, never a
    /// direct 4 → 5. This method is therefore <b>NOT</b> called by
    /// <see cref="ApplicationUseCases.SelectCharacterAsync"/> on the online path — the use-case only
    /// sends 1/9 and waits for 3/5. The 4 → 5 write here is exercised only by the engine-internal
    /// <see cref="AdvanceScene"/> arm (case 4 → 5) and the headless/no-network auto-walk.
    /// spec: Docs/RE/specs/client_runtime.md §7.5.3; §7.9.4 (post-tick flag → 5, then 3/5 overwrites
    /// with 2); §7.9.5 (happy path: 4 → 2 via 3/5, NOT direct 4 → 5). CAMPAIGN 16 correction.
    /// </summary>
    public bool OnSelectConfirmCharacter() =>
        Current.State == EngineSceneState.Select && Commit(Current.To(EngineSceneState.InGame, GameState.SubStateNone));

    /// <summary>
    /// Login network-machine fatal path. Failure type chooses either the quit path (6/8) or an error
    /// path (7/8 with the supplied detail); both are documented possibilities.
    /// spec: Docs/RE/specs/client_runtime.md §7.5.3.
    /// </summary>
    public bool OnLoginNetworkFatal(bool quitPath = true, int errorDetail = 0)
    {
        if (Current.State != EngineSceneState.Login)
        {
            return false;
        }

        return quitPath
            ? Commit(Current.To(EngineSceneState.Quit, GameState.SubStateNone))
            : Commit(Current.ToError(GameState.SubStateNone, errorDetail));
    }

    /// <summary>
    /// The scene-aware quit router: a window-close / user-quit request reads the live scene and
    /// routes to Quit (6). spec: Docs/RE/specs/client_runtime.md §7.5.3.
    /// </summary>
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

    // ── Terminal model ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see langword="true"/> when the live scene is a terminal teardown state (Quit or Error),
    /// which converge on the shared exit tail (field 0 → 8) rather than advancing further.
    /// spec: Docs/RE/specs/client_runtime.md §7.3 (shared exit tail).
    /// </summary>
    public bool IsTerminal => Current.State is EngineSceneState.Quit or EngineSceneState.Error;

    /// <summary>
    /// Handles the Load (state 2) case-body advance. A reload re-enters the <b>identical</b> case-2
    /// body and re-reads <c>OPENNING/SKIP</c> unconditionally — there is no reload-specific
    /// "forces Select" rule. The reload marker is consumed here (cleared) but affects only whether
    /// the <see cref="LoadOrchestrator"/> skips the <c>msg.xdb</c> pre-load; it never changes the
    /// post-load destination. spec: Docs/RE/specs/resource_pipeline.md §2.5; client_runtime.md §7.5.2.
    /// </summary>
    private bool AdvanceLoadScene()
    {
        _loadIsReload = false; // consume the flag — its only job was to inform the LoadOrchestrator
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