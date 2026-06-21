// Ui/Hud/HudHelpOverlay.cs
//
// In-game HelpPanel — full-screen help overlay (CODE-CONFIRMED).
//
// IMPORTANT: This is NOT a `+0x238` service-slot panel and not a separate panel class.
// It is a lazily-built FULL-SCREEN IMAGE OVERLAY that lives as a direct member of the
// root in-game HUD window ("MainMaster" MainWindow).
//   spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED.
//
// The overlay draws a single image `data/ui/help.dds` stretched to the FULL SCREEN at
// (0, 0) with opaque white tint. There is NO navigation, NO prev/next, NO topic list,
// NO msg.xdb feed — the whole help/manual content is the picture itself.
//   spec: Docs/RE/specs/ui_system.md §8.24.2 CODE-CONFIRMED — "single literal image data/ui/help.dds".
//   spec: Docs/RE/specs/ui_system.md §8.24.1 CODE-CONFIRMED — "(0,0) full-screen, opaque white".
//
// Toggle: key `h` (0x68) — confirmed on the keyboard hotkey dispatcher.
//   "Space" trigger is REFUTED in static code.
//   DefaultMenu action 4011 also calls the docked help button (slot 176); that button and this
//   overlay stay in sync.
//   spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED.
//
// z-order: HIGH — drawn above all other HUD panels.
//   spec: Docs/RE/specs/ui_system.md §8.24.1 — "z-order field: high (drawn above the HUD)".
//
// Graceful-null: if data/ui/help.dds is absent, GD.PrintErr + show a fallback colored rect.
//
// Member layout:
//   The spec describes help texture wrapper + help image component + docked help button as
//   DIRECT DWORD MEMBERS of the root HUD window, NOT service-slot entries. In our Godot port
//   we model this as a Control child of HudMaster (equivalent to a direct member).
//   spec: Docs/RE/specs/ui_system.md §8.24 — "plain members, not entries in the +0x238 slot table".
//
// PASSIVE: zero game logic. Toggle calls fire from HudMaster._Input on key `h` and from
// HudCommandBar action 4011.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game full-screen help overlay (HelpPanel member of MainMaster).
///     <para>
///         Draws a single full-screen image from <c>data/ui/help.dds</c>. No navigation,
///         no message feed — the picture IS the help content. Toggled by key <c>h</c>.
///     </para>
///     <para>PASSIVE: zero game logic. Toggle calls from HudMaster._Input and HudCommandBar action 4011.</para>
///     spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudHelpOverlay : Control
{
    // VFS path — direct literal bind, not a uitex registry id.
    // spec: Docs/RE/specs/ui_system.md §8.24.2 CODE-CONFIRMED — "literal path data/ui/help.dds"
    private const string HelpDdsVfsPath = "data/ui/help.dds"; // spec: §8.24.2
    private TextureRect? _helpImage;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _visible;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the full-screen help overlay.
    ///     spec: Docs/RE/specs/ui_system.md §8.24.1 CODE-CONFIRMED — "(0,0) full-screen, opaque white".
    ///     spec: Docs/RE/specs/ui_system.md §8.24.2 CODE-CONFIRMED — "single literal image data/ui/help.dds".
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudHelpOverlay";

        // Full-screen overlay, TOP z-order.
        // spec: §8.24.1 — "dst (0,0), full-screen, opaque white, z-order HIGH (above HUD)"
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // Blocks all input while visible

        Visible = false;
        _visible = false;

        // High z-index so it renders above all HUD panels.
        // spec: §8.24.1 — "z-order field: high (drawn above the HUD)"
        ZIndex = 100; // spec: §8.24.1

        // ── Help image — data/ui/help.dds ──
        // spec: §8.24.2 — "single literal image data/ui/help.dds; NOT a uitex registry id"
        // Graceful-null: if the DDS is absent, fall back to a colored rect with a notice.
        _helpImage = new TextureRect
        {
            Name = "HelpImage",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _helpImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Try to load help.dds via the atlas library or a direct Godot resource path.
        // The atlas library resolves VFS paths to Godot ImageTextures; if absent it returns null.
        // spec: §8.24.2 — "bound by direct path, NOT a uitex integer id"
        var helpTex = atlas?.GetByPath(HelpDdsVfsPath); // spec: §8.24.2 — literal "data/ui/help.dds"
        if (helpTex != null)
        {
            _helpImage.Texture = helpTex;
        }
        else
        {
            // Graceful-null: no help.dds in VFS — show fallback colored rect.
            GD.PrintErr("[HudHelpOverlay] data/ui/help.dds not found in VFS — showing fallback. " +
                        "spec: Docs/RE/specs/ui_system.md §8.24.2 CODE-CONFIRMED.");
            var fallback = new ColorRect
            {
                Name = "HelpFallback",
                Color = new Color(0.05f, 0.05f, 0.10f, 0.95f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(fallback);

            var fallbackLbl = new Label
            {
                Name = "HelpFallbackLabel",
                Text = "[ data/ui/help.dds — VFS asset absent ]",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore
            };
            fallbackLbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(fallbackLbl);
        }

        AddChild(_helpImage);

        // Close hint label (no page nav — single page, press h to close).
        // spec: §8.24.2 — "Page count = 1. Navigation = none."
        // spec: §8.24.4 — "No code-driven caption / title / topic label"
        var closeHint = new Label
        {
            Name = "CloseHint",
            Text = "[H] 닫기", // press H to close
            Position = new Vector2(10f, 10f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(closeHint);

        GD.Print("[HudHelpOverlay] Built — full-screen help overlay (HelpPanel member §8.24). " +
                 "Image: data/ui/help.dds (literal path, not uitex id). " +
                 "Toggle: key h (§8.17.1) / DefaultMenu action 4011 (§8.23.3). " +
                 "Page count = 1, no navigation (§8.24.2). " +
                 "spec: Docs/RE/specs/ui_system.md §8.24 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the help overlay. Called by key 'h' and DefaultMenu action 4011.
    ///     spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED —
    ///     "Toggle = key h: pressing h while hidden shows it; pressing while visible hides it."
    ///     Space trigger REFUTED in static code.
    /// </summary>
    public void Toggle()
    {
        _visible = !_visible;
        Visible = _visible;
        GD.Print($"[HudHelpOverlay] Toggle → {(_visible ? "SHOWN" : "HIDDEN")}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.24.3 CODE-CONFIRMED.");
    }

    /// <summary>
    ///     Forces the overlay to a specific visible state (for ESC / forced-hide on logout).
    ///     spec: Docs/RE/specs/ui_system.md §8.24.3 — "Forced hide when leaving the world".
    /// </summary>
    public void Toggle(bool show)
    {
        _visible = show;
        Visible = show;
    }

    public override void _Input(InputEvent @event)
    {
        // Block all input while the help overlay is visible.
        // ESC dismisses it (§8.24.5 MED: "whether Esc dismisses — MED"; we apply it as consistent UX).
        if (!_visible) return;
        if (@event is InputEventKey { Pressed: true })
        {
            // Any key dismisses the overlay (consistent with the single-page design).
            // spec: §8.24.3 — "pressing h while visible hides it"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}