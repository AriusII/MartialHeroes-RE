---
status: code-confirmed
sample_verified: false   # offsets recovered from binary analysis; one reconciliation item open
subsystems: [resource_pipeline, world_systems]
---

# Struct: Terrain Streamer (Terrain Manager) — Field Offset Table

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room runtime comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets below are byte offsets relative to the start of the object** —
> they are file/struct offsets, NOT memory addresses, and must never be treated as such.
> Behaviour of the streaming lifecycle is in `specs/terrain-streaming.md`. Citing engineers:
> `// spec: Docs/RE/structs/terrain-manager.md`.
>
> **Confidence vocabulary:** CODE-CONFIRMED (recovered and corroborated across multiple sites);
> MED (structurally inferred; flagged); UNVERIFIED (hypothesis).

---

## Object identity

A single terrain-streamer instance (a process-wide singleton) owns the whole streaming spine: the
(dormant) async thread + request FIFO, the fixed 25-slot cell pool, the 5×5 ring index, the center
cell, the area cell-key set, the stream radius, the camera-frustum scratch, and the spawn cell. The
streaming behaviour that reads/writes these fields is specified in `specs/terrain-streaming.md`.

---

## Streamer object — field table

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0  | 4 | void* | `worker_thread_slot_a` | Thread-slot; holds the async worker entry. **Worker is DORMANT** (no FIFO producer ships). | CODE-CONFIRMED |
| +4  | 4 | void* | `worker_thread_slot_b` | Zeroed at construction (handle / param slot). | CODE-CONFIRMED |
| +8  | 4 | void* | `worker_thread_slot_c` | Zeroed at construction. | CODE-CONFIRMED |
| +12 | 1 | bool  | `worker_keep_running` | Set to 1 at construction; the worker outer loop runs while set. | CODE-CONFIRMED |
| +16 | 4 | ptr   | `fifo_list_head` | Ordered-list sentinel head for the request FIFO. **DORMANT — no producer in this build.** | CODE-CONFIRMED |
| +20 | 4 | ptr   | `fifo_list_second_word` | Second list-header word of the FIFO sub-struct. | CODE-CONFIRMED |
| +24 | 4 | int   | `fifo_count` | FIFO size; the worker only drains when non-zero — nothing ever increments it. **DORMANT.** | CODE-CONFIRMED |
| +28 | 4 | HANDLE| `fifo_mutex` | FIFO guard mutex (zero at construction); the worker waits on it. **DORMANT.** | CODE-CONFIRMED |
| +32 | 4 | int   | `resident_count` | Slot count scanned by the cache lookup, the slot recycler, and the post-shift cull. See **Open item**. | CODE-CONFIRMED |
| +36 | 4 | cell* | `slot_scan_base` | First slot scanned by the cache/recycle scan (iterates `resident_count` entries from here). See **Open item**. | CODE-CONFIRMED |
| +44 | 25×4 | cell* [25] | `ring_slots` | The **5×5 ring** pointer array (25 cell pointers, `+44`..`+143`). Zeroed wholesale by the ring-clear routine. | CODE-CONFIRMED |
| +68..+116 | (subset) | cell* | `ring_inner_3x3` | The inner **3×3** subset of the ring used by the 3×3 path and cold-start (the 9 slots around the center within the 25-slot array). | CODE-CONFIRMED |
| +92 | 4 | cell* | `center_cell` | The middle slot of the 5×5 ring; the shift keeps the loaded center cell here. | CODE-CONFIRMED |
| +144 | 4 | int  | `active_area_id` | Stored by the cold-start ring fill; passed as the areaId to the per-cell loader. | CODE-CONFIRMED |
| +148 | 24 | f32[6] | `camera_frustum_corners` | Camera frustum corners pushed by the stream-radius update and read by the frustum-plane build. | CODE-CONFIRMED |
| +176 | 4 | int  | `center_mapX` | Center-cell map-X index; written by the center-cell setter. | CODE-CONFIRMED |
| +180 | 4 | int  | `center_mapZ` | Center-cell map-Z index; written by the center-cell setter. | CODE-CONFIRMED |
| +184 | ~12 | ordered-set | `area_cell_keys` | Ordered set (RB-tree) of valid cell keys (`mapZ + 100000·mapX`) for the active area. Filled from the `.lst` manifest; gates the per-cell loader. | CODE-CONFIRMED |
| +188 | 4 | node* | `area_cell_keys_end` | End-sentinel of the area cell-key set; the loader compares its lower-bound result against this. | CODE-CONFIRMED |
| +252..+272 | 24 | f32[6] | `camera_pose` | Camera pose (eye/direction) pushed by the stream-radius update; read by the frustum build. | CODE-CONFIRMED |
| +356 | 4 | f32  | `stream_radius` | Ring-size selector. Clamped (an upper clamp at the high end maps to 1000); a value > 1000 selects the 5×5 ring, otherwise 3×3. | CODE-CONFIRMED |
| +464 | 4 | int  | `spawn_mapX` | Spawn cell map-X; stored by the spawn-init step. | CODE-CONFIRMED |
| +468 | 4 | int  | `spawn_mapZ` | Spawn cell map-Z; stored by the spawn-init step. | CODE-CONFIRMED |

### Quick-reference (offsets the streaming spec leans on)

- **Ring slot array:** `+44` (25 pointers, the 5×5 grid).
- **Center cell:** `+92` (middle slot).
- **Center indices:** `+176` (mapX) / `+180` (mapZ).
- **Area cell-key set:** `+184`.
- **Stream radius float:** `+356`.
- **Spawn cell:** `+464` (mapX) / `+468` (mapZ).
- **Per-cell 9-sub-manager array (on the cell object, not the streamer):** `+16296`..`+16328`.

---

## Per-cell (streamed-cell) object — field table

These offsets are relative to a streamed-cell object (one of the pooled slots), **not** the
streamer. They are the fields the streamer reads to match, attach, detach, and recycle a cell.

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +256 | 256 | u8[16][16] | `subtile_dirty_grid` | 16×16 per-sub-tile flag grid walked during cell detach. | CODE-CONFIRMED |
| +16252 | 4 | int | `mapX` | Cache-match key; world-bbox origin base. | CODE-CONFIRMED |
| +16256 | 4 | int | `mapZ` | Cache-match key. | CODE-CONFIRMED |
| +16260 | 4 | int | `areaId` | Cache-match key. | CODE-CONFIRMED |
| +16268 | 4 | f32 | `elevation_feed` | Read into the post-shift height/elevation feed. | CODE-CONFIRMED |
| +16296..+16328 | 9×4 | mgr* [9] | `sub_managers` | **The fixed array of 9 per-cell sub-manager pointers** (attach/detach fan-out). Identities split between the collision/grid lane and the env/water/FX lane — left **opaque** here. | CODE-CONFIRMED |
| +24708 | 1 | bool | `loaded_flag` | Set on load/cache-hit; cleared on cull/recycle. The **recycle predicate** the slot allocator and the cull both test. | CODE-CONFIRMED |

---

## Open item (the one unresolved reconciliation)

**Confidence: MED — flagged, do not assume.**

The cache/recycle scan iterates the resident-slot array from `slot_scan_base` (`+36`) for
`resident_count` (`+32`) entries, while the ring-clear routine zeroes the 25 spatial slot pointers
starting at `ring_slots` (`+44`). These are almost certainly **two views of the same fixed 25-cell
pool**:

- one possibility: a small pool header `{ count @ +32, first-slot @ +36 }` followed by the
  pointer storage, with the `+44` array being the 5×5 *spatial index* into that same pool; or
- the resident pool and the 5×5 ring are the same 25 pointers read from two slightly different
  bases.

This is the single open struct question. **An engineer should treat the pool as one 25-cell array
and must not assume two independent arrays.** A raw live-layout read settles it (deferred).

---

## Cross-references

- Streaming lifecycle behaviour: `specs/terrain-streaming.md`.
- World / scene singletons: `structs/runtime_singletons.md`.
- Terrain cell-blob / heightmap formats: `formats/terrain.md` (asset-format lane).
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
