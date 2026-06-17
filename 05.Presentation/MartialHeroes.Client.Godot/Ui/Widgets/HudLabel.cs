// Ui/Widgets/HudLabel.cs
//
// HUD label widget — faithful reimplementation of GULabel behaviour.
//
// Fixed-advance CP949 text rendering:
//   The legacy D3DXFont draws with a fixed-advance grid:
//     bounding rect = {x, y, x + charWidth(slot)*strlen, y + rowHeight(slot)*scale}
//   We approximate this with a Godot Label whose font-size overrides match the spec
//   §6.2 row-height for the chosen font slot.
//
// Font slot defaults to 0 (DotumChe 12/6/wt0) — the zero-initialised label slot default.
//
// spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance grid rendering: CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md §6.2 — 15-slot font table: CODE-CONFIRMED.
// spec: Docs/RE/structs/gucomponent.md +0xE4 — GULabel font-slot field, default 0.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
/// GULabel-faithful fixed-advance HUD text label.
///
/// <para>CP949 strings from msg.xdb arrive already decoded as .NET strings from
/// <see cref="Assets.HudTextLibrary"/>. Pass them directly to <see cref="Text"/>.
/// Never decode bytes in the UI layer.</para>
///
/// spec: Docs/RE/specs/ui_system.md §6.3 — fixed-advance text rendering.
/// </summary>
public sealed class HudLabel : HudWidget
{
    private readonly Label _label;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a HudLabel.
    /// </summary>
    /// <param name="x">Screen-local X on the 1024×768 canvas.</param>
    /// <param name="y">Screen-local Y.</param>
    /// <param name="w">Width in pixels (determines label wrap / clip boundary).</param>
    /// <param name="h">Height in pixels.</param>
    /// <param name="text">Initial text (CP949-decoded .NET string from msg.xdb or empty).</param>
    /// <param name="color">Text colour (ARGB).</param>
    /// <param name="fontSlot">Font slot index 0..14 (default 0 = DotumChe 12/6/wt0).
    ///   spec §6.2 / §6.3 — GULabel slot at +0xE4, default 0: CODE-CONFIRMED.</param>
    /// <param name="multiline">True for multi-line autowrap labels.</param>
    public HudLabel(
        int x, int y, int w, int h,
        string text     = "",
        Color? color    = null,
        int fontSlot    = 0, // spec: §6.2/§6.3 GULabel font slot default 0: CODE-CONFIRMED
        bool multiline  = false)
    {
        _label = new Label
        {
            Text             = text,
            Position         = new Vector2(x, y),
            Size             = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            AutowrapMode     = multiline
                ? TextServer.AutowrapMode.WordSmart
                : TextServer.AutowrapMode.Off,
            ClipText         = !multiline,
            MouseFilter      = Control.MouseFilterEnum.Ignore,
        };

        _label.AddThemeColorOverride("font_color", color ?? Colors.White);

        // Apply font slot.
        // spec: §6.3 — GULabel font slot at +0xE4, default 0 = DotumChe 12/6: CODE-CONFIRMED.
        HudFont.ApplyToLabel(_label, fontSlot);
    }

    // -------------------------------------------------------------------------
    // Public properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets the label text. Pass already-decoded CP949 strings from HudTextLibrary.
    /// Never decode bytes in the UI layer.
    /// </summary>
    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>Gets or sets the text colour.</summary>
    public Color FontColor
    {
        get => _label.GetThemeColor("font_color");
        set => _label.AddThemeColorOverride("font_color", value);
    }

    // -------------------------------------------------------------------------
    // Fixed-advance width helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes the fixed-advance layout width for a string at the given font slot:
    /// <c>charWidth(slot) × text.Length</c>.
    ///
    /// spec: Docs/RE/specs/ui_system.md §6.3 — "bounding rect right = x + charWidth×strlen".
    /// </summary>
    public static int FixedAdvanceWidth(string text, int fontSlot)
        => HudFont.CharWidth(fontSlot) * text.Length;

    // -------------------------------------------------------------------------
    // HudWidget
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override Control? GetControl() => _label;
}
