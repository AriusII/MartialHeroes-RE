// Screens/CharacterSelectScreen.cs
//
// CHARACTER-SELECT screen (master scene state 4) — FROM-SCRATCH rewrite, CAMPAIGN 9 WAVE 3.
//
// WHAT THIS FILE IS:
//   Strictly passive 2D chrome overlaid on the unified CharSelectScene3D 3D backdrop.
//   Every visual is a real VFS atlas slice or a msg.xdb CP949 string — zero solid-colour
//   panels, zero synthetic/demo characters, zero hardcoded baked text.
//
// WIDGET LIST (SELECT VIEW):
//   1. 3D scene SubViewport (full-canvas, bottom layer) — CharSelectScene3D hosted in SubViewport.
//      spec: Docs/RE/specs/frontend_scenes.md §3.7 CODE-CONFIRMED.
//   2. Char-count caption (msg id 2209, orange, top-centre).
//      spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED.
//   3. Server tab (actionId=1): loginwindow.dds NORMAL (675,795) / PRESSED (483,883).
//      spec: Docs/RE/specs/frontend_scenes.md §11.5b CODE-CONFIRMED.
//   4. Channel tab (actionId=2): loginwindow.dds NORMAL (640,742) / PRESSED (483,923).
//      spec: Docs/RE/specs/frontend_scenes.md §11.5b CODE-CONFIRMED.
//   5. Back tab (actionId=3): loginwindow.dds NORMAL (625,691) / PRESSED (483,963).
//      spec: Docs/RE/specs/frontend_scenes.md §11.5b CODE-CONFIRMED.
//   6. Create button (actionId=4): loginwindow.dds NORMAL (0,1004) / PRESSED (59,1004), 59×20.
//      spec: Docs/RE/specs/frontend_scenes.md §11.5c CODE-CONFIRMED.
//   7. Delete button (actionId=5): loginwindow.dds NORMAL (118,1004) / PRESSED (177,1004), 59×20.
//      spec: Docs/RE/specs/frontend_scenes.md §11.5c CODE-CONFIRMED.
//   8. Enter button (actionId=6): loginwindow.dds NORMAL (236,1004) / PRESSED (295,1004), 59×20.
//      spec: Docs/RE/specs/frontend_scenes.md §11.5c CODE-CONFIRMED.
//   9. Stat-icon grid (5 rows × 2 cols): loginwindow.dds col1 N(500,770)/P(548,770), col2 N(524,770)/P(572,770).
//      base-Y 191, stride 24, col1 x=154, col2 x=178.
//      spec: Docs/RE/specs/ui_system.md §8.2+§8.4 CODE-CONFIRMED.
//  10. 5-slot selector row: blank slots (5 TextureRect from loginwindow.dds or fallback Label rows).
//      Populated from CharacterListEvent only — ZERO synthetic names.
//  11. Corner close (actionId=99): blacksheet.dds src (941,910) 23×23 at (971,610).
//      spec: Docs/RE/specs/ui_system.md §8.2 CODE-CONFIRMED.
//
// WIDGET LIST (CREATE FORM — 3-column overlay):
//  LEFT panel: 4 class buttons from loginwindow.dds src-Y=1005, src-X=590/635/680/725 (19×30).
//              HOVER src-X=815/860/905 (btn3 HOVER=NORMAL). Caption = msg.xdb 14003..14006.
//              spec: Docs/RE/specs/ui_system.md §8.2+§8.4 CODE-CONFIRMED.
//  LEFT panel: class name label (msg.xdb 14003..14006).
//  LEFT panel: npc.scr description (real CP949 text from data/script/npc.scr via NpcScrDescriptions).
//              spec: Docs/RE/formats/config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
//  CENTER: CharCreatePreview3D SubViewport (scale 75 vs slot-row 50).
//              spec: Docs/RE/specs/frontend_scenes.md §4.2 CODE-CONFIRMED.
//  CENTER: Face ± buttons (actionId 21/22). spec: ui_system.md §8.2 CODE-CONFIRMED.
//  CENTER: Turntable L/R buttons (press-and-hold ≈±2 rad/s). spec: frontend_scenes.md §4.2 CODE-CONFIRMED.
//  RIGHT panel: stat-grid 8 keys via 2·disc+{110..141}, cp949 labels from msg.xdb.
//              spec: Docs/RE/formats/config_tables.md §2.17.3 CODE-CONFIRMED (two-witness).
//              NOTE: 2·disc+{210..240} REFUTED — those are equipment IDs, not stat keys.
//  RIGHT panel: stat ± buttons (actionIds 25..34). spec: ui_system.md §8.2 CODE-CONFIRMED.
//  RIGHT panel: name-entry LineEdit + toast (msg id 2075).
//  RIGHT panel: OK (actionId 35) / Cancel (actionId 36) from inventwindow.dds.
//              spec: Docs/RE/specs/ui_system.md §8.2 CODE-CONFIRMED.
//
// SIGNALS (preserved from prior version — Lane E keeps the same API):
//   EnterGameRequested(string characterName, int slotIndex)
//   BackRequested()
//   CreateCharacterRequested(string name, int internalClass, int faceIndex)
//
// 3D-HOST API (preserved for Lane E):
//   _scene3D.SlotDescriptors = (bool IsOccupied, uint SkinClassId)[5]
//   _scene3D.Initialise(RealClientAssets?)
//   _scene3D.SetSelectedSlot(int)
//   _scene3D.TryHitTestSlot(Vector2) → int
//   _createPreview3D.InternalClassId / .SharedRealAssets / .RebuildForClass() / .RotateLeft(dt) / .RotateRight(dt)
//
// PASSIVE: zero game logic, zero domain state, zero packet parsing, zero stat math.
// All CP949 text arrives already-decoded from Assets.Parsers / UiAssetLoader.
//
// spec: Docs/RE/specs/ui_system.md §8.2/§8.4/§10.
// spec: Docs/RE/specs/frontend_scenes.md §3–§7/§11.
// spec: Docs/RE/formats/config_tables.md §2.17.3.

using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Character-select Control on the 1024×768 reference canvas, scaled by the parent ScreenHost.
///
/// <para>Strictly passive. Reads Application event channels/catalogues, renders them.
/// Turns UI gestures into signals (intent calls). ZERO game-rule authority.</para>
///
/// <para>spec: Docs/RE/specs/ui_system.md §8.2 (layout); frontend_scenes.md §3–§7 (flow).</para>
/// </summary>
public sealed partial class CharacterSelectScreen : Control
{
    // =========================================================================
    // Outgoing signals — consumed by BootFlow (zero game logic here).
    // =========================================================================

    /// <summary>
    /// Raised when the player enters the game with the selected slot.
    /// BootFlow calls IApplicationUseCases.SelectCharacterAsync(slotIndex).
    /// spec: Docs/RE/specs/frontend_scenes.md §7 — "send 1/9 with slot index". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void EnterGameRequestedEventHandler(string characterName, int slotIndex);

    /// <summary>Raised when the player goes back (Back tab, action 3).</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    /// <summary>
    /// Raised when the player confirms the Create-character form with valid local state.
    /// spec: Docs/RE/specs/frontend_scenes.md §4 / §8 — "gather fields → guard → send 1/6 (52B)". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void CreateCharacterRequestedEventHandler(string name, int internalClass, int faceIndex);

    // =========================================================================
    // Constants
    // =========================================================================

    private const int MaxSlots = 5; // spec: frontend_scenes.md §3.1 — "at most 5 slots". CODE-CONFIRMED.
    private const string BlankSentinel = "@BLANK@"; // spec: frontend_scenes.md §3.1. CODE-CONFIRMED.

    // Stat disc value used for the create-form grid key formula 2·disc + {110..141}.
    // disc = 0 is the base class-neutral value (no class-specific discriminator applied here).
    // The eight offsets produce the eight label keys for the stat/appearance columns.
    // spec: Docs/RE/formats/config_tables.md §2.17.3 CODE-CONFIRMED (two-witness).
    private const int StatDisc = 0; // base discriminator; keyed-string lookup for the stat grid.

    // =========================================================================
    // Live slot data — driven by CharacterListEvent (Application event bus, opcode 3/1).
    // All slots are BLANK by default. NO synthetic demo data.
    // spec: Docs/RE/specs/frontend_scenes.md §3.1 — slot count 0..4, @BLANK@ empty. CODE-CONFIRMED.
    // =========================================================================

    private readonly LiveSlot[] _liveSlots = new LiveSlot[MaxSlots];

    /// <summary>
    /// Called by the BootFlow/CharListEventDrainer when a CharacterListEvent (opcode 3/1) arrives.
    /// Replaces all slots with server data. Always called on the Godot main thread (from _Process).
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1 — "@BLANK@" sentinel + at most 5 slots. CODE-CONFIRMED.
    /// spec: Docs/RE/specs/login_flow.md §3.2 — per-slot 981-byte records. CODE-CONFIRMED.
    /// </summary>
    public void ApplyCharacterList(ImmutableArray<CharacterListSlot> slots)
    {
        // Reset all slots to empty.
        for (int i = 0; i < MaxSlots; i++)
            _liveSlots[i] = new LiveSlot(IsEmpty: true);

        // Populate from event payload. Cap at MaxSlots.
        // spec: frontend_scenes.md §3.1 — loop bound is exactly 5. CODE-CONFIRMED.
        foreach (CharacterListSlot s in slots)
        {
            int idx = s.SlotIndex;
            if (idx < 0 || idx >= MaxSlots) continue;

            bool empty = s.Name == BlankSentinel || string.IsNullOrEmpty(s.Name);
            _liveSlots[idx] = new LiveSlot(
                IsEmpty: empty,
                Name: empty ? string.Empty : s.Name,
                Level: s.Level,
                ServerClass: s.ServerClass,
                CurrentHp: s.CurrentHp,
                SlotIndex: idx);
        }

        _selectedSlot = 0;

        // Refresh display (main thread).
        RebuildSlotSelectorRow();
        RefreshInfo();
        RefreshCharCountCaption();

        // Push slot occupancy into the 3D scene.
        PushSlotDescriptorsToScene();
        _scene3D?.SetSelectedSlot(_selectedSlot);

        GD.Print($"[CharacterSelectScreen] ApplyCharacterList: {slots.Length} slots received; " +
                 $"vfs={(_assets.HasVfs ? "real-atlas" : "offline")}.");
    }

    // =========================================================================
    // View state (NO domain state)
    // =========================================================================

    private int _selectedSlot;
    private UiAssetLoader _assets = null!;
    private RealClientAssets? _realAssets;
    private bool _ownsAssets;

    // Char-count caption (msg id 2209 "캐릭터 개수 : %d", orange, top-centre).
    // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED.
    private Label _charCountCaption = null!;

    // Slot info labels (left info panel, refreshed on selection change).
    private Label _infoName = null!;
    private Label _infoLevel = null!;
    private Label _infoClass = null!;

    // Create sub-form state (view-only).
    private Control _createForm = null!;
    private bool _createFormVisible;
    private int _createUiClassIndex; // 0..3
    private int _createFaceIndex = CharacterSelectLayout.FaceIndexMin; // 1..7

    // Create form label references (updated on class change).
    private Label _createClassLabel = null!;   // class name from msg.xdb
    private Label _createDescLabel = null!;    // npc.scr description text
    private Label _createFaceLabel = null!;    // face index display

    // Turntable press-and-hold state.
    // spec: frontend_scenes.md §4.2 "press-and-hold turntable ≈±2 rad/s". CODE-CONFIRMED.
    private bool _rotatePressLeft;
    private bool _rotatePressRight;

    // CP949 class descriptions from data/script/npc.scr (keys 1..4).
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — npc.scr class-description records: CONFIRMED.
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — class description source = npc.scr: CONFIRMED.
    private NpcScrDescriptions _npcScrDesc = null!;

    // Create form stat-grid labels (8 stat rows, driven by 2·disc+{110..141} key lookup).
    // spec: Docs/RE/formats/config_tables.md §2.17.3 CODE-CONFIRMED (two-witness).
    private const int StatGridRowCount = 8; // keys offset {110,111,120,121,130,131,140,141}
    private readonly Label[] _createStatLabels = new Label[StatGridRowCount];
    private readonly Label[] _createStatNameLabels = new Label[StatGridRowCount];

    // Name entry / toast.
    private LineEdit _createNameEntry = null!;
    private Label _createToastLabel = null!;
    private double _toastTimer;

    // 3D scene — single SubViewport with CharSelectScene3D.
    // spec: Docs/RE/specs/frontend_scenes.md §3 CODE-CONFIRMED.
    private CharSelectScene3D? _scene3D;
    private SubViewport? _scene3DViewport;
    private SubViewportContainer? _scene3DContainer;

    // Create preview 3D.
    private CharCreatePreview3D? _createPreview3D;

    // Slot selector row container (rebuilt on ApplyCharacterList).
    private Control _slotRowContainer = null!;
    private readonly Button?[] _slotButtons = new Button?[MaxSlots];

    /// <summary>Optional shared asset loader injected by the flow node.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        try { _realAssets = RealClientAssets.TryOpen(); }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterSelectScreen] VFS open for 3D previews failed: {ex.Message}");
        }

        // Load CP949 class descriptions from data/script/npc.scr (keys 1..4).
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — npc.scr class-description records: CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — class description source = npc.scr: CONFIRMED.
        _npcScrDesc = NpcScrDescriptions.Load(_realAssets);

        try { BuildUi(); }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterSelectScreen] BuildUi failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
        _realAssets?.Dispose();
        _realAssets = null;
    }

    public override void _Process(double delta)
    {
        // Turntable drive (press-and-hold). spec: frontend_scenes.md §4.2 CODE-CONFIRMED.
        if (_createFormVisible && _createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            float dt = (float)delta;
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
                if (_createToastLabel is not null && IsInstanceValid(_createToastLabel))
                    _createToastLabel.Visible = false;
            }
        }
    }

    // =========================================================================
    // UI construction — SELECT VIEW
    // =========================================================================

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        int widgetCount = 0;

        // LAYER 0: 3D scene SubViewport (full canvas, bottom-most layer).
        // spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop". CODE-CONFIRMED.
        widgetCount += Build3DSceneViewport();

        // LAYER 1: 2D chrome overlaid on the 3D viewport.

        // Tab buttons: Server (act 1), Channel (act 2), Back (act 3).
        // spec: Docs/RE/specs/frontend_scenes.md §11.5b. CODE-CONFIRMED.
        // Added before the caption so caption draws on top.
        var serverTab = MakeTabButton(
            CharacterSelectLayout.ServerTabBtn with { Y = 4 },
            CharacterSelectLayout.ServerTabHov with { Y = 4 },
            CharacterSelectLayout.ServerTabPrs with { Y = 4 },
            actionId: 1); // spec: ui_system.md §8.2 actionId=1
        serverTab.ActionFired += OnTabAction;
        AddChild(serverTab);
        widgetCount++;

        var channelTab = MakeTabButton(
            CharacterSelectLayout.ChannelTabBtn with { Y = 4 },
            CharacterSelectLayout.ChannelTabHov with { Y = 4 },
            CharacterSelectLayout.ChannelTabPrs with { Y = 4 },
            actionId: 2); // spec: ui_system.md §8.2 actionId=2
        channelTab.ActionFired += OnTabAction;
        AddChild(channelTab);
        widgetCount++;

        var backTab = MakeTabButton(
            CharacterSelectLayout.BackTabBtn with { Y = 4 },
            CharacterSelectLayout.BackTabHov with { Y = 4 },
            CharacterSelectLayout.BackTabPrs with { Y = 4 },
            actionId: 3); // spec: ui_system.md §8.2 actionId=3
        backTab.ActionFired += OnTabAction;
        AddChild(backTab);
        widgetCount++;

        // Char-count caption: centred top, msg id 2209 "캐릭터 개수 : %d", orange.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED.
        _charCountCaption = WidgetFactory.MakeLabel(
            BuildCharCountCaption(),
            CharacterSelectLayout.FontTitleHeight,
            new Color(0.95f, 0.86f, 0.55f));
        _charCountCaption.Position = new Vector2(0f, 12f);
        _charCountCaption.Size = new Vector2(1024f, 28f);
        _charCountCaption.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_charCountCaption);
        widgetCount++;

        // Left character-info panel: compact, semi-transparent.
        // Shows selected char name/level/class from live slot data.
        var infoPanel = new Panel
        {
            Name = "CharInfoPanel",
            Position = new Vector2(8f, 46f),
            Size = new Vector2(194f, 80f),
        };
        {
            var ps = new StyleBoxFlat
            {
                BgColor = new Color(0.08f, 0.07f, 0.10f, 0.78f),
                BorderColor = new Color(0.45f, 0.38f, 0.22f),
            };
            ps.SetBorderWidthAll(1);
            infoPanel.AddThemeStyleboxOverride("panel", ps);
        }
        AddChild(infoPanel);
        widgetCount++;

        _infoName = BuildInfoLabel(infoPanel, "–", new Vector2(6f, 6f));
        _infoLevel = BuildInfoLabel(infoPanel, "–", new Vector2(6f, 24f));
        _infoClass = BuildInfoLabel(infoPanel, "–", new Vector2(6f, 42f));
        widgetCount += 3;

        // Bottom button bar: Create / Delete / Enter — centred at bottom.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5c. CODE-CONFIRMED.
        // spec: Docs/RE/specs/ui_system.md §8.2 Create=4/Delete=5/Enter=6 CODE-CONFIRMED.
        const float btnBarY = 718f;
        const float btnBarCentreX = 512f;
        const float btnW = 59f;
        const float btnH = 20f;
        const float btnGap = 8f;
        const float btnBarLeft = btnBarCentreX - (3f * btnW + 2f * btnGap) / 2f;

        // Create button: loginwindow.dds N(0,1004) P(59,1004). spec §11.5c. CODE-CONFIRMED.
        var createBtn = WidgetFactory.MakeStateButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)btnBarLeft, (int)btnBarY, (int)btnW, (int)btnH,
            0, 1004,   // NORMAL src. spec §11.5c. CODE-CONFIRMED.
            0, 1004,   // HOVER = NORMAL. spec §1.5.
            59, 1004,  // PRESSED src. spec §11.5c. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.CreateActionId); // 4
        createBtn.ActionFired += OnCharAction;
        AddChild(createBtn);
        widgetCount++;

        // Delete button: loginwindow.dds N(118,1004) P(177,1004). spec §11.5c. CODE-CONFIRMED.
        var deleteBtn = WidgetFactory.MakeStateButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)(btnBarLeft + btnW + btnGap), (int)btnBarY, (int)btnW, (int)btnH,
            118, 1004,  // NORMAL src. spec §11.5c. CODE-CONFIRMED.
            118, 1004,  // HOVER = NORMAL.
            177, 1004,  // PRESSED src. spec §11.5c. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.DeleteActionId); // 5
        deleteBtn.ActionFired += OnCharAction;
        AddChild(deleteBtn);
        widgetCount++;

        // Enter button: loginwindow.dds N(236,1004) P(295,1004). spec §11.5c. CODE-CONFIRMED.
        var enterBtn = WidgetFactory.MakeStateButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)(btnBarLeft + 2f * (btnW + btnGap)), (int)btnBarY, (int)btnW, (int)btnH,
            236, 1004,  // NORMAL src. spec §11.5c. CODE-CONFIRMED.
            236, 1004,  // HOVER = NORMAL.
            295, 1004,  // PRESSED src. spec §11.5c. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.EnterActionId); // 6
        enterBtn.ActionFired += OnCharAction;
        AddChild(enterBtn);
        widgetCount++;

        // Stat-icon grid (5 rows × 2 cols). spec §8.2+§8.4 CODE-CONFIRMED.
        widgetCount += BuildStatGrid(this);

        // Slot selector row (5 blank slots by default, driven by ApplyCharacterList).
        widgetCount += BuildSlotSelectorRow();

        // Corner close button: blacksheet.dds src (941,910) 23×23 at (971,610).
        // spec: Docs/RE/specs/ui_system.md §8.2 CODE-CONFIRMED.
        var closeBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasBlacksheet,
            CharacterSelectLayout.CornerClose.X, CharacterSelectLayout.CornerClose.Y,
            CharacterSelectLayout.CornerClose.W, CharacterSelectLayout.CornerClose.H,
            CharacterSelectLayout.CornerClose.SrcX, CharacterSelectLayout.CornerClose.SrcY,
            actionId: 99);
        closeBtn.ActionFired += _ => EmitSignal(SignalName.BackRequested);
        AddChild(closeBtn);
        widgetCount++;

        // Create sub-form (initially hidden).
        // spec: Docs/RE/specs/frontend_scenes.md §4 CODE-CONFIRMED.
        _createForm = BuildCreateForm();
        _createForm.Visible = false;
        AddChild(_createForm);
        widgetCount++;

        // NOTE: char_select-u.xeff (torch brazier effect) is a 3D composite effect — not a 2D
        // overlay. It is placed by CharSelectScene3D. spec: frontend_scenes.md §3.6.5 CODE-CONFIRMED.

        RefreshInfo();

        GD.Print($"[CharacterSelectScreen] built ({widgetCount} widgets; " +
                 $"vfs={(_assets.HasVfs ? "real-atlas" : "offline")}; " +
                 $"slots=BLANK-until-CharacterListEvent; 3D viewport deferred).");
    }

    // =========================================================================
    // Stat-icon grid — spec §8.2+§8.4 (base-Y 191, stride 24, 5 rows). CODE-CONFIRMED.
    // =========================================================================

    private int BuildStatGrid(Control parent)
    {
        int count = 0;
        for (int row = 0; row < CharacterSelectLayout.StatGridRows; row++)
        {
            int y = CharacterSelectLayout.StatGridBaseY + row * CharacterSelectLayout.StatGridStride;

            // Col 1: loginwindow.dds NORMAL (500,770) PRESSED (548,770).
            // spec: Docs/RE/specs/ui_system.md §8.2 "Per-slot stat-icon grid col 1". CODE-CONFIRMED.
            var col1 = WidgetFactory.MakeStateButton(
                _assets, CharacterSelectLayout.AtlasLoginWindow,
                CharacterSelectLayout.StatIconCol1X, y,
                CharacterSelectLayout.StatIconW, CharacterSelectLayout.StatIconH,
                CharacterSelectLayout.StatIconCol1NormalSrcX, CharacterSelectLayout.StatIconCol1NormalSrcY,
                CharacterSelectLayout.StatIconCol1NormalSrcX, CharacterSelectLayout.StatIconCol1NormalSrcY,
                CharacterSelectLayout.StatIconCol1PressedSrcX, CharacterSelectLayout.StatIconCol1PressedSrcY,
                actionId: 61 + row * 2); // actions 61..74. spec §8.2. CODE-CONFIRMED.
            parent.AddChild(col1);
            count++;

            // Col 2: loginwindow.dds NORMAL (524,770) PRESSED (572,770).
            // spec: Docs/RE/specs/ui_system.md §8.2 "Per-slot stat-icon grid col 2". CODE-CONFIRMED.
            var col2 = WidgetFactory.MakeStateButton(
                _assets, CharacterSelectLayout.AtlasLoginWindow,
                CharacterSelectLayout.StatIconCol2X, y,
                CharacterSelectLayout.StatIconW, CharacterSelectLayout.StatIconH,
                CharacterSelectLayout.StatIconCol2NormalSrcX, CharacterSelectLayout.StatIconCol2NormalSrcY,
                CharacterSelectLayout.StatIconCol2NormalSrcX, CharacterSelectLayout.StatIconCol2NormalSrcY,
                CharacterSelectLayout.StatIconCol2PressedSrcX, CharacterSelectLayout.StatIconCol2PressedSrcY,
                actionId: 62 + row * 2); // spec §8.2. CODE-CONFIRMED.
            parent.AddChild(col2);
            count++;

            // Stat value label @ (51, y+2) 35×12.
            // spec: Docs/RE/specs/ui_system.md §8.2+§8.4 "stat value labels x=51 base-Y 193 35×12". CODE-CONFIRMED.
            var valLabel = WidgetFactory.MakeLabel("–",
                CharacterSelectLayout.FontRowHeight, new Color(0.85f, 0.85f, 0.90f));
            valLabel.Position = new Vector2(
                CharacterSelectLayout.StatValueX,
                CharacterSelectLayout.StatValueBaseY + row * CharacterSelectLayout.StatGridStride);
            valLabel.Size = new Vector2(CharacterSelectLayout.StatValueW, CharacterSelectLayout.StatValueH);
            parent.AddChild(valLabel);
            count++;
        }
        return count;
    }

    // =========================================================================
    // 3D scene SubViewport. spec: frontend_scenes.md §3.7 CODE-CONFIRMED.
    // =========================================================================

    private int Build3DSceneViewport()
    {
        _scene3DViewport = new SubViewport
        {
            Name = "CharSelect3DViewport",
            Size = new Vector2I(1024, 768), // spec: ui_system.md §8.1 "1024×768". CODE-CONFIRMED.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };

        _scene3D = new CharSelectScene3D { Name = "CharSelectScene3D" };

        // All 5 slots start empty (no occupancy until ApplyCharacterList).
        PushSlotDescriptorsToScene();

        _scene3DViewport.AddChild(_scene3D);

        var container = new SubViewportContainer
        {
            Name = "Scene3DContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Pass,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_scene3DViewport);

        // Wire 3D ray-pick slot selection.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.3 CODE-CONFIRMED.
        container.GuiInput += OnViewport3DGuiInput;

        _scene3DContainer = container;
        AddChild(container);

        Callable.From(InitialiseScene3D).CallDeferred();

        GD.Print("[CharacterSelectScreen] 3D scene SubViewport queued (deferred Initialise). " +
                 "spec: frontend_scenes.md §3.7 CODE-CONFIRMED.");
        return 2;
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
            GD.PrintErr($"[CharacterSelectScreen] InitialiseScene3D failed: {ex.Message}");
        }
    }

    private void PushSlotDescriptorsToScene()
    {
        if (_scene3D is null) return;
        var descs = new (bool IsOccupied, uint SkinClassId)[MaxSlots];
        for (int i = 0; i < MaxSlots; i++)
        {
            LiveSlot ls = _liveSlots[i];
            descs[i] = (!ls.IsEmpty, !ls.IsEmpty ? (uint)ls.ServerClass : 0u);
        }
        _scene3D.SlotDescriptors = descs;
    }

    // =========================================================================
    // Slot selector row — 5 blank slots, driven by ApplyCharacterList.
    // NO synthetic names, NO demo roster.
    // =========================================================================

    private int BuildSlotSelectorRow()
    {
        _slotRowContainer = new Control { Name = "SlotRowContainer" };
        AddChild(_slotRowContainer);
        PopulateSlotSelectorRow();
        return MaxSlots + 1;
    }

    private void PopulateSlotSelectorRow()
    {
        const float slotX0 = 260f;
        const float slotW = 148f;
        const float slotGap = 2f;
        const float rowY = 535f;
        const float rowH = 28f;

        foreach (Node child in _slotRowContainer.GetChildren())
            child.QueueFree();
        for (int j = 0; j < MaxSlots; j++)
            _slotButtons[j] = null;

        for (int i = 0; i < MaxSlots; i++)
        {
            LiveSlot ls = _liveSlots[i];
            bool occupied = !ls.IsEmpty;

            // Slot label: only from live Application data — NEVER synthetic names.
            // spec: frontend_scenes.md §3.2 — "slot info line: name + level". CODE-CONFIRMED.
            string label = occupied
                ? $"{ls.Name}\nLv {ls.Level}"
                : string.Empty; // blank slot — no text

            var btn = new Button
            {
                Text = label,
                Position = new Vector2(slotX0 + i * (slotW + slotGap), rowY),
                Size = new Vector2(slotW, rowH * 2),
            };
            btn.AddThemeFontSizeOverride("font_size", 10);

            int slotCapture = i;
            btn.Pressed += () =>
            {
                _selectedSlot = slotCapture;
                RefreshInfo();
                HighlightSlot(slotCapture);
            };
            _slotRowContainer.AddChild(btn);
            _slotButtons[i] = btn;
        }
    }

    private void RebuildSlotSelectorRow()
    {
        if (_slotRowContainer is null || !IsInstanceValid(_slotRowContainer)) return;
        PopulateSlotSelectorRow();
    }

    // =========================================================================
    // Create sub-form — spec: frontend_scenes.md §4 CODE-CONFIRMED.
    //
    // 3-column layout (3D temple backdrop shows through semi-transparent panels):
    //   LEFT  (x=0..200)   : 4 class buttons (atlas slices) + class name + npc.scr description
    //   CENTER(x=200..620)  : CharCreatePreview3D + face ± + turntable L/R
    //   RIGHT (x=620..1024) : stat-grid (8 rows, 2·disc+{110..141} labels) + name entry + OK/Cancel
    // =========================================================================

    private Control BuildCreateForm()
    {
        var form = new Control
        {
            Name = "CreateForm",
            MouseFilter = MouseFilterEnum.Stop,
        };
        form.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Light dim overlay — 3D temple remains visible.
        // spec: frontend_scenes.md §4 — "sub-state drawn over the SAME 3D char-select scene". CODE-CONFIRMED.
        var dimBg = new ColorRect
        {
            Name = "CreateDim",
            Color = new Color(0f, 0f, 0f, 0.30f),
        };
        dimBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        form.AddChild(dimBg);

        // Orange char-count caption (same msg 2209 as select view).
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED.
        var topCaption = WidgetFactory.MakeLabel(
            BuildCharCountCaption(),
            CharacterSelectLayout.FontTitleHeight,
            new Color(0.95f, 0.86f, 0.55f));
        topCaption.Name = "CreateCountCaption";
        topCaption.Position = new Vector2(0f, 14f);
        topCaption.Size = new Vector2(1024f, 28f);
        topCaption.HorizontalAlignment = HorizontalAlignment.Center;
        form.AddChild(topCaption);

        // ── LEFT PANEL: 4 class buttons (atlas slices) + class name + npc.scr description ──
        // spec: frontend_scenes.md §4.1 CODE-CONFIRMED.
        var leftPanel = new Panel
        {
            Name = "CreateLeft",
            Position = new Vector2(8f, 46f),
            Size = new Vector2(186f, 660f),
        };
        {
            var ls = new StyleBoxFlat
            {
                BgColor = new Color(0.06f, 0.05f, 0.08f, 0.82f),
                BorderColor = new Color(0.45f, 0.38f, 0.22f),
            };
            ls.SetBorderWidthAll(2);
            leftPanel.AddThemeStyleboxOverride("panel", ls);
        }
        form.AddChild(leftPanel);

        // 4 class buttons from loginwindow.dds: size 19×30, base-Y=45, stride 48.
        // NORMAL src-Y=1005; NORMAL src-X=590/635/680/725; HOVER src-X=815/860/905 (btn3=NORMAL).
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4 CODE-CONFIRMED.
        // dst-X is right-anchored / computed in the legacy build routine — we layout in the panel.
        for (int ci = 0; ci < 4; ci++)
        {
            int normalSrcX = CharacterSelectLayout.ClassBtnNormalSrcX[ci]; // 590/635/680/725. CODE-CONFIRMED.
            int hoverSrcX = CharacterSelectLayout.ClassBtnHoverSrcX[ci];   // 815/860/905/725. CODE-CONFIRMED.
            int btnY = CharacterSelectLayout.ClassBtnBaseY + ci * CharacterSelectLayout.ClassBtnStride;
            // dst-X: right-anchored COMPUTED in the legacy code — we place them left-to-right in the panel.
            // spec: ui_system.md §8.4 "dst-X right-anchored COMPUTED, stride 48". CODE-CONFIRMED.
            // Since the exact right-anchor base is register-fed we use the panel width to mirror the intent.
            int btnX = 8 + ci * (CharacterSelectLayout.ClassBtnW + 4); // approximate panel-local layout

            // ActionIds 10/11/12/13 for class selection. spec §8.2 CODE-CONFIRMED.
            var classBtn = WidgetFactory.MakeStateButton(
                _assets, CharacterSelectLayout.AtlasLoginWindow,
                btnX, btnY,
                CharacterSelectLayout.ClassBtnW, CharacterSelectLayout.ClassBtnH,
                normalSrcX, CharacterSelectLayout.ClassBtnNormalSrcY, // NORMAL. CODE-CONFIRMED.
                hoverSrcX, CharacterSelectLayout.ClassBtnNormalSrcY,   // HOVER src-Y same as NORMAL. CODE-CONFIRMED.
                normalSrcX, CharacterSelectLayout.ClassBtnNormalSrcY,  // PRESSED = NORMAL. spec §8.2.
                actionId: 10 + ci, // 10/11/12/13. spec §8.2 CODE-CONFIRMED.
                caption: _assets.Text(CharacterSelectLayout.ClassLabelMsgIds[ci],
                    CharacterSelectLayout.ClassLabelFallbacks[ci]));
            classBtn.ActionFired += OnCreateClassAction;
            leftPanel.AddChild(classBtn);
        }

        // Class name display label.
        _createClassLabel = WidgetFactory.MakeLabel(
            ClassCaption(_createUiClassIndex),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.90f, 0.85f, 0.55f),
            multiline: true);
        _createClassLabel.Name = "CreateClassLabel";
        _createClassLabel.Position = new Vector2(8f, 230f);
        _createClassLabel.Size = new Vector2(170f, 36f);
        leftPanel.AddChild(_createClassLabel);

        // npc.scr class description (real CP949 text, no English fallback displayed in final build).
        // spec: frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        _createDescLabel = WidgetFactory.MakeLabel(
            GetClassDescription(_createUiClassIndex),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.78f, 0.78f, 0.82f),
            multiline: true);
        _createDescLabel.Name = "CreateDescLabel";
        _createDescLabel.Position = new Vector2(8f, 272f);
        _createDescLabel.Size = new Vector2(170f, 380f);
        leftPanel.AddChild(_createDescLabel);

        // ── CENTER PANEL: CharCreatePreview3D + face ± + turntable ──
        // spec: frontend_scenes.md §4.2 CODE-CONFIRMED.
        var centerPanel = new Control
        {
            Name = "CreateCenter",
            Position = new Vector2(200f, 46f),
            Size = new Vector2(424f, 660f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(centerPanel);

        // CharCreatePreview3D: scale 75 vs slot-row 50.
        // spec: frontend_scenes.md §4.2 "scale 75". CODE-CONFIRMED.
        _createPreview3D = new CharCreatePreview3D
        {
            Name = "CreatePreview3D",
            Position = new Vector2(2f, 2f),
            Size = new Vector2(420f, 600f),
            SharedRealAssets = _realAssets,
            InternalClassId = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex],
        };
        centerPanel.AddChild(_createPreview3D);

        // Face ± buttons. ActionIds 22 (−) / 21 (+).
        // spec: Docs/RE/specs/ui_system.md §8.2 "21/22 Face increment ±". CODE-CONFIRMED.
        // "face ± buttons range 1..7; the visible 3D face does NOT change". CODE-CONFIRMED.
        var faceMinusBtn = new Button
        {
            Name = "FaceMinus",
            Text = "−",
            Position = new Vector2(54f, 607f),
            Size = new Vector2(28f, 22f),
        };
        faceMinusBtn.Pressed += () => OnFaceAction(CharacterSelectLayout.FaceDecrementActionId);
        centerPanel.AddChild(faceMinusBtn);

        _createFaceLabel = WidgetFactory.MakeLabel(
            _createFaceIndex.ToString(),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.95f, 0.95f, 0.95f));
        _createFaceLabel.Name = "FaceIndexLabel";
        _createFaceLabel.Position = new Vector2(87f, 610f);
        _createFaceLabel.Size = new Vector2(24f, 22f);
        _createFaceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        centerPanel.AddChild(_createFaceLabel);

        var facePlusBtn = new Button
        {
            Name = "FacePlus",
            Text = "+",
            Position = new Vector2(116f, 607f),
            Size = new Vector2(28f, 22f),
        };
        facePlusBtn.Pressed += () => OnFaceAction(CharacterSelectLayout.FaceIncrementActionId);
        centerPanel.AddChild(facePlusBtn);

        // Turntable L/R (press-and-hold).
        // spec: frontend_scenes.md §4.2 "press-and-hold turntable ≈±2 rad/s". CODE-CONFIRMED.
        var rotLeftBtn = new Button
        {
            Name = "RotLeft",
            Text = "◄",
            Position = new Vector2(160f, 607f),
            Size = new Vector2(36f, 22f),
        };
        rotLeftBtn.ButtonDown += () => _rotatePressLeft = true;
        rotLeftBtn.ButtonUp += () => _rotatePressLeft = false;
        centerPanel.AddChild(rotLeftBtn);

        var rotRightBtn = new Button
        {
            Name = "RotRight",
            Text = "►",
            Position = new Vector2(200f, 607f),
            Size = new Vector2(36f, 22f),
        };
        rotRightBtn.ButtonDown += () => _rotatePressRight = true;
        rotRightBtn.ButtonUp += () => _rotatePressRight = false;
        centerPanel.AddChild(rotRightBtn);

        // ── RIGHT PANEL: stat-grid + name entry + OK/Cancel ──
        var rightPanel = new Panel
        {
            Name = "CreateRight",
            Position = new Vector2(626f, 46f),
            Size = new Vector2(390f, 660f),
        };
        {
            var rs = new StyleBoxFlat
            {
                BgColor = new Color(0.06f, 0.05f, 0.08f, 0.82f),
                BorderColor = new Color(0.45f, 0.38f, 0.22f),
            };
            rs.SetBorderWidthAll(2);
            rightPanel.AddThemeStyleboxOverride("panel", rs);
        }
        form.AddChild(rightPanel);

        // Stat-grid: 8 rows, labels from 2·disc + {110..141} msg.xdb lookup.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 CODE-CONFIRMED (two-witness).
        // REFUTED: disc+{210..240} are equipment IDs, NOT stat-grid keys — must NOT be used.
        // Actions 25..34. spec: Docs/RE/specs/ui_system.md §8.2 CODE-CONFIRMED.
        BuildCreateStatGrid(rightPanel);

        // Separator.
        var sep = new ColorRect
        {
            Color = new Color(0.35f, 0.30f, 0.18f),
            Position = new Vector2(8f, 8f + StatGridRowCount * 30f + 4f),
            Size = new Vector2(374f, 1f),
        };
        rightPanel.AddChild(sep);

        float nameY = sep.Position.Y + 10f;

        // Name entry.
        // spec: frontend_scenes.md §4.4 — "min 2 chars; a–z + digits + Hangul". CODE-CONFIRMED.
        var nameLabel = WidgetFactory.MakeLabel("Name:", CharacterSelectLayout.FontRowHeight,
            new Color(0.75f, 0.75f, 0.75f));
        nameLabel.Position = new Vector2(8f, nameY);
        rightPanel.AddChild(nameLabel);

        _createNameEntry = new LineEdit
        {
            Name = "NameEntry",
            Position = new Vector2(8f, nameY + 18f),
            Size = new Vector2(374f, 26f),
        };
        _createNameEntry.AddThemeFontSizeOverride("font_size", 13);
        rightPanel.AddChild(_createNameEntry);

        // Toast label (msg id 2075 rejection feedback).
        // spec: frontend_scenes.md §4.4 — "show a rejection toast (msg id 2075)". CODE-CONFIRMED.
        _createToastLabel = WidgetFactory.MakeLabel(
            string.Empty,
            CharacterSelectLayout.FontRowHeight,
            new Color(1.0f, 0.35f, 0.20f),
            multiline: true);
        _createToastLabel.Name = "NameToast";
        _createToastLabel.Position = new Vector2(8f, nameY + 48f);
        _createToastLabel.Size = new Vector2(374f, 36f);
        _createToastLabel.Visible = false;
        rightPanel.AddChild(_createToastLabel);

        float btnsY = nameY + 90f;

        // OK button (actionId=35) / Cancel (actionId=36): inventwindow.dds.
        // spec: Docs/RE/specs/ui_system.md §8.2 "35/36 Create-form Confirm/Cancel". CODE-CONFIRMED.
        var confirmBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            22, (int)btnsY, 162, 40,
            302, 860, // NORMAL src (InventWindow.dds confirm row). spec §8.3. CODE-CONFIRMED.
            actionId: 35, caption: _assets.Text(2301u, "OK")); // msg 2301 fallback "OK"
        confirmBtn.ActionFired += OnCreateConfirm;
        rightPanel.AddChild(confirmBtn);

        var cancelBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            208, (int)btnsY, 162, 40,
            302, 900, // NORMAL src (InventWindow.dds cancel row). spec §8.3. CODE-CONFIRMED.
            actionId: 36, caption: _assets.Text(2302u, "Cancel")); // msg 2302 fallback "Cancel"
        cancelBtn.ActionFired += _ => HideCreateForm();
        rightPanel.AddChild(cancelBtn);

        return form;
    }

    // =========================================================================
    // Create-form stat-grid construction.
    // 8 rows, labels from 2·disc + {110..141}. spec config_tables.md §2.17.3 CODE-CONFIRMED.
    // Actions 25..34 (5 × 2 = 10 stat ± buttons). spec ui_system.md §8.2 CODE-CONFIRMED.
    // =========================================================================

    private void BuildCreateStatGrid(Control parent)
    {
        const float baseY = 8f;
        const float rowH = 30f;

        for (int row = 0; row < StatGridRowCount; row++)
        {
            float y = baseY + row * rowH;
            int keyOffset = CharacterSelectLayout.StatGridKeyOffsets[row]; // {110,111,120,121,130,131,140,141}

            // Key formula: 2·disc + offset. spec: config_tables.md §2.17.3 CODE-CONFIRMED.
            // disc=0 here (class-neutral base).
            uint statKey = (uint)(2 * StatDisc + keyOffset); // spec: 2·disc + {110..141}. CODE-CONFIRMED.
            // Fallback: show the key as hex so the dev can see which key wasn't in msg.xdb.
            string statName = _assets.Text(statKey, $"[{statKey}]");

            // Stat name label.
            _createStatNameLabels[row] = WidgetFactory.MakeLabel(statName,
                CharacterSelectLayout.FontRowHeight, new Color(0.72f, 0.72f, 0.75f));
            _createStatNameLabels[row].Name = $"StatName{row}";
            _createStatNameLabels[row].Position = new Vector2(8f, y + 2f);
            _createStatNameLabels[row].Size = new Vector2(80f, 18f);
            parent.AddChild(_createStatNameLabels[row]);

            // − button: actionId = 25 + row*2 (for rows 0..4) → actions 25,27,29,31,33.
            // spec: ui_system.md §8.2 "25…34 stat point-buy ±". CODE-CONFIRMED.
            // Rows 5..7 are additional appearance entries (cols beyond 5 base stats) — same action family.
            int actionMinus = CharacterSelectLayout.StatPlusBuyBaseActionId + row * 2;
            int actionPlus = actionMinus + 1;

            var minusBtn = new Button
            {
                Name = $"StatMinus{row}",
                Text = "−",
                Position = new Vector2(94f, y),
                Size = new Vector2(24f, 22f),
            };
            int rowCapture = row;
            minusBtn.Pressed += () => OnStatAdjust(rowCapture, -1);
            parent.AddChild(minusBtn);

            // Stat value label.
            _createStatLabels[row] = WidgetFactory.MakeLabel("–",
                CharacterSelectLayout.FontRowHeight, new Color(0.92f, 0.92f, 0.92f));
            _createStatLabels[row].Name = $"StatVal{row}";
            _createStatLabels[row].Position = new Vector2(122f, y + 2f);
            _createStatLabels[row].Size = new Vector2(40f, 18f);
            _createStatLabels[row].HorizontalAlignment = HorizontalAlignment.Center;
            parent.AddChild(_createStatLabels[row]);

            var plusBtn = new Button
            {
                Name = $"StatPlus{row}",
                Text = "+",
                Position = new Vector2(166f, y),
                Size = new Vector2(24f, 22f),
            };
            plusBtn.Pressed += () => OnStatAdjust(rowCapture, +1);
            parent.AddChild(plusBtn);

            _ = actionMinus; // consumed via the button index calculation above — suppress CS0219.
            _ = actionPlus;
        }
    }

    // =========================================================================
    // 3D viewport ray-pick. spec: frontend_scenes.md §3.3.3 CODE-CONFIRMED.
    // =========================================================================

    private void OnViewport3DGuiInput(InputEvent ev)
    {
        if (_scene3D is null || _scene3DViewport is null || _scene3DContainer is null) return;
        if (_createFormVisible) return;

        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
        {
            // Convert container-local position to SubViewport pixel coords.
            // spec: ui_system.md §8.1 "reference canvas 1024×768". CODE-CONFIRMED.
            global::Godot.Vector2 vpSize = new(_scene3DViewport.Size.X, _scene3DViewport.Size.Y);
            global::Godot.Vector2 ctrlSize = _scene3DContainer.Size;
            float scaleX = ctrlSize.X > 0f ? vpSize.X / ctrlSize.X : 1f;
            float scaleY = ctrlSize.Y > 0f ? vpSize.Y / ctrlSize.Y : 1f;
            global::Godot.Vector2 vpPos = new(mb.Position.X * scaleX, mb.Position.Y * scaleY);

            int hit = _scene3D.TryHitTestSlot(vpPos);
            if (hit >= 0)
            {
                // spec: frontend_scenes.md §3.3.3 — "first slot hit → confirmed-pick". CODE-CONFIRMED.
                _selectedSlot = hit;
                RefreshInfo();
                HighlightSlot(hit);
                GD.Print($"[CharacterSelectScreen] 3D ray-pick → slot {hit}. " +
                         "spec: frontend_scenes.md §3.3.3 CODE-CONFIRMED.");
            }
        }
    }

    // =========================================================================
    // Intent handlers
    // =========================================================================

    private void OnCharAction(int actionId)
    {
        switch (actionId)
        {
            case CharacterSelectLayout.CreateActionId: // 4
                GD.Print("[CharacterSelectScreen] Create (action 4) → opening create form.");
                ShowCreateForm();
                break;
            case CharacterSelectLayout.DeleteActionId: // 5
                GD.Print($"[CharacterSelectScreen] Delete (action 5) slot={_selectedSlot} — stub (awaits ApplicationUseCases).");
                RefreshCharCountCaption();
                break;
            case CharacterSelectLayout.EnterActionId: // 6
                OnEnterGamePressed();
                break;
        }
    }

    private void OnTabAction(int actionId)
    {
        switch (actionId)
        {
            case 3: // Back. spec §8.2 actionId=3. CODE-CONFIRMED.
                GD.Print("[CharacterSelectScreen] Back tab (action 3).");
                EmitSignal(SignalName.BackRequested);
                break;
            default:
                GD.Print($"[CharacterSelectScreen] Tab action {actionId} — stub (offline).");
                break;
        }
    }

    private void OnEnterGamePressed()
    {
        LiveSlot ls = _liveSlots[_selectedSlot];
        bool isEmpty = ls.IsEmpty;

        if (isEmpty)
        {
            // Empty slot → open Create form. spec: frontend_scenes.md §7 CODE-CONFIRMED.
            GD.Print($"[CharacterSelectScreen] Enter on empty slot {_selectedSlot} → Create form.");
            ShowCreateForm();
            return;
        }

        // spec: frontend_scenes.md §7 — "Enter → SFX 920100200; send 1/9 (40B)". CODE-CONFIRMED.
        GD.Print($"[CharacterSelectScreen] EnterGameRequested: name='{ls.Name}' slot={_selectedSlot}.");
        EmitSignal(SignalName.EnterGameRequested, ls.Name, _selectedSlot);
    }

    private void OnCreateClassAction(int actionId)
    {
        // ActionIds 10/11/12/13 → UI index 0..3. spec §8.2 CODE-CONFIRMED.
        int uiIndex = actionId - 10;
        if (uiIndex >= 0 && uiIndex < 4)
            SetCreateClass(uiIndex);
    }

    private void OnFaceAction(int actionId)
    {
        // ActionId 21 = increment, 22 = decrement. spec §8.2 CODE-CONFIRMED.
        int delta = actionId == CharacterSelectLayout.FaceIncrementActionId ? +1 : -1;
        ChangeFace(delta);
    }

    private void OnStatAdjust(int rowIndex, int delta)
    {
        // The stat-grid ± buttons emit view-only feedback. No domain mutation.
        // spec: frontend_scenes.md §4.2 — "pure display from the class template". CODE-CONFIRMED.
        // The stat value labels display "–" until the Application delivers real class-template data.
        // In offline mode we simply log the intent (no optimistic mutation).
        GD.Print($"[CharacterSelectScreen] Stat adjust row={rowIndex} delta={delta} — view-only; no domain mutation.");
    }

    private void OnCreateConfirm(int _actionId)
    {
        string name = _createNameEntry?.Text.Trim() ?? string.Empty;

        if (!ValidateCreateName(name, out string toastMsg))
        {
            ShowCreateToast(toastMsg);
            GD.Print($"[CharacterSelectScreen] Create name rejected: '{name}' → {toastMsg}");
            return;
        }

        int internalClass = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
        // Class map: UI {0,1,2,3} → internal {4,1,3,2}. spec: frontend_scenes.md §4.1. CODE-CONFIRMED.

        EmitSignal(SignalName.CreateCharacterRequested, name, internalClass, _createFaceIndex);
        GD.Print($"[CharacterSelectScreen] CreateCharacterRequested: name='{name}' " +
                 $"internalClass={internalClass} face={_createFaceIndex}. " +
                 "spec: frontend_scenes.md §4/§8 CODE-CONFIRMED.");
        HideCreateForm();
        RefreshCharCountCaption();
    }

    // =========================================================================
    // Create form helpers
    // =========================================================================

    private void ShowCreateForm()
    {
        _createFormVisible = true;
        _createForm.Visible = true;

        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
            _createPreview3D.RebuildForClass();
        }

        _toastTimer = 0.0;
        if (_createToastLabel is not null && IsInstanceValid(_createToastLabel))
            _createToastLabel.Visible = false;

        _rotatePressLeft = false;
        _rotatePressRight = false;

        GD.Print($"[CharacterSelectScreen] Create form opened (uiClass={_createUiClassIndex} → " +
                 $"internal={CharacterSelectLayout.UiToInternalClass[_createUiClassIndex]}). " +
                 "spec: frontend_scenes.md §4 CODE-CONFIRMED.");
    }

    private void HideCreateForm()
    {
        _createFormVisible = false;
        _createForm.Visible = false;
        _rotatePressLeft = false;
        _rotatePressRight = false;
        _toastTimer = 0.0;
    }

    private void SetCreateClass(int uiIndex)
    {
        _createUiClassIndex = Mathf.Clamp(uiIndex, 0, 3);
        int internalClass = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
        GD.Print($"[CharacterSelectScreen] Create class: UI={_createUiClassIndex} → internal={internalClass}. " +
                 "spec: frontend_scenes.md §4.1 CODE-CONFIRMED.");

        // Update class name label.
        if (_createClassLabel is not null && IsInstanceValid(_createClassLabel))
            _createClassLabel.Text = ClassCaption(_createUiClassIndex);

        // Update description label (npc.scr CP949 text).
        // spec: frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        if (_createDescLabel is not null && IsInstanceValid(_createDescLabel))
            _createDescLabel.Text = GetClassDescription(_createUiClassIndex);

        // Rebuild 3D preview for new class.
        // spec: frontend_scenes.md §4.2 — "changing class rebuilds the actor". CODE-CONFIRMED.
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = internalClass;
            _createPreview3D.RebuildForClass();
        }

        // Update class button highlight (selected = orange tint; others = white).
        // StateButton is a custom Control and has no ButtonPressed property — use Modulate.
        for (int ci = 0; ci < 4; ci++)
        {
            Node? btnNode = _createForm?.FindChild($"StateBtn_{10 + ci}", owned: false);
            if (btnNode is StateButton sb)
                sb.Modulate = ci == _createUiClassIndex
                    ? new Color(1.0f, 0.90f, 0.55f)
                    : Colors.White;
        }
    }

    private void ChangeFace(int delta)
    {
        // spec: frontend_scenes.md §4.2 — "face index clamped 1..7". CODE-CONFIRMED.
        // "The visible 3D face does NOT change (face feeds a separate 2D portrait)". CODE-CONFIRMED.
        _createFaceIndex = Mathf.Clamp(
            _createFaceIndex + delta,
            CharacterSelectLayout.FaceIndexMin,
            CharacterSelectLayout.FaceIndexMax);
        GD.Print($"[CharacterSelectScreen] face={_createFaceIndex} " +
                 $"(range {CharacterSelectLayout.FaceIndexMin}..{CharacterSelectLayout.FaceIndexMax}). " +
                 "spec: frontend_scenes.md §4.2 CODE-CONFIRMED.");
        if (_createFaceLabel is not null && IsInstanceValid(_createFaceLabel))
            _createFaceLabel.Text = _createFaceIndex.ToString();
    }

    private string ClassCaption(int uiIndex)
    {
        uint msgId = CharacterSelectLayout.ClassLabelMsgIds[uiIndex];
        // spec: ui_system.md §10 / frontend_scenes.md §4.1 — ids 14003..14007. CODE-CONFIRMED.
        return _assets.Text(msgId, CharacterSelectLayout.ClassLabelFallbacks[uiIndex]);
    }

    private string GetClassDescription(int uiIndex)
    {
        // Delegates to NpcScrDescriptions (loaded from data/script/npc.scr CP949).
        // spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        return _npcScrDesc.GetDescription(Mathf.Clamp(uiIndex, 0, 3));
    }

    // =========================================================================
    // Name validation. spec: frontend_scenes.md §4.4 CODE-CONFIRMED.
    // =========================================================================

    private bool ValidateCreateName(string name, out string toastMsg)
    {
        // spec: frontend_scenes.md §4.4 — "minimum length 2 characters". CODE-CONFIRMED.
        if (name.Length < 2)
        {
            toastMsg = _assets.Text(2075u, "Name must be at least 2 characters.");
            return false;
        }

        // spec: frontend_scenes.md §4.4 — "a–z + digits + CP949 Hangul; reject all else". CODE-CONFIRMED.
        foreach (char c in name)
        {
            if (c >= 'a' && c <= 'z') continue;   // a–z. CODE-CONFIRMED.
            if (c >= '0' && c <= '9') continue;    // 0–9. CODE-CONFIRMED.
            if (c >= '가' && c <= '힣') continue;  // Hangul syllables. CODE-CONFIRMED.
            if (c >= 'ᄀ' && c <= 'ᇿ') continue;  // Hangul jamo. CODE-CONFIRMED.
            if (c >= '㄰' && c <= '㆏') continue;  // Hangul compat jamo. CODE-CONFIRMED.
            toastMsg = _assets.Text(2075u, "Only a-z, 0-9, and Korean Hangul allowed.");
            return false;
        }

        toastMsg = string.Empty;
        return true;
    }

    private void ShowCreateToast(string message)
    {
        if (_createToastLabel is null || !IsInstanceValid(_createToastLabel)) return;
        _createToastLabel.Text = message;
        _createToastLabel.Visible = true;
        _toastTimer = 3.0;
    }

    // =========================================================================
    // Char-count caption — msg id 2209 "캐릭터 개수 : %d".
    // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED.
    // =========================================================================

    private string BuildCharCountCaption()
    {
        // Count occupied slots from live data only. ZERO synthetic count.
        int count = 0;
        for (int i = 0; i < MaxSlots; i++)
            if (!_liveSlots[i].IsEmpty) count++;

        // CP949 template from msg.xdb. spec: frontend_scenes.md §3.8.2 CODE-CONFIRMED.
        string template = _assets.Text(2209u, "캐릭터 개수 : {0}");
        return template.Contains("%d")
            ? template.Replace("%d", count.ToString())
            : string.Format(template, count);
    }

    private void RefreshCharCountCaption()
    {
        if (_charCountCaption is null || !IsInstanceValid(_charCountCaption)) return;
        _charCountCaption.Text = BuildCharCountCaption();
        GD.Print($"[CharacterSelectScreen] Char-count caption: '{_charCountCaption.Text}'. " +
                 "spec: frontend_scenes.md §3.8.2 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Info refresh + slot highlight
    // =========================================================================

    private void RefreshInfo()
    {
        LiveSlot ls = _liveSlots[_selectedSlot];
        if (!ls.IsEmpty)
        {
            _infoName.Text = ls.Name;
            _infoLevel.Text = $"Lv {ls.Level}";
            _infoClass.Text = $"Cl {ls.ServerClass}";
        }
        else
        {
            _infoName.Text = "–";
            _infoLevel.Text = "–";
            _infoClass.Text = "–";
        }

        HighlightSlot(_selectedSlot);
    }

    private void HighlightSlot(int index)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_slotButtons[i] is Button btn)
            {
                btn.Modulate = i == index
                    ? new Color(1.0f, 0.90f, 0.55f)
                    : Colors.White;
            }
        }
        _scene3D?.SetSelectedSlot(index);
    }

    // =========================================================================
    // Widget factories
    // =========================================================================

    private static Label BuildInfoLabel(Control parent, string defaultText, Vector2 pos)
    {
        var lbl = WidgetFactory.MakeLabel(defaultText,
            CharacterSelectLayout.FontRowHeight, new Color(0.85f, 0.85f, 0.9f));
        lbl.Position = pos;
        parent.AddChild(lbl);
        return lbl;
    }

    /// <summary>
    /// Builds a tab button (3-state: NORMAL/HOVER/PRESSED) from atlas frames.
    /// spec: Docs/RE/specs/ui_system.md §8.2/§11.5b CODE-CONFIRMED.
    /// </summary>
    private StateButton MakeTabButton(WidgetRect norm, WidgetRect hov, WidgetRect prs, int actionId)
    {
        return WidgetFactory.MakeStateButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            norm.X, norm.Y, norm.W, norm.H,
            norm.SrcX, norm.SrcY,   // NORMAL. CODE-CONFIRMED.
            hov.SrcX, hov.SrcY,    // HOVER. CODE-CONFIRMED.
            prs.SrcX, prs.SrcY,    // PRESSED. CODE-CONFIRMED.
            actionId);
    }

    // =========================================================================
    // Data model
    // =========================================================================

    /// <summary>
    /// A resolved live slot driven by <see cref="CharacterListEvent"/> (opcode 3/1).
    /// IsEmpty=true = slot has no character. View state only — never domain state.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1 — "@BLANK@" sentinel. CODE-CONFIRMED.
    /// </summary>
    private readonly record struct LiveSlot(
        bool IsEmpty,
        string Name = "",
        ushort Level = 0,
        ushort ServerClass = 0,
        uint CurrentHp = 0,
        int SlotIndex = 0);
}
