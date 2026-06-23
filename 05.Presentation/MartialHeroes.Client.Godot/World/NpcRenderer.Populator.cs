
using System.Text;
using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.World;
using MartialHeroes.Assets.Parsers.World.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class NpcRenderer
{

    public void PopulateFromArea(RealClientAssets assets, int areaId)
    {
        ClearChildren();
        _pendingSnaps.Clear();
        _skinnedActors.Clear();
        Array.Clear(_bucketAccum);
        _tickCursor = 0;
        _totalSpawned = 0;
        _totalGrounded = 0;
        _totalSkinned = 0;
        _totalStaticFallback = 0;

        if (areaId == 0)
        {
            GD.Print("[NpcRenderer] Area 0 — no mob/NPC spawns (expected).");
            return;
        }

        var tag = AreaTag(areaId);

        EnsureActorMotionLoaded(assets);
        EnsureSkinClassMapLoaded(assets);

        var spawned = 0;

        PopulateMobSpawns(assets, tag, areaId, ref spawned);

        PopulateNpcSpawns(assets, tag, areaId, ref spawned);

        _totalSpawned = spawned;

        GD.Print($"[NpcRenderer] summary: {_totalSkinned} skinned / {_totalStaticFallback} static-fallback / " +
                 $"{_totalGrounded} grounded ({spawned} spawned, cap={MaxSpawns}, " +
                 $"{_pendingSnaps.Count} pending terrain arrival).");
    }


    private void PopulateMobSpawns(RealClientAssets assets, string tag, int areaId, ref int spawned)
    {
        var mobArrPath = $"data/map{tag}/mob{tag}.arr";
        if (!assets.Contains(mobArrPath))
        {
            GD.Print($"[NpcRenderer] No mob{tag}.arr for area {areaId} — skipping monster spawns.");
            return;
        }

        var mobData = assets.GetRaw(mobArrPath);
        if (mobData.IsEmpty) return;

        MobSpawnRecord[] mobRecords;
        try
        {
            mobRecords = MobSpawnParser.Parse(mobData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] MobSpawnParser.Parse failed for '{mobArrPath}': {ex.Message}");
            mobRecords = [];
        }

        GD.Print($"[NpcRenderer] mob{tag}.arr: {mobRecords.Length} unique records.");

        foreach (var rec in mobRecords)
        {
            if (spawned >= MaxSpawns) break;

            var node = TryBuildFromMobId(assets, rec.MobId);
            if (node is null) continue;

            var gy = ResolveGroundY(rec.WorldX, rec.WorldZ);
            node.Position = new Vector3(rec.WorldX, gy, -rec.WorldZ);
            node.Scale = Vector3.One * CharacterScale;
            node.Name = $"Mob_{rec.MobId}_{spawned}";
            AddChild(node);
            spawned++;

            RegisterPendingSnap(node, rec.WorldX, rec.WorldZ);
        }
    }

    private void PopulateNpcSpawns(RealClientAssets assets, string tag, int areaId, ref int spawned)
    {
        var npcArrPath = $"data/map{tag}/npc{tag}.arr";
        if (!assets.Contains(npcArrPath))
        {
            GD.Print($"[NpcRenderer] No npc{tag}.arr for area {areaId} — skipping NPC spawns.");
            return;
        }

        var npcData = assets.GetRaw(npcArrPath);
        if (npcData.IsEmpty) return;

        NpcSpawnArray npcArray;
        try
        {
            npcArray = NpcSpawnParser.Parse(npcData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] NpcSpawnParser.Parse failed for '{npcArrPath}': {ex.Message}");
            npcArray = new NpcSpawnArray { Records = [] };
        }

        GD.Print($"[NpcRenderer] npc{tag}.arr: {npcArray.Records.Length} records.");

        foreach (var rec in npcArray.Records)
        {
            if (spawned >= MaxSpawns) break;
            if (rec.MobId == 0) continue;

            var node = TryBuildFromMobId(assets, rec.MobId)
                       ?? TryBuildDirectSkinProbe(assets, rec.MobId);
            if (node is null) continue;

            var gy = ResolveGroundY(rec.WorldX, rec.WorldZ);
            node.Position = new Vector3(rec.WorldX, gy, -rec.WorldZ);
            node.Scale = Vector3.One * CharacterScale;
            node.Name = $"Npc_{rec.MobId}_{spawned}";
            AddChild(node);
            spawned++;

            RegisterPendingSnap(node, rec.WorldX, rec.WorldZ);
        }
    }

    public void PopulateFromSpawns(
        RealClientAssets assets,
        IReadOnlyList<AreaSpawnDescriptor> spawns)
    {
        ClearChildren();
        _pendingSnaps.Clear();
        _skinnedActors.Clear();
        Array.Clear(_bucketAccum);
        _tickCursor = 0;
        _totalSpawned = 0;
        _totalGrounded = 0;
        _totalSkinned = 0;
        _totalStaticFallback = 0;

        if (spawns.Count == 0)
        {
            GD.Print("[NpcRenderer][Composer] spawn list empty (area has no .arr spawns or area 0).");
            return;
        }

        EnsureActorMotionLoaded(assets);
        EnsureSkinClassMapLoaded(assets);

        var spawned = 0;

        foreach (var desc in spawns)
        {
            if (spawned >= MaxSpawns) break;

            var visualId = desc.VisualId;
            if (visualId == 0) continue;

            var node = TryBuildFromMobId(assets, (ushort)visualId);
            if (node is null && desc.IsNpc)
                node = TryBuildDirectSkinProbe(assets, (ushort)visualId);
            if (node is null) continue;

            var gy = ResolveGroundY(desc.WorldX, desc.WorldZ);
            node.Position = new Vector3(desc.WorldX, gy, -desc.WorldZ);
            node.Rotation = new Vector3(0f, desc.Yaw, 0f);
            node.Scale = Vector3.One * CharacterScale;
            var kind = desc.IsNpc ? "Npc" : "Mob";
            node.Name = $"{kind}_{visualId}_{spawned}";
            AddChild(node);
            spawned++;

            RegisterPendingSnap(node, desc.WorldX, desc.WorldZ);
        }

        _totalSpawned = spawned;

        GD.Print($"[NpcRenderer][Composer] PopulateFromSpawns summary: {_totalSkinned} skinned / " +
                 $"{_totalStaticFallback} static-fallback / {_totalGrounded} grounded " +
                 $"({spawned} spawned from {spawns.Count} composer descriptors, cap={MaxSpawns}, " +
                 $"{_pendingSnaps.Count} pending terrain arrival). " +
                 "spec: assembly_graph.md §1 (Phase A — spawns from AreaAssembledEvent).");
    }


    private static void EnsureActorMotionLoaded(RealClientAssets assets)
    {
        if (_actorMotionCacheOwner == assets && _actorMotionLookup is not null)
            return;

        _actorMotionCacheOwner = assets;
        _actorMotionLookup = null;

        const string tablePath = "data/char/actormotion.txt";
        if (!assets.Contains(tablePath))
        {
            GD.Print($"[NpcRenderer] '{tablePath}' absent — actormotion chain disabled.");
            _actorMotionLookup = new Dictionary<int, ActormotionEntry>(0);
            return;
        }

        try
        {
            var raw = assets.GetRaw(tablePath);
            _actorMotionLookup = ActormotionParser.ParseAsLookup(raw);
            GD.Print($"[NpcRenderer] actormotion.txt loaded: {_actorMotionLookup.Count} entries.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] actormotion.txt parse failed: {ex.Message}");
            _actorMotionLookup = new Dictionary<int, ActormotionEntry>(0);
        }
    }

    private static void EnsureSkinClassMapLoaded(RealClientAssets assets)
    {
        if (_skinCacheOwner == assets && _skinClassToSknPath is not null)
            return;

        _skinCacheOwner = assets;
        _skinClassToSknPath = null;

        const string listPath = "data/char/skinlist.txt";
        if (!assets.Contains(listPath))
        {
            GD.Print($"[NpcRenderer] '{listPath}' absent — skinlist-based skin resolution disabled.");
            _skinClassToSknPath = new Dictionary<int, string>(0);
            return;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var raw = assets.GetRaw(listPath);
            var text = Encoding.GetEncoding(949).GetString(raw.Span);

            var map = new Dictionary<int, string>(1024);
            var parsedCount = 0;
            var errorCount = 0;

            const string skinDir = "data/char/skin/";

            foreach (var rawLine in text.Split('\n'))
            {
                var fname = rawLine.Trim('\r', '\n', ' ').ToLowerInvariant();
                if (fname.Length == 0) continue;

                if (!fname.EndsWith(".skn", StringComparison.Ordinal)) continue;

                var sknPath = skinDir + fname;
                if (!assets.Contains(sknPath)) continue;

                try
                {
                    var sknData = assets.GetRaw(sknPath);
                    if (sknData.IsEmpty) continue;

                    var mesh = SknParser.Parse(sknData);
                    var idB = (int)mesh.IdB;
                    if (idB == 0) continue;

                    map.TryAdd(idB, sknPath);
                    parsedCount++;
                }
                catch
                {
                    errorCount++;
                }
            }

            _skinClassToSknPath = map;
            GD.Print($"[NpcRenderer] skinlist.txt scan: {map.Count} unique skin_class mappings " +
                     $"({parsedCount} parsed, {errorCount} errors).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] skinlist.txt scan failed: {ex.Message}");
            _skinClassToSknPath = new Dictionary<int, string>(0);
        }
    }


    public void OnSectorBecameResident(int mapX, int mapZ)
    {
        if (TryGroundYFunc is null) return;
        if (_pendingSnaps.Count == 0) return;

        var snapped = 0;

        for (var i = _pendingSnaps.Count - 1; i >= 0; i--)
        {
            var ps = _pendingSnaps[i];

            if (!IsInstanceValid(ps.Node))
            {
                _pendingSnaps.RemoveAt(i);
                continue;
            }

            if (!TryGroundYFunc(ps.LegacyX, ps.LegacyZ, out var correctY))
                continue;

            var pos = ps.Node.Position;
            pos.Y = correctY;
            ps.Node.Position = pos;

            _pendingSnaps.RemoveAt(i);
            snapped++;
        }

        if (snapped > 0)
        {
            _totalGrounded += snapped;
            GD.Print($"[NpcRenderer] Sector ({mapX},{mapZ}) resident — grounded {snapped} actors " +
                     $"({_totalGrounded}/{_totalSpawned} total, {_pendingSnaps.Count} still pending).");
        }
    }
}