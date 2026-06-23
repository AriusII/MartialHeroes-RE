
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudStallListWindow : Control
{

    private const float PanelW = 375f;
    private const float PanelH = 481f;

    private const float HeaderH = 18f;

    private const int VisibleRows = 10;
    private const int RowPoolSize = 58;

    private const float RowX = 23f;
    private const float RowBaseY = 58f;
    private const float RowStrideY = 25f;
    private const float RowW = 337f;
    private const float RowH = 21f;

    private const float SearchX = 25f;
    private const float SearchY = 328f;
    private const float SearchW = 325f;
    private const float SearchH = 22f;
    private const int SearchMaxLen = 30;

    private const double SearchRateSecs = 10.0;

    private const int MsgSubmitCaption = 105;
    private const int MsgResetCaption = 102;
    private const int MsgNoStallSelected = 29010;
    private const int MsgSearchPlaceholder = 29017;
    private const int MsgOwnStall = 29018;
    private const int MsgSearchInstruction = 29019;
    private const int MsgRateLimitCountdown = 29020;
    private const int MsgInputInvalid = 29021;
    private const int MsgUnknownOwner = 29022;
    private const int MsgEmptyRow = 29023;

    private readonly Label[] _rowLabels = new Label[VisibleRows];
    private int _currentPage;
    private double _lastSearchTime = -SearchRateSecs;


    private bool _open;
    private Label? _pageLabel;
    private LineEdit? _searchBox;
    private int _selectedRow = -1;
    private int _totalPages = 1;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudStallListWindow";

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

        var bd = new Panel { Name = "Backdrop" };
        bd.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.12f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.3f, 0.3f, 0.55f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        var hdr = new Panel { Name = "Header" };
        hdr.Position = Vector2.Zero;
        hdr.Size = new Vector2(PanelW, HeaderH);
        var hdrStyle = new StyleBoxFlat();
        hdrStyle.BgColor = new Color(0.1f, 0.1f, 0.18f, 0.95f);
        hdr.AddThemeStyleboxOverride("panel", hdrStyle);
        AddChild(hdr);

        var titleA = new Button
        {
            Name = "TitleBtnA",
            Text = "↻",
            Position = new Vector2(333f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        titleA.Pressed += OnRefreshOwnList;
        AddChild(titleA);

        var titleB = new Button
        {
            Name = "TitleBtnB",
            Text = "⠿",
            Position = new Vector2(345f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        titleB.Pressed += OnHeaderDrag;
        AddChild(titleB);

        var titleC = new Button
        {
            Name = "TitleBtnClose",
            Text = "×",
            Position = new Vector2(357f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        titleC.Pressed += OnDismissBody;
        AddChild(titleC);

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

        var pagePrev = new Button
        {
            Name = "PagePrev",
            Text = "◄",
            Position = new Vector2(16f, 355f),
            Size = new Vector2(26f, 26f),
            MouseFilter = MouseFilterEnum.Stop
        };
        pagePrev.Pressed += () => OnPage(-1);
        AddChild(pagePrev);

        var pageNext = new Button
        {
            Name = "PageNext",
            Text = "►",
            Position = new Vector2(332f, 355f),
            Size = new Vector2(26f, 26f),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageNext.Pressed += () => OnPage(+1);
        AddChild(pageNext);

        _pageLabel = new Label
        {
            Name = "PageLabel",
            Text = "1 / 1",
            Position = new Vector2(60f, 363f),
            Size = new Vector2(60f, 13f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_pageLabel);

        _searchBox = new LineEdit
        {
            Name = "SearchBox",
            PlaceholderText = text.GetCaption(MsgSearchPlaceholder, "Search stalls..."),
            Position = new Vector2(SearchX, SearchY),
            Size = new Vector2(SearchW, SearchH),
            MaxLength = SearchMaxLen,
            MouseFilter = MouseFilterEnum.Stop
        };
        _searchBox.TextSubmitted += _ => OnSearchSubmit();
        AddChild(_searchBox);

        var submitBtn = new Button
        {
            Name = "SubmitBtn",
            Text = text.GetCaption(MsgSubmitCaption, "Search"),
            Position = new Vector2(268f, 321f),
            Size = new Vector2(44f, 28f),
            MouseFilter = MouseFilterEnum.Stop
        };
        submitBtn.Pressed += OnSearchSubmit;
        AddChild(submitBtn);

        var resetBtn = new Button
        {
            Name = "ResetBtn",
            Text = text.GetCaption(MsgResetCaption, "All"),
            Position = new Vector2(313f, 321f),
            Size = new Vector2(44f, 28f),
            MouseFilter = MouseFilterEnum.Stop
        };
        resetBtn.Pressed += OnSearchReset;
        AddChild(resetBtn);

        var enterBtn = new Button
        {
            Name = "EnterBtn",
            Text = "Enter Stall",
            Position = new Vector2(37f, 410f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        enterBtn.Pressed += OnEnterStall;
        AddChild(enterBtn);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "Close",
            Position = new Vector2(224f, 410f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += OnClose;
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


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            if (_searchBox is not null) _searchBox.Text = "";
            _selectedRow = -1;
        }

        Visible = _open;
        GD.Print($"[HudStallListWindow] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.5 CODE-CONFIRMED.");
    }


    private void OnRowSelect(int row)
    {
        _selectedRow = row;
        GD.Print($"[HudStallListWindow] Row {row} selected. TODO(capture): S2C 4/74 populate. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnEnterStall()
    {
        if (_selectedRow < 0)
        {
            GD.Print($"[HudStallListWindow] Enter stall: no row selected (msg {MsgNoStallSelected}). " +
                     "spec: Docs/RE/specs/ui_system.md §8.29.4.");
            return;
        }

        GD.Print($"[HudStallListWindow] Enter stall row {_selectedRow}: " +
                 "TODO(world-campaign): C2S 2/56 CmsgStallEnter. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4/§8.29.6.");
        Toggle(false);
    }

    private void OnClose()
    {
        Toggle(false);
    }

    private void OnSearchSubmit()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastSearchTime < SearchRateSecs)
        {
            GD.Print($"[HudStallListWindow] Search rate-limited (msg {MsgRateLimitCountdown}). " +
                     "spec: Docs/RE/specs/ui_system.md §8.29.4.");
            return;
        }

        var filter = _searchBox?.Text.Trim() ?? "";
        _lastSearchTime = now;
        GD.Print($"[HudStallListWindow] Search submit filter='{filter}' → " +
                 "TODO(world-campaign): C2S 2/74 CmsgStallListRequest. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4/§8.29.6.");
    }

    private void OnSearchReset()
    {
        if (_searchBox is not null) _searchBox.Text = "";
        _selectedRow = -1;
        GD.Print("[HudStallListWindow] Reset/All (action 30). " +
                 "TODO(world-campaign): C2S 2/74 empty filter. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnRefreshOwnList()
    {
        GD.Print("[HudStallListWindow] Refresh own-list (action 12). " +
                 "TODO(world-campaign): fetch player own-stall list. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnHeaderDrag()
    {
        GD.Print("[HudStallListWindow] Header drag toggle (action 13). " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void OnDismissBody()
    {
        GD.Print("[HudStallListWindow] Dismiss body sub-panel (action 14). " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
        Toggle(false);
    }

    private void OnPage(int delta)
    {
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
        GD.Print($"[HudStallListWindow] Sort action {action} → re-render. " +
                 "spec: Docs/RE/specs/ui_system.md §8.29.4.");
    }

    private void UpdatePageLabel()
    {
        if (_pageLabel is not null)
            _pageLabel.Text = $"{_currentPage + 1} / {_totalPages}";
    }


    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}