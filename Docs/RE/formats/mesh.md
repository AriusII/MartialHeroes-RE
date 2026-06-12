# Format: .xobj / .skn / .bnd  (geometry and skeleton assets)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

## Overview

Three file types carry geometry and skeletal data. All are opened through the VFS chokepoint
(see `formats/pak.md`).

| Extension | Style | Role | Sample verified |
|---|---|---|---|
| `.xobj` | ASCII whitespace-tokenized | Static triangle mesh; used for scene objects and editor primitives | YES — 3 samples (plane, cone, triangle) |
| `.skn` | Binary, little-endian | Skinned character mesh with per-vertex bone weights | YES — 3 item-skin samples (rigid); 1,140 multi-weight character skins census-verified |
| `.bnd` | Binary, little-endian | Bind-pose skeleton: bone hierarchy and rest transforms | YES — 3 single-bone samples; 349-file census (incl. 84–89-bone player rigs) |

> **The math that consumes `.skn` + `.bnd` + `.mot`** — linear-blend skinning, the load-time
> inverse-bind bake, pose composition, and the quaternion / handedness conventions — is specified in
> `specs/skinning.md`. This file documents the on-disk bytes; `specs/skinning.md` documents how they
> are deformed and animated.

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
- **Samples analysed (byte-level):** 3 item-skin files (`gi213050001.skn`, `gi213062382.skn`,
  `gi292020105.skn`) from `data/item/skin/`. All three exhausted exactly at the predicted end of the
  weight table (zero residual bytes). All are simple rigid prop meshes (single-bone, weight = 1.0).
- **Census-verified (corpus):** the full corpus of **2,786 `.skn` files** parses with 0 errors.
  **1,140** are multi-bone / multi-weight skins (true weighted blending, not rigid). The 1,269 files
  under `data/char/skin/` all carry **`id_b != 0`** (every character body skin references a skeleton).
  See §Multi-bone weighted skinning below.

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

**Encoding:** ASCII for the item samples examined. Korean class/character names in `data/char/` files
are expected to be encoded as CP949 / EUC-KR (no BOM). The census confirms character skins exist in
quantity; embedded names in `data/char/bind/` include both ASCII labels and source-path fragments.

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
| 4 | 4 | u32 LE | `id_b` | **Bind-pose reference ID (= SkinClassId).** Selects the `.bnd` skeleton this skin binds to: the loader resolves the bind pose whose `actor_id` equals `id_b`. Item-skin samples carry `id_b = 0` (rigid, no skeleton). For character body skins `id_b != 0` is **universal** (all 1,269 `data/char/skin/` files), and the set of distinct `id_b` values is exactly the set of `.bnd` `actor_id` values — a confirmed bijection (see §id_b ↔ skeleton bijection). | CONFIRMED (structure); CONFIRMED (non-zero bijection, census-verified) |
| 8 | 4 + N | LenStr | `name` | Length-prefixed name string. Wire format: 4-byte u32 LE length, then N bytes of body with no null terminator. Observed item values: `"book"` (N=4), `"seven_gift"` (N=10). | CONFIRMED |

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
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index into the vertex array. Stored as u32 on disk; in item samples all indices fit within u8 (max observed = 7). | CONFIRMED |
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
prop mesh scale); character skins span the full body extent.

Parser note: when mapping to a standard vertex buffer (position first), read floats at
sub-offsets 12–23 as position and floats at sub-offsets 0–11 as normal.

> Render-vertex note (cross-reference): the loader builds a deduplicated 32-byte render vertex
> (position[0..11], normal[12..23], uv[24..31]) from the 24-byte disk vertex plus the per-corner UVs,
> merging shared corners by position. The skinning math operates on those deduplicated render
> vertices — see `specs/skinning.md` §2.

### Weight / skin table

Immediately follows the vertex table (no alignment padding).

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `weight_count` | Number of skin-weight records. In item-skin samples `weight_count == vertex_count` (degenerate single-bone case). In character skins `weight_count > vertex_count` — census shows averages from ~2.6 up to exactly 4.0 weights per vertex (see §Multi-bone weighted skinning). | CONFIRMED |
| 4 | `weight_count × 12` | bytes | `weight_data` | Raw block; `weight_count` records of 12 bytes each. | CONFIRMED |

**Weight record — 12 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index of the vertex this weight influences. | CONFIRMED |
| 4 | 4 | u32 LE | `bone_index` | **Bone ID** — addresses the bind-pose bone by `id − base_id` (see §Bone addressing), NOT a palette slot, NOT an indirection-table index, NOT a `.mot` track index. Item-skin samples carry `bone_index = 0` (the root); character skins carry many distinct bone IDs (up to 242 distinct bones in one census skin). | CONFIRMED |
| 8 | 4 | f32 LE | `weight` | Influence weight. Item-skin samples carry `weight = 1.0`. **Records where `weight < 0.01` are skipped by the loader** — SAMPLE-VERIFIED: the minimum observed weight across the multi-weight corpus is exactly **0.010** (records at 0.010 are kept; the threshold drops anything below 0.010). | CONFIRMED (structure); CONFIRMED (skip threshold, census-verified) |

Post-load invariant: the engine **normalizes weights per vertex so the per-vertex sum equals 1.0**,
then splits each vertex's influences into a single dominant (MAJOR) influence and the remainder
(MINOR). A zero total weight for any vertex is a fatal assertion in the original client. The deform
math that consumes these normalized influences is specified in `specs/skinning.md` §5.

### Multi-bone weighted skinning (census-verified)

> Earlier revisions marked multi-bone fractional skinning as UNVERIFIED, because the only byte-level
> samples were rigid single-bone item props. The corpus census resolves this: **true weighted
> multi-bone skinning is present and common.**

| Finding | Evidence | Status |
|---|---|---|
| Multi-bone / multi-weight skins exist in quantity | 1,140 of 2,786 `.skn` files are multi-bone or multi-weight-per-vertex | SAMPLE-VERIFIED |
| `weight_count > vertex_count` for character skins | e.g. ~5,066 weights for ~1,950 vertices (≈2.6 wt/vert); newer skins hit exactly 4.0 wt/vert (e.g. 23,236 weights / 5,809 verts) | SAMPLE-VERIFIED |
| Fractional weights down to the skip boundary | minimum observed weight = 0.010 exactly; records below 0.01 are dropped | SAMPLE-VERIFIED |
| Many distinct bones per skin | up to 242 distinct bone IDs referenced by one skin's weight records | SAMPLE-VERIFIED |
| Weights address bones by **bone ID**, no indirection | the `bone_index` field is a bone ID resolved by `id − base_id`; there is no palette/indirection table between weight and bone | CONFIRMED (code) |

Two weight-distribution styles appear: an older variable-influence style (~2.6 influences/vertex) and
a newer fixed-4-influence style (exactly 4.0 weights/vertex). Both are multi-bone weighted skinning;
neither is rigid. An importer must therefore support an **arbitrary** influence count per vertex (then
re-normalize if it must cap to its engine's limit — see `specs/skinning.md` §5.4).

### Bone addressing (shared with `.bnd` and `.mot`)

A weight record's `bone_index` is a **bone ID**, resolved against the bound `.bnd` skeleton by
`bone_array[bone_index − base_id]`, where `base_id` is the first bone's `self_id`. This is the same
addressing used by `.mot` track `bone_id`s (see `formats/animation.md` §Bone-track linkage). For the
recovered sample skeletons `base_id == 0`, so ID equals array index — but a parser **must not assume**
`base_id == 0` in general. Full discussion: `specs/skinning.md` §3.

### id_b ↔ skeleton bijection (census-verified)

Every character body skin (`data/char/skin/`) carries a non-zero `id_b`, and the set of distinct
`id_b` values across all character skins is **exactly the set of `.bnd` `actor_id` values** — **349
distinct `id_b` values matching 349 `.bnd` files**, a confirmed one-to-one correspondence. This makes
`id_b` the reliable skin→skeleton link: to find a character skin's skeleton, load
`data/char/bind/g{id_b}.bnd`. (`id_b = 0` means rigid / no skeleton, used by item props.) SAMPLE-VERIFIED.

### End of file

No explicit end-of-file marker. The file ends immediately after the last weight record. All three
item samples have exactly zero residual bytes after the weight table.

---

## Format: `.bnd` — binary bind-pose skeleton

### Status

- **sample_verified:** true
- **Samples analysed (byte-level):** 3 files (`g2036.bnd`, `g2039.bnd`, `g2040.bnd`) from
  `data/char/bind/`. All three are 56 bytes and contain exactly 1 bone each (root-only skeleton,
  identity transform).
- **Census-verified (corpus):** the full corpus of **349 `.bnd` files** parses with 0 errors.
  **327** are multi-bone (`bone_count > 1`), the typical character skeleton having 31–80 bones. The
  **player rigs** are `g1.bnd`–`g4.bnd` (84 / 87 / 82 / 89 bones; embedded labels `M_musa`, `salsu`,
  `dosa`, `Monk` — the base playable classes). The largest census skeleton is 244 bones.

### Identification

- **Extension:** `.bnd`
- **Found in:** `.vfs` archive (logical path pattern: e.g. `data/char/bind/*.bnd`)
- **Magic / signature:** none — file begins immediately with the header fields; no sentinel bytes.
- **Endianness:** little-endian throughout.

### Header

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `actor_id` | Numeric ID for this skeleton. Matched against the `id_b` field in `.skn` headers to associate a skin with its skeleton (see §id_b ↔ skeleton bijection). Typically corresponds to the numeric suffix of the filename (e.g. `g2036.bnd` → `actor_id = 2036`), though this is a naming convention, not a format invariant. | CONFIRMED |
| 4 | 4 + N | LenStr | `actor_name` | Length-prefixed name string. Wire format: 4-byte u32 LE length, then N bytes with no null terminator. This is a free-form human-assigned label and does NOT reliably encode the numeric `actor_id`. Observed: `g2039.bnd` has `actor_id = 2039` but `actor_name = "a123"`; player rigs carry labels like `"M_musa(84)"`. See String encoding in the `.skn` section above. | CONFIRMED |
| after name | 4 | u32 LE | `bone_count` | Number of bone records that follow. The loader reads a full 4-byte u32 but discards the upper three bytes, giving an effective maximum of 255 bones. SAMPLE-VERIFIED across 349 files: all observed bone counts fit in one byte (max 244), consistent with the low-byte-only semantic. | CONFIRMED |

Note: a previous revision of this spec described `bone_count` as a 1-byte on-disk field.
That was incorrect. The on-disk representation is a full 4-byte u32 LE; only the low byte is
retained by the loader.

### Bone array

`bone_count` records of **36 bytes each**, read in sequence. No alignment padding between records.

> Note: the in-memory bone object is 72 bytes. The extra 36 bytes beyond the 36 on-disk bytes
> are computed fields filled by post-load passes (parent/child/sibling tree pointers, world-space
> translation, world-space rotation). These fields are never stored on disk. Any parser written
> against a 72-byte on-disk stride will over-read by 36 bytes per bone and must be corrected.
> The post-load world-transform accumulation (parent-relative locals → world) is specified in
> `specs/skinning.md` §3.

**BndBone on-disk record — 36 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `self_id` | Identifies this bone within the skeleton. The loader retains only the low byte; the upper three bytes are discarded after the read. This ID is the link target for `.skn` weight `bone_index` and `.mot` track `bone_id` (both resolve `id − base_id`). | CONFIRMED |
| 4 | 4 | u32 LE | `parent_id` | Identifies the parent bone. The loader retains only the low byte. For the root bone this field holds the same value as `self_id` (both zero). | CONFIRMED |
| 8 | 4 | f32 LE | `local_trans_x` | Rest-pose **parent-relative (local)** translation X. The bind WORLD transform is computed at load by accumulating down the tree (`specs/skinning.md` §3). | CONFIRMED |
| 12 | 4 | f32 LE | `local_trans_y` | Rest-pose local translation Y. | CONFIRMED |
| 16 | 4 | f32 LE | `local_trans_z` | Rest-pose local translation Z. | CONFIRMED |
| 20 | 4 | f32 LE | `local_rot_x` | Rest-pose local rotation quaternion — X component (stored first). | CONFIRMED |
| 24 | 4 | f32 LE | `local_rot_y` | Quaternion Y component. | CONFIRMED |
| 28 | 4 | f32 LE | `local_rot_z` | Quaternion Z component. | CONFIRMED |
| 32 | 4 | f32 LE | `local_rot_w` | Quaternion W (scalar) component — stored last. | CONFIRMED |

**Total on-disk per bone: 36 bytes.**

The single-bone sample bones contain an identity transform: translation (0, 0, 0), quaternion
(0, 0, 0, 1). Multi-bone character rigs carry non-identity parent-relative transforms.

### Quaternion component order

The local rotation quaternion is stored as four consecutive floats in **XYZW order**: X at
sub-offset +20, Y at +24, Z at +28, W (scalar) at +32. The loader reads the 16 bytes directly
into the in-memory quaternion fields without reordering. XYZW on disk equals XYZW in memory. The
same XYZW order is used by `.mot` keyframe rotations (`formats/animation.md`) and by the skinning
math (`specs/skinning.md` §7).

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

All three validated single-bone samples have 4 bytes of `0x00` after the last bone record that are
not consumed by the `.bnd` parser. The parser reads the header and `bone_count × 36` bytes, then
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
| CONFIRMED present in all 3 single-bone samples; semantics UNVERIFIED |

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
- **`.skn` name field CP949 encoding:** item-skin names in available byte-level samples are pure
  ASCII. Korean-encoded names are expected in some character skins; no Korean-encoded sample byte
  block has been inspected.
- **`.skn` / `.bnd` `self_id` / `parent_id` upper bytes:** the loader discards the upper three
  bytes after reading the full u32. Whether these bytes are always zero in real files, or whether
  they carry information consumed by other code paths, is not confirmed (census bone counts and IDs
  all fit in the low byte, consistent with the discard).
- **`.bnd` `actor_id` upper 3 bytes:** the parser stores the full u32 `actor_id`. Whether the
  upper bytes carry meaning or are always zero in practice is unknown.
- **`.bnd` trailing 4 bytes:** three hypotheses remain open (tool padding, null sentinel,
  checksum). The semantics cannot be determined from the available single-bone samples; the
  multi-bone trailer has not been byte-inspected.
- **`.bnd` multi-bone byte-level cross-check:** the 36-byte stride per bone is confirmed by parser
  analysis and the corpus census parses cleanly, but a full byte-level offset cross-check on a
  multi-bone rig (e.g. the §canonical player trio in `specs/skinning.md`) is the recommended next
  validation step.
- **`.skn` per-vertex influence cap:** the format imposes no fixed cap; the corpus shows both
  variable (~2.6) and fixed (4.0) influence styles. Whether the engine enforces a maximum at load
  is not confirmed (it sums all influences). Importers capping to an engine limit must re-normalize.

### Resolved items (removed from the unknowns above)

| Former item | Resolution |
|---|---|
| `.skn` `id_b` non-zero semantics | RESOLVED — `id_b` is the SkinClassId / bind-pose reference; non-zero is universal for character skins and the distinct `id_b` set is a bijection with the 349 `.bnd` `actor_id`s (§id_b ↔ skeleton bijection). |
| `.skn` multi-bone weight records | RESOLVED — 1,140 multi-bone/multi-weight skins census-verified (§Multi-bone weighted skinning). |
| `.skn` weight skip threshold | RESOLVED — minimum observed weight = 0.010; records below 0.01 are dropped (SAMPLE-VERIFIED). |
| Weights addressing bones by index vs ID | RESOLVED — `bone_index` is a **bone ID** resolved by `id − base_id`, no indirection table (§Bone addressing). |
| `.bnd` multi-bone skeletons exist | RESOLVED — 327 multi-bone `.bnd` files; player rigs g1–g4 (82–89 bones); largest 244 bones. |

---

## Cross-references

- Related formats: `formats/pak.md` (container), `formats/animation.md` (`.mot` clips that drive
  these skeletons), `formats/texture.md` (co-referenced assets)
- Deform / skinning math: `specs/skinning.md` (linear-blend skinning, inverse-bind bake, pose
  composition, quaternion/handedness conventions, Godot import guidance, canonical test specimens)
- Canonical names: see `Docs/RE/names.yaml` (`XobjFile`, `SkinFile`, `BindPoseFile`, `SknFace`,
  `SknCorner`, `SknVertex`, `SknWeight`, `BndBone`)
- Provenance: see `Docs/RE/journal.md`
