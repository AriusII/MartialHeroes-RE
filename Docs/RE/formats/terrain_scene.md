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

## Re-verification banner (2026-06-27 — CYCLE 14 re-anchor, confirmatory)

| Attribute        | Value |
|------------------|-------|
| `ida_reverified` | `2026-06-27` |
| `ida_anchor`     | `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` |
| `verification`   | `confirmed` — CYCLE 14 re-anchor (f61f66a9): confirmatory — subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected. All prior verification findings from 2026-06-26 / 2026-06-24 / 2026-06-21 remain valid. |
| `evidence`       | `[static-ida]` |
| `conflicts`      | None. |

---

## Re-verification banner (2026-06-26 — render-class + world-placement confirmation)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `confirmed` — static-object render-class dispatch by `kind` range (four-way: static-copy / solid-shadow / sway-small-medium / sway-large) confirmed from draw-site analysis; material state for all static-object passes (alpha-blend on, alpha-test off) confirmed; absolute world-coordinate placement (no per-asset recenter, no per-object instance transform, D3DTS_WORLD never set) confirmed. §5 rewritten with confirmed four-way classification; §7.1 extended with port placement rule. |
| `ida_reverified` | `2026-06-26` |
| `ida_anchor`     | `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` |
| `evidence`       | `[static-ida]` — draw-site analysis confirming per-frame bucketing, material state, culling, and absolute world-coordinate placement |
| `conflicts`      | None. The `type_byte`-vs-`kind` independence (confirmed in prior revision) is reinforced: render class is driven by the texture `kind` byte, not by the per-object `type_byte`. |

---

## Re-verification banner (2026-06-24 — path-template confirmation, no layout corrections)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `sample-verified` — independent re-confirmation pass (witness 1 = `.bud` loader read-sequence; witness 2 = 420,965-byte 17-object real VFS sample + three 195-byte byte-identical single-object samples). **Zero contradictions** with the layout below. New finding incorporated: per-cell scene file path templates recovered from `.rdata` strings (§3.0 table). The map-level `.bin` family (`data/map%s/map%s.bin`) was also encountered but is **out of scope** for this document — see gap note in §11. |
| `ida_reverified` | `2026-06-24` |
| `ida_anchor`     | `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` |
| `evidence`       | `[static-ida, vfs-sample]` — witness 1 = `.bud` and `.sod` loader read-sequences (confirmed field-by-field); witness 2 = byte-exact real VFS samples (object_count, vertex/index blocks, file-size formula); witness 3 = `.rdata` path-template strings for per-cell file naming. |
| `conflicts`      | None. All layout fields confirmed correct. Per-cell path templates (§3.0) added from `.rdata` discovery; no existing field changed. |

---

## Re-verification banner (2026-06-21)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `sample-verified` for the `.map`+`.bud` scene model. This pass re-confirmed the format **end-to-end** — loader chain, on-disk layout, read algorithm, and linkages — against the `.bud` loader and a real 195-byte single-object sample, with the file-size formula reproducing to the byte and zero trailing data. No layout field flipped. The pass added refinements only: the precise cull-footprint metric (§7.1), the `TEXTURES` first-integer positivity gate (§2), the pool-cap drop vs. per-object clamp distinction (§6), the explicit read algorithm (§3.3), and the `.bud.pre` negative finding (§3.0). |
| `ida_reverified` | `2026-06-21` |
| `ida_anchor`     | `263bd994` |
| `evidence`       | `[static-ida, vfs-sample]` — `.bud` loader read-sequence (witness 1) + real `.bud` sample, byte-exact (witness 2) |
| `conflicts`      | None. RE-confirmed with no drift: the BUILDING section is reached only via the `.map` `BUILDING` block and loads `.bud`; the `BUILDING TEXTURES` pool registers via the discard-first-int / keep-second-int (`intTexId`) convention shared with `TERRAIN`, and `tex_id` is a 1-based index into it; `.bud` and `.sod` are independent blobs with no in-file cross-reference (SOLID → `.sod`). The geometry directives `WIDTH`/`HEIGHT`/`GRID`/`MAX_HEIGHTFILED`/`MIN_HEIGHTFILED`/`ORIGIN` are present on disk but **not consumed by the located runtime `.map` parser** (see `terrain.md §3.4`). The earlier "XZ footprint extent" wording for the cull metric is refined (not contradicted) to the 0.6-scaled XZ-diagonal vs. Y-extent maximum (§7.1). |

> **Cross-area identity (sample-verified):** the same cell coordinate yields byte-identical `.bud`
> content across different area IDs (cell `x10023z10035` is identical across maps 016/022/026/034/205),
> consistent with world-space geometry baked at content-build time (§7.1).

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

Each `DATAFILE` (the `.map` itself, the `.bud`, the `.sod`) is opened through a single
file-open router that resolves the name two ways: when the packed VFS is mounted it locates the
entry in the archive table of contents and reads it (see `formats/pak.md`); when running against
loose files on disk it opens the file directly. The geometry stream is then read sequentially,
field by field, with no decompression or decryption stage — see §3.3.

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
  is a line of two integers and a quoted original artist path:
  - **First integer — a positivity gate, not stored.** The parser reads it first; a value `≤ 0`
    is treated as the block terminator / skip predicate (the loop also stops on a closing `}`),
    so only lines whose first integer is `> 0` are taken as data lines. On a data line this first
    integer is then **discarded** (re-read but not retained). Its only observed effect is the
    `> 0` gate; see open questions.
  - **Second integer — the registered pool entry.** This is the value the engine actually
    registers as the pool entry; it is itself a 1-based index into the global `bgtexture.lst`
    catalogue (`intTexId`). This is the same "discard-first-int / keep-second-int" convention the
    `TERRAIN` section uses for its texture entries.
  - **Quoted path — metadata only.** The binary parser does not open it (textures are resolved
    through the packed VFS / texture atlas, not through this string). The string may contain
    CP949 Korean text.
- The `TEXTURES` list builds an ordered pool. The legacy client caps this pool at **128** entries
  for the building section: registering past the cap **drops** the over-cap entry (it does not
  clamp or overwrite) and logs a pool-overflow message. A `.bud` record's `tex_id` is a
  **1-based** index into this pool (see §3.2 and §6).
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
- **`.bud.pre` siblings (ignore at runtime):** the asset set contains `.bud.pre` files beside the
  `.bud` (same base name). A `.bud.pre` uses the same structure (same `object_count`,
  `vertex_count`, `index_count`) but holds different pre-bake source vertex data. The client
  **never opens `.bud.pre`** — it only ever loads the `.bud` named explicitly by the `.map`
  `DATAFILE`. `.bud.pre` is a content-build artifact, not a runtime format; a parser should
  ignore `.pre` siblings (this parallels the `.ted` / `.ted.post` pair).

**Per-cell scene file path templates (recovered from client path-template strings, CONFIRMED):**

The client constructs per-cell file paths from a cell base of the form `d<area>x<mapX>z<mapZ>` where
`<area>` is the zero-padded area ID, `<mapX>` and `<mapZ>` are the cell grid indices.
Note that the `.bud` path is **not** one of these templates — it is never a stored literal in the
client; it is always named by the `.map` `BUILDING { DATAFILE … }` stanza at runtime.

| Path template (printf-style) | Produces |
|------------------------------|----------|
| `/dat/d%sx%dz%d.sod` | Per-cell solid-collision blob |
| `/dat/d%sx%dz%d.ted` | Per-cell heightmap blob |
| `/dat/d%sx%dz%d.ted.post` | Post-bake heightmap sibling (content-build artifact; not loaded at runtime) |
| `/dat/d%s.lst` | Per-cell object/scene list |

The `.bud` name comes only from the `.map` `DATAFILE` stanza (no client-side template for it). The
area component in the path base is formatted with `%s` (string), accepting the three-character zero-padded
area string (e.g. `000`, `001`) passed in by the cell streamer, not an integer printf directly.

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

### 3.3 Read algorithm (bytes → runtime)

The loader reads the stream strictly sequentially in the on-disk order, with no transform and no
decode:

1. Open the `.bud` named by the `BUILDING { DATAFILE … }` stanza through the file-open router
   (VFS entry read when mounted, direct file read when loose — see §1).
2. Read `object_count` (u32). Allocate an array of that many object records (no count cap).
3. For each object, in order: read `type_byte` (u8), `tex_id` (u32), `vertex_count` (u32);
   allocate `32 × vertex_count` bytes and **blit the whole vertex block in one read** (the
   per-vertex layout is a raw copy — there is no per-vertex decode or conversion step); apply the
   warn-only `0xC00` cap check (§3.2.1) without truncating; read `index_count` (u32); allocate
   `2 × index_count` bytes and blit the index block.
4. Per object after the read, compute the AABB from vertex positions (reading only the first
   12 bytes of each 32-byte vertex), apply the degenerate-axis nudge, and derive the
   distance-squared cull threshold (§7.1).
5. No coordinate transform, matrix multiply, endianness swap, or geometry decode/decompression is
   applied — positions are already absolute world-space (§7.1). The only mutations the loader
   makes are the AABB degenerate-axis nudge and the `tex_id` clamp-to-1 guard (§6); the warn-only
   vertex cap mutates nothing.
6. After **all** `.map` sections are parsed, a per-cell mass-object grid builder bins the loaded
   objects into the cell's spatial grid for rendering and culling (this runs alongside the
   ground-grid and FX-layer builders for the cell). The render mesh build consumes these objects'
   32-byte FVF-0x112 vertices and u16 index lists directly.

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

The per-texture render-class is controlled by the `kind` byte in `bgtexture.lst` (see
`formats/texture.md §bgtexture.lst`). At grid-build time the object's `.map` `tex_id` is
resolved to its `bgtexture.lst` record; the `kind` byte of that record is stored in a
parallel kind array keyed by the bgtexture.lst index and drives the per-frame draw-bucket
and sway decision. The following is **runtime state, not a field in the `.bud` file** — it
is recorded here because it directly determines how a faithfully parsed `.bud` must be
rendered.

**Four-way render-class dispatch by `kind` range — CONFIRMED:**

| `kind` range | Render class | Sway behaviour | Notes |
|---:|---|---|---|
| 0x01 (1) | Static copy (else bucket) | None — vertices copied verbatim to the render buffer | ~1100 of 1222 bgtexture.lst records |
| 0x02 (2) | Solid/shadow bucket | None — drawn in colour pass AND projected-shadow pass | ~101 records; stone/moss/building/dense-foliage; no sway |
| 0x0A..0x0E (10–14) | Wind-sway small/medium | Per-vertex amplitude; vertex_count==9 path is fully unrolled; sway divisor = 2 raised to (kind−10); writable deform scratch buffer allocated | Shipped data: kinds 10, 11, 12 |
| 0x14..0x18 (20–24) | Wind-sway large | Amplitude = AABB XZ-diagonal × 0.01 × 0.5, clamped to 2.0, then divided by 2 raised to (kind−20); writable deform scratch buffer allocated | Shipped data: kind 20 only |
| all other values | Static copy (else bucket) | None — range fall-through; unexercised in shipped data | — |

**Material state for all static-object passes — CONFIRMED:**

Every render class (all kind values) draws through the same opaque world colour pass. The
state is identical for all buckets:
- **Alpha blend on** — ALPHABLENDENABLE=1, SRCBLEND=SRCALPHA, DESTBLEND=INVSRCALPHA. Foliage
  cutout is achieved purely by alpha blending on the texture's own alpha channel.
- **Alpha test off** — ALPHATESTENABLE=0; no ALPHAREF or ALPHAFUNC on this path.
- ZENABLE=1. Stage 0: colour = MODULATE2X(texture, diffuse); alpha = texture alpha.
- **No additive blend**, no alpha-to-coverage, no billboard or camera-facing orientation.
  Wind sway deforms vertex positions in place (world-space pre-baked geometry); there is no
  per-object rotation matrix.
- FVF = 0x112 (XYZ | NORMAL | TEX1, 32-byte stride); confirmed per §3.2.2.
- **Culling**: static/else bucket — D3DCULL_CW (one-sided, inherited state). Kind==2 bucket
  and both sway buckets (0x0A..0x0E and 0x14..0x18) — D3DCULL_NONE (two-sided) in the
  colour pass. Projected-shadow pass uses D3DCULL_CW (one-sided) for static and kind==2.

A consumer of `.bud` geometry MUST NOT apply alpha testing for foliage cutout; the legacy
path uses alpha blending on the texture alpha channel instead. The Viewer hard-coded set
{10, 11, 12, 20} is correct for shipped data but incomplete as a general rule — the full
ranges 0x0A..0x0E and 0x14..0x18 must be supported. Kind==2 is two-sided and alpha-blended
in the colour pass (not a one-sided solid), which is load-bearing for dense-foliage kind==2
textures.

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
- **Two distinct guards — do not conflate them.** The 128-entry pool cap and the per-object
  `tex_id` validation react differently:
  - *Pool build (cap 128):* registering a `TEXTURES` entry past the 128-entry cap **drops** the
    over-cap entry and logs a pool-overflow message. It does not clamp or overwrite an existing
    slot.
  - *Per-object resolve (`tex_id`):* a `tex_id` that is `< 1` or `> pool_size` is logged as an
    error and **clamped to 1** (see below).
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

**Port rule — shared world space, no per-asset bbox recenter (CONFIRMED).** `.bud` buildings
and `.ted` terrain share one absolute world coordinate system with the same +Y up axis; the
device world matrix (D3DTS_WORLD) is never set and remains identity. The per-map cell origin
`(mapX−10000)×1024` / `(mapZ−10000)×1024` is baked into every vertex. A Viewer or port MUST
place ALL assets in one shared absolute world space using the cell origin; it MUST NOT recenter
any individual asset by its own bounding box. If floating-point precision requires a render
origin offset, subtract ONE uniform offset applied identically to every asset AND the camera
— never a per-asset recenter.

**Y-datum cross-confirmation — tree/foliage base co-location ([static-hypothesis] 2026-06-26).**
The `.ted` loader builds each terrain vertex Y directly from the raw height f32 read from the
height block; no per-cell Y offset and no scale factor are applied. `.bud` vertex `pos_y` is
equally an absolute f32 blitted verbatim at load time; the loader applies no transform at load
(§3.3). Both assets therefore share one identical absolute Y datum — not merely the same up axis.
A tree or foliage object (an ordinary `.bud` object — addendum A1.1) whose base vertices are
authored to the terrain surface will co-locate with the terrain at the same XZ coordinates with
no per-asset Y correction at runtime. Wind sway does not disturb this co-location: the per-frame
deformation leaves `pos_y` unchanged and applies horizontal displacement only (addendum A1.4). The
opaque-world draw path was additionally parser-traced: only VIEW and PROJECTION transforms are set
within the terrain-and-building draw; no D3DTS_WORLD call was found for either terrain or building
draws, reinforcing the identity-world-matrix claim by draw-site analysis rather than sample
inference alone. [static-hypothesis] — recovered 2026-06-26 from a session whose live IDB build
differs from the pinned anchor; behaviors build-stable, exact offsets pending build reconciliation.

The runtime does compute, per object after load, an axis-aligned bounding box from the vertex
positions and derives a distance-squared visibility-culling threshold from the object's
footprint. This is **runtime state, not stored in the file**; it is documented here only so
that implementors understand the intent of the `pos_*` fields.

The footprint metric used to pick the threshold is **not** a raw axis extent. It is the larger
of two measures of the object's bounding box:

- a horizontal measure: the planar (XZ) diagonal length of the box, scaled by **0.6** and
  truncated to an integer; and
- a vertical measure: the box's Y (height) extent, truncated to an integer.

`footprint = max( floor(0.6 × XZ_diagonal_length), floor(Y_extent) )`. The threshold table is
then selected by `footprint`:

| `footprint` band | Cull distance (units) | Distance² |
|------------------|-----------------------|-----------|
| < 8  | 300   | 90 000    |
| < 16 | 500   | 250 000   |
| < 32 | 1 000 | 1 000 000 |
| < 64 | 1 500 | 2 250 000 |
| ≥ 64 | ~1 800 | 3 240 000 |

The post-load bounding-box routine also guards against a degenerate (zero-thickness) axis: if
any AABB axis has `min == max`, it nudges that axis's `max` upward by 2.0 before computing the
footprint, so a flat object still receives a non-zero box.

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
7. **`BUILDING TEXTURES` first integer.** The first integer on each `TEXTURES` line is read but
   then discarded on a data line; its only *observed* effect is a positivity gate (`≤ 0` ends /
   skips the block, `> 0` marks a data line — see §2). Whether the value carries any further
   meaning beyond that gate is unknown. It is **not** the registered pool entry (that is the
   second integer) and not the `bgtexture.lst` record index.
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

**Known gap — map-level `.bin` descriptor family (out of scope for this document):**

The client loads a family of per-MAP binary descriptors via the path template
`data/map%s/map%s.bin` (e.g. `data/map001/map001.bin`). A sibling family of per-map
environment/scene-config tables is loaded via templates of the form `/dat/<name>%d.bin`
(covering wind, weather, point-light, cloud-cycle, material, light, fog, clouddome, stardome, and
sky configurations). These are per-map tables, NOT per-cell geometry. They are loaded by dedicated
loaders (e.g. `Wind_LoadMapData`, `PointLight_LoadMapData`) and are entirely independent of the
`.map`/`.bud`/`.sod` per-cell model described in this document.

The `data/map%s/map%s.bin` file observed (520 bytes) contains world-space XZ coordinate pairs
and small-integer region/zone IDs in a fixed-size table. Its exact record layout is a known gap:
the loader was not isolated in the current RE pass (the path is constructed dynamically with no
direct static cross-reference), so the internal field semantics are LOW-confidence hypotheses
only. A dedicated spec (e.g. `map_bin.md` or a section in a region-descriptor document) should be
authored once the loader is recovered. Do not fold map-level `.bin` coverage into this document.

---

## Addendum 2026-06-26 — tree-foliage-render (deep-dive confirmation)

This addendum records a deep-dive confirmation pass focused on the tree/foliage render path
(`.bud` kind dispatch, colour-pass material state, and wind-sway vertex deformation). All claims
below are derived from firewall-clean analysis; they must be read in conjunction with §5, which
they extend and partially correct. Where this addendum contradicts §5, this addendum supersedes.

### A1. Confirmed facts

**A1.1 — Trees are ordinary `.bud` objects; no separate foliage object list exists.**

The mass-object grid builder processes ALL `.bud` objects through a single shared 16×16
placement grid. Foliage and trees are not loaded from a distinct list or a distinct file section.
The only thing that distinguishes a tree or foliage object from a stone wall or prop object is its
bound texture's `kind` byte, resolved at grid-build time from the `bgtexture.lst` record that
`tex_id` maps to (through the per-cell `BUILDING TEXTURES` pool — see §6 and
`Docs/RE/formats/texture.md`). The `kind` byte is looked up via the parallel `TerrainPool`
kind-byte array keyed by the `bgtexture.lst` record index; values outside the array's populated
range return the default value 1 (static). The resolved kind is stored in the runtime object
record for use by the per-frame render-class dispatch. No content-side tagging (in the `.bud`
file or the `.map` file) is needed to mark an object as foliage.

The `tex_id` validation gate described in §6 (clamp-to-1 on out-of-range) applies here: an
object whose `tex_id` is invalid is clamped to pool entry 1 before kind resolution, meaning it
inherits the kind of whatever texture occupies pool slot 1.

**A1.2 — Per-class object lists; deform scratch buffers allocated at classify time.**

Objects are pre-sorted into separate per-class lists at grid-build time, giving the four-way
render-class dispatch described in §5. The classification tests kind in the inclusive ranges
0x0A..0x0E (sway small/medium) and 0x14..0x18 (sway large). For both sway buckets, a writable
deform scratch buffer is allocated immediately on classification: it is a copy of the object's
base vertex positions (32 bytes per vertex, matching the FVF = 0x112 stride from §3.2.2), and
this per-object scratch buffer is what the per-frame sway deformation mutates in place.

The colour pass (the opaque-world render pass) draws the solid/kind==2 list alongside the static
and both sway lists. The projected-shadow pass independently redraws the solid/kind==2 list,
confirming that kind==2 objects participate in both the colour pass and the shadow pass (consistent
with §5's material state table, where kind==2 is listed as the "solid/shadow bucket").

**A1.3 — MODULATE2X colour operation confirmed; complete colour-pass material state.**

The colour-pass material state for the mass-object draw was confirmed directly from the
draw-site render-state setup block. The full confirmed state is:

| State | Value | D3D name |
|---|---|---|
| Stage 0 COLOROP | 5 | D3DTOP_MODULATE2X |
| Stage 0 COLORARG1 | TEXTURE | — |
| Stage 0 COLORARG2 | DIFFUSE | — |
| Stage 0 ALPHAOP | SELECTARG1 | — |
| Stage 0 ALPHAARG1 | TEXTURE | alpha = texture alpha |
| Stage 1 | disabled | — |
| ALPHABLENDENABLE | 1 (on) | — |
| SRCBLEND | 5 | D3DBLEND_SRCALPHA |
| DESTBLEND | 6 | D3DBLEND_INVSRCALPHA |
| ALPHATESTENABLE | 0 (off) | — |
| ZENABLE | 1 (on) | — |

This state is consistent with §5's "Material state for all static-object passes" block and
confirms it with greater precision. Specifically: the colour result is `texture × diffuseLit × 2`
(the ×2 doubling of MODULATE2X vs. MODULATE), the alpha is sourced purely from the texture, and
foliage cutout is achieved by alpha BLEND on that texture alpha — never by alpha test (ALPHATESTENABLE=0,
confirmed). Depth testing is on; the depth-write state was not explicitly set in this pass and
therefore inherits whatever the sub-pass sequence left it at — implementors should keep depth-test
on and default depth-write on for opaque-canopy geometry (see §A4 port guidance).

**A1.4 — Wind sway: per-vertex horizontal displacement driven by a global time accumulator.**

The per-frame sway deformation operates as follows (confirmed):

- A global environment time block records elapsed time per frame. The sway dispatcher reads a
  per-frame time delta from this block, accumulates it, scales the accumulated value by 1e−6, and
  gates each deform update to fire only once the accumulator reaches a threshold of 50 (in the
  scaled unit). When the game-state is paused (state value 3), accumulation is skipped for that
  frame.
- A single global tick counter cycles over the values 0..4 and is shared by ALL sway objects in
  the cell. Each object multiplies this counter by a per-object speed value to form a phase step,
  then advances a per-object phase value using a bounded ping-pong oscillator (see A2.4).
- Vertex displacement is horizontal only. Each displaced vertex receives:
  `displaced.x = base.x + direction.x × phase`, `displaced.z = base.z + direction.z × phase`.
  The Y (vertical) component is left unchanged in the main deformation path — sway is a pure
  horizontal bending motion.
- `direction` is a small per-object horizontal vector precomputed from the object's vertex normals,
  scaled approximately 0.5.
- The FVF for the sway draw is also 0x112, consistent with §3.2.2.
- Two deformation variants exist: a high-detail variant that additionally queries the Wind subsystem
  (see §A3 open edge) by the vertex's height to add an extra bend term, and a low-detail variant
  that applies a simpler single-axis (Z) displacement only.

**A1.5 — Per-object sway amplitude formula (both buckets).**

See §A2.3 and §A2.5 for the corrections to the §5 formula. The now-confirmed formula is:

- Large bucket (kind 0x14..0x18): `amplitude = 3D_corner_distance × 0.01 × 0.5`, clamped to a
  maximum of 2.0, then divided by `2 << (kind − 20)` (i.e. kind 20 → ÷2, kind 21 → ÷4, …). The
  distance is the full 3D Euclidean distance between two stored AABB corner points — see A2.2.
- Small/medium bucket (kind 0x0A..0x0E): `amplitude` is derived from per-geometry vertex deltas
  (differences between specific vertex positions, scaled × 0.1), with a dedicated sub-case for
  `vertex_count == 9` and another for `vertex_count > 5`. It is NOT the distance-diagonal formula.
  The final amplitude is divided by `2 << (kind − 10)` (kind 10 → ÷2, kind 11 → ÷4, kind 12 → ÷8).
- If the computed amplitude is zero for any object, that object falls back to a plain non-deformed
  vertex copy (no sway applied).

### A2. Corrections to §5

The following items in §5 are corrected by this addendum. §5 text should be understood subject
to these corrections; a future revision of §5 should incorporate them inline.

**A2.1 — CORRECTION: sway divisor formula (§5, "Wind-sway small/medium" and "Wind-sway large" rows).**

§5 states the sway divisor is "2 raised to (kind−10)" for the small/medium bucket and "2 raised
to (kind−20)" for the large bucket, yielding divide-by-1 for kind 10 and kind 20 respectively.
The binary uses the expression `2 << (kind − base)`, which in integer arithmetic equals
`2^(kind − base + 1)`, not `2^(kind − base)`. The correct divisors for shipped kinds are:

| kind | base | divisor (corrected) | divisor (old, wrong) |
|---|---|---|---|
| 10 | 10 | 2 | 1 |
| 11 | 10 | 4 | 2 |
| 12 | 10 | 8 | 4 |
| 20 | 20 | 2 | 1 |

The §5 formula is off by a factor of 2 across the board. Use `2 << (kind − base)` (base 10 for
small/medium, base 20 for large) as the divisor.

**A2.2 — CORRECTION: large-bucket amplitude distance is 3D, not XZ-only (§5, "Wind-sway large" row).**

§5 describes the large-bucket amplitude distance as "AABB XZ-diagonal". The distance helper
used in the binary computes a full 3D Euclidean distance (it includes the Y component) between
two stored AABB corner points. If those two corners are the AABB min and max, the result is the
full 3D box diagonal, not an XZ-projected diagonal. The "XZ-diagonal" label is imprecise;
replace it with "full 3D corner-to-corner distance".

**A2.3 — CORRECTION/ADDITION: small/medium amplitude uses a different formula to large (§5, "Wind-sway small/medium" row).**

§5 does not describe the small/medium amplitude formula distinctly from the large-bucket formula
(the table entry is vague on this point). The small/medium bucket uses a completely different
formula: per-geometry vertex deltas (selected vertex-position differences) scaled by 0.1, with
separate unrolled sub-cases for `vertex_count == 9` and `vertex_count > 5`. The two buckets do
not share an amplitude formula. The large-bucket distance × 0.01 × 0.5 formula applies only to
the large bucket.

**A2.4 — ADDITION: oscillator shape is bounded linear ping-pong, not sinusoidal.**

§5 does not state the shape of the sway oscillator. The confirmed shape is a bounded linear
ping-pong: the phase value advances by a per-object step each tick; when it reaches the reflect
bound (proportional to amplitude × 0.1) the velocity flips sign between +100 and −100. This
produces a triangle-wave motion, not a sinusoid. For a strict 1:1 recreation the oscillator must
be a ping-pong; a sine approximation (with the corrected amplitude from A1.5) is visually
acceptable but is not byte-identical.

**A2.5 — REINFORCEMENT: amplitude clamp for large bucket.**

The large bucket's amplitude is clamped to a maximum of 2.0 before applying the divisor. §5 does
not state this clamp; add it to the record.

### A3. Still-open edges

1. **Per-bucket CULLMODE — not independently re-derived in this pass.** The colour-pass material
   state in §5 assigns two-sided rendering (D3DCULL_NONE) to kind==2 and both sway buckets
   (0x0A..0x0E, 0x14..0x18), and one-sided (D3DCULL_CW) to the static/else bucket. This pass did
   not encounter an explicit CULLMODE render-state set in the colour-pass draw helpers or any
   per-object draw function traced here. The cull assignment may be set in a device-init or
   material-setup helper not traced in this pass, or inherited from a global state. Treat the §5
   cull assignments as doc-claimed-not-byte-reconfirmed; verify via a live-debugger read of the
   CULLMODE render-state at the mass-object draw site before treating per-bucket cull as
   independently byte-confirmed.

2. **Environment time delta units.** The per-frame elapsed-time value read from the global
   environment time block is scaled by 1e−6 and accumulated to a gate of ≥50 before triggering
   a deform update. The exact units of the raw delta value (milliseconds, microseconds, or a
   game-tick count) and the physical meaning of the 1e−6 scaling constant and the 50 gate were
   not pinned in this pass.

3. **Exact per-vertex index mapping in the unrolled deform for small fixed vertex counts.** The
   sway deformation has special-cased paths for small fixed vertex counts (including
   `vertex_count == 8` and `vertex_count == 9`). The precise set of vertices that receive the
   wind extra-bend term in the high-detail path vs. the standard displacement was not
   exhaustively enumerated.

4. **Wind-subsystem coupling (high-detail variant).** The high-detail sway variant queries the
   Wind subsystem by the vertex's height to add an extra bend term. The exact Wind-subsystem
   field sampled and the formula by which it modulates the amplitude were only partially read.
   See also `Docs/RE/formats/terrain_layers.md` (the wind-blob entry) for the Wind subsystem's
   asset side. A dedicated RE pass on the Wind subsystem coupling is needed before this can be
   implemented faithfully.

### A4. Port guidance

**Foliage material — fixes the "too dark" render.** The original multiplies texture by
vertex-diffuse (from fixed-function D3D lighting) then doubles the result (D3DTOP_MODULATE2X).
In a modern linear-light / PBR material:

- With dynamic lighting: `albedo_out = saturate(2.0 × texture.rgb × litDiffuse.rgb)`.
- Unlit / albedo-only preview (Viewer): `albedo_out = saturate(texture.rgb × 2.0)` approximates
  the original brightness at full diffuse.

**Transparency.** Use alpha-BLEND (SrcAlpha / InvSrcAlpha) sourced from the texture's own alpha
channel. Do NOT use alpha-scissor or alpha-test for foliage cutout — the original has
ALPHATESTENABLE = 0 on this pass. Depth-test on; depth-write on for opaque-canopy geometry is
a safe default (the legacy pass does not explicitly toggle depth-write here; sorting artifacts
from blended foliage against itself may require disabling depth-write per-object if visible).

**Two-sidedness.** Render kind==2 and both sway buckets (0x0A..0x0E and 0x14..0x18) two-sided
(cull off); static/else bucket one-sided. This matches the §5 table, but treat the exact cull
state as doc-claimed-not-byte-reconfirmed (see A3.1) and verify on a representative sample before
finalising.

**Sway shader — implement as a per-vertex horizontal displacement, not a billboard.**

```
displaced.x = base.x + dir.x × phase
displaced.y = base.y                  // Y fixed — purely horizontal bending
displaced.z = base.z + dir.z × phase
```

where:
- `dir` is a small per-object horizontal vector (derive from the object's averaged vertex normal,
  horizontal components only, scaled ~0.5).
- `phase` oscillates over time driven by a single global clock shared by all foliage objects.
  For a strict 1:1 match use a bounded linear ping-pong oscillator (velocity flips ±100 at reflect
  bounds proportional to `amplitude × 0.1`). A sine approximation is visually acceptable:
  `phase = amplitude × sin(globalTime × perObjectSpeed + perObjectPhaseOffset)`.
- `amplitude` per object (use the corrected formulas from A1.5 and A2.1):
  - Small/medium bucket (kind 0x0A..0x0E): vertex-extent-based — verticalDelta × 0.1, divided by
    `2 << (kind − 10)` (kind 10 → ÷2, kind 11 → ÷4, kind 12 → ÷8).
  - Large bucket (kind 0x14..0x18): 3D corner distance × 0.01 × 0.5, clamped to 2.0, divided by
    `2 << (kind − 20)` (kind 20 → ÷2).
  - If computed amplitude is zero, skip deformation entirely (plain vertex copy).

**Classification.** Trees need no special path. Classify any `.bud` object by its bound texture's
`kind` byte (resolved through `tex_id` → `BUILDING TEXTURES` pool → `bgtexture.lst` → kind, as
described in §6 and `Docs/RE/formats/texture.md`) and route it to the matching render bucket.
The Viewer's hard-coded sway set {10, 11, 12, 20} is correct for the current shipped asset set
but must be generalised to the full ranges 0x0A..0x0E and 0x14..0x18 for spec-correctness.