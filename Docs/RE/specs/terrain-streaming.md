---
status: code-confirmed
sample_verified: false   # static comprehension; live debugger confirmation deferred (see §9)
subsystems: [resource_pipeline, world_systems, game_loop]
ida_reverified: 2026-06-27
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
verification: confirmed (re-confirmed against IDB SHA 263bd994, CYCLE 7 (2026-06-20))   # control-flow-confirmed
                          # across the streaming spine; CYCLE 7 re-confirmed the per-frame caller chain
                          # is MAIN-THREAD-BLOCKING (ring-shift → find-or-load → blocking VFS reads, no
                          # queue/signal hop) and the worker apparatus is present-but-dormant; three
                          # runtime items (worker count==0 / a runtime worker spawn, dispatcher is sole
                          # driver, frustum matrix-major / up-axis) remain capture/debugger-pending — see §9
                          # CYCLE 12 (2026-06-22, IDB SHA 263bd994): §6.5 ground-height feed CORRECTED —
                          # the terrain-height sampler uses PER-TRIANGLE PLANE interpolation, NOT 4-corner
                          # bilinear lerp.  See §6.5 correction note.
                          # CYCLE 14 (2026-06-27, IDB SHA f61f66a9): confirmatory re-anchor — subsystem
                          # cleanly relocated, 1 re-confirmed SAME. Prior reverified: 2026-06-16 / 2026-06-22.
conflicts: none-open      # the campaign-10 conflicts (pool 34 vs ring 25, +10000 index offset,
                          # per-frame load count, clamp threshold wording) are RESOLVED in-text
# CORRECTED CYCLE 1 (ida_anchor 263bd994): §7 split into a two-phase bootstrap — Phase A (area load +
# stream-radius pick) in the area orchestrator, Phase B (spawn-cell set + cold-start ring kick) one
# call frame UP in the world-enter caller; the membership gate is the d<NNN>.lst cell-key set keyed by
# mapZ + 100000*mapX; the 34-pool cache/recycle is confirmed; singleton init order recorded. [2026-06-19]
---

# Spec: Terrain Streaming — Player-Centered Cell Ring & Per-Frame Load/Cull

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room runtime comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by
> `Assets.Vfs` / `Assets.Parsers` (cell asset fan-out) and `Client.Application` /
> presentation (the streaming driver). Field offsets cited here live in
> `structs/terrain-manager.md`. Citing engineers: `// spec: Docs/RE/specs/terrain-streaming.md`.
>
> **Re-verification banner.** `ida_reverified: 2026-06-27` (prior: 2026-06-16; CYCLE 12: 2026-06-22), `ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (CYCLE 14 re-anchor: confirmatory — 1 re-confirmed SAME),
> `evidence: [static-ida]`. Verification status: **confirmed** where the behaviour was recovered
> from control flow and corroborated across multiple use sites (the synchronous-per-frame driver,
> the dormant-worker proof, the cell-key gate, the ring shift load/cull, the cold-start fill, the
> radius selection/clamp). **Capture/debugger-pending** for the genuinely-runtime items in §9 —
> chiefly **whether the dormant worker is ever spawned at runtime** (the key debugger item; static is
> decisive that the worker apparatus exists but is dormant and streaming is main-thread-blocking),
> the worker request-count always reading zero, the per-frame dispatcher being the *sole* live driver
> under real input, and the frustum matrix major-order / world up-axis (the last is a render-lane
> concern, not streaming). **Conflicts: none open** — the Campaign-10 reconciliation
> (the 34-slot pool vs the 25-slot ring, the `+10000` cell-index origin offset, the per-frame
> 3-cell load count, and the 15000-threshold clamp wording) is resolved in-text below.
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
| Player-centered fixed cell ring (3×3 ≤1000, 5×5 >1000 stream radius) | CODE-CONFIRMED |
| Per-frame leading-edge load + trailing-edge cull (ring-shift) | CODE-CONFIRMED |
| Fixed **34-object** cell pool (loader) + **25-slot** spatial ring (manager), loaded-flag recycle (no per-cell malloc) | CODE-CONFIRMED |
| Pool **34** and ring **25** are two arrays on two objects, both indexing the **same 34** heap cells | CODE-CONFIRMED |
| `.lst` cell-key manifest; key = `mapZ + 100000·mapX` | CODE-CONFIRMED |
| Cell-index origin offset `+10000` in the world→cell snap (`index = 10000 + floor(coord/1024)`) | CODE-CONFIRMED |
| Four-function ring set: {3×3, 5×5} × {cold-start, per-frame}; the 3×3 fn is the entry that forwards to 5×5 when radius > 1000 | CODE-CONFIRMED |
| Stream radius by quality mode (1800 / 1000 / 600) with a literal per-area override and a 15000→1000 upper clamp | CODE-CONFIRMED |
| Two-phase area bootstrap: Phase A (area load + radius pick) in the area orchestrator, Phase B (spawn-cell set + cold-start kick) one call frame up in the world-enter caller | CODE-CONFIRMED |
| **Streaming is SYNCHRONOUS per-frame on the main thread** | CODE-CONFIRMED |
| The async worker thread + request FIFO is **dormant scaffolding (never runs)** | CODE-CONFIRMED |
| Cell holds a fixed 9-sub-manager array (slot0 ground texture, slot1 building/object, slots2-8 = fx1..fx7) | CODE-CONFIRMED |

---

## 1. Architecture in one paragraph

Terrain is divided into fixed **1024×1024 cells** identified by integer `(mapX, mapZ)`. The system
is split across **two singleton objects** (see `structs/terrain-manager.md`): a **cell loader** that
owns the heap-allocated cell objects and the area membership gate, and a **terrain manager** that
owns the spatial ring centered on the local player and the stream radius. The manager keeps a
**fixed-size grid of cells centered on the local player** — a **3×3 ring** when the stream radius is
≤ 1000, or a **5×5 ring** when the radius is > 1000. As the player crosses a cell boundary, the ring
**shifts**: it loads the new cells at the advancing (leading) edge and culls the cells at the
receding (trailing) edge. Cell objects come from a **fixed pool of 34 pre-allocated cell objects**
(owned by the loader) that are recycled by a loaded-flag predicate — there is no per-cell
allocation. The manager's **25-slot spatial ring** (5×5 capacity) is a separate pointer array
*into* that same 34-cell pool — **34 ≠ 25, and they are not the same array** (§5). Which cells are
loadable at all is gated by an **area cell-key set** populated from the area's `.lst` manifest.

---

## 2. LOAD-BEARING — Streaming is synchronous per-frame (the async FIFO is dormant)

**Confidence: CODE-CONFIRMED.**

> **In this build, terrain cell loading is driven SYNCHRONOUSLY by the per-frame ring shift on the
> main thread. Do NOT model or implement a shipping asynchronous producer.**

**The per-frame caller chain is main-thread-blocking, end to end (CYCLE 7 re-confirmation).** The
chain from frame tick to disk runs entirely on the calling (main game-loop) thread, with **no
enqueue / signal / completion-poll hop anywhere on it**:

1. The local-player per-frame update (run for the local entity once movement settles) calls the
   **per-frame ring-shift** with the player's current world `(X, Z)`.
2. The ring-shift calls the **per-cell find-or-load directly (inline)** — not via a request queue. (If
   the stream radius is very large the ring-shift forwards to the 5×5 variant, which *also* calls
   find-or-load inline.)
3. Find-or-load computes the cell key (`mapZ + 100000·mapX`), membership-checks it against the area's
   cell-key set, does the cache lookup, and **only on a true miss** acquires a free pool slot and
   reads the cell's files.
4. The slot loader performs the actual **blocking VFS reads** of the cell's `.mud` / `.gad` / `.map`
   files, in that order, on the **same call, on the same thread**.

So the ring shift, the find-or-load, and the disk reads all happen inside one call on the main thread.
There is **no** request enqueue, **no** wait-Event signal, and **no** completion poll on this live
path — that machinery belongs to the dormant worker below. The same synchronous find-or-load is used
by the cold-start / area-change fills (first terrain init, the character-select scene-build, the world
tick), and the whole-area (not per-cell) load — area binaries, region tables, the cell-key set,
sound/weather/sky/wind — is a separate but **also synchronous** path.

The loader's constructor wires up a background load thread, a request FIFO, and a wait gate (a
**named Win32 Event**, not a mutex — the worker waits on this Event, and a separate file-scope
critical section serialises the FIFO pop). The worker thread would drain the FIFO by calling the
per-cell loader. **But the worker never runs, on two independent grounds:**

- **The keep-running flag is set then immediately cleared.** The constructor sets the loader's
  keep-running flag to `1`, but the loader's **init routine — which runs right after construction —
  re-clears that same flag to `0`.** The worker's outer loop tests the keep-running flag at the top
  and is therefore **false on its very first test**, so the worker returns at once. (This is the
  primary, decisive proof. It is corroborated by a literal `TerrainLoader::init()` source-file
  string at the clearing site.)
- **There is no producer.** Even if the outer loop ran, the inner drain only proceeds while the FIFO
  request-count is non-zero. The only count mutation in the streaming spine is the FIFO **pop, which
  decrements**; nothing anywhere enqueues a request (increments the count). The FIFO constructor is
  invoked from exactly one site — the loader constructor — and nothing else touches the list head to
  push.
- The per-cell loader (the function the worker would call) is reached only from **synchronous ring
  paths** (the cold-start fill, the 3×3 shift, the 5×5 shift, the ring dispatcher) and from the
  worker's own (never-reached) drain. None of these is a FIFO producer.
- **The worker proc is never started.** The loader's thread/handle holder only **stores** the worker
  proc pointer (and zeroes the handle slot) — it does **not** hand that proc to any thread-create
  call. No static call site passes the terrain worker proc to a thread-create primitive; the only
  thread-create sites in the binary belong to unrelated subsystems. So no terrain streaming thread is
  ever spawned on any static path.

**The only live worker threads are the sound subsystem and input — terrain streaming is NOT among
them.** A faithful port MUST NOT assume async terrain streaming; the ring streams synchronously on the
main thread.

**Conclusion:** the async worker + request FIFO is **compiled-in scaffolding for a deferred-streaming
design that was never wired (or was compiled out).** The struct fields for it (FIFO head, count, the
wait Event handle, the critical sections) still exist and are still initialised, so
`structs/terrain-manager.md` documents them — but the worker never executes and the producer side is
absent. A faithful reimplementation drives streaming **synchronously from the per-frame ring shift**
and may omit the dormant thread entirely.

The **dormant request record**, documented only because the struct initialises it, would have been a
12-byte block `{ u32 mapX; u32 mapZ; u32 areaId; }` — confirmed by what the (never-reached) worker
reads off a popped node: it reads three consecutive `u32`s and passes them as `(mapX, mapZ, areaId)`
to the per-cell loader before freeing the node. It never receives data in this build.

### 2.1 Blocking-mitigation guards on the live (synchronous) path

Because the reads are synchronous on the main thread, three guards — and only these three — keep the
per-frame cost bounded. There is **no double-buffer and no completion-poll** anywhere on the live
path (that overlap machinery belongs to the dormant worker):

1. **"Already loaded" cache check (§4).** Find-or-load returns the cached cell on a hit, so a steady
   camera re-loads nothing — only the newly-entered ring row/column actually hits the VFS.
2. **One-cell-per-frame shift cap (§6).** The ring-shift asserts a per-frame cell delta `≤ 1` on each
   axis (guaranteed by the per-frame cadence), bounding the work to the cells crossing the ring
   boundary that frame, not the whole ring.
3. **Fixed 34-slot cell pool (§5).** Slot reuse, no per-frame allocation of cell objects.

The mitigation is therefore "skip if cached + cap shifts per frame + reuse slots", **not** async
overlap.

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

The manifest loader clears the loader's existing key-set, reads the count, reads that many `u32` keys
into a temporary buffer, and inserts each key into the area cell-key set (an ordered set / RB-tree)
on the loader. (This `.lst` shape is the only manifest concern of the streaming lane; the per-cell
asset decode — `.ted`/`.map`/`.mud`/`.sod`/`.bud` — belongs to the asset-format and collision lanes;
see §8 cross-references.)

> **Scope note (this pass).** The streaming spine only *consumes* the already-populated cell-key set;
> the **`.lst` on-disk shape and the `d<NNN>.lst` path are owned by the asset-format / VFS lane** and
> were **not re-walked in this behaviour pass** (the table above is preserved prior content). Treat
> the on-disk shape as the format lane's authority, not this spec's. The runtime *use* of the
> populated set — the membership gate in §3.3 — is the part this spec confirms. The area→cell fan-out
> and the per-cell open order are documented in `formats/area_inventory.md §1A`.

### 3.3 The membership gate

The per-cell loader **first** looks the requested `cellKey` up in the area cell-key set — an
**ordered set / red-black tree** on the loader. The lookup is a lower-bound search; if its result is
the set's end node (i.e. the key is **not present**), the loader returns "not loadable" immediately —
*this `(mapX, mapZ)` is not part of the active area, do not load it.* Only when the key is a member
does it proceed to the cache lookup or the actual load. This gate is what keeps the ring from loading
cells outside the area's footprint. The set itself is populated in **Phase A** of the bootstrap (§7)
**before** any cell could ever be requested.

---

## 4. The per-cell loader and cache

**Confidence: CODE-CONFIRMED** (HIGH on the cache-hit touch internals).

The per-cell loader takes `(streamer, mapX, mapZ, areaId)` and proceeds:

1. **Membership gate** (§3.3): compute `cellKey`, look it up in the area cell-key set (an ordered
   set / RB-tree on the loader). The gate runs **first** — if the key is not a member, the loader
   returns "not loadable" before any cache or load work.
2. **Cache lookup:** linear scan of the **34-cell pool** (§5). A slot matches when its `cell.mapX`,
   `cell.mapZ`, and `cell.areaId` all equal the request. On a hit it sets the cell's loaded/in-use
   flag, runs a per-cell cache-hit "touch" (a re-link / refresh into the resident/visible set —
   internals owned by the render lane), and returns the cell.
3. **Cache miss → actual load:** take the global load critical section (which serialises all actual
   cell loads), obtain a slot from the **slot recycler** (§5), and if a slot is obtained and the
   per-cell asset fan-out succeeds, mark the slot loaded and return it; otherwise return failure.
   Then leave the critical section.

The per-cell asset fan-out (the function that actually reads the cell's files) is **owned by the
asset-format / collision / building lanes** and is out of scope here; this spec documents only that
the loader invokes it under the load lock and keys the result by `(mapX, mapZ, areaId)`. Its open
order (`.mud` → `.gad` stub → `.map`, with the sub-assets pulled from `DATAFILE` tokens inside the
`.map` parse) is documented in `formats/area_inventory.md §1A.4`.

---

## 5. The fixed-pool slot recycle (no per-cell allocation)

**Confidence: CODE-CONFIRMED.**

The ring does **not** allocate a cell per load. The **cell loader** allocates a fixed array of cell
objects **once**, at loader-init time (a single construction loop over a fixed count), and the load
path thereafter only scans and reuses those objects.

> **The pool is 34, not 25 — and the 25 is a different array on a different object.** This is the
> one HIGH-severity correction to the earlier draft (which called this a "25-slot pool, the 5×5
> grid capacity"). Two distinct arrays exist on the two singletons:
> - **Cell loader → 34-object cell pool.** The loader's resident-slot **count is 34**, set at
>   loader-init. The cache lookup (§4) and the slot recycler (below) both iterate `< 34`. This is the
>   array that backs all actual cell storage — **the single authoritative OWNER of live cells.**
> - **Terrain manager → 25-slot spatial ring.** The manager holds a **25-entry pointer ring** (the
>   5×5 spatial index) that the shift functions rotate and clear. These 25 entries are *borrowed
>   pointers into the 34-cell pool* — the spatial grid is at most 25 cells, but the pool keeps **34**
>   so there is recycle headroom for in-flight loads while the previous edge is still being culled.
>   The ring is a **view**, not an owner; the live centre cell is ring slot 12.
>
> **Engineering consequence:** size the cell pool at **34**, not 25. A pool sized at 25 will hit the
> "terrain empty" failure path under a full 5×5 ring plus in-flight recycle headroom. The spatial
> ring (the thing you rotate per shift) is 25; the cell store (the thing you scan for a free slot)
> is 34. (Previously the one open struct item; **now resolved** — see `structs/terrain-manager.md`.)

The recycle mechanism:

- The slot recycler scans the **34-cell pool** and returns the **first cell whose loaded flag is
  `0`** — i.e. the first free/recyclable cell object.
- A slot becomes free when the **cull** (the post-shift visibility pass) **clears its loaded flag**
  on any resident cell that is now more than 2 cells from the new center. Clearing the loaded flag
  both removes the cell from the visible set and makes its slot recyclable.
- If every slot is in use (the scan exhausts the 34 count), the recycler reports a "terrain empty"
  error and returns failure rather than allocating.

So the loaded flag is a single boolean per cell that doubles as the **recycle predicate**: cull
clears it, the recycler claims the first cleared slot, and the loader re-sets it.

---

## 6. The ring shift (the load/cull core)

**Confidence: CODE-CONFIRMED** (HIGH).

### 6.0 The four-function ring set

Streaming is driven by **four ring routines**, paired as `{3×3, 5×5} × {cold-start, per-frame}`:

| | Cold-start (first fill, §7) | Per-frame (steady-state shift) |
|---|---|---|
| **3×3** | first 3×3 fill (center + 8) | 3×3 leading-edge load + trailing cull |
| **5×5** | first 5×5 fill (25 cells) | 5×5 leading-edge load + trailing cull |

In **both** the cold-start pair and the per-frame pair, the **3×3 routine is the entry point**: it
tests the stream radius and, if the radius is `> 1000`, **forwards to its 5×5 sibling**; otherwise it
runs the 3×3 body inline. So an engineer should model two ring sizes, each with a cold-start and a
steady-state shift, and treat the 3×3 routine as the dispatcher that selects ring size by radius.

### 6.1 Cadence

The local-player per-frame update, when the player is the local entity and movement settles
(i.e. `this == local-player singleton` **and** the not-blocked predicate is satisfied), fetches the
terrain-world singleton and calls the **ring-shift dispatcher** with the player's current world
`(X, Z)`. So **every frame the local player moves, the ring is asked to re-center.** This per-frame
cadence is what guarantees at most one cell of movement per shift.

### 6.2 Ring-shift dispatcher (3×3 entry; forwards to 5×5 when radius > 1000)

If the stream radius is `> 1000` the dispatcher forwards to the 5×5 ring variant (§6.3); otherwise
it runs the 3×3 path:

1. **Cell-delta:** snap world `(X, Z)` to integer cell indices, then subtract the current center
   cell's `mapX`/`mapZ`, yielding `(diff_x, diff_z)`. The cell index is

   > **`cellIndex = 10000 + floor(coord / 1024)`**   (with a `−1024` bias applied before the divide
   > for negative coordinates)

   — note the **`+10000` origin offset**, which keeps the indices non-negative for the ring's
   pointer arithmetic. The *delta* (`index − center`) is unaffected by the constant `+10000`, so the
   shift math is identical with or without it, **but a faithful absolute-index reconstruction must
   include `+10000`** (an index computed as a bare `floor(coord/1024)` will be off by 10000 against
   the original). The code asserts at most one cell of movement on either axis per shift (guaranteed
   by the per-frame cadence). If both diffs are zero (no cell crossing), no shift. Early-out if the
   destination ring slot is null.
2. **Recenter:** update the stored center-cell indices.
3. **Leading-edge LOAD:** on a cardinal step the 3×3 shift loads exactly **3 new cells** (the
   advancing edge of the 3-wide ring) via the per-cell loader, then attaches each non-null result
   into the scene. (The per-frame cadence asserts a single-axis step, so the steady-state per-frame
   count is **3** — a diagonal "up to 5" edge is *not reachable per-frame*; the larger 9 / 25 counts
   are the **cold-start** fills, see §7.)
4. **Trailing-edge UNLOAD/cull:** flush and detach the receding-edge cells from the scene.
5. **Slot-array rotation:** rotate the manager's **25-slot ring pointers** so the center stays at the
   ring's middle slot and the new edge occupies the freed slots.
6. **Post-shift fan-out:** rebuild the visible-cell list, run the per-cell sub-manager rebuild chain
   (env/water/FX — out of scope, see §8), run the post-shift visibility cull, and **feed the
   height/elevation value sourced from the new center cell** to its downstream consumer. (The
   sub-manager rebuild chain is owned by the env/water/weather/FX lane; see §6.5 for the shared
   structure.)

### 6.3 The 5×5 variant

The 5×5 ring applies the **same** load/cull semantics on the larger ring: on a cardinal step it
loads up to **9 new leading cells** (the 5-wide edge plus diagonal corners) and drops up to 9
trailing cells. It is structurally identical to the 3×3 path with a larger ring and the same
post-shift fan-out.

### 6.4 The post-shift visibility cull

After a recenter, the cull walks all resident cell slots and **clears the loaded flag** on any cell
whose `(mapX, mapZ)` is more than 2 cells from the new center. As noted in §5, clearing the flag both
removes the cell from the visible set and frees its slot for recycling. *(The exact ">2 cells from
center" distance test was not isolated as a single distinct site this pass — the clear-on-recycle
behaviour is confirmed; the precise distance predicate is a static hypothesis.)*

### 6.5 Shared post-shift sub-manager chain + height feed

Step 6 above is **the same** in the per-frame shift and the cold-start fill — a *single* shared
sequence, not two independent ones:

- A fixed multi-stage **sub-manager rebuild chain** (the env / water / weather / FX managers — owned
  by the env/FX lane, opaque here; see §8) runs after every recenter.
- A **visible-cell list rebuild** refreshes the resident/visible set.
- A **height/elevation feed** reads an elevation value off the **new center cell** and pushes it to
  a downstream consumer every shift (so the consumer always tracks the player-cell ground height).

> **CYCLE 12 CORRECTION (binary-won, IDB SHA 263bd994) — terrain ground height uses PER-TRIANGLE
> PLANE interpolation, NOT 4-corner bilinear lerp.**
> The terrain-height sampler that this feed (and the camera height-clamp of §A.6 in
> `specs/camera_movement.md`) calls does NOT perform a 4-corner bilinear interpolation. It
> **determines which of the two triangles in the quad the query XZ falls in, then evaluates the
> plane equation of that triangle** — standard barycentric/plane height. The bilinear-lerp reading
> in any prior note or in `CLAUDE.md` is incorrect and is superseded by this correction. A faithful
> reimplementation MUST use per-triangle plane interpolation: find the quad, determine the triangle
> (by the diagonal split direction — see `formats/terrain.md §5.7` for the split/UV-direction
> flags), and solve for Y from the plane of that triangle's three vertices. The 4-corner bilinear
> shortcut gives different results near the diagonal and must NOT be used.
> (Confidence: CODE-CONFIRMED, IDB SHA 263bd994. cite: `// spec: Docs/RE/specs/terrain-streaming.md §6.5`)

### 6.6 Stream-radius selection, override, and clamp

The stream radius (the float that selects 3×3 vs 5×5 and drives the frustum) is chosen on area entry
and clamped by the setter:

- **Quality-mode default:** the radius is picked from the graphics-quality setting (a quality word
  read off the graphics-quality singleton): **mode 1 → 1800**, **mode 2 → 1000**, **otherwise →
  600**. Alongside the radius the selector writes a pair of mode flags — `{0, 5}` for mode 1, `{1, 4}`
  for the other modes and for the override below.
- **Per-area literal override:** if the area carries a non-zero **region radius-override value**, it
  **bypasses the quality-mode table entirely** and is used directly as the stream radius (with mode
  flags `{1, 4}`). This is a per-area knob that lets an area force a specific stream radius.
- **Upper clamp:** the radius setter stores the value and then clamps it: **if the radius is
  `≥ 15000` it is reset to `1000`.** The clamp **threshold is 15000**; the clamped **result is 1000**
  (these are distinct numbers — do not conflate "clamps to 1000" the result with the 15000 trigger).
  The setter then rebuilds the view frustum from the current camera pose.

---

## 7. Area bootstrap and cold-start ring — the two-phase sequence

**Confidence: CODE-CONFIRMED.**

The area-enter bootstrap is split across **two distinct call frames**. **Phase A** (in the area
orchestrator) loads the area's data and picks the stream radius but does **not** set the spawn cell
or kick the ring. **Phase B** (one call frame **up**, in the world-enter local-map init caller that
invokes the orchestrator) runs **after** Phase A returns, once the local player's spawn world-XZ is
known, and is what **sets the spawn cell and kicks the cold-start ring**. The earlier draft folded
both into the orchestrator's "step 4"; the behaviour is identical, but the **kick lives in the
caller, not inside the orchestrator** — corrected here.

### 7.1 Phase A — area load + stream-radius pick (the area orchestrator)

In order, the area orchestrator:

1. Store the active area id; resolve the area-setting descriptor record; format the 3-digit area
   folder code.
2. Get the loader and **load the area cell-list manifest** (`.lst`, §3) — populating the area
   cell-key set that gates all loads.
3. Set area time-of-day and option/dome flags; load the map-option binary.
4. **Open the 4 per-area binaries** — `map<NNN>.bin`, `regiontable<NNN>.bin`, `region<NNN>.bin`,
   `npc<NNN>.arr` (see `formats/area_inventory.md §1A.3`).
5. Load the 5 sound tables for the area.
6. Get the terrain manager and write its **map-option + region words**, then **PICK the stream
   radius** (the quality-mode default, or the per-area literal override — see §6.6), which selects
   the ring size.
7. Initialise weather / sky / wind (owned by the env lane).

> **Ordering fact (load-bearing):** the `.lst` cell-key set (step 2) is loaded **before** the area
> binaries (step 4) and **before** the radius pick (step 6) — so the membership gate (§3.3) is fully
> populated before any cell could ever be requested. Phase A does **not** set the spawn cell and does
> **not** kick the ring. *(Note: the area orchestrator has other callers — e.g. the character-select
> scene preview builder — that load an area for a preview scene and may not run the Phase-B kick.)*

### 7.2 Phase B — spawn-cell set + cold-start ring kick (the world-enter caller)

After Phase A returns, the **world-enter local-map init caller** (one call frame up) runs, in order:

1. Look up the map-local record for this area id.
2. Call Phase A (the whole area orchestrator above). On failure, return.
3. Write the local player's spawn world position from the map-local record.
4. **Resolve the player's spawn cell from the spawn world-XZ** (snap world `(X, Z)` to a cell index).
5. Get the terrain manager and **kick the cold-start ring** with `(spawnX, spawnZ, areaId)`.
6. **Sample the ground height** at the spawn position and write it to the player.

So the spawn-cell set + cold-start kick is owned by the **caller**, not the area orchestrator.

### 7.3 The cold-start ring fill

The **cold-start ring fill** does a first-time population at the spawn world position. Like the
per-frame shift (§6.0), the **3×3 cold-start is the entry point** and **forwards to the 5×5
cold-start when the radius is `> 1000`**:

- **3×3 cold-start:** loads the **center cell plus its 8 neighbours = 9 cells** (`mapX−1..+1 ×
  mapZ−1..+1`).
- **5×5 cold-start:** loads the **full 5×5 block = 25 cells** (`mapX−2..+2 × mapZ−2..+2`) into the
  manager's 25-slot ring, then runs a 5×5 attach loop.

Either variant copies the ring into a scratch and runs a ring-reset, stores the area id, computes the
spawn center cell from the spawn world `(X, Z)`, recenters, then loads the cells via the per-cell
loader and attaches each non-null result into the scene (an init-variant attach). If the **center
cell** fails to load it reports a "first terrain init" error and aborts. It then runs the **same**
post-shift fan-out as the per-frame shift (§6.5): visible-cell list rebuild, sub-manager rebuild
chain, and the center-cell height feed.

### 7.4 Singleton init dependency order (what must exist before the first cell streams)

Both terrain singletons are lazy one-shot singletons (constructed on first access). The hard ordering:

- **The TerrainLoader is constructed and init'd before the TerrainManager finishes constructing.**
  The manager's construction forces the loader first and stores a pointer to it; the loader's init is
  what allocates the **34-cell pool** (and clears the dormant-worker flag, §2). So accessing the
  manager guarantees the pool exists.
- **The texture-preload pool (from `bgtexture.lst`) is built during manager construction.**
- **Then Phase A** loads the `.lst` membership set and picks the radius; **then Phase B** sets the
  spawn cell and kicks the cold-start ring — the first point any cell is actually loaded.

By the time the cold-start kick runs, the pool + the cell-key gate + the texture-preload pool + the
ring + the radius are all live.

---

## 8. The per-cell 9-sub-manager array (scope boundary)

**Confidence: CODE-CONFIRMED** (the 9 slot roles are now named — see below).

Each streamed cell object holds a **fixed array of 9 sub-manager pointers** (see
`structs/terrain-manager.md`). The cell load attaches all 9 into the live scene; the cull detaches
all 9. The slot roles (named in CYCLE 1, proven by the per-slot build-function order + literal error
strings) are: **slot 0 = the ground texture-patch grid; slot 1 = the building/object placement grid;
slots 2..8 = the seven FX overlay texture layers (fx1..fx7).** All nine are per-cell texture-index /
object-placement grids driven by the cell `.map` descriptor.

> **Scope boundary (coordination point):** the streaming lane documents only that the cell holds 9
> sub-managers and that load/cull attaches/detaches them symmetrically, plus their slot roles. The
> per-layer *byte layouts* and the FX-attach wiring (which `.map` section feeds which slot, during
> the `.map` parse) are owned by the asset-format / env-FX lane — see `formats/terrain.md` and
> `structs/terrain-manager.md`. Collision and region are NOT in this 9-slot array (§5,
> `structs/terrain-manager.md`).

---

## 9. Known unknowns / deferred confirmation

**Resolved since the prior draft:**

- **Slot-pool reconciliation — RESOLVED.** The earlier MED-confidence open item (the relationship
  between the resident-slot count view and the 5×5 ring pointer array) is settled: the **loader owns a
  34-cell pool** and the **manager owns a 25-slot spatial ring** that points into it (§5). Two arrays
  on two objects, both indexing the same 34 cells. No longer open. See `structs/terrain-manager.md`.
- **Area bootstrap ownership — RESOLVED (CYCLE 1).** The active-area orchestrator end-to-end sequence
  (§7) is now confirmed as a **two-phase** sequence: Phase A (area load + radius pick) in the area
  orchestrator, Phase B (spawn-cell set + cold-start kick) one call frame up in the world-enter
  caller. The streaming-relevant kick is in the caller, not the orchestrator.

**Static hypotheses (recovered from control flow, not isolated to a single site this pass):**

- The exact **">2 cells from center" cull distance predicate** as a single distinct site (§6.4) — the
  clear-on-recycle behaviour itself is confirmed.
- The **`.lst` on-disk shape and path** (`d<NNN>.lst`) — a format concern owned by the asset-format /
  VFS lane; the streaming spine only *consumes* the already-populated cell-key set.
- The claim that the request-FIFO constructor is invoked from exactly one site (the loader ctor).

**Capture/debugger-pending (genuinely needs a live run):**

- **Whether the dormant worker is ever spawned at runtime (the key debugger-pending item).** Static
  evidence is decisive that **the worker apparatus exists but is dormant and streaming is
  main-thread-blocking**: there is no thread-create site for the worker proc, and the keep-running
  flag is cleared at init. A `?ext=dbg` session is the only thing that could witness whether anything
  flips the keep-running flag to `1` and spawns the thread, and whether anything enqueues to the
  request queue and signals the wait-Event during normal play. **Absent that live observation, the
  static conclusion stands: the ring streams synchronously on the main thread.**
- That the worker's request-count **always reads zero** at runtime — confirming the FIFO truly never
  receives a request (static shows no producer; a live read is the ground truth).
- That the per-frame dispatcher is the **sole** live driver, firing under real player input (static
  shows the only live call site; a breakpoint would confirm it hits on movement).
- The **frustum matrix major-order / world up-axis** (and any on-screen visible-cell colour) — a
  render-lane concern, **out of streaming-behaviour scope**, flagged here for the render/capture lane.

**Out of scope (other lanes):**

- The per-cell asset fan-out (`.ted`/`.map`/`.mud`/`.sod`/`.bud` decode) — see the asset-format /
  collision / building specs and `formats/area_inventory.md §1A`.
- **The 9 sub-manager byte layouts and FX-attach wiring** — see `formats/terrain.md` (§8 names only
  the slot roles).

---

## 10. Cross-references

- Streamer struct field table: `structs/terrain-manager.md`.
- Area cell census + per-cell fan-out / open order: `formats/area_inventory.md` (§1A).
- Terrain heightmap / cell-blob formats: `formats/terrain.md` (and the asset-format lane).
- World / scene lifecycle: `specs/world_systems.md`, `specs/resource_pipeline.md`.
- Collision (`.sod`) and building (`.bud`) cell objects: the collision/building specs.
- VFS open router: `specs/resource_pipeline.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
