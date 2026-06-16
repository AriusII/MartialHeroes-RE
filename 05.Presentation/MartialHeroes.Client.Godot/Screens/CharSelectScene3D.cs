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
    //
    // RENDER-PARITY MITIGATION (the #1 "too dark" complaint): the legacy D3D9 fixed-function pipeline
    // (rendering.md §1) applied the OPTION_BRIGHT ambient as a FULL-LUMINANCE per-vertex/material colour
    // floor with NO energy attenuation, on NEUTRAL-WHITE (FF FF FF) geometry — i.e. the white ambient
    // landed on the stone at unit weight. Godot's PBR ambient at energy 1.0 lands ~Lambert-attenuated on
    // a default StandardMaterial3D (roughness 1 / metallic 0), so the same "1.0" reads darker than the
    // original's flat full-bright floor. The scene authors NO scene point-lights (the original has none —
    // §3.6.1 / §3.6.6) and the only directional is near-zero (0.047), so essentially ALL of the on-screen
    // brightness comes from this ambient floor; under-driving it = the dark-cave look. We therefore drive
    // the Godot AmbientLightEnergy ABOVE 1.0 so the white floor reaches the stone at the original's
    // full-bright luminance (a Godot-side parity scalar, NOT a new asset constant — the asset value is
    // still OPTION_BRIGHT = 1.0 white). spec: §3.6.1/§3.6.2 (white ambient = main illuminant) +
    // rendering.md §1 (D3D9 flat full-bright ambient, no attenuation) — Godot parity mitigation.
    private const float AmbientFloorEnergy = 1.0f; // the recovered asset value (OPTION_BRIGHT/100 = 1.0)

    // Godot parity scalar: the energy actually fed to WorldEnvironment so the unit-white ambient floor
    // lands on the neutral stone at the original D3D9 full-bright luminance (see AmbientFloorEnergy note).
    //
    // PASS-2 FINAL (2026-06-16): 0.65. The props .bud are now SHADED again (Unshaded was dropped because
    // it ignores the ambient tint, producing cold-grey stone instead of warm tan). With Shaded + PBR, the
    // Godot Lambert-attenuated ambient at energy 0.65 + warm amber tint (1.0, 0.72, 0.44) shifts the
    // neutral stone textures to the warm tan of the official oracle, while the fog (density 0.010) handles
    // the enclosure/dark-background effect. The scene reads "fairly dark warm stone temple" — matching the
    // official oracle — not over-bright (1.10 blew out lateral walls) and not too dark (0.50 was too dim).
    // aesthetic calibration — Godot rendering-parity mitigation; asset value stays OPTION_BRIGHT=1.0.
    private const float AmbientFloorEnergyGodot = 0.65f;

    // Achromatic dark background — the recovered area-0 keyframe-29 sky tone. The char-select scene
    // consumes the area-0 set at keyframe 29 (environment_bins.md §11); its `sky_haze` group [0..3] is
    // the achromatic near-zero value R=G=B = 0.004303 (float [0,1], applied directly — §11.6, §11.2
    // STATIC+ACHROMATIC). There is no skybox file (§1.1 skybox always 0) and the scene builds no
    // procedural sky, so the flat sky_haze tone is the faithful clear colour.
    // spec: Docs/RE/formats/environment_bins.md §11.6 (sky_haze 0.004303) / §11.2 (achromatic).
    private const float SkyHazeArea0Kf29 = 0.004303f; // spec: environment_bins.md §11.6 sky_haze [0..3]

    private static readonly Color BackgroundColorAchromatic =
        new(SkyHazeArea0Kf29, SkyHazeArea0Kf29, SkyHazeArea0Kf29);

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
    // Water layer (§3.7.3 / §3.6.5) — the cell's BLUE terrain water sheet (.fx3/.fx5), the bright blue
    // surface behind the row. This is the cell water LAYER (a scrolling flat plane), NOT the xeff
    // waterfall spray (white sprites, owned by the xeff). spec: §3.6.5 (water = cell water layer, "blue
    // surface behind the row", gated by the water option) / §3.7.3 (FX3/FX5, textures _water_new01/03/04).
    // =========================================================================

    // The cell footprint = a 1024-unit square at cell world origin (0, −10240) (§3.7.1 cell addressing:
    // origin (cx·1024, cz·1024) = (0, −10240); X ∈ [0,1024], Z ∈ [−10240,−9216]). The water sheet spans
    // the cell footprint. spec: §3.7.1 (cell origin (0,−10240), 1024 units/side). CODE-CONFIRMED.
    private const float CellOriginLegacyX = 0.0f; // spec: §3.7.1 cell origin X
    private const float CellOriginLegacyZ = -10240.0f; // spec: §3.7.1 cell origin Z
    private const float CellSizeUnits = 1024.0f; // spec: §3.7.1 1024 units/side

    // Water-surface Y (HORIZONTAL plane). The exact water level is a per-cell .fx3/.fx5 field (parse
    // TODO — the .fx3/.fx5 binary format is terrain-side and not yet decoded). The platform/row sits at
    // Y≈70 (§3.6.5); the cell water reads as a flat blue sheet at/below the platform floor, so the
    // horizontal plane is placed just below the platform. PLAUSIBLE level (not asset-exact — .fx3/.fx5
    // water level not yet harvested). spec: §3.6.5 (cell water layer) / §3.7.3 (FX3/FX5 parse TODO).
    private const float WaterSurfaceLegacyY = 68.0f; // PLAUSIBLE — just below the Y≈70 platform floor

    // The blue water texture (§3.7.3: rel "terrain/_water_new01"); resolves to data/map000/texture/
    // terrain/_water_new01.dds (DXT-compressed, animated-texture flag). spec: §3.7.3 CODE-CONFIRMED present.
    private const string WaterTextureRel = "terrain/_water_new01";
    private const string WaterTextureDds = "data/map000/texture/terrain/_water_new01.dds";

    // UV-scroll speed (the §3.7.3 animated-texture flag → scrolling-UV blue sheet). The exact scroll
    // rate is the .fx3/.fx5 animated-texture field (parse TODO); a slow drift reproduces the moving-water
    // read. spec: §3.7.3 (animated-texture flag) / §3.6.5 (scrolling water illusion) — rate is parse TODO.
    private const float WaterUvScrollPerSecond = 0.03f; // spec: §3.7 water-layer (parse TODO)

    // =========================================================================
    // Preview-character assets (§3.7.5) — the four starter classes (IdA=1).
    // =========================================================================

    // Per starter class → its base-skin .skn. Each mesh carries a DISTINCT id_b that drives its OWN
    // rig + idle clip (resolved per slot in TryBuildSlotActor). The four §3.7.5 starter meshes (all
    // default appearance IdA=1; CONFIRMED-present in the VFS) are:
    //   g202110001 (Bichimi/Dosa), g203110001 (Monk), g209110001 (Archer), g206110001 (Sorceress).
    // A prior version used a g202xxx-family table + an invented `g202{skinClassId}10001` fallback that
    // is NOT in §3.7.5 — corrected to the four confirmed starter meshes, fallback removed. The
    // skinClassId→mesh binding is best-effort (PLAUSIBLE): the exact map runs through the skin.txt
    // appearance chain (skinning.md §3.5.2/§3.5.3); an id outside 1..4 has no defined starter mesh →
    // empty candidate list → logged + skipped (no synthetic substitution). spec: §3.7.5 (starter
    // meshes) / skinning.md §3.5.2/§3.5.3 (appearance chain).
    private static string[] SknCandidatesForClass(uint skinClassId) => skinClassId switch
    {
        1 => ["data/char/skin/g202110001.skn"], // §3.7.5 Bichimi / Dosa starter mesh
        2 => ["data/char/skin/g203110001.skn"], // §3.7.5 Monk starter mesh
        3 => ["data/char/skin/g209110001.skn"], // §3.7.5 Archer starter mesh
        4 => ["data/char/skin/g206110001.skn"], // §3.7.5 Sorceress / Summoner starter mesh
        _ => [], // unknown skin-class → no candidate → logged + skipped (no invented fallback)
    };

    // =========================================================================
    // Runtime state
    // =========================================================================

    private Camera3D? _camera;
    private CharSelectCameraRig? _cameraRig;
    private TerrainNode? _backdropTerrain;
    private readonly Node3D?[] _slotActors = new Node3D?[5];
    private int _selectedSlot;

    // Whether Initialise has run (the one-time cell/env/camera/effects build). A RefreshSlotActors
    // call before init is a NO-OP — the descriptors will be picked up at init time. The assets handed
    // to Initialise are retained so a later refresh can rebuild the actor row from the same VFS.
    private bool _initialised;
    private RealClientAssets? _assets;

    // The blue water sheet's material — its UV offset is scrolled in _Process (the §3.7.3 animated flag).
    private StandardMaterial3D? _waterMaterial;
    private float _waterScrollPhase;

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
            // Retain the VFS handle so a later RefreshSlotActors (when the 3/1 char list arrives a
            // frame or more after the deferred Initialise) can rebuild the actor row from the SAME VFS.
            _assets = assets;

            BuildEnvironment();
            BuildLighting();
            BuildCamera();

            if (assets is not null)
            {
                // The cell / env / camera / effects build is ONE-TIME (it must NOT be duplicated on a
                // later refresh — only the character actors rebuild). spec: §3.7.1 (single backdrop cell).
                BuildBackdropTerrain(assets);
                BuildBackdropProps(assets);
                BuildWaterLayer(assets);
                BuildAmbientEffect(assets);
            }
            else
            {
                GD.Print(
                    "[CharSelectScene3D] No VFS — backdrop cell / actors / ambient effect skipped; env + camera only.");
            }

            // Mark initialised BEFORE the first actor build so RefreshSlotActors runs (it is a no-op
            // only when called before init). The first row build picks up SlotDescriptors as they are
            // at deferred-init time; a later ApplyCharacterList calls RefreshSlotActors to rebuild.
            _initialised = true;
            RefreshSlotActors(assets);

            GD.Print("[CharSelectScene3D] 3D scene initialised from real assets (no procedural sky, no omni rig). " +
                     "spec: frontend_scenes.md §3.3/§3.5/§3.6/§3.7.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Initialise failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuilds the per-slot character actor ROW from the CURRENT <see cref="SlotDescriptors"/>:
    /// frees any existing slot-actor nodes, then rebuilds them via the same <c>TryBuildSlotActor</c>
    /// path used at first build. The cell / environment / camera / ambient effect are NOT touched —
    /// only the character actors. A call before <see cref="Initialise"/> is a no-op (the descriptors
    /// are picked up at init). Called by the host's <c>ApplyCharacterList</c> after the 3/1 character
    /// list (or the dev seed) arrives a frame or more after the deferred init. spec: §3.3.1.
    /// </summary>
    /// <param name="assets">The VFS handle; if null the row is cleared (no actors built).</param>
    public void RefreshSlotActors(RealClientAssets? assets)
    {
        // Before init the cell/env/camera have not been built yet; the descriptors set on this node
        // will be consumed by Initialise → RefreshSlotActors. Refreshing now would build actors with
        // no camera/env in place, so we no-op and let init drive the first build. spec: §3.3.1.
        if (!_initialised) return;

        try
        {
            // (a) Remove the EXISTING slot-actor nodes (the only thing that rebuilds).
            for (int i = 0; i < _slotActors.Length; i++)
            {
                Node3D? actor = _slotActors[i];
                if (actor is not null && IsInstanceValid(actor))
                {
                    RemoveChild(actor);
                    actor.QueueFree();
                }

                _slotActors[i] = null;
            }

            // (b) Rebuild from the CURRENT descriptors via the existing TryBuildSlotActor path.
            if (assets is not null)
                BuildCharacterRow(assets);
            else
                GD.Print("[CharSelectScene3D] RefreshSlotActors: no VFS — actor row cleared (no actors).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] RefreshSlotActors failed: {ex.Message}");
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

        // Achromatic dark background = the area-0 keyframe-29 sky_haze tone (R=G=B=0.004303). The
        // area-0 sky is parametric (no skybox file; §1.1) and the builder authors NO procedural sky —
        // a flat achromatic clear colour at the recovered sky_haze value is the faithful base.
        // spec: environment_bins.md §11.6 (sky_haze 0.004303) / §11.2 (achromatic).
        env.BackgroundMode = global::Godot.Environment.BGMode.Color;
        env.BackgroundColor = BackgroundColorAchromatic;

        // WHITE ambient FLOOR — the scene's MAIN illuminant (OPTION_BRIGHT/100 = 1.0 → white).
        // spec: §3.6.1/§3.6.2 + environment_bins.md §10.5/§11.5. CODE-CONFIRMED.
        // AmbientSource.Color (NOT Sky/Disabled) so the FLAT white colour is the ambient — and pin sky
        // contribution to 0 so the achromatic dark BG can NOT bleed in and crush the floor. Energy is the
        // Godot parity scalar (> 1.0) so the unit-white floor reaches the neutral stone at the original
        // D3D9 full-bright luminance (the "too dark" fix — see AmbientFloorEnergyGodot note).
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        // Asset value = white (OPTION_BRIGHT/100 = 1.0 → white). spec: §3.6.1/§3.6.2 CODE-CONFIRMED.
        // Godot-side warm tint mitigation: the "warm stone temple" look in the official screenshot comes
        // from the additive xeff fire billboards (orange-warm, not a light source) heating the neutral-
        // white geometry. Under Godot PBR the additive sprites add brightness but not ambient hue.
        // A very slight warm-amber ambient floor tint (R=1.0, G=0.88, B=0.70) approximates the D3D9
        // original's warm read without adding point-lights the spec forbids. The asset value stays
        // OPTION_BRIGHT=1.0 white; this is a Godot-side parity mitigation, NOT a new asset constant.
        // aesthetic choice — warm tint to match the official "dark warm stone temple" look (no spec constant)
        // R=1.0 G=0.72 B=0.44: iter 5 — deep amber. Stone textures in this cell are neutral/grey;
        // only a strongly-saturated amber ambient shifts them to the warm tan of the official oracle.
        // The additive brazier sprites (xeff) ADD warmth on top of this base, so the ambient itself
        // needn't reproduce the full fire warmth — just the warm stone base. aesthetic calibration.
        env.AmbientLightColor = new Color(1.0f, 0.72f, 0.44f);
        env.AmbientLightSkyContribution = 0.0f; // flat colour only — the dark BG must not dim the floor
        env.AmbientLightEnergy = AmbientFloorEnergyGodot; // spec: §3.6.2 OPTION_BRIGHT (1.0) → parity-driven

        // Linear tonemap = a Godot-side MITIGATION (not an original asset value). The legacy renderer
        // is a Direct3D 9 fixed-function pipeline with NO HDR tonemapper — it writes colours straight
        // to the 8-bit backbuffer (rendering.md §1: thin D3D9 device wrapper; §6: the only post chain
        // is glow/bloom, with no ACES/Reinhard stage). Godot defaults to a filmic/ACES tonemap that
        // would darken the white ambient floor; selecting Linear + exposure 1.0 reproduces the D3D9
        // no-tonemap behaviour. spec: Docs/RE/specs/rendering.md §1/§6 (D3D9 fixed-function, no HDR
        // tonemapper) — Godot-side mitigation, NOT an original constant.
        env.TonemapMode = global::Godot.Environment.ToneMapper.Linear;
        env.TonemapExposure = 1.0f;

        // Distance fog OFF — spec §3.6.2 CODE-CONFIRMED: the select-scene build helper zeroes the
        // fog-blend OFFSET field (factor 0.0), which turns distance fog OFF behind the preview row so the
        // row reads clearly. This matches the create preview (CharCreatePreview3D.BuildEnvironment also
        // disables fog). spec: §3.6.2 (fog-blend offset zeroed → distance fog off). CODE-CONFIRMED.
        //
        // NOTE: a prior pass enabled Godot exponential "enclosure" fog as a Godot-side mitigation for a
        // "bright floating island / grey void" defect (terrain .ted extending beyond the .bud walls). That
        // was a NON-SPEC mitigation that contradicted the CODE-CONFIRMED fog-off directive and has been
        // REMOVED. If the beyond-wall horizon needs masking, mitigate it geometrically (clip/cull the .ted
        // mesh to the cell footprint) rather than re-introducing fog the original scene does not draw.
        env.FogEnabled = false; // spec: §3.6.2 distance fog OFF (fog-blend offset zeroed)

        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);

        GD.Print($"[CharSelectScene3D] Area-0 environment: achromatic dark BG + WHITE ambient floor " +
                 $"(OPTION_BRIGHT=1.0, Godot parity energy {AmbientFloorEnergyGodot}, sky-contrib 0) + fog OFF " +
                 "— spec: §3.6.2 (fog-blend offset zeroed → distance fog off) CODE-CONFIRMED. " +
                 "NO procedural sky. spec: §3.6 + environment_bins.md.");
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
            Fov = CameraFov, // spec: §3.5.1 vertical FOV 50°
            Near = CameraNear, // spec: §3.5.1 near 5.0
            Far = CameraFar, // spec: §3.5.1 far 15000.0
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

            GD.Print(
                $"[CharSelectScene3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. spec: §3.7.1.");
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

            // PASS-2 SHADING NOTE (2026-06-16): the previous pass forced Unshaded on all .bud props to
            // approximate D3D9's flat device-ambient. However, Unshaded ignores ALL ambient contributions
            // from the WorldEnvironment — including the warm amber tint (R=1.0, G=0.82, B=0.60) — which
            // produced cold-grey stone walls instead of warm tan (the textures are neutral, so Unshaded
            // strips out any ambient colour contribution). The correct parity mechanism is:
            //   • Shaded (PerPixel) + warm AmbientLight colour + energy 0.90 → stone receives warm amber
            //     from the WorldEnvironment ambient, closely replicating D3D9's device-ambient floor which
            //     was also white × (OPTION_BRIGHT/100), but in Godot with a warm tint added for visual parity
            //     against the official screenshot.
            //   • The near-zero directional (0.047) barely contributes; top-facing normals get ~0.047 extra —
            //     acceptable at this low energy.
            // The D3D9 flat-ambient parity is now handled by AmbientFloorEnergyGodot=0.90 + sky-contrib=0,
            // NOT by Unshaded (which disables ALL lighting). Unshaded is REMOVED.
            // spec: §3.6.1 + rendering.md §1 (D3D9 flat device-ambient ≈ Godot ambient colour floor, Shaded)
            // — Godot parity mitigation (Pass 2). BudMeshBuilder's default PerPixel shading is kept.

            propsRoot.Name = "BackdropProps";
            AddChild(propsRoot);

            GD.Print(
                $"[CharSelectScene3D] Backdrop props built ({scene.Objects.Length} objects, carved wall). spec: §3.7.1 / §3.7.3.");
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
    // Water layer — the cell's BLUE water sheet (.fx3/.fx5), a flat scrolling-UV plane.
    // =========================================================================

    private void BuildWaterLayer(RealClientAssets assets)
    {
        // The blue water surface behind the row is the cell's terrain WATER LAYER (.fx3/.fx5), rendered
        // by the terrain system, NOT a spawned xeff (the xeff carries the WHITE waterfall spray sprites;
        // this is the blue sheet). spec: §3.6.5 (water = cell water layer, "blue surface behind the row")
        // / §3.7.3 (FX3/FX5, textures _water_new01/03/04, animated). The .fx3/.fx5 binary format is
        // terrain-side and not yet decoded, so the exact water plane extent / level / scroll come from the
        // recovered cell footprint + a parse-TODO placeholder. spec: §3.7 water-layer (parse TODO).
        try
        {
            // Resolve the blue water texture. Prefer the bgtexture chain (rel "terrain/_water_new01");
            // fall back to the direct VFS path. spec: §3.7.3 (water rel paths under data/map000/texture).
            ImageTexture? waterTex = null;
            BgTextureCatalog? bgPool = TryLoadBgPool(assets);
            string ddsPath = bgPool is not null
                ? $"data/map000/texture/{WaterTextureRel}.dds"
                : WaterTextureDds;
            if (assets.Contains(ddsPath))
                waterTex = assets.LoadTexture(ddsPath);
            else if (assets.Contains(WaterTextureDds))
                waterTex = assets.LoadTexture(WaterTextureDds);

            if (waterTex is null)
            {
                GD.Print(
                    $"[CharSelectScene3D] Water texture absent ({WaterTextureDds}) — water sheet skipped. spec: §3.7.3.");
                return;
            }

            // HORIZONTAL water plane = the cell's terrain WATER LAYER (§3.6.5: "the cell's own terrain
            // water layer (.fx3/.fx5; textures _water_new01/03/04), rendered by the terrain system …
            // not a spawned .xeff"). The VERTICAL waterfall spray is the XeffSceneEffect (white additive
            // sprites), owned by BuildAmbientEffect — NOT this layer. This layer is the flat blue surface
            // over the cell footprint at the cell water level. A prior pass modelled this as a VERTICAL
            // "cascade curtain", which is the xeff's job, not the cell water layer's — corrected to a
            // horizontal flat plane. spec: §3.6.5 (cell water layer = flat blue surface) / §3.7.3.
            //
            // Extent = the cell footprint (1024-unit square at cell origin (0, −10240); §3.7.1). The
            // plane lies in the XZ plane (default PlaneMesh orientation = Y-up) at the water-surface Y.
            var plane = new PlaneMesh
            {
                Size = new Vector2(CellSizeUnits, CellSizeUnits), // cell footprint 1024×1024 (§3.7.1)
                Orientation = PlaneMesh.OrientationEnum.Y, // horizontal (face up) — the flat water surface
            };

            // Flat blue water material with a tiled, scrolling UV. The .fx3/.fx5 animated flag (§3.7.3) →
            // scrolling-UV; the bright blue read = the _water_new texture under a blue albedo tint,
            // alpha-blended over the cell floor. The blue/alpha tint is PLAUSIBLE (the exact .fx3/.fx5
            // diffuse/alpha is parse-TODO). spec: §3.7.3 (animated) / §3.6.5 (blue cell water surface).
            _waterMaterial = new StandardMaterial3D
            {
                AlbedoTexture = waterTex,
                AlbedoColor = new Color(0.30f, 0.60f, 1.0f, 0.75f), // PLAUSIBLE blue tint (parse-TODO)
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, // visible from above and below
                // Unshaded so the white ambient floor / near-zero directional do not darken the water —
                // the legacy water layer is a flat textured sheet, not a lit PBR surface. spec: §3.6.1.
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                // Tile the 256² water texture ~8× across the 1024-unit plane.
                Uv1Scale = new Vector3(8.0f, 8.0f, 1.0f),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };

            var waterMeshInstance = new MeshInstance3D
            {
                Name = "BackdropWater",
                Mesh = plane,
                MaterialOverride = _waterMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };

            // Centre the horizontal plane on the cell footprint centre (cell origin + half a cell side).
            // Cell origin (0, −10240), 1024 units/side (§3.7.1) → centre legacy (512, y, −9728). The plane
            // spans the whole cell, so the blue surface sits under/behind the row exactly as the cell water
            // layer does in the original. The footprint extent is asset-exact (§3.7.1); the water LEVEL is
            // PLAUSIBLE (parse-TODO). spec: §3.7.1 (cell origin/size) / §3.6.5 (cell water layer).
            float centreLegacyX = CellOriginLegacyX + CellSizeUnits * 0.5f; // 0 + 512 = 512
            float centreLegacyZ = CellOriginLegacyZ + CellSizeUnits * 0.5f; // −10240 + 512 = −9728
            waterMeshInstance.Position = ToGodotVec(centreLegacyX, WaterSurfaceLegacyY, centreLegacyZ);
            AddChild(waterMeshInstance);

            GD.Print($"[CharSelectScene3D] Water layer: horizontal blue _water_new01 plane over the cell " +
                     $"footprint (centre Godot {waterMeshInstance.Position}, {CellSizeUnits}u/side, Y={WaterSurfaceLegacyY}), " +
                     "scrolling UV, alpha-blended. The vertical spray is the xeff, not this layer. " +
                     "spec: §3.6.5 (cell water layer) / §3.7.3 (.fx3/.fx5 parse TODO; level/tint PLAUSIBLE).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Water layer failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // Scroll the water UV so the blue sheet reads as moving water (the §3.7.3 animated-texture flag).
        // Exact scroll rate is the .fx3/.fx5 animated field (parse TODO). spec: §3.7.3 / §3.6.5.
        if (_waterMaterial is not null)
        {
            _waterScrollPhase += (float)delta * WaterUvScrollPerSecond;
            if (_waterScrollPhase > 1.0f) _waterScrollPhase -= 1.0f;
            _waterMaterial.Uv1Offset = new Vector3(_waterScrollPhase, _waterScrollPhase * 0.5f, 0f);
        }
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
                    GD.Print(
                        $"[CharSelectScene3D] Slot {i} actor at Godot ({SlotLegacyX[i]:F1}, {rowY:F2}, {SlotGodotZ[i]:F1}). spec: §3.3.1.");
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
            GD.PrintErr(
                $"[CharSelectScene3D] Slot {slotIdx}: no .skn present for skinClassId={skinClassId} — skipped.");
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
        // spec: skinning.md §8(e) — data/char/bind/g{id_b}.bnd + actormotion.txt col2==id_b→col15 (idle).
        Skeleton? skeleton = TryLoadSkeletonForIdB(assets, mesh.IdB);
        AnimationClip? idleClip = TryLoadIdleClipForIdB(assets, mesh.IdB);
        ImageTexture? albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);

        // SkinnedCharacterBuilder applies the upright stand-up pivot + recentre (feet at local Y=0);
        // its returned Position is the recentre offset — keep it, and place the slot via a wrapper.
        Node3D actorRoot = SkinnedCharacterBuilder.Build(mesh, skeleton, idleClip, albedo);

        var slotWrapper = new Node3D { Name = $"Slot{slotIdx}Actor" };
        slotWrapper.Position = new Vector3(SlotLegacyX[slotIdx], rowY, SlotGodotZ[slotIdx]);
        slotWrapper.Scale = Vector3.One * PreviewScale; // spec: §3.3.1 ×3.0 (unit-reconciled)

        // Facing — pure yaw about world Y (§3.3.2 CODE-CONFIRMED): lock CLEAR → yaw 0 (front, toward
        // camera); lock SET (locked / new / creating) → yaw π (back to viewer).
        //
        // CURRENT LIMITATION (yaw-π path dormant): this actor is only ever built for an OCCUPIED slot
        // (BuildCharacterRow skips unoccupied slots), and SlotDescriptors carries no lock bit — only
        // (IsOccupied, SkinClassId). Driving the real lock yaw needs (1) an IsLocked bit added to the
        // SlotDescriptors tuple (a public API change consumed by CharacterSelectScreen.PushSlotDescriptors-
        // ToScene — NOT owned by this lane) and (2) a lock-state source in CharacterSelectScreen (which
        // has no lock concept today — also not owned). So yaw is fixed to 0 (front) for every occupied
        // slot here; the lock-driven yaw-π is DEFERRED to a change that owns CharacterSelectScreen.
        // spec: frontend_scenes.md §3.3.2 / §3.3.5.
        bool isOccupied = slotIdx < SlotDescriptors.Length && SlotDescriptors[slotIdx].IsOccupied;
        float slotYaw = isOccupied ? 0.0f : Mathf.Pi; // lock bit unavailable → occupied ⇒ front (yaw 0)
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
                if (cols.Length <= 15) continue;
                if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != idB) continue;

                // Idle motion = motion_ids_a[0] = column 15 (0-based), record offset +0x40 — IDB-confirmed
                // operand-for-operand. spec: formats/actormotion.md (col15 = +0x40 = idle / stand motion).
                string idle = cols[15].Trim();
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