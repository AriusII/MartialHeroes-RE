using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Shared.Kernel.State;

/// <summary>
/// The legacy client's engine-state struct — the sole source of truth for which scene is active.
/// A faithful model of the 3-contiguous-integer + 1-byte record the application entry-point's
/// <c>switch</c> dispatches on.
/// </summary>
/// <remarks>
/// <para>
/// Field layout (derived from the binary's struct layout):
/// <list type="table">
///   <item>
///     <term>field 0 (+0x00 i32)</term>
///     <description><see cref="State"/> — the engine scene value 0..7 the switch dispatches on;
///     value 8 (<see cref="EngineSceneState.Exit"/>) is the terminal sentinel, not a case.</description>
///   </item>
///   <item>
///     <term>field 1 (+0x04 i32)</term>
///     <description><see cref="SubState"/> — sub-state or error code; constructor default is
///     <see cref="SubStateNone"/> (8). Reused for exit-tail keying: states 6/7 set sub to 8
///     before converging on the shared exit tail.</description>
///   </item>
///   <item>
///     <term>field 2 (+0x08 i32)</term>
///     <description><see cref="ErrorDetail"/> — the offending result code when
///     <see cref="State"/> is <see cref="EngineSceneState.Error"/>.</description>
///   </item>
///   <item>
///     <term>+0x0C u8</term>
///     <description><see cref="DebugMode"/> — set once at startup from the <c>game.lua</c>
///     <c>debugmode</c> global; gates developer overlays; never drives scene transitions.</description>
///   </item>
/// </list>
/// spec: Docs/RE/specs/client_runtime.md §7.1 (engine-state struct layout + constructor defaults).
/// </para>
/// <para>
/// The constructor initialises to <c>{ Init, SubState = 8, ErrorDetail = 0, DebugMode = false }</c>.
/// spec: Docs/RE/specs/client_runtime.md §7.1 (sub-state default = 8), §7.3 (8 is not a switch case).
/// </para>
/// <para>
/// This is a pure value record: it carries no I/O and no transition policy. The commit mechanism
/// (write next state → clear run-flag → re-dispatch) lives in the application scene machine,
/// not here.
/// spec: Docs/RE/specs/client_runtime.md §7.2.
/// </para>
/// </remarks>
public readonly record struct GameState
{
    /// <summary>
    /// The "no specific sub-state" sentinel and the binary's constructor default for field 1.
    /// The same value (8) is written to the sub-state field by states 6/7 before they converge
    /// on the shared exit tail; it is also the value of <see cref="EngineSceneState.Exit"/> —
    /// one numeric constant doing double duty in the original engine.
    /// spec: Docs/RE/specs/client_runtime.md §7.1 (sub-state default = 8), §7.3 (exit tail keyed on sub 8).
    /// </summary>
    public const int SubStateNone = 8; // spec: Docs/RE/specs/client_runtime.md §7.1, §7.3

    /// <summary>The live engine scene (field 0). spec: Docs/RE/specs/client_runtime.md §7.1.</summary>
    public EngineSceneState State { get; init; }

    /// <summary>
    /// Sub-state / error code (field 1). Default is <see cref="SubStateNone"/> (8) — the binary's
    /// constructor default. spec: Docs/RE/specs/client_runtime.md §7.1.
    /// </summary>
    public int SubState { get; init; }

    /// <summary>
    /// The offending result code when <see cref="State"/> is <see cref="EngineSceneState.Error"/>
    /// (field 2). spec: Docs/RE/specs/client_runtime.md §7.1.
    /// </summary>
    public int ErrorDetail { get; init; }

    /// <summary>
    /// The developer-mode flag (+0x0C byte). Read once from <c>game.lua</c> <c>debugmode</c> at
    /// startup; gates developer overlays; never drives scene transitions.
    /// spec: Docs/RE/specs/client_runtime.md §0.1 (game.lua debugmode), §7.1.
    /// </summary>
    public bool DebugMode { get; init; }

    /// <summary>
    /// Creates the engine-state struct in its binary constructor-default form:
    /// <c>{ State = Init, SubState = 8, ErrorDetail = 0, DebugMode = false }</c>.
    /// spec: Docs/RE/specs/client_runtime.md §7.1.
    /// </summary>
    public GameState()
    {
        State = EngineSceneState.Init;
        SubState = SubStateNone; // spec: Docs/RE/specs/client_runtime.md §7.1 (sub-state default = 8)
        ErrorDetail = 0;
        DebugMode = false;
    }

    /// <summary>
    /// The constructor-default engine-state struct (boot state). Equivalent to <c>new GameState()</c>.
    /// spec: Docs/RE/specs/client_runtime.md §7.1.
    /// </summary>
    public static GameState Initial => new();

    /// <summary>
    /// Returns a copy advanced to <paramref name="next"/> with the sub-state reset to
    /// <see cref="SubStateNone"/> (8) and the error detail cleared — the common default-edge
    /// engine-internal transition (e.g. 0 → 1, 3 → 4, 4 → 5).
    /// spec: Docs/RE/specs/client_runtime.md §7.5.1.
    /// </summary>
    public GameState To(EngineSceneState next) =>
        this with { State = next, SubState = SubStateNone, ErrorDetail = 0 };

    /// <summary>
    /// Returns a copy advanced to <paramref name="next"/> carrying an explicit
    /// <paramref name="subState"/> (e.g. network-driven re-entry, error sub-codes).
    /// spec: Docs/RE/specs/client_runtime.md §7.5.2.
    /// </summary>
    public GameState To(EngineSceneState next, int subState) =>
        this with { State = next, SubState = subState, ErrorDetail = 0 };

    /// <summary>
    /// Returns a copy in the <see cref="EngineSceneState.Error"/> state (7) carrying the
    /// <paramref name="subState"/> and the offending <paramref name="errorDetail"/> result code.
    /// spec: Docs/RE/specs/client_runtime.md §7.3 (state 7), §7.5.2.
    /// </summary>
    public GameState ToError(int subState, int errorDetail) =>
        this with { State = EngineSceneState.Error, SubState = subState, ErrorDetail = errorDetail };

    /// <summary>
    /// Returns a copy with the <see cref="DebugMode"/> flag updated. All other fields are
    /// preserved. Use once at startup when the <c>game.lua</c> <c>debugmode</c> value is known.
    /// spec: Docs/RE/specs/client_runtime.md §0.1 (game.lua debugmode), §7.1.
    /// </summary>
    public GameState WithDebugMode(bool debugMode) =>
        this with { DebugMode = debugMode };
}