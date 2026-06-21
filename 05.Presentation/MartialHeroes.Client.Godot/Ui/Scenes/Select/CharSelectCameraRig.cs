// Screens/CharSelectCameraRig.cs
//
// The character-select preview camera rig — REWRITTEN FROM SCRATCH against the recovered spec
// (CAMPAIGN 9 WAVE 3). Every constant is a spec-cited IDA value; there are NO hand-tuned numbers.
//
// WHAT THIS NODE DOES (all CODE-CONFIRMED in the spec):
//   1. ENTRY DOLLY: on scene enter, blends the Camera3D from keyframe 0 (KF0, dolly start) to
//      keyframe 1 (KF1, resting pose) over ~2.0 s:
//        - position LERP from KF0 to KF1
//        - orientation SLERP between the two per-keyframe FREE-LOOK Euler quaternions (yaw∘pitch);
//          there is NO look-at point — the §3.5 HEADLINE CORRECTION supersedes the old "LookAt the
//          row pivot" framing: each keyframe carries an explicit Euler (yaw, pitch) and the view
//          direction comes from those angles, not from aiming at a world point.
//        - tween normalizer t = clamp(elapsedMs × 0.0005, 0, 1)  [0.0005 = 1/2000 → 2.0 s full]
//      The KF0↔KF1 leg uses the PLAIN lerp/slerp (no parabolic mid-arc bow — that only fires for
//      inner keyframes ≥ 2, which are dormant in this scene). After t = 1.0 the camera holds KF1.
//      spec: Docs/RE/specs/frontend_scenes.md §3.5 (free-look Euler headline) / §3.5.2 / §3.5.3
//            (the 12 PI-scaled angle multipliers) / §3.5.4 CODE-CONFIRMED.
//   2. MANUAL BOOM-ZOOM: after the dolly, a hold-to-zoom moves the camera along its forward view
//      axis; the boom depth is HARD-CLAMPED to [0, 22] (boom seed = 0 → eye sits on KF1 at rest).
//      spec: §3.5.4 — boom-Z clamp [0,22], boom seed 0. CODE-CONFIRMED.
//   3. SLOT RAY-PICK: a 3D world-space ray-pick (Camera3D unproject) against per-slot AABBs
//      (X ± 6, Z ± 6, Y band 70..92), nearest hit → slot index (0..4) or −1. spec: §3.3.3.
//
// NO CAMERA RE-AIM ON SELECTION: slot selection never moves this camera rig. The selected/deselected
//   preview actor yaw and manual left/right actor turntable live in CharSelectScene3D.
//   spec: Docs/RE/specs/frontend_scenes.md §3.3.2 / §3.3.4; recovered manual-yaw,
//   doida.exe SelectWindow_FaceActiveSlotFront/TickSelectedPreviewYaw.
//
// COORDINATE CONVENTION: world geometry negates Z; KF positions and slot XZ arrive already in
//   Godot-space (Z negated once by the scene).
//
// NAMESPACE PITFALL: bare `Input` / `Time` collide with the sibling project namespace (CS0234) →
//   use global::Godot.Input / global::Godot.Key.
//
// PASSIVE: zero game logic. Reads camera input, animates a camera, intersects boxes.

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

/// <summary>
///     The entry-dolly camera rig for the character-select preview: drives the KF0→KF1 dolly over
///     ~2.0 s on scene enter, then the manual camera boom-zoom and the per-slot ray-pick. Slot selection
///     never re-aims this camera; actor-local selection yaw is owned by <see cref="CharSelectScene3D" />.
///     <para>
///         Owned by <see cref="CharSelectScene3D" />, which constructs the camera at KF0 then calls
///         <see cref="Configure" /> with the camera, the dolly keyframes, the row-pivot look-at, the
///         per-slot Godot-space XZ arrays, and the slot-actor accessor (used by the ray-pick). The dolly
///         runs in <see cref="_Process" /> against the engine ms clock.
///     </para>
///     spec: Docs/RE/specs/frontend_scenes.md §3.5 (entry dolly KF0→KF1) / §3.3.3 (hit-test).
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

    // =========================================================================
    // Per-keyframe FREE-LOOK orientation (Euler yaw/pitch) — spec: §3.5.3 CODE-CONFIRMED.
    //   The rig is a FREE-LOOK keyframed camera (§3.5 HEADLINE CORRECTION): NO look-at point, the
    //   view direction is the keyframe's explicit Euler. §3.5.3 gives 12 PI-scaled multipliers —
    //   indices 0..5 = PITCH per kf (about local X), indices 6..11 = YAW per kf (about world-up Y).
    //   Only KF0 (idx 0 pitch / idx 6 yaw) and KF1 (idx 1 pitch / idx 7 yaw) are armed (§3.5.2).
    //   The angle is multiplier × π (radians).
    // =========================================================================

    // KF0 orientation. spec: §3.5.3 — idx 0 PITCH mult −0.03333334 (= −6.000°); idx 6 YAW mult
    //   0.01333333 (= +2.400°). CODE-CONFIRMED.
    private const float Kf0PitchRad = -0.03333334f * Mathf.Pi; // ≈ −0.104720 rad (−6.000°)
    private const float Kf0YawRad = 0.01333333f * Mathf.Pi; // ≈ +0.041888 rad (+2.400°)

    // KF1 orientation (the RESTING pose the player holds). spec: §3.5.3 — idx 1 PITCH mult
    //   −0.01483333 (= −2.670°); idx 7 YAW mult 0.00436111 (= +0.785°). CODE-CONFIRMED.
    private const float Kf1PitchRad = -0.01483333f * Mathf.Pi; // ≈ −0.046600 rad (−2.670°)
    private const float Kf1YawRad = 0.00436111f * Mathf.Pi; // ≈ +0.013701 rad (+0.785°)

    // Base heading that turns the free-look Euler so the camera's −Z view axis points at the actor
    // row IN GODOT-SPACE. The §3.5.3 angles were authored in legacy LEFT-handed space, where the
    // KF1 camera at Z=−9652 looks in −Z toward the row at Z≈−9738 (§3.3.2 / §3.5.4). The single
    // world Z-negate (Helpers/WorldCoordinates) flips that inequality: in Godot-space the camera at
    // Z=+9652 must look toward GREATER Z (+9738) to face the row, i.e. +Z — but a Godot camera's
    // default view axis is its local −Z. A π base yaw turns the rig so the small per-keyframe yaw/
    // pitch then frame the row from in front. The sign is VERIFIED EMPIRICALLY from the screenshot
    // (row IN FRONT, not behind the camera). spec: §3.5.4 (KF1 looks in −Z toward the row; Z-negate
    // flips the realized heading) / Helpers/WorldCoordinates (world geometry negates Z).
    private const float BaseHeadingYawRad = Mathf.Pi;

    // Manual boom-zoom (a forward/back dolly on the view axis); boom depth clamped [0, 22].
    // spec: §3.5.4 — boom-Z clamp [0,22], boom seed 0. CODE-CONFIRMED.
    private const float BoomZoomUnitsPerSecond = 10.0f; // the §3.5.3 manual-zoom input-rate scalar (10.0)

    private const float BoomMinZ = 0.0f; // spec: §3.5.4 CODE-CONFIRMED

    // spec: §3.5.4 C3 RESOLVED (static IDA, 2026-06-21) — boom-Z clamp literal = 26.0. The earlier
    // "22" reading is superseded and dropped. The static clamp literal is 26.0 (boom/zoom accumulator
    // clamped to [0, 27] in wheel-boom and per-frame tick handlers; 26.0 is the boom-Z depth limit).
    // Low impact: the boom seed is 0, so the resting eye sits on KF1 regardless.
    private const float BoomMaxZ = 26.0f; // spec: §3.5.4 C3 RESOLVED — boom-Z clamp 26.0 (CODE-CONFIRMED)

    // NOTE: slot selection and manual left/right yaw rotate the preview ACTOR in CharSelectScene3D;
    // this rig deliberately keeps the camera fixed. spec: Docs/RE/specs/frontend_scenes.md §3.3.2 /
    // §3.3.4; recovered manual-yaw, doida.exe SelectWindow_FaceActiveSlotFront/TickSelectedPreviewYaw.

    // Per-slot pick-box extents. spec: §3.3.3 — X ± 6, Z ± 6, Y band 70..92 (= +22). CODE-CONFIRMED.
    private const float HitBoxHalfExtentXZ = 6.0f;
    private const float HitBoxYHeight = 22.0f;
    private float _boomZ; // boom depth accumulator, seed 0. spec: §3.5.4 CODE-CONFIRMED.

    // =========================================================================
    // Wiring (set by Configure).
    // =========================================================================

    private Camera3D? _camera;
    private bool _dollyComplete;

    private float _dollyElapsedMs;
    private Quaternion _kf0Orientation;

    // =========================================================================
    // Dolly state (set by Configure, animated by _Process).
    // =========================================================================

    private Vector3 _kf0Pos; // Godot-space KF0 (= world (515.549,137.266,−9397.710), Z negated)
    private Quaternion _kf1Orientation;
    private Vector3 _kf1Pos; // Godot-space KF1 (= world (512,87,−9652), Z negated)

    // Slot-actor accessor — used by the ray-pick (HitTest) to read each actor's base Y. The former
    // selected-slot accessor drove the removed actor-yaw spin and is no longer stored.
    private Func<int, Node3D?>? _slotActorProvider;
    private float[] _slotGodotX = [];

    private float[] _slotGodotZ = [];

    /// <summary>
    ///     Wires the rig to the scene camera, the two dolly keyframes, the row-pivot look-at, the
    ///     per-slot Godot-space XZ arrays, and the slot-actor accessor (used by the ray-pick). The dolly
    ///     timer starts from this call.
    /// </summary>
    public void Configure(
        Camera3D camera,
        float[] slotGodotX,
        float[] slotGodotZ,
        Func<int, Node3D?> slotActorProvider,
        Vector3 kf0Pos,
        Vector3 kf1Pos)
    {
        _camera = camera;
        _slotGodotX = slotGodotX;
        _slotGodotZ = slotGodotZ;
        _slotActorProvider = slotActorProvider;

        _kf0Pos = kf0Pos;
        _kf1Pos = kf1Pos;

        // FREE-LOOK Euler endpoints — NO look-at point (§3.5 HEADLINE CORRECTION). Each keyframe's
        // orientation is its explicit per-keyframe Euler (yaw, pitch) from §3.5.3, turned by the
        // π base heading so the camera's −Z view axis faces the actor row in Godot-space (the world
        // Z-negate flips the legacy −Z heading). spec: §3.5.3 (angle multipliers) / §3.5.4.
        _kf0Orientation = EulerOrientation(Kf0YawRad, Kf0PitchRad);
        _kf1Orientation = EulerOrientation(Kf1YawRad, Kf1PitchRad);

        _dollyElapsedMs = 0.0f;
        _dollyComplete = false;
        _boomZ = 0.0f; // spec: §3.5.4 boom seed = 0 → eye on KF1 at rest. CODE-CONFIRMED.

        GD.Print(
            $"[CharSelectCameraRig] Entry dolly armed (FREE-LOOK Euler, NO look-at): KF0={kf0Pos} → KF1={kf1Pos}; " +
            $"KF0 yaw {Mathf.RadToDeg(Kf0YawRad):F3}°/pitch {Mathf.RadToDeg(Kf0PitchRad):F3}°, " +
            $"KF1 yaw {Mathf.RadToDeg(Kf1YawRad):F3}°/pitch {Mathf.RadToDeg(Kf1PitchRad):F3}° (+π base heading); " +
            $"t = clamp(elapsedMs × 0.0005, 0, 1) → 2.0 s. spec: §3.5.3/§3.5.4.");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_camera is null) return;
        var dt = (float)delta;

        if (!_dollyComplete) TickDolly(dt);
        ApplyCameraBoomZoom(dt);
        // No camera re-aim on slot interaction; actor-local yaw is owned by CharSelectScene3D.
    }

    // =========================================================================
    // Entry dolly — KF0→KF1, plain lerp/slerp, 2.0 s.
    // =========================================================================

    private void TickDolly(float dt)
    {
        _dollyElapsedMs += dt * 1000.0f; // delta is seconds; the spec normalizer is over the ms clock
        var t = Mathf.Clamp(_dollyElapsedMs * DollyRatePerMs, 0.0f, 1.0f); // spec: §3.5.4 literal 0.0005

        _camera!.Position = _kf0Pos.Lerp(_kf1Pos, t); // position LERP. spec: §3.5.4
        _camera.Quaternion = _kf0Orientation.Slerp(_kf1Orientation, t); // orientation SLERP. spec: §3.5.4

        if (t >= 1.0f)
        {
            _dollyComplete = true;
            _camera.Position = _kf1Pos; // snap to exact KF1
            _camera.Quaternion = _kf1Orientation;
            GD.Print(
                "[CharSelectCameraRig] Entry dolly complete — holding KF1. spec: §3.5.2 (only indices 0/1 armed).");
        }
    }

    // =========================================================================
    // Manual boom-zoom — active only after the dolly holds KF1.
    // Hold keys: PageUp = zoom in (forward), PageDown = zoom out (backward). Boom-Z clamp [0,22].
    // =========================================================================

    private void ApplyCameraBoomZoom(float dt)
    {
        if (!_dollyComplete || _camera is null) return;

        var dir = 0.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(Key.Pageup)) dir += 1.0f;
        if (global::Godot.Input.IsPhysicalKeyPressed(Key.Pagedown)) dir -= 1.0f;
        if (dir == 0.0f) return;

        // Grow/shrink the boom depth, clamped [0, 22], then re-derive the eye along camera forward.
        // spec: §3.5.4 — eye = orbit point + boom along the view axis; boom-Z clamp [0,22]. CODE-CONFIRMED.
        _boomZ = Mathf.Clamp(_boomZ + dir * BoomZoomUnitsPerSecond * dt, BoomMinZ, BoomMaxZ);
        var forward = -_camera.GlobalTransform.Basis.Z.Normalized();
        _camera.GlobalPosition = _kf1Pos + forward * _boomZ;
    }

    // =========================================================================
    // 3D world-space slot ray-pick. spec: §3.3.3 CODE-CONFIRMED.
    // =========================================================================

    /// <summary>
    ///     Picks the slot (0..4) whose per-slot AABB the camera ray through
    ///     <paramref name="viewportLocalPos" /> first hits, or −1 if none.
    /// </summary>
    public int HitTest(Vector2 viewportLocalPos)
    {
        if (_camera is null) return -1;

        var rayOrigin = _camera.ProjectRayOrigin(viewportLocalPos);
        var rayDir = _camera.ProjectRayNormal(viewportLocalPos);

        var bestSlot = -1;
        var bestT = float.PositiveInfinity;

        var count = Math.Min(Math.Min(_slotGodotX.Length, _slotGodotZ.Length), 5);
        for (var i = 0; i < count; i++)
        {
            var actor = _slotActorProvider?.Invoke(i);
            if (actor is null) continue;

            var rowBaseY = actor.Position.Y;
            var boxMin = new Vector3(_slotGodotX[i] - HitBoxHalfExtentXZ, rowBaseY,
                _slotGodotZ[i] - HitBoxHalfExtentXZ);
            var boxMax = new Vector3(_slotGodotX[i] + HitBoxHalfExtentXZ, rowBaseY + HitBoxYHeight,
                _slotGodotZ[i] + HitBoxHalfExtentXZ);

            if (TryRayAabb(rayOrigin, rayDir, boxMin, boxMax, out var t) && t < bestT)
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
    ///     Builds a keyframe's FREE-LOOK orientation from its explicit Euler (yaw, pitch), per
    ///     spec §3.5.3: a YAW quaternion about world-up Y composed with a PITCH quaternion about the
    ///     camera's local X ("builds a pitch quaternion from the 0..5 value and a yaw quaternion from
    ///     the matching 6..11 value, multiplies them into that keyframe's orientation"). A π base
    ///     heading is folded into the yaw so the camera's local −Z view axis faces the actor row in
    ///     Godot-space (the world Z-negate flips the legacy −Z heading — verified on-screen).
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.5.3 (yaw∘pitch) / §3.5 (free-look, no look-at).
    /// </summary>
    private static Quaternion EulerOrientation(float yawRad, float pitchRad)
    {
        // Yaw about world-up Y (azimuth), then pitch about the local X (elevation). Composing
        // yaw ∘ pitch (yaw on the LEFT) rotates the local pitch axis by the yaw — matching the
        // spec's "yaw quaternion × pitch quaternion" multiply order. The π base heading turns the
        // default −Z view axis toward the row in Godot-space.
        var yaw = new Quaternion(Vector3.Up, yawRad + BaseHeadingYawRad);
        var pitch = new Quaternion(Vector3.Right, pitchRad);
        return (yaw * pitch).Normalized();
    }

    private static bool TryRayAabb(Vector3 origin, Vector3 dir, Vector3 boxMin, Vector3 boxMax, out float tHit)
    {
        tHit = 0.0f;
        var tEnter = float.NegativeInfinity;
        var tExit = float.PositiveInfinity;

        for (var axis = 0; axis < 3; axis++)
        {
            var o = origin[axis];
            var d = dir[axis];
            var lo = boxMin[axis];
            var hi = boxMax[axis];

            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi) return false;
                continue;
            }

            var inv = 1.0f / d;
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
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