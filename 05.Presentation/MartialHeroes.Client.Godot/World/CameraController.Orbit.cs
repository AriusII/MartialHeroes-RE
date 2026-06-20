// World/CameraController.Orbit.cs
//
// Partial class — free-fly math (ProcessFreeFly, ApplyFlyTransform, SyncFlyAnglesFromCurrentBasis,
// ClampFlyPitch), yaw-clamp helper (ApplyYawClamp), and guard utilities (IsFiniteVector,
// ClampToFinite).
// See CameraController.cs for the full file description and all spec cites.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/specs/camera_movement.md §A.4 / §A.5 — orbit math.

using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class CameraController
{
    // =========================================================================
    // § FREE-FLY (developer mode, non-original)
    // =========================================================================

    private void ProcessFreeFly(float dt)
    {
        var fast = global::Godot.Input.IsKeyPressed(Key.Shift);
        var speed = fast ? FlyFastSpeed : FlyNormalSpeed;
        var dist = speed * dt;

        var forward = -Transform.Basis.Z;
        var right = Transform.Basis.X;

        var move = Vector3.Zero;
        if (global::Godot.Input.IsKeyPressed(Key.W)) move += forward;
        if (global::Godot.Input.IsKeyPressed(Key.S)) move -= forward;
        if (global::Godot.Input.IsKeyPressed(Key.A)) move -= right;
        if (global::Godot.Input.IsKeyPressed(Key.D)) move += right;
        if (global::Godot.Input.IsKeyPressed(Key.E)) move += Vector3.Up;
        if (global::Godot.Input.IsKeyPressed(Key.Q)) move -= Vector3.Up;

        if (move == Vector3.Zero) return;

        if (move.LengthSquared() > 1e-6f)
            move = move.Normalized();

        Position += move * dist;
        Position = ClampToFinite(Position, 1e7f);
    }

    private void ApplyFlyTransform()
    {
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, _flyYaw);
        var pitchBasis = yawBasis.Rotated(yawBasis.X, _flyPitch);
        Transform = new Transform3D(pitchBasis, Position);
    }

    private void SyncFlyAnglesFromCurrentBasis()
    {
        var fwd = -Transform.Basis.Z;
        _flyPitch = Mathf.Asin(Mathf.Clamp(fwd.Y, -1f, 1f));
        _flyYaw = Mathf.Atan2(fwd.X, fwd.Z);
        ClampFlyPitch();
    }

    private void ClampFlyPitch()
    {
        var limit = Mathf.DegToRad(89f);
        _flyPitch = Mathf.Clamp(_flyPitch, -limit, limit);
    }

    // =========================================================================
    // § CLAMP HELPERS
    // =========================================================================

    private void ApplyYawClamp()
    {
        var yawMax = _mode == ViewMode.Third ? YawMaxThird : YawMaxSymmetric;
        // spec: Docs/RE/specs/camera_movement.md §A.4 — Third upper eased +1.4137; base ±π/2. CODE-CONFIRMED.
        _yaw = Mathf.Clamp(_yaw, YawMin, yawMax);
    }

    // =========================================================================
    // § GUARD UTILITIES
    // =========================================================================

    private static bool IsFiniteVector(Vector3 v)
    {
        return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }

    private static Vector3 ClampToFinite(Vector3 v, float limit)
    {
        var x = float.IsFinite(v.X) ? Mathf.Clamp(v.X, -limit, limit) : 0f;
        var y = float.IsFinite(v.Y) ? Mathf.Clamp(v.Y, -limit, limit) : 0f;
        var z = float.IsFinite(v.Z) ? Mathf.Clamp(v.Z, -limit, limit) : 0f;
        return new Vector3(x, y, z);
    }
}