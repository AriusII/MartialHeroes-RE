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
using MartialHeroes.Assets.Parsers.Character.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Passive <see cref="Node3D" /> that renders NPC / monster characters as static-pose meshes
///     for a given map area.
///     Call <see cref="PopulateFromArea" /> once from the Godot main thread.
///     All previously spawned children are removed on each call so the node can be re-populated
///     when the viewed area changes.
///     spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — passive rendering node.
/// </summary>
public sealed partial class NpcRenderer : Node3D
{
    /// <summary>
    ///     Delegate type for the nullable terrain height query.
    ///     Returns <see langword="true" /> when the cell is resident and <paramref name="height" />
    ///     is a real heightmap value; <see langword="false" /> when the cell is not loaded.
    /// </summary>
    public delegate bool TryGetHeightDelegate(float worldX, float worldZ, out float height);

    // Number of round-robin stagger buckets. Spreading 40 actors over 6 buckets keeps at most
    // ~7 LBS deforms per frame instead of 40, and at 60 fps each bucket is revisited every 6
    // frames ≈ 100 ms ≈ the 10 Hz target.
    private const int SkinTickGroups = 6;

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

    // Per-bucket elapsed-time accumulator (reused; no per-frame allocation).
    private readonly double[] _bucketAccum = new double[SkinTickGroups];

    // All actors still waiting for their cell to become resident.
    // Cleared (and rebuilt) on each PopulateFromArea call.
    private readonly List<PendingSnap> _pendingSnaps = new();

    private readonly List<SkinnedActor> _skinnedActors = new();

    // Deterministic per-actor phase randomizer (seeded once; reused). Avoids System.Random churn.
    private uint _phaseRng = 0x9E3779B9u;

    // Round-robin cursor: which bucket is pumped this frame.
    private int _tickCursor;
    private int _totalGrounded;
    private int _totalSkinned; // actors built on the new CPU-LBS skinned path

    // Running totals for the summary log.
    private int _totalSpawned;

    private int _totalStaticFallback; // actors that fell back to the static rest-pose path
    // -------------------------------------------------------------------------
    // Configuration
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Maximum number of NPC/mob characters rendered in one <see cref="PopulateFromArea" /> call.
    ///     Excess entries are silently skipped.
    ///     spec: MISSION B — "Cap to ~40 spawns for perf."
    /// </summary>
    public int MaxSpawns { get; set; } = 40;

    /// <summary>
    ///     Visual scale applied to each spawned character node.
    ///     5.0 matches the player character scale in RealWorldRenderer.
    ///     spec: RealWorldRenderer.CharacterScale = 5.0f (visual tuning, no spec source).
    /// </summary>
    public float CharacterScale { get; set; } = 5.0f;

    /// <summary>
    ///     Flat ground Y used when <see cref="GroundYFunc" /> is not set.
    ///     Default 26 f — observed terrain surface height for flat terrain at the origin cell.
    /// </summary>
    public float GroundY { get; set; } = 26f;

    /// <summary>
    ///     Optional injectable ground-height function: receives (worldX, worldZ) in legacy
    ///     coordinate space and returns the Godot Y for that position.
    ///     When null, <see cref="GroundY" /> is used as a flat constant.
    ///     spec: MISSION B — "accept an injected groundY function Func&lt;float,float,float&gt;
    ///     (default flat) so the orchestrator can wire terrain height later."
    /// </summary>
    public Func<float, float, float>? GroundYFunc { get; set; }

    /// <summary>
    ///     Optional injectable height sampler that returns <see langword="null" /> when the queried
    ///     cell is not yet resident (terrain not loaded).  Used by the pending-snap mechanism to
    ///     distinguish "real height available" from "fallback only".
    ///     When set, <see cref="OnSectorBecameResident" /> calls this for each pending actor and snaps
    ///     only those for which a non-null value is returned.  This correctly handles actors whose
    ///     cells may span multiple sectors — they are snapped as soon as their sector arrives,
    ///     regardless of which sector triggered the notification.
    ///     Wire alongside <see cref="GroundYFunc" /> in the orchestrator:
    ///     <c>npcRenderer.TryGroundYFunc = (lx, lz, out float y) =&gt; _terrainNode.TryGetGroundHeight(lx, lz, out y);</c>
    ///     spec: TerrainNode.TryGetGroundHeight — returns true when sector is resident: CONFIRMED.
    /// </summary>
    public TryGetHeightDelegate? TryGroundYFunc { get; set; }

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

    // -------------------------------------------------------------------------
    // ~10 Hz staggered skinning scheduler  (frame lifecycle)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Drives the externally-driven mob skinning at ~10 Hz, staggered across
    ///     <see cref="SkinTickGroups" /> round-robin buckets so they do not all resample on the same
    ///     frame. Each frame advances exactly one bucket, passing the real elapsed seconds accumulated
    ///     for that bucket (so clip time still tracks wall-clock while the resample cadence stays
    ///     coarse). Allocation-free: it walks the pre-built list and pumps each node's reused buffers.
    ///     The player's SkinnedCharacterNode self-ticks per frame (not driven here) — this loop only
    ///     touches the mob nodes registered in <see cref="_skinnedActors" />.
    ///     spec: MISSION — "Update each actor's skinning at ~10 Hz (staggered); player stays per-frame;
    ///     keep per-frame allocations near zero (reuse buffers)."
    /// </summary>
    public override void _Process(double delta)
    {
        var count = _skinnedActors.Count;
        if (count == 0) return;

        // Accumulate elapsed time into every bucket, then advance only the one the cursor selects.
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

    // -------------------------------------------------------------------------
    // Helpers shared across partials
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Resolves Godot Y for a spawn position, using the injected <see cref="GroundYFunc" />
    ///     if set, otherwise returning the flat <see cref="GroundY" /> constant.
    ///     spec: MISSION B — "accept an injected groundY function Func&lt;float,float,float&gt;
    ///     (default flat) so the orchestrator can wire terrain height later."
    /// </summary>
    private float ResolveGroundY(float worldX, float worldZ)
    {
        return GroundYFunc is not null ? GroundYFunc(worldX, worldZ) : GroundY;
    }

    /// <summary>Removes all currently spawned character children.</summary>
    private void ClearChildren()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    ///     Converts an integer area id to the three-digit tag used in VFS paths.
    ///     spec: Docs/RE/formats/terrain.md §1.1 — d0=areaId/100, d1=(areaId/10)%10, d2=areaId%10. CONFIRMED.
    /// </summary>
    private static string AreaTag(int areaId)
    {
        var d0 = areaId / 100;
        var d1 = areaId / 10 % 10;
        var d2 = areaId % 10;
        return $"{d0}{d1}{d2}";
    }

    /// <summary>
    ///     Tries to snap <paramref name="node" /> to the real terrain height immediately (if the sector
    ///     is already resident via <see cref="TryGroundYFunc" />), or adds it to the pending list for
    ///     deferred snapping when its sector arrives via <see cref="OnSectorBecameResident" />.
    ///     Cell-key computation for the pending record matches <c>TerrainNode.TryGetGroundHeight</c>
    ///     for diagnostic reference only; the actual snap uses <see cref="TryGroundYFunc" /> directly.
    ///     cellMapX = floor(worldX / 1024) + 10000
    ///     cellMapZ = floor(worldZ / 1024) + 10000
    ///     spec: Docs/RE/formats/terrain.md §1.4 — origin bias 10000, cell size 1024 wu: CONFIRMED.
    /// </summary>
    private void RegisterPendingSnap(Node3D node, float legacyX, float legacyZ)
    {
        // Try an immediate snap if TryGroundYFunc is wired (e.g. sector already resident).
        if (TryGroundYFunc is not null && TryGroundYFunc(legacyX, legacyZ, out var immediateY))
        {
            var pos = node.Position;
            pos.Y = immediateY;
            node.Position = pos;
            _totalGrounded++;
            return; // grounded immediately — no need to add to pending list
        }

        // Sector not yet resident — queue for deferred snap.
        // Compute the cell key for diagnostics (not for matching — TryGroundYFunc handles that).
        // spec: terrain.md §1.4 — cellMapX = floor(worldX / 1024) + 10000: CONFIRMED.
        var cellMapX = (int)Math.Floor(legacyX / 1024.0) + 10000;
        var cellMapZ = (int)Math.Floor(legacyZ / 1024.0) + 10000;
        _pendingSnaps.Add(new PendingSnap(node, legacyX, legacyZ, cellMapX, cellMapZ));
    }

    /// <summary>
    ///     Prints the rest AABB of the first skinned mob (in the actor's local pivot space) so a headless
    ///     run can confirm the deform is plausible (human-sized, finite) and not exploded (huge / NaN).
    /// </summary>
    private void PrintMobAabbSanity()
    {
        if (_skinnedActors.Count == 0)
        {
            GD.Print("[NpcRenderer] AABB sanity: no skinned mobs (all static-fallback).");
            return;
        }

        var node = _skinnedActors[0].Node;
        if (!IsInstanceValid(node)) return;

        var a = node.GetMeshAabb();
        var sz = a.Size;
        var finite = !(float.IsNaN(sz.X) || float.IsNaN(sz.Y) || float.IsNaN(sz.Z) ||
                       float.IsInfinity(sz.X) || float.IsInfinity(sz.Y) || float.IsInfinity(sz.Z));
        var plausible = finite && sz.Length() > 0.001f && sz.Length() < 1e4f;
        GD.Print($"[NpcRenderer] AABB sanity (first skinned mob '{node.GetParent()?.Name}'): " +
                 $"size=({sz.X:F2},{sz.Y:F2},{sz.Z:F2}) finite={finite} " +
                 $"plausible={(plausible ? "YES" : "NO — EXPLOSION?")}.");
    }

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

    // One externally-driven skinned node plus the per-bucket accumulator it is pumped with.
    private readonly record struct SkinnedActor(SkinnedCharacterNode Node, int Bucket);
}