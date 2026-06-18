// Ui/Scenes/Opening/OpeningWindow.cs
//
// State-3 Opening intro window — immediate-mode ortho quads.
//
// SPEC (authoritative): Docs/RE/specs/frontend_layout_tables.md §6 (supersedes intro_sequence.md
// and earlier frontend_scenes.md notes where they disagree).
//
// TWO LAYERS:
//   1. SPLASH SLIDESHOW — 4 full-screen quads openning_001..004.dds, 17500 ms dwell each.
//      Alpha ramps ±1/frame up to 250 (0xFA) — fade-out then swap, NOT a cross-blend.
//      Phase 4 auto-exits after its dwell (arming flag → final fade 0→250 → advance to Select).
//      spec: frontend_layout_tables.md §6.
//
//   2. CREDIT CRAWL — openning_scenario.dds (1024×2048), positional Y translate.
//      Starts at Y = screenH − 200 = 568 (bottom area), 1000 ms gate, then moves UP at 30 px/s.
//      The original increments +Y (DirectX Y-down); Godot (Y-up) MUST invert the sign so the
//      crawl scrolls upward. Stop clamp at offset 1843 (legacy crawlY value).
//      spec: Docs/RE/specs/frontend_layout_tables.md §6
//            "code increments +Y (DirectX Y-down); Godot Y-up port must invert the sign".
//
// SKIP (any exit):
//   Keyboard Enter(10)/ESC(27)/Space(32) OR skip button (action 100) at
//   (screenW−120,10,110×32) on mainwindow.dds, src Normal/Hover(761,165)/Pressed(634,165).
//   Persists [OPENNING] SKIP=1 → advances to Select (4).
//   spec: frontend_layout_tables.md §6.
//
// AUTO-EXIT:
//   After phase 4 dwell expires, an arming flag triggers a final fade 0→250 then advances
//   to Select (4). spec: frontend_layout_tables.md §6 "arming flag → final fade 0→250".
//
// AUDIO:
//   Looped BGM 910061000, started at scene build, stopped on teardown.
//   spec: frontend_layout_tables.md §6/§7.
//
// ASSETS: data/ui/openning_001..004.dds, openning_scenario.dds, mainwindow.dds.
//   "openning" (double-n) is the exact VFS spelling. spec §6.
//
// PASSIVE: zero game logic. Emits IntroFinished when skipping or auto-exiting.

using Godot;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Opening;

/// <summary>
/// Post-login intro Control (GameState 3): splash slideshow + scenario crawl ("red ribbon").
///
/// <para>Emits <see cref="IntroFinishedEventHandler"/> when the player skips OR when phase 4
/// auto-exits after its dwell completes.</para>
///
/// spec: Docs/RE/specs/frontend_layout_tables.md §6.
/// </summary>
public sealed partial class OpeningWindow : Control
{
    // =========================================================================
    // Signal
    // =========================================================================

    /// <summary>Emitted when the intro ends (skip or auto-exit).</summary>
    [Signal]
    public delegate void IntroFinishedEventHandler();

    // =========================================================================
    // Constants — spec: frontend_layout_tables.md §6.
    // =========================================================================

    // Scenario crawl. spec: frontend_layout_tables.md §6.
    private const float ScrollSpeed = 30.0f; // design-px/second (wall-clock). spec §6.
    private const float ScrollStartDelayMs = 1000f; // 1000 ms startup gate. spec §6.
    private const float ScrollClamp = 1843.0f; // stop after offset 1843 legacy units. spec §6.

    // Scenario quad size. spec: frontend_layout_tables.md §6 "1024×2048".
    private const float ScenarioW = 1024f;
    private const float ScenarioH = 2048f;

    // Design canvas (top-left origin, +Y down in Godot).
    // spec: frontend_layout_tables.md §1 "reference canvas 1024×768". CODE-CONFIRMED.
    private const float CanvasW = 1024f;
    private const float CanvasH = 768f;

    // Scenario starting Y (Godot canvas space):
    //   Original: starts at Y = screenH − 200 = 568 (DirectX Y-down, near bottom).
    //   Godot: same starting Y = 568, but crawl must DECREASE Y (upward).
    // spec: Docs/RE/specs/frontend_layout_tables.md §6
    //       "starting Y = screenH − 200"; "Godot Y-up port must invert the sign".
    private const float ScenarioStartY = CanvasH - 200f; // 568. spec §6.

    // Slideshow. spec: frontend_layout_tables.md §6.
    private const int SlideshowFrameCount = 4; // four panels. spec §6.
    private const double DwellMs = 17500.0; // ms per panel. spec §6 "17 500 ms". CODE-CONFIRMED.
    private const int AlphaMax = 250; // alpha ceiling (0xFA, not 255). spec §6. CODE-CONFIRMED.

    // VFS asset paths. spec: frontend_layout_tables.md §6.
    // "openning" (double-n) is the exact VFS spelling. CODE-CONFIRMED.
    private const string ScenarioPath = "data/ui/openning_scenario.dds"; // spec §6.
    private const string MainWindowPath = "data/ui/mainwindow.dds"; // spec §6.

    private static readonly string[] SlideshowPaths =
    [
        "data/ui/openning_001.dds", // phase 1. spec §6.
        "data/ui/openning_002.dds", // phase 2. spec §6.
        "data/ui/openning_003.dds", // phase 3. spec §6.
        "data/ui/openning_004.dds", // phase 4. spec §6.
    ];

    // Skip persistence. spec: frontend_layout_tables.md §6 "[OPENNING] SKIP=1".
    internal const string SkipCfgPath = "user://options.cfg"; // spec §6.
    private const string SkipCfgSection = "OPENNING"; // spec §6.
    private const string SkipCfgKey = "SKIP"; // spec §6.

    // =========================================================================
    // Injection — set by OpeningScene before SetScreen
    // =========================================================================

    /// <summary>Shared HUD atlas library — loads DDS textures from the VFS.</summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>Shared audio node — fires the intro BGM.</summary>
    public FrontEndAudio? Audio { get; set; }

    // =========================================================================
    // View state — NO domain state
    // =========================================================================

    // Scenario crawl.
    private TextureRect? _scenarioRect;
    private float _scrollOffset; // legacy crawlY offset (0 → 1843); scroll offset from start
    private float _scrollStartWait; // countdown ms; crawl starts when ≤ 0
    private bool _scrollDone;

    // Slideshow.
    private TextureRect? _slideshowRect;
    private int _slideshowState = 1; // 1..4 (1-based). spec §6.
    private readonly Texture2D?[] _slideshowTextures = new Texture2D?[SlideshowFrameCount];
    private double _dwellAccumMs;
    private int _alpha;
    private int _alphaDir = 1; // ramp 0→250 on enter. spec §6.
    private bool _panelFadedIn;

    // Auto-exit after phase 4. spec: frontend_layout_tables.md §6 "arming flag".
    private bool _phase4Armed; // set when phase 4 dwell expires → trigger final fade-in
    private bool _phase4FadeDone; // latched after the final 0→250 completes

    // Global finish guard.
    private bool _finished;

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        Size = new Vector2(CanvasW, CanvasH);
        MouseFilter = MouseFilterEnum.Stop;

        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OpeningWindow] Build failed: {ex.Message} — skipping intro.");
            Finish();
            return;
        }

        _scrollStartWait = ScrollStartDelayMs; // spec §6 "1000 ms startup gate".
        _scrollOffset = 0f;
        _alpha = 0;
        _alphaDir = 1;
        _dwellAccumMs = 0.0;
        _panelFadedIn = false;
        _phase4Armed = false;
        _phase4FadeDone = false;
        _slideshowState = 1;

        // Fire the intro BGM once at scene start.
        // spec: frontend_layout_tables.md §6/§7 "BGM 910061000 looped, started at scene build".
        Audio?.PlayIntroBgm();

        GD.Print("[OpeningWindow] Intro started: slideshow+crawl active, BGM 910061000. " +
                 "spec: frontend_layout_tables.md §6.");

        Dev.LayoutDump.DumpDeferred(this, "OPENING");
    }

    // =========================================================================
    // Per-frame update
    // =========================================================================

    public override void _Process(double delta)
    {
        if (_finished) return;

        float dt = (float)delta;
        float dtMs = dt * 1000f;

        UpdateScroll(dtMs, dt);
        UpdateSlideshow(dtMs);
    }

    // =========================================================================
    // Input — skip on Enter/ESC/Space (keyboard).
    // spec: Docs/RE/specs/frontend_layout_tables.md §6 "Enter(10)/ESC(27)/Space(32)". CODE-CONFIRMED.
    // =========================================================================

    public override void _Input(InputEvent ev)
    {
        if (_finished) return;

        bool skip = ev switch
        {
            InputEventKey { Pressed: true, KeyLabel: Key.Enter } => true,
            InputEventKey { Pressed: true, KeyLabel: Key.Escape } => true,
            InputEventKey { Pressed: true, KeyLabel: Key.Space } => true,
            _ => false,
        };

        if (skip)
        {
            GD.Print("[OpeningWindow] Keyboard skip received.");
            PersistSkip();
            Finish();
        }
    }

    // =========================================================================
    // Skip button handler (action 100, mainwindow.dds).
    // spec: Docs/RE/specs/frontend_layout_tables.md §6.
    // =========================================================================

    private void OnSkipPressed()
    {
        if (_finished) return;
        GD.Print("[OpeningWindow] Skip button (action 100) pressed.");
        PersistSkip();
        Finish();
    }

    // =========================================================================
    // Skip persistence — write [OPENNING] SKIP=1.
    // spec: Docs/RE/specs/frontend_layout_tables.md §6.
    // =========================================================================

    private static void PersistSkip()
    {
        var cfg = new ConfigFile();
        cfg.Load(SkipCfgPath);
        cfg.SetValue(SkipCfgSection, SkipCfgKey, 1);
        cfg.Save(SkipCfgPath);
    }

    // =========================================================================
    // UI construction
    // =========================================================================

    private void BuildUi()
    {
        // ── Layer 1: splash slideshow (full-screen quads below the crawl) ──────
        // spec: frontend_layout_tables.md §6 "4 full-screen quads".
        _slideshowRect = new TextureRect
        {
            Name = "SlideshowRect",
            Position = Vector2.Zero,
            Size = new Vector2(CanvasW, CanvasH),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(1f, 1f, 1f, 0f), // start transparent; alpha ramp drives fade
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_slideshowRect);

        for (int i = 0; i < SlideshowFrameCount; i++)
        {
            _slideshowTextures[i] = Atlas?.GetByPath(SlideshowPaths[i]);
            if (_slideshowTextures[i] is not null)
                GD.Print($"[OpeningWindow] Panel {i + 1}: {SlideshowPaths[i]}");
            else
                GD.PrintErr($"[OpeningWindow] Panel {i + 1} absent: {SlideshowPaths[i]}");
        }

        _slideshowRect.Texture = _slideshowTextures[0]; // phase 1. spec §6.

        // ── Layer 2: scenario crawl (1024×2048 positional Y-translate) ─────────
        // Starting Y (Godot canvas): screenH − 200 = 568.
        // Crawl scrolls UPWARD (Y decreases) as _scrollOffset grows.
        // spec: frontend_layout_tables.md §6
        //       "starting Y = screenH − 200; Godot Y-up port must invert the sign".
        _scenarioRect = new TextureRect
        {
            Name = "ScenarioRect",
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        Texture2D? scenarioTex = Atlas?.GetByPath(ScenarioPath);
        if (scenarioTex is not null)
        {
            _scenarioRect.Texture = scenarioTex;
            GD.Print($"[OpeningWindow] Scenario crawl loaded: {ScenarioPath}");
        }
        else
        {
            GD.PrintErr($"[OpeningWindow] Scenario crawl absent: {ScenarioPath}");
        }

        // Place at starting position: x = screenW/2 − 512, y = screenH − 200 = 568.
        // spec: frontend_layout_tables.md §6.
        _scenarioRect.Position = new Vector2((CanvasW * 0.5f) - 512f, ScenarioStartY);
        _scenarioRect.Size = new Vector2(ScenarioW, ScenarioH);
        AddChild(_scenarioRect);

        // ── Layer 3: Skip button (action 100) from mainwindow.dds ──────────────
        // dst (screenW−120, 10, 110×32); Normal/Hover src (761,165); Pressed src (634,165).
        // spec: frontend_layout_tables.md §6.
        AtlasTexture? skipNormal = Atlas?.SliceByPath(MainWindowPath, 761, 165, 110, 32);
        AtlasTexture? skipPressed = Atlas?.SliceByPath(MainWindowPath, 634, 165, 110, 32);

        if (skipNormal is null)
        {
            GD.PrintErr("[OpeningWindow] mainwindow.dds skip-button null — skip button absent (offline).");
        }
        else
        {
            // Anchor to top-right: x = clientWidth − 120, y = 10. spec §6.
            var skipBtn = new TextureButton
            {
                Name = "SkipBtn",
                AnchorLeft = 1f,
                AnchorRight = 1f,
                AnchorTop = 0f,
                AnchorBottom = 0f,
                OffsetLeft = -120f, // x = clientWidth − 120. spec §6.
                OffsetTop = 10f, // y = 10. spec §6.
                OffsetRight = -10f, // right edge (110 px wide from OffsetLeft)
                OffsetBottom = 42f, // y + 32 px. spec §6 "32 px tall".
                TextureNormal = skipNormal,
                TexturePressed = skipPressed ?? skipNormal,
                StretchMode = TextureButton.StretchModeEnum.KeepAspect,
            };
            skipBtn.Pressed += OnSkipPressed;
            AddChild(skipBtn);
        }
    }

    // =========================================================================
    // Scenario crawl logic
    // spec: frontend_layout_tables.md §6 — positional Y-translate, Y decreases in Godot.
    // =========================================================================

    private void UpdateScroll(float dtMs, float dt)
    {
        if (_scenarioRect is null) return;

        if (_scrollStartWait > 0f)
        {
            // spec §6 "1000 ms startup gate". CODE-CONFIRMED.
            _scrollStartWait -= dtMs;
            return;
        }

        if (!_scrollDone)
        {
            // Original increments crawlY by 30/s (DirectX +Y = down).
            // Godot: we track the offset and SUBTRACT from starting Y so it goes UP.
            // spec: frontend_layout_tables.md §6 "Godot Y-up port must invert the sign".
            _scrollOffset += dt * ScrollSpeed; // spec §6 "30 units/second". CODE-CONFIRMED.

            if (_scrollOffset >= ScrollClamp)
            {
                _scrollOffset = ScrollClamp; // spec §6 "clamp at 1843". CODE-CONFIRMED.
                _scrollDone = true;
                GD.Print("[OpeningWindow] Scenario crawl reached clamp 1843 — stopped.");
            }
        }

        // Y(Godot) = ScenarioStartY − _scrollOffset  (upward motion).
        // spec: frontend_layout_tables.md §6 "Godot Y-up port must invert the sign".
        float godotY = ScenarioStartY - _scrollOffset;
        _scenarioRect.Position = new Vector2((CanvasW * 0.5f) - 512f, godotY);
    }

    // =========================================================================
    // Slideshow logic
    // spec: frontend_layout_tables.md §6 "±1/frame up to 250; fade-out then swap".
    // =========================================================================

    private void UpdateSlideshow(float dtMs)
    {
        if (_slideshowRect is null) return;

        // Phase 4 auto-exit: arming flag set → run final fade-in 0→250 then finish.
        // spec: frontend_layout_tables.md §6 "arming flag triggers final fade 0→250 → Select(4)".
        if (_phase4Armed && !_phase4FadeDone)
        {
            _alpha += _alphaDir;
            if (_alpha >= AlphaMax)
            {
                _alpha = AlphaMax;
                _phase4FadeDone = true;
                _slideshowRect.Modulate = new Color(1f, 1f, 1f, _alpha / (float)AlphaMax);
                GD.Print("[OpeningWindow] Phase 4 auto-exit: final fade complete → Finish(). spec §6.");
                Finish(); // auto-advance to Select(4). spec §6.
                return;
            }

            _slideshowRect.Modulate = new Color(1f, 1f, 1f, _alpha / (float)AlphaMax);
            return;
        }

        // Normal alpha ramp: 0→250 per frame on fade-in. spec §6 "±1 per frame". CODE-CONFIRMED.
        if (!_panelFadedIn)
        {
            _alpha += _alphaDir;
            if (_alpha >= AlphaMax)
            {
                _alpha = AlphaMax;
                _panelFadedIn = true;
            }

            _slideshowRect.Modulate = new Color(1f, 1f, 1f, _alpha / (float)AlphaMax);
            return;
        }

        _slideshowRect.Modulate = new Color(1f, 1f, 1f, _alpha / (float)AlphaMax);

        _dwellAccumMs += dtMs;

        if (!(_dwellAccumMs >= DwellMs)) return;

        // Dwell expired.
        if (_slideshowState < SlideshowFrameCount)
        {
            // Advance to the next panel. spec §6.
            _slideshowState++;
            _slideshowRect.Texture = _slideshowTextures[_slideshowState - 1];
            _dwellAccumMs = 0.0;
            _panelFadedIn = false;
            _alpha = 0;
            _alphaDir = 1;
            _slideshowRect.Modulate = new Color(1f, 1f, 1f, 0f);
            GD.Print(
                $"[OpeningWindow] Slideshow → phase {_slideshowState} ({SlideshowPaths[_slideshowState - 1]}). spec §6.");
        }
        else
        {
            // Phase 4 dwell expired → arm auto-exit. spec §6 "arming flag → final fade 0→250".
            if (!_phase4Armed)
            {
                _phase4Armed = true;
                _alpha = 0;
                _alphaDir = 1;
                _slideshowRect.Modulate = new Color(1f, 1f, 1f, 0f);
                GD.Print("[OpeningWindow] Phase 4 dwell done — armed auto-exit (final fade 0→250). spec §6.");
            }
        }
    }

    // =========================================================================
    // Transition
    // =========================================================================

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        GD.Print("[OpeningWindow] Emitting IntroFinished.");
        EmitSignal(SignalName.IntroFinished);
    }
}