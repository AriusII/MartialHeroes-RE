using Godot;
using MartialHeroes.Client.Application.Contracts.Events;
using MartialHeroes.Client.Application.Engine;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Presentation.Helpers;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class ActorRegistry : Node
{
    private readonly Dictionary<ActorKey, VisualActor> _actors = new();

    private readonly List<(ActorKey Key, float LegacyX, float LegacyZ)> _pendingSnaps = new();

    private ClientContext _clientContext = null!;

    private TerrainNode? _terrainNode;

    public override void _Ready()
    {
        GD.Print("[ActorRegistry] _Ready start");
    }

    public void Initialise(ClientContext context)
    {
        _clientContext = context;
    }

    public void SetTerrainNode(TerrainNode terrainNode)
    {
        _terrainNode = terrainNode;
        _terrainNode.SectorBecameResident += OnSectorBecameResident;
        GD.Print("[ActorRegistry] TerrainNode wired — fallback-Y deferred snap enabled. " +
                 "spec: Docs/RE/formats/terrain.md (bilinear ground height).");
    }


    public void OnWorldSnapshot(WorldSnapshotEvent snapshot)
    {
        var tickSec = snapshot.FixedDeltaMs > 0
            ? snapshot.FixedDeltaMs / 1000.0
            : 1.0 / GameEngineLoop.DefaultTickRateHz;

        foreach (var actorSnap in snapshot.Actors)
            if (_actors.TryGetValue(actorSnap.Key, out var visual))
                visual.ApplySnapshot(in actorSnap, tickSec);
    }


    public void OnActorSpawned(ActorSpawnedEvent evt)
    {
        if (_actors.ContainsKey(evt.Key))
            RemoveActor(evt.Key);

        var visual = new VisualActor();

        var (fx, fy, fz) = evt.Position.ToVector3Float();

        var groundY = fy;
        var grounded = false;
        if (_terrainNode is not null)
        {
            grounded = _terrainNode.TryGetGroundHeight(fx, fz, out var terrainY);
            if (grounded) groundY = terrainY;
        }

        visual.ActorKey = evt.Key;
        visual.ActorName = evt.Name;

        AddChild(visual);

        var (gx, _, gz) = WorldCoordinates.ToGodot(fx, groundY, fz);
        visual.GlobalPosition = new Vector3(gx, groundY, gz);
        _actors[evt.Key] = visual;

        if (!grounded && _terrainNode is not null) _pendingSnaps.Add((evt.Key, fx, fz));

        GD.Print($"[ActorRegistry] Spawned actor {evt.Key.RawId} '{evt.Name}' at {visual.GlobalPosition}" +
                 (grounded ? " (terrain Y)" : " (fallback Y — pending snap)"));
    }

    public void OnActorMoved(ActorMovedEvent evt)
    {
        if (!_actors.TryGetValue(evt.Key, out var visual)) return;

        var (fx, fy, fz) = evt.MoveTarget.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        visual.SetMoveTarget(new Vector3(gx, gy, gz), evt.IsRunning);
    }

    public void OnActorDespawned(ActorDespawnedEvent evt)
    {
        if (evt.PlayLeaveEffect) GD.Print($"[ActorRegistry] Actor {evt.Key.RawId} left the area.");

        RemoveActor(evt.Key);
    }


    public VisualActor? TryGetActor(ActorKey key)
    {
        return _actors.TryGetValue(key, out var v) ? v : null;
    }


    private void OnSectorBecameResident(int mapX, int mapZ)
    {
        if (_terrainNode is null) return;
        if (_pendingSnaps.Count == 0) return;

        var i = 0;
        while (i < _pendingSnaps.Count)
        {
            var (key, lx, lz) = _pendingSnaps[i];

            var actorCellX = (int)Math.Floor(lx / 1024.0) + 10000;
            var actorCellZ = (int)Math.Floor(lz / 1024.0) + 10000;

            if (actorCellX != mapX || actorCellZ != mapZ)
            {
                i++;
                continue;
            }

            if (_actors.TryGetValue(key, out var visual) &&
                IsInstanceValid(visual) &&
                _terrainNode.TryGetGroundHeight(lx, lz, out var groundY))
            {
                var pos = visual.GlobalPosition;
                visual.GlobalPosition = new Vector3(pos.X, groundY, pos.Z);
                GD.Print($"[ActorRegistry] Ground-snap: actor {key.RawId} snapped to Y={groundY:F2} " +
                         $"(sector {mapX},{mapZ}). spec: Docs/RE/formats/terrain.md §5.4.");
            }

            var last = _pendingSnaps.Count - 1;
            if (i < last) _pendingSnaps[i] = _pendingSnaps[last];
            _pendingSnaps.RemoveAt(last);
        }
    }


    public void OnActorVisualRefreshed(ActorVisualRefreshedEvent evt)
    {
        if (!_actors.TryGetValue(evt.Key, out var visual) || !IsInstanceValid(visual))
        {
            GD.Print($"[ActorRegistry] ActorVisualRefreshed: actor {evt.Key.RawId} not in registry " +
                     "(refresh-before-spawn) — no-op. spec: Docs/RE/structs/actor.md (KindByte==5).");
            return;
        }

        GD.Print($"[ActorRegistry] ActorVisualRefreshed: actor {evt.Key.RawId} " +
                 $"relationVisual={evt.RelationVisual} (meaning capture-pending). " +
                 "In-place only — no respawn. spec: Docs/RE/structs/actor.md (KindByte==5).");
    }


    public void OnActorDied(ActorDiedEvent evt)
    {
        if (!_actors.TryGetValue(evt.VictimKey, out var visual) || !IsInstanceValid(visual))
        {
            GD.Print($"[ActorRegistry] ActorDied: victim {evt.VictimKey.RawId} not in registry — " +
                     "no-op. spec: Docs/RE/packets/5-10_combat_death.yaml.");
            return;
        }

        visual.PlayDeathMotion();
        GD.Print($"[ActorRegistry] ActorDied: victim={evt.VictimKey.RawId} cause={evt.DeathCause} " +
                 $"isPkA={evt.IsPkA} isPkB={evt.IsPkB} — death motion played. " +
                 "spec: Docs/RE/packets/5-10_combat_death.yaml.");
    }


    public void OnLocalPlayerStateSynced(LocalPlayerStateSyncedEvent evt)
    {
        if (evt.Mode == 5)
        {
            GD.Print("[ActorRegistry] LocalPlayerStateSynced: mode=5 (no-write) — skipped. " +
                     "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
            return;
        }

        if (!_actors.TryGetValue(evt.Key, out var visual) || !IsInstanceValid(visual))
        {
            GD.Print($"[ActorRegistry] LocalPlayerStateSynced: local player {evt.Key.RawId} not in " +
                     "registry — skip. spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
            return;
        }

        var (fx, fy, fz) = evt.Position.ToVector3Float();
        var (gx, gy, gz) = WorldCoordinates.ToGodot(fx, fy, fz);
        var syncedPos = new Vector3(gx, gy, gz);

        const float TeleportThresholdSq = 200f * 200f;
        var delta = syncedPos - visual.GlobalPosition;
        var squaredDelta = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;

        if (squaredDelta > TeleportThresholdSq)
        {
            visual.GlobalPosition = syncedPos;
            GD.Print($"[ActorRegistry] LocalPlayerStateSynced: TELEPORT snap to {syncedPos} " +
                     $"(delta²={squaredDelta:F0} > {TeleportThresholdSq:F0}). " +
                     "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
        }
        else
        {
            visual.SetMoveTarget(syncedPos, false);
            GD.Print($"[ActorRegistry] LocalPlayerStateSynced: smooth glide to {syncedPos} " +
                     $"mode={evt.Mode} heading={evt.Heading} (yaw convention facing-pending — not fabricated). " +
                     "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
        }

        GD.Print($"[ActorRegistry] LocalPlayerStateSynced: heading={evt.Heading} (facing-pending — " +
                 "not applied; convention not statically recovered). " +
                 "spec: Docs/RE/packets/4-13_local_player_state_sync.yaml.");
    }


    private void RemoveActor(ActorKey key)
    {
        if (_actors.Remove(key, out var visual)) visual.QueueFree();

        for (var i = _pendingSnaps.Count - 1; i >= 0; i--)
            if (_pendingSnaps[i].Key == key)
            {
                _pendingSnaps.RemoveAt(i);
                break;
            }
    }
}