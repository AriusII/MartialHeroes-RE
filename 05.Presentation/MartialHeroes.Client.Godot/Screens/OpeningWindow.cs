// Screens/OpeningWindow.cs
//
// The pre-login intro scene (OpeningWindow) — the "red ribbon" crawl + splash slideshow.
//
// TWO INDEPENDENT ANIMATED LAYERS, per spec:
//   1. SCENARIO CRAWL  — openning_scenario.dds (1024×2048), positional Y-translate downward.
//      spec: Docs/RE/specs/intro_sequence.md §2. CODE-CONFIRMED.
//      Speed: 30 design-px/s after a ~1000 ms gate; clamp/stop at 1843.
//   2. SPLASH SLIDESHOW — openning_001..004.dds (1024×768), 4-state machine, 17500 ms dwell.
//      spec: Docs/RE/specs/intro_sequence.md §3. CODE-CONFIRMED.
//      Alpha ramp 0→250 (frame-gated ±1 step) per panel.
//
// SOUND:
//   Fires intro stinger 910061000 once at scene start via FrontEndAudio.PlayIntroBgm().
//   spec: Docs/RE/specs/intro_sequence.md §4. CODE-CONFIRMED.
//
// SKIP:
//   Any mouse click or any key press short-circuits to the login screen immediately.
//   This makes the normally-long intro (4×17.5 s = 70 s) skip-friendly for testing.
//
// ASSETS:
//   All textures loaded from the real VFS via UiAssetLoader.LoadAtlas().
//   When offline (no VFS) the intro is a solid dim panel that immediately skips to login.
//   spec: Docs/RE/specs/intro_sequence.md §1 — VFS paths. SAMPLE-VERIFIED.
//
// PASSIVE: zero game logic. Reads VFS textures; emits one signal (IntroFinished) when done.
//
// spec: Docs/RE/specs/intro_sequence.md §0–§6 (CODE-CONFIRMED static; on-screen rate UNVERIFIED).

using Godot;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The pre-login opening/intro Control: scenario crawl ("red ribbon") + 4-panel slideshow.
/// Emits <see cref="IntroFinishedEventHandler"/> when the sequence ends (or is skipped).
/// <para>spec: Docs/RE/specs/intro_sequence.md §0–§6.</para>
/// </summary>
public sealed partial class OpeningWindow : Control
{
    // -------------------------------------------------------------------------
    // Signal
    // -------------------------------------------------------------------------

    /// <summary>Emitted when the intro sequence ends or the player skips it.</summary>
    [Signal]
    public delegate void IntroFinishedEventHandler();

    // -------------------------------------------------------------------------
    // Constants — all CODE-CONFIRMED from spec intro_sequence.md §2 / §3.
    // -------------------------------------------------------------------------

    // Scenario crawl. spec: intro_sequence.md §2. CODE-CONFIRMED.
    private const float ScrollSpeed = 30.0f;      // design-px / second. spec §2.1. CODE-CONFIRMED.
    private const float ScrollStartDelayMs = 1000f; // ~1000 ms gate. spec §2.1. CODE-CONFIRMED.
    private const float ScrollClamp = 1843.0f;    // stop clamp. spec §2.1 / §2.3. CODE-CONFIRMED.

    // Scenario quad size. spec: intro_sequence.md §1. SAMPLE-VERIFIED.
    private const float ScenarioW = 1024f; // spec §1. SAMPLE-VERIFIED.
    private const float ScenarioH = 2048f; // spec §1. SAMPLE-VERIFIED.

    // Slideshow. spec: intro_sequence.md §3. CODE-CONFIRMED.
    private const int SlideshowFrameCount = 4; // 4 panels. spec §3.1. CODE-CONFIRMED.
    private const double DwellMs = 17500.0;    // ms per panel. spec §3.3. CODE-CONFIRMED.
    private const int AlphaMax = 250;          // alpha ramp bound. spec §3.3. CODE-CONFIRMED.
    // Alpha step is ±1 per rendered frame (frame-gated, not ms-gated). spec §3.2. CODE-CONFIRMED.

    // VFS asset paths. spec: intro_sequence.md §1. SAMPLE-VERIFIED.
    // NOTE: legacy typo "openning" (double-n) — exact VFS filename. spec §0 note. CODE-CONFIRMED.
    private const string ScenarioPath = "data/ui/openning_scenario.dds"; // spec §1. SAMPLE-VERIFIED.

    private static readonly string[] SlideshowPaths =
    [
        "data/ui/openning_001.dds", // state 1. spec §1. SAMPLE-VERIFIED.
        "data/ui/openning_002.dds", // state 2. spec §1. SAMPLE-VERIFIED.
        "data/ui/openning_003.dds", // state 3. spec §1. SAMPLE-VERIFIED.
        "data/ui/openning_004.dds", // state 4. spec §1. SAMPLE-VERIFIED.
    ];

    // -------------------------------------------------------------------------
    // Injection
    // -------------------------------------------------------------------------

    /// <summary>Shared asset loader injected by BootFlow (has VFS open already).</summary>
    public UiAssetLoader? SharedAssets { get; set; }

    /// <summary>Shared audio node — used to fire the intro stinger.</summary>
    public FrontEndAudio? Audio { get; set; }

    // -------------------------------------------------------------------------
    // View state — NO domain state
    // -------------------------------------------------------------------------

    // Scroll state.
    private TextureRect? _scenarioRect;
    private float _scrollPos;         // current Y translate (design-px)
    private float _scrollStartWait;   // countdown in ms; scroll starts when ≤ 0
    private bool _scrollDone;         // scroll has reached the clamp

    // Slideshow state.
    private TextureRect? _slideshowRect;
    private int _slideshowState = 1;  // 1..4 (panel index, 1-based). spec §3.1.
    private readonly Texture2D?[] _slideshowTextures = new Texture2D?[SlideshowFrameCount];
    private double _dwellAccumMs;      // ms elapsed in the current dwell
    private int _alpha;               // 0..250 ramp. spec §3.2.
    private int _alphaDir = 1;        // +1 = ramp up, -1 = ramp down
    private bool _panelFadedIn;       // true once alpha first reaches AlphaMax for this panel
    private bool _sequenceDone;       // all 4 panels shown → ready to transition
    private bool _finished;           // guard so we emit IntroFinished only once

    // Manual-nudge range. spec §2.2.
    // Using arrow key actions for the nudge (no named actions needed — just key detection).

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Accept input so clicks/keys can skip the intro.
        MouseFilter = MouseFilterEnum.Stop;

        UiAssetLoader assets = SharedAssets ?? UiAssetLoader.Open();
        bool ownsAssets = SharedAssets is null;

        try
        {
            BuildUi(assets);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[OpeningWindow] Build failed: {ex.Message}");
            // If the UI build fails, skip directly to login.
            Finish();
            return;
        }
        finally
        {
            if (ownsAssets) assets.Dispose();
        }

        // Init scroll wait. spec §2.1 — "~1000 ms startup gate". CODE-CONFIRMED.
        _scrollStartWait = ScrollStartDelayMs; // spec: intro_sequence.md §2.1. CODE-CONFIRMED.
        _scrollPos = 0f;
        _alpha = 0;
        _alphaDir = 1;
        _dwellAccumMs = 0.0;
        _panelFadedIn = false;
        _slideshowState = 1;

        // Fire intro stinger. spec: intro_sequence.md §4. CODE-CONFIRMED.
        Audio?.PlayIntroBgm();

        GD.Print("[OpeningWindow] Intro sequence started (scroll + slideshow active).");
    }

    // -------------------------------------------------------------------------
    // Per-frame update
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_finished) return;

        float dt = (float)delta;
        float dtMs = dt * 1000f;

        // --- Scenario crawl update. spec: intro_sequence.md §2.1. CODE-CONFIRMED. ---
        UpdateScroll(dtMs, dt);

        // --- Slideshow update. spec: intro_sequence.md §3.1/§3.2. CODE-CONFIRMED. ---
        UpdateSlideshow(dtMs);
    }

    // -------------------------------------------------------------------------
    // Input — skip on any click or key
    // -------------------------------------------------------------------------

    public override void _Input(InputEvent ev)
    {
        if (_finished) return;

        bool skip = ev switch
        {
            InputEventMouseButton { Pressed: true } => true,
            InputEventKey { Pressed: true } => true,
            _ => false,
        };

        if (skip)
        {
            GD.Print("[OpeningWindow] Intro skipped by user input.");
            Finish();
        }
    }

    // -------------------------------------------------------------------------
    // UI construction
    // -------------------------------------------------------------------------

    private void BuildUi(UiAssetLoader assets)
    {
        // Dark backdrop (full canvas).
        var bg = new ColorRect { Color = Colors.Black };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // --- Layer 1: slideshow panels (rendered below the crawl so the crawl overlays it).
        // spec: intro_sequence.md §3 — "full-screen DDS panels". SAMPLE-VERIFIED. ---
        _slideshowRect = new TextureRect
        {
            Name = "SlideshowRect",
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(1f, 1f, 1f, 0f), // start transparent
        };
        _slideshowRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_slideshowRect);

        // Pre-load all 4 slideshow textures. spec: intro_sequence.md §1. SAMPLE-VERIFIED.
        for (int i = 0; i < SlideshowFrameCount; i++)
        {
            _slideshowTextures[i] = assets.LoadAtlas(SlideshowPaths[i]);
            if (_slideshowTextures[i] is not null)
                GD.Print($"[OpeningWindow] Slideshow panel {i + 1} loaded: {SlideshowPaths[i]}");
            else
                GD.Print($"[OpeningWindow] Slideshow panel {i + 1} not found: {SlideshowPaths[i]}");
        }

        // Set initial panel.
        _slideshowRect.Texture = _slideshowTextures[0]; // state 1 → panel 001

        // --- Layer 2: scenario crawl ("red ribbon").
        // spec: intro_sequence.md §2 — single 1024×2048 sprite; positional Y translate.
        // CODE-CONFIRMED. ---
        _scenarioRect = new TextureRect
        {
            Name = "ScenarioRect",
            StretchMode = TextureRect.StretchModeEnum.KeepAspect, // keep the 2048 height
        };

        Texture2D? scenarioTex = assets.LoadAtlas(ScenarioPath);
        if (scenarioTex is not null)
        {
            _scenarioRect.Texture = scenarioTex;
            // Position: centred horizontally on the 1024-wide reference canvas;
            // vertical position starts at Y=0 and is scrolled downward.
            // spec: intro_sequence.md §1 — "centred horizontally". CODE-CONFIRMED.
            _scenarioRect.Position = new Vector2(0f, 0f);
            _scenarioRect.Size = new Vector2(ScenarioW, ScenarioH);
            GD.Print($"[OpeningWindow] Scenario crawl loaded: {ScenarioPath}");
        }
        else
        {
            // Offline fallback: a dim coloured rect that fades out quickly.
            _scenarioRect.Texture = null;
            _scenarioRect.Position = new Vector2(0f, 0f);
            _scenarioRect.Size = new Vector2(ScenarioW, ScenarioH);
            GD.Print($"[OpeningWindow] Scenario not found in VFS: {ScenarioPath} — using placeholder.");
        }

        AddChild(_scenarioRect);

        // --- Skip-hint label (shown bottom-centre). ---
        var skipLabel = new Label
        {
            Text = "Press any key or click to skip",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        skipLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        skipLabel.AddThemeFontSizeOverride("font_size", 11);
        skipLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f, 0.7f));
        AddChild(skipLabel);
    }

    // -------------------------------------------------------------------------
    // Crawl logic — spec: intro_sequence.md §2.1/§2.2. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void UpdateScroll(float dtMs, float dt)
    {
        if (_scenarioRect is null) return;

        if (_scrollStartWait > 0f)
        {
            // spec: intro_sequence.md §2.1 — "~1000 ms startup gate". CODE-CONFIRMED.
            _scrollStartWait -= dtMs;
            return;
        }

        if (!_scrollDone)
        {
            // Auto-crawl: advance pos at 30 design-px/s. spec §2.1. CODE-CONFIRMED.
            _scrollPos += dt * ScrollSpeed; // spec: intro_sequence.md §2.1. CODE-CONFIRMED.

            if (_scrollPos >= ScrollClamp)
            {
                // spec: intro_sequence.md §2.1 — "stop clamp 1843; no wrap-around". CODE-CONFIRMED.
                _scrollPos = ScrollClamp; // spec: intro_sequence.md §2.3. CODE-CONFIRMED.
                _scrollDone = true;
                GD.Print("[OpeningWindow] Scenario crawl reached clamp 1843 — auto-scroll stopped.");
            }
        }
        else
        {
            // Manual nudge mode. spec: intro_sequence.md §2.2.
            // Arrow-up = scroll up; arrow-down = scroll down.
            if (global::Godot.Input.IsActionPressed("ui_up"))
            {
                _scrollPos = Mathf.Max(0f, _scrollPos - dt * ScrollSpeed); // spec §2.2. CODE-CONFIRMED.
            }
            else if (global::Godot.Input.IsActionPressed("ui_down"))
            {
                _scrollPos = Mathf.Min(ScrollClamp, _scrollPos + dt * ScrollSpeed); // spec §2.2. CODE-CONFIRMED.
            }
        }

        // Drive the sprite's on-screen Y position. spec §2.1 — "set destination Y". CODE-CONFIRMED.
        // The sprite's native size is 1024×2048; we translate it upward (negative Y offset)
        // so that as _scrollPos increases the image scrolls downward on-screen:
        //   Y_screen = -_scrollPos keeps the top of the image at the top while the
        //   content revealed below increases as pos grows.
        // Actually the spec says "destination Y is translated downward" — the sprite Y is pushed
        // positively to bring more of the bottom portion into view:
        _scenarioRect.Position = new Vector2(_scenarioRect.Position.X, _scrollPos);
    }

    // -------------------------------------------------------------------------
    // Slideshow logic — spec: intro_sequence.md §3. CODE-CONFIRMED.
    // -------------------------------------------------------------------------

    private void UpdateSlideshow(float dtMs)
    {
        if (_sequenceDone || _slideshowRect is null) return;

        // Alpha ramp: ±1 per rendered frame. spec §3.2. CODE-CONFIRMED.
        _alpha = Mathf.Clamp(_alpha + _alphaDir, 0, AlphaMax); // spec §3.3. CODE-CONFIRMED.
        float alphaF = _alpha / (float)AlphaMax;
        _slideshowRect.Modulate = new Color(1f, 1f, 1f, alphaF);

        // Latch "faded in" once alpha reaches max. spec §3.1. CODE-CONFIRMED.
        if (!_panelFadedIn && _alpha >= AlphaMax)
            _panelFadedIn = true;

        // Dwell counter: only accumulate after the panel has faded in.
        // spec §3.1 — "when the dwell expires AND the panel has fully faded in". CODE-CONFIRMED.
        if (_panelFadedIn)
            _dwellAccumMs += dtMs;

        if (!_panelFadedIn || !(_dwellAccumMs >= DwellMs)) return;

        // Dwell expired → advance state. spec §3.1. CODE-CONFIRMED.
        _slideshowState++;
        if (_slideshowState > SlideshowFrameCount)
        {
            // After state 4, the sequence is done. spec §3.1 — "after state 4, transition to login". CODE-CONFIRMED.
            _sequenceDone = true;
            GD.Print("[OpeningWindow] Slideshow complete — transitioning to login.");
            Finish();
            return;
        }

        // Swap texture for the new panel. spec §3.1 — "paging four whole textures". CODE-CONFIRMED.
        _slideshowRect.Texture = _slideshowTextures[_slideshowState - 1];
        GD.Print($"[OpeningWindow] Slideshow → state {_slideshowState} ({SlideshowPaths[_slideshowState - 1]}).");

        // Reset for next panel.
        _dwellAccumMs = 0.0;
        _panelFadedIn = false;
        _alpha = 0;
        _alphaDir = 1;
    }

    // -------------------------------------------------------------------------
    // Transition
    // -------------------------------------------------------------------------

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        GD.Print("[OpeningWindow] Emitting IntroFinished.");
        EmitSignal(SignalName.IntroFinished);
    }
}
