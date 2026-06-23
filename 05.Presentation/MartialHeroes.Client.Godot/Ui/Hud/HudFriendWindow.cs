
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudFriendWindow : Control
{

    private const float FriendW = 318f;
    private const float FriendH = 732f;

    private const int VisibleRows = 10;
    private const float RowBaseY = 212f;
    private const float RowStrideY = -16f;
    private const float RowW = 189f;
    private const float RowH = 17f;
    private const float RowX = 15f;
    private const float StatusGlyphX = 155f;
    private const float StatusGlyphSize = 12f;

    private const float TbX = 82f;
    private const float TbY = 46f;
    private const float TbW = 95f;
    private const float TbH = 20f;
    private const int TbMaxLen = 16;

    private const int MainTexId = 9;
    private const int ArrowTexId = 1;
    private const int ConfirmTexId = 2;

    private const int MsgLegend = 16012;
    private const int MsgModalHint = 100614;

    private const double RefreshThrottleSecs = 180.0;

    private readonly Button[] _rowBtnsA = new Button[VisibleRows];
    private readonly Button[] _rowBtnsB = new Button[VisibleRows];
    private readonly Label[] _statusA = new Label[VisibleRows];
    private readonly Label[] _statusB = new Label[VisibleRows];
    private int _activeTab;
    private double _lastRefreshTime = -RefreshThrottleSecs;


    private bool _open;
    private Control? _tabAContent;
    private Control? _tabBContent;

    private LineEdit? _textboxAdd;
    private LineEdit? _textboxCut;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudFriendWindow";

        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = FriendW;
        OffsetTop = 0f;
        OffsetRight = FriendW + FriendW;
        OffsetBottom = FriendH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.13f, 0.93f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.35f, 0.35f, 0.5f, 0.85f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var mainTex = atlas.GetById(MainTexId);
        if (mainTex is null)
            GD.PrintErr("[HudFriendWindow] messagewindow.dds (uitex 9) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.14.");

        var tabA = new Button
        {
            Name = "TabA",
            Text = "Add",
            Position = new Vector2(11f, 7f),
            Size = new Vector2(62f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tabA.Pressed += () => SelectTab(0);
        AddChild(tabA);

        var tabB = new Button
        {
            Name = "TabB",
            Text = "Cut",
            Position = new Vector2(73f, 7f),
            Size = new Vector2(62f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tabB.Pressed += () => SelectTab(1);
        AddChild(tabB);

        if (mainTex is not null)
        {
            var hdrSlice = atlas.SliceById(MainTexId, 463, 338, 189, 17);
            if (hdrSlice is not null)
            {
                var hdr = new TextureRect
                {
                    Name = "HeaderStrip",
                    Texture = hdrSlice,
                    Position = new Vector2(12f, 84f),
                    Size = new Vector2(189f, 17f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                AddChild(hdr);
            }
        }

        _textboxAdd = new LineEdit
        {
            Name = "TextboxAdd",
            Position = new Vector2(TbX, TbY),
            Size = new Vector2(TbW, TbH),
            MaxLength = TbMaxLen,
            PlaceholderText = "Friend name...",
            MouseFilter = MouseFilterEnum.Stop
        };
        _textboxAdd.TextSubmitted += name => CommitAddFriend(name);
        AddChild(_textboxAdd);

        _textboxCut = new LineEdit
        {
            Name = "TextboxCut",
            Position = new Vector2(TbX, TbY),
            Size = new Vector2(TbW, TbH),
            MaxLength = TbMaxLen,
            PlaceholderText = "Friend name...",
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        _textboxCut.TextSubmitted += name => CommitCutFriend(name);
        AddChild(_textboxCut);

        _tabAContent = BuildTabRows("TabA", _rowBtnsA, _statusA, false);
        AddChild(_tabAContent);

        _tabBContent = BuildTabRows("TabB", _rowBtnsB, _statusB, true);
        _tabBContent.Visible = false;
        AddChild(_tabBContent);

        BuildScrollButtons(atlas);

        var refreshBtn = new Button
        {
            Name = "RefreshBtn",
            Text = "Refresh",
            Position = new Vector2(130f, 236f),
            Size = new Vector2(83f, 22f),
            MouseFilter = MouseFilterEnum.Stop
        };
        refreshBtn.Pressed += OnRefresh;
        AddChild(refreshBtn);

        var okX = FriendW / 2f - 56f;
        var okY = FriendH - 66f;
        var okBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = text.GetCaption(MsgModalHint, "OK"),
            Position = new Vector2(okX, okY),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        okBtn.Pressed += OnConfirm;
        AddChild(okBtn);

        for (var i = 0; i < 3; i++)
        {
            var actionId = 15 + i;
            var topBtn = new Button
            {
                Name = $"TopBtn{actionId}",
                Text = "•",
                Position = new Vector2(265f + i * 16f, 7f),
                Size = new Vector2(14f, 14f),
                MouseFilter = MouseFilterEnum.Stop
            };
            var captured = actionId;
            topBtn.Pressed += () => OnTopBarAction(captured);
            AddChild(topBtn);
        }

        SelectTab(0);

        GD.Print("[HudFriendWindow] Built — FriendPanel two-tab (add/cut). " +
                 "2 textboxes (95×20, maxlen 16); 10 visible rows/tab (189×17, stride −16 from y=212). " +
                 "Inbound list: TODO(debugger/capture): inbound friend feed (5/26 candidate). " +
                 "Outbound: TODO(world-campaign): C2S 2/49 (add/cut) + 2/54 (refresh 3-min throttle). " +
                 "spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.");
    }

    private Control BuildTabRows(string tabName, Button[] rowBtns, Label[] statusGlyphs, bool hidden)
    {
        var container = new Control { Name = tabName + "Rows" };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.Visible = !hidden;

        for (var r = 0; r < VisibleRows; r++)
        {
            var rowY = RowBaseY + r * RowStrideY;

            var rowBtn = new Button
            {
                Name = $"{tabName}Row{r}",
                Text = "",
                Position = new Vector2(RowX, rowY),
                Size = new Vector2(RowW, RowH),
                Flat = true,
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedRow = r;
            rowBtn.Pressed += () => OnRowSelect(tabName == "TabA" ? 0 : 1, capturedRow);
            container.AddChild(rowBtn);
            rowBtns[r] = rowBtn;

            var statusGlyph = new Label
            {
                Name = $"{tabName}Status{r}",
                Text = "",
                Position = new Vector2(StatusGlyphX, rowY),
                Size = new Vector2(StatusGlyphSize, StatusGlyphSize),
                MouseFilter = MouseFilterEnum.Ignore
            };
            container.AddChild(statusGlyph);
            statusGlyphs[r] = statusGlyph;
        }

        return container;
    }

    private void BuildScrollButtons(HudAtlasLibrary atlas)
    {
        var scrollUp = new Button
        {
            Name = "ScrollUp",
            Text = "↑",
            Position = new Vector2(203f, 68f),
            Size = new Vector2(13f, 10f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollUp.Pressed += () => OnScroll(-1);
        AddChild(scrollUp);

        var scrollDown = new Button
        {
            Name = "ScrollDown",
            Text = "↓",
            Position = new Vector2(203f, 221f),
            Size = new Vector2(13f, 10f),
            MouseFilter = MouseFilterEnum.Stop
        };
        scrollDown.Pressed += () => OnScroll(+1);
        AddChild(scrollDown);
    }


    private void SelectTab(int tab)
    {
        _activeTab = tab;

        if (_tabAContent is not null) _tabAContent.Visible = tab == 0;
        if (_tabBContent is not null) _tabBContent.Visible = tab == 1;
        if (_textboxAdd is not null) _textboxAdd.Visible = tab == 0;
        if (_textboxCut is not null) _textboxCut.Visible = tab == 1;
    }


    private void CommitAddFriend(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Length > TbMaxLen ? name[..TbMaxLen] : name;
        GD.Print($"[HudFriendWindow] Add friend '{trimmed}' → TODO(world-campaign): C2S 2/49 tag=0. " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
        if (_textboxAdd is not null) _textboxAdd.Text = "";
    }

    private void CommitCutFriend(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var trimmed = name.Length > TbMaxLen ? name[..TbMaxLen] : name;
        GD.Print($"[HudFriendWindow] Cut friend '{trimmed}' → TODO(world-campaign): C2S 2/49 tag=1. " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
        if (_textboxCut is not null) _textboxCut.Text = "";
    }

    private void OnRefresh()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastRefreshTime < RefreshThrottleSecs)
        {
            GD.Print("[HudFriendWindow] Refresh throttled (3-min cooldown). " +
                     "spec: Docs/RE/specs/ui_system.md §8.14.");
            return;
        }

        _lastRefreshTime = now;
        GD.Print("[HudFriendWindow] action 18 = refresh → TODO(world-campaign): C2S 2/54. " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnConfirm()
    {
        GD.Print("[HudFriendWindow] action 14 = confirm (modal hint msg 100614). " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnTopBarAction(int actionId)
    {
        if (actionId == 17)
            GD.Print($"[HudFriendWindow] action 17 = legend (msg {MsgLegend}). " +
                     "spec: Docs/RE/specs/ui_system.md §8.14 CODE-CONFIRMED.");
        else
            GD.Print($"[HudFriendWindow] action {actionId} top-bar. " +
                     "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnRowSelect(int tab, int row)
    {
        GD.Print($"[HudFriendWindow] tab={tab} row={row} selected. " +
                 "TODO(debugger/capture): inbound friend feed (5/26 candidate). " +
                 "spec: Docs/RE/specs/ui_system.md §8.14.");
    }

    private void OnScroll(int direction)
    {
        GD.Print($"[HudFriendWindow] scroll direction={direction}.");
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;

        if (_open)
        {
            OffsetLeft = -FriendW;
            OffsetRight = 0f;
        }
        else
        {
            OffsetLeft = FriendW;
            OffsetRight = FriendW + FriendW;
        }

        Visible = _open;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is InputEventKey key && key.Pressed)
            switch (key.Keycode)
            {
                case Key.Escape:
                    _textboxAdd?.ReleaseFocus();
                    _textboxCut?.ReleaseFocus();
                    Toggle(false);
                    GetViewport().SetInputAsHandled();
                    break;

                case Key.Tab:
                    if (_activeTab == 0 && _textboxAdd is not null)
                    {
                        if (_textboxAdd.HasFocus()) _textboxAdd.ReleaseFocus();
                        else _textboxAdd.GrabFocus();
                    }
                    else if (_activeTab == 1 && _textboxCut is not null)
                    {
                        if (_textboxCut.HasFocus()) _textboxCut.ReleaseFocus();
                        else _textboxCut.GrabFocus();
                    }

                    GetViewport().SetInputAsHandled();
                    break;
            }
    }
}