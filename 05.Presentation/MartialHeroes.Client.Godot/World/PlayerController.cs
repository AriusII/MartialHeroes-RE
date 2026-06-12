// World/PlayerController.cs
//
// Classic MMO player controller — strictly PASSIVE presentation node.
//
// Responsibilities (all visual only; zero game-rule authority):
//   - Left-click on the ground → move the avatar toward the hit point (local visual movement).
//   - WASD keyboard → move the avatar in the horizontal plane.
//   - Avatar smoothly rotates to face the current movement direction (heading lerp).
//   - Exposes TargetForCamera (Vector3) so an optional external camera can follow without
//     a hard coupling to CameraController.
//
// Architecture:
//   SetAvatar(Node3D)             — attach the character node that this controller drives.
//   SetGroundY(float)             — flat-ground Y fallback used when no colliders exist.
//   GroundHeightFunc               — optional per-XZ height query (terrain following).
//   TargetForCamera               — smoothed world position the camera may track (read-only).
//
// Ground hit detection (left-click):
//   1. Try a physics raycast via PhysicsRayQueryParameters3D (works when terrain has a
//      StaticBody3D/CollisionShape3D child).
//   2. If the ray misses or no physics bodies are present, fall back to infinite-plane
//      intersection at Y = GroundY — ensures click-to-move always works even with
//      flat synthetic terrain that has no collider.
//
// Coordinate convention:
//   spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 units.
//   spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z.
//   All positions here are already in Godot world-space (Z negated); no further conversion
//   is needed for purely visual local movement.
//
// Threading:
//   All node mutation happens on the Godot main thread (_Process / _UnhandledInput).
//   No background threads are used by this class.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "strictly passive rendering".

using Godot;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive MMO movement controller.  Owns the local visual movement of a single avatar
/// <see cref="Node3D"/>.  It drives that node's position and rotation every frame;
/// it does NOT communicate with any server, run game formulas, or modify domain state.
///
/// <para>
/// <b>Left-click</b>: raycasts from the active <see cref="Camera3D"/> into the scene.
/// If a physics body is hit the avatar walks toward that hit point.  If no collider exists
/// the controller falls back to an infinite horizontal plane at <see cref="GroundY"/>.
/// </para>
/// <para>
/// <b>WASD</b>: direct heading-relative keyboard movement.  Left-click destination is
/// cancelled as soon as a WASD key is pressed.
/// </para>
/// <para>
/// <b>Camera follow</b>: read <see cref="TargetForCamera"/> each frame and point the camera
/// at it — no reference to <c>CameraController</c> is stored here.
/// </para>
/// </summary>
public sealed partial class PlayerController : Node3D
{
    // -----------------------------------------------------------------------
    // Tunable movement constants
    // -----------------------------------------------------------------------

    /// <summary>
    /// Walk speed in Godot units per second.
    /// At 1 cell = 1024 units a walk speed of ~200 u/s crosses a cell in ~5 s,
    /// similar to a classic MMORPG walk pace.
    /// spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 units. CONFIRMED.
    /// </summary>
    private const float WalkSpeed = 200f;

    /// <summary>
    /// Angular speed used for smooth heading interpolation, in radians per second.
    /// 8 rad/s → a full 180° turn takes ~0.39 s — feels snappy but not instant.
    /// </summary>
    private const float TurnSpeed = 8f;

    /// <summary>
    /// Arrival threshold: the avatar stops when it is within this many units of the
    /// click-to-move destination (prevents micro-oscillation at the goal).
    /// </summary>
    private const float ArrivalThreshold = 12f;

    /// <summary>
    /// Maximum raycast length for the left-click ground probe (Godot units).
    /// Must comfortably exceed any camera-to-terrain distance across the whole map.
    /// At MaxZoom = 8000 units (CameraController) a ray length of 20 000 units is safe.
    /// spec: CameraController — MaxZoom = 8000 units.
    /// </summary>
    private const float RayLength = 20_000f;

    /// <summary>
    /// Ground collision mask — bit 0 (layer 1) is the Godot default for static geometry.
    /// Override if the terrain StaticBody3D is assigned to a different physics layer.
    /// spec: Godot 4 physics layers — layer 1 = mask value 1.
    /// </summary>
    private const uint GroundCollisionMask = 0xFFFF_FFFF; // match all layers as a broad fallback

    // -----------------------------------------------------------------------
    // Public surface — configuration setters
    // -----------------------------------------------------------------------

    /// <summary>
    /// The avatar <see cref="Node3D"/> driven by this controller.
    /// Must be set before the controller does anything useful.
    /// Call this after <see cref="SkinnedCharacterBuilder.Build"/> returns and the node
    /// has been added to the scene tree.
    /// </summary>
    public void SetAvatar(Node3D avatar)
    {
        _avatar = avatar;
        if (avatar is not null)
        {
            // Initialise TargetForCamera to the avatar's current position so a camera
            // that reads this property before the first frame gets a sensible value.
            TargetForCamera = avatar.GlobalPosition;
            GD.Print($"[PlayerController] Avatar attached: '{avatar.Name}' " +
                     $"at {avatar.GlobalPosition}.");
        }
    }

    /// <summary>
    /// Y coordinate of the flat-ground fallback plane (Godot world-space units).
    /// Used when the left-click ray misses all physics colliders.
    /// For the current cell (10000, 9990) the terrain surface is approximately Y = 26.
    /// spec: RealWorldRenderer.LoadAndSpawnCharacter — charRoot.Position.Y = 26f.
    /// </summary>
    public void SetGroundY(float y)
    {
        GroundY = y;
    }

    /// <summary>
    /// Y coordinate of the flat-ground fallback plane.
    /// Readable externally so callers can query the current value.
    /// </summary>
    public float GroundY { get; private set; } = 26f;

    /// <summary>
    /// Optional per-XZ terrain height query.  When set, the controller queries this
    /// function after every move step to snap the avatar's Y to the terrain surface.
    /// Signature: <c>float GetHeight(Vector3 worldPos)</c> — returns Y in Godot units.
    /// When null the avatar stays at <see cref="GroundY"/> (flat-ground mode).
    /// </summary>
    public Func<Vector3, float>? GroundHeightFunc { get; set; }

    // -----------------------------------------------------------------------
    // Public surface — camera follow target
    // -----------------------------------------------------------------------

    /// <summary>
    /// Smoothed world-space position that an external camera can track.
    /// Updated every <c>_Process</c> frame to equal the avatar's current position
    /// (once an avatar is attached).  Read this from a CameraController or any other
    /// node that wants to follow the player — no hard coupling required.
    /// </summary>
    public Vector3 TargetForCamera { get; private set; }

    // -----------------------------------------------------------------------
    // Internal state
    // -----------------------------------------------------------------------

    /// <summary>The avatar node this controller is currently driving (may be null).</summary>
    private Node3D? _avatar;

    /// <summary>True while the controller is navigating toward a left-click destination.</summary>
    private bool _navigating;

    /// <summary>The Godot world-space point the avatar is walking toward.</summary>
    private Vector3 _destination;

    /// <summary>
    /// Current avatar facing direction in the XZ plane (unit vector).
    /// Persists between frames so rotation can be smoothly interpolated.
    /// Initialised to +Z (facing "into the screen" in Godot, i.e. South in legacy space).
    /// </summary>
    private Vector3 _facing = -Vector3.Forward; // +Z in Godot right-handed space

    // -----------------------------------------------------------------------
    // Godot lifecycle
    // -----------------------------------------------------------------------

    public override void _Ready()
    {
        GD.Print(
            "[PlayerController] Ready. " +
            "LMB=click-to-move, WASD=keyboard movement. " +
            "Assign SetAvatar() and optionally SetGroundY() / GroundHeightFunc.");
    }

    // -----------------------------------------------------------------------
    // Input — left-click to move
    //
    // _UnhandledInput is used so that UI Control nodes (HUD, inventory, etc.)
    // can consume clicks before this handler sees them — matching the same
    // guard pattern used by CameraController.
    // -----------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed) return;
        if (_avatar is null) return;

        // Find the active Camera3D for the current viewport.
        Camera3D? cam = GetViewport()?.GetCamera3D();
        if (cam is null) return;

        Vector2 screenPos = mb.Position;
        Vector3? hit = TryRaycastGround(cam, screenPos);
        if (hit is null) return;

        // Set the destination; begin navigation.
        _destination = hit.Value;
        _navigating = true;

        GetViewport().SetInputAsHandled();
    }

    // -----------------------------------------------------------------------
    // _Process — movement tick
    // -----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_avatar is null) return;

        float dt = (float)delta;

        // Collect the desired movement vector this frame.
        Vector3 moveDir = Vector3.Zero;

        // --- WASD keyboard input ---
        bool anyWasd = false;
        if (global::Godot.Input.IsKeyPressed(Key.W))
        {
            moveDir += GetCameraForward();
            anyWasd = true;
        }

        if (global::Godot.Input.IsKeyPressed(Key.S))
        {
            moveDir -= GetCameraForward();
            anyWasd = true;
        }

        if (global::Godot.Input.IsKeyPressed(Key.A))
        {
            moveDir -= GetCameraRight();
            anyWasd = true;
        }

        if (global::Godot.Input.IsKeyPressed(Key.D))
        {
            moveDir += GetCameraRight();
            anyWasd = true;
        }

        // WASD cancels click-to-move so the two input modes don't fight.
        if (anyWasd)
            _navigating = false;

        // --- Click-to-move navigation ---
        if (_navigating && !anyWasd)
        {
            Vector3 toGoal = _destination - _avatar.GlobalPosition;
            toGoal.Y = 0f; // movement in the horizontal plane only

            float dist = toGoal.Length();
            if (dist <= ArrivalThreshold)
            {
                // Arrived — stop navigation.
                _navigating = false;
            }
            else
            {
                // Walk toward the destination at WalkSpeed.
                moveDir = toGoal / dist; // normalised horizontal direction
            }
        }

        // --- Apply movement ---
        if (moveDir.LengthSquared() > 1e-6f)
        {
            // Flatten to horizontal (remove any Y component from camera-derived vectors).
            moveDir.Y = 0f;
            if (moveDir.LengthSquared() > 1e-6f)
                moveDir = moveDir.Normalized();

            // Translate avatar.
            Vector3 newPos = _avatar.GlobalPosition + moveDir * (WalkSpeed * dt);

            // Snap Y to terrain surface (or flat ground fallback).
            newPos.Y = SampleGroundHeight(newPos);

            _avatar.GlobalPosition = newPos;

            // Smooth heading rotation: lerp _facing toward moveDir.
            _facing = SmoothedFacing(_facing, moveDir, TurnSpeed * dt);

            // Rotate the avatar so it faces the direction of travel.
            // We want the avatar's local -Z axis to align with _facing.
            // Godot's LookAt looks toward -Z, so pass (pos + _facing) as the target.
            // Guard: LookAt is undefined when the target equals position.
            if (_facing.LengthSquared() > 1e-6f)
            {
                Vector3 lookTarget = _avatar.GlobalPosition + _facing;
                // Additional guard: the standard up vector (Y) must not be collinear with
                // the forward vector. Martial Heroes is a flat-ish world so this is
                // virtually never an issue, but defensive programming costs nothing.
                if (Mathf.Abs(_facing.Dot(Vector3.Up)) < 0.999f)
                    _avatar.LookAt(lookTarget, Vector3.Up);
            }
        }

        // Publish camera follow target.
        TargetForCamera = _avatar.GlobalPosition;
    }

    // -----------------------------------------------------------------------
    // Ground hit detection
    // -----------------------------------------------------------------------

    /// <summary>
    /// Attempts to find the ground hit point for a left-click at <paramref name="screenPos"/>.
    ///
    /// Strategy:
    ///   1. Cast a physics ray (depth-tests all collidable bodies on all layers).
    ///      Returns the collision point when a StaticBody3D / terrain collider is hit.
    ///   2. Fall back to an infinite horizontal plane at Y = <see cref="GroundY"/> using
    ///      ray–plane intersection when no physics hit is recorded.  This guarantees
    ///      click-to-move always works even on fully synthetic terrain with no colliders.
    ///
    /// spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 units (for scale reference).
    /// </summary>
    private Vector3? TryRaycastGround(Camera3D cam, Vector2 screenPos)
    {
        // Build the world-space ray from the camera through the clicked screen pixel.
        Vector3 rayOrigin = cam.ProjectRayOrigin(screenPos);
        Vector3 rayDir = cam.ProjectRayNormal(screenPos);

        // --- Attempt 1: physics raycast ---
        // PhysicsDirectSpaceState3D is available when the node is in the scene tree.
        // We call it via the viewport's world rather than storing a reference, which is
        // the safest way to avoid dangling pointers.
        PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
        if (space is not null)
        {
            var query = PhysicsRayQueryParameters3D.Create(
                rayOrigin,
                rayOrigin + rayDir * RayLength,
                GroundCollisionMask);

            // Exclude the avatar itself so it never blocks its own movement ray.
            if (_avatar is not null)
            {
                // Collect RIDs from any PhysicsBody3D children of the avatar.
                foreach (Node child in _avatar.GetChildren())
                {
                    if (child is PhysicsBody3D body)
                        query.Exclude.Add(body.GetRid());
                }
            }

            var result = space.IntersectRay(query);
            if (result.Count > 0 && result.TryGetValue("position", out var posVariant))
            {
                Vector3 hitPos = posVariant.AsVector3();
                GD.Print($"[PlayerController] Physics ray hit: {hitPos}");
                return hitPos;
            }
        }

        // --- Attempt 2: ray–plane intersection at Y = GroundY ---
        // Plane equation: N · (P − P0) = 0, N = (0,1,0), P0 = (0, GroundY, 0).
        // Substituting the ray P = rayOrigin + t·rayDir:
        //   t = (GroundY − rayOrigin.Y) / rayDir.Y
        // Valid only when rayDir.Y != 0 (guard below) and t > 0 (ray goes forward).
        float denominator = rayDir.Y;
        const float EpsilonY = 1e-5f; // avoid division by near-zero
        if (Mathf.Abs(denominator) < EpsilonY)
            return null; // ray is nearly horizontal — no valid ground intersection

        float t = (GroundY - rayOrigin.Y) / denominator;
        if (t < 0f)
            return null; // intersection is behind the camera

        Vector3 planeHit = rayOrigin + rayDir * t;
        return planeHit;
    }

    // -----------------------------------------------------------------------
    // Terrain height sampling
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the Y coordinate the avatar should stand at for a given world position.
    /// Uses <see cref="GroundHeightFunc"/> when set, otherwise returns <see cref="GroundY"/>.
    /// </summary>
    private float SampleGroundHeight(Vector3 worldPos)
    {
        if (GroundHeightFunc is not null)
        {
            try
            {
                return GroundHeightFunc(worldPos);
            }
            catch (Exception ex)
            {
                // A bad height-func should not crash the frame; fall through to flat ground.
                GD.PrintErr($"[PlayerController] GroundHeightFunc threw: {ex.Message}");
            }
        }

        return GroundY;
    }

    // -----------------------------------------------------------------------
    // Camera direction helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the camera's horizontal forward vector (XZ projection, normalised).
    /// This lets WASD move relative to the camera's current look direction, which is
    /// standard for MMORPGs with orbiting cameras.
    /// Falls back to -Vector3.Forward (Godot +Z) if no active camera is found.
    /// </summary>
    private Vector3 GetCameraForward()
    {
        Camera3D? cam = GetViewport()?.GetCamera3D();
        if (cam is null)
            return -Vector3.Forward; // Godot +Z as fallback

        // Camera's local -Z is its look direction.  Project onto XZ and normalise.
        Vector3 fwd = -cam.GlobalTransform.Basis.Z;
        fwd.Y = 0f;
        return fwd.LengthSquared() > 1e-6f ? fwd.Normalized() : -Vector3.Forward;
    }

    /// <summary>
    /// Returns the camera's horizontal right vector (XZ projection, normalised).
    /// </summary>
    private Vector3 GetCameraRight()
    {
        Camera3D? cam = GetViewport()?.GetCamera3D();
        if (cam is null)
            return Vector3.Right;

        Vector3 right = cam.GlobalTransform.Basis.X;
        right.Y = 0f;
        return right.LengthSquared() > 1e-6f ? right.Normalized() : Vector3.Right;
    }

    // -----------------------------------------------------------------------
    // Heading smooth rotation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Smoothly interpolates <paramref name="current"/> toward <paramref name="target"/>
    /// by at most <paramref name="maxAngle"/> radians.
    ///
    /// <para>Uses a signed-angle approach in the XZ plane to guarantee the shortest-path
    /// rotation (no 360° spins when crossing the ±π boundary).</para>
    ///
    /// Both inputs are assumed to be horizontal unit vectors (Y ≈ 0, |v| ≈ 1).
    /// The function is engine-free in its arithmetic and can be unit-tested without Godot.
    /// </summary>
    /// <param name="current">Current facing direction (unit vector in XZ plane).</param>
    /// <param name="target">Desired facing direction (unit vector in XZ plane).</param>
    /// <param name="maxAngle">Maximum rotation to apply this frame (radians).</param>
    /// <returns>New facing direction, unit length.</returns>
    private static Vector3 SmoothedFacing(Vector3 current, Vector3 target, float maxAngle)
    {
        // Compute the signed angle from current to target around the Y axis.
        //   sign = cross(current, target) · Y  (positive = target is CCW from current)
        //   angle = atan2(|cross|, dot) — gives angle in [0, π]
        //   signed_angle = sign(cross·Y) × angle
        float dot = Mathf.Clamp(current.Dot(target), -1f, 1f);
        float cross = current.X * target.Z - current.Z * target.X; // Y component of cross product

        float signedAngle = Mathf.Atan2(Mathf.Abs(cross), dot) * Mathf.Sign(cross);

        // Clamp so we rotate at most maxAngle this frame.
        float step = Mathf.Clamp(signedAngle, -maxAngle, maxAngle);

        // If the remaining angle is tiny, snap to the target to avoid micro-jitter.
        if (Mathf.Abs(signedAngle) < 0.001f)
            return target;

        // Rotate current by step radians around the Y axis.
        float cos = Mathf.Cos(step);
        float sin = Mathf.Sin(step);
        return new Vector3(
            current.X * cos + current.Z * sin,
            0f,
            -current.X * sin + current.Z * cos
        ).Normalized();
    }
}