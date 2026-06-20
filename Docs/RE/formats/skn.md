# Format: .skn  (skinned character / item mesh — on-disk layout)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary code addresses. Consumed by Assets.Parsers. Every offset an engineer cites must
> reference this file.

<!--
verification: confirmed (loader-control-flow)
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: none
-->

> **Verification banner.** `confirmed (loader-control-flow)` · `ida_anchor: 263bd994` ·
> `evidence: [static-ida]` · `conflicts: none`. Re-verified against doida.exe IDB SHA `263bd994`,
> CYCLE 7 (2026-06-20): the `.skn` byte parser was walked end-to-end in the loader (header → face
> section → vertex section → influence section, each as a `count` u32 followed by a bulk
> `count × stride` raw block), and the per-vertex influence packing (drop / normalize / major-minor
> split) and the verbatim coordinate handling were read directly. The on-disk strides (36-byte face,
> 24-byte vertex, 12-byte influence) and the 4-byte LenStr name prefix re-confirmed exactly with
> the byte layout already documented in `formats/mesh.md` (§Format: `.skn`). No structural drift.

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
| Face section: `u32 Nface` + `36 × Nface` bytes (3 inline corner positions per face) | Resolved | CONFIRMED (loader-control-flow) |
| Vertex section: `u32 Nvtx` + `24 × Nvtx` bytes (2 × vec3 f32: geometry + normal) | Resolved | CONFIRMED (loader-control-flow) |
| Influence section: `u32 Nweight` + `12 × Nweight` bytes (12-byte records) | Resolved | CONFIRMED (loader-control-flow) |
| Influence record = {u32 vertex-position key, u32 bone id, f32 weight} | Resolved | CONFIRMED (loader-control-flow) |
| Loader drops influences with `weight < 0.01`, normalizes each vertex's survivors to Σ = 1.0 | Resolved | CONFIRMED (loader-control-flow) |
| Variable influences per vertex (no on-disk cap) | Resolved | CONFIRMED (loader-control-flow) |
| Positions and normals stored VERBATIM (no per-component negate / swap / scale at parse) | Resolved | CONFIRMED (loader-control-flow) |
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

**Face record — 36 bytes (3 inline corners × 12 bytes):**

Each face record is one triangle stored as three consecutive corner sub-records; each corner inlines
a vertex position (a `vec3` of f32) plus the per-corner texture coordinates. The face section is an
**index / triangle soup with vertex positions inlined per corner**; the loader consumes it to
enumerate and deduplicate the unique vertex positions referenced by the mesh.

| Sub-offset within corner | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `vertex_index` | Zero-based index into the vertex section that follows. | CONFIRMED |
| 4 | 4 | f32 LE | `uv_u` | Per-corner texture U. | CONFIRMED |
| 8 | 4 | f32 LE | `uv_v` | Per-corner texture V (the engine applies `1.0 − uv_v` when building the render vertex). | CONFIRMED |

> **Refinement vs `formats/mesh.md`.** Walking the loader on build `263bd994`, the 36-byte face
> record is consumed as **three inline corner positions** (3 × `vec3` f32 = 9 floats) for the purpose
> of position dedup; the per-corner `vertex_index` + UV view in `formats/mesh.md` is the same 12-byte
> corner sub-record described another way (the first 4 bytes serve as the index / position reference,
> the next two floats as UV). Both views describe the same 36 bytes; the dedup-by-position behavior is
> the CYCLE-7 refinement. The binary wins — see `formats/mesh.md` §Face table for the
> sample-verified item-skin witnesses.

---

## Vertex section

Immediately follows the face section (no alignment padding).

| Rel. offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 4 | u32 LE | `Nvtx` | Number of vertex records. | CONFIRMED |
| 4 | `Nvtx × 24` | bytes | `vertex_data` | Raw block; `Nvtx` vertex records of 24 bytes each. | CONFIRMED |

**Vertex record — 24 bytes (2 × vec3 f32), little-endian:**

Each vertex stores two consecutive `vec3` f32 triples: a geometry (position) triple and a normal
triple. **Both are stored VERBATIM** — there is no per-component negate, no axis swap, and no scale
applied by the byte reader. The render-vertex assembly re-orders the two triples into its in-memory
layout downstream (see `formats/mesh.md` §Vertex table for the disk-order normal-then-position
witness and `specs/skinning.md` for the derived 32-byte render vertex).

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| 0 | 12 | 3 × f32 LE | `triple_0` | One `vec3` triple (geometry / normal — disk order detailed in `formats/mesh.md`). Stored verbatim. | CONFIRMED |
| 12 | 12 | 3 × f32 LE | `triple_1` | The other `vec3` triple. Stored verbatim. | CONFIRMED |

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
| 4 | `Nweight × 12` | bytes | `influence_data` | Raw block; `Nweight` influence records of 12 bytes each. | CONFIRMED |

**Influence record — 12 bytes, little-endian:**

| Sub-offset | Size | Type | Field | Notes | Confidence |
|---:|---|---|---|---|---|
| +0x00 | 4 | u32 LE | `vertex_position_key` | The vertex this influence binds to, expressed as a **position reference**: matched against the face-corner position float-bits to find the deduplicated vertex. It is a position key (raw float-bit compare), not a plain sequential index. | CONFIRMED |
| +0x04 | 4 | u32 LE | `bone_id` | The bone this weight binds to. Resolved **base-relative** (`id − base_id`) against the bound skeleton downstream (the bind-pose bone whose `self_id` matches), not by array position. See `formats/mesh.md` §Bone addressing and `specs/skinning.md`. | CONFIRMED |
| +0x08 | 4 | f32 LE | `weight` | Per-influence blend weight. The **loader drops** influences with `weight < 0.01` and then **normalizes** each vertex's surviving influences so their sum equals 1.0 (see §Per-vertex influence packing). | CONFIRMED |

**Influence count per vertex is variable** — there is no on-disk cap. A vertex may have one influence
(rigid) or several. The on-disk records are an unordered list keyed by `vertex_position_key`; the
loader groups them by vertex.

### Per-vertex influence packing (loader behavior)

After reading all influence records, the loader, per vertex:

1. **Drops** any influence with `weight < 0.01` (a near-`0.01` single-precision threshold; records at
   or above `0.01` are kept).
2. **Normalizes** the surviving influences so each vertex's weights sum to **1.0** (a zero total
   weight is a fatal assertion in the original client).
3. Partitions the result into a single dominant (MAJOR) influence per vertex plus the remaining
   (MINOR) influences — a derived split used by the deform path.

A port must reproduce the **drop-below-0.01** and **per-vertex normalize-to-1.0** steps; the
major/minor partition and the deform that consumes these normalized influences are documented in
`specs/skinning.md` (the in-memory 36-byte influence record with its pre-baked bone-local rest
position/normal is a DERIVED runtime structure, not on disk).

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
- **`bone_id` / `vertex_position_key` upper bytes:** downstream the `bone_id` is consumed as a low
  byte (base-relative resolve); whether the upper bytes ever carry meaning is not confirmed
  (observed values fit the low byte).
- **Per-vertex influence cap:** the format imposes no fixed cap; the corpus shows both variable and
  fixed-4 influence styles (see `formats/mesh.md` §Multi-bone weighted skinning). A Godot port capping
  to Skeleton3D's 4-bone limit takes the top-4 by weight and re-normalizes.
- **Raw disk order of the two vertex triples (geometry vs normal):** the runtime ROLES are pinned
  (position then normal in memory), but which raw disk triple is which is MEDIUM — the bake and deform
  consume them symmetrically, so a parser that reads them in the same order is correct regardless of
  the label. See `formats/mesh.md` §Vertex table (normal-first disk order witness).

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
- **Provenance:** see `Docs/RE/journal.md`. The CYCLE-7 refinements (face-section position dedup,
  influence record = {position key, bone id, weight}, drop-below-0.01 + per-vertex normalize-to-1.0,
  verbatim Y-up coordinates with the `−X` flip identified as a port convention) were recovered from
  doida.exe IDB SHA `263bd994`, static analysis, 2026-06-20.
