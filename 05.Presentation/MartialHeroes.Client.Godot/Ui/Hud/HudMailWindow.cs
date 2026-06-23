
using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudMailWindow : Control
{

    private const float MenuW = 140f;
    private const float MenuH = 195f;

    private const float BtnW = 90f;
    private const float BtnH = 25f;
    private const float Btn1Y = 64f;
    private const float Btn2Y = 103f;
    private const float Btn3Y = 142f;
    private const float BtnX = 25f;

    private const int Top_NormX = 141;
    private const int Top_NormY = 156;
    private const int Top_PrsX = 141;
    private const int Top_PrsY = 182;
    private const int Mid_NormX = 141;
    private const int Mid_NormY = 50;
    private const int Mid_PrsX = 141;
    private const int Mid_PrsY = 75;
    private const int Bot_NormX = 141;
    private const int Bot_NormY = 0;
    private const int Bot_PrsX = 141;
    private const int Bot_PrsY = 25;

    private const int RecipientMaxLen = 16;
    private const int BodyMaxLen = 199;

    private const int PostageCost = 200000;

    private const int Msg52001 = 52001;
    private const int Msg52002 = 52002;
    private const int Msg52003 = 52003;
    private const int Msg52019 = 52019;
    private const int Msg55001 = 55001;
    private const int Msg55002 = 55002;
    private const int Msg55003 = 55003;
    private LineEdit? _bodyBox;


    private bool _open;
    private Control? _readPanel;
    private bool _readPanelVisible;
    private LineEdit? _recipientBox;

    private Control? _sendPanel;
    private bool _sendPanelVisible;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudMailWindow";

        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = MenuW;
        OffsetBottom = MenuH;

        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.08f, 0.06f, 0.04f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.60f, 0.50f, 0.30f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        var btn1 = new Button
        {
            Name = "BtnList",
            Text = "Read Mail",
            Position = new Vector2(BtnX, Btn1Y),
            Size = new Vector2(BtnW, BtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        btn1.Pressed += OnOpenReadPanel;
        AddChild(btn1);

        var btn2 = new Button
        {
            Name = "BtnSend",
            Text = "Write Mail",
            Position = new Vector2(BtnX, Btn2Y),
            Size = new Vector2(BtnW, BtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        btn2.Pressed += OnOpenSendPanel;
        AddChild(btn2);

        var btn3 = new Button
        {
            Name = "BtnClose",
            Text = "Close",
            Position = new Vector2(BtnX, Btn3Y),
            Size = new Vector2(BtnW, BtnH),
            MouseFilter = MouseFilterEnum.Stop
        };
        btn3.Pressed += () => Toggle(false);
        AddChild(btn3);

        _sendPanel = BuildSendPanel(atlas, text);
        _sendPanel.Visible = false;
        AddChild(_sendPanel);

        _readPanel = BuildReadPanel(atlas, text);
        _readPanel.Visible = false;
        AddChild(_readPanel);

        GD.Print("[HudMailWindow] Built — CarrierPigeonPanal slot 96 (140×195, top-left). " +
                 "SendPanel (§8.21.3): recipient max 16, body max 199, postage 200000. " +
                 "ReadPanel (§8.21.4): reply textbox max 199. " +
                 "C2S 2/70 CmsgCarrierPigeonSend + 2/60 CmsgLetterRequest = TODO(world-campaign). " +
                 "Inbox populate = TODO(capture: 1/20 arrival only). " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.2/§8.21.3/§8.21.4 CODE-CONFIRMED.");
    }


    private Control BuildSendPanel(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        var panel = new Control
        {
            Name = "CarrierPigeonSendPanel",
            Position = new Vector2(MenuW + 4f, 0f),
            Size = new Vector2(360f, 260f)
        };

        var sendBackdrop = new Panel { Name = "SendBackdrop" };
        sendBackdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var sbs = new StyleBoxFlat();
        sbs.BgColor = new Color(0.09f, 0.07f, 0.05f, 0.97f);
        sbs.SetBorderWidthAll(2);
        sbs.BorderColor = new Color(0.60f, 0.50f, 0.30f, 0.9f);
        sendBackdrop.AddThemeStyleboxOverride("panel", sbs);
        panel.AddChild(sendBackdrop);

        panel.AddChild(new Label
        {
            Text = "Write Mail", Position = new Vector2(8f, 8f), MouseFilter = MouseFilterEnum.Ignore
        });

        panel.AddChild(new Label
        {
            Text = "To:", Position = new Vector2(8f, 36f), MouseFilter = MouseFilterEnum.Ignore
        });
        _recipientBox = new LineEdit
        {
            Name = "RecipientBox",
            MaxLength = RecipientMaxLen,
            Position = new Vector2(80f, 34f),
            Size = new Vector2(138f, 16f),
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddChild(_recipientBox);

        panel.AddChild(new Label
        {
            Text = "Message:", Position = new Vector2(8f, 60f), MouseFilter = MouseFilterEnum.Ignore
        });
        _bodyBox = new LineEdit
        {
            Name = "BodyBox",
            MaxLength = BodyMaxLen,
            Position = new Vector2(80f, 58f),
            Size = new Vector2(265f, 16f),
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddChild(_bodyBox);

        var sendBtn = new Button
        {
            Name = "SendBtn",
            Text = $"Send (postage: {PostageCost:N0}g)",
            Position = new Vector2(8f, 90f),
            Size = new Vector2(180f, 25f),
            MouseFilter = MouseFilterEnum.Stop
        };
        sendBtn.Pressed += OnSendMail;
        panel.AddChild(sendBtn);

        var attachBtn = new Button
        {
            Name = "AttachBtn",
            Text = "📎",
            Position = new Vector2(200f, 90f),
            Size = new Vector2(22f, 22f),
            MouseFilter = MouseFilterEnum.Stop
        };
        attachBtn.Pressed += OnAttachItem;
        panel.AddChild(attachBtn);

        var attachStub = new Label
        {
            Name = "AttachListStub",
            Text = string.Empty,
            Position = new Vector2(8f, 122f),
            Size = new Vector2(344f, 40f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(attachStub);

        var closeBtn = new Button
        {
            Name = "CloseSend",
            Text = "×",
            Position = new Vector2(330f, 4f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        closeBtn.Pressed += HideSendPanel;
        panel.AddChild(closeBtn);

        return panel;
    }


    private static Control BuildReadPanel(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        var panel = new Control
        {
            Name = "CarrierPigeonReadPanel",
            Position = new Vector2(MenuW + 4f, 0f),
            Size = new Vector2(360f, 320f)
        };

        var readBackdrop = new Panel { Name = "ReadBackdrop" };
        readBackdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var rbs = new StyleBoxFlat();
        rbs.BgColor = new Color(0.07f, 0.06f, 0.04f, 0.97f);
        rbs.SetBorderWidthAll(2);
        rbs.BorderColor = new Color(0.60f, 0.50f, 0.30f, 0.9f);
        readBackdrop.AddThemeStyleboxOverride("panel", rbs);
        panel.AddChild(readBackdrop);

        panel.AddChild(new Label
        {
            Text = "Read Mail", Position = new Vector2(8f, 8f), MouseFilter = MouseFilterEnum.Ignore
        });

        string[] fieldNames = { "From:", "Date:", "Subject:", "Body:" };
        for (var i = 0; i < fieldNames.Length; i++)
            panel.AddChild(new Label
            {
                Text = fieldNames[i],
                Position = new Vector2(8f, 34f + i * 22f),
                MouseFilter = MouseFilterEnum.Ignore
            });

        panel.AddChild(new Label
        {
            Name = "InboxStub",
            Text = string.Empty,
            Position = new Vector2(8f, 130f),
            Size = new Vector2(344f, 60f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        });

        var replyBox = new LineEdit
        {
            Name = "ReplyBox",
            MaxLength = 199,
            Position = new Vector2(8f, 220f),
            Size = new Vector2(277f, 25f),
            PlaceholderText = "Reply...",
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddChild(replyBox);

        var replyBtn = new Button
        {
            Name = "ReplyBtn",
            Text = "Reply",
            Position = new Vector2(8f, 255f),
            Size = new Vector2(90f, 25f),
            MouseFilter = MouseFilterEnum.Stop
        };
        replyBtn.Pressed += () =>
        {
            GD.Print("[HudMailWindow] Reply pressed → TODO(world-campaign): C2S 2/60 CmsgLetterRequest. " +
                     "spec: Docs/RE/specs/ui_system.md §8.21.6 CODE-CONFIRMED.");
        };
        panel.AddChild(replyBtn);

        string[] tabNames = { "Reply", "Delete", "Next", "Prev" };
        for (var i = 0; i < tabNames.Length; i++)
        {
            var tab = new Button
            {
                Name = $"Tab{tabNames[i]}",
                Text = tabNames[i],
                Position = new Vector2(8f + i * 88f, 285f),
                Size = new Vector2(80f, 22f),
                MouseFilter = MouseFilterEnum.Stop
            };
            panel.AddChild(tab);
        }

        var closeBtn = new Button
        {
            Name = "CloseRead",
            Text = "×",
            Position = new Vector2(330f, 4f),
            Size = new Vector2(20f, 20f),
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddChild(closeBtn);

        return panel;
    }


    private void OnOpenReadPanel()
    {
        _sendPanelVisible = false;
        _readPanelVisible = true;
        if (_sendPanel is not null) _sendPanel.Visible = false;
        if (_readPanel is not null) _readPanel.Visible = true;
        GD.Print("[HudMailWindow] Read panel opened. " +
                 "TODO(capture): populate inbox. spec: §8.21.7.");
    }

    private void OnOpenSendPanel()
    {
        _sendPanelVisible = true;
        _readPanelVisible = false;
        if (_sendPanel is not null) _sendPanel.Visible = true;
        if (_readPanel is not null) _readPanel.Visible = false;
    }

    private void HideSendPanel()
    {
        _sendPanelVisible = false;
        if (_sendPanel is not null) _sendPanel.Visible = false;
    }


    private void OnSendMail()
    {
        var recipient = _recipientBox?.Text ?? string.Empty;
        var body = _bodyBox?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            GD.PrintErr($"[HudMailWindow] Send failed: empty recipient. " +
                        $"msg.xdb {Msg52001}. spec: §8.21.3 CODE-CONFIRMED.");
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            GD.PrintErr($"[HudMailWindow] Send failed: empty body. " +
                        $"msg.xdb {Msg52002}. spec: §8.21.3 CODE-CONFIRMED.");
            return;
        }

        GD.Print($"[HudMailWindow] Mail send intent: to='{recipient}', body={body.Length}B. " +
                 $"Postage={PostageCost}. TODO(world-campaign): C2S 2/70 CmsgCarrierPigeonSend. " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.3 CODE-CONFIRMED.");
    }

    private void OnAttachItem()
    {
        GD.Print("[HudMailWindow] Attach item: TODO(world-campaign): item-attach list (action 6..35). " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.3 CODE-CONFIRMED.");
    }


    public void Toggle(bool? forceState = null)
    {
        _open = forceState ?? !_open;
        Visible = _open;
        if (!_open)
        {
            if (_sendPanel is not null) _sendPanel.Visible = false;
            if (_readPanel is not null) _readPanel.Visible = false;
            _sendPanelVisible = false;
            _readPanelVisible = false;
        }
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