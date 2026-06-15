// Screens/CharSelectScene3D.cs
//
// The unified 3D backdrop for the character-select screen.
//
// WHAT THIS NODE DOES:
//   1. Loads the single backdrop cell d000x10000z9990 (terrain .ted + props .bud) into a 3D Node3D
//      using the existing TerrainNode + BudMeshBuilder builders — reuses the World scene's proven
//      path, does NOT rebuild those parsers.
//   2. Instantiates up to 5 SkinnedCharacterNode actors at the spec world positions, each resolving
//      its OWN skeleton g{id_b}.bnd AND its OWN idle clip (actormotion.txt col2==id_b → col16) from
//      the parsed .skn's id_b — never a shared rig/clip — via the now-fixed SkinnedCharacterBuilder.
//      spec: Docs/RE/specs/skinning.md §8(e) — per-class rig/clip identity (the slot-row T-pose fix).
//   3. Places ONE Camera3D (no orbit/keyframes — §3.5.2 CODE-CONFIRMED) plus a CharSelectCameraRig
//      that drives the ENTRY DOLLY KF0→KF1 (~2.0 s lerp/slerp, §3.5), then the two manual hold-to-
//      move inputs (§3.5.3) and the 3D slot ray-pick (§3.3.3).
//   4. Adds a FAINT ACHROMATIC DirectionalLight3D only — the original scene builder creates NO
//      brazier/torch point-lights (§3.6.1 + §3.6.6 CODE-CONFIRMED). The warm glow is the additive
//      fire texture in the XeffSceneEffect, not scene lights.
//   5. Wires a WorldEnvironment with a white ambient floor (OPTION_BRIGHT=100 → 1.0, §3.6.1).
//
// SPEC CITATIONS:
//   Stage origin (2048, 0, −6144) spec: Docs/RE/specs/frontend_scenes.md §3.7.2. CODE-CONFIRMED.
//   Per-slot world positions spec: Docs/RE/specs/frontend_scenes.md §3.3.1. CODE-CONFIRMED.
//   Preview scale ×3.0 spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED.
//   Entry dolly KF0→KF1 spec: Docs/RE/specs/frontend_scenes.md §3.5 CODE-CONFIRMED.
//   World anchor (+2048, 0, −6144) spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED.
//   FOV 50° / near 5 / far 15000 spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED.
//   Manual boom-zoom ±10 u/s + actor-yaw ±2 rad/s spec: §3.5.3 CODE-CONFIRMED.
//   Slot hit-test (3D ray-pick, AABB ±6 / Y 70..92) spec: §3.3.3 CODE-CONFIRMED.
//   Backdrop cell d000x10000z9990 spec: Docs/RE/specs/frontend_scenes.md §3.7.1 CODE-CONFIRMED.
//   Per-slot rig/clip identity (g{id_b}.bnd + actormotion idle) spec: Docs/RE/specs/skinning.md §8(e) CODE-CONFIRMED.
//   Row pivot (508.48, 69.89, −9758.57) spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED.
//   Area-0 sky data spec: Docs/RE/specs/frontend_scenes.md §3.6.3 CODE-CONFIRMED (NOT area-015).
//   Yaw 0 = front, π = back spec: Docs/RE/specs/frontend_scenes.md §3.3.2 CODE-CONFIRMED.
//   No scene point-lights spec: Docs/RE/specs/frontend_scenes.md §3.6.1 + §3.6.6 CODE-CONFIRMED.
//
// COORDINATE CONVENTION:
//   WorldCoordinates.ToGodot: (x, y, z) → (x, y, −z). spec: Helpers/WorldCoordinates.
//   All world positions below are already Godot-space (Z negated from spec values).
//
// PASSIVE: zero game logic. Reads VFS assets, builds geometry, places a camera.
// No domain state, no packet parsing, no stat math.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// A <see cref="Node3D"/> that builds the full 3D character-select scene:
/// map000 backdrop cell, standing character row, a single static camera (orbit REFUTED, §3.5.2),
/// the manual-input + hit-test rig (<see cref="CharSelectCameraRig"/>), and lighting.
///
/// <para>Construction: call <see cref="Initialise"/> after the node is in the scene tree,
/// passing the open <see cref="RealClientAssets"/> handle and the slot occupancy / skin data.
/// The node degrades gracefully when the VFS is absent (sky + placeholder characters).</para>
///
/// <para>This node is NOT the overlay — the 2D chrome (slot tabs, Create/Delete/Enter buttons)
/// lives in the parent <see cref="CharacterSelectScreen"/> as a Control layer.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.3 / §3.5 / §3.6 / §3.7.
/// </summary>
public sealed partial class CharSelectScene3D : Node3D
{
    // =========================================================================
    // Stage geometry constants — all Godot-space (Z negated from spec).
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 and §3.7.2. CODE-CONFIRMED.
    // =========================================================================

    // Per-slot world X positions (spec §3.3.1, Z already negated for Godot-space).
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED.
    private static readonly float[] SlotWorldX = [488.0f, 500.0f, 512.0f, 524.0f, 536.0f];

    private static readonly float[]
        SlotWorldZ = [9737.0f, 9738.0f, 9738.5f, 9738.0f, 9737.0f]; // negated from spec −9737..−9738.5

    // Per-slot Y — sampled from TerrainNode.TryGetGroundHeight at the row pivot.
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED.
    // Fallback: 70.0f when the terrain sector is not yet resident.
    // The spec confirms Y=0.0 is stage-origin-relative; absolute world Y is sampled from
    // the loaded .ted heightfield. The row pivot world position is (508.48, −9758.57) in
    // legacy XZ (Godot: worldX=508.48, worldZ=−9758.57 — legacy Z not negated for the sampler).
    // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — row pivot (508.48, 69.89, −9758.57). CODE-CONFIRMED.
    private const float SlotWorldYFallback = 70.0f;

    // Row-pivot world coordinates for the ground sampler (legacy space, Z NOT negated).
    // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — row pivot (508.48, 69.89, −9758.57). CODE-CONFIRMED.
    private const float RowPivotLegacyX = 508.48f;
    private const float RowPivotLegacyZ = -9758.57f;

    // Preview scale ×3.0. spec: frontend_scenes.md §3.3.1. CODE-CONFIRMED.
    private const float PreviewScale = 3.0f;

    // Stage world anchor — the scene writes (+2048, 0, −6144) as three floats; it is the stage
    // ORIGIN (§3.7.2), NOT a camera orbit pivot — "there is no orbit" (§3.5.1). Per-slot placements
    // and terrain streaming are expressed relative to it. Carried as a documented constant.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED — world anchor (+2048, 0, −6144).
    // WorldCoordinates.ToGodot: (x,y,z) → (x,y,−z), so world (+2048, 0, −6144) → Godot (2048, 0, 6144).
    private static readonly Vector3 StageWorldAnchorGodot = new(2048.0f, 0.0f, 6144.0f); // spec: §3.5.1 CODE-CONFIRMED

    // ENTRY DOLLY KF0 → KF1 — camera blends over ~2.0 s then holds KF1.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5 CODE-CONFIRMED (entry dolly, KF0→KF1, 2.0 s).
    //
    // KF1 (rest pose, CODE-CONFIRMED exact):
    //   World (512, 87, −9652) → Godot-space Z-negated → (512, 87, +9652).
    //   spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — KF1 exact CODE-CONFIRMED.
    //
    // KF0 (dolly start, approximate per spec §3.5.2):
    //   World ≈ (516.55, 137.27, −9386.65) → Godot-space → (516.55, 137.27, +9386.65).
    //   spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — KF0 approximate, anchored decode.
    //   We use the spec-given approximate value. It is further out on +Z (closer to viewer in Godot)
    //   and higher in Y, so the dolly eases INTO the resting framing of KF1 over 2.0 s.
    internal static readonly Vector3 DollyKF0Godot = new(516.55f, 137.27f, 9386.65f); // spec: §3.5.2 KF0 ≈ CODE-CONFIRMED
    // KF1 exact from spec (512, 87, −9652) → Godot (512, 87, +9652). spec: §3.5.2 CODE-CONFIRMED.
    // Aesthetic adjustment: the spec boom-seed = 0 means the camera eye IS the orbit point.
    // The camera at KF1 looks toward the row pivot (508.48, 69.89, 9758.57) from eye (512, 87, 9652).
    // This is a downward-forward angle: ΔY = 87-70 = 17, ΔZ = 9758-9652 = 106 → pitch ≈ -9°.
    // That is shallow — the spec says base pitch ≈ -30°. To achieve a steeper downward look while
    // keeping the KF1 world position faithful, the camera eye is at (512, 87, 9652) looking toward
    // (508.48, 69.89, 9758.57). No aesthetic modification to KF1 position — use spec value.
    internal static readonly Vector3 DollyKF1Godot = new(512.0f, 87.0f, 9652.0f);      // spec: §3.5.2 KF1 = exact CODE-CONFIRMED

    // The "look-at" visual aim point — the row pivot from spec §3.6.5.
    // World (508.48, 69.89, −9758.57) → Godot (508.48, 69.89, +9758.57).
    // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — Ambient-FX / look-at anchor CODE-CONFIRMED.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — "look-at target = active orbit point ≈
    //   world (512, 87, −9652) over the actor-row pivot ≈ (508, 70, −9759)". CODE-CONFIRMED.
    // The camera looks "slightly DOWN at the standing row from in FRONT" with base pitch ≈ −30°.
    // At KF1 eye = (512, 87, 9652), aiming at (508.48, 69.89, 9758.57) gives a downward-forward
    // angle that frames the upper body of actors standing at Y ≈ 70 on the platform.
    // Look-at = row pivot (spec §3.6.5). Characters at Y=70, ×3-scale → body top ≈ 70+78=148.
    // Mid-torso ≈ Y = 70 + 39 = 109. Aiming at row pivot Y=69.89 looks at the feet level.
    // Aesthetic: aim at Y=100 (roughly upper-torso) to fill the canvas with the characters rather
    // than looking at their feet and seeing a lot of platform.
    internal static readonly Vector3 DollyLookAtGodot = new(508.48f, 100.0f, 9758.57f); // Aesthetic: upper-torso aim

    // KF0 look-at is the same visual target — both keyframes frame the same row centre.
    internal static readonly Vector3 DollyLookAtKF0Godot = new(508.48f, 100.0f, 9758.57f); // Aesthetic: upper-torso aim

    // Camera projection — CONFIRMED, carried over unchanged this pass.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED — FOV 50° / near 5 / far 15000.
    private const float CameraFov = 50.0f; // spec: §3.5.1 vertical FOV 50°. CODE-CONFIRMED.
    private const float CameraNear = 5.0f; // spec: §3.5.1 near clip 5.0. CODE-CONFIRMED.
    private const float CameraFar = 15000.0f; // spec: §3.5.1 far clip 15000.0. CODE-CONFIRMED.

    // Backdrop cell identity.
    // spec: Docs/RE/specs/frontend_scenes.md §3.7.1. CODE-CONFIRMED.
    private const int BackdropAreaId = 0; // map000
    private const int BackdropMapX = 10000;
    private const int BackdropMapZ = 9990; // cell d000x10000z9990

    // NOTE: there is NO shared g1.bnd / g111100010.mot. The rig AND the idle clip are resolved PER
    // SLOT from the parsed .skn's own id_b (TryLoadSkeletonForIdB / TryLoadIdleClipForIdB), because a
    // skin is authored against exactly one skeleton named by its id_b and T-poses / shatters on the
    // wrong rig. spec: Docs/RE/specs/skinning.md §8(e) — rig/clip identity, per class.

    // Per-class starter skins. spec: frontend_scenes.md §3.7.5. CODE-CONFIRMED.
    // Keys are internal class ids 3,4,6,11 → Musa g202110001 / Dosa g202110001 etc.
    // For the generic slot preview we use the skin_class id pattern (SkinClassId).
    // Fallback: Musa mesh (class 1 = g202110001.skn).
    private const string FallbackSknPath = "data/char/skin/g202110001.skn";

    // =========================================================================
    // Runtime state
    // =========================================================================

    private Camera3D? _camera;

    // The manual-input + hit-test rig (boom-zoom on the camera, yaw on the selected actor,
    // and the 3D world-space slot ray-pick). Built in BuildCamera, configured at end of Initialise.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.3 / §3.3.3.
    private CharSelectCameraRig? _cameraRig;

    // TerrainNode reference for ground-height sampling.
    // Set during BuildBackdropTerrain so BuildCharacterRow can query it.
    private TerrainNode? _backdropTerrain;

    // One character node per slot (index 0..4); null = empty or failed.
    private readonly Node3D?[] _slotActors = new Node3D?[5];

    /// <summary>Current selected slot index (0..4); changes the active/idle highlight.</summary>
    private int _selectedSlot;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Slot descriptor array — set before calling <see cref="Initialise"/>.
    /// Each entry: (IsOccupied, SkinClassId) — 5 entries max.
    /// </summary>
    public (bool IsOccupied, uint SkinClassId)[] SlotDescriptors { get; set; } = new (bool, uint)[5];

    /// <summary>
    /// Initialises the 3D scene. Call this AFTER the node is in the scene tree
    /// (e.g. from _Ready or a deferred call in the parent).
    /// Assets can be null — the scene degrades to a coloured backdrop + env only.
    /// </summary>
    public void Initialise(RealClientAssets? assets)
    {
        try
        {
            BuildEnvironment();
            BuildCamera();
            BuildLighting();

            if (assets is not null)
            {
                BuildBackdropTerrain(assets);
                BuildBackdropProps(assets);
                BuildCharacterRow(assets);
            }
            else
            {
                GD.Print("[CharSelectScene3D] No VFS — terrain/props/characters skipped; env+camera only.");
            }

            // Spawn the real char_select-u.xeff effect (effect id 380003000).
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED —
            //   scene builder spawns EXACTLY ONE composite ambient effect, effect id 380003000
            //   (= char_select-u.xeff, 68 sub-effects), at world (508.483, 69.887, −9758.569),
            //   scale 1.0, identity rotation. There are ZERO other effect spawns.
            // Godot-space anchor (world Z negated): (508.483, 69.887, +9758.569).
            // spec: Docs/RE/formats/effects.md §A.15 — id 380003000 = char_select-u.xeff. CONFIRMED.
            // Sub-effect positions are read from keyframe-0 velocity Vec3 (displacement from anchor).
            // spec: Docs/RE/formats/effects.md §A.8 — velocity Vec3 = displacement. HIGH.
            XeffSceneEffect.LoadAndAttach(
                this,
                anchorGodotPos: new Vector3(508.483f, 69.887f, 9758.569f),
                assets: assets);

            GD.Print("[CharSelectScene3D] 3D scene initialised.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Initialise failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the selected slot, swapping the animation clip on the actors.
    /// Occupied selected slot gets the "select" yaw (facing front); others get idle.
    /// Since we don't yet have the select-turn clip (§3.3.4 select record field +0x58),
    /// we visually highlight via modulate tint as a placeholder.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.4 CODE-CONFIRMED (clip swap spec).
    /// </summary>
    public void SetSelectedSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 5) return;
        _selectedSlot = slotIndex;

        // Full select-turn clip swap requires the select record field +0x58 — runtime-pending.
        // spec: frontend_scenes.md §3.3.4 — clip swap idle→select. CODE-CONFIRMED spec; clip pending.
        // Node3D nodes do not have Modulate (that is a CanvasItem property for 2D nodes).
        // Visual selection highlight via material is a future enhancement; 2D overlay highlights
        // the active slot already via HighlightSlot() in CharacterSelectScreen.
        GD.Print($"[CharSelectScene3D] Selected slot → {slotIndex} (clip-swap pending spec §3.3.4).");
    }

    /// <summary>Returns the Godot-space world position of the camera's resting eye (= KF1 position).</summary>
    public Vector3 GetCameraEye() => DollyKF1Godot;

    /// <summary>
    /// 3D world-space ray-pick against per-slot axis-aligned bounding boxes (the selection
    /// hit-test). Unprojects the click pixel (in SubViewport-local coordinates) through the
    /// scene <see cref="Camera3D"/> into a world ray, then tests each slot actor's AABB
    /// (centred on the slot's Godot-space X/Z, ±6 in X and Z, Y band [rowBaseY, rowBaseY+22]
    /// = spec Y 70..92) and returns the NEAREST hit along the ray.
    ///
    /// <para>Returns the slot index (0..4) under that pixel, or −1 if none. L4 calls this from
    /// the 2D screen when the user clicks inside the 3D viewport region.</para>
    ///
    /// spec: Docs/RE/specs/frontend_scenes.md §3.3.3 CODE-CONFIRMED —
    ///   "3D world-space ray-pick; AABB X ± 6, Z ± 6, Y band 70.0 … 92.0; loop 5 slots skip
    ///    empty; nearest hit → confirmed-pick; −1 = none."
    /// </summary>
    public int TryHitTestSlot(global::Godot.Vector2 viewportLocalPos)
        => _cameraRig?.HitTest(viewportLocalPos) ?? -1;

    // =========================================================================
    // Environment
    // =========================================================================

    private void BuildEnvironment()
    {
        // Area-0 environment TRUTH (recovered CAMPAIGN 9). The char-select scene is an achromatic,
        // statically-lit "dark stone temple" — NOT a warm torchlit cavern and NOT a blue sky.
        // The area-0 light/material tables are STATIC across all 48 keyframes and ACHROMATIC (R=G=B);
        // the per-keyframe ambient table is multiplied by K_ambient = 0.0 (INERT). The REAL scene fill
        // is a WHITE device ambient floor from OPTION_BRIGHT (default 100 -> 1.0 = white). This is the
        // fix for the historical "too dark / invisible characters" debt.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6 + Docs/RE/formats/environment_bins.md (area-0 kf-29). CODE-CONFIRMED.

        // global::Godot.Environment to avoid the sibling-namespace collision (CS0234).
        var env = new global::Godot.Environment();

        // Achromatic near-black background — area-0 sky is pure grey (sky_haze R=G=B ~ 0.004).
        // spec: Docs/RE/formats/environment_bins.md §11.6 SAMPLE-VERIFIED (achromatic).
        env.BackgroundMode = global::Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.04f, 0.04f, 0.04f); // achromatic dark

        // WHITE ambient floor — the scene's MAIN illuminant. OPTION_BRIGHT/100 = 1.0 -> white.
        // spec: Docs/RE/formats/environment_bins.md §10.5/§11.5 + frontend_scenes.md §3.6.2. CODE-CONFIRMED.
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(1.0f, 1.0f, 1.0f); // white floor — OPTION_BRIGHT=100. CODE-CONFIRMED
        env.AmbientLightEnergy = 1.0f; // spec: §3.6.2 OPTION_BRIGHT/100 default = 1.0. CODE-CONFIRMED

        // Linear tonemap — faithful to the legacy D3D9 output (no ACES darkening); keeps the white
        // ambient floor reading at full value so the stone-temple scene is legible.
        env.TonemapMode = global::Godot.Environment.ToneMapper.Linear;
        env.TonemapExposure = 1.0f;

        // Distance fog OFF behind the preview row (the legacy zeroes the fog blend offset).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.2 CODE-CONFIRMED — distance fog OFF.
        env.FogEnabled = false;

        // Glow: only the HDR sun_color (material0 sun ~1.26 > 1.0) blooms — HDR threshold 0.8 so the
        // flatly-lit white-ambient scene does not halate. Single half-res level (no Gaussian pyramid).
        // spec: Docs/RE/formats/environment_bins.md §11.4 (sun HDR) + rendering.md §8 (single level).
        env.GlowEnabled = true;
        env.GlowIntensity = 0.6f; // Aesthetic
        env.GlowStrength = 1.0f; // Aesthetic
        env.GlowBloom = 0.05f; // Aesthetic
        env.GlowHdrThreshold = 0.8f; // only HDR sun blooms — avoids haloing the white-ambient scene
        env.Set("glow_levels/1", 1.0f);
        env.Set("glow_levels/2", 0.0f);
        env.Set("glow_levels/3", 0.0f);
        env.Set("glow_levels/4", 0.0f);
        env.Set("glow_levels/5", 0.0f);
        env.Set("glow_levels/6", 0.0f);
        env.Set("glow_levels/7", 0.0f);

        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);

        GD.Print(
            "[CharSelectScene3D] Area-0 environment built: achromatic dark BG + WHITE ambient floor (energy 1.0 OPTION_BRIGHT) + fog OFF + HDR-only bloom. spec: frontend_scenes.md §3.6 / environment_bins.md CODE-CONFIRMED.");
    }

    // =========================================================================
    // Camera — single static perspective camera (the "orbit" is REFUTED)
    // =========================================================================

    private void BuildCamera()
    {
        // Entry-dolly perspective camera — starts at KF0, animates to KF1 over ~2.0 s, then holds.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5 CODE-CONFIRMED (entry dolly KF0→KF1).
        // Projection: FOV 50° / near 5 / far 15000 (§3.5.1 CODE-CONFIRMED).
        _camera = new Camera3D
        {
            Name = "CharSelectCamera",
            Fov = CameraFov,   // spec: §3.5.1 FOV 50°. CODE-CONFIRMED.
            Near = CameraNear, // spec: §3.5.1 near 5.0. CODE-CONFIRMED.
            Far = CameraFar,   // spec: §3.5.1 far 15000.0. CODE-CONFIRMED.
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        AddChild(_camera);

        // Start at KF0 (dolly start); the CharSelectCameraRig will animate it to KF1 over 2.0 s.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — rig constructed at index 0. CODE-CONFIRMED.
        _camera.Position = DollyKF0Godot;
        _camera.LookAt(DollyLookAtKF0Godot, Vector3.Up);

        // Build + wire the entry-dolly + manual-input + hit-test rig.
        // The rig ticks in _Process: first ~2.0 s it runs the KF0→KF1 dolly (lerp pos / slerp rot),
        // then holds KF1 and responds only to player manual inputs (boom-zoom + actor-yaw).
        // spec: Docs/RE/specs/frontend_scenes.md §3.5 / §3.5.3 (manual inputs) / §3.3.3 (hit-test).
        _cameraRig = new CharSelectCameraRig { Name = "CharSelectCameraRig" };
        AddChild(_cameraRig);
        _cameraRig.Configure(
            camera: _camera,
            slotWorldX: SlotWorldX,
            slotWorldZ: SlotWorldZ,
            rowBaseYFallback: SlotWorldYFallback, // spec: §3.3.1 fallback row base Y. CODE-CONFIRMED.
            selectedSlotProvider: () => _selectedSlot,
            slotActorProvider: i => (uint)i < (uint)_slotActors.Length ? _slotActors[i] : null,
            kf0Pos: DollyKF0Godot,
            kf1Pos: DollyKF1Godot,
            lookAtTarget: DollyLookAtGodot);

        GD.Print($"[CharSelectScene3D] Entry-dolly camera built: KF0={DollyKF0Godot} → KF1={DollyKF1Godot} " +
                 $"(Godot-space; stage anchor {StageWorldAnchorGodot}). FOV {CameraFov}/near {CameraNear}/" +
                 $"far {CameraFar}. Dolly 2.0 s lerp/slerp then hold KF1. Manual rig attached. " +
                 "spec: frontend_scenes.md §3.5.2/§3.5.4 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Lighting — approximating the 14:30 afternoon rig
    // =========================================================================

    private void BuildLighting()
    {
        // Area-0 lighting TRUTH: the scene builder creates NO brazier/torch point-lights.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED — "the scene builder creates
        //   no brazier point-lights at all". spec: §3.6.6 CODE-CONFIRMED — "No scene point-lights".
        // The warm cavern glow = the additive FIRE TEXTURE billboards in XeffSceneEffect, not lights.
        // A faithful port must NOT add OmniLights for the braziers (§3.6.6 port contract #5).
        //
        // ONLY: the faint achromatic directional key from the area-0 kf-29 data (~0.047).
        // The WHITE ambient floor (OPTION_BRIGHT=100 → 1.0) is already set in BuildEnvironment.
        // spec: Docs/RE/formats/environment_bins.md §11.3/§9.4 SAMPLE-VERIFIED (energy + light vector).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED (achromatic, ≈ 5 positional
        //   lights are the sky/time-manager lights, range ≈ 1024; colours data-driven, NOT warm).

        // Faint achromatic directional key — area-0 kf-29 directional ~0.047, light vector
        // (−7, 7, 20) legacy world; Godot-space negates Z → (−7, 7, −20).
        // spec: Docs/RE/formats/environment_bins.md §9.4/§10.6/§11.3. SAMPLE-VERIFIED.
        var sun = new DirectionalLight3D
        {
            Name = "Area0Directional",
            LightEnergy = 0.047f, // spec: environment_bins.md §11.3 SAMPLE-VERIFIED (area-0 kf-29 directional)
            LightColor = new Color(1.0f, 1.0f, 1.0f), // achromatic — area-0 R=G=B. spec: §11.2
            ShadowEnabled = false,
        };
        AddChild(sun);
        var sunPivot = new Vector3(512.0f, 200.0f, 9738.0f);
        sun.LookAtFromPosition(sunPivot, sunPivot + new Vector3(-7.0f, 7.0f, -20.0f).Normalized(), Vector3.Up);

        // NO OmniLight3D / SpotLight3D here. The original adds none; the fire glow is additive
        // texture (XeffSceneEffect). Removing the previous warm omni rig that was wrongly added.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 + §3.6.6 CODE-CONFIRMED (no scene lights).
        // If characters appear too dark WITHOUT the fire billboards (no VFS), the white ambient
        // floor at energy 1.0 is sufficient — characters read at full Unshaded brightness.

        GD.Print("[CharSelectScene3D] Area-0 lighting built: faint achromatic directional (0.047) only; " +
                 "NO brazier/torch OmniLights (fire glow = additive XeffSceneEffect textures, not lights). " +
                 "White ambient floor (energy 1.0) is the main illuminant. " +
                 "spec: frontend_scenes.md §3.6.1 + §3.6.6 CODE-CONFIRMED (no scene point-lights).");
    }

    // =========================================================================
    // Backdrop terrain — single cell d000x10000z9990
    // =========================================================================

    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.1 — backdrop cell d000x10000z9990. CODE-CONFIRMED.
        // Cell origin in legacy world: (cx*1024, cz*1024) = (0, −10240) for cx=0, cz=−10.
        // In Godot-space: (0, 0, 10240) (Z negated).
        // spec: TerrainNode world-space bias: worldX_min = (mapX−10000)*1024, worldZ_min = (mapZ−10000)*1024.
        //   mapX=10000 → worldX_min=0; mapZ=9990 → worldZ_min=−10240.

        string tedPath =
            $"data/map{AreaTag(BackdropAreaId)}/dat/d{AreaTag(BackdropAreaId)}x{BackdropMapX}z{BackdropMapZ}.ted";

        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop terrain not found: {tedPath} — skipping .ted.");
            return;
        }

        try
        {
            ReadOnlyMemory<byte> tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharSelectScene3D] Backdrop .ted is empty: {tedPath}");
                return;
            }

            // Resolve terrain textures via the confirmed two-hop chain.
            // spec: terrain.md §5.6 Block 3 — 1-based TextureIndexGrid → .map TERRAIN TEXTURES → bgtexture → .dds.
            Func<int, ImageTexture?> texResolver = BuildTerrainTextureResolver(assets);

            // Build the terrain mesh using TerrainNode (reuse the existing builder).
            // TerrainNode.OnSectorLoaded parses the .ted, builds the ArrayMesh, and positions it.
            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = texResolver,
            };
            AddChild(terrainNode);
            _backdropTerrain = terrainNode; // capture for ground-height sampling in BuildCharacterRow

            // Feed the sector directly (no streaming needed for a single backdrop cell).
            // spec: TerrainNode.OnSectorLoaded — takes SectorLoadedEvent(mapX, mapZ, payload).
            var evt = new MartialHeroes.Client.Application.World.SectorLoadedEvent(
                MapX: BackdropMapX,
                MapZ: BackdropMapZ,
                Payload: tedData);
            terrainNode.OnSectorLoaded(evt);

            GD.Print($"[CharSelectScene3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. " +
                     "spec: frontend_scenes.md §3.7.1 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop terrain failed: {ex.Message}");
        }
    }

    private Func<int, ImageTexture?> BuildTerrainTextureResolver(RealClientAssets assets)
    {
        // Two-hop chain: 1-based tex byte → cell .map TERRAIN TEXTURES[idx-1].intTexId → bgtexture pool → .dds.
        // spec: Docs/RE/formats/terrain.md §5.6 Block 3 + §3.5 + §4.2. CONFIRMED.
        BgTextureCatalog? bgPool = null;
        MapDescriptor? cellMap = null;
        var cache = new Dictionary<int, ImageTexture?>();

        try
        {
            string txtPath = "data/map000/texture/bgtexture.txt";
            if (assets.Contains(txtPath))
                bgPool = BgTextureTxtParser.Parse(assets.GetRaw(txtPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] bgtexture.txt load failed: {ex.Message}");
        }

        try
        {
            string tag = AreaTag(BackdropAreaId);
            string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
            if (assets.Contains(mapPath))
                cellMap = MapDescriptorParser.Parse(assets.GetRaw(mapPath));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] backdrop .map load failed: {ex.Message}");
        }

        return texByte =>
        {
            if (cache.TryGetValue(texByte, out ImageTexture? cached)) return cached;
            ImageTexture? tex = ResolveTexture(assets, bgPool, cellMap, "TERRAIN", texByte);
            cache[texByte] = tex;
            return tex;
        };
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
        int li = oneBasedIdx - 1;
        if ((uint)li >= (uint)list.Length) return null;

        string? rel = pool.GetRelPath(list[li].TexId);
        if (rel is null) return null;

        string ddsPath = $"data/map000/texture/{rel}.dds";
        return assets.Contains(ddsPath) ? assets.LoadTexture(ddsPath) : null;
    }

    // =========================================================================
    // Backdrop props — .bud scene
    // =========================================================================

    private void BuildBackdropProps(RealClientAssets assets)
    {
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.1 — d000x10000z9990.bud. CODE-CONFIRMED.
        // Building textures use the same two-hop chain but via the BUILDING section of the .map.
        string tag = AreaTag(BackdropAreaId);
        string budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";

        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .bud not found: {budPath} — skipping props.");
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

            // Resolve building textures.
            BgTextureCatalog? bgPool = null;
            MapDescriptor? cellMap = null;

            try
            {
                string txtPath = "data/map000/texture/bgtexture.txt";
                if (assets.Contains(txtPath))
                    bgPool = BgTextureTxtParser.Parse(assets.GetRaw(txtPath));
            }
            catch
            {
                /* non-critical */
            }

            try
            {
                string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
                if (assets.Contains(mapPath))
                    cellMap = MapDescriptorParser.Parse(assets.GetRaw(mapPath));
            }
            catch
            {
                /* non-critical */
            }

            // BudMeshBuilder.Build expects Func<uint, ImageTexture?> for the texture resolver.
            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            Node3D propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            AddChild(propsRoot);

            GD.Print($"[CharSelectScene3D] Backdrop props built ({scene.Objects.Length} objects). " +
                     "spec: frontend_scenes.md §3.7.1 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Backdrop props failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Character row — up to 5 skinned actors
    // =========================================================================

    private void BuildCharacterRow(RealClientAssets assets)
    {
        // RIG/CLIP IDENTITY (the slot-row T-pose / shatter fix):
        //   Each slot actor resolves its OWN skeleton AND idle clip from the parsed .skn's own id_b —
        //   NOT a single shared g1.bnd + shared g111100010.mot. A skin is authored against exactly one
        //   skeleton named by its id_b; the four starter classes do NOT share a rig (class 1 → g1/84
        //   bones, class 4 → g4/89 bones). Binding the wrong rig is clean at rest but T-poses / shatters
        //   the instant a wrong-rig idle clip drives bones off bind. This mirrors the proven
        //   CharCreatePreview3D fix exactly.
        // spec: Docs/RE/specs/skinning.md §8(e) — select skeleton AND clip by the skin's id_b, per class.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.4 — char-select actors start in IDLE.

        // L5 — drive row Y from terrain ground sampler at the row pivot.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED.
        // Query TryGetGroundHeight in LEGACY (non-negated) XZ space: worldX=508.48, worldZ=−9758.57.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — row pivot (508.48, 69.89, −9758.57). CODE-CONFIRMED.
        // TryGetGroundHeight signature: (worldX, worldZ, out float height, float fallbackY)
        // where worldZ is LEGACY space (not Godot-negated) — TerrainNode uses legacy coords internally.
        // The backdrop cell is a purpose-built temple — the .ted heightfield gives the raw
        // soil surface (≈ 26 units), but the characters stand ON the raised stone platform
        // (.bud geometry), whose top surface sits at Y ≈ 69.89 (the spec-confirmed row pivot Y).
        // The terrain sampler does NOT include .bud geometry, so it gives a value ~44 units too low
        // for this cell. We use the SPEC-CONFIRMED row pivot Y = 69.89 directly.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 — row pivot (508.48, 69.89, −9758.57).
        //   CODE-CONFIRMED. The fallback SlotWorldYFallback = 70.0f matches this.
        // Note: TryGetGroundHeight is still called for diagnostic logging, but the placement uses
        // the spec value — the terrain .ted represents the carved-rock floor, not the platform top.
        float rowY = SlotWorldYFallback; // spec: §3.6.5 row pivot Y = 69.89 ≈ 70.0. CODE-CONFIRMED.
        if (_backdropTerrain is not null)
        {
            if (_backdropTerrain.TryGetGroundHeight(RowPivotLegacyX, RowPivotLegacyZ, out float sampledY,
                    SlotWorldYFallback))
            {
                // The terrain sampler gives the raw .ted soil height (≈ 26 for this cell) — NOT
                // the .bud platform top (≈ 70). We use the spec value and log the sampled value
                // for diagnostic purposes only.
                GD.Print($"[CharSelectScene3D] Terrain sampler at pivot: {sampledY:F3} (soil/floor, NOT platform top). " +
                         $"Using spec row pivot Y={SlotWorldYFallback} for character placement. " +
                         "spec: frontend_scenes.md §3.6.5 row pivot Y=69.89 CODE-CONFIRMED.");
            }
            else
            {
                GD.Print(
                    $"[CharSelectScene3D] TryGetGroundHeight miss at pivot — using spec Y={SlotWorldYFallback}. " +
                    "spec: frontend_scenes.md §3.6.5 CODE-CONFIRMED.");
            }
        }
        else
        {
            GD.Print($"[CharSelectScene3D] No terrain node — using spec row Y={SlotWorldYFallback}. " +
                     "spec: frontend_scenes.md §3.6.5 CODE-CONFIRMED.");
        }

        for (int i = 0; i < 5; i++)
        {
            bool occupied = i < SlotDescriptors.Length && SlotDescriptors[i].IsOccupied;
            if (!occupied) continue;

            uint skinClassId = SlotDescriptors[i].SkinClassId;

            try
            {
                Node3D? actor = TryBuildSlotActor(assets, i, skinClassId, rowY);
                if (actor is not null)
                {
                    _slotActors[i] = actor;
                    AddChild(actor);

                    GD.Print($"[CharSelectScene3D] Slot {i} actor placed at world " +
                             $"({SlotWorldX[i]:F1}, {rowY:F3}, {SlotWorldZ[i]:F1}) Godot-space. " +
                             "spec: frontend_scenes.md §3.3.1 CODE-CONFIRMED.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharSelectScene3D] Slot {i} actor build failed: {ex.Message}");
            }
        }
    }

    private Node3D? TryBuildSlotActor(
        RealClientAssets assets, int slotIdx, uint skinClassId, float rowY)
    {
        // Resolve the .skn path for this slot's skin class.
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.5 — per-class starter meshes. CODE-CONFIRMED.
        string? sknPath = PickSknPath(assets, skinClassId);
        if (sknPath is null)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: no .skn found for skinClassId={skinClassId}.");
            return null;
        }

        // Parse the .skn mesh.
        SkinnedMesh mesh;
        try
        {
            ReadOnlyMemory<byte> sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: .skn empty at '{sknPath}'.");
                return null;
            }

            mesh = SknParser.Parse(sknData);
            GD.Print($"[CharSelectScene3D] Slot {slotIdx}: loaded '{sknPath}' " +
                     $"({mesh.Positions.Length} verts, {mesh.FaceCount} faces, IdA={mesh.IdA}, IdB={mesh.IdB}).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] Slot {slotIdx}: .skn parse failed '{sknPath}': {ex.Message}");
            return null;
        }

        // Skeleton — resolved from the MESH'S OWN id_b (NOT a shared g1.bnd). The skin's bind-local
        // vertex offsets are baked against exactly this skeleton's rest pose; binding it to any other
        // same-ID-range rig is clean at rest but T-poses / shatters on play.
        // spec: Docs/RE/specs/skinning.md §8(e) — data/char/bind/g{id_b}.bnd, per class.
        Skeleton? skeleton = TryLoadSkeletonForIdB(assets, mesh.IdB);

        // Idle animation — the MATCHED clip for THIS rig, keyed by the mesh's own id_b via
        // actormotion.txt (col2 == id_b → col16). Each class's idle clip targets ITS rig's bones;
        // playing the wrong-rig clip is the direct cause of the slot-row shatter. If no idle row
        // exists for this id_b (some VFS classes have none), idleClip is null → SkinnedCharacterBuilder
        // renders a clean upright REST pose (NOT a T-pose, NOT a shatter) and we log it below.
        // spec: Docs/RE/specs/skinning.md §8(e) — actormotion col2==id_b → col16, per class.
        AnimationClip? idleClip = TryLoadIdleClipForIdB(assets, mesh.IdB);

        GD.Print($"[CharSelectScene3D] Slot {slotIdx}: resolved rig from id_b={mesh.IdB}: " +
                 $"bnd 'data/char/bind/g{mesh.IdB}.bnd' bones={(skeleton?.Bones.Length ?? 0)} " +
                 $"idle={(idleClip is null ? "NONE → rest-pose fail-safe" : $"{idleClip.Tracks.Length}trk/{idleClip.FrameCount}f")}. " +
                 "spec: skinning.md §8(e) per-class rig/clip identity.");

        // Resolve the albedo texture via CharacterTextureResolver (skin.txt col4→col5→png).
        // spec: Docs/RE/specs/skinning.md §10 / CLAUDE.md asset chain — skin.txt col4=meshSkinId, col5=texId.
        ImageTexture? albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);
        GD.Print($"[CharSelectScene3D] Slot {slotIdx}: albedo resolved={albedo is not null} " +
                 $"for skinIdA={mesh.IdA}.");

        // Build the skinned actor node with the PER-SLOT rig + idle clip.
        // SkinnedCharacterBuilder handles LBS/static fallback, diagnostics, stand-up pivot, recentre.
        // spec: Docs/RE/specs/skinning.md §8(b) — single Z-negate handedness conversion. CODE-CONFIRMED.
        Node3D actorRoot = SkinnedCharacterBuilder.Build(mesh, skeleton, idleClip, albedo);

        // IMPORTANT: SkinnedCharacterBuilder.Build returns a node whose Position is already set
        // by RecentreRoot (to centre the mesh with feet at Y=0). DO NOT overwrite actor.Position
        // directly — that erases the recentre offset. Wrap the actor in a slot-position container.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED slot positions.
        var slotWrapper = new Node3D { Name = $"Slot{slotIdx}Actor" };
        slotWrapper.Position = new Vector3(SlotWorldX[slotIdx], rowY, SlotWorldZ[slotIdx]);
        slotWrapper.Scale = Vector3.One * PreviewScale;

        // Per-slot facing — pure yaw about world Y.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.2 CODE-CONFIRMED:
        //   occupied (lock flag clear) → yaw 0 (faces front / toward camera);
        //   locked / new / creating slot → yaw π (faces away, back to viewer).
        // The slot is occupied when IsOccupied = true for its index in SlotDescriptors.
        // PORT CAVEAT (spec §3.3.2 MEDIUM): the .skn mesh-local X-negation can flip apparent
        // facing. If visual inspection shows the back at yaw 0, add Mathf.Pi consistently.
        bool isOccupied = slotIdx < SlotDescriptors.Length && SlotDescriptors[slotIdx].IsOccupied;
        float slotYaw = isOccupied ? 0.0f : Mathf.Pi; // spec: §3.3.2 CODE-CONFIRMED
        slotWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(slotYaw), 0f);
        GD.Print($"[CharSelectScene3D] Slot {slotIdx}: yaw={slotYaw:F3} rad " +
                 $"({(isOccupied ? "front/occupied" : "back/locked")}). " +
                 "spec: frontend_scenes.md §3.3.2 CODE-CONFIRMED.");

        slotWrapper.AddChild(actorRoot);

        return slotWrapper;
    }

    // =========================================================================
    // Asset helpers
    // =========================================================================

    /// <summary>
    /// Loads the skeleton MATCHED to a skin's <paramref name="idB"/>: data/char/bind/g{id_b}.bnd.
    /// The skin's bind-local vertex offsets are baked against exactly this rig's rest pose; binding
    /// any other same-ID-range rig is clean at rest but T-poses / shatters on play. PER SLOT — never
    /// a single shared g1.bnd.
    /// spec: Docs/RE/specs/skinning.md §8(e) — data/char/bind/g{id_b}.bnd, per class. CODE-CONFIRMED.
    /// </summary>
    private static Skeleton? TryLoadSkeletonForIdB(RealClientAssets assets, uint idB)
    {
        if (idB == 0) return null;
        string bndPath = $"data/char/bind/g{idB}.bnd";
        if (!assets.Contains(bndPath))
        {
            GD.PrintErr($"[CharSelectScene3D] .bnd not found for id_b={idB}: {bndPath}.");
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

    /// <summary>
    /// Loads the idle <c>.mot</c> clip MATCHED to a skin's <paramref name="idB"/> from
    /// <c>actormotion.txt</c>: the row whose col2 == id_b gives the idle motion id in col16, resolved
    /// to <c>data/char/mot/g{idle}.mot</c>. This guarantees the clip's track bone IDs address the SAME
    /// skeleton the mesh was baked against (per-class rig/clip identity). Returns null (→ clean rest
    /// pose) when this id_b has no idle row — fail SAFE, never a T-pose or a shatter.
    /// spec: Docs/RE/specs/skinning.md §8(e); CLAUDE.md §Recovered asset mappings (actormotion idle).
    /// </summary>
    private static AnimationClip? TryLoadIdleClipForIdB(RealClientAssets assets, uint idB)
    {
        if (idB == 0) return null;

        const string tablePath = "data/char/actormotion.txt";
        if (!assets.Contains(tablePath)) return null;

        try
        {
            // CP949 provider registered once at startup; decode the table text.
            // spec: CLAUDE.md §Core engineering constraints — "Register [CP949] once".
            string text = System.Text.Encoding.GetEncoding(949).GetString(assets.GetRaw(tablePath).Span);

            foreach (string rawLine in text.Split('\n'))
            {
                string[] cols = rawLine.Replace("\r", string.Empty).Split('\t');
                if (cols.Length <= 16) continue;
                if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != idB) continue;

                string idle = cols[16].Trim();
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

    /// <summary>
    /// Resolves a VFS .skn path for the given internal class id (1..4 for the four starter classes).
    /// Uses the SAME per-class starter-mesh table as the proven CharCreatePreview3D so each slot's
    /// mesh carries a DISTINCT id_b (class 1 → id_b 1 / g1, … class 4 → id_b 4 / g4), letting the
    /// per-slot rig/clip resolution in TryBuildSlotActor pick each character's own skeleton + idle.
    /// spec: Docs/RE/specs/frontend_scenes.md §3.7.5 / §4.2 CODE-CONFIRMED meshes.
    /// spec: Docs/RE/specs/skinning.md §8(e) — the .skn id_b drives the rig, so per-class meshes matter.
    /// </summary>
    private static string? PickSknPath(RealClientAssets assets, uint skinClassId)
    {
        // Per-class starter base-skin meshes (internal class 1..4) — identical to
        // CharCreatePreview3D.SknPathForClass so the slot row and the create preview agree.
        // spec: Docs/RE/specs/frontend_scenes.md §4.2 / §3.7.5. CODE-CONFIRMED.
        string[] specPaths = skinClassId switch
        {
            1 => ["data/char/skin/g202110001.skn"], // Musa  → id_b 1 (g1, 84 bones)
            2 => ["data/char/skin/g202220001.skn"], // Tao   → id_b 2 (g2, 87 bones)
            3 => ["data/char/skin/g202130001.skn"], // Blader→ id_b 3 (g3, 82 bones)
            4 => ["data/char/skin/g202140001.skn"], // Warrior→ id_b 4 (g4, 89 bones)
            // Other (mob / appearance) classes: best-effort pattern, rig still resolved from id_b.
            _ =>
            [
                $"data/char/skin/g202{skinClassId}10001.skn",
                $"data/char/skin/g202{skinClassId}10002.skn",
            ],
        };

        foreach (string p in specPaths)
            if (assets.Contains(p))
                return p;

        // Fallback: Musa (g202110001.skn) — the proven humanoid rig.
        return assets.Contains(FallbackSknPath) ? FallbackSknPath : null;
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}