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
>   §6.7) — read verbatim from the real VFS file. *static-hypothesis* for the per-scene frame-rate
>   field's per-window default value. *(CYCLE 11: c0/c1 defaults corrected to 1.0 / 1.0 — the
>   earlier ×0.5 derivation was an FP-stack artifact, not a real code-side halving; particle/UI
>   vertex strides upgraded to CONFIRMED; apply-site for 9-state character tint pinned. See §6.3,
>   §5.2, §6.7.) *(2026-06-21: the glow `.psh` question is RESOLVED — see the C5 note below and §6.4.)*
>   *capture/debugger-pending* for the matrix major-order /
>   up-axis, any on-screen colour verdict, and the front-end blend final confirmation (§4.2, CYCLE 11
>   DOWNGRADED to DEBUGGER-PENDING — see §4.2 CYCLE 11 re-open note); in-game HUD per-quad
>   opt-in stays CONFIRMED. *Present-blit blend (ONE/ZERO, opaque copy) upgraded to
>   static-confirmed by wave-5 walk of `Renderer_DrawScene_OffscreenRT_0` — see §6.3 and
>   `Docs/RE/specs/post_processing.md §8.1`.* *(2026-06-21 ASSET-FIDELITY re-walk: the 18-slot render-state cache
>   mechanism is re-confirmed, and the UI/HUD row split into in-game-HUD vs. front-end-overlay entry
>   paths. 2026-06-22: §4.2 front-end blend reconciled — global ONE/ONE additive binary-won,
>   opaque-panel SRCALPHA hypothesis refuted — see §4.2. CYCLE 11: front-end verdict DOWNGRADED to
>   DEBUGGER-PENDING as noted above.)* CYCLE 14 re-anchor (f61f66a9): 1 fact re-confirmed SAME
>   (D3DXCreateRenderToSurface 4 sites / 2 systems / none-for-water); 1 corrected (offscreen-enable
>   flag stored at >=2 sites in the device-creation context — see §6.1).
> - **ida_reverified:** 2026-06-27 (CYCLE 14 re-anchor, f61f66a9): 1 fact re-confirmed SAME (D3DXCreateRenderToSurface 4 call sites in 2 systems; none for water); 1 corrected (offscreen-enable flag stored at >=2 sites, not exactly 1 — see §6.1 CYCLE 14 note and `formats/shaders.md §C5.6b`). Prior: 2026-06-24 *(CYCLE 12 audit (263bd994): world projection confirmed RH (D3DXMatrixPerspectiveOffCenterRH); UI ortho confirmed LH (D3DXMatrixOrthoOffCenterLH/OrthoLH) — RH/LH split per pass is a static fact; DisplayConfig_ParseFramerate identified as the display.lua loader (confirms §6.3/§9.4 shared display-config apply-path provenance). Prior: CYCLE 11 (2026-06-22, 263bd994): brightness = composite PS constants (NOT a gamma ramp), defaults 1.0 (the 0.5 was an FP-stack artifact); DISPLAY_LIGHT_RATIO confirmed parsed-but-dead; 9-state character tint apply-site pinned; UI/particle stride 24 (UI via D3DX sprite helper); §4.2 FRONT-END one/one additive verdict DOWNGRADED to debugger-pending (sprite Begin may override it) — in-game HUD per-quad opt-in unaffected. IDB SHA 263bd994)*
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the C# rebuild (control-flow-confirmed against IDB SHA 263bd994); items explicitly tagged debugger-pending / capture-pending / RD-* are NON-blocking runtime residuals to confirm later.
> - **evidence:** [static-ida, sample-vfs]
> - **cycle7_additions:** glow blur was described as using a **fixed pass-loop count of 16** (single
>   small 2×2 box blur, one power-shader pass) — §6.4; `[open-question]` wave-5 static analysis
>   (`Docs/RE/specs/post_processing.md §6.2`) found **no loop** in either draw routine — see §6.4
>   for the correction; the **toon ramp light-direction constant
>   is (−1.0, 0.0, 0.0)** — §5.1a / §6.5; cel **edge/outline REFUTED** (no outline/edge-detect shader
>   in the full shader set — only a ramp-shade pipeline) — §6.5 (reaffirmed HIGH); background/fallback
>   **clear colour 0xFF505050** dark-grey ARGB — §2.0.1 (reaffirmed).
> - **conflicts:** CYCLE 14 (f61f66a9): (C6) offscreen-enable flag stored at >=2 sites (not exactly 1 as shaders.md §C5.6b previously stated) — corrected in §6.1. Prior: four corrections vs. the prior text, now applied — (C1) Present runs *inside*
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
> - **wave-5-reconciliation (2026-06-28):** (W1) §6.3 present-blit blend upgraded to static-confirmed
>   per `Docs/RE/specs/post_processing.md §8.1`; (W2) §6.1 TEX2 allocation base corrected to
>   1024-base (not backbuffer-base) per `post_processing.md §4.1` — `[open-question]` above
>   1024×1024; (W3) §6.4 "16-loop / 2×2 box blur" claim downgraded to `[open-question]` per
>   `post_processing.md §6.2` (no loop in draw routines); (W4) §4.1 cross-linked
>   `Docs/RE/structs/render_state.md §7` for `GRSTransparency` coupling matrix; (W5) §5.1a
>   cross-linked `Docs/RE/specs/character_rendering.md §3.1/§4`.

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
| Per-class vertex stride / FVF | CONFIRMED for world geometry (terrain/building/static/skinned = 32-byte XYZ+N+UV) and FX (24-byte XYZ+DIFFUSE+UV). Particle stride CONFIRMED (CYCLE 11, 24 bytes). UI/HUD CONFIRMED-via-sprite-helper (CYCLE 11, 24 bytes, no client vertex buffer). See §5.2. |
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
> build/invert, and the frustum construction were re-confirmed this lane. **World projection is
> right-handed (`D3DXMatrixPerspectiveOffCenterRH`); the UI ortho path is left-handed
> (`D3DXMatrixOrthoOffCenterLH` / `D3DXMatrixOrthoLH`) — RH/LH split per pass, static fact
> (CONFIRMED).** Matrix major-order and the world up-axis remain **capture/debugger-pending** (the
> RH/LH distinction narrows that item to the world transform handedness; the full up-axis and
> row/column-major order still need a live read).

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
assumed from a slot default — see §4.2. The `GRSTransparency` slot (id 15 in the 18-slot hierarchy)
acts as a cross-slot master override: when active it forces alpha-blend ON and Z-write OFF regardless
of the `GRSBlending` (slot 1) and `GRSDepthMask` (slot 5) settings — confirmed coupling matrix in
`Docs/RE/structs/render_state.md §7`.

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

**UI / HUD bucket (re-walked 2026-06-21 / reconciled 2026-06-22, binary-won).** The UI bucket has
two distinct entry paths that differ fundamentally in their blend setup; the single "UI / HUD" row
is therefore split above.

**In-game HUD panels** run a 2D-ortho enter at the head of every panel draw: depth test OFF, depth
write OFF, lighting OFF, cull clockwise, fill solid, an orthographic projection (near/far roughly
−300 .. +300) over identity world/view, and **alpha-blend left disabled at bucket-enter** — each
quad or glyph opts into translucency itself through its own blend state (the canonical translucent
pair being source-alpha / inverse-source-alpha). No alpha-reference is written (alpha-test stays
disabled). This per-class blend switching is the correct reading for the in-game HUD path; it is
confirmed by the observation that in-game panel draw routines (for link/inventory-type panels)
explicitly set SRCALPHA / INVSRCALPHA for their atlas content and only switch to additive for
specific overlay sub-draws.

**Front-end overlay (login, character-select, opening windows) — global ONE/ONE additive
(binary-won, 2026-06-22).** The front-end is a single global additive batch. A dedicated per-frame
state-setup entry point (canonical name `Diamond_UI_SetRenderStateAndDraw`) runs **once** before
the entire window/widget tree is walked. In order it: disables Z-write, lighting, fog, and
last-pixel; enables alpha-blend (`ALPHABLENDENABLE = TRUE`); and sets `SRCBLEND = ONE` /
`DESTBLEND = ONE`. Then it calls the window draw. **The per-element draw path — every widget
subclass (`GUPanel`, `GUButton`, `GUList`, `GUTextbox`, `GULabel`, `GUShortLabel`, `GUCheckBox`,
`GUScroll`) and the leaf batch submitter (`UI_SpriteBatch_SubmitQuad`) — issues zero
`SetRenderState` calls for blend.** Every front-end element inherits the global ONE/ONE state.

Consequence: **every class of front-end UI element — background atlas panels, framed window art,
buttons, labels, list rows, text fields, checkboxes, scrollbars, and overlay sprites — draws with
ONE/ONE additive**. The hypothesis that "opaque background atlas panels = SRCALPHA/INVSRCALPHA,
overlay sprites/effects = ONE/ONE additive" is **refuted for the front-end** — the binary confirms
global additive applies to every element, including opaque-looking atlas panels. Per-element
opacity is expressed through the vertex diffuse alpha byte (`alpha<<24 | rgb`), not through a
per-class blend-mode change. The art convention that makes this correct is that UI atlas panels are
authored on a black background, so ONE/ONE additive composites them cleanly over a black backdrop
or the rendered 3D scene.

Additionally, the front-end entry clears fog, dither, and alpha-test and configures texture stage
0 = select-arg-1 / diffuse with stage 1 disabled and linear/linear/mip-none sampling.

A 3D item-preview inset embedded inside an inventory panel (in-game, not front-end) temporarily
re-enables depth test and then restores it on the way out — confirming that "no depth" is the
in-game HUD default that the inset deviates from.

**Contrast: where per-draw blend switching exists (NOT the front-end).** Per-class blend
switching is real for world particle/effect drawers (which switch SRCBLEND/DESTBLEND per item via
a blend-mode field: mode 0 → SRCALPHA/INVSRCALPHA, mode 1 → SRCALPHA/ONE, mode 2 → ONE/ZERO)
and for the in-game HUD path (which sets SRCALPHA/INVSRCALPHA at atlas-panel level and may switch
to additive for specific overlay sub-draws). A spec that applies the per-class rule
(opaque = SRCALPHA, overlay = ONE/ONE) to the **front-end** is the mistranslation; a spec that
applies global ONE/ONE to the **in-game HUD** would be the inverse error.

The per-quad translucent source/dest pair for the in-game panels and the effective depth-write
state at the very first in-game UI draw remain **DBG-PENDING** (a live device render-state read
during a panel draw is required to fully confirm the in-game HUD per-quad pair). The front-end
ONE/ONE verdict is static-only; the decisive live confirmation (breakpoint
`Diamond_UI_SetRenderStateAndDraw`, read live render-state 19/20/27 at the login/char-select
screen) is recommended as a `re-validator` (`?ext=dbg`) pass before promotion to implementation.
See `Docs/RE/_dirty/ui_blend_state.md` (static analysis notes, dirty-room, gitignored).

> **CYCLE 11 re-open (front-end blend).** The front-end UI leaf quads are submitted through the D3DX sprite helper's Begin(alpha-blend) call WITHOUT the do-not-modify-render-state flag — which reprograms the device to source-alpha / inverse-source-alpha for each Begin/End at draw time, potentially OVERRIDING the global one/one the front-end wrapper sets once before the window tree. The static path cannot finish this (the device writes happen inside the sprite-helper runtime). Therefore the '§4.2 front-end = global one/one additive' verdict is DOWNGRADED to **DEBUGGER-PENDING** (needs a live render-state read of source-blend / dest-blend / alpha-blend-enable at a front-end sprite-quad draw). The IN-GAME HUD per-quad opt-in (alpha-blend disabled at bucket-enter, each panel opting in source-alpha / inverse-source-alpha) is UNAFFECTED and stays CONFIRMED.

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
>
> *Cross-link: `Docs/RE/specs/character_rendering.md §3.1/§4` confirms the per-part programmable
> draw detail: `SetStreamSource` with stride 32 (step 8 of §3.1), `DrawIndexedPrimitive` with
> primitive type 4 (`TRIANGLELIST`, step 9), and BT.601 luma coefficients at VS register c9
> (`character_rendering.md §4.1`).*

### 5.2 Vertex declaration / FVF per object type

**Confidence: CONFIRMED for world geometry and FX strides; particle stride CONFIRMED (CYCLE 11); UI/HUD CONFIRMED-via-sprite-helper (CYCLE 11).**

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
| **Billboard / particle / water** | Inherits the FX `position | diffuse | 1 texcoord` format within the FX overlay; particles batch through a shared dynamic vertex buffer. | **24** | CONFIRMED (CYCLE 11): particle/FX vertex stride = 24 bytes (position + diffuse + one UV) — consistent with the FX 24-byte declaration; particle VB uses the same layout. |
| **UI / 2D HUD (in-world 2D overlays and text quads)** | Drawn through the D3DX sprite helper — NO client-side UI vertex buffer. The sprite helper's own vertex format is transformed-position + diffuse + one UV = 24 bytes. | **24** | CONFIRMED-via-sprite-helper (CYCLE 11): the in-world 2D UI/HUD and text quads are submitted through the D3DX sprite helper; there is no client-authored UI vertex buffer. |

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
| TEX2 — bloom blur | **1024 / divX × 1024 / divY** (allocation uses a 1024 base, not backbuffer-base; render region is backbuffer / divX × backbuffer / divY — `[open-question]` above 1024×1024, see note below; `Docs/RE/specs/post_processing.md §4.1`); no fallback | The blurred, downsampled glow buffer. |

> **`[open-question]` TEX2 allocation base vs. render region (`Docs/RE/specs/post_processing.md §4.1`).**
> Wave-5 static analysis shows the `D3DXCreateTexture` call for TEX2 allocates from a **1024 base**
> (`1024/divX × 1024/divY`), not backbuffer ÷ divisors. The blur-pass orthographic projection is
> **backbuffer-scaled** (`backbuffer W / divX × backbuffer H / divY`). These agree when the backbuffer
> does not exceed 1024 in either dimension; for higher backbuffer sizes the render region exceeds the
> allocated texture. Confirm via debugger (breakpoint the TEX2 `D3DXCreateTexture` call) before porting
> to resolutions above 1024×1024. The §6.3 description "TEX2 is rendered at backbuffer ÷ divisor"
> refers to the **render region**, not the **allocated size**.

The toon ramp LUT (`data/shader/toonramp.bmp`) is loaded into a separate slot for the cel pass — see
`formats/shaders.md`. A separate shadow-map render target (built from a shadow texture asset, sized
to a tiered quality cap) is used by the ground/actor shadow buckets and is **not** part of the glow
chain.

> **CYCLE 14 correction (f61f66a9): offscreen-enable flag stored at >=2 sites.** The device-creation
> context contains at least two distinct call sites that store the value 2 into the offscreen-RT /
> post-process enable flag on the scene object — not at a single site as previously implied in
> `formats/shaders.md §C5.6b` (CYCLE 11). The `D3DXCreateRenderToSurface` call is made from exactly
> 4 sites across exactly 2 systems: the shadow-map initialiser (`ShadowManager_Init`, 1 target) and
> the cel/glow initialiser (`Renderer_InitCelGlowShaders`, 3 targets — TEX0/TEX1/TEX2); no
> render-to-surface call exists for a water renderer (none-for-water confirmed SAME). The gate-polarity
> analysis and Godot fidelity implications live in `formats/shaders.md §C5.6b`.

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
per axis when the configured value is zero or absent. TEX2 **render region** is `backbuffer / divisor` on each axis (allocation uses a 1024 base —
see §6.1 `[open-question]` note and `Docs/RE/specs/post_processing.md §4.1`).

> **Config source (SAMPLE-VERIFIED) — `data/script/display.lua` glow range.** The shipped
> `data/script/display.lua` sets `DISPLAY_GLOW_RANGE_X = 1` and `DISPLAY_GLOW_RANGE_Y = 1`
> (SAMPLE-VERIFIED values; powers of 2 are valid per the file comment). At range 1×1 the downsample
> is effectively 1:1 — TEX2 is full-resolution, not ÷2 — which **differs from the binary's
> hardcoded default ÷2**. Whether the loader applies the configured 1 directly (so TEX2 is full-res)
> or the binary's ÷2 fallback dominates is part of the IDA-pending reconciliation (§6.4 / C5).
> `// spec: Docs/RE/specs/rendering.md §6.3`

**Composite weights c0 / c1 — apply path and defaults (CYCLE 11, binary-won).** In-world scene brightness is applied as **pixel-shader constants in the offscreen composite stage** — there is **no device gamma-ramp call** (no SetGammaRamp / SetDeviceGammaRamp path exists in the binary). The composite pass uploads two scalar pixel-shader constants from code (reads them off the scene/post object and broadcasts each one 4-wide to a register): **a base-brightness multiplier feeds composite pixel-shader constant slot 0 (c0)** and **a glow-brightness multiplier feeds slot 1 (c1)**, both uploaded just before the fullscreen toon/glow composite quad. Stage 0 binds the bright/edge-extract RT, stage 1 binds the bloom RT, and the two scalars weight them. **The upload itself is CONFIRMED.**

**Constructor defaults are 1.0 / 1.0 (CYCLE 11 binary-won).** An earlier reading reported uploaded defaults of **c0 = c1 = 0.5**, derived from an inferred ×0.5 code-side half-scaling applied to 1.0 base multipliers. CYCLE 11 re-walk found this ×0.5 was a **floating-point-stack decompiler artifact, not a real halving** — the constructor seeds both multipliers at **1.0** and there is no code-side ×0.5 divide in the producer site. The correct binary defaults are therefore **c0 = 1.0** (base-brightness) and **c1 = 1.0** (glow-brightness). These are the **two tunable knobs** for the glow — they are **not** hardwired white. The binding is consistent with a `saturate(edge·c0 + bloom·c1)`-style composite. The exact pixel-shader arithmetic lives in the external `.psh` and is documented in `formats/shaders.md` — only the c0 / c1 scalars come from code.

> **Config source (SAMPLE-VERIFIED) — `data/script/display.lua` glow brightness.** The shipped
> `data/script/display.lua` sets `DISPLAY_GLOW_BRIGHT_MULTI = 0.3` and `DISPLAY_BASE_BRIGHT_MULTI =
> 1.05` (SAMPLE-VERIFIED values; the world-geometry `DISPLAY_BASE_BRIGHT_MULTI` is owned by
> `environment.md §9`). `DISPLAY_GLOW_BRIGHT_MULTI = 0.3` is the **glow/bloom post-pass multiplier**
> — intentionally dim (30%). This is the live glow-brightness that feeds c1 at runtime. With the CYCLE
> 11 binary-won defaults of 1.0 / 1.0, the shipped config values (base ≈ 1.05, glow 0.3) are read
> verbatim from the VFS file and overwrite the defaults — so the live uploaded values are approximately
> `c0 = 1.05` and `c1 = 0.3` (capture/config-sourced). The producer site that writes c0/c1 onto the
> scene object from the display config is **IDA-pending** (§6.4 / C5). `// spec: Docs/RE/specs/rendering.md §6.3`

**Present blend (corrects earlier "additive present" wording).** Structurally the present pass blits
the composited TEX0 to the backbuffer as a straight **opaque copy** — **source = ONE, destination =
ZERO**, not an additive (ONE / ONE) blend (alpha-blend is enabled, but the ONE / ZERO factor pair
makes it a copy). The additive "glow add" math is performed **inside the composite pixel shader into
TEX0** (pass 4); the present (pass 5) merely copies that result to the backbuffer, so **do not re-add
at present time**. The present-stage blend bytes are **static-confirmed** by wave-5 walk of
`Renderer_DrawScene_OffscreenRT_0` (`Docs/RE/specs/post_processing.md §8.1`):
`D3DRS_SRCBLEND` = **D3DBLEND_ONE (value 2)**, `D3DRS_DESTBLEND` = **D3DBLEND_ZERO (value 1)**.
Alpha-blend is enabled but the ONE/ZERO factor pair makes this an opaque copy. The glow
contribution is accumulated entirely inside the composite pixel shader (pass 4) and must not
be re-added at present time.

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

> **`[open-question]` Blur loop claim revised — CYCLE 7 vs. wave-5 static analysis.** The CYCLE 7
> reading attributed a **fixed pass-loop count of 16** and a "2×2 box blur" structure to the blur
> routines. Wave-5 static analysis of both `Renderer_GlowExtractDownsample` and
> `Renderer_GlowBlurDownsampled` (`Docs/RE/specs/post_processing.md §6.1–6.2`) found **exactly one
> `DrawPrimitiveUP` call per routine** (single `TRIANGLEFAN, 2 primitives`), with **no loop, no
> multi-tap offset accumulation, and no tap-weight array** in either function. The confirmed blur
> mechanism is: (1) bilinear minification of TEX0 into TEX2 (sampler MINFILTER = LINEAR) and
> (2) the glow pixel shader (`power1dx8.psh` by default, §6.5) applying a power-law scalar falloff.
> The integer immediate `16` (if present in the constructor) may reside in a different, unwalked
> code path or be structurally unused; RE-domain must walk the constructor to locate it before
> treating it as a draw-loop counter. **For a Godot port: a single bilinear-downsample + glow PS
> pass is the confirmed structure — do not implement a multi-pass blur loop (§8).** Cross-link:
> `Docs/RE/specs/post_processing.md §6.2`. The glow pixel shader is the single **editable-filename** shader, whose default
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
>
> **Loader canonical name (CONFIRMED, CYCLE 12 audit).** The `display.lua` config loader — the routine
> that parses both `DISPLAY_BASE_BRIGHT_MULTI` and the framerate key in one pass — is
> `DisplayConfig_ParseFramerate`. It is a shared display-config apply routine; this is the confirmed
> "same display-config apply-path" provenance that §6.3 and `environment.md §9.4` both reference.
> Per-stage shader-constant reads remain IDA-pending as before.

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

> **Status: `sample-verified` VALUES; APPLY-SITE CONFIRMED (CYCLE 11); NOT-YET-PORTED FEATURE.** The shipping
> `data/script/display.lua` defines a per-state **character render tint / alpha** table —
> a colour-grade applied to character meshes depending on the character's gameplay state (idle,
> selected, hit, poisoned, etc.). This is a distinct render feature from the world-geometry and
> glow brightness scalars; it is **not yet reproduced in the Godot port**.
>
> **Apply mechanism (CYCLE 11: apply site + state→tint mapping confirmed).** The per-actor tint is applied at the skinned-actor cel draw as **two pixel-shader constants — a multiply colour in slot 0 (alpha 1) and an add colour in slot 1 (with alpha)** — giving `out = multiply·in + add`. The active tint is selected by a per-actor state field decoded one-hot to an ordinal 0..8 (nine states: default / choice / hit / alpha / hidden / poison / type / anger / auto) indexing the nine-entry tint table (4-byte stride). (CYCLE 11: apply site + state→tint mapping confirmed; the nine tint VALUES were already sample-verified.)

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
- **`DISPLAY_LIGHT_RATIO` — PARSED BUT NOT CONSUMED (CYCLE 11, binary-won).** A full code scan found a constructor default and a config-parser write for `DISPLAY_LIGHT_RATIO` but **no reader** — the same dead signature as the confirmed-dead framerate field. It does not affect character lighting in the shipped client. (CYCLE 11 binary-won; a remote struct-copy edge case is the only residual, flagged for a debugger pass.) Cross-reference: `environment.md §9`. (Note: earlier readings in `environment.md §9.2` described `DISPLAY_LIGHT_RATIO = 0.5` as a live character light-colour correction — that characterization is superseded by this CYCLE 11 finding for purposes of this spec.)

> **Citation breadcrumb.** A C# constant carrying any of these values cites this section:
> `// spec: Docs/RE/specs/rendering.md §6.7`. **Source citation:** all values are from the
> `data/script/display.lua` config layer. **Apply-path:** CYCLE 11 pinned — see apply mechanism note above. State-selection logic (the runtime per-actor state field decode) is confirmed; the nine tint VALUES are SAMPLE-VERIFIED.

---

## 7. Known unknowns

- **Particle vertex-buffer stride** — RESOLVED (CYCLE 11): stride = 24 bytes (position + diffuse + one UV), confirmed consistent with the FX 24-byte contract. See §5.2.
- **UI / 2D HUD FVF + stride** — RESOLVED (CYCLE 11): the in-world 2D UI/HUD and text quads are drawn through the D3DX sprite helper with no client-side UI vertex buffer; stride = 24 bytes (transformed-position + diffuse + one UV). See §5.2.
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
- **`DISPLAY_CHAR_BRIGHT_*` per-state character tint apply-path (CYCLE 11 CONFIRMED — NOT-YET-PORTED).** The 9-state tint/alpha values (§6.7) are SAMPLE-VERIFIED; the apply-site and state→tint mapping are now CONFIRMED (CYCLE 11): two pixel-shader constants at the skinned-actor cel draw (multiply slot 0, add slot 1), state decoded one-hot to ordinal 0..8 indexing the nine-entry table (4-byte stride). This remains a **recovered-but-unported** character render feature. See §6.7.
- **Live display-config values** — the actual shipped glow-range divisors, base/glow brightness
  multipliers, and the power-shader override are now SAMPLE-VERIFIED from `data/script/display.lua`
  (§6.6); the binary defines the defaults (2,2 / 1.0 / `power1dx8`). Note: the ×0.5 code-side scaling
  previously inferred is RETRACTED (CYCLE 11: FP-stack artifact — binary defaults are 1.0 / 1.0 with
  no halving). The reconciliation between the shipped config and the binary defaults is IDA-PENDING
  (§6.4).
- **Internal per-primitive logic of the opaque sub-draws** (terrain / buildings / objects / actors)
  — their role and order in the opaque bucket is confirmed; their internal logic was out of scope.
- **Whether terrain actually populates the NORMAL field** — the world geometry format declares a
  normal, but whether the terrain vertex copy writes it or zeroes it was not opened. PLAUSIBLE.
- **18-slot render-state cache mechanism + per-bucket blend/Z-write byte matrix (§4)** — recovered and
  byte-verified in a prior pass but **NOT re-walked this lane**; they live in the post-scene FX-overlay
  body and the cache class hierarchy. Re-confirm-pending: a focused follow-up should re-read the
  byte-exact source/destination blend pairs and the per-bucket Z-write directly there.
- **Present-stage blend bytes (the ONE / ZERO opaque-blit pair, §6.3)** — RESOLVED: static-confirmed
  by `Docs/RE/specs/post_processing.md §8.1` (wave-5 walk of `Renderer_DrawScene_OffscreenRT_0`).
  SrcBlend = D3DBLEND_ONE (value 2), DestBlend = D3DBLEND_ZERO (value 1) — opaque copy of TEX0 to
  backbuffer. See §6.3.
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
  pyramid), and treat the glow base/intensity knobs as the two composite scalars (base-bright c0 and
  glow-bright c1, binary defaults **1.0 / 1.0** — CYCLE 11 binary-won; the earlier "0.5 each" was a
  decompiler artifact; shipped config values are base ≈ 1.05, glow 0.3 — SAMPLE-VERIFIED). The additive "glow add" happens inside the composite
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
  IDA-pending. **`DISPLAY_LIGHT_RATIO` is confirmed parsed-but-dead in this build (§6.7, CYCLE 11)** — it does not compose with this layer in the shipped client.
- Mesh stride contract: **world geometry** (terrain / building / static / skinned) = **32-byte** XYZ (12) + NORMAL (12) + UV (8); **FX / billboard / particle** = **24-byte** XYZ (12) + DIFFUSE (4) + UV (8) (CONFIRMED CYCLE 11). **UI/HUD quads** use the D3DX sprite helper (no client vertex buffer) — also 24-byte transformed-position + diffuse + UV (CONFIRMED CYCLE 11).
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

## 9. Character-select 3D preview pipeline

> **Verification banner (this section)**
> - **verification:** *static-hypothesis* for the scene-construction call sequence, the six-slot
>   camera-manipulator layout, the scene-root selector flag, and the overlay-callback slot
>   population. *literal* for the camera FOV / near / far, the world anchor, the terrain
>   cold-start centre, the environment area id and time-of-day, the ambient-effect id and its
>   world position, the light fallback direction and scale, the fog file layout, and the
>   actor-preview scale. *sample-witnessed* (2026-06-22, decoded from the production VFS and
>   matching the committed format specs byte-for-byte) for the map000 fog parameters, the
>   directional/ambient light values, and the `map_option0.bin` skybox/sky-element flags (§9.3,
>   resolving open items O-1 / O-2 / O-3). *file* (still open — values live in VFS assets, not the
>   binary) for the sky-dome `.bin` colour tables, the sun/moon keyframe positions, and the
>   ambient-effect identity. Recovery based on static analysis of `doida.exe` against IDB `263bd994`,
>   plus VFS sample-witnessing of the map000 environment binaries. No debugger confirmation yet for
>   the remaining runtime open items in §9.5.
> - **ida_anchor:** 263bd994
> - **evidence:** [static-ida]

### 9.1 Architectural shape — one shared view, one scene

The character-select 3D preview is **not** a dedicated render target, a separate sub-viewport, or
an offscreen texture widget. The client uses the **single shared engine view** (the same engine
view object the main game world uses) and its **single engine scene root**. The character-select
builder seeds that shared scene with a new scene named `"select"` (the in-game world scene is
named `"charater scene"` — note the typo is original), a character-select camera, a
character-select camera manipulator, and one ambient effect. The same per-frame draw path
(device-step → camera/cull setup → scene-draw fork → Present) renders it, unchanged.

The 2D chrome (the front-end UI panels: slot portraits, confirm dialog, class/name labels) is
drawn as screen-space ortho overlays inside the **same** BeginScene / EndScene span, after the 3D
scene-root draw, using the front-end `ONE / ONE` additive blend state (§4.2). There is **no**
offscreen render-to-texture dedicated to character-select; the only RT path is the engine-wide
glow/bloom compositor (§6), shared with every scene when the post-process flag is set.

Consequence for a 1:1 port: one 3D viewport fills the window; the same scene-draw fork (§2)
renders it with the character-select camera's FOV and the six-slot manipulator; the 2D chrome
draws over the top via the front-end UI overlay path — **not** a separate sub-viewport or RT.

### 9.2 Scene construction — element inventory

The character-select 3D scene is built by a single dedicated function (canonical name:
`SelectWindow_BuildScene`). It is called exactly once from the larger character-select window
initialiser (`SelectWindow_BuildAndInit`) when the client enters game-state character-select.
Every element listed below is created **exactly once**; none is duplicated.

**Environment load (literal values):**
| Parameter | Value | Confidence |
|-----------|-------|------------|
| Area id | **0** (directory token `"000"` — map000) | literal |
| Date seed | 0 | literal |
| Time-of-day (TOD) | **52,200 seconds = 14:30** | literal |
| Environment flags | 0x30 (decimal 48) | literal |

The environment load runs the same engine path the live game uses
(`Env_MapSetAndLoadArea`). It is a **pinned** literal TOD — the character-select screen always
renders at 14:30, it is not a live wall-clock.

**Camera (literal values):**
| Parameter | Value | Confidence |
|-----------|-------|------------|
| Type | Perspective | confirmed |
| Vertical FOV | **50.0°** (converted to radians; divided by aspect ratio) | literal |
| Near plane | **5.0** | literal |
| Far plane | **15,000.0** | literal |
| Aspect ratio | backbuffer width ÷ height (runtime) | parser |

Note: the in-game world scene uses FOV **65°** — the two FOVs must not be cross-wired.

**World anchor and terrain stream (literal values):**
| Parameter | Value | Confidence |
|-----------|-------|------------|
| World anchor (X, Y, Z) | **(2048.0, 0.0, −6144.0)** | literal |
| Terrain cold-start centre (X, Z) | **(508.0, −9734.0)** (anchor minus offsets 1540 / 3590) | literal |
| Terrain cold-start ring | 3×3 or forward 5×5 cells around the centre | parser |

**Camera manipulator — six-slot presets:**
The scene uses a dedicated `SelectCameraManipulator` with **exactly six slot anchors** arranged
around the world anchor. Each anchor position is stored relative to the origin and offset by the
world anchor (+2048.0, +0.0, −6144.0) to produce the six character-slot stage positions. Each
slot carries two angle-channel presets and shares a common distance scalar of **10.0**. The
initial active slot is **0**. The per-slot position vectors and angle-channel values are recovered
but classified as file-tier literals needing spec-author promotion; the rebuild requires exactly
these six anchors and twelve angle constants (see open item O-7 in §9.5).

**Ambient XEffect (literal values):**
| Parameter | Value | Confidence |
|-----------|-------|------------|
| Effect id | **380003000** | literal |
| World position (X, Y, Z) | **(508.483, 69.887, −9758.569)** | literal |
| Direction | identity (0, 0, 0) | literal |
| Scale | **1.0** | literal |
| Loop | **1** (looping) | literal |

The effect manager is cleared of all active user effects immediately before this spawn, guaranteeing
a single-instance ambient effect. The position sits near the terrain cold-start centre (508, −9734)
but at Y ≈ 69.9, placing the effect as a floor-level ambient in front of the character row rather
than as a background element.

**Scene-graph children:** the scene root receives exactly **two** children — the camera manipulator
node and a global light/scene node (sourced from an engine singleton). No additional scene children
are created by the character-select builder.

**Preview actors** are spawned by a separate per-actor function (`ActorPreview_CreateAndConfigureRender`),
not by the scene builder. Each preview actor: spawns from a character descriptor, receives a scale of
**35.0**, has its idle motion applied immediately (via the actormotion idle chain — see
`specs/skinning.md` and CLAUDE.md's motion-chain recovery notes), and is configured with a
distinct texture-stage material state (see §9.3). The number of simultaneous preview actors (one
per slot vs. one selected slot only) is an open item (§9.5 O-6).

### 9.3 Sky, light, and fog — map000 environment at TOD 14:30

> **Status update (2026-06-22): the map000 environment binaries are now sample-witnessed.** The
> `fog0.bin` / `light0.bin` / `map_option0.bin` literals below were decoded from the user's mounted
> production VFS and **match the committed format specs byte-for-byte** — they resolve the former
> file-tier open items O-1 / O-2 / O-3 (§9.5). The recovered values and layouts are owned by
> `formats/environment_bins.md` and `formats/sky.md`; cite those for any constant.
> `// spec: Docs/RE/formats/environment_bins.md`  `// spec: Docs/RE/formats/sky.md`

The character-select backdrop is the **ordinary map000 environment rendered at a pinned
time-of-day**, not a bespoke skybox or dedicated interior cell. `Env_MapSetAndLoadArea` (called
with area id 0) runs the full sky-system initialisation, loading the sun, moon, star-dome,
cloud-dome, material, directional and ambient light, and fog from the map000 `data/sky/dat/`
asset files. The sky dome is **procedural** (sun + moon + star-dome + cloud-dome); the static
enclosure mesh (`sky0.box`) is **off** for map000 (see below).

**`map_option0.bin` per-area flags (40 B = 10× u32; sample-witnessed for map000).** Decoded
pattern `[0, 0, 1, 1, 1, 1, 1, 0, 0, 0]` = the **standard outdoor area** row.
| Flag | Value | Effect |
|------|:-----:|--------|
| `is_dungeon` / `sight_distance` | 0 / 0 | outdoor, free sight range |
| `lensflare` / `stardome` / `clouddome` | **1 / 1 / 1** | lens-flare, star-dome, cloud-dome **on** |
| `sun` / `moon` | **1 / 1** | sun + moon billboards **on** |
| `skybox` (SKYBOX) | **0** | static `sky0.box` dome **NOT loaded** for map000 |
| `indoor` / reserved | 0 / 0 | not an interior |

So the procedural sky (lens-flare + star-dome + cloud-dome + sun + moon) is fully enabled and the
static skybox is disabled for character-select. `// spec: Docs/RE/formats/environment_bins.md §1`

> **Static skybox is a confirmed-absent feature, not just disabled.** No `sky0.box` exists in the
> production VFS for map000 — nor any `.box` asset in any area (a census of the full archive found
> **zero** `.box` files). The map000 `SKYBOX` flag being 0 keeps the load path gated off, and there
> is no asset to load even if it were on. There is **no "cavern backdrop" skybox**: any enclosed look
> comes from terrain geometry plus the fog / ambient / sky-colour data, not a static mesh. A faithful
> port should **not** implement a `.box` loader — use a synthetic sky-dome tinted by the colour
> tables. `// spec: Docs/RE/formats/sky.md §A`

**Directional and ambient light (sample-witnessed from `light0.bin`, keyframe 29 = TOD 14:30).**
Light colours are read directly in the **[0, 1] float domain (no /255)**.
- **Directional colour:** a very dark grey diffuse, **(0.047, 0.047, 0.047)**, applied raw — so the
  scene is **not** lit primarily by the directional term.
- **Light direction:** there is **no per-keyframe direction** in the light data; the only direction
  is the static fallback vector **(−7.0, 7.0, 20.0)** (normalised ≈ **(−0.314, 0.314, 0.896)**). The
  day/night cycle does **not** rotate this vector in the light file (sun/moon orbiting is the separate
  billboard system).
- **Ambient floor:** the per-keyframe ambient table in the file is **inert at runtime** — it is
  multiplied by a global ambient gate that is **0.0**, so its contribution is zero. The actual
  device-ambient floor is the additive brightness offset from the user `OPTION_BRIGHT` setting
  (default 100 → device ambient saturates to **full white (1, 1, 1)**). The character-select scene is
  therefore lit primarily by this **white ambient floor**, not by the dark directional colour. A
  user-saved lower `OPTION_BRIGHT` would lower this floor (a per-machine runtime residual, §9.5 O-4).

`// spec: Docs/RE/formats/environment_bins.md §9`

**Fog (sample-witnessed from `fog0.bin`, 204 B).** The applied fog path is **LINEAR** (the D3D fog
type is not stored in the file; the observed applied path is linear). The fog colour is
**synthesised** from the sky-material LUT — the file's `data_load_flag` is **0**, so the on-disk
`fog_colors[]` table is **not consumed** (the table present in the file is an unused editorial
artefact). The static start / end fractions (0.5 / 0.9) are a **baseline only**: the live fog range
is driven per-tick from the `light0.bin` fog scalar (keyframe-29 scalar 25.0 × 3.0 = a **75.0-world-
unit** LINEAR range); the secondary haze scalar is 0.0 (no haze). LINEAR-path density is 0.
`// spec: Docs/RE/formats/environment_bins.md §2`  `// spec: Docs/RE/formats/sky.md §B`

> **Read-from-data, do not hardcode.** Every decoded byte matched the committed specs, so a faithful
> port should **parse** `fog0.bin` / `light0.bin` / `map_option0.bin` and drive the look from those
> values rather than baking colours in. The two facts most likely to be wrongly hardcoded are the
> **white ambient floor** (from `OPTION_BRIGHT`, not from the inert in-file ambient table) and the
> **static light direction (−7, 7, 20)** (there is no per-keyframe sun direction).

The OPTION_SKY quality setting (ini-driven, values 1 / 2 / 3, maps to sky-brightness scales
1.0 / 0.71 / 2.0) also applies to the character-select sky but is user-dependent (§9.5 O-4).

### 9.4 Per-frame render path for character-select

Character-select participates in the **standard per-frame device-step loop** (§2.0) with exactly
**one active view** (the shared engine view). The per-frame sequence is therefore identical to the
general pipeline (§2) with the character-select camera's 50° FOV and the "select" scene root.

**Draw order** (consistent with the general pipeline §3):
1. Clear (TARGET | ZBUFFER, depth 1.0) — background clear colour `0xFF505050` (dark grey ARGB).
2. Sky-callback pass (sky-dome draw: sun/moon/star-dome/cloud-dome if enabled).
3. Scene-root draw: terrain tiles around (508, −9734) + preview actor(s) + ambient XEffect 380003000.
4. Front-end UI overlay: character-slot chrome, portrait panels, confirm dialog — via the
   `Diamond_UI_SetRenderStateAndDraw` entry point using the **ONE / ONE additive** blend state (§4.2
   front-end row). All 2D panels inherit this global additive state.
5. EndScene → Present (single Present per frame, inside the device-step).

The glow/bloom post chain (§6) runs if the global post-process flag is set; whether it is enabled
during character-select is a runtime open item (§9.5 O-5).

**Scene-root selector.** The engine view holds two candidate cull-root pointers, selected by a
per-view byte flag. The character-select scene is installed into one of those slots; which slot it
occupies and which flag value the view carries during character-select are runtime open items
(§9.5 O-5).

**Preview actor material.** The preview actor configuration applies a distinct texture-stage and
sampler block — different from the opaque-world pass block (§4.2) — to the actor at spawn time.
The exact wrapper enum values for this actor material state are catalogued in the dirty-room RE
notes; they must not be conflated with the in-world material state. The actor draws with the cel
shader path if the post-process flag is set (§5.1a applies to skinned preview actors equally).

### 9.5 Open items (debugger / file-witnessing required)

These items are unresolved static hypotheses or file-tier values that must not be invented:

| ID | Item | How to resolve |
|----|------|----------------|
| O-1 | ~~`fog0.bin` for map000: fog type, colour, start/end, density~~ | **RESOLVED (2026-06-22, sample-witnessed):** LINEAR applied path; colour synthesised from the sky LUT (`data_load_flag = 0`, in-file table unused); baseline start/end 0.5 / 0.9 overwritten by the live `light0.bin` scalar (75.0-unit range); density 0. See §9.3. |
| O-2 | ~~`light0.bin` for map000: directional colour, direction vector, ambient floor at TOD 52,200~~ | **RESOLVED (2026-06-22, sample-witnessed):** directional (0.047, 0.047, 0.047) raw; static direction (−7, 7, 20); in-file ambient table inert (gate 0) — ambient floor is white (1,1,1) from `OPTION_BRIGHT` default 100. See §9.3. |
| O-3 | ~~Does `map_option0.bin` enable the static skybox? Does `sky0.box` exist for map000?~~ | **RESOLVED (2026-06-22, sample-witnessed):** SKYBOX flag = 0 (disabled); `sky0.box` is CONFIRMED-ABSENT (no `.box` asset in any area). No skybox to load — use a synthetic dome. See §9.3. |
| O-4 | Effective OPTION_SKY quality (1/2/3) on the reference machine → sky-brightness scale | ini / session config |
| O-5 | Post-process fork path during character-select (direct vs. offscreen), scene-root selector flag value, overlay callback slot population | Live `?ext=dbg` session |
| O-6 | Preview actor count / per-slot spawn loop: one actor per slot (up to six simultaneous) vs. single selected actor? | Live `?ext=dbg` session or slot-switch trace |
| O-7 | Six-slot camera anchor positions and twelve angle-channel constants (the float literals): ready for spec-author promotion once O-5 confirmed | Static (literals recovered; promotion pending re-validator sign-off) |
| O-8 | XEffect 380003000 → asset identity and visual appearance (per-character glow? floor ring?) | XEffect table trace from sample VFS |
| O-9 | Sun and moon literal positions at TOD 52,200 (interpolated from `sun0.bin` keyframes) | Witness from sample VFS |

The map000 environment binaries (O-1 / O-2 / O-3) are now **sample-witnessed** and match the
committed format specs byte-for-byte (§9.3) — the fog / light / skybox values are resolved. The
guidance to **read these files at runtime rather than hardcode** still holds (they are data, not
binary constants): parse `fog0.bin` / `light0.bin` / `map_option0.bin` and drive the look from them.
The remaining unwitnessed file-tier items are the sun/moon keyframe positions (O-9) and the XEffect
identity (O-8), plus the user-dependent OPTION_SKY quality (O-4). The binary literals in §9.2
(camera, anchor, XEffect spawn) are implementation-ready.

---

- `specs/game_loop.md`, `specs/client_runtime.md` — the **per-scene-state run loop** and the
  **frame-rate field** (the +48 / +0x30 rate seeded 60 and unoverwritten); §2.0 here describes only
  the render side of that loop (the device-step + Present + device-lost recovery) and is reconciled
  with — not contradicting — those specs.
- `specs/environment.md` — the **world-brightness scalar** (`DISPLAY_BASE_BRIGHT_MULTI = 1.05`) from the same `data/script/display.lua` config layer (`environment.md §9`); this spec owns the glow/bloom (§6.6) and per-state character-tint (§6.7) keys from that file. Note: `DISPLAY_LIGHT_RATIO` is parsed but confirmed dead in this build (§6.7, CYCLE 11 binary-won) — `environment.md §9` should be updated to reflect that finding.
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
- **§9 CharSelect 3D pipeline** depends on `specs/skinning.md` (idle-motion and actor-preview
  chains), `specs/environment.md` (map000 sky/light/fog — §9.3), `formats/animation.md`
  (actormotion idle column), and `formats/skn.md` (preview actor class/variant). Open items O-1
  through O-9 (§9.5) must be resolved by a sample-witnessing pass (O-1 / O-2 / O-3 / O-8 / O-9)
  and a live `?ext=dbg` session (O-5 / O-6) before the §9 camera literals are promoted to
  implementation. Flag for `names.yaml`: `SelectWindow_BuildScene`, `SelectWindow_BuildAndInit`,
  `SelectCameraManipulator`, `ActorPreview_CreateAndConfigureRender`, `Env_MapSetAndLoadArea`,
  `SkySystem_Init`, `Diamond_UI_SetRenderStateAndDraw`.
- Glossary: see `Docs/RE/names.yaml` (flag for canonicalisation: `DISPLAY_GLOW_BRIGHT_MULTI`,
  `DISPLAY_GLOW_RANGE_X/Y`, `DISPLAY_POWER` / `DISPLAY_POWERSHADER`, `DISPLAY_FRAMERATE`,
  `DISPLAY_CHAR_BRIGHT_*` state table; and the §9 canonical names above).
- Provenance: see `Docs/RE/journal.md`.

---

## Addendum — CYCLE 11 / Block A: char-select preview camera, material & ambient effect (binary-reconciled, static)

> Verification refresh, IDB SHA **263bd994**, static-only (CYCLE 11 / Block A, 2026-06-22).
> Reconciles two analyst passes against the binary (a yaw/pitch labelling that was swapped, then
> re-swapped, then settled by reading the orientation-arm consumption directly). The values below are
> the **binary-decided** final reading.

### A.1 Preview camera projection & scene fork

- **Projection:** vertical field of view **50 degrees** (then divided by the viewport aspect ratio),
  near plane **5.0**, far plane **15000.0**. *([CONFIRMED]* immediates.)*
- **Scene anchor:** the preview world is anchored at **(2048, 0, −6144)**; the terrain cold-start is
  centred near **(508, −9734)**; one ambient effect sits at **(508.483, 69.887, −9758.569)**.
- **Render fork:** the preview does **not** use a separate render target or a distinct post-process
  chain — it renders through the **shared** UI/world render-state-and-draw path into the same back
  buffer. The only things that make it a "preview" are a dedicated scene root + camera/rig, fog forced
  off (see `environment.md` addendum), a fixed time-of-day, and one ambient effect. *([CONFIRMED]* by
  absence — no offscreen-surface / set-render-target call on the path.)*

### A.2 The six-keyframe camera path (final yaw/pitch reading)

The preview camera is driven by a **6-slot keyframe table** plus **two parallel 6-element angle arrays**
— one **pitch** array and one **yaw** array (the earlier "12 angle channels" reading was a miscount;
it is 6 + 6). The orientation-arm step feeds the **yaw** array into the yaw-rotation quaternion and the
**pitch** array into the pitch (side-axis) quaternion — that consumption is what fixes the labelling.

| keyframe | role | armed? |
|---|---|---|
| **KF0** | initial pose: **yaw +2.4°**, **pitch −6.0°** | YES — armed at rig construction |
| **KF1** | settle pose: **yaw +0.785 rad (≈ π/4)**, **pitch −2.67°** | YES — armed at scene reset (the deferred populate) |
| KF2 … KF5 | further absolute poses exist in the table | **NO — statically dead** (never armed in this scene) |

*([CONFIRMED]* exactly two arm sites exist — one at rig construction (index 0), one at scene reset
(index 1); the keyframe-index field has no other writer, so KF2…KF5 are unreachable in char-select.)*

### A.3 Entry dolly & manual camera controls

- **Entry dolly:** a single **~2.0-second** blend from KF0 to KF1. The blend parameter advances as
  `elapsed × 0.0005` and clamps at 1.0; **position is linearly interpolated and orientation is
  spherically interpolated (slerp)**. A parabolic "bow" arc exists in the code but is gated on **both**
  the previous and current keyframe indices being ≥ 2 — which never happens here — so the char-select
  dolly is a **straight** KF0→KF1 ease, no bow. *([CONFIRMED]* the dt scaling, the lerp+slerp, and the
  bow gate.)*
- **Manual boom (dolly):** a wheel/key zoom accumulator moves the rig at **±10.0 units/second** with
  **no clamp**, gated by the boom action. *([CONFIRMED]* immediates.)*
- **Preview turntable:** the selected/created preview rotates at **±2.0 radians/second** — applied to
  the **previewed actor**, not the camera — and is zeroed on a slot change. *([CONFIRMED]*.)*
- **Free-look rate clamps:** the free-look is a **rate accumulator** with clamps **boom ±4**,
  **yaw-rate ±1**, **pitch-rate ±1** and a small dead-zone; there is **no fixed absolute Euler
  min/max** — the absolute orientation is bounded only by easing back toward the armed keyframe target.
  *([CONFIRMED]* the clamp magnitudes.)*

### A.4 Preview actor material / sampler

The preview actors use the **standard character material** — there is **no** preview-only material.
The character draw is shader-lit (fixed-function lighting off), renders with **fog off**, uses
**alpha blend SRC_ALPHA / INV_SRC_ALPHA** with **alpha-test off**, and draws as a **triangle list**.
*([CONFIRMED]* the render-state block on the character draw path.)*

> **UNVERIFIED (runtime residuals):** the texture **filter** mode actually bound at the character draw
> (a point-filter default is set at preview-create time while the world toon pass uses linear — the
> effective filter at the char-select draw is debugger-pending) and the **cull** mode (no explicit cull
> state is set on the path, so backface-cull-default vs two-sided is unconfirmed). These do not change
> the material's blend/lighting shape.

### A.5 Ambient effect

The char-select scene clears all active user effects and then spawns exactly **one** ambient effect,
**id 380003000**, at **(508.483, 69.887, −9758.569)**, identity direction, **scale 1.0**, **looping**.
It is spawned at a single site image-wide (single-spawn guarantee). *([CONFIRMED]* the id, position,
loop, scale, and the clear-then-spawn sequence.)* The effect's on-disk filename is VFS-derived (there is
no in-binary filename string) — treat the asset filename as an external mapping, not a recovered string.

> spec path: `// spec: Docs/RE/specs/rendering.md`

---

## 10. Shadow Projection and View Matrices

**Confidence: CONFIRMED.**

The ground and actor shadows are generated using a dedicated shadow mapping pipeline consisting of a projection matrix, a light-view look-at matrix, and a depth-bias mapping matrix.

### 10.1 Shadow Perspective Projection Matrix
The shadow projection matrix is constructed using left-handed perspective field-of-view mathematics:
* **Vertical Field of View (FOV):** `0.39269909` radians (exactly $\pi / 8$ or 22.5 degrees).
* **Aspect Ratio:** `1.0`.
* **Near Plane:** `0.0`.
* **Far Plane:** `10000.0`.

### 10.2 Depth-Bias / Texture Coordinates Mapping
To map the projected coordinates from standard clip space $[-1, 1]$ to texture UV coordinate space $[0, 1]$, a transformation matrix is built:
* **Scale Vector:** `(0.5, -0.5, 1.0)`
* **Translation Vector:** `(0.5, 0.5, 0.0)`
* **Formula:** `DepthBiasMatrix = Scaling * Translation`

### 10.3 Shadow Light-View look-at Matrix
The light-view matrix is constructed using left-handed look-at mathematics:
* **Target Position:** Centered directly on the local player character position coordinates.
* **Up Vector:** Fixed as the vertical axis `(0.0, 1.0, 0.0)`.
* **Eye Position:** Computed by offsetting the target position based on daylight cycle angles:
  * $Eye.x = Target.x + \cos(Yaw) \times Multiplier \times 10.0$
  * $Eye.y = Target.y + Multiplier \times 10.0$
  * $Eye.z = Target.z + \sin(Yaw) \times Multiplier \times 10.0$
* The final view-projection matrix is computed by multiplying the inverted camera matrix with the projection and depth-bias matrices.

---

## 11. Direct3D 9 Device Wrapper and Reset Lifecycle

**Confidence: CONFIRMED.**

The client does not employ a separate abstract class to wrap Direct3D 9 device calls; the main `Renderer` instance serves as the direct device holder.

### 11.1 Wrapper Structure
* **Device Cache:** The `IDirect3DDevice9` interface pointer is cached directly inside the `Renderer` structure at a dedicated offset (offset `0x2B728`).
* **Default Texture:** The renderer initializes a default 2×2 solid grey texture (filled with values `120, 120, 120, 80`) in memory to act as a fallback when normal textures fail to bind.

### 11.2 Creation & Recovery Lifecycle
* **Device Initialization:** The renderer calls `Direct3DCreate9` and creates the active device via `IDirect3D9::CreateDevice` over the Win32 window handle, mapping present parameters to target resolutions (falling back to a 60 FPS presentation refresh rate in windowed modes).
* **Cooperative Level Checks:** Checked per frame loop iteration:
  * **Device Not Reset (`D3DERR_DEVICENOTRESET`):** Invokes the cooperative recovery sequence, issuing `Reset` with the updated parameters and rebuilding dynamic textures, shaders, and render targets.
  * **Device Lost (`D3DERR_DEVICELOST`):** Pauses frame execution, sleeping for `1000` ms before issuing another status check.

---

## 12. Screenshot Capture Pipeline

**Confidence: CONFIRMED.**

The client provides two screenshot formats: standard BMP and compressed JPEG. Both pipelines search for a free file name sequentially to prevent overwriting existing captures.

### 12.1 BMP Capture Pathway
1. Loops through file indices (`Value` starting from 1) using `"screenshot/screen%d.bmp"`.
2. Tests if the file exists using `CreateFileA`. If the file exists, the handle is closed and `Value` is incremented.
3. Once a free file name is found (where `CreateFileA` returns an invalid handle), the backbuffer render target surface is acquired.
4. Saves the raw surface directly to disk using `D3DXSaveSurfaceToFileA` with the BMP image format specifier.

### 12.2 JPEG Capture Pathway (Intel JPEG Library Wrapper)
1. Formats a file prefix using the system time: `"screenshot/DO%Y%m%d_%H%M_<index>.jpg"`, searching for a free file name similarly.
2. Acquires the render surface in `D3DFMT_A8R8G8B8` (ARGB 32-bit format).
3. Locks the surface, reads the pixels, and converts them to a raw 24-bit RGB packed buffer based on the source surface formats (handling 32-bit ARGB/XRGB, 24-bit RGB, 16-bit RGB 565, and 15-bit RGB 555 conversions).
4. Configures the Intel JPEG Library (IJL) `JPEG_CORE_PROPERTIES` properties structure:
   * Sets `DIBWidth` / `JPGWidth` and `DIBHeight` / `JPGHeight` to the surface dimensions.
   * Sets `JPGQuality` to the desired compression level.
   * Sets `DIBBytes` to point to the packed RGB buffer.
   * Sets `DIBChannels` to `3` (RGB).
   * Calculates `DIBPadBytes` stride padding as `((3 * width + 3) & ~3) - 3 * width`.
   * Sets `JPGFile` to the output file path.
5. Invokes `ijlInit`, `ijlWrite` (using `IJL_WRITE_WHOLEIMAGE`), and `ijlFree` to write the JPEG to disk.
6. Frees temporary memory and unlocks the Direct3D surface.

---

## 13. DirectX Version Detection

**Confidence: CONFIRMED.**

The client verifies system DirectX features by reading version resources directly from Windows system libraries.
* **Libraries Inspected:** `ddraw.dll`, `d3d9.dll`, `dinput.dll`, `dplayx.dll`, `d3drg8x.dll`, `mpg2splt.ax`, and `dpnet.dll`.
* **API Used:** Reads the file version information resources using `GetSystemDirectoryA` followed by standard version query APIs.
* **Logic:** Compares build numbers against hardcoded constants representing DirectX versions (e.g. checking for Direct3D 9 build signatures such as `0x10371` or `0xA280371` to verify DX9.0a/b/c presence). Assigns minor/major version integers to global compatibility flags to determine rendering pathways.

---

## 14. Cel / Toon Shader Loading (dotoonshading)

**Confidence: CONFIRMED.**

The characters' cel-shaded render passes utilize two distinct toon shading pixel shaders.
* **Cel Pixel Shader #1 (`data/shader/dotoonshading.psh`):** Compiled and stored inside the renderer class structure. This handles the primary character toon lookup.
* **Cel Pixel Shader #2 (`data/shader/dotoonshading2.psh`):** Compiled and stored inside the renderer class structure. This handles variant character toon highlight configurations.
* **Assembly at Load Time:** Shaders are loaded by name from the virtual filesystem (VFS) if mounted, falling back to direct disk reads. They are compiled from source assembly files on initialization and reload requests. No extra shaders are loaded under these names; they map directly to the cel/toon character rendering stage.

