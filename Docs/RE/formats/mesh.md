# Format: .xobj / .skn / .bnd / .bud  (geometry, skeleton and building mesh assets)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.

<!--
verification: sample-verified; CYCLE 14 re-anchor (f61f66a9): 2 facts re-confirmed SAME, 0 corrected
ida_reverified: 2026-06-24; 2026-06-27
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida, vfs-sample]
conflicts: none
bud_coverage: static-analysis only (no VFS sample walked); ida_anchor: f61f66a9; evidence: [static-ida]
bud_open_questions: type-byte semantics, UV v-flip — see Known Unknowns (LOD/decimated-buffer and lod_class items resolved by wave-3 struct reconciliation; see Resolved items)
wave3_reconciliation: 2026-06-28 — §In-memory BudObject upper half corrected against Docs/RE/structs/bud_object.md (authoritative for the full 116-byte layout): lod_class→anim_class, lod_vertex_buffer→anim_vertex_buffer, lod_step→sway_amp, lod_factor→sway_vfactor, far_lod_flag→near_far_flag; LOD/decimated-buffer construction and lod_class-source-table Known Unknowns resolved; Grid build step 3 and Draw passes sections updated accordingly.
deep_cartography_pass: 2026-06-29 — centroid +104 corrected from AABB midpoint to vertex-buffer normal-slot formula; near_far_flag renamed flag_60 (role unverified in foliage cluster); draw constants table updated; per-frame cull section annotated; Resolved items table extended.
-->

> **Verification banner (`xobj`/`skn`/`bnd`).** `sample-verified` · `ida_reverified: 2026-06-24; 2026-06-27` ·
> `ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` ·
> `evidence: [static-ida, vfs-sample]` · `conflicts: none`. Re-verified two-witness against build
> `263bd994` (the `.skn` / `.bnd` byte parsers in the loader chain) AND a real VFS sample: a rigid
> item skin (`data/item/skin/*.skn`), a weighted character skin (`data/char/skin/*.skn`), a player
> rig (`data/char/bind/g1.bnd`), and `data/char/bindlist.txt`. Every prior offset, stride, count and
> rule re-confirmed exactly; the `.bnd` documentation is unchanged in substance and gained three
> corroborating refinements (2026-06-21): the single-bone `g2036`/`g2039`/`g2040` samples are
> reclassified into the +4-trailer group (52-byte payload in a 56-byte file); the per-bone disk order
> is restated as the literal parser read order (self → parent → 12-byte translation → 16-byte
> quaternion); and the former "multi-bone byte-level cross-check" Known Unknown is resolved on the
> 84-bone `g1.bnd` rig. None contradicts a prior fact. Third-pass corroboration (2026-06-24): two
> additional byte-level `.skn` samples — `gi201011001.skn` (2593 bytes; id_b=0, 46 faces / 25
> vertices / 25 weights, rigid, single-bone weight 1.0, EOF residual 0) and `g200002620.skn` (84083
> bytes; id_b=3045, 1432 faces / 786 vertices / 1136 weights, multi-bone, EOF residual 0) — walked
> end-to-end against the loader; zero conflicts with any committed fact. Tier policy: a fact also
> matched against the real VFS sample is `[sample-verified]`; a loader-control-flow-only fact is
> `[confirmed]`. CYCLE 14 re-anchor (f61f66a9, 2026-06-27): `.skn` body strides (face 36 B, vertex
> 24 B, weight 12 B), disk vertex NORMAL-first order, weight drop threshold (~0.01), per-vertex
> normalize-to-1.0, render-vertex field copy order, `.bnd` `actor_id`/`bone_count`/36-byte bone
> record layout, and hierarchy build pass all re-confirmed SAME against build `f61f66a9`
> (static-ida); no corrections. The `.bud` building-mesh section added in this revision is **static-analysis only** (no VFS sample walked); its facts carry `[confirmed]` confidence from the loader code and do not affect the `sample-verified` status of `.xobj`/`.skn`/`.bnd`.

## Overview

Five file types carry geometry, skeletal, and building mesh data. All are opened through the VFS
chokepoint (see `formats/pak.md`).

| Extension | Style | Role | Sample verified |
|---|---|---|---|
| `.xobj` | ASCII whitespace-tokenized | Static triangle mesh; used for scene objects and editor primitives | YES — 3 samples (plane, cone, triangle) |
| `.skn` | Binary, little-endian | Skinned character mesh with per-vertex bone weights | YES — 3 item-skin samples (rigid); 1,140 multi-weight character skins census-verified |
| `.bnd` | Binary, little-endian | Bind-pose skeleton: bone hierarchy and rest transforms | YES — 3 single-bone samples; 349-file census (incl. 84–89-bone player rigs) |
| `.bud` | Binary, little-endian | Static building / prop geometry; world-absolute vertices; no per-object transform | NO — static-analysis only; no VFS sample byte-walked |
| `.map` | ASCII token grammar | Per-cell layout descriptor; references `.bud`, `.ted`, `.exd`, `.upd`, `.sod` and FX layers | N/A (text format) |

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

- **sample_verified:** true · **ida_anchor:** `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` · **ida_reverified:** 2026-06-24
- **Samples analysed (byte-level):** 3 rigid item-skin files (`gi213050001.skn`, `gi213062382.skn`,
  `gi292020105.skn`) from `data/item/skin/`. All three exhausted exactly at the predicted end of the
  weight table (zero residual bytes). All are simple rigid prop meshes (single-bone, weight = 1.0).
  Third-pass (2026-06-24): `gi201011001.skn` (2593 bytes; id_b=0, 46 faces / 25 vertices / 25
  weights, EOF residual 0) and `g200002620.skn` (84083 bytes; id_b=3045, 1432 faces / 786 vertices /
  1136 weights across 52 distinct bones, EOF residual 0) — both walked end-to-end, zero conflicts.
- **Re-verified two-witness (build `263bd994`):** the byte parser was walked in the loader (header →
  face table → vertex table → weight table, each as a `count` u32 followed by `count × stride` raw
  bytes via the bulk slurp), AND re-checked against two fresh VFS samples — a **rigid item skin**
  (46 faces / 25 vertices / 25 weights, 1 bone, weight 1.0) and a **weighted character skin**
  (1,432 faces / 786 vertices / 1,136 weights across **52 distinct bones**, ≈1.45 weights/vertex,
  `id_b` = a real sparse `actor_id`). Both samples exhaust to EOF with **zero residual**, and every
  weight/corner `vertex_index` lands in range. The 36-byte face, 24-byte vertex and 12-byte weight
  strides each walk exactly to the next section. All re-confirmed; no drift.
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

> **Binary-mode witness (why the prefix is 4 raw bytes).** The asset-stream primitives that read
> `.skn` and `.bnd` are **dual-mode**: each branches on a per-stream mode bit (a single flag bit in
> the stream object's state word — the `& 8` mode flag). When the bit is **set** the stream is in
> TEXT mode (line-parse + decimal-string conversion, as used for `.xobj` and the `.txt` tables); when
> the bit is **clear** the stream is in BINARY mode (a raw 4-byte little-endian read). `.skn` and
> `.bnd` are opened in BINARY mode, so every "u32" below — including each LenStr length prefix — is a
> raw 4-byte read, never a parsed decimal string. This is the loader-side proof behind the 4-byte
> (not 1-byte) prefix warning: the same primitives that text-parse `.xobj` raw-read `.skn`/`.bnd`.
> | Confidence: [confirmed] — the mode branch is in the shared asset-stream reader; the binary branch
> drives both `.skn` and `.bnd`.

**Encoding:** ASCII for the item samples examined. Korean class/character names in `data/char/` files
are expected to be encoded as CP949 / EUC-KR (no BOM). The census confirms character skins exist in
quantity; embedded names in `data/char/bind/` include both ASCII labels and source-path fragments.

> Warning: a prior version of this spec suggested the prefix might be 1 byte. That was incorrect.
> A 1-byte prefix interpretation is definitively disproven by sample evidence: treating the prefix
> as 1 byte misplaces the `face_count` field by 3 bytes and produces a nonsensically large count.
> The prefix is always 4 bytes.

| Confidence |
|---|
| [sample-verified] — 4-byte prefix two-witness re-confirmed on build `263bd994`: the binary-mode reader takes a raw 4-byte length, and on a real VFS rigid item skin (name length 13) and a weighted character skin (name length 11) the 4-byte-prefix walk lands every later face/vertex/weight stride exactly, exhausting both files with zero residual. A 1-byte prefix misplaces `face_count` by 3 bytes. |

### Header

Appears at offset 0. The `name` field is variable-length (its wire size is `4 + name_length` bytes),
so all sections after the header have no fixed absolute offsets.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `id_a` | Numeric identifier for this mesh object. In item-skin files this matches the numeric suffix of the filename (e.g. `gi213050001.skn` → `id_a = 213050001`). | CONFIRMED |
| 4 | 4 | u32 LE | `id_b` | **Skeleton pointer — an `actor_id`, NOT a small "349-valued index".** This field's value is a `.bnd` skeleton `actor_id`: the loader resolves the bind pose whose `actor_id` equals `id_b`. The value is a **sparse `actor_id` in the range 1..8892** (`id_b = 0` = no skeleton, rigid). It is **not** a dense 0..348 index — there are exactly 349 registered skeletons, but their `actor_id`s are spread sparsely across the 1..8892 span, so a parser must resolve `id_b` by `actor_id` lookup against the bindlist registry and must NOT treat it as an array index into a 349-slot table. Item-skin samples carry `id_b = 0`. For character body skins `id_b != 0` is **universal** (all 1,269 `data/char/skin/` files); across the whole corpus 350 distinct `id_b` values occur (including 0), and the 349 non-zero values are exactly the 349 `.bnd` `actor_id`s — a confirmed bijection (see §id_b ↔ skeleton bijection). **Do NOT conflate this field with the `skin.txt` `col2` class tag.** That is a separate, 6-value outfit / class-family tag that lives in `skin.txt`, not in the `.skn` mesh; it indexes a base-offset table and never selects a skeleton. The `.skn` `id_b` is the `actor_id` skeleton pointer; the `skin.txt` `col2` is the 6-value class tag — two distinct fields that happen to share the legacy label "IdB". Re-verified on build `263bd994`: the rigid item sample carries `id_b = 0` (no skeleton) and a fresh weighted character sample carries a non-zero `id_b` that is a sparse `actor_id` (a value well inside 1..8892, not a 0..348 subscript) — both regimes observed together. | [sample-verified] (structure; `id_b = 0` rigid AND non-zero sparse `actor_id` both observed on build `263bd994`); CONFIRMED (actor_id 1..8892, not a 349-index — two-witness); CONFIRMED (two distinct fields — skeleton pointer vs class tag); CONFIRMED (non-zero bijection, census-verified) |
| 8 | 4 + N | LenStr | `name` | Length-prefixed name string. Wire format: 4-byte u32 LE length, then N bytes of body with no null terminator. Observed item values: `"book"` (N=4), `"seven_gift"` (N=10). | CONFIRMED |

> **Naming disambiguation (firewall note).** The legacy label "IdB" was historically applied to BOTH
> the `.skn` `id_b` skeleton pointer documented above AND the `skin.txt` `col2` class tag. These are
> two distinct fields with two distinct value domains (`actor_id` skeleton keys in 1..8892 vs 6 class tags) and two
> distinct consumers (skeleton resolution vs a base-offset table). This spec names the `.skn` field
> `id_b` exclusively as the skeleton pointer. The canonical glossary split of these two meanings is
> owned by Tier-1 in `Docs/RE/names.yaml`; this spec does not edit that glossary.

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

> **Normal-first disk order — two-witness (build `263bd994`).** The render-vertex assembly copies
> disk float indices 3/4/5 into the render *position* slot and disk indices 0/1/2 into the render
> *normal* slot — i.e. the disk layout is normal then position. The sample witness: in BOTH fresh VFS
> samples the **leading float triple of the first vertex has magnitude exactly 1.00000**, which only
> the unit surface normal (never an arbitrary position) can have. Disk order = normal (0..11), then
> position (12..23). | Confidence: [sample-verified].

Parser note: when mapping to a standard vertex buffer (position first), read floats at
sub-offsets 12–23 as position and floats at sub-offsets 0–11 as normal.

> Handedness note (cross-reference, NOT an on-disk transform): positions are stored verbatim in
> the file's own coordinate frame. The project's **mesh-local X-negation** for `.skn` geometry is an
> importer/render-domain handedness conversion applied downstream — it is not applied by the byte
> reader. Apply it (and the world Z-negation) once, at the importer layer; see `specs/skinning.md`.
> The `Assets.Parsers` byte decoder must NOT pre-negate any axis.

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
| 8 | 4 | f32 LE | `weight` | Influence weight. Item-skin samples carry `weight = 1.0`. **Records where `weight < 0.01` are skipped by the loader** — the keep test is `weight >= ~0.01` (a single-precision near-`0.01` constant), so records at or above `0.01` are kept and anything below is dropped. SAMPLE-VERIFIED two ways: the corpus minimum observed weight is exactly **0.010**, and a fresh weighted character skin on build `263bd994` carries a minimum weight of **0.011377** — both ≥ the threshold, both kept. | CONFIRMED (structure); [sample-verified] (skip threshold — keep-constant + two distinct min-weight observations agree) |

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
| `weight_count > vertex_count` for character skins | e.g. ~5,066 weights for ~1,950 vertices (≈2.6 wt/vert); newer skins hit exactly 4.0 wt/vert (e.g. 23,236 weights / 5,809 verts); a fresh sample on build `263bd994` shows an *intermediate* ≈1.45 wt/vert (1,136 weights / 786 verts, 52 bones) | SAMPLE-VERIFIED |
| Fractional weights down to the skip boundary | minimum observed weight = 0.010 exactly; records below 0.01 are dropped | SAMPLE-VERIFIED |
| Many distinct bones per skin | up to 242 distinct bone IDs referenced by one skin's weight records | SAMPLE-VERIFIED |
| Weights address bones by **bone ID**, no indirection | the `bone_index` field is a bone ID resolved by `id − base_id`; there is no palette/indirection table between weight and bone | CONFIRMED (code) |

Multiple weight-distribution densities appear: an older variable-influence style (~2.6
influences/vertex), a newer fixed-4-influence style (exactly 4.0 weights/vertex), and intermediate
densities in between — a fresh build-`263bd994` sample lands at ≈1.45 weights/vertex (786 verts /
1,136 weights / 52 bones). All are multi-bone weighted skinning; none is rigid. An importer must
therefore support an **arbitrary** influence count per vertex (then re-normalize if it must cap to
its engine's limit — see `specs/skinning.md` §5.4).

### Bone addressing (shared with `.bnd` and `.mot`)

A weight record's `bone_index` is a **bone ID**, resolved against the bound `.bnd` skeleton by
`bone_array[bone_index − base_id]`, where `base_id` is the first bone's `self_id`. This is the same
addressing used by `.mot` track `bone_id`s (see `formats/animation.md` §Bone-track linkage). For the
recovered sample skeletons `base_id == 0`, so ID equals array index — but a parser **must not assume**
`base_id == 0` in general. Full discussion: `specs/skinning.md` §3.

### id_b ↔ skeleton bijection (census-verified)

Every character body skin (`data/char/skin/`) carries a non-zero `id_b`, and the set of distinct
`id_b` values across all character skins is **exactly the set of `.bnd` `actor_id` values** — **349
distinct non-zero `id_b` values matching 349 `.bnd` files** (the corpus shows 350 distinct `id_b`
values in total, the extra value being `0`), a confirmed one-to-one correspondence. The matching
`actor_id`s are **sparse over the range 1..8892**, not a dense 0..348 index. This makes `id_b` the
reliable skin→skeleton link: to find a character skin's skeleton, resolve the registered `.bnd`
whose `actor_id` equals `id_b` (the bindlist registry holds all 349 such entries — see
`formats/bindlist.md`); never use `id_b` as an array subscript into a 349-slot table.
(`id_b = 0` means rigid / no skeleton, used by item props.) CONFIRMED — two-witness (loader read
order + black-box census of all 2,786 `.skn` files, zero residual).

> **`id_b` is the skeleton pointer, not the class tag.** The `id_b` `actor_id` domain documented here
> (sparse over 1..8892, 349 non-zero values) is the skeleton-selection domain. It must not be confused with the 6-value `skin.txt` `col2` class
> tag (a separate field, a separate file, a separate consumer). Both were historically labelled "IdB";
> they are split into two distinct fields. See the Header naming-disambiguation note above.

### End of file

No explicit end-of-file marker. The file ends immediately after the last weight record. All three
item samples have exactly zero residual bytes after the weight table.

---

## Format: `.bnd` — binary bind-pose skeleton

### Status

- **sample_verified:** true · **ida_anchor:** `263bd994` · **ida_reverified:** 2026-06-16
- **Samples analysed (byte-level):** 3 files (`g2036.bnd`, `g2039.bnd`, `g2040.bnd`) from
  `data/char/bind/`. All three are 56 bytes and contain exactly 1 bone each (root-only skeleton,
  identity transform). Byte arithmetic re-confirmed: header `actor_id`(4) + `name_len`(4) +
  `actor_name`(N) + `bone_count`(4) plus one 36-byte bone record accounts for 52 bytes, so the
  56-byte file size means **all three carry the +4 trailing zero block** — they belong to the
  "236 files with +4 trailer" group, NOT the "113 exact-end" group (see §File trailer).
- **Re-verified two-witness (build `263bd994`):** the byte parser (header → bone records →
  hierarchy build) was walked in the loader and re-checked against a fresh **player rig** sample
  (`g1.bnd`). That sample reads `actor_id = 1`, an ASCII `actor_name` LenStr of length 10, and a
  `bone_count` whose **low byte is 84 with the upper three bytes zero** (an 84-bone player rig). Its
  84 bone records of 36 bytes each (3,024 bytes) walk **exactly** to the bone-array end, after which
  the file carries a **+4 trailing block of zero bytes** (one of the 236/349 files that do — see
  §File trailer). The root bone reads `self_id` low 0 / `parent_id` low 0 (root sentinel) with an
  identity rest quaternion whose W component at sub-offset +32 ≈ 1.0000, confirming XYZW order on a
  real bone. All re-confirmed; no drift.
- **Census-verified (corpus):** the full corpus of **349 `.bnd` files** parses with 0 errors —
  **349 distinct `actor_id`s, sparse over the range 1..8892** (matching the 349 `.skn` `id_b`
  skeleton pointers, see §id_b ↔ skeleton bijection). **327** are multi-bone (`bone_count > 1`), the
  typical character skeleton having 31–80 bones. The **player rigs** are `g1.bnd`–`g4.bnd`
  (84 / 87 / 82 / 89 bones; embedded labels `M_musa`, `salsu`, `dosa`, `Monk` — the base playable
  classes). The largest census skeleton is 244 bones. **Of the 349 files, 113 end exactly at the last
  bone record and 236 carry +4 trailing zero bytes** after the bone array (an old-serializer name
  terminator — see §File trailer); the loader correctly leaves these unconsumed in both cases.

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
| after name | 4 | u32 LE | `bone_count` | Number of bone records that follow. The loader reads a full 4-byte u32 but discards the upper three bytes (the loop bound is the low byte only), giving an effective maximum of 255 bones. SAMPLE-VERIFIED across 349 files: all observed bone counts fit in one byte (max 244), consistent with the low-byte-only semantic. Build `263bd994` witness: `g1.bnd` stores the raw u32 little-endian with low byte 84 and upper three bytes all zero (84-bone rig). | [sample-verified] |

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

> **Disk order proven by read sequence (not merely inferred from layout).** The per-bone parser
> reads the four logical fields in this exact order: `self_id` (one int), then `parent_id` (one int),
> then the **12-byte local translation** as one contiguous raw read (3×f32), then the **16-byte local
> rotation quaternion** as one contiguous raw read (4×f32). The two integers are read individually
> and the translation/quaternion as two bulk slurps, so the on-disk field order below is the literal
> read order, not an offset guess. | Confidence: [confirmed].

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
| [sample-verified] — the loader does not reorder components (confirmed by analysis of the quaternion multiply routine used during world-transform accumulation); on build `263bd994` the `g1.bnd` root bone is an identity rest quaternion whose **W (scalar) ≈ 1.0000 at sub-offset +32**, i.e. stored last, proving XYZW order on a real bone. Additionally cross-checked on a **non-identity, multi-bone** rest pose: `g1.bnd` bone 1 (`self_id` 1, `parent_id` 0, child of root) reads a non-trivial quaternion whose magnitude is **exactly 1.0** under the XYZW reading, confirming both the component order and the 36-byte stride against a real animated bone (not just the identity root) |

### Root bone sentinel

The root bone is identified by the condition: `self_id` low byte `== 0` and `parent_id` low
byte `== 0`. No other on-disk convention marks the root. Any bone whose `parent_id` low byte
matches no other bone's `self_id` low byte causes a fatal abort in the original client.

| Confidence |
|---|
| [sample-verified] — resolved by analysis of the hierarchy-build routine (it resolves each parent by low-byte match against the other bones' `self_id` low bytes); on build `263bd994` the `g1.bnd` root bone reads `self_id` low byte 0 and `parent_id` low byte 0, satisfying the sentinel |

### File trailer

**Corpus finding (two-witness):** of the 349 `.bnd` files, **113 end exactly at the last bone record
(zero residual)** and **236 carry exactly +4 trailing bytes of `0x00`** after the bone array. The
shipping loader consumes neither case incorrectly: it reads the header and `bone_count × 36` bytes,
runs the post-load hierarchy / world-transform passes, and returns — it never reads past the bone
array, so the 4 extra bytes (when present) are correctly left unconsumed.

**Origin (now identified):** the +4 trailing bytes are an **old-serializer artifact — a residual
4-byte string terminator left over from how the export tool wrote the `actor_name` LenStr** in an
earlier file-format generation. They are not a bone record, not a count, not a required field. The
two earlier alternative hypotheses (alignment padding, checksum) are no longer the working
explanation given the corpus split (a fixed +4 block on a majority of files, always all-zero, with
no relationship to bone content), and the "null LenStr sentinel for a concatenated variant" reading
is consistent with the serializer-terminator origin (a zero word after the name) but no
concatenated-file variant is observed.

**Recommended parser behaviour:** read the header and `bone_count × 36` bytes, then stop. Do **not**
require the bone array to end exactly at EOF, and do **not** read or interpret any trailing bytes.
Tolerate 0 or 4 residual bytes after the last bone record (both are valid; ~32% of files end exactly,
~68% carry the +4 block). Never treat a trailing block as a bone record or a required field.

| Confidence |
|---|
| [sample-verified] — corpus split 113 exact / 236 with +4 zero bytes (two-witness: loader read order + black-box census of all 349 files); build `263bd994` spot-check: the `g1.bnd` player rig is one of the 236 "+4 trailing zero" files (84 bones × 36B walk exactly to the bone-array end, then 4 zero residual bytes the loader never reads). Origin = old-serializer name terminator (CONFIRMED behaviourally; exact byte-generation provenance not byte-traced) |

---

## Format: `.bud` — binary building and static-mesh container

### Status

- **sample_verified:** NO — all facts below are static-analysis only; no `.bud` sample bytes have
  been walked. Pull a live VFS sample to two-witness `object_count`, the per-object stride, and the
  32-byte vertex field order before treating this section as fully confirmed.
- **ida_anchor:** `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963`
- **evidence:** [static-ida]
- **confidence baseline:** `[confirmed]` for loader-code-derived facts; `[unverified]` for all open
  questions listed in §Known unknowns.

### Identification

- **Extension:** `.bud`
- **Found in:** `.vfs` archive; path referenced from the cell `.map` descriptor under
  `BUILDING { DATAFILE … }`.
- **Magic / signature:** none — file begins immediately with `object_count`; no sentinel bytes or
  version field.
- **Endianness:** little-endian throughout.
- **Purpose:** stores all static building geometry ("mass objects") for a map cell. Walled-town
  architecture and scene props are stored in this format. Vertices are world-absolute (baked with
  the cell offset at export time); no per-object or per-cell transform is applied at render time.

### On-disk container layout

**File header — 4 bytes:**

| Offset | Size | Type | Field | Notes |
|---:|---:|---|---|---|
| 0 | 4 | u32 LE | `object_count` | Number of mass-object sub-meshes in this file. File begins immediately here. |
| 4 | … | — | `objects[object_count]` | Variable-stride per-object records (below), read sequentially to EOF. |

**Per mass-object on-disk record — variable stride, read in order:**

| Read # | Size | Type | Field | Notes |
|---:|---:|---|---|---|
| 1 | 1 | u8 | `type` | Object category/type tag. Retained in memory but the loader does not branch on it during load (see §Known unknowns — `type` byte). |
| 2 | 4 | u32 LE | `tex_id` | **1-based local** texture index into the cell `.map` `BUILDING TEXTURES` list. Remapped to a global `bgtexture.lst` slot at grid-build time. |
| 3 | 4 | u32 LE | `vertex_count` | Number of vertices in this object. Soft warn threshold: if greater than 3,072 the loader emits a warning containing the vertex count and the first vertex's X,Z position, but loading continues; the object is not clamped or rejected. |
| 4 | `vertex_count × 32` | bytes | `vertices` | Raw block of 32-byte render-vertex records (see §Render vertex layout). |
| 5 | 4 | u32 LE | `index_count` | Number of u16 indices (= 3 × triangle count). |
| 6 | `index_count × 2` | bytes | `indices` | Raw block of u16 LE vertex indices (triangle list). |

No alignment padding between fields or between records. No end-of-file trailer; the file ends after
the last object's index block.

### Render vertex layout — 32 bytes

**FVF: 0x112 (D3DFVF_XYZ | D3DFVF_NORMAL | D3DFVF_TEX1)**, confirmed from the building draw
passes. Index format: D3DFMT_INDEX16 (u16). Primitive type: D3DPT_TRIANGLELIST. Vertex stride:
32 bytes (global constant).

| Sub-offset | Size | Type | Field | Notes |
|---:|---:|---|---|---|
| 0 | 12 | 3×f32 LE | `position` (x, y, z) | **World-absolute** coordinates. Used directly to compute the per-object AABB at load. |
| 12 | 12 | 3×f32 LE | `normal` (x, y, z) | Surface normal; used by the fixed-function lighting path. |
| 24 | 8 | 2×f32 LE | `uv` (u, v) | Texture coordinates. The building draw applies no v-flip transform (unlike `.skn`/`.xobj`). Whether `.bud` UVs are already in D3D top-left convention is an open question — see §Known unknowns. |

> **FVF confirmation.** The building draw calls `SetFVF` with the constant 0x112 (274 decimal) and
> uses a vertex stride of 32 bytes. This is distinct from the textured-quad/decal format which uses
> FVF 0x102 (XYZ | TEX1) with stride 20 bytes (pos 12 + uv 8). Confirmed from static analysis of
> the building draw passes.

> **World-absolute vertices.** The loader copies vertex data verbatim into the draw staging buffer
> without any per-object or per-cell world transform. The global building tree batches objects from
> multiple cells into a single draw call, so world-space positions must be pre-baked at export.

### In-memory BudObject — 116 bytes

Each mass-object is represented in memory as a 116-byte structure. The loader allocates an array
of `object_count` such structs (plus a 4-byte count prefix) and default-constructs each before
reading. The selected load-relevant fields are listed here; the **full authoritative 116-byte layout
(all fields, padding, constructor defaults, and lifecycle notes) is in
`Docs/RE/structs/bud_object.md`**.

> **Wave-3 reconciliation (2026-06-28).** The upper half of this struct (+70..+115), previously
> described in this table as a geometric LOD block, is corrected here. `structs/bud_object.md` is now
> the authority for the full layout. The fields renamed below (`anim_class`, `anim_vertex_buffer`,
> `sway_amp`, `sway_vfactor`, `near_far_flag`) supersede the former names (`lod_class`,
> `lod_vertex_buffer`, `lod_step`, `lod_factor`, `far_lod_flag`) everywhere in this spec.

| Offset | Size | Type | Field | Source / role |
|---:|---:|---|---|---|
| 0 | 4 | u32 | `tex_id` | On load: the `.bud` local 1-based index. Overwritten at grid-build time with the global `bgtexture.lst` slot. Default-constructed as `−1`. |
| 4 | 4 | ptr | `vertex_buffer` | Heap pointer to the 32-byte vertex array. |
| 8 | 4 | u32 | `vertex_count` | |
| 12 | 4 | ptr | `index_buffer` | Heap pointer to the u16 index array. |
| 16 | 4 | u32 | `index_count` | |
| 20 | 12 | 3×f32 | `aabb_min` (x, y, z) | Computed at load from vertex positions (world coords). Constructor seeds this block as an inverted-AABB (min = +1, max = −1 per axis). |
| 32 | 12 | 3×f32 | `aabb_max` (x, y, z) | Computed at load. Degenerate-axis fix: if `min == max` on any axis, `max += 2.0`. |
| 48 | 4 | u32 | `vertex_byte_size` | `vertex_count × 32`, cached for the draw memcpy. |
| 52 | 1 | u8 | `type` | The `type` byte from disk (read #1). No branch on this value during load. |
| 56 | 4 | f32 | `dist_sq` | Per-frame squared XZ distance from the AABB midpoint to the camera. |
| 60 | 1 | u8 | `flag_60` | Role unverified in build f61f66a9 foliage/mass-object cluster: no read-site or non-zero write-site found. The frame-cull path writes `dist_sq` (+56) and `culled_flag` (+61) only. The `near_far`-partition role (0.7 × budget) described in prior revisions may apply to a static-building draw path outside the examined cluster. Previously named `far_lod_flag`, then `near_far_flag`. See `Docs/RE/structs/bud_object.md §Full offset table`. |
| 61 | 1 | u8 | `culled_flag` | Per-frame: set when `dist_sq > budget`; the object is skipped that frame. |
| 64 | 4 | f32 | `budget` | Squared draw-distance cull threshold (see §AABB and draw-distance budget). Default-constructed as 1,000,000. |
| 70 | 1 | u8 | `anim_class` | **Animation-style class byte (NOT geometric LOD).** Assigned by `BudObject_InitSwayBuffer` from a TerrainManager per-texture table indexed by the global `tex_id`. Values 10–14 → sway family 1; 20–24 → sway family 2; all others (e.g. 1) → static, no sway buffer. Previously named `lod_class`. |
| 72 | 4 | ptr | `anim_vertex_buffer` | **Full-size scratch vertex buffer** (`32 × vertex_count` bytes), allocated when `anim_class` is in the range 10–14 or 20–24. Rebuilt every frame with wind-sway displacement applied. NOT a decimated or LOD buffer. Previously named `lod_vertex_buffer`. |
| 80 | 4 | f32 | `sway_dir` | Sway direction/velocity sign; toggles between +100.0 and −100.0 as the oscillation phase reaches its bounds. Default: +100.0. |
| 92 | 4 | f32 | `sway_amp` | Sway amplitude and step parameter. A value of 0.0 means no animation. Computed by `BudObject_InitSwayBuffer` from the object's vertical AABB extent. Previously named `lod_step`. |
| 96 | 4 | f32 | `sway_vfactor` | Sway vertical multiplier applied to the Y displacement component. Class-dependent. Previously named `lod_factor`. |
| 100 | 4 | f32 | `sway_intensity` | Sway intensity / damping factor. Updated each frame, scaled toward the `AmbientSoundManager`-driven target. Default (constructor): 1.0. |
| 104 | 12 | 3×f32 | `centroid` | **Corrected (2026-06-29).** Computed by `BudObject_InitSwayBuffer` as `0.5 × (vtx[0].normal + vtx[2].normal)` componentwise — half the sum of the normal-slot vectors of vertices 0 and 2 of the raw source vertex buffer. NOT 0.5 × AABB midpoint. Reused by `BudObject_SwayAnimate_A` as a horizontal amplitude factor. Full layout: `Docs/RE/structs/bud_object.md`. |

See `Docs/RE/structs/bud_object.md` for the complete field table including padding bytes at +54–+55, +62–+63, and +71, the fields at +44, +53, +68, +69, +76, +80, +84, and +88, and full constructor / clear defaults.

### Draw constants

| Constant | Value | Meaning |
|---|---:|---|
| Vertex stride | 32 bytes | Global constant; matches FVF 0x112. |
| FVF | 0x112 (274) | D3DFVF_XYZ \| D3DFVF_NORMAL \| D3DFVF_TEX1. |
| Index format | 101 | D3DFMT_INDEX16 (u16 indices). |
| Primitive type | 4 | D3DPT_TRIANGLELIST. |
| Section vertex cap | 6,144 | Batch flushed before accumulating beyond this count. |
| Section index cap | 24,576 | Batch flushed before accumulating beyond this count. |
| Per-object soft vertex warn | >3,072 | Warning logged; load continues. |
| Texture cap per cell | 128 | Maximum local texture ids in the `BUILDING TEXTURES` table. |
| Degenerate-AABB fix | +2.0 | Per-axis correction when `aabb_min == aabb_max`. |
| Near/far render-pass split | 0.7 × budget | `flag_60` (+60): role unverified in foliage/mass-object cluster of build f61f66a9 — the frame-cull path writes only `dist_sq` (+56) and `culled_flag` (+61); a separate static-building path may be the consumer. See `structs/bud_object.md §Open items`. |
| Budget tier: small prop | 90,000 | = 300² squared draw units; metric <8. |
| Budget tier: medium prop | 250,000 | = 500²; metric <16. |
| Budget tier: standard | 1,000,000 | = 1,000²; metric <32. |
| Budget tier: large | 2,250,000 | = 1,500²; metric <64. |
| Budget tier: extra-large | 3,240,000 | = 1,800²; metric ≥64. |

### AABB and draw-distance budget

Computed at load time from the vertex array:

1. Initialize an inverted bounding box, then walk every vertex `position` to find `aabb_min` and
   `aabb_max`. Apply the degenerate-axis fix (`+2.0`) on any axis where `min == max`.
2. Compute `metric = max(distance(aabb_max, aabb_min) × 0.6,  aabb_max.y − aabb_min.y)`, i.e. the
   larger of 60% of the box diagonal and the box height.
3. Select the squared cull distance (`budget`) by `metric`: `<8 → 90,000`, `<16 → 250,000`,
   `<32 → 1,000,000`, `<64 → 2,250,000`, `≥64 → 3,240,000`. Larger objects cull from a greater
   camera distance.

### Per-frame cull and render-pass partition

Each frame, for every object in the building tree:

```
mid        = (aabb_min + aabb_max) × 0.5
dist_sq    = (mid.x − cam.x)² + (mid.z − cam.z)²    // XZ-plane only; Y (height) is ignored
culled     = dist_sq > budget
near_far   = (dist_sq ≤ budget) AND (dist_sq > 0.7 × budget)
```

`culled` is written to `culled_flag` (+61); `dist_sq` is written to `BudObject +56`. The
destination for `near_far` is `flag_60` (+60), whose role is unverified in the foliage/mass-object
cluster of build f61f66a9 (no confirmed read-site in the complete examined cluster — a static-building
draw path may be the consumer). Both near and far passes draw full geometry (no vertex reduction).
The camera XZ position is read from the render-globals structure each frame.

### Texture binding chain

The `.bud` per-object `tex_id` (a 1-based local index) is resolved at grid-build time:

```
bud tex_id (1-based local)
  → validated: 1 ≤ local ≤ cell.building_tex_count  (invalid → clamped to 1 with error log)
  → global = cell.building_tex_table[local − 1]      (populated from .map BUILDING TEXTURES block)
  → BudObject.tex_id := global                        (field overwritten in place)
  → TexturePool entry at global bgtexture.lst slot    (see formats/bgtexture_lst.md)
  → data/map000/texture/<name>.dds
```

This joins the documented terrain-texture chain (see `CLAUDE.md` §Recovered asset mappings and
`formats/bgtexture_lst.md`). The cell's building texture table is populated from the `.map`
`BUILDING { TEXTURES { … } }` block at descriptor parse time.

### Coordinate handedness

Source geometry is D3D9 left-handed, Y-up (engine-wide convention). Vertex positions are
world-absolute in that frame. For the Godot importer:

- Apply **world Z-negation** `(x, y, z) → (x, y, −z)` once at import.
- Do **not** apply the mesh-local X-negation used for `.skn` character meshes — that convention
  applies only to character-mesh-local coordinates.
- After Z-negation the triangle winding order is reversed. The importer must compensate (reverse
  winding or select the matching cull face) to prevent the buildings rendering inside-out. Confirm
  against a real sample before shipping (see §Known unknowns — winding).

---

## Cell descriptor: `.map` — text layout grammar

### Identification

- **Extension:** `.map`
- **Format:** ASCII, whitespace-tokenized; not binary. Lines beginning with `#` are comments; block
  delimiters are literal `{` and `}` tokens.
- **Role:** per-cell master layout descriptor. Lists the binary geometry files (`.bud`, `.ted`,
  `.exd`, `.upd`, `.sod`, FX layers) and their associated texture tables. Referenced by the cell
  loader at cell load time before any binary blob is opened.

### Grammar

Each named section follows the pattern `SECTION_NAME { DATAFILE <path> TEXTURES { <pairs> } }`.
Observed section names and roles:

| Section | Role | Has TEXTURES block |
|---|---|---|
| `TERRAIN` | Ground terrain geometry (`.ted`) | Yes |
| `EXTRA_TERRAIN` | Additional terrain layer (`.exd`) | Yes |
| `UP_TERRAIN` | Upper terrain layer (`.upd`) | Yes |
| `BUILDING` | Static building/prop geometry (`.bud`) | Yes |
| `FX1`–`FX7` | Terrain FX layers | Yes |
| `SOLID` | Collision geometry (`.sod`) | No (DATAFILE only) |

The `BUILDING` section in detail:

```
BUILDING {
    DATAFILE  <relative path to the .bud file>
    TEXTURES {
        <local_id_1>  <global_bgtexture_lst_id_1>
        <local_id_2>  <global_bgtexture_lst_id_2>
        ...
    }
}
```

In the `TEXTURES` block each pair is two integers: the first is the local texture id and the second
is the global `bgtexture.lst` slot. The loop continues while the first integer is positive; a `}`
token or a non-positive first integer terminates the block. Maximum 128 entries per cell.

> **Cross-subsystem reconciliation note.** Prior analysis (RECON-2) described `.ted` as
> export-only / not loaded at runtime. The building-pipeline RE shows the `TERRAIN` section of the
> `.map` feeds a `.ted` geometry blob to a runtime terrain loader at cell load time. This is a
> conflict to be resolved by the terrain analyst; it is flagged here and in §Known unknowns and
> does not affect any `.bud` fact.

---

## Building draw pipeline (overview)

### Cell load path

At cell load time, the cell master loader (`Terrain_LoadCellFiles`) reads the per-cell base path
and dispatches to `Map_LoadCellDescriptor`, which parses the `.map` token grammar via
`Map_ParseDescriptor`. Each named section in the descriptor triggers a dedicated binary loader:

| `.map` section | Binary loader | File loaded |
|---|---|---|
| `TERRAIN` | `Ted_LoadGeometryBlob` | `.ted` ground geometry |
| `EXTRA_TERRAIN` | `Exd_DecodeTriangles` | `.exd` extra terrain layer |
| `UP_TERRAIN` | `Up_DecodeTriangles` | `.upd` upper terrain layer |
| `BUILDING` DATAFILE | `Bud_LoadBuildingBlob` | `.bud` building geometry |
| `BUILDING` TEXTURES | `BuildingSection_AddTextureId` | — (populates cell building texture table) |
| `FX1`–`FX7` | `Fx{n}_DecodeGroups` | terrain FX layer files |
| `SOLID` | `Sod_LoadCollisionBlob` | `.sod` collision geometry |

After all sections are dispatched, `Map_BuildMassObjectGrid` executes the post-load finalisation
pass (see §Grid build below).

### Grid build (Map_BuildMassObjectGrid)

After descriptor parsing, `Map_BuildMassObjectGrid` performs three post-load operations on the
cell's mass objects:

1. **Texture remap:** for each `BudObject`, the local 1-based `tex_id` read from disk is validated
   and replaced in place with the corresponding global `bgtexture.lst` slot drawn from the cell's
   building texture table (see §Texture binding chain).
2. **Spatial binning:** each object is placed into one cell of a 16×16 spatial grid (64 world-space
   units per grid-cell, XZ plane). Per-grid-cell minimum and maximum Y values are tracked and updated
   as each object is binned, providing fast height-range queries over the cell.
3. **Sway buffer initialisation:** for each object, `BudObject_InitSwayBuffer` is called. The function
   queries the `anim_class` byte (BudObject offset +70) from a TerrainManager per-texture table
   indexed by the global texture id. The class byte selects the sway variant: values 10–14 (sway
   family 1) and 20–24 (sway family 2) each allocate a full-size scratch `anim_vertex_buffer` and
   compute `sway_amp`, `sway_vfactor`, and `centroid`; all other values (e.g. 1) leave the object
   static with no scratch buffer. This is a wind-sway animation system, not geometric LOD. See
   `Docs/RE/structs/bud_object.md §Wind-sway animation system` for the full initialisation detail.

### Draw passes

Two draw passes per frame consume building geometry, both using FVF 0x112 with a 32-byte vertex
stride:

1. **Opaque world pass** — handles near buildings via a quadtree traversal with per-object near/far
   render-pass selection (full geometry in both cases). A second per-object sub-pass applies a
   projected lightmap/shadow texture on stage 1 using a texture-matrix path (D3DTS_TEXTURE1,
   transform index 17). This second sub-pass is why a single UV set in the vertex suffices: stage 1
   uses generated texture coordinates, not a second UV attribute.
2. **Main cull-and-render-pass batcher** — the primary pass that computes per-object squared XZ
   distance, sets the cull and near/far render-pass flags, and merges visible objects into
   section-sized batches for a single indexed draw call per batch.

### Batch merge

The main batcher iterates the global building tree and accumulates objects into a staging buffer.
For each object:

- Skip if the object's `culled_flag` is set.
- Flush the current batch before accumulating if the running index total plus the object's
  `index_count` would reach or exceed 24,576, or if the running vertex total plus `vertex_count`
  would reach or exceed 6,144. An object exceeding an entire section capacity by itself is skipped.
- Copy the object's u16 indices into the staging index region, rebasing each index by adding the
  current accumulated vertex count.
- Copy the object's 32-byte vertices into the staging vertex region verbatim (no transform).
- Flush remaining geometry at the end of the tree traversal.

The staging buffer is partitioned: a vertex region (6,144 vertices × 32 bytes = 196,608 bytes)
immediately followed by an index region (24,576 × 2 bytes = 49,152 bytes); total 245,760 bytes.

### Multitexture render states

The opaque building pass configures two texture stages: stage 0 carries the diffuse building
texture (MODULATE-class color operation, alpha-test enabled, depth-write enabled, cull mode
clockwise); stage 1 is a projected lightmap/shadow texture driven by a texgen path
(D3DTS_TEXTURE1, transform index 17, `TEXCOORDINDEX`/`TEXTURETRANSFORMFLAGS` setup). The exact
D3DTSS and D3DSAMP enum values are a remaining deliverable for the render-state analyst and should
be confirmed against a live session before porting.

---

## Enumerations / flags

None identified in `.xobj`, `.skn`, or `.bnd` formats. The `.bud` `type` field (one byte per
mass-object) is read and stored but the loader does not branch on it; its value semantics are
unconfirmed (see §Known unknowns — `type` byte).

---

## Known unknowns

### `.bud` / building mesh open questions

- **`type` byte (BudObject +52):** read from disk and retained in memory, but the building loader
  does not branch on it during load. Likely a content/category tag consumed by a different system
  (collision, LOD class, sub-mesh category). Trace readers of this field to resolve.
  `[unverified]`
- **UV v-flip:** the building draw applies no `1−v` transform (unlike `.xobj`/`.skn`). Confirm
  whether `.bud` UVs are stored already in D3D top-left convention, or whether the textures appear
  flipped without correction. `[sample-unverified]` — requires a real `.bud` sample.
- **Winding order after Z-negation:** indices are stored in their authored winding; the Godot
  importer must reverse winding after the world Z-negation to prevent inside-out rendering.
  Confirm against a real sample before shipping. `[sample-unverified]`
- **Sample byte-verification:** all `.bud` facts in this spec are static-analysis only. No `.bud`
  sample has been extracted and walked. Two-witness against a real VFS `.bud` (extract to an
  external path; walk `object_count`, per-object stride, 32-byte vertex field order) before marking
  this section `[sample-verified]`. `[sample-unverified]`
- **`.map` / `.ted` runtime-load conflict:** the `TERRAIN` section of the cell `.map` feeds a
  `.ted` geometry blob to the runtime terrain loader at cell load time, contradicting the RECON-2
  reading that `.ted` is export-only. Cross-reference with `formats/terrain.md` and resolve with
  the terrain analyst. `[cross-subsystem conflict]`

### `.xobj` / `.skn` / `.bnd` open questions

- **`.xobj` `slot_id` (token 1):** the discard behaviour is confirmed; the value is confirmed to
  match the filename numeric prefix. Whether a non-matching `slot_id` is an error or is
  accepted silently is unknown (the parser never validates it).
- **`.xobj` intra-vertex delimiter:** tab-delimited in all observed samples. Whether the parser
  accepts any whitespace as a delimiter, or strictly requires tab, is unconfirmed. The parser is
  described as a general whitespace tokenizer, suggesting tab is not mandatory.
- **`.xobj` maximum index:** u16 ceiling is 65535; no sample has more than 13 vertices.
  Off-by-one behaviour at the u16 boundary is untested.
- **`.skn` name field CP949 encoding:** item-skin names in available byte-level samples are pure
  ASCII, and the fresh build-`263bd994` weighted **character** skin sample (name length 11) is also
  pure ASCII. Korean-encoded names are expected in some character skins; no Korean-encoded sample
  byte block has yet been inspected. The Known Unknown stands (D-2).
- **`.skn` / `.bnd` `self_id` / `parent_id` upper bytes:** the loader discards the upper three
  bytes after reading the full u32. Whether these bytes are always zero in real files, or whether
  they carry information consumed by other code paths, is not confirmed (census bone counts and IDs
  all fit in the low byte, consistent with the discard).
- **`.bnd` `actor_id` upper 3 bytes:** the parser stores the full u32 `actor_id`. Whether the
  upper bytes carry meaning or are always zero in practice is unknown.
- **`.bnd` trailing 4 bytes — origin downgraded, not fully open:** the corpus split (113 files end
  exact, 236 carry +4 zero bytes) and the all-zero, content-independent nature of the block identify
  it as an old-serializer name-terminator artifact (see §File trailer). What remains unverified is
  only the exact byte-generation provenance in the original export tool; the parser handling is
  settled (read `bone_count × 36`, ignore any trailing bytes).
- **`.skn` per-vertex influence cap:** the format imposes no fixed cap; the corpus shows both
  variable (~2.6) and fixed (4.0) influence styles. Whether the engine enforces a maximum at load
  is not confirmed (it sums all influences). Importers capping to an engine limit must re-normalize.

> Note: the former "`.bnd` multi-bone byte-level cross-check" Known Unknown is now **resolved** — see
> the Resolved items table below.

### Resolved items (removed from the unknowns above)

| Former item | Resolution |
|---|---|
| `.skn` `id_b` non-zero semantics | RESOLVED — `id_b` is an `actor_id` skeleton pointer (sparse range 1..8892, 0 = no skeleton), resolved by `actor_id` lookup against the bindlist registry — NOT a dense 0..348 index. Non-zero is universal for character skins and the 349 distinct non-zero `id_b` values are a bijection with the 349 `.bnd` `actor_id`s (§id_b ↔ skeleton bijection). |
| `.skn` `id_b` vs `skin.txt col2` ("IdB") naming collision | RESOLVED — two distinct fields: `.skn` `id_b` = `actor_id` skeleton pointer (1..8892, resolved by `actor_id`, not a 349-index); `skin.txt col2` = 6-value outfit/class tag (separate file, base-offset table). See the Header naming-disambiguation note. (Canonical glossary split owned by Tier-1.) |
| `.skn` multi-bone weight records | RESOLVED — 1,140 multi-bone/multi-weight skins census-verified (§Multi-bone weighted skinning). |
| `.skn` weight skip threshold | RESOLVED — minimum observed weight = 0.010; records below 0.01 are dropped (SAMPLE-VERIFIED). |
| Weights addressing bones by index vs ID | RESOLVED — `bone_index` is a **bone ID** resolved by `id − base_id`, no indirection table (§Bone addressing). |
| `.bnd` multi-bone skeletons exist | RESOLVED — 327 multi-bone `.bnd` files; player rigs g1–g4 (82–89 bones); largest 244 bones. |
| `.bnd` trailing 4 bytes present-in-all? | RESOLVED (corpus) — present in 236/349 files; absent (exact end) in 113. Always all-zero, content-independent → old-serializer name terminator. Parser ignores it (§File trailer). Only the exact tool provenance remains unverified. |
| `.bnd` multi-bone byte-level cross-check | RESOLVED — performed on the `g1.bnd` player rig (`actor_id` 1, 84 bones): the 84 records × 36 bytes walk exactly to the bone-array end (then the +4 trailer), and bone 1 (`self_id` 1, `parent_id` 0) reads a non-identity quaternion of magnitude exactly 1.0 in XYZW order — the 36-byte stride and XYZW order are now confirmed on a real multi-bone rig, not just on identity single-bone props (§Quaternion component order). |
| LOD / decimated buffer construction | RESOLVED (wave-3, 2026-06-28) — the buffer at +72 is a **full-size scratch vertex buffer** for per-frame wind-sway vertex animation, not a decimated LOD mesh. The build path copies the full vertex buffer and computes oscillation parameters (`sway_amp`, `sway_vfactor`, `centroid`). No vertex reduction occurs anywhere. Authority: `Docs/RE/structs/bud_object.md §Wind-sway animation system`. |
| `lod_class` source table | RESOLVED (wave-3, 2026-06-28) — renamed `anim_class`. Assigned by `BudObject_InitSwayBuffer` from a TerrainManager per-texture table indexed by the global `tex_id`. Values 10–14 = sway family 1; 20–24 = sway family 2; all other values = static (no sway buffer). Authority: `Docs/RE/structs/bud_object.md`. |
| `centroid` formula (prior: "0.5 × AABB midpoint") | RESOLVED (deep-cartography, 2026-06-29) — corrected to `0.5 × (vtx[0].normal + vtx[2].normal)` componentwise (half-sum of the normal-slot vectors of vertices 0 and 2 of the raw source vertex buffer). Authority: `Docs/RE/structs/bud_object.md §Full offset table`. |
| Sway family-2 amp scale direction | RESOLVED (deep-cartography, 2026-06-29) — confirmed **DIVIDE** by `2 << (anim_class − 20)`, same direction as family 1. Authority: `Docs/RE/specs/bud_loader.md §6`. |
| Sway oscillator type | RESOLVED (deep-cartography, 2026-06-29) — confirmed **linear triangle-wave ping-pong**; no trigonometric call in any of the three sway-animator variants. Authority: `Docs/RE/structs/bud_object.md §Wind-sway animation system`. |
| Grid query path | RESOLVED (deep-cartography, 2026-06-29) — `MassObjectGrid_SampleCellMaxHeight` queries by cell-index lookup + per-object plane sampling; the only `dist_sq` in the system is the per-frame draw cull, NOT a grid search. Authority: `Docs/RE/specs/bud_loader.md §5.2`. |

---

## Cross-references

- **BudObject in-memory layout (authoritative):** `Docs/RE/structs/bud_object.md` — full 116-byte
  field table, padding, constructor defaults, wind-sway animation system, draw dispatch, and AABB/budget
  rules. This file (mesh.md) documents the on-disk `.bud` container; `bud_object.md` documents the
  runtime struct that the loader populates from it.
- Related formats: `formats/pak.md` (container), `formats/animation.md` (`.mot` clips that drive
  these skeletons), `formats/texture.md` (co-referenced assets), `formats/bindlist.md` (the
  authoritative registry of all 349 `.bnd` skeletons keyed by `actor_id` — the resolution target for
  `.skn` `id_b`)
- Building / terrain texture chain: `formats/bgtexture_lst.md` (the global texture index used by
  the `.bud` texture binding chain); `formats/terrain.md` (`.ted` ground geometry and cell
  coordinate system; also the target of the `.map`/`.ted` runtime-load reconciliation note)
- Deform / skinning math: `specs/skinning.md` (linear-blend skinning, inverse-bind bake, pose
  composition, quaternion/handedness conventions, Godot import guidance, canonical test specimens)
- Canonical names: see `Docs/RE/names.yaml` (`XobjFile`, `SkinFile`, `BindPoseFile`, `SknFace`,
  `SknCorner`, `SknVertex`, `SknWeight`, `BndBone`; the `id_b` skeleton-pointer vs `skin.txt col2`
  class-tag split is owned by Tier-1). Proposed names from the `.bud` recovery — `BudFile`,
  `BudObject`, `building_tex_table` — are pending registration in `names.yaml` by the RE orchestrator.
- Provenance: see `Docs/RE/journal.md`. The `.bud` building-mesh section was promoted from dirty-room
  static analysis on build `f61f66a9` (2026-06-28); no sample byte-verification yet. The
  `id_b` skeleton-pointer / class-tag split was promoted under CAMPAIGN VFS-MASTERY. Re-verified
  under CAMPAIGN 10 Block D against build `263bd994` + a fresh VFS sample set on 2026-06-16.
