// World/EnvironmentNode.cs
//
// Passive environment node that assembles a Godot WorldEnvironment + DirectionalLight3D from the
// per-area environment BINARY family under data/sky/dat/, parsed by Assets.Parsers
// (EnvironmentBinParsers). It consumes the parsed *Bin models — it owns NO parsing, NO game-rule
// logic, and NO domain state: it only translates the decoded environment into Godot visuals.
//
// Source files (loaded via Adapters/VfsEnvironmentSource, which mirrors VfsTerrainSectorSource):
//   data/sky/dat/map_option{id}.bin (40 B)   — flags: lensflare/stardome/clouddome/sun/moon enables, indoor.
//   data/sky/dat/fog{id}.bin        (204 B)  — start/end ratios + 48 BGRA fog colours.
//   data/sky/dat/light{id}.bin      (5312 B) — 48 directional + 48 ambient keyframes + fallback dir.
//   data/sky/dat/material{id}.bin   (9792 B) — sun/sky material table (sky-tint, optional).
//
// Day/night cycle (spec: environment.md §2):
//   48 keyframes, 1800 ms each, period 86 400 ms. Keyframe 0 = midnight, 24 = noon.
//   This node runs a SLOW looping cycle seeded at NOON (kf 24, the brightest frame — §6.3), so
//   the town reads in daylight immediately and then drifts through the day. CycleSpeed scales how
//   many simulated milliseconds advance per real second; default 30× gives a ~48-min visible day.
//   Set CycleEnabled=false to freeze at the seeded keyframe.
//
// Coordinate / value conversions (each cites its spec):
//   - Fog start/end ratios are FRACTIONS of the camera view range; the far plane is 15000
//     (camera_movement.md §A.7), so world units = ratio × 15000. spec: environment.md §6.1.
//   - Fog/material colours are BGRA u8 → Godot Color(r/255,g/255,b/255,1). spec: environment.md §6.2.
//   - light{id}.bin §A color_A (RGBA f32) → DirectionalLight3D colour; §B color_A → ambient.
//     spec: environment.md §6.1 node-mapping table.
//   - Directional light DIRECTION: no per-keyframe direction exists in the binary (§8.4); use the
//     fallback vector at light{id}.bin 0x14B0 (-7,7,20), normalised, converted via WorldCoordinates.
//     spec: environment_bins.md §9.4 + environment.md §6.1.
//
// Threading: all Godot node mutation happens on the Godot main thread (Configure is called from
// RealWorldRenderer.Initialise on the main thread; the cycle runs in _Process).
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/specs/environment.md — runtime environment model (the master reference).
// spec: Docs/RE/formats/environment_bins.md — file byte layouts (consumed via the parsers).

using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Composition;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Passive rendering node that reads the per-area environment bins and configures the scene's
///     <see cref="WorldEnvironment" /> and <see cref="DirectionalLight3D" />.
///     Call <see cref="Configure" /> once per area load (after the area id is resolved). The slow
///     day/night cycle then drives time-of-day each frame in <see cref="_Process" />.
/// </summary>
public sealed partial class EnvironmentNode : Node3D
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Camera far-clip distance, used to scale fog start/end ratios (fractions of view range)
    ///     into world units.
    ///     spec: Docs/RE/specs/camera_movement.md §A.7 — "far 15000.0": CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/environment.md §6.1 — fog start/end = ratio × view_range.
    /// </summary>
    private const float ViewRange = 15000f;

    /// <summary>
    ///     Day/night keyframe count.
    ///     spec: Docs/RE/specs/environment.md §2.1 — SKY_KEYFRAME_COUNT = 48: CONFIRMED.
    /// </summary>
    private const int KeyframeCount = LightBin.KeyframeCount; // 48

    /// <summary>
    ///     Milliseconds per keyframe step.
    ///     spec: Docs/RE/specs/environment.md §2.1 — SKY_KEYFRAME_MS = 1800: CONFIRMED.
    /// </summary>
    private const double KeyframeMs = 1800.0;

    /// <summary>
    ///     Total simulated day length (48 × 1800 ms).
    ///     spec: Docs/RE/specs/environment.md §2.1 — SKY_PERIOD_MS = 86 400: CONFIRMED.
    /// </summary>
    private const double PeriodMs = KeyframeCount * KeyframeMs; // 86 400

    /// <summary>
    ///     Noon keyframe — the brightest frame; used as the cycle seed.
    ///     spec: Docs/RE/specs/environment.md §2.4 — keyframe 24 = noon (12:00): CONFIRMED.
    ///     spec: Docs/RE/specs/environment.md §6.3 — "noon start gives the brightest initial lighting".
    /// </summary>
    private const int NoonKeyframe = 24;

    private int _appliedKeyframe = -1;
    private int _areaId;

    private double _clockMs = NoonKeyframe * KeyframeMs; // seed at noon.
    private DirectionalLight3D? _dirLight;

    // -------------------------------------------------------------------------
    // Parsed environment state
    // -------------------------------------------------------------------------

    private AreaEnvironment? _env;

    // The single Godot Environment resource this node owns and mutates IN PLACE every frame.
    // Created once when the WorldEnvironment is resolved; never re-allocated per _Process tick
    // (the day/night cycle only changes colours/energies on this same instance — zero per-frame alloc).
    private Environment? _environment;
    private bool _hasSunDir;

    // FIX 10 (IDA sub_44966F @0x44966F): the sun ORBIT is the SOLE per-frame owner of the directional
    // light direction (SkyDomeNode.UpdateBillboards → negated orbit pos → light.Basis). When a sky
    // dome exists it drives the direction every frame; this node must NOT also write the Basis per
    // frame. The fallback set-once write (no dome / no light.bin) is applied at most once, guarded here.
    private bool _fallbackDirApplied;

    // Sky dome rendering node (star + cloud domes). Null when VFS absent or domes not enabled.
    // Owned by this node; created and parented in Configure.
    // spec: Docs/RE/specs/environment.md §6 — Godot reconstruction guidance for sky domes.
    private SkyDomeNode? _skyDome;

    // Cached fallback directional light direction in Godot space (computed once in Configure).
    private Vector3 _sunDirGodot = Vector3.Zero;

    private WorldEnvironment? _worldEnv;

    // -------------------------------------------------------------------------
    // Tunable rendering / cycle parameters
    // -------------------------------------------------------------------------

    /// <summary>
    ///     When true, the day/night clock advances each frame; when false, the environment is frozen
    ///     at the seeded keyframe (noon by default).
    /// </summary>
    public bool CycleEnabled { get; set; } = true;

    /// <summary>
    ///     Simulated milliseconds advanced per real second. 30 000 ms/s → the 86 400 ms day plays in
    ///     ~2.88 real minutes; slow enough to read as a gentle drift, fast enough to be observable.
    /// </summary>
    public float CycleSpeed { get; set; } = 30_000f;

    // TonemapExposure removed: the original DX8 client has no tonemap/exposure pass.
    // spec: Docs/RE/specs/rendering.md §6 — post chain is bright-copy → blur → composite → present; NO tonemap.
    // spec: Docs/RE/specs/environment.md §6.2a — colours applied RAW, no gamma.

    // MinSunEnergy removed: the original applies color_A RAW at energy 1.0.
    // spec: Docs/RE/specs/environment.md §6.2a — "Directional light applied RAW without any multiplier."

    /// <summary>
    ///     Player brightness floor, matching OPTION_BRIGHT / 100.
    ///     spec: Docs/RE/specs/environment.md §6.2a — OPTION_BRIGHT default = 100 → floor = 1.0 (full).
    ///     The per-keyframe section-B ambient is inert in the original (K_ambient = 0.0, zero writers).
    ///     This is the ONLY live ambient the original device receives at default settings.
    ///     spec: Docs/RE/specs/environment.md §6.2b — root-cause fix for the "too-dark" EnvironmentNode.
    ///     DEBT #3 STATUS (un-darken atmosphere):
    ///     The ambient floor is set to 1.0 (full white) here, which matches OPTION_BRIGHT=100 (the
    ///     confirmed binary default). ApplyAmbient uses this as AmbientLightEnergy (never the
    ///     per-keyframe §B table, which is gated by K_ambient=0 and contributes nothing in the
    ///     original). This is the spec-dictated root-cause fix for the "too-dark" world.
    ///     spec: Docs/RE/specs/environment.md §6.2a — K_ambient=0 CONFIRMED; §6.2b — floor=1.0 default.
    ///     ORACLE-PENDING: the exact on-screen brightness is a windowed-screenshot call that
    ///     Tier-1 / render-reviewer must verify against the official captures. If the scene reads
    ///     too bright, lower DoOption.ini OPTION_BRIGHT and read it at runtime (see §6.2b residual).
    ///     The math here is spec-faithful; the aesthetic verdict is oracle-pending.
    ///     Declared aesthetic for the Godot-side mapping (energy=1.0 vs D3D device_ambient=(255,255,255)):
    ///     the principle is spec-dictated; the exact knob mapping is an engineering approximation.
    /// </summary>
    public float OptionBrightFloor { get; set; } =
        1.0f; // spec: Docs/RE/specs/environment.md §6.2a — default OPTION_BRIGHT=100 → 1.0

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Loads and applies the environment for <paramref name="areaId" />.
    ///     Must be called on the Godot main thread after the area is resolved.
    ///     <paramref name="sceneWorldEnv" /> / <paramref name="sceneDirLight" /> are the World scene's
    ///     own <see cref="WorldEnvironment" /> + <see cref="DirectionalLight3D" />, passed explicitly by
    ///     <see cref="RealWorldRenderer" /> (which owns the World scene root). Passing them avoids the
    ///     scene-tree-walk ambiguity under <c>boot_flow=login</c> (where the tree is
    ///     <c>/root → Boot → World → RealWorldRenderer → EnvironmentNode</c> and a parent walk that
    ///     stops at the top-most-under-/root node lands on <c>Boot</c>, whose direct children do NOT
    ///     include the World scene's env/light → duplicates were created). When both are null this
    ///     node falls back to a RECURSIVE scene search, then to creating its own.
    ///     Wiring (RealWorldRenderer.WireEnvironmentAndWater):
    ///     <code>
    ///   var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
    ///   AddChild(envNode);
    ///   envNode.Configure(_assets, TargetAreaId, sceneWorldEnv, sceneDirLight);
    /// </code>
    ///     spec: Docs/RE/specs/environment.md §3.1 — per-area load sequence.
    /// </summary>
    public void Configure(
        RealClientAssets? assets,
        int areaId,
        WorldEnvironment? sceneWorldEnv = null,
        DirectionalLight3D? sceneDirLight = null)
    {
        _areaId = areaId;
        ResolveOrCreateSceneNodes(sceneWorldEnv, sceneDirLight);

        // Apply static post/tonemap settings ONCE at configure time — they never change per-keyframe.
        // Removed from ApplyKeyframe (which runs every frame) to eliminate redundant native property
        // writes on a path that changes nothing after the first call. spec: PERF-M3.
        // spec: Docs/RE/specs/rendering.md §6 — no tonemap/exposure pass in original post chain.
        // spec: Docs/RE/specs/environment.md §6.2a — colours applied RAW, no gamma.
        ApplyStaticPostSettings();

        if (assets is null)
        {
            // No VFS data — _environment is now a freshly-allocated empty Environment that replaced
            // the .tscn sub-resource. Configure it with a sensible fallback so the scene is visible:
            //   white ambient at full energy (matches the OPTION_BRIGHT=100 default floor),
            //   neutral mid-grey sky (matching the .tscn background_color fallback),
            //   linear tonemap (no original tonemap pass — spec: rendering.md §6),
            //   directional light left as-is (already configured in World.tscn).
            // Aesthetic: these values are engineering choices for a legible fallback, not spec-dictated.
            if (_environment is not null)
            {
                // Tonemap/SSAO/SSIL/SDFGI already set by ApplyStaticPostSettings() above.
                _environment.BackgroundMode = Environment.BGMode.Color;
                _environment.BackgroundColor = new Color(0.45f, 0.55f, 0.70f); // neutral sky — aesthetic
                _environment.AmbientLightSource = Environment.AmbientSource.Color;
                _environment.AmbientLightColor = Colors.White;
                _environment.AmbientLightEnergy = 1.0f; // OPTION_BRIGHT=100 floor — spec: environment.md §6.2a
                _environment.GlowEnabled = false;
                _environment.FogEnabled = false;
            }

            GD.Print(
                "[Environment] No VFS — applied visible fallback environment (white ambient 1.0, neutral sky, linear tonemap).");
            return;
        }

        // Load + parse the area's environment bins through the VFS adapter.
        // spec: Docs/RE/specs/environment.md §3.1 — load sequence.
        _env = VfsEnvironmentSource.Load(assets, areaId);

        // Resolve the directional-light direction from the light fallback vector (no per-keyframe
        // direction exists — §8.4). Default to the spec's hard-coded fallback when light is absent.
        ResolveSunDirection();

        // Build sky domes (star + cloud) when their flags allow.
        // spec: Docs/RE/specs/environment.md §3.1 steps 4–5 — gated by stardome_enable / clouddome_enable.
        // Domes are suppressed for indoor areas (spec §5.1) and when the parsed flags are absent.
        BuildSkyDomes(assets);

        // Seed at noon and apply once immediately so the first rendered frame is daylight.
        // The original always runs the day/night cycle — there is no dev freeze toggle.
        _clockMs = NoonKeyframe * KeyframeMs;
        _appliedKeyframe = -1;

        ApplyKeyframe(NoonKeyframe, 0f);

        PrintSummary(NoonKeyframe);
    }

    /// <summary>
    ///     Sets the visual time of day to a fixed keyframe and (by default) freezes the cycle there.
    ///     keyframeIndex 0 = midnight, 24 = noon, 47 = late night. No-op if Configure has not run.
    ///     spec: Docs/RE/specs/environment.md §2.4 — keyframe-to-time mapping.
    /// </summary>
    public void SetTimeOfDay(int keyframeIndex, bool freeze = true)
    {
        keyframeIndex = Math.Clamp(keyframeIndex, 0, KeyframeCount - 1);
        if (freeze) CycleEnabled = false;
        _clockMs = keyframeIndex * KeyframeMs;
        _appliedKeyframe = -1;
        ApplyKeyframe(keyframeIndex, 0f);
        GD.Print($"[Environment] SetTimeOfDay applied keyframe={keyframeIndex} freeze={freeze}");
    }

    // -------------------------------------------------------------------------
    // Per-frame day/night cycle
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_environment is null || !CycleEnabled) return;

        // Advance the simulated clock and wrap to the day period.
        // spec: Docs/RE/specs/environment.md §2.2 — t_wrapped = t_ms mod SKY_PERIOD_MS.
        _clockMs += delta * CycleSpeed;
        if (_clockMs >= PeriodMs) _clockMs %= PeriodMs;

        // spec: Docs/RE/specs/environment.md §2.2 — kf_index / frac derivation.
        var kf = (int)(_clockMs / KeyframeMs) % KeyframeCount;
        var frac = (float)(_clockMs % KeyframeMs / KeyframeMs);

        ApplyKeyframe(kf, frac);

        // Update sky domes (star/cloud tint, visibility, UV scroll) from the same clock.
        // spec: Docs/RE/specs/environment.md §3.2 step 4 — compute star_kf_index and star_frac.
        _skyDome?.UpdateDomes(_clockMs, delta);
    }
}