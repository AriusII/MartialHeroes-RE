---
status: code-confirmed
sample_verified: false   # static comprehension; live debugger confirmation deferred (see §9)
subsystems: [resource_pipeline, world_systems, game_loop]
---

# Spec: Terrain Streaming — Player-Centered Cell Ring & Per-Frame Load/Cull

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room runtime comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by
> `Assets.Vfs` / `Assets.Parsers` (cell asset fan-out) and `Client.Application` /
> presentation (the streaming driver). Field offsets cited here live in
> `structs/terrain-manager.md`. Citing engineers: `// spec: Docs/RE/specs/terrain-streaming.md`.
>
> **Confidence vocabulary:**
> - **CODE-CONFIRMED** — behaviour recovered from the instruction stream, corroborated across
>   multiple use sites.
> - **HIGH** — consistent across the traced streaming-spine sites.
> - **MED** — structurally inferred; one open reconciliation flagged.
> - **UNVERIFIED** — hypothesis only.

---

## Status

| Item | Confidence |
|------|------------|
| Player-centered fixed cell ring (3×3 ≤1000, 5×5 >1000) | CODE-CONFIRMED |
| Per-frame leading-edge load + trailing-edge cull (ring-shift) | CODE-CONFIRMED |
| Fixed 25-object slot pool, loaded-flag recycle (no per-cell malloc) | CODE-CONFIRMED |
| `.lst` cell-key manifest; key = `mapZ + 100000·mapX` | CODE-CONFIRMED |
| Area bootstrap sequence (active-area → cell-list → spawn-init → cold-start ring) | CODE-CONFIRMED |
| **Streaming is SYNCHRONOUS per-frame on the main thread** | CODE-CONFIRMED |
| The async worker thread + request FIFO is **dormant scaffolding (never runs)** | CODE-CONFIRMED |
| Cell holds a fixed 9-sub-manager array (terrain/grid vs env/water/FX) | CODE-CONFIRMED |
| Slot-pool header `count/scan-base` vs ring-array offset reconciliation | MED (one open item) |

---

## 1. Architecture in one paragraph

Terrain is divided into fixed **1024×1024 cells** identified by integer `(mapX, mapZ)`. A single
streamer object (the terrain manager) keeps a **fixed-size grid of cells centered on the local
player** — a **3×3 ring** when the stream radius is ≤ 1000, or a **5×5 ring** when the radius is
> 1000. As the player crosses a cell boundary, the ring **shifts**: it loads the new cells at the
advancing (leading) edge and culls the cells at the receding (trailing) edge. Cell objects come
from a **fixed pool of 25 pre-allocated slots** that are recycled by a loaded-flag predicate — there
is no per-cell allocation. Which cells are loadable at all is gated by an **area cell-key set**
populated from the area's `.lst` manifest.

---

## 2. LOAD-BEARING — Streaming is synchronous per-frame (the async FIFO is dormant)

**Confidence: CODE-CONFIRMED.**

> **In this build, terrain cell loading is driven SYNCHRONOUSLY by the per-frame ring shift on the
> main thread. Do NOT model or implement a shipping asynchronous producer.**

The streamer's constructor wires up a background load thread and a request FIFO, and the worker
thread drains that FIFO by calling the per-cell loader. **But there is no producer anywhere in the
build that enqueues a request onto the FIFO:**

- The FIFO constructor is invoked from exactly one site — the streamer constructor. Nothing else
  touches the list head to push a request.
- The per-cell loader (the function the worker would call) is reached only from **synchronous ring
  paths** (the cold-start fill, the 3×3 shift, the 5×5 shift, the ring dispatcher) and from the
  worker's own drain. None of these is a FIFO producer.
- The worker's inner drain only proceeds when the FIFO request-count is non-zero; since nothing
  ever increments it, the drain never runs. The thread spins on its sleep cadence with the FIFO
  permanently empty.

**Conclusion:** the async worker + request FIFO is **compiled-in scaffolding for a deferred-streaming
design that was never wired (or was compiled out).** The struct fields for it (FIFO head, count,
mutex handle) still exist and are still initialised, so `structs/terrain-manager.md` documents them —
but the producer side is absent. A faithful reimplementation drives streaming **synchronously from
the per-frame ring shift** and may omit the dormant thread entirely.

The **dormant request record**, documented only because the struct initialises it, would have been a
12-byte block `{ u32 mapX; u32 mapZ; u32 areaId; }`. It never receives data in this build.

---

## 3. The cell-key manifest (`.lst`) and the area-membership gate

**Confidence: CODE-CONFIRMED.**

### 3.1 Cell key formula

Every cell is keyed by a single integer:

> **`cellKey = mapZ + 100000 · mapX`**

This key is used both as the membership-set key and as the cache-match identity.

### 3.2 The `.lst` manifest

On entering an area, the manifest loader opens the area cell-list file:

> `data/map<NNN>/dat/d<NNN>.lst`   (the 3-digit area code substituted twice)

opened through the VFS open router. Its on-disk shape:

| Order | Size | Type | Field | Notes |
|-------|------|------|-------|-------|
| 1 | 4 | u32 | `key_count` | Number of cell keys that follow. |
| 2 | `key_count × 4` | u32[] | `cell_keys` | Each key = `mapZ + 100000·mapX`. |

The loader clears the streamer's existing key-set, reads the count, reads that many `u32` keys into a
temporary buffer, and inserts each key into the area cell-key set (an ordered set / RB-tree) on the
streamer. (This `.lst` shape is the only manifest concern of the streaming lane; the per-cell asset
decode — `.ted`/`.map`/`.mud`/`.sod`/`.bud` — belongs to the asset-format and collision lanes; see
§8 cross-references.)

### 3.3 The membership gate

The per-cell loader **first** looks the requested `cellKey` up in the area cell-key set. If the key
is **not present**, it returns "not loadable" immediately — i.e. *this `(mapX, mapZ)` is not part of
the active area, do not load it.* Only when the key is a member does it proceed to the cache lookup
or the actual load. This gate is what keeps the ring from loading cells outside the area's footprint.

---

## 4. The per-cell loader and cache

**Confidence: CODE-CONFIRMED** (HIGH on the cache-hit touch internals).

The per-cell loader takes `(streamer, mapX, mapZ, areaId)` and proceeds:

1. **Membership gate** (§3.3): compute `cellKey`, fail fast if not a set member.
2. **Cache lookup:** linear scan of the resident-cell slot array. A slot matches when its
   `cell.mapX`, `cell.mapZ`, and `cell.areaId` all equal the request. On a hit it sets the cell's
   loaded/in-use flag, runs a per-cell cache-hit "touch" (a re-link / refresh into the resident/
   visible set — internals owned by the render lane), and returns the cell.
3. **Cache miss → actual load:** take the global load critical section (which serialises all actual
   cell loads), obtain a slot from the **slot recycler** (§5), and if a slot is obtained and the
   per-cell asset fan-out succeeds, mark the slot loaded and return it; otherwise return failure.
   Then leave the critical section.

The per-cell asset fan-out (the function that actually reads the cell's files) is **owned by the
asset-format / collision / building lanes** and is out of scope here; this spec documents only that
the loader invokes it under the load lock and keys the result by `(mapX, mapZ, areaId)`.

---

## 5. The fixed-pool slot recycle (no per-cell allocation)

**Confidence: CODE-CONFIRMED.**

The ring does **not** `malloc` a cell per load. It reuses a **fixed array of pre-allocated cell
objects** (25 slots — the 5×5 grid capacity). The recycle mechanism:

- The slot recycler scans the slot array and returns the **first slot whose loaded flag is `0`** —
  i.e. the first free/recyclable cell object.
- A slot becomes free when the **cull** (the post-shift visibility pass) **clears its loaded flag**
  on any resident cell that is now more than 2 cells from the new center. Clearing the loaded flag
  both removes the cell from the visible set and makes its slot recyclable.
- If every slot is in use (the scan exhausts the count), the recycler reports a "terrain empty"
  error and returns failure rather than allocating.

So the loaded flag is a single boolean per cell that doubles as the **recycle predicate**: cull
clears it, the recycler claims the first cleared slot, and the loader re-sets it.

> **Reconciliation note (MED).** The recycle/cache scan iterates the resident-slot array from one
> base using a count field, while the ring-clear routine zeroes the 25 spatial slot pointers from a
> different base. These are almost certainly two views of the same 25-slot pool (a `{count, first-slot}`
> pool header plus a 5×5 spatial index into it), but the exact relationship is the one open struct
> item — see `structs/terrain-manager.md` §"Open item". An engineer should treat the pool as a single
> 25-cell array and not assume two independent arrays.

---

## 6. The ring shift (the load/cull core)

**Confidence: CODE-CONFIRMED** (HIGH).

### 6.1 Cadence

The local-player per-frame update, when the player is the local entity and movement settles,
fetches the terrain-world singleton and calls the **ring-shift dispatcher** with the player's
current world `(X, Z)`. So **every frame the local player moves, the ring is asked to re-center.**
This per-frame cadence is what guarantees at most one cell of movement per shift.

### 6.2 Ring-shift dispatcher (3×3 path; defers to 5×5 when radius > 1000)

If the stream radius is > 1000 the dispatcher defers to the 5×5 ring variant; otherwise it runs the
3×3 path:

1. **Cell-delta:** snap world `(X, Z)` to cell indices by dividing by 1024 (with a `-1024` bias for
   negative coordinates), subtract the current center cell's `mapX`/`mapZ`, yielding `(diff_x,
   diff_z)`. The code asserts at most one cell of movement on either axis per shift (guaranteed by
   the per-frame cadence). If both diffs are zero, no shift. Early-out if the destination slot is
   null.
2. **Recenter:** update the stored center-cell indices.
3. **Leading-edge LOAD:** load the 3 new cells at the advancing edge (up to 5 on a diagonal) via the
   per-cell loader, then attach each non-null result into the scene.
4. **Trailing-edge UNLOAD/cull:** detach the receding-edge cells from the scene.
5. **Slot-array rotation:** rotate the ring's pointer slots so the center stays at the ring's middle
   slot and the new edge occupies the freed slots.
6. **Post-shift fan-out:** rebuild the visible-cell list, run the per-cell sub-manager rebuild chain
   (env/water/FX — out of scope, see §8), run the post-shift visibility cull, and feed the
   height/elevation value. (The sub-manager rebuild chain is owned by the env/water/weather/FX lane.)

### 6.3 The 5×5 variant

The 5×5 ring applies the **same** load/cull semantics on the larger ring: it loads up to 9 new
leading cells and drops up to 9 trailing cells. It is structurally identical to the 3×3 path with a
larger ring.

### 6.4 The post-shift visibility cull

After a recenter, the cull walks all resident cell slots and **clears the loaded flag** on any cell
whose `(mapX, mapZ)` is more than 2 cells from the new center. As noted in §5, clearing the flag both
removes the cell from the visible set and frees its slot for recycling.

---

## 7. Area bootstrap and cold-start ring

**Confidence: CODE-CONFIRMED.**

On entering an area, the active-area switch performs this streaming-relevant sequence:

1. Store the active area id; resolve the area descriptor; format the 3-digit area folder code.
2. Get the streamer and **load the area cell-list manifest** (`.lst`, §3) — populating the area
   cell-key set that gates all loads.
3. Set area time-of-day and option flags; load region/sound/option tables. (The env/FX sub-manager
   reset here is out of scope.)
4. Get the terrain-world singleton and **set the spawn cell + kick the cold-start ring**: store the
   spawn cell coords and pick the stream radius from graphics quality, which selects the ring size
   and triggers the cold-start ring fill.
5. Initialise weather / sky / wind (owned by the env lane).

The **cold-start ring fill** does a first-time 9-cell (3×3) population at the spawn world position:
if the radius selects the larger ring it pre-shifts first; it clears/rebuilds the ring slot array,
stores the area id, computes the spawn center cell, recenters, then loads the center plus its 8
neighbours via the per-cell loader and attaches each into the scene (an init-variant attach). If the
center cell fails to load it reports a "first terrain init" error and aborts. It then rebuilds the
visible-cell list and runs the post-shift sub-manager chain and height feed.

---

## 8. The per-cell 9-sub-manager array (scope boundary)

**Confidence: CODE-CONFIRMED** (that there are 9; their internals are out of scope).

Each streamed cell object holds a **fixed array of 9 sub-manager pointers** (see
`structs/terrain-manager.md`). The cell load attaches all 9 into the live scene; the cull detaches
all 9. Roughly two of the nine are terrain-proper (the collision/grid manager family and the
terrain-texture manager); the remaining ~7 are env / water / weather / FX managers.

> **Scope boundary (coordination point):** the streaming lane documents only that the cell holds 9
> opaque sub-managers and that load/cull attaches/detaches them symmetrically. The *identity and
> internals* of these 9 are split between the collision/grid lane and the env/water/weather/FX lane;
> the streaming spec leaves all 9 as opaque. Do not infer sub-manager behaviour from this file.

---

## 9. Known unknowns / deferred confirmation

- **Slot-pool header reconciliation (MED):** the exact relationship between the resident-slot
  `{count, first-slot}` view and the 25-slot 5×5 ring pointer array is the one open struct item; a
  raw-layout read settles it. See `structs/terrain-manager.md`.
- **The 9 sub-manager identities** are deferred to the collision and env/FX lanes (§8).
- **Dormant-FIFO live confirmation (deferred):** a live run would confirm the worker's request-count
  always reads zero (FIFO never receives requests) and that the ring-shift dispatcher is the real
  per-frame driver, firing on player movement. Static comprehension is documented; no debugger was
  driven for this dossier.
- The per-cell asset fan-out (`.ted`/`.map`/`.mud`/`.sod`/`.bud` decode) is out of scope here — see
  the asset-format / collision / building specs.

---

## 10. Cross-references

- Streamer struct field table: `structs/terrain-manager.md`.
- Terrain heightmap / cell-blob formats: `formats/terrain.md` (and the asset-format lane).
- World / scene lifecycle: `specs/world_systems.md`, `specs/resource_pipeline.md`.
- Collision (`.sod`) and building (`.bud`) cell objects: the collision/building specs.
- VFS open router: `specs/resource_pipeline.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
