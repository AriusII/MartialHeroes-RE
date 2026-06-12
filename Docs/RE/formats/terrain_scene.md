# Format: terrain scene — per-cell static-object placement (`.map` scene descriptor + `.bud` building geometry, with `.bud`/`.sod` relationship)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by Assets.Parsers. Every offset an engineer cites must
> reference this file.
>
> Scope note: this document is the scene-level companion to `terrain.md`. It describes how
> static objects are *placed* in a cell — the `.map` text scene descriptor, the `.bud`
> building-geometry blob (vertex format, texture binding, object typing), and the
> relationship between `.bud` and the `.sod` collision blob. The full byte-level `.sod`
> layout (`SolidRecord`/`SegmentRecord`/`CollisionQuad` tables) lives in `terrain.md §11`
> and is NOT duplicated here; this file only describes how `.sod` relates to `.bud` at the
> placement level. The broader streaming-cell overview (`.lst` / `.mud` / `.ted` / `.up` /
> `.exd` / `.fx1`–`.fx7`) also lives in `terrain.md`.

---

## Status block

| Attribute          | Value |
|--------------------|-------|
| `status`           | `sample-verified` — `.bud` fields cross-checked against three real `.bud` files (195 bytes each, byte-identical content) and against draw-call configuration recovered from the legacy client. |
| `sample_verified`  | `true` — `.bud` header, object header, vertex format, index format, and file-size formula all reproduce the 195-byte samples exactly. |
| `binary_analysed`  | `doida.exe` (legacy 32-bit client build, x86 little-endian). |
| `confidence`       | Per-field confidence is given in each table. `CONFIRMED` = corroborated by both parser read-sequence and real sample bytes (and, where noted, by the recovered draw-call vertex declaration). `VERIFIED` = confirmed directly by observed sample bytes. `OBSERVED` = a single value seen in every sample but its full range/meaning is not established. `PARTIAL` = existence/size confirmed, semantics uncertain. `UNVERIFIED` = inferred from call-site context only. |

### Per-field confidence summary (changes since the previous revision)

| Item | Previous | Now | Basis for the change |
|------|----------|-----|----------------------|
| Per-vertex bytes `+0x0C..+0x17` are a unit normal (3 × f32) | CONFIRMED | **CONFIRMED** (reinforced) | The static-building draw pass configures a fixed-function vertex declaration that includes a 3-float normal; all sample normals have magnitude 1.0 to within 1e-7. |
| Per-vertex bytes `+0x18..+0x1F` are a UV pair (2 × f32) | CONFIRMED | **CONFIRMED** (reinforced) | The same vertex declaration includes a single 2-float texture-coordinate set; the two trailing floats match. |
| `tex_id` base | PARTIAL (assumed 1-based) | **CONFIRMED 1-based** | A runtime validation rejects `tex_id < 1` as an error and clamps the offending value to 1 (not 0); the smallest legal value is 1. |
| Index winding order | not stated | **CONFIRMED counter-clockwise** | Per-triangle face normals computed under a CCW convention agree (positive dot product) with the stored per-vertex normals for all sampled triangles. |
| `type_byte` enumeration | PARTIAL | **OBSERVED (value 0 only); meaning LOW confidence** | The byte is read from the file and retained in memory, but no read-site that branches on it was found in the building render path; the visible static-vs-sway behaviour is driven by a *separate* per-texture classification, not by this byte. |

---

## 1. Overview

A **cell** is a 1024 × 1024 world-unit tile of the streaming world (see `terrain.md §1` for
the cell grid and `(mapX, mapZ)` addressing). Each cell's static dressing — buildings, props,
and their collision footprints — is described by a small group of files that share a common
base path:

| File | Role | Detailed byte layout |
|------|------|----------------------|
| `.map` | Plain-text scene descriptor. Names the binary blobs for this cell and binds texture pools to them. | This file (§2) and `terrain.md §4`. |
| `.bud` | Binary building / mass-object geometry (renderable triangle meshes). | This file (§3–§6). |
| `.sod` | Binary solid-collision geometry for the same cell volume. | `terrain.md §11`; relationship only in §7 here. |

The `.map` text file is the entry point: a section parser reads it, and when it encounters a
`BUILDING { DATAFILE … }` stanza it loads the named `.bud`; when it encounters a
`SOLID { DATAFILE … }` stanza it loads the named `.sod`. The two blobs are independent and
have no in-file cross-reference to one another (see §7).

All multi-byte fields in `.bud` are **little-endian**. Korean path/comment text that may appear
in a `.map` file is **CP949 (EUC-KR)** encoded.

---

## 2. The `.map` scene descriptor (placement context for `.bud`)

The `.map` file is plain ASCII with `#`-style line comments and whitespace-separated tokens.
It groups assets into named sections; the building loader is reached only through the
`BUILDING` section:

```
BUILDING
{
    DATAFILE  <path/to/cell.bud>
    TEXTURES
    {
        <int_flag>  <tex_pool_index>  "<original/artist/texture/path>"
        ...
    }
}
```

Key facts for an implementor of the `.bud` consumer:

- **`DATAFILE`** gives the logical path of the `.bud` blob to load for this cell.
- **`TEXTURES`** declares, in order, the texture pool that `.bud` records index into. Each entry
  has an integer flag (purpose unknown — see open questions), an integer texture-pool index that
  the engine registers, and a quoted original artist path. The quoted path is **metadata only**;
  the binary parser does not open it (textures are resolved through the packed VFS / texture
  atlas, not through this string). The string may contain CP949 Korean text.
- The `TEXTURES` list builds an ordered pool (the legacy client caps this pool at **128** entries
  for the building section). A `.bud` record's `tex_id` is a **1-based** index into this pool
  (see §3.2 and §6).
- Section keywords are order-independent. Other sections (`TERRAIN`, `EXTRA_TERRAIN`,
  `UP_TERRAIN`, `SOLID`, `FX1`–`FX7`) drive their own loaders and are documented in
  `terrain.md`. Only `BUILDING` reaches the `.bud` loader; only `SOLID` reaches the `.sod` loader.

For the complete `.map` grammar, the full section/loader table, and the text-only `TERRAIN`
metadata keywords (`WIDTH`, `HEIGHT`, `GRID`, `MAX_HEIGHTFILED`/`MIN_HEIGHTFILED`, `ORIGIN`),
see `terrain.md §4`. (`HEIGHTFILED` is an original spelling in the game data, not a transcription
error.)

---

## 3. `.bud` — building / mass-object geometry blob

### 3.0 Identification

- **Extension:** `.bud`
- **Found in:** `.pak` archive; logical path pattern `data/map<NNN>/dat/d<NNN>x<mapX>z<mapZ>.bud`
  (e.g. `d026x10023z10035.bud`), where `<NNN>` is the three-digit zero-padded area ID and
  `mapX`/`mapZ` are the cell grid indices.
- **Magic / signature:** none. The file begins directly with the 4-byte object count.
- **Version field:** none observed.
- **Endianness:** little-endian.
- **Role:** all static-object (building / prop) triangle geometry for one cell. Loaded as the
  `DATAFILE` of the `.map` `BUILDING` section. It is not the heightmap (`.ted`) or the
  collision blob (`.sod`); those are separate formats with separate loaders.

### 3.1 File-level header

| Offset | Size | Type   | Field          | Notes / observed values | Confidence |
|-------:|-----:|--------|----------------|-------------------------|-----------|
| `+0x00` | 4 | u32 | `object_count` | Number of object records that follow. Observed value: 1. May be 0 (empty cell, not sampled). No object-count cap is applied by the loader. | CONFIRMED |

The header is exactly 4 bytes — no magic, no padding, no additional metadata. Object records
follow immediately, back-to-back, with no inter-record padding.

### 3.2 Per-object record

Each record is variable-length and laid out as:

```
[ Object header — 9 bytes ]
[ Vertex array  — 32 × vertex_count bytes ]
[ Index header  — 4 bytes ]
[ Index array   — 2 × index_count bytes ]
```

#### 3.2.1 Object header (9 bytes)

| Offset within record | Size | Type | Field          | Notes / observed values | Confidence |
|---------------------:|-----:|------|----------------|-------------------------|-----------|
| `+0x00` | 1 | u8  | `type_byte`    | Object sub-class tag. Observed value: 0 in every sample. Read from the file and retained in memory, but no branch on it was found in the building render path; treat as opaque for now (see §5 and open questions). | OBSERVED |
| `+0x01` | 4 | u32 | `tex_id`       | **1-based** index into the `BUILDING` `TEXTURES` pool declared in the cell's `.map`. Smallest legal value is 1. A value of 0, or a value greater than the pool size, is treated by the legacy client as an error and clamped to 1. Observed value: 1. | CONFIRMED |
| `+0x05` | 4 | u32 | `vertex_count` | Number of vertices in this record's vertex array. The legacy loader warns (logs and continues) when this exceeds **3072 (0xC00)**. Observed value: 5. | CONFIRMED |

#### 3.2.2 Vertex array

Begins at `+0x09` within the record and contains exactly `vertex_count` fixed-stride vertex
records of **32 bytes** each (no per-file stride field). The layout corresponds to a
fixed-function vertex declaration combining a position, a normal, and one texture-coordinate
set (position 3 floats + normal 3 floats + one 2-float UV set = the 32-byte stride below). All
eight components are `f32` little-endian.

| Byte within vertex | Size | Type | Field      | Notes | Confidence |
|-------------------:|-----:|------|------------|-------|-----------|
| `+0x00` | 4 | f32 | `pos_x`    | World-space X position. | CONFIRMED |
| `+0x04` | 4 | f32 | `pos_y`    | World-space Y (vertical / up axis; height). | CONFIRMED |
| `+0x08` | 4 | f32 | `pos_z`    | World-space Z position. | CONFIRMED |
| `+0x0C` | 4 | f32 | `normal_x` | Surface normal X component. | CONFIRMED |
| `+0x10` | 4 | f32 | `normal_y` | Surface normal Y component. | CONFIRMED |
| `+0x14` | 4 | f32 | `normal_z` | Surface normal Z component. | CONFIRMED |
| `+0x18` | 4 | f32 | `uv_u`     | Texture U coordinate. | CONFIRMED |
| `+0x1C` | 4 | f32 | `uv_v`     | Texture V coordinate. | CONFIRMED |

Resolution of the five previously-unknown trailing floats (`+0x0C..+0x1F`):

- **`+0x0C..+0x17` is a unit surface normal** (`normal_x`, `normal_y`, `normal_z`). Two
  independent lines of evidence agree: (1) the static-building draw pass installs a
  fixed-function vertex declaration that includes a 3-float normal directly after the 3-float
  position; (2) every sampled vertex's `(normal_x, normal_y, normal_z)` has Euclidean magnitude
  1.0 to within 1e-7 — the signature of a unit normal. The position-only bounding-box pass over
  the vertex array reads only the first three floats and ignores `+0x0C..+0x1F`, consistent with
  those bytes being normal + UV rather than further positional data.
- **`+0x18..+0x1F` is a single UV texture-coordinate pair** (`uv_u`, `uv_v`). The same vertex
  declaration includes one 2-float texture-coordinate set immediately after the normal. In the
  samples these values lie in the approximate range 24–29; the integer part behaves as a tile
  count under wrap-mode sampling. They are tiled / world-scale coordinates, **not** normalised
  `[0, 1]` atlas coordinates, for the meshes sampled. Whether normalised UVs appear in other
  cells is unconfirmed (open question).

No compact (8-bit / 16-bit) normal or UV encoding was observed; all components are full `f32`.

#### 3.2.3 Index header (4 bytes)

Immediately follows the vertex array.

| Offset (relative to end of vertex array) | Size | Type | Field | Notes | Confidence |
|----------------------------------------:|-----:|------|-------|-------|-----------|
| `+0x00` | 4 | u32 | `index_count` | Number of u16 indices that follow. A multiple of 3 (triangle list). Observed value: 9 (= 3 triangles). | CONFIRMED |

#### 3.2.4 Index array

Immediately follows the index header. Contains exactly `index_count` values, each a **u16**
little-endian. Indices are 0-based; the legal range is `[0, vertex_count − 1]`.

- **Primitive topology:** triangle list. Every three consecutive indices form one triangle.
  No strip, fan, or adjacency structure is present.
- **Winding order:** **counter-clockwise (CCW)** for front faces. Face normals derived from
  each triangle under a CCW convention agree (positive dot product) with the averaged stored
  per-vertex normals for every sampled triangle. Under the legacy client's default culling
  (cull clockwise-facing, keep CCW-facing) these triangles are visible from the side their
  normals point toward. A renderer that culls back faces should treat CCW as front-facing for
  `.bud` geometry.
- **Index width:** u16 (2 bytes). No 32-bit index variant was observed.

---

## 4. File-size formula

```
total_bytes = 4                                  # file header: object_count
            + Σ over each object of:
                  9                              # object header: type_byte(1) + tex_id(4) + vertex_count(4)
                + 32 × vertex_count              # vertex array
                + 4                              # index_count
                + 2  × index_count               # index array
```

For one object with `vertex_count = 5` and `index_count = 9` this yields
`4 + 9 + 160 + 4 + 18 = 195` bytes, matching every sample exactly with no trailing data.

---

## 5. `type_byte` enumeration and the static-vs-animated distinction

| Value | Meaning | Confidence |
|------:|---------|-----------|
| 0 | Only value observed (all three samples). Default static building / prop. | OBSERVED |
| 1–255 | Not seen in samples; meaning unknown. | UNVERIFIED |

Important behavioural finding: the file's `type_byte` does **not** select the static-vs-sway
render behaviour. That selection is driven at runtime by a **separate per-texture
classification** computed from the `BUILDING` `TEXTURES` pool entry referenced by `tex_id`,
not by this byte. In other words, whether an object is drawn as static geometry or animated
with a wind-sway effect is keyed off the texture binding, not off `type_byte`.

Observed behaviour of that per-texture classification (provided for context only — it is
runtime state, not a field in the `.bud` file):

- A "static" class copies vertices straight to the render buffer with no animation.
- One sway class (lighter vegetation, e.g. trees / shrubs) applies a wind-sway deformation;
  meshes with a small fixed vertex count use a fully unrolled path, larger ones a simplified
  path.
- A second sway class (larger vegetation) drives sway amplitude from the object's XZ
  bounding-box size.
- A shadow pass uses the same per-texture classification to decide which objects cast shadows.

Because no read-site that branches on `type_byte` was found in the building render path, its
semantic role is **LOW confidence**. The leading hypotheses are: a render-sort / blend-mode tag
(value 0 = opaque static), editor-time metadata not consumed at runtime, or a vestigial field
superseded by the per-texture classification but retained for file compatibility. Implementors
should expose `type_byte` as a raw `byte` (not a typed enum) until a non-zero value and a
read-site are found.

---

## 6. Texture binding (`tex_id` → `BUILDING TEXTURES` pool)

- `tex_id` is a **1-based** index into the ordered texture pool declared by the `BUILDING`
  `TEXTURES { … }` stanza of the cell's `.map`. The first pool entry is `tex_id = 1`.
- The legacy client validates `tex_id` after loading all objects: if `tex_id < 1` or
  `tex_id > pool_size`, it logs an error and clamps the value to 1. Because the guard rejects
  0, a 0-based interpretation is impossible — `tex_id = 0` is an error value, not "the first
  texture" and not "no texture".
- Pool entries are themselves indices that resolve, downstream, into the global building-texture
  atlas. A `.bud` consumer only needs to treat `tex_id` as a 1-based offset into the per-cell
  `BUILDING` texture list it was given alongside the blob.

Implementor guidance: validate `1 ≤ tex_id ≤ pool_size`. A strict parser may reject out-of-range
values; a lenient parser may mirror the legacy clamp-to-1 behaviour. There is no "no texture"
sentinel in the observed data.

---

## 7. Object placement, instancing, and the `.bud` ↔ `.sod` relationship

### 7.1 Placement and instancing — geometry is pre-baked to world space

There is **no per-object instance transform** in a `.bud` file. Every vertex position is an
absolute world-space coordinate; the loader applies no matrix multiply at load time. Placement
is therefore encoded implicitly in the vertex positions themselves.

Consequences for an implementor:

- A `.bud` record is **not** a reusable model with a separate transform — it is a
  pre-transformed geometry patch for one specific cell. Do not expect a position/rotation/scale
  header.
- Vertex positions fall within (or, allowing floating-point edge cases, exactly on the boundary
  of) the cell's world AABB. The cell AABB is derived from `(mapX, mapZ)`:
  `worldX_min = (mapX − 10000) × 1024`, `worldZ_min = (mapZ − 10000) × 1024`, each extent
  spanning 1024 units (see `terrain.md §1`). This is a useful sanity check when validating a
  parsed `.bud`.
- The same cell coordinate can yield byte-identical `.bud` content across different area IDs
  (the three samples for cell `x10023z10035` under maps 026, 034, and 205 are identical),
  consistent with world-space coordinates baked at content-build time.

The runtime does compute, per object after load, an axis-aligned bounding box from the vertex
positions and derives a distance-squared visibility-culling threshold from the object's XZ
footprint. This is **runtime state, not stored in the file**; it is documented here only so
that implementors understand the intent of the `pos_*` fields:

| XZ footprint extent | Cull distance (units) | Distance² |
|---------------------|-----------------------|-----------|
| < 8  | 300   | 90 000    |
| < 16 | 500   | 250 000   |
| < 32 | 1 000 | 1 000 000 |
| < 64 | 1 500 | 2 250 000 |
| ≥ 64 | ~1 800 | 3 240 000 |

### 7.2 Relationship to `.sod` (collision)

The `.bud` (renderable building geometry) and `.sod` (solid-collision geometry) for a cell are
**independent** binary blobs referenced from the same `.map` text file through separate sections
(`BUILDING` → `.bud`, `SOLID` → `.sod`). They are loaded into separate runtime managers and are
post-initialised independently after all `.map` sections are parsed.

| Aspect | `.bud` | `.sod` |
|--------|--------|--------|
| Purpose | Renderable static geometry (triangles) | Collision footprints for the same world volume |
| Coordinate space | Absolute world-space (baked, no transform) | Absolute world-space (baked, no transform) |
| Dimensionality | Full 3D triangle meshes (XYZ + normal + UV) | Strictly 2D in the XZ plane; collision testing ignores Y except for a stored surface elevation |
| Cross-reference to the other | none | none |

Key points:

- There is **no field linking a `.bud` record to a `.sod` record.** The only relationship is
  **spatial**: both blobs use the same world-space coordinate system and the same cell AABB, so
  a building's renderable mesh and its collision footprint occupy overlapping world coordinates,
  but nothing in either file names the other.
- A cell may have a `.bud` and a `.sod`, only a `.sod` (terrain-only collision), or neither.
  In the available samples no `.bud` appeared without an accompanying `.sod`, but this is not
  guaranteed by the format and a parser must not assume the pairing.
- Consumers should treat the two as orthogonal inputs: load `.bud` for rendering, load `.sod`
  for collision, and correlate them only by cell coordinate and world position — never by an
  index or pointer in the data.

For the full byte-level `.sod` layout (`SolidRecord` 108-byte headers, the variable per-record
segment/quad arrays, and the per-quad fields including the still-unresolved trailing floats),
see `terrain.md §11`. That layout is authoritative and is not repeated here.

---

## 8. Implementor checklist (Assets.Parsers)

1. Read `object_count` (u32) at `+0x00`. May be 0.
2. For each object, read the 9-byte header: `type_byte` (u8), `tex_id` (u32), `vertex_count`
   (u32). Optionally warn when `vertex_count > 3072`.
3. Read `vertex_count` × 32-byte vertices. Each vertex is 8 × f32: position(3), normal(3),
   uv(2). Normals are unit-length; UVs are tiled world-scale floats (may exceed `[0, 1]`).
4. Read `index_count` (u32), then `index_count` × u16 indices. Triangle list, **CCW** front
   faces, 0-based, range `[0, vertex_count − 1]`.
5. Validate `1 ≤ tex_id ≤ BUILDING-pool-size` (1-based). Decide whether to clamp-to-1
   (legacy behaviour) or reject.
6. Expose `type_byte` as a raw `byte` until its enumeration is established.
7. Cross-check total bytes consumed against the §4 formula; there is no trailing data.
8. Sanity-check vertex positions against the cell world AABB derived from `(mapX, mapZ)`.

---

## 9. Open questions

1. **`type_byte` full enumeration and read-site.** Only value 0 observed; no branch on the
   byte found in the building render path. Other values may exist for other building types or
   render/sort modes, or the byte may be editor metadata / vestigial. Needs a `.bud` with a
   non-zero `type_byte` and/or a confirmed read-site.
2. **`tex_id = 0` and out-of-range handling.** No sample with `tex_id = 0` exists; the runtime
   guard proves it is an error (clamped to 1), but real data with `tex_id = 0` has not been
   seen.
3. **Multi-object `.bud` files.** All samples have `object_count = 1`. The loader applies no
   object-count cap, so multi-object files are structurally valid, but record-boundary
   alignment (especially for odd `vertex_count`, where the index array would start on an odd
   byte) has not been exercised. No alignment padding has been observed; assume none until a
   counter-example appears.
4. **Empty cell (`object_count = 0`).** Unverified; the loader is expected to handle it by
   producing an empty object set.
5. **UV normalisation.** Observed UVs are tiled world-scale (~24–29). Whether normalised
   `[0, 1]` UVs appear in other cells, and whether any fixed `UV = world_pos / tile_size`
   relationship holds, is unconfirmed.
6. **`BUILDING TEXTURES` integer flag.** The first integer on each `TEXTURES` line is read but
   its meaning is unknown.
7. **No name/string fields in `.bud`.** Object naming, if any, is carried by the `.map` text,
   not the `.bud` binary; an exhaustive parse of the 195-byte samples leaves no unaccounted
   bytes.

---

## 10. Cross-references

- `Docs/RE/formats/terrain.md` — streaming-cell overview, the `.map` grammar/section table, and
  the authoritative byte-level `.sod` layout (`§11`).
- `Docs/RE/formats/terrain_layers.md` — per-cell overlay/lighting sidecars (`.up`, `.exd`,
  `.fx1`–`.fx7`, wind/light blobs).
- `Docs/RE/formats/texture.md` — texture/atlas formats the `BUILDING` texture pool resolves into.
- `Docs/RE/formats/pak.md` — archive container that holds these per-cell files.
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
