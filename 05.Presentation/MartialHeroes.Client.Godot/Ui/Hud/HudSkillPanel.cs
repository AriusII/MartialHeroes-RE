// Ui/Hud/HudSkillPanel.cs
//
// In-game skill book window — SkillPanel (key K toggle).
//
// Placement (CODE-CONFIRMED):
//   W = 964, H = 655, X = 43, Y = −655 (off-screen top, slides down on open).
//   spec: Docs/RE/specs/ui_system.md §8.8 CODE-CONFIRMED — "SkillPanel W=964 H=655 at (43,-655)".
//
// Chrome atlas: data/ui/skill_window_1.dds (uitex 3).
//   spec: ui_system.md §8.6.1 — uitex 3 = skill_window_1.dds.
//
// Title: msg.xdb key 3027 (CODE-CONFIRMED).
//   spec: ui_system.md §8.8 CODE-CONFIRMED.
//
// 9 class tabs (action ids 802–810):
//   Each tab = clickable strip that filters the skill list by class group.
//   spec: ui_system.md §8.8 CODE-CONFIRMED — "action ids 802–810, 9 tabs".
//
// Skill pipe: 4 panels (not 50 as earlier plausible reading).
//   spec: ui_system.md §8.8 CODE-CONFIRMED — "4 skill-pipe panels".
//   TODO(spec): skill-pipe sub-panel exact geometry not recovered.
//
// Hotbar drag intent: raise SkillDragToHotbarRequested intent.
//   Outcome arrives as a hotbar-slot-set event from the Application layer.
//   Zero local mutation.
//
// Toggle: key K (toggled with inventory in the original; the HudMaster calls Toggle()).
//
// PASSIVE: reads ClientContext.SkillCatalogue; zero game logic.
// spec: Docs/RE/specs/ui_system.md §8.8 — SkillPanel CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game skill book window (SkillPanel). 964×655 parked at (43,−655); key-K toggles it.
///
/// <para>PASSIVE: reads skill catalogue for display; emits drag-to-hotbar intents as use-case calls.
/// Zero game logic — no validation, no optimistic state.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.8 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudSkillPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited placement constants
    // spec: Docs/RE/specs/ui_system.md §8.8 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float SkillPanelW = 964f;  // spec: ui_system.md §8.8
    private const float SkillPanelH = 655f;  // spec: ui_system.md §8.8
    private const float ParkX = 43f;         // spec: ui_system.md §8.8 — parked X
    private const float ParkY = -655f;       // spec: ui_system.md §8.8 — parked Y (off-screen top)
    private const float OpenY = 0f;          // revealed Y (slide in from top)

    // Tab action ids: 802–810 (9 class tabs)
    // spec: ui_system.md §8.8 CODE-CONFIRMED — "action ids 802–810"
    private const int TabActionStart = 802; // spec: ui_system.md §8.8
    private const int TabCount = 9;         // spec: ui_system.md §8.8

    // Skill-pipe count
    // spec: ui_system.md §8.8 CODE-CONFIRMED — "4 skill-pipe panels"
    private const int SkillPipeCount = 4; // spec: ui_system.md §8.8

    // Title msg.xdb key 3027
    // spec: ui_system.md §8.8 CODE-CONFIRMED — "title string = msg.xdb[3027]"
    private const int TitleMsgKey = 3027; // spec: ui_system.md §8.8

    // Chrome atlas — skill_window_1.dds (uitex 3)
    // spec: ui_system.md §8.6.1 — uitex 3 = data/ui/skill_window_1.dds
    private const int SkillWindowTexId = 3; // spec: ui_system.md §8.6.1

    // -------------------------------------------------------------------------
    // Child controls
    // -------------------------------------------------------------------------

    private readonly Button[] _tabs = new Button[TabCount];
    private VBoxContainer _skillList = null!;
    private Label _titleLabel = null!;

    // -------------------------------------------------------------------------
    // View state (not domain state)
    // -------------------------------------------------------------------------

    private bool _open;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    // HUD-II (world campaign): the skill→hotbar drag intent (skillId, hotbarSlot) belongs here, but it is
    // omitted until the drag gesture + the hotbar-slot-set use-case are built — declaring an unraised event
    // now would be dead code (CS0067). spec: ui_system.md §8.8 (skill panel); wiring is a follow-on.

    // -------------------------------------------------------------------------
    // Build (geometry pass)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: creates the 964×655 skill panel parked at (43,−655).
    /// spec: Docs/RE/specs/ui_system.md §8.8 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudSkillPanel";

        // Parked off-screen top: X=43, Y=−655
        // spec: ui_system.md §8.8 CODE-CONFIRMED
        Position = new Vector2(ParkX, ParkY);
        Size = new Vector2(SkillPanelW, SkillPanelH);
        Visible = false; // hidden by default; key-K reveals it
        MouseFilter = MouseFilterEnum.Stop;

        // Load chrome (skill_window_1.dds, uitex 3)
        // spec: ui_system.md §8.6.1 — uitex 3 = data/ui/skill_window_1.dds
        Texture2D? chromeTex = atlas.GetById(SkillWindowTexId);
        if (chromeTex is null)
            GD.PrintErr("[HudSkillPanel] skill_window_1.dds (uitex 3) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.6.1.");

        // Window background
        var bg = new Panel { Name = "Bg" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.93f);
        bgStyle.SetBorderWidthAll(2);
        bgStyle.BorderColor = new Color(0.45f, 0.35f, 0.15f, 0.9f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        // Chrome overlay
        if (chromeTex is not null)
        {
            var chrome = new TextureRect
            {
                Name = "Chrome",
                Texture = chromeTex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(chrome);
        }

        // Title from msg.xdb key 3027
        // spec: ui_system.md §8.8 CODE-CONFIRMED — msg.xdb[3027] = window title
        string titleStr = text.GetCaption(TitleMsgKey, "스킬"); // CP949 "스킬" = "Skill"
        _titleLabel = new Label
        {
            Name = "TitleLabel",
            Text = titleStr,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _titleLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _titleLabel.OffsetBottom = 30f;
        AddChild(_titleLabel);

        // 9 class tabs (action ids 802–810)
        // spec: ui_system.md §8.8 CODE-CONFIRMED — "9 tabs, action ids 802–810"
        var tabRow = new HBoxContainer
        {
            Name = "TabRow",
            Position = new Vector2(0f, 30f),
            Size = new Vector2(SkillPanelW, 28f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(tabRow);

        for (int t = 0; t < TabCount; t++)
        {
            int tabIdx = t;
            var tab = new Button
            {
                Name = $"Tab{TabActionStart + t}",
                // Tab label = class name; resolved from SkillCatalogue tab labels at runtime.
                // Placeholder numeric label for now.
                Text = $"{t + 1}",
                CustomMinimumSize = new Vector2(SkillPanelW / TabCount - 2f, 26f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            tab.Pressed += () => OnTabPressed(tabIdx);
            tabRow.AddChild(tab);
            _tabs[t] = tab;
        }

        // Skill list area (VBoxContainer of clickable skill entries)
        _skillList = new VBoxContainer
        {
            Name = "SkillList",
            Position = new Vector2(8f, 62f),
            Size = new Vector2(SkillPanelW - 16f, SkillPanelH - 80f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_skillList);

        // Skill-pipe panels (4 panels — placeholder; exact geometry not recovered)
        // spec: ui_system.md §8.8 CODE-CONFIRMED — "4 skill-pipe panels"
        // TODO(spec): recover exact skill-pipe geometry.
        for (int p = 0; p < SkillPipeCount; p++)
        {
            var pipePlaceholder = new Panel
            {
                Name = $"SkillPipe{p}",
                Position = new Vector2(8f + p * 240f, SkillPanelH - 60f),
                Size = new Vector2(232f, 52f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var ps = new StyleBoxFlat();
            ps.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.8f);
            ps.SetBorderWidthAll(1);
            ps.BorderColor = new Color(0.4f, 0.3f, 0.1f, 0.7f);
            pipePlaceholder.AddThemeStyleboxOverride("panel", ps);
            AddChild(pipePlaceholder);
        }

        GD.Print($"[HudSkillPanel] Built — 964×655 parked at ({ParkX},{ParkY}). " +
                 $"9 tabs (actions 802–810), 4 skill-pipe panels. " +
                 "spec: Docs/RE/specs/ui_system.md §8.8 CODE-CONFIRMED. " +
                 "TODO(spec): skill-pipe exact geometry pending. " +
                 "TODO(spec): hotbar overlay-rect values debugger-pending.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles the skill panel. Called by HudMaster on key-K press.
    /// spec: ui_system.md §8.8 — "[K] toggles SkillPanel (slots 158+159 logic)".
    /// </summary>
    public void Toggle()
    {
        _open = !_open;
        float newY = _open ? OpenY : ParkY;
        Position = new Vector2(ParkX, newY);
        Visible = _open;
        GD.Print($"[HudSkillPanel] Toggle → open={_open}, Y={newY}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.8.");
    }

    // -------------------------------------------------------------------------
    // Tab selection
    // -------------------------------------------------------------------------

    private void OnTabPressed(int tabIdx)
    {
        // Tab selection filters the skill list by class group.
        // Action ids 802–810 — the filtering uses the SkillCatalogue, not local game logic.
        // TODO(world-campaign): request a filtered skill list from UseCases/SkillCatalogue.
        GD.Print($"[HudSkillPanel] Tab {TabActionStart + tabIdx} pressed. " +
                 $"Filter intent deferred (TODO world-campaign). " +
                 "spec: Docs/RE/specs/ui_system.md §8.8 — action ids 802–810.");
    }
}
