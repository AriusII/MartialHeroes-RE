using Godot;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class CameraController
{
    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true } key when key.Keycode is Key.Tab:
                if (CurrentMode == ViewMode.FreeFly)
                    SetViewMode(_modeBeforeFreeFly);
                else
                    SetViewMode(ViewMode.FreeFly);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true } esc when esc.Keycode is Key.Escape:
                if (CurrentMode == ViewMode.FreeFly && _mouseCaptured)
                {
                    ReleaseMouse();
                    GetViewport().SetInputAsHandled();
                }
                else if (CurrentMode != ViewMode.Third)
                {
                    SetViewMode(ViewMode.Third);
                    GetViewport().SetInputAsHandled();
                }

                break;

            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Right:
                _rightMouseHeld = mb.Pressed;
                if (mb.Pressed && CurrentMode == ViewMode.FreeFly)
                    CaptureMouse();
                else if (!mb.Pressed && CurrentMode == ViewMode.FreeFly)
                    ReleaseMouse();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton mb
                when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && mb.Pressed:
                if (CurrentMode != ViewMode.FreeFly)
                {
                    var sign = (mb.ButtonIndex == MouseButton.WheelUp ? 1f : -1f) * ElevationKeyPolarity;
                    _elevationRate += sign * WheelZoomScale;
                    _elevationRate = Mathf.Clamp(_elevationRate, -OrbitStepRateClamp, OrbitStepRateClamp);
                    GetViewport().SetInputAsHandled();
                }

                break;

            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
        }
    }


    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion capturedMotion && _mouseCaptured)
        {
            HandleMouseMotion(capturedMotion);
            GetViewport().SetInputAsHandled();
        }
    }


    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        var rel = motion.Relative;
        if (rel == Vector2.Zero) return;

        if (CurrentMode == ViewMode.FreeFly)
        {
            if (_rightMouseHeld || _mouseCaptured)
            {
                _flyYaw -= rel.X * FlyMouseSensitivity;
                _flyPitch -= rel.Y * FlyMouseSensitivity;
                ClampFlyPitch();
                ApplyFlyTransform();
            }

            return;
        }

        if (!_rightMouseHeld) return;

        if (CurrentMode != ViewMode.Static)
        {
            _yaw += rel.X * FlyMouseSensitivity;
            ApplyYawClamp();
        }

        var pitchDelta = -rel.Y * MouseDragPitchGain * PitchDragPolarity;
        _elevation = Mathf.Clamp(_elevation + pitchDelta, ElevationMinRad, ElevationMaxRad);

        ApplyCurrentModeTransform();
    }


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