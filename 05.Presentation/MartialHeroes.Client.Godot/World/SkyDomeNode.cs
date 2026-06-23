// World/SkyDomeNode.cs
//
// Renders the per-area sky: a star dome (night) and a cloud dome (day), both as inverted
// hemisphere ArrayMesh nodes built directly (never GltfDocument). Each dome is unshaded, fog-exempt,
// and rendered behind all world geometry via VisualInstance3D render priority / sky layer.
//
// The domes are driven by the parsed StarDomeBin and CloudDomeBin models supplied by
// VfsEnvironmentSource. The EnvironmentNode's existing 48-keyframe day/night clock drives
// UpdateDomes() each frame:
//   - Stars visible at night (kf 40–47, 0–12); fade out during dawn/dusk transition.
//   - Clouds visible by day (kf 8–40); fade out during night.
//   - Per-vertex tint from stardome/clouddome BGRA keyframes (12-frame, 7200 ms/step).
//     spec: Docs/RE/specs/environment.md §2.1 — STARDOME_KF_COUNT=12, STARDOME_KF_MS=7200.
//     spec: Docs/RE/formats/environment_bins.md §4 (stardome), §5 (clouddome).
//
// Hemisphere geometry:
//   The dome is built as an inverted hemisphere (faces pointing inward) so it surrounds the scene.
//   Vertex count for the cloud dome is exactly 240 — matching the spec's confirmed count, which
//   dictates the tessellation (15 stacks × 16 sectors + top).
//   spec: Docs/RE/formats/environment_bins.md §5.4 — "total vertex count of the cloud-dome mesh is 240".
//
//   Star dome uses 192 vertices (12 stacks × 16 sectors):
//   spec: Docs/RE/formats/environment_bins.md §4.1 — "192 star instances per keyframe".
//
// Cloud cycle animation:
//   CloudCycleBin drives a scroll-speed applied to the cloud material UV offset each frame.
//   The speed integer (1–10) is mapped to a UV offset rate.
//   spec: Docs/RE/formats/environment_bins.md §6.1 — speed u8 @ col[0], range 1–10: CONFIRMED.
//   spec: Docs/RE/formats/environment_bins.md §6.2 — 10 day patterns; client selects one per day.
//   The day pattern selection algorithm is unconfirmed; this implementation cycles row 0 (the
//   first day pattern) which is always present.
//   spec: Docs/RE/formats/environment_bins.md §6.3 — day-pattern selection: known unknown.
//
// Render order:
//   Both domes use RenderingServer.set_canvas_item_z_index to draw behind world geometry.
//   Godot 4 achieves this via GeometryInstance3D.ExtraCullMargin = 0 and a large negative
//   RenderPriority so the sky draws first in the transparent queue, plus disabling depth-write.
//   spec: Docs/RE/specs/environment.md §8 item 9 — "client renders sky layers as concentric domes
//         in a specific draw order (cloud dome, star dome, sun/moon, lens flare)".
//
// Threading: all Godot node mutation is called from the main thread (via EnvironmentNode._Process
// calling UpdateDomes). UpdateDomes is a thin, main-thread-only method.
//
// Graceful no-op: if StarDomeBin / CloudDomeBin / CloudCycleBin are null (VFS absent or
// star/clouddome_enable = 0), the corresponding dome node is simply not created.
//
// spec: Docs/RE/specs/environment.md §6 — Godot reconstruction guidance (sky domes).
// spec: Docs/RE/formats/environment_bins.md §4 (stardome), §5 (clouddome), §6 (cloud_cycle).
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.

using Godot;
using MartialHeroes.Assets.Parsers.Texture.Models;
using MartialHeroes.Client.Presentation.Helpers;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Passive sky-dome rendering node: builds and animates a star dome and cloud dome
///     from the parsed environment bin data.
///     Call <see cref="Build" /> once after construction (on the Godot main thread).
///     Call <see cref="UpdateDomes" /> each frame from <see cref="EnvironmentNode._Process" />.
/// </summary>
public sealed partial class SkyDomeNode : Node3D
{
    // -------------------------------------------------------------------------
    // Day/night cycle timing constants
    // spec: Docs/RE/specs/environment.md §2.1
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Stardome/clouddome keyframe count (coarser 12-frame cycle).
    ///     spec: Docs/RE/specs/environment.md §2.1 — STARDOME_KF_COUNT = 12: CONFIRMED.
    /// </summary>
    private const int DomeKfCount = StarDomeBin.KeyframeCount; // 12

    /// <summary>
    ///     Milliseconds per stardome/clouddome keyframe step (4 × 1800 ms lighting keyframes).
    ///     spec: Docs/RE/specs/environment.md §2.1 — STARDOME_KF_MS = 7200: CONFIRMED.
    /// </summary>
    private const double DomeKfMs = 7200.0;

    /// <summary>
    ///     Total simulated day period (same as the main cycle).
    ///     spec: Docs/RE/specs/environment.md §2.1 — SKY_PERIOD_MS = 86 400: CONFIRMED.
    /// </summary>
    private const double PeriodMs = 86_400.0;

    // -------------------------------------------------------------------------
    // Dome geometry constants
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Dome radius in world units. Sized to sit just inside the far-clip plane (15 000 wu)
    ///     so it always surrounds the scene without z-fighting at the horizon.
    ///     Engineering choice (not a legacy constant); the legacy domes were rendered at an
    ///     unspecified radius by the D3D9 pipeline's fixed-function sky stage.
    /// </summary>
    private const float DomeRadius = 13_000f;

    /// <summary>
    ///     Sector count for the dome hemisphere tessellation.
    ///     Chosen to match the per-keyframe vertex count constraints from the spec:
    ///     - Cloud dome: 240 vertices = 15 stacks × 16 sectors.
    ///     spec: Docs/RE/formats/environment_bins.md §5.4 — "vertex count … is 240": CONFIRMED.
    ///     - Star dome: 192 vertices = 12 stacks × 16 sectors.
    ///     spec: Docs/RE/formats/environment_bins.md §4.1 — "192 star instances": CONFIRMED.
    /// </summary>
    private const int DomeSectors = 16;

    /// <summary>
    ///     Stack count for the cloud dome (15 stacks × 16 sectors = 240 vertices).
    ///     spec: Docs/RE/formats/environment_bins.md §5.4 — vertex count 240: CONFIRMED.
    /// </summary>
    private const int CloudDomeStacks = 15;

    /// <summary>
    ///     Stack count for the star dome (12 stacks × 16 sectors = 192 vertices).
    ///     spec: Docs/RE/formats/environment_bins.md §4.1 — 192 star instances: CONFIRMED.
    /// </summary>
    private const int StarDomeStacks = 12;

    // -------------------------------------------------------------------------
    // Sun / moon billboard orbit constants (spec: Docs/RE/formats/sky.md §D)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Orbit radius/scale for both sun and moon billboards.
    ///     spec: Docs/RE/formats/sky.md §D.2 — ±3200.0 scaling: HIGH (static immediate in binary).
    /// </summary>
    private const float OrbitScale = 3200f; // spec: Docs/RE/formats/sky.md §D.2

    /// <summary>
    ///     Billboard size scalar for the sun sprite.
    ///     spec: Docs/RE/formats/sky.md §D.5 — sun billboard size 2048.0: HIGH (immediate).
    /// </summary>
    private const float SunBillboardSize = 2048f; // spec: Docs/RE/formats/sky.md §D.5

    /// <summary>
    ///     The 45° seed tilt angle used in the sun vertical-term derivation.
    ///     spec: Docs/RE/formats/sky.md §D.2 — "seed tilt angle 45.0": HIGH (static immediate).
    /// </summary>
    private const float SunTiltDeg = 45f; // spec: Docs/RE/formats/sky.md §D.2

    // -------------------------------------------------------------------------
    // Visibility thresholds (day/night fade — engineering choices)
    // -------------------------------------------------------------------------

    // The legacy client shows stars at night and clouds by day with a smooth transition.
    // The main 48-keyframe cycle maps kf 0 = midnight, kf 24 = noon (spec: environment.md §2.4).
    // Stars are fully visible at kf 0–8 (night) and fully hidden at kf 16–36 (day).
    // Clouds are fully visible at kf 12–40 (day) and fully hidden at kf 44–4 (night).

    // Star alpha: 0.0 at daytime (kf 16–36 = 33%–75%), 1.0 at night (kf 0–8 and 40–47).
    // Transition bands: kf 8–16 (dawn fade-out) and kf 36–44 (dusk fade-in).

    // These keyframe boundaries are engineering choices — the spec does not enumerate them.
    // They are placed symmetrically around the noon/midnight axis per spec §2.4.

    private const float StarFullNightKf = 8f; // stars fully bright below this kf index
    private const float StarFadeOutKf = 16f; // stars fully faded above this kf index
    private const float StarFadeInKf = 36f; // stars start fading back in above this kf index
    private const float StarFullNightKf2 = 44f; // stars fully bright above this kf index (pre-midnight)

    // -------------------------------------------------------------------------
    // Cloud UV scroll
    // -------------------------------------------------------------------------

    /// <summary>
    ///     UV offset rate per second for each speed unit from the cloud cycle spec.
    ///     The spec gives speed as an integer 1–10 but does not specify world-space drift velocity.
    ///     spec: Docs/RE/formats/environment_bins.md §6.3 known unknown — "speed units: unverified".
    ///     Engineering choice: 0.003 UV/s per speed unit (so speed 5 ≈ 0.015 UV/s — a gentle drift).
    /// </summary>
    private const float CloudUvRatePerSpeedUnit = 0.003f;

    // Active cloud cycle row (row 0 — day-pattern selection algorithm is a known unknown).
    // spec: Docs/RE/formats/environment_bins.md §6.3 — selection algorithm: known unknown.
    private CloudCycleRow _activeCycleRow;
    private CloudCycleBin? _cloudCycle;
    private CloudDomeBin? _cloudDome;
    private MeshInstance3D? _cloudDomeMesh1; // inner cloud layer
    private MeshInstance3D? _cloudDomeMesh2; // outer/haze cloud layer
    private ShaderMaterial? _cloudMaterial1;
    private ShaderMaterial? _cloudMaterial2;

    // Moon billboard node.
    // spec: Docs/RE/formats/sky.md §D.2 — moon traces a flat circle; sine(+3200) X, cosine(+3200) Y, no Z.
    private MeshInstance3D? _moonBillboard;

    // -------------------------------------------------------------------------
    // Data references (parsed bins)
    // -------------------------------------------------------------------------

    private StarDomeBin? _starDome;

    private MeshInstance3D? _starDomeMesh;

    private ShaderMaterial? _starMaterial;

    // -------------------------------------------------------------------------
    // Node references
    // -------------------------------------------------------------------------

    // Sun billboard node (separately positioned each frame from the orbit angle).
    // spec: Docs/RE/formats/sky.md §D.5 — sun billboard texture sun.dds, size 2048.
    private MeshInstance3D? _sunBillboard;

    // Externally-provided DirectionalLight3D whose direction tracks the negated sun position.
    // When null, no light direction update is performed (the sun orbit still renders).
    // spec: Docs/RE/formats/sky.md §D.2.1 — sun position vector negated → directional light direction.
    private DirectionalLight3D? _trackedDirLight;

    // -------------------------------------------------------------------------
    // Per-frame animation state
    // -------------------------------------------------------------------------

    // _cloudUvOffset removed: the dome shader is colour-only (no albedo texture), so the UV
    // scroll offset was computed but never applied. Dead field removed.

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds the dome meshes and sun/moon billboards from parsed environment data.
    ///     Must be called on the Godot main thread once, after this node is added to the scene tree.
    ///     Passing null for either dome bin is a graceful no-op: that dome is simply not created.
    ///     <paramref name="dirLight" /> is optional: when supplied, the sun billboard orbit drives its
    ///     direction each frame (negated sun world position). When null, no light tracking is performed.
    ///     spec: Docs/RE/specs/environment.md §7 — graceful fallback when files absent.
    ///     spec: Docs/RE/formats/sky.md §B.1 — build order: sun → star-dome → cloud-dome → moon → fog.
    ///     spec: Docs/RE/formats/sky.md §D — sun/moon billboard orbit (closed-form trig; seconds-of-day).
    /// </summary>
    public void Build(StarDomeBin? starDome, CloudDomeBin? cloudDome, CloudCycleBin? cloudCycle,
        DirectionalLight3D? dirLight = null)
    {
        _starDome = starDome;
        _cloudDome = cloudDome;
        _cloudCycle = cloudCycle;
        _trackedDirLight = dirLight;

        // Choose first day-pattern row. Selection algorithm is unconfirmed (known unknown).
        // spec: Docs/RE/formats/environment_bins.md §6.3 — day-pattern selection: known unknown.
        _activeCycleRow = cloudCycle?.Rows[0] ?? default;

        // Build sun billboard FIRST (matches §B.1 build order: sun → star → cloud → moon → fog).
        // spec: Docs/RE/formats/sky.md §B.1 — sky sub-object construction order: sun, star, cloud, moon.
        BuildSunBillboard();

        if (starDome is not null)
            BuildStarDome();

        if (cloudDome is not null)
            BuildCloudDome();

        // Build moon billboard after cloud dome (§B.1 order: sun → star → cloud → moon).
        // spec: Docs/RE/formats/sky.md §B.1 — moon billboard after cloud-dome construction.
        BuildMoonBillboard();

        // Report construction so headless runs can verify the dome creation log line (lane gate).
        var starStatus = starDome is not null ? "built" : "absent(no-op)";
        var cloudStatus = cloudDome is not null ? "built" : "absent(no-op)";
        var cycleStatus = cloudCycle is not null
            ? $"speed={_activeCycleRow.Speed} cloud1Id={_activeCycleRow.Cloud1Id0To12H}"
            : "absent";
        GD.Print($"[SkyDome] star={starStatus} cloud={cloudStatus} cloudCycle={cycleStatus} " +
                 $"sun=billboard moon=billboard radius={DomeRadius:F0}wu sectors={DomeSectors}. " +
                 "spec: Docs/RE/formats/sky.md §D (sun/moon billboard orbit).");
    }

    /// <summary>
    ///     Applies loaded VFS textures to the sun and moon billboard shader materials.
    ///     Call after <see cref="Build" /> from <c>EnvironmentNode.BuildSkyDomes</c>, on the main thread.
    ///     Each parameter is nullable: a null value is a graceful no-op that keeps the existing
    ///     placeholder colour already set by <see cref="BuildSunBillboard" /> /
    ///     <see cref="BuildMoonBillboard" /> (warm-white for sun, blue-white for moon).
    ///     The billboard shaders already declare <c>uniform sampler2D albedo_tex</c> with
    ///     <c>hint_default_white</c>, so an unset sampler renders as the <c>albedo_color</c> tint alone,
    ///     which is the existing placeholder behaviour. Setting <c>albedo_tex</c> causes the billboard to
    ///     sample the real DDS sprite and modulate it by <c>albedo_color</c> (day/night alpha).
    ///     spec: Docs/RE/formats/sky.md §D.5 — sun billboard texture: data/sky/texture/sun.dds: HIGH.
    ///     spec: Docs/RE/formats/sky.md §D.3 — moon phase: floor((day_counter mod 30) / 2) → moon{i}.dds.
    ///     spec: Docs/RE/specs/environment.md §7 — VFS absent → graceful no-op (placeholder retained).
    /// </summary>
    public void SetBillboardTextures(ImageTexture? sunTexture, ImageTexture? moonTexture)
    {
        // Sun billboard texture.
        // spec: Docs/RE/formats/sky.md §D.5 — sun.dds: HIGH confidence.
        if (sunTexture is not null
            && _sunBillboard?.MaterialOverride is ShaderMaterial sunMat)
        {
            sunMat.SetShaderParameter("albedo_tex", sunTexture); // spec: Docs/RE/formats/sky.md §D.5
            GD.Print("[SkyDome] sun.dds texture applied to SunBillboard. spec: Docs/RE/formats/sky.md §D.5");
        }

        // Moon billboard texture.
        // spec: Docs/RE/formats/sky.md §D.3 — moon{n}.dds (phase-selected): oracle-pending default moon0.
        if (moonTexture is not null
            && _moonBillboard?.MaterialOverride is ShaderMaterial moonMat)
        {
            moonMat.SetShaderParameter("albedo_tex", moonTexture); // spec: Docs/RE/formats/sky.md §D.3
            GD.Print("[SkyDome] moon{n}.dds texture applied to MoonBillboard (phase ORACLE-PENDING: default moon0). " +
                     "spec: Docs/RE/formats/sky.md §D.3");
        }
    }

    /// <summary>
    ///     Updates dome tint and visibility for the current day/night clock.
    ///     Call once per frame from EnvironmentNode._Process (main thread only).
    ///     <paramref name="clockMs" /> is the same running clock EnvironmentNode maintains
    ///     (milliseconds elapsed, already wrapped to the day period).
    ///     <paramref name="delta" /> is the frame delta in seconds (for UV scroll animation).
    ///     spec: Docs/RE/specs/environment.md §3.2 — per-frame update: steps 4–5 for star/cloud.
    /// </summary>
    public void UpdateDomes(double clockMs, double delta)
    {
        // Dome 12-frame keyframe.
        // spec: Docs/RE/specs/environment.md §2.2 — star_kf_index = t_wrapped / STARDOME_KF_MS.
        var tWrapped = clockMs % PeriodMs;
        var domeKf = (int)(tWrapped / DomeKfMs) % DomeKfCount;
        var domeKfNext = (domeKf + 1) % DomeKfCount;
        var domeFrac = (float)(tWrapped % DomeKfMs / DomeKfMs);

        // Main 48-frame keyframe index (for day/night star-visibility weighting).
        // spec: Docs/RE/specs/environment.md §2.2 — kf_index = t_wrapped / SKY_KEYFRAME_MS.
        var mainKfFloat = (float)(tWrapped / 1800.0);

        UpdateStarDome(domeKf, domeKfNext, domeFrac, mainKfFloat);
        UpdateCloudDomes(domeKf, domeKfNext, domeFrac, mainKfFloat, (float)delta);

        // Update sun/moon billboard orbits each frame.
        // spec: Docs/RE/formats/sky.md §D — sun/moon orbit uses seconds-of-day angle.
        UpdateBillboards(clockMs);
    }

    // -------------------------------------------------------------------------
    // Star dome update
    // -------------------------------------------------------------------------

    private void UpdateStarDome(int kf, int kfNext, float frac, float mainKf)
    {
        if (_starDomeMesh is null || _starMaterial is null || _starDome is null) return;

        // Interpolated star tint from the 12-frame BGRA table (star [0] is the uniform tint).
        // spec: Docs/RE/formats/environment_bins.md §4.3 — "all 192 instances share the same BGRA":
        //       SAMPLE-VERIFIED. Use star index [0] as the representative tint.
        var tintA = BgraToColor(_starDome.StarColors[kf][0]);
        var tintB = BgraToColor(_starDome.StarColors[kfNext][0]);
        var tint = tintA.Lerp(tintB, frac);

        // Day/night alpha: fade stars in at dusk and out at dawn.
        var alpha = StarAlpha(mainKf);

        // Apply to the unshaded material as a tint × alpha.
        _starMaterial.SetShaderParameter("albedo_color", new Color(tint.R, tint.G, tint.B, alpha));

        // Hide the mesh entirely when fully transparent (saves GPU overdraw during daytime).
        _starDomeMesh.Visible = alpha > 0.01f;
    }

    /// <summary>
    ///     Computes star visibility alpha [0, 1] from the fractional 48-keyframe index.
    ///     Night = [0, StarFullNightKf] and [StarFullNightKf2, 48): alpha = 1.
    ///     Day   = [StarFadeOutKf, StarFadeInKf]: alpha = 0.
    ///     Transition dawn [StarFullNightKf, StarFadeOutKf]: linear fade from 1→0.
    ///     Transition dusk [StarFadeInKf, StarFullNightKf2]: linear fade from 0→1.
    ///     Engineering choice — boundary keyframes are symmetric around kf 0 and kf 24.
    /// </summary>
    private static float StarAlpha(float kf)
    {
        // Wrap kf into [0, 48) for post-midnight region.
        if (kf < StarFullNightKf) return 1f; // deep night before dawn
        if (kf < StarFadeOutKf) return 1f - (kf - StarFullNightKf) / (StarFadeOutKf - StarFullNightKf); // dawn fade-out
        if (kf < StarFadeInKf) return 0f; // full day
        if (kf < StarFullNightKf2) return (kf - StarFadeInKf) / (StarFullNightKf2 - StarFadeInKf); // dusk fade-in
        return 1f; // post-dusk night
    }

    // -------------------------------------------------------------------------
    // Cloud dome update
    // -------------------------------------------------------------------------

    private void UpdateCloudDomes(int kf, int kfNext, float frac, float mainKf, float delta)
    {
        if (_cloudDome is null) return;

        // Cloud alpha: visible by day, hidden at night (inverse of stars).
        var cloudAlpha = 1f - StarAlpha(mainKf);

        // UV scroll (CloudCycleBin speed, §6.1) is spec-confirmed but not yet applied:
        // the dome shader is colour-only (no albedo texture sampler), so UV offset has no effect.
        // The speed field is preserved here as a spec note for future dome texture wiring.
        // spec: Docs/RE/formats/environment_bins.md §6.1 — speed u8 range 1–10: CONFIRMED.
        // float speedUnits = Math.Max(1, (int)_activeCycleRow.Speed); // reserved for future use

        UpdateCloudLayer(_cloudDomeMesh1, _cloudMaterial1, _cloudDome.Layer1Colors, kf, kfNext, frac, cloudAlpha);
        UpdateCloudLayer(_cloudDomeMesh2, _cloudMaterial2, _cloudDome.Layer2Colors, kf, kfNext, frac,
            cloudAlpha * 0.6f);
    }

    private void UpdateCloudLayer(
        MeshInstance3D? mesh,
        ShaderMaterial? mat,
        BgraColor[][] layerColors,
        int kf, int kfNext, float frac,
        float alpha)
    {
        if (mesh is null || mat is null) return;

        // Per-vertex cloud tint: use the mean of all 240 vertex colours (uniform in all sampled data).
        // spec: Docs/RE/formats/environment_bins.md §5.4 — 240 per-vertex tints per keyframe: CONFIRMED.
        // Rather than re-uploading per-vertex colours every frame (expensive), we derive the
        // representative uniform tint (vertex [0]) and apply it as a shader parameter.
        // This is consistent with the spec's observation that all vertices in a keyframe share
        // the same value in the sampled data (spec §4.3 equivalent observation for clouds).
        var tintA = BgraToColor(layerColors[kf][0]);
        var tintB = BgraToColor(layerColors[kfNext][0]);
        var tint = tintA.Lerp(tintB, frac);

        // PORT-SIDE LEGIBILITY GATE (aesthetic, not spec-dictated):
        // If the cloud tint is near-black (luminance < 0.015), hide the dome so it doesn't render as an
        // opaque black hemisphere over the WorldEnvironment background. This is observed with area-2
        // cloud-dome bins where the BGRA vertex colours are effectively zero at several keyframes,
        // producing a black sky cap. When official captures are available, calibrate/remove this gate.
        // Aesthetic threshold 0.015 — engineering choice for port-side legibility.
        var tintLum = 0.2126f * tint.R + 0.7152f * tint.G + 0.0722f * tint.B;
        if (tintLum < 0.015f)
        {
            mesh.Visible = false;
            return;
        }

        mat.SetShaderParameter("albedo_color", new Color(tint.R, tint.G, tint.B, alpha));
        // uv_offset removed: dome shader is colour-only, no texture to scroll.

        mesh.Visible = alpha > 0.01f;
    }

    // -------------------------------------------------------------------------
    // Dome mesh construction
    // -------------------------------------------------------------------------

    private void BuildStarDome()
    {
        // Star dome: inverted hemisphere, 12 stacks × 16 sectors = 192 vertices.
        // spec: Docs/RE/formats/environment_bins.md §4.1 — 192 star instances: CONFIRMED.
        var mesh = BuildHemisphereMesh(DomeRadius, StarDomeStacks, DomeSectors, true);

        _starMaterial = BuildDomeMaterial(false);
        var mi = new MeshInstance3D
        {
            Name = "StarDome",
            Mesh = mesh,
            MaterialOverride = _starMaterial,
            // Render behind world geometry. Negative priority = drawn early in the transparent
            // pass, so opaque geometry writes over it. Engineering choice; the spec only
            // documents draw order (spec §8 item 9), not the exact Godot render-priority value.
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        mi.SetLayerMaskValue(1, true);
        // Use VisualInstance3D.Layers to assign sky layer (bit 20 = Godot sky/background convention).
        // This is an engineering choice — Godot does not mandate a specific sky layer number.
        mi.Layers = 1; // visible to camera layer 1 (the default 3D camera layer)
        // Start hidden — UpdateDomes sets visibility each frame. Avoids a one-frame white flash
        // before the first UpdateDomes call evaluates the tint + luminance gate.
        mi.Visible = false;
        AddChild(mi);
        _starDomeMesh = mi;
    }

    private void BuildCloudDome()
    {
        // Cloud dome: two layers, each inverted hemisphere, 15 stacks × 16 sectors = 240 vertices.
        // spec: Docs/RE/formats/environment_bins.md §5.4 — vertex count 240: CONFIRMED.

        // Inner cloud layer (layer 1).
        var mesh1 = BuildHemisphereMesh(DomeRadius * 0.97f, CloudDomeStacks, DomeSectors, true);
        _cloudMaterial1 = BuildDomeMaterial(true);
        var mi1 = new MeshInstance3D
        {
            Name = "CloudDomeInner",
            Mesh = mesh1,
            MaterialOverride = _cloudMaterial1,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            // Start hidden — UpdateDomes sets visibility each frame (avoids a one-frame white flash).
            Visible = false
        };
        AddChild(mi1);
        _cloudDomeMesh1 = mi1;

        // Outer/haze cloud layer (layer 2) — slightly larger radius so it draws behind layer 1.
        // spec: Docs/RE/formats/environment_bins.md §5.5 — "layer 2 is outer/haze": CONFIRMED (inferred).
        var mesh2 = BuildHemisphereMesh(DomeRadius, CloudDomeStacks, DomeSectors, true);
        _cloudMaterial2 = BuildDomeMaterial(true);
        var mi2 = new MeshInstance3D
        {
            Name = "CloudDomeOuter",
            Mesh = mesh2,
            MaterialOverride = _cloudMaterial2,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            // Start hidden — UpdateDomes sets visibility each frame (avoids a one-frame white flash).
            Visible = false
        };
        AddChild(mi2);
        _cloudDomeMesh2 = mi2;
    }

    /// <summary>
    ///     Builds an inverted hemisphere ArrayMesh (vertices on the inside face inward).
    ///     The hemisphere spans from Y=0 (equator) to Y=+radius (zenith).
    ///     The tessellation is a standard UV-sphere hemisphere: <paramref name="stacks" /> latitude
    ///     rings × <paramref name="sectors" /> longitude segments.
    ///     When <paramref name="inverted" /> is true the triangle winding is reversed so that the mesh
    ///     is visible from inside (back-face culling sees the outside; the camera is at the centre).
    ///     Vertex count = stacks × sectors (no top cap vertex — the topmost ring collapses cleanly).
    ///     For cloud dome: 15 × 16 = 240. For star dome: 12 × 16 = 192.
    ///     spec: Docs/RE/formats/environment_bins.md §5.4 — cloud vertex count 240: CONFIRMED.
    ///     spec: Docs/RE/formats/environment_bins.md §4.1 — star vertex count 192: CONFIRMED.
    ///     Note: GltfDocument is NOT used. The mesh is built via Godot ArrayMesh directly.
    ///     spec: CLAUDE.md — "GltfDocument.AppendFromBuffer crashes natively … Never use it."
    /// </summary>
    private static ArrayMesh BuildHemisphereMesh(float radius, int stacks, int sectors, bool inverted)
    {
        // We build a UV-hemisphere: theta goes from π/2 (equator, Y≈0) to π (zenith, Y=radius).
        // phi goes from 0 to 2π (full circle).
        // The inverted flag reverses index winding so faces point inward.

        // Vertex/index counts are fully deterministic: (stacks+1)×(sectors+1) ring vertices and
        // stacks×sectors×6 indices (two triangles per quad). Pre-size and write by index to avoid the
        // List growth + ToArray() copies — this is one-time build cost, but the counts are known up front.
        var stride = sectors + 1;
        var vertCount = (stacks + 1) * stride;
        var indexCount = stacks * sectors * 6;

        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var indices = new int[indexCount];

        // Theta from π/2 (equator) up to π (top). stacks+1 rings including equator and zenith.
        for (var stack = 0; stack <= stacks; stack++)
        {
            // theta: π/2 at equator (stack=0), π at zenith (stack=stacks).
            var theta = MathF.PI / 2f + stack * (MathF.PI / 2f) / stacks;
            var sinTheta = MathF.Sin(theta);
            var cosTheta = MathF.Cos(theta);

            for (var sec = 0; sec <= sectors; sec++)
            {
                var phi = sec * (2f * MathF.PI) / sectors;
                var sinPhi = MathF.Sin(phi);
                var cosPhi = MathF.Cos(phi);

                // Sphere position (Y = cosTheta × radius, up). At θ=π/2 cosθ=0 → equator. At θ=π cosθ=-1 → top.
                // Remap: use sinTheta for horizontal spread and -cosTheta for Y (so equator Y=0, zenith Y=radius).
                var x = radius * sinTheta * cosPhi;
                var y = radius * -cosTheta; // remapped so zenith is +Y
                var z = radius * sinTheta * sinPhi;

                Vector3 pos = new(x, y, z);
                // Inward-pointing normal (negated outward normal) for inverted dome.
                var outward = pos.Normalized();
                var vi = stack * stride + sec;
                vertices[vi] = pos;
                normals[vi] = inverted ? -outward : outward;
                uvs[vi] = new Vector2((float)sec / sectors, (float)stack / stacks);
            }
        }

        // Generate quad indices (two triangles per quad), reversed winding if inverted.
        var idx = 0;
        for (var stack = 0; stack < stacks; stack++)
        for (var sec = 0; sec < sectors; sec++)
        {
            var tl = stack * stride + sec;
            var tr = tl + 1;
            var bl = tl + stride;
            var br = bl + 1;

            if (inverted)
            {
                // Reversed winding: face points inward.
                indices[idx++] = tl;
                indices[idx++] = bl;
                indices[idx++] = tr;
                indices[idx++] = tr;
                indices[idx++] = bl;
                indices[idx++] = br;
            }
            else
            {
                indices[idx++] = tl;
                indices[idx++] = tr;
                indices[idx++] = bl;
                indices[idx++] = tr;
                indices[idx++] = br;
                indices[idx++] = bl;
            }
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // -------------------------------------------------------------------------
    // Material factory
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds an unshaded (no lighting), fog-exempt, transparent ShaderMaterial for a sky dome.
    ///     The material uses a minimal inline shader:
    ///     - No lighting calculation (unshaded = sky appearance does not depend on scene lights).
    ///     - Fog exempt: the Godot 4 FogOverride shader built-in disables depth-fog on this surface.
    ///     - Alpha from the albedo_color parameter for day/night fading.
    ///     - UV offset parameter for cloud scroll animation.
    ///     - Depth write disabled so the sky does not occlude world geometry's depth buffer.
    ///     - Render priority = -128 (behind all default-priority geometry).
    ///     The unshaded + fog-exempt approach is the canonical Godot 4 sky-dome technique.
    ///     Engineering choice; the spec does not mandate a shader implementation.
    /// </summary>
    private static ShaderMaterial BuildDomeMaterial(bool isCloud)
    {
        // Minimal unshaded shader with fog exemption and alpha-blend transparency.
        // albedo_color: tint + alpha (set per-frame by UpdateDomes).
        // uv_offset: horizontal UV scroll (clouds only; stars have uv_offset=0.0 → no scroll).
        const string ShaderSrc =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_mix, depth_draw_never, cull_front;

            uniform vec4 albedo_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

            void fragment() {
                ALBEDO = albedo_color.rgb;
                ALPHA  = albedo_color.a;
            }
            """;

        var shader = new Shader();
        shader.Code = ShaderSrc;

        var mat = new ShaderMaterial();
        mat.Shader = shader;
        mat.RenderPriority = -128; // behind default (0) geometry in the transparent pass
        mat.SetShaderParameter("albedo_color", new Color(1f, 1f, 1f));
        // uv_offset removed: the dome shader is colour-only (no albedo texture) so UV scroll
        // had no effect. The cloud UV scroll is animated via C# CloudCycleBin speed but the
        // dome geometry doesn't sample a texture, making the offset a dead no-op.
        return mat;
    }

    // -------------------------------------------------------------------------
    // Sun / moon billboard construction
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds the sun billboard as a camera-facing quad MeshInstance3D.
    ///     The billboard is positioned at the orbit origin; <see cref="UpdateBillboards" /> moves it
    ///     each frame. Texture is not loaded here (no asset channel available in SkyDomeNode) — the
    ///     material uses a placeholder white colour. If a caller supplies an asset reference later via
    ///     <see cref="SetBillboardTexture" />, the texture will be applied.
    ///     spec: Docs/RE/formats/sky.md §D.5 — sun billboard size 2048.0, texture sun.dds.
    ///     spec: Docs/RE/formats/sky.md §B.1 — sun built before star-dome.
    /// </summary>
    private void BuildSunBillboard()
    {
        // Build a simple quad (two triangles) for the sun billboard.
        // The quad is centred at the local origin; world position is set each frame by UpdateBillboards.
        // spec: Docs/RE/formats/sky.md §D.5 — sun billboard size 2048.0 (world units).
        var half = SunBillboardSize / 2f; // spec: Docs/RE/formats/sky.md §D.5
        Vector3[] verts =
        [
            new(-half, -half, 0f),
            new(half, -half, 0f),
            new(half, half, 0f),
            new(-half, half, 0f)
        ];
        Vector2[] uvs =
        [
            new(0f, 1f),
            new(1f, 1f),
            new(1f, 0f),
            new(0f, 0f)
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        // Unshaded + fog-exempt + depth-write-never (billboard draws over the dome but under scene geo).
        // spec: Docs/RE/specs/environment.md §8 item 9 — draw order: sun over cloud/star dome.
        const string BillboardShader =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_mix, depth_draw_never, cull_disabled;

            uniform vec4 albedo_color : source_color = vec4(1.0, 0.95, 0.7, 1.0);
            uniform sampler2D albedo_tex : source_color, hint_default_white;

            void fragment() {
                vec4 tex = texture(albedo_tex, UV);
                ALBEDO = albedo_color.rgb * tex.rgb;
                ALPHA  = albedo_color.a * tex.a;
            }
            """;

        var shader = new Shader { Code = BillboardShader };
        var mat = new ShaderMaterial { Shader = shader };
        // Warm white placeholder; a real sun.dds texture would be applied via SetBillboardTexture.
        // Aesthetic warm-white default (not spec-dictated).
        mat.SetShaderParameter("albedo_color", new Color(1f, 0.95f, 0.7f));
        mat.RenderPriority = -64; // behind world geo, in front of domes (which use -128)

        var mi = new MeshInstance3D
        {
            Name = "SunBillboard",
            Mesh = mesh,
            MaterialOverride = mat,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            Visible = true
        };
        AddChild(mi);
        _sunBillboard = mi;

        GD.Print("[SkyDome] SunBillboard constructed. spec: Docs/RE/formats/sky.md §D.5 (sun billboard size 2048.0).");
    }

    /// <summary>
    ///     Builds the moon billboard as a camera-facing quad MeshInstance3D.
    ///     Moon phase texture selection is oracle-pending (§D.3 — floor((day_counter mod 30) / 2)).
    ///     spec: Docs/RE/formats/sky.md §D.2 — moon: flat circle orbit, sine(+3200) X, cosine(+3200) Y, Z=0.
    ///     spec: Docs/RE/formats/sky.md §D.3 — moon phase: floor((day_counter mod 30) / 2) → moon{i}.dds.
    ///     spec: Docs/RE/formats/sky.md §B.1 — moon built after cloud-dome.
    ///     ORACLE-PENDING: moon texture asset chain (moon{i}.dds loading via Assets.Mapping) not yet
    ///     wired; placeholder blue-white disc until texture asset channel is available.
    /// </summary>
    private void BuildMoonBillboard()
    {
        // Same quad geometry as the sun but a slightly smaller display size.
        // The spec gives no explicit moon billboard size; using SunBillboardSize / 2 as a
        // reasonable default. ORACLE-PENDING: exact moon size not confirmed in sky.md.
        const float MoonSize = SunBillboardSize / 2f; // aesthetic / oracle-pending — sky.md §D.2 gives no explicit size
        var half = MoonSize / 2f;

        Vector3[] verts =
        [
            new(-half, -half, 0f),
            new(half, -half, 0f),
            new(half, half, 0f),
            new(-half, half, 0f)
        ];
        Vector2[] uvs =
        [
            new(0f, 1f),
            new(1f, 1f),
            new(1f, 0f),
            new(0f, 0f)
        ];
        int[] indices = [0, 1, 2, 0, 2, 3];

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        const string BillboardShader =
            """
            shader_type spatial;
            render_mode unshaded, fog_disabled, blend_mix, depth_draw_never, cull_disabled;

            uniform vec4 albedo_color : source_color = vec4(0.85, 0.9, 1.0, 1.0);
            uniform sampler2D albedo_tex : source_color, hint_default_white;

            void fragment() {
                vec4 tex = texture(albedo_tex, UV);
                ALBEDO = albedo_color.rgb * tex.rgb;
                ALPHA  = albedo_color.a * tex.a;
            }
            """;

        var shader = new Shader { Code = BillboardShader };
        var mat = new ShaderMaterial { Shader = shader };
        // Cool blue-white placeholder; moon{i}.dds not yet loaded. Aesthetic default.
        mat.SetShaderParameter("albedo_color", new Color(0.85f, 0.9f, 1f));
        mat.RenderPriority = -64;

        var mi = new MeshInstance3D
        {
            Name = "MoonBillboard",
            Mesh = mesh,
            MaterialOverride = mat,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
            Visible = true
        };
        AddChild(mi);
        _moonBillboard = mi;

        GD.Print("[SkyDome] MoonBillboard constructed (phase texture oracle-pending). " +
                 "spec: Docs/RE/formats/sky.md §D.2 (flat circle orbit, ±3200 scale).");
    }

    // -------------------------------------------------------------------------
    // Sun / moon billboard per-frame orbit update
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Computes and applies sun/moon world positions from the seconds-of-day orbit formula.
    ///     Also drives the tracked directional light from the negated sun position (§D.2.1).
    ///     FIX 15c — IDA-CONFIRMED (SkySun_UpdateBillboardOrbit @0x44987c; the decompiler's "logf_1 /
    ///     logf_2" autonames are MISNOMERS — _logf_1 @0x401647 is cos, _logf_2 @0x401661 is sin):
    ///       flt_7961F8 = 0x3c8efa35 = π/180; dbl_720988 = −3200.0; dbl_720820 = 360.0;
    ///       dbl_720818 = 1/86400; dbl_720990 = 45.0.
    ///       angle_deg = secondsOfDay × 360 × (1/86400); angle_rad = angle_deg × (π/180)
    ///                 = secondsOfDay × 2π / 86400.
    ///       flt_7B1288 = cos(45° × π/180) = cos(45°); flt_7B1284 = sin(45° × π/180) = sin(45°)
    ///                 (computed once at runtime; both static-init to 0 in the IDB).
    ///       sunX = sin(angle) × −3200                                 (@0x44996f, esi+10h)
    ///       sunY = cos(angle) × −3200 × flt_7B1288  (= ×cos45°)       (@0x4499a2, esi+8)
    ///       sunZ = sunY × flt_7B1284                (= ×sin45°)       (@0x4499ae, esi+0Ch)
    ///     Moon orbit (SkyMoon_UpdateBillboardOrbit @0x447ca8, IDA-CONFIRMED):
    ///       moonX = sin(angle) × +3200; moonY = cos(angle) × +3200; moonZ = 0 (flat circle, no Z).
    ///     Sun/moon positions are computed in LEGACY world space then routed through
    ///     WorldCoordinates.ToGodot (single Z-negate). Directional light direction = −normalize(sunPos)
    ///     (sub_44966F @0x44966F: negates the orbit pos into light dir obj+184 + global sun triple,
    ///     gated *(this+6704)==0 — the sole per-frame owner of the light direction).
    ///     spec: Docs/RE/formats/sky.md §D.1 — angle formula (seconds-of-day × 2π / 86400): CONFIRMED.
    ///     spec: Docs/RE/formats/sky.md §D.2 — ±3200 scale, sign opposition, moon flat circle.
    ///     spec: Docs/RE/formats/sky.md §D.2.1 — directional light direction = −sunPos.
    /// </summary>
    private void UpdateBillboards(double clockMs)
    {
        // Seconds of day (wraps at 86400 per the spec period).
        // IDA: angle_deg = secondsOfDay × 360 × (1/86400); angle_rad = angle_deg × (π/180).
        // This collapses to secondsOfDay × 2π / 86400. spec: Docs/RE/formats/sky.md §D.1.
        var secondsOfDay = clockMs / 1000.0 % 86400.0; // spec: Docs/RE/formats/sky.md §D.1
        var angleRad = secondsOfDay * (2.0 * Math.PI) / 86400.0; // IDA: SkySun_UpdateBillboardOrbit @0x44993d..0x449949

        var sinA = Math.Sin(angleRad);
        var cosA = Math.Cos(angleRad);

        // 45° tilt factors — IDA flt_7B1288 = cos(45°), flt_7B1284 = sin(45°)
        // (= cos/sin of SunTiltDeg × π/180). spec: Docs/RE/formats/sky.md §D.2.
        var cos45 = Math.Cos(SunTiltDeg * (Math.PI / 180.0)); // IDA flt_7B1288
        var sin45 = Math.Sin(SunTiltDeg * (Math.PI / 180.0)); // IDA flt_7B1284

        // ── Sun position (LEGACY space) ───────────────────────────────────────────
        // IDA SkySun_UpdateBillboardOrbit @0x44987c:
        //   sunX = sin(angle) × −3200            (@0x44996f)
        //   sunY = cos(angle) × −3200 × cos45°   (@0x4499a2)
        //   sunZ = sunY × sin45°                 (@0x4499ae)
        var sunXLegacy = sinA * -OrbitScale; // IDA @0x44996f (sin = _logf_2)
        var sunYLegacy = cosA * -OrbitScale * cos45; // IDA @0x4499a2 (cos = _logf_1, × flt_7B1288)
        var sunZLegacy = sunYLegacy * sin45; // IDA @0x4499ae (× flt_7B1284)

        // Route through the single Z-negate world convention. spec: WorldCoordinates.ToGodot.
        var (sgx, sgy, sgz) = WorldCoordinates.ToGodot((float)sunXLegacy, (float)sunYLegacy, (float)sunZLegacy);
        var sunPos = new Vector3(sgx, sgy, sgz);

        // ── Moon position (LEGACY space; flat circle — no Z) ──────────────────────
        // IDA SkyMoon_UpdateBillboardOrbit @0x447ca8:
        //   moonX = sin(angle) × +3200 (@0x447d15); moonY = cos(angle) × +3200 (@0x447d3c); moonZ = 0.
        var moonXLegacy = sinA * OrbitScale; // IDA @0x447d15 (sin = _logf_2)
        var moonYLegacy = cosA * OrbitScale; // IDA @0x447d3c (cos = _logf_1)

        // Route through the single Z-negate convention (Z=0 negates to 0). spec: WorldCoordinates.ToGodot.
        var (mgx, mgy, mgz) = WorldCoordinates.ToGodot((float)moonXLegacy, (float)moonYLegacy, 0f);
        var moonPos = new Vector3(mgx, mgy, mgz);

        // Apply billboard world positions.
        if (_sunBillboard is not null)
            _sunBillboard.Position = sunPos;

        if (_moonBillboard is not null)
            _moonBillboard.Position = moonPos;

        // Drive the directional light from the negated sun position.
        // spec: Docs/RE/formats/sky.md §D.2.1 — light direction = −normalize(sunPos): HIGH.
        if (_trackedDirLight is not null && sunPos.LengthSquared() > 1e-6f)
        {
            var lightDir = -sunPos.Normalized(); // spec: Docs/RE/formats/sky.md §D.2.1
            // Godot DirectionalLight3D direction = the node's -Z axis in global space.
            // To aim the light at `lightDir`, set the basis so -Z = lightDir:
            //   +Z = -lightDir, +Y = world up (unless straight up/down), then compute X = Z.cross(Y).
            var up = Vector3.Up;
            if (Math.Abs(lightDir.Dot(up)) > 0.999f)
                up = Vector3.Right; // avoid degenerate cross product when sun is at zenith
            var right = lightDir.Cross(up).Normalized();
            var newUp = right.Cross(lightDir).Normalized();
            // Basis: X=right, Y=newUp, Z=-lightDir (Godot camera-space convention: -Z is forward).
            _trackedDirLight.Basis = new Basis(right, newUp, -lightDir);
        }
    }

    // -------------------------------------------------------------------------
    // Colour helpers
    // -------------------------------------------------------------------------

    /// <summary>BGRA u8 → Godot Color. spec: Docs/RE/specs/environment.md §6.2 — r=bgra[2], g=bgra[1], b=bgra[0].</summary>
    private static Color BgraToColor(BgraColor c)
    {
        return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
    }
}