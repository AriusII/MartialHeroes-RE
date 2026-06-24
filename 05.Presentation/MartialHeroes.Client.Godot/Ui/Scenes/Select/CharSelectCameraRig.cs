using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectCameraRig : Node
{
    public const float CameraFov = 50.0f;
    public const float CameraNear = 5.0f;
    public const float CameraFar = 15000.0f;

    private const float BoomZoomUnitsPerSecond = 10.0f;

    private const float HitBoxHalfExtentXZ = 6.0f;
    private const float HitBoxYHeight = 22.0f;

    private const float DollyDurationSeconds = 2.5f;

    private int _activeZoomAction;

    private float _boomZ;

    private Camera3D? _camera;
    private Vector3 _dollyKf0;
    private Vector3 _dollyKf1;
    private float _dollyT;
    private bool _dollyActive;
    private Vector3 _dollyKf0Look;
    private Vector3 _dollyKf1Look;
    private Func<int, Node3D?>? _slotActorProvider;
    private float[] _slotGodotX = [];
    private float[] _slotGodotZ = [];
    private Vector3 _staticEyeGodot;

    public void Configure(
        Camera3D camera,
        Vector3 staticEyeGodot,
        float[] slotGodotX,
        float[] slotGodotZ,
        Func<int, Node3D?> slotActorProvider,
        Vector3 kf0Eye,
        Vector3 kf0LookAt,
        Vector3 kf1LookAt)
    {
        _camera = camera;
        _staticEyeGodot = staticEyeGodot;
        _slotGodotX = slotGodotX;
        _slotGodotZ = slotGodotZ;
        _slotActorProvider = slotActorProvider;

        _boomZ = 0.0f;
        _activeZoomAction = 0;

        _dollyKf0 = kf0Eye;
        _dollyKf1 = staticEyeGodot;
        _dollyKf0Look = kf0LookAt;
        _dollyKf1Look = kf1LookAt;
        _dollyT = 0.0f;
        _dollyActive = true;

        _camera.GlobalPosition = kf0Eye;
        _camera.LookAt(kf0LookAt, Vector3.Up);

        GD.Print(
            $"[CharSelectCameraRig] Dolly KF0→KF1 started: kf0={kf0Eye} kf1={staticEyeGodot} " +
            $"duration={DollyDurationSeconds}s FOV={CameraFov}/near={CameraNear}/far={CameraFar}. " +
            "spec: charselect.md §6.1 (entry dolly KF0→KF1).");
    }

    public void SetZoomAction(int actionId)
    {
        _activeZoomAction = actionId;
    }

    public override void _Process(double delta)
    {
        if (_camera is null) return;
        var dt = (float)delta;
        if (_dollyActive)
            ApplyDollyTick(dt);
        else
            ApplyCameraBoomZoom(dt);
    }

    private void ApplyDollyTick(float dt)
    {
        if (_camera is null) return;

        _dollyT = Math.Min(_dollyT + dt / DollyDurationSeconds, 1.0f);
        var t = Mathf.SmoothStep(0.0f, 1.0f, _dollyT);
        var pos = _dollyKf0.Lerp(_dollyKf1, t);
        var look = _dollyKf0Look.Lerp(_dollyKf1Look, t);
        _camera.GlobalPosition = pos;
        if (look.DistanceSquaredTo(pos) > 0.01f)
            _camera.LookAt(look, Vector3.Up);

        if (_dollyT >= 1.0f)
        {
            _dollyActive = false;
            _camera.GlobalPosition = _dollyKf1;
            GD.Print("[CharSelectCameraRig] Dolly KF0→KF1 complete; holding at KF1. spec: charselect.md §6.1.");
        }
    }

    private void ApplyCameraBoomZoom(float dt)
    {
        if (_camera is null) return;

        var actionDir = _activeZoomAction switch
        {
            72 => 1.0f,
            73 => -1.0f,
            _ => 0.0f
        };

        if (actionDir == 0.0f) return;

        _boomZ += actionDir * BoomZoomUnitsPerSecond * dt;
        var forward = -_camera.GlobalTransform.Basis.Z.Normalized();
        _camera.GlobalPosition = _staticEyeGodot + forward * _boomZ;
    }

    public int HitTest(Vector2 viewportLocalPos)
    {
        if (_camera is null) return -1;

        var rayOrigin = _camera.ProjectRayOrigin(viewportLocalPos);
        var rayDir = _camera.ProjectRayNormal(viewportLocalPos);

        var bestSlot = -1;
        var bestT = float.PositiveInfinity;

        var count = Math.Min(Math.Min(_slotGodotX.Length, _slotGodotZ.Length), 5);
        for (var i = 0; i < count; i++)
        {
            var actor = _slotActorProvider?.Invoke(i);
            if (actor is null) continue;

            var rowBaseY = actor.Position.Y;
            var boxMin = new Vector3(
                _slotGodotX[i] - HitBoxHalfExtentXZ,
                rowBaseY,
                _slotGodotZ[i] - HitBoxHalfExtentXZ);
            var boxMax = new Vector3(
                _slotGodotX[i] + HitBoxHalfExtentXZ,
                rowBaseY + HitBoxYHeight,
                _slotGodotZ[i] + HitBoxHalfExtentXZ);

            if (TryRayAabb(rayOrigin, rayDir, boxMin, boxMax, out var t) && t < bestT)
            {
                bestT = t;
                bestSlot = i;
            }
        }

        return bestSlot;
    }

    private static bool TryRayAabb(
        Vector3 origin, Vector3 dir,
        Vector3 boxMin, Vector3 boxMax,
        out float tHit)
    {
        tHit = 0.0f;
        var tEnter = float.NegativeInfinity;
        var tExit = float.PositiveInfinity;

        for (var axis = 0; axis < 3; axis++)
        {
            var o = origin[axis];
            var d = dir[axis];
            var lo = boxMin[axis];
            var hi = boxMax[axis];

            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi) return false;
                continue;
            }

            var inv = 1.0f / d;
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tEnter) tEnter = t1;
            if (t2 < tExit) tExit = t2;
            if (tEnter > tExit) return false;
        }

        if (tExit < 0.0f) return false;
        tHit = tEnter >= 0.0f ? tEnter : 0.0f;
        return true;
    }
}