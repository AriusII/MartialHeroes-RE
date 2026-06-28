# Spec: Diamond Engine — Scene-Graph Class Hierarchy

> Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO
> binary addresses, NO decompiler-generated identifiers. Promoted from dirty-room
> static analysis under EU Software Directive 2009/24/EC Art. 6, solely to
> achieve interoperability.

> **Verification banner**
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **analysis_date:** 2026-06-27
> - **method:** RTTI scan + vtable slot dump + constructor cross-reference trace
> - **evidence:** [static-ida]
> - **readiness:** DRAFT — hierarchy and slot tables recovered; object sizes are
>   lower-bound estimates pending confirmation.

---

## 1. Overview

The Diamond engine uses an OpenSceneGraph-style scene graph. Class names were
recovered from MSVC RTTI type-info blocks embedded in the binary. Each class
has a corresponding vtable and a set of constructors identified via vtable-write
cross-reference tracing.

### 1.1 Confirmed class hierarchy

```
Diamond::GNode                       base node
├── Diamond::GGroup                  container: holds N child GNode pointers
│   ├── Diamond::GScene              scene root; overrides cull + update
│   │   └── Diamond::GView           viewport / render-target attachment
│   └── Diamond::GViewPlatform       platform-specific view wrapper
├── Diamond::GGeode                  leaf geometry container; holds GGeometry refs
│   └── Diamond::GGeometry           explicit vertex/index-buffer geometry
├── Diamond::GCamera                 view-point; carries projection matrix
├── Diamond::GLight                  D3D9 light-source attachment
├── Diamond::GPipeline               render-state override node (abstract)
└── Diamond::GParticleBuffer         GPU particle quad-batch
```

`Diamond::GDrawablePair` is confirmed by RTTI but carries a four-slot vtable
inconsistent with a GNode-derived class; its position in the hierarchy is
pending additional RE.

### 1.2 Additional RTTI symbols observed (hierarchy placement pending)

The following class names appear in type-info strings without a confirmed
parent–child relationship:

- `Diamond::GDrawable`
- `Diamond::GTraverser`
- `Diamond::GRenderState`
- `Diamond::GRSCullMode`
- `Diamond::GCull`
- `Diamond::GCullPipeline`
- `Diamond::GStatsCull`

---

## 2. Vtable Slot Conventions

MSVC places the scalar-deleting destructor in slot 0 of every polymorphic
class. Comparison across base and derived vtables establishes the following
canonical slot roles:

| Slot | Offset | Canonical role |
|------|--------|----------------|
| 0 | +0 | destructor (scalar-deleting) |
| 1 | +4 | `accept` — visitor/traverse dispatch |
| 2 | +8 | `cull` — frustum-cull pass |
| 3 | +12 | `draw` — direct draw call |
| 4 | +16 | `computeBound` — bounding-volume query |
| 5 | +20 | `dirtyBound` — invalidate cached bound |
| 6 | +24 | `update` — per-frame state update |
| 7 | +28 | `setName` |
| 8 | +32 | `getName` |
| 9 | +36 | child/parent-list accessor (role uncertain — shared across GGroup and GGeode) |
| 10–15 | +40–+60 | class-specific methods (see per-class tables below) |

Slots marked **pure virtual** have no concrete implementation in that class and
must be overridden by a concrete subclass. Slots marked **stub** are no-ops
(empty body) in that class.

---

## 3. Per-Class Vtable Layouts

### 3.1 Diamond::GDrawablePair (4 slots)

Holds a pair of references: one GGeometry-like object and one GNode-like object.
The slot-1 body passes both references to visitor/cull helpers before dispatching
to the child node's draw slot.

| Slot | Offset | Role |
|------|--------|------|
| 0 | +0 | destructor |
| 1 | +4 | accept/traverse — dispatches to both held references |
| 2 | +8 | cull — culls both held references; increments cull-time counter |
| 3 | +12 | draw — returns the geometry reference |

### 3.2 Diamond::GGroup (16 slots)

The standard container node. Maintains a dynamic child list (GVector of GNode
pointers). The cull slot iterates children and calls their cull slot if the
child's visibility flag is set; it then delegates to a shared bounding-volume
cull helper. The draw slot is a no-op (groups do not draw directly).

| Slot | Offset | Canonical method / role |
|------|--------|------------------------|
| 0 | +0 | destructor — calls GGroup::ctor (cleanup path) |
| 1 | +4 | `GGroup::accept` — traverses each child recursively |
| 2 | +8 | cull — iterates child list, culls visible children |
| 3 | +12 | draw — stub (no-op) |
| 4 | +16 | `GGroup::computeBound` — aggregates child bounds |
| 5 | +20 | `GGroup::dirtyBound` |
| 6 | +24 | `GGroup::update` |
| 7 | +28 | `GGroup::setName` |
| 8 | +32 | `GGroup::getName` |
| 9 | +36 | child/parent-list accessor (shared with GGeode; exact role pending) |
| 10 | +40 | `GGroup::removeAllChildren` |
| 11 | +44 | `GGroup::addChild` |
| 12 | +48 | `GGroup::VFunc_12` (inferred: getNumChildren) |
| 13 | +52 | `GGroup::removeChild` |
| 14 | +56 | `GGroup::VFunc_14` (inferred: getMatrix) |
| 15 | +60 | `GGroup::VFunc_15` (inferred: getWorldMatrix) |

### 3.3 Diamond::GGeode (10 slots observed)

A leaf node that owns a list of GGeometry drawables (not GNode children). The
accept slot iterates the drawable list and prints each drawable's parent-geode
and name to a debug stream. The draw slot is a no-op (drawing is dispatched
through GDrawablePair or the render list, not directly).

| Slot | Offset | Canonical method / role |
|------|--------|------------------------|
| 0 | +0 | destructor — calls GGeode::ctor (cleanup path) |
| 1 | +4 | `GGeode::accept` — iterates drawable list, prints debug info |
| 2 | +8 | cull override |
| 3 | +12 | draw — stub (no-op, shared with GGroup) |
| 4 | +16 | `GGeode::computeBound` |
| 5 | +20 | `GGeode::dirtyBound` |
| 6 | +24 | `GGeode::update` |
| 7 | +28 | `GGeode::setName` |
| 8 | +32 | `GGeode::getName` |
| 9 | +36 | child/parent-list accessor (shared implementation with GGroup slot 9) |

> Note: only 10 slots observed in the vtable dump; slots 10–15 may be inherited
> from GNode without override, or may not exist for GGeode.

### 3.4 Diamond::GGeometry (16 slots)

The concrete vertex/index-buffer geometry class. Slots 3, 4, 6, 7, 8, and 13
are pure virtual — GGeometry is an abstract base; concrete rendering classes
provide the draw implementation. The cull slot (slot 2) is implemented and
returns a pointer to the geometry's cached-bound field at offset +36.

| Slot | Offset | Canonical method / role |
|------|--------|------------------------|
| 0 | +0 | destructor |
| 1 | +4 | `GGeometry::accept` (traverse) |
| 2 | +8 | `GGeometry::VFunc_02` — cull; returns pointer to bound at object+36 |
| 3 | +12 | draw — **pure virtual** |
| 4 | +16 | computeBound — **pure virtual** |
| 5 | +20 | dirtyBound — override; returns constant true |
| 6 | +24 | update — **pure virtual** |
| 7 | +28 | setName — **pure virtual** |
| 8 | +32 | getName — **pure virtual** |
| 9 | +36 | override |
| 10 | +40 | override |
| 11 | +44 | stub (no-op) |
| 12 | +48 | stub (no-op) |
| 13 | +52 | setMatrix — **pure virtual** |
| 14 | +56 | `GGeometry::VFunc_14` |
| 15 | +60 | `GGeometry::VFunc_15` |

### 3.5 Diamond::GParticleBuffer (1 slot observed)

Carries quad-batch GPU particle data (pre-allocated vertex and index buffers).
The vtable scan recovered only one slot; the class likely has a minimal vtable
(destructor only at the RTTI-observed entry point) and uses non-virtual methods
for fill/draw operations (`GParticleBuffer::fillStaticIndices`,
`GParticleBuffer::appendQuadBatch`, `GParticleBuffer::draw`,
`GParticleBuffer::drawLock`).

| Slot | Offset | Role |
|------|--------|------|
| 0 | +0 | destructor — `GParticleBuffer` destructor |

### 3.6 Diamond::GPipeline (6 slots)

A render-state override node. All non-destructor slots are pure virtual, making
GPipeline an abstract interface. Concrete subclasses provide the actual D3D9
render-state application.

| Slot | Offset | Role |
|------|--------|------|
| 0 | +0 | destructor |
| 1 | +4 | accept/traverse — **pure virtual** |
| 2 | +8 | cull — **pure virtual** |
| 3 | +12 | draw — **pure virtual** |
| 4 | +16 | computeBound — **pure virtual** |
| 5 | +20 | dirtyBound — **pure virtual** |

### 3.7 Diamond::GView (1 slot observed)

The vtable scan recovered only the destructor slot for GView. GView likely
inherits the full GGroup slot table and overrides only the destructor.

| Slot | Offset | Role |
|------|--------|------|
| 0 | +0 | destructor — `GView` destructor |

### 3.8 Diamond::GScene (16 slots)

Inherits GGroup's full 16-slot layout. Slot 1 (accept) is implemented as a
direct thunk to `GGroup::accept` — GScene does not override traverse. Slots 2
(cull) and 6 (update) have GScene-specific implementations. All other slots are
shared with GGroup.

| Slot | Offset | Canonical method / role |
|------|--------|------------------------|
| 0 | +0 | destructor — GScene destructor |
| 1 | +4 | accept — thunk to `GGroup::accept` (not overridden) |
| 2 | +8 | cull — **GScene override** |
| 3 | +12 | draw — stub (no-op, shared with GGroup) |
| 4 | +16 | computeBound — shared with GGroup |
| 5 | +20 | dirtyBound — shared with GGroup |
| 6 | +24 | update — **GScene override** |
| 7 | +28 | setName — shared with GGroup |
| 8 | +32 | getName — shared with GGroup |
| 9 | +36 | shared with GGroup slot 9 |
| 10 | +40 | `GGroup::removeAllChildren` (shared) |
| 11 | +44 | `GGroup::addChild` (shared) |
| 12 | +48 | `GGroup::VFunc_12` (shared) |
| 13 | +52 | `GGroup::removeChild` (shared) |
| 14 | +56 | `GGroup::VFunc_14` (shared) |
| 15 | +60 | `GGroup::VFunc_15` (shared) |

### 3.9 Diamond::GViewPlatform (10 slots)

A platform-specific view wrapper that mirrors GGeode's 10-slot layout. Slots 5
(dirtyBound) and 6 (update) are no-ops. Slot 2 (cull) is shared with GGroup's
bounding-volume cull helper. Large allocation sites (≥ 108 bytes per call site)
suggest a substantial data payload beyond the vtable.

| Slot | Offset | Canonical method / role |
|------|--------|------------------------|
| 0 | +0 | destructor |
| 1 | +4 | accept/traverse — GViewPlatform override |
| 2 | +8 | cull — shared bounding-volume cull helper |
| 3 | +12 | draw — stub (no-op) |
| 4 | +16 | computeBound override |
| 5 | +20 | dirtyBound — stub (no-op) |
| 6 | +24 | update — stub (no-op) |
| 7 | +28 | setName override |
| 8 | +32 | getName override |
| 9 | +36 | shared accessor (same implementation as GGroup slot 9) |

### 3.10 Diamond::GCamera (3 slots observed)

| Slot | Offset | Canonical method / role |
|------|--------|------------------------|
| 0 | +0 | destructor — GCamera destructor |
| 1 | +4 | `GCamera::accept` (traverse) |
| 2 | +8 | cull — **pure virtual** |

> Note: only 3 slots observed; the full vtable may extend further. GCamera's
> constructor writes the vtable pointer after initialising projection state.
> Allocation call sites indicate ≥ 6 dword-width fields beyond the vtable.

---

## 4. Object Size Estimates

Sizes below are lower bounds derived from constructor analysis (highest
member-access offset observed, or allocation size from `operator new` call
sites). All values are in bytes. Confirmed exact sizes require additional RE.

| Class | Size lower bound | Basis |
|-------|-----------------|-------|
| GDrawablePair | 12 bytes | `operator new(12)` call in constructor |
| GGroup | ≥ 80 bytes | highest member field at dword-index 19 (≥ 76 bytes); child list follows |
| GGeode | ≥ 80 bytes | same layout as GGroup (holds drawable list at same index) |
| GGeometry | ≥ 88 bytes | highest byte-offset member access at +84 in constructor |
| GParticleBuffer | ≥ 248 bytes | highest byte-offset member access at +244 in full constructor |
| GView | ≥ 296 bytes | highest member offset 292 (byte) in constructor |
| GViewPlatform | ≥ 108 bytes | `operator new(108)` at primary call sites |
| GCamera | ≥ 48 bytes | 6 dword-width alloc-size candidate |
| GScene | ≥ 104 bytes | highest dword-index 25 in constructor (≥ 100 bytes) |
| GLight | TBD | constructor identified; size not yet extracted |
| GPipeline | TBD | constructor identified; size not yet extracted |

---

## 5. Traversal / Cull / Draw Pattern

The scene graph uses a visitor pattern:

1. A traversal root (typically a `GScene` node) initiates a cull pass by
   calling its `cull` slot.
2. `GGroup::cull` iterates the child list. For each child whose visibility flag
   is set, it calls the child's `cull` slot. It then invokes the shared
   bounding-volume cull helper to test the group's aggregate bound against the
   current frustum.
3. The cull pass builds an ordered per-frame render list.
4. The render list is iterated for `draw` calls. Direct draw calls ultimately
   reach `IDirect3DDevice9::DrawIndexedPrimitive` or `DrawPrimitive` (see
   `Docs/RE/specs/rendering.md §5`).
5. `GParticleBuffer` bypasses the scene-graph draw slot; its
   `GParticleBuffer::draw` and `GParticleBuffer::drawLock` methods are called
   directly by the particle emitter system.

GGroup and GGeode nodes never draw themselves — their draw slots are stubs.
Drawing is the responsibility of concrete GGeometry subclasses or
GParticleBuffer.

---

## 6. Relationship to Rendering Pipeline

The scene graph is the primary organiser of the per-frame draw loop described
in `Docs/RE/specs/rendering.md`. The `GScene::cull` override builds the ordered
draw list that feeds into the bucket ordering described in
`Docs/RE/specs/rendering.md §3`.

`GPipeline` nodes carry D3D9 render-state overrides (blend mode, Z-write, etc.)
that the render-state cache described in `Docs/RE/specs/rendering.md §4.1`
applies.

The `GCamera` node supplies the view and projection matrices consumed by the
transform stack during the cull and draw passes.

---

## 7. Known Constructor Chains

- `GGroup::ctor` calls `GNode::ctor` after initialising the child list.
- `GGeode::ctor` calls `GNode::ctor` after initialising the drawable list.
- `GScene::ctor` is a direct constructor (single vtable write observed).
- `GView::ctor` is a direct constructor.
- `GCamera::ctor` initialises projection state before writing the vtable.
- `GParticleBuffer` has two constructors: a minimal form and a full form
  (`GParticleBuffer::ctor_full`) that pre-allocates GPU buffers and initialises
  sprite-size and index state. The full form also calls
  `GParticleBuffer::fillStaticIndices`.

---

## 8. Open Questions (escalate to RE domain)

- Exact hierarchy placement of `GDrawablePair` (is it a GNode subclass?).
- Exact role of vtable slot 9 (shared between GGroup and GGeode — `addChild`,
  `addParent`, or a reference-count helper?).
- Full vtable slot counts for GView, GCamera, and GLight.
- GGeometry concrete subclass(es) that provide the `draw` implementation.
- Confirmed byte sizes for GLight, GPipeline, and GCamera.
- Hierarchy and vtable layout of GTraverser, GRenderState, GCull, and related
  helper classes observed in RTTI strings.
