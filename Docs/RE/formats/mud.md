# Format: .mud  (per-cell ambient-sound zone grid)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> C# parsers MUST cite `// spec: Docs/RE/formats/mud.md` on every magic constant / offset.

---

## Re-verification banner (2026-06-27, Cycle 14 re-anchor)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `sample-verified` — re-verified loader, indexer, and update loop logic statically against the live database (`f61f66a9`). Verified timed evaluation (10 min timer) and movement threshold gating. |
| `ida_reverified` | `2026-06-27` |
| `ida_anchor`     | `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` |
| `evidence`       | `[static-ida, vfs-sample]` — loader `Mud_ReadBlob`, indexer `Mud_TileAt`, and timer loop `SoundMgr_UpdateAmbientFromMudTile` verified. |
| `conflicts`      | None. All structural and indexing claims confirmed. |

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
see `terrain.md`

## Read algorithm

The file loader allocates memory on demand and handles missing grids gracefully.

1. **Path Construction:** Constructed via `sprintf(buf, "%s.mud")` in the streaming call.
2. **File Invocation:** Invoked via `Mud_LoadGrid` during cell streaming. The manager frees any previously allocated grid buffer before attempting to load.
3. **Low-Level Read (`Mud_ReadBlob`):**
   - Instantiates a local stack-based `DiskFile` object.
   - If the file is **absent** (`DiskFile::IsValid()` returns false), the function cleans up, sets the return value `al` to `1` (`true` status), and returns. The grid pointer remains **null**. (Soft fallback prevents crashes and allows cell loading to proceed).
   - If the file exists:
     - Allocates exactly **32,768 bytes (0x8000)** via `operator new`.
     - Performs a single bulk read `DiskFile::ReadVirtual` of `32,768` bytes into the buffer.
     - On read error (hard failure), the buffer is deleted, the pointer is nulled, and the function returns `false` (`0`), which aborts cell loading.

---

## Tile layout (8 bytes)

Each tile is a fixed 8-byte record:

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 1 | `u8` | `unread0` | Ignored by consumer. |
| `+0x01` | 1 | `u8` | `unread1` | Ignored by consumer. |
| `+0x02` | 1 | `u8` | `bgmZoneId` | BGM zone index mapping to the per-area `.bgm` table. |
| `+0x03` | 1 | `u8` | `bgeAmbientId0` | Ambient sound layer 0 mapping to the `.bge` table. |
| `+0x04` | 1 | `u8` | `bgeAmbientId1` | Ambient sound layer 1 mapping to the `.bge` table. |
| `+0x05` | 1 | `u8` | `effId0` | 3D positional effect slot 0 mapping to the `.eff` table. |
| `+0x06` | 1 | `u8` | `effId1` | 3D positional effect slot 1 mapping to the `.eff` table. |
| `+0x07` | 1 | `u8` | `effId2` | 3D positional effect slot 2 mapping to the `.eff` table. |

- **Sentinel Value:** `0` means silence. Transitions to `0` stop active playbacks.

---

## Runtime usage (behavioral)

### A. Coordinate Indexing (`Mud_TileAt` & `Mud_TileAtLocal`)
To sample the grid, absolute player coordinates `(worldX, worldZ)` are biased by subtracting the cell's SW origins `(originX, originZ)`:
- $\text{localX} = \text{worldX} - \text{originX}$
- $\text{localZ} = \text{worldZ} - \text{originZ}$
- $\text{col} = \lfloor \frac{\text{localX}}{16} \rfloor \ \& \ 0\text{x}3\text{F}$
- $\text{row} = \lfloor \frac{\text{localZ}}{16} \rfloor \ \& \ 0\text{x}3\text{F}$
- $\text{tile\_offset} = (\text{col} + (\text{row} \times 64)) \times 8$

### B. Missing File Fallback
If the grid pointer is **null** (due to missing `.mud` file), `Mud_TileAt` returns a pointer to a statically-allocated default tile in the BSS region of the binary. The OS loader zero-fills BSS, so the default tile is all-zero bytes, resulting in total silence for all slots.

### C. Double-Gated Update Loop (`SoundMgr_UpdateAmbientFromMudTile`)
The update loop evaluates player coordinates every frame but is double-gated to save cycles:
- **Timer Gate:** Checks if `GetTickCount() - last_eval_time` exceeds `600,000 ms` (10 minutes). If true, updates the timer and forces a full coordinates sampling (allowing time-of-day changes to apply even if player is still).
- **Movement Gate:** If the timer has not expired, a coordinates check runs only if the player has moved more than `2.0` world units since the last evaluation (computed via `Vector_DistanceCompare`). Otherwise, the loop exits immediately.

Crossfade/stop timing and the indoor override id are behavioural details that belong to a
sound-subsystem spec, not to this format doc.

---

## Known unknowns

- **Acronym / origin of "mud":** the content is audio-zone data; the extension name's meaning is
  not proven from the binary. Do not over-read the name.
- **Bytes 0 and 1:** not read by the located consumer; their meaning is unknown. The former
  walk/run footstep reading is REFUTED — do not reintroduce it. A faithful port treats these two
  bytes as opaque/ignored.
- **Exact crossfade / stop semantics and the player-state override cue id:** observed but behavioural; out
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
- **Corpus re-confirm** (2026-06-24, anchor 263bd994): full-corpus census (1578 `.mud` files, all
  exactly 0x8000 B) plus re-walk of the static loader/indexer/consumer read-path. All claims
  re-confirmed with no drift. Per-byte value distribution (6 463 488 tiles) independently
  corroborates the byte-map: bytes 0–1 universally `0x00`, bytes 2–7 carry small-index values
  consistent with the BGM/BGE/EFF role. BGM override label refined from "indoor override" to
  "player-state override" to better reflect the binary evidence (keyed on a player-state flag).

---

## Names flagged for names.yaml (orchestrator to record)

- Format: `mud` → "Ambient Sound Zone Grid" / `MudSoundGrid`
- Struct: `MudTile` (8 bytes): `unread0` (byte 0, unconsumed), `unread1` (byte 1, unconsumed),
  `bgmZoneId`, `bgeAmbientId0`, `bgeAmbientId1`, `effId0`, `effId1`, `effId2`
- Constants: `MUD_GRID_DIM = 64`, `MUD_TILE_STRIDE = 8`, `MUD_FILE_SIZE = 32768`,
  `MUD_TILE_WORLD_SIZE = 16`
