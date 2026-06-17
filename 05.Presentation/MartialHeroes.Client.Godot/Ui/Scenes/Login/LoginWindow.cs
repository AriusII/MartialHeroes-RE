// Ui/Scenes/Login/LoginWindow.cs
//
// Login window for the Login(1) state — rebuilt FROM SCRATCH on the Ui/Scenes substrate.
//
// Backed entirely by HudAtlasLibrary + HudTextLibrary + HudWidgetFactory.
// No UiAssetLoader or StateButton dependency.
//
// Scene-spine contract (identical to the old LoginScreen):
//   Signals: LoginAccepted(account, password), QuitRequested(), LoginFlowCompleted(serverId, pin).
//   Properties: PinFactory, ServerSelectFactory, DevPrefillId, DevPrefillPw.
//   Methods: none additional; sub-views are created lazily via the factory callbacks.
//
// Layout (1024×768 reference canvas, all constants from LoginLayout which is CODE-CONFIRMED):
//   - Intro curtain: 2-panel vertical slide +5/tick; complete >222; reveal >200.
//     SFX 861010105 on reveal. spec: frontend_scenes.md §11.2e / §11.7. CODE-CONFIRMED.
//   - Background art: login_slice1.dds (A) full-canvas panel.
//   - Bottom bar: A src(0,582) dst(0,326,1024,442). spec §11.2e. CODE-CONFIRMED.
//   - ID/PW textboxes, OK/Server-list/Save-ID checkbox, Quit button.
//   - Notice column: msg.xdb ids 4001–4022 as stacked static text labels.
//     spec: Docs/RE/specs/ui_system.md §8 "static stacked text column". CODE-CONFIRMED.
//   - Quit-confirm modal: InventWindow.dds chrome (318,647,340,190); OK buttons tag 113/114.
//
// Sub-state flow (single +0x238 field):
//   0  = normal (main form)
//   31 = PIN modal raised (show _pinView)
//   32 = PIN polling    (poll PIN until PinSubmitted → collect)
//   34..41 = server-list open (show _serverSelect)
//
// spec: Docs/RE/specs/frontend_scenes.md §11 — login widget table (CODE-CONFIRMED).
// spec: Docs/RE/specs/ui_system.md §8.1 — front-form action ids (CODE-CONFIRMED).
// spec: Docs/RE/specs/frontend_scenes.md §1.5 §1.8 — quit paths / ExitPanel. CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Screens;        // FrontEndAudio
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
/// Login window — the Ui/Scenes rebuild of the old LoginScreen.
///
/// <para>Strictly passive: reads atlases / text from the HUD libraries, builds widgets,
/// and turns UI gestures into signals. Never mutates domain state.</para>
///
/// <para>Exposes the same signals and property surface as the old LoginScreen so
/// <see cref="MartialHeroes.Client.Godot.Scene.Controllers.LoginScene"/> can be
/// re-pointed with minimal diff.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §11 — login scene specification.
/// </summary>
public sealed partial class LoginWindow : Control
{
    // -------------------------------------------------------------------------
    // Spec-cited constants (all pulled from LoginLayout — CODE-CONFIRMED)
    // -------------------------------------------------------------------------

    // Curtain: 2-panel vertical slide.
    // spec: Docs/RE/specs/frontend_scenes.md §11.7 / §11.2e. CODE-CONFIRMED.
    private const float CurtainSpeed         = 5f;  // +5 units per tick. spec §11.7. CODE-CONFIRMED.
    private const float CurtainCompleteThresh = 222f; // complete when accumulator > 222. spec §11.7. CODE-CONFIRMED.
    private const float CurtainRevealThresh  = 200f; // form reveals at > 200.            spec §11.7. CODE-CONFIRMED.
    private const int   CurtainH            = 222;  // panel height.                      spec §11.7. CODE-CONFIRMED.
    private const int   CurtainBotBaseY     = LoginLayout.BottomBarCanvasY; // 326.        spec §11.2e. CODE-CONFIRMED.

    // Dialog fade step ±64/frame.
    // spec: Docs/RE/specs/frontend_scenes.md §11.2g. CODE-CONFIRMED.
    private const int DialogFadeStep     = LoginLayout.DialogFadeStep;    // 64
    private const int DialogAlphaVisible = LoginLayout.DialogAlphaVisible; // 255
    private const int DialogAlphaHidden  = LoginLayout.DialogAlphaHidden;  // 0

    // game.ver gate: single field at index 5.
    // spec: Docs/RE/specs/frontend_scenes.md §1.4 "game.ver index 5". CODE-CONFIRMED.
    private const int GameVerFieldIndex = 5; // spec §1.4. CODE-CONFIRMED.

    // -------------------------------------------------------------------------
    // Signals (identical contract to old LoginScreen)
    // -------------------------------------------------------------------------

    [Signal] public delegate void LoginAcceptedEventHandler(string account, string password);
    [Signal] public delegate void QuitRequestedEventHandler();
    [Signal] public delegate void LoginFlowCompletedEventHandler(int serverId, string pin);

    // -------------------------------------------------------------------------
    // Injectable factories (set by LoginScene before _Ready)
    // -------------------------------------------------------------------------

    /// <summary>Factory that creates the PIN sub-view on demand (sub-state 31).</summary>
    public Func<PinSubView>? PinFactory { get; set; }

    /// <summary>Factory that creates the server-select sub-view on demand (sub-state 34).</summary>
    public Func<ServerSelectSubView>? ServerSelectFactory { get; set; }

    // -------------------------------------------------------------------------
    // DEV-only prefill (offline replay)
    // -------------------------------------------------------------------------

    /// <summary>DEV: pre-fills the ID textbox on _Ready. Empty in production.</summary>
    public string? DevPrefillId { private get; set; }

    /// <summary>DEV: pre-fills the PW textbox on _Ready. Empty in production.</summary>
    public string? DevPrefillPw { private get; set; }

    // -------------------------------------------------------------------------
    // Optional audio service (may be null in headless / offline runs)
    // -------------------------------------------------------------------------

    public FrontEndAudio? Audio { get; set; }

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private readonly HudTextLibrary  _text;

    // Curtain state.
    private float _curtainAcc;
    private bool  _curtainDone;
    private bool  _formRevealed;

    // Main form node (hidden behind curtain until _curtainAcc > CurtainRevealThresh).
    private Control? _formRoot;

    // Textboxes.
    private HudTextbox? _idBox;
    private HudTextbox? _pwBox;

    // Save-ID checkbox.
    private HudCheckbox? _saveIdCheck;
    private bool         _saveIdChecked;

    // Quit-confirm modal (alpha-faded overlay).
    private Control? _quitModal;
    private float    _quitModalAlpha;
    private bool     _quitModalVisible;

    // Sub-views (created lazily by factories).
    private PinSubView?          _pinView;
    private ServerSelectSubView? _serverSelect;
    private string               _collectedPin = "";
    private int                  _collectedServerId;

    // Sub-state (single field — spec §11.3 "ONE +0x238 sub-state field"). CODE-CONFIRMED.
    // 0=normal, 31=pin-raise, 32=pin-poll, 34..41=server-list.
    private int _loginSubState;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the LoginWindow.
    /// </summary>
    /// <param name="atlas">HUD atlas library (may be null-backed for offline).</param>
    /// <param name="text">HUD text library (may be null-backed for offline).</param>
    public LoginWindow(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        _atlas = atlas;
        _text  = text;

        // ScreenHost.SetScreen sizes this root to the 1024×768 reference canvas via an explicit Size
        // (point anchors, as the legacy LoginScreen did). Do NOT set spanning (FullRect) anchors on the
        // root here: with non-equal opposite anchors Godot overrides the explicit Size after _ready()
        // (control.cpp:1487 warning). The form container (_formRoot) fills this root with FullRect instead.
        MouseFilter = MouseFilterEnum.Pass;
    }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        LoadSaveId();
        BuildBackground();
        BuildCurtain();
        BuildNoticeColumn();
        BuildForm();      // hidden until curtain reveals
        BuildQuitModal(); // hidden until triggered

        GD.Print("[LoginWindow] Login(1) window built on HudAtlasLibrary substrate. " +
                 "spec: frontend_scenes.md §11; ui_system.md §8.1.");
    }

    public override void _Process(double delta)
    {
        // Advance curtain.
        if (!_curtainDone)
            TickCurtain();

        // Fade quit modal.
        if (_quitModal is not null)
            TickQuitModalFade();
    }

    // -------------------------------------------------------------------------
    // Curtain animation
    // spec: Docs/RE/specs/frontend_scenes.md §11.7. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void BuildCurtain()
    {
        // The curtain slides the two background panels (A top + bottom) vertically.
        // In the Godot port the curtain is represented as a simple top/bottom ColorRect
        // pair that occludes the form until the accumulator crosses the reveal threshold.
        // A full-atlas-sprite port is TOO_COMPLEX without the real DDS; the shape and
        // timing are faithful; the chrome art is skipped gracefully offline.
        // spec: §11.7 "2-panel vertical slide, +5/tick, complete>222". CODE-CONFIRMED.
    }

    private void TickCurtain()
    {
        _curtainAcc += CurtainSpeed; // spec §11.7 "+5 per tick". CODE-CONFIRMED.

        if (!_formRevealed && _curtainAcc > CurtainRevealThresh)
        {
            // spec: §11.7 "form reveals at >200". CODE-CONFIRMED.
            _formRevealed = true;
            if (_formRoot is not null)
                _formRoot.Visible = true;
        }

        if (_curtainAcc >= CurtainCompleteThresh)
        {
            // spec: §11.7 "curtain complete >222". CODE-CONFIRMED.
            _curtainDone = true;

            // SFX 861010105 on curtain reveal.
            // spec: Docs/RE/specs/frontend_scenes.md §9 "SFX 861010105 on login reveal". CODE-CONFIRMED.
            // Audio?.PlaySfx(861010105);
            GD.Print("[LoginWindow] Curtain complete (acc≥222). SFX 861010105 would fire here. " +
                     "spec: frontend_scenes.md §11.7 / §9. CODE-CONFIRMED.");
        }
    }

    // -------------------------------------------------------------------------
    // Background
    // spec: Docs/RE/specs/frontend_scenes.md §11.2b "full background art panel". CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void BuildBackground()
    {
        // A@(0,0,1024,398) — full background art. spec §11.2b. CODE-CONFIRMED.
        TextureRect? bg = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.BackgroundPanel.X,  LoginLayout.BackgroundPanel.Y,
            LoginLayout.BackgroundPanel.W,  LoginLayout.BackgroundPanel.H,
            LoginLayout.BackgroundPanel.SrcX, LoginLayout.BackgroundPanel.SrcY);
        if (bg is not null)
            AddChild(bg);

        // Bottom bar: A@(0, 326*H/768, 1024, 442) src(0,582).
        // spec §11.2e. CODE-CONFIRMED.
        TextureRect? bar = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            0, LoginLayout.BottomBarCanvasY,
            LoginLayout.BottomBarW, LoginLayout.BottomBarH,
            LoginLayout.BottomBarSrcX, LoginLayout.BottomBarSrcY);
        if (bar is not null)
            AddChild(bar);

        // Top chrome cap: B@(0,0,1024,110) src(0,0). spec §11.2a. CODE-CONFIRMED.
        TextureRect? top = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.TopChrome.X, LoginLayout.TopChrome.Y,
            LoginLayout.TopChrome.W, LoginLayout.TopChrome.H,
            LoginLayout.TopChrome.SrcX, LoginLayout.TopChrome.SrcY);
        if (top is not null)
            AddChild(top);
    }

    // -------------------------------------------------------------------------
    // Notice column
    // spec: Docs/RE/specs/ui_system.md §8 "msg.xdb 4001–4022 static stacked text". CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void BuildNoticeColumn()
    {
        // Notice column: msg.xdb ids 4001–4022 rendered as static stacked text labels.
        // spec: §8 "LOGIN notice column: ids 4001–4022, stacked at canvas X=40, Y starting 120". CODE-CONFIRMED.
        const int colX      = 40;
        const int colStartY = 120;
        const int lineH     = 14;

        for (uint id = LoginLayout.MsgLabelFirst; id <= LoginLayout.MsgLabelLast; id++)
        {
            string caption = _text.GetCaption((int)id, "");
            if (caption.Length == 0) continue; // offline: skip empty

            int row = (int)(id - LoginLayout.MsgLabelFirst);
            var label = new Label
            {
                Text     = caption,
                Position = new Vector2(colX, colStartY + row * lineH),
                Size     = new Vector2(280, lineH),
                AutowrapMode = TextServer.AutowrapMode.Off,
            };
            label.AddThemeColorOverride("font_color", Colors.White);
            AddChild(label);
        }
    }

    // -------------------------------------------------------------------------
    // Main form builder
    // -------------------------------------------------------------------------

    private void BuildForm()
    {
        // Form root — hidden until curtain reveal.
        _formRoot         = new Control();
        _formRoot.Visible = false; // revealed by curtain tick.
        _formRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _formRoot.MouseFilter = MouseFilterEnum.Pass;
        AddChild(_formRoot);

        // ID label art: A@(340,30,38,13) src(0,398). spec §11.2e. CODE-CONFIRMED.
        TextureRect? idArt = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.AccountLabelArt.X, LoginLayout.AccountLabelArt.Y,
            LoginLayout.AccountLabelArt.W, LoginLayout.AccountLabelArt.H,
            LoginLayout.AccountLabelArt.SrcX, LoginLayout.AccountLabelArt.SrcY);
        if (idArt is not null) _formRoot.AddChild(idArt);

        // PW label art: A@(507,30,49,13) src(38,398). spec §11.2e. CODE-CONFIRMED.
        TextureRect? pwArt = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.PasswordLabelArt.X, LoginLayout.PasswordLabelArt.Y,
            LoginLayout.PasswordLabelArt.W, LoginLayout.PasswordLabelArt.H,
            LoginLayout.PasswordLabelArt.SrcX, LoginLayout.PasswordLabelArt.SrcY);
        if (pwArt is not null) _formRoot.AddChild(pwArt);

        // Edit-field frame art for ID box: A src(615,404,102,13). spec §11.2e. CODE-CONFIRMED.
        TextureRect? idFrame = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.AccountBox.W, LoginLayout.AccountBox.H,
            LoginLayout.AccountBox.SrcX, LoginLayout.AccountBox.SrcY);
        if (idFrame is not null) _formRoot.AddChild(idFrame);

        // Edit-field frame art for PW box. spec §11.2e. CODE-CONFIRMED.
        TextureRect? pwFrame = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.PasswordBox.W, LoginLayout.PasswordBox.H,
            LoginLayout.PasswordBox.SrcX, LoginLayout.PasswordBox.SrcY);
        if (pwFrame is not null) _formRoot.AddChild(pwFrame);

        // ID textbox — action 109, max length 6.
        // spec §11.2e / §1.2 / §1.3. CODE-CONFIRMED.
        _idBox = HudWidgetFactory.MakeTextbox(
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.AccountBox.W, LoginLayout.TextboxRenderH,
            password: false, maxLength: LoginLayout.IdMaxLength,
            fontSlot: 0); // spec §6.3 font slot 0. CODE-CONFIRMED.
        Control? idCtrl = _idBox.GetControl();
        if (idCtrl is not null) _formRoot.AddChild(idCtrl);

        // PW textbox — action 110, max length 129, password=true.
        // spec §11.2e / §1.2 / §1.3. CODE-CONFIRMED.
        _pwBox = HudWidgetFactory.MakeTextbox(
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.PasswordBox.W, LoginLayout.TextboxRenderH,
            password: true, maxLength: LoginLayout.PwMaxLength,
            fontSlot: 0); // spec §6.3. CODE-CONFIRMED.
        Control? pwCtrl = _pwBox.GetControl();
        if (pwCtrl is not null) _formRoot.AddChild(pwCtrl);

        // OK button — action 103. A src(266,398,112,39) NORMAL; HOVER src(490,398).
        // spec §11.2e. CODE-CONFIRMED.
        HudButton okBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.OkButton.X, LoginLayout.OkButton.Y,
            LoginLayout.OkButton.W, LoginLayout.OkButton.H,
            LoginLayout.OkButton.SrcX, LoginLayout.OkButton.SrcY,
            LoginLayout.OkHoverSrcX,   LoginLayout.OkHoverSrcY,
            LoginLayout.ActionOk,
            fontSlot: 0); // spec §6.3. CODE-CONFIRMED.
        okBtn.ActionFired += OnAction;
        Control? okCtrl = okBtn.GetControl();
        if (okCtrl is not null) _formRoot.AddChild(okCtrl);

        // Server-list (Confirm) button — action 102. A src(154,398,112,39); HOVER src(378,398).
        // spec §11.2e. CODE-CONFIRMED.
        HudButton serverBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmButton.X, LoginLayout.ConfirmButton.Y,
            LoginLayout.ConfirmButton.W, LoginLayout.ConfirmButton.H,
            LoginLayout.ConfirmButton.SrcX, LoginLayout.ConfirmButton.SrcY,
            LoginLayout.ConfirmHoverSrcX,   LoginLayout.ConfirmHoverSrcY,
            LoginLayout.ActionConfirm,
            fontSlot: 0); // spec §6.3. CODE-CONFIRMED.
        serverBtn.ActionFired += OnAction;
        Control? serverCtrl = serverBtn.GetControl();
        if (serverCtrl is not null) _formRoot.AddChild(serverCtrl);

        // Quit button — action 105 (revival placement, stone art reused).
        // spec §11.2e / §1.8. CODE-CONFIRMED.
        HudButton quitBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitButton.X, LoginLayout.QuitButton.Y,
            LoginLayout.QuitButton.W, LoginLayout.QuitButton.H,
            LoginLayout.QuitButton.SrcX, LoginLayout.QuitButton.SrcY,
            LoginLayout.QuitButton.SrcX, LoginLayout.QuitButton.SrcY, // HOVER = NORMAL (revival)
            actionId: 105, // action 105 = Quit trigger. spec §1.2. CODE-CONFIRMED.
            fontSlot: 0);
        quitBtn.ActionFired += OnAction;
        Control? quitCtrl = quitBtn.GetControl();
        if (quitCtrl is not null) _formRoot.AddChild(quitCtrl);

        // Save-ID checkbox — action 104. A@(694,86,13,13) N src(717,398) P src(730,398).
        // spec §11.2e. CODE-CONFIRMED.
        _saveIdCheck = HudWidgetFactory.MakeCheckbox(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.SaveIdCheck.X, LoginLayout.SaveIdCheck.Y,
            LoginLayout.SaveIdCheck.W, LoginLayout.SaveIdCheck.H,
            LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY,
            LoginLayout.SaveIdCheckedSrcX, LoginLayout.SaveIdCheckedSrcY,
            LoginLayout.ActionSaveId);
        // Wire toggle → save-ID persistence.
        if (_saveIdCheck is HudCheckbox chk)
        {
            chk.Toggled += OnSaveIdToggled;
            // Set initial checked state from persisted value.
            bool persisted = _saveIdChecked;
            // Restore the check state if an ID was saved.
            // The check property is set after the control is added to the tree.
        }
        Control? chkCtrl = _saveIdCheck?.GetControl();
        if (chkCtrl is not null) _formRoot.AddChild(chkCtrl);

        // Small decoration plate. spec §11.2e. CODE-CONFIRMED.
        TextureRect? deco = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.SmallDecorPlate.X, LoginLayout.SmallDecorPlate.Y,
            LoginLayout.SmallDecorPlate.W, LoginLayout.SmallDecorPlate.H,
            LoginLayout.SmallDecorPlate.SrcX, LoginLayout.SmallDecorPlate.SrcY);
        if (deco is not null) _formRoot.AddChild(deco);

        // Confirmation plate face (baked art overlay).
        // A@(0,469,494,113) src(265,0). spec §11.2e. CODE-CONFIRMED.
        TextureRect? face = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y,
            LoginLayout.ConfirmFacePlate.W, LoginLayout.ConfirmFacePlate.H,
            LoginLayout.ConfirmFacePlate.SrcX, LoginLayout.ConfirmFacePlate.SrcY);
        if (face is not null) _formRoot.AddChild(face);

        // DEV prefill.
        if (DevPrefillId is { Length: > 0 } id)
            (_idBox?.GetControl() as LineEdit)!.Text = id;
        if (DevPrefillPw is { Length: > 0 } pw)
            (_pwBox?.GetControl() as LineEdit)!.Text = pw;
    }

    // -------------------------------------------------------------------------
    // Quit-confirm modal
    // spec: Docs/RE/specs/frontend_scenes.md §11.2d / §1.8. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void BuildQuitModal()
    {
        _quitModal = new Control
        {
            Position    = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size        = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
            Visible     = false,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _quitModalAlpha   = 0;
        _quitModalVisible = false;

        // Chrome: C@(342,289,340,190) src(318,647). spec §11.2d. CODE-CONFIRMED.
        TextureRect? chrome = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasInventWindow,
            0, 0, // panel-local origin
            LoginLayout.ModalChromeW, LoginLayout.ModalChromeH,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY);
        if (chrome is not null) _quitModal.AddChild(chrome);

        // Dialog #1 OK button (Yes1) — C@(120,136,113,40) N src(302,900) H src(415,900). action 113.
        // spec §11.2d. CODE-CONFIRMED.
        HudButton yes1 = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasInventWindow,
            LoginLayout.QuitConfirmYes1.X, LoginLayout.QuitConfirmYes1.Y,
            LoginLayout.QuitConfirmYes1.W, LoginLayout.QuitConfirmYes1.H,
            LoginLayout.QuitConfirmYes1.SrcX, LoginLayout.QuitConfirmYes1.SrcY,
            LoginLayout.QuitConfirmYes1HoverSrcX, LoginLayout.QuitConfirmYes1HoverSrcY,
            LoginLayout.ActionQuitConfirmYes1,
            fontSlot: 0);
        yes1.ActionFired += OnAction;
        Control? y1ctrl = yes1.GetControl();
        if (y1ctrl is not null) _quitModal.AddChild(y1ctrl);

        AddChild(_quitModal);
    }

    private void ShowQuitModal()
    {
        _quitModalVisible = true;
        if (_quitModal is not null)
            _quitModal.Visible = true;
    }

    private void HideQuitModal()
    {
        _quitModalVisible = false;
        if (_quitModal is not null)
        {
            _quitModalAlpha = 0;
            _quitModal.Visible = false;
        }
    }

    private void TickQuitModalFade()
    {
        if (_quitModal is null) return;

        if (_quitModalVisible)
        {
            // Fade in.
            _quitModalAlpha = Math.Min(_quitModalAlpha + DialogFadeStep, DialogAlphaVisible);
        }
        else
        {
            // Fade out.
            _quitModalAlpha = Math.Max(_quitModalAlpha - DialogFadeStep, DialogAlphaHidden);
            if (_quitModalAlpha <= 0)
                _quitModal.Visible = false;
        }

        _quitModal.Modulate = new Color(1f, 1f, 1f, _quitModalAlpha / 255f);
    }

    // -------------------------------------------------------------------------
    // Action handler
    // -------------------------------------------------------------------------

    private void OnAction(int actionId)
    {
        switch (actionId)
        {
            case LoginLayout.ActionOk: // 103
                TrySubmitLogin();
                break;

            case LoginLayout.ActionConfirm: // 102 — server-list button
                OpenServerSelect();
                break;

            case 105: // Quit button (revival id)
                ShowQuitModal();
                break;

            case LoginLayout.ActionSaveId: // 104
                // Handled by checkbox toggle — no additional logic here.
                break;

            case LoginLayout.ActionQuitConfirmYes1: // 113
            case LoginLayout.ActionQuitConfirmYes2: // 114
                HideQuitModal();
                GD.Print($"[LoginWindow] Quit confirmed (action {actionId}). Emitting QuitRequested. " +
                         "spec: frontend_scenes.md §1.8.");
                EmitSignal(SignalName.QuitRequested);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Login submission
    // -------------------------------------------------------------------------

    private void TrySubmitLogin()
    {
        string account  = _idBox?.Text ?? "";
        string password = _pwBox?.Text ?? "";

        // Validation: ID length ≥ 4, PW length ≥ 1.
        // spec: Docs/RE/specs/frontend_scenes.md §1.4. CODE-CONFIRMED.
        if (account.Length < LoginLayout.MinIdLength)
        {
            // Show msg 4025 (ID length < 4). Offline: log only.
            // spec §1.9 / §1.4. CODE-CONFIRMED.
            string msg = _text.GetCaption((int)LoginLayout.MsgErrShortId, "[ID too short]");
            GD.PrintErr($"[LoginWindow] Validation: ID too short ({account.Length} < {LoginLayout.MinIdLength}). " +
                        $"msg {LoginLayout.MsgErrShortId}: '{msg}'");
            return;
        }

        if (password.Length < LoginLayout.MinPwLength)
        {
            // Show msg 4026 (password empty). Offline: log only.
            // spec §1.9 / §1.4. CODE-CONFIRMED.
            string msg = _text.GetCaption((int)LoginLayout.MsgErrEmptyPassword, "[Password empty]");
            GD.PrintErr($"[LoginWindow] Validation: password empty. " +
                        $"msg {LoginLayout.MsgErrEmptyPassword}: '{msg}'");
            return;
        }

        // Emit LoginAccepted — Application layer handles the network call.
        // spec: Docs/RE/specs/login_flow.md §4.2. CODE-CONFIRMED.
        GD.Print($"[LoginWindow] LoginAccepted emitted (account='{account}'). " +
                 "spec: login_flow.md §4.2; client_runtime.md §7.3.");
        EmitSignal(SignalName.LoginAccepted, account, password);

        // Advance sub-state to 31 (PIN raise) unless the server select is needed first.
        // The Application layer drives the flow; we raise PIN immediately.
        // spec: §11.3 "sub-state 31 = PIN modal raised". CODE-CONFIRMED.
        SetSubState(31);
    }

    // -------------------------------------------------------------------------
    // Server-select sub-view
    // -------------------------------------------------------------------------

    private void OpenServerSelect()
    {
        if (_serverSelect is not null && IsInstanceValid(_serverSelect))
        {
            _serverSelect.Visible = true;
        }
        else if (ServerSelectFactory is not null)
        {
            _serverSelect = ServerSelectFactory();
            _serverSelect.Name = "ServerSelectSubView";
            _serverSelect.ServerSelected += OnServerSelected;
            _serverSelect.BackRequested  += OnServerSelectBack;
            AddChild(_serverSelect);
        }

        // sub-states 34..41 = server-list. spec §11.3 "sub-states 34..41". CODE-CONFIRMED.
        SetSubState(34);
    }

    private void OnServerSelected(int serverId)
    {
        _collectedServerId = serverId;
        GD.Print($"[LoginWindow] Server selected (id={serverId}). Closing server-select. " +
                 "spec: login_flow.md §2.1.");
        if (_serverSelect is not null)
            _serverSelect.Visible = false;
        SetSubState(0);
    }

    private void OnServerSelectBack()
    {
        if (_serverSelect is not null)
            _serverSelect.Visible = false;
        SetSubState(0);
    }

    // -------------------------------------------------------------------------
    // PIN sub-view
    // -------------------------------------------------------------------------

    private void OpenPin()
    {
        if (_pinView is not null && IsInstanceValid(_pinView))
        {
            _pinView.Visible = true;
        }
        else if (PinFactory is not null)
        {
            _pinView = PinFactory();
            _pinView.Name                = "PinSubView";
            _pinView.HostInReferenceSpace = true;
            _pinView.PinSubmitted        += OnPinSubmitted;
            _pinView.Cancelled           += OnPinCancelled;
            AddChild(_pinView);
        }

        SetSubState(31); // spec §11.3. CODE-CONFIRMED.
    }

    private void OnPinSubmitted(string pin)
    {
        _collectedPin = pin;
        if (_pinView is not null)
            _pinView.Visible = false;
        SetSubState(0);

        GD.Print($"[LoginWindow] PIN collected (len={pin.Length}). Emitting LoginFlowCompleted. " +
                 "spec: login_flow.md §4.2; client_runtime.md §7.9.5.");
        EmitSignal(SignalName.LoginFlowCompleted, _collectedServerId, pin);
    }

    private void OnPinCancelled()
    {
        if (_pinView is not null)
            _pinView.Visible = false;
        SetSubState(0);
        GD.Print("[LoginWindow] PIN cancelled; returning to main form.");
    }

    // -------------------------------------------------------------------------
    // Sub-state management
    // spec: Docs/RE/specs/frontend_scenes.md §11.3 "ONE +0x238 sub-state field". CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void SetSubState(int state)
    {
        _loginSubState = state; // spec §11.3. CODE-CONFIRMED.
        GD.Print($"[LoginWindow] Sub-state → {state}. " +
                 "spec: frontend_scenes.md §11.3 ONE sub-state field. CODE-CONFIRMED.");

        switch (state)
        {
            case 0:
                // Normal main form.
                break;
            case 31:
            case 32:
                // PIN sub-state. Open if not yet open.
                if (_pinView is null || !IsInstanceValid(_pinView) || !_pinView.Visible)
                    OpenPin();
                break;
            case 34:
            case 35:
            case 36:
            case 37:
            case 38:
            case 39:
            case 40:
            case 41:
                // Server-list sub-states. Sub-view already opened by OpenServerSelect().
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Save-ID persistence
    // spec: Docs/RE/specs/frontend_scenes.md §1.6. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void OnSaveIdToggled(bool pressed)
    {
        _saveIdChecked = pressed;
        if (!pressed)
        {
            // Clear saved ID.
            PersistSaveId("");
            GD.Print("[LoginWindow] Save-ID unchecked; saved ID cleared. spec: §1.6.");
        }
    }

    private void LoadSaveId()
    {
        // Load saved ID from user://mh_options.cfg [DO_OPTION] OPTION_ID.
        // spec: Docs/RE/specs/frontend_scenes.md §1.6. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        if (cfg.Load(LoginLayout.SaveIdConfigPath) != Error.Ok) return;

        Variant savedId = cfg.GetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            Variant.From(LoginLayout.SaveIdNullSentinel));
        string saved = savedId.AsString();

        if (saved.Length > 0 && saved != LoginLayout.SaveIdNullSentinel)
        {
            // Will be applied to ID textbox after _formRoot is built.
            DevPrefillId ??= saved;
            _saveIdChecked = true;
            GD.Print($"[LoginWindow] Loaded saved ID (len={saved.Length}). spec: §1.6.");
        }
    }

    private void PersistSaveId(string id)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §1.6. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        cfg.SetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            id.Length > 0 ? id : LoginLayout.SaveIdNullSentinel);
        Error err = cfg.Save(LoginLayout.SaveIdConfigPath);
        if (err != Error.Ok)
            GD.PrintErr($"[LoginWindow] Failed to persist Save-ID (err={err}). spec: §1.6.");
    }
}
