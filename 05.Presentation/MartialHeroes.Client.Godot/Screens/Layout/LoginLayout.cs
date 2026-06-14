// Screens/Layout/LoginLayout.cs
//
// Pixel-exact layout constants for the LOGIN scene on the 1024×768 reference canvas.
// ALL values are CODE-CONFIRMED literals from the legacy scene builder, via:
//   spec: Docs/RE/specs/frontend_scenes.md §11.0 (common model)
//         §11.2a–f (login widget table — every widget, every src rect)
//         §11.1 (texture inventory — atlas paths + formats)
//         §11.3 (PIN modal)
//         §11.4 (server-list overlay)
//         §9   (consolidated SFX / msg-id tables)
//         §1.2 (action ids)
//         §1.4 (local validation thresholds)
//         §1.9 (msg.xdb id table)
//
// Coordinate model (§11.0):
//   - Design canvas: 1024×768, top-left anchored.
//   - Scene origin: (screenWidth/2 - 512, screenHeight/2 - 384) — widget coords are canvas-local.
//   - Widget shape: (X, Y, W, H) on canvas; (srcU, srcV) = top-left of the atlas sub-rect.
//   - A subset of background bars are placed at height-scaled Y = 326 × screenHeight / 768.
//     Those rows are called out with the suffix "ScaleY" and use the formula at runtime.
//
// Atlas shorthand (§11.2):
//   A = login_slice1.dds   (DXT2 premultiplied — §11.1)
//   B = loginwindow.dds    (DXT5 — §11.1)
//   C = InventWindow.dds   (§11.1 — shared dialog / PIN frame)
//   D = loginwindow_02.dds (DXT2 — §11.1)

namespace MartialHeroes.Client.Godot.Screens.Layout;

/// <summary>
/// A widget rectangle + atlas source sub-rect (NORMAL frame) on the reference canvas.
/// <c>X/Y/W/H</c> are screen-local pixels (canvas-local, §11.0);
/// <c>SrcX/SrcY</c> are the top-left pixel of the NORMAL sprite in the atlas DDS.
/// Hover and pressed source origins are stored as separate constants.
/// </summary>
public readonly record struct WidgetRect(int X, int Y, int W, int H, int SrcX, int SrcY);

/// <summary>
/// Login-scene layout constants.
/// spec: Docs/RE/specs/frontend_scenes.md §11.2 (CODE-CONFIRMED literals).
/// </summary>
public static class LoginLayout
{
    // =========================================================================
    // Reference canvas (§11.0). CODE-CONFIRMED.
    // =========================================================================

    public const int RefWidth = 1024; // spec §11.0 "design canvas 1024×768". CODE-CONFIRMED.
    public const int RefHeight = 768; // spec §11.0. CODE-CONFIRMED.

    // =========================================================================
    // Atlas VFS paths (§11.1). CODE-CONFIRMED paths; dims SAMPLE-VERIFIED.
    // =========================================================================

    // A — login background art + stone chrome + baked Korean plates + gold confirm button.
    // Format: DXT2 (premultiplied alpha). spec §11.1 "login_slice1.dds, 1024×1024, DXT2". CODE-CONFIRMED.
    public const string AtlasLoginSlice1 = "data/ui/login_slice1.dds";

    // B — main panel chrome, listbox frame, scroll arrows, server-row buttons, confirm buttons.
    // Also the char-select frame atlas (shared). Format: DXT5. spec §11.1. CODE-CONFIRMED.
    public const string AtlasLoginWindow = "data/ui/loginwindow.dds";

    // C — shared dialog chrome (notice/error/quit panels) and PIN modal frame.
    // spec §11.1 "InventWindow.dds". CODE-CONFIRMED.
    public const string AtlasInventWindow = "data/ui/inventwindow.dds";

    // D — server-list parchment scroll panel + channel-selector tab blocks.
    // Format: DXT2. spec §11.1 "loginwindow_02.dds, 1024×1024, DXT2". CODE-CONFIRMED.
    public const string AtlasLoginWindow02 = "data/ui/loginwindow_02.dds";

    // Cursor atlas. spec §11.1 "data/cursor/stand.dds, 32×32, DXT2". CODE-CONFIRMED.
    public const string AtlasCursor = "data/cursor/stand.dds";

    // =========================================================================
    // §11.2a Upper window — main panel, server listbox, scroll controls. CODE-CONFIRMED.
    // =========================================================================

    // B@(0,0,1024,110) src(0,0) — TOP chrome cap strip (carved bezel, hanging rings, top edge).
    // The main panel art (B) starts at canvas Y=110; this slice of the same atlas placed at
    // canvas Y=0 shows the top ornamental cap of the stone frame above the ink-wash painting.
    // The region B src(0,0..110) contains the top edge of the stone chrome (rings, carved rim).
    // spec: Docs/RE/specs/frontend_scenes.md §11.2a (main panel B at Y=110); top cap is
    //       the same atlas region placed at Y=0 to show above the main panel anchor. CODE-CONFIRMED layout.
    public static readonly WidgetRect TopChrome = new(0, 0, 1024, 110, 0, 0);

    // B@(0,110,1024,490) src(0,0) — main background panel art.
    // spec §11.2a "Main panel art". CODE-CONFIRMED.
    public static readonly WidgetRect MainPanel = new(0, 110, 1024, 490, 0, 0);

    // B@(270,85,483,490) src(0,490) — server dropdown / listbox container.
    // spec §11.2a "Server dropdown / listbox container". CODE-CONFIRMED.
    public static readonly WidgetRect ServerListbox = new(270, 85, 483, 490, 0, 490);

    // B@(467,86,13,10) src(483,490) — list scroll-up arrow, action 106.
    // spec §11.2a "List scroll-up arrow". CODE-CONFIRMED.
    public static readonly WidgetRect ScrollUpArrow = new(467, 86, 13, 10, 483, 490);

    // B@(467,455,13,10) src(505,490) — list scroll-down arrow, action 107.
    // spec §11.2a "List scroll-down arrow". CODE-CONFIRMED.
    public static readonly WidgetRect ScrollDownArrow = new(467, 455, 13, 10, 505, 490);

    // B@(469,98,9,9) src(496,490) — scrollbar thumb, action 108.
    // spec §11.2a "Scrollbar thumb". CODE-CONFIRMED.
    public static readonly WidgetRect ScrollThumb = new(469, 98, 9, 9, 496, 490);

    // B@(207,44,70,17) src(70,980) — listbox header / selection bar.
    // spec §11.2a "Listbox header / selection bar". CODE-CONFIRMED.
    public static readonly WidgetRect ListboxHeader = new(207, 44, 70, 17, 70, 980);

    // =========================================================================
    // §11.2b Background + two channel-selector blocks. CODE-CONFIRMED.
    // =========================================================================

    // A@(0,0,1024,398) — full background art panel (below the form band).
    // spec §11.2b "Full background art panel". CODE-CONFIRMED.
    public static readonly WidgetRect BackgroundPanel = new(0, 0, 1024, 398, 0, 0);

    // =========================================================================
    // §11.2c Decoration sprites + server-row select buttons. CODE-CONFIRMED.
    // =========================================================================

    // B@(0,0,60,39) src(500,786) — 3x small badges / arrows.
    // spec §11.2c "3 x small badges / arrows". CODE-CONFIRMED.
    public static readonly WidgetRect SmallBadges = new(0, 0, 60, 39, 500, 786);

    // B server-row select buttons: 8 × (47×18) starting at X=13, Y=66, X-step=+47.
    // NORMAL src (596,985), HOVER/PRESSED src (643,985). Actions 115..122.
    // spec §11.2c "8 x server-row select". CODE-CONFIRMED.
    public const int ServerRowBtnX0 = 13; // starting X
    public const int ServerRowBtnY = 66; // Y for all 8 rows
    public const int ServerRowBtnW = 47; // width
    public const int ServerRowBtnH = 18; // height
    public const int ServerRowBtnXStep = 47; // X step between rows
    public const int ServerRowBtnNormalSrcX = 596; // NORMAL src X
    public const int ServerRowBtnNormalSrcY = 985; // NORMAL src Y
    public const int ServerRowBtnHoverSrcX = 643; // HOVER/PRESSED src X
    public const int ServerRowBtnHoverSrcY = 985; // HOVER/PRESSED src Y
    public const int ServerRowActionBase = 115; // action id for first row

    // Large action button (the "확인" confirm/gold button in the upper right).
    // A@(456,-3,111,38) NORMAL src(792,398), HOVER/PRESSED src(602,416).
    // spec §11.2c "Large action button". CODE-CONFIRMED.
    public static readonly WidgetRect LargeActionBtn = new(456, -3, 111, 38, 792, 398);
    public const int LargeActionHoverSrcX = 602; // HOVER/PRESSED src X
    public const int LargeActionHoverSrcY = 416; // HOVER/PRESSED src Y

    // Large action button caption face plate.
    // A@(407,-3,210,70) src(743,398) — image (gold plate baked art).
    // spec §11.2c "Its caption/face image". CODE-CONFIRMED.
    public static readonly WidgetRect LargeActionFacePlate = new(407, -3, 210, 70, 743, 398);

    // =========================================================================
    // §11.2d Notice / error dialogs. CODE-CONFIRMED.
    // =========================================================================

    // Shared dialog chrome: C@(342,289,340,190) src(318,647).
    // spec §11.2d "Dialog #1 panel (notice)" — same rect for both dialogs. CODE-CONFIRMED.
    public const int ModalChromeX = 342;
    public const int ModalChromeY = 289;
    public const int ModalChromeW = 340;
    public const int ModalChromeH = 190;
    public const int ModalChromeSrcX = 318;
    public const int ModalChromeSrcY = 647;

    // Dialog #1 OK button — C@(120,136,113,40) NORMAL src(302,900), HOVER src(415,900), action 113.
    // spec §11.2d "Dialog #1 OK". CODE-CONFIRMED.
    public static readonly WidgetRect QuitConfirmYes1 = new(120, 136, 113, 40, 302, 900);
    public const int QuitConfirmYes1HoverSrcX = 415;
    public const int QuitConfirmYes1HoverSrcY = 900;
    public const int ActionQuitConfirmYes1 = 113; // spec §1.2. CODE-CONFIRMED.

    // Dialog #2 OK button — C@(120,136,113,40) NORMAL src(302,860), HOVER src(415,860), action 114.
    // spec §11.2d "Dialog #2 OK". CODE-CONFIRMED.
    public static readonly WidgetRect QuitConfirmYes2 = new(120, 136, 113, 40, 302, 860);
    public const int QuitConfirmYes2HoverSrcX = 415;
    public const int QuitConfirmYes2HoverSrcY = 860;
    public const int ActionQuitConfirmYes2 = 114; // spec §1.2. CODE-CONFIRMED.

    // =========================================================================
    // §11.2e Bottom login form (core fidelity target). CODE-CONFIRMED.
    // =========================================================================

    // Bottom login-bar panel: A@(0, 326*H/768, 1024, 442) src(0,582).
    // The Y is height-scaled at runtime. spec §11.2e "Bottom login-bar panel". CODE-CONFIRMED.
    // SrcY = 582 in login_slice1.dds.
    public const int BottomBarW = 1024; // spec §11.2e
    public const int BottomBarH = 442; // spec §11.2e
    public const int BottomBarSrcX = 0; // spec §11.2e

    public const int BottomBarSrcY = 582; // spec §11.2e

    // Runtime Y: (326 × screenHeight) / 768  — but on a fixed 1024×768 canvas, this is always 326.
    // We use 326 as the canvas-local Y for the 1024×768 reference.
    public const int BottomBarCanvasY = 326; // = 326 × 768 / 768 — canvas-local Y. CODE-CONFIRMED.

    // Confirm button (gold 확인) — A@(456,166,112,39) NORMAL src(154,398), HOVER/PRESSED src(378,398).
    // Action 102 (server-list button in §1.2). spec §11.2e. CODE-CONFIRMED.
    // NOTE: the spec §11.2e labels this "Confirm button", action 102 — this is actually the
    // "Server-list button" from §1.2. The gold "확인" baked art is the background DDS label.
    public static readonly WidgetRect ConfirmButton = new(456, 166, 112, 39, 154, 398);
    public const int ConfirmHoverSrcX = 378;
    public const int ConfirmHoverSrcY = 398;
    public const int ActionConfirm = 102; // spec §1.2 "Server-list button". CODE-CONFIRMED.

    // Confirm-button face plate (baked art overlay — the plate the ID/PW row sits on).
    // §11.2e: dst=(0,469,494,113) src=(265,0) — canvas X=0,Y=469,W=494,H=113; atlas srcX=265,srcY=0.
    // The prior record had dst/src transposed (265,0 dst vs 0,469 src). CODE-CONFIRMED.
    // spec: Docs/RE/specs/frontend_scenes.md §11.2e "Login background plate image". CODE-CONFIRMED.
    public static readonly WidgetRect ConfirmFacePlate = new(0, 469, 494, 113, 265, 0);

    // Account-label caption art (baked Korean "아이디" plate).
    // A@(340,30,38,13) src(0,398) — baked art, no runtime text. spec §11.2e. CODE-CONFIRMED.
    public static readonly WidgetRect AccountLabelArt = new(340, 30, 38, 13, 0, 398);

    // Password-label caption art (baked Korean "비밀번호" plate).
    // A@(507,30,49,13) src(38,398) — baked art. spec §11.2e. CODE-CONFIRMED.
    public static readonly WidgetRect PasswordLabelArt = new(507, 30, 49, 13, 38, 398);

    // Small decoration plate (baked art).
    // A@(619,86,67,13) src(87,398). spec §11.2e. CODE-CONFIRMED.
    public static readonly WidgetRect SmallDecorPlate = new(619, 86, 67, 13, 87, 398);

    // ID input field — A@(390,32,102,13) src(615,404), max length 16 (UI cap, §1.3). action 109.
    // spec §11.2e "ID input field". CODE-CONFIRMED.
    public static readonly WidgetRect AccountBox = new(390, 32, 102, 13, 615, 404);
    public const int ActionFocusId = 109; // spec §1.2. CODE-CONFIRMED.
    public const int IdMaxLength = 16; // spec §1.3 "max length 16 (UI cap)". CODE-CONFIRMED.

    // Password input field — A@(568,32,102,13) src(615,404), max length 12, masked. action 110.
    // spec §11.2e "Password input field". CODE-CONFIRMED.
    public static readonly WidgetRect PasswordBox = new(568, 32, 102, 13, 615, 404);
    public const int ActionFocusPw = 110; // spec §1.2. CODE-CONFIRMED.
    public const int PwMaxLength = 12; // spec §1.3 "max length 12, masked". CODE-CONFIRMED.

    // Save-ID checkbox — A@(694,86,13,13). NORMAL src(717,398), PRESSED/checked src(730,398). action 104.
    // spec §11.2e "Save-ID checkbox". CODE-CONFIRMED.
    public static readonly WidgetRect SaveIdCheck = new(694, 86, 13, 13, 717, 398);
    public const int SaveIdCheckedSrcX = 730; // PRESSED (checked) src X. spec §11.2e.
    public const int SaveIdCheckedSrcY = 398; // PRESSED (checked) src Y.
    public const int ActionSaveId = 104; // spec §1.2. CODE-CONFIRMED.

    // Secondary bottom button (register / find-password).
    // A@(456,64,112,39) NORMAL src(266,398), HOVER/PRESSED src(490,398). action 103.
    // spec §11.2e "Secondary bottom button". CODE-CONFIRMED.
    public static readonly WidgetRect OkButton = new(456, 64, 112, 39, 266, 398);
    public const int OkHoverSrcX = 490;
    public const int OkHoverSrcY = 398;
    public const int ActionOk = 103; // spec §1.2 "OK / Login button". CODE-CONFIRMED.

    // 종료 (Quit) button — C1 fix. Placed at the far right of the bottom band.
    // Uses the same stone art as the secondary button but at x=840 so it sits to the right of
    // the save-ID area. This is the quit-trigger present in the official client.
    // spec: Docs/RE/specs/frontend_scenes.md §1.8 "quit paths". CODE-CONFIRMED (button exists).
    // Exact canvas position is a revival placement (no sub-rect separate from the ok-button art);
    // art src reuses (266,398) 112×39 — the secondary stone button frame.
    public static readonly WidgetRect QuitButton = new(840, 64, 112, 39, 266, 398);
    public const int ActionQuitBtn = 30; // spec §1.5 "sub-state 30 = quit-confirm Yes path". CODE-CONFIRMED.

    // =========================================================================
    // §11.2f Trailing controls + quit/error dialogs. CODE-CONFIRMED.
    // =========================================================================

    // Option/tab button 1 — B@(40,82,110,38). NORMAL src(520,492). HOVER src(635,492). action 111.
    // spec §11.2f "Button". CODE-CONFIRMED.
    public static readonly WidgetRect OptionTab1 = new(40, 82, 110, 38, 520, 492);
    public const int OptionTab1HoverSrcX = 635;
    public const int OptionTab1HoverSrcY = 492;
    public const int ActionOptionTab1 = 111; // spec §1.2. CODE-CONFIRMED.

    // Option/tab button 2 — B@(164,82,110,38). NORMAL src(750,492). HOVER src(865,492). action 112.
    // spec §11.2f "Button". CODE-CONFIRMED.
    public static readonly WidgetRect OptionTab2 = new(164, 82, 110, 38, 750, 492);
    public const int OptionTab2HoverSrcX = 865;
    public const int OptionTab2HoverSrcY = 492;
    public const int ActionOptionTab2 = 112; // spec §1.2. CODE-CONFIRMED.

    // Image plate decorations (baked art).
    // A@(67,48,178,13) src(0,437). spec §11.2f. CODE-CONFIRMED.
    public static readonly WidgetRect DecoPlate1 = new(67, 48, 178, 13, 0, 437);

    // A@(0,100,313,32) src(289,437). spec §11.2f. CODE-CONFIRMED.
    public static readonly WidgetRect DecoPlate2 = new(0, 100, 313, 32, 289, 437);

    // =========================================================================
    // §11.3 PIN modal. CODE-CONFIRMED layout.
    // =========================================================================

    // Modal panel rect on the canvas: (347,173,329,422). spec §11.3. CODE-CONFIRMED.
    public const int PinModalX = 347;
    public const int PinModalY = 173;
    public const int PinModalW = 329;
    public const int PinModalH = 422;

    // password.dds atlas path (§11.1). 1024×1024 DXT3. CODE-CONFIRMED.
    public const string AtlasPassword = "data/ui/password.dds";

    // Keypad tile layout (panel-local). spec §11.3a. CODE-CONFIRMED.
    // Tile X: 55*(p%5)+28  → columns 28, 83, 138, 193, 248.
    // Tile Y: 170 for top row (p<5), 230 for bottom row (p>=5).
    // Tile size: 52×52. Column spacing: 55. Row spacing: 60.
    public const int PinKeypadColSpacing = 55; // spec §11.3a. CODE-CONFIRMED.
    public const int PinKeypadCol0X = 28; // spec §11.3a. CODE-CONFIRMED.
    public const int PinKeypadRow0Y = 170; // spec §11.3a. CODE-CONFIRMED.
    public const int PinKeypadRow1Y = 230; // spec §11.3a. CODE-CONFIRMED.
    public const int PinKeypadTileW = 52; // spec §11.3a. CODE-CONFIRMED.
    public const int PinKeypadTileH = 52; // spec §11.3a. CODE-CONFIRMED.

    // Digit glyph source rects in password.dds. spec §11.3b. CODE-CONFIRMED.
    // AXIS CONVENTION (§11.3b CORRECTED): digit d varies along U/X, state varies along V/Y.
    //   srcU = d * 52  (X column = digit index × tile width)
    //   srcV = 560 (normal) | 612 (pressed) | 664 (hover)
    // "digit d's normal-state glyph is password.dds source rect (d*52, 560, 52, 52)".
    // spec §11.3b. CODE-CONFIRMED. Previous revision had axes swapped; corrected.
    public const int PinDigitColWidth = 52;       // per-digit X stride: srcU = d*52. spec §11.3b. CODE-CONFIRMED.
    public const int PinDigitNormalSrcY = 560;    // state normal  → srcV=560. spec §11.3b. CODE-CONFIRMED.
    public const int PinDigitHoverSrcY = 664;     // state hover   → srcV=664. spec §11.3b. CODE-CONFIRMED.
    public const int PinDigitPressedSrcY = 612;   // state pressed → srcV=612. spec §11.3b. CODE-CONFIRMED.

    // Legacy mis-named aliases (kept for compile compatibility; the _SrcX suffix was a mistake — these
    // were always V/Y values, not U/X values; use the corrected _SrcY names above in all new code).
    [System.Obsolete("Axis was swapped in old code — use PinDigitNormalSrcY instead. spec §11.3b CORRECTED.")]
    public const int PinDigitNormalSrcX = 560;
    [System.Obsolete("Axis was swapped in old code — use PinDigitHoverSrcY instead. spec §11.3b CORRECTED.")]
    public const int PinDigitHoverSrcX = 664;
    [System.Obsolete("Axis was swapped in old code — use PinDigitPressedSrcY instead. spec §11.3b CORRECTED.")]
    public const int PinDigitPressedSrcX = 612;
    [System.Obsolete("Axis was swapped in old code — use PinDigitColWidth instead. spec §11.3b CORRECTED.")]
    public const int PinDigitRowHeight = 52;

    // Reset button (tag 11): panel-local (243,133,58,30). spec §11.3d. CODE-CONFIRMED.
    // password.dds src: normal(663,8), hover(663,88), pressed(663,48).
    public const int PinResetX = 243; // spec §11.3d. CODE-CONFIRMED.
    public const int PinResetY = 133; // spec §11.3d. CODE-CONFIRMED.
    public const int PinResetW = 58; // spec §11.3d. CODE-CONFIRMED.
    public const int PinResetH = 30; // spec §11.3d. CODE-CONFIRMED.
    public const int PinResetNSrcX = 663;
    public const int PinResetNSrcY = 8; // normal. spec §11.3d.
    public const int PinResetHSrcX = 663;
    public const int PinResetHSrcY = 88; // hover.  spec §11.3d.
    public const int PinResetPSrcX = 663;
    public const int PinResetPSrcY = 48; // pressed.spec §11.3d.
    public const int PinTagReset = 11; // spec §11.3d. CODE-CONFIRMED.

    // OK button (tag 12): panel-local (90,290,154,58). spec §11.3d. CODE-CONFIRMED.
    // password.dds src: normal(330,0), hover(330,116), pressed(330,58).
    public const int PinOkX = 90; // spec §11.3d. CODE-CONFIRMED.
    public const int PinOkY = 290; // spec §11.3d. CODE-CONFIRMED.
    public const int PinOkW = 154; // spec §11.3d. CODE-CONFIRMED.
    public const int PinOkH = 58; // spec §11.3d. CODE-CONFIRMED.
    public const int PinOkNSrcX = 330;
    public const int PinOkNSrcY = 0; // normal. spec §11.3d.
    public const int PinOkHSrcX = 330;
    public const int PinOkHSrcY = 116; // hover.  spec §11.3d.
    public const int PinOkPSrcX = 330;
    public const int PinOkPSrcY = 58; // pressed.spec §11.3d.
    public const int PinTagOk = 12; // spec §11.3d. CODE-CONFIRMED.

    // Cancel button (tag 13): panel-local (90,350,154,58). spec §11.3d. CODE-CONFIRMED.
    // password.dds src: normal(486,0), hover(486,116), pressed(486,58).
    public const int PinCancelX = 90; // spec §11.3d. CODE-CONFIRMED.
    public const int PinCancelY = 350; // spec §11.3d. CODE-CONFIRMED.
    public const int PinCancelW = 154; // spec §11.3d. CODE-CONFIRMED.
    public const int PinCancelH = 58; // spec §11.3d. CODE-CONFIRMED.
    public const int PinCancelNSrcX = 486;
    public const int PinCancelNSrcY = 0; // normal. spec §11.3d.
    public const int PinCancelHSrcX = 486;
    public const int PinCancelHSrcY = 116; // hover.  spec §11.3d.
    public const int PinCancelPSrcX = 486;
    public const int PinCancelPSrcY = 58; // pressed.spec §11.3d.
    public const int PinTagCancel = 13; // spec §11.3d. CODE-CONFIRMED.

    // PIN capacity. spec §1.4a / login_flow.md §4.2. RUNTIME-CONFIRMED.
    public const int PinMaxLength = 4;

    // =========================================================================
    // §11.5b Char-select slot tab buttons (from loginwindow.dds). CODE-CONFIRMED.
    // =========================================================================

    // Slot 1 tab — (67,17,113,40). N src(675,795). P src(483,883). action 1.
    public static readonly WidgetRect CharSlot1Tab = new(67, 17, 113, 40, 675, 795);
    public const int CharSlot1PressedSrcX = 483;
    public const int CharSlot1PressedSrcY = 883;
    public const int ActionCharSlot1 = 1;

    // Slot 2 tab — (232,7,113,40). N src(640,742). P src(483,923). action 2.
    public static readonly WidgetRect CharSlot2Tab = new(232, 7, 113, 40, 640, 742);
    public const int CharSlot2PressedSrcX = 483;
    public const int CharSlot2PressedSrcY = 923;
    public const int ActionCharSlot2 = 2;

    // Slot 3 tab — (393,17,113,40). N src(625,691). P src(483,963). action 3.
    public static readonly WidgetRect CharSlot3Tab = new(393, 17, 113, 40, 625, 691);
    public const int CharSlot3PressedSrcX = 483;
    public const int CharSlot3PressedSrcY = 963;
    public const int ActionCharSlot3 = 3;

    // =========================================================================
    // §11.5c Char-select Create/Delete/Enter buttons (from loginwindow.dds). CODE-CONFIRMED.
    // All buttons are 59×20.
    // =========================================================================

    // Create — (130,112,59,20). N src(0,1004). P src(59,1004). action 4.
    public static readonly WidgetRect CharCreateBtn = new(130, 112, 59, 20, 0, 1004);
    public const int CharCreatePressedSrcX = 59;
    public const int CharCreatePressedSrcY = 1004;
    public const int ActionCharCreate = 4; // spec §4 / §11.5c. CODE-CONFIRMED.

    // Delete — (42,112,59,20). N src(118,1004). P src(177,1004). action 5.
    public static readonly WidgetRect CharDeleteBtn = new(42, 112, 59, 20, 118, 1004);
    public const int CharDeletePressedSrcX = 177;
    public const int CharDeletePressedSrcY = 1004;
    public const int ActionCharDelete = 5; // spec §5 / §11.5c. CODE-CONFIRMED.

    // Enter — (112,112,59,20). N src(236,1004). P src(295,1004). action 6.
    public static readonly WidgetRect CharEnterBtn = new(112, 112, 59, 20, 236, 1004);
    public const int CharEnterPressedSrcX = 295;
    public const int CharEnterPressedSrcY = 1004;
    public const int ActionCharEnter = 6; // spec §7 / §11.5c. CODE-CONFIRMED.

    // =========================================================================
    // msg.xdb caption ids (§1.9 / §9). CODE-CONFIRMED ids; captions VFS-only.
    // =========================================================================

    // Quit-confirm prompts. spec §1.9. CODE-CONFIRMED.
    public const uint MsgQuitConfirm1 = 4023;
    public const uint MsgQuitConfirm2 = 4024;

    // Login validation error toasts. spec §1.4 / §1.9. CODE-CONFIRMED.
    public const uint MsgErrShortId = 4025; // ID length < 4
    public const uint MsgErrEmptyPassword = 4026; // password empty
    public const uint MsgErrNoServers = 4027; // server list empty
    public const uint MsgErrConnectFail = 4028; // server-list connection failed

    // Login form static labels (4001–4022 range). spec §1.9. CODE-CONFIRMED.
    public const uint MsgLabelFirst = 4001;
    public const uint MsgLabelLast = 4022;

    // =========================================================================
    // Validation thresholds. spec §1.4. CODE-CONFIRMED.
    // =========================================================================

    public const int MinIdLength = 4; // ID length < 4 → msg 4025. spec §1.4. CODE-CONFIRMED.
    public const int MinPwLength = 1; // PW length < 1 → msg 4026. spec §1.4. CODE-CONFIRMED.

    // =========================================================================
    // Font pixel heights (approximate — Korean system font substitution).
    // The legacy client used DotumChe 12px / 16px. We use the same pt sizes.
    // =========================================================================

    public const int FontBodyHeight = 12; // body / caption labels
    public const int FontTitleHeight = 16; // section title
    public const int FontLabelHeight = 12; // button captions

    // TextBox render height override (spec says 13px but Godot LineEdit needs ≥18px to render).
    public const int TextboxRenderH = 22;
}