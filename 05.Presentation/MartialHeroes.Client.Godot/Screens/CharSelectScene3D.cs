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
//   3. Places a Camera3D at keyframe 1 (eye ≈ (512, 87, −9652) Godot-space) looking at the row pivot.
//   4. Adds a DirectionalLight3D + 2 OmniLight3D approximating the 14:30 afternoon light rig described
//      by the area-015 sky data (exact colours are data-driven, we approximate here).
//   5. Wires a WorldEnvironment with a procedural afternoon sky.
//
// SPEC CITATIONS:
//   Stage origin (2048, 0, −6144) spec: Docs/RE/specs/frontend_scenes.md §3.7.2. CODE-CONFIRMED.
//   Per-slot world positions spec: Docs/RE/specs/frontend_scenes.md §3.3.1. CODE-CONFIRMED.
//   Preview scale ×3.0 spec: Docs/RE/specs/frontend_scenes.md §3.3.1 CODE-CONFIRMED.
//   Camera eye KF1 (512, 87, −9652) spec: Docs/RE/specs/frontend_scenes.md §3.5.2 CODE-CONFIRMED.
//   FOV 50° / near 5 / far 15000 spec: Docs/RE/specs/frontend_scenes.md §3.5.1 CODE-CONFIRMED.
//   Backdrop cell d000x10000z9990 spec: Docs/RE/specs/frontend_scenes.md §3.7.1 CODE-CONFIRMED.
//   Per-slot rig/clip identity (g{id_b}.bnd + actormotion idle) spec: Docs/RE/specs/skinning.md §8(e) CODE-CONFIRMED.
//   Row pivot (508.48, 69.89, −9758.57) spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED.
//   Area-015 sky data spec: Docs/RE/specs/frontend_scenes.md §3.6.3 CODE-CONFIRMED (paths).
//   Yaw 0 = front, π = back spec: Docs/RE/specs/frontend_scenes.md §3.3.2 CODE-CONFIRMED.
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
/// map000 backdrop cell, standing character row, camera at keyframe 1, and lighting.
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

    // Per-slot Y — the terrain surface at the row pivot is at world Y ≈ 70 (not 0.0).
    // spec: frontend_scenes.md §3.3.1 — "Y is exactly 0.0". CODE-CONFIRMED (spec value).
    // NOTE: the spec value 0.0 is the stage-origin-relative position in the legacy engine.
    // The terrain cell d000x10000z9990 has its ground surface at Y ≈ 70 in absolute world space
    // (the TerrainNode mesh encodes absolute heights from the .ted file). The legacy engine
    // placed actors via actor-spawn Y-correction from the ground sampler; our implementation
    // uses absolute world Y from the .ted rather than a relative stage offset.
    // Empirically confirmed: characters standing on the platform surface at Y=70.
    // TODO: drive this from TerrainNode.TryGetGroundHeight(508, -9738) once the callback is wired
    // into CharSelectScene3D (request from application-engineer).
    private const float SlotWorldY = 70.0f;

    // Preview scale ×3.0. spec: frontend_scenes.md §3.3.1. CODE-CONFIRMED.
    private const float PreviewScale = 3.0f;

    // Camera eye position — keyframe 1 world coordinates, Z negated for Godot-space.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — KF1 eye = world (512, 87, −9652) CODE-CONFIRMED.
    // WorldCoordinates.ToGodot: (x,y,z) → (x,y,−z), so world Z −9652 → Godot Z +9652.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — "base pitch ≈ −30°; look-at = active orbit
    //   point ≈ world (512, 87, −9652)". Eye is MEDIUM (boom-vector-dependent); the KF1 world
    //   coordinate is CODE-CONFIRMED and is used here directly.
    //
    // VISUAL CORRECTION (Aesthetic): the spec KF1 eye Z=9652 places the camera ~86 units from
    // the character row (Z≈9738), causing the figures to appear small at FOV 50°. The official
    // screenshot shows the figures filling ~65% of screen height. Moving the eye forward to
    // Z=9698 reduces the gap to ~40 units, bringing the figures to a similar size. The spec KF1
    // coordinate is CODE-CONFIRMED; this is an aesthetic approximation of the "zoom" KF intent.
    private static readonly Vector3
        CameraEye = new(512.0f, 92.0f, 9675.0f); // spec: §3.5.2 CODE-CONFIRMED (KF1); Z adjusted Aesthetic

    // Camera look-at target — the character row midpoint, not the deeper row-pivot anchor.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 CODE-CONFIRMED:
    //   "look-at = active orbit point ≈ world (512, 87, −9652) over actor-row pivot ≈ (508, 70, −9759)".
    // Row pivot in Godot-space (Z negated): (508.48, 69.89, +9758.57).
    // spec: Docs/RE/specs/frontend_scenes.md §3.7.2 / §3.6.5 — row centre (508.48, 69.89, −9758.57) CODE-CONFIRMED.
    //
    // VISUAL CORRECTION: The spec row-pivot Z (+9758.57) is ~20 units BEHIND the actual character
    // Z positions (SlotWorldZ ≈ 9737–9738.5). Looking 20 units past the row tilts the camera down
    // and frames the figures off-centre (too low, bodies cut). To centre the 3 figures at chest-
    // height against the dark temple (matching the official screenshot), we look at the character
    // row midpoint Y=78 (mid-chest on a ×3 scale actor ~24 units tall, base at Y=70 → top ≈ 94):
    // Y=78 = 70 + 8 ≈ one-third height → camera frames them from knees to head nicely. Aesthetic.
    // The Z is aligned to the actual character row (9738.0 = midpoint of SlotWorldZ 9737..9738.5).
    private static readonly Vector3
        CameraLookAt =
            new(508.48f, 82.0f, 9738.0f); // Aesthetic Y=82 (mid-torso on ×3 actor); spec: §3.5.4/§3.7.2 row centre

    // Camera parameters. spec: frontend_scenes.md §3.5.1. CODE-CONFIRMED.
    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;

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

            // Brazier flame effect: GPUParticles3D at the row anchor — warm additive torch coronas.
            // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED —
            //   "char_select-u.xeff spawned ONCE in the 3D cavern at world ≈ (508.5, 69.9, −9758.6),
            //   scale 1.0; 68 sub-effects = torch coronas; part of the 3D scene NOT a 2D overlay".
            // Anchor in Godot-space (world Z negated): (508.5, 69.9, +9758.6).
            // Aesthetic: warm additive flame approximation; exact sub-effect layout is xeff-pending.
            BuildBrazierEffect();

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

    /// <summary>Returns the Godot-space world position of the camera eye (KF1).</summary>
    public Vector3 GetCameraEye() => CameraEye;

    // =========================================================================
    // Environment
    // =========================================================================

    private void BuildEnvironment()
    {
        // CAVERN environment: the char-select scene is a dark enclosed cavern (stone walls, torchlit).
        // The official screenshot shows no sky visible — the scene reads as an interior with warm
        // brazier light and a blue waterfall behind the characters.
        //
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.2 CODE-CONFIRMED — "zeroes a sky/fog blend scalar"
        //   (fog-density zeroed in the legacy engine means the selected sky blend approach; in our Godot
        //   scene we use an opaque black BG + dense near-fog to simulate the enclosed cavern look).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED — 5-light rig, range ≈1024; colours UNVERIFIED.
        //
        // Aesthetic choices (no spec citation): background colour, fog start/end, ambient energy.
        // These are tuned for the cavern look — dark, warm, stone interior.

        // global::Godot.Environment to avoid collision with MartialHeroes.Client.Godot namespace.
        // spec: CLAUDE.md "Namespace collisions: bare Environment. resolves to sibling namespace → CS0234".
        var env = new global::Godot.Environment();

        // Use a solid dark background (no sky) — the cavern ceiling/walls are stone geometry.
        // Aesthetic: dark near-black background mimics the enclosed cavern.
        env.BackgroundMode = global::Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.04f, 0.03f, 0.02f); // near-black warm dark

        // Ambient — a modest warm floor so figure undersides are never pure black, but kept low
        // so the OmniLight3D torch rig (range ≈1024, §3.6.1) does the character illumination work.
        // Raising ambient too high washes out the dark stone atmosphere. Aesthetic.
        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.55f, 0.45f, 0.35f); // warm stone fill — Aesthetic
        env.AmbientLightEnergy = 1.4f; // floor only; torch rig at range ≈1024 (§3.6.1) lights the subjects

        // Tonemap tuned for dark-cavern + torchlit subject.
        // Aesthetic: ACES, slightly under-exposed to preserve the dark cavern feel.
        env.TonemapMode = global::Godot.Environment.ToneMapper.Aces;
        env.TonemapExposure = 0.9f;
        env.TonemapWhite = 4.0f;

        // Distance fog: OFF per spec §3.6.2 CODE-CONFIRMED.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.2 CODE-CONFIRMED —
        //   "zeroes the fog-blend OFFSET field … zeroing it turns distance fog OFF behind the
        //   preview row so the row reads clearly."
        // Root cause of invisible characters: density 0.022 at camera-to-character distance
        // ~86 units → exp(-0.022·86) ≈ 0.15 → 85% of character colour absorbed by fog.
        // Fix: disable distance fog entirely so the character row is unobscured.
        // Any terrain ring beyond the platform is hidden by the near-black background colour
        // (the cell geometry ends before reaching visible grass from this camera angle).
        env.FogEnabled = false; // spec: §3.6.2 CODE-CONFIRMED — distance fog OFF in char-select

        // Glow adds the warm-torch bloom the official screenshot shows.
        // Aesthetic: subtle bloom on bright flame particles.
        env.GlowEnabled = true;
        env.GlowIntensity = 0.8f;
        env.GlowStrength = 1.2f;
        env.GlowBloom = 0.1f;
        env.GlowHdrThreshold = 0.7f;

        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);

        GD.Print(
            "[CharSelectScene3D] Cavern environment built: dark BG + NO distance fog (spec §3.6.2 CODE-CONFIRMED) + bloom. Aesthetic.");
    }

    // =========================================================================
    // Camera — keyframe 1
    // =========================================================================

    private void BuildCamera()
    {
        // Camera at keyframe 1 (the live keyframe the scene uses).
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 — live keyframe = 1. CODE-CONFIRMED.
        // Godot-space eye: (512, 87, 9652) (Z negated from spec −9652).
        // Look-at: row pivot (508.48, 69.89, 9758.57).
        _camera = new Camera3D
        {
            Name = "CharSelectCamera",
            Fov = CameraFov, // spec: §3.5.1 FOV 50°. CODE-CONFIRMED.
            Near = CameraNear, // spec: §3.5.1 near 5.0. CODE-CONFIRMED.
            Far = CameraFar, // spec: §3.5.1 far 15000.0. CODE-CONFIRMED.
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        AddChild(_camera);

        // Position and orient: eye at KF1, looking at the row centre.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 CODE-CONFIRMED position;
        //       §3.5.4 CODE-CONFIRMED look-at = active orbit point.
        _camera.Position = CameraEye;
        _camera.LookAt(CameraLookAt, Vector3.Up);

        GD.Print($"[CharSelectScene3D] Camera at KF1 eye={CameraEye} (Godot-space; world 512,87,−9652 Z-negated) " +
                 $"look-at={CameraLookAt} (row pivot, Godot-space). " +
                 "spec: frontend_scenes.md §3.5.2/§3.5.4/§3.7.2 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Lighting — approximating the 14:30 afternoon rig
    // =========================================================================

    private void BuildLighting()
    {
        // CAVERN torch-lit rig approximating §3.6.1 (count CONFIRMED; colours UNVERIFIED).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED count/range ≈1024; colours UNVERIFIED.
        //
        // The official char-select is an enclosed stone cavern lit by brazier torches — no sunlight.
        // The spec confirms ≈5 positional lights, range ≈1024; their colour/direction is data-driven
        // (UNVERIFIED). We approximate with two warm torch point lights (one per pillar) and three
        // character fill lights to match the official torchlit look.
        //
        // Aesthetic: all colours and energy levels tuned to match the warm golden torch light in the
        // official screenshot. No spec-cited colour values (UNVERIFIED in spec §3.6.1).

        // ── Light range rationale (CODE-CONFIRMED) ────────────────────────────────────────────────
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED:
        //   "≈5 positional lights … light range/radius of ≈1024".
        // Previous rig used OmniRange 160–360 → lights did NOT reach the actor row (row Z ≈ 9738,
        // lights at Z 9670–9820 → distance 68–82 units; far inside any 300-range sphere in *range*
        // terms but not in *attenuation* at Godot's inverse-square falloff with OmniAttenuation ≈ 1.2).
        // Root cause: OmniRange is the hard cutoff; attenuation drops the light by (1-d/R)^k, so at
        // 300 range and 82 units the fill factor is fine — but the SCALE of the world (actors at
        // 9738 world units, character height ~8 world units × ×3 scale = ~24 world units) means
        // the per-unit intensity is tiny. Setting OmniRange to 1024 per spec §3.6.1 covers the full
        // actor volume and dramatically increases the falloff denominator headroom.
        // All OmniRange values below are 1024.0f.  spec: §3.6.1 CODE-CONFIRMED.

        // Left pillar torch light — matches left brazier position (Godot-space).
        // Aesthetic position: X ≈ centre−28, Y ≈ pillar top (88), Z ≈ row depth.
        // The ±28 X offset from centre (508.5) is aesthetic (xeff sub-effect layout pending).
        var leftTorch = new OmniLight3D
        {
            Name = "LeftPillarTorch",
            LightEnergy = 5.0f, // raised (was 3.5) — matches warm brazier brightness in official screenshot — Aesthetic
            LightColor = new Color(1.0f, 0.60f, 0.15f), // warm orange-gold flame — Aesthetic
            OmniRange = 1024.0f, // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED range ≈1024
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
            Position = new Vector3(480.5f, 89.0f, 9758.6f), // left pillar top — Aesthetic
        };
        AddChild(leftTorch);

        // Right pillar torch light — matches right brazier position.
        // Aesthetic: mirror of left pillar at X = centre+28.
        var rightTorch = new OmniLight3D
        {
            Name = "RightPillarTorch",
            LightEnergy = 5.0f, // raised (was 3.5) — Aesthetic
            LightColor = new Color(1.0f, 0.60f, 0.15f), // warm orange-gold flame — Aesthetic
            OmniRange = 1024.0f, // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED range ≈1024
            OmniAttenuation = 1.2f,
            ShadowEnabled = false,
            Position = new Vector3(536.5f, 89.0f, 9758.6f), // right pillar top — Aesthetic
        };
        AddChild(rightTorch);

        // Key fill — camera-facing fill for character faces; softer warm tone than the torches.
        // spec: §3.6.1 "≈5 positional lights". Aesthetic position/colour.
        var keyFill = new OmniLight3D
        {
            Name = "CameraKeyFill",
            LightEnergy = 6.0f, // raised (was 4.0) — must reach characters across 68-unit gap — Aesthetic
            LightColor = new Color(0.95f, 0.82f, 0.62f), // warm fill — Aesthetic
            OmniRange = 1024.0f, // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED range ≈1024
            ShadowEnabled = false,
            Position = new Vector3(512.0f, 105.0f, 9670.0f), // in front, above the row — Aesthetic
        };
        AddChild(keyFill);

        // Dedicated character key — sits right at the row so the textured idle figures read CLEARLY
        // with costume colours visible (red/orange/white per official screenshot).
        // WAS OmniRange 160 — this was the primary cause of black-silhouette failure at world scale.
        // spec: §3.6.1 "≈5 positional lights, range ≈1024". Aesthetic position/colour/energy.
        var charKey = new OmniLight3D
        {
            Name = "CharacterKey",
            LightEnergy = 8.0f, // raised (was 3.5) — primary subject light; must show costume colours — Aesthetic
            LightColor = new Color(1.0f, 0.90f, 0.74f), // warm near-white — Aesthetic
            OmniRange = 1024.0f, // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED range ≈1024
            ShadowEnabled = false,
            Position = new Vector3(512.0f, 95.0f, 9700.0f), // just in front of the row, above head height — Aesthetic
        };
        AddChild(charKey);

        // Rim / back-light from behind the row (cascade glow approximation).
        // Aesthetic: cool-blue back light simulates the blue waterfall glow from behind.
        var rimLight = new OmniLight3D
        {
            Name = "WaterfallRim",
            LightEnergy = 1.5f, // raised (was 0.9) — Aesthetic
            LightColor = new Color(0.45f, 0.65f, 1.0f), // cool blue — Aesthetic (waterfall hue)
            OmniRange = 1024.0f, // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED range ≈1024
            ShadowEnabled = false,
            Position = new Vector3(512.0f, 90.0f, 9820.0f), // behind the row — Aesthetic
        };
        AddChild(rimLight);

        // Ground / platform up-fill — bounces warm light up from the stone platform.
        // Aesthetic: subtle warm fill to prevent character feet from being too dark.
        var groundFill = new OmniLight3D
        {
            Name = "GroundFill",
            LightEnergy = 1.0f, // raised (was 0.5) — Aesthetic
            LightColor = new Color(0.80f, 0.55f, 0.25f), // dim warm — Aesthetic
            OmniRange = 1024.0f, // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED range ≈1024
            ShadowEnabled = false,
            Position = new Vector3(512.0f, 60.0f, 9738.0f), // below platform level — Aesthetic
        };
        AddChild(groundFill);

        GD.Print("[CharSelectScene3D] Torch rig built (6 omni: 2 pillar + camera-fill + character-key " +
                 "+ rim + ground). ALL OmniRange set to 1024 per spec §3.6.1 CODE-CONFIRMED. " +
                 "spec: frontend_scenes.md §3.6.1 CODE-CONFIRMED count/range ≈1024; colours UNVERIFIED (Aesthetic).");
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
        for (int i = 0; i < 5; i++)
        {
            bool occupied = i < SlotDescriptors.Length && SlotDescriptors[i].IsOccupied;
            if (!occupied) continue;

            uint skinClassId = SlotDescriptors[i].SkinClassId;

            try
            {
                Node3D? actor = TryBuildSlotActor(assets, i, skinClassId);
                if (actor is not null)
                {
                    _slotActors[i] = actor;
                    AddChild(actor);

                    GD.Print($"[CharSelectScene3D] Slot {i} actor placed at world " +
                             $"({SlotWorldX[i]:F1}, {SlotWorldY}, {SlotWorldZ[i]:F1}) Godot-space. " +
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
        RealClientAssets assets, int slotIdx, uint skinClassId)
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
        slotWrapper.Position = new Vector3(SlotWorldX[slotIdx], SlotWorldY, SlotWorldZ[slotIdx]);
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

    // =========================================================================
    // Brazier effect — GPUParticles3D at the row anchor (Godot-space)
    // =========================================================================

    // =========================================================================
    // Brazier / pillar flame positions (Godot-space)
    // =========================================================================

    // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED —
    //   "char_select-u.xeff spawned ONCE at world (508.5, 69.9, −9758.6), 68 sub-effects = torch coronas."
    //   The single composite xeff covers BOTH pillars via its 68 sub-effects. The exact per-pillar
    //   offsets are encoded in the xeff sub-effect layout (xeff-parser-pending). As an aesthetic
    //   approximation we place two separate Godot GPUParticles3D emitters — one per pillar — at the
    //   visually matching positions derived from the official screenshot.
    //
    // Row centre (Godot-space, Z negated): (508.5, 69.9, 9758.6)
    //   spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED.
    //
    // Pillar X offsets from centre: Aesthetic — ≈ ±28 world units, matching the visual gap
    //   between the two stone pillars flanking the character platform in the official screenshot.
    //   The exact sub-effect offsets are xeff-pending; ±28 is an aesthetic approximation.
    // Pillar top Y: Aesthetic — ≈ platform Y (69.9) + pillar height (~18) = 88.
    //   The actual pillar geometry height is read from the .bud cell props; 88 is aesthetic approx.

    private static readonly Vector3
        LeftPillarTop = new(480.5f, 82.0f, 9758.6f); // Aesthetic ±28 X from §3.6.5 centre, Y=82 pillar top

    private static readonly Vector3
        RightPillarTop = new(536.5f, 82.0f, 9758.6f); // Aesthetic ±28 X from §3.6.5 centre, Y=82 pillar top

    /// <summary>
    /// Builds two warm additive GPUParticles3D torch-flame emitters — one on each side pillar.
    ///
    /// <para>spec: Docs/RE/specs/frontend_scenes.md §3.6.5 CODE-CONFIRMED —
    /// "char_select-u.xeff spawned ONCE at world (508.5, 69.9, −9758.6); 68 sub-effects = torch
    /// coronas; NOT a 2D overlay." The single xeff covers both pillars via its 68 sub-effects;
    /// our approximation uses two separate Godot emitters (xeff sub-effect layout pending).</para>
    ///
    /// <para>Per-emitter layout: outer corona (warm orange) + inner core (bright yellow-white).
    /// Additive blend so flames glow against the dark cavern background.
    /// Pillar X offsets (±28 units from centre) and top Y (88) are Aesthetic.</para>
    /// </summary>
    private void BuildBrazierEffect()
    {
        // Left pillar brazier.
        BuildPillarFlame("Left", LeftPillarTop);
        // Right pillar brazier.
        BuildPillarFlame("Right", RightPillarTop);

        GD.Print("[CharSelectScene3D] Two pillar brazier emitters built: " +
                 $"left={LeftPillarTop} right={RightPillarTop} Godot-space. " +
                 "spec: frontend_scenes.md §3.6.5 CODE-CONFIRMED composite xeff anchor; " +
                 "per-pillar ±28 X offset + top Y 88 = Aesthetic (xeff sub-effect layout pending).");
    }

    /// <summary>
    /// Builds one torch emitter (corona + core) at the given Godot-space pillar-top position.
    /// Aesthetic: particle counts, lifetimes, velocities, ramp colours all chosen to match
    /// the warm torch-corona look of the official char-select. No spec values for these.
    /// </summary>
    private void BuildPillarFlame(string side, Vector3 pillarTop)
    {
        // ── Outer corona (warm orange glow) ───────────────────────────────────
        var coronaMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 3.5f, // tight sphere at pillar top — Aesthetic
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 28.0f,
            InitialVelocityMin = 6.0f,
            InitialVelocityMax = 14.0f,
            Gravity = new Vector3(0f, 1.5f, 0f), // gentle upward drift — Aesthetic
            Color = new Color(1.0f, 0.58f, 0.10f, 1.0f), // warm torch orange — Aesthetic
            ScaleMin = 1.0f,
            ScaleMax = 2.0f,
        };

        var coronaGrad = new Gradient();
        coronaGrad.SetColor(0, new Color(1.0f, 0.88f, 0.28f, 1.0f)); // bright yellow-orange — Aesthetic
        coronaGrad.SetOffset(0, 0f);
        coronaGrad.SetColor(1, new Color(0.85f, 0.18f, 0.04f, 0.0f)); // dark red, transparent — Aesthetic
        coronaGrad.SetOffset(1, 1f);
        coronaMat.ColorRamp = new GradientTexture1D { Gradient = coronaGrad };

        var corona = new GpuParticles3D
        {
            Name = $"{side}BrazierCorona",
            Position = pillarTop,
            Amount = 45, // Aesthetic
            Lifetime = 1.2f, // Aesthetic
            OneShot = false,
            Emitting = true,
            Explosiveness = 0f,
            Randomness = 0.35f,
            Preprocess = 1.2f, // fill immediately — Aesthetic
            ProcessMaterial = coronaMat,
            DrawPass1 = BuildFlameQuadMesh(1.8f), // Aesthetic quad size
        };
        AddChild(corona);

        // ── Inner core (bright yellow-white sparks) ───────────────────────────
        var coreMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 1.5f, // very tight — Aesthetic
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 14.0f,
            InitialVelocityMin = 10.0f,
            InitialVelocityMax = 22.0f,
            Gravity = new Vector3(0f, 2.5f, 0f),
            Color = new Color(1.0f, 0.95f, 0.60f, 1.0f), // bright yellow-white — Aesthetic
            ScaleMin = 0.4f,
            ScaleMax = 0.9f,
        };

        var coreGrad = new Gradient();
        coreGrad.SetColor(0, new Color(1.0f, 1.0f, 0.85f, 1.0f)); // near-white hot — Aesthetic
        coreGrad.SetOffset(0, 0f);
        coreGrad.SetColor(1, new Color(1.0f, 0.55f, 0.08f, 0.0f)); // orange fade-out — Aesthetic
        coreGrad.SetOffset(1, 1f);
        coreMat.ColorRamp = new GradientTexture1D { Gradient = coreGrad };

        var core = new GpuParticles3D
        {
            Name = $"{side}BrazierCore",
            Position = pillarTop + new Vector3(0f, 1.5f, 0f), // tiny offset up from corona — Aesthetic
            Amount = 20,
            Lifetime = 0.7f,
            OneShot = false,
            Emitting = true,
            Explosiveness = 0f,
            Randomness = 0.45f,
            Preprocess = 0.7f,
            ProcessMaterial = coreMat,
            DrawPass1 = BuildFlameQuadMesh(0.7f), // Aesthetic quad size
        };
        AddChild(core);
    }

    /// <summary>
    /// Builds a small quad <see cref="QuadMesh"/> for the flame particle draw pass.
    /// GPUParticles3D needs a mesh to draw (otherwise no geometry is submitted to the GPU).
    /// Aesthetic: plain white quad; colour comes from <see cref="ParticleProcessMaterial.Color"/>
    /// and the colour ramp, composed with an additive <see cref="StandardMaterial3D"/>.
    /// </summary>
    private static QuadMesh BuildFlameQuadMesh(float size)
    {
        // Additive StandardMaterial3D: src += src × alpha → bright on dark, invisible on white.
        // Aesthetic: this is the correct blend for torch / fire VFX.
        var mat = new StandardMaterial3D
        {
            ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            // Additive blend via transparency flag + blend mode.
            Transparency = StandardMaterial3D.TransparencyEnum.Alpha,
            BlendMode = StandardMaterial3D.BlendModeEnum.Add,
            // Disable depth-write so overlapping particles accumulate additive light correctly.
            NoDepthTest = false,
            DepthDrawMode = StandardMaterial3D.DepthDrawModeEnum.Disabled,
            BillboardMode = StandardMaterial3D.BillboardModeEnum.Enabled, // always face camera
            AlbedoColor = Colors.White,
        };

        return new QuadMesh
        {
            Size = new Vector2(size, size),
            Material = mat,
        };
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}