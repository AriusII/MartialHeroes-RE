shader_type spatial;

render_mode cull_disabled;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_texture = false;
uniform vec2 uv_scroll = vec2(0.0, 0.0);

void fragment() {
    if (!FRONT_FACING) discard;
    vec2 uv = UV + uv_scroll * TIME;
    vec3 tex = use_texture ? texture(albedo_tex, uv).rgb : vec3(1.0);
    ALBEDO = tex * COLOR.rgb * 2.0;
    ROUGHNESS = 0.9;
}
