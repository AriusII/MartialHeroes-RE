---
status: confirmed
sample_verified: partial   # VFS index lookup mechanics CODE-CONFIRMED; area file-coverage counts SAMPLE-VERIFIED; thread timing values CODE-CONFIRMED
subsystems: [vfs_loader, boot_loader, loading_screen, terrain_streaming, subsystem_caches]
networked: false           # loading pipeline is entirely client-side; no wire traffic involved
encoding_note: Korean in-game text and config strings are CP949 (MS-949 code page), not UTF-8.
---

# Resource & Loading Pipeline — Clean-Room Specification

> Clean-room neutral spec. Promoted from dirty-room analyst notes under EU Software Directive
> 2009/24/EC Art. 6. No decompiler pseudo-code, no binary virtual addresses, no decompiler
> identifiers. Every behaviour described below is expressed in the spec-author's own words and
> tables, based on mechanically neutral analyst annotations.
>
> **Scope.** This spec documents the full **runtime resource pipeline**: how a file path resolves
> to bytes (the VFS chokepoint), how assets are cached at the subsystem layer, how the boot loader
> and loading screen work, and how terrain cells are preloaded and streamed on world entry. It also
> maps the per-area data inventory to the loading requirements that inventory creates.
>
> **Out of scope / cross-references.** The on-disk `.inf`/`.vfs` container byte layout is owned by
> `formats/pak.md`. Terrain cell byte formats are owned by `formats/terrain.md` and
> `formats/terrain_scene.md`. The nine-state scene lifecycle that drives state 2 (load) is owned
> by `specs/client_runtime.md §7`. Sound table format is owned by `formats/sound_tables.md`.
> Per-area census tables belong to `specs/area_inventory.md` (pending — see §7 for a loading-
> relevant summary drawn from the same source data).

---

## Status and verification banner

Evidence grades used throughout:

- **(CODE-CONFIRMED)** — behaviour or constant recovered directly from binary control-flow logic;
  safe to implement.
- **(SAMPLE-VERIFIED)** — additionally cross-checked against real VFS file contents; strongest tier.
- **(PLAUSIBLE)** — single-source behavioural inference; implement but mark tunable.
- **(UNVERIFIED)** — hypothesis only; do **not** hard-code.

---

# 1. The single file-open chokepoint

## 1.1 Architecture overview — (CODE-CONFIRMED)

Every file open in the client (~150+ distinct call sites) routes through a single file-open
function referred to here as the **open router**. The router examines two conditions to pick one
of three I/O paths:

1. **Is the packed VFS mounted?** (A boolean flag set at startup from the `game.lua` key
   `vfsmode`; when `vfsmode = false` the client reads loose files from the real filesystem.)
2. **Is a "raw/seek" mode bit set on the open request?**

| VFS mounted | Raw mode bit | I/O path chosen | Mechanism |
|---|---|---|---|
| Yes | Clear (normal) | **VFS in-memory path** | Binary search in the sorted directory index → `malloc` + `ReadFile` into a fresh heap buffer; reads are satisfied from this buffer. |
| Yes | Set | **Packed on-disk path** | Open the pack file with `CreateFile`, binary-search the index for the entry, position the file pointer to the entry's offset within the pack. |
| No | Either | **Loose-file path** | Plain `CreateFile` on the bare path. |

The data blob is **never memory-mapped** in the in-memory path. It is always `malloc` + `ReadFile`
under a critical section; `MapViewOfFile` appears in the codebase only in an unrelated integrity
check and is not part of the normal asset pipeline. — (CODE-CONFIRMED)

## 1.2 VFS index lookup mechanics — (CODE-CONFIRMED; format owned by `formats/pak.md`)

The directory index is a flat sorted array of records loaded once at startup into memory. The on-disk
format is specified in `formats/pak.md`; the runtime lookup behaviour is as follows:

- **Key type:** the lowercased virtual path string (ASCII). There is **no hash table and no
  filename interning** — names are stored already-lowercased in the index and the runtime
  lowercases the query before comparing. Lookup is **case-insensitive by construction**.
- **Algorithm:** **binary search** using a lexicographic string comparison over the sorted index.
  Complexity is **O(log N)** over the ~43 000-entry shipped VFS.
- **Result:** a pointer to the matching index entry (containing offset and size into the pack
  file), or null on a miss.
- **Read operation (in-memory path):** allocates a fresh heap buffer of exactly the entry size,
  acquires the read critical section, seeks to the entry's offset in the pack file, reads the
  bytes, releases the lock, and returns the buffer. **The caller is responsible for freeing the
  buffer.**

## 1.3 No file-level cache — (CODE-CONFIRMED)

There is **no cache at the file-open layer**. Opening the same virtual path twice performs two
independent binary searches and two independent `malloc` + `ReadFile` calls. Any caching or
deduplication lives one layer up, in the per-subsystem managers (§3).

**Reimplementation freedom:** a .NET VFS implementation may freely add an in-memory LRU or
dictionary at this layer. Nothing in the original's behaviour depends on re-reading; any caching
is a pure performance improvement with no behavioural contract.

## 1.4 `vfsmode` / loose-file fallback — (CODE-CONFIRMED)

The `vfsmode` flag is consumed at VFS-mount time (called from application startup, before the
scene loop). If `vfsmode = false`, the entire packed path is bypassed and all opens go to the
filesystem directly. The Godot/.NET loader already exposes this toggle via the VFS mount +
loose-file fallback in `Assets.Vfs`.

## 1.5 Campaign 7 re-confirmation — VFS runtime access path on build 263bd994 — (CODE-CONFIRMED)

The full VFS open/read/find machinery in §1.1–§1.4 was **re-confirmed against the newer client
build (SHA-256 prefix `263bd994`) in Campaign 7** by re-anchoring the VFS core globals and
re-reading the open router, the entry reader, the find chokepoint, the seek path, and the mount /
teardown sequence. The prior facts hold. The items below are either freshly pinned **runtime**
details or sharper statements of behaviour the earlier pass described only generally. None of them
change or contradict the on-disk container byte layout, which remains owned by `formats/pak.md`.

### 1.5.1 Mount sequence (runtime) — newly pinned

The VFS-mount routine performs, in order:

1. Open the **index file** (`data.inf`) for reading.
2. Read a **24-byte header** from the index file. (Header field meanings are owned by
   `formats/pak.md`; here the runtime fact is the fixed 24-byte read at mount.)
3. Take an **entry count** from that header. This count drives the next allocation.
4. Allocate the in-memory **table-of-contents (TOC) array** sized **144 bytes per entry**
   (`144 × entry_count`). The **144-byte TOC stride is a runtime fact** confirmed here; the
   per-field on-disk record layout remains owned by `formats/pak.md`.
5. Read the TOC records into that array.
6. Open the **data archive** (`data.vfs`) and **retain the OS handle** for the lifetime of the
   mount (it is the handle every subsequent entry read seeks within). The handle is initialised
   to an invalid sentinel before mount.

This sequence corroborates the existing "directory index loaded once at startup into memory"
statement (§1.2) and adds the concrete mount steps a clean-room loader can mirror.

### 1.5.2 Find (metadata-only) — re-confirmed

The find operation is a **pure metadata lookup**: it lowercases the requested path, then performs
a **binary search over the 144-byte-stride TOC by string comparison**, returning the matching
entry (offset + size) or a miss. When progress tracking is enabled (§2.4), the find/read paths
accumulate load progress as a side effect. This matches §1.2 exactly; the 144-byte stride is the
newly explicit runtime detail.

### 1.5.3 Read one entry under the read lock — re-confirmed and sharpened

Reading a single TOC entry's payload follows a **critical-section-bracketed** sequence:

1. Allocate a heap buffer of exactly the entry size.
2. **Enter** the VFS read critical section (the lock catalogued in §5.3).
3. Seek to the entry's offset within the retained data-archive handle (64-bit file pointer).
4. Read the entry's bytes into the buffer.
5. **Leave** the critical section.
6. On a **short read** (fewer bytes than the entry size), free the buffer and fail.

The caller owns the returned buffer (consistent with §1.2). The lock brackets only the
seek-and-read pair, as already noted in §5.3.

### 1.5.4 Find-and-read chokepoint — newly pinned detail

A combined **find-and-read chokepoint** is the single entry point most callers use. It:

1. **Zeroes a 16-byte output block** (the caller-supplied descriptor that receives the buffer
   pointer and size) before doing anything else.
2. Lowercases the requested path and binary-searches the TOC (the §1.5.2 find).
3. Reads the entry's bytes (the §1.5.3 read).
4. Accumulates load progress when tracking is on.

The **16-byte zero-initialised out-block** is the new runtime detail; a clean-room implementation
should return an explicit "found + buffer + size" result and treat a miss as a zeroed/empty
descriptor rather than an exception.

### 1.5.5 Mount flag selects packed vs. loose — re-confirmed

A single **global mount-flag byte** selects packed-archive access versus loose-file access; an
`is-mounted` predicate simply returns that byte. This is the same flag described as `vfsmode` in
§1.1/§1.4 — re-confirmed on this build as a one-byte global read by the open router.

### 1.5.6 Three-way open router and the 64-bit three-backend seek — newly pinned

The open router (§1.1) was re-confirmed as a **three-way branch** on the mount flag and a
**raw/seek mode bit**, choosing among: a VFS TOC find (in-memory path), a loose file opened with a
VFS byte-offset, or a plain OS file open with read/write/create flags. Two predicates gate the
loose-file open flags by testing the request's **read bit** and **write/create bit**. (Two router
variants exist — one taking the path by value, one copying it by name first — with the same
branching body.)

Newly pinned in this pass is the **seek behaviour**, which the earlier spec did not document. A
single **64-bit seek** (set / current / end origins) spans **three backends**, dispatched by the
file object's open mode:

| Backend | Seek mechanism |
|---|---|
| Plain OS file | OS 64-bit file-pointer set on the OS handle |
| Packed VFS entry | OS file-pointer set on the retained data-archive handle, **biased by the entry's base offset** within the archive |
| In-memory blob | Arithmetic on an in-memory cursor over a heap buffer |

The resolved position is **bounds-checked against the backing size**. The owning file object
carries **dual-path state** (an OS handle initialised to an invalid sentinel, plus an in-memory
blob pointer and cursor); its constructor zeroes this state and installs the file vtable. This
three-backend seek is the model a clean-room `Assets.Vfs` stream abstraction should reproduce so
that callers seek uniformly regardless of whether bytes come from a loose file, a packed entry, or
an already-slurped buffer.

### 1.5.7 Progress tracking (normalized) — re-confirmed

Load-progress tracking was re-confirmed as: a **cumulative bytes-loaded counter** (incremented per
read while a tracking flag is set) divided by an **expected-total denominator**, yielding the
**normalized progress value** the loading-screen bar reads (§2.3). Enabling tracking **resets the
counter to zero**; disabling tracking clears the per-read accumulation flag; a getter returns the
normalized value. This matches §2.4; the hardcoded denominator constant stays as documented there.

### 1.5.8 Teardown / unmount — newly pinned

VFS teardown (unmount) **drains the subscriber list, closes the retained data-archive handle, and
frees the TOC array base**. A clean-room loader should release the archive handle and the TOC on
unmount; nothing persists across an unmount.

### 1.5.9 Loaders route through the same open router — re-confirmed

The terrain **stream worker thread** (§4.3) and per-asset loaders — the `.map` descriptor text
parser (§4.5) and the `items.scr` record loader (boot set, §2.1) — all open their files through the
**same open router** documented above, so they inherit the mount-flag / raw-mode / progress
behaviour uniformly. The `.map` parser token-reads the terrain / extra-terrain / up-terrain /
building / FX / solid blocks, each with its own datafile + textures sub-blocks, opening each
referenced datafile via the router (block contents owned by `formats/terrain_scene.md`).

### 1.5.10 Delta check

No Campaign-7 runtime fact contradicts the existing spec or `formats/pak.md`. The 24-byte header
read, the 144-byte TOC stride, the 16-byte find-and-read out-block, and the three-backend 64-bit
seek are **additive runtime details**, not corrections. The denominator constant, the critical-
section-locked read, the binary-search find, and the three-way open router all **re-confirm** the
prior pass on the newer build.

---

# 2. Boot loading — state 2 (`LoadHandler`)

State 2 in the scene lifecycle (see `specs/client_runtime.md §7`) contains the boot loader. The
state is entered twice per session: once after login to load global data tables, and once on
entering the world (see §2.6).

## 2.1 Boot-load worker thread — (CODE-CONFIRMED)

The `LoadHandler` starts a single background worker thread (using the CRT-safe `_beginthreadex`
path through a generic thread-slot primitive) that loads, **in a fixed hardcoded sequence**, every
global data table and catalogue the game requires before play can begin. The load order is
compiled into the binary — it is **not data-driven**.

The boot set consists of approximately **50 entries** in a fixed global pointer array, covering:

- **Record tables (.scr / .do / .xdb files):** events, system_control, map_setting,
  playtime_reward, items, skills, skill icons/categories/needset, class-stance, users, products
  (including collect and random-name variants), helps, npc, npcs, items_extra, mobs, repair,
  upgrade-items, quests, emoticon, text commands, chivalry, letters, nick-to-fame, guild-crest,
  discript, tip-help, set-item-name, oblist, citems, Tutor, warstone-info, statue, VIP levels,
  item-scale, item-effect, effectscale, creature-item, vehicle, buff-icon-position.
- **Subsystem initialisations triggered on the worker thread:** shadow manager initialisation,
  character bind-pose pool warm-up, `data/item/skinlist.txt`, `data/char/sameemoticon.txt`, and
  the animation catalogue (which registers the skin/motion/bind-pose list-text registries).
  Guild-icon pool resolver is also initialised here.
- **Completion handshake:** after all loads, the thread sleeps for **500 ms** (a fixed grace
  period), then clears the thread-running flag (stored at a fixed offset from the handler object),
  and exits.

## 2.2 State-1-to-state-2 synchronous pre-load — (CODE-CONFIRMED)

**Before** the worker thread starts, during the state-1 → state-2 transition, the UI message
database `data/script/msg.xdb` is loaded **synchronously on the main thread**. This is a separate
load from the worker thread's boot set. The `msg.xdb` catalogue is the CP949 UI string table (see
`formats/misc_data.md` §msg.xdb). It must be present and loaded before the loading screen can
display any localised text.

## 2.3 Loading-screen rendering — (CODE-CONFIRMED)

While the worker thread runs, the main thread renders a loading screen at a deliberate low
frame rate:

- **Background:** one of three DDS images is chosen at random (`rand() % 3`):
  `data/ui/loading.dds`, `data/ui/loading06.dds`, `data/ui/loading08.dds`.
- **Frame rate cap:** the render callback calls `Sleep(100)` per frame, producing approximately
  **10 FPS** during loading.
- **Reference canvas:** `1024 × 768` pixels (global width/height values). The progress bar
  geometry is scaled relative to this canvas.
- **Progress bar:** maximum drawn bar width is **223 px**. Fill = `(progress / 100) × 223`, clamped
  to 223. UV scaling is proportional.
- **A loading SFX** (sound cue `920100100`) is played when the loading screen starts.
- **Completion detection:** the render callback polls the thread-running flag each frame. When
  the worker clears the flag, the render callback signals the engine to exit the loading loop and
  advance the state machine.

## 2.4 Progress meter — (CODE-CONFIRMED)

A small progress tracking mechanism accumulates bytes as they are loaded through the VFS:

| Component | Description |
|---|---|
| Tracking-enabled flag | Set when the `LoadHandler` constructs; cleared when it destructs. |
| Cumulative bytes counter | Incremented by the VFS file-open path (both the in-memory and metadata lookup paths) whenever the tracking flag is active. |
| Expected-total denominator | A **hardcoded constant of 9,395,240 bytes (≈ 8.96 MiB)**. This is a compile-time literal, not a dynamically computed total. |
| Normalised progress value | `cumulative / 9,395,240`, producing the 0–100-ish scalar the progress bar reads. |

**Implementation note:** the denominator is a fixed estimate. The bar may reach 100% before all
loads finish, or may finish before 100% on a content build with a different byte total.
**Completion is signalled by the thread-done flag, not by the bar reaching 100%.** A clean-room
reimplementation should drive completion from an explicit done event and may compute a real
denominator by summing TOC entry sizes for the boot set to display an accurate bar.

## 2.5 The `OPENNING/SKIP` flag — (CODE-CONFIRMED behaviour; path UNVERIFIED)

After the state-2 boot load completes, the engine reads a per-account INI key `OPENNING/SKIP`
(exact INI file path not recovered — see §8 open item 3). If set, the opening cinematic is skipped
and the engine transitions directly to state 4; otherwise it proceeds to state 3. This has no
effect on the resource pipeline but affects whether the second state-2 pass (see §2.6) is invoked
early.

## 2.6 State 2 is entered twice per session — (CODE-CONFIRMED)

The scene lifecycle enters state 2 both after login (the boot-load pass above) and on entering the
world. The **same handler and worker thread machinery runs for both passes.** Whether the world-entry
pass reloads the full ~50-table set or short-circuits already-loaded entries (subsystem caches would
make re-registration cheap) was not confirmed; treat both passes as potentially running the full
sequence (see §8 open item 5).

---

# 3. Per-subsystem caches

## 3.1 The universal cache pattern — (CODE-CONFIRMED)

All per-subsystem resource managers share a single architecture: a **singleton manager** owns a
**sorted associative container** (a red-black tree map or sorted vector) keyed by an integer ID or
a string path. Access is always **lazy find-or-load**:

1. Look up the key in the container.
2. On a **hit**: return the cached entry.
3. On a **miss**: format the canonical VFS path, load the asset, insert it into the container,
   then return the newly inserted entry.

There is **no eviction during a session.** Containers only grow; they are torn down wholesale when
the owning scene or handler is destroyed (e.g. on scene exit or on logout).

## 3.2 Per-manager inventory — (CODE-CONFIRMED)

| Manager | Key | Canonical VFS path pattern | Cache lifetime |
|---|---|---|---|
| **Skin cache** (`CoreSkinManager`) | Skin ID (gid integer) | `data/char/skin/g{id}.skn` | Scene/session |
| **Motion cache** (`CoreMotManager`) | Motion ID or path; populated at boot via list-text files | `data/char/mot/…` (paths from a boot-time list-text registry) | Session |
| **Bind-pose pool** | Bind ID (IdB integer) | `data/char/bind/g{id}.bnd` | Session |
| **Animation catalogue** | — (no per-entry key; a one-time boot init) | Loads skin/mot/bind list-text registries | Session |
| **Named UI/icon texture cache** (`GHTexManager`) | Texture name (string) | Name-keyed sorted array; path formed from the name | Scene |
| **Terrain texture pool** (`TerrainPool`) | Background-texture ID | Populated from `bgtexture.lst`; runtime terrain bypasses the named-texture cache and uses this pool directly | Scene |
| **Shadow manager** | — (singleton init, no per-entry key) | Inited on the boot thread | Session |

**Error path** (skin cache as the documented example): a cache miss that fails to load logs
`"load core skin check error manager id <name> <id>"`.

## 3.3 Anonymous one-shot texture loads — (CODE-CONFIRMED)

A separate one-shot texture loader is used by approximately 200+ UI and icon load sites. It does
**not** deduplicate by name. Each call:

1. Loads the VFS bytes into a heap buffer.
2. Creates a D3D texture from the in-memory bytes.
3. Frees the heap buffer.
4. Appends the D3D texture handle to a vector owned by the calling UI object.

Two different UI windows loading the same icon path produce **two separate D3D texture objects**.
This is the intentional design; deduplication for these textures would require using the named
`GHTexManager` path instead.

## 3.4 GPU texture residency — delegated to D3D9 — (CODE-CONFIRMED)

All D3D textures in the pipeline are created with `D3DPOOL_MANAGED`. This means:

- The **Direct3D 9 runtime**, not the client code, owns VRAM residency management. D3D9 will
  evict managed textures from VRAM when under pressure and reload them from the system-memory
  backing copy transparently.
- The client **never explicitly evicts a GPU texture during a scene.**
- This is the only "eviction" in the entire pipeline, and it is delegated entirely to the API.

**Implication for reimplementation:** the client's memory behaviour is bounded by the total
distinct assets touched in a scene, not by a working-set limit. A clean-room reimplementation may
use the same load-once-per-scene lifetime without a per-asset LRU and will not exceed the original
memory envelope in normal play.

---

# 4. World entry and terrain streaming

## 4.1 The area cell manifest — (CODE-CONFIRMED; counts SAMPLE-VERIFIED)

Before any cell can be streamed, the terrain streamer loads the area's **cell manifest** from
`data/map{NNN}/dat/d{NNN}.lst`. This file lists the integer cell keys (`mapZ + 100000 × mapX`)
that belong to the area. Only cells present in this manifest are valid loads; any cell not in the
manifest is silently skipped.

The `.lst` format is owned by `formats/terrain.md §1.2`. Cell count = `(file_size − 4) / 4`
(one u32le key per cell after a 4-byte header).

## 4.2 Initial synchronous 3×3 ring — (CODE-CONFIRMED)

Immediately on entering the world, the spawn handler **synchronously loads the 3×3 grid of 9
cells** centred on the player's spawn position before returning. The loading sequence:

1. Compute the centre cell from the spawn world position. Cell-grid formula:
   `cellIndex = 10000 − (int)(worldCoord × (−1 / 1024))`, with a −1024.0 pre-bias for negative
   coordinates. This formula is shared with the collision/camera subsystems and is the same as
   `formats/terrain.md` tiling.
2. For each of the 9 cells: look up the cell in the area manifest. If absent, skip. Otherwise
   call the synchronous cell loader (§4.4) and attach the loaded cell to the scene.
3. The centre cell is cached at a known slot for fast access.
4. If a configured quality radius exceeds 1000, the initial load is expanded to a 5×5 ring (25
   cells) via a variant loader. The source of the quality radius value (a graphics option or a
   per-map config) was not traced — see §8 open item 4.

The character-select preview also uses the 3×3 ring loader (for visual terrain previews); the
same path is re-used for both gameplay and preview contexts.

## 4.3 Terrain streamer thread — (CODE-CONFIRMED)

After the initial ring, a background **terrain streamer thread** handles peripheral cell loads:

- **Startup:** created when the terrain streamer object is constructed (before the world is
  entered), using the same `_beginthreadex`-based thread-slot primitive as the boot loader.
- **Input:** a mutex-guarded FIFO queue of cell-load requests, each carrying a map-X, map-Z, and
  area ID.
- **Timing:** the thread uses a `Sleep(10)` poll loop, then sleeps **4,000 ms** after startup
  before processing its first batch, then sleeps **3,000 ms** between subsequent batches. This
  means the first peripheral cell batch arrives ~4 seconds after world entry; subsequent batches
  arrive ~3 seconds apart.
- **Processing:** each dequeued request is passed through the find-or-load path (§4.4) and the
  request record is freed after the load.
- **Lifetime:** the thread runs while a "streamer active" flag is set; clearing the flag allows
  the thread to exit on its next poll.

## 4.4 Find-or-load cell (shared by main thread and streamer) — (CODE-CONFIRMED)

The find-or-load path is shared by both the initial synchronous ring (§4.2) and the streamer
thread (§4.3). Its steps:

1. **Manifest check:** compute the cell key `mapZ + 100000 × mapX`. If the key is not in the
   area's loaded manifest set, return "not found" (not an error — it is simply not part of this
   area).
2. **Cache check:** look up the cell key in the loaded-cell cache. On a hit, return the cached
   cell immediately.
3. **On a miss:** call the synchronous cell loader.

## 4.5 Synchronous cell loader and the shared mutex — (CODE-CONFIRMED)

The synchronous cell loader is **wrapped in a single global critical section** shared by all
callers:

- Both the streamer thread and the main thread (during the initial ring) call the synchronous
  cell loader.
- They **serialize on the same mutex.** A heavy initial-ring load (9 cells synchronously) and a
  concurrent streamer request contend on this lock.

Inside the critical section, the cell loader calls the per-cell asset bundle loader, which opens
and parses the following files for the cell (format details in the referenced format specs):

| File | Role | Absence behaviour |
|---|---|---|
| `d{NNN}x{mapX}z{mapZ}.map` | Scene descriptor (textures, buildings, FX layer refs) | Required; absent cell is skipped |
| `d{NNN}x{mapX}z{mapZ}.ted` | Terrain heightmap geometry | Required for visual terrain |
| `d{NNN}x{mapX}z{mapZ}.sod` | 2D collision wall segments | Optional; absent in ~1 cell per 5 areas (see §6.2) |
| `d{NNN}x{mapX}z{mapZ}.lst` | (referenced via manifest, not per-cell) | — |
| `d{NNN}x{mapX}z{mapZ}.bud` | Building/prop mesh geometry | Optional; absent on terrain-only cells |
| Textures referenced by .map | DDS or PNG terrain textures | Loaded through subsystem texture pool |

The exact internal order and partial-failure behaviour of the per-cell bundle loader was not fully
traced in this pass (see §8 open item 6). Parsers **must degrade gracefully** on absent sidecar
files rather than aborting the cell load.

---

# 5. Thread model

## 5.1 Resource-relevant threads — (CODE-CONFIRMED)

The client has exactly **two resource-related background threads** beyond the main thread:

| Thread | Started by | Role | Timing / completion |
|---|---|---|---|
| **Boot-load worker** | `LoadHandler` during state 2 | Loads ~50 global data tables in a fixed order; inits subsystems | Runs to completion; `Sleep(500)` grace then clears the thread-running flag and exits |
| **Terrain streamer** | Terrain streamer constructor (before world entry) | Lazy peripheral cell streaming | Poll `Sleep(10)`, initial `Sleep(4000)`, then `Sleep(3000)` between batches; runs while streamer-active flag is set |

A third background thread handles streaming-BGM refill (sound subsystem); its timing is specified
in `specs/client_runtime.md §1.7` and is out of scope for the resource pipeline.

## 5.2 Thread primitives — (CODE-CONFIRMED)

Both resource threads use a common thread-slot primitive:

- **Initialise:** stores the thread procedure pointer.
- **Start:** calls `_beginthreadex` to create the OS thread.
- **Set priority:** adjusts the thread's OS scheduling priority.
- **Close:** closes the thread handle when done.

A second thread-creation utility using `CreateThread` + `CreateEventA` exists in the codebase
for other subsystems (e.g. networking); the loading and terrain threads use only the
`_beginthreadex` path.

## 5.3 Locking model — (CODE-CONFIRMED)

Two critical sections are relevant to the resource pipeline:

| Critical section | Protects | Contention point |
|---|---|---|
| VFS read lock | `SetFilePointer` + `ReadFile` inside the VFS | Any two concurrent VFS opens (boot worker vs. any main-thread load) |
| Terrain cell lock | The synchronous cell loader | Streamer thread vs. main thread during the initial 3×3 ring |

There is no lock-free path, no job system, and no thread pool. The model is classic
single-worker-thread + mutex + flag/FIFO.

---

# 6. Per-area data inventory — loading implications

The full per-area census table (63 areas, per-cell file counts, all extension types) belongs to
`specs/area_inventory.md` (pending promotion). This section summarises **only the facts that
directly constrain loading behaviour** — what the parser and streamer must tolerate.

## 6.1 Area registration and the manifest gate — (SAMPLE-VERIFIED)

- 63 areas are registered in the VFS, identified by `data/map{NNN}/dat/d{NNN}.lst`.
- Area IDs form three non-contiguous ranges: **0–47** (48 areas, main open world), **100** (1
  area, training/test zone), **201–210** (10 instanced/instance zones), **300** (1 special zone).
  No IDs exist in the gaps 48–99, 101–200, or 211–299.
- If no `.lst` file exists for an area ID, the area does not exist. The loader must not attempt
  to load cells for an unregistered area.
- **Anomaly (area 0):** the `.lst` for area 0 contains two entries with identical cell keys.
  Only one cell file exists. The parser must handle duplicate keys without aborting (e.g. by
  de-duplicating) — see §8 open item 1.

## 6.2 Sidecar file absence — parser must degrade gracefully — (SAMPLE-VERIFIED)

Parsers must not abort on absent sidecar files. The verified absence patterns:

| File type | Absence scope | Required parser behaviour |
|---|---|---|
| `.sod` (collision) | Missing for **1 cell in each of 5 areas** (areas 5, 17, 18, 27, 33) | Treat as no collision data for that cell; do not abort cell load |
| `.bud` (building mesh) | Absent on **terrain-only cells** in many areas; the shortfall is largest in areas 1, 2, 3, 19, 21, 42, 47 | Treat as no building geometry; render terrain only |
| `.mud` (ambient-sound tile grid) | **Absent in 20 entire areas**: 0, 6, 19, 20, 24, 28, 29, 30, 32, 36, 38, 39, 40, 41, 42, 44, 46, 206, 207, 208. Also sparse within areas that have it | Default to silence; do not treat as an error |
| `.up` (upper-terrain / multi-level floor) | Present in **only 17 areas** (all water-enabled or indoor-flagged): 11, 15, 16, 22, 23, 24, 25, 26, 31, 34, 201–207 | Absent means no multi-level floor geometry; safe to skip |
| `.tol` (walkability tile bitmap) | Present in **only 3 areas**: 9 (2048×2048), 13 (2048×2048), 100 (256×256) | Absent means fall back to `.sod` collision for movement blocking |

## 6.3 Area count anomalies — (SAMPLE-VERIFIED)

Three areas (2, 21, 47) have one more `.map` file than their `.lst` cell count (52 files vs.
51 LST entries). The extra file is not referenced by the manifest and will be skipped by the
runtime. Parsers must not assume the VFS file count equals the LST entry count.

Area 300 has 16 LST cells but only 1 `.mud` file.

## 6.4 Environment binary files — always present — (SAMPLE-VERIFIED)

All 63 areas have the full set of core environment binaries in `data/sky/dat/`:
`map_option{N}.bin`, `fog{N}.bin`, `light{N}.bin`, `material{N}.bin`, `point_light{N}.bin`,
`weather{N}.bin`. These may be loaded unconditionally at area entry.

Three sky-dome binary types are **conditionally absent** for indoor and water-plus-indoor areas:
`clouddome`, `stardome`, `cloud_cycle`. Their absence correlates with `map_option.indoor_flag = 1`
and is not an error. See `specs/environment.md` and `formats/environment_bins.md` for parsing.

**Zero-padded naming artifact:** areas 15 and 16 have both a zero-padded form (e.g. `fog015.bin`)
and an unpadded form (`fog15.bin`) for every sky binary type; both files are byte-identical. The
loader should prefer the unpadded form; the padded form is an authoring artifact.

## 6.5 Sound table files — always present — (SAMPLE-VERIFIED)

All five per-area sound table files (`.bgm`, `.bge`, `.run`, `.wlk`, `.eff`, each 13,312 bytes)
are present for every area that has a `.lst`. They may be loaded unconditionally at area entry.
See `formats/sound_tables.md` for record layout.

## 6.6 Spawn data — (SAMPLE-VERIFIED)

- `mob{NNN}.arr` is absent for 8 areas: 0, 6, 100, 201, 202, 203, 206, 207, 208. Treat as 0
  mob spawns.
- `npc{NNN}.arr` is absent for 2 areas: 11, 14. Treat as 0 NPC spawns.
- **Partial-record anomaly:** areas 0 and 207 carry 16-byte trailing data in their `npc.arr`
  files that does not constitute a complete 28-byte record. The parser must compute the record
  count as `floor(file_size / 28)`, discarding any trailing partial record.

See `formats/npc_spawns.md` for the spawn record layouts.

---

# 7. Godot guidance — implications for the clean-room client

This section translates the documented legacy pipeline into concrete recommendations for the
`Assets.Parsers`, `Assets.Vfs`, and Godot presentation layer. No legacy contracts are copied;
every item below is a clean design decision informed by the original's mechanics.

## 7.1 VFS lookup — safe to modernise

The original uses O(log N) `strcmp` binary search over a flat sorted index with no file-level
cache. A .NET reimplementation may freely:

- Use a `Dictionary<string, VfsEntry>` (O(1) lookup). Key = lowercased virtual path; comparison
  is case-insensitive (match the original's lowercased-key semantics).
- Add an in-memory LRU byte-buffer cache at the VFS layer. Nothing in the original's behaviour
  depends on re-reading, so caching is a transparent improvement.

## 7.2 Boot loader — parallelism is safe but gate the completion

The original boot set is ~50 independent record tables loaded in a fixed compiled order. No
data dependency between them was observed that would mandate the original order. A .NET task
scheduler may load these tables in parallel batches, but must maintain a single
**"boot-complete" gate** before world entry — mirroring the original's thread-running flag. A
progress value computed from the actual sum of TOC entry sizes for the boot files is more
accurate than the legacy hardcoded 8.96 MiB constant.

## 7.3 Enter-world sync/async split — addressing the NPC-grounding race

The legacy client synchronously loads the 3×3 (or 5×5) cell ring before the first rendered
frame, then streams the rest with multi-second delays. This is the direct cause of the
"NPCs spawn at fallback Y before terrain streams" debt (noted in `CLAUDE.md`). The original
tolerates it because server-side Y is always 0 and terrain height is visual-only.

**Recommended approach for the Godot client:**

1. Await the initial 3×3 cell ring (or 5×5 for high-quality mode) **before revealing the
   player or spawning NPCs** — matching the original's "sync core" guarantee for the immediate
   vicinity.
2. Defer outer-ring cells to async streaming after reveal.
3. Resolve NPC Y from terrain height **after** the 3×3 ring is loaded and before the character
   is placed in the scene, eliminating the grounding race entirely.

## 7.4 Cell loading — keep off the main thread

The original serialises all cell loads (main thread + streamer) through one mutex, producing
visible hitches. The Godot client should:

- Run all cell loads on a worker task/thread, never blocking the rendering thread.
- Keep a FIFO request queue similar to the original's streamer.
- Bound the synchronous "boot ring" to a `Task.WhenAll` over the initial cell set, awaited
  before scene reveal, rather than blocking the main thread.

## 7.5 Cache lifetime — load-once-per-scene is the contract

The original loads every asset at most once per scene and frees everything on scene exit.
This is the intended memory model. Per-asset LRU is not needed unless profiling reveals
memory pressure. The per-subsystem manager pattern (§3.2) maps cleanly onto .NET
`ConcurrentDictionary<int, T>` or lazy-init wrappers.

## 7.6 Missing-file tolerance is required

As documented in §6.2, several file types are structurally absent in the shipped VFS for valid
areas and cells. Every parser in `Assets.Parsers` **must return a null/empty result rather than
throwing** on a missing optional sidecar (`.bud`, `.mud`, `.sod`, `.up`, `.tol`). Only `.map`
and `.ted` are required for a cell to be minimally renderable.

---

# 8. Open questions

1. **Area-0 duplicate `.lst` key runtime behaviour.** The area-0 manifest contains two identical
   cell keys. Whether the original runtime detects and de-duplicates this or attempts two loads
   of the same cell was not observed. The Godot streaming system should de-duplicate by key.
2. **Boot-load table order dependencies.** The ~50 boot tables load in a fixed compiled order.
   No mandatory data dependency between tables was confirmed in this pass. Before a Godot
   parallelisation of the boot set, a dependency audit over the individual loader bodies is
   needed to confirm the order is non-load-bearing.
3. **`OPENNING/SKIP` INI file path.** The INI key `OPENNING/SKIP` is read from a per-account
   file whose path is resolved from a network-client field. The exact path was not isolated.
   This affects whether the opening cinematic is shown; it does not affect the resource pipeline.
4. **5×5 vs. 3×3 ring selection.** A 5×5 variant of the initial ring loader exists alongside
   the 3×3 default. The configuration value (a radius threshold > 1000) that selects between
   them was observed but its source (a graphics option, a map config entry, or a hard constant)
   was not traced.
5. **Whether world-entry state-2 reloads all ~50 boot tables.** State 2 is entered both after
   login and on entering the world. The same handler machinery runs both times. Whether the
   world-entry pass replays the full boot table sequence or short-circuits already-cached entries
   was not confirmed. This determines whether the second loading screen is fast or full-length.
6. **`Map_LoadCellAssets` internal order and partial-failure behaviour.** The exact sequence in
   which `.map`, `.ted`, `.sod`, `.bud`, and texture files are opened within the cell bundle
   loader, and whether a missing `.sod` or `.bud` aborts or degrades the load, was not traced in
   this pass. Parser robustness must be validated against the known missing-file cases (§6.2).
7. **Exact progress-denominator origin.** The value `9,395,240` appears to be a build-time
   literal rather than a runtime-computed total. Confirming it is not patched at runtime, and
   whether it still matches the shipped VFS's boot-set byte sum, would allow an accurate progress
   bar without a hardcoded constant.
8. **`GHTexManager` vs. anonymous loader boundary.** `GHTexManager` provides a name-keyed
   cache; the anonymous one-shot loader (§3.3) does not. The boundary between which call sites
   use which path was not exhaustively mapped. The two approaches produce different memory
   behaviour (deduped vs. duplicated); implementers of the UI texture system should not mix them
   without understanding which path each asset category uses.
9. **`.mud` absence in large outdoor areas.** Twenty areas (including many full-featured 50-cell
   outdoor areas) have no `.mud` files at all. Whether this is intentional (no ambient sound
   authored) or a content-generation gap is not confirmed. The ambient-sound system must default
   to silence, not to an error.
10. **Area-207 `npc.arr` 16-byte tail.** The same 16-byte partial-record anomaly appears in both
    area-0 and area-207. Whether this is a shared content-tool format variant or two independent
    authoring errors is not confirmed. The `floor(file_size / 28)` counting rule handles both.

---

## Cross-references

- **VFS container format (`.inf` / `.vfs` byte layout):** `formats/pak.md`
- **Terrain cell formats (`.map`, `.ted`, `.sod`, `.bud`):** `formats/terrain.md`,
  `formats/terrain_scene.md`
- **Animation catalogue and motion files:** `formats/animation.md`
- **Skinned mesh and bind-pose formats:** `formats/mesh.md`
- **Environment sky binary formats:** `formats/environment_bins.md`
- **Sound table format:** `formats/sound_tables.md`
- **Spawn record format:** `formats/npc_spawns.md`
- **Misc data (`.scr` record tables, `msg.xdb`):** `formats/misc_data.md`
- **Scene lifecycle (state-2 context):** `specs/client_runtime.md §7`
- **Environment assembly at area entry:** `specs/environment.md`
- **Skinning pipeline (uses skin/bind/motion caches):** `specs/skinning.md`
- **Per-area census tables (full file counts):** `specs/area_inventory.md` (pending)
- **Canonical names:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`
