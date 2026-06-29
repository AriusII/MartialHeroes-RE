using Godot;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class WorldNpcSpawner : Node3D
{
    private const float SpawnAvatarScale = 5.0f;

    private readonly List<(Node3D Node, float LegacyX, float LegacyZ)> _pendingSnaps = new();

    private RealClientAssets? _assets;
    private bool _terrainHooked;
    private TerrainNode? _terrainNode;

    public void Initialise(RealClientAssets assets, TerrainNode? terrainNode)
    {
        _assets = assets;
        _terrainNode = terrainNode;

        if (_terrainNode is not null && !_terrainHooked)
        {
            _terrainNode.SectorBecameResident += OnSectorBecameResident;
            _terrainHooked = true;
        }
    }

    public void SpawnArea(IReadOnlyList<AreaSpawnDescriptor> spawns)
    {
        if (_assets is null)
        {
            GD.Print(
                "[WorldNpcSpawner] No VFS handle — area spawns rendered as nothing. spec: entity_placement.md §8.");
            return;
        }

        ClearSpawns();

        var registry = CharVisualRegistry.GetOrBuild(_assets);
        if (registry is null)
        {
            GD.Print("[WorldNpcSpawner] No CharVisualRegistry (char tables absent) — no NPC avatars built. " +
                     "spec: skinning.md §8(e).");
            return;
        }

        var built = 0;
        var skipped = 0;

        for (var i = 0; i < spawns.Count; i++)
        {
            var spawn = spawns[i];

            var skinClass = ResolveSkinClass(registry, spawn.VisualId);
            if (skinClass <= 0)
            {
                skipped++;
                GD.Print($"[WorldNpcSpawner] AreaSpawn[{i}] visualId={spawn.VisualId}: actormotion col1 lookup " +
                         "did not resolve a skin_class — SKIPPED (no fabrication). " +
                         "spec: CLAUDE.md mob_id→actormotion→skin chain; entity_placement.md §8.");
                continue;
            }

            var avatar = PlayerAvatarResolver.TryBuild(_assets, (ushort)skinClass);
            if (avatar is null)
            {
                skipped++;
                GD.Print($"[WorldNpcSpawner] AreaSpawn[{i}] visualId={spawn.VisualId} skinClass={skinClass}: " +
                         "no .skn with IdB==skin_class resolved — SKIPPED. spec: skinning.md §8(e).");
                continue;
            }

            PlaceAvatar(avatar, spawn);
            built++;
        }

        GD.Print($"[WorldNpcSpawner] Area spawns instantiated: built={built} skipped={skipped} " +
                 $"(pendingGroundSnaps={_pendingSnaps.Count}). spec: entity_placement.md §1/§3/§8.");
    }

    private static int ResolveSkinClass(CharVisualRegistry registry, int visualId)
    {
        var entry = registry.ActorMotion.GetByIntraOffset(visualId);
        return entry?.SkinClassId ?? 0;
    }

    private void PlaceAvatar(Node3D avatar, AreaSpawnDescriptor spawn)
    {
        var legacyX = spawn.WorldX;
        var legacyZ = spawn.WorldZ;

        var groundY = 0f;
        var grounded = false;
        if (_terrainNode is not null)
        {
            grounded = _terrainNode.TryGetGroundHeight(legacyX, legacyZ, out var terrainY);
            if (grounded) groundY = terrainY;
        }

        avatar.Scale = Vector3.One * SpawnAvatarScale;
        avatar.Rotation = new Vector3(0f, spawn.Yaw, 0f);
        AddChild(avatar);

        var (gx, _, gz) = WorldCoordinates.ToGodot(legacyX, groundY, legacyZ);
        avatar.GlobalPosition = new Vector3(gx, groundY, gz);

        if (!grounded && _terrainNode is not null)
            _pendingSnaps.Add((avatar, legacyX, legacyZ));
    }

    private void OnSectorBecameResident(int mapX, int mapZ)
    {
        if (_terrainNode is null || _pendingSnaps.Count == 0) return;

        var i = 0;
        while (i < _pendingSnaps.Count)
        {
            var (node, lx, lz) = _pendingSnaps[i];

            var cellX = (int)Math.Floor(lx / 1024.0) + 10000;
            var cellZ = (int)Math.Floor(lz / 1024.0) + 10000;

            if (cellX != mapX || cellZ != mapZ)
            {
                i++;
                continue;
            }

            if (IsInstanceValid(node) && _terrainNode.TryGetGroundHeight(lx, lz, out var groundY))
            {
                var pos = node.GlobalPosition;
                node.GlobalPosition = new Vector3(pos.X, groundY, pos.Z);
                GD.Print($"[WorldNpcSpawner] Ground-snap: spawn snapped to Y={groundY:F2} " +
                         $"(sector {mapX},{mapZ}). spec: Docs/RE/formats/terrain.md §5.4.");
            }

            var last = _pendingSnaps.Count - 1;
            if (i < last) _pendingSnaps[i] = _pendingSnaps[last];
            _pendingSnaps.RemoveAt(last);
        }
    }

    private void ClearSpawns()
    {
        _pendingSnaps.Clear();

        foreach (var child in GetChildren())
            if (child is Node3D n && IsInstanceValid(n))
            {
                RemoveChild(n);
                n.QueueFree();
            }
    }

    public override void _ExitTree()
    {
        if (_terrainNode is not null && _terrainHooked)
        {
            _terrainNode.SectorBecameResident -= OnSectorBecameResident;
            _terrainHooked = false;
        }
    }
}