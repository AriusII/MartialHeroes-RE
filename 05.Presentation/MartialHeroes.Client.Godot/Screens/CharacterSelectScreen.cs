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
//  LEFT column (vertical, per screenshots): 4 class buttons from loginwindow.dds src-Y=1005,
//              size ~45×19, src-X=590/635/680/725 (idle) / 770/815/860/905 (selected).
//              Stacked VERTICALLY — screenshots confirm a LEFT VERTICAL COLUMN (not a horizontal strip).
//              spec: Docs/RE/specs/frontend_scenes.md §4 / §11.5e CODE-CONFIRMED (screenshots win).
//              Actions 10/11/12/13. spec: ui_system.md §8.2 CODE-CONFIRMED.
//  LEFT column (below class buttons): stat area (pure display, 8 rows, 2·disc+{110..141} keys).
//              spec: Docs/RE/formats/config_tables.md §2.17.3 CODE-CONFIRMED (two-witness).
//              NOTE: stat values = PURE DISPLAY (class template). No ± steppers (screenshots confirm).
//  CENTER: CharCreatePreview3D SubViewport — LARGE, near full-height, character fills frame.
//              spec: Docs/RE/specs/frontend_scenes.md §4.2 CODE-CONFIRMED.
//  CENTER: Face ± buttons (actionId 21/22). spec: ui_system.md §8.2 CODE-CONFIRMED.
//  CENTER: Turntable L/R buttons (press-and-hold ≈±2 rad/s). spec: frontend_scenes.md §4.2 CODE-CONFIRMED.
//  RIGHT panel: description plate ~215×274 from mainwindow.dds chrome.
//              spec: Docs/RE/specs/frontend_scenes.md §11.5e + §4.1.1 CODE-CONFIRMED.
//              Three CP949 lines from npc.scr keys 1..4 (via NpcScrDescriptions).
//              spec: Docs/RE/formats/config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
//  NAME MODAL (on-demand, shown on create-confirm intent):
//              Title = class name (msg.xdb 14003..14007). spec: frontend_scenes.md §4.1.1 CODE-CONFIRMED.
//              Name textbox (LineEdit) + OK (actionId 35) / Cancel (actionId 36) from inventwindow.dds.
//              spec: Docs/RE/specs/ui_system.md §8.2 CODE-CONFIRMED.
//              Toast label (msg id 2075). spec: frontend_scenes.md §4.4 CODE-CONFIRMED.
//
// SIGNALS (preserved from prior version — Lane E keeps the same API):
//   EnterGameRequested(string characterName, int slotIndex)
//   BackRequested()
//   CreateCharacterRequested(string name, int internalClass, int faceIndex)
//   DeleteCharacterRequested(int slotIndex, string characterName)
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

    /// <summary>
    /// Raised when the player presses Delete on an occupied slot.
    /// SelectScene routes this passive intent to the Application layer once the core facade exposes the
    /// recovered delete use-case. spec: Docs/RE/specs/frontend_scenes.md §5 / §8.
    /// </summary>
    [Signal]
    public delegate void DeleteCharacterRequestedEventHandler(int slotIndex, string characterName);

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

        // Refresh display (main thread). No 2D slot strip to rebuild; selection is 3D ray-pick.
        RefreshInfo();
        RefreshCharCountCaption();

        // Push slot occupancy into the 3D scene.
        PushSlotDescriptorsToScene();

        // The deferred Initialise (one frame after Build3DSceneViewport) builds the actor ROW from the
        // descriptors AS THEY ARE THEN — which is blank, because the 3/1 list (or the dev seed) arrives
        // a frame or more LATER via CharListEventDrainer → here. Setting SlotDescriptors above does NOT
        // rebuild the already-built (blank) row, so we must explicitly refresh it now. RefreshSlotActors
        // is a no-op before init (the pre-init case is handled by the deferred Initialise), so this is
        // self-guarding: it only rebuilds once the scene is initialised. spec: frontend_scenes.md §3.3.1.
        _scene3D?.RefreshSlotActors(_realAssets);
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
    // NOTE: the class NAME (msg.xdb 14003..14007) lives in the NAME MODAL title — NOT a static panel label.
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 CODE-CONFIRMED.
    private Label _createDescLine0 = null!; // npc.scr line 0 (+0x14). spec: config_tables.md §2.17.3.
    private Label _createDescLine1 = null!; // npc.scr line 1 (+0x54). spec: config_tables.md §2.17.3.
    private Label _createDescLine2 = null!; // npc.scr line 2 (+0x94). spec: config_tables.md §2.17.3.
    private Label _createFaceLabel = null!; // face index display

    // Name modal — shown on-demand when the player presses an explicit "Name" confirm intent.
    // The modal title is the class name from msg.xdb 14003..14007.
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 / §4.4 CODE-CONFIRMED.
    private Control _nameModal = null!;
    private Label _nameModalTitle = null!; // class name (msg.xdb 14003..14007), updated on class change.

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

    // Slot selection is 3D ray-pick via CharSelectScene3D.TryHitTestSlot and the 3 slot-tab buttons
    // (actions 1/2/3, §11.5b). There is no 2D button strip.
    // spec: Docs/RE/specs/frontend_scenes.md §11.5b / §3.3.3 CODE-CONFIRMED.

    /// <summary>Optional shared asset loader injected by the flow node.</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        _assets = SharedAssets ?? UiAssetLoader.Open();
        _ownsAssets = SharedAssets is null;

        try
        {
            _realAssets = RealClientAssets.TryOpen();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharacterSelectScreen] VFS open for 3D previews failed: {ex.Message}");
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

        // Character-info background art from loginwindow.dds (the real atlas plate).
        // Root panel: X=(W/2-288),575, 244x187. Char-info background art: src (556,542) 215x147.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5a "Char-info background art T1 (556,542)". CODE-CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5d "Info plate 0,142,215,147 src 556,542". CODE-CONFIRMED.
        // We place the char-info area at the left info panel origin (0,142 in window-local, mapped to canvas).
        // The centred panel origin is X=(W-215)/2, Y=575+0 per §11.5a; we use (5,142) as the offset within it.
        const float infoPanelOriginX = (1024f - 215f) / 2f; // spec §11.5a "X=(W-215)/2". CODE-CONFIRMED.
        const float infoPanelOriginY = 575f; // spec §11.5a "y=575". CODE-CONFIRMED.

        // Char-info background art: loginwindow.dds src (556,542) 215x147, at (0,142) panel-local.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5d "Info plate 0,142,215,147 src 556,542". CODE-CONFIRMED.
        var charInfoArt = WidgetFactory.MakeAtlasRect(_assets,
            CharacterSelectLayout.AtlasLoginWindow,
            (int)infoPanelOriginX, (int)(infoPanelOriginY + 142f),
            215, 147,
            556, 542); // spec §11.5d CODE-CONFIRMED.
        if (charInfoArt is not null)
        {
            AddChild(charInfoArt);
            widgetCount++;
        }

        // Info labels: caption ids 48001/48003/48004/48005 (name/level/position/class).
        // spec: Docs/RE/specs/frontend_scenes.md §11.5d "caption labels 48001/48003/48004/48005". CODE-CONFIRMED.
        // Placed over the info art background. When offline (no msg.xdb), labels are empty — faithfully.
        float infoLabelX = infoPanelOriginX + 46f; // spec §11.5d "x=46/51" per stat column. CODE-CONFIRMED.
        _infoName = BuildInfoLabel(this, string.Empty, new Vector2(infoLabelX, infoPanelOriginY + 145f));
        _infoLevel = BuildInfoLabel(this, string.Empty, new Vector2(infoLabelX, infoPanelOriginY + 169f));
        _infoClass = BuildInfoLabel(this, string.Empty, new Vector2(infoLabelX, infoPanelOriginY + 193f));
        widgetCount += 3;

        // Title/info chrome plates A/B/C from loginwindow.dds.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5a. CODE-CONFIRMED.
        // Plate A: 0,12,200,46 src 608,793.
        var plateA = WidgetFactory.MakeAtlasRect(_assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)infoPanelOriginX + 0, (int)infoPanelOriginY + 12, 200, 46, 608, 793);
        if (plateA is not null)
        {
            AddChild(plateA);
            widgetCount++;
        }

        // Plate B: 200,0,176,58 src 608,735.
        var plateB = WidgetFactory.MakeAtlasRect(_assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)infoPanelOriginX + 200, (int)infoPanelOriginY + 0, 176, 58, 608, 735);
        if (plateB is not null)
        {
            AddChild(plateB);
            widgetCount++;
        }

        // Plate C: 376,12,201,46 src 608,689.
        var plateC = WidgetFactory.MakeAtlasRect(_assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)infoPanelOriginX + 376, (int)infoPanelOriginY + 12, 201, 46, 608, 689);
        if (plateC is not null)
        {
            AddChild(plateC);
            widgetCount++;
        }

        // Additional info plate §11.5d "Info plate 215,0,29,22 src 556,729".
        var infoPlate2 = WidgetFactory.MakeAtlasRect(_assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)infoPanelOriginX + 215, (int)infoPanelOriginY + 0, 29, 22, 556, 729);
        if (infoPlate2 is not null)
        {
            AddChild(infoPlate2);
            widgetCount++;
        }

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
            0, 1004, // NORMAL src. spec §11.5c. CODE-CONFIRMED.
            0, 1004, // HOVER = NORMAL. spec §1.5.
            59, 1004, // PRESSED src. spec §11.5c. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.CreateActionId); // 4
        createBtn.ActionFired += OnCharAction;
        AddChild(createBtn);
        widgetCount++;

        // Delete button: loginwindow.dds N(118,1004) P(177,1004). spec §11.5c. CODE-CONFIRMED.
        var deleteBtn = WidgetFactory.MakeStateButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)(btnBarLeft + btnW + btnGap), (int)btnBarY, (int)btnW, (int)btnH,
            118, 1004, // NORMAL src. spec §11.5c. CODE-CONFIRMED.
            118, 1004, // HOVER = NORMAL.
            177, 1004, // PRESSED src. spec §11.5c. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.DeleteActionId); // 5
        deleteBtn.ActionFired += OnCharAction;
        AddChild(deleteBtn);
        widgetCount++;

        // Enter button: loginwindow.dds N(236,1004) P(295,1004). spec §11.5c. CODE-CONFIRMED.
        var enterBtn = WidgetFactory.MakeStateButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            (int)(btnBarLeft + 2f * (btnW + btnGap)), (int)btnBarY, (int)btnW, (int)btnH,
            236, 1004, // NORMAL src. spec §11.5c. CODE-CONFIRMED.
            236, 1004, // HOVER = NORMAL.
            295, 1004, // PRESSED src. spec §11.5c. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.EnterActionId); // 6
        enterBtn.ActionFired += OnCharAction;
        AddChild(enterBtn);
        widgetCount++;

        // Stat-icon grid (5 rows × 2 cols). spec §8.2+§8.4 CODE-CONFIRMED.
        widgetCount += BuildStatGrid(this);

        // NOTE: no 2D slot-selector button strip. Slot selection is via the 3 slot tabs (§11.5b,
        // actions 1/2/3, already built above) and 3D row ray-pick via CharSelectScene3D.TryHitTestSlot.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5b / §3.3.3 CODE-CONFIRMED.
        // The old 5-button 2D strip at y=535 was an invented element — removed.

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

    // Slot selection is performed via the 3 slot-tab buttons (actions 1/2/3, §11.5b) and
    // 3D ray-pick via CharSelectScene3D.TryHitTestSlot — no 2D button strip.
    // spec: Docs/RE/specs/frontend_scenes.md §11.5b / §3.3.3 CODE-CONFIRMED.

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

        // The create sub-form draws over the SAME 3D temple backdrop — no opaque overlay.
        // spec: frontend_scenes.md §4 — "sub-state drawn over the SAME 3D char-select scene". CODE-CONFIRMED.
        // spec: frontend_scenes.md §3.7.6 / §4 — "carved wall baked into the scene — no side panels". CODE-CONFIRMED.
        // No ColorRect dim, no StyleBoxFlat side panels — faithfully empty chrome when offline.

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

        // ── LEFT COLUMN: 4 class buttons (VERTICAL stack, per screenshots) + stat area ──
        //
        // POSITION DECISION: the 4 class-picker buttons form a LEFT VERTICAL COLUMN.
        // The screenshots are the visual oracle — they show a vertical left-column of class buttons.
        // spec: Docs/RE/specs/frontend_scenes.md §4 / §11.5e CODE-CONFIRMED (screenshots win over
        //        any formula suggesting a right-strip position).
        //
        // Button size ~45×19 from AppSelectorW/H constants. Vertical stride ~28px.
        // NORMAL src-Y=1005. src-X idle={590,635,680,725}; selected={770,815,860,905}.
        // spec: ui_system.md §8.2/§8.4 CODE-CONFIRMED.
        var leftPanel = new Control
        {
            Name = "CreateLeft",
            Position = new Vector2(8f, 46f),
            Size = new Vector2(200f, 680f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(leftPanel);

        // Class buttons — vertical column. Actions 10/11/12/13.
        // Size = AppSelectorW×AppSelectorH (45×19). Vertical stride = 28px.
        // SELECTED src-X = {770,815,860,905} (same row V=1005). IDLE src-X = {590,635,680,725}.
        // spec: Docs/RE/specs/ui_system.md §8.2+§8.4 (AppSelector generator). CODE-CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5e CODE-CONFIRMED.
        const int ClassBtnVertStride = 28; // vertical spacing for column layout. spec §11.5e.
        const int ClassBtnColX = 8; // left-column X offset within the left panel.
        const int ClassBtnColBaseY = 10; // top of the class-button column.
        for (int ci = 0; ci < 4; ci++)
        {
            int idleSrcX = CharacterSelectLayout.ClassBtnNormalSrcX[ci]; // {590,635,680,725}. CODE-CONFIRMED.
            // Selected highlight: {770,815,860,905} — same row V=1005.
            // spec: ui_system.md §8.4 "selected src-X base 770 (+45 per btn)". CODE-CONFIRMED.
            int selectedSrcX = CharacterSelectLayout.ClassBtnHoverSrcX[ci]; // reused for selected highlight.
            int btnY = ClassBtnColBaseY + ci * ClassBtnVertStride;

            // ActionIds 10/11/12/13. spec §8.2 CODE-CONFIRMED.
            var classBtn = WidgetFactory.MakeStateButton(
                _assets, CharacterSelectLayout.AtlasLoginWindow,
                ClassBtnColX, btnY,
                CharacterSelectLayout.AppSelectorW, CharacterSelectLayout.AppSelectorH,
                // NORMAL = idle src.
                idleSrcX, CharacterSelectLayout.ClassBtnNormalSrcY, // NORMAL. CODE-CONFIRMED.
                selectedSrcX, CharacterSelectLayout.ClassBtnNormalSrcY, // HOVER = selected highlight. CODE-CONFIRMED.
                idleSrcX, CharacterSelectLayout.ClassBtnNormalSrcY, // PRESSED = idle (no distinct pressed art).
                actionId: 10 + ci); // 10/11/12/13. spec §8.2 CODE-CONFIRMED.
            classBtn.ActionFired += OnCreateClassAction;
            leftPanel.AddChild(classBtn);
        }

        // Stat area — pure display, 8 rows, below class buttons.
        // spec: Docs/RE/formats/config_tables.md §2.17.3 CODE-CONFIRMED (two-witness).
        // Stat values = PURE DISPLAY from class template. No ± steppers.
        // spec: frontend_scenes.md §4.2 "pure display from the class template". CODE-CONFIRMED.
        float statAreaBaseY = ClassBtnColBaseY + 4 * ClassBtnVertStride + 16f;
        BuildCreateStatGrid(leftPanel, statAreaBaseY);

        // ── BACKGROUND: CharCreatePreview3D — FULL-SCREEN behind all overlay panels ──
        //
        // The create preview fills the ENTIRE form rect as a backdrop (OwnWorld3D=true keeps it
        // isolated from the char-select scene). The left/right/centre overlay panels draw on top.
        // This ensures the carved-wall cell fills all margins, matching the official captures where
        // the temple backdrop is visible edge-to-edge. spec: frontend_scenes.md §4 / §3.7.6.
        _createPreview3D = new CharCreatePreview3D
        {
            Name = "CreatePreview3D",
            MouseFilter = MouseFilterEnum.Ignore, // let input pass to overlapping panels
            SharedRealAssets = _realAssets,
            InternalClassId = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex],
        };
        _createPreview3D.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // full-screen backdrop
        form.AddChild(_createPreview3D); // added FIRST → z-order 0 (behind all other panels)

        // ── CENTER PANEL: face ± + turntable (overlay controls; preview is full-screen bg) ──
        //
        // The centre panel hosts the face/turntable buttons only — the 3D preview itself is now
        // full-screen above. Buttons positioned in canvas-absolute coordinates.
        // spec: frontend_scenes.md §4.2 CODE-CONFIRMED.
        const float CenterPanelX = 215f; // after left column (~200) + gap.
        const float CenterPanelW = 595f; // wide centre: 215..810 on the 1024 canvas.
        var centerPanel = new Control
        {
            Name = "CreateCenter",
            Position = new Vector2(CenterPanelX, 46f),
            Size = new Vector2(CenterPanelW, 680f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(centerPanel);

        // Face ± buttons (actions 21/22). spec: ui_system.md §8.2. CODE-CONFIRMED.
        // "face ± buttons range 1..7; the visible 3D face does NOT change". CODE-CONFIRMED.
        var faceDecrBtn = WidgetFactory.MakeAtlasButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            40, 626, 28, 22,
            483, 490, // face-decrement glyph. spec §8.2. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.FaceDecrementActionId); // 22
        if (faceDecrBtn is not null)
        {
            faceDecrBtn.Name = "FaceMinus";
            faceDecrBtn.ActionFired += OnFaceAction;
            centerPanel.AddChild(faceDecrBtn);
        }

        _createFaceLabel = WidgetFactory.MakeLabel(
            _createFaceIndex.ToString(),
            CharacterSelectLayout.FontRowHeight,
            new Color(0.95f, 0.95f, 0.95f));
        _createFaceLabel.Name = "FaceIndexLabel";
        _createFaceLabel.Position = new Vector2(73f, 629f);
        _createFaceLabel.Size = new Vector2(28f, 20f);
        _createFaceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        centerPanel.AddChild(_createFaceLabel);

        var faceIncrBtn = WidgetFactory.MakeAtlasButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            106, 626, 28, 22,
            505, 490, // face-increment glyph. spec §8.2. CODE-CONFIRMED.
            actionId: CharacterSelectLayout.FaceIncrementActionId); // 21
        if (faceIncrBtn is not null)
        {
            faceIncrBtn.Name = "FacePlus";
            faceIncrBtn.ActionFired += OnFaceAction;
            centerPanel.AddChild(faceIncrBtn);
        }

        // Turntable L/R (press-and-hold ≈±2 rad/s). Actions 23/24.
        // spec: frontend_scenes.md §4.2 CODE-CONFIRMED.
        var rotLeftBtn = WidgetFactory.MakeAtlasButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            150, 626, 36, 22,
            483, 490, // left arrow glyph.
            actionId: 23); // spec §8.2. CODE-CONFIRMED.
        if (rotLeftBtn is not null)
        {
            rotLeftBtn.Name = "RotLeft";
            rotLeftBtn.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                    _rotatePressLeft = mb.Pressed;
            };
            centerPanel.AddChild(rotLeftBtn);
        }

        var rotRightBtn = WidgetFactory.MakeAtlasButton(
            _assets, CharacterSelectLayout.AtlasLoginWindow,
            192, 626, 36, 22,
            505, 490, // right arrow glyph.
            actionId: 24); // spec §8.2. CODE-CONFIRMED.
        if (rotRightBtn is not null)
        {
            rotRightBtn.Name = "RotRight";
            rotRightBtn.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                    _rotatePressRight = mb.Pressed;
            };
            centerPanel.AddChild(rotRightBtn);
        }

        // "Name" / "Enter name" button in the centre-bottom — opens the name modal.
        // This is the trigger that advances the create flow to the name-entry stage.
        // spec: frontend_scenes.md §4 — "after class/appearance selection, player enters name". CODE-CONFIRMED.
        // Uses InventWindow.dds OK-row art (same chrome as the confirm buttons). ActionId=35 intent.
        var openNameBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            (int)(CenterPanelW / 2f - 56f), 650, 113, 40,
            302, 860, // NORMAL src (InventWindow.dds confirm row). spec §8.3. CODE-CONFIRMED.
            actionId: 37, // local create-form action: open name modal. No wire send here — view only.
            caption: string.Empty); // caption = msg.xdb 2301 (VFS-only); empty offline.
        openNameBtn.ActionFired += _ => ShowNameModal();
        centerPanel.AddChild(openNameBtn);

        // Cancel button in the centre-bottom — exits the create form.
        // spec: frontend_scenes.md §4 / §4.4 CODE-CONFIRMED.
        var cancelCreateBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            (int)(CenterPanelW / 2f + 62f), 650, 113, 40,
            302, 900, // NORMAL src (InventWindow.dds cancel row). spec §8.3. CODE-CONFIRMED.
            actionId: 36, // action 36 = Cancel. spec §8.2. CODE-CONFIRMED.
            caption: string.Empty); // caption = msg.xdb 2302 (VFS-only); empty offline.
        cancelCreateBtn.ActionFired += _ => HideCreateForm();
        centerPanel.AddChild(cancelCreateBtn);

        // ── RIGHT PANEL: description plate from mainwindow.dds + npc.scr 3-line description ──
        //
        // The right panel carries:
        //   • A chrome plate (~215×274) sourced from mainwindow.dds.
        //     spec: frontend_scenes.md §11.5e / §4.1.1 "right-hand panel chrome from mainwindow.dds". CODE-CONFIRMED.
        //   • A lower plate (InventWindow.dds centred 340×190) backing the description text area.
        //     spec: frontend_scenes.md §11.5e CODE-CONFIRMED.
        //   • Three npc.scr CP949 lines (record fields +0x14/+0x54/+0x94) as description labels.
        //     spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        //
        // NOTE: the class NAME (msg.xdb 14003..14007) is the TITLE of the NAME MODAL only —
        //       it is NOT a static label on this panel.
        //       spec: frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        //
        // The name entry + OK/Cancel live in the NAME MODAL (see BuildNameModal below).
        const float RightPanelX = 815f; // right column: 815..1016 on the 1024 canvas.
        const float RightPanelW = 201f;
        var rightPanel = new Control
        {
            Name = "CreateRight",
            Position = new Vector2(RightPanelX, 46f),
            Size = new Vector2(RightPanelW, 680f),
            MouseFilter = MouseFilterEnum.Pass,
        };
        form.AddChild(rightPanel);

        // Description chrome plate from mainwindow.dds (~215×274 area).
        // spec: frontend_scenes.md §11.5e "right panel chrome from mainwindow.dds". CODE-CONFIRMED.
        // Exact src rect in mainwindow.dds is not individually catalogued (Open question / UNVERIFIED).
        // We draw the plate as a full-width rect; when the atlas is online the src rect fills in;
        // when offline the panel is faithfully empty (no synthetic background).
        var descPlate = WidgetFactory.MakeAtlasRect(
            _assets, CharacterSelectLayout.AtlasMainWindow,
            0, 0, (int)RightPanelW, 274, // ~215×274. spec §11.5e. CODE-CONFIRMED approximate.
            0, 0); // src rect in mainwindow.dds: top-left block (exact rect UNVERIFIED).
        if (descPlate is not null)
        {
            descPlate.Name = "DescPlate";
            rightPanel.AddChild(descPlate);
        }

        // Lower backing plate from InventWindow.dds (centred 340×190).
        // spec: frontend_scenes.md §11.5e "lower plate InventWindow.dds 340×190". CODE-CONFIRMED.
        // Placed at y=274 (below the upper plate).
        var lowerPlate = WidgetFactory.MakeAtlasRect(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            0, 274, (int)RightPanelW, 190, // lower plate, panel-local.
            318, 647); // src (318,647) per §8.3 confirm-popup chrome. CODE-CONFIRMED.
        if (lowerPlate is not null)
        {
            lowerPlate.Name = "DescLowerPlate";
            rightPanel.AddChild(lowerPlate);
        }

        // npc.scr description — 3 labels (lines at record fields +0x14 / +0x54 / +0x94).
        // spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        // Text is CP949, already decoded by NpcScrDescriptions. Zero hardcoded text.
        string[] descLines = GetDescriptionLines(_createUiClassIndex);
        const float DescLineBaseY = 18f;
        const float DescLineStride = 60f; // ~60px between lines to fit 3 lines in 274px plate.
        var descColor = new Color(0.82f, 0.82f, 0.88f);

        _createDescLine0 = WidgetFactory.MakeLabel(
            descLines.Length > 0 ? descLines[0] : string.Empty,
            CharacterSelectLayout.FontRowHeight, descColor, multiline: true);
        _createDescLine0.Name = "DescLine0";
        _createDescLine0.Position = new Vector2(6f, DescLineBaseY);
        _createDescLine0.Size = new Vector2(RightPanelW - 12f, DescLineStride - 4f);
        rightPanel.AddChild(_createDescLine0);

        _createDescLine1 = WidgetFactory.MakeLabel(
            descLines.Length > 1 ? descLines[1] : string.Empty,
            CharacterSelectLayout.FontRowHeight, descColor, multiline: true);
        _createDescLine1.Name = "DescLine1";
        _createDescLine1.Position = new Vector2(6f, DescLineBaseY + DescLineStride);
        _createDescLine1.Size = new Vector2(RightPanelW - 12f, DescLineStride - 4f);
        rightPanel.AddChild(_createDescLine1);

        _createDescLine2 = WidgetFactory.MakeLabel(
            descLines.Length > 2 ? descLines[2] : string.Empty,
            CharacterSelectLayout.FontRowHeight, descColor, multiline: true);
        _createDescLine2.Name = "DescLine2";
        _createDescLine2.Position = new Vector2(6f, DescLineBaseY + 2f * DescLineStride);
        _createDescLine2.Size = new Vector2(RightPanelW - 12f, DescLineStride - 4f);
        rightPanel.AddChild(_createDescLine2);

        // ── NAME MODAL — on-demand, shown when "Open name entry" is triggered ──
        // Contains: class name title (msg.xdb 14003..14007), name LineEdit, OK (35)/Cancel (36).
        // spec: frontend_scenes.md §4.1.1 / §4.4 CODE-CONFIRMED.
        _nameModal = BuildNameModal();
        _nameModal.Visible = false;
        form.AddChild(_nameModal);

        return form;
    }

    // =========================================================================
    // Name modal — on-demand overlay shown when the player confirms character creation.
    // Contains: class name title (msg.xdb), name entry, OK (35) / Cancel (36).
    // spec: Docs/RE/specs/frontend_scenes.md §4.1.1 / §4.4 CODE-CONFIRMED.
    // =========================================================================

    private Control BuildNameModal()
    {
        // Modal centred on the canvas at ~400×200 (ConfirmPopup chrome: InventWindow.dds 340×190).
        // spec: ui_system.md §8.3 "ConfirmPopup 340×190 src (318,647)". CODE-CONFIRMED.
        const float modalW = 340f;
        const float modalH = 190f;
        const float modalX = (CharacterSelectLayout.RefWidth - modalW) / 2f; // centred.
        const float modalY = (CharacterSelectLayout.RefHeight - modalH) / 2f; // centred.

        var modal = new Control
        {
            Name = "NameModal",
            Position = new Vector2(modalX, modalY),
            Size = new Vector2(modalW, modalH),
            MouseFilter = MouseFilterEnum.Stop,
        };

        // Background chrome: InventWindow.dds 340×190 src (318,647).
        // spec: ui_system.md §8.3 CODE-CONFIRMED.
        var bg = WidgetFactory.MakeAtlasRect(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            0, 0, (int)modalW, (int)modalH,
            318, 647); // src (318,647). spec §8.3. CODE-CONFIRMED.
        if (bg is not null)
        {
            bg.Name = "NameModalBg";
            modal.AddChild(bg);
        }

        // Class name title (msg.xdb 14003..14007 — updated on class change).
        // spec: frontend_scenes.md §4.1.1 "class NAME = msg.xdb 14003..14007, shown in name modal title". CODE-CONFIRMED.
        _nameModalTitle = WidgetFactory.MakeLabel(
            ClassCaption(_createUiClassIndex),
            CharacterSelectLayout.FontTitleHeight,
            new Color(0.95f, 0.86f, 0.55f));
        _nameModalTitle.Name = "NameModalTitle";
        _nameModalTitle.Position = new Vector2(12f, 12f);
        _nameModalTitle.Size = new Vector2(modalW - 24f, 22f);
        _nameModalTitle.HorizontalAlignment = HorizontalAlignment.Center;
        modal.AddChild(_nameModalTitle);

        // Name-entry caption (msg.xdb id 48001, VFS-only — empty offline).
        // spec: frontend_scenes.md §11.5d "caption 48001". CODE-CONFIRMED.
        string nameCaptionText = _assets.Text(48001u, string.Empty);
        if (!string.IsNullOrEmpty(nameCaptionText))
        {
            var nameCaption = WidgetFactory.MakeLabel(nameCaptionText,
                CharacterSelectLayout.FontRowHeight, new Color(0.75f, 0.75f, 0.75f));
            nameCaption.Position = new Vector2(34f, 48f);
            modal.AddChild(nameCaption);
        }

        // Name LineEdit. spec: frontend_scenes.md §4.4 / §8.2. CODE-CONFIRMED.
        _createNameEntry = new LineEdit
        {
            Name = "NameEntry",
            Position = new Vector2(34f, 66f),
            Size = new Vector2(modalW - 68f, 26f),
        };
        _createNameEntry.AddThemeFontSizeOverride("font_size", 13);
        modal.AddChild(_createNameEntry);

        // Toast label (msg id 2075). spec: frontend_scenes.md §4.4 CODE-CONFIRMED.
        _createToastLabel = WidgetFactory.MakeLabel(
            string.Empty,
            CharacterSelectLayout.FontRowHeight,
            new Color(1.0f, 0.35f, 0.20f),
            multiline: true);
        _createToastLabel.Name = "NameToast";
        _createToastLabel.Position = new Vector2(34f, 96f);
        _createToastLabel.Size = new Vector2(modalW - 68f, 30f);
        _createToastLabel.Visible = false;
        modal.AddChild(_createToastLabel);

        // OK button (actionId=35) / Cancel (actionId=36) — InventWindow.dds.
        // spec: ui_system.md §8.2 "35/36 Create-form Confirm/Cancel". CODE-CONFIRMED.
        const float btnsY = 136f;
        var confirmBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            55, (int)btnsY, 113, 40,
            302, 860, // NORMAL src (InventWindow.dds confirm row). spec §8.3. CODE-CONFIRMED.
            actionId: 35, caption: _assets.Text(2301u, string.Empty)); // msg 2301; empty offline
        confirmBtn.ActionFired += OnCreateConfirm;
        modal.AddChild(confirmBtn);

        var cancelBtn = WidgetFactory.MakeStateButton2(
            _assets, CharacterSelectLayout.AtlasInventWindow,
            174, (int)btnsY, 113, 40,
            302, 900, // NORMAL src (InventWindow.dds cancel row). spec §8.3. CODE-CONFIRMED.
            actionId: 36, caption: _assets.Text(2302u, string.Empty)); // msg 2302; empty offline
        cancelBtn.ActionFired += _ => HideNameModal();
        modal.AddChild(cancelBtn);

        return modal;
    }

    // =========================================================================
    // Create-form stat-grid construction.
    // 8 rows, labels from 2·disc + {110..141}. spec config_tables.md §2.17.3 CODE-CONFIRMED.
    // Pure display — no ± steppers. spec: frontend_scenes.md §4.2 CODE-CONFIRMED.
    // Placed in the LEFT panel below the class buttons.
    // =========================================================================

    private void BuildCreateStatGrid(Control parent, float baseY)
    {
        const float rowH = 26f; // compact row height for the 8 rows.

        for (int row = 0; row < StatGridRowCount; row++)
        {
            float y = baseY + row * rowH;
            int keyOffset = CharacterSelectLayout.StatGridKeyOffsets[row]; // {110,111,120,121,130,131,140,141}

            // Key formula: 2·disc + offset. spec: config_tables.md §2.17.3 CODE-CONFIRMED.
            // disc=0 here (class-neutral base).
            uint statKey = (uint)(2 * StatDisc + keyOffset); // spec: 2·disc + {110..141}. CODE-CONFIRMED.
            // PURE DISPLAY: label comes from msg.xdb (VFS-only). Empty when offline — faithfully.
            string statName = _assets.Text(statKey, string.Empty);
            // spec: frontend_scenes.md §4.2 "pure display from the class template". CODE-CONFIRMED.
            // spec: "Stat values are PURE DISPLAY — NOT interactive point-buy +/- buttons". CODE-CONFIRMED.

            // Stat name label — empty when msg.xdb is absent (faithfully offline).
            _createStatNameLabels[row] = WidgetFactory.MakeLabel(statName,
                CharacterSelectLayout.FontRowHeight, new Color(0.72f, 0.72f, 0.75f));
            _createStatNameLabels[row].Name = $"StatName{row}";
            _createStatNameLabels[row].Position = new Vector2(8f, y + 2f);
            _createStatNameLabels[row].Size = new Vector2(100f, 18f);
            parent.AddChild(_createStatNameLabels[row]);

            // Stat value label (pure display — no ± buttons; Application delivers values via events).
            // spec: frontend_scenes.md §4.2 "pure display". CODE-CONFIRMED.
            _createStatLabels[row] = WidgetFactory.MakeLabel(string.Empty,
                CharacterSelectLayout.FontRowHeight, new Color(0.92f, 0.92f, 0.92f));
            _createStatLabels[row].Name = $"StatVal{row}";
            _createStatLabels[row].Position = new Vector2(112f, y + 2f);
            _createStatLabels[row].Size = new Vector2(56f, 18f);
            _createStatLabels[row].HorizontalAlignment = HorizontalAlignment.Center;
            parent.AddChild(_createStatLabels[row]);
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

    /// <summary>
    /// DEV-ONLY: opens the create form for screenshot/oracle verification. The caller
    /// (BootFlow dev_screen=create) is guarded by dev-offline mode; never used in the production flow.
    /// </summary>
    public void DevShowCreateForm() => ShowCreateForm();

    private void OnCharAction(int actionId)
    {
        switch (actionId)
        {
            case CharacterSelectLayout.CreateActionId: // 4
                GD.Print("[CharacterSelectScreen] Create (action 4) → opening create form.");
                ShowCreateForm();
                break;
            case CharacterSelectLayout.DeleteActionId: // 5
                OnDeletePressed();
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

    private void OnDeletePressed()
    {
        LiveSlot ls = _liveSlots[_selectedSlot];
        if (ls.IsEmpty)
        {
            GD.Print(
                $"[CharacterSelectScreen] Delete on empty slot {_selectedSlot} ignored. spec: frontend_scenes.md §5.");
            return;
        }

        GD.Print($"[CharacterSelectScreen] DeleteCharacterRequested: name='{ls.Name}' slot={_selectedSlot}. " +
                 "spec: frontend_scenes.md §5/§8.");
        EmitSignal(SignalName.DeleteCharacterRequested, _selectedSlot, ls.Name);
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
        // Close the name modal and the entire create form on success.
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

        // HIDE the char-select 3D backdrop while the create form is open: the create preview
        // fills the screen with its OWN carved-wall cell (OwnWorld3D=true), so the char-select
        // braziers/platform must not bleed into the margins around the create SubViewport.
        // Restore on HideCreateForm. spec: frontend_scenes.md §4 / §3.7.6 CODE-CONFIRMED.
        if (_scene3DContainer is not null && IsInstanceValid(_scene3DContainer))
            _scene3DContainer.Visible = false;

        // Hide name modal when first opening the create form.
        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;

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

        // Restore the char-select 3D backdrop (hidden during create). spec: frontend_scenes.md §4.
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
        // Update modal title with current class name before showing.
        // spec: frontend_scenes.md §4.1.1 — class NAME in name modal title. CODE-CONFIRMED.
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClassIndex);
        if (_createNameEntry is not null && IsInstanceValid(_createNameEntry))
            _createNameEntry.Text = string.Empty;
        if (_createToastLabel is not null && IsInstanceValid(_createToastLabel))
            _createToastLabel.Visible = false;
        _toastTimer = 0.0;
        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = true;
        GD.Print($"[CharacterSelectScreen] Name modal opened for class {_createUiClassIndex}. " +
                 "spec: frontend_scenes.md §4.1.1 CODE-CONFIRMED.");
    }

    private void HideNameModal()
    {
        if (_nameModal is not null && IsInstanceValid(_nameModal))
            _nameModal.Visible = false;
        _toastTimer = 0.0;
    }

    private void SetCreateClass(int uiIndex)
    {
        _createUiClassIndex = Mathf.Clamp(uiIndex, 0, 3);
        int internalClass = CharacterSelectLayout.UiToInternalClass[_createUiClassIndex];
        GD.Print($"[CharacterSelectScreen] Create class: UI={_createUiClassIndex} → internal={internalClass}. " +
                 "spec: frontend_scenes.md §4.1 CODE-CONFIRMED.");

        // NOTE: the class NAME label lives in the name modal title — NOT on the main panel.
        // spec: frontend_scenes.md §4.1.1 "class NAME in name modal title only". CODE-CONFIRMED.
        // Update it here so it is current when the modal is opened.
        if (_nameModalTitle is not null && IsInstanceValid(_nameModalTitle))
            _nameModalTitle.Text = ClassCaption(_createUiClassIndex);

        // Update the three npc.scr description lines (right panel).
        // spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        string[] lines = GetDescriptionLines(_createUiClassIndex);
        if (_createDescLine0 is not null && IsInstanceValid(_createDescLine0))
            _createDescLine0.Text = lines.Length > 0 ? lines[0] : string.Empty;
        if (_createDescLine1 is not null && IsInstanceValid(_createDescLine1))
            _createDescLine1.Text = lines.Length > 1 ? lines[1] : string.Empty;
        if (_createDescLine2 is not null && IsInstanceValid(_createDescLine2))
            _createDescLine2.Text = lines.Length > 2 ? lines[2] : string.Empty;

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
        // ClassLabelMsgIds has 5 entries (ids 14003..14007) for 4 active UI classes (0..3) plus a
        // placeholder at index 4. We clamp to the 4 active classes so index 4 is never returned as
        // a real class name. spec: ui_system.md §10 / frontend_scenes.md §4.1. CODE-CONFIRMED.
        int safeIndex = Math.Clamp(uiIndex, 0, 3); // 4 active classes; index 4 is placeholder only
        uint msgId = CharacterSelectLayout.ClassLabelMsgIds[safeIndex];
        return _assets.Text(msgId, CharacterSelectLayout.ClassLabelFallbacks[safeIndex]);
    }

    private string GetClassDescription(int uiIndex)
    {
        // Delegates to NpcScrDescriptions (loaded from data/script/npc.scr CP949).
        // spec: config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
        return _npcScrDesc.GetDescription(Mathf.Clamp(uiIndex, 0, 3));
    }

    /// <summary>
    /// Returns the npc.scr description as an array of up to 3 lines (record fields +0x14/+0x54/+0x94).
    /// Returns a <see cref="string"/><c>[]</c> of length 0..3; never null entries.
    /// spec: Docs/RE/formats/config_tables.md §2.17.3 + frontend_scenes.md §4.1.1 CODE-CONFIRMED.
    /// </summary>
    private string[] GetDescriptionLines(int uiIndex)
    {
        string joined = GetClassDescription(uiIndex);
        if (string.IsNullOrEmpty(joined))
            return [];
        // npc.scr Paragraph0/1/2 were joined with '\n' in NpcScrDescriptions.Load().
        return joined.Split('\n', StringSplitOptions.None);
    }

    // =========================================================================
    // Name validation. spec: frontend_scenes.md §4.4 CODE-CONFIRMED.
    // =========================================================================

    private bool ValidateCreateName(string name, out string toastMsg)
    {
        // spec: frontend_scenes.md §4.4 — "minimum length 2 characters". CODE-CONFIRMED.
        if (name.Length < 2)
        {
            // Faithfully empty offline — no invented English fallback text. spec §4.4 / campaign-9 rule.
            toastMsg = _assets.Text(2075u, string.Empty);
            return false;
        }

        // spec: frontend_scenes.md §4.4 — "a–z + digits + CP949 Hangul; reject all else". CODE-CONFIRMED.
        foreach (char c in name)
        {
            if (c >= 'a' && c <= 'z') continue; // a–z. CODE-CONFIRMED.
            if (c >= '0' && c <= '9') continue; // 0–9. CODE-CONFIRMED.
            if (c >= '가' && c <= '힣') continue; // Hangul syllables. CODE-CONFIRMED.
            if (c >= 'ᄀ' && c <= 'ᇿ') continue; // Hangul jamo. CODE-CONFIRMED.
            if (c >= '㄰' && c <= '㆏') continue; // Hangul compat jamo. CODE-CONFIRMED.
            // Faithfully empty offline — no invented English fallback text. spec §4.4 / campaign-9 rule.
            toastMsg = _assets.Text(2075u, string.Empty);
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
            if (!_liveSlots[i].IsEmpty)
                count++;

        // CP949 template from msg.xdb id 2209 — empty when offline; no hardcoded Korean string.
        // spec: Docs/RE/specs/frontend_scenes.md §3.8.2 "MessageDB id 2209". CODE-CONFIRMED.
        string template = _assets.Text(2209u, string.Empty);
        if (string.IsNullOrEmpty(template))
            return string.Empty; // faithfully empty when offline

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
            // Char info labels: name + level + class from slot data.
            // Caption labels: 48001/48003/48004/48005 per §11.5d.
            // spec: Docs/RE/specs/frontend_scenes.md §11.5d "caption labels 48001/48003/48004/48005". CODE-CONFIRMED.
            // No "Lv"/"Cl" English prefix — those are spec caption art; show values only.
            _infoName.Text = ls.Name;
            _infoLevel.Text = ls.Level.ToString();
            _infoClass.Text = ls.ServerClass.ToString();
        }
        else
        {
            _infoName.Text = string.Empty;
            _infoLevel.Text = string.Empty;
            _infoClass.Text = string.Empty;
        }

        HighlightSlot(_selectedSlot);
    }

    private void HighlightSlot(int index)
    {
        // Slot selection is reflected in the 3D scene only — no 2D button strip to highlight.
        // spec: Docs/RE/specs/frontend_scenes.md §11.5b / §3.3.3 CODE-CONFIRMED.
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
            norm.SrcX, norm.SrcY, // NORMAL. CODE-CONFIRMED.
            hov.SrcX, hov.SrcY, // HOVER. CODE-CONFIRMED.
            prs.SrcX, prs.SrcY, // PRESSED. CODE-CONFIRMED.
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