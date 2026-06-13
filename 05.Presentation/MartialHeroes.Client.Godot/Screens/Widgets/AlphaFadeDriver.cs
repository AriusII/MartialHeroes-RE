// Screens/Widgets/AlphaFadeDriver.cs
//
// Thin Node that drives the alpha-fade animation on a parent Control, matching the legacy
// GUComponent show/hide alpha integrator described in the UI system spec.
//
// USAGE:
//   Attached as a child of a Control by WidgetFactory.BeginShow / BeginHide.
//   Callers should not create this class directly — use WidgetFactory instead.
//
// SPEC FACTS:
//   §7.1  The show/hide target byte (+0x8C) is the animation target; the current alpha (+0x04)
//         chases it ±64 per tick (±32 for GUComponentEx).  A hidden widget fades to alpha 0;
//         the parent's draw loop skips children whose target is 0.
//         We implement the GUComponent fast-path: ±64 per tick (normalised: ±64/255 per frame).
//
//   §7.1  The forced-alpha byte (+0x0F ≠ 0xFF) pins alpha immediately, bypassing the fade.
//         That is handled by WidgetFactory.ShowInstant / HideInstant which remove this driver
//         and set Modulate directly.
//
// ZERO-ALLOC:
//   _Process does no heap allocations — only float arithmetic and one Color struct assignment.
//
// spec: Docs/RE/specs/ui_system.md §7.1 (alpha-fade show/hide ±64/tick).

using Godot;

namespace MartialHeroes.Client.Godot.Screens.Widgets;

/// <summary>
/// Internal fade-animation driver attached as a child node by
/// <see cref="WidgetFactory.BeginShow"/> / <see cref="WidgetFactory.BeginHide"/>.
///
/// <para>On each <c>_Process</c> tick it advances the parent <see cref="Control"/>'s
/// <see cref="CanvasItem.Modulate"/> alpha by ±<see cref="FadeStep"/>/255 until it reaches
/// the target (0 = fully hidden, 1 = fully visible).  When the target is 0 and alpha reaches 0,
/// the parent's <see cref="Control.Visible"/> is set to <c>false</c> so it no longer receives
/// input or consumes hit-test resources.
/// spec: Docs/RE/specs/ui_system.md §7.1.</para>
///
/// <para>Do <b>not</b> instantiate this class directly.  Use
/// <see cref="WidgetFactory.BeginShow"/> and <see cref="WidgetFactory.BeginHide"/>.</para>
/// </summary>
internal sealed partial class AlphaFadeDriver : Node
{
    /// <summary>
    /// Alpha step per <c>_Process</c> tick, in normalised [0,1] units.
    /// Corresponds to 64 out of 255 alpha levels per tick.
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "±64 per tick toward [0,255]; GUComponent fast path".
    /// </summary>
    private const float FadeStep = 64f / 255f; // spec: Docs/RE/specs/ui_system.md §7.1

    private float _target = 1f;
    private float _current = 1f;

    /// <summary>Sets the fade target: 1 = fade in, 0 = fade out.</summary>
    internal void SetTarget(float target)
    {
        _target = Mathf.Clamp(target, 0f, 1f);
        // Sync current from the parent's actual modulate so we don't jump on the first tick.
        if (GetParent() is CanvasItem parent)
            _current = parent.Modulate.A;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (GetParent() is not CanvasItem parentItem) return;

        // Chase the target at ±FadeStep per tick.
        // spec: Docs/RE/specs/ui_system.md §7.1 — "alpha chases ±64 per tick".
        if (_current < _target)
            _current = Mathf.Min(_current + FadeStep, _target);
        else if (_current > _target)
            _current = Mathf.Max(_current - FadeStep, _target);

        // Apply: preserve RGB, update alpha only (zero-alloc Color struct).
        Color c = parentItem.Modulate;
        c.A = _current;
        parentItem.Modulate = c;

        // When hidden and fully faded out: set Visible=false so the control releases
        // input focus and hit-test resources.  Match legacy: "the parent draw loop skips
        // children whose target is 0 (or alpha is 0)".
        // spec: Docs/RE/specs/ui_system.md §7.1.
        if (_target < 0.01f && _current < 0.01f && parentItem is Control ctrl)
        {
            ctrl.Visible = false;
        }
    }
}