// World/EnvironmentNode.Configurator.cs
//
// Partial class — WorldEnvironment / atmosphere / lighting / fog setup,
// sun direction resolution, sky dome wiring, and keyframe application.
// Colour helpers / scene node resolution / diagnostics live in EnvironmentNode.Helpers.cs.
// See EnvironmentNode.cs for the full file description and all spec cites.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: Docs/RE/specs/environment.md — runtime environment model.
// spec: Docs/RE/formats/environment_bins.md — file byte layouts.

using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;
using Environment = Godot.Environment;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EnvironmentNode
{
    // -------------------------------------------------------------------------
    // Keyframe → Godot environment assembly
    // -------------------------------------------------------------------------

    private void ApplyKeyframe(int kf, float frac)
    {
        // spec: Docs/RE/specs/environment.md §2.2 — kf_next = (kf+1) mod 48.
        var kfNext = (kf + 1) % KeyframeCount;

        // Mutate the cached, owned Environment instance in place — NO per-frame allocation (Fix 2).
        var env = _environment;
        if (env is null) return;

        // ---- Sky / background ----
        ApplyBackground(env, kf, kfNext, frac);

        // ---- Fog ----
        ApplyFog(env, kf, kfNext, frac);

        // ---- Ambient light ----
        ApplyAmbient(env, kf, kfNext, frac);

        // ---- Directional sun ----
        ApplyDirectional(kf, kfNext, frac);

        _appliedKeyframe = kf;
    }

    /// <summary>
    ///     Applies the static (never-changing) post-process and tone-mapping settings to the owned
    ///     <see cref="Environment" /> instance. Called ONCE from <see cref="Configure" />, not per-frame
    ///     (these values never change after Configure runs — pulling them out of ApplyKeyframe eliminates
    ///     redundant native property writes on the hot per-frame path, i.e. PERF-M3).
    ///     spec: Docs/RE/specs/rendering.md §6 — post chain: bright-copy → blur → composite → present; NO tonemap.
    ///     spec: Docs/RE/specs/environment.md §6.2a — colours applied RAW, no gamma.
    ///     spec: Docs/RE/specs/rendering.md §6.4 — glow wiring; SSAO/SSIL/SDFGI absent from original.
    /// </summary>
    private void ApplyStaticPostSettings()
    {
        var env = _environment;
        if (env is null) return;

        // Tonemap: Linear pass-through (no tonemap/exposure pass in the original DX8 post chain).
        // spec: Docs/RE/specs/rendering.md §6 — no tonemap stage confirmed.
        // spec: Docs/RE/specs/environment.md §6.2a — colours applied RAW, no gamma.
        env.TonemapMode = Environment.ToneMapper.Linear;
        env.TonemapExposure = 1.0f; // identity — spec: rendering.md §6 — no exposure pass.

        // Screen-space and global-illumination effects: all absent from the original DX8 engine.
        env.SsaoEnabled = false;
        env.SsilEnabled = false;
        env.SdfgiEnabled = false;

        // Glow / bloom — reproduces the legacy 3-RT chain (constant, never per-keyframe).
        // The legacy post chain is: scene capture → bright extract (TEX1) → glow blur at ÷2
        // downsample (TEX2, power1dx8.psh) → additive composite (finaldx8.psh) onto the backbuffer.
        // All glow parameters are constant (aesthetic engineering choices matching the spec-confirmed
        // pass ORDER and blend weights — none change per keyframe). Moved here from ApplyKeyframe
        // (PERF-M3) to eliminate redundant native property writes on the hot per-frame path.
        // spec: Docs/RE/specs/rendering.md §6 — glow/bloom post chain (CONFIRMED load + execution).
        // spec: Docs/RE/specs/rendering.md §6.1 — TEX2 downscaled by the glow divisors (default ÷2).
        // spec: Docs/RE/specs/rendering.md §6.4 — single-tap blur (power1dx8 only). CONFIRMED.
        // spec: Docs/RE/formats/shaders.md §C5.1 — power1dx8.psh glow blur, finaldx8.psh composite.
        ApplyGlow(env);
    }

    /// <summary>
    ///     Background colour: noon sky-ambient tint from material{id}.bin when present, else a fog-tinted
    ///     neutral sky. spec: environment.md §6.1 — Sky colour from material ambient_sky_color [29..32].
    ///     Legibility note: when the spec-dictated ambient_sky_color [29..32] is very dark (near black —
    ///     observed in area 2 keyframe 24 where those material floats are zero), the raw value yields a
    ///     black sky patch that reads as a render defect. In that case we fall through to the sky_haze
    ///     tint [0..3] which is the other primary sky descriptor in the material table, then to the fog
    ///     colour, then to the neutral fallback.
    ///     This luminance gate is a PORT-SIDE AESTHETIC DECISION (not spec-dictated). The luminance
    ///     threshold (0.025) and the fallback chain are engineering choices for a readable world view;
    ///     they are declared aesthetic. When the official captures become available, calibrate against them.
    ///     spec: environment_bins.md §3.2 — ambient_sky_color at [29..32]; sky_haze at [0..3].
    ///     spec: environment.md §7 — fallback when data absent.
    /// </summary>
    private void ApplyBackground(Environment env, int kf, int kfNext, float frac)
    {
        env.BackgroundMode = Environment.BGMode.Color;

        var mat = _env?.Material;
        if (mat is not null && mat.ColorTable.Length == MaterialBin.KeyframeCount)
        {
            // ambient_sky_color RGBA at indices [29..32]. spec: environment_bins.md §3.2.
            // Material colours are float32 RGBA (may exceed 1.0 — HDR; clamp). spec: environment.md §6.2.
            var a = MaterialSkyColor(mat.ColorTable[kf]);
            var b = MaterialSkyColor(mat.ColorTable[kfNext]);
            var skyColor = a.Lerp(b, frac);

            // Legibility gate — PORT-SIDE AESTHETIC: if the spec-dictated ambient_sky_color is near-black
            // (observed in real area-2 material bins where indices [29..32] are effectively zero), fall
            // through to the sky_haze [0..3] descriptor which tends to carry a visible tint.
            // Threshold 0.025 ≈ "below 2.5% luminance" — empirically chosen; declared aesthetic.
            // When official captures are available, remove/calibrate this gate.
            var lum = 0.2126f * skyColor.R + 0.7152f * skyColor.G + 0.0722f * skyColor.B;
            if (lum >= 0.025f)
            {
                env.BackgroundColor = skyColor;
                return;
            }

            // Fallback to sky_haze [0..3] when ambient_sky_color is too dark.
            // spec: environment_bins.md §3.2 — sky_haze RGBA at indices [0..3].
            // Aesthetic: prefer a visible tint from the same material bin over a hard-coded constant.
            var hazeA = SkyHazeColor(mat.ColorTable[kf]);
            var hazeB = SkyHazeColor(mat.ColorTable[kfNext]);
            var hazeColor = hazeA.Lerp(hazeB, frac);
            var hazeLum = 0.2126f * hazeColor.R + 0.7152f * hazeColor.G + 0.0722f * hazeColor.B;
            if (hazeLum >= 0.025f)
            {
                // Attenuate haze slightly so it reads as sky, not harsh. Aesthetic multiplier: 0.6.
                env.BackgroundColor = new Color(hazeColor.R * 0.6f, hazeColor.G * 0.6f, hazeColor.B * 0.6f);
                return;
            }

            // Both material colours are near-black → fall through.
        }

        // No material or both material colours near-black → derive a muted sky from the fog colour
        // (keeps the horizon coherent with the fog-saturated terrain).
        // Aesthetic: attenuate the fog colour slightly for the sky so it reads brighter/lighter
        // than the fog-blanketed ground plane. Declared aesthetic — not spec-dictated.
        if (_env?.Fog is { } fog)
        {
            var fogColor = LerpFogColor(fog, kf, kfNext, frac);
            var fogLum = 0.2126f * fogColor.R + 0.7152f * fogColor.G + 0.0722f * fogColor.B;
            if (fogLum >= 0.025f)
            {
                // Brighten slightly: fog-as-sky should read lighter than fog-on-terrain. Aesthetic multiplier.
                env.BackgroundColor = new Color(
                    Math.Min(fogColor.R * 1.3f, 1f),
                    Math.Min(fogColor.G * 1.3f, 1f),
                    Math.Min(fogColor.B * 1.3f, 1f));
                return;
            }
        }

        // Last resort: all data-driven colours are near-black (e.g. night-time keyframe or absent bins).
        // Use a neutral daytime blue-grey sky so the world always reads as inhabitable.
        // Aesthetic: this is a port-side choice for world legibility. Not spec-dictated.
        // The original client's sky in darkness conditions is not known without the official captures.
        // Calibrate against captures when available.
        env.BackgroundColor = new Color(0.45f, 0.55f, 0.70f); // neutral sky — aesthetic
    }

    private void ApplyFog(Environment env, int kf, int kfNext, float frac)
    {
        var fog = _env?.Fog;
        if (fog is null)
        {
            // spec: Docs/RE/specs/environment.md §7 — missing fog → fog disabled.
            env.FogEnabled = false;
            return;
        }

        // Fog is LINEAR; the only live far-plane driver is s×3.0 from the section-C scalar.
        // Gate the whole fog block on s>0: if no positive scalar is available fog stays off.
        // spec: Docs/RE/specs/environment.md §6.2a — "observed apply path is LINEAR … fog_range = s*3.0
        //   … EXP/EXP2 confirmed NOT driven … LINEAR mode + density 0.0; enabled when s>0."
        // spec: Docs/RE/specs/environment.md §6.1 — fog far = s×3.0 (node-mapping).
        // spec: Docs/RE/formats/environment_bins.md §9.3 — FogDistanceScalars.

        var light = _env?.Light;
        var fogScalar = 0f;
        if (light is { FogDistanceScalars.Length: >= LightBin.KeyframeCount })
        {
            var sA = light.FogDistanceScalars[kf];
            var sB = light.FogDistanceScalars[kfNext];
            fogScalar = sA + (sB - sA) * frac;
        }

        if (fogScalar <= 0f)
        {
            // s=0 → fog effectively off per spec.
            env.FogEnabled = false;
            return;
        }

        env.FogEnabled = true;
        // Godot uses FogModeEnum.Depth for the linear depth-based fog (begin→end).
        // spec: Docs/RE/specs/environment.md §6.2a — LINEAR fog (begin/end depth).
        env.FogMode = Environment.FogModeEnum.Depth;

        // Far = s×3.0; near scaled by 1/s.
        // spec: Docs/RE/specs/environment.md §6.2a — far = s*3.0, near = 1/s.
        env.FogDepthEnd = fogScalar * 3.0f; // spec: Docs/RE/specs/environment.md §6.2a
        env.FogDepthBegin = 1.0f / fogScalar; // spec: Docs/RE/specs/environment.md §6.2a
        env.FogDepthCurve = 1.0f;

        // Fog colour interpolated between adjacent BGRA keyframes. spec: environment.md §2.3 + §6.2.
        env.FogLightColor = LerpFogColor(fog, kf, kfNext, frac);
        env.FogLightEnergy = 1.0f;
        // No sky-affect tinting — the original has no sky haze pass.
        // spec: Docs/RE/specs/environment.md §6.2a — no FogSkyAffect in original.
        env.FogSkyAffect = 0.0f;
    }

    private void ApplyAmbient(Environment env, int kf, int kfNext, float frac)
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

        env.AmbientLightSource = Environment.AmbientSource.Color;
        // Uniform white, matching the additive (offset, offset, offset) device ambient in the original.
        // spec: Docs/RE/specs/environment.md §6.2a — ambient base = (0,0,0); floor adds (offset,offset,offset).
        env.AmbientLightColor = Colors.White;
        // spec: Docs/RE/specs/environment.md §6.2a — OPTION_BRIGHT default = 100 → floor = 1.0 (full).
        env.AmbientLightEnergy = OptionBrightFloor; // spec: Docs/RE/specs/environment.md §6.2

        // Propagate the brightness floor to CelShadeMaterialFactory so newly-built cel materials
        // start with the correct ambient_floor_energy. This does NOT retroactively update already-built
        // material instances (those were already seeded with the correct 1.0 default). This call is
        // only meaningful when OptionBrightFloor changes from its default (OPTION_BRIGHT != 100).
        // spec: Docs/RE/specs/environment.md §6.2a — device_ambient is applied to ALL surfaces,
        //   including the cel-shaded skinned actors on the offscreen post-process path.
        CelShadeMaterialFactory.AmbientFloorEnergy = OptionBrightFloor; // spec: environment.md §6.2a
    }

    // -------------------------------------------------------------------------
    // Glow / bloom — 3-RT post chain approximation
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Configures Godot's built-in WorldEnvironment Glow to approximate the legacy 3-RT
    ///     bright-extract → blur → composite chain from the original DX8 engine.
    ///     CONFIRMED chain (spec: Docs/RE/specs/rendering.md §6):
    ///     scene → TEX0 (capture) → TEX1 (plain copy, NO bright threshold) →
    ///     TEX2 (single ÷2 blur via power1dx8 only; power2/power4 are ABSENT from binary) →
    ///     composite into TEX0 as:  base×(c0=0.5) + glow×(c1=0.5)  →
    ///     present: opaque copy (SRC=ONE, DEST=ZERO) to backbuffer.
    ///     Godot approximation notes (Godot Glow ≠ DX8 RT chain — declared as approximation):
    ///     • NO bright threshold: spec §6.4 — "no luminance cutoff; every pixel of the scene
    ///     feeds the blur." → GlowHdrThreshold = 0 (inclusive).
    ///     • SINGLE-TAP: spec §6.4 — only power1dx8 runs; power2/power4 absent from binary.
    ///     → ONE level enabled (level 1, Godot's half-res Gaussian), all others off.
    ///     Level 1 (÷2 in Godot) corresponds to the single TEX2 ÷2 downsample.
    ///     • COMPOSITE ≈ base×0.5 + glow×0.5: spec §6 — c0=c1=0.5 default.
    ///     The DX8 composite performs the "glow add" inside the shader into TEX0, then
    ///     blits TEX0 opaquely. Godot BlendMode.Mix (Screen) mixes rather than pure-adds,
    ///     approximating the "base + glow contribution" semantics without double-adding.
    ///     GlowIntensity = 0.5 maps to the glow scalar c1 = 0.5.
    ///     Additive mode (ONE/ONE) would over-brighten vs the 0.5/0.5 opaque present.
    ///     spec: Docs/RE/specs/rendering.md §6 — bloom chain confirmed.
    ///     spec: Docs/RE/specs/rendering.md §6.4 — no bright threshold; single-tap CONFIRMED.
    ///     All float values below are AESTHETIC engineering choices matching the spec-confirmed
    ///     semantics (threshold=0, single level, 0.5 composite weight). No spec-dictated concrete
    ///     floats exist for the Godot knobs — the spec records pass order / blend factors, not Godot params.
    /// </summary>
    private static void ApplyGlow(Environment env)
    {
        env.GlowEnabled = true;

        // Blend mode: Screen — the composite is saturate(2·edge·c0 + bloom·c1) performed INSIDE the
        // composite shader into TEX0; the final present is ONE/ZERO (opaque blit of the already-composited RT).
        // Godot Additive would re-add the glow on top of what is already composited, doubling the effect.
        // Screen (Mix) matches the opaque-blit present semantics without double-adding.
        // spec: Docs/RE/specs/rendering.md §6.2 — present pass = ONE/ZERO opaque blit; NOT re-added.
        // spec: Docs/RE/specs/rendering.md §6.3 — composite c0=c1=0.5; opaque present; NO second add.
        // spec: Docs/RE/specs/rendering.md §8 — "use Screen/Mix; Additive over-brightens at present."
        env.GlowBlendMode = Environment.GlowBlendModeEnum.Screen; // spec: rendering.md §6.2/§8

        // HDR threshold — Godot pipeline correction (aesthetic engineering choice, declared):
        // The spec records that the DX8 bright-extract had NO luminance cutoff (spec §6.4:
        // "BLOOM_BRIGHT_THRESHOLD = NONE"). In DX8 the pre-glow frame was LDR-clamped (output per
        // stage saturates at 1.0), so "no cutoff" = every pixel up to 1.0 feeds the blur at
        // exactly its clamped value — effectively zero extra energy from the clamp ceiling.
        // In Godot's HDR pipeline the additive xeff quads (BlendMode.Add, 68 sub-effects) accumulate
        // BEYOND 1.0 before clamping, and GlowHdrThreshold=0 feeds that unbounded HDR energy into
        // the glow blur with no cutoff — causing the entire 3D SubViewport to white-out
        // (confirmed: shot_charselect.png overexposure). Setting the threshold to 1.0 reproduces
        // the DX8 LDR-clamp semantic: only pixels genuinely exceeding 1.0 (the additive saturated
        // fire/glow emitters) generate bloom, while the rest of the geometry (which is LDR in the
        // original DX8 frame) does not receive extra glow. The bloom is still present; it is just
        // gated at the LDR boundary rather than at zero.
        // AESTHETIC ENGINEERING CHOICE: threshold=1.0 is the Godot-side approximation of DX8's
        // LDR-clamp semantic. The spec records the DX8 PASS ORDER and blend factors; it does not
        // specify a Godot knob value. Declared aesthetic; calibrate against oracle captures.
        // spec: Docs/RE/specs/rendering.md §6.4 — no luminance cutoff in DX8 (LDR context).
        // spec: Docs/RE/specs/rendering.md §6 — DX8 pipeline is LDR-clamped per-stage.
        env.GlowHdrThreshold = 1.0f; // aesthetic: Godot-HDR LDR-clamp approximation (see above)

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
        var sunEnabled = _env?.MapOption is not { SunEnable: 0 };
        if (!sunEnabled)
        {
            _dirLight.Visible = false;
            return;
        }

        _dirLight.Visible = true;

        // Direction: fixed fallback vector (no per-keyframe direction — §8.4).
        if (_hasSunDir && _sunDirGodot.LengthSquared() > 1e-6f)
            if (!_sunDirGodot.IsZeroApprox())
                try
                {
                    var dir = _sunDirGodot.Normalized();
                    var up = Math.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
                    _dirLight.Basis = Basis.LookingAt(dir, up);
                }
                catch (Exception ex)
                {
                    // Degenerate direction after normalization — leave the existing basis.
                    GD.PrintErr($"[EnvironmentNode] degenerate sun dir: {ex.Message}");
                }

        var light = _env?.Light;
        if (light is not null && light.DirectionalKeyframes.Length == KeyframeCount)
        {
            // Directional colour_A (RGBA f32) from section A, applied RAW.
            // spec: Docs/RE/specs/environment.md §6.1 — node mapping: color_A → light_color directly.
            // spec: Docs/RE/specs/environment.md §6.2a — "applied RAW without any multiplier. No /255, no gamma."
            var a = ColorAOf(light.DirectionalKeyframes[kf]);
            var b = ColorAOf(light.DirectionalKeyframes[kfNext]);
            var sun = a.Lerp(b, frac);
            // Apply RAW: no hue-normalise, no lum×4, no energy floor.
            _dirLight.LightColor = sun;
            _dirLight.LightEnergy = 1.0f; // spec: Docs/RE/specs/environment.md §6.2a — energy 1.0, no multiplier.
        }
        else
        {
            // spec: Docs/RE/specs/environment.md §7 — light absent → energy 1.0, white.
            _dirLight.LightColor = Colors.White;
            _dirLight.LightEnergy = 1.0f;
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
        var (gx, gy, gz) = WorldCoordinates.ToGodot(dx, dy, dz);
        _sunDirGodot = new Vector3(gx, gy, gz);
        _hasSunDir = _sunDirGodot.LengthSquared() > 1e-6f;
    }

    // -------------------------------------------------------------------------
    // Sky dome wiring
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Creates and builds the <see cref="SkyDomeNode" /> child from the loaded area environment,
    ///     then wires the sun and moon billboard textures from the VFS.
    ///     Suppressed when:
    ///     - VFS data is absent (_env is null).
    ///     - The area is indoor (indoor_flag = 1 suppresses all sky domes — spec §5.1).
    ///     - Both stardome and clouddome bins are null (files absent for this area).
    ///     spec: Docs/RE/specs/environment.md §5.1 — indoor areas suppress cloud dome, star dome.
    ///     spec: Docs/RE/specs/environment.md §3.1 steps 4–5 — star/clouddome gated by enable flags.
    ///     spec: Docs/RE/specs/environment.md §7 — fallback: domes absent → graceful no-op.
    ///     spec: Docs/RE/formats/sky.md §D.5 — sun billboard texture: data/sky/texture/sun.dds.
    ///     spec: Docs/RE/formats/sky.md §D.3 — moon phase: floor((day_counter mod 30) / 2) → moon{i}.dds.
    /// </summary>
    private void BuildSkyDomes(RealClientAssets? assets)
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
        // Pass _dirLight so the sun billboard orbit drives the scene directional light each frame.
        // spec: Docs/RE/formats/sky.md §D.2.1 — sun position negated → directional light direction: HIGH.
        _skyDome.Build(_env.StarDome, _env.CloudDome, _env.CloudCycle, _dirLight);

        // Wire sun and moon billboard textures from the VFS.
        // spec: Docs/RE/formats/sky.md §D.5 — sun billboard texture: data/sky/texture/sun.dds: HIGH.
        // spec: Docs/RE/formats/sky.md §D.3 — moon phase: floor((day_counter mod 30) / 2) → moon{i}.dds.
        // VFS-safe: if assets is null or the file is absent, SetBillboardTexture receives null and
        // gracefully keeps the placeholder warm-white / blue-white colour already set by Build().
        // spec: Docs/RE/specs/environment.md §7 — VFS absent → graceful no-op (placeholder colour retained).
        if (assets is not null)
        {
            // Sun texture: data/sky/texture/sun.dds.
            // spec: Docs/RE/formats/sky.md §D.5 — path confirmed HIGH confidence.
            const string SunDdsPath = "data/sky/texture/sun.dds"; // spec: Docs/RE/formats/sky.md §D.5
            var sunTex = assets.Contains(SunDdsPath) ? assets.LoadTexture(SunDdsPath) : null;
            if (sunTex is null)
                GD.Print("[SkyDome] sun.dds absent from VFS — placeholder colour retained. " +
                         "spec: Docs/RE/formats/sky.md §D.5");

            // Moon phase texture: data/sky/texture/moon{n}.dds.
            // spec: Docs/RE/formats/sky.md §D.3 — phase index n = floor((day_counter mod 30) / 2).
            // ORACLE-PENDING: day_counter source not reachable from EnvironmentNode; using phase 0
            // (full moon / moon0.dds) as the safe default until the day-counter channel is wired.
            // spec: Docs/RE/formats/sky.md §D.3 — phase selection: ORACLE-PENDING (day counter unavailable).
            const int MoonPhase = 0; // ORACLE-PENDING: sky.md §D.3 — defaults to phase 0 (moon0.dds)
            var moonDdsPath = $"data/sky/texture/moon{MoonPhase}.dds"; // spec: Docs/RE/formats/sky.md §D.3
            var moonTex = assets.Contains(moonDdsPath) ? assets.LoadTexture(moonDdsPath) : null;
            if (moonTex is null)
                GD.Print($"[SkyDome] {moonDdsPath} absent from VFS — placeholder colour retained. " +
                         "spec: Docs/RE/formats/sky.md §D.3");

            _skyDome.SetBillboardTextures(sunTex, moonTex);
        }
        else
        {
            GD.Print("[SkyDome] assets null — billboard textures not loaded; placeholder colours retained. " +
                     "spec: Docs/RE/formats/sky.md §D.5/§D.3");
        }
    }
}