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

## Re-verification banner (2026-06-16, CAMPAIGN 10 / Block D)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `sample-verified` for the `.map`+`.bud` scene model. This pass re-confirmed the **scene/routing** layer (BUILDING section → `.bud` loader; SOLID section → `.sod` loader; `.bud`↔`.sod` independence; the BUILDING `TEXTURES` pool and 1-based `tex_id`) two-witness. The `.bud` byte-internal tables (9-byte object header, 32-byte FVF-0x112 vertex, warn-and-continue 0xC00 cap) were **not** re-dumped this pass and hold at their committed (sample-verified) tier. |
| `ida_reverified` | `2026-06-16` |
| `ida_anchor`     | `263bd994` |
| `evidence`       | `[static-ida, vfs-sample]` — `.map` parser dispatch (witness 1) + prior `.bud` sample verification (committed tier) |
| `conflicts`      | None. RE-confirmed with no drift: the BUILDING section is reached only via the `.map` `BUILDING` block and loads `.bud`; the `BUILDING TEXTURES` pool registers in slot order (same discard-first-int / read-`intTexId` logic as `TERRAIN`), and `tex_id` is a 1-based index into it; `.bud` and `.sod` are independent blobs with no in-file cross-reference (SOLID → `.sod`). The geometry directives `WIDTH`/`HEIGHT`/`GRID`/`MAX_HEIGHTFILED`/`MIN_HEIGHTFILED`/`ORIGIN` are present on disk but **not consumed by the located runtime `.map` parser** (see `terrain.md §3.4`). |

> **Building-mesh lane flag (deferred):** the `.bud` 32-byte vertex / 9-byte object header / 0xC00
> warn-and-continue cap were carried forward at their committed tier this pass; a dedicated
> building-mesh re-verification against the `.bud` loader and a multi-object sample is the place to
> re-open those tables, not this terrain-core pass.

---

## Status block

| Attribute          | Value |
|--------------------|-------|
| `status`           | `sample-verified` — `.bud` fields cross-checked against three real `.bud` files (195 bytes each, byte-identical content) and a 17-object `.bud` file (420,965 bytes; all objects parse to exact size), and against draw-call configuration recovered from the legacy client. |
| `sample_verified`  | `true` — `.bud` header, object header, vertex format (position + unit normal + single UV pair), index format, and file-size formula all reproduce the sample data exactly. |
| `binary_analysed`  | `doida.exe` (legacy 32-bit client build, x86 little-endian). |
| `confidence`       | Per-field confidence is given in each table. `CONFIRMED` = corroborated by both parser read-sequence and real sample bytes (and, where noted, by the recovered draw-call vertex declaration / D3D FVF). `CONFIRMED-variable` = the field is read and retained, its value is confirmed to vary across samples, but the consumer takes no branch on it (value confirmed; semantics open). `loader-resolved` = a behaviour the loader's read-sequence settles (e.g. how a guard reacts) even where a sample alone is silent. `VERIFIED` = confirmed directly by observed sample bytes. `OBSERVED` = a single value seen in every sample but its full range/meaning is not established. `PARTIAL` = existence/size confirmed, semantics uncertain. `UNVERIFIED` = inferred from call-site context only. |

### Per-field confidence summary (changes since the previous revision)

| Item | Previous | Now | Basis for the change |
|------|----------|-----|----------------------|
| Per-vertex bytes `+0x0C..+0x17` are a unit normal (3 × f32) | CONFIRMED | **CONFIRMED** (reinforced) | The static-building draw pass configures D3D FVF = 0x112 (D3DFVF_XYZ \| D3DFVF_NORMAL \| D3DFVF_TEX1); all 9,049 sample normals have magnitude 1.0 ± 1e-3. |
| Per-vertex bytes `+0x18..+0x1F` are a UV pair (2 × f32) | CONFIRMED | **CONFIRMED** (reinforced) | FVF = 0x112 includes exactly one 2-float texture-coordinate set; two trailing floats match. |
| UV value range | PARTIAL (tiled ~24–29 only) | **CONFIRMED dual-convention** | 17-object sample shows both tiled world-scale (range roughly −5 to +10) and atlas-normalized ([0,1]) UVs across different objects in the same file; both are valid for the same single UV channel. |
| No lightmap UV (UV2) | UNVERIFIED | **CONFIRMED absent** | D3DFVF_TEX2 (0x200) is NOT set in FVF = 0x112; only one UV set exists. |
| No per-vertex color | UNVERIFIED | **CONFIRMED absent** | D3DFVF_DIFFUSE (0x040) and D3DFVF_SPECULAR (0x080) are NOT set in FVF = 0x112. |
| `tex_id` base | PARTIAL (assumed 1-based) | **CONFIRMED 1-based** | A runtime validation rejects `tex_id < 1` as an error and clamps the offending value to 1 (not 0); the smallest legal value is 1. |
| Index winding order | not stated | **CONFIRMED counter-clockwise** | Per-triangle face normals computed under a CCW convention agree (positive dot product) with the stored per-vertex normals for all sampled triangles. |
| `type_byte` value range | PARTIAL / OBSERVED (value 0 only) | **CONFIRMED-variable** (values {0, 1, 2}; consumer does not branch) | Two-witness review (loader read-sequence + black-box sampling) shows the byte takes values 0, 1 and 2 across the asset set. It is read and retained, but no read-site branches on it; the static-vs-sway behaviour is driven by a *separate* per-texture classification, not by this byte. Value is now confirmed-variable; its semantic role remains open. |
| `vertex_count` cap behaviour | warn (unspecified action) | **loader-resolved: warn-and-continue on full count** | The cap guard is log-only and runs *after* the full-count allocation and read; the loader never throws, clamps, or truncates at the cap. A faithful parser must read all `vertex_count` vertices regardless of the cap (see §3.2.1, §9, §10). |
| `light*.bin` files and building lightmaps | unaddressed | **CONFIRMED: light*.bin are NOT per-building lightmaps** | These files are per-map sky/directional light keyframe tables applied globally to the whole scene. No per-cell or per-building lightmap texture exists in the legacy asset set. |

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
| `+0x00` | 4 | u32 | `object_count` | Number of object records that follow. Observed values: 1 (three 195-byte samples), 17 (420,965-byte sample). May be 0 (empty cell, not sampled). No object-count cap is applied by the loader. | CONFIRMED |

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
| `+0x00` | 1 | u8  | `type_byte`    | Object sub-class tag. Confirmed-variable: values 0, 1 and 2 occur across the asset set. The byte is read from the file and retained in memory, but no read-site branches on it in the building render path — the consumer takes no decision from its value (see §5 and open questions). Expose it as a raw `byte`. | CONFIRMED-variable |
| `+0x01` | 4 | u32 | `tex_id`       | **1-based** index into the `BUILDING` `TEXTURES` pool declared in the cell's `.map`. Smallest legal value is 1. A value of 0, or a value greater than the pool size, is treated by the legacy client as an error and clamped to 1. Observed value: 1. | CONFIRMED |
| `+0x05` | 4 | u32 | `vertex_count` | Number of vertices in this record's vertex array. The legacy loader applies a **log-only, warn-and-continue** cap check at **3072 (0xC00)**: it allocates and reads the *full* `vertex_count` first, then merely logs a warning if the count exceeded the cap — it never throws, clamps, or truncates. A faithful parser must read all `vertex_count` vertices regardless of the cap (see §9 and §10). Observed values: 5 (small samples); up to 2017 (17-object sample). **VFS census (2026-06-16, two-witness over all 2 296 `.bud` files):** the file-size formula reproduces with zero residual on every file; **exactly 4 files breach the 3 072 cap** (maps 17/18/27/33 at cell `x10034z10038`, max `vertex_count = 3328`) — confirming the warn-and-continue path is exercised by real data and that a clamping parser would desync the stream on those 4 files. | CONFIRMED (cap behaviour: loader-resolved; census-verified) |

#### 3.2.2 Vertex array

Begins at `+0x09` within the record and contains exactly `vertex_count` fixed-stride vertex
records of **32 bytes** each (no per-file stride field). The layout is determined by the D3D
fixed-function vertex format (FVF) value **0x112** used by the static-building draw pass, which
decodes as D3DFVF_XYZ (3-float position) | D3DFVF_NORMAL (3-float normal) | D3DFVF_TEX1
(one 2-float texture-coordinate set) = 12 + 12 + 8 = 32 bytes per vertex. All eight components
are `f32` little-endian.

| Byte within vertex | Size | Type | Field      | Notes | Confidence |
|-------------------:|-----:|------|------------|-------|-----------|
| `+0x00` | 4 | f32 | `pos_x`    | World-space X position. | CONFIRMED |
| `+0x04` | 4 | f32 | `pos_y`    | World-space Y (vertical / up axis; height). | CONFIRMED |
| `+0x08` | 4 | f32 | `pos_z`    | World-space Z position. | CONFIRMED |
| `+0x0C` | 4 | f32 | `normal_x` | Surface normal X component. | CONFIRMED |
| `+0x10` | 4 | f32 | `normal_y` | Surface normal Y component. | CONFIRMED |
| `+0x14` | 4 | f32 | `normal_z` | Surface normal Z component. | CONFIRMED |
| `+0x18` | 4 | f32 | `uv_u`     | Texture U coordinate. May be tiled or atlas-normalized — see UV conventions below. | CONFIRMED |
| `+0x1C` | 4 | f32 | `uv_v`     | Texture V coordinate. May be tiled or atlas-normalized — see UV conventions below. | CONFIRMED |

**Evidence for the vertex layout — three independent lines:**

- **D3D FVF = 0x112:** The static-building draw pass configures the D3D device with FVF value
  0x112. Breaking this down: D3DFVF_XYZ (0x002) + D3DFVF_NORMAL (0x010) + D3DFVF_TEX1 (0x100)
  = 0x112. The D3D driver stride for this FVF is exactly 32 bytes, matching the file stride.
  Critically: D3DFVF_DIFFUSE (0x040) is NOT set — no per-vertex color. D3DFVF_SPECULAR (0x080)
  is NOT set. D3DFVF_TEX2 (0x200) is NOT set — no second UV set, no lightmap UV channel.
- **Normal unit-check:** All 9,049 vertices across a 17-object real sample have normal magnitude
  1.0 ± 1e-3. The per-vertex `(normal_x, normal_y, normal_z)` triplet is unambiguously a unit
  surface normal.
- **AABB computation:** The post-load bounding-box routine reads only the first 12 bytes of each
  vertex (the position) and advances by 32 bytes per vertex. It does not read bytes `+0x0C..+0x1F`
  as positional data, confirming those bytes are not further position components (tangents,
  a second position, etc.).

**Conclusions confirmed by the above evidence:**

- There is **no lightmap UV (UV2) channel** in `.bud` geometry. FVF D3DFVF_TEX2 is absent;
  the file contains exactly one UV pair per vertex.
- There is **no per-vertex color** in `.bud` geometry. FVF D3DFVF_DIFFUSE and D3DFVF_SPECULAR
  are absent; the 32 bytes are fully accounted for by position + normal + UV.
- There are **no tangent or bitangent vectors**. The FVF has no tangent bit; all 32 bytes are
  accounted for.

**UV conventions — dual mode (SAMPLE-VERIFIED):**

Two distinct UV conventions coexist across objects within the same `.bud` file, both encoding
valid texture coordinates for the single UV channel:

| Mode | UV value range | Observed in | Meaning |
|------|---------------|-------------|---------|
| Tiled world-scale | Approximately −5 to +10 (typically positive; integer part = tile count) | Large ground-cover geometry, vegetation patches, and building surfaces that use repeating textures | The UV maps world units to texture repetitions; the sampler uses wrap mode. The specific world-unit-to-UV ratio is not a fixed constant — it varies by object. |
| Atlas-normalized | Strictly in [0, 1] (approximately 0.01–0.99 in practice) | Uniquely-mapped building parts, atlas-packed geometry | Each texel has a unique UV address; the full texture image maps once onto the surface. |

Both conventions use the same single UV channel (`uv_u`, `uv_v`). The choice between tiled
and atlas-normalized is a content decision made per object at export time; there is no flag
in the `.bud` record that indicates which convention applies. A parser cannot determine
convention from the file alone — it follows from the texture atlas type referenced by `tex_id`.

No compact (8-bit / 16-bit) normal or UV encoding was observed; all components are full `f32`.

#### 3.2.3 Index header (4 bytes)

Immediately follows the vertex array.

| Offset (relative to end of vertex array) | Size | Type | Field | Notes | Confidence |
|----------------------------------------:|-----:|------|-------|-------|-----------|
| `+0x00` | 4 | u32 | `index_count` | Number of u16 indices that follow. A multiple of 3 (triangle list). Observed value: 9 (= 3 triangles) in small samples. | CONFIRMED |

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
`4 + 9 + 160 + 4 + 18 = 195` bytes, matching every small sample exactly with no trailing data.

For 17 objects (vertex counts 1207, 738, 98, 128, 1105, 582, 2017, 128×5, 1308, 1229, 16, 122,
1347) the formula yields 420,965 bytes — an exact match for the real-VFS sample, confirming the
formula holds for multi-object files and that there is no inter-object padding.

---

## 5. `type_byte` value range and the static-vs-animated distinction

| Value | Meaning | Confidence |
|------:|---------|-----------|
| 0 | Observed (most common). Default static building / prop. | CONFIRMED-variable |
| 1 | Observed in the asset set. Meaning unknown; no consumer branch. | CONFIRMED-variable |
| 2 | Observed in the asset set. Meaning unknown; no consumer branch. | CONFIRMED-variable |
| 3–255 | Not seen in samples; meaning unknown. | UNVERIFIED |

The byte is **confirmed-variable**: across the asset set it takes the values 0, 1 and 2. Even so,
no consumer branches on it — the loader reads and retains the byte but takes no decision from its
value. In particular, the file's `type_byte` does **not** select the static-vs-sway render
behaviour. That selection is driven at runtime by a **separate per-texture classification**
computed from the `BUILDING` `TEXTURES` pool entry referenced by `tex_id`, not by this byte. In
other words, whether an object is drawn as static geometry or animated with a wind-sway effect is
keyed off the texture binding, not off `type_byte`.

Observed behaviour of that per-texture classification (provided for context only — it is
runtime state, not a field in the `.bud` file):

- A "static" class copies vertices straight to the render buffer with no animation.
- One sway class (lighter vegetation, e.g. trees / shrubs) applies a wind-sway deformation;
  meshes with a small fixed vertex count use a fully unrolled path, larger ones a simplified
  path.
- A second sway class (larger vegetation) drives sway amplitude from the object's XZ
  bounding-box size.
- A shadow pass uses the same per-texture classification to decide which objects cast shadows.

The per-texture classification is controlled by the `kind` byte in `bgtexture.lst` — see
`formats/texture.md §bgtexture.lst`.

Because no read-site that branches on `type_byte` was found in the building render path, its
value is now confirmed to vary but its *semantic role* remains open. The leading hypotheses are:
a render-sort / blend-mode tag, editor-time metadata not consumed at runtime, or a vestigial
field superseded by the per-texture classification but retained for file compatibility.
Implementors should expose `type_byte` as a raw `byte` (not a typed enum) and must not branch
on it in the render path, mirroring the legacy consumer.

---

## 6. Texture binding (`tex_id` → `BUILDING TEXTURES` pool)

- `tex_id` is a **1-based** index into the ordered texture pool declared by the `BUILDING`
  `TEXTURES { … }` stanza of the cell's `.map`. The first pool entry is `tex_id = 1`.
- The legacy client validates `tex_id` after loading all objects: if `tex_id < 1` or
  `tex_id > pool_size`, it logs an error and clamps the value to 1. Because the guard rejects
  0, a 0-based interpretation is impossible — `tex_id = 0` is an error value, not "the first
  texture" and not "no texture".
- Pool entries are themselves 1-based indices into the global `bgtexture.lst` catalogue
  (`intTexId` fields). To resolve a texture: `tex_id` → pool entry `intTexId` → `bgtexture.lst`
  record at index `(intTexId − 1)` → `kind` byte and `path_stem` → full DDS path. The complete
  resolution chain is documented in `formats/texture.md §bgtexture.lst`.
- A `.bud` consumer only needs to treat `tex_id` as a 1-based offset into the per-cell
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

## 8. Lighting model for `.bud` geometry — no per-building lightmaps

**Status: CONFIRMED**

Buildings rendered from `.bud` geometry receive lighting entirely through the legacy client's
D3D fixed-function pipeline driven by global per-map light data. There are no per-cell or
per-building lightmap textures.

The key evidence:
- The D3D FVF for the building draw pass (0x112) includes no per-vertex color or second UV set;
  there is no channel in the vertex data that could carry baked lightmap UVs.
- The `light*.bin` files in `data/sky/dat/` and `data/map*/` are per-map sky/directional-light
  **keyframe tables**, not lightmap textures. Each file encodes a sequence of timed keyframes;
  each keyframe describes global directional light color, ambient color, sun direction, and
  related atmosphere parameters applied to the entire scene at once.
- The BMP files at `data/effect/map/d%sx%dz%d.bmp` are terrain lightmap tiles applied to the
  ground surface (`.ted` terrain), not to building geometry.

For the full format of the `light*.bin` environment keyframe files, see
`Docs/RE/formats/environment_bins.md` (authored in parallel; may not yet be committed).

Implementors building a Godot renderer for `.bud` geometry should apply scene-wide directional
and ambient lighting rather than attempting to source per-object lightmap data from the file.
If baked lightmaps are desired, they must be generated at import time — they do not exist
in the legacy asset set.

---

## 9. Implementor checklist (Assets.Parsers)

1. Read `object_count` (u32) at `+0x00`. May be 0.
2. For each object, read the 9-byte header: `type_byte` (u8), `tex_id` (u32), `vertex_count`
   (u32). The `vertex_count` cap (3072 / 0xC00) is **warn-only**: read the full `vertex_count`
   regardless — never throw, clamp, or truncate at the cap (4 real files breach it, max 3328; a
   clamp/skip desyncs the stream on those files). Optionally emit a log warning to mirror the legacy
   loader, then continue.
3. Read `vertex_count` × 32-byte vertices. Each vertex is 8 × f32: position(3), normal(3),
   uv(2). Normals are unit-length (magnitude 1.0 ± 1e-3). UVs may be tiled world-scale
   (values outside [0,1] are normal) or atlas-normalized ([0,1]); both are valid and the same
   sampler can handle both with wrap mode enabled.
4. Read `index_count` (u32), then `index_count` × u16 indices. Triangle list, **CCW** front
   faces, 0-based, range `[0, vertex_count − 1]`.
5. Validate `1 ≤ tex_id ≤ BUILDING-pool-size` (1-based). Decide whether to clamp-to-1
   (legacy behaviour) or reject.
6. Expose `type_byte` as a raw `byte` (it varies over {0, 1, 2}); do not branch on it in the
   render path — the legacy consumer does not.
7. Cross-check total bytes consumed against the §4 formula; there is no trailing data and no
   inter-object padding.
8. Sanity-check vertex positions against the cell world AABB derived from `(mapX, mapZ)`.
9. Do NOT attempt to source a lightmap UV from bytes `+0x18..+0x1F` — there is only one UV
   set per vertex. Lightmap UVs do not exist in `.bud` geometry.

---

## 10. Open questions

1. **`type_byte` semantics and read-site.** The byte is confirmed to vary over {0, 1, 2}, but no
   branch on it was found in the building render path, so its meaning is unresolved. Other values
   may exist for other building types or render/sort modes, or the byte may be editor metadata /
   vestigial. Needs a confirmed read-site (or a documented use) to assign meaning. Its *value*
   being variable is settled; its *role* is the open part.
2. **`tex_id = 0` and out-of-range handling.** No sample with `tex_id = 0` exists; the runtime
   guard proves it is an error (clamped to 1), but real data with `tex_id = 0` has not been
   seen.
3. **`vertex_count` cap rationale.** The 3072 (0xC00) cap is warn-only and real files exceed it;
   why the legacy code logs at that threshold (a heuristic budget vs. a hard buffer limit elsewhere)
   is unresolved. The *parser behaviour* is settled (read full count, never truncate); only the
   intent of the threshold is open.
4. **Multi-object `.bud` files — record boundary alignment.** Multi-object files are confirmed
   by sample (17 objects). For objects where `vertex_count` is odd, the index array starts on
   an odd byte offset (since the vertex array is `32 × vertex_count` bytes; 32 is always even,
   so this cannot produce an odd boundary). No alignment padding has been observed across any
   sample; assume none.
5. **Empty cell (`object_count = 0`).** Unverified; the loader is expected to handle it by
   producing an empty object set.
6. **UV convention selection.** Both tiled world-scale and atlas-normalized UVs are confirmed
   present in the real data. The choice is per-object and follows from the content. Whether any
   fixed UV = world_position / tile_size relationship holds for tiled objects is unconfirmed.
7. **`BUILDING TEXTURES` integer flag.** The first integer on each `TEXTURES` line is read but
   its meaning is unknown. It is not the `bgtexture.lst` record index.
8. **No name/string fields in `.bud`.** Object naming, if any, is carried by the `.map` text,
   not the `.bud` binary.

---

## 11. Cross-references

- `Docs/RE/formats/terrain.md` — streaming-cell overview, the `.map` grammar/section table, and
  the authoritative byte-level `.sod` layout (`§11`).
- `Docs/RE/formats/terrain_layers.md` — per-cell overlay/lighting sidecars (`.up`, `.exd`,
  `.fx1`–`.fx7`, wind/light blobs).
- `Docs/RE/formats/texture.md` — texture/atlas formats the `BUILDING` texture pool resolves
  into, and the `bgtexture.lst` catalogue (`§bgtexture.lst`).
- `Docs/RE/formats/environment_bins.md` — format of the `light*.bin` per-map sky/light keyframe
  tables (authored in parallel; cross-reference by path; may not yet be committed at time of
  writing).
- `Docs/RE/formats/pak.md` — archive container that holds these per-cell files.
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`. This revision applies the CAMPAIGN VFS-MASTERY
  two-witness gate (loader read-sequence + black-box sampling): the `vertex_count` cap is
  warn-and-continue on the full count (loader-resolved), and `type_byte` is confirmed-variable
  over {0, 1, 2} with no consumer branch.
```