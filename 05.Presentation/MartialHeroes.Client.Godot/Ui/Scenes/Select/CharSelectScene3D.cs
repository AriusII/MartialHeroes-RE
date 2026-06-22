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
//      5 / far 15000. FREE-LOOK keyframed rig: each keyframe carries an explicit Euler (yaw, pitch)
//      per §3.5.3 — there is NO look-at point (the §3.5 HEADLINE CORRECTION). spec: §3.5 / §3.5.1 / §3.5.3.
//   4. AMBIENT EFFECT — the real char_select-u.xeff (id 380003000) at the row-centre anchor, as
//      alpha-blended camera-facing billboards (XeffSceneEffect). spec: §3.6.5 / §3.6.6.
//   5. ACTORS — up to 5 preview actors via SkinnedCharacterBuilder at the spec per-slot positions
//      (the slightly-bowed Z), at stage-origin Y=0.0 (§3.3.1 hard 0.0 immediate), PreviewScale ×6.0.
//      spec: §3.3.1 (Y=0.0 CODE-CONFIRMED) / §3.7.5.
//      Selection-facing is actor-local: selected slot snaps front (yaw 0), non-selected slots face
//      back (yaw π), and ui_left/ui_right manually yaw only the selected preview at 2 rad/s.
//      spec: Docs/RE/specs/frontend_scenes.md §3.3.2 / §3.3.4; recovered manual-yaw,
//      doida.exe SelectWindow_FaceActiveSlotFront/TickSelectedPreviewYaw.
//
// HOST API PRESERVED (read by the host CharSelectWindow — keep these signatures EXACT):
//   - public void Initialise(RealClientAssets? assets)
//   - public int TryHitTestSlot(global::Godot.Vector2 viewportLocalPos)
//   - public void SetSelectedSlot(int slotIndex)
//   - public SlotDescriptor[] SlotDescriptors { get; set; }   (widened from the old
//       (bool IsOccupied, uint SkinClassId) tuple to the §3.2 descriptor shape — class (+0x34),
//       variant (+0x2C), faceA (+0x2E), equip table (+0x58). The host CharSelectWindow
//       (PushSlotDescriptors) fills these PER-SLOT from the 3/1 roster's surfaced descriptor fields.)
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
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Application.World;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Godot.World;
using MartialHeroes.Client.Presentation.Helpers;

namespace MartialHeroes.Client.Godot.Ui.Scenes.Select;

/// <summary>
///     A <see cref="Node3D" /> that builds the full 3D character-select scene from real VFS assets:
///     the map000 backdrop cell, the standing preview row, the entry-dolly camera
///     (<see cref="CharSelectCameraRig" />), the area-0 environment, and the single ambient effect.
///     <para>
///         Construction: set <see cref="SlotDescriptors" />, then call <see cref="Initialise" /> after
///         the node is in the scene tree. Assets may be null — the scene degrades to env + camera only,
///         logging each skipped asset (no crash, no synthetic data).
///     </para>
///     spec: Docs/RE/specs/frontend_scenes.md §3.3 / §3.5 / §3.6 / §3.7.
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

    // Row platform Y. spec §3.3.1 (CODE-CONFIRMED) — "Y is exactly 0.0 for every slot; a hard 0.0
    // immediate (stage-origin Y 0.0 + slot ΔY 0.0), with no terrain sample and no per-slot ground
    // lookup on the placement path." The §3.6.5 Y≈69.89 value is the ambient-effect / look-at anchor
    // (the xeff spawn position), NOT the actor placement Y. The hit-test rig (CharSelectCameraRig.HitTest)
    // reads actor.Position.Y dynamically, so it adapts to Y=0 automatically.
    // spec: §3.3.1 CODE-CONFIRMED hard 0.0 immediate.
    private const float RowPlatformY = 0.0f; // spec: §3.3.1 CODE-CONFIRMED — stage-origin Y 0.0, no terrain sample.

    // Row pivot (LEGACY world) — the focal point of the backdrop and the camera look-at anchor.
    // spec: §3.6.5 / §3.7.2 — row pivot world (508.48, 69.89, −9758.57). CODE-CONFIRMED.
    private const float RowPivotLegacyX = 508.48f;
    private const float RowPivotLegacyY = 69.89f;
    private const float RowPivotLegacyZ = -9758.57f;

    // Preview scale — a PORT UNIT-RECONCILIATION of the BINARY-CONFIRMED lineup scale literal.
    // BINARY GROUND TRUTH: each lineup actor is spawned with scale field +1160 = 70.0 (the create/zoom
    // actor uses 81.0 in +1160 AND +1164); the "3.0" that appears in the original is the idle-motion
    // PLAYBACK-RATE multiplier (written to actor+100), NOT a scale — do NOT conflate the two.
    // spec: Docs/RE/scenes/charselect.md §6.2 (lineup scale 70.0 @+1160; create scale 81.0 @+1160/+1164;
    //       idle playback-rate 3.0 @actor+100 is a motion-rate override, distinct from scale).
    // The 70.0 (and 81.0) are LEGACY-space scale-field values, NOT ready-to-use Godot multipliers, so a
    // unit-reconcile factor maps them to Godot units. The deformed actor is only ≈11 Godot units tall;
    // at the §3.5 KF1 camera distance (≈85 units) with FOV 50° an unscaled-ish actor read too small. The
    // chosen reconcile factor maps the legacy 70.0 → Godot ×6.0 (i.e. factor ≈ 6.0/70 ≈ 0.0857), so each
    // avatar fills ~25-30% of frame height — a prominent preview, matching the captures oracle (visuals
    // are not headless-verifiable; oracle > spec for pixels — do NOT blindly change this working value).
    // This ×6.0 IS the shared lineup baseline: CharCreatePreview3D derives the create scale by applying
    // the SAME reconcile factor to the binary 81.0 → 6.0 × (81/70) ≈ 6.943, strictly larger by the
    // spec-confirmed 81/70 ratio. spec: Docs/RE/scenes/charselect.md §6.2 (lineup 70.0 / create 81.0).
    private const float PreviewScale = 6.0f; // = reconcile(70.0 lineup scale @+1160). spec: charselect.md §6.2

    // Slot-selection visual yaw — actor rotates, camera stays fixed.
    // spec: Docs/RE/specs/frontend_scenes.md §3.3.2 / §3.3.4; recovered manual-yaw,
    // doida.exe SelectWindow_FaceActiveSlotFront/TickSelectedPreviewYaw.
    private const float SelectedActorFrontYaw = 0.0f;
    private const float DeselectedActorBackYaw = Mathf.Pi;
    private const float ManualSelectedYawRadiansPerSecond = 2.0f;

    // Projection — EXACT. spec: §3.5.1 — vertical FOV 50°, near 5.0, far 15000.0. CODE-CONFIRMED.
    private const float CameraFov = 50.0f;
    private const float CameraNear = 5.0f;
    private const float CameraFar = 15000.0f;

    // =========================================================================
    // Environment (§3.6) — DATA-DRIVEN from area-0 sky .bin files, frozen at 14:30 (kf 29).
    // spec: §3.6.1 — "No hard-coded ambient colour literal exists in the scene builder — the final
    //   on-screen sun/ambient/fog colours are DATA-DRIVEN through the sky/time manager and the
    //   area-0 sky data at the frozen 14:30 clock." CODE-CONFIRMED.
    // spec: §3.6.3 — area-0 sky family: data/sky/dat/light0.bin, fog0.bin, material0.bin, etc.
    // spec: §3.6.2 — fog-blend offset zeroed → distance fog OFF. CODE-CONFIRMED.
    // =========================================================================

    // The frozen time-of-day: 52 200 s = 14:30 → 48-keyframe-per-day table → kf index 29.
    // spec: frontend_scenes.md §3.6.1 — "clock pinned to 14:30; keyframe 29 exactly".
    private const int EnvFrozenKeyframe = 29; // spec: §3.6.1 CODE-CONFIRMED (52200 s / 1800 s = 29)

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

    // =========================================================================
    // Camera keyframes & projection (§3.5). All EXACT from the spec; positions converted to
    // Godot-space (Z negated once).
    // =========================================================================

    // KF0 (entry-dolly start) = world (515.549, 137.266, −9397.710). EXACT. spec: §3.5 / §3.5.2.
    internal static readonly Vector3 DollyKF0Godot = ToGodotVec(515.549f, 137.266f, -9397.710f);

    // KF1 (entry-dolly end / resting pose) = world (512, 87, −9652). EXACT. spec: §3.5 / §3.5.2.
    internal static readonly Vector3 DollyKF1Godot = ToGodotVec(512.0f, 87.0f, -9652.0f);

    // NO look-at anchor: the camera is a FREE-LOOK keyframed rig (§3.5 HEADLINE CORRECTION) — each
    // keyframe carries an explicit Euler (yaw, pitch) per §3.5.3, NOT an aim at a world point. The
    // former DollyLookAtGodot (row-pivot look-at) is superseded and removed; CharSelectCameraRig
    // builds each endpoint orientation from the §3.5.3 angle multipliers. spec: §3.5 / §3.5.3.

    // =========================================================================
    // Ambient effect (§3.6.5) — the single code-spawned char_select-u.xeff (id 380003000).
    // =========================================================================

    // Effect anchor = the row centre, world (508.483, 69.887, −9758.569), scale 1.0, identity
    // rotation, the builder's SOLE spawn. spec: §3.6.5 CODE-CONFIRMED. Godot-space negates Z.
    private static readonly Vector3 XeffAnchorGodot = ToGodotVec(508.483f, 69.887f, -9758.569f);
    private readonly Node3D?[] _slotActors = new Node3D?[5];
    private RealClientAssets? _assets;
    private TerrainNode? _backdropTerrain;

    // =========================================================================
    // Runtime state
    // =========================================================================

    private Camera3D? _camera;

    private CharSelectCameraRig? _cameraRig;

    // Data-driven environment node — resolves area-0 sky .bin files, frozen at kf 29 (14:30).
    // spec: §3.6 / §3.6.3 (area-0 sky family). Null when VFS absent (graceful fallback in EnvironmentNode).
    private EnvironmentNode? _environmentNode;

    // Whether Initialise has run (the one-time cell/env/camera/effects build). A RefreshSlotActors
    // call before init is a NO-OP — the descriptors will be picked up at init time. The assets handed
    // to Initialise are retained so a later refresh can rebuild the actor row from the same VFS.
    private bool _initialised;
    private int _selectedSlot;

    // =========================================================================
    // Public host API — DO NOT change these signatures (the host CharSelectWindow calls them).
    // =========================================================================

    /// <summary>
    ///     Per-slot descriptor array (max 5). Set before <see cref="Initialise" /> (and re-pushed on a
    ///     later <c>ApplyCharacterList</c>). The row is descriptor-driven. WAVE 2: widened from the old
    ///     <c>(bool IsOccupied, uint SkinClassId)</c> tuple to the §3.2 descriptor shape so the
    ///     appearance/skeleton can be resolved faithfully — the §3.3.7 overlay build is driven by the raw
    ///     880-byte spawn descriptor (class +0x34, variant +0x2C, faceA +0x2E, equip table +0x58).
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.2 / §3.3.1 / §3.3.7.
    /// </summary>
    public SlotDescriptor[] SlotDescriptors { get; set; } = new SlotDescriptor[5];

    private static float[] BuildSlotGodotZ()
    {
        var z = new float[SlotLegacyZ.Length];
        for (var i = 0; i < z.Length; i++)
            z[i] = WorldCoordinates.ToGodot(0f, 0f, SlotLegacyZ[i]).Z; // (x,y,z)→(x,y,−z)
        return z;
    }

    // =========================================================================
    // Preview-character assets (§3.7.5) — appearance resolution delegated to SlotAppearanceResolver.
    // =========================================================================
    // Slot actor building is now ENTIRELY handled by SlotAppearanceResolver.BuildSlotActor, which:
    //   - keys the ONE shared deform skeleton g{SkinClassId}.bnd by SkinClassId = descriptor
    //     InternalClass ∈ {1,2,3,4} (the §8(e) verbatim rig key, same as PlayerAvatarResolver),
    //   - builds the body as overlay slot 3 (the §3.5.3 catalogue body — NO separate base mesh),
    //   - multi-surface-deforms the overlays {4,6,2,11} onto that SAME skeleton (one pass, full
    //     teardown semantics — no double-attach / no exploded mesh, §3.6.2), the weapon (slot 14)
    //     rigid hand-attached, any absent gear .skn LOGGED-and-SKIPPED (no fabrication), and
    //   - keys the idle .mot by actormotion col2 == SkinClassId -> col16 so every slot animates.
    // spec: Docs/RE/specs/skinning.md §3.5.1 / §3.5.2 / §3.6.2 / §8(e); frontend_scenes.md §3.3.7 / §3.7.5.

    /// <summary>
    ///     Builds the whole 3D scene from real VFS assets. Call AFTER the node is in the scene tree.
    ///     A null <paramref name="assets" /> degrades to env + camera only (logged; no crash).
    /// </summary>
    public void Initialise(RealClientAssets? assets)
    {
        try
        {
            // Retain the VFS handle so a later RefreshSlotActors (when the 3/1 char list arrives a
            // frame or more after the deferred Initialise) can rebuild the actor row from the SAME VFS.
            _assets = assets;

            BuildEnvironmentDataDriven(assets);
            BuildCamera();

            if (assets is not null)
            {
                // The cell / env / camera / effects build is ONE-TIME (it must NOT be duplicated on a
                // later refresh — only the character actors rebuild). spec: §3.7.1 (single backdrop cell).
                BuildBackdropTerrain(assets);
                BuildBackdropProps(assets);
                // Water plane removed — environment.md §4 RESOLVED-NEGATIVE: the original has no water
                // renderer; char-select is a stone temple. spec: environment.md §4.
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
    ///     Rebuilds the per-slot character actor ROW from the CURRENT <see cref="SlotDescriptors" />:
    ///     frees any existing slot-actor nodes, then rebuilds them via <c>TryBuildSlotActorViaResolver</c>
    ///     (which calls <see cref="SlotAppearanceResolver.BuildSlotActor" />). The cell / environment / camera / ambient
    ///     effect are NOT touched —
    ///     only the character actors. A call before <see cref="Initialise" /> is a no-op (the descriptors
    ///     are picked up at init). Called by the host's <c>ApplyCharacterList</c> after the 3/1 character
    ///     list arrives a frame or more after the deferred init. spec: §3.3.1.
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
            for (var i = 0; i < _slotActors.Length; i++)
            {
                var actor = _slotActors[i];
                if (actor is not null && IsInstanceValid(actor))
                {
                    RemoveChild(actor);
                    actor.QueueFree();
                }

                _slotActors[i] = null;
            }

            // (b) Rebuild from the CURRENT descriptors via TryBuildSlotActorViaResolver → SlotAppearanceResolver.
            if (assets is not null)
            {
                BuildCharacterRow(assets);
                ApplySlotSelectionFacing();
            }
            else
            {
                GD.Print("[CharSelectScene3D] RefreshSlotActors: no VFS — actor row cleared (no actors).");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] RefreshSlotActors failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Updates the selected slot and snaps actor-local facing: selected = front (yaw 0),
    ///     every other preview = back (yaw π). The camera is not moved.
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.3.2 / §3.3.4; recovered manual-yaw,
    ///     doida.exe SelectWindow_FaceActiveSlotFront/TickSelectedPreviewYaw.
    /// </summary>
    public void SetSelectedSlot(int slotIndex)
    {
        if ((uint)slotIndex >= 5u) return;
        _selectedSlot = slotIndex;
        ApplySlotSelectionFacing();
    }

    /// <summary>
    ///     3D world-space ray-pick: unprojects <paramref name="viewportLocalPos" /> through the scene
    ///     camera and returns the slot (0..4) whose per-slot AABB it first hits, or −1. The AABB is
    ///     X ± 6, Z ± 6, Y band [rowBaseY, rowBaseY + 22] (= spec Y 70..92). spec: §3.3.3 CODE-CONFIRMED.
    /// </summary>
    public int TryHitTestSlot(Vector2 viewportLocalPos)
    {
        return _cameraRig?.HitTest(viewportLocalPos) ?? -1;
    }

    // =========================================================================
    // Environment — DATA-DRIVEN area-0 sky .bin files, frozen at keyframe 29 (14:30).
    // spec: §3.6 — "The select scene activates the real area-0 world environment and freezes the
    //   world clock at 14:30 (time-of-day value 52200, weather sub-index 48)." CODE-CONFIRMED.
    // spec: §3.6.1 — "No hard-coded ambient colour literal; colours are DATA-DRIVEN from the area-0
    //   sky data at the frozen 14:30 clock." CODE-CONFIRMED.
    // spec: §3.6.3 — area-0 sky family: light0.bin / fog0.bin / material0.bin / clouddome0.bin /
    //   stardome0.bin / wind0.bin / cloud_cycle0.bin / map_option0.bin. CODE-CONFIRMED.
    // spec: §3.6.2 — fog-blend offset zeroed → distance fog OFF. CODE-CONFIRMED.
    // Implementation: reuses the project's EnvironmentNode (World/EnvironmentNode.cs) which already
    //   reads the per-area sky .bin family, applies the OPTION_BRIGHT=1.0 ambient floor
    //   (spec: environment.md §6.2a), and builds star/cloud domes. We freeze it at keyframe 29.
    // =========================================================================

    private void BuildEnvironmentDataDriven(RealClientAssets? assets)
    {
        // Instantiate the shared EnvironmentNode; it owns the WorldEnvironment + DirectionalLight3D.
        // Configure with area 0 (char-select reuses the real area-0 world environment) and any VFS
        // handle we have (null = graceful fallback: white ambient + neutral sky, logged).
        // spec: §3.5.1 / §3.6 / §3.6.3 — area code 0, folder "000". CODE-CONFIRMED.
        _environmentNode = new EnvironmentNode
        {
            Name = "CharSelectEnvironment",
            // Cycle disabled: clock is frozen at 14:30 (keyframe 29). spec: §3.6 — "freezes the
            // world clock at 14:30". CODE-CONFIRMED. The day/night cycle does NOT advance.
            CycleEnabled = false
        };
        AddChild(_environmentNode);

        // Configure loads the area-0 sky .bin files and seeds the day/night clock.
        // spec: §3.6.3 — reads data/sky/dat/light0.bin, fog0.bin, material0.bin, etc.
        _environmentNode.Configure(assets, BackdropAreaId);

        // Freeze at keyframe 29 = 14:30 (52200 s ÷ 1800 s/kf = 29 exactly, interpolation frac 0).
        // spec: §3.6.1 — "At the frozen char-select clock 52200 s = 14:30 the index is keyframe 29
        //   exactly (interpolation fraction 0)." CODE-CONFIRMED.
        _environmentNode.SetTimeOfDay(EnvFrozenKeyframe);

        // Fog override: the select-scene build helper explicitly zeroes the fog-blend OFFSET field,
        // turning distance fog OFF. EnvironmentNode's ApplyFog may re-enable it from the bin data;
        // we must honour the spec's CODE-CONFIRMED fog-off directive for this scene.
        // spec: §3.6.2 — "fog-blend OFFSET field … factor 0 → distance fog OFF." CODE-CONFIRMED.
        // Access the WorldEnvironment through the EnvironmentNode's child list to apply the fog-off.
        // If the WorldEnvironment cannot be found we log and continue (fog off is a fidelity note,
        // not a crash-inducing defect).
        var we = FindWorldEnvironmentChild(_environmentNode);
        if (we?.Environment is { } env)
        {
            env.FogEnabled = false; // spec: §3.6.2 — fog-blend offset 0 → distance fog OFF. CODE-CONFIRMED.
            GD.Print(
                "[CharSelectScene3D] Environment fog forced OFF per §3.6.2 (fog-blend offset zeroed). CODE-CONFIRMED.");
        }
        else
        {
            GD.Print(
                "[CharSelectScene3D] WARNING: could not locate WorldEnvironment child to apply fog-off override (§3.6.2).");
        }

        GD.Print(
            $"[CharSelectScene3D] Data-driven environment: area 0, keyframe {EnvFrozenKeyframe} (14:30 = 52200 s). " +
            "spec: §3.6 / §3.6.1 / §3.6.3 CODE-CONFIRMED. NO hardcoded ambient/fog literals.");
    }

    /// <summary>
    ///     Walks the immediate children of <paramref name="parent" /> to find the first
    ///     <see cref="WorldEnvironment" /> child (the one created by <see cref="EnvironmentNode" />).
    ///     Returns null when not found (node may not yet be in tree, or Configure not yet called).
    /// </summary>
    private static WorldEnvironment? FindWorldEnvironmentChild(Node parent)
    {
        for (var i = 0; i < parent.GetChildCount(); i++)
            if (parent.GetChild(i) is WorldEnvironment we)
                return we;
        return null;
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
            KeepAspect = Camera3D.KeepAspectEnum.Height // vertical FOV is the reference (§3.5.1)
        };
        AddChild(_camera);

        // Pose the camera at KF0; the rig sets the FREE-LOOK Euler orientation (NO look-at point —
        // §3.5 HEADLINE CORRECTION) and animates position+orientation to KF1 over 2.0 s.
        // spec: §3.5.2 — rig constructed at index 0 (dolly start). CODE-CONFIRMED.
        _camera.Position = DollyKF0Godot;

        // Build + wire the entry-dolly + manual-input + hit-test rig.
        // spec: §3.5 (entry dolly KF0→KF1, free-look Euler) / §3.5.4 (manual boom-zoom) / §3.3.3 (hit-test).
        _cameraRig = new CharSelectCameraRig { Name = "CharSelectCameraRig" };
        AddChild(_cameraRig);
        _cameraRig.Configure(
            _camera,
            SlotLegacyX, // X is convention-neutral (Godot X = legacy X)
            SlotGodotZ,
            i => (uint)i < (uint)_slotActors.Length ? _slotActors[i] : null,
            DollyKF0Godot,
            DollyKF1Godot);

        GD.Print($"[CharSelectScene3D] Camera: KF0={DollyKF0Godot} → KF1={DollyKF1Godot} " +
                 $"(FREE-LOOK Euler per §3.5.3, NO look-at); FOV {CameraFov}/near {CameraNear}/far {CameraFar}; " +
                 "2.0 s lerp/slerp then hold KF1. spec: §3.5 / §3.5.1 / §3.5.2 / §3.5.3.");
    }

    // =========================================================================
    // Backdrop terrain — the real cell d000x10000z9990.ted via TerrainNode.
    // =========================================================================

    private void BuildBackdropTerrain(RealClientAssets assets)
    {
        var tag = AreaTag(BackdropAreaId);
        var tedPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.ted";

        if (!assets.Contains(tedPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .ted absent: {tedPath} — terrain skipped.");
            return;
        }

        try
        {
            var tedData = assets.GetRaw(tedPath);
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
                TextureResolver = BuildTerrainTextureResolver(assets)
            };
            AddChild(terrainNode);
            _backdropTerrain = terrainNode;

            // Feed the single sector directly (no streaming for one backdrop cell).
            terrainNode.OnSectorLoaded(new SectorLoadedEvent(
                BackdropMapX, BackdropMapZ, tedData));

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
        var tag = AreaTag(BackdropAreaId);
        var budPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.bud";

        if (!assets.Contains(budPath))
        {
            GD.Print($"[CharSelectScene3D] Backdrop .bud absent: {budPath} — props skipped.");
            return;
        }

        try
        {
            var scene = assets.LoadBud(budPath);
            if (scene is null || scene.Objects.Length == 0)
            {
                GD.Print("[CharSelectScene3D] Backdrop .bud empty — no props.");
                return;
            }

            // Building textures resolve through the same two-hop chain via the BUILDING section.
            // spec: §3.7.3 — suksang01..04 / walll04 / walll04_2 / haha under building/.
            var bgPool = TryLoadBgPool(assets);
            var cellMap = TryLoadCellMap(assets);

            Func<uint, ImageTexture?> budTexResolver = budIdx =>
                ResolveTexture(assets, bgPool, cellMap, "BUILDING", (int)budIdx);

            var propsRoot = BudMeshBuilder.Build(scene, budTexResolver);

            // SHADING NOTE: BudMeshBuilder uses PerPixel (Shaded) shading so the .bud props receive
            // the ambient light from the WorldEnvironment (EnvironmentNode, data-driven from area-0
            // light0.bin at keyframe 29 = 14:30). Unshaded is NOT used — it would suppress the ambient
            // contribution entirely (D3D9 flat device-ambient = OPTION_BRIGHT/100 = 1.0 white, which
            // reaches all geometry surfaces; Shaded + WorldEnvironment ambient reproduces that behaviour
            // in Godot). spec: §3.6.1 + rendering.md §1 (D3D9 flat device-ambient → Godot ambient floor,
            // Shaded). BudMeshBuilder's default PerPixel shading is kept.

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
        XeffSceneEffect.LoadAndAttach(this, XeffAnchorGodot, assets);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        TickSelectedPreviewYaw((float)delta);
    }

    private void TickSelectedPreviewYaw(float dt)
    {
        var selectedActor = GetSlotActor(_selectedSlot);
        if (selectedActor is null) return;

        var yawDelta = 0.0f;
        if (global::Godot.Input.IsActionPressed("ui_left")) yawDelta -= ManualSelectedYawRadiansPerSecond * dt;
        if (global::Godot.Input.IsActionPressed("ui_right")) yawDelta += ManualSelectedYawRadiansPerSecond * dt;
        if (yawDelta == 0.0f) return;

        var rotation = selectedActor.Rotation;
        rotation.Y += yawDelta;
        selectedActor.Rotation = rotation;
    }

    private void ApplySlotSelectionFacing()
    {
        for (var i = 0; i < _slotActors.Length; i++)
        {
            var actor = GetSlotActor(i);
            if (actor is null) continue;

            var rotation = actor.Rotation;
            rotation.Y = i == _selectedSlot ? SelectedActorFrontYaw : DeselectedActorBackYaw;
            actor.Rotation = rotation;
        }
    }

    private Node3D? GetSlotActor(int slotIndex)
    {
        return (uint)slotIndex < (uint)_slotActors.Length && _slotActors[slotIndex] is { } actor &&
               IsInstanceValid(actor)
            ? actor
            : null;
    }

    // =========================================================================
    // Character row — up to 5 skinned preview actors at the spec per-slot positions.
    // =========================================================================

    private void BuildCharacterRow(RealClientAssets assets)
    {
        // Placement Y = 0.0 (spec §3.3.1 CODE-CONFIRMED hard immediate; no terrain sample, no per-slot
        // ground lookup). The terrain sampler at the pivot is run for diagnostics only. spec: §3.3.1.
        var rowY = RowPlatformY;
        if (_backdropTerrain is not null &&
            _backdropTerrain.TryGetGroundHeight(RowPivotLegacyX, RowPivotLegacyZ, out var sampledY))
            GD.Print($"[CharSelectScene3D] Terrain sampler at pivot = {sampledY:F3} (diagnostic only); " +
                     $"placing actors on stage-origin Y={rowY:F2} per §3.3.1. spec: §3.3.1 CODE-CONFIRMED.");

        for (var i = 0; i < 5; i++)
        {
            var occupied = i < SlotDescriptors.Length && SlotDescriptors[i].IsOccupied;
            if (!occupied) continue;

            try
            {
                var actor = TryBuildSlotActorViaResolver(assets, i, SlotDescriptors[i], rowY);
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

    /// <summary>
    ///     Builds one preview slot's actor via <see cref="SlotAppearanceResolver.BuildSlotActor" />,
    ///     which resolves the skeleton + idle from the descriptor-derived <c>model_class_id</c>
    ///     (the AUTHORITATIVE appearance key) — NOT the blindly-parsed <c>mesh.IdB</c>.
    ///     Placement (position/scale/facing) is applied here as a wrapper Node3D.
    ///     spec: Docs/RE/specs/skinning.md §3.5.2 / §8(e); frontend_scenes.md §3.3.7 / §3.7.5.
    /// </summary>
    private Node3D? TryBuildSlotActorViaResolver(
        RealClientAssets assets, int slotIdx, SlotDescriptor descriptor, float rowY)
    {
        // Map the host SlotDescriptor to the engine-free SlotAppearance record that
        // SlotAppearanceResolver consumes (same fields, engine-free primitives only).
        // spec: frontend_scenes.md §3.2 (descriptor shape: class +0x34, variant +0x2C, faceA +0x2E, equip +0x58).
        var appearance = new SlotAppearanceResolver.SlotAppearance(
            descriptor.InternalClass,
            descriptor.Variant,
            descriptor.FaceA,
            descriptor.Equip);

        var result = SlotAppearanceResolver.BuildSlotActor(assets, appearance, $"slot{slotIdx}");

        if (result.ActorRoot is null)
            // SlotAppearanceResolver already logged the specific reason (invisible sentinel, absent .skn, etc.).
            return null;

        // Wrap the recentred actor root in a placement Node3D (position / scale / facing).
        // The actor root is already recentred (feet at local Y=0) by SkinnedCharacterBuilder.
        var slotWrapper = new Node3D { Name = $"Slot{slotIdx}Actor" };
        slotWrapper.Position = new Vector3(SlotLegacyX[slotIdx], rowY, SlotGodotZ[slotIdx]);
        slotWrapper.Scale = Vector3.One * PreviewScale; // reconcile(70.0 lineup scale @+1160). spec: charselect.md §6.2

        // Facing is selection-driven: selected preview snaps front; every other built preview faces back.
        // spec: Docs/RE/specs/frontend_scenes.md §3.3.2 / §3.3.4; recovered manual-yaw,
        // doida.exe SelectWindow_FaceActiveSlotFront/TickSelectedPreviewYaw.
        slotWrapper.Rotation = new Vector3(
            0f,
            slotIdx == _selectedSlot ? SelectedActorFrontYaw : DeselectedActorBackYaw,
            0f);

        slotWrapper.AddChild(result.ActorRoot);
        return slotWrapper;
    }

    // ── Terrain/building texture two-hop chain (terrain.md §5.6 / §3.5 / §4.2) ───────────────────

    private Func<int, ImageTexture?> BuildTerrainTextureResolver(RealClientAssets assets)
    {
        var bgPool = TryLoadBgPool(assets);
        var cellMap = TryLoadCellMap(assets);
        var cache = new Dictionary<int, ImageTexture?>();
        return texByte =>
        {
            if (cache.TryGetValue(texByte, out var cached)) return cached;
            var tex = ResolveTexture(assets, bgPool, cellMap, "TERRAIN", texByte);
            cache[texByte] = tex;
            return tex;
        };
    }

    private static BgTextureCatalog? TryLoadBgPool(RealClientAssets assets)
    {
        try
        {
            // Runtime form first: the binary bgtexture.lst the original client consumes (the
            // Real packed data.vfs only provides bgtexture.lst; FromTxt was removed (Task A1 carry-over).
            // spec: Docs/RE/specs/asset_pipeline.md §3 chain B — runtime path is .lst only.
            const string lstPath = "data/map000/texture/bgtexture.lst";
            if (assets.Contains(lstPath))
                return BgTextureCatalog.FromLst(assets.GetRaw(lstPath));

            GD.PrintErr($"[CharSelectScene3D] bgtexture.lst absent ({lstPath}) — terrain/buildings stay untextured.");
            return null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CharSelectScene3D] bgtexture.lst load failed: {ex.Message}");
            return null;
        }
    }

    private static MapDescriptor? TryLoadCellMap(RealClientAssets assets)
    {
        try
        {
            var tag = AreaTag(BackdropAreaId);
            var mapPath = $"data/map{tag}/dat/d{tag}x{BackdropMapX}z{BackdropMapZ}.map";
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
            if (string.Equals(s.Keyword, section, StringComparison.OrdinalIgnoreCase))
            {
                list = s.Textures;
                break;
            }

        if (list is null) return null;

        var li = oneBasedIdx - 1; // 1-based index → 0-based table slot. spec: terrain.md §5.6 Block 3
        if ((uint)li >= (uint)list.Length) return null;

        // The .map intTexId is the 0-based bgtexture pool slot, used DIRECTLY (NO -1); the only -1 is
        // the .ted byte → list step above (li = oneBasedIdx - 1).
        // spec: Docs/RE/formats/bgtexture_lst.md §Cross-file join (IDA-corrected 263bd994: 0x445833).
        var rel = pool.ResolveRelativePath(list[li].TexId);
        if (rel is null) return null;

        var ddsPath = $"data/map000/texture/{rel}.dds";
        return assets.Contains(ddsPath) ? assets.LoadTexture(ddsPath) : null;
    }

    // ── Conversions ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Converts a legacy world (x,y,z) to a Godot-space <see cref="Vector3" /> (Z negated once).</summary>
    private static Vector3 ToGodotVec(float legacyX, float legacyY, float legacyZ)
    {
        var (gx, gy, gz) = WorldCoordinates.ToGodot(legacyX, legacyY, legacyZ);
        return new Vector3(gx, gy, gz);
    }

    private static string AreaTag(int areaId)
    {
        return areaId.ToString("D3");
    }

    /// <summary>
    ///     One char-select slot's appearance descriptor, as much of the §3.2 / §3.3.7 880-byte spawn
    ///     descriptor as the host can surface from the 3/1 roster. The skinning/skeleton chain needs the
    ///     INTERNAL class (descriptor +0x34, {1..4} = Musa/Salsu/Dosa/Monk) and variant (+0x2C); the
    ///     §3.3.7 per-part overlay build additionally needs faceA (+0x2E) and the equipment table (+0x58).
    ///     <para>
    ///         HOST-API GAP (reported, WAVE 2): the layer-04 <c>CharacterListSlot</c> event surface
    ///         currently carries ONLY <c>ServerClass</c> (descriptor +0x74) — it does NOT decode +0x34,
    ///         +0x2C, +0x2E, or +0x58. The host plumbs <see cref="InternalClass" /> from the best
    ///         class-like value it has and leaves <see cref="Variant" />/<see cref="FaceA" />/
    ///         <see cref="Equip" /> at their defaults until layer 04 surfaces those fields. We do NOT
    ///         fabricate the missing bytes (that would manufacture a missing fact, forbidden).
    ///     </para>
    ///     spec: Docs/RE/specs/frontend_scenes.md §3.2 (descriptor: variant +0x2C, faceA +0x2E,
    ///     class +0x34, equip +0x58) / §3.3.7; Docs/RE/specs/skinning.md §3.5.2.
    /// </summary>
    /// <param name="IsOccupied">True when the slot holds a character (a body is built).</param>
    /// <param name="InternalClass">
    ///     Internal class id (descriptor +0x34, {1,2,3,4}). Used VERBATIM as the §3.3.7 / §3.5.2 class
    ///     arg — NEVER offset to 0. Drives the starter body and, with <see cref="Variant" />, the
    ///     appearance key. spec: skinning.md §3.5.2; frontend_scenes.md §3.3.7.
    /// </param>
    /// <param name="Variant">Appearance variant (descriptor +0x2C). spec: §3.5.2 (variant arg).</param>
    /// <param name="FaceA">Face / slot-14 'd' byte (descriptor +0x2E / +0x22). spec: §3.3.7.</param>
    /// <param name="Equip">
    ///     The §3.3.7 visible-gear part gids for slots {3,4,6,2,11,14} (descriptor +0x58 table leading
    ///     dwords), or empty when the host could not surface them. spec: §3.3.7 (equip_ref_table).
    /// </param>
    public readonly record struct SlotDescriptor(
        bool IsOccupied,
        uint InternalClass,
        uint Variant = 0,
        uint FaceA = 0,
        uint[]? Equip = null);
}