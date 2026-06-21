// Ui/Hud/HudMailWindow.cs
//
// In-game CarrierPigeonPanal — mailbox menu (slot 96) + CarrierPigeonReadPanel (slot 98).
// Note: "Panal" is the binary's exact class name (misspelling preserved).
//   spec: Docs/RE/specs/ui_system.md §8.21.2 — "CarrierPigeonPanal is the binary's class name"
//
// CarrierPigeonPanal (slot 96) — top-left (0,0) 140×195, atlas carrierpigeon.dds.
//   Three stacked 3-state buttons (90×25):
//     top    (25,64)  action 1 = open mail LIST / receive
//     middle (25,103) action 2 = open WRITE/SEND panel
//     bottom (25,142) action 3 = close
//   Esc closes.
//   spec: Docs/RE/specs/ui_system.md §8.21.2 CODE-CONFIRMED.
//
// CarrierPigeonSendPanel — compose sub-window (parent-relative).
//   Atlases: CarrierPigeonAll.dds / CarrierPigeonPerson.dds.
//   Recipient textbox (138×16, max 16), body textbox (265×16, max 199).
//   Send button action 1 (deferred-confirm → C2S CmsgCarrierPigeonSend 2/70).
//   Attach-item button action 2; up to 30 rows (action 6..35); list scrollbar 3/4/5.
//   TAB toggles focus between textboxes.
//   Postage = 200000 gold (literal constant).
//   Captions: msg 52001/52002/52003/52019; 55001/55002/55003.
//   spec: Docs/RE/specs/ui_system.md §8.21.3 CODE-CONFIRMED.
//
// CarrierPigeonReadPanel (slot 98) — view sub-window.
//   Atlases: CarrierPigeonAll.dds / CarrierPigeonPerson.dds.
//   Reply textbox (277×25, max 199), OK/reply button action 1.
//   Four tab buttons: Reply / Delete / Next / Prev.
//   spec: Docs/RE/specs/ui_system.md §8.21.4 CODE-CONFIRMED.
//
// Network (CODE-CONFIRMED):
//   Send:    C2S CmsgCarrierPigeonSend (2/70, 132B) — deferred-confirm.
//   Receive: C2S CmsgLetterRequest (2/60, 8B) — open/read/acknowledge a specific letter.
//   Arrival: S2C SmsgSrvLetterReceived (1/20) — notification only (not inbox list).
//   Inbox list populate: S2C opcode unknown (TODO(capture): mailbox populate).
//   spec: Docs/RE/specs/ui_system.md §8.21.2-4 CODE-CONFIRMED.
//
// PASSIVE: zero game logic. Network sends are use-case calls (stubbed; no method exists yet).

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

/// <summary>
///     In-game mailbox window family: CarrierPigeonPanal (slot 96) + CarrierPigeonReadPanel (slot 98).
///     <para>
///         PASSIVE: slot 96 is the menu; slot 98 is the read/compose surface.
///         All network sends are stub TODOs (no use-case method yet).
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.21.2/§8.21.3/§8.21.4 CODE-CONFIRMED.
///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 96 / slot 98.
/// </summary>
public sealed partial class HudMailWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/ui_system.md §8.21.2 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    // CarrierPigeonPanal geometry (slot 96)
    private const float MenuW = 140f; // spec: §8.21.2 — W=140
    private const float MenuH = 195f; // spec: §8.21.2 — H=195

    // Button geometry (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.2 — three 3-state buttons, all 90×25
    private const float BtnW = 90f; // spec: §8.21.2 — W=90
    private const float BtnH = 25f; // spec: §8.21.2 — H=25
    private const float Btn1Y = 64f; // spec: §8.21.2 — top button Y
    private const float Btn2Y = 103f; // spec: §8.21.2 — middle button Y
    private const float Btn3Y = 142f; // spec: §8.21.2 — bottom button Y
    private const float BtnX = 25f; // spec: §8.21.2 — all buttons X=25

    // Atlas button src-rects (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.2 — NORMAL/PRESSED pairs
    private const int Top_NormX = 141;
    private const int Top_NormY = 156; // top btn NORMAL/HOVER
    private const int Top_PrsX = 141;
    private const int Top_PrsY = 182; // top btn PRESSED
    private const int Mid_NormX = 141;
    private const int Mid_NormY = 50; // middle btn NORMAL/HOVER
    private const int Mid_PrsX = 141;
    private const int Mid_PrsY = 75; // middle btn PRESSED
    private const int Bot_NormX = 141;
    private const int Bot_NormY = 0; // bottom btn NORMAL/HOVER
    private const int Bot_PrsX = 141;
    private const int Bot_PrsY = 25; // bottom btn PRESSED

    // CarrierPigeonSendPanel textbox limits (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.3 CODE-CONFIRMED
    private const int RecipientMaxLen = 16; // spec: §8.21.3 — recipient max length 16
    private const int BodyMaxLen = 199; // spec: §8.21.3 — body max length 199

    // Postage literal (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.3 — "gold ≥ 200000 postage (else ...)"
    private const int PostageCost = 200000; // spec: §8.21.3 CODE-CONFIRMED

    // msg.xdb caption ids (CODE-CONFIRMED)
    // spec: ui_system.md §8.21.3 CODE-CONFIRMED
    private const int Msg52001 = 52001; // recipient-empty spec: §8.21.3
    private const int Msg52002 = 52002; // body-empty      spec: §8.21.3
    private const int Msg52003 = 52003; // spec: §8.21.3
    private const int Msg52019 = 52019; // postage-short   spec: §8.21.3
    private const int Msg55001 = 55001; // postage format  spec: §8.21.3
    private const int Msg55002 = 55002; // spec: §8.21.3
    private const int Msg55003 = 55003; // spec: §8.21.3
    private LineEdit? _bodyBox;

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private bool _open;
    private Control? _readPanel;
    private bool _readPanelVisible;
    private LineEdit? _recipientBox;

    // Sub-panel nodes
    private Control? _sendPanel;
    private bool _sendPanelVisible;

    // -------------------------------------------------------------------------
    // Build
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Geometry pass: builds the 140×195 mailbox menu at (0,0) with send + read sub-panels.
    ///     spec: Docs/RE/specs/ui_system.md §8.21.2/§8.21.3/§8.21.4 CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/ui_hud_layout.md §5.13 — slot 96 / slot 98.
    /// </summary>
    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudMailWindow";

        // Absolute top-left origin (0,0) 140×195.
        // spec: ui_system.md §8.21.2 — "top-left (0,0) 140×195"
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

        // Backdrop (art from carrierpigeon.dds when VFS present)
        var backdrop = new Panel { Name = "Backdrop" };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bdStyle = new StyleBoxFlat();
        bdStyle.BgColor = new Color(0.08f, 0.06f, 0.04f, 0.97f);
        bdStyle.SetBorderWidthAll(2);
        bdStyle.BorderColor = new Color(0.60f, 0.50f, 0.30f, 0.9f);
        backdrop.AddThemeStyleboxOverride("panel", bdStyle);
        AddChild(backdrop);

        // Top button: open mail LIST / receive (action 1)
        // spec: ui_system.md §8.21.2 — "(25,64) action 1 = open LIST/receive"
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

        // Middle button: open WRITE/SEND panel (action 2)
        // spec: ui_system.md §8.21.2 — "(25,103) action 2 = open WRITE/SEND panel"
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

        // Bottom button: close (action 3)
        // spec: ui_system.md §8.21.2 — "(25,142) action 3 = close"
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

        // CarrierPigeonSendPanel — compose sub-window
        // spec: ui_system.md §8.21.3 CODE-CONFIRMED
        _sendPanel = BuildSendPanel(atlas, text);
        _sendPanel.Visible = false;
        AddChild(_sendPanel);

        // CarrierPigeonReadPanel (slot 98) — read sub-window
        // spec: ui_system.md §8.21.4 CODE-CONFIRMED
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

    // -------------------------------------------------------------------------
    // SendPanel builder
    // spec: Docs/RE/specs/ui_system.md §8.21.3 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private Control BuildSendPanel(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        // Parent-relative; positioned beside/below the menu
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

        // Title
        panel.AddChild(new Label
        {
            Text = "Write Mail", Position = new Vector2(8f, 8f), MouseFilter = MouseFilterEnum.Ignore
        });

        // Recipient textbox (138×16, max 16, focus action 20)
        // spec: ui_system.md §8.21.3 — "Recipient-name textbox (138×16), max length 16, focus action 20"
        panel.AddChild(new Label
        {
            Text = "To:", Position = new Vector2(8f, 36f), MouseFilter = MouseFilterEnum.Ignore
        });
        _recipientBox = new LineEdit
        {
            Name = "RecipientBox",
            MaxLength = RecipientMaxLen, // spec: §8.21.3 — max 16
            Position = new Vector2(80f, 34f),
            Size = new Vector2(138f, 16f),
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddChild(_recipientBox);

        // Body textbox (265×16, max 199, multiline, focus/edit action 21)
        // spec: ui_system.md §8.21.3 — "Message-body textbox (265×16), max length 199, multiline"
        panel.AddChild(new Label
        {
            Text = "Message:", Position = new Vector2(8f, 60f), MouseFilter = MouseFilterEnum.Ignore
        });
        _bodyBox = new LineEdit
        {
            Name = "BodyBox",
            MaxLength = BodyMaxLen, // spec: §8.21.3 — max 199
            Position = new Vector2(80f, 58f),
            Size = new Vector2(265f, 16f),
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddChild(_bodyBox);

        // Send button (action 1) — deferred-confirm → C2S 2/70
        // spec: ui_system.md §8.21.3 — "Send 3-state button (90×25), action 1"
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

        // Attach-item button (action 2, 22×22)
        // spec: ui_system.md §8.21.3 — "Attach-item button (22×22), action 2"
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

        // Attachment list stub (up to 30 rows, action 6..35)
        // spec: ui_system.md §8.21.3 — "opening an item-attach list of up to 30 rows (action 6+row)"
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

        // Close button
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

    // -------------------------------------------------------------------------
    // ReadPanel builder
    // spec: Docs/RE/specs/ui_system.md §8.21.4 CODE-CONFIRMED
    // -------------------------------------------------------------------------

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

        // Sender / date / subject / body labels (stub)
        // spec: ui_system.md §8.21.4 — "shows sender / date / subject / body labels"
        string[] fieldNames = { "From:", "Date:", "Subject:", "Body:" };
        for (var i = 0; i < fieldNames.Length; i++)
            panel.AddChild(new Label
            {
                Text = fieldNames[i],
                Position = new Vector2(8f, 34f + i * 22f),
                MouseFilter = MouseFilterEnum.Ignore
            });

        // Inbox list stub — populate via S2C (opcode unknown; TODO capture)
        // spec: ui_system.md §8.21.7 — "S2C populate for mailbox list not walked (TODO capture)"
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

        // Reply textbox (277×25, max 199, action 2)
        // spec: ui_system.md §8.21.4 — "reply textbox (277×25, max length 199, multiline, action 2)"
        var replyBox = new LineEdit
        {
            Name = "ReplyBox",
            MaxLength = 199, // spec: §8.21.4 — max 199
            Position = new Vector2(8f, 220f),
            Size = new Vector2(277f, 25f),
            PlaceholderText = "Reply...",
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddChild(replyBox);

        // OK/reply button (action 1, 90×25)
        // spec: ui_system.md §8.21.4 — "OK/reply button (90×25, action 1)"
        var replyBtn = new Button
        {
            Name = "ReplyBtn",
            Text = "Reply",
            Position = new Vector2(8f, 255f),
            Size = new Vector2(90f, 25f),
            MouseFilter = MouseFilterEnum.Stop
        };
        // TODO(world-campaign): CmsgLetterRequest (2/60) → IApplicationUseCases method pending
        // spec: ui_system.md §8.21.6 — "action-1 emits C2S CmsgLetterRequest (2/60)"
        replyBtn.Pressed += () =>
        {
            GD.Print("[HudMailWindow] Reply pressed → TODO(world-campaign): C2S 2/60 CmsgLetterRequest. " +
                     "spec: Docs/RE/specs/ui_system.md §8.21.6 CODE-CONFIRMED.");
        };
        panel.AddChild(replyBtn);

        // Four tab buttons: Reply / Delete / Next / Prev
        // spec: ui_system.md §8.21.4 — "Four tab buttons: Reply / Delete / Next / Prev"
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

        // Close
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

    // -------------------------------------------------------------------------
    // Sub-panel visibility
    // -------------------------------------------------------------------------

    private void OnOpenReadPanel()
    {
        // spec: ui_system.md §8.21.2 — action 1 = open mail LIST / receive
        _sendPanelVisible = false;
        _readPanelVisible = true;
        if (_sendPanel is not null) _sendPanel.Visible = false;
        if (_readPanel is not null) _readPanel.Visible = true;
        GD.Print("[HudMailWindow] Read panel opened. " +
                 "TODO(capture): populate inbox. spec: §8.21.7.");
    }

    private void OnOpenSendPanel()
    {
        // spec: ui_system.md §8.21.2 — action 2 = open WRITE/SEND panel
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

    // -------------------------------------------------------------------------
    // Mail send (deferred-confirm flow)
    // spec: ui_system.md §8.21.3 CODE-CONFIRMED
    // -------------------------------------------------------------------------

    private void OnSendMail()
    {
        // spec: ui_system.md §8.21.3 — "action 1 validates recipient (else msg 52001) and body (else 52002),
        //   requires gold >= 200000 (else 55001/55002/55003 + 52019), then opens InfoPanel confirm;
        //   confirm 'Yes' emits C2S CmsgCarrierPigeonSend (2/70)"
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

        // Gold check is deferred to Application / server — we emit the intent.
        // TODO(world-campaign): IApplicationUseCases.CarrierPigeonSendAsync (C2S 2/70, 132B).
        // spec: ui_system.md §8.21.3 — "confirm 'Yes' emits C2S CmsgCarrierPigeonSend (2/70)"
        // spec: Docs/RE/packets/ — CmsgCarrierPigeonSend (2/70, 132B).
        GD.Print($"[HudMailWindow] Mail send intent: to='{recipient}', body={body.Length}B. " +
                 $"Postage={PostageCost}. TODO(world-campaign): C2S 2/70 CmsgCarrierPigeonSend. " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.3 CODE-CONFIRMED.");
    }

    private void OnAttachItem()
    {
        // spec: ui_system.md §8.21.3 — "Attach-item button (22×22), action 2, opening item-attach list of up to 30 rows"
        GD.Print("[HudMailWindow] Attach item: TODO(world-campaign): item-attach list (action 6..35). " +
                 "spec: Docs/RE/specs/ui_system.md §8.21.3 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Toggle
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Toggles the mailbox menu.
    ///     Opened from NPC service interactions; specific open opcode = debugger-pending.
    ///     spec: Docs/RE/specs/ui_system.md §8.21.7 — "open from NPC-service; opcode not isolated (debugger-pending)".
    /// </summary>
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
            // spec: ui_system.md §8.21.2 — "Esc also closes"
            Toggle(false);
            GetViewport().SetInputAsHandled();
        }
    }
}