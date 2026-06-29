---
verification: confirmed (static IDA on IDB SHA f61f66a9, 2026-06-28) — every offset and field role recovered from static control-flow + operand analysis on this build; closes the four open questions from the wave-1 camera lane.
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false      # in-memory C++ object layout, not a packed-file format
subsystems: [render_pipeline, world_systems]
conflicts: none-open        # wave-1 left/right swap corrected; vtable slot count corrected; no remaining open items except the debugger-pending fovy storage note
---

# Struct: GPerspectiveCamera — field offset table

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static analysis under EU Software
> Directive 2009/24/EC Art. 6, solely to document object layout for clean-room reimplementation.
> **All offsets below are byte offsets relative to the start of the object** — they are struct/layout
> offsets, NOT memory addresses, and must never be treated as such. Citing engineers:
> `// spec: Docs/RE/structs/perspective_camera.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow + operand analysis,
> corroborated across multiple sites; `[static-hypothesis]` = structurally inferred or seen only as a
> zero-init, not independently re-isolated this pass; `[debugger-pending]` = layout is confirmed but
> one behavioural detail requires a live `?ext=dbg` read to settle definitively. This is a static-only
> pass — no debugger session was run.

---

## Summary

`GPerspectiveCamera` is the renderer's perspective-projection object. It is a small, refcounted, named
base object that holds **near/far planes, an embedded 6-plane frustum, the four off-centre extents
(left/right/bottom/top), the vertical FOV, a derived horizontal FOV, the aspect ratio, and a device
back-pointer** — and nothing else. Object size is **184 bytes**, naturally aligned (not packed). Every
byte is accounted for; the only padding is a 3-byte alignment gap after the off-centre flag.

The base class chain is `GObject` (refcounted, named) → `GCamera` (adds near/far + embedded frustum;
its apply-projection virtual is pure) → `GPerspectiveCamera` (adds extents / FOV / aspect / device and
the concrete projection builder). One instance is constructed per scene: the in-world scene builds one,
the character-select scene builds another.

**Four architectural facts confirmed by this pass:**

1. **No eye / target / up / position / orientation.** The camera's base is `{vtable, reference-count,
   name-string}` — a refcounted named object, not a transform scene-node. The camera pose lives
   entirely on the owning `GViewPlatform` transform node; the camera contributes only projection.
2. **No cached view or projection matrix.** The view matrix is built per-frame into the render-view
   descriptor and applied as `D3DTS_VIEW`; the projection matrix is built on the stack inside the
   apply-projection virtual and applied directly as `D3DTS_PROJECTION`. The only persistent geometric
   cache is the embedded 6-plane frustum, which `UpdateProjection` rebuilds whenever projection
   parameters change.
3. **No handedness field.** Right-handedness is hard-coded (`D3DXMatrixPerspectiveOffCenterRH`). The
   left-handed projection path belongs exclusively to the separate shadow projector rig, not this object.
4. **Vtable has exactly 3 slots** — destructor / describe / apply-projection. A pointer preceding the
   vtable in memory is the RTTI complete-object-locator, not a method slot.

---

## Object ownership and lifetime

`GPerspectiveCamera` is owned by the scene's `GViewPlatform` transform node, held at field offset +80
on `GViewPlatform`. The per-frame setup reaches it via:
`render-view descriptor [field +40] → GViewPlatform → [field +80] → GPerspectiveCamera`.

The camera is a pointed-to sub-object of the platform — the platform supplies the transform
(eye/up/orientation via its world-matrix chain); the camera supplies the projection (frustum / FOV /
aspect). They are distinct objects.

The object is refcounted: the reference count (base field +4) is initialized to 0 in the `GObject`
constructor. Vtable slot 0 (the destructor) performs the scalar-deleting deletion. The name is an
embedded MSVC `std::string` at field +8 (28 bytes wide, VC7.1 layout), constructed by the `GObject`
constructor.

---

## Full offset table (object size 184 bytes) — naturally aligned

| Byte offset | Hex | Size | Type | Field | Notes | Confidence |
|------------:|-----|-----:|------|-------|-------|------------|
| +0 | +0x00 | 4 | ptr | **vtable pointer** | Points to the `GPerspectiveCamera` virtual function table, set in turn by the `GObject`, `GCamera`, and `GPerspectiveCamera` constructors. | confirmed |
| +4 | +0x04 | 4 | int32 | **reference count** | Initialized to 0 by the `GObject` constructor. Debug-print labels it "Reference count". | confirmed |
| +8 | +0x08 | 28 | `std::string` | **node name** | MSVC `std::basic_string<char>`, VC7.1 layout, 28 bytes. Constructed at +8 by the `GObject` constructor; debug-print labels it "name". Spans +8..+35; the near-plane field begins at +36 (offset forced by the 28-byte string width). | confirmed |
| +36 | +0x24 | 4 | float | **near plane** | `GCamera` base field. Constructor default **0.1**; scene construction sets **5.0** (in-world). | confirmed |
| +40 | +0x28 | 4 | float | **far plane** | `GCamera` base field. Constructor default **1000.0**; scene construction sets **15000.0** (in-world). | confirmed |
| +44 | +0x2C | 4 | ptr | **embedded GFrustum vtable pointer** | Marks the start of the embedded `GFrustum` (`GPolytope`) sub-object. Set by the `GCamera` constructor. See **Embedded GFrustum layout** below. | confirmed |
| +48 | +0x30 | 4 | int32 | **frustum plane count** = 6 | Written by the plane builder. Cull mask = `(1 << count) − 1` = 63. | confirmed |
| +52 | +0x34 | 16 | float4 | **frustum plane[0]** | Normalized `{nx, ny, nz, d}` where `d = −dot(normal, point)`. See **Plane format** note. | confirmed |
| +68 | +0x44 | 16 | float4 | **frustum plane[1]** | | confirmed |
| +84 | +0x54 | 16 | float4 | **frustum plane[2]** | | confirmed |
| +100 | +0x64 | 16 | float4 | **frustum plane[3]** | | confirmed |
| +116 | +0x74 | 16 | float4 | **frustum plane[4]** | | confirmed |
| +132 | +0x84 | 16 | float4 | **frustum plane[5]** | Frustum block ends at byte +148. | confirmed |
| +148 | +0x94 | 1 | uint8 | **off-centre / extent-source flag** | `0` = symmetric mode: derive left/right/bottom/top from fovy + aspect. `1` = explicit mode: explicit left/right/bottom/top given, back-derive fovy/aspect. Initialized to 0 by the constructor. **This is NOT a handedness flag** — right-handedness is unconditionally hard-coded. | confirmed |
| +149 | +0x95 | 3 | — | **alignment padding** | The object's sole padding gap (3 bytes). | confirmed |
| +152 | +0x98 | 4 | float | **frustum left** | Off-centre left extent. Symmetric mode: `left = −right`. | confirmed |
| +156 | +0x9C | 4 | float | **frustum right** | Off-centre right extent. Corrected from wave-1 draft (which had left/right swapped); the debug-print reads +156 as "right" and +152 as "left". | confirmed |
| +160 | +0xA0 | 4 | float | **frustum bottom** | Off-centre bottom extent. Symmetric mode: `bottom = −top`. | confirmed |
| +164 | +0xA4 | 4 | float | **frustum top** | Off-centre top extent. Symmetric mode: `top = near × tan(fovy / 2)`. | confirmed |
| +168 | +0xA8 | 4 | float | **fovy** (vertical FOV, radians) | Constructor default **π/4 (45°)**; scene construction sets **65°** (in-world) or **50°** (character-select). See **fovy storage note** below. | confirmed |
| +172 | +0xAC | 4 | float | **fovx** (derived horizontal FOV, radians) | Computed by `UpdateProjection` from near / left / right. Constructor default **π/3 (60°)**. Consumed by culling and LOD detail-scale. | confirmed |
| +176 | +0xB0 | 4 | float | **aspect ratio** | Screen width / screen height. Constructor initializes to 4/3 (≈ 1.333) then immediately resets to the live screen ratio. The parameter setter ignores any passed aspect argument — this field always tracks the live screen resolution. | confirmed |
| +180 | +0xB4 | 4 | ptr | **device / renderer back-pointer** | Set in the constructor to the renderer singleton. Passed to `SetTransform` when applying the projection matrix. | confirmed |
| +184 | +0xB8 | — | — | **end of object (184 bytes)** | | |

### Plane format

Each frustum plane is a normalized `float4 {nx, ny, nz, d}` where `nx/ny/nz` form the unit inward
normal and `d = −dot(normal, point)`. The six planes represent the near, far, left, right, top, and
bottom faces of the frustum. They are built from the 8 frustum corner points (derived from
left/right/bottom/top/near/far) using a triangle-edge cross product to obtain the normal, a
normalization step, and then solving for `d`.

### fovy storage note [debugger-pending]

The value stored at +0xA8 may be `(π · degrees / 180) / aspect` (i.e., the radian measure divided by
the aspect ratio at the build call site), rather than the raw radian measure. Whether the perceived
vertical FOV at the device equals 65° or `65° / aspect` requires a live `?ext=dbg` read of +0xA8 on a
running session. This does not affect the struct layout or any other field, and is a carry-over open
item from the prior wave.

---

## Constructor defaults (before scene-build overrides)

| Field | Offset | Constructor default |
|-------|--------|---------------------|
| off-centre flag | +0x94 | 0 (symmetric mode) |
| frustum left/right/bottom/top | +0x98..+0xA4 | 0.0 each |
| near plane | +0x24 | 0.1 |
| far plane | +0x28 | 1000.0 |
| fovy | +0xA8 | π/4 (45°) |
| fovx | +0xAC | π/3 (60°) |
| aspect | +0xB0 | 4/3 (≈ 1.333), then reset to live screen ratio |
| device back-pointer | +0xB4 | renderer singleton |

The 45°/60° FOV defaults are overridden by scene construction before any frame renders. They are purely
the post-constructor baseline — no runtime code sees them.

---

## Embedded GFrustum layout (camera +0x2C)

The embedded `GFrustum` (which derives from `GPolytope`) occupies 104 bytes starting at camera offset
+0x2C (+44 dec). Its internal layout, relative to the frustum base:

| Frustum-relative | Camera-absolute | Size | Role |
|-----------------:|----------------:|-----:|------|
| +0 | +0x2C | 4 | `GFrustum` vtable pointer |
| +4 | +0x30 | 4 | plane count (int) = 6 |
| +8 | +0x34 | 96 | plane array: 6 × `float4 {nx, ny, nz, d}`, stride 16 |
| — | — | **104 total** | ends at camera +0x94 (the off-centre flag byte) |

`UpdateProjection` rebuilds all six planes whenever the projection parameters change. The plane builder
derives the 8 frustum corners from left/right/bottom/top/near/far, computes each plane normal via
edge-cross product, normalizes, stores `d = −dot(normal, point)`, then writes count = 6.

---

## Virtual method table (3 slots)

The `GPerspectiveCamera` vtable has **exactly 3 method slots**. The RTTI complete-object-locator
pointer sits in memory immediately before the vtable start (at vtable−4); it is not a method slot and
was mistakenly counted as one in the wave-1 note.

| Slot | Conceptual name | Description |
|-----:|-----------------|-------------|
| 0 | **destructor** | Scalar-deleting destructor (standard MSVC pattern). |
| 1 | **describe** | Streams the object's fields to a text sink for diagnostic output. Prints `near/far=` and then, depending on the off-centre flag (+0x94), either `fovy=` / `aspect=` (symmetric mode) or `right/left=` / `bottom/top=` (explicit mode). No runtime side-effects. |
| 2 | **apply-projection** | Concrete projection builder: computes `D3DXMatrixPerspectiveOffCenterRH(left, right, bottom, top, near, far)` and applies the result to the device as `D3DTS_PROJECTION`. **In the `GCamera` base this slot is pure virtual** — `GPerspectiveCamera` supplies the perspective implementation. |

The `GCamera` base vtable has the same 3-slot shape: slot 0 dtor, slot 1 describe (near/far only),
slot 2 pure virtual (the projection hook each subclass overrides).

---

## Where the eye, orientation, and matrices actually live

Because `GPerspectiveCamera` contains no transform, recovering camera pose requires the owning objects:

**Eye / target / up / orientation** — not in this struct. The pose is the world matrix of the owning
`GViewPlatform` transform node, computed by `ComputeWorldTransform` walking the parent chain: seeds a
row-major 4×4 identity, accumulates each parent's local transform via the node vtable's transform
virtual (vtable slot 3), then optionally post-multiplies by a correction matrix if a flag at
`GViewPlatform+0x4C` is set.

**View matrix** — computed per frame in `Renderer_SetupCameraAndFrustum` into the render-view
descriptor at field offset +44 (a 64-byte 4×4 matrix) as the inverse-orthonormal of the owning node's
world transform (rotation transposed; translation = −Rᵀ·t), then applied as `D3DTS_VIEW`. The view
matrix is **not cached** on `GPerspectiveCamera`.

**Projection matrix** — built on the stack in vtable slot 2 and applied directly to the device as
`D3DTS_PROJECTION`. The projection matrix is **not cached** on `GPerspectiveCamera`.

**Handedness** — right-handed, hard-coded via `D3DXMatrixPerspectiveOffCenterRH`. The off-centre flag
at +0x94 selects only the extent-derivation mode, not the handedness. The left-handed projection path
(`PerspectiveFovLH` / `LookAtLH`) belongs exclusively to the shadow projector rig, a separate object.

---

## Parameter setter semantics (port fidelity)

`ParamObject_SetAngleAndRange` applies the following before invoking `UpdateProjection`:

- **Validates** that `0 < fovy ≤ π`, `aspect > 0`, `near > 0`, and `far > 0`; rejects otherwise.
- **Forces** off-centre flag (+0x94) to 0 (symmetric mode).
- **Writes** the fovy argument to +0xA8.
- **Auto-sorts** near and far: `near = min(near, far)`, `far = max(near, far)`.
- **Ignores the passed aspect argument** — the aspect field (+0xB0) always holds the live screen ratio
  set at construction and is never overwritten by this setter.
- **Calls** `UpdateProjection` to rebuild extents and the frustum.

A faithful port: the camera owns fovy + near + far; aspect always derives from the live screen
resolution.

---

## Notes and open items

1. **fovy storage [debugger-pending].** The exact value at +0xA8 may be `(π · deg / 180) / aspect`
   rather than the raw radian measure. A live `?ext=dbg` read on a running session is the definitive
   test. Struct layout is unaffected.
2. **std::string size [confirmed].** The 28-byte extent of the name field (+8..+35) is fixed by the
   binary's own constructor offsets (string constructor at +8, near-plane field at +36). Consistent
   with VC7.1 release `std::string`. A live read would confirm the SSO / heap-pointer split if ever
   needed but is not required for layout fidelity.
3. **Device back-pointer (+0xB4).** Holds the renderer singleton (a process-wide object). A port
   models this as the render device that projection is applied to; the exact type belongs to the
   GDevice / renderer lane.
4. **`GCamera` base defaults.** The near/far constructor defaults (0.1 / 1000.0) are set in the
   `GCamera` constructor and are shared by all `GCamera`-derived subtypes. The apply-projection virtual
   is pure in `GCamera`.
5. **Not packed.** The object is naturally aligned. The sole padding is 3 bytes at +0x95. Total size
   184 bytes; no hidden or compiler-injected fields remain unaccounted.

---

## Cross-references

- Camera movement behaviour and per-frame matrix construction: `Docs/RE/specs/camera_movement.md §A.7.1`.
- Render pipeline and render-view descriptor layout: `Docs/RE/specs/render_pipeline.md`.
- `GViewPlatform` transform node and `ComputeWorldTransform`: struct lane pending (GViewPlatform lane).
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
