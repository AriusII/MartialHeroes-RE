// Ui/Widgets/HudWidget.cs
//
// Base HUD widget — the Godot reimplementation of GUComponent behaviour.
//
// Faithful to spec §1–7:
//   - Action id (field +0x10), src-rect atlas draw, AABB hit-test.
//   - Show/hide via alpha-fade integrator ±64/tick (±32 for the Ex variant).
//   - Child management in insertion order (paint back→front); reverse hit-test for z-order input.
//   - Deferred-removal mark: call MarkForRemoval(); the parent sweeps on the next RemoveMarked pass.
//
// PASSIVE: zero game logic. Reads atlas textures from HudAtlasLibrary (via TextureRect child),
// fires ActionFired events on click, manages its own child vector. Domain state never touched.
//
// NOTE: HudWidget does NOT derive from Godot.Control; each concrete subclass does (so that
//       Godot's own layout/hit-test is available). This base class is a behaviour mixin —
//       concrete widgets call the methods below from their Godot overrides.
//
// spec: Docs/RE/structs/gucomponent.md — field offsets (informational only — we model BEHAVIOR).
// spec: Docs/RE/specs/ui_system.md §1   — widget class hierarchy.
// spec: Docs/RE/specs/ui_system.md §3   — render path (back→front, child-vector insertion order).
// spec: Docs/RE/specs/ui_system.md §4   — hit-test / capture / drag.
// spec: Docs/RE/specs/ui_system.md §7   — show/hide lifecycle and z-order.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
/// GUComponent-faithful base widget for the new HUD substrate.
///
/// <para>Concrete widgets (HudButton, HudLabel, etc.) extend this class. Each must call
/// <see cref="InitBase"/> after construction and override
/// <see cref="GetControl"/> to return the backing <see cref="Control"/> node.</para>
///
/// <para>Paint order = child insertion order (back→front).
/// Hit-test order = reverse (topmost painter wins input).
/// spec: Docs/RE/specs/ui_system.md §7.3 — z-order rules.</para>
/// </summary>
public abstract class HudWidget
{
    // -------------------------------------------------------------------------
    // Action id
    // spec: Docs/RE/structs/gucomponent.md +0x10 — action_id (default -1).
    // spec: Docs/RE/specs/ui_system.md §1.2 — "actionId at +0x10; routes to OnAction on click-release".
    // -------------------------------------------------------------------------

    /// <summary>
    /// Integer action identifier fired to <see cref="ActionFired"/> on click-release.
    /// Default = -1 (no action, matches legacy sentinel).
    ///
    /// spec: Docs/RE/structs/gucomponent.md +0x10 action_id default -1.
    /// </summary>
    public int ActionId { get; set; } = -1;

    /// <summary>
    /// Fired when the widget is clicked (pressed and released inside its bounds).
    /// Subscribers should emit a use-case call — NEVER mutate domain state directly.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.4 — "click fires actionId on click-release only".
    /// </summary>
    public event Action<int>? ActionFired;

    // -------------------------------------------------------------------------
    // Remove-mark (deferred removal)
    // spec: Docs/RE/structs/gucomponent.md +0x8D remove_mark.
    // spec: Docs/RE/specs/ui_system.md §7.4 — RemoveMarkedChildren sweeps +0x8D==1 children.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Whether this widget is marked for removal on the next <see cref="RemoveMarked"/> pass.
    ///
    /// spec: Docs/RE/structs/gucomponent.md +0x8D remove_mark.
    /// </summary>
    public bool IsMarkedForRemoval { get; private set; }

    /// <summary>Marks this widget for deferred removal on the next RemoveMarked pass.</summary>
    public void MarkForRemoval() => IsMarkedForRemoval = true;

    // -------------------------------------------------------------------------
    // Show / hide
    // spec: Docs/RE/specs/ui_system.md §7.1 — alpha-fade show/hide.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Begins a fade-in (alpha toward 1.0 at ±64/tick).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "showing → alpha +64 per tick toward 255".
    /// </summary>
    public void Show()
    {
        Control? ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).Show();
    }

    /// <summary>
    /// Begins a fade-out (alpha toward 0 at −64/tick; Visible=false when done).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "hiding → alpha −64 toward 0".
    /// </summary>
    public void Hide()
    {
        Control? ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).Hide();
    }

    /// <summary>
    /// Snaps alpha to 1.0 immediately and sets Visible=true (SetShown=show / forced-alpha pin).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "SetShown snaps to 255 immediately".
    /// spec: Docs/RE/specs/ui_system.md §7.2 — "forced-alpha byte ≠ 0xFF pins alpha immediately".
    /// </summary>
    public void ShowInstant()
    {
        Control? ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).ShowInstant();
    }

    /// <summary>
    /// Snaps alpha to 0.0 immediately and sets Visible=false (SetShown=hide / forced-alpha pin).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "SetShown snaps to 0 immediately".
    /// </summary>
    public void HideInstant()
    {
        Control? ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).HideInstant();
    }

    /// <summary>
    /// Switches this widget to the slow ±32/tick fade (GUComponentEx variant).
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.1 — "GUComponentEx uses ±32/tick".
    /// </summary>
    public void UseSlowFade()
    {
        Control? ctrl = GetControl();
        if (ctrl is null) return;
        AlphaFade.For(ctrl).UseSlowFade();
    }

    // -------------------------------------------------------------------------
    // Child management (insertion-order list — back→front paint, reverse hit-test)
    // spec: Docs/RE/specs/ui_system.md §7.3 — z-order rules.
    // spec: Docs/RE/specs/ui_system.md §7.4 — child management.
    // -------------------------------------------------------------------------

    private readonly List<HudWidget> _children = [];

    /// <summary>
    /// Adds a child widget. Added children paint later (on top) and have lower hit-test
    /// priority than earlier children.
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.3 — "paint order = insertion order; later = on top".
    /// spec: Docs/RE/specs/ui_system.md §7.4 — Panel_AddChild.
    /// </summary>
    public void AddChild(HudWidget child)
    {
        _children.Add(child);
        Control? parentCtrl = GetControl();
        Control? childCtrl  = child.GetControl();
        if (parentCtrl is not null && childCtrl is not null)
            parentCtrl.AddChild(childCtrl);
    }

    /// <summary>
    /// Adds a child widget and sets its <see cref="ActionId"/>.
    ///
    /// spec: Docs/RE/specs/ui_system.md §7.4 — Panel_AddChildWithAction(parent, child, actionId).
    /// </summary>
    public void AddChildWithAction(HudWidget child, int actionId)
    {
        child.ActionId = actionId;
        AddChild(child);
    }

    /// <summary>
    /// Sweeps all children whose <see cref="IsMarkedForRemoval"/> is true, removing them from
    /// the child vector and freeing their backing <see cref="Control"/> node.
    ///
    /// spec: Docs/RE/specs/ui_system.md §2 slot 13 — RemoveMarkedChildren.
    /// spec: Docs/RE/specs/ui_system.md §7.4 — "deferred-removal sweep on remove-flag +0x8D==1".
    /// </summary>
    public void RemoveMarked()
    {
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (!_children[i].IsMarkedForRemoval) continue;
            HudWidget removed = _children[i];
            _children.RemoveAt(i);
            removed.GetControl()?.QueueFree();
        }
    }

    /// <summary>Read-only view of the current child list (insertion order).</summary>
    public IReadOnlyList<HudWidget> Children => _children;

    // -------------------------------------------------------------------------
    // Action dispatch helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires the <see cref="ActionFired"/> event with this widget's <see cref="ActionId"/>.
    /// Call this from the backing Control's pressed/clicked signal handler.
    ///
    /// spec: Docs/RE/specs/ui_system.md §1.4 — "window command handler routes actionId to OnAction".
    /// </summary>
    protected void FireAction()
    {
        if (ActionId >= 0)
            ActionFired?.Invoke(ActionId);
    }

    // -------------------------------------------------------------------------
    // Abstract members
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the backing Godot <see cref="Control"/> node for this widget.
    /// The control is the visual representation; its position, size, and Modulate
    /// reflect this widget's state.
    /// </summary>
    public abstract Control? GetControl();
}
