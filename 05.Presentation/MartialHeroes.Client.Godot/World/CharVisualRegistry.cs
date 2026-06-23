// World/CharVisualRegistry.cs
//
// Layer-05 composition-root singleton that builds ONCE from the VFS (first use) and caches the four
// data-driven character catalogues the spawn factory queries by the APPEARANCE KEY / id_b instead of
// the invented g{id}.bnd / g{id}.mot sprintf rules:
//
//   (a) BindPosePool  — bindlist.txt -> BndParser per entry -> keyed by Skeleton.ActorId (the parsed
//       .bnd header actor_id). This is the faithful "pose_pool[id_b]" of skinning.md §8(e): the bnd is
//       resolved by parsed actor_id, NOT by a derived g{n}.bnd filename (there is no filename rule — the
//       bnd is named in bindlist.txt). For the four players the pool key equals model_class_id reduced to
//       {1,2,3,4} (g1..g4.bnd parse to actor_id 1..4).
//   (b) MotlistRegistry — motlist.txt -> for each filename read 'data/char/mot/'+filename, parse ONLY
//       the .mot header (AnimationParser exposes IdB = header id_b @ +4), build id_b -> path. This is the
//       faithful replacement for g{id}.mot: the public motion-id registry is keyed by the clip header
//       id_b (MotClip_RegisterByPath keys clip[18] = id_b), NOT a derived numeric filename.
//   (c) ActormotionCatalogue — parsed WITH the recovered CategoryBase { 0, 0, 10000, 1000 } (default
//       parser overload), so GetByMotionKey(model_class_id) is the authoritative player idle key.
//
// The literal directory prefixes are the recovered strings: 'data/char/bind/' for the bnd pool and
// 'data/char/mot/' for the mot registry. Bindlist preload is eager in the binary; here it is built lazily
// on first use and cached (header-only .mot reads for the mot index — no full keyframe parse).
//
// HONEST GAPS (logged, never fabricated):
//   - A needed actor whose .bnd is absent from bindlist.txt correctly MISSES (rest pose) instead of
//     coincidentally loading g{n}.bnd.
//   - Building the full id_b -> mot map reads every .mot header; that is bounded but not free. We build
//     lazily and cache. A .mot whose header cannot be read is skipped (logged).
//
// spec: skinning.md §8(e) (verbatim id_b pose-pool key); formats/bindlist.md (eager preload, actor_id
//       key); formats/animation.md (header id_b @ +4); formats/actormotion.md (motion_key = col1 +
//       CategoryBase[(byte)(col0+1)]).

using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     The bind-pose pool: skeletons keyed by their parsed <c>.bnd</c> header <c>actor_id</c> (the
///     verbatim <c>id_b</c> pose-pool key, skinning §8(e)). Built from <c>bindlist.txt</c>; a skeleton
///     not listed there is not registered even if its <c>.bnd</c> exists in the VFS.
/// </summary>
internal sealed class BindPosePool
{
    private readonly Dictionary<int, Skeleton> _byActorId;

    private BindPosePool(Dictionary<int, Skeleton> byActorId)
    {
        _byActorId = byActorId;
    }

    /// <summary>Number of registered skeletons.</summary>
    public int Count => _byActorId.Count;

    /// <summary>
    ///     Builds the pool from <c>bindlist.txt</c>: for each listed filename read
    ///     <c>data/char/bind/</c> + filename, <see cref="BndParser" /> it, and key by
    ///     <see cref="Skeleton.ActorId" />. First occurrence wins on a duplicate actor_id.
    ///     spec: formats/bindlist.md (register by parsed actor_id); skinning §8(e).
    /// </summary>
    public static BindPosePool Build(BindlistData? bindlist, RealClientAssets assets)
    {
        const string bindDir = "data/char/bind/"; // recovered dir prefix
        IReadOnlyList<string> entries = bindlist?.Entries ?? [];
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
                map.TryAdd((int)skel.ActorId, skel); // verbatim actor_id key (§8(e))
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharVisualRegistry] bind-pool: '{path}' parse failed: {ex.Message}");
            }
        }

        return new BindPosePool(map);
    }

    /// <summary>
    ///     Resolves the skeleton for <paramref name="idB" /> (the skin header <c>id_b</c>) by the verbatim
    ///     pool key. Returns <see langword="false" /> (rest pose) when no <c>.bnd</c> with that parsed
    ///     <c>actor_id</c> is registered. spec: skinning §8(e).
    /// </summary>
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

/// <summary>
///     The motion-id registry: <c>.mot</c> VFS paths keyed by their clip header <c>id_b</c>. Faithful
///     replacement for the invented <c>g{id}.mot</c> sprintf — the runtime registers each clip from
///     <c>motlist.txt</c> by its header <c>id_b</c> (<c>clip[18]</c>), not a derived numeric filename.
/// </summary>
internal sealed class MotlistRegistry
{
    private readonly Dictionary<int, string> _idBToPath;

    private MotlistRegistry(Dictionary<int, string> idBToPath)
    {
        _idBToPath = idBToPath;
    }

    /// <summary>Number of registered clip ids.</summary>
    public int Count => _idBToPath.Count;

    /// <summary>
    ///     Builds the registry from <c>motlist.txt</c>: for each listed filename read
    ///     <c>data/char/mot/</c> + filename, parse the <c>.mot</c> header and key by its <c>id_b</c>.
    ///     First occurrence wins. The BANI variant (which <see cref="AnimationParser.Parse(System.ReadOnlyMemory{byte})" />
    ///     returns null for) is skipped. spec: MotList_LoadAndRegister (id_b key, 'data/char/mot/' prefix).
    /// </summary>
    public static MotlistRegistry Build(MotlistData? motlist, RealClientAssets assets)
    {
        IReadOnlyList<string> entries = motlist?.Entries ?? [];
        var map = new Dictionary<int, string>(entries.Count);
        foreach (var filename in entries)
        {
            var path = MotlistData.MotDirPrefix + filename;
            if (!assets.Contains(path)) continue;
            try
            {
                var raw = assets.GetRaw(path);
                if (raw.IsEmpty) continue;
                var clip = AnimationParser.Parse(raw); // header parse suffices for id_b
                if (clip is null) continue; // BANI variant — not loadable (faithful skip)
                map.TryAdd((int)clip.IdB, path);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharVisualRegistry] mot-registry: '{path}' header read failed: {ex.Message}");
            }
        }

        return new MotlistRegistry(map);
    }

    /// <summary>
    ///     Resolves the <c>.mot</c> VFS path for a motion id (the clip header <c>id_b</c>), or
    ///     <see langword="null" /> when no clip with that id is registered. The faithful replacement for
    ///     <c>data/char/mot/g{id}.mot</c>.
    /// </summary>
    public string? ResolvePath(int idB)
    {
        return _idBToPath.TryGetValue(idB, out var p) ? p : null;
    }
}

/// <summary>
///     Layer-05 composition-root that builds ONCE (lazily, cached) the data-driven character catalogues
///     the spawn factory queries by appearance key / id_b. Shared by the four resolvers + the live-spawn
///     path so they do not re-parse per slot. Construct via <see cref="GetOrBuild" /> with an open VFS.
/// </summary>
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

    /// <summary>The bind-pose pool (skeleton by verbatim id_b / parsed actor_id).</summary>
    public BindPosePool BindPool { get; }

    /// <summary>The motion-id registry (.mot path by clip header id_b).</summary>
    public MotlistRegistry Motlist { get; }

    /// <summary>The actormotion catalogue (parsed with the recovered CategoryBase).</summary>
    public ActormotionCatalogue ActorMotion { get; }

    /// <summary>
    ///     Returns the cached registry for <paramref name="assets" />, building it on first use. The cache
    ///     is keyed by the VFS handle identity so a re-opened VFS rebuilds. Returns <see langword="null" />
    ///     only when the core registries (bindlist / motlist / actormotion) are entirely absent.
    /// </summary>
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
            return null; // nothing to build (offline / VFS missing the registries)

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

    /// <summary>
    ///     Resolves the skeleton for a skin <c>id_b</c> through the bind-pose pool (verbatim key).
    ///     Returns <see langword="null" /> (rest pose) when not registered.
    /// </summary>
    public Skeleton? TryGetSkeletonByIdB(int idB)
    {
        return BindPool.TryGetByIdB(idB, out var s) ? s : null;
    }

    /// <summary>Resolves a <c>.mot</c> VFS path by motion id (clip header id_b), or null.</summary>
    public string? ResolveMotPath(int idB)
    {
        return Motlist.ResolvePath(idB);
    }

    /// <summary>Looks up the actormotion record by computed motion key (= appearance key for players).</summary>
    public ActormotionEntry? GetByMotionKey(int motionKey)
    {
        return motionKey < 0 ? null : ActorMotion.GetByMotionKey((uint)motionKey);
    }
}
