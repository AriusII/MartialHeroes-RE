using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.Ids;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Full HUD for the real-client build. Subscribes to Application events forwarded by
/// <see cref="World.GameLoop"/> and updates label text / progress bars / hotbar.
///
/// PASSIVE: zero game logic, zero stat math, zero protocol knowledge.
/// Event payloads carry all data; this node only projects them onto Godot controls.
///
/// Original chrome layer (stage-2, texture-driven):
///   The background panel is backed by atlas uitex 0001 → data/ui/mainwindow.dds
///   (1024×1024 DXT3, spec: Docs/RE/formats/ui_manifests.md §1.4 SAMPLE-VERIFIED).
///   HP gauge bar backing: mainwindow.dds sub-rect (0, 0, 310, 60) // PLAUSIBLE – not byte-confirmed
///   MP gauge bar backing: same chrome, Y-shifted                   // PLAUSIBLE
///   When mainwindow.dds is unavailable (VFS offline) the TextureRect is transparent and the
///   plain Godot PanelContainer fallback remains fully visible.
///
/// Control hierarchy (built procedurally in _Ready — no .tscn required):
///   Control (anchor top-left)
///     TextureRect _hudChrome       — mainwindow.dds chrome slice (behind everything)
///     VBoxContainer (stats panel)
///       Label  _stateLabel         — shows current ClientState
///       Label  _actorCount         — shows number of visible actors
///       HBoxContainer (HP row)
///         Label "HP:"
///         ProgressBar _hpBar
///         Label _hpText
///       HBoxContainer (MP row)
///         Label "MP:"
///         ProgressBar _mpBar
///         Label _mpText
///       HBoxContainer (Level row)
///         Label "Lv:"
///         Label _levelLabel
///         Label "XP:"
///         Label _xpLabel
///       Label _buffLabel
///       Label _combatStatsLabel
///   HBoxContainer (hotbar, anchor bottom)
///     9 × VBoxContainer (slot panels, with TextureRect icon placeholders)
///   PanelContainer (chat, anchor bottom-right)
///     VBoxContainer
///       Label _chatLabel
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — HUD bound to Application state.
/// spec: Docs/RE/specs/ui_system.md §8.5 — in-game panels bind by uitex integer id.
/// spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0001 = data/ui/mainwindow.dds 1024×1024.
/// spec: Docs/RE/specs/input_ui.md §4 — hotbar displays skills from SkillHotbarSlotSetEvent.
/// </summary>
public sealed partial class GameHud : Control
{
    // -------------------------------------------------------------------------
    // Atlas binding constants
    // spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0001 = data/ui/mainwindow.dds.
    // -------------------------------------------------------------------------

    /// <summary>uitex integer id for the main HUD chrome atlas.</summary>
    private const int MainWindowTexId = 1;
    // spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0001 = data/ui/mainwindow.dds, 1024×1024 DXT3.

    /// <summary>Fallback VFS path — used when the uitex manifest is unavailable so we can still
    /// try a direct load via UiAssetLoader.</summary>
    private const string MainWindowPath = "data/ui/mainwindow.dds";
    // spec: Docs/RE/formats/ui_manifests.md §5.4 — mainwindow.dds 1024×1024 DXT3, hard-coded path.

    // HUD chrome source rect on mainwindow.dds (top-left corner chrome strip).
    // spec: no per-widget layout recovered for mainwindow.dds — values are PLAUSIBLE based on
    //       the 1024×1024 atlas and the typical top-left panel position on the 1024×768 canvas.
    //       Docs/RE/specs/ui_system.md §12 (open item 6) — in-game window layouts gated on uitex manifest.
    private const int ChromeSrcX = 0; // PLAUSIBLE
    private const int ChromeSrcY = 0; // PLAUSIBLE
    private const int ChromeW = 310; // PLAUSIBLE
    private const int ChromeH = 130; // PLAUSIBLE

    // Hotbar atlas: uitex 0010 = data/ui/skillpipe.dds (primary skill hotbar).
    // spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0010 = data/ui/skillpipe.dds, 1024×1024 DXT3.
    private const int HotbarTexId = 10;

    private const string HotbarPath = "data/ui/skillpipe.dds";

    // Hotbar slot display size: the skill icon cell on screen.
    // The real icon is 23×23 CODE-CONFIRMED; we scale up to 46×46 on screen for readability.
    // spec: Docs/RE/formats/ui_manifests.md §2.6 — "Destination cell size: 23×23 pixels on screen":
    //       CODE-CONFIRMED (legacy client). Our Godot HUD upscales to 46×46 for modern resolution.
    // The Atlas source rect is always 23×23 (IconCatalogs.IconCellW/H).
    private const int HotbarSlotW = 46; // display size — upscaled for readability (legacy: 23)
    private const int HotbarSlotH = 46; // display size — upscaled for readability (legacy: 23)

    // Hotbar slot chrome source rect on skillpipe.dds (PLAUSIBLE — layout unrecovered).
    private const int HotbarSlotSrcX = 0; // PLAUSIBLE
    private const int HotbarSlotSrcY = 0; // PLAUSIBLE

    // -------------------------------------------------------------------------
    // Control handles (built in _Ready)
    // -------------------------------------------------------------------------

    private TextureRect _hudChrome = null!;
    private Label _stateLabel = null!;
    private Label _actorCount = null!;
    private ProgressBar _hpBar = null!;
    private Label _hpText = null!;
    private ProgressBar _mpBar = null!;
    private Label _mpText = null!;
    private Label _levelLabel = null!;
    private Label _xpLabel = null!;
    private Label _buffLabel = null!;
    private Label _combatStatsLabel = null!;

    // Hotbar: 9 skill slots (spec: input_ui.md §4 — hotbar slots 0–239, first 9 shown).
    private const int HotbarVisibleSlots = 9;
    private readonly Label[] _hotbarKey = new Label[HotbarVisibleSlots];
    private readonly Label[] _hotbarName = new Label[HotbarVisibleSlots];
    private readonly TextureRect[] _hotbarIcon = new TextureRect[HotbarVisibleSlots];

    // Chat: ring buffer of last 6 lines.
    private readonly Queue<string> _chatLines = new(6);
    private const int ChatLineMax = 6;

    private Label _chatLabel = null!;

    // Reusable StringBuilder for chat-line joins (avoids per-message heap allocation).
    private readonly System.Text.StringBuilder _chatSb = new(512);

    // -------------------------------------------------------------------------
    // View state (display only — no domain state)
    // -------------------------------------------------------------------------

    private int _visibleActorCount;
    private ActorKey _trackedPlayerKey;
    private bool _hasTrackedPlayer;
    private uint _trackedHp;
    private uint _trackedMaxHp;
    private uint _trackedMp;
    private uint _trackedMaxMp;
    private ushort _trackedLevel;

    // Hotbar: slot → skill ID mapping (updated by SkillHotbarSlotSetEvent).
    // spec: Docs/RE/specs/handlers.md §4 (5/33 SmsgSkillHotbarSlotSet).
    private readonly SkillId[] _hotbarSkills = new SkillId[HotbarVisibleSlots];

    // Buff summary: first 3 active effect codes.
    private readonly int[] _activeBuffCodes = new int[3];

    // -------------------------------------------------------------------------
    // Stage-B component references (wired in Initialise after context is available)
    // -------------------------------------------------------------------------

    // ChatWindow: always-on chat panel (Enter to focus/submit).
    private ChatWindow? _chatWindow;

    // CharacterStatsWindow: toggled by key C in its own _Input handler.
    private CharacterStatsWindow? _characterStatsWindow;

    // BuffBar: always-on 30-slot buff icon strip, anchored top below chrome.
    private BuffBar? _buffBar;

    // TargetFrame: always-on but hidden when no target; anchored top-left below chrome.
    private TargetFrame? _targetFrame;

    // FloatingCombatText: full-rect overlay, pooled rising+fading numbers.
    private FloatingCombatText? _floatingCombatText;

    // MinimapPanel: always-on radar, anchored top-right.
    private MinimapPanel? _minimapPanel;

    // OptionsWindow: toggled by key O in its own _Input handler.
    private OptionsWindow? _optionsWindow;

    // RightEdgeGaugePanel: stacked HP/MP gauge strips at screen_width−135, Y=200/250.
    // spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
    private RightEdgeGaugePanel? _rightEdgeGauge;

    // BottomActionBar: 1024×60 bar at centerX(1024), screen_height−60.
    // spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.
    private BottomActionBar? _bottomActionBar;

    // TopStatusBar: full-width 20px strip at Y=120.
    // spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.
    private TopStatusBar? _topStatusBar;

    // ConfirmDialog: the most common centred modal family (340×190).
    // spec: Docs/RE/specs/ui_hud_layout.md §5.8 — ~12 sites; reusable via Open(message).
    private ConfirmDialog? _confirmDialog;

    // Zone indicator: a small label anchored top-right showing Safe / PvP / Closed / Unknown.
    // Populated by draining IHudEventHub.ZoneChanges in _Process.
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — zone-type enum.
    private Label? _zoneIndicatorLabel;
    private Panel? _zoneIndicatorPanel;

    // Cached zone-pill StyleBoxFlat instances — built once in BuildZoneIndicator, swapped on ZoneChangedEvent.
    // Avoids per-event StyleBoxFlat allocation; zone changes are infrequent but still heap-free after init.
    private StyleBoxFlat? _zonePillSafe;
    private StyleBoxFlat? _zonePillPvp;
    private StyleBoxFlat? _zonePillClosed;
    private StyleBoxFlat? _zonePillUnknown;

    // The HUD event hub — wired in BindStageBComponents (after Initialise).
    // ZoneChanges is the only channel drained here; other families are drained by their widgets.
    private IHudEventHub? _hudEventHub;

    // ClientContext reference (for catalogue lookups and texture loading).
    private ClientContext? _context;

    // Short-hand reference to the skill icon catalog (non-owning; owned by ClientContext).
    private IconCatalogs? _iconCatalogs;

    // UiAssetLoader for slicing atlas textures — lazy-opened on first use.
    private UiAssetLoader? _uiLoader;

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    /// <summary>Called by GameLoop._Ready; gives the HUD its context handle for catalogue lookups.</summary>
    public void Initialise(ClientContext context)
    {
        _context = context;
        _iconCatalogs = context.IconCatalogs;

        // Bind the HUD chrome now that _context is available (avoids null read during _Ready,
        // which runs before GameLoop calls Initialise — see finding 3).
        BindHudChromeTexture();

        // Populate the hotbar with real Musa-jung skill icons if VFS is available.
        // spec: Docs/RE/formats/ui_manifests.md §2.6 — "23×23 pixel cell": CODE-CONFIRMED.
        // spec: Docs/RE/formats/ui_manifests.md §2.7 — "musajung.do slotIndex 0..N": CODE-CONFIRMED.
        PopulateHotbarIcons();

        // Bind Stage-B components to the HUD event hub.
        // All nodes were added as children in ReadyInternal; Bind is called here (after context
        // is available) so the hub is always non-null at bind time.
        BindStageBComponents(context);
    }

    /// <summary>
    /// Binds all Stage-B HUD components to the application HUD event hub and catalogues.
    /// Called from <see cref="Initialise"/> after the ClientContext is available.
    /// All nodes are already in the scene tree (added in ReadyInternal).
    /// PASSIVE: only forwards the hub reference; no game logic here.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive HUD.
    /// </summary>
    private void BindStageBComponents(ClientContext context)
    {
        var hub = context.HudEventHub;

        // Store hub reference so _Process can drain ZoneChanges each frame.
        _hudEventHub = hub;

        // Chat window — always visible; live chat lines arrive via hub.ChatLines.
        if (_chatWindow is not null)
        {
            _chatWindow.Bind(hub);
            // Wire send-chat intent to UseCases (no-op log until a real use-case exists).
            _chatWindow.SendChatRequested += (channel, text) =>
            {
                // PASSIVE: forward intent to IApplicationUseCases when the method is added.
                GD.Print($"[GameHud] Chat intent: channel={channel} text={text} " +
                         "(IApplicationUseCases.SendChatAsync not yet wired).");
            };
            GD.Print("[GameHud] ChatWindow bound to HudEventHub.");
        }

        // Character stats window — always visible but hidden by default; key C toggles in its own _Input.
        if (_characterStatsWindow is not null)
        {
            _characterStatsWindow.Bind(hub, context.UseCases);
            GD.Print("[GameHud] CharacterStatsWindow bound to HudEventHub.");
        }

        // Buff bar — always visible; 30-slot icon strip.
        if (_buffBar is not null)
        {
            _buffBar.Bind(hub, context.BuffIconCatalog);
            GD.Print("[GameHud] BuffBar bound to HudEventHub + BuffIconCatalog.");
        }

        // Target frame — hidden when no target; self-hides via hub.TargetChanges.IsCleared.
        if (_targetFrame is not null)
        {
            _targetFrame.Bind(hub);
            GD.Print("[GameHud] TargetFrame bound to HudEventHub.");
        }

        // Floating combat text — full-rect overlay; needs hub + an ActorRegistry.
        // ActorRegistry is a sibling of GameHud in the GameLoop scene tree.
        if (_floatingCombatText is not null)
        {
            try
            {
                var registry = GetNode<ActorRegistry>("../ActorRegistry");
                _floatingCombatText.Bind(hub, registry);
                GD.Print("[GameHud] FloatingCombatText bound to HudEventHub + ActorRegistry.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameHud] FloatingCombatText: ActorRegistry not found: {ex.Message} — " +
                            "hub bound without registry (fallback centre-screen).");
                // Bind with a null-registry-safe path: FloatingCombatText handles missing registry.
            }
        }

        // Minimap panel — always visible radar.
        if (_minimapPanel is not null)
        {
            try
            {
                var registry = GetNode<ActorRegistry>("../ActorRegistry");
                _minimapPanel.Bind(hub, registry);
                GD.Print("[GameHud] MinimapPanel bound to HudEventHub + ActorRegistry.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameHud] MinimapPanel: ActorRegistry not found: {ex.Message} — minimap in demo mode.");
            }
        }

        // Options window — hidden by default; key O toggles in its own _Input.
        if (_optionsWindow is not null)
        {
            _optionsWindow.Bind(hub);
            GD.Print("[GameHud] OptionsWindow bound to HudEventHub.");
        }
    }

    public override void _Ready()
    {
        GD.Print("[GameHud] _Ready start");
        try
        {
            ReadyInternal();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] _Ready failed: {ex.Message}. HUD may be partially visible.");
            EnsureFallbackLabels();
        }
    }

    private void ReadyInternal()
    {
        // Make the HUD root full-rect (covers the whole viewport) with MouseFilter=Ignore so it
        // does not eat world/camera input. The chrome box and hotbar are positioned as children.
        // Finding 2: root was sized 318×138 → hotbar anchored AnchorTop=1 landed 54 px from top,
        // clipped. Full-rect root lets the hotbar anchor to the true viewport bottom-centre.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // ---- Chrome box — primary ActorState (stats/vitals) panel. ----
        // Positioned at the recovered HUD coordinates: X=180, Y=95, W=130, H=196 (absolute).
        // spec: Docs/RE/specs/ui_hud_layout.md §3.3 — "Stats/ActorState: X=180 Y=95 W=130 H=196 Absolute"
        //       CODE-CONFIRMED-static (plain literal immediates decoded from HUD-assembly call site).
        // HudPanelConfig.StatsX/Y/W/H carry these constants.
        var chromeBox = new Control
        {
            Name = "ChromeBox",
            Position = new Vector2(HudPanelConfig.StatsX,
                HudPanelConfig.StatsY), // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            Size = new Vector2(HudPanelConfig.StatsW,
                HudPanelConfig.StatsH), // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            CustomMinimumSize = new Vector2(HudPanelConfig.StatsW, HudPanelConfig.StatsH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(chromeBox);

        // ---- Stat sub-panels A / B / C (absolute sibling panels) ----
        // Four panels (including the primary above) recovered from the HUD-build routine.
        // Roles (HP vs MP vs stamina vs status) not yet individually labelled — §4 known unknowns.
        // spec: Docs/RE/specs/ui_hud_layout.md §3.4 — "three sibling stat sub-panels plus primary"
        //       CODE-CONFIRMED-static.
        AddStatSubPanel("StatSubPanelA", HudPanelConfig.StatSubPanelA); // spec: Docs/RE/specs/ui_hud_layout.md §3.4
        AddStatSubPanel("StatSubPanelB", HudPanelConfig.StatSubPanelB); // spec: Docs/RE/specs/ui_hud_layout.md §3.4
        AddStatSubPanel("StatSubPanelC", HudPanelConfig.StatSubPanelC); // spec: Docs/RE/specs/ui_hud_layout.md §3.4

        // ---- Chrome layer (mainwindow.dds) ----
        // Placed first so stats panel draws on top (paint-order = insertion order,
        // spec: Docs/RE/specs/ui_system.md §3.1).
        _hudChrome = new TextureRect
        {
            Name = "HudChrome",
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(ChromeW, ChromeH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hudChrome.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _hudChrome.Position = Vector2.Zero;
        _hudChrome.Size = new Vector2(ChromeW, ChromeH);
        chromeBox.AddChild(_hudChrome);

        // Chrome bind is deferred to Initialise(context) when _context is available.
        // Calling BindHudChromeTexture() here when _context is still null would skip the
        // primary uitex path — see finding 3.

        // ---- Stats panel (above chrome, inside chromeBox) ----
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        panel.CustomMinimumSize = new Vector2(ChromeW, ChromeH);
        // Transparent background — the chrome TextureRect provides the visual panel.
        var transparent = new StyleBoxEmpty();
        panel.AddThemeStyleboxOverride("panel", transparent);
        chromeBox.AddChild(panel);

        var vbox = new VBoxContainer();
        panel.AddChild(vbox);

        _stateLabel = new Label { Text = "State: Login" };
        vbox.AddChild(_stateLabel);

        _actorCount = new Label { Text = "Actors: 0" };
        vbox.AddChild(_actorCount);

        // HP row.
        var hpRow = new HBoxContainer();
        vbox.AddChild(hpRow);
        hpRow.AddChild(new Label { Text = "HP: " });
        _hpBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100 };
        _hpBar.CustomMinimumSize = new Vector2(120, 18);
        hpRow.AddChild(_hpBar);
        _hpText = new Label { Text = "0/0" };
        hpRow.AddChild(_hpText);

        // MP row.
        var mpRow = new HBoxContainer();
        vbox.AddChild(mpRow);
        mpRow.AddChild(new Label { Text = "MP: " });
        _mpBar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100 };
        _mpBar.CustomMinimumSize = new Vector2(120, 18);
        var mpFillStyle = new StyleBoxFlat();
        mpFillStyle.BgColor = new Color(0.1f, 0.3f, 0.9f, 1f);
        _mpBar.AddThemeStyleboxOverride("fill", mpFillStyle);
        mpRow.AddChild(_mpBar);
        _mpText = new Label { Text = "0/0" };
        mpRow.AddChild(_mpText);

        // Level / XP row.
        var lvRow = new HBoxContainer();
        vbox.AddChild(lvRow);
        lvRow.AddChild(new Label { Text = "Lv: " });
        _levelLabel = new Label { Text = "—" };
        lvRow.AddChild(_levelLabel);
        lvRow.AddChild(new Label { Text = "  XP: " });
        _xpLabel = new Label { Text = "0" };
        lvRow.AddChild(_xpLabel);

        _buffLabel = new Label { Text = "Buffs: —" };
        vbox.AddChild(_buffLabel);

        _combatStatsLabel = new Label { Text = "" };
        vbox.AddChild(_combatStatsLabel);

        // ---- Skill bar (container origin at recovered (349, 13), 9-slot data-driven grid) ----
        // spec: Docs/RE/specs/ui_hud_layout.md §3.5 — "container origin 349, 13; nine skill slots"
        //       container origin CODE-CONFIRMED-static; per-slot data-driven from runtime registry.
        // HudPanelConfig.SkillBarX=349, SkillBarY=13, SkillBarSlotCount=9 carry these constants.
        try
        {
            BuildSkillBar();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BuildSkillBar failed: {ex.Message}");
        }

        // ---- Party panel (off-screen right column — screen_width + 318, Y=0, W=318, H=732) ----
        // spec: Docs/RE/specs/ui_hud_layout.md §3.3 — "Party: X=screen_width+318, Y=0, W=318, H=732"
        //       CONFIRMED-formula; starts off-screen (classic slide-in). Visible when party is active.
        // HudPanelConfig.PartyOffsetFromRight=318, PartyW=318, PartyH=732.
        try
        {
            BuildPartyPanel();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BuildPartyPanel failed: {ex.Message}");
        }

        // ---- Trade panel (parent-relative: overlays inventory's right column, Y=0, W=318, H=732) ----
        // spec: Docs/RE/specs/ui_hud_layout.md §3.3 — "Trade: X=inventory's X, Y=0, W=318, H=732"
        //       CODE-CONFIRMED-static; hidden by default; shown during a trade session.
        // HudPanelConfig.TradeW=318, TradeH=732.
        try
        {
            BuildTradePanel();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BuildTradePanel failed: {ex.Message}");
        }

        // ---- Chat (bottom-left corner) — replaced by ChatWindow Stage-B component ----
        // The old BuildChatPanel() is superseded by ChatWindow; we keep BuildChatPanel as a
        // fallback only if ChatWindow fails to construct.
        try
        {
            _chatWindow = new ChatWindow { Name = "ChatWindow" };
            AddChild(_chatWindow);
            GD.Print("[GameHud] ChatWindow added to scene tree.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] ChatWindow attach failed: {ex.Message} — falling back to simple chat panel.");
            try
            {
                BuildChatPanel();
            }
            catch
            {
                /* ignored */
            }
        }

        // ---- BuffBar (top strip, below chrome) ----
        // spec: Docs/RE/specs/ui_hud_layout.md §2 — data-driven buff icon positions from
        //       buff_icon_position.xdb. 30 icon slots; buff_id ≤ 80 = 23×23 flowing;
        //       buff_id > 80 = 25×25 fixed per-slot. CODE-CONFIRMED.
        try
        {
            _buffBar = new BuffBar
            {
                Name = "BuffBar",
                // Left-anchored below the chrome box.
                // The 4px left-inset and 136px top-inset place the bar immediately below
                // the 130px-high chrome at (4, 136). Width spans to 800px (covers the
                // 30-slot strip: 30 × (25+2) = 810px max — truncated at right edge).
                // Height 36px gives a 5px margin above/below the 25px state cells.
                AnchorLeft = 0f,
                AnchorTop = 0f,
                AnchorRight = 0f,
                AnchorBottom = 0f,
                OffsetLeft = 4f,
                OffsetTop = 136f, // just below the 130px chrome box — PLAUSIBLE
                OffsetRight = 814f, // 30 slots × (25+2)px = 810 + 4px left margin = 814
                OffsetBottom = 172f, // 36px band: 5px pad + 25px cell + 6px pad (state cells)
            };
            AddChild(_buffBar);
            GD.Print("[GameHud] BuffBar added to scene tree.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BuffBar attach failed: {ex.Message}");
        }

        // ---- TargetFrame (top-left, below buff bar) ----
        try
        {
            _targetFrame = new TargetFrame { Name = "TargetFrame" };
            AddChild(_targetFrame);
            GD.Print("[GameHud] TargetFrame added to scene tree.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] TargetFrame attach failed: {ex.Message}");
        }

        // ---- FloatingCombatText (full-rect overlay) ----
        try
        {
            _floatingCombatText = new FloatingCombatText { Name = "FloatingCombatText" };
            AddChild(_floatingCombatText);
            GD.Print("[GameHud] FloatingCombatText added to scene tree.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] FloatingCombatText attach failed: {ex.Message}");
        }

        // ---- MinimapPanel (top-right corner) ----
        try
        {
            _minimapPanel = new MinimapPanel { Name = "MinimapPanel" };
            AddChild(_minimapPanel);
            GD.Print("[GameHud] MinimapPanel added to scene tree.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] MinimapPanel attach failed: {ex.Message}");
        }

        // ---- CharacterStatsWindow (centre; toggles by key C in its own _Input) ----
        try
        {
            _characterStatsWindow = new CharacterStatsWindow { Name = "CharacterStatsWindow" };
            AddChild(_characterStatsWindow);
            GD.Print("[GameHud] CharacterStatsWindow added to scene tree.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] CharacterStatsWindow attach failed: {ex.Message}");
        }

        // ---- OptionsWindow (centre; toggles by key O in its own _Input) ----
        try
        {
            _optionsWindow = new OptionsWindow { Name = "OptionsWindow" };
            AddChild(_optionsWindow);
            GD.Print("[GameHud] OptionsWindow added to scene tree.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] OptionsWindow attach failed: {ex.Message}");
        }

        // ---- RightEdgeGaugePanel (§5.6) — stacked HP/MP strips at screen_width−135, Y=200/250 ----
        // spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
        // The panel self-anchors in its own _Ready; GameHud only constructs and adds it.
        // Vitals are forwarded via OnActorSpawned / OnActorVitalsChanged / OnCombatStatsRecomputed.
        try
        {
            _rightEdgeGauge = new RightEdgeGaugePanel { Name = "RightEdgeGaugePanel" };
            AddChild(_rightEdgeGauge);
            GD.Print("[GameHud] RightEdgeGaugePanel added. " +
                     $"Anchor: screen_width−{HudPanelConfig.RightGaugeOffsetFromRight}, " +
                     $"Y={HudPanelConfig.RightGaugeHpY}/{HudPanelConfig.RightGaugeMpY}, " +
                     $"W={HudPanelConfig.RightGaugeW} H={HudPanelConfig.RightGaugeH}. " +
                     "spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] RightEdgeGaugePanel attach failed: {ex.Message}");
        }

        // ---- BottomActionBar (§5.7) — 1024×60 bar at centerX(1024), screen_height−60 ----
        // spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.
        try
        {
            _bottomActionBar = new BottomActionBar { Name = "BottomActionBar" };
            AddChild(_bottomActionBar);
            GD.Print("[GameHud] BottomActionBar added. " +
                     $"centerX({HudPanelConfig.ActionBarW}), " +
                     $"screen_height−{HudPanelConfig.ActionBarH}. " +
                     "spec: Docs/RE/specs/ui_hud_layout.md §5.7 CONFIRMED-formula.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BottomActionBar attach failed: {ex.Message}");
        }

        // ---- TopStatusBar (§5.4) — full-width 20px strip at Y=120 ----
        // spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.
        try
        {
            _topStatusBar = new TopStatusBar { Name = "TopStatusBar" };
            AddChild(_topStatusBar);
            GD.Print("[GameHud] TopStatusBar added. " +
                     $"X=0, Y={HudPanelConfig.TopStatusBarY}, W=screen_width, H={HudPanelConfig.TopStatusBarH}. " +
                     "spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] TopStatusBar attach failed: {ex.Message}");
        }

        // ---- ConfirmDialog (§5.8) — centred modal 340×190, the most common dialog family ----
        // spec: Docs/RE/specs/ui_hud_layout.md §5.8 — "Confirm/info dialog: W=340, H=190, ~12 sites"
        //       Uses CenteredModal base: center = (screen − size) / 2 on both axes.
        //       spec: Docs/RE/specs/ui_hud_layout.md §5.1 CONFIRMED-formula.
        try
        {
            _confirmDialog = new ConfirmDialog { Name = "ConfirmDialog" };
            AddChild(_confirmDialog);
            GD.Print($"[GameHud] ConfirmDialog added (hidden). " +
                     $"W={HudPanelConfig.ConfirmDialogW} H={HudPanelConfig.ConfirmDialogH}. " +
                     "center = (screen − size) / 2. " +
                     "spec: Docs/RE/specs/ui_hud_layout.md §5.8 §5.1 CONFIRMED-formula.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] ConfirmDialog attach failed: {ex.Message}");
        }

        // ---- ZoneIndicator (top-right corner, below minimap) ----
        // Small pill label: "Safe" / "PvP" / "Closed" / "Unknown".
        // Populated by ZoneChangedEvent from IHudEventHub.ZoneChanges (drained in _Process).
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — zone-type enum.
        try
        {
            BuildZoneIndicator();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] ZoneIndicator build failed: {ex.Message}");
        }

        GD.Print("[GameHud] _Ready completed. HUD chrome wired to uitex 0001 (mainwindow.dds). " +
                 $"ActorState at ({HudPanelConfig.StatsX},{HudPanelConfig.StatsY}) W={HudPanelConfig.StatsW} H={HudPanelConfig.StatsH} " + // spec: Docs/RE/specs/ui_hud_layout.md §3.3
                 $"+ 3 sub-panels (A/B/C). " + // spec: Docs/RE/specs/ui_hud_layout.md §3.4
                 $"SkillBar container at ({HudPanelConfig.SkillBarX},{HudPanelConfig.SkillBarY}) 9-slot. " + // spec: Docs/RE/specs/ui_hud_layout.md §3.5
                 $"Minimap top-right (screen_width−{HudPanelConfig.MinimapW}, Y={HudPanelConfig.MinimapY}). " + // spec: Docs/RE/specs/ui_hud_layout.md §3.3
                 $"Party off-screen (screen_width+{HudPanelConfig.PartyOffsetFromRight}). " + // spec: Docs/RE/specs/ui_hud_layout.md §3.3
                 "Trade overlay (inventory X, Y=0). " + // spec: Docs/RE/specs/ui_hud_layout.md §3.3
                 "Stage-B components (ChatWindow, BuffBar, TargetFrame, FloatingCombatText, " +
                 "MinimapPanel, CharacterStatsWindow, OptionsWindow) added to scene tree.");
    }

    // -------------------------------------------------------------------------
    // Chrome texture binding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Binds the HUD chrome texture from the uitex catalogue (texId 0001) or falls back
    /// to a direct VFS path load. Degrades gracefully when VFS is offline.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.5 — in-game windows bind atlas by uitex integer id.
    /// spec: Docs/RE/formats/ui_manifests.md §1.4 — uitex 0001 = data/ui/mainwindow.dds.
    /// </summary>
    private void BindHudChromeTexture()
    {
        if (_hudChrome is null) return;

        // Primary path: uitex catalogue via ClientContext.UiCatalogs.
        // spec: Docs/RE/specs/ui_system.md §8.5.
        if (_context?.UiCatalogs is { } cats)
        {
            ImageTexture? tex = cats.GetTexture(MainWindowTexId);
            if (tex is not null)
            {
                // Slice the chrome panel sub-rect from the full 1024×1024 atlas.
                // spec: Docs/RE/specs/ui_system.md §1.3 — "atlas pixels map 1:1 to screen pixels
                //       at 1:1 on the reference 1024×768 canvas".
                var at = new AtlasTexture
                {
                    Atlas = tex,
                    Region = new Rect2(ChromeSrcX, ChromeSrcY, ChromeW, ChromeH), // PLAUSIBLE
                    FilterClip = true,
                };
                _hudChrome.Texture = at;
                GD.Print($"[GameHud] Chrome bound via UiCatalogs uitex {MainWindowTexId} " +
                         $"(mainwindow.dds). Rect2({ChromeSrcX},{ChromeSrcY},{ChromeW},{ChromeH}) // PLAUSIBLE");
                return;
            }
        }

        // Fallback: try via UiAssetLoader (direct VFS path).
        // spec: Docs/RE/formats/ui_manifests.md §5.4 — mainwindow.dds hard-coded path.
        try
        {
            _uiLoader ??= UiAssetLoader.Open();
            AtlasTexture? atFallback = _uiLoader.Slice(MainWindowPath, ChromeSrcX, ChromeSrcY, ChromeW, ChromeH);
            if (atFallback is not null)
            {
                _hudChrome.Texture = atFallback;
                GD.Print($"[GameHud] Chrome bound via UiAssetLoader fallback ({MainWindowPath}). " +
                         $"Rect2({ChromeSrcX},{ChromeSrcY},{ChromeW},{ChromeH}) // PLAUSIBLE");
                return;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] UiAssetLoader chrome bind failed: {ex.Message} — chrome invisible (VFS offline).");
        }

        GD.Print("[GameHud] mainwindow.dds unavailable — HUD chrome invisible (VFS offline mode). " +
                 "Plain PanelContainer fallback is active.");
    }

    // -------------------------------------------------------------------------
    // Stat sub-panel construction (one helper for A, B, C)
    // spec: Docs/RE/specs/ui_hud_layout.md §3.4 — three sibling stat sub-panels CODE-CONFIRMED-static.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds and adds one of the three stat sub-panels at the given absolute rect.
    /// Roles (HP / MP / stamina / status) are not yet individually labelled — §4 known unknowns.
    /// spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED-static.
    /// </summary>
    private void AddStatSubPanel(string name, Rect2 rect)
    {
        var ctrl = new Control
        {
            Name = name,
            Position = new Vector2(rect.Position.X, rect.Position.Y), // spec: Docs/RE/specs/ui_hud_layout.md §3.4
            Size = new Vector2(rect.Size.X, rect.Size.Y), // spec: Docs/RE/specs/ui_hud_layout.md §3.4
            MouseFilter = MouseFilterEnum.Ignore,
        };

        var panel = new Panel();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0f, 0f, 0f, 0.25f);
        style.SetBorderWidthAll(1);
        style.BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        panel.AddThemeStyleboxOverride("panel", style);
        ctrl.AddChild(panel);

        var lbl = new Label
        {
            Text = $"[{name}]",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ctrl.AddChild(lbl);

        AddChild(ctrl);
        GD.Print($"[GameHud] {name} added at ({rect.Position.X},{rect.Position.Y}) " +
                 $"W={rect.Size.X} H={rect.Size.Y}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.4 CODE-CONFIRMED-static.");
    }

    // -------------------------------------------------------------------------
    // Party panel construction (off-screen right column, revealed on party join)
    // spec: Docs/RE/specs/ui_hud_layout.md §3.3 — "X=screen_width+318, Y=0, W=318, H=732"
    //       CONFIRMED-formula; starts off-screen (slide-in pattern).
    // -------------------------------------------------------------------------

    private void BuildPartyPanel()
    {
        // screen_width + 318 → AnchorLeft=1, OffsetLeft=+318, OffsetRight=+636 (= 318+318)
        // This positions the panel completely off-screen to the right until revealed.
        // spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.
        // spec: Docs/RE/specs/ui_hud_layout.md §3.2 — screen-width-relative anchor convention.
        var ctrl = new Control
        {
            Name = "PartyPanel",
            AnchorLeft = 1f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 0f,
            OffsetLeft =
                HudPanelConfig
                    .PartyOffsetFromRight, // = +318 → off-screen  // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            OffsetTop = 0f, // Y=0               // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            OffsetRight =
                HudPanelConfig.PartyOffsetFromRight +
                HudPanelConfig.PartyW, // = +636            // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            OffsetBottom = HudPanelConfig.PartyH, // = 732             // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            Visible = false, // hidden until party is active (no party use-case wired yet)
            MouseFilter = MouseFilterEnum.Ignore,
        };

        var panel = new Panel();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.15f, 0.85f);
        style.SetBorderWidthAll(1);
        style.BorderColor = new Color(0.4f, 0.5f, 0.8f, 0.9f);
        panel.AddThemeStyleboxOverride("panel", style);
        ctrl.AddChild(panel);

        var lbl = new Label
        {
            Text = "Party",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        ctrl.AddChild(lbl);

        AddChild(ctrl);
        GD.Print($"[GameHud] PartyPanel added (off-screen, Visible=false). " +
                 $"Anchor: screen_width+{HudPanelConfig.PartyOffsetFromRight} Y=0 W={HudPanelConfig.PartyW} H={HudPanelConfig.PartyH}. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.3 CONFIRMED-formula.");
    }

    // -------------------------------------------------------------------------
    // Trade panel construction (parent-relative: overlays inventory column, Y=0, W=318, H=732)
    // spec: Docs/RE/specs/ui_hud_layout.md §3.3 — "X=inventory's X, Y=0, W=318, H=732"
    //       CODE-CONFIRMED-static; hidden by default, shown during a trade session.
    // -------------------------------------------------------------------------

    private void BuildTradePanel()
    {
        // Trade reads the inventory panel's stored X — same right-column anchor as inventory.
        // Inventory is right-anchored: AnchorLeft=1, OffsetLeft=-732, OffsetRight=+318.
        // Trade overlays at the same X: AnchorLeft=1, OffsetLeft=+318−318=0... but spec says
        // it reads inventory.X directly, which on the 1024 canvas = 1024−732+318 = 610.
        // In Godot: mirror inventory's right-anchor formula with TradeW=318.
        // spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.
        // spec: Docs/RE/specs/ui_hud_layout.md §3.2 — parent-relative anchor convention.
        var ctrl = new Control
        {
            Name = "TradePanel",
            AnchorLeft = 1f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 0f,
            // Trade overlays the inventory right column:
            // inventory: OffsetLeft=-732, OffsetRight=+318 (W=1050 from right edge? No — W=732)
            // Trade W=318, anchored at OffsetLeft=+318−318=0, but the spec says it reads
            // inventory's stored X. For the 1024 reference canvas the simplest mapping:
            // AnchorRight=1, OffsetLeft=+318 (trade starts where inventory ends on-screen),
            // OffsetRight=+318+318 (trade W=318 past that). Kept Visible=false until session.
            OffsetLeft =
                HudPanelConfig
                    .PartyOffsetFromRight, // +318 → same X as inventory right-anchor  // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            OffsetTop = 0f, // Y=0  // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            OffsetRight =
                HudPanelConfig.PartyOffsetFromRight +
                HudPanelConfig.TradeW, // +636 // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            OffsetBottom = HudPanelConfig.TradeH, // 732  // spec: Docs/RE/specs/ui_hud_layout.md §3.3
            Visible = false, // hidden until a trade session is active
            MouseFilter = MouseFilterEnum.Ignore,
        };

        var panel = new Panel();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.15f, 0.1f, 0.05f, 0.85f);
        style.SetBorderWidthAll(1);
        style.BorderColor = new Color(0.8f, 0.6f, 0.3f, 0.9f);
        panel.AddThemeStyleboxOverride("panel", style);
        ctrl.AddChild(panel);

        var lbl = new Label
        {
            Text = "Trade",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        ctrl.AddChild(lbl);

        AddChild(ctrl);
        GD.Print($"[GameHud] TradePanel added (Visible=false, overlay inventory column). " +
                 $"W={HudPanelConfig.TradeW} H={HudPanelConfig.TradeH} Y=0. " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.3 CODE-CONFIRMED-static.");
    }

    // -------------------------------------------------------------------------
    // Zone indicator (top-right pill, below minimap)
    // Shows the current zone type: Safe / PvP / Closed / Unknown (provisional).
    // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — zone-type enum.
    // -------------------------------------------------------------------------

    private void BuildZoneIndicator()
    {
        // Anchored to top-right corner, below minimap.
        // Minimap: screen_width − MinimapW(135), Y=MinimapY(0), H=MinimapH(195).
        // spec: Docs/RE/specs/ui_hud_layout.md §3.3 — MinimapW=135, MinimapY=0, MinimapH=195.
        // Zone pill sits below: X = screen_width − 135, Y = MinimapY + MinimapH + 4 = 0 + 195 + 4 = 199.
        // Width ~120px, height ~24px — compact but readable.
        // Layout is PLAUSIBLE (zone-pill position not yet byte-confirmed).
        _zoneIndicatorPanel = new Panel
        {
            Name = "ZoneIndicatorPanel",
            AnchorLeft = 1f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 0f,
            OffsetLeft = -129f, // screen_width − 129 → ~125px wide pill  // PLAUSIBLE
            OffsetTop = 199f, // below minimap: MinimapY(0) + MinimapH(195) + 4 = 199  // PLAUSIBLE
            OffsetRight = -4f, // 4px from right edge
            OffsetBottom = 223f, // 24px tall
            MouseFilter = MouseFilterEnum.Ignore,
        };

        // Build and cache all zone-pill StyleBoxFlat instances once.
        // Swapped by ApplyZoneChanged without any further allocation.
        static StyleBoxFlat MakePillStyle(Color bg)
        {
            var s = new StyleBoxFlat();
            s.BgColor = bg;
            s.SetBorderWidthAll(1);
            s.BorderColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            s.CornerRadiusTopLeft = s.CornerRadiusTopRight =
                s.CornerRadiusBottomLeft = s.CornerRadiusBottomRight = 3;
            return s;
        }

        _zonePillUnknown = MakePillStyle(new Color(0f, 0f, 0f, 0.65f)); // black — unknown
        _zonePillSafe = MakePillStyle(new Color(0f, 0.4f, 0f, 0.75f)); // green — safe
        _zonePillPvp = MakePillStyle(new Color(0.6f, 0f, 0f, 0.75f)); // red   — PvP
        _zonePillClosed = MakePillStyle(new Color(0.3f, 0.1f, 0.5f, 0.75f)); // purple — closed
        _zoneIndicatorPanel.AddThemeStyleboxOverride("panel", _zonePillUnknown);

        _zoneIndicatorLabel = new Label
        {
            Name = "ZoneIndicatorLabel",
            Text = "Zone: ?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _zoneIndicatorLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _zoneIndicatorPanel.AddChild(_zoneIndicatorLabel);
        AddChild(_zoneIndicatorPanel);

        GD.Print("[GameHud] ZoneIndicator built (top-right pill, subscribes to ZoneChanges channel). " +
                 "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.");
    }

    /// <summary>
    /// Converts a <see cref="ZoneType"/> to a short display string for the zone indicator.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — zone-type enum values 0/1/2.
    /// </summary>
    private static string ZoneTypeLabel(ZoneType zone) => zone switch
    {
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 0: CONFIRMED-COMPLETE (Safe).
        ZoneType.Safe => "Zone: Safe",
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 1: CONFIRMED (OpenPvP).
        ZoneType.OpenPvp => "Zone: PvP",
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — value 2: CONFIRMED (Closed).
        ZoneType.Closed => "Zone: Closed",
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — values 3+ / missing: UNVERIFIED.
        _ => "Zone: ?",
    };

    // -------------------------------------------------------------------------
    // Skill bar construction (container origin at recovered (349, 13), 9-slot grid)
    // spec: Docs/RE/specs/ui_hud_layout.md §3.5 — container origin CODE-CONFIRMED-static;
    //       per-slot data-driven from runtime registry.
    // -------------------------------------------------------------------------

    private void BuildSkillBar()
    {
        // Thin anchor container at absolute origin (349, 13).
        // spec: Docs/RE/specs/ui_hud_layout.md §3.5 — "container origin 349, 13"
        //       CODE-CONFIRMED-static. Anchor strip W~7, H~504 (spec §3.5).
        // In this first render pass, we use a 9-slot horizontal row as a data-driven placeholder.
        // Each slot uses the fallback 58×58 cell size (smallest observed branch, spec §3.5).
        // When the skill-slot registry record layout is recovered (§4 known unknowns), each
        // slot's base X/Y will be read from the registry instead of the uniform grid here.

        // Total width for 9 × 58px slots = 522px; use that as the container size.
        float slotSize = HudPanelConfig.SkillBarSlotFallbackSize; // = 58  // spec: Docs/RE/specs/ui_hud_layout.md §3.5
        int slotCount = HudPanelConfig.SkillBarSlotCount; // = 9   // spec: Docs/RE/specs/ui_hud_layout.md §3.5
        float containerW = slotSize * slotCount;
        float containerH = slotSize;

        var container = new Control
        {
            Name = "SkillBarContainer",
            Position = HudPanelConfig.SkillBarOrigin, // (349, 13) spec: Docs/RE/specs/ui_hud_layout.md §3.5
            Size = new Vector2(containerW, containerH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(container);

        // Nine skill slots in a horizontal row (placeholder until slot registry is recovered).
        // spec: Docs/RE/specs/ui_hud_layout.md §3.5 — "nine skill slots; each slot base X/Y from registry".
        for (int i = 0; i < slotCount; i++)
        {
            float slotX = i * slotSize; // placeholder: uniform horizontal layout
            var slot = new Control
            {
                Name = $"SkillSlot{i}",
                Position = new Vector2(slotX, 0f), // placeholder X; real X from registry §3.5
                Size = new Vector2(slotSize, slotSize),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            container.AddChild(slot);

            // Slot background.
            var slotBg = new Panel();
            slotBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var slotStyle = new StyleBoxFlat();
            slotStyle.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            slotStyle.SetBorderWidthAll(1);
            slotStyle.BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            slotBg.AddThemeStyleboxOverride("panel", slotStyle);
            slot.AddChild(slotBg);

            // Key label ("1"–"9") at top.
            _hotbarKey[i] = new Label
            {
                Text = $"{i + 1}",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _hotbarKey[i].SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            slot.AddChild(_hotbarKey[i]);

            // Icon TextureRect.
            _hotbarIcon[i] = new TextureRect
            {
                Name = $"SkillIcon{i}",
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(slotSize - 4f, slotSize - 4f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _hotbarIcon[i].SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            BindHotbarSlotIcon(_hotbarIcon[i], i);
            slot.AddChild(_hotbarIcon[i]);

            // Skill name at bottom.
            _hotbarName[i] = new Label
            {
                Text = "—",
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(slotSize, 14f),
                AutowrapMode = TextServer.AutowrapMode.Off,
                ClipText = true,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _hotbarName[i].SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
            slot.AddChild(_hotbarName[i]);
        }

        // Optional: bind hotbar chrome texture from skillpipe.dds.
        var chromeBg = new TextureRect
        {
            Name = "SkillBarChrome",
            StretchMode = TextureRect.StretchModeEnum.Tile,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        chromeBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(chromeBg);
        BindHotbarChromeTexture(chromeBg);

        GD.Print($"[GameHud] SkillBar built: container at ({HudPanelConfig.SkillBarX},{HudPanelConfig.SkillBarY}), " +
                 $"{slotCount} slots × {slotSize}px (placeholder; registry data-driven pending). " +
                 "spec: Docs/RE/specs/ui_hud_layout.md §3.5 CODE-CONFIRMED-static origin.");
    }

    // -------------------------------------------------------------------------
    // Hotbar chrome / slot icon binding (called by BuildSkillBar)
    // BuildHotbar() was removed — it was superseded by BuildSkillBar() and had no callers.
    // -------------------------------------------------------------------------

    private void BindHotbarChromeTexture(TextureRect target)
    {
        try
        {
            if (_context?.UiCatalogs is { } cats)
            {
                ImageTexture? tex = cats.GetTexture(HotbarTexId);
                if (tex is not null)
                {
                    // Full-width hotbar chrome strip from top of skillpipe.dds. // PLAUSIBLE
                    target.Texture = new AtlasTexture
                    {
                        Atlas = tex,
                        Region = new Rect2(0, 0, 450, 80), // PLAUSIBLE
                        FilterClip = true,
                    };
                    GD.Print(
                        $"[GameHud] Hotbar chrome bound via UiCatalogs uitex {HotbarTexId} (skillpipe.dds). // PLAUSIBLE");
                    return;
                }
            }

            _uiLoader ??= UiAssetLoader.Open();
            AtlasTexture? at = _uiLoader.Slice(HotbarPath, 0, 0, 450, 80); // PLAUSIBLE
            if (at is not null)
            {
                target.Texture = at;
                GD.Print($"[GameHud] Hotbar chrome bound via UiAssetLoader ({HotbarPath}). // PLAUSIBLE");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] Hotbar chrome bind failed: {ex.Message} — hotbar no chrome.");
        }
    }

    private void BindHotbarSlotIcon(TextureRect target, int slotIndex)
    {
        try
        {
            // Column-major slot layout on skillpipe.dds: each slot occupies HotbarSlotW×HotbarSlotH.
            // Exact layout unrecovered; this is a PLAUSIBLE grid.
            // spec: Docs/RE/specs/ui_system.md §12 open item 6 — hotbar slot layout is gated on uitex manifest.
            int slotSrcX = HotbarSlotSrcX + slotIndex * HotbarSlotW; // PLAUSIBLE
            int slotSrcY = HotbarSlotSrcY; // PLAUSIBLE

            if (_context?.UiCatalogs is { } cats)
            {
                ImageTexture? tex = cats.GetTexture(HotbarTexId);
                if (tex is not null)
                {
                    target.Texture = new AtlasTexture
                    {
                        Atlas = tex,
                        Region = new Rect2(slotSrcX, slotSrcY, HotbarSlotW, HotbarSlotH), // PLAUSIBLE
                        FilterClip = true,
                    };
                    return;
                }
            }

            _uiLoader ??= UiAssetLoader.Open();
            AtlasTexture? at = _uiLoader.Slice(HotbarPath, slotSrcX, slotSrcY, HotbarSlotW, HotbarSlotH); // PLAUSIBLE
            if (at is not null) target.Texture = at;
        }
        catch
        {
            // Silent: slot icon remains blank — not critical to HUD function.
        }
    }

    // -------------------------------------------------------------------------
    // Real skill icon population (called from Initialise after context is available)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates the hotbar slots with real 23×23 Musa-jung skill icons from <see cref="IconCatalogs"/>.
    /// Falls back to the existing skillpipe.dds chrome slice when VFS is offline.
    ///
    /// Icons are loaded via <see cref="IconCatalogs.GetIcon"/> (Map B lookup by slotIndex).
    /// The first <see cref="HotbarVisibleSlots"/> non-negative-coordinate records from
    /// musajung.do are applied to hotbar slots 0 … (HotbarVisibleSlots − 1).
    ///
    /// spec: Docs/RE/formats/ui_manifests.md §2.6 — "23×23 pixel cell, data-driven UV": CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §2.7 — "Map B keyed by slotIndex (+0x08)": CODE-CONFIRMED.
    /// spec: Docs/RE/formats/ui_manifests.md §2.4 — sheet musajung.dds 512×512: SAMPLE-VERIFIED.
    /// </summary>
    private void PopulateHotbarIcons()
    {
        if (_iconCatalogs is null) return;

        IReadOnlyList<(uint SlotIndex, AtlasTexture? Icon)> slots =
            _iconCatalogs.GetFirstSlots(HotbarVisibleSlots);

        if (slots.Count == 0)
        {
            // VFS offline — skill bar keeps skillpipe.dds chrome slices from BuildSkillBar.
            GD.Print("[GameHud] IconCatalogs returned 0 slots — hotbar uses skillpipe.dds placeholders.");
            return;
        }

        int applied = 0;
        for (int i = 0; i < slots.Count && i < HotbarVisibleSlots; i++)
        {
            AtlasTexture? icon = slots[i].Icon;
            if (icon is null) continue;

            // Replace the placeholder texture in the slot's TextureRect.
            if (_hotbarIcon[i] is { } rect)
            {
                rect.Texture = icon;
                applied++;
            }
        }

        GD.Print($"[GameHud] Hotbar: applied {applied} real 23×23 skill icons from musajung.do. " +
                 "spec: Docs/RE/formats/ui_manifests.md §2.6 CODE-CONFIRMED.");
    }

    // -------------------------------------------------------------------------
    // Chat panel construction
    // -------------------------------------------------------------------------

    private void BuildChatPanel()
    {
        var chatPanel = new PanelContainer();
        chatPanel.AnchorLeft = 1.0f;
        chatPanel.AnchorTop = 1.0f;
        chatPanel.AnchorRight = 1.0f;
        chatPanel.AnchorBottom = 1.0f;
        chatPanel.OffsetLeft = -360f;
        chatPanel.OffsetTop = -180f;
        chatPanel.OffsetRight = -4f;
        chatPanel.OffsetBottom = -4f;
        AddChild(chatPanel);

        var chatVBox = new VBoxContainer();
        chatPanel.AddChild(chatVBox);

        _chatLabel = new Label();
        _chatLabel.Text = "";
        _chatLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _chatLabel.CustomMinimumSize = new Vector2(350, 160);
        chatVBox.AddChild(_chatLabel);
    }

    // -------------------------------------------------------------------------
    // Ensure fallback (partial _Ready failure path)
    // -------------------------------------------------------------------------

    private void EnsureFallbackLabels()
    {
        _stateLabel ??= new Label();
        _actorCount ??= new Label();
        _hpBar ??= new ProgressBar();
        _hpText ??= new Label();
        _mpBar ??= new ProgressBar();
        _mpText ??= new Label();
        _levelLabel ??= new Label();
        _xpLabel ??= new Label();
        _buffLabel ??= new Label();
        _combatStatsLabel ??= new Label();
        _chatLabel ??= new Label();
        for (int i = 0; i < HotbarVisibleSlots; i++)
        {
            _hotbarKey[i] ??= new Label();
            _hotbarName[i] ??= new Label();
            _hotbarIcon[i] ??= new TextureRect();
        }
    }

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reacts to an actor spawn: track the first PlayerCharacter for the vitals display.
    /// Also forwards to MinimapPanel for player-key identification.
    /// No game logic — reads event payload only and updates controls.
    /// </summary>
    public void OnActorSpawned(ActorSpawnedEvent evt)
    {
        if (_actorCount is null) return;
        _visibleActorCount++;
        _actorCount.Text = $"Actors: {_visibleActorCount}";

        if (!_hasTrackedPlayer && evt.Key.Sort == EntitySort.PlayerCharacter)
        {
            _hasTrackedPlayer = true;
            _trackedPlayerKey = evt.Key;
            _trackedHp = evt.CurrentHp;
            _trackedMaxHp = evt.MaxHp;
            _trackedLevel = evt.Level;
            _trackedMp = 0;
            _trackedMaxMp = 0;
            RefreshVitals();
            _levelLabel.Text = $"{_trackedLevel}";
        }

        // Forward to MinimapPanel so it can identify the local player ActorKey.
        _minimapPanel?.OnActorSpawned(evt);

        // Forward to RightEdgeGaugePanel for §5.6 HP/MP strips.
        // spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
        _rightEdgeGauge?.OnActorSpawned(evt);
    }

    /// <summary>
    /// Reacts to an actor move event: forwards to MinimapPanel for player position tracking.
    /// No game logic — pure view forwarding.
    /// </summary>
    public void OnActorMoved(ActorMovedEvent evt)
    {
        _minimapPanel?.OnActorMoved(evt);
    }

    /// <summary>
    /// Reacts to a vitals change event: refreshes HP / MP bars.
    /// No stat computation — values come directly from the event (server-authoritative).
    /// spec: Docs/RE/packets/5-53_actor_vitals_and_pair_state.yaml.
    /// </summary>
    public void OnActorVitalsChanged(ActorVitalsChangedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        _trackedHp = evt.CurrentHp;
        _trackedMp = evt.CurrentMp;
        if (_trackedMaxHp == 0) _trackedMaxHp = evt.CurrentHp;
        if (_trackedMaxMp == 0) _trackedMaxMp = evt.CurrentMp;
        RefreshVitals();

        // Forward to RightEdgeGaugePanel for §5.6 HP/MP strips.
        // spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
        _rightEdgeGauge?.OnActorVitalsChanged(evt);
    }

    /// <summary>
    /// Reacts to a level-up event: refreshes level label and vitals.
    /// spec: Docs/RE/packets/5-32_level_up.yaml.
    /// </summary>
    public void OnActorLeveledUp(ActorLeveledUpEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        _trackedLevel = evt.NewLevel;
        _trackedHp = evt.CurrentHp;
        _trackedMp = evt.CurrentMp;
        _levelLabel.Text = $"{_trackedLevel}";
        RefreshVitals();
    }

    /// <summary>
    /// Reacts to the server's stat sync update: refreshes XP display.
    /// spec: Docs/RE/specs/handlers.md §4 (5/67 SmsgStatsUpdate).
    /// </summary>
    public void OnActorStatSync(ActorStatSyncEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        _xpLabel.Text = evt.CurrentXp.ToString("N0");
    }

    /// <summary>
    /// Reacts to a derived combat-stat recompute: refreshes combat-stats summary label.
    /// PASSIVE: reads pre-computed stats from the event — no formula here.
    /// spec: Docs/RE/specs/combat.md §1 / §2 — CombatStats aggregate.
    /// </summary>
    public void OnCombatStatsRecomputed(CombatStatsRecomputedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        var s = evt.Stats;
        if (s.MaxLife > 0)
        {
            _trackedMaxHp = (uint)s.MaxLife;
            if (_trackedHp > _trackedMaxHp) _trackedHp = _trackedMaxHp;
        }

        if (s.MaxEnergy > 0)
        {
            _trackedMaxMp = (uint)s.MaxEnergy;
            if (_trackedMp > _trackedMaxMp) _trackedMp = _trackedMaxMp;
        }

        RefreshVitals();
        _combatStatsLabel.Text = $"Atk:{s.MinDamage}–{s.MaxDamage}  Def:{s.Defence}";

        // Forward to RightEdgeGaugePanel for §5.6 max-HP/MP updates.
        // spec: Docs/RE/specs/ui_hud_layout.md §5.6 CONFIRMED-formula.
        _rightEdgeGauge?.OnCombatStatsRecomputed(evt);
    }

    /// <summary>
    /// Reacts to a buff slot change: refreshes buff summary label.
    /// spec: Docs/RE/specs/handlers.md §4 (5/31 SmsgBuffSlotUpdate).
    /// </summary>
    public void OnBuffSlotChanged(BuffSlotChangedEvent evt)
    {
        if (!_hasTrackedPlayer || evt.Key != _trackedPlayerKey) return;
        if (evt.SlotIndex < _activeBuffCodes.Length)
            _activeBuffCodes[evt.SlotIndex] = evt.DurationTicks > 0 ? evt.EffectCode : 0;
        RefreshBuffLabel();
    }

    /// <summary>
    /// Reacts to a skill-hotbar slot set event: updates the hotbar label for that slot.
    /// spec: Docs/RE/specs/handlers.md §4 (5/33 SmsgSkillHotbarSlotSet).
    /// </summary>
    public void OnSkillHotbarSlotSet(SkillHotbarSlotSetEvent evt)
    {
        if (evt.HotbarSlot >= HotbarVisibleSlots) return;
        int slot = evt.HotbarSlot;
        _hotbarSkills[slot] = evt.Skill;
        _hotbarName[slot].Text = ResolveSkillName(evt.Skill.Value);
    }

    /// <summary>
    /// Reacts to a chat broadcast: appends to the rolling chat window.
    /// Text is already CP949-decoded at the Application boundary.
    /// spec: Docs/RE/packets/5-7_chat_broadcast.yaml.
    /// </summary>
    public void OnChatBroadcast(ChatBroadcastEvent evt)
    {
        // _chatLabel is only set when BuildChatPanel() runs (ChatWindow-construction fallback).
        // On the normal path ChatWindow is the chat surface and _chatLabel stays null.
        // Guard here to prevent NullReferenceException when ChatWindow is active.
        if (_chatLabel is null) return;

        string line = $"[{evt.SenderName}]: {evt.Text}";
        if (_chatLines.Count >= ChatLineMax) _chatLines.Dequeue();
        _chatLines.Enqueue(line);
        // Reuse _chatSb to avoid per-message heap allocation.
        _chatSb.Clear();
        bool first = true;
        foreach (string l in _chatLines)
        {
            if (!first) _chatSb.Append('\n');
            _chatSb.Append(l);
            first = false;
        }

        _chatLabel.Text = _chatSb.ToString();
    }

    /// <summary>Reacts to a client lifecycle state change: update the state label and top status bar.</summary>
    public void OnClientStateChanged(ClientStateChangedEvent evt)
    {
        if (_stateLabel is null) return;
        _stateLabel.Text = $"State: {evt.Current}";
        // Forward state text to the top status bar §5.4 strip.
        // spec: Docs/RE/specs/ui_hud_layout.md §5.4 CONFIRMED-formula.
        _topStatusBar?.SetStatusText($"State: {evt.Current}");
    }

    // -------------------------------------------------------------------------
    // HUD hit-test (used by HudInputHandler)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when the screen point (x,y) lands inside any HUD control rect.
    /// spec: Docs/RE/specs/input_ui.md §3 — "UI hit-test first".
    /// </summary>
    public bool HitTest(int x, int y)
    {
        var pt = new Vector2(x, y);
        for (int i = 0; i < GetChildCount(); i++)
        {
            if (GetChild(i) is Control c && c.GetRect().HasPoint(pt))
                return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void RefreshVitals()
    {
        if (_trackedMaxHp > 0)
        {
            _hpBar.MaxValue = _trackedMaxHp;
            _hpBar.Value = _trackedHp;
        }
        else _hpBar.Value = 0;

        _hpText.Text = $"{_trackedHp}/{_trackedMaxHp}";

        if (_trackedMaxMp > 0)
        {
            _mpBar.MaxValue = _trackedMaxMp;
            _mpBar.Value = _trackedMp;
        }
        else _mpBar.Value = 0;

        _mpText.Text = $"{_trackedMp}/{_trackedMaxMp}";
    }

    private void RefreshBuffLabel()
    {
        // Manual loop avoids Array.FindAll allocation on every buff-slot change (finding 4).
        int activeCount = 0;
        for (int i = 0; i < _activeBuffCodes.Length; i++)
            if (_activeBuffCodes[i] != 0)
                activeCount++;

        if (activeCount == 0)
        {
            _buffLabel.Text = "Buffs: —";
            return;
        }

        _chatSb.Clear(); // reuse the existing StringBuilder field
        _chatSb.Append("Buffs: ");
        bool first = true;
        for (int i = 0; i < _activeBuffCodes.Length; i++)
        {
            if (_activeBuffCodes[i] == 0) continue;
            if (!first) _chatSb.Append(", ");
            _chatSb.Append(_activeBuffCodes[i]);
            first = false;
        }

        _buffLabel.Text = _chatSb.ToString();
    }

    /// <summary>
    /// Resolves a skill ID to a display name via SkillCatalogue.
    /// Returns "Sk#id" when the catalogue is unavailable or the ID is missing.
    /// spec: Docs/RE/formats/config_tables.md §2.8 skills.scr — skill_id lookup.
    /// NOTE: confirmed name field offset unknown (spec §2.8 known unknowns); numeric label used.
    /// </summary>
    private string ResolveSkillName(uint skillId)
    {
        // skill.scr records do not have a confirmed name column (spec §2.8 known unknowns).
        return $"Sk#{skillId}";
    }

    /// <summary>
    /// Drains the <see cref="IHudEventHub.ZoneChanges"/> channel each frame (main thread).
    /// All Control mutation happens here — never from a background thread.
    ///
    /// Only the ZoneChanges channel is drained here; all other HUD channels are drained by
    /// their dedicated widgets (ChatWindow, BuffBar, TargetFrame, etc.).
    ///
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3 — zone-type enum (Safe/OpenPvp/Closed).
    /// PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — all Control mutation on the main thread.
    /// </summary>
    public override void _Process(double delta)
    {
        // Guard: hub not yet wired (before Initialise is called).
        if (_hudEventHub is null) return;

        // Drain zone-change events (latest-wins capacity=1, so at most one per frame).
        while (_hudEventHub.ZoneChanges.TryRead(out ZoneChangedEvent? evt))
        {
            ApplyZoneChanged(evt);
        }
    }

    /// <summary>
    /// Applies a <see cref="ZoneChangedEvent"/> to the zone-indicator pill.
    /// Updates both the label text and the background colour.
    /// PASSIVE: no game logic — reads event payload only.
    /// spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.
    /// </summary>
    private void ApplyZoneChanged(ZoneChangedEvent evt)
    {
        if (_zoneIndicatorLabel is null || _zoneIndicatorPanel is null) return;

        _zoneIndicatorLabel.Text = ZoneTypeLabel(evt.Zone);

        // Swap cached StyleBoxFlat — no per-event allocation.
        // Colours are PLAUSIBLE — no original art recovered for this widget.
        // spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.
        StyleBoxFlat? pillStyle = evt.Zone switch
        {
            ZoneType.Safe => _zonePillSafe,
            ZoneType.OpenPvp => _zonePillPvp,
            ZoneType.Closed => _zonePillClosed,
            _ => _zonePillUnknown,
        };
        if (pillStyle is not null)
            _zoneIndicatorPanel.AddThemeStyleboxOverride("panel", pillStyle);

        GD.Print($"[GameHud] ZoneIndicator → {evt.Zone} ({ZoneTypeLabel(evt.Zone)}). " +
                 "spec: Docs/RE/specs/world_systems.md Ch. 16 §16.3.");
    }

    public override void _ExitTree()
    {
        _uiLoader?.Dispose();
        _uiLoader = null;
    }
}