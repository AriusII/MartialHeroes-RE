// Screens/Layout/LoginLayout.cs
//
// Hardcoded pixel layout table for the LOGIN screen, on the 1024×768 reference canvas.
// Every value here is an INTEROP FACT recovered in the login BuildScene routine — the spec
// states these are "interop facts, not guesses". Each constant cites its spec row.
//
// spec: Docs/RE/specs/ui_system.md §2.0 (reference canvas), §2.1 (login layout table),
//       §3.1 (login asset manifest), §4.2 (font slot pixel heights), §5 (msg.xdb ids).

namespace MartialHeroes.Client.Godot.Screens.Layout;

/// <summary>
/// A widget rectangle + atlas source sub-rect on the reference canvas.
/// <c>X/Y/W/H</c> are screen-local pixels (spec §2.0); <c>SrcX/SrcY</c> are the atlas UV origin
/// of the widget's sprite (spec §2.0 — sub-rect is (SrcX,SrcY)..(SrcX+W,SrcY+H)).
/// </summary>
public readonly record struct WidgetRect(int X, int Y, int W, int H, int SrcX, int SrcY);

/// <summary>
/// Login-screen layout constants. spec: Docs/RE/specs/ui_system.md §2.1.
/// </summary>
public static class LoginLayout
{
    // --- Reference canvas. spec §2.0 — "Reference canvas: 1024 × 768 pixels". CODE-CONFIRMED. ---
    public const int RefWidth = 1024;
    public const int RefHeight = 768;

    // --- Login form band. spec §2.1 "Animated login slice" row: y=110, the panel band whose
    //     origin the account/password/OK local coordinates are measured from. CODE-CONFIRMED. ---
    public const int BandTopY = 110;
    public const int BandHeight = 398; // spec §2.1 "Root backdrop panel" h=398

    // --- Atlas paths. spec §3.1 login asset manifest. CODE-CONFIRMED (xref from builder). ---
    public const string AtlasLoginWindow = "data/ui/loginwindow.dds";
    public const string AtlasLoginWindow02 = "data/ui/loginwindow_02.dds";
    public const string AtlasLoginSlice = "data/ui/login_slice1.dds"; // panning intro art (backdrop)
    public const string AtlasInventWindow = "data/ui/inventwindow.dds";

    // --- Widget rects (panel-local). spec §2.1 login layout table. CODE-CONFIRMED. ---

    // Account / ID textbox — @ (390,32) 102×13, src (615,404). spec §2.1.
    public static readonly WidgetRect AccountBox = new(390, 32, 102, 13, 615, 404);

    // Password textbox — @ (568,32) 102×13, src (615,404), masked. spec §2.1.
    public static readonly WidgetRect PasswordBox = new(568, 32, 102, 13, 615, 404);

    // Save-ID checkbox — @ (694,86) 13×13, src 717. spec §2.1.
    public static readonly WidgetRect SaveIdCheck = new(694, 86, 13, 13, 717, 0);

    // "ID" label strip — @ (340,30) 38×13, src 398. spec §2.1.
    public static readonly WidgetRect IdLabel = new(340, 30, 38, 13, 398, 0);

    // "PW" label strip — @ (507,30) 49×13, src (38,398). spec §2.1.
    public static readonly WidgetRect PwLabel = new(507, 30, 49, 13, 38, 398);

    // OK / Login button (7-state) — @ (456,64) 112×39, src 266. spec §2.1 actions 200/201/202.
    public static readonly WidgetRect OkButton = new(456, 64, 112, 39, 266, 0);

    // Server-list button (7-state) — @ (456,166) 112×39, src 154. spec §2.1 actions 206/16.
    public static readonly WidgetRect ServerListButton = new(456, 166, 112, 39, 154, 0);

    // Quit button (7-state) — @ (456, Y) 111×38, src (792,398). spec §2.1 actions 209/220.
    // NOTE: the Y coordinate is PARTIAL in the spec (open item §7.1 — passed via register, not a
    // literal). Per the mission brief we use ServerListButton.Y + ServerListButton.H + 8 as the
    // documented placeholder. That gives: 166 + 39 + 8 = 213.
    // Deviation constant: +8 px gap below the ServerListButton bottom edge (arbitrary spacing
    // placeholder pending recovery of the real Y). Cite: spec §7 open item 1.
    private const int QuitButtonYDeviation = 213; // = ServerListButton.Y(166) + ServerListButton.H(39) + 8 gap
    public static readonly WidgetRect QuitButton = new(456, QuitButtonYDeviation, 111, 38, 792, 398);

    // --- Font slot pixel heights. spec §4.2 — D3DXCreateFontA Height column. CODE-CONFIRMED. ---
    // slot 2: DotumChe 16/800 → large bold title. spec §4.2 slot 2.
    public const int FontTitleHeight = 16;

    // slot 0: DotumChe 6/12 → default small fixed; we render labels a touch larger for legibility.
    public const int FontLabelHeight = 13;

    // --- msg.xdb caption ids.
    // IMPORTANT: the per-widget caption ids for the ID/PW/OK/Quit static labels were NOT recovered
    // (spec §7 open item 4 — per-widget caption id not exhaustively extracted). Probing the real
    // msg.xdb showed the ids that WERE guessed map to unrelated strings (9001+state = error-dialog
    // messages; 4025–4028 = login error TOASTS; 200–204 = time formats). Binding static button/label
    // captions to those ids is therefore WRONG, so the screen uses honest English placeholders for
    // those captions and reserves msg.xdb for the ids the spec confirms (the login error toasts).
    //
    // CONFIRMED login error toasts. spec §5 — "4025–4028: login error toast messages". These are
    // shown by the OK handler when a field is empty (faithful to the legacy validation behaviour).
    public const uint MsgErrEmptyId = 4025; // CONFIRMED — "아이디를 입력해야 합니다." (enter ID)
    public const uint MsgErrEmptyPassword = 4026; // CONFIRMED — "패스워드를 입력해야 합니다." (enter PW)
    public const uint MsgErrAllServersDown = 4027; // CONFIRMED — "모든서버가 점검중입니다." (all servers down)
    public const uint MsgErrConnectFailed = 4028; // CONFIRMED — "서버와 연결을 실패하였습니다." (connect failed)
}