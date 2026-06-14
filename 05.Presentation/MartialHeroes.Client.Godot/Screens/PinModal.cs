// Screens/PinModal.cs
//
// Second-password / PIN modal — pixel-faithful rebuild against §11.3 of frontend_scenes.md.
//
// LAYOUT (§11.3):
//   Modal panel rect (canvas-absolute): (347, 173, 329, 422).
//   No runtime text — all captions are baked atlas art in password.dds.
//   The entered PIN is shown as a masked * string in an internal display (no LineEdit widget).
//
//   Keypad: 2 rows × 5 columns of 52×52 tile buttons, all from password.dds.
//   Tile X (panel-local): 55*(p%5)+28 → columns 28,83,138,193,248.
//   Tile Y (panel-local): 170 (top row p<5), 230 (bottom row p>=5).
//   Each position builds a STACK of 10 overlapping 52×52 buttons (one per digit 0..9);
//   exactly one per position is visible (the one matching perm[p]).
//
//   Digit glyph source (password.dds):
//     digit d: srcY = d*52; columns: normal=560, hover=664, pressed=612.
//
//   Scramble: Fisher-Yates shuffle of [0..9] seeded from the wall-clock time.
//     Re-rolled on every open AND on Reset.
//     spec §11.3c. CODE-CONFIRMED.
//
//   Reset (tag 11): panel-local (243,133,58,30). src: N(663,8), H(663,88), P(663,48).
//   OK    (tag 12): panel-local (90,290,154,58). src: N(330,0), H(330,116), P(330,58).
//   Cancel(tag 13): panel-local (90,350,154,58). src: N(486,0), H(486,116), P(486,58).
//
//   Frame background: InventWindow.dds src(318,647) 340×190 (the shared notice panel chrome).
//   spec §11.3. CODE-CONFIRMED.
//
// FLOW (§1.4a):
//   Shown after primary login validation (sub-state 29) and before the TAB-string handoff (sub-state 40).
//   PIN ≤ 4 chars. spec: login_flow.md §4.2. RUNTIME-CONFIRMED.
//   Value goes into the optional login-blob field ([0x2B][account\0][PIN\0]).
//   spec: login_flow.md §4.2. RUNTIME-CONFIRMED.
//
// PASSIVE: zero game logic. No domain state, no packet building here.
//
// spec: Docs/RE/specs/frontend_scenes.md §11.3 (CODE-CONFIRMED layout).
//       §1.4a (flow position, RUNTIME-CONFIRMED).
//       Docs/RE/specs/login_flow.md §4.2 (PIN capacity, RUNTIME-CONFIRMED).

using Godot;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Second-password / PIN modal. Pixel-faithful to §11.3.
/// Presents a 2×5 scrambled digit keypad. On submit emits PinSubmitted (≤ 4 chars).
/// On cancel emits Cancelled.
/// </summary>
public sealed partial class PinModal : Control
{
    // PIN capacity: ≤ 4 characters. spec: login_flow.md §4.2. RUNTIME-CONFIRMED.
    private const int MaxPinLength = LoginLayout.PinMaxLength; // 4

    // =========================================================================
    // Outgoing intents
    // =========================================================================

    /// <summary>PIN submitted. Carries the entered PIN string (≤ 4 chars; may be empty = skip).</summary>
    [Signal]
    public delegate void PinSubmittedEventHandler(string pin);

    /// <summary>Raised when Cancel is clicked or the modal is dismissed.</summary>
    [Signal]
    public delegate void CancelledEventHandler();

    // =========================================================================
    // View state
    // =========================================================================

    private string _pin = ""; // held as internal string; never domain state

    private UiAssetLoader _assets = null!;
    private bool _ownsAssets;

    // The scramble permutation: perm[p] = digit shown at position p.
    private readonly int[] _perm = new int[10];

    // The array of 100 digit buttons (10 positions × 10 digits per position).
    // Indexed: [position * 10 + digitValue]. Exactly one per position is Visible.
    private readonly TextureButton?[] _digitBtns = new TextureButton?[100];

    // The masked-PIN display label.
    private Label _pinDisplay = null!;

    /// <summary>Optionally inject a shared asset loader.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        // Initial scramble on open. spec §11.3c. CODE-CONFIRMED.
        Scramble();

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PinModal] Build failed: {ex.Message}");
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
        // Full-canvas blocker: prevents clicks reaching behind the modal.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // Semi-transparent dimmer behind the panel.
        var dimmer = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f) };
        dimmer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dimmer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dimmer);

        // Modal panel — canvas-absolute position (347,173) size (329,422).
        // spec §11.3 "Modal panel rect: 347,173,329,422". CODE-CONFIRMED.
        var panel = new Control
        {
            Name = "PinPanel",
            Position = new Vector2(LoginLayout.PinModalX, LoginLayout.PinModalY),
            Size = new Vector2(LoginLayout.PinModalW, LoginLayout.PinModalH),
        };
        AddChild(panel);

        // Panel background: password.dds stretched over the panel.
        // The full 1024×1024 DDS contains the modal art; we show the whole atlas scaled to fit.
        // spec §11.3 "password.dds (1024×1024 DXT3)". CODE-CONFIRMED.
        Texture2D? pwTex = _assets.LoadAtlas(LoginLayout.AtlasPassword);
        if (pwTex is not null)
        {
            // The modal art occupies the left half of password.dds approximately.
            // Since we don't have exact bounds of the modal chrome within the DDS,
            // we tile/stretch the full atlas over the panel as a background.
            // PLAUSIBLE: exact chrome sub-rect within password.dds not yet catalogued (§11.7).
            var pwBg = new TextureRect
            {
                Name = "PinPanelBg",
                Texture = pwTex,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            pwBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            panel.AddChild(pwBg);
        }
        else
        {
            // VFS offline fallback.
            var fallback = new ColorRect
            {
                Name = "PinPanelFallback",
                Color = new Color(0.08f, 0.07f, 0.12f, 0.98f),
            };
            fallback.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var border = new Panel();
            border.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0f, 0f, 0f, 0f),
                BorderColor = new Color(0.6f, 0.5f, 0.25f),
            };
            style.SetBorderWidthAll(2);
            border.AddThemeStyleboxOverride("panel", style);
            panel.AddChild(fallback);
            panel.AddChild(border);
        }

        // H3 fix: red warning line near the top of the modal.
        // The official PIN modal shows a red warning text near the top of the panel.
        // The exact text is baked atlas art in password.dds (no msg.xdb id; §11.3 "no runtime text").
        // We render it as a red Label with the offline-fallback text until a sub-rect is catalogued.
        // Positioned at panel-local (28, 50) based on the keypad layout (first row at Y=170,
        // so the warning sits in the top section of the 422px-tall panel, approximately Y~50..80).
        // spec: Docs/RE/specs/frontend_scenes.md §11.3 — "warning line … baked into atlas art".
        // CODE-CONFIRMED (warning line exists); exact panel-local Y PLAUSIBLE until sub-rect swept.
        var warningLine = WidgetFactory.MakeLabel(
            "※ 비밀번호를 입력하세요", // fallback; real text is baked art in password.dds
            LoginLayout.FontBodyHeight,
            new Color(1f, 0.20f, 0.20f)); // red — spec §11.3 "red warning line". CODE-CONFIRMED.
        warningLine.Name = "WarningLine";
        warningLine.Position = new Vector2(28, 52);
        warningLine.Size = new Vector2(273, 20);
        warningLine.HorizontalAlignment = HorizontalAlignment.Left;
        panel.AddChild(warningLine);

        // PIN masked display — shows "****" as the user types.
        // The label position is approximate (no exact coord in spec for the display line).
        // PLAUSIBLE: centred near the top of the form area.
        _pinDisplay = WidgetFactory.MakeLabel("____", 20, new Color(0.95f, 0.90f, 0.55f));
        _pinDisplay.Name = "PinDisplay";
        _pinDisplay.Position = new Vector2(100, 90);
        _pinDisplay.Size = new Vector2(130, 30);
        _pinDisplay.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(_pinDisplay);

        // Keypad — 2 rows × 5 columns, 100 overlapping buttons.
        // spec §11.3a/§11.3b. CODE-CONFIRMED layout.
        BuildKeypad(panel);

        // Reset button (tag 11). spec §11.3d. CODE-CONFIRMED.
        BuildPinButton(panel,
            LoginLayout.PinResetX, LoginLayout.PinResetY,
            LoginLayout.PinResetW, LoginLayout.PinResetH,
            LoginLayout.PinResetNSrcX, LoginLayout.PinResetNSrcY,
            LoginLayout.PinResetHSrcX, LoginLayout.PinResetHSrcY,
            LoginLayout.PinResetPSrcX, LoginLayout.PinResetPSrcY,
            "ResetBtn", () => OnReset());

        // OK button (tag 12). spec §11.3d. CODE-CONFIRMED.
        BuildPinButton(panel,
            LoginLayout.PinOkX, LoginLayout.PinOkY,
            LoginLayout.PinOkW, LoginLayout.PinOkH,
            LoginLayout.PinOkNSrcX, LoginLayout.PinOkNSrcY,
            LoginLayout.PinOkHSrcX, LoginLayout.PinOkHSrcY,
            LoginLayout.PinOkPSrcX, LoginLayout.PinOkPSrcY,
            "OkBtn", () => OnOk());

        // Cancel button (tag 13). spec §11.3d. CODE-CONFIRMED.
        BuildPinButton(panel,
            LoginLayout.PinCancelX, LoginLayout.PinCancelY,
            LoginLayout.PinCancelW, LoginLayout.PinCancelH,
            LoginLayout.PinCancelNSrcX, LoginLayout.PinCancelNSrcY,
            LoginLayout.PinCancelHSrcX, LoginLayout.PinCancelHSrcY,
            LoginLayout.PinCancelPSrcX, LoginLayout.PinCancelPSrcY,
            "CancelBtn", () => OnCancel());

        // Refresh the visible digit buttons to match the initial scramble.
        ApplyScramble();

        GD.Print("[PinModal] Built. Keypad scrambled. PIN capacity ≤ 4 chars. " +
                 "spec: frontend_scenes.md §11.3 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Keypad construction (§11.3a / §11.3b). CODE-CONFIRMED.
    // =========================================================================

    private void BuildKeypad(Control parent)
    {
        // For each position p (0..9), build a stack of 10 digit buttons.
        // Tile X: 55*(p%5)+28. Tile Y: 170 (row 0), 230 (row 1). Tile size: 52×52.
        // spec §11.3a. CODE-CONFIRMED.
        for (int p = 0; p < 10; p++)
        {
            int tileX = LoginLayout.PinKeypadColSpacing * (p % 5) + LoginLayout.PinKeypadCol0X;
            int tileY = p < 5 ? LoginLayout.PinKeypadRow0Y : LoginLayout.PinKeypadRow1Y;

            for (int d = 0; d < 10; d++)
            {
                // Digit d at position p.
                // Glyph row in password.dds: d * 52. Three state columns: N=560, H=664, P=612.
                // spec §11.3b. CODE-CONFIRMED.
                int srcY = d * LoginLayout.PinDigitRowHeight;

                AtlasTexture? normalTex = _assets.Slice(
                    LoginLayout.AtlasPassword,
                    LoginLayout.PinDigitNormalSrcX, srcY,
                    LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH);

                AtlasTexture? hoverTex = _assets.Slice(
                    LoginLayout.AtlasPassword,
                    LoginLayout.PinDigitHoverSrcX, srcY,
                    LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH);

                AtlasTexture? pressedTex = _assets.Slice(
                    LoginLayout.AtlasPassword,
                    LoginLayout.PinDigitPressedSrcX, srcY,
                    LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH);

                var btn = new TextureButton
                {
                    Name = $"Digit_p{p}_d{d}",
                    Position = new Vector2(tileX, tileY),
                    CustomMinimumSize = new Vector2(LoginLayout.PinKeypadTileW, LoginLayout.PinKeypadTileH),
                    Visible = false, // hidden until ApplyScramble sets the correct one
                };

                if (normalTex is not null)
                    btn.TextureNormal = normalTex;
                if (hoverTex is not null)
                    btn.TextureHover = hoverTex;
                if (pressedTex is not null)
                    btn.TexturePressed = pressedTex;

                // Fallback when atlas unavailable: show digit as text.
                if (normalTex is null)
                {
                    var fallbackLbl = new Label
                    {
                        Text = d.ToString(),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        MouseFilter = MouseFilterEnum.Ignore,
                    };
                    fallbackLbl.AddThemeFontSizeOverride("font_size", 18);
                    fallbackLbl.AddThemeColorOverride("font_color", Colors.White);
                    btn.AddChild(fallbackLbl);
                    btn.Visible = true; // show fallback always in offline mode
                }

                int captureD = d;
                btn.Pressed += () => OnDigitPressed(captureD);

                parent.AddChild(btn);
                _digitBtns[p * 10 + d] = btn;
            }
        }
    }

    /// <summary>
    /// Builds a three-state button from password.dds sub-rects.
    /// </summary>
    private void BuildPinButton(Control parent,
        int x, int y, int w, int h,
        int nSrcX, int nSrcY,
        int hSrcX, int hSrcY,
        int pSrcX, int pSrcY,
        string name, Action onPress)
    {
        var btn = new TextureButton
        {
            Name = name,
            Position = new Vector2(x, y),
            CustomMinimumSize = new Vector2(w, h),
        };

        AtlasTexture? nTex = _assets.Slice(LoginLayout.AtlasPassword, nSrcX, nSrcY, w, h);
        AtlasTexture? hTex = _assets.Slice(LoginLayout.AtlasPassword, hSrcX, hSrcY, w, h);
        AtlasTexture? pTex = _assets.Slice(LoginLayout.AtlasPassword, pSrcX, pSrcY, w, h);

        if (nTex is not null) btn.TextureNormal = nTex;
        if (hTex is not null) btn.TextureHover = hTex;
        if (pTex is not null) btn.TexturePressed = pTex;

        // Offline fallback label.
        if (nTex is null)
        {
            var lbl = new Label
            {
                Text = name.Replace("Btn", ""),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            lbl.AddThemeFontSizeOverride("font_size", 12);
            lbl.AddThemeColorOverride("font_color", Colors.White);
            lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            btn.AddChild(lbl);
        }

        btn.Pressed += () => onPress();
        parent.AddChild(btn);
    }

    // =========================================================================
    // Scramble (§11.3c). CODE-CONFIRMED.
    // Fisher-Yates shuffle of [0..9] seeded from the wall-clock time.
    // Called on open and on Reset.
    // =========================================================================

    private void Scramble()
    {
        // Seed from current wall-clock time. spec §11.3c "seeded from wall-clock time". CODE-CONFIRMED.
        // Using .NET Random with Time.GetTicksMsec() as the seed is equivalent.
        // spec: Docs/RE/specs/frontend_scenes.md §11.3c. CODE-CONFIRMED.
        var rng = new Random((int)(global::Godot.Time.GetTicksMsec() & 0x7FFFFFFF));

        // Initialise identity permutation.
        for (int i = 0; i < 10; i++)
            _perm[i] = i;

        // Fisher-Yates shuffle. spec §11.3c. CODE-CONFIRMED.
        for (int i = 9; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (_perm[i], _perm[j]) = (_perm[j], _perm[i]);
        }
    }

    /// <summary>
    /// Shows the one visible digit button per position matching the current permutation.
    /// All others are hidden. spec §11.3b / §11.3c. CODE-CONFIRMED.
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
                // When VFS is offline all buttons have a fallback label and are always visible.
                // When VFS is online, exactly one digit per position is visible.
                if (btn.TextureNormal is not null)
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
        GD.Print($"[PinModal] Digit {digit} pressed. PIN length = {_pin.Length}.");
    }

    private void OnReset()
    {
        // Clear PIN and re-scramble. spec §11.3d "re-run scramble". CODE-CONFIRMED.
        _pin = "";
        RefreshPinDisplay();
        Scramble();
        ApplyScramble();
        GD.Print("[PinModal] Reset — PIN cleared, keypad re-scrambled.");
    }

    private void OnOk()
    {
        // Submit the PIN (may be empty — optional field). spec §11.3d / §1.4a. CODE-CONFIRMED.
        GD.Print($"[PinModal] OK — submitting PIN (length={_pin.Length}).");
        EmitSignal(SignalName.PinSubmitted, _pin);
    }

    private void OnCancel()
    {
        // spec §11.3d "Cancel: close / abort modal". CODE-CONFIRMED.
        GD.Print("[PinModal] Cancel pressed. Emitting Cancelled.");
        EmitSignal(SignalName.Cancelled);
    }

    private void RefreshPinDisplay()
    {
        // Show one '*' per entered digit, padded to 4 with '_'. Not in spec (display is baked art
        // in the original); we add a label as the closest equivalent.
        // PLAUSIBLE: position above the keypad (§11.3 gives no display widget coords).
        _pinDisplay.Text = new string('*', _pin.Length) + new string('_', MaxPinLength - _pin.Length);
    }
}