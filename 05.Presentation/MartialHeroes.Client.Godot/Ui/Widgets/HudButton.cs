// Ui/Widgets/HudButton.cs
//
// HUD button widget — faithful reimplementation of GUButton behaviour.
//
// 3-state frame machine (NORMAL / HOVER / PRESSED), with DISABLED = NORMAL.
// Caption text colour by interaction state:
//   Disabled → grey (0xFF666666), Hovered → yellow (0xFFFFFF00),
//   Normal/Pressed → per-widget tint (+0x0C, default white).
// Caption font slot defaults to 0 (DotumChe 12/6/wt0) — the zero-initialised default.
// ActionFired fires on click-release (pressed and released inside bounds).
//
// Frame-selection precedence (spec §1.5): disabled > pressed > hovered > normal.
//
// spec: Docs/RE/specs/ui_system.md §1.5 — GUButton frame-state machine: CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §1.5 — "DISABLED always equals NORMAL from ctor": CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §1.5 — "caption colour: disabled=grey, hover=yellow": CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §6.3 — "button caption font slot at +0xE8, default 0": CODE-CONFIRMED.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
///     GUButton-faithful 3-state HUD button.
///     <para>
///         Holds up to three sprite frames (NORMAL/HOVER/PRESSED). When a frame is null,
///         the NORMAL frame is used (2-state / 3-state fallback). DISABLED always uses NORMAL.
///         spec: Docs/RE/specs/ui_system.md §1.5.
///     </para>
///     <para>
///         Caption (CP949-decoded string) is drawn over the sprite with state-dependent
///         colour. Font slot defaults to 0 (DotumChe 12/6 — spec §6.2 / §6.3).
///     </para>
/// </summary>
public sealed class HudButton : HudWidget
{
    // -------------------------------------------------------------------------
    // Spec-cited colour constants
    // -------------------------------------------------------------------------

    // Disabled caption colour.
    // spec: Docs/RE/specs/ui_system.md §1.5 — "disabled → grey 0xFF666666": CODE-CONFIRMED.
    private static readonly Color DisabledCaptionColor = new(0x66 / 255f, 0x66 / 255f, 0x66 / 255f);

    // Hovered caption colour.
    // spec: Docs/RE/specs/ui_system.md §1.5 — "hovered → yellow 0xFFFFFF00": CODE-CONFIRMED.
    private static readonly Color HoveredCaptionColor = new(1f, 1f, 0f);

    // -------------------------------------------------------------------------
    // Backing Godot node
    // -------------------------------------------------------------------------

    private readonly TextureButton _btn;
    private readonly Label _captionLabel;

    // Per-widget caption tint for the normal/pressed state.
    // Stored as a field so IsDisabled can restore it without requiring a MouseExited signal.
    // spec: Docs/RE/specs/ui_system.md §1.5 — "+0x0C tint/colour, default 0xFFFFFFFF = white".
    private readonly Color _captionTint;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates a HudButton.
    /// </summary>
    /// <param name="x">Screen-local X on the 1024×768 canvas.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="normalFrame">NORMAL frame (and DISABLED). Must not be null.</param>
    /// <param name="hoverFrame">HOVER frame; null = use NORMAL.</param>
    /// <param name="pressedFrame">PRESSED frame; null = use NORMAL.</param>
    /// <param name="caption">CP949-decoded caption string (may be empty).</param>
    /// <param name="captionTint">
    ///     Per-widget tint for normal/pressed caption (default white).
    ///     spec §1.5 — "+0x0C tint/colour".
    /// </param>
    /// <param name="captionFontSlot">
    ///     Font slot index (0..14, default 0 = DotumChe 12).
    ///     spec §6.3 — "button caption font at +0xE8, default 0": CODE-CONFIRMED.
    /// </param>
    public HudButton(
        int x, int y, int w, int h,
        Texture2D? normalFrame,
        Texture2D? hoverFrame = null,
        Texture2D? pressedFrame = null,
        string caption = "",
        Color? captionTint = null,
        int captionFontSlot = 0) // spec: §6.3 button caption font slot default 0: CODE-CONFIRMED
    {
        // Build the backing TextureButton.
        _btn = new TextureButton
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            IgnoreTextureSize = true,
            // Each atlas frame is sliced to the destination W×H before assignment; keep native size.
            // spec: Docs/RE/specs/frontend_scenes.md — widget blits are 1:1 pixel rects, never scaled.
            StretchMode = TextureButton.StretchModeEnum.Keep
        };

        // Tag the TextureButton so AudioService can identify HUD buttons via NodeAdded without a
        // direct type-check (HudButton is a non-Node wrapper). AudioService hooks Pressed → PlayUiClick
        // on any TextureButton carrying this meta.
        // spec: Docs/RE/specs/sound.md — UI click SFX 861010101 on button presses.
        _btn.SetMeta("is_hud_button", Variant.From(true));

        // Assign frames.
        // spec: §1.5 — HOVER / PRESSED default to NORMAL when no distinct frame provided.
        _btn.TextureNormal = normalFrame;
        _btn.TextureHover = hoverFrame ?? normalFrame;
        _btn.TexturePressed = pressedFrame ?? normalFrame;
        _btn.TextureDisabled = normalFrame; // spec: §1.5 — DISABLED always equals NORMAL.
        _btn.TextureFocused = normalFrame;

        // Caption label.
        _captionLabel = new Label
        {
            Text = caption,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        _captionTint = captionTint ?? Colors.White;
        _captionLabel.AddThemeColorOverride("font_color", _captionTint);

        // Apply font slot (default 0 = DotumChe 12/6).
        // spec: §6.3 — "button caption font slot at +0xE8, default 0": CODE-CONFIRMED.
        HudFont.ApplyToLabel(_captionLabel, captionFontSlot);

        _btn.AddChild(_captionLabel);

        // Hover signals → caption colour updates.
        _btn.MouseEntered += () =>
        {
            // spec: §1.5 — "hovered → yellow 0xFFFFFF00".
            _captionLabel.AddThemeColorOverride("font_color", HoveredCaptionColor);
        };
        _btn.MouseExited += () =>
        {
            // Restore tint or disabled colour.
            // spec: §1.5 — "caption colour = grey when disabled, widget tint otherwise".
            _captionLabel.AddThemeColorOverride("font_color",
                _btn.Disabled ? DisabledCaptionColor : _captionTint);
        };

        // Click-release signal → ActionFired.
        // spec: §1.4 — "click fires actionId on click-release only".
        _btn.Pressed += FireAction;
    }

    // -------------------------------------------------------------------------
    // Public properties
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Gets or sets the caption text (CP949-decoded, already a .NET string).
    ///     Never decode bytes in the UI layer.
    /// </summary>
    public string Caption
    {
        get => _captionLabel.Text;
        set => _captionLabel.Text = value;
    }

    /// <summary>
    ///     Gets or sets the disabled state.
    ///     <para>
    ///         When disabled: the button uses the NORMAL frame (spec §1.5 — "DISABLED = NORMAL")
    ///         and the caption draws in grey (0xFF666666).
    ///     </para>
    ///     spec: Docs/RE/specs/ui_system.md §1.5 — frame-selection precedence + disabled caption colour.
    /// </summary>
    public bool IsDisabled
    {
        get => _btn.Disabled;
        set
        {
            _btn.Disabled = value;
            // spec: Docs/RE/specs/ui_system.md §1.5 — "disabled → grey 0xFF666666; normal/pressed → widget tint".
            // Restore the per-widget caption tint on re-enable so the caption does not stay grey
            // after a disable→enable cycle without an intervening MouseExited signal.
            _captionLabel.AddThemeColorOverride("font_color",
                value ? DisabledCaptionColor : _captionTint);
        }
    }

    // -------------------------------------------------------------------------
    // HudWidget
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public override Control? GetControl()
    {
        return _btn;
    }
}