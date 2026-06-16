// World/CelShade.gdshader
//
// Faithful Godot 4.6 spatial shader reproducing the legacy "dotoonshading" cel look.
//
// SCOPE: skinned characters ONLY.
// spec: Docs/RE/specs/rendering.md §5.2 — the cel/dotoonshading vertex path is the SKINNED-CHARACTER
//   pipeline (stride-32 XYZ/NORMAL/TEXCOORD0/TEXCOORD1_N·L). Terrain, buildings, and static meshes
//   draw through a vertex-color-lit opaque bucket (§3.2) that does NOT use dotoonshading — they are
//   NOT cel-shaded.
//
// ALGORITHM (from spec):
//   1. Two-light Lambert: for each light, dp3(normal, lightDir), clamp [0,1], accumulate diffuse.
//      spec: Docs/RE/formats/shaders.md — dotoonshading.vsh semantics.
//   2. BT.601 luminance projection: luminance = dot(diffuse_rgb, c9)
//      where c9 = [0.299, 0.587, 0.114, 1.0] (ITU-R BT.601 luma coefficients, HIGH confidence).
//      spec: Docs/RE/formats/shaders.md §C5.4 register c9 — "Luminance weights — exactly the
//      ITU-R BT.601 luma coefficients (R, G, B)."
//   3. Toon ramp lookup: sample a 1-D ramp texture (toonramp.bmp, stage 1) at UV (luminance, 0.5).
//      spec: Docs/RE/formats/shaders.md §C5.3 — "1-D lookup into the ramp by the interpolated
//      N·L luminance." The light-step quantisation lives entirely in the ramp file, NOT in code.
//   4. Output: albedo × ramp_tone (multiply albedo by the quantised tone from the ramp).
//
// FALLBACK ramp (VFS absent): a procedural 3-step ramp is generated in the shader via smoothstep.
//   This is an aesthetic engineering choice; when toonramp.bmp is loaded via CelShadeMaterialFactory
//   the ramp sampler overrides it. Declared as aesthetic, not spec-dictated.
//
// spec: Docs/RE/formats/shaders.md §C5.4 — BT.601 c9 constant [0.299, 0.587, 0.114, 1.0] HIGH.
// spec: Docs/RE/specs/rendering.md §5.2 — stride-32 cel skinned-character vertex declaration.
// spec: Docs/RE/formats/shaders.md §C5.3 — toon ramp LUT on texture stage 1.

shader_type spatial;

// Disable Godot's built-in lighting model — we implement our own N·L → ramp tone.
render_mode unshaded, cull_disabled, depth_draw_always;

// ---- Uniforms ----

// Albedo texture (character skin, bound by CelShadeMaterialFactory from skin.txt chain).
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap;
uniform vec4 albedo_color : source_color = vec4(0.85, 0.75, 0.65, 1.0);
uniform bool use_albedo_texture = false;

// Toon ramp LUT — 1-D ramp, sampled at (luminance, 0.5).
// spec: Docs/RE/formats/shaders.md §C5.3 — toonramp.bmp, stage 1, 1-D N·L cel ramp.
uniform sampler2D toon_ramp : filter_linear, repeat_disable;
uniform bool use_toon_ramp = false;

// Primary toon light direction (world space, pointing FROM surface TO light, normalized).
// Corresponds to c4 = [-1, 0, 0, 0]: default toon light pointing along the −X world axis.
// spec: Docs/RE/formats/shaders.md §C5.4 — "c4: default [-1, 0, 0, 0] — toon light direction."
// NOTE: this is the TOON light (uploaded each frame from the renderer slot); it is DISTINCT from
// the scene DirectionalLight3D (which models the ambient sun). The toon light controls the N·L
// shading that feeds the BT.601 ramp lookup; the scene light is for shadow casting only.
// Default: -X axis (c4 default); editable via CelShadeMaterialFactory.SetToonLightDir.
uniform vec3 light_dir = vec3(-1.0, 0.0, 0.0); // spec: Docs/RE/formats/shaders.md §C5.4 c4 = [-1,0,0,0]

// Second light direction (the fixed view-Z axis c5 = [0,0,-1,0], negated for an ambient fill).
// spec: Docs/RE/formats/shaders.md §C5.4 c5 = [0, 0, -1, 0].
// In world space as a fill light contribution, aesthetic choice for second-light intensity.
uniform vec3 light2_dir = vec3(0.0, 1.0, 0.0);

// Per-light colours (c6 = [1,1,1,1] white main, second aesthetic warm fill).
// spec: Docs/RE/formats/shaders.md §C5.4 c6 = [1, 1, 1, 1].
uniform vec4 light_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 light2_color : source_color = vec4(0.5, 0.5, 0.6, 1.0);

// ---- Vertex ----

varying vec3 v_normal_world;

void vertex() {
    // Normal in world space for per-pixel N·L.
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

    // ---- Two-light Lambert accumulation ----
    // spec: Docs/RE/formats/shaders.md — dotoonshading.vsh: "Two-light Lambert accumulation:
    //   for each light, dp3(normal, lightDir), clamp to [0, 1] with max, modulate by light colour,
    //   accumulate."
    vec3 n = normalize(v_normal_world);

    float ndl1 = max(dot(n, normalize(light_dir)), 0.0);
    float ndl2 = max(dot(n, normalize(light2_dir)), 0.0);

    vec3 diffuse1 = ndl1 * light_color.rgb;
    vec3 diffuse2 = ndl2 * light2_color.rgb;
    vec3 diffuse_total = diffuse1 + diffuse2;

    // ---- BT.601 luminance projection ----
    // spec: Docs/RE/formats/shaders.md §C5.4 c9 = [0.299, 0.587, 0.114, 1.0]
    //   "dp3 oT1.xyz, r1, c9 — dot the accumulated diffuse colour against a luminance-weight
    //    vector (in c9) to produce a scalar luminance value; write to the x-component of output
    //    texcoord 1. This value indexes the 1D toon LUT texture."
    // ITU-R BT.601 luma coefficients (R, G, B).
    // spec: Docs/RE/formats/shaders.md §C5.4 — HIGH confidence, decoded from IEEE-754 literals.
    const vec3 BT601 = vec3(0.299, 0.587, 0.114);
    float luminance = clamp(dot(diffuse_total, BT601), 0.0, 1.0);

    // ---- Toon ramp lookup ----
    // spec: Docs/RE/formats/shaders.md §C5.3 — "1-D lookup into the ramp by the interpolated
    //   N·L luminance." The ramp file converts smooth 0..1 into hard tone bands (the stepped look).
    //   "The light-step lives in the ramp file, not in code."
    vec3 tone;
    if (use_toon_ramp) {
        // Sample the 1-D ramp at (luminance, 0.5) — same as a 1-D texture on stage 1 keyed by lum.
        tone = texture(toon_ramp, vec2(luminance, 0.5)).rgb;
    } else {
        // FALLBACK procedural ramp (3 bands): dark shadow / mid tone / bright lit.
        // Aesthetic choice: 3 stepped bands approximating a typical toon ramp.
        // Not spec-dictated — the spec says the quantisation lives in toonramp.bmp.
        float band = smoothstep(0.05, 0.15, luminance) * 0.35
                   + smoothstep(0.35, 0.45, luminance) * 0.35
                   + smoothstep(0.65, 0.75, luminance) * 0.30;
        band = clamp(band + 0.10, 0.0, 1.0); // floor ambient so shadow areas aren't pure black
        tone = vec3(band);
    }

    // ---- Composite: albedo × tone ----
    ALBEDO = base.rgb * tone;
    ALPHA  = base.a;
}
