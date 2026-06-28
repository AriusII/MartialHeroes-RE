using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Presentation.Screens;
using MartialHeroes.Client.Presentation.World;

namespace MartialHeroes.Explorer.Viewer;

public sealed record SkeletonResolution(
    Skeleton? Skeleton,
    string Path,
    string Mode,
    uint MatchedActorId,
    int Coverage);

public sealed record MotionResolution(
    string[] Paths,
    int IdleMotionId,
    string? IdlePath,
    string Provenance,
    int ModelClipCount,
    int RegistryTotal);

public sealed record MotRegistry(Dictionary<int, string> ByIdB, Dictionary<int, string> ByIdA)
{
    public (string? Path, string Via) Resolve(int id)
    {
        if (id <= 0) return (null, "none");
        if (ByIdB.TryGetValue(id, out var pb)) return (pb, "id_b");
        if (ByIdA.TryGetValue(id, out var pa)) return (pa, "id_a");
        return (null, "miss");
    }

    public string[] AllPaths()
    {
        var set = new HashSet<string>(ByIdA.Values);
        foreach (var p in ByIdB.Values) set.Add(p);
        return [.. set];
    }
}

public static class CharacterAssetResolver
{
    private const string BindlistPath = "data/char/bindlist.txt";
    private const string BindDirPrefix = "data/char/bind/";
    private const string MotlistPath = "data/char/motlist.txt";
    private const string ActormotionPath = "data/char/actormotion.txt";

    private static Dictionary<int, (Skeleton Skeleton, string Path)>? _bindPool;
    private static MappedVfsArchive? _bindPoolFor;

    private static MotRegistry? _motRegistry;
    private static MappedVfsArchive? _motRegistryFor;

    public static SkeletonResolution ResolveSkeleton(MappedVfsArchive archive, SkinnedMesh mesh)
    {
        var pool = GetOrBuildBindPool(archive);

        if (pool.TryGetValue((int)mesh.IdB, out var hit))
            return new SkeletonResolution(hit.Skeleton, hit.Path, "id_b-verbatim",
                hit.Skeleton.ActorId, CoverageOf(hit.Skeleton, mesh));

        return new SkeletonResolution(null, string.Empty, "none", 0, 0);
    }

    public static string? FindDefaultBodySknPath(MappedVfsArchive archive, IReadOnlyList<string> sknPaths)
    {
        string? fallback = null;

        foreach (var path in sknPaths)
            try
            {
                var raw = archive.GetFileContent(path);
                if (raw.IsEmpty) continue;
                var mesh = SknParser.Parse(raw);
                var idB = (int)mesh.IdB;
                if (idB is < 1 or > 4) continue;

                fallback ??= path;

                bool textured;
                try
                {
                    textured = ViewerTextures.ResolveSkn(archive, mesh).Texture is not null;
                }
                catch (Exception)
                {
                    textured = false;
                }

                if (textured) return path;
            }
            catch (Exception)
            {
            }

        return fallback;
    }

    private static Dictionary<int, (Skeleton Skeleton, string Path)> GetOrBuildBindPool(MappedVfsArchive archive)
    {
        if (_bindPool is not null && ReferenceEquals(_bindPoolFor, archive))
            return _bindPool;

        var pool = BuildBindPool(archive);
        _bindPool = pool;
        _bindPoolFor = archive;
        return pool;
    }

    private static Dictionary<int, (Skeleton Skeleton, string Path)> BuildBindPool(MappedVfsArchive archive)
    {
        var pool = new Dictionary<int, (Skeleton, string)>();
        if (!archive.Contains(BindlistPath)) return pool;

        BindlistData bindlist;
        try
        {
            bindlist = BindlistParser.Parse(archive.GetFileContent(BindlistPath));
        }
        catch (Exception)
        {
            return pool;
        }

        foreach (var filename in bindlist.Entries)
        {
            var path = BindDirPrefix + filename;
            if (!archive.Contains(path)) continue;
            try
            {
                var raw = archive.GetFileContent(path);
                if (raw.IsEmpty) continue;
                var skel = BndParser.Parse(raw);
                pool.TryAdd((int)skel.ActorId, (skel, path));
            }
            catch (Exception)
            {
            }
        }

        return pool;
    }

    public static MotionResolution ResolveMotionPaths(MappedVfsArchive archive, Skeleton? skeleton, SkinnedMesh mesh)
    {
        var registry = GetOrBuildMotRegistry(archive);
        var registryTotal = registry.ByIdA.Count;
        if (registryTotal == 0 && registry.ByIdB.Count == 0)
            return new MotionResolution([], 0, null, "no-motlist", 0, 0);

        var lookup = LoadActorMotion(archive, skeleton, mesh);
        var entry = lookup.Entry;
        var idleId = entry?.IdleMotionId ?? 0;
        var (idlePath, idleVia) = registry.Resolve(idleId);

        if (entry is null)
        {
            var all = registry.AllPaths();
            return new MotionResolution(all, idleId, idlePath, $"{lookup.Mode}-full-registry", all.Length,
                registryTotal);
        }

        var ordered = new List<string>();
        var seen = new HashSet<string>();

        void Add(int id)
        {
            if (id <= 0) return;
            var (p, _) = registry.Resolve(id);
            if (p is null) return;
            if (seen.Add(p)) ordered.Add(p);
        }

        Add(idleId);
        foreach (var row in lookup.ActorRows)
        foreach (var id in row.DirArray1)
            Add(id);

        if (ordered.Count == 0)
        {
            var all = registry.AllPaths();
            return new MotionResolution(all, idleId, idlePath, $"{lookup.Mode}-no-resolved-clips-full-registry",
                all.Length, registryTotal);
        }

        var provenance = idlePath is not null ? $"{lookup.Mode}/idle-{idleVia}" : $"{lookup.Mode}/idle-miss";
        return new MotionResolution(ordered.ToArray(), idleId, idlePath, provenance, ordered.Count, registryTotal);
    }

    private static MotRegistry GetOrBuildMotRegistry(MappedVfsArchive archive)
    {
        if (_motRegistry is not null && ReferenceEquals(_motRegistryFor, archive))
            return _motRegistry;

        var reg = BuildMotRegistry(archive);
        _motRegistry = reg;
        _motRegistryFor = archive;
        return reg;
    }

    private static MotRegistry BuildMotRegistry(MappedVfsArchive archive)
    {
        var byIdB = new Dictionary<int, string>();
        var byIdA = new Dictionary<int, string>();
        if (!archive.Contains(MotlistPath)) return new MotRegistry(byIdB, byIdA);

        MotlistData motlist;
        try
        {
            motlist = MotlistParser.Parse(archive.GetFileContent(MotlistPath));
        }
        catch (Exception)
        {
            return new MotRegistry(byIdB, byIdA);
        }

        foreach (var filename in motlist.Entries)
        {
            var p = filename.Replace('\\', '/');
            var path = p.Contains('/') ? p : MotlistData.MotDirPrefix + p;
            if (!archive.Contains(path)) continue;
            try
            {
                var raw = archive.GetFileContent(path);
                if (raw.IsEmpty) continue;
                var clip = AnimationParser.Parse(raw);
                if (clip is null) continue;
                byIdB.TryAdd((int)clip.IdB, path);
                byIdA.TryAdd((int)clip.IdA, path);
            }
            catch (Exception)
            {
            }
        }

        return new MotRegistry(byIdB, byIdA);
    }

    public static SkinningMath.VertexInfluences[] BuildInfluences(
        SknWeight[] weights, int vertexCount, int[] idToIndex, int baseId, int boneCount)
    {
        var raw = new List<(int Bone, float W)>[vertexCount];
        for (var i = 0; i < vertexCount; i++) raw[i] = [];

        foreach (var wr in weights)
        {
            if (wr.Weight < SkinningMath.WeightSkipThreshold) continue;

            var vi = (int)wr.VertexIndex;
            if ((uint)vi >= (uint)vertexCount) continue;

            var bid = (int)(wr.BoneIndex & 0xFF);
            var bIdx = bid is >= 0 and < 256 ? idToIndex[bid] : -1;
            if (bIdx < 0)
            {
                var off = (int)wr.BoneIndex - baseId;
                bIdx = off >= 0 && off < boneCount ? off : -1;
            }

            if (bIdx < 0 || bIdx >= boneCount) continue;

            raw[vi].Add((bIdx, wr.Weight));
        }

        var result = new SkinningMath.VertexInfluences[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            var list = raw[v];
            if (list.Count == 0)
            {
                result[v] = new SkinningMath.VertexInfluences
                {
                    Items = [new SkinningMath.Influence { BoneIndex = 0, Weight = 1f }]
                };
                continue;
            }

            var total = 0f;
            foreach (var it in list) total += it.W;
            var inv = total > 1e-8f ? 1f / total : 1f;

            var items = new SkinningMath.Influence[list.Count];
            for (var k = 0; k < list.Count; k++)
                items[k] = new SkinningMath.Influence { BoneIndex = list[k].Bone, Weight = list[k].W * inv };

            result[v] = new SkinningMath.VertexInfluences { Items = items };
        }

        return result;
    }

    private static int CoverageOf(Skeleton skeleton, SkinnedMesh mesh)
    {
        if (mesh.Weights.Length == 0) return 0;

        var boneIds = new HashSet<uint>(skeleton.Bones.Length);
        foreach (var b in skeleton.Bones) boneIds.Add(b.SelfId & 0xFFu);

        var seen = new HashSet<uint>();
        var covered = 0;
        foreach (var w in mesh.Weights)
        {
            var id = w.BoneIndex & 0xFFu;
            if (!seen.Add(id)) continue;
            if (boneIds.Contains(id)) covered++;
        }

        return covered;
    }

    private static ActorMotionLookup LoadActorMotion(
        MappedVfsArchive archive, Skeleton? skeleton, SkinnedMesh mesh)
    {
        if (!archive.Contains(ActormotionPath))
            return new ActorMotionLookup(null, "no-actormotion", []);

        try
        {
            var catalogue = ActormotionParser.Parse(archive.GetFileContent(ActormotionPath));
            var skinClass = (int)mesh.IdB;

            if (skinClass is >= 1 and <= 4)
            {
                ActormotionEntry? primary = null;
                var mode = "no-row";

                var appearanceKey = ClassAppearanceResolver.StarterBodyModelClassId(skinClass);
                if (appearanceKey > 0)
                {
                    primary = catalogue.GetByMotionKey((uint)appearanceKey);
                    if (primary is not null) mode = "appearance-key";
                }

                if (primary is null)
                {
                    primary = catalogue.GetBySkinClass(skinClass);
                    if (primary is not null) mode = "skin-class-fallback";
                }

                return new ActorMotionLookup(primary, mode, CollectActorRows(catalogue, primary, skinClass));
            }

            if (skeleton is not null)
            {
                var actorClass = (int)skeleton.ActorId;
                var byActor = catalogue.GetBySkinClass(actorClass);
                var rows = CollectActorRows(catalogue, byActor, actorClass);
                return new ActorMotionLookup(byActor, byActor is not null ? "mob-skin-class" : "no-row", rows);
            }

            return new ActorMotionLookup(null, "no-row", []);
        }
        catch (Exception)
        {
            return new ActorMotionLookup(null, "actormotion-parse-failed", []);
        }
    }

    private static IReadOnlyList<ActormotionEntry> CollectActorRows(
        ActormotionCatalogue catalogue, ActormotionEntry? primary, int skinClass)
    {
        var rows = new List<ActormotionEntry>();
        if (primary is not null) rows.Add(primary);

        foreach (var e in catalogue.AllEntries)
        {
            if (e.IntA != skinClass) continue;
            if (ReferenceEquals(e, primary)) continue;
            if (primary is not null && e.Col0Category != primary.Col0Category) continue;
            rows.Add(e);
        }

        return rows;
    }

    private sealed record ActorMotionLookup(
        ActormotionEntry? Entry,
        string Mode,
        IReadOnlyList<ActormotionEntry> ActorRows);
}