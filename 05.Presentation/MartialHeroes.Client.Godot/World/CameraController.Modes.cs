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
    ///     Third-person: fixed-radius orbit + terrain collision.
    ///     Eye = focus + eyeOffset(yaw, elevation)
    ///     where |eyeOffset| = OrbitRadius (fixed; no scale).
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 Fixed-radius orbit model. CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 Third — terrain collision. CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — focus Z = −40 (base / Third). CODE-CONFIRMED.
    /// </summary>
    private void ApplyThirdPersonTransform()
    {
        // Build focus point: player position + the spec "Focus Z −40" vertical/focus offset.
        // spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Focus / look-at Z −40.0". CODE-CONFIRMED.
        // In Godot space the legacy Z-40 offset is applied as a Godot +Z shift (Z negated).
        // We apply it as a Y bias so the camera looks at a point 40 units above the ground
        // (approximately head-height), which is the standard over-the-shoulder feel.
        var focusPoint = _focus + new Vector3(0f, -FocusZThird, 0f); // −(−40) = +40 on Y

        // Compute eye offset for the fixed-radius orbit.
        // In legacy-space the eye-offset is (−750, 0, +500); its magnitude ≈ 901.39.
        // We represent it as (yaw, elevation) polar coordinates at radius OrbitRadius.
        // Convention: elevation < 0 → camera is above the focus looking downward.
        var cosEl = Mathf.Cos(_elevation);
        var sinEl = Mathf.Sin(_elevation);
        var cosYaw = Mathf.Cos(_yaw);
        var sinYaw = Mathf.Sin(_yaw);

        // Spherical → Cartesian eye offset (Godot right-handed Y-up):
        //   x = r · cos(elev) · sin(yaw)
        //   y = −r · sin(elev)     [negative elev → positive y, camera above focus]
        //   z = r · cos(elev) · cos(yaw)
        var eyeOffset = new Vector3(
            OrbitRadius * cosEl * sinYaw,
            -OrbitRadius * sinEl,
            OrbitRadius * cosEl * cosYaw
        );

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
    ///     First-person: eye collapses to player head position; yaw/pitch look.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 First — "Eye sits at the player's head". CODE-CONFIRMED.
    ///     No terrain collision.
    /// </summary>
    private void ApplyFirstPersonTransform()
    {
        // In first-person the follow radius collapses to 0 — eye is at the player.
        // We approximate "player head" as the player position + some height offset.
        const float HeadHeight = 15f; // rough head height in world units (legacy scale)
        var eyePos = _focus + new Vector3(0f, HeadHeight, 0f);

        if (!IsFiniteVector(eyePos)) return;

        // Build basis from yaw/elevation (like free-fly but from the player eye).
        var yawBasis = Basis.Identity.Rotated(Vector3.Up, _yaw);
        var fullBasis = yawBasis.Rotated(yawBasis.X, _elevation);

        Transform = new Transform3D(fullBasis, eyePos);
    }

    /// <summary>
    ///     Static: fixed-angle follow. Tracks position, never orbits.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 Static — "follows position, never rotates". CODE-CONFIRMED.
    ///     Yaw is fixed; only elevation key is polled (see _Process).
    /// </summary>
    private void ApplyStaticTransform()
    {
        // Static uses a fixed yaw (locked at its initial seed = 0).
        // Only the elevation is updateable.
        const float FixedYaw = 0f;

        var cosEl = Mathf.Cos(_elevation);
        var sinEl = Mathf.Sin(_elevation);
        var cosYaw = Mathf.Cos(FixedYaw);
        var sinYaw = Mathf.Sin(FixedYaw);

        var eyeOffset = new Vector3(
            OrbitRadius * cosEl * sinYaw,
            -OrbitRadius * sinEl,
            OrbitRadius * cosEl * cosYaw
        );

        var eyePos = _focus + eyeOffset;
        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - _focus).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(_focus, Vector3.Up);
    }
}