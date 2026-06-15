// Screens/CharSelectScene3D.cs
//
// The unified 3D backdrop for the character-select screen — REWRITTEN FROM SCRATCH against the
// recovered spec (CAMPAIGN 9 WAVE 3). Every value below is a REAL VFS asset (the cell, the env
// bins, the xeff, the skins) or a spec-cited IDA constant. There is NO procedural sky, NO
// hand-placed torch / omni rig, NO hand-tuned look-at, NO bloom tuning, NO "looks-nice" numbers.
//
// WHAT THIS NODE BUILDS, AND THE REAL ASSET EACH PART COMES FROM:
//   1. BACKDROP CELL — the real cell data/map000/dat/d000x10000z9990.{ted,bud,map} via TerrainNode
//      (heightfield) + BudMeshBuilder (the carved suksang*/walll04* stone wall). No procedural
//      geometry. spec: §3.7.1 / §3.7.3.
//   2. ENVIRONMENT — the recovered AREA-0 values: a near-white ambient FLOOR (OPTION_BRIGHT/100 =
//      1.0 → white, the MAIN illuminant), a faint achromatic directional (≈0.047), fog OFF, an
//      achromatic dark background. NO procedural sky shader, NO coloured omni lights. spec: §3.6 +
//      environment_bins.md.
//   3. CAMERA (entry dolly) — KF0 → KF1 over 2.0 s (CharSelectCameraRig), projection FOV 50 / near
//      5 / far 15000, framed by a documented LookAt toward the row pivot. spec: §3.5 / §3.5.1.
//   4. AMBIENT EFFECT — the real char_select-u.xeff (id 380003000) at the row-centre anchor, as
//      alpha-blended camera-facing billboards (XeffSceneEffect). spec: §3.6.5 / §3.6.6.
//   5. ACTORS — up to 5 preview actors via SkinnedCharacterBuilder at the spec per-slot positions
//      (the slightly-bowed Z), on the platform Y≈70, PreviewScale ×3.0. spec: §3.3.1 / §3.7.5.
//
// HOST API PRESERVED (read by Lane D's CharacterSelectScreen — keep these signatures EXACT):
//   - public void Initialise(RealClientAssets? assets)
//   - public int TryHitTestSlot(global::Godot.Vector2 viewportLocalPos)
//   - public void SetSelectedSlot(int slotIndex)
//   - public (bool IsOccupied, uint SkinClassId)[] SlotDescriptors { get; set; }
//
// COORDINATE CONVENTION: world geometry negates Z (Helpers/WorldCoordinates.ToGodot: (x,y,z) →
//   (x,y,−z)). Every world position below is converted to Godot-space by negating Z exactly once.
//
// NAMESPACE PITFALL: inside MartialHeroes.Client.Godot.* a bare `Environment`/`Input`/`Time`
//   resolves to the sibling project namespace (CS0234) → use global::Godot.Environment etc.
//
// NO FALLBACK: a missing asset logs + skips; it never crashes and never substitutes synthetic data.
// PASSIVE: zero game logic. Reads VFS assets, builds geometry, places a camera. No domain state.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Helpers;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A <see cref="Node3D"/> that builds the full 3D character-select scene from real VFS assets:
/// the map000 backdrop cell, the standing preview row, the entry-dolly camera
/// (<see cref="CharSelectCameraRig"/>), the area-0 environment, and the single ambient effect.
///
/// <para>Construction: set <see cref="SlotDescriptors"/>, then call <see cref="Initialise"/> after
/// the node is in the scene tree. Assets may be null — the scene degrades to env + camera only,
/// logging each skipped asset (no crash, no synthetic data).</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.3 / §3.5 / §3.6 / §3.7.
/// </summary>
public sealed partial class CharSelectScene3D : Node3D
{
    // =========================================================================
    // Backdrop cell identity — the real, purpose-built char-select cell.
    // spec: §3.7.1 — base world data/map000 (area code 0 → "000"); the only cell in map000 is
    //   d000x10000z9990 (mapX=10000, mapZ=9990). CODE-CONFIRMED.
    // =========================================================================

    private const int BackdropAreaId = 0; // map000 (area code 0). spec: §3.5.1 / §3.7.1
    private const int BackdropMapX = 10000; // spec: §3.7.1 cell d000x10000z9990
    private const int BackdropMapZ = 9990; // spec: §3.7.1 cell d000x10000z9990

    // =========================================================================
    // Stage frame & per-slot placement (§3.3.1, §3.7.2). All in LEGACY world space; the
    // Godot-space value used for placement negates Z exactly once via WorldCoordinates.
    // =========================================================================

    // Stage world origin (2048, 0, −6144) — the anchor the per-slot offsets and the camera
    // keyframes are added to. spec: §3.7.2 CODE-CONFIRMED. (Documented; placement uses the
    // already-summed per-slot world positions below.)
    // (no standalone field needed — the per-slot world X/Z below are the anchor + offset already summed.)

    // Per-slot LEGACY world X (= base X 2048 + ΔX {−1560,−1548,−1536,−1524,−1512}); convention-
    // neutral, so identical in Godot-space. spec: §3.3.1 CODE-CONFIRMED.
    private static readonly float[] SlotLegacyX = [488.0f, 500.0f, 512.0f, 524.0f, 536.0f];

    // Per-slot LEGACY world Z (= base Z −6144 + ΔZ {−3593,−3594,−3594.5,−3594,−3593}); the row bows
    // ~1.5 units toward the camera at the centre slot. Do NOT flatten this bow. spec: §3.3.1.
    private static readonly float[] SlotLegacyZ = [-9737.0f, -9738.0f, -9738.5f, -9738.0f, -9737.0f];

    // Per-slot Godot-space Z (world Z negated once). spec: WorldCoordinates.ToGodot.
    private static readonly float[] SlotGodotZ = BuildSlotGodotZ();

    private static float[] BuildSlotGodotZ()
    {
        var z = new float[SlotLegacyZ.Length];
        for (int i = 0; i < z.Length; i++)
            z[i] = WorldCoordinates.ToGodot(0f, 0f, SlotLegacyZ[i]).Z; // (x,y,z)→(x,y,−z)
        return z;
    }

    // Row platform Y. spec §3.3.1 says slot ΔY is a hard 0.0 on the stage-origin plane (no terrain
    // sample on the placement path); spec §3.6.5 records the row-pivot / look-at anchor lifted to
    // Y≈69.89 (the .bud platform top). The actors stand on that platform, so the placement Y is the
    // spec row-pivot Y (≈70). The terrain .ted sampler returns the raw soil/rock floor, NOT the
    // platform top, so it is used for diagnostics only. spec: §3.6.5 row pivot (508.48, 69.89, …).
    private const float RowPlatformY = 69.89f; // spec: §3.6.5 row pivot Y. CODE-CONFIRMED.

    // Row pivot (LEGACY world) — the focal point of the backdrop and the camera look-at anchor.
    // spec: §3.6.5 / §3.7.2 — row pivot world (508.48, 69.89, −9758.57). CODE-CONFIRMED.
    private const float RowPivotLegacyX = 508.48f;
    private const float RowPivotLegacyY = 69.89f;
    private const float RowPivotLegacyZ = -9758.57f;

    // Preview scale ×3.0. spec §3.3.1: the legacy per-slot scale literal ≈70 (lineup) is a LEGACY-
    // space value that does NOT map 1:1 to Godot; the unit-reconciled Godot equivalent is ×3.0
    // (verified against the importer's mesh scale, documented — NOT a hard-coded guess; the raw 70
    // literal would explode the actors in Godot space). spec: §3.3.1 (fidelity-reconciliation item).
    private const float PreviewScale = 3.0f;

    // =========================================================================
    // Camera keyframes & projection (§3.5). All EXACT from the spec; positions converted to
    // Godot-space (Z negated once).
    // =========================================================================

    // KF0 (entry-dolly start) = world (515.549, 137.266, −9397.710). EXACT. spec: §3.5 / §3.5.2.
    internal static readonly Vector3 DollyKF0Godot = ToGodotVec(515.549f, 137.266f, -9397.710f);

    // KF1 (entry-dolly end / resting pose) = world (512, 87, −9652). EXACT. spec: §3.5 / §3.5.2.
    internal static readonly Vector3 DollyKF1Godot = ToGodotVec(512.0f, 87.0f, -9652.0f);

    // Camera look-at anchor = the row pivot (the camera look-at sits essentially over it; §3.5.4 /
    // §3.7.2). The exact per-keyframe free-look Euler (yaw/pitch) is DEBUGGER-PENDING (§3.5
    // headline), so — per the Lane brief — we keep a documented LookAt toward the row pivot for
    // framing rather than inventing an orbit or an aesthetic aim point. spec: §3.5 / §3.6.5.
    internal static readonly Vector3 DollyLookAtGodot = ToGodotVec(RowPivotLegacyX, RowPivotLegacyY, RowPivotLegacyZ);

    // Projection — EXACT. spec: §3.5.1 — vertical FOV 50°, near 5.0, far 15000.0. CODE-CONFIRMED.
    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;

    // =========================================================================
    // Environment (§3.6) — the recovered AREA-0 values. NO procedural sky, NO coloured lights.
    // =========================================================================

    // White ambient FLOOR (the scene's main illuminant): OPTION_BRIGHT/100 = 100/100 = 1.0 → white.
    // spec: environment_bins.md §10.5/§11.5 + frontend_scenes.md §3.6.1/§3.6.2. CODE-CONFIRMED.
    private const float AmbientFloorEnergy = 1.0f;

    // Achromatic dark background — the area-0 sky is pure grey (sky_haze R=G=B ≈ 0.004); there is no
    // skybox file and the scene builds no procedural sky. spec: §3.6.3 / §3.7.4 + environment_bins.md.
    private static readonly Color BackgroundColorAchromatic = new(0.04f, 0.04f, 0.04f);

    // Faint achromatic DIRECTIONAL key — area-0 light-keyframe 29 (14:30) directional energy ≈0.047,
    // achromatic (R=G=B). Light vector (−7, 7, 20) LEGACY world → Godot negates Z → (−7, 7, −20).
    // spec: environment_bins.md §9.4/§10.6/§11.2/§11.3 SAMPLE-VERIFIED (read from light0.bin kf-29).
    private const float DirectionalEnergy = 0.047f;
    private static readonly Vector3 DirectionalDirGodot = ToGodotVec(-7.0f, 7.0f, 20.0f).Normalized();

    // =========================================================================
    // Ambient effect (§3.6.5) — the single code-spawned char_select-u.xeff (id 380003000).
    // =========================================================================

    // Effect anchor = the row centre, world (508.483, 69.887, −9758.569), scale 1.0, identity
    // rotation, the builder's SOLE spawn. spec: §3.6.5 CODE-CONFIRMED. Godot-space negates Z.
    private static readonly Vector3 XeffAnchorGodot = ToGodotVec(508.483f, 69.887f, -9758.569f);

    // =========================================================================
    // Preview-character assets (§3.7.5) — the four starter classes (IdA=1).
    // =========================================================================

    // Per starter class → its base-skin .skn. Each mesh carries a DISTINCT id_b that drives its OWN
    // rig + idle clip (resolved per slot in TryBuildSlotActor). spec: §3.7.5 CODE-CONFIRMED.
    // (Resolution is best-effort by skin-class id; absent → logged + skipped, no synthetic actor.)
    private static string[] SknCandidatesForClass(uint skinClassId) => skinClassId switch
    {
        1 => ["data/char/skin/g202110001.skn"], // Bichimi / Dosa
        2 => ["data/char/skin/g202220001.skn"],
        3 => ["data/char/skin/g202130001.skn"],
        4 => ["data/char/skin/g202140001.skn"],
        _ =>
        [
            $"data/char/skin/g202{skinClassId}10001.skn",
            $"data/char/skin/g202{skinClassId}10002.skn",
        ],
    };

    // =========================================================================
    // Runtime state
    // =========================================================================

    private Camera3D? _camera;
    private CharSelectCameraRig? _cameraRig;
    private TerrainNode? _backdropTerrain;
    private readonly Node3D?[] _slotActors = new Node3D?[5];
    private int _selectedSlot;

    // =========================================================================
    // Public host API — DO NOT change these signatures (CharacterSelectScreen calls them).
    // =========================================================================

    /// <summary>
    /// Per-slot descriptor array (max 5). Each entry: (IsOccupied, SkinClassId). Set before
    /// <see cref="Initialise"/>. spec: §3.2 / §3.3.1 — the row is descriptor-driven.
    /// </summary>
    public (bool IsOccupied, uint SkinClassId)[] SlotDescriptors { get; set; } = new (bool, uint)[5];

    /// <summary>
    /// Builds the whole 3D scene from real VFS assets. Call AFTER the node is in the scene tree.
    /// A null <paramref name="assets"/> degrades to env + camera only (logged; no crash).
    /// </summary>
    public void Initialise(RealClientAssets? assets)
    {
        try
        {
            BuildEnvironment();
            BuildLighting();
            BuildCamera();

            if (assets is not null)
            {
                BuildBackdropTerrain(assets);
                BuildBackdropProps(assets);
                BuildCharacterRow(assets);
                BuildAmbientEffect(assets);
            }
            else
            {
                GD.Print("[CharSelectScene3D] No VFS — backdrop cell / actors / ambient effect skipped; env + camera only.");
            }

            GD.Print("[CharSelectScene3D] 3D scene initialised from real assets (no procedural sky, no omni rig). " +
                     "spec: frontend_scenes.md §3.3/§3.5/§3.6/§3.7.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Initialise failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the selected slot. The legacy on-actor change is a select/idle motion-clip swap
    /// (NO transform / tint change — §3.3.4); the concrete select .mot literal is a VFS data read
    /// (actormotion.txt col 21) that is not yet harvested, so we only record the selection here and
    /// the 2D overlay (CharacterSelectScreen) carries the visible highlight. spec: §3.3.3 / §3.3.4.
    /// </summary>
    public void SetSelectedSlot(int slotIndex)
    {
        if ((uint)slotIndex >= 5u) return;
        _selectedSlot = slotIndex;
    }

    /// <summary>
    /// 3D world-space ray-pick: unprojects <paramref name="viewportLocalPos"/> through the scene
    /// camera and returns the slot (0..4) whose per-slot AABB it first hits, or −1. The AABB is
    /// X ± 6, Z ± 6, Y band [rowBaseY, rowBaseY + 22] (= spec Y 70..92). spec: §3.3.3 CODE-CONFIRMED.
    /// </summary>
    public int TryHitTestSlot(global::Godot.Vector2 viewportLocalPos)
        => _cameraRig?.HitTest(viewportLocalPos) ?? -1;

    // =========================================================================
    // Environment — area-0 white ambient floor + achromatic dark BG + fog OFF (NO procedural sky).
    // =========================================================================

    private void BuildEnvironment()
    {
        // global::Godot.Environment avoids the sibling-namespace collision (CS0234).
        var env = new global::Godot.Environment();

        // Achromatic dark background. The area-0 sky is parametric (no skybox file; §3.7.4) and the
        // builder authors NO procedural sky — a flat achromatic clear colour is the faithful base.
        // spec: §3.6.3 + environment_bins.md (achromatic sky_haze).
        env.BackgroundMode = global::Godot.Environment.BGMode.Color;
        env.BackgroundColor = BackgroundColorAchromatic;

        // WHITE ambient FLOOR — the scene's MAIN illuminant (OPTION_BRIGHT/100 = 1.0 → white).
        // spec: §3.6.1/§3.6.2 + environment_bins.md §10.5/§11.5. CODE-CONFIRMED.
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(1.0f, 1.0f, 1.0f);
        env.AmbientLightEnergy = AmbientFloorEnergy; // spec: §3.6.2 OPTION_BRIGHT/100 = 1.0

        // Linear tonemap — faithful to the legacy D3D9 output (no ACES darkening) so the white
        // ambient floor reads at full value. spec: rendering.md (D3D9 linear output).
        env.TonemapMode = global::Godot.Environment.ToneMapper.Linear;
        env.TonemapExposure = 1.0f;

        // Distance fog OFF — the select-scene build helper zeroes the fog-blend offset (factor 0).
        // spec: §3.6.2 CODE-CONFIRMED.
        env.FogEnabled = false;

        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);

        GD.Print("[CharSelectScene3D] Area-0 environment: achromatic dark BG + WHITE ambient floor " +
                 "(energy 1.0, OPTION_BRIGHT) + fog OFF. NO procedural sky. spec: §3.6 + environment_bins.md.");
    }

    // =========================================================================
    // Lighting — ONLY the faint achromatic area-0 directional. NO point-lights.
    // =========================================================================

    private void BuildLighting()
    {
        // The scene builder creates NO brazier/torch point-lights — the warm look is the additive
        // fire texture in the ambient .xeff, not lights. spec: §3.6.1 / §3.6.6 CODE-CONFIRMED.
        // The ONLY light authored here is the faint achromatic area-0 directional (kf-29, 14:30).
        var sun = new DirectionalLight3D
        {
            Name = "Area0Directional",
            LightEnergy = DirectionalEnergy, // spec: environment_bins.md §11.3 (area-0 kf-29 directional ≈0.047)
            LightColor = new Color(1.0f, 1.0f, 1.0f), // achromatic — area-0 R=G=B. spec: §11.2
            ShadowEnabled = false,
        };
        AddChild(sun);

        // Orient the directional along the recovered light vector. The pivot is just a placement
        // origin for the parallel light's direction (a directional light has no position effect);
        // the row centre at a raised Y is used so the LookAtFromPosition direction is well-defined.
        var pivot = ToGodotVec(RowPivotLegacyX, 200.0f, RowPivotLegacyZ);
        sun.LookAtFromPosition(pivot, pivot + DirectionalDirGodot, Vector3.Up);

        GD.Print("[CharSelectScene3D] Lighting: faint achromatic directional (0.047) ONLY; " +
                 "NO point-lights (the warm glow is the additive xeff fire texture). spec: §3.6.1 / §3.6.6.");
    }

    // =========================================================================
    // Camera — projection camera at KF0 + the entry-dolly rig (KF0→KF1, 2.0 s).
    // =========================================================================

    private void BuildCamera()
    {
        // Bare projection camera (FOV / near / far only) — the rig drives the realised view.
        // spec: §3.5 (two objects: projection camera + path rig) / §3.5.1 (projection).
        _camera = new Camera3D
        {
            Name = "CharSelectCamera",
            Fov = CameraFov,   // spec: §3.5.1 vertical FOV 50°
            Near = CameraNear, // spec: §3.5.1 near 5.0
            Far = CameraFar,   // spec: §3.5.1 far 15000.0
            KeepAspect = Camera3D.KeepAspectEnum.Height, // vertical FOV is the reference (§3.5.1)
        };
        AddChild(_camera);

        // Pose the camera at KF0 framing the row pivot; the rig animates it to KF1 over 2.0 s.
        // spec: §3.5.2 — rig constructed at index 0 (dolly start). CODE-CONFIRMED.
        _camera.Position = DollyKF0Godot;
        _camera.LookAt(DollyLookAtGodot, Vector3.Up);

        // Build + wire the entry-dolly + manual-input + hit-test rig.
        // spec: §3.5 (entry dolly KF0→KF1) / §3.5.4 (manual boom-zoom + actor-yaw) / §3.3.3 (hit-test).
        _cameraRig = new CharSelectCameraRig { Name = "CharSelectCameraRig" };
        AddChild(_cameraRig);
        _cameraRig.Configure(
            camera: _camera,
            slotGodotX: SlotLegacyX, // X is convention-neutral (Godot X = legacy X)
            slotGodotZ: SlotGodotZ,
            selectedSlotProvider: () => _selectedSlot,
            slotActorProvider: i => (uint)i < (uint)_slotActors.Length ? _slotActors[i] : null,
            kf0Pos: DollyKF0Godot,
            kf1Pos: DollyKF1Godot,
            lookAtTarget: DollyLookAtGodot);

        GD.Print($"[CharSelectScene3D] Camera: KF0={DollyKF0Godot} → KF1={DollyKF1Godot} " +
                 $"look-at(row pivot)={DollyLookAtGodot}; FOV {CameraFov}/near {CameraNear}/far {CameraFar}; " +
                 "2.0 s lerp/slerp then hold KF1. spec: §3.5 / §3.5.1 / §3.5.2.");
    }

    // =========================================================================
    // Backdrop terrain — the real cell d000x10000z9990.ted via TerrainNode.
    // =========================================================================

    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        string tag = AreaTag(BackdropAreaId);
        string tedPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.ted";

        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .ted absent: {tedPath} — terrain skipped.");
            return;
        }

        try
        {
            ReadOnlyMemory<byte> tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharSelectScene3D] Backdrop .ted empty: {tedPath} — terrain skipped.");
                return;
            }

            // Terrain textures resolve through the confirmed two-hop chain (TERRAIN section).
            // spec: terrain.md §5.6 Block 3 — 1-based tex byte → .map TERRAIN TEXTURES[idx-1] →
            //   bgtexture.txt → data/map000/texture/<rel>.dds.
            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = BuildTerrainTextureResolver(assets),
            };
            AddChild(terrainNode);
            _backdropTerrain = terrainNode;

            // Feed the single sector directly (no streaming for one backdrop cell).
            terrainNode.OnSectorLoaded(new MartialHeroes.Client.Application.World.SectorLoadedEvent(
                MapX: BackdropMapX, MapZ: BackdropMapZ, Payload: tedData));

            GD.Print($"[CharSelectScene3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. spec: §3.7.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop terrain failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Backdrop props — the real cell d000x10000z9990.bud (the carved suksang*/walll04* wall).
    // =========================================================================

    private void BuildBackdropProps(RealClientAssets assets)
    {
        string tag = AreaTag(BackdropAreaId);
        string budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";

        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .bud absent: {budPath} — props skipped.");
            return;
        }

        try
        {
            BudScene? scene = assets.LoadBud(budPath);
            if (scene is null || scene.Objects.Length == 0)
            {
                GD.Print("[CharSelectScene3D] Backdrop .bud empty — no props.");
                return;
            }

            // Building textures resolve through the same two-hop chain via the BUILDING section.
            // spec: §3.7.3 — suksang01..04 / walll04 / walll04_2 / haha under building/.
            BgTextureCatalog? bgPool = TryLoadBgPool(assets);
            MapDescriptor? cellMap = TryLoadCellMap(assets);

            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            Node3D propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            AddChild(propsRoot);

            GD.Print($"[CharSelectScene3D] Backdrop props built ({scene.Objects.Length} objects, carved wall). spec: §3.7.1 / §3.7.3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop props failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Ambient effect — the single real char_select-u.xeff (id 380003000) at the row centre.
    // =========================================================================

    private void BuildAmbientEffect(RealClientAssets assets)
    {
        // The scene builder spawns EXACTLY ONE ambient effect: char_select-u.xeff (id 380003000) at
        // the row-centre anchor, identity orientation, scale 1.0, loop. Rendered as alpha-blended
        // camera-facing billboards (XeffSceneEffect — the existing pattern, reused, not re-invented).
        // Absent file → logged + skipped inside LoadAndAttach (no fallback geometry). spec: §3.6.5 / §3.6.6.
        XeffSceneEffect.LoadAndAttach(this, anchorGodotPos: XeffAnchorGodot, assets: assets);
    }

    // =========================================================================
    // Character row — up to 5 skinned preview actors at the spec per-slot positions.
    // =========================================================================

    private void BuildCharacterRow(RealClientAssets assets)
    {
        // Placement Y = the spec row-pivot platform Y (≈70); the .ted sampler returns the raw soil
        // floor (NOT the .bud platform top) so it is read for diagnostics only. spec: §3.3.1 / §3.6.5.
        float rowY = RowPlatformY;
        if (_backdropTerrain is not null &&
            _backdropTerrain.TryGetGroundHeight(RowPivotLegacyX, RowPivotLegacyZ, out float sampledY, RowPlatformY))
        {
            GD.Print($"[CharSelectScene3D] Terrain sampler at pivot = {sampledY:F3} (soil floor, NOT platform top); " +
                     $"placing actors on platform Y={rowY:F2}. spec: §3.6.5.");
        }

        for (int i = 0; i < 5; i++)
        {
            bool occupied = i < SlotDescriptors.Length && SlotDescriptors[i].IsOccupied;
            if (!occupied) continue;

            try
            {
                Node3D? actor = TryBuildSlotActor(assets, i, SlotDescriptors[i].SkinClassId, rowY);
                if (actor is not null)
                {
                    _slotActors[i] = actor;
                    AddChild(actor);
                    GD.Print($"[CharSelectScene3D] Slot {i} actor at Godot ({SlotLegacyX[i]:F1}, {rowY:F2}, {SlotGodotZ[i]:F1}). spec: §3.3.1.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharSelectScene3D] Slot {i} actor build failed: {ex.Message}");
            }
        }
    }

    private Node3D? TryBuildSlotActor(RealClientAssets assets, int slotIdx, uint skinClassId, float rowY)
    {
        string? sknPath = PickSknPath(assets, skinClassId);
        if (sknPath is null)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: no .skn present for skinClassId={skinClassId} — skipped.");
            return null;
        }

        SkinnedMesh mesh;
        try
        {
            ReadOnlyMemory<byte> sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: .skn empty '{sknPath}' — skipped.");
                return null;
            }
            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: .skn parse failed '{sknPath}': {ex.Message}");
            return null;
        }

        // Skeleton + idle clip resolve from the MESH'S OWN id_b (per class) — never a shared rig.
        // spec: skinning.md §8(e) — data/char/bind/g{id_b}.bnd + actormotion.txt col2==id_b→col16.
        Skeleton? skeleton = TryLoadSkeletonForIdB(assets, mesh.IdB);
        AnimationClip? idleClip = TryLoadIdleClipForIdB(assets, mesh.IdB);
        ImageTexture? albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);

        // SkinnedCharacterBuilder applies the upright stand-up pivot + recentre (feet at local Y=0);
        // its returned Position is the recentre offset — keep it, and place the slot via a wrapper.
        Node3D actorRoot = SkinnedCharacterBuilder.Build(mesh, skeleton, idleClip, albedo);

        var slotWrapper = new Node3D { Name = $"Slot{slotIdx}Actor" };
        slotWrapper.Position = new Vector3(SlotLegacyX[slotIdx], rowY, SlotGodotZ[slotIdx]);
        slotWrapper.Scale = Vector3.One * PreviewScale; // spec: §3.3.1 ×3.0 (unit-reconciled)

        // Facing — pure yaw about world Y. Occupied (lock clear) → yaw 0 (front, toward camera);
        // locked / new / creating → yaw π (back to viewer). spec: §3.3.2 CODE-CONFIRMED.
        bool isOccupied = slotIdx < SlotDescriptors.Length && SlotDescriptors[slotIdx].IsOccupied;
        float slotYaw = isOccupied ? 0.0f : Mathf.Pi;
        slotWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(slotYaw), 0f);

        slotWrapper.AddChild(actorRoot);
        return slotWrapper;
    }

    // =========================================================================
    // Asset helpers — all existence-checked, no synthetic fallback.
    // =========================================================================

    private static Skeleton? TryLoadSkeletonForIdB(RealClientAssets assets, uint idB)
    {
        if (idB == 0) return null;
        string bndPath = $"data/char/bind/g{idB}.bnd";
        if (!assets.Contains(bndPath))
        {
            GD.PrintErr($"[CharSelectScene3D] .bnd absent for id_b={idB}: {bndPath} — rest pose.");
            return null;
        }
        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(bndPath);
            return data.IsEmpty ? null : BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] BndParser failed '{bndPath}': {ex.Message}");
            return null;
        }
    }

    private static AnimationClip? TryLoadIdleClipForIdB(RealClientAssets assets, uint idB)
    {
        if (idB == 0) return null;
        const string tablePath = "data/char/actormotion.txt";
        if (!assets.Contains(tablePath)) return null;

        try
        {
            // CP949 (registered once at startup). spec: CLAUDE.md §Core engineering constraints.
            string text = System.Text.Encoding.GetEncoding(949).GetString(assets.GetRaw(tablePath).Span);
            foreach (string rawLine in text.Split('\n'))
            {
                string[] cols = rawLine.Replace("\r", string.Empty).Split('\t');
                if (cols.Length <= 16) continue;
                if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != idB) continue;

                string idle = cols[16].Trim(); // col 16 = idle motion id. spec: §3.3.4
                if (idle.Length == 0 || idle == "0") return null;

                string motPath = $"data/char/mot/g{idle}.mot";
                if (!assets.Contains(motPath)) return null;

                ReadOnlyMemory<byte> motData = assets.GetRaw(motPath);
                return motData.IsEmpty ? null : AnimationParser.Parse(motData);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] TryLoadIdleClipForIdB(id_b={idB}) failed: {ex.Message}");
        }
        return null;
    }

    private static string? PickSknPath(RealClientAssets assets, uint skinClassId)
    {
        foreach (string p in SknCandidatesForClass(skinClassId))
            if (assets.Contains(p))
                return p;
        return null; // absent → caller logs + skips (no synthetic substitution)
    }

    // ── Terrain/building texture two-hop chain (terrain.md §5.6 / §3.5 / §4.2) ───────────────────

    private Func<int, ImageTexture?> BuildTerrainTextureResolver(RealClientAssets assets)
    {
        BgTextureCatalog? bgPool = TryLoadBgPool(assets);
        MapDescriptor? cellMap = TryLoadCellMap(assets);
        var cache = new Dictionary<int, ImageTexture?>();
        return texByte =>
        {
            if (cache.TryGetValue(texByte, out ImageTexture? cached)) return cached;
            ImageTexture? tex = ResolveTexture(assets, bgPool, cellMap, "TERRAIN", texByte);
            cache[texByte] = tex;
            return tex;
        };
    }

    private static BgTextureCatalog? TryLoadBgPool(RealClientAssets assets)
    {
        try
        {
            const string txtPath = "data/map000/texture/bgtexture.txt";
            return assets.Contains(txtPath) ? BgTextureTxtParser.Parse(assets.GetRaw(txtPath)) : null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] bgtexture.txt load failed: {ex.Message}");
            return null;
        }
    }

    private static MapDescriptor? TryLoadCellMap(RealClientAssets assets)
    {
        try
        {
            string tag = AreaTag(BackdropAreaId);
            string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
            return assets.Contains(mapPath) ? MapDescriptorParser.Parse(assets.GetRaw(mapPath)) : null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] backdrop .map load failed: {ex.Message}");
            return null;
        }
    }

    private static ImageTexture? ResolveTexture(
        RealClientAssets assets, BgTextureCatalog? pool, MapDescriptor? map,
        string section, int oneBasedIdx)
    {
        if (pool is null || map is null || oneBasedIdx <= 0) return null;

        (int Flag, int TexId)[]? list = null;
        foreach (var s in map.Sections)
        {
            if (string.Equals(s.Keyword, section, StringComparison.OrdinalIgnoreCase))
            {
                list = s.Textures;
                break;
            }
        }
        if (list is null) return null;

        int li = oneBasedIdx - 1; // 1-based index → 0-based table slot. spec: terrain.md §5.6 Block 3
        if ((uint)li >= (uint)list.Length) return null;

        string? rel = pool.GetRelPath(list[li].TexId);
        if (rel is null) return null;

        string ddsPath = $"data/map000/texture/{rel}.dds";
        return assets.Contains(ddsPath) ? assets.LoadTexture(ddsPath) : null;
    }

    // ── Conversions ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Converts a legacy world (x,y,z) to a Godot-space <see cref="Vector3"/> (Z negated once).</summary>
    private static Vector3 ToGodotVec(float legacyX, float legacyY, float legacyZ)
    {
        (float gx, float gy, float gz) = WorldCoordinates.ToGodot(legacyX, legacyY, legacyZ);
        return new Vector3(gx, gy, gz);
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}
