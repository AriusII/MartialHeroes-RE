
namespace MartialHeroes.Client.Presentation.Screens.Layout;

public readonly record struct WidgetRect(int X, int Y, int W, int H, int SrcX, int SrcY);

public static class LoginLayout
{

    public const int RefWidth = 1024;
    public const int RefHeight = 768;


    public const string AtlasLoginSlice1 = "data/ui/login_slice1.dds";

    public const string AtlasLoginWindow = "data/ui/loginwindow.dds";

    public const string AtlasInventWindow = "data/ui/inventwindow.dds";

    public const string AtlasLoginWindow02 = "data/ui/loginwindow_02.dds";


    public const int StatusIndicatorSrcX = 500;
    public const int StatusIndicatorSrcY = 786;
    public const int StatusIndicatorW = 60;
    public const int StatusIndicatorH = 39;
    public const int StatusIndicatorCount = 3;
    public const int ActionScrollUp = 106;
    public const int ActionScrollDown = 107;
    public const int ActionScrollThumb = 108;


    public const int ModalChromeX = 342;
    public const int ModalChromeY = 289;
    public const int ModalChromeW = 340;
    public const int ModalChromeH = 190;
    public const int ModalChromeSrcX = 318;
    public const int ModalChromeSrcY = 647;

    public const int ModalPromptX = 32;
    public const int ModalPromptY = 42;
    public const int ModalPromptW = 276;
    public const int ModalPromptH = 70;

    public const int ConfirmLabelX = 10;
    public const int ConfirmLabelY = 100;
    public const int ConfirmLabelW = 330;
    public const int ConfirmLabelH = 20;
    public const int QuitConfirmYes1HoverSrcX = 415;
    public const int QuitConfirmYes1HoverSrcY = 900;
    public const int ActionQuitConfirmYes1 = 113;
    public const int QuitConfirmYes2HoverSrcX = 415;
    public const int QuitConfirmYes2HoverSrcY = 860;
    public const int ActionQuitConfirmYes2 = 114;


    public const int BottomBarW = 1024;
    public const int BottomBarH = 442;
    public const int BottomBarSrcX = 0;

    public const int BottomBarSrcY = 582;

    public const int BottomBarCanvasY = 326;
    public const int ConfirmHoverSrcX = 378;
    public const int ConfirmHoverSrcY = 398;

    public const int ActionConfirm = 102;

    public const string EditFieldFrameAtlas = AtlasLoginSlice1;
    public const int EditFieldFrameSrcX = 615;
    public const int EditFieldFrameSrcY = 404;

    public const int
        IdMaxLength =
            19;

    public const int
        PwMaxLength =
            17;

    public const int IdTextboxKeystrokeCap = 16;

    public const int PwTextboxKeystrokeCap = 12;

    public const int SaveIdCheckedSrcX = 730;
    public const int SaveIdCheckedSrcY = 398;
    public const int ActionSaveId = 104;
    public const int OkHoverSrcX = 490;
    public const int OkHoverSrcY = 398;
    public const int ActionOk = 103;

    public const int ActionAppQuit = 101;

    public const uint MsgExitConfirm = 2007;
    public const int QuitHoverSrcX = 602;
    public const int QuitHoverSrcY = 416;
    public const int OptionTab1HoverSrcX = 635;
    public const int OptionTab1HoverSrcY = 492;
    public const int ActionOptionTab1 = 111;
    public const int OptionTab2HoverSrcX = 865;
    public const int OptionTab2HoverSrcY = 492;
    public const int ActionOptionTab2 = 112;


    public const int PinModalX = 347;
    public const int PinModalY = 173;
    public const int PinModalW = 329;
    public const int PinModalH = 422;

    public const string AtlasPassword = "data/ui/password.dds";

    public const int PinKeypadColSpacing = 55;
    public const int PinKeypadCol0X = 28;
    public const int PinKeypadRow0Y = 170;
    public const int PinKeypadRow1Y = 230;
    public const int PinKeypadTileW = 52;
    public const int PinKeypadTileH = 52;

    public const int PinKeypadCellCount = 10;
    public const int PinKeypadDigitsPerCell = 10;

    public const int PinDigitColWidth = 52;

    public const int
        PinDigitNormalSrcY = 560;

    public const int
        PinDigitHoverSrcY =
            612;

    public const int
        PinDigitPressedSrcY =
            664;

    public const int PinResetX = 243;
    public const int PinResetY = 133;
    public const int PinResetW = 58;
    public const int PinResetH = 30;
    public const int PinResetNSrcX = 663;
    public const int PinResetNSrcY = 8;
    public const int PinResetHSrcX = 663;
    public const int PinResetHSrcY = 88;
    public const int PinResetPSrcX = 663;
    public const int PinResetPSrcY = 48;

    public const int PinOkX = 90;
    public const int PinOkY = 290;
    public const int PinOkW = 154;
    public const int PinOkH = 58;
    public const int PinOkNSrcX = 330;
    public const int PinOkNSrcY = 0;
    public const int PinOkHSrcX = 330;
    public const int PinOkHSrcY = 116;
    public const int PinOkPSrcX = 330;
    public const int PinOkPSrcY = 58;

    public const int PinCancelX = 90;
    public const int PinCancelY = 350;
    public const int PinCancelW = 154;
    public const int PinCancelH = 58;
    public const int PinCancelNSrcX = 486;
    public const int PinCancelNSrcY = 0;
    public const int PinCancelHSrcX = 486;
    public const int PinCancelHSrcY = 116;
    public const int PinCancelPSrcX = 486;
    public const int PinCancelPSrcY = 58;

    public const int PinMaxLength = 4;


    public const uint MsgVersionMismatch = 2204;

    public const uint MsgQuitConfirm1 = 4023;
    public const uint MsgQuitConfirm2 = 4024;

    public const uint MsgErrShortId = 4025;
    public const uint MsgErrEmptyPassword = 4026;
    public const uint MsgErrNoServers = 4027;
    public const uint MsgErrConnectFail = 4028;
    public const uint MsgServerUnknown = 5901;
    public const uint MsgServerLoadRed = 6001;
    public const uint MsgServerLoadOrange = 6002;
    public const uint MsgServerLoadYellow = 6003;
    public const uint MsgServerPreparing = 6004;
    public const uint MsgServerClockFormat = 6005;


    public const int ServerNameMsgBase = 5000;

    public const int StatusCaptionMsgBase = 4029;

    public const int PopulationRedThreshold = 1200;
    public const int PopulationOrangeThreshold = 800;
    public const int PopulationYellowThreshold = 500;

    public const uint PopulationRedArgb = 0xFFFF0000;
    public const uint PopulationOrangeArgb = 0xFFED6806;
    public const uint PopulationYellowArgb = 0xFFFFFF00;
    public const uint PopulationGreenArgb = 0xFFB5FF7A;

    public const int SelectableStatusCode = 0;
    public const int SelectableLoadCeiling = 2400;
    public const int SpecialRowServerId = 100;

    public const uint MsgLabelFirst = 4001;
    public const uint MsgLabelLast = 4022;

    public const int NoticeLabelLocalX = 50;
    public const int NoticeLabelStartY = 100;
    public const int NoticeLabelStrideY = 18;
    public const int NoticeLabelW = 383;
    public const int NoticeLabelH = 50;


    public const string SaveIdConfigPath = "user://mh_options.cfg";
    public const string SaveIdSection = "DO_OPTION";
    public const string SaveIdKey = "OPTION_ID";
    public const string SaveIdNullSentinel = "(null)";


    public const int MinIdLength = 4;
    public const int MinPwLength = 1;


    public static readonly WidgetRect MainPanel = new(0, 110, 1024, 490, 0, 0);

    public static readonly WidgetRect ServerListbox = new(270, 85, 483, 490, 0, 490);

    public static readonly WidgetRect ScrollUpArrow = new(467, 86, 13, 10, 483, 490);

    public static readonly WidgetRect ScrollDownArrow = new(467, 455, 13, 10, 505, 490);

    public static readonly WidgetRect ScrollThumb = new(469, 98, 9, 9, 496, 490);

    public static readonly WidgetRect ListboxHeader = new(207, 44, 70, 17, 70, 980);


    public static readonly WidgetRect BackgroundPanel = new(0, 0, 1024, 398, 0, 0);


    public static readonly WidgetRect QuitConfirmYes1 = new(120, 136, 113, 40, 302, 900);

    public static readonly WidgetRect QuitConfirmYes2 = new(120, 136, 113, 40, 302, 860);

    public static readonly WidgetRect ConfirmButton = new(456, 166, 112, 39, 154, 398);

    public static readonly WidgetRect ConfirmFacePlate = new(265, 0, 494, 113, 0, 469);

    public static readonly WidgetRect AccountLabelArt = new(340, 30, 38, 13, 0, 398);

    public static readonly WidgetRect PasswordLabelArt = new(507, 30, 49, 13, 38, 398);

    public static readonly WidgetRect SmallDecorPlate = new(619, 86, 67, 13, 87, 398);

    public static readonly WidgetRect AccountBox = new(390, 32, 102, 13, EditFieldFrameSrcX, EditFieldFrameSrcY);

    public static readonly WidgetRect PasswordBox = new(568, 32, 102, 13, EditFieldFrameSrcX, EditFieldFrameSrcY);

    public static readonly WidgetRect SaveIdCheck = new(694, 86, 13, 13, 717, 398);

    public static readonly WidgetRect OkButton = new(456, 64, 112, 39, 266, 398);

    public static readonly WidgetRect QuitButton = new(456, -3, 111, 38, 792, 398);

    public static readonly WidgetRect QuitDecoPlate = new(407, -3, 210, 70, 743, 398);


    public static readonly WidgetRect OptionTab1 = new(40, 82, 110, 38, 520, 492);

    public static readonly WidgetRect OptionTab2 = new(164, 82, 110, 38, 750, 492);

    public static readonly WidgetRect DecoPlate1 = new(67, 48, 178, 13, 0, 437);

    public static readonly WidgetRect DecoPlate2 = new(0, 100, 313, 32, 289, 437);
}