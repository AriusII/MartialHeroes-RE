// World/NpcRenderer.Populator.cs
//
// Partial class — area population / placement / spawn iteration, lazy lookup-table loading,
// ground-snap notification, and the g2048 pivot probe.
// See NpcRenderer.cs for the full file description and all spec cites.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/npc_spawns.md (NPC .arr format, 28-byte stride).
// spec: MISSION B (mob .arr format, 20-byte stride).

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
    // -------------------------------------------------------------------------
    // g2048 pivot probe
    // -------------------------------------------------------------------------

    /// <summary>
    ///     One-time explicit probe of the spec's canonical MOB rig (g2048) so its stand-up pivot is
    ///     derived from its own rest AABB and printed, independently of g1. Builds the trio
    ///     (g2048.bnd + g219000630.skn + g182006900.mot) off-tree, logs the pivot decision via the
    ///     builder's diagnostics, then frees the temporary node. Skips silently if the assets are
    ///     absent (the trio is user-supplied and may not be present in every install).
    ///     spec: MISSION TASK 2 — "derive/verify the pivot empirically for mob rigs; check g2048
    ///     explicitly and print per-rig pivot decisions."
    ///     spec: Docs/RE/specs/skinning.md §8(d) — mob trio paths.
    /// </summary>
    private static bool _g2048Probed;
    // -------------------------------------------------------------------------
    // Primary entry point
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Loads both monster (<c>mob{tag}.arr</c>) and NPC (<c>npc{tag}.arr</c>) spawn files
    ///     for the given area, resolves each spawn's model via the confirmed mob_id→skin chain,
    ///     builds static-pose character nodes, and places them in the scene.
    ///     <para>Must be called on the Godot main thread.</para>
    ///     spec: Docs/RE/formats/npc_spawns.md §Identification — "data/map{NNN}/npc{NNN}.arr".
    ///     spec: MISSION B — mob{tag}.arr path, 20-byte records, MobId u16 @0, WorldX f32 @4, WorldZ f32 @8.
    /// </summary>
    /// <param name="assets">Open VFS assets handle.</param>
    /// <param name="areaId">Area identifier (e.g. 1 for map001). Area 0 yields zero records.</param>
    public void PopulateFromArea(RealClientAssets assets, int areaId)
    {
        // Remove previously spawned children and reset tracking state.
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
            // Area 0 has no spawn data (map000 .arr files are stubs / anomalies).
            // spec: Docs/RE/formats/npc_spawns.md §Anomaly: map 000 — 16 bytes → 0 records: CONFIRMED.
            GD.Print("[NpcRenderer] Area 0 — no mob/NPC spawns (expected).");
            return;
        }

        var tag = AreaTag(areaId);

        // Ensure the shared lookup tables are built (lazy, per-assets-instance).
        EnsureActorMotionLoaded(assets);
        EnsureSkinClassMapLoaded(assets);

        // STAND-UP PIVOT verification: the +90°-about-Z pivot was validated on the g1 PLAYER rig
        // only. Explicitly probe the spec's canonical MOB rig (g2048) so its pivot decision is
        // derived from ITS OWN rest AABB rather than inherited from g1.
        // spec: MISSION TASK 2 — "check g2048 explicitly and print per-rig pivot decisions."
        // spec: Docs/RE/specs/skinning.md §8(d) — g2048.bnd (45 bones) + g219000630.skn (933 verts).
        ProbeG2048Pivot(assets);

        var spawned = 0;

        // ── Step 1: Monster spawns (mob{tag}.arr, 20-byte records) ─────────────
        // spec: MISSION B — "Load mob{tag}.arr (MobSpawnParser)".
        PopulateMobSpawns(assets, tag, areaId, ref spawned);

        // ── Step 2: NPC spawns (npc{tag}.arr, 28-byte records) ─────────────────
        // spec: Docs/RE/formats/npc_spawns.md §Identification — path pattern. CONFIRMED.
        // spec: MISSION B — "Load npc{tag}.arr (NpcSpawnParser)".
        PopulateNpcSpawns(assets, tag, areaId, ref spawned);

        _totalSpawned = spawned;

        // Mandatory summary line. spec: MISSION VERIFY — "[NpcRenderer] summary: X skinned /
        // Y static-fallback / Z grounded (grounding must stay 23/40)."
        GD.Print($"[NpcRenderer] summary: {_totalSkinned} skinned / {_totalStaticFallback} static-fallback / " +
                 $"{_totalGrounded} grounded ({spawned} spawned, cap={MaxSpawns}, " +
                 $"{_pendingSnaps.Count} pending terrain arrival).");

        // One mob AABB sanity line so headless runs can confirm "plausible, not exploded".
        // spec: MISSION VERIFY — "print one mob AABB sanity line".
        PrintMobAabbSanity();
    }

    // -------------------------------------------------------------------------
    // Spawn sub-steps (extracted from PopulateFromArea for readability)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Loads <c>mob{tag}.arr</c>, builds a Node3D for each record via <see cref="TryBuildFromMobId" />,
    ///     places it in the scene, and registers each actor for deferred Y-snap.
    ///     Called only from <see cref="PopulateFromArea" />; behavior is identical to the inlined original.
    ///     spec: MISSION B — "Load mob{tag}.arr (MobSpawnParser)".
    /// </summary>
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

            // Place at Godot (worldX, groundY, -worldZ).
            // spec: Helpers/WorldCoordinates.cs — ToGodot: (x,y,z) → (x,y,-z). CONFIRMED.
            // spec: MISSION B — "Place at Godot (WorldX, groundY, -WorldZ)."
            var gy = ResolveGroundY(rec.WorldX, rec.WorldZ);
            node.Position = new Vector3(rec.WorldX, gy, -rec.WorldZ);
            node.Scale = Vector3.One * CharacterScale;
            node.Name = $"Mob_{rec.MobId}_{spawned}";
            AddChild(node);
            spawned++;

            // Register for deferred Y-correction once the cell's heightmap loads.
            // Cell-key formula matches TerrainNode.TryGetGroundHeight.
            // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024: CONFIRMED.
            RegisterPendingSnap(node, rec.WorldX, rec.WorldZ);
        }
    }

    /// <summary>
    ///     Loads <c>npc{tag}.arr</c>, builds a Node3D for each record via the actormotion chain
    ///     (with fallback to direct-skin probe), places it in the scene, and registers each actor
    ///     for deferred Y-snap.
    ///     Called only from <see cref="PopulateFromArea" />; behavior is identical to the inlined original.
    ///     spec: Docs/RE/formats/npc_spawns.md §Identification — path pattern. CONFIRMED.
    ///     spec: MISSION B — "Load npc{tag}.arr (NpcSpawnParser)".
    /// </summary>
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

            // NPC spawns also go through the actormotion chain first; if that fails we
            // fall back to the old best-effort probe (data/char/skin/g{MobId}.skn).
            var node = TryBuildFromMobId(assets, rec.MobId)
                       ?? TryBuildDirectSkinProbe(assets, rec.MobId);
            if (node is null) continue;

            // spec: Docs/RE/formats/npc_spawns.md — world_x f32 @4, world_z f32 @8: CONFIRMED.
            var gy = ResolveGroundY(rec.WorldX, rec.WorldZ);
            node.Position = new Vector3(rec.WorldX, gy, -rec.WorldZ);
            node.Scale = Vector3.One * CharacterScale;
            node.Name = $"Npc_{rec.MobId}_{spawned}";
            AddChild(node);
            spawned++;

            // Register for deferred Y-correction once the cell's heightmap loads.
            // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024: CONFIRMED.
            RegisterPendingSnap(node, rec.WorldX, rec.WorldZ);
        }
    }

    /// <summary>
    ///     Composer-driven actor placement path (CYCLE 2 Phase 2-A.5).
    ///     Places actors from the area's pre-composed <see cref="AreaSpawnDescriptor" /> list (sourced
    ///     from <c>AreaAssembledEvent.Spawns</c>) instead of re-reading the raw <c>.arr</c> files.
    ///     Shares all of the same internal machinery as <see cref="PopulateFromArea" /> (actormotion lookup,
    ///     skin-class map, <see cref="TryBuildFromMobId" />, pending-snap Y-defer).
    ///     Must be called on the Godot main thread. The <paramref name="assets" /> handle must be the same
    ///     live handle used by the rest of the renderer (for skin loading).
    ///     spec: Docs/RE/specs/assembly_graph.md §1 (Phase A — spawns reach layer-05 via AreaAssembledEvent).
    ///     spec: Docs/RE/formats/npc_spawns.md (world_x @4, world_z @8, facing → yaw=π/2−value: already applied).
    ///     spec: Helpers/WorldCoordinates.cs — ToGodot: (x,y,z) → (x,y,−z): CONFIRMED.
    /// </summary>
    /// <param name="assets">Open VFS assets handle — shared with the rest of the renderer.</param>
    /// <param name="spawns">
    ///     Pre-composed spawn list from <c>AreaAssembledEvent.Spawns</c>. Faithfully empty for area 0
    ///     or when no <c>.arr</c> data was present — never synthesised.
    /// </param>
    public void PopulateFromSpawns(
        RealClientAssets assets,
        IReadOnlyList<AreaSpawnDescriptor> spawns)
    {
        // Remove previously spawned children and reset tracking state.
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

        // Ensure the shared lookup tables are built (lazy, per-assets-instance).
        EnsureActorMotionLoaded(assets);
        EnsureSkinClassMapLoaded(assets);

        var spawned = 0;

        foreach (var desc in spawns)
        {
            if (spawned >= MaxSpawns) break;

            // VisualId maps to MobId in the actormotion/skin chain.
            // spec: assembly_graph.md §4 — VisualId is the mob_id / NPC template id.
            var visualId = desc.VisualId;
            if (visualId == 0) continue;

            // Reuse TryBuildFromMobId (the actormotion→skin chain), identical to PopulateFromArea.
            // For NPCs (desc.IsNpc true) we also try the direct-skin probe as fallback.
            var node = TryBuildFromMobId(assets, (ushort)visualId);
            if (node is null && desc.IsNpc)
                node = TryBuildDirectSkinProbe(assets, (ushort)visualId);
            if (node is null) continue;

            // Place at Godot (worldX, groundY, -worldZ).
            // spec: Helpers/WorldCoordinates.cs — ToGodot: (x,y,z) → (x,y,-z). CONFIRMED.
            // spec: Docs/RE/formats/npc_spawns.md — world_x @4, world_z @8. CONFIRMED.
            var gy = ResolveGroundY(desc.WorldX, desc.WorldZ);
            node.Position = new Vector3(desc.WorldX, gy, -desc.WorldZ);
            // Yaw from AreaSpawnDescriptor already has π/2 − rawFacing applied by AreaComposer.
            // spec: Docs/RE/formats/npc_spawns.md §facing — "runtime applies π/2 − value". CONFIRMED.
            node.Rotation = new Vector3(0f, desc.Yaw, 0f);
            node.Scale = Vector3.One * CharacterScale;
            var kind = desc.IsNpc ? "Npc" : "Mob";
            node.Name = $"{kind}_{visualId}_{spawned}";
            AddChild(node);
            spawned++;

            // Register for deferred Y-correction once the cell's heightmap loads.
            // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024: CONFIRMED.
            RegisterPendingSnap(node, desc.WorldX, desc.WorldZ);
        }

        _totalSpawned = spawned;

        GD.Print($"[NpcRenderer][Composer] PopulateFromSpawns summary: {_totalSkinned} skinned / " +
                 $"{_totalStaticFallback} static-fallback / {_totalGrounded} grounded " +
                 $"({spawned} spawned from {spawns.Count} composer descriptors, cap={MaxSpawns}, " +
                 $"{_pendingSnaps.Count} pending terrain arrival). " +
                 "spec: assembly_graph.md §1 (Phase A — spawns from AreaAssembledEvent).");

        PrintMobAabbSanity();
    }

    private void ProbeG2048Pivot(RealClientAssets assets)
    {
        if (_g2048Probed) return;
        _g2048Probed = true;

        const string sknPath = "data/char/skin/g219000630.skn";
        const string bndPath = "data/char/bind/g2048.bnd";
        const string motPath = "data/char/mot/g182006900.mot";

        if (!assets.Contains(sknPath) || !assets.Contains(bndPath))
        {
            GD.Print("[NpcRenderer] g2048 pivot probe: trio assets absent — skipping explicit check.");
            return;
        }

        try
        {
            var mesh = SknParser.Parse(assets.GetRaw(sknPath));
            var skel = BndParser.Parse(assets.GetRaw(bndPath));
            var clip = assets.Contains(motPath)
                ? AnimationParser.Parse(assets.GetRaw(motPath))
                : null;

            GD.Print($"[NpcRenderer] g2048 pivot probe: skn={mesh.Positions.Length}v " +
                     $"bnd={skel.Bones.Length}bones mot={(clip is null ? "none" : $"{clip.FrameCount}f")}.");

            // Build off-tree (externalDrive so it never self-ticks) purely to trigger the per-rig
            // pivot derivation + diagnostics, then free it. The builder prints the pivot decision.
            var probe = SkinnedCharacterBuilder.Build(
                mesh, skel, clip, null,
                true, 0f,
                out _, "g2048(mob-canonical)");
            probe.QueueFree();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] g2048 pivot probe failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Lazy lookup-table loading
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds the actormotion lookup (mob_id → <see cref="ActormotionEntry" />) from
    ///     <c>data/char/actormotion.txt</c> on first call and caches it.
    ///     spec: ActormotionParser.ParseAsLookup — keyed by ActorClassId. CONFIRMED.
    ///     spec: Docs/RE/formats/mesh.md §actormotion.txt. CONFIRMED.
    /// </summary>
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

    /// <summary>
    ///     Builds the skin-class → skn-path dictionary by scanning <c>data/char/skinlist.txt</c>
    ///     on first call and caches it.
    ///     skinlist.txt is a plain-text list of VFS paths, one per line (CP949).  For each non-empty
    ///     line that names a <c>.skn</c> file, we parse the file header to read <c>IdB</c> and map
    ///     <c>skinClass → sknPath</c>.  Only the first occurrence of each skin_class is retained
    ///     (mirrors the "first entry" guarantee in <see cref="ActormotionEntry" />).
    ///     Parsing is done header-only (SknParser reads the full file but allocates only once per
    ///     unique path); this is acceptable for a one-time startup scan of ~900 entries.
    ///     spec: MISSION B — "build a Dictionary&lt;int skin_class, string sknPath&gt; ONCE by
    ///     scanning data/char/skinlist.txt entries that parse with matching IdB; cache it."
    ///     spec: ActormotionEntry — "id_b == SkinClassId for all 5 spot-checked triples: CONFIRMED."
    ///     spec: Docs/RE/formats/mesh.md §.skn header — "id_b at +4". CONFIRMED.
    /// </summary>
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
            // spec: project brief — "ALL game text is CP949".
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var raw = assets.GetRaw(listPath);
            var text = Encoding.GetEncoding(949).GetString(raw.Span);

            var map = new Dictionary<int, string>(1024);
            var parsedCount = 0;
            var errorCount = 0;

            // skinlist.txt lines are bare filenames (e.g. "g200002620.skn"), NOT full VFS paths.
            // The full VFS path is "data/char/skin/" + filename.
            // Verified on real VFS: all 1269 lines are bare filenames present under data/char/skin/.
            const string skinDir = "data/char/skin/";

            foreach (var rawLine in text.Split('\n'))
            {
                var fname = rawLine.Trim('\r', '\n', ' ').ToLowerInvariant();
                if (fname.Length == 0) continue;

                // Only process .skn entries.
                if (!fname.EndsWith(".skn", StringComparison.Ordinal)) continue;

                // Prepend directory to get the full VFS path.
                // spec: verified on real VFS — skinlist.txt bare filenames live under data/char/skin/.
                var sknPath = skinDir + fname;
                if (!assets.Contains(sknPath)) continue;

                try
                {
                    var sknData = assets.GetRaw(sknPath);
                    if (sknData.IsEmpty) continue;

                    var mesh = SknParser.Parse(sknData);
                    var idB = (int)mesh.IdB;
                    if (idB == 0) continue;

                    // First occurrence of each skin_class wins.
                    // spec: ActormotionEntry — "first entry in skinlist.txt whose id_b == SkinClassId."
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

    // -------------------------------------------------------------------------
    // Ground-snap notification — called from TerrainNode.SectorBecameResident
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Called on the Godot main thread when the terrain sector at (<paramref name="mapX" />,
    ///     <paramref name="mapZ" />) becomes resident (its heightmap is now queryable via
    ///     <see cref="Func{T1,T2,TResult}" /> <see cref="GroundYFunc" />).
    ///     Walks only the actors whose cell-key matches this sector, snaps their Godot Y to the
    ///     real heightmap value, and removes them from the pending list.  All other actors are left
    ///     untouched.  No allocation occurs per-call beyond iterating the list once.
    ///     Wire this in <see cref="RealWorldRenderer" /> after NpcRenderer is created:
    ///     <c>terrainNode.SectorBecameResident += npcRenderer.OnSectorBecameResident;</c>
    ///     spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
    ///     spec: Docs/RE/formats/terrain.md §5.4 — Heights[] direct world-space Y: CONFIRMED.
    ///     spec: Helpers/WorldCoordinates.cs — ToGodot negate Z: CONFIRMED.
    /// </summary>
    /// <param name="mapX">Biased cell X that just became resident.</param>
    /// <param name="mapZ">Biased cell Z that just became resident.</param>
    public void OnSectorBecameResident(int mapX, int mapZ)
    {
        if (TryGroundYFunc is null) return; // no height function wired — nothing to snap
        if (_pendingSnaps.Count == 0) return; // already all grounded

        var snapped = 0;

        // Walk backward so removal by index is safe and avoids shifting.
        // We iterate ALL pending snaps (not just those matching the loaded sector) because
        // a single sector-load may enable height queries for actors scattered across the area —
        // e.g., actors near the edge of two cells that become resident at different times.
        // Using TryGroundYFunc (returns false when not resident) is the correct discriminator;
        // cell-key matching would miss actors whose cell differs from the notified sector.
        for (var i = _pendingSnaps.Count - 1; i >= 0; i--)
        {
            var ps = _pendingSnaps[i];

            // The node may have been freed if the world was reloaded while we were waiting.
            if (!IsInstanceValid(ps.Node))
            {
                _pendingSnaps.RemoveAt(i);
                continue;
            }

            // Attempt to sample the heightmap.  Returns false → cell not yet resident → leave in list.
            // spec: TerrainNode.TryGetGroundHeight — false when cell absent from _cellCache: CONFIRMED.
            if (!TryGroundYFunc(ps.LegacyX, ps.LegacyZ, out var correctY))
                continue;

            // Apply: only correct the Y component; X and Z (Godot) are already right.
            // spec: Helpers/WorldCoordinates.cs — Godot X = legacyX, Godot Z = -legacyZ: CONFIRMED.
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