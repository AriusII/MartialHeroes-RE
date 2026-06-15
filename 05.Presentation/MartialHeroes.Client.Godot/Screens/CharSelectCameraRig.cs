// Screens/CharSelectCameraRig.cs
//
// The character-select preview camera rig — REWRITTEN FROM SCRATCH against the recovered spec
// (CAMPAIGN 9 WAVE 3). Every constant is a spec-cited IDA value; there are NO hand-tuned numbers.
//
// WHAT THIS NODE DOES (all CODE-CONFIRMED in the spec):
//   1. ENTRY DOLLY: on scene enter, blends the Camera3D from keyframe 0 (KF0, dolly start) to
//      keyframe 1 (KF1, resting pose) over ~2.0 s:
//        - position LERP from KF0 to KF1
//        - orientation SLERP between the two LookAt(eye, row pivot) quaternions
//        - tween normalizer t = clamp(elapsedMs × 0.0005, 0, 1)  [0.0005 = 1/2000 → 2.0 s full]
//      The KF0↔KF1 leg uses the PLAIN lerp/slerp (no parabolic mid-arc bow — that only fires for
//      inner keyframes ≥ 2, which are dormant in this scene). After t = 1.0 the camera holds KF1.
//      spec: Docs/RE/specs/frontend_scenes.md §3.5 / §3.5.2 / §3.5.4 CODE-CONFIRMED.
//   2. MANUAL BOOM-ZOOM: after the dolly, a hold-to-zoom moves the camera along its forward view
//      axis; the boom depth is HARD-CLAMPED to [0, 22] (boom seed = 0 → eye sits on KF1 at rest).
//      spec: §3.5.4 — boom-Z clamp [0,22], boom seed 0. CODE-CONFIRMED.
//   3. ACTOR YAW: a hold-to-spin applies manual yaw to the SELECTED preview actor (NOT the camera),
//      about world up. The legacy create-preview turntable rate is ≈±2 rad/s. spec: §4.2.
//   4. SLOT RAY-PICK: a 3D world-space ray-pick (Camera3D unproject) against per-slot AABBs
//      (X ± 6, Z ± 6, Y band 70..92), nearest hit → slot index (0..4) or −1. spec: §3.3.3.
//
// COORDINATE CONVENTION: world geometry negates Z; KF positions and slot XZ arrive already in
//   Godot-space (Z negated once by the scene).
//
// NAMESPACE PITFALL: bare `Input` / `Time` collide with the sibling project namespace (CS0234) →
//   use global::Godot.Input / global::Godot.Key.
//
// PASSIVE: zero game logic. Reads input, animates a camera, rotates an actor, intersects boxes.

using Godot;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The entry-dolly camera rig for the character-select preview: drives the KF0→KF1 dolly over
/// ~2.0 s on scene enter, then the two manual hold-to-move inputs (camera boom-zoom + selected-
/// actor yaw) and the per-slot ray-pick.
///
/// <para>Owned by <see cref="CharSelectScene3D"/>, which constructs the camera at KF0 then calls
/// <see cref="Configure"/> with the camera, the dolly keyframes, the row-pivot look-at, the
/// per-slot Godot-space XZ arrays, and the selected-slot / slot-actor accessors. The dolly runs in
/// <see cref="_Process"/> against the engine ms clock.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.5 (entry dolly KF0→KF1) / §3.3.3 (hit-test).
/// </summary>
public sealed partial class CharSelectCameraRig : Node
{
    // =========================================================================
    // Dolly blend law — spec: §3.5.4 CODE-CONFIRMED.
    //   t = clamp(elapsedMs × 0.0005, 0, 1) → full blend in 1/0.0005 = 2000 ms = 2.0 s.
    // The KF0↔KF1 transition uses the plain linear blend (the inner-keyframe quadratic ease is
    // dead code here — only indices 0 and 1 are armed).
    // =========================================================================

    private const float DollyRatePerMs = 0.0005f; // spec: §3.5.4 literal 0.0005 CODE-CONFIRMED

    // Manual boom-zoom (a forward/back dolly on the view axis); boom depth clamped [0, 22].
    // spec: §3.5.4 — boom-Z clamp [0,22], boom seed 0. CODE-CONFIRMED.
    private const float BoomZoomUnitsPerSecond = 10.0f; // the §3.5.3 manual-zoom input-rate scalar (10.0)
    private const float BoomMinZ = 0.0f;  // spec: §3.5.4 CODE-CONFIRMED
    private const float BoomMaxZ = 22.0f; // spec: §3.5.4 CODE-CONFIRMED

    // Manual actor yaw — the legacy create-preview turntable rate ≈±2 rad/s. spec: §4.2.
    private const float ActorYawRadiansPerSecond = 2.0f;

    // Per-slot pick-box extents. spec: §3.3.3 — X ± 6, Z ± 6, Y band 70..92 (= +22). CODE-CONFIRMED.
    private const float HitBoxHalfExtentXZ = 6.0f;
    private const float HitBoxYHeight = 22.0f;

    // =========================================================================
    // Dolly state (set by Configure, animated by _Process).
    // =========================================================================

    private Vector3 _kf0Pos;          // Godot-space KF0 (= world (515.549,137.266,−9397.710), Z negated)
    private Vector3 _kf1Pos;          // Godot-space KF1 (= world (512,87,−9652), Z negated)
    private Vector3 _lookAtTarget;    // the row pivot (constant through the dolly). spec: §3.5.4 / §3.6.5
    private Quaternion _kf0Orientation;
    private Quaternion _kf1Orientation;

    private float _dollyElapsedMs;
    private bool _dollyComplete;
    private float _boomZ; // boom depth accumulator, seed 0. spec: §3.5.4 CODE-CONFIRMED.

    // =========================================================================
    // Wiring (set by Configure).
    // =========================================================================

    private Camera3D? _camera;
    private float[] _slotGodotX = [];
    private float[] _slotGodotZ = [];
    private System.Func<int>? _selectedSlotProvider;
    private System.Func<int, Node3D?>? _slotActorProvider;

    /// <summary>
    /// Wires the rig to the scene camera, the two dolly keyframes, the row-pivot look-at, the
    /// per-slot Godot-space XZ arrays, and the selected-slot / slot-actor accessors. The dolly timer
    /// starts from this call.
    /// </summary>
    public void Configure(
        Camera3D camera,
        float[] slotGodotX,
        float[] slotGodotZ,
        System.Func<int> selectedSlotProvider,
        System.Func<int, Node3D?> slotActorProvider,
        Vector3 kf0Pos,
        Vector3 kf1Pos,
        Vector3 lookAtTarget)
    {
        _camera = camera;
        _slotGodotX = slotGodotX;
        _slotGodotZ = slotGodotZ;
        _selectedSlotProvider = selectedSlotProvider;
        _slotActorProvider = slotActorProvider;

        _kf0Pos = kf0Pos;
        _kf1Pos = kf1Pos;
        _lookAtTarget = lookAtTarget;

        // Both keyframes frame the same row-pivot look-at; the exact free-look Euler is debugger-
        // pending, so LookAt toward the row pivot is the documented framing. spec: §3.5 / §3.5.4.
        _kf0Orientation = LookAtQuaternion(kf0Pos, lookAtTarget);
        _kf1Orientation = LookAtQuaternion(kf1Pos, lookAtTarget);

        _dollyElapsedMs = 0.0f;
        _dollyComplete = false;
        _boomZ = 0.0f; // spec: §3.5.4 boom seed = 0 → eye on KF1 at rest. CODE-CONFIRMED.

        GD.Print($"[CharSelectCameraRig] Entry dolly armed: KF0={kf0Pos} → KF1={kf1Pos} " +
                 $"look-at(row pivot)={lookAtTarget}; t = clamp(elapsedMs × 0.0005, 0, 1) → 2.0 s. spec: §3.5.2/§3.5.4.");
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_camera is null) return;
        var dt = (float)delta;

        if (!_dollyComplete) TickDolly(dt);
        ApplyCameraBoomZoom(dt);
        ApplySelectedActorYaw(dt);
    }

    // =========================================================================
    // Entry dolly — KF0→KF1, plain lerp/slerp, 2.0 s.
    // =========================================================================

    private void TickDolly(float dt)
    {
        _dollyElapsedMs += dt * 1000.0f; // delta is seconds; the spec normalizer is over the ms clock
        float t = Mathf.Clamp(_dollyElapsedMs * DollyRatePerMs, 0.0f, 1.0f); // spec: §3.5.4 literal 0.0005

        _camera!.Position = _kf0Pos.Lerp(_kf1Pos, t);                  // position LERP. spec: §3.5.4
        _camera.Quaternion = _kf0Orientation.Slerp(_kf1Orientation, t); // orientation SLERP. spec: §3.5.4

        if (t >= 1.0f)
        {
            _dollyComplete = true;
            _camera.Position = _kf1Pos;             // snap to exact KF1
            _camera.Quaternion = _kf1Orientation;
            GD.Print("[CharSelectCameraRig] Entry dolly complete — holding KF1. spec: §3.5.2 (only indices 0/1 armed).");
        }
    }

    // =========================================================================
    // Manual boom-zoom — active only after the dolly holds KF1.
    // Hold keys: PageUp = zoom in (forward), PageDown = zoom out (backward). Boom-Z clamp [0,22].
    // =========================================================================

    private void ApplyCameraBoomZoom(float dt)
    {
        if (!_dollyComplete || _camera is null) return;

        float dir = 0.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.Pageup)) dir += 1.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.Pagedown)) dir -= 1.0f;
        if (dir == 0.0f) return;

        // Grow/shrink the boom depth, clamped [0, 22], then re-derive the eye along camera forward.
        // spec: §3.5.4 — eye = orbit point + boom along the view axis; boom-Z clamp [0,22]. CODE-CONFIRMED.
        _boomZ = Mathf.Clamp(_boomZ + dir * BoomZoomUnitsPerSecond * dt, BoomMinZ, BoomMaxZ);
        Vector3 forward = -_camera.GlobalTransform.Basis.Z.Normalized();
        _camera.GlobalPosition = _kf1Pos + forward * _boomZ;
    }

    // =========================================================================
    // Manual actor yaw — the SELECTED preview actor only (NOT the camera).
    // Hold keys: Q = −2 rad/s, E = +2 rad/s.
    // =========================================================================

    private void ApplySelectedActorYaw(float dt)
    {
        float dir = 0.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.Q)) dir -= 1.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(global::Godot.Key.E)) dir += 1.0f;
        if (dir == 0.0f) return;

        int selected = _selectedSlotProvider?.Invoke() ?? -1;
        if (selected < 0) return;

        Node3D? actor = _slotActorProvider?.Invoke(selected);
        actor?.RotateY(dir * ActorYawRadiansPerSecond * dt);
    }

    // =========================================================================
    // 3D world-space slot ray-pick. spec: §3.3.3 CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    /// Picks the slot (0..4) whose per-slot AABB the camera ray through
    /// <paramref name="viewportLocalPos"/> first hits, or −1 if none.
    /// </summary>
    public int HitTest(Vector2 viewportLocalPos)
    {
        if (_camera is null) return -1;

        Vector3 rayOrigin = _camera.ProjectRayOrigin(viewportLocalPos);
        Vector3 rayDir = _camera.ProjectRayNormal(viewportLocalPos);

        int bestSlot = -1;
        float bestT = float.PositiveInfinity;

        int count = System.Math.Min(System.Math.Min(_slotGodotX.Length, _slotGodotZ.Length), 5);
        for (int i = 0; i < count; i++)
        {
            Node3D? actor = _slotActorProvider?.Invoke(i);
            if (actor is null) continue;

            float rowBaseY = actor.Position.Y;
            var boxMin = new Vector3(_slotGodotX[i] - HitBoxHalfExtentXZ, rowBaseY, _slotGodotZ[i] - HitBoxHalfExtentXZ);
            var boxMax = new Vector3(_slotGodotX[i] + HitBoxHalfExtentXZ, rowBaseY + HitBoxYHeight, _slotGodotZ[i] + HitBoxHalfExtentXZ);

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

    private static Quaternion LookAtQuaternion(Vector3 eye, Vector3 target)
    {
        Vector3 forward = (target - eye).Normalized();
        if (forward.LengthSquared() < 1e-6f) return Quaternion.Identity;
        return Basis.LookingAt(forward, Vector3.Up).GetRotationQuaternion();
    }

    private static bool TryRayAabb(Vector3 origin, Vector3 dir, Vector3 boxMin, Vector3 boxMax, out float tHit)
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
