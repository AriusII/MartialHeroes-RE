# Spec: Character Rendering — Cel-Shaded Skinned Actor Draw Path

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents the **per-actor draw path** for characters and mobs: the cel/toon shading system,
> the per-part programmable draw loop, the UI-panel fixed-function cel path, status tinting,
> shadow projection and blob geometry, and the optional glow/PowerShader post-process chain.
>
> The CPU-LBS deform chain (bind skeleton, inverse-bind, per-vertex linear-blend skinning) is
> owned by `Docs/RE/specs/skinning.md` and is not redefined here. This spec picks up after
> the deformed mesh is built and covers how it is *drawn*. Frame-level orchestration (driver
> loop, GView layout, pass installer, offscreen-path sequence) is owned by
> `Docs/RE/specs/render_pipeline.md`; this spec adds the per-actor inner draw detail and
> the character-rendering-specific render state.

> **Verification banner**
> - **verification:** *static-confirmed* for all findings in this document, recovered by
>   following the per-part cel draw routine, the cel-shader bind/unbind helpers, the toon
>   constant upload path, the blob-shadow draw, and the ShadowManager and post-process
>   object constructors. Items explicitly tagged `[debugger-confirm]` are static hypotheses
>   awaiting a live `?ext=dbg` session before treating them as implementation facts.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the two cel draw paths, shader asset names, VS/PS
>   constant registers, render-state recipes, blob-shadow geometry, ShadowManager projection
>   math, and post-process glow chain. Items tagged `[debugger-confirm]` are NON-BLOCKING.
> - **evidence:** [static-ida]
> - **wave-9 reconciliation (2026-06-28):** `Docs/RE/specs/screen_effects.md §4` (deep-3d wave 7,
>   IDB anchor f61f66a9) resolved §4.3 open item #5 — the nine per-character status-tint state
>   names are now STATIC-CONFIRMED from `display.lua` key suffixes; MULTI\_R/G/B channels are
>   halved at load (×0.5); ADD\_R/G/B and ALPHA channels are raw. See §4.3 of this spec
>   (updated inline) and `screen_effects.md §4` (authoritative for the state-name table and
>   palette-offset layout).
> - **cross-links:**
>   `Docs/RE/specs/render_pipeline.md` (frame draw order §4/§5, global scene/post object §8,
>   cel VS registers c4..c10 §10, offscreen-path sequence §5);
>   `Docs/RE/specs/rendering.md` (per-pass render-state matrix, glow chain RT layout);
>   `Docs/RE/specs/skinning.md` (CPU-LBS deform chain, `.skn` mesh-part layout, bone palette);
>   `Docs/RE/formats/shaders.md` (cel VS/PS shader files and toon-ramp LUT format).

---

## Status

| Item | Confidence |
|---|---|
| Graphics API — Direct3D 9 + D3DX9_42 | CONFIRMED |
| Shader assets assembled from VFS at runtime | CONFIRMED |
| Cel VS constant registers c4..c10 (incl. BT.601 luma c9) | CONFIRMED |
| Per-part programmable draw loop — WVP, tint, bind/unbind | CONFIRMED |
| Status-tint system — 9-state mul/add palette | CONFIRMED |
| UI-panel fixed-function cel path | CONFIRMED |
| Post-process enable fork (default OFF) | CONFIRMED |
| World render-pass order | CONFIRMED |
| Blob shadow geometry and render states | CONFIRMED |
| ShadowManager projected shadow — FOV, bias matrix, quality tiers | CONFIRMED |
| Glow/PowerShader post-process — RT setup, extract, blur, composite | CONFIRMED |
| No geometric back-face / two-pass outline in doida.exe | CONFIRMED |
| Exact Vector4fCount for cel VS constant uploads | [debugger-confirm] |
| Semantic roles of cel VS c5, c7, c8 | [debugger-confirm] |
| RT read/write ping-pong order (glow extract→blur) | [debugger-confirm] |
| Status-tint state real meanings (DEFAULT..AUTO, 9 states) | STATIC-CONFIRMED — see §4.3 and `screen_effects.md §4` |
| PowerShader enable flag value at runtime on a normal session | [debugger-confirm] |
| Whether power1dx8.psh is bound in the blur step | [debugger-confirm] |

---

## 1. Subsystem scope

The character rendering subsystem is the per-actor draw path executed for each visible
character or mob when the scene-graph cull-set emits them during the frame DRAW phase. It
sits between two separately-documented subsystems:

- **Upstream — deform chain** (`Docs/RE/specs/skinning.md`): the CPU-LBS deform pipeline
  builds a deformed vertex buffer per actor per frame. This spec picks up after that buffer
  is ready.
- **Downstream — frame orchestration** (`Docs/RE/specs/render_pipeline.md`): the cull-set
  draw call invokes the character draw via the cull-drawable vtable, within the pass sequence
  documented in that spec (§4 direct-path, §5 offscreen-path).

This spec adds the **inner draw detail**: the cel/toon shading model, the two draw paths
(UI-panel fixed-function and in-world programmable), status tinting, blob and projected
shadows, and the optional glow/PowerShader post-process.

---

## 2. Shader and ramp assets (VFS-resident)

All shader programs are **assembled at runtime** from VFS files using `D3DXAssembleShader` /
`D3DXAssembleShaderFromFileA`. The shader instructions live in the VFS, not in `doida.exe`.
The cel ramp texture is also VFS-resident.

| VFS asset path | Role |
|---|---|
| `data/shader/dotoonshading.vsh` | Cel vertex shader — applies BT.601 luma weighting against the toon ramp |
| `data/shader/dotoonshading.psh` | Cel pixel shader — standard per-part variant |
| `data/shader/dotoonshading2.psh` | Cel pixel shader — alternate per-part variant (selected by the part shading-variant flag) |
| `data/shader/finaldx8.psh` | Composite pixel shader — scene + glow additive blend for the post-process path |
| `data/shader/power1dx8.psh` | Glow pixel shader — used in the PowerShader blur/glow step |
| `data/shader/toonramp.bmp` | Toon-ramp LUT texture — bound on sampler 1 during cel passes (CLAMP addressing) |

Handles for these assets are stored in the renderer object (§9). The cel VS and both PS
variants are loaded during renderer initialization. The power-glow shader VFS path string is
stored data-driven on the renderer object at field `+179028`.

*Cross-link: `Docs/RE/formats/shaders.md` for per-instruction content and assembly version
conventions of these files.*

---

## 3. Cel/toon draw paths

Two distinct draw paths exist for characters:

1. **UI-panel 3D preview** — fixed-function multitexture. Used by the character-info and
   inventory panel previews; does not use the programmable cel shaders.
2. **In-world per-part** — programmable vertex and pixel shaders. Used for all in-world
   actor draws. This is the primary cel path.

Both paths use `data/shader/toonramp.bmp` as a ramp LUT and fall back to fixed-function
rendering when the post-process flag (§6.1) is off.

### 3.1 Per-part in-world draw sequence (programmable path)

For each visible actor, once per frame, the animation pose is advanced and the CPU-LBS
deform runs (per `Docs/RE/specs/skinning.md`). Then for each visible mesh part (overlay
slot — see `Docs/RE/specs/skinning.md §3.5/§3.6`):

1. Bind the part's skin/equipment texture on stage 0 (by name; fall back to a default if
   absent).
2. If the post-process flag (renderer field `+178748`) is zero: invoke the fixed-function
   part draw (cull-drawable vtable fallback slot) and skip the remaining steps.
3. Read the World, View, and Projection transforms; compute `WVP = World · View · Proj`;
   **transpose** the result; upload as vertex-shader constant registers **c0..c3**
   (`SetVertexShaderConstantF`, start register 0, 4 rows × 4 floats = 16 floats).
4. Compute a **status tint index** (0..8) from a one-hot actor status field: bit values
   1, 2, 4, 8, 16 (→ index 5), 32, 64, 128 map to indices 1..8; no bit set → index 0.
5. Upload two **pixel-shader constants** from the 9-entry mul/add palette arrays on the
   renderer object:
   - PS c0 = `(mulR, mulG, mulB, 1.0)` — multiply tint for the selected state.
   - PS c1 = `(addR, addG, addB, addA)` — add tint for the selected state.
   (`SetPixelShaderConstantF`, registers 0 and 1, one 4-vector each.)
   Default palette entries are identity (mul = 1, 1, 1; add = 0, 0, 0; alpha = 1).
6. Set render state: disable fog; enable alpha-blend; SrcBlend = SRCALPHA (5);
   DestBlend = INVSRCALPHA (6).
7. **Bind cel shaders** (begin-cel-part helper):
   - SetTexture(sampler 1, toon ramp `toonramp.bmp`).
   - SetVertexShader = cel VS (`dotoonshading.vsh`).
   - SetPixelShader = `dotoonshading2.psh` if the part's shading-variant flag (actor field
     `+1836`) is set; else `dotoonshading.psh`.
8. SetStreamSource(0, part vertex buffer, **stride 32**); SetIndices(part index buffer).
9. DrawIndexedPrimitive (primitive type 4 = **TRIANGLELIST**; base vertex, min index 0,
   vertex count, start index, and primitive count all sourced from the part/mesh record).
10. **Unbind cel shaders** (end-cel-part helper): SetVertexShader(null); SetPixelShader(null);
    SetTexture(sampler 1, null).

### 3.2 UI-panel fixed-function draw sequence

Used by the character-info and inventory panel previews. The animation pose and CPU-LBS
deform run identically to the in-world path. Then:

1. Configure the fixed-function multitexture cel state (render-state recipe in §5.1).
2. Iterate the part array; draw each part via the cull-drawable vtable fixed-function slot.
3. Restore the standard modulate/alpha-blend stage state (§5.1 restore block).

This path does not bind the programmable cel VS or PS. The toon ramp is applied on stage 1
with CLAMP addressing when the global ramp-enable toggle is set.

---

## 4. Shader constant registers

### 4.1 Per-frame cel vertex-shader constants (c4..c10)

Uploaded once per frame by the toon-constant upload routine — invoked in the offscreen path
before the cull-set draw (see `Docs/RE/specs/render_pipeline.md §5 step 8`) — via
`SetVertexShaderConstantF`. These confirm and extend `render_pipeline.md §10`.

| VS register | float4 value | Role |
|---|---|---|
| c4 | `[lightDir.x, lightDir.y, lightDir.z, 0]` | Toon light direction — dynamic per-frame; default `(−1, 0, 0, 0)` |
| c5 | `[0, 0, −1.0, 0]` | Fixed secondary direction vector (negative Z) — `[debugger-confirm]` semantic |
| c6 | `[1, 1, 1, 1]` | Material/ambient constant |
| c7 | `[0, 0, 0, 0]` | Zero constant — `[debugger-confirm]` semantic |
| c8 | `[0, 1, 1, 1]` | Material/ambient constant — `[debugger-confirm]` semantic |
| **c9** | **`[0.299, 0.587, 0.114, 1.0]`** | **BT.601 luminance weights** — the cel VS dots vertex color or light by these to compute luminance for the toon-ramp lookup |
| c10 | `[1, 1, 1, 1]` | Material/ambient constant |

c9 is the headline confirmation: the BT.601 luma coefficients are compiled into `doida.exe`
and pushed to the cel vertex shader every frame. The toon-ramp band is selected from
`toonramp.bmp` (sampler 1, CLAMP) using the resulting luminance value. The cel VS algorithm
itself is in the external `.vsh` (VFS) and is not in `doida.exe`.

c4 is the per-frame dynamic light direction sourced from the renderer object at fields
`+178692..+178700` (matching `render_pipeline.md §8`). The exact `Vector4fCount` argument in
each upload call is `[debugger-confirm]`; consecutive single-register start indices imply one
4-vector per register.

### 4.2 Per-part cel vertex-shader constants (c0..c3 — WVP)

Uploaded per part in step 3 of §3.1 via `SetVertexShaderConstantF`, start register 0:

| VS registers | Value | Role |
|---|---|---|
| c0..c3 | `World · View · Proj` matrix, **transposed** | World-view-projection transform (4 rows × 4 floats) |

These are not a bone palette. CPU-LBS deform has already applied the skeleton transform;
c0..c3 supply the camera projection only.

### 4.3 Per-part pixel-shader constants (c0, c1 — status tint)

Uploaded per part in step 5 of §3.1:

| PS register | float4 value | Role |
|---|---|---|
| c0 | `(mulR, mulG, mulB, 1.0)` | Multiply tint — per status state (default 1, 1, 1, 1) |
| c1 | `(addR, addG, addB, addA)` | Add tint — per status state (default 0, 0, 0, 0) |

The mul/add palettes are 9-entry arrays on the renderer object (entries 0..8), with channel
stride 9 within each array. The tint state index (0 = no status; 1..8 = one-hot bit states)
selects the palette row.

**STATIC-CONFIRMED — resolves open item #5 (authority: `Docs/RE/specs/screen_effects.md §4`).**
`DisplayConfig_ParseFramerate` (the `display.lua` loader) names the nine states via their key
suffixes. State index → meaning:

| State index | display.lua key suffix | Meaning |
|---|---|---|
| 0 | `_DEFAULT` | Normal / no status |
| 1 | `_CHOICE` | Currently-selected or targeted actor |
| 2 | `_HIT` | Damage flash (actor just received a hit) |
| 3 | `_ALPHA` | Translucent / semi-transparent actor |
| 4 | `_HIDDEN` | Stealth / concealment (pairs with `dotoonshading2.psh` cel-PS swap) |
| 5 | `_POISON` | Poisoned status |
| 6 | `_TYPE` | Type / class colouring (specific sub-meaning `[debugger-confirm]`) |
| 7 | `_ANGER` | Rage / berserk status |
| 8 | `_AUTO` | Auto-attack / auto mode |

**Load-time palette math:** MULTI\_R/G/B values are **halved** by `DisplayConfig_ParseFramerate`
on load — a `display.lua` value of 2.0 yields a ×1.0 identity multiply. ADD\_R/G/B and ALPHA
values are stored **raw** (not scaled). The full per-component offset table for the palette
arrays (seven arrays of nine floats, starting at renderer field `+178764`) is in
`Docs/RE/specs/screen_effects.md §4.2`.

Non-identity authored palette values (the actual `display.lua` numbers per state) remain
`[debugger-confirm]` pending inspection of the shipped `data/script/display.lua` or a live
palette read in the scene/post singleton.

### 4.4 Final-composite pixel-shader constants

Uploaded before the post-process composite quad (§8, step 4):

| PS register | Value | Role |
|---|---|---|
| c0 | Scalar from renderer `+179016`, broadcast to float4 | Scene/base brightness for the `finaldx8.psh` composite |
| c1 | Scalar from renderer `+179020`, broadcast to float4 | Glow brightness for the `finaldx8.psh` composite |

Default value 1.0 for both. These match `Docs/RE/specs/render_pipeline.md §8` offsets
`+179016` and `+179020`.

---

## 5. Render-state recipe

D3D9 enum codes used throughout this section:

| Category | Codes |
|---|---|
| Texture stage state (TSS) types | 1 COLOROP, 2 COLORARG1, 3 COLORARG2, 4 ALPHAOP, 5 ALPHAARG1, 6 ALPHAARG2 |
| Texture op (TOP) | 1 DISABLE, 2 SELECTARG1, 4 MODULATE, 8 ADDSIGNED, 11 ADDSMOOTH |
| Texture arg (TA) | 0 DIFFUSE, 1 CURRENT, 2 TEXTURE |
| Sampler state (SAMP) | 1 ADDRESSU, 2 ADDRESSV, 5 MAGFILTER, 6 MINFILTER, 7 MIPFILTER |
| Texture address (TADDRESS) | 1 WRAP, 3 CLAMP |
| Texture filter (TEXF) | 2 LINEAR |
| Blend factor (BLEND) | 1 ZERO, 2 ONE, 5 SRCALPHA, 6 INVSRCALPHA |
| Primitive type | 4 TRIANGLELIST, 6 TRIANGLEFAN |

### 5.1 UI-panel cel (fixed-function multitexture)

**Cel block** (applied before drawing all parts):

| Stage / sampler | TSS/SAMP type | Value | D3D9 name |
|---|---|---|---|
| Stage 0 | COLORARG1 | 2 | TEXTURE |
| Stage 0 | COLORARG2 | 0 | DIFFUSE |
| Stage 0 | COLOROP | 2 | SELECTARG1 |
| Stage 0 | ALPHAOP | 1 | DISABLE |
| Stage 1 | COLORARG1 | 2 | TEXTURE |
| Stage 1 | COLORARG2 | 0 | DIFFUSE |
| Stage 1 | COLOROP | 2 if ramp-enable toggle set, else 1 | SELECTARG1 / DISABLE |
| Stage 1 | ALPHAOP | 1 | DISABLE |
| Stage 2 | COLOROP | 1 | DISABLE |
| Stage 2 | ALPHAOP | 1 | DISABLE |
| Sampler 0 | MINFILTER | 2 | LINEAR |
| Sampler 0 | MAGFILTER | 2 | LINEAR |
| Sampler 0 | MIPFILTER | 2 | LINEAR |
| Sampler 0 | ADDRESSU | 1 | WRAP |
| Sampler 0 | ADDRESSV | 1 | WRAP |
| Sampler 1 | ADDRESSU | 3 | **CLAMP** |
| Sampler 1 | ADDRESSV | 3 | **CLAMP** |

**Restore block** (applied after drawing all parts):

Stage 0: COLORARG1 = TEXTURE (2), COLORARG2 = DIFFUSE (0), COLOROP = MODULATE (4);
ALPHAARG1 = TEXTURE (2), ALPHAARG2 = DIFFUSE (0), ALPHAOP = MODULATE (4); Sampler 0
MIN/MAG = LINEAR. Stage 1: COLOROP = DISABLE (1). Z-test on, Z-write on, lighting on, fog
off, dither/alpha-test/alpha-blend configured; SrcBlend = SRCALPHA (5), DestBlend =
INVSRCALPHA (6).

### 5.2 In-world per-part cel (programmable)

States applied within the per-part draw loop (steps 6–10 of §3.1):

| State | Value | D3D9 name |
|---|---|---|
| Fog | Disabled | D3DRS_FOGENABLE = false |
| Alpha-blend | Enabled | D3DRS_ALPHABLENDENABLE = true |
| SrcBlend | 5 | SRCALPHA |
| DestBlend | 6 | INVSRCALPHA |
| Vertex shader | `dotoonshading.vsh` handle | — |
| Pixel shader | `dotoonshading.psh` or `dotoonshading2.psh` handle | variant selected by part flag at actor `+1836` |
| Sampler 1 texture | `toonramp.bmp` handle | CLAMP addressing |
| Stream stride | 32 bytes | — |
| Primitive type | 4 | TRIANGLELIST |

After the draw call all shader and sampler-1 binds are cleared.

---

## 6. Per-frame orchestration fork and draw order

### 6.1 Post-process / PowerShader enable flag

The per-frame draw is gated by a flag in the renderer object at field `+178748` (also
documented in `Docs/RE/specs/render_pipeline.md §8` as the offscreen-RT / post-process
enable flag):

- **Flag 0 (default):** direct-to-back-buffer path — actors draw via the fixed-function
  fallback slot; the programmable cel shader bind in §3.1 step 2 is skipped.
- **Flag non-zero:** offscreen RT path — the toon constant upload (§4.1) runs before the
  cull-set draw; the full programmable per-part loop (§3.1) applies.

The flag is initialized to 0 in the post-process constructor and toggled by the
`DISPLAY_POWERSHADER` display configuration key. The runtime value for a normal in-world
session is `[debugger-confirm]`.

### 6.2 World render-pass order

Actors are submitted as cull drawables and drawn through the cull manager's traversal (via
the cull-drawable vtable), not directly inside the world-pass functions. The installed pass
order (from `Docs/RE/specs/render_pipeline.md §6.1`):

```
begin-of-frame callback (eye cache, terrain visibility, fog, effects tick, shadow matrix)
  → sky / background pass
  → world terrain and buildings pass
  → opaque world pass
      → culled scene-graph draw (statics + actors, via cull-set vtable slot)
      → blob shadow quads (drawn at end of opaque pass)
  → transparent and particles pass
```

In the offscreen path the cull-set draw is deferred to after the glow composite
(see `render_pipeline.md §5 step 8`).

---

## 7. Shadow subsystem

### 7.1 Blob shadow geometry

A simple textured ground quad is drawn under each actor at the end of the opaque world pass.

| Attribute | Value |
|---|---|
| Shape | 4-vertex quad centred on the actor ground position |
| Half-extent | Shadow radius from the AnimCatalog record field `+20` |
| Height offset | `+0.4` units above the ground plane |
| FVF | `0x102` — XYZ position + 1 texture-coordinate set (TEX1) |
| Vertex stride | 20 bytes |
| Primitive type | TRIANGLEFAN, 2 primitives |
| Draw call | `DrawPrimitiveUP` |
| Render states | Alpha-test enabled; Z-write **disabled**; sampler 0 mip filter LINEAR |
| Texture | Blob shadow texture from the texture pool |

### 7.2 Projected shadow (ShadowManager)

A render-to-texture projected shadow for higher-quality silhouette coverage.

**Projection parameters:**

| Parameter | Value |
|---|---|
| Projection type | Perspective (`D3DXMatrixPerspectiveFovLH`) |
| Field of view | π/8 radians (22.5°) |
| Aspect ratio | 1.0 |
| Near plane | 0.0 |
| Far plane | 10000.0 |
| NDC→UV bias | `Scale(0.5, −0.5, 1.0) × Translation(0.5, 0.5, 0.0)` — standard D3D9 shadow-map remap with Y flip |

**Quality tiers:**

| Tier | Render target size cap | Behaviour |
|---|---|---|
| Default | 4096 | Full projected-shadow RTT; far-plane parameter 60.0 |
| Quality level 2 | Forced to 1024 | Far-plane parameter 24.0 |
| Quality level 3 | — | Skip render-to-surface; no RTT shadow produced |

Shadow blend texture: `data/effect/tex/shadow.dds`. RTT surface built with
`D3DXCreateRenderToSurface`.

The per-actor shadow pass iterates the part array and draws each part via the fixed-function
cull-drawable vtable slot (the same fallback used when the post-process flag is off),
rendering the actor as an un-shaded silhouette mesh into the projector render target.
Whether the projector RT bind/clear is wrapped around that draw is `[debugger-confirm]`
(open item §11.2 item 2).

---

## 8. Glow / PowerShader post-process chain

Active only when the post-process enable flag (renderer field `+178748`) is non-zero.
Managed by the renderer/post-process object.

**Render targets:**

| Target | Size | Role |
|---|---|---|
| RT0 | Screen size (fallback 1024×1024) | Scene capture and composite destination |
| RT1 | Screen size (fallback 1024×1024) | Glow extract intermediate |
| RT2 | RT0 / downsample divisors (default 2×2 → quarter area) | Downsampled glow source |

Downsample divisors are stored at renderer fields `+178752` and `+178756` (default 2 each).
Each RT is wrapped by a `D3DXCreateRenderToSurface` helper (vtable offset `+20` =
BeginScene, vtable offset `+24` = EndScene).

**Full-screen quad geometry:**

| Quad | FVF | Stride | Notes |
|---|---|---|---|
| Composite quad | `0x202` — XYZ + TEX2 | 28 bytes (12 pos + 2×8 UV) | Half-texel offset: each corner `±(dim × 0.5) − 0.5` |
| Present / blit quad | `0x102` — XYZ + TEX1 | 20 bytes | Half-texel offset applied |

Both use `DrawPrimitiveUP(TRIANGLEFAN, 2 primitives)`. Orthographic projection:
`D3DXMatrixOrthoLH(width, height, 0, 1)` with identity world and view.

**Pass sequence** (offscreen path, extending `Docs/RE/specs/render_pipeline.md §5`):

1. Render scene (sky, terrain/buildings, opaque-world extras) to RT0.
2. **Glow extract/downsample:** clear RT1 to opaque black; draw a full-screen quad sampling
   RT0 onto RT1 (Z-write off, Z on, lighting on, alpha-blend off, sampler 0 LINEAR). This
   downsamples the scene for the glow source.
3. **Glow blur:** blur the downsampled RT (separate blur helper; whether `power1dx8.psh` is
   bound in this step is `[debugger-confirm]`).
4. **Composite:** bind `finaldx8.psh`; upload PS constants c0/c1 from renderer `+179016`/
   `+179020` (§4.4); set stage 0 = scene RT, stage 1 = blurred glow RT; stage ops =
   ADDSIGNED (stage 0) + ADDSMOOTH (stage 1); draw the composite quad (FVF `0x202`,
   stride 28) onto RT0; clear shader and stage binds.
5. **Toon constant upload + cull-set draw:** upload cel VS registers c4..c10 (§4.1); draw
   the cull-set (statics + cel actors) on top of the composited background. Blend: source
   ONE (2), destination ZERO (1); alpha-test enabled.
6. **Transparent/particles pass** over the world.
7. **Present:** clear the back buffer; begin scene; draw the blit quad (FVF `0x102`,
   stride 20, source = composited RT0, SrcBlend ONE / DestBlend ZERO) to the back buffer;
   run the overlay/UI callback and FPS HUD; EndScene; Present.

---

## 9. Renderer object — character-rendering field map

Fields in the renderer / global scene-post object used by the character rendering subsystem.
Fields shared with `Docs/RE/specs/render_pipeline.md §8` are noted; the table here adds
fields specific to this subsystem.

| Renderer field offset | Role |
|---|---|
| `+177876` | Glow PS handle (`power1dx8.psh`) |
| `+177880` | Composite PS handle (`finaldx8.psh`) — also in `render_pipeline.md §8` |
| `+178320` | Cel VS handle (`dotoonshading.vsh`) |
| `+178324` | Cel PS handle — standard variant (`dotoonshading.psh`) |
| `+178328` | Cel PS handle — alternate variant (`dotoonshading2.psh`) |
| `+178652` | Toon-ramp texture handle (`toonramp.bmp`) |
| `+178656` | RT0 texture handle |
| `+178660` | Composite-stage texture, stage 0 — also in `render_pipeline.md §8` |
| `+178664` | Composite-stage texture, stage 1 — also in `render_pipeline.md §8` |
| `+178668` | RT0 surface handle |
| `+178672` | RT1 surface handle |
| `+178676` | RT2 (downsampled glow) surface handle |
| `+178680` | `D3DXCreateRenderToSurface` helper for RT0 — also in `render_pipeline.md §8` |
| `+178684` | `D3DXCreateRenderToSurface` helper for RT1 |
| `+178688` | `D3DXCreateRenderToSurface` helper for RT2 (downsampled glow) |
| `+178692..+178700` | Toon light direction (float3 X/Y/Z; default −1, 0, 0) — also in `render_pipeline.md §8` |
| `+178704..+178744` | Toon material / secondary color vectors (default 1.0) |
| `+178748` | Post-process / PowerShader enable flag (default 0) — also in `render_pipeline.md §8` |
| `+178752` | Glow downsample divisor X (default 2) |
| `+178756` | Glow downsample divisor Y (default 2) |
| `+178764..` | 9-entry mul/add status-tint palette arrays (channel stride 9; default identity) |
| `+179016` | Final-composite modulation scalar c0 (scene brightness; default 1.0) — also in `render_pipeline.md §8` |
| `+179020` | Final-composite modulation scalar c1 (glow brightness; default 1.0) — also in `render_pipeline.md §8` |
| `+179028` | `power1dx8.psh` VFS path string |

---

## 10. D3D9 device vtable slot role map

Slots used by the character rendering subsystem, identified by their byte offset into the
`IDirect3DDevice9` COM vtable (roles only). Slots also present in
`Docs/RE/specs/render_pipeline.md §11` are noted; this table adds the entries specific to
this subsystem (`+228`, `+260`, `+364`, `+424`).

| Vtable offset | Slot index | Role |
|---|---|---|
| `+168` | 42 | EndScene — also in `render_pipeline.md §11` |
| `+228` | 57 | SetRenderState |
| `+260` | 65 | SetTexture |
| `+268` | 67 | SetTextureStageState — also in `render_pipeline.md §11` |
| `+276` | 69 | SetSamplerState — also in `render_pipeline.md §11` |
| `+332` | 83 | DrawPrimitiveUP (blob shadow and post-process quads) — also in `render_pipeline.md §11` |
| `+356` | 89 | SetFVF — also in `render_pipeline.md §11` |
| `+364` | 91 | CreateVertexShader (shader load at renderer initialization) |
| `+376` | 94 | SetVertexShaderConstantF — also in `render_pipeline.md §11` |
| `+424` | 106 | CreatePixelShader (shader load at renderer initialization) |
| `+428` | 107 | SetPixelShader — also in `render_pipeline.md §11` |
| `+436` | 109 | SetPixelShaderConstantF — also in `render_pipeline.md §11` |

`DrawIndexedPrimitive`, `SetStreamSource`, and `SetIndices` are dispatched through thin
`GDevice` wrapper routines rather than being called directly on the device vtable.

`ID3DXRenderToSurface` vtable: offset `+20` = BeginScene, offset `+24` = EndScene.
`IDirect3DTexture9` vtable: offset `+72` = GetSurfaceLevel (level 0), offset `+48` =
GetLevelDesc.

---

## 11. Open items and [debugger-confirm]

### 11.1 [debugger-confirm] items

Static-confirmed hypotheses requiring a live `?ext=dbg` session before treating them as
implementation facts. All are NON-BLOCKING for the core port work.

| # | Item | What to confirm |
|---|---|---|
| 1 | Cel VS constant upload count | Confirm the `Vector4fCount` argument for each `SetVertexShaderConstantF` call uploading c4..c10; consecutive single-register start indices imply 1 vec4 per register, but the decompiler-rendered argument value differs. |
| 2 | Cel VS c5, c7, c8 semantics | Static values `(0, 0, −1, 0)`, `(0, 0, 0, 0)`, `(0, 1, 1, 1)` are confirmed; their semantic use inside `dotoonshading.vsh` is not visible from the exe. Confirm by reading the VFS shader or inspecting live register state. |
| 3 | RT read/write ping-pong order | The roles of RT0 (scene capture), RT1 (glow extract), and RT2 (downsampled) are recovered; the exact read/write sequence between the extract and blur passes is cleanest confirmed live. |
| 4 | Post-process flag at runtime | The default is 0 (direct path); confirm whether the shipped `DISPLAY_POWERSHADER` display configuration sets this flag on the target machine. |
| 5 | Status-tint state meanings | **RESOLVED — STATIC-CONFIRMED** by `Docs/RE/specs/screen_effects.md §4` (wave-7, IDB f61f66a9). The nine state names (DEFAULT / CHOICE / HIT / ALPHA / HIDDEN / POISON / TYPE / ANGER / AUTO) are confirmed from `display.lua` key suffixes; MULTI channels are halved ×0.5 at load; ADD/ALPHA are raw. Non-identity authored palette values remain `[debugger-confirm]`. See §4.3 (updated). |
| 6 | power1dx8.psh bind site | The VFS path for the glow PS is stored on the renderer; the call site where it is bound during the blur step is inside the blur helper function (not fully traced in this pass). |

### 11.2 Open questions (escalated to RE / shader analysis domain)

| # | Open item | Evidence and escalation path |
|---|---|---|
| 1 | Outline / silhouette mechanism | No geometric back-face-expand or two-pass CCW/CW outline pass was found in `doida.exe`. Cel edges arise from ramp banding and any edge-detection logic inside the external `.psh` files. The definitive answer requires disassembling the VFS token streams in `data/shader/dotoonshading.psh` and `finaldx8.psh`; route to RE / shader analysis. |
| 2 | Shadow → character silhouette pass | The ShadowManager draws the actor as an un-shaded silhouette mesh into the projector RT via the fixed-function cull-drawable slot. The projector RT bind/clear wrapper, depth states, and whether a stencil write is involved were not fully traced in this pass. |
| 3 | Rim / specular term | Specular-related configuration strings exist in the binary and the cel VS carries a light-direction constant; no dedicated rim or specular term was isolated in `doida.exe`. Any such contribution would be inside the external cel shader files. |
| 4 | Toon-ramp texture dimensions and ramp axis | `toonramp.bmp` is loaded via the standard texture loader with CLAMP on sampler 1; whether it is a 1D gradient or a 2D LUT, and how its U/V axes map to luminance and any secondary parameter, requires inspection of the VFS asset. |
