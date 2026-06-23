
using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudEmoticonWindow : Control
{

    private const float EmoW = 318f;
    private const float EmoH = 732f;

    private const float BackgroundX = 0f;
    private const float BackgroundY = 85f;
    private const float TopBarX = 0f;
    private const float TopBarY = 36f;
    private const float HeaderIconX = 125f;
    private const float HeaderIconY = 60f;
    private const float CloseBtnX = 286f;
    private const float CloseBtnY = 46f;
    private const float Tab0X = 10f;
    private const float TabY = 96f;
    private const float Tab1X = 159f;
    private const float BottomBtnX = 259f;
    private const float BottomBtnY = 655f;
    private const float InputBoxX = 60f;
    private const float InputBoxY = 80f;

    private const float GridPageX = 0f;
    private const float GridPageY = 127f;
    private const float GridPageW = 318f;
    private const float GridPageH = 605f;

    private const int Tex1 = 1;
    private const int Tex2 = 2;
    private const int Tex3 = 3;
    private const int Tex4 = 4;
    private const int Tex8 = 8;
    private const int Tex27 = 27;

    private const int BackSrcX = 318;
    private const int BackSrcY = 0;
    private const int TopBarSrcX = 0;
    private const int TopBarSrcY = 683;
    private const int HdrIconSrcX = 921;
    private const int HdrIconSrcY = 669;
    private const int CloseSrcX = 354;
    private const int CloseSrcY = 596;
    private const int ClosePrsSrcX = 354;
    private const int ClosePrsSrcY = 622;
    private const int Tab0NormX = 677;
    private const int Tab0NormY = 694;
    private const int Tab0PresX = 677;
    private const int Tab0PresY = 724;
    private const int Tab1NormX = 677;
    private const int Tab1NormY = 754;
    private const int Tab1PresX = 677;
    private const int Tab1PresY = 784;
    private const int BotBtnNormX = 301;
    private const int BotBtnNormY = 947;
    private const int BotBtnPrsX = 360;
    private const int BotBtnPrsY = 947;
    private const int InputSrcX = 518;
    private const int InputSrcY = 669;

    private const int MacroSlotCount = 9;
    private const long MacroRateLimitMs = 5000L;

    private readonly string?[] _macroSlots = new string?[MacroSlotCount];
    private int _activePage;

    private ClientContext? _ctx;

    private long _lastMacroSentMs;


    private bool _open;

    private Control? _page0Panel;
    private Control? _page1Panel;


    public void Build(HudAtlasLibrary atlas, ClientContext? ctx)
    {
        Name = "HudEmoticonWindow";
        _ctx = ctx;

        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -EmoW;
        OffsetRight = 0f;
        OffsetTop = 0f;
        OffsetBottom = EmoH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.97f);
        bdStyle.SetBorderWidthAll(1);
        bdStyle.BorderColor = new Color(0.45f, 0.35f, 0.15f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var tex2 = atlas?.GetById(Tex2);
        if (tex2 is not null)
        {
            var topBar = new TextureRect
            {
                Name = "TopBar",
                Texture = atlas!.SliceById(Tex2, TopBarSrcX, TopBarSrcY, (int)EmoW, 50),
                Position = new Vector2(TopBarX, TopBarY),
                Size = new Vector2(EmoW, 50f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(topBar);
        }

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Text = "×",
            Position = new Vector2(CloseBtnX, CloseBtnY),
            Size = new Vector2(29f, 26f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += () => Toggle(false);
        AddChild(closeBtn);

        var tab0 = new Button
        {
            Name = "Tab0",
            Text = "Chat Macros",
            Position = new Vector2(Tab0X, TabY),
            Size = new Vector2(149f, 29f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tab0.Pressed += () => SelectPage(0);
        AddChild(tab0);

        var tab1 = new Button
        {
            Name = "Tab1",
            Text = "Emotes",
            Position = new Vector2(Tab1X, TabY),
            Size = new Vector2(149f, 29f),
            MouseFilter = MouseFilterEnum.Stop
        };
        tab1.Pressed += () => SelectPage(1);
        AddChild(tab1);

        var botBtn = new Button
        {
            Name = "BottomBtn",
            Text = "≡",
            Position = new Vector2(BottomBtnX, BottomBtnY),
            Size = new Vector2(59f, 77f),
            MouseFilter = MouseFilterEnum.Stop
        };
        botBtn.Pressed += () => Toggle(false);
        AddChild(botBtn);

        var inputBox = new LineEdit
        {
            Name = "InputTextbox",
            MaxLength = 47,
            Position = new Vector2(InputBoxX, InputBoxY),
            Size = new Vector2(297f, 23f),
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(inputBox);

        _page0Panel = BuildMacroPage();
        AddChild(_page0Panel);

        _page1Panel = BuildEmotePageStub();
        AddChild(_page1Panel);

        SelectPage(0);

        GD.Print("[HudEmoticonWindow] Built — right-dock 318×732 EmoticonPanel (+0x370). " +
                 "Page 0: 9 macro slots (INI SHIFT_1..9 → chat C2S 2/7, rate 5000 ms). " +
                 "Page 1: graphical emote grid stub (emoticon.do 40-byte records, TODO(format)). " +
                 "spec: Docs/RE/specs/ui_system.md §8.19 CODE-CONFIRMED.");
    }


    private Control BuildMacroPage()
    {
        var panel = new Control
        {
            Name = "MacroPage",
            Position = new Vector2(GridPageX, GridPageY),
            Size = new Vector2(GridPageW, GridPageH)
        };

        for (var i = 0; i < MacroSlotCount; i++)
        {
            var capturedI = i;
            var slotBtn = new Button
            {
                Name = $"Macro{i + 1}",
                Text = $"SHIFT+{i + 1}",
                Position = new Vector2(10f, 5f + i * 34f),
                Size = new Vector2(92f, 29f),
                MouseFilter = MouseFilterEnum.Stop
            };
            slotBtn.Pressed += () => OnMacroPressed(capturedI);
            panel.AddChild(slotBtn);

            var phraseBtn = new Button
            {
                Name = $"Phrase{i + 1}",
                Text = _macroSlots[i] ?? string.Empty,
                Position = new Vector2(110f, 5f + i * 34f + 3f),
                Size = new Vector2(180f, 23f),
                MouseFilter = MouseFilterEnum.Stop
            };
            var capturedPhrase = i;
            phraseBtn.Pressed += () => OnMacroPressed(capturedPhrase);
            panel.AddChild(phraseBtn);
        }

        return panel;
    }


    private static Control BuildEmotePageStub()
    {
        var panel = new Control
        {
            Name = "EmotePage",
            Position = new Vector2(GridPageX, GridPageY),
            Size = new Vector2(GridPageW, GridPageH)
        };

        var stub = new Label
        {
            Name = "EmoteStub",
            Text = string.Empty,
            Position = new Vector2(5f, 5f),
            Size = new Vector2(308f, GridPageH - 10f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(stub);

        return panel;
    }


    private void SelectPage(int page)
    {
        _activePage = page;
        if (_page0Panel is not null) _page0Panel.Visible = page == 0;
        if (_page1Panel is not null) _page1Panel.Visible = page == 1;
    }


    private void OnMacroPressed(int slotIndex)
    {
        var nowMs = (long)Time.GetTicksMsec();
        if (nowMs - _lastMacroSentMs < MacroRateLimitMs)
        {
            GD.Print($"[HudEmoticonWindow] Macro rate-limited (5000 ms). slot={slotIndex}. " +
                     "spec: Docs/RE/specs/ui_system.md §8.19.4 CODE-CONFIRMED.");
            return;
        }

        var text = _macroSlots[slotIndex];
        if (string.IsNullOrEmpty(text)) return;

        _lastMacroSentMs = nowMs;

        if (_ctx is not null)
        {
            _ = _ctx.UseCases.SendChatAsync(0, text);
            GD.Print($"[HudEmoticonWindow] Macro slot {slotIndex} sent via chat C2S 2/7. " +
                     "spec: Docs/RE/specs/ui_system.md §8.19.4 CODE-CONFIRMED.");
        }
        else
        {
            GD.PrintErr("[HudEmoticonWindow] SendChatAsync: ctx is null (offline). Macro not sent.");
        }
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