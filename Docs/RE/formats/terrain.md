# Format: terrain map cell  (streaming terrain system — `.lst` / `.map` / `.ted` / `.mud` / `.gad` / `.sod`)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

---

## Status block

| Attribute          | Value |
|--------------------|-------|
| `status`           | `hypothesis` — parser-derived; no asset samples available |
| `sample_verified`  | `false` — no `.map`, `.ted`, `.mud`, `.sod`, or `.lst` file was available during analysis |
| `binary_analysed`  | `doida.exe` (legacy 32-bit client build) |
| `confidence`       | Fields listed as CONFIRMED are corroborated by the parser read-sequence. Fields marked UNVERIFIED are inferred from call-site context or IDA comments only. |

---

## Overview

The terrain system organises the game world as a uniform grid of **cells**, each covering exactly
1024 × 1024 world units. Cells are identified by a `(mapX, mapZ)` integer coordinate pair biased
around the world origin at `(10000, 10000)`, so the cell whose south-west corner aligns with
world-space `(0, 0)` has coordinates `mapX = 10000, mapZ = 10000`.

At runtime a ring of cells is kept loaded around the player position, driven by a background
thread. Each cell is described by three files sharing a common base path:

| Extension | Role |
|-----------|------|
| `.mud`    | Fixed-size binary blob; ambient/audio tile grid (internal layout UNVERIFIED) |
| `.gad`    | Purpose UNKNOWN — loader is a no-op stub |
| `.map`    | Plain-text ASCII scene descriptor; references per-cell binary asset blobs |

Binary assets referenced from within `.map` include:

| Extension | Role |
|-----------|------|
| `.ted`    | Terrain geometry blob: heightmap, normals, colour, lookup tables |
| `.sod`    | Collision solid geometry |

A separate per-area cell manifest (`.lst`) and a global background-texture list (`bgtexture.lst`)
are loaded at area-switch time, outside the per-cell path.

---

## 1. Area and cell naming

### 1.1 Area identifier

A map area is identified by an integer `areaId`. Its three decimal digits are separated as:

```
d0 = areaId / 100
d1 = (areaId / 10) % 10
d2 = areaId % 10
```

The directory root for an area is:

```
data/map<d0><d1><d2>/
```

### 1.2 Per-area cell manifest — `.lst`

**Path pattern:** `data/map<d0><d1><d2>/dat/d<d0><d1><d2>.lst`

The manifest is a compact binary file that lists the valid `(mapX, mapZ)` pairs for an area.
A cell coordinate pair that does not appear in this manifest is never loaded, regardless of
proximity to the player.

**Binary layout** (UNVERIFIED — inferred from parser read sequence; no magic or version field
observed):

| Offset | Size     | Type       | Field   | Notes                                    | Confidence |
|-------:|---------:|------------|---------|------------------------------------------|------------|
| 0      | 4        | u32le      | `count` | Number of valid cell entries             | CONFIRMED  |
| 4      | `count × 4` | u32le[] | `keys`  | One cell key per entry; see formula below | CONFIRMED |

Cell key formula:

```
key = mapZ + 100000 * mapX
```

The runtime ingests all keys into a sorted set. A cell load request is rejected if its computed
key is absent from the set.

**Known unknowns:** Whether the file begins with a magic number or version prefix is unverified.
The parser was observed reading a 4-byte count then a contiguous array of 4-byte keys, but any
file-level header preceding that sequence would be transparent to the observed read calls.

### 1.3 Per-cell base path

For cell `(mapX, mapZ)` in area `areaId`:

```
data/map<d0><d1><d2>/dat/d<d0><d1><d2>x<mapX>z<mapZ>
```

The three per-cell files are formed by appending `.mud`, `.gad`, and `.map` to this base path.

### 1.4 Per-cell world-space bounds

The world-space bounding rectangle of a cell is derived from its grid coordinates:

```
worldX_min = (mapX - 10000) × 1024.0
worldZ_min = (mapZ - 10000) × 1024.0
worldX_max = worldX_min + 1024.0
worldZ_max = worldZ_min + 1024.0
```

The subtraction of 10000 is the world-origin bias. One cell therefore covers exactly
**1024 × 1024 world units**. The mapping of "world units" to metres or any other physical scale
is UNVERIFIED.

---

## 2. World-position to cell coordinate mapping

Given a player world position `(worldX, worldZ)`, the corresponding cell coordinates are:

```
If worldX < 0: adjusted_X = worldX - 1024.0   else: adjusted_X = worldX
If worldZ < 0: adjusted_Z = worldZ - 1024.0   else: adjusted_Z = worldZ

cellX_raw = floor(adjusted_X × (1 / 1024.0))
cellZ_raw = floor(adjusted_Z × (1 / 1024.0))

mapX = cellX_raw + 10000
mapZ = cellZ_raw + 10000
```

The multiplier `1 / 1024.0 = 0.0009765625` is CONFIRMED. The negative-axis correction
(subtract 1024 before truncation) ensures floor-division semantics for negative world
coordinates.

---

## 3. The `.map` scene descriptor (text format)

The `.map` file is a **plain ASCII text** file. It is parsed by a line-token reader that
understands the following grammar:

- Lines beginning with `#` are comments and are ignored.
- Section blocks are opened with the section keyword followed by `{` and closed with `}`.
- Within a section, two directives are understood: `DATAFILE` and `TEXTURES`.

### 3.1 Sections

| Section keyword  | Role                                              | DATAFILE target |
|------------------|---------------------------------------------------|-----------------|
| `TERRAIN`        | Primary tile geometry layer                       | `.ted` blob     |
| `EXTRA_TERRAIN`  | Secondary tile geometry layer (e.g. water surface)| `.ted` blob     |
| `UP_TERRAIN`     | Upper or overlay tile geometry layer              | `.ted` blob     |
| `BUILDING`       | Static building and prop objects                  | building mesh   |
| `FX1` … `FX7`   | Special-effect layers (7 named slots)             | format UNVERIFIED |
| `SOLID`          | Collision geometry                                | `.sod` blob     |

All section keywords are CONFIRMED as present in the client string table.

### 3.2 `DATAFILE` directive

```
DATAFILE <path>
```

Specifies the virtual-filesystem path to the binary asset blob for this section. The correct
sub-loader is selected by the section type, not by the file extension.

### 3.3 `TEXTURES` directive

```
TEXTURES {
    <intFlag> <intTexId>
    ...
}
```

Each entry inside the `TEXTURES` block appears to be a pair of integers: a flag and a texture
index. The texture index refers into the background-texture pool loaded from `bgtexture.lst`
(section 4 below). The exact semantics of `intFlag` are UNVERIFIED.

---

## 4. Background texture list — `bgtexture.lst`

**Path:** `data/map000/texture/bgtexture.lst`

This file is loaded once at startup for area 000 and is not reloaded per-cell. It populates
a global pool of background textures that all terrain sections reference by index.

Texture asset path format (CONFIRMED):
```
data/map000/texture/<name>.dds
```

### 4.1 Binary layout

| Offset | Size           | Type      | Field        | Notes                                     | Confidence  |
|-------:|---------------:|-----------|--------------|-------------------------------------------|-------------|
| 0      | 4              | u32le     | `texCount`   | Number of background texture entries; valid range observed: 1 – 1999 (< 2000) | CONFIRMED |
| 4      | `texCount × 1` | u8[]      | `typeBytes`  | One type byte per texture entry; `1` = animated, `≥ 2` = static (UNVERIFIED semantics) | UNVERIFIED |
| 4 + `texCount` | `texCount × 76` | record[] | `texRecords` | One 76-byte (`0x4C`) GHTex record per entry | UNVERIFIED |

**Known unknowns:** The internal layout of each 76-byte GHTex record is not available from
the current analysis. Only the stride (76 bytes) and the type-byte conditional (1 vs. ≥ 2) are
inferred from loader logic.

---

## 5. Terrain geometry blob — `.ted`

The `.ted` blob is the binary payload referenced by `DATAFILE` inside a `TERRAIN`,
`EXTRA_TERRAIN`, or `UP_TERRAIN` section. It contains no file-level header: the five sequential
data blocks begin at offset 0 with no magic number or version prefix (UNVERIFIED — no header
read was observed before the first block read, but the VFS abstraction layer could silently
consume a header).

### 5.1 Grid geometry

| Property       | Value                                  | Confidence |
|----------------|----------------------------------------|------------|
| Vertex grid    | 65 × 65 vertices                       | CONFIRMED  |
| Quad grid      | 64 × 64 quads per cell                 | CONFIRMED  |
| Vertex spacing | 16.0 world units                       | CONFIRMED (derived: 1024 / 64 = 16) |
| Total file size | 46 987 bytes (0xB78B)                 | CONFIRMED (sum of the five block sizes) |

### 5.2 Sequential block layout

The file is read as five contiguous fixed-size blocks with no padding or alignment between them:

| Block | Byte offset | Size (bytes) | Hex size | Content | Type per element | Elements | Confidence |
|------:|------------:|-------------:|----------|---------|-----------------|----------|------------|
| 1     | 0           | 16 900       | 0x4204   | Heightmap | f32le           | 65 × 65 = 4 225 | CONFIRMED |
| 2     | 16 900      | 12 675       | 0x3183   | Vertex normals | u8 × 3 (R, G, B) | 65 × 65 = 4 225 | CONFIRMED |
| 3     | 29 575      | 256          | 0x100    | Lookup table | u8              | 256 entries | CONFIRMED (size); purpose UNVERIFIED |
| 4     | 29 831      | 256          | 0x100    | Direction map | u8              | 256 entries | CONFIRMED (size); purpose UNVERIFIED |
| 5     | 30 087      | 16 900       | 0x4204   | Diffuse colour map | u8 × 4 (R, G, B, A) | 65 × 65 = 4 225 | CONFIRMED |

**Block 1 — Heightmap:** 4 225 IEEE 754 single-precision float values, one per vertex, stored in
row-major order (row 0 = northernmost or X-axis-leading row — axis orientation UNVERIFIED). Height
values appear to be stored in world-space Y units directly with no additional scale factor, but
this is UNVERIFIED (no scale multiply was observed in the loader, but a scale applied elsewhere
in the call chain cannot be ruled out).

**Block 2 — Normals:** 4 225 packed RGB triples, one per vertex. The encoding convention
(e.g. 0–255 mapped to −1…+1, or 128-bias) is UNVERIFIED.

**Block 3 — Lookup table:** 256 single-byte entries. Purpose is UNVERIFIED; may be a palette or
texture-blend index lookup.

**Block 4 — Direction map:** 256 single-byte entries. Purpose is UNVERIFIED; may encode per-quad
surface orientation or material flags.

**Block 5 — Diffuse colour:** 4 225 packed RGBA quads (4 bytes per vertex), one per vertex,
stored in the same row-major order as the heightmap.

### 5.3 Known unknowns

- Whether a file header (magic, version) exists before block 1, handled transparently by the VFS.
- Height scale: is the f32 value in world-space Y directly, or is a multiplier applied?
- Axis orientation of row-major storage (which axis is the "inner" loop).
- Encoding convention for packed normals in block 2.
- Semantic meaning of blocks 3 and 4.

---

## 6. Ambient/audio tile blob — `.mud`

The `.mud` file is a fixed-size opaque binary blob. It is read in a single operation with no
header parsing.

| Offset | Size (bytes) | Hex size | Content | Confidence |
|-------:|-------------:|----------|---------|------------|
| 0      | 32 768       | 0x8000   | Opaque tile data | CONFIRMED (fixed read size) |

**Total file size: exactly 32 768 bytes.**

The internal structure of the buffer is UNVERIFIED. A hypothesis present in the analysis notes
suggests a 64 × 64 grid of 8-byte records (64 × 64 × 8 = 32 768), which would match the 64 × 64
quad grid of a terrain cell, but this was not derived from any observed parse loop and must be
treated as unconfirmed.

---

## 7. Unknown extension — `.gad`

The `.gad` file is loaded as the third per-cell asset, but the loader is a complete stub that
takes no action and returns success unconditionally. The format and purpose of `.gad` are entirely
UNKNOWN. No further analysis is available.

---

## 8. Collision solid blob — `.sod`

The `.sod` blob is the binary payload referenced by `DATAFILE` inside a `SOLID` section. It
contains a variable-length list of solid records followed by per-record triangle lists. No file
header, magic number, or version prefix was observed.

### 8.1 Top-level layout

| Offset | Size (bytes)          | Type      | Field          | Notes                         | Confidence |
|-------:|----------------------:|-----------|----------------|-------------------------------|------------|
| 0      | 4                     | u32le     | `solidCount`   | Number of solid records       | CONFIRMED  |
| 4      | `solidCount × 108`    | record[]  | `SolidRecord[]`| 108-byte records; layout below | CONFIRMED (stride); fields UNVERIFIED |
| After records | variable    | —         | Triangle data  | Per-record: see section 8.3   | CONFIRMED (read sequence) |

### 8.2 SolidRecord — 108 bytes (0x6C)

The full internal field layout of the 108-byte `SolidRecord` is UNVERIFIED. Only the stride
(108 bytes) is confirmed from the allocation pattern. After all `solidCount` records are read,
the records are inserted individually into a spatial quadtree for collision queries.

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0      | 108  | —    | (opaque) | Internal layout unknown | UNVERIFIED |

### 8.3 Per-record triangle data

After the flat `SolidRecord` array, each record is followed by its triangle list:

| Relative offset | Size (bytes)     | Type      | Field       | Notes                         | Confidence |
|----------------:|-----------------:|-----------|-------------|-------------------------------|------------|
| 0               | 4                | u32le     | `triCount`  | Number of triangles for this record | CONFIRMED |
| 4               | `triCount × 48`  | record[]  | triangles   | 48-byte triangle structs; layout UNVERIFIED | CONFIRMED (stride); fields UNVERIFIED |

The internal layout of the 48-byte triangle struct is UNVERIFIED.

---

## 9. Cell streaming policy

### 9.1 Cell pool

The runtime maintains a fixed pool of **34 cell slots**. A cell slot is 24 712 bytes (0x6088).
The pool supports a maximum of 25 simultaneously active cells (5 × 5 ring), with the remaining
9 slots used as overflow or load buffers.

Each cell slot carries an **active flag** at byte offset 0x6084 within the slot:

| Value | Meaning |
|-------|---------|
| `1`   | Cell is loaded and active |
| `0`   | Slot is free or marked evictable |

### 9.2 Quality-dependent stream radii

The streaming ring radius is selected at startup based on a client quality setting:

| Quality level | Stream radius (world units) | Ring shape | Ring-radius cells |
|---------------|-----------------------------|-----------|--------------------|
| High (1)      | 1 800.0                     | 5 × 5     | 5                  |
| Medium (2)    | 1 000.0                     | 3 × 3     | 4                  |
| Low (other)   | 600.0                       | 3 × 3     | 4                  |

The ring shape (5 × 5 vs. 3 × 3) is determined at runtime by comparing the stored stream radius
to 1 000.0: if greater, the 5 × 5 ring dispatcher is used; otherwise the 3 × 3 dispatcher.

### 9.3 Eviction policy

When the player moves to a new centre cell, every loaded cell whose distance from the new centre
exceeds **2 cells** in either the X or Z axis is marked evictable (active flag set to 0). The
eviction threshold is strictly `> 2` (cells exactly 2 away are retained). Reuse of an evictable
slot is handled by the background streaming thread on its next scheduling cycle.

### 9.4 Per-frame update constraint

The per-frame ring-shift logic asserts that the player cannot move more than **one cell** in
either axis between consecutive frames. A delta greater than 1 triggers an assertion failure
(confirmed via source-filename string `terrainmanager.cpp`, line 443).

### 9.5 Background thread schedule

A single background worker thread handles asynchronous cell loads. Its schedule is:

| Wake event             | Delay |
|------------------------|-------|
| First wake after start | 4 000 ms |
| Subsequent polling interval | 3 000 ms |

Load requests are enqueued as `{mapX, mapZ, areaId}` triples in a FIFO queue. A separate
synchronous load path exists for immediate-priority requests and is protected by a mutex shared
with the background thread.

---

## 10. Key cell object field layout (for completeness; not a binary file format)

This section documents select byte offsets within the in-memory cell object, included here
because `Assets.Parsers` populates these fields after loading the binary assets. All offsets are
byte offsets from the start of the cell object allocation.

| Byte offset | Type    | Field        | Notes              | Confidence |
|------------:|--------:|--------------|--------------------|------------|
| 0x3F4C      | i32     | mapX         | Grid X coordinate  | CONFIRMED  |
| 0x3F50      | i32     | mapZ         | Grid Z coordinate  | CONFIRMED  |
| 0x3F5C      | f32     | bboxXMin     | World bbox X min   | CONFIRMED  |
| 0x3F60      | f32     | bboxZMin     | World bbox Z min   | CONFIRMED  |
| 0x3F64      | f32     | bboxXMax     | World bbox X max   | CONFIRMED  |
| 0x3F68      | f32     | bboxZMax     | World bbox Z max   | CONFIRMED  |
| 0x3F70      | i32     | mapX (alias) | Redundant copy     | CONFIRMED  |
| 0x3F74      | i32     | mapZ (alias) | Redundant copy     | CONFIRMED  |
| 0x3F78      | i32     | areaId       | Area identifier    | CONFIRMED  |
| 0x3F80      | ptr     | tileTerrain  | Pointer to primary TileTerrain sub-object (0x47E10 bytes) | CONFIRMED |
| 0x3F84      | ptr     | massTerrain  | Pointer to MassTerrain sub-object (0x1214 bytes) | CONFIRMED |
| 0x3F88      | ptr     | extraLayer   | Pointer to extra layer sub-object (0x1094 bytes) | CONFIRMED |
| 0x3F8C–0x3FA8 | ptr[] | fx1…fx7    | Pointers to FX1–FX7 sub-objects (each 0x1094 bytes) | CONFIRMED |
| 0x6084      | u8      | activeFlag   | 1 = loaded, 0 = free/evictable | CONFIRMED |

---

## 11. Known unknowns (priority list for sample verification)

The following items are unverified and represent the highest-risk unknowns for implementors:

1. **`.mud` internal layout:** The 32 768-byte buffer layout is completely unknown. The
   "64 × 64 grid of 8-byte records" hypothesis exists but is unconfirmed. Cannot be parsed
   meaningfully without a sample file and a deeper loader trace.

2. **`.gad` format:** The loader is a no-op stub. The extension is referenced and the file is
   opened, but no bytes are consumed. Purpose and format are unknown.

3. **`.ted` height scale:** Float32 height values may be direct world-space Y coordinates or
   may require a scale multiplier applied elsewhere in the rendering pipeline. A sample `.ted`
   file paired with known terrain topology would disambiguate.

4. **`.ted` absence of header:** No magic number or version prefix was observed, but the VFS
   abstraction layer could handle a header silently. Confirmed only by the absence of an
   observed header-read before block 1 — not by seeing offset 0 of the file be a height value.

5. **`SolidRecord` (108 bytes) and triangle struct (48 bytes) internal layouts:** Only the
   strides are confirmed. Spatial positions, flags, and winding conventions within these records
   are entirely unverified.

---

## 12. Cross-references

- **Related formats:** `Docs/RE/formats/pak.md` (VFS container that delivers all per-cell files),
  `Docs/RE/formats/mesh.md` (building mesh blobs referenced by BUILDING sections)
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
