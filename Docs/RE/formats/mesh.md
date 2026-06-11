# Format: .xobj / .skn / .bnd  (geometry and skeleton assets)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

## Overview

Three file types carry geometry and skeletal data. All are opened through the VFS chokepoint
(see `formats/pak.md`).

| Extension | Style | Role |
|---|---|---|
| `.xobj` | ASCII whitespace-tokenized | Static triangle mesh; used for simple scene objects and editor primitives |
| `.skn` | Binary, little-endian | Skinned character mesh with per-vertex bone weights |
| `.bnd` | Binary, little-endian | Bind-pose skeleton: bone hierarchy and rest transforms |

---

## Format: `.xobj` — ASCII static mesh

### Identification

- **Extension:** `.xobj`
- **Found in:** `.vfs` archive (logical path pattern: e.g. `objects/*.xobj`)
- **Magic / signature:** none — file is pure ASCII with no binary header.
- **Endianness:** N/A (ASCII numeric tokens, parsed with standard decimal/float parsers)

### Read order

The file is tokenized by whitespace (spaces, tabs, carriage returns, newlines). Tokens appear in
the order below; there is no explicit section header between the index list and the vertex list.

**Preamble:**

| Token position | Type | Field | Notes | Confidence |
|---|---|---|---|---|
| 1 | u32 | `unused_token` | Read and silently discarded by the parser. Purpose unknown. | CONFIRMED (discard confirmed; meaning UNVERIFIED) |
| 2 | u32 | `num_triangles` | Count of triangles in the index list that follows. | CONFIRMED |

**Index list** (`num_triangles × 3` tokens, one u32 per token):

Each triangle occupies three consecutive tokens, each being a zero-based index into the vertex
array that follows. The in-memory representation stores each index as a u16 (the parser truncates
from the parsed u32). Order within a triangle is the winding order as stored; no winding reversal
is applied on load.

| Repetition | Type | Field | Confidence |
|---|---|---|---|
| `num_triangles × 3` | u32 → u16 | `vertex_index[n]` | CONFIRMED |

**Vertex count:**

| Token position | Type | Field | Confidence |
|---|---|---|---|
| after index list | u32 | `num_vertices` | CONFIRMED |

**Vertex list** (`num_vertices` records, 8 tokens each):

| Token within record | Type | Field | Notes | Confidence |
|---|---|---|---|---|
| 1 | f32 | `pos_x` | World/local X position | CONFIRMED |
| 2 | f32 | `pos_y` | World/local Y position | CONFIRMED |
| 3 | f32 | `pos_z` | World/local Z position | CONFIRMED |
| 4 | f32 | `norm_x` | Surface normal X — read then discarded; not kept in memory | CONFIRMED |
| 5 | f32 | `norm_y` | Surface normal Y — read then discarded | CONFIRMED |
| 6 | f32 | `norm_z` | Surface normal Z — read then discarded | CONFIRMED |
| 7 | f32 | `tex_u` | UV horizontal coordinate | CONFIRMED |
| 8 | f32 | `tex_v` | UV vertical coordinate as stored on disk; the engine transforms it to `1.0 - tex_v` in-memory | CONFIRMED |

**In-memory vertex stride:** 24 bytes (position 3 × f32 + UV 2 × f32 + 4 bytes padding/unused).
Normals are not carried into the in-memory vertex buffer.

**End of file:** no trailer; file ends after the last vertex record.

---

## Format: `.skn` — binary skinned mesh

### Identification

- **Extension:** `.skn`
- **Found in:** `.vfs` archive (logical path pattern: e.g. `characters/*.skn`)
- **Magic / signature:** none identified; file begins immediately with the header fields below.
- **Endianness:** little-endian throughout.

### Header

Appears at offset 0. The `name` field is variable-length (see String encoding below), so
subsequent sections have no fixed absolute offsets.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `id_a` | First numeric identifier for this mesh object. | CONFIRMED |
| 4 | 4 | u32 LE | `id_b` | Bind-pose reference ID. The loader calls `CorePoseManager_GetById(id_b)` to associate this skin with a `.bnd` skeleton. | CONFIRMED |
| 8 | variable | LenStr | `name` | Length-prefixed name string. See String encoding section. | CONFIRMED (presence); UNVERIFIED (exact wire encoding) |

### String encoding (LenStr) — used in `.skn` and `.bnd`

The string is read by a dedicated primitive that indicates a length prefix rather than a
null-terminated layout. The exact prefix width (1 byte vs. 2 bytes) and whether a null terminator
follows the character data are **sample-unverified**. Implementors should support at least
a 1-byte-prefixed byte string until a sample confirms the encoding width.

### Face table

Immediately follows the header (after the variable-length `name`).

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `face_count` | Number of triangles. | CONFIRMED |
| 4 | `face_count × 36` | bytes | `face_data` | Raw block; `face_count` records of 36 bytes each. | CONFIRMED |

**Face record — 36 bytes (3 corners × 12 bytes each):**

Each face record encodes one triangle as three consecutive corner sub-records.

| Sub-offset within corner | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index into the vertex array. | CONFIRMED |
| 4 | 4 | f32 LE | `uv_u` | UV horizontal coordinate. | CONFIRMED |
| 8 | 4 | f32 LE | `uv_v` | UV vertical coordinate as stored; the engine applies `1.0 - uv_v` when building the render vertex. | CONFIRMED |

Corner sub-record stride: 12 bytes. Three sub-records per face record = 36 bytes per face.

### Vertex table

Immediately follows the face table.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_count` | Number of vertex records. | CONFIRMED |
| 4 | `vertex_count × 24` | bytes | `vertex_data` | Raw block; `vertex_count` records of 24 bytes each. | CONFIRMED |

**Vertex record — 24 bytes (6 floats), little-endian:**

IMPORTANT: the on-disk layout is **normal first, then position** — the reverse of the common
convention. The engine re-orders the fields when constructing the render vertex.

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | f32 LE | `normal_x` | Surface normal X — stored first on disk. | CONFIRMED |
| 4 | 4 | f32 LE | `normal_y` | Surface normal Y. | CONFIRMED |
| 8 | 4 | f32 LE | `normal_z` | Surface normal Z. | CONFIRMED |
| 12 | 4 | f32 LE | `pos_x` | Vertex position X — stored second on disk. | CONFIRMED |
| 16 | 4 | f32 LE | `pos_y` | Vertex position Y. | CONFIRMED |
| 20 | 4 | f32 LE | `pos_z` | Vertex position Z. | CONFIRMED |

Parser note: when mapping to a standard vertex buffer (position first), read floats at sub-offsets
12–23 as position and floats at sub-offsets 0–11 as normal.

### Weight / skin table

Immediately follows the vertex table.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `weight_count` | Number of skin-weight records. | CONFIRMED |
| 4 | `weight_count × 12` | bytes | `weight_data` | Raw block; `weight_count` records of 12 bytes each. | CONFIRMED |

**Weight record — 12 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index of the vertex this weight influences. | CONFIRMED |
| 4 | 4 | u32 LE | `bone_index` | Zero-based index into the associated bind-pose bone array. | CONFIRMED |
| 8 | 4 | f32 LE | `weight` | Influence weight in the range (0.0, 1.0]. Records where `weight < 0.01` are skipped by the loader. | CONFIRMED |

Post-load invariant: the engine normalizes weights per vertex so the sum equals 1.0. A zero total
weight for any vertex is a fatal assertion in the original client (`total_weight != 0.0f`).

### End of file

No explicit end-of-file marker. The file ends immediately after the last weight record.

---

## Format: `.bnd` — binary bind-pose skeleton

### Identification

- **Extension:** `.bnd`
- **Found in:** `.vfs` archive (logical path pattern: e.g. `characters/*.bnd`)
- **Magic / signature:** none identified; file begins immediately with the header fields below.
- **Endianness:** little-endian throughout.

### Header

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `actor_id` | Numeric ID for this skeleton; typically the numeric suffix of the filename (e.g. filename `g100.bnd` → `actor_id = 100`). Matched against the `id_b` field in `.skn` headers. | CONFIRMED |
| 4 | variable | LenStr | `actor_name` | Length-prefixed name string (see String encoding in `.skn` section). | CONFIRMED (presence); UNVERIFIED (exact wire encoding) |
| after name | 1 | u8 | `bone_count` | Number of bone records that follow. Stored as a single unsigned byte; maximum value 255. | CONFIRMED |

### Bone array

`bone_count` records of **72 bytes each**, read in sequence. The 72-byte on-disk record size is
confirmed by the loader's pointer arithmetic (stride of 18 dwords × 4 bytes = 72 bytes). The
in-memory bone allocation also uses 72 bytes.

Note: a prior IDB comment described the bone record as "36 bytes on disk." This is incorrect; the
stride evidence indicates 72 bytes on disk. The first 36 bytes account for the confirmed fields
below; the remaining 36 bytes are uncharacterized. Mark as CONFLICTED pending a sample.

**BndBone record — 72 bytes on disk, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `self_id` | Identifies this bone within the skeleton; only the low byte is consumed in the observed code paths. | MEDIUM |
| 4 | 4 | u32 LE | `parent_id` | Identifies the parent bone (for hierarchy linkage); only the low byte is consumed. Root bone convention (self-referential or sentinel value) is unverified. | MEDIUM |
| 8 | 12 | f32[3] LE | `translation` | Rest-pose local translation vector (X, Y, Z). | MEDIUM |
| 20 | 16 | f32[4] LE | `rotation` | Rest-pose local rotation quaternion (X, Y, Z, W component order unverified). | MEDIUM |
| 36 | 36 | u8[36] | `unknown_36` | Remaining bytes of the 72-byte record. Never characterized by examined code paths. May contain scale, flags, inverse-bind matrix data, or additional transform data. | UNVERIFIED |

**Confirmed sub-field byte count:** 4 + 4 + 12 + 16 = 36 bytes of identified fields.  
**Uncharacterized:** 36 bytes (sub-offsets 36–71).

Post-load: the engine computes parent/child hierarchy linkage and world-space transforms from root
after loading all bone records. These are derived data and are not stored on disk.

---

## Enumerations / flags

None identified in these formats.

## Known unknowns

- `.xobj` token 1 (`unused_token`): purpose of the first discarded token is unknown.
- `.skn` / `.bnd` `LenStr` prefix width: 1-byte vs. 2-byte prefix unresolved; sample required.
- `.bnd` bone record bytes 36–71: 36 bytes of each bone record are entirely uncharacterized.
- `.bnd` bone `self_id` / `parent_id`: only the low byte is observed to be consumed; whether the
  high bytes carry meaning is unknown.
- `.bnd` root bone sentinel: what value `parent_id` holds for the root bone is unverified.
- `.bnd` quaternion component order (XYZW vs. WXYZ): unverified without a sample.
- No magic bytes confirmed for `.skn` or `.bnd`; the files may begin with content fields directly
  or may have an unexamined preamble byte that was not observed.

## Cross-references

- Related formats: `formats/pak.md` (container), `formats/texture.md` (co-referenced assets)
- Canonical names: see `Docs/RE/names.yaml` (`XobjFile`, `SkinFile`, `BindPoseFile`, `SknFace`,
  `SknCorner`, `SknVertex`, `SknWeight`, `BndBone`)
- Provenance: see `Docs/RE/journal.md`
