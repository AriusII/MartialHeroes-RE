using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Manages the set of live <see cref="VisualActor"/> nodes in the scene tree:
/// spawns a new node on <see cref="ActorSpawnedEvent"/>, routes move events, and
/// removes the node on <see cref="ActorDespawnedEvent"/>.
///
/// PASSIVE: this class does not compute positions, does not know the protocol, and
/// holds zero domain state. It only maps ActorKey → VisualActor node.
///
/// All methods are called from GameLoop._Process — Godot main thread only.
/// </summary>
public sealed partial class ActorRegistry : Node
{
    // ActorKey → live VisualActor node.
    private readonly Dictionary<ActorKey, VisualActor> _actors = new();

    // Set during Initialise() — not constructor, since Godot nodes are wired after _Ready.
    private ClientContext _clientContext = null!;

    /// <summary>Called by GameLoop._Ready before any events can arrive.</summary>
    public void Initialise(ClientContext context)
    {
        _clientContext = context;
    }

    // -------------------------------------------------------------------------
    // Event handlers (called from GameLoop._Process, main thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns a <see cref="VisualActor"/> node for the given actor, converts the Q16.16
    /// spawn position to Godot world space at this presentation boundary, and adds it to
    /// the scene tree.
    ///
    /// spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
    /// </summary>
    public void OnActorSpawned(ActorSpawnedEvent evt)
    {
        if (_actors.ContainsKey(evt.Key))
        {
            // Duplicate spawn — remove and re-create (server can resend on reconnect).
            RemoveActor(evt.Key);
        }

        var visual = new VisualActor();

        // Convert Q16.16 fixed-point position to Godot float world space.
        // Step 1: Q16.16 → float (presentation boundary). spec: Vector3Fixed.ToVector3Float().
        // Step 2: legacy left-handed → Godot right-handed (negate Z). spec: WorldCoordinates.ToGodot.
        var (fx, fy, fz) = evt.Position.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        visual.GlobalPosition = new Vector3(gx, gy, gz);
        visual.ActorKey = evt.Key;
        visual.ActorName = evt.Name;

        AddChild(visual);
        _actors[evt.Key] = visual;

        GD.Print($"[ActorRegistry] Spawned actor {evt.Key.RawId} '{evt.Name}' at {visual.GlobalPosition}");
    }

    /// <summary>
    /// Applies the confirmed position from an <see cref="ActorMovedEvent"/> to the
    /// corresponding <see cref="VisualActor"/>. The node interpolates toward that position;
    /// we set the authoritative target — no movement math here.
    ///
    /// spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
    /// </summary>
    public void OnActorMoved(ActorMovedEvent evt)
    {
        if (!_actors.TryGetValue(evt.Key, out VisualActor? visual))
        {
            return; // Move arrived before spawn; GamePacketHandler already handles this on the domain side.
        }

        // Convert the confirmed move-target to Godot world space at this boundary.
        // Step 1: Q16.16 → float. spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
        // Step 2: legacy → Godot handedness. spec: WorldCoordinates.ToGodot.
        var (fx, fy, fz) = evt.MoveTarget.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        visual.SetMoveTarget(new Vector3(gx, gy, gz), evt.IsRunning);
    }

    /// <summary>
    /// Removes the <see cref="VisualActor"/> from the scene tree when the actor leaves the world.
    /// </summary>
    public void OnActorDespawned(ActorDespawnedEvent evt)
    {
        if (evt.PlayLeaveEffect)
        {
            GD.Print($"[ActorRegistry] Actor {evt.Key.RawId} left the area.");
        }

        RemoveActor(evt.Key);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void RemoveActor(ActorKey key)
    {
        if (_actors.Remove(key, out VisualActor? visual))
        {
            visual.QueueFree();
        }
    }
}