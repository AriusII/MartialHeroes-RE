// Ui/Scenes/Opening/OpeningWindow.cs
//
// State-3 Opening intro window — rebuilt FROM SCRATCH on the Ui/Scenes substrate.
//
// Backed entirely by HudAtlasLibrary (GetByPath / SliceByPath) and FrontEndAudio.
// No UiAssetLoader dependency.
//
// TWO INDEPENDENT ANIMATED LAYERS per spec:
//   1. SPLASH SLIDESHOW — openning_001..004.dds (1024×768), 4-state machine, 17500 ms dwell per
//      panel; alpha seeded at 250 (max), direction-toggled fade-OUT first (not fade-in from 0).
//      spec: Docs/RE/specs/intro_sequence.md §3/§3.2/§3.4. CODE-CONFIRMED.
//
//   2. SCENARIO CRAWL — openning_scenario.dds (1024×2048), single quad translated vertically.
//      Scrolls UPWARD (credits-style): Godot Y decreases as _scrollPos grows 0→1843.
//      Speed 30 design-px/s after a ~1000 ms gate; clamp/stop at 1843; no wrap.
//      Centred horizontally: left edge at 0 on a 1024-wide canvas.
//      spec: Docs/RE/specs/intro_sequence.md §2; Docs/RE/specs/frontend_scenes.md §1.0.3. CODE-CONFIRMED.
//
// SOUND:
//   Looped intro BGM 910061000 fired once at scene start via FrontEndAudio.PlayIntroBgm().
//   spec: Docs/RE/specs/intro_sequence.md §4. CODE-CONFIRMED.
//
// SKIP (the ONLY exit):
//   Enter(10) / ESC(27) / Space(32) key OR click the skip button (action id 100).
//   Persists [OPENNING] SKIP=1 then emits IntroFinished.
//   There is NO auto-finish — panel 4 loops indefinitely until an explicit skip.
//   spec: Docs/RE/specs/intro_sequence.md §3.1 / §2.5. CODE-CONFIRMED.
//
// ASSETS (all real VFS DDS — no solid-colour fallback):
//   data/ui/openning_scenario.dds  — 1024×2048 scenario crawl strip. SAMPLE-VERIFIED.
//   data/ui/openning_001..004.dds  — 1024×768 splash panels. SAMPLE-VERIFIED.
//   data/ui/mainwindow.dds         — chrome layer (skip-button sprite). SAMPLE-VERIFIED.
//   Missing asset → GD.PrintErr + skip/continue; no crash, no solid-colour substitute.
//   spec: Docs/RE/specs/intro_sequence.md §1. SAMPLE-VERIFIED.
//
// NOTE on legacy typo: the original client spells the stem "openning" (double-n) — that is the
// exact VFS filename; preserve it.
// spec: Docs/RE/specs/intro_sequence.md §0 note. CODE-CONFIRMED.
//
// SUBSTRATE:
//   HudAtlasLibrary — GetByPath / SliceByPath (Phase-A).
//   ScreenHost — the 1024×768 canvas host (DO NOT set FullRect anchors on the window root).
//   FrontEndAudio — PlayIntroBgm() for BGM 910061000.
//
// PASSIVE: zero game logic. Reads VFS textures; emits IntroFinished when skipped.
// spec: Docs/RE/specs/intro_sequence.md §0–§6.

using Godot;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Opening;

/// <summary>
/// Post-login intro Control (GameState 3): splash slideshow + scenario crawl ("red ribbon").
/// Built on the Ui/Scenes substrate; backed by <see cref="HudAtlasLibrary"/>.
///
/// <para>Emits <see cref="IntroFinishedEventHandler"/> when the player skips.
/// There is NO auto-finish — panel 4 loops indefinitely.</para>
///
/// spec: Docs/RE/specs/intro_sequence.md §0–§6 / §0.1.
/// </summary>
public sealed partial class OpeningWindow : Control
{
    // =========================================================================
    // Signal
    // =========================================================================

    /// <summary>Emitted when the player skips the intro.</summary>
    [Signal]
    public delegate void IntroFinishedEventHandler();

    // =========================================================================
    // Constants — spec: intro_sequence.md §2 / §3. CODE-CONFIRMED.
    // =========================================================================

    // Scenario crawl. spec: intro_sequence.md §2. CODE-CONFIRMED.
    private const float ScrollSpeed = 30.0f;       // design-px / second. spec §2.1/§2.3. CODE-CONFIRMED.
    private const float ScrollStartDelayMs = 1000f; // ~1000 ms startup gate. spec §2.1. CODE-CONFIRMED.
    private const float ScrollClamp = 1843.0f;      // stop clamp; no wrap. spec §2.1/§2.3. CODE-CONFIRMED.

    // Scenario quad size. spec: intro_sequence.md §1. SAMPLE-VERIFIED.
    private const float ScenarioW = 1024f; // spec §1 "width 1024". SAMPLE-VERIFIED.
    private const float ScenarioH = 2048f; // spec §1 "height 2048". SAMPLE-VERIFIED.

    // Design canvas height — anchor base for upward-scroll.
    // spec: Docs/RE/specs/frontend_scenes.md §11.0 "canvas 1024×768". CODE-CONFIRMED.
    private const float CanvasH = 768f;

    // Slideshow. spec: intro_sequence.md §3. CODE-CONFIRMED.
    private const int SlideshowFrameCount = 4;    // four panels. spec §3.1/§3.3. CODE-CONFIRMED.
    private const double DwellMs = 17500.0;        // ms per panel. spec §3.3. CODE-CONFIRMED.
    private const int AlphaMax = 250;              // alpha ramp bound (not 255). spec §3.3. CODE-CONFIRMED.
    // Alpha step ±1 per rendered frame (frame-gated, NOT ms-gated). spec §3.2. CODE-CONFIRMED.

    // VFS asset paths. spec: intro_sequence.md §1. SAMPLE-VERIFIED.
    // "openning" (double-n) is the exact VFS spelling. spec §0 note. CODE-CONFIRMED.
    private const string ScenarioPath = "data/ui/openning_scenario.dds"; // spec §1. SAMPLE-VERIFIED.
    private const string MainWindowPath = "data/ui/mainwindow.dds";       // spec §1. SAMPLE-VERIFIED.

    private static readonly string[] SlideshowPaths =
    [
        "data/ui/openning_001.dds", // state 1. spec §1. SAMPLE-VERIFIED.
        "data/ui/openning_002.dds", // state 2. spec §1. SAMPLE-VERIFIED.
        "data/ui/openning_003.dds", // state 3. spec §1. SAMPLE-VERIFIED.
        "data/ui/openning_004.dds", // state 4. spec §1. SAMPLE-VERIFIED.
    ];

    // Skip persistence constants — must match what BootFlow / ClientContext read.
    // These mirror MartialHeroes.Client.Godot.Screens.OpeningWindow.SkipCfg* so the
    // write here and the read in BootFlow/ClientContext agree on the same file/section/key.
    // spec: Docs/RE/specs/frontend_scenes.md §1.0.0 (read) / §1.0.5 (write). CODE-CONFIRMED.
    private const string SkipCfgPath    = "user://options.cfg"; // same path as Screens.OpeningWindow. spec §1.0.5.
    private const string SkipCfgSection = "OPENNING";           // spec §1.0.0 section [OPENNING]. CODE-CONFIRMED.
    private const string SkipCfgKey     = "SKIP";               // spec §1.0.0 key SKIP. CODE-CONFIRMED.

    // =========================================================================
    // Injection — set by OpeningScene before SetScreen
    // =========================================================================

    /// <summary>Shared HUD atlas library — loads DDS textures from the VFS.</summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>Shared audio node — used to fire the intro BGM.</summary>
    public FrontEndAudio? Audio { get; set; }

    // =========================================================================
    // View state — NO domain state
    // =========================================================================

    // Scenario crawl node and state.
    private TextureRect? _scenarioRect;
    private float _scrollPos;       // current scroll position in design-px (0 → 1843)
    private float _scrollStartWait; // countdown ms; crawl starts when ≤ 0
    private bool _scrollDone;       // latch: crawl reached 1843

    // Slideshow node and state.
    private TextureRect? _slideshowRect;
    private int _slideshowState = 1; // 1..4 (1-based panel index). spec §3.1.
    private readonly Texture2D?[] _slideshowTextures = new Texture2D?[SlideshowFrameCount];
    private double _dwellAccumMs;    // ms elapsed in current dwell
    private int _alpha;              // 0..250 ramp. spec §3.2.
    private int _alphaDir = -1;      // +1 ramp-up, −1 ramp-down; starts at -1 (fade-OUT). spec §3.2.
    private bool _panelFadedIn;      // latched when alpha first reaches AlphaMax

    // Global finish guard.
    private bool _finished;

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        // ScreenHost calls SetScreen which sets our Size to 1024×768 — set defensively here too.
        Size = new Vector2(ScenarioW, CanvasH);
        MouseFilter = MouseFilterEnum.Stop; // catch clicks/keys for the skip gate

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Ui/Opening/OpeningWindow] Build failed: {ex.Message} — skipping intro.");
            Finish();
            return;
        }

        // Initialise crawl state.
        _scrollStartWait = ScrollStartDelayMs; // spec §2.1 "~1000 ms startup gate". CODE-CONFIRMED.
        _scrollPos = 0f;

        // Alpha seeded at 250 (max); first phase is a FADE-OUT.
        // spec: intro_sequence.md §3.2/§3.3/§3.4. CODE-CONFIRMED.
        _alpha = AlphaMax;
        _alphaDir = -1; // fade-OUT first. spec §3.2. CODE-CONFIRMED.
        _dwellAccumMs = 0.0;
        _panelFadedIn = true; // panel starts at full alpha; dwell may begin immediately.
        _slideshowState = 1;

        // Fire the intro BGM once at scene start. spec: intro_sequence.md §4. CODE-CONFIRMED.
        Audio?.PlayIntroBgm();

        GD.Print("[Ui/Opening/OpeningWindow] Intro sequence started (slideshow + scenario crawl active). " +
                 "spec: intro_sequence.md §1–§4.");
    }

    // =========================================================================
    // Per-frame update
    // =========================================================================

    public override void _Process(double delta)
    {
        if (_finished) return;

        float dt   = (float)delta;
        float dtMs = dt * 1000f;

        // Update scenario crawl. spec: intro_sequence.md §2.1. CODE-CONFIRMED.
        UpdateScroll(dtMs, dt);

        // Update splash slideshow. spec: intro_sequence.md §3.1/§3.2. CODE-CONFIRMED.
        UpdateSlideshow(dtMs);
    }

    // =========================================================================
    // Input — skip on Enter/ESC/Space (keyboard). Mouse skip via skip button only.
    // spec: Docs/RE/specs/frontend_scenes.md §1.0.5 / intro_sequence.md §2.5. CODE-CONFIRMED.
    // =========================================================================

    public override void _Input(InputEvent ev)
    {
        if (_finished) return;

        bool skip = ev switch
        {
            // spec §2.5: key codes 10 (Enter), 27 (ESC), 32 (Space). CODE-CONFIRMED.
            InputEventKey { Pressed: true, KeyLabel: Key.Enter  } => true,
            InputEventKey { Pressed: true, KeyLabel: Key.Escape } => true,
            InputEventKey { Pressed: true, KeyLabel: Key.Space  } => true,
            // Mouse: only the dedicated skip button (action id 100) skips — not any click.
            // The skip button TextureButton is wired to OnSkipPressed() below.
            // spec §2.5: "button-click event carrying action-tag 100". CODE-CONFIRMED.
            _ => false,
        };

        if (skip)
        {
            GD.Print("[Ui/Opening/OpeningWindow] Keyboard skip (Enter/ESC/Space) received.");
            PersistSkip();
            Finish();
        }
    }

    // =========================================================================
    // Skip button handler (action id 100, mainwindow.dds).
    // spec: Docs/RE/specs/intro_sequence.md §2.2 / §2.5 / frontend_scenes.md §1.0.5. CODE-CONFIRMED.
    // =========================================================================

    private void OnSkipPressed()
    {
        if (_finished) return;
        GD.Print("[Ui/Opening/OpeningWindow] Skip button (action 100) pressed.");
        PersistSkip();
        Finish();
    }

    // =========================================================================
    // Skip persistence — write [OPENNING] SKIP=1 to options INI.
    // spec: Docs/RE/specs/frontend_scenes.md §1.0.0 (read) / §1.0.5 (write). CODE-CONFIRMED.
    // =========================================================================

    private static void PersistSkip()
    {
        // Write SKIP=1 so BootFlow's read-gate (§1.0.0 / §0.1) skips the intro on the next launch.
        // spec: Docs/RE/specs/frontend_scenes.md §1.0.0 / §1.0.5. CODE-CONFIRMED.
        var cfg = new ConfigFile();
        cfg.Load(SkipCfgPath); // load existing (ignore error — file may not exist yet)
        cfg.SetValue(SkipCfgSection, SkipCfgKey, 1);
        cfg.Save(SkipCfgPath);
    }

    // =========================================================================
    // UI construction — real VFS assets only; missing asset → log + skip, no solid-colour fallback.
    // spec: intro_sequence.md §1. SAMPLE-VERIFIED.
    // =========================================================================

    private void BuildUi()
    {
        // ── Layer 1: splash slideshow (full-screen DDS panels, drawn below the crawl) ──
        // spec: intro_sequence.md §3 "full-screen DDS panels". SAMPLE-VERIFIED.
        _slideshowRect = new TextureRect
        {
            Name        = "SlideshowRect",
            Position    = Vector2.Zero,
            Size        = new Vector2(ScenarioW, CanvasH),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate    = new Color(1f, 1f, 1f, 0f), // start transparent; alpha ramp drives fade
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_slideshowRect);

        // Pre-load all four slideshow textures.
        // spec: intro_sequence.md §1 "loaded once at scene build". SAMPLE-VERIFIED.
        for (int i = 0; i < SlideshowFrameCount; i++)
        {
            _slideshowTextures[i] = Atlas?.GetByPath(SlideshowPaths[i]);
            if (_slideshowTextures[i] is not null)
                GD.Print($"[Ui/Opening/OpeningWindow] Slideshow panel {i + 1} loaded: {SlideshowPaths[i]}");
            else
                GD.PrintErr(
                    $"[Ui/Opening/OpeningWindow] Slideshow panel {i + 1} absent from VFS: {SlideshowPaths[i]} — panel will be blank.");
        }

        // Wire the first panel (state 1 → openning_001.dds). spec §3.1. CODE-CONFIRMED.
        _slideshowRect.Texture = _slideshowTextures[0];

        // ── Layer 2: scenario crawl ("red ribbon") — 1024×2048 positional Y translate ──
        // spec: intro_sequence.md §2 "destination-Y translate; not UV-scroll; not a shader".
        // CODE-CONFIRMED.
        _scenarioRect = new TextureRect
        {
            Name        = "ScenarioRect",
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        Texture2D? scenarioTex = Atlas?.GetByPath(ScenarioPath);
        if (scenarioTex is not null)
        {
            _scenarioRect.Texture = scenarioTex;
            GD.Print($"[Ui/Opening/OpeningWindow] Scenario crawl loaded: {ScenarioPath}");
        }
        else
        {
            GD.PrintErr(
                $"[Ui/Opening/OpeningWindow] Scenario crawl absent from VFS: {ScenarioPath} — crawl layer will be blank.");
        }

        // Place the scenario sprite: centred horizontally, starting just below the canvas bottom.
        // As _scrollPos grows 0→1843 the sprite rises upward (credits-style).
        // spec: intro_sequence.md §2.1/§2.4. CODE-CONFIRMED.
        // Horizontal centre: left edge at (canvasW/2 − ScenarioW/2) = 0 on a 1024-wide canvas.
        _scenarioRect.Position = new Vector2(0f, CanvasH); // sprite top-left at Y=768 (off-screen below)
        _scenarioRect.Size     = new Vector2(ScenarioW, ScenarioH);
        AddChild(_scenarioRect);

        // ── Layer 3: Skip button (action id 100) from mainwindow.dds ──
        // Anchored to TOP-right: x = clientWidth − 120, y = 10, size 110×32.
        // Normal src (761,165,110,32) / pressed src (634,165,110,32) from mainwindow.dds.
        // spec: intro_sequence.md §2.2/§6. CODE-CONFIRMED (top-right, y=10).
        AtlasTexture? skipNormal  = Atlas?.SliceByPath(MainWindowPath, 761, 165, 110, 32);
        AtlasTexture? skipPressed = Atlas?.SliceByPath(MainWindowPath, 634, 165, 110, 32);

        if (skipNormal is null)
        {
            GD.PrintErr("[Ui/Opening/OpeningWindow] mainwindow.dds skip-button slice null " +
                        "— skip button absent (VFS offline). spec: frontend_scenes.md §1.0.5.");
        }
        else
        {
            // AnchorRight=1, AnchorTop=0; x = clientWidth − 120 → OffsetLeft = −120 from right.
            // spec: intro_sequence.md §2.2/§6. CODE-CONFIRMED.
            var skipBtn = new TextureButton
            {
                Name            = "SkipBtn",
                AnchorLeft      = 1f,
                AnchorRight     = 1f,
                AnchorTop       = 0f,
                AnchorBottom    = 0f,
                OffsetLeft      = -120f,   // x = clientWidth − 120. spec §2.2. CODE-CONFIRMED.
                OffsetTop       = 10f,     // y = 10. spec §2.2. CODE-CONFIRMED.
                OffsetRight     = -10f,    // right edge 10 px from right (110 px wide)
                OffsetBottom    = 42f,     // y + 32 px height
                TextureNormal   = skipNormal,
                TexturePressed  = skipPressed ?? skipNormal, // fallback to normal when pressed absent
                StretchMode     = TextureButton.StretchModeEnum.KeepAspect,
            };
            skipBtn.Pressed += OnSkipPressed;
            AddChild(skipBtn);
        }
    }

    // =========================================================================
    // Scenario crawl logic
    // spec: intro_sequence.md §2.1/§2.2. CODE-CONFIRMED.
    // =========================================================================

    private void UpdateScroll(float dtMs, float dt)
    {
        if (_scenarioRect is null) return;

        if (_scrollStartWait > 0f)
        {
            // spec §2.1 "~1000 ms startup gate before scroll begins". CODE-CONFIRMED.
            _scrollStartWait -= dtMs;
            return;
        }

        if (!_scrollDone)
        {
            // Auto-crawl: advance at 30 design-px/s. spec §2.1/§2.3. CODE-CONFIRMED.
            _scrollPos += dt * ScrollSpeed; // spec §2.3 "30 design-px/s". CODE-CONFIRMED.

            if (_scrollPos >= ScrollClamp)
            {
                _scrollPos = ScrollClamp; // spec §2.1/§2.3 "stop clamp 1843; no wrap". CODE-CONFIRMED.
                _scrollDone = true;
                GD.Print("[Ui/Opening/OpeningWindow] Scenario crawl reached clamp 1843 — stopped.");
            }
        }
        else
        {
            // Manual-nudge review mode after auto-crawl latches done. spec §2.2. CODE-CONFIRMED.
            // Actions 1004 (up) / 1005 (down). spec: intro_sequence.md §2.2. CODE-CONFIRMED.
            if (global::Godot.Input.IsActionPressed("ui_up"))
                _scrollPos = Mathf.Min(ScrollClamp, _scrollPos + dt * ScrollSpeed);
            else if (global::Godot.Input.IsActionPressed("ui_down"))
                _scrollPos = Mathf.Max(0f, _scrollPos - dt * ScrollSpeed);
        }

        // Drive the sprite's on-screen Y.
        // Godot +Y is DOWN. To scroll upward (credits-style), Y must DECREASE as _scrollPos grows.
        // Start: Y = CanvasH (sprite below canvas). End: Y = CanvasH − 1843 (well above canvas).
        // spec: intro_sequence.md §2.1 "positional translate; upward direction". CODE-CONFIRMED.
        _scenarioRect.Position = new Vector2(_scenarioRect.Position.X, CanvasH - _scrollPos);
    }

    // =========================================================================
    // Slideshow logic
    // spec: intro_sequence.md §3.1/§3.2. CODE-CONFIRMED.
    // =========================================================================

    private void UpdateSlideshow(float dtMs)
    {
        if (_slideshowRect is null) return;

        // Alpha ramp: ±1 per rendered frame (frame-gated). Direction toggles at 0 and AlphaMax.
        // The alpha field is INITIALISED to 250 (max) and direction starts at -1 (fade-OUT first).
        // spec: intro_sequence.md §3.2/§3.3/§3.4. CODE-CONFIRMED.
        _alpha += _alphaDir;
        if (_alpha <= 0)
        {
            _alpha    = 0;
            _alphaDir = 1; // reverse: now fade-IN
        }
        else if (_alpha >= AlphaMax)
        {
            _alpha    = AlphaMax;
            _alphaDir = -1; // reverse: now fade-OUT
        }

        _slideshowRect.Modulate = new Color(1f, 1f, 1f, _alpha / (float)AlphaMax);

        // Latch "at extreme" (alpha at maximum) as the dwell gate.
        // spec §3.1 "when dwell expires AND alpha at its maximum". CODE-CONFIRMED.
        if (!_panelFadedIn && _alpha >= AlphaMax)
            _panelFadedIn = true;

        // Accumulate dwell only after the panel has reached its alpha extreme.
        // spec §3.1 "when dwell expires AND panel fully faded in". CODE-CONFIRMED.
        if (_panelFadedIn)
            _dwellAccumMs += dtMs;

        if (!_panelFadedIn || !(_dwellAccumMs >= DwellMs)) return;

        // Dwell expired → advance to the next panel if not yet at 4. spec §3.1. CODE-CONFIRMED.
        // CAMPAIGN 16 correction: there is NO auto-finish after state 4. Panel 4 holds and loops
        // its alpha fade INDEFINITELY. The ONLY exit is an explicit skip (keyboard Enter/ESC/Space
        // or click on action-100 skip button). spec: intro_sequence.md §3.1.
        if (_slideshowState < SlideshowFrameCount)
        {
            _slideshowState++;

            // Swap texture for the new panel. spec §3.1 "paging four whole textures". CODE-CONFIRMED.
            _slideshowRect.Texture = _slideshowTextures[_slideshowState - 1];
            GD.Print(
                $"[Ui/Opening/OpeningWindow] Slideshow → state {_slideshowState} ({SlideshowPaths[_slideshowState - 1]}).");
        }
        else
        {
            // State 4 — hold and loop the alpha fade indefinitely. spec §3.1 (CAMPAIGN 16 correction).
            GD.Print("[Ui/Opening/OpeningWindow] Slideshow at panel 4 — looping alpha fade indefinitely. " +
                     "Waiting for explicit skip. spec: intro_sequence.md §3.1.");
        }

        // Reset dwell accumulator and alpha for the next fade cycle.
        // spec: intro_sequence.md §3.2/§3.4. CODE-CONFIRMED.
        _dwellAccumMs = 0.0;
        _panelFadedIn = false;
        _alpha        = AlphaMax;
        _alphaDir     = -1;
    }

    // =========================================================================
    // Transition
    // =========================================================================

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        GD.Print("[Ui/Opening/OpeningWindow] Emitting IntroFinished.");
        EmitSignal(SignalName.IntroFinished);
    }
}
