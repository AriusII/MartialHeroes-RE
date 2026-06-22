// World/CameraController.cs
//
// Faithful re-implementation of the Martial Heroes camera system.
//
// Three user-selectable view modes (Third / First / Static), matching the legacy client's
// OPTION_VIEW_CHAR range 1..3, plus a developer-only FREE-FLY mode (non-original, clearly marked).
//
// ARCHITECTURE
// ─────────────
// This is a pure VIEW node: it reads PlayerGodotPosition (pushed each frame by RealWorldRenderer
// from the live local-player node) and translates raw input
// into camera transform mutations only. It calls no use-case, owns no domain state, and never
// decides whether a position is legal. It may freely mutate its own Camera3D / Node3D each frame.
//
// SPEC CONSTANTS
// ──────────────
// All tuning constants are taken verbatim from the confirmed spec values in
// Docs/RE/specs/camera_movement.md §A — every constant is cited inline.
// Where spec says "(INFERRED/configurable)", the field is [Export]-able so it can be tuned.
//
// FIXED-RADIUS ORBIT MODEL — ROTATED EYE-OFFSET
// ──────────────────────────────────────────────
// The camera is a FIXED-RADIUS orbit (not a spring-arm).
// Eye position = focus + Rotate(eyeOffsetSeed, yaw, elevation)
// where eyeOffsetSeed = (−750, 0, −500) in Godot space (= legacy (−750,0,+500) with Z negated).
// Magnitude ≈ 901.39 world units = fixed orbit radius; NEVER scaled.
// Yaw and elevation rotate this seed vector directly — no spherical-polar approximation.
// "Zoom" / elevation keys change the elevation angle, not the orbit radius.
// spec: Docs/RE/specs/camera_movement.md §A.4 "Fixed-radius orbit model — important correction".
// spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Eye-offset vector (−750.0, 0.0, +500.0)". CODE-CONFIRMED.
// spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
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
///     Faithful Martial Heroes camera controller.
///     <para>
///         Three original modes (selectable by the user via F1/F2/F3):
///         <b>Third</b> (default, F1) — fixed-radius orbit follow, full input, terrain collision.<br />
///         <b>First</b> (F2) — short-boom view from player head; Focus Z −55, yaw seed π.<br />
///         <b>Static</b> (F3) — fixed-angle follow; tracks position, yaw from player facing.<br />
///     </para>
///     <para>
///         Eye placement uses the legacy rotated eye-offset model for all three modes:
///         eye = focus + Rotate((−750,0,−500)_Godot, yaw, elevation).
///         spec: Docs/RE/specs/camera_movement.md §A.5.1 — Eye-offset seed (−750,0,+500) legacy. CODE-CONFIRMED.
///     </para>
///     <para>
///         One developer-only mode (non-original):
///         <b>FreeFly</b> — free-roam WASD flight for debugging. Press Tab to toggle.
///     </para>
///     <para>
///         ESC with no modal panel open resets to Third-person.
///         spec: Docs/RE/specs/camera_movement.md §A.2.2 call site 2 (ESC reset).
///     </para>
/// </summary>
public sealed partial class CameraController : Camera3D
{
    // =========================================================================
    // § VIEW MODE ENUM
    // =========================================================================

    /// <summary>
    ///     Camera view mode. Matches the legacy OPTION_VIEW_CHAR range 1..3 for the first three values.
    ///     spec: Docs/RE/specs/camera_movement.md §A.2.1 — "clamped to range 1..3 on load". CODE-CONFIRMED.
    /// </summary>
    public enum ViewMode
    {
        /// <summary>
        ///     Over-the-shoulder orbit follow camera (OPTION_VIEW_CHAR = 1). Default.
        ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 — Third. CODE-CONFIRMED.
        /// </summary>
        Third = 1,

        /// <summary>
        ///     First-person view (OPTION_VIEW_CHAR = 2). Eye collapses to player head.
        ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 — First. CODE-CONFIRMED.
        /// </summary>
        First = 2,

        /// <summary>
        ///     Fixed-angle tracking; follows position, never orbits (OPTION_VIEW_CHAR = 3).
        ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 — Static. CODE-CONFIRMED.
        /// </summary>
        Static = 3,

        /// <summary>
        ///     Developer-only free-fly debug camera. Not an original client mode.
        ///     Non-original. Toggled by Tab key.
        /// </summary>
        FreeFly = 99
    }
    // =========================================================================
    // § PROJECTION constants — CODE-CONFIRMED per spec §A.7
    // =========================================================================

    /// <summary>
    ///     Vertical field of view in degrees. The authoritative in-world gameplay value.
    ///     spec: Docs/RE/specs/camera_movement.md §A.7 — "65° vertical FOV". CODE-CONFIRMED.
    /// </summary>
    private const float GameFov = 65f;

    /// <summary>
    ///     Near clip plane distance (world units).
    ///     spec: Docs/RE/specs/camera_movement.md §A.7 — "near 5.0". CODE-CONFIRMED.
    /// </summary>
    private const float GameNear = 5f;

    /// <summary>
    ///     Far clip plane distance (world units).
    ///     spec: Docs/RE/specs/camera_movement.md §A.7 — "far 15000.0". CODE-CONFIRMED.
    /// </summary>
    private const float GameFar = 15000f;

    // =========================================================================
    // § ORBIT MODEL constants — CODE-CONFIRMED per spec §A.4 and §A.5
    // =========================================================================

    /// <summary>
    ///     Fixed orbit radius. The eye-offset vector is (−750, 0, +500) in legacy world space,
    ///     giving magnitude sqrt(750²+500²) ≈ 901.39 world units.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Eye-offset vector (−750.0, 0.0, +500.0)
    ///     magnitude ≈ 901.39 units = fixed orbit radius". CODE-CONFIRMED.
    /// </summary>
    private const float OrbitRadius = 901.39f;

    /// <summary>
    ///     Default / initial elevation (pitch) angle in radians (−π/6 = −30°).
    ///     This is the camera's PITCH/ELEVATION (vertical depression), not yaw.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 Third — "Default pitch −π/6 (−30°)". CODE-CONFIRMED.
    ///     Note: we store elevation as a negative value; −30° means the camera looks down 30°.
    /// </summary>
    private const float DefaultElevationRad = -Mathf.Pi / 6f; // −30°

    /// <summary>
    ///     Hard minimum elevation angle (most downward; camera looking nearly straight down).
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Elevation angle clamp [−90.0, −12.0] degrees". CODE-CONFIRMED.
    /// </summary>
    private const float ElevationMinRad = -Mathf.Pi / 2f; // −90°

    /// <summary>
    ///     Hard maximum elevation angle (least downward; camera shallowest depression above the player).
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Elevation angle clamp [−90.0, −12.0] degrees". CODE-CONFIRMED.
    /// </summary>
    private const float ElevationMaxRad = -12f * Mathf.Pi / 180f; // −12°

    // =========================================================================
    // § YAW CLAMP constants — CODE-CONFIRMED per spec §A.4 and §A.5
    // =========================================================================

    /// <summary>
    ///     Absolute yaw lower bound (symmetric), shared by all player modes.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "[−π/2, +π/2] = [−1.5708, +1.5708] base bound". CODE-CONFIRMED.
    /// </summary>
    private const float YawMin = -Mathf.Pi / 2f; // −π/2 ≈ −1.5708

    /// <summary>
    ///     Third-person upper yaw bound: π/2 × 0.9 ≈ +1.4137 rad.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Third upper eased to +1.4137". CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Third yaw upper ease factor 0.9". CODE-CONFIRMED.
    /// </summary>
    private const float YawMaxThird = Mathf.Pi / 2f * 0.9f; // ≈ +1.4137

    /// <summary>
    ///     Symmetric upper yaw bound for First/Static modes.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "base bound [−π/2, +π/2]". CODE-CONFIRMED.
    /// </summary>
    private const float YawMaxSymmetric = Mathf.Pi / 2f; // +π/2 ≈ +1.5708

    // =========================================================================
    // § INTEGRATOR / SMOOTHING constants — CODE-CONFIRMED per spec §A.4
    // =========================================================================

    /// <summary>
    ///     Time-delta scale: milliseconds → seconds.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "1e-3". CODE-CONFIRMED.
    /// </summary>
    private const float TimeDeltaScale = 1e-3f;

    /// <summary>
    ///     Per-frame friction when no keyboard input is pressed. Multiplies the rate integrator.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "No-input rate decay (friction) 0.6". CODE-CONFIRMED.
    ///     (Static mode uses 0.8 instead.)
    /// </summary>
    private const float FrictionDefault = 0.6f;

    /// <summary>Static mode uses stronger friction (yaw is fixed).</summary>
    private const float FrictionStatic = 0.8f;

    /// <summary>
    ///     Per-frame step added on a keyboard input.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Keyboard input → rate gain 0.3". CODE-CONFIRMED.
    /// </summary>
    private const float KeyboardGain = 0.3f;

    /// <summary>
    ///     Orbit-step rate clamp (both min and max).
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Zoom-rate / orbit-step clamp [−0.1, +0.1]". CODE-CONFIRMED.
    /// </summary>
    private const float OrbitStepRateClamp = 0.1f;

    /// <summary>
    ///     Rate dead-zone: rates with magnitude below this snap to 0.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Rate dead-zone 1e-3". CODE-CONFIRMED.
    /// </summary>
    private const float RateDeadZone = 1e-3f;

    /// <summary>
    ///     Mouse-drag pitch gain (cursor-Y delta → pitch rate).
    ///     spec: Docs/RE/specs/camera_movement.md §A.3.2 — "mouse-drag pitch gain = 5e-4". CODE-CONFIRMED.
    /// </summary>
    private const float MouseDragPitchGain = 5e-4f;

    /// <summary>
    ///     Wheel / other-button zoom scale.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Wheel / other-button zoom scale 0.01". CODE-CONFIRMED.
    /// </summary>
    private const float WheelZoomScale = 0.01f;

    // =========================================================================
    // § TERRAIN COLLISION constants — CODE-CONFIRMED per spec §A.6
    // =========================================================================

    /// <summary>
    ///     Camera eye must stay at or above terrainHeight + this value.
    ///     spec: Docs/RE/specs/camera_movement.md §A.6 — "Terrain-collision camera lift 3.8". CODE-CONFIRMED.
    /// </summary>
    private const float TerrainLift = 3.8f;

    /// <summary>
    ///     Additive bias applied after terrain clamping.
    ///     spec: Docs/RE/specs/camera_movement.md §A.6 — "Collision Y-bias step +2.0". CODE-CONFIRMED.
    /// </summary>
    private const float TerrainYBias = 2.0f;

    /// <summary>
    ///     Yaw-rate forced value on a hard terrain hit (to stop fighting the ground).
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — "Terrain hard-hit yaw kill −0.01". CODE-CONFIRMED.
    /// </summary>
    private const float TerrainHitYawKill = -0.01f;

    /// <summary>
    ///     Focus look-at Z offset for Third-person mode (legacy space).
    ///     The focus is the player position plus this Z offset in legacy space.
    ///     Legacy Z → Godot Z conversion: Godot Z = −legacy Z, so the applied Godot shift = +40.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Focus / look-at Z −40.0". CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 — Third Focus Z −40. CODE-CONFIRMED.
    /// </summary>
    private const float FocusZThird = -40f;

    /// <summary>
    ///     Focus look-at Z offset for First-person mode (legacy space).
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 — First Focus Z −55. CODE-CONFIRMED.
    /// </summary>
    private const float FocusZFirst = -55f;

    /// <summary>
    ///     Focus look-at Z offset for Static mode (legacy space).
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.2 — Static Focus Z −55. CODE-CONFIRMED.
    /// </summary>
    private const float FocusZStatic = -55f;

    // =========================================================================
    // § EYE-OFFSET SEED — CODE-CONFIRMED per spec §A.5.1
    // =========================================================================

    /// <summary>
    ///     Eye-offset seed vector X component in legacy world space.
    ///     In Godot space, X is unchanged (world coordinates negate only Z).
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Eye-offset vector (−750.0, 0.0, +500.0)". CODE-CONFIRMED.
    /// </summary>
    private const float EyeOffsetSeedLegacyX = -750f;

    /// <summary>
    ///     Eye-offset seed vector Y component in legacy world space (= 0; no vertical bias in the seed).
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Eye-offset vector (−750.0, 0.0, +500.0)". CODE-CONFIRMED.
    /// </summary>
    private const float EyeOffsetSeedLegacyY = 0f;

    /// <summary>
    ///     Eye-offset seed vector Z component in Godot space (legacy +500 negated to Godot −500).
    ///     spec: Docs/RE/specs/camera_movement.md §A.5.1 — "Eye-offset vector (−750.0, 0.0, +500.0)". CODE-CONFIRMED.
    ///     spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
    /// </summary>
    private const float EyeOffsetSeedGodotZ = -500f; // legacy +500 → Godot -500

    // =========================================================================
    // § FREE-FLY constants (DEVELOPER-ONLY, non-original)
    // =========================================================================

    /// <summary>Move speed in Godot units per second (free-fly, normal). Developer mode only.</summary>
    private const float FlyNormalSpeed = 600f;

    /// <summary>Move speed with Shift held (free-fly, fast). Developer mode only.</summary>
    private const float FlyFastSpeed = 2000f;

    /// <summary>Mouse sensitivity for free-fly look (radians per pixel). Developer mode only.</summary>
    private const float FlyMouseSensitivity = 0.004f;

    /// <summary>
    ///     Current elevation angle in radians. Negative = looking down.
    ///     Clamped to [ElevationMinRad, ElevationMaxRad].
    /// </summary>
    private float _elevation = DefaultElevationRad;

    /// <summary>
    ///     Per-frame elevation rate integrator. Decays with friction.
    /// </summary>
    private float _elevationRate;

    private float _flyPitch;

    // =========================================================================
    // § STATE — free-fly (developer only)
    // =========================================================================

    private float _flyYaw;

    // =========================================================================
    // § STATE — shared orbit
    // =========================================================================

    /// <summary>World-space focus point the camera orbits around (player position, optionally + focus-Z offset).</summary>
    private Vector3 _focus = Vector3.Zero;

    // =========================================================================
    // § MODE STATE
    // =========================================================================

    /// <summary>Active view mode. Changed via <see cref="SetViewMode" />.</summary>
    private ViewMode _mode = ViewMode.Third;

    /// <summary>Previous mode before entering free-fly (so ESC / Tab returns to it).</summary>
    private ViewMode _modeBeforeFreeFly = ViewMode.Third;

    /// <summary>True when the OS mouse cursor is captured (free-fly mode).</summary>
    private bool _mouseCaptured;

    /// <summary>True while the right mouse button is held (starts look drag).</summary>
    private bool _rightMouseHeld;

    /// <summary>
    ///     Current yaw offset around the player, in radians.
    ///     Zero = player's default facing. Clamped per mode.
    /// </summary>
    private float _yaw;

    /// <summary>
    ///     Per-frame yaw rate integrator (degrees / second equivalent). Decays with friction.
    ///     spec: Docs/RE/specs/camera_movement.md §A.4 — rate integrator with gain/friction.
    /// </summary>
    private float _yawRate;

    // =========================================================================
    // § EXPORTED / CONFIGURABLE fields (spec says polarity is INFERRED)
    // =========================================================================

    /// <summary>
    ///     Sign applied to the wheel / elevation-key orbit step. +1 = wheel-up raises camera, −1 = lowers.
    ///     spec: Docs/RE/specs/camera_movement.md §D item 1 — "camera action polarity INFERRED/configurable".
    /// </summary>
    [Export]
    public float ElevationKeyPolarity { get; set; } = 1f;

    /// <summary>
    ///     Sign applied to the pitch drag from right-mouse vertical delta.
    ///     spec: Docs/RE/specs/camera_movement.md §D item 1 — "camera action polarity INFERRED/configurable".
    /// </summary>
    [Export]
    public float PitchDragPolarity { get; set; } = 1f;

    // =========================================================================
    // § GROUND HEIGHT DELEGATE
    // =========================================================================

    /// <summary>
    ///     Optional per-XZ terrain height query for camera ground collision.
    ///     Signature: <c>float GetHeight(float legacyWorldX, float legacyWorldZ)</c>.
    ///     When null, no terrain clamping is applied.
    ///     The caller supplies legacy world coordinates (X, Z) — NOT Godot coordinates.
    ///     Convert the eye position: legacyX = eyePos.X, legacyZ = −eyePos.Z.
    ///     spec: Docs/RE/specs/camera_movement.md §A.6 — terrain height clamp (Third only).
    /// </summary>
    public Func<float, float, float>? GroundHeightFunc { get; set; }

    // =========================================================================
    // § PLAYER FOLLOW TARGET
    // =========================================================================

    /// <summary>
    ///     Set by the renderer each frame (or whenever the player moves) to the avatar's
    ///     Godot-space world position. The camera uses this as its orbit focus.
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
            $"eyeSeed=({EyeOffsetSeedLegacyX},{EyeOffsetSeedLegacyY},{EyeOffsetSeedGodotZ}) Godot | " +
            $"radius={OrbitRadius:F1}u | elev_default={Mathf.RadToDeg(DefaultElevationRad):F1}deg | " +
            $"elev_clamp=[{Mathf.RadToDeg(ElevationMinRad):F0}deg,{Mathf.RadToDeg(ElevationMaxRad):F0}deg] | " +
            $"yaw_clamp_third=[{Mathf.RadToDeg(YawMin):F1}deg,{Mathf.RadToDeg(YawMaxThird):F1}deg ({YawMaxThird:F4}rad)] | " +
            "RMB=orbit wheel=elevation ESC=reset-to-Third F1=Third F2=First F3=Static Tab=devFreeFly");
    }

    public override void _ExitTree()
    {
        ReleaseMouse();
    }

    // =========================================================================
    // § PUBLIC API — configuration
    // =========================================================================

    /// <summary>
    ///     Positions the camera to frame a cell's centre on first load. Reproduces the
    ///     oblique aerial framing previously used by RealWorldRenderer.SpawnCamera.
    ///     Keeps the Third-person model while providing a reasonable initial view of the world.
    ///     spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 world units. CONFIRMED.
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
    ///     Switches to the requested view mode.
    ///     ESC resets to Third-person (call site 2 in spec §A.2.2).
    ///     spec: Docs/RE/specs/camera_movement.md §A.2.2 — "exactly three call sites". CODE-CONFIRMED.
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
                // Returning from free-fly: re-anchor focus to the player position.
                _focus = PlayerGodotPosition;

            _mode = newMode;
            ApplyCurrentModeTransform();
            GD.Print($"[Camera] Switched to {newMode}.");
        }
    }
}