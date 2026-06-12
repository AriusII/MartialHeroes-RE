// World/WaterRenderer.cs
//
// PASSIVE Node3D that renders a semi-transparent, animated water plane over a map cell that
// contains water.  The orchestrator (RealWorldRenderer / GameLoop) must call CellHasWater before
// adding this node to the scene — only add it when CellHasWater returns true.
//
// Water detection (R5-confirmed approach):
//   Water is not a dedicated WATER section in the .map file.  It is identified by the FX
//   section's TEXTURES list: if any TexId in any FX{1-7} section resolves (via bgtexture.txt)
//   to a relative path whose name contains "_water", "_sea", or "_wateredge", the cell has water.
//   spec: R5 recon — "water is an FX overlay identified by texture name". 2026-06-12.
//   VFS-confirmed water texture rel-paths (from bgtexture.txt probe 2026-06-12):
//     TexId 313: terrain/_water02-1
//     TexId 315: terrain/_water01
//     TexId 316: terrain/_water02
//     TexId 827: terrain/_water_new01
//     TexId 828: terrain/_water_new02
//     TexId 829: terrain/_water_new03
//     TexId 830: terrain/_water_new04
//     TexId 848: terrain/_sea
//     TexId 859: terrain/_wateredge
//     TexId 860: terrain/_water01_1
//
//   CellHasWater(MapDescriptor) does the check on the parsed .map.  Because MapDescriptorParser
//   only captures integer TexIds (not the artist string path), the caller must pass the bgtexture
//   catalog as well — see the three-argument overload.  The one-argument overload is a
//   convenient approximation that checks whether any FX section has ANY non-empty TEXTURES
//   (conservative: may produce false positives); use the three-argument overload for precision.
//
// Water surface Y determination:
//   The legacy .map file does not store an explicit water-plane height.
//   TERRAIN section MIN_HEIGHTFILED is the minimum terrain vertex world-Y in the cell.
//   spec: terrain.md §3.4 — MIN_HEIGHTFILED float: CONFIRMED.
//   The helper WaterSurfaceY(MapDescriptor) returns:
//     - MIN_HEIGHTFILED + WaterOffsetAboveFloor  (when TERRAIN MinHeightFiled is present)
//     - FallbackWaterY                           (otherwise)
//   where WaterOffsetAboveFloor = 2.0f (the water plane sits ~2 world-units above the lowest
//   vertex — just above the seafloor so the plane covers coastal areas without floating high).
//
// Rendering approach: a PlaneMesh with an inline ShaderMaterial scrolling two UV layers.
// Threading: Configure() may be called before the node is in the scene tree;
//   _Process and all Godot mutations run on the main thread.
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive.
// spec: terrain.md §3.4 — MIN_HEIGHTFILED float: CONFIRMED.
// spec: terrain.md §1.4 — cell size 1024 world units: CONFIRMED.
// spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z): CONFIRMED.

using Godot;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
/// Passive <see cref="Node3D"/> that places a semi-transparent animated water plane in the scene.
///
/// IMPORTANT: only add to the scene when <see cref="CellHasWater(MapDescriptor, BgTextureCatalog)"/>
/// (or its bgtexture-aware overload) returns <see langword="true"/>.
///
/// Call <see cref="Configure"/> to position the plane. <see cref="WaterSurfaceY"/> is a
/// convenience helper for computing the Y value from the cell's parsed <see cref="MapDescriptor"/>.
/// </summary>
public sealed partial class WaterRenderer : Node3D
{
    // -------------------------------------------------------------------------
    // Water-presence constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// Vertical offset above the terrain MIN_HEIGHTFILED at which the water plane is placed.
    /// Ensures the plane sits just above the seafloor / shoreline vertices.
    /// spec: heuristic — "waterY = MIN_HEIGHTFILED + 2.0f" (mission brief, 2026-06-12).
    /// </summary>
    public const float WaterOffsetAboveFloor = 2.0f;

    /// <summary>
    /// Fallback water-plane Y when the .map TERRAIN section has no MIN_HEIGHTFILED.
    /// Sea-level / coastal default.
    /// </summary>
    public const float FallbackWaterY = 0f;

    // -------------------------------------------------------------------------
    // Water-presence detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> if the cell described by <paramref name="cellMap"/> has
    /// water, using the bgtexture catalog to resolve TexIds to artist paths.
    ///
    /// Strategy: enumerate every FX{1-7} section; for each TEXTURES entry, resolve
    /// <c>TexId</c> via <paramref name="bgTextures"/> to a relative path; check whether the
    /// (lowercased) path contains "_water", "_sea", or "_wateredge".
    ///
    /// spec: R5 recon — "water is an FX overlay identified by texture name". 2026-06-12.
    /// spec: bgtexture.txt VFS probe 2026-06-12 — confirmed water rel-path substrings:
    ///   terrain/_water*, terrain/_sea, terrain/_wateredge.
    /// </summary>
    /// <param name="cellMap">Parsed .map descriptor for the target cell.</param>
    /// <param name="bgTextures">Parsed bgtexture.txt catalog (global; use map000 pool).</param>
    public static bool CellHasWater(MapDescriptor cellMap, BgTextureCatalog bgTextures)
    {
        if (cellMap is null || bgTextures is null) return false;

        foreach (MapSection sec in cellMap.Sections)
        {
            // Only FX sections carry water overlays.
            // spec: MapDescriptorParser — FX keywords: FX1..FX7. CONFIRMED.
            if (!sec.Keyword.StartsWith("FX", StringComparison.OrdinalIgnoreCase)) continue;

            foreach ((int _, int texId) in sec.Textures)
            {
                string? rel = bgTextures.GetRelPath(texId);
                if (rel is null) continue;

                string lower = rel.ToLowerInvariant();
                // spec: VFS probe 2026-06-12 — water rel-paths contain these substrings.
                if (lower.Contains("_water") || lower.Contains("_sea") || lower.Contains("_wateredge"))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Conservative overload that returns <see langword="true"/> if any FX section in
    /// <paramref name="cellMap"/> has a non-empty TEXTURES list.
    ///
    /// Use this when you do not have the bgtexture catalog available. May produce false
    /// positives (FX sections can carry non-water overlays), but is never a false negative
    /// for well-formed .map files where water FX always appear in FX sections.
    ///
    /// spec: MapDescriptorParser — FX keywords: FX1..FX7. CONFIRMED.
    /// </summary>
    public static bool CellHasWater(MapDescriptor cellMap)
    {
        if (cellMap is null) return false;

        foreach (MapSection sec in cellMap.Sections)
        {
            if (!sec.Keyword.StartsWith("FX", StringComparison.OrdinalIgnoreCase)) continue;
            if (sec.Textures.Length > 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Derives the water surface Y position from the cell's .map descriptor.
    ///
    /// Returns <c>TERRAIN.MinHeightFiled + <see cref="WaterOffsetAboveFloor"/></c> when the
    /// TERRAIN section has a MIN_HEIGHTFILED value; otherwise returns <see cref="FallbackWaterY"/>.
    ///
    /// spec: terrain.md §3.4 — MIN_HEIGHTFILED float: CONFIRMED.
    /// spec: heuristic — "waterY = MIN_HEIGHTFILED + 2.0f" (mission brief, 2026-06-12).
    /// </summary>
    public static float WaterSurfaceY(MapDescriptor cellMap)
    {
        if (cellMap is null) return FallbackWaterY;

        foreach (MapSection sec in cellMap.Sections)
        {
            if (sec.Keyword.Equals("TERRAIN", StringComparison.OrdinalIgnoreCase) &&
                sec.MinHeightFiled.HasValue)
            {
                return sec.MinHeightFiled.Value + WaterOffsetAboveFloor;
            }
        }

        return FallbackWaterY;
    }

    // -------------------------------------------------------------------------
    // Public configuration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Water surface colour and base opacity.
    /// Alpha = max transparency before Fresnel modulation. 0.65 gives a visible translucent surface
    /// at a top-down MMORPG camera angle.
    /// </summary>
    public Color WaterColor { get; set; } = new Color(0.10f, 0.35f, 0.65f, 0.65f);

    /// <summary>
    /// Speed of primary UV scroll layer (UV1). ~0.04 u/s matches the legacy 50 ms animation gate.
    /// spec: Docs/RE/specs/client_runtime.md §"Terrain water animation gate". CONFIRMED.
    /// </summary>
    public float ScrollSpeedU1 { get; set; } = 0.04f;

    /// <summary>Speed of secondary UV scroll layer — slightly slower for interference ripple effect.</summary>
    public float ScrollSpeedU2 { get; set; } = 0.025f;

    /// <summary>Fresnel exponent (edge glow/transparency falloff). 3.0 is a neutral value for flat water.</summary>
    public float FresnelExponent { get; set; } = 3.0f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private MeshInstance3D? _meshInst;
    private ShaderMaterial? _mat;
    private float _time;

    // Deferred Configure() state (applied in _Ready if called before entering the tree).
    private Vector3 _centre      = Vector3.Zero;
    private float   _size        = 3072f; // default: 3×3 cell ring (3 × 1024 world units)
    private float   _waterHeightY = FallbackWaterY;
    private bool    _configured;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        BuildWaterPlane();
        if (_configured)
            ApplyPlacement();
    }

    public override void _Process(double delta)
    {
        if (_mat is null) return;
        _time += (float)delta;
        _mat.SetShaderParameter("time", _time);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Positions and sizes the water plane. May be called before or after <see cref="Node._Ready"/>.
    ///
    /// Typical orchestrator call (inside RealWorldRenderer, only when <see cref="CellHasWater"/> is true):
    /// <code>
    ///   if (WaterRenderer.CellHasWater(_cellMap, _bgTextures))
    ///   {
    ///       float waterY  = WaterRenderer.WaterSurfaceY(_cellMap);
    ///       float legacyX = (TargetMapX - 10000) * 1024f + 512f;
    ///       float legacyZ = (TargetMapZ - 10000) * 1024f + 512f;
    ///       // spec: WorldCoordinates.ToGodot — negate Z.
    ///       var centre = new Vector3(legacyX, waterY, -legacyZ);
    ///       var water  = new WaterRenderer { Name = "WaterRenderer" };
    ///       AddChild(water);
    ///       water.Configure(centre, 3072f, waterY);
    ///   }
    /// </code>
    ///
    /// <param name="centre">Godot-space XYZ centre (X/Z from cell coords; Y overridden by waterHeightY).</param>
    /// <param name="size">Edge length of the water square in legacy world units (1 cell = 1024; 3-cell ring = 3072).</param>
    /// <param name="waterHeightY">Y of the water surface. Use <see cref="WaterSurfaceY"/> for .map-derived value.</param>
    ///
    /// spec: terrain.md §1.4 — cell size 1024 world units: CONFIRMED.
    /// spec: WorldCoordinates.ToGodot — (x,y,z) → (x,y,-z): CONFIRMED.
    /// </summary>
    public void Configure(Vector3 centre, float size, float waterHeightY)
    {
        _centre       = centre;
        _size         = size;
        _waterHeightY = waterHeightY;
        _configured   = true;

        if (IsInsideTree())
            ApplyPlacement();
    }

    // -------------------------------------------------------------------------
    // Mesh + material construction
    // -------------------------------------------------------------------------

    private void BuildWaterPlane()
    {
        // PlaneMesh in XZ plane (Y=0 local), 32×32 subdivisions for Fresnel smoothness.
        // One sub-quad per ~96 world units at the default 3072-unit extent.
        var planeMesh = new PlaneMesh
        {
            Size           = new Vector2(_size, _size),
            SubdivideDepth = 32,
            SubdivideWidth = 32,
        };

        var shader = new Shader { Code = WaterShaderSource };
        _mat = new ShaderMaterial { Shader = shader };
        _mat.SetShaderParameter("water_color",    WaterColor);
        _mat.SetShaderParameter("scroll_speed_1", ScrollSpeedU1);
        _mat.SetShaderParameter("scroll_speed_2", ScrollSpeedU2);
        _mat.SetShaderParameter("fresnel_exp",    FresnelExponent);
        _mat.SetShaderParameter("time",           0.0f);
        planeMesh.Material = _mat;

        _meshInst = new MeshInstance3D
        {
            Mesh        = planeMesh,
            Name        = "WaterPlaneMesh",
            CastShadow  = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_meshInst);

        GD.Print($"[WaterRenderer] Water plane built (default size={_size:F0} units, 32×32 subdivisions).");
    }

    private void ApplyPlacement()
    {
        // spec: WorldCoordinates.ToGodot — only Z is negated; Y is unchanged.
        Position = new Vector3(_centre.X, _waterHeightY, _centre.Z);

        if (_meshInst?.Mesh is PlaneMesh plane)
            plane.Size = new Vector2(_size, _size);

        GD.Print($"[WaterRenderer] Placed at ({Position.X:F0}, {Position.Y:F1}, {Position.Z:F0}), size={_size:F0}.");
    }

    // -------------------------------------------------------------------------
    // Shader source
    // -------------------------------------------------------------------------

    // Animated water plane shader: two UV layers scroll at different speeds to produce a
    // ripple-like interference pattern. Fresnel blend modulates alpha by view angle.
    //
    // Legacy reference: original client used INVSRCALPHA dest-blend with animated UV multi-texture
    // passes for water. spec: client_runtime.md §"Terrain water animation gate". CONFIRMED.
    //
    // render_mode blend_mix = standard SRC_ALPHA / ONE_MINUS_SRC_ALPHA (matches INVSRCALPHA).
    // cull_disabled = visible from both sides (camera may dip below water surface).
    private const string WaterShaderSource = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled;

uniform vec4  water_color    : source_color = vec4(0.10, 0.35, 0.65, 0.65);
uniform float scroll_speed_1 : hint_range(0.0, 1.0) = 0.04;
uniform float scroll_speed_2 : hint_range(0.0, 1.0) = 0.025;
uniform float fresnel_exp    : hint_range(0.5, 8.0) = 3.0;
uniform float time = 0.0;

varying vec2 uv_layer1;
varying vec2 uv_layer2;

void vertex() {
    vec2 base_uv = UV * 8.0;
    uv_layer1 = base_uv + vec2( time * scroll_speed_1, time * scroll_speed_1 * 0.4);
    uv_layer2 = base_uv + vec2(-time * scroll_speed_2, time * scroll_speed_2 * 0.7);
}

void fragment() {
    vec2 delta = sin(uv_layer1 * 6.2831) - sin(uv_layer2 * 6.2831);
    vec3 perturbed_normal = normalize(vec3(delta.x * 0.12, 1.0, delta.y * 0.12));

    float vdotn  = clamp(dot(normalize(VIEW), perturbed_normal), 0.0, 1.0);
    float fresnel = 1.0 - pow(vdotn, fresnel_exp);
    float alpha   = water_color.a * mix(0.40, 1.0, fresnel);

    float ripple = 0.5 + 0.5 * sin(dot(uv_layer1 - uv_layer2, vec2(1.0, 0.7)) * 3.14159);
    vec3 highlight    = water_color.rgb + vec3(0.08, 0.10, 0.08);
    vec3 surface_color = mix(water_color.rgb, highlight, ripple * 0.4);

    ALBEDO    = surface_color;
    ALPHA     = alpha;
    ROUGHNESS = 0.05;
    METALLIC  = 0.0;
    SPECULAR  = 0.8;
}
";
}
