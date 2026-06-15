// Screens/LoginScreen.cs
//
// LOGIN screen — pixel-faithful FROM-SCRATCH rewrite against §11.2 widget table.
//
// COMPOSITION (spec: Docs/RE/specs/frontend_scenes.md §11.0 / §11.2):
//
//   Design canvas: 1024×768, top-left anchored, centered on screen.
//   ALL widget positions are canvas-local.  Atlas shorthand:
//     A = login_slice1.dds   (DXT2 premultiplied)   §11.1
//     B = loginwindow.dds    (DXT5)                  §11.1
//     C = InventWindow.dds   (DXT3)                  §11.1
//     D = loginwindow_02.dds (DXT2 premultiplied)    §11.1
//
// WIDGET LIST (build order = back-to-front, spec §11.2g):
//   z=1  A@(0,0,1024,398) src(0,0)          — upper baked-art backdrop     §11.2h / §11.2b
//   z=2  B@(0,110,1024,490) src(0,0)        — central ink-wash painting     §11.2a
//   z=3  B@(270,85,483,490) src(0,490)      — server listbox (hidden boot)  §11.2a
//   z=4  D channel-block pair (hidden boot)                                  §11.2b
//   z=10 A@(0,326,1024,442) src(0,582)      — lower baked-art backdrop      §11.2h / §11.2e
//   z=11 A@(456,166,112,39) src(154,398)    — notice/agreement btn  act 102 §11.2e
//   z=12 [inner form panel at band-relative Y=0]
//        A@(340,30,38,13) src(0,398)        — 아이디 label art               §11.2e
//        A@(507,30,49,13) src(38,398)       — 비밀번호 label art              §11.2e
//        A@(619,86,67,13) src(87,398)       — small decor plate              §11.2e
//        A@(0,469,494,113) src(265,0)       — login background plate image   §11.2e
//        A@(390,32,102,13) frm src(615,404) — ID text field                  §11.2e
//        A@(568,32,102,13) frm src(615,404) — PW text field (masked *)       §11.2e
//        A@(694,86,13,13) src(717,398/on 730,398) — Save-ID checkbox act 104 §11.2e
//        B@(40,82,110,38) src(520,492)      — option tab 1    act 111        §11.2f
//        B@(164,82,110,38) src(750,492)     — option tab 2    act 112        §11.2f
//        A@(67,48,178,13) src(0,437)        — deco plate 1                   §11.2f
//        A@(0,100,313,32) src(289,437)      — deco plate 2                   §11.2f
//   z=17 A@(456,64,112,39) src(266,398)    — login/OK btn     act 103       §11.2e
//   z=19 C@(342,289,340,190) src(318,647)  — quit-confirm modal (hidden)    §11.2d
//        C@(120,136,113,40) src(302,900)   — quit yes #1      act 113       §11.2d
//        C@(120,136,113,40) src(302,860)   — quit yes #2      act 114       §11.2d
//   z=100  two black curtain bars (slide open §1.5a)
//
// RULES: NO fallback ColorRect, NO English text labels, NO EULA panel,
//        NO invented quit button, NO toast label. Missing asset → log + SKIP.
//
// PUBLIC API preserved (BootFlow signals):
//   LoginAccepted(string account)
//   ServerListRequested(string account)
//   QuitRequested()
//   SharedAssets / Audio
//
// spec: Docs/RE/specs/frontend_scenes.md §11.0–§11.2 (CODE-CONFIRMED literals).
//       §1.2 (action ids), §1.4 (validation), §1.5a (curtain), §1.6 (Save-ID),
//       §1.8 (quit), §1.9 (msg ids).

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Login screen on the 1024×768 reference canvas.  Pure VFS-atlas view; emits use-case intents.
/// spec: Docs/RE/specs/frontend_scenes.md §11.2. CODE-CONFIRMED.
/// </summary>
public sealed partial class LoginScreen : Control
{
    // -------------------------------------------------------------------------
    // Public signals — preserved for BootFlow wiring.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised when OK/Login (action 103) is pressed and local validation passes (ID≥4, PW≥1).
    /// spec: Docs/RE/specs/frontend_scenes.md §1.4. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void LoginAcceptedEventHandler(string account);

    /// <summary>
    /// Raised when the notice/agreement button (action 102) is clicked.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.2 action 102. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void ServerListRequestedEventHandler(string account);

    /// <summary>
    /// Raised when quit-confirm Yes #1 (action 113) or #2 (action 114) is clicked.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.8. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void QuitRequestedEventHandler();

    // -------------------------------------------------------------------------
    // Curtain constants — two-edge letterbox OPEN animation.
    // spec: Docs/RE/specs/frontend_scenes.md §1.5a. CODE-CONFIRMED.
    //
    // Shared accumulator C advances +5/tick = 300 px/s (wall-clock deterministic).
    // TOP Y = −C.  BOTTOM Y = C + 326.
    // Complete when C > 222 (≈ 45 ticks @ +5/tick).
    // spec §1.5a. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private const float CurtainSpeed         = 300f; // px/s (+5/tick @ 60 fps). spec §1.5a.
    private const float CurtainCompleteThresh = 222f; // spec §1.5a. CODE-CONFIRMED.
    private const int   CurtainH             = 222;   // bar height = |endY − startY|. spec §1.5a.
    private const int   CurtainBotBase       = 326;   // bottom bar base Y. spec §1.5a. CODE-CONFIRMED.

    // -------------------------------------------------------------------------
    // Public read: BootFlow queries whether the curtain has passed Y>200.
    // spec §1.5a "reveal threshold 200". CODE-CONFIRMED.
    // -------------------------------------------------------------------------
    public bool CurtainRevealThresholdPassed => _curtainAccum > 200f;

    // -------------------------------------------------------------------------
    // Shared asset loader (injected by BootFlow or created locally).
    // -------------------------------------------------------------------------
    public UiAssetLoader? SharedAssets { get; set; }

    /// <summary>
    /// Optionally inject the shared front-end audio node.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 1 — play SFX 861010105. CODE-CONFIRMED.
    /// </summary>
    public FrontEndAudio? Audio { get; set; }

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private LineEdit _accountEdit = null!;
    private LineEdit _passwordEdit = null!;

    // Save-ID view flag — purely presentational (no domain state).
    private bool _saveIdChecked;

    // Quit-confirm modal alpha ramp state. spec §11.2g. CODE-CONFIRMED.
    private Control? _quitModal;
    private int _quitModalAlpha;  // current [0..255]
    private int _quitModalTarget; // 255 = showing, 0 = hiding

    // Two curtain bars. spec §1.5a. CODE-CONFIRMED.
    private ColorRect? _curtainTop;
    private ColorRect? _curtainBot;
    private float      _curtainAccum;
    private bool       _curtainOpen;

    private UiAssetLoader _assets = null!;
    private bool          _ownsAssets;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        _assets     = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        // Preload the four login atlases before any widget construction.
        // spec: Docs/RE/specs/frontend_scenes.md §11.1 / §9.0. CODE-CONFIRMED.
        _assets.PreloadLoginAtlases();

        // Load saved id before building UI so AccountEdit can be pre-filled.
        // spec: Docs/RE/specs/frontend_scenes.md §1.6. CODE-CONFIRMED.
        string savedId = LoadSavedId();

        try
        {
            BuildUi(savedId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScreen] BuildUi failed: {ex.Message}");
        }

        // Play login-curtain SFX 861010105 at sub-state 1 entry.
        // spec: Docs/RE/specs/frontend_scenes.md §1.5 sub-state 1. CODE-CONFIRMED.
        Audio?.PlayLoginCurtainSfx();

        // Default focus: ID box when no saved id, PW box when id is pre-filled.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2e "Default input focus". CODE-CONFIRMED.
        if (_accountEdit is not null && _passwordEdit is not null)
        {
            if (string.IsNullOrEmpty(savedId))
                _accountEdit.CallDeferred(Control.MethodName.GrabFocus);
            else
                _passwordEdit.CallDeferred(Control.MethodName.GrabFocus);
        }

        GD.Print($"[LoginScreen] Built; vfs={(_assets.HasVfs ? "real-atlas" : "offline-skip")}.");
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    /// <summary>
    /// Enter key = login (same as OK button). Tab = swap ID ↔ PW focus.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.1 — event class 1, id 9/10. CODE-CONFIRMED.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed) return;

        if (key.PhysicalKeycode is Key.Enter or Key.KpEnter)
        {
            GetViewport().SetInputAsHandled();
            OnOkPressed();
            return;
        }

        if (key.PhysicalKeycode == Key.Tab)
        {
            GetViewport().SetInputAsHandled();
            if (_accountEdit.HasFocus()) _passwordEdit.GrabFocus();
            else                         _accountEdit.GrabFocus();
        }
    }

    /// <summary>
    /// Per-frame: curtain slide + quit-modal alpha ramp.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.5a / §11.2g. CODE-CONFIRMED.
    /// </summary>
    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // --- Curtain two-edge vertical OPEN slide. spec §1.5a. CODE-CONFIRMED. ---
        if (!_curtainOpen)
        {
            _curtainAccum = Math.Min(_curtainAccum + CurtainSpeed * dt, CurtainCompleteThresh);

            if (_curtainTop is not null)
                _curtainTop.Position = new Vector2(0, -_curtainAccum);
            if (_curtainBot is not null)
                _curtainBot.Position = new Vector2(0, _curtainAccum + CurtainBotBase);

            if (_curtainAccum >= CurtainCompleteThresh)
            {
                if (_curtainTop is not null) { _curtainTop.Position = new Vector2(0, -222f); _curtainTop.Visible = false; }
                if (_curtainBot is not null) { _curtainBot.Position = new Vector2(0, 548f);  _curtainBot.Visible = false; }
                _curtainOpen = true;
                GD.Print("[LoginScreen] Curtain fully open (C>222). spec §1.5a. CODE-CONFIRMED.");
            }
        }

        // --- Quit-confirm modal alpha ramp ±64/frame. spec §11.2g. CODE-CONFIRMED. ---
        if (_quitModal is null) return;
        if (_quitModalAlpha == _quitModalTarget) return;

        int step = LoginLayout.DialogFadeStep;
        if (_quitModalTarget > _quitModalAlpha)
            _quitModalAlpha = Math.Min(_quitModalAlpha + step, LoginLayout.DialogAlphaVisible);
        else
            _quitModalAlpha = Math.Max(_quitModalAlpha - step, LoginLayout.DialogAlphaHidden);

        _quitModal.Modulate = new Color(1f, 1f, 1f, _quitModalAlpha / 255f);

        if (_quitModalAlpha > 0 && !_quitModal.Visible)  _quitModal.Visible = true;
        if (_quitModalAlpha == 0 && _quitModal.Visible)  _quitModal.Visible = false;
    }

    // -------------------------------------------------------------------------
    // UI construction — §11.2 layer by layer, back-to-front.
    // NO fallback ColorRect, NO English synthetic text, NO invented elements.
    // Missing VFS atlas → GD.PrintErr + skip that element.  Never crash.
    // -------------------------------------------------------------------------

    private void BuildUi(string savedId)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // =======================================================================
        // z=1  UPPER BACKDROP — login_slice1.dds (A) @(0,0,1024,398) src(0,0).
        // The carved bezel / hanging rings / red flag / URL are baked into this art.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2h / §11.2b. CODE-CONFIRMED.
        // =======================================================================
        AddAtlasSprite(this, "UpperBackdrop",
            LoginLayout.AtlasLoginSlice1, 0, 0,
            0, 0, 1024, 398);

        // =======================================================================
        // z=2  CENTRAL PAINTING — loginwindow.dds (B) @(0,110,1024,490) src(0,0).
        // Ink-wash warrior + landscape + red ribbon. Covers backdrop Y=110..600.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2a "Main panel art". CODE-CONFIRMED.
        // =======================================================================
        AddAtlasSprite(this, "MainPanelArt",
            LoginLayout.AtlasLoginWindow, 0, 0,
            0, 110, 1024, 490);

        // =======================================================================
        // z=3  SERVER LISTBOX — B@(270,85,483,490) src(0,490). Hidden at boot.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2a. CODE-CONFIRMED.
        // =======================================================================
        {
            AtlasTexture? listTex = _assets.Slice(LoginLayout.AtlasLoginWindow, 0, 490, 483, 490);
            if (listTex is not null)
            {
                var listRect = MakeSprite("ServerListbox", listTex, 270, 85, 483, 490);
                listRect.Visible = false; // hidden at boot. spec §1.5. CODE-CONFIRMED.
                AddChild(listRect);
            }
            else GD.PrintErr("[LoginScreen] loginwindow.dds slice(0,490) null — server listbox skipped.");
        }

        // =======================================================================
        // z=4  CHANNEL BLOCKS x2 (hidden at boot) — D loginwindow_02.dds.
        // Body src-U starts 448, step +124. Plate src(9,6)/hover(220,6). actions 400/401.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2b. CODE-CONFIRMED.
        // =======================================================================
        for (int blk = 0; blk < 2; blk++)
        {
            int blockX   = 30 + blk * 233;  // spec §11.2b "block X starts 30, step +233". CODE-CONFIRMED.
            int bodySrcU = 448 + blk * 124; // spec §11.2b "body src-U starts 448, step +124". CODE-CONFIRMED.

            // Channel block body — D@(X+47,97,100,372) src(bodySrcU,6).
            AtlasTexture? bodyTex = _assets.Slice(LoginLayout.AtlasLoginWindow02, bodySrcU, 6, 100, 372);
            if (bodyTex is not null)
            {
                var body = MakeSprite($"ChannelBlock{blk}Body", bodyTex, blockX + 47, 97, 100, 372);
                body.Visible = false; // hidden until server-list sub-state. spec §1.5.
                AddChild(body);
            }
            else GD.PrintErr($"[LoginScreen] loginwindow_02.dds block{blk} body null — skipped.");
        }

        // =======================================================================
        // z=10  LOWER BACKDROP — login_slice1.dds (A) @(0,326,1024,442) src(0,582).
        // Stone form-band. Y = round(326 × screenH/768); on 1024×768 canvas = 326.
        // MUST come after painting so stone overlays ink-wash in Y=326..600 overlap.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2h "Lower backdrop". CODE-CONFIRMED.
        //       §11.2e "Bottom login-bar panel" Y scales with screen height. CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? bottomTex = _assets.Slice(
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.BottomBarSrcX, LoginLayout.BottomBarSrcY,
            LoginLayout.BottomBarW, LoginLayout.BottomBarH);

        if (bottomTex is not null)
        {
            AddChild(MakeSprite("LowerBackdrop", bottomTex,
                0, LoginLayout.BottomBarCanvasY,
                LoginLayout.BottomBarW, LoginLayout.BottomBarH));
        }
        else GD.PrintErr("[LoginScreen] login_slice1.dds slice(0,582) null — lower backdrop skipped.");

        // =======================================================================
        // z=11  NOTICE/AGREEMENT BUTTON — A@(456,166,112,39) src(154,398) action 102.
        // spec §11.2e "Notice / agreement button". CODE-CONFIRMED.
        // 3-state frame order: NORMAL(154,398) / PRESSED(378,398) / HOVER(378,398).
        // spec §1.4b: order is NORMAL/PRESSED/HOVER. Hover==Pressed here. CODE-CONFIRMED.
        // =======================================================================
        {
            var noticeBtn = WidgetFactory.MakeStateButton(
                _assets, LoginLayout.AtlasLoginSlice1,
                LoginLayout.ConfirmButton.X, LoginLayout.ConfirmButton.Y,
                LoginLayout.ConfirmButton.W, LoginLayout.ConfirmButton.H,
                LoginLayout.ConfirmButton.SrcX, LoginLayout.ConfirmButton.SrcY, // NORMAL (154,398)
                LoginLayout.ConfirmHoverSrcX,   LoginLayout.ConfirmHoverSrcY,   // HOVER  (378,398)
                LoginLayout.ConfirmHoverSrcX,   LoginLayout.ConfirmHoverSrcY,   // PRESSED(378,398)
                LoginLayout.ActionConfirm,
                caption: "", captionTint: Colors.White);
            noticeBtn.Name = "NoticeBtn";
            noticeBtn.ActionFired += _ => OnNoticePressed();
            AddChild(noticeBtn);
        }

        // =======================================================================
        // z=12  FORM BAND CONTAINER — transparent panel at canvas Y=326.
        // All interactive form controls are children of this panel.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2g z=12 (inner form). CODE-CONFIRMED.
        // =======================================================================
        var formBand = new Control
        {
            Name     = "FormBand",
            Position = new Vector2(0, LoginLayout.BottomBarCanvasY),
            Size     = new Vector2(LoginLayout.RefWidth, LoginLayout.BottomBarH),
        };
        AddChild(formBand);

        // --- Baked Korean label art plates (no runtime text — baked into atlas A). ---
        // spec §11.2e. CODE-CONFIRMED.

        // A@(340,30,38,13) src(0,398) — 아이디 label. spec §11.2e. CODE-CONFIRMED.
        AddAtlasSprite(formBand, "AccountLabelArt",
            LoginLayout.AtlasLoginSlice1, 0, 398,
            LoginLayout.AccountLabelArt.X, LoginLayout.AccountLabelArt.Y,
            LoginLayout.AccountLabelArt.W, LoginLayout.AccountLabelArt.H);

        // A@(507,30,49,13) src(38,398) — 비밀번호 label. spec §11.2e. CODE-CONFIRMED.
        AddAtlasSprite(formBand, "PasswordLabelArt",
            LoginLayout.AtlasLoginSlice1, 38, 398,
            LoginLayout.PasswordLabelArt.X, LoginLayout.PasswordLabelArt.Y,
            LoginLayout.PasswordLabelArt.W, LoginLayout.PasswordLabelArt.H);

        // A@(619,86,67,13) src(87,398) — small decoration plate. spec §11.2e. CODE-CONFIRMED.
        AddAtlasSprite(formBand, "SmallDecorPlate",
            LoginLayout.AtlasLoginSlice1, 87, 398,
            LoginLayout.SmallDecorPlate.X, LoginLayout.SmallDecorPlate.Y,
            LoginLayout.SmallDecorPlate.W, LoginLayout.SmallDecorPlate.H);

        // A@(0,469,494,113) src(265,0) — login background plate image.
        // The plate the ID/PW row sits on. spec §11.2e. CODE-CONFIRMED.
        AddAtlasSprite(formBand, "LoginBgPlate",
            LoginLayout.AtlasLoginSlice1, 265, 0,
            LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y,
            LoginLayout.ConfirmFacePlate.W, LoginLayout.ConfirmFacePlate.H);

        // --- Option tab buttons (§11.2f). ---
        // B@(40,82,110,38) src(520,492) NORMAL / (635,492) HOVER action 111.
        // spec §11.2f. CODE-CONFIRMED.
        {
            var tab1 = WidgetFactory.MakeStateButton(
                _assets, LoginLayout.AtlasLoginWindow,
                LoginLayout.OptionTab1.X, LoginLayout.OptionTab1.Y,
                LoginLayout.OptionTab1.W, LoginLayout.OptionTab1.H,
                LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY,         // NORMAL (520,492)
                LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY, // HOVER  (635,492)
                LoginLayout.OptionTab1.SrcX,     LoginLayout.OptionTab1.SrcY,     // PRESSED = NORMAL
                LoginLayout.ActionOptionTab1,
                caption: "", captionTint: Colors.White);
            tab1.Name = "OptionTab1";
            formBand.AddChild(tab1);
        }

        // B@(164,82,110,38) src(750,492) NORMAL / (865,492) HOVER action 112.
        // spec §11.2f. CODE-CONFIRMED.
        {
            var tab2 = WidgetFactory.MakeStateButton(
                _assets, LoginLayout.AtlasLoginWindow,
                LoginLayout.OptionTab2.X, LoginLayout.OptionTab2.Y,
                LoginLayout.OptionTab2.W, LoginLayout.OptionTab2.H,
                LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY,         // NORMAL (750,492)
                LoginLayout.OptionTab2HoverSrcX, LoginLayout.OptionTab2HoverSrcY, // HOVER  (865,492)
                LoginLayout.OptionTab2.SrcX,     LoginLayout.OptionTab2.SrcY,     // PRESSED = NORMAL
                LoginLayout.ActionOptionTab2,
                caption: "", captionTint: Colors.White);
            tab2.Name = "OptionTab2";
            formBand.AddChild(tab2);
        }

        // --- Decoration image plates §11.2f (baked art). ---
        // A@(67,48,178,13) src(0,437). spec §11.2f. CODE-CONFIRMED.
        AddAtlasSprite(formBand, "DecoPlate1",
            LoginLayout.AtlasLoginSlice1, LoginLayout.DecoPlate1.SrcX, LoginLayout.DecoPlate1.SrcY,
            LoginLayout.DecoPlate1.X, LoginLayout.DecoPlate1.Y,
            LoginLayout.DecoPlate1.W, LoginLayout.DecoPlate1.H);

        // A@(0,100,313,32) src(289,437). spec §11.2f. CODE-CONFIRMED.
        AddAtlasSprite(formBand, "DecoPlate2",
            LoginLayout.AtlasLoginSlice1, LoginLayout.DecoPlate2.SrcX, LoginLayout.DecoPlate2.SrcY,
            LoginLayout.DecoPlate2.X, LoginLayout.DecoPlate2.Y,
            LoginLayout.DecoPlate2.W, LoginLayout.DecoPlate2.H);

        // --- Edit-field frame art (src login_slice1.dds A src(615,404,102,13)). ---
        // Both ID and PW frames share the same source rect. spec §11.2e atlas note. CODE-CONFIRMED.
        AddAtlasSprite(formBand, "AccountFrameArt",
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.EditFieldFrameSrcX, LoginLayout.EditFieldFrameSrcY,
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.EditFieldFrameW, LoginLayout.EditFieldFrameH);

        AddAtlasSprite(formBand, "PasswordFrameArt",
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.EditFieldFrameSrcX, LoginLayout.EditFieldFrameSrcY,
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.EditFieldFrameW, LoginLayout.EditFieldFrameH);

        // --- ID input field (§11.2e). dest(390,32,102,13). plain text. maxlen 16. action 109. ---
        // spec §11.2e. CODE-CONFIRMED.
        _accountEdit = MakeTextbox(masked: false, maxLen: LoginLayout.IdMaxLength);
        _accountEdit.Name     = "AccountEdit";
        _accountEdit.Position = new Vector2(LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y);
        _accountEdit.Size     = new Vector2(LoginLayout.AccountBox.W, LoginLayout.TextboxRenderH);
        if (!string.IsNullOrEmpty(savedId))
            _accountEdit.Text = savedId;
        formBand.AddChild(_accountEdit);

        // --- PW input field (§11.2e). dest(568,32,102,13). masked *. maxlen 12. action 110. ---
        // spec §11.2e "Password masking — one ASCII asterisk per character". CODE-CONFIRMED.
        _passwordEdit = MakeTextbox(masked: true, maxLen: LoginLayout.PwMaxLength);
        _passwordEdit.Name            = "PasswordEdit";
        _passwordEdit.Position        = new Vector2(LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y);
        _passwordEdit.Size            = new Vector2(LoginLayout.PasswordBox.W, LoginLayout.TextboxRenderH);
        _passwordEdit.SecretCharacter = "*"; // ASCII * per spec §11.2e. CODE-CONFIRMED.
        formBand.AddChild(_passwordEdit);

        // --- Save-ID checkbox (§11.2e). A@(694,86,13,13) src off(717,398)/on(730,398). action 104. ---
        // spec §11.2e. CODE-CONFIRMED.
        {
            var cbx = WidgetFactory.MakeStateButton(
                _assets, LoginLayout.AtlasLoginSlice1,
                LoginLayout.SaveIdCheck.X,    LoginLayout.SaveIdCheck.Y,
                LoginLayout.SaveIdCheck.W,    LoginLayout.SaveIdCheck.H,
                LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY, // NORMAL off (717,398)
                LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY, // HOVER  = NORMAL
                LoginLayout.SaveIdCheckedSrcX, LoginLayout.SaveIdCheckedSrcY, // PRESSED on (730,398)
                LoginLayout.ActionSaveId,
                caption: "", captionTint: Colors.White);
            cbx.Name = "SaveIdCheckbox";
            _saveIdChecked = !string.IsNullOrEmpty(savedId);
            cbx.ActionFired += _ => OnSaveIdToggled();
            formBand.AddChild(cbx);
        }

        // =======================================================================
        // z=17  LOGIN/OK BUTTON — A@(456,64,112,39) src NORMAL(266,398) action 103.
        // spec §11.2e "Login / confirm button (gold)". CODE-CONFIRMED.
        // 3-state frame order: NORMAL(266,398) / PRESSED(490,398) / HOVER(490,398).
        // spec §1.4b: order NORMAL/PRESSED/HOVER. Hover==Pressed here. CODE-CONFIRMED.
        // =======================================================================
        {
            var loginBtn = WidgetFactory.MakeStateButton(
                _assets, LoginLayout.AtlasLoginSlice1,
                LoginLayout.OkButton.X, LoginLayout.OkButton.Y,
                LoginLayout.OkButton.W, LoginLayout.OkButton.H,
                LoginLayout.OkButton.SrcX, LoginLayout.OkButton.SrcY, // NORMAL  (266,398)
                LoginLayout.OkHoverSrcX,   LoginLayout.OkHoverSrcY,   // HOVER   (490,398)
                LoginLayout.OkHoverSrcX,   LoginLayout.OkHoverSrcY,   // PRESSED (490,398)
                LoginLayout.ActionOk,
                caption: "", captionTint: Colors.White);
            loginBtn.Name = "LoginButton";
            loginBtn.ActionFired += _ => OnOkPressed();
            AddChild(loginBtn);
        }

        // =======================================================================
        // z=19  QUIT-CONFIRM MODAL — C@(342,289,340,190) src(318,647). Hidden at boot.
        // spec §11.2d. CODE-CONFIRMED.
        // =======================================================================
        _quitModal = BuildQuitConfirmModal();
        _quitModal.Visible = false;
        _quitModal.Modulate = new Color(1f, 1f, 1f, 0f);
        _quitModalAlpha  = LoginLayout.DialogAlphaHidden;
        _quitModalTarget = LoginLayout.DialogAlphaHidden;
        AddChild(_quitModal);

        // =======================================================================
        // z=100  CURTAIN BARS — two full-width black ColorRects.
        // Top: start Y=0, slides to Y=−222. Bottom: start Y=326, slides to Y=548.
        // spec: Docs/RE/specs/frontend_scenes.md §1.5a. CODE-CONFIRMED.
        // These are the ONLY ColorRects permitted — they are part of the spec animation.
        // =======================================================================
        _curtainTop = new ColorRect
        {
            Name     = "CurtainTop",
            Color    = Colors.Black,
            Position = new Vector2(0, 0),
            Size     = new Vector2(LoginLayout.RefWidth, CurtainH),
            ZIndex   = 100,
        };
        AddChild(_curtainTop);

        _curtainBot = new ColorRect
        {
            Name     = "CurtainBot",
            Color    = Colors.Black,
            Position = new Vector2(0, CurtainBotBase),
            Size     = new Vector2(LoginLayout.RefWidth, CurtainH),
            ZIndex   = 100,
        };
        AddChild(_curtainBot);

        _curtainAccum = 0f;
        _curtainOpen  = false;
    }

    // -------------------------------------------------------------------------
    // Quit-confirm modal (§11.2d).
    // Chrome: C@(342,289,340,190) src(318,647).
    // Buttons: yes#1 C@(120,136,113,40) src(302,900) act 113;
    //          yes#2 C@(120,136,113,40) src(302,860) act 114.
    // NO invented Cancel button, NO English text fallback.
    // spec: Docs/RE/specs/frontend_scenes.md §11.2d. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private Control BuildQuitConfirmModal()
    {
        var modal = new Control
        {
            Name     = "QuitConfirmModal",
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size     = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
        };

        // Chrome background — C src(318,647) 340×190.
        // spec §11.2d "Dialog #1 panel (notice)". CODE-CONFIRMED.
        AtlasTexture? chrome = _assets.Slice(
            LoginLayout.AtlasInventWindow,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY,
            LoginLayout.ModalChromeW, LoginLayout.ModalChromeH);

        if (chrome is not null)
        {
            var chromeBg = new TextureRect
            {
                Name        = "ModalChrome",
                Texture     = chrome,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chromeBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            modal.AddChild(chromeBg);
        }
        else GD.PrintErr("[LoginScreen] InventWindow.dds slice(318,647) null — modal chrome skipped.");

        // Dialog body caption label — msg id 4023.
        // NO hardcoded English text: text() returns VFS msg.xdb caption or empty string.
        // spec §11.2d "Dialog #1 body text" caption 4023. CODE-CONFIRMED.
        {
            string caption = _assets.Text(LoginLayout.MsgQuitConfirm1, "");
            if (!string.IsNullOrEmpty(caption))
            {
                var lbl = WidgetFactory.MakeLabel(caption, LoginLayout.FontBodyHeight,
                    new Color(0.9f, 0.9f, 0.9f));
                lbl.Position             = new Vector2(10, 80);
                lbl.Size                 = new Vector2(320, 20);
                lbl.HorizontalAlignment  = HorizontalAlignment.Center;
                modal.AddChild(lbl);
            }
        }

        // Dialog body caption label — msg id 4024.
        // spec §11.2d "Dialog #2 body text" caption 4024. CODE-CONFIRMED.
        {
            string caption = _assets.Text(LoginLayout.MsgQuitConfirm2, "");
            if (!string.IsNullOrEmpty(caption))
            {
                var lbl = WidgetFactory.MakeLabel(caption, LoginLayout.FontBodyHeight,
                    new Color(0.75f, 0.75f, 0.75f));
                lbl.Position             = new Vector2(10, 100);
                lbl.Size                 = new Vector2(320, 20);
                lbl.HorizontalAlignment  = HorizontalAlignment.Left;
                modal.AddChild(lbl);
            }
        }

        // Yes button #1 — C@(120,136,113,40) src NORMAL(302,900) HOVER(302,900) PRESSED(415,900) action 113.
        // spec §11.2d "Dialog #1 OK". CODE-CONFIRMED.
        {
            var yes1 = WidgetFactory.MakeStateButton(
                _assets, LoginLayout.AtlasInventWindow,
                LoginLayout.QuitConfirmYes1.X, LoginLayout.QuitConfirmYes1.Y,
                LoginLayout.QuitConfirmYes1.W, LoginLayout.QuitConfirmYes1.H,
                LoginLayout.QuitConfirmYes1.SrcX,     LoginLayout.QuitConfirmYes1.SrcY,     // NORMAL (302,900)
                LoginLayout.QuitConfirmYes1.SrcX,     LoginLayout.QuitConfirmYes1.SrcY,     // HOVER  = NORMAL
                LoginLayout.QuitConfirmYes1HoverSrcX, LoginLayout.QuitConfirmYes1HoverSrcY, // PRESSED(415,900)
                LoginLayout.ActionQuitConfirmYes1,
                caption: "", captionTint: Colors.White);
            yes1.Name = "QuitYes1";
            yes1.ActionFired += _ => OnQuitConfirmed();
            modal.AddChild(yes1);
        }

        // Yes button #2 — C@(120,136,113,40) src NORMAL(302,860) HOVER(302,860) PRESSED(415,860) action 114.
        // spec §11.2d "Dialog #2 OK". CODE-CONFIRMED.
        {
            var yes2 = WidgetFactory.MakeStateButton(
                _assets, LoginLayout.AtlasInventWindow,
                LoginLayout.QuitConfirmYes2.X, LoginLayout.QuitConfirmYes2.Y,
                LoginLayout.QuitConfirmYes2.W, LoginLayout.QuitConfirmYes2.H,
                LoginLayout.QuitConfirmYes2.SrcX,     LoginLayout.QuitConfirmYes2.SrcY,     // NORMAL (302,860)
                LoginLayout.QuitConfirmYes2.SrcX,     LoginLayout.QuitConfirmYes2.SrcY,     // HOVER  = NORMAL
                LoginLayout.QuitConfirmYes2HoverSrcX, LoginLayout.QuitConfirmYes2HoverSrcY, // PRESSED(415,860)
                LoginLayout.ActionQuitConfirmYes2,
                caption: "", captionTint: Colors.White);
            yes2.Name = "QuitYes2";
            yes2.ActionFired += _ => OnQuitConfirmed();
            modal.AddChild(yes2);
        }

        return modal;
    }

    // -------------------------------------------------------------------------
    // Intent handlers — passive view: turns gestures into use-case calls.
    // -------------------------------------------------------------------------

    private void OnOkPressed()
    {
        // game.ver version gate (runs first). spec §1.4. CODE-CONFIRMED.
        if (!CheckGameVersion())
        {
            // Version mismatch → show msg 2204 via msg.xdb if available, then abort.
            // spec §1.4 "On mismatch: Win32 modal error box msg 2204". CODE-CONFIRMED.
            GD.PrintErr("[LoginScreen] game.ver mismatch (msg 2204). Login aborted. spec §1.4.");
            // NOTE: A faithful impl shows msg 2204 as an OS modal and quits the client.
            // In the revival we log and return (no client quit in offline/dev flow).
            return;
        }

        string account = _accountEdit.Text.Trim();

        // Persist Save-ID if checkbox checked (before validation). spec §1.4 step 2. CODE-CONFIRMED.
        if (_saveIdChecked && !string.IsNullOrEmpty(account))
            PersistSavedId(account);

        // ID length < 4 → msg 4025 → return to sub-state 6. spec §1.4 / §1.9. CODE-CONFIRMED.
        if (account.Length < LoginLayout.MinIdLength)
        {
            GD.Print($"[LoginScreen] ID too short ({account.Length} < {LoginLayout.MinIdLength}), msg 4025. spec §1.4.");
            return;
        }

        // PW length < 1 → msg 4026 → return to sub-state 6. spec §1.4 / §1.9. CODE-CONFIRMED.
        if (_passwordEdit.Text.Length < LoginLayout.MinPwLength)
        {
            GD.Print("[LoginScreen] PW empty, msg 4026. spec §1.4.");
            return;
        }

        GD.Print($"[LoginScreen] Login OK (account='{account}'). Emitting LoginAccepted.");
        EmitSignal(SignalName.LoginAccepted, account);
    }

    private void OnNoticePressed()
    {
        // Notice/agreement button (action 102) — open the server-select / notice flow.
        // spec §1.2 "Server-list button, action 102 → reveal the server-list panel". CODE-CONFIRMED.
        string account = _accountEdit.Text.Trim();
        GD.Print($"[LoginScreen] Notice/server-list (action 102, account='{account}'). Emitting ServerListRequested.");
        EmitSignal(SignalName.ServerListRequested, account);
    }

    private void OnSaveIdToggled()
    {
        // Toggle Save-ID and persist/clear. spec §1.6. CODE-CONFIRMED.
        _saveIdChecked = !_saveIdChecked;
        if (_saveIdChecked)
        {
            string acct = _accountEdit?.Text.Trim() ?? "";
            if (!string.IsNullOrEmpty(acct)) PersistSavedId(acct);
        }
        else
        {
            ClearSavedId();
        }
        GD.Print($"[LoginScreen] Save-ID toggled: {_saveIdChecked}. spec §1.6.");
    }

    private void OnQuitConfirmed()
    {
        // Quit-confirm Yes (actions 113 or 114) → engine state 6 / substate 8.
        // spec §1.8. CODE-CONFIRMED.
        GD.Print("[LoginScreen] Quit confirmed (actions 113/114). Emitting QuitRequested. spec §1.8.");
        _quitModalTarget = LoginLayout.DialogAlphaHidden;
        EmitSignal(SignalName.QuitRequested);
    }

    // -------------------------------------------------------------------------
    // game.ver version gate (§1.4). Binary 28-byte = 7×u32 LE comparison.
    // spec: Docs/RE/specs/frontend_scenes.md §1.4 / §1.4b. CODE-CONFIRMED gate.
    // -------------------------------------------------------------------------

    // VFS path for the version file. spec §1.4. CODE-CONFIRMED.
    private const string GameVerVfsPath  = "data/cursor/game.ver"; // spec §1.4.
    private const int    GameVerSizeBytes = 28; // 7 × u32 LE. spec §1.4b. CODE-CONFIRMED.

    private bool CheckGameVersion()
    {
        // If VFS offline → gate passes. spec §1.4 "VFS not mounted → continue". CODE-CONFIRMED.
        if (!_assets.HasVfs)
        {
            GD.Print("[LoginScreen] Version gate: VFS offline — gate passes. spec §1.4.");
            return true;
        }

        ReadOnlyMemory<byte> vfsBytes = _assets.GetRaw(GameVerVfsPath);
        if (vfsBytes.IsEmpty)
        {
            GD.Print("[LoginScreen] Version gate: game.ver absent in VFS — degrading (allow). spec §1.4.");
            return true;
        }

        if (vfsBytes.Length != GameVerSizeBytes)
        {
            GD.PrintErr($"[LoginScreen] Version gate: VFS game.ver wrong size ({vfsBytes.Length}). Degrading (allow).");
            return true;
        }

        // Read the on-disk copy.
        ReadOnlyMemory<byte> diskBytes = ReadDiskGameVer("data/cursor/game.ver");
        if (diskBytes.IsEmpty)
        {
            GD.Print("[LoginScreen] Version gate: on-disk game.ver absent — degrading (allow). spec §1.4.");
            return true;
        }

        if (diskBytes.Length != GameVerSizeBytes)
        {
            GD.PrintErr($"[LoginScreen] Version gate: on-disk game.ver wrong size ({diskBytes.Length}). Degrading (allow).");
            return true;
        }

        // 7×u32 LE field-by-field compare. spec §1.4b. CODE-CONFIRMED.
        ReadOnlySpan<byte> v = vfsBytes.Span;
        ReadOnlySpan<byte> d = diskBytes.Span;
        for (int f = 0; f < 7; f++)
        {
            int o = f * 4;
            uint vf = (uint)(v[o] | (v[o+1] << 8) | (v[o+2] << 16) | (v[o+3] << 24));
            uint df = (uint)(d[o] | (d[o+1] << 8) | (d[o+2] << 16) | (d[o+3] << 24));
            if (vf != df)
            {
                GD.Print($"[LoginScreen] Version gate: MISMATCH field[{f}] VFS=0x{vf:X8} disk=0x{df:X8} → msg 2204. spec §1.4b.");
                return false;
            }
        }

        GD.Print("[LoginScreen] Version gate: all 7 u32 fields match. spec §1.4b. CODE-CONFIRMED.");
        return true;
    }

    private static ReadOnlyMemory<byte> ReadDiskGameVer(string relPath)
    {
        string[] candidates =
        [
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), relPath),
            System.IO.Path.Combine(global::Godot.OS.GetUserDataDir(), relPath),
        ];
        foreach (string c in candidates)
        {
            try
            {
                if (System.IO.File.Exists(c))
                    return System.IO.File.ReadAllBytes(c);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LoginScreen] ReadDiskGameVer '{c}': {ex.Message}");
            }
        }
        return ReadOnlyMemory<byte>.Empty;
    }

    // -------------------------------------------------------------------------
    // Save-ID persistence (§1.6) — Godot ConfigFile (≡ DoOption.ini [DO_OPTION] OPTION_ID).
    // spec: Docs/RE/specs/frontend_scenes.md §1.6. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private static string LoadSavedId()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(LoginLayout.SaveIdConfigPath) != Error.Ok) return string.Empty;
        string raw = (string)cfg.GetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            LoginLayout.SaveIdNullSentinel);
        return raw == LoginLayout.SaveIdNullSentinel ? string.Empty : raw;
    }

    private static void PersistSavedId(string account)
    {
        var cfg = new ConfigFile();
        cfg.Load(LoginLayout.SaveIdConfigPath);
        cfg.SetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey, account);
        cfg.Save(LoginLayout.SaveIdConfigPath);
        GD.Print($"[LoginScreen] Save-ID persisted '{account}'. spec §1.6.");
    }

    private static void ClearSavedId()
    {
        var cfg = new ConfigFile();
        cfg.Load(LoginLayout.SaveIdConfigPath);
        cfg.SetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey, LoginLayout.SaveIdNullSentinel);
        cfg.Save(LoginLayout.SaveIdConfigPath);
        GD.Print("[LoginScreen] Save-ID cleared (null sentinel). spec §1.6.");
    }

    // -------------------------------------------------------------------------
    // Quit-confirm modal show/hide (§11.2d). Alpha ramp driven in _Process.
    // -------------------------------------------------------------------------

    /// <summary>Fade in the quit-confirm modal. spec §11.2g ±64/frame. CODE-CONFIRMED.</summary>
    public void ShowQuitConfirmModal()
    {
        _quitModalTarget = LoginLayout.DialogAlphaVisible;
        if (_quitModal is not null) _quitModal.Visible = true;
        GD.Print("[LoginScreen] Quit confirm modal — fade in. spec §11.2g.");
    }

    /// <summary>Fade out the quit-confirm modal. spec §11.2g. CODE-CONFIRMED.</summary>
    public void HideQuitConfirmModal()
    {
        _quitModalTarget = LoginLayout.DialogAlphaHidden;
        GD.Print("[LoginScreen] Quit confirm modal — fade out. spec §11.2g.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Slice atlas + add a TextureRect to the parent.  No-op + log when VFS returns null.
    /// NO fallback ColorRect — skip silently on missing asset, per task rule 3.
    /// </summary>
    private void AddAtlasSprite(Control parent, string name, string atlasPath,
        int srcX, int srcY, int dstX, int dstY, int w, int h)
    {
        AtlasTexture? tex = _assets.Slice(atlasPath, srcX, srcY, w, h);
        if (tex is null)
        {
            GD.PrintErr($"[LoginScreen] Atlas slice null: {atlasPath} src({srcX},{srcY},{w},{h}) — '{name}' skipped.");
            return;
        }
        parent.AddChild(MakeSprite(name, tex, dstX, dstY, w, h));
    }

    private static TextureRect MakeSprite(string name, AtlasTexture tex, int x, int y, int w, int h)
    {
        return new TextureRect
        {
            Name        = name,
            Texture     = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Position    = new Vector2(x, y),
            Size        = new Vector2(w, h),
        };
    }

    /// <summary>
    /// LineEdit for ID/PW fields.  Spec places them at 102×13 px canvas-local; Godot LineEdit
    /// needs ≥18 px height to render text, so height is expanded to TextboxRenderH (22 px).
    /// The caret blinks at 1 Hz (500 ms on/off), driven by Godot's built-in CaretBlink.
    /// spec §11.2e "Caret behaviour". CODE-CONFIRMED.
    /// </summary>
    private static LineEdit MakeTextbox(bool masked, int maxLen)
    {
        var edit = new LineEdit
        {
            Secret    = masked,
            CaretBlink = true,
            MaxLength  = maxLen,
            Alignment  = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(102, 18),
        };

        // Transparent background — the atlas frame art behind renders the visual frame.
        var style = new StyleBoxFlat
        {
            BgColor     = new Color(0f, 0f, 0f, 0f), // transparent — frame art is behind
            BorderColor = new Color(0f, 0f, 0f, 0f),
        };
        style.SetBorderWidthAll(0);
        style.ContentMarginLeft   = 2;
        style.ContentMarginRight  = 2;
        style.ContentMarginTop    = 1;
        style.ContentMarginBottom = 1;
        edit.AddThemeStyleboxOverride("normal", style);
        edit.AddThemeStyleboxOverride("focus",  style);
        edit.AddThemeColorOverride("font_color",   new Color(0.95f, 0.90f, 0.75f));
        edit.AddThemeColorOverride("caret_color",  new Color(0.95f, 0.90f, 0.55f));
        edit.AddThemeColorOverride("selection_color", new Color(0.35f, 0.35f, 0.55f, 0.8f));

        return edit;
    }
}
