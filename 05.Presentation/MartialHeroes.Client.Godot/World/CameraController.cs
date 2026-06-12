// World/CameraController.cs
//
// Faithful re-implementation of the Martial Heroes camera system.
//
// Three user-selectable view modes (Third / First / Static), matching the legacy client's
// OPTION_VIEW_CHAR range 1..3, plus a developer-only FREE-FLY mode (non-original, clearly marked).
//
// ARCHITECTURE
// ─────────────
// This is a pure VIEW node: it reads PlayerController.TargetForCamera and translates raw input
// into camera transform mutations only. It calls no use-case, owns no domain state, and never
// decides whether a position is legal. It may freely mutate its own Camera3D / Node3D each frame.
//
// SPEC CONSTANTS
// ──────────────
// All tuning constants are taken verbatim from the confirmed spec values in
// Docs/RE/specs/camera_movement.md §A — every constant is cited inline.
// Where spec says "(INFERRED/configurable)", the field is [Export]-able so it can be tuned.
//
// FIXED-RADIUS ORBIT MODEL
// ────────────────────────
// The camera is a FIXED-RADIUS orbit (not a spring-arm).
// Eye position = focus + eye-offset rotated by the current (yaw, elevation).
// The eye-offset vector magnitude is constant ≈ 901.39 world units.
// "Zoom" keys change the elevation angle, not the orbit radius.
// spec: Docs/RE/specs/camera_movement.md §A.4 "Fixed-radius orbit model — important correction".
//
// COORDINATE CONVENTION
// ──────────────────────
// All positions accepted/returned are Godot-space (Z negated vs legacy).
// spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 world units.
// spec: Docs/RE/helpers/WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
//
// THREADING
// ─────────
// All node mutation happens on the Godot main thread via _Process / _Input / _UnhandledInput.
// No background threads are used by this class.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "strictly passive rendering".
// spec: Docs/RE/specs/camera_movement.md §A — camera system spec.

using Godot;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Faithful Martial Heroes camera controller.
///
/// <para>
/// Three original modes (selectable by the user):
///   <b>Third</b> (default) — fixed-radius orbit follow, full input, terrain collision.<br/>
///   <b>First</b> — first-person view; eye at player head.<br/>
///   <b>Static</b> — fixed-angle follow; tracks position without orbiting.<br/>
/// </para>
/// <para>
/// One developer-only mode (non-original):
///   <b>FreeFly</b> — free-roam WASD flight for debugging. Press Tab to toggle.
/// </para>
/// <para>
/// ESC with no modal panel open resets to Third-person.
/// spec: Docs/RE/specs/camera_movement.md §A.2.2 call site 2 (ESC reset).
/// </para>
/// </summary>
public sealed partial class CameraController : Camera3D
{
    // =========================================================================
    // § PROJECTION constants — CODE-CONFIRMED per spec §A.7
    // =========================================================================

    /// <summary>
    /// Vertical field of view in degrees. The authoritative in-world gameplay value.
    /// spec: Docs/RE/specs/camera_movement.md §A.7 — "65° vertical FOV". CODE-CONFIRMED.
    /// </summary>
    private const float GameFov = 65f;

    /// <summary>
    /// Near clip plane distance (world units).
    /// spec: Docs/RE/specs/camera_movement.md §A.7 — "near 5.0". CODE-CONFIRMED.
    /// </summary>
    private const float GameNear = 5f;

    /// <summary>
    /// Far clip plane distance (world units).
    /// spec: Docs/RE/specs/camera_movement.md §A.7 — "far 15000.0". CODE-CONFIRMED.
    /// </summary>
    private const float GameFar = 15000f;

    // =========================================================================
    // § ORBIT MODEL constants — CODE-CONFIRMED per spec §A.4 and §A.5
    // =========================================================================

    /// <summary>
    /// Fixed orbit radius. The eye-offset vector is (−750, 0, +500) in legacy world space,
    /// giving magnitude sqrt(750²+500²) ≈ 901.39 world units.
    /// spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Eye-offset vector (−750.0, 0.0, +500.0)
    ///       magnitude ≈ 901.39 units = fixed orbit radius". CODE-CONFIRMED.
    /// </summary>
    private const float OrbitRadius = 901.39f;

    /// <summary>
    /// Default / initial elevation (pitch) angle in radians (−π/6 = −30°).
    /// This is the camera's PITCH/ELEVATION (vertical depression), not yaw.
    /// spec: Docs/RE/specs/camera_movement.md §A.5.2 Third — "Default pitch −π/6 (−30°)". CODE-CONFIRMED.
    /// Note: we store elevation as a negative value; −30° means the camera looks down 30°.
    /// </summary>
    private const float DefaultElevationRad = -Mathf.Pi / 6f; // −30°

    /// <summary>
    /// Hard minimum elevation angle (most downward; camera looking nearly straight down).
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Elevation angle clamp [−90.0, −12.0] degrees". CODE-CONFIRMED.
    /// </summary>
    private const float ElevationMinRad = -Mathf.Pi / 2f; // −90°

    /// <summary>
    /// Hard maximum elevation angle (least downward; camera shallowest depression above the player).
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Elevation angle clamp [−90.0, −12.0] degrees". CODE-CONFIRMED.
    /// </summary>
    private const float ElevationMaxRad = -12f * Mathf.Pi / 180f; // −12°

    // =========================================================================
    // § YAW CLAMP constants — CODE-CONFIRMED per spec §A.4 and §A.5
    // =========================================================================

    /// <summary>
    /// Absolute yaw lower bound (symmetric), shared by all player modes.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "[−π/2, +π/2] = [−1.5708, +1.5708] base bound". CODE-CONFIRMED.
    /// </summary>
    private const float YawMin = -Mathf.Pi / 2f; // −π/2 ≈ −1.5708

    /// <summary>
    /// Third-person upper yaw bound: π/2 × 0.9 ≈ +1.4137 rad.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Third upper eased to +1.4137". CODE-CONFIRMED.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Third yaw upper ease factor 0.9". CODE-CONFIRMED.
    /// </summary>
    private const float YawMaxThird = Mathf.Pi / 2f * 0.9f; // ≈ +1.4137

    /// <summary>
    /// Symmetric upper yaw bound for First/Static modes.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "base bound [−π/2, +π/2]". CODE-CONFIRMED.
    /// </summary>
    private const float YawMaxSymmetric = Mathf.Pi / 2f; // +π/2 ≈ +1.5708

    // =========================================================================
    // § INTEGRATOR / SMOOTHING constants — CODE-CONFIRMED per spec §A.4
    // =========================================================================

    /// <summary>
    /// Time-delta scale: milliseconds → seconds.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "1e-3". CODE-CONFIRMED.
    /// </summary>
    private const float TimeDeltaScale = 1e-3f;

    /// <summary>
    /// Per-frame friction when no keyboard input is pressed. Multiplies the rate integrator.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "No-input rate decay (friction) 0.6". CODE-CONFIRMED.
    /// (Static mode uses 0.8 instead.)
    /// </summary>
    private const float FrictionDefault = 0.6f;

    /// <summary>Static mode uses stronger friction (yaw is fixed).</summary>
    private const float FrictionStatic = 0.8f;

    /// <summary>
    /// Per-frame step added on a keyboard input.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Keyboard input → rate gain 0.3". CODE-CONFIRMED.
    /// </summary>
    private const float KeyboardGain = 0.3f;

    /// <summary>
    /// Orbit-step rate clamp (both min and max).
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Zoom-rate / orbit-step clamp [−0.1, +0.1]". CODE-CONFIRMED.
    /// </summary>
    private const float OrbitStepRateClamp = 0.1f;

    /// <summary>
    /// Rate dead-zone: rates with magnitude below this snap to 0.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Rate dead-zone 1e-3". CODE-CONFIRMED.
    /// </summary>
    private const float RateDeadZone = 1e-3f;

    /// <summary>
    /// Mouse-drag pitch gain (cursor-Y delta → pitch rate).
    /// spec: Docs/RE/specs/camera_movement.md §A.3.2 — "mouse-drag pitch gain = 5e-4". CODE-CONFIRMED.
    /// </summary>
    private const float MouseDragPitchGain = 5e-4f;

    /// <summary>
    /// Wheel / other-button zoom scale.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Wheel / other-button zoom scale 0.01". CODE-CONFIRMED.
    /// </summary>
    private const float WheelZoomScale = 0.01f;

    // =========================================================================
    // § TERRAIN COLLISION constants — CODE-CONFIRMED per spec §A.6
    // =========================================================================

    /// <summary>
    /// Camera eye must stay at or above terrainHeight + this value.
    /// spec: Docs/RE/specs/camera_movement.md §A.6 — "Terrain-collision camera lift 3.8". CODE-CONFIRMED.
    /// </summary>
    private const float TerrainLift = 3.8f;

    /// <summary>
    /// Additive bias applied after terrain clamping.
    /// spec: Docs/RE/specs/camera_movement.md §A.6 — "Collision Y-bias step +2.0". CODE-CONFIRMED.
    /// </summary>
    private const float TerrainYBias = 2.0f;

    /// <summary>
    /// Yaw-rate forced value on a hard terrain hit (to stop fighting the ground).
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — "Terrain hard-hit yaw kill −0.01". CODE-CONFIRMED.
    /// </summary>
    private const float TerrainHitYawKill = -0.01f;

    /// <summary>
    /// Focus look-at Z offset for Third-person mode.
    /// spec: Docs/RE/specs/camera_movement.md §A.5.2 — "Focus Z −40". CODE-CONFIRMED.
    /// </summary>
    private const float FocusZThird = -40f;

    // =========================================================================
    // § FREE-FLY constants (DEVELOPER-ONLY, non-original)
    // =========================================================================

    /// <summary>Move speed in Godot units per second (free-fly, normal). Developer mode only.</summary>
    private const float FlyNormalSpeed = 600f;

    /// <summary>Move speed with Shift held (free-fly, fast). Developer mode only.</summary>
    private const float FlyFastSpeed = 2000f;

    /// <summary>Mouse sensitivity for free-fly look (radians per pixel). Developer mode only.</summary>
    private const float FlyMouseSensitivity = 0.004f;

    // =========================================================================
    // § VIEW MODE ENUM
    // =========================================================================

    /// <summary>
    /// Camera view mode. Matches the legacy OPTION_VIEW_CHAR range 1..3 for the first three values.
    /// spec: Docs/RE/specs/camera_movement.md §A.2.1 — "clamped to range 1..3 on load". CODE-CONFIRMED.
    /// </summary>
    public enum ViewMode
    {
        /// <summary>
        /// Over-the-shoulder orbit follow camera (OPTION_VIEW_CHAR = 1). Default.
        /// spec: Docs/RE/specs/camera_movement.md §A.5.2 — Third. CODE-CONFIRMED.
        /// </summary>
        Third = 1,

        /// <summary>
        /// First-person view (OPTION_VIEW_CHAR = 2). Eye collapses to player head.
        /// spec: Docs/RE/specs/camera_movement.md §A.5.2 — First. CODE-CONFIRMED.
        /// </summary>
        First = 2,

        /// <summary>
        /// Fixed-angle tracking; follows position, never orbits (OPTION_VIEW_CHAR = 3).
        /// spec: Docs/RE/specs/camera_movement.md §A.5.2 — Static. CODE-CONFIRMED.
        /// </summary>
        Static = 3,

        /// <summary>
        /// Developer-only free-fly debug camera. Not an original client mode.
        /// Non-original. Toggled by Tab key.
        /// </summary>
        FreeFly = 99,
    }

    // =========================================================================
    // § EXPORTED / CONFIGURABLE fields (spec says polarity is INFERRED)
    // =========================================================================

    /// <summary>
    /// Sign applied to the wheel / elevation-key orbit step. +1 = wheel-up raises camera, −1 = lowers.
    /// spec: Docs/RE/specs/camera_movement.md §D item 1 — "camera action polarity INFERRED/configurable".
    /// </summary>
    [Export]
    public float ElevationKeyPolarity { get; set; } = 1f;

    /// <summary>
    /// Sign applied to the pitch drag from right-mouse vertical delta.
    /// spec: Docs/RE/specs/camera_movement.md §D item 1 — "camera action polarity INFERRED/configurable".
    /// </summary>
    [Export]
    public float PitchDragPolarity { get; set; } = 1f;

    // =========================================================================
    // § STATE — shared orbit
    // =========================================================================

    /// <summary>World-space focus point the camera orbits around (player position, optionally + focus-Z offset).</summary>
    private Vector3 _focus = Vector3.Zero;

    /// <summary>
    /// Current yaw offset around the player, in radians.
    /// Zero = player's default facing. Clamped per mode.
    /// </summary>
    private float _yaw;

    /// <summary>
    /// Current elevation angle in radians. Negative = looking down.
    /// Clamped to [ElevationMinRad, ElevationMaxRad].
    /// </summary>
    private float _elevation = DefaultElevationRad;

    /// <summary>
    /// Per-frame yaw rate integrator (degrees / second equivalent). Decays with friction.
    /// spec: Docs/RE/specs/camera_movement.md §A.4 — rate integrator with gain/friction.
    /// </summary>
    private float _yawRate;

    /// <summary>
    /// Per-frame elevation rate integrator. Decays with friction.
    /// </summary>
    private float _elevationRate;

    /// <summary>True while the right mouse button is held (starts look drag).</summary>
    private bool _rightMouseHeld;

    /// <summary>True when the OS mouse cursor is captured (free-fly mode).</summary>
    private bool _mouseCaptured;

    // =========================================================================
    // § STATE — free-fly (developer only)
    // =========================================================================

    private float _flyYaw;
    private float _flyPitch;

    // =========================================================================
    // § MODE STATE
    // =========================================================================

    /// <summary>Active view mode. Changed via <see cref="SetViewMode"/>.</summary>
    private ViewMode _mode = ViewMode.Third;

    /// <summary>Previous mode before entering free-fly (so ESC / Tab returns to it).</summary>
    private ViewMode _modeBeforeFreeFly = ViewMode.Third;

    // =========================================================================
    // § GROUND HEIGHT DELEGATE
    // =========================================================================

    /// <summary>
    /// Optional per-XZ terrain height query for camera ground collision.
    /// Signature: <c>float GetHeight(float legacyWorldX, float legacyWorldZ)</c>.
    /// When null, no terrain clamping is applied.
    ///
    /// The caller supplies legacy world coordinates (X, Z) — NOT Godot coordinates.
    /// Convert the eye position: legacyX = eyePos.X, legacyZ = −eyePos.Z.
    ///
    /// spec: Docs/RE/specs/camera_movement.md §A.6 — terrain height clamp (Third only).
    /// </summary>
    public Func<float, float, float>? GroundHeightFunc { get; set; }

    // =========================================================================
    // § PLAYER FOLLOW TARGET
    // =========================================================================

    /// <summary>
    /// Set by the renderer each frame (or whenever the player moves) to the avatar's
    /// Godot-space world position. The camera uses this as its orbit focus.
    /// </summary>
    public Vector3 PlayerGodotPosition { get; set; } = Vector3.Zero;

    // =========================================================================
    // § GODOT LIFECYCLE
    // =========================================================================

    public override void _Ready()
    {
        // Apply spec-confirmed projection parameters.
        // spec: Docs/RE/specs/camera_movement.md §A.7 — "65° vertical FOV, near 5, far 15000". CODE-CONFIRMED.
        Fov = GameFov;
        Near = GameNear;
        Far = GameFar;

        // Seed the elevation to the spec default.
        _elevation = DefaultElevationRad;
        _yaw = 0f;

        // Apply initial transform so the camera is positioned correctly on the first frame.
        ApplyThirdPersonTransform();

        // spec: Docs/RE/specs/camera_movement.md §A.7 — FOV 65°/near5/far15000. CODE-CONFIRMED.
        // spec: Docs/RE/specs/camera_movement.md §A.5.1 — radius≈901, pitch−30°. CODE-CONFIRMED.
        // spec: Docs/RE/specs/camera_movement.md §A.4 — elev clamp [−90°,−12°], yaw clamp Third [−90°,+81°≈+1.4137rad]. CODE-CONFIRMED.
        GD.Print(
            "[Camera] mode=Third | FOV=65deg near=5 far=15000 | " +
            $"radius={OrbitRadius:F1}u | elev_default={Mathf.RadToDeg(DefaultElevationRad):F1}deg | " +
            $"elev_clamp=[{Mathf.RadToDeg(ElevationMinRad):F0}deg,{Mathf.RadToDeg(ElevationMaxRad):F0}deg] | " +
            $"yaw_clamp_third=[{Mathf.RadToDeg(YawMin):F1}deg,{Mathf.RadToDeg(YawMaxThird):F1}deg ({YawMaxThird:F4}rad)] | " +
            "RMB=orbit wheel=elevation ESC=reset-to-Third Tab=devFreeFly");
    }

    public override void _ExitTree()
    {
        ReleaseMouse();
    }

    // =========================================================================
    // § PUBLIC API — configuration
    // =========================================================================

    /// <summary>
    /// Positions the camera to frame a cell's centre on first load. Reproduces the
    /// oblique aerial framing previously used by RealWorldRenderer.SpawnCamera.
    /// Keeps the Third-person model while providing a reasonable initial view of the world.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 world units. CONFIRMED.
    /// </summary>
    /// <param name="focus">Godot-space world position to follow (the cell centre).</param>
    /// <param name="cellSize">Width of the cell in Godot units (typically 1024).</param>
    public void Configure(Vector3 focus, float cellSize)
    {
        _ = cellSize; // kept in API for caller compatibility; radius is fixed per spec.
        PlayerGodotPosition = focus;
        _focus = focus;
        _elevation = DefaultElevationRad;
        _yaw = 0f;
        _yawRate = 0f;
        _elevationRate = 0f;
        ApplyThirdPersonTransform();

        GD.Print($"[Camera] Configure: focus={focus}, mode={_mode}, radius={OrbitRadius:F1}u.");
    }

    /// <summary>
    /// Switches to the requested view mode.
    /// ESC resets to Third-person (call site 2 in spec §A.2.2).
    /// spec: Docs/RE/specs/camera_movement.md §A.2.2 — "exactly three call sites". CODE-CONFIRMED.
    /// </summary>
    public void SetViewMode(ViewMode newMode)
    {
        if (_mode == newMode) return;

        if (newMode == ViewMode.FreeFly)
        {
            _modeBeforeFreeFly = _mode == ViewMode.FreeFly ? ViewMode.Third : _mode;
            SyncFlyAnglesFromCurrentBasis();
            _mode = ViewMode.FreeFly;
            GD.Print("[Camera] Entered DEVELOPER FREE-FLY (non-original). Tab to return.");
        }
        else
        {
            ReleaseMouse();
            _rightMouseHeld = false;

            if (_mode == ViewMode.FreeFly)
            {
                // Returning from free-fly: re-anchor focus to the player position.
                _focus = PlayerGodotPosition;
            }

            _mode = newMode;
            ApplyCurrentModeTransform();
            GD.Print($"[Camera] Switched to {newMode}.");
        }
    }

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
                    float sign = (mb.ButtonIndex == MouseButton.WheelUp ? 1f : -1f) * ElevationKeyPolarity;
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
        Vector2 rel = motion.Relative;
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
        float pitchDelta = (-rel.Y) * MouseDragPitchGain * PitchDragPolarity;
        _elevation = Mathf.Clamp(_elevation + pitchDelta, ElevationMinRad, ElevationMaxRad);

        ApplyCurrentModeTransform();
    }

    // =========================================================================
    // § _PROCESS — keyboard input and per-frame orbit integration
    // =========================================================================

    public override void _Process(double delta)
    {
        float dt = (float)delta;

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

        bool anyKey = false;
        float friction = _mode == ViewMode.Static ? FrictionStatic : FrictionDefault;

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
    /// Third-person: fixed-radius orbit + terrain collision.
    ///
    /// Eye = focus + eyeOffset(yaw, elevation)
    /// where |eyeOffset| = OrbitRadius (fixed; no scale).
    ///
    /// spec: Docs/RE/specs/camera_movement.md §A.4 Fixed-radius orbit model. CODE-CONFIRMED.
    /// spec: Docs/RE/specs/camera_movement.md §A.5.2 Third — terrain collision. CODE-CONFIRMED.
    /// spec: Docs/RE/specs/camera_movement.md §A.5.1 — focus Z = −40 (base / Third). CODE-CONFIRMED.
    /// </summary>
    private void ApplyThirdPersonTransform()
    {
        // Build focus point: player position + the spec "Focus Z −40" vertical/focus offset.
        // spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Focus / look-at Z −40.0". CODE-CONFIRMED.
        // In Godot space the legacy Z-40 offset is applied as a Godot +Z shift (Z negated).
        // We apply it as a Y bias so the camera looks at a point 40 units above the ground
        // (approximately head-height), which is the standard over-the-shoulder feel.
        Vector3 focusPoint = _focus + new Vector3(0f, -FocusZThird, 0f); // −(−40) = +40 on Y

        // Compute eye offset for the fixed-radius orbit.
        // In legacy-space the eye-offset is (−750, 0, +500); its magnitude ≈ 901.39.
        // We represent it as (yaw, elevation) polar coordinates at radius OrbitRadius.
        // Convention: elevation < 0 → camera is above the focus looking downward.
        float cosEl = Mathf.Cos(_elevation);
        float sinEl = Mathf.Sin(_elevation);
        float cosYaw = Mathf.Cos(_yaw);
        float sinYaw = Mathf.Sin(_yaw);

        // Spherical → Cartesian eye offset (Godot right-handed Y-up):
        //   x = r · cos(elev) · sin(yaw)
        //   y = −r · sin(elev)     [negative elev → positive y, camera above focus]
        //   z = r · cos(elev) · cos(yaw)
        var eyeOffset = new Vector3(
            OrbitRadius * cosEl * sinYaw,
            -OrbitRadius * sinEl,
            OrbitRadius * cosEl * cosYaw
        );

        Vector3 eyePos = focusPoint + eyeOffset;

        // ── Terrain collision — Third only ─────────────────────────────────────
        // spec: Docs/RE/specs/camera_movement.md §A.6 — vertical slide, no horizontal pull-in. CODE-CONFIRMED.
        if (GroundHeightFunc is not null)
        {
            try
            {
                // Convert Godot eye X/Z to legacy world coordinates for the heightmap query.
                // spec: WorldCoordinates.ToLegacy — legacyZ = −godotZ.
                float legacyX = eyePos.X;
                float legacyZ = -eyePos.Z;
                float terrainY = GroundHeightFunc(legacyX, legacyZ);

                // Clamp eye Y to terrain + TerrainLift + TerrainYBias (slide, not snap).
                // spec: Docs/RE/specs/camera_movement.md §A.6 — "terrainHeight + 3.8, +2.0 bias". CODE-CONFIRMED.
                float minY = terrainY + TerrainLift + TerrainYBias;
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
        }

        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - focusPoint).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(focusPoint, Vector3.Up);
    }

    /// <summary>
    /// First-person: eye collapses to player head position; yaw/pitch look.
    /// spec: Docs/RE/specs/camera_movement.md §A.5.2 First — "Eye sits at the player's head". CODE-CONFIRMED.
    /// No terrain collision.
    /// </summary>
    private void ApplyFirstPersonTransform()
    {
        // In first-person the follow radius collapses to 0 — eye is at the player.
        // We approximate "player head" as the player position + some height offset.
        const float HeadHeight = 15f; // rough head height in world units (legacy scale)
        Vector3 eyePos = _focus + new Vector3(0f, HeadHeight, 0f);

        if (!IsFiniteVector(eyePos)) return;

        // Build basis from yaw/elevation (like free-fly but from the player eye).
        Basis yawBasis = Basis.Identity.Rotated(Vector3.Up, _yaw);
        Basis fullBasis = yawBasis.Rotated(yawBasis.X, _elevation);

        Transform = new Transform3D(fullBasis, eyePos);
    }

    /// <summary>
    /// Static: fixed-angle follow. Tracks position, never orbits.
    /// spec: Docs/RE/specs/camera_movement.md §A.5.2 Static — "follows position, never rotates". CODE-CONFIRMED.
    /// Yaw is fixed; only elevation key is polled (see _Process).
    /// </summary>
    private void ApplyStaticTransform()
    {
        // Static uses a fixed yaw (locked at its initial seed = 0).
        // Only the elevation is updateable.
        const float FixedYaw = 0f;

        float cosEl = Mathf.Cos(_elevation);
        float sinEl = Mathf.Sin(_elevation);
        float cosYaw = Mathf.Cos(FixedYaw);
        float sinYaw = Mathf.Sin(FixedYaw);

        var eyeOffset = new Vector3(
            OrbitRadius * cosEl * sinYaw,
            -OrbitRadius * sinEl,
            OrbitRadius * cosEl * cosYaw
        );

        Vector3 eyePos = _focus + eyeOffset;
        if (!IsFiniteVector(eyePos)) return;
        if ((eyePos - _focus).LengthSquared() < 1e-6f) return;

        Position = eyePos;
        LookAt(_focus, Vector3.Up);
    }

    // =========================================================================
    // § FREE-FLY (developer mode, non-original)
    // =========================================================================

    private void ProcessFreeFly(float dt)
    {
        bool fast = global::Godot.Input.IsKeyPressed(Key.Shift);
        float speed = fast ? FlyFastSpeed : FlyNormalSpeed;
        float dist = speed * dt;

        Vector3 forward = -Transform.Basis.Z;
        Vector3 right = Transform.Basis.X;

        Vector3 move = Vector3.Zero;
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
        Basis yawBasis = Basis.Identity.Rotated(Vector3.Up, _flyYaw);
        Basis pitchBasis = yawBasis.Rotated(yawBasis.X, _flyPitch);
        Transform = new Transform3D(pitchBasis, Position);
    }

    private void SyncFlyAnglesFromCurrentBasis()
    {
        Vector3 fwd = -Transform.Basis.Z;
        _flyPitch = Mathf.Asin(Mathf.Clamp(fwd.Y, -1f, 1f));
        _flyYaw = Mathf.Atan2(fwd.X, fwd.Z);
        ClampFlyPitch();
    }

    private void ClampFlyPitch()
    {
        float limit = Mathf.DegToRad(89f);
        _flyPitch = Mathf.Clamp(_flyPitch, -limit, limit);
    }

    // =========================================================================
    // § CLAMP HELPERS
    // =========================================================================

    private void ApplyYawClamp()
    {
        float yawMax = _mode == ViewMode.Third ? YawMaxThird : YawMaxSymmetric;
        // spec: Docs/RE/specs/camera_movement.md §A.4 — Third upper eased +1.4137; base ±π/2. CODE-CONFIRMED.
        _yaw = Mathf.Clamp(_yaw, YawMin, yawMax);
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

    // =========================================================================
    // § GUARD UTILITIES
    // =========================================================================

    private static bool IsFiniteVector(Vector3 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static Vector3 ClampToFinite(Vector3 v, float limit)
    {
        float x = float.IsFinite(v.X) ? Mathf.Clamp(v.X, -limit, limit) : 0f;
        float y = float.IsFinite(v.Y) ? Mathf.Clamp(v.Y, -limit, limit) : 0f;
        float z = float.IsFinite(v.Z) ? Mathf.Clamp(v.Z, -limit, limit) : 0f;
        return new Vector3(x, y, z);
    }
}