// Screens/LoginScreen.cs
//
// The legacy LOGIN screen (master scene state 1), rebuilt as a Godot Control to pixel fidelity
// against the full 21-site widget table in the spec.
//
// KEY CHANGES FROM THE PRIOR IMPLEMENTATION:
//   1. ATLAS CORRECTION — form widgets (OK/Login, Server-list, ID/PW textboxes, Save-ID checkbox,
//      Quit/Help strip) now come from login_slice1.dds (NOT loginwindow.dds), per spec §9.1 and §8.1.
//   2. ALL 7-STATE BUTTONS now use StateButton (via WidgetFactory.MakeStateButton) with all three
//      frames (NORMAL/HOVER/PRESSED) sliced from the correct atlas.
//   3. OPTION TABS — two option/tab buttons added at (40,82) and (164,82) on loginwindow.dds with
//      corrected NORMAL src (520,492)/(750,492), per spec §8.1 correction note.
//   4. QUIT BUTTON Y — Y = -3, CODE-CONFIRMED from spec §12 open item 1 RESOLVED.
//   5. QUIT-CONFIRM MODAL — InventWindow.dds chrome (318,647) 340×190; msg 4023/4024 prompts;
//      Yes buttons (actions 113/114), per spec §8.3 and §8.1.
//   6. LOCAL VALIDATION — ID >= 4 chars (msg 4025); PW >= 1 (msg 4026); per spec §1.4 and §1.9.
//   7. CAPTIONS from msg.xdb (4001–4022 range, 4023/4024, 4025/4026) via UiAssetLoader.Text.
//
// OFFLINE STUB: no server exists in this revival build. No network, no packet parsing, no
// credential validation against a server. OK accepts credentials after local validation (len rules)
// and emits LoginAccepted; quit-confirm emits QuitRequested. Sub-states 34–41 (lobby/server-list
// fetch, TAB-string, secure-context handshake) belong to the network layer and are absent here.
//
// PASSIVE: this is a view. It reads UI atlas chrome + msg.xdb captions (via UiAssetLoader) and
// turns OK/Quit gestures into C# events the flow node consumes. Zero game logic, zero domain state.
//
// spec: Docs/RE/specs/ui_system.md §8.1 (login BuildScene — 21-site widget table, CODE-CONFIRMED),
//       §9.1 (login asset manifest, CODE-CONFIRMED),
//       §8.3 (shared modal chrome — InventWindow.dds 318,647 340×190),
//       §8.0 (reference canvas 1024×768),
//       §6.2 (font table, D3DX Height column),
//       §10  (msg.xdb id ranges, CODE-CONFIRMED),
//       §13.1 (Godot reconstruction guidance — AtlasTexture, CanvasLayer, scaling).
// spec: Docs/RE/specs/frontend_scenes.md §1 (login scene flow),
//       §1.4 (OK button local validation — ID len ≥ 4, PW len ≥ 1, CODE-CONFIRMED),
//       §1.9 (message-catalogue id table, CODE-CONFIRMED).

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Login screen Control. Built on the 1024×768 reference canvas (spec §8.0) and scaled to the
/// window by the parent <see cref="ScreenHost"/>'s reference-size container.
///
/// <para>Widgets implemented (spec §8.1 full 21-site table):</para>
/// <list type="bullet">
///   <item><description>Account textbox @ (390,32) 102×13 — login_slice1.dds src (615,404)</description></item>
///   <item><description>Password textbox @ (568,32) 102×13 — login_slice1.dds src (615,404), masked</description></item>
///   <item><description>Save-ID checkbox @ (694,86) 13×13 — login_slice1.dds, unchecked (717,398) / checked (730,398)</description></item>
///   <item><description>OK/Login button (7-state) @ (456,64) 112×39 — login_slice1.dds N(266,398) H/P(490,398) — action 103</description></item>
///   <item><description>Server-list button (7-state) @ (456,166) 112×39 — login_slice1.dds N(154,398) H/P(378,398) — action 102 (disabled offline)</description></item>
///   <item><description>Quit/Help strip button (7-state) @ (456,-3) 111×38 — login_slice1.dds N(792,398) H/P(602,416) — action 105</description></item>
///   <item><description>Option tab 1 @ (40,82) 110×38 — loginwindow.dds N(520,492) H(635,492) P(520,492) — action 111</description></item>
///   <item><description>Option tab 2 @ (164,82) 110×38 — loginwindow.dds N(750,492) H(865,492) P(750,492) — action 112</description></item>
///   <item><description>Quit-confirm modal — InventWindow.dds chrome (318,647) 340×190; msg 4023/4024; Yes btns (113/114)</description></item>
/// </list>
/// </summary>
public sealed partial class LoginScreen : Control
{
    // -------------------------------------------------------------------------
    // Outgoing intent — consumed by the BootFlow node. Zero game logic here.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raised when the player presses OK/Login and local validation passes (ID len ≥ 4, PW len ≥ 1).
    /// Carries the entered account name.
    /// spec: Docs/RE/specs/frontend_scenes.md §1.4 — local validation then advance.
    /// </summary>
    [Signal]
    public delegate void LoginAcceptedEventHandler(string account);

    /// <summary>
    /// Raised when the player confirms quit (via the quit-confirm modal's Yes button).
    /// spec: Docs/RE/specs/frontend_scenes.md §1.8 — user quit-confirm → engine state 6.
    /// </summary>
    [Signal]
    public delegate void QuitRequestedEventHandler();

    /// <summary>
    /// Raised when the player clicks the Server-list button (action 102).
    /// Carries the current account name so BootFlow can pre-fill it on return.
    /// The actual server list is fetched from the lobby (port 10000) in a live build.
    /// In the offline/dev flow the caller (BootFlow) populates a synthetic list.
    /// spec: Docs/RE/specs/frontend_scenes.md §2 — "Server-list button opens ServerSelectScreen".
    /// spec: Docs/RE/specs/login_flow.md §2.1 — lobby port 10000, server-list records.
    /// </summary>
    [Signal]
    public delegate void ServerListRequestedEventHandler(string account);

    // -------------------------------------------------------------------------
    // View state (no domain state — positions, focus, modal open/close only)
    // -------------------------------------------------------------------------

    private LineEdit _accountEdit = null!;
    private LineEdit _passwordEdit = null!;
    private Label _toast = null!;
    private Control _quitModal = null!;

    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    /// <summary>
    /// Optionally inject a shared asset loader. When null the screen opens its own and
    /// disposes it on exit.
    /// </summary>
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
    // Construction — the full 21-site layout on the 1024×768 reference canvas.
    // spec: Docs/RE/specs/ui_system.md §8.1 (login BuildScene table, CODE-CONFIRMED).
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Fill the reference canvas; ScreenHost scales us to the window.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        int widgetCount = 0;

        // --- [1] Backdrop base — solid colour fallback (shown when VFS is offline). ---
        //     Dark blue-violet, typical of the legacy MMORPG night/dungeon aesthetic.
        //     // PLAUSIBLE (no spec for canvas background colour; legacy sets D3D clear colour not captured).
        var solid = new ColorRect
        {
            Name = "BackdropBase",
            Color = new Color(0.04f, 0.04f, 0.10f),
        };
        solid.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(solid);
        widgetCount++;

        // --- [2] Intro banner art — loginwindow_02.dds panning strip.
        //     spec §8.1 site "Intro banner": dst (—,97,202,372); x is register-fed/centre-computed
        //     (i.e. the original animates it in from the side; exact x is a runtime value not in spec).
        //     We place it at x=0 (left side, visible under the option tabs) as a plausible static
        //     approximation of the animated entry position. Height=372 places it y=97..469 panel-local,
        //     which maps to y=(BandTopY+97)..(BandTopY+469) absolu on the canvas.
        //     PLAUSIBLE POSITION (exact x not in spec — runtime animated).
        //     spec: Docs/RE/specs/ui_system.md §8.1 — "Intro banner BTN7 (—,97,202,372)". CODE-CONFIRMED.
        //     spec: Docs/RE/specs/ui_system.md §9.1 — loginwindow_02.dds panning banner. CODE-CONFIRMED.
        Texture2D? bannerArt = _assets.LoadAtlas(LoginLayout.AtlasLoginWindow02);
        if (bannerArt is not null)
        {
            var artRect = new TextureRect
            {
                Name = "BannerStrip",
                Texture = bannerArt,
                // Scale within its panel-local rect: spec gives (w=202, h=372) for the strip.
                // Keep aspect to display faithfully rather than stretching.
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                // Panel-local position: x=0 (plausible left anchor), y=BandTopY+97 adjusted for
                // the fact that BannerStrip is added as a child of LoginScreen (not the band).
                // We add it after the band so it renders on top of the dark backdrop but under widgets.
                Position = new Vector2(LoginLayout.BannerStripX,
                    LoginLayout.BandTopY + LoginLayout.BannerStripLocalY),
                Size = new Vector2(LoginLayout.BannerStripW, LoginLayout.BannerStripH),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(artRect);
            widgetCount++;
        }

        // --- [3] Form band — widget local coordinates from spec §8.1 are relative to this panel. ---
        // spec §8.0 — "Widget coordinates are in pixels, relative to their parent panel's origin."
        var band = new Control { Name = "LoginBand" };
        band.Position = new Vector2(0, LoginLayout.BandTopY);
        band.Size = new Vector2(LoginLayout.RefWidth, LoginLayout.BandHeight);
        band.CustomMinimumSize = band.Size;
        AddChild(band);

        // --- [3a] Form panel backing — semi-opaque rect placed behind form widgets only.
        //     The spec (§8.1, §9.1) does not document a dedicated form-panel chrome sub-rect;
        //     no form-panel art region was recovered. We add a ColorRect so buttons/textboxes
        //     remain legible. The backing covers the right half of the band where the form widgets
        //     live (x=380..810 panel-local, covering ID/PW textboxes, OK, ServerList, SaveId).
        //     The left zone (x=0..300) is left clear so the loginwindow_02.dds banner strip shows.
        //     // PLAUSIBLE backing geometry (legacy panel art unrecovered; covers widget cluster)
        //     spec: Docs/RE/specs/ui_system.md §8.1 (form widget cluster x=390..706). CODE-CONFIRMED coords.
        var formBacking = new ColorRect
        {
            Name = "FormBacking",
            Color = LoginLayout.FormBackingColor,
            Position = new Vector2(LoginLayout.FormBackingX, LoginLayout.FormBackingY),
            Size = new Vector2(LoginLayout.FormBackingW, LoginLayout.FormBackingH),
        };
        formBacking.MouseFilter = MouseFilterEnum.Ignore;
        band.AddChild(formBacking); // inserted first → drawn behind all sibling widgets
        widgetCount++;

        // --- [4] Account / ID textbox — @ (390,32) 102×13, login_slice1.dds src (615,404).
        //     spec §8.1 "ID/account textbox" — IME slot 16, maxlen 6. CODE-CONFIRMED.
        //     13px spec height is too small for a usable Godot LineEdit; we render at 22px for
        //     legibility. Position (x,y) is spec-exact; width (102) is spec-exact.
        _accountEdit = MakeTextbox(masked: false, maxLength: 6);
        PlaceLocal(_accountEdit, LoginLayout.AccountBox, sizeFromRect: false);
        _accountEdit.Size = new Vector2(LoginLayout.AccountBox.W, LoginLayout.TextboxRenderH);
        _accountEdit.CustomMinimumSize = new Vector2(LoginLayout.AccountBox.W, LoginLayout.TextboxRenderH);
        band.AddChild(_accountEdit);
        widgetCount++;

        // --- [4a] ID label — "ID:" prefix label to the left of the account textbox. ---
        //     Not in the widget table (the legacy used a GULabel atlas sprite with text from msg.xdb).
        //     Added here for legibility in the offline build. PLAUSIBLE.
        //     msg id 4001 might be "ID" or another caption — exact mapping not confirmed; fall back.
        var idLabel = WidgetFactory.MakeLabel("ID", LoginLayout.FontBodyHeight,
            new Color(0.90f, 0.90f, 0.75f));
        idLabel.Position = new Vector2(LoginLayout.AccountBox.X - 28, LoginLayout.AccountBox.Y + 4);
        idLabel.Size = new Vector2(26, 16);
        idLabel.HorizontalAlignment = HorizontalAlignment.Right;
        band.AddChild(idLabel);

        // --- [5] Password textbox — @ (568,32) 102×13, login_slice1.dds src (615,404), masked.
        //     spec §8.1 "Password textbox" — IME slot 12, maxlen 129. CODE-CONFIRMED.
        //     Same height adjustment as the ID textbox (13px → 22px render height).
        _passwordEdit = MakeTextbox(masked: true, maxLength: 129);
        PlaceLocal(_passwordEdit, LoginLayout.PasswordBox, sizeFromRect: false);
        _passwordEdit.Size = new Vector2(LoginLayout.PasswordBox.W, LoginLayout.TextboxRenderH);
        _passwordEdit.CustomMinimumSize = new Vector2(LoginLayout.PasswordBox.W, LoginLayout.TextboxRenderH);
        band.AddChild(_passwordEdit);
        widgetCount++;

        // --- [5a] PW label — "PW:" prefix label. PLAUSIBLE (as per [4a]). ---
        // msg id 4001 = "ID", 4002 = "Server List" (used by ServerList button below).
        // "PW" has no dedicated msg id in the 4001-4022 range; fall back to hardcoded string.
        var pwLabel = WidgetFactory.MakeLabel("PW", LoginLayout.FontBodyHeight,
            new Color(0.90f, 0.90f, 0.75f));
        pwLabel.Position = new Vector2(LoginLayout.PasswordBox.X - 28, LoginLayout.PasswordBox.Y + 4);
        pwLabel.Size = new Vector2(26, 16);
        pwLabel.HorizontalAlignment = HorizontalAlignment.Right;
        band.AddChild(pwLabel);

        // --- [6] Save-ID checkbox — @ (694,86) 13×13, login_slice1.dds.
        //     Unchecked NORMAL (717,398); checked = PRESSED (730,398).
        //     spec §8.1 "Save-ID checkbox", action 104. CODE-CONFIRMED. ---
        var saveIdBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.SaveIdCheck.X, LoginLayout.SaveIdCheck.Y,
            LoginLayout.SaveIdCheck.W, LoginLayout.SaveIdCheck.H,
            LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY, // NORMAL (unchecked)
            LoginLayout.SaveIdCheck.SrcX, LoginLayout.SaveIdCheck.SrcY, // HOVER = NORMAL
            LoginLayout.SaveIdCheckedSrcX, LoginLayout.SaveIdCheckedSrcY, // PRESSED (checked)
            LoginLayout.ActionSaveId,
            caption: "",
            captionTint: Colors.White);
        saveIdBtn.Name = "SaveIdCheckbox";
        // Caption label "Save ID" — inline beside the checkbox.
        // In the real client this is a GULabel drawn at the same row, not part of the checkbox widget.
        var saveIdLabel = WidgetFactory.MakeLabel(
            _assets.Text(4004u, "Save ID"), // msg id 4004 is in the 4001-4022 range
            LoginLayout.FontLabelHeight,
            new Color(0.85f, 0.85f, 0.9f));
        saveIdLabel.Position = new Vector2(
            LoginLayout.SaveIdCheck.X + LoginLayout.SaveIdCheck.W + 3,
            LoginLayout.SaveIdCheck.Y);
        saveIdLabel.Size = new Vector2(60, 14);
        band.AddChild(saveIdLabel);
        band.AddChild(saveIdBtn);
        widgetCount += 2;

        // --- [7] OK / Login button (7-state) — @ (456,64) 112×39.
        //     NORMAL (266,398)  HOVER/PRESSED (490,398)  login_slice1.dds  action 103.
        //     spec §8.1 "OK/Login button". CODE-CONFIRMED. ---
        var okBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.OkButton.X, LoginLayout.OkButton.Y,
            LoginLayout.OkButton.W, LoginLayout.OkButton.H,
            LoginLayout.OkButton.SrcX, LoginLayout.OkButton.SrcY, // NORMAL (266,398)
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY, // HOVER  (490,398)
            LoginLayout.OkHoverSrcX, LoginLayout.OkHoverSrcY, // PRESSED (490,398) — HOVER==PRESSED per spec
            LoginLayout.ActionOk,
            caption: _assets.Text(4003u, "Login"),
            captionTint: Colors.White);
        okBtn.Name = "OkButton";
        okBtn.ActionFired += _ => OnOkPressed();
        band.AddChild(okBtn);
        widgetCount++;

        // --- [8] Server-list button (7-state) — @ (456,166) 112×39.
        //     NORMAL (154,398)  HOVER/PRESSED (378,398)  login_slice1.dds  action 102.
        //     OFFLINE STUB: disabled (no lobby server in this build).
        //     spec §8.1 "Server-list button". CODE-CONFIRMED. ---
        var serverBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.ServerListButton.X, LoginLayout.ServerListButton.Y,
            LoginLayout.ServerListButton.W, LoginLayout.ServerListButton.H,
            LoginLayout.ServerListButton.SrcX, LoginLayout.ServerListButton.SrcY, // NORMAL (154,398)
            LoginLayout.ServerListHoverSrcX, LoginLayout.ServerListHoverSrcY, // HOVER  (378,398)
            LoginLayout.ServerListHoverSrcX, LoginLayout.ServerListHoverSrcY, // PRESSED (378,398)
            LoginLayout.ActionServerList,
            caption: _assets.Text(4002u, "Server List"),
            captionTint: Colors.White);
        serverBtn.Name = "ServerListButton";
        // Server button is now ENABLED — BootFlow handles it (online or dev-offline synthetic list).
        // spec: Docs/RE/specs/frontend_scenes.md §2 — "Server-list button opens ServerSelectScreen".
        serverBtn.ActionFired += _ => OnServerListPressed();
        band.AddChild(serverBtn);
        widgetCount++;

        // --- [9] Quit/Help strip button (7-state) — @ (456,-3) 111×38.
        //     Y = -3: CODE-CONFIRMED — spec §12 open item 1 RESOLVED.
        //     NORMAL (792,398)  HOVER/PRESSED (602,416)  login_slice1.dds  action 105.
        //     spec §8.1 "Quit/Help strip". CODE-CONFIRMED. ---
        var quitBtn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginSlice1,
            LoginLayout.QuitButton.X, LoginLayout.QuitButton.Y,
            LoginLayout.QuitButton.W, LoginLayout.QuitButton.H,
            LoginLayout.QuitButton.SrcX, LoginLayout.QuitButton.SrcY, // NORMAL (792,398)
            LoginLayout.QuitHoverSrcX, LoginLayout.QuitHoverSrcY, // HOVER  (602,416)
            LoginLayout.QuitHoverSrcX, LoginLayout.QuitHoverSrcY, // PRESSED (602,416)
            LoginLayout.ActionQuit,
            caption: _assets.Text(4005u, "Quit"),
            captionTint: Colors.White);
        quitBtn.Name = "QuitButton";
        quitBtn.ActionFired += _ => ShowQuitConfirmModal();
        band.AddChild(quitBtn);
        widgetCount++;

        // --- [10] Option/tab button 1 — @ (40,82) 110×38.
        //     NORMAL (520,492)  HOVER (635,492)  PRESSED (520,492)  loginwindow.dds  action 111.
        //     spec §8.1 "Option/tab button 1". NORMAL src corrected to (520,492). CODE-CONFIRMED. ---
        var tab1Btn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab1.X, LoginLayout.OptionTab1.Y,
            LoginLayout.OptionTab1.W, LoginLayout.OptionTab1.H,
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY, // NORMAL (520,492)
            LoginLayout.OptionTab1HoverSrcX, LoginLayout.OptionTab1HoverSrcY, // HOVER (635,492)
            LoginLayout.OptionTab1.SrcX, LoginLayout.OptionTab1.SrcY, // PRESSED = NORMAL (520,492)
            LoginLayout.ActionOptionTab1,
            caption: _assets.Text(4006u, "Option 1"),
            captionTint: Colors.White);
        tab1Btn.Name = "OptionTab1";
        // Decorative only in offline stub — no option sub-panel implemented.
        band.AddChild(tab1Btn);
        widgetCount++;

        // --- [11] Option/tab button 2 — @ (164,82) 110×38.
        //     NORMAL (750,492)  HOVER (865,492)  PRESSED (750,492)  loginwindow.dds  action 112.
        //     spec §8.1 "Option/tab button 2". NORMAL src corrected to (750,492). CODE-CONFIRMED. ---
        var tab2Btn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasLoginWindow,
            LoginLayout.OptionTab2.X, LoginLayout.OptionTab2.Y,
            LoginLayout.OptionTab2.W, LoginLayout.OptionTab2.H,
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY, // NORMAL (750,492)
            LoginLayout.OptionTab2HoverSrcX, LoginLayout.OptionTab2HoverSrcY, // HOVER (865,492)
            LoginLayout.OptionTab2.SrcX, LoginLayout.OptionTab2.SrcY, // PRESSED = NORMAL (750,492)
            LoginLayout.ActionOptionTab2,
            caption: _assets.Text(4007u, "Option 2"),
            captionTint: Colors.White);
        tab2Btn.Name = "OptionTab2";
        band.AddChild(tab2Btn);
        widgetCount++;

        // --- [12] Validation toast line — hidden until a validation error on OK click.
        //     Captions come from msg.xdb ids 4025/4026 (spec §10). Loaded lazily at click time
        //     (the strings depend on which field is bad, so we set them then). ---
        _toast = WidgetFactory.MakeLabel("", LoginLayout.FontBodyHeight + 2, new Color(0.95f, 0.40f, 0.40f));
        _toast.Position = new Vector2(LoginLayout.RefWidth / 2f - 260f, 220f);
        _toast.Size = new Vector2(520, 24);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        band.AddChild(_toast);
        widgetCount++;

        // --- [13] Quit-confirm modal — built but initially hidden.
        //     Shown when the Quit button is clicked.
        //     spec §8.1 "Quit-confirm Yes #1/#2", §8.3 "shared InventWindow.dds chrome". ---
        _quitModal = BuildQuitConfirmModal();
        _quitModal.Visible = false;
        AddChild(_quitModal); // added to LoginScreen root (not band) so it overlays everything
        widgetCount++;

        GD.Print($"[LoginScreen] Built — {widgetCount} widgets; " +
                 $"vfs={(_assets.HasVfs ? "real-atlas" : "offline-fallback")}; " +
                 $"captions={(HasMsg() ? "msg.xdb" : "en-fallback")}.");
    }

    // -------------------------------------------------------------------------
    // Quit-confirm modal (spec §8.3 — InventWindow.dds shared chrome)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the quit-confirm modal panel (InventWindow.dds chrome, msg 4023/4024, Yes buttons).
    /// spec: Docs/RE/specs/ui_system.md §8.3 — "340×190 chrome at src (318,647) InventWindow.dds".
    /// spec: Docs/RE/specs/ui_system.md §8.1 sites "Quit-confirm Yes #1/#2". CODE-CONFIRMED.
    /// </summary>
    private Control BuildQuitConfirmModal()
    {
        // Modal root — centred on the 1024×768 canvas.
        // Chrome width/height: spec §8.3 — 340×190.
        int chromeW = LoginLayout.ModalChromeW; // 340
        int chromeH = LoginLayout.ModalChromeH; // 190
        int chromeX = (LoginLayout.RefWidth - chromeW) / 2; // 342
        int chromeY = (LoginLayout.RefHeight - chromeH) / 2; // 289

        var modal = new Control
        {
            Name = "QuitConfirmModal",
            Position = new Vector2(chromeX, chromeY),
            Size = new Vector2(chromeW, chromeH),
        };

        // Chrome background — InventWindow.dds sub-rect (318,647) 340×190.
        // spec §8.3: "same 340×190 chrome at source (318,647) from data/ui/inventwindow.dds".
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
            // VFS offline fallback — solid dark panel so the modal is still readable.
            var fallbackBg = new ColorRect
            {
                Name = "ModalFallbackBg",
                Color = new Color(0.07f, 0.07f, 0.12f, 0.97f),
            };
            fallbackBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            modal.AddChild(fallbackBg);
        }

        // Prompt label — msg 4023 (quit-confirm prompt #1).
        // spec §8.1 site "Quit-confirm prompt #1" @ (10,100) 330×20. CODE-CONFIRMED.
        var prompt1 = WidgetFactory.MakeLabel(
            _assets.Text(LoginLayout.MsgQuitConfirm1, "Are you sure you want to quit?"),
            LoginLayout.FontBodyHeight,
            new Color(0.9f, 0.9f, 0.9f));
        prompt1.Position = new Vector2(10, 80);
        prompt1.Size = new Vector2(320, 20);
        prompt1.HorizontalAlignment = HorizontalAlignment.Center;
        modal.AddChild(prompt1);

        // Second prompt line — msg 4024 (quit-confirm prompt #2).
        // spec §8.1 site "Quit-confirm prompt #2" @ (10,100) 330×20. CODE-CONFIRMED.
        var prompt2 = WidgetFactory.MakeLabel(
            _assets.Text(LoginLayout.MsgQuitConfirm2, "Unsaved progress will be lost."),
            LoginLayout.FontBodyHeight,
            new Color(0.75f, 0.75f, 0.75f));
        prompt2.Position = new Vector2(10, 100);
        prompt2.Size = new Vector2(320, 20);
        prompt2.HorizontalAlignment = HorizontalAlignment.Center;
        modal.AddChild(prompt2);

        // Yes button #1 — @ (120,136) 113×40, InventWindow.dds N(302,900) H(415,900) action 113.
        // spec §8.1 site "Quit-confirm Yes #1". CODE-CONFIRMED.
        var yes1Btn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasInventWindow,
            LoginLayout.QuitConfirmYes1.X, LoginLayout.QuitConfirmYes1.Y,
            LoginLayout.QuitConfirmYes1.W, LoginLayout.QuitConfirmYes1.H,
            LoginLayout.QuitConfirmYes1.SrcX, LoginLayout.QuitConfirmYes1.SrcY, // NORMAL (302,900)
            LoginLayout.QuitConfirmYes1HoverSrcX, LoginLayout.QuitConfirmYes1HoverSrcY, // HOVER  (415,900)
            LoginLayout.QuitConfirmYes1.SrcX, LoginLayout.QuitConfirmYes1.SrcY, // PRESSED = NORMAL
            LoginLayout.ActionQuitConfirmYes1,
            caption: _assets.Text(4008u, "Yes"),
            captionTint: Colors.White);
        yes1Btn.Name = "QuitConfirmYes1";
        yes1Btn.ActionFired += _ => OnQuitConfirmed();
        modal.AddChild(yes1Btn);

        // Yes button #2 — @ (120,136) 113×40, InventWindow.dds N(302,860) H(415,860) action 114.
        // spec §8.1 site "Quit-confirm Yes #2". CODE-CONFIRMED.
        // (Both Yes buttons appear at the same position — the spec shows identical dst rects.
        //  In the original they alternate visibility; in this offline build they stack; we keep #2
        //  slightly offset below for legibility when both are visible.)
        var yes2Btn = WidgetFactory.MakeStateButton(
            _assets, LoginLayout.AtlasInventWindow,
            LoginLayout.QuitConfirmYes2.X, LoginLayout.QuitConfirmYes2.Y + 44, // offset for readability
            LoginLayout.QuitConfirmYes2.W, LoginLayout.QuitConfirmYes2.H,
            LoginLayout.QuitConfirmYes2.SrcX, LoginLayout.QuitConfirmYes2.SrcY, // NORMAL (302,860)
            LoginLayout.QuitConfirmYes2HoverSrcX, LoginLayout.QuitConfirmYes2HoverSrcY, // HOVER  (415,860)
            LoginLayout.QuitConfirmYes2.SrcX, LoginLayout.QuitConfirmYes2.SrcY, // PRESSED = NORMAL
            LoginLayout.ActionQuitConfirmYes2,
            caption: _assets.Text(4009u, "OK"),
            captionTint: Colors.White);
        yes2Btn.Name = "QuitConfirmYes2";
        yes2Btn.ActionFired += _ => OnQuitConfirmed();
        modal.AddChild(yes2Btn);

        // No / Cancel label — no action id in the spec; we add a plain text button for usability.
        var noBtn = new Button { Text = _assets.Text(4010u, "No") };
        noBtn.Position = new Vector2(chromeW - 140, LoginLayout.QuitConfirmYes1.Y);
        noBtn.Size = new Vector2(100, 36);
        noBtn.Pressed += HideQuitConfirmModal;
        modal.AddChild(noBtn);

        return modal;
    }

    // -------------------------------------------------------------------------
    // Intent handlers (NO game logic — emit signals the flow node consumes).
    // -------------------------------------------------------------------------

    private void OnOkPressed()
    {
        // Local credential validation — CODE-CONFIRMED from spec §1.4:
        //   "ID length < 4 → show msg 4025 → return to sub-state 6 (stay on form). No network send."
        //   "password length < 1 → show msg 4026 → return to sub-state 6. No network send."
        // spec: Docs/RE/specs/frontend_scenes.md §1.4. CODE-CONFIRMED.
        // spec: Docs/RE/specs/ui_system.md §10 — msg ids 4025/4026. CODE-CONFIRMED.

        _toast.Text = "";

        string account = _accountEdit.Text.Trim();
        if (account.Length < LoginLayout.MinIdLength)
        {
            // spec: ID length < 4 → msg 4025.
            // spec: Docs/RE/specs/frontend_scenes.md §1.4 CODE-CONFIRMED.
            _toast.Text = _assets.Text(LoginLayout.MsgErrShortId,
                $"ID must be at least {LoginLayout.MinIdLength} characters.");
            GD.Print(
                $"[LoginScreen] Validation: ID too short ({account.Length} < {LoginLayout.MinIdLength}), msg 4025.");
            return;
        }

        if (_passwordEdit.Text.Length < LoginLayout.MinPwLength)
        {
            // spec: password length < 1 → msg 4026.
            // spec: Docs/RE/specs/frontend_scenes.md §1.4 CODE-CONFIRMED.
            _toast.Text = _assets.Text(LoginLayout.MsgErrEmptyPassword,
                "Please enter a password.");
            GD.Print("[LoginScreen] Validation: password empty, msg 4026.");
            return;
        }

        // OFFLINE STUB: local validation passed. No server, no packet, no handshake.
        // Emit LoginAccepted so BootFlow advances to char-select (state 4 analogue).
        // spec: Docs/RE/specs/frontend_scenes.md §1.4 — "persist Save-ID, advance to sub-state 31".
        //       In this offline build we skip sub-states 31–41 (network absent) and go straight
        //       to the character-select phase.
        GD.Print($"[LoginScreen] OK — validation passed (offline stub), account='{account}'. Emitting LoginAccepted.");
        EmitSignal(SignalName.LoginAccepted, account);
    }

    private void OnServerListPressed()
    {
        // Emit ServerListRequested so BootFlow opens the ServerSelectScreen.
        // The account field is passed so the server select can display a greeting if desired,
        // and so BootFlow knows the account credential to forward to LoginAsync.
        // No local validation required before showing the server list — it is purely informational.
        // spec: Docs/RE/specs/frontend_scenes.md §2 — server-list button opens selection screen.
        // spec: Docs/RE/specs/login_flow.md §2.1 — server list fetched from lobby port 10000.
        string account = _accountEdit.Text.Trim();
        GD.Print($"[LoginScreen] Server List button pressed (account='{account}'). Emitting ServerListRequested.");
        EmitSignal(SignalName.ServerListRequested, account);
    }

    private void ShowQuitConfirmModal()
    {
        // spec: §1.8 — quit-confirm modal shown on Quit/Help strip click.
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
        // spec: §1.8 "Quit-confirm Yes path → engine state 6 / substate 8 (quit)".
        // In this presentation build: emit QuitRequested; BootFlow calls GetTree().Quit().
        GD.Print("[LoginScreen] Quit confirmed (actions 113/114). Emitting QuitRequested.");
        _quitModal.Visible = false;
        EmitSignal(SignalName.QuitRequested);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds an editable text field at the spec-recovered rect.
    /// spec: Docs/RE/specs/ui_system.md §8.1 — textbox rows; §13.3 — Godot LineEdit guidance.
    /// </summary>
    private static LineEdit MakeTextbox(bool masked, int maxLength)
    {
        return new LineEdit
        {
            // spec §8.1 — masked password field; both accept CP949 Korean via Godot IME.
            Secret = masked,
            CaretBlink = true,
            MaxLength = maxLength,
            Alignment = HorizontalAlignment.Left,
            // The recovery rect is 13px tall — too short for a glyph.  We keep the widget for
            // the control frame and let the theme override the minimum height for readability.
            // This matches the legacy client where text was rendered over the atlas frame at the
            // font height rather than inside a clipped 13px box.
            CustomMinimumSize = new Vector2(102, 18),
        };
    }

    /// <summary>
    /// Places a control at a recovered panel-local position and optionally sizes it.
    /// spec §8.0 — "(x,y) is the widget's screen-local position (relative to its immediate parent)."
    /// </summary>
    private static void PlaceLocal(Control c, WidgetRect rect, bool sizeFromRect = true)
    {
        c.Position = new Vector2(rect.X, rect.Y);
        if (sizeFromRect)
            c.Size = new Vector2(rect.W, rect.H);
        c.CustomMinimumSize = new Vector2(rect.W, rect.H);
    }

    /// <summary>Returns true if the msg.xdb catalogue loaded at least one record.</summary>
    private bool HasMsg() => _assets.HasVfs && _assets.Text(4001u, "") != "";
}