// Screens/LoginScreen.cs
//
// LOGIN screen — pixel-faithful rebuild against the §11.2 widget table in frontend_scenes.md.
//
// COMPOSITION MODEL (spec §11.0):
//   Design canvas: 1024×768, centered on screen.
//   Widget shape: (X, Y, W, H) canvas-local; (srcU, srcV) = top-left pixel in atlas DDS.
//   Atlas A = login_slice1.dds (DXT2 premultiplied); B = loginwindow.dds (DXT5).
//   C = InventWindow.dds (shared dialog); D = loginwindow_02.dds (DXT2).
//
// LAYERS (bottom → top, pixel-confirmed visual order 2026-06-14):
//   0. Dark canvas fill (near-black background).
//   1. A — bottom login-bar panel (0,326,1024,442) src(0,582)                          §11.2e
//          (Z-LOWEST so painting covers it Y=326..600; stone band visible Y=600..768)
//   2. A — upper bezel backdrop (0,0,1024,398) src(0,0) — carved frame+rings+flag+URL §11.2b
//          (visible at Y=0..110 above painting; painting covers it Y=110..398)
//   3. B — ink-wash painting (0,110,1024,490) src(0,0) — warrior+ribbon               §11.2a
//          (highest art; covers backdrop Y=110..398 and BottomBar Y=326..600)
//   4. B — server listbox + channel selector (hidden at boot)                          §11.2a/b
//   5. BottomBand container (transparent) — holds all form controls                    §11.2e
//      5a. A — confirm/gold button (456,166,112,39) src(154,398)                       §11.2e
//      5b. A — face plate overlay (265,0,494,113) src(0,469)                           §11.2e
//      5c. B — option/tab buttons 1+2 at (40,82) / (164,82)                           §11.2f
//      5d. LineEdit controls for ID/PW at (390,32) / (568,32) (height-expanded to 22)  §11.2e
//      5e. A — Save-ID checkbox (694,86,13,13)                                         §11.2e
//      5f. A — secondary bottom button (OK / Login) at (456,64,112,39) src(266,398)    §11.2e
//   6. C — quit-confirm modal (342,289,340,190) src(318,647)                           §11.2d
//
// ATLAS NOTES:
//   - DXT2 atlases (login_slice1.dds, loginwindow_02.dds) use premultiplied alpha.
//     Godot 4 imports DDS DXT2 as BC2 internally (same data as DXT3).
//     Premultiplied alpha can cause slight colour shifts on transparent edges.
//     The fallback when the VFS is unavailable is a solid-color backdrop.
//   - The BottomBar Y position is height-scaled: Y = 326*screenH/768.
//     On the fixed 1024×768 reference canvas this is simply Y=326.
//
// FLOW / SIGNALS (spec §1.4 / §1.5):
//   LoginAccepted  — OK pressed, local validation passed (ID≥4, PW≥1). Emits account name.
//   ServerListRequested — Server-list button (action 102) clicked. Emits account name.
//   QuitRequested  — Quit-confirm Yes button (actions 113/114). Emits quit.
//
// PASSIVE: pure view. Reads VFS atlas textures + msg.xdb captions via UiAssetLoader.
//          No game logic, no domain state, no packet parsing.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.0–§11.2 (CODE-CONFIRMED literals).
//       §1.2 (action ids), §1.4 (validation), §1.8 (quit), §1.9 (msg ids).

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Login screen Control on the 1024×768 reference canvas (spec §11.0).
/// Pixel-faithful to the §11.2 widget table: background art, stone chrome, ID/PW fields,
/// login/server/quit buttons, save-ID checkbox, quit-confirm modal.
/// </summary>
public sealed partial class LoginScreen : Control
{
    // -------------------------------------------------------------------------
    // Outgoing intents (signals consumed by BootFlow — no game logic here).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised when OK/Login is pressed and local validation passes.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.4 — validation then advance. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void LoginAcceptedEventHandler(string account);

    /// <summary>
    /// Raised when the server-list button (action 102) is clicked.
    /// spec: Docs/RE/specs/frontend_scenes.md §2 / §1.2 action 102. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void ServerListRequestedEventHandler(string account);

    /// <summary>
    /// Raised when the quit-confirm Yes button (action 113 or 114) is clicked.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.8. CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void QuitRequestedEventHandler();

    // -------------------------------------------------------------------------
    // View state
    // -------------------------------------------------------------------------

    private LineEdit _accountEdit = null!;
    private LineEdit _passwordEdit = null!;
    private Label _toast = null!;
    private Control _quitModal = null!;

    // Save-ID checkbox view state (item 2 — §1.6). Not domain state — purely presentational.
    private bool _saveIdChecked;

    // Dialog fade state (item 6 — §11.2g): current alpha [0..255] and target alpha.
    // Alpha ramp ±64 per frame until target is reached. spec §11.2g. CODE-CONFIRMED.
    private int _quitModalAlpha; // current modulated alpha [0..255]
    private int _quitModalTarget; // 255 = showing, 0 = hiding

    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    /// <summary>Optionally inject a shared asset loader (disposed externally when set).</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        // Load saved id BEFORE building UI so BuildUi can pre-fill and set default focus.
        // spec: Docs/RE/specs/frontend_scenes.md §1.6 — DoOption.ini [DO_OPTION] OPTION_ID. CODE-CONFIRMED.
        string savedId = LoadSavedId();

        try
        {
            BuildUi(savedId);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScreen] Build failed: {ex.Message}");
        }

        // Default focus: ID box when no saved id, PW box when id is pre-filled.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2e "Default input focus". CODE-CONFIRMED.
        if (_accountEdit is not null && _passwordEdit is not null)
        {
            if (string.IsNullOrEmpty(savedId))
                _accountEdit.CallDeferred(Control.MethodName.GrabFocus);
            else
                _passwordEdit.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    /// <summary>
    /// Keyboard shortcuts for the login form.
    /// Enter = login (same as OK button press). Tab = swap focus ID ↔ PW.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.1 / §1.2 "id 9 = swap focus; id 10 = Enter = Login".
    /// CODE-CONFIRMED.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed) return;

        // Enter key → same as OK/Login button. spec §1.2 "id 10 = Enter on form page". CODE-CONFIRMED.
        if (key.PhysicalKeycode == Key.Enter || key.PhysicalKeycode == Key.KpEnter)
        {
            GetViewport().SetInputAsHandled();
            OnOkPressed();
            return;
        }

        // Tab → swap focus ID ↔ PW (mutually exclusive). spec §1.1 "id 9 = swap focused textbox". CODE-CONFIRMED.
        if (key.PhysicalKeycode == Key.Tab)
        {
            GetViewport().SetInputAsHandled();
            if (_accountEdit.HasFocus())
                _passwordEdit.GrabFocus();
            else
                _accountEdit.GrabFocus();
        }
    }

    /// <summary>
    /// Per-frame: advance dialog alpha ramps.
    /// spec: Docs/RE/specs/frontend_scenes.md §11.2g — ±64 alpha per frame toward 255/0. CODE-CONFIRMED.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_quitModal is null) return;

        if (_quitModalAlpha != _quitModalTarget)
        {
            // Ramp ±64 per frame, clamped [0,255]. spec §11.2g. CODE-CONFIRMED.
            int step = LoginLayout.DialogFadeStep;
            if (_quitModalTarget > _quitModalAlpha)
                _quitModalAlpha = Math.Min(_quitModalAlpha + step, LoginLayout.DialogAlphaVisible);
            else
                _quitModalAlpha = Math.Max(_quitModalAlpha - step, LoginLayout.DialogAlphaHidden);

            float a = _quitModalAlpha / 255f;
            _quitModal.Modulate = new Color(1f, 1f, 1f, a);

            // Make visible as soon as ramp starts showing; hide once fully transparent.
            if (_quitModalAlpha > 0 && !_quitModal.Visible)
                _quitModal.Visible = true;
            else if (_quitModalAlpha == 0 && _quitModal.Visible)
                _quitModal.Visible = false;
        }
    }

    // -------------------------------------------------------------------------
    // UI construction — §11.2 layer-by-layer.
    // -------------------------------------------------------------------------

    private void BuildUi(string savedId = "")
    {
        // Fill the reference 1024×768 canvas. ScreenHost scales us to the window.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        int widgetCount = 0;

        // =======================================================================
        // CONFIRMED VISUAL LAYER ORDER (pixel-confirmed 2026-06-14 from atlas analysis):
        //
        // login_slice1.dds (atlas A) src(0,0..398):
        //   Entirely OPAQUE dark-stone backdrop (alpha=255 throughout Y=0..330, fading
        //   transparent only at Y=330..398). Provides the dark frame background behind the
        //   painting, plus the visible top ornaments (rings, red flag, URL) in canvas Y=0..110
        //   where no painting sits.
        //
        // loginwindow.dds (atlas B): fully OPAQUE painting (alpha=255, Y=0..490).
        //   PLACED ABOVE the A backdrop: the painting covers the dark stone from canvas Y=110..600.
        //   Above Y=110, only login_slice1.dds is visible (rings, flag, URL).
        //
        // login_slice1.dds (atlas A) BottomBar src(0,582..1024):
        //   The login form band, transparent at the central opening (src Y=582..606 transparent,
        //   then partially transparent to ~Y=800), opaque stone at the very bottom (Y=800..1024)
        //   which maps to canvas Y≈630..768. This stone band reveals the login form widgets.
        //
        // Layer order (bottom → top):
        //   1. Dark bg fill (ColorRect, always-visible backdrop).
        //   2. BottomBar (login_slice1.dds src 0,582, canvas Y=326, H=442) — Z=LOWEST backdrop.
        //   3. BackgroundPanel (login_slice1.dds src 0,0, canvas Y=0, H=398) — Z=MIDDLE.
        //   4. MainPanelChrome (loginwindow.dds painting, canvas Y=110, H=490) — Z=HIGHEST art.
        //   5. Form widgets (buttons, textboxes, etc.) — added last, always topmost.
        //
        // Result: painting visible from canvas Y=110..600 (covers dark stone). Dark stone
        //         visible Y=0..110 (frame top with rings/flag). Form band visible Y=600..768
        //         (painting ends there, stone bottom band shows). Form widgets float above all.
        //
        // spec: Docs/RE/specs/frontend_scenes.md §11.2a–b, §11.2e (layer order, CODE-CONFIRMED).
        //       Atlas alpha analysis 2026-06-14: loginwindow.dds alpha=255 Y=0..490;
        //       login_slice1.dds src(0..398) alpha=255 Y=0..330 (opaque dark stone backdrop).
        // =======================================================================

        // =======================================================================
        // BASE BACKGROUND: solid dark fill covering the entire canvas.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2b.
        // =======================================================================
        {
            var bgFill = new ColorRect
            {
                Name = "CanvasBgFill",
                Color = new Color(0.04f, 0.04f, 0.08f, 1f),
            };
            bgFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(bgFill);
            widgetCount++;
        }

        // =======================================================================
        // [Z=1] LOWER BACKDROP — bottom carved-metal panel. ADDED FIRST so it is Z-LOWEST
        // art. The painting (Z=3, added later) will cover it from Y=110..600. It remains
        // visible only at Y=600..768 where the painting alpha fades, revealing the stone
        // login-form band.
        // A@(0, 326, 1024, 442) src(0,582) — login_slice1.dds — the brown/stone bottom band.
        // Y = 326 on the 1024×768 reference canvas (= 326×768/768).
        // spec §11.2e "Bottom login-bar panel". CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? bottomBar = _assets.Slice(
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.BottomBarSrcX, LoginLayout.BottomBarSrcY,
            LoginLayout.BottomBarW, LoginLayout.BottomBarH);

        if (bottomBar is not null)
        {
            var bottomBarRect = MakeSprite("BottomBar", bottomBar,
                0, LoginLayout.BottomBarCanvasY,
                LoginLayout.BottomBarW, LoginLayout.BottomBarH);
            AddChild(bottomBarRect);
            widgetCount++;
        }
        else
        {
            // Offline fallback: solid parchment-tone band.
            var fallbackBand = new ColorRect
            {
                Name = "BottomBarFallback",
                Color = new Color(0.22f, 0.14f, 0.08f, 0.95f),
                Position = new Vector2(0, LoginLayout.BottomBarCanvasY),
                Size = new Vector2(LoginLayout.RefWidth, LoginLayout.BottomBarH),
            };
            AddChild(fallbackBand);
        }

        // =======================================================================
        // [Z=2] UPPER BACKDROP — login_slice1.dds (A), MIDDLE LAYER.
        // A@(0,0,1024,398) src(0,0) — dark-stone carved backdrop:
        //   Opaque stone texture behind the painting. Visible at canvas Y=0..110 (top frame
        //   area with rings, red flag, URL). Hidden by painting from Y=110..398. The frame
        //   ornaments (rings/flag) are baked art at Y=0..110 of the atlas.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2b "Full background art panel". CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? bgSlice = _assets.Slice(
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.BackgroundPanel.SrcX, LoginLayout.BackgroundPanel.SrcY,
            LoginLayout.BackgroundPanel.W, LoginLayout.BackgroundPanel.H);

        if (bgSlice is not null)
        {
            var bgRect = MakeSprite("BgArtPanel", bgSlice,
                LoginLayout.BackgroundPanel.X, LoginLayout.BackgroundPanel.Y,
                LoginLayout.BackgroundPanel.W, LoginLayout.BackgroundPanel.H);
            AddChild(bgRect);
            widgetCount++;
        }

        // =======================================================================
        // [Z=3] CENTRAL PANEL PAINTING — loginwindow.dds (B), HIGHEST ART LAYER.
        // B@(0,110,1024,490) src(0,0) — the ink-wash painting: warrior + landscape.
        // Fully opaque (alpha=255 Y=0..490). Placed above the dark stone backdrop so the
        // painting covers it from canvas Y=110..600. Only the top frame ornaments (canvas
        // Y=0..110) remain visible from the backdrop below.
        // Also covers the BottomBar (Z=1) from canvas Y=326..600. The painting and BottomBar
        // share the zone Y=326..600; painting wins (higher Z). At Y=600..768, painting ends
        // (alpha goes to 0) and BottomBar stone form-band becomes visible.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2a "Main panel art". CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? mainPanel = _assets.Slice(
            LoginLayout.AtlasLoginWindow,
            LoginLayout.MainPanel.SrcX, LoginLayout.MainPanel.SrcY,
            LoginLayout.MainPanel.W, LoginLayout.MainPanel.H);

        if (mainPanel is not null)
        {
            var mainPanelRect = MakeSprite("MainPanelChrome", mainPanel,
                LoginLayout.MainPanel.X, LoginLayout.MainPanel.Y,
                LoginLayout.MainPanel.W, LoginLayout.MainPanel.H);
            AddChild(mainPanelRect);
            widgetCount++;
        }
        else
        {
            GD.PrintErr("[LoginScreen] loginwindow.dds Slice returned NULL — painting missing!");
        }

        // =======================================================================
        // [Z=4..N] Widgets: server listbox + channel-selector blocks (hidden at boot).
        // Hidden at boot — only shown when the server-list sub-state is active.
        // spec: Docs/RE/specs/frontend_scenes.md §1.5. CODE-CONFIRMED.
        // =======================================================================

        // Server listbox container (widget #1). Hidden at boot.
        {
            AtlasTexture? listbox = _assets.Slice(
                LoginLayout.AtlasLoginWindow,
                LoginLayout.ServerListbox.SrcX, LoginLayout.ServerListbox.SrcY,
                LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H);
            if (listbox is not null)
            {
                var listboxRect = MakeSprite("ServerListboxBg", listbox,
                    LoginLayout.ServerListbox.X, LoginLayout.ServerListbox.Y,
                    LoginLayout.ServerListbox.W, LoginLayout.ServerListbox.H);
                listboxRect.Visible = false; // hidden at boot. spec §1.5.
                AddChild(listboxRect);
                widgetCount++;
            }
        }

        // Channel-selector blocks (widgets #33..#37). Hidden at boot.
        // D@two-block loop — loginwindow_02.dds.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2b "Channel block: body". CODE-CONFIRMED.
        for (int blk = 0; blk < 2; blk++)
        {
            int blockX = 30 + blk * 233;
            int bodySrcV = 448 + blk * 124;

            AtlasTexture? blockBody = _assets.Slice(
                LoginLayout.AtlasLoginWindow02,
                bodySrcV, 6, 100, 372);
            if (blockBody is not null)
            {
                var bodyRect = MakeSprite($"ChannelBlock{blk}Body", blockBody,
                    blockX + 47, 97, 100, 372);
                bodyRect.Visible = false; // hidden until server-list sub-state. spec §1.5.
                AddChild(bodyRect);
                widgetCount++;
            }
        }

        // =======================================================================
        // Form-widget band: a transparent container at canvas Y=326 that holds all
        // interactive form controls (textboxes, buttons). Added AFTER the painting so
        // these widgets are ALWAYS topmost (Z=N). The BottomBar art beneath provides
        // the stone frame look; these widgets float above the painting + BottomBar.
        // =======================================================================
        Control bottomBand = new Control
        {
            Name = "BottomBand",
            Position = new Vector2(0, LoginLayout.BottomBarCanvasY),
            Size = new Vector2(LoginLayout.RefWidth, LoginLayout.BottomBarH),
        };
        AddChild(bottomBand);

        // --- Baked label art plates (§11.2e) — "아이디" / "비밀번호" / decoration. ---
        // These are DDS sub-rects that contain Korean text baked into the atlas art.
        // No runtime text — just sprites. spec §11.2e "baked art". CODE-CONFIRMED.
        AddAtlasSprite(bottomBand, "AccountLabelArt",
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.AccountLabelArt.SrcX, LoginLayout.AccountLabelArt.SrcY,
            LoginLayout.AccountLabelArt.X, LoginLayout.AccountLabelArt.Y,
            LoginLayout.AccountLabelArt.W, LoginLayout.AccountLabelArt.H);
        widgetCount++;

        AddAtlasSprite(bottomBand, "PasswordLabelArt",
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.PasswordLabelArt.SrcX, LoginLayout.PasswordLabelArt.SrcY,
            LoginLayout.PasswordLabelArt.X, LoginLayout.PasswordLabelArt.Y,
            LoginLayout.PasswordLabelArt.W, LoginLayout.PasswordLabelArt.H);
        widgetCount++;

        AddAtlasSprite(bottomBand, "DecoPlate",
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.SmallDecorPlate.SrcX, LoginLayout.SmallDecorPlate.SrcY,
            LoginLayout.SmallDecorPlate.X, LoginLayout.SmallDecorPlate.Y,
            LoginLayout.SmallDecorPlate.W, LoginLayout.SmallDecorPlate.H);
        widgetCount++;

        // =======================================================================
        // [L6] Confirm/gold button (§11.2e) — action 102.
        // A@(456,166,112,39) NORMAL src(154,398), HOVER/PRESSED src(378,398).
        // This is the "확인" (confirm) button — its baked art label is inside the atlas.
        // The action id is 102 (server-list button per §1.2).
        // In our offline flow we wire it as the "Server List" / "확인" submission.
        // spec §11.2e "Confirm button" action 102. CODE-CONFIRMED.
        // =======================================================================
        var confirmBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmButton.X, LoginLayout.ConfirmButton.Y,
            LoginLayout.ConfirmButton.W, LoginLayout.ConfirmButton.H,
            LoginLayout.ConfirmButton.SrcX, LoginLayout.ConfirmButton.SrcY, // NORMAL (154,398)
            LoginLayout.ConfirmHoverSrcX, LoginLayout.ConfirmHoverSrcY, // HOVER  (378,398)
            LoginLayout.ConfirmHoverSrcX, LoginLayout.ConfirmHoverSrcY, // PRESSED= HOVER
            LoginLayout.ActionConfirm,
            caption: "", // No overlay text — word is baked into atlas art. spec §11.2e.
            captionTint: Colors.White);
        confirmBtn.Name = "ConfirmButton";
        confirmBtn.ActionFired += _ => OnServerListPressed();
        bottomBand.AddChild(confirmBtn);
        widgetCount++;

        // Confirm button face plate (gold plate baked art overlay).
        // spec §11.2e "Confirm-button face plate". CODE-CONFIRMED.
        AddAtlasSprite(bottomBand, "ConfirmFacePlate",
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.ConfirmFacePlate.SrcX, LoginLayout.ConfirmFacePlate.SrcY,
            LoginLayout.ConfirmFacePlate.X, LoginLayout.ConfirmFacePlate.Y,
            LoginLayout.ConfirmFacePlate.W, LoginLayout.ConfirmFacePlate.H);
        widgetCount++;

        // =======================================================================
        // [L7] Secondary bottom button (OK / Login) — action 103.
        // A@(456,64,112,39) NORMAL src(266,398), HOVER/PRESSED src(490,398).
        // spec §11.2e "Secondary bottom button". CODE-CONFIRMED.
        // In the original this is the register / find-password button; we use it as the
        // Login button (send credentials → advance to server select or login).
        // =======================================================================
        var loginBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.OkButton.X, LoginLayout.OkButton.Y,
            LoginLayout.OkButton.W, LoginLayout.OkButton.H,
            LoginLayout.OkButton.SrcX, LoginLayout.OkButton.SrcY, // NORMAL (266,398)
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY, // HOVER  (490,398)
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY, // PRESSED = HOVER
            LoginLayout.ActionOk,
            caption: "", // baked art label. spec §11.2e "baked art".
            captionTint: Colors.White);
        loginBtn.Name = "LoginButton";
        loginBtn.ActionFired += _ => OnOkPressed();
        bottomBand.AddChild(loginBtn);
        widgetCount++;

        // =======================================================================
        // [L8] Option / tab buttons (§11.2f).
        // B@(40,82,110,38) action 111 — "Option 1" tab.
        // B@(164,82,110,38) action 112 — "Option 2" tab.
        // spec §11.2f. CODE-CONFIRMED.
        // =======================================================================
        var tab1 = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab1.X, LoginLayout.OptionTab1.Y,
            LoginLayout.OptionTab1.W, LoginLayout.OptionTab1.H,
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY, // N (520,492)
            LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY, // H (635,492)
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY, // P = N
            LoginLayout.ActionOptionTab1,
            caption: "", captionTint: Colors.White);
        tab1.Name = "OptionTab1";
        bottomBand.AddChild(tab1);
        widgetCount++;

        var tab2 = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab2.X, LoginLayout.OptionTab2.Y,
            LoginLayout.OptionTab2.W, LoginLayout.OptionTab2.H,
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY, // N (750,492)
            LoginLayout.OptionTab2HoverSrcX, LoginLayout.OptionTab2HoverSrcY, // H (865,492)
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY, // P = N
            LoginLayout.ActionOptionTab2,
            caption: "", captionTint: Colors.White);
        tab2.Name = "OptionTab2";
        bottomBand.AddChild(tab2);
        widgetCount++;

        // Decoration image plates (§11.2f baked art).
        AddAtlasSprite(bottomBand, "DecoPlate1",
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate1.SrcX, LoginLayout.DecoPlate1.SrcY,
            LoginLayout.DecoPlate1.X, LoginLayout.DecoPlate1.Y,
            LoginLayout.DecoPlate1.W, LoginLayout.DecoPlate1.H);

        AddAtlasSprite(bottomBand, "DecoPlate2",
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.DecoPlate2.SrcX, LoginLayout.DecoPlate2.SrcY,
            LoginLayout.DecoPlate2.X, LoginLayout.DecoPlate2.Y,
            LoginLayout.DecoPlate2.W, LoginLayout.DecoPlate2.H);
        widgetCount += 2;

        // =======================================================================
        // [L9] ID and PW text-entry fields (§11.2e).
        // ID field:  dest(390,32,102,13); frame src = login_slice1.dds (A) at (615,404). max 16, action 109.
        // PW field:  dest(568,32,102,13); frame src = login_slice1.dds (A) at (615,404). max 12, masked, action 110.
        //
        // ATLAS CORRECTION (§11.2e IDA pass 2026-06-14):
        //   Both edit-field frames sample the SAME source rect in atlas A (login_slice1.dds)
        //   at src(615,404,102,13). The (390,32)/(568,32) numbers are DEST canvas positions.
        //   AtlasLoginWindow (B/loginwindow.dds) is NOT the source for these frames.
        //   spec: Docs/RE/specs/frontend_scenes.md §11.2e "Atlas note for the edit fields". CODE-CONFIRMED.
        //
        // NOTE: The spec DDS frame height is 13px — too small for a usable Godot LineEdit.
        // We keep the spec X/Y position and width (102) exact; height is expanded to 22px.
        // The legacy client drew text ON TOP of the atlas frame at the font height, not
        // clipped inside a 13px box. Our LineEdit approach is the closest Godot equivalent.
        // =======================================================================

        // Draw the edit-field frame sprites (from atlas A / login_slice1.dds). §11.2e atlas correction.
        // Both frames share src(615,404,102,13) in login_slice1.dds. spec §11.2e. CODE-CONFIRMED.
        AddAtlasSprite(bottomBand, "AccountFrameArt",
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.EditFieldFrameSrcX, LoginLayout.EditFieldFrameSrcY,
            LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y,
            LoginLayout.EditFieldFrameW, LoginLayout.EditFieldFrameH);

        AddAtlasSprite(bottomBand, "PasswordFrameArt",
            LoginLayout.EditFieldFrameAtlas,
            LoginLayout.EditFieldFrameSrcX, LoginLayout.EditFieldFrameSrcY,
            LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y,
            LoginLayout.EditFieldFrameW, LoginLayout.EditFieldFrameH);

        _accountEdit = MakeTextbox(masked: false, maxLen: LoginLayout.IdMaxLength);
        _accountEdit.Name = "AccountEdit";
        _accountEdit.Position = new Vector2(LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y);
        _accountEdit.Size = new Vector2(LoginLayout.AccountBox.W, LoginLayout.TextboxRenderH);
        // Pre-fill if a saved id is available. spec §1.6 / §11.2e "Default input focus". CODE-CONFIRMED.
        if (!string.IsNullOrEmpty(savedId))
            _accountEdit.Text = savedId;
        bottomBand.AddChild(_accountEdit);
        widgetCount++;

        _passwordEdit = MakeTextbox(masked: true, maxLen: LoginLayout.PwMaxLength);
        _passwordEdit.Name = "PasswordEdit";
        _passwordEdit.Position = new Vector2(LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y);
        _passwordEdit.Size = new Vector2(LoginLayout.PasswordBox.W, LoginLayout.TextboxRenderH);
        // Password mask character — ASCII '*', not the default Godot bullet. spec §11.2e. CODE-CONFIRMED.
        _passwordEdit.SecretCharacter = "*";
        bottomBand.AddChild(_passwordEdit);
        widgetCount++;

        // =======================================================================
        // [L10] Save-ID checkbox (§11.2e).
        // A@(694,86,13,13) NORMAL src(717,398), PRESSED/checked src(730,398). action 104.
        // spec §11.2e. CODE-CONFIRMED.
        // =======================================================================
        var saveIdBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.SaveIdCheck.X, LoginLayout.SaveIdCheck.Y,
            LoginLayout.SaveIdCheck.W, LoginLayout.SaveIdCheck.H,
            LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY, // NORMAL  (717,398) unchecked
            LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY, // HOVER   = NORMAL
            LoginLayout.SaveIdCheckedSrcX, LoginLayout.SaveIdCheckedSrcY, // PRESSED (730,398) checked
            LoginLayout.ActionSaveId,
            caption: "", captionTint: Colors.White);
        saveIdBtn.Name = "SaveIdCheckbox";
        // Initialise checkbox visual state from persistent save (pre-check if we have a saved id).
        // spec §1.6. CODE-CONFIRMED.
        _saveIdChecked = !string.IsNullOrEmpty(savedId);
        saveIdBtn.ActionFired += _ => OnSaveIdToggled(); // wire toggle. spec §1.6.
        bottomBand.AddChild(saveIdBtn);
        widgetCount++;

        // =======================================================================
        // [L11] Toast / validation error line (hidden until error). Not in original layout but
        // needed to surface msg 4025/4026. Placed near the top of the bottom band.
        // spec §1.4 / §1.9 — msg 4025 (ID short) / 4026 (PW empty). CODE-CONFIRMED ids.
        // =======================================================================
        _toast = WidgetFactory.MakeLabel("", LoginLayout.FontBodyHeight, new Color(1f, 0.40f, 0.40f));
        _toast.Name = "ToastLabel";
        _toast.Position = new Vector2(280, 115);
        _toast.Size = new Vector2(480, 20);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        bottomBand.AddChild(_toast);
        widgetCount++;

        // =======================================================================
        // [L13] 종료 (Quit) button — C1 fix.
        // The official client has a 종료 button in the stone bottom bar that opens the
        // quit-confirm modal. spec §1.8 "quit paths" / §1.2 action ids 113/114. CODE-CONFIRMED.
        // We use the secondary-bottom-button atlas art (same stone style) and place it at the
        // far right of the bottom band. The button text is baked art; we show a text label as
        // a fallback since we do not have a standalone 종료-only sub-rect catalogued.
        // spec: Docs/RE/specs/frontend_scenes.md §1.8 / §1.2. CODE-CONFIRMED behaviour.
        // =======================================================================
        var quitBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitButton.X, LoginLayout.QuitButton.Y,
            LoginLayout.QuitButton.W, LoginLayout.QuitButton.H,
            LoginLayout.QuitButton.SrcX, LoginLayout.QuitButton.SrcY, // NORMAL (266,398) — same stone art
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY, // HOVER  (490,398)
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY, // PRESSED = HOVER
            LoginLayout.ActionQuitBtn,
            caption: _assets.Text(LoginLayout.MsgQuitConfirm1, "종료"),
            captionTint: new Color(0.95f, 0.85f, 0.55f));
        quitBtn.Name = "QuitButton";
        quitBtn.ActionFired += _ => ShowQuitConfirmModal();
        bottomBand.AddChild(quitBtn);
        widgetCount++;

        // =======================================================================
        // [L12] Quit-confirm modal (§11.2d) — initially hidden.
        // C@(342,289,340,190) src(318,647) — InventWindow.dds chrome.
        // spec §11.2d. CODE-CONFIRMED.
        // =======================================================================
        _quitModal = BuildQuitConfirmModal();
        _quitModal.Visible = false; // starts hidden; shown via alpha ramp in _Process. spec §11.2g.
        _quitModal.Modulate = new Color(1f, 1f, 1f, 0f); // start fully transparent
        _quitModalAlpha = LoginLayout.DialogAlphaHidden;
        _quitModalTarget = LoginLayout.DialogAlphaHidden;
        AddChild(_quitModal); // added to root so it overlays everything
        widgetCount++;

        GD.Print($"[LoginScreen] Built — {widgetCount} widgets; " +
                 $"vfs={(_assets.HasVfs ? "real-atlas" : "offline-fallback")}; " +
                 $"captions={(_assets.HasVfs ? "msg.xdb" : "en-fallback")}.");
    }

    // -------------------------------------------------------------------------
    // Quit-confirm modal (§11.2d).
    // Chrome: C@(342,289,340,190) src(318,647) — InventWindow.dds.
    // -------------------------------------------------------------------------

    private Control BuildQuitConfirmModal()
    {
        // Modal is positioned canvas-absolute (not band-relative).
        var modal = new Control
        {
            Name = "QuitConfirmModal",
            Position = new Vector2(LoginLayout.ModalChromeX, LoginLayout.ModalChromeY),
            Size = new Vector2(LoginLayout.ModalChromeW, LoginLayout.ModalChromeH),
        };

        // Chrome background — InventWindow.dds src(318,647) 340×190.
        // spec §11.2d "Dialog #1 panel (notice)" src(318,647) 340×190. CODE-CONFIRMED.
        AtlasTexture? chrome = _assets.Slice(
            LoginLayout.AtlasInventWindow,
            LoginLayout.ModalChromeSrcX, LoginLayout.ModalChromeSrcY,
            LoginLayout.ModalChromeW, LoginLayout.ModalChromeH);

        if (chrome is not null)
        {
            var chromeBg = new TextureRect
            {
                Name = "ModalChrome",
                Texture = chrome,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            chromeBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            modal.AddChild(chromeBg);
        }
        else
        {
            var fallbackBg = new ColorRect
            {
                Name = "ModalFallbackBg",
                Color = new Color(0.07f, 0.07f, 0.12f, 0.97f),
            };
            fallbackBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            modal.AddChild(fallbackBg);
        }

        // Prompt label — msg 4023. spec §11.2d "Dialog #1 body text" @ (10,100) 330×20. CODE-CONFIRMED.
        var prompt1 = WidgetFactory.MakeLabel(
            _assets.Text(LoginLayout.MsgQuitConfirm1, "Are you sure you want to quit?"),
            LoginLayout.FontBodyHeight, new Color(0.9f, 0.9f, 0.9f));
        prompt1.Position = new Vector2(10, 80);
        prompt1.Size = new Vector2(320, 20);
        prompt1.HorizontalAlignment = HorizontalAlignment.Center;
        modal.AddChild(prompt1);

        // Second prompt line — msg 4024. spec §11.2d. CODE-CONFIRMED.
        var prompt2 = WidgetFactory.MakeLabel(
            _assets.Text(LoginLayout.MsgQuitConfirm2, "Unsaved progress will be lost."),
            LoginLayout.FontBodyHeight, new Color(0.75f, 0.75f, 0.75f));
        prompt2.Position = new Vector2(10, 100);
        prompt2.Size = new Vector2(320, 20);
        prompt2.HorizontalAlignment = HorizontalAlignment.Center;
        modal.AddChild(prompt2);

        // Yes button #1 — C@(120,136,113,40) NORMAL src(302,900), HOVER src(415,900), action 113.
        // spec §11.2d "Dialog #1 OK". CODE-CONFIRMED.
        var yes1 = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasInventWindow,
            LoginLayout.QuitConfirmYes1.X, LoginLayout.QuitConfirmYes1.Y,
            LoginLayout.QuitConfirmYes1.W, LoginLayout.QuitConfirmYes1.H,
            LoginLayout.QuitConfirmYes1.SrcX, LoginLayout.QuitConfirmYes1.SrcY,
            LoginLayout.QuitConfirmYes1HoverSrcX, LoginLayout.QuitConfirmYes1HoverSrcY,
            LoginLayout.QuitConfirmYes1.SrcX, LoginLayout.QuitConfirmYes1.SrcY,
            LoginLayout.ActionQuitConfirmYes1,
            caption: _assets.Text(4008u, "Yes"), captionTint: Colors.White);
        yes1.Name = "QuitConfirmYes1";
        yes1.ActionFired += _ => OnQuitConfirmed();
        modal.AddChild(yes1);

        // Yes button #2 — C@(120,136,113,40) NORMAL src(302,860), HOVER src(415,860), action 114.
        // spec §11.2d "Dialog #2 OK". CODE-CONFIRMED.
        // Slightly offset (+44) to avoid overlap with #1 in this offline build.
        var yes2 = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasInventWindow,
            LoginLayout.QuitConfirmYes2.X, LoginLayout.QuitConfirmYes2.Y + 44,
            LoginLayout.QuitConfirmYes2.W, LoginLayout.QuitConfirmYes2.H,
            LoginLayout.QuitConfirmYes2.SrcX, LoginLayout.QuitConfirmYes2.SrcY,
            LoginLayout.QuitConfirmYes2HoverSrcX, LoginLayout.QuitConfirmYes2HoverSrcY,
            LoginLayout.QuitConfirmYes2.SrcX, LoginLayout.QuitConfirmYes2.SrcY,
            LoginLayout.ActionQuitConfirmYes2,
            caption: _assets.Text(4009u, "OK"), captionTint: Colors.White);
        yes2.Name = "QuitConfirmYes2";
        yes2.ActionFired += _ => OnQuitConfirmed();
        modal.AddChild(yes2);

        // No / Cancel — not in the spec as a widget (quit-confirm has only Yes actions 113/114).
        // Added for usability in offline build. Placed in the right quarter.
        var noBtn = new Button
        {
            Text = _assets.Text(4010u, "No"),
            Position = new Vector2(LoginLayout.ModalChromeW - 140, LoginLayout.QuitConfirmYes1.Y),
            Size = new Vector2(100, 36),
        };
        noBtn.Pressed += HideQuitConfirmModal;
        modal.AddChild(noBtn);

        return modal;
    }

    // -------------------------------------------------------------------------
    // Intent handlers
    // -------------------------------------------------------------------------

    private void OnOkPressed()
    {
        // Local credential validation sequence. spec §1.4. CODE-CONFIRMED.
        _toast.Text = "";

        // ===================================================================
        // STEP 1 — game.ver version gate (runs FIRST, before ID/PW checks).
        // spec: Docs/RE/specs/frontend_scenes.md §1.4 "Version gate (local, runs first)".
        // CODE-CONFIRMED: mismatch → msg 2204 → abort login.
        //
        // In the offline/dev flow, the VFS comparison is stubbed as always-equal so the
        // gate passes. The abort path is wired for when VFS is mounted and versions differ.
        // ===================================================================
        if (!CheckGameVersion())
        {
            // Version mismatch — show msg 2204 and abort. spec §1.4 / §1.8. CODE-CONFIRMED.
            _toast.Text = _assets.Text(LoginLayout.MsgVersionMismatch,
                "Client version mismatch. Please update your client.");
            GD.Print("[LoginScreen] Version gate: game.ver mismatch (msg 2204). Aborting login.");
            return;
        }

        string account = _accountEdit.Text.Trim();

        // ===================================================================
        // STEP 2 — Persist Save-ID if checkbox is checked (before validation).
        // spec §1.4 step 2 "Persist Save-ID if the checkbox is set (§1.6)". CODE-CONFIRMED.
        // ===================================================================
        if (_saveIdChecked && !string.IsNullOrEmpty(account))
            PersistSavedId(account);

        // spec §1.4: "ID length < 4 → msg 4025 → return to sub-state 6". CODE-CONFIRMED.
        if (account.Length < LoginLayout.MinIdLength)
        {
            _toast.Text = _assets.Text(LoginLayout.MsgErrShortId,
                $"Account must be at least {LoginLayout.MinIdLength} characters.");
            GD.Print(
                $"[LoginScreen] Validation: ID too short ({account.Length} < {LoginLayout.MinIdLength}), msg 4025.");
            return;
        }

        // spec §1.4: "password length < 1 → msg 4026 → return to sub-state 6". CODE-CONFIRMED.
        if (_passwordEdit.Text.Length < LoginLayout.MinPwLength)
        {
            _toast.Text = _assets.Text(LoginLayout.MsgErrEmptyPassword, "Please enter a password.");
            GD.Print("[LoginScreen] Validation: password empty, msg 4026.");
            return;
        }

        GD.Print($"[LoginScreen] Login OK (account='{account}'). Emitting LoginAccepted.");
        EmitSignal(SignalName.LoginAccepted, account);
    }

    // -------------------------------------------------------------------------
    // game.ver version gate helper (§1.4). Stubbed as always-equal in dev/offline flow.
    // spec: Docs/RE/specs/frontend_scenes.md §1.4 "Version gate". CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Compares VFS game.ver against on-disk game.ver.
    /// Returns true when they match (or VFS is unavailable).
    /// Returns false on mismatch — caller must show msg 2204 and abort.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.4. CODE-CONFIRMED gate; stub = always true.
    /// </summary>
    private bool CheckGameVersion()
    {
        // Offline/dev stub: always pass.
        // When VFS is mounted: read data/cursor/game.ver from VFS and compare to local on-disk file.
        // On mismatch → return false so the caller shows msg 2204 and aborts. spec §1.4. CODE-CONFIRMED.
        // TODO (VFS online): _assets.ReadVfsText("data/cursor/game.ver") vs FileAccess.ReadFileAsString("game.ver").
        return true; // stub: treat as equal. spec §1.4 "match or VFS not mounted → continue". CODE-CONFIRMED.
    }

    // -------------------------------------------------------------------------
    // Save-ID persistence helpers (§1.6). Layer-05 Godot ConfigFile.
    // Equivalent of DoOption.ini [DO_OPTION] OPTION_ID.
    // spec: Docs/RE/specs/frontend_scenes.md §1.6. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private static string LoadSavedId()
    {
        // Load from Godot ConfigFile at user://mh_options.cfg.
        // Section [DO_OPTION], key OPTION_ID. spec §1.6. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        if (cfg.Load(LoginLayout.SaveIdConfigPath) != Error.Ok)
            return string.Empty;

        string raw = (string)cfg.GetValue(
            LoginLayout.SaveIdSection, LoginLayout.SaveIdKey,
            LoginLayout.SaveIdNullSentinel);

        // Spec §1.6: the "(null)" sentinel means no id is saved.
        return raw == LoginLayout.SaveIdNullSentinel ? string.Empty : raw;
    }

    private static void PersistSavedId(string account)
    {
        // Write id to Godot ConfigFile. spec §1.6. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        cfg.Load(LoginLayout.SaveIdConfigPath); // load existing (ignore if missing)
        cfg.SetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey, account);
        cfg.Save(LoginLayout.SaveIdConfigPath);
        GD.Print($"[LoginScreen] Save-ID: persisted account='{account}'.");
    }

    private static void ClearSavedId()
    {
        // Clearing = write the "(null)" sentinel. spec §1.6. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        cfg.Load(LoginLayout.SaveIdConfigPath);
        cfg.SetValue(LoginLayout.SaveIdSection, LoginLayout.SaveIdKey, LoginLayout.SaveIdNullSentinel);
        cfg.Save(LoginLayout.SaveIdConfigPath);
        GD.Print("[LoginScreen] Save-ID: cleared (wrote null sentinel).");
    }

    private void OnServerListPressed()
    {
        // Server-list button (action 102) — open the server-select flow.
        // spec §1.2 "Server-list button, action 102". CODE-CONFIRMED.
        string account = _accountEdit.Text.Trim();
        GD.Print($"[LoginScreen] Server list (action 102, account='{account}'). Emitting ServerListRequested.");
        EmitSignal(SignalName.ServerListRequested, account);
    }

    private void ShowQuitConfirmModal()
    {
        _toast.Text = "";
        // Fade in via alpha ramp ±64/frame → 255. spec §11.2g. CODE-CONFIRMED.
        _quitModalTarget = LoginLayout.DialogAlphaVisible;
        _quitModal.Visible = true; // make visible immediately so ramp is visible
        GD.Print("[LoginScreen] Quit confirm modal — fade in started.");
    }

    private void HideQuitConfirmModal()
    {
        // Fade out via alpha ramp ±64/frame → 0. spec §11.2g. CODE-CONFIRMED.
        _quitModalTarget = LoginLayout.DialogAlphaHidden;
        GD.Print("[LoginScreen] Quit confirm modal — fade out started.");
    }

    private void OnQuitConfirmed()
    {
        // spec §1.8 "Quit-confirm Yes → engine state 6 / substate 8". CODE-CONFIRMED.
        GD.Print("[LoginScreen] Quit confirmed (actions 113/114). Emitting QuitRequested.");
        _quitModalTarget = LoginLayout.DialogAlphaHidden; // begin fade out before quit
        EmitSignal(SignalName.QuitRequested);
    }

    private void OnSaveIdToggled()
    {
        // Toggle the save-id preference and persist / clear accordingly. spec §1.6. CODE-CONFIRMED.
        _saveIdChecked = !_saveIdChecked;
        if (_saveIdChecked)
        {
            // Persist the current account text (may be empty — will not overwrite with empty).
            string account = _accountEdit?.Text.Trim() ?? "";
            if (!string.IsNullOrEmpty(account))
                PersistSavedId(account);
        }
        else
        {
            ClearSavedId();
        }

        GD.Print($"[LoginScreen] Save-ID toggled: saveIdChecked={_saveIdChecked}");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a TextureRect sprite at the given canvas-local position/size.
    /// </summary>
    private static TextureRect MakeSprite(string name, AtlasTexture tex, int x, int y, int w, int h)
    {
        return new TextureRect
        {
            Name = name,
            Texture = tex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
        };
    }

    /// <summary>
    /// Slices an atlas sub-rect and adds a TextureRect sprite to the parent.
    /// No-op when the VFS is offline (atlas returns null).
    /// </summary>
    private void AddAtlasSprite(Control parent, string name, string atlasPath,
        int srcX, int srcY, int dstX, int dstY, int w, int h)
    {
        AtlasTexture? tex = _assets.Slice(atlasPath, srcX, srcY, w, h);
        if (tex is null) return;
        parent.AddChild(MakeSprite(name, tex, dstX, dstY, w, h));
    }

    /// <summary>
    /// Creates a single-line text input with a styled dark background for visibility.
    /// The spec (§11.2e) places ID/PW boxes at 102×13 px canvas-local, but Godot LineEdit
    /// requires ≥18 px height to render text. We use a flat-style dark input box.
    /// </summary>
    private static LineEdit MakeTextbox(bool masked, int maxLen)
    {
        var edit = new LineEdit
        {
            Secret = masked,
            CaretBlink = true,
            MaxLength = maxLen,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(102, 18),
        };

        // Dark recessed style matching the spec "recessed dark panel" aesthetic. §11.2e.
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.04f, 0.08f, 0.90f),
            BorderColor = new Color(0.45f, 0.38f, 0.22f, 0.80f),
        };
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = 3;
        style.ContentMarginRight = 3;
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 2;
        edit.AddThemeStyleboxOverride("normal", style);
        edit.AddThemeStyleboxOverride("focus", style);
        edit.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.75f));
        edit.AddThemeColorOverride("caret_color", new Color(0.95f, 0.90f, 0.55f));

        return edit;
    }
}