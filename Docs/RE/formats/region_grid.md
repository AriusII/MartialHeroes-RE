# Format: region grid  (`.tol` authoring layout / `region<NNN>.bin` runtime layout — map-wide region/zone ID grid)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file:
> `// spec: Docs/RE/formats/region_grid.md`

---

## Status

```
sample_verified: true    # .tol header (4 LE u32, last two = width/height) and W×H byte body confirmed against real bytes (009/013/100)
parser_corroborated: true # cell-is-region-id semantic, 256-unit cell stride, row-major indexing, runtime region.bin layout
```

**Two distinct file layouts describe the same logical grid. Do NOT silently equate them — see the "Two layouts" section below.**

- **`region<NNN>.bin`** — the **runtime** layout. **This is the one the shipped client actually loads and reads.** Origins are stored AFTER the grid body.
- **`.tol`** — the **authoring / tool-side** counterpart. Loose on disk, never opened by the shipped client. Origins (if that is their meaning) lead a 16-byte front header.

---

## Identification

- **Logical role:** A single, map-wide (one per map — NOT per cell) 2D grid of region / zone IDs. Each cell is one unsigned byte naming which **region record** (0..31) governs that patch of the map. The grid is sampled at runtime by world position to answer "which region/zone is this point in?", driving spawn rules, gather points, sound zones, and movement / region-state validation.
- **Runtime file:** `data/map<NNN>/region<NNN>.bin` — loaded by name by the shipped client (`<NNN>` is the map number). This is the format the engine consumes.
- **Authoring file:** `data/map<NNN>/<NNN>.tol` — loose on disk (not packed inside `data.vfs`); the VFS lister matched zero `.tol` entries. No `.tol` filename is referenced by the shipped client; the engine never opens a `.tol`.
- **Magic / signature:** none — neither layout has a magic value.
- **Version field:** none.
- **Endianness:** little-endian throughout (x86 client). All multi-byte fields decode sanely as LE.
- **Cell type:** single unsigned byte (`u8`) = region ID.
- **Cell-to-world stride:** **256 world units per cell** (the divisor used by the runtime region indexer) — CONFIRMED (parser).

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

The `.tol` samples place all four `u32`s in a 16-byte FRONT header, then the `width × height` grid body. The first two header fields are proposed to be `originX` / `originZ` by analogy with the runtime indexer (which subtracts a stored origin), but their exact meaning is not debugger-confirmed because the engine never loads a `.tol`.

```
+0x00             field0 / proposed originX (i32, signed)
+0x04             field1 / proposed originZ (i32, signed)
+0x08             width  (u32)
+0x0C             height (u32)
+0x10             regionIdGrid : width × height bytes, row-major
total size      = 16 + (width × height)
```

| Offset | Size           | Type | Field                  | Notes                                                          | Confidence        |
|-------:|----------------|------|------------------------|----------------------------------------------------------------|-------------------|
| 0x00   | 4              | i32  | `field0` (≈ `originX`) | Proposed world-space X origin (signed); order origin-then-dims inferred | MED (sample only) |
| 0x04   | 4              | i32  | `field1` (≈ `originZ`) | Proposed world-space Z origin (signed)                         | MED (sample only) |
| 0x08   | 4              | u32  | `width`                | Grid columns (cells along X)                                   | HIGH (sample)     |
| 0x0C   | 4              | u32  | `height`               | Grid rows (cells along Z)                                      | HIGH (sample)     |
| 0x10   | width × height | u8[] | `regionIdGrid`         | Row-major region IDs                                           | HIGH (sample)     |

### The distinction, stated plainly

| Aspect             | `.tol` (authoring)                   | `region<NNN>.bin` (runtime)                    |
|--------------------|--------------------------------------|------------------------------------------------|
| Read by client?    | **No** — never opened by the engine  | **Yes** — the shipped client loads this        |
| Origin placement   | FRONT, leading a 16-byte header      | END, trailing the grid body                    |
| Field order        | `originX, originZ, width, height, grid` | `width, height, grid, originX, originZ`      |
| On disk            | loose (not in `data.vfs`)            | loaded by name per active map area             |
| Total size         | `16 + W×H`                           | `16 + W×H` (same total)                         |
| Grid size/meaning  | identical                            | identical                                      |
| Cell stride        | 256 world units                      | 256 world units                                |

Same total size, same grid size, same cell semantic — **only the header arrangement differs**. They are the tool-side source and the runtime-repacked product of the same grid; treat them as two formats, not one.

---

## Grid body layout (both layouts)

| Region | Size           | Type | Field          | Notes                                  |
|--------|----------------|------|----------------|----------------------------------------|
| body   | width × height | u8[] | `regionIdGrid` | row-major; one region ID per cell      |

- **Element type:** single unsigned byte = region ID. `0` = none / no-region; `1..31` = a region record slot.
- **Element stride:** 1 byte.
- **Row order:** row-major — `index = col + row × width`. Derived from the runtime indexer `index = (X − originX) / 256 + (Z − originZ) / 256 × width`, with `originX`/`originZ` taken as signed and a 256-unit cell. **CONFIRMED.**
- **Cell-to-world stride:** **256 world units per cell**.
- **Observed values:** in the three sampled maps only IDs `0` and `1` occur, so the body LOOKS like a 0/1 mask, but the type is a full-range region ID (0..31), not a boolean mask — based on the runtime indexer returning the byte and a consumer using it to fetch a region record and test a field within that record.

### Size derivation

- `total = 16 + width × height` (1 byte per cell) for both layouts.
- Sample-verified totals (from `.tol` samples): two maps with `2048 × 2048` grids decode to `16 + 4,194,304 = 4,194,320` bytes; one map with `256 × 256` decodes to `16 + 65,536 = 65,552` bytes — all match exactly.
- World span = `width × 256`: e.g. `2048 × 256 = 524,288` world units across; `256 × 256 = 65,536` world units across.
- **Record count source:** the grid extent comes from the `width`/`height` fields; the body length must equal `width × height` and reconciles with the on-disk file size as `16 + width × height`.

---

## Runtime use

The runtime region grid (`region<NNN>.bin`) is loaded once per active map area, kept in a heap buffer, and queried by world position `(X, Z)`:

1. Translate the world position by the stored origin (`X − originX`, `Z − originZ`), with the origins read as **signed** 32-bit integers.
2. Divide each axis by the 256-unit cell stride.
3. Compute the row-major index and bounds-check against `width × height`.
4. Return the cell byte as a **region ID**.

The returned ID indexes a 32-slot **region table** (`regiontable<NNN>.bin`, exactly `32 × 48 = 1,536` bytes — see below) to obtain per-region properties. The same indexer is reached from spawn, gather, sound-zone, and movement / region-state code, making the region grid **the authoritative spatial partition of a map into named zones/regions**.

> **Active-region note (CONFIRMED).** For the **player's own** position the active region id is **not** read from this grid — it is a separate value **pushed by the server** into the world-state tick (the local-player status handler and the per-tick game-state handler). The local grid lookup above is used to classify a **target actor's** position. See `Docs/RE/specs/world_systems.md §16.3`.

---

## `regiontable<NNN>.bin` — region-properties record layout

A fixed **32 records × 48 bytes = 1,536 bytes** table, indexed directly by region id (`0..31`). A region id `≥ 32` has no record and is treated as the default. Any bytes present on disk **beyond offset 1536** (the end of the 32nd record) are ignored by the loader.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 40 | char[40] | `zoneName` | NUL-terminated zone display-name string (the **minimap sub-zone caption**). Three minimap/HUD label sites read the record base pointer directly as a C-string starting at offset +0. | HIGH (read as a `char*` by 3 UI sites; the exact 40-byte width is parser-derived from the `48 − 4 − 4` layout, **not** sample-confirmed) |
| +0x28 | 4 | u32 | `zoneType` | Zone-type enum (see below). The **only numeric field any region-gating path reads**. | CONFIRMED |
| +0x2C | 4 | (opaque) | `_tail` | Trailing dword; **no reader found** in any path. | UNVERIFIED |

- **Record count:** fixed 32. **Record stride:** **48 bytes — CONFIRMED (RE-AFFIRMED).** **Index source:** the region-id byte from the grid (Layout A) or the server-pushed active region id.
- **Stride is 48, not 32 (note on the conflation).** An interim reading proposed a 32-byte stride; that is **REFUTED**. The same on-disk loader that walks this region table also reads the **28-byte `npc.arr` spawn record** (see `npc_spawns.md`). The "32" figure is a conflation of that adjacent 28-byte record path with this table; the region table record itself is firmly **48 bytes**, which is the only stride that reconciles the table size to the fixed `32 × 48 = 1,536` byte block. The two record sizes belong to two different on-disk structures the loader handles in sequence — they must not be merged.
- **One file, two roles (unified).** The same `regiontable<NNN>.bin` is BOTH the minimap sub-zone **label** source (the `zoneName` string at +0) AND the zone-type **gating** source (the `zoneType` enum at +40). These were previously treated as two unrelated reads of the same file; they are the two named fields of one record. The earlier "`+0..+39` opaque/unread" reading is **superseded** — those bytes are the zone-name string.

### `zoneType` enum (offset +40) — CONFIRMED-COMPLETE at three values

The zone type is a small **enumerated value**, **never a packed bitmask** — every consuming site does an equality / inequality / truthiness compare (`== 1`, `!= 1`, `== 2`, `!= 0`), and the richest consumer (the minimap renderer) switches on `{0, 1, 2, default}` with **no arm for any value ≥ 3** and **no bit test anywhere**. The set of values the client distinguishes is therefore **exactly `{0, 1, 2}`**.

| Value | Meaning | Confidence |
|------:|---------|------------|
| `0`   | **Safe / no-combat** zone (the combat arbiter yields the denied mode; the minimap renders this as a distinct safe caption/colour). | CONFIRMED (behaviour); label PLAUSIBLE |
| `1`   | **Open PvP / combat-enabled** zone — the only "permitted" type; flagged-PvP gates require **both** the active and the target region to be type 1. | CONFIRMED |
| `2`   | **Movement-restricted / closed** zone — entry/movement is denied (the move is rejected, a localized message is shown, the actor is snapped back); also a distinct non-open combat mode. | CONFIRMED |
| `3..31` | **Not modelled.** No site compares against any value ≥ 3; all such ids fall through to the default and are treated like the safe case. | CONFIRMED-COMPLETE (3..31 = default) |

This **removes** the earlier "`3..31` undetermined" residual doubt: the enum is **complete at three values**, with `3..31` rolling into the default (safe) behaviour at every site.

---

## `mapsetting<NNN>.bin` — per-map setting record

A per-map settings record read on the same map-load path as the region grid. Only the fields the loader actually reads are documented as load-bearing; fields the load path does not touch are marked DBG-pending and left opaque — they must not be invented.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x3C | 1 | u8 (enum) | `mode` | **Mode enum.** The loader branches on this byte by equality (`== 3` selects a distinct mode). Small enumerated value, **not padding**. The full value set beyond the tested member is not yet enumerated. | CONFIRMED (role); value set partial |
| +0x44 | 4 | (opaque) | `_dbg44` | **Unread on this load path.** No reader observed; meaning undetermined. | DBG-pending |
| +0x48 | 4 | (opaque) | `_dbg48` | **Unread on this load path.** No reader observed; meaning undetermined. | DBG-pending |
| +0x4C | 4 | (opaque) | `_dbg4C` | **Unread on this load path.** No reader observed; meaning undetermined. | DBG-pending |
| +0x50 | 1 | u8 (flag) | `nameMask` | **Name-mask flag.** The loader tests this byte by equality (`== 1` enables the masked-name behaviour). A flag, **not padding**. | CONFIRMED (role) |

- **`+0x3C` and `+0x50` reclassified.** Both were previously treated as padding. They are **not** padding: `+0x3C` is a byte **mode enum** the loader tests for equality (`== 3`), and `+0x50` is a byte **name-mask flag** the loader tests for equality (`== 1`). Reclassify them from padding to these roles.
- **`+0x44 / +0x48 / +0x4C` are opaque (DBG-pending).** This load path does not read them. Their type/width above is provisional sizing only; their semantics need live-debugger confirmation and must not be guessed at during implementation.

---

## Enumerations / flags

### Region ID (cell byte)

| Value | Meaning                                          |
|------:|--------------------------------------------------|
| 0     | none / no-region                                 |
| 1..31 | region record slot — index into the region table |

Only `0` and `1` are observed in the available `.tol` samples; the valid range `0..31` follows from the 32-slot region table.

### Zone type (`regiontable` record +40)

See the `zoneType` enum table above — `{0 safe, 1 open-PvP, 2 movement-restricted/closed}`, CONFIRMED-COMPLETE.

### Map-setting mode (`mapsetting` record +0x3C)

A byte mode enum; the loader distinguishes the member `3` by equality. The remaining members are not yet enumerated (the load path only branches on `== 3`).

### Map-setting name-mask (`mapsetting` record +0x50)

A byte flag; the loader enables a name-masking behaviour when the value equals `1`.

---

## Known unknowns

The following remain unresolved and must not be guessed at during implementation:

1. **`.tol` `field0`/`field1` exact meaning** — proposed `originX`/`originZ` by analogy with the runtime indexer, MED confidence. Not debugger-confirmed, because the shipped client never loads a `.tol`. Engineers implementing the runtime path should rely on Layout A (`region.bin`) origins, which ARE parser-confirmed (and signed).
2. **Whether `.tol` cells ever exceed 1** — only IDs `0`/`1` seen in the three available samples. A map with higher IDs would confirm the id-not-mask thesis from the data side as well.
3. **Endianness** — assumed little-endian (x86 MSVC client); all fields decode sanely as LE. Not independently confirmed by a big-endian counter-sample (none exists for this platform).
4. **Whether the engine ever consumes `.tol` directly** — none found. `.tol` appears to be purely the tool/source artifact later repacked into the runtime `region.bin`.
5. **`.tol` ↔ `region.bin` content match for the same map** — not verified by matching a `.tol` against the `region.bin` of the same map by content; the header-order conflict therefore cannot be cross-resolved here.
6. **`zoneName` exact byte width / encoding** — asserted 40 bytes by the `48 − 4 − 4` layout and consumed as a `char*`, but not byte-sampled. A `regiontable<NNN>.bin` hex sample would confirm the name length and whether `+44` is padding versus a continuation of the name. The game text encoding is CP949.
7. **`regiontable` record `_tail` (+44)** — the only genuinely-unread dword in the record; meaning undetermined.
8. **`mapsetting` fields `+0x44` / `+0x48` / `+0x4C`** — unread on the map-load path; semantics need a live debugger to settle. Provisional sizing only; do not model behaviour.
9. **`mapsetting` `+0x3C` mode-enum value set** — only the member `3` is distinguished on the load path; the other members and their meanings are not yet enumerated.

---

## Cross-references

### Related formats

| Format / file        | File                              | Relationship                                                        |
|----------------------|-----------------------------------|--------------------------------------------------------------------|
| `regiontable.bin`    | `data/map<NNN>/regiontable.bin`   | 32-slot × 48-byte region record table (zoneName + zoneType + tail), indexed by the cell region ID |
| `mapsetting.bin`     | `data/map<NNN>/mapsetting.bin`    | Per-map setting record read on the same map-load path (mode enum +0x3C, name-mask flag +0x50, opaque +0x44/+0x48/+0x4C) |
| `npc_spawns.md`      | `data/map<NNN>/npc<NNN>.arr` etc. | Spawn placement; the **28-byte `npc.arr` record** is read by the same loader, and is the source of the refuted "32-byte" region-table stride conflation |
| `terrain.md`         | `data/map<NNN>/*.ted` etc.        | Per-cell terrain; the region grid is the coarser map-wide partition |
| `pak.md`             | `data.inf` / `data/data.vfs`      | VFS container (note: `region.bin` loaded by name; `.tol` is loose)  |

### Subsystem specs

- `Docs/RE/specs/world_systems.md §16` — the region/zone gating subsystem (movement / combat / PvP gates, the server-authoritative active-region push, the cosmetic-only quest/event verdict, and the per-cell `.mud` sound grid).

### Coordinate system

World coordinates use X (east/west) and Z (north/south) as the horizontal plane. The region grid is sampled by subtracting the stored (signed) origin and dividing by the 256-unit cell stride. Y (vertical) is not part of the region grid.

- **Glossary:** see `Docs/RE/names.yaml`. Names flagged for the glossary: `RegionGridFile` / `TolFile`, `RegionGridHeader`, `RegionId` (u8), `RegionGrid.CellStrideWorldUnits = 256`, `RegionGridOrigin` (i32 signed X/Z), `RegionRecord { char zoneName[40]; u32 zoneType; u32 _tail; }`, `RegionZoneType { Safe=0, OpenPvp=1, Closed=2 }`, `MapSettingRecord { u8 mode@+0x3C; u8 nameMask@+0x50; opaque +0x44/+0x48/+0x4C }`.
- **Provenance:** see `Docs/RE/journal.md`. CAMPAIGN VFS-MASTERY two-witness promotion (region-table loader + black-box layout): regiontable stride RE-AFFIRMED 48; origins refined to signed i32; mapsetting `+0x3C` mode enum and `+0x50` name-mask reclassified from padding; `+0x44/+0x48/+0x4C` left DBG-pending.
