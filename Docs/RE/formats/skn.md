# Format: .skn  (skinned character / item mesh — on-disk layout)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary code addresses. Consumed by Assets.Parsers. Every offset an engineer cites must
> reference this file.

<!--
verification: confirmed (loader-control-flow + sample-verified)
ida_anchor: 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
evidence: [static-ida, sample-byte]
conflicts: none
-->

> **Verification banner.** `confirmed (loader-control-flow + sample-verified)` ·
> `ida_anchor: 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` ·
> `evidence: [static-ida, sample-byte]` · `conflicts: none`. Re-verified against doida.exe IDB SHA
> `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`, CYCLE 7 (2026-06-20),
> re-walked + byte-verified against a rigid item-skin sample (2026-06-21), and re-confirmed against
> two further samples (item: `gi201011001.skn`, 2593 bytes, Nface 46 / Nvtx 25 / Nweight 25;
> character: `g200002620.skn`, 84083 bytes, Nface 1432 / Nvtx 786 / Nweight 1136 — both exhaust at
> EOF with zero residual). Both the item byte parser and the character byte parser were walked
> end-to-end in the loader (header → face section → vertex section → influence section, each as a
> `count` u32 followed by a bulk `count × stride` raw block), and the per-vertex influence packing
> (drop / normalize / major-minor split) and the verbatim coordinate handling were read directly.
> The on-disk strides (36-byte face, 24-byte vertex, 12-byte influence) and the 4-byte LenStr name
> prefix re-confirmed exactly. Earlier framings corrected by the sample passes (see the corrections
> box below): the face-corner first field and the influence first field are both **plain `u32` vertex
> indices**, and face dedup is by **(vertex index, UV)** — not by inline corner positions. No
> structural drift in the section/stride layout.
>
> **Corrections applied (binary-won, sample-verified).**
> - The influence record's first field is a **plain `u32` vertex index** (sample = `0,1,…,Nvtx−1`,
>   one per vertex in the rigid sample), **not** a "position key / raw float-bit position compare".
>   The loader matches it (as a raw `u32` equality) against the **face-corner's `u32` vertex index** —
>   an index-to-index match, not a position-float compare.
> - The 36-byte face record is **3 corners × {`u32` vertex_index, `f32` uv_u, `f32` uv_v}**, **not**
>   "3 inline corner positions (9 floats)". Render-vertex dedup is nominally by `(vertex_index, uv_u,
>   uv_v)` (unique render vertices that differ by index or UV); however, the loader's actual collapse
>   test uses **vertex_index equality plus a 0.001 position epsilon** (and a separate coarser ~0.2
>   position-proximity weld over major-bone groups), with UV carried per render vertex — see the
>   dedup footnote in §Face section. (Sample face0 corner indices = `2, 3, 4` in the small item skin,
>   `4, 5, 9` in the character skin: small integers valid as indices, not denormal floats.)
> - The raw disk order of the two vertex triples is **sample-confirmed**: triple 0 = **normal**
>   (unit length), triple 1 = **position** (mesh extent). Upgraded from MEDIUM to CONFIRMED.
>
> **Sample witnesses:** rigid item skin `gi201011001.skn` (`id_a` = 201011001, `id_b = 0`,
> `name` = "gum_201011001" 13 bytes ASCII, `Nface = 46`, `Nvtx = 25`, `Nweight = 25`, single bone
> weight 1.0, EOF residual 0); character skin `g200002620.skn` (`id_a` = 200002620, `id_b = 3045`,
> `name` = "s_200002620" 11 bytes ASCII, `Nface = 1432`, `Nvtx = 786`, `Nweight = 1136`,
> multi-bone, EOF residual 0). Source-file provenance for the character skinning math: `coreskin.cpp`
> (the load-time normalize / major-bone-select asserts at lines 294, 306, 333).

## Scope

This file documents the **on-disk `.skn` (skinned mesh) byte layout** — the only artifact an
`Assets.Parsers` engineer must implement to read a `.skn` file. The companion byte-level format
record (faces / vertices / weights, with sample-verified item-skin and character-skin witnesses)
already lives in **`formats/mesh.md` (§Format: `.skn`)**; this file is a focused, CYCLE-7-refined
description of the same on-disk container, written as the primary `.skn` reference and cross-linked
to `formats/mesh.md` for the corpus-census witnesses. **Where this file and `formats/mesh.md` differ
on a detail, the binary wins** — the differences here are refinements observed walking the loader on
build `263bd994`, noted inline.

The **runtime / in-memory** structures this loader derives from the file — the deduplicated 32-byte
render vertex and the 36-byte in-memory influence record (with the pre-baked bone-local rest
position/normal from the inverse-bind bake) — are **DERIVED, not on disk**. They are documented in
`specs/skinning.md` and are only cross-referenced here; this file defines the bytes that exist in the
file, not the structures built from them.

---

## Status and confidence summary

| Area | Status | Confidence |
|------|--------|------------|
| `.skn` body = header + face section + vertex section + influence section | Resolved | CONFIRMED (loader-control-flow) |
| Header `id_a` (u32, skin/material id → `skin.txt`) | Resolved | CONFIRMED (loader-control-flow) |
| Header `id_b` (u32, skeleton / skin-class key, used verbatim as the pose-pool lookup key) | Resolved | CONFIRMED (loader-control-flow) |
| Header `name` (LenStr — 4-byte u32 LE length + body, no on-disk terminator; discarded for math) | Resolved | CONFIRMED (loader-control-flow; same LenStr as `.mot`/`.bnd`) |
| Face section: `u32 Nface` + `36 × Nface` bytes (3 corners × {u32 vertex_index, f32 uv_u, f32 uv_v}) | Resolved | CONFIRMED (loader-control-flow + sample) |
| Face corner first field = `u32` vertex index (NOT inline position floats); dedup collapse test = vertex_index equality + 0.001 position epsilon (net effect: unique by index and UV) | Resolved | CONFIRMED (sample-verified + loader-control-flow) |
| Vertex section: `u32 Nvtx` + `24 × Nvtx` bytes (2 × vec3 f32: disk order normal then position) | Resolved | CONFIRMED (loader-control-flow + sample) |
| Influence section: `u32 Nweight` + `12 × Nweight` bytes (12-byte records) | Resolved | CONFIRMED (loader-control-flow) |
| Influence record = {u32 vertex_index, u32 bone id, f32 weight} | Resolved | CONFIRMED (sample-verified) |
| Influence first field = plain `u32` vertex index (NOT a position key / float-bit compare) | Resolved | CONFIRMED (sample-verified) |
| Loader drops influences with `weight < 0.01`, normalizes each vertex's survivors to Σ = 1.0 (character path) | Resolved | CONFIRMED (loader-control-flow) |
| Variable influences per vertex (no on-disk cap) | Resolved | CONFIRMED (loader-control-flow) |
| Positions and normals stored VERBATIM (no per-component negate / swap / scale at parse) | Resolved | CONFIRMED (loader-control-flow) |
| Rigid item path consumes only header + face + vertex (ignores the file's weight section) | Resolved | CONFIRMED (loader-control-flow + sample) |
| Up-axis of stored geometry is Y-up | Resolved | CONFIRMED (engine-treatment; raw-byte sample not inspected) |
| The project's `−X` mesh-local flip is a PORT convention, not a format field | Resolved | CONFIRMED (absence in binary geometry path) |

Open items are tracked in §Known unknowns.

---

## Identification

- **Extension:** `.skn`
- **Found in:** `.vfs` archive (see `formats/pak.md`); logical path patterns e.g.
  `data/char/skin/*.skn` (character body skins), `data/item/skin/*.skn` (rigid item props).
- **Magic / signature:** none — the file begins immediately with the header fields; no sentinel bytes.
- **Endianness:** little-endian throughout (all multi-byte integers and floats are LE).
- **Mode:** opened in BINARY mode — every "u32" below, including each LenStr length prefix, is a raw
  4-byte little-endian read, never a parsed decimal string (the shared asset-stream reader is
  dual-mode; `.skn` and `.bnd` take the binary branch — see `formats/mesh.md` §String encoding).
- **Confidence:** CONFIRMED (loader-control-flow, build `263bd994`).

---

## On-disk layout (overview)

The body is a header followed by three count-prefixed raw sections, in this fixed order:

```
header  : id_a (u32) | id_b (u32) | name (LenStr)
face    : Nface (u32)   | Nface   × 36-byte face record
vertex  : Nvtx (u32)    | Nvtx    × 24-byte vertex record
influence : Nweight (u32) | Nweight × 12-byte influence record
```

Each section is a `count` u32 immediately followed by `count × stride` raw bytes (a bulk block read);
there is **no alignment padding** between sections or between records. Because `name` is
variable-length, the sections after the header have **no fixed absolute offsets** — a parser must
advance the cursor section by section.

> Engineer note: read the header, then for each of the three sections read a `u32 count` and then
> `count × stride` bytes; stop after the influence section. // spec: Docs/RE/formats/skn.md

---

## Header

Appears at offset 0. The `name` field is variable-length (wire size `4 + name_length` bytes), so all
sections after the header have no fixed absolute offset.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `id_a` | Skin / material id. Resolves the texture chain through `data/char/skin.txt` (see §Cross-references and `formats/texture.md`). In item-skin files this matches the numeric suffix of the filename. | CONFIRMED |
| 4 | 4 | u32 LE | `id_b` | Skeleton / skin-class key. Passed **verbatim** as the lookup key into the runtime pose pool to resolve the bind-pose skeleton (see §Skeleton resolution and `formats/bindlist.md`). `id_b = 0` = no skeleton (rigid prop). | CONFIRMED |
| 8 | 4 + N | LenStr | `name` | Length-prefixed name string: a 4-byte u32 LE byte-length `N`, then `N` body bytes with **no on-disk null terminator**. Same LenStr encoding as `.mot` and `.bnd`. The value is **discarded for math** — a parser must consume `4 + N` bytes to stay aligned but need not retain it. | CONFIRMED |

### LenStr encoding (shared with `.mot` and `.bnd`)

The `name` field uses the same length-prefixed-string encoding used by `.mot` and `.bnd`: a 4-byte
u32 LE byte-length prefix followed by exactly that many body bytes, with no trailing null byte on
disk. This is the single shared `LenStr` helper described in `formats/mesh.md` (§String encoding) and
`formats/animation.md` (§LenStr encoding). A 1-byte or 2-byte prefix interpretation misaligns the
subsequent `Nface` count and is rejected.

**Encoding of the body:** ASCII in the item samples; Korean class/character names are expected as
CP949 / EUC-KR (no BOM). Because the field is discarded, encoding matters only for diagnostics.

> **`id_b` disambiguation (firewall note).** The legacy label "IdB" was historically applied to BOTH
> this `.skn` `id_b` skeleton key AND the `skin.txt col2` outfit/class-family tag — they are two
> distinct fields with two distinct consumers (skeleton resolution vs a base-offset table). This file
> names the `.skn` field `id_b` exclusively as the skeleton key. The canonical glossary split is owned
> by Tier-1 in `Docs/RE/names.yaml`; this file does not edit that glossary. See `formats/mesh.md`
> (§Header) for the full `id_b` ↔ skeleton discussion (`actor_id` resolution, the 349-skeleton
> bijection, sparse 1..8892 range).

---

## Face section

Immediately follows the header (after the variable-length `name`).

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `Nface` | Number of faces (triangles). | CONFIRMED |
| 4 | `Nface × 36` | bytes | `face_data` | Raw block; `Nface` face records of 36 bytes each. | CONFIRMED |

**Face record — 36 bytes (3 corners × 12 bytes):**

Each face record is one triangle stored as three consecutive corner sub-records; each corner is a
`u32` **vertex index** (into the vertex section that follows) plus the per-corner texture coordinates.
The face section is an **index / triangle soup** — vertex positions are stored once in the vertex
section, not inlined per corner. The loader walks all `3 × Nface` corners and deduplicates to build
the unique render-vertex list. The effective dedup key is **vertex_index equality plus a 0.001
position epsilon** on the vertex position components; a separate coarser weld (~0.2 units) runs
over major-bone groups. UV is stored per render vertex, so a single mesh vertex referenced from
corners with different UVs becomes distinct render vertices. The shorthand `(vertex_index, uv_u,
uv_v)` captures the net result (unique by index and UV); the underlying collapse test is the
position-epsilon variant above.

| Sub-offset within corner | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index into the vertex section that follows. A **plain `u32` index** (sample face0 corners = `2, 3, 4`), not packed position floats. The loader compares it as a raw `u32` equality, and matches it against the influence section's `vertex_index` (an index-to-index match). | CONFIRMED |
| 4 | 4 | f32 LE | `uv_u` | Per-corner texture U. | CONFIRMED |
| 8 | 4 | f32 LE | `uv_v` | Per-corner texture V (the engine applies `1.0 − uv_v` when building the render vertex — the v-flip lives in the parser's render-vertex assembly, not in the bytes). | CONFIRMED |

> **Dedup correction and refinement.** The 36-byte face record is **3 corners × {`u32` vertex_index,
> `f32` uv_u, `f32` uv_v}**. An earlier reading framed the 36 bytes as "three inline corner positions
> (9 floats)" deduplicated by position — the bytes disprove this: the first 4 bytes of each corner
> are a small `u32` index (item sample face0 = `2, 3, 4`, valid indices into the 25-vertex section;
> character sample face0 = `4, 5, 9`, valid into the 786-vertex section; read as `f32` those integers
> would be denormals ≈ 2.8e-45). The effective collapse test is vertex_index equality **plus a 0.001
> position epsilon** on the vertex position, with UV carried per render vertex; a separate coarser
> ~0.2-unit weld runs over major-bone groups. The net result: render vertices are unique by index and
> UV, with position proximity breaking ties. See `formats/mesh.md` §Face table for additional
> sample-verified witnesses.

---

## Vertex section

Immediately follows the face section (no alignment padding).

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `Nvtx` | Number of vertex records. | CONFIRMED |
| 4 | `Nvtx × 24` | bytes | `vertex_data` | Raw block; `Nvtx` vertex records of 24 bytes each. | CONFIRMED |

**Vertex record — 24 bytes (2 × vec3 f32), little-endian:**

Each vertex stores two consecutive `vec3` f32 triples. The **disk order is NORMAL first, then
POSITION** (sample-confirmed: vertex 0's first triple is `(0, −1, 0)`, unit length = the normal; the
second triple spans the mesh extent = the position). **Both are stored VERBATIM** — there is no
per-component negate, no axis swap, and no scale applied by the byte reader. The render-vertex
assembly re-orders the two triples into its in-memory layout downstream (the derived 32-byte render
vertex takes its position from the 2nd triple and its normal from the 1st — see `specs/skinning.md`).

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 12 | 3 × f32 LE | `normal` | The vertex normal (disk triple 0). All observed normals are unit length. Stored verbatim. | CONFIRMED |
| 12 | 12 | 3 × f32 LE | `position` | The vertex position (disk triple 1). Spans the mesh's own extent. Stored verbatim. | CONFIRMED |

All normal vectors observed are unit length (magnitude 1.0 within float precision). Position
coordinates span the mesh's own extent (small for item props; full body extent for character skins).

> **Verbatim coordinates — no negate at parse.** The byte reader copies position X,Y,Z and normal
> X,Y,Z exactly as stored; no component is negated, swapped, or scaled. See §Coordinate conventions.

---

## Influence (weight) section

Immediately follows the vertex section (no alignment padding). This is the per-vertex bone-weight
("skin") data.

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `Nweight` | Number of influence records. In rigid item skins `Nweight == Nvtx` (single-bone, weight 1.0); in character skins `Nweight > Nvtx` (multiple influences per vertex). | CONFIRMED |
| 4 | `Nweight × 12` | bytes | `influence_data` | Raw block; `Nweight` influence records of 12 bytes each. (The character byte parser reads these records one field at a time rather than as a single bulk block — the layout is identical either way.) | CONFIRMED |

**Influence record — 12 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| +0x00 | 4 | u32 LE | `vertex_index` | The vertex this influence binds to, as a **plain `u32` index** (sample = `0,1,…,Nvtx−1`, one per vertex in the rigid case). The loader matches it (raw `u32` equality) against the **face-corner `vertex_index`** to attach the influence to the deduplicated render vertices — an **index-to-index match**, not a position-float compare. | CONFIRMED |
| +0x04 | 4 | u32 LE | `bone_id` | The bone this weight binds to. Resolved **base-relative** (`id − base_id`) against the bound skeleton downstream (the bind-pose bone whose `self_id` matches), not by array position. See `formats/mesh.md` §Bone addressing and `specs/skinning.md`. | CONFIRMED |
| +0x08 | 4 | f32 LE | `weight` | Per-influence blend weight. The **loader drops** influences with `weight < 0.01` and then **normalizes** each vertex's surviving influences so their sum equals 1.0 (see §Per-vertex influence packing). | CONFIRMED |

> **Correction — `vertex_index`, not a "position key".** An earlier reading called this field a
> `vertex_position_key` matched against face-corner position float-bits. The bytes disprove that: the
> field is a plain `u32` vertex index, and the field it is matched against (the face corner's first
> field) is **itself** a `u32` vertex index — so the comparison is an index-to-index `u32` equality.
> The "raw-bits compare" describes how two `u32`s are compared at the instruction level; it is **not**
> a float compare and there is no position key.

**Influence count per vertex is variable** — there is no on-disk cap. A vertex may have one influence
(rigid) or several. The on-disk records are an unordered list keyed by `vertex_index`; the loader
groups them by vertex.

### Per-vertex influence packing (character loader behavior)

This packing is performed by the **character** byte parser. After reading all influence records, the
loader, per vertex:

1. **Drops** any influence with `weight < 0.01` (a near-`0.01` single-precision threshold; records at
   or above `0.01` are kept).
2. **Normalizes** the surviving influences so each vertex's weights sum to **1.0** (a zero total
   weight is a fatal assertion in the original client — `coreskin.cpp`).
3. Partitions the result into a single dominant (MAJOR) influence per vertex plus the remaining
   (MINOR) influences — a derived split used by the deform path.

A port must reproduce the **drop-below-0.01** and **per-vertex normalize-to-1.0** steps; the
major/minor partition and the deform that consumes these normalized influences are documented in
`specs/skinning.md` (the in-memory 36-byte influence record with its pre-baked bone-local rest
position/normal is a DERIVED runtime structure, not on disk).

### Rigid item path — weight section present on disk but unused at runtime

An item (rigid prop) `.skn` is read by a **separate** byte parser that consumes only the header, face,
and vertex sections, builds the deduplicated render-vertex table, and then **returns without reading
the influence section** — even though the file physically contains one (the sample's 8-record weight
section is present and the file exhausts exactly at EOF). Consequences for a parser:

- The on-disk **layout is identical** for item and character `.skn`: all four sections are present and
  a generic reader can consume all four.
- For a rigid prop the weight section is informational (single bone, weight 1.0 per vertex); the
  original's rigid runtime path skips it. A port may read and ignore it, or skip it once it knows the
  skin is rigid (`id_b = 0`). Either way the bytes are accounted for.

---

## Coordinate conventions

The `.skn` vertex stream stores position X,Y,Z and normal X,Y,Z **verbatim** — the byte reader
applies no per-component negation, no axis swap, and no scale. Two consequences:

- **Up-axis is Y-up.** The engine treats character geometry as Y-up (height along +Y, mapping 1:1 to
  world +Y), with the XZ plane as ground and +Z forward, rendered under a left-handed D3D view matrix.
  The bind pose is consumed verbatim with no loader remap and no fixed character import rotation
  (other than the runtime yaw heading about Y). The absolute up-axis label is settled by the engine's
  placement/facing math, not by any field of the file. See `specs/skinning.md` (up-axis / import
  orientation section).
- **The `−X` mesh-local flip is a PORT convention, NOT a format field.** The project's "`.skn`
  mesh-local geometry negates X" rule (and the world `−Z` rule) are **port-side** handedness / forward
  reconciliations introduced by the Godot port to map the original's left-handed, +Z-forward space to
  Godot's right-handed, −Z-forward space. The original parser performs **no** such negation. The
  `Assets.Parsers` byte decoder must **not** pre-negate any axis; apply the handedness flip once, at
  the importer layer (see `specs/skinning.md` and the Godot port helpers). The up axis (Y) is left
  unchanged across the conversion.

---

## End of file

No explicit end-of-file marker. The file ends immediately after the last influence record. The
shipping loader reads the header and the three count-prefixed sections and stops; rigid item samples
exhaust with zero residual.

---

## Enumerations / flags

None identified in this format.

---

## Known unknowns

- **`name` field CP949 encoding:** item-skin and the inspected character-skin name bodies are ASCII;
  Korean-encoded name bodies are expected in some character skins but no Korean byte block has been
  inspected. The field is discarded, so this does not affect parsing.
- **`bone_id` upper bytes:** downstream the `bone_id` is consumed as a low byte (base-relative
  resolve); whether the upper bytes ever carry meaning is not confirmed (observed values fit the low
  byte). The `vertex_index` field is a full `u32` index and is no longer an open item.
- **Per-vertex influence cap:** the format imposes no fixed cap; the corpus shows both variable and
  fixed-4 influence styles (see `formats/mesh.md` §Multi-bone weighted skinning). A Godot port capping
  to Skeleton3D's 4-bone limit takes the top-4 by weight and re-normalizes.
- **`name` body in Korean character skins:** see the first bullet above — discarded, so it does not
  affect parsing.

(Resolved this pass — formerly open: the raw disk order of the two vertex triples is now
sample-confirmed as **normal then position**; the influence-record first field is sample-confirmed as
a plain `u32` **vertex index** (not a position key).)

---

## Cross-references

- **Asset chain (skin → texture):** `.skn` `id_a` → `data/char/skin.txt` → texture id →
  `data/char/tex{…}/{id}.png`. Texture id resolution and texture file formats: `formats/texture.md`.
- **Skeleton resolution:** `.skn` `id_b` → the registered skeleton in the runtime pose pool, resolved
  by the parsed `.bnd` `actor_id` (verbatim by `actor_id`, NOT a literal `g{id_b}.bnd` filename rule).
  The authoritative `.bnd` registry keyed by `actor_id` is `formats/bindlist.md`; the on-disk `.bnd`
  byte layout is in `formats/mesh.md` (§Format: `.bnd`).
- **Shared on-disk byte layout:** the byte-level `.skn` record tables (with sample-verified item-skin
  and character-skin witnesses, the `id_b` ↔ skeleton bijection, and the multi-bone weighted-skinning
  census) live in `formats/mesh.md` (§Format: `.skn`). This file is the focused, CYCLE-7-refined
  description of the same container.
- **Deform / skinning math:** the math that consumes these bytes — linear-blend skinning, the
  load-time inverse-bind bake (`conj(q_bind)·(v − t_bind)` baked into the derived per-influence rest
  record), pose composition, the derived 32-byte render vertex, the derived 36-byte in-memory
  influence record, the major/minor partition, the base-relative bone resolve, and the up-axis / import
  orientation — is specified in `specs/skinning.md`. Those structures are DERIVED at runtime and are
  NOT redefined here as on-disk.
- **Animation:** `formats/animation.md` (`.mot` clips that drive the skeleton resolved by `id_b`).
- **Container:** `formats/pak.md` (the VFS the `.skn` is read from).
- **Canonical names:** see `Docs/RE/names.yaml` (`SkinFile`, `SknFace`, `SknCorner`, `SknVertex`,
  `SknWeight`; the `id_b` skeleton-key vs `skin.txt col2` class-tag split is owned by Tier-1).
- **Provenance:** see `Docs/RE/journal.md`. The format (four count-prefixed sections, strides
  36/24/12, LenStr name, drop-below-0.01 + per-vertex normalize-to-1.0, verbatim Y-up coordinates
  with the `−X` flip identified as a port convention) was recovered from doida.exe IDB SHA
  `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`, static analysis, 2026-06-20,
  and re-walked + byte-verified against two samples (item `gi201011001.skn` 2593 bytes Nface 46 /
  Nvtx 25 / Nweight 25; character `g200002620.skn` 84083 bytes Nface 1432 / Nvtx 786 / Nweight
  1136 — both EOF residual 0). Those sample passes corrected two earlier framings: the face-corner
  first field and the influence first field are both plain `u32` vertex indices (face dedup collapse
  test is vertex_index equality + 0.001 position epsilon, not a position-float key compare; the
  influence match is index-to-index, not a position-key float compare); and the vertex disk-triple
  order was confirmed as normal then position. The `id_b = 3045` witness from the character sample
  confirms `id_b` is a verbatim pose-pool key, not a small 1..4 class index at this layer.
