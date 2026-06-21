# Format: region grid  (`.tol` authoring layout / `region<NNN>.bin` runtime layout — map-wide region/zone ID grid)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file:
> `// spec: Docs/RE/formats/region_grid.md`

---

## Status

```
verification:   sample-verified   # runtime region.bin layout, regiontable stride/size, .tol layout — all matched against a real VFS sample
ida_reverified: 2026-06-20
ida_anchor:     263bd994
evidence:       [static-ida, vfs-sample]
conflicts:      none-open         # the 5 campaign-10 conflicts (C1–C5) are RESOLVED into this revision (see below)
# CYCLE 7 note: re-confirmed against the live region/area loader (IDB SHA 263bd994, CYCLE 7 (2026-06-20)) — the
# 32×48 region table with zoneType@+0x28, the 256-unit region cell, and the two-region combat-mode resolution rule
# are CONFIRMED via static IDA; the d<NNN>.lst count-prefixed cell-list sub-format (dungeon 201–210 cell counts are
# data-driven, NOT a binary constant) is newly CONFIRMED via static IDA. Sections added below.
```

Two-witness re-verification on build `263bd994` (the per-map area loader + a 60-map `region.bin`
corpus + the 3 in-VFS `.tol` samples) RE-CONFIRMED the core layout and **corrected five facts** the
earlier revision had wrong. The corrected facts (each folded in below):

1. **Cell byte is a region-record INDEX spanning the full `0..31` range**, NOT a `0/1` mask — the
   60-map runtime corpus exercises IDs `{0..24, 30, 31}` (26 of 60 maps carry IDs > 2).
2. **`zoneType` on-disk union is `{0, 1, 2, 9}`** — the value `9` appears once on disk; the consumer
   set is `{0, 1, 2}` + default, so `9` is handled as default (benign). Consumer-complete ≠
   on-disk-exhaustive.
3. **The per-map binary the loader reads is `map<NNN>.bin` (520 bytes = `0x208`)**, NOT
   `mapsetting<NNN>.bin` — the `mode` / `nameMask` fields live in this record. `mapsetting.scr`
   (a separate `data/script/` script) is a different file.
4. **3 `.tol` files DO live inside `data.vfs`** (`map009/009.tol`, `map013/013.tol`,
   `map100/100.tol`) — the earlier "loose on disk / VFS lister matched zero `.tol`" verdict is
   REVERTED. (The engine still never *opens* a `.tol` — no `.tol` path literal is referenced.)
5. **`.tol` front origins MATCH the same map's `region.bin` trailing origins** (cross-matched on
   map013 and map100) — confirming `.tol field0/field1 = originX/originZ`.

**Two distinct file layouts describe the same logical grid. Do NOT silently equate them — see the "Two layouts" section below.**

- **`region<NNN>.bin`** — the **runtime** layout. **This is the one the shipped client actually loads and reads.** Origins are stored AFTER the grid body.
- **`.tol`** — the **authoring / tool-side** counterpart. Present inside `data.vfs` for 3 maps, but never *opened* by the shipped client. Origins lead a 16-byte front header (cross-confirmed to equal the runtime trailing origins).

---

## Identification

- **Logical role:** A single, map-wide (one per map — NOT per cell) 2D grid of region / zone IDs. Each cell is one unsigned byte = a **region-record INDEX in `0..31`** naming which region record governs that patch of the map (the runtime corpus exercises the full `0..31` range — see "Grid body layout"). The grid is sampled at runtime by world position to answer "which region/zone is this point in?", driving spawn rules, gather points, sound zones, and movement / region-state validation.
- **Runtime file:** `data/map<NNN>/region<NNN>.bin` — loaded by name by the shipped client (`<NNN>` is the map number). This is the format the engine consumes. The full per-area corpus (60 `region*.bin` entries) is present in the VFS and decodes byte-exact as Layout A.
- **Authoring file:** `data/map<NNN>/<NNN>.tol` — the authoring / tool-side counterpart. **CORRECTED: 3 `.tol` files DO live inside `data.vfs`** (`data/map009/009.tol`, `data/map013/013.tol`, `data/map100/100.tol`); the earlier "loose on disk / VFS lister matched zero `.tol`" reading is REVERTED. The engine **still never opens a `.tol`** — no `.tol` filename is referenced by the shipped client, so the in-VFS `.tol` files are authoring artifacts that ride inside the archive but have no runtime read-site.
- **Magic / signature:** none — neither layout has a magic value.
- **Version field:** none.
- **Endianness:** little-endian throughout (x86 client). All multi-byte fields decode sanely as LE.
- **Cell type:** single unsigned byte (`u8`) = region ID.
- **Cell-to-world stride:** **256 world units per cell** (the divisor used by the runtime region indexer) — CONFIRMED (parser).

> **⚠️ Do NOT confuse the region cell size with the terrain cell size.** The **region** grid cell is **256 world units** (its own coarse spatial overlay). This is **DISTINCT** from the **terrain** cell, which is **1024 world units** on a 65×65 grid (see `terrain.md` / the coordinate conventions). The region grid is a separate, coarser partition with its own `width`/`height`/origins read from `region<NNN>.bin` — an engineer must index it with the 256-unit stride, never the 1024-unit terrain stride. CONFIRMED via static IDA (CYCLE 7).

---

## Two layouts — side by side

Both layouts encode the same logical content: four 32-bit integers (origin X, origin Z, width, height) plus a `width × height` byte grid of region IDs, 256 world units per cell. They differ ONLY in **where the four integers sit relative to the grid body**.

### Layout A — `region<NNN>.bin` (RUNTIME — what the shipped client reads)

The runtime loader reads `width` and `height` at the FRONT, then the `width × height` grid body, then `originX` and `originZ` at the END (origins trail the grid).

```
+0                width (u32)
+4                height (u32)
+8                regionIdGrid : width × height bytes, row-major
+8 + W×H          originX (i32, signed)
+8 + W×H + 4      originZ (i32, signed)
total size      = 8 + (width × height) + 8 = 16 + width × height
```

| Region                | Size            | Type  | Field         | Notes                                              | Confidence       |
|-----------------------|-----------------|-------|---------------|----------------------------------------------------|------------------|
| 0x00                  | 4               | u32   | `width`       | Grid columns (cells along X)                       | HIGH (parser)    |
| 0x04                  | 4               | u32   | `height`      | Grid rows (cells along Z)                          | HIGH (parser)    |
| 0x08                  | width × height  | u8[]  | `regionIdGrid`| Row-major region IDs (index = col + row × width)   | HIGH (parser)    |
| 0x08 + W×H            | 4               | i32   | `originX`     | World-space X origin (signed) subtracted before indexing | CONFIRMED   |
| 0x08 + W×H + 4        | 4               | i32   | `originZ`     | World-space Z origin (signed) subtracted before indexing | CONFIRMED   |

- **This is the layout an `Assets.Parsers` engineer should implement for the live game**, because it is the only one the engine opens. Read `width`/`height` first, then the body, then the two trailing origins.
- **Origin signedness (CONFIRMED).** The two trailing origin fields are **signed 32-bit integers** (`i32`), so a map whose authored extent begins at negative world coordinates is addressed correctly. The runtime indexer computes `(X − originX) / 256` with signed arithmetic.

### Layout B — `.tol` (AUTHORING / TOOL-SIDE — not read by the shipped client)

The `.tol` samples place all four `u32`s in a 16-byte FRONT header, then the `width × height` grid body. The first two header fields are `originX` / `originZ` — **now CONFIRMED**: cross-matching the 3 in-VFS `.tol` samples against the same map's `region<NNN>.bin` shows the `.tol` front origins **equal** the runtime trailing origins byte-for-byte (`map013.tol` `(8192, 57344)` == `region013.bin` `(8192, 57344)`; `map100.tol` `(23552, 55296)` == `region100.bin` `(23552, 55296)`). This resolves the earlier MED "proposed by analogy" doubt and settles former known-unknowns #1 and #5. (The engine still never *loads* a `.tol`; the confirmation comes from the cross-match, not from a runtime read.)

```
+0x00             originX (i32, signed)
+0x04             originZ (i32, signed)
+0x08             width  (u32)
+0x0C             height (u32)
+0x10             regionIdGrid : width × height bytes, row-major
total size      = 16 + (width × height)
```

| Offset | Size           | Type | Field                  | Notes                                                          | Confidence        |
|-------:|----------------|------|------------------------|----------------------------------------------------------------|-------------------|
| 0x00   | 4              | i32  | `originX`              | World-space X origin (signed); cross-matched to `region.bin` trailing originX (same map) | CONFIRMED (cross-match) |
| 0x04   | 4              | i32  | `originZ`              | World-space Z origin (signed); cross-matched to `region.bin` trailing originZ (same map) | CONFIRMED (cross-match) |
| 0x08   | 4              | u32  | `width`                | Grid columns (cells along X)                                   | HIGH (sample)     |
| 0x0C   | 4              | u32  | `height`               | Grid rows (cells along Z)                                      | HIGH (sample)     |
| 0x10   | width × height | u8[] | `regionIdGrid`         | Row-major region IDs                                           | HIGH (sample)     |

### The distinction, stated plainly

| Aspect             | `.tol` (authoring)                   | `region<NNN>.bin` (runtime)                    |
|--------------------|--------------------------------------|------------------------------------------------|
| Read by client?    | **No** — never opened by the engine  | **Yes** — the shipped client loads this        |
| Origin placement   | FRONT, leading a 16-byte header      | END, trailing the grid body                    |
| Field order        | `originX, originZ, width, height, grid` | `width, height, grid, originX, originZ`      |
| On disk            | **inside `data.vfs`** for 3 maps (009/013/100); never *opened* by the engine | loaded by name per active map area |
| Total size         | `16 + W×H`                           | `16 + W×H` (same total)                         |
| Grid size/meaning  | identical                            | identical                                      |
| Cell stride        | 256 world units                      | 256 world units                                |
| Origins match?     | front origins == the same map's runtime trailing origins (CONFIRMED cross-match) | trailing origins are the source of truth |

Same total size, same grid size, same cell semantic — **only the header arrangement differs**. They are the tool-side source and the runtime-repacked product of the same grid; treat them as two formats, not one.

---

## Grid body layout (both layouts)

| Region | Size           | Type | Field          | Notes                                  |
|--------|----------------|------|----------------|----------------------------------------|
| body   | width × height | u8[] | `regionIdGrid` | row-major; one region ID per cell      |

- **Element type:** single unsigned byte = region-record **INDEX**. `0` = none / no-region; `1..31` = a region record slot.
- **Element stride:** 1 byte.
- **Row order:** row-major — `index = col + row × width`. Derived from the runtime indexer `index = (X − originX) / 256 + (Z − originZ) / 256 × width`, with `originX`/`originZ` taken as signed and a 256-unit cell. **CONFIRMED.**
- **Cell-to-world stride:** **256 world units per cell**.
- **Observed values — the full `0..31` range IS exercised (CORRECTED, sample-verified).** A scan of the **60-map runtime `region.bin` corpus** found the union of cell IDs = `{0..24, 30, 31}`, with **26 of 60 maps carrying IDs > 2** (up to 31). This is the strongest data-side proof that the cell byte is a region-record INDEX, not a `0/1` mask. The earlier "only IDs `0`/`1` occur" observation was an artifact of the 3 `.tol` samples alone; the runtime corpus REFUTES any `0/1`-mask reading and VINDICATES the index-not-mask thesis from the data side. (Former known-unknown #2 — "whether cells ever exceed 1" — is thereby resolved: yes, far beyond 1.)

### Size derivation

- `total = 16 + width × height` (1 byte per cell) for both layouts.
- Sample-verified totals (runtime `region.bin`): all **60** per-area files decode byte-exact as `16 + W×H` (e.g. `map001` `W=60 H=68` → `16 + 4080 = 4096` bytes; `map002` `W=32 H=52` → `16 + 1664 = 1680` bytes).
- Sample-verified totals (`.tol` samples): the two `2048 × 2048` maps (009/013) decode to `16 + 4,194,304 = 4,194,320` bytes; the `256 × 256` map (100) decodes to `16 + 65,536 = 65,552` bytes — all match exactly.
- World span = `width × 256`: e.g. `2048 × 256 = 524,288` world units across; `256 × 256 = 65,536` world units across.
- **Record count source:** the grid extent comes from the `width`/`height` fields; the body length must equal `width × height` and reconciles with the on-disk file size as `16 + width × height`.

---

## Runtime use

The runtime region grid (`region<NNN>.bin`) is loaded once per active map area, kept in a heap buffer, and queried by world position `(X, Z)`:

1. Translate the world position by the stored origin (`X − originX`, `Z − originZ`), with the origins read as **signed** 32-bit integers.
2. Divide each axis by the 256-unit cell stride.
3. Compute the row-major index and bounds-check against `width × height`.
4. Return the cell byte as a **region ID**.

The returned ID indexes a 32-slot **region table** (`regiontable<NNN>.bin` — `32 × 48 = 1,536` bytes of records that the loader reads, inside a `1,664`-byte on-disk file; see below) to obtain per-region properties. The same indexer is reached from spawn, gather, sound-zone, and movement / region-state code, making the region grid **the authoritative spatial partition of a map into named zones/regions**.

> **Active-region note (CONFIRMED).** For the **player's own** position the active region id is **not** read from this grid — it is a separate value **pushed by the server** into the world-state tick (the local-player status handler and the per-tick game-state handler). The local grid lookup above is used to classify a **target actor's** position. See `Docs/RE/specs/world_systems.md §16.3`.

### Combat-mode resolution — the two-region rule (consumer contract) — CONFIRMED (static IDA, CYCLE 7)

The decision "is this point in a safe / PvP / closed combat context" is **not** taken from a single region record. The combat-mode arbiter reads **TWO** region records and combines their `zoneType` fields, with **PvP winning the boundary**:

1. The record for the cached **current** region id (the active region described in the note above).
2. The record for the region looked up at the actor's world `(X, Z)` via the grid indexer above (`region<NNN>.bin` → cell byte → region-table record).

Each record contributes its `zoneType` (offset +0x28). If a record pointer is absent, that endpoint is treated as the **PvP** default (`1`). The resolved combat-mode is then:

| Condition on the two endpoints' `zoneType` | Resolved combat-mode | Meaning |
|---|--:|---|
| **Either** endpoint `zoneType == 1` | `1` | **PvP** — free fight (the OR of the two endpoints; PvP wins the boundary) |
| Else **both** endpoints non-zero (neither is `1`) | `2` | **Closed / restricted** combat context |
| Else (some endpoint `zoneType == 0`) | `0` | **Safe** — no combat |

> **The "can I attack" gate.** A player-versus-player attack is permitted **only when the resolved combat-mode `== 1`** (i.e. at least one of the two endpoints is an open-PvP region). Any other resolved value forbids the player attack. This is the load-bearing consumer rule for the region grid: **`can-attack ⟺ combat-mode == 1`**. (Additional non-region gates — the per-map peace policy on the `map<NNN>.bin` record, target-style and level checks — are layered on top; see `Docs/RE/specs/world_systems.md §16`.) CONFIRMED via static IDA (CYCLE 7).

So crossing from a safe cell into a PvP cell (or vice-versa) is decided by the **OR** of the two endpoints — a faithful port must resolve combat-mode from *both* the active region and the target's region, not from one cell alone. The richer movement-restriction gate additionally reads `zoneType` at the **destination** cell and refuses movement when it is `2` (the closed case).

---

## `regiontable<NNN>.bin` — region-properties record layout

A fixed **32 records × 48 bytes = 1,536 bytes** of records that the loader reads, indexed directly
by region id (`0..31`). A region id `≥ 32` has no record and is treated as the default.

**On-disk size is `1,664` bytes (CORRECTED), not `1,536`.** The real file is `1,536` bytes of
record data (`32 × 48`) followed by a **`128`-byte trailer the loader never reads**
(`1,536 + 128 = 1,664`). The loader's guard accepts a file of at least `1,536` bytes and reads
only that record block; any bytes **beyond offset 1,536** (including the 128-byte trailer) are
ignored. A faithful parser must read the `1,536`-byte record block and skip the trailing
`128` bytes — do not treat `1,664` as the record region, and do not treat `1,536` as the whole
file. (CONFIRMED two-witness: the loader bulk-reads `0x600 = 1,536`; the on-disk files measure
`1,664`, with CP949 zone-name strings byte-aligning at record offsets `0 / 48 / 96`, which
independently confirms the 48-byte record stride.)

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 40 | char[40] | `zoneName` | NUL-terminated zone display-name string (the **minimap sub-zone caption**). Three minimap/HUD label sites read the record base pointer directly as a C-string starting at offset +0. | HIGH (read as a `char*` by 3 UI sites; the exact 40-byte width is parser-derived from the `48 − 4 − 4` layout, **not** sample-confirmed) |
| +0x28 | 4 | u32 | `zoneType` | Zone-type enum (see below). The **only numeric field any region-gating path reads**. | CONFIRMED |
| +0x2C | 4 | (opaque) | `_tail` | Trailing dword; **no reader found** in any path. | UNVERIFIED |

- **Record count:** fixed 32. **Record stride:** **48 bytes — CONFIRMED (RE-AFFIRMED).** **On-disk file size:** `1,664` bytes = `1,536` record data + `128`-byte unread trailer (CONFIRMED). **Index source:** the region-id byte from the grid (Layout A) or the server-pushed active region id.
- **Stride is 48, not 32 (note on the conflation).** An interim reading proposed a 32-byte stride; that is **REFUTED**. The same on-disk loader that walks this region table also reads the **28-byte `npc.arr` spawn record** (see `npc_spawns.md`). The "32" figure is a conflation of that adjacent 28-byte record path with this table; the region table record itself is firmly **48 bytes**, which is the only stride that reconciles the table size to the fixed `32 × 48 = 1,536` byte block. The two record sizes belong to two different on-disk structures the loader handles in sequence — they must not be merged.
- **One file, two roles (unified).** The same `regiontable<NNN>.bin` is BOTH the minimap sub-zone **label** source (the `zoneName` string at +0) AND the zone-type **gating** source (the `zoneType` enum at +40). These were previously treated as two unrelated reads of the same file; they are the two named fields of one record. The earlier "`+0..+39` opaque/unread" reading is **superseded** — those bytes are the zone-name string.

### `zoneType` enum (offset +40) — consumer-complete at `{0, 1, 2}`; on-disk union `{0, 1, 2, 9}`

The zone type is a small **enumerated value**, **never a packed bitmask** — every consuming site does an equality / inequality / truthiness compare (`== 1`, `!= 1`, `== 2`, `!= 0`), and the richest consumer (the minimap renderer) switches on `{0, 1, 2, default}` with **no arm for any value ≥ 3** and **no bit test anywhere**. The set of values the **client distinguishes** is therefore **exactly `{0, 1, 2}`**.

> **CORRECTED nuance (sample-verified) — consumer-complete ≠ on-disk-exhaustive.** A scan of the
> on-disk `regiontable<NNN>.bin` records across the corpus found the `zoneType` union is
> **`{0, 1, 2, 9}`** (histogram `{0: 1796, 1: 110, 2: 13, 9: 1}` — the value `9` appears **once**, in
> `map002` record 14). The client's consumer has no arm for `9`, so it falls through the
> `default` arm and is treated as the safe case (benign). The enum is **complete at the consumer**,
> but a value outside `{0, 1, 2}` **can appear on disk** and is handled as default — a faithful port
> must route any unrecognised `zoneType` (here `9`, and any `≥ 3`) to the default/safe behaviour
> rather than asserting the field is exactly `{0, 1, 2}`.

| Value | Meaning | Confidence |
|------:|---------|------------|
| `0`   | **Safe / no-combat** zone (the combat arbiter yields the denied mode; the minimap renders this as a distinct safe caption/colour). | CONFIRMED (behaviour); label PLAUSIBLE |
| `1`   | **Open PvP / combat-enabled** zone — the only "permitted" type; flagged-PvP gates require **both** the active and the target region to be type 1. | CONFIRMED |
| `2`   | **Movement-restricted / closed** zone — entry/movement is denied (the move is rejected, a localized message is shown, the actor is snapped back); also a distinct non-open combat mode. | CONFIRMED |
| `9` (and any `3..31`) | **Not modelled — routed to default.** No site compares against any value ≥ 3; all such ids fall through to the default and are treated like the safe case. A stray `9` is present on disk (1 record). | CONFIRMED (consumer routes to default); on-disk presence of `9` SAMPLE-VERIFIED |

The earlier "`3..31` undetermined" residual doubt is removed at the **consumer** (every site routes `≥ 3` to default), but the on-disk value set is **`{0, 1, 2, 9}`**, not exactly `{0, 1, 2}` — handle unrecognised values as default.

---

## `map<NNN>.bin` — per-map setting record (CORRECTED filename — was `mapsetting<NNN>.bin`)

> **⚠️ Filename corrected (sample-verified, build `263bd994`).** The per-map settings record the
> area loader reads is **`data/map<NNN>/map<NNN>.bin`**, a **fixed 520-byte (`0x208`) record** — NOT a
> file named `mapsetting<NNN>.bin`. The earlier `mapsetting<NNN>.bin` filename is REVERTED: the VFS
> contains **no** `mapsetting<NNN>.bin`. The `mode` / `nameMask` byte fields below are fields of this
> 520-byte `map<NNN>.bin` record. Do not confuse this with **`data/script/mapsetting.scr`** — a
> separate 4,368-byte script file that is unrelated to the per-map binary. (The per-map area loader
> reads four files in order: `map<NNN>.bin` (520 B), `regiontable<NNN>.bin`, `region<NNN>.bin`,
> `npc<NNN>.arr` (28-byte records).)

A per-map settings record read on the same map-load path as the region grid (it is the **first** of
the four files the loader opens). Only the fields the loader actually reads are documented as
load-bearing; fields the load path does not touch are marked DBG-pending and left opaque — they must
not be invented. The byte offsets below fit inside the 520-byte record; the per-field roles were not
re-traced in the build-`263bd994` two-witness pass, so they stand from the earlier reading
(DBG-pending where flagged).

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x3C | 1 | u8 (enum) | `mode` | **Mode enum.** The loader branches on this byte by equality (`== 3` selects a distinct mode). Small enumerated value, **not padding**. The full value set beyond the tested member is not yet enumerated. | CONFIRMED (role); value set partial |
| +0x44 | 4 | (opaque) | `_dbg44` | **Unread on this load path.** No reader observed; meaning undetermined. | DBG-pending |
| +0x48 | 4 | (opaque) | `_dbg48` | **Unread on this load path.** No reader observed; meaning undetermined. | DBG-pending |
| +0x4C | 4 | (opaque) | `_dbg4C` | **Unread on this load path.** No reader observed; meaning undetermined. | DBG-pending |
| +0x50 | 1 | u8 (flag) | `nameMask` | **Name-mask flag.** The loader tests this byte by equality (`== 1` enables the masked-name behaviour). A flag, **not padding**. | CONFIRMED (role) |

- **Record size:** fixed **520 bytes (`0x208`)** — read in one bounded slurp. SAMPLE-VERIFIED (the loader's fixed `0x208` read).
- **`+0x3C` and `+0x50` reclassified.** Both were previously treated as padding. They are **not** padding: `+0x3C` is a byte **mode enum** the loader tests for equality (`== 3`), and `+0x50` is a byte **name-mask flag** the loader tests for equality (`== 1`). Reclassify them from padding to these roles.
- **`+0x44 / +0x48 / +0x4C` are opaque (DBG-pending).** This load path does not read them. Their type/width above is provisional sizing only; their semantics need live-debugger confirmation and must not be guessed at during implementation.

---

## `d<NNN>.lst` — per-area cell-list (count-prefixed u32 array) — CONFIRMED (static IDA, CYCLE 7)

> **Path:** `data/map<NNN>/dat/d<NNN>.lst` (`<NNN>` is the 3-digit area string). Read on the area-load
> path; it declares **which cells the area populates** and therefore the **per-area cell count**.

The cell list is a trivial **count-prefixed `u32` array**: a leading `u32` count, then that many `u32`
cell-key entries. Each cell-key is fed to the area's cell-registration routine.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | `cellCount` | Number of cell-key entries that follow | CONFIRMED |
| +0x04 | `cellCount × 4` | u32[] | `cellKeys` | One cell-key per populated cell, fed to the cell registration | CONFIRMED |

- **Total size:** `4 + cellCount × 4` bytes; equivalently `cellCount = (fileSize − 4) / 4`.
- **No validation, no per-area constant, no special-case branch.** The loader simply reads the leading
  `u32` and loops that many times — there is **no** hard-coded cell count baked into the binary for any
  area, including dungeons.

### Dungeon area-ids 201–210: cell counts are DATA-DRIVEN (not a binary constant) — CONFIRMED

Dungeon areas (numeric area-ids `201`–`210`) load through the **exact same** area-load path as overworld
areas — there is **no** `area-id ≥ 201` branch anywhere. The area-id is purely a numeric key turned into
the 3-digit path string (e.g. `201 → "201"`), which then fills every per-area path including the cell
list `data/map<NNN>/dat/d<NNN>.lst`.

Consequently the **per-dungeon cell count is whatever its `d<NNN>.lst` declares in its leading `u32`** — it
**cannot** be read from the executable; it must be read from each VFS `.lst` file. The structural file
families and layouts (the 32×48 region table, the 256-unit region grid, the 520-byte `map<NNN>.bin`,
`npc<NNN>.arr` spawns) are **identical** between dungeon and overworld areas; only the **data** differs
(usually a smaller declared cell set, a different `map<NNN>.bin` settings record, and whatever
dimensions/origins that area's `region<NNN>.bin` declares). No client-side **instancing** is visible in
the asset loader — one area's files stream into the same singletons (the prior area is freed first); any
private-instance separation would be a server concern, not an asset-format one.

| Area id | 3-digit string | Cell-list file | Cell count |
|--------:|:--------------:|----------------|-----------:|
| 201 | "201" | `data/map201/dat/d201.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY (read from the VFS) |
| 202 | "202" | `data/map202/dat/d202.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 203 | "203" | `data/map203/dat/d203.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 204 | "204" | `data/map204/dat/d204.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 205 | "205" | `data/map205/dat/d205.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 206 | "206" | `data/map206/dat/d206.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 207 | "207" | `data/map207/dat/d207.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 208 | "208" | `data/map208/dat/d208.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 209 | "209" | `data/map209/dat/d209.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |
| 210 | "210" | `data/map210/dat/d210.lst` | leading `u32` of the `.lst` — RUNTIME-ONLY |

To populate the concrete per-dungeon counts, read the leading `u32` of each `d<NNN>.lst` from the VFS
(a `pak-explore` task). Dungeon **names** likewise come from data (the per-map settings / message
catalogue), not from a binary string keyed on `201`–`210`.

---

## Enumerations / flags

### Region ID (cell byte)

| Value | Meaning                                          |
|------:|--------------------------------------------------|
| 0     | none / no-region                                 |
| 1..31 | region record slot — index into the region table |

The valid range `0..31` follows from the 32-slot region table, and the **runtime `region.bin` corpus exercises the full range** (union `{0..24, 30, 31}` across 60 maps). The earlier "only `0` and `1` observed" was an artifact of the 3 `.tol` samples only — REFUTED by the runtime corpus.

### Zone type (`regiontable` record +40)

See the `zoneType` enum table above — `{0 safe, 1 open-PvP, 2 movement-restricted/closed}` at the consumer; on-disk union is `{0, 1, 2, 9}` (a stray `9` routes to default).

### Map-setting mode (`map<NNN>.bin` record +0x3C)

A byte mode enum; the loader distinguishes the member `3` by equality. The remaining members are not yet enumerated (the load path only branches on `== 3`).

### Map-setting name-mask (`map<NNN>.bin` record +0x50)

A byte flag; the loader enables a name-masking behaviour when the value equals `1`.

---

## Known unknowns

The following remain unresolved and must not be guessed at during implementation:

1. **`.tol` `field0`/`field1` exact meaning** — RESOLVED. Cross-matching the 3 in-VFS `.tol` samples against the same map's `region.bin` trailing origins shows they are equal, confirming `originX`/`originZ`. Engineers implementing the runtime path still rely on Layout A (`region.bin`) origins, which are parser-confirmed and signed.
2. **Whether region cells ever exceed 1** — RESOLVED. The runtime `region.bin` corpus exercises the full `0..31` range (union `{0..24, 30, 31}`, 26/60 maps > 2), confirming the index-not-mask thesis from the data side. (The earlier doubt was scoped to the 3 `.tol` samples.)
3. **Endianness** — assumed little-endian (x86 MSVC client); all fields decode sanely as LE. Not independently confirmed by a big-endian counter-sample (none exists for this platform).
4. **Whether the engine ever consumes `.tol` directly** — none found (no `.tol` path literal is referenced by the loader). `.tol` is the tool/source artifact later repacked into the runtime `region.bin`; 3 such `.tol` files ride inside `data.vfs` (009/013/100) but are never opened.
5. **`.tol` ↔ `region.bin` content match for the same map** — RESOLVED for the origin fields: the `.tol` front origins equal the runtime trailing origins (map013, map100 cross-matched), so the former header-order doubt is cross-resolved. (A full grid-body byte-match was not performed; the size and origin agreement suffice for the layout claim.)
6. **`zoneName` exact byte width / encoding** — asserted 40 bytes by the `48 − 4 − 4` layout and consumed as a `char*` (the record base is a CP949 string region preceding `zoneType` at +0x28), but not byte-sampled for the exact field width. A `regiontable<NNN>.bin` hex sample would confirm the name length and whether `+44` is padding versus a continuation of the name. The game text encoding is CP949.
7. **`regiontable` record `_tail` (+44)** — the only genuinely-unread dword in the record; meaning undetermined.
8. **`map<NNN>.bin` fields `+0x44` / `+0x48` / `+0x4C`** — unread on the map-load path; semantics need a live debugger to settle. Provisional sizing only; do not model behaviour.
9. **`map<NNN>.bin` `+0x3C` mode-enum value set** — only the member `3` is distinguished on the load path; the other members and their meanings are not yet enumerated.

---

## Cross-references

### Related formats

| Format / file        | File                              | Relationship                                                        |
|----------------------|-----------------------------------|--------------------------------------------------------------------|
| `regiontable.bin`    | `data/map<NNN>/regiontable.bin`   | 32-slot × 48-byte region record table (zoneName + zoneType + tail), indexed by the cell region ID |
| `map<NNN>.bin`       | `data/map<NNN>/map<NNN>.bin`      | Per-map setting record (520 B = `0x208`) read FIRST on the map-load path (mode enum +0x3C, name-mask flag +0x50, opaque +0x44/+0x48/+0x4C). **CORRECTED filename** — was `mapsetting<NNN>.bin`; do not confuse with `data/script/mapsetting.scr` (a separate 4,368-byte script). |
| `npc_spawns.md`      | `data/map<NNN>/npc<NNN>.arr` etc. | Spawn placement; the **28-byte `npc.arr` record** is read by the same loader (right after `region.bin`), and is the source of the refuted "32-byte" region-table stride conflation |
| `d<NNN>.lst`         | `data/map<NNN>/dat/d<NNN>.lst`     | Per-area cell list (`[u32 count][count × u32 cell-key]`) read on the same area-load path; the data-driven source of each area's cell count, incl. dungeon area-ids 201–210 |
| `terrain.md`         | `data/map<NNN>/*.ted` etc.        | Per-cell terrain; the region grid (256-unit cell) is the **coarser** map-wide partition — **distinct** from the 1024-unit / 65×65 terrain cell grid; do not conflate the two cell sizes |
| `pak.md`             | `data.inf` / `data/data.vfs`      | VFS container (note: `region.bin` loaded by name; `.tol` is loose)  |

### Subsystem specs

- `Docs/RE/specs/world_systems.md §16` — the region/zone gating subsystem (movement / combat / PvP gates, the server-authoritative active-region push, the cosmetic-only quest/event verdict, and the per-cell `.mud` sound grid).

### Coordinate system

World coordinates use X (east/west) and Z (north/south) as the horizontal plane. The region grid is sampled by subtracting the stored (signed) origin and dividing by the 256-unit cell stride. Y (vertical) is not part of the region grid.

- **Glossary:** see `Docs/RE/names.yaml`. Names flagged for the glossary: `RegionGridFile` / `TolFile`, `RegionGridHeader`, `RegionId` (u8 index 0..31), `RegionGrid.CellStrideWorldUnits = 256`, `RegionGridOrigin` (i32 signed X/Z), `RegionRecord { char zoneName[40]; u32 zoneType; u32 _tail; }`, `RegionZoneType { Safe=0, OpenPvp=1, Closed=2 }` (consumer set; on-disk may carry a stray 9 → default), `MapBinRecord` (520-byte `map<NNN>.bin`) `{ u8 mode@+0x3C; u8 nameMask@+0x50; opaque +0x44/+0x48/+0x4C }`, `AreaCellListFile` (`d<NNN>.lst` = `{ u32 cellCount; u32 cellKeys[cellCount]; }`), `CombatMode { Safe=0, Pvp=1, Closed=2 }` (resolved from two region records; `can-attack ⟺ Pvp`).
- **Provenance:** see `Docs/RE/journal.md`. CAMPAIGN VFS-MASTERY two-witness promotion (region-table loader + black-box layout): regiontable stride RE-AFFIRMED 48 with on-disk size CORRECTED to 1,664 (1,536 records + 128-byte unread trailer); origins refined to signed i32; map-binary `+0x3C` mode enum and `+0x50` name-mask reclassified from padding; `+0x44/+0x48/+0x4C` left DBG-pending. **CAMPAIGN 10 Block D6 two-witness re-verification (build `263bd994`, area loader + 60-map `region.bin` corpus + 3 in-VFS `.tol`)** corrected five facts: cell byte is a region INDEX exercising the full `0..31` range (REFUTES the `0/1`-mask reading); on-disk `zoneType` union is `{0,1,2,9}` (consumer routes `9` to default); the per-map binary is `map<NNN>.bin` (520 B), NOT `mapsetting<NNN>.bin`; 3 `.tol` files DO live inside `data.vfs`; `.tol` front origins MATCH the runtime trailing origins (resolves the field0/field1 = originX/originZ question). **CYCLE 7 (IDB SHA 263bd994, CYCLE 7 (2026-06-20))** added, from the live region/area loader (static IDA): the **two-region combat-mode resolution rule** (combat-mode resolved from the active region record AND the target's region record; PvP wins the OR; `can-attack ⟺ combat-mode == 1`); the explicit **256-unit region cell vs 1024-unit terrain cell** callout (the two cell sizes must not be conflated); and the new **`d<NNN>.lst` count-prefixed cell-list sub-format** (`[u32 count][count × u32]`) establishing that dungeon area-ids 201–210 have **data-driven** cell counts read from the VFS, not hard-coded constants. No prior fact was contradicted — CYCLE 7 re-confirmed the existing 32×48 / zoneType@+0x28 / 256-unit constants and extends them.
