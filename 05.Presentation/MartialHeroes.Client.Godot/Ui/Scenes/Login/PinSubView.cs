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
// Fisher-Yates scramble: seeded from wall-clock milliseconds, scrambles digit position array.
// spec: Docs/RE/specs/frontend_scenes.md §11.3e "Fisher-Yates seed from wall-clock ms". CODE-CONFIRMED.
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
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Login;

/// <summary>
/// PIN input sub-view for Login(1) sub-states 31/32.
///
/// <para>Builds a 2×5 scrambled keypad from <c>password.dds</c> via <see cref="HudAtlasLibrary"/>.
/// Fisher-Yates scramble seeded from wall-clock milliseconds (spec §11.3e).
/// Reset (tag 11), OK (tag 12), Cancel (tag 13) buttons.</para>
///
/// <para>Subscribe to <see cref="PinSubmitted"/> and <see cref="Cancelled"/> to receive
/// the outcome and emit the appropriate use-case call. Never mutate domain state here.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §11.3
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

    // -------------------------------------------------------------------------
    // Signals
    // -------------------------------------------------------------------------

    [Signal]
    public delegate void PinSubmittedEventHandler(string pin);

    [Signal]
    public delegate void CancelledEventHandler();

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
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _pinDisplay.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_pinDisplay);

        // Reset(11) / OK(12) / Cancel(13) control buttons.
        BuildControlButtons();
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
            MouseFilter = MouseFilterEnum.Stop,
        });

        // password.dds backdrop blit: source (0,0,329,422) → panel-local (0,0) size 329×422.
        // This IS the entire ornate window chrome — frame, title, warning text, input-field box
        // are all baked into this region of the texture. Drawn BEFORE the keypad children.
        // spec: Docs/RE/specs/frontend_layout_tables.md §3 (chrome re-trace 2026-06-19).
        Texture2D? backdrop = _atlas.SliceByPath(
            AtlasPassword,
            0, 0,           // srcX=0, srcY=0 — top-left corner of password.dds
            ModalW, ModalH  // 329×422 — exact panel size = exact source region
        );
        if (backdrop is not null)
        {
            AddChild(new TextureRect
            {
                Position = Vector2.Zero, // panel-local (0,0)
                Size = new Vector2(ModalW, ModalH),
                Texture = backdrop,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = MouseFilterEnum.Ignore, // transparent to input; buttons above handle it
            });
        }

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
        // 2 × 5 grid positions, each with 10 overlapping digit-face buttons.
        // Position 0..4 = top row (Y=Row0Y), Position 5..9 = bottom row (Y=Row1Y).
        // Digit assigned via _scrambled[position].
        // spec: Docs/RE/specs/frontend_scenes.md §11.3a "100 buttons total, actions 0..99".
        for (int pos = 0; pos < 10; pos++)
        {
            int digit = _scrambled[pos];
            int col = pos % 5;
            int row = pos / 5;

            int x = Col0X + col * ColSpacing; // spec §11.3a. CODE-CONFIRMED.
            int y = row == 0 ? Row0Y : Row1Y; // spec §11.3a. CODE-CONFIRMED.

            for (int face = 0; face < 10; face++)
            {
                // Digit face glyph: srcU = face*52; srcV per state.
                // spec: Docs/RE/specs/frontend_scenes.md §11.3b.
                int srcU = face * DigitColW;

                Texture2D? normal = _atlas.SliceByPath(AtlasPassword, srcU, DigitNormalV, TileW, TileH);
                Texture2D? pressed = _atlas.SliceByPath(AtlasPassword, srcU, DigitPressedV, TileW, TileH);
                Texture2D? hover = _atlas.SliceByPath(AtlasPassword, srcU, DigitHoverV, TileW, TileH);
                bool shown = face == digit;

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
                    Visible = shown,
                    MouseFilter = shown ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore,
                };

                int actionId = pos * 10 + face;
                btn.Pressed += () => OnDigitFaceAction(actionId);
                AddChild(btn);
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
            TextureDisabled = normal,
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

    private void OnDigitFaceAction(int actionId)
    {
        int pos = actionId / 10;
        int face = actionId % 10;
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
                // OK tag 12: submit the PIN. Application validates length.
                // spec: frontend_layout_tables.md §3 (binary: tag 12 → SubmitOk).
                GD.Print($"[PinSubView] OK (tag 12): PinSubmitted(pin_len={_pin.Length}). " +
                         "spec: frontend_layout_tables.md §3.");
                EmitSignal(SignalName.PinSubmitted, _pin);
                break;

            case TagCancel:
                // Cancel tag 13: close the modal.
                // spec: frontend_layout_tables.md §3 (binary: tag 13 → Cancel).
                GD.Print("[PinSubView] Cancel (tag 13): Cancelled. spec: frontend_layout_tables.md §3.");
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

        // Seed from the whole-second wall clock (CRT time()). spec §3.
        int seed = (int)((long)global::Godot.Time.GetUnixTimeFromSystem() & 0x7FFF_FFFF);
        var rng = new Random(seed);

        // Ascending Fisher-Yates (MSVC random_shuffle): for i = 1..9, swap a[i] with a[rand() mod (i+1)].
        // spec §3.
        for (int i = 1; i < 10; i++)
        {
            int j = rng.Next(0, i + 1);
            (_scrambled[i], _scrambled[j]) = (_scrambled[j], _scrambled[i]);
        }
    }

    private void RebuildKeypad()
    {
        // Remove existing keypad buttons (100 digit-face buttons after the dim overlay).
        // We can't safely iterate-and-remove; just rebuild all children.
        // Store non-keypad children, clear, re-add.
        // Simpler: just free keypad-tagged buttons. Since we added dim+keypad+display+buttons
        // in a known order, re-add everything.
        foreach (Node child in GetChildren())
            child.QueueFree();
        _pinDisplay = null;

        // Re-run the full build (same as _Ready, no DEV prefill auto-submit on reset).
        BuildModalChrome();

        BuildKeypad();

        _pinDisplay = new Label
        {
            Text = new string('*', _pin.Length),
            Position = new Vector2(PinDisplayX, PinDisplayY),
            Size = new Vector2(PinDisplayW, PinDisplayH),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _pinDisplay.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_pinDisplay);

        BuildControlButtons();
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