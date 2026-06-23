using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class CameraController : Camera3D
{
    public enum ViewMode
    {
        Third = 1,

        First = 2,

        Static = 3,

        FreeFly = 99
    }

    private const float GameFov = 65f;

    private const float GameNear = 5f;

    private const float GameFar = 15000f;


    private const float OrbitRadius = 901.39f;

    private const float DefaultElevationRad = -Mathf.Pi / 6f;

    private const float ElevationMinRad = -Mathf.Pi / 2f;

    private const float ElevationMaxRad = -12f * Mathf.Pi / 180f;


    private const float YawMin = -Mathf.Pi / 2f;

    private const float YawMaxThird = Mathf.Pi / 2f * 0.9f;

    private const float YawMaxSymmetric = Mathf.Pi / 2f;


    private const float TimeDeltaScale = 1e-3f;

    private const float FrictionDefault = 0.6f;

    private const float FrictionStatic = 0.8f;

    private const float KeyboardGain = 0.3f;

    private const float OrbitStepRateClamp = 0.1f;

    private const float RateDeadZone = 1e-3f;

    private const float MouseDragPitchGain = 5e-4f;

    private const float WheelZoomScale = 0.01f;


    private const float TerrainLift = 3.8f;

    private const float TerrainYBias = 2.0f;

    private const float TerrainHitYawKill = -0.01f;

    private const float FocusZThird = -40f;

    private const float FocusZFirst = -55f;

    private const float FocusZStatic = -55f;


    private const float EyeOffsetSeedLegacyX = -750f;

    private const float EyeOffsetSeedLegacyY = 0f;

    private const float EyeOffsetSeedGodotZ = -500f;


    private const float FlyNormalSpeed = 600f;

    private const float FlyFastSpeed = 2000f;

    private const float FlyMouseSensitivity = 0.004f;

    private float _elevation = DefaultElevationRad;

    private float _elevationRate;

    private float _flyPitch;


    private float _flyYaw;


    private Vector3 _focus = Vector3.Zero;


    private ViewMode _mode = ViewMode.Third;

    private ViewMode _modeBeforeFreeFly = ViewMode.Third;

    private bool _mouseCaptured;

    private bool _rightMouseHeld;

    private float _yaw;

    private float _yawRate;


    [Export] public float ElevationKeyPolarity { get; set; } = 1f;

    [Export] public float PitchDragPolarity { get; set; } = 1f;


    public Func<float, float, float>? GroundHeightFunc { get; set; }


    public Vector3 PlayerGodotPosition { get; set; } = Vector3.Zero;


    public override void _Ready()
    {
        Fov = GameFov;
        Near = GameNear;
        Far = GameFar;

        _elevation = DefaultElevationRad;
        _yaw = 0f;

        ApplyThirdPersonTransform();

        GD.Print(
            "[Camera] mode=Third | FOV=65deg near=5 far=15000 | " +
            $"eyeSeed=({EyeOffsetSeedLegacyX},{EyeOffsetSeedLegacyY},{EyeOffsetSeedGodotZ}) Godot | " +
            $"radius={OrbitRadius:F1}u | elev_default={Mathf.RadToDeg(DefaultElevationRad):F1}deg | " +
            $"elev_clamp=[{Mathf.RadToDeg(ElevationMinRad):F0}deg,{Mathf.RadToDeg(ElevationMaxRad):F0}deg] | " +
            $"yaw_clamp_third=[{Mathf.RadToDeg(YawMin):F1}deg,{Mathf.RadToDeg(YawMaxThird):F1}deg ({YawMaxThird:F4}rad)] | " +
            "RMB=orbit wheel=elevation ESC=reset-to-Third F1=Third F2=First F3=Static Tab=devFreeFly");
    }

    public override void _ExitTree()
    {
        ReleaseMouse();
    }


    public void Configure(Vector3 focus, float cellSize)
    {
        _ = cellSize;
        PlayerGodotPosition = focus;
        _focus = focus;
        _elevation = DefaultElevationRad;
        _yaw = 0f;
        _yawRate = 0f;
        _elevationRate = 0f;
        ApplyThirdPersonTransform();

        GD.Print($"[Camera] Configure: focus={focus}, mode={_mode}, radius={OrbitRadius:F1}u.");
    }

    public void SetViewMode(ViewMode newMode)
    {
        if (_mode == newMode) return;

        if (newMode == ViewMode.FreeFly)
        {
            _modeBeforeFreeFly = _mode == ViewMode.FreeFly ? ViewMode.Third : _mode;
            SyncFlyAnglesFromCurrentBasis();
            _mode = ViewMode.FreeFly;
            GD.Print("[Camera] Entered DEVELOPER FREE-FLY (non-original). Tab to return.");
        }
        else
        {
            ReleaseMouse();
            _rightMouseHeld = false;

            if (_mode == ViewMode.FreeFly)
                _focus = PlayerGodotPosition;

            _mode = newMode;
            ApplyCurrentModeTransform();
            GD.Print($"[Camera] Switched to {newMode}.");
        }
    }
}