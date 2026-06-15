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

    /// <summary>
    /// Raised when the player confirms the Create-character form with valid local state.
    /// BootFlow or Application wires this signal to <c>IApplicationUseCases.CreateCharacterAsync</c>.
    ///
    /// <para>Parameters:
    /// <list type="bullet">
    /// <item><paramref name="name"/> — validated character name (min 2, a-z/0-9/Hangul).</item>
    /// <item><paramref name="internalClass"/> — internal class id ∈ {1,2,3,4} mapped from UI index
    ///   via <see cref="CharacterSelectLayout.UiToInternalClass"/> ({btn0→4, btn1→1, btn2→3, btn3→2}).</item>
    /// <item><paramref name="faceIndex"/> — face appearance index 1..7.</item>
    /// </list>
    /// Starter gear is SERVER-ASSIGNED; the client class template values are preview-mannequin only
    /// and must NOT be treated as authoritative by the receiver.
    /// spec: Docs/RE/specs/frontend_scenes.md §4 / §8 — "gather fields → guard → send 1/6 (52B)". CODE-CONFIRMED.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.3 — "starter equipment = server-side; gear ids are preview IDs only". CODE-CONFIRMED.
    /// </para>
    /// </summary>
    [Signal]
    public delegate void CreateCharacterRequestedEventHandler(string name, int internalClass, int faceIndex);

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
        // Refresh the "character count : N" caption — count changed when live data arrived.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED — caption rebuilt on char-list.
        RefreshCharCountCaption();

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

    // Title-bar char-count caption label — driven by msg id 2209 "캐릭터 개수 : %d".
    // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED — MessageDB id 2209, count from BillingState +0x80.
    // Offline: count = number of occupied slots in the current roster.
    private Label _charCountCaption = null!;

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

    // Create sub-form: 3D preview node (centered, scale 75, turntable).
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 CODE-CONFIRMED.
    private CharCreatePreview3D? _createPreview3D;

    // Create sub-form: turntable button state (press-and-hold).
    // spec: frontend_scenes.md §4.2 "press-and-hold turntable ≈±2 rad/s". CODE-CONFIRMED.
    private bool _rotatePressLeft;
    private bool _rotatePressRight;

    // CP949 class descriptions from data/script/npc.scr (keys 1..4).
    // Loaded once in _Ready after _realAssets is opened.
    // spec: Docs/RE/formats/config_tables.md §2.17.3 — npc.scr class-description records: CONFIRMED.
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — class description source = npc.scr: CONFIRMED.
    private NpcScrDescriptions _npcScrDesc = null!;

    // Create sub-form: stat allocation (6 stats, shared budget, floor 10).
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 — "6 stats, shared point budget, floor 10 each;
    //   +/- buttons; remaining-points display. Pure display from class template." CODE-CONFIRMED.
    private readonly int[] _createStatValues = new int[6]; // HP MP STR INT DEX STA
    private int _createStatBudgetRemaining;
    private readonly Label[] _createStatLabels = new Label[6];
    private Label _createBudgetLabel = null!;

    // Name textbox reference (for validation on confirm).
    private LineEdit _createNameEntry = null!;

    // Toast label for name-validation feedback.
    // spec: frontend_scenes.md §4.4 — "show a rejection toast (msg id 2075)". CODE-CONFIRMED.
    private Label _createToastLabel = null!;
    private double _toastTimer; // seconds remaining

    // Class base stats for display — these are NOT authoritative; the Application layer owns stats.
    // We mirror illustrative starting values so the ±buttons give visible feedback.
    // spec: frontend_scenes.md §4.2 — "pure display from the class template". CODE-CONFIRMED.
    private static readonly int[][] CreateClassBaseStats =
    [
        [420, 80, 18, 6, 10, 18], // UI 0 → internal 4 (Warrior)
        [350, 100, 15, 8, 12, 14], // UI 1 → internal 1 (Musa)
        [300, 120, 14, 9, 15, 12], // UI 2 → internal 3 (Blader)
        [280, 150, 8, 15, 14, 10], // UI 3 → internal 2 (Tao)
    ];

    // Starting point budget per class (illustrative; real value comes from Application).
    private static readonly int[] CreateClassBudgets = [25, 25, 25, 25];

    // The single unified 3D scene — replaces the old per-slot CharPreview3D array.
    // spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop". CODE-CONFIRMED.
    private CharSelectScene3D? _scene3D;
    private SubViewport? _scene3DViewport;
    private SubViewportContainer? _scene3DContainer; // held for ray-pick coordinate scaling

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

        // Load CP949 class descriptions from data/script/npc.scr (keys 1..4).
        // spec: Docs/RE/formats/config_tables.md §2.17.3 — npc.scr class-description records: CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — class description source = npc.scr: CONFIRMED.
        _npcScrDesc = NpcScrDescriptions.Load(_realAssets);

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

    public override void _Process(double delta)
    {
        // Turntable: drive CharCreatePreview3D rotation while buttons are held.
        // spec: Docs/RE/specs/frontend_scenes.md §4.2 — "press-and-hold turntable ≈±2 rad/s". CODE-CONFIRMED.
        if (_createFormVisible && _createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            float dt = (float)delta;
            if (_rotatePressLeft)
                _createPreview3D.RotateLeft(dt);
            if (_rotatePressRight)
                _createPreview3D.RotateRight(dt);
        }

        // Toast timer: hide after duration expires.
        // spec: frontend_scenes.md §4.4 — rejection toast. CODE-CONFIRMED.
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
        // The official client is MINIMAL chrome over the 3D scene: just the orange count caption
        // centred at the top, a compact info panel top-left, and the C/D/E buttons at the bottom.
        // Reference: Capture d'écran 2026-06-14 015759.png — the 3D temple dominates the view.

        // --- Left character-info panel: compact, top-left, semi-transparent dark background.
        // The official view shows a small dark panel with the selected char's name/level/class.
        // Size: 194×80 (name + level + class, compact to match minimal chrome in ref screenshot).
        // spec §8.2 "Left character-info panel". CODE-CONFIRMED for the panel; size trimmed to
        //   match the minimal chrome visible in the reference screenshot (015759).
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

        // Char name / level / class info labels in the compact panel.
        _infoName = BuildInfoLabel(infoPanel, "Name", new Vector2(6f, 6f));
        _infoLevel = BuildInfoLabel(infoPanel, "Level", new Vector2(6f, 24f));
        _infoClass = BuildInfoLabel(infoPanel, "Class", new Vector2(6f, 42f));
        widgetCount += 3;

        // --- Tab buttons: Server (act 1), Channel (act 2), Back (act 3).
        // spec §8.2 tab button table; spec §11.5b positions relative to titleBar at (0,0).
        // CODE-CONFIRMED. Placed at their spec X, absolute y=4 (top edge strip).
        // These are added BEFORE the caption so the caption draws on top (later child = drawn last).
        var serverTab = MakeTabButton(
            CharacterSelectLayout.ServerTabBtn with { Y = 4 },
            CharacterSelectLayout.ServerTabHov with { Y = 4 },
            CharacterSelectLayout.AtlasLoginWindow,
            actionId: 1, caption: "Server"); // actionId=1, spec §8.2
        serverTab.ActionFired += OnTabAction;
        AddChild(serverTab);
        widgetCount++;

        var channelTab = MakeTabButton(
            CharacterSelectLayout.ChannelTabBtn with { Y = 4 },
            CharacterSelectLayout.ChannelTabHov with { Y = 4 },
            CharacterSelectLayout.AtlasLoginWindow,
            actionId: 2, caption: "Channel"); // actionId=2, spec §8.2
        channelTab.ActionFired += OnTabAction;
        AddChild(channelTab);
        widgetCount++;

        var backTab = MakeTabButton(
            CharacterSelectLayout.BackTabBtn with { Y = 4 },
            CharacterSelectLayout.BackTabHov with { Y = 4 },
            CharacterSelectLayout.AtlasLoginWindow,
            actionId: 3, caption: "Back"); // actionId=3, spec §8.2
        backTab.ActionFired += OnTabAction;
        AddChild(backTab);
        widgetCount++;

        // --- Character-count caption: centred horizontally, near top of canvas.
        // msg id 2209 "캐릭터 개수 : %d", orange text, no background panel.
        // Added AFTER the tab buttons so it draws on top of them (Godot render order = child order).
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED — MessageDB id 2209.
        // Position y=12 to sit within the top strip above the info panel (y=46).
        _charCountCaption = WidgetFactory.MakeLabel(
            BuildCharCountCaption(),
            CharacterSelectLayout.FontTitleHeight, new Color(0.95f, 0.86f, 0.55f));
        _charCountCaption.Position = new Vector2(0f, 12f);
        _charCountCaption.Size = new Vector2(1024f, 28f);
        _charCountCaption.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_charCountCaption);
        widgetCount++;

        // --- Bottom button bar: Create / Delete / Enter — placed at the bottom of the canvas.
        // In the official client these three buttons sit as a centred strip near y~720.
        // spec §8.2 — Create=4, Delete=5, Enter=6. CORRECTION from 413/531. CODE-CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5c. CODE-CONFIRMED.
        // Button strip: centred ~x=350 for Create, x=420 for Delete, x=490 for Enter at y=718.
        // Each button is 59×20 per the atlas frame (spec §11.5c). CODE-CONFIRMED.
        const float btnBarY = 718f;
        const float btnBarCentreX = 512f; // canvas centre
        const float btnW = 59f;
        const float btnGap = 8f;
        // 3 buttons total width = 3*59 + 2*8 = 193px → leftmost starts at 512 − 96.5 ≈ 415
        const float btnBarLeft = btnBarCentreX - (3f * btnW + 2f * btnGap) / 2f;

        // Create button at btnBarLeft, Delete at +67, Enter at +134.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5c NORMAL src (0,1004)/(118,1004)/(236,1004). CODE-CONFIRMED.
        WidgetRect createR = CharacterSelectLayout.CreateButton with { X = (int)btnBarLeft, Y = (int)btnBarY };
        var createBtn = MakeCharButton(
            createR,
            CharacterSelectLayout.CreateButton with { X = (int)btnBarLeft, Y = (int)btnBarY },
            CharacterSelectLayout.AtlasLoginWindow,
            CharacterSelectLayout.CreateActionId, // 4, spec §8.2
            "Create");
        createBtn.ActionFired += OnCharAction;
        AddChild(createBtn);
        widgetCount++;

        WidgetRect deleteR = CharacterSelectLayout.DeleteButton with
        {
            X = (int)(btnBarLeft + btnW + btnGap), Y = (int)btnBarY
        };
        var deleteBtn = MakeCharButton(
            deleteR,
            CharacterSelectLayout.DeleteButton with { X = deleteR.X, Y = (int)btnBarY },
            CharacterSelectLayout.AtlasLoginWindow,
            CharacterSelectLayout.DeleteActionId, // 5, spec §8.2
            "Delete");
        deleteBtn.ActionFired += OnCharAction;
        AddChild(deleteBtn);
        widgetCount++;

        WidgetRect enterR = CharacterSelectLayout.EnterButton with
        {
            X = (int)(btnBarLeft + 2f * (btnW + btnGap)), Y = (int)btnBarY
        };
        // M1 fix: Enter button uses atlas art with no text overlay.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5c — Enter N src(236,1004). CODE-CONFIRMED.
        var enterBtn = MakeCharButton(
            enterR,
            CharacterSelectLayout.EnterButton with { X = enterR.X, Y = (int)btnBarY },
            CharacterSelectLayout.AtlasLoginWindow,
            CharacterSelectLayout.EnterActionId, // 6, spec §8.2
            ""); // no text — baked art label. spec §11.5c "Enter" (236,1004). CODE-CONFIRMED.
        enterBtn.ActionFired += OnCharAction;
        AddChild(enterBtn);
        widgetCount++;

        // The 3D viewport (Layer 0) was already inserted as the first child in BuildUi().
        // The 2D chrome (info panel, buttons, slot row) composites on top of the 3D scene.
        // No per-slot SubViewport boxes needed anymore — the unified CharSelectScene3D handles
        // the full map000 backdrop + character row + camera.
        // spec: Docs/RE/specs/frontend_scenes.md §3 — "a full 3D world backdrop". CODE-CONFIRMED.

        // --- Slot selector row (text buttons below the 3D region, above the button bar) ---
        widgetCount += BuildSlotSelectorRow();

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

        // NOTE: char_select-u.xeff (effect_id 380003000, 68 sub-effects) is a COMPOSITE 3D effect —
        // torch coronas placed in the 3D cavern. Braziers live in the 3D subviewport (CharSelectScene3D),
        // NOT as a 2D fullscreen overlay on the canvas.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED — "spawned ONCE in the 3D cavern
        //   at world ≈ (508.5, 69.9, −9758.6), scale 1.0; part of the 3D scene — NOT a 2D overlay".
        // A 2D FrontEndEffectPlayer with raw SizeX ~160–194 × 24f × 20f = ~77,000 px effective particle
        // size covers the entire viewport (the full-red-screen bug). Braziers are GPUParticles3D in
        // CharSelectScene3D.BuildBrazierEffect(). Aesthetic/3D only — no 2D overlay here.

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
        // MouseFilter.Pass so the container forwards GuiInput events while the 2D chrome nodes
        // on top can still receive their own clicks (Stop/Pass on those children as needed).
        // spec: frontend_scenes.md §3.3.3 — click on the 3D viewport region drives slot selection. CODE-CONFIRMED.
        var container = new SubViewportContainer
        {
            Name = "Scene3DContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Pass, // pass so we receive GuiInput for ray-pick
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_scene3DViewport);

        // Wire GuiInput for 3D ray-pick slot selection.
        // spec: frontend_scenes.md §3.3.3 — "unproject click pixel through perspective camera;
        //   test each slot AABB; first hit → confirmed-pick". CODE-CONFIRMED.
        container.GuiInput += OnViewport3DGuiInput;

        _scene3DContainer = container; // held for coordinate scaling in OnViewport3DGuiInput
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
    // Camera pose-button scaffolding REMOVED — CAMPAIGN 9.
    // The legacy char-select camera is a SINGLE STATIC camera; the "6-keyframe orbit"
    // (and its pose-button selector) was REFUTED by re-RE this campaign. Camera framing +
    // the two manual hold-to-move inputs (boom-zoom, preview-actor yaw) live in
    // CharSelectScene3D / CharSelectCameraRig. spec: Docs/RE/specs/frontend_scenes.md §3.5.
    // =========================================================================

    // =========================================================================
    // Create sub-form — spec: frontend_scenes.md §4. CODE-CONFIRMED.
    //
    // Layout (faithful to the official client visual target):
    //   FULL-SCREEN overlay (1024×768) with three regions:
    //   LEFT  (x=0..200)   : class selection list (4 classes)
    //   CENTER(x=200..620)  : enlarged 3D preview (CharCreatePreview3D, scale 75)
    //                         + turntable L/R buttons + face ± buttons
    //   RIGHT (x=620..1024) : class name / description, stat allocation (+/− budget),
    //                         name textbox, OK/Cancel
    //   BOTTOM caption strip with the orange "Create Character" label.
    //
    // spec: Docs/RE/specs/frontend_scenes.md §4 CODE-CONFIRMED.
    //   Class choice: UI index 0..3 → internal {4,1,3,2}. Face index 1..7.
    //   Scale 75. Turntable ≈±2 rad/s press-and-hold. No sex toggle.
    //   Stat allocation: 6 stats, floor 10, shared budget, remaining-points display.
    //   Name: min 2 chars, lowercase a–z + digits + Hangul; rejection toast msg id 2075.
    // =========================================================================

    private Control BuildCreateForm()
    {
        // Full-screen semi-transparent overlay drawn over the 3D char-select scene.
        // spec: frontend_scenes.md §4 — "sub-state drawn over the SAME 3D char-select scene". CODE-CONFIRMED.
        var form = new Control
        {
            Name = "CreateForm",
            MouseFilter = MouseFilterEnum.Stop, // capture clicks so they don't fall through
        };
        form.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Dim overlay: LIGHT dim so the 3D temple remains visible through the panels.
        // Reference screenshots (055712/709/716/720) clearly show the carved stone temple
        // columns behind the LEFT and RIGHT panels — the 3D backdrop is NOT fully hidden.
        var dimBg = new ColorRect
        {
            Name = "CreateDim",
            Color = new Color(0f, 0f, 0f, 0.30f),
        };
        dimBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        form.AddChild(dimBg);

        // ── ORANGE TOP CAPTION ──────────────────────────────────────────────
        // The char-count caption "캐릭터 개수 : N" stays centred at the top of the create
        // form, same as on the select view.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED — MessageDB id 2209.
        // "Create Character" note is not a separate label in the spec; the count caption
        // persists on the create sub-state. We reuse BuildCharCountCaption() for accuracy.
        var topCaption = WidgetFactory.MakeLabel(
            BuildCharCountCaption(),
            CharacterSelectLayout.FontTitleHeight,
            new Color(0.95f, 0.86f, 0.55f)); // same orange as select view
        topCaption.Name = "CreateCountCaption";
        topCaption.Position = new Vector2(0f, 14f);
        topCaption.Size = new Vector2(1024f, 28f);
        topCaption.HorizontalAlignment = HorizontalAlignment.Center;
        form.AddChild(topCaption);

        // ── LEFT PANEL: class selection list ────────────────────────────────
        // spec: frontend_scenes.md §4.1 — "class-selection list on the LEFT". CODE-CONFIRMED.
        // Semi-transparent so the 3D temple stone is partially visible behind it (ref screenshots).
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

        var classTitle = WidgetFactory.MakeLabel("Class", CharacterSelectLayout.FontTitleHeight,
            new Color(0.90f, 0.80f, 0.45f));
        classTitle.Position = new Vector2(8f, 10f);
        leftPanel.AddChild(classTitle);

        // 4 class buttons stacked vertically.
        // spec: frontend_scenes.md §4.1 — "4 classes, UI index 0..3". CODE-CONFIRMED.
        for (int ci = 0; ci < 4; ci++)
        {
            uint msgId = CharacterSelectLayout.ClassLabelMsgIds[ci]; // 14003..14006
            string fallback = CharacterSelectLayout.ClassLabelFallbacks[ci];
            string classCaption = _assets.Text(msgId, fallback);

            var classBtn = new Button
            {
                Name = $"CreateClassBtn{ci}",
                Text = classCaption,
                Position = new Vector2(8f, 42f + ci * 48f),
                Size = new Vector2(174f, 40f),
                ToggleMode = true,
                ButtonPressed = ci == _createUiClassIndex,
            };
            classBtn.AddThemeFontSizeOverride("font_size", 13);
            int uiIdx = ci;
            classBtn.Pressed += () => SetCreateClass(uiIdx);
            leftPanel.AddChild(classBtn);
        }

        // Current class name display.
        _createClassLabel = WidgetFactory.MakeLabel(
            ClassCaption(_createUiClassIndex),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.90f, 0.85f, 0.55f),
            multiline: true);
        _createClassLabel.Position = new Vector2(8f, 240f);
        _createClassLabel.Size = new Vector2(174f, 36f);
        leftPanel.AddChild(_createClassLabel);

        // ── CENTER PANEL: 3D preview + face ± + turntable buttons ───────────
        // spec: frontend_scenes.md §4.2 — "single, centered, +56.5 units nearer camera". CODE-CONFIRMED.
        // spec: frontend_scenes.md §4.2 — "scale 75 vs the slots' 50". CODE-CONFIRMED.
        // The center panel has NO solid background — the 3D scene is the backdrop.
        // The character preview (CharCreatePreview3D) is a SubViewport that renders the model.
        // Reference screenshots show the carved stone temple visible behind the character.
        var centerPanel = new Control
        {
            Name = "CreateCenter",
            Position = new Vector2(200f, 46f),
            Size = new Vector2(424f, 660f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(centerPanel);

        // 3D preview occupies most of the center area.
        _createPreview3D = new CharCreatePreview3D
        {
            Name = "CreatePreview3D",
            Position = new Vector2(2f, 2f),
            Size = new Vector2(420f, 600f),
            SharedRealAssets = _realAssets,
            InternalClassId = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex],
        };
        centerPanel.AddChild(_createPreview3D);

        // Face ± buttons below the 3D preview.
        // spec: frontend_scenes.md §4.2 — "face ± buttons range 1..7". CODE-CONFIRMED.
        // "the visible 3D face does NOT change". CODE-CONFIRMED.
        // Placed at y=608 (just below the 600h preview area).
        var faceLbl = WidgetFactory.MakeLabel("Face:", CharacterSelectLayout.FontRowHeight,
            new Color(0.75f, 0.75f, 0.75f));
        faceLbl.Position = new Vector2(8f, 610f);
        centerPanel.AddChild(faceLbl);

        var faceMinus = new Button
        {
            Name = "FaceMinus",
            Text = "−",
            Position = new Vector2(54f, 607f),
            Size = new Vector2(28f, 22f),
        };
        faceMinus.Pressed += () => ChangeFace(-1);
        centerPanel.AddChild(faceMinus);

        _createFaceLabel = WidgetFactory.MakeLabel(
            _createFaceIndex.ToString(),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.95f, 0.95f, 0.95f));
        _createFaceLabel.Position = new Vector2(87f, 610f);
        _createFaceLabel.Size = new Vector2(24f, 22f);
        _createFaceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        centerPanel.AddChild(_createFaceLabel);

        var facePlus = new Button
        {
            Name = "FacePlus",
            Text = "+",
            Position = new Vector2(116f, 607f),
            Size = new Vector2(28f, 22f),
        };
        facePlus.Pressed += () => ChangeFace(+1);
        centerPanel.AddChild(facePlus);

        // Turntable L / R buttons (press-and-hold).
        // spec: frontend_scenes.md §4.2 — "press-and-hold turntable ≈±2 rad/s while a rotate control
        //   is held". CODE-CONFIRMED. NOT auto-spin.
        var rotLeftBtn = new Button
        {
            Name = "RotLeft",
            Text = "◄",
            Position = new Vector2(170f, 607f),
            Size = new Vector2(36f, 22f),
        };
        rotLeftBtn.ButtonDown += () => _rotatePressLeft = true;
        rotLeftBtn.ButtonUp += () => _rotatePressLeft = false;
        centerPanel.AddChild(rotLeftBtn);

        var rotNote = WidgetFactory.MakeLabel("Rotate", CharacterSelectLayout.FontRowHeight,
            new Color(0.60f, 0.60f, 0.65f));
        rotNote.Position = new Vector2(210f, 610f);
        centerPanel.AddChild(rotNote);

        var rotRightBtn = new Button
        {
            Name = "RotRight",
            Text = "►",
            Position = new Vector2(260f, 607f),
            Size = new Vector2(36f, 22f),
        };
        rotRightBtn.ButtonDown += () => _rotatePressRight = true;
        rotRightBtn.ButtonUp += () => _rotatePressRight = false;
        centerPanel.AddChild(rotRightBtn);

        // ── RIGHT PANEL: description + stat allocation + name + buttons ──────
        // spec: frontend_scenes.md §4 — "two description/stat panels on the RIGHT". CODE-CONFIRMED.
        // Semi-transparent so the 3D temple remains visible behind the panel (ref screenshots).
        // Width: 1024 − 200 − 424 = 400px.
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

        // Class description header + large text block.
        // Reference screenshots show a large block of Korean text filling most of the right panel.
        // spec: frontend_scenes.md §4.1 — "class description". CODE-CONFIRMED (msg ids).
        // The description occupies roughly the top half of the right panel.
        var descHeader = WidgetFactory.MakeLabel("Description", CharacterSelectLayout.FontTitleHeight,
            new Color(0.90f, 0.80f, 0.45f));
        descHeader.Position = new Vector2(8f, 8f);
        rightPanel.AddChild(descHeader);

        // Large description text block — matches the big Korean text in the reference screenshots.
        // In the official client this contains multi-line class lore text from msg.xdb.
        var descText = WidgetFactory.MakeLabel(
            GetClassDescription(_createUiClassIndex),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.78f, 0.78f, 0.82f),
            multiline: true);
        descText.Name = "CreateDescText";
        descText.Position = new Vector2(8f, 32f);
        descText.Size = new Vector2(374f, 220f); // tall block matching the reference
        rightPanel.AddChild(descText);

        // Separator.
        var sepR = new ColorRect
        {
            Color = new Color(0.35f, 0.30f, 0.18f),
            Position = new Vector2(8f, 262f),
            Size = new Vector2(374f, 1f),
        };
        rightPanel.AddChild(sepR);

        // Stat allocation (6 stats, floor 10, shared budget).
        // Repositioned below the description block (separator now at 262px).
        // spec: frontend_scenes.md §4.2 — "6 stats, shared point budget, floor 10 each;
        //   +/- buttons; remaining-points display (pure display from the class template)". CODE-CONFIRMED.
        var statHeader2 = WidgetFactory.MakeLabel("Stats", CharacterSelectLayout.FontTitleHeight,
            new Color(0.90f, 0.80f, 0.45f));
        statHeader2.Position = new Vector2(8f, 270f);
        rightPanel.AddChild(statHeader2);

        _createBudgetLabel = WidgetFactory.MakeLabel("Points: 0", CharacterSelectLayout.FontRowHeight,
            new Color(1.0f, 0.75f, 0.20f));
        _createBudgetLabel.Name = "BudgetLabel";
        _createBudgetLabel.Position = new Vector2(200f, 270f);
        _createBudgetLabel.Size = new Vector2(176f, 18f);
        rightPanel.AddChild(_createBudgetLabel);

        string[] statNames = ["HP", "MP", "STR", "INT", "DEX", "STA"];
        InitCreateStats();

        for (int s = 0; s < 6; s++)
        {
            int row = s;
            float sy = 292f + s * 30f; // compact 30px stride (fits 6 stats in ~180px)

            var statName = WidgetFactory.MakeLabel(statNames[s], CharacterSelectLayout.FontRowHeight,
                new Color(0.72f, 0.72f, 0.75f));
            statName.Position = new Vector2(8f, sy + 2f);
            statName.Size = new Vector2(36f, 18f);
            rightPanel.AddChild(statName);

            var minusBtn = new Button
            {
                Name = $"StatMinus{s}",
                Text = "−",
                Position = new Vector2(48f, sy),
                Size = new Vector2(24f, 22f),
            };
            minusBtn.Pressed += () => AdjustStat(row, -1);
            rightPanel.AddChild(minusBtn);

            _createStatLabels[s] = WidgetFactory.MakeLabel(
                _createStatValues[s].ToString(),
                CharacterSelectLayout.FontRowHeight,
                new Color(0.92f, 0.92f, 0.92f));
            _createStatLabels[s].Name = $"StatVal{s}";
            _createStatLabels[s].Position = new Vector2(76f, sy + 2f);
            _createStatLabels[s].Size = new Vector2(40f, 18f);
            _createStatLabels[s].HorizontalAlignment = HorizontalAlignment.Center;
            rightPanel.AddChild(_createStatLabels[s]);

            var plusBtn = new Button
            {
                Name = $"StatPlus{s}",
                Text = "+",
                Position = new Vector2(120f, sy),
                Size = new Vector2(24f, 22f),
            };
            plusBtn.Pressed += () => AdjustStat(row, +1);
            rightPanel.AddChild(plusBtn);
        }

        // 6 stats × 30px = 180px → bottom of stat section ~= 292 + 180 = 472.
        // Separator.
        var sepR2 = new ColorRect
        {
            Color = new Color(0.35f, 0.30f, 0.18f),
            Position = new Vector2(8f, 480f),
            Size = new Vector2(374f, 1f),
        };
        rightPanel.AddChild(sepR2);

        // Name entry.
        // spec: frontend_scenes.md §4.4 — "min 2 chars; allowed = lowercase a–z + digits + Hangul;
        //   reject uppercase/punctuation; show rejection toast msg id 2075". CODE-CONFIRMED.
        var nameLabel = WidgetFactory.MakeLabel("Name:", CharacterSelectLayout.FontRowHeight,
            new Color(0.75f, 0.75f, 0.75f));
        nameLabel.Position = new Vector2(8f, 490f);
        rightPanel.AddChild(nameLabel);

        _createNameEntry = new LineEdit
        {
            Name = "NameEntry",
            PlaceholderText = "a-z / 0-9 / Hangul",
            Position = new Vector2(8f, 508f),
            Size = new Vector2(374f, 26f),
        };
        _createNameEntry.AddThemeFontSizeOverride("font_size", 13);
        rightPanel.AddChild(_createNameEntry);

        // Toast label for validation rejection.
        // spec: frontend_scenes.md §4.4 — "show a rejection toast (msg id 2075)". CODE-CONFIRMED.
        // Msg id 2075 caption text is in the VFS (CP949); we show an English fallback when absent.
        _createToastLabel = WidgetFactory.MakeLabel(
            string.Empty,
            CharacterSelectLayout.FontRowHeight,
            new Color(1.0f, 0.35f, 0.20f),
            multiline: true);
        _createToastLabel.Name = "NameToast";
        _createToastLabel.Position = new Vector2(8f, 538f);
        _createToastLabel.Size = new Vector2(374f, 36f);
        _createToastLabel.Visible = false;
        rightPanel.AddChild(_createToastLabel);

        // Confirm and Cancel buttons — placed near the bottom of the right panel (y~580+).
        // spec §8.2 action-id map — "Create-form Create=10, Cancel=13". CODE-CONFIRMED.
        // Repositioned to y=584 so they sit below the toast label (y=538+36=574).
        var confirmBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            22, 584, 162, 40,
            302, 860, // NORMAL src (InventWindow.dds confirm row). spec §8.2. CODE-CONFIRMED.
            actionId: 10, caption: "OK");
        confirmBtn.ActionFired += OnCreateConfirm;
        rightPanel.AddChild(confirmBtn);

        var cancelBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            208, 584, 162, 40,
            302, 900, // NORMAL src (InventWindow.dds cancel row). spec §8.2. CODE-CONFIRMED.
            actionId: 13, caption: "Cancel");
        cancelBtn.ActionFired += _ => HideCreateForm();
        rightPanel.AddChild(cancelBtn);

        return form;
    }

    /// <summary>
    /// Confirms the create form after local validation.
    /// spec: frontend_scenes.md §4.4 — local name validation then send intent.
    /// spec: frontend_scenes.md §4.5 — "OK → plays click SFX 861010101". CODE-CONFIRMED.
    /// </summary>
    private void OnCreateConfirm(int _actionId)
    {
        string name = _createNameEntry?.Text.Trim() ?? string.Empty;

        // Name validation. spec: frontend_scenes.md §4.4. CODE-CONFIRMED.
        if (!ValidateCreateName(name, out string toastMsg))
        {
            // Show rejection toast. spec: frontend_scenes.md §4.4 — "show rejection toast (msg id 2075)".
            // CODE-CONFIRMED. Msg id 2075 text is VFS-only; we show the Application-decoded text when
            // available, or an English fallback.
            ShowCreateToast(toastMsg);
            GD.Print($"[CharacterSelectScreen] Create name rejected: '{name}' → {toastMsg}");
            return;
        }

        int internalClass = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
        // Class map: UI index {0,1,2,3} → internal {4,1,3,2}. spec: frontend_scenes.md §4.1. CODE-CONFIRMED.
        // Starter gear is SERVER-ASSIGNED; the client class template values are preview-mannequin only.
        // spec: frontend_scenes.md §4.3 — "starter equipment = server-side". CODE-CONFIRMED.

        // Emit signal instead of calling BootFlow directly.
        // BootFlow/Application wires CreateCharacterRequested → IApplicationUseCases.CreateCharacterAsync.
        // spec: frontend_scenes.md §4 / §8 — "gather fields → guard → send 1/6 (52B)". CODE-CONFIRMED.
        // TODO(Tier-1): wire BootFlow.OnCreateCharacterRequested signal handler to
        //   IApplicationUseCases.CreateCharacterAsync(name, internalClass, faceIndex).
        EmitSignal(SignalName.CreateCharacterRequested, name, internalClass, _createFaceIndex);
        GD.Print($"[CharacterSelectScreen] CreateCharacterRequested emitted: name='{name}' " +
                 $"internalClass={internalClass} face={_createFaceIndex}. " +
                 "spec: frontend_scenes.md §4/§8 CODE-CONFIRMED.");
        HideCreateForm();
        // Refresh the char-count caption — the server will later send 3/6 (create-accept) which
        // increments BillingState +0x80; in offline mode we refresh now as the best-effort view update.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED — "create-accept → +1 + repaint".
        RefreshCharCountCaption();
    }

    /// <summary>
    /// Local name validation per spec §4.4.
    /// Returns true if valid; sets <paramref name="toastMsg"/> to the rejection reason on false.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.4. CODE-CONFIRMED.
    /// </summary>
    private bool ValidateCreateName(string name, out string toastMsg)
    {
        // spec: frontend_scenes.md §4.4 — "minimum length 2 characters". CODE-CONFIRMED.
        if (name.Length < 2)
        {
            // Msg id 2075 caption text is in the VFS (CP949); English fallback here.
            // spec: frontend_scenes.md §4.4 — "show a rejection toast (msg id 2075)". CODE-CONFIRMED.
            toastMsg = _assets.Text(2075u, "Name must be at least 2 characters.");
            return false;
        }

        // spec: frontend_scenes.md §4.4 — "allowed = lowercase a–z + digits + CP949 Hangul;
        //   rejected: uppercase Latin, punctuation, and any other byte". CODE-CONFIRMED.
        foreach (char c in name)
        {
            // Lowercase a-z. spec: §4.4 "ASCII lowercase a–z (0x61–0x7A)". CODE-CONFIRMED.
            if (c >= 'a' && c <= 'z') continue;
            // Digits 0-9. spec: §4.4 "ASCII digits 0–9 (0x30–0x39)". CODE-CONFIRMED.
            if (c >= '0' && c <= '9') continue;
            // CP949 Hangul: .NET char values for Hangul block U+AC00..U+D7A3.
            // The original client validates CP949 double-byte pairs; in our UTF-16 string
            // the equivalent is checking the Hangul syllable block.
            // spec: §4.4 "CP949 double-byte Hangul (valid lead + trail)". CODE-CONFIRMED.
            if (c >= '가' && c <= '힣') continue; // Hangul syllables
            if (c >= 'ᄀ' && c <= 'ᇿ') continue; // Hangul jamo
            if (c >= '㄰' && c <= '㆏') continue; // Hangul compatibility jamo

            // Any other character is rejected.
            toastMsg = _assets.Text(2075u, "Only a-z, 0-9, and Korean Hangul allowed.");
            return false;
        }

        toastMsg = string.Empty;
        return true;
    }

    /// <summary>
    /// Shows the create form toast for ~3 seconds then fades it.
    /// spec: frontend_scenes.md §4.4 — rejection toast msg id 2075. CODE-CONFIRMED.
    /// </summary>
    private void ShowCreateToast(string message)
    {
        if (_createToastLabel is null || !IsInstanceValid(_createToastLabel)) return;
        _createToastLabel.Text = message;
        _createToastLabel.Visible = true;
        _toastTimer = 3.0; // seconds
    }

    /// <summary>
    /// Returns the CP949-decoded class description text for the given UI class index (0..3).
    /// Text comes from <c>data/script/npc.scr</c> keys 1..4, string fields 0/1/2.
    ///
    /// <para>UI slot → npc.scr key mapping (crossover per spec):
    /// UI 0 → key 1 (Monk/internal 4), UI 1 → key 2 (Musa/internal 1),
    /// UI 2 → key 4 (Dosa/internal 3), UI 3 → key 3 (Salsu/internal 2).</para>
    ///
    /// <para>Falls back to English when npc.scr is unavailable (VFS absent).</para>
    ///
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 — npc.scr class keys 1..4, fields 0/1/2: CONFIRMED.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.1.1 — class description source = npc.scr: CONFIRMED.
    /// </summary>
    private string GetClassDescription(int uiIndex)
    {
        // Delegate to NpcScrDescriptions which loaded the real CP949 text at startup.
        // spec: config_tables.md §2.17.3 — UI-slot vs npc.scr key crossover: CONFIRMED.
        // spec: frontend_scenes.md §4.1.1 — description = npc.scr string fields 0/1/2: CONFIRMED.
        int idx = Mathf.Clamp(uiIndex, 0, 3);
        return _npcScrDesc.GetDescription(idx);
    }

    /// <summary>
    /// Initialises the stat allocation arrays from the class template.
    /// spec: frontend_scenes.md §4.2 — "floor 10 each; class template stats". CODE-CONFIRMED.
    /// </summary>
    private void InitCreateStats()
    {
        int[] base_ = CreateClassBaseStats[_createUiClassIndex];
        _createStatBudgetRemaining = CreateClassBudgets[_createUiClassIndex];
        for (int s = 0; s < 6; s++)
            _createStatValues[s] = base_[s];
        RefreshStatDisplay();
    }

    /// <summary>
    /// Adjusts a stat by delta (+1 or -1) within the budget/floor constraints.
    /// spec: frontend_scenes.md §4.2 — "shared point budget, floor 10 each". CODE-CONFIRMED.
    /// </summary>
    private void AdjustStat(int statIndex, int delta)
    {
        if (statIndex < 0 || statIndex >= 6) return;
        int newVal = _createStatValues[statIndex] + delta;

        // Floor 10 per stat. spec: frontend_scenes.md §4.2. CODE-CONFIRMED.
        if (newVal < 10) return;

        // Budget check (only applies when spending points, i.e. delta > 0).
        if (delta > 0 && _createStatBudgetRemaining <= 0) return;

        _createStatValues[statIndex] = newVal;
        _createStatBudgetRemaining -= delta;
        RefreshStatDisplay();
    }

    /// <summary>Refreshes all stat labels and the budget label.</summary>
    private void RefreshStatDisplay()
    {
        for (int s = 0; s < 6; s++)
        {
            if (_createStatLabels[s] is not null && IsInstanceValid(_createStatLabels[s]))
                _createStatLabels[s].Text = _createStatValues[s].ToString();
        }

        if (_createBudgetLabel is not null && IsInstanceValid(_createBudgetLabel))
            _createBudgetLabel.Text = $"Points: {_createStatBudgetRemaining}";
    }

    // =========================================================================
    // 3D viewport ray-pick input
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.3 — slot selection is a 3D ray-pick. CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    /// Handles GuiInput from the SubViewportContainer.
    /// On a left mouse-button press, converts the local position to SubViewport coordinates
    /// and calls <see cref="CharSelectScene3D.TryHitTestSlot"/> for slot ray-pick.
    ///
    /// <para>This replaces the old text-button selection for the 3D row.
    /// The text-button row remains as a keyboard/headless fallback.</para>
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.3 CODE-CONFIRMED —
    ///   "slot selection = 3D world-space ray-pick; no 2D screen-rect test".
    /// </summary>
    private void OnViewport3DGuiInput(InputEvent ev)
    {
        if (_scene3D is null || _scene3DViewport is null || _scene3DContainer is null) return;
        if (_createFormVisible) return; // create form is on top — do not pick through it

        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb)
        {
            // Convert the click position (container-local) to SubViewport pixel coordinates.
            // The SubViewportContainer (FullRect, Stretch=true) scales the viewport to its own size.
            // SubViewport pixel = click_local * (vpSize / containerSize).
            // spec: ui_system.md §8.1 "reference canvas 1024×768". CODE-CONFIRMED.
            global::Godot.Vector2 vpSize = new(_scene3DViewport.Size.X, _scene3DViewport.Size.Y);
            global::Godot.Vector2 ctrlSize = _scene3DContainer.Size;
            float scaleX = ctrlSize.X > 0f ? vpSize.X / ctrlSize.X : 1f;
            float scaleY = ctrlSize.Y > 0f ? vpSize.Y / ctrlSize.Y : 1f;
            global::Godot.Vector2 vpPos = new(mb.Position.X * scaleX, mb.Position.Y * scaleY);

            int hit = _scene3D.TryHitTestSlot(vpPos);
            if (hit >= 0)
            {
                // spec: frontend_scenes.md §3.3.3 — "first slot whose box the ray hits → confirmed-pick". CODE-CONFIRMED.
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
            case CharacterSelectLayout.CreateActionId: // 4 — spec §8.2
                GD.Print("[Screens] CharacterSelectScreen: Create pressed (action 4) — opening create form.");
                ShowCreateForm();
                break;

            case CharacterSelectLayout.DeleteActionId: // 5 — spec §8.2
                GD.Print($"[Screens] CharacterSelectScreen: Delete pressed (action 5) — " +
                         $"offline stub (slot={_selectedSlot}, no use-case available).");
                // Refresh char-count caption on delete intent — the server delete-accept (3/4 or 3/7)
                // decrements BillingState +0x80 and repaints; in offline stub we do a best-effort refresh.
                // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED — "delete-accept → −1 + repaint".
                RefreshCharCountCaption();
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

        // Ensure the 3D preview shows the right class and has been built.
        // spec: frontend_scenes.md §4.2 — preview rebuilds on each Create entry. CODE-CONFIRMED.
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            int internalClass = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
            _createPreview3D.InternalClassId = internalClass;
            _createPreview3D.RebuildForClass();
        }

        // Reset stat allocation for the current class.
        InitCreateStats();

        // Reset toast.
        _toastTimer = 0.0;
        if (_createToastLabel is not null && IsInstanceValid(_createToastLabel))
            _createToastLabel.Visible = false;

        // Reset turntable buttons.
        _rotatePressLeft = false;
        _rotatePressRight = false;

        GD.Print($"[CharacterSelectScreen] Create form opened (class UI={_createUiClassIndex}, " +
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
        // spec: frontend_scenes.md §4.1 — "UI index 0..3 → internal {4,1,3,2}". CODE-CONFIRMED.
        _createUiClassIndex = Mathf.Clamp(uiIndex, 0, 3);
        int internalClass = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
        GD.Print($"[Screens] CharacterSelectScreen: Create class selected: " +
                 $"UI={_createUiClassIndex} → internal={internalClass}.");

        // Update class name label.
        if (_createClassLabel is not null && IsInstanceValid(_createClassLabel))
            _createClassLabel.Text = ClassCaption(_createUiClassIndex);

        // Update description text.
        // spec: frontend_scenes.md §4.1 — "shows the class description". CODE-CONFIRMED.
        Node? descNode = _createForm?.FindChild("CreateDescText", owned: false);
        if (descNode is Label descLbl)
            descLbl.Text = GetClassDescription(_createUiClassIndex);

        // Rebuild the 3D preview for the new class.
        // spec: frontend_scenes.md §4.2 — "changing the class rebuilds the whole actor". CODE-CONFIRMED.
        if (_createPreview3D is not null && IsInstanceValid(_createPreview3D))
        {
            _createPreview3D.InternalClassId = internalClass;
            _createPreview3D.RebuildForClass();
        }

        // Re-initialise stat allocation for the new class.
        // spec: frontend_scenes.md §4.2 — "per-class stat preview filled from the class template". CODE-CONFIRMED.
        InitCreateStats();

        // Update toggle state of class buttons (radio-button effect).
        for (int ci = 0; ci < 4; ci++)
        {
            Node? btnNode = _createForm?.FindChild($"CreateClassBtn{ci}", owned: false);
            if (btnNode is Button btn)
                btn.ButtonPressed = ci == _createUiClassIndex;
        }
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
    // Char-count caption — msg id 2209 "캐릭터 개수 : %d"
    // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    /// Returns the formatted "character count : N" caption for the title bar.
    /// Uses MessageDB template id 2209 with the current occupied-slot count.
    ///
    /// <para>Online: count comes from BillingState +0x80 (account-wide, independent of slot mask).
    /// Offline: we derive it from the current roster occupancy — the nearest available value.
    /// The Application layer will overwrite this via <see cref="ApplyCharacterList"/> when live data arrives.</para>
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED (msg id 2209, count source).
    /// </summary>
    private string BuildCharCountCaption()
    {
        // Count occupied slots from live data or the demo roster.
        int count;
        if (_liveDataApplied)
        {
            count = 0;
            for (int i = 0; i < MaxSlots; i++)
                if (!_liveSlots[i].IsEmpty)
                    count++;
        }
        else
        {
            count = DemoRoster.Length;
        }

        // Retrieve the CP949-decoded template from the msg.xdb catalogue via UiAssetLoader.
        // Template is "캐릭터 개수 : %d" (VFS-only; not reproduced here).
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED (msg id 2209).
        // Fallback when VFS absent: English approximation so the offline flow shows something.
        string template = _assets.Text(2209u, "캐릭터 개수 : {0}");

        // The template uses a C-style %d; we do a simple substitution of the placeholder.
        // UiAssetLoader already decodes the CP949 text; we only swap %d → count.
        string caption = template.Contains("%d")
            ? template.Replace("%d", count.ToString())
            : string.Format(template, count); // handle both printf and .NET placeholder fallbacks
        return caption;
    }

    /// <summary>
    /// Refreshes the title-bar "character count : N" caption widget in-place.
    /// Call after create or delete operations mutate the character count.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.8.2 CODE-CONFIRMED — "re-rendered after create and after delete".
    /// </summary>
    private void RefreshCharCountCaption()
    {
        if (_charCountCaption is null || !IsInstanceValid(_charCountCaption)) return;
        _charCountCaption.Text = BuildCharCountCaption();
        GD.Print($"[CharacterSelectScreen] Char-count caption refreshed: '{_charCountCaption.Text}'. " +
                 "spec: frontend_scenes.md §3.8.2 CODE-CONFIRMED.");
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