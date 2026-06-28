---
verification: confirmed (static, IDB SHA f61f66a9, CYCLE 14 (2026-06-28))
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [render_pipeline, scene_graph]
conflicts: none outstanding — cull_pipeline.md Table F +76/+80 reconciliation resolved by wave-11 deep-dive (see §8)
correction: 2026-06-28 — D3D8/IDirect3DDevice8 labels in the wave-4 pass were wrong; corrected throughout to D3D9/IDirect3DDevice9. Vtable slot numbers were already the D3D9 values; no slot renumbering needed. Verdict source: Docs/RE/_dirty/d3d-version/verdict.md (static-confirmed, three independent pillars).
deepening: wave-11 deep-dive (2026-06-28) — closed GObject+4 (reference count), +76/+80 widths and roles, GVector word order, GPU-mesh resource 11-field table, SwordLightEffect trail-record inner layout (FVF 322), per-class DrawIndexedPrimitive(UP) arg sources incl. BaseVertexIndex correction, full D3D9 wrapper slot table; two items remain [debugger-confirm] (see §8). deep-3d-cartography static pass (2026-06-29) — no new facts for this spec; §8 [debugger-confirm] items unchanged (SetLight overlap for leaf objects at +76/+80; in-world slot-4 dispatch chain).
---

# Structs: Drawable and Geometry Leaf Objects (Diamond render engine)

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. All offsets are byte offsets relative to the start of the respective class
> sub-object — they are struct/layout offsets, NOT memory addresses, and must never be treated
> as such.
> Citing engineers: `// spec: Docs/RE/structs/drawable_geometry.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from constructor, destructor, vtable, and
> draw control-flow, corroborated across multiple call sites; `[hypothesis]` = inferred from
> zero-initialisation or seen only once, not independently re-isolated; `[debugger-pending]` =
> flagged for live `?ext=dbg` session confirmation (never `dbg_start`).

---

## 1. Overview

The renderable leaf family implements an SGI Cosmo3D-style scene-graph hierarchy, retargeted to the
`Diamond::` namespace with a Direct3D 9 backend. The inheritance chain is:

```
Diamond::GObject              (polymorphic base; vtable + name field; ~36 bytes)
  └─ Diamond::GDrawable       (abstract drawable; AABB bound + render-state vector + parent back-list)
       └─ Diamond::GGeometry  (abstract geometry; extends vtable to 16 slots; draw virtuals remain pure)
            ├─ StaticSkin           (concrete; system-memory vertex/index arrays; DrawIndexedPrimitiveUP)
            ├─ Skin                 (concrete; D3D vertex/index buffers; cel-shade tint path; main skinned character)
            └─ SwordLightEffect     (concrete; dynamic trail geometry)
```

`GDrawable` and `GGeometry` are both abstract — their draw-related vtable slots are pure virtual.
All vertex/index buffers, FVF values, vertex stride, primitive type, material handles, and
`DrawIndexedPrimitive` invocations reside exclusively in the concrete leaf classes.

The per-drawable draw entry is **vtable slot 4** (byte offset +16 within the vtable pointer array).
It is not invoked by the object's own internal dispatch; the cull draw-walk (see
`structs/cull_pipeline.md §5`) drives the device-wrapper `render-drawable` path, which in turn
calls slot 4 on each drawable. [confirmed]

The backend is **Direct3D 9**. The global `GRenderDevice` wrapper singleton holds the raw
`IDirect3DDevice9*` at a known internal byte offset; it is reached via static-confirmed wrapper
thunks that map one-to-one to canonical `IDirect3DDevice9` vtable slot indices (see §7). The D3D
version is unambiguous from these thunk indices. [confirmed]

---

## 2. Class identity

All five classes in this hierarchy are confirmed polymorphic via RTTI (type-descriptor strings under
the `Diamond::` namespace). The confirmed class names are: `Diamond::GObject`, `Diamond::GNode`,
`Diamond::GDrawable`, `Diamond::GGeometry`, and `Diamond::GGeode`. The cull bin-entry records
(`GDrawablePair`, `GDrawItem`, `GRangeObject`) are covered separately in
`structs/cull_pipeline.md` (Table E).

`GDrawable` does not have a standalone vtable separate from `GGeometry` in the compiled binary — it
is instantiated only as `GGeometry`'s base sub-object. Its base vtable (6 slots) is restored by the
geometry destructor immediately before the `GObject` destructor runs. [confirmed]

`GGeometry`'s vtable (16 slots) is installed by the `GGeometry` constructor and reinstalled at the
top of the `GGeometry` destructor. It resides in the read-only data segment adjacent to the literal
string `"    geometry "`. [confirmed]

Concrete class vtables are installed after chaining `GGeometry::ctor`:

- `StaticSkin` — installs its own vtable overriding slots 0, 3, 4, 6, 7, 8, 11, 12, and 13.
- `Skin` — same override set; additionally appends a slot 16.
- `SwordLightEffect` — same override set; additionally appends a slot 16.

[confirmed]

---

## 3. Ownership and parent linkage

A `GDrawable` (and any concrete leaf) keeps a **back-list of parent `GGeode` nodes** in a vector
field at +84. The destructor walks this vector and calls `GGroup::UnlinkChild(parentGeode, this)`
for each entry, allowing a drawable to be shared by (referenced from) multiple geodes — i.e.,
instancing is supported at the scene-graph level. [confirmed]

`GGeode` (10-slot vtable, confirmed via RTTI) is the owning scene-graph leaf-group. Its vtable
slot 5 collects drawables for the `GCull` opaque phase; slot 6 collects sorted (transparent or
distance-sorted) drawables. [confirmed]

---

## 4. Field offset tables

### Table A — `Diamond::GObject` (base class; ~36 bytes total)

| Byte offset | Size | Type | Field | Confidence |
|------------:|-----:|------|-------|------------|
| +0  | 4  | vtable ptr | Polymorphic dispatch pointer. The `GObject` base vtable carries 2 slots: [0] deleting destructor, [1] print-debug-info. | confirmed |
| +4  | 4  | u32 | **Reference count.** The unref routine asserts `ref_count != 0` (source line: dGObject.h:58) then decrements this word. The constructor zeroes it. | confirmed |
| +8  | 28 | std::string | Object name. VC7 `basic_string` layout: 16-byte SSO buffer + size word + capacity word. Constructed empty. | confirmed |

### Table B — `Diamond::GDrawable` (abstract; extends GObject; base region spans +0..~+96)

| Byte offset | Size | Type | Field | Confidence |
|------------:|-----:|------|-------|------------|
| +0  | 4  | vtable ptr | Overwritten by derived constructors: first to the 6-slot `GDrawable` base vtable, then to the 16-slot `GGeometry` vtable or a concrete leaf vtable. | confirmed |
| +4  | 4  | u32 | GObject reference count (see Table A). | confirmed |
| +8  | 28 | std::string | GObject name (see Table A). | confirmed |
| +36 | 12 | float[3] | **Bounding-box minimum** (XYZ). Written by the update-bound virtual; returned by the `getBound` override after a bound-update. Reset to a sentinel value by the constructor. | confirmed |
| +48 | 12 | float[3] | **Bounding-box maximum** (XYZ). Written alongside the minimum by the same update-bound virtual. | confirmed |
| +60 | 16 | GVector | **Render-state / GeoState vector.** GVector layout (wave-11 confirmed): word0 (+60) = live element count; word1 (+64) = reserve/initial-size; word2 (+68) = capacity (default 16); word3 (+72) = data buffer pointer (`malloc(4×count)`). The destructor walks and frees each element, then frees the backing buffer at +72. The `GGeometry` constructor pushes one default 24-byte state element (heap-allocated). Reported by the print-debug slot as "Number of states". | confirmed |
| +76 | 4  | u32 | **Render-enable / blend flag (DWORD).** Constructor writes 1. Roles: (a) bound-update enable gate — slots 10 and 15 gate the dirty-check on `*(u32*)(this+76) != 0`; (b) light-enable gate — the cull draw-walk calls `ApplyDrawableLight` only when this field is non-zero; (c) blend-path selector — the cull draw-walk emits the blended path when `*(u8*)(this+76) == 1` (low byte). Corrects `cull_pipeline.md` Table F which described this as a 1-byte field; it is 4 bytes and the low byte carries the value described there. | confirmed |
| +80 | 1  | u8 | **Bound-dirty flag (bit 0).** Constructor sets to 1. Cleared by the update-bound routine (slot 13 prologue) and by vtable slot 14. Tested by slots 10 and 15. The cull draw-walk also passes `&(drawable+80)` as the `D3DLIGHT9*` argument to `SetLight` when `+76` is non-zero — meaning bytes +80 onward serve as an in-place light descriptor for cull-drawn scene objects. Whether Skin/StaticSkin leaves (≈ 132/156 bytes total) are reached by this path or bypass it via `+76==0` requires debugger confirmation. [debugger-confirm] | confirmed (width + dirty role); [debugger-confirm] (SetLight overlap for leaf objects) |
| +81 | 3  | — | Alignment padding (between the 1-byte dirty flag and the next GVector). | — |
| +84 | 16 | GVector | **Parent-geode back-pointer vector** (element type: `GGeode*`). GVector word order same as +60: count@+84, reserve@+88, capacity@+92, data@+96. Destructor iterates this vector and calls `GGroup::UnlinkChild(parent, this)` for each entry. | confirmed |
| +96 | 4  | ptr | Data-buffer pointer of the parent-geode GVector above (GVector word3 = +96). Also freed explicitly in the destructor as an owned heap buffer. Its producer (who populates the parent-geode list) is not in the constructor/draw path — if this is purely the GVector backing, no second distinct buffer exists at this offset. [hypothesis — confirm no second buffer] | hypothesis |

### Table C — `Diamond::GGeometry` (abstract; extends GDrawable vtable to 16 slots; no new data fields isolated)

`GGeometry` extends the inherited vtable from 6 to 16 slots but introduces no new data members of
its own beyond what the base constructor writes (it constructs the +60 state vector's default element
and sets +76/+80). Vtable slots 3, 4, and 13 remain pure virtual. Concrete leaf data (buffers,
material handles, draw ranges) begins at or after the GGeometry/GDrawable base region (approximately
+96..+104). [confirmed]

### Table D — `StaticSkin` (concrete; DrawIndexedPrimitiveUP path)

| Byte offset | Size | Type | Field | Confidence |
|------------:|-----:|------|-------|------------|
| +0..+95 | — | GGeometry/GDrawable base | As Tables A/B. | confirmed |
| +100 | 4 | int | Zero-initialised. Purpose not isolated. | hypothesis |
| +104 | 4 | int | Constructor writes −1 (sentinel). Read at draw as an owner/actor reference: `if (*(this+104) > −1 && time % 1000 < 50)` swaps the default texture for a tint-flicker effect. Dual role: sentinel owner index AND tint-flicker actor ref. | confirmed |
| +108 | 4 | ptr | **Material / render-state object.** The texture name string pointer bound to stage 0 is at internal offset +52 within this object. The draw path gates execution on this field being non-null. | confirmed |
| +112 | 4 | ptr | **Mesh-data source object.** NOT initialised by the constructor (left zero); populated by the build/attach path. Vtable slot 13 reads `*(*(this+112) + 1064)` as a vec3 center, writing it to both AABB min (+36) and max (+48) as a degenerate point-bound. | confirmed |
| +116..+136 | 24 | int[6] | Zero-initialised scratch region (flags or counters). | hypothesis |
| +140 | 4 | float | Default 1.0 (scale or alpha default). | confirmed |
| +144 | 4 | float | Default 1.0 (scale or alpha default). | confirmed |
| +148 | 4 | ptr | **Draw-range / mesh descriptor** (see §4a below). NumVertices at internal offset +40; PrimitiveCount at +44; system-memory index array at +52. | confirmed |
| +152 | 4 | ptr | **pVertexStreamZeroData** — system-memory vertex array pointer passed directly to `DrawIndexedPrimitiveUP`. Likely equals the transformed-output vertex array pointer at mesh-descriptor internal +56. | confirmed |

**Draw sequence (vtable slot 4):** `SetFVF`(274) → bind stage-0 texture by name from
`*(material+52)` → optional default-texture flicker (actor/timing check via `*(this+104)`) →
`DrawIndexedPrimitiveUP(D3DPT_TRIANGLELIST, MinVertexIndex=0, NumVertices=*(mesh+40),
PrimitiveCount=*(mesh+44), pIndexData=*(mesh+52), IndexDataFormat=D3DFMT_INDEX16,
pVertexStreamZeroData=*(this+152), VertexStreamZeroStride=32)`. [confirmed]

#### Table D.a — StaticSkin mesh-descriptor internal layout (object at `*(this+148)`)

Recovered from the AABB/budget recompute routine and the slot-4 draw body.

| Internal offset | Type | Field |
|---:|---|---|
| +4  | float[3] | Translation vector added to each source vertex during transform. |
| +28 | u32 | LOD/budget tag; set to 200 by the budget-recompute routine. |
| +40 | u32 | **NumVertices** |
| +44 | u32 | **PrimitiveCount** |
| +48 | ptr | Source (model-space) vertex array. |
| +52 | ptr | **Index array** (`pIndexData`). |
| +56 | ptr | Transformed/output vertex array (32-byte stride). |
| +60 | float[3] | AABB minimum (XYZ). |
| +72 | float[3] | AABB maximum (XYZ). |
| +88 | u32 | Byte size of the output vertex region = `32 × NumVertices`. |
| +104 | u32 | Render-budget bucket: one of {90000, 250000, 1000000, 2250000, 3240000}, assigned by AABB extent. |

[confirmed — from static analysis of the recompute and draw routines]

### Table E — `Skin` (concrete; real D3D VB/IB path; main skinned character)

| Byte offset | Size | Type | Field | Confidence |
|------------:|-----:|------|-------|------------|
| +0..+95 | — | GGeometry/GDrawable base | As Tables A/B. | confirmed |
| +100 | 4 | int | Zero-initialised. Purpose not isolated. | hypothesis |
| +104 | 4 | ptr | **GPU-mesh / SkinSet resource object** (see §4b below). Holds the D3D vertex buffer at internal offset +1264, D3D index buffer at +1268, lock-gate flag at +1272, tint-state selector (one-hot 0..8) at +1280, deform mode at +1768 (0=LBS, 1=rigid-major, 2=rigid-owned), and per-draw render flag at +1836. This resource is the GPU-side upload target for `.bud`/`.skn` mesh data and is NOT freed by the Skin destructor (cache-owned). | confirmed |
| +108 | 4 | ptr | **Draw-range descriptor.** NumVertices at internal offset +44; PrimitiveCount at +48. | confirmed |
| +112 | 4 | ptr | **Material / render-state object.** Texture name string pointer at internal offset +52 (stage 0). Material refcount is at `(material+52)+44`; destructor decrements it. | confirmed |
| +116 | 4 | ptr | **CPU deform-scratch vertex buffer** (32-byte vertices). Owned by Skin; freed in the destructor. The deform+upload routine copies from this buffer into the locked VB at byte offset `32 × *(this+120)`. | confirmed |
| +120 | 4 | u32 | **BaseVertexIndex** — the vertex offset within the VB, passed as the `BaseVertexIndex` argument to `DrawIndexedPrimitive`. *Correction from prior pass:* was labelled `MinVertexIndex`; the D3D9 ABI argument order confirms this is `BaseVertexIndex` (MinVertexIndex is literal 0). Used by the deform routine as a byte offset (`32 × BaseVertexIndex`) into the locked VB. | confirmed |
| +124 | 2 | u16 | **StartIndex** — passed directly as the `StartIndex` argument to `DrawIndexedPrimitive`. Constructor initialises to 0. | confirmed |
| +128 | 4 | float | Default 1.0 (scale or alpha default). | confirmed |

**Draw sequence, cel-shaded tint path (vtable slot 4):** bind stage-0 texture from
`*(material+52)`; if the `GRenderDevice` wrapper reports VS capability:

1. Compute World × View × Projection matrix; transpose; upload to VS constant register c0 (4 rows).
2. Read tint-state selector from `*(model+1280)` (one-hot value mapped to index 0..8); upload tint multiply color to PS constant register c0 and tint add/alpha color to PS constant register c1. Palettes are on the wrapper (9 entries).
3. Disable fog; enable alpha blend with SrcBlend=D3DBLEND_SRCALPHA (5) / DestBlend=D3DBLEND_INVSRCALPHA (6).
4. Write per-draw render flag from `*(model+1836)` → selects sampler/render-state combination.
5. `SetStreamSource(0, *(model+1264), offset=0, stride=32)`.
6. `SetIndices(*(model+1268))`.
7. `DrawIndexedPrimitive(D3DPT_TRIANGLELIST, BaseVertexIndex=*(this+120), MinVertexIndex=0,
   NumVertices=*(range+44), StartIndex=*(this+124), PrimitiveCount=*(range+48))`.
   *Note:* `*(this+120)` is the **BaseVertexIndex** (not MinVertexIndex); MinVertexIndex is literal 0 — confirmed by D3D9 ABI arg order across both the VS and fixed-function draw paths.

If VS capability is absent, slot 4 tail-calls vtable **slot 12** (the fixed-function fallback draw).
[confirmed]

**Fixed-function fallback (vtable slot 12):** `SetFVF(274)` → `SetStreamSource(0, *(model+1264),
offset=0, stride=32)` → `SetIndices(*(model+1268))` → same `DrawIndexedPrimitive` call as above
(BaseVertexIndex=*(this+120), MinVertexIndex=0, NumVertices=*(range+44), StartIndex=*(this+124),
PrimitiveCount=*(range+48)). No shader constant uploads. [confirmed]

**Slot 11 (has-material gate):** `if (*(this+112)) → call slot 4`; the material field gates dispatch.
Slot 12 is the fixed-function fallback; all other leaf slots 12 are no-ops.

#### Table E.b — GPU-mesh / SkinSet resource (object at `*(this+104)`)

Recovered from the SkinSet deform+upload routine and the slot-4 draw body.

| Internal offset | Type | Field |
|---:|---|---|
| +100  | float | Scale, passed to attach-point world-matrix composition. |
| +1264 | ptr | **`IDirect3DVertexBuffer9*`** (dynamic; Lock = VB vtable slot 11 with `D3DLOCK_DISCARD` (0x2000); Unlock = slot 12). |
| +1268 | ptr | **`IDirect3DIndexBuffer9*`** |
| +1272 | u32 | Lock/upload gate flag. Deform+upload runs only when this field and +1264 are both non-zero. |
| +1280 | u32 | **Tint-state selector** (one-hot value; switch maps {1,2,4,8,16,32,64,128,…} → palette index 0..8). |
| +1364/+1368 | ptr, ptr | Skin-part vector `[begin, end)` — each element is a `Skin*` leaf pointer. |
| +1380/+1384 | ptr, ptr | Attach-point vector `[begin, end)`. |
| +1396/+1400 | ptr, ptr | Weapon-trail bone vector `[begin, end)`. |
| +1408 | ptr | Single attach-point pointer. |
| +1768 | u32 | **Deform mode**: 0 = linear blend skinning (LBS), 1 = rigid-major, 2 = rigid-owned. |
| +1836 | u8 | **Per-draw render flag** — selects the sampler/render-state combination for this part. |

Deform+upload per frame: Lock VB (`D3DLOCK_DISCARD`) → for each part: deform vertices according to mode at +1768 → `memcpy(lockedVB + 32 × part[+120], part[+116], 32 × partRange[+44])` → Unlock → compose attach-point world matrices. Bone/skinning matrix palette upload is handled separately — see `specs/skinning.md`. [confirmed — 11 fields recovered; inner bone-palette and sub-mesh regions of this class remain pending]

### Table F — `SwordLightEffect` (concrete; dynamic trail geometry)

`SwordLightEffect` is a large object (approximately 5964 bytes). The base region (+0..+95) follows
Tables A/B. Pre-array per-instance fields (from the constructor): +100 (u8) = 0; +104 = 0; +108 = trail-point count; +116 = 0; bytes +120..+123 = BGRA diffuse (B=G=R=0, A=0xFF); bytes +124..+127 = BGRA diffuse (same); +128 = texture-pool entry pointer; +132 = state/blend-selector object pointer (`*(+132)+20` selects trail blend mode). Beginning at +136, the constructor initialises an embedded array of 242 trail-segment records (24 bytes each) using an element-wise vector constructor. Per-instance state fields at the trailing end: +5944, +5948, and +5952 are initialised to 0; +5956 to 1.0f; +5960 to 44 (segment capacity).

#### Table F.a — Embedded trail-segment record (24 bytes; 242 elements at +136)

| Offset in record | Size | Type | Field |
|---:|---:|---|---|
| +0  | 12 | float[3] | Position (XYZ). Set per-frame during trail update; zeroed at construction. |
| +12 |  4 | u8[4] | Diffuse color BGRA. Constructor sets B=G=R=0, A=0xFF. |
| +16 |  8 | float[2] | UV coordinates. |

**Draw sequence (vtable slot 4):** gate `if (*(this+108) > 16 && *(this+100))`; disable Z-write; enable alpha test; set texture-stage states for trail blend (ADD when `*(*(this+132)+20) == 1`, else MODULATE); bind texture from +128 (or default); `SetFVF(322)`; `DrawPrimitiveUP` with vertex data at +136, vertexCount = `2 × *(this+108) − 32`, stride 24.

**FVF = 322 (0x142) = `D3DFVF_XYZ | D3DFVF_DIFFUSE | D3DFVF_TEX1`** — a 24-byte vertex (position 12 bytes + diffuse 4 bytes + UV 8 bytes). *Correction:* the spec previously assumed FVF 274/stride 32 for SwordLightEffect; trails use FVF 322 with a 24-byte stride. Independently corroborated by the per-quad vertex buffer creator, which calls `CreateVertexBuffer(Length=96 = 4×24, Usage=0x208 = D3DUSAGE_DYNAMIC|WRITEONLY, FVF=322, Pool=0 = D3DPOOL_DEFAULT)`. The +132 state object class is not yet identified. [confirmed — trail record layout and FVF; +132 state class pending]

---

## 5. Geometry / vertex format constants (all concrete leaves)

| Constant | Value | Public D3D9 meaning | Applies to |
|---|---|---|---|
| FVF | 274 (0x112) | `D3DFVF_XYZ \| D3DFVF_NORMAL \| D3DFVF_TEX1` — position (12 B) + normal (12 B) + one UV set (8 B) | `StaticSkin`, `Skin` (fixed-function fallback) |
| FVF | 322 (0x142) | `D3DFVF_XYZ \| D3DFVF_DIFFUSE \| D3DFVF_TEX1` — position (12 B) + diffuse BGRA (4 B) + one UV set (8 B) | `SwordLightEffect` (corrects prior FVF-274 assumption) |
| Vertex stride | 32 bytes | Matches FVF 274 component layout. | `StaticSkin`, `Skin` |
| Vertex stride | 24 bytes | Matches FVF 322 component layout. | `SwordLightEffect` |
| Primitive type | 4 | `D3DPT_TRIANGLELIST` | All leaves |
| Index format | 101 | `D3DFMT_INDEX16` (16-bit indices) | `StaticSkin`, `Skin` |
| Transparent blend | SrcBlend=5 / DestBlend=6 | `D3DBLEND_SRCALPHA / D3DBLEND_INVSRCALPHA` | `Skin` VS path; `SwordLightEffect` (ADD variant also possible — see Table F) |

[confirmed — FVF 274 for StaticSkin and Skin; FVF 322 for SwordLightEffect (static + per-quad VB creator corroboration)]

---

## 6. Vtable slot-role maps

### 6.1 GDrawable base vtable (6 slots)

| Slot | Vtable offset | Role |
|---|---|---|
| 0 | +0  | Scalar/vector deleting destructor. |
| 1 | +4  | Print-debug-info: dumps the object name, parent-geode list, and render-state count. |
| 2 | +8  | *(pure virtual)* `getBound` — overridden by `GGeometry` to return &this+36 after triggering a bound-update. |
| 3 | +12 | *(pure virtual)* Concrete draw hook A — overridden by each leaf class. |
| 4 | +16 | *(pure virtual)* Concrete draw hook B (main DRAW entry) — overridden by each leaf class. |
| 5 | +20 | *(pure virtual)* Type/visibility query — each leaf returns a leaf-specific constant. |

### 6.2 GGeometry vtable (16 slots) — abstract geometry

| Slot | Vtable offset | Role |
|---|---|---|
| 0  | +0  | Deleting destructor: unlinks the drawable from all parent geodes, frees the render-state vector and any owned buffers, restores the base vtable, then chains to the `GObject` destructor. |
| 1  | +4  | Print-debug-info override. |
| 2  | +8  | `getBound`: marks the bound dirty (calls slot 15), then returns &this+36. |
| 3  | +12 | *(pure virtual)* Concrete draw hook A — overridden by each leaf (`StaticSkin`: no-op body; `Skin`: no-op body). |
| 4  | +16 | *(pure virtual)* **DRAW** — the per-drawable render entry. Overridden by every concrete leaf to issue `DrawIndexedPrimitive` or `DrawIndexedPrimitiveUP`. Invoked by the render pipeline's device-wrapper `render-drawable` path. |
| 5  | +20 | Query; returns 1 (is-drawable / is-geometry indicator). |
| 6  | +24 | *(pure virtual)* Concrete hook — leaf-specific. |
| 7  | +28 | *(pure virtual)* Concrete hook — leaf-specific. |
| 8  | +32 | *(pure virtual)* Concrete hook — leaf-specific. |
| 9  | +36 | Query; returns 0. |
| 10 | +40 | Bound-update dispatch: tests `*(u8*)(this+80) & 1` (dirty bit); if set and `*(u32*)(this+76)` (enable gate) is non-zero, calls slot 13; otherwise calls slot 14. |
| 11 | +44 | Has-material gate / draw forwarder (overridden by concrete leaves). `Skin` slot 11: `if (*(this+112)) → call slot 4`. `StaticSkin` slot 11: calls slot 4 unconditionally. Used by the per-part draw loop (`Character_DrawSkinnedCelShaded` — the UI character-preview path). |
| 12 | +48 | No-op in `GGeometry`; **overridden by `Skin`** as the fixed-function fallback draw (see Table E). `StaticSkin` and `SwordLightEffect` slot 12 are no-ops. |
| 13 | +52 | *(pure virtual)* **update-bound** — leaf recomputes the AABB into +36/+48 and clears the dirty bit (`*(u8*)(this+80) &= ~1`). `Skin` slot 13 is a no-op that only clears the dirty bit (uses host-supplied bound). |
| 14 | +56 | Clears the bound-dirty bit: `*(u8*)(this+80) &= ~0x01`. |
| 15 | +60 | Bound-update fast path: tests `*(u8*)(this+80) & 1`; if set and `*(u32*)(this+76)` non-zero, calls slot 13. |

Concrete leaf vtables override slots 0, 3, 4, 6, 7, 8, 11, 12, and 13 with class-specific bodies.
Slots 1, 2, 5, 9, 10, 14, and 15 inherit the `GGeometry` implementations. `Skin` and
`SwordLightEffect` each append a slot 16. [confirmed]

### 6.3 GGeode owning-node vtable (10 slots) — context for parent linkage

`GGeode` is the scene-graph leaf-group that owns drawables and feeds the `GCull` collect phase.

| Slot | Vtable offset | Role |
|---|---|---|
| 0 | +0  | Deleting destructor. |
| 1 | +4  | Init / activate hook. |
| 2 | +8  | `getBound`. |
| 3 | +12 | No-op hook. |
| 4 | +16 | Bound/transform hook. |
| 5 | +20 | **Cull-collect opaque drawables** — feeds visible child drawables to the `GCull` opaque collect phase. |
| 6 | +24 | **Cull-collect sorted drawables** — distance-sorted / transparent path. |
| 7 | +28 | Add/attach drawable hook. |
| 8 | +32 | Traverse/visit hook. |
| 9 | +36 | Miscellaneous hook. |

---

## 7. Per-frame draw path (end to end)

1. **Cull collect** (`structs/cull_pipeline.md`): `GCull` traverses the scene graph; each visible
   `GGeode` (vtable slots 5/6) emits its child drawables as `GDrawablePair{drawable, render-state-set}`
   (opaque path) or `GRangeObject` (distance-sorted transparent path). These are sorted into bins and
   flushed into the `GCull` block-deque draw queue as linked nodes
   `{next, prev, state-set, drawable}`.

2. **Cull draw-walk** (`GCull` vtable slot 1 — `drawTraverse`): for each draw node `{next, prev, state-set, drawable}`:
   (a) sets engine render-state token 256 to the node's state-set via `Renderer_SetRenderState_256`;
   (b) calls `GCull_ApplyDrawableLight(drawable, idx)` — this applies `IDirect3DDevice9::SetLight(idx, &drawable+80)` only when `*(u32*)(drawable+76)` is non-zero (so `drawable+80` onward is treated as an in-place `D3DLIGHT9` block for scene drawables that have room; whether Skin/StaticSkin leaves traverse this path requires debugger confirmation — see §8);
   (c) calls `render-drawable` passing the drawable and blended flag (`1` when `*(u8*)(drawable+76) == 1`).

3. **Render-drawable → drawable slot 4:** the wrapper `render-drawable` path invokes **vtable slot 4**
   on the drawable. Slot 4 has zero direct code callers — it is reached exclusively via virtual dispatch.
   The per-part draw loop (`Character_DrawSkinnedCelShaded`) is the confirmed UI character-preview path
   (called from inventory/info panel draw routines); it iterates parts and calls slot 11 (the has-material
   gate), which forwards to slot 4. Which driver runs for an in-world character — the cull render-drawable
   walk or the per-part loop — requires debugger confirmation. [debugger-confirm]

4. **Leaf draw (slot 4):** binds the stage-0 texture from the material object (`*(material+52)`);
   either uploads WVP and tint shader constants (`Skin` VS path) or calls `SetFVF(274)` (fixed-function
   path); binds the vertex buffer at stride 32 and the index buffer (or passes system-memory arrays to
   `DrawIndexedPrimitiveUP`); issues `DrawIndexedPrimitive` or `DrawIndexedPrimitiveUP` as
   `D3DPT_TRIANGLELIST` with `D3DFMT_INDEX16` (16-bit) indices.

### D3D9 methods invoked by the drawable family

The following canonical `IDirect3DDevice9` methods are called by the concrete leaf draw routines via
the `GRenderDevice` wrapper thunks. All slot indices are public Direct3D 9 SDK vtable slot numbers.

The table below covers all `IDirect3DDevice9` methods called by the drawable family, including
the cull draw-walk and the SkinSet deform path. All slot indices are public Direct3D 9 SDK vtable
slot numbers. The `GRenderDevice` wrapper singleton holds the raw `IDirect3DDevice9*` at internal
byte offset +177976; methods are reached via static wrapper thunks.

| Operation | D3D9 vtable slot | `IDirect3DDevice9` method |
|---|---|---|
| Create dynamic vertex buffer | 26 | `CreateVertexBuffer(Length, Usage, FVF, Pool, ppVB, 0)` |
| Apply light to device | 51 | `SetLight(index, pLight)` — `pLight = &drawable+80` in the cull draw-walk |
| Bind texture to stage | 65 | `SetTexture(stage, pTexture)` |
| Set texture stage state | 67 | `SetTextureStageState` |
| Draw indexed triangles from VB/IB | 82 | `DrawIndexedPrimitive` |
| Draw indexed triangles from system memory | 84 | `DrawIndexedPrimitiveUP` |
| Set fixed-function vertex format (FVF) | 89 | `SetFVF` (274 for Skin/StaticSkin; 322 for SwordLightEffect) |
| Set render state | 92 | `SetRenderState` |
| Upload VS constant registers (float) | 94 | `SetVertexShaderConstantF` (WVP → register c0, 4 rows) |
| Bind vertex buffer to stream | 100 | `SetStreamSource(stream=0, pVB, offset=0, stride=32)` |
| Bind index buffer | 104 | `SetIndices(pIB)` |
| Upload PS constant registers (float) | 109 | `SetPixelShaderConstantF` (tint multiply → c0; tint add/alpha → c1) |
| Set sampler state | 107 | `SetSamplerState` |
| Lock / Unlock vertex buffer | VB vtable slot 11 / 12 | Called on the `IDirect3DVertexBuffer9*` COM object, not the device. Lock uses `D3DLOCK_DISCARD` (0x2000). |

[confirmed — full static thunk mapping, wave-11 deep-dive]

---

## 8. Open items and pending confirmation

Wave-11 deep-dive (2026-06-28) closed most previously-open items. The following remain.

### Closed by wave-11 (no further confirmation needed)

- **GObject +4 semantics** — CLOSED: confirmed as reference count (u32); unref routine asserts non-zero then decrements. See Table A.
- **+76 / +80 widths and roles** — CLOSED: +76 is a 4-byte DWORD (enable gate + low-byte blend selector); +80 is a 1-byte bound-dirty flag (bit 0). Corrects `cull_pipeline.md` Table F on both width and label. See Table B and §6.2.
- **GVector internal word order** — CLOSED: `{count@+0, reserve@+4, capacity@+8(default 16), data@+12}`, 4-byte elements. See Table B rows +60 and +84.
- **GPU-mesh resource fields** — SUBSTANTIALLY CLOSED: 11 fields recovered including VB, IB, lock-gate, tint-selector, part/attach/trail vectors, deform mode, and render flag. See Table E.b.
- **Render-drawable dispatch chain** — TIGHTENED: slot 4 = vtbl+16, zero direct callers; slot 11 is the has-material gate; `Character_DrawSkinnedCelShaded` is the UI preview path (calls slot 11 per part). One debugger item remains (see below).
- **SwordLightEffect trail-record inner layout** — CLOSED: 24-byte record = pos(12)+BGRA diffuse(4)+UV(8); FVF 322 (`D3DFVF_XYZ|D3DFVF_DIFFUSE|D3DFVF_TEX1`), `DrawPrimitiveUP`, stride 24. Corrects prior FVF-274 assumption. See Table F.a.
- **Skin +120 label correction** — CLOSED: `*(this+120)` is **BaseVertexIndex**, not MinVertexIndex (literal 0). Corrects Table E draw sequence step 7. See Tables E and the draw sequence note.

### Still [debugger-confirm] (tightly bounded by static analysis)

1. **+80 as SetLight argument vs. bound-dirty overlap for leaf objects.** Static-final: +80 is 1 byte, bit 0 = bound-dirty; the cull draw-walk passes `&(drawable+80)` as `pLight` to `SetLight`. A full `D3DLIGHT9` (104 bytes) cannot fit at +80 within the `Skin` (≈132 bytes) or `StaticSkin` (≈156 bytes) leaves without overrunning the object. Two static-consistent resolutions: (a) leaf objects are not drawn through the `GCull_ApplyDrawableLight` path (some other scene drawable class is the actual light carrier), or (b) `*(u32*)(leaf+76)` is zero at draw time so `ApplyDrawableLight` short-circuits before calling `SetLight`. **Live:** read a drawn `Skin`'s +76 value; observe whether `SetLight` fires for it.

2. **Which driver invokes slot 4 for an in-world character.** Static-final: slot 4 has zero direct code callers; `Character_DrawSkinnedCelShaded` is confirmed as the UI preview path (inventory/info panels). **Live:** breakpoint the `Skin` slot-4 body while a character renders in-world; read the return-address chain to determine whether dispatch comes from the cull render-drawable walk or from a per-part slot-11 loop.

### New open questions (follow-up static lanes)

- **GDrawable +96 second-buffer question:** +96 coincides with the parent-geode GVector's data pointer. Confirm there is no second distinct owned buffer at this offset (i.e., the explicit `free(*(this+96))` in the destructor is purely freeing the GVector backing).
- **Skin +100** (zero-init) — purpose not isolated.
- **GPU-mesh resource full class** — bone-palette region and sub-mesh tables between +1268 and +1768 (consumed by the LBS/rigid deform functions); recover as a dedicated struct lane keyed off `Skin+104`. See `specs/skinning.md`.
- **SwordLightEffect +132 state object** — `*(+132)+20` selects the trail blend mode; the owning class is not yet identified.
- **StaticSkin mesh-descriptor (+148) identity vs. the AABB/budget recompute object** — confirm they are the same object (both expose `NumVertices@+40`) and that `StaticSkin+152` is the same array as mesh-descriptor internal +56 (the transformed-output vertex array).

---

## 9. Cross-references

- Cull pipeline draw-walk, device-wrapper render-drawable path, and cull bin-entry record layouts:
  `structs/cull_pipeline.md`.
- Render-pass frame sequence and `GRenderDevice` wrapper: `specs/render_pipeline.md`.
- Scene-graph class hierarchy (`GGroup`, `GScene`, `GCamera`, `GCull`, `GGeode`):
  `specs/scene_graph.md`.
- Skinning matrix upload and `.bud`/`.skn` GPU mesh resource loading: `specs/skinning.md`.
- Terrain rendering and cell streaming: `specs/terrain-streaming.md`.
- Character skin texture chain: `specs/entity_placement.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
