using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Domain.Simulation.Simulation;

public static class CameraModes
{
    public static bool OrbitsPlayer(CameraMode mode)
    {
        return mode is CameraMode.Third or CameraMode.First or CameraMode.Gamble;
    }

    public static bool RunsTerrainCollision(CameraMode mode)
    {
        return mode == CameraMode.Third;
    }

    public static CameraTransform Compute(in Vector3Fixed focus, CameraMode mode, in CameraParameters parameters)
    {
        if (mode == CameraMode.First) return new CameraTransform(focus, parameters.Yaw, parameters.Pitch);

        var distance = parameters.FollowDistance;
        var horizontal = distance * MathF.Cos(parameters.Pitch);
        var vertical = distance * MathF.Sin(parameters.Pitch);

        var offsetX = -horizontal * MathF.Sin(parameters.Yaw);
        var offsetZ = -horizontal * MathF.Cos(parameters.Yaw);

        var (fx, fy, fz) = focus.ToVector3Float();
        var eye = Vector3Fixed.FromFloat(fx + offsetX, fy - vertical, fz + offsetZ);
        return new CameraTransform(eye, parameters.Yaw, parameters.Pitch);
    }

    public static Vector3Fixed ClampEyeAboveTerrain(
        in Vector3Fixed eye,
        float terrainHeight,
        float clampOffset = RecoveredDefaults.TerrainHeightClampOffset)
    {
        var (x, y, z) = eye.ToVector3Float();
        var minY = terrainHeight + clampOffset;
        return y < minY ? Vector3Fixed.FromFloat(x, minY, z) : eye;
    }

    public static float ClampSymmetric(float value, float magnitude)
    {
        if (value > magnitude) return magnitude;

        return value < -magnitude ? -magnitude : value;
    }

    public static float IntegrateRate(
        float rate,
        float inputStep,
        float gain,
        float friction,
        float deadZone,
        float clamp)
    {
        var next = inputStep != 0f ? rate + inputStep * gain : rate * friction;

        if (MathF.Abs(next) < deadZone) next = 0f;

        return ClampSymmetric(next, clamp);
    }

    public static class RecoveredDefaults
    {
        public const float NoInputFriction = 0.6f;

        public const float KeyboardGain = 0.3f;

        public const float YawRateClamp = 0.1f;

        public const float ZoomDeltaClamp = 1.0f;

        public const float PitchClamp = 4.0f;

        public const float AbsoluteYawClamp = 1.5707963f;

        public const float YawDamping = 0.9f;

        public const float RateDeadZone = 1e-3f;

        public const float ThirdFollowDistance = 10.0f;

        public const float ThirdDefaultPitch = -0.5235988f;

        public const float TerrainHeightClampOffset = 3.8f;
    }
}

public readonly record struct CameraParameters
{
    public float FollowDistance { get; init; }

    public float Yaw { get; init; }

    public float Pitch { get; init; }

    public static CameraParameters ThirdDefault => new()
    {
        FollowDistance = CameraModes.RecoveredDefaults.ThirdFollowDistance,
        Yaw = 0f,
        Pitch = CameraModes.RecoveredDefaults.ThirdDefaultPitch
    };
}

public readonly record struct CameraTransform(Vector3Fixed Eye, float Yaw, float Pitch);