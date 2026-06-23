
using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

internal sealed class BindPosePool
{
    private readonly Dictionary<int, Skeleton> _byActorId;

    private BindPosePool(Dictionary<int, Skeleton> byActorId)
    {
        _byActorId = byActorId;
    }

    public int Count => _byActorId.Count;

    public static BindPosePool Build(BindlistData? bindlist, RealClientAssets assets)
    {
        const string bindDir = "data/char/bind/";
        var entries = bindlist?.Entries ?? [];
        var map = new Dictionary<int, Skeleton>(entries.Count);
        foreach (var filename in entries)
        {
            var path = bindDir + filename;
            if (!assets.Contains(path)) continue;
            try
            {
                var raw = assets.GetRaw(path);
                if (raw.IsEmpty) continue;
                var skel = BndParser.Parse(raw);
                map.TryAdd((int)skel.ActorId, skel);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharVisualRegistry] bind-pool: '{path}' parse failed: {ex.Message}");
            }
        }

        return new BindPosePool(map);
    }

    public bool TryGetByIdB(int idB, out Skeleton skeleton)
    {
        if (idB > 0 && _byActorId.TryGetValue(idB, out var s))
        {
            skeleton = s;
            return true;
        }

        skeleton = null!;
        return false;
    }
}

internal sealed class MotlistRegistry
{
    private readonly Dictionary<int, string> _idBToPath;

    private MotlistRegistry(Dictionary<int, string> idBToPath)
    {
        _idBToPath = idBToPath;
    }

    public int Count => _idBToPath.Count;

    public static MotlistRegistry Build(MotlistData? motlist, RealClientAssets assets)
    {
        var entries = motlist?.Entries ?? [];
        var map = new Dictionary<int, string>(entries.Count);
        foreach (var filename in entries)
        {
            var path = MotlistData.MotDirPrefix + filename;
            if (!assets.Contains(path)) continue;
            try
            {
                var raw = assets.GetRaw(path);
                if (raw.IsEmpty) continue;
                var clip = AnimationParser.Parse(raw);
                if (clip is null) continue;
                map.TryAdd((int)clip.IdB, path);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharVisualRegistry] mot-registry: '{path}' header read failed: {ex.Message}");
            }
        }

        return new MotlistRegistry(map);
    }

    public string? ResolvePath(int idB)
    {
        return _idBToPath.TryGetValue(idB, out var p) ? p : null;
    }
}

internal sealed class CharVisualRegistry
{
    private const string BindlistPath = "data/char/bindlist.txt";
    private const string MotlistPath = "data/char/motlist.txt";
    private const string ActormotionPath = "data/char/actormotion.txt";

    private static CharVisualRegistry? _cached;
    private static RealClientAssets? _cachedFor;

    private CharVisualRegistry(BindPosePool pool, MotlistRegistry mot, ActormotionCatalogue actorMotion)
    {
        BindPool = pool;
        Motlist = mot;
        ActorMotion = actorMotion;
    }

    public BindPosePool BindPool { get; }

    public MotlistRegistry Motlist { get; }

    public ActormotionCatalogue ActorMotion { get; }

    public static CharVisualRegistry? GetOrBuild(RealClientAssets assets)
    {
        if (_cached is not null && ReferenceEquals(_cachedFor, assets))
            return _cached;

        BindlistData? bindlist = null;
        MotlistData? motlist = null;
        ActormotionCatalogue? actorMotion = null;

        try
        {
            if (assets.Contains(BindlistPath))
                bindlist = BindlistParser.Parse(assets.GetRaw(BindlistPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharVisualRegistry] bindlist.txt parse failed: {ex.Message}");
        }

        try
        {
            if (assets.Contains(MotlistPath))
                motlist = MotlistParser.Parse(assets.GetRaw(MotlistPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharVisualRegistry] motlist.txt parse failed: {ex.Message}");
        }

        try
        {
            if (assets.Contains(ActormotionPath))
                actorMotion = ActormotionParser.Parse(assets.GetRaw(ActormotionPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharVisualRegistry] actormotion.txt parse failed: {ex.Message}");
        }

        if (bindlist is null && motlist is null && actorMotion is null)
            return null;

        var pool = BindPosePool.Build(bindlist, assets);
        var motReg = MotlistRegistry.Build(motlist, assets);
        var am = actorMotion ?? ActormotionParser.ParseText(string.Empty);

        var registry = new CharVisualRegistry(pool, motReg, am);
        _cached = registry;
        _cachedFor = assets;
        GD.Print($"[CharVisualRegistry] Built: bindPool={pool.Count} motlist={motReg.Count} actormotion={am.Count}. " +
                 "spec: skinning.md §8(e) (verbatim id_b pool) / formats/animation.md (header id_b mot registry).");
        return registry;
    }

    public Skeleton? TryGetSkeletonByIdB(int idB)
    {
        return BindPool.TryGetByIdB(idB, out var s) ? s : null;
    }

    public string? ResolveMotPath(int idB)
    {
        return Motlist.ResolvePath(idB);
    }

    public ActormotionEntry? GetByMotionKey(int motionKey)
    {
        return motionKey < 0 ? null : ActorMotion.GetByMotionKey((uint)motionKey);
    }
}