// Ui/Scenes/Login/LoginWindow.cs
//
// Login window for the Login(1) state — rebuilt on the Ui/Scenes substrate.
//
// Implements the flowSubState machine from:
//   spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.3
//
// Sub-state machine (§2.2):
//   1  intro one-shot: SFX 861010105; reset curtain offset; hide all groups.         → 2
//   2  curtain opening: offset+=5/tick; top Y=−offset; bot Y=offset+326;
//      offset>200 snap submit plate to (494,469); offset>222                         → 3
//   3  curtain done: show login-form group; hide server-list.                         → 4
//   4  form idle; Enter → 5                                                       (event)
//   5  commit form.                                                                   → 6
//   6  validate-armed idle: OK/Enter → 29
//  29  validate: ID≥4 / PW≠0 check → 31
//  31  PIN entry: keypad modal shown.                                                (UI)
//  32  PIN poll: wait for PinSubmitted → 33
//  33  start server-list fetch                                                       → 34
//  34  (re)start fetch                                                               → 35
//  35  fetching                                                                  (→ 36)
//  36  fetch result → 37
//  37  server list shown: pick plate (400/401) or page (115..124).
//      Commit guard: status==0 && load<2400 → 38
//  38  channel-endpoint fetch → 39
//  39..40 connecting
//  41  hand-off: emit LoginFlowCompleted.
//
// Per-sub-state visibility (§2.1):
//   BG:          hidden 1/2;  shown 3+
//   Curtains:    shown  1/2;  hidden 3+
//   FormGroup:   hidden 1/2;  shown 3..32; hidden 33..41
//   NoticePanel: always hidden (never shown per spec §2.1 init=hidden)
//   ServerList:  hidden 1..32; shown 33..37; hidden 38..41
//   PinKeypad:   hidden 1..30; shown 31/32; hidden 33+
//   PinYesNo:    always hidden (legacy PIN dialog, unused in active flow)
//
// spec: Docs/RE/specs/frontend_layout_tables.md §2.

using Godot;
using MartialHeroes.Client.Godot.Screens; // FrontEndAudio
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
/// Login window — stateful login scene with spec-faithful flowSubState machine.
///
/// <para>Strictly passive: reads atlases/text from HUD libraries, builds widgets,
/// turns UI gestures into use-case signals. Never mutates domain state.</para>
///
/// spec: Docs/RE/specs/frontend_layout_tables.md §2 — login scene specification.
/// </summary>
public sealed partial class LoginWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 / §2.3
    // -------------------------------------------------------------------------

    // Curtain: +5 per tick. spec §2.2.
    private const float CurtainSpeed = 5f; // spec: frontend_layout_tables.md §2.2. CODE-CONFIRMED.

    // Stop (→ state 3) when offset > 222. spec §2.2.
    private const float CurtainCompleteThresh = 222f; // spec: frontend_layout_tables.md §2.2. CODE-CONFIRMED.

    // Snap submit plate when offset > 200. spec §2.3.
    private const float CurtainSnapThresh = 200f; // spec: frontend_layout_tables.md §2.3. CODE-CONFIRMED.

    // Bottom curtain base Y (= 326). spec §2.3 "bottom Y = offset + 326".
    private const int CurtainBotBaseY = 326; // spec: frontend_layout_tables.md §2.3. CODE-CONFIRMED.

    // Submit plate snaps to canvas (494,469). spec §2.3.
    private const int SubmitPlateSnapX = 494; // spec: frontend_layout_tables.md §2.3. CODE-CONFIRMED.
    private const int SubmitPlateSnapY = 469; // spec: frontend_layout_tables.md §2.3. CODE-CONFIRMED.

    // Dialog fade ±64/frame. spec: frontend_layout_tables.md §2.
    private const int DialogFadeStep = LoginLayout.DialogFadeStep; // 64
    private const int DialogAlphaVisible = LoginLayout.DialogAlphaVisible; // 255
    private const int DialogAlphaHidden = LoginLayout.DialogAlphaHidden; // 0

    // -------------------------------------------------------------------------
    // Signals (same contract as old LoginScreen)
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void LoginAcceptedEventHandler(string account, string password);

    [Signal]
    public delegate void QuitRequestedEventHandler();

    [Signal]
    public delegate void LoginFlowCompletedEventHandler(int serverId, string pin);

    // -------------------------------------------------------------------------
    // Injectable factories (set by LoginScene before AddChild)
    // -------------------------------------------------------------------------

    public Func<PinSubView>? PinFactory { get; set; }
    public Func<ServerSelectSubView>? ServerSelectFactory { get; set; }

    // DEV-only prefill (offline replay).
    public string? DevPrefillId { private get; set; }
    public string? DevPrefillPw { private get; set; }

    // Optional audio.
    public FrontEndAudio? Audio { get; set; }

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary _text;

    // flowSubState: single field, init=1. spec §2.2.
    private int _flowSubState;

    // Curtain state.
    private float _curtainAcc;
    private bool _curtainDone;
    private bool _submitPlateSnapped;

    // --- Layer containers (visibility gated per sub-state) ---

    // loginwindow.dds backdrop (0,110,1024,490). spec §2.1 "Background | init hidden".
    private Control? _backgroundLayer;

    // Bottom credential form strip. spec §2.1 "Login-form host strip".
    private Control? _formGroup;

    // Server-list overlay root (0,0,1024,398). spec §2.1 "Server-list root | init hidden".
    private Control? _serverListRoot;

    // Central notice panel. spec §2.1 "Notice panel | init hidden".
    private Control? _noticePanel;

    // PIN yes/no prompt. spec §2.1 "PIN yes/no panel | init hidden".
    private Control? _pinYesNoPanel;

    // Curtain panels.
    private TextureRect? _curtainTop; // Y = −offset
    private TextureRect? _curtainBot; // Y = offset + 326

    // Server-list submit plate button (snaps position on curtain>200). spec §2.3.
    private Control? _submitPlateCont;

    // Quit-confirm modals. spec §2.1 "Exit modal | init hidden".
    private Control? _quitModal;
    private Control? _quitModal2;
    private float _quitModalAlpha;
    private bool _quitModalVisible;

    // Credential textboxes.
    private HudTextbox? _idBox;
    private HudTextbox? _pwBox;

    // Save-ID checkbox.
    private HudCheckbox? _saveIdCheck;
    private bool _saveIdChecked;

    // Sub-view instances.
    private PinSubView? _pinView;
    private ServerSelectSubView? _serverSelect;
    private string _collectedPin = "";
    private int _collectedServerId;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    public LoginWindow(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text = text;
        MouseFilter = MouseFilterEnum.Pass;
    }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        LoadSaveId();

        // Build all layers back-to-front (Z-order = add-order).
        BuildCurtainPanels(); // curtain halves (slide apart in state 2)
        BuildBackgroundLayer(); // loginwindow.dds backdrop (hidden in 1/2)
        BuildNoticePanel(); // notice column (always hidden per spec)
        BuildServerListRoot(); // server-list container (hidden until 33..37)
        BuildFormGroup(); // bottom form strip (hidden until state 3)
        BuildPinYesNoPanel(); // PIN yes/no prompt (hidden per spec)
        BuildQuitModals(); // confirm modals (hidden per spec)

        // Enter state 1 — intro one-shot. spec §2.2.
        RunState(1);

        GD.Print("[LoginWindow] Login(1) built. flowSubState=1. spec: frontend_layout_tables.md §2.");

        if (Dev.LayoutDump.Enabled)
            RunLayoutDump();
    }

    // Headless layout oracle (MH_DUMP_LAYOUT=1).
    private async void RunLayoutDump()
    {
        SceneTree? tree = GetTree();
        if (tree is null) return;
        for (int i = 0; i < 3; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        try
        {
            SnapCurtainOpen();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Dev.LayoutDump.Dump(this, "LOGIN-REST");

            DoOpenServerSelect();
            for (int i = 0; i < 2; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Dev.LayoutDump.Dump(this, "LOGIN-SERVER");

            DoCloseServerSelect();
            DoOpenPin();
            for (int i = 0; i < 2; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Dev.LayoutDump.Dump(this, "LOGIN-PIN");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LAYOUTDUMP] {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        if (_flowSubState == 2) TickCurtain();
        if (_quitModal is not null) TickQuitModalFade();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMCloseRequest)
        {
            GD.Print("[LoginWindow] OS window-close → QuitRequested.");
            EmitSignal(SignalName.QuitRequested);
        }
    }

    // -------------------------------------------------------------------------
    // State machine
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2
    // -------------------------------------------------------------------------

    private void RunState(int state)
    {
        _flowSubState = state;
        GD.Print($"[LoginWindow] flowSubState={state}. spec: frontend_layout_tables.md §2.2.");
        ApplyVisibility(state);
        DispatchState(state);
    }

    // Per-sub-state visibility gating.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 (init column) / §2.2.
    private void ApplyVisibility(int state)
    {
        // Background (loginwindow.dds): hidden in 1/2, shown from 3+.
        // spec: §2.1 "Background | init hidden". CODE-CONFIRMED.
        if (_backgroundLayer is not null)
            _backgroundLayer.Visible = state >= 3;

        // Curtain panels: visible in 1/2, hidden once done.
        bool curtainOn = state <= 2;
        if (_curtainTop is not null) _curtainTop.Visible = curtainOn;
        if (_curtainBot is not null) _curtainBot.Visible = curtainOn;

        // Form group: shown in 3..32, hidden in 1/2 and 33..41.
        // spec: §2.2 "state 3 show login-form group".
        if (_formGroup is not null)
            _formGroup.Visible = state is >= 3 and <= 32;

        // Notice panel: always hidden (init=hidden, never re-shown). spec §2.1.
        if (_noticePanel is not null)
            _noticePanel.Visible = false;

        // Server-list root: visible in 33..37 only.
        // spec: §2.1 "Server-list root | init hidden".
        if (_serverListRoot is not null)
            _serverListRoot.Visible = state is >= 33 and <= 37;

        // PIN yes/no: hidden (init hidden, separate prompt not in active flow).
        if (_pinYesNoPanel is not null)
            _pinYesNoPanel.Visible = false;

        // PIN keypad: shown in 31/32 only.
        bool pinOn = state == 31 || state == 32;
        if (_pinView is not null && IsInstanceValid(_pinView))
            _pinView.Visible = pinOn;
    }

    private void DispatchState(int state)
    {
        switch (state)
        {
            case 1:
                // Intro one-shot: SFX, reset curtain, immediately → 2.
                // spec: §2.2 "1 intro one-shot: play curtain SFX 861010105 (cat 2); reset curtain offset 0".
                _curtainAcc = 0f;
                _curtainDone = false;
                _submitPlateSnapped = false;
                Audio?.PlayLoginCurtainSfx();
                GD.Print("[LoginWindow] State 1: SFX 861010105. spec: §2.2/§7.");
                RunState(2);
                break;

            case 2:
                // Curtain opening — TickCurtain() advances per _Process frame.
                break;

            case 3:
                // Curtain done → immediately advance to form idle.
                RunState(4);
                break;

            case 4:
                // Form idle — waits for user action (Enter/OK). spec §2.2 "4 form idle; Enter → 5".
                break;

            case 5:
                // Commit form → validate-armed idle.
                RunState(6);
                break;

            case 6:
                // Validate-armed idle. spec §2.2 "6 validate-armed idle: OK button (103) or Enter → 29".
                break;

            case 29:
                // Validate — runs synchronously. spec §2.2.
                RunValidation();
                break;

            case 31:
                // PIN entry — ensure keypad open. spec §2.2 "31 PIN entry: keypad modal shown".
                DoOpenPin();
                break;

            case 32:
                // PIN poll — wait for PinSubmitted signal.
                break;

            case 33:
                // Start server-list fetch. spec §2.2 "33 start server-list fetch worker".
                DoEnsureServerSelect();
                RunState(34);
                break;

            case 34:
                // (Re)start fetch → 35.
                RunState(35);
                break;

            case 35:
                // Fetching — show loading progress (stub). spec §2.2 "35 fetching".
                GD.Print("[LoginWindow] State 35: fetching server list. spec: §2.2.");
                break;

            case 36:
                // Fetch result (driven externally by ApplyServerList). spec §2.2 "36 fetch result".
                break;

            case 37:
                // Server list shown. spec §2.2 "37 server list shown: user picks a plate".
                break;

            case 38:
            case 39:
            case 40:
                GD.Print($"[LoginWindow] State {state}: endpoint/handoff. spec: §2.2.");
                break;

            case 41:
                // Hand-off: emit LoginFlowCompleted. spec §2.6.
                GD.Print("[LoginWindow] State 41: hand-off → LoginFlowCompleted. spec: §2.6.");
                EmitSignal(SignalName.LoginFlowCompleted, _collectedServerId, _collectedPin);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Curtain animation
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.3
    // -------------------------------------------------------------------------

    private void BuildCurtainPanels()
    {
        // Top curtain: login_slice1.dds (A1) at (0,0,1024,398). Slides up (Y = −offset). spec §2.3.
        TextureRect? top = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.BackgroundPanel.X, LoginLayout.BackgroundPanel.Y,
            LoginLayout.BackgroundPanel.W, LoginLayout.BackgroundPanel.H,
            LoginLayout.BackgroundPanel.SrcX, LoginLayout.BackgroundPanel.SrcY);
        if (top is not null)
        {
            top.Name = "CurtainTop";
            _curtainTop = top;
            AddChild(top);
        }

        // Bottom curtain: A1 at (0,326,1024,442) src(0,582). Slides down (Y = offset+326). spec §2.3.
        TextureRect? bot = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            0, CurtainBotBaseY,
            LoginLayout.BottomBarW, LoginLayout.BottomBarH,
            LoginLayout.BottomBarSrcX, LoginLayout.BottomBarSrcY);
        if (bot is not null)
        {
            bot.Name = "CurtainBot";
            _curtainBot = bot;
            AddChild(bot);
        }
    }

    private void TickCurtain()
    {
        _curtainAcc += CurtainSpeed; // spec §2.2 "offset+=5". CODE-CONFIRMED.

        // Top Y = −offset; bottom Y = offset+326. spec §2.3. CODE-CONFIRMED.
        if (_curtainTop is not null)
            _curtainTop.Position = new Vector2(0f, -_curtainAcc);
        if (_curtainBot is not null)
            _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + _curtainAcc);

        // At offset > 200: snap submit plate to (494,469). spec §2.3. CODE-CONFIRMED.
        if (!_submitPlateSnapped && _curtainAcc > CurtainSnapThresh)
        {
            _submitPlateSnapped = true;
            if (_submitPlateCont is not null)
            {
                // The snap is canvas-absolute; the button is parented to formPanel at Y=326.
                // So form-panel-local snapped pos = (494, 469−326) = (494, 143).
                _submitPlateCont.Position = new Vector2(SubmitPlateSnapX, SubmitPlateSnapY - CurtainBotBaseY);
            }

            GD.Print("[LoginWindow] Curtain offset>200: submit plate snapped. spec: §2.3.");
        }

        if (_curtainAcc >= CurtainCompleteThresh)
        {
            // spec: §2.2 "at offset>222 → 3". CODE-CONFIRMED.
            _curtainDone = true;
            GD.Print("[LoginWindow] Curtain complete (offset≥222) → state 3. spec: §2.2.");
            RunState(3);
        }
    }

    private void SnapCurtainOpen()
    {
        _curtainAcc = CurtainCompleteThresh;
        if (_curtainTop is not null) _curtainTop.Position = new Vector2(0f, -CurtainCompleteThresh);
        if (_curtainBot is not null) _curtainBot.Position = new Vector2(0f, CurtainBotBaseY + CurtainCompleteThresh);
        if (!_submitPlateSnapped)
        {
            _submitPlateSnapped = true;
            if (_submitPlateCont is not null)
                _submitPlateCont.Position = new Vector2(SubmitPlateSnapX, SubmitPlateSnapY - CurtainBotBaseY);
        }

        _curtainDone = true;
        if (_flowSubState < 3) RunState(3);
    }

    // -------------------------------------------------------------------------
    // Background layer (loginwindow.dds backdrop)
    // spec: §2.1 "Background | image | 0,110,1024,490 | A2 | init hidden"
    // -------------------------------------------------------------------------

    private void BuildBackgroundLayer()
    {
        _backgroundLayer = new Control
        {
            Name = "BackgroundLayer",
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false, // init hidden. spec §2.1.
        };
        _backgroundLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // loginwindow.dds (A2) at (0,110,1024,490) src(0,0). spec §2.1.
        TextureRect? backdrop = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.MainPanel.X, LoginLayout.MainPanel.Y,
            LoginLayout.MainPanel.W, LoginLayout.MainPanel.H,
            LoginLayout.MainPanel.SrcX, LoginLayout.MainPanel.SrcY);
        if (backdrop is not null) _backgroundLayer.AddChild(backdrop);

        AddChild(_backgroundLayer);
    }

    // -------------------------------------------------------------------------
    // Notice panel
    // spec: §2.1 "Notice panel | init hidden"
    // -------------------------------------------------------------------------

    private void BuildNoticePanel()
    {
        _noticePanel = new Control
        {
            Name = "NoticePanel",
            Position = new Vector2(LoginLayout.ServerListbox.X, LoginLayout.ServerListbox.Y),
            Size = new Vector2(LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H),
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false, // init hidden. spec §2.1.
        };

        // Panel frame art: loginwindow.dds src(0,490). spec §2.1.
        TextureRect? frame = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            0, 0, LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H,
            LoginLayout.ServerListbox.SrcX, LoginLayout.ServerListbox.SrcY);
        if (frame is not null) _noticePanel.AddChild(frame);

        // Scroll controls. spec §2.1 "Scroll-up/down/thumb buttons".
        AddNoticeButton(LoginLayout.ScrollUpArrow, LoginLayout.ActionScrollUp);
        AddNoticeButton(LoginLayout.ScrollDownArrow, LoginLayout.ActionScrollDown);
        AddNoticeButton(LoginLayout.ScrollThumb, LoginLayout.ActionScrollThumb);

        // Header. spec §2.1 "Title plate".
        TextureRect? header = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.ListboxHeader.X, LoginLayout.ListboxHeader.Y,
            LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H,
            LoginLayout.ListboxHeader.SrcX, LoginLayout.ListboxHeader.SrcY);
        if (header is not null) _noticePanel.AddChild(header);

        // 22 body labels at panel-local (50, 100+18·k, 383, 50). spec §2.1 "Notice labels ×22".
        for (uint id = LoginLayout.MsgLabelFirst; id <= LoginLayout.MsgLabelLast; id++)
        {
            string caption = _text.GetCaption((int)id, "");
            if (caption.Length == 0) continue;
            int k = (int)(id - LoginLayout.MsgLabelFirst);
            var label = new Label
            {
                Name = $"NoticeLabel{k}",
                Text = caption,
                Position = new Vector2(LoginLayout.NoticeLabelLocalX,
                    LoginLayout.NoticeLabelStartY + k * LoginLayout.NoticeLabelStrideY),
                Size = new Vector2(LoginLayout.NoticeLabelW, LoginLayout.NoticeLabelH),
                AutowrapMode = TextServer.AutowrapMode.Off,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeColorOverride("font_color", Colors.White);
            _noticePanel.AddChild(label);
        }

        AddChild(_noticePanel);
    }

    private void AddNoticeButton(WidgetRect rect, int actionId)
    {
        HudButton button = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            rect.X, rect.Y, rect.W, rect.H,
            rect.SrcX, rect.SrcY, rect.SrcX, rect.SrcY,
            actionId, fontSlot: 0);
        button.ActionFired += OnAction;
        Control? control = button.GetControl();
        if (control is not null) _noticePanel?.AddChild(control);
    }

    // -------------------------------------------------------------------------
    // Server-list root
    // spec: §2.1 "Server-list root | panel (opaque) | 0,0,1024,398 | A1 | init hidden"
    // -------------------------------------------------------------------------

    private void BuildServerListRoot()
    {
        _serverListRoot = new Control
        {
            Name = "ServerListRoot",
            Position = Vector2.Zero,
            Size = new Vector2(1024f, 398f),
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false, // init hidden. spec §2.1.
        };
        AddChild(_serverListRoot);
    }

    // -------------------------------------------------------------------------
    // Login-form group (bottom credential strip)
    // spec: §2.1 "Login-form host strip | panel | 0,326/768,1024,442 | A1 src(0,582)"
    // -------------------------------------------------------------------------

    private void BuildFormGroup()
    {
        // Full-canvas container so child rects align to canvas origin.
        // Hidden until state 3. spec §2.1/§2.2.
        _formGroup = new Control
        {
            Name = "FormGroup",
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false, // hidden until state 3.
        };
        _formGroup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_formGroup);

        // Form panel at fixed canvas Y=326 (NOT animated). spec §2.1.
        var formPanel = new Control
        {
            Name = "FormPanel",
            Position = new Vector2(0f, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.BottomBarW, LoginLayout.BottomBarH),
            MouseFilter = MouseFilterEnum.Pass,
        };
        _formGroup.AddChild(formPanel);

        // Confirm face-plate: A1 dst(265,0,494,113) src(0,469). spec §2.1 "Server-list plate".
        AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y,
            LoginLayout.ConfirmFacePlate.W, LoginLayout.ConfirmFacePlate.H,
            LoginLayout.ConfirmFacePlate.SrcX, LoginLayout.ConfirmFacePlate.SrcY);

        // ID label plate: A1 (340,30,38,13) src(0,398). spec §2.1 "ID label plate".
        AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.AccountLabelArt.X, LoginLayout.AccountLabelArt.Y,
            LoginLayout.AccountLabelArt.W, LoginLayout.AccountLabelArt.H,
            LoginLayout.AccountLabelArt.SrcX, LoginLayout.AccountLabelArt.SrcY);

        // PW label plate: A1 (507,30,49,13) src(38,398). spec §2.1 "PW label plate".
        AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.PasswordLabelArt.X, LoginLayout.PasswordLabelArt.Y,
            LoginLayout.PasswordLabelArt.W, LoginLayout.PasswordLabelArt.H,
            LoginLayout.PasswordLabelArt.SrcX, LoginLayout.PasswordLabelArt.SrcY);

        // Save-ID label plate: A1 (619,86,67,13) src(87,398). spec §2.1 "Save-ID label plate".
        AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.SmallDecorPlate.X, LoginLayout.SmallDecorPlate.Y,
            LoginLayout.SmallDecorPlate.W, LoginLayout.SmallDecorPlate.H,
            LoginLayout.SmallDecorPlate.SrcX, LoginLayout.SmallDecorPlate.SrcY);

        // Edit-field frame art for ID box. A1 src(615,404,102,13). spec §2.1 "ID textbox".
        AddRect(formPanel, LoginLayout.EditFieldFrameAtlas,
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.AccountBox.W, LoginLayout.AccountBox.H,
            LoginLayout.AccountBox.SrcX, LoginLayout.AccountBox.SrcY);

        // Edit-field frame art for PW box. spec §2.1 "PW textbox".
        AddRect(formPanel, LoginLayout.EditFieldFrameAtlas,
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.PasswordBox.W, LoginLayout.PasswordBox.H,
            LoginLayout.PasswordBox.SrcX, LoginLayout.PasswordBox.SrcY);

        // ID textbox — action 109, max 6, IME mode 16. spec §2.1 "ID textbox | maxlen 6".
        _idBox = HudWidgetFactory.MakeTextbox(
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.AccountBox.W, LoginLayout.TextboxRenderH,
            password: false, maxLength: LoginLayout.IdMaxLength, fontSlot: 0);
        Control? idCtrl = _idBox.GetControl();
        if (idCtrl is not null)
        {
            idCtrl.Name = "IdTextbox";
            formPanel.AddChild(idCtrl);
        }

        // PW textbox — action 110, max 129, masked. spec §2.1 "PW textbox | maxlen 129; masked".
        _pwBox = HudWidgetFactory.MakeTextbox(
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.PasswordBox.W, LoginLayout.TextboxRenderH,
            password: true, maxLength: LoginLayout.PwMaxLength, fontSlot: 0);
        Control? pwCtrl = _pwBox.GetControl();
        if (pwCtrl is not null)
        {
            pwCtrl.Name = "PwTextbox";
            formPanel.AddChild(pwCtrl);
        }

        // Enter on credential boxes. spec §2.2 "ENTER (10) → if state 6 run OK path, if state 4 → 5".
        _idBox.TextSubmitted += _ => OnEnterKey();
        _pwBox.TextSubmitted += _ => OnEnterKey();

        // OK / Login button — action 103. A1 N(266,398) H(490,398). spec §2.1 "OK / Login button".
        HudButton okBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.OkButton.X, LoginLayout.OkButton.Y,
            LoginLayout.OkButton.W, LoginLayout.OkButton.H,
            LoginLayout.OkButton.SrcX, LoginLayout.OkButton.SrcY,
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY,
            LoginLayout.ActionOk, fontSlot: 0);
        okBtn.ActionFired += OnAction;
        Control? okCtrl = okBtn.GetControl();
        if (okCtrl is not null)
        {
            okCtrl.Name = "OkButton";
            formPanel.AddChild(okCtrl);
        }

        // Server-list submit button — action 102. A1 N(154,398) H(378,398). spec §2.1.
        // Starts at form-local (456,166); snaps to canvas (494,469) when curtain>200.
        // spec: §2.3 "at offset>200 snap the server-list submit plate to (494,469)".
        HudButton serverBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmButton.X, LoginLayout.ConfirmButton.Y,
            LoginLayout.ConfirmButton.W, LoginLayout.ConfirmButton.H,
            LoginLayout.ConfirmButton.SrcX, LoginLayout.ConfirmButton.SrcY,
            LoginLayout.ConfirmHoverSrcX, LoginLayout.ConfirmHoverSrcY,
            LoginLayout.ActionConfirm, fontSlot: 0);
        serverBtn.ActionFired += OnAction;
        Control? serverCtrl = serverBtn.GetControl();
        if (serverCtrl is not null)
        {
            serverCtrl.Name = "ServerSubmitButton";
            _submitPlateCont = serverCtrl; // tracked for snap. spec §2.3.
            formPanel.AddChild(serverCtrl);
        }

        // Quit/help strip deco: A1 dst(407,-3,210,70) src(743,398). spec §2.1 "Help plate".
        AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitDecoPlate.X, LoginLayout.QuitDecoPlate.Y,
            LoginLayout.QuitDecoPlate.W, LoginLayout.QuitDecoPlate.H,
            LoginLayout.QuitDecoPlate.SrcX, LoginLayout.QuitDecoPlate.SrcY);

        // Quit/help strip button — action 105. A1 N(792,398) H(602,416). spec §2.1 "Help/Quit strip".
        HudButton quitBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitButton.X, LoginLayout.QuitButton.Y,
            LoginLayout.QuitButton.W, LoginLayout.QuitButton.H,
            LoginLayout.QuitButton.SrcX, LoginLayout.QuitButton.SrcY,
            LoginLayout.QuitHoverSrcX, LoginLayout.QuitHoverSrcY,
            actionId: 105, fontSlot: 0);
        quitBtn.ActionFired += OnAction;
        Control? quitCtrl = quitBtn.GetControl();
        if (quitCtrl is not null)
        {
            quitCtrl.Name = "HelpQuitButton";
            formPanel.AddChild(quitCtrl);
        }

        // Save-ID checkbox — action 104. A1 N(717,398) P(730,398). spec §2.1 "Save-ID checkbox".
        _saveIdCheck = HudWidgetFactory.MakeCheckbox(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.SaveIdCheck.X, LoginLayout.SaveIdCheck.Y,
            LoginLayout.SaveIdCheck.W, LoginLayout.SaveIdCheck.H,
            LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY,
            LoginLayout.SaveIdCheckedSrcX, LoginLayout.SaveIdCheckedSrcY,
            LoginLayout.ActionSaveId);
        if (_saveIdCheck is HudCheckbox chk)
        {
            chk.Toggled += OnSaveIdToggled;
            chk.IsChecked = _saveIdChecked;
        }

        Control? chkCtrl = _saveIdCheck?.GetControl();
        if (chkCtrl is not null)
        {
            chkCtrl.Name = "SaveIdCheckbox";
            formPanel.AddChild(chkCtrl);
        }

        // DEV prefill.
        if (DevPrefillId is { Length: > 0 } devId)
            (_idBox?.GetControl() as LineEdit)!.Text = devId;
        if (DevPrefillPw is { Length: > 0 } devPw)
            (_pwBox?.GetControl() as LineEdit)!.Text = devPw;
    }

    // Helper: add an atlas TextureRect as a child of parent.
    private void AddRect(Control parent, string atlas, int x, int y, int w, int h, int srcX, int srcY)
    {
        TextureRect? r = HudWidgetFactory.MakeAtlasRect(_atlas, atlas, x, y, w, h, srcX, srcY);
        if (r is not null) parent.AddChild(r);
    }

    // -------------------------------------------------------------------------
    // PIN yes/no panel
    // spec: §2.1 "PIN yes/no panel | 0,356,531,313 | init hidden"
    // -------------------------------------------------------------------------

    private void BuildPinYesNoPanel()
    {
        // spec: §2.1 "PIN yes/no panel | panel | 0,356,531,313 | 132,0 | init hidden".
        _pinYesNoPanel = new Control
        {
            Name = "PinYesNoPanel",
            Position = new Vector2(0f, 356f),
            Size = new Vector2(531f, 313f),
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false, // always hidden per spec §2.1.
        };

        // Prompt plate A: A1 dst(67,48,178,13) src(0,437). spec §2.1.
        AddRect(_pinYesNoPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate1.X, LoginLayout.DecoPlate1.Y,
            LoginLayout.DecoPlate1.W, LoginLayout.DecoPlate1.H,
            LoginLayout.DecoPlate1.SrcX, LoginLayout.DecoPlate1.SrcY);

        // Prompt plate B: A1 dst(0,100,313,32) src(289,437). spec §2.1.
        AddRect(_pinYesNoPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate2.X, LoginLayout.DecoPlate2.Y,
            LoginLayout.DecoPlate2.W, LoginLayout.DecoPlate2.H,
            LoginLayout.DecoPlate2.SrcX, LoginLayout.DecoPlate2.SrcY);

        // Yes button: A2 (40,82,110,38) N(520,492) P(635,492). action 111. spec §2.1.
        HudButton yesBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab1.X, LoginLayout.OptionTab1.Y,
            LoginLayout.OptionTab1.W, LoginLayout.OptionTab1.H,
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY,
            LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY,
            LoginLayout.ActionOptionTab1, fontSlot: 0);
        yesBtn.ActionFired += OnAction;
        Control? yesCtrl = yesBtn.GetControl();
        if (yesCtrl is not null) _pinYesNoPanel.AddChild(yesCtrl);

        // No button: A2 (164,82,110,38) N(750,492) P(865,492). action 112. spec §2.1.
        HudButton noBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab2.X, LoginLayout.OptionTab2.Y,
            LoginLayout.OptionTab2.W, LoginLayout.OptionTab2.H,
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY,
            LoginLayout.OptionTab2HoverSrcX, LoginLayout.OptionTab2HoverSrcY,
            LoginLayout.ActionOptionTab2, fontSlot: 0);
        noBtn.ActionFired += OnAction;
        Control? noCtrl = noBtn.GetControl();
        if (noCtrl is not null) _pinYesNoPanel.AddChild(noCtrl);

        AddChild(_pinYesNoPanel);
    }

    // -------------------------------------------------------------------------
    // Quit-confirm modals
    // spec: §2.1 "Exit (quit) modal / Error modal | 342,289,340,190 | A3 src(318,647) | init hidden"
    //       "Confirm-A/B panel | 342,289,340,190 | A3 src(318,647) | init hidden"
    // -------------------------------------------------------------------------

    private void BuildQuitModals()
    {
        _quitModal = BuildConfirmModalPanel(
            LoginLayout.MsgQuitConfirm1,
            LoginLayout.QuitConfirmYes1,
            LoginLayout.QuitConfirmYes1HoverSrcX, LoginLayout.QuitConfirmYes1HoverSrcY,
            LoginLayout.ActionQuitConfirmYes1, "QuitModal1");
        AddChild(_quitModal);

        _quitModal2 = BuildConfirmModalPanel(
            LoginLayout.MsgQuitConfirm2,
            LoginLayout.QuitConfirmYes2,
            LoginLayout.QuitConfirmYes2HoverSrcX, LoginLayout.QuitConfirmYes2HoverSrcY,
            LoginLayout.ActionQuitConfirmYes2, "QuitModal2");
        AddChild(_quitModal2);

        _quitModalAlpha = 0f;
        _quitModalVisible = false;
    }

    private Control BuildConfirmModalPanel(
        uint promptMsgId, WidgetRect yesRect,
        int hoverSrcX, int hoverSrcY, int actionId, string nodeName)
    {
        // spec: §2.1 "Confirm-A panel | 342,289,340,190 | A3 src(318,647) | init hidden".
        var panel = new Control
        {
            Name = nodeName,
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };

        // Chrome: InventWindow.dds (A3) src(318,647,340,190) at panel-local (0,0). spec §2.1.
        TextureRect? chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) panel.AddChild(chrome);

        // Prompt label: msg.xdb 4023/4024. spec §2.1 "Confirm-A label (msg 4023)".
        string prompt = _text.GetCaption(promptMsgId, $"[msg {promptMsgId}]");
        var promptLabel = new Label
        {
            Name = "PromptLabel",
            Text = prompt,
            Position = new Vector2(LoginLayout.ModalPromptX, LoginLayout.ModalPromptY),
            Size = new Vector2(LoginLayout.ModalPromptW, LoginLayout.ModalPromptH),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        promptLabel.AddThemeColorOverride("font_color", Colors.White);
        panel.AddChild(promptLabel);

        // OK button: A3 dst(120,136,113,40). spec §2.1 "Confirm-A OK | N302,900/P415,900 | action 113".
        HudButton yes = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasInventWindow,
            yesRect.X, yesRect.Y, yesRect.W, yesRect.H,
            yesRect.SrcX, yesRect.SrcY, hoverSrcX, hoverSrcY,
            actionId, fontSlot: 0);
        yes.ActionFired += OnAction;
        Control? yesControl = yes.GetControl();
        if (yesControl is not null)
        {
            yesControl.Name = "ConfirmOk";
            panel.AddChild(yesControl);
        }

        return panel;
    }

    private void ShowQuitModal()
    {
        _quitModalVisible = true;
        if (_quitModal is not null) _quitModal.Visible = true;
        if (_quitModal2 is not null) _quitModal2.Visible = false;
    }

    private void HideQuitModal()
    {
        _quitModalVisible = false;
        if (_quitModal is not null)
        {
            _quitModalAlpha = 0f;
            _quitModal.Visible = false;
        }

        if (_quitModal2 is not null) _quitModal2.Visible = false;
    }

    private void TickQuitModalFade()
    {
        if (_quitModal is null) return;
        if (_quitModalVisible)
            _quitModalAlpha = Math.Min(_quitModalAlpha + DialogFadeStep, DialogAlphaVisible);
        else
        {
            _quitModalAlpha = Math.Max(_quitModalAlpha - DialogFadeStep, DialogAlphaHidden);
            if (_quitModalAlpha <= 0) _quitModal.Visible = false;
        }

        _quitModal.Modulate = new Color(1f, 1f, 1f, _quitModalAlpha / 255f);
    }

    // -------------------------------------------------------------------------
    // Action handler
    // spec: §2.2 "OnEvent action map"
    // -------------------------------------------------------------------------

    private void OnAction(int actionId)
    {
        // Any action while curtain is opening snaps it to done. spec: ui_system.md §7.7.
        if (_flowSubState == 2) SnapCurtainOpen();

        switch (actionId)
        {
            case LoginLayout.ActionOk: // 103 — OK / Login button
                // spec: §2.2 "103 OK/login … requires flowSubState==6, runs the game.ver gate → 29".
                if (_flowSubState == 6) RunState(29);
                else if (_flowSubState == 4) RunState(5);
                break;

            case LoginLayout.ActionConfirm: // 102 — show server-list
                // spec: §2.2 "102 show server-list".
                DoOpenServerSelect();
                break;

            case 105: // Help/Quit strip — server-list re-fetch (→ 34). spec §2.2.
                DoOpenServerSelect();
                break;

            case LoginLayout.ActionSaveId: // 104 — save-ID toggle (handled by checkbox).
                break;

            case LoginLayout.ActionOptionTab1: // 111 — PIN-yes (→ 5). spec §2.2.
                RunState(5);
                break;

            case LoginLayout.ActionOptionTab2: // 112 — PIN-no. spec §2.2.
                if (_pinYesNoPanel is not null) _pinYesNoPanel.Visible = false;
                break;

            case LoginLayout.ActionQuitConfirmYes1: // 113 — confirm-A OK (→ 34). spec §2.2.
            case LoginLayout.ActionQuitConfirmYes2: // 114 — confirm-B OK (→ 34). spec §2.2.
                HideQuitModal();
                DoOpenServerSelect();
                break;
        }
        // Pager (115..124) and plate (400/401) actions handled inside ServerSelectSubView.
    }

    private void OnEnterKey()
    {
        // spec: §2.2 "ENTER (10) → if state 6 run OK path, if state 4 → 5".
        if (_flowSubState == 6) RunState(29);
        else if (_flowSubState == 4) RunState(5);
    }

    // -------------------------------------------------------------------------
    // Validation (state 29)
    // spec: §2.2 "29 validate: ID length ≥4 … PW length ≠0 … both OK → 31"
    // -------------------------------------------------------------------------

    private void RunValidation()
    {
        string account = _idBox?.Text ?? "";
        string password = _pwBox?.Text ?? "";

        // ID length ≥ 4. spec §2.4.
        if (account.Length < LoginLayout.MinIdLength)
        {
            string msg = _text.GetCaption((int)LoginLayout.MsgErrShortId, "[ID too short]");
            GD.PrintErr($"[LoginWindow] ID too short. msg {LoginLayout.MsgErrShortId}: '{msg}'");
            RunState(6);
            return;
        }

        // PW length ≠ 0. spec §2.4.
        if (password.Length < LoginLayout.MinPwLength)
        {
            string msg = _text.GetCaption((int)LoginLayout.MsgErrEmptyPassword, "[Password empty]");
            GD.PrintErr($"[LoginWindow] PW empty. msg {LoginLayout.MsgErrEmptyPassword}: '{msg}'");
            RunState(6);
            return;
        }

        // Persist save-ID if checked. spec §2.5.
        if (_saveIdChecked) PersistSaveId(account);

        // Emit credential accepted. spec: login_flow.md §4.2.
        GD.Print($"[LoginWindow] LoginAccepted (account='{account}'). spec: login_flow.md §4.2.");
        EmitSignal(SignalName.LoginAccepted, account, password);

        // → state 31 (PIN raise). spec §2.2.
        RunState(31);
    }

    // -------------------------------------------------------------------------
    // Server-select management
    // -------------------------------------------------------------------------

    private void DoOpenServerSelect()
    {
        DoEnsureServerSelect();
        if (_serverSelect is not null && IsInstanceValid(_serverSelect))
            _serverSelect.Visible = true;
        RunState(33);
    }

    private void DoCloseServerSelect()
    {
        if (_serverSelect is not null && IsInstanceValid(_serverSelect))
            _serverSelect.Visible = false;
        RunState(4);
    }

    private void DoEnsureServerSelect()
    {
        if (_serverListRoot is null) return;
        if (_serverSelect is not null && IsInstanceValid(_serverSelect)) return;
        if (ServerSelectFactory is null) return;

        _serverSelect = ServerSelectFactory();
        _serverSelect.Name = "ServerSelectSubView";
        _serverSelect.ServerSelected += OnServerSelected;
        _serverSelect.BackRequested += OnServerSelectBack;
        _serverListRoot.AddChild(_serverSelect);
    }

    private void OnServerSelected(int serverId)
    {
        _collectedServerId = serverId;
        GD.Print($"[LoginWindow] Server selected (id={serverId}). spec: login_flow.md §2.1.");
        // Close server-list and begin hand-off.
        if (_serverSelect is not null && IsInstanceValid(_serverSelect))
            _serverSelect.Visible = false;
        RunState(38);
        RunState(41); // hand-off.
    }

    private void OnServerSelectBack()
    {
        DoCloseServerSelect();
    }

    // -------------------------------------------------------------------------
    // PIN management
    // -------------------------------------------------------------------------

    private void DoOpenPin()
    {
        if (_pinView is not null && IsInstanceValid(_pinView))
        {
            _pinView.Visible = true;
        }
        else if (PinFactory is not null)
        {
            _pinView = PinFactory();
            _pinView.Name = "PinSubView";
            _pinView.HostInReferenceSpace = true;
            _pinView.PinSubmitted += OnPinSubmitted;
            _pinView.Cancelled += OnPinCancelled;
            AddChild(_pinView);
        }

        // Ensure visibility is applied for state 31.
        if (_pinView is not null && IsInstanceValid(_pinView))
            _pinView.Visible = true;
    }

    private void OnPinSubmitted(string pin)
    {
        _collectedPin = pin;
        if (_pinView is not null && IsInstanceValid(_pinView)) _pinView.Visible = false;
        GD.Print($"[LoginWindow] PIN collected (len={pin.Length}). spec: login_flow.md §4.2.");
        RunState(32); // poll → 33 immediately.
        RunState(33);
    }

    private void OnPinCancelled()
    {
        if (_pinView is not null && IsInstanceValid(_pinView)) _pinView.Visible = false;
        RunState(6);
        GD.Print("[LoginWindow] PIN cancelled; returning to validate-armed idle.");
    }

    // -------------------------------------------------------------------------
    // Save-ID persistence
    // spec: §2.5
    // -------------------------------------------------------------------------

    private void OnSaveIdToggled(bool pressed)
    {
        _saveIdChecked = pressed;
        if (!pressed) PersistSaveId("");
    }

    private void LoadSaveId()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(LoginLayout.SaveIdConfigPath) != Error.Ok) return;
        Variant savedId = cfg.GetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            Variant.From(LoginLayout.SaveIdNullSentinel));
        string saved = savedId.AsString();
        if (saved.Length > 0 && saved != LoginLayout.SaveIdNullSentinel)
        {
            DevPrefillId ??= saved;
            _saveIdChecked = true;
        }
    }

    private void PersistSaveId(string id)
    {
        var cfg = new ConfigFile();
        cfg.SetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            id.Length > 0 ? id : LoginLayout.SaveIdNullSentinel);
        Error err = cfg.Save(LoginLayout.SaveIdConfigPath);
        if (err != Error.Ok)
            GD.PrintErr($"[LoginWindow] PersistSaveId failed (err={err}).");
    }
}