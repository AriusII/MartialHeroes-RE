using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class CameraController : Camera3D
{
    public enum ViewMode
    {
        Third = 1,

        First = 2,

        Static = 3,

        Gamble = 4,

        Event = 5,

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


    private const float GambleEyeLegacyX = 24097.46484375f;

    private const float GambleEyeLegacyZ = 55694.4296875f;

    private const float GambleElevationRad = -Mathf.Pi / 3f;

    private const float GambleEyeHeightBias = 80f;

    private const float GambleFocusZ = -160f;


    private const float EventOrbitBoomDistance = 12.0f;

    private const float EventLookAtLift = 8.0f;

    private const float EventElevationRad = -Mathf.Pi / 6f;


    private const float FirstYawSeed = Mathf.Pi;

    private const float FirstEyeLift = 3.8f;

    private const float FirstLookLift = 2.0f;

    private const float FirstLookDistance = 200f;

    private const float StaticEyeLift = 2.0f;


    private const string ViewOptionConfigPath = "user://do_option.cfg";

    private const string ViewOptionSection = "DO_OPTION";

    private const string ViewOptionKey = "OPTION_VIEW_CHAR";

    private const int ViewOptionMin = 1;

    private const int ViewOptionMax = 3;

    private float _elevation = DefaultElevationRad;

    private float _elevationRate;

    private float _flyPitch;


    private float _flyYaw;


    private Vector3 _focus = Vector3.Zero;
    private bool _hasPrevPlayerPos;


    private ViewMode _modeBeforeFreeFly = ViewMode.Third;

    private bool _mouseCaptured;

    private float _playerFacingYaw;

    private Vector3 _prevPlayerPos = Vector3.Zero;

    private bool _rightMouseHeld;

    private float _yaw;

    private float _yawRate;


    [Export] public float ElevationKeyPolarity { get; set; } = 1f;

    [Export] public float PitchDragPolarity { get; set; } = 1f;


    public Func<float, float, float>? GroundHeightFunc { get; set; }


    public Vector3 PlayerGodotPosition { get; set; } = Vector3.Zero;

    public ViewMode CurrentMode { get; private set; } = ViewMode.Third;

    public bool IsGameplayView => CurrentMode is ViewMode.Third or ViewMode.First or ViewMode.Static;


    public override void _Ready()
    {
        KeepAspect = KeepAspectEnum.Width;
        Fov = GameFov;
        Near = GameNear;
        Far = GameFar;

        _elevation = DefaultElevationRad;
        _yaw = 0f;

        CurrentMode = LoadPersistedView();

        ApplyCurrentModeTransform();

        GD.Print(
            "[Camera] mode=Third | FOV=65deg HORIZONTAL (KeepAspect=Width, spec: Docs/RE/structs/perspective_camera.md) near=5 far=15000 | " +
            $"eyeSeed=({EyeOffsetSeedLegacyX},{EyeOffsetSeedLegacyY},{EyeOffsetSeedGodotZ}) Godot | " +
            $"radius={OrbitRadius:F1}u | elev_default={Mathf.RadToDeg(DefaultElevationRad):F1}deg | " +
            $"elev_clamp=[{Mathf.RadToDeg(ElevationMinRad):F0}deg,{Mathf.RadToDeg(ElevationMaxRad):F0}deg] | " +
            $"yaw_clamp_third=[{Mathf.RadToDeg(YawMin):F1}deg,{Mathf.RadToDeg(YawMaxThird):F1}deg ({YawMaxThird:F4}rad)] | " +
            $"mode={CurrentMode} (OPTION_VIEW_CHAR persisted 1..3) | " +
            "RMB=orbit wheel=elevation ESC=reset-to-Third SelectView()=view-menu Tab=devFreeFly. " +
            "spec: Docs/RE/specs/camera_movement.md §A.2.2/§A.8.");
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

        GD.Print($"[Camera] Configure: focus={focus}, mode={CurrentMode}, radius={OrbitRadius:F1}u.");
    }

    public void SetViewMode(ViewMode newMode)
    {
        if (CurrentMode == newMode) return;

        if (newMode == ViewMode.FreeFly)
        {
            _modeBeforeFreeFly = CurrentMode == ViewMode.FreeFly ? ViewMode.Third : CurrentMode;
            SyncFlyAnglesFromCurrentBasis();
            CurrentMode = ViewMode.FreeFly;
            GD.Print("[Camera] Entered DEVELOPER FREE-FLY (non-original). Tab to return.");
        }
        else
        {
            ReleaseMouse();
            _rightMouseHeld = false;

            if (CurrentMode == ViewMode.FreeFly)
                _focus = PlayerGodotPosition;

            CurrentMode = newMode;
            ApplyCurrentModeTransform();
            PersistView(newMode);
            GD.Print($"[Camera] Switched to {newMode}.");
        }
    }

    public void SelectView(ViewMode mode)
    {
        if (mode is ViewMode.Third or ViewMode.First or ViewMode.Static)
        {
            SetViewMode(mode);
            GD.Print($"[Camera] SelectView({mode}) via view-menu mechanism. " +
                     "spec: Docs/RE/specs/camera_movement.md §A.2.2 (view menu / hotkey dispatcher).");
            return;
        }

        GD.Print($"[Camera] SelectView({mode}) ignored — only Third/First/Static are user-selectable. " +
                 "spec: Docs/RE/specs/camera_movement.md §A.2.1.");
    }

    private ViewMode LoadPersistedView()
    {
        var config = new ConfigFile();
        var err = config.Load(ViewOptionConfigPath);
        if (err != Error.Ok)
            return ViewMode.Third;

        var raw = config.GetValue(ViewOptionSection, ViewOptionKey, ViewOptionMin).AsInt32();
        var clamped = Math.Clamp(raw, ViewOptionMin, ViewOptionMax);
        return (ViewMode)clamped;
    }

    private void PersistView(ViewMode mode)
    {
        if (mode is not (ViewMode.Third or ViewMode.First or ViewMode.Static))
            return;

        var value = Math.Clamp((int)mode, ViewOptionMin, ViewOptionMax);

        var config = new ConfigFile();
        config.Load(ViewOptionConfigPath);
        config.SetValue(ViewOptionSection, ViewOptionKey, value);
        var err = config.Save(ViewOptionConfigPath);
        if (err != Error.Ok)
            GD.PrintErr($"[Camera] OPTION_VIEW_CHAR persist failed (err={err}). " +
                        "spec: Docs/RE/specs/camera_movement.md §A.8.");
    }
}