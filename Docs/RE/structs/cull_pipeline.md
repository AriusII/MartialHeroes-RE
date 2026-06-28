---
verification: confirmed (initial, IDB SHA f61f66a9, CYCLE 14 (2026-06-28))
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [render_pipeline, scene_graph]
cross_refs: [structs/gview.md, specs/render_pipeline.md, specs/scene_graph.md, specs/transparency_sort.md]
wave9_reconciliation: "Table E GRangeObject +12 corrected to drawable pointer (not transform) and +4 flagged as dormant (written but never read by any comparator) per Docs/RE/specs/transparency_sort.md §5/§6 (2026-06-28)."
deep_3d_cartography_deepening (2026-06-29): "GFrustum internal layout confirmed — header {+0 vtable, +4 count, +8 planes}, 16-byte plane stride {nx,ny,nz,D}, 6-plane total = 104 bytes; world-space GFrustum size at +144 corrected ~96→~104; GRangeObject +12 closed from [debugger-confirm] to confirmed; near/far plane-index ordering added as static-strong inference (near=index 1, far=index 3); 18-slot GRS* class enumeration and transform=slot-4 anchor documented; render-group-bitmask sort-key mechanism and GSeparatedPipeline +16 sub-bucket vector added; §6 open-items tightened."
---

# Structs: GCull / GCullPipeline / GStatsCull — Frustum-Cull Pipeline Objects

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. All offsets are byte offsets relative to the start of the named struct —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> Citing engineers: `// spec: Docs/RE/structs/cull_pipeline.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow and operand analysis,
> corroborated across the GCull constructor, destructor, `GCull_BeginView`, `GCull_CullScene`,
> `GCull_DrawAndFlush`, `GCull_ApplyRenderStateSet`, and the embedded pipeline paths;
> `[static-hypothesis]` = inferred or seen only as a zero-init — not independently re-isolated,
> do not assume without further confirmation; `[debugger-pending]` = flagged for live `?ext=dbg`
> session confirmation (never `dbg_start`).

---

## 1. Object identity

The per-view frustum-cull family consists of three related classes rooted in `Diamond::GCull`. All
three are confirmed polymorphic via RTTI. The confirmed class hierarchy is:

```
Diamond::GTraverser            (primary base of GCull — the scene-graph visitor interface)
Diamond::GPipeline             (root of the pipeline family)
  └─ Diamond::GCullPipeline    (: GPipeline) — draw-side dispatcher; embedded in GCull at +584
Diamond::GCull                 (: GTraverser, GCullPipeline) — the concrete cull object
  └─ Diamond::GStatsCull       (: GCull) — adds a GStats accumulator at +736
```

`Diamond::GView` (see `structs/gview.md`) owns one or two cull objects: the **primary** at GView
byte +112 and an optional **secondary** at GView byte +116. GView byte +108 (a one-byte selector)
determines which is active for a given frame: value 0 selects the primary; non-zero selects the
secondary. When the secondary is present it is always a `GStatsCull`, which is why the GView spec
notes that the secondary "carries `GStats` at its own internal +736".

**`Diamond::GCull`** (~736 bytes, heap-allocated) is both engines in one: it is the
*collect-traverse* engine (frustum-tests scene nodes and gathers visible drawables into sorted bins)
and the *draw-traverse* engine (walks the ordered draw queue and submits each drawable to the D3D
device wrapper). Its embedded `Diamond::GCullPipeline` sub-object at +584 owns the per-frame inner
pipeline that dispatches bin-draw calls.

**`Diamond::GCullPipeline`** (~76 bytes) is the draw-side dispatcher. It is embedded inside GCull
at byte +584. It owns a per-frame "inner pipeline" pointer (+4) instantiated each frame as either a
`GMultiplePipeline` (28 bytes) or a `GSeparatedPipeline` (32 bytes) depending on a capability query
(`GView_SelectPipelineKind`). As the common base class it is also the base sub-object that `GCull`
(and by extension `GStatsCull`) inherit.

**`Diamond::GStatsCull`** derives from `GCull` and appends a `GStats` sub-object at byte +736 that
accumulates average cull time and average draw time. It is the stats-instrumented variant used as
the secondary cull object. Its vtable slot 1 is an empty no-op hook; the actual per-frame draw
still executes through `GCull`'s free-function path with external timing writes into `GStats` at
+736.

---

## 2. Ownership and lifetime

### Construction

**`GCull` construction** proceeds in the following order:
1. Runs the `GTraverser` base constructor (at offset 0), installing the traverser interface vtable.
2. Overwrites offset +0 with the `Diamond::GCull` vtable.
3. Zeroes the early scalar fields in the +28/+32/+36 region.
4. Constructs two `GFrustum` sub-objects (a `GPolytope`-derived class) in place: one at +40 (the
   view-space frustum) and one at +144 (the world-space frustum), each receiving the `GFrustum`
   vtable.
5. Initialises the collection containers: the bin vector at +560 (initial capacity 10), the
   leaf/transform pool at +676 (initial capacity 100), the render-state-set pool at +700 (initial
   capacity 100), and smaller per-frame collection vectors.
6. Constructs the **embedded `GCullPipeline`** at +584, passing it a back-pointer to the bin vector
   at +560 so the pipeline can read the bins during the draw phase.
7. Initialises the **block-deque** draw-queue header at +724, allocating its circular-list sentinel.
8. Builds a temporary bin descriptor `{stride=10, kind=4}` and calls the separated-pipeline bin
   configurator on the embedded pipeline at +584.

**`GStatsCull` construction** calls the `GCull` constructor, overwrites +0 with the `GStatsCull`
vtable, then constructs a `GStats` sub-object at +736.

**`GCullPipeline` construction** (used both as an embedded sub-object inside `GCull` and for any
standalone pipeline): installs the `GCullPipeline` vtable at +0; nulls the inner-pipeline pointer
at +4; constructs a collected-drawable vector at +8 and a `GDrawablePair` container at +32; nulls
+60/+64/+68; stores the supplied bin-vector back-pointer at +72.

### Destruction

`GCull` destruction proceeds in reverse construction order: destroys the +724 draw deque, the +700
render-state-set pool, the +676 leaf pool, frees the +672 heap buffer if set, runs the embedded
`GCullPipeline` destructor at +584, destroys the +560 bin vector, frees the +388 heap buffer if
set, destroys the two `GFrustum` sub-objects at +144 and +40, then restores the base `GTraverser`
vtable at +0. `GView`'s destructor invokes this via the cull object's virtual deleting destructor
(vtable slot 0) for both the +112 and +116 fields.

### Per-frame reset

`GCull_BeginView` resets the object at the start of each frame: frees all queued draw nodes and
re-self-links the sentinel of the block-deque at +724 (zeroing the count at +732); resets all
collection vectors by setting their end pointer back to begin (no free, no reallocation); zeroes
the `GRSTransform` stack count; then rebuilds the frustums and the default render-state table from
the new camera parameters. The GCull object is reused across frames; only the per-frame draw-queue
nodes churn.

---

## 3. Field offset tables

### Table A — `Diamond::GCull` (~736 bytes; primary cull at GView +112)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0   | 4    | vtable ptr | Polymorphic dispatch. `Diamond::GCull` vtable (2 slots); overridden to `GStatsCull` vtable in derived instances. | confirmed |
| +4   | 4    | int | View parameter — level-of-detail / detail value (argument to `GCull_BeginView`). | confirmed |
| +8   | 4    | int | View parameter — render-target / pass identifier. | confirmed |
| +12  | 4    | int | View parameter — zeroed each `GCull_BeginView`. | confirmed |
| +16  | 4    | int | View parameter — argument 11 to `GCull_BeginView`. | confirmed |
| +20  | 4    | int | Viewport width (from the view-dimensions pair argument). | confirmed |
| +24  | 4    | int | Viewport height (from the view-dimensions pair argument). | confirmed |
| +28  | 1    | byte (flags) | Collection-mode flags. Bit 0x02 = store drawable; 0x04 = store render-state id; 0x40 = store model matrix; 0x80 = store light. Written by `GCull_BeginView`. | confirmed |
| +29  | 3    | — | Alignment pad. | — |
| +32  | 4    | int | View parameter — screen-height term for detail scale. | confirmed |
| +36  | 4    | int | View parameter — argument 8 to `GCull_BeginView`. | confirmed |
| +40  | ~104 | `GFrustum` (`GPolytope`-derived) | **View-space frustum.** Planes copied from the camera frustum each `GCull_BeginView`. | confirmed |
| +144 | ~104 | `GFrustum` (`GPolytope`-derived) | **World-space frustum.** Camera frustum planes transformed by the inverse-view matrix; this is the frustum tested against scene nodes during the recursive traverse. Same class as the view-space frustum at +40; size corrected from the earlier ~96 estimate. | confirmed |
| +240 | 8    | — | Unidentified gap preceding the view matrix. Not written by `GCull_BeginView`. | static-hypothesis |
| +248 | 64   | float[16] | **View matrix** (camera world-to-view). Written by `GCull_BeginView`; pushed to the device as the view transform. | confirmed |
| +312 | 64   | float[16] | **Inverse-view matrix** (view-to-world). Written by `GCull_BeginView`. | confirmed |
| +376 | ~16  | vector | **GRSTransform stack.** The root transform (identity, created each frame) is pushed here during `GCull_BeginView`; the recursive traverse pushes and pops nested transforms. | confirmed |
| +392 | 4    | int | `GRSTransform` id counter — incremented each time a new transform is created. | confirmed |
| +396 | 72   | ptr[18] | **18-slot default render-state-set heads.** One pointer per render-state type, indexed by `4 × stateType`. Seeded each `GCull_BeginView` from the scene's default and override render-state vectors. | confirmed |
| +468 | ~18  | byte[18] | **"default-applied" bits** — one bit per state type (indexed by `stateType`). Zeroed each `GCull_BeginView`. | confirmed |
| +486 | 2    | — | Alignment pad. | — |
| +488 | 72   | ptr[18] | **18-slot active render-state-set (working buffer).** Snapshot-copied from the queued set and applied/restored around each draw pass by `GCull_ApplyRenderStateSet`. | confirmed |
| +560 | ~24  | `GCullBinVec` | **Render-bin vector** (initial capacity 10). Visible drawables are sorted into these bins during the collect phase. Back-referenced by the embedded pipeline at +584. | confirmed |
| +584 | ~76  | `GCullPipeline` (embedded) | **Embedded draw-pipeline sub-object** (see Table B). Owns the per-frame inner pipeline pointer. | confirmed |
| +660 | ~16  | vector | **Pipeline stack.** Sub-pipelines are pushed and popped here during the draw phase. | confirmed |
| +676 | ~24  | leaf/transform pool | `GRSTransform` object pool (initial capacity 100). `GCull_BeginView` allocates the root transform from it. | confirmed |
| +700 | ~24  | render-state-set pool | Pool of 18-slot render-state-set objects (initial capacity 100). `GCull_SubmitActorDrawable` allocates one set per drawable. | confirmed |
| +724 | 12   | `GBlockDeque` header | **Ordered draw queue** (see Table D). Header word at +724, sentinel-node pointer at +728, entry count at +732. | confirmed |
| +736 | —    | `GStats` | Present **only** in `GStatsCull` — the avg-cull-time / avg-draw-time accumulator sub-object. | confirmed |

Several per-frame vector pairs in the +560..+724 region are cleared each frame (end pointer reset
to begin). Their element types are identified (bins, ranges, drawable pairs) but the exact
begin/end/capacity word offsets of every intermediate vector were not fully isolated this pass —
see §6.

### Table A.1 — `Diamond::GFrustum` (`GPolytope`-derived) internal layout

Both GFrustum sub-objects embedded in GCull (view-space at +40 and world-space at +144) are the
same class. Internal layout confirmed from `GPolytope_CopyPlanes` (copies `*(src+4)` count then
N×16-byte planes from `src+8`) and from all sphere/AABB classifier callers:

| Offset | Size | Type | Field |
|-------:|-----:|------|-------|
| +0 | 4 | vtable ptr | `GFrustum` / `GPolytope` vtable. |
| +4 | 4 | u32 | **Plane count.** For the camera frustum = 6; the initial mask fed to the scene-graph traverse = `(1 << count) − 1` = **0x3F**. |
| +8 | N×16 | `float[4]` per plane | **Plane array.** Each plane = `{nx, ny, nz, D}` as four f32 values (16 bytes). The sphere test evaluates `d = dot({nx,ny,nz}, center) + D`; `d − r > 0` ⇒ inside (fully culled, ret 2); `d + r < 0` ⇒ outside (ret 0); else straddling (ret 1). |

Total for 6 planes: 4 + 4 + 6 × 16 = **104 bytes**.

**Plane-index ordering (static-strong inference):** The `SkipNearFar` frustum classifier (terrain
quadtree only) explicitly skips plane indices **1 and 3** during iteration. This places **near
at plane index 1** and **far at plane index 3** in the camera's 6-plane layout (indices 0/2/4/5
are the four side planes). The camera function that writes these six planes was not located this
pass (see §6) — the ordering is a strong inference from the skip semantics, not yet byte-exact proof.

### Table B — `Diamond::GCullPipeline` (~76 bytes; embedded at GCull +584)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0  | 4   | vtable ptr | `GCullPipeline` vtable (6 slots). | confirmed |
| +4  | 4   | ptr | **Inner pipeline** — `GMultiplePipeline` (28 bytes) or `GSeparatedPipeline` (32 bytes), instantiated each frame by `GView_SelectPipelineKind`. Vtable slots 1, 3, and 5 forward to this object. Null in the constructor. | confirmed |
| +8  | ~24 | vector | Collected-drawable / range vector. Element count = `(end@+28 − begin@+12) / 4`; reported by vtable slot 2. | confirmed |
| +32 | ~24 | `GDrawablePair` container | Opaque-drawable pair store. | confirmed |
| +56 | 4   | (context) | Address `&(this+56)` handed to the inner pipeline as its context argument. | confirmed |
| +60 | 4   | ptr | Render-config / capability pointer. `*(+60)` is indexed to read the "needs multi-pass" flag that drives the `GMultiplePipeline` vs `GSeparatedPipeline` choice. | confirmed |
| +64 | 4   | int | Zero-initialised (constructor). | confirmed |
| +68 | 4   | int | Zero-initialised (constructor). | confirmed |
| +72 | 4   | ptr | **Bin-vector back-pointer** (points to `GCull` +560). | confirmed |

### Table C — `Diamond::GStatsCull` (derives from `GCull`)

Layout is identical to Table A through byte +735, with the following differences:

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0   | 4 | vtable ptr | Overwritten to the `GStatsCull` vtable (2 slots) after `GCull` construction completes. | confirmed |
| +736 | — | `GStats` | Stats sub-object: accumulates average cull duration and average draw duration over a reporting interval. | confirmed |

### Table D — `GBlockDeque` header (at GCull +724) and draw-queue node

Header (12 bytes at GCull +724):

| Offset (GCull abs.) | Size | Type | Role |
|--------------------:|-----:|------|------|
| +0 (+724) | 4 | — | Header word (written by the block-allocator init; semantics not recovered). |
| +4 (+728) | 4 | node ptr | **Sentinel node** of the circular doubly-linked list. When the queue is empty it points to itself in both directions. |
| +8 (+732) | 4 | int | **Entry count.** Guards the draw walk; zeroed by the per-frame reset. |

Draw-queue node (per-entry record; allocated from a block allocator; the four confirmed fields span
16 bytes):

| Offset | Size | Type | Role |
|-------:|-----:|------|------|
| +0  | 4 | node ptr | **Next** node. `drawTraverse` advances on this pointer; wraps to the sentinel. |
| +4  | 4 | node ptr | **Prev** node (doubly-linked upkeep; sentinel self-links both ways when empty). |
| +8  | 4 | ptr | **Render-state-set handle** — the 18-slot set for this drawable; passed to the device as engine render-state token 256. |
| +12 | 4 | ptr | **Drawable pointer** — the renderable object for this draw call. |

The per-frame reset walks the entire list, frees every node, and re-self-links the sentinel,
confirming the circular-list shape.

### Table E — Collected-drawable records (produced during collect; consumed by the draw flush)

The collect stage produces two bin-entry kinds and one richer geode-level element.

**`GDrawablePair`** (opaque bin entry):

| Offset | Size | Role |
|-------:|-----:|------|
| +0 | 4 | Container / vtable word. |
| +4 | 4 | Drawable pointer. |
| +8 | 4 | Render-state-set (18-slot) pointer. |

**`GRangeObject`** (transparent bin entry — range path; the squared-distance field at `+4` is written but dormant; no active back-to-front sort exists — see `Docs/RE/specs/transparency_sort.md §1/§5`):

| Offset | Size | Role |
|-------:|-----:|------|
| +0  | 4 | Container / vtable word. |
| +4  | 4 | **Squared distance to camera** — computed by `GCull_AppendToRegularBin` but **never read by any comparator** (dormant in the shipping build; no back-to-front sort is performed). Computed by transforming the drawable's AABB center (`(min+max)×0.5`, AABB min at drawable +36..+44, max at +48..+56`) into camera space via the render-state-set's transform state at slot 4 (set +16), then `x²+y²+z²`. See `Docs/RE/specs/transparency_sort.md §5`. |
| +8  | 4 | Render-state-set (18-slot) pointer. |
| +12 | 4 | **Drawable pointer** — statically confirmed; the drawable argument is stored directly at this field. The transform is not stored here; it is accessed indirectly through the render-state-set's transform state at slot 4 (byte `+16`). See `Docs/RE/specs/transparency_sort.md §6`. |

**`GRenderElement`** (88 bytes — the per-drawable element built by the geode collection pass and
stored in the pipeline's collection vector):

| Offset | Size | Role |
|-------:|-----:|------|
| +0  | 4  | Render-state id. Stored when collection flag 0x04 is set. |
| +4  | 4  | Drawable pointer. Stored when collection flag 0x02 is set. |
| +8  | 64 | Model matrix (float[16]). Identity-initialised; copied from the geode transform when collection flag 0x40 is set. |
| +72 | 4  | Light / colour block reference. Copied when collection flag 0x80 is set. |
| +76 | 4  | Reserved (zeroed). |
| +80 | 4  | Reserved (zeroed). |
| +84 | 4  | Reserved (zeroed). |

### Table F — Drawable fields read by the draw stage

The draw walk reads two fields on each drawable object (offsets from the drawable's own base):

| Offset | Size | Role |
|-------:|-----:|------|
| +76 | 1 | **Render-mode flag.** Value `1` selects the blended/transparent submit path; otherwise opaque. Non-zero also gates light-block application. |
| +80 | — | **Light parameter block.** Applied to the device when +76 is non-zero. |

---

## 4. Vtable layouts

### `GCullPipeline` vtable — 6 slots

| Slot | Role |
|---|---|
| [0] | Scalar/vector **deleting destructor**. |
| [1] | **Draw/flush** — adjustor-forward to the inner pipeline pointer (+4), slot 1. |
| [2] | **Report visible count** — computes `(end@+28 − begin@+12) / 4` and adds it to a stats accumulator. |
| [3] | **Get / begin active set** — forwards to the inner pipeline pointer (+4), slot 3. Returns the render-state-set to restore after the pass. |
| [4] | **Begin / reset** — calls the inner pipeline (+4) slot 4, then resets the collection vectors (end := begin). |
| [5] | **End / teardown** — forwards to the inner pipeline (+4) slot 5. |

### `GCull` vtable — 2 slots (extends the `GTraverser` interface)

| Slot | Role |
|---|---|
| [0] | **Deleting destructor.** |
| [1] | **Draw visible items** (`GCull_DrawAndFlush`): runs `drawTraverse` on the ordered draw queue at +724, then flushes the embedded pipeline at +584. |

The recursive collect-traverse virtual is on the `GTraverser`-interface portion of the vtable,
invoked by `GCull_CullScene` via the traverser dispatch with a frustum-node mask. It does not
appear as a named slot in this 2-entry table.

### `GStatsCull` vtable — 2 slots

| Slot | Role |
|---|---|
| [0] | **Deleting destructor** (frees and runs the stats-cull teardown). |
| [1] | **Empty / no-op hook.** Stats timing is driven externally via writes into `GStats` at +736; the per-frame draw still runs through `GCull`'s free-function path. |

---

## 5. Render-pass and dispatch details

### Collect-traverse — `GCull_CullScene` → `GCull_BeginView` → recursive traverse

1. `GCull_CullScene` computes a viewport detail scale, then calls `GCull_BeginView`.
2. `GCull_BeginView` performs the per-frame reset and setup:
   - Clears the draw deque (+724) and all collection vectors.
   - Stores the view matrix at +248 and its orthonormal inverse at +312.
   - Copies the camera frustum planes into the view-space frustum at +40, then transforms them by
     the inverse-view matrix into the world-space frustum at +144 (the set tested against scene
     nodes during the traverse).
   - Creates the **root `GRSTransform`** (identity matrix, id from the counter at +392), pushes it
     onto the transform stack at +376, and records it as the current transform.
   - Pushes the view transform to the D3D device wrapper.
   - Seeds the **18-slot default render-state table** (+396 heads, +468 applied-bits):
     `GCull_SetGlobalRenderState` sets each slot and its applied bit from the scene's default
     render-state vector; `GCull_LinkRenderStateOverride` chains override states without setting
     the applied bit.
   - Calls `GView_SelectPipelineKind` on the embedded pipeline (+584): if the capability flag at
     `*(+60)` is set, creates a `GMultiplePipeline` (28 bytes); otherwise creates a
     `GSeparatedPipeline` (32 bytes). The chosen pipeline is wired with the bin-vector back-pointer
     and a context pointer, then stored at `GCullPipeline` +4.
   - Calls the embedded pipeline's vtable slot 4 (begin/reset).
3. The recursive scene-graph traverse visits each node; visible `GGeode` nodes are collected:
   - `GCull_SubmitActorDrawable` allocates a fresh 18-slot render-state-set from the pool at +700,
     snapshot-copies the 18 default slots from +396, overlays per-drawable override states
     (indexed by the state object's type at state +40, skipping slots whose applied-bit at +468 is
     set), then routes the `GDrawablePair{drawable, set}` to the active sub-pipeline (via the
     pipeline stack at +660 if non-empty) or to `GCull_AppendToRegularBin`.
   - `GCull_AppendToRegularBin` (operating on the embedded pipeline at +584): the transparent/range
     path is triggered by the material flag at `*(u8)(*(set+60) + 44)` (set slot index 15, that
     state object's `+44` range/transparent flag); when set, it computes the squared camera distance
     and stores a `GRangeObject{dist, set, drawable}` in the range vector. Otherwise it stores a
     `GDrawablePair{drawable, set}` and forwards to the inner pipeline. No back-to-front sort is
     applied; distSq in GRangeObject+4 is produced but dormant.
   - **Bucket assignment is by render-group bitmask** (not distance, not render state).
     `Diamond_GDrawable_SelectRenderGroup` walks the drawable's render-group vector (drawable +60)
     and returns the first group whose mask ANDs non-zero with the current pass mask, else the default
     (first) group. This group tag drives which sub-bucket inside `GSeparatedPipeline` receives the
     drawable.
   - After all nodes are visited, bins/pairs/ranges are flushed into the `GBlockDeque` at +724 as
     draw nodes `{next, prev, state-set, drawable}`. **Submission order = sub-bucket vector order
     (data-driven):** `GSeparatedPipeline`'s flush iterates its ordered sub-bucket vector at +16 in
     index order; the concrete bucket sequence (opaque/alpha/particle) is set when the scene's
     render-group configuration populates the sub-buckets — see §6.

### Draw-traverse — `GCull_DrawAndFlush` (vtable slot 1) → `drawTraverse`

1. Calls the embedded pipeline (+584) vtable slot 3 to obtain the active render-state-set and
   visible count.
2. If the deque count at +732 is non-zero, walks the circular list from the sentinel at +728:
   - Per node: sets **engine render-state token 256** to the node's state-set handle (node +8) via
     the device wrapper's state-setter (device vtable offset +176); applies the drawable's light
     block if the drawable's render-mode flag (+76) is non-zero; then submits the drawable via the
     device wrapper's render-drawable entry (device vtable offset +212), passing a blended flag of
     1 when drawable +76 == 1 (transparent), otherwise 0 (opaque).
   - Advances on node +0 until returning to the sentinel.
3. Applies the full **18-slot render-state-set** via `GCull_ApplyRenderStateSet` and flushes the
   embedded pipeline.

### The 18-slot render-state dispatcher — `GCull_ApplyRenderStateSet`

Operates on a source and a working 18-pointer render-state-set (each 72 bytes). It first
**snapshot-copies all 18 pointer slots** from the source set into the working set, then iterates
the 18 slots and for each non-null slot calls that render-state object's **apply virtual (vtable
slot 2)**, passing the set as context. This is the "apply the whole render-state-set, then restore"
mechanism.

The 18 slots correspond to 18 distinct `GRS*` render-state types. Each `GRS*` class carries its
type index at object `+40` (set in its base constructor), and that index is used as `slot i` in
the 18-slot array (accessed as `set + 4×i`). The full class set recovered from RTTI is:
`GRSTransform`, `GRSMaterial`, `GRSLighting`, `GRSCullMode`, `GRSBlending`, `GRSDepthTest`,
`GRSDepthMask`, `GRSAlphaTest`, `GRSTransparency`, `GRSTFactor`, `GRSColorMask`, `GRSLinePattern`,
`GRSDithering`, `GRSHighlight`, `GRSShadeModel`, `GRSFillMode`, `GRSFog`, `GRSBoolean` (base for
boolean on/off states). **Transform is anchored at slot index 4** (set `+16` = `4×4`): the
squared-distance computation in `GCull_AppendToRegularBin` reads the transform from `set+16`, and
the root transform is pushed there by `GCull_BeginView`. The numeric slot indices for the remaining
17 classes are the next recoverable detail — see §6.

### Inner pipeline kinds

| Class | Size | Usage |
|---|---|---|
| `GMultiplePipeline` | 28 bytes | Multi-pass pipeline. Created when the capability flag at `*(GCullPipeline +60)` is set. |
| `GSeparatedPipeline` | 32 bytes | Opaque/transparent separated pipeline (default). Set up with `{stride=10, kind=4}`. Holds an ordered sub-bucket vector at **+16**; its flush iterates this vector in index order (see collect-traverse above). |

Both are wired with `{binVector, &(pipeline+56), 0}` and stored at `GCullPipeline` +4.
`GSeparatedPipeline` +16 sub-bucket vector is confirmed; the full per-field layout of both classes
and the concrete render-group → sub-bucket population order are still open — see §6.

---

## 6. Open items and pending confirmation

**Closed this pass:** GRangeObject+12=drawable (Table E), world-space GFrustum size (+144 = 104 B),
GFrustum internal layout (Table A.1), GRS* class enumeration, transform=slot-4 anchor,
render-group-bitmask sort key, GSeparatedPipeline +16 sub-bucket vector, GSeperatedPipeline size
= 32 B confirmed, GMultiplePipeline size = 28 B confirmed, GRangeObject+4 squared-distance
computation detail.

**Still [debugger-pending]** — for a live `?ext=dbg` session (never `dbg_start`):

- **GCull total size + live +488 confirmation.** Field offsets through +724 are all mapped
  statically; the heap-alloc block total (expected 736 bytes) and that +488 holds 18 live non-null
  render-state pointers mid-frame require reading the live GCull object at GView+112. Recommend:
  host-window → GView+112 → GCull; read 736 bytes; confirm +488..+559 are 18 non-null ptrs in a
  drawable-heavy frame.
- **`GStatsCull` vtable slot 1 draw path.** Static: confirmed no-op hook; external timing writes
  feed GStats at +736. Live: trace how the stats-instrumented secondary (GView+116) drives drawing
  and where timing writes land at +736.
- **TerrainManager frustum roles at +148 / +360.** Carried from `specs/occlusion_culling.md §6`.
- **`OPTION_VIEW_CHAR` / `OPTION_VIEW_BACK` (1..3) → radius/budget mapping.** Carried.

**Static-recoverable (not done this pass):**

- **Full 18-slot GRS* type-index → slot-number map.** Mechanism confirmed; transform=4 anchored.
  Decompiling each GRS* base constructor's store to object+40 would complete the mapping for all
  18 classes (material, lighting, blend, fog, etc.).
- **Camera frustum plane-BUILD.** The perspective-camera function that writes the 6 planes into
  the camera's GFrustum sub-object was not located. Confirming it writes near at index 1 and far
  at index 3 (the static-strong inference from the SkipNearFar skip set) requires tracing the
  camera frustum update caller. This belongs in the camera spec lane.
- **GSeparatedPipeline sub-bucket vector population order.** The flush-order is confirmed
  (sub-bucket vector at +16, iterated in index order). Which render-group tag occupies which
  bucket index (the concrete opaque / alpha / particle assignment) is set during scene render-group
  config build — not yet traced.
- **GMultiplePipeline (28 bytes) internal layout.** Size confirmed; field layout not recovered.
- **GStats (+736) and `GRSTransform` / individual `GRS*` class layouts.** Each is its own
  struct lane.
- **Block-deque node block size.** The four confirmed node fields total 16 bytes; whether the
  block allocator adds trailing bytes was not measured.
- **+240..+247.** 8 bytes not written by `GCull_BeginView`; purpose unknown.
- **Intermediate vectors in +560..+724.** Confirmed sub-objects: bin vector (+560), embedded
  pipeline (+584), pipeline stack (+660), leaf pool (+676), render-state-set pool (+700), draw
  deque (+724). Precise begin/end/capacity word offsets for the intermediate vectors in this
  region were not fully isolated.
- **Engine render-state token 256.** The draw walk sets device-wrapper state index 256 to the
  node's state-set handle; the device-side meaning should be cross-checked against the
  device-state map in `specs/render_pipeline.md`.

---

## 7. Cross-references

- Per-view render object owning this cull family: `structs/gview.md`.
- Render-pass frame sequence and the D3D device wrapper: `specs/render_pipeline.md`.
- Scene-graph class hierarchy (`GTraverser`, `GGeode`, `GGroup`, `GScene`, `GCamera`,
  `GFrustum`, `GViewPlatform`): `specs/scene_graph.md`.
- Terrain streaming and cell management: `specs/terrain-streaming.md`.
- Runtime singletons (global scene/post object, frame driver): `structs/runtime_singletons.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
