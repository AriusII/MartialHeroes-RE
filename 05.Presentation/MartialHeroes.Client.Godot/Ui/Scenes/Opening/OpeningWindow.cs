using Godot;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Opening;

public sealed partial class OpeningWindow : Control
{

    [Signal]
    public delegate void IntroFinishedEventHandler();


    private const float ScrollSpeed = 30.0f;
    private const float ScrollStartDelayMs = 1000f;
    private const float ScrollClamp = 1843.0f;
    private const float ScenarioW = 1024f;
    private const float ScenarioH = 2048f;
    private const float CanvasW = 1024f;

    private const float CanvasH = 768f;

    private const float ScenarioStartY = CanvasH - 200f;
    private const int SlideshowFrameCount = 4;
    private const double DwellMs = 17500.0;
    private const int AlphaMax = 250;

    private const string ScenarioPath = "data/ui/openning_scenario.dds";
    private const string MainWindowPath = "data/ui/mainwindow.dds";

    internal const string SkipCfgPath = "user://options.cfg";
    private const string SkipCfgSection = "OPENNING";
    private const string SkipCfgKey = "SKIP";

    private const float WheelScrubStep = 30f;
    private const float WheelScrubMin = 30f;
    private const float WheelScrubMax = 1833f;


    private const float PageScrubSpeed = 30.0f;
    private const float PageScrubFloor = 0f;
    private const float PageScrubCeil = 1843f;

    private static readonly string[] SlideshowPaths =
    [
        "data/ui/openning_001.dds",
        "data/ui/openning_002.dds",
        "data/ui/openning_003.dds",
        "data/ui/openning_004.dds"
    ];

    private readonly Texture2D?[] _slideshowTextures = new Texture2D?[SlideshowFrameCount];

    private int _alpha = AlphaMax;

    private int _alphaDir = -1;


    private ColorRect?
        _blackBackdrop;

    private double _dwellAccumMs;

    private bool _finished;

    private bool _panelAtMax = true;

    private TextureRect? _scenarioRect;
    private bool _scrollDone;
    private float _scrollOffset;
    private float _scrollStartWait;

    private TextureRect? _slideshowRect;
    private int _slideshowState = 1;
    private float _wheelScrubOffset;


    public HudAtlasLibrary? Atlas { get; set; }

    public FrontEndAudio? Audio { get; set; }


    public override void _Ready()
    {
        Size = new Vector2(CanvasW, CanvasH);
        MouseFilter = MouseFilterEnum.Stop;

        Material = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.Add
        };

        BuildUi();

        _scrollStartWait = ScrollStartDelayMs;
        _scrollOffset = 0f;
        _wheelScrubOffset = 0f;
        _alpha = AlphaMax;
        _alphaDir = -1;
        _dwellAccumMs = 0.0;
        _panelAtMax = true;
        _slideshowState = 1;

        Audio?.PlayIntroBgm();

        GD.Print("[OpeningWindow] Intro started: slideshow+crawl active, BGM 910061000. " +
                 "spec: frontend_layout_tables.md §6.");
    }


    public override void _Process(double delta)
    {
        if (_finished) return;

        var dt = (float)delta;
        var dtMs = dt * 1000f;

        UpdateScroll(dtMs, dt);
        UpdateSlideshow(dtMs);
    }

    public override void _Input(InputEvent ev)
    {
        if (_finished) return;

        var skip = ev switch
        {
            InputEventKey { Pressed: true } k when
                k.Keycode == Key.Enter || k.PhysicalKeycode == Key.Enter => true,
            InputEventKey { Pressed: true } k when
                k.Keycode == Key.Escape || k.PhysicalKeycode == Key.Escape => true,
            InputEventKey { Pressed: true } k when
                k.Keycode == Key.Space || k.PhysicalKeycode == Key.Space => true,
            _ => false
        };

        if (skip)
        {
            GD.Print("[OpeningWindow] Keyboard skip received.");
            PersistSkip();
            Finish();
            return;
        }

        if (_scrollDone && ev is InputEventKey { Pressed: true } pageKey)
        {
            var dt = (float)GetProcessDeltaTime();
            var step = PageScrubSpeed * dt;

            var physKey = pageKey.Keycode != Key.None ? pageKey.Keycode : pageKey.PhysicalKeycode;

            if (physKey == Key.Pageup)
            {
                _scrollOffset = Mathf.Max(_scrollOffset - step, PageScrubFloor);
                ApplyScrollPosition();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (physKey == Key.Pagedown)
            {
                _scrollOffset = Mathf.Min(_scrollOffset + step, PageScrubCeil);
                ApplyScrollPosition();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (_scrollDone && ev is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
        {
            var delta = mouseBtn.ButtonIndex switch
            {
                MouseButton.WheelUp => -WheelScrubStep,
                MouseButton.WheelDown => WheelScrubStep,
                _ => 0f
            };

            if (delta != 0f)
            {
                _wheelScrubOffset = Mathf.Clamp(
                    _wheelScrubOffset + delta,
                    WheelScrubMin,
                    WheelScrubMax);
                ApplyScrollPosition();
                GetViewport().SetInputAsHandled();
            }
        }
    }


    private void OnSkipPressed()
    {
        if (_finished) return;
        GD.Print("[OpeningWindow] Skip button (action 100) pressed.");
        PersistSkip();
        Finish();
    }


    private static void PersistSkip()
    {
        var cfg = new ConfigFile();
        cfg.Load(SkipCfgPath);
        cfg.SetValue(SkipCfgSection, SkipCfgKey, 1);
        cfg.Save(SkipCfgPath);
    }


    private void BuildUi()
    {
        _blackBackdrop = new ColorRect
        {
            Name = "BlackBackdrop",
            Position = Vector2.Zero,
            Size = new Vector2(CanvasW, CanvasH),
            Color = new Color(0f, 0f, 0f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_blackBackdrop);

        _slideshowRect = new TextureRect
        {
            Name = "SlideshowRect",
            Position = Vector2.Zero,
            Size = new Vector2(CanvasW, CanvasH),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(1f, 1f, 1f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_slideshowRect);

        for (var i = 0; i < SlideshowFrameCount; i++)
        {
            _slideshowTextures[i] = Atlas?.GetByPath(SlideshowPaths[i]);
            if (_slideshowTextures[i] is not null)
                GD.Print($"[OpeningWindow] Panel {i + 1}: {SlideshowPaths[i]}");
            else
                GD.PrintErr($"[OpeningWindow] Panel {i + 1} absent: {SlideshowPaths[i]}");
        }

        _slideshowRect.Texture = _slideshowTextures[0];

        _scenarioRect = new TextureRect
        {
            Name = "ScenarioRect",
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };

        var scenarioTex = Atlas?.GetByPath(ScenarioPath);
        if (scenarioTex is not null)
        {
            _scenarioRect.Texture = scenarioTex;
            GD.Print($"[OpeningWindow] Scenario crawl loaded: {ScenarioPath}");
        }
        else
        {
            GD.PrintErr($"[OpeningWindow] Scenario crawl absent: {ScenarioPath}");
        }

        _scenarioRect.Position = new Vector2(CanvasW * 0.5f - 512f, ScenarioStartY);
        _scenarioRect.Size = new Vector2(ScenarioW, ScenarioH);
        AddChild(_scenarioRect);

        var skipNormal = Atlas?.SliceByPath(MainWindowPath, 761, 165, 110, 32);
        var skipPressed = Atlas?.SliceByPath(MainWindowPath, 634, 165, 110, 32);

        var skipBtn = new TextureButton
        {
            Name = "SkipBtn",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -120f,
            OffsetTop = 10f,
            OffsetRight = -10f,
            OffsetBottom = 42f,
            TextureNormal = skipNormal,
            TexturePressed = skipPressed ?? skipNormal,
            TextureHover = skipNormal,
            StretchMode = TextureButton.StretchModeEnum.KeepAspect
        };
        skipBtn.Pressed += OnSkipPressed;
        AddChild(skipBtn);
    }


    private void UpdateScroll(float dtMs, float dt)
    {
        if (_scenarioRect is null) return;

        if (_scrollStartWait > 0f)
        {
            _scrollStartWait -= dtMs;
            return;
        }

        if (!_scrollDone)
        {
            _scrollOffset += dt * ScrollSpeed;

            if (_scrollOffset >= ScrollClamp)
            {
                _scrollOffset = ScrollClamp;
                _scrollDone = true;
                GD.Print("[OpeningWindow] Scenario crawl reached clamp 1843 — stopped.");
            }
        }

        ApplyScrollPosition();
    }

    private void ApplyScrollPosition()
    {
        if (_scenarioRect is null) return;

        var effectiveOffset = _scrollDone && _wheelScrubOffset > 0f
            ? _wheelScrubOffset
            : _scrollOffset;

        var godotY = ScenarioStartY - effectiveOffset;
        _scenarioRect.Position = new Vector2(CanvasW * 0.5f - 512f, godotY);
    }


    private void UpdateSlideshow(float dtMs)
    {
        if (_slideshowRect is null) return;

        _alpha += _alphaDir;

        if (_alpha >= AlphaMax)
        {
            _alpha = AlphaMax;
            _alphaDir = -1;
            _panelAtMax = true;
        }
        else if (_alpha <= 0)
        {
            _alpha = 0;
            _alphaDir = 1;
            _panelAtMax = false;
        }

        _slideshowRect.Modulate = new Color(1f, 1f, 1f, _alpha / (float)AlphaMax);

        _dwellAccumMs += dtMs;

        if (!(_dwellAccumMs >= DwellMs && _panelAtMax)) return;

        if (_slideshowState < SlideshowFrameCount)
        {
            _slideshowState++;
            _slideshowRect.Texture = _slideshowTextures[_slideshowState - 1];
            _dwellAccumMs = 0.0;
            _alpha = AlphaMax;
            _alphaDir = -1;
            _panelAtMax = true;
            _slideshowRect.Modulate = new Color(1f, 1f, 1f);
            GD.Print($"[OpeningWindow] Slideshow → phase {_slideshowState}. spec §6.");
        }
        else
        {
            _dwellAccumMs = 0.0;
        }
    }


    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        GD.Print("[OpeningWindow] Emitting IntroFinished.");
        EmitSignal(SignalName.IntroFinished);
    }
}