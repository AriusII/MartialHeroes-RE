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
| Per-bucket render-state matrix | CONFIRMED for explicit toggles; per-bucket Z-write toggling PLAUSIBLE; D3DBLEND enum bytes PLAUSIBLE. See §4.2. |
| Per-object material/texture binding | CONFIRMED. See §5. |
| Cel skinned-character vertex declaration (stride 32) | CONFIRMED. Other object-type vertex formats PLAUSIBLE. See §5.2. |
| Glow/bloom post chain (3 render targets, ordered pass list) | CONFIRMED (load + execution). Multi-tap power2/power4 chain depth UNVERIFIED. See §6. |

---

## 1. Orientation — the two principal engine objects

Two objects recur throughout the pipeline; understanding their roles makes the whole flow legible.

1. **The renderer device wrapper** — a thin object that owns the single cached `IDirect3DDevice9`
   interface. A family of convenience methods (set source/destination blend, enable/disable Z-test,
   disable lighting, set transforms, draw indexed primitive, begin/end scene, present, clear, bind
   texture-or-default) all forward to this one device. Several engine globals lazily cache the same
   device pointer on first use; treat them as aliases of one device singleton.

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
modes. Particles and FX are drawn in **linked-list / insertion order** and rely on additive or alpha
blending to look correct. The scene graph buckets opaque vs transparent **by render-state object
identity** (the render-state cache diff), not by sorting geometry.

### 3.2 Opaque sub-order (main scene-root callback)

1. **Ground-shadow stamp** — stamps the projected shadow gradient onto terrain cells; sets the color
   and alpha operations for the shadow modulate.
2. **Opaque world** — lighting ON, Z-test ON, alpha-blend OFF, stage-0 modulate. Sub-draws, in order:
   terrain → buildings / static meshes → world objects → characters / actors. This is the **only
   Z-writing bucket** (PLAUSIBLE; see §4.2).
3. **Actor blob shadows** (when the ground-shadow flag is set) — a projective-texture pass: a shadow
   texture-coordinate matrix bound to a high texture stage, texcoord generation, alpha-blend
   source-alpha / inverse-source-alpha. Render state is restored afterward.

### 3.3 Transparent / effects sub-order (FX overlay callback)

| Order | Sub-bucket | Blend intent |
|------:|-----------|--------------|
| 1 | Fog / additive terrain-overlay FX | alpha-test ON, texture-factor fog color, additive `ONE / INVSRCCOLOR` |
| 2 | Billboards | `SRCALPHA / INVSRCALPHA` |
| 3 | Multiply / 2× FX layer | `DESTCOLOR / SRCCOLOR` (modulate-2× style) |
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
(enable/disable Z-test, enable/disable lighting, set alpha-blend enable, set source/destination
blend, set fill mode, set fog enable, set Z compare function, set texture-stage state, set sampler
state). Cull mode is owned by its dedicated cache slot.

### 4.2 Per-bucket render-state matrix

**Confidence: CONFIRMED for explicit toggles; per-bucket Z-write PLAUSIBLE; blend enum bytes
PLAUSIBLE.**

| Bucket | Z-test | Z-write | Lighting | Alpha-blend (Src / Dest) | Alpha-test | Notes |
|--------|:------:|:-------:|:--------:|--------------------------|:----------:|-------|
| Ground-shadow stamp | on | (default on) | — | modulate via color/alpha ops | — | stamps shadow gradient onto terrain |
| Opaque world (terrain / buildings / objects / actors) | ON | ON | ON | OFF | off | stage-0 modulate; the only Z-writing bucket |
| Actor blob shadows | on | (default) | — | SRCALPHA / INVSRCALPHA | — | projective texture matrix on a high stage |
| FX: fog / terrain overlay | on | (likely off) | OFF | ONE / INVSRCCOLOR | ON | texture-factor fog color |
| FX: billboards | on | off | OFF | SRCALPHA / INVSRCALPHA | varies | billboard batch |
| FX: 2× / multiply layer | on | off | OFF | DESTCOLOR / SRCCOLOR | — | modulate-2× style |
| FX: water / reflective | on | off | OFF | SRCALPHA / INVSRCALPHA | — | reflective FX draw |
| FX: particles | on | off | OFF | SRCALPHA / INVSRCALPHA default; SRCALPHA / ONE additive override | ON | no back-to-front sort |
| Post: glow extract / bloom blur | OFF | OFF | OFF | OFF (extract); blur via glow PS | off | fullscreen quad |
| Post: final composite | OFF | OFF | OFF | OFF (RT pass) → additive (backbuffer present) | off | composite / glow PS |
| UI / HUD | OFF | OFF | OFF | SRCALPHA / INVSRCALPHA | — | 2D ortho |

**Blend-factor note (PLAUSIBLE).** The named source/destination blend pairs are the engine's
*intent*, read from the bucket setup code. The wrapper indirection between the convenience setters
and the literal `D3DBLEND` enum value was not fully unwound, so the named pairs above are
high-confidence-from-context but **not byte-verified** Direct3D enum values. Treat them as the
intended blend behavior, not as confirmed enum bytes.

**Z-write note (PLAUSIBLE).** The bucket setups toggle Z-test, lighting, and blend explicitly but do
not visibly re-toggle Z-write per bucket. Transparent buckets are therefore *assumed* to inherit
Z-write OFF from a default not captured in the setup code; the opaque bucket is assumed to be the
sole Z-writer. The depth-mask cache slot's default value would confirm this.

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

### 5.2 Vertex declaration per object type

| Object type | Vertex layout | Confidence |
|-------------|---------------|------------|
| **Skinned character (cel path)** | Stream 0, **stride 32 bytes**: POSITION (XYZ) at offset 0, NORMAL at offset 12, TEXCOORD0 at offset 24, with TEXCOORD1 carrying the per-vertex N·L luminance coordinate written by the skinning stage. This is the post-skinning vertex buffer the cel shader consumes. | CONFIRMED (from the declaration bytes) |
| Terrain | Positional + multi-texcoord layout for the per-patch multi-texture blend. | PLAUSIBLE (exact bits not decoded) |
| Effects / billboards / particles | Transformed-or-untransformed position + diffuse + one texcoord; particles batched through a shared dynamic vertex buffer. | PLAUSIBLE |
| UI / 2D | Pre-transformed (RHW) position + diffuse + texcoord, drawn under an orthographic projection. | PLAUSIBLE |

> The skinned-character cel layout is the contract for the cel toon shader — see
> `formats/shaders.md` (TEXCOORD1 carries the interpolated N·L luminance that keys the toon ramp
> lookup). `// spec: Docs/RE/formats/shaders.md`

---

## 6. Glow / bloom post chain

**Confidence: CONFIRMED (load + execution). Multi-tap power2/power4 chain depth UNVERIFIED.**

The post-process path runs only when the offscreen-RT flag is set. It uses **three render targets**,
each created at backbuffer dimensions (with a 1024×1024 fallback on creation failure), each paired
with a surface and a render-to-surface helper.

### 6.1 Render targets

| Render target | Dimensions | Role |
|---------------|------------|------|
| TEX0 — scene capture | backbuffer dims | The cel/scene RT the whole world is drawn into. |
| TEX1 — bright / glow extract | backbuffer dims | The bright-pass extract source for the bloom. |
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
| 2 — Glow extract | TEX0 | Fullscreen ortho quad, Z/lighting/blend OFF; fixed-function bright-pass. | TEX1 |
| 3 — Bloom blur | TEX0 | Fullscreen ortho quad sized to the downscaled dims; glow-blur pixel shader. | TEX2 (downscaled) |
| 4 — Composite | TEX1 + TEX2 | Ortho fullscreen quad over 3 texture stages; composite (cel/edge) pixel shader with per-tone constants; binds the bright-extract RT to stage 0 and the bloom RT to stage 1. Then the per-frame cel shader constants are uploaded and the FX overlay callback runs into this RT. | TEX0 |
| 5 — Present | TEX0 | Clear; begin scene; ortho; additive blend; bind the composited RT and draw a fullscreen quad. | backbuffer |
| 6 — UI / HUD | backbuffer | UI/HUD callback, optional FPS counter, end scene, accumulate profiling. | (present) |

Compact view:

```
scene        → capture           → TEX0
TEX0         → bright extract     → TEX1
TEX0         → glow-blur PS       → TEX2 (downscaled)
TEX1 + TEX2  → composite PS       → TEX0
TEX0         → additive quad      → BACKBUFFER
BACKBUFFER   → UI/HUD callback    → (present)
```

The composite/blur pixel shaders are documented in `formats/shaders.md`
(`finaldx8.psh` composite, `power1dx8.psh` glow blur). `// spec: Docs/RE/formats/shaders.md`

### 6.3 Whole-frame ordered phase list (offscreen / post path)

1. RT capture begin → TEX0.
2. Pre-scene / sky callbacks.
3. **Opaque:** ground-shadow stamp → opaque world (terrain, buildings, objects, characters; Z on,
   lighting on, blend off) → actor blob shadows.
4. RT capture end (resolve TEX0).
5. **Glow extract:** TEX0 → TEX1.
6. **Bloom blur:** TEX0 → TEX2 (downscaled).
7. **Composite:** TEX1 + TEX2 → TEX0, then the FX overlay runs into TEX0.
8. **FX / transparent** (inside the overlay): fog/terrain overlay → billboards → 2× layer → water →
   particles. Alpha-test + transparent buckets, no depth sort.
9. Present composited TEX0 → backbuffer (additive fullscreen quad).
10. **UI / HUD** + FPS counter.
11. End scene / present (outer frame driver).

In the **direct** (no-post) path, the RT-capture and post passes (1, 4–7, 9) collapse: the world (3),
FX (8), and UI (10) draw straight to the backbuffer in the same relative order.

### 6.4 Power-chain depth (UNVERIFIED)

Only `power1dx8.psh` is statically wired as the default glow pixel shader (read from the editable
filename slot pre-filled with that path). Whether a multi-tap `power2` → `power4` down-sample chain
runs is **data-driven** (the filename slot can be overwritten at runtime before the loader runs) and
is therefore **not provable from the static load path**. The power2/power4 family exists as shader
files (see `formats/shaders.md`) but the chain depth is UNVERIFIED here.

---

## 7. Known unknowns

- **Exact `D3DBLEND` enum bytes** for the bucket blend pairs — the wrapper indirection was not
  unwound to literal enum values. The named pairs are intent, not byte-verified. PLAUSIBLE.
- **Per-bucket Z-write toggling** — transparent buckets are assumed to inherit Z-write OFF; the
  depth-mask cache slot default would confirm. PLAUSIBLE.
- **Non-cel vertex formats** — only the skinned-character cel declaration (stride 32) is
  byte-confirmed; terrain / billboard / particle / UI vertex layouts are inferred. PLAUSIBLE.
- **Power2/power4 multi-tap glow chain depth** — data-driven via the editable filename slot; not
  provable statically. UNVERIFIED.
- **Internal per-primitive logic of the opaque sub-draws** (terrain / buildings / objects / actors)
  — their role and order in the opaque bucket is confirmed; their internal logic was out of scope.

---

## 8. Godot re-implementation guidance (N2)

- The glow/bloom is a **3-RT chain**: scene capture → bright extract → blur (downscaled) → additive
  composite. A Godot `WorldEnvironment` Glow can stand in for the bright-extract + blur + additive
  composite; the cel/toon look needs the toon ramp material from `formats/shaders.md`.
- Reproduce the **draw order** exactly: opaque (Z on, lighting on) → transparent buckets in the order
  fog/terrain-overlay → billboard → 2× → water → particle, with **no back-to-front sort** and
  **additive particles**. Getting the order or the blend modes wrong changes the look even with the
  same textures.
- The skinned character feeds the cel shader through the stride-32 layout (§5.2); pair this spec with
  `formats/shaders.md` and `specs/skinning.md`.

---

## Cross-references

- `formats/shaders.md` — the cel / composite / glow shader roles, the toon ramp LUT, and the
  recovered vertex-shader constants (including the BT.601 luma weights that key the ramp).
- `specs/skinning.md` — how the skinned-character vertex buffer this pipeline draws is produced.
- `specs/effects.md`, `specs/world_systems.md` — the FX/particle and world buckets this loop draws
  (owned by other authors).
- Glossary: see `Docs/RE/names.yaml`.
- Provenance: see `Docs/RE/journal.md`.
