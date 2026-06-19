using Godot;
using MartialHeroes.Client.Godot.Screens;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Opening;

/// <summary>
/// Post-login intro Control (GameState 3): splash slideshow + scenario crawl ("red ribbon").
///
/// <para>Phase 4 holds and loops its alpha fade indefinitely; the SOLE exit is an explicit skip
/// (keyboard Enter/ESC/Space or skip button action-tag 100), which persists OPENNING/SKIP=1 and
/// emits <see cref="IntroFinishedEventHandler"/>. There is NO auto-finish after phase 4.</para>
///
/// spec: Docs/RE/specs/intro_sequence.md §3.1 (no auto-finish; skip is the sole exit).
/// </summary>
public sealed partial class OpeningWindow : Control
{
    // ── Signal ────────────────────────────────────────────────────────────────

    /// <summary>Emitted only when the player skips the intro (the sole exit).</summary>
    [Signal]
    public delegate void IntroFinishedEventHandler();

    // ── Constants — spec: frontend_layout_tables.md §6 ───────────────────────

    private const float ScrollSpeed = 30.0f; // design-px/second. spec §6.
    private const float ScrollStartDelayMs = 1000f; // startup gate. spec §6.
    private const float ScrollClamp = 1843.0f; // stop clamp. spec §6.
    private const float ScenarioW = 1024f;
    private const float ScenarioH = 2048f;
    private const float CanvasW = 1024f;

    private const float CanvasH = 768f;

    // Scenario starting Y: screenH − 200 = 568. spec §6.
    private const float ScenarioStartY = CanvasH - 200f;
    private const int SlideshowFrameCount = 4;
    private const double DwellMs = 17500.0; // ms per panel. spec §6.
    private const int AlphaMax = 250; // ceiling 0xFA, not 255. spec §6.

    // VFS asset paths. "openning" (double-n) is the exact VFS spelling. spec §6.
    private const string ScenarioPath = "data/ui/openning_scenario.dds";
    private const string MainWindowPath = "data/ui/mainwindow.dds";

    private static readonly string[] SlideshowPaths =
    [
        "data/ui/openning_001.dds",
        "data/ui/openning_002.dds",
        "data/ui/openning_003.dds",
        "data/ui/openning_004.dds",
    ];

    // Skip persistence. spec: frontend_layout_tables.md §6 "[OPENNING] SKIP=1".
    internal const string SkipCfgPath = "user://options.cfg";
    private const string SkipCfgSection = "OPENNING";
    private const string SkipCfgKey = "SKIP";

    // ── Injection (set by OpeningScene before SetScreen) ─────────────────────

    /// <summary>Shared HUD atlas library — loads DDS textures from the VFS.</summary>
    public HudAtlasLibrary? Atlas { get; set; }

    /// <summary>Shared audio node — fires the intro BGM.</summary>
    public FrontEndAudio? Audio { get; set; }

    // ── View state — NO domain state ─────────────────────────────────────────

    private ColorRect?
        _blackBackdrop; // full-screen black behind slideshow. spec: frontend_layout_tables.md §6 "alpha-over-(black-cleared)-back-buffer".

    private TextureRect? _scenarioRect;
    private float _scrollOffset;
    private float _scrollStartWait;
    private bool _scrollDone;

    private TextureRect? _slideshowRect;
    private int _slideshowState = 1; // 1..4 (1-based). spec §6.
    private readonly Texture2D?[] _slideshowTextures = new Texture2D?[SlideshowFrameCount];

    private double _dwellAccumMs;

    // spec: intro_sequence.md §3.4 — alpha seeded at 250 (max); first direction = fade-OUT (−1).
    // The spec seeds the constructor alpha field to 250; the port mirrors this.
    // spec: intro_sequence.md §3.2 — direction byte selects fade-in vs fade-out; field initialised to 250.
    private int _alpha = AlphaMax; // spec: intro_sequence.md §3.4 "alpha byte = 250 (maximum)".

    private int _alphaDir = -1; // spec: intro_sequence.md §3.2 "first phase is a fade-OUT".

    // _panelAtMax: true when alpha has reached its upper extreme (250). Dwell transition requires this AND dwell elapsed.
    // spec: intro_sequence.md §3.1 "when the dwell expires AND the panel has reached its alpha extreme (alpha at its maximum, 250)".
    private bool _panelAtMax = true; // initialised true because _alpha starts at 250. spec: intro_sequence.md §3.4.

    // Wheel scrub: a second independent crawl-Y, ±30/event, clamped 30..1833.
    // spec: frontend_layout_tables.md §6 "mouse-wheel/drag scrub path".
    private const float WheelScrubStep = 30f; // spec: frontend_layout_tables.md §6.
    private const float WheelScrubMin = 30f; // spec: frontend_layout_tables.md §6.
    private const float WheelScrubMax = 1833f; // spec: frontend_layout_tables.md §6.
    private float _wheelScrubOffset;

    private bool _finished;

    // ── Godot lifecycle ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        Size = new Vector2(CanvasW, CanvasH);
        MouseFilter = MouseFilterEnum.Stop;

        BuildUi();

        _scrollStartWait = ScrollStartDelayMs;
        _scrollOffset = 0f;
        _wheelScrubOffset = 0f;
        // spec: intro_sequence.md §3.4 — alpha seeded at 250 (max), first direction = fade-OUT (−1).
        _alpha = AlphaMax;
        _alphaDir = -1;
        _dwellAccumMs = 0.0;
        _panelAtMax = true; // alpha starts at max; dwell can tick immediately. spec: intro_sequence.md §3.4.
        _slideshowState = 1;

        // Fire the intro BGM once at scene start. spec: frontend_layout_tables.md §6/§7.
        Audio?.PlayIntroBgm();

        GD.Print("[OpeningWindow] Intro started: slideshow+crawl active, BGM 910061000. " +
                 "spec: frontend_layout_tables.md §6.");
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_finished) return;

        float dt = (float)delta;
        float dtMs = dt * 1000f;

        UpdateScroll(dtMs, dt);
        UpdateSlideshow(dtMs);
    }

    // ── Input — skip on Enter/ESC/Space; Page Up/Down + wheel scrub after crawl done ──
    // spec: frontend_layout_tables.md §6 "Enter(10)/ESC(27)/Space(32)".
    // spec: frontend_layout_tables.md §6 "mouse-wheel/drag scrub path steps ±30/event, clamped 30..1833".
    // spec: frontend_layout_tables.md §6 "Manual scrub … Page Up / Page Down".

    // Page Up/Down scrub constants. spec: frontend_layout_tables.md §6 "action 1004/1005 … ±30·dt_s".
    private const float PageScrubSpeed = 30.0f; // design-px/second. spec: frontend_layout_tables.md §6.
    private const float PageScrubFloor = 0f; // floor 0. spec: frontend_layout_tables.md §6 action 1004.
    private const float PageScrubCeil = 1843f; // ceil 1843. spec: frontend_layout_tables.md §6 action 1005.

    public override void _Input(InputEvent ev)
    {
        if (_finished) return;

        // Skip keys: Enter(10) / ESC(27) / Space(32).
        // spec: frontend_layout_tables.md §6 "Enter(10)/ESC(27)/Space(32)".
        // Use Keycode (not KeyLabel) — these are non-printable physical keys; KeyLabel is layout-dependent
        // and may not match for Enter/ESC on non-QWERTY layouts. Keycode is the reliable match.
        // spec: intro_sequence.md §2.5 "key codes 10 / 27 / 32 (Enter / ESC / Space)".
        bool skip = ev switch
        {
            InputEventKey { Pressed: true } k when
                k.Keycode == Key.Enter || k.PhysicalKeycode == Key.Enter => true,
            InputEventKey { Pressed: true } k when
                k.Keycode == Key.Escape || k.PhysicalKeycode == Key.Escape => true,
            InputEventKey { Pressed: true } k when
                k.Keycode == Key.Space || k.PhysicalKeycode == Key.Space => true,
            _ => false,
        };

        if (skip)
        {
            GD.Print("[OpeningWindow] Keyboard skip received.");
            PersistSkip();
            Finish();
            return;
        }

        // Page Up/Down scrub: active after crawl auto-completes (scroll done).
        // action 1004 (Page Up) → rewind −30·dt, floor 0.
        // action 1005 (Page Down) → forward +30·dt, ceil 1843.
        // spec: frontend_layout_tables.md §6 "Manual scrub … Page Up (DIK_PRIOR) / Page Down (DIK_NEXT)".
        // These are FIXED keyboard bindings (DirectInput DIK table — not layout-configurable).
        // DIK_PRIOR / DIK_NEXT are physical scan codes → use Keycode (not KeyLabel, which is
        // layout-dependent and unreliable for non-printable keys on non-QWERTY layouts).
        // spec: frontend_layout_tables.md §6 "FIXED keyboard bindings via the DirectInput DIK→app-code table".
        if (_scrollDone && ev is InputEventKey { Pressed: true } pageKey)
        {
            float dt = (float)GetProcessDeltaTime();
            float step = PageScrubSpeed * dt; // spec: frontend_layout_tables.md §6 "−30·dt_s / +30·dt_s"

            // Use Keycode for physical-key match (DIK_PRIOR/DIK_NEXT are scan-code bindings).
            // PhysicalKeycode fallback covers keyboards where Keycode resolves differently.
            // spec: frontend_layout_tables.md §6 "FIXED keyboard bindings via the DirectInput DIK→app-code table".
            Key physKey = pageKey.Keycode != Key.None ? pageKey.Keycode : pageKey.PhysicalKeycode;

            if (physKey == Key.Pageup)
            {
                // action 1004: rewind, floor 0. spec: frontend_layout_tables.md §6.
                _scrollOffset = Mathf.Max(_scrollOffset - step, PageScrubFloor);
                ApplyScrollPosition();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (physKey == Key.Pagedown)
            {
                // action 1005: forward, ceil 1843. spec: frontend_layout_tables.md §6.
                _scrollOffset = Mathf.Min(_scrollOffset + step, PageScrubCeil);
                ApplyScrollPosition();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Wheel scrub: active after crawl auto-completes (scroll done).
        // Operates on a second independent crawl-Y, clamped 30..1833.
        // spec: frontend_layout_tables.md §6 "second crawl-Y by ±30 per event, clamped 30..1833".
        if (_scrollDone && ev is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
        {
            float delta = mouseBtn.ButtonIndex switch
            {
                MouseButton.WheelUp => -WheelScrubStep, // wheel up → rewind (crawl upward in screen = lower offset)
                MouseButton.WheelDown => WheelScrubStep, // wheel down → forward
                _ => 0f,
            };

            if (delta != 0f)
            {
                _wheelScrubOffset = Mathf.Clamp(
                    _wheelScrubOffset + delta,
                    WheelScrubMin,
                    WheelScrubMax); // spec: frontend_layout_tables.md §6 clamp 30..1833.
                ApplyScrollPosition();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // ── Skip button handler (action 100, mainwindow.dds) ─────────────────────

    private void OnSkipPressed()
    {
        if (_finished) return;
        GD.Print("[OpeningWindow] Skip button (action 100) pressed.");
        PersistSkip();
        Finish();
    }

    // ── Skip persistence — write [OPENNING] SKIP=1 ───────────────────────────
    // spec: frontend_layout_tables.md §6.

    private static void PersistSkip()
    {
        var cfg = new ConfigFile();
        cfg.Load(SkipCfgPath);
        cfg.SetValue(SkipCfgSection, SkipCfgKey, 1);
        cfg.Save(SkipCfgPath);
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUi()
    {
        // Layer 0: full-screen black backdrop (behind the slideshow).
        // spec: frontend_layout_tables.md §6 "single-texture alpha-over-(black-cleared)-back-buffer".
        // Without a black rect behind the panel, a partially-faded quad exposes whatever was rendered below it
        // rather than black — the original D3D clear filled the back-buffer with black each frame.
        _blackBackdrop = new ColorRect
        {
            Name = "BlackBackdrop",
            Position = Vector2.Zero,
            Size = new Vector2(CanvasW, CanvasH),
            Color = new Color(0f, 0f, 0f, 1f), // spec: §6 "alpha-over-(black-cleared)-back-buffer".
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_blackBackdrop);

        // Layer 1: splash slideshow (full-screen quads). spec: frontend_layout_tables.md §6.
        // Modulate alpha starts at AlphaMax/AlphaMax = 1.0 (fully visible) because spec seeds alpha = 250.
        // spec: intro_sequence.md §3.4 "alpha byte = 250 (maximum)".
        _slideshowRect = new TextureRect
        {
            Name = "SlideshowRect",
            Position = Vector2.Zero,
            Size = new Vector2(CanvasW, CanvasH),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(1f, 1f, 1f, 1f), // alpha = 250/250 = 1.0. spec: intro_sequence.md §3.4.
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

        _slideshowRect.Texture = _slideshowTextures[0];

        // Layer 2: scenario crawl (1024×2048 positional Y-translate). spec §6.
        // Original increments +Y (DirectX Y-down); Godot port MUST invert sign → crawl scrolls upward.
        // StretchMode = Scale for 1:1 whole-texture blit per §0.10 (no UV scaling).
        _scenarioRect = new TextureRect
        {
            Name = "ScenarioRect",
            StretchMode = TextureRect.StretchModeEnum.Scale, // spec: frontend_layout_tables.md §0.10
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

        _scenarioRect.Position = new Vector2((CanvasW * 0.5f) - 512f, ScenarioStartY);
        _scenarioRect.Size = new Vector2(ScenarioW, ScenarioH);
        AddChild(_scenarioRect);

        // Layer 3: skip button (action 100) from mainwindow.dds.
        // dst (screenW−120, 10, 110×32); Normal/Hover src (761,165); Pressed src (634,165).
        // spec: frontend_layout_tables.md §6.
        AtlasTexture? skipNormal = Atlas?.SliceByPath(MainWindowPath, 761, 165, 110, 32);
        AtlasTexture? skipPressed = Atlas?.SliceByPath(MainWindowPath, 634, 165, 110, 32);

        // §0.12: 3-state arg order = Normal, Pressed, Hover. For this button Normal == Hover = (761,165),
        // Pressed = (634,165). spec: frontend_layout_tables.md §0.12, §6.
        var skipBtn = new TextureButton
        {
            Name = "SkipBtn",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -120f, // x = clientWidth − 120. spec: frontend_layout_tables.md §6.
            OffsetTop = 10f, // y = 10. spec: frontend_layout_tables.md §6.
            OffsetRight = -10f,
            OffsetBottom = 42f, // y + 32 px. spec: frontend_layout_tables.md §6.
            TextureNormal = skipNormal, // src (761,165) 110×32. spec: frontend_layout_tables.md §6.
            TexturePressed = skipPressed ?? skipNormal, // src (634,165) 110×32. spec §6.
            TextureHover = skipNormal, // Normal == Hover per §0.12. spec: frontend_layout_tables.md §0.12.
            StretchMode = TextureButton.StretchModeEnum.KeepAspect,
        };
        skipBtn.Pressed += OnSkipPressed;
        AddChild(skipBtn);
    }

    // ── Scenario crawl logic ──────────────────────────────────────────────────
    // spec: frontend_layout_tables.md §6 — positional Y-translate, Y decreases in Godot.

    private void UpdateScroll(float dtMs, float dt)
    {
        if (_scenarioRect is null) return;

        if (_scrollStartWait > 0f)
        {
            _scrollStartWait -= dtMs; // spec: frontend_layout_tables.md §6 "1000 ms startup gate".
            return;
        }

        if (!_scrollDone)
        {
            _scrollOffset += dt * ScrollSpeed; // spec: frontend_layout_tables.md §6 "30 units/second".

            if (_scrollOffset >= ScrollClamp)
            {
                _scrollOffset = ScrollClamp; // spec: frontend_layout_tables.md §6 "clamp at 1843".
                _scrollDone = true;
                GD.Print("[OpeningWindow] Scenario crawl reached clamp 1843 — stopped.");
            }
        }

        ApplyScrollPosition();
    }

    // Applies the correct scroll Y to the scenario rect.
    // When the wheel scrub is active (after crawl done), uses the second crawl-Y field.
    // spec: frontend_layout_tables.md §6 "a different position field" for wheel scrub.
    private void ApplyScrollPosition()
    {
        if (_scenarioRect is null) return;

        // Use wheel-scrub Y when active; otherwise use auto-crawl offset.
        float effectiveOffset = (_scrollDone && _wheelScrubOffset > 0f)
            ? _wheelScrubOffset
            : _scrollOffset;

        // Y(Godot) = ScenarioStartY − effectiveOffset (upward motion).
        // spec: frontend_layout_tables.md §6 "Godot Y-up port must invert the sign".
        float godotY = ScenarioStartY - effectiveOffset;
        _scenarioRect.Position = new Vector2((CanvasW * 0.5f) - 512f, godotY);
    }

    // ── Slideshow logic ───────────────────────────────────────────────────────
    // Phase 4 holds indefinitely; NO auto-finish. Skip is the sole exit.
    // spec: intro_sequence.md §3.1.
    //
    // Alpha model (spec: intro_sequence.md §3.2):
    //   - Single alpha byte, bounds 0..250, ±1 per frame via a direction-toggle.
    //   - Seeded at 250 (max); first direction is −1 (fade-OUT). spec: intro_sequence.md §3.4.
    //   - Phase transition requires BOTH: dwell elapsed AND alpha == 250 (upper extreme).
    //     spec: intro_sequence.md §3.1 "when the dwell expires AND the panel has reached its alpha extreme (250)".
    //   - Dwell accumulator runs concurrently every frame (not gated on alpha state).

    private void UpdateSlideshow(float dtMs)
    {
        if (_slideshowRect is null) return;

        // Step alpha ±1 per frame (frame-gated). spec: intro_sequence.md §3.2/§3.3 "±1 per rendered frame".
        _alpha += _alphaDir;

        if (_alpha >= AlphaMax)
        {
            _alpha = AlphaMax;
            _alphaDir = -1; // reverse direction: fade out. spec: intro_sequence.md §3.2 "direction-toggle".
            _panelAtMax = true; // reached upper extreme → dwell transition is now eligible.
        }
        else if (_alpha <= 0)
        {
            _alpha = 0;
            _alphaDir = 1; // reverse direction: fade in. spec: intro_sequence.md §3.2.
            _panelAtMax = false;
        }

        _slideshowRect.Modulate = new Color(1f, 1f, 1f, _alpha / (float)AlphaMax);

        // Dwell accumulates every frame concurrently with the alpha ramp. spec: intro_sequence.md §3.1.
        _dwellAccumMs += dtMs;

        // Transition condition: dwell elapsed AND alpha at upper extreme (250).
        // spec: intro_sequence.md §3.1 "when the dwell expires AND the panel has reached its alpha extreme".
        if (!(_dwellAccumMs >= DwellMs && _panelAtMax)) return;

        // Dwell expired — advance only if not yet at phase 4.
        // Phase 4 holds indefinitely (no auto-finish). spec: intro_sequence.md §3.1.
        if (_slideshowState < SlideshowFrameCount)
        {
            _slideshowState++;
            _slideshowRect.Texture = _slideshowTextures[_slideshowState - 1];
            _dwellAccumMs = 0.0;
            // Next phase seeds at 250 (max), direction −1 (fade-out). spec: intro_sequence.md §3.4.
            _alpha = AlphaMax;
            _alphaDir = -1;
            _panelAtMax = true;
            _slideshowRect.Modulate = new Color(1f, 1f, 1f, 1f); // alpha 250/250.
            GD.Print($"[OpeningWindow] Slideshow → phase {_slideshowState}. spec §6.");
        }
        // Phase 4: reset dwell accumulator so it holds without advancing. alpha ramp continues.
        // spec: intro_sequence.md §3.1 "phase 4 holds and loops its alpha fade indefinitely".
        else
        {
            _dwellAccumMs = 0.0;
            // alpha continues its bidirectional ramp — do not reset it here.
        }
    }

    // ── Transition ────────────────────────────────────────────────────────────

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        GD.Print("[OpeningWindow] Emitting IntroFinished.");
        EmitSignal(SignalName.IntroFinished);
    }
}