# Format: terrain map cell  (streaming terrain system — `.lst` / `.map` / `.ted` / `.mud` / `.bud` / `.up` / `.exd` / `.fx1`–`.fx7` / `.sod`)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

---

## Status block

| Attribute          | Value |
|--------------------|-------|
| `status`           | `sample-verified` — real asset samples available and cross-checked against parser analysis |
| `sample_verified`  | `true` — confirmed with 3 `.map`, 3 `.ted`, 3 `.mud`, 3 `.bud`, 3 `.sod`, 3 `.lst`, 3 `.up`, 3 `.exd`, and `.fx1`–`.fx7` blobs |
| `binary_analysed`  | `doida.exe` (legacy 32-bit client build) |
| `confidence`       | Fields labelled CONFIRMED are corroborated by parser read-sequence and/or real sample bytes. Fields labelled VERIFIED are confirmed directly by observed sample data. PARTIAL means size or existence is confirmed but field semantics are uncertain. UNVERIFIED means inferred from call-site context only. |

**Recently promoted (2026-06-12):** The `.mud` per-tile sound-index targets (which in-memory sound
table each byte indexes), the `.ted` texture-index reference chain (block 3), the `.ted`
direction-flag bit mapping (block 4), the `.ted` diffuse alpha byte semantic (block 5), and the
full internal layout of the `.sod` 48-byte collision record (now read as four XZ corners, not a
slope-intercept line) are all sample- and read-sequence-confirmed and flipped to VERIFIED/CONFIRMED
below. See sections 5.6–5.8, 6.2 and 11.3.

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
| `.mud`    | Headerless binary blob; ambient-sound tile grid (internal layout CONFIRMED — see section 6) |
| `.gad`    | Purpose UNKNOWN — loader is a no-op stub |
| `.map`    | Plain-text ASCII scene descriptor; references per-cell binary asset blobs |

Binary assets referenced from within `.map` include:

| Extension | Role |
|-----------|------|
| `.ted`    | Terrain geometry blob: heightmap, normals, colour, lookup tables |
| `.bud`    | Building mesh blob: static building and prop geometry |
| `.up`     | Upper-terrain geometry blob: overlay/bridge triangle list |
| `.exd`    | Extended-detail blob: same binary structure as `.up`; section association UNVERIFIED |
| `.fx1`–`.fx7` | Special-effect layer blobs (7 named slots) |
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

**Binary layout** (CONFIRMED — verified against 3 sample files; no magic or version prefix):

| Offset | Size        | Type      | Field   | Notes                                     | Confidence |
|-------:|-------------|-----------|---------|-------------------------------------------|------------|
| 0      | 4           | u32le     | `count` | Number of valid cell entries              | CONFIRMED  |
| 4      | `count × 4` | u32le[]   | `keys`  | One cell key per entry; see formula below | CONFIRMED  |

Cell key formula:

```
key = mapZ + 100000 × mapX
```

Verified: file size is exactly `4 + count × 4` bytes with no header prefix. The three samples
(1-entry 8-byte files and a 2-entry 12-byte file) all match this formula exactly.

**Known unknowns:** None outstanding — the three sample files are fully accounted for.

### 1.3 Per-cell base path

For cell `(mapX, mapZ)` in area `areaId`:

```
data/map<d0><d1><d2>/dat/d<d0><d1><d2>x<mapX>z<mapZ>
```

The three fixed per-cell files are formed by appending `.mud`, `.gad`, and `.map` to this base
path. Additional blobs (`.ted`, `.bud`, `.up`, `.sod`, etc.) are referenced by path from within
the `.map` file.

### 1.4 Per-cell world-space bounds

The world-space bounding rectangle of a cell is derived from its grid coordinates:

```
worldX_min = (mapX - 10000) × 1024.0
worldZ_min = (mapZ - 10000) × 1024.0
worldX_max = worldX_min + 1024.0
worldZ_max = worldZ_min + 1024.0
```

The subtraction of 10000 is the world-origin bias. One cell covers exactly **1024 × 1024 world
units**. The mapping of "world units" to metres or any other physical scale is UNVERIFIED.

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

The `.map` file is a **plain ASCII text** file with CRLF line endings (`0x0D 0x0A`) and TAB
indentation (`0x09`). It contains no binary bytes.

### 3.1 Grammar

```
<mapfile>   ::= <section>*
<section>   ::= <sectionKW> '{' <directive>* '}'
<directive> ::= 'WIDTH'           <integer>
              | 'HEIGHT'          <integer>
              | 'GRID'            <integer>
              | 'MAX_HEIGHTFILED' <float>
              | 'MIN_HEIGHTFILED' <float>
              | 'ORIGIN'          <float> ',' <float>
              | 'DATAFILE'        <vfsPath>
              | 'TEXTURES'        '{' <texEntry>* '}'
<texEntry>  ::= <intFlag> <intTexId> ['"' <devPath> '"']
<sectionKW> ::= 'TERRAIN' | 'EXTRA_TERRAIN' | 'UP_TERRAIN' | 'BUILDING'
              | 'FX1' | 'FX2' | 'FX3' | 'FX4' | 'FX5' | 'FX6' | 'FX7'
              | 'SOLID'
```

All 12 section keywords are CONFIRMED in the parser and samples. Lines where the first
non-whitespace character is `#` are comments and are ignored.

### 3.2 Sections and `DATAFILE` targets

Each section contains a `DATAFILE` directive that supplies the VFS-relative path to the
associated binary blob. The sub-loader is selected by section keyword, not by file extension.

| Section keyword  | Binary blob extension | Loader role                                     | Confidence |
|------------------|-----------------------|-------------------------------------------------|------------|
| `TERRAIN`        | `.ted`                | Primary tile geometry                           | CONFIRMED (parser + samples) |
| `EXTRA_TERRAIN`  | `.ted` (spec) or `.exd` (hypothesis — see §3.3) | Secondary tile geometry | CONFIRMED (parser); blob ext UNVERIFIED |
| `UP_TERRAIN`     | `.up`                 | Upper/overlay tile geometry                     | CONFIRMED (parser + samples) |
| `BUILDING`       | `.bud`                | Static building and prop geometry               | CONFIRMED (parser + samples) |
| `FX1`–`FX7`      | `.fx1`–`.fx7`         | Special-effect layers                           | CONFIRMED (parser + samples) |
| `SOLID`          | `.sod`                | Collision geometry                              | CONFIRMED (parser + samples) |

### 3.3 EXTRA_TERRAIN and `.exd` blobs

Sample `.exd` files share cell coordinates with cells that could have `EXTRA_TERRAIN` sections,
and their binary structure is identical to `.up` blobs (triangle-count prefix + 40-byte triangle
records). However, the string `.exd` does not appear in the parser's string table, and none of
the available `.map` samples contain an `EXTRA_TERRAIN` section. The mapping of `.exd` to
`EXTRA_TERRAIN` is **plausible but UNVERIFIED**. A `.map` file containing an `EXTRA_TERRAIN`
block would settle this.

### 3.4 Geometry directives (TERRAIN section)

These directives appear inside `TERRAIN {}` blocks. All values confirmed from samples:

| Directive         | Type    | Sample value  | Semantics                              | Confidence |
|-------------------|---------|---------------|----------------------------------------|------------|
| `WIDTH`           | integer | `64`          | Quad grid width (quads per row)        | CONFIRMED  |
| `HEIGHT`          | integer | `64`          | Quad grid height (quads per column)    | CONFIRMED  |
| `GRID`            | integer | `16`          | World-unit spacing between vertices    | CONFIRMED  |
| `MAX_HEIGHTFILED` | float   | e.g. `266.053680` | Maximum world-Y in this cell; informational only | CONFIRMED |
| `MIN_HEIGHTFILED` | float   | e.g. `1.276898`   | Minimum world-Y in this cell; informational only | CONFIRMED |
| `ORIGIN`          | float, float | e.g. `0.000, -1024.000` | World-space XZ origin of the quad, comma-separated; equals `(mapX-10000)*1024, (mapZ-10000)*1024` | CONFIRMED |

**Note:** `MAX_HEIGHTFILED` and `MIN_HEIGHTFILED` are verbatim keywords in the original data
files, preserving the dropped-L spelling; an implementation must match this exact spelling.

### 3.5 `TEXTURES` directive

```
TEXTURES
{
    <intFlag>  <intTexId>  "<devPath>"
    ...
}
```

Each entry is a pair of integers followed by an optional quoted development path. Parse logic:

1. Read the next whitespace-delimited token. If it parses as an integer greater than zero, it is
   `intFlag` (consumed and discarded — semantics unknown, always `1` in samples).
2. Read the following token as `intTexId` (integer). Call the section's `setTextureId(intTexId)`.
3. The quoted `"devPath"` token (a development-machine source path) is read by the tokenizer
   and silently ignored at runtime.
4. If the token from step 1 parses as zero or less, check for `}` to close the block.

| Field      | Type    | Sample value             | Semantics                                    | Confidence |
|------------|---------|--------------------------|----------------------------------------------|------------|
| `intFlag`  | integer | `1`                      | Read and discarded; purpose UNVERIFIED       | CONFIRMED (read); UNVERIFIED (semantics) |
| `intTexId` | integer | e.g. `155`               | Index into the `bgtexture.lst` global pool   | CONFIRMED  |
| `devPath`  | string  | `"d:/do_project/..."` | Development source path; runtime-ignored     | CONFIRMED (present; not loaded) |

Texture table capacity per section type:

| Section type          | Maximum entries |
|-----------------------|-----------------|
| `TERRAIN`/`TileTerrain` | UNVERIFIED    |
| `UP_TERRAIN`/`MassTerrain` | 128        | CONFIRMED |
| `FX1`–`FX7`           | 32 per slot     | CONFIRMED |

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

| Offset | Size              | Type      | Field        | Notes                                       | Confidence |
|-------:|------------------:|-----------|--------------|---------------------------------------------|------------|
| 0      | 4                 | u32le     | `texCount`   | Number of background texture entries; valid range 1–1999 | CONFIRMED |
| 4      | `texCount × 1`    | u8[]      | `typeBytes`  | One type byte per entry; `1` = animated, `≥ 2` = static | UNVERIFIED (semantics) |
| 4 + `texCount` | `texCount × 76` | record[] | `texRecords` | One 76-byte GHTex record per entry | UNVERIFIED |

**Known unknowns:** The internal layout of each 76-byte GHTex record is not yet documented.
Only the stride (76 bytes) and the type-byte conditional are inferred from loader logic.

---

## 5. Terrain geometry blob — `.ted`

The `.ted` blob is the binary payload referenced by `DATAFILE` inside a `TERRAIN`,
`EXTRA_TERRAIN`, or `UP_TERRAIN` section. It has **no file header**: offset 0 of the file is
always the first height value of the heightmap. This is CONFIRMED — the loader issues its first
read at file offset 0 with no preceding header read, and all three sample files begin with valid
IEEE 754 height values at byte 0.

Files named `*.ted.post` use an identical 46987-byte layout and are written by the in-game
terrain editor as workspace copies. The runtime client reads only `.ted` files, never `.ted.post`.

### 5.1 Grid geometry

| Property          | Value                     | Confidence |
|-------------------|---------------------------|------------|
| Vertex grid       | 65 × 65 vertices          | CONFIRMED  |
| Quad grid         | 64 × 64 quads per cell    | CONFIRMED  |
| Vertex spacing    | 16.0 world units          | CONFIRMED (derived: 1024 / 64 = 16; literal constant in loader) |
| Total file size   | 46 987 bytes (0xB78B)     | CONFIRMED (three samples, all identical size; sum of the five block sizes) |

### 5.2 Axis orientation

Rows are constant-Z slices; columns are constant-X slices. CONFIRMED by seam-continuity test
between two adjacent Z-axis-neighbouring cells: the last row (index 64) of the lower-Z cell
matches the first row (index 0) of the higher-Z cell within float rounding noise.

```
heights[row * 65 + col]
  row = Z axis:  row 0 = cell Z minimum (southern edge),  row 64 = cell Z maximum (northern edge)
  col = X axis:  col 0 = cell X minimum (western edge),   col 64 = cell X maximum (eastern edge)
```

All five data blocks use this same row-major, Z-as-row, X-as-column convention.

### 5.3 Sequential block layout

The file is read as five contiguous fixed-size blocks with no padding or alignment between them.
Client error strings name four of the five blocks directly.

| Block | Byte offset | Size (bytes) | Hex size | Client name / evidence         | Element type | Elements    | Confidence |
|------:|------------:|-------------:|----------|--------------------------------|--------------|-------------|------------|
| 1     | 0           | 16 900       | 0x4204   | `height_map` (error string)    | f32le        | 65 × 65 = 4 225 | CONFIRMED |
| 2     | 16 900      | 12 675       | 0x3183   | `normal_map` (error string)    | i8 × 3      | 65 × 65 = 4 225 | CONFIRMED |
| 3     | 29 575      | 256          | 0x100    | texture index (unnamed)        | u8           | 256         | CONFIRMED |
| 4     | 29 831      | 256          | 0x100    | `direction_map` (error string) | u8           | 256         | CONFIRMED |
| 5     | 30 087      | 16 900       | 0x4204   | `diffuse_map` (error string)   | u8 × 4      | 65 × 65 = 4 225 | CONFIRMED |

Block 5 offset 30 087 is independently confirmed by the editor writer, which seeks explicitly
to that offset before patching the diffuse block.

### 5.4 Block 1 — Heightmap

- 4 225 IEEE 754 single-precision floats, little-endian, stored in row-major order (see §5.2).
- Values are **direct world-space Y coordinates** (no scale multiplier). CONFIRMED — the loader
  applies no scale to the f32 values; the only related constant is 16.0 (the XZ vertex spacing).
- Sample observed height ranges: flat sea tiles hold a constant value near +26 world units;
  hilly tiles span approximately −160 to +380 world units.

### 5.5 Block 2 — Vertex normals

- 4 225 packed RGB triples, one per vertex, same row-major order as block 1.
- **Encoding:** each component is a signed byte (i8); decode as `N_component = (i8)byte / 127.0`.
  The divisor 127.0 is a literal constant in the loader. The loader sign-extends each byte before
  dividing, confirming signed input. CONFIRMED.
- **Channel order:** R = Nx, G = Ny (vertical/up), B = Nz.
- **Verification:** a flat horizontal tile (all heights equal) yields all normals (0, 127, 0),
  which decodes to (0.0, 1.0, 0.0) — a perfect Y-up unit vector. Hilly-tile normals decode to
  near-unit vectors aligned with the computed surface slope.
- **Axis convention:** normals are in world space. The Y axis is vertical-up. Terrain normals
  always point upward, so the G channel (Ny) is always in the range 0–127; R and B span −128..+127.

### 5.6 Block 3 — Texture index grid

- 256 unsigned bytes forming a **16 × 16 grid** (same row=Z, col=X convention; 4 × 4 quad region
  per entry, since 64 / 16 = 4 quads per axis per entry).
- Values are **1-based** (no zero observed in any sample; maximum observed is 11).
- The `.map` `GRID 16` directive matches this 16 × 16 subdivision factor exactly.
- **Semantic:** each byte selects the background texture for the corresponding 4 × 4 quad region.
  The value is a **1-based position into the per-cell texture list** that the section builds from
  the `.map` file's `TEXTURES {}` block, and that list in turn holds indices into the global
  `bgtexture.lst` pool. The full resolution chain is therefore two hops:

  ```
  texture_index_byte (1-based)
    -> per_cell_texture_list[byte - 1]   (built from the .map TEXTURES{} entries, capacity 128)
    -> bgtexture.lst[pool_slot]          (global texture pool loaded at startup)
  ```

  CONFIRMED: the loader stashes each registered texture into the section's per-cell array as the
  `.map` is parsed, and stores this block-3 byte alongside each tile for later lookup against that
  array. The two-hop resolution (per-cell list, then global pool) is confirmed; the per-cell list
  caps at 128 entries. Sample `d036` exercises values 1..11 across distinct texture zones; `d000`
  uses only value 1.
- **Value 0:** no sample tile carries value 0. A 0 byte would be an invalid 1-based index and is
  read as "no texture override / fallback"; this fallback meaning is PARTIAL (inferred, not yet
  seen in a sample).

### 5.7 Block 4 — Quad split / UV orientation flags

- 256 unsigned bytes forming a **16 × 16 grid**, same coverage as block 3 (one byte per 4 × 4 quad
  patch).
- Only the two least-significant bits are used; observed values are 0, 1, 2, 3 only.
- **Bit-to-axis mapping** (CONFIRMED from the UV-generation path):

  | Mask  | Name     | Effect when set                                              |
  |-------|----------|-------------------------------------------------------------|
  | `0x01`| `s_flip` | Mirror the S (horizontal, U) texture coordinate of the patch |
  | `0x02`| `t_flip` | Mirror the T (vertical, V) texture coordinate of the patch  |

- **UV generation:** within each patch the loader walks the 5 × 5 vertices and assigns a UV
  fraction `f = vertex_offset × 0.25` (stepping 0.0, 0.25, 0.5, 0.75, 1.0). When `s_flip` is set,
  the S coordinate becomes `1.0 − f`; otherwise it is `f`. The same rule applies to T using
  `t_flip`. The four bit combinations therefore are:

  | Value | S axis            | T axis            |
  |-------|-------------------|-------------------|
  | `0b00`| forward           | forward           |
  | `0b01`| mirrored          | forward           |
  | `0b10`| forward           | mirrored          |
  | `0b11`| mirrored          | mirrored          |

- **Diagonal note:** because the texture seam follows the UV orientation, the flip combination also
  determines the visual diagonal of the two-triangle quad split when the patch is rendered. The
  client error string names this block "direction map". The S/T flip mapping is CONFIRMED; whether
  a specific combination additionally re-selects the triangulation diagonal versus only re-orienting
  the texture is PARTIAL.

### 5.8 Block 5 — Per-vertex diffuse colour

- 4 225 four-byte RGBA quads, one per vertex, same row-major order as block 1.
- **Channel order: R at +0, G at +1, B at +2, A at +3** (RGBA, not ARGB). CONFIRMED — the editor
  write path fills the three colour bytes individually at those aligned offsets.
- All three available samples contain exclusively `(R=255, G=255, B=255, A=0)` across all
  vertices — the default unset (pure-white) state.
- **Alpha byte (+3):** the A byte is **0x00 in every sampled vertex and is never read by the
  runtime loader**. It is alignment padding emitted by the editor to keep each pixel 4-byte aligned,
  not a meaningful channel. CONFIRMED (zero across all samples; no runtime read path consumes it).
  Any modern re-encoder should write A as 0 and must not depend on it.
- **On-disk 2× / load-time 0.5× encoding:** the editor doubles each colour byte on write
  (`stored = 2 × source`) and the loader halves it on read (`output = stored × 0.5`), so the
  round-trip is identity. The runtime halving deliberately leaves brightness headroom so that
  dynamic lighting can add up to full white at draw time. This scaling is code-confirmed; it is not
  independently sample-verified because all available samples are uniform white, where the 2×
  relationship is unobservable (255 is not exactly 2 × any stored byte). Treat the 2× factor as
  CONFIRMED-by-code / PARTIAL-by-sample.

### 5.9 Known unknowns

- Block 3 reference target: RESOLVED — the 1-based byte indexes the per-cell `TEXTURES{}` list,
  which itself indexes the global `bgtexture.lst` pool (§5.6). Open (PARTIAL): the fallback meaning
  of a value-0 byte, never seen in a sample.
- Block 4 flag mapping: RESOLVED for texture orientation — bit `0x01` mirrors S (U), bit `0x02`
  mirrors T (V) (§5.7). Open (PARTIAL): whether a flip combination also re-selects the quad
  triangulation diagonal rather than only re-orienting the texture.
- Block 5 alpha: RESOLVED — the A byte is alignment padding, always 0, never read (§5.8). The 2×
  store / 0.5× load scaling is code-confirmed but stays sample-unverified until a tinted (non-white)
  tile is captured.
- World-unit-to-physical-scale relationship (1024 wu per cell = ? metres).

---

## 6. Ambient-sound tile blob — `.mud`

The `.mud` file is a **headerless fixed-size binary blob** of exactly **32 768 bytes (0x8000)**.
There is no magic number, version field, or length prefix. The file is loaded in a single read
call. Its sole consumer at runtime is the sound manager, which uses it to select music and ambient
sound indices for the player's current position. It has no relation to collision or walkability.

### 6.1 Grid dimensions

| Property                  | Value                   | Confidence |
|---------------------------|-------------------------|------------|
| Grid columns (X axis)     | 64                      | CONFIRMED  |
| Grid rows (Z axis)        | 64                      | CONFIRMED  |
| Record stride             | 8 bytes                 | CONFIRMED  |
| Total size                | 64 × 64 × 8 = 32 768 B | CONFIRMED (three samples) |
| Storage order             | Row-major (Z = row, X = column) | CONFIRMED |
| Tile size (world units)   | 16 × 16                 | CONFIRMED  |

Index formula (record byte offset from file start):

```
col            = floor(worldX / 16) & 0x3F
row            = floor(worldZ / 16) & 0x3F
record_offset  = (row × 64 + col) × 8
```

The `& 0x3F` mask (6-bit clamp to 0–63) is derived from the integer division and mask observed
in the index function.

### 6.2 Record layout — 8 bytes per tile

All fields are single bytes; endianness is not material.

| Offset | Size | Type  | Field           | Observed values      | Confidence | Notes |
|-------:|-----:|-------|-----------------|----------------------|------------|-------|
| 0      | 1    | u8    | `pad0`          | `0x00` only          | VERIFIED   | Always zero across all 3 samples (12 288 observations); no code reads this byte |
| 1      | 1    | u8    | `pad1`          | `0x00` only          | VERIFIED   | Always zero; no code reads this byte |
| 2      | 1    | u8    | `music_group`   | 0, 7                 | VERIFIED   | 1-based index into the per-area **background-music (BGM) sound table**. Value 0 = no music. Lookup is `table[index]`; entry stride is 48 bytes and the entry's first field is the playable sound resource handle |
| 3      | 1    | u8    | `ambient_idx_0` | 0, 2, 3, 16          | VERIFIED   | Index into the per-area **ambient-loop (BGE) sound table** (48-byte stride). First of two ambient slots. Value 0 = no sound. In samples this slot pairs consistently with the value 16 when active |
| 4      | 1    | u8    | `ambient_idx_1` | 0, 17                | VERIFIED   | Same ambient table as byte 3. Second ambient slot. Value 0 = no sound. Pairs with value 17 when active in samples |
| 5      | 1    | u8    | `effect_idx_0`  | 0, 57, 58, 91, 106   | VERIFIED   | Index into the per-area **event-effect (EFF) sound table** (48-byte stride). First of three effect slots. Value 0 = no sound |
| 6      | 1    | u8    | `effect_idx_1`  | 0, 91, 106           | VERIFIED   | Same EFF table as byte 5. Second effect slot. Value 0 = no sound. Non-zero values (91, 106) occur in samples, so this slot is genuinely used |
| 7      | 1    | u8    | `effect_idx_2`  | `0x00` only          | VERIFIED (limited) | Same EFF table. Third effect slot. The slot is read (the player loop runs three iterations) but is `0` in every sampled tile, so no non-zero value is yet observed |

**Sound tables referenced:** the position-driven ambient update reads exactly three of the
per-area sound tables — the BGM (music) table via byte 2, the BGE (ambient-loop) table via bytes
3–4, and the EFF (event-effect) table via bytes 5–7. Each table holds up to 256 entries at a
48-byte (0x30) stride; the first field of an entry is the playable sound resource handle and the
remaining entry fields are UNVERIFIED. Two further per-area sound tables exist (a walk table and a
run table) but are **not** indexed from the `.mud` grid in the analysed path — footstep sounds
appear to be driven by movement/animation events rather than per-tile lookup, which is the leading
explanation for why bytes 0–1 are unused (see §6.3).

### 6.3 Known unknowns

- `pad0` and `pad1` (bytes 0–1) purpose: always zero across all 12 288 sampled tiles and never
  read by the analysed ambient-update path. The leading hypothesis is that they are simply unused
  (a per-tile walkable-surface or footstep-sound flag that the movement system would have consumed
  was not found in the cell-lookup path; footstep audio appears event-driven). They could
  alternatively be reserved/version padding. PARTIAL — only zero values are available.
- `effect_idx_2` non-zero values: the loop slot exists but is zero in all samples.
- Maximum valid sound index (highest observed: 106 for `effect_idx_0`; theoretical maximum: 255).
- Whether row 0 corresponds to world-min-Z or world-max-Z (the index formula is confirmed, but
  the absolute Z orientation within a cell is not directly observable from binary layout alone).

---

## 7. Unknown extension — `.gad`

The `.gad` file is loaded as the third fixed per-cell asset, but the loader is a complete stub
that takes no action and returns success unconditionally. The format and purpose of `.gad` are
entirely UNKNOWN.

---

## 8. Building mesh blob — `.bud`

The `.bud` blob is the binary payload referenced by `DATAFILE` inside a `BUILDING` section. It
contains a variable-count list of mesh objects, each consisting of a vertex array and an index
list. The name "BUD" likely abbreviates "BUilDing" (the symbol prefix for all related
loader functions uses this three-letter prefix). There is no file-level magic or version field.

### 8.1 Top-level layout

| Offset | Size                  | Type      | Field           | Notes                                | Confidence |
|-------:|----------------------:|-----------|-----------------|--------------------------------------|------------|
| 0      | 4                     | u32le     | `objectCount`   | Number of MassObject records         | CONFIRMED  |
| 4      | (variable; see §8.2)  | record[]  | `MassObject[]`  | Tightly packed; no inter-record pad  | CONFIRMED  |

### 8.2 MassObject record (variable size, packed)

Fields are read in the order listed; there is no inter-field alignment padding.

| Read order | Size                        | Type      | Field          | Notes                                                  | Confidence |
|-----------:|----------------------------:|-----------|----------------|--------------------------------------------------------|------------|
| 1          | 1                           | u8        | `type`         | Object type discriminator; value `0` in all samples; other values UNKNOWN | CONFIRMED (read); UNVERIFIED (semantics) |
| 2          | 4                           | u32le     | `texId`        | Index into this section's TEXTURES list (1-based); value `1` in all samples | CONFIRMED |
| 3          | 4                           | u32le     | `vertexCount`  | Number of vertices; max 3072 (0xC00) enforced by loader | CONFIRMED |
| 4          | `vertexCount × 32`          | record[]  | vertex array   | 32-byte vertex records; see §8.3                       | CONFIRMED  |
| 5          | 4                           | u32le     | `indexCount`   | Number of u16 triangle indices                         | CONFIRMED  |
| 6          | `indexCount × 2`            | u16le[]   | index array    | Triangle list (every 3 consecutive indices = 1 triangle) | CONFIRMED |

**Note on byte alignment:** the `type` u8 at read-order 1 is not padded to 4 bytes. Fields begin
at whatever byte offset follows the preceding field.

### 8.3 Vertex record — 32 bytes (8 × f32le)

| Byte offset within vertex | Size | Type  | Field   | Notes                               | Confidence |
|--------------------------:|-----:|-------|---------|-------------------------------------|------------|
| 0                         | 4    | f32le | `X`     | World-space X coordinate            | CONFIRMED (values in expected cell range) |
| 4                         | 4    | f32le | `Y`     | World-space Y (height)              | CONFIRMED  |
| 8                         | 4    | f32le | `Z`     | World-space Z coordinate            | CONFIRMED  |
| 12                        | 20   | —     | (5 × f32le, purpose unknown) | May be normals, UV, colour | UNVERIFIED |

**Known unknowns for vertex bytes 12–31:** candidates are (Nx, Ny, Nz) packed normal plus (U, V)
texture coordinates, but the packing and whether any byte encodes alpha or material data is not
confirmed.

### 8.4 Known unknowns

- `type` field semantics: only value `0` observed; what other values exist and what they select.
- Vertex bytes 12–31: the five remaining f32le fields per vertex.

---

## 9. Upper-terrain / extended-detail blobs — `.up` and `.exd`

Both `.up` (referenced by `UP_TERRAIN` sections) and `.exd` (section association UNVERIFIED —
see §3.3) share a common binary structure: a count prefix followed by an array of 40-byte
triangle records. All coordinate fields are world-space f32 values.

### 9.1 File layout

| Offset | Size                    | Type      | Field             | Notes                         | Confidence |
|-------:|------------------------:|-----------|-------------------|-------------------------------|------------|
| 0      | 4                       | u32le     | `triangleCount`   | Number of triangle records    | CONFIRMED  |
| 4      | `triangleCount × 40`    | record[]  | triangle array    | 40-byte records; see §9.2     | CONFIRMED  |

Total file size: `4 + triangleCount × 40`. Verified against three `.up` samples (all 484 bytes =
4 + 12 × 40) and three `.exd` samples (all 84 bytes = 4 + 2 × 40).

### 9.2 Triangle record — 40 bytes (10 × f32le)

| Byte offset | Size | Type  | Field   | Notes                                                        | Confidence |
|------------:|-----:|-------|---------|--------------------------------------------------------------|------------|
| 0           | 4    | f32le | `X0`    | Vertex 0 world X                                             | CONFIRMED  |
| 4           | 4    | f32le | `Y0`    | Vertex 0 world Y                                             | CONFIRMED  |
| 8           | 4    | f32le | `Z0`    | Vertex 0 world Z                                             | CONFIRMED  |
| 12          | 4    | f32le | `X1`    | Vertex 1 world X                                             | CONFIRMED  |
| 16          | 4    | f32le | `Y1`    | Vertex 1 world Y                                             | CONFIRMED  |
| 20          | 4    | f32le | `Z1`    | Vertex 1 world Z                                             | CONFIRMED  |
| 24          | 4    | f32le | `X2`    | Vertex 2 world X                                             | CONFIRMED  |
| 28          | 4    | f32le | `Y2`    | Vertex 2 world Y                                             | CONFIRMED  |
| 32          | 4    | f32le | `Z2`    | Vertex 2 world Z                                             | CONFIRMED  |
| 36          | 4    | f32le | `extra` | In all available samples equals Y0 = Y1 = Y2 (planar triangles); may be a surface Y-level or material discriminator | PARTIAL |

All triangle vertex coordinates in samples fall within the expected cell world-space bounds.

**Known unknowns:** the `extra` field at byte 36 always equals the common Y height in these flat
samples; behaviour for non-planar triangles is unknown.

---

## 10. Special-effect layer blobs — `.fx1` through `.fx7`

Each FX section (`FX1`–`FX7`) has its own DATAFILE blob with the corresponding extension
(`.fx1`–`.fx7`). All blobs share a common outer structure, but the header size and vertex record
stride depend on a `typeId` field within the header.

### 10.1 Common outer structure

Every FX blob begins with a header whose first three u32le words are:

| Offset | Type  | Field            | Confidence |
|-------:|-------|------------------|------------|
| 0      | u32le | `objectCount`    | CONFIRMED  |
| 4      | u32le | `subObjectCount` | CONFIRMED  |
| 8      | u32le | padding (= 0)    | CONFIRMED  |

After the header: a flat vertex array followed immediately by a u16le index array (no separate
index-count prefix; `indexCount` is stored in the header). The vertex stride and header size
depend on `typeId`.

### 10.2 FX type 15 — `.fx1` and `.fx2`

Header is 24 bytes (6 × u32le):

| Offset | Type  | Field          | Confidence |
|-------:|-------|----------------|------------|
| 0      | u32le | `objectCount`  | CONFIRMED  |
| 4      | u32le | `subObjCount`  | CONFIRMED  |
| 8      | u32le | padding (= 0)  | CONFIRMED  |
| 12     | u32le | `typeId` (= 15)| CONFIRMED  |
| 16     | u32le | `vertexCount`  | CONFIRMED  |
| 20     | u32le | `indexCount`   | CONFIRMED  |

`.fx1` vertex record — 36 bytes (9 × f32le):

| Byte offset | Type  | Field   | Notes                           | Confidence |
|------------:|-------|---------|----------------------------------|------------|
| 0           | f32le | `X`     | World X                          | CONFIRMED  |
| 4           | f32le | `Y`     | World Y                          | CONFIRMED  |
| 8           | f32le | `Z`     | World Z                          | CONFIRMED  |
| 12          | f32le | `Nx`    | Normal X (value near ±1)         | CONFIRMED  |
| 16          | f32le | `Ny`    | Normal Y                         | CONFIRMED  |
| 20          | f32le | `Nz`    | Normal Z                         | CONFIRMED  |
| 24          | f32le | `unk6`  | Anomalous bit pattern in samples; may be a packed sentinel or alpha | UNVERIFIED |
| 28          | f32le | `U`     | Texture coordinate U             | PARTIAL    |
| 32          | f32le | `V`     | Texture coordinate V             | PARTIAL    |

`.fx2` vertex record — 44 bytes: same as `.fx1` with two additional f32le fields at bytes 36
and 40 (second UV set or lightmap UVs). PARTIAL confidence.

File size formula: `24 + vertexCount × stride + indexCount × 2`. Verified for both `.fx1` (stride
36, 3 verts, 3 indices → 138 bytes) and `.fx2` (stride 44, 3 verts, 3 indices → 162 bytes).

### 10.3 FX type 5 — `.fx3`

Header is 48 bytes (12 × u32le/f32le):

| Offset | Type  | Field          | Confidence |
|-------:|-------|----------------|------------|
| 0      | u32le | `objectCount`  | CONFIRMED  |
| 4      | u32le | `subObjCount`  | CONFIRMED  |
| 8      | u32le | padding        | CONFIRMED  |
| 12     | u32le | `typeId` (= 5) | CONFIRMED  |
| 16     | u32le | `unk4` (= 0)   | UNVERIFIED |
| 20     | f32le | `unk5` (= 1.0) | UNVERIFIED |
| 24     | u32le | `unk6` (= 0)   | UNVERIFIED |
| 28     | u32le | `unk7` (= 5)   | UNVERIFIED |
| 32     | u32le | `unk8` (= 5)   | UNVERIFIED |
| 36     | u32le | padding (= 0)  | CONFIRMED  |
| 40     | u32le | `vertexCount`  | CONFIRMED  |
| 44     | u32le | `indexCount`   | CONFIRMED  |

Vertex stride: 36 bytes (same layout as `.fx1`). File size formula: `48 + vertexCount × 36 +
indexCount × 2`. Verified (4 verts, 6 indices → 204 bytes).

### 10.4 Larger FX blobs — `.fx4`, `.fx5`, `.fx6`, `.fx7`

These blobs share the common outer structure but are more complex. Sizes are fully accounted for
`.fx5` and `.fx6`; header field semantics are UNVERIFIED for all four.

| Extension | Approx. size | Vertex stride | Notes                                                  | Confidence |
|-----------|-------------|---------------|--------------------------------------------------------|------------|
| `.fx4`    | 1 076 B     | UNKNOWN       | Contains 2 top-level objects with 3 sub-objects; full decode incomplete | PARTIAL |
| `.fx5`    | 880 B       | 36            | 48-byte header similar to type 5; size fully accounted | PARTIAL |
| `.fx6`    | 29 444 B    | 32            | 40-byte header; `objectCount` field may be a grid dimension rather than a count | PARTIAL |
| `.fx7`    | 35 202 B    | UNKNOWN       | `objectCount` = 1; full decode incomplete              | PARTIAL |

**Note on `.fx6` vertex stride:** the 32-byte stride differs from the 36-byte stride used by
`.fx1`/`.fx3`. Whether this difference is driven by `typeId` or another header field is UNVERIFIED.

---

## 11. Collision solid blob — `.sod`

The `.sod` blob is the binary payload referenced by `DATAFILE` inside a `SOLID` section. It
encodes per-cell 2D collision geometry in the XZ world-space plane. All multi-byte fields are
little-endian. There is no file-level magic or version prefix.

The format is **strictly 2D**: collision testing is performed in the XZ plane; there is no Y
(height) component in the geometry.

### 11.1 Top-level layout

```
File:
  u32le           solidCount
  SolidRecord[solidCount]          — fixed stride 108 bytes each
  — then, for each SolidRecord i in [0, solidCount):
  u32le           quadCount_i
  QuadRecord[quadCount_i]          — fixed stride 48 bytes each
```

The 48-byte record is the unit of collision geometry. The game code calls these records
"triangles" internally (and the per-solid count field "triCount"), but each on-disk record actually
stores **four XZ corner points** of a 2D quad plus four trailing scalars (see §11.3). This spec
calls them **collision quads** to avoid confusion with 3D triangle meshes.

**Important:** the per-solid `quadCount` u32le appears in the data stream **after** the entire flat
`SolidRecord` array, interleaved with its corresponding `QuadRecord` array. It is NOT embedded
before the matching `SolidRecord`. A redundant copy of the count is also stored inside the
`SolidRecord` at byte offset +060, but the parser reads the stream copy; the internal copy is
overwritten at runtime with a heap pointer.

File size formula (all three samples verified with zero remainder):

```
file_size = 4 + solidCount × (108 + 4 + quadCount × 48)
```

Worked sample sizes (zero remainder): a 1-solid / 4-quad cell is `4 + (108 + 4 + 4 × 48) = 308`
bytes; a 1-solid / 3-quad cell is `4 + (108 + 4 + 3 × 48) = 260` bytes. Both match their samples
exactly.

### 11.2 SolidRecord — 108 bytes (0x6C)

| Offset | Size | Type  | Field                      | Notes                                                                 | Confidence |
|-------:|-----:|-------|----------------------------|-----------------------------------------------------------------------|------------|
| +000   | 4    | f32le | `aabb_xmin`               | AABB minimum X; used by AABB overlap test                             | VERIFIED   |
| +004   | 4    | f32le | `aabb_zmin`               | AABB minimum Z                                                        | VERIFIED   |
| +008   | 4    | f32le | `aabb_xmax`               | AABB maximum X                                                        | VERIFIED   |
| +012   | 4    | f32le | `aabb_zmax`               | AABB maximum Z; AABB equals the union of all owned QuadRecord AABBs    | VERIFIED   |
| +016   | 44   | —     | `_reserved_a`             | All zero on disk across every sample (including two cells with identical geometry under different map IDs, byte-for-byte equal). Not read by any analysed routine; purpose unknown | VERIFIED (all-zero) / UNVERIFIED (meaning) |
| +060   | 4    | u32le | `quad_count_embedded`     | Redundant copy of the per-solid quad count; parser reads the separate stream copy instead and overwrites this slot at runtime with the count it allocated | VERIFIED (equals stream count in all samples) |
| +064   | 4    | u32le | `_authoring_ptr`          | **Stale heap pointer left over from the authoring machine** — differs between cells, is a plausible 32-bit address, and is overwritten at load with the runtime pointer to the quad array. Carries no file-format meaning; a parser MUST ignore it | VERIFIED (stale pointer) |
| +068   | 40   | —     | `_reserved_b`             | All zero on disk in every sample; repurposed at runtime as a scratch/visited-frame cache (not file data) | VERIFIED (all-zero) / UNVERIFIED (meaning) |

### 11.3 QuadRecord — 48 bytes (0x30)

> **Correction (2026-06-12):** an earlier revision of this spec described the 48-byte record as a
> slope-intercept *line segment* (`slope`/`intercept`/`lineKind` fields). Direct inspection of the
> sample bytes shows that interpretation was wrong: the first 32 bytes are **four explicit XZ
> corner points** that form a closed quad whose extents reproduce the parent `SolidRecord` AABB
> exactly. The corner layout below supersedes the line-segment reading. The four trailing scalars
> at +032..+047 remain only partly understood.

Each record stores one 2D collision quad in the XZ world-space plane as four corner points,
followed by four scalars. There is no Y (height) component — collision is evaluated in XZ at the
solid's elevation. The engine names the count field "triangle count" and may split each quad into
two triangles at runtime; the split diagonal is not determined from the available code.

| Offset | Size | Type  | Field    | Notes                                                                          | Confidence |
|-------:|-----:|-------|----------|--------------------------------------------------------------------------------|------------|
| +000   | 4    | f32le | `x0`     | Corner 0 world X                                                                | VERIFIED   |
| +004   | 4    | f32le | `z0`     | Corner 0 world Z                                                                | VERIFIED   |
| +008   | 4    | f32le | `x1`     | Corner 1 world X                                                                | VERIFIED   |
| +012   | 4    | f32le | `z1`     | Corner 1 world Z                                                                | VERIFIED   |
| +016   | 4    | f32le | `x2`     | Corner 2 world X                                                                | VERIFIED   |
| +020   | 4    | f32le | `z2`     | Corner 2 world Z                                                                | VERIFIED   |
| +024   | 4    | f32le | `x3`     | Corner 3 world X                                                                | VERIFIED   |
| +028   | 4    | f32le | `z3`     | Corner 3 world Z                                                                | VERIFIED   |
| +032   | 4    | f32le | `plane0` | Trailing scalar 0. Non-zero and varies between quads; candidate: a plane-equation coefficient or a Y surface height. Sample magnitude is in the tens (e.g. ≈ −27) | PARTIAL |
| +036   | 4    | f32le | `plane1` | Trailing scalar 1. `0.0` in every sampled quad | PARTIAL |
| +040   | 4    | f32le | `plane2` | Trailing scalar 2. Non-zero, large magnitude (e.g. in the thousands); candidate: the other plane coefficient or a plane-distance term | PARTIAL |
| +044   | 4    | f32le | `plane3` | Trailing scalar 3. `0.0` in every sampled quad | PARTIAL |

**Corner verification:** the four XZ corners of each sampled quad form a geometrically valid
rectangle whose min/max X and Z equal the owning `SolidRecord` AABB to within float rounding. This
was confirmed for all four quads of the 4-quad sample cell and all three quads of the 3-quad sample
cell — the corners are unambiguously world-space XZ positions, not line-equation parameters.

**Trailing scalars (+032..+047):** the alternating non-zero / zero pattern (`plane0`, `0`,
`plane2`, `0`) is consistent with a packed plane representation such as `(a, b, c, d)` for a
`a·x + b·z = d` style edge plane, or with a `(normal, distance)` pair, but the exact meaning is
not yet pinned to a runtime read. Treat +032..+047 as PARTIAL.

**Terminology note:** the game code calls these records "triangles" (count field "triCount"). The
on-disk record is a four-corner XZ quad; this spec uses "quad" to avoid confusion with 3D triangle
meshes. Whether the runtime tests one quad or two split triangles per record — and how the diagonal
is chosen — is not confirmed.

### 11.4 Known unknowns for `.sod`

- `SolidRecord` bytes +016..+059 (`_reserved_a`, 44 bytes): always zero in all samples; purpose
  unknown. Could be padding or additional spatial metadata (for example a Y / height range) that
  only appears in files not present in the sample set.
- `SolidRecord` byte +064 (`_authoring_ptr`): identified as a stale authoring-machine heap pointer
  (overwritten at load); it carries no portable file meaning, but its 4-byte slot must still be
  skipped by a parser.
- `QuadRecord` trailing scalars +032..+047 (`plane0..plane3`): the alternating non-zero / zero
  pattern suggests a packed plane equation or a normal-plus-distance pair, but no runtime read has
  been tied to a concrete interpretation. Needed to support exact (non-AABB) collision response.
- `QuadRecord` quad-to-triangle split: whether the runtime tests the four-corner quad directly or
  splits it into two triangles, and which diagonal it picks, is unknown.
- All available samples have `solidCount = 1`. Behaviour with multiple solids per cell is
  sample-unverified.

---

## 12. Cell streaming policy

### 12.1 Cell pool

The runtime maintains a fixed pool of **34 cell slots**. A cell slot is 24 712 bytes (0x6088).
The pool supports a maximum of 25 simultaneously active cells (5 × 5 ring), with the remaining
9 slots used as overflow or load buffers.

Each cell slot carries an **active flag** at byte offset 0x6084 within the slot:

| Value | Meaning |
|-------|---------|
| `1`   | Cell is loaded and active |
| `0`   | Slot is free or marked evictable |

### 12.2 Quality-dependent stream radii

The streaming ring radius is selected at startup based on a client quality setting:

| Quality level | Stream radius (world units) | Ring shape | Ring-radius cells |
|---------------|-----------------------------|-----------|--------------------|
| High (1)      | 1 800.0                     | 5 × 5     | 5                  |
| Medium (2)    | 1 000.0                     | 3 × 3     | 4                  |
| Low (other)   | 600.0                       | 3 × 3     | 4                  |

The ring shape (5 × 5 vs. 3 × 3) is determined at runtime by comparing the stored stream radius
to 1 000.0: if greater, the 5 × 5 ring dispatcher is used; otherwise the 3 × 3 dispatcher.

### 12.3 Eviction policy

When the player moves to a new centre cell, every loaded cell whose distance from the new centre
exceeds **2 cells** in either the X or Z axis is marked evictable (active flag set to 0). The
eviction threshold is strictly `> 2` (cells exactly 2 away are retained). Reuse of an evictable
slot is handled by the background streaming thread on its next scheduling cycle.

### 12.4 Per-frame update constraint

The per-frame ring-shift logic asserts that the player cannot move more than **one cell** in
either axis between consecutive frames. A delta greater than 1 triggers an assertion failure
(confirmed via source-filename string `terrainmanager.cpp`, line 443).

### 12.5 Background thread schedule

A single background worker thread handles asynchronous cell loads. Its schedule is:

| Wake event                  | Delay    |
|-----------------------------|----------|
| First wake after start      | 4 000 ms |
| Subsequent polling interval | 3 000 ms |

Load requests are enqueued as `{mapX, mapZ, areaId}` triples in a FIFO queue. A separate
synchronous load path exists for immediate-priority requests and is protected by a mutex shared
with the background thread.

---

## 13. Key cell object field layout (for completeness; not a binary file format)

This section documents select byte offsets within the in-memory cell object, included because
`Assets.Parsers` populates these fields after loading the binary assets. All offsets are byte
offsets from the start of the cell object allocation.

| Byte offset | Type | Field          | Notes                                                            | Confidence |
|------------:|-----:|----------------|------------------------------------------------------------------|------------|
| 0x3F4C      | i32  | mapX           | Grid X coordinate                                                | CONFIRMED  |
| 0x3F50      | i32  | mapZ           | Grid Z coordinate                                                | CONFIRMED  |
| 0x3F5C      | f32  | bboxXMin       | World bbox X min                                                 | CONFIRMED  |
| 0x3F60      | f32  | bboxZMin       | World bbox Z min                                                 | CONFIRMED  |
| 0x3F64      | f32  | bboxXMax       | World bbox X max                                                 | CONFIRMED  |
| 0x3F68      | f32  | bboxZMax       | World bbox Z max                                                 | CONFIRMED  |
| 0x3F70      | i32  | mapX (alias)   | Redundant copy                                                   | CONFIRMED  |
| 0x3F74      | i32  | mapZ (alias)   | Redundant copy                                                   | CONFIRMED  |
| 0x3F78      | i32  | areaId         | Area identifier                                                  | CONFIRMED  |
| 0x3F80      | ptr  | tileTerrain    | Pointer to primary TileTerrain sub-object (0x47E10 bytes)        | CONFIRMED  |
| 0x3F84      | ptr  | massTerrain    | Pointer to MassTerrain sub-object (0x1214 bytes)                 | CONFIRMED  |
| 0x3F88      | ptr  | extraLayer     | Pointer to extra layer sub-object (0x1094 bytes)                 | CONFIRMED  |
| 0x3F8C–0x3FA8 | ptr[] | fx1…fx7   | Pointers to FX1–FX7 sub-objects (each 0x1094 bytes)             | CONFIRMED  |
| 0x6084      | u8   | activeFlag     | 1 = loaded, 0 = free/evictable                                   | CONFIRMED  |

---

## 14. Known unknowns (priority list for further analysis)

The following items remain unverified and represent the highest-risk unknowns for implementors:

1. **EXTRA_TERRAIN DATAFILE extension:** Does `EXTRA_TERRAIN` target `.ted` blobs (as the parser
   suggests by routing to the same loader) or `.exd` blobs (as sample file naming implies)?
   Needs a `.map` file containing an `EXTRA_TERRAIN` section.

2. **`.ted` block 3 reference target:** RESOLVED — the 1-based texture index resolves first into
   the per-cell `TEXTURES{}` list, then into the global `bgtexture.lst` pool (§5.6). Remaining
   sub-question (PARTIAL): the fallback meaning of a value-0 byte, never seen in a sample.

3. **`.ted` block 4 bit-to-geometry mapping:** RESOLVED for texture orientation — bit `0x01`
   mirrors S (U), bit `0x02` mirrors T (V) (§5.7). Remaining sub-question (PARTIAL): whether a flip
   combination also re-selects the quad triangulation diagonal versus only re-orienting the texture.

4. **`.ted` block 5 alpha and 2× encoding:** Alpha byte RESOLVED — it is 4-byte alignment padding,
   always 0, never read (§5.8). The 2× store / 0.5× load scaling is code-confirmed; it stays
   sample-unverified because all samples are uniform white. A tinted tile would close the loop.

5. **`.bud` vertex bytes 12–31:** The five f32le values per vertex beyond XYZ are unconfirmed.

6. **`.up`/`.exd` `extra` field (byte 36):** Always equals the common Y for flat triangles.
   Behaviour for non-planar geometry is unknown.

7. **`.sod` `SolidRecord` bytes +016..+059:** Always zero in single-solid samples; purpose may
   become apparent in files with `solidCount > 1`.

8. **`.sod` `QuadRecord` trailing scalars (+032..+047):** RESOLVED that the first 32 bytes are
   four XZ corners (§11.3); the four trailing scalars (`plane0..plane3`) still need a runtime read
   tied to them before exact (non-AABB) collision can be reproduced.

9. **`.gad` format:** Loader is a no-op stub; purpose and format entirely unknown.

10. **`.bgtexture.lst` GHTex record layout (76 bytes):** Only the stride is confirmed.

11. **`.mud` `pad0`/`pad1` purpose:** Always zero with no observed read in available samples.

---

## 15. Cross-references

- **Related formats:** `Docs/RE/formats/pak.md` (VFS container that delivers all per-cell files),
  `Docs/RE/formats/mesh.md` (building mesh blobs referenced by BUILDING sections)
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
