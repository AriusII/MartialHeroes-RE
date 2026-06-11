using Godot;
using MartialHeroes.Client.Domain.Actors;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// The visual representation of one actor (player / NPC / monster) in the scene.
///
/// PASSIVE: this node holds only VIEW state (current animation, tween target, cached
/// node handles). It holds ZERO domain state. It does not compute movement — it only
/// glides toward the position that <see cref="ActorRegistry"/> set, which came from
/// an authoritative <see cref="MartialHeroes.Client.Application.Events.ActorMovedEvent"/>.
///
/// Animation is placeholder: we switch between "idle" and "walk"/"run" string names
/// that would be provided by the model scene once assets are available.
/// </summary>
public sealed partial class VisualActor : CharacterBody3D
{
    // -------------------------------------------------------------------------
    // View state (no domain state here)
    // -------------------------------------------------------------------------

    /// <summary>The actor's composite identity — used for logging/debug only; no game logic.</summary>
    public ActorKey ActorKey { get; set; }

    /// <summary>Display name — shown in debug label only.</summary>
    public string ActorName { get; set; } = string.Empty;

    // Movement interpolation target set by ActorRegistry.
    private Vector3 _moveTarget;
    private bool _isRunning;
    private bool _hasTarget;

    // Visual speed constants for smooth gliding (display only — not game-authoritative speed).
    // These are pure visual interpolation rates; the game server owns actual speed.
    private const float WalkGlideSpeed = 5.0f; // Godot units/second for walk glide
    private const float RunGlideSpeed = 10.0f; // Godot units/second for run glide

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Placeholder mesh: a capsule so the actor is visible without real assets.
        // Replace with a scene instantiated from Assets.Mapping-produced glTF once available.
        var mesh = new MeshInstance3D();
        var capsule = new CapsuleMesh();
        capsule.Radius = 0.4f;
        capsule.Height = 1.8f;
        mesh.Mesh = capsule;
        AddChild(mesh);

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
    /// Smoothly interpolates toward the last authoritative move target.
    /// This is a VISUAL approximation only — the domain owns the authoritative position,
    /// which arrives on the next <see cref="MartialHeroes.Client.Application.Events.ActorMovedEvent"/>.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (!_hasTarget)
        {
            return;
        }

        float speed = _isRunning ? RunGlideSpeed : WalkGlideSpeed;
        Vector3 current = GlobalPosition;
        Vector3 direction = _moveTarget - current;
        float distance = direction.Length();

        if (distance < 0.05f)
        {
            // Close enough — snap and stop.
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

    // -------------------------------------------------------------------------
    // View-facing API called by ActorRegistry (main thread only)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets the visual interpolation target. Called by <see cref="ActorRegistry.OnActorMoved"/>
    /// after converting the domain position to Godot world space.
    /// </summary>
    public void SetMoveTarget(Vector3 target, bool running)
    {
        _moveTarget = target;
        _isRunning = running;
        _hasTarget = true;
    }
}