using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Godot.Autoload;
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
    private const int InboxCap = 8;
    private LineEdit? _bodyBox;

    private ClientContext? _ctx;
    private bool _open;
    private Control? _readPanel;
    private bool _readPanelVisible;
    private LineEdit? _recipientBox;

    private Control? _sendPanel;
    private bool _sendPanelVisible;

    private readonly List<MailLetterArrivedEvent> _inbox = new(InboxCap);
    private ChannelReader<MailLetterArrivedEvent>? _mailLetters;
    private Label? _fromValue;
    private Label? _dateValue;
    private Label? _subjectValue;
    private Label? _bodyValue;
    private Label? _inboxList;
    private int _selectedLetter = -1;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text, ClientContext ctx)
    {
        _ctx = ctx;
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
                 "SendPanel: recipient max 16, body max 199, postage 200000. ReadPanel: reply textbox max 199. " +
                 "WIRED: Send btn -> UseCases.SendMailAsync -> 2/70 CmsgCarrierPigeonSend (attachmentless mode 4); " +
                 "panel close btns route locally (send + read). " +
                 "BLOCKED (left + flagged, no invention): inbox populate (no IHudEventHub mail/delivery ChannelReader " +
                 "+ window not bound by HudMaster.BindHub); reply (mail.md §6 refutes the 2/60 seed = marriage, §5 found " +
                 "no carrier-pigeon reply opcode, no reply use-case); attach (SendMailAsync has no item/money params). " +
                 "spec: Docs/RE/specs/mail.md §3/§4/§5/§6.");
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


    private Control BuildReadPanel(HudAtlasLibrary atlas, HudTextLibrary text)
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
        {
            panel.AddChild(new Label
            {
                Text = fieldNames[i],
                Position = new Vector2(8f, 34f + i * 22f),
                MouseFilter = MouseFilterEnum.Ignore
            });

            var value = new Label
            {
                Name = $"FieldValue{i}",
                Text = string.Empty,
                Position = new Vector2(70f, 34f + i * 22f),
                Size = new Vector2(282f, 18f),
                ClipText = true,
                MouseFilter = MouseFilterEnum.Ignore
            };
            panel.AddChild(value);
            switch (i)
            {
                case 0:
                    _fromValue = value;
                    break;
                case 1:
                    _dateValue = value;
                    break;
                case 2:
                    _subjectValue = value;
                    break;
                default:
                    _bodyValue = value;
                    break;
            }
        }

        _inboxList = new Label
        {
            Name = "InboxStub",
            Text = string.Empty,
            Position = new Vector2(8f, 130f),
            Size = new Vector2(344f, 60f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontSize = 9 },
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddChild(_inboxList);

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
            GD.Print("[HudMailWindow] Reply pressed → BLOCKED: no carrier-pigeon reply intent. governing spec " +
                     "mail.md §6 REFUTES the old '2/60 CmsgLetterRequest' seed (2/60 is the couple/marriage request, " +
                     "owned by social.md), and §5 recovered NO carrier-pigeon reply/receive opcode distinct from 4/70. " +
                     "IApplicationUseCases has no reply use-case, and the addressee would need inbox data that has no " +
                     "wired source — left unrouted, no invention. spec: Docs/RE/specs/mail.md §5/§6.");
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
        closeBtn.Pressed += HideReadPanel;
        panel.AddChild(closeBtn);

        return panel;
    }


    public void BindHub(IHudEventHub hub)
    {
        _mailLetters = hub.MailLetters;
        GD.Print("[HudMailWindow] BindHub: MailLetters channel connected — carrier-pigeon receive " +
                 "(SmsgSrvLetterReceived) now feeds the read-panel inbox (sender/date/subject/body all carried). " +
                 "spec: Docs/RE/specs/mail.md §3/§5.");
    }

    public override void _Process(double delta)
    {
        if (_mailLetters is null) return;

        var changed = false;
        while (_mailLetters.TryRead(out var letter))
            if (letter is not null)
            {
                if (_inbox.Count >= InboxCap) _inbox.RemoveAt(0);
                _inbox.Add(letter);
                changed = true;
            }

        if (!changed) return;

        _selectedLetter = _inbox.Count - 1;
        RenderInbox();
        RenderSelectedLetter();
    }

    private void RenderInbox()
    {
        if (_inboxList is null) return;

        var lines = new string[_inbox.Count];
        for (var i = 0; i < _inbox.Count; i++)
            lines[i] = $"{i + 1}. {_inbox[i].Sender} — {_inbox[i].Subject}";
        _inboxList.Text = string.Join("\n", lines);
    }

    private void RenderSelectedLetter()
    {
        if (_selectedLetter < 0 || _selectedLetter >= _inbox.Count) return;

        var letter = _inbox[_selectedLetter];
        if (_fromValue is not null) _fromValue.Text = letter.Sender;
        if (_dateValue is not null) _dateValue.Text = letter.Date;
        if (_subjectValue is not null) _subjectValue.Text = letter.Subject;
        if (_bodyValue is not null) _bodyValue.Text = letter.Body;
    }

    private void OnOpenReadPanel()
    {
        _sendPanelVisible = false;
        _readPanelVisible = true;
        if (_sendPanel is not null) _sendPanel.Visible = false;
        if (_readPanel is not null) _readPanel.Visible = true;
        GD.Print("[HudMailWindow] Read panel opened. " +
                 "Inbox populate BLOCKED: GamePacketHandler.Mail publishes MailLetterArrivedEvent (carrier-pigeon " +
                 "receive) + DeliveryRecordUpdatedEvent (4/70) on IClientEventBus, but IHudEventHub exposes NO mail/" +
                 "delivery ChannelReader and HudMaster.BindHub does not bind this window, so the 8-slot inbox has no " +
                 "wired source — panel stays empty (no mock data). spec: Docs/RE/specs/mail.md §3/§5.");
    }

    private void HideReadPanel()
    {
        _readPanelVisible = false;
        if (_readPanel is not null) _readPanel.Visible = false;
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
            GD.PrintErr($"[HudMailWindow] Send: empty recipient (msg.xdb {Msg52001}).");
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            GD.PrintErr($"[HudMailWindow] Send: empty body (msg.xdb {Msg52002}).");
            return;
        }

        if (_ctx is not null)
            _ = _ctx.UseCases.SendMailAsync(recipient, body);
    }

    private void OnAttachItem()
    {
        GD.Print("[HudMailWindow] Attach item: BLOCKED: no attach intent. IApplicationUseCases.SendMailAsync emits " +
                 "2/70 CmsgCarrierPigeonSend in attachmentless mode 4 (all five item handles forced to 0xFFFFFFFF), " +
                 "and exposes no recipient-money / item-slot parameters, so the compose panel's attach slots have no " +
                 "use-case to route to — left unrouted, no invention. spec: Docs/RE/specs/mail.md §4.1.");
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