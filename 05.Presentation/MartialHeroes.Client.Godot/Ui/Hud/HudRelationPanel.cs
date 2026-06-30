using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudRelationPanel : Control
{
    private const float PanelW = 295f;
    private const float PanelH = 393f;
    private const float TabX = 0f;
    private const float TabW = 50f;
    private const float TabH = 55f;

    private const float NameTbX = 74f;
    private const float NameTbY = 74f;
    private const float NameTbW = 136f;
    private const float NameTbH = 21f;
    private const int NameMaxLen = 16;

    private const float MemberBaseY = 118f;
    private const float MemberStride = 21f;
    private const int MaxVisibleMembers = 6;

    private const float ConfirmX = 123f;
    private const float ConfirmY = 361f;
    private const float ConfirmW = 45f;
    private const float ConfirmH = 25f;
    private const float BotAX = 58f;
    private const float BotBX = 165f;
    private const float BotY = 337f;
    private const float BotW = 68f;
    private const float BotH = 20f;

    private const float PageUpX = 17f;
    private const float PageDownX = 241f;
    private const float PageBtnY = 303f;
    private const float PageBtnSize = 26f;

    private const float NumPageBaseX = 57f;
    private const float NumPageY = 309f;
    private const float NumPageW = 15f;
    private const float NumPageH = 14f;
    private const int NumPageCount = 10;

    private const double TimedActionCooldownSecs = 180.0;

    private const int MsgTeacherSetFail1 = 2089;
    private const int MsgTeacherSetFail2 = 10074;

    private static readonly float[] TabY = { 6f, 63f, 120f, 177f };

    private readonly Label[] _memberLabels = new Label[MaxVisibleMembers];
    private ClientContext? _ctx;
    private int _activeTab;
    private int _currentPage;
    private double _lastTimedAction = -TimedActionCooldownSecs;
    private LineEdit? _nameTextbox;


    private bool _open;
    private Label? _pageIndicator;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text, ClientContext? ctx)
    {
        _ctx = ctx;
        Name = "HudRelationPanel";

        Position = new Vector2(80f, 200f);
        Size = new Vector2(PanelW, PanelH);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var bd = new Panel { Name = "Backdrop" };
        bd.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.07f, 0.07f, 0.10f, 0.94f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.35f, 0.3f, 0.5f, 0.9f);
        bd.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(bd);

        var titleLbl = new Label
        {
            Name = "TitleLabel",
            Text = "Relations",
            Position = new Vector2(10f, 4f),
            Size = new Vector2(180f, 18f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(titleLbl);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(PanelW - 12f, 2f),
            Size = new Vector2(11f, 11f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => OnAction(16);
        AddChild(closeBtn);

        for (var t = 0; t < 4; t++)
        {
            var capturedT = t;
            var tabBtn = new Button
            {
                Name = $"Tab{t}",
                Text = t == 0 ? "Master" : t == 1 ? "Parent" : t == 2 ? "Spouse" : "Fate",
                Position = new Vector2(TabX, TabY[t]),
                Size = new Vector2(TabW, TabH),
                MouseFilter = MouseFilterEnum.Stop
            };
            tabBtn.Pressed += () => OnAction(19 + capturedT);
            AddChild(tabBtn);
        }

        _nameTextbox = new LineEdit
        {
            Name = "NameTextbox",
            PlaceholderText = "Name...",
            Position = new Vector2(NameTbX, NameTbY),
            Size = new Vector2(NameTbW, NameTbH),
            MaxLength = NameMaxLen,
            MouseFilter = MouseFilterEnum.Stop
        };
        _nameTextbox.TextSubmitted += _ => OnAction(9);
        AddChild(_nameTextbox);

        var nameOkBtn = new Button
        {
            Name = "NameOkBtn",
            Text = "OK",
            Position = new Vector2(235f, NameTbY),
            Size = new Vector2(45f, NameTbH),
            MouseFilter = MouseFilterEnum.Stop
        };
        nameOkBtn.Pressed += () => OnAction(10);
        AddChild(nameOkBtn);

        for (var r = 0; r < MaxVisibleMembers; r++)
        {
            var ry = MemberBaseY + r * MemberStride;
            var capturedR = r;

            var rowBtn = new Button
            {
                Name = $"MemberRow{r}",
                Text = "",
                Position = new Vector2(57f, ry),
                Size = new Vector2(185f, MemberStride - 2f),
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                MouseFilter = MouseFilterEnum.Stop
            };
            rowBtn.Pressed += () => OnAction(capturedR);
            AddChild(rowBtn);

            var rowLbl = new Label
            {
                Name = $"MemberLbl{r}",
                Text = "",
                Position = new Vector2(59f, ry + 2f),
                Size = new Vector2(183f, MemberStride - 6f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(rowLbl);
            _memberLabels[r] = rowLbl;

            var miniBtn = new Button
            {
                Name = $"MiniBtn{r}",
                Text = "•",
                Position = new Vector2(245f, ry),
                Size = new Vector2(25f, MemberStride - 2f),
                MouseFilter = MouseFilterEnum.Stop
            };
            miniBtn.Pressed += () => OnAction(34 + capturedR);
            AddChild(miniBtn);
        }

        var bigListBtn = new Button
        {
            Name = "BigListBtn",
            Text = "",
            Position = new Vector2(29f, 259f),
            Size = new Vector2(224f, 18f),
            Flat = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        bigListBtn.Pressed += () => OnAction(12);
        AddChild(bigListBtn);

        var secRow = new Button
        {
            Name = "SecondaryRow",
            Text = "",
            Position = new Vector2(16f, 94f),
            Size = new Vector2(185f, 18f),
            Flat = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        secRow.Pressed += () => OnAction(13);
        AddChild(secRow);

        var secMiniBtn = new Button
        {
            Name = "SecondaryMiniBtn",
            Text = "•",
            Position = new Vector2(206f, 96f),
            Size = new Vector2(59f, 14f),
            MouseFilter = MouseFilterEnum.Stop
        };
        secMiniBtn.Pressed += () => OnAction(40);
        AddChild(secMiniBtn);

        var pageUpBtn = new Button
        {
            Name = "PageUp",
            Text = "◄",
            Position = new Vector2(PageUpX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageUpBtn.Pressed += () => OnAction(7);
        AddChild(pageUpBtn);

        var pageDownBtn = new Button
        {
            Name = "PageDown",
            Text = "►",
            Position = new Vector2(PageDownX, PageBtnY),
            Size = new Vector2(PageBtnSize, PageBtnSize),
            MouseFilter = MouseFilterEnum.Stop
        };
        pageDownBtn.Pressed += () => OnAction(8);
        AddChild(pageDownBtn);

        _pageIndicator = new Label
        {
            Name = "PageIndicator",
            Text = "1",
            Position = new Vector2(17f, 340f),
            Size = new Vector2(50f, 13f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_pageIndicator);

        for (var p = 0; p < NumPageCount; p++)
        {
            var capturedP = p;
            var numPage = new Button
            {
                Name = $"NumPage{p}",
                Text = $"{p + 1}",
                Position = new Vector2(NumPageBaseX + 15f * p, NumPageY),
                Size = new Vector2(NumPageW, NumPageH),
                MouseFilter = MouseFilterEnum.Stop
            };
            numPage.Pressed += () => OnAction(23 + capturedP);
            AddChild(numPage);
        }

        var moreBtn = new Button
        {
            Name = "MoreBtn",
            Text = "...",
            Position = new Vector2(207f, NumPageY),
            Size = new Vector2(30f, NumPageH),
            MouseFilter = MouseFilterEnum.Stop
        };
        moreBtn.Pressed += () => OnAction(33);
        AddChild(moreBtn);

        var botA = new Button
        {
            Name = "BotBtnA",
            Text = "Info",
            Position = new Vector2(BotAX, BotY),
            Size = new Vector2(BotW, BotH),
            MouseFilter = MouseFilterEnum.Stop
        };
        botA.Pressed += () => OnAction(11);
        AddChild(botA);

        var botB = new Button
        {
            Name = "BotBtnB",
            Text = "Request",
            Position = new Vector2(BotBX, BotY),
            Size = new Vector2(BotW, BotH),
            MouseFilter = MouseFilterEnum.Stop
        };
        botB.Pressed += () => OnAction(14);
        AddChild(botB);

        var botC = new Button
        {
            Name = "BotBtnC",
            Text = "Set Master",
            Position = new Vector2(BotBX, BotY + BotH + 2f),
            Size = new Vector2(BotW, BotH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        botC.Pressed += () => OnAction(15);
        AddChild(botC);

        var confirmBtn = new Button
        {
            Name = "ConfirmBtn",
            Text = "Close",
            Position = new Vector2(ConfirmX, ConfirmY),
            Size = new Vector2(ConfirmW, ConfirmH),
            MouseFilter = MouseFilterEnum.Stop
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


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        if (_open) _activeTab = 0;
        Visible = _open;
        GD.Print($"[HudRelationPanel] Toggle → open={_open}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.28.5 CODE-CONFIRMED.");
    }


    private void OnAction(int action)
    {
        switch (action)
        {
            case >= 0 and <= 5:
                GD.Print($"[HudRelationPanel] Row {action} selected. " +
                         "TODO(capture): inbound roster. spec: ui_system.md §8.28.4.");
                break;

            case 6:
                Toggle(false);
                break;

            case 7:
                if (_currentPage > 0) _currentPage--;
                UpdatePageIndicator();
                GD.Print($"[HudRelationPanel] Page up → {_currentPage}. spec: ui_system.md §8.28.4.");
                break;

            case 8:
                _currentPage++;
                UpdatePageIndicator();
                GD.Print($"[HudRelationPanel] Page down → {_currentPage}. spec: ui_system.md §8.28.4.");
                break;

            case 9:
                GD.Print("[HudRelationPanel] Name submit (action 9). " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 10:
                var name = _nameTextbox?.Text.Trim() ?? "";
                if (!string.IsNullOrEmpty(name) && _ctx is not null)
                    _ = _ctx.UseCases.SendChatAsync(0, name);
                break;

            case 11:
                GD.Print("[HudRelationPanel] Action 11 = detail/info. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 12:
            case 13:
                GD.Print($"[HudRelationPanel] Action {action} (list/secondary — MED). " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 14:
                var now = global::Godot.Time.GetTicksMsec() / 1000.0;
                if (now - _lastTimedAction < TimedActionCooldownSecs) break;
                _lastTimedAction = now;
                break;

            case 15:
                GD.Print($"[HudRelationPanel] Action 15 = set teacher/master. " +
                         $"Fail notices: msg {MsgTeacherSetFail1}/{MsgTeacherSetFail2}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4 CODE-CONFIRMED.");
                break;

            case 16:
                Toggle(false);
                break;

            case >= 19 and <= 22:
                _activeTab = action - 19;
                GD.Print($"[HudRelationPanel] Tab {_activeTab} selected (action {action}). " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4 CODE-CONFIRMED.");
                break;

            case >= 23 and <= 33:
                if (action <= 32)
                    _currentPage = action - 23;
                UpdatePageIndicator();
                GD.Print($"[HudRelationPanel] Numeric page action {action} → page {_currentPage}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case >= 34 and <= 39:
                GD.Print($"[HudRelationPanel] Mini-button action {action} (row {action - 34}) — MED. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4.");
                break;

            case 40:
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


    public override void _Input(InputEvent @event)
    {
        if (!_open) return;
        if (@event is not InputEventKey key || !key.Pressed) return;

        switch (key.Keycode)
        {
            case Key.Escape:
                Toggle(false);
                GetViewport().SetInputAsHandled();
                break;

            case Key.Tab:
                _activeTab = (_activeTab + 1) % 4;
                GD.Print($"[HudRelationPanel] TAB key → tab {_activeTab}. " +
                         "spec: Docs/RE/specs/ui_system.md §8.28.4 CODE-CONFIRMED.");
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}