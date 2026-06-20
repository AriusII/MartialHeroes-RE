using Godot;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Shared.Kernel.Numerics;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     The visual representation of one actor (player / NPC / monster) in the scene.
///     PASSIVE — SNAPSHOT INTERPOLATION ONLY:
///     This node holds zero domain state. It holds only VIEW state: the previous snapshot, the
///     current snapshot, and the accumulated time since the last snapshot. On every Godot frame
///     (_Process), it interpolates the visual position between the two confirmed snapshots using
///     alpha = elapsed / tickDuration. It never decides WHERE an actor goes — only WHERE it looks
///     between two authoritative fixed-tick snapshots.
///     Interpolation model:
///     - <see cref="ApplySnapshot" /> is called each time a new <see cref="ActorSnapshot" /> arrives
///     (30 Hz from <see cref="GameEngineLoop" />). The previous "current" snapshot becomes "previous";
///     the new one becomes "current". The elapsed interpolation timer is reset to 0.
///     - In <see cref="_Process(double)" /> the alpha is clamped to [0, 1.2] (slight overshoot allowed
///     for lerp smoothing). The visual position is lerped between previous and current positions.
///     spec: Docs/RE/specs/game_loop.md §6 — "Godot … interpolates between simulation snapshots
///     produced by the fixed tick." / "updates the spatial transforms of the associated Node3D
///     on the next frame".
///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "zero game-rule authority".
/// </summary>
public sealed partial class VisualActor : CharacterBody3D
{
    // Visual glide speeds (display only, not authoritative).
    private const float WalkGlideSpeed = 5.0f;
    private const float RunGlideSpeed = 10.0f;

    /// <summary>
    ///     Visual display scale applied to the attached skinned avatar (legacy unit → Godot). Matches
    ///     RealWorldRenderer/NpcRenderer's CharacterScale so the in-world player is the same size as the
    ///     demo avatar and the spawned mobs/NPCs.
    ///     spec: World/RealWorldRenderer.cs CharacterScale = 5.0f / World/NpcRenderer.cs CharacterScale.
    /// </summary>
    private const float SkinnedAvatarScale = 5.0f;

    // Current snapshot's Godot-space position (lerp TO).
    private Vector3 _currPosition;

    // Whether we have at least one snapshot to interpolate from.
    private bool _hasSnapshot;
    private bool _hasTarget;
    private bool _isRunning;

    // -------------------------------------------------------------------------
    // Legacy glide-to-target (kept for backward compatibility with ActorMovedEvent path)
    // -------------------------------------------------------------------------
    private Vector3 _moveTarget;

    // -------------------------------------------------------------------------
    // Skinned-avatar attachment (replaces the placeholder capsule)
    // -------------------------------------------------------------------------

    // The placeholder cyan capsule MeshInstance3D built in _Ready. Held so it can be removed once a
    // real skinned avatar is attached (the in-world local player). Null after AttachSkinnedAvatar.
    private MeshInstance3D? _placeholderMesh;

    // Previous snapshot's Godot-space position (lerp FROM).
    private Vector3 _prevPosition;

    // The attached skinned-avatar root (SkinnedCharacterBuilder.Build output), or null when this actor
    // still shows the placeholder capsule. Display visual only — no game state.
    private Node3D? _skinnedAvatar;

    // -------------------------------------------------------------------------
    // Snapshot interpolation state (VIEW state only)
    // -------------------------------------------------------------------------

    // Tick duration in seconds. Default for 30 Hz; updated when a snapshot carries FixedDeltaMs.
    // spec: Docs/RE/specs/game_loop.md §6 — "30 Hz via PeriodicTimer".
    private double _tickDurationSec = 1.0 / GameEngineLoop.DefaultTickRateHz;

    // Time elapsed since the last snapshot was received (seconds, main-thread only).
    private double _timeSinceSnapshot;
    // -------------------------------------------------------------------------
    // View identity (no domain state)
    // -------------------------------------------------------------------------

    /// <summary>The actor's composite identity — for debug labels only.</summary>
    public ActorKey ActorKey { get; set; }

    /// <summary>Display name — shown in debug label only.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>
    ///     Swaps the placeholder cyan capsule for a real skinned, idle-animated avatar (the in-world local
    ///     player). The <paramref name="skinnedRoot" /> is a <see cref="SkinnedCharacterBuilder.Build" />
    ///     output — it already carries the single handedness conversion, the §7/§9 up-axis remap pivot, and
    ///     an auto-engaged looping standing idle (the contained <c>SkinnedCharacterNode</c> self-ticks via
    ///     <c>_Process</c>). This node only re-parents and scales it; it adds NO game-rule authority.
    ///     Idempotent: a second call frees the prior avatar and attaches the new one. Safe to call before or
    ///     after <see cref="_Ready" /> (the placeholder is removed if present; otherwise just the avatar is
    ///     added). Main-thread only.
    ///     spec: Docs/RE/specs/skinning.md §8(e) (the avatar resolves via the recovered skin/bind/idle chain),
    ///     §10.5 (the col15 standing idle plays, looping); §8(b)/§7/§9 (handedness + up-axis remap done
    ///     inside Build, identical to NPCs).
    ///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive rendering.
    /// </summary>
    /// <param name="skinnedRoot">The skinned-avatar root from SkinnedCharacterBuilder.Build.</param>
    public void AttachSkinnedAvatar(Node3D skinnedRoot)
    {
        // Replace any prior avatar (idempotent re-attach on a re-spawn).
        if (_skinnedAvatar is not null && IsInstanceValid(_skinnedAvatar))
        {
            RemoveChild(_skinnedAvatar);
            _skinnedAvatar.QueueFree();
            _skinnedAvatar = null;
        }

        // Remove the placeholder capsule mesh (the collision shape + label stay).
        if (_placeholderMesh is not null && IsInstanceValid(_placeholderMesh))
        {
            RemoveChild(_placeholderMesh);
            _placeholderMesh.QueueFree();
            _placeholderMesh = null;
        }

        skinnedRoot.Name = "SkinnedAvatar";
        skinnedRoot.Scale = Vector3.One * SkinnedAvatarScale;
        AddChild(skinnedRoot);
        _skinnedAvatar = skinnedRoot;
    }

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // No placeholder capsule: actors render nothing until a real skinned avatar is attached
        // via AttachSkinnedAvatar. The real client never renders a fallback geometry for actors
        // without a resolved skin — it simply skips them.
        // Collision shape required for CharacterBody3D.
        var col = new CollisionShape3D();
        var shape = new CapsuleShape3D();
        shape.Radius = 0.4f;
        shape.Height = 1.8f;
        col.Shape = shape;
        AddChild(col);

        // Debug name label.
        var label = new Label3D();
        label.Text = ActorName;
        label.Position = new Vector3(0, 1.2f, 0);
        label.FontSize = 18;
        AddChild(label);
    }

    /// <summary>
    ///     Interpolates the visual position between the previous and current snapshots each frame.
    ///     Alpha = elapsed time since last snapshot / tick duration (clamped to [0, 1.2]).
    ///     The overshoot cap (1.2) allows a brief extrapolation for visual smoothness when the
    ///     next snapshot is slightly late. Godot computes ZERO game logic here — only a lerp.
    ///     spec: Docs/RE/specs/game_loop.md §6 — "Godot interpolates between simulation snapshots
    ///     produced by the fixed tick" / "updates the spatial transforms of the associated
    ///     Node3D on the next frame".
    /// </summary>
    public override void _Process(double delta)
    {
        if (_hasSnapshot)
        {
            _timeSinceSnapshot += delta;
            var alpha = _tickDurationSec > 0.0
                ? _timeSinceSnapshot / _tickDurationSec
                : 1.0;

            // Clamp with slight overshoot headroom for smooth arrival.
            var t = (float)Math.Clamp(alpha, 0.0, 1.2);
            GlobalPosition = _prevPosition.Lerp(_currPosition, t);
        }
        else if (_hasTarget)
        {
            // Fallback to legacy glide path (ActorMovedEvent, no snapshot yet).
            LegacyGlide(delta);
        }
    }

    // -------------------------------------------------------------------------
    // Snapshot API (called by ActorRegistry, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Applies a new <see cref="ActorSnapshot" /> from the fixed-tick loop. Converts the
    ///     Q16.16 position and move-target to Godot world space at this presentation boundary,
    ///     then resets the interpolation timer.
    ///     spec: Vector3Fixed.ToVector3Float() — presentation boundary conversion.
    ///     spec: WorldCoordinates.ToGodot — legacy left-handed Y-up → Godot right-handed.
    ///     spec: Docs/RE/specs/game_loop.md §6 — interpolation from Position → MoveTarget.
    /// </summary>
    /// <param name="snapshot">The immutable actor snapshot for this tick.</param>
    /// <param name="tickDurationSec">
    ///     The fixed tick duration in seconds (from <see cref="WorldSnapshotEvent.FixedDeltaMs" />).
    ///     Used to compute the interpolation alpha.
    /// </param>
    public void ApplySnapshot(in ActorSnapshot snapshot, double tickDurationSec)
    {
        // Previous "current" becomes the new "previous" so we lerp from here.
        _prevPosition = _hasSnapshot ? _currPosition : ConvertPosition(snapshot.Position);

        // Convert the authoritative move-target to Godot space.
        // We lerp Position → MoveTarget per the snapshot design (spec: WorldSnapshotEvent).
        _currPosition = ConvertPosition(snapshot.MoveTarget);

        _tickDurationSec = tickDurationSec > 0.0 ? tickDurationSec : 1.0 / GameEngineLoop.DefaultTickRateHz;
        _timeSinceSnapshot = 0.0;
        _hasSnapshot = true;
        _hasTarget = false; // snapshot path supersedes legacy glide.
    }

    // -------------------------------------------------------------------------
    // Legacy glide API (ActorMovedEvent path — used before snapshots arrive)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Sets the visual interpolation target for the legacy glide path. Called by
    ///     <see cref="ActorRegistry.OnActorMoved" /> when a snapshot has not yet been received.
    ///     Once <see cref="ApplySnapshot" /> is called, this path is superseded.
    ///     spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
    /// </summary>
    public void SetMoveTarget(Vector3 target, bool running)
    {
        _moveTarget = target;
        _isRunning = running;
        _hasTarget = true;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Converts a Q16.16 position to Godot world space at the presentation boundary.
    ///     spec: Vector3Fixed.ToVector3Float() + WorldCoordinates.ToGodot.
    /// </summary>
    private static Vector3 ConvertPosition(Vector3Fixed pos)
    {
        var (fx, fy, fz) = pos.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        return new Vector3(gx, gy, gz);
    }

    /// <summary>Legacy glide fallback — smooth movement toward the last confirmed move target.</summary>
    private void LegacyGlide(double delta)
    {
        var speed = _isRunning ? RunGlideSpeed : WalkGlideSpeed;
        var current = GlobalPosition;
        var direction = _moveTarget - current;
        var distance = direction.Length();

        if (distance < 0.05f)
        {
            GlobalPosition = _moveTarget;
            Velocity = Vector3.Zero;
            _hasTarget = false;
        }
        else
        {
            Velocity = direction.Normalized() * speed;
            MoveAndSlide();
        }
    }
}