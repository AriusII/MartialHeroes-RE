# Spec: Transparency Sort — Alpha-Pass Draw Ordering

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> describes **how transparent (alpha-blended) drawables are ordered for rendering** in the
> Direct3D 9 scene-graph pipeline: the definitive absence of a per-object depth sort, the
> three-tier ordering mechanism that replaces it, the transparency master switch, the per-object
> D3D9 render-state recipe, and the dormant squared-distance field.
>
> The frame-level draw-pass sequence (sky → terrain → opaque scene-graph → transparent/particles)
> is documented in `Docs/RE/specs/render_pipeline.md` §4/§6.1. The scene-graph cull and
> collection mechanism is in `Docs/RE/structs/scene_graph_nodes.md`. Per-pass blend modes and the
> glow chain are in `Docs/RE/specs/rendering.md`. The cull pipeline class hierarchy and struct
> layouts are in `Docs/RE/structs/cull_pipeline.md`. This spec cross-links rather than
> duplicates those.
>
> Every transparency-ordering or alpha-blend constant in C# must reference this file:
> `// spec: Docs/RE/specs/transparency_sort.md`.

> **Verification banner**
> - **verification:** *static-confirmed* for all findings in this document, recovered by
>   exhaustive static analysis of the collection path (`GCull_AppendToRegularBin`), the inner
>   pipeline bin machinery (`GSeparatedPipeline`, `GMultiplePipeline`, `GRegularPipeline`), the
>   draw-traverse function (`GCull__drawTraverse`), and the render-state bind helpers
>   (`ApplyAlphaBlend`, `ApplyZWrite`), including a full opcode scan of the cull/scene-graph/
>   pipeline code region with zero floating-point comparison instructions found in any pipeline
>   flush, bin, range-object, or draw-traverse path. Items explicitly tagged
>   `[debugger-confirm]` are static hypotheses awaiting a live `?ext=dbg` session.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the three-tier ordering model, the transparency
>   recipe, and the render-state type table. Items tagged `[debugger-confirm]` are NON-BLOCKING
>   residuals.
> - **evidence:** [static-ida], [static-exhaustive-opcode-scan]
> - **deep-3d-wave7 (2026-06-28):** New spec. Proves the definitive negative (no depth sort),
>   recovers the three-tier order mechanism and the transparency render-state recipe, and
>   corrects `Docs/RE/structs/cull_pipeline.md` Table E field `+12` (drawable, not transform —
>   see §6).
> - **deep-3d-cartography static pass (2026-06-29):** No new facts for this spec. §7
>   `[debugger-confirm]` items (GRangeObject `+12` drawable identity, drain order, blend
>   factor enum values) remain outstanding; static bound is unchanged.
> - **cross-links:**
>   `Docs/RE/specs/render_pipeline.md` (frame draw-pass sequence, §4/§6.1, §12);
>   `Docs/RE/specs/rendering.md` (per-pass blend modes, glow chain, UI blend model);
>   `Docs/RE/structs/scene_graph_nodes.md` (GNode/GGroup/GScene hierarchy, frustum cull visitor);
>   `Docs/RE/structs/cull_pipeline.md` (GRangeObject, GDrawablePair, GRenderElement layouts).

---

## Status

| Item | Confidence |
|---|---|
| No per-object depth sort — definitive negative | CONFIRMED (exhaustive static opcode scan) |
| Draw order: pass split → render-order-id buckets → submission order | CONFIRMED |
| `GRSTransparency` (type 15) as the transparency master switch | CONFIRMED |
| `ALPHABLENDENABLE` on, `ZWRITEENABLE` off per transparent drawable | CONFIRMED |
| Blend factor source: `GRSBlending` (type 1, +44/+48) | CONFIRMED |
| Two-sided rendering: `GRSCullMode` (type 2), independent of transparency | CONFIRMED |
| Squared-distance field `GRangeObject +4` — computed but never compared | CONFIRMED (exhaustive scan) |
| Render-order id 15 reserved as the transparent slot; id 18 = catch-all | CONFIRMED |
| `GRangeObject +12` = drawable pointer (correction to `cull_pipeline.md` Table E) | [debugger-confirm] |
| Exact drain path of the range pool into the final draw | [debugger-confirm] |
| Actual `D3DRS_SRCBLEND` / `D3DRS_DESTBLEND` factor values per material | [debugger-confirm] |

---

## 1. The Definitive Negative — No Per-Object Depth Sort

**Confidence: CONFIRMED (exhaustive static opcode scan).**

There is no per-object back-to-front depth sort of transparent geometry in this build. The
absence is proven, not assumed, by two independent lines of static evidence:

**No sort calls in the render cluster.** The C runtime sort function has exactly five call
sites in the entire binary; every one resides in the audio subsystem (sound/Vorbis code). The
render, cull, and scene-graph cluster contains no call to any sort routine.

**No float comparisons in the pipeline.** An exhaustive static opcode scan of the entire
cull/scene-graph/pipeline code region — covering every instruction across all flush, bin,
range-object, and draw-traverse functions — found zero floating-point comparison instructions
inside any of those paths. Every floating-point comparison found in the scan belongs to
frustum/AABB/sphere cull tests, particle sprite sizing, positional-light math, or per-frame
statistics averaging — none to a drawable-ordering comparator.

The combination of these two findings establishes that no comparator-based sort of transparent
drawables exists in any reachable path of the render/cull pipeline.

> **This deepens and confirms `render_pipeline.md` §12:** "No per-object depth sort at the
> pipeline level." The present spec adds the exhaustive proof and recovers the three-tier
> ordering mechanism that replaces depth sorting (§2).

---

## 2. Draw-Order Mechanism — Three Tiers

**Confidence: CONFIRMED.**

Alpha-blended drawables are ordered by three tiers applied in sequence. No tier involves a
depth comparison.

### 2.1 Pass-Level Split (Tier 1)

The coarsest ordering is a fixed pass split: the entire opaque world draws first (sky →
terrain/buildings → culled scene-graph opaque → opaque-world extras), followed by the
dedicated **`RenderPass_TransparentAndParticles`** callback as the last draw step. This
callback is installed at view offset `+172` (see `render_pipeline.md` §4 direct-path step 5h
and §6.1). All transparent geometry and particles draw through this callback, after all opaque
passes are complete. No per-object comparison operates at this tier.

### 2.2 Render-Order-Id Buckets (Tier 2)

Within the culled scene-graph draw, each drawable's leading render-state carries a
**render-order id** at render-state byte `+36`. The inner draw pipeline collects drawables into
bins keyed by that id and drains the bins in ascending id order.

Two inner pipeline variants are used, selected per frame by a capability flag:

| Pipeline class | Bin structure | Drain order |
|---|---|---|
| `GSeparatedPipeline` | Id-indexed vector — each id maps directly to a sub-bin by index | Ascending id, index 0 to N |
| `GMultiplePipeline` | Id-ordered linked list — bins inserted at the id-ordered position | Ascending id, list head to tail |

The bin configurator that builds the opaque bin list explicitly **skips render-order id 15**
(the reserved transparent slot) and always appends **render-order id 18** as the
catch-all/regular bin. Transparent drawables are therefore never placed into any opaque bin;
they are diverted at collection time to the range path (§3). The render-order-id namespace:

| Render-order id | Role |
|---|---|
| 0–14, 16–17 | Opaque material tiers (specific material-type assignments are a separate RE item, out of scope for this spec) |
| 15 | Reserved transparent slot — skipped from opaque bins; routed via `GRangeObject` path |
| 18 | Catch-all / regular opaque bin |

Each bin is a `GRegularPipeline` whose flush iterates its stored element vector in order and
submits each element via the element's own draw callback. No reordering occurs at flush time.

### 2.3 Submission Order Within a Bucket (Tier 3)

Within a single render-order-id bin, drawables are drawn in the order they were submitted
during the cull traversal of the scene graph. No comparator reorders them after submission.

**Per-node blend selection at draw time.** Within `GCull__drawTraverse`, each node's render-
state-set is applied, and then the drawable is submitted using either the blended or opaque
draw path. The choice is made by a flag at drawable byte `+76`: value 1 selects the blended
submit path (device "render-drawable" flag = 1); any other value selects the opaque submit
path. This per-drawable flag governs the D3D9 call variant — it does not influence draw
position within the submission order.

---

## 3. The Transparency Master Switch — GRSTransparency

**Confidence: CONFIRMED.**

The render-state system uses 18 pointer slots indexed by `4 × typeId`. The render-state at
**type id 15** — `GRSTransparency` — is the master switch for transparent rendering. Its
payload is a single boolean at set byte `+60`, payload byte `+44`.

When the `GRSTransparency` flag is set on a drawable's render-state-set, three effects follow:

1. **Route to the range path.** During collection in `GCull_AppendToRegularBin`, the drawable
   is diverted from the opaque bin pipeline into the range-object path and a `GRangeObject`
   record is constructed for it. It is never forwarded to any opaque render-order bin.

2. **Force `ALPHABLENDENABLE` on.** At draw time, `ApplyAlphaBlend` enables
   `D3DRS_ALPHABLENDENABLE` when either the `GRSBlending` enable byte (type 1, set slot 1,
   payload byte `+52`) is set **or** the `GRSTransparency` flag (type 15, payload `+44`) is
   set. Transparent objects therefore always draw with alpha blending enabled.

3. **Force `ZWRITEENABLE` off.** At draw time, `ApplyZWrite` disables `D3DRS_ZWRITEENABLE`
   when either the `GRSDepthMask` flag (type 5, payload `+44`) is clear **or** the
   `GRSTransparency` flag (type 15, payload `+44`) is set. The depth test (Z-read) remains
   active; only the depth write is masked.

> **On the coincidence of the number 15.** Render-order id 15 (the transparent bucket id,
> §2.2) and `GRSTransparency` type id 15 share the same integer value. They are orthogonal
> mechanisms: the type id indexes which render-state class occupies a given slot in the state
> set; the render-order id indexes which opaque bin an opaque drawable enters. Transparent
> drawables bypass the opaque bin machinery entirely.

---

## 4. Per-Object Transparent Render-State Recipe

**Confidence: CONFIRMED** (state assignments); blend-factor values `[debugger-confirm]`.

The D3D9 render state forced for each transparent drawable:

| D3D9 render state | Index | Value / source |
|---|---|---|
| `D3DRS_ALPHABLENDENABLE` | — | TRUE — forced by `ApplyAlphaBlend` when `GRSTransparency` is set |
| `D3DRS_ZWRITEENABLE` | — | FALSE — forced by `ApplyZWrite` when `GRSTransparency` is set |
| `D3DRS_ZFUNC` | — | Unchanged — depth test reads as configured; only write is masked |
| `D3DRS_SRCBLEND` | 19 | Source blend factor from `GRSBlending +44` — per material `[debugger-confirm]` |
| `D3DRS_DESTBLEND` | 20 | Destination blend factor from `GRSBlending +48` — per material `[debugger-confirm]` |
| `D3DRS_CULLMODE` | 22 | CW / CCW / NONE from `GRSCullMode +44` — independent per-material state |
| Device blended-submit flag | — | 1 when drawable `+76 == 1`; governs the D3D9 call variant, not draw position |

**Two-sided rendering** is controlled by `GRSCullMode` (type 2 → `D3DRS_CULLMODE`, index 22).
Its payload maps to three values: CW, CCW, or NONE; NONE disables face culling and produces
two-sided output. This state is fully independent of transparency — a material may be two-sided
whether or not it is alpha-blended.

**Actual blend-factor enum values** used per material (GRSBlending payload bytes `+44`/`+48`)
are `[debugger-confirm]` — enumerate live by reading `GRSBlending` instances mid-frame via
the `?ext=dbg` session.

### 4.1 Render-State Type Id Table

The 18-slot render-state-set uses pointer slots indexed by `4 × typeId`. Types relevant to
transparency and blending:

| Type id | Class | Payload of interest |
|---|---|---|
| 1 | `GRSBlending` | Source blend factor (+44), destination blend factor (+48), blend-enable byte (+52) |
| 2 | `GRSCullMode` | Cull-mode enum (+36, +44): CW / CCW / NONE |
| 5 | `GRSDepthMask` | Boolean: Z-write enable (+44) |
| 15 | `GRSTransparency` | Boolean: transparency master switch (+44) |

Additional classes present in the full 18-slot table (roles documented in
`Docs/RE/specs/rendering.md` and `Docs/RE/structs/scene_graph_nodes.md`): `GRSDepthTest`,
`GRSAlphaTest`, `GRSMaterial`, `GRSFog`, `GRSColorMask`, `GRSShadeModel`, `GRSFillMode`,
`GRSLinePattern`, `GRSDithering`, `GRSHighlight`, `GRSTFactor`, `GRSTransform`.

---

## 5. Computed Sort Key — A Dormant Field

**Confidence: CONFIRMED (exhaustive static opcode scan).**

`GCull_AppendToRegularBin`, transparent path, computes a squared camera-space distance and
stores it at `GRangeObject` byte `+4`. The computation:

1. Read the drawable's local bounding box as six floats at drawable bytes `+36..+56` (min at
   `+36`/`+40`/`+44`; max at `+48`/`+52`/`+56`). Compute the box centre as
   `(min + max) * 0.5` per axis.
2. Transform the centre into camera/view space using the render-state-set's transform state
   at set slot 4 (set byte `+16`), accessed via that state's transform-apply vtable slot.
3. Compute `x'² + y'² + z'²` — the full three-component squared Euclidean distance in
   camera/view space (not camera-Z alone; not a screen-space metric). Store the result at
   `GRangeObject +4`.

**This value is never read by any comparison or ordering function.** Both draw virtual-function
implementations on `GRangeObject` read only the render-state-set pointer at `+8` and the
drawable pointer at `+12`; neither reads `+4`. No other code in the render, cull, or
scene-graph cluster reads the `+4` field for comparison. The field is present and populated
but dormant — likely a disabled or legacy sort path preserved in the record layout, or a field
consumed only by a tool or debug build variant.

> **Port implication.** The squared-distance value can be computed and stored at `+4` to
> preserve the record layout, but need not feed any ordering decision in the re-implementation.
> The slot is dead weight in the current shipping build.

---

## 6. GRangeObject Layout — Correction to cull_pipeline.md Table E

**Confidence: CONFIRMED** (structure from static analysis); `+12` field identity:
`[debugger-confirm]`.

`GRangeObject` is a 16-byte record:

| Offset (bytes) | Field |
|---|---|
| `+0` | Vtable pointer |
| `+4` | Squared camera-space distance (computed by `GCull_AppendToRegularBin`; unused for ordering — see §5) |
| `+8` | Render-state-set pointer |
| `+12` | Drawable pointer `[debugger-confirm]` |

> **Correction to `Docs/RE/structs/cull_pipeline.md` Table E**, which labels `+12` as
> "Transform pointer." Static analysis of `GCull_AppendToRegularBin` shows the drawable pointer
> is stored at `+12`, and both draw virtual-function implementations on `GRangeObject` treat
> `+12` as the drawable, submitting it via the drawable's own draw vtable slot. The transform
> is accessed indirectly through the render-state-set's transform state at set slot 4 (byte
> `+16`), not stored directly in the `GRangeObject` record. Confirm by reading a live
> `GRangeObject` mid-frame via `?ext=dbg`.

The opaque-bin path produces a **`GDrawablePair`** record (vtable/container at `+0`, drawable
at `+4`, render-state-set at `+8`). This layout is unchanged from `cull_pipeline.md`.

**`GRenderElement`** (88 bytes) — used by `GGeode_CullCollectDrawables` →
`GCull_AppendRenderElement` for the multi-pass collection path — is confirmed at: render-state
id at `+0`; drawable at `+4`; model matrix (16 floats) at `+8`; light at `+72`. Collection is
gated by flag bits at `+28` (0x04 = id, 0x02 = drawable, 0x40 = matrix, 0x80 = light). This
layout is unchanged from `cull_pipeline.md` Table E.

---

## 7. Debugger-Confirm Items

The following are static-confirmed hypotheses requiring a live `?ext=dbg` session before they
are treated as implementation facts. All are NON-BLOCKING for port work on the ordering model
and transparency recipe (§2/§4). Route to `re-validator`; never use `dbg_start`.

| # | Item | What to confirm |
|---|---|---|
| 1 | `GRangeObject +12` = drawable | Read a live `GRangeObject` record mid-frame; confirm `+12` holds the drawable pointer (not a transform). If confirmed, `cull_pipeline.md` Table E field `+12` must be corrected to "drawable pointer." |
| 2 | Range-pool drain sequence and draw order | Breakpoint the transparent-flush path mid-frame; confirm that `GRangeObject` entries are submitted in submission/bucket order, not in distance order. This is the live cross-check of the definitive negative (§1). |
| 3 | Blend-factor enum values per material | Read live `GRSBlending` instances (payload bytes `+44`/`+48`) to enumerate the actual `D3DRS_SRCBLEND` / `D3DRS_DESTBLEND` factor constants in use per material type. |
| 4 | Dormant `+4` field usage | Confirm the computed `GRangeObject +4` distance is not consumed by any non-render path (stats/debug). Low priority — does not affect port correctness. |
