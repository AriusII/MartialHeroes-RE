namespace MartialHeroes.Shared.Kernel.Enums;

/// <summary>
///     The legacy client's master scene enumeration — the single integer the application entry-point's
///     <c>while(1) switch(engineState)</c> dispatches on. This is field 0 of the engine-state struct
///     (<see cref="MartialHeroes.Shared.Kernel.State.GameState" />).
/// </summary>
/// <remarks>
///     <para>
///         The switch has exactly <b>8 arms — cases 0..7 — plus a <c>default</c></b>. There is
///         <b>no <c>case 8:</c></b>. The value <see cref="Exit" /> (8) is the
///         <em>
///             terminal loop-exit
///             sentinel
///         </em>
///         : states 6 and 7 (and <c>default</c>) all write field-0 to 8 before falling to the
///         shared exit tail, which tears down the engine and returns from <c>WinMain</c>.
///         spec: Docs/RE/specs/client_runtime.md §7.3 (8 cases 0..7; no case 8; value 8 = exit tail).
///     </para>
///     <para>
///         Each case is a "build + run" block: it writes the <em>next</em> engine-state value, constructs
///         that scene's window object, enters the shared per-frame loop (<c>Engine_MainLoop</c>), and tears
///         down the window on loop-exit. The outer <c>while(1)</c> then re-dispatches on whatever state
///         was written last.
///         spec: Docs/RE/specs/client_runtime.md §7.3, §7.9.
///     </para>
/// </remarks>
public enum EngineSceneState
{
    /// <summary>
    ///     State 0 — Initialisation. One-time startup: sizes the window from display config, stores
    ///     <c>16</c> in an engine constant, then immediately falls into the state-1 (Login) body.
    ///     Always transitions to <see cref="Login" />.
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 0), §7.5.1 (0 → 1).
    /// </summary>
    Init = 0,

    /// <summary>
    ///     State 1 — Login. Constructs <c>LoginWindow</c> (~1368 B); loads <c>msg.xdb</c>; builds
    ///     15 Hangul font slots. On successful login the case body writes <c>= 2</c> (Load); the
    ///     network EnterGameAck (opcode 3/5) also drives 1 → 2. Fatal failure → <see cref="Error" />.
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 1), §7.5.1 / §7.5.2.
    /// </summary>
    Login = 1,

    /// <summary>
    ///     State 2 — Load. Constructs the <c>LoadHandler</c> (async loader thread, loading SFX).
    ///     Reads the <c>OPENNING\SKIP</c> INI key: <c>true</c> → write <c>= 4</c> (skip to Select);
    ///     otherwise <c>= 3</c> (play the Opening intro first).
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 2), §7.5.1 (2 → 4 / 2 → 3).
    /// </summary>
    Load = 2,

    /// <summary>
    ///     State 3 — Opening. Constructs <c>COpeningWindow</c> (~720 B); plays the intro sequence.
    ///     After the intro the case body writes <c>= 4</c> (Select).
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 3), §7.5.1 (3 → 4).
    /// </summary>
    Opening = 3,

    /// <summary>
    ///     State 4 — Character Select. Constructs <c>SelectWindow</c> (~6280 B): character-select UI,
    ///     preview actor, and the Select preview camera. Writes <c>= 5</c> (InGame) on enter-world
    ///     confirmation; writes <c>= 6</c> (Quit) on explicit quit.
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 4), §7.5.1 (4 → 5 / 4 → 6).
    /// </summary>
    Select = 4,

    /// <summary>
    ///     State 5 — In-Game. Constructs <c>MainHandler</c> (~200 B) and calls
    ///     <c>BuildGameWorld</c> (camera rig, scene graph, services, HUD); enables networking. The
    ///     default loop-return transition writes <c>= 4</c> (back to Select).
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 5), §7.5.1 (5 → 4).
    /// </summary>
    InGame = 5,

    /// <summary>
    ///     State 6 — Quit. Calls the engine shutdown routine, then writes field-0 to
    ///     <see cref="Exit" /> (8) and falls to the shared exit tail.
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 6), §7.5.1 (6 → 8).
    /// </summary>
    Quit = 6,

    /// <summary>
    ///     State 7 — Error. Builds an error string from the sub-state / error-detail; hides the
    ///     window; drops the network connection; writes <c>error.log</c>; shows a modal dialog; then
    ///     writes field-0 to <see cref="Exit" /> (8) and falls to the shared exit tail.
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (state 7), §7.5.1 (7 → 8).
    /// </summary>
    Error = 7,

    /// <summary>
    ///     Value 8 — Loop-exit sentinel. This is <b>NOT a constructed scene</b> and there is
    ///     <b>no <c>case 8:</c></b> in the original switch. States 6, 7, and the <c>default</c> arm
    ///     all write field-0 to this value and fall to the shared exit tail, which performs the final
    ///     engine teardown (scheduler release, crash-logger close, OS resource cleanup) and returns
    ///     from <c>WinMain</c>. In the sub-state field this same value (8) is the constructor default
    ///     ("no specific sub-state"); see <see cref="MartialHeroes.Shared.Kernel.State.GameState.SubStateNone" />.
    ///     spec: Docs/RE/specs/client_runtime.md §7.3 (value 8 = exit tail, not a 9th case), §7.1.
    /// </summary>
    Exit = 8
}