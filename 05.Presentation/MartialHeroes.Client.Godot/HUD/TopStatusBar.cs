using Godot;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Top full-width status bar — a 20px-tall strip spanning the full viewport width at Y=120.
///
/// PASSIVE: renders the recovered chrome strip. Text/state content is set by the caller via
/// <see cref="SetStatusText"/>. No game logic.
///
/// Placement (§5.4 CONFIRMED-formula):
///   X = 0, Y = 120, W = screen_width, H = 20
///
/// The right-edge square buttons §5.4 are included as child slots:
///   Button #1/2: X = screen_width − 200, Y = 0, W = 64, H = 64
///   Right-anchored panel: X = screen_width − 406, Y = 0, W = 406, H = 119
///
/// spec: Docs/RE/specs/ui_hud_layout.md §5.4 — "Top full-width status bar: 0,120,screen_width,20"
/// spec: Docs/RE/specs/ui_hud_layout.md §5.4 — "Right-edge square button #1/#2: screen_width−200, 0, 64, 64"
/// spec: Docs/RE/specs/ui_hud_layout.md §5.4 — "Right-anchored panel: screen_width−406, 0, 406, 119"
/// </summary>
public sealed partial class TopStatusBar : Control
{
    // ── Placement constants — CONFIRMED-formula ────────────────────────────────────────────────
    // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Y of the status bar strip. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    private const float BarY = 120f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Height of the status bar strip. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    private const float BarH = 20f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Offset from right edge for the corner buttons. = screen_width − 200.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    private const float CornerButtonOffsetFromRight = 200f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Corner button size. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    private const float CornerButtonSize = 64f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Right-anchored panel offset from right edge. = screen_width − 406.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    private const float RightPanelOffsetFromRight = 406f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Right-anchored panel width. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    private const float RightPanelW = 406f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    /// <summary>Right-anchored panel height. spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.</summary>
    private const float RightPanelH = 119f; // spec: Docs/RE/specs/ui_hud_layout.md §5.4

    // ── Child node handles ─────────────────────────────────────────────────────────────────────

    private Label _statusLabel = null!;

    // ── Initialisation ─────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        GD.Print("[TopStatusBar] _Ready start");
        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TopStatusBar] _Ready failed: {ex.Message}");
        }
    }

    private void BuildUi()
    {
        // Root: full-width at Y=120, H=20.
        // Anchor: AnchorLeft=0, AnchorRight=1 → full width; AnchorTop=AnchorBottom=0; OffsetTop=120.
        // spec: Docs/RE/specs/ui_hud_layout.md §5.4 — "X=0, Y=120, W=screen_width, H=20" CONFIRMED-formula.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = 0f; // X=0  // spec: Docs/RE/specs/ui_hud_layout.md §5.4
        OffsetTop = BarY; // Y=120 // spec: Docs/RE/specs/ui_hud_layout.md §5.4
        OffsetRight = 0f; // flush to right viewport edge (W=screen_width)  // spec: Docs/RE/specs/ui_hud_layout.md §5.4
        OffsetBottom = BarY + BarH; // Y+H=140  // spec: Docs/RE/specs/ui_hud_layout.md §5.4
        MouseFilter = MouseFilterEnum.Ignore;

        // Background strip.
        var bg = new Panel { Name = "StatusBarBg" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.8f);
        bgStyle.SetBorderWidthAll(0);
        bgStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f, 0.6f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Status text label — full width.
        _statusLabel = new Label
        {
            Name = "StatusLabel",
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _statusLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.8f, 1f));
        AddChild(_statusLabel);

        // ── Right-edge square buttons §5.4 ────────────────────────────────────────────────────
        // Placed relative to this bar's coordinate space (Y=0 = top of bar = world Y=120).
        // Their absolute Y=0 means they are above and aligned with the bar's top edge,
        // which places them at world Y=120 (same as the bar origin). They extend 64px up,
        // sitting above the bar at world Y 56–120. Here we anchor them at the top of the
        // HUD root instead (they overlap the bar; we add them as siblings in GameHud).
        // As children of this Control they sit at local Y=0:
        //   local Y=0 maps to world Y=120, which makes buttons at Y range 120–184 (wrong spec §5.4 says Y=0).
        // Because §5.4 says these buttons are at Y=0 on the reference canvas (independent of the bar),
        // they should be siblings of this bar in GameHud — not children. We only build the bar strip here.
        // The right-anchored panel and corner buttons are wired as separate widgets in GameHud.

        GD.Print($"[TopStatusBar] UI built. X=0, Y={BarY}, W=screen_width, H={BarH}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.");
    }

    // ── Public API ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the status text displayed in the full-width strip.
    /// Called from GameHud (main thread) on state changes.
    /// PASSIVE: direct label mutation, no game logic.
    /// </summary>
    public void SetStatusText(string text)
    {
        if (_statusLabel is not null)
            _statusLabel.Text = text;
    }
}