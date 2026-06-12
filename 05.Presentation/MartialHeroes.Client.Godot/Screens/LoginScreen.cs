// Screens/LoginScreen.cs
//
// The legacy LOGIN screen (master scene state 1), rebuilt as a Godot Control.
// Layout coordinates are INTEROP FACTS recovered in the spec layout tables — every literal
// pixel value below cites its spec row.
//
// OFFLINE STUB: no server exists in this revival build. There is no network here and no game
// logic. The OK button accepts ANY credentials locally and raises LoginAccepted; the BootFlow
// node then advances to the character-select screen. Sub-states 34–41 (lobby/server-list fetch
// threads, credential TAB-string, secure-context handshake) from spec §6.3 are intentionally
// NOT implemented — they belong to the network layer, which is absent in this presentation-only
// flow.
//
// PASSIVE: this is a view. It reads UI atlas chrome + msg.xdb captions from the VFS (via
// UiAssetLoader) and turns the OK/Quit gestures into a C# event the flow node consumes. It does
// not validate credentials, does not touch domain state, does not parse packets.
//
// spec: Docs/RE/specs/ui_system.md §2.1 (login BuildScene layout table),
//       §3.1 (login asset manifest), §4 (font table), §6.2 (state 1 → 2), §8 (Godot rebuild).
// spec: Docs/RE/formats/ui_manifests.md §6 (DXT3 atlas), §5 (do.dds caveat).

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Login screen Control. Built on the 1024×768 reference canvas (spec §2.0) and scaled to the
/// window by the parent <see cref="ScreenHost"/>'s reference-size container.
///
/// Widgets (spec §2.1 BuildScene):
///   - account textbox  @ (390,32) 102×13 atop the login panel band   (spec §2.1 Account/ID)
///   - password textbox @ (568,32) 102×13, masked                     (spec §2.1 Password)
///   - OK / Login button (7-state)  @ (456,64) 112×39 → action 200/201/202 (spec §2.1)
///   - Server-list button (7-state) @ (456,166) 112×39 → action 206/16     (spec §2.1)
///   - Quit button (7-state)        @ (456, Y) 111×38 → action 209/220      (spec §2.1; Y PARTIAL)
///
/// The textbox/button local coordinates are relative to the login panel band whose top is at
/// y=110 (spec §2.1 "Animated login slice" — y 110). We add that band origin to place widgets
/// on the absolute reference canvas.
/// </summary>
public sealed partial class LoginScreen : Control
{
    // ---------------------------------------------------------------------
    // Outgoing intent — consumed by the BootFlow node (no game logic here).
    // ---------------------------------------------------------------------

    /// <summary>Raised when the player presses OK/Login. Carries the entered account name.</summary>
    [Signal]
    public delegate void LoginAcceptedEventHandler(string account);

    /// <summary>Raised when the player presses Quit.</summary>
    [Signal]
    public delegate void QuitRequestedEventHandler();

    // ---------------------------------------------------------------------
    // View state
    // ---------------------------------------------------------------------

    private LineEdit _accountEdit = null!;
    private LineEdit _passwordEdit = null!;
    private Label _toast = null!;
    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    /// <summary>
    /// Optionally inject a shared asset loader (the flow node opens one VFS for both screens).
    /// When null, the screen opens its own loader and disposes it on exit.
    /// </summary>
    public UiAssetLoader? SharedAssets { get; set; }

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
            GD.PrintErr($"[Screens] LoginScreen build failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    // ---------------------------------------------------------------------
    // Construction — on the 1024×768 reference canvas (spec §2.0).
    // ---------------------------------------------------------------------

    private void BuildUi()
    {
        // Fill the reference canvas; ScreenHost scales us to the window.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        int widgetCount = 0;

        // --- Backdrop. spec §2.1 "Root backdrop panel" is 1024×398, "no sprite" — the screen
        //     background is the panning intro slice (login_slice1.dds), NOT the loginwindow.dds
        //     sprite SHEET (which holds buttons/strips and must not be stretched fullscreen).
        //     We lay a solid ink base, then the intro-slice art on top. spec §3.1 manifest. ---
        var solid = new ColorRect { Name = "BackdropBase", Color = new Color(0.09f, 0.08f, 0.11f) };
        solid.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(solid);
        widgetCount++;

        // login_slice1.dds is the panning intro art (spec §3.1). It renders as the hero backdrop.
        Texture2D? introArt = _assets.LoadAtlas(LoginLayout.AtlasLoginSlice);
        if (introArt is not null)
        {
            var art = new TextureRect
            {
                Name = "Backdrop",
                Texture = introArt,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            };
            art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(art);
            widgetCount++;
        }

        // --- Login form band. spec §2.1: "Animated login slice" top at y=110 — the account /
        //     password / OK widget local coordinates are relative to this band's origin. ---
        var band = new Control { Name = "LoginBand" };
        band.Position = new Vector2(0, LoginLayout.BandTopY); // spec §2.1 banner Y = 110
        band.Size = new Vector2(LoginLayout.RefWidth, LoginLayout.BandHeight);
        AddChild(band);

        // Title plate so the panel reads as a login window even when only the solid fallback shows.
        // English placeholder — the legacy title caption id was not recovered (spec §7 open item 4).
        var title = MakeLabel("MARTIAL HEROES  —  LOGIN",
            LoginLayout.FontTitleHeight, new Color(0.95f, 0.86f, 0.55f));
        title.Position = new Vector2(LoginLayout.RefWidth / 2f - 220f, -84f);
        title.Size = new Vector2(440, 40);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        band.AddChild(title);
        widgetCount++;

        // --- ID label strip. spec §2.1: "Label strip — ID" @ (340,30) 38×13.
        //     English placeholder caption (per-widget caption id not recovered — spec §7 item 4). ---
        widgetCount += AddCaptionStrip(band, LoginLayout.IdLabel, "ID");

        // --- Account / ID textbox. spec §2.1: @ (390,32) 102×13, IME field 1, CP949. ---
        _accountEdit = MakeTextbox(masked: false);
        PlaceLocal(_accountEdit, LoginLayout.AccountBox);
        band.AddChild(_accountEdit);
        widgetCount++;

        // --- PW label strip. spec §2.1: "Label strip — PW" @ (507,30) 49×13. English placeholder. ---
        widgetCount += AddCaptionStrip(band, LoginLayout.PwLabel, "PW");

        // --- Password textbox. spec §2.1: @ (568,32) 102×13, IME field 2, masked. ---
        _passwordEdit = MakeTextbox(masked: true);
        PlaceLocal(_passwordEdit, LoginLayout.PasswordBox);
        band.AddChild(_passwordEdit);
        widgetCount++;

        // --- Save-ID checkbox. spec §2.1: GUCheckBox @ (694,86) 13×13. English placeholder. ---
        var saveId = new CheckBox { Text = "Save ID" };
        PlaceLocal(saveId, LoginLayout.SaveIdCheck, sizeFromRect: false);
        band.AddChild(saveId);
        widgetCount++;

        // --- OK / Login button (7-state). spec §2.1: @ (456,64) 112×39, action 200/201/202. ---
        var okBtn = MakeAtlasButton(LoginLayout.OkButton, "OK", LoginLayout.AtlasLoginWindow);
        okBtn.Pressed += OnOkPressed;
        band.AddChild(okBtn);
        widgetCount++;

        // --- Server-list button (7-state). spec §2.1: @ (456,166) 112×39, action 206/16. ---
        // OFFLINE STUB: no lobby server exists; this button is shown for layout fidelity but
        // disabled (the legacy server-list fetch threads of spec §6.3 sub-states 34–41 are
        // network-layer concerns, absent here).
        var serverBtn = MakeAtlasButton(
            LoginLayout.ServerListButton, "Server List", LoginLayout.AtlasLoginWindow);
        serverBtn.Disabled = true;
        serverBtn.TooltipText = "Offline build — no lobby server (spec §6.3 sub-states 34–41 unimplemented).";
        band.AddChild(serverBtn);
        widgetCount++;

        // --- Quit button (7-state). spec §2.1: @ (456, Y) 111×38, action 209/220.
        //     Y is PARTIAL in the spec (passed via register, not a literal). We use the
        //     server-list Y + one button pitch as a plausible placement and flag it. ---
        var quitBtn = MakeAtlasButton(LoginLayout.QuitButton, "Quit", LoginLayout.AtlasLoginWindow);
        quitBtn.Pressed += OnQuitPressed;
        band.AddChild(quitBtn);
        widgetCount++;

        // --- Validation toast line (hidden until a field is empty on OK). Shows the CONFIRMED
        //     CP949 login error messages from msg.xdb (spec §5 ids 4025/4026). ---
        _toast = MakeLabel("", LoginLayout.FontLabelHeight + 2, new Color(0.95f, 0.45f, 0.40f));
        _toast.Position = new Vector2(LoginLayout.RefWidth / 2f - 260f, 230f);
        _toast.Size = new Vector2(520, 24);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        band.AddChild(_toast);
        widgetCount++;

        GD.Print($"[Screens] LoginScreen built ({widgetCount} widgets; " +
                 $"vfs={(_assets.HasVfs ? "real-atlas" : "offline-fallback")}).");
    }

    // ---------------------------------------------------------------------
    // Intent handlers (NO game logic — emit a signal the flow node consumes).
    // ---------------------------------------------------------------------

    private void OnOkPressed()
    {
        // OFFLINE STUB: accept any credentials locally. There is no credential validation against a
        // server, no packet send, no handshake here — that is all network/Application territory and
        // absent in this presentation-only flow.
        //
        // We DO reproduce the legacy empty-field guard, surfacing the CONFIRMED CP949 toasts from
        // msg.xdb (spec §5 ids 4025/4026), because those strings are spec-confirmed and faithful.
        // spec: Docs/RE/specs/ui_system.md §6.2 — login (state 1) → load/select (state 2 → 4).
        string account = _accountEdit.Text.Trim();
        if (account.Length == 0)
        {
            _toast.Text = _assets.Text(LoginLayout.MsgErrEmptyId, "Please enter an ID.");
            return;
        }

        if (_passwordEdit.Text.Length == 0)
        {
            _toast.Text = _assets.Text(LoginLayout.MsgErrEmptyPassword, "Please enter a password.");
            return;
        }

        _toast.Text = "";
        GD.Print($"[Screens] LoginScreen: OK pressed (offline stub) — account='{account}'.");
        EmitSignal(SignalName.LoginAccepted, account);
    }

    private void OnQuitPressed()
    {
        GD.Print("[Screens] LoginScreen: Quit pressed.");
        EmitSignal(SignalName.QuitRequested);
    }

    // ---------------------------------------------------------------------
    // Widget factories (shared with the placement helpers below).
    // ---------------------------------------------------------------------

    private LineEdit MakeTextbox(bool masked)
    {
        var edit = new LineEdit
        {
            // spec: §8.2 — masked password field; both accept CP949 Korean via Godot IME.
            Secret = masked,
            CaretBlink = true,
            // The real boxes are 13px tall — too short for a glyph; the legacy client draws
            // text over the box at the font height. We keep the recovered rect for the control
            // frame but allow the text to render at a readable height.
            Alignment = HorizontalAlignment.Left,
        };
        return edit;
    }

    /// <summary>
    /// Builds a button whose face is the recovered atlas sub-rect when the VFS is available,
    /// otherwise a themed text button at the same rect. spec §8.3 — 7-state buttons keep
    /// separate atlas regions; here we use the normal-state region (hover/pressed not recovered
    /// as distinct rects for every button — open item §7.1/§7.4).
    /// </summary>
    private Button MakeAtlasButton(WidgetRect rect, string caption, string atlasPath)
    {
        var btn = new Button { Text = caption };
        PlaceLocal(btn, rect, sizeFromRect: true);

        AtlasTexture? face = _assets.Slice(atlasPath, rect.SrcX, rect.SrcY, rect.W, rect.H);
        if (face is not null)
        {
            // Show the atlas sprite as the button's normal texture; keep the caption as an
            // overlay so the action is readable even if the sprite is a blank plate.
            btn.Icon = face;
            btn.ExpandIcon = true;
            btn.IconAlignment = HorizontalAlignment.Center;
            btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.80f));
        }

        return btn;
    }

    private Label MakeLabel(string text, int fontHeight, Color color)
    {
        var label = new Label { Text = text };
        // spec §4.2 — font slot pixel heights; we map height to Godot font_size.
        label.AddThemeFontSizeOverride("font_size", Mathf.Max(10, fontHeight));
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>Adds a small caption strip label at a recovered rect. Returns 1 (widget count).</summary>
    private int AddCaptionStrip(Control parent, WidgetRect rect, string text)
    {
        var label = MakeLabel(text, LoginLayout.FontLabelHeight, new Color(0.85f, 0.85f, 0.9f));
        PlaceLocal(label, rect, sizeFromRect: false);
        label.Size = new Vector2(rect.W + 24, rect.H + 6); // give CJK glyphs room above the 13px strip
        parent.AddChild(label);
        return 1;
    }

    /// <summary>
    /// Places a control at a recovered panel-local rect (spec §2.0 — x/y relative to parent).
    /// When <paramref name="sizeFromRect"/> is true the control is sized to the recovered w/h.
    /// </summary>
    private static void PlaceLocal(Control c, WidgetRect rect, bool sizeFromRect = true)
    {
        c.Position = new Vector2(rect.X, rect.Y);
        if (sizeFromRect)
            c.Size = new Vector2(rect.W, rect.H);
        c.CustomMinimumSize = new Vector2(rect.W, rect.H);
    }
}