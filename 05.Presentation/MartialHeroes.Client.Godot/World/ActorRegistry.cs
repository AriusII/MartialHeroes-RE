using Godot;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Manages the set of live <see cref="VisualActor"/> nodes in the scene tree:
/// spawns a new node on <see cref="ActorSpawnedEvent"/>, routes per-actor snapshots from
/// <see cref="WorldSnapshotEvent"/>, routes legacy <see cref="ActorMovedEvent"/>s (pre-snapshot
/// fallback), and removes the node on <see cref="ActorDespawnedEvent"/>.
///
/// PASSIVE: this class does not compute positions, does not know the protocol, and holds zero
/// domain state. It maps ActorKey → VisualActor node.
///
/// Threading contract: all public methods are called from GameLoop._Process — Godot main thread.
///
/// spec: Docs/RE/specs/game_loop.md §6 — "updates the spatial transforms of the associated
///       Node3D on the next frame" / snapshot interpolation model.
/// </summary>
public sealed partial class ActorRegistry : Node
{
    // ActorKey → live VisualActor node.
    private readonly Dictionary<ActorKey, VisualActor> _actors = new();

    // Set during Initialise() — not constructor, since Godot nodes are wired after _Ready.
    private ClientContext _clientContext = null!;

    public override void _Ready()
    {
        GD.Print("[ActorRegistry] _Ready start");
    }

    /// <summary>Called by GameLoop._Ready before any events can arrive.</summary>
    public void Initialise(ClientContext context)
    {
        _clientContext = context;
    }

    // -------------------------------------------------------------------------
    // WorldSnapshotEvent handler — snapshot interpolation (primary path)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies per-actor interpolation snapshots from the fixed-tick <see cref="GameEngineLoop"/>.
    /// For each <see cref="ActorSnapshot"/> in the event, if a matching <see cref="VisualActor"/>
    /// is registered, its interpolation state is updated.
    ///
    /// Unknown actors in the snapshot (not yet spawned) are silently ignored — the spawn event
    /// will arrive on the next packet and register the node.
    ///
    /// spec: Docs/RE/specs/game_loop.md §6 — "Godot interpolates between simulation snapshots".
    /// spec: WorldSnapshotEvent — tick + FixedDeltaMs + per-actor ActorSnapshot[].
    /// </summary>
    public void OnWorldSnapshot(WorldSnapshotEvent snapshot)
    {
        // Convert FixedDeltaMs to seconds for VisualActor.ApplySnapshot.
        // spec: Docs/RE/specs/game_loop.md §6 — fixed delta in ms post time-scale.
        double tickSec = snapshot.FixedDeltaMs > 0
            ? snapshot.FixedDeltaMs / 1000.0
            : 1.0 / GameEngineLoop.DefaultTickRateHz;

        foreach (ActorSnapshot actorSnap in snapshot.Actors)
        {
            if (_actors.TryGetValue(actorSnap.Key, out VisualActor? visual))
            {
                visual.ApplySnapshot(in actorSnap, tickSec);
            }
            // Actor not yet in registry → snapshot ignored; spawn event will register it.
        }
    }

    // -------------------------------------------------------------------------
    // Spawn / move / despawn event handlers (called from GameLoop._Process)
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
    /// corresponding <see cref="VisualActor"/> via the legacy glide path. Used when the
    /// <see cref="GameEngineLoop"/> / snapshots are not yet running (offline or pre-boot).
    ///
    /// spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
    /// </summary>
    public void OnActorMoved(ActorMovedEvent evt)
    {
        if (!_actors.TryGetValue(evt.Key, out VisualActor? visual))
        {
            return; // Move arrived before spawn.
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