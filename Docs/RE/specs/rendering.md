# Spec: 3D Render Pipeline — Per-Frame Draw Loop, Draw Order, Render-State Cache, and Glow/Bloom Post Chain

> Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by the
> Godot presentation/render engineers (layer 05) and by `Assets.Mapping`. The cel/glow shaders
> that implement these passes are documented in `formats/shaders.md`; this spec describes the
> *pipeline that binds and orders them*. Every render-pipeline constant an engineer cites must
> reference this file: `// spec: Docs/RE/specs/rendering.md`.

> **Verification banner**
> - **verification:** *confirmed* for the device-step / Present / device-lost routine, the
>   offscreen-vs-direct fork, the four-(really-≥6)-callback firing order, the glow/bloom 3-RT
>   chain (plain bright-extract, single blur, TEX1+TEX2 composite), the cel-gating-on-post flag,
>   the RTTI camera setup, the per-frame Clear flags, the world/FX vertex strides, and — re-walked
>   2026-06-21 — the UI/HUD render-state row and the glow `.psh` selection — each
>   recovered by following the static control flow. *sample-verified* for the
>   `data/script/display.lua` glow/bloom config values (GLOW_BRIGHT_MULTI=0.3, GLOW_RANGE 1×1,
>   POWER=2→power2dx8.psh) and the `DISPLAY_CHAR_BRIGHT_*` 9-state character-tint table (§6.6,
>   §6.7) — read verbatim from the real VFS file. *static-hypothesis* for the c0/c1 ×0.5
>   derivation and 1.0 defaults, the per-scene frame-rate field's per-window default value, the
>   particle/UI vertex strides. *(2026-06-21: the glow `.psh` question is RESOLVED — see the C5 note
>   below and §6.4.)*
>   *capture/debugger-pending* for the present-time blend bytes (ONE/ZERO), the matrix major-order /
>   up-axis, and any on-screen colour verdict. *(2026-06-21 ASSET-FIDELITY re-walk: the 18-slot
>   render-state cache mechanism is re-confirmed, and the UI/HUD row of the per-bucket matrix is
>   re-walked and split into the in-game-HUD vs. front-end-overlay entry paths — see §4.1 / §4.2.)*
> - **ida_reverified:** 2026-06-21    *(ASSET-FIDELITY 2026-06-21 — UI render-state row split, auto-hide timer, glow `.psh` conflict resolved; on CYCLE 7 2026-06-20 glow/toon post chain; IDB SHA 263bd994)*
> - **ida_anchor:** 263bd994
> - **evidence:** [static-ida, sample-vfs]
> - **cycle7_additions:** glow blur uses a **fixed pass-loop count of 16** (single small 2×2 box blur,
>   one power-shader pass — NOT a multi-tap pyramid) — §6.4; the **toon ramp light-direction constant
>   is (−1.0, 0.0, 0.0)** — §5.1a / §6.5; cel **edge/outline REFUTED** (no outline/edge-detect shader
>   in the full shader set — only a ramp-shade pipeline) — §6.5 (reaffirmed HIGH); background/fallback
>   **clear colour 0xFF505050** dark-grey ARGB — §2.0.1 (reaffirmed).
> - **conflicts:** four corrections vs. the prior text, now applied — (C1) Present runs *inside*
>   the per-iteration device-step, not an "outer frame driver" (§2.1); (C2) the "four scene
>   callbacks" abstraction hides ≥6 real callback slots (§2); (C3) the cel vertex shader receives
>   light/material/luma constants in registers 4..10 (incl. the BT.601 luma weights), **not** a
>   world-view-projection matrix constant (§5.1a); (C4) the frame-rate cap is a configurable
>   per-scene rate, with 60 being only the device-reset presentation refresh-rate default (§2.0).
>   **RESOLVED (2026-06-21, binary-won) — `display.lua` vs §6.4 glow chain (C5):** the §6.4 reading
>   wins. The binary's shader string set is exactly five files (the cel vertex shader, two cel pixel
>   shaders, the `finaldx8` composite, and `power1dx8.psh`) — there is **no `power2dx8.psh` /
>   `power4dx8.psh` literal and no `power-N` filename-construction format string** anywhere in the
>   executable, so the binary cannot itself name a power-2/4 shader. The glow loader opens whatever
>   **string** the editable glow-shader filename slot holds; that slot is pre-filled with
>   `data/shader/power1dx8.psh` and is overwritten only by the display-config `DISPLAY_POWERSHADER`
>   **string** key (read as a string and copied verbatim — it is a filename, not a numeric power level).
>   The shipped client therefore binds `power1dx8.psh` for glow and `finaldx8.psh` for the composite.
>   A config that names `power2dx8.psh` would only load if such a file existed in the VFS (a
>   **DATA-PENDING** question about the data files, not an IDA question about the binary). See §6.4.

---

## Status

| Item | Value |
|------|-------|
| Confidence model | CONFIRMED = behavior recovered by following the static control flow of the draw machinery; SAMPLE-VERIFIED = value read verbatim from a real VFS file; PLAUSIBLE = structurally inferred from surrounding code but not fully unwound; UNVERIFIED = not provable from the path read, would need a live/debugger or on-disk asset confirmation. |
| Graphics API | Direct3D 9 (`IDirect3DDevice9`). The engine is a thin wrapper over a single device. |
| Frame structure (per-scene run loop, device-step + Present + device-lost recovery, separate scene-draw fork) | CONFIRMED. The Present and the device-lost recovery live in the per-iteration device-step routine; the scene draw is a separate fork. See §2.0. |
| Frame-rate cap (configurable per-scene rate, seeded 60, unoverwritten) | CONFIRMED (mechanism). See §2.0. |
| Per-frame draw loop (direct + offscreen paths, ordered scene callbacks, cull walk) | CONFIRMED. The "four callbacks" model is a behavioural abstraction over ≥6 real callback slots. See §2. |
| Draw order verdict (opaque → alpha-test → transparent → effects → post → UI, no depth sort) | CONFIRMED (firing order). The FX sub-bucket internals are re-confirm-pending — see §3 / §4. |
| Render-state cache (18-slot lazy compare-and-apply) | CONFIRMED — re-confirmed 2026-06-21 (the per-state-type render-state objects, one cache slot per state type; blend setter forwards the bare integer to the device blend state; Z-write toggled imperatively). See §4.1. |
| Per-bucket render-state matrix | CONFIRMED previously (D3DBLEND enum bytes byte-verified; two transparent buckets DO write depth); NOT re-walked this lane — re-confirm pending. See §4.2. |
| Per-object material/texture binding (cel = skinned actors only, gated on post-process flag) | CONFIRMED. See §5. |
| Cel shader binding (cel VS gets light/luma constants in regs 4..10, NOT a WVP matrix) | CONFIRMED. See §5.1a. |
| Shader set (5 shaders: 2 cel PS + cel VS + composite + editable glow; VFS-first load) | CONFIRMED. See §6 / §6.5. |
| Per-class vertex stride / FVF | CONFIRMED for world geometry (terrain/building/static/skinned = 32-byte XYZ+N+UV) and FX (24-byte XYZ+DIFFUSE+UV). Particle/UI strides PLAUSIBLE. See §5.2. |
| Glow/bloom post chain (3 render targets, ordered pass list) | CONFIRMED (load + execution). No bright-pass threshold; single blur pass; present is an opaque copy; composite weights config-driven. See §6. |
| `display.lua` glow/bloom config values (GLOW_BRIGHT_MULTI, GLOW_RANGE, glow-shader filename) | SAMPLE-VERIFIED values; the §6.4 conflict is **RESOLVED (2026-06-21, binary-won)** — the glow shader is `power1dx8.psh` (the `DISPLAY_POWERSHADER` key is a filename string, no `power-N` construction exists in the binary), composite is `finaldx8.psh`. See §6.3 / §6.4 / §6.6. |
| `DISPLAY_CHAR_BRIGHT_*` 9-state character tint table | SAMPLE-VERIFIED values; **recovered, NOT-YET-PORTED feature**. See §6.7. |
| Device-lost / reset / restore lifecycle | CONFIRMED. See §2.0.2. |

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

## 2.0 Frame structure — run loop, device-step, and the frame-rate cap

**Confidence: CONFIRMED.**

The frame is built from three nested routines, distinct from the scene-draw fork (§2):

1. **Per-scene run loop.** Each scene/window state runs its **own** run loop instance — this is not
   a single global loop. The client's entry-point body is a `while-switch` state machine over a game
   state (0..8); each non-terminal state builds its window object and enters a run loop that blocks
   until that scene exits. So login / opening / loading / character-select / main-world each drive a
   separate run loop. The loop sets the timer period to 1 ms (`timeBeginPeriod(1)`), then repeats —
   while an engine "running" flag holds — a body of: **pump Win32 messages** (peek/get/translate/
   dispatch) **and drain a critical-section-guarded work queue** → **device-step** (item 2) → **timed
   subsystem tick** (sound / streaming ring) → **frame-rate limiter** (item 3). Cross-link
   `specs/game_loop.md` and `specs/client_runtime.md` — those own the per-scene-state model; this
   spec only describes what the loop drives on the render side.

2. **Device-step + Present + device-lost recovery (one routine).** Per iteration, the device-step
   routine does the scene draw and the Present in one place:
   - **Active branch** (a per-object "active" flag set): walk the scene list and, for each scene,
     run the **camera/cull setup** (§2.2 — RTTI-driven, see below) and the **scene-draw fork** (§2 —
     offscreen vs. direct), then call **`Present`** (the full present, device vtable). On a failed
     Present it enters the **device-lost recovery** lifecycle (§2.0.2).
   - **Paused/inactive branch** (flag clear): it merely `Clear`s and `EndScene`s — no scene walk, no
     Present.
   > **Correction (C1).** The `Present` is issued **inside this per-iteration device-step**, right
   > after the per-scene draw loop and guarded by the device-lost recovery — there is **no separate
   > "outer frame driver"** that presents. The earlier §2.1 step-8 wording ("the actual `Present`
   > happens in the outer frame driver") is corrected accordingly.

3. **Frame-rate limiter (configurable, seeded 60).** The limiter measures elapsed time with the
   high-resolution performance counter (`QueryPerformanceFrequency` / `QueryPerformanceCounter`) and
   `Sleep`s to hit a target frame-time of **`1.0 / rate`**, where `rate` is a **float field on the
   scene object at offset +48**. It also stores the measured frame-seconds back onto the scene
   object.
   > **Reconciliation with `game_loop.md` / `client_runtime.md` (C4).** This +48 field is the **same
   > field** those specs describe as the per-frame limiter's rate at engine-object **+0x30** (0x30
   > hex = 48 decimal). It is **seeded to 60.0 in the engine constructor and is never overwritten on
   > any static path** → the effective behaviour is a fixed ~60 FPS, but the mechanism is a
   > **configurable per-scene rate** (a QPC `Sleep` to `1.0 / rate`). Do **not** read this as a
   > hardwired 60. The only *literal* `60` elsewhere is the windowed presentation **refresh-rate**
   > field written into the Direct3D present parameters during a device reset (§2.0.2) — that is a
   > present-params field, **not** the loop cap. So: "the field is configurable but seeded 60 and
   > unoverwritten" — phrase it that way, and do not contradict `game_loop.md`.
   >
   > **Note (FPS counter display):** the on-screen FPS counter (the optional UI-callback step, §2.1
   > / §2) is gated by a separate FPS-enable flag. The `data/script/display.lua` key
   > `DISPLAY_FRAMERATE` (SAMPLE-VERIFIED value **0** = hidden) is the config source for that flag;
   > the apply-path (which flag it sets) is IDA-pending. See §6.6.

### 2.0.1 Per-frame Clear and scene begin/end

**Confidence: CONFIRMED.** Every frame, on **both** the direct and offscreen paths, the device is
cleared with **TARGET | ZBUFFER** (the combined clear flag value `3`) and a depth-clear value of
**1.0**. `BeginScene` / `EndScene` bracket each draw. The clear colour is the configured scene clear
colour; the **background / fallback clear colour is `0xFF505050`** (a dark-grey ARGB, set once at the
init of the transparent/particle render pass). CONFIRMED (immediate).

### 2.0.2 Device-lost / reset / restore lifecycle

**Confidence: CONFIRMED.** When `Present` fails, the device-step enters a cooperative-level recovery:

1. Call **`TestCooperativeLevel`** on the device.
2. If the result is **`D3DERR_DEVICENOTRESET`** (`0x88760869`): invoke the registered
   **device-lost callback**, then run the **reset path** — rebuild the Direct3D present parameters
   (this is where the windowed presentation **refresh rate is set to 60**) and call **`Reset`** on
   the device; on a successful reset, invoke the registered **device-restored callback** (so RTs,
   shaders, and dynamic resources are rebuilt).
3. If the result is **`D3DERR_DEVICELOST`** (`0x88760868`): return it; the run loop **`Sleep`s for
   1000 ms** and retries on the next iteration.

This lifecycle is the engine-side contract for surviving an alt-tab / mode-change / lost device. A
faithful port that recreates render targets and shaders should hook the equivalent of the
device-lost / device-restored callbacks (or rely on the engine's automatic resource recreation).

---

## 2. Per-frame scene draw loop

**Confidence: CONFIRMED.**

The scene-draw fork (the routine the device-step calls per scene, §2.0) reads the offscreen-enable
flag on the scene/post object. If set, it runs the **offscreen render-to-texture / post-process
path** (the glow chain, §6); otherwise it runs the **direct path** (draws straight to the backbuffer
with no post-process). Both paths drive the same scene content through an ordered set of scene
callbacks fired in this fixed order:

| Order | Callback role | Bucket it draws |
|------:|---------------|-----------------|
| 1 | Pre-scene / sky callbacks (two slots) | Background, sky-dome — run first, with the view transform set. |
| 2 | Main scene-root draw callback | Opaque world + characters + ground/actor shadows (§3). |
| 3 | FX / post-scene overlay callback | The transparent / effects bucket (§3). |
| 4 | UI / HUD callback | 2D window/HUD draw, followed by an optional FPS counter. |

> **Correction (C2) — "four callbacks" is an abstraction.** The four rows above are a clean
> *behavioural* grouping. The draw object actually holds **≥6 distinct callback-pointer slots** —
> two pre-scene/sky slots, a cull-root draw object **plus a debug-variant cull-root** selected by a
> per-object debug flag, a post-opaque overlay slot, a further overlay slot, and the FX/post-scene
> overlay slot — together with a separate **FPS-enable flag** that gates the optional FPS counter.
> The four-bucket order is a faithful description of *what draws when*, but a port should not assume
> literally four callback fields; the real layout is a richer slot set whose firing collapses into
> the order above.

### 2.1 Direct path, phase order

1. Snapshot the backbuffer viewport, set the viewport, clear with **TARGET | ZBUFFER** (flag `3`,
   depth 1.0, §2.0.1), begin scene.
2. Set the View transform and run the pre-scene / sky callbacks.
3. Configure texture stages 0/1/2 to a default modulate plus sampler filters.
4. Run the camera / cull-root object (one of two candidate cull roots selected by a debug flag): this
   performs the **RTTI-driven camera setup** (§2.2) and **frustum-plane culling**, and emits the
   draw-item list.
5. Main scene-root draw (opaque + shadows).
6. FX overlay draw (transparent + particles).
7. UI / HUD callback; optional FPS counter; end scene.
8. Accumulate average cull-time / draw-time profiling. **The `Present` is issued by the enclosing
   device-step routine (§2.0), immediately after this per-scene draw and under the device-lost
   recovery guard — not by a separate outer driver** (corrects the earlier wording, C1).

### 2.2 Scene-graph cull and walk (RTTI-driven camera setup)

**Camera / projection setup is RTTI-driven (CONFIRMED).** Before the cull, the setup routine
**dynamic-casts the bound camera to the engine's perspective-camera class**; on success it reads the
camera's **field-of-view** and **aspect-ratio** fields and builds the projection from them; if the
cast fails it falls back to a **default FOV of π/4 (45°)**. It then builds the **view matrix**,
inverts it (orthonormal inverse), and constructs the **frustum / polytope** from the projection.

The scene-graph walk then packs the projection parameters, runs a frustum-plane cull + tree walk, and
`accept`s a visitor with a visibility mask of `(1 << planeCount) - 1`. The cull pipeline's draw stage
iterates each emitted draw-item: sets the per-item world transform, applies the per-item
light/material (driving the material/lighting cache slots), dispatches to one of two per-item draw
routines selected by a per-item flag (indexed vs non-indexed primitive draw), then **force-commits
the entire 18-slot render-state cache** so the device matches the cache exactly before the next item.

> The per-item draw-stage internals (the indexed-vs-non-indexed branch and the force-commit) were
> recovered in a prior pass but **not re-walked this lane** — treat them as re-confirm-pending along
> with §4. The RTTI camera cast, the FOV/aspect read, the π/4 fallback, the view-matrix
> build/invert, and the frustum construction were re-confirmed this lane. Matrix major-order and the
> up-axis remain **capture/debugger-pending**.

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

> **Re-confirm-pending flag (this lane).** The 18-slot render-state cache **mechanism** (§4.1) and
> the **per-bucket blend / Z-write byte matrix** (§4.2) were recovered and byte-verified in a prior
> pass but were **NOT re-walked in this lane**. They live in the FX-overlay (post-scene) body and in
> the render-state cache class hierarchy. The content below stands as previously confirmed; a focused
> follow-up should re-verify the byte-exact source/destination blend pairs and the per-bucket Z-write
> directly in the post-scene overlay body. The present-time blend pair (§4.2 "Post: present" row, and
> §6.3) is specifically **capture/debugger-pending** — the present-stage blend bytes were not re-read.

### 4.1 Mechanism

**Confidence: CONFIRMED (prior pass); not re-walked this lane.**

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

**Confidence: CONFIRMED (byte-verified); the UI/HUD row re-walked 2026-06-21.**
The named source/destination blend pairs are **byte-verified** Direct3D `D3DBLEND` enum values (the
blend setters forward the bare integer straight to `SetRenderState`, so the integer in each bucket
setup *is* the enum byte). Per-bucket **Z-write** is also byte-verified: it is toggled explicitly per
bucket, and the earlier assumption that transparent buckets inherit Z-write OFF is **REFUTED** — two
transparent buckets (2×/multiply and water/reflective) enable Z-write. A focused follow-up lane on
the post-scene overlay body should re-confirm these bytes; the present-stage blend pair specifically
remains capture/debugger-pending (§6.3).

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
| UI / HUD — in-game panels (ortho enter) | OFF | OFF | OFF | **blend disabled at bucket-enter** (each quad/glyph opts in to translucency itself; canonical translucent = SRCALPHA / INVSRCALPHA) | OFF | 2D ortho enter at the head of every panel draw; also sets cull = CW, fill = solid, an orthographic projection (near/far ≈ −300 .. +300) with identity world/view; **no alpha-reference** |
| UI / HUD — front-end overlay (login / character-select / opening) | OFF | OFF | OFF | **ONE / ONE (additive)** | OFF | additionally clears fog, dither, and alpha-test; stage 0 = select-arg-1 / diffuse, stage 1 = disabled; samplers = linear / linear / mip-none |

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

**UI / HUD bucket (re-walked 2026-06-21, CODE-CONFIRMED).** The UI bucket has two distinct entry
paths and they differ in their blend setup, so the single "UI / HUD" row was split above. The
**in-game HUD panels** run a 2D-ortho enter at the head of every panel draw: depth test OFF, depth
write OFF, lighting OFF, cull clockwise, fill solid, an orthographic projection (near/far roughly
−300 .. +300) over identity world/view, and **alpha-blend left disabled at bucket-enter** — each
quad or glyph opts into translucency itself through its own blend state (the canonical translucent
pair being source-alpha / inverse-source-alpha). No alpha-reference is written (alpha-test stays
disabled). The **front-end overlay** (login, character-select, opening windows) instead enters with
alpha-blend ON and an **additive** source = ONE / destination = ONE pair, and additionally clears
fog, dither, and alpha-test and configures texture stage 0 = select-arg-1 / diffuse with stage 1
disabled and linear/linear/mip-none sampling. A 3D item-preview inset embedded inside an inventory
panel temporarily re-enables depth test and then restores it on the way out — confirming that
"no depth" is the UI default that the inset deviates from. The per-quad translucent source/dest
pair for the in-game panels and the effective depth-write state at the very first UI draw are
**DBG-PENDING** (a live device render-state read during a panel draw).

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
— the same flag §2 uses to choose the direct vs offscreen path. The cel/toon **constant upload +
lit-world draw** routine is called **only** from the offscreen path, after the composite and before
the post-scene overlay; the direct path draws the same cull-root object with **no** toon
vertex-shader constant upload. So with post-process **OFF**, even a skinned actor falls back to its
**fixed-function** draw (no cel shader, no toon constants). With post-process **ON**, the skinned
actor binds the cel pair and draws from the stride-32 skinned vertex buffer. **The toon look is
therefore coupled to the post-process feature**: turning post-process off removes the cel shading from
skinned actors. This matters for Godot fidelity.

> **Correction (C3) — what the cel vertex shader actually receives.** The earlier text said the cel
> path "builds the composite world-view-projection matrix and uploads it to the cel vertex shader as
> a single matrix constant (four vec4 registers)". That is **imprecise** and is corrected here:
> - The toon constant-upload routine sets **vertex-shader constant registers 4 through 10** (seven
>   four-float registers). Register 4 carries a **3-vector light/sky direction** (padded to four
>   floats); registers 5..10 carry **light / material / luma constants**, and among them are the
>   literal **BT.601 luminance weights ≈ 0.299, 0.587, 0.114** — the weights that key the toon-ramp
>   lookup (matching the ramp-luma note in `formats/shaders.md`).
> - These registers are **not** a world-view-projection matrix and **not** a bone palette. The
>   world / view / projection transform is pushed through the device's **`SetTransform`** path (the
>   engine's world-0 / view transform setters), **not** uploaded as a vertex-shader constant in this
>   routine.
>
> For a Godot port this means the cel material's per-frame inputs are the **light direction and the
> luma weights / material constants**, with the geometry transform supplied the ordinary way — do
> not model the cel VS as consuming a hand-uploaded MVP matrix.

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
each paired with a surface and a render-to-surface helper. **TEX0** and **TEX1** are created at
backbuffer dimensions **with a 1024×1024 retry on creation failure**; **TEX2** is created directly at
the *downscaled* size (backbuffer ÷ the glow divisors) **with no fallback**.

### 6.1 Render targets

| Render target | Dimensions | Role |
|---------------|------------|------|
| TEX0 — scene capture | backbuffer dims (1024×1024 retry) | The cel/scene RT the whole world is drawn into; also the composite destination and the present source. |
| TEX1 — bright / edge extract | backbuffer dims (1024×1024 retry) | A plain copy of the scene RT (no thresholding — see §6.4). Consumed only by the composite. |
| TEX2 — bloom blur | downscaled (backbuffer ÷ the glow divisors, default ÷2 each); no fallback | The blurred, downsampled glow buffer. |

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

> **Config source (SAMPLE-VERIFIED) — `data/script/display.lua` glow range.** The shipped
> `data/script/display.lua` sets `DISPLAY_GLOW_RANGE_X = 1` and `DISPLAY_GLOW_RANGE_Y = 1`
> (SAMPLE-VERIFIED values; powers of 2 are valid per the file comment). At range 1×1 the downsample
> is effectively 1:1 — TEX2 is full-resolution, not ÷2 — which **differs from the binary's
> hardcoded default ÷2**. Whether the loader applies the configured 1 directly (so TEX2 is full-res)
> or the binary's ÷2 fallback dominates is part of the IDA-pending reconciliation (§6.4 / C5).
> `// spec: Docs/RE/specs/rendering.md §6.3`

**Composite weights c0 / c1.** The composite pass uploads two pixel-shader scalar constants from code
(reads them off the scene/post object and broadcasts each one 4-wide to a register): stage 0 binds
the bright/edge-extract RT, stage 1 binds the bloom RT, and the two scalars weight them. **The upload
itself is CONFIRMED.** The interpretation of the two scalars as

- **c0** = (base-bright-multiplier × 0.5)
- **c1** = (glow-bright-multiplier × 0.5)

— with both multipliers defaulting to **1.0** (so the uploaded defaults are **c0 = c1 = 0.5**),
sourced from the external display config, and the ×0.5 being a real code-side multiply — is
**static-hypothesis**: the scalars are read pre-computed from the scene object and the **producer site
that writes them was not opened this lane**, so the half-scaling and the 1.0 default are inferred from
the prior pass, not re-derived. Either way these are the **two tunable knobs** for the glow (base
brightness and glow brightness) — they are **not** hardwired white. The binding is consistent with a
`saturate(2·edge·c0 + bloom·c1)`-style composite. The exact pixel-shader arithmetic (the `2×`, the
`saturate`, any embedded constants) lives in the external `.psh` and is documented in
`formats/shaders.md` — only the c0 / c1 scalars come from code.

> **Config source (SAMPLE-VERIFIED) — `data/script/display.lua` glow brightness.** The shipped
> `data/script/display.lua` sets `DISPLAY_GLOW_BRIGHT_MULTI = 0.3` and `DISPLAY_BASE_BRIGHT_MULTI =
> 1.05` (SAMPLE-VERIFIED values; the world-geometry `DISPLAY_BASE_BRIGHT_MULTI` is owned by
> `environment.md §9`). `DISPLAY_GLOW_BRIGHT_MULTI = 0.3` is the **glow/bloom post-pass multiplier**
> — intentionally dim (30%). This is the live `glow-bright-multiplier` that the c1 derivation above
> describes: with the binary's ×0.5 code-side multiply this would yield `c1 = 0.3 × 0.5 = 0.15`,
> **differing from the §6.3 default-1.0 → c1 = 0.5** computed from the binary defaults. The base
> multiplier the c0 derivation uses is `DISPLAY_BASE_BRIGHT_MULTI = 1.05` (→ `c0 = 1.05 × 0.5 =
> 0.525` if the ×0.5 holds). Both the binary-default reading (1.0 → 0.5) and the shipped-config
> reading (0.3 / 1.05) are recorded; the reconciliation (whether the ×0.5 code-side multiply is
> applied to the config values, and the producer site that writes c0/c1) is **IDA-pending** (§6.4 /
> C5). `// spec: Docs/RE/specs/rendering.md §6.3`

**Present blend (corrects earlier "additive present" wording).** Structurally the present pass blits
the composited TEX0 to the backbuffer as a straight **opaque copy** — **source = ONE, destination =
ZERO**, not an additive (ONE / ONE) blend (alpha-blend is enabled, but the ONE / ZERO factor pair
makes it a copy). The additive "glow add" math is performed **inside the composite pixel shader into
TEX0** (pass 4); the present (pass 5) merely copies that result to the backbuffer, so **do not re-add
at present time**. The present-stage path was re-confirmed this lane, but the **exact present-time
blend bytes (the ONE / ZERO pair) were NOT re-read** — that specific factor pair is
**capture/debugger-pending** (or a dedicated blend-byte read on the present stage).

### 6.4 Bright-pass threshold and power-chain depth (CONFIRMED) — with `display.lua` CONFLICT

**Bright-pass threshold: there is NONE (CONFIRMED).** The bright/edge-extract pass (pass 2) clears
TEX1 to opaque black and performs a **plain fixed-function copy** of the scene RT into TEX1 at full
backbuffer size. It binds **no pixel shader**, sets **no alpha-test / alpha-reference**, applies
**no subtract**, and uploads **no texture-factor scale**. There is no luminance cutoff of any kind —
every pixel of the scene feeds the blur. The glow's selectivity comes entirely from the composite
scalars (c0 / c1) and the external glow-shader's own multiply, **not** from a bright threshold.

**Power-chain depth: a single blur pass; only one glow shader runs.** Exactly **one blur pass** runs
(CONFIRMED this lane: a single glow-blur routine binds the glow pixel-shader handle, draws one
downscaled quad, then clears the pixel shader) — there is **no** `power2 → power4` multi-tap chain in
the code path.

> **Glow is a single small box blur with a fixed pass-loop count of 16 (CYCLE 7).** The post-process
> engine constructor stores a fixed **pass-loop count of `16`** alongside the default glow blur
> **range (2, 2)** (§6.3 downsample divisors). The shape recovered is therefore a **single small 2×2
> box blur driven by a fixed 16-iteration loop and a single power-shader pass** (`power1dx8.psh` by
> default, §6.5) — **not** a multi-level Gaussian pyramid and not a `power2/power4` tap escalation.
> Confidence: the single-blur-pass structure and the stored `16` / range-`(2,2)` immediates are
> CONFIRMED; the precise way the 16 loop count drives the box-blur taps inside the pass is MED-HIGH
> (the loop count is an immediate; its exact per-tap consumption inside the bound `.psh` lives in the
> external shader, `formats/shaders.md`). For a Godot port a single half-res box-ish blur is the
> faithful equivalent — do not stack a multi-level pyramid (§8). The glow pixel shader is the single **editable-filename** shader, whose default
filename slot is pre-filled with the `power1dx8` path and can be overwritten **only** by an external
display-config string (a dedicated power-shader key). That a stock client only ever loads `power1dx8`,
and that a byte-level search finds **zero** `power2` / `power4` literals, was established in a prior
pass and **re-searched and re-confirmed 2026-06-21** (exhaustive string sweep): the executable's
complete shader-filename set is exactly `dotoonshading.vsh`, `dotoonshading.psh`,
`dotoonshading2.psh`, `finaldx8.psh`, and `power1dx8.psh` — there is **no `power2dx8.psh` /
`power4dx8.psh` literal and no `power-N` / `%ddx8.psh` filename-construction format string** in the
binary. So the `power1dx8`-as-default-name, the absence of power2/4, and the **single-blur-pass**
structure are all **CONFIRMED**. A multi-tap chain could only run if the external display config
explicitly named such a path *and* the file existed in the VFS; the executable never names or
sequences them.

> **RESOLVED (2026-06-21, binary-won) — `display.lua` `DISPLAY_POWERSHADER` is a filename string, not
> a numeric power level.** The earlier conflict (whether `DISPLAY_POWER = 2` selects a `power2dx8.psh`)
> is settled in favour of the binary:
>
> - The binary reads the display-config key **`DISPLAY_POWERSHADER`** with the **string** getter and
>   copies it **verbatim** into the editable glow-shader filename slot (which is pre-filled with
>   `data/shader/power1dx8.psh` by the post-process constructor). It is consumed as a **filename**, not
>   as a number that indexes a shader array.
> - An exhaustive string sweep of the executable finds **only** `power1dx8.psh` among the `power*`
>   shaders, and **no `power-N` / `%ddx8.psh` filename-construction format string** — the binary cannot
>   itself build a `power2dx8.psh` / `power4dx8.psh` name. The complete shader-filename set is the five
>   files of §6.5.
> - The glow loader therefore binds **whatever string the slot holds**, which is `power1dx8.psh` in the
>   stock client, and the composite pass binds `finaldx8.psh`.
>
> Consequence: §6.4's binary reading — single-tap `power1dx8`, no power-2/4 in the binary — is
> **CONFIRMED**. The only remaining question is **DATA-PENDING (not IDA-pending)**: *if* a player's
> `display.lua` set `DISPLAY_POWERSHADER` to a `power2dx8.psh` *and* such a file existed in the VFS,
> the VFS-first loader (§6.5) would open it — but that depends on the data files, not the binary, and
> no such shader is shipped. For a faithful port, bind a single `power1dx8`-style blur shader.
> `// spec: Docs/RE/specs/rendering.md §6.4`

### 6.5 Shader set — five shaders, two cel pixel shaders, VFS-first load

**Confidence: CONFIRMED (count, roles, load path).** The cel/glow initializer assembles **five**
shaders and loads the toon-ramp LUT:

| Shader | Role |
|--------|------|
| Cel **vertex** shader | Skinned-actor toon vertex shader; receives the light/luma constants of §5.1a (regs 4..10). |
| Cel **pixel** shader **#1** | Primary toon-shading pixel shader. |
| Cel **pixel** shader **#2** | A **second** toon-shading pixel shader (a variant) — uploaded to its own handle slot alongside #1. |
| Composite **pixel** shader | The glow/composite shader bound in pass 4 (the `finaldx8` composite — see `formats/shaders.md`). |
| Editable **glow** pixel shader | The blur shader bound in pass 3; its filename is the editable slot defaulting to `power1dx8.psh`, overridable by the `display.lua` `DISPLAY_POWERSHADER` **filename string** key (§6.4, resolved). The stock client binds `power1dx8.psh`. |

> **Cel EDGE / outline — REFUTED (CYCLE 7, HIGH, absence over the full set).** There is **no
> outline / edge-detect shader and no edge pass** anywhere in the cel pipeline. The complete shader
> set was enumerated (the cel VS, the two cel PS variants, the composite shader, and the editable glow
> shader, plus the toon-ramp LUT) — **none** of them is an outline or edge-detect shader, and no
> separate edge pass runs. Only a **toon RAMP shade** pipeline exists (the ramp LUT keyed by the
> per-vertex luminance, §5.1a / §5.2). Any cel **outline** in a Godot port is therefore a **port-side
> invention**, not a faithful reproduction — alongside the "ambient ×3" refutation in
> `environment.md §6.2b`, this is the second of the two port-side embellishments to drop for fidelity.

> **Correction / enrichment (MISSED #7).** The prior text described "the cel pixel shader" in the
> singular; the binary assembles **two** cel pixel shaders (a primary and a `…2` variant), each in its
> own handle slot. The actual on-disk shader **filenames** observed in the binary
> (`dotoonshading.vsh` / `dotoonshading.psh` / `dotoonshading2.psh` / `finaldx8.psh`, plus the
> editable glow shader) are documented and owned by **`formats/shaders.md`** (Block D) — this spec
> describes only the count, roles, and binding; it defers the canonical filename list there.

**Toon ramp light-direction constant (CYCLE 7, CONFIRMED immediate).** The post-process engine
constructor seeds the toon-ramp **light direction** as the constant vector **`(−1.0, 0.0, 0.0)`**.
This is the directional input that keys the toon ramp shade (the §5.1a register-4 light/sky direction
the cel vertex shader consumes); it is a fixed shader-side constant, distinct from the scene's
day/night directional-light direction (`environment.md §6.2a`, the static `(−7, 7, 20)` sun vector).
For a Godot toon material, drive the ramp lookup from this `(−1, 0, 0)` light direction (plus the
BT.601 luma weights of §5.1a), not from the scene sun direction. Confidence: MED-HIGH (immediate).

**Shader load path — VFS-first, then disk (CONFIRMED, MISSED #8).** Each shader is loaded by name:
if the client VFS is mounted, the shader source is opened **from the VFS** and assembled from the
in-memory blob; otherwise it is assembled **from the on-disk file**. The `.vsh` / `.psh` sources are
**hand-written assembly assembled at load time**, not pre-compiled binary shader objects. The toon
ramp LUT (`data/shader/toonramp.bmp`) is loaded through the same VFS-or-disk texture loader into its
own cel slot (see §6.1 / `formats/shaders.md`).

> **Bearing on the §6.4 conflict (RESOLVED 2026-06-21).** Because shaders are **VFS-first by name** and
> the glow filename slot holds the verbatim `DISPLAY_POWERSHADER` string, a `power2dx8.psh` named in
> `display.lua` *could* load straight from the VFS — but the binary itself never constructs that name
> and ships only `power1dx8.psh`, so the stock client binds `power1dx8`. Whether any data set actually
> supplies a `power2dx8.psh` is a DATA-PENDING question about the VFS, not a binary question (§6.4).

### 6.6 `data/script/display.lua` glow/bloom + display config — SAMPLE-VERIFIED values

> **Status: `sample-verified` for the VALUES** (read verbatim from the real VFS file
> `data/script/display.lua`, a 5,100-byte CP949 Lua key/value script). **Apply-paths are
> `static-hypothesis / IDA-pending`** where noted. This subsection collects the glow/bloom and
> framerate keys this spec consumes; the world-brightness keys (`DISPLAY_BASE_BRIGHT_MULTI`,
> `DISPLAY_LIGHT_RATIO`) are owned by `environment.md §9`; the per-state character-tint table is in
> §6.7.

| Key | Value | Role | Where consumed | Confidence (value) |
|-----|------:|------|----------------|--------------------|
| `DISPLAY_GLOW_BRIGHT_MULTI` | **0.3** | Glow / bloom post-pass multiplier — intentionally dim (30%). Feeds the c1 glow-bright derivation. | §6.3 composite weights | SAMPLE-VERIFIED |
| `DISPLAY_GLOW_RANGE_X` | **1** | Glow sampling radius / downsample divisor, X axis (powers of 2 valid). | §6.3 downsample divisors | SAMPLE-VERIFIED |
| `DISPLAY_GLOW_RANGE_Y` | **1** | Glow sampling radius / downsample divisor, Y axis (powers of 2 valid). | §6.3 downsample divisors | SAMPLE-VERIFIED |
| `DISPLAY_POWER` | **2** | Glow-power selector (valid set 1/2/4/8/16/32) → resolves the glow `.psh` filename. | §6.4 conflict | SAMPLE-VERIFIED |
| `DISPLAY_POWERSHADER` | **`"data/shader/power2dx8.psh"`** | The resolved glow blur shader path (from the Lua if/elseif keyed by `DISPLAY_POWER`; unknown POWER → `power1dx8.psh`). | §6.4 conflict / §6.5 | SAMPLE-VERIFIED |
| `DISPLAY_FRAMERATE` | **0** | FPS counter visibility (0 = hidden, 1 = shown) → the FPS-enable flag (§2.0 / §2). | §2.0 FPS counter | SAMPLE-VERIFIED |

> **Citation breadcrumb.** A C# constant carrying any of these cites this section:
> `// spec: Docs/RE/specs/rendering.md §6.6`. The values are literal scalars/strings in
> `data/script/display.lua` and are SAMPLE-VERIFIED; the **render-stage apply-paths** for the glow
> values are the subject of the §6.4 IDA-pending reconciliation. **Source citation:** all values are
> from the `data/script/display.lua` config layer (the shipping client's display-config script).

### 6.7 `DISPLAY_CHAR_BRIGHT_*` — per-state character tint table (recovered, NOT-YET-PORTED)

> **Status: `sample-verified` VALUES; RECOVERED, NOT-YET-PORTED FEATURE.** The shipping
> `data/script/display.lua` defines a per-state **character render tint / alpha** table —
> a colour-grade applied to character meshes depending on the character's gameplay state (idle,
> selected, hit, poisoned, etc.). This is a distinct render feature from the world-geometry and
> glow brightness scalars; it is **not yet reproduced in the Godot port**. The **apply-path**
> (which render stage applies the per-state tint to character meshes, and how the active state is
> selected at runtime) is **`static-hypothesis / IDA-pending`** — only the values are recovered.

Each of the 9 states defines a per-channel multiply (`MULTI_R/G/B`), a per-channel add
(`ADD_R/G/B`), and an `ALPHA`. The applied formula is per-channel `y = MULTI · x + ADD`, where `x` is
the source character-pixel value; `ALPHA` is the character draw alpha (the file notes alpha below 0.6
becomes fully transparent). The 9 states (SAMPLE-VERIFIED values):

| State | MULTI R | MULTI G | MULTI B | ADD R | ADD G | ADD B | ALPHA | Meaning |
|-------|--------:|--------:|--------:|------:|------:|------:|------:|---------|
| `DEFAULT` | 1.3 | 1.3 | 1.3 | 0.0 | 0.0 | 0.0 | 1.0 | Normal / idle state (the baseline character tint). |
| `CHOICE` | 1.7 | 1.7 | 1.7 | 0.1 | 0.1 | 0.1 | 1.0 | Selected NPC / monster (brightened highlight). |
| `HIT` | 1.3 | 1.2 | 1.2 | 0.1 | 0.0 | 0.0 | 0.9 | Brief tint each time a hit lands (reddish flash). |
| `ALPHA` | 1.2 | 1.2 | 1.2 | 0.0 | 0.0 | 0.0 | 1.0 | Meaning unknown (file comment notes it is ignored for now). |
| `HIDDEN` | 1.5 | 1.5 | 1.5 | 0.0 | 0.0 | 0.0 | 0.6 | Own stealth / summoned units / same-faction (half-transparent). |
| `POISON` | 1.1 | 1.3 | 1.1 | 0.0 | 0.1 | 0.02 | 1.0 | Poisoned (greenish tint). |
| `TYPE` | 1.2 | 1.2 | 1.4 | 0.1 | 0.1 | 0.4 | 1.0 | Final-damage-reduction buff active (bluish tint). |
| `ANGER` | 1.5 | 1.0 | 1.0 | 0.15 | 0.0 | 0.0 | 1.0 | Fury / rage mode active (reddish tint). |
| `AUTO` | 0.3 | 0.3 | 0.3 | 0.0 | 0.0 | 0.0 | 1.0 | Auto-penalty active (darkened). |

Notes:

- **`DEFAULT = 1.3×` all channels** is the baseline tint every character receives in the normal
  state — characters are rendered ~30% brighter than their source texture/lighting even when idle.
- The tint is a **character-only** colour grade; it does not affect terrain, buildings, or FX.
- **`DISPLAY_LIGHT_RATIO = 0.5`** (owned by `environment.md §9.2`) applies to **character lighting
  only** — it is the character light-colour correction factor that composes with this per-state tint
  layer. Cross-reference: `environment.md §9`.

> **Citation breadcrumb.** A C# constant carrying any of these values cites this section:
> `// spec: Docs/RE/specs/rendering.md §6.7`. **Source citation:** all values are from the
> `data/script/display.lua` config layer. **Apply-path (which render stage tints character meshes,
> and the state-selection logic) is IDA-pending** — see §7 Known unknowns.

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
- **`display.lua` glow chain vs §6.4 binary reading (CONFLICT — IDA-PENDING).** The shipped
  `data/script/display.lua` selects `DISPLAY_POWER = 2 → power2dx8.psh`, glow range 1×1, and a 0.3
  glow multiplier, whereas §6.4 reads the binary as a single-tap `power1dx8` with `power2`/`power4`
  absent and a composite scalar default of 0.5. The resumed IDA pass must settle (a) which `.psh` the
  loader actually opens at POWER=2; (b) whether `power2dx8.psh` is VFS-loaded vs. hardcoded; (c)
  whether the single-blur-pass structure holds with it bound; (d) whether the ×0.5 code-side multiply
  is applied to the config values (0.3 / 1.05) or only to the binary defaults. Values SAMPLE-VERIFIED;
  apply-path / reconciliation IDA-PENDING. See §6.3 / §6.4 / §6.6.
- **`display.lua` glow-value apply-paths (IDA-PENDING).** `DISPLAY_GLOW_BRIGHT_MULTI = 0.3`,
  `DISPLAY_GLOW_RANGE_X/Y = 1`, `DISPLAY_FRAMERATE = 0` — values SAMPLE-VERIFIED; the exact
  render-pipeline field / D3D state each maps into is not yet recovered (the dirty IDA lane crashed
  before reaching the render-stage reads). See §6.6.
- **`DISPLAY_CHAR_BRIGHT_*` per-state character tint apply-path (IDA-PENDING — NOT-YET-PORTED).** The
  9-state tint/alpha values (§6.7) are SAMPLE-VERIFIED, but the render stage that applies the
  per-state colour grade to character meshes, and the runtime logic that selects the active state
  (idle / selected / hit / poison / buff / anger / auto / hidden), are not yet recovered. This is a
  recovered-but-unported character render feature. See §6.7.
- **Live display-config values** — the actual shipped glow-range divisors, base/glow brightness
  multipliers, and the power-shader override are now SAMPLE-VERIFIED from `data/script/display.lua`
  (§6.6); the binary defines only the defaults (2,2 / 1.0 / `power1dx8`) and the ×0.5 scaling. The
  reconciliation between the shipped config and the binary defaults is IDA-PENDING (§6.4).
- **Internal per-primitive logic of the opaque sub-draws** (terrain / buildings / objects / actors)
  — their role and order in the opaque bucket is confirmed; their internal logic was out of scope.
- **Whether terrain actually populates the NORMAL field** — the world geometry format declares a
  normal, but whether the terrain vertex copy writes it or zeroes it was not opened. PLAUSIBLE.
- **18-slot render-state cache mechanism + per-bucket blend/Z-write byte matrix (§4)** — recovered and
  byte-verified in a prior pass but **NOT re-walked this lane**; they live in the post-scene FX-overlay
  body and the cache class hierarchy. Re-confirm-pending: a focused follow-up should re-read the
  byte-exact source/destination blend pairs and the per-bucket Z-write directly there.
- **Present-stage blend bytes (the ONE / ZERO opaque-blit pair, §6.3)** — structurally consistent with
  an opaque copy, but the present-time blend-state bytes were not re-read this lane.
  CAPTURE/DEBUGGER-PENDING (or a dedicated blend-byte read on the present stage).
- **Composite c0 / c1 producer site (§6.3)** — the upload is confirmed, but the site that *writes* the
  two scalars onto the scene/post object (and thus the ×0.5 half-scaling and the 1.0 defaults) was not
  opened; the values are read pre-computed. STATIC-HYPOTHESIS.
- **Per-scene frame-rate default (the +48 / +0x30 rate field, §2.0)** — the field is seeded 60 in the
  engine constructor and unoverwritten on the static paths read, but the per-window scene constructors
  (login / loading / opening / character-select / world) were not traced to confirm none of them
  requests a different target. STATIC-HYPOTHESIS (mechanism CONFIRMED; per-scene default value
  unverified per window).
- **Matrix major-order and up-axis** — the view-matrix build/invert and frustum construction are
  confirmed structurally, but the storage major-order (row vs column) and the world up-axis cannot be
  pinned from static reads alone. CAPTURE/DEBUGGER-PENDING.

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
  > **Shipped display-config (SAMPLE-VERIFIED, §6.6):** the real `data/script/display.lua` sets glow
  > brightness **0.3** (dim), glow range **1×1**, and `DISPLAY_POWER = 2 → power2dx8.psh`. These
  > differ from the binary defaults (1.0 glow brightness, ÷2 range, single-tap `power1dx8`); the
  > §6.4 conflict between the shipped config and the binary reading is IDA-pending. For an initial
  > faithful port, prefer the SAMPLE-VERIFIED shipped values (glow ~0.3, range 1×1) and revisit once
  > the conflict is reconciled.
- Bind the cel/toon material **only to skinned-actor meshes** (player + skinned mobs); leave terrain,
  buildings, and static props unshaded / fixed-function-equivalent. Remember the original couples the
  toon look to the post-process feature flag — with post off, even skinned actors render unshaded.
- **Character per-state tint (§6.7 — recovered, not yet ported).** The original colour-grades
  character meshes per gameplay state (`DISPLAY_CHAR_BRIGHT_*`), with `DEFAULT = 1.3×` all channels
  as the idle baseline and distinct tints for selected / hit / poison / buff / anger / auto / hidden.
  A faithful port should apply this per-state `y = MULTI·x + ADD` tint + alpha to character materials;
  the values are SAMPLE-VERIFIED but the render-stage apply-path and state-selection logic are
  IDA-pending. `DISPLAY_LIGHT_RATIO = 0.5` (character light correction, `environment.md §9`) composes
  with this layer.
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
  `formats/shaders.md` and `specs/skinning.md`. Feed the cel material the **light direction + the
  BT.601 luma weights / material constants** (the cel VS's regs 4..10, §5.1a) — **not** a
  hand-uploaded model-view-projection matrix; the geometry transform comes through the normal vertex
  transform path.
- **Frame structure (cross-link `game_loop.md`).** Each scene/state drives its own run loop; the
  scene draw, the `Present`, and the device-lost recovery are one device-step routine (§2.0), not a
  separate present driver. Run at the **configurable per-scene rate** (the field seeded 60 and never
  overwritten on the static paths → effective ~60 FPS) rather than hardwiring 60 — do not contradict
  `game_loop.md`. `Clear` is **TARGET | ZBUFFER, depth 1.0** every frame (§2.0.1). The shipped
  `DISPLAY_FRAMERATE = 0` (§6.6) means the FPS counter is hidden by default.
- **Camera setup** is RTTI-driven from a perspective-camera object's FOV / aspect, defaulting to a
  **π/4 (45°) FOV** when absent (§2.2). Matrix major-order / up-axis stay capture/debugger-pending —
  validate against the running client before locking them in.
- **Device-lost lifecycle** (§2.0.2): on a lost device the original tears down and rebuilds its render
  targets and shaders via device-lost / device-restored callbacks. A Godot port largely gets this for
  free from the engine, but any manually-created RTs (if you mirror the 3-RT chain by hand) must be
  recreated on a window/device reset.

---

## Cross-references

- `specs/game_loop.md`, `specs/client_runtime.md` — the **per-scene-state run loop** and the
  **frame-rate field** (the +48 / +0x30 rate seeded 60 and unoverwritten); §2.0 here describes only
  the render side of that loop (the device-step + Present + device-lost recovery) and is reconciled
  with — not contradicting — those specs.
- `specs/environment.md` — the **world-brightness scalars** (`DISPLAY_BASE_BRIGHT_MULTI = 1.05`,
  `DISPLAY_LIGHT_RATIO = 0.5`) from the same `data/script/display.lua` config layer (`environment.md
  §9`); this spec owns the glow/bloom (§6.6) and per-state character-tint (§6.7) keys from that file.
- `formats/shaders.md` — the cel / composite / glow shader roles, the toon ramp LUT, and the
  recovered vertex-shader constants (including the BT.601 luma weights that key the ramp). It owns the
  canonical on-disk shader **filename list**; note this lane re-confirmed there are **two** cel pixel
  shaders (a primary + a variant) plus the cel VS, the composite shader, and the editable glow shader
  = **five** total (§6.5). The external `.psh` arithmetic (blur scale, composite
  `2·edge·c0 + bloom·c1`) lives there, not here. The `display.lua` `DISPLAY_POWERSHADER` →
  `power2dx8.psh` selection (§6.4 conflict) bears on which glow `.psh` `shaders.md` should document.
- `specs/skinning.md` — how the skinned-character vertex buffer this pipeline draws is produced.
- `specs/effects.md`, `specs/world_systems.md` — the FX/particle and world buckets this loop draws
  (owned by other authors).
- Glossary: see `Docs/RE/names.yaml` (flag for canonicalisation: `DISPLAY_GLOW_BRIGHT_MULTI`,
  `DISPLAY_GLOW_RANGE_X/Y`, `DISPLAY_POWER` / `DISPLAY_POWERSHADER`, `DISPLAY_FRAMERATE`,
  `DISPLAY_CHAR_BRIGHT_*` state table).
- Provenance: see `Docs/RE/journal.md`.
