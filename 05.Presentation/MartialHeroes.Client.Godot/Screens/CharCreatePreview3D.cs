// Screens/CharCreatePreview3D.cs
//
// A SubViewport-backed Control that renders the single, enlarged character-CREATION preview
// IN FRONT OF the real carved-stone-relief temple backdrop — the SAME cell as character-select.
//
// BACKDROP TRUTH (recovered CAMPAIGN 9, WAVE 2 — frontend_scenes.md §3.7.6 VFS-VERIFIED):
//   Character CREATION reuses the IDENTICAL cell / stage / camera / environment as character
//   SELECT. There is NO separate creation stage anywhere in the VFS — `data/map000` contains
//   exactly one cell, `d000x10000z9990`. The carved stone-relief wall (suksang01..04.dds) and
//   the bright portal/archway the player sees behind the create character are BAKED into that
//   one cell's `.bud` building geometry. Select→create differs only by:
//     - the camera STAYS put (KF1 rest pose; §3.5.4 — the camera does not move), and
//     - a single create-preview actor is placed ≈56 units NEARER the camera (a Z shift only,
//       §3.5.4 / §4.2) in place of the 5-slot row.
//   So this preview loads `map000` cell `d000x10000z9990` ONCE (terrain .ted + props .bud =
//   the carved wall), frames the camera on the KF1 rest pose, and builds the single forward-
//   placed create actor — exactly mirroring the CharSelectScene3D backdrop path (TerrainNode +
//   BudMeshBuilder), but inside this SubViewport so it shows as the create form's centre panel.
//   spec: Docs/RE/specs/frontend_scenes.md §3.7.6 VFS-VERIFIED (same cell, actor Z only).
//   spec: Docs/RE/specs/frontend_scenes.md §3.7.1 / §3.7.3 (cell files + carved-wall textures).
//
// CAMERA / ACTOR placement (frontend_scenes.md §3.5.2 / §3.5.4 / §4.2 CODE-CONFIRMED):
//   - The camera holds keyframe 1, anchored Godot-space rest pose (Z negated from legacy):
//       legacy KF1 eye (512, 87, −9652) → Godot (512, 87, 9652); look-at over the row pivot.
//     spec: §3.5.2 (KF1 = (512,87,−9652) exact) / §3.5.4 (held rest pose). CODE-CONFIRMED.
//   - The single create actor sits at the row pivot (508.48, 69.89, −9758.57 legacy →
//     Godot (508.48, 69.89, 9758.57)) shifted +56.5 NEARER the camera. In Godot-space the
//     camera (Z≈9652) is at SMALLER Z than the row (Z≈9758.6), so "nearer the camera" = a
//     56.5-unit DECREASE in Godot Z.  spec: §3.5.4 / §4.2 "+56.5 units nearer the camera".
//
// ACTOR (frontend_scenes.md §4.2 CODE-CONFIRMED):
//   - ONE actor, centred, scale 75 (vs slot-row scale 50). Here we reuse the slot-row Godot
//     PreviewScale=3.0 so the actor matches the backdrop's world scale (the .bud/.ted are at
//     native world units; the actor must scale to the same world frame, exactly like the slot
//     row in CharSelectScene3D).  spec: §4.2 "scale 75 vs the slots' 50" (slot-row scale).
//   - Rotation is PRESS-AND-HOLD turntable only (≈±2 rad/s while a rotate control is held).
//     NOT a continuous auto-spin.  spec: §4.2 CODE-CONFIRMED.
//   - Changing the class rebuilds the actor; the ±face buttons rebuild it too but the visible
//     3D face does NOT change (face feeds a separate 2D portrait).  spec: §4.2 CODE-CONFIRMED.
//   - Per-class IdB / .skn (§4.2 + §3.7.5):
//       class 1 → g202110001.skn  /  class 2 → g202220001.skn
//       class 3 → g202130001.skn  /  class 4 → g202140001.skn
//
// RIG/CLIP IDENTITY (the class-mismatch shatter fix):
//   A .skn mesh is authored against ONE skeleton named by its OWN id_b. That id_b selects BOTH
//   the skeleton (data/char/bind/g{id_b}.bnd) AND the matched idle clip (actormotion.txt
//   col2 == id_b → col16 → data/char/mot/g{...}.mot). The four creatable classes do NOT share a
//   rig: class 1 has id_b=1 (g1, 84 bones); class 4 (Warrior) has id_b=4 (g4, 89 bones). We
//   resolve the rig AND clip from the mesh's own id_b, PER class — never a single shared rig.
//   spec: Docs/RE/specs/skinning.md §8(e) "Rig/clip identity" — SAMPLE-VERIFIED / CODE-CONFIRMED.
//
// ENVIRONMENT (frontend_scenes.md §3.6 CODE-CONFIRMED — area-0 truth, identical to select):
//   The create backdrop is the SAME achromatic "dark stone temple" as select: achromatic dark
//   background + a WHITE ambient floor (OPTION_BRIGHT=100 → energy 1.0) + a very faint achromatic
//   directional key (~0.047) — NO coloured lamps. This is the recovered fix for the historical
//   "too dark / grey void" debt and keeps create lighting consistent with select.
//
// PASSIVE: zero game logic. View state only (which class/face, turntable angle).
// Reads VFS assets via RealClientAssets; rebuilds the actor node when the class changes.
// All Control mutation on the main thread.
//
// COORDINATE CONVENTION:
//   WorldCoordinates.ToGodot: (x, y, z) → (x, y, −z). spec: Helpers/WorldCoordinates.
//   All world positions below are already Godot-space (Z negated from spec/legacy values).

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The enlarged, turntable-rotatable character-creation preview shown during character creation,
/// rendered in front of the real carved-stone-relief temple backdrop (cell <c>d000x10000z9990</c>,
/// the SAME cell as character-select — frontend_scenes.md §3.7.6).
///
/// <para>Set <see cref="InternalClassId"/> (1..4) then call <see cref="RebuildForClass"/>.
/// The node hosts a <see cref="SubViewport"/> with the backdrop cell (terrain + .bud carved wall),
/// the area-0 environment, the static KF1 camera, and the single create actor placed ≈56 units
/// nearer the camera than the select row.</para>
///
/// <para>Turntable: the owning form calls <see cref="RotateLeft"/>/<see cref="RotateRight"/>
/// while the rotate button is held; ~2 rad/s.</para>
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.7.6 / §3.5.4 / §4.2 CODE-CONFIRMED.
/// </summary>
public sealed partial class CharCreatePreview3D : Control
{
    // =========================================================================
    // Spec constants — backdrop cell identity (the carved-wall temple cell).
    // spec: Docs/RE/specs/frontend_scenes.md §3.7.1 / §3.7.6. CODE-CONFIRMED / VFS-VERIFIED.
    // =========================================================================

    private const int BackdropAreaId = 0; // map000. spec: §3.7.1 CODE-CONFIRMED
    private const int BackdropMapX = 10000; // cell d000x10000z9990. spec: §3.7.1 CODE-CONFIRMED
    private const int BackdropMapZ = 9990; // cell d000x10000z9990. spec: §3.7.1 CODE-CONFIRMED

    // =========================================================================
    // Spec constants — camera (KF1 rest pose) and actor placement (Godot-space).
    // =========================================================================

    // Camera resting eye = keyframe 1, anchored. Legacy (512, 87, −9652) → Godot (512, 87, 9652)
    // (Z negated). The camera holds this pose for both select and create (it does not move).
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 (KF1 exact) / §3.5.4 (held). CODE-CONFIRMED.
    private static readonly Vector3 CameraEyeGodot = new(512.0f, 87.0f, 9652.0f);

    // Row pivot — the focal point of the backdrop. Legacy (508.48, 69.89, −9758.57) →
    // Godot (508.48, 69.89, 9758.57). The camera looks over this point; the create actor is
    // placed here, then shifted toward the camera (see CreateActorZNudgeGodot).
    // spec: Docs/RE/specs/frontend_scenes.md §3.6.5 / §3.7.2 — row pivot. CODE-CONFIRMED.
    private const float RowPivotGodotX = 508.48f;
    private const float RowPivotLegacyZ = -9758.57f; // legacy Z (TerrainNode samples in legacy space)
    private const float RowPivotGodotZ = 9758.57f; // = −RowPivotLegacyZ
    private const float RowPivotYFallback = 70.0f; // spec: §3.3.1 fallback row base Y. CODE-CONFIRMED

    // Camera look-at — over the row pivot at mid-torso height of the ×3 actor (base ≈70 → top ≈94).
    // Matches CharSelectScene3D's resting look-at so create frames the same temple view.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 — look-at over the row centre. CODE-CONFIRMED.
    private static readonly Vector3 CameraLookAtGodot = new(508.48f, 82.0f, 9738.0f);

    // The create actor sits +56.5 units NEARER the camera than the select row (a Z shift only).
    // In Godot-space the camera (Z≈9652) is at SMALLER Z than the row (Z≈9758.6), so "nearer the
    // camera" = a 56.5-unit DECREASE in Godot Z.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.4 / §4.2 — "+56.5 units nearer the camera". CODE-CONFIRMED.
    private const float CreateActorZNudgeGodot = -56.5f;

    // Camera projection — identical to select.
    // spec: Docs/RE/specs/frontend_scenes.md §3.5.1 — FOV 50° / near 5 / far 15000. CODE-CONFIRMED.
    private const float CameraFov = 50.0f; // spec: §3.5.1 vertical FOV 50°. CODE-CONFIRMED
    private const float CameraNear = 5.0f; // spec: §3.5.1 near clip 5.0. CODE-CONFIRMED
    private const float CameraFar = 15000.0f; // spec: §3.5.1 far clip 15000.0. CODE-CONFIRMED

    // =========================================================================
    // Spec constants — actor scale and turntable.
    // =========================================================================

    // Actor scale — reuse the slot-row Godot PreviewScale=3.0 so the create actor matches the
    // backdrop's native world scale (the .ted/.bud are at world units). spec §4.2 says "scale 75
    // vs the slots' 50"; in our Godot frame the slot row is scale 3.0 (CharSelectScene3D.PreviewScale),
    // so the create actor uses the same 3.0 to share the world frame with the carved-wall backdrop.
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 "scale 75 vs the slots' 50". CODE-CONFIRMED (slot scale).
    private const float CreatePreviewScale = 3.0f;

    // Turntable rate: ≈±2 rad/s. spec: Docs/RE/specs/frontend_scenes.md §4.2. CODE-CONFIRMED.
    private const float TurntableRadPerSec = 2.0f;

    // =========================================================================
    // Per-class skin path table (§4.2 / §3.7.5). CODE-CONFIRMED.
    // =========================================================================

    // Starter base-skin mesh per internal class id 1..4 (each mesh carries a DISTINCT id_b that
    // drives its own rig + idle clip — see TryBuildActorForClass).
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 / §3.7.5 CODE-CONFIRMED.
    private static string SknPathForClass(int internalClass) => internalClass switch
    {
        1 => "data/char/skin/g202110001.skn",
        2 => "data/char/skin/g202220001.skn",
        3 => "data/char/skin/g202130001.skn",
        4 => "data/char/skin/g202140001.skn",
        _ => "data/char/skin/g202110001.skn", // fallback Musa
    };

    // =========================================================================
    // View state
    // =========================================================================

    /// <summary>Internal class id 1..4. Changing this requires calling <see cref="RebuildForClass"/>.</summary>
    public int InternalClassId { get; set; } = 1;

    /// <summary>Optional shared VFS handle from the owning screen.</summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    // Current turntable Y-rotation (radians). View state only, no domain meaning.
    private float _turntableYRot;

    // The 3D actor wrapper node (at the forward-placed world position). We rotate this for turntable.
    private Node3D? _actorWrapper;

    // SubViewport references.
    private SubViewport? _subViewport;
    private Camera3D? _camera;

    // Terrain node for ground-height sampling (the create actor's base Y).
    private TerrainNode? _backdropTerrain;

    // Sampled ground Y at the row pivot (filled after the .ted loads); fallback until then.
    private float _rowGroundY = RowPivotYFallback;

    // Whether the SubViewport (backdrop + camera + env) has been built.
    private bool _builtOnce;

    // =========================================================================
    // Godot lifecycle
    // =========================================================================

    public override void _Ready()
    {
        // Build the viewport, backdrop and initial character.
        CallDeferred(MethodName.DeferredBuild);
    }

    public override void _Process(double delta)
    {
        // Turntable is driven externally by RotateLeft/RotateRight from the owning form
        // (press-and-hold pattern). No auto-spin here.
        // spec: Docs/RE/specs/frontend_scenes.md §4.2 "NOT a continuous auto-spin". CODE-CONFIRMED.
    }

    public override void _ExitTree()
    {
        _actorWrapper = null;
        _subViewport = null;
        _camera = null;
        _backdropTerrain = null;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Rebuilds the 3D actor for the current <see cref="InternalClassId"/> (the backdrop, camera
    /// and environment persist — only the actor is rebuilt, per §4.2). Main thread only.
    /// </summary>
    public void RebuildForClass()
    {
        if (!_builtOnce)
        {
            // Not yet initialised — the deferred build will pick up InternalClassId.
            return;
        }

        BuildActorInWrapper();
    }

    /// <summary>
    /// Rotates the preview left. Call once per frame while the rotate-left button is held.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.2 "≈±2 rad/s turntable". CODE-CONFIRMED.
    /// </summary>
    public void RotateLeft(float deltaSeconds)
    {
        _turntableYRot -= TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    /// <summary>
    /// Rotates the preview right. Call once per frame while the rotate-right button is held.
    /// spec: Docs/RE/specs/frontend_scenes.md §4.2 "≈±2 rad/s turntable". CODE-CONFIRMED.
    /// </summary>
    public void RotateRight(float deltaSeconds)
    {
        _turntableYRot += TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    // =========================================================================
    // Build pipeline
    // =========================================================================

    private void DeferredBuild()
    {
        if (!IsInstanceValid(this)) return;

        try
        {
            BuildViewport();
            _builtOnce = true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] DeferredBuild failed: {ex.Message}");
        }
    }

    private void BuildViewport()
    {
        // Determine control size (fallback 420×600 if not yet laid out — matches the centre panel).
        int vpW = Size.X > 4 ? (int)Size.X : 420;
        int vpH = Size.Y > 4 ? (int)Size.Y : 600;

        _subViewport = new SubViewport
        {
            Name = "CreatePreviewVP",
            Size = new Vector2I(vpW, vpH),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            // Opaque BG: the backdrop cell IS the background (the carved temple), like the select
            // scene — not a transparent cut-out over the 2D form.
            TransparentBg = false,
        };

        // Add the SubViewport (inside its container) to the tree FIRST, so every child built below
        // (camera, lights, terrain) is in-tree — Camera3D.LookAt and DirectionalLight3D.LookAt*
        // require the node to be inside the scene tree.
        var container = new SubViewportContainer
        {
            Name = "CreatePreviewContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_subViewport);
        AddChild(container);

        // ── Environment — area-0 "dark stone temple", identical to CharSelectScene3D ──────────
        BuildEnvironment();

        // ── Camera — static KF1 rest pose (the camera does NOT move; same as select) ──────────
        BuildCamera();

        // ── Lighting — faint achromatic directional key (no coloured lamps) ───────────────────
        BuildLighting();

        // ── Backdrop cell d000x10000z9990 (terrain .ted + props .bud = the carved wall) ───────
        RealClientAssets? assets = SharedRealAssets;
        if (assets is null)
        {
            try { assets = RealClientAssets.TryOpen(); }
            catch (Exception ex) { GD.PrintErr($"[CharCreatePreview3D] VFS open failed: {ex.Message}"); }
        }

        if (assets is not null)
        {
            BuildBackdropTerrain(assets);
            BuildBackdropProps(assets);
        }
        else
        {
            GD.Print("[CharCreatePreview3D] VFS offline — no carved-wall backdrop; actor placeholder only.");
        }

        // ── Actor wrapper for the single create actor (turntable rotation applied here) ───────
        _actorWrapper = new Node3D { Name = "ActorWrapper" };
        _subViewport.AddChild(_actorWrapper);

        // Build the initial character actor at the forward-placed world position.
        BuildActorInWrapper();

        GD.Print($"[CharCreatePreview3D] Viewport {vpW}×{vpH} built for class={InternalClassId} " +
                 "(carved-wall backdrop cell d000x10000z9990 + KF1 camera + area-0 env). " +
                 "spec: frontend_scenes.md §3.7.6 / §3.5.4 / §4.2 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Environment — area-0 "dark stone temple" (mirrors CharSelectScene3D.BuildEnvironment)
    // =========================================================================

    private void BuildEnvironment()
    {
        if (_subViewport is null) return;

        // Area-0 environment TRUTH (CAMPAIGN 9): the create backdrop is the SAME achromatic,
        // statically-lit "dark stone temple" as select — NOT a blue sky and NOT a torchlit cavern.
        // Achromatic dark background + a WHITE ambient floor (OPTION_BRIGHT=100 → energy 1.0) is the
        // scene's main illuminant; the directional is a faint achromatic key. No coloured lamps.
        // spec: Docs/RE/specs/frontend_scenes.md §3.6 + formats/environment_bins.md (area-0). CODE-CONFIRMED.

        // global::Godot.Environment to avoid the sibling-namespace collision (CS0234).
        var env = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.04f, 0.04f, 0.04f), // achromatic dark. spec: environment_bins.md §11.6
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1.0f, 1.0f, 1.0f), // white floor — OPTION_BRIGHT=100. spec: §3.6.2 CODE-CONFIRMED
            AmbientLightEnergy = 1.0f, // spec: §3.6.2 OPTION_BRIGHT/100 default = 1.0. CODE-CONFIRMED
            TonemapMode = global::Godot.Environment.ToneMapper.Linear, // faithful D3D9 output (no ACES darkening)
            TonemapExposure = 1.0f,
            FogEnabled = false, // spec: §3.6.2 — distance fog OFF (the invisible-chars fix). CODE-CONFIRMED
            GlowEnabled = true,
            GlowIntensity = 0.6f, // Aesthetic
            GlowStrength = 1.0f, // Aesthetic
            GlowBloom = 0.05f, // Aesthetic
            GlowHdrThreshold = 0.8f, // only HDR sun blooms — avoids haloing the white-ambient scene
        };
        env.Set("glow_levels/1", 1.0f);
        env.Set("glow_levels/2", 0.0f);
        env.Set("glow_levels/3", 0.0f);
        env.Set("glow_levels/4", 0.0f);
        env.Set("glow_levels/5", 0.0f);
        env.Set("glow_levels/6", 0.0f);
        env.Set("glow_levels/7", 0.0f);

        var worldEnv = new WorldEnvironment { Environment = env };
        _subViewport.AddChild(worldEnv);

        GD.Print("[CharCreatePreview3D] Area-0 environment built: achromatic dark BG + WHITE ambient floor " +
                 "(energy 1.0) + fog OFF. spec: frontend_scenes.md §3.6 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Camera — single static KF1 rest pose (the camera does NOT move; §3.5.4)
    // =========================================================================

    private void BuildCamera()
    {
        if (_subViewport is null) return;

        // SINGLE STATIC perspective camera, built once. No orbit, no keyframes, no animation.
        // The camera holds keyframe 1 for both select and create.
        // spec: Docs/RE/specs/frontend_scenes.md §3.5.2 / §3.5.4 CODE-CONFIRMED.
        _camera = new Camera3D
        {
            Name = "CreatePreviewCam",
            Fov = CameraFov, // spec: §3.5.1 FOV 50°. CODE-CONFIRMED
            Near = CameraNear, // spec: §3.5.1 near 5.0. CODE-CONFIRMED
            Far = CameraFar, // spec: §3.5.1 far 15000.0. CODE-CONFIRMED
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        _subViewport.AddChild(_camera);

        // Position and orient: the KF1 resting eye looking over the row pivot (the one fixed pose).
        _camera.Position = CameraEyeGodot;
        _camera.LookAt(CameraLookAtGodot, Vector3.Up);

        GD.Print($"[CharCreatePreview3D] Static KF1 camera built: eye={CameraEyeGodot} look-at={CameraLookAtGodot} " +
                 $"(Godot-space). FOV {CameraFov}/near {CameraNear}/far {CameraFar}. " +
                 "spec: frontend_scenes.md §3.5.2/§3.5.4 CODE-CONFIRMED (KF1 rest pose, camera does not move).");
    }

    // =========================================================================
    // Lighting — faint achromatic directional key (mirrors CharSelectScene3D, no coloured lamps)
    // =========================================================================

    private void BuildLighting()
    {
        if (_subViewport is null) return;

        // Area-0 lighting TRUTH: the scene fill is the WHITE ambient floor (built in BuildEnvironment).
        // The directional is a very FAINT ACHROMATIC key (~0.047 grey), NOT a warm torch rig. We add a
        // couple of dim achromatic fills for gentle local shaping of the create actor only — they must
        // not re-introduce any colour. Consistent with the select scene's lighting.
        // spec: Docs/RE/formats/environment_bins.md §11.3/§9.4 SAMPLE-VERIFIED (energy + light vector);
        //       Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED (count ~5, range ~1024).

        // Faint achromatic directional key — area-0 kf-29 directional ~0.047 with the static light
        // vector (-7,7,20) legacy world; Godot-space negates Z → (-7,7,-20).
        // spec: Docs/RE/formats/environment_bins.md §9.4/§10.6/§11.3. SAMPLE-VERIFIED.
        var sun = new DirectionalLight3D
        {
            Name = "Area0Directional",
            LightEnergy = 0.047f, // spec: environment_bins.md §11.3 SAMPLE-VERIFIED (area-0 kf-29 directional)
            LightColor = new Color(1.0f, 1.0f, 1.0f), // achromatic — area-0 is grey. spec: §11.2 R=G=B
            ShadowEnabled = false,
        };
        _subViewport.AddChild(sun);
        var sunPivot = new Vector3(512.0f, 200.0f, 9738.0f);
        sun.LookAtFromPosition(sunPivot, sunPivot + new Vector3(-7.0f, 7.0f, -20.0f).Normalized(), Vector3.Up);

        // Dim achromatic fills, range 1024 (§3.6.1 count/range CONFIRMED; colours UNVERIFIED → white).
        // Positioned around the forward-placed create actor (≈56 nearer the camera than the row).
        // spec: Docs/RE/specs/frontend_scenes.md §3.6.1 CODE-CONFIRMED (range ~1024).
        float actorZ = RowPivotGodotZ + CreateActorZNudgeGodot; // ≈ 9702
        (string name, Vector3 pos, float energy)[] fills =
        [
            ("FillCharacterKey", new Vector3(512.0f, 95.0f, actorZ - 40.0f), 0.9f),
            ("FillLeftPillar", new Vector3(480.5f, 89.0f, RowPivotGodotZ), 0.6f),
            ("FillRightPillar", new Vector3(536.5f, 89.0f, RowPivotGodotZ), 0.6f),
        ];
        foreach ((string name, Vector3 pos, float energy) in fills)
        {
            var fill = new OmniLight3D
            {
                Name = name,
                LightEnergy = energy, // Aesthetic (dim achromatic fill; the ambient floor dominates)
                LightColor = new Color(1.0f, 1.0f, 1.0f), // achromatic — area-0 is grey
                OmniRange = 1024.0f, // spec: frontend_scenes.md §3.6.1 CODE-CONFIRMED range ~1024
                OmniAttenuation = 1.0f,
                ShadowEnabled = false,
                Position = pos, // Aesthetic positions
            };
            _subViewport.AddChild(fill);
        }

        GD.Print("[CharCreatePreview3D] Area-0 lighting built: faint achromatic directional (0.047) + dim " +
                 "achromatic fills (range 1024); white ambient floor is the main illuminant (no coloured lamps). " +
                 "spec: environment_bins.md §11.3 + frontend_scenes.md §3.6.1 CODE-CONFIRMED.");
    }

    // =========================================================================
    // Backdrop terrain — single cell d000x10000z9990 (reuses TerrainNode, like CharSelectScene3D)
    // =========================================================================

    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        // spec: Docs/RE/specs/frontend_scenes.md §3.7.1 — backdrop cell d000x10000z9990. CODE-CONFIRMED.
        string tag = AreaTag(BackdropAreaId);
        string tedPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.ted";

        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop terrain not found: {tedPath} — skipping .ted.");
            return;
        }

        try
        {
            ReadOnlyMemory<byte> tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharCreatePreview3D] Backdrop .ted is empty: {tedPath}");
                return;
            }

            // Resolve terrain textures via the confirmed two-hop chain (TERRAIN section).
            // spec: terrain.md §5.6 Block 3 — 1-based TextureIndexGrid → .map TERRAIN TEXTURES → bgtexture → .dds.
            Func<int, ImageTexture?> texResolver = BuildTerrainTextureResolver(assets);

            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = texResolver,
            };
            _subViewport.AddChild(terrainNode);
            _backdropTerrain = terrainNode;

            // Feed the single sector directly (no streaming needed for one backdrop cell).
            var evt = new MartialHeroes.Client.Application.World.SectorLoadedEvent(
                MapX: BackdropMapX,
                MapZ: BackdropMapZ,
                Payload: tedData);
            terrainNode.OnSectorLoaded(evt);

            // The terrain sampler returns the .ted soil/rock floor (~26), NOT the .bud platform top.
            // Place the actor on the PLATFORM at the spec row-pivot Y (≈70), matching CharSelectScene3D
            // (which also uses the fixed pivot Y for placement and the sampler for diagnostics only).
            // spec: frontend_scenes.md §3.6.5 — row pivot Y = 69.89 ≈ 70.0. CODE-CONFIRMED.
            _rowGroundY = RowPivotYFallback;
            if (terrainNode.TryGetGroundHeight(RowPivotGodotX, RowPivotLegacyZ, out float sampledY, RowPivotYFallback))
            {
                GD.Print($"[CharCreatePreview3D] Terrain sampler at pivot: {sampledY:F3} (soil/floor, NOT platform top). " +
                         $"Using spec row pivot Y={RowPivotYFallback} for actor placement. spec §3.6.5 CODE-CONFIRMED.");
            }

            GD.Print($"[CharCreatePreview3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. " +
                     "spec: frontend_scenes.md §3.7.1 CODE-CONFIRMED.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop terrain failed: {ex.Message}");
        }
    }

    private void BuildBackdropProps(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        // The carved stone-relief wall + portal are baked into this cell's .bud building geometry.
        // spec: Docs/RE/specs/frontend_scenes.md §3.7.6 — suksang*/walll04* baked into the .bud. VFS-VERIFIED.
        string tag = AreaTag(BackdropAreaId);
        string budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";

        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop .bud (carved wall) not found: {budPath} — skipping props.");
            return;
        }

        try
        {
            BudScene? scene = assets.LoadBud(budPath);
            if (scene is null || scene.Objects.Length == 0)
            {
                GD.Print("[CharCreatePreview3D] Backdrop .bud empty — no carved-wall props.");
                return;
            }

            BgTextureCatalog? bgPool = null;
            MapDescriptor? cellMap = null;

            try
            {
                string txtPath = "data/map000/texture/bgtexture.txt";
                if (assets.Contains(txtPath))
                    bgPool = BgTextureTxtParser.Parse(assets.GetRaw(txtPath));
            }
            catch { /* non-critical */ }

            try
            {
                string mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
                if (assets.Contains(mapPath))
                    cellMap = MapDescriptorParser.Parse(assets.GetRaw(mapPath));
            }
            catch { /* non-critical */ }

            // Building textures use the same two-hop chain via the BUILDING section of the .map.
            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            Node3D propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            _subViewport.AddChild(propsRoot);

            GD.Print($"[CharCreatePreview3D] Carved-wall backdrop props built ({scene.Objects.Length} objects). " +
                     "spec: frontend_scenes.md §3.7.6 CODE-CONFIRMED (suksang*/walll04* baked into .bud).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop props failed: {ex.Message}");
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
            GD.PrintErr($"[CharCreatePreview3D] bgtexture.txt load failed: {ex.Message}");
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
            GD.PrintErr($"[CharCreatePreview3D] backdrop .map load failed: {ex.Message}");
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
    // Create actor — single forward-placed actor (+56.5 nearer the camera than the row)
    // =========================================================================

    private void BuildActorInWrapper()
    {
        if (_actorWrapper is null) return;

        // Clear any existing actor.
        foreach (Node child in _actorWrapper.GetChildren())
            child.QueueFree();

        RealClientAssets? assets = SharedRealAssets;
        bool ownsAssets = false;
        if (assets is null)
        {
            try
            {
                assets = RealClientAssets.TryOpen();
                ownsAssets = assets is not null;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharCreatePreview3D] VFS open failed: {ex.Message}");
            }
        }

        // Position the actor wrapper at the row pivot, shifted +56.5 NEARER the camera (Godot −Z),
        // at the sampled ground Y, scaled to match the backdrop's world frame. The wrapper carries
        // the turntable rotation. spec: frontend_scenes.md §4.2 / §3.5.4 CODE-CONFIRMED.
        float actorZ = RowPivotGodotZ + CreateActorZNudgeGodot;
        _actorWrapper.Position = new Vector3(RowPivotGodotX, _rowGroundY, actorZ);
        _actorWrapper.Scale = Vector3.One * CreatePreviewScale;
        ApplyTurntableRotation();

        if (assets is null)
        {
            GD.Print("[CharCreatePreview3D] VFS offline — placeholder actor only.");
            var box = new MeshInstance3D
            {
                Name = "Placeholder",
                Mesh = new BoxMesh { Size = new Vector3(2f, 4f, 1f) },
                Position = new Vector3(0f, 2f, 0f),
            };
            var mat = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.30f, 0.50f) };
            box.MaterialOverride = mat;
            _actorWrapper.AddChild(box);
            return;
        }

        try
        {
            Node3D? actor = TryBuildActorForClass(assets, InternalClassId);
            if (actor is not null)
            {
                // SkinnedCharacterBuilder applies the SAME upright stand-up pivot + RecentreRoot the
                // slot row uses, so the actor stands vertically with feet at local Y≈0 centred on X/Z.
                // Its Position is the recentre OFFSET — keep it; the wrapper supplies the world placement.
                // spec: frontend_scenes.md §4.2 / §3.3.1 — preview reuses the slot stand-up + framing.
                _actorWrapper.AddChild(actor);

                GD.Print($"[CharCreatePreview3D] Create actor built for class={InternalClassId} at world " +
                         $"({RowPivotGodotX:F1}, {_rowGroundY:F3}, {actorZ:F1}) Godot-space (+56.5 nearer camera), " +
                         $"scale={CreatePreviewScale}. spec: frontend_scenes.md §4.2 / §3.5.4 CODE-CONFIRMED.");
            }
            else
            {
                GD.Print($"[CharCreatePreview3D] Actor build returned null for class={InternalClassId}.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] BuildActorInWrapper failed: {ex.Message}");
        }
        finally
        {
            if (ownsAssets) assets?.Dispose();
        }
    }

    private static Node3D? TryBuildActorForClass(RealClientAssets assets, int internalClass)
    {
        // Resolve .skn path.
        // spec: Docs/RE/specs/frontend_scenes.md §4.2 / §3.7.5 CODE-CONFIRMED.
        string sknPath = SknPathForClass(internalClass);
        if (!assets.Contains(sknPath))
        {
            GD.PrintErr($"[CharCreatePreview3D] .skn not found: {sknPath}");
            return null;
        }

        // Parse mesh.
        SkinnedMesh mesh;
        try
        {
            ReadOnlyMemory<byte> raw = assets.GetRaw(sknPath);
            if (raw.IsEmpty) return null;
            mesh = SknParser.Parse(raw);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] SknParser failed '{sknPath}': {ex.Message}");
            return null;
        }

        // Skeleton — resolved from the MESH'S OWN id_b (NOT a shared g1.bnd). The skin's bind-local
        // vertex offsets are baked against exactly this skeleton's rest pose; the wrong rig shatters.
        // spec: Docs/RE/specs/skinning.md §8(e) — data/char/bind/g{id_b}.bnd, per class.
        Skeleton? skeleton = null;
        string bndPath = $"data/char/bind/g{mesh.IdB}.bnd";
        if (mesh.IdB != 0 && assets.Contains(bndPath))
        {
            try
            {
                ReadOnlyMemory<byte> bndData = assets.GetRaw(bndPath);
                if (!bndData.IsEmpty)
                    skeleton = BndParser.Parse(bndData);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CharCreatePreview3D] BndParser failed '{bndPath}': {ex.Message}");
            }
        }
        else
        {
            GD.PrintErr($"[CharCreatePreview3D] .bnd not found for id_b={mesh.IdB}: {bndPath}");
        }

        // Idle animation — the MATCHED clip for this rig (actormotion.txt col2 == id_b → col16).
        // spec: Docs/RE/specs/skinning.md §8(e) — actormotion col2==id_b → col16, per class.
        AnimationClip? idleClip = TryLoadIdleClip(assets, mesh.IdB);

        GD.Print($"[CharCreatePreview3D] class={internalClass} resolved rig from id_b={mesh.IdB}: " +
                 $"bnd='{bndPath}' bones={(skeleton?.Bones.Length ?? 0)} " +
                 $"idle={(idleClip is null ? "none" : $"{idleClip.Tracks.Length}trk/{idleClip.FrameCount}f")}. " +
                 "spec: skinning.md §8(e) per-class rig/clip identity.");

        // Texture.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] texture resolve failed: {ex.Message}");
        }

        // Build via the standard SkinnedCharacterBuilder (same path as the slot previews).
        // spec: frontend_scenes.md §4.2 "reuse the same SkinnedCharacterBuilder + texture path". CODE-CONFIRMED.
        bool savedDiag = SkinnedCharacterBuilder.PrintDiagnostics;
        try
        {
            SkinnedCharacterBuilder.ForceSkinned = true;
            SkinnedCharacterBuilder.PrintDiagnostics = false;
            return SkinnedCharacterBuilder.Build(
                mesh, skeleton, idleClip, albedo,
                externalDrive: false,
                startPhaseSeconds: 0f,
                out _,
                debugLabel: $"create_preview_class{internalClass}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] SkinnedCharacterBuilder failed: {ex.Message}");
            return null;
        }
        finally
        {
            SkinnedCharacterBuilder.PrintDiagnostics = savedDiag;
        }
    }

    /// <summary>
    /// Loads the idle <c>.mot</c> clip MATCHED to a skin's <paramref name="actorClassId"/> (its
    /// id_b) from <c>actormotion.txt</c>: the row whose col2 == id_b gives the idle motion id in
    /// col16, resolved to <c>data/char/mot/g{idle}.mot</c>.
    /// spec: Docs/RE/specs/skinning.md §8(e); CLAUDE.md §Recovered asset mappings (actormotion idle).
    /// </summary>
    private static AnimationClip? TryLoadIdleClip(RealClientAssets assets, uint actorClassId)
    {
        if (actorClassId == 0) return null;

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
                if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != actorClassId) continue;

                string idle = cols[16].Trim();
                if (idle.Length == 0 || idle == "0") return null;

                string motPath = $"data/char/mot/g{idle}.mot";
                if (!assets.Contains(motPath)) return null;

                ReadOnlyMemory<byte> motData = assets.GetRaw(motPath);
                if (motData.IsEmpty) return null;

                return AnimationParser.Parse(motData);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] TryLoadIdleClip(id_b={actorClassId}) failed: {ex.Message}");
        }

        return null;
    }

    private void ApplyTurntableRotation()
    {
        if (_actorWrapper is null || !IsInstanceValid(_actorWrapper)) return;
        // Rotate the actor wrapper around world Y-axis (turntable). View state only.
        _actorWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(_turntableYRot), 0f);
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}
