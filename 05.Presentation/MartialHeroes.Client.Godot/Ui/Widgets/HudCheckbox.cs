// Ui/Widgets/HudCheckbox.cs
//
// HUD checkbox widget — reimplementation of GUCheckBox.
//
// GUCheckBox derives from GUButton (spec §1):
//   "checked state = the PRESSED frame; chains the 3-state button constructor".
// It is a TEXTURED toggle button — the unchecked/checked visuals are the NORMAL / PRESSED atlas
// sprites at the exact widget rect (e.g. the login Save-ID box is 13×13). We back it with a Godot
// TextureButton in toggle mode (NOT a CheckButton — a CheckButton draws Godot's own ~44×24 switch
// graphic, which overrides the requested size and is not the original look).
// ActionFired is fired on every toggle with the checkbox's ActionId.
//
// spec: Docs/RE/specs/ui_system.md §1 — GUCheckBox: "derives GUButton; checked = PRESSED frame".
// spec: Docs/RE/specs/ui_system.md §1.5 — "3-state ctor: (sX,sY), PRESSED=(a9,a10), HOVER=(a11,a12)".

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
///     GUCheckBox-faithful toggle widget backed by a Godot <see cref="TextureButton" /> in toggle mode.
///     <para>
///         The NORMAL frame is the unchecked sprite; the PRESSED frame is the checked sprite, drawn at
///         the exact widget rect. spec: Docs/RE/specs/ui_system.md §1 — "GUCheckBox checked = PRESSED frame".
///     </para>
///     <para>
///         Subscribe to <see cref="Toggled" /> to react to state changes and emit a use-case
///         call. Never mutate domain state in a UI handler.
///     </para>
/// </summary>
public sealed class HudCheckbox : HudWidget
{
    private readonly TextureButton _check;

    /// <param name="x">Screen-local X.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="normalFrame">Unchecked sprite (NORMAL frame).</param>
    /// <param name="pressedFrame">Checked sprite (PRESSED frame); null = use NORMAL.</param>
    public HudCheckbox(int x, int y, int w, int h,
        Texture2D? normalFrame = null,
        Texture2D? pressedFrame = null)
    {
        var checkedTex = pressedFrame ?? normalFrame;

        _check = new TextureButton
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            ToggleMode = true,
            IgnoreTextureSize = true, // respect the 13×13 (etc.) rect, not the texture's own size.
            StretchMode = TextureButton.StretchModeEnum.Scale,
            // Unchecked = NORMAL frame; Checked = PRESSED frame (drawn via TexturePressed in toggle mode).
            // spec: §1 "checked = PRESSED frame"; §1.5 "DISABLED always equals NORMAL".
            TextureNormal = normalFrame,
            TexturePressed = checkedTex,
            TextureHover = normalFrame,
            TextureDisabled = normalFrame
        };

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
    ///     Fired when the checkbox state changes. Subscribers must emit a use-case call.
    ///     Never mutate domain state in the UI layer.
    /// </summary>
    public event Action<bool>? Toggled;

    /// <inheritdoc />
    public override Control? GetControl()
    {
        return _check;
    }
}