// Ui/Hud/HudRelationPanel.cs
//
// In-game relation / teacher / fate window — `RelationPanel` (master service slot 193).
//
// RECONCILIATION DECISION (§8.28 — BuddyRelation vs RelationPanel):
//   Per §8.28, there are TWO SEPARATE social windows:
//     - RelationPanel (slot 193): the relation/teacher/fate window — THIS file.
//     - BuddyRelation (slot 185): the buddy/social sibling window — a SEPARATE class.
//   The existing HudFriendWindow is §8.14 FriendPanel (add/cut friend by name textbox) — a THIRD
//   distinct window, not either of these two. None of the three are duplicates of each other.
//
//   DefaultMenu action 4002 opens BOTH slot 185 (BuddyRelation) AND slot 193 (RelationPanel)
//   together via the group-open dispatcher. In HudMaster, DefaultMenu 4002 → ToggleRelation()
//   which calls both ToggleBuddyRelation() (slot 185 stub, routed to FriendWindow as closest
//   available social window until BuddyRelation is built) AND ToggleRelation() (this window).
//
//   BuddyRelation (slot 185) full layout is a SEPARATE DELIVERABLE (§8.28 "separate deliverable").
//   For Wave 4 E: RelationPanel (slot 193) is built here; BuddyRelation (slot 185) is a
//   TODO(world-campaign) — DefaultMenu 4002 routes to HudFriendWindow as the interim social window.
//
//   spec: Docs/RE/specs/ui_system.md §8.28 — "Load-bearing 185 ≠ 193 distinction"
//
// Geometry (CODE-CONFIRMED, panel-local origin):
//   Build-time dst (80, 200), size 295×393.
//   spec: Docs/RE/specs/ui_system.md §8.28.1 CODE-CONFIRMED
//
// Widgets (CODE-CONFIRMED, panel-local):
//   Close button (top-right)          (panelW−12, 2, 11, 11)    action 16
//   4 Tab buttons                     (x, y∈{6,63,120,177}, 50, 55) step y+57  actions 19..22
//   Header image                      (15, 74, 49, 21)
//   Name textbox (maxlen 16, IME off) (74, 74, 136, 21)          action 9 (submit)
//   Name OK/search button             (235, 74, 45, 21)           action 10
//   Bottom button A                   (58, 337, 68, 20)           action 11
//   Bottom button B                   (165, 337, 68, 20)          action 14
//   Bottom button C (overlay)         (165, 337, 68, 20)          action 15
//   6 member list rows                base y=118, step y+21 (while y<244)  row 0..5 + mini 34..39
//   Big list button                   (29, 259, 224, 18)          action 12
//   Secondary row                     (16, 94, 251, 18)           action 13
//   Secondary mini-button             (206, 96, 59, 14)           action 40
//   Page-up button                    (17, 303, 26, 26)           action 7
//   Page-down button                  (241, 303, 26, 26)          action 8
//   Page indicator label              (17, 340, 15, 13)
//   10 numeric page buttons           y=309, x=57+15·i, 15×14    actions 23..32
//   Page "more" button                (207, 309, 30, 14)          action 33
//   Confirm/close button              (123, 361, 45, 25)          action 6
//   spec: Docs/RE/specs/ui_system.md §8.28.1 CODE-CONFIRMED
//
// Atlas: literal path `data/ui/relation.dds` (hard-embedded; per-widget texId arg = 0).
//   spec: Docs/RE/specs/ui_system.md §8.28.2 CODE-CONFIRMED
//
// 4 tabs (actions 19..22). Tab cycle via TAB key. Paged 6-row list.
//   spec: Docs/RE/specs/ui_system.md §8.28.3 CODE-CONFIRMED structure
//
// Emits: chat-command text (not binary opcodes) via the chat-submit path.
//   action 10 (add/remove): "friend %s %s" / "cut %s"
//   action 14: CP949 chat-command (3-min cooldown — master/training request)
//   action 15: set teacher/master locally + detail page (fail: msg 2089/10074)
//   spec: Docs/RE/specs/ui_system.md §8.28.6 CODE-CONFIRMED
//
// Captions (msg.xdb ids CODE-CONFIRMED): 2089/10074 (teacher-set fail); 10066/10067/10078/10079
//   (relation removed/requested/fee notice); 2115/2116 (confirmation templates).
//   spec: Docs/RE/specs/ui_system.md §8.28.7 CODE-CONFIRMED
//
// Open: DefaultMenu 4002 group-open dispatcher (opens BOTH slot 185 BuddyRelation + this slot 193).
//   spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED path
//
// PASSIVE: zero game logic; intents → use-case stubs / chat-command line.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
/// In-game relation / teacher / fate window (RelationPanel, master service slot 193).
///
/// <para>A 4-tab paged roster for relation/teacher/fate/spouse management. Opened by DefaultMenu
/// action 4002 alongside BuddyRelation (slot 185). Uses chat-command text for add/remove/master
/// requests (no binary packet builders).</para>
///
/// <para>PASSIVE: zero game logic. All actions emit chat-command text stubs. Inbound roster
/// populate (S2C relation-push) is stubbed — rows are empty until server delivers them.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.28 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudRelationPanel : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.28 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Panel size (CODE-CONFIRMED build-time dst (80,200) size 295×393)
    // spec: ui_system.md §8.28.1 CODE-CONFIRMED
    private const float PanelW = 295f; // spec: ui_system.md §8.28.1 CODE-CONFIRMED
    private const float PanelH = 393f; // spec: ui_system.md §8.28.1 CODE-CONFIRMED

    // Tabs (4 tabs at y∈{6,63,120,177}, step y+57, 50×55)
    // spec: ui_system.md §8.28.1 CODE-CONFIRMED
    private static readonly float[] TabY = { 6f, 63f, 120f, 177f };
    private const float TabX = 0f; // spec: ui_system.md §8.28.1 "x"
    private const float TabW = 50f; // spec: ui_system.md §8.28.1 "50"
    private const float TabH = 55f; // spec: ui_system.md §8.28.1 "55"

    // Name textbox
    // spec: ui_system.md §8.28.1 — "(74, 74, 136, 21) maxlen 16, IME off, action 9"
    private const float NameTbX = 74f;
    private const float NameTbY = 74f;
    private const float NameTbW = 136f;
    private const float NameTbH = 21f;
    private const int NameMaxLen = 16; // spec: ui_system.md §8.28.1 CODE-CONFIRMED

    // Member list rows
    // spec: ui_system.md §8.28.1 — "base y=118, step y+21 (while y<244)"
    private const float MemberBaseY = 118f; // spec: ui_system.md §8.28.1 CODE-CONFIRMED
    private const float MemberStride = 21f; // spec: ui_system.md §8.28.1 CODE-CONFIRMED
    private const int MaxVisibleMembers = 6; // rows 0..5 fit in y 118..244

    // Confirm/close button and bottom buttons
    // spec: ui_system.md §8.28.1 CODE-CONFIRMED
    private const float ConfirmX = 123f; // action 6
    private const float ConfirmY = 361f;
    private const float ConfirmW = 45f;
    private const float ConfirmH = 25f;
    private const float BotAX = 58f; // action 11
    private const float BotBX = 165f; // action 14
    private const float BotY = 337f;
    private const float BotW = 68f;
    private const float BotH = 20f;

    // Page-up/down buttons
    // spec: ui_system.md §8.28.1 CODE-CONFIRMED
    private const float PageUpX = 17f; // action 7
    private const float PageDownX = 241f; // action 8
    private const float PageBtnY = 303f;
    private const float PageBtnSize = 26f;

    // 10 numeric page buttons + more
    // spec: ui_system.md §8.28.1 — "y=309, x=57+15·i, 15×14 actions 23..32"
    private const float NumPageBaseX = 57f;
    private const float NumPageY = 309f;
    private const float NumPageW = 15f;
    private const float NumPageH = 14f;
    private const int NumPageCount = 10; // spec: ui_system.md §8.28.1 CODE-CONFIRMED

    // 3-minute timed action cooldown
    // spec: ui_system.md §8.28.4 — "action 14: timed action (3-minute cooldown)"
    private const double TimedActionCooldownSecs = 180.0; // spec: ui_system.md §8.28.4 CODE-CONFIRMED

    // msg.xdb caption ids (CODE-CONFIRMED; CP949 text VFS-pending)
    // spec: ui_system.md §8.28.7 CODE-CONFIRMED
    private const int MsgTeacherSetFail1 = 2089; // teacher-set fail (action 15)
    private const int MsgTeacherSetFail2 = 10074; // teacher-set fail (action 15)

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private int _activeTab; // 0..3
    private int _currentPage;
    private double _lastTimedAction = -TimedActionCooldownSecs; // allow first immediately

    private readonly Label[] _memberLabels = new Label[MaxVisibleMembers];
    private LineEdit? _nameTextbox;
    private Label? _pageIndicator;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Geometry pass: builds the RelationPanel (slot 193).
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.28.1 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudRelationPanel";

        // Build-time dst (80, 200); absolute on-screen origin re-anchored by master-window (debugger-pending)
        // spec: ui_system.md §8.28.1 — "build-time dst (80, 200), size 295×393"
        Position = new Vector2(80f, 200f); // spec: ui_system.md §8.28.1 CODE-CONFIRMED
        Size = new Vector2(PanelW, PanelH);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var bd = new Panel { Name = "Backdrop" };
        bd.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.10f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.35f, 0.3f, 0.5f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        // Title label
        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Relations",
            Position = new Vector2(10f, 4f),
            Size = new Vector2(180f, 18f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(titleLbl);

        // Close button (top-right, action 16)
        // spec: ui_system.md §8.28.1 — "(panelW−12, 2, 11, 11) action 16"
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(PanelW - 12f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        closeBtn.Pressed += () => OnAction(16); // action 16 = close
        AddChild(closeBtn);

        // 4 tab buttons (actions 19..22)
        // spec: ui_system.md §8.28.1 — "(x, y∈{6,63,120,177}, 50, 55) step y+57, actions 19..22"
        for (int t = 0; t < 4; t++)
        {
            int capturedT = t;
            var tabBtn = new Button
            {
                Name = $"Tab{t}",
                Text = t == 0 ? "Master" : t == 1 ? "Parent" : t == 2 ? "Spouse" : "Fate",
                Position = new Vector2(TabX, TabY[t]),
                Size = new Vector2(TabW, TabH),
                MouseFilter = MouseFilterEnum.Stop,
            };
            tabBtn.Pressed += () => OnAction(19 + capturedT);
            AddChild(tabBtn);
        }

        // Name textbox (action 9, submit; maxlen 16, IME off)
        // spec: ui_system.md §8.28.1 — "(74,74,136,21) maxlen 16 IME off action 9"
        _nameTextbox = new LineEdit
        {
            Name = "NameTextbox",
            PlaceholderText = "Name...",
            Position = new Vector2(NameTbX, NameTbY),
            Size = new Vector2(NameTbW, NameTbH),
            MaxLength = NameMaxLen,
            MouseFilter = MouseFilterEnum.Stop,
        };
        _nameTextbox.TextSubmitted += (_) => OnAction(9);
        AddChild(_nameTextbox);

        // Name OK/search button (action 10)
        // spec: ui_system.md §8.28.1 — "(235, 74, 45, 21) action 10"
        var nameOkBtn = new Button
        {
            Name = "NameOkBtn",
            Text = "OK",
            Position = new Vector2(235f, NameTbY),
            Size = new Vector2(45f, NameTbH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        nameOkBtn.Pressed += () => OnAction(10);
        AddChild(nameOkBtn);

        // 6 member list rows (rows 0..5, base y=118, stride 21)
        // spec: ui_system.md §8.28.1 — "base y=118, step y+21 (while y<244)"
        for (int r = 0; r < MaxVisibleMembers; r++)
        {
            float ry = MemberBaseY + r * MemberStride;
            int capturedR = r;

            var rowBtn = new Button
            {
                Name = $"MemberRow{r}",
                Text = "",
                Position = new Vector2(57f, ry),
                Size = new Vector2(185f, MemberStride - 2f),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Stop,
            };
            rowBtn.Pressed += () => OnAction(capturedR); // actions 0..5
            AddChild(rowBtn);

            var rowLbl = new Label
            {
                Name = $"MemberLbl{r}",
                Text = "",
                Position = new Vector2(59f, ry + 2f),
                Size = new Vector2(183f, MemberStride - 6f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(rowLbl);
            _memberLabels[r] = rowLbl;

            // Mini-button per row (actions 34..39)
            // spec: ui_system.md §8.28.1 — "miniBtn (296,385)/(296,400) actions 34..39"
            var miniBtn = new Button
            {
                Name = $"MiniBtn{r}",
                Text = "•",
                Position = new Vector2(245f, ry),
                Size = new Vector2(25f, MemberStride - 2f),
                MouseFilter = MouseFilterEnum.Stop,
            };
            miniBtn.Pressed += () => OnAction(34 + capturedR);
            AddChild(miniBtn);
        }

        // Big list button (action 12)
        // spec: ui_system.md §8.28.1 — "(29,259,224,18) action 12"
        var bigListBtn = new Button
        {
            Name = "BigListBtn",
            Text = "",
            Position = new Vector2(29f, 259f),
            Size = new Vector2(224f, 18f),
            Flat = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        bigListBtn.Pressed += () => OnAction(12);
        AddChild(bigListBtn);

        // Secondary row (action 13)
        // spec: ui_system.md §8.28.1 — "(16,94,185,18) action 13"
        var secRow = new Button
        {
            Name = "SecondaryRow",
            Text = "",
            Position = new Vector2(16f, 94f),
            Size = new Vector2(185f, 18f),
            Flat = true,
            MouseFilter = MouseFilterEnum.Stop,
        };
        secRow.Pressed += () => OnAction(13);
        AddChild(secRow);

        // Secondary mini-button (action 40)
        // spec: ui_system.md §8.28.1 — "(206,96,59,14) action 40"
        var secMiniBtn = new Button
        {
            Name = "SecondaryMiniBtn",
            Text = "•",
            Position = new Vector2(206f, 96f),
            Size = new Vector2(59f, 14f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        secMiniBtn.Pressed += () => OnAction(40);
        AddChild(secMiniBtn);

        // Page-up (action 7) / page-down (action 8)
        // spec: ui_system.md §8.28.1 CODE-CONFIRMED
        var pageUpBtn = new Button
        {
            Name = "PageUp",
            Text = "◄",
            Position = new Vector2(PageUpX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop,
        };
        pageUpBtn.Pressed += () => OnAction(7); // action 7
        AddChild(pageUpBtn);

        var pageDownBtn = new Button
        {
            Name = "PageDown",
            Text = "►",
            Position = new Vector2(PageDownX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop,
        };
        pageDownBtn.Pressed += () => OnAction(8); // action 8
        AddChild(pageDownBtn);

        // Page indicator label (17, 340, 15, 13)
        // spec: ui_system.md §8.28.1 CODE-CONFIRMED
        _pageIndicator = new Label
        {
            Name = "PageIndicator",
            Text = "1",
            Position = new Vector2(17f, 340f),
            Size = new Vector2(50f, 13f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_pageIndicator);

        // 10 numeric page buttons (y=309, x=57+15·i, 15×14, actions 23..32)
        // spec: ui_system.md §8.28.1 CODE-CONFIRMED
        for (int p = 0; p < NumPageCount; p++)
        {
            int capturedP = p;
            var numPage = new Button
            {
                Name = $"NumPage{p}",
                Text = $"{p + 1}",
                Position = new Vector2(NumPageBaseX + 15f * p, NumPageY),
                Size = new Vector2(NumPageW, NumPageH),
                MouseFilter = MouseFilterEnum.Stop,
            };
            numPage.Pressed += () => OnAction(23 + capturedP);
            AddChild(numPage);
        }

        // Page "more" button (207, 309, 30, 14, action 33)
        // spec: ui_system.md §8.28.1 CODE-CONFIRMED
        var moreBtn = new Button
        {
            Name = "MoreBtn",
            Text = "...",
            Position = new Vector2(207f, NumPageY),
            Size = new Vector2(30f, NumPageH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        moreBtn.Pressed += () => OnAction(33);
        AddChild(moreBtn);

        // Bottom button A (action 11)
        // spec: ui_system.md §8.28.1 — "(58,337,68,20) action 11"
        var botA = new Button
        {
            Name = "BotBtnA",
            Text = "Info",
            Position = new Vector2(BotAX, BotY),
            Size = new Vector2(BotW, BotH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        botA.Pressed += () => OnAction(11);
        AddChild(botA);

        // Bottom button B (action 14) — 3-min cooldown
        // spec: ui_system.md §8.28.1 — "(165,337,68,20) action 14"
        var botB = new Button
        {
            Name = "BotBtnB",
            Text = "Request",
            Position = new Vector2(BotBX, BotY),
            Size = new Vector2(BotW, BotH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        botB.Pressed += () => OnAction(14);
        AddChild(botB);

        // Bottom button C overlay (action 15) — set teacher
        // spec: ui_system.md §8.28.1 — "(165,337,68,20) action 15 overlay"
        var botC = new Button
        {
            Name = "BotBtnC",
            Text = "Set Master",
            Position = new Vector2(BotBX, BotY + BotH + 2f), // slightly offset for overlay port
            Size = new Vector2(BotW, BotH),
            Visible = false, // hidden by default (overlay)
            MouseFilter = MouseFilterEnum.Stop,
        };
        botC.Pressed += () => OnAction(15);
        AddChild(botC);

        // Confirm / close button (action 6)
        // spec: ui_system.md §8.28.1 — "(123,361,45,25) action 6"
        var confirmBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = "Close",
            Position = new Vector2(ConfirmX, ConfirmY),
            Size = new Vector2(ConfirmW, ConfirmH),
            MouseFilter = MouseFilterEnum.Stop,
        };
        confirmBtn.Pressed += () => OnAction(6);
        AddChild(confirmBtn);

        GD.Print("[HudRelationPanel] Built — RelationPanel slot 193 (295×393). " +
                 "4 tabs (actions 19..22); 6 member rows (0..5) + mini-btns (34..39). " +
                 "Page up/down (7/8); 10 numeric pages (23..32); more (33). " +
                 "Add/remove name textbox (maxlen 16, IME off, action 9/10). " +
                 "Bottom: info (11), timed-request (14, 3-min cooldown), set-master (15). " +
                 "Atlas: literal data/ui/relation.dds per-widget texId=0 (VFS-pending). " +
                 "Emits: chat-command text via chat-submit path (not binary opcodes). " +
                 "Inbound: TODO(capture): relation-roster push S2C. " +
                 "RECONCILIATION: RelationPanel(193)≠BuddyRelation(185)≠FriendPanel(§8.14). " +
                 "BuddyRelation(185) = separate deliverable TODO(world-campaign). " +
                 "spec: Docs/RE/specs/ui_system.md §8.28 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles RelationPanel (slot 193).
    /// Opened by DefaultMenu action 4002 (which also opens BuddyRelation slot 185).
    /// spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED.
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        if (_open) _activeTab = 0; // tab reset to 0 on open per §8.28.5
        Visible = _open;
        GD.Print($"[HudRelationPanel] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action dispatch
    // -------------------------------------------------------------------------

    private void OnAction(int action)
    {
        // spec: ui_system.md §8.28.4 CODE-CONFIRMED
        switch (action)
        {
            case >= 0 and <= 5:
                // Member row click (rows 0..5) — select roster entry
                // spec: ui_system.md §8.28.4 — "0..5: member-list row click → select"
                GD.Print($"[HudRelationPanel] Row {action} selected. " +
                         "TODO(capture): inbound roster. spec: ui_system.md §8.28.4.");
                break;

            case 6:
                // Confirm/close (hide + rebuild)
                // spec: ui_system.md §8.28.4 — "6: close/confirm — hide + rebuild"
                Toggle(false);
                break;

            case 7:
                // Page-up
                // spec: ui_system.md §8.28.4 — "7: page-up"
                if (_currentPage > 0) _currentPage--;
                UpdatePageIndicator();
                GD.Print($"[HudRelationPanel] Page up → {_currentPage}. spec: ui_system.md §8.28.4.");
                break;

            case 8:
                // Page-down
                // spec: ui_system.md §8.28.4 — "8: page-down"
                _currentPage++;
                UpdatePageIndicator();
                GD.Print($"[HudRelationPanel] Page down → {_currentPage}. spec: ui_system.md §8.28.4.");
                break;

            case 9:
                // Name textbox submit (routes input focus, shows modal focus)
                // spec: ui_system.md §8.28.4 — "9: name-textbox submit"
                GD.Print("[HudRelationPanel] Name submit (action 9). " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 10:
                // ADD / REMOVE relation by typed name (chat-command text)
                // spec: ui_system.md §8.28.6 — "action 10 add: 'friend %s %s'; remove: 'cut %s'"
                string name = _nameTextbox?.Text.Trim() ?? "";
                if (!string.IsNullOrEmpty(name))
                {
                    // TODO(world-campaign): submit chat-command via IApplicationUseCases.ChatSubmit("friend %s %s" / "cut %s")
                    GD.Print($"[HudRelationPanel] Action 10 add/remove name='{name}': " +
                             "TODO(world-campaign): chat-command 'friend %s %s' / 'cut %s'. " +
                             "spec: Docs/RE/specs/ui_system.md §8.28.6 CODE-CONFIRMED.");
                }

                break;

            case 11:
                // Open detail/info context for selected member (keyed by active tab)
                // spec: ui_system.md §8.28.4 — "11: detail/info for selected member"
                GD.Print("[HudRelationPanel] Action 11 = detail/info. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 12:
            case 13:
                // Big list-button / secondary-row action (MED)
                // spec: ui_system.md §8.28.4 — "12/13: big list-button / secondary-row (MED)"
                GD.Print($"[HudRelationPanel] Action {action} (list/secondary — MED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 14:
                // Timed action (3-minute cooldown): CP949 chat-command (master/training request)
                // spec: ui_system.md §8.28.4 — "14: timed action (3-min cooldown); CP949 chat-command"
                double now = global::Godot.Time.GetTicksMsec() / 1000.0;
                if (now - _lastTimedAction < TimedActionCooldownSecs)
                {
                    GD.Print("[HudRelationPanel] Action 14 timed-request: cooldown active (3 min). " +
                             "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                    break;
                }

                _lastTimedAction = now;
                // TODO(world-campaign): submit CP949 chat-command via IApplicationUseCases.ChatSubmit(cp949Cmd)
                GD.Print("[HudRelationPanel] Action 14 timed-request: " +
                         "TODO(world-campaign): CP949 chat-command (master/training request). " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4/§8.28.6 CODE-CONFIRMED.");
                break;

            case 15:
                // Set teacher/master — validate local roster + detail page; fail: msg 2089/10074
                // spec: ui_system.md §8.28.4 — "15: set teacher/master (fail: msg 2089/10074)"
                GD.Print($"[HudRelationPanel] Action 15 = set teacher/master. " +
                         $"Fail notices: msg {MsgTeacherSetFail1}/{MsgTeacherSetFail2}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4 CODE-CONFIRMED.");
                break;

            case 16:
                // Window close box (top-right)
                // spec: ui_system.md §8.28.4 — "16: window close box"
                Toggle(false);
                break;

            case >= 19 and <= 22:
                // Select tab 0..3
                // spec: ui_system.md §8.28.4 — "19..22: select tab 0..3"
                _activeTab = action - 19;
                GD.Print($"[HudRelationPanel] Tab {_activeTab} selected (action {action}). " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4 CODE-CONFIRMED.");
                break;

            case >= 23 and <= 33:
                // Numeric page selection + page-more
                // spec: ui_system.md §8.28.4 — "23..32: numeric page; 33: page-more"
                if (action <= 32)
                    _currentPage = action - 23;
                UpdatePageIndicator();
                GD.Print($"[HudRelationPanel] Numeric page action {action} → page {_currentPage}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case >= 34 and <= 39:
                // Per-row mini-button (rows 0..5) — MED
                // spec: ui_system.md §8.28.4 — "34..39: per-row mini-button (MED)"
                GD.Print($"[HudRelationPanel] Mini-button action {action} (row {action - 34}) — MED. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 40:
                // Secondary-row mini-button — MED
                // spec: ui_system.md §8.28.4 — "40: secondary-row mini-button (MED)"
                GD.Print("[HudRelationPanel] Secondary mini-button (action 40) — MED. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            default:
                GD.Print($"[HudRelationPanel] Action {action} — unhandled. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;
        }
    }

    private void UpdatePageIndicator()
    {
        if (_pageIndicator is not null)
            _pageIndicator.Text = $"{_currentPage + 1}";
    }

    // -------------------------------------------------------------------------
    // Input (ESC closes; TAB cycles tabs)
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is not InputEventKey key || !key.Pressed) return;

        switch (key.Keycode)
        {
            case Key.Escape:
                // spec: ui_system.md §8.28.4 — "ESC (when focused): close"
                Toggle(false);
                GetViewport().SetInputAsHandled();
                break;

            case Key.Tab:
                // spec: ui_system.md §8.28.4 — "TAB key: cycle active tab (index+1 mod 4)"
                _activeTab = (_activeTab + 1) % 4;
                GD.Print($"[HudRelationPanel] TAB key → tab {_activeTab}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}