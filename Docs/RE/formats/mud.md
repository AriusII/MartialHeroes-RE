# Format: .mud  (per-cell ambient-sound zone grid)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> C# parsers MUST cite `// spec: Docs/RE/formats/mud.md` on every magic constant / offset.

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `sample-verified` — real asset sample available and cross-checked against parser read-sequence |
| `sample_verified` | `true` — fixed file size, 8-byte tile cadence, and per-byte field interpretation confirmed against a real sample |
| `binary_analysed` | `doida.exe` (legacy 32-bit client build) |
| `confidence`      | CONFIRMED facts are corroborated by **both** the sample value distribution and the loader/indexer/consumer read order, with no conflicts. Lower-confidence facts are tagged inline. |

---

## Overview

A `.mud` file is a **per-cell ambient-sound zone grid**. Despite the extension name, it does NOT
contain terrain height, water depth, mud, blocking, or flow data — it is purely an audio zone map.

Each grid tile carries a small set of byte indices that select, for the area the local player is
standing on:

- the **background-music (BGM) zone**,
- two layers of **background-environment / ambient (BGE) sounds**,
- up to three **3D positional sound effects (EFF)**, and
- (PLAUSIBLE, see below) the **walk / run footstep sound zones**.

At runtime the sound manager samples this grid every frame from the local player's world `(X, Z)`
position and starts/stops/crossfades audio as the player crosses tile boundaries. If the grid is
absent (file missing), the lookup falls back to a static default tile.

Each tile byte is a **direct 0-based row index** into a matching per-area sound table; the leaf
audio file is resolved from there. See `sound_tables.md` for the table format and the full
resolution chain, summarised below. This is one of the three files that share a terrain cell's
base path; see `terrain.md` for the cell streaming model.

---

## Identification

- **Extension:** `.mud`
- **Found in:** `.pak` / VFS, logical path pattern
  `data/map<area>/dat/d<area>x<mapX>z<mapZ>.mud` — loaded alongside the cell's `.gad` (no-op stub)
  and `.map` (text scene descriptor), which share the same base path with a different extension.
- **Magic / signature:** **none.** Headerless raw blob — no file-level magic, no version field,
  no length field, no compression, no encryption. — confidence: CONFIRMED
- **Endianness:** little-endian. Only relevant to bytes 0/1 if they prove to be multi-byte; the
  audio consumer reads all meaningful fields as individual bytes. — confidence: CONFIRMED
- **File size:** fixed **32768 bytes (0x8000)** for every `.mud` — the loader allocates exactly
  0x8000 bytes and reads exactly 0x8000 bytes in a single read. — confidence: CONFIRMED

---

## Grid geometry

- **Grid dimensions:** 64 × 64 tiles. — confidence: CONFIRMED
- **Tile (record) stride:** 8 bytes. — confidence: CONFIRMED
- **Size reconciliation:** 64 × 64 × 8 = 32768 bytes = the fixed file size. — confidence: CONFIRMED
- **Tile world size:** one terrain cell spans 1024 × 1024 world units; the grid divides it into
  64 × 64 tiles of **16 world units each** (1024 / 64 = 16). — confidence: CONFIRMED

### Indexing (world → tile)

The grid is addressed from world coordinates, NOT from any header. Given a world position
`(worldX, worldZ)` and the owning cell's south-west origin `(cellOriginX, cellOriginZ)`:

```
local_x     = worldX - cellOriginX        // cellOrigin derived from (mapX-10000)*1024, etc.
local_z     = worldZ - cellOriginZ
col         = (local_x / 16) & 0x3F       // 0..63
row         = (local_z / 16) & 0x3F       // 0..63
tile_index  = col + (row << 6)            // row stride = 64 tiles
tile_offset = tile_index * 8              // byte offset into the blob
```

The `& 0x3F` masks clamp both axes to the 0..63 range; the `<< 6` is the 64-tile row stride; the
`* 8` is the per-tile byte stride. — confidence: CONFIRMED

The cell-origin biasing convention `(mapX - 10000) * 1024` is shared with the terrain system —
see `terrain.md` for the full cell coordinate model.

---

## Tile layout (8 bytes)

Each tile is a fixed 8-byte record. Per-byte usage was recovered from the sound-manager
consumer's byte reads and cross-checked against the sample's value distribution.

| Offset | Size | Type | Field          | Notes / meaning                                                   | Confidence |
|-------:|-----:|------|----------------|------------------------------------------------------------------|------------|
| 0      | 1    | u8   | wlkZoneId?     | PLAUSIBLY walk-footstep zone index → `.wlk` table. Was previously labelled `reserved0`. Not read by the analysed ambient-update path; observed 0 in available samples. See hypothesis below. | PLAUSIBLE (was MED/unused) |
| 1      | 1    | u8   | runZoneId?     | PLAUSIBLY run-footstep zone index → `.run` table. Was previously labelled `reserved1`. Not read by the analysed ambient-update path; observed 0 in available samples. See hypothesis below. | PLAUSIBLE (was MED/unused) |
| 2      | 1    | u8   | bgmZoneId      | Background-music zone index → `.bgm` table (per-zone music)       | CONFIRMED |
| 3      | 1    | u8   | bgeAmbientId0  | Background-environment ambient index, layer 0 → `.bge` table      | CONFIRMED |
| 4      | 1    | u8   | bgeAmbientId1  | Background-environment ambient index, layer 1 → `.bge` table      | CONFIRMED |
| 5      | 1    | u8   | effId0         | 3D positional sound-effect index, slot 0 → `.eff` table           | CONFIRMED |
| 6      | 1    | u8   | effId1         | 3D positional sound-effect index, slot 1 → `.eff` table           | CONFIRMED |
| 7      | 1    | u8   | effId2         | 3D positional sound-effect index, slot 2 → `.eff` table           | CONFIRMED |

- **Record count source:** fixed by geometry — always 64 × 64 = 4096 tiles. Not stored in the
  file; derived from the constant grid dimensions. — confidence: CONFIRMED
- **Record stride:** 8 bytes. — confidence: CONFIRMED
- **Sentinel value:** index value `0` in any audio field means "no sound / silence" for that slot;
  the consumer stops the previously-playing entry when a tile's value transitions to 0. The same
  0 = null-row rule applies to bytes 0/1 under the walk/run hypothesis. — confidence: CONFIRMED
  for bytes 2–7; PLAUSIBLE for bytes 0/1.

### Bytes 0 and 1 — walk/run footstep hypothesis (PLAUSIBLE — needs verify)

The VFS census proves exactly **five** per-area sound-table types exist: `.bgm`, `.bge`, `.eff`,
`.wlk`, `.run` (footstep walk / run). The `.bgm`, `.bge` and `.eff` index families map cleanly to
tile bytes 2, 3–4, and 5–7 respectively, accounting for six of the eight tile bytes. That leaves
exactly **two** unaccounted-for tile bytes (0 and 1) and exactly **two** unaccounted-for table
families (`.wlk` and `.run`). The natural one-to-one pairing is:

- **byte 0 → `.wlk` (walk footstep) zone index**,
- **byte 1 → `.run` (run footstep) zone index**,

each used as a direct 0-based row index into the area's `soundtable<AAA>.wlk` / `.run` table, with
0 = null/silence. This is **PLAUSIBLE only** — bytes 0/1 are not read by the ambient-update path
that was analysed, and all sampled `.wlk` / `.run` tables are entirely null, so neither the index
source nor the leaf audio could be exercised. **Verify with a one-line harness/debugger check:
breakpoint the footstep-sound trigger and confirm it indexes a `.wlk` / `.run` table by mud byte
0 / 1.** Until then do not treat bytes 0/1 as confirmed footstep indices, and do not treat them as
hard "reserved" either. See `sound_tables.md` (`.wlk` / `.run` index-source known unknown).

---

## Enumerations / flags

No bitflag fields. All meaningful bytes are small integer indices into the per-area sound tables
(`.bgm` / `.bge` / `.eff`, and PLAUSIBLY `.wlk` / `.run`), each entry of which is selected by the
byte's value used directly as a 0-based row index, with `0` reserved as the silence sentinel. The
on-disk format of those tables and the full leaf-audio resolution chain are documented in
`sound_tables.md`.

---

## Resolution chain (mud tile byte → sound table → leaf audio)

Each tile byte is used as a **direct 0-based row index** into the matching per-area sound table for
the cell's area (`<AAA>` = zero-padded 3-digit area number from the `.mud` path, e.g. `map001`).
The table record's `sound_entry_id` (u32 LE at record offset +0x00) names the leaf `.ogg`.

```
byte 2 (bgmZoneId)     → soundtable<AAA>.bgm[N] → sound_id → data/sound/2d/{sound_id}.ogg
byte 3,4 (bgeAmbientId)→ soundtable<AAA>.bge[N] → sound_id → data/sound/2d/{sound_id}.ogg
byte 5,6,7 (effId)     → soundtable<AAA>.eff[N] → sound_id → data/sound/3d/{sound_id}.ogg
                          + 3D position (record +0x20 X / +0x24 Y / +0x28 Z) and radius (+0x2C)
byte 0 (wlkZoneId?)    → soundtable<AAA>.wlk[N] → footstep walk sound   [PLAUSIBLE — unverified]
byte 1 (runZoneId?)    → soundtable<AAA>.run[N] → footstep run sound    [PLAUSIBLE — unverified]
```

A mud byte of 0 selects record index 0 (the null sentinel) and plays nothing. The 0-based direct
index rule is SAMPLE-VERIFIED for BGM / BGE / EFF across a full area-001 census and globally
corroborated (cross-map census: max bgmZoneId = 10, max bgeAmbientId = 20, max effId = 154 — all
well under the 256 records per table). Full table layout and chain detail: `sound_tables.md`.

---

## Runtime usage (informative — behaviour, not file layout)

1. `.mud` is loaded together with the cell's `.gad` and `.map` when a terrain cell is streamed in.
2. The 32 KiB blob is retained as the terrain world's per-cell audio grid.
3. Each frame the sound manager takes the local player's world `(X, Z)`, computes the current
   tile via the indexing above, and compares the tile's indices against what is currently playing:
   - **BGM (byte 2):** selects the music zone via the `.bgm` table. An indoor override substitutes
     a fixed indoor music id when the player is flagged as indoors.
   - **BGE (bytes 3, 4):** two ambient/environment sound layers via the `.bge` table, started and
     stopped as the player crosses tiles.
   - **EFF (bytes 5, 6, 7):** up to three 3D positional sound effects via the `.eff` table.
   - **Walk / run (bytes 0, 1 — PLAUSIBLE):** footstep sounds via the `.wlk` / `.run` tables,
     keyed on the tile surface zone (hypothesis; not in the analysed ambient path).
   - A periodic forced re-pick (long-interval timer) re-evaluates the current tile even without
     movement.

Crossfade/stop timing and the indoor override id are behavioural details that belong to a
sound-subsystem spec, not to this format doc.

---

## Known unknowns

- **Acronym / origin of "mud":** the content is audio-zone data; the extension name's meaning is
  not proven from the binary. Do not over-read the name.
- **Bytes 0 and 1 — walk/run hypothesis:** PLAUSIBLY the `.wlk` / `.run` footstep zone indices
  (byte 0 → walk, byte 1 → run), based on the exact 2-byte / 2-table-family pairing. Not read by
  the analysed ambient path and not exercised by samples (all `.wlk` / `.run` tables are null).
  Needs a one-line harness/debugger re-verify (breakpoint the footstep trigger, confirm it indexes
  `.wlk` / `.run` by mud byte 0 / 1).
- **Exact crossfade / stop semantics and the indoor override id:** observed but behavioural; out
  of scope for this format spec.

---

## Cross-references

- Cell streaming model and `(mapX, mapZ)` origin biasing: `terrain.md`
- On-disk sound tables and the full mud-byte → table → leaf-audio resolution chain:
  `sound_tables.md` (CONFIRMED for BGM / BGE / EFF; `.wlk` / `.run` index source PLAUSIBLE)
- Companion per-cell files sharing the base path: `.gad` (no-op stub), `.map` (text scene)
- Glossary: see Docs/RE/names.yaml
- Provenance: see Docs/RE/journal.md (entry paired with this spec)

---

## Names flagged for names.yaml (orchestrator to record)

- Format: `mud` → "Ambient Sound Zone Grid" / `MudSoundGrid`
- Struct: `MudTile` (8 bytes): `wlkZoneId?` (byte 0, PLAUSIBLE), `runZoneId?` (byte 1, PLAUSIBLE),
  `bgmZoneId`, `bgeAmbientId0`, `bgeAmbientId1`, `effId0`, `effId1`, `effId2`
- Constants: `MUD_GRID_DIM = 64`, `MUD_TILE_STRIDE = 8`, `MUD_FILE_SIZE = 32768`,
  `MUD_TILE_WORLD_SIZE = 16`
