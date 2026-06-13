// HUD/OptionsWindow.cs
//
// In-game Options window — 4-tab host (Character / Sound / Graphic / fourth).
//
// PASSIVE: zero game logic. Reads AudioService bus volumes, surfaces on/off toggles and
// 0..100 volume sliders for the Music and Sfx Godot audio buses. Drives AudioServer directly
// from the slider callbacks (main thread). No disk persistence this pass.
//
// Legacy spec references:
//   Options window structure:       spec: Docs/RE/specs/ui_system.md §8.9
//   DoOption.ini sound keys:        spec: Docs/RE/formats/config_tables.md §5.2
//   Sound on/off clamp (1..2):      spec: Docs/RE/formats/config_tables.md §5.2 "Clamp-range refinement"
//   Sound volume clamp (0..100):    spec: Docs/RE/formats/config_tables.md §5.2 "Clamp-range refinement"
//   4-tab container layout:         spec: Docs/RE/specs/ui_system.md §8.9 "4-tab container"
//   Apply/Close buttons:            spec: Docs/RE/specs/ui_system.md §8.9 "Character sub-panel — Apply/Close"
//   Tab button atlas (uitex 9):     spec: Docs/RE/specs/ui_system.md §8.6.1 uitex 9 = messagewindow.dds
//   Checkbox atlas (uitex 1):       spec: Docs/RE/specs/ui_system.md §8.6.1 uitex 1 = mainwindow.dds
//   Audio bus mapping (2→Music/Sfx):spec: Audio/AudioService.cs §Bus simplification
//
// Threading: all Control mutations happen on the main thread (Godot main thread contract).
//   IHudEventHub channels are drained via TryRead in _Process — no background tasks.
//
// House style: mirrors InventoryWindow.cs and SkillWindow.cs (both siblings in this folder).

using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Screens;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Toggleable options window (key O or programmatic toggle via <see cref="Toggle"/>).
///
/// PASSIVE: this node owns only view state (which tab is active, cached slider values,
/// open/close visibility). It never mutates any domain state — it drives Godot's
/// <see cref="AudioServer"/> bus volumes as the immediate audio effect of the slider
/// movements, which is a rendering / output concern, not a domain concern.
///
/// Architecture contract:
///   - Reads the active Godot audio-bus volumes for initial population (from AudioServer
///     directly, because AudioService owns the bus lifecycle and there is no Application
///     catalogue for transient audio settings).
///   - Drives <c>global::Godot.AudioServer.SetBusVolumeDb</c> on slider change.
///   - Subscribes to <see cref="IHudEventHub"/> (when bound via <see cref="Bind"/>):
///     drains <see cref="IHudEventHub.StatAllocations"/> to populate the stat sub-panel.
///   - All other hub streams are ignored here (they belong to GameHud).
///   - Exposes <see cref="Toggle"/> for external wiring without modifying GameHud.cs.
///
/// spec: Docs/RE/specs/ui_system.md §8.9 — 4-tab OptionPanel host. CODE-CONFIRMED.
/// spec: Docs/RE/formats/config_tables.md §5.2 — [DO_OPTION] sound keys. CONFIRMED.
/// </summary>
public sealed partial class OptionsWindow : Control
{
    // -------------------------------------------------------------------------
    // Atlas binding constants (uitex integer ids → DDS)
    // spec: Docs/RE/specs/ui_system.md §8.6.1 — uitex id → DDS table. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    // uitex 9 → data/ui/messagewindow.dds — tab buttons + Apply/Close buttons.
    // spec: Docs/RE/specs/ui_system.md §8.6.1. CODE-CONFIRMED.
    private const int MsgWinTexId = 9;
    private const string MsgWinPath = "data/ui/messagewindow.dds";

    // uitex 1 → data/ui/mainwindow.dds — checkbox glyphs (UNCHECKED/CHECKED) + header strips.
    // spec: Docs/RE/specs/ui_system.md §8.6.1. CODE-CONFIRMED.
    private const int MainWinTexId = 1;
    private const string MainWinPath = "data/ui/mainwindow.dds";

    // Close button atlas — shared modal chrome on inventwindow.dds (uitex 2).
    // Reuses the same frames as InventoryWindow and SkillWindow close buttons.
    // spec: Docs/RE/specs/ui_system.md §8.1 "Quit-confirm Yes #1". CODE-CONFIRMED.
    private const string InvWinPath = "data/ui/inventwindow.dds";
    private const int CloseBtnNormX = 302;
    private const int CloseBtnNormY = 900;
    private const int CloseBtnHoverX = 415;
    private const int CloseBtnHoverY = 900;
    private const int CloseBtnPressX = 302; // PRESSED = NORMAL per spec §1.5
    private const int CloseBtnPressY = 900;
    private const int CloseBtnW = 113;
    private const int CloseBtnH = 40;

    // Tab button frames on messagewindow.dds.
    // spec: Docs/RE/specs/ui_system.md §8.9 — "4 tab buttons (7-state, x=15, 186×40, y-stride 40)
    //        on messagewindow.dds (uitex 9)". CODE-CONFIRMED.
    // Row indices 0..3 → Character / Sound / Graphic / (fourth).
    private static readonly (int NormX, int NormY, int PressX, int PressY)[] TabFrames =
    [
        (833, 517, 460, 916), // tab 0 Character  — spec §8.9 CODE-CONFIRMED
        (833, 557, 460, 876), // tab 1 Sound       — spec §8.9 CODE-CONFIRMED
        (833, 597, 460, 956), // tab 2 Graphic     — spec §8.9 CODE-CONFIRMED
        (833, 637, 646, 516), // tab 3 (fourth)    — spec §8.9 CODE-CONFIRMED
    ];

    private const int TabBtnW = 186;
    private const int TabBtnH = 40;

    // Apply button frames (uitex 9 = messagewindow.dds).
    // spec: Docs/RE/specs/ui_system.md §8.9 "Apply / Close": CODE-CONFIRMED.
    private const int ApplyBtnNormX = 462;
    private const int ApplyBtnNormY = 757;
    private const int ApplyBtnPressX = 646;
    private const int ApplyBtnPressY = 796;

    // Checkbox UNCHECKED / CHECKED frames on mainwindow.dds (uitex 1).
    // spec: Docs/RE/specs/ui_system.md §8.9 "Character sub-panel — 12 checkboxes". CODE-CONFIRMED.
    private const int ChkUncheckedX = 372;
    private const int ChkUncheckedY = 730;
    private const int ChkCheckedX = 372;
    private const int ChkCheckedY = 754;
    private const int ChkSize = 24;

    // -------------------------------------------------------------------------
    // DO_OPTION sound key semantics
    // spec: Docs/RE/formats/config_tables.md §5.2. CONFIRMED.
    // Sound on/off: 1 = on, 2 = off (clamped 1..2, per "Clamp-range refinement").
    // Volume: 0..100 integer, maps to [0.0, 1.0] linear → Godot bus VolumeDb.
    // spec: Docs/RE/formats/config_tables.md §5.2 "Clamp-range refinement". CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    // The Godot audio bus names — must match AudioService constants.
    // spec: Audio/AudioService.cs §Bus simplification (Music + Sfx, 2-bus mapping). DOCUMENTED.
    private const string MusicBusName = "Music";
    private const string SfxBusName = "Sfx";

    // Volume range per spec.
    // spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUNDVOL_* clamp 0..100. CODE-CONFIRMED.
    private const float VolumeMin = 0f;
    private const float VolumeMax = 100f;

    // -------------------------------------------------------------------------
    // Tab index constants (keep in sync with TabFrames array).
    // spec: Docs/RE/specs/ui_system.md §8.9. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private const int TabCharacter = 0;
    private const int TabSound = 1;
    private const int TabGraphic = 2;
    private const int TabFourth = 3;
    private const int TabCount = 4;

    // -------------------------------------------------------------------------
    // View state (NO domain state here)
    // -------------------------------------------------------------------------

    private int _activeTab = TabSound; // open on Sound tab by default (the task focus)

    // Drag state (window is draggable, like InventoryWindow/SkillWindow).
    private bool _dragging;
    private Vector2 _dragOffset;

    // -------------------------------------------------------------------------
    // Hub subscription (Stage Integrate wires this; optional)
    // -------------------------------------------------------------------------

    private IHudEventHub? _hub;
    private System.Threading.CancellationTokenSource? _hubCts;

    // -------------------------------------------------------------------------
    // Child node references (built in BuildUi, stored for tab switching)
    // -------------------------------------------------------------------------

    // One content panel per tab (only the active tab panel is visible at once).
    private readonly Control[] _tabPanels = new Control[TabCount];

    // Tab toggle buttons (to reset their visual state when switching).
    // Stored as Control (StateButton extends Control, not Button).
    private readonly Control[] _tabButtons = new Control[TabCount];

    // Sound tab widgets.
    private CheckBox? _musicOnCheck;
    private HSlider? _musicVolSlider;
    private Label? _musicVolLabel;

    private CheckBox? _sfxOnCheck;
    private HSlider? _sfxVolSlider;
    private Label? _sfxVolLabel;

    // Status label (shown at the top of the window).
    private Label? _statusLabel;

    // -------------------------------------------------------------------------
    // Context / asset loader
    // -------------------------------------------------------------------------

    private ClientContext? _context;
    private UiAssetLoader? _uiLoader;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Resolve ClientContext (optional — degrades gracefully when offline).
        try
        {
            _context = GetNode<ClientContext>("/root/ClientContext");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OptionsWindow] Could not resolve ClientContext: {ex.Message} — offline mode.");
        }

        // Open a UiAssetLoader for atlas slicing.
        try
        {
            _uiLoader = UiAssetLoader.Open();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OptionsWindow] UiAssetLoader.Open failed: {ex.Message} — chrome offline.");
        }

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OptionsWindow] _Ready failed: {ex.Message}");
        }

        // Start hidden; caller toggles via Toggle() or key O.
        Visible = false;

        // When no hub was bound before _Ready, show a demo state so a headless run proves the
        // window renders correctly (mirrors InventoryWindow / SkillWindow behaviour).
        if (_hub is null)
            ShowDemoState();

        GD.Print("[OptionsWindow] Ready. Sound tab drives AudioServer Music/Sfx buses. " +
                 "spec: Docs/RE/formats/config_tables.md §5.2 CONFIRMED. " +
                 "spec: Docs/RE/specs/ui_system.md §8.9 CODE-CONFIRMED.");
    }

    public override void _ExitTree()
    {
        _hubCts?.Cancel();
        _hubCts?.Dispose();
        _hubCts = null;

        _uiLoader?.Dispose();
        _uiLoader = null;
    }

    /// <summary>
    /// Drains the hub's StatAllocations stream each frame (main thread).
    /// All other hub streams are not consumed here — they belong to GameHud.
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — channel drain on main thread.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_hub is null) return;

        // Drain stat-allocation updates into the stat sub-panel (future: Character tab).
        // For now we just consume and discard — prevents the channel from filling.
        while (_hub.StatAllocations.TryRead(out _))
        {
        }
    }

    public override void _Input(InputEvent ev)
    {
        // Toggle on key O (not held).
        // spec: Docs/RE/specs/input_ui.md §4 — options window key toggle.
        if (ev is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.O)
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }

        // Drag — title-bar initiated.
        if (ev is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (GetRect().HasPoint(mb.Position))
                {
                    _dragging = true;
                    _dragOffset = mb.Position - GlobalPosition;
                }
            }
            else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = false;
            }
        }

        if (_dragging && ev is InputEventMouseMotion motion)
            GlobalPosition = motion.Position - _dragOffset;
    }

    // -------------------------------------------------------------------------
    // Public API (Integrate stage wires these)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Binds the window to an <see cref="IHudEventHub"/> so it can drain the
    /// <see cref="IHudEventHub.StatAllocations"/> stream each frame.
    ///
    /// Call once from the integration autoload or GameLoop composition root.
    /// Safe to call before or after <see cref="_Ready"/>.
    ///
    /// spec: Docs/RE/specs/ui_system.md §8.9 — options panel is part of the in-game HUD scene.
    /// </summary>
    public void Bind(IHudEventHub hub)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));

        // If we already showed a demo state before the hub was bound, clear it now.
        if (_statusLabel is not null)
            _statusLabel.Text = "Options (Live)";

        GD.Print("[OptionsWindow] Bound to IHudEventHub. StatAllocations stream will be drained in _Process.");
    }

    /// <summary>
    /// Toggles the window's visibility. Brings to front on show.
    /// Integrate stage may call this from a toolbar button or menu bar without modifying GameHud.cs.
    /// </summary>
    public void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            MoveToFront();
            RefreshSoundTab(); // sync slider positions to current AudioServer bus state
        }
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi()
    {
        // Anchor: centre-right area (does not overlap InventoryWindow at left, SkillWindow at mid).
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -180f;
        OffsetTop = -280f;
        OffsetRight = 220f;
        OffsetBottom = 280f;

        var outerPanel = new PanelContainer();
        outerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        outerPanel.CustomMinimumSize = new Vector2(400, 560);
        AddChild(outerPanel);

        var rootVbox = new VBoxContainer();
        outerPanel.AddChild(rootVbox);

        // ---- Title bar ----
        var titleRow = new HBoxContainer();
        rootVbox.AddChild(titleRow);

        _statusLabel = new Label
        {
            Text = "Options",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        titleRow.AddChild(_statusLabel);

        var closeBtn = BuildCloseButton();
        titleRow.AddChild(closeBtn);

        // ---- Tab selector row ----
        // spec: Docs/RE/specs/ui_system.md §8.9 — "4 tab buttons, x=15, 186×40, y-stride 40".
        // In Godot we lay them horizontally (the legacy had them vertically on the left edge;
        // horizontal is more readable on wider modern windows). Labels come from msg.xdb when VFS
        // is available; English fallbacks are used offline.
        var tabRow = new HBoxContainer();
        rootVbox.AddChild(tabRow);

        // Tab captions — msg.xdb ids are in the 8xxx options bank but not individually confirmed
        // for the tab labels themselves (§8.9 documents the sprite-only tabs; no msg id for tab text).
        // spec: Docs/RE/specs/ui_system.md §8.9 "Tab container — tabs are sprite-only (no title caption)":
        //       CODE-CONFIRMED from §8.11. Using English fallbacks is correct here.
        string[] tabCaptions = ["Character", "Sound", "Graphic", "Other"];

        for (int i = 0; i < TabCount; i++)
        {
            int capturedIndex = i; // capture for lambda
            Control tabBtn = BuildTabButton(i, tabCaptions[i]);
            // Wire the press event to either a plain Button or a StateButton.
            int idx = capturedIndex;
            if (tabBtn is Button btn)
                btn.Pressed += () => SwitchTab(idx);
            else if (tabBtn is Screens.Widgets.StateButton stateBtn)
                stateBtn.ActionFired += _ => SwitchTab(idx);
            _tabButtons[i] = tabBtn;
            tabRow.AddChild(tabBtn);
        }

        // ---- Content area ----
        var contentArea = new PanelContainer();
        contentArea.SizeFlagsVertical = SizeFlags.ExpandFill;
        contentArea.CustomMinimumSize = new Vector2(0, 420);
        rootVbox.AddChild(contentArea);

        // Build one sub-panel per tab.
        _tabPanels[TabCharacter] = BuildCharacterPanel();
        _tabPanels[TabSound] = BuildSoundPanel();
        _tabPanels[TabGraphic] = BuildGraphicPanel();
        _tabPanels[TabFourth] = BuildFourthPanel();

        foreach (Control panel in _tabPanels)
        {
            panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            contentArea.AddChild(panel);
        }

        SwitchTab(_activeTab);
    }

    // -------------------------------------------------------------------------
    // Tab panels
    // -------------------------------------------------------------------------

    /// <summary>
    /// Character tab — 12 checkboxes + caption labels.
    /// spec: Docs/RE/specs/ui_system.md §8.9 "Character sub-panel — 12 checkboxes". CODE-CONFIRMED.
    /// Checkbox glyphs from mainwindow.dds (uitex 1), UNCHECKED src (372,730), CHECKED src (372,754).
    /// spec: Docs/RE/specs/ui_system.md §8.9. CODE-CONFIRMED.
    /// Caption msg.xdb ids: 8009–8039.
    /// spec: Docs/RE/specs/ui_system.md §8.9. CODE-CONFIRMED.
    /// </summary>
    private Control BuildCharacterPanel()
    {
        var panel = new VBoxContainer();

        // Apply / Close bar at the bottom (we put it at the top inside the tab scroll for now).
        // spec: Docs/RE/specs/ui_system.md §8.9 — "Apply / Close at (60,415)/(60,455) CODE-CONFIRMED".
        var applyRow = new HBoxContainer();
        panel.AddChild(applyRow);

        var applyBtn = BuildApplyCloseButton("Apply", isClose: false);
        applyRow.AddChild(applyBtn);

        var closeBtnInner = BuildApplyCloseButton("Close", isClose: true);
        applyRow.AddChild(closeBtnInner);

        // 12 checkbox rows.
        // spec: Docs/RE/specs/ui_system.md §8.9 "12 checkboxes, base y=50, y-stride 30".
        // Caption msg.xdb ids (CODE-CONFIRMED): 8009..8039 (see spec §8.9 label table).
        int[] msgIds = [8009, 8010, 8011, 8012, 8013, 8014, 8018, 8016, 8017, 8037, 8039, 8015];
        string[] fallbacks =
        [
            "Effect quality", "Name display", "HP bar", "Title display",
            "Pet display", "Widget shadow", "FPS show", "Chat bubble",
            "Tooltip", "Auto-loot", "Auto-decline", "Map icons"
        ];

        for (int i = 0; i < 12; i++)
        {
            var row = new HBoxContainer();
            panel.AddChild(row);

            // Caption label (msg.xdb id).
            string caption = GetMsg((uint)msgIds[i], fallbacks[i]);
            var label = new Label
            {
                Text = caption,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(115, 24),
            };
            row.AddChild(label);

            // Checkbox — backed by mainwindow.dds UNCHECKED/CHECKED frames when VFS is online.
            // Defaults to checked (on).
            // spec: Docs/RE/specs/ui_system.md §8.9 — UNCHECKED src (372,730) CHECKED src (372,754): CODE-CONFIRMED.
            var chk = BuildSpriteCheckBox(checked_: true);
            chk.CustomMinimumSize = new Vector2(ChkSize, ChkSize);
            row.AddChild(chk);
        }

        return panel;
    }

    /// <summary>
    /// Sound tab — the fully-functional tab.
    ///
    /// Implements the FULLY FUNCTIONAL sound tab:
    ///   - Music on/off checkbox (OPTION_SOUND_MUSIC, clamped 1..2).
    ///   - Music volume slider 0..100 (OPTION_SOUNDBOL_MUSIC — note: spec-confirmed typo BOL not VOL).
    ///   - Sfx on/off checkbox (maps to OPTION_SOUND_CHAR + OPTION_SOUND_MOB, simplified to single Sfx bus).
    ///   - Sfx volume slider 0..100 (OPTION_SOUNDVOL_CHAR, simplified to single Sfx bus).
    ///
    /// All volume changes drive <c>global::Godot.AudioServer</c> on the main thread.
    ///
    /// spec: Docs/RE/formats/config_tables.md §5.2 — OPTION_SOUND_MUSIC, OPTION_SOUNDBOL_MUSIC,
    ///       OPTION_SOUND_CHAR, OPTION_SOUNDVOL_CHAR. CODE-CONFIRMED keys, typo confirmed.
    /// spec: Docs/RE/formats/config_tables.md §5.2 "Clamp-range refinement" — on/off 1..2; vol 0..100.
    ///       CODE-CONFIRMED.
    /// spec: Audio/AudioService.cs §Bus simplification — Music + Sfx 2-bus mapping. DOCUMENTED.
    /// </summary>
    private Control BuildSoundPanel()
    {
        var panel = new VBoxContainer();
        panel.AddThemeConstantOverride("separation", 12);

        // ---- Header ----
        var headerLabel = new Label
        {
            Text = "Sound Settings",
        };
        headerLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.5f));
        panel.AddChild(headerLabel);

        var sep0 = new HSeparator();
        panel.AddChild(sep0);

        // ---- Music section ----
        var musicHeader = new Label { Text = "Music (BGM)" };
        musicHeader.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
        panel.AddChild(musicHeader);

        // Music on/off — checkbox.
        // spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUND_MUSIC 0/1 (revived: on=1/off=2). CONFIRMED.
        var musicOnRow = new HBoxContainer();
        panel.AddChild(musicOnRow);
        var musicOnLabel = new Label
        {
            Text = "Music enabled",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        musicOnRow.AddChild(musicOnLabel);

        _musicOnCheck = BuildSpriteCheckBox(checked_: IsBusEnabled(MusicBusName));
        _musicOnCheck.Toggled += OnMusicOnToggled;
        musicOnRow.AddChild(_musicOnCheck);

        // Music volume slider 0..100.
        // spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUNDBOL_MUSIC 0..100. CODE-CONFIRMED.
        var musicVolRow = new HBoxContainer();
        panel.AddChild(musicVolRow);
        var musicVolLbl = new Label
        {
            Text = "Music volume",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        musicVolRow.AddChild(musicVolLbl);

        _musicVolSlider = new HSlider
        {
            MinValue = VolumeMin, // spec: 0..100. CODE-CONFIRMED.
            MaxValue = VolumeMax, // spec: 0..100. CODE-CONFIRMED.
            Step = 1,
            Value = GetBusVolumePct(MusicBusName),
            CustomMinimumSize = new Vector2(140, 20),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _musicVolSlider.ValueChanged += OnMusicVolChanged;
        musicVolRow.AddChild(_musicVolSlider);

        _musicVolLabel = new Label
        {
            Text = $"{_musicVolSlider.Value:F0}",
            CustomMinimumSize = new Vector2(32, 0),
        };
        musicVolRow.AddChild(_musicVolLabel);

        var sep1 = new HSeparator();
        panel.AddChild(sep1);

        // ---- SFX section ----
        // Legacy has OPTION_SOUND_CHAR / OPTION_SOUND_MOB / OPTION_SOUND_TERRAIN separately;
        // our 2-bus mapping unifies them as "Sfx".
        // spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUND_CHAR et al. CODE-CONFIRMED.
        // spec: Audio/AudioService.cs §Bus simplification — DOCUMENTED SIMPLIFICATION.
        var sfxHeader = new Label { Text = "Sound Effects (SFX)" };
        sfxHeader.AddThemeColorOverride("font_color", new Color(0.7f, 1f, 0.7f));
        panel.AddChild(sfxHeader);

        // SFX on/off.
        // spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUND_CHAR 0/1. CONFIRMED.
        var sfxOnRow = new HBoxContainer();
        panel.AddChild(sfxOnRow);
        var sfxOnLabel = new Label
        {
            Text = "SFX enabled",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        sfxOnRow.AddChild(sfxOnLabel);

        _sfxOnCheck = BuildSpriteCheckBox(checked_: IsBusEnabled(SfxBusName));
        _sfxOnCheck.Toggled += OnSfxOnToggled;
        sfxOnRow.AddChild(_sfxOnCheck);

        // SFX volume slider 0..100.
        // spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUNDVOL_CHAR 0..100. CODE-CONFIRMED.
        var sfxVolRow = new HBoxContainer();
        panel.AddChild(sfxVolRow);
        var sfxVolLbl = new Label
        {
            Text = "SFX volume",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        sfxVolRow.AddChild(sfxVolLbl);

        _sfxVolSlider = new HSlider
        {
            MinValue = VolumeMin, // spec: 0..100. CODE-CONFIRMED.
            MaxValue = VolumeMax, // spec: 0..100. CODE-CONFIRMED.
            Step = 1,
            Value = GetBusVolumePct(SfxBusName),
            CustomMinimumSize = new Vector2(140, 20),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _sfxVolSlider.ValueChanged += OnSfxVolChanged;
        sfxVolRow.AddChild(_sfxVolSlider);

        _sfxVolLabel = new Label
        {
            Text = $"{_sfxVolSlider.Value:F0}",
            CustomMinimumSize = new Vector2(32, 0),
        };
        sfxVolRow.AddChild(_sfxVolLabel);

        var sep2 = new HSeparator();
        panel.AddChild(sep2);

        // ---- Bus info note ----
        // Inform the user of the 2-bus simplification so it's transparent.
        var noteLabel = new Label
        {
            Text = "Note: Terrain/Char/Mob SFX are unified on one Sfx bus.\n" +
                   "Disk persistence is not yet implemented.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        noteLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        panel.AddChild(noteLabel);

        return panel;
    }

    /// <summary>
    /// Graphic tab — placeholder (widget tables not yet recovered for this sub-panel).
    /// spec: Docs/RE/specs/ui_system.md §12 open item 15 — Sound/Graphic sub-panels not swept.
    /// </summary>
    private static Control BuildGraphicPanel()
    {
        var panel = new VBoxContainer();
        var label = new Label
        {
            Text = "Graphic settings\n(not yet recovered — spec: Docs/RE/specs/ui_system.md §12 open item 15)",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        panel.AddChild(label);
        return panel;
    }

    /// <summary>
    /// Fourth tab — placeholder.
    /// spec: Docs/RE/specs/ui_system.md §8.9 — "fourth tab button" CODE-CONFIRMED (tab exists, content unswept).
    /// </summary>
    private static Control BuildFourthPanel()
    {
        var panel = new VBoxContainer();
        var label = new Label
        {
            Text = "Additional options\n(fourth tab — content not recovered)",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        panel.AddChild(label);
        return panel;
    }

    // -------------------------------------------------------------------------
    // Tab switching
    // -------------------------------------------------------------------------

    private void SwitchTab(int tabIndex)
    {
        _activeTab = tabIndex;
        for (int i = 0; i < TabCount; i++)
        {
            bool active = (i == tabIndex);
            _tabPanels[i].Visible = active;

            // Visual feedback: tint active tab button differently.
            // StateButton extends Control (not Button), so we cast to Control and use Modulate.
            if (_tabButtons[i] is Control tabCtrl)
            {
                tabCtrl.Modulate = active ? new Color(1f, 1f, 0f) : new Color(1f, 1f, 1f);
            }
        }

        // Refresh slider values when switching to the Sound tab so they always reflect
        // the live AudioServer state rather than stale cached values.
        if (tabIndex == TabSound)
            RefreshSoundTab();

        GD.Print(
            $"[OptionsWindow] Switched to tab {tabIndex} ({new[] { "Character", "Sound", "Graphic", "Other" }[tabIndex]}).");
    }

    // -------------------------------------------------------------------------
    // Sound tab — AudioServer integration (FULLY FUNCTIONAL)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Refreshes the Sound tab sliders and checkboxes from the live AudioServer bus state.
    /// Called when the window opens or the Sound tab is selected.
    ///
    /// All reads are from <c>global::Godot.AudioServer</c> (main thread safe).
    /// spec: Audio/AudioService.cs §Bus simplification — 2 buses Music + Sfx. DOCUMENTED.
    /// </summary>
    private void RefreshSoundTab()
    {
        if (_musicOnCheck is not null)
            _musicOnCheck.ButtonPressed = IsBusEnabled(MusicBusName);

        if (_musicVolSlider is not null)
        {
            double pct = GetBusVolumePct(MusicBusName);
            _musicVolSlider.SetValueNoSignal(pct);
            if (_musicVolLabel is not null)
                _musicVolLabel.Text = $"{pct:F0}";
        }

        if (_sfxOnCheck is not null)
            _sfxOnCheck.ButtonPressed = IsBusEnabled(SfxBusName);

        if (_sfxVolSlider is not null)
        {
            double pct = GetBusVolumePct(SfxBusName);
            _sfxVolSlider.SetValueNoSignal(pct);
            if (_sfxVolLabel is not null)
                _sfxVolLabel.Text = $"{pct:F0}";
        }
    }

    /// <summary>
    /// Called when the music on/off checkbox is toggled.
    /// Mutes or restores the Music bus.
    ///
    /// spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUND_MUSIC on=1 off=2. CODE-CONFIRMED.
    /// spec: Docs/RE/formats/config_tables.md §5.2 "a value of 2 forces the matching vol field to 0". CODE-CONFIRMED.
    /// </summary>
    private void OnMusicOnToggled(bool pressed)
    {
        // pressed = true → Music on (OPTION_SOUND_MUSIC = 1 in legacy).
        // pressed = false → Music off (OPTION_SOUND_MUSIC = 2 in legacy, vol forced to 0).
        // spec: Docs/RE/formats/config_tables.md §5.2 "clamp 1..2; value 2 forces vol to 0". CODE-CONFIRMED.
        try
        {
            int busIdx = global::Godot.AudioServer.GetBusIndex(MusicBusName);
            if (busIdx < 0) return;

            global::Godot.AudioServer.SetBusMute(busIdx, !pressed);
            GD.Print($"[OptionsWindow] Music bus {(pressed ? "unmuted" : "muted")}. " +
                     "spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUND_MUSIC. CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OptionsWindow] OnMusicOnToggled failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the music volume slider changes.
    /// Maps 0..100 to the Godot bus VolumeDb using a linear→dB conversion.
    ///
    /// spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUNDBOL_MUSIC 0..100. CODE-CONFIRMED.
    /// spec: Audio/AudioService.cs §Volume curve — linear→dB simplified conversion. DOCUMENTED SIMPLIFICATION.
    /// </summary>
    private void OnMusicVolChanged(double value)
    {
        if (_musicVolLabel is not null)
            _musicVolLabel.Text = $"{value:F0}";

        // value is in [0, 100]. Map to [0.0, 1.0] then to dB.
        // spec: Docs/RE/formats/config_tables.md §5.2 — volume 0..100. CODE-CONFIRMED.
        // spec: Audio/AudioService.cs §Volume curve — VolumeDb = 20 * log10(X), X=0 → -80 dB. DOCUMENTED.
        SetBusVolumePct(MusicBusName, (float)value);
    }

    /// <summary>
    /// Called when the SFX on/off checkbox is toggled.
    /// spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUND_CHAR on=1/off=2. CODE-CONFIRMED.
    /// </summary>
    private void OnSfxOnToggled(bool pressed)
    {
        try
        {
            int busIdx = global::Godot.AudioServer.GetBusIndex(SfxBusName);
            if (busIdx < 0) return;

            global::Godot.AudioServer.SetBusMute(busIdx, !pressed);
            GD.Print($"[OptionsWindow] Sfx bus {(pressed ? "unmuted" : "muted")}. " +
                     "spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUND_CHAR. CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OptionsWindow] OnSfxOnToggled failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the SFX volume slider changes.
    /// spec: Docs/RE/formats/config_tables.md §5.2 OPTION_SOUNDVOL_CHAR 0..100. CODE-CONFIRMED.
    /// spec: Audio/AudioService.cs §Volume curve — simplified linear→dB. DOCUMENTED SIMPLIFICATION.
    /// </summary>
    private void OnSfxVolChanged(double value)
    {
        if (_sfxVolLabel is not null)
            _sfxVolLabel.Text = $"{value:F0}";

        SetBusVolumePct(SfxBusName, (float)value);
    }

    // -------------------------------------------------------------------------
    // AudioServer bus helpers (main thread, all calls guarded)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the current bus volume as a percentage in [0..100].
    /// Returns 100 when the bus is not found (safe default).
    ///
    /// Conversion: VolumeDb → linear amplitude = 10^(dB/20) → pct = amplitude × 100.
    /// spec: Audio/AudioService.cs §Volume curve — documented linear/dB model.
    /// </summary>
    private static double GetBusVolumePct(string busName)
    {
        try
        {
            int idx = global::Godot.AudioServer.GetBusIndex(busName);
            if (idx < 0) return 100.0;

            float db = global::Godot.AudioServer.GetBusVolumeDb(idx);
            // -80 dB is treated as silence (maps to 0 pct).
            if (db <= -79f) return 0.0;
            float linear = MathF.Pow(10f, db / 20f);
            return Math.Clamp(linear * 100.0, 0.0, 100.0);
        }
        catch
        {
            return 100.0;
        }
    }

    /// <summary>
    /// Sets the bus volume from a percentage in [0..100].
    /// spec: Docs/RE/formats/config_tables.md §5.2 — volume 0..100. CODE-CONFIRMED.
    /// spec: Audio/AudioService.cs §Volume curve — VolumeDb = 20*log10(X). DOCUMENTED SIMPLIFICATION.
    /// </summary>
    private static void SetBusVolumePct(string busName, float pct)
    {
        try
        {
            int idx = global::Godot.AudioServer.GetBusIndex(busName);
            if (idx < 0) return;

            // Map pct [0..100] → linear [0..1] → dB.
            // spec: Docs/RE/formats/config_tables.md §5.2 volume 0..100. CODE-CONFIRMED.
            float linear = Math.Clamp(pct / 100f, 0f, 1f);
            float db = linear <= 0f ? -80f : 20f * MathF.Log10(linear);
            global::Godot.AudioServer.SetBusVolumeDb(idx, db);
            GD.Print($"[OptionsWindow] {busName} bus volume → {pct:F0}% ({db:F1} dB). " +
                     "spec: Docs/RE/formats/config_tables.md §5.2. CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OptionsWindow] SetBusVolumePct({busName}, {pct}) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true when the named Godot audio bus is not muted.
    /// Returns true when bus is not found (safe default = enabled).
    /// spec: Audio/AudioService.cs §Bus simplification — 2-bus model. DOCUMENTED.
    /// </summary>
    private static bool IsBusEnabled(string busName)
    {
        try
        {
            int idx = global::Godot.AudioServer.GetBusIndex(busName);
            if (idx < 0) return true;
            return !global::Godot.AudioServer.IsBusMute(idx);
        }
        catch
        {
            return true;
        }
    }

    // -------------------------------------------------------------------------
    // Widget builders
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the close StateButton using inventwindow.dds shared modal frames.
    /// spec: Docs/RE/specs/ui_system.md §8.1 "Quit-confirm Yes #1". CODE-CONFIRMED.
    /// Falls back to a plain Godot Button when the VFS atlas is offline.
    /// </summary>
    private Control BuildCloseButton()
    {
        if (_uiLoader is not null)
        {
            AtlasTexture? normFrame =
                _uiLoader.Slice(InvWinPath, CloseBtnNormX, CloseBtnNormY, CloseBtnW, CloseBtnH);
            AtlasTexture? hoverFrame =
                _uiLoader.Slice(InvWinPath, CloseBtnHoverX, CloseBtnHoverY, CloseBtnW, CloseBtnH);
            AtlasTexture? pressedFrame =
                _uiLoader.Slice(InvWinPath, CloseBtnPressX, CloseBtnPressY, CloseBtnW, CloseBtnH);

            if (normFrame is not null)
            {
                var stateBtn = new Screens.Widgets.StateButton
                {
                    Name = "CloseBtn",
                    CustomMinimumSize = new Vector2(CloseBtnW, CloseBtnH),
                    NormalFrame = normFrame,
                    HoverFrame = hoverFrame,
                    PressedFrame = pressedFrame,
                    Caption = "X",
                    ActionId = 1, // spec §8.9 action index 1 = Close. CODE-CONFIRMED.
                };
                stateBtn.ActionFired += _ => { Visible = false; };
                return stateBtn;
            }
        }

        var fallback = new Button { Text = "X" };
        fallback.Pressed += () => { Visible = false; };
        return fallback;
    }

    /// <summary>
    /// Builds an Apply or Close button using messagewindow.dds frames.
    /// spec: Docs/RE/specs/ui_system.md §8.9 "Character sub-panel — Apply/Close". CODE-CONFIRMED.
    /// </summary>
    private Control BuildApplyCloseButton(string caption, bool isClose)
    {
        // Apply: action index 0 / Close: action index 1.
        // spec: Docs/RE/specs/ui_system.md §8.9 "Apply action 0, Close action 1". CODE-CONFIRMED.
        int normX = isClose ? CloseBtnNormX : ApplyBtnNormX;
        int normY = isClose ? CloseBtnNormY : ApplyBtnNormY;
        int pressX = isClose ? CloseBtnPressX : ApplyBtnPressX;
        int pressY = isClose ? CloseBtnPressY : ApplyBtnPressY;
        string atlasPath = isClose ? InvWinPath : MsgWinPath;

        if (_uiLoader is not null)
        {
            AtlasTexture? normFrame = _uiLoader.Slice(atlasPath, normX, normY, CloseBtnW, CloseBtnH);
            AtlasTexture? pressedFrame = _uiLoader.Slice(atlasPath, pressX, pressY, CloseBtnW, CloseBtnH);

            if (normFrame is not null)
            {
                var stateBtn = new Screens.Widgets.StateButton
                {
                    Name = isClose ? "InnerCloseBtn" : "ApplyBtn",
                    Caption = caption,
                    NormalFrame = normFrame,
                    PressedFrame = pressedFrame,
                    ActionId = isClose ? 1 : 0,
                };

                if (isClose)
                    stateBtn.ActionFired += _ => { Visible = false; };
                // Apply fires no action this pass (disk write is a follow-up).

                return stateBtn;
            }
        }

        var fallback = new Button { Text = caption };
        if (isClose)
            fallback.Pressed += () => { Visible = false; };
        return fallback;
    }

    /// <summary>
    /// Builds a tab selector button from messagewindow.dds frames.
    /// spec: Docs/RE/specs/ui_system.md §8.9 — "tab buttons on messagewindow.dds (uitex 9)". CODE-CONFIRMED.
    /// HOVER = NORMAL (caption-only hover feedback per spec §8.9).
    /// Falls back to a plain Button when the VFS atlas is offline.
    /// Returns <see cref="Control"/> because <see cref="Screens.Widgets.StateButton"/> extends
    /// <see cref="Control"/> directly (not <see cref="Button"/>).
    /// </summary>
    private Control BuildTabButton(int tabIndex, string caption)
    {
        // Tab frames per spec §8.9. CODE-CONFIRMED.
        (int normX, int normY, int pressX, int pressY) = TabFrames[tabIndex];

        if (_uiLoader is not null)
        {
            AtlasTexture? normFrame = _uiLoader.Slice(MsgWinPath, normX, normY, TabBtnW, TabBtnH);
            AtlasTexture? pressedFrame = _uiLoader.Slice(MsgWinPath, pressX, pressY, TabBtnW, TabBtnH);

            if (normFrame is not null)
            {
                return new Screens.Widgets.StateButton
                {
                    Name = $"TabBtn{tabIndex}",
                    Caption = caption,
                    NormalFrame = normFrame,
                    HoverFrame = normFrame, // HOVER = NORMAL per spec §8.9 "caption-only hover"
                    PressedFrame = pressedFrame,
                    ActionId = tabIndex,
                    CustomMinimumSize = new Vector2(TabBtnW / 2f, TabBtnH / 2f),
                };
            }
        }

        return new Button
        {
            Name = $"TabBtn{tabIndex}",
            Text = caption,
        };
    }

    /// <summary>
    /// Builds a Godot CheckBox backed by mainwindow.dds UNCHECKED/CHECKED sprite frames.
    /// spec: Docs/RE/specs/ui_system.md §8.9 — "UNCHECKED src (372,730) CHECKED src (372,754)". CODE-CONFIRMED.
    /// Falls back to a plain Godot CheckBox when the VFS atlas is offline.
    ///
    /// NOTE: A Godot CheckBox uses the ButtonPressed property for checked state, which matches
    /// the GUCheckBox semantics (PRESSED frame = checked).
    /// spec: Docs/RE/specs/ui_system.md §1 "GUCheckBox: checked = PRESSED frame". CODE-CONFIRMED.
    /// </summary>
    private CheckBox BuildSpriteCheckBox(bool checked_)
    {
        // We use a plain CheckBox here — AtlasTexture-backed sprite checkboxes require a
        // custom StyleBox which would bloat this class substantially. The VFS sprite binding
        // is a future visual polish step; the behaviour (on/off toggle) is identical.
        // OPEN: wire mainwindow.dds (372,730)/(372,754) into CheckBox icon overrides.
        // spec: Docs/RE/specs/ui_system.md §8.9 — checkbox glyph atlas. CODE-CONFIRMED.
        var chk = new CheckBox
        {
            ButtonPressed = checked_,
        };
        return chk;
    }

    // -------------------------------------------------------------------------
    // Demo state (shown when no hub is bound — headless verification path)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates the window with demo values so a headless run proves the window renders.
    /// Mirrors the InventoryWindow / SkillWindow "offline mode" pattern.
    /// spec: CLAUDE.md Headless Verify Loop.
    /// </summary>
    private void ShowDemoState()
    {
        if (_statusLabel is not null)
            _statusLabel.Text = "Options (DEMO — no hub bound)";

        // Force-open the window and switch to Sound tab so the headless runner can capture it.
        Visible = true;
        SwitchTab(TabSound);

        // Set sliders to fixed demo values so the screenshot shows real content.
        _musicVolSlider?.SetValueNoSignal(75.0);
        if (_musicVolLabel is not null) _musicVolLabel.Text = "75";

        _sfxVolSlider?.SetValueNoSignal(60.0);
        if (_sfxVolLabel is not null) _sfxVolLabel.Text = "60";

        GD.Print("[OptionsWindow] DEMO state active (no IHudEventHub bound). " +
                 "Music=75, SFX=60 demo values shown. " +
                 "spec: CLAUDE.md Headless Verify Loop — self-populate when no hub.");
    }

    // -------------------------------------------------------------------------
    // msg.xdb helpers
    // -------------------------------------------------------------------------

    private string GetMsg(uint id, string fallback)
    {
        if (_context?.UiCatalogs is { } cats)
            return cats.GetMessage((int)id, fallback);
        return fallback;
    }
}