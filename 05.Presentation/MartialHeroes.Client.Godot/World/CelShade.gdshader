shader_type spatial;

render_mode unshaded, cull_disabled, depth_draw_always;

uniform bool cel_enabled = false;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap;
uniform vec4 albedo_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform bool use_albedo_texture = false;

uniform sampler2D toon_ramp : filter_linear, repeat_disable;
uniform bool use_toon_ramp = false;

uniform vec3 light_dir = vec3(-1.0, 0.0, 0.0);

uniform vec4 light_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

uniform vec3 state_multi = vec3(0.525, 0.525, 0.525);
uniform vec4 state_add   = vec4(0.0, 0.0, 0.0, 1.0);

uniform bool stealth = false;

varying vec3 v_normal_world;

void vertex() {
    v_normal_world = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec4 base;
    if (use_albedo_texture) {
        base = texture(albedo_texture, UV) * albedo_color;
    } else {
        base = albedo_color;
    }

    vec3 n = normalize(v_normal_world);
    float ndl = max(dot(n, normalize(-light_dir)), 0.0);
    vec3 diffuse = ndl * light_color.rgb;

    const vec3 BT601 = vec3(0.299, 0.587, 0.114);
    float luminance = clamp(dot(diffuse, BT601), 0.0, 1.0);

    vec3 tone;
    if (use_toon_ramp) {
        tone = texture(toon_ramp, vec2(luminance, 0.5)).rgb;
    } else {
        tone = vec3(luminance);
    }

    vec3 lit_rgb = (base.rgb * tone) * 2.0 * state_multi + state_add.rgb;

    float lit_alpha = stealth ? state_add.w : base.a;

    ALBEDO = clamp(cel_enabled ? lit_rgb : base.rgb, vec3(0.0), vec3(1.0));
    ALPHA  = lit_alpha;
}
