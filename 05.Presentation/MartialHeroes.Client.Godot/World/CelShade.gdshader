// World/CelShade.gdshader
//
// Faithful Godot 4.6 spatial shader reproducing the legacy "dotoonshading" cel look.
//
// SCOPE: skinned characters ONLY.
// spec: Docs/RE/specs/rendering.md §5.2 — the cel/dotoonshading vertex path is the SKINNED-CHARACTER
//   pipeline (stride-32 XYZ/NORMAL/TEXCOORD0/TEXCOORD1_N·L). Terrain, buildings, and static meshes
//   draw through a vertex-color-lit opaque bucket (§3.2) that does NOT use dotoonshading.
//
// ALGORITHM (IDA-confirmed, spec: Docs/RE/formats/shaders.md §C5 / Re-authoring Guidance):
//   dotoonshading.psh  (normal):  out.rgb = (base × toonRamp) × 2 × c0 + c1 ; out.a = lit.a
//   dotoonshading2.psh (stealth): identical RGB, out.a = c1.w
//   VS uses a SINGLE light; light dir is NEGATED (c4 default [-1,0,0,0]); c7=[0,0,0,0] so light2
//   is DEAD. BT.601 luma via c9=[0.299,0.587,0.114,1.0].
//   Per-state c0=MULTI rgb, c1=ADD rgb[.w] from data/script/display.lua DISPLAY_CHAR_BRIGHT_* (9 states).
//   spec: Docs/RE/formats/shaders.md §C5.4 c4=[-1,0,0,0], c7=[0,0,0,0], c9=[0.299,0.587,0.114,1.0]
//   spec: Docs/RE/formats/shaders.md §C5.5 per-state brightness/tint system; state indices 0..8.
//
// FALLBACK ramp (VFS absent): procedural 3-step ramp via smoothstep.
//   Aesthetic choice; overridden when toonramp.bmp loads via CelShadeMaterialFactory.

shader_type spatial;

render_mode unshaded, cull_disabled, depth_draw_always;

// ---- Uniforms ----

// spec: Docs/RE/specs/rendering.md §5.1a — cel gated on offscreen post-process flag.
uniform bool cel_enabled = true;

// Albedo texture (character skin, bound from skin.txt chain).
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap;
uniform vec4 albedo_color : source_color = vec4(0.85, 0.75, 0.65, 1.0);
uniform bool use_albedo_texture = false;

// Toon ramp LUT — 1-D ramp, sampled at (luminance, 0.5).
// spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp, stage 1, 1-D N·L cel ramp.
uniform sampler2D toon_ramp : filter_linear, repeat_disable;
uniform bool use_toon_ramp = false;

// Single toon light direction (world space, FROM surface TO light, normalized).
// VS negates the incoming c4 for the dp3 — so light_dir here is the pre-negation direction.
// spec: Docs/RE/formats/shaders.md §C5.4 c4 default [-1,0,0,0]; light dir is NEGATED in the VS.
uniform vec3 light_dir = vec3(-1.0, 0.0, 0.0);

// spec: Docs/RE/formats/shaders.md §C5.4 c6=[1,1,1,1] white main light.
uniform vec4 light_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// Per-state brightness MULTI and ADD (c0 and c1 in dotoonshading.psh).
// spec: Docs/RE/formats/shaders.md §C5.5 — 9-state DISPLAY_CHAR_BRIGHT_* table from display.lua.
// state_add.w is used as alpha in the stealth (dotoonshading2.psh) variant.
// state_multi carries the HALVED c0 (display.lua MULTI × 0.5); the × 2.0 below is the
// MODULATE2X that restores the spec net (DEFAULT 0.65 × 2 = 1.3). Halving is done in
// CelShadeMaterialFactory so the shader matches the original dotoonshading.psh exactly.
uniform vec3 state_multi = vec3(0.65, 0.65, 0.65); // spec: display.lua DISPLAY_CHAR_BRIGHT_MULTI_*_DEFAULT × 0.5
uniform vec4 state_add   = vec4(0.0, 0.0, 0.0, 1.0); // spec: display.lua DISPLAY_CHAR_BRIGHT_ADD_*_DEFAULT + ALPHA

// Stealth flag: selects dotoonshading2.psh alpha path (out.a = c1.w = state_add.w).
// spec: Docs/RE/formats/shaders.md §C5.5, C5.1 — dotoonshading2.psh is the stealth variant.
uniform bool stealth = false;

// ---- Vertex ----

varying vec3 v_normal_world;

void vertex() {
    v_normal_world = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// ---- Fragment ----

void fragment() {
    // ---- Albedo ----
    vec4 base;
    if (use_albedo_texture) {
        base = texture(albedo_texture, UV) * albedo_color;
    } else {
        base = albedo_color;
    }

    // ---- Single-light Lambert ----
    // spec: Docs/RE/formats/shaders.md Re-authoring Guidance — dotoonshading.vsh single light,
    //   light dir negated (c4 default [-1,0,0,0]). c7=[0,0,0,0] so light2 is DEAD.
    vec3 n = normalize(v_normal_world);
    float ndl = max(dot(n, normalize(-light_dir)), 0.0);
    vec3 diffuse = ndl * light_color.rgb;

    // ---- BT.601 luminance projection ----
    // spec: Docs/RE/formats/shaders.md §C5.4 c9=[0.299,0.587,0.114,1.0] HIGH (bit-pattern confirmed).
    const vec3 BT601 = vec3(0.299, 0.587, 0.114);
    float luminance = clamp(dot(diffuse, BT601), 0.0, 1.0);

    // ---- Toon ramp lookup ----
    // spec: Docs/RE/formats/shaders.md §C5.3 — 1-D lookup into the ramp by interpolated N·L luminance.
    vec3 tone;
    if (use_toon_ramp) {
        tone = texture(toon_ramp, vec2(luminance, 0.5)).rgb;
    } else {
        // Fallback procedural ramp (3 bands). Aesthetic; not spec-dictated.
        float band = smoothstep(0.05, 0.15, luminance) * 0.35
                   + smoothstep(0.35, 0.45, luminance) * 0.35
                   + smoothstep(0.65, 0.75, luminance) * 0.30;
        band = clamp(band + 0.10, 0.0, 1.0);
        tone = vec3(band);
    }

    // ---- dotoonshading.psh arithmetic ----
    // spec: Docs/RE/formats/shaders.md Re-authoring Guidance:
    //   out.rgb = (base × toonRamp) × 2 × c0 + c1
    //   dotoonshading.psh:  out.a = lit.a  (base alpha passes through)
    //   dotoonshading2.psh: out.a = c1.w   (state_add.w drives stealth fade)
    // spec: Docs/RE/formats/shaders.md §C5 — the original PS output is exactly
    //   out.rgb = (base × toonRamp) × 2 × c0 + c1. There is NO additive ambient term; the
    //   per-state c0 (state_multi, halved) + c1 (state_add) are the only brightness inputs.
    //   The trailing clamp is the implicit D3D8 pixel-shader output saturation (not extra math).
    vec3 lit_rgb = (base.rgb * tone) * 2.0 * state_multi + state_add.rgb;

    float lit_alpha = stealth ? state_add.w : base.a;

    ALBEDO = clamp(cel_enabled ? lit_rgb : base.rgb, vec3(0.0), vec3(1.0));
    ALPHA  = lit_alpha;
}
