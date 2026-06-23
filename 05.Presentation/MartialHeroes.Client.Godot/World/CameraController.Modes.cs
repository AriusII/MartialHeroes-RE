using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class CameraController
{
    public override void _Process(double delta)
    {
        var dt = (float)delta;

        if (_mode == ViewMode.FreeFly)
        {
            ProcessFreeFly(dt);
            return;
        }

        _focus = PlayerGodotPosition;


        var anyKey = false;
        var friction = _mode == ViewMode.Static ? FrictionStatic : FrictionDefault;

        if (global::Godot.Input.IsKeyPressed(Key.Q))
        {
            _yawRate -= KeyboardGain;
            anyKey = true;
        }

        if (global::Godot.Input.IsKeyPressed(Key.E))
        {
            _yawRate += KeyboardGain;
            anyKey = true;
        }

        if (global::Godot.Input.IsKeyPressed(Key.R))
        {
            _elevationRate -= KeyboardGain * ElevationKeyPolarity;
            anyKey = true;
        }

        if (global::Godot.Input.IsKeyPressed(Key.F))
        {
            _elevationRate += KeyboardGain * ElevationKeyPolarity;
            anyKey = true;
        }

        if (!anyKey)
        {
            _yawRate *= friction;
            _elevationRate *= friction;
        }

        if (Mathf.Abs(_yawRate) < RateDeadZone) _yawRate = 0f;
        if (Mathf.Abs(_elevationRate) < RateDeadZone) _elevationRate = 0f;

        _yawRate = Mathf.Clamp(_yawRate, -OrbitStepRateClamp, OrbitStepRateClamp);
        _elevationRate = Mathf.Clamp(_elevationRate, -OrbitStepRateClamp, OrbitStepRateClamp);

        _yaw += _yawRate;
        _elevation += _elevationRate;

        ApplyYawClamp();

        _elevation = Mathf.Clamp(_elevation, ElevationMinRad, ElevationMaxRad);

        ApplyCurrentModeTransform();
    }


    private void ApplyCurrentModeTransform()
    {
        switch (_mode)
        {
            case ViewMode.Third:
                ApplyThirdPersonTransform();
                break;
            case ViewMode.First:
                ApplyFirstPersonTransform();
                break;
            case ViewMode.Static:
                ApplyStaticTransform();
                break;
            case ViewMode.FreeFly:
                ApplyFlyTransform();
                break;
        }
    }

    private void ApplyThirdPersonTransform()
    {
        var focusPoint = _focus + new Vector3(0f, 0f, -FocusZThird);

        var eyeOffsetSeed = new Vector3(EyeOffsetSeedLegacyX, EyeOffsetSeedLegacyY, EyeOffsetSeedGodotZ);
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, _yaw);
        var orbitBasis = yawBasis.Rotated(yawBasis.X, _elevation);
        var eyeOffset = orbitBasis * eyeOffsetSeed;

        var eyePos = focusPoint + eyeOffset;

        if (GroundHeightFunc is not null)
            try
            {
                var legacyX = eyePos.X;
                var legacyZ = -eyePos.Z;
                var terrainY = GroundHeightFunc(legacyX, legacyZ);

                var minY = terrainY + TerrainLift + TerrainYBias;
                if (eyePos.Y < minY)
                {
                    eyePos.Y = minY;
                    _yawRate = TerrainHitYawKill;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Camera] GroundHeightFunc threw: {ex.Message}");
            }

        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - focusPoint).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(focusPoint, Vector3.Up);
    }

    private void ApplyFirstPersonTransform()
    {
        var focusPoint = _focus + new Vector3(0f, 0f, -FocusZFirst);

        var eyeOffsetSeed = new Vector3(EyeOffsetSeedLegacyX, EyeOffsetSeedLegacyY, EyeOffsetSeedGodotZ);
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, _yaw);
        var orbitBasis = yawBasis.Rotated(yawBasis.X, _elevation);
        var eyeOffset = orbitBasis * eyeOffsetSeed;
        var eyePos = focusPoint + eyeOffset;

        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - focusPoint).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(focusPoint, Vector3.Up);
    }

    private void ApplyStaticTransform()
    {
        var focusPoint = _focus + new Vector3(0f, 0f, -FocusZStatic);

        const float FixedYaw = 0f;

        var eyeOffsetSeed = new Vector3(EyeOffsetSeedLegacyX, EyeOffsetSeedLegacyY, EyeOffsetSeedGodotZ);
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, FixedYaw);
        var orbitBasis = yawBasis.Rotated(yawBasis.X, _elevation);
        var eyeOffset = orbitBasis * eyeOffsetSeed;

        var eyePos = focusPoint + eyeOffset;
        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - focusPoint).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(focusPoint, Vector3.Up);
    }
}