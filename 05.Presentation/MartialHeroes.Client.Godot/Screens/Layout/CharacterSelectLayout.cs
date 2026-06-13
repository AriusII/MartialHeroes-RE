// Screens/Layout/CharacterSelectLayout.cs
//
// Hardcoded pixel layout table for the CHARACTER-SELECT screen, on the 1024×768 reference canvas.
// Recovered from InitFromCharListAndBuildUI (77+ widget-constructor calls). INTEROP FACTS.
//
// spec: Docs/RE/specs/ui_system.md §8.2 (character-select layout table — 77 ctor sites CODE-CONFIRMED),
//       §8.3 (shared InventWindow.dds modal chrome), §8.4 (generator patterns — base-Y 191, stride 24),
//       §9.2 (character-select asset manifest),
//       Docs/RE/specs/frontend_scenes.md §3 (slot record), §4 (create form), §9 (constants).

namespace MartialHeroes.Client.Godot.Screens.Layout;

/// <summary>
/// Character-select layout constants. spec: Docs/RE/specs/ui_system.md §8.2.
/// </summary>
public static class CharacterSelectLayout
{
    // --- Reference canvas. spec §8.0. CODE-CONFIRMED. ---
    public const int RefWidth = 1024;
    public const int RefHeight = 768;

    // =========================================================================
    // Atlas paths. spec: Docs/RE/specs/ui_system.md §9.2 character-select asset manifest.
    // All paths are VFS-relative. CODE-CONFIRMED.
    // =========================================================================
    public const string
        AtlasLoginWindow = "data/ui/loginwindow.dds"; // tab buttons, stat-icon grids, Create/Delete/Enter strip

    public const string AtlasInventWindow = "data/ui/inventwindow.dds"; // confirm/modal chrome + buttons
    public const string AtlasBlacksheet = "data/ui/blacksheet.dds"; // corner close button, dim overlay

    public const string
        AtlasCarrierPigeon = "data/ui/carrierpigeonperson.dds"; // appearance selector ±, gender/class swatches

    // mainwindow.dds used for top bar and panel chrome — paths not confirmed in VFS scan; fallback panels used.
    public const string AtlasMainWindow = "data/ui/mainwindow.dds";

    // =========================================================================
    // §8.2 Structural recovery — key rectangles (CODE-CONFIRMED from spec §8.2 table).
    // =========================================================================

    // --- Top title bar panel: (0,0) 577×58. spec §8.2 "Top title bar panel". CODE-CONFIRMED. ---
    public static readonly WidgetRect TitleBar = new(0, 0, 577, 58, 0, 0);

    // --- Big character list panel: (0,0) 244×474 (left column). spec §8.2.
    //     Note: CharacterSelectScreen.cs references this constant by name. ---
    public static readonly WidgetRect CharListPanel = new(0, 0, 244, 474, 0, 0);

    // --- Left character-info panel: (0,0) 244×187. spec §8.2 "Left character-info panel". CODE-CONFIRMED. ---
    public static readonly WidgetRect CharInfoPanel = new(0, 0, 244, 187, 0, 0);

    // --- Char-info portrait box: (0,12) 200×46, src (608,793). spec §8.2. ---
    public static readonly WidgetRect PortraitBox = new(0, 12, 200, 46, 608, 793);

    // --- Stat-row labels (Lv/HP/class): x=60, y={37,61,85} 70×12. spec §8.2. CODE-CONFIRMED. ---
    public static readonly WidgetRect StatLabelLv = new(60, 37, 70, 12, 0, 0);
    public static readonly WidgetRect StatLabelHp = new(60, 61, 70, 12, 0, 0);
    public static readonly WidgetRect StatLabelClass = new(60, 85, 70, 12, 0, 0);

    // --- Per-slot row geometry for the slot list. spec §8.2 PARTIAL: stride CODE-CONFIRMED.
    //     Note: CharacterSelectScreen.cs references these constants by name. ---
    public const int SlotRowStride = 24; // spec §8.2 — "loop step 24 px". CODE-CONFIRMED.
    public const int SlotRowBaseY = 70; // plausible base Y (register-fed in legacy code).
    public const int SlotIconX = 12; // per-slot stat-icon grid x=12. spec §8.2. CODE-CONFIRMED.
    public const int SlotIconW = 34; // src 771, 34×18. spec §8.2. CODE-CONFIRMED.
    public const int SlotIconH = 18; // src 771, 34×18. spec §8.2. CODE-CONFIRMED.
    public const int SlotLabelX = 46; // per-slot value labels x=46. spec §8.2. CODE-CONFIRMED.
    public const int SlotLabelW = 157; // per-slot label width. spec §8.2. CODE-CONFIRMED.
    public const int SlotLabelH = 18; // per-slot label height. spec §8.2. CODE-CONFIRMED.

    // --- Server tab button: (67,17) 113×40. NORMAL (675,795), HOVER (483,883). spec §8.2. CODE-CONFIRMED. ---
    public static readonly WidgetRect ServerTabBtn = new(67, 17, 113, 40, 675, 795);
    public static readonly WidgetRect ServerTabHov = new(67, 17, 113, 40, 483, 883); // hover src
    // Server tab actionId = 1. spec §8.2 action-id map.

    // --- Channel tab button: (232,7) 113×40. NORMAL (640,742), HOVER (483,923). spec §8.2. CODE-CONFIRMED. ---
    public static readonly WidgetRect ChannelTabBtn = new(232, 7, 113, 40, 640, 742);
    public static readonly WidgetRect ChannelTabHov = new(232, 7, 113, 40, 483, 923); // hover src
    // Channel tab actionId = 2.

    // --- Back tab button: (393,17) 113×40. NORMAL (625,691), HOVER (483,963). spec §8.2. CODE-CONFIRMED. ---
    public static readonly WidgetRect BackTabBtn = new(393, 17, 113, 40, 625, 691);
    public static readonly WidgetRect BackTabHov = new(393, 17, 113, 40, 483, 963); // hover src
    // Back tab actionId = 3.

    // =========================================================================
    // §8.2 Create / Delete / Enter buttons — ACTION IDs CORRECTED.
    //
    // CORRECTION (CODE-CONFIRMED):  The values 413 and 531 that appeared in earlier versions of
    // this code were the HOVER src-X of the Create and Delete buttons, NOT action ids.
    //   Create button NORMAL src-X = 354; HOVER src-X = 354+59 = 413.
    //   Delete button NORMAL src-X = 472; HOVER src-X = 472+59 = 531.
    // The actual Panel_AddChildWithAction ids are:
    //   Create = 4, Delete = 5, Enter = 6  (CODE-CONFIRMED from widget-sweep literal dump).
    // spec: Docs/RE/specs/ui_system.md §8.2 action-id map + correction note.
    // spec: Docs/RE/specs/frontend_scenes.md §4 correction note (same).
    // =========================================================================

    // Create button: (42,325) 59×20. NORMAL src (354,1004), HOVER src (413,1004). CODE-CONFIRMED.
    public const int CreateActionId = 4; // spec: ui_system.md §8.2, frontend_scenes.md §4.
    public static readonly WidgetRect CreateButton = new(42, 325, 59, 20, 354, 1004);
    public static readonly WidgetRect CreateButtonHover = new(42, 325, 59, 20, 413, 1004);
    // PRESSED = HOVER for this button (3-state ctor, spec §1.5 table).

    // Delete button: (112,325) 59×20. NORMAL src (472,1004), HOVER src (531,1004). CODE-CONFIRMED.
    public const int DeleteActionId = 5; // spec: ui_system.md §8.2, frontend_scenes.md §5.
    public static readonly WidgetRect DeleteButton = new(112, 325, 59, 20, 472, 1004);
    public static readonly WidgetRect DeleteButtonHover = new(112, 325, 59, 20, 531, 1004);

    // Enter/select button: (112,112) 59×20. NORMAL src (236,1004), HOVER src (295,1004). CODE-CONFIRMED.
    public const int EnterActionId = 6; // spec: ui_system.md §8.2.
    public static readonly WidgetRect EnterButton = new(112, 112, 59, 20, 236, 1004);
    public static readonly WidgetRect EnterButtonHover = new(112, 112, 59, 20, 295, 1004);

    // =========================================================================
    // §8.2 / §8.4 Stat-icon grid — CODE-CONFIRMED.
    //   Per-slot stat-icon col1: x=154, base-Y 191, stride 24, size 24×16,
    //     NORMAL (500,770), PRESSED (548,770).
    //   Per-slot stat-icon col2: x=178, same base/stride/size,
    //     NORMAL (524,770), PRESSED (572,770).
    //   Per-slot stat value labels: x=51, base-Y 193, stride 24, 35×12.
    // spec: Docs/RE/specs/ui_system.md §8.2 + §8.4 (generator, CODE-CONFIRMED).
    // =========================================================================
    public const int StatGridBaseY = 191; // spec: ui_system.md §8.2+§8.4 "base-Y 191". CODE-CONFIRMED.
    public const int StatGridStride = 24; // spec: ui_system.md §8.2+§8.4 "stride 24". CODE-CONFIRMED.
    public const int StatGridRows = 5; // up to 5 per-slot stat rows.

    public const int StatIconCol1X = 154; // spec: ui_system.md §8.2. CODE-CONFIRMED.
    public const int StatIconCol2X = 178; // spec: ui_system.md §8.2. CODE-CONFIRMED.
    public const int StatIconW = 24; // spec: ui_system.md §8.2. CODE-CONFIRMED.
    public const int StatIconH = 16; // spec: ui_system.md §8.2. CODE-CONFIRMED.

    // NORMAL / PRESSED src coords for the stat icon grid buttons.
    // spec: ui_system.md §8.2 "Per-slot stat-icon grid col 1: NORMAL (500,770), PRESSED (548,770)".
    public const int StatIconCol1NormalSrcX = 500; // CODE-CONFIRMED
    public const int StatIconCol1NormalSrcY = 770; // CODE-CONFIRMED
    public const int StatIconCol1PressedSrcX = 548; // CODE-CONFIRMED
    public const int StatIconCol1PressedSrcY = 770; // CODE-CONFIRMED
    public const int StatIconCol2NormalSrcX = 524; // CODE-CONFIRMED
    public const int StatIconCol2NormalSrcY = 770; // CODE-CONFIRMED
    public const int StatIconCol2PressedSrcX = 572; // CODE-CONFIRMED
    public const int StatIconCol2PressedSrcY = 770; // CODE-CONFIRMED

    public const int StatValueX = 51; // spec: ui_system.md §8.2+§8.4 "x=51". CODE-CONFIRMED.
    public const int StatValueBaseY = 193; // spec: ui_system.md §8.2+§8.4 "base-Y 193". CODE-CONFIRMED.
    public const int StatValueW = 35; // spec: ui_system.md §8.2. CODE-CONFIRMED.
    public const int StatValueH = 12; // spec: ui_system.md §8.2. CODE-CONFIRMED.

    // =========================================================================
    // §8.2 Confirm popup chrome — InventWindow.dds 340×190 at src (318,647). spec §8.3. CODE-CONFIRMED.
    // =========================================================================
    public static readonly WidgetRect ConfirmPopup = new(342, 289, 340, 190, 318, 647);

    // Confirm Yes/No buttons (popup-local). spec §8.2. CODE-CONFIRMED.
    // Yes: (55,136) 113×40, src (302,860). No: (174,136) 113×40, src (302,900).
    public static readonly WidgetRect ConfirmYes = new(55, 136, 113, 40, 302, 860);
    public static readonly WidgetRect ConfirmNo = new(174, 136, 113, 40, 302, 900);

    // =========================================================================
    // §8.2 Name-entry sub-form. spec §8.2. CODE-CONFIRMED.
    // =========================================================================
    // Name-entry textbox (window-local): (60,80) 274×18, CP949. spec §8.2. CODE-CONFIRMED.
    public static readonly WidgetRect NameEntryBox = new(60, 80, 274, 18, 0, 0);

    // Corner close button: (971,610) 23×23. src (941,910) of blacksheet.dds. spec §8.2. CODE-CONFIRMED.
    public static readonly WidgetRect CornerClose = new(971, 610, 23, 23, 941, 910);

    // =========================================================================
    // §8.4 Appearance selector (create form) — generator pattern. CODE-CONFIRMED.
    //   Base y=30, w=45, h=19; NORMAL src-X base 590 stepping +45; HOVER base 815 (+45).
    // spec: Docs/RE/specs/ui_system.md §8.4 "Char-select appearance selector". CODE-CONFIRMED.
    // =========================================================================
    public const int AppSelectorBaseY = 30; // CODE-CONFIRMED
    public const int AppSelectorW = 45; // CODE-CONFIRMED
    public const int AppSelectorH = 19; // CODE-CONFIRMED
    public const int AppSelectorNormBase = 590; // NORMAL src-X base. CODE-CONFIRMED.
    public const int AppSelectorHovBase = 815; // HOVER src-X base. CODE-CONFIRMED.
    public const int AppSelectorStep = 45; // src-X step per selector. CODE-CONFIRMED.

    // =========================================================================
    // §4.1 Create sub-form: class map, face range. spec: frontend_scenes.md §4.1/§4.2.
    // =========================================================================

    // UI index → internal class id map. spec: frontend_scenes.md §4.1, CODE-CONFIRMED.
    //   UI {0,1,2,3} → internal {4,1,3,2}. NOT the identity.
    public static readonly int[] UiToInternalClass = [4, 1, 3, 2]; // CODE-CONFIRMED

    // Face selector range [1..7]. spec: frontend_scenes.md §4.2, §9. CODE-CONFIRMED.
    public const int FaceIndexMin = 1; // CODE-CONFIRMED
    public const int FaceIndexMax = 7; // CODE-CONFIRMED

    // =========================================================================
    // §10 msg.xdb caption ids. spec: ui_system.md §10 + frontend_scenes.md §9.
    // =========================================================================

    // Class labels on the create form: ids 14003..14007. spec: ui_system.md §10. CODE-CONFIRMED.
    // The human-readable Korean caption text is in msg.xdb (CP949, VFS-only) — not reproduced here.
    // We fall back to English placeholders when msg.xdb is absent or the id is missing.
    //
    // INDEXING CONVENTION: both arrays are indexed by UI-select index (0..3); the 5th entry
    // (index 4) pairs with msg id 14007 and exists for the case where the msg catalog returns a
    // record beyond the four active classes — callers must guard against index == 4 being an
    // "(unknown)" placeholder and not a real class.  The mapping from UI index to internal class id
    // is given by <see cref="UiToInternalClass"/> (NOT the identity — see §8.2 action-id map).
    //   UI index 0 → internal 4, UI index 1 → internal 1 (Musa), UI index 2 → internal 3,
    //   UI index 3 → internal 2.  spec: frontend_scenes.md §4.1. CODE-CONFIRMED.
    //
    // NOTE: CharacterSelectScreen.cs carries the same roster convention and should carry an
    // equivalent one-line note on its DemoRoster definition for clarity (not edited here per
    // task scope — see task finding 5 note).
    public static readonly uint[] ClassLabelMsgIds = [14003u, 14004u, 14005u, 14006u, 14007u]; // CODE-CONFIRMED

    // English fallbacks for class labels (used when VFS is absent; IDs 14003..14007 in msg.xdb).
    // Array index = UI-select index 0..3; index 4 = "(unknown)" placeholder for msg id 14007.
    public static readonly string[] ClassLabelFallbacks = ["Musa", "Tao", "Blader", "Warrior", "(unknown)"];

    // =========================================================================
    // §9 Per-slot stage positions for 3D previews. spec: frontend_scenes.md §3.3. CODE-CONFIRMED.
    //   Per-slot X offsets: {-1560, -1548, -1536, -1524, -1512} (12 units apart).
    //   Z ≈ -3593 (plus stage origin). Scale ×3.0.
    // =========================================================================
    public static readonly float[] PreviewSlotX = [-1560f, -1548f, -1536f, -1524f, -1512f]; // CODE-CONFIRMED
    public const float PreviewSlotZ = -3593f; // CODE-CONFIRMED
    public const float PreviewScale = 3.0f; // CODE-CONFIRMED

    // =========================================================================
    // §font: Font slot heights used in the select screen. spec §6.2.
    // =========================================================================
    public const int FontTitleHeight = 16; // DotumChe slot 14: Height=16. spec §6.2. CODE-CONFIRMED.
    public const int FontRowHeight = 12; // DotumChe slot 0: Height=12. spec §6.2. CODE-CONFIRMED.
}