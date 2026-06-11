# Format: .xobj / .skn / .bnd  (geometry and skeleton assets)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

## Overview

Three file types carry geometry and skeletal data. All are opened through the VFS chokepoint
(see `formats/pak.md`).

| Extension | Style | Role | Sample verified |
|---|---|---|---|
| `.xobj` | ASCII whitespace-tokenized | Static triangle mesh; used for scene objects and editor primitives | YES — 3 samples (plane, cone, triangle) |
| `.skn` | Binary, little-endian | Skinned character mesh with per-vertex bone weights | YES — 3 item-skin samples |
| `.bnd` | Binary, little-endian | Bind-pose skeleton: bone hierarchy and rest transforms | YES — 3 single-bone samples |

---

## Format: `.xobj` — ASCII static mesh

### Status

- **sample_verified:** true
- **Samples analysed:** 3 files (`(0000)-plane.xobj`, `(0002)-cone.xobj`, `(0004)-triangurate.xobj`)
- **Confidence baseline:** all structural claims CONFIRMED against real bytes unless marked otherwise.

### Identification

- **Extension:** `.xobj`
- **Found in:** `.vfs` archive — path prefix `data/effect/xobj/`; manifest at `data/effect/xobj.lst`
- **Magic / signature:** none — file is pure ASCII with no binary header.
- **Line endings:** CRLF observed in all samples. The parser uses a whitespace tokenizer that accepts
  any line-ending convention; CRLF is not enforced.
- **Endianness:** N/A (ASCII numeric tokens, parsed with standard decimal/float parsers)

### Read order

The file is tokenized by whitespace (spaces, tabs, carriage returns, newlines). Each logical token
occupies its own line in observed samples. Tokens appear in the order below; there is no explicit
section header between the index list and the vertex list.

**Preamble:**

| Token position | Type | Field | Notes | Confidence |
|---|---|---|---|---|
| 1 | u32 | `slot_id` | The object's slot index as written by the content-creation tool. Value matches the numeric prefix embedded in the filename (e.g. `(0002)-cone.xobj` → `slot_id = 2`). Read and silently discarded by the parser; never validated against the runtime slot index at load time. | CONFIRMED — discard confirmed; meaning identified from sample pattern |
| 2 | u32 | `face_count` | Count of triangles in the index list that follows. | CONFIRMED |

> Note: the previous revision of this spec labelled this first token `unused_token` and marked its
> purpose unknown. Sample evidence shows it is the self-referential slot ID. The discard behaviour
> is unchanged.

**Index list** (`face_count × 3` tokens, one u32 per token):

Each triangle occupies three consecutive tokens (`v0`, `v1`, `v2`), each being a zero-based index
into the vertex array that follows. The parser stores each index as a `uint16` in the in-memory
index buffer (allocated as `2 × 3 × face_count` bytes). Order within a triangle is the winding
order as stored; no winding reversal is applied on load.

| Repetition | Type | Field | Confidence |
|---|---|---|---|
| `face_count × 3` | u32 → u16 | `vertex_index[n]` | CONFIRMED — all sample face indices are in range and winding is geometrically consistent |

Maximum representable index is 65535 (uint16 ceiling). No sample has come close to this limit.

**Vertex count:**

| Token position | Type | Field | Confidence |
|---|---|---|---|
| after index list | u32 | `vertex_count` | CONFIRMED |

**Vertex data rows** (`vertex_count` records, 8 tab-delimited floats per row):

Each vertex is one line of exactly 8 decimal float tokens separated by tab characters. The in-memory
vertex buffer layout does NOT include normals (see in-memory note below).

| Column (0-based) | Type | Field | Notes | Confidence |
|---|---|---|---|---|
| 0 | f32 | `pos_x` | Vertex position X | CONFIRMED |
| 1 | f32 | `pos_y` | Vertex position Y | CONFIRMED |
| 2 | f32 | `pos_z` | Vertex position Z | CONFIRMED |
| 3 | f32 | `norm_x` | Surface normal X — read into a scratch local then discarded; NOT stored in the in-memory vertex buffer | CONFIRMED |
| 4 | f32 | `norm_y` | Surface normal Y — discarded | CONFIRMED |
| 5 | f32 | `norm_z` | Surface normal Z — discarded | CONFIRMED |
| 6 | f32 | `tex_u` | UV horizontal coordinate | CONFIRMED |
| 7 | f32 | `tex_v` | UV vertical coordinate as stored on disk (D3D convention); the parser applies `v_mem = 1.0 − tex_v` when writing the in-memory vertex | CONFIRMED |

All normal vectors in observed samples have magnitude 1.000000 (within 1e-6 float precision),
confirming they are unit-length surface normals.

**In-memory vertex layout — 24 bytes per vertex (6 × f32):**

Normals are not carried into the in-memory vertex buffer. The 24-byte record layout is:

| Byte offset | Size | Field | Source |
|---:|---:|---|---|
| 0 | 4 | `pos_x` | disk column 0 |
| 4 | 4 | `pos_y` | disk column 1 |
| 8 | 4 | `pos_z` | disk column 2 |
| 12 | 4 | (uninitialised / padding) | never written; default-constructed as zero |
| 16 | 4 | `tex_u` | disk column 6 |
| 20 | 4 | `tex_v` | `1.0 − disk column 7` |

**End of file:** no trailer; file ends after the last vertex record.

---

## Format: `.skn` — binary skinned mesh

### Status

- **sample_verified:** true
- **Samples analysed:** 3 item-skin files (`gi213050001.skn`, `gi213062382.skn`, `gi292020105.skn`)
  from `data/item/skin/`. All three exhausted exactly at the predicted end of the weight table
  (zero residual bytes). All are simple rigid prop meshes (single-bone, weight = 1.0 throughout).
- **Not yet validated:** character body skins (`data/char/skin/`) — likely to exhibit non-zero
  `id_b`, multi-bone weights, and fractional weight values. See Known unknowns.

### Identification

- **Extension:** `.skn`
- **Found in:** `.vfs` archive (logical path pattern: e.g. `data/char/skin/*.skn`,
  `data/item/skin/*.skn`)
- **Magic / signature:** none — file begins immediately with the header fields; no sentinel bytes.
- **Endianness:** little-endian throughout. Confirmed: every multi-byte field parses correctly as LE;
  big-endian interpretation of the same fields produces invalid values.

### String encoding (LenStr) — used in `.skn` and `.bnd`

Both `.skn` and `.bnd` use a length-prefixed string encoding throughout. The on-disk format is:

```
[u32 LE — byte length of the body (4 bytes)]
[char[length] — string body, no null terminator on disk]
```

The length field is a 4-byte unsigned integer in little-endian order. The body is exactly `length`
bytes with no trailing null byte on disk.

**Encoding:** ASCII for the samples examined. Korean class/character names in `data/char/` files
are expected to be encoded as CP949 / EUC-KR (no BOM). No Korean-encoded sample was available;
this is a project-convention assumption, not observed evidence.

> Warning: a prior version of this spec suggested the prefix might be 1 byte. That was incorrect.
> A 1-byte prefix interpretation is definitively disproven by sample evidence: treating the prefix
> as 1 byte misplaces the `face_count` field by 3 bytes and produces a nonsensically large count.
> The prefix is always 4 bytes.

| Confidence |
|---|
| CONFIRMED — 4-byte prefix validated against three samples including one 10-byte name body |

### Header

Appears at offset 0. The `name` field is variable-length (its wire size is `4 + name_length` bytes),
so all sections after the header have no fixed absolute offsets.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `id_a` | Numeric identifier for this mesh object. In item-skin files this matches the numeric suffix of the filename (e.g. `gi213050001.skn` → `id_a = 213050001`). | CONFIRMED |
| 4 | 4 | u32 LE | `id_b` | Bind-pose reference ID. Intended to match the `actor_id` of the associated `.bnd` skeleton. All three item-skin samples carry `id_b = 0`; semantic meaning of non-zero values is UNVERIFIED (see Known unknowns). | CONFIRMED (structure); UNVERIFIED (non-zero semantics) |
| 8 | 4 + N | LenStr | `name` | Length-prefixed name string. Wire format: 4-byte u32 LE length, then N bytes of body with no null terminator. Observed values: `"book"` (N=4), `"seven_gift"` (N=10). | CONFIRMED |

### Face table

Immediately follows the header (after the variable-length `name`). No alignment padding between
sections.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `face_count` | Number of triangles. | CONFIRMED |
| 4 | `face_count × 36` | bytes | `face_data` | Raw block; `face_count` records of 36 bytes each. | CONFIRMED |

**Face record — 36 bytes (3 corners × 12 bytes each):**

Each face record encodes one triangle as three consecutive corner sub-records. Corner stride is
confirmed 12 bytes; face stride is confirmed 36 bytes.

| Sub-offset within corner | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index into the vertex array. Stored as u32 on disk; all sample indices fit within u8 (max observed = 7). | CONFIRMED |
| 4 | 4 | f32 LE | `uv_u` | UV horizontal coordinate. Observed range: 0.011 – 0.990. | CONFIRMED |
| 8 | 4 | f32 LE | `uv_v` | UV vertical coordinate as stored on disk; the engine applies `1.0 − uv_v` when building the render vertex. Observed range: 0.001 – 0.996. | CONFIRMED |

### Vertex table

Immediately follows the face table (no alignment padding).

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

All normal vectors in observed samples have magnitude 1.000000 (within 1e-6 float precision).
Position coordinates in item-skin samples range approximately −1.1 to +1.1 on all axes (small
prop mesh scale).

Parser note: when mapping to a standard vertex buffer (position first), read floats at
sub-offsets 12–23 as position and floats at sub-offsets 0–11 as normal.

### Weight / skin table

Immediately follows the vertex table (no alignment padding).

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `weight_count` | Number of skin-weight records. In all three item-skin samples `weight_count == vertex_count`; this is a degenerate single-bone case. Character skins are expected to have `weight_count > vertex_count`. | CONFIRMED |
| 4 | `weight_count × 12` | bytes | `weight_data` | Raw block; `weight_count` records of 12 bytes each. | CONFIRMED |

**Weight record — 12 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index of the vertex this weight influences. | CONFIRMED |
| 4 | 4 | u32 LE | `bone_index` | Zero-based index into the associated bind-pose bone array. All item-skin samples carry `bone_index = 0`. | CONFIRMED |
| 8 | 4 | f32 LE | `weight` | Influence weight. All item-skin samples carry `weight = 1.0`. Records where `weight < 0.01` are skipped by the loader (threshold from parser analysis; not exercised by available samples). | CONFIRMED (structure); UNVERIFIED (skip threshold — consistent but untestable on available samples) |

Post-load invariant: the engine normalizes weights per vertex so the per-vertex sum equals 1.0.
A zero total weight for any vertex is a fatal assertion in the original client. This invariant
is consistent with available samples (all sums already equal 1.0) but cannot be further exercised
without multi-weight samples.

### End of file

No explicit end-of-file marker. The file ends immediately after the last weight record. All three
samples have exactly zero residual bytes after the weight table.

---

## Format: `.bnd` — binary bind-pose skeleton

### Status

- **sample_verified:** true
- **Samples analysed:** 3 files (`g2036.bnd`, `g2039.bnd`, `g2040.bnd`) from `data/char/bind/`.
  All three are 56 bytes and contain exactly 1 bone each (root-only skeleton, identity transform).
- **Not yet validated:** multi-bone skeletons (`bone_count > 1`). The 36-byte per-bone stride is
  confirmed by parser analysis; multi-bone byte-level cross-check is pending.

### Identification

- **Extension:** `.bnd`
- **Found in:** `.vfs` archive (logical path pattern: e.g. `data/char/bind/*.bnd`)
- **Magic / signature:** none — file begins immediately with the header fields; no sentinel bytes.
- **Endianness:** little-endian throughout.

### Header

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `actor_id` | Numeric ID for this skeleton. Matched against the `id_b` field in `.skn` headers to associate a skin with its skeleton. Typically corresponds to the numeric suffix of the filename (e.g. `g2036.bnd` → `actor_id = 2036`), though this is a naming convention, not a format invariant. | CONFIRMED |
| 4 | 4 + N | LenStr | `actor_name` | Length-prefixed name string. Wire format: 4-byte u32 LE length, then N bytes with no null terminator. This is a free-form human-assigned label and does NOT reliably encode the numeric `actor_id`. Observed: `g2039.bnd` has `actor_id = 2039` but `actor_name = "a123"`. See String encoding in the `.skn` section above. | CONFIRMED |
| after name | 4 | u32 LE | `bone_count` | Number of bone records that follow. The loader reads a full 4-byte u32 but discards the upper three bytes, giving an effective maximum of 255 bones. | CONFIRMED |

Note: a previous revision of this spec described `bone_count` as a 1-byte on-disk field.
That was incorrect. The on-disk representation is a full 4-byte u32 LE; only the low byte is
retained by the loader.

### Bone array

`bone_count` records of **36 bytes each**, read in sequence. No alignment padding between records.

> Note: the in-memory bone object is 72 bytes. The extra 36 bytes beyond the 36 on-disk bytes
> are computed fields filled by post-load passes (parent/child/sibling tree pointers, world-space
> translation, world-space rotation). These fields are never stored on disk. Any parser written
> against a 72-byte on-disk stride will over-read by 36 bytes per bone and must be corrected.

**BndBone on-disk record — 36 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `self_id` | Identifies this bone within the skeleton. The loader retains only the low byte; the upper three bytes are discarded after the read. | CONFIRMED |
| 4 | 4 | u32 LE | `parent_id` | Identifies the parent bone. The loader retains only the low byte. For the root bone this field holds the same value as `self_id` (both zero). | CONFIRMED |
| 8 | 4 | f32 LE | `local_trans_x` | Rest-pose local translation X. | CONFIRMED |
| 12 | 4 | f32 LE | `local_trans_y` | Rest-pose local translation Y. | CONFIRMED |
| 16 | 4 | f32 LE | `local_trans_z` | Rest-pose local translation Z. | CONFIRMED |
| 20 | 4 | f32 LE | `local_rot_x` | Rest-pose local rotation quaternion — X component (stored first). | CONFIRMED |
| 24 | 4 | f32 LE | `local_rot_y` | Quaternion Y component. | CONFIRMED |
| 28 | 4 | f32 LE | `local_rot_z` | Quaternion Z component. | CONFIRMED |
| 32 | 4 | f32 LE | `local_rot_w` | Quaternion W (scalar) component — stored last. | CONFIRMED |

**Total on-disk per bone: 36 bytes.**

All sample bones contain an identity transform: translation (0, 0, 0), quaternion (0, 0, 0, 1).

### Quaternion component order

The local rotation quaternion is stored as four consecutive floats in **XYZW order**: X at
sub-offset +20, Y at +24, Z at +28, W (scalar) at +32. The loader reads the 16 bytes directly
into the in-memory quaternion fields without reordering. XYZW on disk equals XYZW in memory.

| Confidence |
|---|
| CONFIRMED — the loader does not reorder components; confirmed by analysis of the quaternion multiply routine used during world-transform accumulation |

### Root bone sentinel

The root bone is identified by the condition: `self_id` low byte `== 0` and `parent_id` low
byte `== 0`. No other on-disk convention marks the root. Any bone whose `parent_id` low byte
matches no other bone's `self_id` low byte causes a fatal abort in the original client.

| Confidence |
|---|
| CONFIRMED — resolved by analysis of the hierarchy-build routine |

### File trailer

All three validated samples have 4 bytes of `0x00` after the last bone record that are not
consumed by the `.bnd` parser. The parser reads the header and `bone_count × 36` bytes, then
performs post-load hierarchy and world-transform passes and returns. No additional read occurs.

The origin of these 4 trailing bytes is unclear. Three hypotheses are open:

1. **Tool-generation padding** — the export tool appended 4 zero bytes as alignment padding or a
   fencepost marker. Most likely for all-zero values.
2. **Null LenStr sentinel** — a u32 = 0 that would signify "no further records" in a hypothetical
   concatenated-file variant of the format.
3. **Checksum** — a XOR or byte-sum checksum; the value is 0 for identity-only bones, so a
   checksum cannot be ruled out from these samples alone.

**Recommended parser behaviour:** read `bone_count × 36` bytes after the header. Tolerate and
silently skip up to 4 trailing bytes if the end of the bone array does not align with EOF. Do not
interpret the trailing bytes as a bone record or as a required field.

| Confidence |
|---|
| CONFIRMED present in all 3 samples; semantics UNVERIFIED |

---

## Enumerations / flags

None identified in these formats.

---

## Known unknowns

- **`.xobj` `slot_id` (token 1):** the discard behaviour is confirmed; the value is confirmed to
  match the filename numeric prefix. Whether a non-matching `slot_id` is an error or is
  accepted silently is unknown (the parser never validates it).
- **`.xobj` intra-vertex delimiter:** tab-delimited in all observed samples. Whether the parser
  accepts any whitespace as a delimiter, or strictly requires tab, is unconfirmed. The parser is
  described as a general whitespace tokenizer, suggesting tab is not mandatory.
- **`.xobj` maximum index:** u16 ceiling is 65535; no sample has more than 13 vertices.
  Off-by-one behaviour at the u16 boundary is untested.
- **`.skn` `id_b` non-zero semantics:** all item-skin samples carry `id_b = 0`. The non-zero
  case (expected in character body skins referencing a skeleton) has not been confirmed against
  real character skin samples.
- **`.skn` multi-bone weight records:** all item-skin samples have one weight per vertex
  (`weight_count == vertex_count`, `bone_index = 0`, `weight = 1.0`). Fractional blend weights
  across multiple bones are expected in character skins but not yet sample-verified.
- **`.skn` weight skip threshold:** the parser is known to skip records where `weight < 0.01`.
  This threshold value comes from parser analysis; no sample exercises it (all weights are 1.0).
- **`.skn` name field CP949 encoding:** item-skin names in available samples are pure ASCII.
  Korean-encoded names are expected in character skins; no sample was available.
- **`.bnd` `actor_id` upper 3 bytes:** the parser stores the full u32 `actor_id`. Whether the
  upper bytes carry meaning or are always zero in practice is unknown.
- **`.bnd` multi-bone skeletons:** the 36-byte stride per bone is confirmed by parser analysis
  but not yet cross-checked against a sample with `bone_count > 1`.
- **`.bnd` trailing 4 bytes:** three hypotheses remain open (tool padding, null sentinel,
  checksum). The semantics cannot be determined from the available single-bone samples.
- **`.bnd` `actor_name` CP949:** all three samples use ASCII names. Korean character class names
  are expected; no such sample was available.
- **`.skn` / `.bnd` `self_id` / `parent_id` upper bytes:** the loader discards the upper three
  bytes after reading the full u32. Whether these bytes are always zero in real files, or whether
  they carry information consumed by other code paths, is not confirmed.

---

## Cross-references

- Related formats: `formats/pak.md` (container), `formats/texture.md` (co-referenced assets)
- Canonical names: see `Docs/RE/names.yaml` (`XobjFile`, `SkinFile`, `BindPoseFile`, `SknFace`,
  `SknCorner`, `SknVertex`, `SknWeight`, `BndBone`)
- Provenance: see `Docs/RE/journal.md`
