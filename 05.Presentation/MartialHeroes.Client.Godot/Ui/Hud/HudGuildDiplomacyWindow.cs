
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudGuildDiplomacyWindow : Control
{

    private const float PanelW = 250f;
    private const float PanelH = 420f;

    private const int VisibleRows = 6;

    private const float RowCheckX = 30f;
    private const float RowNameX = 70f;
    private const float RowBaseY = 113f;
    private const float RowStride = 30f;
    private const float CheckW = 23f;
    private const float CheckH = 23f;
    private const float NameW = 147f;
    private const float NameH = 14f;
    private const float TabY = 24f;
    private const float TabW = 89f;
    private const float TabH = 20f;

    private const float DeclWarX = 30f;
    private const float DeclAllyX = 96f;
    private const float CancelX = 141f;
    private const float TabAX = 11f;
    private const float TabBX = 76f;
    private const float TabCX = 141f;
    private const float ActionBtnY = 346f;
    private const float ActionBtnW = 64f;
    private const float ActionBtnH = 25f;

    private const float NameInputX = 13f;
    private const float NameInputY = 383f;
    private const float NameInputW = 146f;
    private const float NameInputH = 26f;
    private const int NameInputMaxLen = 16;

    private const float RefreshX = 194f;
    private const float RefreshY = 375f;
    private const float RefreshW = 45f;
    private const float RefreshH = 25f;

    private const float PagePrevX = 23f;
    private const float PageNextX = 202f;
    private const float PageBtnY = 303f;
    private const float PageBtnSize = 25f;

    private const int MsgCancelNotice = 51014;

    private static readonly float[] TabX = { 11f, 91f, 171f };

    private readonly Label[] _rowNameLabels = new Label[VisibleRows];
    private int _activeTab;
    private Button? _cancelBtn;
    private int _currentPage;
    private LineEdit? _nameInput;


    private bool _open;
    private Label? _pageLabel;
    private int _selectedRow = -1;
    private int _totalPages = 1;


    public void Build(HudAtlasLibrary atlas)
    {
        Name = "HudGuildDiplomacyWindow";

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
        bdStyle.BgColor = new Color(0.07f, 0.06f, 0.08f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.35f, 0.25f, 0.45f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Guild Diplomacy",
            Position = new Vector2(10f, 4f),
            Size = new Vector2(180f, 18f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(titleLbl);

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

        var pagePrev = new Button
        {
            Name = "PagePrev",
            Text = "◄",
            Position = new Vector2(PagePrevX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        pagePrev.Pressed += () => OnPage(-1);
        AddChild(pagePrev);

        var pageNext = new Button
        {
            Name = "PageNext",
            Text = "►",
            Position = new Vector2(PageNextX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageNext.Pressed += () => OnPage(+1);
        AddChild(pageNext);

        _pageLabel = new Label
        {
            Name = "PageLabel",
            Text = "1 / 1",
            Position = new Vector2(PagePrevX, 330f),
            Size = new Vector2(60f, 13f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_pageLabel);

        var refreshBtn = new Button
        {
            Name = "RefreshBtn",
            Text = "List",
            Position = new Vector2(RefreshX, RefreshY),
            Size = new Vector2(RefreshW, RefreshH),
            MouseFilter = MouseFilterEnum.Stop
        };
        refreshBtn.Pressed += OnRefresh;
        AddChild(refreshBtn);

        var declWarBtn = new Button
        {
            Name = "DeclareWarBtn",
            Text = "Declare War",
            Position = new Vector2(DeclWarX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        declWarBtn.Pressed += OnDeclareWar;
        AddChild(declWarBtn);

        var declAllyBtn = new Button
        {
            Name = "DeclareAllyBtn",
            Text = "Declare Ally",
            Position = new Vector2(DeclAllyX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        declAllyBtn.Pressed += OnDeclareAlly;
        AddChild(declAllyBtn);

        _cancelBtn = new Button
        {
            Name = "CancelBtn",
            Text = "Cancel",
            Position = new Vector2(CancelX, ActionBtnY),
            Size = new Vector2(ActionBtnW, ActionBtnH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        _cancelBtn.Pressed += OnCancelRelation;
        AddChild(_cancelBtn);

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

        _nameInput = new LineEdit
        {
            Name = "NameInput",
            PlaceholderText = "Guild name...",
            Position = new Vector2(NameInputX, NameInputY),
            Size = new Vector2(NameInputW, NameInputH),
            MaxLength = NameInputMaxLen,
            MouseFilter = MouseFilterEnum.Stop
        };
        _nameInput.TextSubmitted += _ => OnNameSubmit();
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


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        GD.Print($"[HudGuildDiplomacyWindow] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.5 CODE-CONFIRMED.");
    }


    private void OnRowName(int row)
    {
        _selectedRow = row;
        GD.Print($"[HudGuildDiplomacyWindow] Row name {row} selected. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnRowCheck(int action)
    {
        var row = action - 6;
        _selectedRow = row;
        GD.Print($"[HudGuildDiplomacyWindow] Row check action {action} (row {row}) selected. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnRefresh()
    {
        GD.Print("[HudGuildDiplomacyWindow] Refresh list (action 12). " +
                 "TODO(world-campaign): C2S 2/81 list-fetch. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnPage(int delta)
    {
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
        _activeTab = action - 15;
        GD.Print($"[HudGuildDiplomacyWindow] Category tab action {action} (tab {_activeTab}). " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnDeclareWar()
    {
        GD.Print("[HudGuildDiplomacyWindow] Declare-war (action 28). " +
                 "TODO(world-campaign): validate (phase≥6, level, gold, banned, time) → confirm popup slot 167. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnDeclareAlly()
    {
        GD.Print("[HudGuildDiplomacyWindow] Declare-ally (action 29). " +
                 "TODO(world-campaign): validate (phase≥6, guild-master, cooldown, gold) → confirm popup. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnCancelRelation()
    {
        GD.Print($"[HudGuildDiplomacyWindow] Cancel-relation (action 30): notice msg {MsgCancelNotice}. " +
                 "No C2S send from this action. spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnActionTab(int action)
    {
        GD.Print($"[HudGuildDiplomacyWindow] Action tab {action}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
    }

    private void OnNameSubmit()
    {
        var name = _nameInput?.Text.Trim() ?? "";
        GD.Print($"[HudGuildDiplomacyWindow] Name submit '{name}' (action 34). " +
                 "spec: Docs/RE/specs/ui_system.md §8.30.4.");
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