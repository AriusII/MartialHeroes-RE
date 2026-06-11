using MartialHeroes.Client.Application.Input;

namespace MartialHeroes.Client.Godot.Input;

/// <summary>
/// UI-side <see cref="IInputHandler"/> for the HUD. Registered FIRST in the
/// <see cref="InputBus"/> so it consumes pointer events that land on a UI panel before the
/// world handler ever sees them.
///
/// For this wave the HUD is built in code (no native Godot UI hit-testing bridge). The handler
/// conservatively marks every MouseButtonDown as NOT consumed (returns false) unless the
/// hit-test tells us a panel was under the cursor. This preserves the "UI is the gate" contract
/// while the full UI hit-test bridge is a future concern.
///
/// spec: Docs/RE/specs/input_ui.md §3 — "UI hit-test always before world interaction": CONFIRMED.
/// spec: Docs/RE/specs/input_ui.md §6 ("UI is the gate" contract).
/// </summary>
public sealed class HudInputHandler : IInputHandler
{
    // Delegate to a Godot-side hit-test function that returns true when the cursor is over
    // a UI panel. Provided at construction from the HUD/GameLoop composition root.
    // This is a plain Func<int,int,bool> so this class stays engine-free.
    private readonly Func<int, int, bool>? _hitTest;

    /// <summary>
    /// Creates a handler whose UI hit-test is provided by <paramref name="hitTest"/>.
    /// Pass <see langword="null"/> for a pass-through handler that never consumes any event.
    /// </summary>
    public HudInputHandler(Func<int, int, bool>? hitTest = null)
    {
        _hitTest = hitTest;
    }

    /// <summary>
    /// Returns <see langword="true"/> (consume) when the event is a mouse button press AND the
    /// HUD hit-test reports a panel is under the cursor.
    /// All other event types pass through unconsumed.
    /// spec: Docs/RE/specs/input_ui.md §3 — "(a) UI hit-test — walk the widget tree; if a widget
    ///       is hit, the UI consumes the click and world interaction is skipped."
    /// </summary>
    public bool TryHandle(in InputEvent e)
    {
        if (_hitTest is null)
        {
            return false;
        }

        // Only consume pointer press events — mouse-up, move, wheel, and keys pass through.
        // spec: Docs/RE/specs/input_ui.md §3 — "on press: UI hit-test ... if a widget is hit,
        //       the UI consumes the click."
        if (e.Type != InputType.MouseButtonDown)
        {
            return false;
        }

        return _hitTest(e.X, e.Y);
    }
}