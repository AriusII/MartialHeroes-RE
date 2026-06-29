// World/Terrain.gdshader
//
// Faithful Godot 4 spatial terrain ground shader.
//
// spec: Docs/RE/specs/terrain-streaming.md §11.4 / §11.7 — the world terrain pass draws each ground
//   subtile LIT (fixed-function LIGHTING ON, all-zero D3DMATERIAL9 so per-vertex diffuse supplies
//   reflectance) with stage-0 COLOROP = MODULATE2X: out.rgb = texture.rgb * diffuse.rgb * 2.
// spec: Docs/RE/formats/terrain.md §5.8 — the diffuse byte is HALVED at load (output = stored * 0.5),
//   so the runtime MODULATE2X x2 restores brightness headroom: with the default white vertex the net
//   is the unmodulated texture, and the scene directional + ambient light then shade it.
// spec: Docs/RE/specs/terrain-streaming.md §11.1 — ground uses texture address WRAP (repeat_enable).
//
// The swapped/halved/normalized diffuse arrives via the per-vertex COLOR (built in TerrainNode), so
// this shader keeps Godot's VertexColorUseAsAlbedo semantics: ALBEDO = texture * COLOR * 2.

shader_type spatial;

render_mode cull_disabled;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_texture = false;

// uv_scroll stays (0,0) = static (bgtexture KIND_STATIC, the default). A future KIND_SCROLL ground
// tile can drive a per-material scroll here once the legacy scroll vector is confirmed.
// spec: Docs/RE/formats/bgtexture_lst.md §Enumerations — KIND byte; scroll vector INFERRED, not confirmed.
uniform vec2 uv_scroll = vec2(0.0, 0.0);

void fragment() {
    vec2 uv = UV + uv_scroll * TIME;
    vec3 tex = use_texture ? texture(albedo_tex, uv).rgb : vec3(1.0);
    ALBEDO = tex * COLOR.rgb * 2.0;
}
