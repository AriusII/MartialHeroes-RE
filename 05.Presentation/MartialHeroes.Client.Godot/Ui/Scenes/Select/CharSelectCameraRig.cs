
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

public sealed partial class CharSelectCameraRig : Node
{

    private const float DollyRatePerMs = 0.0005f;


    private const float Kf0PitchRad = -0.03333334f * Mathf.Pi;
    private const float Kf0YawRad = 0.01333333f * Mathf.Pi;

    private const float Kf1PitchRad = -0.01483333f * Mathf.Pi;

    private const float Kf1YawRad = 0.004361111f * Mathf.Pi;


    private const float BoomZoomUnitsPerSecond = 10.0f;

    private const float BoomMinZ = 0.0f;

    private const float BoomMaxZ = 26.0f;


    private const float HitBoxHalfExtentXZ = 6.0f;
    private const float HitBoxYHeight = 22.0f;

    private int _activeZoomAction;
    private float _boomZ;


    private Camera3D? _camera;
    private bool _dollyComplete;

    private float _dollyElapsedMs;
    private Quaternion _kf0Orientation;


    private Vector3 _kf0Pos;
    private Quaternion _kf1Orientation;
    private Vector3 _kf1Pos;

    private Func<int, Node3D?>? _slotActorProvider;
    private float[] _slotGodotX = [];

    private float[] _slotGodotZ = [];

    public void Configure(
        Camera3D camera,
        float[] slotGodotX,
        float[] slotGodotZ,
        Func<int, Node3D?> slotActorProvider,
        Vector3 kf0Pos,
        Vector3 kf1Pos)
    {
        _camera = camera;
        _slotGodotX = slotGodotX;
        _slotGodotZ = slotGodotZ;
        _slotActorProvider = slotActorProvider;

        _kf0Pos = kf0Pos;
        _kf1Pos = kf1Pos;

        _kf0Orientation = EulerOrientation(Kf0YawRad, Kf0PitchRad);
        _kf1Orientation = EulerOrientation(Kf1YawRad, Kf1PitchRad);

        _dollyElapsedMs = 0.0f;
        _dollyComplete = false;
        _boomZ = 0.0f;

        GD.Print(
            $"[CharSelectCameraRig] Entry dolly armed (FREE-LOOK Euler, NO look-at): KF0={kf0Pos} → KF1={kf1Pos}; " +
            $"KF0 yaw {Mathf.RadToDeg(Kf0YawRad):F3}°/pitch {Mathf.RadToDeg(Kf0PitchRad):F3}°, " +
            $"KF1 yaw {Mathf.RadToDeg(Kf1YawRad):F3}° (0.004361111×π, IDA sub_40566E)/pitch {Mathf.RadToDeg(Kf1PitchRad):F3}° (no base heading); " +
            $"t = clamp(elapsedMs × 0.0005, 0, 1) → 2.0 s. IDA: sub_40566E (KF table) / sub_404EE8 (yawQuat×pitchQuat, no +π).");
    }

    public void SetZoomAction(int actionId)
    {
        _activeZoomAction = actionId;
    }

    public override void _Process(double delta)
    {
        if (_camera is null) return;
        var dt = (float)delta;

        if (!_dollyComplete) TickDolly(dt);
        ApplyCameraBoomZoom(dt);
    }


    private void TickDolly(float dt)
    {
        _dollyElapsedMs += dt * 1000.0f;
        var t = Mathf.Clamp(_dollyElapsedMs * DollyRatePerMs, 0.0f, 1.0f);

        _camera!.Position = _kf0Pos.Lerp(_kf1Pos, t);
        _camera.Quaternion = _kf0Orientation.Slerp(_kf1Orientation, t);

        if (t >= 1.0f)
        {
            _dollyComplete = true;
            _camera.Position = _kf1Pos;
            _camera.Quaternion = _kf1Orientation;
            GD.Print(
                "[CharSelectCameraRig] Entry dolly complete — holding KF1. spec: §3.5.2 (only indices 0/1 armed).");
        }
    }


    private void ApplyCameraBoomZoom(float dt)
    {
        if (!_dollyComplete || _camera is null) return;

        var actionDir = _activeZoomAction switch
        {
            72 => 1.0f,
            73 => -1.0f,
            _ => 0.0f
        };

        if (actionDir != 0.0f)
        {
            _boomZ += actionDir * BoomZoomUnitsPerSecond * dt;
            var forward = -_camera.GlobalTransform.Basis.Z.Normalized();
            _camera.GlobalPosition = _kf1Pos + forward * _boomZ;
            return;
        }

        var keyDir = 0.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(Key.Pageup)) keyDir += 1.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(Key.Pagedown)) keyDir -= 1.0f;
        if (keyDir == 0.0f) return;

        _boomZ = Mathf.Clamp(_boomZ + keyDir * BoomZoomUnitsPerSecond * dt, BoomMinZ, BoomMaxZ);
        var fwd = -_camera.GlobalTransform.Basis.Z.Normalized();
        _camera.GlobalPosition = _kf1Pos + fwd * _boomZ;
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
            var boxMin = new Vector3(_slotGodotX[i] - HitBoxHalfExtentXZ, rowBaseY,
                _slotGodotZ[i] - HitBoxHalfExtentXZ);
            var boxMax = new Vector3(_slotGodotX[i] + HitBoxHalfExtentXZ, rowBaseY + HitBoxYHeight,
                _slotGodotZ[i] + HitBoxHalfExtentXZ);

            if (TryRayAabb(rayOrigin, rayDir, boxMin, boxMax, out var t) && t < bestT)
            {
                bestT = t;
                bestSlot = i;
            }
        }

        return bestSlot;
    }


    private static Quaternion EulerOrientation(float yawRad, float pitchRad)
    {
        var yaw = new Quaternion(Vector3.Up, yawRad);
        var pitch = new Quaternion(Vector3.Right, pitchRad);
        return (yaw * pitch).Normalized();
    }

    private static bool TryRayAabb(Vector3 origin, Vector3 dir, Vector3 boxMin, Vector3 boxMax, out float tHit)
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