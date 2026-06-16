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
//   stone wall (suksang01..04 / walll04*) is BAKED into that cell's .bud. The backdrop (terrain +
//   carved wall) IS rebuilt; only the foreground platform prop that occludes the close-up is culled
//   geometrically (CullForegroundOccluders), so the carved relief wall stays visible behind the
//   figure. Select→create differs only by: the camera STAYS at KF1 (it does not move), and a single
//   create actor is placed ≈56 units NEARER the camera (a Z shift only) in place of the 5-slot row.
//
// CAMERA — CREATE CLOSE-UP (§3.5.4 / §3.5.6 / §4.2): ACTOR-ONLY. The camera is held FIXED at KF1 =
//   world (512, 87, −9652) → Godot (512, 87, +9652) — there is NO camera boom (the campaign-9c boom
//   was DEMOTED, §3.5.6 CONFLICT C2; A5 = the create scene moves the ACTOR, not the camera). It is
//   framed by a documented LookAt toward the create subject's mid-height so the single figure centres
//   (the exact free-look Euler is debugger-pending); projection FOV 50 / near 5 / far 15000.
//
// ENVIRONMENT (§3.6): the area-0 values — a WHITE ambient floor (OPTION_BRIGHT/100 = 1.0, the MAIN
//   illuminant; driven at the SAME Godot parity energy as CharSelectScene3D so both 3D scenes light
//   the shared cell identically — the "too dark" fix), a faint achromatic directional (≈0.047), fog
//   OFF, an achromatic dark background. NO coloured lamps, NO procedural sky.
//
// ACTOR — CREATE CLOSE-UP (§4.2 / §3.5.4): ONE actor at world Z ≈ −9682 (~56u NEARER the lineup row),
//   at the BIGGER create scale (the legacy 81/70 ratio over the lineup's Godot ×3.0 → ≈×3.471),
//   rotated by a press-and-hold turntable (≈±2 rad/s) — NOT an auto-spin. It is built through the FULL
//   skinned + animated path (real skeleton + idle clip resolved from the mesh's OWN id_b, per class) —
//   exactly like CharSelectScene3D, no longer a static rest pose.
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

    // CREATE CLOSE-UP (the official create view = ONE character near full-frame). The create scene is
    // ACTOR-ONLY (A5, fresh IDA front-end analysis): the camera holds KF1 unchanged and ONLY the actor
    // is rebuilt NEARER + BIGGER — at world Z ≈ −9682 (~56u nearer the lineup row at Z≈−9738) at the
    // LEGACY scale literal 81.0 (the lineup uses 70.0). There is NO camera boom (the campaign-9c boom
    // dolly was DEMOTED — §3.5.6 CONFLICT C2). spec: §3.5.4 / §3.5.6 / §4.2.

    // Create actor world Z ≈ −9682 (the lineup row Z ≈ −9738 nudged ~56u toward the camera). Placed in
    // ABSOLUTE legacy world Z (then negated to Godot), not as a pivot offset, to land exactly on the
    // recovered target. spec: §4.2 (create actor +55.5..+56.5 in Z toward camera) / §3.5.4. CODE-CONFIRMED.
    private const float CreateActorLegacyZ = -9682.0f; // spec: §4.2 / §3.5.4 create-actor world Z ≈ −9682

    // Create actor world X = anchorX(2048.0) − 1536.5 = 511.5 (NOT the look-at pivot X 508.48). Godot X =
    // legacy X (only Z is negated by ToGodot). spec: §4.2 — IDA SelectWindow_BuildZoomPreviewActor
    // store @0x545e1e, scene anchor @0x54824a (2048.0). CONFIRMED 263bd994.
    private const float CreateActorGodotX = 511.5f;

    // Fallback vertical rise (above the actor's feet) at which the create camera AIMS when the actor's
    // real height is not yet known. The camera is RE-AIMED at the actor's measured vertical centre once
    // the figure is built (AimCameraAtActorCentre) — that measured aim is what frames the whole figure;
    // this constant is only the pre-actor placeholder. spec: §4.2 (centred create close-up).
    private const float CreateSubjectCentreRiseFallback = 17.0f;

    // Projection — identical to select. spec: §3.5.1 — FOV 50° / near 5 / far 15000. CODE-CONFIRMED.
    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;

    // =========================================================================
    // Actor scale & turntable.
    // =========================================================================

    // Create-preview scale, reconciled against the legacy 81/70 ratio. The lineup's unit-reconciled
    // Godot scale is ×3.0 for the LEGACY literal 70.0 (§3.3.1); the create actor's LEGACY literal is
    // 81.0 (§4.2). Applying the SAME 70→3.0 reconciliation to 81 gives the spec-ratio Godot scale
    // 3.0 × (81/70) ≈ 3.471. With the camera now held at KF1 (actor-only, ~30u away — the boom that
    // overshot the figure is removed, frontend F4) the ratio-correct scale frames the full figure as
    // the official captures show, so the prior aesthetic 1.8 is replaced by the spec ratio.
    // spec: §4.2 (create scale 81) / §3.3.1 (lineup 70 → ×3.0; the 81/70 ratio preserved into create).
    private const float LineupLegacyScale = 70.0f; // spec: §3.3.1 lineup scale literal
    private const float CreateLegacyScale = 81.0f; // spec: §4.2 create scale literal
    private const float LineupGodotScale = 3.0f; // spec: §3.3.1 lineup Godot equivalent (70 → 3.0)

    // Godot create scale = lineup Godot scale × (create legacy / lineup legacy) = 3.0 × (81/70) ≈ 3.471.
    // The 81/70 ratio is CODE-CONFIRMED (§4.2 / §3.3.1); only the 70→3.0 unit reconciliation is the
    // shared port choice (identical to CharSelectScene3D.PreviewScale). spec: §4.2 / §3.3.1.
    private const float CreatePreviewScale = LineupGodotScale * (CreateLegacyScale / LineupLegacyScale);

    // Turntable rate ≈±2 rad/s (press-and-hold, NOT auto-spin). spec: §4.2 CODE-CONFIRMED.
    private const float TurntableRadPerSec = 2.0f;

    // Environment — the area-0 values (identical to select; §3.6).
    private const float AmbientFloorEnergy = 1.0f; // OPTION_BRIGHT/100 = 1.0 (the recovered asset value). spec: §3.6.2

    // Godot parity scalar (same "too dark" fix as CharSelectScene3D): the legacy D3D9 pipeline applied the
    // OPTION_BRIGHT ambient as a FLAT full-bright floor (no energy attenuation) on neutral-white stone, so
    // the unit-white floor reads darker under Godot PBR ambient. UNIFIED with CharSelectScene3D
    // (AmbientFloorEnergyGodot = 0.65, frontend F4): both 3D scenes light the SAME cell at the SAME energy,
    // so create and select read identically — the prior divergent 2.0 had no spec basis. The asset value
    // stays 1.0 white. spec: §3.6.1/§3.6.2 + rendering.md §1 (D3D9 full-bright) — Godot parity mitigation.
    private const float AmbientFloorEnergyGodot = 0.65f; // unified with CharSelectScene3D (parity: asset=1.0 white)

    // Achromatic dark background = the area-0 keyframe-29 sky_haze tone R=G=B=0.004303 (float [0,1],
    // applied directly). spec: environment_bins.md §11.6 (sky_haze [0..3]) / §11.2 (achromatic).
    private const float SkyHazeArea0Kf29 = 0.004303f; // spec: environment_bins.md §11.6 sky_haze [0..3]

    private static readonly Color BackgroundColorAchromatic =
        new(SkyHazeArea0Kf29, SkyHazeArea0Kf29, SkyHazeArea0Kf29);

    // Directional energy boosted above the raw area-0 value (0.047) to produce the "warmly lit"
    // look of the official create captures (close-up of a single character on a dark floor).
    // The spec value 0.047 is the environment asset constant; the higher Godot energy compensates
    // for D3D9 vs PBR albedo differences at close-up range. Aesthetic-tuned.
    // Faint achromatic area-0 directional, UNIFIED with CharSelectScene3D (the spec asset value 0.047,
    // white). The prior boosted 0.55 + warm amber directional was an aesthetic divergence with no spec
    // basis; the warm read comes from the shared warm-amber ambient floor + the additive xeff fire
    // billboards, not the directional. spec: environment_bins.md §11.3 (area-0 kf-29 directional ≈0.047).
    private const float DirectionalEnergy = 0.047f; // spec: environment_bins.md §11.3 (area-0 directional)
    private static readonly Color DirectionalColor = new(1.0f, 1.0f, 1.0f); // achromatic — area-0 R=G=B. spec: §11.2

    private static readonly Vector3 DirectionalDirGodot = ToGodotVec(-7.0f, 7.0f, 20.0f).Normalized(); // spec: §11.2

    // =========================================================================
    // Per-class skin path (§4.2 / §3.7.5). Each mesh carries a DISTINCT id_b that drives its rig + clip.
    // =========================================================================

    // Class → base-skin .skn resolved through the ONE shared ClassAppearanceResolver — the SAME
    // table CharSelectScene3D uses, so a class shows the IDENTICAL body in both screens. The earlier
    // create-only table invented stems (g202220001 / g202130001 / g202140001) that are absent from
    // the VFS, so classes 2/3/4 rendered nothing — those invented stems are removed. Returns NULL for
    // an unknown class (caller logs + skips, no wrong-class fallback). The four §3.7.5 starter meshes
    // (IdA=1) are the spec-grounded stopgap for the full skin.txt appearance chain (skinning.md §3.5.2).
    // spec: Docs/RE/specs/frontend_scenes.md §3.7.5 / Docs/RE/specs/skinning.md §3.5.2.
    private static string? SknPathForClass(int internalClass)
        => ClassAppearanceResolver.SknPathForClass(internalClass);

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
            // The create backdrop is the SAME cell + carved wall as select (§3.7.6): the carved
            // suksang*/walll04* relief wall the player sees behind the create figure is BAKED into
            // d000x10000z9990.bud. Re-enable the backdrop so the official temple wall is visible in
            // the far background instead of an empty dark void. The foreground rocky-platform prop
            // that occludes the close-up camera is CULLED geometrically inside BuildBackdropProps
            // (CullForegroundOccluders) — we clip the occluder, NOT the whole cell. The terrain
            // height-field is also rebuilt so the floor reads as the same temple stage as select.
            // spec: Docs/RE/specs/frontend_scenes.md §3.7.6 (create reuses the identical cell + wall).
            BuildBackdropTerrain(assets);
            BuildBackdropProps(assets);
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
            // Warm amber ambient floor UNIFIED with CharSelectScene3D (R=1.0, G=0.72, B=0.44) so both
            // 3D scenes shift the neutral stone to the same warm tan; the asset value stays
            // OPTION_BRIGHT=1.0 white — the tint is the shared Godot parity mitigation. spec: §3.6.1/§3.6.2.
            AmbientLightColor = new Color(1.0f, 0.72f, 0.44f),
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
            LightEnergy = DirectionalEnergy, // spec: environment_bins.md §11.3 (area-0 directional ≈0.047)
            LightColor = DirectionalColor, // achromatic — area-0 R=G=B (unified with select). spec: §11.2
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
        // subject — the screenshots win.) This is the PLACEHOLDER aim using the fallback rise; the camera is
        // RE-AIMED at the actor's measured vertical centre by AimCameraAtActorCentre once the figure exists.
        // spec: §4.2 (centred create close-up; the exact Euler is debugger-pending).
        Vector3 createSubject =
            ToGodotVec(RowPivotLegacyX, RowPivotLegacyY + CreateSubjectCentreRiseFallback, CreateActorLegacyZ);

        // A5 (fresh IDA front-end analysis) DEMOTED the campaign-9c camera boom: char-create keeps ONE
        // FIXED camera (held at KF1) and moves the ACTOR only (+56.5 −Z, scale 81). With the camera held at
        // KF1 ~30u from the create actor, the close range means a feet-level look-at frames only the BOOTS
        // (a ~34u-tall figure at FOV 50° overshoots the top of frame). The fix is NOT a boom — the camera
        // stays at KF1 — but to aim at the figure's VERTICAL CENTRE (≈ feet + ½·height) so the whole figure
        // frames. AimCameraAtActorCentre does that from the actor's MEASURED AABB once built; this is the
        // pre-actor placeholder aim. spec: Docs/RE/specs/frontend_scenes.md §4.2 (A5: actor-only, NO boom).
        _camera.Position = CameraEyeGodot; // held at KF1 — NO boom dolly (A5 actor-only)
        _camera.LookAt(createSubject, Vector3.Up); // placeholder aim — re-aimed at the measured centre

        GD.Print(
            $"[CharCreatePreview3D] Held-KF1 camera (NO boom — A5 actor-only): eye={_camera.GlobalPosition} " +
            $"placeholder look-at={createSubject}; FOV {CameraFov}/near {CameraNear}/far {CameraFar}. " +
            "Re-aimed at the figure's measured vertical centre once built. spec: §4.2 (actor-only; full-figure).");
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

            global::MartialHeroes.Assets.Mapping.BgTextureCatalog? bgPool = TryLoadBgPool(assets);
            MapDescriptor? cellMap = TryLoadCellMap(assets);

            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            Node3D propsRoot = BudMeshBuilder.Build(scene, budTexResolver);
            propsRoot.Name = "BackdropProps";
            _subViewport.AddChild(propsRoot);

            // Cull the FOREGROUND platform prop that occludes the close-up create camera, keeping the
            // FAR carved wall visible (§3.7.6). The camera holds KF1 (Godot Z ≈ +9652) and looks toward
            // increasing Z (the wall behind the row at Z ≈ +9738); the create subject sits at Godot
            // Z ≈ +9682. A prop whose geometry lies nearer the camera than the subject (max Z below the
            // subject Z) is between the eye and the figure → hide it. This clips ONLY the occluder, not
            // the whole cell — the carved relief wall (far, higher Z) is retained.
            // spec: Docs/RE/specs/frontend_scenes.md §3.7.6 (cull the foreground platform, keep the wall).
            int culled = CullForegroundOccluders(propsRoot, ToGodotVec(0f, 0f, CreateActorLegacyZ).Z);

            GD.Print(
                $"[CharCreatePreview3D] Carved-wall props built ({scene.Objects.Length} objects, " +
                $"{culled} foreground occluder mesh(es) culled). spec: §3.7.6 / §3.7.3.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] Backdrop props failed: {ex.Message}");
        }
    }

    // Hides every MeshInstance3D under <paramref name="propsRoot"/> whose global AABB lies fully
    // nearer the camera than the subject along the +Z view axis (max Z < subjectGodotZ), i.e. the
    // foreground platform prop that would occlude the close-up. The far carved wall (higher Z) is
    // kept. Returns the count hidden. spec: frontend_scenes.md §3.7.6 (cull the foreground occluder).
    private static int CullForegroundOccluders(Node3D propsRoot, float subjectGodotZ)
    {
        int culled = 0;
        var stack = new Stack<Node>();
        stack.Push(propsRoot);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            if (node is MeshInstance3D mi && mi.Mesh is not null)
            {
                Aabb local = mi.Mesh.GetAabb();
                Aabb global = mi.GlobalTransform * local;
                float maxZ = global.Position.Z + global.Size.Z;
                // Fully in front of the subject (nearer the camera) → occluder → hide it.
                if (maxZ < subjectGodotZ)
                {
                    mi.Visible = false;
                    culled++;
                }
            }

            foreach (Node child in node.GetChildren())
                stack.Push(child);
        }

        return culled;
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
        _actorWrapper.Position = new Vector3(CreateActorGodotX, _rowGroundY, actorZ);
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

                // Re-aim the held-KF1 camera at the figure's MEASURED vertical centre so the WHOLE
                // figure frames (not the boots). The figure stands from _rowGroundY (feet) up by its
                // scaled height; aiming at feet-level over-frames the top. spec: §4.2 (centred close-up).
                AimCameraAtActorCentre(actor);

                GD.Print($"[CharCreatePreview3D] Create actor (class={InternalClassId}) at Godot " +
                         $"({CreateActorGodotX:F1}, {_rowGroundY:F2}, {actorZ:F1}) (world Z≈{CreateActorLegacyZ}, ~56u nearer camera), " +
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
        // spec: skinning.md §8(e) — data/char/bind/g{id_b}.bnd + actormotion.txt col2==id_b→col15 (idle).
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

        try
        {
            // FULL skinned + animated path: build with the REAL skeleton + idle clip (resolved from
            // the mesh's own id_b above). The create preview now animates exactly like every other
            // actor — the prior ForceSkinned=false override (static rest pose, null skeleton/clip) was
            // removed: the LBS math is CONFIRMED correct against IDA and CharSelectScene3D already
            // drives the SAME class meshes through this same skinned path. The rest-pose cancellation
            // diagnostic stays on so a true regression is caught numerically, not by eyeballing.
            // spec: Docs/RE/specs/skinning.md §8(a)/§8(e) (animated actor) / §1 (CPU LBS).
            return SkinnedCharacterBuilder.Build(
                mesh, skeleton, idleClip, albedo,
                externalDrive: false, startPhaseSeconds: 0f, out _,
                debugLabel: $"create_preview_class{internalClass}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharCreatePreview3D] SkinnedCharacterBuilder failed: {ex.Message}");
            return null;
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
                if (cols.Length <= 15) continue;
                if (!uint.TryParse(cols[2].Trim(), out uint classId) || classId != idB) continue;

                // idle = motion_ids_a[0] = column 15 (record +0x40), IDB-confirmed operand-for-operand.
                // spec: Docs/RE/formats/actormotion.md §Per-record layout (col15=+0x40=idle); animation.md.
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
            GD.PrintErr($"[CharCreatePreview3D] TryLoadIdleClipForIdB(id_b={idB}) failed: {ex.Message}");
        }

        return null;
    }

    // Re-aims the held-KF1 camera at the build figure's measured VERTICAL CENTRE so the whole standing
    // figure frames in the close-up (not the boots). The actor is recentred with feet near local Y=0 and
    // placed in the wrapper at _rowGroundY, scaled by CreatePreviewScale, so the figure spans world Y from
    // ≈ _rowGroundY up by its scaled height. We read the actor's combined global AABB (all MeshInstance3D
    // descendants) to get its true rendered extent and aim at the AABB's Y centre over the create-actor
    // X/Z. This is a FRAMING aim only — the camera stays at KF1 (NO boom; A5 actor-only). If no mesh is
    // found yet (deferred build), the placeholder feet+rise aim from BuildCamera remains.
    // spec: Docs/RE/specs/frontend_scenes.md §4.2 (centred full-figure create close-up; actor-only).
    private void AimCameraAtActorCentre(Node3D actor)
    {
        if (_camera is null || !IsInstanceValid(_camera)) return;

        if (!TryGetCombinedGlobalAabb(actor, out Aabb aabb) || aabb.Size == Vector3.Zero)
            return; // mesh not built yet → keep the placeholder aim

        float centreY = aabb.Position.Y + aabb.Size.Y * 0.5f;
        float actorZ = ToGodotVec(0f, 0f, CreateActorLegacyZ).Z;
        var aim = new Vector3(CreateActorGodotX, centreY, actorZ);
        _camera.LookAt(aim, Vector3.Up);

        GD.Print($"[CharCreatePreview3D] Camera re-aimed at figure centre Y={centreY:F2} " +
                 $"(AABB minY={aabb.Position.Y:F2} h={aabb.Size.Y:F2}); full figure framed. spec: §4.2.");
    }

    // Combines the GLOBAL AABBs of every MeshInstance3D under <paramref name="root"/> (so the figure's
    // true rendered extent is measured regardless of the recentre/scale chain). Returns false if none.
    private static bool TryGetCombinedGlobalAabb(Node root, out Aabb combined)
    {
        combined = default;
        bool any = false;
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node node = stack.Pop();
            if (node is MeshInstance3D mi && mi.Mesh is not null)
            {
                Aabb g = mi.GlobalTransform * mi.Mesh.GetAabb();
                combined = any ? combined.Merge(g) : g;
                any = true;
            }

            foreach (Node child in node.GetChildren())
                stack.Push(child);
        }

        return any;
    }

    private void ApplyTurntableRotation()
    {
        if (_actorWrapper is null || !IsInstanceValid(_actorWrapper)) return;
        // Y turntable only — no X/Z correction here. Orientation falls out of the single handedness
        // conversion (world Z-negate) inside the skinning math; there is no per-rig stand-up rotation.
        // If the mesh appears tilted, that is a skinning convention item (the §8(b)/§9 quaternion remap
        // or a missing actor yaw, screenshot-verified), not a guessed angle to add here.
        _actorWrapper.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(_turntableYRot), 0f);
    }

    // ── Terrain/building texture two-hop chain (terrain.md §5.6 / §3.5 / §4.2) ───────────────────

    private Func<int, ImageTexture?> BuildTerrainTextureResolver(RealClientAssets assets)
    {
        global::MartialHeroes.Assets.Mapping.BgTextureCatalog? bgPool = TryLoadBgPool(assets);
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

    private static global::MartialHeroes.Assets.Mapping.BgTextureCatalog? TryLoadBgPool(RealClientAssets assets)
    {
        try
        {
            // Runtime form first: the binary bgtexture.lst the original client consumes (the
            // bgtexture.txt mirror is absent from a real packed data.vfs); fall back to the .txt
            // mirror only for dev/loose trees. spec: Docs/RE/specs/asset_pipeline.md §3 chain B.
            const string lstPath = "data/map000/texture/bgtexture.lst";
            if (assets.Contains(lstPath))
                return global::MartialHeroes.Assets.Mapping.BgTextureCatalog.FromLst(assets.GetRaw(lstPath));

            const string txtPath = "data/map000/texture/bgtexture.txt";
            return assets.Contains(txtPath)
                ? global::MartialHeroes.Assets.Mapping.BgTextureCatalog.FromTxt(assets.GetRaw(txtPath))
                : null;
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
        RealClientAssets assets, global::MartialHeroes.Assets.Mapping.BgTextureCatalog? pool, MapDescriptor? map,
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

        // The .map intTexId is the 0-based bgtexture pool slot, used DIRECTLY (NO -1); the only -1 is
        // the .ted byte → list step above (li = oneBasedIdx - 1).
        // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join (IDA-corrected 263bd994: 0x445833).
        string? rel = pool.ResolveRelativePath(list[li].TexId);
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