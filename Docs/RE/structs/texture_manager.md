# Struct: GTextureManager / GTexture / FrameTickScheduler (Texture cache container system)

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

**Scope.** This spec covers the two *container* objects — `GTextureManager` (the dedup map and its RB-tree
node layout) and `FrameTickScheduler` (the streaming ring) — together with the complete `GTexture`
cache-wrapper layout. For the per-texture runtime handle (`GHandle` / `GHTex`, 76 bytes) see
`Docs/RE/structs/ghtex.md`. For the binary format of the texture list files that feed the loader see
`Docs/RE/formats/texture.md`.

**Wave-2 corrections to `ghtex.md` (wave-1).** The wave-1 findings in `ghtex.md §1.3` and `§1.4` should
be read with the following corrections, which this spec supersedes:

- The dedup map is `std::map<uint32_t textureId, GTexture*>` — the key is a **32-bit numeric texture id**,
  not a path string. `ghtex.md §1.3` (`m_pKey` "typically the pointer to the path") and `§1.4`
  (`std::map<void*, GTexture*>`) reflect the wave-1 reading.
- The refcount on `GTexture` increments only at first cache insert. There is no refcount bump on a cache
  hit; callers manage subsequent releases.
- The scheduler is a **single** static-storage object. The scheduler pointer global and the BSS object it
  references are the same instance (the pointer is set once at static initialisation). All GHTex
  subscribers and the per-frame tick share one clock and one ring.

---

## 1. Object Ownership

```
GTextureManager  (witnessed instance = id-registry singleton)
   std::map<uint32_t textureId, GTexture*>  (red-black tree; dedup + L1 refcount)
     └── GTexture  (60 B cache wrapper; refcount L1; see §3)
           └── GHTex  (76 B runtime handle; see Docs/RE/structs/ghtex.md)
                 └── registers one slot in FrameTickScheduler

FrameTickScheduler  (single static-storage instance, ~72 KB; see §4)
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

## 2. GTextureManager Container Layout (16 bytes, `0x10`)

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

### 2.1 Red-Black Tree Node (24 bytes, `0x18`)

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

---

## 3. GTexture Cache Wrapper Layout (60 bytes, `0x3C`)

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
| `+0x34` | 4 | `GHTex*` | handle | Pointer to the 76-byte runtime texture object (see `Docs/RE/structs/ghtex.md`). |
| `+0x38` | 4 | `uint32_t` | cache key copy | Copy of the numeric `textureId` used as the map key. Written on cache insert. |

---

## 4. FrameTickScheduler Layout (~72 068 bytes)

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
| `+0x00004` | 23 996 | `GHTex*[5999]` | primary ring `[1..5999]` | Every `GHTex` handle constructed registers here. The assigned 1-based slot index is stored at `GHTex+0x34` (`m_nSchedulerIndex` in `ghtex.md`). |
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

### 4.1 Ring Mechanics

**Register** (`FrameTickScheduler_Register`). Increments the primary count (clamped below 6000) and stores
the handle pointer in the primary ring; the assigned 1-based slot index is returned and stored at
`GHTex+0x34`. If the handle's idle interval (`GHTex+0x24`, `m_dwTimeout` in `ghtex.md`) is non-zero, the
method also increments the effect count, stores the handle in the effect ring, and writes the effect-ring
index into `backindex[primarySlot]`. Handles with idle interval `0` are pinned-resident and do not enter
the effect ring.

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

## 5. Two-Level Lifetime Model

The system maintains two independent lifetime layers that together govern cache sharing and GPU residency.

| Level | Mechanism | Scope | Release action |
|:---|:---|:---|:---|
| L1 | `GTexture.refcount` (`+0x04`) | Cache-entry sharing — one wrapper per `textureId` | All references released → remove wrapper from the map and destruct both `GTexture` and `GHTex`. |
| L2 | Global device counters (live-texture count + VRAM byte accumulator) | GPU / VRAM residency | Idle eviction releases the Direct3D texture interface and adjusts both counters, but leaves the `GTexture` wrapper and its map entry alive for lazy reload. |

**Idle eviction** invokes `GHTex_Unload`, which calls `Release()` on the Direct3D texture pointer, zeros the
pointer, clears the loaded flag, and subtracts the per-texture VRAM byte size (`GHTex+0x44`,
`m_dwTextureVramSize` in `ghtex.md`) from the VRAM byte accumulator. The live-texture count is decremented
at the same time. The map entry and `GTexture` wrapper survive; the next access triggers a fresh load.

**Global device counters** are two distinct BSS-segment globals: a 32-bit live-texture count (incremented
on a successful texture load, decremented on unload or eviction) and a 32-bit VRAM byte accumulator
(decremented by the per-texture VRAM byte size on unload or eviction). They reside alongside the cached
Direct3D device reference (lazily populated from the renderer singleton on first load) and the scheduler
pointer global (assigned once at static initialisation).

---

## 6. Open Questions

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
