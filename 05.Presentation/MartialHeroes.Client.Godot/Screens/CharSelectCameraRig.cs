// Screens/CharSelectCameraRig.cs
//
// The character-select preview camera rig.
//
// WHAT THIS NODE DOES (all CODE-CONFIRMED in the rewritten spec):
//   1. ENTRY DOLLY: on scene enter, blends the Camera3D from keyframe 0 (KF0, dolly start) to
//      keyframe 1 (KF1, rest pose) over ~2.0 s using:
//        - position LERP from KF0 to KF1
//        - orientation SLERP from LookAt(KF0,target) to LookAt(KF1,target)
//        - tween normalizer: t = clamp(elapsedMs * 0.0005, 0, 1)  [= 1/2000 ms → 2.0 s full]
//      After t = 1.0 the dolly is complete; the camera holds KF1.
//      spec: Docs/RE/specs/frontend_scenes.md §3.5 / §3.5.2 / §3.5.4 CODE-CONFIRMED.
//
//   2. MANUAL ZOOM: after the dolly, the hold-to-zoom boom dolly moves the CAMERA along its
//      forward view axis at ±10 u/s while a zoom key is held. Clamp [0, 22] along boom.
//      spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — boom-Z clamped [0,22]. CODE-CONFIRMED.
//
//   3. ACTOR YAW: applies manual preview-actor yaw to the currently-SELECTED preview actor's
//      Node3D (NOT the camera), ±2 radians/second while held.
//      spec: Docs/RE/specs/frontend_scenes.md §3.5.3(b) — ±2 rad/s, selected actor only.
//
//   4. SLOT RAY-PICK: 3D world-space ray-pick via Camera3D unproject against per-slot AABBs,
//      returns the NEAREST hit's slot index (0..4), or -1.
//      spec: Docs/RE/specs/frontend_scenes.md §3.3.3 CODE-CONFIRMED.
//
// COORDINATE CONVENTION: world negates Z (WorldCoordinates.ToGodot: (x,y,z) → (x,y,−z)).
// All KF positions are passed in already in Godot-space (Z negated from spec world values).
//
// NAMESPACE PITFALL: inside MartialHeroes.Client.Godot.* a bare `Input`/`Time` resolves to
// the sibling project namespace (CS0234) — all refs are global::Godot.Input / global::Godot.Key.
// spec: CLAUDE.md "Namespace collisions: use global::Godot.Input / global::Godot.Time".
//
// PASSIVE: zero game logic. Reads input, animates a camera, moves an actor, intersects boxes.

using Godot;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The entry-dolly camera rig for the character-select preview: drives the KF0→KF1 dolly over
/// ~2.0 s on scene enter, then the two manual hold-to-move inputs (camera boom-zoom + selected-
/// actor yaw) and the per-slot ray-pick.
///
/// <para>Owned by <see cref="CharSelectScene3D"/>: the scene builds the camera at KF0, then hands
/// this rig the camera, the dolly keyframe positions and look-at target, the per-slot Godot-space
/// XZ arrays, the fallback row base Y, and the selected-slot / slot-actor accessors.
/// The dolly runs in <see cref="_Process"/>, driven by the engine ms clock.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.5 (entry dolly KF0→KF1) and §3.3.3 (hit-test).
/// </summary>
public sealed partial class CharSelectCameraRig : Node
{
    // =========================================================================
    // Dolly blend law constants.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 CODE-CONFIRMED:
    //   t = clamp(elapsedMs * 0.0005, 0.0, 1.0)  → full blend in 1/0.0005 = 2000 ms = 2.0 s.
    //   The KF0↔KF1 transition uses the PLAIN LINEAR blend (no quadratic ease; ease applies
    //   only for inner keyframes ≥ 2 which are dormant in this scene).
    // =========================================================================

    // 0.0005 = 1/2000 ms; clamped to [0,1] → full dolly in 2.0 s.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 CODE-CONFIRMED.
    private const float DollyRatePerMs = 0.0005f; // spec: §3.5.4 literal 0.0005 CODE-CONFIRMED

    // =========================================================================
    // Manual-input rates — both are linear integrators of frame delta-time.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 / §3.5.3.
    // =========================================================================

    // Camera boom zoom: ±10 u/s along view axis, boom-Z clamped [0, 22].
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 CODE-CONFIRMED — boom-Z clamp [0,22].
    private const float BoomZoomUnitsPerSecond = 10.0f;
    private const float BoomMaxZ = 22.0f; // spec: §3.5.4 CODE-CONFIRMED
    private const float BoomMinZ = 0.0f;  // spec: §3.5.4 CODE-CONFIRMED

    // Preview-actor yaw: ±2 radians/second about world-up (Y), applied to the SELECTED actor.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.3(b) CODE-CONFIRMED.
    private const float ActorYawRadiansPerSecond = 2.0f;

    // =========================================================================
    // Per-slot pick-box extents.
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.3 CODE-CONFIRMED.
    // =========================================================================

    private const float HitBoxHalfExtentXZ = 6.0f; // spec: §3.3.3 — X ± 6, Z ± 6. CODE-CONFIRMED.
    private const float HitBoxYHeight = 22.0f;       // spec: §3.3.3 — Y band 70..92 = +22. CODE-CONFIRMED.

    // =========================================================================
    // Dolly keyframe state — set by Configure, animated by _Process.
    // =========================================================================

    // Godot-space KF0 and KF1 positions (world Z negated by the caller).
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 CODE-CONFIRMED (KF1 exact; KF0 ≈).
    private Vector3 _kf0Pos;
    private Vector3 _kf1Pos;
    private Vector3 _lookAtTarget; // the orbit point; constant throughout the dolly.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — look-at target = active orbit point. CODE-CONFIRMED.

    // Pre-computed start and end orientations (as Quaternion) for the slerp.
    private Quaternion _kf0Orientation;
    private Quaternion _kf1Orientation;

    // Dolly progress: t ∈ [0, 1]. Starts at 0, reaches 1 when dolly is complete.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — t = clamp(elapsedMs * 0.0005, 0, 1).
    private float _dollyT;
    private bool _dollyComplete; // true once t = 1.0

    // Elapsed ms since the rig was configured (for the dolly normalizer).
    private float _dollyElapsedMs;

    // Current boom accumulator (zoom dolly, player-driven).
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — boom-Z ∈ [0, 22]. CODE-CONFIRMED.
    private float _boomZ; // initial value = 0 (seed = 0). spec: §3.5.4 CODE-CONFIRMED.

    // =========================================================================
    // Wiring set by Configure.
    // =========================================================================

    private Camera3D? _camera;

    // Godot-space per-slot centres (already Z-negated by the scene). 5 entries.
    private float[] _slotWorldX = [];
    private float[] _slotWorldZ = [];

    // Fallback row base Y.
    private float _rowBaseYFallback = 70.0f;

    // Reads the currently-selected slot index (0..4).
    private System.Func<int>? _selectedSlotProvider;

    // Returns the slot-position container Node3D for slot i (0..4), or null if empty/failed.
    private System.Func<int, Node3D?>? _slotActorProvider;

    // =========================================================================
    // Wiring API (called once by CharSelectScene3D from BuildCamera).
    // =========================================================================

    /// <summary>
    /// Wires the rig to the scene's camera, keyframe positions, look-at target, the per-slot
    /// Godot-space XZ arrays, the fallback row base Y, and the selected-slot / slot-actor accessors.
    /// The dolly elapsed timer starts from this call.
    /// </summary>
    public void Configure(
        Camera3D camera,
        float[] slotWorldX,
        float[] slotWorldZ,
        float rowBaseYFallback,
        System.Func<int> selectedSlotProvider,
        System.Func<int, Node3D?> slotActorProvider,
        Vector3 kf0Pos,
        Vector3 kf1Pos,
        Vector3 lookAtTarget)
    {
        _camera = camera;
        _slotWorldX = slotWorldX;
        _slotWorldZ = slotWorldZ;
        _rowBaseYFallback = rowBaseYFallback;
        _selectedSlotProvider = selectedSlotProvider;
        _slotActorProvider = slotActorProvider;

        _kf0Pos = kf0Pos;
        _kf1Pos = kf1Pos;
        _lookAtTarget = lookAtTarget;

        // Pre-compute KF0 and KF1 orientations as Quaternions.
        // Both look toward the same orbit point (look-at = KF1 position = the orbit point).
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — orientation slerp. CODE-CONFIRMED.
        _kf0Orientation = LookAtQuaternion(kf0Pos, lookAtTarget);
        _kf1Orientation = LookAtQuaternion(kf1Pos, lookAtTarget);

        // Dolly starts at t=0, elapsed=0.
        _dollyT = 0.0f;
        _dollyElapsedMs = 0.0f;
        _dollyComplete = false;
        _boomZ = 0.0f; // spec: §3.5.4 boom seed = 0. CODE-CONFIRMED.

        GD.Print($"[CharSelectCameraRig] Entry dolly armed: KF0={kf0Pos} → KF1={kf1Pos} " +
                 $"look-at={lookAtTarget}. Blend law: t = clamp(elapsedMs × 0.0005, 0, 1) → 2.0 s. " +
                 "spec: frontend_scenes.md §3.5.2/§3.5.4 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Per-frame update: dolly + manual input.
    // =========================================================================

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_camera is null) return;

        var dt = (float)delta;

        if (!_dollyComplete)
            TickDolly(dt);

        ApplyCameraBoomZoom(dt);
        ApplySelectedActorYaw(dt);
    }

    /// <summary>
    /// Advances the KF0→KF1 entry dolly.
    /// t = clamp(elapsedMs * 0.0005, 0, 1) — plain LINEAR lerp/slerp (no quadratic ease for kf0↔1).
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.4 CODE-CONFIRMED.
    /// </summary>
    private void TickDolly(float dt)
    {
        _dollyElapsedMs += dt * 1000.0f; // delta is in seconds; spec uses ms clock.
        // spec: §3.5.4 — tween normalizer literal 0.0005 × elapsed_ms, clamped at 1.0. CODE-CONFIRMED.
        float t = Mathf.Clamp(_dollyElapsedMs * DollyRatePerMs, 0.0f, 1.0f);
        _dollyT = t;

        // Linear interpolate position (lerp).
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — orbit point linearly interpolated. CODE-CONFIRMED.
        _camera!.Position = _kf0Pos.Lerp(_kf1Pos, t);

        // Spherical interpolate orientation (slerp) between the two LookAt quaternions.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — orientation spherically interpolated (slerp). CODE-CONFIRMED.
        Quaternion blendedQuat = _kf0Orientation.Slerp(_kf1Orientation, t);
        _camera.Quaternion = blendedQuat;

        if (t >= 1.0f && !_dollyComplete)
        {
            _dollyComplete = true;
            // Snap to exact KF1 to eliminate float drift.
            _camera.Position = _kf1Pos;
            _camera.Quaternion = _kf1Orientation;
            GD.Print("[CharSelectCameraRig] Entry dolly complete — holding KF1. " +
                     "spec: frontend_scenes.md §3.5.2 — only indices 0 and 1 are ever armed. CODE-CONFIRMED.");
        }
    }

    // =========================================================================
    // Manual camera boom-zoom (active only after dolly completes).
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    /// Hold-to-zoom boom dolly: moves the CAMERA along its forward view axis at ±10 u/s while a
    /// zoom key is held. Boom-Z clamped [0, 22].
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.4 CODE-CONFIRMED — boom-Z [0,22].
    /// Hold keys: PageUp = zoom in (forward), PageDown = zoom out (backward).
    /// </summary>
    private void ApplyCameraBoomZoom(float dt)
    {
        if (!_dollyComplete) return; // boom only after the dolly holds KF1.

        float dir = 0.0f;
        // global::Godot.Input — bare `Input` collides with the sibling project namespace (CS0234).
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.Pageup)) dir += 1.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.Pagedown)) dir -= 1.0f;

        if (dir == 0.0f) return;

        // Update boom accumulator, clamped to [0, 22].
        // spec: §3.5.4 CODE-CONFIRMED — boom-Z clamped [0, 22]; boom seed = 0.
        _boomZ = Mathf.Clamp(_boomZ + dir * BoomZoomUnitsPerSecond * dt, BoomMinZ, BoomMaxZ);

        // Apply as a local-forward offset from KF1 position (eye = orbit + rotate(boom)).
        // The boom direction is the camera's −Z (forward) axis. Simplified: use camera forward.
        Vector3 forward = -_camera!.GlobalTransform.Basis.Z.Normalized();
        _camera.GlobalPosition = _kf1Pos + forward * _boomZ;
    }

    // =========================================================================
    // Manual actor yaw (both during and after dolly — input can be active at any time).
    // =========================================================================

    /// <summary>
    /// Hold-to-spin yaw on the SELECTED preview actor (NOT the camera): ±2 rad/s about world-up.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.5.3(b) CODE-CONFIRMED.
    /// Hold keys: Q = −2 rad/s, E = +2 rad/s.
    /// </summary>
    private void ApplySelectedActorYaw(float dt)
    {
        float dir = 0.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.Q)) dir -= 1.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.E)) dir += 1.0f;

        if (dir == 0.0f) return;

        int selected = _selectedSlotProvider?.Invoke() ?? -1;
        if (selected < 0) return;

        Node3D? actor = _slotActorProvider?.Invoke(selected);
        if (actor is null) return;

        float yawDelta = dir * ActorYawRadiansPerSecond * dt;
        actor.RotateY(yawDelta);
    }

    // =========================================================================
    // 3D world-space slot ray-pick.
    // =========================================================================

    /// <summary>
    /// Picks the slot (0..4) whose per-slot AABB the camera ray through
    /// <paramref name="viewportLocalPos"/> first hits, or -1 if none.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.3 CODE-CONFIRMED.
    /// </summary>
    public int HitTest(Vector2 viewportLocalPos)
    {
        if (_camera is null) return -1;

        Vector3 rayOrigin = _camera.ProjectRayOrigin(viewportLocalPos);
        Vector3 rayDir = _camera.ProjectRayNormal(viewportLocalPos);

        int bestSlot = -1;
        float bestT = float.PositiveInfinity;

        int count = System.Math.Min(_slotWorldX.Length, _slotWorldZ.Length);
        count = System.Math.Min(count, 5);

        for (int i = 0; i < count; i++)
        {
            Node3D? actor = _slotActorProvider?.Invoke(i);
            if (actor is null) continue;

            float rowBaseY = actor.Position.Y;

            var boxMin = new Vector3(
                _slotWorldX[i] - HitBoxHalfExtentXZ,
                rowBaseY,
                _slotWorldZ[i] - HitBoxHalfExtentXZ);
            var boxMax = new Vector3(
                _slotWorldX[i] + HitBoxHalfExtentXZ,
                rowBaseY + HitBoxYHeight,
                _slotWorldZ[i] + HitBoxHalfExtentXZ);

            if (TryRayAabb(rayOrigin, rayDir, boxMin, boxMax, out float t) && t < bestT)
            {
                bestT = t;
                bestSlot = i;
            }
        }

        return bestSlot;
    }

    // =========================================================================
    // Helpers.
    // =========================================================================

    /// <summary>
    /// Computes the orientation Quaternion a camera at <paramref name="eye"/> needs to look toward
    /// <paramref name="target"/>, using world-up as the up vector. Equivalent to Basis.LookingAt
    /// but returned as a Quaternion ready for slerp.
    /// </summary>
    private static Quaternion LookAtQuaternion(Vector3 eye, Vector3 target)
    {
        // Build a Basis from the LookAt direction and extract its quaternion.
        // Godot convention: forward = −Z. LookingAt(target−eye, up) produces the basis
        // whose −Z points from eye toward target.
        Vector3 forward = (target - eye).Normalized();
        if (forward.LengthSquared() < 1e-6f)
            return Quaternion.Identity;

        // Build a transform positioned at eye, looking at target.
        var basis = Basis.LookingAt(forward, Vector3.Up);
        return basis.GetRotationQuaternion();
    }

    /// <summary>
    /// Standard 3-axis slab ray/AABB intersection.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.3 step 3 CODE-CONFIRMED.
    /// </summary>
    private static bool TryRayAabb(
        Vector3 origin, Vector3 dir, Vector3 boxMin, Vector3 boxMax, out float tHit)
    {
        tHit = 0.0f;
        float tEnter = float.NegativeInfinity;
        float tExit = float.PositiveInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis];
            float d = dir[axis];
            float lo = boxMin[axis];
            float hi = boxMax[axis];

            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi) return false;
                continue;
            }

            float inv = 1.0f / d;
            float t1 = (lo - o) * inv;
            float t2 = (hi - o) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);

            if (t1 > tEnter) tEnter = t1;
            if (t2 < tExit) tExit = t2;
            if (tEnter > tExit) return false;
        }

        if (tExit < 0.0f) return false;

        tHit = tEnter >= 0.0f ? tEnter : 0.0f;
        return true;
    }
}
