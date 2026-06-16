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
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Dev;
using MartialHeroes.Client.Godot.Helpers;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive rendering node that reads the per-area environment bins and configures the scene's
/// <see cref="WorldEnvironment"/> and <see cref="DirectionalLight3D"/>.
///
/// Call <see cref="Configure"/> once per area load (after the area id is resolved). The slow
/// day/night cycle then drives time-of-day each frame in <see cref="_Process"/>.
/// </summary>
public sealed partial class EnvironmentNode : Node3D
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Camera far-clip distance, used to scale fog start/end ratios (fractions of view range)
    /// into world units.
    /// spec: Docs/RE/specs/camera_movement.md §A.7 — "far 15000.0": CODE-CONFIRMED.
    /// spec: Docs/RE/specs/environment.md §6.1 — fog start/end = ratio × view_range.
    /// </summary>
    private const float ViewRange = 15000f;

    /// <summary>
    /// Day/night keyframe count.
    /// spec: Docs/RE/specs/environment.md §2.1 — SKY_KEYFRAME_COUNT = 48: CONFIRMED.
    /// </summary>
    private const int KeyframeCount = LightBin.KeyframeCount; // 48

    /// <summary>
    /// Milliseconds per keyframe step.
    /// spec: Docs/RE/specs/environment.md §2.1 — SKY_KEYFRAME_MS = 1800: CONFIRMED.
    /// </summary>
    private const double KeyframeMs = 1800.0;

    /// <summary>
    /// Total simulated day length (48 × 1800 ms).
    /// spec: Docs/RE/specs/environment.md §2.1 — SKY_PERIOD_MS = 86 400: CONFIRMED.
    /// </summary>
    private const double PeriodMs = KeyframeCount * KeyframeMs; // 86 400

    /// <summary>
    /// Noon keyframe — the brightest frame; used as the cycle seed.
    /// spec: Docs/RE/specs/environment.md §2.4 — keyframe 24 = noon (12:00): CONFIRMED.
    /// spec: Docs/RE/specs/environment.md §6.3 — "noon start gives the brightest initial lighting".
    /// </summary>
    private const int NoonKeyframe = 24;

    // -------------------------------------------------------------------------
    // Tunable rendering / cycle parameters
    // -------------------------------------------------------------------------

    /// <summary>
    /// When true, the day/night clock advances each frame; when false, the environment is frozen
    /// at the seeded keyframe (noon by default).
    /// </summary>
    public bool CycleEnabled { get; set; } = true;

    /// <summary>
    /// Simulated milliseconds advanced per real second. 30 000 ms/s → the 86 400 ms day plays in
    /// ~2.88 real minutes; slow enough to read as a gentle drift, fast enough to be observable.
    /// </summary>
    public float CycleSpeed { get; set; } = 30_000f;

    /// <summary>
    /// Tonemap exposure for the assembled environment. 1.1 lifts the (deliberately muted) legacy
    /// daylight values into a readable range without blowing out. Engineering choice, not legacy data.
    /// </summary>
    public float TonemapExposure { get; set; } = 1.15f;

    /// <summary>
    /// Floor applied to directional light energy so a muted legacy noon colour still lights the town.
    /// The legacy noon directional colour_A is ~0.40 RGB (probe-confirmed area 2); without a floor
    /// the sun would be very dim. Engineering choice; the colour HUE still comes from the data.
    /// </summary>
    public float MinSunEnergy { get; set; } = 1.6f;

    /// <summary>
    /// Player brightness floor, matching OPTION_BRIGHT / 100.
    /// spec: Docs/RE/specs/environment.md §6.2a — OPTION_BRIGHT default = 100 → floor = 1.0 (full).
    /// The per-keyframe section-B ambient is inert in the original (K_ambient = 0.0, zero writers).
    /// This is the ONLY live ambient the original device receives at default settings.
    /// spec: Docs/RE/specs/environment.md §6.2b — root-cause fix for the "too-dark" EnvironmentNode.
    /// </summary>
    public float OptionBrightFloor { get; set; } =
        1.0f; // spec: Docs/RE/specs/environment.md §6.2a — default OPTION_BRIGHT=100 → 1.0

    // -------------------------------------------------------------------------
    // Parsed environment state
    // -------------------------------------------------------------------------

    private AreaEnvironment? _env;
    private int _areaId;

    // Sky dome rendering node (star + cloud domes). Null when VFS absent or domes not enabled.
    // Owned by this node; created and parented in Configure.
    // spec: Docs/RE/specs/environment.md §6 — Godot reconstruction guidance for sky domes.
    private SkyDomeNode? _skyDome;

    // Cached fallback directional light direction in Godot space (computed once in Configure).
    private Vector3 _sunDirGodot = Vector3.Zero;
    private bool _hasSunDir;

    private double _clockMs = NoonKeyframe * KeyframeMs; // seed at noon.
    private int _appliedKeyframe = -1;

    private WorldEnvironment? _worldEnv;
    private DirectionalLight3D? _dirLight;

    // The single Godot Environment resource this node owns and mutates IN PLACE every frame.
    // Created once when the WorldEnvironment is resolved; never re-allocated per _Process tick
    // (the day/night cycle only changes colours/energies on this same instance — zero per-frame alloc).
    private global::Godot.Environment? _environment;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads and applies the environment for <paramref name="areaId"/>.
    /// Must be called on the Godot main thread after the area is resolved.
    ///
    /// <paramref name="sceneWorldEnv"/> / <paramref name="sceneDirLight"/> are the World scene's
    /// own <see cref="WorldEnvironment"/> + <see cref="DirectionalLight3D"/>, passed explicitly by
    /// <see cref="RealWorldRenderer"/> (which owns the World scene root). Passing them avoids the
    /// scene-tree-walk ambiguity under <c>boot_flow=login</c> (where the tree is
    /// <c>/root → Boot → World → RealWorldRenderer → EnvironmentNode</c> and a parent walk that
    /// stops at the top-most-under-/root node lands on <c>Boot</c>, whose direct children do NOT
    /// include the World scene's env/light → duplicates were created). When both are null this
    /// node falls back to a RECURSIVE scene search, then to creating its own.
    ///
    /// Wiring (RealWorldRenderer.WireEnvironmentAndWater):
    /// <code>
    ///   var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
    ///   AddChild(envNode);
    ///   envNode.Configure(_assets, TargetAreaId, sceneWorldEnv, sceneDirLight);
    /// </code>
    ///
    /// spec: Docs/RE/specs/environment.md §3.1 — per-area load sequence.
    /// </summary>
    public void Configure(
        RealClientAssets? assets,
        int areaId,
        WorldEnvironment? sceneWorldEnv = null,
        DirectionalLight3D? sceneDirLight = null)
    {
        _areaId = areaId;
        ResolveOrCreateSceneNodes(sceneWorldEnv, sceneDirLight);

        if (assets is null)
        {
            GD.Print("[Environment] No VFS — leaving default scene environment.");
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
        BuildSkyDomes();

        // Seed at noon and apply once immediately so the first rendered frame is daylight.
        _clockMs = NoonKeyframe * KeyframeMs;
        _appliedKeyframe = -1;
        ApplyKeyframe(NoonKeyframe, 0f);

        PrintSummary(NoonKeyframe);
    }

    /// <summary>
    /// Sets the visual time of day to a fixed keyframe and (by default) freezes the cycle there.
    /// keyframeIndex 0 = midnight, 24 = noon, 47 = late night. No-op if Configure has not run.
    /// spec: Docs/RE/specs/environment.md §2.4 — keyframe-to-time mapping.
    /// </summary>
    public void SetTimeOfDay(int keyframeIndex, bool freeze = true)
    {
        keyframeIndex = Math.Clamp(keyframeIndex, 0, KeyframeCount - 1);
        if (freeze) CycleEnabled = false;
        _clockMs = keyframeIndex * KeyframeMs;
        _appliedKeyframe = -1;
        ApplyKeyframe(keyframeIndex, 0f);
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
        int kf = (int)(_clockMs / KeyframeMs) % KeyframeCount;
        float frac = (float)((_clockMs % KeyframeMs) / KeyframeMs);

        ApplyKeyframe(kf, frac);

        // Update sky domes (star/cloud tint, visibility, UV scroll) from the same clock.
        // spec: Docs/RE/specs/environment.md §3.2 step 4 — compute star_kf_index and star_frac.
        _skyDome?.UpdateDomes(_clockMs, delta);
    }

    // -------------------------------------------------------------------------
    // Keyframe → Godot environment assembly
    // -------------------------------------------------------------------------

    private void ApplyKeyframe(int kf, float frac)
    {
        // spec: Docs/RE/specs/environment.md §2.2 — kf_next = (kf+1) mod 48.
        int kfNext = (kf + 1) % KeyframeCount;

        // Mutate the cached, owned Environment instance in place — NO per-frame allocation (Fix 2).
        global::Godot.Environment? env = _environment;
        if (env is null) return;

        // ---- Sky / background ----
        ApplyBackground(env, kf, kfNext, frac);

        // ---- Fog ----
        ApplyFog(env, kf, kfNext, frac);

        // ---- Ambient light ----
        ApplyAmbient(env, kf, kfNext, frac);

        // ---- Tonemap / post (readability) ----
        env.TonemapMode = global::Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure = TonemapExposure;
        env.SsaoEnabled = false;
        env.SsilEnabled = false;
        env.SdfgiEnabled = false;

        // ---- Glow / bloom — reproduces the legacy 3-RT chain ----
        // The legacy post chain is: scene capture → bright extract (TEX1) → glow blur at ÷2
        // downsample (TEX2, power1dx8.psh) → additive composite (finaldx8.psh) onto the backbuffer.
        // spec: Docs/RE/specs/rendering.md §6 — glow/bloom post chain (CONFIRMED load + execution).
        // spec: Docs/RE/specs/rendering.md §6.1 — TEX2 downscaled by the glow divisors (default ÷2).
        // spec: Docs/RE/formats/shaders.md §C5.1 — power1dx8.psh glow blur, finaldx8.psh composite.
        //
        // Godot WorldEnvironment Glow stands in for the bright-extract + blur + additive composite:
        //   HDRThreshold  → equivalent to the bright-pass extract cutoff.
        //   Intensity/Bloom → the accumulation weight of the blurred pass in the composite.
        //   BlendMode Additive → matches the additive fullscreen-quad present (pass 5, rendering.md §6.2).
        //   Levels           → mirror the ÷2 default downsample (levels 1–2 cover that range).
        //
        // The power1dx8.psh semantics recovered in the spec: "sample base texture, scale by constant"
        // → simple 1× downsample blur. That matches Godot's default Glow (Gaussian blur, not quartic).
        // spec: Docs/RE/formats/shaders.md — power1dx8.psh: "r0 = r0 * c0 — sample base texture, scale."
        //
        // Parameter values: aesthetic engineering choices to match the legacy bright look without
        // blowing out; none are spec-dictated numeric literals (the spec records the pass ORDER and
        // RT ROLES, not the concrete float values). Declared as aesthetic.
        ApplyGlow(env);

        // ---- Directional sun ----
        ApplyDirectional(kf, kfNext, frac);

        _appliedKeyframe = kf;
    }

    /// <summary>
    /// Background colour: noon sky-ambient tint from material{id}.bin when present, else a fog-tinted
    /// neutral sky. spec: environment.md §6.1 — Sky colour from material ambient_sky_color [29..32].
    /// </summary>
    private void ApplyBackground(global::Godot.Environment env, int kf, int kfNext, float frac)
    {
        env.BackgroundMode = global::Godot.Environment.BGMode.Color;

        MaterialBin? mat = _env?.Material;
        if (mat is not null && mat.ColorTable.Length == MaterialBin.KeyframeCount)
        {
            // ambient_sky_color RGBA at indices [29..32]. spec: environment_bins.md §3.2.
            // Material colours are float32 RGBA (may exceed 1.0 — HDR; clamp). spec: environment.md §6.2.
            Color a = MaterialSkyColor(mat.ColorTable[kf]);
            Color b = MaterialSkyColor(mat.ColorTable[kfNext]);
            env.BackgroundColor = a.Lerp(b, frac);
            return;
        }

        // No material → derive a muted sky from the fog colour (keeps the horizon coherent).
        if (_env?.Fog is { } fog)
        {
            env.BackgroundColor = LerpFogColor(fog, kf, kfNext, frac);
            return;
        }

        // spec: Docs/RE/specs/environment.md §7 — no data → neutral grey sky.
        env.BackgroundColor = new Color(0.45f, 0.55f, 0.70f, 1f);
    }

    private void ApplyFog(global::Godot.Environment env, int kf, int kfNext, float frac)
    {
        FogBin? fog = _env?.Fog;
        if (fog is null)
        {
            // spec: Docs/RE/specs/environment.md §7 — missing fog → fog disabled.
            env.FogEnabled = false;
            return;
        }

        env.FogEnabled = true;
        env.FogMode = global::Godot.Environment.FogModeEnum.Depth;

        // Static start/end from fog{id}.bin as the pre-data baseline (fractions of view range).
        // spec: environment.md §6.1 + environment_bins.md §2.1.
        env.FogDepthBegin = fog.StartDist * ViewRange;
        env.FogDepthEnd = fog.EndDist * ViewRange;
        env.FogDepthCurve = 1.0f;

        // Per-frame fog range from light{id}.bin section-C scalar s:
        //   fog_range = s * 3.0 (world units), overwrites the live fog far distance each tick.
        //   s > 0 guard: non-positive s leaves the fog struct untouched (fog stays at baseline).
        // spec: Docs/RE/specs/environment.md §6.2a — "fog mode LINEAR: far = s·3.0 … enabled when s>0
        //   (per-keyframe section-C scalar); the static fog.bin start/end seed a baseline but the
        //   per-frame s×3.0 derivation overwrites the live fog range each tick."
        // spec: Docs/RE/formats/environment_bins.md §9.3 — FogDistanceScalars @ 0x1260: CONFIRMED.
        LightBin? light = _env?.Light;
        if (light is { FogDistanceScalars.Length: >= LightBin.KeyframeCount })
        {
            // Interpolate the section-C scalar between adjacent keyframes.
            float sA = light.FogDistanceScalars[kf];
            float sB = light.FogDistanceScalars[kfNext];
            float s = sA + (sB - sA) * frac;
            if (s > 0f)
            {
                // spec: environment.md §6.2a — fog far = s * 3.0 (world units); LINEAR mode.
                env.FogDepthEnd = s * 3.0f; // spec: Docs/RE/specs/environment.md §6.2a
            }
        }

        // Fog colour interpolated between adjacent BGRA keyframes. spec: environment.md §2.3 + §6.2.
        env.FogLightColor = LerpFogColor(fog, kf, kfNext, frac);
        env.FogLightEnergy = 1.0f;
        // Let the fog tint the sky a little so the horizon and fog agree.
        env.FogSkyAffect = 0.5f;
    }

    private void ApplyAmbient(global::Godot.Environment env, int kf, int kfNext, float frac)
    {
        // spec: Docs/RE/specs/environment.md §6.2a — the per-keyframe section-B ambient is multiplied
        // by K_ambient = 0.0 (static init, zero writers anywhere in the binary — CONFIRMED). It is
        // therefore inert: ambient_kf = lerp(B[kf], B[kf_next], frac) * K_ambient = 0 every frame.
        // Do NOT drive ambient brightness from the §B keyframe table; it contributes nothing in the
        // original and reproducing it as the ambient energy is the root cause of the "too-dark" scene.
        //
        // The only live ambient the original device receives is the OPTION_BRIGHT additive floor:
        //   offset = floor(OPTION_BRIGHT / 100.0 * 255)  →  device_ambient = (offset, offset, offset)
        // At the default OPTION_BRIGHT = 100, offset = 255 → full white ambient (1.0).
        // spec: Docs/RE/specs/environment.md §6.2a — "At the default OPTION_BRIGHT=100, device ambient = full white."
        // spec: Docs/RE/specs/environment.md §6.2b — "Set ambient_light_energy = OPTION_BRIGHT/100 (default 1.0)."
        // spec: Docs/RE/specs/environment.md §6.2 — root-cause fix: default floor = 1.0, not 0.5.

        env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
        // Uniform white, matching the additive (offset, offset, offset) device ambient in the original.
        // spec: Docs/RE/specs/environment.md §6.2a — ambient base = (0,0,0); floor adds (offset,offset,offset).
        env.AmbientLightColor = Colors.White;
        // spec: Docs/RE/specs/environment.md §6.2a — OPTION_BRIGHT default = 100 → floor = 1.0 (full).
        env.AmbientLightEnergy = OptionBrightFloor; // spec: Docs/RE/specs/environment.md §6.2
    }

    // -------------------------------------------------------------------------
    // Glow / bloom — 3-RT post chain approximation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures Godot's built-in WorldEnvironment Glow to approximate the legacy 3-RT
    /// bright-extract → blur → composite chain from the original DX8 engine.
    ///
    /// CONFIRMED chain (spec: Docs/RE/specs/rendering.md §6):
    ///   scene → TEX0 (capture) → TEX1 (plain copy, NO bright threshold) →
    ///   TEX2 (single ÷2 blur via power1dx8 only; power2/power4 are ABSENT from binary) →
    ///   composite into TEX0 as:  base×(c0=0.5) + glow×(c1=0.5)  →
    ///   present: opaque copy (SRC=ONE, DEST=ZERO) to backbuffer.
    ///
    /// Godot approximation notes (Godot Glow ≠ DX8 RT chain — declared as approximation):
    ///   • NO bright threshold: spec §6.4 — "no luminance cutoff; every pixel of the scene
    ///     feeds the blur." → GlowHdrThreshold = 0 (inclusive).
    ///   • SINGLE-TAP: spec §6.4 — only power1dx8 runs; power2/power4 absent from binary.
    ///     → ONE level enabled (level 1, Godot's half-res Gaussian), all others off.
    ///     Level 1 (÷2 in Godot) corresponds to the single TEX2 ÷2 downsample.
    ///   • COMPOSITE ≈ base×0.5 + glow×0.5: spec §6 — c0=c1=0.5 default.
    ///     The DX8 composite performs the "glow add" inside the shader into TEX0, then
    ///     blits TEX0 opaquely. Godot BlendMode.Mix (Screen) mixes rather than pure-adds,
    ///     approximating the "base + glow contribution" semantics without double-adding.
    ///     GlowIntensity = 0.5 maps to the glow scalar c1 = 0.5.
    ///     Additive mode (ONE/ONE) would over-brighten vs the 0.5/0.5 opaque present.
    ///   spec: Docs/RE/specs/rendering.md §6 — bloom chain confirmed.
    ///   spec: Docs/RE/specs/rendering.md §6.4 — no bright threshold; single-tap CONFIRMED.
    ///
    /// All float values below are AESTHETIC engineering choices matching the spec-confirmed
    /// semantics (threshold=0, single level, 0.5 composite weight). No spec-dictated concrete
    /// floats exist for the Godot knobs — the spec records pass order / blend factors, not Godot params.
    /// </summary>
    private static void ApplyGlow(global::Godot.Environment env)
    {
        env.GlowEnabled = true;

        // Blend mode: Additive — matches the additive "glow add" inside the composite pass.
        // The DX8 composite (finaldx8.psh) performs saturate(2·edge·c0 + bloom·c1) which is additive
        // accumulation; the subsequent present is ONE/ZERO (opaque copy of the already-composited RT).
        // Godot's Additive blend approximates the additive accumulation step inside the composite.
        // spec: Docs/RE/specs/rendering.md §6.3 — "saturate(2·edge·c0 + bloom·c1)" composite; CONFIRMED.
        // spec: Docs/RE/specs/rendering.md §6.3 — additive accumulation inside composite PS before present.
        env.GlowBlendMode = global::Godot.Environment.GlowBlendModeEnum.Additive; // spec: rendering.md §6.3

        // HDR threshold = 0: no bright-pass cutoff — CONFIRMED (every pixel feeds the blur).
        // spec: Docs/RE/specs/rendering.md §6.4 — "no luminance cutoff; BLOOM_BRIGHT_THRESHOLD = NONE".
        env.GlowHdrThreshold = 0.0f; // spec: Docs/RE/specs/rendering.md §6.4 — no threshold CONFIRMED

        // HDR scale: how aggressively pixels above threshold feed the blur (moot with threshold=0 but
        // still scales the blur kernel energy). Aesthetic: 1.0 = neutral.
        env.GlowHdrScale = 1.0f;

        // Intensity ≈ glow-bright composite scalar c1 = 0.5.
        // spec: Docs/RE/specs/rendering.md §6 — c1 = glow-bright-multiplier × 0.5 = 0.5 (default).
        // Aesthetic mapping: Godot GlowIntensity drives the glow contribution weight in Mix mode.
        // 0.5 approximates the spec-confirmed default composite scalar c1 = 0.5.
        env.GlowIntensity = 0.5f; // spec: Docs/RE/specs/rendering.md §6 — c1 = 0.5

        // Bloom spread: tight (power1dx8 is a simple 1× sample — not a multi-tap diffusion).
        // Aesthetic: 0.0 means no extra diffuse spread beyond the blur kernel.
        env.GlowBloom = 0.0f;

        // Glow levels — SINGLE-TAP: only level 1 (Godot half-res, equivalent to ÷2 downsample).
        // spec: Docs/RE/specs/rendering.md §6.4 — "only power1dx8 runs; single blur pass; CONFIRMED."
        //   BLOOM_CHAIN = single-tap (power2/power4 absent from binary).
        //   BLOOM_DOWNSAMPLE_DIV = (2,2).
        // Level 1 in Godot's 7-level stack corresponds to the half-res (÷2) single pass.
        // All other levels disabled so the bloom does not spread beyond the single pass.
        env.SetGlowLevel(0, 0.0f); // off — finer than ÷2 (not in original chain)
        env.SetGlowLevel(1, 1.0f); // ON — half-res ÷2 single blur pass; spec §6.4 CONFIRMED
        env.SetGlowLevel(2, 0.0f); // off
        env.SetGlowLevel(3, 0.0f); // off
        env.SetGlowLevel(4, 0.0f); // off
        env.SetGlowLevel(5, 0.0f); // off
        env.SetGlowLevel(6, 0.0f); // off

        // Strength: per-level blur radius weight. Aesthetic: 1.0 full single-pass contribution.
        env.GlowStrength = 1.0f;
    }

    private void ApplyDirectional(int kf, int kfNext, float frac)
    {
        if (_dirLight is null) return;

        // SUN enable flag (map_option 0x14). When the area has the sun disabled (e.g. indoor /
        // dungeon areas, SUN = 0), suppress the directional sun so the scene reads as enclosed.
        // RECONCILED Campaign 5: SUN is its own u32 word at 0x14. spec: environment_bins.md §1.1.
        bool sunEnabled = _env?.MapOption is not { SunEnable: 0 };
        if (!sunEnabled)
        {
            _dirLight.Visible = false;
            return;
        }

        _dirLight.Visible = true;

        // Direction: fixed fallback vector (no per-keyframe direction — §8.4).
        if (_hasSunDir && _sunDirGodot.LengthSquared() > 1e-6f)
        {
            if (!_sunDirGodot.IsZeroApprox())
            {
                try
                {
                    Vector3 dir = _sunDirGodot.Normalized();
                    Vector3 up = Math.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
                    _dirLight.Basis = Basis.LookingAt(dir, up);
                }
                catch (Exception ex)
                {
                    // Degenerate direction after normalization — leave the existing basis.
                    global::Godot.GD.PrintErr($"[EnvironmentNode] degenerate sun dir: {ex.Message}");
                }
            }
        }

        LightBin? light = _env?.Light;
        if (light is not null && light.DirectionalKeyframes.Length == KeyframeCount)
        {
            // Directional colour_A (RGBA f32) from section A. spec: environment.md §6.1.
            Color a = ColorAOf(light.DirectionalKeyframes[kf]);
            Color b = ColorAOf(light.DirectionalKeyframes[kfNext]);
            Color sun = a.Lerp(b, frac);

            float lum = Luminance(sun);
            // Normalise hue, drive brightness through energy with a readability floor.
            // The legacy noon colour_A is muted (~0.40); MinSunEnergy keeps the town lit. The HUE
            // (warm at dawn/dusk, neutral at noon) still comes from the data.
            Color hue = lum > 1e-4f ? new Color(sun.R / lum, sun.G / lum, sun.B / lum, 1f) : Colors.White;
            hue = ClampColor(hue);
            _dirLight.LightColor = hue;
            _dirLight.LightEnergy = Math.Max(MinSunEnergy, lum * 4.0f);
        }
        else
        {
            // spec: Docs/RE/specs/environment.md §7 — light absent → energy 1.0, white.
            _dirLight.LightColor = Colors.White;
            _dirLight.LightEnergy = MinSunEnergy;
        }

        _dirLight.ShadowEnabled = true;
    }

    // -------------------------------------------------------------------------
    // Sun direction resolution
    // -------------------------------------------------------------------------

    private void ResolveSunDirection()
    {
        // Fallback world-space direction. spec: environment_bins.md §9.4 — (-7, 7, 20), scale 1.0.
        float dx = -7f, dy = 7f, dz = 20f;
        if (_env?.Light is { } light)
        {
            // Use the file's fallback vector (the only direction the client ever uses — §8.4).
            dx = light.FallbackDirX;
            dy = light.FallbackDirY;
            dz = light.FallbackDirZ;
            // Guard against an all-zero fallback (degenerate file) → spec default.
            if (dx == 0f && dy == 0f && dz == 0f)
            {
                dx = -7f;
                dy = 7f;
                dz = 20f;
            }
        }

        // Convert legacy left-handed world space to Godot (negate Z). spec: WorldCoordinates.ToGodot.
        (float gx, float gy, float gz) = WorldCoordinates.ToGodot(dx, dy, dz);
        _sunDirGodot = new Vector3(gx, gy, gz);
        _hasSunDir = _sunDirGodot.LengthSquared() > 1e-6f;
    }

    // -------------------------------------------------------------------------
    // Sky dome wiring
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates and builds the <see cref="SkyDomeNode"/> child from the loaded area environment.
    ///
    /// Suppressed when:
    ///   - VFS data is absent (_env is null).
    ///   - The area is indoor (indoor_flag = 1 suppresses all sky domes — spec §5.1).
    ///   - Both stardome and clouddome bins are null (files absent for this area).
    ///
    /// spec: Docs/RE/specs/environment.md §5.1 — indoor areas suppress cloud dome, star dome.
    /// spec: Docs/RE/specs/environment.md §3.1 steps 4–5 — star/clouddome gated by enable flags.
    /// spec: Docs/RE/specs/environment.md §7 — fallback: domes absent → graceful no-op.
    /// </summary>
    private void BuildSkyDomes()
    {
        if (_env is null) return;

        // Indoor areas suppress all sky subsystems including domes.
        // spec: Docs/RE/specs/environment.md §5.1 — indoor_flag = 1 suppresses domes.
        if (_env.MapOption is { IndoorFlag: 1 })
        {
            GD.Print("[SkyDome] indoor area — domes suppressed.");
            return;
        }

        // Both dome bins may be null when their VFS files are absent; that is graceful.
        // spec: Docs/RE/specs/environment.md §7 — stardome absent → white; clouddome absent → white.
        if (_env.StarDome is null && _env.CloudDome is null)
        {
            GD.Print("[SkyDome] no dome bins available — no sky domes created.");
            return;
        }

        _skyDome = new SkyDomeNode { Name = "SkyDomeNode" };
        AddChild(_skyDome);

        // Build the dome meshes from parsed data (graceful when either bin is null).
        _skyDome.Build(_env.StarDome, _env.CloudDome, _env.CloudCycle);
    }

    // -------------------------------------------------------------------------
    // Colour helpers
    // -------------------------------------------------------------------------

    /// <summary>color_A (RGBA f32) of a lighting keyframe → clamped Godot Color (alpha forced 1).</summary>
    private static Color ColorAOf(LightingKeyframe kf)
    {
        float[] c = kf.ColorA;
        // spec: environment_bins.md §9.2 — color_A RGBA, R at lowest index; alpha always 0 → use 1.
        return ClampColor(new Color(SafeF(c, 0), SafeF(c, 1), SafeF(c, 2), 1f));
    }

    /// <summary>material ambient_sky_color [29..32] (RGBA f32) → clamped Godot Color.</summary>
    private static Color MaterialSkyColor(float[] row)
    {
        // spec: environment_bins.md §3.2 — ambient_sky_color RGBA at indices [29..32].
        // spec: environment.md §6.2 — material colours are float RGBA; clamp >1 to non-HDR.
        return ClampColor(new Color(SafeF(row, 29), SafeF(row, 30), SafeF(row, 31), 1f));
    }

    /// <summary>Fog colour for fractional position between kf and kfNext (BGRA → RGB).</summary>
    private static Color LerpFogColor(FogBin fog, int kf, int kfNext, float frac)
    {
        Color a = BgraToColor(fog.FogColors[Math.Clamp(kf, 0, fog.FogColors.Length - 1)]);
        Color b = BgraToColor(fog.FogColors[Math.Clamp(kfNext, 0, fog.FogColors.Length - 1)]);
        return a.Lerp(b, frac);
    }

    /// <summary>BGRA u8 → Godot Color. spec: environment.md §6.2 — r=bgra[2], g=bgra[1], b=bgra[0].</summary>
    private static Color BgraToColor(BgraColor c)
        => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);

    private static Color ClampColor(Color c)
        => new(Math.Clamp(c.R, 0f, 1f), Math.Clamp(c.G, 0f, 1f), Math.Clamp(c.B, 0f, 1f), 1f);

    private static float Luminance(Color c)
        => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

    private static float SafeF(float[] arr, int i)
        => (uint)i < (uint)arr.Length ? arr[i] : 0f;

    // -------------------------------------------------------------------------
    // Scene node resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves exactly one <see cref="WorldEnvironment"/> and one <see cref="DirectionalLight3D"/>
    /// to drive, NEVER creating a duplicate when the scene already provides them.
    ///
    /// Resolution order (most reliable first):
    ///   1. Explicit references passed by the owner (RealWorldRenderer owns the World scene root).
    ///   2. A RECURSIVE scene search (descends past intermediate nodes — fixes the boot_flow=login
    ///      parent-walk miss where the scene env/light live two levels below the resolved root).
    ///   3. Create our own child only if neither yields one.
    ///
    /// Logs the found/created counts so headless runs can prove exactly one of each is active.
    /// </summary>
    private void ResolveOrCreateSceneNodes(WorldEnvironment? sceneWorldEnv, DirectionalLight3D? sceneDirLight)
    {
        Node sceneRoot = GetSceneRoot();

        // 1) explicit (owner-provided) → 2) recursive search → 3) create.
        bool envFromScene = false;
        bool envCreated = false;
        _worldEnv = sceneWorldEnv ?? FindDescendantOfType<WorldEnvironment>(sceneRoot);
        if (_worldEnv is not null)
        {
            envFromScene = true;
        }
        else
        {
            _worldEnv = new WorldEnvironment { Name = "EnvironmentNode_WorldEnv" };
            AddChild(_worldEnv);
            envCreated = true;
        }

        bool lightFromScene = false;
        bool lightCreated = false;
        _dirLight = sceneDirLight ?? FindDescendantOfType<DirectionalLight3D>(sceneRoot);
        if (_dirLight is not null)
        {
            lightFromScene = true;
        }
        else
        {
            _dirLight = new DirectionalLight3D { Name = "EnvironmentNode_SunLight", ShadowEnabled = true };
            AddChild(_dirLight);
            lightCreated = true;
        }

        // One-line proof of exactly-one resolution (counts the live nodes in the scene subtree so a
        // duplicate would show up immediately as >1).
        int envCount = CountDescendantsOfType<WorldEnvironment>(sceneRoot);
        int lightCount = CountDescendantsOfType<DirectionalLight3D>(sceneRoot);
        GD.Print($"[Environment] node resolution: WorldEnvironment {(envFromScene ? "reused scene" : "created own")}" +
                 $" (live in subtree={envCount}); DirectionalLight3D {(lightFromScene ? "reused scene" : "created own")}" +
                 $" (live in subtree={lightCount}). created(env={envCreated},light={lightCreated}).");

        // Create the single Environment resource we own and mutate in place. Replaces whatever the
        // scene shipped (e.g. the ProceduralSky default) with our data-driven environment, and gives
        // the per-frame cycle a stable instance to mutate (no per-tick allocation — Fix 2).
        _environment = new global::Godot.Environment();
        _worldEnv.Environment = _environment;
    }

    private Node GetSceneRoot()
    {
        Node current = this;
        while (current.GetParent() is { } parent)
        {
            current = parent;
            if (current.GetParent() == GetTree().Root) break;
        }

        return current;
    }

    /// <summary>Recursive depth-first search for the first descendant of type T (root included).</summary>
    private static T? FindDescendantOfType<T>(Node root) where T : Node
    {
        if (root is T self) return self;
        foreach (Node child in root.GetChildren())
        {
            T? found = FindDescendantOfType<T>(child);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>Counts every live descendant of type T in the subtree (root included). Diagnostic only.</summary>
    private static int CountDescendantsOfType<T>(Node root) where T : Node
    {
        int count = root is T ? 1 : 0;
        foreach (Node child in root.GetChildren())
            count += CountDescendantsOfType<T>(child);
        return count;
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    private void PrintSummary(int kf)
    {
        MapOptionBin? mo = _env?.MapOption;
        FogBin? fog = _env?.Fog;
        LightBin? light = _env?.Light;

        string fogStr = fog is not null
            ? $"start={fog.StartDist:F3}(×{ViewRange:F0}={fog.StartDist * ViewRange:F0}u) " +
              $"end={fog.EndDist:F3}(={fog.EndDist * ViewRange:F0}u) " +
              $"noonColor={BgraToColor(fog.FogColors[kf])}"
            : "none(disabled)";

        string lightStr;
        if (light is not null && light.DirectionalKeyframes.Length == KeyframeCount)
        {
            Color sun = ColorAOf(light.DirectionalKeyframes[kf]);
            // Note: AmbientKeyframes (§B) are inert in original (K_ambient=0.0 — spec §6.2a).
            // The actual device ambient = OPTION_BRIGHT/100 floor = OptionBrightFloor.
            // spec: Docs/RE/specs/environment.md §6.2a
            lightStr = $"sunColorA={sun} (energy floor {MinSunEnergy:F1}) " +
                       $"ambFloor(OPTION_BRIGHT/100)={OptionBrightFloor:F2} [§B keyframes inert, K_ambient=0] " +
                       $"fallbackDir=({light.FallbackDirX:F0},{light.FallbackDirY:F0},{light.FallbackDirZ:F0})";
        }
        else
        {
            lightStr = $"none(fallback: dir(-7,7,20), ambFloor={OptionBrightFloor:F2})";
        }

        string skyGate = mo is not null
            ? $"indoor={mo.IndoorFlag} sun={mo.SunEnable} moon={mo.MoonEnable} " +
              $"lensflare={mo.LensFlareEnable} stardome={mo.StarDomeEnable} clouddome={mo.CloudDomeEnable}"
            : "no map_option";
        string sunDir = _hasSunDir ? $"{_sunDirGodot.Normalized()}" : "default";

        GD.Print($"[Environment] area={_areaId} keyframe={kf}(noon) {skyGate} " +
                 $"material={(_env?.Material is not null)} cycle={CycleEnabled}@{CycleSpeed:F0}ms/s " +
                 $"exposure={TonemapExposure:F2} | fog: {fogStr} | light: {lightStr} | sunDirGodot={sunDir}");
    }
}