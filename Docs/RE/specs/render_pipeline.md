# Spec: Render Pipeline — Frame Orchestration, Per-View Processing, and Draw-Order Detail

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> describes the **frame-level orchestration** (the multi-view circular driver loop), the
> **per-view two-phase processing model** (update then draw), the **frame-setup pass
> internals**, the **direct and offscreen draw orders in full detail**, the **render-pass
> installer and subsystem-wiring globals**, the **complete GView render-view object layout**,
> the **opaque texture-stage and sampler configuration**, and the **cel vertex-shader constant
> registers**.
>
> Per-pass render-state behaviour, the glow/bloom chain, and the UI blend model are documented
> in `Docs/RE/specs/rendering.md`. Shader details are in `Docs/RE/formats/shaders.md`. The
> scene-graph class hierarchy and cull/draw mechanism are in `Docs/RE/structs/scene_graph_nodes.md`.
> This spec cross-links rather than duplicates those.
>
> Every render-pipeline constant cited in C# must reference this file:
> `// spec: Docs/RE/specs/render_pipeline.md`.

> **Verification banner**
> - **verification:** *static-confirmed* for all findings in this document, recovered by
>   following the static control flow of the four spine routines —
>   `Engine_DeviceStepAndPresent`, `Renderer_SetupCameraAndFrustum`,
>   `Renderer_DrawScene_Fork`, `Renderer_DrawScene_Direct`,
>   `Renderer_DrawScene_OffscreenRT_0` — and the render-pass installer. Items explicitly
>   tagged `[debugger-confirm]` are static hypotheses awaiting a live `?ext=dbg` session
>   before treating them as implementation facts.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the frame orchestration, view layout, pass
>   installer, direct-path draw order, cel VS constants, and texture-stage block. Items tagged
>   `[debugger-confirm]` are NON-BLOCKING residuals.
> - **evidence:** [static-ida]
> - **wave-3 reconciliation (2026-06-28):** §1.1 driver-layout table corrected and extended
>   per `Docs/RE/structs/render_driver.md` (authoritative for the full 56-byte frame-driver
>   layout): field at +24 is `view_count` / `_Mysize` (MSVC `std::list` element count), not
>   an "active flag"; driver carries no vtable (+0 reserved). §7 GView layout and §13 items
>   4/5 corrected per `Docs/RE/structs/gview.md` (authoritative for the per-view object):
>   field at +36 is the scene root (`GGroup*`/`GScene*`), not a "GView sub-object pointer";
>   +112 is `Diamond::GCull*` (owned, 736 bytes); +116 is `GCullPipeline*` (secondary, default
>   null); vtable at +0 confirmed (`Diamond::GView`, RTTI). §13 item 5 resolved — render-view
>   IS `Diamond::GView`, confirmed via RTTI (`Docs/RE/structs/gview.md §1, §6.2`).
> - **wave-5 reconciliation (2026-06-28):** §8 scene/post object table split and expanded per
>   `Docs/RE/specs/post_processing.md §3` (authoritative for the five cel/glow shader-handle
>   slots): the single "+177880 composite/glow" entry is replaced by +177876 (glow-blur PS) and
>   +177880 (composite PS); cel VS (+178320), cel PS normal (+178324), cel PS stealth (+178328),
>   and glow-shader filename buffer (+179028) added with cross-references. §11 vtable key extended
>   with four `IDirect3DDevice9` slots per `Docs/RE/specs/character_rendering.md §10`:
>   SetRenderState (+228), SetTexture (+260), CreateVertexShader (+364), CreatePixelShader (+424).
> - **wave-11 deep-dive (2026-06-28):** §1.3 extended with the device-recovery short-circuit
>   (driver `+44` non-zero suppresses the frame draw until the device recovers). §2.3 expanded
>   with the full offscreen-flag decision logic: default 0 (direct); set to 2 only iff
>   `Renderer_InitCelGlowShaders` succeeds AND config-singleton field `+0x30` equals 1. §5
>   corrected: the composite + cel actors + transparent passes render into RT-A a second time
>   (second `RenderToSurface` pass on the same surface), not onto the back buffer; the present
>   pass then blits RT-A → back buffer with source ONE / destination ZERO blend. Glow extract
>   (RT-A→RT-B) is a plain fixed-function texture copy with no pixel shader; only glow blur
>   (RT-A→RT-C) applies the `power1dx8` shader. RT-A/RT-B/RT-C identity and routing fully
>   mapped. §8 extended with RT-B surface (`+178672`), RT-C surface (`+178676`), RT-B wrapper
>   (`+178684`), RT-C wrapper (`+178688`), toon-ramp LUT (`+178652`), backbuffer clear color
>   (`+178340`), bit depth (`+177868`), and downsample divisors (`+178752`/`+178756`). §13
>   items 1–3 tightened; item 7 added (config-singleton key open question).
> - **deep-3d-cartography static pass (2026-06-29):** §12 tightened — `GTransform` internal
>   matrix convention confirmed row-major with composition `localMatrix × parentModelView`
>   (local-first, row-vector pre-multiplication); this closes the `GTransform`-internal portion
>   of the matrix-order item. §13 item 3 updated accordingly: device-side `SetTransform` upload
>   order and world up-axis remain the only open `[debugger-confirm]` in that item.
> - **scene-graph-traversal consolidation (2026-06-29):** §2.2 step 3 updated — deferred
>   `scene_graph.md §3` reference replaced with explicit citations to `occlusion_culling.md §3
>   Regime 1` (sphere-vs-plane algorithm, confirmed) and `scene_graph_nodes.md §5` (DFS
>   pre-order child-list traversal, confirmed); `occlusion_culling.md` added to cross-links.
> - **render-state / fog / transform-upload corrections (2026-06-30):** §3 step 3 gains the
>   in-world fog recipe (linear vertex **range** fog: FOGENABLE 1, FOGVERTEXMODE 3, RANGEFOGENABLE 1,
>   start/end from clamped view distance, colour from a 48-entry time-of-day table). §12 gains the
>   **global default render-state block** note (global default cull = `D3DCULL_NONE` via engine
>   ordinal 2; ordinal→device map 0→CW, 1→CCW, 2→NONE; ~17 defaults) and the **transform upload
>   states** (projection → `SetTransform` state 3, view → state 2; world view = inverse-orthonormal,
>   no LookAt on world path). §13 item 3 narrowed to the world up-axis direction only.
> - **cross-links:**
>   `Docs/RE/specs/rendering.md` (per-pass render-state matrix, glow chain, cel-shader
>   gating, FVF strides, UI blend modes);
>   `Docs/RE/structs/scene_graph_nodes.md` (GNode / GGroup / GScene / GView / GCamera / GFrustum
>   class hierarchy and vtables);
>   `Docs/RE/specs/occlusion_culling.md` (definitive no-occlusion verdict; sphere-vs-plane
>   frustum algorithm, scalar x87, plane-index ordering, and three draw-set pruning regimes);
>   `Docs/RE/specs/game_loop.md` (four-phase loop, FPS cap, scene-state machine);
>   `Docs/RE/formats/shaders.md` (cel VS/PS, composite shader, glow shader set).

---

## Status

| Item | Confidence |
|---|---|
| Frame orchestration — multi-view circular list, driver layout | CONFIRMED |
| Per-view two-phase processing (update then draw) | CONFIRMED |
| Camera RTTI cast, FOV/aspect read, view-matrix build | CONFIRMED (matches `rendering.md` §2.2) |
| Frustum cull entry wiring (detail scale, node mask, BeginView) | CONFIRMED |
| Frame-setup pass internals (eye cache, terrain, fog, effects, shadow) | CONFIRMED |
| Direct-path draw order — exact callback slot sequence | CONFIRMED |
| Offscreen-path draw order — different from direct path | CONFIRMED |
| Bloom source scope — RT-A/RT-B/RT-C identities and routing pinned | CONFIRMED (static); optional visual surface cross-check `[debugger-confirm]` |
| Render-pass installer — pass slot assignments | CONFIRMED |
| Subsystem-wiring render-globals block — cached singleton list | CONFIRMED |
| GView render-view object layout (L1 offsets) | CONFIRMED |
| Global scene/post object key members | CONFIRMED |
| Opaque texture-stage block (MODULATE2X, aniso/aniso/linear) | CONFIRMED |
| Cel VS registers c4..c10 (including BT.601 luma weights) | CONFIRMED |
| IDirect3DDevice9 vtable offset key (expanded) | CONFIRMED (call-shape verified) |

---

## 1. Frame Orchestration — The Multi-View Circular Driver Loop

**Confidence: CONFIRMED.**

The per-frame device step (`Engine_DeviceStepAndPresent`) operates on a **driver object** and
walks a **circular linked list of view-nodes** — one full update+draw cycle per view, then a
single Present at the end. This is the concrete mechanism underlying the device-step
abstraction described in `rendering.md` §2.0; it corrects and sharpens "walk the scene list"
into an explicit circular-list contract with the driver and node layouts below.

### 1.1 Driver Object Layout

| Offset (bytes) | Field |
|---|---|
| `+4` | Renderer / device wrapper (holds the cached `IDirect3DDevice9`) |
| `+20` | Head of the circular linked list of view-nodes (`list_head` / `_Myhead` — the sentinel node pointer) |
| `+24` | View count (`view_count` / `_Mysize`): non-zero → full list walk + Present; zero → Clear + EndScene only. This is the embedded MSVC `std::list` element count, not a boolean flag; the gating behaviour is unchanged. |
| `+44` | Last Present / device-lost result code (drives the recovery branch) |

> **No vtable.** The driver object is a plain struct — `+0` is reserved (zero-initialised;
> no vtable write by the constructor). Polymorphism in this subsystem resides in the GView
> render-view descriptor, not in the driver. For the complete 14-field, 56-byte layout see
> `Docs/RE/structs/render_driver.md`.

### 1.2 View-Node Layout

| Offset (bytes) | Field |
|---|---|
| `+0` | Next pointer (cycles back to the head from the last node) |
| `+8` | View object pointer (the render-view described in §7) |

### 1.3 Per-Frame Loop

**Active branch** (driver `+24` non-zero):

0. **Device-recovery short-circuit**: if the stored result code at driver `+44` is non-zero
   (a failed `Present` from the previous frame), skip steps 1–2 entirely and call
   `Device_TestCooperativeLevelAndRecover` directly. The frame draw is suppressed until the
   device recovers. If the device remains lost, sleep 1000 ms and retry on the next iteration.
1. Walk the circular list from the head. For each view-node, call
   `Renderer_SetupCameraAndFrustum` (the UPDATE phase, §2) then `Renderer_DrawScene_Fork`
   (the DRAW phase, §4/§5) on the view object at node `+8`.
2. After all views are processed, call `Renderer_Present` — **one Present for the entire
   frame**, regardless of how many views are in the list.
3. On Present failure: store the non-zero result into driver `+44`; call
   `Device_TestCooperativeLevelAndRecover`. If the device remains lost, sleep 1000 ms and
   retry on the next iteration. *(See `rendering.md` §2.0.2 for the full device-lost
   lifecycle.)*

**Inactive branch** (driver `+24` zero): call `GDevice_ClearBackbufferDefault` and EndScene
only — no list walk, no Present.

The engine supports **multiple views per frame** (for example, a main world view alongside a
character-preview inset). How many view-nodes are in the list at runtime in-world versus at
character-select is a `[debugger-confirm]` item — see §13.

---

## 2. Per-View Processing — Two Phases

**Confidence: CONFIRMED.**

For each view-node, `Engine_DeviceStepAndPresent` calls two routines in sequence:

- **Phase A — UPDATE** (`Renderer_SetupCameraAndFrustum`): camera setup → frustum build →
  cull → frame-setup pass. All rendering preparation completes here before any draw call is
  issued.
- **Phase B — DRAW** (`Renderer_DrawScene_Fork`): reads the offscreen-RT enable flag and
  branches to the direct (§4) or offscreen (§5) draw path.

### 2.1 Phase A — Update: Camera, Frustum, Cull, Frame-Setup

`Renderer_SetupCameraAndFrustum` runs on the render-view object (layout in §7). In order:

1. **Guard**: proceed only if the scene root at view `+36` and the camera
   holder at view `+40` are both non-null.
2. Increment the frame counter at view `+272`.
3. Call `Diamond_GView_BeginRenderFrame` on the view object (`Diamond::GView` render-view
   descriptor itself; guarded by scene root at view `+36` being non-null).
4. **Camera RTTI cast**: read the GCamera from `*(*(view+40)+80)`; dynamic-cast to
   `GPerspectiveCamera`. On success, read the field-of-view from camera offset `+168` and
   the aspect ratio from camera offset `+172`. On cast failure, both default to `π/4`
   (approximately 0.7854 radians). *(Matches `rendering.md` §2.2.)*
5. Call `Diamond_GNode_ComputeWorldTransform` on the camera holder into view `+44`, then call
   `Matrix4_InvertOrthonormal` — this builds the **view matrix as the orthonormal inverse of
   the camera world transform**.
6. Cache the camera eye position and rotation into the render-globals block (§8). The eye
   position is also re-cached by the frame-setup pass (§3).
7. Call `Diamond_GFrustum_ctor_copy` to copy the camera's embedded `GFrustum` (at camera
   `+0x2C`, matching `scene_graph_nodes.md`'s GCamera layout) into a stack-local frustum.
8. **Cull** (§2.2): call `GCull_CullScene` with the GView sub-object, the selected cull-set
   (chosen by the selector byte at view `+108`), the FOV/aspect, viewport dimensions from
   view `+28` and `+32`, the stack frustum, the view matrix at view `+44`, and cull context
   parameters from view `+120`, `+140`, `+144`.
9. **Frame-setup pass** (§3): read the function pointer at view `+180`; if non-null, call it
   with the context argument at view `+184`. This fires **in the UPDATE phase** before any
   draw.
10. Accumulate cull time into view `+256` (double-precision); rebuild the frustum polytope for
    the next frame.

### 2.2 Frustum Cull and Visible-Set Build

`GCull_CullScene` produces the visible draw-item list inside the chosen cull-set:

1. `GView_ComputeDetailScale`: derive a per-frame LOD/detail scale from FOV, aspect ratio,
   and viewport dimensions. This value feeds terrain-cell and mesh LOD selection for the
   frame.
2. `GCull_BeginView`: seed the cull traverser with the frustum planes, view matrix, viewport
   dimensions, and the detail scale.
3. Dispatch the recursive cull traverse through the scene-root's vtable cull slot with an
   initial node visibility mask of `(1 << planeCount) - 1`, where `planeCount` is read from
   the stack frustum at offset `+4` (typically 6 planes → mask `0x3F`). The sphere-vs-plane
   algorithm (`dist = (N·C) + d`; `dist < −R` for any plane → subtree culled; `dist ≥ −R`
   for all planes → visible), scalar x87 implementation (no SIMD), plane-index ordering
   (near=index 1 / far=index 3 by strong static inference), and negative-radius always-visible
   sentinel are documented in `Docs/RE/specs/occlusion_culling.md` §3 Regime 1
   (static-confirmed). Child lists are iterated depth-first pre-order, index 0 to count−1
   across all GVector child lists (`Docs/RE/structs/scene_graph_nodes.md` §5, confirmed).
   This section records only the entry wiring (mask seed, frustum source, cull-set dispatch)
   surrounding that algorithm.
4. Emitted visible draw-items accumulate inside the chosen cull-set object. The cull-set's
   vtable slot `+4` is the "draw the emitted visible items" call invoked during the draw
   phase.

**Cull-set selection**: view `+108` is a selector byte. Zero → use the primary cull-set at
view `+112`. Non-zero → use the secondary cull-set at view `+116`. The secondary set also
carries per-frame cumulative statistics at `cull-set+736`.

### 2.3 Phase B — Draw Fork

`Renderer_DrawScene_Fork` reads the **offscreen-RT enable flag** from the global scene/post
object, accessed via the back-reference at view `+288`, at scene-object offset `+178748`. If
the flag is zero, the direct path runs (§4); if non-zero, the offscreen path runs (§5).

**Decision logic (statically resolved):** the flag defaults to `0` (direct path) at
construction. It is set to `2` (offscreen path) only inside `Renderer_CreateD3DDevice`, iff
**both** conditions hold: (a) `Renderer_InitCelGlowShaders` returns success — which requires
the cel + glow shaders and all three render targets to be created successfully, making this an
implicit GPU/driver capability gate — and (b) the config-singleton field at offset `+0x30`
equals `1` (the display/post-process enable toggle; see §13 item 7 for the open config-key
question). The flag is cleared on device teardown by `Renderer_ReleaseDeviceResources`. It is
also read by `RenderPass_OpaqueWorld` and `Actor_DrawSkinnedCelWithTint` to gate their
respective opaque-pass and cel-actor behaviours. Only the runtime landed value on a given
machine and configuration remains `[debugger-confirm]` — see §13.

---

## 3. Frame-Setup Pass — Runs in the UPDATE Phase

**Confidence: CONFIRMED.**

The frame-setup pass function is installed at view `+180` by the render-pass installer (§6).
It fires during Phase A, before any draw call is issued. It reads all inputs from the
render-globals block (§8) and takes no significant arguments of its own. Operations in order:

1. **Eye cache**: re-extract the camera eye position from the inverted view transform; write
   the three eye-coordinate floats (X, Y, Z) into dedicated fields in the render-globals
   block. This ensures sub-system callbacks reading from that block see the current eye
   position.
2. **Terrain cell visibility**: call `Terrain_RegionForEachLoadedCell` on the TerrainManager
   with the current view matrix — iterates all loaded terrain-region cells to prepare
   per-frame streaming and visibility state.
3. **Fog and distance setup**: call the fog/distance setup routine with a fog parameter, the
   camera eye position, and a player anchor position. The anchor is the local player's world
   position (read from the local player object) when a local player is present; a static
   default position is used otherwise.

   **Fog recipe (linear vertex range fog).** When fog is enabled the controller sets
   `D3DRS_FOGENABLE` (state 28) = 1, `D3DRS_FOGVERTEXMODE` (state 140) = 3 (`D3DFOG_LINEAR`
   vertex fog), and `D3DRS_RANGEFOGENABLE` (state 48) = 1 (radial **range** fog — fog factor
   keyed on true eye-to-vertex distance, not view-space Z). `FogStart`/`FogEnd` are derived from
   a clamped per-frame view distance (interpolated for view distances below 1000 world units),
   and the fog colour is read from a 48-entry time-of-day colour table. The disable path sets
   `D3DRS_FOGENABLE` (state 28) = 0. A Godot port reproduces this as linear distance fog with the
   fog factor computed from radial eye distance, the start/end driven by the same clamped view
   distance, and the colour sampled from the time-of-day table.
4. **Effects tick and schedule drain**: call `Effects_TickAllManagers` on the EffectManager
   to advance all effect sub-managers for the frame; then call `EffectSchedule_DrainDueEvents`
   to fire any due scheduled effects.
5. **Shadow look-at**: lazily create the shadow scene singleton on the first call; then call
   `Renderer_BuildShadowLookAtMatrix` on it to build the per-frame shadow look-at matrix.

After the frame-setup pass completes all work — eye cache, terrain visibility, fog/distance,
effects tick and drain, shadow matrix — is ready before the first draw call of Phase B.

---

## 4. Direct-Path Draw Order (`Renderer_DrawScene_Direct`)

**Confidence: CONFIRMED.**

The direct path draws straight to the back buffer with no post-process. In order:

1. Save the current viewport (device vtable `+192` GetViewport).
2. Call `GDevice_SetViewport` with the viewport rect from view `+20` (X), `+24` (Y),
   `+28` (width), `+32` (height).
3. Call `GDevice_ClearTargetAndZ` with the clear color from view `+8` and the clear-Z float
   from view `+12` (default 1.0). Clear flags are TARGET | ZBUFFER. *(See `rendering.md`
   §2.0.1.)*
4. Call `GDevice_BeginScene`.
5. Proceed only if the scene root (view `+36`) and camera holder (view `+40`) are both
   set:

   a. Record draw-begin timestamp into view `+236`.

   b. **Camera per-frame apply**: call the camera object's vtable slot `+8` (per-frame
      update / bind).

   c. **Sky/background pass**: call `Renderer_SetViewTransform` with the view matrix at
      view `+44`, then call the function pointer at view `+188` with its context argument
      at view `+192` — this is `RenderPass_SkyAndBackground`.

   d. **Terrain/buildings pass**: call `Renderer_SetViewTransform`, then call the function
      at view `+196` with its context argument at view `+200` — this is
      `RenderPass_WorldTerrainAndBuildings`.

   e. **Opaque texture-stage and sampler block**: configure Direct3D texture stages and
      sampler as specified in §9. This state is set once for the culled scene-graph opaque
      draw that follows.

   f. **Culled scene-graph opaque draw**: select the cull-set via view `+108`; call the
      cull-set's vtable slot `+4`. This draws all visible static world meshes and skinned
      actors (characters and mobs) emitted by the cull in Phase A. *(Cel material is bound
      per-object inside the skinned-actor draw routine only when post-process is enabled —
      see `rendering.md` §5.1a.)*

   g. **Opaque-world extras pass**: call `Renderer_SetViewTransform`, then call the function
      at view `+204` with its context argument at view `+208` — this is
      `RenderPass_OpaqueWorld` (additional opaque world FX / extras).

   h. **Transparent/particles pass**: call `Renderer_SetViewTransform`, then call the
      function at view `+172` with its context argument at view `+176` — this is
      `RenderPass_TransparentAndParticles`.

6. **Overlay/UI callback**: if the function pointer at view `+212` is set, call it with the
   context argument at view `+216`.
7. **FPS counter**: if the byte flag at view `+292` is set, call `HUD_DrawFPSCounter`.
8. Call EndScene (device vtable `+168`).
9. Accumulate draw time into view `+264`. At the stats reporting interval (view `+280`),
   compute the FPS average into view `+296` and reset the cull-set statistics.

**Direct-path draw order (one back-buffer pass):**

```
sky/background
→ terrain/buildings
→ [texstage/sampler block]
→ culled scene-graph opaque (statics + actors)
→ opaque-world extras
→ transparent/particles
→ overlay/UI
→ FPS counter
→ EndScene
```

> **Refinement of `rendering.md` §2.1 and §3**: the culled scene-graph world (statics and
> actors) draws **between terrain/buildings and the opaque-extras callback**, not after it. The
> four-callback abstraction in `rendering.md` §2 is a correct behavioural grouping; this
> section pins the exact callback-slot sequence within it.

---

## 5. Offscreen-Path Draw Order (`Renderer_DrawScene_OffscreenRT_0`)

**Confidence: CONFIRMED.**

The offscreen path uses three render targets:
- **RT-A** — the primary scene-capture RT and the final composited-scene store: texture at
  scene `+178656`, surface at `+178668`, render-to-surface wrapper at `+178680`.
- **RT-B** — the full-size plain copy of RT-A (composite stage 0; the untouched base image):
  texture at scene `+178660`, surface at `+178672`, wrapper at `+178684`.
- **RT-C** — the downsampled blurred glow RT (composite stage 1): texture at scene `+178664`,
  surface at `+178676`, wrapper at `+178688`.

Each render-to-surface wrapper's vtable exposes `BeginScene` at slot `+20` and `EndScene` at
slot `+24`. See §8 for the full member table.

The most important difference from the direct path is **where the culled scene-graph world
(statics and actors) draws relative to the glow composite**: in the direct path it draws
before the extras pass straight to the back buffer; in the offscreen path it draws inside a
second RT-A render pass, **after** the glow composite, on top of the already-bloomed
background.

**Offscreen sequence:**

1. **RT-A pass 1 — bloom-source capture**: `RT-A.BeginScene(surface +178668)`.
2. Clear RT-A: flags `D3DCLEAR_TARGET | D3DCLEAR_ZBUFFER` (3), color from view `+8`,
   Z from view `+12`.
3. If the scene root (view `+36`) and camera holder are both set: camera per-frame apply; run the
   **sky/background pass** (view `+188` / `+192`); run the **terrain/buildings pass**
   (view `+196` / `+200`).
4. Call `Diamond_Renderer_DrawOverlayPass`: sets world to identity, calls
   `Renderer_SetViewTransform`, then calls the function at view `+204` only — the
   **opaque-world extras pass** (`RenderPass_OpaqueWorld`).
5. `RT-A.EndScene(0)`.

   > **RT-A content at this point: sky/background + terrain/buildings + opaque-world extras
   > only. This is the bloom source.** The culled scene-graph world (statics, cel actors),
   > transparent/particle FX, and UI are added to RT-A in pass 2 and the present pass,
   > **after** glow extraction, and are **not in the bloom source**.

6. **Glow extract** — `Renderer_GlowExtractDownsample`: `RT-B.BeginScene(surface +178672)`;
   clear RT-B (`D3DCLEAR_TARGET` only, flags 1, color `0xFF000000`); bind RT-A texture
   (`+178656`) as stage 0; **no pixel shader** (plain fixed-function texture copy); SetFVF
   `0x102` (XYZ|TEX1, stride 20); `DrawPrimitiveUP(TRIANGLEFAN, 2, stride 20)`;
   `RT-B.EndScene`. RT-B now holds an exact full-size copy of RT-A.

7. **Glow blur** — `Renderer_GlowBlurDownsampled`: `RT-C.BeginScene(surface +178676)`; clear
   RT-C (`D3DCLEAR_TARGET` only, flags 1, color `0xFF000000`); bind RT-A texture (`+178656`)
   as stage 0; bind glow-blur PS (scene `+177876`, `power1dx8.psh`) via device vtable `+428`;
   SetFVF `0x102` (stride 20); `DrawPrimitiveUP(TRIANGLEFAN, 2, stride 20)` with vertex
   positions scaled by the downsample divisors (scene `+178752` / `+178756`, default 2/2);
   unbind PS; `RT-C.EndScene`. RT-C now holds the downsampled blurred glow.

8. **RT-A pass 2 — composite and foreground**: `RT-A.BeginScene(surface +178668)` again.
   Orthographic projection over back-buffer dimensions (scene `+177860` × `+177864`) via
   `D3DXMatrixOrthoLH`; identity world and view; Z-test on, Z-write off, lighting off,
   alpha-blend **disabled**.

   a. **Composite**: bind composite PS (scene `+177880`, `finaldx8.psh`) via device vtable
      `+428`; upload PS constants register 0 from scene `+179016`, register 1 from scene
      `+179020` (device vtable `+436`); bind RT-B texture (`+178660`) as stage 0, RT-C
      texture (`+178664`) as stage 1; SetFVF `0x202` (XYZ|TEX2, stride 28) via device
      vtable `+356`; `DrawPrimitiveUP(TRIANGLEFAN, 2, stride 28)` via device vtable `+332`.
      Unbind PS; clear stage textures.

   b. **Cel actors** — call `Renderer_UploadToonShaderConstants`: uploads cel VS constants
      c4..c10 (see §10) via device vtable `+376`; then immediately draws the culled
      scene-graph set by calling the selected cull-set's vtable slot `+4`. Static world
      meshes and cel-shaded actors draw here, on top of the already-composited (bloomed)
      background. Blend: source ONE / destination ZERO; alpha-test **enabled**.

   c. **Transparent/particles** — call `Renderer_DrawScene_PostScene`: runs the transparent/
      particles pass (view `+172` / `+176`) with world set to identity.

   `RT-A.EndScene(0)`. RT-A now holds the fully composited scene: base image + glow +
   cel actors + transparent FX.

9. **Present pass — blit RT-A to the back buffer**: call `GDevice_ClearBackbufferDefault`
   (flags `D3DCLEAR_TARGET | D3DCLEAR_ZBUFFER` (3), color `0xFF000000` from scene `+178340`,
   Z 1.0); `BeginScene`; orthographic LH, identity world and view; Z-test on, Z-write off,
   lighting off; alpha-blend **ON**, SrcBlend `ONE` (2), DestBlend `ZERO` (1) — opaque copy;
   bind RT-A texture (`+178656`) as stage 0; SetFVF `0x102` (stride 20); device vtable `+356`;
   `DrawPrimitiveUP(TRIANGLEFAN, 2, stride 20)` via device vtable `+332`; then run the
   **overlay/UI callback** (view `+212` / `+216`); if view `+292` flag set call
   `HUD_DrawFPSCounter`; EndScene (device vtable `+168`). `Present` itself is issued once
   by `Renderer_Present` in the driver loop after the full view walk (§1.3).

**Offscreen-path draw order:**

```
[RT-A pass 1: sky/background → terrain/buildings → opaque-world extras]
→ glow extract: RT-A → RT-B  (no PS, fixed-function copy, FVF 0x102 stride 20)
→ glow blur:    RT-A → RT-C  (power1dx8 PS, FVF 0x102 stride 20, downsample divisors)
→ [RT-A pass 2:
    composite: RT-B (stage 0) + RT-C (stage 1) → RT-A  [finaldx8 PS, FVF 0x202 stride 28]
    → cel VS c4..c10 uploaded → cull-set draw  [ONE/ZERO blend, alpha-test ON]
    → transparent/particles]
→ present-blit: RT-A → back buffer  (alpha-blend ONE/ZERO, FVF 0x102 stride 20)
→ overlay/UI → FPS counter → EndScene
```

### 5.1 Pipeline-Level Consequences

**Bloom source scope** (RT identities and routing statically confirmed; optional visual
surface cross-check remains `[debugger-confirm]`):
Only sky + terrain/buildings + opaque-world extras feed the glow extract via RT-A pass 1.
The scene-graph statics, cel actors, transparent/particle FX, and UI are added to RT-A in
pass 2 and the present pass respectively, and are therefore **excluded from the bloom
source**. This provides the concrete pipeline site for `rendering.md` §5.1a's "cel constant
upload + lit-world draw, offscreen only" — the cel actors composite over a bloomed
background, not into it. Critically, the glow extract step (RT-A→RT-B) is a **plain
fixed-function texture copy with no pixel shader**; only the blur step (RT-A→RT-C) applies
the `power1dx8` shader. RT-B is therefore the untouched full-size copy used as the composite
base; RT-C is the sole blurred glow source.

**UI is never bloomed** in either path. The overlay/UI callback runs in the present pass,
directly into the back buffer, after `RT-A.EndScene` on pass 2.

**Cull-set drawn exactly once per frame** in both paths: in the direct path between
terrain/buildings and opaque-world extras (§4, step 5f); in the offscreen path within RT-A
pass 2 (§5, step 8b) via `Renderer_UploadToonShaderConstants`.

> Cross-link: `rendering.md` §5.1a (cel gating on post-process flag), §6 (glow chain RT
> layout, shader selection, composite weight details). This section provides the pipeline-
> level frame; `rendering.md` §6 owns the RT setup and glow chain detail.

---

## 6. Render-Pass Installer and Subsystem-Wiring Globals

**Confidence: CONFIRMED.**

The render-pass installer runs during scene-graph construction. It writes all callback
function pointers into the render-view object and populates a fixed render-globals block with
every manager singleton the pass callbacks need to read.

### 6.1 Pass-Slot Assignments

The installer writes the following callbacks into the render-view object (layout in §7).
Each entry is a function pointer (fn at the listed offset) and its context argument (ctx at
fn offset + 4):

| View offset (fn) | View offset (ctx) | Callback | Phase / draw step |
|---|---|---|---|
| `+180` | `+184` | Frame-setup pass function (§3) | UPDATE phase (Phase A) |
| `+188` | `+192` | `RenderPass_SkyAndBackground` | DRAW — sky step |
| `+196` | `+200` | `RenderPass_WorldTerrainAndBuildings` | DRAW — terrain/buildings step |
| `+204` | `+208` | `RenderPass_OpaqueWorld` (opaque-world extras) | DRAW — extras step |
| `+172` | `+176` | `RenderPass_TransparentAndParticles` | DRAW — transparent step |

The overlay/UI callback (view `+212` / `+216`) is wired separately and is not set by this
installer.

### 6.2 Subsystem-Wiring Render-Globals Block

The installer caches every manager singleton into a fixed render-globals block. Pass callbacks
read from this block each frame. The cached singletons, in installation order:

| Slot | Cached singleton / role |
|---|---|
| 1 | Render-globals base — the global scene/post object (provides the camera eye position cache and per-frame render data) |
| 2 | Sky/terrain scene helper |
| 3 | TerrainManager |
| 4 | BattleController |
| 5 | EffectManager |
| 6 | BattleController (variant instance) |
| 7 | Scene helper A (specific role pending — escalated to effects/sky analyst) |
| 8 | MapXEffectManager |
| 9 | JointEffectManager (instance A) |
| 10 | JointEffectManager (instance B) |
| 11 | SwordLightManager |
| 12 | EnvironmentLightScene |
| 13 | Scene helper B (specific role pending — escalated to effects/sky analyst) |
| 14 | AppService |
| 15 | SoundManager |
| 16 | AmbientSoundManager |

Additional fields in the render-globals block written by the frame-setup pass and its callers:
- Camera eye position cache (three float fields: X, Y, Z)
- Shadow scene singleton pointer (lazily populated on the first frame)
- Shadow scene creation latch (prevents re-creation once set)

---

## 7. GView Render-View Object Layout

**Confidence: CONFIRMED** from the four spine routines and the render-pass installer.

> **Reconciliation note.** This is the render-view object that `Renderer_SetupCameraAndFrustum`,
> `Renderer_DrawScene_Direct`, `Renderer_DrawScene_OffscreenRT_0`, and the render-pass
> installer operate on. It may be the same class as — or a sibling of — the `GView` described
> in `scene_graph_nodes.md` (`+0x24` camera, `+0x70` platform, `+0x74` pipeline, `+0x130` device).
> Reconciliation of the two offset tables is flagged for `re-struct-analyst` (§13, item 5).

| Offset (bytes) | Field |
|---|---|
| `+0` | Vtable pointer — class `Diamond::GView`, confirmed via RTTI. One virtual slot: deleting destructor (unrefs scene root, deletes cull pipelines, COM-releases FPS texture, destroys cull deque). |
| `+8` | Clear color (passed by pointer to `GDevice_ClearTargetAndZ`) |
| `+12` | Clear-Z float (default 1.0) |
| `+20` | Viewport X |
| `+24` | Viewport Y |
| `+28` | Viewport width (also cull detail-scale input) |
| `+32` | Viewport height (also cull detail-scale input) |
| `+36` | Scene root (`GGroup*`/`GScene*`, refcounted): guard for `Diamond_GView_BeginRenderFrame` and the cull call; non-null check gates Phase A and the draw steps in §4/§5. |
| `+40` | Camera holder/platform: `*(+40)` = GNode for world transform; `*(+40)+80` = GCamera |
| `+44` | 16-float camera world / view transform (`ComputeWorldTransform` dest; `SetViewTransform` src) |
| `+108` | Cull-set selector byte: 0 → use primary cull-set at `+112`; non-zero → use secondary at `+116` |
| `+112` | `Diamond::GCull*` — primary cull pipeline (owned; 736 bytes, heap-allocated). Vtable slot `+4` is the "draw visible items" call invoked during the draw phase. |
| `+116` | `GCullPipeline*` — secondary cull pipeline (owned; default null; built on demand). Carries cull/draw statistics at its own internal offset `+736`. |
| `+120` | Cull context (passed to `GCull_BeginView`) |
| `+140` | Cull parameter |
| `+144` | Cull parameter |
| `+172` | Pass fn: transparent/particles |
| `+176` | Context for `+172` |
| `+180` | Pass fn: frame-setup (UPDATE phase) |
| `+184` | Context for `+180` |
| `+188` | Pass fn: sky/background |
| `+192` | Context for `+188` |
| `+196` | Pass fn: terrain/buildings |
| `+200` | Context for `+196` |
| `+204` | Pass fn: opaque-world extras |
| `+208` | Context for `+204` |
| `+212` | Callback fn: overlay / UI / HUD |
| `+216` | Context for `+212` |
| `+220` | Stats interval start timestamp |
| `+228` | Wall-clock timestamp: cull begin |
| `+236` | Wall-clock timestamp: draw begin |
| `+244` | Wall-clock timestamp: draw end |
| `+256` | Accumulated cull time (double-precision) |
| `+264` | Accumulated draw time (double-precision) |
| `+272` | Frame counter (unsigned int) |
| `+280` | Stats reporting interval threshold (double-precision) |
| `+288` | Back-reference to the global scene/post object |
| `+292` | FPS-counter enable byte |
| `+296` | Computed FPS average (double-precision) |

> **Full field table:** `Docs/RE/structs/gview.md` is the authoritative per-view struct spec
> (complete 308-byte layout, vtable note, lifecycle, and reconciliation with `scene_graph_nodes.md`).
> The table above covers the offsets exercised by the four spine routines and the installer;
> `structs/gview.md` adds +4 (reserved), +16 (clearEnable byte), +120 (embedded GBlockDeque
> cull-scratch), +164/+168 (extra callback pair), and +304 (FPS-counter digit texture).

---

## 8. Global Scene/Post Object — Key Pipeline Members

**Confidence: CONFIRMED.**

The global scene/post object is the large (~179 KB) singleton described in `rendering.md` §1.
Its key members used by the render pipeline are:

| Offset (bytes) | Member / role |
|---|---|
| `+177860` | Back-buffer width (used for post-process quad ortho sizing) |
| `+177864` | Back-buffer height |
| `+177868` | Back-buffer bit depth (default 32) |
| `+177876` | Glow-blur pixel shader handle (`power1dx8.psh` by default; overridable via `DISPLAY_POWERSHADER`; filename buffer at `+179028`) — see `Docs/RE/specs/post_processing.md §3`, `Docs/RE/specs/character_rendering.md §9` |
| `+177880` | Composite pixel shader handle (`finaldx8.psh`) — see `Docs/RE/specs/post_processing.md §3`, `Docs/RE/specs/character_rendering.md §9` |
| `+177976` | Cached `IDirect3DDevice9` pointer |
| `+178320` | Cel vertex shader handle (`dotoonshading.vsh`) — see `Docs/RE/specs/post_processing.md §3`, `Docs/RE/specs/character_rendering.md §9` |
| `+178324` | Cel pixel shader handle — normal mode (`dotoonshading.psh`) — see `Docs/RE/specs/post_processing.md §3`, `Docs/RE/specs/character_rendering.md §9` |
| `+178328` | Cel pixel shader handle — stealth/concealment mode (`dotoonshading2.psh`) — see `Docs/RE/specs/post_processing.md §3`, `Docs/RE/specs/character_rendering.md §9` |
| `+178340` | Default backbuffer clear color (stored `D3DCOLOR`; initialized to `0xFF000000` opaque black; used by `GDevice_ClearBackbufferDefault` in the inactive branch and the present pass) |
| `+178652` | Toon-ramp LUT texture (`data/shader/toonramp.bmp`; loaded during `Renderer_InitCelGlowShaders`) |
| `+178656` | **RT-A texture** — primary scene-capture color render target (source in the present-blit pass; used in both RT-A pass 1 and pass 2) |
| `+178660` | **RT-B texture** — full-size plain copy of RT-A (composite stage 0; the untouched base image) |
| `+178664` | **RT-C texture** — downsampled blurred glow render target (composite stage 1; the sole blurred glow source) |
| `+178668` | RT-A surface (`GetSurfaceLevel(0)` of RT-A texture; used in RT-A pass 1 and pass 2) |
| `+178672` | RT-B surface (`GetSurfaceLevel(0)` of RT-B texture) |
| `+178676` | RT-C surface (`GetSurfaceLevel(0)` of RT-C texture) |
| `+178680` | RT-A render-to-surface wrapper (`D3DXCreateRenderToSurface`; vtable: `+20` = BeginScene, `+24` = EndScene) |
| `+178684` | RT-B render-to-surface wrapper (`D3DXCreateRenderToSurface`) |
| `+178688` | RT-C render-to-surface wrapper (`D3DXCreateRenderToSurface`; no depth buffer) |
| `+178692` | Cel VS register c4 — light/sky direction X |
| `+178696` | Cel VS register c4 — light/sky direction Y |
| `+178700` | Cel VS register c4 — light/sky direction Z |
| `+178748` | **Offscreen-RT / post-process enable flag** (0 = direct path; `2` = offscreen path — see §2.3 for full decision logic) |
| `+178752` | Glow downsample divisor X (default `2`; scales the RT-C viewport width) |
| `+178756` | Glow downsample divisor Y (default `2`; scales the RT-C viewport height) |
| `+179016` | Glow composite PS constant source for register 0 (base-brightness scalar c0) |
| `+179020` | Glow composite PS constant source for register 1 (glow-brightness scalar c1) |
| `+179028` | Glow-shader filename buffer (pre-filled with `data/shader/power1dx8.psh`; written by the `display.lua` loader when `DISPLAY_POWERSHADER` is set) — see `Docs/RE/specs/post_processing.md §3`, `Docs/RE/specs/character_rendering.md §9` |

*Cross-link: `rendering.md` §1 (scene/post object description), §6.1 (RT0/TEX0 setup),
§6.3 (composite weights c0/c1); `Docs/RE/specs/post_processing.md §3` (authoritative for the
five cel/glow shader-handle slot identities); `Docs/RE/specs/character_rendering.md §9`
(renderer field map including all shader handles and RT references).*

---

## 9. Opaque Texture-Stage and Sampler Configuration

**Confidence: CONFIRMED.**

Issued before the culled scene-graph opaque draw in the direct path (§4, step 5e), via device
vtable `+268` (SetTextureStageState) and `+276` (SetSamplerState). Values are the standard
Direct3D 9 enum integers:

| Stage / sampler | Parameter | Value | D3D9 enum |
|---|---|---|---|
| Stage 0 | COLORARG1 | 2 | TEXTURE |
| Stage 0 | COLORARG2 | 0 | DIFFUSE |
| Stage 0 | **COLOROP** | **5** | **MODULATE2X** |
| Stage 0 | ALPHAOP | 1 | DISABLE |
| Stage 1 | COLOROP | 1 | DISABLE |
| Stage 1 | ALPHAOP | 1 | DISABLE |
| Stage 2 | COLOROP | 1 | DISABLE |
| Stage 2 | ALPHAOP | 1 | DISABLE |
| Sampler 0 | **MINFILTER** | **3** | **ANISOTROPIC** |
| Sampler 0 | **MAGFILTER** | **3** | **ANISOTROPIC** |
| Sampler 0 | **MIPFILTER** | **2** | **LINEAR** |

> Refinement of `rendering.md` §2.1 step 3: the "default modulate plus sampler filters" is
> specifically **MODULATE2X** (not plain MODULATE, value 4) with **anisotropic min/mag and
> linear mip** filtering. The offscreen composite and cel passes use different stage and
> sampler configurations, owned by `rendering.md` §6 and `formats/shaders.md`.

---

## 10. Cel Vertex-Shader Constant Registers c4..c10

**Confidence: CONFIRMED.**

Uploaded by `Renderer_UploadToonShaderConstants` via device vtable `+376`
(SetVertexShaderConstantF), four floats per register, immediately before the culled scene-graph
draw in the offscreen path (§5, step 8). These registers confirm and extend `rendering.md`
§5.1a and `formats/shaders.md`.

| Register | float4 value | Meaning |
|---|---|---|
| c4 | `[lightDir.x, lightDir.y, lightDir.z, 0]` | Light/sky **direction** — dynamic per-frame; X/Y/Z sourced from scene `+178692..+178700`; W = 0 |
| c5 | `[0, 0, −1.0, 0]` | Fixed direction vector (negative Z) |
| c6 | `[1, 1, 1, 1]` | Unit / material constant |
| c7 | `[0, 0, 0, 0]` | Zero constant |
| c8 | `[0, 1, 1, 1]` | Material/ambient constant |
| c9 | `[0.299, 0.587, 0.114, 1.0]` | **BT.601 luminance weights** — the toon-ramp luma key; W = 1 |
| c10 | `[1, 1, 1, 1]` | Unit / material constant |

Notes:
- c4 is the per-frame dynamic light direction stored in the global scene/post object (§8,
  offsets `+178692..+178700`).
- These constants are **not** a world-view-projection matrix and **not** a bone palette. The
  world/view/projection transform is applied through the standard `SetTransform` path. *(This
  is the C3 correction in `rendering.md` §5.1a.)*
- The cull-set draw executes **immediately after** the register upload within the same
  `Renderer_UploadToonShaderConstants` call.

*Cross-link: `formats/shaders.md` (cel VS/PS and toon ramp) for the shader-side use of these
constants. `rendering.md` §5.1a for the cel gating on the post-process flag.*

---

## 11. IDirect3DDevice9 Vtable Offset Key (Expanded)

**Confidence: CONFIRMED** (call-shape verified this lane against the standard 32-bit
`IDirect3DDevice9` vtable by argument shape).

| Vtable offset (bytes) | Method | Evidence |
|---|---|---|
| `+64` | Reset | Device-lost recovery path |
| `+68` | Present | `Renderer_Present` |
| `+164` | BeginScene | `GDevice_BeginScene` |
| `+168` | EndScene | Final EndScene in both paths |
| `+172` | Clear | Clear cross-check |
| `+188` | SetViewport | `GDevice_SetViewport` |
| `+192` | GetViewport | Viewport save at frame start |
| `+228` | SetRenderState | Alpha-blend, fog, Z-write, and alpha-test configuration for cel and shadow passes — see `Docs/RE/specs/character_rendering.md §10` |
| `+260` | SetTexture | Texture binding for cel parts, toon-ramp LUT, and RT composite stages — see `Docs/RE/specs/character_rendering.md §10` |
| `+268` | SetTextureStageState | Stage/type/value calls (§9) |
| `+276` | SetSamplerState | Sampler calls (§9) |
| `+332` | DrawPrimitiveUP | Post quads (TRIANGLEFAN, 2 primitives; strides 28 and 20) |
| `+356` | SetFVF | Composite FVF and present FVF |
| `+364` | CreateVertexShader | Cel VS creation at renderer initialization — see `Docs/RE/specs/character_rendering.md §10` |
| `+376` | SetVertexShaderConstantF | Cel registers c4..c10 (§10) |
| `+424` | CreatePixelShader | Cel and glow PS creation at renderer initialization — see `Docs/RE/specs/character_rendering.md §10` |
| `+428` | SetPixelShader | Composite/glow shader bind and clear |
| `+436` | SetPixelShaderConstantF | Glow composite PS registers 0/1 |

---

## 12. Render-State and Math Notes

- **World-projection handedness**: the world camera path builds the view matrix as the
  orthonormal inverse of the camera world transform (`Matrix4_InvertOrthonormal`). World
  projection is right-handed (`D3DXMatrixPerspectiveOffCenterRH` — CONFIRMED in `rendering.md`
  §2.2). Post/composite/present quads use `D3DXMatrixOrthoLH` (left-handed) over back-buffer
  dimensions with identity world and view. **`GTransform` internal matrix convention — confirmed
  row-major, composition order `localMatrix × parentModelView` (local-first, row-vector
  pre-multiplication; the matrix multiply helper reads source rows and destination columns in
  standard row-major order) — is statically confirmed (deep-3d-cartography pass).**

- **Transform upload states (corrected 2026-06-30 — device-side upload order resolved
  statically).** The projection matrix is uploaded via `SetTransform` transform-state **3**
  (`D3DTS_PROJECTION`); the per-pass view matrix is uploaded via transform-state **2**
  (`D3DTS_VIEW`). On the world path the view matrix is the **inverse-orthonormal of the camera
  holder world matrix** — there is **no `LookAt` on the world path** (the `D3DXMatrixLookAtLH`
  call is **shadow-projector-only**). This closes the device-side projection/view upload-order
  portion of the `[debugger-confirm]` note formerly in §13 item 3; only the exact world up-axis
  direction remains for a live confirmation.

- **Global default scene render-state block (recorded 2026-06-30).** The scene's global default
  render-state builder establishes the baseline device state for the frame. **The global default
  cull-mode is `D3DCULL_NONE` (two-sided / no backface culling)**: the builder writes engine
  cull ordinal **2**, and the engine-ordinal → device mapping is **0 → `D3DCULL_CW` (2),
  1 → `D3DCULL_CCW` (3), 2 → `D3DCULL_NONE` (1)** — so ordinal 2 maps to device `D3DCULL_NONE`.
  Per-material render-state blocks override this default (for example, building/mass-object meshes
  set `D3DCULL_CW`, and the terrain ground draw sets `D3DCULL_CW`). The block sets roughly **17
  global default render states**, including: alpha-test compare-function ordinal **5** with
  reference value **150**; blend factors `SRCALPHA` (5) / `INVSRCALPHA` (6) with **blend-enable
  OFF**; depth-test `LESSEQUAL` (4); **z-write ON**; **dithering ON**; fill mode **solid**;
  shade-model **Gouraud** (2); and **transparency OFF**. A port that begins each frame from a
  two-sided, depth-tested, unblended baseline and applies per-material overrides reproduces this
  faithfully.

- **No per-object depth sort at the pipeline level** (`rendering.md` §3): the cull performs
  only frustum visibility + LOD detail-scale selection. Draw-order correctness relies on
  bucket ordering + Z-test/Z-write + blend modes.

- **Per-view world transform**: before each of the sky, terrain/buildings, opaque-extras, and
  transparent pass callbacks, `Renderer_SetViewTransform` is called with the view matrix at
  view `+44`. Post/composite/present quads force identity world + identity view + an ortho
  projection.

- **Cull node mask**: `(1 << planeCount) - 1`; `planeCount` read from the stack frustum at
  offset `+4`. With six frustum planes, the mask is `0x3F`.

- **Detail/LOD scale**: derived per frame from FOV + aspect + viewport dimensions by
  `GView_ComputeDetailScale`; fed into `GCull_CullScene` as the LOD selection input for
  terrain cells and mesh-LOD tiers.

- **Frame statistics**: cull time (view `+256`) and draw time (view `+264`) accumulate
  double-precision per frame; averaged over a reporting interval (view `+280`) into an FPS
  figure (view `+296`). Statistics counters are held at cull-set `+736`.

---

## 13. Debugger-Confirm Items

The following are static-confirmed hypotheses requiring a live `?ext=dbg` session before they
are treated as implementation facts. All are NON-BLOCKING for most port work.

| # | Item | What to confirm |
|---|---|---|
| 1 | Active path — runtime landed value | Decision logic fully resolved statically (§2.3): flag at scene `+178748` defaults to `0` (direct); set to `2` iff `Renderer_InitCelGlowShaders` succeeds AND config-singleton `+0x30` == 1. Remaining: breakpoint `Renderer_DrawScene_Fork` in-world and read the live flag value to record which branch this build/config/GPU actually took. |
| 2 | Bloom source scope — optional visual cross-check | RT-A/RT-B/RT-C identities and routing are statically confirmed (§5): bloom extracted from RT-A pass 1 (sky+terrain+extras only); cel actors and transparent/UI are added in RT-A pass 2 and the present pass after extraction. Remaining: optional visual capture of RT-A vs RT-C surfaces to confirm glow exclusion in practice. |
| 3 | World up-axis direction | `GTransform` internal convention **statically confirmed** (deep-3d-cartography pass): row-major matrix multiply; composition `localMatrix × parentModelView` (local-first, row-vector). Post/composite/present quads confirmed left-handed (`D3DXMatrixOrthoLH`); world path uses RH perspective (per `rendering.md` §2.2). **Device-side upload order resolved statically (2026-06-30):** projection uploads via `SetTransform` transform-state **3** (`D3DTS_PROJECTION`); the per-pass view uploads via transform-state **2** (`D3DTS_VIEW`); the world view matrix is the inverse-orthonormal of the camera holder world matrix (no `LookAt` on the world path — `LookAtLH` is shadow-only). See §12. **Remaining `[debugger-confirm]`:** only the exact world up-axis direction. |
| 4 | Multi-view count at runtime | **Partially resolved.** `Docs/RE/structs/render_driver.md §1` confirms two instantiation contexts: a global singleton (used for opening, character-select, and in-world scenes) and private instances embedded in scene-window objects (confirmed for the login scene). The in-world node count and whether a character-preview inset adds a node or uses a separate driver remain `[debugger-pending]` — confirm via live `?ext=dbg` session; route to `re-validator`. |
| 5 | GView struct reconciliation | **RESOLVED.** The render-view object in §7 IS `Diamond::GView` — same class, confirmed via RTTI (class name `Diamond::GView`). See `Docs/RE/structs/gview.md §1` and `§6.2`. The four field discrepancies in `scene_graph_nodes.md` (+0x24, +0x70, +0x74, +0x130) are corrected there. |
| 6 | JointEffectManager and scene helper roles | Two JointEffectManager instances and two unnamed scene helpers are cached in the render-globals block (§6.2, slots 7, 9, 10, 13). Resolve their specific rendering roles. Route to effects/sky analyst. |
| 7 | Config-singleton `+0x30` key identity | The flag at scene `+178748` is gated on config-singleton field `+0x30` equalling 1 (§2.3). Which display/option key in `display.lua` (or the option system) drives this field is unknown. Route to the config/UI analyst. |
