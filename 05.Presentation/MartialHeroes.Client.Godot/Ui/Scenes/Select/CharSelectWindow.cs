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
//   - Left info panel shows name/level/position (3 labels only).
//     The 96-byte stats block is NOT consumed on select; stat grid is a CREATE-form exclusive.
//     spec: Docs/RE/specs/frontend_scenes.md §3.2 / CYCLE-6b fact 1. CODE-CONFIRMED.
//   - Create/Delete/Enter actions 4/5/6 at dst-Y 112 (centred), visibility gated by slot occupancy.
//     spec: Docs/RE/specs/frontend_scenes.md §3.2/§3.4 / CYCLE-6b fact 3. CODE-CONFIRMED.
//   - NO corner-X close widget. The select-window builder constructs no close-button widget;
//     blacksheet.dds is NOT a corner-X frame close (REFUTED). Window close is a system-close
//     message handler branch → scene state 6, sub-state 8 (return to login).
//     spec: Docs/RE/specs/ui_system.md §8.2 ("Window close — NO corner-X widget; premise REFUTED").
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
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.Ui.Assets;
using MartialHeroes.Client.Godot.Ui.Widgets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

/// <summary>
///     Character-select 2D chrome window (state 4), built on the Ui/ substrate.
///     <para>
///         Strictly passive. Reads atlases via HudAtlasLibrary and captions via HudTextLibrary.
///         Turns UI gestures into signals (intents). ZERO game-rule authority.
///     </para>
///     <para>
///         The 3D scene layer (CharSelectScene3D/CharCreatePreview3D) is REUSED unchanged.
///         This class provides only the 2D chrome overlay.
///     </para>
///     spec: Docs/RE/specs/ui_system.md §8.2/§8.4 (layout); frontend_scenes.md §3–§7 (flow).
/// </summary>
public sealed partial class CharSelectWindow : Control
{
    /// <summary>Raised when the player presses Back tab (action 3).</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    /// <summary>
    ///     Raised when the player confirms the Create-character form.
    ///     spec: Docs/RE/specs/frontend_scenes.md §4/§8 — "send 1/6 (52B)". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void CreateCharacterRequestedEventHandler(string name, int internalClass, int faceIndex);

    /// <summary>
    ///     Raised when the player presses Delete on an occupied slot.
    ///     spec: Docs/RE/specs/frontend_scenes.md §5/§8.
    /// </summary>
    [Signal]
    public delegate void DeleteCharacterRequestedEventHandler(int slotIndex, string characterName);
    // =========================================================================
    // Outgoing signals — consumed by SelectScene (zero game logic here).
    // =========================================================================

    /// <summary>
    ///     Raised when the player enters the game with the selected slot.
    ///     spec: Docs/RE/specs/frontend_scenes.md §7 — "send 1/9 with slot index". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void EnterGameRequestedEventHandler(string characterName, int slotIndex);

    // =========================================================================
    // Constants (spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.)
    // =========================================================================

    private const int MaxSlots = 5; // spec: frontend_scenes.md §3.1. CODE-CONFIRMED.
    private const string BlankSentinel = "@BLANK@";

    // Reference canvas.
    // spec: Docs/RE/specs/ui_system.md §8.0. CODE-CONFIRMED.
    private const float RefW = 1024f;
    private const float RefH = 768f;

    // Atlas paths.
    // spec: Docs/RE/specs/ui_system.md §8.2 atlas correction (CODE-CONFIRMED).
    private const string AtlasLoginWindow = "data/ui/loginwindow.dds";
    private const string AtlasInventWindow = "data/ui/inventwindow.dds";
    private const string AtlasMainWindow = "data/ui/mainwindow.dds";

    // Top tabs — actionIds 1/2/3.
    // spec: Docs/RE/specs/frontend_scenes.md §11.5b. CODE-CONFIRMED.
    private const int ActionServer = 1; // Server tab.
    private const int ActionChannel = 2; // Channel tab.
    private const int ActionBack = 3; // Back tab.

    // Roster action row — actionIds 4/5/6, dst-Y 112.
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private const int ActionCreate = 4;
    private const int ActionDelete = 5;
    private const int ActionEnter = 6;

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
    private const int ActionCancel = 36;

    // Stat grid — 10 cells (2×5), stride 24, cell 24×16.
    // Used exclusively in the CREATE form point-buy grid (BuildCreateForm).
    // The SELECT view has NO stat grid — the 96-byte stats block is not consumed on select.
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4 (create form); CYCLE-6b fact 1 (select view).
    private const int StatGridRows = 5;
    private const int StatGridStride = 24; // spec §8.2+§8.4. CODE-CONFIRMED.
    private const int StatIconW = 24;
    private const int StatIconH = 16;

    // Col-1 NORMAL/HOVER src. spec §8.2. CODE-CONFIRMED.
    private const int StatCol1NormX = 500;
    private const int StatCol1NormY = 770;

    private const int StatCol1HovX = 548;

    // Col-2 NORMAL/HOVER src. spec §8.2. CODE-CONFIRMED.
    private const int StatCol2NormX = 524;
    private const int StatCol2NormY = 770;
    private const int StatCol2HovX = 572;

    // Roster button row — loginwindow.dds, dst-Y 112, w=59 h=20.
    // spec: Docs/RE/specs/ui_system.md §8.4. CODE-CONFIRMED.
    private const float RosterBtnY = 112f; // spec §8.2+§8.4 dst-Y 112. CODE-CONFIRMED.
    private const float RosterBtnW = 59f;
    private const float RosterBtnH = 20f;

    // Create-form confirm/cancel — on loginwindow.dds create-plate row V=1004.
    // dst-Y 325, w=59, h=20. Confirm dst-X 42, Cancel dst-X 112.
    // HOVER src-X 413 (Confirm) / 531 (Cancel).
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private const int ConfirmDstX = 42;
    private const int CancelDstX = 112;
    private const int ConfirmCancelY = 325; // spec §8.4 "dst-Y 325". CODE-CONFIRMED.
    private const int ConfirmCancelW = 59;
    private const int ConfirmCancelH = 20;
    private const int ConfirmNormSrcX = 354; // spec §8.4. CODE-CONFIRMED.
    private const int ConfirmHovSrcX = 413; // spec §8.4. CODE-CONFIRMED.
    private const int CancelNormSrcX = 472; // spec §8.4. CODE-CONFIRMED.
    private const int CancelHovSrcX = 531; // spec §8.4. CODE-CONFIRMED.
    private const int ConfirmCancelSrcY = 1004; // spec §8.4 "V=1004". CODE-CONFIRMED.

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
    private const int ClassBtnSrcY = 1005; // spec §8.2. CODE-CONFIRMED.
    private const int ClassBtnW = 45; // AppSelectorW. CODE-CONFIRMED.
    private const int ClassBtnH = 19; // AppSelectorH. CODE-CONFIRMED.

    // Char-count caption: msg 2209. spec: frontend_scenes.md §3.8.2. CODE-CONFIRMED.
    private const uint MsgCharCount = 2209u;

    // Class button NORMAL src-Y 1005; NORMAL src-X {590,635,680,725}; HOVER src-X {770,815,860,905}.
    // spec: Docs/RE/specs/ui_system.md §8.2+§8.4. CODE-CONFIRMED.
    private static readonly int[] ClassBtnNormSrcX = [590, 635, 680, 725];
    private static readonly int[] ClassBtnHovSrcX = [770, 815, 860, 905];

    // UI index → internal class id. spec: frontend_scenes.md §4.1. CODE-CONFIRMED.
    // UI {0,1,2,3} → internal {4,1,3,2} (Monk/Musa/Dosa/Salsu).
    private static readonly int[] UiToInternal = [4, 1, 3, 2]; // CODE-CONFIRMED.

    // Class name msg ids 14003-14006 (4 classes). spec: ui_system.md §8.2. CODE-CONFIRMED.
    private static readonly uint[] ClassMsgIds = [14003u, 14004u, 14005u, 14006u];

    // =========================================================================
    // View state (NO domain state)
    // =========================================================================

    private readonly LiveSlot[] _slots = new LiveSlot[MaxSlots];

    // D5: roster button Controls for occupancy-gated visibility.
    // spec: Docs/RE/specs/frontend_scenes.md §3.2/§3.4 / CYCLE-6b fact 3.
    private Control? _btnCreate;
    private Control? _btnDelete;
    private Control? _btnEnter;

    // Cached node references (UI widgets updated dynamically).
    // HudLabel wraps a Godot Label; use GetControl() to add to parent, and .Text for mutations.
    private HudLabel? _charCountCaptionWidget;
    private HudLabel? _createDescLine0Widget;
    private HudLabel? _createDescLine1Widget;
    private HudLabel? _createDescLine2Widget;
    private int _createFaceIndex = FaceMin;
    private HudLabel? _createFaceLabelWidget;
    private Control? _createForm;
    private bool _createFormVisible;
    private CharCreatePreview3D? _createPreview3D;
    private int _createUiClass; // 0..3

    private HudLabel? _infoLevelWidget;
    private HudLabel? _infoNameWidget;

    // D2: third info-row label = world position "%d , %d", NOT a class label.
    // spec: Docs/RE/specs/frontend_scenes.md §3.2 / CYCLE-6b fact 2.
    private HudLabel? _infoPositionWidget;
    private LineEdit? _nameEntry;

    private Control? _nameModal;
    private HudLabel? _nameModalTitleWidget;
    private HudLabel? _nameToastWidget;

    // npc.scr class descriptions (CP949, already decoded by NpcScrDescriptions).
    private NpcScrDescriptions _npcScrDesc = null!;

    // Shared assets for 3D previews.
    private RealClientAssets? _realAssets;
    private bool _rotatePressLeft;
    private bool _rotatePressRight;

    // 3D scene references (REUSED — not rebuilt here).
    private CharSelectScene3D? _scene3D;
    private SubViewportContainer? _scene3DContainer;
    private SubViewport? _scene3DViewport;
    private int _selectedSlot;
    private double _toastTimer;

    // =========================================================================
    // Injectable services (set by SelectScene before _Ready fires)
    // =========================================================================

    /// <summary>Atlas library (from ClientContext.HudAtlas). Null = offline.</summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>Text library (from ClientContext.HudText). Null = offline.</summary>
    public HudTextLibrary? Text { get; set; }

    // Convenience accessors — resolve the backing Label from HudLabel for HorizontalAlignment etc.
    private Label? _charCountCaption => (Label?)_charCountCaptionWidget?.GetControl();
    private Label? _infoName => (Label?)_infoNameWidget?.GetControl();
    private Label? _infoLevel => (Label?)_infoLevelWidget?.GetControl();
    private Label? _infoPosition => (Label?)_infoPositionWidget?.GetControl();
    private Label? _createFaceLabel => (Label?)_createFaceLabelWidget?.GetControl();
    private Label? _createDescLine0 => (Label?)_createDescLine0Widget?.GetControl();
    private Label? _createDescLine1 => (Label?)_createDescLine1Widget?.GetControl();
    private Label? _createDescLine2 => (Label?)_createDescLine2Widget?.GetControl();
    private Label? _nameModalTitle => (Label?)_nameModalTitleWidget?.GetControl();
    private Label? _nameToast => (Label?)_nameToastWidget?.GetControl();

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    ///     Called on the Godot main thread (via CharSelectEventDrainer._Process) when a
    ///     CharacterListEvent (opcode 3/1) arrives.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.1. CODE-CONFIRMED.
    /// </summary>
    public void ApplyCharacterList(ImmutableArray<CharacterListSlot> slots)
    {
        for (var i = 0; i < MaxSlots; i++)
            _slots[i] = new LiveSlot(true);

        foreach (var s in slots)
        {
            var idx = s.SlotIndex;
            if (idx < 0 || idx >= MaxSlots) continue;

            var empty = s.Name == BlankSentinel || string.IsNullOrEmpty(s.Name);
            _slots[idx] = new LiveSlot(
                empty,
                empty ? string.Empty : s.Name,
                s.Level,
                s.ServerClass,
                s.CurrentHp,
                idx,
                s.PosX,
                s.PosZ);
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
    public void DevShowCreateForm()
    {
        ShowCreateForm();
    }

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
            var dt = (float)delta;
            if (_rotatePressLeft) _createPreview3D.RotateLeft(dt);
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
    // Data model
    // =========================================================================

    /// <summary>
    ///     A resolved live slot driven by CharacterListEvent (opcode 3/1).
    ///     IsEmpty=true = slot has no character. View state only.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.1. CODE-CONFIRMED.
    ///     PosX/PosZ: the character's last in-world position floats, shown on info-row line 3
    ///     formatted as "%d , %d". Axis X/Z pairing is debugger-pending.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.2 / CYCLE-6b fact 2. CODE-CONFIRMED.
    /// </summary>
    private readonly record struct LiveSlot(
        bool IsEmpty,
        string Name = "",
        ushort Level = 0,
        ushort ServerClass = 0,
        uint CurrentHp = 0,
        int SlotIndex = 0,
        float PosX = 0f,
        float PosZ = 0f);
}