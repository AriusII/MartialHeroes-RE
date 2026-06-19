---
verification: confirmed          # control-flow + operand facts from static IDA (no debugger)
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida]           # heap layout, not a file format → no vfs-sample tier applies here
sample_verified: false           # object layout is runtime heap, not a packed-file format
subsystems: [resource_pipeline, world_systems]
conflicts: none-open             # the prior "Open item" is RESOLVED (two objects, not one)
# CORRECTED CYCLE 1 (ida_anchor 263bd994): named the 9 per-cell render sub-manager slots
# (slot0 ground texture grid, slot1 building/object grid, slots2-8 = fx1..fx7 overlays); recorded
# that the 34-slot loader pool OWNS live cells while the 25-slot manager ring is a borrowed-pointer
# 5x5 spatial view (live centre = ring slot 12). [2026-06-19]
---

# Structs: Terrain Loader (streamer) + Terrain Manager — Field Offset Tables

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets below are byte offsets relative to the start of the object** —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> Behaviour of the streaming lifecycle is in `specs/terrain-streaming.md`. Citing engineers:
> `// spec: Docs/RE/structs/terrain-manager.md`.
>
> **Confidence vocabulary (per the campaign banner tiers):** `[confirmed]` = recovered from static
> control-flow + operand analysis and corroborated across multiple sites; `[static-hypothesis]` =
> structurally inferred or seen only as a zero-init, NOT independently re-isolated this pass (do not
> assume); `[sample-verified]` = additionally matched against a real VFS sample — **does not apply
> here** because this is in-memory heap layout, not a packed-file format. No item here is
> debugger-confirmed (static-only run); a live read of the two singletons would settle the
> remaining `[static-hypothesis]` rows.

---

## Object identity — TWO singletons, not one

The terrain streaming spine is owned by **two distinct process-wide singletons that point at each
other**, NOT a single merged object. The prior revision of this spec conflated them; every offset
below is now attributed to the object that actually owns it.

| Object | Role | What it owns |
|--------|------|--------------|
| **TerrainLoader** ("the streamer") | The async load engine. | The (dormant) worker thread, the request FIFO (`std::list`), the load **Event** handle, the **34-slot** cell pool (count + pointer array), and the area cell-key red-black-tree set. |
| **TerrainManager** | The grid/render front object. | A pointer to the streamer (`+0`), a texture-preload singleton pointer (`+4`), 9 grid/render sub-objects (`+8`..`+40`), the **25-slot ring** (`+44`), the center cell (`+92`), **two view-frustum objects** (`+148`, `+360`), the camera pose, the stream-radius float (`+356`), and the map-option/region values (`+464`/`+468`). |

**The pool and the ring are two different arrays on two different objects.** TerrainLoader's pool
(`+36`) holds **34** cell pointers; TerrainManager's ring (`+44`) holds **25** cell pointers. Both
index into the **same 34 heap-allocated cell objects**, via two independent pointer arrays. (This
dissolves the old "Open item" reconciliation — see **Resolved item** below.)

> **Ownership rule (CYCLE 1).** The **34-slot loader pool is the single authoritative OWNER** of
> every live cell object — cells are allocated, key-matched, loaded, recycled and freed there. The
> **25-slot manager ring is a borrowed-pointer VIEW** — a 5×5 spatial window of pointers INTO the
> pool, rotated one step at a time as the player moves; it never moves cell objects, only re-points
> its window slots. The **live centre cell** the player stands on is **ring slot 12** (the manager's
> center-cell pointer, `+92`). See **Pool ownership vs ring view** below.

> **Heap-layout caveat.** These are in-memory C++ object layouts recovered statically (no debugger).
> The field roles and offsets are `[confirmed]` from control-flow + operand evidence where marked;
> rows marked `[static-hypothesis]` are inferred and must not be assumed.

---

## TerrainLoader (the streamer) — field table

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0  | 4 | void* | `worker_thread_slot` | Thread-slot installed by the ctor with the streaming-worker entry. **Worker is DORMANT** (see `+12`). | confirmed |
| +4  | 4 | void* | `worker_thread_slot_b` | Plausible handle/param slot; not directly confirmed as a separately zeroed word. | static-hypothesis |
| +8  | 4 | void* | `worker_thread_slot_c` | Plausible handle/param slot; not directly confirmed as a separately zeroed word. | static-hypothesis |
| +12 | 1 | bool  | `worker_keep_running` | Set to 1 by the ctor; the worker outer loop runs while set. **TerrainLoader::init immediately RE-CLEARS it to 0**, so the worker exits at once — the strongest evidence the streaming thread is intentionally **DORMANT** in this build. | confirmed |
| +16 | 4 | ptr   | `fifo_list_base` | Base of a circular doubly-linked list (`std::list`); the FIFO init allocates a self-linked sentinel. **DORMANT — no producer in this build.** | confirmed |
| +20 | 4 | ptr   | `fifo_list_sentinel` | The list sentinel-node pointer; the worker dereferences it to read the head node, and pop compares against it as the end marker. | confirmed |
| +24 | 4 | int   | `fifo_count` | List element count; the worker breaks the drain loop when this is 0 and pop decrements it. Nothing ever increments it. **DORMANT.** | confirmed |
| +28 | 4 | HANDLE| `load_event` | **Load Event handle** created by `CreateEventA` in TerrainLoader::init (zeroed by the ctor first). The worker waits on it (`WaitForSingleObject`). It is the **event gate**, NOT a FIFO mutex — the real serialization is on two **file-scope critical sections** (one for the FIFO, one for cell load), which are globals, not object fields. | confirmed |
| +32 | 4 | int   | `pool_count` | Cell-pool slot count. **Initialized to 34** by TerrainLoader::init. Scanned by the cache lookup and the slot recycler (both iterate `< pool_count`). | confirmed |
| +36 | 34×4 | cell* [34] | `pool_slots` | The fixed **34-cell pool** pointer array; init fills 34 cell pointers from here. The cache/recycle scans iterate `pool_count` entries from this base. **This pool is the authoritative owner of live cells** (allocate / key-match / load / recycle / free). | confirmed |
| +184 | ~16 | ordered-set | `area_cell_keys` | Ordered set (red-black tree) of valid cell keys (`mapZ + 100000·mapX`) for the active area; initialized by the ctor (a `std::set` header init: node alloc, color byte, self-linked L/R/parent, size 0). Membership **gates** the per-cell loader. | confirmed |
| +188 | 4 | node* | `area_cell_keys_end` | The set's end/header node; the loader's lower-bound result is compared against it (a hit on the end node means "not a valid cell key" → load fails). | confirmed |

**Notes:**
- The ctor also zeroes a small block around `+172`/`+176`/`+180`; only the zero-init is observed, not
  a write-site or role for those words (see TerrainManager note on `+176`/`+180`).

### Quick-reference (TerrainLoader)

- **Worker dormancy:** `+12` set 1 by ctor, cleared 0 by init.
- **FIFO:** base `+16`, sentinel `+20`, count `+24` (all dormant).
- **Load event:** `+28` (Event, not a mutex).
- **Cell pool:** count `+32` (= 34), pointer array `+36` — **the owner of live cells**.
- **Area cell-key set (RB-tree):** `+184`, end node `+188`.

---

## TerrainManager — field table

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0  | 4 | TerrainLoader* | `streamer_ptr` | Pointer to the TerrainLoader singleton (set from the streamer accessor). | confirmed |
| +4  | 4 | void* | `texture_preload_singleton` | Pointer to a second singleton (a texture-preload object). | confirmed |
| +8 .. +40 | 9×4 | mgr* [9] | `grid_render_sub_objects` | The 9 grid/render sub-objects, allocated at construction with a fixed set of distinct sizes (one large grid object plus several smaller render/index objects). Identities left opaque here. | confirmed |
| +44 | 25×4 | cell* [25] | `ring_slots` | The **5×5 ring** pointer array (25 cell pointers, `+44`..`+143`). Zeroed wholesale (a 100-byte `memset`) at construction. A different array from the streamer's 34-slot pool — both index the same 34 cells. **Holds BORROWED pointers into the pool; stored row-major (slot = 5·row + col); rotated one step per move.** | confirmed |
| +68..+116 | (subset) | cell* | `ring_inner_3x3` | The inner **3×3** subset of the ring (the 9 slots around the center within the 25-slot array). Plausible spatial sub-window; not independently isolated as a distinct field. | static-hypothesis |
| +92 | 4 | cell* | `center_cell` | The middle slot of the 5×5 ring (**ring slot 12** → `+44 + 48 = +92`). **The LIVE CENTRE cell the player stands on** — the point/height/pick query helpers read this slot to test against the live cell. A pointer borrowed from the 34-slot pool, re-pointed (not moved) as the ring rotates. | confirmed |
| +144 | 4 | int  | `active_area_id` | Area id associated with the ring fill; not re-confirmed this pass. | static-hypothesis |
| +148 | (object) | frustum | `view_frustum_a` | A **view-frustum object** (with vftable + body), constructed in place at this offset and rebuilt by the stream-radius update. **NOT a raw `f32[6]` corner array.** | confirmed |
| +176 | 4 | int  | `center_mapX?` | Lives on the **streamer**, not here (the ctor zero-inits `+172`/`+176`/`+180`). Only the zero-init is observed; the "center map-X index" role/write-site is NOT confirmed. | static-hypothesis |
| +180 | 4 | int  | `center_mapZ?` | As above — zero-init only; role unconfirmed. | static-hypothesis |
| +252..+268 | 5×4 | f32[5] | `camera_pose` | Camera pose read by the stream-radius update (5 floats at `+252`,`+256`,`+260`,`+264`,`+268`) and fed to the frustum build. Recovered as **5** floats, not a clean `[6]`. | confirmed |
| +356 | 4 | f32  | `stream_radius` | Ring-size selector, written by the set-radius path. **Upper clamp: if the value is ≥ 15000.0 it is set to 1000.0.** A larger radius selects the 5×5 ring, otherwise the 3×3 path. (Default radii by global mode: ~1800 / ~1000 / ~600.) | confirmed |
| +360 | (object) | frustum | `view_frustum_b` | A **second view-frustum object** (with vftable + body) adjacent to the camera-pose block. | confirmed |
| +464 | 4 | int  | `map_option_value` | **Map-option / region value**, written by the area-set step (from the environment map-set/load-area path). **NOT a spawn-cell map-X.** This area-level scalar is the home of the region/map-option — there is no per-cell region sub-object. | confirmed |
| +468 | 4 | int  | `region_value` | **Region value**, written by the same area-set step; later also **consumed as an optional stream-radius override**. **NOT a spawn-cell map-Z.** Area-level scalar (not a per-cell field). | confirmed |
| +472 | 4 | int  | `mode_flag_a` | Mode flag chosen by the radius-update (commonly 1; 0 for the special mode). | confirmed |
| +476 | 4 | int  | `mode_flag_b` | Mode flag chosen by the radius-update (commonly 4; 5 for the special mode). | confirmed |

### Quick-reference (TerrainManager — offsets the streaming spec leans on)

- **Streamer pointer:** `+0`; texture-preload singleton: `+4`.
- **9 grid/render sub-objects:** `+8`..`+40`.
- **Ring slot array:** `+44` (25 pointers, the 5×5 grid; borrowed pointers; row-major 5·row+col).
- **Center cell (ring slot 12 = the live centre):** `+92`.
- **View-frustum objects:** `+148` and `+360` (objects, not float arrays).
- **Camera pose:** `+252`..`+268` (5 floats).
- **Stream radius float:** `+356` (clamp: value ≥ 15000 → 1000).
- **Map-option / region values (area-level scalars):** `+464` / `+468` (`+468` doubles as a radius override).
- **Mode flags:** `+472` / `+476`.
- **Per-cell 9-sub-manager array (on the cell object, not the manager):** `+16296`..`+16328`.

---

## Per-cell (streamed-cell) object — field table

These offsets are relative to a streamed-cell object (one of the **34** pooled cells), **not** the
streamer or the manager. Each cell object is **24,712 bytes** (allocated with its own ctor; 34 of
them are allocated by TerrainLoader::init). They are the fields the streamer reads to match,
attach, detach, and recycle a cell.

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +16252 | 4 | int | `mapX` | Cache-match key #1; world-bbox origin base. | confirmed |
| +16256 | 4 | int | `mapZ` | Cache-match key #2. | confirmed |
| +16260 | 4 | int | `areaId` | Cache-match key #3. | confirmed |
| +16268 | 4 | f32 | `elevation_feed` | Read into the post-shift height/elevation feed; not re-confirmed at this offset this pass. | static-hypothesis |
| +16296..+16328 | 9×4 | mgr* [9] | `sub_managers` | **The fixed array of 9 per-cell render sub-manager pointers** (attach/detach fan-out). Confirmed exactly: cell detach walks all 9 pointers (`+16296`,`+16300`,`+16304`,`+16308`,`+16312`,`+16316`,`+16320`,`+16324`,`+16328`), destroying + freeing + nulling each; the cell ctor sets them up at the same offsets. **Slot identities (CYCLE 1):** slot 0 (`+16296`) = ground texture-patch grid; slot 1 (`+16300`) = building/object grid; slots 2..8 (`+16304`..`+16328`) = fx1..fx7 overlays. See **The 9 per-cell render sub-manager slots** below. | confirmed |
| +24708 | 1 | bool | `loaded_flag` | Cleared to 0 by the cell ctor; set to 1 on load-success and on cache-hit. The **recycle predicate** the slot allocator tests (it picks the first cell whose flag is 0) and the cull clears. | confirmed |

**Cell interior arrays (informational — not stream-control fields).** The cell ctor also builds
several interior arrays whose exact roles are not needed by the streamer's match/attach/detach path:
a 256-element ray-segment array at `+516` (24-byte stride), a 256-element array at `+7684`
(12-byte stride), and a 341-element array at `+10756` (16-byte stride), plus scalar fields around
`+16212`..`+16228`. These are documented as `[static-hypothesis]` interior structure only. (The
256-element ray-segment array at `+516` is the cell's 2D XZ wall-segment / ray-parity collision
geometry — see **Collision is not part of the 9-slot array** below.)

> **Downgraded:** the previous revision listed a `subtile_dirty_grid u8[16][16]` at cell `+256`.
> That field was **not observed at `+256`** this pass — the cell ctor's first interior array is the
> ray-segment array at `+516`. The `+256` 16×16 dirty-grid claim is unverified and has been removed
> from the confirmed table (kept here only as a residual note, see below).

---

## The 9 per-cell render sub-manager slots (CYCLE 1)

Each pooled cell holds a fixed array of **9 render sub-manager pointers** at cell offsets
`+16296`..`+16328` (one pointer per slot, 4 bytes apart). Their roles are proven by the call order of
the per-slot build functions in the cell-descriptor finalize tail and by the literal error strings
inside each build function ("fx1 texture index … < 1 || > max …", through "fx7 texture index …").
**All nine are per-cell texture-index / object-placement grids driven by the cell's `.map`
descriptor**; slot 0 additionally resolves its grid indices against the cell's per-cell texture list
(the `.ted`-style indices).

| Slot | Cell offset | Role | Ingested data | Confidence |
|-----:|------------:|------|---------------|------------|
| 0 | +16296 | **Terrain BASE ground texture-patch grid** (16×16). A large per-entry stride (a texture-patch record + a texture count field). Each grid index is clamped and then mapped through the cell's per-cell texture list. | The `.ted` texture-index grid resolved through the cell `.map` per-cell texture list. | confirmed |
| 1 | +16300 | **Mass / building-object placement grid** — the placed static-object / building layer for the cell. | The `.map` mass-object descriptor records. | confirmed |
| 2 | +16304 | **FX overlay texture layer 1 (fx1)** — a water/effect overlay texture grid (16×16, small per-entry stride, small count word). | The `.map` fx1 layer records. | confirmed |
| 3 | +16308 | **FX overlay texture layer 2 (fx2).** | The `.map` fx2 layer records. | confirmed |
| 4 | +16312 | **FX overlay texture layer 3 (fx3)** — a cell water texture-layer build. | The `.map` fx3 layer records. | confirmed |
| 5 | +16316 | **FX overlay texture layer 4 (fx4).** | The `.map` fx4 layer records. | confirmed |
| 6 | +16320 | **FX overlay texture layer 5 (fx5)** — a cell water texture-layer build. | The `.map` fx5 layer records. | confirmed |
| 7 | +16324 | **FX overlay texture layer 6 (fx6).** | The `.map` fx6 layer records. | confirmed |
| 8 | +16328 | **FX overlay texture layer 7 (fx7).** | The `.map` fx7 layer records. | confirmed |

So the 9-slot family is exactly **one ground texture-patch grid (slot 0) + one mass/building object
grid (slot 1) + seven FX overlay texture-layer grids (slots 2..8 = fx1..fx7)** — all driven by the
cell `.map` descriptor. The FX-layer attachment wiring (which `.map` section feeds which slot, and
when) is in `formats/terrain.md`; the layer byte layouts are in `formats/terrain.md` / `specs/effects.md`.

### Trailing companion grids (NOT part of the 9)

Two further sub-objects are built in the same finalize tail but are **not** members of the 9-slot
array: a 64-unit cell-bbox bucketing pass over a sub-grid (a companion spatial index of the cell into
64-unit tiles), and a second one of identical structure. They are spatial indices, not render layers.

### What is NOT in the 9-slot array

- **Collision is separate.** The cell's 2D XZ wall-segment / ray-parity collision geometry is the
  256-element ray-segment array at cell `+516`, together with the per-cell building/grid blobs the
  loader holds outside this array — NOT one of the 9 render slots.
- **Sound-grid is not a member** of the 9-slot array.
- **Region / map-option is an AREA-LEVEL scalar on the TerrainManager** (`+464` / `+468`), not a
  per-cell sub-object.

---

## Pool ownership vs ring view (CYCLE 1)

The two arrays are not symmetric — one owns, the other borrows:

- **The 34-slot loader pool (`+36` on TerrainLoader) OWNS the live cell objects.** It is the single
  authoritative owner. Cells are **allocated**, **key-matched** (on `mapX` / `mapZ` / `areaId`),
  **loaded**, **recycled** (the slot allocator picks the first cell whose `loaded_flag` is clear) and
  **freed** there. The find-or-load path scans this pool on a cache hit and acquires a free pool slot
  on a miss.
- **The 25-slot manager ring (`+44` on TerrainManager) is a borrowed-pointer 5×5 spatial VIEW.** It
  is a window of pointers INTO the pool, stored row-major (slot index = `5·row + col`), rotated one
  step at a time as the player walks. It never moves cell objects — it only re-points which pool cell
  each window slot references. A single-step recenter shifts the whole 5×5 window by one cell, so only
  the newly-exposed edge needs new cells loaded; the rest are still pointed-to in the pool.
- **The LIVE CENTRE cell is ring slot 12** (`+92`). The point/height/pick query helpers read the
  center slot to test against the cell the player currently stands on. Ring rotation changes which
  pool cell the centre slot points at; it never relocates a cell object.

> **Engineer model:** two arrays over one cell pool — a 34-cell pool (truth, keyed by
> `(mapX, mapZ, areaId)` with a loaded flag, frees recycle on `loaded==false`), and a moving 5×5 ring
> of pointers into that pool (centre = slot 12) that shifts one step per move; load the newly-exposed
> edge cells on demand, recycle the ones that fall out. Do **not** model it as a single 25-cell array.

---

## Key formula (confirmed)

- **Cell key = `mapZ + 100000·mapX`** (mapX is the X index, mapZ is the Z index). Confirmed exactly
  in the find-or-load path. Membership of this key in the area cell-key set (`TerrainLoader+184`)
  gates loading: a non-member key fails the load outright; only a member proceeds to the cache
  lookup, then slot acquire + load.

---

## Resolved item (the prior reconciliation, now closed)

The prior revision flagged an **Open item**: it suspected the `+32`/`+36` "resident pool" and the
`+44` "ring" were *two views of one 25-cell array*. **That framing is wrong and is now resolved.**

- The `+32`/`+36` pool is on **TerrainLoader** and holds **34** cell pointers — **the owner**.
- The `+44` ring is on **TerrainManager** (a different object) and holds **25** cell pointers — **a
  borrowed-pointer view**.
- They are **two independent pointer arrays on two different objects**, both indexing the **same 34
  heap-allocated cell objects**.

An engineer must model these as two separate arrays (pool = 34 owner, ring = 25 view) sharing the same
backing cell objects — **not** as one 25-cell array, and **not** as two slices of a single base.

---

## Residual / lower-confidence items (flagged)

The following are `[static-hypothesis]` — plausible but not independently re-isolated in this static
pass, and not assumable by engineers without further confirmation (a live read of the two singletons
would settle them):

- TerrainManager `+68`..`+116` inner 3×3 ring subset; `+144` active area id.
- The `+176`/`+180` words (zero-init only observed, on the streamer; "center map index" role
  unconfirmed).
- Cell `+16268` elevation feed.
- The removed cell `+256` `subtile_dirty_grid u8[16][16]` claim (not observed at `+256`).
- The `+4`/`+8` extra TerrainLoader worker slots (zeroing inferred, not directly confirmed).

(The `+92` center cell is now `[confirmed]` as the live centre — promoted out of this list in CYCLE 1.)

---

## Cross-references

- Streaming lifecycle behaviour: `specs/terrain-streaming.md`.
- World / scene singletons: `structs/runtime_singletons.md`.
- Terrain cell-blob / heightmap formats: `formats/terrain.md` (asset-format lane).
- Area cell census + per-cell fan-out: `formats/area_inventory.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
