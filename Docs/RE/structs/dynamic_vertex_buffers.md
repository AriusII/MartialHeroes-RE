---
verification: confirmed (static, IDB SHA f61f66a9, CYCLE 14 (2026-06-28))
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [render_pipeline, particles, sky, weather, characters, terrain]
---

# Structs: Dynamic Vertex and Index Buffers — Geometry Submission Substrate (Diamond engine, D3D9)

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. All offsets are byte offsets relative to the start of the respective class
> sub-object — they are struct/layout offsets, NOT memory addresses, and must never be treated
> as such.
> Citing engineers: `// spec: Docs/RE/structs/dynamic_vertex_buffers.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from constructor, destructor, vtable, and
> draw control-flow, corroborated across multiple call sites; `[hypothesis]` = inferred from
> zero-initialisation or seen only once, not independently re-isolated; `[debugger-pending]` =
> flagged for live `?ext=dbg` session confirmation (never `dbg_start`).

---

## 1. Overview

The Diamond render engine submits all geometry through **three distinct device paths**, chosen per
subsystem. There are no D3DPOOL_MANAGED buffers anywhere in the client: every `CreateVertexBuffer` /
`CreateIndexBuffer` call site inspected uses Usage 520 (D3DUSAGE_DYNAMIC | D3DUSAGE_WRITEONLY) and
Pool 0 (D3DPOOL_DEFAULT). The only "static" data is the CPU heap arrays passed on the user-pointer
draw paths, which never become device buffer objects. [confirmed — 22 create sites inspected]

**Path 1 — system-memory user-pointer draws (no device buffer).** Vertices and indices live in
ordinary CPU heap arrays passed directly to the device each draw call via `DrawIndexedPrimitiveUP`
(D3D9 slot 84) and `DrawPrimitiveUP` (D3D9 slot 83). Used by terrain layers, buildings, all seven
Fx terrain-decoration layers, static-skin meshes (see `drawable_geometry.md` Table D), actor trails,
and UI overlay quads.

**Path 2 — shared, class-static, grow-only dynamic VB/IB pool (`GParticleBuffer`).** A lightweight
wrapper object whose vertex and index buffers are class-static singletons shared by all instances.
Each draw group executes Lock → append vertices (memcpy at a cursor) → Unlock → Draw → reset cursor.
Used by all generic particles, sky billboards, weather (rain/snow), and sky particles.

**Path 3 — per-instance dynamic VB/IB, created once and refilled on demand.** Real
`IDirect3DVertexBuffer9` / `IDirect3DIndexBuffer9` objects owned by one object, created via
`CreateVertexBuffer` / `CreateIndexBuffer` (D3D9 slots 26/27). Used by skinned characters (the Skin
GPU-mesh resource — see `drawable_geometry.md` Table E), X-object static meshes, the cloud dome, the
star dome, lens flares, and sword-light trails.

---

## 2. D3D9 device-vtable method reference

All slot numbers are public D3D9 SDK values. Canonical Diamond wrapper thunk names are given where
the thunk is a named, non-inline function.

| Operation | D3D9 slot | Vtable byte offset | Canonical wrapper thunk |
|---|---:|---:|---|
| CreateVertexBuffer | 26 | +104 | `Renderer_CreateVertexBuffer` |
| CreateIndexBuffer | 27 | +108 | `Renderer_CreateIndexBuffer` |
| SetRenderState | 57 | +228 | (inline in particle draw groups) |
| SetTextureStageState | 67 | +268 | (inline in actor-trail draw) |
| DrawPrimitive | 81 | +324 | (inline in particle point-sprite draw) |
| DrawIndexedPrimitive | 82 | +328 | `RenderDevice_DrawIndexedPrimitive` |
| DrawPrimitiveUP | 83 | +332 | `GDevice_DrawPrimitiveUP` |
| DrawIndexedPrimitiveUP | 84 | +336 | `RenderDevice_DrawIndexedPrimitiveUP` |
| SetFVF | 89 | +356 | `RenderDevice_SetFVF` |
| SetStreamSource | 100 | +400 | `RenderDevice_SetStreamSource` (stream offset forced to 0) |
| SetIndices | 104 | +416 | `RenderDevice_SetIndices` |

`IDirect3DVertexBuffer9` and `IDirect3DIndexBuffer9` (inherit `IDirect3DResource9` → `IUnknown`):
Release = slot 2 (vtable +8); **Lock = slot 11 (vtable +44)**; **Unlock = slot 12 (vtable +48)**.

The `GRenderDevice` singleton holds the raw `IDirect3DDevice9*` at a known internal byte offset;
all wrapper thunks double-deref through it. See `Docs/RE/structs/renderer_device.md` and
`Docs/RE/specs/render_pipeline.md`. [confirmed — static thunk mapping]

---

## 3. D3D9 constants used at create and draw sites

| Value | Field | Public D3D9 meaning |
|---:|---|---|
| 520 | Usage (buffer create) | D3DUSAGE_DYNAMIC (0x200) \| D3DUSAGE_WRITEONLY (0x8) |
| 584 | Usage (point-sprite VB) | D3DUSAGE_DYNAMIC \| D3DUSAGE_WRITEONLY \| D3DUSAGE_POINTS (0x40) |
| 0 | Pool | D3DPOOL_DEFAULT |
| 101 | Index buffer format | D3DFMT_INDEX16 (16-bit unsigned indices) |
| 0x2000 | Lock flags | D3DLOCK_DISCARD (single-fill index or data buffers) |
| 0x800 | Lock flags | D3DLOCK_NOSYSLOCK (per-flush whole-VB lock in `GParticleBuffer`) |

[confirmed — decoded at all 22 inspected create sites]

---

## 4. FVF and stride reference

Every FVF value used across all three submission paths is a public D3DFVF combination.

| FVF value | Public D3D9 composition | Vertex stride | Used by |
|---:|---|---:|---|
| 2 | D3DFVF_XYZ | 12 bytes | Particle point-sprite VB |
| 258 | D3DFVF_XYZ \| D3DFVF_TEX1 | 20 bytes | Particle billboard VB |
| 274 | D3DFVF_XYZ \| D3DFVF_NORMAL \| D3DFVF_TEX1 | 32 bytes | Skinned character + static mesh (matches `drawable_geometry.md` §5) |
| 322 | D3DFVF_XYZ \| D3DFVF_DIFFUSE \| D3DFVF_TEX1 | 24 bytes | Sword-light trail, X-effect, cloud dome, star dome |
| 324 | D3DFVF_XYZRHW \| D3DFVF_DIFFUSE \| D3DFVF_TEX1 | 28 bytes | Lens flare (pre-transformed screen-space geometry) |

[confirmed — recovered from all concrete create and draw sites]

---

## 5. GParticleBuffer wrapper object

### 5.1 Class-static buffer model

`GParticleBuffer` is the sole shared dynamic-buffer wrapper in the client. Its
`IDirect3DVertexBuffer9` and `IDirect3DIndexBuffer9` objects are **class-static globals** (not
instance members): one billboard vertex buffer, one point-sprite vertex buffer, and one shared quad
index buffer, each paired with a class-static capacity counter. All `GParticleBuffer` instances share
these three device buffers; each instance holds a pointer to whichever vertex buffer it uses
(at object +24).

Buffers grow lazily: when a new instance requests a `Block` count exceeding the current capacity,
the old buffer is released (via `IDirect3DResource9::Release`, slot 2, vtable +8) and replaced with
a larger one. Once grown, a buffer never shrinks and is never freed per-instance. The shared quad IB
is filled with its static index pattern exactly once at grow time by `FillStaticIndices`. [confirmed]

### 5.2 Object offset table (~248 bytes)

| Byte offset | Size | Type | Field | Confidence |
|------------:|-----:|------|-------|------------|
| +0   | 4  | vtable ptr | Polymorphic dispatch. Single-slot vtable (deleting destructor only). All other methods are non-virtual. | confirmed |
| +4   | 4  | u32 | FVF code for this instance: 258 (billboard) or 2 (point-sprite). | confirmed |
| +8   | 4  | u32 | Vertex stride for this instance: 20 bytes (billboard) or 12 bytes (point-sprite). | confirmed |
| +12  | 1  | u8 (bool) | Mode flag: 1 = billboard-quad mode; 0 = point-sprite mode. | confirmed |
| +13  | 3  | — | Alignment padding. | — |
| +16  | 4  | u32 | `Block` — maximum particle capacity (quads in billboard mode; points in point-sprite mode). | confirmed |
| +20  | 4  | ptr | Texture-name string pointer; bound to texture stage 0 at draw time. | confirmed |
| +24  | 4  | ptr→ptr | Pointer to the active class-static vertex buffer global (selects billboard VB or point-sprite VB). | confirmed |
| +28  | 4  | ptr | CPU staging vertex array (heap-allocated). In billboard mode the array holds 4 vertices per quad; a 4-byte vertex-count header precedes the array data. | confirmed |
| +32  | 48 | float[3]×4 | Four quad-corner base offset vectors (untransformed object-space). | confirmed |
| +80  | 48 | float[3]×4 | Four transformed (camera-billboard) corner vectors; rebuilt from the camera orientation matrix at each `drawLock` call. | confirmed |
| +128 | 32 | float[2]×4 | Four corner UVs: (0,0), (1,0), (0,1), (1,1). | confirmed |
| +160 | 64 | float[16] | 4×4 billboard orientation matrix; copied from the active camera object at each `drawLock` call. | confirmed |
| +224 | 4  | float | Base point size; written to D3DRS_POINTSIZE (RS 154) at draw time. | confirmed |
| +228 | 4  | float | Minimum point size; written to D3DRS_POINTSIZE_MIN (RS 155); also the lower size clamp for per-particle billboard scaling. | confirmed |
| +232 | 4  | float | Maximum point size; written to D3DRS_POINTSIZE_MAX (RS 166); also the upper size clamp for billboard scaling. | confirmed |
| +236 | 4  | float | Point scale C coefficient; written to D3DRS_POINTSCALE_C (RS 160) at draw time. | confirmed |
| +240 | 4  | u32 | **Write cursor / current particle count.** Incremented by `appendQuadBatch` / `fillVertices`; **reset to 0 after every draw.** | confirmed |
| +244 | 4  | ptr | **Locked VB write pointer.** Base address returned by Lock (ppbData); held from `drawLock` through `unlockVB`. | confirmed |

Approximate object size: 248 bytes (highest member ends at +248). Exact allocation size including
tail padding is pending a live instance read. [debugger-pending for exact size]

### 5.3 Non-virtual method roles

| Method | Role |
|---|---|
| `setSpriteSize` | Writes maximum size (+232), minimum size (+228); sets the mode flag (+12) to point-sprite mode. |
| `setBillboardSize` | Writes base point size (+224); rebuilds the four base corner vectors (+32..+79) as ±halfSize offsets. |
| `setPointScaleA` | Writes the point scale C field at +236 (applied to D3DRS_POINTSCALE_C at draw time). |
| `FillStaticIndices` | One-time fill of the shared quad IB: Lock with D3DLOCK_DISCARD, write 6 indices per quad (see §5.5), Unlock. |
| `drawLock` | Copies the active camera orientation matrix into +160; rebuilds transformed corner vectors at +80; Locks the whole shared VB (offset 0, entire buffer; flags D3DLOCK_NOSYSLOCK = 0x800); stores the write pointer at +244. |
| `appendQuadBatch` | Memcpys pre-expanded quad vertices to `(+244) + (+240) × stride`; advances +240 by the quad count. |
| `fillVertices` | Per-particle append with per-particle size clamping to [+228, +232]; advances +240 by the particle count. |
| `unlockVB` | Calls Unlock (vtable +48) on the active shared VB. |
| `draw` | Issues the full SetFVF / SetStreamSource / SetIndices / bind-texture / DrawIndexedPrimitive sequence (billboard) or the SetRenderState batch / DrawPrimitive sequence (point-sprite); resets +240 to 0 after the draw call. |

### 5.4 Vtable slot-role map

| Slot | Vtable byte offset | Role |
|---:|---:|---|
| 0 | +0 | Deleting destructor. |

The vtable is one slot wide, confirmed by the read-only segment string-literal bound immediately
following the vtable head. All other behaviour is reached through direct non-virtual calls.
[confirmed]

### 5.5 Shared quad index pattern

`FillStaticIndices` writes the following pattern once (Lock with D3DLOCK_DISCARD; 6 × u16 per
quad; 12 bytes per quad). For quad index `i`:

```
{ 4i, 4i+1, 4i+3,  4i, 4i+3, 4i+2 }
```

Two triangles forming a rectangle. Total IB byte length = 12 × Block. [confirmed]

---

## 6. Per-frame submission paths

### 6.1 GParticleBuffer flush (particles / sky billboards / weather)

Consumers (`SkyParticle_DrawLocked`, `WeatherParticle_Draw`, `SkyBillboard_DrawQuadBatch`,
`ParticleEffect_drawAlive`, and others) all follow the same sequence per draw group:

1. (optional) `setBillboardSize` to set per-group quad size.
2. `drawLock`: in billboard mode, copies the camera orientation matrix into +160 and rebuilds the
   four transformed corner vectors at +80; then **Locks the whole active VB** (offset 0, entire
   buffer; D3DLOCK_NOSYSLOCK = 0x800). This is a fresh whole-buffer lock per flush — not a DISCARD
   or NOOVERWRITE sub-allocating ring.
3. One or more `appendQuadBatch` or `fillVertices` calls, each memcpying vertices at the current
   cursor position and advancing +240.
4. `unlockVB`: Unlock (slot 12, vtable +48).
5. `draw`:
   - **Billboard mode:** SetFVF(+4) → SetStreamSource(stream 0, billboard VB, stride=+8) →
     SetIndices(shared quad IB) → bind stage-0 texture → DrawIndexedPrimitive (D3DPT_TRIANGLELIST,
     BaseVertexIndex 0, MinVertexIndex 0, NumVertices `4×(+240)`, StartIndex 0,
     PrimitiveCount `2×(+240)`).
   - **Point-sprite mode:** SetRenderState batch — D3DRS_POINTSPRITEENABLE(156)=1,
     D3DRS_POINTSIZE(154)=+224, D3DRS_POINTSIZE_MIN(155)=+228, D3DRS_POINTSIZE_MAX(166)=+232,
     D3DRS_POINTSCALEENABLE(157)=1, D3DRS_POINTSCALE_A(158)=engine global,
     D3DRS_POINTSCALE_B(159)=engine global, D3DRS_POINTSCALE_C(160)=+236 — then SetFVF →
     SetStreamSource → bind stage-0 texture → DrawPrimitive (D3DPT_POINTLIST, StartVertex 0,
     PrimitiveCount=+240); then disables D3DRS_POINTSPRITEENABLE and D3DRS_POINTSCALEENABLE.
   - Both paths finish with `+240 = 0`. The shared buffer is reused serially: each instance locks
     from offset 0, fills, unlocks, and draws in turn.

[confirmed — static analysis of particle, billboard, and weather draw paths]

### 6.2 System-memory user-pointer draws (terrain / buildings / Fx / static skin / trails / UI)

No device buffer is created. The caller fills a CPU heap array and invokes the wrapper directly.

- `DrawIndexedPrimitiveUP` (D3D9 slot 84): called by `TerrainSection_DrawLayer`,
  `BuildingSection_DrawIndexed`, and the seven Fx terrain-decoration section draw functions.
- `DrawPrimitiveUP` (D3D9 slot 83): called by `ActorTrail_EmitTrailGeometry` (D3DPT_TRIANGLELIST /
  TRIANGLEFAN; FVF 322, stride 24; CPU array at object +136) and by `LinkPanel_DrawTextureOverlay`
  (D3DPT_TRIANGLEFAN, 6 vertices; stride 24; CPU array at object +196).

[confirmed — static caller enumeration]

### 6.3 Per-instance dynamic VB/IB (skinned characters / static meshes / domes / lens flares / sword light)

All per-instance buffers use Usage 520 (D3DUSAGE_DYNAMIC | D3DUSAGE_WRITEONLY), Pool 0
(D3DPOOL_DEFAULT). A Release-then-Create guard protects lazy re-creation. [confirmed]

| Subsystem | Create context | FVF | Stride | IB format | Notes |
|---|---|---:|---:|---|---|
| Skinned character + static mesh GPU resource | GPU-mesh init; rebind triggered by `ActorVisual_RebindLocalPlayerParts` on part change | 274 | 32 | D3DFMT_INDEX16 | VB at resource +1264, IB at +1268, vtx count +1272, idx count +1276 — matches `drawable_geometry.md` Table E. IB rebuilt via Lock DISCARD on part change. |
| X-object static mesh IB | `XObj_CreateIndexBufferFromData` (VB managed elsewhere) | — | — | D3DFMT_INDEX16 | Lock DISCARD, memcpy full index data, Unlock. |
| X-effect element VB | `XEffectElement_CreateVertexBuffer` | 322 | 24 | — | Dynamic refill per effect frame. |
| Cloud dome VB + IB | `CloudDome_CreateGpuResources` | 322 | 24 | D3DFMT_INDEX16 | Fixed geometry: 1440-byte VB, 720-byte IB. |
| Star dome VB + IB | `Stardome_BuildGeometryAndTexture`, `Stardome_RebuildBuffersAndTexture` | 322 | 24 | D3DFMT_INDEX16 | Buffer lengths are data-driven by dome tessellation; concrete sizes pending a live capture. [debugger-pending] |
| Lens flare VB | `LensFlare_BuildGeometry` | 324 (XYZRHW screen-space) | 28 | — | 112 bytes × billboard count. |
| Sword-light trail VB | `SwordLight_CreateVertexBuffer` | 322 | 24 | — | Fixed 96-byte VB (4 vertices × 24 bytes). |

---

## 7. Subsystem → submission-path matrix

| Subsystem | Path | Buffer object |
|---|---|---|
| Terrain layers | DrawIndexedPrimitiveUP (system memory) | none |
| Buildings | DrawIndexedPrimitiveUP | none |
| Fx terrain-decoration layers 1–7 | DrawIndexedPrimitiveUP | none |
| Static-skin meshes | DrawIndexedPrimitiveUP | none |
| Actor trails | DrawPrimitiveUP (system memory) | none |
| UI overlay quads | DrawPrimitiveUP (TRIANGLEFAN) | none |
| Generic particles | GParticleBuffer flush | shared class-static VB + IB |
| Sky billboards | GParticleBuffer flush | shared class-static VB + IB |
| Weather (rain / snow) | GParticleBuffer flush | shared class-static VB + IB |
| Sky particles | GParticleBuffer flush | shared class-static VB + IB |
| Skinned characters | DrawIndexedPrimitive (per-instance VB/IB) | dynamic, FVF 274, stride 32 |
| X-object static meshes | DrawIndexedPrimitive (per-instance VB/IB) | dynamic |
| Cloud dome / star dome | DrawIndexedPrimitive (per-instance VB/IB) | dynamic, FVF 322, stride 24 |
| Lens flare | DrawPrimitive (per-instance VB) | dynamic, FVF 324, stride 28 |
| Sword-light trail | DrawPrimitive (per-instance VB) | dynamic, FVF 322, stride 24 |

---

## 8. Object ownership and device lifetime

**GParticleBuffer class-static buffers** are shared engine-lifetime globals. They grow on demand
and are never freed per-instance; only the destructor (slot 0) releases the instance's heap CPU
staging array. Engine teardown releases all device children via `Engine_ReleaseRenderDeviceChildren`.

**Per-instance buffers** (skinned character GPU resource, X-object, cloud dome, star dome, lens
flare, sword light) are owned by their parent object, created lazily, and freed when the parent is
destroyed. All DYNAMIC/DEFAULT device buffers are invalid after a device loss and must be recreated
on device reset; the engine teardown path confirms release of these resources. The matching
recreation path on device reset has not been isolated. [create/release confirmed; reset path hypothesis]

The D3DRS_POINTSCALE_A (RS 158) and D3DRS_POINTSCALE_B (RS 159) coefficients read during the
point-sprite draw are engine-global state stored outside the `GParticleBuffer` object; their
producer (engine init or config path) has not been traced. [static follow-up]

---

## 9. Open items and pending confirmation

- **GParticleBuffer drawLock lock model:** the shared VB is locked per flush with D3DLOCK_NOSYSLOCK
  (not DISCARD / NOOVERWRITE). Whether multiple particle draw groups within one frame stall on the
  shared VB or whether the serial-reuse model avoids contention in practice is pending a live-frame
  capture. [debugger-pending]
- **GParticleBuffer exact object size:** taken as approximately 248 bytes from the highest written
  member (+244, 4 bytes); confirm exact allocation size and tail padding from a live instance.
  [debugger-pending]
- **Point-scale A/B global producer:** D3DRS_POINTSCALE_A (RS 158) and D3DRS_POINTSCALE_B (RS 159)
  are sourced from engine-global state outside the `GParticleBuffer` object; the setter has not been
  traced. [static follow-up]
- **Star dome buffer sizes:** cloud dome confirmed at 1440-byte VB / 720-byte IB; star dome buffer
  lengths are data-driven by dome tessellation and require a live capture to measure concretely.
  [debugger-pending]
- **Device-reset recreation path:** teardown confirmed via `Engine_ReleaseRenderDeviceChildren`; the
  matching per-instance buffer recreation on D3D device reset has not been isolated. [hypothesis]

---

## 10. Cross-references

- Drawable class hierarchy, StaticSkin / Skin vtable slot roles, GPU-mesh resource fields at
  +1264 / +1268 / +1272 / +1276: `Docs/RE/structs/drawable_geometry.md`.
- Cull pipeline draw-walk and device-wrapper render-drawable dispatch:
  `Docs/RE/structs/cull_pipeline.md`.
- Scene-graph node classes (GGeode, GGroup, GScene): `Docs/RE/structs/scene_graph_nodes.md`.
- GRenderDevice singleton layout and device-wrapper thunk mapping:
  `Docs/RE/structs/renderer_device.md`.
- Render-frame sequence and device-wrapper overview: `Docs/RE/specs/render_pipeline.md`.
- Skinning matrix upload and GPU-mesh resource loading from `.bud` / `.skn`:
  `Docs/RE/specs/skinning.md`.
- Terrain rendering and cell streaming: `Docs/RE/specs/terrain-streaming.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
