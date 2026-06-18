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
//      offset>222                                                                    → 3
//   3  curtain done; host strip already present; credential group still hidden.       → 4
//   4  form idle; Enter → 5                                                       (event)
//   5  commit form.                                                                   → 6
//   6  validate-armed idle: shows credential group; OK/Enter → 29
//  29  validate: ID≥4 / PW≠0 check; hides credential group; shows PIN → 31
//  31  PIN entry: keypad modal shown.                                                (UI)
//  32  PIN poll: wait for PinSubmitted → 33
//  33  start server-list fetch; hide credential group                                → 34
//  34  (re)start fetch                                                               → 35
//  35  fetching                                                                  (→ 36)
//  36  fetch result → 37
//  37  server list shown: pick plate (400/401) or page (115..124).
//      Commit guard: status==0 && load<2400 → 38
//  38  channel-endpoint fetch → 39
//  39..40 connecting
//  41  hand-off: emit LoginFlowCompleted.
//
// Per-sub-state visibility (CORRECTED, §2.2 bands, CYCLE 18 C5b):
//   BG:               hidden 1;  shown 2+ (1→2 edge). spec §2.2 corrected.
//   Curtains (host panels): always-present Y-animated panels; NOT toggled by sub-state.
//                     Binary models them as Y-animated always-present panels that slide off-canvas.
//                     Port hides them after state 2 (equivalent, no partial-occlusion). spec §2.2 bands.
//   FormGroup (host strip): shown from state 2+. ALWAYS PRESENT after curtain starts.
//                     Distinct from credential group. spec §2.2 "Login-form host strip | always present".
//   CredentialGroup:  hidden 1..5; SHOWN on the 5→6 edge; hidden leaving 33.
//                     Band ≈ 5..33 (NOT 3..32). spec §2.2 bands CORRECTED.
//   NoticePanel:      always hidden (never shown per spec §2.1 init=hidden)
//   ServerList:       hidden 1..34; shown 35..37; hidden 38+. spec §2.2 corrected.
//   PinKeypad:        hidden 1..30; shown 31/32; hidden 33+
//   PinYesNo:         always hidden (legacy PIN dialog, unused in active flow)
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

    // Bottom curtain base Y (= 326). spec §2.3 "bottom Y = offset + 326".
    private const int CurtainBotBaseY = 326; // spec: frontend_layout_tables.md §2.3. CODE-CONFIRMED.

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

    // Server-list re-fetch throttle (action 105): the IDA handler skips the re-fetch while a fetch is
    // already in flight (sub-state 35) or within 10 s of the last one. spec §2.2.
    private const ulong ServerFetchThrottleMs = 10000;
    private ulong _lastServerFetchMs;

    // Curtain state.
    private float _curtainAcc;
    private bool _curtainDone;

    // --- Layer containers (visibility gated per sub-state) ---

    // loginwindow.dds backdrop (0,110,1024,490). spec §2.1 "Background | init hidden".
    private Control? _backgroundLayer;

    // Login-form host strip: always-present bottom bar (server-submit[102], help/quit[105], confirm
    // face-plate, help deco plate). Visible from state 2. spec §2.2 "Login-form host strip | always present".
    private Control? _formGroup;

    // Interactive credential group: ID textbox, PW textbox, Save-ID checkbox + label plate,
    // OK button (103), ID/PW label plates, edit-field frame art.
    // Built hidden; shown on the 5→6 edge; hidden when leaving 33. Band ≈ 5..33.
    // SOLE visibility authority — never toggled elsewhere. spec §2.2 bands CORRECTED.
    private Control? _credentialGroup;

    // Server-list overlay root (0,0,1024,398). spec §2.1 "Server-list root | init hidden".
    private Control? _serverListRoot;

    // PIN keypad host — structural root gated SOLELY by ApplyVisibility (states 31/32), mirroring
    // _serverListRoot. The PinSubView child lives inside it and is never toggled directly, so the
    // sub-state band is the single visibility authority (no double-source .Visible writes).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1/§2.2/§3.
    private Control? _pinKeypadRoot;

    // Central notice panel. spec §2.1 "Notice panel | init hidden".
    private Control? _noticePanel;

    // PIN yes/no prompt. spec §2.1 "PIN yes/no panel | init hidden".
    private Control? _pinYesNoPanel;

    // Curtain panels.
    private TextureRect? _curtainTop; // Y = −offset
    private TextureRect? _curtainBot; // Y = offset + 326

    // Form backing children ride with the bottom curtain panel. spec §2.3.
    private Control? _formPanel;
    private Control? _credPanel;

    // Server-list strip and deco are visible only while the server list is open. spec §2.2.
    private Control? _serverListStrip;
    private Control? _serverListStripDeco;

    // Re-fetch confirm popups (msg 4023/4024), built init-hidden exactly as the IDA login build
    // creates them. Their OK buttons fire action 113/114 (→ hide + restart server-list fetch). The
    // SHOW trigger lives in the server-list fetch-result path (network layer, not yet ported), so
    // offline they remain hidden — faithful to the real client offline. spec §2.2.
    private Control? _quitModal;
    private Control? _quitModal2;

    // Quit-confirm ExitPanel (msg 2007), opened by action 102/112; "yes" (101) quits. spec §2.2.
    private Control? _exitConfirm;

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
        BuildCurtainPanels(); // curtain halves (always-present Y-animated panels)
        BuildBackgroundLayer(); // loginwindow.dds backdrop (hidden in state 1 only)
        BuildNoticePanel(); // notice column (always hidden per spec)
        BuildServerListRoot(); // server-list container (hidden until 35..37)
        BuildFormGroup(); // always-present host strip (visible from state 2)
        BuildCredentialGroup(); // interactive credential widgets (shown on 5→6 edge, band ≈ 5..33)
        BuildPinKeypadRoot(); // PIN keypad host (hidden until 31/32) — above the form, below modals
        BuildPinYesNoPanel(); // PIN yes/no prompt (hidden per spec)
        BuildQuitModals(); // confirm modals (hidden per spec)

        // Enter state 1 — intro one-shot. spec §2.2.
        RunState(1);

        GD.Print("[LoginWindow] Login(1) built. flowSubState=1. spec: frontend_layout_tables.md §2.");

        if (Dev.LayoutDump.Enabled)
            RunLayoutDump();
    }

    // Headless layout oracle (MH_DUMP_LAYOUT=1).
    // Proves the credential-group split: at LOGIN-REST (state 4) CredentialGroup must be hidden
    // while FormGroup (host strip) is visible; at LOGIN-CREDS (state 6) CredentialGroup must be visible.
    // spec: §2.2 bands "Interactive credential group | state ≈ 5..33" (CORRECTED, CYCLE 18 C5b).
    private async void RunLayoutDump()
    {
        SceneTree? tree = GetTree();
        if (tree is null) return;
        for (int i = 0; i < 3; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        try
        {
            // LOGIN-REST: state 4 after snap. CredentialGroup=hidden, FormGroup(host strip)=visible.
            SnapCurtainOpen();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Dev.LayoutDump.Dump(this, "LOGIN-REST");

            // LOGIN-CREDS: advance to state 6 (5→6 edge shows credential group).
            // spec: §2.2 "entering 6 shows the inner credential group".
            RunState(6);
            for (int i = 0; i < 2; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Dev.LayoutDump.Dump(this, "LOGIN-CREDS");

            DoOpenServerSelect();
            for (int i = 0; i < 2; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Dev.LayoutDump.Dump(this, "LOGIN-SERVER");

            RunState(31); // raise PIN via the real path: ApplyVisibility shows _pinKeypadRoot, DispatchState opens it.
            for (int i = 0; i < 2; i++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Dev.LayoutDump.Dump(this, "LOGIN-PIN");

            // Return to rest so the scene auto-walk can cleanly advance Login→Load→Opening for their dumps.
            RunState(4);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LAYOUTDUMP] {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        if (_flowSubState == 2) TickCurtain();
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMCloseRequest)
        {
            GD.Print("[LoginWindow] OS window-close → QuitRequested.");
            EmitSignal(SignalName.QuitRequested);
        }
    }

    // Window-level Enter key handler. The credential textboxes are hidden at state 4 so their
    // TextSubmitted cannot fire. This handler covers Enter at ALL states, including 4→5 (before
    // the credential group appears) and 6→29 (while the credential group is shown). No per-textbox
    // TextSubmitted wiring — this is the single Enter path. spec: §2.2 "ENTER(10) → state6→OK, state4→5".
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
            {
                OnEnterKey();
                AcceptEvent();
            }
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

    // Per-sub-state visibility gating (SOLE authority — no .Visible writes elsewhere for these groups).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.2 bands (CORRECTED, CYCLE 18 C5b).
    private void ApplyVisibility(int state)
    {
        // Background (loginwindow.dds): shown on the 1→2 edge (revealed behind parting curtains),
        // never hidden afterward. spec: §2.2 bands "Background visible from state 2".
        if (_backgroundLayer is not null)
            _backgroundLayer.Visible = state >= 2;

        // Curtain panels: the binary models them as always-present Y-animated host panels that slide
        // off-canvas (their visible flag stays set). Port hides them after state 2 which is visually
        // equivalent (they are off-canvas). No partial-occlusion regression.
        // spec: §2.2 bands "Curtains | not a hideable widget | always-present Y-animated panels".
        bool curtainOn = state <= 2;
        if (_curtainTop is not null) _curtainTop.Visible = curtainOn;
        if (_curtainBot is not null) _curtainBot.Visible = curtainOn;

        // Form group (host strip: server-submit[102], help/quit[105], confirm face-plate, help deco):
        // ALWAYS PRESENT from state 2. Distinct from the credential group.
        // spec: §2.2 bands "Login-form host strip | always present" (CORRECTED from 3..32).
        if (_formGroup is not null)
            _formGroup.Visible = state >= 2;

        // Credential group (ID/PW textboxes, Save-ID, OK[103], label plates, frame art): SOLE
        // visibility authority. Per the §2.2 EDGE ladder (the precise IDA truth, more authoritative
        // than the reconstructed "5..33" band summary): SHOWN entering state 6 (the validate-armed idle
        // where the user types), HIDDEN entering 29 (validate hides it + raises PIN), and re-hidden at
        // 31/33. So the faithful visible interval is [6, 28] (in practice only state 6; PIN/server-list
        // and states 3/4 keep it hidden). spec: §2.2 ladder (29/31/33 "hide the credential group").
        if (_credentialGroup is not null)
            _credentialGroup.Visible = state is >= 6 and <= 28;

        // Notice panel: always hidden (init=hidden, never re-shown). spec §2.1.
        if (_noticePanel is not null)
            _noticePanel.Visible = false;

        // Server-list CONTENT panel: shown on the 34→35 edge, records painted at 37, hidden leaving 37.
        // State 33 only STARTS the fetch worker; the content panel appears at 35.
        // spec: §2.2 bands "Server-list CONTENT panel | state 35..37" (NOT 33..37; CYCLE 18 Phase A).
        if (_serverListRoot is not null)
            _serverListRoot.Visible = state is >= 35 and <= 37;

        bool serverListOpen = state is >= 35 and <= 37;
        if (_serverListStrip is not null) _serverListStrip.Visible = serverListOpen;
        if (_serverListStripDeco is not null) _serverListStripDeco.Visible = serverListOpen;

        // PIN yes/no: hidden (init hidden, separate prompt not in active flow). spec §2.1.
        if (_pinYesNoPanel is not null)
            _pinYesNoPanel.Visible = false;

        // PIN keypad root: shown in 31/32 only. The PinSubView child lives inside this root and is
        // never toggled directly — this root is the SOLE visibility authority. spec §2.2/§3.
        bool pinOn = state == 31 || state == 32;
        if (_pinKeypadRoot is not null)
            _pinKeypadRoot.Visible = pinOn;
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

        float formRideY = CurtainBotBaseY + _curtainAcc;
        if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formRideY);
        if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formRideY);

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
        float formOpenY = CurtainBotBaseY + CurtainCompleteThresh; // 548
        if (_formPanel is not null) _formPanel.Position = new Vector2(0f, formOpenY);
        if (_credPanel is not null) _credPanel.Position = new Vector2(0f, formOpenY);

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

    // PIN keypad host root — full-canvas container, hidden until states 31/32. The PinSubView builds
    // its own full-canvas click-capture + the (347,173) 329×422 dragon-frame panel inside this root;
    // visibility is governed exclusively by ApplyVisibility. spec §2.1/§2.2/§3.
    private void BuildPinKeypadRoot()
    {
        _pinKeypadRoot = new Control
        {
            Name = "PinKeypadRoot",
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false, // init hidden; shown only in 31/32. spec §2.2.
        };
        _pinKeypadRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_pinKeypadRoot);
    }

    // -------------------------------------------------------------------------
    // Login-form host strip (always-present bottom bar)
    // Contains: confirm face-plate, server-list submit button [102], help deco plate,
    //           help/quit strip [105]. Visible from state 2 onward. NEVER gated on credential state.
    // spec: §2.1 "Login-form host strip | always present"; §2.2 bands CORRECTED.
    // -------------------------------------------------------------------------

    private void BuildFormGroup()
    {
        // Full-canvas container. Visible from state 2 (host strip always present after curtain starts).
        // spec: §2.2 bands "Login-form host strip | always present" (CORRECTED from hidden-until-3).
        _formGroup = new Control
        {
            Name = "FormGroup",
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false, // hidden only at state 1; shown at state 2 via ApplyVisibility.
        };
        _formGroup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_formGroup);

        // Form panel starts at closed Y=326 and rides the bottom curtain to open Y=548. spec §2.3.
        var formPanel = new Control
        {
            Name = "FormPanel",
            Position = new Vector2(0f, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.BottomBarW, LoginLayout.BottomBarH),
            MouseFilter = MouseFilterEnum.Pass,
        };
        _formGroup.AddChild(formPanel);
        _formPanel = formPanel;

        // Confirm face-plate: A1 dst(265,0,494,113) src(0,469). spec §2.1 "Server-list plate".
        AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y,
            LoginLayout.ConfirmFacePlate.W, LoginLayout.ConfirmFacePlate.H,
            LoginLayout.ConfirmFacePlate.SrcX, LoginLayout.ConfirmFacePlate.SrcY);

        // Quit-confirm button — action 102. A1 N(154,398) H(378,398). spec §2.1.
        // Rides with the form panel to its open resting canvas position. spec §2.3.
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
            formPanel.AddChild(serverCtrl);
        }

        // Quit/help strip deco: A1 dst(407,-3,210,70) src(743,398). spec §2.1 "Help plate".
        _serverListStripDeco = AddRect(formPanel, LoginLayout.AtlasLoginSlice1,
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
            _serverListStrip = quitCtrl;
            formPanel.AddChild(quitCtrl);
        }
    }

    // -------------------------------------------------------------------------
    // Interactive credential group (ID/PW textboxes, Save-ID, OK[103], label plates, frame art)
    // Built hidden; SOLE visibility authority = ApplyVisibility (band ≈ 5..33).
    // Parent at same full-rect origin as formPanel so canvas-local coordinates are unchanged.
    // spec: §2.1 LOGIN-FORM table (credential widgets); §2.2 bands "Interactive credential group".
    // -------------------------------------------------------------------------

    private void BuildCredentialGroup()
    {
        // Full-canvas container with an inner panel that rides with the bottom curtain.
        // Built hidden; shown only by ApplyVisibility on the 5→6 edge. No direct .Visible elsewhere.
        // spec: §2.2 bands "Interactive credential group | state ≈ 5..33" (CORRECTED, CYCLE 18 C5b).
        _credentialGroup = new Control
        {
            Name = "CredentialGroup",
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false, // built hidden; ApplyVisibility is the SOLE authority.
        };
        _credentialGroup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_credentialGroup);

        // Inner panel at the same canvas origin as formPanel (Y=326) so coordinates match the spec table.
        var credPanel = new Control
        {
            Name = "CredPanel",
            Position = new Vector2(0f, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.BottomBarW, LoginLayout.BottomBarH),
            MouseFilter = MouseFilterEnum.Pass,
        };
        _credentialGroup.AddChild(credPanel);
        _credPanel = credPanel;

        // ID label plate: A1 (340,30,38,13) src(0,398). spec §2.1 "ID label plate".
        AddRect(credPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.AccountLabelArt.X, LoginLayout.AccountLabelArt.Y,
            LoginLayout.AccountLabelArt.W, LoginLayout.AccountLabelArt.H,
            LoginLayout.AccountLabelArt.SrcX, LoginLayout.AccountLabelArt.SrcY);

        // PW label plate: A1 (507,30,49,13) src(38,398). spec §2.1 "PW label plate".
        AddRect(credPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.PasswordLabelArt.X, LoginLayout.PasswordLabelArt.Y,
            LoginLayout.PasswordLabelArt.W, LoginLayout.PasswordLabelArt.H,
            LoginLayout.PasswordLabelArt.SrcX, LoginLayout.PasswordLabelArt.SrcY);

        // Save-ID label plate: A1 (619,86,67,13) src(87,398). spec §2.1 "Save-ID label plate".
        AddRect(credPanel, LoginLayout.AtlasLoginSlice1,
            LoginLayout.SmallDecorPlate.X, LoginLayout.SmallDecorPlate.Y,
            LoginLayout.SmallDecorPlate.W, LoginLayout.SmallDecorPlate.H,
            LoginLayout.SmallDecorPlate.SrcX, LoginLayout.SmallDecorPlate.SrcY);

        // Edit-field frame art for ID box. A1 src(615,404,102,13). spec §2.1 "ID textbox".
        AddRect(credPanel, LoginLayout.EditFieldFrameAtlas,
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.AccountBox.W, LoginLayout.AccountBox.H,
            LoginLayout.AccountBox.SrcX, LoginLayout.AccountBox.SrcY);

        // Edit-field frame art for PW box. spec §2.1 "PW textbox".
        AddRect(credPanel, LoginLayout.EditFieldFrameAtlas,
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
            credPanel.AddChild(idCtrl);
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
            credPanel.AddChild(pwCtrl);
        }

        // Enter in a focused credential field advances the flow. A focused Godot LineEdit CONSUMES the
        // Enter key (emits text_submitted + marks it handled), so the window-level _UnhandledKeyInput
        // does NOT see it at state 6 where a field has focus. These TextSubmitted handlers cover state 6
        // (field focused); the window handler covers state 4 (credential group hidden → no field focus).
        // They are complementary — never both at the same state — and OnEnterKey is a no-op outside
        // states 4/6, so any stray double-call is harmless.
        // spec: §2.2 "ENTER (10) → if state 6 run OK path, if state 4 → 5".
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
            credPanel.AddChild(okCtrl);
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
            credPanel.AddChild(chkCtrl);
        }

        // DEV prefill.
        if (DevPrefillId is { Length: > 0 } devId)
            (_idBox?.GetControl() as LineEdit)!.Text = devId;
        if (DevPrefillPw is { Length: > 0 } devPw)
            (_pwBox?.GetControl() as LineEdit)!.Text = devPw;
    }

    // Helper: add an atlas TextureRect as a child of parent.
    private TextureRect? AddRect(Control parent, string atlas, int x, int y, int w, int h, int srcX, int srcY)
    {
        TextureRect? r = HudWidgetFactory.MakeAtlasRect(_atlas, atlas, x, y, w, h, srcX, srcY);
        if (r is not null) parent.AddChild(r);
        return r;
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

        _exitConfirm = BuildExitConfirmPanel();
        AddChild(_exitConfirm);
    }

    // Quit-confirm ExitPanel (msg 2007): chrome + prompt + Yes(101 → quit) + No(51 → close).
    // Opened by action 102/112. spec: Docs/RE/specs/frontend_layout_tables.md §2.2.
    private Control BuildExitConfirmPanel()
    {
        var panel = new Control
        {
            Name = "ExitConfirmPanel",
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass,
        };

        // Shared InventWindow.dds chrome src(318,647,340,190).
        TextureRect? chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) panel.AddChild(chrome);

        // Prompt label (msg 2007). Fallback = empty — real client only shows CP949 from msg.xdb.
        string prompt = _text.GetCaption(LoginLayout.MsgExitConfirm, "");
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

        // Yes button (action 101 → quit) — yes/no plate art (loginwindow.dds 520,492 / 635,492).
        HudButton yes = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            40, 136, 110, 38,
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY,
            LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY,
            LoginLayout.ActionAppQuit, fontSlot: 0);
        yes.ActionFired += OnAction;
        Control? yesCtrl = yes.GetControl();
        if (yesCtrl is not null) panel.AddChild(yesCtrl);

        // No button (action 51 → close) — plate art (loginwindow.dds 750,492 / 865,492).
        HudButton no = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            190, 136, 110, 38,
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY,
            LoginLayout.OptionTab2HoverSrcX, LoginLayout.OptionTab2HoverSrcY,
            ActionExitNo, fontSlot: 0);
        no.ActionFired += OnAction;
        Control? noCtrl = no.GetControl();
        if (noCtrl is not null) panel.AddChild(noCtrl);

        return panel;
    }

    // ExitPanel "no" action (close the quit-confirm). Local id; the binary's "yes" is 101.
    private const int ActionExitNo = 51;

    private void ShowExitConfirm()
    {
        if (_exitConfirm is not null) _exitConfirm.Visible = true;
    }

    private void HideExitConfirm()
    {
        if (_exitConfirm is not null) _exitConfirm.Visible = false;
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
        // Fallback = empty — real client only shows CP949 from msg.xdb.
        string prompt = _text.GetCaption(promptMsgId, "");
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

    private void HideQuitModal()
    {
        if (_quitModal is not null) _quitModal.Visible = false;
        if (_quitModal2 is not null) _quitModal2.Visible = false;
    }

    // -------------------------------------------------------------------------
    // Action handler
    // spec: §2.2 "OnEvent action map"
    // -------------------------------------------------------------------------

    private void OnAction(int actionId)
    {
        // Any action while curtain is opening snaps it to done. spec: ui_system.md §7.7.
        if (_flowSubState == 2) SnapCurtainOpen();

        // Action map re-confirmed vs binary (LoginWindow_OnEvent) 2026-06-18.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.2.
        switch (actionId)
        {
            case LoginLayout.ActionAppQuit: // 101 — app quit (fired by the ExitPanel "yes").
                EmitSignal(SignalName.QuitRequested);
                break;

            case LoginLayout.ActionOk: // 103 — OK / Login button
                // spec: §2.2 "103 OK/login … requires flowSubState==6, runs the game.ver gate → 29".
                if (_flowSubState == 6) RunState(29);
                else if (_flowSubState == 4) RunState(5);
                break;

            case LoginLayout.ActionConfirm: // 102 — open quit-confirm ExitPanel (NOT server-list).
            case LoginLayout.ActionOptionTab2: // 112 — same target as 102 (open ExitPanel).
                ShowExitConfirm();
                break;

            case 105: // server-list re-fetch — throttled to 1/10 s, skipped while a fetch is in flight
                // (sub-state 35). spec §2.2.
                if (_flowSubState != 35
                    && global::Godot.Time.GetTicksMsec() >= _lastServerFetchMs + ServerFetchThrottleMs)
                {
                    _lastServerFetchMs = global::Godot.Time.GetTicksMsec();
                    RestartServerFetch();
                }

                break;

            case LoginLayout.ActionSaveId: // 104 — save-ID toggle (handled by checkbox).
                break;

            case LoginLayout.ActionOptionTab1: // 111 — advance (→ 5). spec §2.2.
                RunState(5);
                break;

            case LoginLayout.ActionQuitConfirmYes1: // 113 — re-fetch popup OK → restart fetch (→34).
            case LoginLayout.ActionQuitConfirmYes2: // 114 — re-fetch popup OK → restart fetch (→34).
                HideQuitModal();
                RestartServerFetch();
                break;

            case ActionExitNo: // 51 — ExitPanel "no": close the quit-confirm.
                HideExitConfirm();
                break;
        }
        // Pager (115..124) and plate (400/401) actions handled inside ServerSelectSubView.
    }

    // 105 / 113 / 114: throttled restart of the server-list fetch (re-enter sub-state 34).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.2 (105 = restart fetch, ~10 s throttle).
    private void RestartServerFetch()
    {
        DoEnsureServerSelect();
        RunState(34); // ApplyVisibility(34) keeps _serverListRoot shown (33..37 band) — the sole gate. spec §2.2.
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
            // Fallback = empty — real client only shows CP949 from msg.xdb.
            string msg = _text.GetCaption((int)LoginLayout.MsgErrShortId, "");
            GD.PrintErr($"[LoginWindow] ID too short. msg {LoginLayout.MsgErrShortId}: '{msg}'");
            RunState(6);
            return;
        }

        // PW length ≠ 0. spec §2.4.
        if (password.Length < LoginLayout.MinPwLength)
        {
            // Fallback = empty — real client only shows CP949 from msg.xdb.
            string msg = _text.GetCaption((int)LoginLayout.MsgErrEmptyPassword, "");
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
        RunState(33); // ApplyVisibility(33) shows _serverListRoot — the sole gate. spec §2.2.
    }

    private void DoEnsureServerSelect()
    {
        if (_serverListRoot is null) return;
        if (_serverSelect is not null && IsInstanceValid(_serverSelect)) return;
        if (ServerSelectFactory is null) return;

        _serverSelect = ServerSelectFactory();
        _serverSelect.Name = "ServerSelectSubView";
        _serverSelect.ServerSelected += OnServerSelected;
        _serverSelect.Visible = true; // one-time enable; _serverListRoot gates show/hide. spec §2.1.
        _serverListRoot.AddChild(_serverSelect);
    }

    private void OnServerSelected(int serverId)
    {
        _collectedServerId = serverId;
        GD.Print($"[LoginWindow] Server selected (id={serverId}). spec: login_flow.md §2.1.");
        // Begin hand-off; RunState(38) leaves the 33..37 band so ApplyVisibility hides _serverListRoot.
        RunState(38);
        RunState(41); // hand-off.
    }

    // -------------------------------------------------------------------------
    // PIN management
    // -------------------------------------------------------------------------

    private void DoOpenPin()
    {
        // Lazily create the PinSubView once, parented into the structural _pinKeypadRoot. The root's
        // visibility (set by ApplyVisibility for states 31/32) is the SOLE show/hide authority; the
        // child is enabled once and never toggled directly. spec: §2.2/§3.
        if (_pinView is null || !IsInstanceValid(_pinView))
        {
            if (PinFactory is null || _pinKeypadRoot is null) return;
            _pinView = PinFactory();
            _pinView.Name = "PinSubView";
            _pinView.HostInReferenceSpace = true;
            _pinView.PinSubmitted += OnPinSubmitted;
            _pinView.Cancelled += OnPinCancelled;
            _pinView.Visible = true; // one-time enable; _pinKeypadRoot gates show/hide.
            _pinKeypadRoot.AddChild(_pinView);
        }
    }

    private void OnPinSubmitted(string pin)
    {
        _collectedPin = pin;
        GD.Print($"[LoginWindow] PIN collected (len={pin.Length}). spec: login_flow.md §4.2.");
        RunState(32); // poll → 33 immediately; leaving 31/32 hides _pinKeypadRoot via ApplyVisibility.
        RunState(33);
    }

    private void OnPinCancelled()
    {
        RunState(6); // leaving 31/32 hides _pinKeypadRoot via ApplyVisibility.
        GD.Print("[LoginWindow] PIN cancelled; returning to validate-armed idle.");
    }

    // -------------------------------------------------------------------------
    // Save-ID persistence
    // Original target (CONFIRMED, static IDA, CYCLE 18 Phase A):
    //   file = DoOption.ini (EXE-relative), section = [DO_OPTION], key = OPTION_ID.
    //   Value = stored login-id string; "(null)" / empty = no saved id.
    //   spec: frontend_layout_tables.md §2.5 (CONFIRMED).
    // Port equivalent: user://mh_options.cfg ConfigFile, section/key from LoginLayout.SaveId*.
    //   This is the layer-05 translation of the Windows private-profile API to Godot's ConfigFile.
    //   Behavior is unchanged — do not modify working ConfigFile paths.
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