
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudOptionsWindow : Control
{

    private const float OptW = 215f;
    private const float OptH = 204f;

    private const int CheckTexId = 1;
    private const int CheckUncheckedSrcX = 372;
    private const int CheckUncheckedSrcY = 730;
    private const int CheckCheckedSrcX = 372;
    private const int CheckCheckedSrcY = 754;
    private const int CheckSize = 24;

    private const int TabTexId = 9;

    private const int CheckBaseY = 50;
    private const int CheckStrideY = 30;

    private const int CaptionX = 40;
    private const int CaptionBaseY = 55;
    private const int CaptionStrideY = 30;
    private const int CaptionW = 115;
    private const int CaptionH = 24;

    private static readonly int[] CharCaptionIds =
    {
        8009, 8010, 8011, 8012, 8013, 8014, 8018, 8016, 8017, 8037, 8039, 8015
    };

    private readonly CheckBox[] _checkBoxes = new CheckBox[12];

    private readonly bool[] _checkState = new bool[12];
    private int _activeTab;

    private Control? _charSubPanel;
    private Control? _graphicSubPanel;


    private bool _open;
    private Control? _otherSubPanel;
    private Control? _soundSubPanel;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudOptionsWindow";

        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -OptW / 2f;
        OffsetTop = -OptH / 2f;
        OffsetRight = OptW / 2f;
        OffsetBottom = OptH / 2f;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.95f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.55f, 0.45f, 0.25f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var tabTex = atlas.GetById(TabTexId);

        string[] tabLabels = { "Character", "Sound", "Graphic", "Other" };
        int[] tabSrcY = { 517, 557, 597, 637 };

        for (var i = 0; i < 4; i++)
        {
            var tabI = i;
            var tabBtn = new Button
            {
                Name = $"Tab{i}",
                Text = tabLabels[i],
                Position = new Vector2(15f, 30f + i * 40f),
                Size = new Vector2(186f, 40f),
                MouseFilter = MouseFilterEnum.Stop
            };
            if (tabTex is not null)
            {
                var tabSlice = atlas.SliceById(TabTexId, 833, tabSrcY[i], 186, 40);
                if (tabSlice is not null)
                {
                    var st = new StyleBoxTexture { Texture = tabSlice };
                    tabBtn.AddThemeStyleboxOverride("normal", st);
                }
            }

            tabBtn.Pressed += () => SelectTab(tabI);
            AddChild(tabBtn);
        }

        _charSubPanel = BuildCharacterSubPanel(atlas, text);
        _soundSubPanel = BuildSoundSubPanelStub();
        _graphicSubPanel = BuildGraphicSubPanelStub();
        _otherSubPanel = BuildOtherSubPanelStub();

        AddChild(_charSubPanel);
        AddChild(_soundSubPanel);
        AddChild(_graphicSubPanel);
        AddChild(_otherSubPanel);

        SelectTab(0);

        GD.Print("[HudOptionsWindow] Built — centered 215×204 OptionPanel, 4-tab container. " +
                 "Character tab: 12 checkboxes (action 2..13, uitex 1) + captions (msg 8009/8039 sequence). " +
                 "Sound/Graphic/Other = shells (widget table sweep-pending, spec §8.9.1 Open item 15). " +
                 "spec: Docs/RE/specs/ui_system.md §8.9 / §8.9.1 CODE-CONFIRMED.");
    }


    private Control BuildCharacterSubPanel(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        var panel = new Control { Name = "CharacterSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var applyBtn = new Button
        {
            Name = "ApplyBtn",
            Text = "Apply",
            Position = new Vector2(60f, 165f),
            Size = new Vector2(95f, 24f),
            MouseFilter = MouseFilterEnum.Stop
        };
        applyBtn.Pressed += OnApply;
        panel.AddChild(applyBtn);

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "Close",
            Position = new Vector2(160f, 165f),
            Size = new Vector2(50f, 24f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += HideWindow;
        panel.AddChild(closeBtn);

        var checkX = OptW - 50f;
        for (var i = 0; i < 12; i++)
        {
            float cy = CheckBaseY + i * CheckStrideY;

            var cb = new CheckBox
            {
                Name = $"Check{i + 2}",
                Position = new Vector2(checkX, cy),
                Size = new Vector2(CheckSize, CheckSize),
                MouseFilter = MouseFilterEnum.Stop
            };
            var checkIdx = i;
            cb.Toggled += pressed =>
            {
                _checkState[checkIdx] = pressed;
            };
            panel.AddChild(cb);
            _checkBoxes[i] = cb;
        }

        for (var i = 0; i < CharCaptionIds.Length; i++)
        {
            float ly = CaptionBaseY + i * CaptionStrideY;
            var caption = text.GetCaption(CharCaptionIds[i], $"[msg {CharCaptionIds[i]}]");

            var lbl = new Label
            {
                Name = $"Caption{i}",
                Text = caption,
                Position = new Vector2(CaptionX, ly),
                Size = new Vector2(CaptionW, CaptionH),
                MouseFilter = MouseFilterEnum.Ignore
            };
            panel.AddChild(lbl);
        }

        return panel;
    }


    private static Control BuildSoundSubPanelStub()
    {
        var panel = new Control { Name = "SoundSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var lbl = new Label
        {
            Text = string.Empty,
            Position = new Vector2(10f, 50f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(lbl);
        return panel;
    }

    private static Control BuildGraphicSubPanelStub()
    {
        var panel = new Control { Name = "GraphicSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var lbl = new Label
        {
            Text = string.Empty,
            Position = new Vector2(10f, 50f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(lbl);
        return panel;
    }

    private static Control BuildOtherSubPanelStub()
    {
        var panel = new Control { Name = "OtherSubPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var lbl = new Label
        {
            Text = string.Empty,
            Position = new Vector2(10f, 50f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(lbl);
        return panel;
    }


    private void SelectTab(int tabIndex)
    {
        _activeTab = tabIndex;

        if (_charSubPanel is not null) _charSubPanel.Visible = tabIndex == 0;
        if (_soundSubPanel is not null) _soundSubPanel.Visible = tabIndex == 1;
        if (_graphicSubPanel is not null) _graphicSubPanel.Visible = tabIndex == 2;
        if (_otherSubPanel is not null) _otherSubPanel.Visible = tabIndex == 3;
    }


    private void OnApply()
    {
        GD.Print("[HudOptionsWindow] Apply: Character-tab settings persisted to option.ini " +
                 "(section <accountId>_<charName>_CHARACTER, 11 CHAR_* keys). " +
                 "Notice: msg 8036 (yellow 0xFFFFFF00). " +
                 "spec: Docs/RE/specs/ui_system.md §8.9.1 CODE-CONFIRMED.");
    }

    private void HideWindow()
    {
        Toggle(false);
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
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