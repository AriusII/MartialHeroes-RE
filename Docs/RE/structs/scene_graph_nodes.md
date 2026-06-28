---
verification: confirmed (initial, IDB SHA f61f66a9, CYCLE 14 (2026-06-28)); deepened (wave-11 deep-3d static pass, IDB SHA f61f66a9, 2026-06-28) — bounding-sphere field order corrected (radius at +0x24, centre at +0x28), GNode +0x08 closed as std::string m_name, +0x34 renamed m_autoComputeBound (polarity confirmed), GTransform +0xDC polarity corrected (1=bypass), GTransform slot 3 closed, GScene +0x5C corrected to render-state set, GGeode slot-9 corrected, GLight +0x54/+0x84 sizes tightened; deepened (deep-3d-cartography static pass, IDB SHA f61f66a9, 2026-06-29) — GNode base vtable confirmed present (transiently installed by base-init, overwritten by every derived ctor; census null resolved); GLight +0x50..+0xB7 confirmed as exact 104-byte embedded D3DLIGHT9 (Type=3 DIRECTIONAL); full D3DLIGHT9 sub-field table added (Diffuse/Specular/Ambient/Position/Direction/Range/Falloff/Attenuation/Theta/Phi); GTransform modelview composition confirmed local-first row-major (localMatrix × parentModelView); slot-2 update-gate refinement (bit0 cleared even when autoComputeBound=0); setDirty conditional propagation confirmed; traversal order confirmed DFS pre-order child-index across all slots; GLight slot 6 and slot 7 body roles added
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [scene_graph, render_pipeline]
cross_refs: [structs/gview.md, structs/cull_pipeline.md, specs/scene_graph.md, specs/render_pipeline.md]
---

# Structs: GObject / GNode / GGroup / GScene / GTransform / GSwitch / GGeode / GViewPlatform / GLight — Scene-Graph Node Classes

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. All offsets are byte offsets relative to the start of the named struct —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> Citing engineers: `// spec: Docs/RE/structs/scene_graph_nodes.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static RTTI walk, constructor/destructor
> control-flow analysis, and vtable-content reading, corroborated across the base-init, child-init,
> and per-class constructors; `[static-hypothesis]` = inferred from zero-init or pattern only — not
> independently re-isolated, do not assume without further confirmation;
> `[open]` = exact value or layout not fully isolated this pass, flagged for resolution;
> `[debugger-confirm]` = flagged for live `?ext=dbg` session confirmation (never `dbg_start`).
>
> **Reconciliation note:** the committed `specs/scene_graph.md` carries slot labels for GNode
> slots 1, 2, 4–9 that diverge materially from the content recovered here. This spec carries the
> corrected, content-derived reading. `specs/scene_graph.md` must be reconciled against this
> record; on any conflict this struct spec (backed by constructor + vtable-content analysis) is
> authoritative.

---

## 1. Class hierarchy

### 1.1 Inheritance (RTTI-confirmed)

All nine node classes were confirmed polymorphic via MSVC RTTI: each vtable's complete-object-locator
was verified before slot enumeration, and the base-class arrays were walked to confirm the depth and
order of inheritance.

```
Diamond::GObject                    (refcount root: vtable + m_refCount)
├─ Diamond::GNode                   base scene node            [vtable, 10 slots]
│  ├─ Diamond::GGroup               branch / child container   [vtable, 16 slots]
│  │  ├─ Diamond::GScene            scene root + render-state  [vtable, 16 slots]
│  │  ├─ Diamond::GTransform        local-matrix transform      [vtable, 16 slots]
│  │  └─ Diamond::GSwitch           active-child selector       [vtable, 16 slots]
│  ├─ Diamond::GGeode               geometry leaf node          [vtable, 10 slots]
│  ├─ Diamond::GViewPlatform        camera-carrier node         [vtable, 10 slots]
│  └─ Diamond::GLight               light node                  [vtable, 10 slots]
├─ Diamond::GCamera                 projection + frustum (GObject sibling branch)
├─ Diamond::GDrawable  (abstract)   drawable base
│  └─ Diamond::GGeometry            concrete geometry + state-set
└─ Diamond::GDrawablePair : GDrawItem   12-byte leaf draw-command
```

RTTI base-class arrays (numBaseClasses confirmed):
`GTransform` → {GTransform, GGroup, GNode, GObject};
`GScene` → {GScene, GGroup, GNode, GObject};
`GSwitch` → {GSwitch, GGroup, GNode, GObject};
`GGroup` → {GGroup, GNode, GObject};
`GGeode` → {GGeode, GNode, GObject};
`GViewPlatform` → {GViewPlatform, GNode, GObject};
`GLight` → {GLight, GNode, GObject};
`GGeometry` → {GGeometry, GDrawable, GObject};
`GDrawablePair` → {GDrawablePair, GDrawItem}.

**Confirmed absence:** no `GBillboard` and no GLOD (level-of-detail) node class exists in this build.
The only specialised group nodes are `GTransform` and `GSwitch`.

**GNode abstract status (deep-3d-cartography — updated):** RTTI confirms GNode's complete-object-locator
exists but is not referenced by any concrete derived-class vtable in the binary's read-only data segment,
confirming that `GNode` is never instantiated as a standalone object. A `GNode` base vtable is confirmed
present and is transiently installed at construction by the shared base-init path, then immediately
overwritten by each derived class constructor. Slot 7 (`computeBound`) has a shared base implementation
(unions children bounding spheres; shared with `GGroup` and its sub-classes — see §4.1). Slot 6
(`cullTraverseChildren`) does not have a confirmed standalone `GNode` base body; `GGroup` and `GTransform`
each provide their own implementations. [slot 7 confirmed; slot 6 base body open — low priority]

### 1.2 Construction order

Each derived constructor writes its own vtable at +0x00 then chains its base constructor downward.
The shared base-init (run by the `GNode`-base code path) zeroes the bounding sphere fields
(+0x24..+0x33), sets `m_autoComputeBound` (+0x34) to 1 and `m_flags` (+0x38) to 1, and constructs the
parent `GVector` at +0x3C. `GGroup` and all of its sub-classes additionally run the `GGroup`
constructor, which constructs the child `GVector` at +0x4C, before each derived class stamps its
own vtable and initialises its own extra fields.

### 1.3 Ownership and lifetime

- A `GGroup` **owns refcounted references to its children**: `addChild` increments the child's
  refcount; the destructor unrefs every child, removes itself from each child's parent list, and
  invokes the child's deleting destructor (vtable slot 0) for any child whose refcount reaches zero.
- A `GGeode` owns its geometry/drawable list the same way (unref + conditional delete on teardown).
- Each node holds a **non-owning back-list of parents** (the `GVector` at +0x3C); the child never
  releases its parents.
- `GViewPlatform` owns a **refcounted `GCamera`** at +0x50; the refcount is incremented in the
  `GViewPlatform` constructor.
- Bounding-sphere dirtiness propagates upward: `setDirty` (slot 9) ORs a flag bit into `m_flags`
  (+0x38) and recurses slot 9 on every entry in the parent list, so a geometry change at a leaf
  marks every ancestor dirty. Bounds are recomputed lazily during the UPDATE pass (slots 7 and 8).

---

## 2. GVector — internal dynamic pointer array (16 bytes)

All child, parent, and geometry lists inside the node classes use the engine's `GVector`, a 16-byte
dynamic-pointer-array struct. The layout below is confirmed by constructor and iterator control-flow
and is consistent with `specs/scene_graph.md §2.1`.

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 4 | int | Element count (size). | confirmed |
| +0x04 | 4 | int | Allocated capacity. | confirmed |
| +0x08 | 4 | int | Element stride (4 for pointer arrays). | confirmed |
| +0x0C | 4 | void* | Heap data pointer. | confirmed |

**ABI reconciliation note (wave-11):** a distinct 16-byte engine dynamic-array container also
appears in the engine, used by `GScene`'s default render-state set at +0x5C. Its field layout is
`{reserved@+0, begin*@+4, end*@+8, cap*@+0xC}` with `size = (end - begin) >> 2` — a
begin/end/capacity iterator layout rather than the count/capacity/stride/data layout above. Whether
these are two different structs or the same struct reinterpreted in different contexts is not yet
determined. See Table D note and §6 open items.

---

## 3. Field offset tables

### Table A — `Diamond::GObject` (8 bytes; root base sub-object)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 4 | vtable ptr | Polymorphic dispatch pointer. | confirmed |
| +0x04 | 4 | int | `m_refCount` — reference count; object is freed when decremented to zero. | confirmed |

### Table B — `Diamond::GNode` (76 / 0x4C bytes total; base scene node)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 4  | vtable ptr | Polymorphic dispatch pointer. | confirmed |
| +0x04 | 4  | int | `m_refCount` (from `GObject`). | confirmed |
| +0x08 | 4  | — | `std::string` allocator / reserved word. | confirmed |
| +0x0C | 16 | char[16] or char* | `m_name` — `std::string` SSO buffer (`_Bx` union): inline storage when length < 16; heap pointer otherwise. Written by `setName`; read by `getName` and the dump slot (slot 1). | confirmed |
| +0x1C | 4  | size_t | `std::string` length (`_Mysize`). | confirmed |
| +0x20 | 4  | size_t | `std::string` capacity (`_Myres`). | confirmed |
| +0x24 | 4  | float | `m_boundRadius` — bounding-sphere radius. Sentinel: 0.0 = empty sphere; negative value = infinite / always-visible (used by `GLight` which seeds −1.0). Zeroed by base-init. | confirmed |
| +0x28 | 4  | float | `m_boundCenter.x` — bounding-sphere centre X. Zeroed by base-init. | confirmed |
| +0x2C | 4  | float | `m_boundCenter.y` — bounding-sphere centre Y. Zeroed by base-init. | confirmed |
| +0x30 | 4  | float | `m_boundCenter.z` — bounding-sphere centre Z. Zeroed by base-init. | confirmed |
| +0x34 | 4  | int | `m_autoComputeBound` — bound auto-compute enable flag (default 1). When 1, the UPDATE finalizer calls `computeBound` (slot 7) whenever `m_flags` bit 0 is set; when 0, the stored sphere is kept fixed and the dirty bit is cleared without recomputation. `GViewPlatform` and `GLight` constructors seed 0 here (their bounds are fixed or infinite respectively). | confirmed |
| +0x38 | 1  | byte | `m_flags` — multi-bit state byte (default 1). Bit 0 = bound-dirty (set on geometry change, OR-propagated from children in `computeBound`, cleared on refresh); bit 2 = traversed-this-frame (cleared by the UPDATE finalizer). Bits 1, 3..7 not yet enumerated — `setDirty` (slot 9) ORs a caller-supplied runtime mask into this field and recurses slot 9 on every parent **only if** the OR-result differs from the pre-OR value (conditional propagation — confirmed deep-3d-cartography pass); the mask values are not constants enumerable from the `setDirty` body itself (virtual dispatch only — all 20 use sites are vtable data slots; masks are computed by callers at their call sites). The prior `specs/scene_graph.md` label "m_bVisible" is inconsistent with this multi-bit usage and is superseded here. | confirmed (bits 0, 2; conditional propagation); [debugger-confirm] (bits 1, 3..7) |
| +0x39 | 3  | — | Alignment padding. | — |
| +0x3C | 16 | GVector | `m_parents` — non-owning back-list of parent nodes. | confirmed |

**Bounding-sphere field order (wave-11 correction):** prior editions of this spec listed the
bounding sphere as centre-then-radius (`m_boundCenter` at +0x24, `m_boundRadius` at +0x30). Three
independent static readers confirm the correct order is radius-then-centre: the sphere-expand
function treats element [0] as the radius; the sphere-vs-frustum classify function gates on element
[0] for the negative-radius (infinite) sentinel; and the debug formatter explicitly prints
`"bsphere: [<radius>, <center>]"`. The corrected table above reflects this.

### Table C — `Diamond::GGroup` (92 / 0x5C bytes total; extends GNode)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 76 | GNode | Base `GNode` sub-object (Table B). | confirmed |
| +0x4C | 16 | GVector | `m_children` — owned, refcounted child-node list. | confirmed |

### Table D — `Diamond::GScene` (~120 / 0x78 bytes total; extends GGroup)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 92  | GGroup | Base `GGroup` sub-object (Table C). | confirmed |
| +0x5C | 16  | engine-vector | `m_defaultRenderState` — 16-byte engine dynamic-array (`{reserved@+0x5C, begin@+0x60, end@+0x64, cap@+0x68}`). Owns the 17 render-state objects installed by `BuildDefaultRenderState`: AlphaTest(func=5, ref=150), Blending(5,6,0), ColorMask(1,1,1,1), CullMode(2), DepthMask(1), DepthTest(4), Dithering(1), FillMode(0), GTexture(0), Highlight(0), unnamed-state-object, LinePattern(0xFFFF,1), Material, ShadeModel(2), Transparency(0), TFactor(0,0), Fog. Objects are refcounted and destroyed in the scene destructor. | confirmed |
| +0x6C | 12  | std::vector | Secondary dynamic-object list (begin/end/cap, standard 3-pointer layout). Zeroed in the scene constructor. Element type not yet isolated (candidate: camera-ptr or light-ptr list). | [debugger-confirm] |

**Correction (wave-11):** prior editions of this table labelled +0x5C as "Active-camera pointer list"
and +0x6C as "Active-light pointer list." Both labels were incorrect. +0x5C/+0x60 is the default
global render-state attribute set (16-byte engine-vector, 17 objects). The +0x6C element type is
unconfirmed — see §6.

### Table E — `Diamond::GTransform` (224 / 0xE0 bytes total; extends GGroup)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00  | 92 | GGroup | Base `GGroup` sub-object (Table C). | confirmed |
| +0x5C  | 64 | float[16] | `m_localMatrix` — 4×4 row-major local/model transform. Identity by default. Composition order (deep-3d-cartography confirmed): `modelview = m_localMatrix × parentModelView` — the local transform is applied first (row-vector convention, pre-multiplication by local). The matrix multiply helper uses a row-major reading convention: `dst[i][j] = Σ A[i][k] · B[k][j]`. | confirmed |
| +0x9C  | 64 | float[16] | `m_inverseMatrix` — inverse / plane-transform matrix. Used to transform frustum planes and coordinates into node-local space during cull and intersect traversal; also used by slot 3 to transform caller-supplied matrices into local space. Identity by default. | confirmed |
| +0xDC  | 1  | byte | `m_transformActive` — **bypass/identity flag** (default 1). When set (1), the node behaves as a plain `GGroup` and all matrix math is skipped (cull and intersect slots delegate to the group path). When clear (0), `m_localMatrix` and `m_inverseMatrix` are composed into the traversal. Expected to be cleared to 0 when a non-identity matrix is installed via the public setter; the exact setter was not isolated this pass. | confirmed (polarity and default); [debugger-confirm] (setter that clears to 0) |
| +0xDD  | 1  | byte | Absolute reference-frame flag. Passed to the modelview push in slot 6. | confirmed |
| +0xDE  | 1  | byte | Relative reference-frame flag. Passed to the modelview push in slot 6. | confirmed |
| +0xDF  | 1  | — | Alignment padding. | — |

### Table F — `Diamond::GSwitch` (96 / 0x60 bytes total; extends GGroup)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 92 | GGroup | Base `GGroup` sub-object (Table C). | confirmed |
| +0x5C | 4  | int | `m_activeChild` — active-child selector. **-1 = all children active** (delegates to the full group/transform cull path); **-2 = no children active** (entire subtree is skipped); **>= 0 = exactly that child index** is culled and intersected. Default -1. | confirmed |

### Table G — `Diamond::GGeode` (92 / 0x5C bytes total; extends GNode)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 76 | GNode | Base `GNode` sub-object (Table B). | confirmed |
| +0x4C | 16 | GVector | `m_geometries` — owned, refcounted list of `GGeometry` / `GDrawable` objects. | confirmed |

### Table H — `Diamond::GViewPlatform` (84 / 0x54 bytes total; extends GNode)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 76 | GNode | Base `GNode` sub-object (Table B). `m_autoComputeBound` (+0x34) seeded to 0 in the constructor; bound is fixed to empty (radius 0) and never auto-computed. | confirmed |
| +0x4C | 1  | byte | Mode / flag byte (zero-initialised in constructor). | confirmed |
| +0x4D | 3  | — | Alignment padding. | — |
| +0x50 | 4  | GCamera* | `m_camera` — held camera reference (refcount incremented in the constructor, decremented in the destructor). The per-view setup code dereferences this field at +0x50 to perform camera setup. See `structs/gview.md`. | confirmed |

### Table I — `Diamond::GLight` (184 / 0xB8 bytes total; extends GNode)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 76 | GNode | Base `GNode` sub-object (Table B). `m_autoComputeBound` (+0x34) seeded to 0; `m_boundRadius` (+0x24) seeded to −1.0 (infinite/always-visible sentinel). | confirmed |
| +0x4C | 1  | byte | Enabled flag (initialised to 1). | confirmed |
| +0x4D | 3  | — | Padding. | — |
| +0x50 | 4  | D3DLIGHTTYPE (u32) | **Embedded `D3DLIGHT9` block begins here** (104 bytes total, +0x50..+0xB7 — byte-exact match to the public Direct3D 9 `D3DLIGHT9` structure, confirmed deep-3d-cartography pass). `Type` field — initialised to 3 = `D3DLIGHT_DIRECTIONAL`. | confirmed |
| +0x54 | 16 | D3DCOLORVALUE | `D3DLIGHT9::Diffuse` — RGBA float[4]; defaults 1.0, 1.0, 1.0, 1.0. | confirmed |
| +0x64 | 16 | D3DCOLORVALUE | `D3DLIGHT9::Specular` — RGBA float[4]; defaults 1.0, 1.0, 1.0, 1.0. | confirmed |
| +0x74 | 16 | D3DCOLORVALUE | `D3DLIGHT9::Ambient` — RGBA float[4]; defaults 1.0, 1.0, 1.0, 1.0. | confirmed |
| +0x84 | 12 | D3DVECTOR | `D3DLIGHT9::Position` — XYZ float[3]; defaults 0.0. (Unused by directional lights per D3D9 semantics, but preserved in layout.) | confirmed |
| +0x90 | 12 | D3DVECTOR | `D3DLIGHT9::Direction` — XYZ float[3]; defaults 0.0. | confirmed |
| +0x9C | 4  | float | `D3DLIGHT9::Range` — defaults 0.0. | confirmed |
| +0xA0 | 4  | float | `D3DLIGHT9::Falloff` — defaults 0.0. | confirmed |
| +0xA4 | 4  | float | `D3DLIGHT9::Attenuation0` — defaults 0.0. | confirmed |
| +0xA8 | 4  | float | `D3DLIGHT9::Attenuation1` — defaults 0.0. | confirmed |
| +0xAC | 4  | float | `D3DLIGHT9::Attenuation2` — defaults 0.0. | confirmed |
| +0xB0 | 4  | float | `D3DLIGHT9::Theta` — spot inner angle; defaults 0.0. | confirmed |
| +0xB4 | 4  | float | `D3DLIGHT9::Phi` — spot outer angle; defaults 0.0. **D3DLIGHT9 block ends at +0xB7.** The device `SetLight` consumer that reads +0x50 and passes the block to the D3D9 device is downstream of the cull light-list and is `[debugger-confirm]` (see §6). | confirmed (layout); [debugger-confirm] (SetLight consumer path) |

### Table J — `Diamond::GDrawablePair` (12 bytes; GDrawItem sub-class; leaf draw-command)

| Offset | Size | Type | Field / Role | Confidence |
|-------:|-----:|------|--------------|------------|
| +0x00 | 4 | vtable ptr / container word | Polymorphic header. | confirmed |
| +0x04 | 4 | GDrawable* | Drawable pointer (the renderable object). | confirmed |
| +0x08 | 4 | render-state-set* | Render-state-set pointer (18-slot set). | confirmed |

---

## 4. Vtable slot-role maps

Slot 0 in every vtable is the **deleting destructor** (unrefs, destroys, and frees the object).

### 4.1 Base `GNode` virtual interface — slots 0–9

| Slot | Role | Notes |
|-----:|------|-------|
| 0  | Deleting destructor | Unref / destroy / free. |
| 1  | `printDebugInfo(stream, indentPrefix)` | Formats this node to a text stream and recurses into children. `GNode` base prints RTTI type name, node address, "Reference count = " + m_refCount, and "name = " + m_name. |
| 2  | `updateTraverse` — per-frame UPDATE pass | Runs pending bound-refresh (calls slot 7 when `m_flags` bit 0 is set and `m_autoComputeBound` is 1) and clears the traversed-this-frame bit (bit 2). `GGroup` override recurses active children first. |
| 3  | `draw` | No-op at the node level. Drawing is data-driven from the CULL output (`GDrawablePair` records); the per-node virtual draw slot is never the draw path. |
| 4  | `intersectSegment(visitor)` | Line-segment pick / collision: bounding-sphere-vs-segment test, recurse child slot 4, push the hit path onto the visitor's intersect deque. `GTransform` applies its matrix. |
| 5  | `cullApply(cull, doTest)` | Frustum-cull COLLECT pass: sphere-vs-frustum test; push visible self / drawables into the cull's drawable vector; recurse child slot 5. `GNode` base: stub / no-op. |
| 6  | `cullTraverseChildren(cull, doTest)` | Secondary / modelview-stack-aware cull recursion, operating against a separate frustum sub-field of the cull object and maintaining a modelview matrix stack. `GNode` base: pure virtual. |
| 7  | `computeBound` | Recompute this node's bounding sphere from its contents and clear `m_flags` bit 0. Shared base implementation (deep-3d-cartography confirmed): copies the first child's bounding sphere, expands by each subsequent child's sphere (`ExpandBySphere`), and OR-propagates the children's bound-dirty bit into `m_flags`. This body is shared between `GNode` base and `GGroup`; it produces an empty sphere when a node has no children. `GTransform` overrides this slot to first call the base union, then additionally transforms the resulting sphere by `m_localMatrix` when `m_transformActive` (+0xDC) is 0. |
| 8  | `refreshBound` | Top-down bound refresh: recurse slot 8 on each child in the `m_children` GVector (+0x4C) **whose `m_flags` bit 0 is set** (selective recursion into dirty children only), then call self slot 7 to recompute the local sphere. Gated by `m_flags` bit 0 AND `m_autoComputeBound` (+0x34) — skipped entirely when either is clear. (Deep-3d-cartography: selective-child refinement confirmed.) |
| 9  | `setDirty(flagBits)` | OR `flagBits` into `m_flags` (+0x38) and propagate the dirty state upward to every parent node by recursing slot 9 on each parent in the `m_parents` GVector. |

**Reconciliation:** the committed `specs/scene_graph.md` assigns different roles to these slot
indices (slot 1 = accept(Traverser); slot 2 = cull; slot 4 = cullTraverse/computeBound;
slot 5 = dirtyBound; slot 6 = update; slot 7 = setName; slot 8 = getName; slot 9 = addChild).
The content-derived reading above supersedes those labels. `specs/scene_graph.md` must be updated
to match.

### 4.2 `GGroup` extension — slots 10–15 (child management)

| Slot | Role |
|-----:|------|
| 10 | `removeAllChildren` |
| 11 | `addChild` / `insertChild` (increment child refcount + register parent) |
| 12 | `getChild(index)` |
| 13 | `removeChild(node)` |
| 14 | `getMatrix` (group-level accessor) |
| 15 | `getWorldMatrix` (group-level accessor) |

### 4.3 Per-class vtable overrides

**`GGroup`** implements the abstract base slots: slot 1 dumps "GGroup: N children"; slot 2 recurses
active children for the UPDATE pass; slot 4 runs intersect-children; slot 5 is the core frustum-cull
COLLECT (sphere-test + recurse + emit drawables); slot 6 is cull-children; slot 7 is computeBound
(union of children bounds). Adds slots 10–15. Slot 3 inherits the node-level no-op.

**`GScene`** — slots 1, 2, and 6 are thin forwards to the `GGroup` implementations. Otherwise
inherits `GGroup` wholesale; the scene root behaves as a plain group for traversal purposes.

**`GTransform`** — overrides:
- Slot 1: dump prints "GTransform:" followed by the 4×4 local matrix.
- Slot 3: multiplies the passed matrix argument by `m_inverseMatrix` (+0x9C) in place — transforms a
  matrix or point from parent space into this node's local space.
- Slot 4: matrix-aware intersect — if `m_transformActive` (+0xDC) is set (non-zero, bypass mode),
  delegates to `GGroup`; when clear (0), transforms the segment into local space before recursing.
- Slot 5: matrix-push cull — saves the parent modelview, composes `modelview = localMatrix × parent`,
  sets the cull frustum, transforms frustum planes into local space using `m_inverseMatrix`, optionally
  pushes self as a light/state frame, recurses children slot 5, then restores the parent modelview.
  When `m_transformActive` (+0xDC) is set (non-zero, bypass-identity mode), bypasses all matrix work
  and runs the plain group cull path.
- Slot 6: matrix-aware cull-children (maintains modelview stack; operates on the second embedded
  frustum sub-object of the cull object).
- Slot 7: `computeBound` — union of children bounds, then if radius is valid and `m_transformActive`
  (+0xDC) is 0, transforms the resulting sphere by `m_localMatrix` (+0x5C).

**`GSwitch`** — overrides:
- Slot 1: dump prints "GSwitch val =" followed by `m_activeChild`.
- Slot 4: intersect only the selected child (per `m_activeChild` semantics).
- Slot 5: cull only the selected child — value -1 delegates to the full transform/group path (all
  children), value -2 skips the entire subtree, value >= 0 culls exactly that child index.
- Slot 6: similarly restricted cull-children.

**`GGeode`** (leaf) — overrides:
- Slot 1: dump prints "GGeode," prefix.
- Slot 2: leaf-level UPDATE traverse.
- Slot 4: collect geometry hits into the intersect visitor.
- Slot 5: frustum-test the geode, then per-geometry cull-test each drawable and **append visible
  (drawable, render-state) pairs to the cull's drawable vector** — this is the leaf emission step
  of the CULL pass.
- Slot 6: sorted collect variant.
- Slot 7: `computeBound` from the union of geometry bounding boxes.

No child-management slots (10–15) on `GGeode`. Slot 9 on `GGeode` is the shared base `setDirty`
implementation (same body as the GNode base path); `addDrawable` is a **non-virtual** member and is
not present in the vtable. The prior note in this spec stating "Slot 9 is repurposed as addDrawable
on GGeode" was incorrect and is retracted.

**`GViewPlatform`** — cull slots (5 and 6) are no-ops; slot 7 sets an empty bound (radius 0, centre 0,
clears bit 0). A view-platform participates in UPDATE but not in CULL or DRAW — it is a pure camera
carrier. Its `m_autoComputeBound` is seeded to 0 in the constructor (fixed empty bound).

**`GLight`** — provides its own slot 1 (dump) and light-parameter apply slots. `m_autoComputeBound`
seeded to 0; `m_boundRadius` seeded to −1.0 (infinite sphere, so the light is never frustum-culled).
- Slot 6 (`cullTraverseChildren` / apply): performs a sphere-vs-frustum test, then appends the `GLight`
  node onto the cull light-list (pairing the cull context + current modelview with the light node). The
  device `SetLight` call that consumes the appended `D3DLIGHT9` block at +0x50 is downstream of the cull
  light-list consume pass; the light-list consumer was not isolated this pass. [deep-3d-cartography confirmed for slot 6 enqueue; SetLight consumer is [debugger-confirm]]
- Slot 7 (`computeBound`): writes `m_boundCenter` (+0x28/+0x2C/+0x30) = 0, clears `m_flags` bit 0,
  and sets `m_boundRadius` (+0x24) = −1.0 (infinite; always-visible sentinel). Does not union children.
  [confirmed]

---

## 5. Per-frame traversal path

The three traversal passes below run in order each rendered frame. A fourth pass (picking) is
independent of the frame loop.

**Traversal order — all slots (deep-3d-cartography confirmed):** every traversal (UPDATE slot 2,
CULL slots 5/6, PICK slot 4, bound-refresh slot 8, setDirty slot 9) iterates the relevant
GVector (children at +0x4C for group-family passes; parents at +0x3C for setDirty propagation)
from index 0 to count−1 and recursively applies the same slot on each element. The traversal is
strictly **DFS pre-order** with child-index ordering; there is no breadth-first queue or
reordering pass at any traversal stage.

### 5.1 UPDATE pass — slot 2

The frame driver calls the scene root's **slot 2** (`updateTraverse`). The traversal descends the
tree, dispatching each active child's slot 2, and lazily refreshes any dirty bounding spheres.
The slot-2 finalizer applies the following gating logic (deep-3d-cartography confirmed):
- If **both** `m_autoComputeBound` (+0x34) is non-zero **and** `m_flags` bit 0 (bound-dirty) is
  set: call slot 7 (`computeBound`) to recompute the sphere, which clears bit 0.
- If `m_autoComputeBound` is zero and bit 0 is set: clear bit 0 **without** calling slot 7
  (fixed-bound mode — the stored sphere is preserved unchanged).
- Bit 2 (traversed-this-frame) is always cleared by the slot-2 finalizer regardless of the
  bound-dirty state.

### 5.2 CULL pass — slots 5 and 6

The cull pipeline object (see `structs/cull_pipeline.md`) sets the camera frustum and invokes the
scene root's **slot 5** (`cullApply`). The traversal proceeds:

- **`GGroup` / `GScene`**: sphere-test self against the frustum; recurse visible children via slot 5.
- **`GTransform`**: when `m_transformActive` (+0xDC) is 0 (active), push `m_localMatrix` to compose
  the modelview; transform frustum planes into local space via `m_inverseMatrix`; recurse; restore the
  parent modelview. When +0xDC is non-zero (bypass mode), runs the plain group path with no matrix work.
- **`GSwitch`**: restrict the cull recursion to the child(ren) indicated by `m_activeChild`.
- **`GGeode`**: frustum-test the geode; for each drawable in `m_geometries` that passes its own
  cull test, **append the (drawable, render-state) element** into the cull object's drawable vector.
  This is the collect step that populates the data-driven DRAW queue.

**Slot 5 vs slot 6 division (wave-11, resolved):** slot 5 operates on the cull object's primary
(current) frustum field and emits drawables; slot 6 operates on a separate embedded frustum
sub-object and maintains a modelview matrix stack (push/pop). This division was confirmed from the
function bodies of the respective slot implementations.

### 5.3 DRAW pass — data-driven from GDrawablePair records

The view's render-pass callbacks consume the collected `GDrawablePair` records. Each visible element
carries a `GDrawable*` and a render-state-set pointer; the pipeline applies the render state to the
D3D device and submits the geometry's vertex and index buffers (`DrawIndexedPrimitive`). The
node-level draw slot (slot 3) is **not** invoked during the draw pass — drawing is fully data-driven
from the cull output. See `structs/cull_pipeline.md §5` for the draw-flush sequence.

### 5.4 PICK traversal — slot 4 (independent of the frame loop)

**Slot 4** (`intersectSegment`) runs an independent line-segment traversal: bounding-sphere-vs-segment
test at each node; matrix-aware in `GTransform` (segment transformed into local space when
`m_transformActive` is 0); selection-aware in `GSwitch` (respects `m_activeChild`); leaf hits
recorded into an intersect deque on the visitor. This traversal is separate from the per-frame
UPDATE/CULL/DRAW loop.

---

## 6. Open items and pending confirmation

**Items closed in wave-11 static deep-dive:**
- `GNode` +0x08..+0x23 closed as `std::string m_name` (28-byte MSVC SSO string — see Table B).
- `GNode` +0x34 closed and renamed `m_autoComputeBound` (0/1 gate for bound recomputation; was incorrectly labelled `m_nodeMask` / traversal mask — see Table B).
- Bounding-sphere field order corrected: radius at +0x24, centre at +0x28/+0x2C/+0x30 (was listed inverted — see Table B and its correction note).
- `GTransform` slot 3 closed: multiplies passed matrix by `m_inverseMatrix` in place (local-space transform).
- `GTransform` +0xDC polarity corrected: 1 = bypass/identity, 0 = apply matrix (was inverted in prior edition — see Table E).
- Slot 5 vs slot 6 division resolved: slot 5 = current-frustum collect + drawable emit; slot 6 = modelview-stack push/pop over a second frustum field.
- `GScene` +0x5C corrected to default render-state set (17 objects, engine-vector ABI); "cameras @ +0x5C" label retracted.
- `GGeode` slot 9 corrected: shared base `setDirty`, not `addDrawable` (which is non-virtual).
- `GLight` +0x54 tightened to exactly 48 bytes / 12 floats; +0x84 tightened to exactly 52 bytes / 13 dwords.

**Remaining open / pending confirmation:**

- **`GNode` +0x38 bits 1, 3..7** — `setDirty` (slot 9) OR-accumulates arbitrary bit masks and
  propagates upward; bit 0 (bound-dirty) and bit 2 (traversed-this-frame) are confirmed. Remaining
  bits are set by callers with constant masks — enumerable statically by sweeping all `setDirty`
  call sites, but not completed this pass. Recommended: live watchpoint on +0x38 across a frame to
  capture which bits toggle in UPDATE vs CULL vs picking. [debugger-confirm]
- **`GScene` +0x6C vector element type** — confirmed to be a standard 12-byte `std::vector`, separate
  from the +0x5C render-state set and zeroed in the constructor. Element type (GCamera*, GLight*, or
  other) was not isolated this pass. Recommended: break after scene population, read +0x6C..+0x74,
  classify the first element's vtable. [debugger-confirm]
- **`GTransform` +0xDC setter path** — default 1 (bypass) confirmed; all three cull/intersect/computeBound
  readers confirm that 0 triggers matrix application. The public `setMatrix` that clears +0xDC to 0
  was not isolated this pass. [debugger-confirm]
- **`GLight` +0x54 / +0x84 sub-field grouping** — byte sizes are exact (48 and 52 bytes respectively)
  and defaults are confirmed (all 1.0 / all 0). Internal channel boundaries (ambient/diffuse/specular RGBA
  vs position/direction/attenuation/spot) require a dedicated light-apply slot pass. [static, tightened]
- **Engine-vector vs GVector reconciliation** — the `GScene` render-state container at +0x5C uses a
  16-byte `{reserved, begin*, end*, cap*}` ABI; the §2 GVector uses `{count, capacity, stride, data*}`.
  These share the same byte width but have different field semantics. It is not yet determined whether
  they are the same struct type reinterpreted by different accessors, or genuinely distinct engine
  types. A cross-reference of the accessor functions is needed. [open]
- **GNode abstract / vtable scan** — [RESOLVED by deep-3d-cartography pass.] The `GNode` base vtable
  is confirmed present and is transiently installed by the shared base-init path; it is immediately
  overwritten by every derived class constructor. `GNode`'s complete-object-locator is confirmed not
  referenced by any concrete derived vtable in the binary's read-only data, establishing that `GNode`
  is never instantiated as a standalone object. Slot 7 has a confirmed shared base implementation
  (unions children bounds). Slot 6 base body for `GNode` is not yet isolated (concrete classes each
  provide their own). [low-priority open item for slot 6 only]

---

## 7. Cross-references

- Per-view render object (camera-carrier field at GViewPlatform +0x50; view cull objects): `structs/gview.md`.
- Cull pipeline object layout and the DRAW-flush sequence: `structs/cull_pipeline.md`.
- Scene-graph slot-label reconciliation target: `specs/scene_graph.md` (to be updated against this spec).
- Render-pass frame sequence and D3D device wrapper: `specs/render_pipeline.md`.
- Terrain streaming and cell management: `specs/terrain-streaming.md`.
- Runtime singletons (global scene root, frame driver): `structs/runtime_singletons.md`.
- Glossary and canonical names: `Docs/RE/names.yaml`.
- Provenance audit trail: `Docs/RE/journal.md`.
