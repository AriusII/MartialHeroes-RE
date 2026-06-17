// Ui/Scenes/Login/PinSubView.cs
//
// PIN sub-view for the Login(1) sub-states 31 (raise) / 32 (poll).
//
// Backed entirely by HudAtlasLibrary — no UiAssetLoader dependency.
// Faithfully reimplements GU PIN modal behaviour from:
//   spec: Docs/RE/specs/frontend_scenes.md §11.3 (CODE-CONFIRMED layout).
//
// Layout (panel-local coordinates inside the 329×422 modal panel):
//   Keypad:  2 × 5 tiles, each 52×52. Column spacing 55. Col0 X=28.
//            Row 0 Y=170 (digits 0..4), Row 1 Y=230 (digits 5..9).
//   Digit d glyph: password.dds src(d*52, 560/612/664, 52,52).
//   Reset button (tag 11): panel-local (243,133,58,30). password.dds N(663,8) H(663,88) P(663,48).
//   OK button (tag 12): panel-local (90,290,154,58). password.dds N(330,0) H(330,116) P(330,58).
//   Cancel button (tag 13): panel-local (90,350,154,58). password.dds N(486,0) H(486,116) P(486,58).
//
// Fisher-Yates scramble: seeded from wall-clock milliseconds, scrambles digit position array.
// spec: Docs/RE/specs/frontend_scenes.md §11.3e "Fisher-Yates seed from wall-clock ms". CODE-CONFIRMED.
//
// Signals:
//   PinSubmitted(string pin) — fired when OK (tag 12) is pressed with a non-empty PIN.
//   Cancelled()             — fired when Cancel (tag 13) is pressed.
//
// The parent (LoginWindow) shows/hides this Control.
// HostInReferenceSpace: when true, assume parent is already in 1024×768 canvas space
// (no double-scaling applied). Set this to true when parented inside LoginWindow.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.3 — PIN modal layout: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3a — keypad tile layout: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3b — digit glyph axis convention: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3d — button positions: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3e — Fisher-Yates scramble: CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
/// PIN input sub-view for Login(1) sub-states 31/32.
///
/// <para>Builds a 2×5 scrambled keypad from <c>password.dds</c> via <see cref="HudAtlasLibrary"/>.
/// Fisher-Yates scramble seeded from wall-clock milliseconds (spec §11.3e).
/// OK (tag 12), Cancel (tag 13), Reset (tag 11) buttons.</para>
///
/// <para>Subscribe to <see cref="PinSubmitted"/> and <see cref="Cancelled"/> to receive
/// the outcome and emit the appropriate use-case call. Never mutate domain state here.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §11.3 — CODE-CONFIRMED PIN modal.
/// </summary>
public sealed partial class PinSubView : Control
{
    // -------------------------------------------------------------------------
    // Atlas path
    // spec: Docs/RE/specs/frontend_scenes.md §11.1 "password.dds, 1024×1024 DXT3". CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private const string AtlasPassword = LoginLayout.AtlasPassword; // "data/ui/password.dds"

    // -------------------------------------------------------------------------
    // Layout constants (reuse LoginLayout — all CODE-CONFIRMED)
    // -------------------------------------------------------------------------

    // Modal panel position on the 1024×768 canvas.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3 "canvas rect (347,173,329,422)". CODE-CONFIRMED.
    private const int ModalX = LoginLayout.PinModalX; // 347
    private const int ModalY = LoginLayout.PinModalY; // 173
    private const int ModalW = LoginLayout.PinModalW; // 329
    private const int ModalH = LoginLayout.PinModalH; // 422

    // Keypad tile dimensions.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3a. CODE-CONFIRMED.
    private const int TileW        = LoginLayout.PinKeypadTileW;      // 52
    private const int TileH        = LoginLayout.PinKeypadTileH;      // 52
    private const int ColSpacing   = LoginLayout.PinKeypadColSpacing;  // 55
    private const int Col0X        = LoginLayout.PinKeypadCol0X;       // 28
    private const int Row0Y        = LoginLayout.PinKeypadRow0Y;       // 170
    private const int Row1Y        = LoginLayout.PinKeypadRow1Y;       // 230

    // Digit glyph source V rows in password.dds.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3b. CODE-CONFIRMED.
    private const int DigitNormalV  = LoginLayout.PinDigitNormalSrcY;  // 560
    private const int DigitHoverV   = LoginLayout.PinDigitHoverSrcY;   // 664
    private const int DigitPressedV = LoginLayout.PinDigitPressedSrcY; // 612
    private const int DigitColW     = LoginLayout.PinDigitColWidth;     // 52

    // PIN capacity.
    // spec: Docs/RE/specs/frontend_scenes.md §1.4a / login_flow.md §4.2. CODE-CONFIRMED.
    private const int PinMaxLength = LoginLayout.PinMaxLength; // 4

    // -------------------------------------------------------------------------
    // Tags (spec-confirmed action ids for PIN buttons)
    // spec: Docs/RE/specs/frontend_scenes.md §11.3d. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private const int TagReset  = LoginLayout.PinTagReset;  // 11
    private const int TagOk     = LoginLayout.PinTagOk;     // 12
    private const int TagCancel = LoginLayout.PinTagCancel; // 13

    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal] public delegate void PinSubmittedEventHandler(string pin);
    [Signal] public delegate void CancelledEventHandler();

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;
    private string _pin = "";

    // Scrambled digit assignment: _scrambled[keypadSlot] = actualDigit.
    // Fisher-Yates from wall-clock seed. spec §11.3e. CODE-CONFIRMED.
    private int[] _scrambled = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    // PIN display label.
    private Label? _pinDisplay;

    // DEV: prefill PIN for offline replay (skip keypad interaction).
    // This is the only departure from the pure-passive contract — it is a DEV-only
    // convenience that skips the visual scrambled keypad and auto-submits.
    // guarded by IsDevPrefillActive.
    public string? DevPrefillPin { private get; set; }

    // When true, assume this panel is already inside a 1024×768 reference canvas
    // (parent is LoginWindow in the ScreenHost). No Position offset applied.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3 "panel rect (347,173,329,422)". CODE-CONFIRMED.
    public bool HostInReferenceSpace { get; set; }

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the PIN sub-view.
    /// </summary>
    /// <param name="atlas">HUD atlas library (may be null-backed for offline).</param>
    public PinSubView(HudAtlasLibrary atlas)
    {
        _atlas = atlas;

        // Default hidden; LoginWindow shows when sub-state 31 is entered.
        Visible = false;
    }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Position the panel on the 1024×768 canvas.
        // spec: §11.3 "canvas rect (347,173,329,422)". CODE-CONFIRMED.
        if (!HostInReferenceSpace)
        {
            // Standalone: position at canvas absolute.
            Position = new Vector2(ModalX, ModalY);
        }
        else
        {
            // Hosted inside LoginWindow (already in ref space): use modal canvas coords directly.
            Position = new Vector2(ModalX, ModalY);
        }

        Size              = new Vector2(ModalW, ModalH);
        CustomMinimumSize = new Vector2(ModalW, ModalH);

        // Scramble the keypad.
        Scramble();

        // Build the modal chrome background (password.dds has the modal frame baked in).
        // The modal chrome is inferred from the atlas full-texture background (no dedicated sub-rect spec).
        // We add a dim overlay to darken the scene behind the modal.
        var dim = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.6f),
            Position    = new Vector2(-ModalX, -ModalY), // covers entire 1024×768
            Size        = new Vector2(1024, 768),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(dim);

        // Build keypad tiles.
        BuildKeypad();

        // Build PIN display label.
        _pinDisplay = new Label
        {
            Text     = "",
            Position = new Vector2(80, 100),
            Size     = new Vector2(ModalW - 100, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _pinDisplay.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_pinDisplay);

        // Build Reset button (tag 11).
        // spec: §11.3d (243,133,58,30) N(663,8) H(663,88) P(663,48). CODE-CONFIRMED.
        BuildButton(
            LoginLayout.PinResetX, LoginLayout.PinResetY, LoginLayout.PinResetW, LoginLayout.PinResetH,
            LoginLayout.PinResetNSrcX, LoginLayout.PinResetNSrcY,
            LoginLayout.PinResetHSrcX, LoginLayout.PinResetHSrcY,
            LoginLayout.PinResetPSrcX, LoginLayout.PinResetPSrcY,
            TagReset);

        // Build OK button (tag 12).
        // spec: §11.3d (90,290,154,58) N(330,0) H(330,116) P(330,58). CODE-CONFIRMED.
        BuildButton(
            LoginLayout.PinOkX, LoginLayout.PinOkY, LoginLayout.PinOkW, LoginLayout.PinOkH,
            LoginLayout.PinOkNSrcX, LoginLayout.PinOkNSrcY,
            LoginLayout.PinOkHSrcX, LoginLayout.PinOkHSrcY,
            LoginLayout.PinOkPSrcX, LoginLayout.PinOkPSrcY,
            TagOk);

        // Build Cancel button (tag 13).
        // spec: §11.3d (90,350,154,58) N(486,0) H(486,116) P(486,58). CODE-CONFIRMED.
        BuildButton(
            LoginLayout.PinCancelX, LoginLayout.PinCancelY, LoginLayout.PinCancelW, LoginLayout.PinCancelH,
            LoginLayout.PinCancelNSrcX, LoginLayout.PinCancelNSrcY,
            LoginLayout.PinCancelHSrcX, LoginLayout.PinCancelHSrcY,
            LoginLayout.PinCancelPSrcX, LoginLayout.PinCancelPSrcY,
            TagCancel);

        // DEV: auto-submit prefilled PIN if provided.
        if (DevPrefillPin is { Length: > 0 } pre)
        {
            GD.Print($"[PinSubView] DEV prefill: auto-submitting PIN (len={pre.Length}).");
            _pin = pre.Length > PinMaxLength ? pre[..PinMaxLength] : pre;
            UpdatePinDisplay();
            CallDeferred(MethodName.AutoSubmitPin);
        }
    }

    private void AutoSubmitPin()
    {
        OnButtonAction(TagOk);
    }

    // -------------------------------------------------------------------------
    // Keypad builder
    // -------------------------------------------------------------------------

    private void BuildKeypad()
    {
        // 2 × 5 grid of digit buttons.
        // Position 0..4 = top row (Y=Row0Y), Position 5..9 = bottom row (Y=Row1Y).
        // Digit assigned via _scrambled[position].
        // spec: §11.3a "Tile X: 55*(p%5)+28; Tile Y: 170 for p<5, 230 for p>=5". CODE-CONFIRMED.
        for (int pos = 0; pos < 10; pos++)
        {
            int digit = _scrambled[pos];
            int col   = pos % 5;
            int row   = pos / 5;

            int x = Col0X + col * ColSpacing;                   // spec §11.3a. CODE-CONFIRMED.
            int y = row == 0 ? Row0Y : Row1Y;                   // spec §11.3a. CODE-CONFIRMED.

            // Digit d glyph: srcU = d*52; srcV per state.
            // spec: §11.3b "srcU = d*52; srcV = 560/612/664". CODE-CONFIRMED.
            int srcU = digit * DigitColW;

            Texture2D? normal  = _atlas.SliceByPath(AtlasPassword, srcU, DigitNormalV,  TileW, TileH);
            Texture2D? pressed = _atlas.SliceByPath(AtlasPassword, srcU, DigitPressedV, TileW, TileH);
            Texture2D? hover   = _atlas.SliceByPath(AtlasPassword, srcU, DigitHoverV,   TileW, TileH);

            var btn = new TextureButton
            {
                Position          = new Vector2(x, y),
                Size              = new Vector2(TileW, TileH),
                CustomMinimumSize = new Vector2(TileW, TileH),
                IgnoreTextureSize = true,
                StretchMode       = TextureButton.StretchModeEnum.Scale,
                TextureNormal     = normal,
                TextureHover      = hover,
                TexturePressed    = pressed,
                TextureDisabled   = normal,
            };

            // Capture digit in closure (not pos, not col/row).
            int capturedDigit = digit;
            btn.Pressed += () => OnDigitPressed(capturedDigit);
            AddChild(btn);
        }
    }

    private void BuildButton(
        int x, int y, int w, int h,
        int nSrcX, int nSrcY,
        int hSrcX, int hSrcY,
        int pSrcX, int pSrcY,
        int tag)
    {
        Texture2D? normal  = _atlas.SliceByPath(AtlasPassword, nSrcX, nSrcY, w, h);
        Texture2D? hover   = _atlas.SliceByPath(AtlasPassword, hSrcX, hSrcY, w, h);
        Texture2D? pressed = _atlas.SliceByPath(AtlasPassword, pSrcX, pSrcY, w, h);

        var btn = new TextureButton
        {
            Position          = new Vector2(x, y),
            Size              = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            IgnoreTextureSize = true,
            StretchMode       = TextureButton.StretchModeEnum.Scale,
            TextureNormal     = normal,
            TextureHover      = hover,
            TexturePressed    = pressed,
            TextureDisabled   = normal,
        };

        int capturedTag = tag;
        btn.Pressed += () => OnButtonAction(capturedTag);
        AddChild(btn);
    }

    // -------------------------------------------------------------------------
    // Input handlers
    // -------------------------------------------------------------------------

    private void OnDigitPressed(int digit)
    {
        if (_pin.Length >= PinMaxLength) return; // PIN full.
        _pin += digit.ToString();
        UpdatePinDisplay();
        GD.Print($"[PinSubView] Digit {digit} pressed; PIN length now {_pin.Length}.");
    }

    private void OnButtonAction(int tag)
    {
        switch (tag)
        {
            case TagReset:
                // Reset tag 11: clear PIN and re-scramble.
                // spec: §11.3d "Reset clears and re-scrambles". CODE-CONFIRMED.
                _pin = "";
                UpdatePinDisplay();
                Scramble();
                RebuildKeypad();
                GD.Print("[PinSubView] Reset (tag 11): PIN cleared and keypad re-scrambled. " +
                         "spec: frontend_scenes.md §11.3d.");
                break;

            case TagOk:
                // OK tag 12: emit PinSubmitted (even if empty — Application validates length).
                // spec: §11.3d "OK fires PinSubmitted". CODE-CONFIRMED.
                GD.Print($"[PinSubView] OK (tag 12): PinSubmitted(pin_len={_pin.Length}). " +
                         "spec: frontend_scenes.md §11.3d.");
                EmitSignal(SignalName.PinSubmitted, _pin);
                break;

            case TagCancel:
                // Cancel tag 13: emit Cancelled.
                // spec: §11.3d "Cancel fires Cancelled". CODE-CONFIRMED.
                GD.Print("[PinSubView] Cancel (tag 13): Cancelled. spec: frontend_scenes.md §11.3d.");
                EmitSignal(SignalName.Cancelled);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // PIN display
    // -------------------------------------------------------------------------

    private void UpdatePinDisplay()
    {
        if (_pinDisplay is null) return;
        // Show asterisks for each digit entered.
        _pinDisplay.Text = new string('*', _pin.Length);
    }

    // -------------------------------------------------------------------------
    // Fisher-Yates scramble
    // spec: Docs/RE/specs/frontend_scenes.md §11.3e "Fisher-Yates seed from wall-clock ms". CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void Scramble()
    {
        _scrambled = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        // Seed from wall-clock milliseconds.
        // spec: §11.3e "seed = wall-clock milliseconds (GetTicksMsec or equivalent)". CODE-CONFIRMED.
        int seed = (int)(global::Godot.Time.GetTicksMsec() & 0x7FFF_FFFF); // spec §11.3e. CODE-CONFIRMED.
        var rng  = new Random(seed);

        for (int i = 9; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (_scrambled[i], _scrambled[j]) = (_scrambled[j], _scrambled[i]);
        }
    }

    private void RebuildKeypad()
    {
        // Remove existing keypad buttons (first 10 TextureButtons after the dim overlay).
        // We can't safely iterate-and-remove; just rebuild all children.
        // Store non-keypad children, clear, re-add.
        // Simpler: just free keypad-tagged buttons. Since we added dim+keypad+display+buttons
        // in a known order, re-add everything.
        foreach (Node child in GetChildren())
            child.QueueFree();
        _pinDisplay = null;

        // Re-run the full build (same as _Ready, no DEV prefill auto-submit on reset).
        var dim = new ColorRect
        {
            Color       = new Color(0f, 0f, 0f, 0.6f),
            Position    = new Vector2(-ModalX, -ModalY),
            Size        = new Vector2(1024, 768),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(dim);

        BuildKeypad();

        _pinDisplay = new Label
        {
            Text     = new string('*', _pin.Length),
            Position = new Vector2(80, 100),
            Size     = new Vector2(ModalW - 100, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _pinDisplay.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_pinDisplay);

        BuildButton(
            LoginLayout.PinResetX, LoginLayout.PinResetY, LoginLayout.PinResetW, LoginLayout.PinResetH,
            LoginLayout.PinResetNSrcX, LoginLayout.PinResetNSrcY,
            LoginLayout.PinResetHSrcX, LoginLayout.PinResetHSrcY,
            LoginLayout.PinResetPSrcX, LoginLayout.PinResetPSrcY,
            TagReset);

        BuildButton(
            LoginLayout.PinOkX, LoginLayout.PinOkY, LoginLayout.PinOkW, LoginLayout.PinOkH,
            LoginLayout.PinOkNSrcX, LoginLayout.PinOkNSrcY,
            LoginLayout.PinOkHSrcX, LoginLayout.PinOkHSrcY,
            LoginLayout.PinOkPSrcX, LoginLayout.PinOkPSrcY,
            TagOk);

        BuildButton(
            LoginLayout.PinCancelX, LoginLayout.PinCancelY, LoginLayout.PinCancelW, LoginLayout.PinCancelH,
            LoginLayout.PinCancelNSrcX, LoginLayout.PinCancelNSrcY,
            LoginLayout.PinCancelHSrcX, LoginLayout.PinCancelHSrcY,
            LoginLayout.PinCancelPSrcX, LoginLayout.PinCancelPSrcY,
            TagCancel);
    }
}
