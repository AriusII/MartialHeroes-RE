// Ui/Scenes/Login/PinSubView.cs
//
// PIN sub-view for the Login(1) sub-states 31 (raise) / 32 (poll).
//
// Backed entirely by HudAtlasLibrary — no UiAssetLoader dependency.
// Faithfully reimplements GU PIN modal behaviour from:
//   spec: Docs/RE/specs/frontend_layout_tables.md §3 (chrome re-trace 2026-06-19, CODE-CONFIRMED).
//   spec: Docs/RE/specs/frontend_scenes.md §11.3 (CODE-CONFIRMED layout).
//
// Layout (panel-local coordinates inside the 329×422 modal panel):
//   BACKDROP (drawn first, behind everything):
//     password.dds source (0,0,329,422) → panel-local (0,0) size 329×422.
//     This region contains the ornate window frame, carved title "2차 비밀번호 입력",
//     red multi-line warning, "번호입력" caption and input-field box — all baked art.
//     Do NOT synthesize these as Godot labels or color rects; they are pixels in the texture.
//   Keypad:  2 × 5 tiles, each 52×52. Column spacing 55. Col0 X=28.
//            Row 0 Y=170 (digits 0..4), Row 1 Y=230 (digits 5..9).
//   Digit d glyph: password.dds src(d*52, 560/664/612, 52,52) Normal/Pressed/Hover.
//   Reset button (tag 11): panel-local (243,133,58,30). password.dds N(663,8) H(663,88) P(663,48).
//     → wipes the entered digits AND re-scrambles the keypad (NOT a single-digit backspace).
//   OK button (tag 12): panel-local (90,290,154,58). password.dds N(330,0) H(330,116) P(330,58).
//     → submits the up-to-4 digits (PinSubmitted).
//   Cancel button (tag 13): panel-local (90,350,154,58). password.dds N(486,0) H(486,116) P(486,58).
//     → closes the modal (Cancelled).
//   There is NO separate clear/backspace tag — the only edit verbs are digit / Reset / OK / Cancel.
//   The InventWindow.dds ExitPanel (340×190 src(318,647)) IS built in the original but kept HIDDEN
//   (SetVisible(false)) — it is NOT drawn and NOT added here.
//   spec: Docs/RE/specs/frontend_layout_tables.md §3 — Reset=11, OK=12, Cancel=13.
//
// Fisher-Yates scramble: seeded from wall-clock WHOLE SECONDS (srand(time()) granularity — two opens
// within the same calendar second produce an identical keypad). Scrambles digit position array.
// spec: Docs/RE/specs/frontend_layout_tables.md §3 "srand(time()) whole-second CRT seed". CODE-CONFIRMED.
// GAP-3: intentionally uses whole-second granularity (Math.Floor) to match the original's CRT srand(time())
// behaviour. Do NOT switch to millisecond-precision (GetTicksMsec / GetUnixTimeFromSystem fractional).
// spec: Docs/RE/scenes/login.md §5.2 "srand(time()) — two opens within the same second produce an IDENTICAL keypad".
//
// Signals:
//   PinSubmitted(string pin) — fired when OK (tag 11) is pressed.
//   Cancelled()             — fired when the third button (tag 13) is pressed.
//
// The parent (LoginWindow) shows/hides this Control.
// spec: Docs/RE/specs/frontend_scenes.md §11.3 — PIN modal layout: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3a — keypad tile layout: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3b — digit glyph axis convention: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3d — button positions: CODE-CONFIRMED.
// spec: Docs/RE/specs/frontend_scenes.md §11.3e — Fisher-Yates scramble: CODE-CONFIRMED.

using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Presentation.Screens.Layout;

// LoginLayout, WidgetRect (moved to engine-free layer)

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
///     PIN input sub-view for Login(1) sub-states 31/32.
///     <para>
///         Builds a 2×5 scrambled keypad from <c>password.dds</c> via <see cref="HudAtlasLibrary" />.
///         Fisher-Yates scramble seeded from wall-clock milliseconds (spec §11.3e).
///         Reset (tag 11), OK (tag 12), Cancel (tag 13) buttons.
///     </para>
///     <para>
///         Subscribe to <see cref="PinSubmitted" /> and <see cref="Cancelled" /> to receive
///         the outcome and emit the appropriate use-case call. Never mutate domain state here.
///     </para>
///     spec: Docs/RE/specs/frontend_scenes.md §11.3
/// </summary>
public sealed partial class PinSubView : Control
{
    [Signal]
    public delegate void CancelledEventHandler();

    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void PinSubmittedEventHandler(string pin);
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
    private const int TileW = LoginLayout.PinKeypadTileW; // 52
    private const int TileH = LoginLayout.PinKeypadTileH; // 52
    private const int ColSpacing = LoginLayout.PinKeypadColSpacing; // 55
    private const int Col0X = LoginLayout.PinKeypadCol0X; // 28
    private const int Row0Y = LoginLayout.PinKeypadRow0Y; // 170
    private const int Row1Y = LoginLayout.PinKeypadRow1Y; // 230

    // Digit glyph source V rows in password.dds.
    // spec: Docs/RE/specs/frontend_layout_tables.md §3 — Normal=560, Pressed=664, Hover=612 (CORRECTED).
    // The prior "Pressed 612 / Hover 664" reading was INVERTED — the construct call's argument order
    // (NORMAL,PRESSED,HOVER per §0.12) resolves 664 as PRESSED and 612 as HOVER unambiguously.
    private const int DigitNormalV = LoginLayout.PinDigitNormalSrcY; // 560
    private const int DigitHoverV = LoginLayout.PinDigitHoverSrcY; // 612 (CORRECTED from 664)
    private const int DigitPressedV = LoginLayout.PinDigitPressedSrcY; // 664 (CORRECTED from 612)
    private const int DigitColW = LoginLayout.PinDigitColWidth; // 52

    // PIN capacity.
    // spec: Docs/RE/specs/frontend_scenes.md §1.4a / login_flow.md §4.2. CODE-CONFIRMED.
    private const int PinMaxLength = LoginLayout.PinMaxLength; // 4

    // -------------------------------------------------------------------------
    // Tags (spec-confirmed action ids for PIN buttons)
    // spec: Docs/RE/specs/frontend_scenes.md §11.3d. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    // spec: Docs/RE/specs/frontend_layout_tables.md §3 — re-confirmed vs binary 2026-06-18.
    private const int TagReset = 11; // Reset rect (243,133): wipe entry + re-scramble.
    private const int TagOk = 12; // OK rect (90,290): submit PIN.
    private const int TagCancel = 13; // Cancel rect (90,350): close modal.

    private const int PinDisplayX = 81;
    private const int PinDisplayY = 138;
    private const int PinDisplayW = 150;
    private const int PinDisplayH = 22;

    // Hidden reused ExitPanel child ("really cancel?" confirm overlay).
    // InventWindow.dds (A3) src(318,647) 340×190, centered in the parent panel.
    // Built hidden at construction; revealed by Cancel (tag 13). spec §3.
    // canvas position: parent (347,173) + center offset → (347+(329-340)/2, 173+(422-190)/2) = (341,289)
    // Approximated as panel-local (−6, 116) to achieve a centered appearance.
    // spec: Docs/RE/specs/frontend_layout_tables.md §3 "Hidden reused ExitPanel child | built hidden"
    private const int ExitPanelW = 340; // spec §3 "sized 340×190". CODE-CONFIRMED.
    private const int ExitPanelH = 190; // spec §3. CODE-CONFIRMED.
    private const int ExitPanelSrcX = 318; // spec §3 "source origin (318,647)". CODE-CONFIRMED.

    private const int ExitPanelSrcY = 647; // spec §3. CODE-CONFIRMED.

    // Panel-local position to center the 340×190 ExitPanel within the 329×422 PIN panel.
    // centered X = (329−340)/2 = −5.5 ≈ −6; centered Y = (422−190)/2 = 116. spec §3.
    private const int ExitPanelLocalX = -6; // spec §3 "centered in the parent". Approx.
    private const int ExitPanelLocalY = 116; // spec §3. Approx.

    // ExitPanel "yes" / "no" button tags (action ids for cancel-confirm). spec §3.
    // The same A3 button art used by Confirm-A/B (src 302,900/415,900). spec §3.
    private const int ExitPanelYesAction = 201; // local — "yes, cancel PIN" (dismiss + emit Cancelled).
    private const int ExitPanelNoAction = 202; // local — "no, go back" (hide ExitPanel, resume PIN).

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly HudAtlasLibrary _atlas;

    // 100 digit-face buttons, indexed [position 0..9][face 0..9].
    // Built ONCE in BuildKeypad(); scramble re-arms only toggle Visible per slot.
    // spec: Docs/RE/specs/frontend_layout_tables.md §3 "100 buttons total, build once then toggle visibility"
    private readonly TextureButton?[,] _digitButtons = new TextureButton?[10, 10];

    // The hidden ExitPanel confirm overlay (Cancel → shows this). spec §3.
    private Control? _exitPanel;
    private string _pin = "";

    // PIN display label.
    private Label? _pinDisplay;

    // Scrambled digit assignment: _scrambled[keypadSlot] = actualDigit.
    // Fisher-Yates from wall-clock seed. spec §11.3e. CODE-CONFIRMED.
    private int[] _scrambled = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates the PIN sub-view.
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
        Position = new Vector2(ModalX, ModalY);

        Size = new Vector2(ModalW, ModalH);
        CustomMinimumSize = new Vector2(ModalW, ModalH);

        // Scramble the keypad.
        Scramble();

        // Modal click-capture + dragon-frame chrome (no invented dim tint).
        BuildModalChrome();

        // Build keypad face buttons.
        BuildKeypad();

        // Build PIN display label.
        _pinDisplay = new Label
        {
            Text = "",
            Position = new Vector2(PinDisplayX, PinDisplayY),
            Size = new Vector2(PinDisplayW, PinDisplayH),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _pinDisplay.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_pinDisplay);

        // Reset(11) / OK(12) / Cancel(13) control buttons.
        BuildControlButtons();

        // Hidden reused ExitPanel (cancel-confirm overlay). Built hidden, raised by Cancel (tag 13).
        // spec: Docs/RE/specs/frontend_layout_tables.md §3 "Hidden reused ExitPanel child"
        BuildHiddenExitPanel();
    }

    // -------------------------------------------------------------------------
    // Modal chrome: transparent click-capture + password.dds backdrop blit
    // spec: Docs/RE/specs/frontend_layout_tables.md §3
    //   "the keypad constructor assigns data/ui/password.dds as the panel's own backdrop texture …
    //    blits that backdrop BEFORE the children, source (0,0)-(329,422) → destination (347,173)
    //    size 329×422. The ornate chrome IS this backdrop region — frame, title '2차 비밀번호 입력',
    //    red warning, '번호입력' caption, input-field box — all painted into the texture."
    //   "The reused ExitPanel child (InventWindow.dds 340×190 src(318,647)) is built then kept
    //    HIDDEN (SetVisible(false)). Do not draw it." (§3 "Hidden reused ExitPanel child".)
    // -------------------------------------------------------------------------

    private void BuildModalChrome()
    {
        // Transparent full-canvas capture rect: a focus-capturing modal eats clicks outside the panel
        // so they do NOT fall through to the login form. No invented dim tint (not in the binary).
        // Must be first child so it sits behind the backdrop.
        AddChild(new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0f),
            Position = new Vector2(-ModalX, -ModalY), // covers the whole 1024×768 canvas
            Size = new Vector2(1024, 768),
            MouseFilter = MouseFilterEnum.Stop
        });

        // password.dds backdrop blit: source (0,0,329,422) → panel-local (0,0) size 329×422.
        // This IS the entire ornate window chrome — frame, title, warning text, input-field box
        // are all baked into this region of the texture. Drawn BEFORE the keypad children.
        // spec: Docs/RE/specs/frontend_layout_tables.md §3 (chrome re-trace 2026-06-19).
        Texture2D? backdrop = _atlas.SliceByPath(
            AtlasPassword,
            0, 0, // srcX=0, srcY=0 — top-left corner of password.dds
            ModalW, ModalH // 329×422 — exact panel size = exact source region
        );
        if (backdrop is not null)
            AddChild(new TextureRect
            {
                Position = Vector2.Zero, // panel-local (0,0)
                Size = new Vector2(ModalW, ModalH),
                Texture = backdrop,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = MouseFilterEnum.Ignore // transparent to input; buttons above handle it
            });

        // NOTE: the InventWindow.dds dragon-frame (ExitPanel clone 340×190 src(318,647)) IS built
        // by the original constructor but is immediately kept hidden (SetVisible(false)) and never
        // drawn. We do NOT add it here — it is explicitly absent from the visible draw order.
        // spec: Docs/RE/specs/frontend_layout_tables.md §3 "Hidden reused ExitPanel child".
    }

    // -------------------------------------------------------------------------
    // Keypad builder
    // -------------------------------------------------------------------------

    private void BuildKeypad()
    {
        // Build the 100 digit-face buttons ONCE (10 positions × 10 face-buttons each).
        // The original builds all 100 buttons once at construction then shows exactly one face
        // per position based on the scramble. Subsequent scrambles ONLY toggle Visible — no
        // QueueFree/rebuild, no per-scramble allocation.
        // spec: Docs/RE/specs/frontend_layout_tables.md §3
        //   "100 buttons total, build once then toggle visibility per scramble"
        // spec: Docs/RE/specs/frontend_scenes.md §11.3a "100 buttons total, actions 0..99".
        for (var pos = 0; pos < 10; pos++)
        {
            var col = pos % 5;
            var row = pos / 5;

            var x = Col0X + col * ColSpacing; // spec §11.3a. CODE-CONFIRMED.
            var y = row == 0 ? Row0Y : Row1Y; // spec §11.3a. CODE-CONFIRMED.

            for (var face = 0; face < 10; face++)
            {
                // Digit face glyph: srcU = face*52; srcV per state.
                // spec: Docs/RE/specs/frontend_scenes.md §11.3b.
                var srcU = face * DigitColW;

                Texture2D? normal = _atlas.SliceByPath(AtlasPassword, srcU, DigitNormalV, TileW, TileH);
                Texture2D? pressed = _atlas.SliceByPath(AtlasPassword, srcU, DigitPressedV, TileW, TileH);
                Texture2D? hover = _atlas.SliceByPath(AtlasPassword, srcU, DigitHoverV, TileW, TileH);

                var btn = new TextureButton
                {
                    Position = new Vector2(x, y),
                    Size = new Vector2(TileW, TileH),
                    CustomMinimumSize = new Vector2(TileW, TileH),
                    IgnoreTextureSize = true,
                    StretchMode = TextureButton.StretchModeEnum.Scale,
                    TextureNormal = normal,
                    TextureHover = hover,
                    TexturePressed = pressed,
                    TextureDisabled = normal,
                    Visible = false, // all hidden initially; ApplyScramble() reveals exactly one per pos
                    MouseFilter = MouseFilterEnum.Ignore // ApplyScramble() arms the visible one
                };

                var actionId = pos * 10 + face;
                btn.Pressed += () => OnDigitFaceAction(actionId);
                AddChild(btn);
                _digitButtons[pos, face] = btn;
            }
        }

        // Apply the initial scramble to set exactly one face visible per position.
        // spec: Docs/RE/specs/frontend_layout_tables.md §3 "exactly one digit-button visible per cell"
        ApplyScramble();
    }

    // Toggle visibility on the 100 pre-built digit-face buttons so exactly one face is shown per
    // keypad position. No allocation — all 100 buttons are already in the scene tree.
    // spec: Docs/RE/specs/frontend_layout_tables.md §3
    //   "build once then only toggle visibility after the Fisher-Yates scramble"
    private void ApplyScramble()
    {
        for (var pos = 0; pos < 10; pos++)
        {
            var shownFace = _scrambled[pos];
            for (var face = 0; face < 10; face++)
            {
                var btn = _digitButtons[pos, face];
                if (btn is null) continue;
                var shown = face == shownFace;
                btn.Visible = shown;
                btn.MouseFilter = shown ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            }
        }
    }

    private void BuildButton(
        int x, int y, int w, int h,
        int nSrcX, int nSrcY,
        int hSrcX, int hSrcY,
        int pSrcX, int pSrcY,
        int tag)
    {
        Texture2D? normal = _atlas.SliceByPath(AtlasPassword, nSrcX, nSrcY, w, h);
        Texture2D? hover = _atlas.SliceByPath(AtlasPassword, hSrcX, hSrcY, w, h);
        Texture2D? pressed = _atlas.SliceByPath(AtlasPassword, pSrcX, pSrcY, w, h);

        var btn = new TextureButton
        {
            Position = new Vector2(x, y),
            Size = new Vector2(w, h),
            CustomMinimumSize = new Vector2(w, h),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = normal,
            TextureHover = hover,
            TexturePressed = pressed,
            TextureDisabled = normal
        };

        var capturedTag = tag;
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

    private void OnDigitFaceAction(int actionId)
    {
        var pos = actionId / 10;
        var face = actionId % 10;
        if ((uint)pos >= (uint)_scrambled.Length || _scrambled[pos] != face)
            return;

        OnDigitPressed(face);
    }

    private void OnButtonAction(int tag)
    {
        switch (tag)
        {
            case TagReset:
                // Reset tag 11: wipe the entered digits AND re-scramble the keypad
                // (full re-roll, not a single-digit backspace).
                // spec: frontend_layout_tables.md §3 (binary: tag 11 → ScrambleKeypad).
                _pin = "";
                UpdatePinDisplay();
                Scramble();
                RebuildKeypad();
                GD.Print("[PinSubView] Reset (tag 11): entry wiped + keypad re-scrambled. " +
                         "spec: frontend_layout_tables.md §3.");
                break;

            case TagOk:
                // OK tag 12: submit the PIN, then re-scramble (re-roll on OK per spec §3).
                // spec: frontend_layout_tables.md §3 "re-roll: the scramble re-seeds and re-shuffles on
                //   open (SetVisible-show), Reset, OK, and Cancel."
                GD.Print($"[PinSubView] OK (tag 12): PinSubmitted(pin_len={_pin.Length}). " +
                         "spec: frontend_layout_tables.md §3.");
                EmitSignal(SignalName.PinSubmitted, _pin);
                _pin = "";
                Scramble();
                RebuildKeypad();
                break;

            case TagCancel:
                // Cancel tag 13: clear entry, re-scramble, then raise the hidden ExitPanel as a
                // "really cancel?" confirm overlay. spec §3 (CONFIRMED, counter-check 2026-06-22):
                //   "the Cancel handler clears the entry string, re-scrambles, then explicitly calls
                //    SetVisible(1) on the reused ExitPanel child as a 'really cancel?' confirm overlay."
                // The user must then press "Yes" in the ExitPanel to actually emit Cancelled.
                // spec: Docs/RE/specs/frontend_layout_tables.md §3 "Cancel (tag 13) raises the cancel-confirm ExitPanel"
                GD.Print("[PinSubView] Cancel (tag 13): clear entry + re-scramble + raise ExitPanel. spec: §3.");
                _pin = "";
                UpdatePinDisplay();
                Scramble();
                RebuildKeypad();
                // Show the hidden ExitPanel confirm overlay. spec §3.
                if (_exitPanel is not null) _exitPanel.Visible = true;
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
    // Keypad scramble — uniform permutation reseeded each open.
    // spec: Docs/RE/specs/frontend_layout_tables.md §3 — seed = whole-second time() (CRT srand;
    // explicitly NOT GetTickCount/timeGetTime/QPC), ASCENDING shuffle (j = rand() mod i, i = 2..10;
    // MSVC std::random_shuffle shape), one digit per cell, re-roll on open/Reset/OK/Cancel. The exact
    // permutation is reproduced BY MECHANISM, not hard-coded: C# Random != MSVC CRT rand, so it is not
    // byte-identical — and must not be, the keypad is intentionally unpredictable.
    // CODE-CONFIRMED (static IDA, doida.exe, CYCLE 18 Phase A).
    // -------------------------------------------------------------------------

    private void Scramble()
    {
        _scrambled = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

        // Seed from the whole-second wall clock (CRT srand(time()) — whole-second granularity,
        // explicitly NOT GetTickCount/QueryPerformanceCounter). spec §3.
        // GetUnixTimeFromSystem() returns a double with sub-second precision; floor first so that
        // two opens within the same calendar second produce the IDENTICAL seed — matching the CRT
        // srand(time()) behaviour: time() returns whole seconds. spec §3.
        // spec: Docs/RE/specs/frontend_layout_tables.md §3 "srand(time()) whole-second CRT seed"
        var seed = (int)((long)Math.Floor(Time.GetUnixTimeFromSystem()) & 0x7FFF_FFFF);
        var rng = new Random(seed);

        // Ascending Fisher-Yates (MSVC random_shuffle): for i = 1..9, swap a[i] with a[rand() mod (i+1)].
        // spec §3.
        for (var i = 1; i < 10; i++)
        {
            var j = rng.Next(0, i + 1);
            (_scrambled[i], _scrambled[j]) = (_scrambled[j], _scrambled[i]);
        }
    }

    private void RebuildKeypad()
    {
        // The 100 digit-face buttons and all chrome are BUILT ONCE in _Ready.
        // On each scramble (Reset/OK/Cancel/re-open) only flip Visible flags — no QueueFree, no rebuild,
        // no per-scramble allocation. This matches the original, which builds the button array once in
        // the constructor and only shows/hides the scrambled face per slot on each re-roll.
        // spec: Docs/RE/specs/frontend_layout_tables.md §3
        //   "BUILD ONCE then only TOGGLE VISIBILITY — exactly one face visible per position after scramble"
        ApplyScramble();
        UpdatePinDisplay(); // reflect the current _pin (may have been cleared before calling)
    }

    // Builds the hidden reused ExitPanel cancel-confirm overlay.
    // Called once from _Ready (and NOT from RebuildKeypad — the ExitPanel persists across re-scrambles).
    // Revealed by Cancel (tag 13); dismissed by its own Yes/No buttons.
    // spec: Docs/RE/specs/frontend_layout_tables.md §3
    //   "Hidden reused ExitPanel child: InventWindow.dds 340×190 src(318,647) centered in parent; built
    //    hidden (SetVisible(false)); raised by Cancel (tag 13) as the cancel-confirm."
    private void BuildHiddenExitPanel()
    {
        // Panel chrome: InventWindow.dds (A3) src(318,647,340,190) → centered in the 329×422 parent.
        // Panel-local position: (−6, 116) ≈ centered. spec §3.
        var panel = new Control
        {
            Name = "PinExitPanel",
            Position = new Vector2(ExitPanelLocalX, ExitPanelLocalY),
            Size = new Vector2(ExitPanelW, ExitPanelH),
            Visible = false, // hidden at construction. spec §3.
            MouseFilter = MouseFilterEnum.Pass
        };

        // Chrome blit: A3 (InventWindow.dds) src(318,647,340,190) at panel-local (0,0). spec §3.
        Texture2D? chrome = _atlas.SliceByPath(
            LoginLayout.AtlasInventWindow,
            ExitPanelSrcX, ExitPanelSrcY, ExitPanelW, ExitPanelH);
        if (chrome is not null)
            panel.AddChild(new TextureRect
            {
                Position = Vector2.Zero,
                Size = new Vector2(ExitPanelW, ExitPanelH),
                Texture = chrome,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            });

        // "Yes, cancel" button — uses A3 Confirm-A OK art (302,900/415,900) at (120,136,113,40). spec §3.
        // Clears entry + re-scrambles then emits Cancelled → LoginWindow returns to credential form.
        Texture2D? yesN = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 302, 900, 113, 40);
        Texture2D? yesH = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 415, 900, 113, 40);
        var yesBtn = new TextureButton
        {
            Name = "ExitYes",
            Position = new Vector2(40, 136),
            Size = new Vector2(113, 40),
            CustomMinimumSize = new Vector2(113, 40),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = yesN,
            TextureHover = yesH,
            TexturePressed = yesN
        };
        yesBtn.Pressed += OnExitPanelYes;
        panel.AddChild(yesBtn);

        // "No, go back" button — uses A3 Confirm-B OK art (302,860/415,860) at (190,136,113,40). spec §3.
        Texture2D? noN = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 302, 860, 113, 40);
        Texture2D? noH = _atlas.SliceByPath(LoginLayout.AtlasInventWindow, 415, 860, 113, 40);
        var noBtn = new TextureButton
        {
            Name = "ExitNo",
            Position = new Vector2(190, 136),
            Size = new Vector2(113, 40),
            CustomMinimumSize = new Vector2(113, 40),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.Scale,
            TextureNormal = noN,
            TextureHover = noH,
            TexturePressed = noN
        };
        noBtn.Pressed += OnExitPanelNo;
        panel.AddChild(noBtn);

        AddChild(panel);
        _exitPanel = panel;
    }

    private void OnExitPanelYes()
    {
        // "Yes, cancel PIN entry": hide ExitPanel, re-scramble for next use, emit Cancelled.
        // spec §3 "Cancel handler … SetVisible(1) on ExitPanel". The Yes path confirms cancel.
        // LoginWindow.OnPinCancelled will hide the whole PinKeypadRoot → no need to hide ExitPanel here,
        // but we do it anyway for cleanliness and re-scramble for the next open.
        if (_exitPanel is not null) _exitPanel.Visible = false;
        _pin = "";
        UpdatePinDisplay();
        Scramble();
        RebuildKeypad(); // re-scramble the keypad for the next open. spec §3 "re-roll on Cancel".
        GD.Print("[PinSubView] ExitPanel Yes: confirmed cancel; emitting Cancelled. spec: §3.");
        EmitSignal(SignalName.Cancelled);
    }

    private void OnExitPanelNo()
    {
        // "No, go back": hide ExitPanel and resume PIN entry. spec §3.
        if (_exitPanel is not null) _exitPanel.Visible = false;
        GD.Print("[PinSubView] ExitPanel No: resume PIN entry. spec: §3.");
    }

    // Reset(11) / OK(12) / Cancel(13) control buttons.
    // spec: Docs/RE/specs/frontend_layout_tables.md §3 — re-confirmed vs binary 2026-06-18:
    //   Reset rect (243,133) → tag 11 (wipe + re-scramble); OK rect (90,290) → tag 12 (submit);
    //   Cancel rect (90,350) → tag 13 (close). No separate clear/backspace tag.
    private void BuildControlButtons()
    {
        // Reset (tag 11): password.dds N(663,8) H(663,88) P(663,48).
        BuildButton(
            LoginLayout.PinResetX, LoginLayout.PinResetY, LoginLayout.PinResetW, LoginLayout.PinResetH,
            LoginLayout.PinResetNSrcX, LoginLayout.PinResetNSrcY,
            LoginLayout.PinResetHSrcX, LoginLayout.PinResetHSrcY,
            LoginLayout.PinResetPSrcX, LoginLayout.PinResetPSrcY,
            TagReset);

        // OK (tag 12): password.dds N(330,0) H(330,116) P(330,58).
        BuildButton(
            LoginLayout.PinOkX, LoginLayout.PinOkY, LoginLayout.PinOkW, LoginLayout.PinOkH,
            LoginLayout.PinOkNSrcX, LoginLayout.PinOkNSrcY,
            LoginLayout.PinOkHSrcX, LoginLayout.PinOkHSrcY,
            LoginLayout.PinOkPSrcX, LoginLayout.PinOkPSrcY,
            TagOk);

        // Cancel (tag 13): password.dds N(486,0) H(486,116) P(486,58).
        BuildButton(
            LoginLayout.PinCancelX, LoginLayout.PinCancelY, LoginLayout.PinCancelW, LoginLayout.PinCancelH,
            LoginLayout.PinCancelNSrcX, LoginLayout.PinCancelNSrcY,
            LoginLayout.PinCancelHSrcX, LoginLayout.PinCancelHSrcY,
            LoginLayout.PinCancelPSrcX, LoginLayout.PinCancelPSrcY,
            TagCancel);
    }
}