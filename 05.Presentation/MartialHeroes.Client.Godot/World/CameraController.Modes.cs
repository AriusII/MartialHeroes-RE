using Godot;
using MartialHeroes.Client.Presentation.Helpers;

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

        UpdatePlayerFacingYaw();

        if (_mode is ViewMode.Gamble or ViewMode.Event)
        {
            ApplyCurrentModeTransform();
            return;
        }


        var anyKey = false;
        var friction = _mode == ViewMode.Static ? FrictionStatic : FrictionDefault;

        if (_mode != ViewMode.Static && global::Godot.Input.IsKeyPressed(Key.Q))
        {
            _yawRate -= KeyboardGain;
            anyKey = true;
        }

        if (_mode != ViewMode.Static && global::Godot.Input.IsKeyPressed(Key.E))
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
            case ViewMode.Gamble:
                ApplyGambleTransform();
                break;
            case ViewMode.Event:
                ApplyEventTransform();
                break;
            case ViewMode.FreeFly:
                ApplyFlyTransform();
                break;
        }
    }

    private void UpdatePlayerFacingYaw()
    {
        var pos = PlayerGodotPosition;

        if (!_hasPrevPlayerPos)
        {
            _prevPlayerPos = pos;
            _hasPrevPlayerPos = true;
            return;
        }

        var dx = pos.X - _prevPlayerPos.X;
        var dz = pos.Z - _prevPlayerPos.Z;
        _prevPlayerPos = pos;

        if (dx * dx + dz * dz < 1e-6f) return;

        _playerFacingYaw = Mathf.Atan2(dx, dz);
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
        var eyePos = _focus;
        eyePos.Y = SampleTerrainY(_focus, FirstEyeLift);

        var yawTotal = FirstYawSeed + _playerFacingYaw + _yaw;
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, yawTotal);
        var orbitBasis = yawBasis.Rotated(yawBasis.X, _elevation);

        var forward = orbitBasis * Vector3.Forward;
        var lookAt = eyePos + forward * FirstLookDistance;
        lookAt.Y += FirstLookLift - FirstEyeLift;

        if (!IsFiniteVector(eyePos) || !IsFiniteVector(lookAt)) return;
        if ((eyePos - lookAt).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(lookAt, Vector3.Up);
    }

    private void ApplyStaticTransform()
    {
        var focusPoint = _focus + new Vector3(0f, 0f, -FocusZStatic);

        var eyeOffsetSeed = new Vector3(EyeOffsetSeedLegacyX, EyeOffsetSeedLegacyY, EyeOffsetSeedGodotZ);
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, _playerFacingYaw);
        var orbitBasis = yawBasis.Rotated(yawBasis.X, _elevation);
        var eyeOffset = orbitBasis * eyeOffsetSeed;

        var eyePos = focusPoint + eyeOffset;

        var minY = SampleTerrainY(eyePos, StaticEyeLift);
        if (eyePos.Y < minY) eyePos.Y = minY;

        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - focusPoint).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(focusPoint, Vector3.Up);
    }

    private float SampleTerrainY(Vector3 godotPos, float lift)
    {
        if (GroundHeightFunc is null) return godotPos.Y;

        try
        {
            var terrainY = GroundHeightFunc(godotPos.X, -godotPos.Z);
            return float.IsFinite(terrainY) ? terrainY + lift : godotPos.Y;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Camera] SampleTerrainY threw: {ex.Message}");
            return godotPos.Y;
        }
    }

    private void ApplyGambleTransform()
    {
        var (gx, _, gz) = WorldCoordinates.ToGodot(GambleEyeLegacyX, 0f, GambleEyeLegacyZ);

        var eyeY = GambleEyeHeightBias;
        if (GroundHeightFunc is not null)
            try
            {
                var terrainY = GroundHeightFunc(GambleEyeLegacyX, GambleEyeLegacyZ);
                if (float.IsFinite(terrainY)) eyeY = terrainY + GambleEyeHeightBias;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Camera] Gamble GroundHeightFunc threw: {ex.Message}");
            }

        var eyePos = new Vector3(gx, eyeY, gz);
        var lookAt = _focus + new Vector3(0f, 0f, -GambleFocusZ);

        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - lookAt).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(lookAt, Vector3.Up);
    }

    private void ApplyEventTransform()
    {
        var lookAt = _focus + new Vector3(0f, EventLookAtLift, 0f);

        var orbitBasis = Basis.Identity
            .Rotated(Vector3.Up, _yaw)
            .Rotated(Basis.Identity.Rotated(Vector3.Up, _yaw).X, EventElevationRad);
        var eyeOffset = orbitBasis * new Vector3(0f, 0f, EventOrbitBoomDistance);
        var eyePos = lookAt + eyeOffset;

        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - lookAt).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(lookAt, Vector3.Up);
    }
}