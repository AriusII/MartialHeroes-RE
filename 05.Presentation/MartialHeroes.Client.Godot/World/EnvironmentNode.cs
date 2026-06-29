using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Composition;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EnvironmentNode : Node3D
{
    private const int KeyframeCount = LightBin.KeyframeCount;

    private const double KeyframeMs = 1800.0;

    private const double PeriodMs = KeyframeCount * KeyframeMs;

    private const int NoonKeyframe = 24;


    private const float StreamRadiusHigh = 1800f;

    private const int PointLightSlots = 5;

    private const float WeatherFieldExtent = 800f;

    private const float WeatherSpawnHeight = 400f;

    private const int WeatherMaxParticles = 2000;


    private readonly OmniLight3D?[] _plPool = new OmniLight3D?[PointLightSlots];
    private readonly int[] _plSelected = new int[PointLightSlots];
    private readonly float[] _plSelectedDistSq = new float[PointLightSlots];

    private RealClientAssets? _assets;

    private int _appliedKeyframe = -1;
    private int _areaId;

    private bool _bgFallbackLogged;

    private DirectionalLight3D? _dirLight;

    private AreaEnvironment? _env;

    private Environment? _environment;

    private bool _fallbackDirApplied;

    private float _glowBaseWeight = 1.05f * 0.5f;
    private float _glowGlowWeight = 0.3f;

    private bool _hasSunDir;

    private Vector3 _plCachedFocus = new(float.MaxValue, float.MaxValue, float.MaxValue);
    private int _plActiveCount;
    private int _plFlickerDir = 1;
    private float _plFlickerRamp;
    private Vector3[]? _plGodotPos;
    private float _plMasterIntensity;
    private bool _plPoolInit;
    private float _plProximityRadius = 1024f;
    private PointLightRecord[]? _plRecords;

    private SkyDomeNode? _skyDome;

    private float _streamRadius = StreamRadiusHigh;

    private Vector3 _sunDirGodot = Vector3.Zero;

    private Color[]? _synthFogColors;

    private int _weatherDayCount;
    private int _weatherLastCode = -1;
    private GpuParticles3D? _weatherParticles;

    private WorldEnvironment? _worldEnv;

    public double ClockMs { get; private set; } = NoonKeyframe * KeyframeMs;


    public bool CycleEnabled { get; set; } = true;

    public float CycleSpeed { get; set; } = 30_000f;


    public float OptionBrightFloor { get; set; } = 1.0f;


    public Vector3 PointLightFocusGodot { get; set; } = Vector3.Zero;


    public void Configure(
        RealClientAssets? assets,
        int areaId,
        WorldEnvironment? sceneWorldEnv = null,
        DirectionalLight3D? sceneDirLight = null)
    {
        _areaId = areaId;
        _assets = assets;
        ResolveOrCreateSceneNodes(sceneWorldEnv, sceneDirLight);

        ReadOptionBright();
        ReadDisplayLuaScalars(assets);

        ApplyStaticPostSettings();

        if (assets is null)
        {
            if (_environment is not null)
            {
                _environment.BackgroundMode = Environment.BGMode.Color;
                _environment.BackgroundColor = new Color(0.45f, 0.55f, 0.70f);
                _environment.AmbientLightSource = Environment.AmbientSource.Color;
                _environment.AmbientLightColor =
                    new Color(OptionBrightFloor, OptionBrightFloor, OptionBrightFloor);
                _environment.AmbientLightEnergy = 1.0f;
                _environment.FogEnabled = false;
            }

            GD.Print(
                "[Environment] No VFS — applied visible fallback environment (ambient floor + glow on, neutral sky, linear tonemap).");
            return;
        }

        _env = VfsEnvironmentSource.Load(assets, areaId);

        BuildSynthFogColors();

        ResolveSunDirection();

        BuildSkyDomes(assets);

        InitPointLightPool();
        LoadPointLightData();

        ResetWeatherState();

        ClockMs = NoonKeyframe * KeyframeMs;
        _appliedKeyframe = -1;

        ApplyKeyframe(NoonKeyframe, 0f);
        UpdatePointLights(NoonKeyframe, (NoonKeyframe + 1) % KeyframeCount, 0f, 0f);
        TickWeather();

        PrintSummary(NoonKeyframe);
    }

    public void SetTimeOfDay(int keyframeIndex, bool freeze = true)
    {
        keyframeIndex = Math.Clamp(keyframeIndex, 0, KeyframeCount - 1);
        if (freeze) CycleEnabled = false;
        ClockMs = keyframeIndex * KeyframeMs;
        _appliedKeyframe = -1;
        ApplyKeyframe(keyframeIndex, 0f);
        UpdatePointLights(keyframeIndex, (keyframeIndex + 1) % KeyframeCount, 0f, 0f);
        TickWeather();
        GD.Print($"[Environment] SetTimeOfDay applied keyframe={keyframeIndex} freeze={freeze}");
    }

    public void UpdateClockMs(double absoluteClockMs)
    {
        ClockMs = Math.Clamp(absoluteClockMs, 0.0, PeriodMs - 1.0);
        var kf = (int)(ClockMs / KeyframeMs) % KeyframeCount;
        var kfNext = (kf + 1) % KeyframeCount;
        var frac = (float)(ClockMs % KeyframeMs / KeyframeMs);
        ApplyKeyframe(kf, frac);
        UpdatePointLights(kf, kfNext, frac, 0f);
        TickWeather();
        _skyDome?.UpdateDomes(ClockMs, 0.0);
    }


    public override void _Process(double delta)
    {
        if (_environment is null || !CycleEnabled) return;

        ClockMs += delta * CycleSpeed;
        if (ClockMs >= PeriodMs)
        {
            ClockMs %= PeriodMs;
            _weatherDayCount++;
        }

        var kf = (int)(ClockMs / KeyframeMs) % KeyframeCount;
        var kfNext = (kf + 1) % KeyframeCount;
        var frac = (float)(ClockMs % KeyframeMs / KeyframeMs);

        ApplyKeyframe(kf, frac);
        UpdatePointLights(kf, kfNext, frac, (float)delta);
        TickWeather();
        FollowWeather();

        _skyDome?.UpdateDomes(ClockMs, delta);
    }
}
