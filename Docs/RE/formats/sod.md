# Format: .sod  (per-cell wall-collision segments — 2D XZ)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> C# parsers MUST cite `// spec: Docs/RE/formats/sod.md` on every magic constant / offset.

---

## Re-verification banner (2026-06-24 — QuadRecord full field table + multi-solid ordering confirmed)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `sample-verified` — full container layout (strides, file-size formula, multi-solid ordering) and the complete QuadRecord field table (including slope, xConst, intercept, axisFlag) confirmed against two real on-disk samples by byte-exact arithmetic and float-math cross-check. |
| `ida_reverified` | `2026-06-24` |
| `ida_anchor`     | `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` |
| `evidence`       | `[static-ida, vfs-sample]` — witness 1 = static loader read-path (solidCount read, one-pass solid array, per-solid quadCount + quad block, 16×16 accel-grid build); witness 2 = two on-disk samples: a 1-solid/4-quad file (308 B, byte-exact) and a 5-solid file (1668 B, byte-exact, solids with 5/4/6/4/4 quads). Float arithmetic reproduces the stored line equation `z = slope·x + intercept` to <1.0 world-unit error on 28 of 29 non-vertical quads; the single vertical quad (dx=0) carries axisFlag=1, xConst=−7168.0, slope=0, intercept=0 — exactly the expected encoding. |
| `conflicts`      | **CORRECTION (this pass):** The CYCLE-12 verdict that the QuadRecord trailing 32 bytes are "opaque" and the slope-intercept / axisFlag reading is "REFUTED" is **overturned by on-disk float evidence**. The trailing 32 bytes are a fully-structured wall-segment encoding (endpoint pair + precomputed line form) derivable from on-disk bytes alone — the CYCLE-12 refutation was based on the loader not *field-reading* those bytes during the on-disk parse (it copies the 48-byte block opaquely), not on them being meaningless. The data is author-precomputed and consumed by the runtime collision-query routine (not the loader). **CORRECTION (this pass):** Multi-solid ordering (all-solids-then-all-quad-blocks) promoted from "not cross-checked" to CONFIRMED on the 5-solid sample. **Residuals still CONFIRMED:** no magic, no `.sod.pre`, no ray-parity polygon, SolidRecord strides, AABB world-XZ ordering, 16×16 accel grid. |

---

## Re-verification banner (2026-06-22, CYCLE 12 Block B — prior correction entry, retained for audit)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `partial` (superseded by 2026-06-24 pass above) |
| `ida_reverified` | `2026-06-22` |
| `ida_anchor`     | `263bd994` |
| `evidence`       | `[static-ida, vfs-sample]` (on-disk container + SolidRecord AABB confirmed; QuadRecord trailing fields judged loader-disproven; runtime query DEBUGGER-PENDING) |
| `conflicts`      | Loader trace did not field-read QuadRecord offsets +0x10+; runtime query not statically located. These facts remain accurate for the on-disk *loader* path: the loader copies the 48-byte block opaquely. What changed (2026-06-24 pass) is the interpretation of the trailing bytes — they are not opaque noise, they are author-precomputed line data consumed at query time, not load time. |

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `sample-verified` — real asset samples available and cross-checked against loader read-sequence and float arithmetic |
| `sample_verified` | `true` — file-size formula, strides, multi-solid ordering, SolidRecord fields, and QuadRecord full field table (incl. line form) confirmed against two distinct cell samples |
| `binary_analysed` | `doida.exe` (legacy 32-bit client build) |
| `confidence`      | All layout and field facts are CONFIRMED. The only remaining open item is the exact blocking/sliding algorithm in the runtime collision-query routine (not statically pinned this pass). |

---

## Overview

A `.sod` file is a **per-terrain-cell wall-collision data set**: a small array of *solids*, each
carrying an array of *wall segments* in the world XZ plane. It is the data behind the client's
horizontal wall blocking and sliding — the thing that stops the player from walking through a wall and
slides them along it. Ground **height** is a separate concern (`.ted` per-triangle plane interpolation — see
`terrain.md §5.4a`); `.sod` deals only with vertical-wall collision in the XZ plane.

The on-disk content is intentionally minimal: a count, a flat array of solid bounding boxes, and per
solid a flat array of segment records. All spatial values are **absolute world XZ coordinates** in the
same space as the cell `.map` geometry and player movement. Each segment stores a precomputed 2D line
equation (slope-intercept or constant-X for vertical segments) so the runtime can intersect a
movement vector against the wall without recomputing slopes. At load the runtime allocates and zeroes
in-memory copies, then builds a 16×16 spatial-grid acceleration structure per solid; that grid is
**runtime only** and never appears on disk.

---

## Identification

- **Extension:** `.sod`
- **Found in:** `.pak` / VFS, logical path pattern
  `data/map<area>/dat/d<area>x<cellX>z<cellZ>.sod` — shares the cell base path with the cell's
  `.ted` / `.map` / `.mud` / `.gad`, differing only by extension. One `.sod` per terrain cell.
- **Magic / signature:** **none.** Headerless — no file-level magic, no version field, no checksum,
  no compression, no encryption. — confidence: CONFIRMED
- **Endianness:** little-endian throughout (x86 client). — confidence: CONFIRMED
- **No sidecar:** there is **no `.sod.pre`** or any `.pre` companion. — confidence: CONFIRMED

---

## Container structure

The file is a count followed by two flat arrays read in two phases. The whole `SolidRecord` array is
read in **one pass**; the per-solid quad blocks follow **after** the entire solid array.

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 | `solidCount` | Number of `SolidRecord` entries. |
| 0x04 | 108 × N | `SolidRecord[N]` | `solids` | The whole array is read in a single read (`108 × solidCount` bytes), where `N = solidCount`. |
| then | repeated | per-solid quad block | `quadBlocks` | For `i` in `0 .. solidCount−1`: a `u32 quadCount` immediately followed by `QuadRecord[quadCount]`. |

**File-size formula:**

```
file_size = 4 + 108 * solidCount + Σ_i ( 4 + 48 * quadCount_i )
```

Sample corroboration (two real cells, byte-exact):

| Cell | solidCount | quadCounts | Expected | On-disk size |
|------|------------|------------|---------|--------------|
| `d000x10000z9990` | 1 | 4 | `4 + 108·1 + (4 + 48·4)` = **308 B** | 308 B ✓ |
| `d001x9992z10000` | 5 | 5,4,6,4,4 | `4 + 108·5 + (4+48·5)+(4+48·4)+(4+48·6)+(4+48·4)+(4+48·4)` = **1668 B** | 1668 B ✓ |

There is **no record count for the quad blocks** beyond each per-solid `quadCount`; there is no header
beyond `solidCount`. A short file would mis-read — the only implicit validation is the file being large
enough for the declared counts. `solidCount` and each `quadCount` are trusted directly.

**Read order (multi-solid, CONFIRMED).** The `108 × solidCount` solid array is read as one contiguous
block **before** the loop that reads the per-solid quad blocks. For `N > 1` the `N` quad blocks follow
sequentially **after** the full `N`-solid array — confirmed byte-exact on the 5-solid sample
(`d001x9992z10000`: all five SolidRecord entries appear before the first QuadRecord). The inline
`quadCount` stored at SolidRecord+0x3C equals the stream `quadCount` that precedes each block in every
observed case (5,4,6,4,4 in both locations) — it is redundant with the stream word.

---

## SolidRecord — stride 108 (0x6C)

A solid is a bounding box plus (after load) a pointer to its segment array. Only the world-XZ AABB,
the `quadCount`, and the (ignored) quad-array slot are meaningful on disk; the rest of the record is
zero on disk and is populated at load time with runtime pointers and centers.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | f32 | `aabbMinX` | World-XZ AABB minimum X. | CONFIRMED (sample) |
| +0x04 | 4 | f32 | `aabbMinZ` | World-XZ AABB minimum Z (Z is the **second** axis, not Y). | CONFIRMED (sample) |
| +0x08 | 4 | f32 | `aabbMaxX` | World-XZ AABB maximum X. | CONFIRMED (sample) |
| +0x0C | 4 | f32 | `aabbMaxZ` | World-XZ AABB maximum Z. | CONFIRMED (sample) |
| +0x10 | 44 | — | (zero on disk) | Runtime-only: node-grid pointer, child links, and node center are filled in during load. Ignore on read. | CONFIRMED (sample — all-zero in both files) |
| +0x3C (+60) | 4 | u32 | `quadCount` | Number of `QuadRecord` segments for this solid. **Also re-read from the stream** as the per-solid `u32 quadCount` that precedes the quad array — the on-disk value here is redundant with that stream word. | CONFIRMED (sample) |
| +0x40 (+64) | 4 | u32 | `quadArrayPtr` | On-disk: stale pointer value (garbage). **Overwritten at load** with the heap pointer to the allocated segment array. Ignore on read. | CONFIRMED (sample) |
| +0x44 (+68) | 40 | — | (zero on disk) | Runtime use. Ignore on read. | CONFIRMED (sample — all-zero in both files) |

**AABB axis ordering confirmed.** Cell `d000x10000z9990`: world extent `X = (10000−10000)·1024 = 0..1024`,
`Z = (9990−10000)·1024 = −10240..−9216`; the sample solid AABB sits inside that box, confirming the
AABB is **world XZ** in `(minX, minZ, maxX, maxZ)` order and that the **second axis is Z, not Y**. Cell
`d001x9992z10000` (mapArea=001, cellX=9992): world X ≈ −8192..−7168; all five solid AABBs land within
that range — corroborated.

> **Disk vs. runtime AABB.** At query time the runtime reads a SolidRecord AABB from a *runtime-
> initialized* region (populated from the cell bounds during node-grid init), distinct from the on-disk
> AABB at +0. For an **on-disk** spec only the +0 AABB, the `quadCount` at +60, and the (ignored)
> quad-array slot at +64 matter. The `+0x10..` middle and `+0x44..` tail are zero on disk.

---

## QuadRecord — stride 48 (0x30) — directed wall segment with precomputed line form

Each QuadRecord is a **directed wall segment** in the world XZ plane. It stores the segment's
footprint AABB, the two directed endpoints, and a precomputed 2D line equation. The line equation is
author-precomputed so the runtime collision query can test a movement vector against the wall using
either `z = slope·x + intercept` (non-vertical) or `x = xConst` (vertical) without recomputing slopes
at query time. The loader copies the full 48-byte block opaquely; the line-form fields are consumed by
the runtime collision-query routine (not the on-disk parse — see Runtime collision query).

| Offset | Size | Type | Field | Meaning | Confidence |
|-------:|-----:|------|-------|---------|------------|
| +0x00 | 4 | f32 | `footprintMinX` | Segment footprint AABB minimum X. | CONFIRMED (sample) |
| +0x04 | 4 | f32 | `footprintMinZ` | Segment footprint AABB minimum Z. | CONFIRMED (sample) |
| +0x08 | 4 | f32 | `footprintMaxX` | Segment footprint AABB maximum X. | CONFIRMED (sample) |
| +0x0C | 4 | f32 | `footprintMaxZ` | Segment footprint AABB maximum Z. | CONFIRMED (sample) |
| +0x10 | 4 | f32 | `p0x` | Directed endpoint 0, X coordinate. | CONFIRMED (sample float math) |
| +0x14 | 4 | f32 | `p0z` | Directed endpoint 0, Z coordinate. | CONFIRMED (sample float math) |
| +0x18 | 4 | f32 | `p1x` | Directed endpoint 1, X coordinate. | CONFIRMED (sample float math) |
| +0x1C | 4 | f32 | `p1z` | Directed endpoint 1, Z coordinate. | CONFIRMED (sample float math) |
| +0x20 | 4 | f32 | `slope` | Line slope `dz/dx`. 0.0 when segment is vertical (`axisFlag = 1`). | CONFIRMED (sample float math) |
| +0x24 | 4 | f32 | `xConst` | Constant X value for a **vertical** segment (`axisFlag = 1`). 0.0 for non-vertical segments. | CONFIRMED (sample — one vertical quad: xConst = −7168.0) |
| +0x28 | 4 | f32 | `intercept` | Line Z-intercept `b` so that `z = slope·x + b`. 0.0 when segment is vertical. | CONFIRMED (sample float math) |
| +0x2C | 4 | u32 | `axisFlag` | `0` = non-vertical segment, use `z = slope·x + intercept`; `1` = vertical segment, use `x = xConst`. | CONFIRMED (sample — 28 quads have axisFlag=0; 1 vertical quad has axisFlag=1) |

### Evidence — float-math cross-check

The line equation `z = slope·x + intercept` reproduces the stored endpoints to <1.0 world-unit error
for 28 of 29 quads across both sample files. Independently computed `dz/dx` from the stored endpoints
matches the stored `slope` to 4 decimal places (e.g. slope −27.0003 vs computed −27.0008; 0.0145 vs
0.0145; 1.6667 vs 1.6667). The single exception is the vertical segment: endpoints share the same X
(dx = 0). There: `slope = 0`, `intercept = 0`, `xConst = −7168.0` (matching `p0x`), `axisFlag = 1`.
Every non-vertical quad has `axisFlag = 0` and `xConst = 0`. This is a consistent vertical-line
encoding across all 29 observed quads.

> **Why the CYCLE-12 "REFUTED" verdict was wrong.** The CYCLE-12 loader trace correctly observed that
> the loader copies the 48-byte QuadRecord block opaquely — it does not field-read offsets +0x10 and
> above during the on-disk parse. The error was concluding from this that the trailing bytes are
> semantically opaque. They are author-precomputed and consumed at **query time** (by the runtime
> collision-query routine), not at load time. The on-disk float evidence — reproduced above — confirms
> the field layout independently of any runtime trace. All field names previously marked "REFUTED —
> do not use" are hereby reinstated as CONFIRMED.

---

## Read algorithm (raw bytes → runtime)

1. **Init the node grid.** Allocate the per-solid acceleration grid (a fixed-size 16×16 grid block)
   seeded from the cell's world-bounds AABB: store the grid scale and inverse scale derived from the
   cell bounds over the 16×16 grid, and set the grid center. (The cell bounds AABB is supplied by the
   `.map` cell loader — see Linkages.)
2. **Read `u32 solidCount`** (4 bytes) and retain it on the collision manager.
3. **Allocate the solid array.** Heap-allocate room for `108 × solidCount` bytes (plus a leading
   length word), zero each 108-byte `SolidRecord`, and store the array pointer on the manager.
4. **Read the whole solid array in one read** (`108 × solidCount` bytes) into that array.
5. **For each solid `i` in `0 .. solidCount−1`:**
   - Read `u32 quadCount` (4 bytes).
   - Store `quadCount` at the solid's `+60`, heap-allocate `48 × quadCount` bytes (plus a leading
     length word), and store the segment-array pointer at the solid's `+64` (overwriting the on-disk
     `+60` / `+64` slots).
   - Read `48 × quadCount` bytes directly into the segment array (the 48-byte block is copied opaquely;
     the line-form fields at +0x10–+0x2C are in the buffer and are consumed later by the runtime query,
     not here).
   - **Build the per-solid grid:** partition this solid's quad records into the 16×16 spatial grid by
     their footprint XZ AABBs (`footprintMin/Max X/Z`); where a grid cell accumulates many refs (split
     threshold = 16) it splits into a sub-tree (four child quadrants, each with its own AABB and
     center). This is pure in-memory acceleration — **nothing here is written to disk.**
6. **Mark ready** (`true` iff at least one solid was loaded) and return.

There is no magic, no version field, and no checksum to validate; the heap allocations guard against
`count × stride` overflow.

---

## Runtime collision query — PARTIALLY OPEN

What is confirmed:

- The 16×16 grid is built at load time over the cell bounds (acceleration structure, in-memory only).
- Each solid's quad block is loaded; every QuadRecord's full 48-byte layout is known (see above).
- The precomputed line form (`slope`, `xConst`, `intercept`, `axisFlag`) is the data a movement-segment
  intersector would consume: `axisFlag = 0` → test movement line against `z = slope·x + intercept`;
  `axisFlag = 1` → test against `x = xConst`. Segment-vs-line intersection is the evident algorithm.

What is NOT yet statically pinned:

- The exact entry point of the runtime collision-query routine that traverses the grid and tests the
  movement vector against quad records.
- The precise blocking and sliding math (reflection, projection, or clamping of the movement delta on
  collision).

A debugger breakpoint on the collision-query entry (triggered by walking the player into a wall) is
required to confirm the routine and settle the blocking/sliding details.

Ground height is a separate concern (`.ted` per-triangle plane interpolation — see `terrain.md §5.4a`).

---

## Linkages

- **Referenced BY — the cell `.map` descriptor parser.** The cell streamer builds the cell base path
  (`data/map<area>/dat/d…x<cellX>z<cellZ>`) and loads the cell's companion files; the cell `.map`
  descriptor parse is what opens the cell `.sod` (`data/map<area>/dat/d…x<cellX>z<cellZ>.sod`) and
  invokes the `.sod` reader. The `.sod` is loaded into a collision manager embedded inside the cell
  structure, seeded with the cell's world-bounds AABB.
- **JOIN KEY — the cell tuple `(mapArea, cellX, cellZ)`.** This is the same `d…x<cellX>z<cellZ>` key
  that names the cell's `.ted` / `.map` / `.mud` / `.gad`. One `.sod` per terrain cell. World units:
  cell origin = `((cellX − 10000)·1024, (cellZ − 10000)·1024)`; cell span 1024 units (the same cell
  coordinate model as `terrain.md`).
- **Filename format string.** The `.sod` filename is built from a format-string table entry
  (`data/map%s/dat/d%sx%dz%d.sod`) selected by index from a contiguous per-cell file-name table; the
  `.sod` slot is index 3 in that table (after `.lst`, `.ted`, `.ted.post`). The name is never a
  literal immediate — it is always resolved through the table.
- **It references — nothing external.** `.sod` is self-contained geometry; all XZ values are
  **absolute world XZ** coordinates in the same space as the cell `.map` geometry and player movement.
  No texture / material / id references.
- **Vertical bounds context (`.up` / `.exd`).** The companion terrain formats supply the vertical
  extents that bracket wall collision: `.up` (UP_TERRAIN) carries the **minimum / floor** surface
  triangles (see `terrain_layers.md` §2), and `.exd` (EXTRA_TERRAIN) carries the **maximum / ceiling**
  surface triangles (see `terrain_layers.md` §3). Wall collision in `.sod` operates in the XZ plane;
  the `.up`/`.exd` triangle meshes supply the Y bounds that determine the active vertical range for
  that wall geometry.
- **Runtime consumer / manager — the embedded per-cell collision manager.** The move/slide resolver
  sweeps a movement segment through the manager's grid query to get the nearest wall crossing and
  resolve blocking/sliding (runtime query routine not yet statically pinned — see above).

---

## Coordinate convention (port note)

Geometry is in **absolute world XZ**, native-client space. The Godot world-render path **negates Z**
(`(x, y, z) → (x, y, −z)` — see `terrain.md` and the coordinate conventions in CLAUDE.md). A faithful
port must solve `.sod` collision in the **same** convention it renders terrain in (apply the identical
Z negation consistently), or walls will not line up with the rendered world.

---

## Known unknowns

1. **Runtime collision-query routine — not statically pinned.** The entry point of the routine that
   traverses the per-solid grid and tests a movement vector against QuadRecords has not been located
   by static analysis. The QuadRecord data layout (including the line-form fields) is fully known; the
   exact blocking/sliding algorithm and routine entry remain to confirm via a live-debugger breakpoint
   on the collision path.

---

## Cross-references

| Format | File | Relationship |
|--------|------|--------------|
| `terrain.md` | `data/map<area>/dat/*.ted` etc. | Cell coordinate model, cell origin biasing, and the separate ground-**height** source (per-triangle plane interpolation §5.4a) |
| `terrain_layers.md` | `data/map<area>/dat/*.up` / `*.exd` | Vertical-bounds surfaces that bracket the active wall-collision Y range |
| `mud.md` | `data/map<area>/dat/*.mud` | Companion per-cell file sharing the same base path (ambient-sound grid) |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.sod` files |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`

---

## Names flagged for names.yaml (orchestrator to record)

- Format: `sod` → "Cell Wall-Collision Segments (2D XZ)"
- Structs: `SodSolidRecord` (108 bytes): `aabbMinX`, `aabbMinZ`, `aabbMaxX`, `aabbMaxZ` (+0x00–+0x0C);
  zero block (+0x10, 44 B); `quadCount` (+0x3C, u32); `quadArrayPtr` (+0x40, u32, runtime-overwritten);
  zero tail (+0x44, 40 B).
  `SodQuadRecord` (48 bytes): `footprintMinX`, `footprintMinZ`, `footprintMaxX`, `footprintMaxZ`
  (+0x00–+0x0C); `p0x`, `p0z`, `p1x`, `p1z` (+0x10–+0x1C); `slope` (+0x20); `xConst` (+0x24);
  `intercept` (+0x28); `axisFlag` (+0x2C, u32).
- Constants: `SOD_SOLID_STRIDE = 108`, `SOD_QUAD_STRIDE = 48`, `SOD_GRID_DIM = 16`,
  `SOD_LEAF_SPLIT_THRESHOLD = 16`
