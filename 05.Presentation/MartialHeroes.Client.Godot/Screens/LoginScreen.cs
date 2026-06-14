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
// LAYERS (bottom → top, matching the legacy Z-order from §11.2):
//   1. A — full background art panel (0,0,1024,398) src(0,0)                      §11.2b
//   2. B — main panel chrome (0,110,1024,490) src(0,0)                             §11.2a
//   3. D — channel-selector panning strip                                           §11.2b
//   4. B — server listbox + scrolls                                                 §11.2a
//   5. A — bottom login bar (height-scaled Y=326) + baked label art plates          §11.2e
//   6. A — confirm/gold button (456,166,112,39) src(154,398)                       §11.2e
//   7. A — face plate overlay (265,0,494,113) src(0,469)                           §11.2e
//   8. B — option/tab buttons 1+2 at (40,82) / (164,82)                            §11.2f
//   9. LineEdit controls for ID/PW at (390,32) / (568,32) (height-expanded to 22)  §11.2e
//  10. A — Save-ID checkbox (694,86,13,13)                                          §11.2e
//  11. A — secondary bottom button (OK / Login) at (456,64,112,39) src(266,398)    §11.2e
//  12. C — quit-confirm modal (342,289,340,190) src(318,647)                       §11.2d
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

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScreen] Build failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    // -------------------------------------------------------------------------
    // UI construction — §11.2 layer-by-layer.
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Fill the reference 1024×768 canvas. ScreenHost scales us to the window.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        int widgetCount = 0;

        // =======================================================================
        // [L1] Full background art panel (§11.2b).
        // A@(0,0,1024,398) src(0,0) — login_slice1.dds — the panoramic background art.
        // Falls back to a dark solid when VFS is offline.
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
        }
        else
        {
            // VFS offline fallback: dark art-noir background.
            var fallback = new ColorRect
            {
                Name = "BgFallback",
                Color = new Color(0.04f, 0.04f, 0.10f),
            };
            fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(fallback);
        }

        widgetCount++;

        // =======================================================================
        // [L2] Main panel chrome (§11.2a).
        // B@(0,110,1024,490) src(0,0) — loginwindow.dds — the stone bar that frames the form area.
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

        // =======================================================================
        // [L3] Channel-selector / banner strip (§11.2b).
        // D@two-block loop — loginwindow_02.dds — the panning parchment strip visible to
        // the left of the form. Body src V starts at 448, step +124 for 2 blocks.
        // X starts at 30, step +233.
        // spec: Docs/RE/specs/frontend_scenes.md §11.2b "Channel block: body". CODE-CONFIRMED.
        // =======================================================================
        for (int blk = 0; blk < 2; blk++)
        {
            int blockX = 30 + blk * 233;
            int bodySrcV = 448 + blk * 124;

            // Block body image: (blockX+47, 97, 100, 372) src (bodySrcV, 6).
            // spec §11.2b "Channel block: body". CODE-CONFIRMED.
            AtlasTexture? blockBody = _assets.Slice(
                LoginLayout.AtlasLoginWindow02,
                bodySrcV, 6, 100, 372);
            if (blockBody is not null)
            {
                var bodyRect = MakeSprite($"ChannelBlock{blk}Body", blockBody,
                    blockX + 47, 97, 100, 372);
                AddChild(bodyRect);
                widgetCount++;
            }
        }

        // =======================================================================
        // [L4] Server listbox container + scroll controls (§11.2a).
        // Placed here (above the channel blocks) for correct Z-order.
        // These are decorative in the offline build; the real server-select is a separate screen.
        // spec §11.2a. CODE-CONFIRMED positions/src.
        // =======================================================================
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
                // Make it transparent so it doesn't cover the channel blocks entirely.
                listboxRect.Modulate = new Color(1f, 1f, 1f, 0.7f);
                AddChild(listboxRect);
                widgetCount++;
            }
        }

        // =======================================================================
        // [L5] Bottom login bar + baked label art plates (§11.2e).
        // A@(0, 326, 1024, 442) src(0,582) — login_slice1.dds — the brown/stone bottom band.
        // Y = 326 on the 1024×768 reference canvas (= 326×768/768).
        // spec §11.2e "Bottom login-bar panel". CODE-CONFIRMED.
        // =======================================================================
        AtlasTexture? bottomBar = _assets.Slice(
            LoginLayout.AtlasLoginSlice1,
            LoginLayout.BottomBarSrcX, LoginLayout.BottomBarSrcY,
            LoginLayout.BottomBarW, LoginLayout.BottomBarH);

        Control bottomBand; // parent for all form widgets below
        if (bottomBar is not null)
        {
            var bottomBarRect = MakeSprite("BottomBar", bottomBar,
                0, LoginLayout.BottomBarCanvasY,
                LoginLayout.BottomBarW, LoginLayout.BottomBarH);
            AddChild(bottomBarRect);
            widgetCount++;

            // Form widgets are children of this container (panel-local = band-relative coords).
            bottomBand = new Control
            {
                Name = "BottomBand",
                Position = new Vector2(0, LoginLayout.BottomBarCanvasY),
                Size = new Vector2(LoginLayout.RefWidth, LoginLayout.BottomBarH),
            };
            AddChild(bottomBand);
        }
        else
        {
            // Offline fallback: solid parchment-tone band.
            var fallbackBand = new ColorRect
            {
                Name = "BottomBandFallback",
                Color = new Color(0.22f, 0.14f, 0.08f, 0.95f),
                Position = new Vector2(0, LoginLayout.BottomBarCanvasY),
                Size = new Vector2(LoginLayout.RefWidth, LoginLayout.BottomBarH),
            };
            AddChild(fallbackBand);
            bottomBand = fallbackBand;
        }

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
        // ID field:  A@(390,32,102,13) src(615,404), max 16, action 109.
        // PW field:  A@(568,32,102,13) src(615,404), max 12, masked, action 110.
        // spec §11.2e. CODE-CONFIRMED positions and max-lengths.
        //
        // NOTE: The spec DDS frame height is 13px — too small for a usable Godot LineEdit.
        // We keep the spec X/Y position and width (102) exact; height is expanded to 22px.
        // The legacy client drew text ON TOP of the atlas frame at the font height, not
        // clipped inside a 13px box. Our LineEdit approach is the closest Godot equivalent.
        // =======================================================================
        _accountEdit = MakeTextbox(masked: false, maxLen: LoginLayout.IdMaxLength);
        _accountEdit.Name = "AccountEdit";
        _accountEdit.Position = new Vector2(LoginLayout.AccountBox.X, LoginLayout.AccountBox.Y);
        _accountEdit.Size = new Vector2(LoginLayout.AccountBox.W, LoginLayout.TextboxRenderH);
        bottomBand.AddChild(_accountEdit);
        widgetCount++;

        _passwordEdit = MakeTextbox(masked: true, maxLen: LoginLayout.PwMaxLength);
        _passwordEdit.Name = "PasswordEdit";
        _passwordEdit.Position = new Vector2(LoginLayout.PasswordBox.X, LoginLayout.PasswordBox.Y);
        _passwordEdit.Size = new Vector2(LoginLayout.PasswordBox.W, LoginLayout.TextboxRenderH);
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
        _quitModal.Visible = false;
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
        // Local credential validation. spec §1.4. CODE-CONFIRMED.
        _toast.Text = "";

        string account = _accountEdit.Text.Trim();

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
        _quitModal.Visible = true;
        GD.Print("[LoginScreen] Quit confirm modal shown.");
    }

    private void HideQuitConfirmModal()
    {
        _quitModal.Visible = false;
    }

    private void OnQuitConfirmed()
    {
        // spec §1.8 "Quit-confirm Yes → engine state 6 / substate 8". CODE-CONFIRMED.
        GD.Print("[LoginScreen] Quit confirmed (actions 113/114). Emitting QuitRequested.");
        _quitModal.Visible = false;
        EmitSignal(SignalName.QuitRequested);
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
    /// Creates a single-line text input.
    /// </summary>
    private static LineEdit MakeTextbox(bool masked, int maxLen)
    {
        return new LineEdit
        {
            Secret = masked,
            CaretBlink = true,
            MaxLength = maxLen,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(102, 18),
        };
    }
}