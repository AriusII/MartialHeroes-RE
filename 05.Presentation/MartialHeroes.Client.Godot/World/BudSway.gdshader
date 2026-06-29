shader_type spatial;

render_mode blend_mix, cull_disabled, depth_draw_opaque;

global uniform float bud_sway_phase;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_texture = true;
uniform bool one_sided = false;
uniform bool is_opaque = false;
uniform float sway_amp = 0.0;
uniform vec2 sway_dir = vec2(0.0, 0.0);
uniform bool is_large = false;
uniform vec3 sway_dir3 = vec3(0.0, 0.0, 0.0);

void vertex() {
    if (is_large) {
        VERTEX += sway_dir3 * (sway_amp * bud_sway_phase) * vec3(1.0, 0.7, 1.5);
    } else {
        float bend = COLOR.r;
        float disp = sway_amp * bud_sway_phase * bend;
        VERTEX.x += sway_dir.x * disp;
        VERTEX.z += sway_dir.y * disp;
    }
}

void fragment() {
    if (one_sided && !FRONT_FACING) {
        discard;
    }
    vec4 c = has_texture ? texture(albedo_tex, UV) : vec4(1.0);
    ALBEDO = c.rgb * 2.0;
    ALPHA = is_opaque ? 1.0 : c.a;
    ROUGHNESS = 0.9;
}
