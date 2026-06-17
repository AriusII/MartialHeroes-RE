// Ui/Widgets/HudScrollbar.cs
//
// HUD scrollbar widget — reimplementation of GUScroll: up/down buttons + thumb.
//
// The legacy GUScroll is a simple scrollbar with up-button, down-button, and a thumb.
// We wrap Godot's VScrollBar (which provides all three visually and functionally).
//
// Scroll value changes are surfaced via ValueChanged event; callers subscribe and
// emit a use-case call or scroll their list widget. Never mutate domain state.
//
// spec: Docs/RE/specs/ui_system.md §1 — GUScroll: "up/down button children + thumb".

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
/// GUScroll-faithful vertical scrollbar backed by Godot's <see cref="VScrollBar"/>.
///
/// spec: Docs/RE/specs/ui_system.md §1 — GUScroll: "simple scrollbar (up/down + thumb)".
/// </summary>
public sealed class HudScrollbar : HudWidget
{
    private readonly VScrollBar _bar;

    /// <param name="x">Screen-local X.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="minValue">Scroll range minimum.</param>
    /// <param name="maxValue">Scroll range maximum.</param>
    /// <param name="pageSize">Visible-page size (thumb scale).</param>
    public HudScrollbar(int x, int y, int w, int h,
        double minValue = 0, double maxValue = 100, double pageSize = 10)
    {
        _bar = new VScrollBar
        {
            Position          = new Vector2(x, y),
            Size              = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            MinValue          = minValue,
            MaxValue          = maxValue,
            Page              = pageSize,
            Step              = 1,
        };
    }

    /// <summary>Gets or sets the current scroll value.</summary>
    public double Value
    {
        get => _bar.Value;
        set => _bar.Value = value;
    }

    private event Action<double>? _valueChanged;
    private bool _eventWired;

    private void EnsureEventWired()
    {
        if (_eventWired) return;
        _eventWired = true;
        _bar.ValueChanged += v => _valueChanged?.Invoke(v);
    }

    /// <summary>
    /// Fired when the scroll value changes. Subscribers must emit a use-case call or
    /// adjust an associated HudList scroll offset. Never mutate domain state.
    /// </summary>
    public event Action<double>? ValueChanged
    {
        add    { EnsureEventWired(); _valueChanged += value; }
        remove { _valueChanged -= value; }
    }

    /// <inheritdoc/>
    public override Control? GetControl() => _bar;
}
