---
verification: confirmed    # the boot/loading orchestration load-bearing facts are recovered from control-flow; runtime replay & exact INI string remain capture/debugger-pending
ida_reverified: 2026-06-18   # scene re-confirmation campaign (build 263bd994)
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]   # control-flow recovery + real-VFS file-coverage counts (§6) corroborated against the shipped archive
conflicts: world-entry state-2 full-corpus replay vs cached short-circuit (§2.6, §8 item 5); the OPENNING/SKIP on-disk INI filename string (runtime-populated config field, §2.5, §8 item 3); display-config FRAMERATE consumer reaching the 60 FPS throttle (§5.1 note, §8 item 11)
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
> `formats/terrain_scene.md`. The scene lifecycle that drives state 2 (load) is owned
> by `specs/client_runtime.md §7`; it has **8 top-level cases — `GameState` 0..7** (the application
> entry point's `switch` is bounds-checked `<= 7` over a jump table of 8 entries plus a default). The
> value **8 is a sub-state** (the `GameState` sub-field, whose default is 8 — e.g. state 5 sub 8,
> state 6 sub 8), **not** a ninth top-level case. Sound table format is owned by `formats/sound_tables.md`.
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

State 2 — one of the **8 top-level scene-lifecycle cases (`GameState` 0..7**, see
`specs/client_runtime.md §7`) — contains the boot loader. The state is entered twice per session:
once after login to load global data tables, and once on entering the world (see §2.6).

The application entry point is itself the loading orchestrator: it mounts the VFS exactly **once**
before the scene loop, then drives a `while (true) switch (GameState)` whose case 2 constructs the
loading machinery below. There is **no per-scene resource-manifest request** — the loading set is the
fixed compiled corpus the worker pulls (§2.1); the state machine is the orchestrator and the asset
set is hardcoded, not data-driven. The `game.lua` keys read at entry-point top (`vfsmode`, plus a
launcher/debug gate) seed the VFS mount flag and a debug flag (the `vfsmode` toggle is §1.1/§1.4).

## 2.1 Boot-load worker thread — (CODE-CONFIRMED)

The `LoadHandler` owns a single background worker thread (using the CRT-safe `_beginthreadex` path
through a generic thread-slot primitive) that loads, **in a fixed hardcoded sequence**, every global
data table and catalogue the game requires before play can begin. The load order is compiled into
the binary — it is **not data-driven**.

**Install vs. start (corrected).** The `LoadHandler` **constructor** only *installs* the worker
procedure into the object's thread-slot and raises the thread-running flag — it does **not** spawn
the OS thread. The OS thread is actually **started later**, in the loading-window sub-initialiser
(§2.3), which calls the thread-slot start primitive (`_beginthreadex`) and then sets the worker's
scheduling priority to **ABOVE_NORMAL**. A clean-room boot should mirror this ordering: build the
loading-screen state first, *then* kick the worker (above-normal priority).

**One object, not two.** The `LoadHandler` and the on-screen loading window are the **same object**
(a single allocation). The loading-window sub-initialiser runs on the same instance and decorates it
with the render / SFX / progress-bar state. The shared object exposes three load-bearing fields a
clean-room layout can mirror: a **thread-running flag** (the constructor sets it; the worker clears
it on completion; the render callback polls it), a **loading-background-texture handle** (where the
chosen `loading.dds` lives, read by the render callback), and the **worker thread-slot**.

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

The §2.1 summary lists the boot set by category; the **exact ordered corpus** the worker registers
in sequence is enumerated authoritatively in §2.1a.

## 2.1a Boot data-table corpus — the authoritative registration ORDER — (CODE-CONFIRMED)

The boot worker registers the global data tables and catalogues **in a fixed, compiled sequence**.
The order is **load-bearing**: it is the exact sequence in which the loader installs each table into
its module-global registry, and a clean-room boot should mirror it unless a per-table dependency
audit proves the order non-load-bearing (§7.2, §8 item 2). The list below transcribes the **48
explicit on-disk file paths** the worker opens, in registration order. (All listed files carry
**CP949** contents.)

A handful of **filename quirks** in this corpus are *intentional spellings in the shipped data set*
and must be preserved verbatim by a faithful port: the descript table is **`discript.sc`** (extension
`.sc`, **not** `.scr`); the tutor table is **`Tutor.scr`** (capital **T**); the stance/"do" table is
**`musajung.do`**; and the extra-items table is **`items_extra.do`**. (The post-load destination gate
also reads INI section **`OPENNING`** with a double **N** — §2.5.)

| # | File path | What it is |
|---|---|---|
| 1 | `data/script/events.scr` | scripted-events table |
| 2 | `data/script/system_control.scr` | system-control table |
| 3 | `data/script/mapsetting.scr` | map-setting table |
| 4 | `data/script/playtime_reward.scr` | playtime-reward table |
| 5 | `data/script/items.scr` | item definitions |
| 6 | `data/script/skills.scr` | skill definitions |
| 7 | `data/script/musajung.do` | stance/"do" table (streaming load) |
| 8 | `data/script/skillcategory.scr` | skill-category table |
| 9 | `data/script/users.scr` | stat-curves / user table |
| 10 | `data/script/products.scr` | product (cash-shop) table |
| 11 | `data/script/productcollect.scr` | product-collect table |
| 12 | `data/script/productrandname.scr` | product random-name table |
| 13 | `data/script/helps.scr` | help table |
| 14 | `data/script/npc.scr` | NPC dialog/description table (also class descriptions) |
| 15 | `data/script/npcs.scr` | NPC spawn/definition table |
| 16 | `data/item/items_extra.do` | extra-items "do" table |
| 17 | `data/script/mobs.scr` | mob definitions |
| 18 | `data/script/repair.scr` | repair table |
| 19 | `data/script/upgradeitems.scr` | upgrade-items table |
| 20 | `data/script/quests.scr` | quest table |
| 21 | `data/script/emoticon.do` | emoticon "do" table |
| 22 | `data/script/textcommand.do` | text-command "do" table |
| 23 | `data/script/chivalry.scr` | chivalry table |
| 24 | `data/script/letters.scr` | letters table |
| 25 | `data/script/nicktofame.scr` | nick-to-fame table |
| 26 | `data/script/guildcrest.scr` | guild-crest table |
| 27 | `data/script/discript.sc` | menu-label / descript table (extension `.sc`, not `.scr`) |
| 28 | `data/script/tiphelp.scr` | tip-help table |
| 29 | `data/script/setitemname.scr` | set-item-name table |
| 30 | `data/script/oblist.scr` | ob-list table |
| 31 | `data/script/citems.scr` | cash-item table |
| 32 | `data/script/Tutor.scr` | tutor table (capital `T`) |
| 33 | `data/script/warstoneinfo.scr` | war-stone-info table |
| 34 | `data/script/statue.scr` | statue table |
| 35 | `data/script/skillneedset.scr` | skill-need-set table |
| 36 | `data/script/viplevels.scr` | VIP-levels table |
| 37 | `data/script/itemscale.scr` | item-scale table |
| 38 | `data/script/itemeffect.scr` | item-effect table |
| 39 | `data/ui/UiTex.txt` | UI-texture manifest (the UI id pool, §3A.2(a)) |
| 40 | `data/item/skinlist.txt` | item skin-list table |
| 41 | `data/char/sameemoticon.txt` | same-emoticon table |
| 42 | `data/ui/guildicon/crestlist.txt` | guild-crest icon list (paired with the `data/ui/guildicon/pool/` directory; 23×23 icons) |
| 43 | `data/script/effectscale.xdb` | effect-scale table |
| 44 | `data/script/creature_item.xdb` | creature-item table |
| 45 | `data/script/vehicle.xdb` | vehicle table |
| 46 | `data/script/buff_icon_position.xdb` | buff-icon-position table |
| 47 | `data/effect/bmplist.lst` | effect texture pool list (§3A.2(b)) |
| 48 | (effect-manifest chain) | the remaining effect manifests loaded after `bmplist.lst` — `xobj.lst`, `xeffect.lst` (+ effect-cache prime), `totalmugong.txt`, the joint/sword-light tables (see `specs/effects.md §3`) |

Interleaved with these explicit file paths are roughly a dozen **subsystem-init / manifest steps**
that resolve their own paths internally (a skill-icon manifest parse, a banned-word table init,
the shadow-manager init, the effect-manager init, the terrain-manager singleton first-touch, the
character-visual manifest load, and a few generic subsystem inits). They are not data-table files
per se and are not numbered above; the total worker step count is approximately 57. The 48 entries
above are the **file-registration spine** a port must reproduce in order.

After the last entry the worker performs the completion handshake (`Sleep(500)`, clear the
thread-running flag, exit — §2.1).

## 2.2 State-1-to-state-2 synchronous pre-load — (CODE-CONFIRMED)

**Before** the worker thread starts, during the state-1 → state-2 transition, the UI message
database `data/script/msg.xdb` is loaded **synchronously on the main thread**. This is a separate
load from the worker thread's boot set. Concretely: case 1 sets `GameState = 2` and *immediately*
calls the synchronous `msg.xdb` loader on the main thread, before any case-2 worker setup runs.

The loader opens `msg.xdb` through a disk-file wrapper that itself routes through the open router
(§1.5.9), then reads **fixed 516-byte records** (`file_size / 516` of them) into a global buffer and
inserts each record into a map keyed by the record's first 4-byte field (a map key). On open failure
it logs and continues. The `msg.xdb` catalogue is the CP949 UI string table; its byte layout is owned
by `formats/misc_data.md` §msg.xdb (where the 516-byte stride is the authoritative record size). It
must be present and loaded before the loading screen can display any localised text.

## 2.3 Loading-screen sub-init and rendering — (CODE-CONFIRMED)

The loading-window sub-initialiser runs on the same `LoadHandler` object (§2.1) and performs the
visual setup **and the actual worker-thread launch**:

- **Background:** one of three DDS images is chosen at random (`rand() % 3`):
  `data/ui/loading.dds`, `data/ui/loading06.dds`, `data/ui/loading08.dds`. The chosen texture handle
  is stored in the loading-background-texture field of the shared object.
- **Loading SFX:** sound cue `920100100` is played **looping** when the loading screen starts.
- **Reference canvas:** `1024 × 768` pixels (global width/height values). The progress-bar geometry
  is scaled relative to this canvas (horizontal scale = canvas-width / 1024, vertical scale =
  canvas-height / 768).
- **Worker launch (corrected — this is where the thread actually starts):** at the tail of this
  sub-initialiser the thread-slot start primitive (`_beginthreadex`) spawns the boot worker, and its
  scheduling priority is set to **ABOVE_NORMAL**. (The `LoadHandler` constructor only *installed* the
  procedure and raised the running flag — §2.1.)

While the worker thread runs, the main thread renders the loading screen each frame at a deliberate
low frame rate:

- **Frame rate cap:** the render callback calls `Sleep(100)` per frame, producing approximately
  **10 FPS** during loading.
- **Progress bar:** maximum drawn bar width is **223 px**. Fill = `(progress / 100) × 223`, clamped
  to 223; UV scaling is proportional. As §2.4 explains, `progress` here is the small integer quotient,
  so the bar barely advances.
- **Completion detection:** the render callback polls the thread-running flag each frame. When the
  worker clears the flag, the render callback signals the engine to exit the loading loop and advance
  the state machine. **Completion is gated only by this flag** — never by the bar reaching a value.

## 2.4 Progress meter — (CODE-CONFIRMED)

A small progress tracking mechanism accumulates bytes as they are loaded through the VFS:

| Component | Description |
|---|---|
| Tracking-enabled flag | Set when the `LoadHandler` constructs; cleared when it destructs. |
| Cumulative bytes counter | A 64-bit counter incremented by the VFS file-open path (both the in-memory and metadata lookup paths) whenever the tracking flag is active. |
| Expected-total denominator | A **hardcoded constant of 9,395,240 bytes (≈ 8.96 MiB)**. This is a compile-time literal, not a dynamically computed total. |
| Reported progress value | The **integer quotient** `cumulative_bytes / 9,395,240`. It is a truncating integer division, **not** a fractional 0–1 or a 0–100 percentage. The render callback later treats this small integer as if it were a 0..100 percent when sizing the bar (`progress / 100 × 223 px`). |

**Implementation note — the bar is effectively static.** Because the value is an *integer* quotient
and the entire boot set is only ≈ 8.96 MiB (roughly one denominator's worth), the quotient stays at
about **0–1** for the whole load. Fed into the `progress / 100 × 223 px` bar geometry, that means the
fill advances by at most a hair — **the original loading bar barely moves and never fills**. It is
essentially decorative. **Completion is driven solely by the worker's thread-done flag** (§2.3), not
by the bar reaching any value. A clean-room reimplementation should drive completion from an explicit
done event and — if it wants a bar that actually moves — compute a real denominator by summing the
boot set's TOC entry sizes rather than reusing the legacy constant.

## 2.5 The `OPENNING/SKIP` flag — (CODE-CONFIRMED behaviour and source; filename RESOLVED CAMPAIGN 16)

The post-load destination state is decided by an INI lookup that is **evaluated up front** when case 2
sets up its windows (i.e. *before* the boot load finishes, not after it). The lookup is a Windows
private-profile integer read: section `[OPENNING]`, key `SKIP`, default `0`, over the INI file
**`option.ini` in the client EXE directory**, whose path is held by the **DoOption settings singleton**
(populated by the option-path builder) — **NOT** the per-account / network-config singleton the earlier
draft guessed.

- A **non-zero** `SKIP` value → the opening cinematic is skipped and the engine transitions directly
  to **state 4** (character-select).
- A **zero** `SKIP` value → the engine proceeds to **state 3** (play the opening).

CAMPAIGN 16 resolved the former open item: the file is **`<exe-dir>\option.ini`** (held by the DoOption
singleton). The flag has no effect on the resource pipeline itself; it only fixes which state follows
the load.

> **Reload re-reads SKIP (CAMPAIGN 16).** The char-management reload (`SmsgCharActionResult` 3/100 codes
> 202/203/232 → state 2) re-enters the **identical** case-2 body and **re-reads `OPENNING/SKIP`
> unconditionally** — there is **no** reload-specific "skip the Opening" path. A reload reaches Select
> (4) only via the same SKIP gate (in practice SKIP is already 1 after the first opening, so reloads go
> straight to Select). The one genuine reload difference is that **`msg.xdb` is a case-1-only
> synchronous pre-load and is NOT re-loaded** on a reload (§2.2). So a faithful port must NOT model a
> "reload forces Select / reload skips the INI read" rule; it re-reads SKIP and only omits the `msg.xdb`
> reload.

## 2.6 State 2 is entered twice per session — (CODE-CONFIRMED structure; world-entry replay capture/debugger-pending)

The scene lifecycle enters state 2 both after login (the post-login global-table boot-load pass above)
and on entering the world. The **same handler and worker-thread machinery runs for both passes** — the
case-2 setup constructs the opening window first, then the loading window (= the `LoadHandler`), and
the `OPENNING/SKIP` read (§2.5) is performed before both, fixing the post-load state (3 vs 4) up front.

Whether the in-world (state-2) pass **replays the full table corpus** or **short-circuits
already-loaded / cached entries** (the subsystem caches of §3 would make re-registration cheap) **could
not be determined statically** — it depends on runtime control flow. Treat both passes as potentially
running the full sequence and confirm against a live world-entry transition. *(capture/debugger-pending
— see §8 open item 5; this is what determines whether the second loading screen is fast or full-length.)*

> **Pending / to confirm (Phase 5).** Whether the **second** load pass — the in-world reload
> triggered via opcode **3/100** (the char-management reload path of §2.5) — **replays the full
> 48-file boot corpus (§2.1a)** or **short-circuits to the already-cached subsystem tables (§3)**
> remains open. The case-2 body is byte-for-byte identical between the two entries, but individual
> table loaders may early-out when their registry is already populated, which would make the second
> loading screen near-instant. Confirm against a live world-entry / reload transition (debugger).

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

# 3A. UI / effect texture manifests, per-scene texture lists, and the boot font load — (CODE-CONFIRMED)

> Campaign 9D promoted the front-end asset-loading path. This section adds three things §1–§3 did
> not enumerate: the **two boot texture manifests** (the UI id pool and the effect texture pool),
> the **per-scene window-owned texture list** as a concrete third cache tier, and the **boot font
> load**. It also resolves §8 open item 8 (the named-pool vs. anonymous-loader boundary). The single
> file-open chokepoint and its VFS-or-disk fallback are §1; the on-disk container is `formats/pak.md`;
> image byte layouts are `formats/texture.md`. This deepens, it does not re-derive.

## 3A.1 The single file-open chokepoint and VFS-or-disk fallback — concrete witnesses — (CODE-CONFIRMED)

Every asset open routes through the one mount-flag-gated chokepoint of §1. The fallback is concrete
and identical across all asset loaders: each loader tests the **one global mount-flag byte**; if
mounted it pulls the entry bytes **into memory** (via the find-and-read descriptor of §1.5.4) and
feeds an **in-memory** decode; if not mounted it forwards the bare path to a **loose-file** decode.
The same decoder is reached either way — only the byte source changes. Three witnesses:

| Loader | Mounted path | Unmounted (loose) path | Used for |
|---|---|---|---|
| **Surface loader** | find-and-read → load-surface-**from-memory**, then free | load-surface-**from-file** on the path | sky cloud / lens-flare surfaces; passes a **non-zero colorkey** (opaque-black source key) |
| **Texture-create wrapper** | byte-slurp → create-texture-**from-memory** | create-texture-**from-file** | the core D3DX texture create |
| **Tokenizable text/icon slurp** | find-and-read into a re-tokenizable buffer | plain disk read | the `UiTex.txt` / `.lst` text-manifest parsers read through this |

> So a .NET loader needs **one** `OpenRead(logicalPath) → bytes/stream` abstraction with a mount
> toggle; the decode call is identical on both branches. The only non-zero colorkey in the texture
> path is the sky surface's opaque-black source key — note it for the Godot sky; the UI one-shot path
> uses colorkey 0 (none).

## 3A.2 The two boot texture manifests — (CODE-CONFIRMED) — resolves §8 open item 8

There are **two distinct global texture-pool manifests**, both loaded **once on the boot worker
thread** (§2.1), both producing a global id/name → deferred-texture pool. Neither is per-scene.

### (a) `data/ui/UiTex.txt` — the named UI texture id pool

A **brace/text manifest** (not a fixed-count binary list). Shape:

```
# comment lines start with '#'
UI_TEXTURE {
    DDS { <int tex_id>  "<relative/vfs/path>"  … }
    MSK { <int tex_id>  "<relative/vfs/path>"  … }
}
```

Parser behaviour: skip `#` comments; require the literal `UI_TEXTURE` then `{`; inside, two
sub-blocks keyed by the `DDS` and `MSK` literals; each row is exactly two fields — a base-10 integer
**tex_id** then a **double-quoted path** (quotes stripped). Rows loop until `}` / EOF — there is **no
row count** (the shipped file holds ~37 rows). Per row the loader builds a small id record into a
**global named-texture registry singleton** (with an entry counter) and constructs a deferred
runtime texture handle carrying the path + a display-mode-chosen format code (§3A.3). The image is
loaded **lazily on first use** — `UiTex.txt` itself does no GPU upload; it registers
**id → (path, deferred texture)** for a global UI pool.

### (b) `data/effect/bmplist.lst` — the shared effect texture pool

A **binary** list: a u32 `count`, then `count` fixed **30-byte name** records. Per name the loader
builds the full path `data/effect/texture/<name>`, constructs a deferred texture handle (carrying
that path + a fixed parameter), and appends it to the effect manager's texture-pool vector. After
`bmplist.lst` the loader chains the rest of the effect manifests in order (`xobj.lst`, `xeffect.lst`
+ effect-cache prime, `totalmugong.txt`, the joint/sword-light tables — see §2.1 and
`specs/effects.md §3`). The bmplist pool is the **shared effect texture pool** effects index into by
id.

> **§8 open item 8 RESOLVED — the named-pool vs. anonymous-loader boundary.** The named pools are
> these two boot manifests (UI id pool + effect name/index pool). The **anonymous one-shot loader**
> (§3.3) is what each **scene/window** uses for its own atlases (§3A.4) — it does **not** consult
> either boot pool and does **not** dedup. So: boot manifests build two global deduped pools; per-scene
> windows eager-load their named atlases into their own (non-deduped) texture list. A clean-room boot
> builds **two** global texture registries — a UI id→path map from `UiTex.txt` and an
> effect name/index→path pool from `bmplist.lst` (`data/effect/texture/` prefix) — both populated
> before the first scene, both holding deferred (lazy) textures.

## 3A.3 DDS/TGA decode + GPU upload, and the display-mode format selection — (CODE-CONFIRMED)

The decode chain is: **logical path → VFS bytes-in-memory → one D3DX9 in-memory create call →
managed-pool GPU texture wrapped in a runtime texture handle.** The decoder is delegated entirely to
D3DX9 (the client carries no custom image codec — consistent with `formats/texture.md` and
`specs/asset_pipeline.md §1`). The one-shot per-scene loader (§3.3) calls the in-memory
create-texture API with: device = the global render device; src buffer + size from the VFS read;
**single mip (MipLevels = 1, no full chain)**; **usage = 0**; **managed pool**; **colorkey = 0**;
and a **format hint** chosen by display mode (below). The surface variant (sky) uses the in-memory
load-surface call with the opaque-black colorkey (§3A.1).

**Display-mode DDS format selection.** Both the `UiTex.txt` parser and the per-scene atlas loads pass
a DDS **format constant chosen by the display-config display-mode value**:

| Display mode | Format constant passed |
|---|---|
| 0 | **DXT5** (the default) |
| 1 | **DXT3** |
| otherwise | **DXT2** |

i.e. the client forces a DXT variant on the loaded UI texture by display/quality mode (default DXT5).
This is the live confirmation behind `formats/texture.md`'s "DXT5 dominant / DXT3 constant present"
note, and it resolves the long-standing "894720068 colorkey" puzzle in the login builder: that
integer is the **DXT5 FourCC `Format` argument**, **not** a colorkey (the separate value is the
no-colorkey arg). This mode switch only changes the in-memory texture format D3DX transcodes to, not
the on-disk container — **a faithful port can simply load the DDS as-is.**

## 3A.4 Per-scene window-owned texture lists — the third cache tier — (CODE-CONFIRMED; one detail UNVERIFIED)

The pipeline has **three texture-lifetime tiers**:

1. **Global boot pools (session lifetime).** The `UiTex.txt` named-id registry and the `bmplist.lst`
   effect pool (§3A.2). Built once on the boot thread; their entries are deferred (lazy on first use).
2. **Per-subsystem find-or-load caches (scene/session lifetime).** The sorted-map managers of §3.2
   (skin / motion / bind / terrain-pool / named-texture). No eviction during a session; grow-only;
   torn down wholesale when the owning scene/handler is destroyed.
3. **Per-scene window texture lists (scene lifetime) — the addition.** Each UI window / scene object
   owns a **texture-list member** (a vector). The one-shot loader (§3.3) **pushes every D3D texture it
   creates into that window's vector** — **no name keying, no dedup**: two loads of the same path make
   two textures. When the window is **destroyed on a scene change** (the scene-machine constructs the
   scene window on state entry and destroys it on exit — `specs/client_runtime.md §7.4`), the
   window/panel teardown walks and **releases that texture list**. So the per-scene pattern is:
   **eager-load a handful of named atlas DDS files into a scene-owned texture set, slice widgets out
   of them by src-rect, release the whole set on scene unload.**

   Concrete witness (login scene): the login window's build routine eager-loads **four** named UI
   atlases (`login_slice1.dds`, `loginwindow.dds`, `InventWindow.dds`, `loginwindow_02.dds`) into its
   own texture list via the one-shot loader (each with the DXT5 format hint of §3A.3 and colorkey
   none), then builds its widgets referencing src-rects into those four atlases. It uses neither boot
   pool of §3A.2 (cross-ref `specs/ui_system.md §9.0` / §9.1).

**Eviction / ref-count.** The underlying D3D textures are **managed-pool**, so the D3D runtime owns
VRAM residency and reload-under-pressure — the only "eviction" in the whole pipeline, delegated to the
API (§3.4). The client's own discipline is **load-once-per-scene, release-all-on-scene-exit**, not a
per-asset LRU. There is **no global file cache**; dedup exists only inside the named boot pools.

> **UNVERIFIED (debugger-pending).** The architecture is solid — a per-window owned texture vector,
> filled by the one-shot loader, released on window teardown by the scene machine. The one residual is
> the literal per-handle texture-Release loop inside the texture-list destructor body (the static read
> landed on a neighbour); confirm it by breakpointing a scene-window destructor on a live scene
> transition. This does not change the model.

> **Godot guidance.** Model the texture layer as: (a) two global boot texture pools (UI-id + effect-name),
> (b) per-subsystem grow-only id caches, and (c) a **per-scene texture set owned by each scene/window,
> disposed when that scene unloads.** Decode via a standard DDS/TGA/PNG decoder (no custom codec);
> managed-pool semantics → just dispose on scene exit.

## 3A.5 Boot font load — (CODE-CONFIRMED)

Fonts are a **separate path from the VFS texture pipeline** — they are GDI-backed D3DX fonts created
from **system Hangul typefaces** at boot (state 1), **not** loaded from the archive. The font
registrar, driven by the font-table singleton, registers each of the **15 slots** (0..14): it releases
any prior font in the slot, copies the typeface-name string (DotumChe / Dotum / BatangChe) into the
slot, stores the size/weight params, and calls the D3DX create-font API with the render device,
height/width from the slot, **single mip**, **Hangul charset**, and the face name. So the 15 slots are
**GDI/D3DX system fonts** keyed by Korean typeface name + size, created once at boot. There is **no
glyph atlas in the VFS** for body text. The full per-slot face/size/weight table is owned by
`specs/ui_system.md §6.2`; the per-frame text path is `specs/ui_system.md §6.3 / §15.5`.

> **Godot guidance.** This is a system-font path — the original relies on Windows having DotumChe /
> Dotum / BatangChe (CP949 Hangul). A 1:1 port must ship/substitute equivalent CP949 Korean fonts and
> map the 15 slots (slot index → {face, size, weight}); the text encoding is CP949 throughout.

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
- **Startup gate:** before its poll loop, the worker **blocks on a wait-for-single-object on an
  event handle** (held in the streamer object) — i.e. it parks until that event is signalled rather
  than busy-spinning from the moment of creation. Only after the gate releases does the timing below
  begin.
- **Timing:** the thread uses a `Sleep(10)` poll loop, then sleeps **4,000 ms** after the gate
  releases before processing its first batch, then sleeps **3,000 ms** between subsequent batches.
  This means the first peripheral cell batch arrives ~4 seconds after the streamer is woken;
  subsequent batches arrive ~3 seconds apart.
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

| Thread | Installed / started by | Role | Priority | Timing / completion |
|---|---|---|---|---|
| **Boot-load worker** | *Installed* by the `LoadHandler` constructor; *started* in the loading-window sub-init (§2.1, §2.3) during state 2 | Loads ~50 global data tables in a fixed order; inits subsystems | **ABOVE_NORMAL** | Runs to completion; `Sleep(500)` grace then clears the thread-running flag and exits |
| **Terrain streamer** | Terrain streamer constructor (before world entry) | Lazy peripheral cell streaming | (default) | Event-gated start, then poll `Sleep(10)`, initial `Sleep(4000)`, then `Sleep(3000)` between batches; runs while streamer-active flag is set |

A third background thread handles streaming-BGM refill (sound subsystem); its timing is specified
in `specs/client_runtime.md §1.7` and is out of scope for the resource pipeline.

> **Note (main-thread driver loop).** The boot worker and streamer above are the only *resource*
> background threads. The **main thread's per-frame driver loop** (the four-phase loop —
> message-pump+input / scene-render+present / round-robin tick-scheduler / frame-throttle — held at a
> fixed **60 FPS** software cap) is owned by `specs/client_runtime.md` / `specs/game_loop.md`. The
> loading screen's `Sleep(100)` ≈ 10 FPS cap (§2.3) is a *loading-state-specific* render cadence, not
> the normal-play 60 FPS throttle. *(Whether the display-config FRAMERATE value has any consumer that
> reaches the throttle is capture/debugger-pending — statically it appears inert; treat the cap as a
> hardcoded 60 FPS.)*

## 5.2 Thread primitives — (CODE-CONFIRMED)

Both resource threads use a common thread-slot primitive:

- **Initialise / install:** stores the thread procedure pointer and zeroes the handle/tid fields.
  (This is what the `LoadHandler` constructor does for the boot worker — it installs, it does not
  start.)
- **Start:** closes any prior handle, then calls `_beginthreadex` to create the OS thread, storing
  the resulting handle and thread id.
- **Set priority:** adjusts the thread's OS scheduling priority. The boot worker is set to
  **ABOVE_NORMAL** immediately after it is started (§2.3).
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
3. **`OPENNING/SKIP` INI file — source RESOLVED, literal filename residual.** **RESOLVED (Campaign 10
   — see §2.5):** the destination state is decided by a private-profile integer read over section
   `[OPENNING]`, key `SKIP`, default `0`, with the **INI file path taken from the config singleton's
   path field**; non-zero → state 4 (skip opening), zero → state 3 (play opening). The only residual is
   the **literal on-disk filename** — it is runtime-populated into that config-singleton field rather
   than a static string constant, so the exact path string is *(capture/debugger-pending)* (read the
   field live to recover it). This affects whether the opening cinematic is shown; it does not affect
   the resource pipeline.
4. **5×5 vs. 3×3 ring selection.** A 5×5 variant of the initial ring loader exists alongside
   the 3×3 default. The configuration value (a radius threshold > 1000) that selects between
   them was observed but its source (a graphics option, a map config entry, or a hard constant)
   was not traced.
5. **Whether world-entry state-2 reloads all ~50 boot tables. *(capture/debugger-pending)*** State 2
   is entered both after login and on entering the world. The same handler machinery runs both times.
   Whether the in-world pass replays the full boot table sequence or short-circuits already-cached
   entries depends on runtime control flow and **could not be determined statically**. This determines
   whether the second loading screen is fast or full-length; confirm against a live world-entry
   transition.
6. **`Map_LoadCellAssets` internal order and partial-failure behaviour.** The exact sequence in
   which `.map`, `.ted`, `.sod`, `.bud`, and texture files are opened within the cell bundle
   loader, and whether a missing `.sod` or `.bud` aborts or degrades the load, was not traced in
   this pass. Parser robustness must be validated against the known missing-file cases (§6.2).
7. **Exact progress-denominator origin.** The value `9,395,240` is a build-time literal divided into
   the cumulative byte counter by **integer division** (confirmed — §2.4), which is why the bar is
   effectively static. Confirming it is not patched at runtime, and whether it still matches the
   shipped VFS's boot-set byte sum, would allow an accurate progress bar without a hardcoded constant.
8. ~~**`GHTexManager` vs. anonymous loader boundary.**~~ **RESOLVED (Campaign 9D — see §3A.2).**
   The name-keyed pools are the two boot manifests (the `UiTex.txt` UI id pool and the `bmplist.lst`
   effect pool); the anonymous one-shot loader (§3.3) is what each **scene/window** uses for its own
   atlases (§3A.4) and does **not** consult either boot pool or dedup. Boundary: boot manifests →
   global deduped pools; per-scene windows → own (non-deduped) texture list released on scene exit.
9. **`.mud` absence in large outdoor areas.** Twenty areas (including many full-featured 50-cell
   outdoor areas) have no `.mud` files at all. Whether this is intentional (no ambient sound
   authored) or a content-generation gap is not confirmed. The ambient-sound system must default
   to silence, not to an error.
10. **Area-207 `npc.arr` 16-byte tail.** The same 16-byte partial-record anomaly appears in both
    area-0 and area-207. Whether this is a shared content-tool format variant or two independent
    authoring errors is not confirmed. The `floor(file_size / 28)` counting rule handles both.
11. **Display-config FRAMERATE consumer. *(capture/debugger-pending)*** The normal-play per-frame
    throttle is held at a fixed **60 FPS** (seeded into the engine object's framerate field in the
    engine constructor and never overwritten — owned by `specs/client_runtime.md` / `specs/game_loop.md`).
    The display-config FRAMERATE value has **no statically-traced consumer that reaches the throttle**;
    whether it is truly inert (so the cap is unconditionally hardcoded 60 FPS) needs a live check.

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
