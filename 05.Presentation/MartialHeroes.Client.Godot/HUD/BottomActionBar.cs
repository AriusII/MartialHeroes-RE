using Godot;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Bottom action / command bar — the wide strip at the screen bottom.
///
/// PASSIVE: renders the recovered bar chrome at the spec-confirmed placement.
/// No game logic. Gesture intents (skill use, menu open) are emitted as use-case calls
/// by callers; this node only provides the chrome and slot containers.
///
/// Placement (§5.7 CONFIRMED-formula):
///   X = centerX(1024) = (screen_width − 1024) / 2
///   Y = screen_height − 60
///   W = 1024, H = 60
///   innerY = 957 (noted in spec, corresponds to the reference canvas inner line)
///
/// spec: Docs/RE/specs/ui_hud_layout.md §5.7 — "Bottom action/command bar:
///       centerX(1024), screen_height−60, 1024×60, innerY=957, Mixed (centred-X + bottom)"
/// </summary>
public sealed partial class BottomActionBar : Control
{
    // ── Placement constants — CONFIRMED-formula ────────────────────────────────────────────────
    // spec: Docs/RE/specs/ui_hud_layout.md §5.7

    /// <summary>Bar width. centreX(1024) formula → W=1024.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.</summary>
    private const float BarW = 1024f; // spec: Docs/RE/specs/ui_hud_layout.md §5.7

    /// <summary>Bar height. screen_height − 60 formula → H=60.
    /// spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.</summary>
    private const float BarH = 60f; // spec: Docs/RE/specs/ui_hud_layout.md §5.7

    // ── Initialisation ─────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        GD.Print("[BottomActionBar] _Ready start");
        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BottomActionBar] _Ready failed: {ex.Message}");
        }
    }

    private void BuildUi()
    {
        // Placement: centred-X + bottom-anchored.
        //   X = (screen_width − BarW) / 2   → AnchorLeft=0.5, OffsetLeft=−BarW/2, OffsetRight=+BarW/2
        //   Y = screen_height − BarH         → AnchorTop=AnchorBottom=1, OffsetTop=−BarH, OffsetBottom=0
        // spec: Docs/RE/specs/ui_hud_layout.md §5.7 — "centerX(1024), screen_height−60, 1024×60"
        //       Mixed (centred-X + bottom). CONFIRMED-formula.
        AnchorLeft = 0.5f;
        AnchorTop = 1f;
        AnchorRight = 0.5f;
        AnchorBottom = 1f;
        OffsetLeft = -(BarW / 2f); // = −512 → left half of bar   // spec: Docs/RE/specs/ui_hud_layout.md §5.7
        OffsetRight = BarW / 2f; // = +512 → right half of bar  // spec: Docs/RE/specs/ui_hud_layout.md §5.7
        OffsetTop = -BarH; // = −60 → screen_height − 60  // spec: Docs/RE/specs/ui_hud_layout.md §5.7
        OffsetBottom = 0f; // → flush to bottom viewport edge  // spec: Docs/RE/specs/ui_hud_layout.md §5.7
        MouseFilter = MouseFilterEnum.Ignore;

        // Bar background panel.
        var bg = new Panel { Name = "BarBackground" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.08f, 0.07f, 0.05f, 0.9f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.BorderColor = new Color(0.5f, 0.45f, 0.3f, 0.85f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Thin highlight line at top of bar (visual chrome).
        var topLine = new ColorRect
        {
            Name = "TopLine",
            Color = new Color(0.7f, 0.6f, 0.35f, 0.7f),
            Position = new Vector2(0f, 0f),
            Size = new Vector2(BarW, 2f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(topLine);

        // Inner content: HBoxContainer for action slot placeholders.
        // The real slot contents come from skill/item use-case binds (future cycle).
        var slotRow = new HBoxContainer
        {
            Name = "SlotRow",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        slotRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(slotRow);

        // Six action button slots (placeholder chrome boxes) — widths are visual placeholders.
        // The recovered spec notes the action bar contains the skill bar (§5.10 §3.5 container
        // origin 349,13) and surrounding buttons. We provide structural placeholders here;
        // the skill bar slots are owned by GameHud.BuildSkillBar.
        for (int i = 0; i < 6; i++)
        {
            var slotContainer = new Panel
            {
                Name = $"ActionSlot{i}",
                CustomMinimumSize = new Vector2(56f, 56f),
            };
            var slotStyle = new StyleBoxFlat();
            slotStyle.BgColor = new Color(0.12f, 0.11f, 0.08f, 0.8f);
            slotStyle.SetBorderWidthAll(1);
            slotStyle.BorderColor = new Color(0.45f, 0.4f, 0.25f, 0.7f);
            slotContainer.AddThemeStyleboxOverride("panel", slotStyle);
            slotRow.AddChild(slotContainer);
        }

        GD.Print($"[BottomActionBar] UI built. " +
                 $"centerX({BarW}), screen_height−{BarH}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.");
    }
}