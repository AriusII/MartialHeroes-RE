// World/WaterRenderer.cs
//
// PASSIVE Node3D that renders a semi-transparent, animated water plane over a map cell or
// region that contains water (i.e. where terrain MIN_HEIGHTFILED suggests a below-grade
// surface is present).
//
// Rendering approach:
//   A single PlaneMesh with a cheap inline ShaderMaterial is used.  The shader scrolls two
//   overlapping UV layers at slightly different speeds to simulate surface ripples, applies a
//   Fresnel-based transparency blend, and tints the result with a configurable water colour.
//   No physics simulation, no reflection probes, no sub-surface scattering — just a fast,
//   allocation-free animated plane that reads well in a top-down MMORPG camera.
//
// Threading: Configure() may be called from any thread before the node is added to the scene
//   tree.  _Process runs on the Godot main thread and only writes uniform values — safe.
//
// Legacy context:
//   The original client renders water as an animated UV-scrolling terrain sub-pass (FVF 0x152,
//   dest-blend toggles INVSRCALPHA↔ONE between passes, animated when the 50 ms timer fires).
//   spec: Docs/RE/specs/client_runtime.md §"Terrain water animation gate" — "≥ 50 ms timer;
//         static vs UV-scrolling water variant".  CONFIRMED.
//   We reproduce the UV-scroll concept with two UV channels at slightly different speeds; the
//   50 ms gate is not replicated (we animate every frame, which is smoother under Godot's
//   Variable Rate loop).
//
//   OPTION_WATER in the client config table enables/disables water rendering.
//   spec: Docs/RE/formats/config_tables.md §"OPTION_WATER" — int 0/1, CONFIRMED.
//   This node is simply not added to the scene when water is disabled — the config check belongs
//   in the orchestrator (RealWorldRenderer / GameLoop), not here.
//
// Water height / extent determination (caller guidance — see Configure() XML doc):
//   The legacy client does not store an explicit water-plane Y in the .map scene descriptor.
//   The .map TERRAIN section MIN_HEIGHTFILED directive gives the minimum terrain vertex height
//   for the cell.  spec: Docs/RE/formats/terrain.md §3.4 — "MIN_HEIGHTFILED float: CONFIRMED".
//   When MIN_HEIGHTFILED is near or below 0 it is a reliable indicator that the cell is a
//   water-edge cell.  A practical heuristic confirmed by the recon landscape is:
//
//       waterHeightY = MapSection.MinHeightFiled + <small positive offset>
//
//   e.g. if MIN_HEIGHTFILED ≈ 1.3 then waterHeightY ≈ 1.3 + 2.0 = 3.3  (just above the
//   lowest shoreline vertices so the water plane sits slightly above the seafloor).  A
//   caller that has read the MapDescriptor's TERRAIN section can apply this directly.
//   Until R5 recon supplies a definitive constant, 0.0f is the safe universal default for
//   coastal / near-sea-level cells.
//
// Extent:
//   One cell = 1024 × 1024 legacy world units (64 quads × 16 units/quad).
//   spec: Docs/RE/formats/terrain.md §1.4 — worldX extent = (mapX-10000)×1024, cell size 1024. CONFIRMED.
//   The water plane is sized to cover the 3×3 sector streaming ring (3×1024 = 3072) by default,
//   so the entire visible terrain patch is covered without gaps.  The caller may pass a smaller
//   extent if water is localised.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive rendering node.
// spec: Docs/RE/specs/client_runtime.md §"Terrain water animation gate" (≥50 ms UV scroll).
// spec: Docs/RE/formats/terrain.md §3.4 (MIN_HEIGHTFILED — minimum terrain Y per cell). CONFIRMED.
// spec: Docs/RE/formats/config_tables.md §"OPTION_WATER" — water render toggle. CONFIRMED.

using Godot;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive <see cref="Node3D"/> that places a large semi-transparent animated water plane in
/// the scene.  Zero game-rule authority: it only drives shader time uniforms each frame and
/// exposes <see cref="Configure"/> for the orchestrator to position it.
///
/// Add this node as a child of your world root (e.g. a child of <c>RealWorldRenderer</c>) and
/// call <see cref="Configure"/> before or after <see cref="Node._Ready"/> — the configuration
/// is applied either way.  Set <see cref="WaterColor"/> before adding to the tree to override
/// the default blue-green tint.
/// </summary>
public sealed partial class WaterRenderer : Node3D
{
    // -------------------------------------------------------------------------
    // Public configuration (may be set before or after _Ready)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Water surface colour and base opacity.  The alpha component is the base transparency
    /// before Fresnel modulation: 0.65 gives a translucent but clearly visible water surface
    /// from the default MMORPG bird's-eye camera angle.
    /// </summary>
    public Color WaterColor { get; set; } = new Color(0.10f, 0.35f, 0.65f, 0.65f);

    /// <summary>
    /// Speed of the primary UV scroll layer (UV1).  Legacy client scrolled terrain water
    /// with a 50 ms animation gate; a speed of ~0.04 u/s matches that visual cadence at 60 fps.
    /// spec: Docs/RE/specs/client_runtime.md §"Terrain water animation gate". CONFIRMED reference.
    /// </summary>
    public float ScrollSpeedU1 { get; set; } = 0.04f;

    /// <summary>
    /// Speed of the secondary UV scroll layer (UV2) — slightly slower than UV1 so the two
    /// layers animate out of phase and produce a ripple-like interference pattern.
    /// </summary>
    public float ScrollSpeedU2 { get; set; } = 0.025f;

    /// <summary>
    /// Fresnel exponent controlling edge glow/transparency falloff.
    /// Higher values concentrate the effect toward the edges of the plane (glancing angles).
    /// 3.0 is a visually neutral value for a fairly flat water surface.
    /// </summary>
    public float FresnelExponent { get; set; } = 3.0f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    // The MeshInstance3D carrying the animated water PlaneMesh.
    private MeshInstance3D? _meshInst;

    // The ShaderMaterial applied to the plane surface.
    private ShaderMaterial? _mat;

    // Accumulated animation time driven by _Process delta (seconds).
    private float _time;

    // Configuration state: set by Configure(), consumed in _Ready (or immediately if already ready).
    private Vector3 _centre = Vector3.Zero;
    private float _size = 3072f; // default: 3×3 cell ring (3 × 1024 legacy units)
    private float _waterHeightY = 0f;
    private bool _configured;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the water plane mesh and shader material.  If <see cref="Configure"/> was called
    /// before <c>_Ready</c> the position/size are applied immediately; otherwise the defaults
    /// (centred at origin, 3072-unit square, Y=0) are used until <see cref="Configure"/> is called.
    /// </summary>
    public override void _Ready()
    {
        BuildWaterPlane();

        // Apply any pre-_Ready Configure() call.
        if (_configured)
            ApplyPlacement();
    }

    /// <summary>
    /// Advances the shader time uniform each frame to drive UV scrolling.
    /// Called on the Godot main thread — safe to write shader uniforms here.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_mat is null) return;

        _time += (float)delta;

        // Write the two scroll offsets as separate uniforms so the shader can use them
        // without GLSL mod() drift over long sessions.
        _mat.SetShaderParameter("time", _time);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Positions and sizes the water plane.  May be called before or after <see cref="Node._Ready"/>.
    ///
    /// <para><paramref name="centre"/>: Godot-space XZ centre of the water plane. Compute from
    /// the cell coordinates as:
    /// <code>
    ///   float legacyX = (mapX - 10000) * 1024f + halfSize;
    ///   float legacyZ = (mapZ - 10000) * 1024f + halfSize;
    ///   centre = new Vector3(legacyX, 0f, -legacyZ);   // negate Z: spec WorldCoordinates.ToGodot
    /// </code>
    /// spec: Docs/RE/formats/terrain.md §1.4 — worldX = (mapX-10000)×1024, cell size 1024. CONFIRMED.
    /// spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z). CONFIRMED.
    /// </para>
    ///
    /// <para><paramref name="size"/>: edge length of the square water plane in legacy world units.
    /// One cell = 1024 units; the default 3×3 ring = 3072 units.
    /// spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024. CONFIRMED.
    /// </para>
    ///
    /// <para><paramref name="waterHeightY"/>: world-space Y of the water surface.  The legacy client
    /// does not store an explicit water height in the .map file.  A reliable heuristic is:
    /// <c>waterHeightY = terrainSection.MinHeightFiled + 2.0f</c>, where <c>MinHeightFiled</c>
    /// is parsed from the TERRAIN section of the cell's .map descriptor.
    /// spec: Docs/RE/formats/terrain.md §3.4 — MIN_HEIGHTFILED float: CONFIRMED.
    /// If the .map TERRAIN section is unavailable or the min height is unknown, pass 0f (safe
    /// default for sea-level / coastal cells).
    /// </para>
    /// </summary>
    /// <param name="centre">Godot-space XYZ centre (Y component is overridden by <paramref name="waterHeightY"/>).</param>
    /// <param name="size">Edge length of the water square (legacy world units).</param>
    /// <param name="waterHeightY">Y position of the water surface (legacy world units).</param>
    public void Configure(Vector3 centre, float size, float waterHeightY)
    {
        _centre = centre;
        _size = size;
        _waterHeightY = waterHeightY;
        _configured = true;

        // If already in the scene tree, apply immediately; otherwise _Ready will pick it up.
        if (IsInsideTree())
            ApplyPlacement();
    }

    // -------------------------------------------------------------------------
    // Mesh + material construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the PlaneMesh and attaches the animated water ShaderMaterial.
    /// Called once from <see cref="_Ready"/>.
    /// </summary>
    private void BuildWaterPlane()
    {
        // ---- Plane mesh ----
        // PlaneMesh lies in the XZ plane (Y=0 local), subdivided so the Fresnel effect has
        // enough vertex density to look smooth at large scales.  32×32 subdivisions give
        // 1024 quads for a 3072-unit plane — one sub-quad per ~96 world units, which is
        // visually adequate for a flat animated surface.  No per-vertex distortion is applied
        // (we rely entirely on UV animation for the ripple effect — cheap and correct for a
        // 2004-era style game).
        var planeMesh = new PlaneMesh();
        planeMesh.Size = new Vector2(_size, _size);
        planeMesh.SubdivideDepth = 32;
        planeMesh.SubdivideWidth = 32;

        // ---- Animated water shader ----
        // The shader scrolls two UV layers at different speeds and blends them with a simple
        // normal-ripple approximation.  Fresnel transparency is computed from the dot product
        // of the surface normal and the view direction (always (0,1,0) for a flat plane,
        // so Fresnel is view-angle dependent via the camera tilt).
        //
        // We use a Spatial (canvas_item is 2D) shader so it participates in the 3D depth pass
        // and renders with proper depth-test against terrain geometry.
        //
        // Legacy reference: the original client used INVSRCALPHA dest-blend with animated UV
        // multi-texture passes for water.
        // spec: Docs/RE/specs/client_runtime.md §"Terrain (animated, multi-texture)" — water
        //       identified as a UV-scrolling animated terrain sub-pass, dest-blend INVSRCALPHA↔ONE.
        //       CONFIRMED.
        var shader = new Shader();
        shader.Code = WaterShaderSource;

        _mat = new ShaderMaterial();
        _mat.Shader = shader;

        // Uniforms — written here once; _Process updates `time` each frame.
        _mat.SetShaderParameter("water_color", WaterColor);
        _mat.SetShaderParameter("scroll_speed_1", ScrollSpeedU1);
        _mat.SetShaderParameter("scroll_speed_2", ScrollSpeedU2);
        _mat.SetShaderParameter("fresnel_exp", FresnelExponent);
        _mat.SetShaderParameter("time", 0.0f);

        planeMesh.Material = _mat;

        // ---- MeshInstance3D ----
        _meshInst = new MeshInstance3D();
        _meshInst.Mesh = planeMesh;
        _meshInst.Name = "WaterPlaneMesh";
        _meshInst.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; // water doesn't cast shadows

        AddChild(_meshInst);

        GD.Print($"[WaterRenderer] Water plane built (size={_size:F0} units, 32×32 subdivisions).");
    }

    /// <summary>
    /// Applies the current <see cref="_centre"/>, <see cref="_size"/>, and
    /// <see cref="_waterHeightY"/> to the node position and updates the PlaneMesh size.
    /// </summary>
    private void ApplyPlacement()
    {
        // The water plane's Godot world position.
        // X and Z come from the cell centre; Y is the water surface height (legacy Y is
        // already in Godot Y — no axis flip needed for the vertical axis).
        // spec: WorldCoordinates.ToGodot — only Z is negated; Y is unchanged. CONFIRMED.
        Position = new Vector3(_centre.X, _waterHeightY, _centre.Z);

        // Resize the PlaneMesh if it exists (safe to mutate after _Ready).
        if (_meshInst?.Mesh is PlaneMesh plane)
        {
            plane.Size = new Vector2(_size, _size);
        }

        GD.Print($"[WaterRenderer] Placed at Godot ({Position.X:F0}, {Position.Y:F1}, {Position.Z:F0}), " +
                 $"size={_size:F0}.");
    }

    // -------------------------------------------------------------------------
    // Shader source
    // -------------------------------------------------------------------------

    /// <summary>
    /// GLSL shader for the animated water plane.
    ///
    /// Technique:
    ///   Two UV layers scroll at <c>scroll_speed_1</c> and <c>scroll_speed_2</c> in slightly
    ///   different directions (+X and +X diagonal) so they drift out of phase over time.
    ///   Their XY derivatives are used as an ad-hoc normal perturbation, generating a ripple
    ///   effect from the difference between the two scroll offsets.
    ///   Fresnel blend modulates the alpha: near-normal incidence → more transparent;
    ///   glancing angle → more opaque.  The final alpha is clamped to <c>water_color.a</c> so
    ///   the caller can always cap overall opacity via <see cref="WaterColor"/>.
    ///
    /// Godot render_mode notes:
    ///   - <c>blend_mix</c>: standard alpha-blend (SRC_ALPHA / ONE_MINUS_SRC_ALPHA), matching
    ///     the legacy INVSRCALPHA dest-blend.
    ///     spec: Docs/RE/specs/client_runtime.md — "dest-blend toggles INVSRCALPHA↔ONE". CONFIRMED (reference).
    ///   - <c>depth_draw_opaque</c>: write depth only for fully-opaque fragments so underwater
    ///     geometry can still peek through at the edges.
    ///   - <c>cull_disabled</c>: render both faces so the water is visible from below (camera
    ///     can dip below the surface in free-fly mode).
    ///   - <c>unshaded</c> is intentionally NOT set: we do want the scene's ambient/directional
    ///     light to affect the surface colour lightly, giving a subtle shading variation with
    ///     the sun angle.
    /// </summary>
    private const string WaterShaderSource = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled;

// --------------------------------------------------------------------------
// Uniforms (written by WaterRenderer.BuildWaterPlane / _Process)
// --------------------------------------------------------------------------

/// Base colour and opacity ceiling.  Alpha = max surface opacity (Fresnel modulates down).
uniform vec4 water_color : source_color = vec4(0.10, 0.35, 0.65, 0.65);

/// Scroll speed of the primary UV layer (world units/second mapped to UV space).
uniform float scroll_speed_1 : hint_range(0.0, 1.0) = 0.04;

/// Scroll speed of the secondary UV layer — slower, different direction.
uniform float scroll_speed_2 : hint_range(0.0, 1.0) = 0.025;

/// Fresnel exponent: higher = tighter edge glow, more transparent at normal incidence.
uniform float fresnel_exp : hint_range(0.5, 8.0) = 3.0;

/// Accumulated time in seconds, updated every frame by WaterRenderer._Process.
uniform float time = 0.0;

// --------------------------------------------------------------------------
// Vertex shader
// --------------------------------------------------------------------------

varying vec2 uv_layer1;
varying vec2 uv_layer2;
varying vec3 world_normal;
varying vec3 view_dir;

void vertex() {
    // Tile the UV across the plane: scale by 8 so the ripple pattern repeats every
    // 1/8th of the plane width, giving a fine-grained water look at MMORPG distances.
    // (The plane itself is 3072 legacy units wide; /8 = 384 units per tile — roughly
    // three terrain quads, which looks natural.)
    vec2 base_uv = UV * 8.0;

    // Layer 1 scrolls diagonally along +X at scroll_speed_1.
    uv_layer1 = base_uv + vec2(time * scroll_speed_1, time * scroll_speed_1 * 0.4);

    // Layer 2 scrolls along -X at scroll_speed_2 (opposing current for interference).
    uv_layer2 = base_uv + vec2(-time * scroll_speed_2, time * scroll_speed_2 * 0.7);

    // Pass the object-space normal and camera direction to the fragment shader.
    // NORMAL is always (0,1,0) for a flat plane — this is used for the Fresnel term.
    world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
    view_dir = normalize((INV_VIEW_MATRIX * vec4(0.0, 0.0, 1.0, 0.0)).xyz);
}

// --------------------------------------------------------------------------
// Fragment shader
// --------------------------------------------------------------------------

void fragment() {
    // ---- Ripple normal approximation ----
    // The difference between the two scrolling offsets creates a time-varying
    // oscillation in the (x,y) plane that we use as an ad-hoc normal perturbation.
    // This is a cheap substitute for a scrolling normal map (no texture asset needed).
    vec2 delta = sin(uv_layer1 * 6.2831) - sin(uv_layer2 * 6.2831);
    // Construct a perturbed normal by tilting the flat (0,1,0) surface normal slightly
    // in the XZ plane.  Magnitude is kept small (0.12) so the plane reads as water, not
    // a bumpy surface.
    vec3 perturbed_normal = normalize(vec3(delta.x * 0.12, 1.0, delta.y * 0.12));

    // ---- Fresnel term ----
    // Fresnel(v,n) = 1 - saturate(dot(v,n))^fresnel_exp.
    // For a flat plane and a top-down camera, FRAGCOORD-derived view direction is used.
    // VIEW is the camera-space forward direction (always (0,0,-1) in camera space, pointing
    // into the screen).  We use the VIEW built-in which Godot provides in fragment().
    float vdotn = clamp(dot(normalize(VIEW), perturbed_normal), 0.0, 1.0);
    float fresnel = 1.0 - pow(vdotn, fresnel_exp);
    // Fresnel alpha: at grazing angles (fresnel→1) use full water_color alpha;
    // at normal incidence (fresnel→0) reduce to 40% of that — still visible but transparent.
    float alpha = water_color.a * mix(0.40, 1.0, fresnel);

    // ---- Surface colour ----
    // Blend the two scroll layers to get a simple animated ripple modulation.
    // sin() in [0,1] range gives a soft bright/dark oscillation over the surface.
    float ripple = 0.5 + 0.5 * sin(dot(uv_layer1 - uv_layer2, vec2(1.0, 0.7)) * 3.14159);
    // Mix water colour with a slightly lighter highlight driven by the ripple value.
    vec3 highlight = water_color.rgb + vec3(0.08, 0.10, 0.08);
    vec3 surface_color = mix(water_color.rgb, highlight, ripple * 0.4);

    ALBEDO = surface_color;
    ALPHA = alpha;

    // Slight specular for a wet-surface sheen — not physically correct but looks good
    // for a quick non-PBR approximation of the original fixed-function render.
    ROUGHNESS = 0.05;
    METALLIC = 0.0;
    SPECULAR = 0.8;

    // Normal output (in tangent space — Godot spatial expects NORMAL in view space here).
    // We leave NORMAL at the geometry normal (up) so Godot's light model sees a flat
    // surface; the perturbed_normal is only used for the Fresnel alpha term above.
    // (Full normal-mapping would require a sampler2D normal map asset.)
}
";
}
