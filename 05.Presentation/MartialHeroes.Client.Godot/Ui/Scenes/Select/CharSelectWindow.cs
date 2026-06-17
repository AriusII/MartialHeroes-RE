// Ui/Scenes/Select/CharSelectWindow.cs
//
// Character-select 2D chrome (state 4) — FROM SCRATCH on the Ui/ substrate.
// Replaces the old Screens/CharacterSelectScreen.cs for layer-05 presentation only.
//
// BUILD ON:
//   HudAtlasLibrary   — VFS atlas slices (HudWidgetFactory uses this).
//   HudTextLibrary    — msg.xdb CP949 caption lookup.
//   HudWidgetFactory  — GU-faithful widget constructors.
//   CharSelectScene3D / CharCreatePreview3D — REUSED 3D scene layer (unchanged).
//   CharListEventDrainer (via CharSelectEventDrainer) — REUSED event pump.
//   ScreenHost.SetScreen — POINT-anchor root (no FullRect on root).
//
// WIDGET CONTRACT (state 4 SELECT view):
//   - 3D scene SubViewport (full canvas, bottom layer) via CharSelectScene3D.
//   - Char-count caption msg 2209; orange; top-centre.
//   - Top tabs: Server/Channel/Back = actions 1/2/3; loginwindow.dds.
//     PRESSED src 483,883/923/963 per §11.5b. CODE-CONFIRMED.
//   - Single shared left info panel + stat-icon grid (10 cells, 2×5).
//     col1 x=154 NORMAL(500,770) HOVER(548,770); col2 x=178 NORMAL(524,770) HOVER(572,770).
//     base-Y 191, stride 24, cell 24×16.
//     spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
//   - Create/Delete/Enter actions 4/5/6 at dst-Y 112 (centred).
//     spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
//   - Corner close: fallback rect (971,610) 23×23, blacksheet.dds src (941,910).
//     spec: Docs/RE/specs/ui_system.md §8.2. CODE-CONFIRMED for close action (type 13/id 10001).
//     dst + src are base-window-chrome; debugger-pending.
//
// WIDGET CONTRACT (CREATE sub-form):
//   - 4 class buttons actions 10-13; loginwindow.dds; NORMAL src-Y 1005.
//     NORMAL src-X {590,635,680,725}; HOVER src-X {770,815,860,905}.
//     spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
//     Class map: UI {0,1,2,3} → internal {4,1,3,2} (Monk/Musa/Dosa/Salsu).
//     spec: Docs/RE/specs/frontend_scenes.md §4.1. CODE-CONFIRMED.
//   - Stat-icon grid (10 cells, 2×5) doubling as point-buy ± buttons actions 25-34.
//     spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
//   - Face ± steppers: actions 21/22; range 1-7; 2D only (no 3D rebuild).
//     spec: Docs/RE/specs/frontend_scenes.md §4.2. CODE-CONFIRMED.
//   - Confirm/Cancel at dst-Y 325 on create-plate row art V=1004:
//     Confirm (35) dst-X 42 NORMAL(354,1004) HOVER src-X 413.
//     Cancel  (36) dst-X 112 NORMAL(472,1004) HOVER src-X 531.
//     spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
//   - Name modal: textbox (60,80,274,18); Confirm 35 / Cancel 36;
//     validation: min 2 chars, a-z/0-9/CP949; 16 payload bytes max.
//     Error msg ids 2190/2075/12012.
//     spec: Docs/RE/specs/ui_system.md §8.2. CODE-CONFIRMED.
//   - CharCreatePreview3D — full-screen backdrop (OwnWorld3D=true).
//     spec: Docs/RE/specs/frontend_scenes.md §4/§3.7.6. CODE-CONFIRMED.
//
// SIGNALS (identical surface to Screens/CharacterSelectScreen — allows SelectScene re-point):
//   EnterGameRequested(characterName, slotIndex)
//   BackRequested()
//   CreateCharacterRequested(name, internalClass, faceIndex)
//   DeleteCharacterRequested(slotIndex, characterName)
//
// PUBLIC API (consumed by CharSelectEventDrainer and SelectScene):
//   ApplyCharacterList(ImmutableArray<CharacterListSlot>) — called on main thread.
//   DevShowCreateForm()                                    — dev/headless only.
//
// PASSIVE: zero game logic, zero domain state, zero packet parsing.
// spec: Docs/RE/specs/ui_system.md §8.2/§8.4.
// spec: Docs/RE/specs/frontend_scenes.md §3-§7/§11.
// spec: Docs/RE/formats/config_tables.md §2.17.3.

using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

/// <summary>
/// Character-select 2D chrome window (state 4), built on the Ui/ substrate.
///
/// <para>Strictly passive. Reads atlases via HudAtlasLibrary and captions via HudTextLibrary.
/// Turns UI gestures into signals (intents). ZERO game-rule authority.</para>
///
/// <para>The 3D scene layer (CharSelectScene3D/CharCreatePreview3D) is REUSED unchanged.
/// This class provides only the 2D chrome overlay.</para>
///
/// spec: Docs/RE/specs/ui_system.md §8.2/§8.4 (layout); frontend_scenes.md §3–§7 (flow).
/// </summary>
public sealed partial class CharSelectWindow : Control
{
    // =========================================================================
    // Outgoing signals — consumed by SelectScene (zero game logic here).
    // =========================================================================

    /// <summary>
    /// Raised when the player enters the game with the selected slot.
    /// spec: Docs/RE/specs/frontend_scenes.md §7 — "send 1/9 with slot index". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void EnterGameRequestedEventHandler(string characterName, int slotIndex);

    /// <summary>Raised when the player presses Back tab (action 3).</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    /// <summary>
    /// Raised when the player confirms the Create-character form.
    /// spec: Docs/RE/specs/frontend_scenes.md §4/§8 — "send 1/6 (52B)". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void CreateCharacterRequestedEventHandler(string name, int internalClass, int faceIndex);

    /// <summary>
    /// Raised when the player presses Delete on an occupied slot.
    /// spec: Docs/RE/specs/frontend_scenes.md §5/§8.
    /// </summary>
    [Signal]
    public delegate void DeleteCharacterRequestedEventHandler(int slotIndex, string characterName);

    // =========================================================================
    // Constants (spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.)
    // =========================================================================

    private const int MaxSlots = 5;            // spec: frontend_scenes.md §3.1. CODE-CONFIRMED.
    private const string BlankSentinel = "@BLANK@";

    // Reference canvas.
    // spec: Docs/RE/specs/ui_system.md §8.0. CODE-CONFIRMED.
    private const float RefW = 1024f;
    private const float RefH = 768f;

    // Atlas paths.
    // spec: Docs/RE/specs/ui_system.md §8.2 atlas correction (CODE-CONFIRMED).
    private const string AtlasLoginWindow  = "data/ui/loginwindow.dds";
    private const string AtlasInventWindow = "data/ui/inventwindow.dds";
    private const string AtlasBlacksheet   = "data/ui/blacksheet.dds";
    private const string AtlasMainWindow   = "data/ui/mainwindow.dds";

    // Top tabs — actionIds 1/2/3.
    // spec: Docs/RE/specs/frontend_scenes.md §11.5b. CODE-CONFIRMED.
    private const int ActionServer  = 1; // Server tab.
    private const int ActionChannel = 2; // Channel tab.
    private const int ActionBack    = 3; // Back tab.

    // Roster action row — actionIds 4/5/6, dst-Y 112.
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private const int ActionCreate = 4;
    private const int ActionDelete = 5;
    private const int ActionEnter  = 6;

    // Create-form class buttons — actionIds 10-13.
    // spec: Docs/RE/specs/ui_system.md §8.2. CODE-CONFIRMED.
    private const int ActionClass0 = 10;

    // Face ± actions 21/22. spec: §8.2. CODE-CONFIRMED.
    private const int ActionFaceIncr = 21;
    private const int ActionFaceDecr = 22;

    // Point-buy ± actions 25-34 (col-1 row 0..4 = 25/27/29/31/33; col-2 row 0..4 = 26/28/30/32/34).
    // spec: Docs/RE/specs/ui_system.md §8.2. CODE-CONFIRMED.
    private const int ActionPointBuyBase = 25;

    // Create-form Confirm/Cancel actions 35/36.
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private const int ActionConfirm = 35;
    private const int ActionCancel  = 36;

    // Stat grid — 10 cells (2×5), base-Y 191, stride 24, cell 24×16.
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private const int StatGridRows    = 5;
    private const int StatGridBaseY   = 191; // spec §8.2+§8.4. CODE-CONFIRMED.
    private const int StatGridStride  = 24;  // spec §8.2+§8.4. CODE-CONFIRMED.
    private const int StatIconW       = 24;
    private const int StatIconH       = 16;
    private const int StatCol1X       = 154; // spec §8.2. CODE-CONFIRMED.
    private const int StatCol2X       = 178; // spec §8.2. CODE-CONFIRMED.
    private const int StatValX        = 51;  // spec §8.2+§8.4. CODE-CONFIRMED.
    private const int StatValBaseY    = 193; // spec §8.2+§8.4. CODE-CONFIRMED.
    private const int StatValW        = 35;
    private const int StatValH        = 12;

    // Col-1 NORMAL/HOVER src. spec §8.2. CODE-CONFIRMED.
    private const int StatCol1NormX  = 500;
    private const int StatCol1NormY  = 770;
    private const int StatCol1HovX   = 548;
    // Col-2 NORMAL/HOVER src. spec §8.2. CODE-CONFIRMED.
    private const int StatCol2NormX  = 524;
    private const int StatCol2NormY  = 770;
    private const int StatCol2HovX   = 572;

    // Roster button row — loginwindow.dds, dst-Y 112, w=59 h=20.
    // spec: Docs/RE/specs/ui_system.md §8.4. CODE-CONFIRMED.
    private const float RosterBtnY  = 112f; // spec §8.2+§8.4 dst-Y 112. CODE-CONFIRMED.
    private const float RosterBtnW  = 59f;
    private const float RosterBtnH  = 20f;

    // Create-form confirm/cancel — on loginwindow.dds create-plate row V=1004.
    // dst-Y 325, w=59, h=20. Confirm dst-X 42, Cancel dst-X 112.
    // HOVER src-X 413 (Confirm) / 531 (Cancel).
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private const int ConfirmDstX    = 42;
    private const int CancelDstX     = 112;
    private const int ConfirmCancelY = 325; // spec §8.4 "dst-Y 325". CODE-CONFIRMED.
    private const int ConfirmCancelW = 59;
    private const int ConfirmCancelH = 20;
    private const int ConfirmNormSrcX = 354; // spec §8.4. CODE-CONFIRMED.
    private const int ConfirmHovSrcX  = 413; // spec §8.4. CODE-CONFIRMED.
    private const int CancelNormSrcX  = 472; // spec §8.4. CODE-CONFIRMED.
    private const int CancelHovSrcX   = 531; // spec §8.4. CODE-CONFIRMED.
    private const int ConfirmCancelSrcY = 1004; // spec §8.4 "V=1004". CODE-CONFIRMED.

    // Corner close — fallback dst (971,610) 23×23, blacksheet.dds src (941,910).
    // spec: Docs/RE/specs/ui_system.md §8.2 close action CODE-CONFIRMED; dst+src debugger-pending.
    // TODO(spec): corner-close dst and src are base-window-chrome / debugger-pending.
    private const int CloseDstX = 971;
    private const int CloseDstY = 610;
    private const int CloseSrcX = 941;
    private const int CloseSrcY = 910;
    private const int CloseSz   = 23;

    // Name modal — centred 340×190, chrome from inventwindow.dds src(318,647).
    // spec: Docs/RE/specs/ui_system.md §8.3. CODE-CONFIRMED.
    private const float ModalW = 340f;
    private const float ModalH = 190f;

    // Name textbox in modal: (60,80,274,18).
    // spec: Docs/RE/specs/ui_system.md §8.2. CODE-CONFIRMED.
    private const float NameBoxX = 60f;
    private const float NameBoxY = 80f;
    private const float NameBoxW = 274f;
    private const float NameBoxH = 18f;

    // Face index range 1-7. spec: frontend_scenes.md §4.2+§9. CODE-CONFIRMED.
    private const int FaceMin = 1;
    private const int FaceMax = 7;

    // Class button NORMAL src-Y 1005; NORMAL src-X {590,635,680,725}; HOVER src-X {770,815,860,905}.
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private static readonly int[] ClassBtnNormSrcX = [590, 635, 680, 725];
    private static readonly int[] ClassBtnHovSrcX  = [770, 815, 860, 905];
    private const int ClassBtnSrcY = 1005; // spec §8.2. CODE-CONFIRMED.
    private const int ClassBtnW    = 45;   // AppSelectorW. CODE-CONFIRMED.
    private const int ClassBtnH    = 19;   // AppSelectorH. CODE-CONFIRMED.

    // UI index → internal class id. spec: frontend_scenes.md §4.1. CODE-CONFIRMED.
    // UI {0,1,2,3} → internal {4,1,3,2} (Monk/Musa/Dosa/Salsu).
    private static readonly int[] UiToInternal = [4, 1, 3, 2]; // CODE-CONFIRMED.

    // Class name msg ids 14003-14006 (4 classes). spec: ui_system.md §8.2. CODE-CONFIRMED.
    private static readonly uint[] ClassMsgIds = [14003u, 14004u, 14005u, 14006u];

    // Char-count caption: msg 2209. spec: frontend_scenes.md §3.8.2. CODE-CONFIRMED.
    private const uint MsgCharCount = 2209u;

    // =========================================================================
    // Injectable services (set by SelectScene before _Ready fires)
    // =========================================================================

    /// <summary>Atlas library (from ClientContext.HudAtlas). Null = offline.</summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>Text library (from ClientContext.HudText). Null = offline.</summary>
    public HudTextLibrary? Text { get; set; }

    // =========================================================================
    // View state (NO domain state)
    // =========================================================================

    private readonly LiveSlot[] _slots = new LiveSlot[MaxSlots];
    private int _selectedSlot;
    private int _createUiClass;   // 0..3
    private int _createFaceIndex = FaceMin;
    private bool _createFormVisible;
    private bool _rotatePressLeft;
    private bool _rotatePressRight;
    private double _toastTimer;

    // Shared assets for 3D previews.
    private RealClientAssets? _realAssets;

    // Cached node references (UI widgets updated dynamically).
    // HudLabel wraps a Godot Label; use GetControl() to add to parent, and .Text for mutations.
    private HudLabel?   _charCountCaptionWidget;
    private HudLabel?   _infoNameWidget;
    private HudLabel?   _infoLevelWidget;
    private HudLabel?   _infoClassWidget;
    private HudLabel?   _createFaceLabelWidget;
    private HudLabel?   _createDescLine0Widget;
    private HudLabel?   _createDescLine1Widget;
    private HudLabel?   _createDescLine2Widget;
    private HudLabel?   _nameModalTitleWidget;
    private LineEdit?   _nameEntry;
    private HudLabel?   _nameToastWidget;
    private Control?    _createForm;
    private Control?    _nameModal;
    private HudLabel[]  _statValLabelWidgets = [];

    // Convenience accessors — resolve the backing Label from HudLabel for HorizontalAlignment etc.
    private Label? _charCountCaption => (Label?)_charCountCaptionWidget?.GetControl();
    private Label? _infoName         => (Label?)_infoNameWidget?.GetControl();
    private Label? _infoLevel        => (Label?)_infoLevelWidget?.GetControl();
    private Label? _infoClass        => (Label?)_infoClassWidget?.GetControl();
    private Label? _createFaceLabel  => (Label?)_createFaceLabelWidget?.GetControl();
    private Label? _createDescLine0  => (Label?)_createDescLine0Widget?.GetControl();
    private Label? _createDescLine1  => (Label?)_createDescLine1Widget?.GetControl();
    private Label? _createDescLine2  => (Label?)_createDescLine2Widget?.GetControl();
    private Label? _nameModalTitle   => (Label?)_nameModalTitleWidget?.GetControl();
    private Label? _nameToast        => (Label?)_nameToastWidget?.GetControl();

    // 3D scene references (REUSED — not rebuilt here).
    private CharSelectScene3D?    _scene3D;
    private CharCreatePreview3D?  _createPreview3D;
    private SubViewport?          _scene3DViewport;
    private SubViewportContainer? _scene3DContainer;

    // npc.scr class descriptions (CP949, already decoded by NpcScrDescriptions).
    private NpcScrDescriptions _npcScrDesc = null!;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Called on the Godot main thread (via CharSelectEventDrainer._Process) when a
    /// CharacterListEvent (opcode 3/1) arrives.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1. CODE-CONFIRMED.
    /// </summary>
    public void ApplyCharacterList(ImmutableArray<CharacterListSlot> slots)
    {
        for (int i = 0; i < MaxSlots; i++)
            _slots[i] = new LiveSlot(IsEmpty: true);

        foreach (CharacterListSlot s in slots)
        {
            int idx = s.SlotIndex;
            if (idx < 0 || idx >= MaxSlots) continue;

            bool empty = s.Name == BlankSentinel || string.IsNullOrEmpty(s.Name);
            _slots[idx] = new LiveSlot(
                IsEmpty: empty,
                Name: empty ? string.Empty : s.Name,
                Level: s.Level,
                ServerClass: s.ServerClass,
                CurrentHp: s.CurrentHp,
                SlotIndex: idx);
        }

        _selectedSlot = 0;
        RefreshInfo();
        RefreshCharCountCaption();
        PushSlotDescriptors();
        _scene3D?.RefreshSlotActors(_realAssets);
        _scene3D?.SetSelectedSlot(_selectedSlot);

        GD.Print($"[CharSelectWindow] ApplyCharacterList: {slots.Length} slots; " +
                 $"atlas={Atlas is not null}.");
    }

    /// <summary>DEV-ONLY: opens the create form for screenshot/oracle verification.</summary>
    public void DevShowCreateForm() => ShowCreateForm();

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        // POINT-ANCHOR the root — no FullRect to avoid the control.cpp:1487 warning.
        // spec: PRESERVATION_AND_ARCHITECTURE.md §05 — "POINT-anchor the window ROOT".
        Position = Vector2.Zero;
        Size = new Vector2(RefW, RefH);
        CustomMinimumSize = new Vector2(RefW, RefH);
        MouseFilter = MouseFilterEnum.Ignore;

        try
        {
            _realAssets = RealClientAssets.TryOpen();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectWindow] VFS open for 3D previews failed: {ex.Message}");
        }

        _npcScrDesc = NpcScrDescriptions.Load(_realAssets);

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectWindow] BuildUi failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        _realAssets?.Dispose();
        _realAssets = null;
    }

    public override void _Process(double delta)
    {
        // Turntable drive (press-and-hold). spec: frontend_scenes.md §4.2. CODE-CONFIRMED.
        if (_createFormVisible && _createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            float dt = (float)delta;
            if (_rotatePressLeft)  _createPreview3D.RotateLeft(dt);
            if (_rotatePressRight) _createPreview3D.RotateRight(dt);
        }

        // Toast timer.
        if (_toastTimer > 0.0)
        {
            _toastTimer -= delta;
            if (_toastTimer <= 0.0)
            {
                _toastTimer = 0.0;
                if (_nameToast is not null && IsInstanceValid(_nameToast))
                    _nameToast.Visible = false;
            }
        }
    }

    // =========================================================================
    // UI construction — SELECT view
    // =========================================================================

    private void BuildUi()
    {
        // ── LAYER 0: 3D scene SubViewport (full canvas, bottom layer) ──
        // spec: Docs/RE/specs/frontend_scenes.md §3. CODE-CONFIRMED.
        Build3DSceneViewport();

        // ── LAYER 1: 2D chrome (drawn over the 3D viewport) ──

        // Top tabs: Server (1), Channel (2), Back (3).
        // spec: Docs/RE/specs/frontend_scenes.md §11.5b. CODE-CONFIRMED.
        BuildTabButton(
            x: 67, y: 17, w: 113, h: 40,
            normSrcX: 675, normSrcY: 795,
            hovSrcX: 675,  hovSrcY: 795,
            prsSrcX: 483,  prsSrcY: 883,  // PRESSED src. spec §11.5b. CODE-CONFIRMED.
            actionId: ActionServer);

        BuildTabButton(
            x: 232, y: 7, w: 113, h: 40,
            normSrcX: 640, normSrcY: 742,
            hovSrcX: 640,  hovSrcY: 742,
            prsSrcX: 483,  prsSrcY: 923,  // PRESSED src. spec §11.5b. CODE-CONFIRMED.
            actionId: ActionChannel);

        BuildTabButton(
            x: 393, y: 17, w: 113, h: 40,
            normSrcX: 625, normSrcY: 691,
            hovSrcX: 625,  hovSrcY: 691,
            prsSrcX: 483,  prsSrcY: 963,  // PRESSED src. spec §11.5b. CODE-CONFIRMED.
            actionId: ActionBack);

        // Char-count caption — msg 2209, orange, top-centre.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2. CODE-CONFIRMED.
        _charCountCaptionWidget = HudWidgetFactory.MakeLabel(
            x: 0, y: 12, w: (int)RefW, h: 28,
            text: BuildCharCountCaption(),
            color: new Color(0.95f, 0.86f, 0.55f));
        if (_charCountCaption is not null)
        {
            _charCountCaption.HorizontalAlignment = HorizontalAlignment.Center;
            AddChild(_charCountCaption);
        }

        // Left char-info background art: loginwindow.dds src(556,542) 215×147.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5d. CODE-CONFIRMED.
        const float infoPanelX = (RefW - 215f) / 2f; // spec §11.5a "X=(W-215)/2". CODE-CONFIRMED.
        const float infoPanelY = 575f;                // spec §11.5a "y=575". CODE-CONFIRMED.
        AddAtlasRect(AtlasLoginWindow,
            (int)infoPanelX, (int)(infoPanelY + 142f),
            215, 147, 556, 542); // spec §11.5d. CODE-CONFIRMED.

        // Chrome plates A/B/C from loginwindow.dds.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5a. CODE-CONFIRMED.
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 0,   (int)infoPanelY + 12, 200, 46, 608, 793);
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 200, (int)infoPanelY + 0,  176, 58, 608, 735);
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 376, (int)infoPanelY + 12, 201, 46, 608, 689);
        AddAtlasRect(AtlasLoginWindow, (int)infoPanelX + 215, (int)infoPanelY + 0,  29, 22, 556, 729);

        // Info labels (name/level/class), refreshed on slot selection.
        float infoLX = infoPanelX + 46f;
        _infoNameWidget  = AddInfoLabel(new Vector2(infoLX, infoPanelY + 145f));
        _infoLevelWidget = AddInfoLabel(new Vector2(infoLX, infoPanelY + 169f));
        _infoClassWidget = AddInfoLabel(new Vector2(infoLX, infoPanelY + 193f));

        // Stat-icon grid (10 cells, 2×5) — in the char-info panel area.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        _statValLabelWidgets = new HudLabel[StatGridRows];
        for (int row = 0; row < StatGridRows; row++)
        {
            int y = StatGridBaseY + row * StatGridStride;

            // Col 1 — NORMAL(500,770) HOVER(548,770). spec §8.2+§8.4. CODE-CONFIRMED.
            BuildGridCell(
                x: StatCol1X, y: y,
                normSrcX: StatCol1NormX, normSrcY: StatCol1NormY,
                hovSrcX: StatCol1HovX,  hovSrcY: StatCol1NormY,
                actionId: ActionPointBuyBase + row * 2);

            // Col 2 — NORMAL(524,770) HOVER(572,770). spec §8.2+§8.4. CODE-CONFIRMED.
            BuildGridCell(
                x: StatCol2X, y: y,
                normSrcX: StatCol2NormX, normSrcY: StatCol2NormY,
                hovSrcX: StatCol2HovX,  hovSrcY: StatCol2NormY,
                actionId: ActionPointBuyBase + row * 2 + 1);

            // Stat value label (x=51, base-Y 193, stride 24, 35×12).
            // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
            var valLabelWidget = HudWidgetFactory.MakeLabel(
                StatValX,
                StatValBaseY + row * StatGridStride,
                StatValW, StatValH,
                text: "–",
                color: new Color(0.85f, 0.85f, 0.90f));
            _statValLabelWidgets[row] = valLabelWidget;
            Control? valCtrl = valLabelWidget.GetControl();
            if (valCtrl is not null) AddChild(valCtrl);
        }

        // Roster action buttons: Create/Delete/Enter at dst-Y 112 (centred).
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        const float rosterBarCX = RefW / 2f;
        const float rosterBarLeft = rosterBarCX - (3f * RosterBtnW + 2f * 8f) / 2f;

        BuildRosterButton((int)rosterBarLeft,              (int)RosterBtnY,
            normSrcX: 0,   normSrcY: 1004, prsSrcX: 59,  prsSrcY: 1004, // spec §8.4. CODE-CONFIRMED.
            actionId: ActionCreate);
        BuildRosterButton((int)(rosterBarLeft + RosterBtnW + 8f), (int)RosterBtnY,
            normSrcX: 118, normSrcY: 1004, prsSrcX: 177, prsSrcY: 1004, // spec §8.4. CODE-CONFIRMED.
            actionId: ActionDelete);
        BuildRosterButton((int)(rosterBarLeft + 2f * (RosterBtnW + 8f)), (int)RosterBtnY,
            normSrcX: 236, normSrcY: 1004, prsSrcX: 295, prsSrcY: 1004, // spec §8.4. CODE-CONFIRMED.
            actionId: ActionEnter);

        // Corner close: fallback dst (971,610) 23×23, blacksheet.dds src (941,910).
        // spec: Docs/RE/specs/ui_system.md §8.2 close action CODE-CONFIRMED; dst+src debugger-pending.
        // TODO(spec): corner-close geometry is base-window-chrome / debugger-pending.
        if (Atlas is not null)
        {
            var closeBtn = HudWidgetFactory.MakeButton2(
                Atlas, AtlasBlacksheet,
                CloseDstX, CloseDstY, CloseSz, CloseSz,
                CloseSrcX, CloseSrcY,
                actionId: -99); // internal close action (not a wire action)
            closeBtn.ActionFired += _ => EmitSignal(SignalName.BackRequested);
            AddChild(closeBtn.GetControl()!);
        }

        // 3D ray-pick on the viewport container.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.3. CODE-CONFIRMED.
        if (_scene3DContainer is not null)
            _scene3DContainer.GuiInput += OnViewport3DGuiInput;

        // Create sub-form (initially hidden).
        _createForm = BuildCreateForm();
        _createForm.Visible = false;
        AddChild(_createForm);

        RefreshInfo();

        GD.Print($"[CharSelectWindow] built; atlas={Atlas is not null}; " +
                 "slots=BLANK-until-CharacterListEvent; 3D viewport deferred. " +
                 "spec: ui_system.md §8.2/§8.4.");
    }

    // =========================================================================
    // 3D scene SubViewport. spec: frontend_scenes.md §3.7. CODE-CONFIRMED.
    // =========================================================================

    private void Build3DSceneViewport()
    {
        _scene3DViewport = new SubViewport
        {
            Name = "CharSelect3DViewport",
            Size = new Vector2I(1024, 768), // spec §8.0 "1024×768". CODE-CONFIRMED.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };

        _scene3D = new CharSelectScene3D { Name = "CharSelectScene3D" };
        PushSlotDescriptors();
        _scene3DViewport.AddChild(_scene3D);

        _scene3DContainer = new SubViewportContainer
        {
            Name = "Scene3DContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _scene3DContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _scene3DContainer.AddChild(_scene3DViewport);
        AddChild(_scene3DContainer);

        Callable.From(InitialiseScene3D).CallDeferred();
    }

    private void InitialiseScene3D()
    {
        if (_scene3D is null || !IsInstanceValid(_scene3D)) return;
        try
        {
            _scene3D.Initialise(_realAssets);
            _scene3D.SetSelectedSlot(_selectedSlot);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectWindow] InitialiseScene3D failed: {ex.Message}");
        }
    }

    private void PushSlotDescriptors()
    {
        if (_scene3D is null) return;
        var descs = new (bool IsOccupied, uint SkinClassId)[MaxSlots];
        for (int i = 0; i < MaxSlots; i++)
        {
            LiveSlot ls = _slots[i];
            descs[i] = (!ls.IsEmpty, !ls.IsEmpty ? (uint)ls.ServerClass : 0u);
        }
        _scene3D.SlotDescriptors = descs;
    }

    // =========================================================================
    // Create sub-form (3-column layout over the carved-wall scene backdrop).
    // spec: Docs/RE/specs/frontend_scenes.md §4. CODE-CONFIRMED.
    // =========================================================================

    private Control BuildCreateForm()
    {
        var form = new Control
        {
            Name = "CreateForm",
            MouseFilter = MouseFilterEnum.Stop,
        };
        form.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Char-count caption (same msg 2209 as select view).
        // spec: frontend_scenes.md §3.8.2. CODE-CONFIRMED.
        var topCaptionWidget = HudWidgetFactory.MakeLabel(
            0, 14, (int)RefW, 28,
            text: BuildCharCountCaption(),
            color: new Color(0.95f, 0.86f, 0.55f));
        if (topCaptionWidget.GetControl() is Label topCaptionLabel)
        {
            topCaptionLabel.Name = "CreateCountCaption";
            topCaptionLabel.HorizontalAlignment = HorizontalAlignment.Center;
            form.AddChild(topCaptionLabel);
        }

        // ── Create preview 3D — full-screen backdrop (OwnWorld3D=true).
        // spec: frontend_scenes.md §4/§3.7.6. CODE-CONFIRMED.
        _createPreview3D = new CharCreatePreview3D
        {
            Name = "CreatePreview3D",
            MouseFilter = MouseFilterEnum.Ignore,
            SharedRealAssets = _realAssets,
            InternalClassId = UiToInternal[_createUiClass],
        };
        _createPreview3D.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        form.AddChild(_createPreview3D);

        // ── LEFT COLUMN: class buttons + npc.scr description ──
        // spec: frontend_scenes.md §4/§11.5e. CODE-CONFIRMED.
        var leftPanel = new Control
        {
            Name = "CreateLeft",
            Position = new Vector2(8f, 46f),
            Size = new Vector2(200f, 680f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(leftPanel);

        // 4 class buttons — vertical column, actions 10-13.
        // NORMAL src-Y 1005, NORMAL src-X {590,635,680,725}, HOVER src-X {770,815,860,905}.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        const int classBtnStride = 28; // vertical stride in left column.
        const int classBtnColX   = 8;
        const int classBtnBaseY  = 10;
        for (int ci = 0; ci < 4; ci++)
        {
            int btnY = classBtnBaseY + ci * classBtnStride;

            if (Atlas is not null)
            {
                var classBtn = HudWidgetFactory.MakeButton3(
                    Atlas, AtlasLoginWindow,
                    classBtnColX, btnY, ClassBtnW, ClassBtnH,
                    ClassBtnNormSrcX[ci], ClassBtnSrcY, // NORMAL. spec §8.2+§8.4. CODE-CONFIRMED.
                    ClassBtnHovSrcX[ci],  ClassBtnSrcY, // HOVER.  spec §8.2+§8.4. CODE-CONFIRMED.
                    actionId: ActionClass0 + ci);       // 10/11/12/13.
                classBtn.ActionFired += OnCreateClassAction;
                classBtn.GetControl()!.Name = $"ClassBtn{ci}";
                leftPanel.AddChild(classBtn.GetControl()!);
            }
        }

        // npc.scr description — 3 lines from NpcScrDescriptions (CP949, already decoded).
        // spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1. CODE-CONFIRMED.
        string[] descLines = GetDescLines(_createUiClass);
        const float descBaseY = classBtnBaseY + 4 * classBtnStride + 16f;
        const float descStride = 60f;
        var descColor = new Color(0.82f, 0.82f, 0.88f);

        _createDescLine0Widget = HudWidgetFactory.MakeLabel(
            6, (int)(descBaseY), (int)(200f - 12f), 56,
            descLines.Length > 0 ? descLines[0] : string.Empty, descColor, multiline: true);
        if (_createDescLine0Widget.GetControl() is Label d0Lbl)
        { d0Lbl.Name = "DescLine0"; leftPanel.AddChild(d0Lbl); }

        _createDescLine1Widget = HudWidgetFactory.MakeLabel(
            6, (int)(descBaseY + descStride), (int)(200f - 12f), 56,
            descLines.Length > 1 ? descLines[1] : string.Empty, descColor, multiline: true);
        if (_createDescLine1Widget.GetControl() is Label d1Lbl)
        { d1Lbl.Name = "DescLine1"; leftPanel.AddChild(d1Lbl); }

        _createDescLine2Widget = HudWidgetFactory.MakeLabel(
            6, (int)(descBaseY + 2f * descStride), (int)(200f - 12f), 56,
            descLines.Length > 2 ? descLines[2] : string.Empty, descColor, multiline: true);
        if (_createDescLine2Widget.GetControl() is Label d2Lbl)
        { d2Lbl.Name = "DescLine2"; leftPanel.AddChild(d2Lbl); }

        // ── CENTER COLUMN: face ± + turntable ──
        // spec: frontend_scenes.md §4.2. CODE-CONFIRMED.
        var centerPanel = new Control
        {
            Name = "CreateCenter",
            Position = new Vector2(215f, 46f),
            Size = new Vector2(595f, 680f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(centerPanel);

        // Face ± buttons, actions 21/22. spec §8.2. CODE-CONFIRMED.
        if (Atlas is not null)
        {
            var faceDecrBtn = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                40, 626, 28, 22,
                483, 490, // face-decrement glyph. spec §8.2. CODE-CONFIRMED.
                actionId: ActionFaceDecr);
            faceDecrBtn.ActionFired += OnFaceAction;
            centerPanel.AddChild(faceDecrBtn.GetControl()!);

            var faceIncrBtn = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                106, 626, 28, 22,
                505, 490, // face-increment glyph. spec §8.2. CODE-CONFIRMED.
                actionId: ActionFaceIncr);
            faceIncrBtn.ActionFired += OnFaceAction;
            centerPanel.AddChild(faceIncrBtn.GetControl()!);
        }

        _createFaceLabelWidget = HudWidgetFactory.MakeLabel(
            73, 629, 28, 20,
            text: _createFaceIndex.ToString(),
            color: new Color(0.95f, 0.95f, 0.95f));
        if (_createFaceLabel is not null)
        {
            _createFaceLabel.Name = "FaceIndexLabel";
            _createFaceLabel.HorizontalAlignment = HorizontalAlignment.Center;
            centerPanel.AddChild(_createFaceLabel);
        }

        // Turntable L/R (press-and-hold ≈±2 rad/s), actions 23/24.
        // spec: frontend_scenes.md §4.2. CODE-CONFIRMED.
        if (Atlas is not null)
        {
            var rotLeft = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                150, 626, 36, 22, 483, 490, actionId: 23);
            rotLeft.GetControl()!.Name = "RotLeft";
            rotLeft.GetControl()!.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                    _rotatePressLeft = mb.Pressed;
            };
            centerPanel.AddChild(rotLeft.GetControl()!);

            var rotRight = HudWidgetFactory.MakeButton2(
                Atlas, AtlasLoginWindow,
                192, 626, 36, 22, 505, 490, actionId: 24);
            rotRight.GetControl()!.Name = "RotRight";
            rotRight.GetControl()!.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                    _rotatePressRight = mb.Pressed;
            };
            centerPanel.AddChild(rotRight.GetControl()!);
        }

        // ── RIGHT COLUMN: chrome plate + description lower plate ──
        // spec: frontend_scenes.md §11.5e/§4.1.1. CODE-CONFIRMED.
        var rightPanel = new Control
        {
            Name = "CreateRight",
            Position = new Vector2(815f, 46f),
            Size = new Vector2(201f, 680f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(rightPanel);

        AddAtlasRectTo(rightPanel, AtlasMainWindow,   0, 0,   201, 274, 0, 0);
        AddAtlasRectTo(rightPanel, AtlasInventWindow, 0, 274, 201, 190, 318, 647);

        // ── Point-buy ± stat grid on the create form (10 cells, 2×5, actions 25-34) ──
        // The same 10-cell grid doubles as the create-form point-buy ± buttons.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        var statPanel = new Control
        {
            Name = "CreateStatGrid",
            Position = new Vector2(8f, 300f),
            Size = new Vector2(200f, 200f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(statPanel);

        for (int row = 0; row < StatGridRows; row++)
        {
            int y = row * StatGridStride;

            // Col 1 — actions 25/27/29/31/33.
            BuildGridCellInto(statPanel, 0, y,
                normSrcX: StatCol1NormX, normSrcY: StatCol1NormY,
                hovSrcX: StatCol1HovX,  hovSrcY: StatCol1NormY,
                actionId: ActionPointBuyBase + row * 2);

            // Col 2 — actions 26/28/30/32/34.
            BuildGridCellInto(statPanel, StatIconW + 4, y,
                normSrcX: StatCol2NormX, normSrcY: StatCol2NormY,
                hovSrcX: StatCol2HovX,  hovSrcY: StatCol2NormY,
                actionId: ActionPointBuyBase + row * 2 + 1);
        }

        // Confirm (35) / Cancel (36) buttons on the create form.
        // dst-Y 325 on create-plate row art V=1004.
        // Confirm: dst-X 42, NORMAL(354,1004), HOVER src-X 413.
        // Cancel:  dst-X 112, NORMAL(472,1004), HOVER src-X 531.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        if (Atlas is not null)
        {
            var confirmBtn = HudWidgetFactory.MakeButton3(
                Atlas, AtlasLoginWindow,
                ConfirmDstX, ConfirmCancelY, ConfirmCancelW, ConfirmCancelH,
                ConfirmNormSrcX, ConfirmCancelSrcY, // NORMAL. spec §8.4. CODE-CONFIRMED.
                ConfirmHovSrcX,  ConfirmCancelSrcY, // HOVER.  spec §8.4. CODE-CONFIRMED.
                actionId: ActionConfirm);
            confirmBtn.ActionFired += OnCreateConfirmAction;
            form.AddChild(confirmBtn.GetControl()!);

            var cancelBtn = HudWidgetFactory.MakeButton3(
                Atlas, AtlasLoginWindow,
                CancelDstX, ConfirmCancelY, ConfirmCancelW, ConfirmCancelH,
                CancelNormSrcX, ConfirmCancelSrcY, // NORMAL. spec §8.4. CODE-CONFIRMED.
                CancelHovSrcX,  ConfirmCancelSrcY, // HOVER.  spec §8.4. CODE-CONFIRMED.
                actionId: ActionCancel);
            cancelBtn.ActionFired += _ => HideCreateForm();
            form.AddChild(cancelBtn.GetControl()!);
        }

        // Name modal (on-demand).
        _nameModal = BuildNameModal();
        _nameModal.Visible = false;
        form.AddChild(_nameModal);

        return form;
    }

    // =========================================================================
    // Name modal — on-demand overlay; class name title + name textbox + Confirm/Cancel.
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1/§4.4. CODE-CONFIRMED.
    // =========================================================================

    private Control BuildNameModal()
    {
        float modalX = (RefW - ModalW) / 2f;
        float modalY = (RefH - ModalH) / 2f;

        var modal = new Control
        {
            Name = "NameModal",
            Position = new Vector2(modalX, modalY),
            Size = new Vector2(ModalW, ModalH),
            MouseFilter = MouseFilterEnum.Stop,
        };

        // Chrome: inventwindow.dds 340×190 src(318,647). spec §8.3. CODE-CONFIRMED.
        var bg = Atlas?.SliceByPath(AtlasInventWindow, 318, 647, (int)ModalW, (int)ModalH);
        if (bg is not null)
        {
            var bgRect = new TextureRect
            {
                Name = "NameModalBg",
                Texture = bg,
                Position = Vector2.Zero,
                Size = new Vector2(ModalW, ModalH),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            modal.AddChild(bgRect);
        }

        // Class name title (msg 14003..14006 — updated on class change).
        // spec: frontend_scenes.md §4.1.1. CODE-CONFIRMED.
        _nameModalTitleWidget = HudWidgetFactory.MakeLabel(
            12, 12, (int)(ModalW - 24f), 22,
            text: ClassCaption(_createUiClass),
            color: new Color(0.95f, 0.86f, 0.55f));
        if (_nameModalTitle is not null)
        {
            _nameModalTitle.Name = "NameModalTitle";
            _nameModalTitle.HorizontalAlignment = HorizontalAlignment.Center;
            modal.AddChild(_nameModalTitle);
        }

        // Name LineEdit (textbox). spec: §8.2/§4.4. CODE-CONFIRMED.
        _nameEntry = new LineEdit
        {
            Name = "NameEntry",
            Position = new Vector2(NameBoxX, NameBoxY),
            Size = new Vector2(NameBoxW, NameBoxH),
        };
        _nameEntry.AddThemeFontSizeOverride("font_size", 13);
        modal.AddChild(_nameEntry);

        // Toast label (msg 2075). spec: frontend_scenes.md §4.4. CODE-CONFIRMED.
        _nameToastWidget = HudWidgetFactory.MakeLabel(
            (int)NameBoxX, 96, (int)NameBoxW, 30,
            text: string.Empty,
            color: new Color(1.0f, 0.35f, 0.20f),
            multiline: true);
        if (_nameToast is not null)
        {
            _nameToast.Name = "NameToast";
            _nameToast.Visible = false;
            modal.AddChild(_nameToast);
        }

        // Modal Confirm (35) / Cancel (36) via inventwindow.dds.
        // spec: §8.2. CODE-CONFIRMED.
        if (Atlas is not null)
        {
            var ok = HudWidgetFactory.MakeButton2(
                Atlas, AtlasInventWindow,
                55, 136, 113, 40,
                302, 860, // spec §8.3. CODE-CONFIRMED.
                actionId: ActionConfirm,
                caption: Text?.GetCaption(2301u, string.Empty) ?? string.Empty);
            ok.ActionFired += OnModalConfirm;
            modal.AddChild(ok.GetControl()!);

            var cancel = HudWidgetFactory.MakeButton2(
                Atlas, AtlasInventWindow,
                174, 136, 113, 40,
                302, 900, // spec §8.3. CODE-CONFIRMED.
                actionId: ActionCancel,
                caption: Text?.GetCaption(2302u, string.Empty) ?? string.Empty);
            cancel.ActionFired += _ => HideNameModal();
            modal.AddChild(cancel.GetControl()!);
        }

        return modal;
    }

    // =========================================================================
    // 3D ray-pick. spec: frontend_scenes.md §3.3.3. CODE-CONFIRMED.
    // =========================================================================

    private void OnViewport3DGuiInput(InputEvent ev)
    {
        if (_scene3D is null || _scene3DViewport is null || _scene3DContainer is null) return;
        if (_createFormVisible) return;

        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
        {
            global::Godot.Vector2 vpSize = new(_scene3DViewport.Size.X, _scene3DViewport.Size.Y);
            global::Godot.Vector2 ctrlSize = _scene3DContainer.Size;
            float scaleX = ctrlSize.X > 0f ? vpSize.X / ctrlSize.X : 1f;
            float scaleY = ctrlSize.Y > 0f ? vpSize.Y / ctrlSize.Y : 1f;
            global::Godot.Vector2 vpPos = new(mb.Position.X * scaleX, mb.Position.Y * scaleY);

            int hit = _scene3D.TryHitTestSlot(vpPos);
            if (hit >= 0)
            {
                _selectedSlot = hit;
                RefreshInfo();
                _scene3D?.SetSelectedSlot(hit);
                GD.Print($"[CharSelectWindow] 3D ray-pick → slot {hit}. spec: frontend_scenes.md §3.3.3.");
            }
        }
    }

    // =========================================================================
    // Action handlers
    // =========================================================================

    private void OnTabButtonFired(int actionId)
    {
        switch (actionId)
        {
            case ActionBack:
                GD.Print("[CharSelectWindow] Back tab (action 3).");
                EmitSignal(SignalName.BackRequested);
                break;
            default:
                GD.Print($"[CharSelectWindow] Tab action {actionId} — stub (offline/server-select pending).");
                break;
        }
    }

    private void OnRosterButtonFired(int actionId)
    {
        switch (actionId)
        {
            case ActionCreate:
                GD.Print("[CharSelectWindow] Create (action 4).");
                ShowCreateForm();
                break;
            case ActionDelete:
                OnDeletePressed();
                break;
            case ActionEnter:
                OnEnterPressed();
                break;
        }
    }

    private void OnEnterPressed()
    {
        LiveSlot ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            GD.Print($"[CharSelectWindow] Enter on empty slot {_selectedSlot} → Create form.");
            ShowCreateForm();
            return;
        }
        GD.Print($"[CharSelectWindow] EnterGameRequested: name='{ls.Name}' slot={_selectedSlot}.");
        EmitSignal(SignalName.EnterGameRequested, ls.Name, _selectedSlot);
    }

    private void OnDeletePressed()
    {
        LiveSlot ls = _slots[_selectedSlot];
        if (ls.IsEmpty)
        {
            GD.Print($"[CharSelectWindow] Delete on empty slot {_selectedSlot} ignored.");
            return;
        }
        GD.Print($"[CharSelectWindow] DeleteCharacterRequested: name='{ls.Name}' slot={_selectedSlot}.");
        EmitSignal(SignalName.DeleteCharacterRequested, _selectedSlot, ls.Name);
    }

    private void OnCreateClassAction(int actionId)
    {
        int uiIndex = actionId - ActionClass0; // 10-13 → 0-3.
        if (uiIndex >= 0 && uiIndex < 4) SetCreateClass(uiIndex);
    }

    private void OnFaceAction(int actionId)
    {
        // ActionId 21 = increment, 22 = decrement. spec §8.2. CODE-CONFIRMED.
        int delta = actionId == ActionFaceIncr ? +1 : -1;
        _createFaceIndex = Math.Clamp(_createFaceIndex + delta, FaceMin, FaceMax);
        GD.Print($"[CharSelectWindow] face={_createFaceIndex} " +
                 $"(range {FaceMin}..{FaceMax}). spec: frontend_scenes.md §4.2. CODE-CONFIRMED.");
        if (_createFaceLabel is not null && IsInstanceValid(_createFaceLabel))
            _createFaceLabel.Text = _createFaceIndex.ToString();
    }

    private void OnCreateConfirmAction(int _)
    {
        // Confirm (35) on the create form itself opens the name modal.
        // spec: frontend_scenes.md §4/§4.1.1. CODE-CONFIRMED.
        ShowNameModal();
    }

    private void OnModalConfirm(int _)
    {
        string name = _nameEntry?.Text.Trim() ?? string.Empty;
        if (!ValidateName(name, out string toastMsg))
        {
            ShowToast(toastMsg);
            GD.Print($"[CharSelectWindow] Create name rejected: '{name}' → toast.");
            return;
        }

        int internalClass = UiToInternal[_createUiClass];
        GD.Print($"[CharSelectWindow] CreateCharacterRequested: name='{name}' " +
                 $"class={internalClass} face={_createFaceIndex}. spec: frontend_scenes.md §4/§8.");
        EmitSignal(SignalName.CreateCharacterRequested, name, internalClass, _createFaceIndex);
        HideCreateForm();
        RefreshCharCountCaption();
    }

    // =========================================================================
    // Create form helpers
    // =========================================================================

    private void ShowCreateForm()
    {
        _createFormVisible = true;
        if (_createForm is not null && IsInstanceValid(_createForm))
            _createForm.Visible = true;

        // Hide char-select 3D backdrop — create preview fills the screen with its own cell.
        // spec: frontend_scenes.md §4/§3.7.6. CODE-CONFIRMED.
        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = false;

        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = UiToInternal[_createUiClass];
            _createPreview3D.RebuildForClass();
        }

        _toastTimer = 0.0;
        if (_nameToast is not null && IsInstanceValid(_nameToast))
            _nameToast.Visible = false;

        _rotatePressLeft = false;
        _rotatePressRight = false;

        GD.Print($"[CharSelectWindow] Create form opened (uiClass={_createUiClass} → " +
                 $"internal={UiToInternal[_createUiClass]}). spec: frontend_scenes.md §4.");
    }

    private void HideCreateForm()
    {
        _createFormVisible = false;
        if (_createForm is not null && IsInstanceValid(_createForm))
            _createForm.Visible = false;

        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = true;

        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;

        _rotatePressLeft = false;
        _rotatePressRight = false;
        _toastTimer = 0.0;
    }

    private void ShowNameModal()
    {
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);
        if (_nameEntry is not null && IsInstanceValid(_nameEntry))
            _nameEntry.Text = string.Empty;
        if (_nameToast is not null && IsInstanceValid(_nameToast))
            _nameToast.Visible = false;
        _toastTimer = 0.0;
        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = true;
        GD.Print($"[CharSelectWindow] Name modal opened for class {_createUiClass}.");
    }

    private void HideNameModal()
    {
        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;
        _toastTimer = 0.0;
    }

    private void SetCreateClass(int uiIndex)
    {
        _createUiClass = Math.Clamp(uiIndex, 0, 3);
        int internalClass = UiToInternal[_createUiClass];
        GD.Print($"[CharSelectWindow] Create class: UI={_createUiClass} → internal={internalClass}.");

        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClass);

        string[] lines = GetDescLines(_createUiClass);
        if (_createDescLine0 is not null && IsInstanceValid(_createDescLine0))
            _createDescLine0.Text = lines.Length > 0 ? lines[0] : string.Empty;
        if (_createDescLine1 is not null && IsInstanceValid(_createDescLine1))
            _createDescLine1.Text = lines.Length > 1 ? lines[1] : string.Empty;
        if (_createDescLine2 is not null && IsInstanceValid(_createDescLine2))
            _createDescLine2.Text = lines.Length > 2 ? lines[2] : string.Empty;

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = internalClass;
            _createPreview3D.RebuildForClass();
        }
    }

    // =========================================================================
    // Name validation. spec: frontend_scenes.md §4.4. CODE-CONFIRMED.
    // =========================================================================

    private bool ValidateName(string name, out string toastMsg)
    {
        // Empty → msg 2190. spec §4.4/§8.2. CODE-CONFIRMED.
        if (name.Length < 2)
        {
            toastMsg = Text?.GetCaption(2190u, string.Empty) ?? string.Empty;
            return false;
        }

        // Charset: a-z + 0-9 + CP949 Hangul. spec §4.4. CODE-CONFIRMED.
        foreach (char c in name)
        {
            if (c >= 'a' && c <= 'z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c >= '가' && c <= '힣') continue;
            if (c >= 'ᄀ' && c <= 'ᇿ') continue;
            if (c >= '㄰' && c <= '㆏') continue;
            // Charset violation → msg 12012. spec §8.2. CODE-CONFIRMED.
            toastMsg = Text?.GetCaption(12012, string.Empty) ?? string.Empty;
            return false;
        }

        toastMsg = string.Empty;
        return true;
    }

    private void ShowToast(string message)
    {
        if (_nameToast is null || !IsInstanceValid(_nameToast)) return;
        _nameToast.Text = message;
        _nameToast.Visible = true;
        _toastTimer = 3.0;
    }

    // =========================================================================
    // Char-count caption — msg 2209. spec: frontend_scenes.md §3.8.2. CODE-CONFIRMED.
    // =========================================================================

    private string BuildCharCountCaption()
    {
        int count = 0;
        for (int i = 0; i < MaxSlots; i++)
            if (!_slots[i].IsEmpty) count++;

        string template = Text?.GetCaption(MsgCharCount, string.Empty) ?? string.Empty;
        if (string.IsNullOrEmpty(template)) return string.Empty;
        return template.Contains("%d")
            ? template.Replace("%d", count.ToString())
            : string.Format(template, count);
    }

    private void RefreshCharCountCaption()
    {
        if (_charCountCaption is null || !IsInstanceValid(_charCountCaption)) return;
        _charCountCaption.Text = BuildCharCountCaption();
    }

    // =========================================================================
    // Info refresh
    // =========================================================================

    private void RefreshInfo()
    {
        LiveSlot ls = _slots[_selectedSlot];
        if (_infoName is not null && IsInstanceValid(_infoName))
            _infoName.Text = ls.IsEmpty ? string.Empty : ls.Name;
        if (_infoLevel is not null && IsInstanceValid(_infoLevel))
            _infoLevel.Text = ls.IsEmpty ? string.Empty : ls.Level.ToString();
        if (_infoClass is not null && IsInstanceValid(_infoClass))
            _infoClass.Text = ls.IsEmpty ? string.Empty : ls.ServerClass.ToString();
    }

    // =========================================================================
    // Class caption + npc.scr description helpers
    // =========================================================================

    private string ClassCaption(int uiIndex)
    {
        int safe = Math.Clamp(uiIndex, 0, 3);
        return Text?.GetCaption(ClassMsgIds[safe], string.Empty) ?? string.Empty;
    }

    private string[] GetDescLines(int uiIndex)
    {
        string joined = _npcScrDesc.GetDescription(Math.Clamp(uiIndex, 0, 3));
        if (string.IsNullOrEmpty(joined)) return [];
        return joined.Split('\n', StringSplitOptions.None);
    }

    // =========================================================================
    // Widget construction helpers
    // =========================================================================

    private void BuildTabButton(
        int x, int y, int w, int h,
        int normSrcX, int normSrcY,
        int hovSrcX,  int hovSrcY,
        int prsSrcX,  int prsSrcY,
        int actionId)
    {
        if (Atlas is null) return;
        var btn = HudWidgetFactory.MakeButton(
            Atlas, AtlasLoginWindow,
            x, y, w, h,
            normSrcX, normSrcY,
            prsSrcX,  prsSrcY,   // PRESSED
            hovSrcX,  hovSrcY,   // HOVER
            actionId);
        btn.ActionFired += OnTabButtonFired;
        AddChild(btn.GetControl()!);
    }

    private void BuildRosterButton(
        int x, int y,
        int normSrcX, int normSrcY,
        int prsSrcX,  int prsSrcY,
        int actionId)
    {
        if (Atlas is null) return;
        var btn = HudWidgetFactory.MakeButton(
            Atlas, AtlasLoginWindow,
            x, y, (int)RosterBtnW, (int)RosterBtnH,
            normSrcX, normSrcY,
            prsSrcX,  prsSrcY,
            normSrcX, normSrcY,  // HOVER = NORMAL (spec §1.5 2-state fallback)
            actionId);
        btn.ActionFired += OnRosterButtonFired;
        AddChild(btn.GetControl()!);
    }

    private void BuildGridCell(
        int x, int y,
        int normSrcX, int normSrcY,
        int hovSrcX,  int hovSrcY,
        int actionId)
    {
        BuildGridCellInto(this, x, y, normSrcX, normSrcY, hovSrcX, hovSrcY, actionId);
    }

    private void BuildGridCellInto(
        Control parent,
        int x, int y,
        int normSrcX, int normSrcY,
        int hovSrcX,  int hovSrcY,
        int actionId)
    {
        if (Atlas is null) return;
        var cell = HudWidgetFactory.MakeButton3(
            Atlas, AtlasLoginWindow,
            x, y, StatIconW, StatIconH,
            normSrcX, normSrcY,
            hovSrcX,  hovSrcY,
            actionId);
        // Point-buy ± intent: emitted as an ApplicationUseCases call by SelectScene.
        // We fire ActionFired here; SelectScene wires it. (No game logic in this class.)
        parent.AddChild(cell.GetControl()!);
    }

    private void AddAtlasRect(string vfsPath, int x, int y, int w, int h, int srcX, int srcY)
    {
        AddAtlasRectTo(this, vfsPath, x, y, w, h, srcX, srcY);
    }

    private void AddAtlasRectTo(
        Control parent, string vfsPath,
        int x, int y, int w, int h, int srcX, int srcY)
    {
        if (Atlas is null) return;
        TextureRect? rect = HudWidgetFactory.MakeAtlasRect(Atlas, vfsPath, x, y, w, h, srcX, srcY);
        if (rect is not null) parent.AddChild(rect);
    }

    private HudLabel AddInfoLabel(Vector2 pos)
    {
        HudLabel widget = HudWidgetFactory.MakeLabel(
            (int)pos.X, (int)pos.Y, 120, 14,
            color: new Color(0.85f, 0.85f, 0.9f));
        Control? ctrl = widget.GetControl();
        if (ctrl is not null) AddChild(ctrl);
        return widget;
    }

    // =========================================================================
    // Data model
    // =========================================================================

    /// <summary>
    /// A resolved live slot driven by CharacterListEvent (opcode 3/1).
    /// IsEmpty=true = slot has no character. View state only.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1. CODE-CONFIRMED.
    /// </summary>
    private readonly record struct LiveSlot(
        bool IsEmpty,
        string Name       = "",
        ushort Level      = 0,
        ushort ServerClass = 0,
        uint CurrentHp   = 0,
        int SlotIndex    = 0);
}
