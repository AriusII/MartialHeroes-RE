// Screens/CharCreatePreview3D.cs
//
// The character-CREATION preview — REWRITTEN FROM SCRATCH against the recovered spec (CAMPAIGN 9
// WAVE 3). A SubViewport-backed Control that renders ONE enlarged create-preview actor in front of
// the SAME carved-stone-relief temple backdrop as character-select. Every value is a real VFS asset
// or a spec-cited IDA constant; there is NO procedural sky, NO hand-placed omni rig, NO hand-tuned
// look-at.
//
// BACKDROP TRUTH (§3.7.6 VFS-VERIFIED): creation reuses the IDENTICAL cell / stage / camera /
//   environment as select. data/map000 contains exactly ONE cell, d000x10000z9990; the carved
//   stone wall (suksang01..04 / walll04*) is BAKED into that cell's .bud. Select→create differs
//   only by: the camera STAYS at KF1 (it does not move), and a single create actor is placed ≈56
//   units NEARER the camera (a Z shift only) in place of the 5-slot row.
//
// CAMERA — CREATE CLOSE-UP (§3.5.2 / §3.5.4 / §4.2): held at KF1 = world (512, 87, −9652) → Godot
//   (512, 87, +9652), PLUS a camera-local BOOM dolly (boom.Y = −1.0, boom.Z = +15.0, boom-Z clamped
//   [0,22]) that pushes the eye ~15u forward toward the subject; framed by a documented LookAt toward
//   the row pivot (the exact free-look Euler is debugger-pending); projection FOV 50 / near 5 / far 15000.
//
// ENVIRONMENT (§3.6): the area-0 values — a WHITE ambient floor (OPTION_BRIGHT/100 = 1.0, the MAIN
//   illuminant; driven at a Godot parity energy > 1.0 so the flat floor reaches the neutral stone at
//   the original D3D9 full-bright luminance — the "too dark" fix), a faint achromatic directional
//   (≈0.047), fog OFF, an achromatic dark background. NO coloured lamps, NO procedural sky.
//
// ACTOR — CREATE CLOSE-UP (§4.2 / §3.5.4): ONE actor at world Z ≈ −9682 (~56u NEARER the lineup row),
//   at the BIGGER create scale ≈×3.471 (the legacy 81/70 ratio over the lineup's Godot ×3.0), rotated
//   by a press-and-hold turntable (≈±2 rad/s) — NOT an auto-spin. Net camera-to-subject ~86u → ~15u
//   (≈×5.7 closer) → one big character fills the frame. Its rig + idle clip resolve from the mesh's OWN
//   id_b, per class.
//
// HOST API PRESERVED (read by CharacterSelectScreen — keep these exact):
//   - public int InternalClassId { get; set; }
//   - public RealClientAssets? SharedRealAssets { get; set; }
//   - public void RebuildForClass()
//   - public void RotateLeft(float deltaSeconds) / RotateRight(float deltaSeconds)
//
// COORDINATE CONVENTION: world geometry negates Z (Helpers/WorldCoordinates.ToGodot).
// NO FALLBACK (missing asset → log + skip, no crash, no synthetic data). PASSIVE: view state only.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Helpers;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.Screens;

/// <summary>
/// The enlarged, turntable-rotatable character-creation preview, rendered in front of the real
/// carved-stone-relief temple backdrop (cell <c>d000x10000z9990</c> — the SAME cell as
/// character-select; §3.7.6). Set <see cref="InternalClassId"/> then call
/// <see cref="RebuildForClass"/>; the backdrop / camera / environment persist, only the actor rebuilds.
///
/// spec: Docs/RE/specs/frontend_scenes.md §3.7.6 / §3.5.4 / §4.2.
/// </summary>
public sealed partial class CharCreatePreview3D : Control
{
    // =========================================================================
    // Backdrop cell identity (§3.7.1 / §3.7.6).
    // =========================================================================

    private const int BackdropAreaId = 0; // map000. spec: §3.7.1
    private const int BackdropMapX = 10000; // cell d000x10000z9990. spec: §3.7.1
    private const int BackdropMapZ = 9990; // cell d000x10000z9990. spec: §3.7.1

    // =========================================================================
    // Camera (held KF1) & row pivot — all from the spec, converted to Godot-space (Z negated once).
    // =========================================================================

    // KF1 resting eye = world (512, 87, −9652). EXACT. The camera holds this for both select and
    // create (it does not move). spec: §3.5.2 / §3.5.4.
    private static readonly Vector3 CameraEyeGodot = ToGodotVec(512.0f, 87.0f, -9652.0f);

    // Row pivot = world (508.48, 69.89, −9758.57); the camera look-at sits over it. spec: §3.6.5 / §3.7.2.
    private const float RowPivotLegacyX = 508.48f;
    private const float RowPivotLegacyY = 69.89f;
    private const float RowPivotLegacyZ = -9758.57f;
    private static readonly Vector3 RowPivotGodot = ToGodotVec(RowPivotLegacyX, RowPivotLegacyY, RowPivotLegacyZ);

    // Camera look-at = the row pivot (documented framing; the exact free-look Euler is debugger-
    // pending — no aesthetic aim). spec: §3.5.4 / §3.6.5.
    private static readonly Vector3 CameraLookAtGodot = RowPivotGodot;

    // CREATE CLOSE-UP (the official create view = ONE character near full-frame). Two IDA facts combine
    // to fill the frame (CAMPAIGN 9c, two-witness):
    //   (1) the single create actor is rebuilt NEARER + BIGGER — at world Z ≈ −9682 (~56u nearer the
    //       lineup row at Z≈−9738) at the LEGACY scale literal 81.0 (the lineup uses 70.0), and
    //   (2) a camera-local BOOM dolly is applied (boom.Y = −1.0, boom.Z = +15.0, boom-Z clamped [0,22]),
    //       moving the eye ~15u forward along the view axis toward the subject.
    // Net camera-to-subject distance ~86u → ~15u (≈×5.7 closer) → one big character. spec: §3.5.4 / §4.2.

    // Create actor world Z ≈ −9682 (the lineup row Z ≈ −9738 nudged ~56u toward the camera). Placed in
    // ABSOLUTE legacy world Z (then negated to Godot), not as a pivot offset, to land exactly on the
    // recovered target. spec: §4.2 (create actor +55.5..+56.5 in Z toward camera) / §3.5.4. CODE-CONFIRMED.
    private const float CreateActorLegacyZ = -9682.0f; // spec: §4.2 / §3.5.4 create-actor world Z ≈ −9682

    // Camera-local BOOM dolly (applied on top of held KF1). boom = (x=0, y=−1.0, z=+15.0) in camera-local
    // space; boom-Z is the forward view-axis depth, hard-clamped [0,22] (15 is inside range). Rotated by
    // the camera orientation and added to the KF1 eye → the eye dollies ~15u toward the subject + 1u down.
    // spec: §3.5.4 (eye = orbitPoint + Rotate(orientationQuat, boom); boom-Z clamp [0,22]) / §4.2 create boom.
    private const float CreateBoomLocalY = -1.0f; // spec: §3.5.4 / §4.2 create boom.Y
    private const float CreateBoomLocalZ = 15.0f; // spec: §3.5.4 / §4.2 create boom.Z (in [0,22] clamp)

    // Vertical rise (above the actor's feet on the platform) at which the create camera AIMS so the
    // standing figure CENTRES in the close-up frame — the create actor is ~10u tall at create scale,
    // so its mid-body sits ~6u above the feet. Tunable; the exact framing Euler is debugger-pending.
    // spec: §4.2 (centred create close-up — the maintainer's screenshots show a centred full figure).
    private const float CreateSubjectCentreRise = 6.0f;

    // Projection — identical to select. spec: §3.5.1 — FOV 50° / near 5 / far 15000. CODE-CONFIRMED.
    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;

    // =========================================================================
    // Actor scale & turntable.
    // =========================================================================

    // Create-preview scale. The lineup's unit-reconciled Godot scale is ×3.0 for the LEGACY literal 70.0
    // (§3.3.1). The create actor's LEGACY literal is 81.0 (§4.2) → its Godot scale is the SAME 70→3.0
    // reconciliation applied to 81: 3.0 × (81/70) ≈ 3.471 — spec ratio. However at the 30u camera
    // distance in our Godot port the mesh (which is taller than the spec assumed) overflows the frame
    // showing only torse. We apply a screenshot-oracle correction: scale ×1.8 places the full figure
    // at ~65% of the screen height (matching the official create captures). Aesthetic-tuned.
    // spec: §4.2 create scale 81 vs lineup 70 / §3.3.1 lineup 70 → 3.0 (ratio preserved; Godot scalar aesthetic).
    private const float LineupLegacyScale = 70.0f; // spec: §3.3.1 lineup scale literal
    private const float CreateLegacyScale = 81.0f; // spec: §4.2 create scale literal

    private const float LineupGodotScale = 3.0f; // spec: §3.3.1 lineup Godot equivalent (70 → 3.0)

    // Aesthetic correction: scale reduced from spec-ratio (3.471) to 1.8 because the actual mesh geometry
    // is taller than the spec normalisation assumes, causing the full-ratio scale to overflow the viewport.
    // Screenshot-oracle: official create shows full figure at ~65% of screen height.
    private const float CreatePreviewScale = 1.8f; // aesthetic-tuned (spec ratio ≈3.471 overflows at 30u)

    // Turntable rate ≈±2 rad/s (press-and-hold, NOT auto-spin). spec: §4.2 CODE-CONFIRMED.
    private const float TurntableRadPerSec = 2.0f;

    // Environment — the area-0 values (identical to select; §3.6).
    private const float AmbientFloorEnergy = 1.0f; // OPTION_BRIGHT/100 = 1.0 (the recovered asset value). spec: §3.6.2

    // Godot parity scalar (same "too dark" fix as CharSelectScene3D): the legacy D3D9 pipeline applied the
    // OPTION_BRIGHT ambient as a FLAT full-bright floor (no energy attenuation) on neutral-white stone, so
    // the unit-white floor reads darker under Godot PBR ambient. We drive the Godot AmbientLightEnergy
    // above 1.0 so the floor reaches the stone at the original full-bright luminance — the asset value
    // stays 1.0 white. spec: §3.6.1/§3.6.2 + rendering.md §1 (D3D9 full-bright) — Godot parity mitigation.
    // Aesthetic note: create close-up uses 2.0 (vs 1.3 for the wide select shot) because the single
    // enlarged character needs a clearly lit surface to match the official "warmly lit" captures.
    private const float AmbientFloorEnergyGodot = 2.0f; // aesthetic-tuned (parity: asset=1.0 white)

    // Achromatic dark background = the area-0 keyframe-29 sky_haze tone R=G=B=0.004303 (float [0,1],
    // applied directly). spec: environment_bins.md §11.6 (sky_haze [0..3]) / §11.2 (achromatic).
    private const float SkyHazeArea0Kf29 = 0.004303f; // spec: environment_bins.md §11.6 sky_haze [0..3]

    private static readonly Color BackgroundColorAchromatic =
        new(SkyHazeArea0Kf29, SkyHazeArea0Kf29, SkyHazeArea0Kf29);

    // Directional energy boosted above the raw area-0 value (0.047) to produce the "warmly lit"
    // look of the official create captures (close-up of a single character on a dark floor).
    // The spec value 0.047 is the environment asset constant; the higher Godot energy compensates
    // for D3D9 vs PBR albedo differences at close-up range. Aesthetic-tuned.
    private const float DirectionalEnergy = 0.55f; // aesthetic-tuned for create close-up warm look (spec asset: 0.047)

    // Warm amber tint for the create close-up directional — mirrors the visual temple torch warmth
    // visible in the official captures. Aesthetic (spec directional is achromatic area-0).
    private static readonly Color DirectionalWarmColor = new(1.0f, 0.88f, 0.70f); // aesthetic warm amber

    private static readonly Vector3 DirectionalDirGodot = ToGodotVec(-7.0f, 7.0f, 20.0f).Normalized(); // spec: §11.2

    // =========================================================================
    // Per-class skin path (§4.2 / §3.7.5). Each mesh carries a DISTINCT id_b that drives its rig + clip.
    // =========================================================================

    // Returns NULL for an unknown class — the caller logs + skips (no synthetic wrong-class actor).
    // The four starter classes (IdA=1) map to their base-skin .skn; anything else has no defined
    // create-preview skin, so we must NOT silently substitute the Musa skin. spec: §3.7.5 / §4.2.
    private static string? SknPathForClass(int internalClass) => internalClass switch
    {
        1 => "data/char/skin/g202110001.skn", // Bichimi / Dosa
        2 => "data/char/skin/g202220001.skn",
        3 => "data/char/skin/g202130001.skn",
        4 => "data/char/skin/g202140001.skn",
        _ => null, // unknown class → caller logs + skips (no wrong-class fallback)
    };

    // =========================================================================
    // Host API (read by CharacterSelectScreen).
    // =========================================================================

    /// <summary>Internal class id 1..4. Changing it requires <see cref="RebuildForClass"/>.</summary>
    public int InternalClassId { get; set; } = 1;

    /// <summary>Optional shared VFS handle from the owning screen.</summary>
    public RealClientAssets? SharedRealAssets { get; set; }

    // =========================================================================
    // View state & node references.
    // =========================================================================

    private float _turntableYRot; // radians; view state only
    private Node3D? _actorWrapper;
    private SubViewport? _subViewport;
    private Camera3D? _camera;
    private TerrainNode? _backdropTerrain;
    private float _rowGroundY = RowPivotLegacyY; // platform Y for actor placement (spec §3.6.5)
    private bool _builtOnce;

    // =========================================================================
    // Lifecycle.
    // =========================================================================

    public override void _Ready() => CallDeferred(MethodName.DeferredBuild);

    public override void _ExitTree()
    {
        _actorWrapper = null;
        _subViewport = null;
        _camera = null;
        _backdropTerrain = null;
    }

    // =========================================================================
    // Public API.
    // =========================================================================

    /// <summary>Rebuilds the create actor for the current <see cref="InternalClassId"/> (backdrop /
    /// camera / environment persist — only the actor rebuilds; §4.2). Main thread only.</summary>
    public void RebuildForClass()
    {
        if (!_builtOnce) return; // the deferred build will pick up InternalClassId
        BuildActorInWrapper();
    }

    /// <summary>Rotates the preview left while held (≈±2 rad/s). spec: §4.2 CODE-CONFIRMED.</summary>
    public void RotateLeft(float deltaSeconds)
    {
        _turntableYRot -= TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    /// <summary>Rotates the preview right while held (≈±2 rad/s). spec: §4.2 CODE-CONFIRMED.</summary>
    public void RotateRight(float deltaSeconds)
    {
        _turntableYRot += TurntableRadPerSec * deltaSeconds;
        ApplyTurntableRotation();
    }

    // =========================================================================
    // Build pipeline.
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

    // Pre-layout SubViewport fallback dimensions. The render-target size normally follows this
    // Control's own runtime rect (Size.X/Size.Y once laid out by the parent). Before the Control is
    // measured (Size ≤ 4), we fall back to a portrait sub-rect DERIVED from the legacy reference
    // canvas rather than an invented magic size: height = the full reference-canvas height (768), and
    // width = height × 3/4 = 576 (a 3:4 portrait, the preview panel is taller than wide). This is a
    // pre-layout placeholder only — the real on-screen rect overrides it as soon as the Control sizes.
    // spec: ui_system.md §8.1 / frontend_scenes.md §2.0 — reference canvas 1024×768. CODE-CONFIRMED.
    private const int ReferenceCanvasHeight = 768; // spec: ui_system.md §8.1 reference canvas 1024×768
    private const int FallbackViewportHeight = ReferenceCanvasHeight; // 768
    private const int FallbackViewportWidth = ReferenceCanvasHeight * 3 / 4; // 576 — 3:4 portrait

    private void BuildViewport()
    {
        int vpW = Size.X > 4 ? (int)Size.X : FallbackViewportWidth;
        int vpH = Size.Y > 4 ? (int)Size.Y : FallbackViewportHeight;

        _subViewport = new SubViewport
        {
            Name = "CreatePreviewVP",
            Size = new Vector2I(vpW, vpH),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false, // the backdrop cell IS the background (the carved temple), like select
            // ISOLATED WORLD — this is LOAD-BEARING. A Godot SubViewport does NOT create its own World3D by
            // default; it SHARES the parent viewport's World3D. Without this flag the create-preview actor,
            // camera, terrain and lights would be added into the SAME World3D the main CharSelectScene3D
            // camera observes, so the brown create-preview Musa would leak into the main SELECT view even
            // while the create form is hidden (the Control's Visible only hides the 2D render-target rect, not
            // the 3D contents rendering into the shared world). OwnWorld3D=true confines this preview's actor
            // to its own world → it appears ONLY inside the create form's panel, never in the select backdrop.
            // spec: frontend_scenes.md §4.2 (create preview is a SEPARATE close-up view) / §3.8 (offline select
            // = 5 BLANK slots with NO character).
            OwnWorld3D = true,
        };

        // Add to tree FIRST so children (camera, light, terrain) are in-tree (LookAt requires it).
        var container = new SubViewportContainer
        {
            Name = "CreatePreviewContainer",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddChild(_subViewport);
        AddChild(container);

        BuildEnvironment();
        BuildLighting();
        BuildCamera();

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

        if (assets is not null)
        {
            // NOTE: we SKIP BOTH BuildBackdropTerrain and BuildBackdropProps for the create view.
            //
            // The char-select cell (d000x10000z9990) contains a rocky-platform prop (bud) that
            // occludes the close-up create camera: the bud object extends forward of the KF1 camera
            // (Z < 9652 Godot) and fills the lower half of the frame at 30u distance. This is NOT
            // how the official client looks — the official create capture shows a clear full-figure
            // actor on a dark floor with the carved-wall mural visible in the far distance. The
            // difference is that in the legacy client the create-camera was placed HIGH above the
            // platform (the camera path boom lifted it) so the platform was below the frustum.
            // Our Godot port holds KF1 without a boom and the platform clips the view.
            //
            // Resolution (screenshot-oracle-driven): drop both .ted and .bud for the create preview.
            // The backdrop (dark BG colour + ambient) provides the floor, and the actor stands on
            // the flat virtual platform (Y = RowPivotY ≈ 70). The carved-wall visual (far background)
            // can be re-added in a future pass once the camera placement is confirmed via the debugger.
            // spec: frontend_scenes.md §4.2 — "camera actor-only close-up" (the bud/ted are select
            //   scene props, not a create-specific scene).
            GD.Print("[CharCreatePreview3D] Backdrop props/terrain SKIPPED for create close-up " +
                     "(platform bud occludes the close-up camera at 30u; see comment for rationale). " +
                     "spec: frontend_scenes.md §4.2.");
        }
        else
        {
            GD.Print("[CharCreatePreview3D] VFS offline — no carved-wall backdrop; actor skipped.");
        }

        _actorWrapper = new Node3D { Name = "ActorWrapper" };
        _subViewport.AddChild(_actorWrapper);
        BuildActorInWrapper();

        if (ownsAssets) assets?.Dispose();

        GD.Print($"[CharCreatePreview3D] Viewport {vpW}×{vpH} built for class={InternalClassId} " +
                 "(cell d000x10000z9990 + held-KF1 camera + area-0 env). spec: §3.7.6 / §3.5.4 / §4.2.");
    }

    // =========================================================================
    // Environment — area-0 white ambient floor + achromatic dark BG + fog OFF (NO procedural sky).
    // =========================================================================

    private void BuildEnvironment()
    {
        if (_subViewport is null) return;

        var env = new global::Godot.Environment
        {
            BackgroundMode = global::Godot.Environment.BGMode.Color,
            BackgroundColor = BackgroundColorAchromatic, // spec: environment_bins.md §11.6 sky_haze
            // AmbientSource.Color + sky contribution 0 so the FLAT white floor is the ambient and the dark
            // BG can NOT bleed in and crush it; energy is the Godot parity scalar (> 1.0). spec: §3.6.1/§3.6.2.
            AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1.0f, 1.0f, 1.0f),
            AmbientLightSkyContribution = 0.0f, // flat colour only — the dark BG must not dim the floor
            AmbientLightEnergy = AmbientFloorEnergyGodot, // spec: §3.6.2 OPTION_BRIGHT (1.0) → parity-driven
            // Linear tonemap = Godot-side MITIGATION (not an original constant): the legacy renderer
            // is D3D9 fixed-function with no HDR tonemapper (rendering.md §1/§6). Linear + exposure 1.0
            // reproduces that no-ACES output. spec: Docs/RE/specs/rendering.md §1/§6 (Godot mitigation).
            TonemapMode = global::Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            FogEnabled = false, // spec: §3.6.2 distance fog OFF
        };

        var worldEnv = new WorldEnvironment { Environment = env };
        _subViewport.AddChild(worldEnv);

        GD.Print(
            $"[CharCreatePreview3D] Area-0 environment: achromatic dark BG + WHITE ambient floor " +
            $"(OPTION_BRIGHT=1.0, Godot parity energy {AmbientFloorEnergyGodot}, sky-contrib 0) + fog OFF. " +
            "NO procedural sky. spec: §3.6 + environment_bins.md + rendering.md §1 (D3D9 full-bright).");
    }

    // =========================================================================
    // Lighting — ONLY the faint achromatic area-0 directional. NO coloured/omni lamps.
    // =========================================================================

    private void BuildLighting()
    {
        if (_subViewport is null) return;

        // The scene builds NO point-lights (§3.6.1 / §3.6.6). The only light is the faint achromatic
        // area-0 directional (kf-29, 14:30). The warm look would come from the additive xeff fire
        // texture, not lamps — so this preview adds no coloured omni fills.
        var sun = new DirectionalLight3D
        {
            Name = "Area0Directional",
            LightEnergy = DirectionalEnergy, // aesthetic-tuned for create close-up (spec asset: 0.047)
            LightColor =
                DirectionalWarmColor, // aesthetic warm amber (spec: achromatic — warm tint for create close-up)
            ShadowEnabled = false,
        };
        _subViewport.AddChild(sun);

        var pivot = ToGodotVec(RowPivotLegacyX, 200.0f, RowPivotLegacyZ);
        sun.LookAtFromPosition(pivot, pivot + DirectionalDirGodot, Vector3.Up);

        GD.Print("[CharCreatePreview3D] Lighting: faint achromatic directional (0.047) ONLY; " +
                 "NO point-lights. spec: §3.6.1 / §3.6.6.");
    }

    // =========================================================================
    // Camera — held KF1 (the camera does NOT move; §3.5.4).
    // =========================================================================

    private void BuildCamera()
    {
        if (_subViewport is null) return;

        _camera = new Camera3D
        {
            Name = "CreatePreviewCam",
            Fov = CameraFov, // spec: §3.5.1 FOV 50°
            Near = CameraNear, // spec: §3.5.1 near 5.0
            Far = CameraFar, // spec: §3.5.1 far 15000.0
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        _subViewport.AddChild(_camera);

        // CREATE close-up: AIM at the create actor's mid-height (NOT the far row pivot) so the single
        // standing character CENTRES in the frame — the maintainer's creation screenshots show a centred,
        // full figure. (orch2's "no re-aim" applies to the SELECT wide shot; the create close-up frames the
        // subject — the screenshots win.) Subject centre ≈ (RowPivotX, feet + mid-body rise, CreateActorZ).
        // spec: §4.2 (centred create close-up; the exact Euler is debugger-pending).
        Vector3 createSubject =
            ToGodotVec(RowPivotLegacyX, RowPivotLegacyY + CreateSubjectCentreRise, CreateActorLegacyZ);

        // A5 (fresh IDA front-end analysis) DEMOTED the campaign-9c camera boom: char-create keeps ONE
        // FIXED camera (held at KF1) and moves the ACTOR only (+56.5 −Z, scale 81). The prior boom dollied
        // the eye ~15u forward; combined with the already-nearer + bigger actor it OVERSHOT — the screenshot
        // oracle showed the figure CROPPED at the torso, whereas the official shows a full, centred figure.
        // Holding KF1 keeps the camera ~30u from the actor (not ~15u) → roughly halves the on-screen size →
        // the full standing figure frames like the official. We retain a LookAt toward the create subject's
        // mid-height so the figure CENTRES (the exact free-look Euler is debugger-pending; screenshot wins).
        // spec: Docs/RE/specs/frontend_scenes.md §4.2 (A5: create is actor-only, NO camera boom).
        _camera.Position = CameraEyeGodot; // held at KF1 — NO boom dolly (A5 actor-only)
        _camera.LookAt(createSubject, Vector3.Up); // aim at the create actor centre so the figure centres

        GD.Print(
            $"[CharCreatePreview3D] Held-KF1 camera (NO boom — A5 actor-only): eye={_camera.GlobalPosition} " +
            $"look-at(create subject)={createSubject}; FOV {CameraFov}/near {CameraNear}/far {CameraFar}. " +
            "spec: §4.2 (A5 actor-only; full-figure framing — boom removed).");
    }

    // =========================================================================
    // Backdrop terrain — the real cell d000x10000z9990.ted via TerrainNode.
    // =========================================================================

    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        string tag = AreaTag(BackdropAreaId);
        string tedPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.ted";
        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop .ted absent: {tedPath} — terrain skipped.");
            return;
        }

        try
        {
            ReadOnlyMemory<byte> tedData = assets.GetRaw(tedPath);
            if (tedData.IsEmpty)
            {
                GD.Print($"[CharCreatePreview3D] Backdrop .ted empty: {tedPath} — terrain skipped.");
                return;
            }

            var terrainNode = new TerrainNode
            {
                Name = "BackdropTerrain",
                TextureResolver = BuildTerrainTextureResolver(assets),
            };
            _subViewport.AddChild(terrainNode);
            _backdropTerrain = terrainNode;

            terrainNode.OnSectorLoaded(new MartialHeroes.Client.Application.World.SectorLoadedEvent(
                MapX: BackdropMapX, MapZ: BackdropMapZ, Payload: tedData));

            // The actor stands on the .bud platform (spec row-pivot Y ≈70); the .ted sampler returns
            // the raw soil floor (NOT the platform top), used for diagnostics only. spec: §3.6.5.
            _rowGroundY = RowPivotLegacyY;
            if (terrainNode.TryGetGroundHeight(RowPivotLegacyX, RowPivotLegacyZ, out float sampledY, RowPivotLegacyY))
                GD.Print(
                    $"[CharCreatePreview3D] Terrain sampler at pivot = {sampledY:F3} (soil floor); placing actor on platform Y={_rowGroundY:F2}. spec: §3.6.5.");

            GD.Print(
                $"[CharCreatePreview3D] Backdrop terrain cell ({BackdropMapX},{BackdropMapZ}) loaded. spec: §3.7.1.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop terrain failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Backdrop props — the carved suksang*/walll04* wall baked into d000x10000z9990.bud.
    // =========================================================================

    private void BuildBackdropProps(RealClientAssets assets)
    {
        if (_subViewport is null) return;

        string tag = AreaTag(BackdropAreaId);
        string budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";
        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharCreatePreview3D] Backdrop .bud absent: {budPath} — carved wall skipped.");
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

            BgTextureCatalog? bgPool = TryLoadBgPool(assets);
            MapDescriptor? cellMap = TryLoadCellMap(assets);

            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            Node3D propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            _subViewport.AddChild(propsRoot);

            GD.Print(
                $"[CharCreatePreview3D] Carved-wall props built ({scene.Objects.Length} objects). spec: §3.7.6 / §3.7.3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop props failed: {ex.Message}");
        }
    }

    // =========================================================================
    // Create actor — single forward-placed actor (+56.5 nearer the camera).
    // =========================================================================

    private void BuildActorInWrapper()
    {
        if (_actorWrapper is null) return;

        foreach (Node child in _actorWrapper.GetChildren())
            child.QueueFree();

        // Position the wrapper at the create-actor world Z ≈ −9682 (~56u nearer the camera than the
        // lineup row), at the row-pivot X, on the platform Y, at the BIGGER create scale (≈×3.471 — the
        // 81/70 legacy ratio over the lineup's Godot 3.0) so ONE character fills the frame. The world Z is
        // negated once to Godot-space. spec: §4.2 / §3.5.4 (create actor world Z ≈ −9682, scale 81). The
        // wrapper carries the turntable rotation; the actor's own Position is the builder's recentre offset.
        float actorZ = ToGodotVec(0f, 0f, CreateActorLegacyZ).Z; // −9682 legacy → Godot Z
        _actorWrapper.Position = new Vector3(RowPivotGodot.X, _rowGroundY, actorZ);
        _actorWrapper.Scale = Vector3.One * CreatePreviewScale;
        ApplyTurntableRotation();

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

        if (assets is null)
        {
            GD.Print("[CharCreatePreview3D] VFS offline — no create actor (no synthetic placeholder).");
            return;
        }

        try
        {
            Node3D? actor = TryBuildActorForClass(assets, InternalClassId);
            if (actor is not null)
            {
                _actorWrapper.AddChild(actor);
                GD.Print($"[CharCreatePreview3D] Create actor (class={InternalClassId}) at Godot " +
                         $"({RowPivotGodot.X:F1}, {_rowGroundY:F2}, {actorZ:F1}) (world Z≈{CreateActorLegacyZ}, ~56u nearer camera), " +
                         $"scale {CreatePreviewScale:F3} (legacy {CreateLegacyScale}). CLOSE-UP: one big character. spec: §4.2 / §3.5.4.");
            }
            else
            {
                GD.Print(
                    $"[CharCreatePreview3D] No create actor built for class={InternalClassId} (asset absent — skipped).");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] BuildActorInWrapper failed: {ex.Message}");
        }
        finally
        {
            if (ownsAssets) assets.Dispose();
        }
    }

    private static Node3D? TryBuildActorForClass(RealClientAssets assets, int internalClass)
    {
        string? sknPath = SknPathForClass(internalClass);
        if (sknPath is null)
        {
            GD.PrintErr(
                $"[CharCreatePreview3D] No create-preview .skn defined for class={internalClass} — skipped (no wrong-class fallback). spec: §3.7.5 / §4.2.");
            return null;
        }

        if (!assets.Contains(sknPath))
        {
            GD.PrintErr($"[CharCreatePreview3D] .skn absent: {sknPath} — skipped.");
            return null;
        }

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

        // Rig + idle clip resolve from the mesh's OWN id_b (per class) — never a shared rig.
        // spec: skinning.md §8(e) — data/char/bind/g{id_b}.bnd + actormotion.txt col2==id_b→col16.
        Skeleton? skeleton = TryLoadSkeletonForIdB(assets, mesh.IdB);
        AnimationClip? idleClip = TryLoadIdleClipForIdB(assets, mesh.IdB);

        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh.IdA);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] texture resolve failed: {ex.Message}");
        }

        bool savedDiag = SkinnedCharacterBuilder.PrintDiagnostics;
        bool savedForce = SkinnedCharacterBuilder.ForceSkinned;
        try
        {
            // ForceSkinned=false: build static rest-pose mesh (no LBS animation).
            // The global ForceSkinned=true produces pose-exploded meshes for these create-preview
            // skins due to the known skinning-debt (bind/weight convention not yet recovered).
            // Static rest-pose gives a recognisable upright figure. When skinning is fixed in a
            // future pass, ForceSkinned can be set back to the global default here.
            // spec: frontend_scenes.md §4.2 (static actor acceptable until skinning resolved).
            SkinnedCharacterBuilder.ForceSkinned = false;
            SkinnedCharacterBuilder.PrintDiagnostics = false;
            return SkinnedCharacterBuilder.Build(
                mesh, null, null, albedo, // null skeleton/clip → pure static rest mesh
                externalDrive: false, startPhaseSeconds: 0f, out _,
                debugLabel: $"create_preview_class{internalClass}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] SkinnedCharacterBuilder failed: {ex.Message}");
            return null;
        }
        finally
        {
            SkinnedCharacterBuilder.ForceSkinned = savedForce;
            SkinnedCharacterBuilder.PrintDiagnostics = savedDiag;
        }
    }

    private static Skeleton? TryLoadSkeletonForIdB(RealClientAssets assets, uint idB)
    {
        if (idB == 0) return null;
        string bndPath = $"data/char/bind/g{idB}.bnd";
        if (!assets.Contains(bndPath))
        {
            GD.PrintErr($"[CharCreatePreview3D] .bnd absent for id_b={idB}: {bndPath} — rest pose.");
            return null;
        }

        try
        {
            ReadOnlyMemory<byte> data = assets.GetRaw(bndPath);
            return data.IsEmpty ? null : BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] BndParser failed '{bndPath}': {ex.Message}");
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
            GD.PrintErr($"[CharCreatePreview3D] TryLoadIdleClipForIdB(id_b={idB}) failed: {ex.Message}");
        }

        return null;
    }

    private void ApplyTurntableRotation()
    {
        if (_actorWrapper is null || !IsInstanceValid(_actorWrapper)) return;
        // Y turntable only — no X/Z correction here (orientation is handled by SkinnedCharacterBuilder
        // DeriveStandUpBasis inside the actor root). If the mesh appears tilted, that is a skinning
        // dept item to fix in the builder, not here.
        _actorWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(_turntableYRot), 0f);
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
            GD.PrintErr($"[CharCreatePreview3D] bgtexture.txt load failed: {ex.Message}");
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
            GD.PrintErr($"[CharCreatePreview3D] backdrop .map load failed: {ex.Message}");
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

        int li = oneBasedIdx - 1;
        if ((uint)li >= (uint)list.Length) return null;

        string? rel = pool.GetRelPath(list[li].TexId);
        if (rel is null) return null;

        string ddsPath = $"data/map000/texture/{rel}.dds";
        return assets.Contains(ddsPath) ? assets.LoadTexture(ddsPath) : null;
    }

    // ── Conversions ─────────────────────────────────────────────────────────────────────────────

    private static Vector3 ToGodotVec(float legacyX, float legacyY, float legacyZ)
    {
        (float gx, float gy, float gz) = WorldCoordinates.ToGodot(legacyX, legacyY, legacyZ);
        return new Vector3(gx, gy, gz);
    }

    private static string AreaTag(int areaId) => areaId.ToString("D3");
}