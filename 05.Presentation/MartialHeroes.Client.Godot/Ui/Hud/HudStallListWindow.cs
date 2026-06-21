// Ui/Hud/HudStallListWindow.cs
//
// In-game personal-stall / player-vendor MARKET LIST window — `StallListPanel` (slot 228, key `l`).
//
// This is the searchable, sortable, paged list of player-run stalls. DISTINCT from the NPC
// item-shop vendor (HudVendorWindow, slot 259). Toggled by key `l` (ASCII 108).
//
// Geometry (CODE-CONFIRMED, panel-local origin):
//   Overall: ≈ 375 × 481.
//   Header sub-panel: (375×18 at (0,0)) — three title buttons.
//   Body sub-panel: (375×463 at (0,18)) — list and controls.
//   spec: Docs/RE/specs/ui_system.md §8.29.1 CODE-CONFIRMED
//
// Widget summary:
//   Title button A (refresh)  (333,2, 11,11)  action 12
//   Title button B (drag)     (345,2, 11,11)  action 13
//   Title button C (close)    (357,2, 11,11)  action 14
//   Page prev                 (16,355, 26,26) action 15
//   Page next                 (332,355, 26,26) action 16
//   Row hit buttons ×58       (23,58+25·i, 337,21) action=row_index (visible: 0..9)
//   Search textbox            (25,328, 325,22) maxlen 30, IME on, action 28
//   Submit/search btn         (268,321, 44,28) action 29
//   Reset/all btn             (313,321, 44,28) action 30
//   10 column sort btns       (112+15·i,361, 15,14) actions 17..26
//   Wide sort btn             (262,361, 59,14) action 27
//   Big enter btn             (37,410, 113,40) action 11
//   Big close btn             (224,410, 113,40) action 10
//   spec: Docs/RE/specs/ui_system.md §8.29.1 CODE-CONFIRMED
//
// Atlas: literal path `data/ui/stalllist.dds` (hard-embedded; NOT a uitex id).
//   spec: Docs/RE/specs/ui_system.md §8.29.2 CODE-CONFIRMED
//
// Row model (CODE-CONFIRMED):
//   10 visible rows per page; 58-row widget pool.
//   Row store: stall key/id + availability byte + CP949 "stallName`ownerActorId" string.
//   Render: split on backtick → "<stallName> - <ownerName>" (resolve actor id → display name).
//   Fallback captions: msg 29022 (unknown owner), 29023 (placeholder), 29018 (own-stall marker).
//   Pagination: actions 15/16; total pages = count/10 + 1.
//   spec: Docs/RE/specs/ui_system.md §8.29.3 CODE-CONFIRMED
//
// Key dispatch: `l` (ASCII 108) toggles slot 228.
//   spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED
//
// Opcodes (canonical names):
//   C2S 2/74 = CmsgStallListRequest (search/request, 30-byte CP949 name filter)
//   S2C 4/74 = SmsgStallListRefill (N×36-byte row records)
//   C2S 2/56 = CmsgStallEnter (4-byte selected stall id)
//   spec: Docs/RE/specs/ui_system.md §8.29.6 CODE-CONFIRMED
//
// Captions (msg.xdb ids CODE-CONFIRMED; CP949 text VFS-pending):
//   105=search/submit; 102=reset/all; 16014=own-list refresh; 29010=no stall selected;
//   29017=search placeholder; 29018=own-stall marker; 29019=search instruction;
//   29020=rate-limit countdown; 29021=input invalid; 29022=unknown owner; 29023=empty-row placeholder
//   spec: Docs/RE/specs/ui_system.md §8.29.7 CODE-CONFIRMED
//
// PASSIVE: zero game logic; intents → use-case calls (stubbed pending world-campaign).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game personal-stall market list window (StallListPanel, master service slot 228, key `l`).
///     <para>
///         A searchable, sortable, paged list of player-run stalls. Toggled by key `l`.
///         DISTINCT from the NPC vendor (slot 259) and the trade window (§8.13).
///     </para>
///     <para>
///         PASSIVE: zero game logic. Search/enter/sort emits intent stubs. Inbound S2C 4/74
///         (N×36B row records) is stubbed — stub rows are empty until server populates them.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.29 CODE-CONFIRMED.
/// </summary>
public sealed partial class HudStallListWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.29 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // Panel overall size
    // spec: ui_system.md §8.29.1 — "overall ≈ 375×481"
    private const float PanelW = 375f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float PanelH = 481f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED

    // Header sub-panel
    // spec: ui_system.md §8.29.1 — "header sub-panel (375×18 at (0,0))"
    private const float HeaderH = 18f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED

    // Row pool and visible count
    // spec: ui_system.md §8.29.3 — "10 visible rows per page; 58-row widget pool"
    private const int VisibleRows = 10; // spec: ui_system.md §8.29.3 CODE-CONFIRMED
    private const int RowPoolSize = 58; // spec: ui_system.md §8.29.3 CODE-CONFIRMED

    // Row geometry
    // spec: ui_system.md §8.29.1 — "(23, 58+25·i, 337, 21)"
    private const float RowX = 23f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float RowBaseY = 58f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float RowStrideY = 25f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float RowW = 337f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float RowH = 21f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED

    // Search textbox
    // spec: ui_system.md §8.29.1 — "(25,328,325,22) maxlen 30, IME on, action 28"
    private const float SearchX = 25f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float SearchY = 328f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float SearchW = 325f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const float SearchH = 22f; // spec: ui_system.md §8.29.1 CODE-CONFIRMED
    private const int SearchMaxLen = 30; // spec: ui_system.md §8.29.1 CODE-CONFIRMED

    // Search rate gate (10 seconds)
    // spec: ui_system.md §8.29.4 — "action 29: 10-second rate gate"
    private const double SearchRateSecs = 10.0; // spec: ui_system.md §8.29.4 CODE-CONFIRMED

    // msg.xdb caption ids (CODE-CONFIRMED; CP949 text VFS-pending)
    // spec: ui_system.md §8.29.7 CODE-CONFIRMED
    private const int MsgSubmitCaption = 105; // search/submit caption
    private const int MsgResetCaption = 102; // reset/all caption
    private const int MsgNoStallSelected = 29010; // no stall selected
    private const int MsgSearchPlaceholder = 29017; // search placeholder
    private const int MsgOwnStall = 29018; // own-stall marker
    private const int MsgSearchInstruction = 29019; // search instruction
    private const int MsgRateLimitCountdown = 29020; // rate-limit countdown
    private const int MsgInputInvalid = 29021; // input invalid
    private const int MsgUnknownOwner = 29022; // unknown owner
    private const int MsgEmptyRow = 29023; // empty-row placeholder

    private readonly Label[] _rowLabels = new Label[VisibleRows];
    private int _currentPage;
    private double _lastSearchTime = -SearchRateSecs; // allow first search immediately

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private Label? _pageLabel;
    private LineEdit? _searchBox;
    private int _selectedRow = -1; // -1 = none
    private int _totalPages = 1;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the stall list panel.
    ///     spec: Docs/RE/specs/ui_system.md §8.29.1 CODE-CONFIRMED.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudStallListWindow";

        // Port choice: centred on screen (absolute origin debugger-pending §8.29.8)
        // spec: ui_system.md §8.29 — "master-window-placed; absolute origin debugger-pending"
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

        // Main backdrop
        var bd = new Panel { Name = "Backdrop" };
        bd.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.3f, 0.3f, 0.55f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        // Header sub-panel (375×18 at (0,0))
        // spec: ui_system.md §8.29.1 — "header sub-panel 375×18"
        var hdr = new Panel { Name = "Header" };
        hdr.Position = Vector2.Zero;
        hdr.Size = new Vector2(PanelW, HeaderH);
        var hdrStyle = new StyleBoxFlat();
        hdrStyle.BgColor = new Color(0.1f, 0.1f, 0.18f, 0.95f);
        hdr.AddThemeStyleboxOverride("panel", hdrStyle);
        AddChild(hdr);

        // Title button A — refresh / own-list (action 12)
        // spec: ui_system.md §8.29.1 — "(333,2,11,11) action 12"
        var titleA = new Button
        {
            Name = "TitleBtnA",
            Text = "↻",
            Position = new Vector2(333f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        titleA.Pressed += OnRefreshOwnList; // action 12
        AddChild(titleA);

        // Title button B — drag toggle (action 13)
        // spec: ui_system.md §8.29.1 — "(345,2,11,11) action 13"
        var titleB = new Button
        {
            Name = "TitleBtnB",
            Text = "⠿",
            Position = new Vector2(345f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        titleB.Pressed += OnHeaderDrag; // action 13
        AddChild(titleB);

        // Title button C — close (action 14)
        // spec: ui_system.md §8.29.1 — "(357,2,11,11) action 14"
        var titleC = new Button
        {
            Name = "TitleBtnClose",
            Text = "×",
            Position = new Vector2(357f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        titleC.Pressed += OnDismissBody; // action 14 = dismiss body sub-panel
        AddChild(titleC);

        // 10 visible row hit buttons (actions 0..9)
        // spec: ui_system.md §8.29.1 — "(23, 58+25·i, 337, 21) action = row index"
        for (var r = 0; r < VisibleRows; r++)
        {
            var ry = RowBaseY + r * RowStrideY;
            var rowBtn = new Button
            {
                Name = $"Row{r}",
                Text = "",
                Position = new Vector2(RowX, ry),
                Size = new Vector2(RowW, RowH),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedRow = r;
            rowBtn.Pressed += () => OnRowSelect(capturedRow);
            AddChild(rowBtn);

            var rowLbl = new Label
            {
                Name = $"RowLbl{r}",
                Text = "",
                Position = new Vector2(RowX + 2f, ry + 2f),
                Size = new Vector2(RowW - 4f, RowH - 4f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(rowLbl);
            _rowLabels[r] = rowLbl;
        }

        // 10 column-sort buttons (actions 17..26)
        // spec: ui_system.md §8.29.1 — "(112+15·i, 361, 15, 14) actions 17..26"
        for (var c = 0; c < 10; c++)
        {
            var sortBtn = new Button
            {
                Name = $"SortCol{c}",
                Text = "▼",
                Position = new Vector2(112f + 15f * c, 361f),
                Size = new Vector2(15f, 14f),
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedCol = c;
            sortBtn.Pressed += () => OnSort(17 + capturedCol);
            AddChild(sortBtn);
        }

        // Wide name/owner sort button (action 27)
        // spec: ui_system.md §8.29.1 — "(262,361,59,14) action 27"
        var wideSort = new Button
        {
            Name = "WideSortBtn",
            Text = "Name",
            Position = new Vector2(262f, 361f),
            Size = new Vector2(59f, 14f),
            MouseFilter = MouseFilterEnum.Stop
        };
        wideSort.Pressed += () => OnSort(27);
        AddChild(wideSort);

        // Page prev button (action 15)
        // spec: ui_system.md §8.29.1 — "(16,355,26,26) action 15"
        var pagePrev = new Button
        {
            Name = "PagePrev",
            Text = "◄",
            Position = new Vector2(16f, 355f),
            Size = new Vector2(26f, 26f),
            MouseFilter = MouseFilterEnum.Stop
        };
        pagePrev.Pressed += () => OnPage(-1); // action 15
        AddChild(pagePrev);

        // Page next button (action 16)
        // spec: ui_system.md §8.29.1 — "(332,355,26,26) action 16"
        var pageNext = new Button
        {
            Name = "PageNext",
            Text = "►",
            Position = new Vector2(332f, 355f),
            Size = new Vector2(26f, 26f),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageNext.Pressed += () => OnPage(+1); // action 16
        AddChild(pageNext);

        // Page label "cur / total"
        // spec: ui_system.md §8.29.1 — "(60,363,15,13)"
        _pageLabel = new Label
        {
            Name = "PageLabel",
            Text = "1 / 1",
            Position = new Vector2(60f, 363f),
            Size = new Vector2(60f, 13f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_pageLabel);

        // Search textbox (maxlen 30, IME on, action 28)
        // spec: ui_system.md §8.29.1 — "(25,328,325,22) maxlen 30 action 28"
        // spec: ui_system.md §8.29.7 — "msg 29017 = search placeholder"
        _searchBox = new LineEdit
        {
            Name = "SearchBox",
            PlaceholderText = text.GetCaption(MsgSearchPlaceholder, "Search stalls..."),
            Position = new Vector2(SearchX, SearchY),
            Size = new Vector2(SearchW, SearchH),
            MaxLength = SearchMaxLen,
            MouseFilter = MouseFilterEnum.Stop
        };
        _searchBox.TextSubmitted += _ => OnSearchSubmit(); // action 28/29 combined
        AddChild(_searchBox);

        // Submit/search button (action 29)
        // spec: ui_system.md §8.29.1 — "(268,321,44,28) action 29"
        // spec: ui_system.md §8.29.7 — "msg 105 = search/submit caption"
        var submitBtn = new Button
        {
            Name = "SubmitBtn",
            Text = text.GetCaption(MsgSubmitCaption, "Search"),
            Position = new Vector2(268f, 321f),
            Size = new Vector2(44f, 28f),
            MouseFilter = MouseFilterEnum.Stop
        };
        submitBtn.Pressed += OnSearchSubmit; // action 29
        AddChild(submitBtn);

        // Reset/all button (action 30)
        // spec: ui_system.md §8.29.1 — "(313,321,44,28) action 30"
        // spec: ui_system.md §8.29.7 — "msg 102 = reset/all caption"
        var resetBtn = new Button
        {
            Name = "ResetBtn",
            Text = text.GetCaption(MsgResetCaption, "All"),
            Position = new Vector2(313f, 321f),
            Size = new Vector2(44f, 28f),
            MouseFilter = MouseFilterEnum.Stop
        };
        resetBtn.Pressed += OnSearchReset; // action 30
        AddChild(resetBtn);

        // Big ENTER button (action 11)
        // spec: ui_system.md §8.29.1 — "(37,410,113,40) action 11"
        var enterBtn = new Button
        {
            Name = "EnterBtn",
            Text = "Enter Stall",
            Position = new Vector2(37f, 410f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        enterBtn.Pressed += OnEnterStall; // action 11
        AddChild(enterBtn);

        // Big CLOSE button (action 10)
        // spec: ui_system.md §8.29.1 — "(224,410,113,40) action 10"
        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "Close",
            Position = new Vector2(224f, 410f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += OnClose; // action 10
        AddChild(closeBtn);

        GD.Print("[HudStallListWindow] Built — StallListPanel slot 228 (key 'l'). " +
                 "10 visible rows (58-pool); search textbox (maxlen 30, IME); " +
                 "10 col-sort btns (17..26) + wide sort (27); page prev/next (15/16); " +
                 "big enter (11) + close (10). " +
                 "Atlas: literal data/ui/stalllist.dds (VFS-pending). " +
                 "Inbound: TODO(capture): S2C 4/74 SmsgStallListRefill. " +
                 "Outbound: TODO(world-campaign): C2S 2/74 (search) + 2/56 (enter). " +
                 "spec: Docs/RE/specs/ui_system.md §8.29 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle (key `l`)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the stall list panel (key `l`, ASCII 108, slot 228).
    ///     spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED — key `l` toggle.
    /// </summary>
    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            // Reset search caption to placeholder on open
            // spec: ui_system.md §8.29.5 — "resets search caption to placeholder on open"
            if (_searchBox is not null) _searchBox.Text = "";
            _selectedRow = -1;
        }

        Visible = _open;
        GD.Print($"[HudStallListWindow] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Action handlers
    // -------------------------------------------------------------------------

    private void OnRowSelect(int row)
    {
        // actions 0..9 — select visible row N
        // spec: ui_system.md §8.29.4 — "0..9: select visible row N → set selected id, highlight"
        _selectedRow = row;
        GD.Print($"[HudStallListWindow] Row {row} selected. TODO(capture): S2C 4/74 populate. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnEnterStall()
    {
        // action 11 — enter selected stall
        // spec: ui_system.md §8.29.4 — "11: enter/open selected stall → C2S 2/56; msg 29010 if none selected"
        if (_selectedRow < 0)
        {
            GD.Print($"[HudStallListWindow] Enter stall: no row selected (msg {MsgNoStallSelected}). " +
                     "spec: Docs/RE/specs/ui_system.md §8.29.4.");
            return;
        }

        // TODO(world-campaign): IApplicationUseCases.StallEnter(selectedStallId) → C2S 2/56
        GD.Print($"[HudStallListWindow] Enter stall row {_selectedRow}: " +
                 "TODO(world-campaign): C2S 2/56 CmsgStallEnter. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4/§8.29.6.");
        Toggle(false);
    }

    private void OnClose()
    {
        // action 10 — close window
        // spec: ui_system.md §8.29.4 — "10: CLOSE window"
        Toggle(false);
    }

    private void OnSearchSubmit()
    {
        // action 29 — search/submit with 10-second rate gate
        // spec: ui_system.md §8.29.4 — "29: validate input, 10-second rate gate (msg 29017/29019/29020/29021), C2S 2/74"
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastSearchTime < SearchRateSecs)
        {
            GD.Print($"[HudStallListWindow] Search rate-limited (msg {MsgRateLimitCountdown}). " +
                     "spec: Docs/RE/specs/ui_system.md §8.29.4.");
            return;
        }

        var filter = _searchBox?.Text.Trim() ?? "";
        _lastSearchTime = now;
        // TODO(world-campaign): IApplicationUseCases.StallListRequest(filter) → C2S 2/74
        GD.Print($"[HudStallListWindow] Search submit filter='{filter}' → " +
                 "TODO(world-campaign): C2S 2/74 CmsgStallListRequest. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4/§8.29.6.");
    }

    private void OnSearchReset()
    {
        // action 30 — clear filter, restore placeholder, re-issue default list
        // spec: ui_system.md §8.29.4 — "30: RESET/ALL: clear filter, restore placeholder, re-issue default list"
        if (_searchBox is not null) _searchBox.Text = "";
        _selectedRow = -1;
        // TODO(world-campaign): re-issue default list request C2S 2/74 with empty filter
        GD.Print("[HudStallListWindow] Reset/All (action 30). " +
                 "TODO(world-campaign): C2S 2/74 empty filter. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnRefreshOwnList()
    {
        // action 12 — refresh own-stall list
        // spec: ui_system.md §8.29.4 — "12: refresh own-stall list (msg 16014)"
        GD.Print("[HudStallListWindow] Refresh own-list (action 12). " +
                 "TODO(world-campaign): fetch player own-stall list. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnHeaderDrag()
    {
        // action 13 — header drag toggle (caches drag delta on the master window)
        // spec: ui_system.md §8.29.4 — "13: header drag (caches drag delta on master window)"
        GD.Print("[HudStallListWindow] Header drag toggle (action 13). " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnDismissBody()
    {
        // action 14 — dismiss the body sub-panel
        // spec: ui_system.md §8.29.4 — "14: dismiss the body sub-panel"
        GD.Print("[HudStallListWindow] Dismiss body sub-panel (action 14). " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
        Toggle(false);
    }

    private void OnPage(int delta)
    {
        // actions 15 (prev) / 16 (next)
        // spec: ui_system.md §8.29.4 — "15/16: page up/down → re-render"
        var newPage = _currentPage + delta;
        if (newPage < 0) newPage = 0;
        if (newPage >= _totalPages) newPage = _totalPages - 1;
        _currentPage = newPage;
        UpdatePageLabel();
        GD.Print($"[HudStallListWindow] Page {_currentPage + 1}/{_totalPages} (delta {delta}). " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnSort(int action)
    {
        // actions 17..26 = column-sort; 27 = wide name/owner sort
        // spec: ui_system.md §8.29.4 — "17..26: column-sort; 27: wide name/owner sort → re-render"
        GD.Print($"[HudStallListWindow] Sort action {action} → re-render. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void UpdatePageLabel()
    {
        if (_pageLabel is not null)
            _pageLabel.Text = $"{_currentPage + 1} / {_totalPages}";
    }

    // -------------------------------------------------------------------------
    // Input (ESC closes)
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            // spec: ui_system.md §8.29.4 — "ESC (key 27): close window"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}