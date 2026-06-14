// Screens/CharacterSelectScreen.cs
//
// The legacy CHARACTER-SELECT screen (master scene state 4), rebuilt to spec fidelity.
//
// SPEC FIDELITY (this revision — CS3D rebuild):
//   MAJOR CHANGE: The character-select is now a REAL 3D SCENE, not 5 separate SubViewports.
//   One SubViewportContainer holds CharSelectScene3D (the full 3D backdrop: map000 terrain,
//   character row, camera KF1, environment). The 2D chrome (slot tabs, info, Create/Delete/Enter)
//   is a transparent Control layer composited ON TOP of the 3D viewport.
//   spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop, not a flat 2D screen".
//   CODE-CONFIRMED composition.
//
//   1. Action IDs corrected: Create=4, Delete=5, Enter=6  (was 413/531 — those were HOVER srcX).
//      spec: Docs/RE/specs/ui_system.md §8.2 + correction note, frontend_scenes.md §4 + §5.
//      Buttons constructed via the stage-1 StateButton with real NORMAL/HOVER/PRESSED atlas frames.
//      spec: Docs/RE/specs/ui_system.md §1.5 (3-state button: HOVER=PRESSED for these buttons).
//
//   2. Stat grid per recovered generator: base-Y 191, stride 24, 5 rows, col1 x=154, col2 x=178.
//      spec: Docs/RE/specs/ui_system.md §8.2 + §8.4. CODE-CONFIRMED.
//      Class labels from msg.xdb ids 14003..14007 via UiAssetLoader.Text().
//      spec: Docs/RE/specs/ui_system.md §10; frontend_scenes.md §4.1. CODE-CONFIRMED.
//
//   3. ONE shared 3D scene (CharSelectScene3D in a single SubViewport) replaces 5 SubViewports.
//      The scene holds: map000 backdrop cell d000x10000z9990, standing character row at spec world
//      positions, Camera3D at KF1, DirectionalLight3D + 4 OmniLight3D approximating 14:30 lighting.
//      spec: frontend_scenes.md §3.3/§3.5/§3.6/§3.7. CODE-CONFIRMED.
//      3D selection: slot hover is AABB screen-space hit-test (§3.3.3). Stub in this revision.
//
//   4. Creation sub-form per frontend_scenes.md §4:
//      Class choice UI 0..3 → internal {4,1,3,2} (NOT the identity). spec §4.1. CODE-CONFIRMED.
//      Face index selector 1..7. spec §4.2. CODE-CONFIRMED.
//      Visual + local view state only — no server send, no game logic.
//
//   5. Preserves ScreenHost scaling and both boot flows (login → select; direct world boot).
//
//   6. ApplyCharacterList() — driven from Application CharacterListEvent (opcode 3/1).
//      Replaces the hardcoded DemoRoster when a real (or synthetic-offline) event arrives.
//      Handles "@BLANK@" empty-slot sentinel. spec: frontend_scenes.md §3.1. CODE-CONFIRMED.
//      Max 5 slots (indices 0..4). spec: frontend_scenes.md §3.1 "at most 5". CODE-CONFIRMED.
//
//   7. EnterGameRequested signal now carries (string characterName, int slotIndex) so BootFlow
//      can call IApplicationUseCases.SelectCharacterAsync(slotIndex).
//
// PASSIVE: zero game logic. Reads atlas chrome, msg.xdb captions, and character VFS assets for
// previews; turns UI gestures into C# signals that the flow node consumes. No domain state, no
// packet parsing, no equip/cooldown/stat math.
//
// spec: Docs/RE/specs/ui_system.md §8.2 (select layout, 77 ctor sites), §8.3 (InventWindow modal),
//       §8.4 (generator patterns: base-Y 191, stride 24), §9.2 (atlas manifest), §10 (msg.xdb).
// spec: Docs/RE/specs/frontend_scenes.md §3–§7 (char-select flow, create, delete, enter).
// spec: Docs/RE/specs/frontend_scenes.md §3.7 (3D composition: map000, cell d000x10000z9990, stage).

using System.Collections.Immutable;
using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Screens.Layout;
using MartialHeroes.Client.Godot.Screens.Widgets;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// Character-select Control on the 1024×768 reference canvas, scaled by the parent
/// <see cref="ScreenHost"/>.
///
/// <para><b>Action IDs (CODE-CONFIRMED, corrected):</b> Create=4, Delete=5, Enter=6.
/// See <see cref="CharacterSelectLayout"/> for the full correction note.
/// spec: Docs/RE/specs/ui_system.md §8.2 action-id map.</para>
///
/// <para><b>3D Previews:</b> up to 5 <see cref="CharPreview3D"/> SubViewport slots, one per
/// demo roster entry. Phase-staggered idle animation.
/// spec: Docs/RE/specs/frontend_scenes.md §3.3.</para>
///
/// <para><b>Create sub-form:</b> class choice UI→internal map {0,1,2,3}→{4,1,3,2}, face 1..7.
/// spec: Docs/RE/specs/frontend_scenes.md §4.1/§4.2.</para>
/// </summary>
public sealed partial class CharacterSelectScreen : Control
{
    // =========================================================================
    // Outgoing intents — consumed by BootFlow (no game logic here).
    // =========================================================================

    /// <summary>
    /// Raised when the player enters the game with the selected slot.
    /// Carries the character name and the slot index (0..4) so BootFlow can call
    /// IApplicationUseCases.SelectCharacterAsync(slotIndex).
    /// spec: Docs/RE/specs/frontend_scenes.md §7 — "send 1/9 with slot index". CODE-CONFIRMED.
    /// </summary>
    [Signal]
    public delegate void EnterGameRequestedEventHandler(string characterName, int slotIndex);

    /// <summary>Raised when the player goes back to the login/select entry.</summary>
    [Signal]
    public delegate void BackRequestedEventHandler();

    // =========================================================================
    // Demo roster (offline stub)
    // =========================================================================
    // In a networked build these come from the SmsgCharacterList event payload (Application),
    // specifically the 880-byte SpawnDescriptor per-slot (opcode 3/1 SmsgCharacterList).
    // spec: frontend_scenes.md §3.1 — up to 5 slots, "faceA nonzero ⇒ occupied". CODE-CONFIRMED.
    // spec: packets/3-1_character_list.yaml — per-slot SpawnDescriptor fields.
    //
    // Five varied demo slots cover all four internal classes so the per-class 3D preview
    // resolution path is exercisable offline.
    //
    // UiClassIndex (0..3) → internal class {4,1,3,2}. spec: frontend_scenes.md §4.1. CODE-CONFIRMED.
    //   UI 0 → internal 4 (Warrior)  → skin_class 4 (g202140001.skn)
    //   UI 1 → internal 1 (Musa)     → skin_class 1 (g202110001.skn)
    //   UI 2 → internal 3 (Blader)   → skin_class 3 (g202130001.skn)
    //   UI 3 → internal 2 (Tao)      → skin_class 2 (g202220001.skn)
    //
    // SkinClassId == internal class id for player classes 1..4 (PLAUSIBLE; VFS-confirmed pattern).
    // TODO: pin to a formal class→skin_class spec entry once documented.
    // spec: CLAUDE.md §Recovered asset mappings — skin_class chain. PLAUSIBLE.
    // spec: Docs/RE/formats/config_tables.md §2.6 — class id references.

    private static readonly DemoSlot[] DemoRoster =
    [
        // Slot 0 — UI class 1 → internal class 1 (Musa) → skin_class 1 → g202110001.skn
        // spec: frontend_scenes.md §4.1 — UI index 1 → internal class 1. CODE-CONFIRMED.
        new DemoSlot(Name: "무사영웅", ClassName: "Musa", Level: 25, Hp: 650,
            UiClassIndex: 1, FaceIndex: 2, SkinClassId: 1),

        // Slot 1 — UI class 3 → internal class 2 (Tao) → skin_class 2 → g202220001.skn
        // spec: frontend_scenes.md §4.1 — UI index 3 → internal class 2. CODE-CONFIRMED.
        new DemoSlot(Name: "TaoMaster", ClassName: "Tao", Level: 18, Hp: 480,
            UiClassIndex: 3, FaceIndex: 5, SkinClassId: 2),

        // Slot 2 — UI class 2 → internal class 3 (Blader) → skin_class 3 → g202130001.skn
        // spec: frontend_scenes.md §4.1 — UI index 2 → internal class 3. CODE-CONFIRMED.
        new DemoSlot(Name: "격사전설", ClassName: "Blader", Level: 32, Hp: 520,
            UiClassIndex: 2, FaceIndex: 4, SkinClassId: 3),

        // Slot 3 — UI class 0 → internal class 4 (Warrior) → skin_class 4 → g202140001.skn
        // spec: frontend_scenes.md §4.1 — UI index 0 → internal class 4. CODE-CONFIRMED.
        new DemoSlot(Name: "IronWarrior", ClassName: "Warrior", Level: 40, Hp: 820,
            UiClassIndex: 0, FaceIndex: 1, SkinClassId: 4),
    ];

    private const int MaxSlots = 5; // spec: frontend_scenes.md §3.1 — "at most 5 slots". CODE-CONFIRMED.

    // Empty-slot sentinel value — the name field in the SpawnDescriptor carries this literal string
    // for slots that have no character assigned.
    // spec: Docs/RE/specs/frontend_scenes.md §3.1 — "@BLANK@" empty-slot sentinel. CODE-CONFIRMED.
    private const string BlankSentinel = "@BLANK@"; // spec: frontend_scenes.md §3.1. CODE-CONFIRMED.

    // =========================================================================
    // Live slot data — driven by CharacterListEvent (Application event bus, opcode 3/1).
    // Falls back to DemoRoster entries when no event has arrived yet.
    // spec: Docs/RE/specs/frontend_scenes.md §3.1 — slot count 0..4, @BLANK@ empty. CODE-CONFIRMED.
    // spec: Docs/RE/specs/login_flow.md §3.2 — per-slot 981-byte records. CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    /// Per-slot resolved view state — populated by <see cref="ApplyCharacterList"/>.
    /// Before the first event arrives this mirrors the DemoRoster entries.
    /// </summary>
    private readonly LiveSlot[] _liveSlots = new LiveSlot[MaxSlots];

    /// <summary>
    /// Returns true once ApplyCharacterList has been called (i.e. real Application data arrived).
    /// When false the DemoRoster drives the display.
    /// </summary>
    private bool _liveDataApplied;

    /// <summary>
    /// Called by <see cref="CharListEventDrainer"/> (in BootFlow) when a CharacterListEvent
    /// arrives on the Application event bus. Replaces the DemoRoster view state with spec-driven
    /// data from the 3/1 packet (or synthetic offline equivalent).
    ///
    /// <para>Threading: always called on the Godot main thread (from _Process in CharListEventDrainer).</para>
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1 — "@BLANK@" empty-slot sentinel. CODE-CONFIRMED.
    /// spec: Docs/RE/specs/login_flow.md §3.2 — per-slot decode. CODE-CONFIRMED.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1 — "at most 5 slots, indices 0..4". CODE-CONFIRMED.
    /// </summary>
    public void ApplyCharacterList(ImmutableArray<CharacterListSlot> slots)
    {
        // Reset all slots to empty first.
        for (int i = 0; i < MaxSlots; i++)
            _liveSlots[i] = new LiveSlot(IsEmpty: true);

        // Populate from event payload. Cap at MaxSlots.
        // spec: frontend_scenes.md §3.1 — loop bound is exactly 5. CODE-CONFIRMED.
        foreach (CharacterListSlot s in slots)
        {
            int idx = s.SlotIndex;
            if (idx < 0 || idx >= MaxSlots) continue;

            // "@BLANK@" marks an unoccupied slot — route to character-creation on Enter.
            // spec: frontend_scenes.md §3.1 — "@BLANK@" empty-slot sentinel. CODE-CONFIRMED.
            bool empty = s.Name == BlankSentinel || string.IsNullOrEmpty(s.Name);
            _liveSlots[idx] = new LiveSlot(
                IsEmpty: empty,
                Name: empty ? string.Empty : s.Name,
                Level: s.Level,
                ServerClass: s.ServerClass,
                CurrentHp: s.CurrentHp,
                SlotIndex: idx);
        }

        _liveDataApplied = true;
        _selectedSlot = 0;

        // Refresh the display on the main thread.
        RebuildSlotSelectorRow();
        RefreshInfo();

        // Push updated slot data into the 3D scene.
        PushSlotDescriptorsToScene();
        _scene3D?.SetSelectedSlot(_selectedSlot);

        GD.Print($"[CharacterSelectScreen] ApplyCharacterList: {slots.Length} slots received; " +
                 $"live data applied to {MaxSlots} slot array.");
    }

    // =========================================================================
    // View state (NO domain state)
    // =========================================================================

    private int _selectedSlot;
    private UiAssetLoader _assets = null!;
    private RealClientAssets? _realAssets;
    private bool _ownsAssets;

    // Slot info labels (refreshed on selection change).
    private Label _infoName = null!;
    private Label _infoLevel = null!;
    private Label _infoClass = null!;

    // Create sub-form local state (view only, no domain mutation).
    private Control _createForm = null!;
    private bool _createFormVisible;
    private int _createUiClassIndex; // 0..3
    private int _createFaceIndex = CharacterSelectLayout.FaceIndexMin; // 1..7
    private Label _createClassLabel = null!;
    private Label _createFaceLabel = null!;

    // The single unified 3D scene — replaces the old per-slot CharPreview3D array.
    // spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop". CODE-CONFIRMED.
    private CharSelectScene3D? _scene3D;
    private SubViewport? _scene3DViewport;

    // Active camera keyframe (0..5) — kept for potential runtime switching; KF1 is the live frame.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — "live keyframe = 1". CODE-CONFIRMED.
    private int _activeCameraKeyframe = 1; // KF1 is live per spec.

    // Camera pose buttons (one per keyframe).
    private readonly Button[] _poseButtons = new Button[6];

    // Slot row buttons (for the selection highlight).
    private readonly Button?[] _slotButtons = new Button?[MaxSlots];

    // Container node for the slot selector row — held so RebuildSlotSelectorRow can clear+rebuild it.
    private Control _slotRowContainer = null!;

    /// <summary>Optional shared asset loader injected by the flow node.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        // Also try to open the real VFS for 3D preview SubViewports.
        // The CharPreview3D nodes may also open their own handle; sharing avoids double-open.
        try
        {
            _realAssets = RealClientAssets.TryOpen();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Screens] CharacterSelectScreen: VFS open for previews failed: {ex.Message}");
        }

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Screens] CharacterSelectScreen build failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsAssets) _assets?.Dispose();
        _realAssets?.Dispose();
        _realAssets = null;
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        int widgetCount = 0;

        // --- LAYER 0: The 3D scene viewport (full canvas, bottom-most layer).
        // The legacy char-select is a real 3D world — NOT a 2D screen with a backdrop image.
        // spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop". CODE-CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §3.7 (composition: map000, cell, stage). CODE-CONFIRMED.
        // The SubViewportContainer fills the canvas; the 2D chrome layers are Control children on top.
        widgetCount += Build3DSceneViewport();

        // --- LAYER 1: Transparent 2D chrome overlaid on the 3D viewport.
        // NOTE: No solid dim backdrop (the 3D viewport IS the background).
        // The left info panel and buttons are semi-transparent panels so the 3D scene shows through.

        // --- Title bar panel @ (0,0) 577×58.
        // spec §8.2 "Top title bar panel". CODE-CONFIRMED. ---
        var titleBar = MakeChrome(CharacterSelectLayout.TitleBar, CharacterSelectLayout.AtlasMainWindow);
        AddChild(titleBar);
        var titleLabel = WidgetFactory.MakeLabel("CHARACTER SELECT",
            CharacterSelectLayout.FontTitleHeight, new Color(0.95f, 0.86f, 0.55f));
        titleLabel.Position = new Vector2(16, 16);
        titleBar.AddChild(titleLabel);
        widgetCount += 2;

        // --- Tab buttons: Server (act 1), Channel (act 2), Back (act 3).
        // spec §8.2 tab button table; loginwindow.dds atlas. CODE-CONFIRMED. ---
        var serverTab = MakeTabButton(
            CharacterSelectLayout.ServerTabBtn,
            CharacterSelectLayout.ServerTabHov,
            CharacterSelectLayout.AtlasLoginWindow,
            actionId: 1, caption: "Server"); // actionId=1, spec §8.2
        serverTab.ActionFired += OnTabAction;
        titleBar.AddChild(serverTab);
        widgetCount++;

        var channelTab = MakeTabButton(
            CharacterSelectLayout.ChannelTabBtn,
            CharacterSelectLayout.ChannelTabHov,
            CharacterSelectLayout.AtlasLoginWindow,
            actionId: 2, caption: "Channel"); // actionId=2, spec §8.2
        channelTab.ActionFired += OnTabAction;
        titleBar.AddChild(channelTab);
        widgetCount++;

        var backTab = MakeTabButton(
            CharacterSelectLayout.BackTabBtn,
            CharacterSelectLayout.BackTabHov,
            CharacterSelectLayout.AtlasLoginWindow,
            actionId: 3, caption: "Back"); // actionId=3, spec §8.2
        backTab.ActionFired += OnTabAction;
        titleBar.AddChild(backTab);
        widgetCount++;

        // --- Left character-info panel @ (8,64) 244×187.
        // spec §8.2 "Left character-info panel". CODE-CONFIRMED. ---
        var infoPanel = MakeChrome(CharacterSelectLayout.CharInfoPanel, CharacterSelectLayout.AtlasMainWindow);
        infoPanel.Position = new Vector2(8, 64);
        AddChild(infoPanel);
        widgetCount++;

        // Stat rows per spec §8.2 + §8.4 generator:
        // base-Y 191, stride 24, 5 rows; col1 x=154 (icon), col2 x=178 (icon), x=51 value labels.
        // spec: ui_system.md §8.2+§8.4. CODE-CONFIRMED.
        widgetCount += BuildStatGrid(infoPanel);

        // Char name / level / class info labels.
        _infoName = BuildInfoLabel(infoPanel, "Name", new Vector2(8, 8));
        _infoLevel = BuildInfoLabel(infoPanel, "Level", new Vector2(8, 22));
        _infoClass = BuildInfoLabel(infoPanel, "Class", new Vector2(8, 36));
        widgetCount += 3;

        // Create / Delete / Enter buttons using StateButton with CORRECT action IDs.
        // spec §8.2 — Create=4, Delete=5, Enter=6. CORRECTION from 413/531. CODE-CONFIRMED.
        var createBtn = MakeCharButton(
            CharacterSelectLayout.CreateButton,
            CharacterSelectLayout.CreateButtonHover,
            CharacterSelectLayout.AtlasLoginWindow,
            CharacterSelectLayout.CreateActionId, // 4, spec §8.2
            "Create");
        createBtn.ActionFired += OnCharAction;
        infoPanel.AddChild(createBtn);
        widgetCount++;

        var deleteBtn = MakeCharButton(
            CharacterSelectLayout.DeleteButton,
            CharacterSelectLayout.DeleteButtonHover,
            CharacterSelectLayout.AtlasLoginWindow,
            CharacterSelectLayout.DeleteActionId, // 5, spec §8.2
            "Delete");
        deleteBtn.ActionFired += OnCharAction;
        infoPanel.AddChild(deleteBtn);
        widgetCount++;

        // M1 fix: Enter button uses atlas art (59×20) with no text overlay.
        // The official client shows the 진입 / Enter baked art at src(236,1004); no text caption.
        // Passing caption="" lets the atlas glyph speak for itself.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5c — Enter N src(236,1004). CODE-CONFIRMED.
        var enterBtn = MakeCharButton(
            CharacterSelectLayout.EnterButton,
            CharacterSelectLayout.EnterButtonHover,
            CharacterSelectLayout.AtlasLoginWindow,
            CharacterSelectLayout.EnterActionId, // 6, spec §8.2
            ""); // no text — baked art label. spec §11.5c "Enter" (236,1004). CODE-CONFIRMED.
        enterBtn.ActionFired += OnCharAction;
        infoPanel.AddChild(enterBtn);
        widgetCount++;

        // The 3D viewport (Layer 0) was already inserted as the first child in BuildUi().
        // The 2D chrome (info panel, buttons, slot row) composites on top of the 3D scene.
        // No per-slot SubViewport boxes needed anymore — the unified CharSelectScene3D handles
        // the full map000 backdrop + character row + camera.
        // spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop". CODE-CONFIRMED.

        // --- Slot selector row (text buttons below the 3D region) ---
        widgetCount += BuildSlotSelectorRow();

        // Camera is at KF1 (the live keyframe per spec).
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — "live keyframe = 1". CODE-CONFIRMED.
        _activeCameraKeyframe = 1; // KF1 is the scene's active camera.

        // --- Corner close button @ (971,610) 23×23, blacksheet.dds src (941,910).
        // spec §8.2 "Corner close". CODE-CONFIRMED. ---
        WidgetRect closeR = CharacterSelectLayout.CornerClose;
        var closeBtn = WidgetFactory.MakeStateButton2(
            _assets,
            CharacterSelectLayout.AtlasBlacksheet,
            closeR.X, closeR.Y, closeR.W, closeR.H,
            closeR.SrcX, closeR.SrcY,
            actionId: 99, caption: "×");
        closeBtn.ActionFired += _ => EmitSignal(SignalName.BackRequested);
        AddChild(closeBtn);
        widgetCount++;

        // --- Create sub-form (initially hidden).
        // spec: frontend_scenes.md §4 (class/appearance picker). ---
        _createForm = BuildCreateForm();
        _createForm.Visible = false;
        AddChild(_createForm);
        widgetCount++;

        // --- Char-select ambient VFX — char_select-u.xeff (effect_id 380003000, 68 sub-effects).
        // spec: Docs/RE/formats/effects.md §A.15 — char_select-u.xeff; effect_id 380003000;
        //   sub_effect_count 68; SAMPLE-VERIFIED.
        // Parser caveat per spec: the 68-sub-effect file may fail at the scale-curve (Group D) read;
        // FrontEndEffectPlayer handles this gracefully with a fallback ring effect.
        // spec: Docs/RE/formats/effects.md §A.15 — "parser caveat: high-sub_effect_count files...
        //   currently fail the existing .xeff parser at the scale-curve (Group D) read".
        var charVfxPlayer = new FrontEndEffectPlayer
        {
            Name = "CharSelectEffect",
            XeffVfsPath = "data/effect/xeff/char_select-u.xeff",
            SharedRealAssets = _realAssets,
            MouseFilter = MouseFilterEnum.Ignore,
            // Z-index above other 2D children so particles render in front of the dark
            // SubViewport preview rects. Additive blend makes them glow without hiding chrome.
            ZIndex = 10,
        };
        charVfxPlayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(charVfxPlayer);
        // No MoveChild needed — ZIndex controls draw order regardless of child-list position.
        widgetCount++;

        RefreshInfo();

        GD.Print($"[Screens] CharacterSelectScreen built ({widgetCount} widgets; " +
                 $"roster={DemoRoster.Length}; vfs={(_assets.HasVfs ? "real-atlas" : "offline")}; " +
                 $"3D scene viewport queued for deferred init).");
    }

    // =========================================================================
    // Stat grid — spec §8.2 + §8.4 generator (base-Y 191, stride 24, 5 rows). CODE-CONFIRMED.
    // =========================================================================

    private int BuildStatGrid(Control parent)
    {
        int count = 0;
        for (int row = 0; row < CharacterSelectLayout.StatGridRows; row++)
        {
            int y = CharacterSelectLayout.StatGridBaseY + row * CharacterSelectLayout.StatGridStride;
            // spec §8.4 — "base-Y 191, stride 24". CODE-CONFIRMED.

            // Col 1 stat icon button @ (154, y) 24×16. NORMAL (500,770) PRESSED (548,770).
            // spec §8.2 "Per-slot stat-icon grid col 1". CODE-CONFIRMED.
            var col1 = WidgetFactory.MakeStateButton(
                _assets,
                CharacterSelectLayout.AtlasLoginWindow,
                CharacterSelectLayout.StatIconCol1X, y,
                CharacterSelectLayout.StatIconW, CharacterSelectLayout.StatIconH,
                CharacterSelectLayout.StatIconCol1NormalSrcX, CharacterSelectLayout.StatIconCol1NormalSrcY,
                CharacterSelectLayout.StatIconCol1NormalSrcX,
                CharacterSelectLayout.StatIconCol1NormalSrcY, // HOVER=NORMAL (2-state)
                CharacterSelectLayout.StatIconCol1PressedSrcX, CharacterSelectLayout.StatIconCol1PressedSrcY,
                actionId: 61 + row * 2); // actions 61..74 cover the stat-grid range, spec §8.2
            parent.AddChild(col1);
            count++;

            // Col 2 stat icon button @ (178, y) 24×16. NORMAL (524,770) PRESSED (572,770).
            // spec §8.2 "Per-slot stat-icon grid col 2". CODE-CONFIRMED.
            var col2 = WidgetFactory.MakeStateButton(
                _assets,
                CharacterSelectLayout.AtlasLoginWindow,
                CharacterSelectLayout.StatIconCol2X, y,
                CharacterSelectLayout.StatIconW, CharacterSelectLayout.StatIconH,
                CharacterSelectLayout.StatIconCol2NormalSrcX, CharacterSelectLayout.StatIconCol2NormalSrcY,
                CharacterSelectLayout.StatIconCol2NormalSrcX,
                CharacterSelectLayout.StatIconCol2NormalSrcY, // HOVER=NORMAL
                CharacterSelectLayout.StatIconCol2PressedSrcX, CharacterSelectLayout.StatIconCol2PressedSrcY,
                actionId: 62 + row * 2);
            parent.AddChild(col2);
            count++;

            // Stat value label @ (51, y+2) 35×12.
            // spec §8.2+§8.4 "Per-slot stat value labels: x=51 base-Y 193 stride 24 35×12". CODE-CONFIRMED.
            var valLabel = WidgetFactory.MakeLabel(
                $"–",
                CharacterSelectLayout.FontRowHeight,
                new Color(0.85f, 0.85f, 0.90f));
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
    // 3D scene — ONE unified SubViewport with CharSelectScene3D.
    // Replaces the old per-slot CharPreview3D approach.
    // spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop". CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    /// Builds the single SubViewportContainer that hosts CharSelectScene3D.
    /// The SubViewportContainer fills the full reference canvas (1024×768) as the bottom-most
    /// layer; 2D chrome Controls are added as siblings on top (higher child index = rendered later).
    ///
    /// <para>The 3D scene is initialised via a deferred call so that all parent nodes (ScreenHost,
    /// CanvasLayer) are settled before the SubViewport is populated.</para>
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7 CODE-CONFIRMED (map000, cell, stage origin).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5 CODE-CONFIRMED (camera KF1).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.6 CODE-CONFIRMED (5-light rig, fog zeroed).
    /// </summary>
    private int Build3DSceneViewport()
    {
        // The SubViewport size matches the reference canvas (1024×768).
        // spec: Docs/RE/specs/ui_system.md §8.1 — "reference canvas 1024×768". CODE-CONFIRMED.
        _scene3DViewport = new SubViewport
        {
            Name = "CharSelect3DViewport",
            Size = new Vector2I(1024, 768),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false, // opaque — this IS the background
        };

        // CharSelectScene3D holds all 3D content (terrain, characters, camera, lights).
        // spec: Docs/RE/specs/frontend_scenes.md §3 CODE-CONFIRMED.
        _scene3D = new CharSelectScene3D { Name = "CharSelectScene3D" };

        // Set slot descriptors from the current roster (before Initialise).
        PushSlotDescriptorsToScene();

        _scene3DViewport.AddChild(_scene3D);

        // SubViewportContainer: stretches the viewport to fill the Control area.
        var container = new SubViewportContainer
        {
            Name = "Scene3DContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore, // 2D chrome handles input
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_scene3DViewport);
        AddChild(container);

        // Defer Initialise to the next frame so the SubViewport is settled in the tree.
        // spec: CharSelectScene3D — "call Initialise after node is in tree". CONFIRMED.
        Callable.From(InitialiseScene3D).CallDeferred();

        GD.Print("[CharacterSelectScreen] 3D scene SubViewport queued (deferred Initialise). " +
                 "spec: frontend_scenes.md §3.7 CODE-CONFIRMED.");
        return 2; // container + viewport
    }

    /// <summary>
    /// Deferred: initialises the 3D scene once the SubViewport is settled.
    /// </summary>
    private void InitialiseScene3D()
    {
        if (_scene3D is null || !IsInstanceValid(_scene3D)) return;
        try
        {
            _scene3D.Initialise(_realAssets);
            // Set initial slot selection highlight.
            _scene3D.SetSelectedSlot(_selectedSlot);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterSelectScreen] InitialiseScene3D failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Pushes the current slot occupancy / skin data into <see cref="_scene3D"/>'s descriptor array.
    /// Called before Initialise and after ApplyCharacterList.
    /// </summary>
    private void PushSlotDescriptorsToScene()
    {
        if (_scene3D is null) return;
        var descs = new (bool IsOccupied, uint SkinClassId)[MaxSlots];
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_liveDataApplied)
            {
                LiveSlot ls = _liveSlots[i];
                descs[i] = (!ls.IsEmpty, !ls.IsEmpty ? (uint)ls.ServerClass : 0u);
            }
            else
            {
                bool occ = i < DemoRoster.Length;
                descs[i] = (occ, occ ? DemoRoster[i].SkinClassId : 0u);
            }
        }

        _scene3D.SlotDescriptors = descs;
    }

    // =========================================================================
    // Slot selector row (text buttons beneath previews)
    // =========================================================================

    private int BuildSlotSelectorRow()
    {
        // Create a container node so we can clear+rebuild the row when ApplyCharacterList fires.
        _slotRowContainer = new Control { Name = "SlotRowContainer" };
        AddChild(_slotRowContainer);

        PopulateSlotSelectorRow();
        return MaxSlots + 1; // +1 for the container node
    }

    /// <summary>
    /// Populates the slot selector row buttons from current slot data.
    /// Called both on initial build and when ApplyCharacterList updates the roster.
    /// </summary>
    private void PopulateSlotSelectorRow()
    {
        const float slotX0 = 260f;
        const float slotW = 148f;
        const float slotGap = 2f;
        const float rowY = 535f;
        const float rowH = 28f;

        // Clear old buttons.
        foreach (Node child in _slotRowContainer.GetChildren())
            child.QueueFree();
        for (int j = 0; j < MaxSlots; j++)
            _slotButtons[j] = null;

        for (int i = 0; i < MaxSlots; i++)
        {
            // Determine slot data: live (from CharacterListEvent) or demo fallback.
            string label;
            bool occupied;

            if (_liveDataApplied)
            {
                LiveSlot ls = _liveSlots[i];
                occupied = !ls.IsEmpty;
                // spec: frontend_scenes.md §3.2 — "slot info line shows name, level, and position". CODE-CONFIRMED.
                label = occupied
                    ? $"{ls.Name}\nLv {ls.Level}"
                    : "(empty — Create)";
            }
            else
            {
                occupied = i < DemoRoster.Length;
                if (!occupied)
                {
                    label = "(empty)";
                }
                else
                {
                    DemoSlot slot = DemoRoster[i];
                    // Format: "Name\nClass Lv N" — readable at the 148px slot width.
                    label = $"{slot.Name}\n{slot.ClassName} Lv {slot.Level}";
                }
            }

            var btn = new Button
            {
                Text = label,
                Position = new Vector2(slotX0 + i * (slotW + slotGap), rowY),
                Size = new Vector2(slotW, rowH * 2), // double height to fit two lines
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

    /// <summary>
    /// Rebuilds the slot selector row in-place (called from ApplyCharacterList on the main thread).
    /// </summary>
    private void RebuildSlotSelectorRow()
    {
        if (_slotRowContainer is null || !IsInstanceValid(_slotRowContainer)) return;
        PopulateSlotSelectorRow();
    }

    // =========================================================================
    // Camera keyframe pose buttons
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 / §3.5.3 CODE-CONFIRMED (6 keyframes).
    // The orbit easing / auto-advance law is runtime-pending (spec §3.5.4), so we expose a
    // simple 6-button selector that sets the active keyframe on all preview slots.
    // =========================================================================

    /// <summary>
    /// Builds a row of 6 camera-pose buttons placed just below the slot-selector row.
    /// Each button sets the <see cref="CharPreview3D.ActiveKeyframe"/> on all preview slots.
    /// </summary>
    private int BuildCameraPoseButtons()
    {
        // Row geometry: 6 buttons × 120px wide × 22px tall, centred below the preview row.
        // Placed below the slot selector row (~y=600); above the close button (~y=610).
        // spec: §3.5.3 "initial active keyframe = 0". CODE-CONFIRMED.
        const float btnW = 120f;
        const float btnH = 22f;
        const float gap = 4f;
        const float startX = 260f; // aligns with the preview row left edge
        const float rowY = 596f; // below slot selector row

        // Labels for the 6 camera poses — descriptive only (framing is runtime-pending).
        // Yaw/pitch values (approximate degrees) from spec §3.5.3.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED (values).
        string[] poseLabels =
        [
            "Pose 1 (−6° yaw)", // KF0: yaw −6°, pitch +2.4°
            "Pose 2 (−2.7°)", // KF1: yaw −2.7°, pitch +0.8°
            "Pose 3 (+0.6°)", // KF2: yaw +0.6°, pitch −36.6°
            "Pose 4 (−2°)", // KF3: yaw −2°, pitch −80°
            "Pose 5 (+7.8°)", // KF4: yaw +7.8°, pitch +74.3°
            "Pose 6 (−13.8°)", // KF5: yaw −13.8°, pitch +52.4°
        ];

        var container = new HBoxContainer
        {
            Name = "CameraPoseRow",
            Position = new Vector2(startX, rowY),
            Size = new Vector2((btnW + gap) * 6 - gap, btnH),
        };
        AddChild(container);

        for (int kf = 0; kf < 6; kf++)
        {
            int kfCapture = kf;
            var btn = new Button
            {
                Name = $"PoseBtn{kf}",
                Text = poseLabels[kf],
                CustomMinimumSize = new Vector2(btnW, btnH),
                ToggleMode = true,
                ButtonPressed = kf == 0, // KF0 active by default (spec §3.5.3)
            };
            btn.AddThemeFontSizeOverride("font_size", 10);
            btn.Pressed += () =>
            {
                SetCameraKeyframe(kfCapture);
                // Un-toggle all other buttons (radio-button semantics).
                for (int j = 0; j < 6; j++)
                    if (_poseButtons[j] is Button pb)
                        pb.ButtonPressed = j == kfCapture;
            };
            container.AddChild(btn);
            _poseButtons[kf] = btn;
        }

        GD.Print($"[Screens] CharacterSelectScreen: camera pose row built (6 keyframes). " +
                 $"spec: frontend_scenes.md §3.5.2/§3.5.3 CODE-CONFIRMED.");

        return 7; // 1 container + 6 buttons
    }

    /// <summary>
    /// Sets the active camera keyframe on all preview slots.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.3 CODE-CONFIRMED (6 keyframes).
    /// Runtime easing/auto-advance is runtime-pending (spec §3.5.4).
    /// </summary>
    private void SetCameraKeyframe(int keyframe)
    {
        _activeCameraKeyframe = ((keyframe % 6) + 6) % 6;
        GD.Print($"[Screens] CharacterSelectScreen: camera keyframe → {_activeCameraKeyframe}. " +
                 "(CharSelectScene3D uses a single camera; keyframe switching is runtime-pending. " +
                 "spec: frontend_scenes.md §3.5.4 — keyframe orbit auto-advance: MEDIUM.)");
        // The unified CharSelectScene3D uses one camera fixed at KF1.
        // Full keyframe orbit switching is runtime-pending (spec §3.5.4 open item 4).
    }

    // =========================================================================
    // Create sub-form — spec: frontend_scenes.md §4. CODE-CONFIRMED.
    // Class choice: UI index 0..3 → internal {4,1,3,2}. Face index 1..7.
    // =========================================================================

    private Control BuildCreateForm()
    {
        // The create form floats over the select screen, centred.
        // Rough reference: a 320×400 panel at x=350, y=150 (not a recovered coord — the form
        // position is not in the spec; we position it as a modal overlay).
        var form = new Panel();
        form.Position = new Vector2(340, 120);
        form.Size = new Vector2(330, 440);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.09f, 0.13f, 0.97f),
            BorderColor = new Color(0.50f, 0.43f, 0.25f),
        };
        style.SetBorderWidthAll(2);
        form.AddThemeStyleboxOverride("panel", style);

        // Title.
        var title = WidgetFactory.MakeLabel("Create Character",
            CharacterSelectLayout.FontTitleHeight, new Color(0.95f, 0.86f, 0.55f));
        title.Position = new Vector2(12, 10);
        form.AddChild(title);

        // Class selection — 4 buttons in a row.
        // spec: frontend_scenes.md §4.1 — UI index 0..3 → internal class {4,1,3,2}. CODE-CONFIRMED.
        var classRowLabel = WidgetFactory.MakeLabel("Class:", CharacterSelectLayout.FontRowHeight,
            new Color(0.75f, 0.75f, 0.75f));
        classRowLabel.Position = new Vector2(12, 40);
        form.AddChild(classRowLabel);

        for (int ci = 0; ci < 4; ci++)
        {
            // Fetch the class label from msg.xdb id 14003..14006.
            // spec: ui_system.md §10 + frontend_scenes.md §4.1 — ids 14003..14007. CODE-CONFIRMED.
            uint msgId = CharacterSelectLayout.ClassLabelMsgIds[ci]; // 14003..14006
            string fallback = CharacterSelectLayout.ClassLabelFallbacks[ci];
            string classCaption = _assets.Text(msgId, fallback);

            var classBtn = new Button
            {
                Name = $"ClassBtn{ci}",
                Text = classCaption,
                Position = new Vector2(12 + ci * 76, 58),
                Size = new Vector2(72, 26),
            };
            classBtn.AddThemeFontSizeOverride("font_size", 11);
            int uiIdx = ci;
            classBtn.Pressed += () => SetCreateClass(uiIdx);
            form.AddChild(classBtn);
        }

        // Current class display.
        _createClassLabel = WidgetFactory.MakeLabel(
            ClassCaption(_createUiClassIndex),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.90f, 0.85f, 0.55f));
        _createClassLabel.Position = new Vector2(12, 92);
        form.AddChild(_createClassLabel);

        // Face selector — ± buttons + face index label.
        // spec: frontend_scenes.md §4.2 — face index clamped 1..7 via +/- buttons. CODE-CONFIRMED.
        // Actions 21/22 for face increment ±. spec §8.2 action-id map.
        var faceLabel = WidgetFactory.MakeLabel("Face:", CharacterSelectLayout.FontRowHeight,
            new Color(0.75f, 0.75f, 0.75f));
        faceLabel.Position = new Vector2(12, 118);
        form.AddChild(faceLabel);

        var faceMinus = new Button
        {
            Name = "FaceMinus",
            Text = "−",
            Position = new Vector2(60, 114),
            Size = new Vector2(26, 20),
        };
        faceMinus.Pressed += () => ChangeFace(-1);
        form.AddChild(faceMinus);

        _createFaceLabel = WidgetFactory.MakeLabel(
            _createFaceIndex.ToString(),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.90f, 0.90f, 0.90f));
        _createFaceLabel.Position = new Vector2(92, 118);
        form.AddChild(_createFaceLabel);

        var facePlus = new Button
        {
            Name = "FacePlus",
            Text = "+",
            Position = new Vector2(110, 114),
            Size = new Vector2(26, 20),
        };
        facePlus.Pressed += () => ChangeFace(+1);
        form.AddChild(facePlus);

        // Separator.
        var sep = new ColorRect
        {
            Color = new Color(0.35f, 0.30f, 0.18f),
            Position = new Vector2(12, 142),
            Size = new Vector2(306, 1),
        };
        form.AddChild(sep);

        // Name entry label + placeholder (no LineEdit yet — VFS-absent offline stub).
        var nameLabel = WidgetFactory.MakeLabel("Name:", CharacterSelectLayout.FontRowHeight,
            new Color(0.75f, 0.75f, 0.75f));
        nameLabel.Position = new Vector2(12, 152);
        form.AddChild(nameLabel);

        // Name-entry textbox (GUTextbox equivalent).
        // spec: frontend_scenes.md §4.4 — min 2 chars; lowercase a–z, 0–9, Hangul. CODE-CONFIRMED.
        // The validation itself lives in the Application/Domain layer; we show the field here only.
        var nameEntry = new LineEdit
        {
            Name = "NameEntry",
            PlaceholderText = "enter name (a-z/0-9/Hangul)",
            Position = new Vector2(12, 170),
            Size = new Vector2(306, 22),
        };
        nameEntry.AddThemeFontSizeOverride("font_size", 12);
        form.AddChild(nameEntry);

        // Preview area hint (the per-create 3D preview sits in the main preview row for the
        // "empty slot" that triggered Create; this form doesn't duplicate the preview).
        var previewNote = WidgetFactory.MakeLabel(
            "(3D preview in the selected slot)",
            CharacterSelectLayout.FontRowHeight,
            new Color(0.55f, 0.55f, 0.60f),
            multiline: true);
        previewNote.Position = new Vector2(12, 200);
        previewNote.Size = new Vector2(306, 32);
        form.AddChild(previewNote);

        // Separator.
        var sep2 = new ColorRect
        {
            Color = new Color(0.35f, 0.30f, 0.18f),
            Position = new Vector2(12, 240),
            Size = new Vector2(306, 1),
        };
        form.AddChild(sep2);

        // Starter equipment summary (display only).
        // spec: frontend_scenes.md §4.3 — per-class starter equipment seeded on create. CODE-CONFIRMED.
        var gearLabel = WidgetFactory.MakeLabel(
            StarterGearSummary(_createUiClassIndex),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.65f, 0.70f, 0.55f),
            multiline: true);
        gearLabel.Name = "GearLabel";
        gearLabel.Position = new Vector2(12, 250);
        gearLabel.Size = new Vector2(306, 50);
        form.AddChild(gearLabel);

        // Stat preview rows (class template display — no server call).
        // spec: frontend_scenes.md §4.2 — "per-class stat preview filled from the class template". CODE-CONFIRMED.
        var statHeader = WidgetFactory.MakeLabel("Stats (class default):", CharacterSelectLayout.FontRowHeight,
            new Color(0.75f, 0.75f, 0.75f));
        statHeader.Position = new Vector2(12, 310);
        form.AddChild(statHeader);

        string[] statNames = ["HP", "MP", "STR", "INT", "DEX", "STA"];
        int[] classBaseStats = ClassBaseStats(_createUiClassIndex);
        for (int s = 0; s < 6; s++)
        {
            var sl = WidgetFactory.MakeLabel($"{statNames[s]}: {classBaseStats[s]}",
                CharacterSelectLayout.FontRowHeight, new Color(0.80f, 0.80f, 0.80f));
            sl.Position = new Vector2(12 + (s % 3) * 104, 326 + (s / 3) * 16);
            form.AddChild(sl);
        }

        // Confirm (Create action id 10) and Cancel (action id 13) buttons.
        // spec §8.2 action-id map — "Create-form Create=10, Cancel=13". CODE-CONFIRMED.
        var confirmBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            55, 394, 113, 40,
            302, 860, // NORMAL src (InventWindow.dds confirm row). spec §8.2. CODE-CONFIRMED.
            actionId: 10, caption: "Create");
        confirmBtn.ActionFired += id =>
        {
            GD.Print($"[Screens] CharacterSelectScreen: Create confirmed " +
                     $"(class UI={_createUiClassIndex} internal={CharacterSelectLayout.UiToInternalClass[_createUiClassIndex]} " +
                     $"face={_createFaceIndex}) — offline stub (no use-case available).");
            HideCreateForm();
        };
        form.AddChild(confirmBtn);

        var cancelBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            174, 394, 113, 40,
            302, 900, // NORMAL src (InventWindow.dds cancel row). spec §8.2. CODE-CONFIRMED.
            actionId: 13, caption: "Cancel");
        cancelBtn.ActionFired += _ => HideCreateForm();
        form.AddChild(cancelBtn);

        return form;
    }

    // =========================================================================
    // Intent handlers
    // =========================================================================

    private void OnCharAction(int actionId)
    {
        switch (actionId)
        {
            case CharacterSelectLayout.CreateActionId: // 4 — spec §8.2
                GD.Print("[Screens] CharacterSelectScreen: Create pressed (action 4) — opening create form.");
                ShowCreateForm();
                break;

            case CharacterSelectLayout.DeleteActionId: // 5 — spec §8.2
                GD.Print($"[Screens] CharacterSelectScreen: Delete pressed (action 5) — " +
                         $"offline stub (slot={_selectedSlot}, no use-case available).");
                break;

            case CharacterSelectLayout.EnterActionId: // 6 — spec §8.2
                OnEnterGamePressed();
                break;
        }
    }

    private void OnTabAction(int actionId)
    {
        switch (actionId)
        {
            case 3: // Back tab — spec §8.2 actionId=3
                GD.Print("[Screens] CharacterSelectScreen: Back tab (action 3).");
                EmitSignal(SignalName.BackRequested);
                break;
            default:
                GD.Print($"[Screens] CharacterSelectScreen: Tab action {actionId} — no-op (offline stub).");
                break;
        }
    }

    private void OnEnterGamePressed()
    {
        // Resolve the selected slot.
        bool isEmptySlot;
        string name;

        if (_liveDataApplied)
        {
            LiveSlot ls = _liveSlots[_selectedSlot];
            isEmptySlot = ls.IsEmpty;
            name = ls.Name;
        }
        else
        {
            isEmptySlot = _selectedSlot >= DemoRoster.Length;
            name = isEmptySlot ? string.Empty : DemoRoster[_selectedSlot].Name;
        }

        if (isEmptySlot)
        {
            // Empty slot (or @BLANK@ sentinel) → open create form.
            // spec: frontend_scenes.md §7 — "enter on empty slot = create". CODE-CONFIRMED.
            // spec: frontend_scenes.md §3.1 — "@BLANK@" empty-slot sentinel. CODE-CONFIRMED.
            GD.Print($"[Screens] CharacterSelectScreen: Enter on empty slot {_selectedSlot} → opening Create form.");
            ShowCreateForm();
            return;
        }

        // spec: frontend_scenes.md §7 — "Enter/select (action 6) → SFX 920100200; send 1/9 (40B);
        // cache 880B descriptor + 96B stats; write state 5 (In-game)". CODE-CONFIRMED.
        // The actual 1/9 send is routed through IApplicationUseCases.SelectCharacterAsync
        // in BootFlow.OnEnterGameRequested — we emit the signal carrying slot index.
        GD.Print(
            $"[Screens] CharacterSelectScreen: Enter Game (action 6) — character='{name}' slot={_selectedSlot}.");
        EmitSignal(SignalName.EnterGameRequested, name, _selectedSlot);
    }

    // =========================================================================
    // Create form helpers
    // =========================================================================

    private void ShowCreateForm()
    {
        _createFormVisible = true;
        _createForm.Visible = true;
    }

    private void HideCreateForm()
    {
        _createFormVisible = false;
        _createForm.Visible = false;
    }

    private void SetCreateClass(int uiIndex)
    {
        // spec: frontend_scenes.md §4.1 — "UI index 0..3 → internal {4,1,3,2}". CODE-CONFIRMED.
        _createUiClassIndex = Mathf.Clamp(uiIndex, 0, 3);
        int internalClass = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
        GD.Print($"[Screens] CharacterSelectScreen: Create class selected: " +
                 $"UI={_createUiClassIndex} → internal={internalClass}.");
        _createClassLabel.Text = ClassCaption(_createUiClassIndex);

        // Update the gear label and stat preview (display-only).
        Node? gearLbl = _createForm.FindChild("GearLabel", owned: false);
        if (gearLbl is Label gear)
            gear.Text = StarterGearSummary(_createUiClassIndex);
    }

    private void ChangeFace(int delta)
    {
        // spec: frontend_scenes.md §4.2 — "face index clamped 1..7". CODE-CONFIRMED.
        _createFaceIndex = Mathf.Clamp(
            _createFaceIndex + delta,
            CharacterSelectLayout.FaceIndexMin,
            CharacterSelectLayout.FaceIndexMax);
        GD.Print($"[Screens] CharacterSelectScreen: face index = {_createFaceIndex} " +
                 $"(range {CharacterSelectLayout.FaceIndexMin}..{CharacterSelectLayout.FaceIndexMax}).");
        _createFaceLabel.Text = _createFaceIndex.ToString();
    }

    private string ClassCaption(int uiIndex)
    {
        int internalClass = CharacterSelectLayout.UiToInternalClass[uiIndex];
        uint msgId = CharacterSelectLayout.ClassLabelMsgIds[uiIndex];
        // spec: ui_system.md §10 / frontend_scenes.md §4.1 — ids 14003..14007. CODE-CONFIRMED.
        string caption = _assets.Text(msgId, CharacterSelectLayout.ClassLabelFallbacks[uiIndex]);
        return $"{caption} (class {internalClass})";
    }

    // Spec §4.3 starter equipment per internal class id. CODE-CONFIRMED.
    // The 202xxx/203xxx/206xxx/209xxx ids are equipment/visual ids in the item catalogue.
    private static string StarterGearSummary(int uiIndex)
    {
        int internalClass = CharacterSelectLayout.UiToInternalClass[uiIndex];
        // spec: frontend_scenes.md §4.3 — per-class starter equipment ids. CODE-CONFIRMED.
        return internalClass switch
        {
            1 => "Gear: 202110003 / 203110002 / 206110002 / 209110001", // CODE-CONFIRMED
            2 => "Gear: 202220003 / 203220002 / 206220002 / 209220001", // CODE-CONFIRMED
            3 => "Gear: 202130003 / 203130002 / 206130002 / 209130001", // CODE-CONFIRMED
            4 => "Gear: 202140003 / 203140002 / 206140002 / 209140001", // CODE-CONFIRMED
            _ => "Gear: (unknown class)"
        };
    }

    // Approximate class base stats for display (no domain authority — illustrative only).
    // The actual stat computation lives in Client.Domain; we only mirror plausible values for UX.
    private static int[] ClassBaseStats(int uiIndex)
    {
        int internalClass = CharacterSelectLayout.UiToInternalClass[uiIndex];
        // HP  MP  STR INT DEX STA — purely illustrative display values, not spec-derived.
        return internalClass switch
        {
            1 => [350, 100, 15, 8, 12, 14],
            2 => [280, 150, 8, 15, 14, 10],
            3 => [300, 120, 14, 9, 15, 12],
            4 => [420, 80, 18, 6, 10, 18],
            _ => [100, 100, 10, 10, 10, 10],
        };
    }

    // =========================================================================
    // Info refresh + slot highlight
    // =========================================================================

    private void RefreshInfo()
    {
        // spec: frontend_scenes.md §3.2 — slot info line shows name, level, and last position.
        if (_liveDataApplied)
        {
            LiveSlot ls = _liveSlots[_selectedSlot];
            if (!ls.IsEmpty)
            {
                _infoName.Text = $"Name: {ls.Name}";
                _infoLevel.Text = $"Lv {ls.Level}";
                // ServerClass is the raw server class id. Display it as-is (no class-name lookup
                // here — that would require the domain catalogue; this is a passive view).
                _infoClass.Text = $"Class: {ls.ServerClass}";
            }
            else
            {
                _infoName.Text = "Name: (empty)";
                _infoLevel.Text = "Lv –";
                _infoClass.Text = "Class: –";
            }
        }
        else if (_selectedSlot < DemoRoster.Length)
        {
            DemoSlot slot = DemoRoster[_selectedSlot];
            _infoName.Text = $"Name: {slot.Name}";
            _infoLevel.Text = $"Lv {slot.Level}";
            _infoClass.Text = ClassCaption(slot.UiClassIndex);
        }
        else
        {
            _infoName.Text = "Name: (empty)";
            _infoLevel.Text = "Lv –";
            _infoClass.Text = "Class: –";
        }

        HighlightSlot(_selectedSlot);
    }

    private void HighlightSlot(int index)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (_slotButtons[i] is Button btn)
            {
                // Dim unselected buttons; brighten the selected one.
                btn.Modulate = i == index
                    ? new Color(1.0f, 0.90f, 0.55f)
                    : Colors.White;
            }
        }

        // Propagate selection to the 3D scene for actor highlight.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.4 — clip swap on selection. CODE-CONFIRMED (spec).
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

    /// <summary>Builds a tab button (3-state: NORMAL / HOVER) from atlas frames.</summary>
    private StateButton MakeTabButton(WidgetRect norm, WidgetRect hov, string atlas, int actionId, string caption)
    {
        // 3-state ctor: PRESSED = HOVER (matching spec §1.5 "3-state: NORMAL / HOVER / PRESSED").
        // spec: ui_system.md §8.2 tab button table; §1.5 constructor variants.
        return WidgetFactory.MakeStateButton(
            _assets, atlas,
            norm.X, norm.Y, norm.W, norm.H,
            norm.SrcX, norm.SrcY,
            hov.SrcX, hov.SrcY,
            hov.SrcX, hov.SrcY, // PRESSED = HOVER, spec §1.5
            actionId, caption);
    }

    /// <summary>Builds a Create/Delete/Enter button (3-state) from atlas frames.</summary>
    private StateButton MakeCharButton(WidgetRect norm, WidgetRect hov, string atlas, int actionId, string caption)
    {
        // HOVER src-X differs from NORMAL; PRESSED = HOVER (spec §1.5).
        // spec: ui_system.md §8.2 Create/Delete/Enter button table + §1.5.
        return WidgetFactory.MakeStateButton(
            _assets, atlas,
            norm.X, norm.Y, norm.W, norm.H,
            norm.SrcX, norm.SrcY,
            hov.SrcX, hov.SrcY,
            hov.SrcX, hov.SrcY, // PRESSED = HOVER, spec §1.5
            actionId, caption);
    }

    private Control MakeChrome(WidgetRect rect, string atlasPath)
    {
        AtlasTexture? face = null;
        if (rect.SrcX != 0 || rect.SrcY != 0)
            face = _assets.Slice(atlasPath, rect.SrcX, rect.SrcY, rect.W, rect.H);

        if (face is not null)
        {
            var tr = new TextureRect
            {
                Texture = face,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Position = new Vector2(rect.X, rect.Y),
                Size = new Vector2(rect.W, rect.H),
            };
            return tr;
        }

        // Offline / unrecovered chrome fallback: solid panel.
        var panel = new Panel
        {
            Position = new Vector2(rect.X, rect.Y),
            Size = new Vector2(rect.W, rect.H),
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.11f, 0.14f, 0.96f),
            BorderColor = new Color(0.45f, 0.38f, 0.25f),
        };
        style.SetBorderWidthAll(2);
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    // =========================================================================
    // Data model (offline demo roster)
    // =========================================================================

    /// <summary>
    /// An offline demo roster slot — view-only; never domain state.
    /// Mirrors the fields the SmsgCharacterList SpawnDescriptor carries per slot
    /// (spec: packets/3-1_character_list.yaml + frontend_scenes.md §3.2).
    /// <para>UiClassIndex: 0..3 mapped to internal class {4,1,3,2} via UiToInternalClass.
    /// spec: frontend_scenes.md §4.1. CODE-CONFIRMED.</para>
    /// <para>FaceIndex: 1..7 (spec §4.2). CODE-CONFIRMED.</para>
    /// <para>SkinClassId: IdB in the .skn header = skin_class driving the rig/skeleton/motion chain.
    /// PLAUSIBLE: equals the internal class id for player classes 1..4 (VFS-confirmed pattern).
    /// spec: CLAUDE.md §Recovered asset mappings.</para>
    /// </summary>
    private readonly record struct DemoSlot(
        string Name,
        string ClassName,
        int Level,
        int Hp,
        int UiClassIndex,
        int FaceIndex,
        uint SkinClassId = 1u);

    /// <summary>
    /// A resolved live slot driven by <see cref="CharacterListEvent"/> (opcode 3/1).
    /// Populated by <see cref="ApplyCharacterList"/>; replaces the DemoRoster for the view.
    ///
    /// <para>IsEmpty=true: the slot carries no character (either absent from the event or the
    /// "@BLANK@" sentinel was seen). Enter on an empty slot opens the Create sub-form.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.1 — "@BLANK@" sentinel. CODE-CONFIRMED.</para>
    ///
    /// <para>This is view state only — never domain state.</para>
    /// </summary>
    private readonly record struct LiveSlot(
        bool IsEmpty,
        string Name = "",
        ushort Level = 0,
        ushort ServerClass = 0,
        uint CurrentHp = 0,
        int SlotIndex = 0);
}