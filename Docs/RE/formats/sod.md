# Format: .sod  (per-cell wall-collision segments — 2D XZ)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> C# parsers MUST cite `// spec: Docs/RE/formats/sod.md` on every magic constant / offset.

---

## Status

```
verification:   partial           # container shape (u32 solidCount, 108-byte SolidRecord array in one
                                   #   pass, then per-solid u32 quadCount + 48-byte QuadRecord array)
                                   #   and the file-size formula confirmed. World-XZ AABBs of the
                                   #   SolidRecord confirmed against a real VFS sample.
                                   #   QuadRecord on-disk layout: the first 16 bytes (four XZ corner
                                   #   floats) are confirmed; the trailing 32 bytes are read opaquely
                                   #   and are NOT field-consumed by the loader. Earlier claims about
                                   #   slope/intercept/axisFlag fields at +0x20–+0x2C and the runtime
                                   #   line/segment intersector reading those offsets are REFUTED by
                                   #   the loader trace (see QuadRecord section below).
ida_reverified: 2026-06-22 (CYCLE 12 Block B, IDB SHA 263bd994): prior "segment/slope-intercept"
                QuadRecord reading REFUTED by loader trace. The loader reads the quad block as an
                opaque 48-byte record; only the first 16 bytes (4 XZ corner floats) are consumed.
                Runtime collision query not yet located — marked DEBUGGER-PENDING.
ida_anchor:     263bd994
evidence:       [static-ida, vfs-sample] (on-disk container + SolidRecord AABB confirmed;
                QuadRecord trailing fields loader-disproven; runtime query DEBUGGER-PENDING)
conflicts:      Prior "wall SEGMENT / slope-intercept z=m·x+b / axisFlag" QuadRecord reading REFUTED
                (loader-disproven). Prior "runtime move/slide line/line intersection" section REFUTED
                (no static runtime query was located; see DEBUGGER-PENDING note).
                Residual confirmed: (1) no `.sod.pre` / `.pre` sidecar; (2) no ray-parity polygon.
loader_resolved: partial          # on-disk read-set (solidCount, SolidRecord array, per-solid
                                   #   quadCount + quad block) is confirmed. Runtime field consumers
                                   #   of the QuadRecord content are NOT pinned — DEBUGGER-PENDING.
```

> **Correction record (anchor 263bd994).**
> 1. **Ray-parity point-in-polygon is REFUTED** — the `.sod` does not use even/odd ray-crossing
>    against closed polygon loops. (The geometry is 2D-XZ wall data, not a polygon hull.)
> 2. **There is NO `.sod.pre` (or any `.pre`) sidecar** — no `.pre` / `.sod.pre` path or string was
>    located in the binary. The `.sod` is opened directly from the cell `.map` descriptor parse.
> 3. **LOADER-DISPROVEN (QuadRecord):** The earlier claim that the QuadRecord stores a wall segment as
>    a slope-intercept line `z = m·x + b` with an `axisFlag` at +0x2C, and that the runtime
>    intersector field-reads offsets +0x20/+0x24/+0x28/+0x2C, is REFUTED by the loader trace. The
>    loader reads the 48-byte quad block as one opaque unit; only the first 16 bytes (four XZ-corner
>    floats) are consumed by the on-disk parse. The remaining 32 bytes and the runtime collision query
>    are DEBUGGER-PENDING — no static runtime query was located.

---

## Overview

A `.sod` file is a **per-terrain-cell wall-collision data set**: a small array of *solids*, each
carrying an array of *wall segments* in the world XZ plane. It is the data behind the client's
horizontal wall blocking / sliding — the thing that stops the player from walking through a wall and
slides them along it. Ground **height** is a separate concern (`.ted` per-triangle plane interpolation — see
`terrain.md §5.4a`); `.sod` deals only with vertical-wall collision in the XZ plane.

The on-disk content is intentionally minimal: a count, a flat array of solid bounding boxes, and per
solid a flat array of segment records. All spatial values are **absolute world XZ coordinates** in the
same space as the cell `.map` geometry and player movement. At load the runtime allocates and zeroes
in-memory copies, then builds a quadtree acceleration structure per solid; that quadtree is **runtime
only** and never appears on disk.

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

Sample corroboration (a 1-solid, 3-segment cell): `4 + 108·1 + (4 + 48·3) = 4 + 108 + 148 = 260` bytes. ✓

There is **no record count for the quad blocks** beyond each per-solid `quadCount`; there is no header
beyond `solidCount`. A short file would mis-read — the only implicit validation is the file being large
enough for the declared counts. `solidCount` and each `quadCount` are trusted directly.

**Read order note.** The `108 × solidCount` solid array is read as one contiguous block **before** the
loop that reads the per-solid quad blocks. For `solidCount == 1` the single solid record and its quad
block are simply adjacent. For `N > 1` the `N` quad blocks follow sequentially **after** the full
`N`-solid array — confirmed by the loader's read sequence; the multi-solid on-disk ordering is not yet
cross-checked against a multi-solid sample (only a 1-solid sample is on disk — see Known unknowns).

---

## SolidRecord — stride 108 (0x6C)

A solid is a bounding box plus (after load) a pointer to its segment array. Only the world-XZ AABB,
the `quadCount`, and the (ignored) quad-array slot are meaningful on disk; the rest of the record is
zero on disk and is populated at load time with runtime pointers and centers.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | f32 | `aabbMinX` | World-XZ AABB minimum X. | parser + sample |
| +0x04 | 4 | f32 | `aabbMinZ` | World-XZ AABB minimum Z (Z is the **second** axis, not Y). | parser + sample |
| +0x08 | 4 | f32 | `aabbMaxX` | World-XZ AABB maximum X. | parser + sample |
| +0x0C | 4 | f32 | `aabbMaxZ` | World-XZ AABB maximum Z. | parser + sample |
| +0x10 | 44 | — | (zero on disk) | Runtime-only: node-grid pointer, child links, and node center are filled in during load. Ignore on read. | parser + sample |
| +0x3C (+60) | 4 | u32 | `quadCount` | Number of `QuadRecord` segments for this solid. **Also re-read from the stream** as the per-solid `u32 quadCount` that precedes the quad array — the on-disk value here is redundant with that stream word. | parser + sample |
| +0x40 (+64) | 4 | u32 | `quadArrayPtr` | On-disk garbage (a stale pointer value). **Overwritten at load** with the heap pointer to the allocated segment array. Ignore on read. | parser + sample |
| +0x44 (+68) | 40 | — | (zero on disk) | Runtime use. Ignore on read. | sample |

**AABB axis ordering confirmed.** For cell `d019x10003z10011` the cell world extent is
`X = (10003−10000)·1024 = 3072 .. 4096`, `Z = (10011−10000)·1024 = 11264 .. 12288`; the sample solid's
AABB sits inside that box, which confirms the AABB is **world XZ** in `(minX, minZ, maxX, maxZ)` order
and that the **second axis is Z, not Y**.

> **Disk vs. runtime AABB.** At query time the runtime reads a SolidRecord AABB from a *runtime-
> initialized* region (populated from the cell bounds during node-grid init), distinct from the on-disk
> AABB at +0. For an **on-disk** spec only the +0 AABB, the `quadCount` at +60, and the (ignored)
> quad-array slot at +64 matter. The `+0x10..` middle and `+0x44..` tail are zero on disk.

---

## QuadRecord — stride 48 (0x30) — a wall-geometry entry in XZ

> **LOADER-DISPROVEN CORRECTION.** The earlier description of this record as a "wall SEGMENT stored
> as slope-intercept `z = m·x + b` with axisFlag" is **REFUTED** by the loader trace (anchor
> 263bd994). The loader reads the 48-byte block as one opaque unit: only the first 16 bytes (four
> XZ-corner floats bounding the entry's footprint) are consumed by the on-disk parse. The trailing
> 32 bytes are read into memory but are **not field-accessed** by any statically-located consumer.
> The runtime collision query that uses these records was **not statically located** and is
> **DEBUGGER-PENDING**. Do not assert field meanings for offsets +0x10 and above.

Each QuadRecord is one **wall-geometry entry** in the world XZ plane. The on-disk parse confirms four
XZ corner floats at the start of each record; everything else is opaque trailing data whose runtime
interpretation has not been traced.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | f32 | `cornerMinX` | Footprint XZ minimum X. | parser + sample |
| +0x04 | 4 | f32 | `cornerMinZ` | Footprint XZ minimum Z. | parser + sample |
| +0x08 | 4 | f32 | `cornerMaxX` | Footprint XZ maximum X. | parser + sample |
| +0x0C | 4 | f32 | `cornerMaxZ` | Footprint XZ maximum Z. | parser + sample |
| +0x10 | 32 | — | (opaque trailing data) | Read into the in-memory record but **not field-consumed** by any statically-located runtime path. Prior names (`p0x`/`p0z`/`p1x`/`p1z`/`slope`/`xConst`/`intercept`/`axisFlag`) are REFUTED — do not use them. Semantics are DEBUGGER-PENDING. | REFUTED / DEBUGGER-PENDING |

> **What was disproven.** The earlier field table named offsets +0x10–+0x2C as endpoint pairs and a
> slope-intercept line (`slope`, `xConst`, `intercept`, `axisFlag`), and asserted the runtime
> intersector consumed those four offsets. The loader trace does not support this: no static field
> access at +0x10 or above was located. An engineer must **not** write a parser that reads named
> fields beyond +0x0C until the runtime consumer is confirmed via the live debugger.

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
   - Read `48 × quadCount` bytes directly into the segment array.
   - **Build the per-solid grid:** partition this solid's quad records into the 16×16 spatial grid by
     their corner XZ footprints; where a grid cell accumulates many refs (split threshold = 16) it
     splits into a sub-tree (four child quadrants, each with its own AABB and center). This is pure
     in-memory acceleration — **nothing here is written to disk.** How the grid entries are queried
     at movement time is DEBUGGER-PENDING (see Runtime collision query section).
6. **Mark ready** (`true` iff at least one solid was loaded) and return.

There is no magic, no version field, and no checksum to validate; the heap allocations guard against
`count × stride` overflow.

---

## Runtime collision query — DEBUGGER-PENDING

> **No static runtime query was located.** The earlier description of a "move/slide resolver that
> sweeps a movement segment using slope-intercept line/line intersection" was derived from the prior
> (now-refuted) QuadRecord field table. Because the trailing 32 bytes of each QuadRecord are opaque
> and no runtime consumer was statically traced, the actual collision algorithm is **unknown** and
> must be confirmed via the live debugger.

What is confirmed from the static loader trace:
- The 16×16 grid is built at load time over the cell bounds (acceleration structure, in-memory only).
- Each solid's quad block is loaded into the in-memory record array.
- The in-memory record stores the four XZ corner floats per entry and 32 opaque trailing bytes.

What is NOT confirmed:
- How the runtime tests a movement vector against a quad record.
- Whether the test uses the corner floats, the opaque bytes, or both.
- Whether it is a segment intersection, a point-in-polygon test, a signed-distance test, or another algorithm.
- What "blocking" and "sliding" look like at the algorithm level.

A debugger breakpoint on the collision-query entry (when the player walks into a wall) is required to
settle this. Mark as **DEBUGGER-PENDING**; do not implement a collision algorithm from this spec until confirmed.

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
- **It references — nothing external.** `.sod` is self-contained geometry; the corner XZ values are
  **absolute world XZ** coordinates in the same space as the cell `.map` geometry and player movement.
- **Vertical bounds context (`.up` / `.exd`).** The companion terrain formats supply the vertical
  extents that bracket wall collision: `.up` (UP_TERRAIN) carries the **minimum / floor** surface
  triangles (see `terrain_layers.md` §2), and `.exd` (EXTRA_TERRAIN) carries the **maximum / ceiling**
  surface triangles (see `terrain_layers.md` §3). Wall collision in `.sod` operates in the XZ plane;
  the `.up`/`.exd` triangle meshes supply the Y bounds that determine the active vertical range for
  that wall geometry.
- **Runtime consumer / manager — the embedded per-cell collision manager.** The move/slide resolver
  sweeps a movement segment through the manager's quadtree query (see Runtime collision query) to get
  the nearest wall crossing and resolve blocking/sliding.

---

## Coordinate convention (port note)

Geometry is in **absolute world XZ**, native-client space. The Godot world-render path **negates Z**
(`(x, y, z) → (x, y, −z)` — see `terrain.md` and the coordinate conventions in CLAUDE.md). A faithful
port must solve `.sod` collision in the **same** convention it renders terrain in (apply the identical
Z negation consistently), or walls will not line up with the rendered world.

---

## Known unknowns

1. **Multi-solid file ordering (`solidCount > 1`).** The loader read sequence is explicit that the full
   `108 × solidCount` solid array is read before the loop that reads each `quadCount` + quad block, i.e.
   all-solids-then-all-quad-blocks. Only a 1-solid sample is on disk to confirm this; a multi-solid
   sample (or a debugger trace on a cell with multiple solids) would settle the exact interleaving.
2. **QuadRecord trailing bytes (+0x10–+0x2F) — DEBUGGER-PENDING.** The 32 bytes after the four XZ
   corner floats are read into the in-memory record but no static field consumer was located. Their
   meaning (possible endpoint pair, line parameters, flags, or other encoding) is unknown. The prior
   slope-intercept / axisFlag reading is REFUTED; do not use it. Confirm via debugger.
3. **Runtime collision query algorithm — DEBUGGER-PENDING.** No static movement/slide resolver that
   consumes the quad records was located. Whether the test is segment-intersection, point-in-AABB,
   or another algorithm is unknown. A live debugger breakpoint on the collision path is required.

---

## Cross-references

| Format | File | Relationship |
|--------|------|--------------|
| `terrain.md` | `data/map<area>/dat/*.ted` etc. | Cell coordinate model, cell origin biasing, and the separate ground-**height** source (bilinear) |
| `mud.md` | `data/map<area>/dat/*.mud` | Companion per-cell file sharing the same base path (ambient-sound grid) |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.sod` files |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`

---

## Names flagged for names.yaml (orchestrator to record)

- Format: `sod` → "Cell Wall-Collision Segments (2D XZ)"
- Structs: `SodSolidRecord` (108 bytes): `aabbMinX/Z`, `aabbMaxX/Z`, `quadCount`;
  `SodQuadRecord` (48 bytes): `cornerMinX/Z`, `cornerMaxX/Z` (confirmed); trailing 32 bytes opaque
  (DEBUGGER-PENDING — prior names `p0x/p0z/p1x/p1z/slope/xConst/intercept/axisFlag` are REFUTED)
- Constants: `SOD_SOLID_STRIDE = 108`, `SOD_QUAD_STRIDE = 48`, `SOD_GRID_DIM = 16`,
  `SOD_LEAF_SPLIT_THRESHOLD = 16`
