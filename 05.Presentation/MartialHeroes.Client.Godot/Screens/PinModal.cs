// Screens/PinModal.cs — FROM-SCRATCH rewrite, WAVE 3.
//
// Second-password / PIN modal. Every visual element sources from a real VFS atlas sub-rect.
// No solid-colour fallbacks, no invented text, no synthetic data.
// Missing atlas → GD.PrintErr + skip (no crash).
//
// LAYOUT — spec: Docs/RE/specs/frontend_scenes.md §11.3 (CODE-CONFIRMED)
//
//   Modal panel rect (canvas-absolute): (347, 173, 329, 422).
//   Panel origin = (347, 173); every child rect below is panel-local.
//
//   Background chrome (§11.3 / §11.1a-1):
//     Atlas: InventWindow.dds  src(318, 647, 340, 190)
//     Rendered as NinePatch stretched to 329×422.
//     spec: Docs/RE/specs/frontend_scenes.md §11.3. CODE-CONFIRMED.
//     spec: Docs/RE/specs/frontend_scenes.md §11.1a-1 "PIN dragon-frame = shared notice frame". CODE-CONFIRMED.
//
//   Masked-PIN echo label (§11.3 "Masked-PIN echo label"):
//     Panel-local (81, 138, 150, 22). Passive label, no atlas source.
//     Displays one '*' per entered digit (cap 4), no runtime text lookup.
//     spec: Docs/RE/specs/frontend_scenes.md §11.3. CODE-CONFIRMED.
//
//   Keypad: 2 rows × 5 columns, 100 overlapping buttons.
//   Tile X (panel-local): 55*(p%5)+28 → columns 28, 83, 138, 193, 248.
//   Tile Y (panel-local): 170 (top row p<5), 230 (bottom row p≥5). Tile size: 52×52.
//   Atlas: password.dds. Digit d: srcU = d*52, srcV = 560 (normal) / 612 (pressed) / 664 (hover).
//   spec: Docs/RE/specs/frontend_scenes.md §11.3a, §11.3b. CODE-CONFIRMED.
//
//   Scramble: Fisher-Yates of [0..9] seeded from wall-clock time on every open and every Reset.
//   spec: Docs/RE/specs/frontend_scenes.md §11.3c. CODE-CONFIRMED.
//
//   Reset  (tag 11): panel-local (243,133,58,30). password.dds N(663,8) H(663,88) P(663,48).
//   OK     (tag 12): panel-local (90,290,154,58). password.dds N(330,0) H(330,116) P(330,58).
//   Cancel (tag 13): panel-local (90,350,154,58). password.dds N(486,0) H(486,116) P(486,58).
//   spec: Docs/RE/specs/frontend_scenes.md §11.3d. CODE-CONFIRMED.
//
//   No runtime text — every baked caption (title, warning line, button faces) is atlas art.
//   No msg.xdb caption id exists for the PIN warning line. spec §11.3. CODE-CONFIRMED.
//
// FLOW — spec: Docs/RE/specs/frontend_scenes.md §1.4a. RUNTIME-CONFIRMED.
//   Shown after primary login validation (sub-state 29), before the TAB-string handoff (40).
//   PIN ≤ 4 chars. spec: Docs/RE/specs/login_flow.md §4.2. RUNTIME-CONFIRMED.
//
// PASSIVE: zero game logic. View state only: _pin string, scramble permutation, node handles.

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Second-password / PIN modal — pixel-faithful to §11.3.
/// Emits <see cref="PinSubmitted"/> (≤ 4 chars) on OK.
/// Emits <see cref="Cancelled"/> on Cancel.
/// </summary>
public sealed partial class PinModal : Control
{
    // PIN capacity: ≤ 4 chars. spec: Docs/RE/specs/login_flow.md §4.2. RUNTIME-CONFIRMED.
    private const int MaxPinLength = LoginLayout.PinMaxLength; // 4

    // Nine-patch corner margins for the InventWindow.dds dragon-frame quad.
    // The source rect is 340×190; a 20-px corner margin preserves the stone/ring edge chrome
    // without distorting the baked art when the quad is stretched to 329×422.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3 "NinePatch … from (318,647,340,190)
    //   up to 347,173,329,422". CODE-CONFIRMED.
    private const int NinePatchMargin = 20;

    // =========================================================================
    // Public signals — consumed by BootFlow.cs. Must not be renamed.
    // =========================================================================

    /// <summary>PIN submitted. Carries the entered PIN string (≤ 4 chars; may be empty = skip).</summary>
    [Signal]
    public delegate void PinSubmittedEventHandler(string pin);

    /// <summary>Raised when Cancel is clicked.</summary>
    [Signal]
    public delegate void CancelledEventHandler();

    // =========================================================================
    // View state (never domain state)
    // =========================================================================

    /// <summary>Currently entered PIN (view state only). Never leaves this class.</summary>
    private string _pin = "";

    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    // Scramble permutation: perm[p] = digit shown at keypad position p.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3c. CODE-CONFIRMED.
    private readonly int[] _perm = new int[10];

    // 100 digit buttons [position*10 + digitValue]. Exactly one per position is Visible.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3b. CODE-CONFIRMED.
    private readonly TextureButton?[] _digitBtns = new TextureButton?[100];

    // Masked-PIN echo label. spec §11.3 "Masked-PIN echo label (81,138,150,22)". CODE-CONFIRMED.
    private Label _pinDisplay = null!;

    // =========================================================================
    // Injection point — consumed by BootFlow.cs. Must not be renamed.
    // =========================================================================

    /// <summary>Inject a shared UiAssetLoader (avoids double-loading DDS atlases).</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        // Produce the initial scramble before UI is built so _perm is populated.
        // spec: Docs/RE/specs/frontend_scenes.md §11.3c. CODE-CONFIRMED.
        Scramble();

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PinModal] _Ready: BuildUi threw — {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildUi()
    {
        // Full-canvas blocker — stops clicks from passing through to the login form.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Modal panel — canvas-absolute position (347,173), size (329,422).
        // spec: Docs/RE/specs/frontend_scenes.md §11.3 "Modal panel rect: 347,173,329,422".
        // CODE-CONFIRMED.
        var panel = new Control
        {
            Name = "PinPanel",
            Position = new Vector2(LoginLayout.PinModalX, LoginLayout.PinModalY),
            Size = new Vector2(LoginLayout.PinModalW, LoginLayout.PinModalH),
        };
        AddChild(panel);

        // -------------------------------------------------------------------
        // Background: InventWindow.dds NinePatch src(318,647,340,190).
        // The source dragon-frame art is 340×190 but the modal panel is 329×422;
        // NinePatch stretches the centre while preserving the border chrome.
        // spec: Docs/RE/specs/frontend_scenes.md §11.3. CODE-CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §11.1a-1 "PIN dragon-frame = shared notice/error
        //   frame; InventWindow.dds src(318,647) size 340×190". CODE-CONFIRMED.
        // -------------------------------------------------------------------
        var atlasInventWindow = _assets.LoadAtlas(LoginLayout.AtlasInventWindow);
        if (atlasInventWindow is not null)
        {
            var np = new NinePatchRect
            {
                Name = "PinFrame",
                Texture = atlasInventWindow,
                // Source region: the dragon-frame sub-rect within the 1024×1024 atlas.
                // spec: Docs/RE/specs/frontend_scenes.md §11.3 src(318,647,340,190). CODE-CONFIRMED.
                RegionRect = new Rect2(
                    LoginLayout.ModalChromeSrcX, // 318. spec §11.3. CODE-CONFIRMED.
                    LoginLayout.ModalChromeSrcY, // 647. spec §11.3. CODE-CONFIRMED.
                    LoginLayout.ModalChromeW, // 340. spec §11.3. CODE-CONFIRMED.
                    LoginLayout.ModalChromeH), // 190. spec §11.3. CODE-CONFIRMED.
                PatchMarginTop = NinePatchMargin,
                PatchMarginLeft = NinePatchMargin,
                PatchMarginRight = NinePatchMargin,
                PatchMarginBottom = NinePatchMargin,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            np.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            panel.AddChild(np);

            GD.Print("[PinModal] Dragon-frame NinePatch loaded from InventWindow.dds " +
                     "src(318,647,340,190) → NinePatch 329×422. " +
                     "spec: frontend_scenes.md §11.3 / §11.1a-1. CODE-CONFIRMED.");
        }
        else
        {
            // Missing atlas: log and skip — no solid-colour fallback.
            // spec requirement: "NO FALLBACK (missing → log+skip, no crash)".
            GD.PrintErr("[PinModal] InventWindow.dds not found — dragon-frame background absent " +
                        "(VFS offline). spec: frontend_scenes.md §11.3. CODE-CONFIRMED path.");
        }

        // -------------------------------------------------------------------
        // Masked-PIN echo label (§11.3 "Masked-PIN echo label").
        // Panel-local (81, 138, 150, 22). Passive — no atlas source, no caption id.
        // Displays one '*' per entered digit (up to the 4-char cap).
        // spec: Docs/RE/specs/frontend_scenes.md §11.3. CODE-CONFIRMED.
        // -------------------------------------------------------------------
        _pinDisplay = new Label
        {
            Name = "PinDisplay",
            Position = new Vector2(81, 138), // panel-local. spec §11.3. CODE-CONFIRMED.
            Size = new Vector2(150, 22), // panel-local. spec §11.3. CODE-CONFIRMED.
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        // spec §11.3 "entered PIN … shown as a masked *-per-digit string". CODE-CONFIRMED.
        _pinDisplay.AddThemeFontSizeOverride("font_size", 20);
        _pinDisplay.AddThemeColorOverride("font_color", Colors.White);
        _pinDisplay.Text = ""; // initially empty
        panel.AddChild(_pinDisplay);

        // -------------------------------------------------------------------
        // Keypad — 100 overlapping TextureButton widgets (10 positions × 10 digit values).
        // spec: Docs/RE/specs/frontend_scenes.md §11.3a / §11.3b. CODE-CONFIRMED.
        // -------------------------------------------------------------------
        BuildKeypad(panel);

        // -------------------------------------------------------------------
        // Reset button — tag 11, panel-local (243,133,58,30).
        // password.dds N(663,8) H(663,88) P(663,48).
        // spec: Docs/RE/specs/frontend_scenes.md §11.3d. CODE-CONFIRMED.
        // -------------------------------------------------------------------
        BuildPinButton(panel,
            LoginLayout.PinResetX, LoginLayout.PinResetY,
            LoginLayout.PinResetW, LoginLayout.PinResetH,
            LoginLayout.PinResetNSrcX, LoginLayout.PinResetNSrcY,
            LoginLayout.PinResetHSrcX, LoginLayout.PinResetHSrcY,
            LoginLayout.PinResetPSrcX, LoginLayout.PinResetPSrcY,
            "ResetBtn", OnReset);

        // -------------------------------------------------------------------
        // OK button — tag 12, panel-local (90,290,154,58).
        // password.dds N(330,0) H(330,116) P(330,58).
        // spec: Docs/RE/specs/frontend_scenes.md §11.3d. CODE-CONFIRMED.
        // -------------------------------------------------------------------
        BuildPinButton(panel,
            LoginLayout.PinOkX, LoginLayout.PinOkY,
            LoginLayout.PinOkW, LoginLayout.PinOkH,
            LoginLayout.PinOkNSrcX, LoginLayout.PinOkNSrcY,
            LoginLayout.PinOkHSrcX, LoginLayout.PinOkHSrcY,
            LoginLayout.PinOkPSrcX, LoginLayout.PinOkPSrcY,
            "OkBtn", OnOk);

        // -------------------------------------------------------------------
        // Cancel button — tag 13, panel-local (90,350,154,58).
        // password.dds N(486,0) H(486,116) P(486,58).
        // spec: Docs/RE/specs/frontend_scenes.md §11.3d. CODE-CONFIRMED.
        // -------------------------------------------------------------------
        BuildPinButton(panel,
            LoginLayout.PinCancelX, LoginLayout.PinCancelY,
            LoginLayout.PinCancelW, LoginLayout.PinCancelH,
            LoginLayout.PinCancelNSrcX, LoginLayout.PinCancelNSrcY,
            LoginLayout.PinCancelHSrcX, LoginLayout.PinCancelHSrcY,
            LoginLayout.PinCancelPSrcX, LoginLayout.PinCancelPSrcY,
            "CancelBtn", OnCancel);

        // Apply the initial scramble to show the correct digit per position.
        ApplyScramble();

        GD.Print("[PinModal] Built. Keypad 2×5 scrambled (Fisher-Yates, wall-clock seed). " +
                 "PIN cap=4. All art from password.dds / InventWindow.dds. " +
                 "spec: frontend_scenes.md §11.3. CODE-CONFIRMED.");
    }

    // =========================================================================
    // Keypad construction (§11.3a / §11.3b). CODE-CONFIRMED.
    // =========================================================================

    private void BuildKeypad(Control parent)
    {
        // For each position p (0..9):
        //   Tile X (panel-local): 55*(p%5)+28. spec §11.3a. CODE-CONFIRMED.
        //   Tile Y (panel-local): 170 (p<5) / 230 (p≥5). spec §11.3a. CODE-CONFIRMED.
        //   Build 10 overlapping 52×52 TextureButtons (one per digit value 0..9).
        //   Exactly one is shown (the one whose digit value == perm[p]). spec §11.3b. CODE-CONFIRMED.
        for (int p = 0; p < 10; p++)
        {
            int tileX = LoginLayout.PinKeypadColSpacing * (p % 5) + LoginLayout.PinKeypadCol0X;
            // spec: Docs/RE/specs/frontend_scenes.md §11.3a. CODE-CONFIRMED.
            int tileY = p < 5 ? LoginLayout.PinKeypadRow0Y : LoginLayout.PinKeypadRow1Y;

            for (int d = 0; d < 10; d++)
            {
                // Digit d: srcU = d*52. spec §11.3b "digit d varies along U/X". CODE-CONFIRMED.
                int srcU = d * LoginLayout.PinDigitColWidth;

                // Slice normal / hover / pressed from password.dds.
                // spec: Docs/RE/specs/frontend_scenes.md §11.3b. CODE-CONFIRMED.
                AtlasTexture? normalTex = _assets.Slice(
                    LoginLayout.AtlasPassword,
                    srcU, LoginLayout.PinDigitNormalSrcY, // srcV=560 (normal). spec §11.3b. CODE-CONFIRMED.
                    LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH);
                AtlasTexture? hoverTex = _assets.Slice(
                    LoginLayout.AtlasPassword,
                    srcU, LoginLayout.PinDigitHoverSrcY, // srcV=664 (hover). spec §11.3b. CODE-CONFIRMED.
                    LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH);
                AtlasTexture? pressedTex = _assets.Slice(
                    LoginLayout.AtlasPassword,
                    srcU, LoginLayout.PinDigitPressedSrcY, // srcV=612 (pressed). spec §11.3b. CODE-CONFIRMED.
                    LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH);

                if (normalTex is null)
                {
                    // Atlas not available — log once for position 0, digit 0; skip the rest silently.
                    if (p == 0 && d == 0)
                        GD.PrintErr("[PinModal] password.dds not found — keypad tiles absent (VFS offline). " +
                                    "spec: frontend_scenes.md §11.3b. CODE-CONFIRMED path.");
                    _digitBtns[p * 10 + d] = null;
                    continue;
                }

                var btn = new TextureButton
                {
                    Name = $"Dig_p{p}_d{d}",
                    Position = new Vector2(tileX, tileY),
                    CustomMinimumSize = new Vector2(LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH),
                    TextureNormal = normalTex,
                    Visible = false, // ApplyScramble sets exactly one visible per position
                };
                if (hoverTex is not null) btn.TextureHover = hoverTex;
                if (pressedTex is not null) btn.TexturePressed = pressedTex;

                int captureD = d;
                btn.Pressed += () => OnDigitPressed(captureD);

                parent.AddChild(btn);
                _digitBtns[p * 10 + d] = btn;
            }
        }
    }

    /// <summary>
    /// Builds one three-state button from password.dds sub-rects.
    /// Missing atlas → GD.PrintErr + skip (no crash, no solid-colour fallback).
    /// </summary>
    private void BuildPinButton(Control parent,
        int x, int y, int w, int h,
        int nSrcX, int nSrcY,
        int hSrcX, int hSrcY,
        int pSrcX, int pSrcY,
        string name, Action onPress)
    {
        AtlasTexture? nTex = _assets.Slice(LoginLayout.AtlasPassword, nSrcX, nSrcY, w, h);
        AtlasTexture? hTex = _assets.Slice(LoginLayout.AtlasPassword, hSrcX, hSrcY, w, h);
        AtlasTexture? pTex = _assets.Slice(LoginLayout.AtlasPassword, pSrcX, pSrcY, w, h);

        if (nTex is null)
        {
            GD.PrintErr($"[PinModal] password.dds slice for {name} at ({nSrcX},{nSrcY},{w},{h}) returned null " +
                        "— button absent (VFS offline). spec: frontend_scenes.md §11.3d. CODE-CONFIRMED path.");
            return; // skip — no solid-colour fallback
        }

        var btn = new TextureButton
        {
            Name = name,
            Position = new Vector2(x, y),
            CustomMinimumSize = new Vector2(w, h),
            TextureNormal = nTex,
        };
        if (hTex is not null) btn.TextureHover = hTex;
        if (pTex is not null) btn.TexturePressed = pTex;

        btn.Pressed += () => onPress();
        parent.AddChild(btn);
    }

    // =========================================================================
    // Scramble — Fisher-Yates of [0..9] seeded from wall-clock time.
    // spec: Docs/RE/specs/frontend_scenes.md §11.3c. CODE-CONFIRMED.
    // Called on modal open and on every Reset.
    // =========================================================================

    private void Scramble()
    {
        // Seed from wall-clock time. spec §11.3c "seeded from current local wall-clock time".
        // CODE-CONFIRMED.
        var rng = new Random((int)(global::Godot.Time.GetTicksMsec() & 0x7FFF_FFFF));

        // Initialise identity permutation.
        for (int i = 0; i < 10; i++) _perm[i] = i;

        // Fisher-Yates shuffle. spec §11.3c. CODE-CONFIRMED.
        for (int i = 9; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (_perm[i], _perm[j]) = (_perm[j], _perm[i]);
        }
    }

    /// <summary>
    /// Shows exactly one digit button per position (the one matching perm[p]); hides all others.
    /// spec: Docs/RE/specs/frontend_scenes.md §11.3b / §11.3c. CODE-CONFIRMED.
    /// </summary>
    private void ApplyScramble()
    {
        for (int p = 0; p < 10; p++)
        {
            int visibleDigit = _perm[p];
            for (int d = 0; d < 10; d++)
            {
                TextureButton? btn = _digitBtns[p * 10 + d];
                if (btn is null) continue;
                btn.Visible = d == visibleDigit;
            }
        }
    }

    // =========================================================================
    // Intent handlers
    // =========================================================================

    private void OnDigitPressed(int digit)
    {
        // Append digit to PIN (cap at 4). spec §11.3d "append digit (cap 4)". CODE-CONFIRMED.
        if (_pin.Length >= MaxPinLength) return;
        _pin += digit.ToString();
        RefreshPinDisplay();
    }

    private void OnReset()
    {
        // Clear PIN and re-scramble. spec §11.3d "re-run scramble". CODE-CONFIRMED.
        _pin = "";
        RefreshPinDisplay();
        Scramble();
        ApplyScramble();
        GD.Print("[PinModal] Reset — PIN cleared, keypad re-scrambled. spec §11.3d. CODE-CONFIRMED.");
    }

    private void OnOk()
    {
        // Submit PIN (may be empty — optional field). spec §11.3d / §1.4a. CODE-CONFIRMED.
        GD.Print($"[PinModal] OK — submitting PIN (len={_pin.Length}). spec §11.3d. CODE-CONFIRMED.");
        EmitSignal(SignalName.PinSubmitted, _pin);
    }

    private void OnCancel()
    {
        // spec §11.3d "Cancel: close / abort modal". CODE-CONFIRMED.
        GD.Print("[PinModal] Cancel — emitting Cancelled. spec §11.3d. CODE-CONFIRMED.");
        EmitSignal(SignalName.Cancelled);
    }

    private void RefreshPinDisplay()
    {
        // Show one '*' per entered digit. spec §11.3 "shown as a masked *-per-digit string".
        // CODE-CONFIRMED. No padding glyph — the spec does not describe a placeholder.
        _pinDisplay.Text = new string('*', _pin.Length);
    }
}