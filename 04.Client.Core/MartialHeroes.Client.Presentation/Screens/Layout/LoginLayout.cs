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

namespace MartialHeroes.Client.Presentation.Screens.Layout;

/// <summary>
///     A widget rectangle + atlas source sub-rect (NORMAL frame) on the reference canvas.
///     <c>X/Y/W/H</c> are screen-local pixels (canvas-local, §11.0);
///     <c>SrcX/SrcY</c> are the top-left pixel of the NORMAL sprite in the atlas DDS.
///     Hover and pressed source origins are stored as separate constants.
/// </summary>
public readonly record struct WidgetRect(int X, int Y, int W, int H, int SrcX, int SrcY);

/// <summary>
///     Login-scene layout constants.
///     spec: Docs/RE/specs/frontend_scenes.md §11.2 (CODE-CONFIRMED literals).
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

    // D — server-select plate icons + select buttons.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "A4 = loginwindow_02.dds". CODE-CONFIRMED.
    public const string AtlasLoginWindow02 = "data/ui/loginwindow_02.dds";

    // =========================================================================
    // §4 Server-list status-color indicator quads
    // spec: Docs/RE/specs/frontend_layout_tables.md §4
    // =========================================================================

    // Three status-color indicator quads: A2 (loginwindow.dds) src(500,786) 60×39.
    // Hidden by default; re-anchored around a status==100 special row when present.
    // spec: Docs/RE/specs/frontend_layout_tables.md §4 "status-color indicator quads ×3"
    public const int StatusIndicatorSrcX = 500; // spec: frontend_layout_tables.md §4
    public const int StatusIndicatorSrcY = 786; // spec: frontend_layout_tables.md §4
    public const int StatusIndicatorW = 60; // spec: frontend_layout_tables.md §4
    public const int StatusIndicatorH = 39; // spec: frontend_layout_tables.md §4
    public const int StatusIndicatorCount = 3; // spec: frontend_layout_tables.md §4
    public const int ActionScrollUp = 106; // spec: frontend_scenes.md §11.2a.
    public const int ActionScrollDown = 107; // spec: frontend_scenes.md §11.2a.
    public const int ActionScrollThumb = 108; // spec: frontend_scenes.md §11.2a.

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

    // ExitPanel prompt area (not spec-confirmed; retained for the ExitPanel only).
    public const int ModalPromptX = 32;
    public const int ModalPromptY = 42;
    public const int ModalPromptW = 276;
    public const int ModalPromptH = 70;

    // Confirm-A/B label position: panel-local (10,100,330,20) per §2.1.
    // spec: Docs/RE/specs/frontend_layout_tables.md §2.1 "Confirm-A label (msg 4023) | label center | 10 | 100 | 330 | 20"
    public const int ConfirmLabelX = 10; // spec: frontend_layout_tables.md §2.1
    public const int ConfirmLabelY = 100; // spec: frontend_layout_tables.md §2.1
    public const int ConfirmLabelW = 330; // spec: frontend_layout_tables.md §2.1
    public const int ConfirmLabelH = 20; // spec: frontend_layout_tables.md §2.1
    public const int QuitConfirmYes1HoverSrcX = 415;
    public const int QuitConfirmYes1HoverSrcY = 900;
    public const int ActionQuitConfirmYes1 = 113; // spec §1.2. CODE-CONFIRMED.
    public const int QuitConfirmYes2HoverSrcX = 415;
    public const int QuitConfirmYes2HoverSrcY = 860;
    public const int ActionQuitConfirmYes2 = 114; // spec §1.2. CODE-CONFIRMED.

    // =========================================================================
    // §11.2e Bottom login form (core fidelity target). CODE-CONFIRMED.
    // =========================================================================

    // Bottom login-bar panel: A@(0, 326*H/768, 1024, 442) src(0,582).
    // The Y is height-scaled at runtime. spec: frontend_scenes.md §11.2b.
    // SrcY = 582 in login_slice1.dds.
    public const int BottomBarW = 1024; // spec §11.2e
    public const int BottomBarH = 442; // spec §11.2e
    public const int BottomBarSrcX = 0; // spec §11.2e

    public const int BottomBarSrcY = 582; // spec §11.2e

    // Runtime Y: (326 × screenHeight) / 768  — but on a fixed 1024×768 canvas, this is always 326.
    // We use 326 as the canvas-local Y for the 1024×768 reference.
    public const int BottomBarCanvasY = 326; // = 326 × 768 / 768 — canvas-local Y. CODE-CONFIRMED.
    public const int ConfirmHoverSrcX = 378;
    public const int ConfirmHoverSrcY = 398;
    public const int ActionConfirm = 102; // spec §1.2 "Server-list button". CODE-CONFIRMED.

    // ATLAS CORRECTION (§11.2e, IDA pass 2026-06-14):
    // The edit-field frames sample ATLAS A (login_slice1.dds), NOT loginwindow.dds.
    // Both ID and PW fields share a single source rect in atlas A at src(615,404,102,13).
    // The numbers (390,32) and (568,32) in the widget table are DESTINATION origins on the canvas,
    // not source UV coordinates. The spec note at §11.2e "Atlas note" originally said loginwindow.dds
    // — the IDA pass corrected this to login_slice1.dds. spec: Docs/RE/specs/frontend_scenes.md §11.2e.
    public const string EditFieldFrameAtlas = AtlasLoginSlice1; // A — spec §11.2e atlas correction. CODE-CONFIRMED.
    public const int EditFieldFrameSrcX = 615; // shared source U in atlas A. spec §11.2e. CODE-CONFIRMED.
    public const int EditFieldFrameSrcY = 404; // shared source V in atlas A. spec §11.2e. CODE-CONFIRMED.

    public const int
        IdMaxLength =
            19; // spec §2.6 "account < 20" — the input handler enforces account length < 20, so max typed chars = 19. CODE-CONFIRMED.

    public const int
        PwMaxLength =
            17; // spec: frontend_layout_tables.md §2.6 "password = 17". CORRECTED from 129 (was wrong). CODE-CONFIRMED.

    public const int SaveIdCheckedSrcX = 730; // PRESSED (checked) src X. spec §11.2e.
    public const int SaveIdCheckedSrcY = 398; // PRESSED (checked) src Y.
    public const int ActionSaveId = 104; // spec §1.2. CODE-CONFIRMED.
    public const int OkHoverSrcX = 490;
    public const int OkHoverSrcY = 398;
    public const int ActionOk = 103; // spec §1.2 "OK / Login button". CODE-CONFIRMED.

    // App quit — fired by the ExitPanel "yes". spec: frontend_layout_tables.md §2.2 (binary case 101 →
    // Engine_ClearRunFlag). CODE-CONFIRMED vs binary 2026-06-18.
    public const int ActionAppQuit = 101;

    // Quit-confirm (ExitPanel) caption + Yes/No actions. The ExitPanel is opened by action 102/112 and
    // its "yes" fires 101 (quit). spec: frontend_layout_tables.md §2.2. CODE-CONFIRMED vs binary.
    public const uint MsgExitConfirm = 2007;
    public const int QuitHoverSrcX = 602; // HOVER/PRESSED src X. CODE-CONFIRMED (binary).
    public const int QuitHoverSrcY = 416; // HOVER/PRESSED src Y. CODE-CONFIRMED (binary).
    public const int OptionTab1HoverSrcX = 635;
    public const int OptionTab1HoverSrcY = 492;
    public const int ActionOptionTab1 = 111; // spec §1.2. CODE-CONFIRMED.
    public const int OptionTab2HoverSrcX = 865;
    public const int OptionTab2HoverSrcY = 492;
    public const int ActionOptionTab2 = 112; // spec §1.2. CODE-CONFIRMED.

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
    public const int PinDigitColWidth = 52; // per-digit X stride: srcU = d*52. spec §11.3b. CODE-CONFIRMED.

    public const int
        PinDigitNormalSrcY = 560; // state normal  → srcV=560. spec: frontend_layout_tables.md §3. CODE-CONFIRMED.

    public const int
        PinDigitHoverSrcY =
            612; // state hover   → srcV=612. spec: frontend_layout_tables.md §3 (CORRECTED — construct call order: NORMAL=560, PRESSED=664, HOVER=612).

    public const int
        PinDigitPressedSrcY =
            664; // state pressed → srcV=664. spec: frontend_layout_tables.md §3 (CORRECTED — was 612, which was the hover band).

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

    // PIN capacity. spec §1.4a / login_flow.md §4.2. RUNTIME-CONFIRMED.
    public const int PinMaxLength = 4;

    // =========================================================================
    // msg.xdb caption ids (§1.9 / §9). CODE-CONFIRMED ids; captions VFS-only.
    // =========================================================================

    // Version-gate error box (shown before credential checks). spec §1.4 / §1.8 / §1.9. CODE-CONFIRMED.
    public const uint MsgVersionMismatch = 2204; // game.ver mismatch → show this, then quit. spec §1.4. CODE-CONFIRMED.

    // Quit-confirm prompts. spec §1.9. CODE-CONFIRMED.
    // NOTE (A1 IDA, §1.8): 4023/4024 + buttons 113/114 are actually the SERVER-LIST RE-FETCH popup
    // (advance to tick substate 34), NOT the client-quit confirm. The genuine quit-confirm is the
    // shared ExitPanel (caption msg 2007, Yes=action 50 / No=action 51). At the login GameState the
    // original ExitPanel "Yes" is INERT (no GameState-1 case → real quit was the OS window-close edge,
    // debugger-pending).
    public const uint MsgQuitConfirm1 = 4023; // server-list re-fetch popup #1 (NOT quit). spec §1.8.
    public const uint MsgQuitConfirm2 = 4024; // server-list re-fetch popup #2 (NOT quit). spec §1.8.

    // Login validation error toasts. spec §1.4 / §1.9. CODE-CONFIRMED.
    public const uint MsgErrShortId = 4025; // ID length < 4
    public const uint MsgErrEmptyPassword = 4026; // password empty
    public const uint MsgErrNoServers = 4027; // server list empty
    public const uint MsgErrConnectFail = 4028; // server-list connection failed
    public const uint MsgServerHeaderFirst = 4029; // server-list column/status captions start
    public const uint MsgServerHeaderLast = 4032; // server-list column/status captions end
    public const uint MsgServerUnknown = 5901; // server id outside 1..40 formatted fallback
    public const uint MsgServerLoadRed = 6001; // > 1200
    public const uint MsgServerLoadOrange = 6002; // > 800
    public const uint MsgServerLoadYellow = 6003; // > 500
    public const uint MsgServerPreparing = 6004; // scheduled-open preparing caption
    public const uint MsgServerClockFormat = 6005; // scheduled-open HH:MM format

    // Login form static labels (4001–4022 range). spec §1.9. CODE-CONFIRMED.
    public const uint MsgLabelFirst = 4001;
    public const uint MsgLabelLast = 4022;

    // Central notice/agreement panel body labels — spec: frontend_scenes.md §11.2/§11.10:
    // 22 GULabels added as CHILDREN of the central panel (= ServerListbox frame at dst(270,85,483,490)
    // src(0,490) from loginwindow.dds). Each label is built at PANEL-LOCAL dst(50, 100+18·k, 383, 50),
    // k=0..21, text = msg.xdb (4001+k). The earlier port dumped these at far-left absolute (40,120+14·k)
    // on the scene root — that is the source of the "everything stacked in the corner" disorder.
    // Re-verified vs the binary 2026-06-17. spec §11.2a (panel) / §1.9 (text). CODE-CONFIRMED (binary).
    public const int NoticeLabelLocalX = 50; // panel-local X. CODE-CONFIRMED (binary).
    public const int NoticeLabelStartY = 100; // panel-local Y of first row. CODE-CONFIRMED (binary).
    public const int NoticeLabelStrideY = 18; // per-row Y advance. CODE-CONFIRMED (binary).
    public const int NoticeLabelW = 383; // label width. CODE-CONFIRMED (binary).
    public const int NoticeLabelH = 50; // label height. CODE-CONFIRMED (binary).

    // =========================================================================
    // Save-ID persistence (§1.6, CODE-CONFIRMED). Layer-05 only — Godot ConfigFile.
    // Equivalent of DoOption.ini [DO_OPTION] OPTION_ID in the legacy client.
    // spec: Docs/RE/specs/frontend_scenes.md §1.6. CODE-CONFIRMED.
    // =========================================================================

    public const string SaveIdConfigPath = "user://mh_options.cfg"; // layer-05 ConfigFile path.
    public const string SaveIdSection = "DO_OPTION"; // spec §1.6 — INI section. CODE-CONFIRMED.
    public const string SaveIdKey = "OPTION_ID"; // spec §1.6 — INI key. CODE-CONFIRMED.
    public const string SaveIdNullSentinel = "(null)"; // sentinel = no saved id. spec §1.6. CODE-CONFIRMED.

    // =========================================================================
    // Validation thresholds. spec §1.4. CODE-CONFIRMED.
    // =========================================================================

    public const int MinIdLength = 4; // ID length < 4 → msg 4025. spec §1.4. CODE-CONFIRMED.
    public const int MinPwLength = 1; // PW length < 1 → msg 4026. spec §1.4. CODE-CONFIRMED.

    // =========================================================================
    // §11.2a Upper window — main panel, server listbox, scroll controls. CODE-CONFIRMED.
    // Source: frontend_scenes.md §11.2a.
    // =========================================================================

    // B@(0,110,1024,490) src(0,0) — main background panel art.
    // spec: frontend_scenes.md §11.2a "Backdrop image". CODE-CONFIRMED.
    public static readonly WidgetRect MainPanel = new(0, 110, 1024, 490, 0, 0);

    // B@(270,85,483,490) src(0,490) — server dropdown / listbox container.
    // spec: frontend_scenes.md §11.2a "Centre notice PANEL". CODE-CONFIRMED.
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
    // §11.2b Background + bottom bar. CODE-CONFIRMED.
    // Source: frontend_scenes.md §11.2b.
    // =========================================================================

    // A@(0,0,1024,398) — full background art panel (below the form band).
    // spec §11.2b "Full background art panel". CODE-CONFIRMED.
    public static readonly WidgetRect BackgroundPanel = new(0, 0, 1024, 398, 0, 0);

    // =========================================================================
    // §11.2c Decoration sprites. CODE-CONFIRMED.
    // =========================================================================

    // B@(0,0,60,39) src(500,786) — 3x small badges / arrows.
    // spec §11.2c "3 x small badges / arrows". CODE-CONFIRMED.
    public static readonly WidgetRect SmallBadges = new(0, 0, 60, 39, 500, 786);

    // Dialog #1 OK button — C@(120,136,113,40) NORMAL src(302,900), HOVER src(415,900), action 113.
    // spec §11.2d "Dialog #1 OK". CODE-CONFIRMED.
    public static readonly WidgetRect QuitConfirmYes1 = new(120, 136, 113, 40, 302, 900);

    // Dialog #2 OK button — C@(120,136,113,40) NORMAL src(302,860), HOVER src(415,860), action 114.
    // spec §11.2d "Dialog #2 OK". CODE-CONFIRMED.
    public static readonly WidgetRect QuitConfirmYes2 = new(120, 136, 113, 40, 302, 860);

    // Confirm button (gold 확인) — A@(456,166,112,39) NORMAL src(154,398), HOVER/PRESSED src(378,398).
    // Action 102 (server-list button in §1.2). spec §11.2e. CODE-CONFIRMED.
    // NOTE: the spec §11.2e labels this "Confirm button", action 102 — this is actually the
    // "Server-list button" from §1.2. The gold "확인" baked art is the background DDS label.
    public static readonly WidgetRect ConfirmButton = new(456, 166, 112, 39, 154, 398);

    // Confirm-button face plate (baked art overlay — the plate the ID/PW row sits on).
    // spec: frontend_scenes.md §11.2e — login_slice1 confirm-plate dst(265,0,494,113) src(0,469).
    // i.e. canvas dst X=265,Y=0,W=494,H=113; atlas srcX=0,srcY=469. (A 2026-06-14 pass had transposed
    // these — re-corrected from the binary 2026-06-17.) spec §11.2e. CODE-CONFIRMED (re-verified vs binary).
    public static readonly WidgetRect ConfirmFacePlate = new(265, 0, 494, 113, 0, 469);

    // Account-label caption art (baked Korean "아이디" plate).
    // A@(340,30,38,13) src(0,398) — baked art, no runtime text. spec §11.2e. CODE-CONFIRMED.
    public static readonly WidgetRect AccountLabelArt = new(340, 30, 38, 13, 0, 398);

    // Password-label caption art (baked Korean "비밀번호" plate).
    // A@(507,30,49,13) src(38,398) — baked art. spec §11.2e. CODE-CONFIRMED.
    public static readonly WidgetRect PasswordLabelArt = new(507, 30, 49, 13, 38, 398);

    // Small decoration plate (baked art).
    // A@(619,86,67,13) src(87,398). spec §11.2e. CODE-CONFIRMED.
    public static readonly WidgetRect SmallDecorPlate = new(619, 86, 67, 13, 87, 398);

    // ID input field — dest (390,32,102,13); frame src = AtlasLoginSlice1 (615,404). max length 19. action 109.
    // NOTE: SrcX/SrcY on this WidgetRect are intentionally set to the shared edit-field frame source,
    // NOT to the dest origin; and the atlas is EditFieldFrameAtlas (A), not AtlasLoginWindow (B).
    // spec §11.2e "ID input field atlas correction". CODE-CONFIRMED.
    // NOTE: §2.6 "account < 20" — the binary caps at 19 typed chars at the input-handler hand-off.
    public static readonly WidgetRect AccountBox = new(390, 32, 102, 13, EditFieldFrameSrcX, EditFieldFrameSrcY);

    // Password input field — dest (568,32,102,13); frame src = AtlasLoginSlice1 (615,404). max length 17, masked. action 110.
    // Same shared source rect as ID field (both at login_slice1.dds src 615,404). spec §11.2e. CODE-CONFIRMED.
    // NOTE: §1.3 lists IME composition slot 12 for PW; the effective cap is 17 (credential hand-off, §2.6).
    public static readonly WidgetRect PasswordBox = new(568, 32, 102, 13, EditFieldFrameSrcX, EditFieldFrameSrcY);

    // Save-ID checkbox — A@(694,86,13,13). NORMAL src(717,398), PRESSED/checked src(730,398). action 104.
    // spec §11.2e "Save-ID checkbox". CODE-CONFIRMED.
    public static readonly WidgetRect SaveIdCheck = new(694, 86, 13, 13, 717, 398);

    // Secondary bottom button (register / find-password).
    // A@(456,64,112,39) NORMAL src(266,398), HOVER/PRESSED src(490,398). action 103.
    // spec: frontend_scenes.md §11.2c "OK/Login BTN3". CODE-CONFIRMED.
    public static readonly WidgetRect OkButton = new(456, 64, 112, 39, 266, 398);

    // 종료/도움말 (Quit/Help) strip button — IDA: BuildButton3State(login_slice1, dst(456,-3,111,38),
    // N src(792,398), H src(602,416)), action 105. It sits flush to the top edge (Y=-3, clipped) as the
    // top-right help/quit strip — NOT the earlier far-right (840,64) placement. Re-verified vs the binary
    // 2026-06-17. spec: Docs/RE/specs/frontend_scenes.md §1.2 / §1.8. CODE-CONFIRMED (re-verified vs binary).
    public static readonly WidgetRect QuitButton = new(456, -3, 111, 38, 792, 398);

    // Quit/help strip decoration image behind the button — IDA: BuildImageComponent(login_slice1,
    // dst(407,-3,210,70), src(743,398)). spec §11.2f. CODE-CONFIRMED (binary).
    public static readonly WidgetRect QuitDecoPlate = new(407, -3, 210, 70, 743, 398);

    // =========================================================================
    // §11.2f Trailing controls + quit/error dialogs. CODE-CONFIRMED.
    // Source: frontend_scenes.md §11.2.
    // =========================================================================

    // Option/tab button 1 — B@(40,82,110,38). NORMAL src(520,492). HOVER src(635,492). action 111.
    // spec: frontend_scenes.md §11.2 action 111. CODE-CONFIRMED.
    public static readonly WidgetRect OptionTab1 = new(40, 82, 110, 38, 520, 492);

    // Option/tab button 2 — B@(164,82,110,38). NORMAL src(750,492). HOVER src(865,492). action 112.
    // spec: frontend_scenes.md §11.2 action 112. CODE-CONFIRMED.
    public static readonly WidgetRect OptionTab2 = new(164, 82, 110, 38, 750, 492);

    // Image plate decorations (baked art).
    // A@(67,48,178,13) src(0,437). spec: frontend_scenes.md §11.2.
    public static readonly WidgetRect DecoPlate1 = new(67, 48, 178, 13, 0, 437);

    // A@(0,100,313,32) src(289,437). spec: frontend_scenes.md §11.2.
    public static readonly WidgetRect DecoPlate2 = new(0, 100, 313, 32, 289, 437);
}