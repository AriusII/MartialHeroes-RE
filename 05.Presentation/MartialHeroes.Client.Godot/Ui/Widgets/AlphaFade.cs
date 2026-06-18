// Ui/Widgets/AlphaFade.cs
//
// Alpha-fade integrator for HUD widgets.
//
// Matches the legacy GUComponent show/hide alpha integrator (spec §7.1):
//   - GUComponent fast path: ±64/tick toward [0,255].
//   - GUComponentEx slow path: ±32/tick.
//   - SetShown snaps alpha to the endpoint immediately (the ±tick integrator then holds it).
//   - Forced-alpha override (ShowInstant/HideInstant): pins alpha immediately, no fade.
//
// The Godot mapping uses Modulate.A ([0..1] range). The tick step is ±64/255 (fast) or
// ±32/255 (slow) per _Process call.
//
// spec: Docs/RE/specs/ui_system.md §7.1 — "alpha chases ±64/tick; GUComponentEx ±32/tick".
// spec: Docs/RE/specs/ui_system.md §7.1 — "SetShown snaps alpha to endpoint (shows/hides instantly)".
// spec: Docs/RE/specs/ui_system.md §7.2 — "forced-alpha byte ≠ 0xFF pins alpha immediately".

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
/// GUComponent-faithful alpha-fade driver for HUD <see cref="Control"/> nodes.
///
/// <para>Attach one instance as a child of the control to be faded. Use
/// <see cref="Show"/>, <see cref="Hide"/>, <see cref="ShowInstant"/>, or
/// <see cref="HideInstant"/>. Do NOT instantiate directly; use
/// <see cref="AlphaFade.For"/> to get-or-add an instance.</para>
///
/// spec: Docs/RE/specs/ui_system.md §7.1 — alpha-fade lifecycle.
/// </summary>
internal sealed partial class AlphaFade : Node
{
    // ±64/tick (normalised) — GUComponent fast path.
    // spec: Docs/RE/specs/ui_system.md §7.1 — "±64 per tick toward [0,255]; GUComponent fast path".
    private const float StepFast = 64f / 255f; // spec: Docs/RE/specs/ui_system.md §7.1

    // ±32/tick (normalised) — GUComponentEx slow path.
    // spec: Docs/RE/specs/ui_system.md §7.1 — "GUComponentEx ±32 per tick".
    private const float StepSlow = 32f / 255f; // spec: Docs/RE/specs/ui_system.md §7.1

    private float _target = 1f;
    private float _current = 1f;
    private float _step = StepFast;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Begins a fade-in (alpha toward 1.0).
    ///
    /// <para>Sets Visible=true immediately so the control can receive input as alpha rises.
    /// Alpha chases 1.0 at <see cref="StepFast"/> per tick.</para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "showing → alpha climbs +64 per tick toward 255".
    /// </summary>
    internal void Show()
    {
        SyncCurrentFromParent();
        _target = 1f;
        if (GetParent() is Control ctrl) ctrl.Visible = true;
    }

    /// <summary>
    /// Begins a fade-out (alpha toward 0.0).
    ///
    /// <para>When alpha reaches 0 the parent's Visible is set to false, releasing input
    /// and hit-test resources.</para>
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "hiding → alpha falls −64 toward 0".
    /// </summary>
    internal void Hide()
    {
        SyncCurrentFromParent();
        _target = 0f;
    }

    /// <summary>
    /// Snaps alpha to 1.0 immediately (forced-alpha / SetShown=show equivalent).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "SetShown snaps alpha to 255 immediately".
    /// spec: Docs/RE/specs/ui_system.md §7.2 — "forced-alpha override pins alpha immediately".
    /// </summary>
    internal void ShowInstant()
    {
        _target = 1f;
        _current = 1f;
        ApplyAlpha(1f);
        if (GetParent() is Control ctrl) ctrl.Visible = true;
    }

    /// <summary>
    /// Snaps alpha to 0.0 immediately and hides the parent (forced-alpha / SetShown=hide equivalent).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "SetShown snaps alpha to 0 immediately".
    /// spec: Docs/RE/specs/ui_system.md §7.2 — "forced-alpha override pins alpha immediately".
    /// </summary>
    internal void HideInstant()
    {
        _target = 0f;
        _current = 0f;
        ApplyAlpha(0f);
        if (GetParent() is Control ctrl) ctrl.Visible = false;
    }

    /// <summary>
    /// Switches to the slow ±32/tick fade rate (GUComponentEx path).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "GUComponentEx variant uses ±32 per tick".
    /// </summary>
    internal void UseSlowFade() => _step = StepSlow;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Mathf.IsEqualApprox(_current, _target)) return;

        // Chase target.
        // spec: Docs/RE/specs/ui_system.md §7.1 — "±64/tick clamped [0,255]".
        if (_current < _target)
            _current = Mathf.Min(_current + _step, _target);
        else
            _current = Mathf.Max(_current - _step, _target);

        ApplyAlpha(_current);

        // When fully hidden, set Visible=false to release input/hit-test resources.
        // spec: Docs/RE/specs/ui_system.md §7.1 — "parent draw loop skips children whose target is 0".
        if (_target < 0.01f && _current < 0.01f && GetParent() is Control ctrl)
            ctrl.Visible = false;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    private static readonly StringName DriverNodeName = new("__HudAlphaFade");

    /// <summary>
    /// Returns the <see cref="AlphaFade"/> driver attached to <paramref name="control"/>,
    /// creating and adding it on the first call.
    /// </summary>
    internal static AlphaFade For(Control control)
    {
        Node? existing = control.FindChild(DriverNodeName, owned: false);
        if (existing is AlphaFade fade) return fade;

        var newFade = new AlphaFade { Name = DriverNodeName };
        control.AddChild(newFade);
        newFade._current = control.Modulate.A;
        return newFade;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SyncCurrentFromParent()
    {
        if (GetParent() is CanvasItem item)
            _current = item.Modulate.A;
    }

    private void ApplyAlpha(float alpha)
    {
        if (GetParent() is not CanvasItem item) return;
        Color c = item.Modulate;
        c.A = alpha;
        item.Modulate = c;
    }
}