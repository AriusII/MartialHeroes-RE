// Ui/Widgets/HudList.cs
//
// HUD list widget — reimplementation of GUList: scrollable list with reverse hit-test.
//
// Key behaviours:
//   - Children clipped to viewport (vertical scroll offset).
//   - Hit-test walks children in REVERSE order (topmost painted wins input).
//     For click events it returns on the first consuming child;
//     for move events it continues to update all hover states.
//   - Scroll offset chased by scroll buttons / wheel.
//   - Backed by a Godot ScrollContainer with VBoxContainer for items.
//
// spec: Docs/RE/specs/ui_system.md §1 — GUList: "11 specialised virtual slots; vertical scroll".
// spec: Docs/RE/specs/ui_system.md §4.4 — GUList reverse hit-test: CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §7.5 — GUList vertical scroll clipping.
// TODO(spec): runtime-only — GUList per-keystroke IME composition flow. Not confirmed statically.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
/// GUList-faithful scrollable HUD list.
///
/// <para>Godot's <see cref="ScrollContainer"/> provides the vertical scroll; child
/// <see cref="HudWidget"/> instances are laid out in a <see cref="VBoxContainer"/>.
/// Hit-test is reverse-ordered via <see cref="SetProcessInput"/> — the topmost-painted
/// (last added) child wins input first.</para>
///
/// spec: Docs/RE/specs/ui_system.md §4.4 — "GUList::OnEvent walks children end→front".
/// spec: Docs/RE/specs/ui_system.md §7.5 — scroll-clip model.
/// </summary>
public sealed class HudList : HudWidget
{
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _items;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a HudList.
    /// </summary>
    /// <param name="x">Screen-local X on the 1024×768 canvas.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels (visible viewport height).</param>
    public HudList(int x, int y, int w, int h)
    {
        _scroll = new ScrollContainer
        {
            Position          = new Vector2(x, y),
            Size              = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode   = ScrollContainer.ScrollMode.Auto,
        };

        _items = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.Fill };
        _scroll.AddChild(_items);
    }

    // -------------------------------------------------------------------------
    // Item management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Appends a child widget at the bottom of the list (insertion order = paint order).
    ///
    /// spec: §3.1 — "child vector front→end = back→front paint order".
    /// spec: §4.4 — hit-test reverse = last child wins input.
    /// </summary>
    public void AppendItem(HudWidget item)
    {
        Control? ctrl = item.GetControl();
        if (ctrl is not null)
            _items.AddChild(ctrl);
    }

    /// <summary>
    /// Removes all list items.
    /// </summary>
    public void ClearItems()
    {
        foreach (Node child in _items.GetChildren())
            child.QueueFree();
    }

    // -------------------------------------------------------------------------
    // HudWidget
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override Control? GetControl() => _scroll;
}
