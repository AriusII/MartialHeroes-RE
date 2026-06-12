// World/NpcRenderer.cs
//
// PASSIVE rendering node that spawns NPC / monster skinned-character nodes from a caller-supplied
// spawn list.  It has ZERO game-rule authority: it never decides who spawns, it only visualises
// what it is told to.
//
// Design:
//   The orchestrator (RealWorldRenderer or GameLoop) owns the spawn SOURCE.
//   This node owns only the VISUAL result: a Skeleton3D + skinned mesh + AnimationPlayer per spawn.
//
//   Two entry points are exposed:
//
//   1. Populate(RealClientAssets, IEnumerable<(uint skinId, Vector3 worldPos)>)
//      The minimal, directly-injectable path.  The orchestrator converts its source
//      (NpcSpawnArray, items.csv, synthetic data …) into (skinId, worldPos) pairs and passes them.
//      skinId is the VFS numeric id used to form the path "data/char/skin/g{skinId}.skn".
//
//   2. PopulateFromArea(RealClientAssets, int areaId)
//      Convenience helper: reads "data/map{tag}/npc{tag}.arr", parses it with NpcSpawnParser,
//      and calls Populate() treating NpcSpawnRecord.MobId as a direct skin-id hint.
//      NOTE: MobId → skn-path is NOT a confirmed mapping; this uses MobId as a BEST-EFFORT
//      skin probe (tries data/char/skin/g{mobId}.skn and silently skips if absent).
//      spec: Docs/RE/formats/npc_spawns.md §Identification — path pattern "data/map<NNN>/npc<NNN>.arr".
//      spec: Docs/RE/formats/npc_spawns.md §Record layout — mob_id u16 @ +0: CONFIRMED.
//
//   World-to-Godot coordinate conversion:
//     Godot Z = -world_z (negate Z, same as WorldCoordinates.ToGodot).
//     spec: Helpers/WorldCoordinates.cs — ToGodot: "negate Z".
//     spec: Docs/RE/formats/npc_spawns.md — world_x f32 @ +4; world_z f32 @ +8: CONFIRMED.
//
//   Character loading mirrors RealWorldRenderer.LoadAndSpawnCharacter:
//     .skn path:  "data/char/skin/g{skinId}.skn"
//     .bnd path:  "data/char/bind/g{mesh.IdB}.bnd"
//     idle .mot:  resolved via actormotion.txt col2==mesh.IdB → col16 id → "data/char/mot/g{id}.mot"
//     spec: Docs/RE/formats/mesh.md §.skn header — id_b → .bnd actor_id: CONFIRMED.
//     spec: Docs/RE/formats/mesh.md §actormotion.txt — col2=class, col16=idle: CONFIRMED.
//
// Threading: Populate() is synchronous and MUST be called on the Godot main thread.
//            All AddChild calls happen in Populate() directly.
//
// Performance cap: at most MaxSpawns characters are rendered in one call; the caller is expected
//                  to pass a pre-filtered slice.  Default cap: 30.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive <see cref="Node3D"/> that renders NPC / monster characters as skinned meshes.
///
/// The node owns no spawn logic.  Call <see cref="Populate"/> or
/// <see cref="PopulateFromArea"/> once from the Godot main thread to materialise the characters.
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
    /// Maximum number of NPC/mob characters rendered in one <see cref="Populate"/> call.
    /// Excess entries are silently skipped.
    /// </summary>
    public int MaxSpawns { get; set; } = 30;

    /// <summary>
    /// Visual scale applied to each spawned character node.
    /// 5.0 matches RealWorldRenderer.CharacterScale (empirically tuned to legacy unit scale).
    /// spec: RealWorldRenderer.CharacterScale = 5.0f (visual tuning, no spec source).
    /// </summary>
    public float CharacterScale { get; set; } = 5.0f;

    /// <summary>
    /// Y offset added to the world-ground position of each character node.
    /// Set to a small positive value so characters sit on top of the terrain surface
    /// rather than clipping through it.  Default: 26 f (observed terrain surface height in
    /// the origin cell when flat terrain is rendered).
    /// </summary>
    public float GroundY { get; set; } = 26f;

    // -------------------------------------------------------------------------
    // Primary entry point — injected spawn list
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders each NPC as a skinned character.
    ///
    /// <para>
    /// <paramref name="spawns"/> is a sequence of <c>(skinId, worldPos)</c> where
    /// <c>skinId</c> is the numeric part of the VFS path <c>data/char/skin/g{skinId}.skn</c>
    /// and <c>worldPos</c> is already in Godot world-space (i.e. Z has been negated by the
    /// orchestrator, or pass the raw <c>world_x</c>/<c>world_z</c> and set
    /// <paramref name="rawLegacyCoords"/> to <see langword="true"/> to let this method negate Z).
    /// </para>
    ///
    /// <para>Must be called on the Godot main thread.</para>
    /// </summary>
    /// <param name="assets">Open VFS assets handle.</param>
    /// <param name="spawns">Spawn list.  At most <see cref="MaxSpawns"/> entries are processed.</param>
    /// <param name="rawLegacyCoords">
    ///   When <see langword="true"/>, <c>worldPos.Z</c> is treated as legacy world-Z and is
    ///   negated to produce Godot Z before positioning the node.
    ///   spec: Helpers/WorldCoordinates.cs — ToGodot: "negate Z". CONFIRMED.
    ///   Default: <see langword="false"/> (pos already in Godot space).
    /// </param>
    public void Populate(
        RealClientAssets assets,
        IEnumerable<(uint SkinId, Vector3 WorldPos)> spawns,
        bool rawLegacyCoords = false)
    {
        // Remove previously spawned children.
        ClearChildren();

        int spawned = 0;
        foreach ((uint skinId, Vector3 worldPos) in spawns)
        {
            if (spawned >= MaxSpawns)
                break;

            // Convert raw legacy coords if requested.
            // spec: Helpers/WorldCoordinates.cs — ToGodot: (x,y,z) → (x,y,-z). CONFIRMED.
            Vector3 godotPos = rawLegacyCoords
                ? new Vector3(worldPos.X, worldPos.Y, -worldPos.Z)
                : worldPos;

            // Lift the character to ground level.
            godotPos.Y = GroundY;

            Node3D? charRoot = TryBuildCharacter(assets, skinId);
            if (charRoot is null)
                continue;

            charRoot.Position = godotPos;
            charRoot.Scale = Vector3.One * CharacterScale;
            AddChild(charRoot);
            spawned++;
        }

        GD.Print($"[NpcRenderer] Spawned {spawned} NPC/mob characters.");
    }

    // -------------------------------------------------------------------------
    // Convenience entry point — load from VFS .arr file for an area
    // -------------------------------------------------------------------------

    /// <summary>
    /// Convenience method: loads <c>data/map{tag}/npc{tag}.arr</c> for the given area,
    /// parses it with <see cref="NpcSpawnParser"/>, and delegates to
    /// <see cref="Populate(RealClientAssets, IEnumerable{ValueTuple{uint, Vector3}}, bool)"/>
    /// treating <c>NpcSpawnRecord.MobId</c> as a best-effort skin-id probe.
    ///
    /// <para>
    /// CAVEAT: <c>MobId → skn path</c> is NOT a confirmed mapping.
    /// The method probes <c>data/char/skin/g{MobId}.skn</c> and silently skips if absent.
    /// Spawns from map 000 are always zero (16-byte anomaly; record_count == 0).
    /// </para>
    ///
    /// spec: Docs/RE/formats/npc_spawns.md §Identification — path "data/map{NNN}/npc{NNN}.arr".
    /// spec: Docs/RE/formats/npc_spawns.md §Record layout — mob_id u16 @ +0: CONFIRMED.
    /// spec: Docs/RE/formats/npc_spawns.md §Anomaly: map 000 — 16 bytes → 0 records: CONFIRMED.
    /// </summary>
    /// <param name="assets">Open VFS assets handle.</param>
    /// <param name="areaId">Area identifier (e.g. 1 for map001). Area 0 always yields zero records.</param>
    public void PopulateFromArea(RealClientAssets assets, int areaId)
    {
        // Compute the area tag.
        // spec: Docs/RE/formats/terrain.md §1.1 — digit decomposition. CONFIRMED.
        string tag = AreaTag(areaId);
        string arrPath = $"data/map{tag}/npc{tag}.arr";

        if (!assets.Contains(arrPath))
        {
            GD.Print($"[NpcRenderer] No .arr file for area {areaId} ({arrPath}) — no NPC spawns.");
            return;
        }

        ReadOnlyMemory<byte> arrData = assets.GetRaw(arrPath);
        if (arrData.IsEmpty)
        {
            GD.Print($"[NpcRenderer] .arr file empty for area {areaId} ({arrPath}).");
            return;
        }

        NpcSpawnArray spawnArray;
        try
        {
            spawnArray = NpcSpawnParser.Parse(arrData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] NpcSpawnParser.Parse failed for '{arrPath}': {ex.Message}");
            return;
        }

        GD.Print($"[NpcRenderer] .arr parsed: {spawnArray.Records.Length} records for area {areaId}.");

        // Convert to (skinId, worldPos) pairs.
        // NOTE: MobId → skin-id is not confirmed.  We probe data/char/skin/g{MobId}.skn.
        // world_x and world_z are legacy coordinates; pass rawLegacyCoords=true so Populate
        // negates Z before positioning.
        // spec: Docs/RE/formats/npc_spawns.md — world_x f32 @ +4; world_z f32 @ +8: CONFIRMED.
        IEnumerable<(uint, Vector3)> AsSpawns()
        {
            foreach (NpcSpawnRecord r in spawnArray.Records)
            {
                // world_y is absent from .arr; the terrain system would resolve it at runtime.
                // We pass Y=0 here (GroundY override is applied in Populate).
                // spec: Docs/RE/formats/npc_spawns.md — "Y (height) is not stored." CONFIRMED.
                yield return ((uint)r.MobId, new Vector3(r.WorldX, 0f, r.WorldZ));
            }
        }

        Populate(assets, AsSpawns(), rawLegacyCoords: true);
    }

    // -------------------------------------------------------------------------
    // Character building — mirrors RealWorldRenderer helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to build a Godot node for one NPC/mob character from the VFS.
    ///
    /// Steps:
    ///   1. Parse <c>data/char/skin/g{skinId}.skn</c> via <see cref="SknParser"/>.
    ///   2. Load <c>data/char/bind/g{mesh.IdB}.bnd</c> via <see cref="BndParser"/> (optional).
    ///   3. Load idle .mot via actormotion.txt → <see cref="AnimationParser"/> (optional).
    ///   4. Build Godot node tree via <see cref="SkinnedCharacterBuilder.Build"/>.
    ///
    /// Returns null (and logs) on any failure; never throws.
    ///
    /// spec: Docs/RE/formats/mesh.md §.skn header — id_b → .bnd actor_id: CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §actormotion.txt — TAB-separated, col2=class, col16=idle: CONFIRMED.
    /// spec: Docs/RE/formats/animation.md §.mot.
    /// </summary>
    private Node3D? TryBuildCharacter(RealClientAssets assets, uint skinId)
    {
        string sknPath = $"data/char/skin/g{skinId}.skn";

        if (!assets.Contains(sknPath))
        {
            // Expected for MobId-based probes — silent skip.
            return null;
        }

        SkinnedMesh? mesh = null;
        try
        {
            ReadOnlyMemory<byte> sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty) return null;
            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SknParser.Parse failed for '{sknPath}': {ex.Message}");
            return null;
        }

        // Load skeleton (.bnd) — optional.
        // spec: Docs/RE/formats/mesh.md §.skn header — id_b: CONFIRMED.
        Skeleton? skeleton = TryLoadSkeleton(assets, mesh);

        // Load idle animation (.mot) — optional.
        // spec: Docs/RE/formats/mesh.md §actormotion.txt — col2=class, col16=idle: CONFIRMED.
        AnimationClip? clip = TryLoadAnimation(assets, mesh);

        Node3D charRoot;
        try
        {
            charRoot = SkinnedCharacterBuilder.Build(mesh, skeleton, clip, albedo: null);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] SkinnedCharacterBuilder.Build failed for '{sknPath}': {ex.Message}");
            return null;
        }

        charRoot.Name = $"Npc_{skinId}";
        return charRoot;
    }

    /// <summary>
    /// Loads the skeleton for a character mesh, or returns null (static pose) if absent.
    ///
    /// spec: Docs/RE/formats/mesh.md §.skn header — id_b → .bnd actor_id. CONFIRMED.
    /// spec: Docs/RE/formats/mesh.md §.bnd — "data/char/bind/g{id_b}.bnd". CONFIRMED.
    /// </summary>
    private static Skeleton? TryLoadSkeleton(RealClientAssets assets, SkinnedMesh mesh)
    {
        if (mesh.IdB == 0) return null;
        string bndPath = $"data/char/bind/g{mesh.IdB}.bnd";
        if (!assets.Contains(bndPath)) return null;

        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(bndPath);
            if (data.IsEmpty) return null;
            return BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] .bnd load failed for IdB={mesh.IdB}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads the idle animation for a character mesh, or returns null (rest pose) if absent.
    ///
    /// spec: Docs/RE/formats/mesh.md §actormotion.txt — col2=class, col16=idle-peace id. CONFIRMED.
    /// spec: Docs/RE/formats/animation.md §.mot.
    /// </summary>
    private static AnimationClip? TryLoadAnimation(RealClientAssets assets, SkinnedMesh mesh)
    {
        if (mesh.IdB == 0) return null;

        string? motPath = ResolveIdleMotPath(assets, mesh.IdB);
        if (motPath is null || !assets.Contains(motPath)) return null;

        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(motPath);
            if (data.IsEmpty) return null;
            return AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcRenderer] .mot load failed for IdB={mesh.IdB}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves the idle-peace motion path for an actor class from <c>actormotion.txt</c>.
    /// Returns the VFS path <c>data/char/mot/g{id}.mot</c>, or null when no entry is found.
    ///
    /// spec: Docs/RE/formats/mesh.md §actormotion.txt — TAB-separated; col2=class, col16=idle-peace id. CONFIRMED.
    /// </summary>
    private static string? ResolveIdleMotPath(RealClientAssets assets, uint actorClassId)
    {
        const string tablePath = "data/char/actormotion.txt";
        if (!assets.Contains(tablePath)) return null;

        // All game text is CP949 (EUC-KR).
        // spec: project prompt — "ALL game text is CP949".
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        string text = System.Text.Encoding.GetEncoding(949)
            .GetString(assets.GetRaw(tablePath).Span);

        foreach (string rawLine in text.Split('\n'))
        {
            // TAB-separated; strip CR.
            string[] cols = rawLine.Replace("\r", string.Empty).Split('\t');
            if (cols.Length <= 16) continue;

            if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != actorClassId)
                continue;

            string idle = cols[16].Trim();
            if (idle.Length == 0 || idle == "0") return null;

            // spec: Docs/RE/formats/mesh.md §actormotion.txt — "data/char/mot/g{id}.mot". CONFIRMED.
            return $"data/char/mot/g{idle}.mot";
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Removes all currently spawned character children so the node can be repopulated.
    /// </summary>
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
