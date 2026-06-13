// Screens/Layout/LoginLayout.cs
//
// Hardcoded pixel layout table for the LOGIN screen, on the 1024×768 reference canvas.
// Every value here is an INTEROP FACT recovered from the login BuildScene routine.
// "Interop facts, not guesses." — each constant cites its spec row.
//
// spec: Docs/RE/specs/ui_system.md §8.0 (reference canvas),
//       §8.1 (login layout table — 21 ctor sites, CODE-CONFIRMED),
//       §9.1 (login asset manifest — atlas DDS assignments, CODE-CONFIRMED),
//       §6.2  (font slot pixel heights, font table),
//       §10  (msg.xdb id ranges, CODE-CONFIRMED).

namespace MartialHeroes.Client.Godot.Screens.Layout;

/// <summary>
/// A widget rectangle + atlas source sub-rect (NORMAL frame) on the reference canvas.
/// <c>X/Y/W/H</c> are screen-local pixels (spec §8.0); <c>SrcX/SrcY</c> are the atlas UV origin
/// of the widget's NORMAL sprite (spec §8.0 — "sub-rect is (SrcX,SrcY)..(SrcX+W,SrcY+H)").
/// Hover and pressed frame origins are stored as separate constants in <see cref="LoginLayout"/>.
/// </summary>
public readonly record struct WidgetRect(int X, int Y, int W, int H, int SrcX, int SrcY);

/// <summary>
/// Login-screen layout constants. spec: Docs/RE/specs/ui_system.md §8.1 (full 21-site table).
/// </summary>
public static class LoginLayout
{
    // --- Reference canvas. spec §8.0 — "Reference canvas: 1024 × 768 pixels". CODE-CONFIRMED. ---
    public const int RefWidth = 1024;
    public const int RefHeight = 768;

    // --- Login form band — the GUWindow root panel on the 1024×768 canvas.
    //     spec §8.1 — widget coordinates are *relative to their parent panel* (§8.0).
    //     The panel's absolute position on the canvas is not in the spec; the value 110 is a
    //     plausible prior estimate. Evidence: Quit/Help strip at y=−3 must still be on-screen,
    //     so BandTopY > 3; banner strip at y=97 panel-local → absolu = BandTopY+97; option tabs
    //     at y=82 panel-local. A panel top of 110 places the Quit strip at y=107 (just visible)
    //     and the banner at y=207, which fits the legacy screenshot appearance.
    //     // PLAUSIBLE offset (exact panel y not in spec — use BandTopY=110 as working approximation)
    //     spec: Docs/RE/specs/ui_system.md §8.1 — coords are panel-local; §8.0 — anchor semantics.
    public const int BandTopY = 110; // form panel top on the reference canvas — PLAUSIBLE, not in spec
    public const int BandHeight = 398; // spec §8.1 "Root backdrop panel" h=398

    // --- loginwindow_02.dds banner strip geometry.
    //     spec §8.1 site "Intro banner" BTN7 (—,97,202,372); x is register-fed (runtime animated).
    //     We use x=0 (panel-local left) as a static plausible approximation.
    //     Strip is added directly to LoginScreen (not the band) at canvas-absolute position.
    //     // PLAUSIBLE x=0 (exact runtime x not in spec)
    //     spec: Docs/RE/specs/ui_system.md §8.1 "Intro banner (—,97,202,372)". CODE-CONFIRMED dims.
    public const int BannerStripX = 0;        // PLAUSIBLE (register-fed x not in spec)
    public const int BannerStripLocalY = 97;  // spec §8.1 y=97 panel-local. CODE-CONFIRMED.
    public const int BannerStripW = 202;       // spec §8.1 w=202. CODE-CONFIRMED.
    public const int BannerStripH = 372;       // spec §8.1 h=372. CODE-CONFIRMED.

    // --- Form backing geometry — covers the widget cluster (x=380..810, y=−10..400 panel-local).
    //     No dedicated panel art was recovered; this is a plausible solid backing.
    //     Spec widget cluster: x range = 390..706 (AccountBox..SaveIdCheck+label), y range = −3..205.
    //     We pad slightly for visual comfort: x=370, y=−10, w=430, h=220.
    //     // PLAUSIBLE backing (art not in spec)
    public const int FormBackingX = 370;   // left edge, panel-local, covers form cluster
    public const int FormBackingY = -10;   // top edge, panel-local (Quit strip is at y=-3)
    public const int FormBackingW = 430;   // covers x=370..800 (ID/PW/OK/ServerList/SaveId)
    public const int FormBackingH = 220;   // covers y=-10..210 (all form widgets)

    // Form backing colour — parchment-tinted panel backing behind the form widget cluster.
    // Must clearly contrast with the full-canvas black backdrop (0.06, 0.05, 0.08).
    // We use a mid-tone brown (approximately #6B4A2A) that reads as parchment/wood panel and
    // provides enough contrast for button textures and text to be legible.
    // // PLAUSIBLE colour (no panel art in spec — legacy used a DDS chrome sub-rect not yet recovered)
    // global::Godot.Color avoids CS0234 namespace collision with MartialHeroes.Client.Godot.*
    // spec: CLAUDE.md "Known Godot Pitfalls — Namespace collision".
    public static readonly global::Godot.Color FormBackingColor = new(0.30f, 0.20f, 0.12f, 0.90f);

    // =========================================================================
    // Atlas path constants. spec §9.1 login asset manifest. CODE-CONFIRMED.
    // =========================================================================

    // Primary form atlas: OK/Login, Server-list, ID/PW textboxes, Save-ID checkbox, Quit/Help.
    // spec: Docs/RE/specs/ui_system.md §8.1 "Atlas assignment" table — CODE-CONFIRMED.
    public const string AtlasLoginSlice1 = "data/ui/login_slice1.dds";

    // Option/tab buttons, server name-strip, decorative elements.
    // spec: Docs/RE/specs/ui_system.md §9.1 CODE-CONFIRMED.
    public const string AtlasLoginWindow = "data/ui/loginwindow.dds";

    // Panning intro banner strip (backdrop hero art).
    // spec: Docs/RE/specs/ui_system.md §9.1 CODE-CONFIRMED.
    public const string AtlasLoginWindow02 = "data/ui/loginwindow_02.dds";

    // Shared popup/button chrome — quit-confirm modal.
    // spec: Docs/RE/specs/ui_system.md §8.3, §9.1 CODE-CONFIRMED.
    public const string AtlasInventWindow = "data/ui/inventwindow.dds";

    // =========================================================================
    // Widget rects — dest (x,y,w,h) + NORMAL atlas origin (srcX,srcY).
    // All coordinates are panel-local (relative to the form band origin).
    // spec: Docs/RE/specs/ui_system.md §8.1 table — CODE-CONFIRMED.
    // =========================================================================

    // --- ID / account textbox — @ (390,32) 102×13, src (615,404), IME slot 16, maxlen 6. ---
    // spec: §8.1 site "ID/account textbox"; atlas login_slice1.dds. CODE-CONFIRMED.
    public static readonly WidgetRect AccountBox = new(390, 32, 102, 13, 615, 404);

    // --- Password textbox — @ (568,32) 102×13, src (615,404), IME slot 12, maxlen 129, masked. ---
    // spec: §8.1 site "Password textbox"; atlas login_slice1.dds. CODE-CONFIRMED.
    public static readonly WidgetRect PasswordBox = new(568, 32, 102, 13, 615, 404);

    // --- Save-ID checkbox — @ (694,86) 13×13. ---
    //   NORMAL/unchecked src: (717,398)
    //   PRESSED/checked  src: (730,398)   (spec §8.1 — "checked state = PRESSED frame")
    //   atlas: login_slice1.dds.  ACTION: 104 (`h`). CODE-CONFIRMED.
    public static readonly WidgetRect SaveIdCheck = new(694, 86, 13, 13, 717, 398);
    public const int SaveIdCheckedSrcX = 730; // PRESSED (checked) frame atlas X
    public const int SaveIdCheckedSrcY = 398; // PRESSED (checked) frame atlas Y
    public const int ActionSaveId = 104; // spec §8.1 action table CODE-CONFIRMED

    // --- OK / Login button (7-state) — @ (456,64) 112×39. ---
    //   NORMAL  src: (266,398)
    //   HOVER   src: (490,398)
    //   PRESSED src: (490,398)   (HOVER == PRESSED per spec §8.1 table)
    //   atlas: login_slice1.dds.  ACTION: 103 (`g`). CODE-CONFIRMED.
    public static readonly WidgetRect OkButton = new(456, 64, 112, 39, 266, 398);
    public const int OkHoverSrcX = 490;
    public const int OkHoverSrcY = 398;
    public const int ActionOk = 103; // spec §8.1 action table CODE-CONFIRMED

    // --- Server-list button (7-state) — @ (456,166) 112×39. ---
    //   NORMAL  src: (154,398)
    //   HOVER   src: (378,398)
    //   PRESSED src: (378,398)   (HOVER == PRESSED per spec §8.1 table)
    //   atlas: login_slice1.dds.  ACTION: 102 (`f`). CODE-CONFIRMED.
    public static readonly WidgetRect ServerListButton = new(456, 166, 112, 39, 154, 398);
    public const int ServerListHoverSrcX = 378;
    public const int ServerListHoverSrcY = 398;
    public const int ActionServerList = 102; // spec §8.1 action table CODE-CONFIRMED

    // --- Quit/Help strip button (7-state) — @ (456,-3) 111×38. ---
    //   Y coordinate is -3: CODE-CONFIRMED from the widget-sweep literal dump.
    //   spec: Docs/RE/specs/ui_system.md §12 open item 1 RESOLVED: Y = -3.
    //   NORMAL  src: (792,398)
    //   HOVER   src: (602,416)
    //   PRESSED src: (602,416)   (HOVER == PRESSED per spec §8.1 table)
    //   atlas: login_slice1.dds.  ACTION: 105 (`i`). CODE-CONFIRMED.
    public static readonly WidgetRect QuitButton = new(456, -3, 111, 38, 792, 398);
    public const int QuitHoverSrcX = 602;
    public const int QuitHoverSrcY = 416;
    public const int ActionQuit = 105; // spec §8.1 action table CODE-CONFIRMED

    // --- Option/tab button 1 (7-state) — @ (40,82) 110×38. ---
    //   NORMAL  src: (520,492)   — corrected from earlier spec (was wrong); CODE-CONFIRMED.
    //   HOVER   src: (635,492)
    //   PRESSED src: (520,492)   (PRESSED == NORMAL per spec §8.1 table)
    //   atlas: loginwindow.dds.  ACTION: 111 (`o`). CODE-CONFIRMED.
    public static readonly WidgetRect OptionTab1 = new(40, 82, 110, 38, 520, 492);
    public const int OptionTab1HoverSrcX = 635;
    public const int OptionTab1HoverSrcY = 492;
    public const int ActionOptionTab1 = 111; // spec §8.1 CODE-CONFIRMED

    // --- Option/tab button 2 (7-state) — @ (164,82) 110×38. ---
    //   NORMAL  src: (750,492)   — corrected from earlier spec; CODE-CONFIRMED.
    //   HOVER   src: (865,492)
    //   PRESSED src: (750,492)   (PRESSED == NORMAL per spec §8.1 table)
    //   atlas: loginwindow.dds.  ACTION: 112 (`p`). CODE-CONFIRMED.
    public static readonly WidgetRect OptionTab2 = new(164, 82, 110, 38, 750, 492);
    public const int OptionTab2HoverSrcX = 865;
    public const int OptionTab2HoverSrcY = 492;
    public const int ActionOptionTab2 = 112; // spec §8.1 CODE-CONFIRMED

    // --- Quit-confirm modal chrome (InventWindow.dds). ---
    //   Shared 340×190 chrome at src (318,647). spec §8.3 CODE-CONFIRMED.
    public const int ModalChromeSrcX = 318;
    public const int ModalChromeSrcY = 647;
    public const int ModalChromeW = 340;
    public const int ModalChromeH = 190;

    // Quit-confirm "Yes" button #1 — @ (120,136) 113×40, NORMAL (302,900), HOVER (415,900).
    // spec: §8.1 site "Quit-confirm Yes #1"; atlas InventWindow.dds. ACTION: 113. CODE-CONFIRMED.
    public static readonly WidgetRect QuitConfirmYes1 = new(120, 136, 113, 40, 302, 900);
    public const int QuitConfirmYes1HoverSrcX = 415;
    public const int QuitConfirmYes1HoverSrcY = 900;
    public const int ActionQuitConfirmYes1 = 113;

    // Quit-confirm "Yes" button #2 — @ (120,136) 113×40, NORMAL (302,860), HOVER (415,860).
    // spec: §8.1 site "Quit-confirm Yes #2"; atlas InventWindow.dds. ACTION: 114. CODE-CONFIRMED.
    public static readonly WidgetRect QuitConfirmYes2 = new(120, 136, 113, 40, 302, 860);
    public const int QuitConfirmYes2HoverSrcX = 415;
    public const int QuitConfirmYes2HoverSrcY = 860;
    public const int ActionQuitConfirmYes2 = 114;

    // =========================================================================
    // Font slot pixel heights. spec §6.2 — D3DX Height column. CODE-CONFIRMED.
    // =========================================================================

    // Slot 2: DotumChe h=32, w=16, weight 800 — large bold title (see §6.2 slot 2).
    public const int FontTitleHeight = 32;

    // Slot 0: DotumChe h=12, w=6 — default small fixed (body text, labels).
    public const int FontBodyHeight = 12;

    // Slot 4: DotumChe h=12, w=6, weight 800 — used for button captions (bold small).
    public const int FontLabelHeight = 12;

    // --- Textbox render height override.
    //     The spec-recovered size is (102,13) which is too small for a usable Godot LineEdit.
    //     We render textboxes at 22px height (the legacy drew text ON TOP of the atlas frame at the
    //     font height, not inside a clipped 13px box — see LoginScreen.cs MakeTextbox comment).
    //     The x/width (102) and position (x,y) remain spec-exact.
    //     // PRESENTATION OVERRIDE — legacy drew text over atlas, not inside a clipped 13px box.
    public const int TextboxRenderH = 22; // render height for LineEdit widgets (spec=13; not clipped)

    // =========================================================================
    // msg.xdb caption ids. spec §10 / §1.9. CODE-CONFIRMED (ids); captions VFS-only.
    // =========================================================================

    // Login form static labels: 4001–4022 range.
    // spec: Docs/RE/specs/ui_system.md §10 — "4001–4022: login form static label captions". CODE-CONFIRMED.
    public const uint MsgLabelRangeFirst = 4001;
    public const uint MsgLabelRangeLast = 4022;

    // Quit-confirm prompts — shown in the modal popup.
    // spec: Docs/RE/specs/ui_system.md §10 — "4023 / 4024: login quit-confirm prompts". CODE-CONFIRMED.
    public const uint MsgQuitConfirm1 = 4023;
    public const uint MsgQuitConfirm2 = 4024;

    // Validation error toasts — shown when OK is pressed with bad input.
    // spec: Docs/RE/specs/frontend_scenes.md §1.4 — "ID len < 4 → msg 4025; PW len < 1 → msg 4026". CODE-CONFIRMED.
    public const uint MsgErrShortId = 4025; // ID/account too short (len < 4)
    public const uint MsgErrEmptyPassword = 4026; // password empty

    // Not-yet-used offline (server-list path absent), retained for completeness.
    public const uint MsgErrNoServers = 4027; // server list empty
    public const uint MsgErrConnectFailed = 4028; // server-list connection failed

    // Validation thresholds — CODE-CONFIRMED from frontend_scenes.md §1.4.
    // spec: Docs/RE/specs/frontend_scenes.md §1.4 — "ID length < 4 → show msg 4025". CODE-CONFIRMED.
    public const int MinIdLength = 4; // minimum account field length; msg 4025 if shorter

    // spec: Docs/RE/specs/frontend_scenes.md §1.4 — "password length < 1 → show msg 4026". CODE-CONFIRMED.
    public const int MinPwLength = 1; // minimum password field length; msg 4026 if shorter
}