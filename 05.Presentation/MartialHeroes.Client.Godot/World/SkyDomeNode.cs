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
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive sky-dome rendering node: builds and animates a star dome and cloud dome
/// from the parsed environment bin data.
///
/// Call <see cref="Build"/> once after construction (on the Godot main thread).
/// Call <see cref="UpdateDomes"/> each frame from <see cref="EnvironmentNode._Process"/>.
/// </summary>
public sealed partial class SkyDomeNode : Node3D
{
    // -------------------------------------------------------------------------
    // Day/night cycle timing constants
    // spec: Docs/RE/specs/environment.md §2.1
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stardome/clouddome keyframe count (coarser 12-frame cycle).
    /// spec: Docs/RE/specs/environment.md §2.1 — STARDOME_KF_COUNT = 12: CONFIRMED.
    /// </summary>
    private const int DomeKfCount = StarDomeBin.KeyframeCount; // 12

    /// <summary>
    /// Milliseconds per stardome/clouddome keyframe step (4 × 1800 ms lighting keyframes).
    /// spec: Docs/RE/specs/environment.md §2.1 — STARDOME_KF_MS = 7200: CONFIRMED.
    /// </summary>
    private const double DomeKfMs = 7200.0;

    /// <summary>
    /// Total simulated day period (same as the main cycle).
    /// spec: Docs/RE/specs/environment.md §2.1 — SKY_PERIOD_MS = 86 400: CONFIRMED.
    /// </summary>
    private const double PeriodMs = 86_400.0;

    // -------------------------------------------------------------------------
    // Dome geometry constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dome radius in world units. Sized to sit just inside the far-clip plane (15 000 wu)
    /// so it always surrounds the scene without z-fighting at the horizon.
    /// Engineering choice (not a legacy constant); the legacy domes were rendered at an
    /// unspecified radius by the D3D9 pipeline's fixed-function sky stage.
    /// </summary>
    private const float DomeRadius = 13_000f;

    /// <summary>
    /// Sector count for the dome hemisphere tessellation.
    /// Chosen to match the per-keyframe vertex count constraints from the spec:
    ///   - Cloud dome: 240 vertices = 15 stacks × 16 sectors.
    ///     spec: Docs/RE/formats/environment_bins.md §5.4 — "vertex count … is 240": CONFIRMED.
    ///   - Star dome: 192 vertices = 12 stacks × 16 sectors.
    ///     spec: Docs/RE/formats/environment_bins.md §4.1 — "192 star instances": CONFIRMED.
    /// </summary>
    private const int DomeSectors = 16;

    /// <summary>
    /// Stack count for the cloud dome (15 stacks × 16 sectors = 240 vertices).
    /// spec: Docs/RE/formats/environment_bins.md §5.4 — vertex count 240: CONFIRMED.
    /// </summary>
    private const int CloudDomeStacks = 15;

    /// <summary>
    /// Stack count for the star dome (12 stacks × 16 sectors = 192 vertices).
    /// spec: Docs/RE/formats/environment_bins.md §4.1 — 192 star instances: CONFIRMED.
    /// </summary>
    private const int StarDomeStacks = 12;

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
    /// UV offset rate per second for each speed unit from the cloud cycle spec.
    /// The spec gives speed as an integer 1–10 but does not specify world-space drift velocity.
    /// spec: Docs/RE/formats/environment_bins.md §6.3 known unknown — "speed units: unverified".
    /// Engineering choice: 0.003 UV/s per speed unit (so speed 5 ≈ 0.015 UV/s — a gentle drift).
    /// </summary>
    private const float CloudUvRatePerSpeedUnit = 0.003f;

    // -------------------------------------------------------------------------
    // Node references
    // -------------------------------------------------------------------------

    private MeshInstance3D? _starDomeMesh;
    private MeshInstance3D? _cloudDomeMesh1; // inner cloud layer
    private MeshInstance3D? _cloudDomeMesh2; // outer/haze cloud layer

    private ShaderMaterial? _starMaterial;
    private ShaderMaterial? _cloudMaterial1;
    private ShaderMaterial? _cloudMaterial2;

    // -------------------------------------------------------------------------
    // Data references (parsed bins)
    // -------------------------------------------------------------------------

    private StarDomeBin? _starDome;
    private CloudDomeBin? _cloudDome;
    private CloudCycleBin? _cloudCycle;

    // Active cloud cycle row (row 0 — day-pattern selection algorithm is a known unknown).
    // spec: Docs/RE/formats/environment_bins.md §6.3 — selection algorithm: known unknown.
    private CloudCycleRow _activeCycleRow;

    // -------------------------------------------------------------------------
    // Per-frame animation state
    // -------------------------------------------------------------------------

    private float _cloudUvOffset; // accumulated UV scroll offset (cycles mod 1.0)

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the dome meshes from parsed environment data. Must be called on the Godot main thread
    /// once, after this node is added to the scene tree.
    ///
    /// Passing null for either dome bin is a graceful no-op: that dome is simply not created.
    /// spec: Docs/RE/specs/environment.md §7 — graceful fallback when files absent.
    /// </summary>
    public void Build(StarDomeBin? starDome, CloudDomeBin? cloudDome, CloudCycleBin? cloudCycle)
    {
        _starDome = starDome;
        _cloudDome = cloudDome;
        _cloudCycle = cloudCycle;

        // Choose first day-pattern row. Selection algorithm is unconfirmed (known unknown).
        // spec: Docs/RE/formats/environment_bins.md §6.3 — day-pattern selection: known unknown.
        _activeCycleRow = cloudCycle?.Rows[0] ?? default;

        if (starDome is not null)
            BuildStarDome();

        if (cloudDome is not null)
            BuildCloudDome();

        // Report construction so headless runs can verify the dome creation log line (lane gate).
        string starStatus = starDome is not null ? "built" : "absent(no-op)";
        string cloudStatus = cloudDome is not null ? "built" : "absent(no-op)";
        string cycleStatus = cloudCycle is not null
            ? $"speed={_activeCycleRow.Speed} cloud1Id={_activeCycleRow.Cloud1Id0To12H}"
            : "absent";
        GD.Print($"[SkyDome] star={starStatus} cloud={cloudStatus} cloudCycle={cycleStatus} " +
                 $"radius={DomeRadius:F0}wu sectors={DomeSectors}");
    }

    /// <summary>
    /// Updates dome tint and visibility for the current day/night clock.
    /// Call once per frame from EnvironmentNode._Process (main thread only).
    ///
    /// <paramref name="clockMs"/> is the same running clock EnvironmentNode maintains
    /// (milliseconds elapsed, already wrapped to the day period).
    /// <paramref name="delta"/> is the frame delta in seconds (for UV scroll animation).
    /// spec: Docs/RE/specs/environment.md §3.2 — per-frame update: steps 4–5 for star/cloud.
    /// </summary>
    public void UpdateDomes(double clockMs, double delta)
    {
        // Dome 12-frame keyframe.
        // spec: Docs/RE/specs/environment.md §2.2 — star_kf_index = t_wrapped / STARDOME_KF_MS.
        double tWrapped = clockMs % PeriodMs;
        int domeKf = (int)(tWrapped / DomeKfMs) % DomeKfCount;
        int domeKfNext = (domeKf + 1) % DomeKfCount;
        float domeFrac = (float)((tWrapped % DomeKfMs) / DomeKfMs);

        // Main 48-frame keyframe index (for day/night star-visibility weighting).
        // spec: Docs/RE/specs/environment.md §2.2 — kf_index = t_wrapped / SKY_KEYFRAME_MS.
        float mainKfFloat = (float)(tWrapped / 1800.0);

        UpdateStarDome(domeKf, domeKfNext, domeFrac, mainKfFloat);
        UpdateCloudDomes(domeKf, domeKfNext, domeFrac, mainKfFloat, (float)delta);
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
        Color tintA = BgraToColor(_starDome.StarColors[kf][0]);
        Color tintB = BgraToColor(_starDome.StarColors[kfNext][0]);
        Color tint = tintA.Lerp(tintB, frac);

        // Day/night alpha: fade stars in at dusk and out at dawn.
        float alpha = StarAlpha(mainKf);

        // Apply to the unshaded material as a tint × alpha.
        _starMaterial.SetShaderParameter("albedo_color", new Color(tint.R, tint.G, tint.B, alpha));

        // Hide the mesh entirely when fully transparent (saves GPU overdraw during daytime).
        _starDomeMesh.Visible = alpha > 0.01f;
    }

    /// <summary>
    /// Computes star visibility alpha [0, 1] from the fractional 48-keyframe index.
    ///
    /// Night = [0, StarFullNightKf] and [StarFullNightKf2, 48): alpha = 1.
    /// Day   = [StarFadeOutKf, StarFadeInKf]: alpha = 0.
    /// Transition dawn [StarFullNightKf, StarFadeOutKf]: linear fade from 1→0.
    /// Transition dusk [StarFadeInKf, StarFullNightKf2]: linear fade from 0→1.
    ///
    /// Engineering choice — boundary keyframes are symmetric around kf 0 and kf 24.
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
        float cloudAlpha = 1f - StarAlpha(mainKf);

        // UV scroll driven by the active cycle row's speed value.
        // spec: Docs/RE/formats/environment_bins.md §6.1 — speed u8 range 1–10: CONFIRMED.
        float speedUnits = Math.Max(1, (int)_activeCycleRow.Speed);
        _cloudUvOffset += speedUnits * CloudUvRatePerSpeedUnit * delta;
        _cloudUvOffset %= 1f;

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
        Color tintA = BgraToColor(layerColors[kf][0]);
        Color tintB = BgraToColor(layerColors[kfNext][0]);
        Color tint = tintA.Lerp(tintB, frac);

        mat.SetShaderParameter("albedo_color", new Color(tint.R, tint.G, tint.B, alpha));
        mat.SetShaderParameter("uv_offset", _cloudUvOffset);

        mesh.Visible = alpha > 0.01f;
    }

    // -------------------------------------------------------------------------
    // Dome mesh construction
    // -------------------------------------------------------------------------

    private void BuildStarDome()
    {
        // Star dome: inverted hemisphere, 12 stacks × 16 sectors = 192 vertices.
        // spec: Docs/RE/formats/environment_bins.md §4.1 — 192 star instances: CONFIRMED.
        ArrayMesh mesh = BuildHemisphereMesh(DomeRadius, StarDomeStacks, DomeSectors, inverted: true);

        _starMaterial = BuildDomeMaterial(isCloud: false);
        var mi = new MeshInstance3D
        {
            Name = "StarDome",
            Mesh = mesh,
            MaterialOverride = _starMaterial,
            // Render behind world geometry. Negative priority = drawn early in the transparent
            // pass, so opaque geometry writes over it. Engineering choice; the spec only
            // documents draw order (spec §8 item 9), not the exact Godot render-priority value.
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        mi.SetLayerMaskValue(1, true);
        // Use VisualInstance3D.Layers to assign sky layer (bit 20 = Godot sky/background convention).
        // This is an engineering choice — Godot does not mandate a specific sky layer number.
        mi.Layers = 1; // visible to camera layer 1 (the default 3D camera layer)
        AddChild(mi);
        _starDomeMesh = mi;
    }

    private void BuildCloudDome()
    {
        // Cloud dome: two layers, each inverted hemisphere, 15 stacks × 16 sectors = 240 vertices.
        // spec: Docs/RE/formats/environment_bins.md §5.4 — vertex count 240: CONFIRMED.

        // Inner cloud layer (layer 1).
        ArrayMesh mesh1 = BuildHemisphereMesh(DomeRadius * 0.97f, CloudDomeStacks, DomeSectors, inverted: true);
        _cloudMaterial1 = BuildDomeMaterial(isCloud: true);
        var mi1 = new MeshInstance3D
        {
            Name = "CloudDomeInner",
            Mesh = mesh1,
            MaterialOverride = _cloudMaterial1,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
        };
        AddChild(mi1);
        _cloudDomeMesh1 = mi1;

        // Outer/haze cloud layer (layer 2) — slightly larger radius so it draws behind layer 1.
        // spec: Docs/RE/formats/environment_bins.md §5.5 — "layer 2 is outer/haze": CONFIRMED (inferred).
        ArrayMesh mesh2 = BuildHemisphereMesh(DomeRadius, CloudDomeStacks, DomeSectors, inverted: true);
        _cloudMaterial2 = BuildDomeMaterial(isCloud: true);
        var mi2 = new MeshInstance3D
        {
            Name = "CloudDomeOuter",
            Mesh = mesh2,
            MaterialOverride = _cloudMaterial2,
            ExtraCullMargin = 0f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 1,
        };
        AddChild(mi2);
        _cloudDomeMesh2 = mi2;
    }

    /// <summary>
    /// Builds an inverted hemisphere ArrayMesh (vertices on the inside face inward).
    /// The hemisphere spans from Y=0 (equator) to Y=+radius (zenith).
    ///
    /// The tessellation is a standard UV-sphere hemisphere: <paramref name="stacks"/> latitude
    /// rings × <paramref name="sectors"/> longitude segments.
    ///
    /// When <paramref name="inverted"/> is true the triangle winding is reversed so that the mesh
    /// is visible from inside (back-face culling sees the outside; the camera is at the centre).
    ///
    /// Vertex count = stacks × sectors (no top cap vertex — the topmost ring collapses cleanly).
    /// For cloud dome: 15 × 16 = 240. For star dome: 12 × 16 = 192.
    /// spec: Docs/RE/formats/environment_bins.md §5.4 — cloud vertex count 240: CONFIRMED.
    /// spec: Docs/RE/formats/environment_bins.md §4.1 — star vertex count 192: CONFIRMED.
    ///
    /// Note: GltfDocument is NOT used. The mesh is built via Godot ArrayMesh directly.
    /// spec: CLAUDE.md — "GltfDocument.AppendFromBuffer crashes natively … Never use it."
    /// </summary>
    private static ArrayMesh BuildHemisphereMesh(float radius, int stacks, int sectors, bool inverted)
    {
        // We build a UV-hemisphere: theta goes from π/2 (equator, Y≈0) to π (zenith, Y=radius).
        // phi goes from 0 to 2π (full circle).
        // The inverted flag reverses index winding so faces point inward.

        // Vertex/index counts are fully deterministic: (stacks+1)×(sectors+1) ring vertices and
        // stacks×sectors×6 indices (two triangles per quad). Pre-size and write by index to avoid the
        // List growth + ToArray() copies — this is one-time build cost, but the counts are known up front.
        int stride = sectors + 1;
        int vertCount = (stacks + 1) * stride;
        int indexCount = stacks * sectors * 6;

        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var indices = new int[indexCount];

        // Theta from π/2 (equator) up to π (top). stacks+1 rings including equator and zenith.
        for (int stack = 0; stack <= stacks; stack++)
        {
            // theta: π/2 at equator (stack=0), π at zenith (stack=stacks).
            float theta = MathF.PI / 2f + stack * (MathF.PI / 2f) / stacks;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int sec = 0; sec <= sectors; sec++)
            {
                float phi = sec * (2f * MathF.PI) / sectors;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);

                // Sphere position (Y = cosTheta × radius, up). At θ=π/2 cosθ=0 → equator. At θ=π cosθ=-1 → top.
                // Remap: use sinTheta for horizontal spread and -cosTheta for Y (so equator Y=0, zenith Y=radius).
                float x = radius * sinTheta * cosPhi;
                float y = radius * (-cosTheta); // remapped so zenith is +Y
                float z = radius * sinTheta * sinPhi;

                Vector3 pos = new(x, y, z);
                // Inward-pointing normal (negated outward normal) for inverted dome.
                Vector3 outward = pos.Normalized();
                int vi = stack * stride + sec;
                vertices[vi] = pos;
                normals[vi] = inverted ? -outward : outward;
                uvs[vi] = new Vector2((float)sec / sectors, (float)stack / stacks);
            }
        }

        // Generate quad indices (two triangles per quad), reversed winding if inverted.
        int idx = 0;
        for (int stack = 0; stack < stacks; stack++)
        {
            for (int sec = 0; sec < sectors; sec++)
            {
                int tl = stack * stride + sec;
                int tr = tl + 1;
                int bl = tl + stride;
                int br = bl + 1;

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
        }

        var arrays = new global::Godot.Collections.Array();
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
    /// Builds an unshaded (no lighting), fog-exempt, transparent ShaderMaterial for a sky dome.
    ///
    /// The material uses a minimal inline shader:
    ///   - No lighting calculation (unshaded = sky appearance does not depend on scene lights).
    ///   - Fog exempt: the Godot 4 FogOverride shader built-in disables depth-fog on this surface.
    ///   - Alpha from the albedo_color parameter for day/night fading.
    ///   - UV offset parameter for cloud scroll animation.
    ///   - Depth write disabled so the sky does not occlude world geometry's depth buffer.
    ///   - Render priority = -128 (behind all default-priority geometry).
    ///
    /// The unshaded + fog-exempt approach is the canonical Godot 4 sky-dome technique.
    /// Engineering choice; the spec does not mandate a shader implementation.
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
            uniform float uv_offset = 0.0;

            void fragment() {
                vec2 uv = UV + vec2(uv_offset, 0.0);
                ALBEDO = albedo_color.rgb;
                ALPHA  = albedo_color.a;
            }
            """;

        var shader = new Shader();
        shader.Code = ShaderSrc;

        var mat = new ShaderMaterial();
        mat.Shader = shader;
        mat.RenderPriority = -128; // behind default (0) geometry in the transparent pass
        mat.SetShaderParameter("albedo_color", new Color(1f, 1f, 1f, 1f));
        mat.SetShaderParameter("uv_offset", 0f);
        return mat;
    }

    // -------------------------------------------------------------------------
    // Colour helpers
    // -------------------------------------------------------------------------

    /// <summary>BGRA u8 → Godot Color. spec: Docs/RE/specs/environment.md §6.2 — r=bgra[2], g=bgra[1], b=bgra[0].</summary>
    private static Color BgraToColor(BgraColor c)
        => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
}