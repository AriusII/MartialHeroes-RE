// Ui/Hud/HudQuestWindow.cs
//
// In-game Quest window — `QuestPanel` (right-dock, 318×732).
//
// Placement (CODE-CONFIRMED):
//   X = screenWidth + 318, Y = 0, W = 318, H = 732 — standard right-dock column.
//   Revealed: X = screenWidth − 318.
//   spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.
//
// Atlas (CODE-CONFIRMED):
//   uitex 8 = skillwindow.dds — PRIMARY: backdrops, tab buttons, accept/proceed, quest rows.
//   uitex 1 = mainwindow.dds — list scroll-thumb (action 92), available-list thumb, checkbox glyphs.
//   uitex 2 = inventwindow.dds — row-list scroll background; give-up button (action 91).
//   uitex 9 = messagewindow.dds — detail-tab quest-grid scroll up/down/thumb (actions 87/88/89/90).
//   spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.
//
// 3 browser tabs + detail state:
//   Tab 0 = ACTIVE (6 visible rows, base y=44, stride 31).
//   Tab 1 = COMPLETABLE (6 visible rows).
//   Tab 2 = AVAILABLE (10 visible rows).
//   Tab 3 = DETAIL (single-quest QuestInfoListPanel body).
//   spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.
//
// Quest row (per row): left btn (10,baseY,62×28), name btn (72,baseY,194×28), right btn (266,baseY,44×28).
//   spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.
//
// Actions (CODE-CONFIRMED):
//   Tab 0/1/2/3 = actions 0/1/2/3.
//   Accept = 85; proceed/track = 86; give-up = 91; tracking checkbox = 94.
//   Row bands: 4..9 (ACTIVE), 22..27 (COMPLETABLE), 40..49 (AVAILABLE).
//   Scroll: 87 (grid scroll up), 88 (thumb), 89 (up arrow), 90 (down arrow), 92 (list scroll-thumb).
//   spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.
//
// Captions: msg 18031 = active-tab quest header line; tracking key "CHAR_QUEST_TRACKING".
//   Tab/accept/proceed/give-up button captions: capture/data-pending.
//   spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED (18031).
//
// Outbound: C2S 2/28 CmsgQuestAction (12B, give-up SubAction=4); C2S 2/152 (per-row request).
//   → TODO(world-campaign).
// Inbound: S2C 5/68 SmsgQuestList (452B opaque) + S2C 5/73 SmsgQuestComplete (344B opaque).
//   → TODO(capture): quest list record bodies.
//   spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED opcodes.
//
// Toggle: ESC closes (CODE-CONFIRMED). Open key debugger/capture-pending.
//   TODO(spec): toggle hotkey.
//
// PASSIVE: zero game logic; intents → use-case calls (stubbed).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game Quest window (QuestPanel — 3 browser tabs + detail view).
///     <para>
///         PASSIVE: renders quest rows from Application events; emits accept/give-up intents
///         as use-case calls (stubbed pending world-campaign). Inbound 5/68 + 5/73 stubbed.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §3.3 / §5.3 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudQuestWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private const float QuestW = 318f; // spec: ui_system.md §8.16 — W=318
    private const float QuestH = 732f; // spec: ui_system.md §8.16 — H=732

    // Quest row geometry
    // spec: ui_system.md §8.16 — "row stride 31px; first row baseY=44"
    private const float RowBaseY = 44f; // spec: ui_system.md §8.16 CODE-CONFIRMED
    private const float RowStrideY = 31f; // spec: ui_system.md §8.16 CODE-CONFIRMED

    // Visible row counts per tab
    // spec: ui_system.md §8.16 — "ACTIVE=6, COMPLETABLE=6, AVAILABLE=10"
    private const int ActiveRows = 6; // spec: ui_system.md §8.16
    private const int CompletableRows = 6; // spec: ui_system.md §8.16
    private const int AvailableRows = 10; // spec: ui_system.md §8.16

    // Atlas ids
    // spec: ui_system.md §8.16 CODE-CONFIRMED
    private const int MainTexId = 8; // spec: ui_system.md §8.16 — uitex 8 = skillwindow.dds
    private const int ThumbTexId = 1; // spec: ui_system.md §8.16 — uitex 1 = mainwindow.dds
    private const int BottomTexId = 2; // spec: ui_system.md §8.16 — uitex 2 = inventwindow.dds
    private const int DetailTexId = 9; // spec: ui_system.md §8.16 — uitex 9 = messagewindow.dds

    // Action ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.16
    private const int ActionAccept = 85; // spec: ui_system.md §8.16 CODE-CONFIRMED
    private const int ActionProceed = 86; // spec: ui_system.md §8.16 CODE-CONFIRMED
    private const int ActionGiveUp = 91; // spec: ui_system.md §8.16 CODE-CONFIRMED
    private const int ActionTracking = 94; // spec: ui_system.md §8.16 CODE-CONFIRMED — writes CHAR_QUEST_TRACKING

    // Tab button src-Y (from skillwindow.dds at srcX=960)
    // spec: ui_system.md §8.16 — "tab buttons src-X 960; srcY 66/22/44 for ACTIVE/COMPLETABLE/AVAILABLE"
    private const int TabActiveSrcY = 66; // spec: ui_system.md §8.16
    private const int TabCompleteSrcY = 22; // spec: ui_system.md §8.16
    private const int TabAvailableSrcY = 44; // spec: ui_system.md §8.16

    // msg.xdb ids
    // spec: ui_system.md §8.16 — msg 18031 = "active-tab quest header line" CODE-CONFIRMED
    private const int MsgActiveHeader = 18031; // spec: ui_system.md §8.16 CODE-CONFIRMED
    private int _activeTab;

    private Control? _activeTabContent;
    private Control? _availableTabContent;
    private Control? _completableTabContent;
    private Control? _detailTabContent;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private bool _trackingEnabled;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the 318×732 right-anchored quest window.
    ///     spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudQuestWindow";

        // Right-anchored, off-screen until toggled
        // spec: ui_system.md §8.16 — X=screenWidth+318, Y=0; reveal X=screenWidth−318
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = QuestW;
        OffsetTop = 0f;
        OffsetRight = QuestW + QuestW;
        OffsetBottom = QuestH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var mainTex = atlas.GetById(MainTexId);
        if (mainTex is null)
            GD.PrintErr("[HudQuestWindow] skillwindow.dds (uitex 8) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.16.");

        // 3 browser tab buttons + detail-back control
        // spec: ui_system.md §8.16 — tab ACTIVE dst(4,105,62,22) action 0; COMPLETABLE dst(66,105,62,22) action 1;
        //                              AVAILABLE dst(128,105,64,22) action 2; Detail/close dst(192,107,63,18) action 3
        BuildTabButtons(atlas, text);

        // Quest row lists per tab
        _activeTabContent = BuildQuestRows("ActiveTab", ActiveRows, 4, atlas);
        _completableTabContent = BuildQuestRows("CompletableTab", CompletableRows, 22, atlas);
        _availableTabContent = BuildQuestRows("AvailableTab", AvailableRows, 40, atlas);
        _detailTabContent = BuildDetailPanel(text);

        AddChild(_activeTabContent);
        AddChild(_completableTabContent);
        AddChild(_availableTabContent);
        AddChild(_detailTabContent);

        // List scroll-thumb (action 92, uitex 1, dst 286,46,29,26)
        // spec: ui_system.md §8.16 CODE-CONFIRMED
        var scrollThumb = new Button
        {
            Name = "ListScrollThumb",
            Text = "▐",
            Position = new Vector2(286f, 46f),
            Size = new Vector2(29f, 26f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollThumb.Pressed += () => GD.Print("[HudQuestWindow] list scroll-thumb action 92.");
        AddChild(scrollThumb);

        // Accept / OK button (dst 49,658,113,40, action 85, src 903,552)
        // spec: ui_system.md §8.16 CODE-CONFIRMED
        var acceptBtn = new Button
        {
            Name = "AcceptBtn",
            Text = "Accept",
            Position = new Vector2(49f, 658f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        acceptBtn.Pressed += OnAccept; // action 85
        AddChild(acceptBtn);

        // Proceed / track button (dst 148,658,113,40, action 86, src 903,632)
        // spec: ui_system.md §8.16 CODE-CONFIRMED
        var proceedBtn = new Button
        {
            Name = "ProceedBtn",
            Text = "Proceed",
            Position = new Vector2(148f, 658f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        proceedBtn.Pressed += OnProceed; // action 86
        AddChild(proceedBtn);

        // Give-up / abandon button (dst 259,655,59,77, action 91, uitex 2 src 301,947)
        // spec: ui_system.md §8.16 CODE-CONFIRMED
        var giveUpBtn = new Button
        {
            Name = "GiveUpBtn",
            Text = "Give Up",
            Position = new Vector2(259f, 655f),
            Size = new Vector2(59f, 77f),
            MouseFilter = MouseFilterEnum.Stop
        };
        giveUpBtn.Pressed += OnGiveUp; // action 91
        AddChild(giveUpBtn);

        // Tracking checkbox (dst 9,669,24,24, action 94, uitex 1 off/on src 372,730 / 372,754)
        // spec: ui_system.md §8.16 CODE-CONFIRMED — "writes CHAR_QUEST_TRACKING to %d_%s_CHARACTER INI"
        var trackingCb = new CheckBox
        {
            Name = "TrackingCheckbox",
            Position = new Vector2(9f, 669f),
            Size = new Vector2(24f, 24f),
            ButtonPressed = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        trackingCb.Toggled += pressed =>
        {
            // spec: ui_system.md §8.16 — "action 94 writes CHAR_QUEST_TRACKING (0/1)"
            _trackingEnabled = pressed;
            // TODO(world-campaign): write CHAR_QUEST_TRACKING to per-char INI
            GD.Print($"[HudQuestWindow] action 94 = tracking checkbox → {pressed} (CHAR_QUEST_TRACKING). " +
                     "spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.");
        };
        AddChild(trackingCb);

        SelectTab(0); // default to ACTIVE tab

        GD.Print("[HudQuestWindow] Built — 318×732 right-anchored QuestPanel. " +
                 "Tabs: ACTIVE (6 rows), COMPLETABLE (6 rows), AVAILABLE (10 rows), DETAIL. " +
                 "Row stride 31px, base y=44. " +
                 "Inbound: TODO(capture): quest list record bodies (5/68/5/73 opaque). " +
                 "Outbound: TODO(world-campaign): C2S 2/28 (give-up) + 2/152 (row request). " +
                 "spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.");
    }

    private void BuildTabButtons(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        // spec: ui_system.md §8.16 CODE-CONFIRMED — tab buttons + detail-back
        (float x, float y, float w, float h, int action, string label, int srcY)[] tabs =
        {
            (4f, 105f, 62f, 22f, 0, "Active", TabActiveSrcY), // spec: dst(4,105,62,22) action 0
            (66f, 105f, 62f, 22f, 1, "Complete", TabCompleteSrcY), // spec: dst(66,105,62,22) action 1
            (128f, 105f, 64f, 22f, 2, "Available", TabAvailableSrcY), // spec: dst(128,105,64,22) action 2
            (192f, 107f, 63f, 18f, 3, "Detail", 0) // spec: dst(192,107,63,18) action 3
        };

        foreach (var (x, y, w, h, action, label, srcY) in tabs)
        {
            var capturedAction = action;
            var tabBtn = new Button
            {
                Name = $"TabBtn{action}",
                Text = label,
                Position = new Vector2(x, y),
                Size = new Vector2(w, h),
                MouseFilter = MouseFilterEnum.Stop
            };

            if (srcY > 0 && atlas.GetById(MainTexId) is not null)
            {
                var tabSlice = atlas.SliceById(MainTexId, 960, srcY, (int)w, (int)h);
                if (tabSlice is not null)
                {
                    var st = new StyleBoxTexture { Texture = tabSlice };
                    tabBtn.AddThemeStyleboxOverride("normal", st);
                }
            }

            tabBtn.Pressed += () => SelectTab(capturedAction);
            AddChild(tabBtn);
        }
    }

    private static Control BuildQuestRows(string tabName, int visibleRows, int rowActionBase, HudAtlasLibrary atlas)
    {
        // spec: ui_system.md §8.16 — "per row: left btn(10,baseY,62×28), name btn(72,baseY,194×28), right btn(266,baseY,44×28)"
        var container = new Control { Name = tabName };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        for (var r = 0; r < visibleRows; r++)
        {
            var baseY = RowBaseY + r * RowStrideY; // spec: ui_system.md §8.16

            // Left cell button (state/level)
            var leftBtn = new Button
            {
                Name = $"{tabName}Left{r}",
                Text = "",
                Position = new Vector2(10f, baseY),
                Size = new Vector2(62f, 28f),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedR = r;
            leftBtn.Pressed += () =>
                GD.Print(
                    $"[HudQuestWindow] {tabName} row {capturedR} left cell (action {rowActionBase + capturedR}). " +
                    "TODO(capture): quest list record bodies. " +
                    "spec: Docs/RE/specs/ui_system.md §8.16.");
            container.AddChild(leftBtn);

            // Quest name button
            var nameBtn = new Button
            {
                Name = $"{tabName}Name{r}",
                Text = "", // populated from inbound S2C 5/68 TODO(capture)
                Position = new Vector2(72f, baseY),
                Size = new Vector2(194f, 28f),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            nameBtn.Pressed += () =>
                GD.Print(
                    $"[HudQuestWindow] {tabName} row {capturedR} name cell (action {rowActionBase + capturedR}). " +
                    "TODO(capture): quest list record bodies. " +
                    "spec: Docs/RE/specs/ui_system.md §8.16.");
            container.AddChild(nameBtn);

            // Right cell button (action/icon)
            var rightBtn = new Button
            {
                Name = $"{tabName}Right{r}",
                Text = "",
                Position = new Vector2(266f, baseY),
                Size = new Vector2(44f, 28f),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            container.AddChild(rightBtn);
        }

        return container;
    }

    private Control BuildDetailPanel(HudTextLibrary text)
    {
        // spec: ui_system.md §8.16 — "detail-back = action 3; QuestInfoListPanel 318×605 detail body"
        var panel = new Control { Name = "DetailTab" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var detailBody = new Label
        {
            Name = "DetailBody",
            Text = "", // populated from quest text node — TODO(capture)
            Position = new Vector2(10f, 130f),
            Size = new Vector2(298f, 475f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(detailBody);

        // Detail-grid scroll buttons (actions 87/88/89/90, uitex 9)
        // spec: ui_system.md §8.16 CODE-CONFIRMED
        var scrollUp = new Button
        {
            Name = "DetailScrollUp",
            Text = "↑",
            Position = new Vector2(19f, 15f),
            Size = new Vector2(75f, 19f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollUp.Pressed += () => GD.Print("[HudQuestWindow] detail scroll up (action 87).");
        panel.AddChild(scrollUp);

        var scrollUpArrow = new Button
        {
            Name = "DetailScrollUpArrow",
            Text = "▲",
            Position = new Vector2(284f, 15f),
            Size = new Vector2(13f, 10f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollUpArrow.Pressed += () => GD.Print("[HudQuestWindow] detail scroll up arrow (action 89).");
        panel.AddChild(scrollUpArrow);

        var scrollDownArrow = new Button
        {
            Name = "DetailScrollDownArrow",
            Text = "▼",
            Position = new Vector2(284f, 264f),
            Size = new Vector2(13f, 10f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollDownArrow.Pressed += () => GD.Print("[HudQuestWindow] detail scroll down arrow (action 90).");
        panel.AddChild(scrollDownArrow);

        return panel;
    }

    // -------------------------------------------------------------------------
    // Tab switching
    // -------------------------------------------------------------------------

    private void SelectTab(int tab)
    {
        _activeTab = tab;

        if (_activeTabContent is not null) _activeTabContent.Visible = tab == 0;
        if (_completableTabContent is not null) _completableTabContent.Visible = tab == 1;
        if (_availableTabContent is not null) _availableTabContent.Visible = tab == 2;
        if (_detailTabContent is not null) _detailTabContent.Visible = tab == 3;
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnAccept()
    {
        // spec: ui_system.md §8.16 — "action 85 = accept/confirm (tab-dependent)"
        // TODO(world-campaign): IApplicationUseCases.QuestAccept → C2S 2/28 CmsgQuestAction
        GD.Print("[HudQuestWindow] action 85 = accept → TODO(world-campaign): C2S 2/28 CmsgQuestAction. " +
                 "spec: Docs/RE/specs/ui_system.md §8.16.");
    }

    private void OnProceed()
    {
        // spec: ui_system.md §8.16 — "action 86 = proceed/track"
        GD.Print("[HudQuestWindow] action 86 = proceed/track. " +
                 "spec: Docs/RE/specs/ui_system.md §8.16.");
    }

    private void OnGiveUp()
    {
        // spec: ui_system.md §8.16 — "action 91 = abandon (routes to give-up confirm → C2S 2/28 SubAction=4)"
        // TODO(world-campaign): IApplicationUseCases.QuestGiveUp → C2S 2/28 SubAction=4
        GD.Print("[HudQuestWindow] action 91 = give-up → TODO(world-campaign): C2S 2/28 SubAction=4. " +
                 "spec: Docs/RE/specs/ui_system.md §8.16.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the quest window on/off.
    ///     ESC closes (CODE-CONFIRMED). Open key: debugger/capture-pending.
    ///     spec: Docs/RE/specs/ui_system.md §8.16 — "ESC (key 27 with panel-open latch) closes CODE-CONFIRMED".
    ///     TODO(spec): toggle hotkey (open key debugger/capture-pending).
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            OffsetLeft = -QuestW;
            OffsetRight = 0f;
        }
        else
        {
            OffsetLeft = QuestW;
            OffsetRight = QuestW + QuestW;
        }

        Visible = _open;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            // spec: ui_system.md §8.16 — "ESC (key 27 with panel-open latch set) closes CODE-CONFIRMED"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}