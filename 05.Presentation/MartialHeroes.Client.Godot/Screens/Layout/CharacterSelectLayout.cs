// Screens/Layout/CharacterSelectLayout.cs
//
// Hardcoded pixel layout table for the CHARACTER-SELECT screen, on the 1024×768 reference canvas.
// Recovered from InitFromCharListAndBuildUI (124 widget-constructor calls). INTEROP FACTS.
//
// spec: Docs/RE/specs/ui_system.md §2.2 (character-select layout table),
//       §2.3 (shared InventWindow.dds modal chrome), §3.2 (select asset manifest), §5 (msg.xdb).

namespace MartialHeroes.Client.Godot.Screens.Layout;

/// <summary>
/// Character-select layout constants. spec: Docs/RE/specs/ui_system.md §2.2.
/// </summary>
public static class CharacterSelectLayout
{
    // --- Reference canvas. spec §2.0. CODE-CONFIRMED. ---
    public const int RefWidth = 1024;
    public const int RefHeight = 768;

    // --- Atlas paths. spec §3.2 character-select asset manifest. CODE-CONFIRMED. ---
    public const string AtlasMainWindow = "data/ui/mainwindow.dds";
    public const string AtlasInventWindow = "data/ui/inventwindow.dds";
    public const string AtlasTradeKeep = "data/ui/tradekeepwindow.dds";
    public const string AtlasBlacksheet = "data/ui/blacksheet.dds";
    public const string AtlasLoginWindow = "data/ui/loginwindow.dds";

    // --- Top title bar panel — @ (0,0) 577×58, mainwindow.dds. spec §2.2. CODE-CONFIRMED. ---
    public static readonly WidgetRect TitleBar = new(0, 0, 577, 58, 0, 0);

    // --- Big character list panel — @ (0,0) 244×474 (left column, scrollable). spec §2.2. ---
    public static readonly WidgetRect CharListPanel = new(0, 0, 244, 474, 0, 0);

    // --- Left character-info panel — @ (0,0) 244×187, mainwindow.dds. spec §2.2. ---
    public static readonly WidgetRect CharInfoPanel = new(0, 0, 244, 187, 0, 0);

    // --- Char-info portrait box — @ (0,12) 200×46, src (608,793). spec §2.2. ---
    public static readonly WidgetRect PortraitBox = new(0, 12, 200, 46, 608, 793);

    // --- Stat-row labels (Lv/HP/class) — x=60, y={37,61,85} 70×12. spec §2.2. CODE-CONFIRMED. ---
    public static readonly WidgetRect StatLabelLv = new(60, 37, 70, 12, 0, 0);
    public static readonly WidgetRect StatLabelHp = new(60, 61, 70, 12, 0, 0);
    public static readonly WidgetRect StatLabelClass = new(60, 85, 70, 12, 0, 0);

    // --- Stat icons (Lv/HP/class) — x=20, y={33,57,81} 34×18, src 771. spec §2.2. ---
    public static readonly WidgetRect StatIconLv = new(20, 33, 34, 18, 771, 0);
    public static readonly WidgetRect StatIconHp = new(20, 57, 34, 18, 771, 0);
    public static readonly WidgetRect StatIconClass = new(20, 81, 34, 18, 771, 0);

    // --- Create / Delete buttons. spec §2.2: Create @ (42,325) 59×20 action 413;
    //     Delete @ (112,325) 59×20 action 531. CODE-CONFIRMED. ---
    public static readonly WidgetRect CreateButton = new(42, 325, 59, 20, 0, 0);
    public static readonly WidgetRect DeleteButton = new(112, 325, 59, 20, 0, 0);

    // --- Per-slot row geometry (looped). spec §2.2 PARTIAL note: stride 24px CODE-CONFIRMED,
    //     base Y register-fed (PARTIAL). We pick a plausible base Y = 70 inside the list panel. ---
    public const int SlotRowStride = 24; // spec §2.2 — "loop step 24 px". CODE-CONFIRMED.
    public const int SlotRowBaseY = 70; // PARTIAL — spec §7 open item 2 (register-fed base).
    public const int SlotIconX = 12; // spec §2.2 per-slot stat-icon grid x=12. CODE-CONFIRMED.
    public const int SlotIconW = 34, SlotIconH = 18; // spec §2.2 — src 771, 34×18. CODE-CONFIRMED.
    public const int SlotLabelX = 46; // spec §2.2 per-slot value labels x=46. CODE-CONFIRMED.
    public const int SlotLabelW = 157, SlotLabelH = 18; // spec §2.2 — src (140,980). CODE-CONFIRMED.

    // --- Shared confirm popup chrome — 340×190 at src (318,647) of inventwindow.dds. spec §2.3. ---
    public static readonly WidgetRect ConfirmPopup = new(342, 289, 340, 190, 318, 647);

    // Confirm Yes/No buttons (inside popup, popup-local). spec §2.2 — Yes @ (55,136) 113×40,
    // No @ (174,136) 113×40, both src 415. CODE-CONFIRMED.
    public static readonly WidgetRect ConfirmYes = new(55, 136, 113, 40, 415, 0);
    public static readonly WidgetRect ConfirmNo = new(174, 136, 113, 40, 415, 0);

    // --- Name-entry sub-window — @ (430,100) 176×42, src (132,295), tradekeepwindow.dds. spec §2.2. ---
    public static readonly WidgetRect NameEntryWindow = new(430, 100, 176, 42, 132, 295);

    // Name-entry textbox (window-local) — @ (60,80) 274×18, CP949. spec §2.2. CODE-CONFIRMED.
    public static readonly WidgetRect NameEntryBox = new(60, 80, 274, 18, 0, 0);

    // --- Font slot heights. spec §4.2. ---
    public const int FontTitleHeight = 16; // slot 2 DotumChe 16/800. spec §4.2.
    public const int FontRowHeight = 13; // slot 0/13 fixed. spec §4.2.

    // --- msg.xdb caption ids.
    // NOTE: the per-widget caption ids for the title / Create / Delete / Enter buttons were NOT
    // recovered (spec §7 open item 4). Probing the real msg.xdb confirmed that the spec's known
    // ranges are NOT button captions: 9001+state = error-DIALOG messages (e.g. 9004 = "create
    // window error"); 0xC8–0xD4 (200–212) = char create/rename ERROR strings + time formats, not
    // labels. Binding captions to those ids is therefore wrong, so this screen uses honest English
    // placeholders for those captions. The msg.xdb wiring is in place (UiAssetLoader.Text) and can
    // adopt the real ids the moment a per-widget caption-id table is recovered.
}