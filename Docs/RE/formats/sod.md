# Format: .sod  (per-cell wall-collision segments — 2D XZ)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> C# parsers MUST cite `// spec: Docs/RE/formats/sod.md` on every magic constant / offset.

---

## Status

```
verification:   sample-verified   # container shape (u32 solidCount, 108-byte SolidRecord array in one
                                   #   pass, then per-solid u32 quadCount + 48-byte QuadRecord array),
                                   #   the file-size formula, the world-XZ AABBs, and the line
                                   #   slope/intercept/axisFlag fields all matched against a real VFS
                                   #   sample AND the legacy cell-loader read path
ida_reverified: 2026-06-21; CYCLE 12 Block B (2026-06-22, IDB SHA 263bd994): stale "ray-parity"
                language in any prose that describes this file is SUPERSEDED — the collision method
                is segment-intersection, as documented since the task-hint correction below.
ida_anchor:     263bd994
evidence:       [static-ida, vfs-sample]
conflicts:      none on the on-disk layout. Two task-hint corrections folded in: (1) the geometry is a
                line/SEGMENT intersection, not classic point-in-polygon ray-parity; (2) there is NO
                `.sod.pre` / `.pre` sidecar — the acceleration structure is built in memory at load.
loader_resolved: true             # the on-disk read-set (solidCount, SolidRecord array, per-solid
                                   #   quadCount + quad array) and every runtime field consumer
                                   #   (line intersector reads +32/+36/+40/+44) are pinned to the loader.
```

> **Task-hint corrections (CYCLE, anchor 263bd994).** Two prior assumptions are corrected here:
> 1. **It IS 2D-XZ wall geometry, but the test is a line/segment intersection, not ray-parity
>    point-in-polygon.** Each record stores a single wall **segment** as a slope-intercept line
>    `z = m·x + b` (with a vertical-axis special case), and the runtime finds the nearest segment a
>    movement segment crosses. There is no closed polygon loop and no even/odd crossing count.
> 2. **There is NO `.sod.pre` (or any `.pre`) sidecar.** No `.pre` / `.sod.pre` path or string exists
>    in the binary. The `.sod` is opened directly from the cell `.map` descriptor parse, and the
>    pre-computed acceleration structure (a per-solid quadtree over a 16×16 grid) is built **at load
>    time in memory**, not read from disk.

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

## QuadRecord — stride 48 (0x30) — a single wall SEGMENT in XZ

Each "quad" record is one **wall segment** in the world XZ plane: a 2D AABB bounding the segment, the
two segment endpoints, and the segment's line in slope-intercept form plus a vertical/axis flag.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | f32 | `aabbMinX` | Segment 2D-AABB minimum X. | parser + sample |
| +0x04 | 4 | f32 | `aabbMinZ` | Segment 2D-AABB minimum Z. | parser + sample |
| +0x08 | 4 | f32 | `aabbMaxX` | Segment 2D-AABB maximum X. | parser + sample |
| +0x0C | 4 | f32 | `aabbMaxZ` | Segment 2D-AABB maximum Z. | parser + sample |
| +0x10 | 4 | f32 | `p0x` | Endpoint 0, X. | parser (impl) + sample |
| +0x14 | 4 | f32 | `p0z` | Endpoint 0, Z. | sample |
| +0x18 | 4 | f32 | `p1x` | Endpoint 1, X. | sample |
| +0x1C | 4 | f32 | `p1z` | Endpoint 1, Z. | sample |
| +0x20 (+32) | 4 | f32 | `slope` (m) | Line slope in `z = m·x + b`. | parser + sample |
| +0x24 (+36) | 4 | f32 | `xConst` | Constant X used when the line is parallel to the Z axis (i.e. when `slope == 0` / the vertical-axis case). | parser + sample |
| +0x28 (+40) | 4 | f32 | `intercept` (b) | Line intercept in `z = m·x + b`. | parser + sample |
| +0x2C (+44) | 4 | u32 | `axisFlag` | `== 1` marks the vertical / axis-aligned special case (line parallel to an axis); otherwise the slope/intercept form is used. Only value 0 seen in the available sample. | parser + sample |

**Endpoints + line are both meaningful on disk.** The two-endpoint pair at `+0x10..+0x1F` bounds the
segment (the same two XZ points as the AABB corners, but in segment order); the runtime intersector
reads the **line form** (`slope`, `xConst`, `intercept`, `axisFlag`) for the actual math, then clamps
the intersection point onto both segments using the AABBs. So `+0x20..+0x2F` are real, consumed fields,
not padding.

**Line math verified on a sample segment:** for one segment, `m = (zMax − zMin) / (xMax − xMin)`
matches the stored slope, and `b = zMin − m·xMin` matches the stored intercept (`z = m·x + b`). Other
sampled segments include a near-vertical wall (large-magnitude slope) and an ordinary diagonal — all
consistent with the slope-intercept reading and the `axisFlag` special case for axis-aligned walls.

> **Endpoint order.** Whether `+0x10` is "p0" vs "p1" is cosmetic: the intersector uses the
> slope/intercept line form for the geometry, with the endpoints and AABB only bounding the segment.
> Treat the endpoints as `p0 / p1` segment-bounding values; their precise ordering is unverified and
> does not affect the math.

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
   - **Build the per-solid quadtree:** partition this solid's segments into the 16×16 grid; where a
     grid cell accumulates many refs (split threshold = 16) it splits into a sub-quadtree (four child
     quadrants, each child carrying its own AABB and center). This is pure in-memory acceleration —
     **nothing here is written to disk.**
6. **Mark ready** (`true` iff at least one solid was loaded) and return.

There is no magic, no version field, and no checksum to validate; the heap allocations guard against
`count × stride` overflow.

---

## Runtime collision query (informative — behaviour, not file layout)

At movement time the move/slide resolver sweeps a movement segment against the cell's collision data:

1. The query entry sets the nearest-hit distance to `+∞` and dispatches the sweep AABB into the 16×16
   grid.
2. The grid query maps the query AABB onto its grid cells and visits each cell's bucket (deduplicated
   per query via a global frame counter, so a segment shared across cells is tested once).
3. Where a grid cell holds a sub-quadtree, the query recurses by node-AABB center toward the relevant
   quadrant; at a leaf it iterates the leaf's solid refs and, per solid, its `quadCount` segments.
4. For each candidate segment it does: 2D-AABB cull → slope-intercept **line/line intersection**
   between the movement segment and the wall segment → two point-in-AABB containment checks to clamp
   the intersection onto both segments. It keeps the nearest hit (squared distance to the query origin)
   and returns the hit XZ plus which segment was struck.

This is the wall-collision used to block / slide the player along walls in XZ. Ground height is solved
separately (`.ted` bilinear — see `terrain.md`).

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
- **It references — nothing external.** `.sod` is self-contained geometry; the AABBs and endpoints are
  **absolute world XZ** coordinates in the same space as the cell `.map` geometry and player movement.
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
2. **Endpoint order (`+0x10` = p0 vs p1).** Cosmetic — the intersector uses the slope/intercept line
   form, with endpoints + AABB only bounding the segment. Marked as `p0 / p1` segment-bounding values,
   order unverified.
3. **`axisFlag` (+0x2C) value range.** The `== 1` branch (vertical / axis-aligned line case) is
   confirmed, but a full enumeration of its values across many cells is unverified — only `0` is seen
   in the available sample.

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
  `SodQuadRecord` (48 bytes): `aabbMinX/Z`, `aabbMaxX/Z`, `p0x/p0z`, `p1x/p1z`, `slope`, `xConst`,
  `intercept`, `axisFlag`
- Constants: `SOD_SOLID_STRIDE = 108`, `SOD_QUAD_STRIDE = 48`, `SOD_GRID_DIM = 16`,
  `SOD_LEAF_SPLIT_THRESHOLD = 16`
