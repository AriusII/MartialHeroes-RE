using Godot;

namespace MartialHeroes.Client.Godot.World;

internal readonly record struct WaterPlacement(bool Enabled, float WorldY);

public sealed partial class WaterRenderer : Node3D
{
    public const float FallbackWaterY = 0f;

    private static readonly StringName TimeParam = "time";

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

    private Vector3 _centre = Vector3.Zero;
    private bool _configured;
    private ShaderMaterial? _mat;
    private MeshInstance3D? _meshInst;
    private float _size = 3072f;
    private float _time;
    private float _waterHeightY = FallbackWaterY;

    public Color WaterColor { get; set; } = new(0.10f, 0.35f, 0.65f, 0.65f);
    public float ScrollSpeedU1 { get; set; } = 0.04f;
    public float ScrollSpeedU2 { get; set; } = 0.025f;
    public float FresnelExponent { get; set; } = 3.0f;

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
        _mat.SetShaderParameter(TimeParam, _time);
    }

    public void Configure(Vector3 centre, float size, float waterHeightY)
    {
        _centre = centre;
        _size = size;
        _waterHeightY = waterHeightY;
        _configured = true;

        if (IsInsideTree())
            ApplyPlacement();
    }

    private void BuildWaterPlane()
    {
        var planeMesh = new PlaneMesh
        {
            Size = new Vector2(_size, _size),
            SubdivideDepth = 32,
            SubdivideWidth = 32
        };

        var shader = new Shader { Code = WaterShaderSource };
        _mat = new ShaderMaterial { Shader = shader };
        _mat.SetShaderParameter("water_color", WaterColor);
        _mat.SetShaderParameter("scroll_speed_1", ScrollSpeedU1);
        _mat.SetShaderParameter("scroll_speed_2", ScrollSpeedU2);
        _mat.SetShaderParameter("fresnel_exp", FresnelExponent);
        _mat.SetShaderParameter("time", 0.0f);
        planeMesh.Material = _mat;

        _meshInst = new MeshInstance3D
        {
            Mesh = planeMesh,
            Name = "WaterPlaneMesh",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_meshInst);

        GD.Print($"[WaterRenderer] Water plane built (size={_size:F0} units, 32x32 subdivisions). " +
                 "Node is oracle-gated: WireEnvironmentAndWater only spawns this node when water_oracle.cfg supplies an entry for the current area+cell.");
    }

    private void ApplyPlacement()
    {
        Position = new Vector3(_centre.X, _waterHeightY, _centre.Z);

        if (_meshInst?.Mesh is PlaneMesh plane)
            plane.Size = new Vector2(_size, _size);

        GD.Print($"[WaterRenderer] Placed at ({Position.X:F0}, {Position.Y:F1}, {Position.Z:F0}), size={_size:F0}.");
    }
}
