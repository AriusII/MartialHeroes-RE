# Format: .mud  (per-cell ambient-sound zone grid)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> C# parsers MUST cite `// spec: Docs/RE/formats/mud.md` on every magic constant / offset.

---

## Re-verification banner (2026-06-21, refinement pass)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `sample-verified` — fixed 0x8000 size, grid geometry, world→tile indexer, and the byte-2/3-4/5-7 BGM/BGE/EFF consumer reads are all matched against a real VFS sample **and** the legacy loader/indexer/consumer read-path (two-witness). RE-CONFIRMED in full on this build with no drift. |
| `ida_reverified` | `2026-06-21` |
| `ida_anchor`     | `263bd994` |
| `evidence`       | `[static-ida, vfs-sample]` — loader (raw 0x8000 single read), tile indexer, and the sole ambient-sound consumer (witness 1) + a real VFS cell sample (witness 2) |
| `conflicts`      | None. Re-confirmed on this build: headerless fixed 32 768 B; 64 × 64 tiles, 8-byte stride, 16-unit tiles; the sole consumer reads **only bytes 2–7** (byte 2 = BGM, bytes 3–4 = BGE, bytes 5–7 = EFF, byte 7 `effId2` confirmed consumed); **bytes 0 and 1 are never read** (and were observed single-valued `0x00` across all 4096 tiles of the sample cell) — the walk/run footstep hypothesis stays REFUTED. Two enrichments sharpened (not conflicts): the missing-file (null-blob) fallback tile is specifically a fixed **all-zero** tile ⇒ total silence in every slot (§Runtime usage); and a **missing `.mud` is a soft outcome**, not a load failure — the loader reports success with a null blob, so the cell's `.gad`/`.map` still load (§Read algorithm / §Runtime usage). |

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
- two layers of **background-environment / ambient (BGE) sounds**, and
- up to three **3D positional sound effects (EFF)**.

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
- **Endianness:** little-endian. The audio consumer reads all meaningful fields as individual
  bytes. — confidence: CONFIRMED
- **File size:** fixed **32768 bytes (0x8000)** for every `.mud` — the loader allocates exactly
  0x8000 bytes and reads exactly 0x8000 bytes in a single read, with **no load-time parse** (raw
  fixed blob; the grid is interpreted lazily by the audio consumer). VFS census (2026-06-16,
  two-witness): **all 1 578 `.mud` files are exactly 0x8000 bytes.** — confidence: CONFIRMED

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

## Read algorithm

There is **no parse step**. The file is read as one fixed-size raw blob; the grid is interpreted
lazily by the audio consumer, tile-by-tile, on demand. The on-disk bytes ARE the runtime grid —
no header, no transform, no decode, no validation of tile contents.

1. Open the cell's `.mud` over the VFS by its logical path (the loader builds the cell base path,
   then appends the `.mud` extension).
2. **If the file is absent:** treat as a **soft / non-fatal** outcome — the loader reports success,
   the per-cell grid pointer stays **null**, and the cell's companion `.gad`/`.map` continue to
   load normally. (A missing `.mud` is NOT a load failure.) The later tile lookups then resolve to
   the static default tile (see §Runtime usage). — confidence: CONFIRMED
3. Allocate exactly **32768 bytes (0x8000)**. (Allocation failure is a hard error — the grid
   pointer is left null and the load returns failure.) — confidence: CONFIRMED
4. Read exactly **32768 bytes** in a **single** read into that buffer. (A read error on an opened
   file is a hard error — the buffer is freed, the pointer nulled, and the load returns failure.)
   — confidence: CONFIRMED
5. Retain the buffer verbatim as the cell's per-cell audio grid. No byte-level parse, no decode, no
   tile-content validation occurs at load time. — confidence: CONFIRMED

This sharp split — only a *missing* file is soft, while an alloc failure or a read error on an
*opened* file is hard — is what lets a cell with no ambient-sound grid stream in silently while a
truncated/corrupt grid still surfaces as a load error.

---

## Tile layout (8 bytes)

Each tile is a fixed 8-byte record. Per-byte usage was recovered from the sound-manager
consumer's byte reads and cross-checked against the sample's value distribution. The sole consumer
that walks this grid reads **only bytes 2–7**; bytes 0 and 1 are never read by it (see below).

| Offset | Size | Type | Field          | Notes / meaning                                                   | Confidence |
|-------:|-----:|------|----------------|------------------------------------------------------------------|------------|
| 0      | 1    | u8   | unread0        | Not read by the located consumer. Meaning unknown; treat as opaque/ignored. Was previously hypothesised as a walk-footstep zone index — that reading is REFUTED (see below). Observed 0 in available samples. | REFUTED-hypothesis / unconsumed |
| 1      | 1    | u8   | unread1        | Not read by the located consumer. Meaning unknown; treat as opaque/ignored. Was previously hypothesised as a run-footstep zone index — that reading is REFUTED (see below). Observed 0 in available samples. | REFUTED-hypothesis / unconsumed |
| 2      | 1    | u8   | bgmZoneId      | Background-music zone index → `.bgm` table (per-zone music)       | CONFIRMED |
| 3      | 1    | u8   | bgeAmbientId0  | Background-environment ambient index, layer 0 → `.bge` table      | CONFIRMED |
| 4      | 1    | u8   | bgeAmbientId1  | Background-environment ambient index, layer 1 → `.bge` table      | CONFIRMED |
| 5      | 1    | u8   | effId0         | 3D positional sound-effect index, slot 0 → `.eff` table           | CONFIRMED |
| 6      | 1    | u8   | effId1         | 3D positional sound-effect index, slot 1 → `.eff` table           | CONFIRMED |
| 7      | 1    | u8   | effId2         | 3D positional sound-effect index, slot 2 → `.eff` table. Confirmed consumed: read and used as a direct index into the per-area EFF sound table by the effect-sound-table loader. | CONFIRMED |

- **Record count source:** fixed by geometry — always 64 × 64 = 4096 tiles. Not stored in the
  file; derived from the constant grid dimensions. — confidence: CONFIRMED
- **Record stride:** 8 bytes. — confidence: CONFIRMED
- **Sentinel value:** index value `0` in any audio field means "no sound / silence" for that slot;
  the consumer stops the previously-playing entry when a tile's value transitions to 0. This rule
  applies to bytes 2–7. — confidence: CONFIRMED

### Bytes 0 and 1 — walk/run footstep hypothesis REFUTED

An earlier hypothesis paired bytes 0 and 1 with the per-area `.wlk` (walk) and `.run` (run)
footstep sound tables, on the arithmetic that exactly two tile bytes and exactly two unmapped
sound-table families remained. **That hypothesis is REFUTED.** The sole consumer that walks this
grid reads only bytes 2 through 7; bytes 0 and 1 are never read by it. There is therefore no
evidence that these two bytes carry walk/run footstep zone indices, and this spec makes no such
claim. Both bytes are left as **unconsumed by the located consumer** — meaning unknown, to be
treated as opaque/ignored by a faithful port. Footstep audio, if it exists, is sourced elsewhere
and is not driven by these `.mud` tile bytes.

---

## Enumerations / flags

No bitflag fields. All consumed bytes are small integer indices into the per-area sound tables
(`.bgm` / `.bge` / `.eff`), each entry of which is selected by the byte's value used directly as a
0-based row index, with `0` reserved as the silence sentinel. The on-disk format of those tables
and the full leaf-audio resolution chain are documented in `sound_tables.md`.

---

## Resolution chain (mud tile byte → sound table → leaf audio)

Each consumed tile byte is used as a **direct 0-based row index** into the matching per-area sound
table for the cell's area (`<AAA>` = zero-padded 3-digit area number from the `.mud` path, e.g.
`map001`). The table record's `sound_entry_id` (u32 LE at record offset +0x00) names the leaf
`.ogg`.

```
byte 2 (bgmZoneId)     → soundtable<AAA>.bgm[N] → sound_id → data/sound/2d/{sound_id}.ogg
byte 3,4 (bgeAmbientId)→ soundtable<AAA>.bge[N] → sound_id → data/sound/2d/{sound_id}.ogg
byte 5,6,7 (effId)     → soundtable<AAA>.eff[N] → sound_id → data/sound/3d/{sound_id}.ogg
                          + 3D position (record +0x20 X / +0x24 Y / +0x28 Z) and radius (+0x2C)
byte 0, byte 1         → NOT read by the located consumer (no resolution chain)
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
   - **EFF (bytes 5, 6, 7):** up to three 3D positional sound effects via the `.eff` table;
     byte 7 (`effId2`) is confirmed read and used as an EFF-table index by the effect-sound-table
     loader.
   - **Bytes 0, 1:** not read by this consumer — no audio behaviour is driven by them.
   - A periodic forced re-pick re-evaluates the current tile even without movement. The interval is
     **600 000 ms (exactly 10 minutes)** — the consumer compares the elapsed time since the last
     re-pick against this constant.
4. **Missing `.mud` (null blob):** a missing file is a soft outcome (see §Read algorithm) — the
   grid pointer stays null and the cell streams in regardless. When the grid pointer is null, the
   indexer returns a single fixed **static default tile** rather than reading the grid. That default
   tile is specifically an **all-zero** tile, so every audio slot resolves to the `0` silence
   sentinel: a cell with no `.mud` plays **no BGM, no BGE, and no EFF** at all. — confidence:
   CONFIRMED

Crossfade/stop timing and the indoor override id are behavioural details that belong to a
sound-subsystem spec, not to this format doc.

---

## Known unknowns

- **Acronym / origin of "mud":** the content is audio-zone data; the extension name's meaning is
  not proven from the binary. Do not over-read the name.
- **Bytes 0 and 1:** not read by the located consumer; their meaning is unknown. The former
  walk/run footstep reading is REFUTED — do not reintroduce it. A faithful port treats these two
  bytes as opaque/ignored.
- **Exact crossfade / stop semantics and the indoor override id:** observed but behavioural; out
  of scope for this format spec.

---

## Cross-references

- Cell streaming model and `(mapX, mapZ)` origin biasing: `terrain.md`
- On-disk sound tables and the full mud-byte → table → leaf-audio resolution chain:
  `sound_tables.md` (CONFIRMED for BGM / BGE / EFF). NOTE for the sound_tables author: each of the
  three per-area tables (BGM/BGE/EFF) shares a fixed 48-byte (0x30) record stride, and the EFF
  record's Y coordinate is taken from the **player** position at lookup time (the record itself
  supplies X at +0x20, Z at +0x28, and a radius at +0x2C) — confirm/reconcile there, out of scope
  for this format doc.
- Companion per-cell files sharing the same cell base path: `.gad` (no-op stub) and `.map` (text
  scene descriptor) are loaded in the same streaming call; the wider base-path family also includes
  `.ted` / `.ted.post` (terrain height + post-process), `.sod` (collision), and `.lst` (cell list).
- Glossary: see Docs/RE/names.yaml
- Provenance: see Docs/RE/journal.md (entry paired with this spec)

---

## Provenance

- **CAMPAIGN VFS-MASTERY** two-witness gate (loader read-order witness + black-box sample witness):
  - `effId2` (byte 7) CONFIRMED consumed — read and indexed into the per-area EFF sound table by
    the effect-sound-table loader.
  - Bytes 0–1 walk/run hypothesis REFUTED — the sole consumer reads only bytes 2–7; bytes 0 and 1
    are never read.
- **CAMPAIGN VFS-MASTERY-B** reconcile (2026-06-16): full-corpus census confirms the fixed 0x8000
  size on all 1 578 `.mud` files and the no-load-time-parse / lazy-consumer model
  (byte 2 = BGM, bytes 3–4 = BGE, bytes 5–7 = EFF with byte 7 = `effId2`; bytes 0–1 never read).
- **CAMPAIGN 10 / Block D** re-verification (2026-06-16, anchor 263bd994): two-witness re-confirm
  (loader raw 0x8000 single read + tile indexer + sole ambient-sound consumer, against a real VFS
  sample). No drift. Two minor enrichments added: the forced re-pick interval is exactly 600 000 ms
  (10 min), and the missing-`.mud` (null-blob) lookup falls back to a single fixed static default
  tile (both noted under §Runtime usage).
- **Refinement pass** (2026-06-21, anchor 263bd994): two-witness re-confirm (loader / indexer /
  sole consumer + a real VFS cell sample). All load-bearing claims re-confirmed with no drift;
  bytes 0 and 1 observed single-valued `0x00` across all 4096 tiles of the sample cell. Two
  enrichments sharpened: (a) the static default tile is specifically an **all-zero** tile, so a
  missing `.mud` yields total silence in every slot; (b) a **missing `.mud` is a soft outcome** —
  the loader reports success with a null blob and the cell's `.gad`/`.map` still load, whereas only
  an allocation failure or a read error on an *opened* file is a hard load failure. Added a
  consolidated §Read algorithm. (No change to the on-disk layout or geometry.)

---

## Names flagged for names.yaml (orchestrator to record)

- Format: `mud` → "Ambient Sound Zone Grid" / `MudSoundGrid`
- Struct: `MudTile` (8 bytes): `unread0` (byte 0, unconsumed), `unread1` (byte 1, unconsumed),
  `bgmZoneId`, `bgeAmbientId0`, `bgeAmbientId1`, `effId0`, `effId1`, `effId2`
- Constants: `MUD_GRID_DIM = 64`, `MUD_TILE_STRIDE = 8`, `MUD_FILE_SIZE = 32768`,
  `MUD_TILE_WORLD_SIZE = 16`
