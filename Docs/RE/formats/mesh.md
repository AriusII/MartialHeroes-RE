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

### String encoding (LenStr) — used in `.skn` and `.bnd`

> ⚠ CORRECTION — previously this spec stated the prefix width was unverified and suggested
> supporting a 1-byte prefix. That was wrong. The prefix is a **4-byte little-endian u32**.

Both `.skn` and `.bnd` use a length-prefixed string encoding throughout. The wire format is:

```
[u32 LE — byte length of the body (4 bytes)]
[char[length] — string body, no null terminator on disk]
```

The length field is always a full 4-byte unsigned integer read in little-endian order. The body
is exactly `length` bytes with no trailing null byte on disk; the engine constructs an in-memory
string object from the raw bytes.

Any parser that reads these files must consume 4 bytes for the length prefix, not 1 byte.
Implementations written against the previous (1-byte) assumption will misparse all LenStr fields
in both `.skn` and `.bnd`.

| Confidence |
|---|
| CONFIRMED — resolved by direct analysis of the string-read primitive in binary mode |

### Header

Appears at offset 0. The `name` field is variable-length (its size is `4 + name_length` bytes),
so subsequent sections have no fixed absolute offsets.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `id_a` | First numeric identifier for this mesh object. | CONFIRMED |
| 4 | 4 | u32 LE | `id_b` | Bind-pose reference ID. The loader associates this skin with the `.bnd` skeleton whose `actor_id` matches this value. | CONFIRMED |
| 8 | 4 + N | LenStr | `name` | Length-prefixed name string. Wire format: 4-byte u32 LE length, then N bytes of body with no null terminator. See String encoding section. | CONFIRMED |

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
weight for any vertex is a fatal assertion in the original client.

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
| 0 | 4 | u32 LE | `actor_id` | Numeric ID for this skeleton; typically the numeric suffix of the filename (e.g. `g100.bnd` → `actor_id = 100`). Matched against the `id_b` field in `.skn` headers. | CONFIRMED |
| 4 | 4 + N | LenStr | `actor_name` | Length-prefixed name string. Wire format: 4-byte u32 LE length, then N bytes with no null terminator. See String encoding in the `.skn` section. | CONFIRMED |
| after name | 4 | u32 LE | `bone_count` | Number of bone records that follow. On-disk representation is a full u32; only the low byte is stored to the in-memory count field, giving an effective maximum of 255 bones. | CONFIRMED |

Note: a previous revision of this spec described `bone_count` as a 1-byte on-disk field.
That was incorrect. The loader reads a full 4-byte u32 in binary mode; the high three bytes are
discarded after the read. The on-disk representation is u32 LE.

### Bone array

> ⚠ CORRECTION — a prior version of this spec stated the on-disk bone record was 72 bytes and
> contained 36 bytes of uncharacterized data (sub-offsets 36–71). Both claims were wrong.
>
> The 72-byte figure is the **in-memory** object size, which includes computed fields populated
> by post-load passes (parent/child tree pointers, world-space translation, world-space rotation).
> The **on-disk** record is **36 bytes**. There is no uncharacterized trailing region on disk.
> Any parser code written against the 72-byte on-disk assumption will over-read by 36 bytes per
> bone and must be corrected.

`bone_count` records of **36 bytes each**, read in sequence. Every field in the 36-byte record
is fully characterized.

**BndBone on-disk record — 36 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `self_id` | Identifies this bone within the skeleton. The loader stores only the low byte to the in-memory bone; the high three bytes are discarded after the read. | CONFIRMED |
| 4 | 4 | u32 LE | `parent_id` | Identifies the parent bone. The loader stores only the low byte to the in-memory bone. For the root bone this field holds the same value as `self_id` (both zero). | CONFIRMED |
| 8 | 12 | f32[3] LE | `local_translation` | Rest-pose local translation vector: X at +8, Y at +12, Z at +16. | CONFIRMED |
| 20 | 16 | f32[4] LE | `local_rotation` | Rest-pose local rotation quaternion in **XYZW component order** (the scalar W component is stored last, at sub-offset +32 within the record). | CONFIRMED |

**Total on-disk per bone: 36 bytes.**

The remaining fields in the in-memory bone object (parent/child/sibling tree pointers,
world-space translation, world-space rotation) are derived values computed by post-load routines
after all bone records are read. They are never stored on disk.

### Root bone sentinel

The root bone is identified by the condition: `self_id == 0` and `parent_id == 0` (both low
bytes are zero). No other on-disk convention marks the root. Any bone whose `parent_id` low byte
is non-zero and has no matching `self_id` among the other bones causes a fatal abort in the
original client.

| Confidence |
|---|
| CONFIRMED — resolved by direct analysis of the hierarchy-build routine |

### Quaternion component order

The local rotation quaternion is stored as four consecutive floats in **XYZW order**: X at
sub-offset +20, Y at +24, Z at +28, W (scalar) at +32. This order applies both on disk and in
the in-memory representation; the loader reads the bytes directly into the in-memory quaternion
fields without reordering.

| Confidence |
|---|
| CONFIRMED — resolved by analysis of the quaternion multiply routine used during world-transform accumulation |

---

## Enumerations / flags

None identified in these formats.

## Known unknowns

- `.xobj` token 1 (`unused_token`): purpose of the first discarded token is unknown.
- `.skn` / `.bnd` `self_id` and `parent_id`: the on-disk representation is a full u32 LE; only
  the low byte is consumed by the observed code paths. Whether the upper three bytes carry meaning
  (e.g. a namespace, a category tag, or an extended bone count) is unknown without a sample.
- No magic bytes confirmed for `.xobj`, `.skn`, or `.bnd`; the files begin with content fields
  directly or may have an unexamined preamble not visible in the examined code path.

## Cross-references

- Related formats: `formats/pak.md` (container), `formats/texture.md` (co-referenced assets)
- Canonical names: see `Docs/RE/names.yaml` (`XobjFile`, `SkinFile`, `BindPoseFile`, `SknFace`,
  `SknCorner`, `SknVertex`, `SknWeight`, `BndBone`)
- Provenance: see `Docs/RE/journal.md`
