// World/CameraController.Input.cs
//
// Partial class — all input handling: _UnhandledInput (Tab/Esc/RMB/wheel), _Input (captured mouse),
// HandleMouseMotion dispatcher, and mouse capture/release helpers.
// See CameraController.cs for the full file description and all spec cites.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/specs/camera_movement.md §A.2.2 / §A.3.2 — input handling.

using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class CameraController
{
    // =========================================================================
    // § INPUT — unhandled (mouse buttons / wheel, mode-switch keys)
    // =========================================================================

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            // ── Tab: toggle developer free-fly ────────────────────────────────
            case InputEventKey { Pressed: true } key when key.Keycode is Key.Tab:
                if (_mode == ViewMode.FreeFly)
                    SetViewMode(_modeBeforeFreeFly);
                else
                    SetViewMode(ViewMode.FreeFly);
                GetViewport().SetInputAsHandled();
                break;

            // ── Escape: reset to Third-person (spec §A.2.2 call site 2)
            // Only resets the camera when not in free-fly (where Esc releases mouse).
            case InputEventKey { Pressed: true } esc when esc.Keycode is Key.Escape:
                if (_mode == ViewMode.FreeFly && _mouseCaptured)
                {
                    ReleaseMouse();
                    GetViewport().SetInputAsHandled();
                }
                else if (_mode != ViewMode.Third)
                {
                    // spec: Docs/RE/specs/camera_movement.md §A.2.2 call site 2 —
                    // "on ESC … snap back to Third-person". CODE-CONFIRMED.
                    SetViewMode(ViewMode.Third);
                    GetViewport().SetInputAsHandled();
                }

                break;

            // ── View-mode hotkeys (spec §A.2.2 call site 1 — "View menu / hotkey dispatcher").
            // The five in-world modes (Third/First/Static/Gamble/Event) are selected by the
            // original client's keyboard hotkey path; only the three user-selectable modes
            // (Third/First/Static) are persisted (OPTION_VIEW_CHAR 1..3) and reachable here.
            // spec: Docs/RE/specs/camera_movement.md §A.2.1 — "clamped to range 1..3 on load". CODE-CONFIRMED.
            // spec: Docs/RE/specs/camera_movement.md §A.2.2 call site 1 — hotkey dispatcher. CODE-CONFIRMED.
            case InputEventKey { Pressed: true } vm when vm.Keycode is Key.F1:
                // Third-person (OPTION_VIEW_CHAR = 1). Default.
                // spec: Docs/RE/specs/camera_movement.md §A.5.2 — Third. CODE-CONFIRMED.
                SetViewMode(ViewMode.Third);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true } vm when vm.Keycode is Key.F2:
                // First-person (OPTION_VIEW_CHAR = 2). Eye at player head.
                // spec: Docs/RE/specs/camera_movement.md §A.5.2 — First. CODE-CONFIRMED.
                SetViewMode(ViewMode.First);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true } vm when vm.Keycode is Key.F3:
                // Static fixed-angle follow (OPTION_VIEW_CHAR = 3).
                // spec: Docs/RE/specs/camera_movement.md §A.5.2 — Static. CODE-CONFIRMED.
                SetViewMode(ViewMode.Static);
                GetViewport().SetInputAsHandled();
                break;

            // ── Gamble / Event modes — DEFERRED.
            // Gamble requires a fixed world-coordinate eye (X=24097.46, Z=55694.43) and
            // no user-input; it frames the cube-gamble mini-game location only.
            // Event is a scripted/cutscene mode driven by a MotionClip (2-track eye/look-at
            // track) or player-orbit sub-mode — neither is driveable by a runtime hotkey in
            // normal play; it is activated by a cinematic-lock flag, not by user selection.
            // Both are NON-PERSISTED (not part of OPTION_VIEW_CHAR 1..3).
            // spec: Docs/RE/specs/camera_movement.md §A.2.1 — Gamble/Event not persisted. CODE-CONFIRMED.
            // spec: Docs/RE/specs/camera_movement.md §A.5.2 — Gamble fixed eye / Event cinematic. CODE-CONFIRMED.
            // TODO: implement Gamble + Event when the world-scene provides a cinematic trigger
            //       and the gamble location is known at runtime.

            // ── Right mouse button: start / stop look drag ────────────────────
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Right:
                _rightMouseHeld = mb.Pressed;
                if (mb.Pressed && _mode == ViewMode.FreeFly)
                    CaptureMouse();
                else if (!mb.Pressed && _mode == ViewMode.FreeFly)
                    ReleaseMouse();
                GetViewport().SetInputAsHandled();
                break;

            // ── Mouse wheel: change elevation (not radius)
            // spec: Docs/RE/specs/camera_movement.md §A.3.2 — "wheel/other-button … zoom delta scaled 0.01".
            // spec: Docs/RE/specs/camera_movement.md §A.4 — Fixed-radius orbit; zoom → elevation. CODE-CONFIRMED.
            case InputEventMouseButton mb
                when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && mb.Pressed:
                if (_mode != ViewMode.FreeFly)
                {
                    // spec: Docs/RE/specs/camera_movement.md §A.4 — "Wheel / other-button zoom scale 0.01". CODE-CONFIRMED.
                    var sign = (mb.ButtonIndex == MouseButton.WheelUp ? 1f : -1f) * ElevationKeyPolarity;
                    // Apply the spec wheel scale to an orbit step increment.
                    _elevationRate += sign * WheelZoomScale;
                    _elevationRate = Mathf.Clamp(_elevationRate, -OrbitStepRateClamp, OrbitStepRateClamp);
                    GetViewport().SetInputAsHandled();
                }

                break;

            // ── Mouse motion: look drag ───────────────────────────────────────
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
        }
    }

    // =========================================================================
    // § INPUT — _Input (catches captured-mouse events on some platforms)
    // =========================================================================

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion capturedMotion && _mouseCaptured)
        {
            HandleMouseMotion(capturedMotion);
            GetViewport().SetInputAsHandled();
        }
    }

    // =========================================================================
    // § MOUSE MOTION DISPATCHER
    // =========================================================================

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        var rel = motion.Relative;
        if (rel == Vector2.Zero) return;

        if (_mode == ViewMode.FreeFly)
        {
            // Developer mode: standard first-person look.
            if (_rightMouseHeld || _mouseCaptured)
            {
                _flyYaw -= rel.X * FlyMouseSensitivity;
                _flyPitch -= rel.Y * FlyMouseSensitivity;
                ClampFlyPitch();
                ApplyFlyTransform();
            }

            return;
        }

        // Third / First: right-mouse drag look.
        // spec: Docs/RE/specs/camera_movement.md §A.3.2 — "Right-button drag begins …
        //       vertical delta (anchorY − cursorY) scaled by 5e-4 feeds pitch-rate integrator".
        // CODE-CONFIRMED. Left-click is click-to-move only; NOT camera look.
        if (!_rightMouseHeld) return;

        // Yaw: horizontal delta → yaw offset (apply directly, not to rate — the original
        // integrates the cursor delta on move, not a velocity integrator).
        _yaw += rel.X * FlyMouseSensitivity; // use same per-pixel sensitivity; spec is relative
        ApplyYawClamp();

        // Pitch / elevation: anchorY − cursorY (upward drag = look up = less negative elevation).
        // spec: Docs/RE/specs/camera_movement.md §A.3.2 — "anchorY − cursorY … 5e-4". CODE-CONFIRMED.
        var pitchDelta = -rel.Y * MouseDragPitchGain * PitchDragPolarity;
        _elevation = Mathf.Clamp(_elevation + pitchDelta, ElevationMinRad, ElevationMaxRad);

        ApplyCurrentModeTransform();
    }

    // =========================================================================
    // § MOUSE CAPTURE HELPERS
    // =========================================================================

    private void CaptureMouse()
    {
        if (_mouseCaptured) return;
        global::Godot.Input.MouseMode = global::Godot.Input.MouseModeEnum.Captured;
        _mouseCaptured = true;
    }

    private void ReleaseMouse()
    {
        if (!_mouseCaptured) return;
        global::Godot.Input.MouseMode = global::Godot.Input.MouseModeEnum.Visible;
        _mouseCaptured = false;
    }
}