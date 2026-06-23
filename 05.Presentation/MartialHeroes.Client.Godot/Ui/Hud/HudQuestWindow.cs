using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudQuestWindow : Control
{
    private const float QuestW = 318f;
    private const float QuestH = 732f;

    private const float RowBaseY = 44f;
    private const float RowStrideY = 31f;

    private const int ActiveRows = 6;
    private const int CompletableRows = 6;
    private const int AvailableRows = 10;

    private const int MainTexId = 8;
    private const int ThumbTexId = 1;
    private const int BottomTexId = 2;
    private const int DetailTexId = 9;

    private const int ActionAccept = 85;
    private const int ActionProceed = 86;
    private const int ActionGiveUp = 91;
    private const int ActionTracking = 94;

    private const int TabActiveSrcY = 66;
    private const int TabCompleteSrcY = 22;
    private const int TabAvailableSrcY = 44;

    private const int MsgActiveHeader = 18031;
    private int _activeTab;

    private Control? _activeTabContent;
    private Control? _availableTabContent;
    private Control? _completableTabContent;
    private Control? _detailTabContent;


    private bool _open;
    private bool _trackingEnabled;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudQuestWindow";

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

        BuildTabButtons(atlas, text);

        _activeTabContent = BuildQuestRows("ActiveTab", ActiveRows, 4, atlas);
        _completableTabContent = BuildQuestRows("CompletableTab", CompletableRows, 22, atlas);
        _availableTabContent = BuildQuestRows("AvailableTab", AvailableRows, 40, atlas);
        _detailTabContent = BuildDetailPanel(text);

        AddChild(_activeTabContent);
        AddChild(_completableTabContent);
        AddChild(_availableTabContent);
        AddChild(_detailTabContent);

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

        var acceptBtn = new Button
        {
            Name = "AcceptBtn",
            Text = "Accept",
            Position = new Vector2(49f, 658f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        acceptBtn.Pressed += OnAccept;
        AddChild(acceptBtn);

        var proceedBtn = new Button
        {
            Name = "ProceedBtn",
            Text = "Proceed",
            Position = new Vector2(148f, 658f),
            Size = new Vector2(113f, 40f),
            MouseFilter = MouseFilterEnum.Stop
        };
        proceedBtn.Pressed += OnProceed;
        AddChild(proceedBtn);

        var giveUpBtn = new Button
        {
            Name = "GiveUpBtn",
            Text = "Give Up",
            Position = new Vector2(259f, 655f),
            Size = new Vector2(59f, 77f),
            MouseFilter = MouseFilterEnum.Stop
        };
        giveUpBtn.Pressed += OnGiveUp;
        AddChild(giveUpBtn);

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
            _trackingEnabled = pressed;
            GD.Print($"[HudQuestWindow] action 94 = tracking checkbox → {pressed} (CHAR_QUEST_TRACKING). " +
                     "spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.");
        };
        AddChild(trackingCb);

        SelectTab(0);

        GD.Print("[HudQuestWindow] Built — 318×732 right-anchored QuestPanel. " +
                 "Tabs: ACTIVE (6 rows), COMPLETABLE (6 rows), AVAILABLE (10 rows), DETAIL. " +
                 "Row stride 31px, base y=44. " +
                 "Inbound: TODO(capture): quest list record bodies (5/68/5/73 opaque). " +
                 "Outbound: TODO(world-campaign): C2S 2/28 (give-up) + 2/152 (row request). " +
                 "spec: Docs/RE/specs/ui_system.md §8.16 CODE-CONFIRMED.");
    }

    private void BuildTabButtons(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        (float x, float y, float w, float h, int action, string label, int srcY)[] tabs =
        {
            (4f, 105f, 62f, 22f, 0, "Active", TabActiveSrcY),
            (66f, 105f, 62f, 22f, 1, "Complete", TabCompleteSrcY),
            (128f, 105f, 64f, 22f, 2, "Available", TabAvailableSrcY),
            (192f, 107f, 63f, 18f, 3, "Detail", 0)
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
        var container = new Control { Name = tabName };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        for (var r = 0; r < visibleRows; r++)
        {
            var baseY = RowBaseY + r * RowStrideY;

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

            var nameBtn = new Button
            {
                Name = $"{tabName}Name{r}",
                Text = "",
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
        var panel = new Control { Name = "DetailTab" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var detailBody = new Label
        {
            Name = "DetailBody",
            Text = "",
            Position = new Vector2(10f, 130f),
            Size = new Vector2(298f, 475f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(detailBody);

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


    private void SelectTab(int tab)
    {
        _activeTab = tab;

        if (_activeTabContent is not null) _activeTabContent.Visible = tab == 0;
        if (_completableTabContent is not null) _completableTabContent.Visible = tab == 1;
        if (_availableTabContent is not null) _availableTabContent.Visible = tab == 2;
        if (_detailTabContent is not null) _detailTabContent.Visible = tab == 3;
    }


    private void OnAccept()
    {
        GD.Print("[HudQuestWindow] action 85 = accept → TODO(world-campaign): C2S 2/28 CmsgQuestAction. " +
                 "spec: Docs/RE/specs/ui_system.md §8.16.");
    }

    private void OnProceed()
    {
        GD.Print("[HudQuestWindow] action 86 = proceed/track. " +
                 "spec: Docs/RE/specs/ui_system.md §8.16.");
    }

    private void OnGiveUp()
    {
        GD.Print("[HudQuestWindow] action 91 = give-up → TODO(world-campaign): C2S 2/28 SubAction=4. " +
                 "spec: Docs/RE/specs/ui_system.md §8.16.");
    }


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
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}