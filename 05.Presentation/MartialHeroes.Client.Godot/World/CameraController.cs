// World/CameraController.cs
//
// Free/orbital debug camera controller for the Martial Heroes Godot client.
//
// PASSIVE: zero game logic, zero use-case calls, zero domain state.
// This node IS the Camera3D (inherits Camera3D so it can MakeCurrent() itself).
// It translates raw OS input into transform mutations only.
//
// Controls summary — printed to the Godot Output panel on _Ready:
//
//   ORBIT mode (default):
//     Right-mouse drag       — orbit (yaw left/right, pitch up/down)
//     Mouse wheel            — zoom in / out (distance clamped to [MinZoom, MaxZoom])
//     Middle-mouse drag      — pan the orbit focus point
//     Tab / F                — switch to FREE-FLY mode
//
//   FREE-FLY mode:
//     Right-mouse drag       — look (yaw/pitch, mouse captured)
//     W / S                  — move forward / backward
//     A / D                  — strafe left / right
//     Q / E                  — move down / up
//     Shift (held)           — fast speed multiplier (~3.3×)
//     Tab / F                — switch back to ORBIT mode
//
//   Both modes:
//     Escape                 — release mouse capture (if captured)
//
// Coordinate convention: same as the rest of the presentation layer.
//   spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 units.
//   spec: WorldCoordinates.ToGodot      — legacy Z negated to Godot Z.
//
// Threading: all node mutation happens on the Godot main thread via _Process /
//   _UnhandledInput / _Input, which are called exclusively on the main thread by Godot.
//   No background threads are used.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "strictly passive rendering".

using Godot;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Dual-mode debug camera controller.  Attach to (or replace) the scene's Camera3D node,
/// then call <see cref="Configure"/> to frame an initial cell, and <see cref="Camera3D.MakeCurrent"/>.
///
/// <para>
/// <b>ORBIT mode</b> (default) — orbits a configurable world-space focus point.<br/>
/// Right-mouse drag = yaw+pitch, wheel = zoom, middle-mouse drag = pan focus.
/// </para>
/// <para>
/// <b>FREE-FLY mode</b> — free-roam first-person flight.<br/>
/// WASD = forward/back/strafe, Q/E = down/up, Shift = fast, right-mouse drag = look.
/// </para>
///
/// Toggle between modes with <c>Tab</c> or <c>F</c>.
/// </summary>
public sealed partial class CameraController : Camera3D
{
    // -----------------------------------------------------------------------
    // Tunable constants
    // -----------------------------------------------------------------------

    /// <summary>
    /// Move speed in Godot units per second (free-fly, normal).
    /// At 1 cell = 1024 units, 600 u/s crosses a cell in ~1.7 s — comfortable for exploration.
    /// spec: Docs/RE/formats/terrain.md §1.4 — "cell size 1024 units". CONFIRMED.
    /// </summary>
    private const float NormalSpeed = 600f;

    /// <summary>
    /// Move speed when Shift is held (free-fly, fast).
    /// ~3.3× normal so you can cross several cells quickly without being uncontrollable.
    /// </summary>
    private const float FastSpeed = 2000f;

    /// <summary>Mouse sensitivity for orbit and free-fly look (radians per pixel).</summary>
    private const float MouseSensitivity = 0.004f;

    /// <summary>Minimum zoom distance from the orbit focus (units).</summary>
    private const float MinZoom = 50f;

    /// <summary>Maximum zoom distance from the orbit focus (units).</summary>
    private const float MaxZoom = 8000f;

    /// <summary>Zoom step per wheel tick (units). Larger = coarser zooming.</summary>
    private const float ZoomStep = 80f;

    /// <summary>
    /// Pan sensitivity: world units of focus movement per screen pixel during middle-drag.
    /// Scales with current zoom distance so panning is proportionally consistent.
    /// </summary>
    private const float PanSensitivityFactor = 0.001f;

    /// <summary>Pitch clamp limit in degrees (exclusive). Prevents gimbal at poles.</summary>
    private const float PitchLimitDeg = 89f;

    // -----------------------------------------------------------------------
    // Mode enum
    // -----------------------------------------------------------------------

    private enum CameraMode { Orbit, FreeFly }

    // -----------------------------------------------------------------------
    // State — orbit
    // -----------------------------------------------------------------------

    /// <summary>The world-space point the orbit camera revolves around.</summary>
    private Vector3 _orbitFocus = Vector3.Zero;

    /// <summary>Yaw angle around the orbit focus in radians (rotation around Y).</summary>
    private float _orbitYaw;

    /// <summary>Pitch angle above/below the orbit focus in radians (rotation around X).</summary>
    private float _orbitPitch;

    /// <summary>Distance from the orbit focus to the camera position.</summary>
    private float _orbitDistance = 1200f;

    // -----------------------------------------------------------------------
    // State — free-fly
    // -----------------------------------------------------------------------

    /// <summary>Yaw angle of the free-fly camera in radians (rotation around Y).</summary>
    private float _flyYaw;

    /// <summary>Pitch angle of the free-fly camera in radians (rotation around X).</summary>
    private float _flyPitch;

    // -----------------------------------------------------------------------
    // Shared state
    // -----------------------------------------------------------------------

    private CameraMode _mode = CameraMode.Orbit;

    /// <summary>True while the right mouse button is held and we are accumulating look deltas.</summary>
    private bool _rightMouseHeld;

    /// <summary>True while the middle mouse button is held (orbit pan).</summary>
    private bool _middleMouseHeld;

    /// <summary>True when the OS mouse cursor is captured (Input.MouseMode == Captured).</summary>
    private bool _mouseCaptured;

    // -----------------------------------------------------------------------
    // Godot lifecycle
    // -----------------------------------------------------------------------

    public override void _Ready()
    {
        // Apply projection defaults.  FOV 60° at Near=0.5, Far=8000 frames the world well.
        // spec: Docs/RE/formats/terrain.md §1.4 — world extent several thousand units.
        Fov = 60f;
        Near = 0.5f;
        Far = 8000f;

        // Print keybind summary so the user can see the controls immediately.
        GD.Print(
            "[CameraController] Ready. " +
            "ORBIT: RMB=orbit, Wheel=zoom, MMB=pan, Tab/F=switch | " +
            "FREE-FLY: RMB=look(captured), WASD=move, Q/E=down/up, Shift=fast, Tab/F=switch | " +
            "Both: Esc=release mouse");

        // Apply initial orbit transform so the camera is positioned correctly on the first frame.
        ApplyOrbitTransform();
    }

    public override void _ExitTree()
    {
        // Always release mouse capture when the node leaves the tree so the user
        // cannot be stuck in captured mode after the scene is closed.
        ReleaseMouse();
    }

    // -----------------------------------------------------------------------
    // Public configuration API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Positions the camera to frame a <paramref name="cellSize"/>-wide cell obliquely,
    /// matching the static placement that was previously hard-coded in
    /// <c>RealWorldRenderer.SpawnCamera</c>.
    ///
    /// The initial position is derived geometrically:
    ///   - For a 60° vertical FOV to comfortably frame a <paramref name="cellSize"/> span,
    ///     the required standoff distance ≈ cellSize / (2 · tan(30°)) ≈ cellSize × 0.866.
    ///   - An oblique view adds ~70° up and ~1000 units back on Z (Godot +Z axis is South),
    ///     so height and look-target are biased accordingly.
    ///   - Resulting orbit-distance is the hypotenuse of (height=720, zBack=1000), ≈ 1233 u.
    ///     We round to the nearest cellSize multiple for a consistent framing.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.4 — "cell size 1024 units". CONFIRMED.
    /// spec: WorldCoordinates.ToGodot — Godot Z = −legacy Z.
    /// </summary>
    /// <param name="focus">Godot-space world position to orbit around (the cell centre).</param>
    /// <param name="cellSize">Width of the cell in Godot units (typically 1024).</param>
    public void Configure(Vector3 focus, float cellSize)
    {
        _orbitFocus = focus;

        // Oblique initial distance: hypotenuse of the (720 up, 1000 back) offset vector
        // previously used in RealWorldRenderer.SpawnCamera.  We parameterise on cellSize
        // so that smaller/larger cells still get a sensible initial framing.
        //   ratio = sqrt(720² + 1000²) / 1024 ≈ 1.205  (for the 1024-unit reference cell)
        //   spec: RealWorldRenderer.SpawnCamera — camPos = (centreX, 720, godotZ + 1000).
        const float referenceCell = 1024f;
        const float referenceDistance = 1232f; // sqrt(720² + 1000²) ≈ 1231.7, rounded.
        _orbitDistance = referenceDistance * (cellSize / referenceCell);
        _orbitDistance = Mathf.Clamp(_orbitDistance, MinZoom, MaxZoom);

        // Initial pitch: arctan(720 / 1000) ≈ 35.75° below horizontal, expressed as positive
        // (camera looks downward), negated because pitch convention is negative = looking down.
        //   spec: RealWorldRenderer.SpawnCamera — height 720, zBack 1000.
        _orbitPitch = -Mathf.Atan2(720f, 1000f); // ≈ −0.624 rad ≈ −35.75°

        // Start facing "from behind" (looking toward −Z, i.e. North in Godot space).
        _orbitYaw = 0f;

        // Synchronise free-fly angles from the current orbit perspective so switching
        // modes immediately feels continuous.
        SyncFlyAnglesFromOrbit();

        // Apply the new transform immediately.
        ApplyOrbitTransform();
    }

    // -----------------------------------------------------------------------
    // Input — mouse buttons and wheel
    //
    // We use _UnhandledInput for mouse events so that UI elements (Control nodes)
    // can consume clicks before we orbit/pan — this prevents accidental camera
    // movement when the user clicks a HUD button.
    // -----------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            // ---------- mode toggle ----------
            case InputEventKey { Pressed: true } key
                when key.Keycode is Key.Tab or Key.F:
                ToggleMode();
                GetViewport().SetInputAsHandled();
                break;

            // ---------- Escape: release mouse ----------
            case InputEventKey { Pressed: true } esc
                when esc.Keycode is Key.Escape && _mouseCaptured:
                ReleaseMouse();
                GetViewport().SetInputAsHandled();
                break;

            // ---------- right mouse button ----------
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Right:
                _rightMouseHeld = mb.Pressed;
                if (mb.Pressed)
                {
                    // Free-fly: capture the mouse for smooth look.
                    // Orbit: we do NOT capture — the user can still see the focus handle.
                    if (_mode == CameraMode.FreeFly)
                        CaptureMouse();
                }
                else
                {
                    // On release, always un-capture so the user regains the cursor.
                    if (_mode == CameraMode.FreeFly)
                        ReleaseMouse();
                }
                GetViewport().SetInputAsHandled();
                break;

            // ---------- middle mouse button (orbit pan) ----------
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Middle:
                _middleMouseHeld = mb.Pressed;
                GetViewport().SetInputAsHandled();
                break;

            // ---------- scroll wheel (orbit zoom) ----------
            case InputEventMouseButton mb
                when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && mb.Pressed:
            {
                if (_mode == CameraMode.Orbit)
                {
                    float delta = mb.ButtonIndex == MouseButton.WheelUp ? -ZoomStep : ZoomStep;
                    _orbitDistance = Mathf.Clamp(_orbitDistance + delta, MinZoom, MaxZoom);
                    ApplyOrbitTransform();
                    GetViewport().SetInputAsHandled();
                }
                break;
            }

            // ---------- mouse motion ----------
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // _Input: also handle mouse motion here because captured-mouse events
    // bypass _UnhandledInput on some platforms when the mouse is captured.
    // Guard against double-processing by checking whether we already handled it.
    // -----------------------------------------------------------------------

    public override void _Input(InputEvent @event)
    {
        // Only handle captured-mouse motion in _Input to avoid double processing
        // when the mouse is not captured (those events go through _UnhandledInput above).
        if (@event is InputEventMouseMotion capturedMotion && _mouseCaptured)
        {
            HandleMouseMotion(capturedMotion);
            GetViewport().SetInputAsHandled();
        }
    }

    // -----------------------------------------------------------------------
    // Mouse motion dispatcher
    // -----------------------------------------------------------------------

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        Vector2 rel = motion.Relative;

        // Guard: ignore zero-length motion to avoid computing atan2(0,0) in edge cases.
        if (rel == Vector2.Zero) return;

        if (_mode == CameraMode.Orbit)
        {
            if (_rightMouseHeld)
            {
                // Orbit: yaw and pitch around the focus point.
                _orbitYaw   -= rel.X * MouseSensitivity;
                _orbitPitch -= rel.Y * MouseSensitivity;
                ClampOrbitPitch();
                ApplyOrbitTransform();
            }
            else if (_middleMouseHeld)
            {
                // Pan: move the focus in the camera's local XY plane.
                // Scale by distance so panning feels consistent regardless of zoom level.
                float panScale = _orbitDistance * PanSensitivityFactor;

                // Camera right and up axes in world space.
                Vector3 right = Transform.Basis.X;
                Vector3 up    = Transform.Basis.Y;

                _orbitFocus -= right * rel.X * panScale;
                _orbitFocus += up    * rel.Y * panScale;

                // Guard: clamp focus to finite values (prevent NaN drift on extreme panning).
                _orbitFocus = ClampToFinite(_orbitFocus, 1e6f);

                ApplyOrbitTransform();
            }
        }
        else // FreeFly
        {
            if (_rightMouseHeld || _mouseCaptured)
            {
                _flyYaw   -= rel.X * MouseSensitivity;
                _flyPitch -= rel.Y * MouseSensitivity;
                ClampFlyPitch();
                ApplyFlyTransform();
            }
        }
    }

    // -----------------------------------------------------------------------
    // _Process: continuous WASD movement (free-fly only)
    // -----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_mode != CameraMode.FreeFly) return;

        // Choose speed: normal or fast (Shift held).
        bool fast  = global::Godot.Input.IsKeyPressed(Key.Shift);
        float speed = fast ? FastSpeed : NormalSpeed;
        float dist  = speed * (float)delta;

        // Movement is relative to the camera's current look direction.
        // Basis.Z points "backwards" in Godot's right-handed system, so negate for forward.
        Vector3 forward = -Transform.Basis.Z;
        Vector3 right   =  Transform.Basis.X;
        // Move straight up/down in world Y (not camera-relative) for Q/E — feels more natural.
        Vector3 worldUp = Vector3.Up;

        Vector3 move = Vector3.Zero;

        if (global::Godot.Input.IsKeyPressed(Key.W)) move += forward;
        if (global::Godot.Input.IsKeyPressed(Key.S)) move -= forward;
        if (global::Godot.Input.IsKeyPressed(Key.A)) move -= right;
        if (global::Godot.Input.IsKeyPressed(Key.D)) move += right;
        if (global::Godot.Input.IsKeyPressed(Key.E)) move += worldUp;
        if (global::Godot.Input.IsKeyPressed(Key.Q)) move -= worldUp;

        if (move == Vector3.Zero) return;

        // Normalise diagonal movement so diagonal is not faster than cardinal.
        // Guard: length-squared check avoids a NaN from Normalize() on zero vector.
        if (move.LengthSquared() > 1e-6f)
            move = move.Normalized();

        Position += move * dist;

        // Guard: keep position finite (defensive against accumulated floating-point error).
        Position = ClampToFinite(Position, 1e7f);
    }

    // -----------------------------------------------------------------------
    // Transform application
    // -----------------------------------------------------------------------

    /// <summary>
    /// Recomputes and sets the camera transform from the current orbit state.
    /// Orbit spherical → Cartesian:
    ///   local offset = (0, 0, distance) rotated by (pitch, yaw).
    ///   position     = focus + offset.
    ///   camera looks toward focus.
    ///
    /// We build the transform manually (not via LookAt) because LookAt can produce
    /// degenerate results when the camera is directly above or below the focus.
    /// We clamp pitch to ±89° so that edge case never triggers.
    /// </summary>
    private void ApplyOrbitTransform()
    {
        // Convert spherical to Cartesian offset from focus.
        //   x = distance · cos(pitch) · sin(yaw)
        //   y = distance · sin(-pitch)   [negative: positive pitch = camera above focus]
        //   z = distance · cos(pitch) · cos(yaw)
        float cosPitch = Mathf.Cos(_orbitPitch);
        float sinPitch = Mathf.Sin(_orbitPitch);
        float cosYaw   = Mathf.Cos(_orbitYaw);
        float sinYaw   = Mathf.Sin(_orbitYaw);

        var offset = new Vector3(
            _orbitDistance * cosPitch * sinYaw,
           -_orbitDistance * sinPitch,          // negative: positive pitch lifts camera above focus
            _orbitDistance * cosPitch * cosYaw
        );

        Vector3 newPos = _orbitFocus + offset;

        // Guard: NaN from a degenerate offset (e.g., zero distance) — fall back to current pos.
        if (!IsFiniteVector(newPos))
            return;

        // Use LookAt after the position is set — it's safe because we clamped pitch.
        // We use a temporary Basis construction instead of the LookAt shortcut to avoid
        // the "look direction is zero" assert when focus == position.
        if ((newPos - _orbitFocus).LengthSquared() < 1e-6f)
            return;

        Position = newPos;
        LookAt(_orbitFocus, Vector3.Up);
    }

    /// <summary>
    /// Recomputes and sets the camera transform from the current free-fly state.
    /// Builds a Basis from yaw (world Y) then pitch (camera-local X).
    /// This avoids gimbal lock because we apply rotations in the correct order.
    /// </summary>
    private void ApplyFlyTransform()
    {
        // Yaw around world Y, then pitch around the rotated X axis.
        Basis yawBasis   = Basis.Identity.Rotated(Vector3.Up,    _flyYaw);
        Basis pitchBasis = yawBasis.Rotated(yawBasis.X,          _flyPitch);

        // Apply rotation; preserve position.
        Transform = new Transform3D(pitchBasis, Position);
    }

    // -----------------------------------------------------------------------
    // Mode switching
    // -----------------------------------------------------------------------

    private void ToggleMode()
    {
        if (_mode == CameraMode.Orbit)
        {
            // Entering free-fly: synchronise fly angles from current camera orientation.
            SyncFlyAnglesFromOrbit();
            _mode = CameraMode.FreeFly;
            GD.Print("[CameraController] Switched to FREE-FLY mode. RMB+drag=look, WASD=move, Q/E=up/down, Shift=fast, Tab/F=orbit.");
        }
        else
        {
            // Leaving free-fly: release mouse, restore orbit framing.
            ReleaseMouse();
            _rightMouseHeld = false;
            // Re-anchor orbit focus in front of the current camera so the transition
            // doesn't teleport the user to a distant focus.
            _orbitFocus    = Position + (-Transform.Basis.Z) * _orbitDistance;
            _orbitDistance = Mathf.Clamp(_orbitDistance, MinZoom, MaxZoom);
            _mode           = CameraMode.Orbit;
            ApplyOrbitTransform();
            GD.Print("[CameraController] Switched to ORBIT mode. RMB+drag=orbit, Wheel=zoom, MMB=pan, Tab/F=free-fly.");
        }
    }

    // -----------------------------------------------------------------------
    // Angle sync helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Derives free-fly yaw/pitch from the current camera Basis so that switching to
    /// free-fly mode starts at the same look direction the orbit camera was using.
    /// </summary>
    private void SyncFlyAnglesFromOrbit()
    {
        // Extract yaw and pitch from the current camera basis.
        // Basis.Z points backwards; forward = -Basis.Z.
        Vector3 fwd = -Transform.Basis.Z;

        // Pitch = arcsin(forward.Y), clamped to prevent NaN on unit-vector edge cases.
        float rawPitch = Mathf.Asin(Mathf.Clamp(fwd.Y, -1f, 1f));
        _flyPitch = rawPitch;

        // Yaw = atan2(forward.X, forward.Z).
        _flyYaw = Mathf.Atan2(fwd.X, fwd.Z);

        ClampFlyPitch();
    }

    // -----------------------------------------------------------------------
    // Clamp helpers
    // -----------------------------------------------------------------------

    private void ClampOrbitPitch()
    {
        float limit = Mathf.DegToRad(PitchLimitDeg);
        _orbitPitch = Mathf.Clamp(_orbitPitch, -limit, limit);
    }

    private void ClampFlyPitch()
    {
        float limit = Mathf.DegToRad(PitchLimitDeg);
        _flyPitch = Mathf.Clamp(_flyPitch, -limit, limit);
    }

    // -----------------------------------------------------------------------
    // Mouse capture helpers
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // Guard utilities
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns true when all three components of <paramref name="v"/> are finite
    /// (not NaN or ±Infinity).  Used defensively wherever floating-point operations
    /// could theoretically produce a NaN (e.g. Atan2(0,0), divide-by-zero, etc.).
    /// </summary>
    private static bool IsFiniteVector(Vector3 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// Clamps each component of <paramref name="v"/> to [−<paramref name="limit"/>, +<paramref name="limit"/>].
    /// If any component is NaN, replaces it with 0.  This prevents unlimited drift when
    /// panning or flying far from the origin.
    /// </summary>
    private static Vector3 ClampToFinite(Vector3 v, float limit)
    {
        float x = float.IsFinite(v.X) ? Mathf.Clamp(v.X, -limit, limit) : 0f;
        float y = float.IsFinite(v.Y) ? Mathf.Clamp(v.Y, -limit, limit) : 0f;
        float z = float.IsFinite(v.Z) ? Mathf.Clamp(v.Z, -limit, limit) : 0f;
        return new Vector3(x, y, z);
    }
}
