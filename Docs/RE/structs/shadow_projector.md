---
verification: static analysis, IDB SHA f61f66a9 (CYCLE deep-3d-wave3, 2026-06-28); no debugger; open items noted below
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false          # runtime singleton, not a packed-file format
subsystems: [render_pipeline, world_systems]
conflicts: none-open
---

# Structs: Shadow Projector — Layout and Render-Pass Spec

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets below are byte offsets relative to the start of the object** —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> The terrain shadow-map render chain is described in `specs/render_pipeline.md` and
> `formats/terrain.md §11.8`. Citing engineers: `// spec: Docs/RE/structs/shadow_projector.md`.
>
> **Confidence vocabulary (per campaign banner tiers):** `[confirmed]` = recovered from static
> control-flow + operand analysis, corroborated across multiple sites; `[static-hypothesis]` =
> structurally inferred or observed only as a zero-init, NOT independently re-isolated this pass
> (do not assume); `[debugger-pending]` = formula or role statically certain but live numeric value
> not confirmed against the running client. No item in this spec is debugger-confirmed (static-only
> pass); a live `?ext=dbg` read of the singleton would settle the remaining open items.

---

## Object identity — single non-polymorphic global

`ShadowProjector` is a **statically-allocated process-wide singleton** of **total size 316 bytes**.
It is **not polymorphic** — field `+0` is a `u8` enabled flag, not a vtable pointer. The only
vtables in play are carried by the Direct3D COM objects held at `+88` and `+96`.

The object has two distinct duties:

- **Shadow-map RT path.** Owns the render-to-surface wrapper (`+88`), the RT surface (`+92`), and
  the dynamic shadow-map texture (`+96`), together with the static light-perspective matrix
  (`+112`), the per-frame composed projection-texture matrix (`+176`), and the UV-bias matrix
  (`+240`). The RT texture at `+96` is the texture that the terrain stage-1 pass samples.
- **Blob-quad path.** A flat textured ground quad drawn per actor as a fallback/low-quality
  shadow. Its 4-vertex scratch (`+4`) and blob texture pointer (`+84`) live here; this mechanism
  is independent of the projected shadow-map.

---

## Lifetime and initialization

- **Storage:** one static global instance; no heap allocation.
- **Construction (lazy one-shot, gated by an adjacent flag):** zero/default-initialises early
  fields, presets the four blob-quad UV coordinates to unit-quad corners
  `(0,0) / (1,0) / (1,1) / (0,1)`, and builds the two static matrices (`+112` and `+240`). No
  vtable pointer is written; no Direct3D resource is created.
- **Resource initialisation (`ShadowManager_Init`):** creates the blob base texture
  (`data/effect/tex/shadow.dds`) into `+84` if absent; reads the quality/mode word from the
  graphics-quality config singleton into `+312`; sizes and creates the RT texture (`+96`), its
  surface level 0 (`+92`), and the render-to-surface wrapper (`+88`); sets `enabled_flag` (`+0`)
  to 1. When `mode_flag` (`+312`) equals 3 (shadows disabled) the RT creation is skipped entirely.
  Called from the boot data-table corpus loader and a renderer (re)init path.
- **Teardown (`ShadowProjector_ReleaseResources`):** COM-Releases `+92`, `+96`, `+88` in that
  order, nulls all three, clears `+0`, and resets `+312` to 3.
- **Per-frame use:** a render pre-step lazily fetches the singleton and calls the per-frame look-at
  builder (`Renderer_BuildShadowLookAtMatrix`). The blob-quad pass (`ActorShadow_DrawBlobQuads`) is
  invoked from the opaque-world render pass (`RenderPass_OpaqueWorld`).

---

## Field offset table

`this` = start of the singleton object. All offsets are **byte offsets within the object**, not
memory addresses.

| Byte offset | Size | Type     | Field                    | Notes / evidence | Confidence |
|------------:|-----:|----------|--------------------------|------------------|------------|
| +0          | 1    | u8       | `enabled_flag`           | 1 once the RT is built; 0 when disabled or after teardown. Both the per-frame and blob passes are gated on this being nonzero. | confirmed |
| +1          | 3    | pad      | *(alignment)*            | Padding to `f32` alignment. | confirmed |
| +4          | 80   | f32[20]  | `blob_quad_vertices`     | 4-vertex blob-quad scratch. FVF `0x102` (`D3DFVF_XYZ\|D3DFVF_TEX1`), stride 20 bytes per vertex: `{f32 x, f32 y, f32 z, f32 u, f32 v}`. Constructor presets the four UV pairs to unit-quad corners; the blob pass overwrites XYZ per actor each draw call. | confirmed |
| +84         | 4    | ptr      | `blob_texture`           | Texture-pool entry for `data/effect/tex/shadow.dds`; the blob pass acquires a static handle and binds it to stage 0. | confirmed |
| +88         | 4    | ptr      | `render_to_surface`      | `ID3DXRenderToSurface*` — render-to-texture wrapper. See **COM interfaces** below for vtable slots used. | confirmed |
| +92         | 4    | ptr      | `rt_surface`             | `IDirect3DSurface9*` — RT surface (level 0 of the `+96` texture); passed to `BeginScene`. | confirmed |
| +96         | 4    | ptr      | `rt_shadow_texture`      | `IDirect3DTexture9*` — the dynamic actor shadow-map render target. This is the texture the terrain stage-1 pass samples. | confirmed |
| +100        | 12   | —        | *(reserved / unwritten)* | Not written by the constructor or init in any traced static path. Purpose unconfirmed; see **Open items**. | static-hypothesis |
| +112        | 64   | f32[16]  | `persp_matrix`           | **Static light-perspective matrix** — `D3DXMatrixPerspectiveFovLH(fovY = π/8, aspect = 1.0, zNear = 0.0, zFar = 10000.0)`. Built once by the constructor. Set as `D3DTS_PROJECTION` during the shadow-map RT render pass. | confirmed |
| +176        | 64   | f32[16]  | `composed_matrix`        | **Runtime composed projection-texture matrix** — recomputed every frame as `invCamView · lightView · persp(+112) · UVbias(+240)`. Set as the stage-1 `D3DTS_TEXTURE1` transform in the terrain pass. See **Four-matrix chain** below. | confirmed |
| +240        | 64   | f32[16]  | `uvbias_matrix`          | **Static UV-bias matrix** — `D3DXMatrixScaling(0.5, −0.5, 1.0) · D3DXMatrixTranslation(0.5, 0.5, 0.0)`. Built once by the constructor. Maps clip space to [0, 1]² with a V-flip. | confirmed |
| +304        | 4    | f32      | `light_azimuth`          | Light azimuth angle (radians), recomputed per frame from the `EnvironmentLightScene` sun angle. Fallback constants apply when the sun-elevation term is ≤ 0.5 (approximately 1.594 for the low-angle branch and 4.69 for the other). | confirmed |
| +308        | 4    | f32      | `elevation_scalar`       | Light elevation/distance scalar. Default 60.0; mode 2 sets this to 25.0 during RT init. Multiplies the light-eye offset from the player target position. | confirmed |
| +312        | 4    | i32      | `mode_flag`              | Projector quality/mode setting sourced from the graphics-quality config singleton. 3 = shadows disabled (per-frame render gated on ≠ 3); 1 = wide-quality (actor cull radius² = 23104.0, i.e. 152²); 2 = 1024-px RT with `elevation_scalar` forced to 25.0; other values = narrow quality (cull radius² = 1936.0, i.e. 44²). Constructor initialises to 3. | confirmed |
| —           | —    | —        | *(end)*                  | **Total object size = 316 bytes.** | confirmed |

---

## COM interfaces touched

The object carries no vtable pointer — `+0` is a data flag. The two held Direct3D COM objects are
the only vtables accessed:

| Field | Interface | Vtable slots used |
|-------|-----------|-------------------|
| `+88` | `ID3DXRenderToSurface*` | Slot `+8` = Release (teardown); slot `+20` = BeginScene(surface, viewport); slot `+24` = EndScene |
| `+96` | `IDirect3DTexture9*` | Slot `+8` = Release (teardown); GetSurfaceLevel slot (read in init) yields the surface stored at `+92` |

---

## Render pass and math details

### Per-frame look-at and matrix build (`Renderer_BuildShadowLookAtMatrix`)

Gated on: `enabled_flag` (`+0`) nonzero, a local player present, and `mode_flag` (`+312`) ≠ 3.

1. **Light azimuth (`+304`).** Derived from the `EnvironmentLightScene` sun angle. When the scene
   sun-elevation term is ≤ 0.5 a fixed-angle fallback is applied (approximately 1.594 or 4.69);
   otherwise the angle is computed from the player XZ position and written to `+304`.

2. **Light view matrix.** Target = the local player's world position. Eye = target + an offset
   built from the azimuth direction and `elevation_scalar` (`+308`) as:
   `eye.x = target.x + 10 · S · sin(azimuth)`,
   `eye.y = target.y + 10 · S`,
   `eye.z = target.z + 10 · S · cos(azimuth)` (where S = `elevation_scalar`). Up = `{0, 1, 0}`.
   `D3DXMatrixLookAtLH(lightView, eye, target, up)`.

3. **Four-matrix chain (`composed_matrix`, `+176`).** The device VIEW transform is read and
   inverted, then:
   `+176 = inverse(deviceView) · lightView · persp_matrix(+112) · uvbias_matrix(+240)`.
   This is the matrix the terrain stage-1 `TCI_CAMERASPACEPOSITION` + `PROJECTED|COUNT4` path
   reads, documented in `formats/terrain.md §11.8.2`.

### Shadow-map RT render pass

Follows immediately after the matrix build in the same function.

- `BeginScene` on `render_to_surface` (`+88`) with `rt_surface` (`+92`).
- **Clear to white:** `Clear(0 rects, NULL, flags = TARGET (color-only), color = 0x00FFFFFF,
  z = 0.0, stencil = 0)`. The depth-buffer clear flag is not set.
- Render states applied for silhouette rendering:
  - ZWRITE off, ZENABLE on, ALPHABLEND off, ALPHATEST on, CULLMODE = NONE (1),
    FILLMODE = SOLID (3), LIGHTING off, FOG off. Stage-0 texture = null.
  - `D3DRS_TEXTUREFACTOR = 0x95808080` (RGB ≈ 0x80 mid-gray, A = 0x95).
  - Stage-0 combiner: COLORARG1 = TFACTOR, COLOROP = SELECTARG1, ALPHAOP = DISABLE.
  - Stage-1 COLOROP / ALPHAOP = DISABLE.
  - Result: every silhouette renders as flat mid-gray regardless of mesh material.
- `D3DTS_VIEW` ← per-frame `lightView`; `D3DTS_PROJECTION` ← static `persp_matrix` (`+112`).
- Draw the local player silhouette (`Actor_DrawPartsForShadow`), then iterate the battle-actor
  list. For each actor with its drawable flag set and within XZ distance² of the player below the
  `mode_flag`-selected cull threshold (23104.0 for mode 1 / wide; 1936.0 for other / narrow),
  draw its silhouette and clear its per-frame shadow flag.
- `EndScene` on `render_to_surface` (`+88`).

Net result: a white RT with flat mid-gray actor silhouettes rendered from the light's viewpoint.
The terrain stage-1 pass multiplies this RT over the lit ground — a white pixel applies no
shadow (×1); a gray pixel darkens (×0.5).

### Per-actor silhouette draw (`Actor_DrawPartsForShadow`)

Gated on actor draw-enabled flags (nonzero) and an exclusion flag (zero). Iterates the actor's
mesh-part vector, optionally de-duplicating parts against a per-actor exclusion list, and calls
each part's draw method via the part object's vtable. No silhouette-specific geometry is prepared —
normal part meshes are redrawn flat-gray under the TFACTOR / SELECTARG1 state set by the caller.

### Blob-quad shadow (`ActorShadow_DrawBlobQuads`)

Independent of the projected shadow-map path. Gated on `enabled_flag` (`+0`) and a local player.
Binds `blob_texture` (`+84`) to stage 0 and sets FVF `0x102`. For the local player (when
`mode_flag` (`+312`) = 3) or for each visible battle actor (otherwise), builds a 4-vertex ground
quad into `blob_quad_vertices` (`+4`): center at the actor's world position, half-extent sourced
from the AnimCatalog entry for the current animation, quad lifted +0.4 units in Y. UVs are the
preset unit-quad corners. Then `DrawPrimitiveUP(TRIANGLEFAN, 2 primitives, ptr = +4, stride = 20)`.
This is the fallback/low-quality ground blob and is NOT the projected stage-1 shadow texture.

### RT sizing rule (resolves `formats/terrain.md §11.8.1` open item)

In `ShadowManager_Init`: base dimension = device backbuffer width, clamped to ≤ 4096. When
`mode_flag` (`+312`) = 2 the dimension is forced to 1024 and `elevation_scalar` (`+308`) is set to
25.0 (otherwise 60.0). The RT texture is created square (dim × dim), usage RENDERTARGET, 1 mip
level, format = the global backbuffer color format. On CreateTexture failure the dimension is halved
once and retried; the surface level 0 and the render-to-surface wrapper are then obtained in order.

---

## Open items (debugger-pending)

| Item | Status |
|------|--------|
| `+100`..`+111` (12 bytes) | Not written by any traced path (constructor or init). Purpose unconfirmed — likely reserved or padding. A live runtime read of the singleton would settle this. |
| `composed_matrix` (`+176`) runtime value | The four-matrix formula is statically certain (see **Four-matrix chain** above); the per-frame numeric value requires a live `?ext=dbg` inspection to confirm. |
| `mode_flag` (`+312`) → quality-config mapping | Values 1 / 2 / 3 are inferred from branch analysis; the mapping from the graphics-quality config singleton's quality word to the integer stored at `+312` needs live confirmation. |
| Sun-angle fallback constants (≈ 1.594, ≈ 4.69) | Observed in static branch analysis; confirm exact float encoding via live debugger if sub-radian precision is required. |

---

## Cross-references

- Terrain stage-1 shadow binding and the four-matrix chain: `formats/terrain.md §11.8`.
- Shadow RT integration in the full render pipeline: `specs/render_pipeline.md`.
- World / scene singletons: `structs/runtime_singletons.md`.
- AnimCatalog (blob half-extent source): `specs/entity_placement.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
