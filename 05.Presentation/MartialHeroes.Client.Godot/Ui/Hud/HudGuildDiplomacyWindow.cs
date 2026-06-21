// Ui/Hud/HudGuildDiplomacyWindow.cs
//
// In-game guild-diplomacy / brood-war relations list — `BroodWarListPanel` (slot 235, key `u`).
//
// Despite the class name, this is the GUILD-DIPLOMACY / BROOD-WAR RELATIONS ROSTER —
// the roster of allies / war-declarations / enemies, keyed by relation-state byte.
// It is NOT a list of scheduled brood-war events with per-row join buttons.
//
// Master service slot 235. Toggled by key `u` (ASCII 117).
//
// Geometry (CODE-CONFIRMED, panel-local origin):
//   3 top category tabs       (x∈{11,91,171}, 24, 89, 20)   actions 15/16/17
//   6 row check buttons       (30, 113+30·i, 23, 23)         actions 6..11
//   6 row name buttons        (70, 113+30·i, 147, 14)        actions 0..5
//   Prev-page (◄)             (23, 303, 25, 25)              action 13
//   Next-page (►)             (202, 303, 25, 25)             action 14
//   Page label "%s / %s"      (23, 330, 15, 13)
//   Refresh/list button        (194, 375, 45, 25)             action 12
//   Declare-war button         (30, 346, 64, 25)              action 28
//   Declare-ally button        (96, 346, 64, 25)              action 29
//   Cancel-relation (hidden)   (141, 346, 64, 25)             action 30
//   Tab-A button              (11, 346, 64, 25)               action 31
//   Tab-B button              (76, 346, 64, 25)               action 32
//   Tab-C button              (141, 346, 64, 25)              action 33
//   Name input textbox (maxlen 16) (13, 383, 146, 26)         action 34
//   spec: Docs/RE/specs/ui_system.md §8.30.1 CODE-CONFIRMED
//
// Atlas: literal path `data/ui/broodwarlist.dds` (hard-embedded; NOT a uitex id).
//   spec: Docs/RE/specs/ui_system.md §8.30.2 CODE-CONFIRMED
//
// Row model (CODE-CONFIRMED):
//   6 visible rows per page; page math: 10 pages per "wide page"; total = count/6+1.
//   Each row: guild/target id + relation-state + CP949 name.
//   Row colour by state: 1→blue, 3→orange, 4→green, 5→purple, else white.
//   Filter: relation state < 5 (allies) vs state==5 (brood-war/enemy).
//   spec: Docs/RE/specs/ui_system.md §8.30.3 CODE-CONFIRMED
//
// Key dispatch: `u` (ASCII 117) toggles slot 235.
//   spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED
//
// Opcodes (canonical names):
//   C2S 2/81 = CmsgGuildDiplomacyDeclare (18B: 1-byte action/state + 17-byte CP949 guild name)
//   S2C 4/81 = SmsgGuildDiplomacyResult (relation-result push, rebuilds roster)
//   spec: Docs/RE/specs/ui_system.md §8.30.6 CODE-CONFIRMED
//
// Captions (msg.xdb ids CODE-CONFIRMED; CP949 text VFS-pending):
//   confirm-dialog: 49046..49056, 51002..51013, 21064..21066
//   action-30 notice: 51014
//   precheck notices: 21025..21035, 2085, 51028..51031
//   rank/gold: 21044..21046, 21069
//   spec: Docs/RE/specs/ui_system.md §8.30.7 CODE-CONFIRMED
//
// Note on declare-war/ally: actions 28/29/31/32 open a CONFIRM POPUP (slot 167), NOT this panel.
//   The actual network SEND fires from that dialog's OK callback. This panel leads to send only.
//   spec: Docs/RE/specs/ui_system.md §8.30.4 — "confirm popup at slot 167; actual SEND from there"
//
// PASSIVE: zero game logic; intents → use-case calls (stubbed pending world-campaign).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game guild-diplomacy / brood-war relations list window (BroodWarListPanel, slot 235, key `u`).
///     <para>
///         The roster of allies / war-declarations / enemies, filtered by relation-state bucket.
///         Declare-war/ally actions open a confirm popup (slot 167) — the actual send fires from there.
///         Toggled by key `u` (ASCII 117).
///     </para>
///     <para>
///         PASSIVE: zero game logic. Inbound S2C 4/81 (roster push) is stubbed. Declare/cancel
///         actions emit intent stubs (world-campaign).
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.30 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudGuildDiplomacyWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.30 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Port choice panel size (absolute origin debugger-pending §8.30.8)
    // Estimated from row geometry: 6 rows × 30 = 180 + header/footer ≈ 420×420
    private const float PanelW = 250f; // spec: ui_system.md §8.30 — width estimate (debugger-pending)
    private const float PanelH = 420f; // spec: ui_system.md §8.30 — height estimate (debugger-pending)

    // Visible rows
    // spec: ui_system.md §8.30.3 — "6 visible rows per page"
    private const int VisibleRows = 6; // spec: ui_system.md §8.30.3 CODE-CONFIRMED

    // Row geometry
    // spec: ui_system.md §8.30.1 — "step y+30 (rows 0..5)"
    private const float RowCheckX = 30f; // spec: ui_system.md §8.30.1 "check at (30, 113+30·i, 23, 23)"
    private const float RowNameX = 70f; // spec: ui_system.md §8.30.1 "name at (70, 113+30·i, 147, 14)"
    private const float RowBaseY = 113f; // spec: ui_system.md §8.30.1 CODE-CONFIRMED
    private const float RowStride = 30f; // spec: ui_system.md §8.30.1 CODE-CONFIRMED
    private const float CheckW = 23f; // spec: ui_system.md §8.30.1 CODE-CONFIRMED
    private const float CheckH = 23f; // spec: ui_system.md §8.30.1 CODE-CONFIRMED
    private const float NameW = 147f; // spec: ui_system.md §8.30.1 CODE-CONFIRMED
    private const float NameH = 14f; // spec: ui_system.md §8.30.1 CODE-CONFIRMED
    private const float TabY = 24f;
    private const float TabW = 89f;
    private const float TabH = 20f;

    // Action button geometry (declare-war, declare-ally, cancel-relation, tabs A/B/C)
    // spec: ui_system.md §8.30.1 CODE-CONFIRMED
    private const float DeclWarX = 30f; // action 28
    private const float DeclAllyX = 96f; // action 29
    private const float CancelX = 141f; // action 30 (init hidden)
    private const float TabAX = 11f; // action 31
    private const float TabBX = 76f; // action 32
    private const float TabCX = 141f; // action 33
    private const float ActionBtnY = 346f;
    private const float ActionBtnW = 64f;
    private const float ActionBtnH = 25f;

    // Name input textbox
    // spec: ui_system.md §8.30.1 — "(13, 383, 146, 26) maxlen 16, action 34"
    private const float NameInputX = 13f;
    private const float NameInputY = 383f;
    private const float NameInputW = 146f;
    private const float NameInputH = 26f;
    private const int NameInputMaxLen = 16; // spec: ui_system.md §8.30.1 CODE-CONFIRMED

    // Refresh / list button
    // spec: ui_system.md §8.30.1 — "(194, 375, 45, 25) action 12"
    private const float RefreshX = 194f;
    private const float RefreshY = 375f;
    private const float RefreshW = 45f;
    private const float RefreshH = 25f;

    // Page buttons
    // spec: ui_system.md §8.30.1 — "prev (23,303,25,25) action 13; next (202,303,25,25) action 14"
    private const float PagePrevX = 23f;
    private const float PageNextX = 202f;
    private const float PageBtnY = 303f;
    private const float PageBtnSize = 25f;

    // msg.xdb notice id for cancel
    // spec: ui_system.md §8.30.7 CODE-CONFIRMED
    private const int MsgCancelNotice = 51014; // action 30 cancel notice

    // Top tabs (3 category tabs)
    // spec: ui_system.md §8.30.1 — "(x∈{11,91,171}, 24, 89, 20) actions 15/16/17"
    private static readonly float[] TabX = { 11f, 91f, 171f };

    private readonly Label[] _rowNameLabels = new Label[VisibleRows];
    private int _activeTab; // 0..2 top category tabs
    private Button? _cancelBtn;
    private int _currentPage;
    private LineEdit? _nameInput;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private Label? _pageLabel;
    private int _selectedRow = -1;
    private int _totalPages = 1;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the guild-diplomacy roster panel.
    ///     spec: Docs/RE/specs/ui_system.md §8.30.1 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudGuildDiplomacyWindow";

        // Port choice: centred on screen (absolute origin debugger-pending §8.30.8)
        // spec: ui_system.md §8.30 — "master-window-placed; absolute origin debugger-pending"
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -PanelW / 2f;
        OffsetTop = -PanelH / 2f;
        OffsetRight = PanelW / 2f;
        OffsetBottom = PanelH / 2f;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        // Backdrop
        var bd = new Panel { Name = "Backdrop" };
        bd.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.06f, 0.08f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.35f, 0.25f, 0.45f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        // Title label
        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Guild Diplomacy",
            Position = new Vector2(10f, 4f),
            Size = new Vector2(180f, 18f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(titleLbl);

        // 3 top category tabs (actions 15/16/17)
        // spec: ui_system.md §8.30.1 — "(x∈{11,91,171}, 24, 89, 20) actions 15/16/17"
        for (var t = 0; t < 3; t++)
        {
            var capturedT = t;
            var tab = new Button
            {
                Name = $"CategoryTab{t}",
                Text = t == 0 ? "Allies" : t == 1 ? "War" : "Enemy",
                Position = new Vector2(TabX[t], TabY),
                Size = new Vector2(TabW, TabH),
                MouseFilter = MouseFilterEnum.Stop
            };
            tab.Pressed += () => OnCategoryTab(15 + capturedT);
            AddChild(tab);
        }

        // 6 visible row pairs: check buttons (6..11) + name buttons (0..5)
        // spec: ui_system.md §8.30.1 — "step y+30 (rows 0..5)"
        for (var r = 0; r < VisibleRows; r++)
        {
            var ry = RowBaseY + r * RowStride;
            var capturedR = r;

            var checkBtn = new Button
            {
                Name = $"RowCheck{r}",
                Text = "□",
                Position = new Vector2(RowCheckX, ry),
                Size = new Vector2(CheckW, CheckH),
                MouseFilter = MouseFilterEnum.Stop
            };
            checkBtn.Pressed += () => OnRowCheck(6 + capturedR);
            AddChild(checkBtn);

            var nameBtn = new Button
            {
                Name = $"RowName{r}",
                Text = "",
                Position = new Vector2(RowNameX, ry),
                Size = new Vector2(NameW, NameH),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Stop
            };
            nameBtn.Pressed += () => OnRowName(capturedR);
            AddChild(nameBtn);

            var nameLbl = new Label
            {
                Name = $"RowNameLbl{r}",
                Text = "",
                Position = new Vector2(RowNameX + 2f, ry + 1f),
                Size = new Vector2(NameW - 4f, NameH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(nameLbl);
            _rowNameLabels[r] = nameLbl;
        }

        // Page prev/next buttons
        // spec: ui_system.md §8.30.1 — "prev (23,303,25,25) action 13; next (202,303,25,25) action 14"
        var pagePrev = new Button
        {
            Name = "PagePrev",
            Text = "◄",
            Position = new Vector2(PagePrevX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        pagePrev.Pressed += () => OnPage(-1); // action 13
        AddChild(pagePrev);

        var pageNext = new Button
        {
            Name = "PageNext",
            Text = "►",
            Position = new Vector2(PageNextX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageNext.Pressed += () => OnPage(+1); // action 14
        AddChild(pageNext);

        // Page label "%s / %s"
        // spec: ui_system.md §8.30.1 — "(23, 330, 15, 13)"
        _pageLabel = new Label
        {
            Name = "PageLabel",
            Text = "1 / 1",
            Position = new Vector2(PagePrevX, 330f),
            Size = new Vector2(60f, 13f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_pageLabel);

        // Refresh / list button (action 12)
        // spec: ui_system.md §8.30.1 — "(194, 375, 45, 25) action 12"
        var refreshBtn = new Button
        {
            Name = "RefreshBtn",
            Text = "List",
            Position = new Vector2(RefreshX, RefreshY),
            Size = new Vector2(RefreshW, RefreshH),
            MouseFilter = MouseFilterEnum.Stop
        };
        refreshBtn.Pressed += OnRefresh; // action 12
        AddChild(refreshBtn);

        // Declare-war button (action 28)
        // spec: ui_system.md §8.30.1 — "(30, 346, 64, 25) action 28"
        var declWarBtn = new Button
        {
            Name = "DeclareWarBtn",
            Text = "Declare War",
            Position = new Vector2(DeclWarX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        declWarBtn.Pressed += OnDeclareWar; // action 28
        AddChild(declWarBtn);

        // Declare-ally button (action 29)
        // spec: ui_system.md §8.30.1 — "(96, 346, 64, 25) action 29"
        var declAllyBtn = new Button
        {
            Name = "DeclareAllyBtn",
            Text = "Declare Ally",
            Position = new Vector2(DeclAllyX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        declAllyBtn.Pressed += OnDeclareAlly; // action 29
        AddChild(declAllyBtn);

        // Cancel-relation button (action 30, initially hidden)
        // spec: ui_system.md §8.30.1 — "(141, 346, 64, 25) action 30 — init hidden"
        _cancelBtn = new Button
        {
            Name = "CancelBtn",
            Text = "Cancel",
            Position = new Vector2(CancelX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            Visible = false, // hidden on init per spec
            MouseFilter = MouseFilterEnum.Stop
        };
        _cancelBtn.Pressed += OnCancelRelation; // action 30
        AddChild(_cancelBtn);

        // Tab-A button (action 31)
        // spec: ui_system.md §8.30.1 — "(11, 346, 64, 25) action 31"
        var tabABtn = new Button
        {
            Name = "TabABtn",
            Text = "A",
            Position = new Vector2(TabAX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        tabABtn.Pressed += () => OnActionTab(31);
        AddChild(tabABtn);

        // Tab-B button (action 32)
        // spec: ui_system.md §8.30.1 — "(76, 346, 64, 25) action 32"
        var tabBBtn = new Button
        {
            Name = "TabBBtn",
            Text = "B",
            Position = new Vector2(TabBX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        tabBBtn.Pressed += () => OnActionTab(32);
        AddChild(tabBBtn);

        // Tab-C button (action 33)
        // spec: ui_system.md §8.30.1 — "(141, 346, 64, 25) action 33"
        var tabCBtn = new Button
        {
            Name = "TabCBtn",
            Text = "C",
            Position = new Vector2(TabCX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        tabCBtn.Pressed += () => OnActionTab(33);
        AddChild(tabCBtn);

        // Name input textbox (action 34, maxlen 16)
        // spec: ui_system.md §8.30.1 — "(13, 383, 146, 26) maxlen 16 action 34"
        _nameInput = new LineEdit
        {
            Name = "NameInput",
            PlaceholderText = "Guild name...",
            Position = new Vector2(NameInputX, NameInputY),
            Size = new Vector2(NameInputW, NameInputH),
            MaxLength = NameInputMaxLen,
            MouseFilter = MouseFilterEnum.Stop
        };
        _nameInput.TextSubmitted += _ => OnNameSubmit(); // action 34
        AddChild(_nameInput);

        GD.Print("[HudGuildDiplomacyWindow] Built — BroodWarListPanel slot 235 (key 'u'). " +
                 "6 visible rows; 3 category tabs (15/16/17); row check (6..11) + name (0..5). " +
                 "Page prev/next (13/14); refresh (12). " +
                 "Declare-war (28), declare-ally (29), cancel (30), tab A/B/C (31/32/33). " +
                 "Name input textbox (maxlen 16, action 34). " +
                 "Atlas: literal data/ui/broodwarlist.dds (VFS-pending). " +
                 "Inbound: TODO(capture): S2C 4/81 SmsgGuildDiplomacyResult. " +
                 "Outbound: TODO(world-campaign): C2S 2/81 CmsgGuildDiplomacyDeclare. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle (key `u`)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the guild-diplomacy window (key `u`, ASCII 117, slot 235).
    ///     spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED — key `u` toggle.
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        GD.Print($"[HudGuildDiplomacyWindow] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnRowName(int row)
    {
        // actions 0..5 — row name button select
        // spec: ui_system.md §8.30.4 — "0..5: row name buttons — select row N"
        _selectedRow = row;
        GD.Print($"[HudGuildDiplomacyWindow] Row name {row} selected. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnRowCheck(int action)
    {
        // actions 6..11 — row check buttons (also select)
        // spec: ui_system.md §8.30.4 — "6..11: row check buttons — also row select"
        var row = action - 6;
        _selectedRow = row;
        GD.Print($"[HudGuildDiplomacyWindow] Row check action {action} (row {row}) selected. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnRefresh()
    {
        // action 12 — refresh / re-page the visible list
        // spec: ui_system.md §8.30.4 — "12: refresh / re-page the visible list"
        GD.Print("[HudGuildDiplomacyWindow] Refresh list (action 12). " +
                 "TODO(world-campaign): C2S 2/81 list-fetch. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnPage(int delta)
    {
        // actions 13 (prev) / 14 (next)
        // spec: ui_system.md §8.30.4 — "13/14: prev/next page"
        var newPage = _currentPage + delta;
        if (newPage < 0) newPage = 0;
        if (newPage >= _totalPages) newPage = _totalPages - 1;
        _currentPage = newPage;
        if (_pageLabel is not null)
            _pageLabel.Text = $"{_currentPage + 1} / {_totalPages}";
        GD.Print($"[HudGuildDiplomacyWindow] Page {_currentPage + 1}/{_totalPages}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnCategoryTab(int action)
    {
        // actions 15/16/17 — top category tabs (filter)
        // spec: ui_system.md §8.30.4 — "15/16: top tabs — select category (filter)"
        _activeTab = action - 15;
        GD.Print($"[HudGuildDiplomacyWindow] Category tab action {action} (tab {_activeTab}). " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnDeclareWar()
    {
        // action 28 — declare-war (validates phase≥6, level, gold, banned name, time window → confirm popup)
        // spec: ui_system.md §8.30.4 — "28: DECLARE-WAR → validate → opens confirm popup (slot 167)"
        // Actual send fires from confirm popup's OK callback, not here.
        GD.Print("[HudGuildDiplomacyWindow] Declare-war (action 28). " +
                 "TODO(world-campaign): validate (phase≥6, level, gold, banned, time) → confirm popup slot 167. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnDeclareAlly()
    {
        // action 29 — declare-ally / register (validates phase≥6, guild-master, cooldown, gold → confirm popup)
        // spec: ui_system.md §8.30.4 — "29: DECLARE-ALLY → validate → confirm popup"
        GD.Print("[HudGuildDiplomacyWindow] Declare-ally (action 29). " +
                 "TODO(world-campaign): validate (phase≥6, guild-master, cooldown, gold) → confirm popup. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnCancelRelation()
    {
        // action 30 — cancel-relation: broadcasts notice msg 51014, no send
        // spec: ui_system.md §8.30.4 — "30: CANCEL-RELATION → notice msg 51014, no send"
        GD.Print($"[HudGuildDiplomacyWindow] Cancel-relation (action 30): notice msg {MsgCancelNotice}. " +
                 "No C2S send from this action. spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnActionTab(int action)
    {
        // actions 31/32/33 — action-area tab switches
        // spec: ui_system.md §8.30.4 — "31..33: tab / wide-page nav step"
        GD.Print($"[HudGuildDiplomacyWindow] Action tab {action}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnNameSubmit()
    {
        // action 34 — name textbox commit
        // spec: ui_system.md §8.30.4 — "34: name-input textbox commit"
        var name = _nameInput?.Text.Trim() ?? "";
        GD.Print($"[HudGuildDiplomacyWindow] Name submit '{name}' (action 34). " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    // -------------------------------------------------------------------------
    // Input (ESC closes)
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            // spec: ui_system.md §8.30.4 — "ESC (key 27, when focused): close panel"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}