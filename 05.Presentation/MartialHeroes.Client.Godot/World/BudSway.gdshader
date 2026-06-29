// World/BudSway.gdshader
//
// Faithful Godot 4 spatial shader for static .bud objects (buildings / props / trees / foliage).
//
// spec: Docs/RE/formats/terrain_scene.md §5 + Addendum A1.3 — every static-object colour pass draws
//   with ALPHABLENDENABLE=1 (SRCALPHA/INVSRCALPHA) and ALPHATESTENABLE=0: foliage cutout comes from
//   the texture's own alpha via BLEND, never alpha test. -> render_mode blend_mix; ALPHA = texture.a.
// spec: Docs/RE/formats/terrain_scene.md Addendum A1.3 / A4 — stage-0 COLOROP = MODULATE2X
//   (texture * diffuse * 2). With dynamic lighting: ALBEDO = texture.rgb * 2 then the scene light
//   shades it -> ~ saturate(2 * texture * litDiffuse).
// spec: Docs/RE/formats/terrain_scene.md §5 / A4 — SMALL/MEDIUM foliage (kind 0x0A..0x0E) sway is a
//   per-vertex HORIZONTAL (X/Z only, Y fixed) displacement, driven by a single global ping-pong clock
//   shared by all foliage objects. bud_sway_phase is that shared normalized triangle-wave clock
//   (CPU-fed each frame). Per-vertex bend weight rides in COLOR.r (built on the CPU per the §3/A4
//   bent-vertex set); per-object amplitude + horizontal direction arrive as uniforms.
//
// LARGE objects (kind 0x14..0x18) are a DISTINCT, IDA-confirmed path: a RIGID WHOLE-OBJECT translation
//   of EVERY vertex (the only bucket whose Y/height is displaced) along the object's vertex-0 normal
//   (sway_dir3, already negate-Z'd on the CPU to match the position convention), with fixed per-axis
//   ratios x*1.0 / y*0.7 / z*1.5. No COLOR.r bend, no secondary ratio. is_large selects this branch.
//
// Non-sway buckets pass sway_amp = 0 -> both branches are no-ops.
//
// Culling is per render bucket: static/else bucket is one-sided (one_sided = true -> back faces
// discarded, equivalent to D3DCULL_CW front=CCW); kind==2 and both sway buckets are two-sided.

shader_type spatial;

render_mode blend_mix, cull_disabled, depth_draw_opaque;

global uniform float bud_sway_phase;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_texture = true;
uniform bool one_sided = false;
uniform float sway_amp = 0.0;
uniform vec2 sway_dir = vec2(0.0, 0.0);
uniform bool is_large = false;
uniform vec3 sway_dir3 = vec3(0.0, 0.0, 0.0);

void vertex() {
    if (is_large) {
        // Rigid whole-object lean along the vertex-0 normal; per-axis ratios x/y/z = 1.0/0.7/1.5.
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
    ALPHA = c.a;
    ROUGHNESS = 0.9;
}
