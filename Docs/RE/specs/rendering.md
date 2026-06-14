# Spec: 3D Render Pipeline — Per-Frame Draw Loop, Draw Order, Render-State Cache, and Glow/Bloom Post Chain

> Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by the
> Godot presentation/render engineers (layer 05) and by `Assets.Mapping`. The cel/glow shaders
> that implement these passes are documented in `formats/shaders.md`; this spec describes the
> *pipeline that binds and orders them*. Every render-pipeline constant an engineer cites must
> reference this file: `// spec: Docs/RE/specs/rendering.md`.

---

## Status

| Item | Value |
|------|-------|
| Confidence model | CONFIRMED = behavior recovered from static analysis of the draw machinery; PLAUSIBLE = structurally inferred from surrounding code but not fully unwound; UNVERIFIED = not provable from the path read, would need a live/debugger or on-disk asset confirmation. |
| Graphics API | Direct3D 9 (`IDirect3DDevice9`). The engine is a thin wrapper over a single device. |
| Per-frame draw loop (direct + offscreen paths, four ordered scene callbacks, cull walk) | CONFIRMED. See §2. |
| Draw order verdict (opaque → alpha-test → transparent → effects → post → UI, no depth sort) | CONFIRMED. See §3. |
| Render-state cache (18-slot lazy compare-and-apply) | CONFIRMED (mechanism). See §4. |
| Per-bucket render-state matrix | CONFIRMED. D3DBLEND enum bytes now byte-verified; per-bucket Z-write now byte-verified (two transparent buckets DO write depth). See §4.2. |
| Per-object material/texture binding (cel = skinned actors only, gated on post-process flag) | CONFIRMED. See §5. |
| Per-class vertex stride / FVF | CONFIRMED for world geometry (terrain/building/static/skinned = 32-byte XYZ+N+UV) and FX (24-byte XYZ+DIFFUSE+UV). Particle/UI strides PLAUSIBLE. See §5.2. |
| Glow/bloom post chain (3 render targets, ordered pass list) | CONFIRMED (load + execution). No bright-pass threshold; single blur pass; present is an opaque copy; composite weights config-driven. See §6. |

---

## 1. Orientation — the two principal engine objects

Two objects recur throughout the pipeline; understanding their roles makes the whole flow legible.

1. **The renderer device wrapper** — a thin object that owns the single cached `IDirect3DDevice9`
   interface. A family of convenience methods (set source/destination blend, enable/disable Z-test,
   enable/disable Z-write, disable lighting, set transforms, draw indexed primitive, begin/end
   scene, present, clear, bind texture-or-default) all forward to this one device. The
   source-blend, destination-blend, Z-test, and Z-write setters are each a thin pass-through that
   forwards a bare integer straight to `SetRenderState` for the corresponding `D3DRENDERSTATETYPE`
   — the argument is **not transformed**, so any integer a bucket passes to those setters is the
   literal Direct3D enum/boolean value the device receives (this is what makes the blend and
   Z-write bytes below byte-verified, not merely inferred). Several engine globals lazily cache the
   same device pointer on first use; treat them as aliases of one device singleton.

2. **The scene/post-process engine object** — a very large object (on the order of 179 KB) created
   and zeroed once at startup. It holds: the backbuffer dimensions (default width 1024, height 768,
   32 bpp), the cached device, an **offscreen-RT / post-process enable flag** (default off → the
   direct path runs), the three post-process render targets with their surfaces and
   render-to-surface helpers, the compiled cel/composite/glow shader handles, the glow downsample
   divisors (default 2, 2), the per-tone glow modulation constants, and the editable default
   glow-shader filename slot (pre-filled with `data/shader/power1dx8.psh`).

The render-state objects form an **18-slot render-state cache** (see §4): a base render-state class
with one subclass per logical Direct3D state group (blending, cull mode, depth test, depth mask,
alpha test, transparency, lighting, material, fog, color mask, shade model, fill mode, dithering,
texture factor, highlight, line pattern, …). Each slot is polymorphic with `equals`/`apply` virtual
methods.

---

## 2. Per-frame scene draw loop

**Confidence: CONFIRMED.**

The top-level draw dispatcher reads the offscreen-enable flag on the scene/post object. If set, it
runs the **offscreen render-to-texture / post-process path** (the glow chain, §6); otherwise it runs
the **direct path** (draws straight to the backbuffer with no post-process). Both paths drive the
same scene content through **four scene callbacks** stored on the draw object, fired in this fixed
order:

| Order | Callback role | Bucket it draws |
|------:|---------------|-----------------|
| 1 | Pre-scene / sky callbacks (two slots) | Background, sky-dome — run first, with the view transform set. |
| 2 | Main scene-root draw callback | Opaque world + characters + ground/actor shadows (§3). |
| 3 | FX / post-scene overlay callback | The transparent / effects bucket (§3). |
| 4 | UI / HUD callback | 2D window/HUD draw, followed by an optional FPS counter. |

### 2.1 Direct path, phase order

1. Snapshot the backbuffer viewport, set the viewport, clear with the clear color, begin scene.
2. Set the View transform and run the pre-scene / sky callbacks.
3. Configure texture stages 0/1/2 to a default modulate plus sampler filters.
4. Run the camera / cull-root object (one of two candidate cull roots selected by a debug flag): this
   performs **frustum-plane culling** and emits the draw-item list.
5. Main scene-root draw (opaque + shadows).
6. FX overlay draw (transparent + particles).
7. UI / HUD callback; optional FPS counter; end scene.
8. Accumulate average cull-time / draw-time profiling. The actual `Present` happens in the outer
   frame driver.

### 2.2 Scene-graph cull and walk

The scene-graph walk packs the projection parameters, runs a frustum-plane cull + tree walk, then
`accept`s a visitor with a visibility mask of `(1 << planeCount) - 1`. The cull pipeline's draw stage
iterates each emitted draw-item: sets the per-item world transform, applies the per-item
light/material (driving the material/lighting cache slots), dispatches to one of two per-item draw
routines selected by a per-item flag (indexed vs non-indexed primitive draw), then **force-commits
the entire 18-slot render-state cache** so the device matches the cache exactly before the next item.

### 2.3 Offscreen path

The offscreen path wraps this same scene draw inside a render-to-texture capture and appends the
post-process passes — see §6.

---

## 3. Draw order

**Confidence: CONFIRMED.**

The frame draws buckets in a fixed order and relies on **render-state buckets + blend modes** for
correctness, **not** on a per-object depth sort.

### 3.1 Verdict

```
opaque  →  alpha-test  →  transparent  →  effects  →  post  →  UI
```

**There is no back-to-front per-object or per-particle depth sort.** Opaque correctness comes from
Z-test / Z-write; transparency correctness comes purely from the bucket draw order plus the blend
modes (and, for two FX buckets, from those buckets also writing depth — see §4.2). Particles and FX
are drawn in **linked-list / insertion order** and rely on additive or alpha blending to look
correct. The scene graph buckets opaque vs transparent **by render-state object identity** (the
render-state cache diff), not by sorting geometry.

### 3.2 Opaque sub-order (main scene-root callback)

1. **Ground-shadow stamp** — stamps the projected shadow gradient onto terrain cells; sets the color
   and alpha operations for the shadow modulate.
2. **Opaque world** — lighting ON, Z-test ON, Z-write ON, alpha-blend OFF, stage-0 modulate.
   Sub-draws, in order: terrain → buildings / static meshes → world objects → characters / actors.
   This is the canonical Z-writing bucket, but **not** the only one (the 2×/multiply and
   water/reflective FX buckets also write depth — see §4.2).
3. **Actor blob shadows** (when the ground-shadow flag is set) — a projective-texture pass: a shadow
   texture-coordinate matrix bound to a high texture stage, texcoord generation, alpha-blend
   source-alpha / inverse-source-alpha. Render state is restored afterward.

### 3.3 Transparent / effects sub-order (FX overlay callback)

| Order | Sub-bucket | Blend intent |
|------:|-----------|--------------|
| 1 | Fog / additive terrain-overlay FX | alpha-test ON, texture-factor fog color, `ONE / INVSRCCOLOR` |
| 2 | Billboards | `SRCALPHA / INVSRCALPHA` |
| 3 | Multiply / 2× FX layer | `DESTCOLOR / SRCALPHA` (modulate-by-destination, weighted by source alpha) |
| 4 | Water / reflective FX | `SRCALPHA / INVSRCALPHA` |
| 5 | Particles | alpha-test ON; default `SRCALPHA / INVSRCALPHA`, with a `SRCALPHA / ONE` additive override; no depth sort |
| 6 | Final additive switch | `SRCALPHA / ONE` followed by a closing FX draw |

After the transparent buckets, the UI / HUD callback and (offscreen path only) the post-process
composite close the frame.

---

## 4. Render-state cache

### 4.1 Mechanism

**Confidence: CONFIRMED.**

The 18-slot render-state cache is driven by two routines:

- **Lazy compare-and-apply (per draw-item):** walk all 18 slots; for each slot, if its `equals`
  comparison against the candidate is false, copy the candidate into the cache, call its `apply`
  (which pushes the value to the device), and bump a "state changes" statistic. Unchanged slots are
  skipped — this minimizes redundant device state calls.
- **Force-commit (bind-all):** copy all 18 candidate slots into the cache and unconditionally `apply`
  every one. This is the force-commit used at the start of the cull draw stage so the device matches
  the cache exactly.

In practice the per-bucket convenience wrappers also set individual device render states directly
(enable/disable Z-test, enable/disable Z-write, enable/disable lighting, set alpha-blend enable, set
source/destination blend, set fill mode, set fog enable, set Z compare function, set texture-stage
state, set sampler state). Cull mode is owned by its dedicated cache slot. Note: in the FX overlay
the per-bucket Z-write toggle is driven **imperatively** by the enable/disable-Z-write setters (it is
not routed through a depth-mask cache slot), which is why Z-write must be read per bucket rather than
assumed from a slot default — see §4.2.

### 4.2 Per-bucket render-state matrix

**Confidence: CONFIRMED.** The named source/destination blend pairs are now **byte-verified**
Direct3D `D3DBLEND` enum values (the blend setters forward the bare integer straight to
`SetRenderState`, so the integer in each bucket setup *is* the enum byte). Per-bucket **Z-write** is
also byte-verified: it is toggled explicitly per bucket, and the earlier assumption that transparent
buckets inherit Z-write OFF is **REFUTED** — two transparent buckets (2×/multiply and
water/reflective) enable Z-write.

| Bucket | Z-test | Z-write | Lighting | Alpha-blend (Src / Dest) | Alpha-test | Notes |
|--------|:------:|:-------:|:--------:|--------------------------|:----------:|-------|
| Ground-shadow stamp | ON | ON (opaque default) | — | modulate via color/alpha ops | — | stamps shadow gradient onto terrain |
| Opaque world (terrain / buildings / objects / actors) | ON | **ON** | ON | OFF | off | stage-0 modulate; canonical (but not sole) Z-writer |
| Actor blob shadows | ON | ON (opaque default) | — | SRCALPHA / INVSRCALPHA | — | projective texture matrix on a high stage |
| FX: fog / terrain overlay | ON | **OFF** | OFF | ONE / INVSRCCOLOR | ON | texture-factor fog color |
| FX: billboards | ON | **OFF** | OFF | SRCALPHA / INVSRCALPHA | varies | inherits the Z-write-OFF set at the start of the FX overlay |
| FX: 2× / multiply layer | ON | **ON** | OFF | DESTCOLOR / SRCALPHA | — | modulate-by-destination, weighted by source alpha; Z-write re-enabled for this bucket |
| FX: water / reflective | ON | **ON** | OFF | SRCALPHA / INVSRCALPHA | — | reflective FX draw; Z-write ON |
| FX: particles | ON | **OFF** | OFF | SRCALPHA / INVSRCALPHA default; SRCALPHA / ONE additive override | ON | Z-write disabled again before particles; no back-to-front sort |
| Post: glow extract / bloom blur | OFF | OFF | OFF | OFF (extract); blur via glow PS | off | fullscreen quad |
| Post: final composite | OFF | OFF | OFF | OFF (composite into RT) | off | composite / glow PS; see §6 |
| Post: present | OFF | OFF | OFF | ONE / ZERO (opaque copy to backbuffer) | off | straight blit of the composited RT; see §6 |
| UI / HUD | OFF | OFF | OFF | SRCALPHA / INVSRCALPHA | — | 2D ortho |

**Blend-factor note (CONFIRMED, byte-verified).** The source/destination blend setters pass their
integer argument through unchanged to the Direct3D source-blend / destination-blend render states, so
the pairs above are the literal `D3DBLEND` enum values the device sees. The relevant enum values
(standard Direct3D 9): `ZERO`, `ONE`, `SRCCOLOR`, `INVSRCCOLOR`, `SRCALPHA`, `INVSRCALPHA`,
`DESTCOLOR`.

**Correction — 2×/multiply layer destination factor.** The destination factor for the 2×/multiply
layer is **`SRCALPHA`**, not `SRCCOLOR` as an earlier intent-label read suggested. The byte-verified
pair is **src = `DESTCOLOR`, dest = `SRCALPHA`** (a modulate-by-destination weighted by source
alpha). The "2×" naming comes from the `DESTCOLOR` source factor combined with a MODULATE2X color
operation in the texture-stage setup, **not** from a `SRCCOLOR` destination factor.

**Z-write note (CONFIRMED, byte-verified — prior "inherit" assumption REFUTED).** Z-write is set
imperatively per bucket. The opaque world enables Z-write; the FX overlay disables Z-write at its
start (so fog/terrain-overlay and billboards draw with Z-write OFF), then **re-enables** Z-write for
the 2×/multiply layer and again for the water/reflective bucket, then **disables** it again before
particles. So the opaque bucket is **not the sole Z-writer** — the 2×/multiply and water/reflective
FX buckets also write depth, deliberately letting those surfaces occlude later transparent FX, while
billboards and particles remain depth-read-only.

---

## 5. Per-object material / texture binding

### 5.1 Binding mechanism

**Confidence: CONFIRMED.**

Material and texture binding is **per draw-item**, immediately before that item's primitive draw:

- The object's texture descriptor (the engine's lazily-loaded texture handle, which holds an
  `IDirect3DTexture9` once loaded from the VFS-or-disk loader) is bound to a texture stage; a
  separate path clears the stage when no texture applies.
- The per-item light/material is applied during the cull draw stage, driving the material and
  lighting cache slots.
- Texture-stage state (modulate color/alpha operations, filters) is set **per bucket** before that
  bucket's items draw.

### 5.1a Cel material binding — skinned actors only, gated on the post-process flag

**Confidence: CONFIRMED.**

The cel/toon shader pair (cel vertex shader + cel pixel shader, with the toon-ramp texture bound to a
secondary stage) is bound from **exactly one** site, and that site has **exactly one** caller — the
bone-blended **skinned-actor** render routine. The binding is keyed purely on the object's runtime
class being the skinned (bone-blended) mesh class, **not** on player-vs-mob identity:

| Object class | Cel shader? |
|--------------|:-----------:|
| **Skinned actor — player AND skinned mobs/NPCs** | **YES — cel** (both take the identical cel path) |
| Static posed mesh (non-skinned actor variant) | NO — fixed-function |
| Terrain patch | NO — fixed-function |
| Building / world static mesh | NO — fixed-function |
| FX / billboard / particle / water | NO — fixed-function |

So terrain, buildings, and static props are drawn **unshaded** (fixed-function) and never touch the
cel handles — CONFIRMED. The player and skinned mobs share the identical cel path because both are
instances of the same skinned class; only **static (non-skinned) props bypass it** — CONFIRMED.

**Gating nuance (CONFIRMED).** The cel path is **gated on the offscreen / post-process enable flag**
— the same flag §2 uses to choose the direct vs offscreen path. With post-process **OFF**, even a
skinned actor falls back to its **fixed-function** draw (no cel shader, no composite world-view-proj
upload). With post-process **ON**, the skinned actor builds the composite world-view-projection
matrix, uploads it to the cel vertex shader as a single matrix constant (four vec4 registers — this
is a transform matrix, **not** a bone palette), binds the cel pair, and draws from the stride-32
skinned vertex buffer. **The toon look is therefore coupled to the post-process feature**: turning
post-process off removes the cel shading from skinned actors. This matters for Godot fidelity.

### 5.2 Vertex declaration / FVF per object type

**Confidence: CONFIRMED for world geometry and FX strides; particle/UI strides PLAUSIBLE.**

All world geometry shares a single **32-byte** vertex contract — `position | normal | 1 texcoord` =
XYZ (12 bytes) + NORMAL (12 bytes) + UV (8 bytes). The FX bucket uses a **24-byte**
`position | diffuse | 1 texcoord` contract — XYZ (12) + DIFFUSE (4) + UV (8).

| Object type | Vertex layout | Stride | Confidence |
|-------------|---------------|:------:|------------|
| **Skinned character (cel path, post ON)** | POSITION (XYZ) at offset 0, NORMAL at offset 12, TEXCOORD0 at offset 24, with TEXCOORD1 carrying the per-vertex N·L luminance coordinate written by the skinning stage. This is the post-skinning vertex buffer the cel shader consumes. | **32** | CONFIRMED (declaration + stream stride) |
| **Skinned actor (fixed-function fallback, post OFF)** | `position | normal | diffuse | 2 texcoord` declared at the bucket level; reuses the same 32-byte skinned vertex buffer (the declared format over-declares texcoords). | 32 (reused VB) | CONFIRMED (declared format); fallback stride not separately re-read |
| **Terrain patch** | `position | normal | 1 texcoord` — XYZ + NORMAL + UV. | **32** | CONFIRMED (32× batching multiplier) |
| **Building / static world mesh** | `position | normal | 1 texcoord` — XYZ + NORMAL + UV. | **32** | CONFIRMED (format); stride consistent with the 32-byte contract |
| **Static posed mesh** | `position | normal | 1 texcoord` — XYZ + NORMAL + UV. | **32** | CONFIRMED (literal 32 stride to the draw call) |
| **FX / fog / terrain-overlay** | `position | diffuse | 1 texcoord` — XYZ (12) + DIFFUSE (4) + UV (8). | **24** | CONFIRMED (declared format) |
| **Billboard / particle / water** | Inherits the FX `position | diffuse | 1 texcoord` format within the FX overlay; particles batch through a shared dynamic vertex buffer. | ~24 | PLAUSIBLE (no separate per-bucket override observed; particle VB stride not byte-read) |
| **UI / 2D HUD** | Pre-transformed (RHW) position + diffuse + texcoord, drawn under an orthographic projection. | — | PLAUSIBLE (not opened this pass) |

> The skinned-character cel layout is the contract for the cel toon shader — see
> `formats/shaders.md` (TEXCOORD1 carries the interpolated N·L luminance that keys the toon ramp
> lookup). `// spec: Docs/RE/formats/shaders.md`

---

## 6. Glow / bloom post chain

**Confidence: CONFIRMED (load + execution). There is no bright-pass threshold; a single blur pass
runs; the present is an opaque copy; the composite weights are config-driven.**

The post-process path runs only when the offscreen-RT flag is set. It uses **three render targets**,
each created at backbuffer dimensions (with a 1024×1024 fallback on creation failure), each paired
with a surface and a render-to-surface helper.

### 6.1 Render targets

| Render target | Dimensions | Role |
|---------------|------------|------|
| TEX0 — scene capture | backbuffer dims | The cel/scene RT the whole world is drawn into; also the composite destination and the present source. |
| TEX1 — bright / edge extract | backbuffer dims | A plain copy of the scene RT (no thresholding — see §6.4). Consumed only by the composite. |
| TEX2 — bloom blur | downscaled (backbuffer ÷ the glow divisors, default ÷2 each) | The blurred, downsampled glow buffer. |

The toon ramp LUT (`data/shader/toonramp.bmp`) is loaded into a separate slot for the cel pass — see
`formats/shaders.md`. A separate shadow-map render target (built from a shadow texture asset, sized
to a tiered quality cap) is used by the ground/actor shadow buckets and is **not** part of the glow
chain.

### 6.2 Ordered pass list

Each pass is `source → operation → destination`:

| Pass | Source | Operation | Destination |
|-----:|--------|-----------|-------------|
| 1 — Capture | scene | Bind the capture render target; set view/camera; draw the whole scene into the RT; resolve. | TEX0 |
| 2 — Bright/edge extract | TEX0 | Clear to opaque black; fullscreen ortho quad, Z/lighting/blend OFF; **no pixel shader, no alpha-test, no threshold** — a plain fixed-function copy of the scene RT (see §6.4). | TEX1 |
| 3 — Bloom blur | TEX0 | Fullscreen ortho quad sized to the downscaled dims; glow-blur pixel shader. Samples the full-res scene RT and writes the downscaled result. | TEX2 (downscaled) |
| 4 — Composite | TEX1 + TEX2 | Ortho fullscreen quad over the texture stages; composite/glow pixel shader. Stage 0 ← TEX1 (the bright/edge extract); stage 1 ← TEX2 (the bloom blur). Two scalar pixel-shader constants are uploaded from code (c0, c1 — see §6.3). The FX overlay callback then runs into this RT. | TEX0 |
| 5 — Present | TEX0 | Clear; begin scene; ortho; **alpha-blend enabled but ONE / ZERO (an opaque blit, NOT additive)**; bind the composited RT and draw a fullscreen quad. | backbuffer |
| 6 — UI / HUD | backbuffer | UI/HUD callback, optional FPS counter, end scene, accumulate profiling. | (present) |

Compact view:

```
scene        → capture               → TEX0
TEX0         → bright/edge copy       → TEX1   (no threshold; plain copy)
TEX0         → glow-blur PS           → TEX2   (downscaled ÷ divisors)
TEX1 + TEX2  → composite PS (c0,c1)   → TEX0   (the additive "glow add" happens HERE)
TEX0         → opaque blit (ONE/ZERO) → BACKBUFFER
BACKBUFFER   → UI/HUD callback        → (present)
```

The composite/blur pixel shaders are documented in `formats/shaders.md`
(`finaldx8.psh` composite, `power1dx8.psh` glow blur). `// spec: Docs/RE/formats/shaders.md`

### 6.3 Composite weights, downsample divisors, present blend

**Downsample divisors (CONFIRMED).** Default **(2, 2)**. They are config-driven from the external
display-config globals (the glow range X / Y settings), with a **hardcoded fallback of 2** applied
per axis when the configured value is zero or absent. TEX2 is rendered at `backbuffer ÷ divisor` on
each axis.

**Composite weights c0 / c1 (CONFIRMED that they are code-uploaded).** The composite pass uploads two
pixel-shader scalar constants from code, broadcast 4-wide:

- **c0** = (base-bright-multiplier × 0.5)
- **c1** = (glow-bright-multiplier × 0.5)

Both multipliers default to **1.0** (so the default uploaded values are **c0 = c1 = 0.5**), and are
sourced from the external display config; the ×0.5 is a real code-side multiply, not a shader-side
one. These are the **two tunable knobs** for the glow (base brightness and glow brightness) — they
are **not** hardwired white. Stage 0 binds the bright/edge-extract RT and stage 1 binds the bloom RT,
consistent with a `saturate(2·edge·c0 + bloom·c1)`-style composite. The exact pixel-shader arithmetic
(the `2×`, the `saturate`, any embedded constants) lives in the external `.psh` and is documented in
`formats/shaders.md` — only the c0 / c1 scalars come from code.

**Present blend (CONFIRMED, corrects earlier "additive present" wording).** The present pass blits
the composited TEX0 to the backbuffer with **source = ONE, destination = ZERO** — i.e. a straight
**opaque copy**, not an additive (ONE / ONE) blend. Alpha-blend is enabled, but the ONE / ZERO factor
pair makes it a copy. The additive "glow add" math is performed **inside the composite pixel shader
into TEX0** (pass 4); the present (pass 5) merely copies that result to the backbuffer. Do not
re-add at present time.

### 6.4 Bright-pass threshold and power-chain depth (CONFIRMED)

**Bright-pass threshold: there is NONE (CONFIRMED).** The bright/edge-extract pass (pass 2) clears
TEX1 to opaque black and performs a **plain fixed-function copy** of the scene RT into TEX1 at full
backbuffer size. It binds **no pixel shader**, sets **no alpha-test / alpha-reference**, applies
**no subtract**, and uploads **no texture-factor scale**. There is no luminance cutoff of any kind —
every pixel of the scene feeds the blur. The glow's selectivity comes entirely from the composite
scalars (c0 / c1) and the external glow-shader's own multiply, **not** from a bright threshold.

**Power-chain depth: a single blur pass; only one glow shader runs (CONFIRMED).** Only the
`power1dx8` glow pixel shader is ever loaded by a stock client, and exactly **one blur pass** runs. A
byte-level search of the executable finds **zero occurrences** of any `power2` / `power4` shader
literal — neither exists in the binary. The editable glow-shader filename slot is pre-filled with the
`power1dx8` path and can be overwritten **only** by an external display-config string (a dedicated
power-shader key). A multi-tap `power2 → power4` chain is therefore **not a code path**: it could
only run if the external display config explicitly named such a path *and* that file existed on disk;
the executable itself never names or sequences them. This resolves the previously-UNVERIFIED
multi-tap chain depth to **single-tap, CONFIRMED**.

---

## 7. Known unknowns

- **Particle vertex-buffer stride (exact bytes)** — the particle batch uses a shared dynamic vertex
  buffer; its format is assumed to inherit the FX 24-byte `position | diffuse | 1 texcoord` layout,
  but the particle VB's own stride was not byte-read. PLAUSIBLE.
- **UI / 2D HUD FVF + stride** — the pre-transformed (RHW) position + diffuse + texcoord format is
  the standard assumption but was not byte-confirmed this pass. PLAUSIBLE.
- **Skinned-actor fixed-function fallback stride (post-OFF path)** — the cel-off fallback draw likely
  reuses the same 32-byte skinned vertex buffer, but its draw routine was not separately opened.
  PLAUSIBLE.
- **Ground-shadow stamp / actor-blob-shadow exact Z-write state** — both run inside the opaque bucket
  and are assumed to inherit Z-write ON from the opaque setup; whether the blob-shadow projective
  pass re-toggles the depth mask was not separately traced. PLAUSIBLE.
- **External pixel-shader internals** — the blur shader's own scale constant and the composite
  shader's exact arithmetic (`2×`, `saturate`, any embedded constants) live only in the on-disk
  `data/shader/*.psh` files, not in the executable; recoverable only by tracing the client VFS shader
  sources. UNVERIFIED-from-binary.
- **Live display-config values** — the actual shipped glow-range divisors, base/glow brightness
  multipliers, and any power-shader override live in the on-disk display-config script, not in the
  binary; the binary defines only the defaults (2,2 / 1.0 / `power1dx8`) and the ×0.5 scaling.
  UNVERIFIED-from-binary.
- **Internal per-primitive logic of the opaque sub-draws** (terrain / buildings / objects / actors)
  — their role and order in the opaque bucket is confirmed; their internal logic was out of scope.
- **Whether terrain actually populates the NORMAL field** — the world geometry format declares a
  normal, but whether the terrain vertex copy writes it or zeroes it was not opened. PLAUSIBLE.

---

## 8. Godot re-implementation guidance (N2)

- The glow/bloom is a **3-RT chain**: scene capture → bright/edge copy (no threshold) → single blur
  (downscaled ÷2) → composite into the scene RT → opaque present. A Godot `WorldEnvironment` Glow can
  stand in: set the glow **HDR threshold ≈ 0** (there is no bright cutoff in the original), use
  **one** effective blur level (a single half-res blur — do not stack a multi-level Gaussian
  pyramid), and treat the glow base/intensity knobs as the two composite scalars (base-bright and
  glow-bright, default 0.5 each after the ×0.5). The additive "glow add" happens inside the composite
  before present, so use the additive glow blend for the contribution but do **not** double-add at
  present. The cel/toon look needs the toon ramp material from `formats/shaders.md`.
- Bind the cel/toon material **only to skinned-actor meshes** (player + skinned mobs); leave terrain,
  buildings, and static props unshaded / fixed-function-equivalent. Remember the original couples the
  toon look to the post-process feature flag — with post off, even skinned actors render unshaded.
- Mesh stride contract: **world geometry** (terrain / building / static / skinned) = **32-byte**
  XYZ (12) + NORMAL (12) + UV (8); **FX / billboard / particle** = **24-byte** XYZ (12) + DIFFUSE (4)
  + UV (8).
- Reproduce the **draw order** exactly: opaque (Z-test on, Z-write on, lighting on) → transparent
  buckets in the order fog/terrain-overlay → billboard → 2× → water → particle, with **no
  back-to-front sort** and **additive particles**. Reproduce the **per-bucket Z-write**: opaque ON;
  fog / billboard / particle OFF; **2× and water ON**. Use the byte-exact blend pairs from §4.2 —
  note the 2×/multiply layer is `DESTCOLOR / SRCALPHA`. Getting the order, the Z-write per bucket, or
  the blend modes wrong changes the look even with the same textures.
- The skinned character feeds the cel shader through the stride-32 layout (§5.2); pair this spec with
  `formats/shaders.md` and `specs/skinning.md`.

---

## Cross-references

- `formats/shaders.md` — the cel / composite / glow shader roles, the toon ramp LUT, and the
  recovered vertex-shader constants (including the BT.601 luma weights that key the ramp). The
  external `.psh` arithmetic (blur scale, composite `2·edge·c0 + bloom·c1`) lives there, not here.
- `specs/skinning.md` — how the skinned-character vertex buffer this pipeline draws is produced.
- `specs/effects.md`, `specs/world_systems.md` — the FX/particle and world buckets this loop draws
  (owned by other authors).
- Glossary: see `Docs/RE/names.yaml`.
- Provenance: see `Docs/RE/journal.md`.
