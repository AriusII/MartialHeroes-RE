// Ui/Widgets/HudCheckbox.cs
//
// HUD checkbox widget — reimplementation of GUCheckBox.
//
// GUCheckBox derives from GUButton (spec §1):
//   "checked state = the PRESSED frame; chains the 3-state button constructor".
// We back this with a Godot CheckButton (toggle); the checked/unchecked visuals come
// from atlas sprites (NORMAL = unchecked, PRESSED = checked).
// ActionFired is fired on every toggle with the checkbox's ActionId.
//
// spec: Docs/RE/specs/ui_system.md §1 — GUCheckBox: "derives GUButton; checked = PRESSED frame".
// spec: Docs/RE/specs/ui_system.md §1.5 — "3-state ctor: (sX,sY), PRESSED=(a9,a10), HOVER=(a11,a12)".

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
/// GUCheckBox-faithful toggle widget backed by a Godot <see cref="CheckButton"/>.
///
/// <para>The NORMAL frame is the unchecked sprite; the PRESSED frame is the checked sprite.
/// spec: Docs/RE/specs/ui_system.md §1 — "GUCheckBox checked = PRESSED frame".</para>
///
/// <para>Subscribe to <see cref="Toggled"/> to react to state changes and emit a use-case
/// call. Never mutate domain state in a UI handler.</para>
/// </summary>
public sealed class HudCheckbox : HudWidget
{
    private readonly CheckButton _check;

    /// <param name="x">Screen-local X.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="normalFrame">Unchecked sprite (NORMAL frame).</param>
    /// <param name="pressedFrame">Checked sprite (PRESSED frame); null = use NORMAL.</param>
    public HudCheckbox(int x, int y, int w, int h,
        Texture2D? normalFrame  = null,
        Texture2D? pressedFrame = null)
    {
        _check = new CheckButton
        {
            Position          = new Vector2(x, y),
            Size              = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            // Remove Godot's built-in check icon so the atlas sprite shows.
            Icon = null,
        };

        // Set atlas icons via theme overrides.
        // Normal = unchecked, Checked = pressed.
        // spec: §1.5 — "DISABLED always equals NORMAL": CONFIRMED.
        if (normalFrame is not null)
        {
            _check.AddThemeIconOverride("unchecked",          normalFrame);
            _check.AddThemeIconOverride("unchecked_disabled", normalFrame);
        }
        if (pressedFrame is not null || normalFrame is not null)
        {
            Texture2D checkedTex = pressedFrame ?? normalFrame!;
            _check.AddThemeIconOverride("checked",          checkedTex);
            _check.AddThemeIconOverride("checked_disabled", checkedTex);
        }

        _check.Toggled += pressed =>
        {
            FireAction();
            Toggled?.Invoke(pressed);
        };
    }

    /// <summary>Gets or sets the checked state.</summary>
    public bool IsChecked
    {
        get => _check.ButtonPressed;
        set => _check.ButtonPressed = value;
    }

    /// <summary>
    /// Fired when the checkbox state changes. Subscribers must emit a use-case call.
    /// Never mutate domain state in the UI layer.
    /// </summary>
    public event Action<bool>? Toggled;

    /// <inheritdoc/>
    public override Control? GetControl() => _check;
}
