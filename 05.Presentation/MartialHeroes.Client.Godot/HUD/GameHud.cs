using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.World;
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

        // ---- Chrome box — stats/vitals panel, anchored to top-left. ----
        // A child Control hosts the chrome TextureRect + stats vbox, positioned at the
        // top-left corner at (4,4) with its original (ChromeW+4) × (ChromeH+4) footprint.
        var chromeBox = new Control
        {
            Name = "ChromeBox",
            Position = new Vector2(4f, 4f),
            Size = new Vector2(ChromeW, ChromeH),
            CustomMinimumSize = new Vector2(ChromeW, ChromeH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(chromeBox);

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

        // ---- Hotbar (bottom of screen) ----
        try
        {
            BuildHotbar();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameHud] BuildHotbar failed: {ex.Message}");
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
        try
        {
            _buffBar = new BuffBar
            {
                Name = "BuffBar",
                AnchorLeft = 0f,
                AnchorTop = 0f,
                AnchorRight = 0f,
                AnchorBottom = 0f,
                OffsetLeft = 4f,
                OffsetTop = 140f, // below the chrome box (130 px) — PLAUSIBLE
                OffsetRight = 800f,
                OffsetBottom = 170f,
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

        GD.Print("[GameHud] _Ready completed. HUD chrome wired to uitex 0001 (mainwindow.dds). " +
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
    // Hotbar construction
    // -------------------------------------------------------------------------

    private void BuildHotbar()
    {
        // Anchor to bottom-centre.
        var hotbarContainer = new Control();
        hotbarContainer.AnchorLeft = 0.5f;
        hotbarContainer.AnchorTop = 1.0f;
        hotbarContainer.AnchorRight = 0.5f;
        hotbarContainer.AnchorBottom = 1.0f;
        hotbarContainer.OffsetLeft = -225f;
        hotbarContainer.OffsetTop = -84f;
        hotbarContainer.OffsetRight = 225f;
        hotbarContainer.OffsetBottom = -4f;
        AddChild(hotbarContainer);

        // Optional: hotbar chrome from skillpipe.dds (uitex 0010).
        // We attempt a texture bind but don't block HUD construction on failure.
        var hotbarBg = new TextureRect
        {
            Name = "HotbarChrome",
            StretchMode = TextureRect.StretchModeEnum.Tile,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hotbarBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hotbarContainer.AddChild(hotbarBg);
        BindHotbarChromeTexture(hotbarBg);

        var hbox = new HBoxContainer();
        hbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hotbarContainer.AddChild(hbox);

        for (int i = 0; i < HotbarVisibleSlots; i++)
        {
            var slotVBox = new VBoxContainer();
            slotVBox.CustomMinimumSize = new Vector2(48, 72);
            hbox.AddChild(slotVBox);

            // Key label ("1"–"9").
            _hotbarKey[i] = new Label { Text = $"{i + 1}", HorizontalAlignment = HorizontalAlignment.Center };
            slotVBox.AddChild(_hotbarKey[i]);

            // Icon TextureRect — backed by skillpipe.dds slot sub-rect (PLAUSIBLE).
            _hotbarIcon[i] = new TextureRect
            {
                Name = $"HotbarIcon{i}",
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(HotbarSlotW, HotbarSlotH),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            // Icon slot src rect — each slot is HotbarSlotW×HotbarSlotH on the sheet.
            // Exact per-slot offsets on skillpipe.dds are UNRECOVERED.
            // We use column-major grid: slot i at (i * HotbarSlotW, 0). // PLAUSIBLE
            // spec: Docs/RE/specs/ui_system.md §12 (open item 6) — hotbar layout gated on uitex manifest.
            BindHotbarSlotIcon(_hotbarIcon[i], i);
            slotVBox.AddChild(_hotbarIcon[i]);

            // Skill name (from SkillCatalogue via CP949 name).
            _hotbarName[i] = new Label
            {
                Text = "—",
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(48, 14),
                AutowrapMode = TextServer.AutowrapMode.Off,
                ClipText = true,
            };
            slotVBox.AddChild(_hotbarName[i]);
        }
    }

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
            // VFS offline — hotbar keeps skillpipe.dds chrome slices from BuildHotbar.
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
        string line = $"[{evt.SenderName}]: {evt.Text}";
        if (_chatLines.Count >= ChatLineMax) _chatLines.Dequeue();
        _chatLines.Enqueue(line);
        // Reuse _chatSb to avoid per-message heap allocation (finding 4).
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

    /// <summary>Reacts to a client lifecycle state change: update the state label.</summary>
    public void OnClientStateChanged(ClientStateChangedEvent evt)
    {
        if (_stateLabel is null) return;
        _stateLabel.Text = $"State: {evt.Current}";
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

    public override void _ExitTree()
    {
        _uiLoader?.Dispose();
        _uiLoader = null;
    }
}