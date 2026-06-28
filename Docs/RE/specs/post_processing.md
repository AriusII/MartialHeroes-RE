# Spec: Post-Processing — Cel/Glow Chain Pass Mechanics

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents the **cel/glow post-process chain pass mechanics**: the per-slot identity of the
> ten-slot `glow_chain` COM resource array, the five shader-handle slots, the render-target
> sizing rules, and the render-state and quad-geometry recipe for the extract and blur passes.
>
> The frame-level draw order in which these passes are invoked is documented in
> `Docs/RE/specs/render_pipeline.md §5` (offscreen path, steps 6–7). Per-frame enable gating,
> composite weight detail, and the full glow-chain RT overview are owned by
> `Docs/RE/specs/rendering.md §6`. Composite and glow shader arithmetic lives in
> `Docs/RE/formats/shaders.md §C5`. The global scene/post object full struct layout is in
> `Docs/RE/structs/renderer_device.md`.
>
> Every post-process constant cited in C# must reference this file:
> `// spec: Docs/RE/specs/post_processing.md`.

> **Verification banner**
> - **verification:** *static-confirmed* for all findings in this document, recovered by
>   following the control flow of the initialiser (`Renderer_InitCelGlowShaders`), the
>   two draw routines (`Renderer_GlowExtractDownsample`, `Renderer_GlowBlurDownsampled`),
>   and the offscreen orchestrator (`Renderer_DrawScene_OffscreenRT_0`).
>   Items tagged `[debugger-confirm]` are static hypotheses awaiting a live `?ext=dbg`
>   session before treating them as implementation facts. Items tagged `[open-question]`
>   are known discrepancies that the RE domain must resolve before porting the affected path.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the ten-slot COM array identity, shader-handle
>   offsets, pass recipes, and quad geometry. The TEX2 allocation base (§4.1) is `[open-question]`
>   and is BLOCKING for ports targeting backbuffer resolutions above 1024×1024. The
>   `rendering.md §6.4` loop claim (§6.2) is `[open-question]` and is BLOCKING for any
>   implementation that relies on a multi-pass blur loop. Remaining `[debugger-confirm]` items
>   are NON-BLOCKING.
> - **evidence:** [static-ida]
> - **resolves:** `Docs/RE/structs/renderer_device.md §9` — both open questions are now
>   statically confirmed: Q#1 (per-slot identity of the ten `glow_chain` COM slots) is
>   resolved by §2 of this spec; Q#2 (per-slot identity of the five `device_com_slot_a..e`
>   shader handles) is resolved by §3 of this spec.
> - **refines:**
>   `Docs/RE/specs/render_pipeline.md §8` (scene/post object table entry "+177880
>   composite/glow pixel shader handle" must be split into two distinct slots: +177876 glow-blur
>   PS, +177880 composite PS — see §3);
>   `Docs/RE/specs/rendering.md §6.1` (TEX2 allocation uses a 1024 base, not "backbuffer ÷
>   divisors" — see §4.1 open question);
>   `Docs/RE/specs/rendering.md §6.4` ("16-loop / 2×2 box blur" claim contradicted by
>   single-draw static evidence — see §6.2 open question).
> - **cross-links:**
>   `Docs/RE/specs/render_pipeline.md` (offscreen draw order §5, scene/post object §8,
>   device vtable key §11);
>   `Docs/RE/specs/rendering.md` (enable gating §6.1, RT layout §6.1, composite weights
>   §6.3, blur loop claim §6.4, cel gating §5.1a);
>   `Docs/RE/formats/shaders.md §C5` (glow, composite, and cel shader arithmetic);
>   `Docs/RE/structs/renderer_device.md` (full scene/post object struct layout, release
>   order §8.3).

---

## Status

| Item | Confidence |
|---|---|
| Chain present on build f61f66a9 | CONFIRMED |
| Ten-slot `glow_chain` COM array — per-slot identity | CONFIRMED |
| Five cel/glow shader-handle slots and decimal offsets | CONFIRMED |
| Render-target allocation dimensions (TEX0 / TEX1 / TEX2) | CONFIRMED |
| TEX2 allocation base (1024 vs backbuffer) | `[open-question]` — see §4.1 |
| Glow downsample divisors at +178752 / +178756 | CONFIRMED |
| Pass recipe — bright/edge extract (`Renderer_GlowExtractDownsample`) | CONFIRMED |
| Pass recipe — glow blur (`Renderer_GlowBlurDownsampled`) | CONFIRMED |
| Quad geometry — FVF, stride, primitive type | CONFIRMED |
| Common render-state block — stage, sampler, blend, Z | CONFIRMED |
| No code-side multi-tap blur kernel finding | CONFIRMED (static) |
| "16-loop / 2×2 box blur" claim in `rendering.md §6.4` | `[open-question]` — see §6.2 |
| Present-blit blend — ONE/ZERO (opaque copy) | CONFIRMED (static, addendum walk of `Renderer_DrawScene_OffscreenRT_0`) |
| Offscreen-enable flag == 2 at runtime | `[debugger-confirm]` (carried from `render_pipeline.md §13`) |
| Composite c0/c1 live values from display config | `[debugger-confirm]` (carried from `rendering.md §6.3`) |

---

## 1. Post-Process Chain Overview

**Confidence: CONFIRMED.**

The cel/glow post-process chain is **present and fully recovered** on IDB anchor f61f66a9.
It operates on three render targets (TEX0/TEX1/TEX2) and a toon-ramp LUT texture, all housed
in the ten-slot `glow_chain` COM resource array within the global scene/post object. Enable
gating is owned by `rendering.md §6.1` and `formats/shaders.md §C5.6b`; when the gate passes
(option index 12 == 1, hardcoded to 1 on this build), the offscreen path is active and the
enable flag at scene/post object `+178748` is set to 2.

Per-frame invocation order (`render_pipeline.md §5`):

```
[RT0 / TEX0: sky → terrain/buildings → opaque-world extras]
→ Renderer_GlowExtractDownsample  (bright/edge extract → TEX1)
→ Renderer_GlowBlurDownsampled    (bloom blur → TEX2)
→ composite (TEX1 + TEX2 → back buffer, render_pipeline.md §5 step 7)
→ Renderer_UploadToonShaderConstants + culled scene-graph draw
→ transparent/particles → present-blit → overlay/UI → EndScene
```

This spec owns the extract and blur pass mechanics (steps 2 and 3). The composite step is
documented in `render_pipeline.md §5 step 7` and `rendering.md §6.3`. The toon-ramp LUT
(slot 0 of the COM array below) is consumed by the cel material path; see `rendering.md §5.1a`
and `formats/shaders.md §C5`.

---

## 2. Ten-Slot glow_chain COM Array — Per-Slot Identity

**Confidence: CONFIRMED. Resolves `Docs/RE/structs/renderer_device.md §9`.**

`Renderer_InitCelGlowShaders` allocates the ten COM resources in the order below and stores
each at the listed byte offset within the global scene/post object. The ten slots are
contiguous from `+178652` to `+178688` (hex `+0x2B9DC` to `+0x2BA00`), stride 4 bytes. All
offsets are byte offsets from the start of the global scene/post object.

| Slot | Dec. offset | Hex offset | COM type | Role | Creation |
|----:|------------:|:----------:|----------|------|----------|
| 0 | +178652 | +0x2B9DC | IDirect3DTexture9* | **Toon-ramp LUT texture** — the 1-D N·L cel ramp loaded from `data/shader/toonramp.bmp`; bound to stage 1 during the cel draw pass | `Texture_D3DXCreateFromDiskOrVfs` |
| 1 | +178656 | +0x2B9E0 | IDirect3DTexture9* | **TEX0 — scene-capture render-target texture** (the offscreen scene accumulator; also the present-blit source) | `D3DXCreateTexture`, backbuffer dims; 1024×1024 retry |
| 2 | +178660 | +0x2B9E4 | IDirect3DTexture9* | **TEX1 — bright/edge-extract render-target texture** (composite stage 0) | `D3DXCreateTexture`, backbuffer dims; 1024×1024 retry |
| 3 | +178664 | +0x2B9E8 | IDirect3DTexture9* | **TEX2 — glow-blur render-target texture (downscaled)** (composite stage 1) | `D3DXCreateTexture`, 1024/divX × 1024/divY dims; no fallback — see §4.1 |
| 4 | +178668 | +0x2B9EC | IDirect3DSurface9* | **TEX0 surface** (= RT0 surface referenced in `render_pipeline.md §8`) | `IDirect3DTexture9::GetSurfaceLevel(0)` |
| 5 | +178672 | +0x2B9F0 | IDirect3DSurface9* | **TEX1 surface** | `IDirect3DTexture9::GetSurfaceLevel(0)` |
| 6 | +178676 | +0x2B9F4 | IDirect3DSurface9* | **TEX2 surface** | `IDirect3DTexture9::GetSurfaceLevel(0)` |
| 7 | +178680 | +0x2B9F8 | ID3DXRenderToSurface* | **TEX0 render-to-surface helper** — the offscreen-path `BeginScene`/`EndScene` helper for the scene capture; depth/stencil **enabled**; depth format from object `+177956`; vtable: BeginScene = slot `+20`, EndScene = slot `+24` | `D3DXCreateRenderToSurface(depthStencil=true, fmt=object+177956)` |
| 8 | +178684 | +0x2B9FC | ID3DXRenderToSurface* | **TEX1 render-to-surface helper** — used by `Renderer_GlowExtractDownsample`; depth/stencil **enabled** | `D3DXCreateRenderToSurface(depthStencil=true)` |
| 9 | +178688 | +0x2BA00 | ID3DXRenderToSurface* | **TEX2 render-to-surface helper** — used by `Renderer_GlowBlurDownsampled`; depth/stencil **disabled** (the blur RT requires no depth buffer) | `D3DXCreateRenderToSurface(depthStencil=false)` |

> Each `D3DXCreateRenderToSurface` call reads the surface description (width, height, format)
> from the `GetSurfaceLevel` result — each RTS helper is therefore sized exactly to its own
> render target. The release order recorded in `renderer_device.md §8.3` (releases slots
> `+0x2B9DC..+0x2BA00` first) matches this ten-element array exactly.

---

## 3. Shader-Handle Slots

**Confidence: CONFIRMED.**

The five cel/glow shader handles are stored at separate offsets within the global scene/post
object and are **not** part of the ten-slot `glow_chain` COM array. They are populated by
`Renderer_InitCelGlowShaders`.

> **Refinement of `render_pipeline.md §8`:** that section listed a single "+177880
> composite/glow pixel shader handle". Static analysis of the initialiser shows two distinct
> storage slots: +177876 holds the glow-blur pixel shader and +177880 holds the composite pixel
> shader. Decimal offsets are the confirmed `this+N` store targets from the initialiser; hex
> values are derived arithmetically from those decimal offsets.
>
> **Resolution of `renderer_device.md §9 Q#2`:** the five shader-handle slots below are the
> five `device_com_slot_a..e` named in `renderer_device.md §2`. Their device-gated release
> (documented in `renderer_device.md §8.3`) is because they are Direct3D shader objects
> tied to the device lifetime. This closes §9 Q#2 (individual slot roles previously listed as
> pending).

| Dec. offset | Hex offset | Handle | Source / note |
|------------:|:----------:|--------|---------------|
| +177876 | +0x2B6D4 | **Glow-blur pixel shader** — the editable-slot shader; default `data/shader/power1dx8.psh`; overridable via `display.lua DISPLAY_POWERSHADER`; shader filename buffer at +179028 | Editable |
| +177880 | +0x2B6D8 | **Composite pixel shader** (`finaldx8.psh`) | Fixed string |
| +178320 | +0x2B890 | **Cel vertex shader** (`dotoonshading.vsh`) | Fixed string |
| +178324 | +0x2B894 | **Cel pixel shader — normal mode** (`dotoonshading.psh`) | Fixed string |
| +178328 | +0x2B898 | **Cel pixel shader — stealth / concealment mode** (`dotoonshading2.psh`) | Fixed string |
| +179028 | +0x2BB54 | **Editable glow-shader filename buffer** — `char[]` pre-filled with `data/shader/power1dx8.psh`; written by the `display.lua` loader when `DISPLAY_POWERSHADER` is set | Filename buffer |

> The precise D3D9 creation call for the pixel-shader handles involves Hex-Rays ambiguity in
> the initialiser (a buffer-pointer acquire before shader creation). The **storage offsets** are
> unambiguous — each handle lands at the decimal offset shown. Port code should call the
> appropriate D3D9 creation API and store the result at these offsets.

---

## 4. Render-Target Sizing and Allocation

**Confidence: CONFIRMED (except §4.1 open question).**

### 4.1 Texture Dimensions

| RT | Texture slot (dec.) | Allocated dims | Fallback | Format |
|----|--------------------:|----------------|----------|--------|
| TEX0 (scene capture / present source) | +178656 | backbuffer W (`+177860`) × H (`+177864`) | Retry 1024×1024 | Probed adapter format, 1 mip, usage RENDERTARGET |
| TEX1 (bright/edge extract) | +178660 | backbuffer W × H | Retry 1024×1024 | Same as TEX0 |
| TEX2 (glow blur, downscaled) | +178664 | **1024 / divX × 1024 / divY** | None — hard fail on allocation error | Same as TEX0 |

**Glow downsample divisors:** divX at object `+178752`, divY at object `+178756`; both default
to 2 (cross-ref `rendering.md §6.3` / `display.lua DISPLAY_GLOW_RANGE_X/Y`). With defaults,
TEX2 = 512×512.

**`[open-question]` TEX2 allocation base vs render region:** the initialiser allocates TEX2
from a fixed 1024 base divided by the divisors (`1024/divX × 1024/divY`). However, the blur
pass renders into a region of **backbuffer W / divX × backbuffer H / divY** (the
`D3DXMatrixOrthoLH` projection uses backbuffer dimensions — see §6 step 3). `rendering.md
§6.1` describes TEX2 as "backbuffer ÷ divisors". The two descriptions agree when the
backbuffer does not exceed 1024 in either dimension (TEX2 has slack). For backbuffer sizes
above 1024 the render region would exceed the allocated texture. **This discrepancy must be
confirmed and `rendering.md §6.1` updated before porting to resolutions above 1024×1024.**
Flag for the RE domain: breakpoint the TEX2 allocation call and confirm the actual width/height
argument on a target device.

### 4.2 Depth and Stencil

Depth/stencil format for all RTS helpers comes from object `+177956`. Depth is enabled for the
TEX0 helper (slot 7) and TEX1 helper (slot 8), and **disabled** for the TEX2 helper (slot 9).
The blur render target requires no depth buffer.

---

## 5. Pass Recipe — Bright/Edge Extract (→ TEX1)

**Confidence: CONFIRMED.**

`Renderer_GlowExtractDownsample` performs a **plain fixed-function copy** of the full-resolution
scene texture (TEX0, object `+178656`) into the TEX1 render target (surface at `+178672`, RTS
helper at `+178684`). There is **no pixel shader, no luminance threshold, and no alpha-test**.
The function name is a misnomer; the actual operation is a passthrough. This confirms the
"bright/edge copy, plain fixed-function" characterisation in `rendering.md §6.4`.

See §7 for the common quad geometry and shared render-state applied in this pass.

**Extract-specific steps:**

1. `BeginScene` on the TEX1 RTS helper (object `+178684`; RTS vtable `+20`).
2. Clear: `D3DCLEAR_TARGET` only (no Z clear), color `0xFF000000` (opaque black), Z = 1.0.
   Via device vtable `+172` (see `render_pipeline.md §11`).
3. Set projection: `D3DXMatrixOrthoLH` over the full backbuffer width × height.
4. Apply common render-state block (§7.2): Z disabled, lighting disabled, alpha-blend disabled;
   stage 0 = `COLOROP SELECTARG1 / COLORARG1 TEXTURE`; stages 1 and 2 = `DISABLE`; sampler 0
   bilinear.
5. Bind texture stage 0 ← TEX0 (object `+178656`). **No pixel shader bound.**
6. Emit the fullscreen quad (§7.1): `DrawPrimitiveUP(D3DPT_TRIANGLEFAN, 2 primitives, stride 20)`
   via device vtable `+332` (see `render_pipeline.md §11`).
7. `EndScene` on the TEX1 RTS helper (RTS vtable `+24`).

**Net effect:** full-resolution plain copy of TEX0 into TEX1. Any "bright/edge" selectivity
is entirely a product of the composite shader arithmetic in `finaldx8.psh`
(`formats/shaders.md §C5`), not of any code-side threshold or kernel.

---

## 6. Pass Recipe — Glow Blur (→ TEX2)

**Confidence: CONFIRMED.**

`Renderer_GlowBlurDownsampled` draws the glow pixel shader over a downscaled view of TEX0
into the TEX2 render target (surface at `+178676`, RTS helper at `+178688`).

See §7 for the common quad geometry and shared render-state.

**Blur-specific steps:**

1. `BeginScene` on the TEX2 RTS helper (object `+178688`; RTS vtable `+20`).
2. Clear: `D3DCLEAR_TARGET` only (no Z clear), color `0xFF000000`, Z = 1.0.
3. Set projection: `D3DXMatrixOrthoLH` over **backbuffer W / divX × backbuffer H / divY**
   (the render region is backbuffer-scaled; divX/divY from `+178752`/`+178756`).
4. Apply common render-state block (§7.2).
5. Bind texture stage 0 ← TEX0 (object `+178656`). Set two additional stage-0 states:
   `COLORARG2 (3) = TEXTURE (2)`, `ALPHAARG1 (5) = TEXTURE (2)`.
6. Bind pixel shader ← glow-blur PS handle (object `+177876`) via device vtable `+428`
   (see `render_pipeline.md §11`).
7. Emit the fullscreen quad (§7.1): `DrawPrimitiveUP(D3DPT_TRIANGLEFAN, 2 primitives, stride 20)`
   via device vtable `+332`.
8. Clear pixel shader binding (set to null) via device vtable `+428`.
9. `EndScene` on the TEX2 RTS helper (RTS vtable `+24`).

**Net effect:** the glow pixel shader (`power1dx8.psh` by default) processes a bilinearly
minified sample of TEX0 and writes the result into the downscaled TEX2. The only spatial blur
present is the bilinear minification of the sampler; the glow PS applies a power-law scalar
falloff (see `formats/shaders.md §C5` for the arithmetic). There is **no horizontal/vertical
separable blur, no multi-tap Gaussian, no tap-weight array, and no draw loop** in this routine.

### 6.1 "No Multi-Tap Blur Kernel" Finding

Static analysis of both `Renderer_GlowExtractDownsample` and `Renderer_GlowBlurDownsampled`
shows **exactly one `DrawPrimitiveUP` call per routine** — a single `TRIANGLEFAN, 2 primitives`
draw in each. No loop over multiple draws, no per-tap offset accumulation, and no weight
array exist in either function. The complete blur is accomplished by:

1. Bilinear minification (sampler MINFILTER = LINEAR) reducing the full-res TEX0 sample into
   the smaller TEX2 footprint.
2. The glow pixel shader (`power1dx8.psh`) applying a power-law scalar to the result
   (see `formats/shaders.md §C5`).

### 6.2 Open Question — "16-Loop / 2×2 Box Blur" Claim in rendering.md §6.4

`rendering.md §6.4` mentions a "fixed pass-loop count of 16" and a "2×2 box blur". The two
actual draw routines contain **no loop**. The integer immediate 16 (if present in the binary)
may reside in the post-process constructor and either drives a different, unwalked path or is
structurally unused. **`rendering.md §6.4` must be revisited by the RE domain**: the
statically confirmed blur is a single bilinear-downsample pass through the glow PS, not a
16-iteration box. This is an `[open-question]` — do not implement a multi-pass blur loop from
§6.4 without first walking the constructor to locate the 16 immediate.

---

## 7. Common Quad Geometry and Render-State

Both the extract (§5) and the blur (§6) passes share the following geometry and base
render-state.

### 7.1 Quad Geometry

| Parameter | Value |
|---|---|
| FVF | `0x102` (`D3DFVF_XYZ \| D3DFVF_TEX1`) |
| Vertex stride | 20 bytes (12-byte XYZ + 8-byte UV) |
| Primitive type | `D3DPT_TRIANGLEFAN` |
| Primitive count | 2 (a 4-vertex fan = one fullscreen quad) |
| Half-texel offset | −0.5 applied to X and Y extents |
| X span | `−(W×0.5/scale) − 0.5` to `(W×0.5/scale) − 0.5` |
| UV range | 0.0 to 1.0 |
| World transform | Identity |
| View transform | Identity |
| Projection | `D3DXMatrixOrthoLH` — size is pass-specific (§5 step 3 / §6 step 3) |

`SetFVF` is called via device vtable `+356`; `DrawPrimitiveUP` via device vtable `+332`
(see `render_pipeline.md §11` for the full vtable key).

> Note: the composite pass documented in `render_pipeline.md §5 step 7` uses a **different**
> vertex stride of 28. The extract and blur passes both use stride 20.

### 7.2 Common Render-State

Applied at the start of each pass via `SetTextureStageState` (device vtable `+268`) and
`SetSamplerState` (device vtable `+276`):

| Stage / sampler | D3D9 parameter (enum int) | Value | D3D9 enum |
|---|---|---|---|
| Stage 0 | COLOROP (1) | 2 | SELECTARG1 |
| Stage 0 | COLORARG1 (2) | 2 | TEXTURE |
| Stage 0 | ALPHAOP (4) | 1 | DISABLE |
| Stage 1 | COLOROP (1) | 1 | DISABLE |
| Stage 1 | ALPHAOP (4) | 1 | DISABLE |
| Stage 2 | COLOROP (1) | 1 | DISABLE |
| Stage 2 | ALPHAOP (4) | 1 | DISABLE |
| Sampler 0 | MINFILTER (5) | 2 | LINEAR |
| Sampler 0 | MAGFILTER (6) | 2 | LINEAR |
| Sampler 0 | MIPFILTER (7) | 2 | LINEAR |

Additional state (both passes): Z-write disabled; lighting disabled; alpha-blend disabled.

The blur pass (§6) sets two additional stage-0 states after the common block:
`COLORARG2 (3) = TEXTURE (2)`, `ALPHAARG1 (5) = TEXTURE (2)`.

> Sampler 0 uses **bilinear** (LINEAR) filtering for all three filter modes. This bilinear
> minification is the sole spatial-blur mechanism in the blur pass. Contrast with the opaque
> scene-graph draw block in `render_pipeline.md §9`, which uses anisotropic min/mag filtering.

---

## 8. Composite Pass Reference

The composite step that immediately follows the blur pass (combining TEX1 and TEX2 onto the
back buffer) is fully documented in `render_pipeline.md §5 step 7` (frame position) and
`rendering.md §6.3` (shader arithmetic and c0/c1 weights). This spec contributes the
confirmed field identities feeding that step:

| Input | Object field | Role in composite |
|---|---|---|
| TEX1 texture | +178660 | Stage 0 source (`formats/shaders.md §C5` `DISPLAY_BASE_BRIGHTNESS`) |
| TEX2 texture | +178664 | Stage 1 source (`formats/shaders.md §C5` `DISPLAY_GLOW_BRIGHTNESS`) |
| Composite PS handle | +177880 | Pixel shader bound for the composite draw |
| c0 scalar source | +179016 | PS constant register 0 (base-brightness); uploaded via device vtable `+436` |
| c1 scalar source | +179020 | PS constant register 1 (glow-brightness); uploaded via device vtable `+436` |

Constructor defaults for c0 and c1 are 1.0/1.0; shipped `display.lua` targets c0 ≈ 1.05,
c1 ≈ 0.3 (`[debugger-confirm]` for live values — see `rendering.md §6.3`).

### 8.1 Present-Blit Blend — Static Confirmation

**Confidence: CONFIRMED (static).**

The present-blit pass (step 10 of the offscreen sequence in `render_pipeline.md §5`) was
walked as part of the addendum recovery of `Renderer_DrawScene_OffscreenRT_0`. After the cel
and transparent draws into TEX0, the engine clears the back buffer and opens a new scene to
blit TEX0:

- Alpha-blend is **enabled**.
- `D3DRS_SRCBLEND` = **`D3DBLEND_ONE` (value 2)** — source factor one.
- `D3DRS_DESTBLEND` = **`D3DBLEND_ZERO` (value 1)** — destination factor zero.

This is an **opaque copy** of TEX0 onto the back buffer, not an additive blend. The glow
contribution is entirely accumulated inside the composite pixel shader (`finaldx8.psh`) during
step 7; the present pass does not add a second glow layer. The quad uses FVF `0x102`
(XYZ + TEX1), vertex stride 20, `TRIANGLEFAN` with 2 primitives — matching `render_pipeline.md
§5 step 10`.

> This finding upgrades the present-blit blend entry in `rendering.md §6.3` from
> capture-pending to static-confirmed. See §10 below for the required correction.

---

## 9. Debugger-Confirm Items

| # | Item | What to confirm |
|---|---|---|
| 1 | Offscreen-enable flag == 2 at runtime | Breakpoint `Renderer_DrawScene_Fork`, read scene/post object `+178748`. Carried from `render_pipeline.md §13` item 1 and `shaders.md §C5.6b`. |
| 2 | Composite c0/c1 live values | Read object `+179016` and `+179020` while the composite pass runs. Carried from `rendering.md §6.3`. |
| 3 | TEX2 allocation base with backbuffer > 1024 | Confirm the actual width/height argument fed to the TEX2 `D3DXCreateTexture` call at runtime. See §4.1 open question. |
| 4 | "16-loop" immediate in post-process constructor | Locate the integer 16 in the post-process constructor to determine whether it is dead or drives a path not covered by the two draw routines. See §6.2. |

---

## 10. Corrections and Refinements to Sibling Specs

The following changes to committed specs are indicated by the findings in this document. Each
must be applied by the RE domain or spec-author as a separate edit to the named file.

| Spec | Section | Required change |
|---|---|---|
| `Docs/RE/specs/render_pipeline.md` | §8 (scene/post object table) | Split the single entry "+177880 composite/glow pixel shader handle" into two rows: glow-blur PS at +177876 (+0x2B6D4) and composite PS at +177880 (+0x2B6D8). |
| `Docs/RE/structs/renderer_device.md` | §9 | Mark RESOLVED — all ten `glow_chain` COM slots are identified in §2 of this spec. |
| `Docs/RE/specs/rendering.md` | §6.1 | Update TEX2 description: allocation uses a 1024 base (`1024/divX × 1024/divY`), not "backbuffer ÷ divisors". Note that the blur draw region IS backbuffer-based; the two differ above 1024×1024. Confirm and resolve with debugger (§9 item 3). |
| `Docs/RE/specs/rendering.md` | §6.4 | Downgrade or remove the "16-loop / 2×2 box blur" claim pending RE-domain walk of the post-process constructor. The confirmed blur is a single bilinear-downsample pass through the glow PS (§6.1 of this spec). |
| `Docs/RE/specs/rendering.md` | §6.3 | Upgrade the "present-blit blend bytes" item from capture-pending to static-confirmed: present pass uses `D3DBLEND_ONE` / `D3DBLEND_ZERO` (opaque copy of TEX0 → back buffer). The glow add is in the composite PS, not at present time. See §8.1 of this spec. |
