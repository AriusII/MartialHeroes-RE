namespace MartialHeroes.Client.Application.Input;

/// <summary>
///     The neutral input-event kind. Maps the legacy normalised mouse-event type codes (and the keyboard
///     path) onto a strongly-typed enum. The numeric values mirror the recovered type-byte taxonomy
///     exactly: 3 = move, 4 = press, 5 = release, 6 = synthesised click, 7 = double-click, 8 = wheel.
///     The press / release / click triple are DISTINCT codes — the synthesised click (6) is what HUD
///     command/button handlers fire on (not raw press or release). spec: Docs/RE/specs/input_ui.md
///     §2a (full taxonomy) / §2b (the type-6 click synthesis) / §6 (".NET port may use a strongly-typed enum").
/// </summary>
public enum InputType : byte
{
    /// <summary>No event (default).</summary>
    None = 0,

    /// <summary>Mouse moved. Legacy type code 3. spec: input_ui.md §2a.</summary>
    MouseMove = 3,

    /// <summary>
    ///     A mouse button went down (button index in <see cref="InputEvent.ButtonOrDelta" />). Legacy type code 4
    ///     (press). spec: input_ui.md §2a.
    /// </summary>
    MouseButtonDown = 4,

    /// <summary>
    ///     A mouse button was released (button index in <see cref="InputEvent.ButtonOrDelta" />). Legacy type code 5
    ///     (release). spec: input_ui.md §2a.
    /// </summary>
    MouseButtonUp = 5,

    /// <summary>
    ///     A synthesised CLICK (activation) — re-pushed on a release that lands on the press-captured
    ///     widget (the click-vs-drag discriminator). HUD command/button handlers fire on THIS, not on
    ///     raw press or release. Legacy type code 6. spec: input_ui.md §2a / §2b / §6.
    /// </summary>
    MouseButtonClick = 6,

    /// <summary>A mouse button double-click. Legacy type code 7. spec: input_ui.md §2a (double-click).</summary>
    MouseDoubleClick = 7,

    /// <summary>
    ///     Mouse wheel scrolled (signed delta in <see cref="InputEvent.ButtonOrDelta" />). Legacy type code 8. spec:
    ///     input_ui.md §2a.
    /// </summary>
    MouseWheel = 8,

    /// <summary>A key went down (virtual-key in <see cref="InputEvent.ButtonOrDelta" />).</summary>
    KeyDown = 16,

    /// <summary>A key was released (virtual-key in <see cref="InputEvent.ButtonOrDelta" />).</summary>
    KeyUp = 17,

    /// <summary>A translated character was produced (codepoint in <see cref="InputEvent.ButtonOrDelta" />).</summary>
    KeyChar = 18
}

/// <summary>
///     Mouse button index encoding shared with the legacy normalised event.
///     spec: Docs/RE/specs/input_ui.md §1b ("1 = left, 2 = right, 3 = middle").
/// </summary>
public static class MouseButton
{
    /// <summary>Left button. spec: input_ui.md §1b.</summary>
    public const int Left = 1; // spec: Docs/RE/specs/input_ui.md §1b

    /// <summary>Right button. spec: input_ui.md §1b.</summary>
    public const int Right = 2; // spec: Docs/RE/specs/input_ui.md §1b

    /// <summary>Middle button. spec: input_ui.md §1b.</summary>
    public const int Middle = 3; // spec: Docs/RE/specs/input_ui.md §1b
}

/// <summary>
///     The neutral, managed input event. Reproduces the legacy 20-byte normalised mouse record
///     {type, x, y, button/delta, modifiers} as a managed value type. spec:
///     Docs/RE/specs/input_ui.md §2 ("5 × 4 bytes = 20 bytes total") and §6 (managed event type, the
///     4-byte stride is a wire/buffer detail we do not mirror here).
/// </summary>
/// <remarks>
///     Godot's input layer produces raw pointer/key events and feeds them in via
///     <see cref="InputBus.Dispatch" />; the bus then walks the UI → world chain of responsibility
///     (spec: input_ui.md §3 / §6, "UI is the gate"). The exact modifier-flag bit mapping is UNVERIFIED
///     in the spec (§2 / §7); callers pass whatever modifier bitmask their platform reports.
/// </remarks>
/// <param name="Type">The event kind. spec: input_ui.md §2 (type byte at +0).</param>
/// <param name="X">Cursor X in screen pixels. spec: input_ui.md §2 (+4).</param>
/// <param name="Y">Cursor Y in screen pixels. spec: input_ui.md §2 (+8).</param>
/// <param name="ButtonOrDelta">Button index, wheel delta, virtual-key, or codepoint. spec: input_ui.md §2 (+12).</param>
/// <param name="Modifiers">Modifier-flag bitmask (Shift/Ctrl/Alt; bit mapping UNVERIFIED). spec: input_ui.md §2 (+16).</param>
public readonly record struct InputEvent(
    InputType Type,
    int X,
    int Y,
    int ButtonOrDelta,
    int Modifiers)
{
    /// <summary>Convenience predicate: a left mouse-button press. spec: input_ui.md §3 (left-click chain).</summary>
    public bool IsLeftButtonDown =>
        Type == InputType.MouseButtonDown && ButtonOrDelta == MouseButton.Left;

    /// <summary>Convenience predicate: a left mouse-button release. spec: input_ui.md §2a (release) / §3 (on-release path).</summary>
    public bool IsLeftButtonUp =>
        Type == InputType.MouseButtonUp && ButtonOrDelta == MouseButton.Left;

    /// <summary>
    ///     Convenience predicate: a synthesised left-button click (the activation HUD buttons fire on).
    ///     spec: input_ui.md §2a / §2b ("every HUD command/button handler acts on type 6").
    /// </summary>
    public bool IsLeftButtonClick =>
        Type == InputType.MouseButtonClick && ButtonOrDelta == MouseButton.Left;
}