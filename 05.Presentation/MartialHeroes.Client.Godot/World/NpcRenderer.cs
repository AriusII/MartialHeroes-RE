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
        // Remove previously spawned children.
        ClearChildren();

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
                }
            }
        }
        else
        {
            GD.Print($"[NpcRenderer] No npc{tag}.arr for area {areaId} — skipping NPC spawns.");
        }

        GD.Print($"[NpcRenderer] Area {areaId}: {spawned} total NPC/mob characters spawned (cap={MaxSpawns}).");
    }

    // -------------------------------------------------------------------------
    // Model resolution — mob_id → skin chain
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a model for the given mob_id via the confirmed chain:
    ///   actormotion.txt col1==mob_id → col2=skin_class
    ///   skinlist.txt scan → first .skn whose parsed IdB == skin_class
    ///   CharacterTextureResolver → albedo texture
    ///   SkinnedCharacterBuilder.Build (static: skeleton=null, clip=null)
    ///
    /// Returns null when any step fails; never throws.
    ///
    /// spec: MISSION B — "Resolve each spawn's model: actormotion.txt row col1==mob_id → col2=skin_class;
    ///       skeleton=data/char/bind/g{skin_class}.bnd; body .skn = skinlist.txt whose IdB==skin_class."
    /// spec: ActormotionEntry — ActorClassId=col1, SkinClassId=col2. CONFIRMED.
    /// </summary>
    private Node3D? TryBuildFromMobId(RealClientAssets assets, ushort mobId)
    {
        if (_actorMotionLookup is null || _skinClassToSknPath is null)
            return null;

        // Step 1: actormotion.txt lookup: mob_id → skin_class.
        // spec: ActormotionParser — col1=actor_class_id, col2=skin_class_id. CONFIRMED.
        // spec: MISSION B — "actormotion.txt row col1==mob_id gives col2=skin_class."
        if (!_actorMotionLookup.TryGetValue(mobId, out ActormotionEntry? entry))
            return null; // mob_id not in actormotion.txt — silently skip

        int skinClass = entry.SkinClassId;

        // Step 2: skinlist.txt scan → .skn path.
        // spec: MISSION B — "body .skn = the entry in skinlist.txt whose parsed .skn IdB == skin_class."
        if (!_skinClassToSknPath.TryGetValue(skinClass, out string? sknPath))
            return null; // no .skn with matching IdB — silently skip

        // Step 3: Parse .skn and build a static-pose node.
        // spec: MISSION B — "build STATIC (skeleton=null, clip=null) for now."
        return TryBuildStaticNode(assets, sknPath, $"mob_id={mobId} skin_class={skinClass}");
    }

    /// <summary>
    /// Fallback path: probe <c>data/char/skin/g{mobId}.skn</c> directly.
    /// Used for NPC entries that are not in actormotion.txt.
    /// Returns null (silent skip) when the probe path does not exist in the VFS.
    ///
    /// spec: Old NpcRenderer comment — "MobId → skn path is NOT a confirmed mapping;
    ///       this uses MobId as a BEST-EFFORT skin probe."
    /// </summary>
    private Node3D? TryBuildDirectSkinProbe(RealClientAssets assets, ushort mobId)
    {
        string sknPath = $"data/char/skin/g{mobId}.skn";
        if (!assets.Contains(sknPath))
            return null;
        return TryBuildStaticNode(assets, sknPath, $"direct-probe mob_id={mobId}");
    }

    /// <summary>
    /// Parses the .skn at <paramref name="sknPath"/>, resolves its albedo texture, and calls
    /// <see cref="SkinnedCharacterBuilder.Build"/> with skeleton=null and clip=null (static pose).
    ///
    /// spec: MISSION B — "pass the resolved albedo texture; build STATIC (skeleton=null, clip=null)."
    /// spec: SkinnedCharacterBuilder — "skeleton=null → static ArrayMesh (no Skeleton3D child)."
    /// </summary>
    private static Node3D? TryBuildStaticNode(RealClientAssets assets, string sknPath, string debugLabel)
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

        try
        {
            // Static build: skeleton=null, clip=null.
            // The skinned path currently explodes the mesh; omit until the inverse-bind fix lands.
            // spec: RealWorldRenderer — "_ = skeleton; _ = clip; intentionally unused until
            //       the skinning fix lands." CONFIRMED pattern.
            Node3D root = SkinnedCharacterBuilder.Build(mesh, null, null, albedo: albedo);
            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SkinnedCharacterBuilder.Build failed '{sknPath}': {ex.Message}");
            return null;
        }
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
    // Helpers
    // -------------------------------------------------------------------------

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