// World/CameraController.Modes.cs
//
// Partial class — the view-mode state and transform application:
// ApplyCurrentModeTransform, ApplyThirdPersonTransform (with terrain collision),
// ApplyFirstPersonTransform, ApplyStaticTransform.
// See CameraController.cs for the full file description and all spec cites.
//
// spec: Docs/RE/specs/camera_movement.md §A.4 / §A.5.2 / §A.6 — transform application.

using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class CameraController
{
    // =========================================================================
    // § _PROCESS — keyboard input and per-frame orbit integration
    // =========================================================================

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        if (_mode == ViewMode.FreeFly)
        {
            ProcessFreeFly(dt);
            return;
        }

        // Update focus to follow the player.
        _focus = PlayerGodotPosition;

        // ── Keyboard camera actions (polled per frame) ────────────────────────
        // spec: Docs/RE/specs/camera_movement.md §A.3.1 — action IDs and effect. CODE-CONFIRMED.
        // We map to physical Godot keys; the original client uses action IDs 1000–1029.
        // Polarity of zoom/pitch pairs is configurable (spec §D item 1 — INFERRED).

        var anyKey = false;
        var friction = _mode == ViewMode.Static ? FrictionStatic : FrictionDefault;

        // Yaw keys (action 1028 = yaw left, 1029 = yaw right).
        // spec: Docs/RE/specs/camera_movement.md §A.3.1. CODE-CONFIRMED.
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

        // Elevation / zoom keys (action 1000/1001 = zoom; 1002/1003 = pitch).
        // "Zoom" feeds the elevation integrator, NOT the radius.
        // spec: Docs/RE/specs/camera_movement.md §A.4 — Fixed-radius orbit; zoom → elevation. CODE-CONFIRMED.
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

        // Friction: decay rates when no input.
        // spec: Docs/RE/specs/camera_movement.md §A.4 — "No-input rate decay (friction) 0.6". CODE-CONFIRMED.
        if (!anyKey)
        {
            _yawRate *= friction;
            _elevationRate *= friction;
        }

        // Dead-zone: snap near-zero rates to 0.
        // spec: Docs/RE/specs/camera_movement.md §A.4 — "Rate dead-zone 1e-3". CODE-CONFIRMED.
        if (Mathf.Abs(_yawRate) < RateDeadZone) _yawRate = 0f;
        if (Mathf.Abs(_elevationRate) < RateDeadZone) _elevationRate = 0f;

        // Clamp rates.
        // spec: Docs/RE/specs/camera_movement.md §A.4 — "Zoom-rate / orbit-step clamp [−0.1, +0.1]". CODE-CONFIRMED.
        _yawRate = Mathf.Clamp(_yawRate, -OrbitStepRateClamp, OrbitStepRateClamp);
        _elevationRate = Mathf.Clamp(_elevationRate, -OrbitStepRateClamp, OrbitStepRateClamp);

        // Integrate into yaw / elevation.
        _yaw += _yawRate;
        _elevation += _elevationRate;

        // Clamp yaw.
        ApplyYawClamp();

        // Clamp elevation.
        // spec: Docs/RE/specs/camera_movement.md §A.4 — "[−90.0, −12.0] degrees". CODE-CONFIRMED.
        _elevation = Mathf.Clamp(_elevation, ElevationMinRad, ElevationMaxRad);

        // Apply the transform for the current mode.
        ApplyCurrentModeTransform();
    }

    // =========================================================================
    // § TRANSFORM APPLICATION
    // =========================================================================

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

    /// <summary>
    ///     Third-person: fixed-radius orbit + terrain collision using the legacy rotated eye-offset model.
    ///     Eye = focus + Rotate(eyeOffsetSeed, yaw, elevation)
    ///     where eyeOffsetSeed = (−750, 0, −500) in Godot space (legacy (−750,0,+500) with Z negated).
    ///     |eyeOffsetSeed| ≈ 901.39 = fixed orbit radius (no scaling).
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 Fixed-radius orbit model. CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — Eye-offset seed (−750,0,+500) legacy / (−750,0,−500) Godot.
    ///     CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 Third — Focus Z −40 / terrain collision. CODE-CONFIRMED.
    ///     spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
    /// </summary>
    private void ApplyThirdPersonTransform()
    {
        // Build focus point: player position + the spec "Focus Z −40" offset in legacy space.
        // Legacy Z = −40 → Godot Z = −(−40) = +40. Applied as a +Z shift in Godot space.
        // spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Focus / look-at Z −40.0". CODE-CONFIRMED.
        // spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
        var focusPoint = _focus + new Vector3(0f, 0f, -FocusZThird); // −(−40) = +40 on Godot Z

        // Compute eye offset using the legacy rotated eye-offset model.
        // The seed vector in Godot space is (EyeOffsetSeedLegacyX, EyeOffsetSeedLegacyY, EyeOffsetSeedGodotZ)
        // = (−750, 0, −500). Rotate it by yaw (about Y-up), then by elevation (about the yaw-rotated X).
        // spec: Docs/RE/specs/camera_movement.md §A.5.1 — seed (−750,0,+500) legacy → (−750,0,−500) Godot. CODE-CONFIRMED.
        // spec: Docs/RE/specs/camera_movement.md §A.4 — "compose orientation from yaw quaternion + pitch component". CODE-CONFIRMED.
        var eyeOffsetSeed = new Vector3(EyeOffsetSeedLegacyX, EyeOffsetSeedLegacyY, EyeOffsetSeedGodotZ);
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, _yaw);
        var orbitBasis = yawBasis.Rotated(yawBasis.X, _elevation);
        var eyeOffset = orbitBasis * eyeOffsetSeed;

        var eyePos = focusPoint + eyeOffset;

        // ── Terrain collision — Third only ─────────────────────────────────────
        // spec: Docs/RE/specs/camera_movement.md §A.6 — vertical slide, no horizontal pull-in. CODE-CONFIRMED.
        if (GroundHeightFunc is not null)
            try
            {
                // Convert Godot eye X/Z to legacy world coordinates for the heightmap query.
                // spec: WorldCoordinates.ToLegacy — legacyZ = −godotZ.
                var legacyX = eyePos.X;
                var legacyZ = -eyePos.Z;
                var terrainY = GroundHeightFunc(legacyX, legacyZ);

                // Clamp eye Y to terrain + TerrainLift + TerrainYBias (slide, not snap).
                // spec: Docs/RE/specs/camera_movement.md §A.6 — "terrainHeight + 3.8, +2.0 bias". CODE-CONFIRMED.
                var minY = terrainY + TerrainLift + TerrainYBias;
                if (eyePos.Y < minY)
                {
                    eyePos.Y = minY;
                    // On hard terrain hit, force yaw-rate to stop camera fighting the ground.
                    // spec: Docs/RE/specs/camera_movement.md §A.4 — "Terrain hard-hit yaw kill −0.01". CODE-CONFIRMED.
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

    /// <summary>
    ///     First-person: short-boom from head position, orientation × player heading.
    ///     Focus Z = −55 (legacy space) → Godot Z = +55.
    ///     Yaw-orbit seeded to π (180° flip so the player faces the camera on first-person entry).
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 First — Focus Z −55, yaw seed π. CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — shared eye-offset seed (−750,0,+500). CODE-CONFIRMED.
    ///     spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
    ///     No terrain collision.
    /// </summary>
    private void ApplyFirstPersonTransform()
    {
        // Focus Z −55 in legacy space → +55 in Godot Z.
        // spec: Docs/RE/specs/camera_movement.md §A.5.2 — First Focus Z −55. CODE-CONFIRMED.
        var focusPoint = _focus + new Vector3(0f, 0f, -FocusZFirst); // −(−55) = +55 on Godot Z

        // Rotate the shared eye-offset seed by yaw + elevation (same orbital machinery as Third).
        // The yaw seed for First is π (180° flip), already encoded in _yaw at mode-switch time
        // if needed; the seed vector is (−750, 0, −500) in Godot space.
        // spec: Docs/RE/specs/camera_movement.md §A.5.2 — First yaw seed π. CODE-CONFIRMED.
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

    /// <summary>
    ///     Static: fixed-angle follow. Tracks position, never orbits.
    ///     Focus Z = −55 (legacy space) → Godot Z = +55.
    ///     Yaw is locked (taken from player facing per spec; here fixed at 0 — player heading not
    ///     yet exposed to the camera view; TODO: wire player facing when Application exposes it).
    ///     Only pitch / elevation key is polled (see _Process).
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 Static — Focus Z −55, non-mouse yaw. CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — shared eye-offset seed (−750,0,+500). CODE-CONFIRMED.
    ///     spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
    /// </summary>
    private void ApplyStaticTransform()
    {
        // Focus Z −55 in legacy space → +55 in Godot Z.
        // spec: Docs/RE/specs/camera_movement.md §A.5.2 — Static Focus Z −55. CODE-CONFIRMED.
        var focusPoint = _focus + new Vector3(0f, 0f, -FocusZStatic); // −(−55) = +55 on Godot Z

        // Static yaw is taken from the player's facing/heading field (not mouse-driven).
        // The player heading is not yet exposed through Application; fixed to 0 until it is.
        // spec: Docs/RE/specs/camera_movement.md §A.5.2 — Static: "yaw from player facing, not mouse". CODE-CONFIRMED.
        const float FixedYaw = 0f;

        // Rotate the shared eye-offset seed by the fixed yaw + current elevation.
        // spec: Docs/RE/specs/camera_movement.md §A.5.1 — seed (−750,0,+500) legacy → (−750,0,−500) Godot. CODE-CONFIRMED.
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