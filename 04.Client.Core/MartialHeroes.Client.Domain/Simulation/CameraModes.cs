using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation;

/// <summary>
/// Per-mode camera parameters (distance, pitch, clamps, gains) and the pure computation of camera eye
/// position + orientation from a focus point, mode and integrated input. This is <b>math only</b> — no
/// rendering; Godot applies the resulting transform. spec: Docs/RE/specs/camera_movement.md Part A.
/// </summary>
/// <remarks>
/// <para>
/// <b>All tuning constants are injected, not hard-coded.</b> The spec lifts every numeric constant
/// from the binary's immediate operands and marks them "(solid as code immediates) ... (plausible) as
/// the value that governs runtime feel ... expose as config" (§A.3 banner). Several seed values
/// (follow distance, pitch, eye-offset seed, focus seed) are explicitly <c>plausible</c> /
/// <c>UNVERIFIED</c>. So <see cref="CameraParameters"/> carries them as data and
/// <see cref="RecoveredDefaults"/> exposes the recovered starting tune for callers who want it. We do
/// not bake any magic number into the math. spec: camera_movement.md §A.3/§A.4/§A.5.
/// </para>
/// <para>
/// <b>Determinism note.</b> The camera is purely client-side and never authoritative (it never crosses
/// the wire), so unlike movement it is allowed to use <see cref="float"/> for its tuning. The output is
/// a <see cref="Vector3Fixed"/> eye position so the Godot rig can consume the same logical type used
/// elsewhere. spec: camera_movement.md §A (camera is non-networked) / Part C.
/// </para>
/// </remarks>
public static class CameraModes
{
    /// <summary>The recovered §A.4/§A.5 starting-tune constants (all "expose as config"). spec: camera_movement.md §A.4/§A.5.</summary>
    public static class RecoveredDefaults
    {
        /// <summary>No-input rate decay (friction) — multiplies a rate each frame with no input. spec: camera_movement.md §A.4 (0.6).</summary>
        public const float NoInputFriction = 0.6f;

        /// <summary>Keyboard input → rate gain (per-frame step added on key). spec: camera_movement.md §A.4 (0.3).</summary>
        public const float KeyboardGain = 0.3f;

        /// <summary>Yaw-rate clamp magnitude (bounds per-frame yaw angular velocity). spec: camera_movement.md §A.4 (±0.1).</summary>
        public const float YawRateClamp = 0.1f;

        /// <summary>Zoom-delta clamp magnitude (bounds per-frame distance change). spec: camera_movement.md §A.4 (±1.0).</summary>
        public const float ZoomDeltaClamp = 1.0f;

        /// <summary>Pitch clamp magnitude (bounds the pitch state). spec: camera_movement.md §A.4 (±4.0).</summary>
        public const float PitchClamp = 4.0f;

        /// <summary>Absolute accumulated-yaw clamp (±π/2). spec: camera_movement.md §A.4 (±1.5708).</summary>
        public const float AbsoluteYawClamp = 1.5707963f;

        /// <summary>Yaw slerp / damping factor (eases accumulated yaw toward target). spec: camera_movement.md §A.4 (0.9).</summary>
        public const float YawDamping = 0.9f;

        /// <summary>Rate dead-zone — rates below this magnitude snap to 0. spec: camera_movement.md §A.4 (1e-3).</summary>
        public const float RateDeadZone = 1e-3f;

        /// <summary>Third-person default follow distance. spec: camera_movement.md §A.5 (≈ 10.0; plausible).</summary>
        public const float ThirdFollowDistance = 10.0f;

        /// <summary>Third-person default pitch (≈ -π/6, -30°). spec: camera_movement.md §A.5 (-0.5236; plausible).</summary>
        public const float ThirdDefaultPitch = -0.5235988f;

        /// <summary>Third-person terrain-height clamp offset (eye never below terrain + 3.8). spec: camera_movement.md §A.6 (3.8).</summary>
        public const float TerrainHeightClampOffset = 3.8f;
    }

    /// <summary>True for modes that orbit the player (yaw / pitch around the follow target). spec: camera_movement.md §A.1.</summary>
    public static bool OrbitsPlayer(CameraMode mode) =>
        mode is CameraMode.Third or CameraMode.First or CameraMode.Gamble;

    /// <summary>True for modes that run terrain collision (only Third). spec: camera_movement.md §A.1/§A.6.</summary>
    public static bool RunsTerrainCollision(CameraMode mode) => mode == CameraMode.Third;

    /// <summary>
    /// Computes the camera eye position and orientation from the focus point, mode and parameters. The
    /// eye is placed at the follow distance along the orbit direction defined by yaw + pitch; static /
    /// event modes keep a fixed orientation. spec: Docs/RE/specs/camera_movement.md §A.4 steps 6-7 / §A.5.
    /// </summary>
    /// <param name="focus">The focus point the camera looks at (the player head / ground point). spec: §A.5.</param>
    /// <param name="mode">The active view mode. spec: §A.1.</param>
    /// <param name="parameters">The mode tuning (distance / yaw / pitch). spec: §A.4/§A.5.</param>
    /// <returns>The eye position (Q16.16) and the yaw / pitch the rig should orient by.</returns>
    public static CameraTransform Compute(in Vector3Fixed focus, CameraMode mode, in CameraParameters parameters)
    {
        // First-person: the eye sits at the focus (player head) with no trailing follow distance and no
        // terrain clamp. spec: camera_movement.md §A.5 (First).
        if (mode == CameraMode.First)
        {
            return new CameraTransform(focus, parameters.Yaw, parameters.Pitch);
        }

        // Orbit basis: place the eye at follow distance behind/above the focus, rotated by yaw (about the
        // up axis) and pitch (elevation). Planar XZ from yaw, vertical from pitch. spec: §A.4 steps 6-7.
        float distance = parameters.FollowDistance;
        float horizontal = distance * MathF.Cos(parameters.Pitch);
        float vertical = distance * MathF.Sin(parameters.Pitch);

        // Eye direction: opposite the facing, so the camera trails the focus. spec: §A.5 (eye = focus + offset).
        float offsetX = -horizontal * MathF.Sin(parameters.Yaw);
        float offsetZ = -horizontal * MathF.Cos(parameters.Yaw);

        var (fx, fy, fz) = focus.ToVector3Float();
        var eye = Vector3Fixed.FromFloat(fx + offsetX, fy - vertical, fz + offsetZ);
        return new CameraTransform(eye, parameters.Yaw, parameters.Pitch);
    }

    /// <summary>
    /// Applies the §A.6 Third-person terrain-height clamp: the eye's vertical is raised so it never sinks
    /// below <c>terrainHeight + offset</c>. Only Third runs this. spec: Docs/RE/specs/camera_movement.md §A.6.
    /// </summary>
    /// <param name="eye">The candidate eye position (Q16.16).</param>
    /// <param name="terrainHeight">The sampled terrain height at the eye (X, Z) (real units).</param>
    /// <param name="clampOffset">The minimum clearance above terrain (default 3.8). spec: §A.6.</param>
    /// <returns>The eye, raised if it was below the clamp.</returns>
    public static Vector3Fixed ClampEyeAboveTerrain(
        in Vector3Fixed eye,
        float terrainHeight,
        float clampOffset = RecoveredDefaults.TerrainHeightClampOffset)
    {
        var (x, y, z) = eye.ToVector3Float();
        float minY = terrainHeight + clampOffset;
        return y < minY ? Vector3Fixed.FromFloat(x, minY, z) : eye;
    }

    /// <summary>
    /// Clamps a value into the symmetric range <c>[-magnitude, +magnitude]</c>. Used for the §A.4 yaw /
    /// pitch / zoom clamps. spec: Docs/RE/specs/camera_movement.md §A.4.
    /// </summary>
    public static float ClampSymmetric(float value, float magnitude)
    {
        if (value > magnitude)
        {
            return magnitude;
        }

        return value < -magnitude ? -magnitude : value;
    }

    /// <summary>
    /// Integrates one frame of a rate field: applies friction when there is no input, adds the keyboard
    /// gain when there is, snaps sub-dead-zone rates to 0, and clamps to <paramref name="clamp"/>.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 steps 4-5.
    /// </summary>
    /// <param name="rate">The current rate.</param>
    /// <param name="inputStep">The signed keyboard input step this frame (0 = no input).</param>
    /// <param name="gain">Keyboard input → rate gain. spec: §A.4 (0.3).</param>
    /// <param name="friction">No-input decay. spec: §A.4 (0.6).</param>
    /// <param name="deadZone">Rate dead-zone. spec: §A.4 (1e-3).</param>
    /// <param name="clamp">Symmetric clamp magnitude. spec: §A.4.</param>
    public static float IntegrateRate(
        float rate,
        float inputStep,
        float gain,
        float friction,
        float deadZone,
        float clamp)
    {
        float next = inputStep != 0f ? rate + (inputStep * gain) : rate * friction;

        if (MathF.Abs(next) < deadZone)
        {
            next = 0f;
        }

        return ClampSymmetric(next, clamp);
    }
}

/// <summary>
/// The per-mode camera tuning the math reads: follow distance, accumulated yaw and pitch. The values
/// are injected (the spec's "expose as config" constant set). spec: Docs/RE/specs/camera_movement.md §A.4/§A.5.
/// </summary>
public readonly record struct CameraParameters
{
    /// <summary>Follow distance from focus to eye (real units). spec: camera_movement.md §A.5 (Third ≈ 10.0).</summary>
    public float FollowDistance { get; init; }

    /// <summary>Accumulated yaw about the up axis (radians), clamped to ±π/2. spec: camera_movement.md §A.4.</summary>
    public float Yaw { get; init; }

    /// <summary>Pitch / elevation (radians), clamped to ±4.0. spec: camera_movement.md §A.4/§A.5.</summary>
    public float Pitch { get; init; }

    /// <summary>The recovered Third-person starting tune. spec: camera_movement.md §A.5.</summary>
    public static CameraParameters ThirdDefault => new()
    {
        FollowDistance = CameraModes.RecoveredDefaults.ThirdFollowDistance,
        Yaw = 0f,
        Pitch = CameraModes.RecoveredDefaults.ThirdDefaultPitch,
    };
}

/// <summary>
/// The computed camera transform: an eye position the rig moves to, plus the yaw / pitch it orients by.
/// spec: Docs/RE/specs/camera_movement.md §A.4 (steps 6-8).
/// </summary>
public readonly record struct CameraTransform(Vector3Fixed Eye, float Yaw, float Pitch);