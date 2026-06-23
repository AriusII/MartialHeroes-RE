using Godot;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class NpcRenderer : Node3D
{
    public delegate bool TryGetHeightDelegate(float worldX, float worldZ, out float height);

    private const int SkinTickGroups = 6;


    private static Dictionary<int, string>? _skinClassToSknPath;
    private static RealClientAssets? _skinCacheOwner;

    private static Dictionary<int, ActormotionEntry>? _actorMotionLookup;
    private static RealClientAssets? _actorMotionCacheOwner;

    private readonly double[] _bucketAccum = new double[SkinTickGroups];

    private readonly List<PendingSnap> _pendingSnaps = new();

    private readonly List<SkinnedActor> _skinnedActors = new();

    private uint _phaseRng = 0x9E3779B9u;

    private int _tickCursor;
    private int _totalGrounded;
    private int _totalSkinned;

    private int _totalSpawned;

    private int _totalStaticFallback;

    public int MaxSpawns { get; set; } = 40;

    public float CharacterScale { get; set; } = 5.0f;

    public float GroundY { get; set; } = 26f;

    public Func<float, float, float>? GroundYFunc { get; set; }

    public TryGetHeightDelegate? TryGroundYFunc { get; set; }


    public float SkinTickHz { get; set; } = 10f;


    public override void _Process(double delta)
    {
        var count = _skinnedActors.Count;
        if (count == 0) return;

        for (var b = 0; b < SkinTickGroups; b++)
            _bucketAccum[b] += delta;

        var bucket = _tickCursor;
        _tickCursor = (_tickCursor + 1) % SkinTickGroups;

        var dt = (float)_bucketAccum[bucket];
        _bucketAccum[bucket] = 0.0;
        if (dt <= 0f) return;

        for (var i = 0; i < count; i++)
        {
            var sa = _skinnedActors[i];
            if (sa.Bucket != bucket) continue;
            if (!IsInstanceValid(sa.Node)) continue;
            sa.Node.Tick(dt);
        }
    }


    private float ResolveGroundY(float worldX, float worldZ)
    {
        return GroundYFunc is not null ? GroundYFunc(worldX, worldZ) : GroundY;
    }

    private void ClearChildren()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }

    private void RegisterPendingSnap(Node3D node, float legacyX, float legacyZ)
    {
        if (TryGroundYFunc is not null && TryGroundYFunc(legacyX, legacyZ, out var immediateY))
        {
            var pos = node.Position;
            pos.Y = immediateY;
            node.Position = pos;
            _totalGrounded++;
            return;
        }

        var cellMapX = (int)Math.Floor(legacyX / 1024.0) + 10000;
        var cellMapZ = (int)Math.Floor(legacyZ / 1024.0) + 10000;
        _pendingSnaps.Add(new PendingSnap(node, legacyX, legacyZ, cellMapX, cellMapZ));
    }


    private readonly record struct PendingSnap(
        Node3D Node,
        float LegacyX,
        float LegacyZ,
        int CellMapX,
        int CellMapZ);

    private readonly record struct SkinnedActor(SkinnedCharacterNode Node, int Bucket);
}