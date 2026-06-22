using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Presentation.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Manages the set of live <see cref="VisualActor" /> nodes in the scene tree:
///     spawns a new node on <see cref="ActorSpawnedEvent" />, routes per-actor snapshots from
///     <see cref="WorldSnapshotEvent" />, routes legacy <see cref="ActorMovedEvent" />s (pre-snapshot
///     fallback), and removes the node on <see cref="ActorDespawnedEvent" />.
///     PASSIVE: this class does not compute positions, does not know the protocol, and holds zero
///     domain state. It maps ActorKey → VisualActor node.
///     Threading contract: all public methods are called from GameLoop._Process — Godot main thread.
///     Deferred ground placement (debt #2 — NPC fallback-Y race):
///     Actors spawned via the live 4/4 packet path arrive from the Application layer with a Q16.16
///     position whose Y is zero (spec: world_entry.md §2.3 — "Y forced to 0"). The real ground Y
///     is resolved by terrain bilinear interpolation after the covering sector becomes resident.
///     This registry defers Y placement for any actor whose sector is not yet in the terrain cache:
///     the actor is spawned at the fallback Y (the wire value, zero), the (legacyX, legacyZ) pair
///     is queued, and when TerrainNode fires SectorBecameResident the covering actors are snapped
///     to the correct ground height. This mirrors the NpcRenderer.OnSectorBecameResident pattern.
///     spec: Docs/RE/formats/terrain.md — ground height from .ted bilinear interpolation: CONFIRMED.
///     spec: Docs/RE/specs/world_systems.md — actor placement in the live world.
///     spec: Docs/RE/specs/game_loop.md §6 — "updates the spatial transforms of the associated
///     Node3D on the next frame" / snapshot interpolation model.
/// </summary>
public sealed partial class ActorRegistry : Node
{
    // ActorKey → live VisualActor node.
    private readonly Dictionary<ActorKey, VisualActor> _actors = new();

    // Pending ground-snap tracking (debt #2 fix).
    //
    // Each entry stores the actor key and its legacy world XZ so that when the sector covering
    // that XZ becomes resident the actor's Godot Y can be snapped to the bilinear terrain height.
    //
    // Cell key formula (matches TerrainNode.TryGetGroundHeight):
    //   cellMapX = floor(legacyX / 1024) + 10000
    //   cellMapZ = floor(legacyZ / 1024) + 10000
    // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu. CONFIRMED.
    //
    // Bounded wait: if no sector ever arrives (VFS absent), actors remain at the fallback Y
    // and the pending list is never drained — no game logic, just a deferred Y update.
    private readonly List<(ActorKey Key, float LegacyX, float LegacyZ)> _pendingSnaps = new();

    // Set during Initialise() — not constructor, since Godot nodes are wired after _Ready.
    private ClientContext _clientContext = null!;

    // TerrainNode reference (optional) — wired via SetTerrainNode after Initialise.
    // When present, actors are snapped to real terrain Y when their covering sector becomes resident.
    // spec: Docs/RE/formats/terrain.md — ground height from .ted bilinear: CONFIRMED.
    private TerrainNode? _terrainNode;

    public override void _Ready()
    {
        GD.Print("[ActorRegistry] _Ready start");
    }

    /// <summary>Called by GameLoop._Ready before any events can arrive.</summary>
    public void Initialise(ClientContext context)
    {
        _clientContext = context;
    }

    /// <summary>
    ///     Wires the terrain node reference so spawned actors can be snapped to real ground Y
    ///     when their covering sector becomes resident.
    ///     Must be called AFTER <see cref="Initialise" /> and AFTER the TerrainNode is added to the
    ///     scene tree. Called by GameLoop._Ready immediately after both subsystems are wired.
    ///     spec: Docs/RE/formats/terrain.md — TryGetGroundHeight bilinear lookup: CONFIRMED.
    ///     spec: Docs/RE/specs/world_systems.md — actor placement deferred until terrain is resident.
    /// </summary>
    public void SetTerrainNode(TerrainNode terrainNode)
    {
        _terrainNode = terrainNode;
        // Subscribe to the resident signal so we can snap pending actors on each sector arrival.
        // The signal fires on the Godot main thread (inside GameLoop._Process → TerrainNode.OnSectorLoaded).
        // spec: TerrainNode.SectorBecameResident — fired after _cellCache updated. CONFIRMED.
        _terrainNode.SectorBecameResident += OnSectorBecameResident;
        GD.Print("[ActorRegistry] TerrainNode wired — fallback-Y deferred snap enabled. " +
                 "spec: Docs/RE/formats/terrain.md (bilinear ground height).");
    }

    // -------------------------------------------------------------------------
    // WorldSnapshotEvent handler — snapshot interpolation (primary path)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Applies per-actor interpolation snapshots from the fixed-tick <see cref="GameEngineLoop" />.
    ///     For each <see cref="ActorSnapshot" /> in the event, if a matching <see cref="VisualActor" />
    ///     is registered, its interpolation state is updated.
    ///     Unknown actors in the snapshot (not yet spawned) are silently ignored — the spawn event
    ///     will arrive on the next packet and register the node.
    ///     spec: Docs/RE/specs/game_loop.md §6 — "Godot interpolates between simulation snapshots".
    ///     spec: WorldSnapshotEvent — tick + FixedDeltaMs + per-actor ActorSnapshot[].
    /// </summary>
    public void OnWorldSnapshot(WorldSnapshotEvent snapshot)
    {
        // Convert FixedDeltaMs to seconds for VisualActor.ApplySnapshot.
        // spec: Docs/RE/specs/game_loop.md §6 — fixed delta in ms post time-scale.
        var tickSec = snapshot.FixedDeltaMs > 0
            ? snapshot.FixedDeltaMs / 1000.0
            : 1.0 / GameEngineLoop.DefaultTickRateHz;

        foreach (var actorSnap in snapshot.Actors)
            if (_actors.TryGetValue(actorSnap.Key, out var visual))
                visual.ApplySnapshot(in actorSnap, tickSec);
        // Actor not yet in registry → snapshot ignored; spawn event will register it.
    }

    // -------------------------------------------------------------------------
    // Spawn / move / despawn event handlers (called from GameLoop._Process)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Spawns a <see cref="VisualActor" /> node for the given actor, converts the Q16.16
    ///     spawn position to Godot world space at this presentation boundary, and adds it to
    ///     the scene tree.
    ///     Deferred ground placement (debt #2):
    ///     The spawn position Y from the 4/1 + 4/4 path is zero (spec: world_entry.md §2.3 —
    ///     "Y forced to 0"). If a TerrainNode is wired and the covering sector is already resident,
    ///     the real ground Y is looked up immediately via bilinear interpolation and applied.
    ///     Otherwise the actor is placed at the fallback Y and queued for snapping when the sector
    ///     arrives via <see cref="OnSectorBecameResident" />. This eliminates the fallback-Y race
    ///     without polling. spec: Docs/RE/formats/terrain.md — bilinear ground height: CONFIRMED.
    ///     spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
    /// </summary>
    public void OnActorSpawned(ActorSpawnedEvent evt)
    {
        if (_actors.ContainsKey(evt.Key))
            // Duplicate spawn — remove and re-create (server can resend on reconnect).
            RemoveActor(evt.Key);

        var visual = new VisualActor();

        // Convert Q16.16 fixed-point position to Godot float world space.
        // Step 1: Q16.16 → float (presentation boundary). spec: Vector3Fixed.ToVector3Float().
        // Step 2: legacy left-handed → Godot right-handed (negate Z). spec: WorldCoordinates.ToGodot.
        var (fx, fy, fz) = evt.Position.ToVector3Float();

        // Attempt immediate terrain ground-Y lookup (debt #2 fix — eliminate fallback-Y race).
        // spec: Docs/RE/formats/terrain.md — TryGetGroundHeight bilinear interpolation. CONFIRMED.
        var groundY = fy;
        var grounded = false;
        if (_terrainNode is not null)
        {
            grounded = _terrainNode.TryGetGroundHeight(fx, fz, out var terrainY);
            if (grounded) groundY = terrainY;
        }

        visual.ActorKey = evt.Key;
        visual.ActorName = evt.Name;

        // AddChild BEFORE setting GlobalPosition: Godot throws !is_inside_tree() if GlobalPosition
        // is set on a node that has not yet been added to the scene tree.
        // spec: Docs/RE/specs/world_entry.md §2.3 — actor added to tree, then position applied.
        AddChild(visual);

        var (gx, _, gz) = WorldCoordinates.ToGodot(fx, groundY, fz);
        // spec: Docs/RE/specs/world_entry.md §2.3 — spawn Y forced to 0 on the wire; use terrain Y.
        visual.GlobalPosition = new Vector3(gx, groundY, gz);
        _actors[evt.Key] = visual;

        // If the sector is not yet resident, queue for deferred snap when it arrives.
        // spec: Docs/RE/formats/terrain.md — ground height from .ted bilinear: CONFIRMED.
        // Offline path (no terrain node): actor stays at fallback Y, nothing queued — same as before.
        if (!grounded && _terrainNode is not null) _pendingSnaps.Add((evt.Key, fx, fz));

        GD.Print($"[ActorRegistry] Spawned actor {evt.Key.RawId} '{evt.Name}' at {visual.GlobalPosition}" +
                 (grounded ? " (terrain Y)" : " (fallback Y — pending snap)"));
    }

    /// <summary>
    ///     Applies the confirmed position from an <see cref="ActorMovedEvent" /> to the
    ///     corresponding <see cref="VisualActor" /> via the legacy glide path. Used when the
    ///     <see cref="GameEngineLoop" /> / snapshots are not yet running (offline or pre-boot).
    ///     spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
    /// </summary>
    public void OnActorMoved(ActorMovedEvent evt)
    {
        if (!_actors.TryGetValue(evt.Key, out var visual)) return; // Move arrived before spawn.

        // Convert the confirmed move-target to Godot world space at this boundary.
        // Step 1: Q16.16 → float. spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
        // Step 2: legacy → Godot handedness. spec: WorldCoordinates.ToGodot.
        var (fx, fy, fz) = evt.MoveTarget.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        visual.SetMoveTarget(new Vector3(gx, gy, gz), evt.IsRunning);
    }

    /// <summary>
    ///     Removes the <see cref="VisualActor" /> from the scene tree when the actor leaves the world.
    /// </summary>
    public void OnActorDespawned(ActorDespawnedEvent evt)
    {
        if (evt.PlayLeaveEffect) GD.Print($"[ActorRegistry] Actor {evt.Key.RawId} left the area.");

        RemoveActor(evt.Key);
    }

    // -------------------------------------------------------------------------
    // Lookup (presentation only — read-only view of the actor map)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the <see cref="VisualActor" /> for the given key, or <see langword="null" /> if
    ///     the actor is not yet in the registry. Used by <see cref="FloatingCombatText" /> to resolve
    ///     a 3D head position for world→screen projection.
    ///     Threading contract: main thread only (all callers are in _Process).
    /// </summary>
    public VisualActor? TryGetActor(ActorKey key)
    {
        return _actors.TryGetValue(key, out var v) ? v : null;
    }

    // -------------------------------------------------------------------------
    // Ground-snap notification (debt #2 — eliminate fallback-Y race)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Called when a terrain sector becomes resident (TerrainNode.SectorBecameResident fires on
    ///     the Godot main thread). Walks the pending-snap list and snaps any actor whose covering
    ///     cell matches the freshly-loaded sector to the real terrain ground Y.
    ///     Only actors whose covering cell key matches (<paramref name="mapX" />, <paramref name="mapZ" />)
    ///     are processed; others remain pending until their own sector arrives.
    ///     No allocation beyond iterating the list once (removes in place via swap-remove pattern).
    ///     spec: Docs/RE/formats/terrain.md §1.4 — cell key: floor(legacyX/1024)+10000. CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §5.4 — Heights[] direct world-space Y. CONFIRMED.
    ///     spec: Docs/RE/specs/world_systems.md — actor ground placement deferred until resident.
    /// </summary>
    /// <param name="mapX">Biased cell X that just became resident.</param>
    /// <param name="mapZ">Biased cell Z that just became resident.</param>
    private void OnSectorBecameResident(int mapX, int mapZ)
    {
        if (_terrainNode is null) return;
        if (_pendingSnaps.Count == 0) return;

        var i = 0;
        while (i < _pendingSnaps.Count)
        {
            var (key, lx, lz) = _pendingSnaps[i];

            // Compute the cell that covers this actor's legacy XZ.
            // spec: Docs/RE/formats/terrain.md §1.4 — mapX = floor(worldX/1024)+10000. CONFIRMED.
            var actorCellX = (int)Math.Floor(lx / 1024.0) + 10000; // spec: terrain.md §1.4
            var actorCellZ = (int)Math.Floor(lz / 1024.0) + 10000; // spec: terrain.md §1.4

            if (actorCellX != mapX || actorCellZ != mapZ)
            {
                i++;
                continue; // Different sector — leave in queue.
            }

            // The sector covering this actor just became resident — try to snap.
            if (_actors.TryGetValue(key, out var visual) &&
                IsInstanceValid(visual) &&
                _terrainNode.TryGetGroundHeight(lx, lz, out var groundY))
            {
                // Snap the Godot Y to real terrain height; preserve X and Z.
                // spec: Docs/RE/formats/terrain.md §5.4 — Heights[] world-space Y. CONFIRMED.
                var pos = visual.GlobalPosition;
                visual.GlobalPosition = new Vector3(pos.X, groundY, pos.Z);
                GD.Print($"[ActorRegistry] Ground-snap: actor {key.RawId} snapped to Y={groundY:F2} " +
                         $"(sector {mapX},{mapZ}). spec: Docs/RE/formats/terrain.md §5.4.");
            }

            // Remove by swap with last element (O(1), avoids shifting).
            var last = _pendingSnaps.Count - 1;
            if (i < last) _pendingSnaps[i] = _pendingSnaps[last];
            _pendingSnaps.RemoveAt(last);
            // Don't increment i — re-check position i (now holds the swapped element).
        }
    }

    // -------------------------------------------------------------------------
    // 4/4 KindByte==5 — lightweight in-place visual refresh
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Applies a lightweight visual-only refresh to an already-spawned actor.
    ///     KindByte == 5 means the actor is NOT removed or rebuilt — only its
    ///     visual attributes are updated in place.
    ///     RelationVisual byte meaning is capture-pending; no game-rule value is
    ///     stored here. If the actor is not yet in the registry (refresh-before-spawn),
    ///     the call is a no-op.
    ///     Main-thread only. PASSIVE: no respawn, no node removal.
    ///     spec: Docs/RE/structs/actor.md (4/4 record: KindByte == 5 visual-only refresh).
    /// </summary>
    public void OnActorVisualRefreshed(ActorVisualRefreshedEvent evt)
    {
        if (!_actors.TryGetValue(evt.Key, out var visual) || !IsInstanceValid(visual))
        {
            // Refresh-before-spawn — silently skip; spawn event will register the node.
            // spec: Docs/RE/structs/actor.md (KindByte==5: actor must already exist; no respawn).
            GD.Print($"[ActorRegistry] ActorVisualRefreshed: actor {evt.Key.RawId} not in registry " +
                     "(refresh-before-spawn) — no-op. spec: Docs/RE/structs/actor.md (KindByte==5).");
            return;
        }

        // Lightweight in-place refresh: do NOT remove/re-add the node, do NOT rebuild the avatar.
        // RelationVisual meaning is capture-pending — log it faithfully, store nothing game-relevant.
        // TODO(world-campaign): when RelationVisual meaning is recovered, apply a relation tint here.
        // spec: Docs/RE/structs/actor.md (KindByte==5: visual-only refresh; RelationVisual capture-pending).
        GD.Print($"[ActorRegistry] ActorVisualRefreshed: actor {evt.Key.RawId} " +
                 $"relationVisual={evt.RelationVisual} (meaning capture-pending). " +
                 "In-place only — no respawn. spec: Docs/RE/structs/actor.md (KindByte==5).");
    }

    // -------------------------------------------------------------------------
    // 5/10 death — play victim death motion (no despawn; server sends 5/0 separately)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Plays the death motion/pose on the VICTIM's VisualActor (5/10 SmsgCharDeath).
    ///     Does NOT despawn the actor — the server sends a separate 5/0 despawn.
    ///     Clear-locked-target UI is a HUD concern (not owned here).
    ///     If the victim is not in the registry (death before spawn), the call is a no-op.
    ///     Main-thread only. PASSIVE: no domain mutation.
    ///     spec: Docs/RE/packets/5-10_combat_death.yaml (death: play motion; despawn via 5/0 separately).
    /// </summary>
    public void OnActorDied(ActorDiedEvent evt)
    {
        if (!_actors.TryGetValue(evt.VictimKey, out var visual) || !IsInstanceValid(visual))
        {
            // Death arrived before spawn or after despawn — no-op.
            GD.Print($"[ActorRegistry] ActorDied: victim {evt.VictimKey.RawId} not in registry — " +
                     "no-op. spec: Docs/RE/packets/5-10_combat_death.yaml.");
            return;
        }

        // Play the death pose on the victim's visual. PlayDeathMotion is idempotent.
        // spec: Docs/RE/packets/5-10_combat_death.yaml (5/10 = play death; no despawn here).
        visual.PlayDeathMotion();
        GD.Print($"[ActorRegistry] ActorDied: victim={evt.VictimKey.RawId} cause={evt.DeathCause} " +
                 $"isPkA={evt.IsPkA} isPkB={evt.IsPkB} — death motion played. " +
                 "spec: Docs/RE/packets/5-10_combat_death.yaml.");
    }

    // -------------------------------------------------------------------------
    // 4/13 local-player state sync — snap or glide reconcile
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Reconciles the local player's transform from a 4/13 SmsgLocalPlayerStateSync.
    ///     Mode 5 = no state write (early return — spec-faithful).
    ///     A squared position delta &gt; 200² (legacy units) is a teleport — SNAP GlobalPosition.
    ///     Otherwise, smooth glide via the existing SetMoveTarget path.
    ///     Heading: raw Q16.16 heading — the yaw convention is facing-pending (not statically recovered);
    ///     GD.Print the value for diagnostics rather than fabricating a rotation.
    ///     Main-thread only. PASSIVE: no domain mutation.
    ///     spec: Docs/RE/packets/4-13_local_player_state_sync.yaml
    ///     (&gt;200-unit delta = teleport; mode 5 = no write; heading = raw Q16.16).
    /// </summary>
    public void OnLocalPlayerStateSynced(LocalPlayerStateSyncedEvent evt)
    {
        // Mode 5 = no state write. spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (mode 5 = no write).
        if (evt.Mode == 5)
        {
            GD.Print("[ActorRegistry] LocalPlayerStateSynced: mode=5 (no-write) — skipped. " +
                     "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
            return;
        }

        if (!_actors.TryGetValue(evt.Key, out var visual) || !IsInstanceValid(visual))
        {
            // Local player not yet registered — silently skip.
            GD.Print($"[ActorRegistry] LocalPlayerStateSynced: local player {evt.Key.RawId} not in " +
                     "registry — skip. spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
            return;
        }

        // Convert the synced position to Godot world space using the SAME boundary as OnActorSpawned/OnActorMoved.
        // Step 1: Q16.16 → float at the presentation boundary. spec: Vector3Fixed.ToVector3Float().
        // Step 2: legacy left-handed → Godot right-handed (negate Z). spec: WorldCoordinates.ToGodot.
        var (fx, fy, fz) = evt.Position.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        var syncedPos = new Vector3(gx, gy, gz);

        // Teleport threshold: >200 legacy units squared.
        // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (">200-unit delta is a teleport").
        const float TeleportThresholdSq = 200f * 200f; // spec: 4-13_local_player_state_sync.yaml
        var delta = syncedPos - visual.GlobalPosition;
        var squaredDelta = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;

        if (squaredDelta > TeleportThresholdSq)
        {
            // Large delta — teleport: snap GlobalPosition directly.
            // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (">200-unit delta is a teleport").
            visual.GlobalPosition = syncedPos;
            GD.Print($"[ActorRegistry] LocalPlayerStateSynced: TELEPORT snap to {syncedPos} " +
                     $"(delta²={squaredDelta:F0} > {TeleportThresholdSq:F0}). " +
                     "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
        }
        else
        {
            // Small delta — smooth glide via the existing SetMoveTarget path (not running for a state sync).
            // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (small delta = smooth correction).
            visual.SetMoveTarget(syncedPos, false);
            GD.Print($"[ActorRegistry] LocalPlayerStateSynced: smooth glide to {syncedPos} " +
                     $"mode={evt.Mode} heading={evt.Heading} (yaw convention facing-pending — not fabricated). " +
                     "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
        }

        // Heading: raw Q16.16 — the heading→yaw convention is facing-pending (not statically recovered).
        // We GD.Print the raw value for diagnostics rather than fabricating a rotation convention.
        // spec: Docs/RE/packets/4-13_local_player_state_sync.yaml (heading = raw Q16.16; convention pending).
        GD.Print($"[ActorRegistry] LocalPlayerStateSynced: heading={evt.Heading} (facing-pending — " +
                 "not applied; convention not statically recovered). " +
                 "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void RemoveActor(ActorKey key)
    {
        if (_actors.Remove(key, out var visual)) visual.QueueFree();

        // Remove any pending-snap entry for the despawned actor so it does not attempt
        // to snap a freed node later.
        for (var i = _pendingSnaps.Count - 1; i >= 0; i--)
            if (_pendingSnaps[i].Key == key)
            {
                _pendingSnaps.RemoveAt(i);
                break;
            }
    }
}