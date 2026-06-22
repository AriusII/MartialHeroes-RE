// Ui/Scenes/Login/LoginWindow.Widgets.cs
//
// Partial: widget/atlas-blit construction — Build* methods, modal helpers.
// spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.3

using Godot;
using MartialHeroes.Client.Godot.Ui.Widgets;
using MartialHeroes.Client.Presentation.Screens.Layout;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

public sealed partial class LoginWindow
{
    // -------------------------------------------------------------------------
    // Curtain panels
    // spec: §2.1 / §2.3
    // -------------------------------------------------------------------------

    private void BuildCurtainPanels()
    {
        // Top curtain: login_slice1.dds (A1) at (0,0,1024,398). Slides up (Y = −offset). spec §2.3.
        var top = HudWidgetFactory.MakeAtlasRect(_atlas,
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
        var bot = HudWidgetFactory.MakeAtlasRect(_atlas,
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
            Visible = false // init hidden. spec §2.1.
        };
        _backgroundLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // loginwindow.dds (A2) at (0,110,1024,490) src(0,0). spec §2.1.
        var backdrop = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.MainPanel.X, LoginLayout.MainPanel.Y,
            LoginLayout.MainPanel.W, LoginLayout.MainPanel.H,
            LoginLayout.MainPanel.SrcX, LoginLayout.MainPanel.SrcY);
        if (backdrop is not null) _backgroundLayer.AddChild(backdrop);

        AddChild(_backgroundLayer);
    }

    // -------------------------------------------------------------------------
    // Static central banner frame + logo (STATIC — TickCurtain never touches these)
    // Ground truth (debugger-confirmed IDB 263bd994):
    //   - Central banner frame: loginwindow.dds abs (270,85) 483×490 src(0,490) — STATIC.
    //   - Logo image: loginwindow.dds abs (477,129) 70×17 src(70,980) — child of frame, STATIC.
    // These are NEVER animated and must be visible from state 2 onward (same as _backgroundLayer).
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.3
    // -------------------------------------------------------------------------

    private void BuildBannerFrame()
    {
        // Full-canvas container at (270,85) — the frame sits at fixed canvas Y=85 always.
        // init hidden; shown from state 2 via ApplyVisibility (same policy as _backgroundLayer).
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 "Central banner frame | static | (270,85)"
        _bannerFrame = new Control
        {
            Name = "BannerFrame",
            Position = new Vector2(LoginLayout.ServerListbox.X, LoginLayout.ServerListbox.Y), // (270,85) spec §2.1
            Size = new Vector2(LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H),     // 483×490 spec §2.1
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false // init hidden; shown from state 2 via ApplyVisibility.
        };

        // Central banner frame art: loginwindow.dds src(0,490) at panel-local (0,0) = canvas (270,85).
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 "loginwindow.dds abs(270,85) 483×490 src(0,490)"
        var frameArt = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            0, 0,
            LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H,
            LoginLayout.ServerListbox.SrcX, LoginLayout.ServerListbox.SrcY);
        if (frameArt is not null)
        {
            frameArt.Name = "BannerFrameArt";
            _bannerFrame.AddChild(frameArt);
        }

        // Logo image: loginwindow.dds src(70,980) at panel-local (207,44) = canvas (477,129).
        // Child of the banner frame (inherits the frame's fixed position).
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 "logo abs(477,129) 70×17 src(70,980)"
        var logo = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginWindow,
            LoginLayout.ListboxHeader.X, LoginLayout.ListboxHeader.Y, // panel-local (207,44) → canvas (477,129)
            LoginLayout.ListboxHeader.W, LoginLayout.ListboxHeader.H, // 70×17
            LoginLayout.ListboxHeader.SrcX, LoginLayout.ListboxHeader.SrcY); // src(70,980)
        if (logo is not null)
        {
            logo.Name = "BannerLogo";
            _bannerFrame.AddChild(logo);
        }

        AddChild(_bannerFrame);
    }

    // -------------------------------------------------------------------------
    // Notice panel (the scrollable agreement/notice text column)
    // The frame art for this area is handled by BuildBannerFrame (STATIC, always visible).
    // This panel is ALWAYS HIDDEN per spec §2.1 "Notice panel | init hidden" — it contains
    // only the notice label text and scroll controls; the frame chrome is in _bannerFrame.
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
            Visible = false // init hidden — always hidden per spec §2.1.
        };

        // NOTE: the banner frame art (loginwindow.dds src(0,490)) is NOT in this panel — it is
        // built as the static _bannerFrame widget in BuildBannerFrame() so it stays visible always.
        // The notice panel contains ONLY the scroll controls and body labels (shown only when
        // the notice/agreement text is active, which is never in the current active flow).

        // Scroll controls. spec §2.1 "Scroll-up/down/thumb buttons".
        AddNoticeButton(LoginLayout.ScrollUpArrow, LoginLayout.ActionScrollUp);
        AddNoticeButton(LoginLayout.ScrollDownArrow, LoginLayout.ActionScrollDown);
        AddNoticeButton(LoginLayout.ScrollThumb, LoginLayout.ActionScrollThumb);

        // 22 body labels at panel-local (50, 100+18·k, 383, 50). spec §2.1 "Notice labels ×22".
        for (var id = LoginLayout.MsgLabelFirst; id <= LoginLayout.MsgLabelLast; id++)
        {
            var caption = _text.GetCaption((int)id);
            if (caption.Length == 0) continue;
            var k = (int)(id - LoginLayout.MsgLabelFirst);
            var label = new Label
            {
                Name = $"NoticeLabel{k}",
                Text = caption,
                Position = new Vector2(LoginLayout.NoticeLabelLocalX,
                    LoginLayout.NoticeLabelStartY + k * LoginLayout.NoticeLabelStrideY),
                Size = new Vector2(LoginLayout.NoticeLabelW, LoginLayout.NoticeLabelH),
                AutowrapMode = TextServer.AutowrapMode.Off,
                MouseFilter = MouseFilterEnum.Ignore
            };
            label.AddThemeColorOverride("font_color", Colors.White);
            _noticePanel.AddChild(label);
        }

        AddChild(_noticePanel);
    }

    private void AddNoticeButton(WidgetRect rect, int actionId)
    {
        var button = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginWindow,
            rect.X, rect.Y, rect.W, rect.H,
            rect.SrcX, rect.SrcY, rect.SrcX, rect.SrcY,
            actionId, fontSlot: 0);
        button.ActionFired += OnAction;
        var control = button.GetControl();
        if (control is not null) _noticePanel?.AddChild(control);
    }

    // -------------------------------------------------------------------------
    // Server-list root (idx 202 equivalent: loginwindow.dds 270,85,483,490)
    // This is a transparent structural container that gates the ServerSelectSubView rows.
    // The chrome art behind the server list is the STATIC _bannerFrame (loginwindow.dds src(0,490)
    // at abs (270,85) 483×490), which is always visible from state 2 — so no separate backdrop is
    // needed here. A prior version of this method added a login_slice1.dds full-screen backdrop at
    // (0,0,1024,398), which is the curtain-top art (idx 157) — that was WRONG and has been removed.
    // Gate: shown only at sub-state >= 35 (server-list arrives). The _bannerFrame chrome underneath
    // is always visible; this container just gates the interactive server-row children.
    // spec: Docs/RE/_dirty/login_layout/validation/loginwindow_visibility_schedule.md §DELIVERABLE 3
    //   "idx 202 = server-list COLUMN panel; gate state >= 35; loginwindow 270,85,483,490"
    //   (static-decompile-confirmed, IDB 263bd994)
    // -------------------------------------------------------------------------

    private void BuildServerListRoot()
    {
        _serverListRoot = new Control
        {
            Name = "ServerListRoot",
            Position = Vector2.Zero,
            Size = new Vector2(1024f, 398f),
            MouseFilter = MouseFilterEnum.Pass,
            Visible = false // init hidden; shown at state >= 35 via ApplyVisibility. spec §DELIVERABLE 3.
        };

        // NO backdrop child here. The visual chrome behind the server list is provided by _bannerFrame
        // (loginwindow.dds src(0,490) at abs (270,85) 483×490 — always visible from state 2).
        // Adding login_slice1.dds at (0,0,1024,398) here would paint the curtain-top art (idx 157)
        // as a full-screen opaque panel over the credential/server-list screens — that was the bug.
        // spec: Docs/RE/_dirty/login_layout/validation/loginwindow_visibility_schedule.md §DELIVERABLE 3
        //   "idx 157 (slice1 0,0,1024,398) = TOP CURTAIN HALF → gate state <= 3 (intro only); NOT the server-list"

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
            Visible = false // init hidden; shown only in 31/32. spec §2.2.
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
            Visible = false // hidden only at state 1; shown at state 2 via ApplyVisibility.
        };
        _formGroup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_formGroup);

        // Form panel starts at closed Y=326 and rides the bottom curtain to open Y=548. spec §2.3.
        var formPanel = new Control
        {
            Name = "FormPanel",
            Position = new Vector2(0f, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.BottomBarW, LoginLayout.BottomBarH),
            MouseFilter = MouseFilterEnum.Pass
        };
        _formGroup.AddChild(formPanel);
        _formPanel = formPanel;

        // Form decorative plate (member +0x27C): A1 src(0,469,494,113).
        // HIDDEN until curtain offset>200, then snaps to absolute canvas (265,548). spec §2.3.
        // G2 DEBUGGER-CONFIRMED 2026 / IDB 263bd994: real dst=(265,548); the prior "(494,469)" value
        // confused the 494 width and 469 src-top for a destination — CORRECTED.
        // Built as a child of formPanel so panel-local Y can be adjusted to yield canvas Y=548.
        // Initial state: HIDDEN. TickCurtain reveals it on the offset>200 edge (once).
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.3
        //   "Form decorative plate | A1 src(0,469,494,113) | hidden until offset>200; snaps to (265,548)"
        var decoPlate = HudWidgetFactory.MakeAtlasRect(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y, // dst(265,0) spec §2.3
            LoginLayout.ConfirmFacePlate.W, LoginLayout.ConfirmFacePlate.H, // 494×113 spec §2.3
            LoginLayout.ConfirmFacePlate.SrcX, LoginLayout.ConfirmFacePlate.SrcY); // src(0,469) spec §2.3
        if (decoPlate is not null)
        {
            decoPlate.Name = "FormDecoPlate";
            decoPlate.Visible = false; // hidden until offset>200. spec §2.3.
            // Initial panel-local position at dst(265,0) per the curtain table (before the snap).
            decoPlate.Position = new Vector2(LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y);
            formPanel.AddChild(decoPlate);
            _formDecoPlate = decoPlate;
        }

        // Quit-confirm button — action 102. A1 N(154,398) H(378,398). spec §2.1.
        // Rides with the form panel to its open resting canvas position. spec §2.3.
        var serverBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmButton.X, LoginLayout.ConfirmButton.Y,
            LoginLayout.ConfirmButton.W, LoginLayout.ConfirmButton.H,
            LoginLayout.ConfirmButton.SrcX, LoginLayout.ConfirmButton.SrcY,
            LoginLayout.ConfirmHoverSrcX, LoginLayout.ConfirmHoverSrcY,
            LoginLayout.ActionConfirm, fontSlot: 0);
        serverBtn.ActionFired += OnAction;
        var serverCtrl = serverBtn.GetControl();
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
        var quitBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitButton.X, LoginLayout.QuitButton.Y,
            LoginLayout.QuitButton.W, LoginLayout.QuitButton.H,
            LoginLayout.QuitButton.SrcX, LoginLayout.QuitButton.SrcY,
            LoginLayout.QuitHoverSrcX, LoginLayout.QuitHoverSrcY,
            105, fontSlot: 0);
        quitBtn.ActionFired += OnAction;
        var quitCtrl = quitBtn.GetControl();
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
            Visible = false // built hidden; ApplyVisibility is the SOLE authority.
        };
        _credentialGroup.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_credentialGroup);

        // Inner panel at the same canvas origin as formPanel (Y=326) so coordinates match the spec table.
        var credPanel = new Control
        {
            Name = "CredPanel",
            Position = new Vector2(0f, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.BottomBarW, LoginLayout.BottomBarH),
            MouseFilter = MouseFilterEnum.Pass
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

        // ID textbox — MaskedTextField: 1:1 atlas blit + slot-0 text; mask bit CLEAR.
        // dest (390,32,102,13); src A1 (615,404,102,13). action 109, maxlen 16.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.7 "ID textbox | unmasked"
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "ID field maxlen=16 (per-keystroke cap, GAP-4 RESOLVED)"
        // spec: Docs/RE/specs/frontend_layout_tables.md §0.10 "every front-end widget is a 1:1 atlas blit"
        _idBox = new MaskedTextField(
            _atlas,
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.AccountBox.W, LoginLayout.AccountBox.H, // field rect (102×13)
            LoginLayout.AccountBox.SrcX, LoginLayout.AccountBox.SrcY, // A1 src (615,404)
            false, // spec: frontend_layout_tables.md §2.7 "mask bit clear → clear text"
            LoginLayout
                .IdTextboxKeystrokeCap); // spec: frontend_layout_tables.md §2.7 "ID per-keystroke max length = 16 (GAP-4 RESOLVED)"
        _idBox.Name = "IdTextbox";
        _idBox.TextSubmitted += OnEnterKey;
        credPanel.AddChild(_idBox);

        // PW textbox — MaskedTextField: 1:1 atlas blit + slot-0 masked '*' at 6 px/char.
        // dest (568,32,102,13); src A1 (615,404,102,13). action 110, maxlen 12.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 / §2.7 "PW textbox | masked; '*' glyph, 6 px/char"
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.7 "PW per-keystroke max length = 12 (GAP-4 RESOLVED)"
        _pwBox = new MaskedTextField(
            _atlas,
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.PasswordBox.W, LoginLayout.PasswordBox.H, // field rect (102×13)
            LoginLayout.PasswordBox.SrcX, LoginLayout.PasswordBox.SrcY, // A1 src (615,404)
            true, // spec: frontend_layout_tables.md §2.7 "mask bit set → '*' glyph, 6 px/char"
            LoginLayout
                .PwTextboxKeystrokeCap); // spec: frontend_layout_tables.md §2.7 "PW per-keystroke max length = 12 (GAP-4 RESOLVED)"
        _pwBox.Name = "PwTextbox";
        _pwBox.TextSubmitted += OnEnterKey;
        credPanel.AddChild(_pwBox);

        // OK / Login button — action 103. A1 N(266,398) H(490,398). spec §2.1 "OK / Login button".
        var okBtn = HudWidgetFactory.MakeButton3(_atlas,
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.OkButton.X, LoginLayout.OkButton.Y,
            LoginLayout.OkButton.W, LoginLayout.OkButton.H,
            LoginLayout.OkButton.SrcX, LoginLayout.OkButton.SrcY,
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY,
            LoginLayout.ActionOk, fontSlot: 0);
        okBtn.ActionFired += OnAction;
        var okCtrl = okBtn.GetControl();
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

        var chkCtrl = _saveIdCheck?.GetControl();
        if (chkCtrl is not null)
        {
            chkCtrl.Name = "SaveIdCheckbox";
            credPanel.AddChild(chkCtrl);
        }

        // Restore saved account id and move focus to PW box if a saved id exists.
        // spec: Docs/RE/specs/frontend_layout_tables.md §2.5 "pre-fill the ID textbox and move focus to the PW box"
        if (_savedId.Length > 0 && _idBox is not null)
            _idBox.Text = _savedId;
    }

    // Helper: add an atlas TextureRect as a child of parent.
    private TextureRect? AddRect(Control parent, string atlas, int x, int y, int w, int h, int srcX, int srcY)
    {
        var r = HudWidgetFactory.MakeAtlasRect(_atlas, atlas, x, y, w, h, srcX, srcY);
        if (r is not null) parent.AddChild(r);
        return r;
    }
}