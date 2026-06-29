# Struct: GTextureManager / GTexture / GHandle / GHTex / FrameTickScheduler (Texture cache system)

> Clean-room struct layout specification. Derived-truth from static analysis of the client binary.
>
> **Verification:** container layout, RB-tree node, GTexture wrapper, and scheduler ring mechanics
> verified against the current IDA database.
> ida_reverified: 2026-06-28 · ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> subsystems: [texture_caching, frame_tick_scheduler, resource_lifetime]
> C# implementation: `MartialHeroes.Assets.Vfs`
> deep-3d-cartography (2026-06-29, static-only, ida_anchor f61f66a9): FrameTickScheduler effect-ring
> tail corrected -0x10 throughout (code-constant anchors: effect count +0x11944, options sentinel
> +0x11958); object-end corrected to +0x11984 (≈ 72 068 bytes — now internally consistent with the
> declared size); VRAM accumulator increment resolved to absent-in-static-image (vestigial).
> absorbed-ghtex (2026-06-29): GHandle base-class layout (§2), GHTex runtime-handle layout (§3), and
> GHandle/GHTex vtable definitions (§4) folded in from Docs/RE/structs/ghtex.md (sample-verified,
> ida_anchor f61f66a9, static-hypothesis); load-pipeline steps (§5.2) and manager shutdown (§5.3)
> also absorbed; cross-references to ghtex.md removed. Wave-2 container/manager corrections remain
> the canonical record here.

**Scope.** This spec covers the full texture resource system: the per-texture runtime handles (`GHandle`
base class at §2, `GHTex` concrete handle at §3), both vtable definitions (§4), the two *container*
objects — `GTextureManager` (the dedup map, its RB-tree node layout, and the load/shutdown lifecycle at
§5) and `FrameTickScheduler` (the streaming ring at §7) — together with the complete `GTexture`
cache-wrapper layout (§6). For the binary format of the texture list files that feed the loader see
`Docs/RE/formats/texture.md`.

**Wave-2 container corrections (canonical).** The following corrections to the wave-1 reading are the
authoritative record in this spec:

- The dedup map key is a **32-bit numeric texture id**, not a path string.
- The refcount on `GTexture` increments only at first cache insert; no bump on a cache hit.
- The scheduler is a **single** static-storage object; the pointer global and BSS instance are the same
  singleton.

---

## 1. Object Ownership

```
GTextureManager  (witnessed instance = id-registry singleton)
   std::map<uint32_t textureId, GTexture*>  (red-black tree; dedup + L1 refcount)
     └── GTexture  (60 B cache wrapper; refcount L1; see §6)
           └── GHTex  (76 B runtime handle; see §3)
                 └── registers one slot in FrameTickScheduler

FrameTickScheduler  (single static-storage instance, ~72 KB; see §7)
   primary ring   [up to 6000 slots] : every constructed GHTex handle
   back-index     [up to 6000 slots] : primary-slot to effect-slot mapping
   effect ring    [up to 6000 slots] : only handles with idle interval != 0 (streamable textures)
   round-robin tick: ~1.1% of effect ring per frame; idle handles invoke GHTex_Unload
```

The manager singleton is reached through the id-registry accessor, whose canonical name is the
texture-id-registry getter. The cache-fill method `Diamond_GTextureManager_GetTexture` is the only direct
cache-insert caller; it is driven by the list-file loader. See `Docs/RE/formats/texture.md` for the
list-file format and the "numeric-id texture registry" section that describes the key derivation.

---

## 2. GHandle Base Class Layout (48 bytes, `0x30`)

Abstract base class managing resource file paths, VRAM footprints, and loading flags. All `GHTex`
instances inherit this header. Provenance: sample-verified static-hypothesis at ida_anchor f61f66a9.

| Offset | Size (Bytes) | Type | Field | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `void*` | vtable | Virtual table pointer for `GHandle` (overridden by `GHTex`; see §4). |
| `+0x04` | 28 | `std::string` | `m_strPath` | VFS virtual path of the resource. 4-byte header, 16-byte SSO buffer, length at internal `+0x18`, capacity at internal `+0x1C`. |
| `+0x20` | 1 | `bool` | `m_bActive` | Status flag. Initialized to `1`. |
| `+0x21` | 1 | `bool` | `m_bLoaded` | Set to `1` when the asset is loaded in VRAM; otherwise `0`. |
| `+0x22` | 1 | `bool` | `m_bUnknown` | Initialized to `0`; purpose not confirmed. |
| `+0x23` | 1 | — | padding | Alignment byte. |
| `+0x24` | 4 | `uint32_t` | `m_dwTimeout` | Idle interval threshold in milliseconds. Initialized to `180 000` (3 minutes). Used by the `FrameTickScheduler` due-check. |
| `+0x28` | 4 | `uint32_t` | `m_dwLastAccessTime` | System tick count (`GetTickCount`) of the last access. Restamped on every texture acquire. |
| `+0x2C` | 4 | `uint32_t` | `m_dwVramSize` | Base-class VRAM footprint field. Relationship to `GHTex+0x44` (`m_dwTextureVramSize`) not confirmed. |

---

## 3. GHTex Runtime Handle Layout (76 bytes, `0x4C`)

Concrete texture resource wrapper inheriting from `GHandle`. Contains the Direct3D texture interface and
formatting options. Provenance: sample-verified static-hypothesis at ida_anchor f61f66a9; gap at +0x30
confirmed as ctor-uninitialised by both `GHandle__ctor` and `GHTex__ctor`.

| Offset | Size (Bytes) | Type | Field | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 48 | `—` | `GHandle` base | Base class payload (fields `+0x00..+0x2F`; see §2). |
| `+0x30` | 4 | `—` | *(ctor-uninitialised gap)* | Not written by `GHandle__ctor` or `GHTex__ctor`. Required for size closure (48 + 4 + 24 = 76 bytes). Reserved or populated by an out-of-ctor path. `[debugger-confirm: access scan for GHTex+0x30 reads/writes outside ctors needed]` |
| `+0x34` | 4 | `int32_t` | `m_nSchedulerIndex` | 1-based slot index in the `FrameTickScheduler` primary ring. Assigned on registration; `-1` if inactive. |
| `+0x38` | 4 | `int32_t` | `m_nUnknown` | Initialized to `-1`. Purpose not resolved on the examined paths. |
| `+0x3C` | 4 | `D3DFORMAT` | `m_dwFormat` | Direct3D texture format (e.g. `D3DFMT_A8R8G8B8`). |
| `+0x40` | 4 | `LPDIRECT3DTEXTURE9` | `m_pD3DTexture` | Pointer to the active Direct3D texture interface. `NULL` if unloaded. |
| `+0x44` | 4 | `uint32_t` | `m_dwTextureVramSize` | Specific VRAM allocation footprint in bytes. Zero-initialized by constructor; no additive writer found in static analysis (see §9, open question). |
| `+0x48` | 4 | `D3DXOptions*` | `m_pD3DXOptions` | Pointer to texture loading configuration. Default points to the options sentinel at `FrameTickScheduler+0x11958`; a handle pointing there signals no dedicated options block. |

---

## 4. Virtual Table Definitions

### 4.1 `GHandle` Virtual Table

Shared base vtable; slots 1–3 are pure-virtual and overridden by each concrete subclass.

| Slot | Byte Offset | Symbol |
|:---:|:---:|:---|
| 0 | `+0x00` | `Diamond_GHandle_deleting_dtor` |
| 1 | `+0x04` | pure-virtual `Load` |
| 2 | `+0x08` | pure-virtual `LoadWrapper` |
| 3 | `+0x0C` | pure-virtual `Unload` |

### 4.2 `GHTex` Virtual Table

Overrides slots 1–3. Slot 3 (`GHTex_Unload`) is the entry point invoked by `FrameTickScheduler` on idle
handles during the per-frame tick.

| Slot | Byte Offset | Symbol |
|:---:|:---:|:---|
| 0 | `+0x00` | `Diamond_GHTex_scalar_deleting_dtor` |
| 1 | `+0x04` | `GHTex_Load` |
| 2 | `+0x08` | `Diamond_GHTex_LoadWrapper` |
| 3 | `+0x0C` | `GHTex_Unload` |

---

## 5. GTextureManager Container Layout (16 bytes, `0x10`)

The manager object is a thin host around an embedded MSVC 7.1 `std::map`. The cache methods receive `this`
pointing at the start of the object. There is no class vtable on the cache path; `+0x00` is an
owner/header slot whose meaning depends on the host singleton that embeds the map.

| Offset | Size (Bytes) | Type | Field | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `void*` | owner slot | Host-singleton header; read but not written by the cache methods. |
| `+0x04` | 4 | struct | comparator cell | MSVC `std::map` internal comparator/allocator functor (empty functor, occupies one dword). |
| `+0x08` | 4 | `RBNode*` | `_Myhead` | Pointer to the tree's nil/head sentinel node (the past-the-end marker). |
| `+0x0C` | 4 | `uint32_t` | `_Mysize` | Number of live entries currently in the map. |

There is **no hash table and no bucket array**; the complete tree is a standard MSVC 7.1 balanced RB tree.

### 5.1 Red-Black Tree Node (24 bytes, `0x18`)

Each node holds one `(textureId, GTexture*)` key-value pair.

| Offset | Size (Bytes) | Type | Field | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `RBNode*` | `_Left` | Left child pointer. |
| `+0x04` | 4 | `RBNode*` | `_Parent` | Parent pointer. |
| `+0x08` | 4 | `RBNode*` | `_Right` | Right child pointer. |
| `+0x0C` | 4 | `uint32_t` | key | **Texture id** — the numeric id compared as `searchKey < node.key` (unsigned). |
| `+0x10` | 4 | `GTexture*` | value | Pointer to the refcounted cache wrapper for this texture id. |
| `+0x14` | 1 | `uint8_t` | color | Red/black colour flag. |
| `+0x15` | 1 | `uint8_t` | isnil | Set to `1` on the head/sentinel node; find and insert loops stop when this is set. |
| `+0x16` | 2 | — | padding | Alignment padding. |

**Key derivation.** The list-file loader parses each line, extracts the leading digit token, and converts it
with `atol` to produce the `uint32_t` texture id. The remainder of the line (prefixed with a base path) is
passed as the filesystem path to the `GHTex` constructor. The cache key is the texture id; the path is
incidental to the map. This is the runtime realisation of the "numeric-id texture registry" described in
`Docs/RE/formats/texture.md`.

**Lookup.** Standard RB lower-bound walk. "Not found" is signalled when the returned node's link equals
`_Myhead`.

**Cache hit.** Returns the node's `GTexture*` value directly. No refcount increment occurs on a hit; the
refcount is incremented only at first insertion.

### 5.2 Load Pipeline (`Diamond_GTextureManager_GetTexture`)

The sole cache-fill entry point. Drives the sequence from cache-miss to a loaded and scheduler-registered
handle.

1. Performs an RB lower-bound walk in `m_mapTextures` by numeric `textureId`.
2. **Cache hit:** returns the cached `GTexture*` wrapper directly; no refcount increment.
3. **Cache miss:**
   a. Allocates 76 bytes on the heap for a `GHTex` handle; constructs it (populates `m_strPath`; all flags
      default via `GHandle__ctor`).
   b. If loading immediately, calls `GHTex_Load`:
      - If the VFS is mounted: loads via `D3DXCreateTextureFromFileInMemoryEx` from the in-memory VFS buffer.
      - If the VFS is not mounted: loads from loose files via `D3DXCreateTextureFromFileExA`.
      - On success: sets `m_bLoaded = 1` and writes `m_dwLastAccessTime = GetTickCount()`.
   c. Allocates 60 bytes for a `GTexture` wrapper; sets refcount to `1` and writes `m_pGHTex`.
   d. Inserts the `GTexture*` into `m_mapTextures` keyed by `textureId`.

### 5.3 Manager Shutdown

On manager destruction:
- Traverses all live RB-tree nodes in `m_mapTextures`.
- Invokes the deleting destructor for each `GHTex` handle, which calls `FrameTickScheduler_Free` and
  releases the Direct3D texture interface.
- Invokes the deleting destructor for each `GTexture` wrapper.
- Deallocates the tree sentinel (nil/head) node.

---

## 6. GTexture Cache Wrapper Layout (60 bytes, `0x3C`)

Class hierarchy: `GObject` → `GRenderState` → `GTexture`. The vtable is the `GTexture`-level render-state
vtable. The `GObject` base embeds a 28-byte MSVC `std::string` name at `+0x08`.

| Offset | Size (Bytes) | Type | Field | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `void*` | vtable | `GTexture`-level vtable pointer (render-state object). |
| `+0x04` | 4 | `int32_t` | refcount (L1) | Per-entry reference count (`GObject` base use-count). Zeroed by ctor; incremented to `1` on first cache insert. Callers manage subsequent releases; no increment on cache hit. |
| `+0x08` | 28 | `std::string` | name | `GObject` object name (4-byte header, 16-byte SSO buffer, `+0x18` length, `+0x1C` capacity). |
| `+0x24` | 4 | `int32_t` | link slot | `GRenderState` list or link slot (ctor-zeroed). |
| `+0x28` | 4 | `int32_t` | state-type tag | `GRenderState` state-type tag. Constructed with value **7** for `GTexture`. |
| `+0x2C` | 4 | — | reserved | `GRenderState` field; not exercised on the cache path. |
| `+0x30` | 4 | — | reserved | `GRenderState` field; not exercised on the cache path. |
| `+0x34` | 4 | `GHTex*` | handle | Pointer to the 76-byte runtime texture object (see §3). |
| `+0x38` | 4 | `uint32_t` | cache key copy | Copy of the numeric `textureId` used as the map key. Written on cache insert. |

---

## 7. FrameTickScheduler Layout (~72 068 bytes)

A **single** static-storage instance. A pointer global holds its address, assigned once at static
initialisation. All GHTex subscribers share one clock and one ring through this object. The scheduler is a
plain (non-polymorphic) struct with no vtable. Polymorphism sits on the *subscribers*: each ring entry is a
`GHTex*` whose vtable slot `+0x0C` (`GHTex_Unload`) the tick invokes on idle handles.

The three parallel arrays begin immediately after the 4-byte reserved header and are laid out back-to-back.
Slot indices are 1-based in the primary and back-index arrays; the effect ring uses 0-based indexing. Null
(tombstoned) slots are skipped by the tick without decrementing counts.

| Offset | Size (Bytes) | Type | Field | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00000` | 4 | `void*` | reserved header | Slot 0; not used by register, free, or tick paths. |
| `+0x00004` | 23 996 | `GHTex*[5999]` | primary ring `[1..5999]` | Every `GHTex` handle constructed registers here. The assigned 1-based slot index is stored at `GHTex+0x34` (`m_nSchedulerIndex`). |
| `+0x05DC0` | 4 | `int32_t` | primary count | High-water mark of the primary ring; clamped to 5999 on register. |
| `+0x05DC4` | 4 | — | reserved | Back-index array preamble slot; unused. |
| `+0x05DC8` | 23 996 | `int32_t[5999]` | back-index `[1..5999]` | Maps each primary-ring slot to its paired effect-ring slot; used during slot teardown to locate and tombstone the effect-ring peer. |
| `+0x0BB84` | 24 000 | `GHTex*[6000]` | effect ring `[0..5999]` | Only handles with a non-zero idle interval register here; this is the array the per-frame tick sweeps. |
| `+0x11944` | 4 | `int32_t` | effect count | High-water mark of the effect ring; clamped to 5999 on register. Zeroed by ctor. |
| `+0x11948` | 4 | `int32_t` | cursor | Round-robin sweep index into the effect ring; persistent across frames. |
| `+0x1194C` | 4 | `int32_t` | batch size | Per-frame work budget: `(int)(effectCount × 0.011)` (approximately 1.1% of the ring). Recomputed when cursor wraps back to 0. |
| `+0x11950` | 1 | `uint8_t` | full-sweep flag | When set, the next tick processes the entire effect ring once then clears the flag. |
| `+0x11954` | 4 | `uint32_t` | scheduler clock | `Time_GetMs()` stamped at the start of each tick; the shared "now" used for all idle-interval comparisons. |
| `+0x11958` | 4 | — | options sentinel anchor | Default target for `GHTex+0x48` (`m_pD3DXOptions`). A handle pointing here signals "no dedicated options block allocated". |
| `+0x1195C` | 4 | `int32_t` | reserved | Ctor-zeroed. |
| `+0x11960` | 4 | `int32_t` | reserved | Ctor-zeroed. |
| `+0x11964` | 4 | `int32_t` | reserved | Ctor-zeroed. |
| `+0x11968` | 4 | `int32_t` | reserved | Ctor-initialised to `1`; purpose not pinned on the examined paths. |
| `+0x1196C` | 4 | `int32_t` | scratch cursor A | Ctor-initialised to `−1`; not observed in use by register, free, or tick paths. |
| `+0x11970` | 4 | `int32_t` | scratch cursor B | Ctor-initialised to `−1`; not observed in use by register, free, or tick paths. |
| `+0x11974` | 4 | `int32_t` | reserved | Ctor-zeroed. |
| `+0x11978` | 4 | `int32_t` | reserved | Ctor-zeroed. |
| `+0x1197C` | 4 | `int32_t` | reserved | Ctor-zeroed. |
| `+0x11980` | 1 | `uint8_t` | enable flag | Ctor-initialised to `1`; guards the tick entry path. |

**Object end: approximately `+0x11984` (≈ 72 068 bytes).**

### 7.1 Ring Mechanics

**Register** (`FrameTickScheduler_Register`). Increments the primary count (clamped below 6000) and stores
the handle pointer in the primary ring; the assigned 1-based slot index is returned and stored at
`GHTex+0x34` (`m_nSchedulerIndex`). If the handle's idle interval (`GHTex+0x24`, `m_dwTimeout`) is
non-zero, the method also increments the effect count, stores the handle in the effect ring, and writes the
effect-ring index into `backindex[primarySlot]`. Handles with idle interval `0` are pinned-resident and do
not enter the effect ring.

**Per-frame tick** (`FrameTickScheduler_TickAll`, called from the engine logic-tick phase). Stamps the
scheduler clock with `Time_GetMs()`. Computes or retrieves the batch size. Walks `batchSize` consecutive
entries of the effect ring starting from the cursor, wrapping when the index exceeds the effect count; null
(tombstoned) slots are skipped. For each live entry it calls the per-subscriber due-check. Advances and
saves the cursor. At the 1.1%-per-frame rate the entire effect ring is covered roughly every 91 frames.

**Per-subscriber due-check** (`FrameTickSubscriber_TickIfDue`). Fires only when the handle's two enable
flags are set and `schedulerClock − handle.lastAccessTime(GHTex+0x28) > handle.idleInterval(GHTex+0x24)`.
On fire it invokes `GHTex_Unload` through the subscriber vtable slot `+0x0C`. An idle interval of `0` means
pinned-resident: the condition cannot be satisfied.

**Free** (`FrameTickScheduler_Free`, called from the `GHTex` destructor with slot = `GHTex+0x34`).
Tombstones `primary[slot] = null`. Reads `backindex[slot]` to find the effect-ring peer and tombstones
`effect[e] = null`. **Counts are not decremented and slots are not recycled.** The tick loop skips null
entries, so tombstoned slots accumulate silently across the lifetime of the scheduler.

**Access restamp.** The texture acquire path and the manager's load-now branch write
`handle.lastAccessTime = schedulerClock`, so any texture accessed within its idle interval is never evicted.

---

## 8. Two-Level Lifetime Model

The system maintains two independent lifetime layers that together govern cache sharing and GPU residency.

| Level | Mechanism | Scope | Release action |
|:---|:---|:---|:---|
| L1 | `GTexture.refcount` (`+0x04`) | Cache-entry sharing — one wrapper per `textureId` | All references released → remove wrapper from the map and destruct both `GTexture` and `GHTex`. |
| L2 | Global device counters (live-texture count + VRAM byte accumulator) | GPU / VRAM residency | Idle eviction releases the Direct3D texture interface and adjusts both counters, but leaves the `GTexture` wrapper and its map entry alive for lazy reload. |

**Idle eviction** invokes `GHTex_Unload`, which calls `Release()` on the Direct3D texture pointer, zeros the
pointer, clears the loaded flag, and subtracts the per-texture VRAM byte size (`GHTex+0x44`,
`m_dwTextureVramSize`) from the VRAM byte accumulator. The live-texture count is decremented at the same
time. The map entry and `GTexture` wrapper survive; the next access triggers a fresh load.

**Global device counters** are two distinct BSS-segment globals: a 32-bit live-texture count (incremented
on a successful texture load, decremented on unload or eviction) and a 32-bit VRAM byte accumulator
(decremented by the per-texture VRAM byte size on unload or eviction). They reside alongside the cached
Direct3D device reference (lazily populated from the renderer singleton on first load) and the scheduler
pointer global (assigned once at static initialisation).

---

## 9. Open Questions

- **VRAM accumulator increment absent in static image — vestigial.** The decrement path (on
  unload/eviction, subtracting `m_dwTextureVramSize`) is the sole cross-reference to the accumulator
  in the entire binary; exhaustive static cross-reference analysis finds no additive (`+=`) writer
  anywhere. `m_dwTextureVramSize` (`GHTex+0x44`) is zero-initialised by the constructor and no
  examined path writes it. The accumulator is therefore one-sided and vestigial in the shipped binary.
  Residual `[debugger-confirm]`: whether `GHTex+0x44` ever holds a non-zero value at runtime
  (populated through an aliased write not visible to static cross-reference analysis).
- **Scratch cursors at `+0x1197C` / `+0x11980` and the `+0x11978` field.** Ctor-initialised to `−1`, `−1`,
  and `1` respectively; none were observed on the register, free, or tick paths. Possibly a never-engaged
  free-list head/tail or a debug artefact; semantics unconfirmed.
- **Manager `+0x00` (owner slot).** Read but not written by the cache methods; its meaning depends on the
  host singleton that embeds the manager — deferred to the singleton/owner lane.
- **`GTexture +0x2C` / `+0x30`.** `GRenderState` fields not exercised on the cache path; recorded as
  reserved until the render-state lane maps them.
- **Scheduler clock units.** The clock at `+0x11964` is `Time_GetMs()`, and the default idle interval
  (`GHTex+0x24`) is 180 000, giving a 3-minute idle timeout. This reading is well-supported by a single
  stamp source but has not been confirmed against a live debugger session reading the field over time.
- **GHTex `+0x30` gap.** Ctor-uninitialised by both `GHandle__ctor` and `GHTex__ctor`; required for size
  closure. Access scan needed to determine whether an out-of-ctor path writes it. `[debugger-confirm]`
- **GHandle `m_dwVramSize` (`+0x2C`) vs `GHTex m_dwTextureVramSize` (`+0x44`).** Both fields exist; their
  relationship (one a base-class placeholder, one the concrete footprint?) is not confirmed.
