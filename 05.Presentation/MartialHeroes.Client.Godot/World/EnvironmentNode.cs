
using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Composition;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EnvironmentNode : Node3D
{

    private const float ViewRange = 15000f;

    private const int KeyframeCount = LightBin.KeyframeCount;

    private const double KeyframeMs = 1800.0;

    private const double PeriodMs = KeyframeCount * KeyframeMs;

    private const int NoonKeyframe = 24;

    private int _appliedKeyframe = -1;
    private int _areaId;

    private double _clockMs = NoonKeyframe * KeyframeMs;

    public double ClockMs => _clockMs;
    private DirectionalLight3D? _dirLight;


    private AreaEnvironment? _env;

    private Environment? _environment;

    private bool _fallbackDirApplied;
    private bool _hasSunDir;

    private SkyDomeNode? _skyDome;

    private Vector3 _sunDirGodot = Vector3.Zero;

    private WorldEnvironment? _worldEnv;


    public bool CycleEnabled { get; set; } = true;

    public float CycleSpeed { get; set; } = 30_000f;



    public float OptionBrightFloor { get; set; } =
        1.0f;


    public void Configure(
        RealClientAssets? assets,
        int areaId,
        WorldEnvironment? sceneWorldEnv = null,
        DirectionalLight3D? sceneDirLight = null)
    {
        _areaId = areaId;
        ResolveOrCreateSceneNodes(sceneWorldEnv, sceneDirLight);

        ApplyStaticPostSettings();

        if (assets is null)
        {
            if (_environment is not null)
            {
                _environment.BackgroundMode = Environment.BGMode.Color;
                _environment.BackgroundColor = new Color(0.45f, 0.55f, 0.70f);
                _environment.AmbientLightSource = Environment.AmbientSource.Color;
                _environment.AmbientLightColor = Colors.White;
                _environment.AmbientLightEnergy = 1.0f;
                _environment.GlowEnabled = false;
                _environment.FogEnabled = false;
            }

            GD.Print(
                "[Environment] No VFS — applied visible fallback environment (white ambient 1.0, neutral sky, linear tonemap).");
            return;
        }

        _env = VfsEnvironmentSource.Load(assets, areaId);

        ResolveSunDirection();

        BuildSkyDomes(assets);

        _clockMs = NoonKeyframe * KeyframeMs;
        _appliedKeyframe = -1;

        ApplyKeyframe(NoonKeyframe, 0f);

        PrintSummary(NoonKeyframe);
    }

    public void SetTimeOfDay(int keyframeIndex, bool freeze = true)
    {
        keyframeIndex = Math.Clamp(keyframeIndex, 0, KeyframeCount - 1);
        if (freeze) CycleEnabled = false;
        _clockMs = keyframeIndex * KeyframeMs;
        _appliedKeyframe = -1;
        ApplyKeyframe(keyframeIndex, 0f);
        GD.Print($"[Environment] SetTimeOfDay applied keyframe={keyframeIndex} freeze={freeze}");
    }


    public override void _Process(double delta)
    {
        if (_environment is null || !CycleEnabled) return;

        _clockMs += delta * CycleSpeed;
        if (_clockMs >= PeriodMs) _clockMs %= PeriodMs;

        var kf = (int)(_clockMs / KeyframeMs) % KeyframeCount;
        var frac = (float)(_clockMs % KeyframeMs / KeyframeMs);

        ApplyKeyframe(kf, frac);

        _skyDome?.UpdateDomes(_clockMs, delta);
    }
}