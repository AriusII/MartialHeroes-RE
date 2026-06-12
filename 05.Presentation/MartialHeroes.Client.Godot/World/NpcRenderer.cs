// World/NpcRenderer.cs
//
// PASSIVE rendering node that spawns NPC / monster static-pose character nodes for a given area.
// It has ZERO game-rule authority: it only visualises spawn data it receives.
//
// Design:
//   The sole public entry point is:
//     PopulateFromArea(RealClientAssets assets, int areaId)
//
//   This method:
//     1. Loads data/map{tag}/mob{tag}.arr  → MobSpawnParser   → monster spawns
//     2. Loads data/map{tag}/npc{tag}.arr  → NpcSpawnParser   → NPC spawns
//     3. Resolves each spawn's model via the confirmed mob_id → skin chain:
//          actormotion.txt: col1=mob_id → col2=skin_class
//          skinlist.txt + SknParser: scan entries, first whose parsed IdB == skin_class
//          skeleton path: data/char/bind/g{skin_class}.bnd  (not used for static build)
//     4. Builds each character STATIC (skeleton=null, clip=null) via SkinnedCharacterBuilder.Build.
//          The skinned path currently explodes the mesh; static mode is the safe fallback until
//          the inverse-bind fix lands.  spec: RealWorldRenderer — "_ = skeleton; _ = clip;
//          intentionally unused until the skinning fix lands."
//     5. Places at Godot (WorldX, groundY(WorldX,WorldZ), -WorldZ).
//          Coordinate negation: spec Helpers/WorldCoordinates.cs — ToGodot: "negate Z".
//          spec: Docs/RE/formats/npc_spawns.md — world_x @4, world_z @8: CONFIRMED.
//     6. Caps to MaxSpawns (default 40) for performance.
//
// Ground-Y injection:
//   GroundYFunc is a Func<float,float,float> that receives (worldX, worldZ) and returns the
//   ground Y in Godot space.  Default implementation returns the flat constant GroundY.
//   The orchestrator (RealWorldRenderer) may wire the terrain height function later.
//
// skinlist.txt scan:
//   skinlist.txt is a plain-text list of BARE FILENAMES (one per line, CP949), e.g. "g200002620.skn".
//   The full VFS path is "data/char/skin/" + filename.
//   We parse each .skn with SknParser and check whether mesh.IdB == skin_class.  First match wins.
//   Verified on real VFS: 1269 bare filenames → 349 unique skin_class→sknPath mappings.
//   spec: MISSION B — "build a Dictionary<int skin_class, string sknPath> ONCE by scanning
//         data/char/skinlist.txt entries that parse with matching IdB; cache it."
//   spec: ActormotionEntry — "id_b == SkinClassId for all 5 spot-checked triples: CONFIRMED."
//
// Threading: PopulateFromArea MUST be called on the Godot main thread.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/formats/npc_spawns.md (NPC .arr format, 28-byte stride).
// spec: MISSION B (mob .arr format, 20-byte stride).
// spec: Docs/RE/formats/mesh.md §.skn header — id_b; §actormotion.txt. CONFIRMED.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive <see cref="Node3D"/> that renders NPC / monster characters as static-pose meshes
/// for a given map area.
///
/// Call <see cref="PopulateFromArea"/> once from the Godot main thread.
/// All previously spawned children are removed on each call so the node can be re-populated
/// when the viewed area changes.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — passive rendering node.
/// </summary>
public sealed partial class NpcRenderer : Node3D
{
    // -------------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maximum number of NPC/mob characters rendered in one <see cref="PopulateFromArea"/> call.
    /// Excess entries are silently skipped.
    /// spec: MISSION B — "Cap to ~40 spawns for perf."
    /// </summary>
    public int MaxSpawns { get; set; } = 40;

    /// <summary>
    /// Visual scale applied to each spawned character node.
    /// 5.0 matches the player character scale in RealWorldRenderer.
    /// spec: RealWorldRenderer.CharacterScale = 5.0f (visual tuning, no spec source).
    /// </summary>
    public float CharacterScale { get; set; } = 5.0f;

    /// <summary>
    /// Flat ground Y used when <see cref="GroundYFunc"/> is not set.
    /// Default 26 f — observed terrain surface height for flat terrain at the origin cell.
    /// </summary>
    public float GroundY { get; set; } = 26f;

    /// <summary>
    /// Optional injectable ground-height function: receives (worldX, worldZ) in legacy
    /// coordinate space and returns the Godot Y for that position.
    /// When null, <see cref="GroundY"/> is used as a flat constant.
    /// spec: MISSION B — "accept an injected groundY function Func&lt;float,float,float&gt;
    ///       (default flat) so the orchestrator can wire terrain height later."
    /// </summary>
    public Func<float, float, float>? GroundYFunc { get; set; }

    /// <summary>
    /// Optional injectable height sampler that returns <see langword="null"/> when the queried
    /// cell is not yet resident (terrain not loaded).  Used by the pending-snap mechanism to
    /// distinguish "real height available" from "fallback only".
    ///
    /// When set, <see cref="OnSectorBecameResident"/> calls this for each pending actor and snaps
    /// only those for which a non-null value is returned.  This correctly handles actors whose
    /// cells may span multiple sectors — they are snapped as soon as their sector arrives,
    /// regardless of which sector triggered the notification.
    ///
    /// Wire alongside <see cref="GroundYFunc"/> in the orchestrator:
    ///   <c>npcRenderer.TryGroundYFunc = (lx, lz, out float y) =&gt; _terrainNode.TryGetGroundHeight(lx, lz, out y);</c>
    ///
    /// spec: TerrainNode.TryGetGroundHeight — returns true when sector is resident: CONFIRMED.
    /// </summary>
    public TryGetHeightDelegate? TryGroundYFunc { get; set; }

    /// <summary>
    /// Delegate type for the nullable terrain height query.
    /// Returns <see langword="true"/> when the cell is resident and <paramref name="height"/>
    /// is a real heightmap value; <see langword="false"/> when the cell is not loaded.
    /// </summary>
    public delegate bool TryGetHeightDelegate(float worldX, float worldZ, out float height);

    // -------------------------------------------------------------------------
    // Pending ground-snap tracking
    //
    // Each spawned actor is recorded here with its legacy world position.  When a
    // terrain sector becomes resident (TerrainNode.SectorBecameResident fires),
    // OnSectorBecameResident() walks only the actors whose cell-key matches the
    // freshly loaded sector and snaps their Godot Y to the heightmap value.
    //
    // This eliminates the fallback-Y race without a per-frame poll:
    //   - No allocation per frame: the list is built once in PopulateFromArea.
    //   - Re-grounding cost is O(actors_in_that_cell) per sector-load event, not O(all actors).
    //   - Actors are removed from the list once grounded so re-grounded actors pay no ongoing cost.
    //
    // Cell-key formula (matches TerrainNode.TryGetGroundHeight):
    //   cellMapX = floor(worldX / 1024) + 10000
    //   cellMapZ = floor(worldZ / 1024) + 10000
    // spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
    // -------------------------------------------------------------------------

    /// <summary>One spawned actor that may still need its ground Y corrected.</summary>
    private readonly record struct PendingSnap(
        Node3D Node,
        float LegacyX,
        float LegacyZ,
        int CellMapX,
        int CellMapZ);

    // All actors still waiting for their cell to become resident.
    // Cleared (and rebuilt) on each PopulateFromArea call.
    private readonly List<PendingSnap> _pendingSnaps = new();

    // Running totals for the summary log.
    private int _totalSpawned;
    private int _totalGrounded;
    private int _totalSkinned; // actors built on the new CPU-LBS skinned path
    private int _totalStaticFallback; // actors that fell back to the static rest-pose path

    // -------------------------------------------------------------------------
    // Skinning tick scheduler (~10 Hz, staggered)
    //
    // 40 actors × CPU LBS every frame is wasteful and the original animates at 10 fps anyway
    // (spec: animation.md §Timing). We therefore drive each skinned mob at ~10 Hz, staggered so
    // they do not all resample on the same frame: each frame we advance the actors in one of
    // SkinTickGroups round-robin "buckets", passing the REAL elapsed seconds for that bucket so
    // clip time still tracks wall-clock (only the resample cadence is coarser). The player path is
    // unaffected — it self-ticks per frame via SkinnedCharacterNode._Process.
    //
    // spec: MISSION — "Update each actor's skinning at ~10 Hz (staggered); keep per-frame
    //       allocations near zero (reuse buffers)."
    // -------------------------------------------------------------------------

    /// <summary>Target skinning resample rate for mobs (Hz). spec: animation.md §Timing — 10 fps.</summary>
    public float SkinTickHz { get; set; } = 10f;

    // Number of round-robin stagger buckets. Spreading 40 actors over 6 buckets keeps at most
    // ~7 LBS deforms per frame instead of 40, and at 60 fps each bucket is revisited every 6
    // frames ≈ 100 ms ≈ the 10 Hz target.
    private const int SkinTickGroups = 6;

    // One externally-driven skinned node plus the per-bucket accumulator it is pumped with.
    private readonly record struct SkinnedActor(SkinnedCharacterNode Node, int Bucket);

    private readonly List<SkinnedActor> _skinnedActors = new();

    // Per-bucket elapsed-time accumulator (reused; no per-frame allocation).
    private readonly double[] _bucketAccum = new double[SkinTickGroups];

    // Round-robin cursor: which bucket is pumped this frame.
    private int _tickCursor;

    // Deterministic per-actor phase randomizer (seeded once; reused). Avoids System.Random churn.
    private uint _phaseRng = 0x9E3779B9u;

    // -------------------------------------------------------------------------
    // Per-assets skin-class → skn-path lookup cache
    // -------------------------------------------------------------------------

    // Cached Dictionary<skinClass, sknVfsPath> built once by scanning skinlist.txt.
    // Invalidated when a different RealClientAssets instance is presented.
    // spec: MISSION B — "build a Dictionary<int skin_class, string sknPath> ONCE … cache it."
    private static Dictionary<int, string>? _skinClassToSknPath;
    private static RealClientAssets? _skinCacheOwner;

    // Cached actormotion lookup: mob_id → ActormotionEntry.
    // spec: ActormotionParser.ParseAsLookup — O(1) lookup by ActorClassId.
    private static Dictionary<int, ActormotionEntry>? _actorMotionLookup;
    private static RealClientAssets? _actorMotionCacheOwner;

    // -------------------------------------------------------------------------
    // Primary entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads both monster (<c>mob{tag}.arr</c>) and NPC (<c>npc{tag}.arr</c>) spawn files
    /// for the given area, resolves each spawn's model via the confirmed mob_id→skin chain,
    /// builds static-pose character nodes, and places them in the scene.
    ///
    /// <para>Must be called on the Godot main thread.</para>
    ///
    /// spec: Docs/RE/formats/npc_spawns.md §Identification — "data/map{NNN}/npc{NNN}.arr".
    /// spec: MISSION B — mob{tag}.arr path, 20-byte records, MobId u16 @0, WorldX f32 @4, WorldZ f32 @8.
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

        string tag = AreaTag(areaId);

        // Ensure the shared lookup tables are built (lazy, per-assets-instance).
        EnsureActorMotionLoaded(assets);
        EnsureSkinClassMapLoaded(assets);

        // STAND-UP PIVOT verification: the +90°-about-Z pivot was validated on the g1 PLAYER rig
        // only. Explicitly probe the spec's canonical MOB rig (g2048) so its pivot decision is
        // derived from ITS OWN rest AABB rather than inherited from g1.
        // spec: MISSION TASK 2 — "check g2048 explicitly and print per-rig pivot decisions."
        // spec: Docs/RE/specs/skinning.md §8(d) — g2048.bnd (45 bones) + g219000630.skn (933 verts).
        ProbeG2048Pivot(assets);

        int spawned = 0;

        // ── Step 1: Monster spawns (mob{tag}.arr, 20-byte records) ─────────────
        // spec: MISSION B — "Load mob{tag}.arr (MobSpawnParser)".
        string mobArrPath = $"data/map{tag}/mob{tag}.arr";
        if (assets.Contains(mobArrPath))
        {
            ReadOnlyMemory<byte> mobData = assets.GetRaw(mobArrPath);
            if (!mobData.IsEmpty)
            {
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

                foreach (MobSpawnRecord rec in mobRecords)
                {
                    if (spawned >= MaxSpawns) break;

                    Node3D? node = TryBuildFromMobId(assets, rec.MobId);
                    if (node is null) continue;

                    // Place at Godot (worldX, groundY, -worldZ).
                    // spec: Helpers/WorldCoordinates.cs — ToGodot: (x,y,z) → (x,y,-z). CONFIRMED.
                    // spec: MISSION B — "Place at Godot (WorldX, groundY, -WorldZ)."
                    float gy = ResolveGroundY(rec.WorldX, rec.WorldZ);
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
        }
        else
        {
            GD.Print($"[NpcRenderer] No mob{tag}.arr for area {areaId} — skipping monster spawns.");
        }

        // ── Step 2: NPC spawns (npc{tag}.arr, 28-byte records) ─────────────────
        // spec: Docs/RE/formats/npc_spawns.md §Identification — path pattern. CONFIRMED.
        // spec: MISSION B — "Load npc{tag}.arr (NpcSpawnParser)".
        string npcArrPath = $"data/map{tag}/npc{tag}.arr";
        if (assets.Contains(npcArrPath))
        {
            ReadOnlyMemory<byte> npcData = assets.GetRaw(npcArrPath);
            if (!npcData.IsEmpty)
            {
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

                foreach (NpcSpawnRecord rec in npcArray.Records)
                {
                    if (spawned >= MaxSpawns) break;
                    if (rec.MobId == 0) continue;

                    // NPC spawns also go through the actormotion chain first; if that fails we
                    // fall back to the old best-effort probe (data/char/skin/g{MobId}.skn).
                    Node3D? node = TryBuildFromMobId(assets, rec.MobId)
                                   ?? TryBuildDirectSkinProbe(assets, rec.MobId);
                    if (node is null) continue;

                    // spec: Docs/RE/formats/npc_spawns.md — world_x f32 @4, world_z f32 @8: CONFIRMED.
                    float gy = ResolveGroundY(rec.WorldX, rec.WorldZ);
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
        }
        else
        {
            GD.Print($"[NpcRenderer] No npc{tag}.arr for area {areaId} — skipping NPC spawns.");
        }

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

    /// <summary>
    /// Prints the rest AABB of the first skinned mob (in the actor's local pivot space) so a headless
    /// run can confirm the deform is plausible (human-sized, finite) and not exploded (huge / NaN).
    /// </summary>
    private void PrintMobAabbSanity()
    {
        if (_skinnedActors.Count == 0)
        {
            GD.Print("[NpcRenderer] AABB sanity: no skinned mobs (all static-fallback).");
            return;
        }

        SkinnedCharacterNode node = _skinnedActors[0].Node;
        if (!global::Godot.GodotObject.IsInstanceValid(node)) return;

        Aabb a = node.GetMeshAabb();
        Vector3 sz = a.Size;
        bool finite = !(float.IsNaN(sz.X) || float.IsNaN(sz.Y) || float.IsNaN(sz.Z) ||
                        float.IsInfinity(sz.X) || float.IsInfinity(sz.Y) || float.IsInfinity(sz.Z));
        bool plausible = finite && sz.Length() > 0.001f && sz.Length() < 1e4f;
        GD.Print($"[NpcRenderer] AABB sanity (first skinned mob '{node.GetParent()?.Name}'): " +
                 $"size=({sz.X:F2},{sz.Y:F2},{sz.Z:F2}) finite={finite} " +
                 $"plausible={(plausible ? "YES" : "NO — EXPLOSION?")}.");
    }

    /// <summary>
    /// One-time explicit probe of the spec's canonical MOB rig (g2048) so its stand-up pivot is
    /// derived from its own rest AABB and printed, independently of g1. Builds the trio
    /// (g2048.bnd + g219000630.skn + g182006900.mot) off-tree, logs the pivot decision via the
    /// builder's diagnostics, then frees the temporary node. Skips silently if the assets are
    /// absent (the trio is user-supplied and may not be present in every install).
    ///
    /// spec: MISSION TASK 2 — "derive/verify the pivot empirically for mob rigs; check g2048
    ///       explicitly and print per-rig pivot decisions."
    /// spec: Docs/RE/specs/skinning.md §8(d) — mob trio paths.
    /// </summary>
    private static bool _g2048Probed;

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
            SkinnedMesh mesh = SknParser.Parse(assets.GetRaw(sknPath));
            Skeleton skel = BndParser.Parse(assets.GetRaw(bndPath));
            AnimationClip? clip = assets.Contains(motPath)
                ? AnimationParser.Parse(assets.GetRaw(motPath))
                : null;

            GD.Print($"[NpcRenderer] g2048 pivot probe: skn={mesh.Positions.Length}v " +
                     $"bnd={skel.Bones.Length}bones mot={(clip is null ? "none" : $"{clip.FrameCount}f")}.");

            // Build off-tree (externalDrive so it never self-ticks) purely to trigger the per-rig
            // pivot derivation + diagnostics, then free it. The builder prints the pivot decision.
            Node3D probe = SkinnedCharacterBuilder.Build(
                mesh, skel, clip, albedo: null,
                externalDrive: true, startPhaseSeconds: 0f,
                out _, debugLabel: "g2048(mob-canonical)");
            probe.QueueFree();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] g2048 pivot probe failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // ~10 Hz staggered skinning scheduler
    // -------------------------------------------------------------------------

    /// <summary>
    /// Drives the externally-driven mob skinning at ~10 Hz, staggered across
    /// <see cref="SkinTickGroups"/> round-robin buckets so they do not all resample on the same
    /// frame. Each frame advances exactly one bucket, passing the real elapsed seconds accumulated
    /// for that bucket (so clip time still tracks wall-clock while the resample cadence stays
    /// coarse). Allocation-free: it walks the pre-built list and pumps each node's reused buffers.
    ///
    /// The player's SkinnedCharacterNode self-ticks per frame (not driven here) — this loop only
    /// touches the mob nodes registered in <see cref="_skinnedActors"/>.
    ///
    /// spec: MISSION — "Update each actor's skinning at ~10 Hz (staggered); player stays per-frame;
    ///       keep per-frame allocations near zero (reuse buffers)."
    /// </summary>
    public override void _Process(double delta)
    {
        int count = _skinnedActors.Count;
        if (count == 0) return;

        // Accumulate elapsed time into every bucket, then advance only the one the cursor selects.
        for (int b = 0; b < SkinTickGroups; b++)
            _bucketAccum[b] += delta;

        int bucket = _tickCursor;
        _tickCursor = (_tickCursor + 1) % SkinTickGroups;

        float dt = (float)_bucketAccum[bucket];
        _bucketAccum[bucket] = 0.0;
        if (dt <= 0f) return;

        for (int i = 0; i < count; i++)
        {
            SkinnedActor sa = _skinnedActors[i];
            if (sa.Bucket != bucket) continue;
            if (!global::Godot.GodotObject.IsInstanceValid(sa.Node)) continue;
            sa.Node.Tick(dt);
        }
    }

    // -------------------------------------------------------------------------
    // Model resolution — mob_id → skin chain
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a model for the given mob_id via the confirmed chain and builds a SKINNED +
    /// idle-animated CPU-LBS node (the same pipeline the player uses), falling back to a static
    /// rest-pose node when any piece is missing:
    ///   actormotion.txt col1==mob_id → col2=skin_class (and col15 = idle .mot id)
    ///   skinlist.txt scan → first .skn whose parsed IdB == skin_class
    ///   data/char/bind/g{skin_class}.bnd → skeleton (.bnd)
    ///   data/char/mot/g{idle}.mot → idle clip (.mot)
    ///   CharacterTextureResolver → albedo texture
    ///   SkinnedCharacterBuilder.Build (skinned when bnd present; static otherwise)
    ///
    /// Returns null when even the .skn cannot be resolved; never throws.
    ///
    /// spec: MISSION — "resolve each actor's skin/bind/idle-mot via the existing actormotion chain,
    ///       build the skinned node, fall back to the static path when any piece is missing."
    /// spec: Docs/RE/formats/animation.md §actormotion.txt — col2=SkinClassId, col15=idle motion id.
    /// spec: Docs/RE/specs/skinning.md §8(d) — mob trio g2048.bnd + g219000630.skn + g182006900.mot.
    /// </summary>
    private Node3D? TryBuildFromMobId(RealClientAssets assets, ushort mobId)
    {
        if (_actorMotionLookup is null || _skinClassToSknPath is null)
            return null;

        // Step 1: actormotion.txt lookup: mob_id → skin_class + idle motion id.
        // spec: ActormotionParser — col1=actor_class_id, col2=skin_class_id, col15=idle motion.
        if (!_actorMotionLookup.TryGetValue(mobId, out ActormotionEntry? entry))
            return null; // mob_id not in actormotion.txt — silently skip

        int skinClass = entry.SkinClassId;

        // Step 2: skinlist.txt scan → .skn path.
        // spec: MISSION — "body .skn = the entry in skinlist.txt whose parsed .skn IdB == skin_class."
        if (!_skinClassToSknPath.TryGetValue(skinClass, out string? sknPath))
            return null; // no .skn with matching IdB — silently skip

        // col15 = idle motion id (MotionIds[0]). Zero = empty slot.
        // spec: Docs/RE/formats/animation.md §col15 — idle_motion_id = motion_ids_a[0]. CONFIRMED.
        int idleMotId = entry.MotionIds.Length > 0 ? entry.MotionIds[0] : 0;

        return TryBuildActorNode(assets, sknPath, skinClass, idleMotId,
            $"mob_id={mobId} skin_class={skinClass}");
    }

    /// <summary>
    /// Fallback path: probe <c>data/char/skin/g{mobId}.skn</c> directly (NPCs not in actormotion.txt).
    /// Builds skinned if a <c>.bnd</c> can be derived from the parsed mesh's IdB; static otherwise.
    /// Returns null (silent skip) when the probe path does not exist in the VFS.
    /// </summary>
    private Node3D? TryBuildDirectSkinProbe(RealClientAssets assets, ushort mobId)
    {
        string sknPath = $"data/char/skin/g{mobId}.skn";
        if (!assets.Contains(sknPath))
            return null;
        // No actormotion row → no skin_class hint, no idle id; the builder derives the .bnd from
        // the parsed mesh IdB and renders a static rest pose (skeleton may still resolve).
        return TryBuildActorNode(assets, sknPath, skinClass: -1, idleMotId: 0,
            $"direct-probe mob_id={mobId}");
    }

    /// <summary>
    /// Parses the .skn, resolves its texture, loads the .bnd skeleton and idle .mot clip (when
    /// available), and builds a skinned CPU-LBS node via <see cref="SkinnedCharacterBuilder"/>.
    /// Registers the resulting skinned node with the ~10 Hz stagger scheduler. When the skeleton
    /// is missing, the builder degrades to a static rest pose (counted as a static fallback).
    /// </summary>
    /// <param name="skinClass">SkinClassId from actormotion (the .bnd g-id), or -1 to derive from mesh IdB.</param>
    /// <param name="idleMotId">Idle .mot id_a (col15), or 0 for none.</param>
    private Node3D? TryBuildActorNode(
        RealClientAssets assets, string sknPath, int skinClass, int idleMotId, string debugLabel)
    {
        SkinnedMesh mesh;
        try
        {
            ReadOnlyMemory<byte> sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[NpcRenderer] .skn empty in VFS: {sknPath} ({debugLabel})");
                return null;
            }

            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SknParser.Parse failed '{sknPath}' ({debugLabel}): {ex.Message}");
            return null;
        }

        // Resolve texture — CharacterTextureResolver handles skin.txt + derivation fallback.
        // spec: World/CharacterTextureResolver.cs — Resolve(assets, mesh). CONFIRMED.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] Texture resolve failed for '{sknPath}': {ex.Message}");
            // Albedo stays null — neutral material will be applied by builder.
        }

        // Resolve the .bnd skeleton. The skin_class IS the .bnd g-id (== mesh IdB); fall back to
        // the mesh's own IdB when no actormotion row gave a skin_class.
        // spec: Docs/RE/formats/mesh.md §.bnd — actor_id == skin .skn id_b; path g{id}.bnd. CONFIRMED.
        int bndId = skinClass > 0 ? skinClass : (int)mesh.IdB;
        Skeleton? skeleton = TryLoadSkeleton(assets, bndId, debugLabel);

        // Resolve the idle .mot clip. Only when we have a skeleton to drive it.
        // spec: Docs/RE/formats/animation.md §col15 — idle motion id → data/char/mot/g{id}.mot.
        AnimationClip? clip = (skeleton is not null && idleMotId > 0)
            ? TryLoadAnimation(assets, idleMotId, debugLabel)
            : null;

        try
        {
            // Skinned when a skeleton resolved; static rest pose otherwise. Mobs are externally
            // driven (the ~10 Hz scheduler pumps them) with a randomized clip start phase so the
            // town does not animate in lockstep.
            // spec: MISSION — skinned mob path, ~10 Hz staggered, randomized clip phase.
            float startPhase = clip is not null ? NextPhase(clip.FrameCount * SkinningMath.MotSecondsPerFrame) : 0f;
            Node3D root = SkinnedCharacterBuilder.Build(
                mesh, skeleton, clip, albedo,
                externalDrive: skeleton is not null,
                startPhaseSeconds: startPhase,
                out SkinnedCharacterNode? lbs,
                debugLabel);

            if (lbs is not null)
            {
                // Skinned: register with the stagger scheduler in a round-robin bucket.
                int bucket = _skinnedActors.Count % SkinTickGroups;
                _skinnedActors.Add(new SkinnedActor(lbs, bucket));
                _totalSkinned++;
            }
            else
            {
                _totalStaticFallback++;
            }

            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SkinnedCharacterBuilder.Build failed '{sknPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads the <c>.bnd</c> skeleton at <c>data/char/bind/g{bndId}.bnd</c>, or returns null
    /// (→ static rest pose) when absent / unparseable. Never throws.
    /// spec: Docs/RE/formats/mesh.md §.bnd — path g{id}.bnd; id == skin id_b == skin_class. CONFIRMED.
    /// </summary>
    private static Skeleton? TryLoadSkeleton(RealClientAssets assets, int bndId, string debugLabel)
    {
        if (bndId <= 0) return null;
        string bndPath = $"data/char/bind/g{bndId}.bnd";
        if (!assets.Contains(bndPath)) return null;
        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(bndPath);
            if (data.IsEmpty) return null;
            return BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] .bnd load failed '{bndPath}' ({debugLabel}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads the idle <c>.mot</c> clip at <c>data/char/mot/g{idleMotId}.mot</c>, or returns null
    /// (→ rest pose, no animation) when absent / unparseable. Never throws.
    /// spec: Docs/RE/formats/animation.md §col15 — idle motion id → data/char/mot/g{id}.mot. CONFIRMED.
    /// </summary>
    private static AnimationClip? TryLoadAnimation(RealClientAssets assets, int idleMotId, string debugLabel)
    {
        string motPath = $"data/char/mot/g{idleMotId}.mot";
        if (!assets.Contains(motPath)) return null;
        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(motPath);
            if (data.IsEmpty) return null;
            return AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] .mot load failed '{motPath}' ({debugLabel}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Deterministic pseudo-random clip start phase in [0, duration). Uses a cheap xorshift so it
    /// allocates nothing and stays reproducible across runs (no System.Random per actor).
    /// spec: MISSION — "randomize each actor's clip start phase so the town doesn't move in lockstep."
    /// </summary>
    private float NextPhase(float durationSeconds)
    {
        if (durationSeconds <= 0f) return 0f;
        // xorshift32
        uint x = _phaseRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _phaseRng = x;
        float u = (x & 0xFFFFFF) / (float)0x1000000; // [0,1)
        return u * durationSeconds;
    }

    // -------------------------------------------------------------------------
    // Lazy lookup-table loading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the actormotion lookup (mob_id → <see cref="ActormotionEntry"/>) from
    /// <c>data/char/actormotion.txt</c> on first call and caches it.
    ///
    /// spec: ActormotionParser.ParseAsLookup — keyed by ActorClassId. CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §actormotion.txt. CONFIRMED.
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
            ReadOnlyMemory<byte> raw = assets.GetRaw(tablePath);
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
    /// Builds the skin-class → skn-path dictionary by scanning <c>data/char/skinlist.txt</c>
    /// on first call and caches it.
    ///
    /// skinlist.txt is a plain-text list of VFS paths, one per line (CP949).  For each non-empty
    /// line that names a <c>.skn</c> file, we parse the file header to read <c>IdB</c> and map
    /// <c>skinClass → sknPath</c>.  Only the first occurrence of each skin_class is retained
    /// (mirrors the "first entry" guarantee in <see cref="ActormotionEntry"/>).
    ///
    /// Parsing is done header-only (SknParser reads the full file but allocates only once per
    /// unique path); this is acceptable for a one-time startup scan of ~900 entries.
    ///
    /// spec: MISSION B — "build a Dictionary&lt;int skin_class, string sknPath&gt; ONCE by
    ///       scanning data/char/skinlist.txt entries that parse with matching IdB; cache it."
    /// spec: ActormotionEntry — "id_b == SkinClassId for all 5 spot-checked triples: CONFIRMED."
    /// spec: Docs/RE/formats/mesh.md §.skn header — "id_b at +4". CONFIRMED.
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
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            ReadOnlyMemory<byte> raw = assets.GetRaw(listPath);
            string text = System.Text.Encoding.GetEncoding(949).GetString(raw.Span);

            var map = new Dictionary<int, string>(1024);
            int parsedCount = 0;
            int errorCount = 0;

            // skinlist.txt lines are bare filenames (e.g. "g200002620.skn"), NOT full VFS paths.
            // The full VFS path is "data/char/skin/" + filename.
            // Verified on real VFS: all 1269 lines are bare filenames present under data/char/skin/.
            const string skinDir = "data/char/skin/";

            foreach (string rawLine in text.Split('\n'))
            {
                string fname = rawLine.Trim('\r', '\n', ' ').ToLowerInvariant();
                if (fname.Length == 0) continue;

                // Only process .skn entries.
                if (!fname.EndsWith(".skn", StringComparison.Ordinal)) continue;

                // Prepend directory to get the full VFS path.
                // spec: verified on real VFS — skinlist.txt bare filenames live under data/char/skin/.
                string sknPath = skinDir + fname;
                if (!assets.Contains(sknPath)) continue;

                try
                {
                    ReadOnlyMemory<byte> sknData = assets.GetRaw(sknPath);
                    if (sknData.IsEmpty) continue;

                    SkinnedMesh mesh = SknParser.Parse(sknData);
                    int idB = (int)mesh.IdB;
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
    /// Called on the Godot main thread when the terrain sector at (<paramref name="mapX"/>,
    /// <paramref name="mapZ"/>) becomes resident (its heightmap is now queryable via
    /// <see cref="Func{T1,T2,TResult}"/> <see cref="GroundYFunc"/>).
    ///
    /// Walks only the actors whose cell-key matches this sector, snaps their Godot Y to the
    /// real heightmap value, and removes them from the pending list.  All other actors are left
    /// untouched.  No allocation occurs per-call beyond iterating the list once.
    ///
    /// Wire this in <see cref="RealWorldRenderer"/> after NpcRenderer is created:
    ///   <c>terrainNode.SectorBecameResident += npcRenderer.OnSectorBecameResident;</c>
    ///
    /// spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
    /// spec: Docs/RE/formats/terrain.md §5.4 — Heights[] direct world-space Y: CONFIRMED.
    /// spec: Helpers/WorldCoordinates.cs — ToGodot negate Z: CONFIRMED.
    /// </summary>
    /// <param name="mapX">Biased cell X that just became resident.</param>
    /// <param name="mapZ">Biased cell Z that just became resident.</param>
    public void OnSectorBecameResident(int mapX, int mapZ)
    {
        if (TryGroundYFunc is null) return; // no height function wired — nothing to snap
        if (_pendingSnaps.Count == 0) return; // already all grounded

        int snapped = 0;

        // Walk backward so removal by index is safe and avoids shifting.
        // We iterate ALL pending snaps (not just those matching the loaded sector) because
        // a single sector-load may enable height queries for actors scattered across the area —
        // e.g., actors near the edge of two cells that become resident at different times.
        // Using TryGroundYFunc (returns false when not resident) is the correct discriminator;
        // cell-key matching would miss actors whose cell differs from the notified sector.
        for (int i = _pendingSnaps.Count - 1; i >= 0; i--)
        {
            PendingSnap ps = _pendingSnaps[i];

            // The node may have been freed if the world was reloaded while we were waiting.
            if (!global::Godot.GodotObject.IsInstanceValid(ps.Node))
            {
                _pendingSnaps.RemoveAt(i);
                continue;
            }

            // Attempt to sample the heightmap.  Returns false → cell not yet resident → leave in list.
            // spec: TerrainNode.TryGetGroundHeight — false when cell absent from _cellCache: CONFIRMED.
            if (!TryGroundYFunc(ps.LegacyX, ps.LegacyZ, out float correctY))
                continue;

            // Apply: only correct the Y component; X and Z (Godot) are already right.
            // spec: Helpers/WorldCoordinates.cs — Godot X = legacyX, Godot Z = -legacyZ: CONFIRMED.
            Vector3 pos = ps.Node.Position;
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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Tries to snap <paramref name="node"/> to the real terrain height immediately (if the sector
    /// is already resident via <see cref="TryGroundYFunc"/>), or adds it to the pending list for
    /// deferred snapping when its sector arrives via <see cref="OnSectorBecameResident"/>.
    ///
    /// Cell-key computation for the pending record matches <c>TerrainNode.TryGetGroundHeight</c>
    /// for diagnostic reference only; the actual snap uses <see cref="TryGroundYFunc"/> directly.
    ///   cellMapX = floor(worldX / 1024) + 10000
    ///   cellMapZ = floor(worldZ / 1024) + 10000
    /// spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
    /// </summary>
    private void RegisterPendingSnap(Node3D node, float legacyX, float legacyZ)
    {
        // Try an immediate snap if TryGroundYFunc is wired (e.g. sector already resident).
        if (TryGroundYFunc is not null && TryGroundYFunc(legacyX, legacyZ, out float immediateY))
        {
            Vector3 pos = node.Position;
            pos.Y = immediateY;
            node.Position = pos;
            _totalGrounded++;
            return; // grounded immediately — no need to add to pending list
        }

        // Sector not yet resident — queue for deferred snap.
        // Compute the cell key for diagnostics (not for matching — TryGroundYFunc handles that).
        // spec: terrain.md §1.4 — cellMapX = floor(worldX / 1024) + 10000: CONFIRMED.
        int cellMapX = (int)Math.Floor(legacyX / 1024.0) + 10000;
        int cellMapZ = (int)Math.Floor(legacyZ / 1024.0) + 10000;
        _pendingSnaps.Add(new PendingSnap(node, legacyX, legacyZ, cellMapX, cellMapZ));
    }

    /// <summary>
    /// Resolves Godot Y for a spawn position, using the injected <see cref="GroundYFunc"/>
    /// if set, otherwise returning the flat <see cref="GroundY"/> constant.
    ///
    /// spec: MISSION B — "accept an injected groundY function Func&lt;float,float,float&gt;
    ///       (default flat) so the orchestrator can wire terrain height later."
    /// </summary>
    private float ResolveGroundY(float worldX, float worldZ)
        => GroundYFunc is not null ? GroundYFunc(worldX, worldZ) : GroundY;

    /// <summary>Removes all currently spawned character children.</summary>
    private void ClearChildren()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// Converts an integer area id to the three-digit tag used in VFS paths.
    /// spec: Docs/RE/formats/terrain.md §1.1 — d0=areaId/100, d1=(areaId/10)%10, d2=areaId%10. CONFIRMED.
    /// </summary>
    private static string AreaTag(int areaId)
    {
        int d0 = areaId / 100;
        int d1 = (areaId / 10) % 10;
        int d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }
}