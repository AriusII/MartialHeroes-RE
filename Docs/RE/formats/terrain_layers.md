# Format: terrain layer sidecars  (per-cell terrain overlay and lighting files)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> Related base terrain formats are in `Docs/RE/formats/terrain.md`.

---

## Re-verification banner (2026-06-16, CAMPAIGN 10 / Block D)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `mixed` — see per-format rows. The **FX `.fx1`–`.fx7` group-array on-disk format is now CODE-CONFIRMED** from the seven file decoders (per-channel header width, vc/ic offsets, vertex stride, 1-based `texture_index` at group `+0x00`; the `.fx4` "flat tile" framing is retired) — §1.4a/§1.4b, re-walked 2026-06-21. The **`.fx2` path was independently re-walked against a live single-group sample** (decoder read-sequence + AABB finalize 44-byte stride; exact size match, zero residual) — §1.4a/§1.6 unchanged. Newly recorded this pass: the **`.fxN` load path / `.map` linkages** (§1.1b), the **exact 1-based texture-register remap** (§1.4b), the **FX3/FX5 = water channel identity** (§1.4c, render path DBG-PENDING), and the **`.fx1.pre`–`.fx7.pre` editor sidecars** (§5.5, editor-only). The `.up`/`.exd` 40-byte triangle record and the light/wind/point_light layouts were **not** re-dumped this pass and hold at their committed tiers. |
| `ida_reverified` | `2026-06-21` (FX `.fx1`–`.fx7` on-disk file decoders re-walked: per-channel group-header width, vc/ic offsets, vertex stride, and the 1-based `texture_index` at group `+0x00` — §1.4a/§1.4b; `.fx2` decoder re-walked + live-sample byte-exact; load-path linkages §1.1b; texture-register remap §1.4b; FX3/FX5 water-channel identity §1.4c; `.fxN.pre` sidecars §5.5; prior routing re-confirm `2026-06-16`) |
| `ida_anchor`     | `263bd994` |
| `evidence`       | `[static-ida, vfs-sample]` — routing/dispatch from the located runtime `.map` parser + per-channel FX decoders (witness 1) + VFS census file-count corroboration and a live single-group `.fx2` sample whose size matches the decoder formula exactly (witness 2) |
| `conflicts`      | None. Routing re-confirmed with no drift: (1) **`.up` and `.exd` are ONE shared, distinct format** — `count(u32) + count × 40-byte triangle`, decoded by the same record decoder; `EXTRA_TERRAIN → .exd`, `UP_TERRAIN → .up`. This is structurally **distinct** from the FX group-array model of §1 — any reading that lumps `.exd`/`.up` in with the FX channels is REFUTED. (2) Each FX channel `.fx1`–`.fx7` has its **own** group decoder and its **own** per-channel texture register; the per-channel header width + vertex stride (§1.13) are mandatory. (3) Each FX channel's texture index is **1-based with a `< 1 \|\| > max` guard** (corroborated by per-channel `fx<N> texture index(%d) < 1 \|\| > max(%d)` client error strings) — confirming the shared building/terrain 1-based + clamp texture-index convention without needing the render path. |

**Census this pass (witness 2, full VFS mount, routing-level corroboration):** `.up` 222 files,
`.exd` 1 384 files (both 40-byte-triangle format, zero residual); FX channel counts in the same
ballpark as §1.13's table. File counts are corpus observations, not load-bearing layout facts.

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `mixed` — see per-format status rows below |
| `sample_verified` | `true` for `.fx1`–`.fx6`, `.up`, `.exd`, `.sod.pre`, `.ted.post`, `wind%d.bin` header, and the `.fx{N}.pre` sidecar's existence + leading group-count word; `false` for `light%d.bin`, `point_light%d.bin`; `.fx7` size-formula PLAUSIBLE (dual-sample); `.fx4` structure CONFIRMED-from-loader (flat tile array; internal header fields sample-unverified). `.fx{N}.pre` body is **editor-only and not decoded** (§5.5). |
| `binary_analysed` | `doida.exe` (legacy 32-bit client, x86 LE) |
| `confidence`      | Fields annotated CONFIRMED are corroborated by parser read-sequence and/or real sample bytes. Fields annotated UNVERIFIED are structurally inferred or parser-only, without sample cross-check. |

---

## Overview

This document covers the family of **per-cell sidecar files** that accompany the primary terrain
assets (`.ted`, `.sod`, `.map`) documented in `terrain.md`. It also covers three **map-scoped
sky/lighting blobs** that reside in `data/sky/dat/` rather than per-cell directories.

The formats are grouped as follows:

| Section | Extensions | Scope | Role |
|---------|------------|-------|------|
| 1       | `.fx1`–`.fx7` | Per terrain cell | Terrain overlay mesh layers (3D triangle geometry) |
| 2       | `.up`      | Per terrain cell | Upper/overhanging terrain collision triangles |
| 3       | `.exd`     | Per terrain cell | Extra terrain collision triangles (supplementary) |
| 4       | `.sod.pre` | Per terrain cell | Collision polygon vertex cache (editor sidecar) |
| 5       | `.ted.post`| Per terrain cell | Full terrain snapshot (editor sidecar) |
| 5.5     | `.fx1.pre`–`.fx7.pre` | Per terrain cell | FX layer editor sidecars (editor-only; not read at runtime) |
| 6       | `light%d.bin` | Per map       | Directional and ambient light keyframe table |
| 7       | `point_light%d.bin` | Per map | Point light array |
| 8       | `wind%d.bin` | Per map        | Foliage-sway / wind keyframe array |

**Coordinate system (all formats):** Y-up world space. The world origin is at cell index
`(10000, 10000)`; each cell spans 1024 × 1024 world units. Cell coordinates encode as:

```
world_X_min = (cellX - 10000) * 1024.0
world_Z_min = (cellZ - 10000) * 1024.0
```

**Endianness:** little-endian throughout all formats in this document.

**String encoding:** no string fields are present in any of these binary formats.

**Filename convention for per-cell files:**

```
d{MAP:03d}x1{TILE_X:04d}z1{TILE_Z:04d}.{ext}
```

where `MAP` is the zero-padded map number and `TILE_X` / `TILE_Z` are zero-padded four-digit
tile indices. The literal `1` before each four-digit index is part of the encoding (values are
stored as `10000 + tile_index`).

---

## Section 1: FX Layer Files  (`.fx1` – `.fx7`)

### 1.1 Overview

Each terrain cell may carry up to seven FX layer files. The layer index (1 through 7) is encoded
solely in the file extension; there is no layer-type field inside the file. All observed FX layers
store **3D triangle mesh geometry** — a vertex buffer followed by an index buffer — not 2D texture
blend or alpha-splat maps.

All seven FX decoders share **one** on-disk structure: a universal **group-array** layout described
in §1.1a. The seven extensions differ only in the **vertex stride** they apply (§1.2) and in nothing
else structurally; there is no per-extension sub-format selector and no branch on the leading word.

| Format key | sample_verified |
|-----------|-----------------|
| `fx_layer` | `true` for `.fx1`–`.fx3`, `.fx5`, `.fx6`; `.fx7` PLAUSIBLE (dual-sample); `.fx4` structure CONFIRMED-from-loader (flat tile array) |

**Path pattern:** `data/map{MAP}/dat/d{MAP}x1{TX:04d}z1{TZ:04d}.fx{N}` where `N` is 1–7.

### 1.1a Universal group-array model (all `.fx1`–`.fx7`)

> **Two-witness correction (CAMPAIGN VFS-MASTERY).** Both witnesses (loader read-sequence and
> black-box corpus) agree on a single, branchless model for every FX decoder. This **supersedes
> two earlier readings of the leading word** and **both are REFUTED**:
> - the spec's prior "`type_tag` is a constant equal to 1", and
> - the black-box guess that "`type_tag` is a sub-format selector that switches the file layout".
>
> Neither holds. The leading word is **a group count** — the number of group records that follow.
> There is no constant, and there is no selector: the same group-array parse runs for every value
> and for every FX extension. **Tag CONFIRMED.**

Every FX file is a **count of groups followed by that many group records**. The leading word at
`0x00` is the group count; it is read, the parser loops that many times, and each iteration consumes
one group record. The decoder never branches on the value and never re-interprets the layout based on
it — the only thing that changes between the seven extensions is the vertex stride applied inside each
group's vertex block (see §1.2 for the VF_32 / VF_36 / VF_44 stride per extension).

**File-level header:**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `group_count` | Number of group records that follow. Drives the group loop. **Not** a constant and **not** a sub-format selector — it is purely the count of groups. | CONFIRMED (two-witness: loader loops on it; corpus shows it varying 1..61) |

**Per-group record (repeated `group_count` times):**

The group record begins with a fixed group header, then its own vertex block and index block. The
group header carries a few leading words (read into the group struct but not all consumed for
parsing), a `render_state` word, and the per-group vertex and index counts that size the two blocks.

This table shows the **`.fx1` / `.fx2`** group header (20-byte header); the longer-header channels
(`.fx3`–`.fx7`) place the same leading `texture_index` and the same `vertex_count` / `index_count`
pair at the channel-specific offsets given in the per-channel table of §1.4a.

| Group-rel offset | Size | Type | Field | Notes | Confidence |
|-----------------:|-----:|------|-------|-------|------------|
| +0x00 | 4 | u32 | `texture_index` | **1-based** index into this channel's own texture-id register (the `.map` `TEXTURES{ slot id }` list for this FX channel). The grid-build pass clamps it with `index < 1 || index > count` (substituting `1` on violation, with the per-channel error message), then remaps it to the registered texture id. *(CORRECTED 2026-06-21, binary-won: this was previously read as a `group_flags_0` constant "commonly 1" — that is a 1-based texture index of 1; it is traced through the clamp + the per-channel texture register, see §1.4b.)* | CODE-CONFIRMED |
| +0x04 .. (vc) | varies | u32… | inter-header words | One or more channel-specific dwords between `texture_index` and the `vertex_count` / `index_count` pair (the width difference between the 20/36/44/48-byte headers — see §1.4a). Most are read into the group struct but **not consumed** by the parse. For `.fx3` one such dword (just before `vertex_count`) is a **signed elevation/extent** value the bbox finalize stores as `(−value, +value)` to set a symmetric vertical extent. The remaining inter-header dwords are zero-initialised by the in-memory ctor and their on-disk semantics (material / flags / reserved) are **sample-unverified — DBG-PENDING**; do NOT assert a meaning. | mixed (fx3 elevation word CODE-CONFIRMED; the rest DBG-PENDING) |
| (channel vc offset) | 4 | u32 | `vertex_count` | Sizes the group's vertex block (`vertex_count × vertex_stride`). Offset per channel in §1.4a. | CONFIRMED |
| (channel ic offset) | 4 | u32 | `index_count` | Sizes the group's index block (`index_count × 2`, u16). Offset per channel in §1.4a. | CONFIRMED |

- **Vertex block (per group):** `vertex_count × vertex_stride` bytes, where the stride is the
  extension's vertex format from §1.2 (VF_36 / VF_44 / VF_32).
- **Index block (per group):** `index_count × 2` bytes (u16 triangle list).

**The old "constant 2nd word (15 / `0x0F` for `.fx1`/`.fx2`, 5 for `.fx3`)" reading is REFUTED**
(re-walked 2026-06-21 on the actual file decoders). That reading was at the wrong offset/routine; the
only stable leading field is the 1-based `texture_index` at group `+0x00`. Everything between it and
the `vertex_count` / `index_count` pair is channel-specific and only partly consumed (see the
`inter-header words` row above and §1.4a). An engineer must read `vertex_count` / `index_count` from
the channel-specific offsets and must **not** hard-code any "constant" header word.

> **Note on the longer FX3 / FX5 / FX7 group headers.** The single-group FX-family files documented
> in §§1.5–1.11 below were originally written up with longer, format-specific headers (48-byte for
> FX3, a 40 + 12 split for FX5/FX7). Those longer headers are the **same group record** with
> additional leading words ahead of the `vertex_count` / `index_count` pair — they do not constitute a
> different model. Treat §§1.5–1.11 as worked single-group / per-stride instances of this one
> group-array layout; the leading word is the group count in every case, the `render_state` word is
> variable in every case, and only the vertex stride differs.

### 1.1b Load path / linkages (who reads `.fxN`, and the join to `.map`)

The `.fxN` files are **not** opened by a standalone top-level loader. Each is a **DATAFILE referenced
from the per-cell `.map` text descriptor** (`terrain.md`). The chain, by role:

1. **Cell descriptor loader** — opens the cell `.map` file (via the client's VFS-or-loose-disk file
   router) and, on success, hands it to the descriptor parser, then runs a finalize tail that builds
   the per-cell grids (including the FX layer grids).
2. **`.map` descriptor parser** — a whitespace-delimited, `#`-comment text tokenizer. It recognises
   the block keywords `TERRAIN`, `EXTRA_TERRAIN`, `UP_TERRAIN`, `BUILDING`, `FX1`…`FX7`, and `SOLID`.
   Each FX block carries a `DATAFILE <path>` (the `.fxN` file) and a `TEXTURES { slot id … }` sub-block:
   - the `DATAFILE` is opened through the same file router and passed to that channel's **FX group
     decoder** (one decoder per channel, FX1…FX7);
   - each `TEXTURES` row registers an `(slot, texId)` pair into that channel's **per-channel texture
     register** (§1.4b; capacity 32).
3. **FX group decoder (per channel)** — the actual `.fxN` byte reader. It reads `group_count`, then per
   group reads the channel-specific group header (sizing the vertex/index blocks from `vertex_count` /
   `index_count`), the vertex block (channel stride from §1.2), and the u16 index block, and computes a
   per-group AABB. Read sequence and offsets are §1.1a / §1.4a.
4. **Per-cell grid build pass (per channel)** — runs from the descriptor loader's finalize tail. It
   expands each decoded group into the cell's 16×16 grid bucket and resolves the group's 1-based
   `texture_index` through the per-channel texture register (§1.4b). FX channels 3 and 5 take the
   distinct **water** build path (§1.4c); the other five take the generic build path.

**Join keys:** the per-cell `.map` descriptor is the hub — its `FX{N} { DATAFILE … }` names the `.fxN`
file for that channel, and its `FX{N} { TEXTURES { slot id … } }` populates the register that the
group's 1-based `texture_index` indexes. The resolved texture id then joins the shared terrain texture
chain in `terrain.md`.

### 1.4a Per-channel group-header layout and vertex format (CODE-CONFIRMED, re-walked 2026-06-21)

Re-walking the seven on-disk file decoders directly settles the per-channel group-header width, the
`vertex_count` / `index_count` offsets within that header, and the vertex stride each channel applies.
The universal model (§1.1a) holds for all seven; only these three columns vary.

| Channel | Vertex format | Group-header width | `vertex_count` offset | `index_count` offset |
|---------|---------------|--------------------|-----------------------|----------------------|
| `.fx1` | VF_36 (36 B) | 20 B (0x14) | +0x0C | +0x10 |
| `.fx2` | VF_44 (44 B, dual-UV) | 20 B (0x14) | +0x0C | +0x10 |
| `.fx3` | VF_36 (36 B) | 44 B (0x2C) | +0x24 | +0x28 |
| `.fx4` | VF_44 (44 B) | 48 B (0x30) | +0x28 | +0x2C |
| `.fx5` | VF_36 (36 B) | 48 B (0x30) | +0x28 | +0x2C |
| `.fx6` | VF_32 (32 B, no colour) | 36 B (0x24) | +0x1C | +0x20 |
| `.fx7` | VF_32 (32 B, no colour) | 48 B (0x30) | +0x28 | +0x2C |

- Each decoder multiplies `vertex_count` by the channel's vertex stride for the vertex block and reads
  `index_count × 2` bytes (u16) for the index block.
- **`.fx6` and `.fx7` share VF_32** (no per-vertex colour, confirmed by an empty vertex-element
  constructor) but differ in header width (36 B vs 48 B).
- **`.fx4` is the universal group-array model** with VF_44 vertices and a 48-byte header — CORRECTED
  2026-06-21: the earlier "flat tile array" framing is retired. It is NOT a distinct on-disk format;
  the "flat tile" impression came from the post-parse 16×16 grid bucketing that ALL channels share.
- VF_36 carries an RGBA8 colour at element +24 (the vertex-element constructor defaults it with
  alpha 0xFF); VF_44 = VF_36 + a second UV; VF_32 has no colour. The exact UV0/UV1 float-vs-packed
  encoding is PLAUSIBLE (sample-unverified), and the index-block primitive topology (triangle-list vs
  strip) is PLAUSIBLE/DBG-PENDING — the decoder stores a flat u16 array and does not decide topology.

### 1.4b Per-channel texture register (CODE-CONFIRMED)

Each FX channel owns its **own** texture-id table (no sharing across channels), populated from the
`.fx` channel's `TEXTURES{ slot id }` list in the per-cell `.map` descriptor. The register holds up to
**32 entries** (overflow is logged). A group's `texture_index` (group `+0x00`) is **1-based** into this
register: the build pass guards `index < 1 || index > count`, substitutes `1` on violation, then maps
`index` to the registered texture id (index 1 → the first registered id). This is the same 1-based +
clamp convention the base terrain/building texture indices use (`terrain.md`).

The remap is an exact 1-based lookup: the registered texture id for a group is the entry at
`register[texture_index − 1]`, where `register[0]` is the first id declared in the `.map` `TEXTURES`
block for that channel. (Reconfirmed 2026-06-21 against the `.fx2` build pass: the off-by-one between
the register base and the 1-based index resolves so that `texture_index = 1` selects the first
registered id.) After the build pass, the registered id flows downstream through the shared terrain
texture chain — `bgtexture.txt` → `.dds` — described in `terrain.md`.

### 1.4c WATER channel identity — FX3 and FX5 (channel-identity CONFIRMED; render path DBG-PENDING)

The per-cell `.map` descriptor wires the seven FX channels through seven distinct grid-build passes
after the descriptor is parsed. **FX channels 3 and 5 take a separate "water layer" build path**
(distinct from the generic build path used by channels 1, 2, 4, 6, and 7); they are the channels the
client treats as **water** overlays. This channel identity is CODE-CONFIRMED from the dedicated build
routines for those two channels (and corroborates the per-group elevation/direction word documented in
§§1.7–1.8 for FX3/FX5).

> **Scope of this fact.** "Water layers = FX3 and FX5" is a **channel-identity** statement (which two
> channels follow the water build path). The actual water **material / render branch** — what the
> water shader does, how the elevation/direction word drives it — has **not** been traced and is
> **DBG-PENDING** (needs the render path or the live debugger). Do not assert water render behaviour
> from this section; only the channel assignment is settled.

### 1.2 Vertex formats (on-disk)

Three on-disk vertex formats are used. The applicable format is determined by the layer index
(the extension), not by a flag inside the file.

| Label  | Stride | Field order |
|--------|--------|-------------|
| VF_36  | 36 B   | f32 X, f32 Y, f32 Z, f32 NX, f32 NY, f32 NZ, u8 R, u8 G, u8 B, u8 A, f32 U0, f32 V0 |
| VF_44  | 44 B   | f32 X, f32 Y, f32 Z, f32 NX, f32 NY, f32 NZ, u8 R, u8 G, u8 B, u8 A, f32 U0, f32 V0, f32 U1, f32 V1 |
| VF_32  | 32 B   | f32 X, f32 Y, f32 Z, f32 NX, f32 NY, f32 NZ, f32 U0, f32 V0 |

Notes:
- RGBA bytes are in order R, G, B, A. Opaque black = `00 00 00 FF`.
- VF_32 (used by `.fx6` only) omits the per-vertex colour field.
- UV coordinates may be negative or exceed `[0, 1]`; terrain meshes use tiled textures.

### 1.3 Index format (all FX layers)

All FX layer files use **u16 triangle indices**, little-endian, stored as plain triangle lists
(three indices per triangle; no strip-restart codes). Common observed patterns:

- Single triangle: `[0, 1, 2]`
- Quad (two triangles): `[0, 1, 2, 1, 3, 2]`
- Grid quad-strip: repeating groups of `[N, N+1, N+2, N+1, N+3, N+2]`

### 1.4 In-memory expansion (informational — not on disk)

After loading, the client expands each mesh group into a larger in-memory record that includes
a bounding box and a texture handle. These are never serialised back to disk.

| Layer | In-memory record stride |
|-------|------------------------|
| FX1   | 72 bytes               |
| FX2   | 72 bytes               |
| FX3   | 112 bytes              |
| FX5   | 120 bytes              |
| FX6   | 112 bytes              |

### 1.5 FX1 Format  (`.fx1`)

**Status:** CONFIRMED — group-array model (§1.1a); single-group samples verified by exact size.
**Semantic:** Single-triangle terrain overlay. Each mesh group contains one triangle (3 vertices,
3 indices). Vertex format: VF_36.

**File layout:**

```
FX1_File = group_count (u32) + group_count × [ group header (20 B) + VertexData + IndexData ]
```

**Group header (per group, see §1.1a for the universal field table):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `group_count` | File-level group count (§1.1a). Not a constant, not a selector. | CONFIRMED (two-witness) |
| +0x00 | 4 | u32 | `group_flags_0` | Read-but-not-consumed; near-constant 1. | UNVERIFIED |
| +0x04 | 4 | u32 | `group_flags_1` | Read-but-not-consumed; mostly 0. | UNVERIFIED |
| +0x08 | 4 | u32 | `render_state` | Per-group render-state word. **Variable** — earlier "constant=15" claim REFUTED. | CONFIRMED-variable |
| +0x0C | 4 | u32 | `vertex_count` | Count of vertices in this group. | CONFIRMED |
| +0x10 | 4 | u32 | `index_count` | Count of u16 indices in this group. | CONFIRMED |

**VertexData (per group):** `vertex_count × 36` bytes (VF_36).

**IndexData (per group):** `index_count × 2` bytes (u16).

**File-size formula (single group):** `4 + 20 + vertex_count × 36 + index_count × 2`.

Single-group samples verify exactly (e.g. a 3-vertex / 3-index group: `4 + 20 + 3 × 36 + 3 × 2`).
Multi-group files sum the per-group block sizes over `group_count` groups.

### 1.6 FX2 Format  (`.fx2`)

**Status:** CONFIRMED — group-array model (§1.1a); single-group samples verified by exact size.
**Semantic:** Same role as FX1 but with a second UV channel (UV1). Used for lightmap or secondary
texture blending. Vertex format: VF_44.

**File layout:** identical structure to FX1 (group-array model, §1.1a) with the VF_44 vertex stride.
The `render_state` group word is **variable** (the prior "also 15" claim is REFUTED — see §1.1a).

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `group_count` | CONFIRMED |
| per-group header | — | — | Same fields as FX1 / §1.1a | CONFIRMED (render_state CONFIRMED-variable) |

**VertexData (per group):** `vertex_count × 44` bytes (VF_44). The extra 8 bytes per vertex are
`f32 U1, f32 V1`.

**IndexData (per group):** `index_count × 2` bytes (u16).

**File-size formula (single group):** `4 + 20 + vertex_count × 44 + index_count × 2`.

### 1.7 FX3 Format  (`.fx3`)

**Status:** CONFIRMED — group-array model (§1.1a); single-group samples verified by exact size.
**Semantic:** Quad terrain overlay (4 vertices, 6 indices = 2 triangles). Vertex format: VF_36.
The group header carries additional leading words ahead of the `vertex_count` / `index_count` pair
(a longer group header than FX1/FX2); those extra words are read but not consumed for layout and are
not understood semantically. This is the same group-array model with a wider group header, not a
distinct format.

**File layout:**

```
FX3_File = group_count (u32) + group_count × [ group header (44 B) + VertexData + IndexData ]
```

**Group header (per group — extended; offsets group-relative):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `group_count` | File-level group count (§1.1a). | CONFIRMED |
| +0x00 | 4 | u32 | `group_flags_0` | Read-but-not-consumed; near-constant 1. | UNVERIFIED |
| +0x04 | 4 | u32 | `group_flags_1` | Read-but-not-consumed; mostly 0. | UNVERIFIED |
| +0x08 | 4 | u32 | `render_state` | Per-group render-state word. **Variable** — earlier "constant=5" claim REFUTED. | CONFIRMED-variable |
| +0x0C | 4 | u32 | _unknown_3_ | Read-but-not-consumed. | UNVERIFIED |
| +0x10 | 4 | f32 | _unknown_4_ | A float; candidate scale factor. | UNVERIFIED |
| +0x14 | 4 | u32/f32 | _unknown_5_ | Candidate direction-vector component (decodes as a small fractional float in samples). | UNVERIFIED |
| +0x18 | 4 | u32 | _unknown_6_ | Read-but-not-consumed. | UNVERIFIED |
| +0x1C | 4 | u32 | _unknown_7_ | Read-but-not-consumed. | UNVERIFIED |
| +0x20 | 4 | u32 | _unknown_8_ | Observed constant 0. | UNVERIFIED (constant=0) |
| +0x24 | 4 | u32 | `vertex_count` | Count of vertices in this group. | CONFIRMED |
| +0x28 | 4 | u32 | `index_count` | Count of u16 indices in this group. | CONFIRMED |

**VertexData (per group):** `vertex_count × 36` bytes (VF_36).

**IndexData (per group):** `index_count × 2` bytes (u16). Observed: `[0, 1, 2, 1, 3, 2]` (standard
quad split).

**File-size formula (single group):** `4 + 44 + vertex_count × 36 + index_count × 2`.

### 1.8 FX5 Format  (`.fx5`)

**Status:** CONFIRMED — group-array model (§1.1a); flat per-group iteration verified across all
sampled files (group counts observed from 1 to over a hundred).
**Semantic:** Multi-group terrain overlay mesh with per-group direction metadata in the group header.
Groups may represent different LOD levels or independently-textured geometry groups.
Vertex format: VF_36.

**File layout:**

```
FX5_File = group_count (u32) + group_count × [ group header (48 B) + VertexData + IndexData ]
```

The number of groups is the file-level `group_count` word — it is **not** absent and **not** derived
from accumulated sizes (that earlier "section count not stored" reading is superseded by §1.1a).

**Group header (per group — 48 B; offsets group-relative):**

| Offset | Size | Type | Field | Observed values | Confidence |
|-------:|-----:|------|-------|-----------------|------------|
| +0x00 | 4 | u32 | `group_subtype` | small ascending integer | UNVERIFIED semantic |
| +0x04 | 4 | u32 | _unknown_1_ | 0 or 1 | UNVERIFIED |
| +0x08 | 4 | u32 | _unknown_2_ | candidate LOD distance (e.g. 300/400/450) | UNVERIFIED |
| +0x0C | 4 | f32 | `direction_x` | fractional | CONFIRMED as a float; semantic UNVERIFIED |
| +0x10 | 4 | f32 | `direction_y` | fractional | CONFIRMED as a float; semantic UNVERIFIED |
| +0x14 | 4 | f32 | `direction_z` | fractional | CONFIRMED as a float; semantic UNVERIFIED |
| +0x18 | 4 | u32 | _unknown_3_ | integer parameter | UNVERIFIED |
| +0x1C | 4 | u32 | _unknown_4_ | small integer | UNVERIFIED |
| +0x20 | 4 | u32 | _unknown_5_ | 0 or 1 flag | UNVERIFIED |
| +0x24 | 4 | u32 | _unknown_6_ | small integer | UNVERIFIED |
| +0x28 | 4 | u32 | `vertex_count` | Count of vertices in this group. | CONFIRMED |
| +0x2C | 4 | u32 | `index_count` | Count of u16 indices in this group. | CONFIRMED |

The direction vector at group-relative `+0x0C`–`+0x17` is an oblique near-unit vector per group. Its
purpose is unconfirmed; candidates include slope normal, LOD orientation, or instancing direction.

**VertexData (per group):** `vertex_count × 36` bytes (VF_36). Fractional normals indicate slope geometry.

**IndexData (per group):** `index_count × 2` bytes (u16). Triangle list.

**File-size formula:** `4 + Σ over groups (48 + vertex_count × 36 + index_count × 2)`.

### 1.9 FX6 Format  (`.fx6`)

**Status:** CONFIRMED — group-array model (§1.1a); multiple byte-identical samples.
**Semantic:** Collection of 3D prop or object mesh groups. Each group is an independent small
box-like mesh. Unlike FX1–FX5/FX7, FX6 vertices have no per-vertex colour field (VF_32) and use
normals consistent with 45-degree box faces rather than terrain slopes.

**File layout:**

```
FX6_File = group_count (u32) + global metadata + group_count × [ group header (8 B) + VertexData + IndexData (+ optional trailing block) ]
```

FX6 places several global metadata words after the `group_count` word; each group then carries a
short 8-byte header (vertex count + index count). The trailing block on non-final groups is a fixed
extra block (see below). The group iteration is the same model as §1.1a; FX6 simply uses a compact
per-group header.

**File-level / global metadata:**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | `group_count` | group count (constant 40 across samples) | CONFIRMED |
| 0x04 | 4 | u32 | _version_ | dominant 1 (one observed outlier 2) | CONFIRMED-variable (dominant 1) |
| 0x08 | 4 | u32 | _unknown_1_ | 0 | UNVERIFIED (constant=0) |
| 0x0C | 4 | u32 | _unknown_2_ | 0 | UNVERIFIED (constant=0) |
| 0x10 | 4 | f32 | _unknown_3_ | 1.0 | UNVERIFIED (possibly global scale; constant=1.0) |
| 0x14 | 4 | u32 | _unknown_4_ | 45 | UNVERIFIED (constant=45) |
| 0x18 | 4 | u32 | _unknown_5_ | 60 | UNVERIFIED (constant=60) |
| 0x1C | 4 | u32 | _unknown_6_ | 0 | UNVERIFIED (constant=0) |

**Per-group header (8 bytes):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| +0x00 | 4 | u32 | `vertex_count` | 20 | CONFIRMED |
| +0x04 | 4 | u32 | `index_count` | 30 | CONFIRMED |

**VertexData (per group):** `vertex_count × 32` bytes (VF_32). Box/prop geometry with 45-degree normals.

**IndexData (per group):** `index_count × 2` bytes (u16).

**Trailing block (28 bytes, non-final groups only):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| +0x00 | 4 | u32 | _unknown_a_ | 1 | UNVERIFIED (constant=1) |
| +0x04 | 4 | u32 | _unknown_b_ | 0 | UNVERIFIED (constant=0) |
| +0x08 | 4 | u32 | _unknown_c_ | 0 | UNVERIFIED (constant=0) |
| +0x0C | 4 | f32 | _unknown_d_ | 1.0 | UNVERIFIED (possibly per-group scale) |
| +0x10 | 4 | u32 | _unknown_e_ | varies (10, 40, 45, 50, …) | UNVERIFIED |
| +0x14 | 4 | u32 | _unknown_f_ | varies (30, 60; often 60) | UNVERIFIED |
| +0x18 | 4 | u32 | _unknown_g_ | 0 | UNVERIFIED (constant=0) |

The varying values at trailing-block offsets `+0x10` and `+0x14` differ per group and may encode LOD
distances, texture tile indices, or rendering priority parameters.

**File-size formula:**
`4 + 28 (remaining global metadata) + (group_count - 1) × 736 + 708`
where each non-final group block is `8 + 20 × 32 + 30 × 2 + 28 = 736` and the final group block is
`8 + 640 + 60 = 708`.

### 1.10 FX7 Format  (`.fx7`)

**Status:** PLAUSIBLE — group-array model (§1.1a); 2 byte-identical samples, size formula exact.
**Semantic:** Single-group terrain overlay mesh using the **FX5-style group header** but the
**VF_32** vertex format (no per-vertex colour). The two observed files are a single group with a high
vertex count and a plain position/normal/UV0 vertex.

**File layout:**

```
FX7_File = group_count (u32) + group_count × [ group header (48 B) + VertexData + IndexData ]
```

The group header is the same 48-byte FX5-style group header (§1.8); only the vertex stride differs
(VF_32 instead of VF_36).

**Group header (per group — 48 B; offsets group-relative):**

| Offset | Size | Type | Field | Observed values | Confidence |
|-------:|-----:|------|-------|-----------------|------------|
| +0x00 | 4 | u32 | `group_subtype` | 1 | DUAL-SAMPLE |
| +0x04 | 4 | u32 | _unknown_1_ | 2 | DUAL-SAMPLE |
| +0x08 | 4 | f32 | `unk_dist` | large float (thousands) | DUAL-SAMPLE — a large f32 (candidate world-space coordinate), NOT the FX5 small-integer LOD distance |
| +0x0C | 4 | f32 | _unknown_2_ | large float | DUAL-SAMPLE — candidate world-space coordinate |
| +0x10 | 4 | f32 | _unknown_3_ | large float | DUAL-SAMPLE — candidate world-space coordinate |
| +0x14 | 4 | f32 | `direction_a` | signed near-unit fractional | DUAL-SAMPLE — a horizontal-plane unit-direction component (paired with `direction_b`) |
| +0x18 | 4 | u32/f32 | _unknown_5_ | 0 | DUAL-SAMPLE (zero) |
| +0x1C | 4 | f32 | `direction_b` | signed near-unit fractional | DUAL-SAMPLE — second unit-direction component (`direction_a`² + `direction_b`² ≈ 1) |
| +0x20 | 4 | u32 | _unknown_7_ | 1 | DUAL-SAMPLE |
| +0x24 | 4 | u32 | _unknown_8_ | 0 | DUAL-SAMPLE |
| +0x28 | 4 | u32 | _unknown_flags_ | 0 | DUAL-SAMPLE |
| +0x2C | 4 | u32 | `vertex_count` | large | DUAL-SAMPLE |
| +0x30 | 4 | u32 | `index_count` | large; divisible by 3 | DUAL-SAMPLE |

**VertexData (per group):** `vertex_count × 32` bytes (**VF_32** — position 12 B + normal 12 B +
UV0 8 B; **no per-vertex colour field**). Same VF_32 used by FX6 (§1.2), not FX5's VF_36.

**IndexData (per group):** `index_count × 2` bytes (u16). Plain triangle list (`index_count`
divisible by 3).

**File-size formula:** `4 + 48 + vertex_count × 32 + index_count × 2` (single group). Both samples
satisfy it exactly with zero residual bytes.

### 1.11 FX4 Format  (`.fx4`)

**Status:** CONFIRMED-FROM-LOADER — the FX4 loader's read sequence was recovered and obeys the
universal group-array model (§1.1a). The file is a flat group array: a group count, then per group a
fixed 48-byte group header (with vertex/index counts at fixed offsets), a VF_44 vertex block, and a
u16 index block. The internal meaning of the per-group header's leading words remains sample-unverified.

**Semantic:** Terrain overlay mesh using the **VF_44** vertex format (position + normal + RGBA colour
+ UV0 + UV1 — the same vertex format as FX2, §1.2), stored as a flat array of groups. This is the
same flat group-array model the FX5 loader uses; FX4 and FX5 differ **only** in vertex stride
(FX4 = VF_44 / 44 B, FX5 = VF_36 / 36 B).

> **Group-array confirmation.** The loader reads a single file-level group count, then for **each**
> group reads one fixed **48-byte** header block as a single unit and consumes only two fields from it
> (vertex count, index count). There is no branch on any leading word and no header split. The
> per-group header size is fixed at 48 bytes, not data-driven. A file that earlier looked like "two
> sections" is simply a file with **group count = 2**.

**File layout:**

```
FX4_File = u32 group_count
           per group (× group_count):
               group header (48 bytes)        ; only vertex_count@+0x28 and index_count@+0x2C consumed
               VertexData (vertex_count × 44)  ; VF_44
               IndexData  (index_count  × 2)   ; u16 triangle list
```

**File header (4 bytes):**

| Offset | Size | Type | Field | Notes | Confidence |
|-------:|-----:|------|-------|-------|------------|
| 0x00 | 4 | u32 | `group_count` | Number of groups in the flat array. Drives the per-group loop. | CONFIRMED (parser-verified; two-witness) |

**Per-group header (48 bytes, repeated `group_count` times; offsets group-relative):**

The loader reads this 48-byte block in one operation and indexes only the vertex and index counts
out of it.

| Group-rel offset | Size | Type | Field | Notes | Confidence |
|-----------------:|-----:|------|-------|-------|------------|
| +0x00 | 40 | bytes | _group_metadata_ | Read into the group struct but not consumed for parsing (no branch on any field). Candidates: transform / texture-id / flags / a per-group range value for the post-load AABB compute. The leading word is **not** tested and does **not** size the header. | UNVERIFIED (read-but-not-consumed at load) |
| +0x28 | 4 | u32 | `vertex_count` | Drives the `vertex_count × 44` VF_44 read. | CONFIRMED (parser-verified) |
| +0x2C | 4 | u32 | `index_count` | Drives the `index_count × 2` u16 index read. | CONFIRMED (parser-verified) |

**VertexData (per group):** `vertex_count × 44` bytes (**VF_44**). The leading position float3 (X, Y,
Z) is parser-verified (the post-load AABB compute strides by 44 bytes and reads the first three floats
of each vertex as position). The remaining 32 bytes (normal + RGBA + UV0 + UV1, per the VF_44 layout
in §1.2) are not decomposed by the loader; that breakdown is inherited from §1.2.

**IndexData (per group):** `index_count × 2` bytes (u16). Plain triangle list.

**File-size formula:** `4 + Σ over groups (48 + vertex_count × 44 + index_count × 2)`.

**Cross-confirmation (FX5):** the FX5 loader is byte-for-byte the same control flow — a u32 group
count, then per group a fixed 48-byte header read as one unit (vertex count at group-relative `+0x28`,
index count at `+0x2C`), a vertex block, and a u16 index block — differing **only** in vertex stride
(VF_36 / 36 B instead of VF_44 / 44 B). This cross-family identity is the strongest evidence that the
48-byte per-group header is a fixed, type-agnostic block in both formats, and that the leading word is
a group count rather than a selector.

### 1.12 FX layer summary table

| Layer | sample_verified | Group header size | Vertex format | UV channels | Per-vertex colour |
|-------|-----------------|------------------:|---------------|:-----------:|:-----------------:|
| FX1   | true  | 20 B | VF_36 (36 B) | 1 | yes |
| FX2   | true  | 20 B | VF_44 (44 B) | 2 | yes |
| FX3   | true  | 44 B | VF_36 (36 B) | 1 | yes |
| FX4   | CONFIRMED-from-loader (flat group array) | 48 B/group | VF_44 (44 B) | 2 | yes |
| FX5   | true | 48 B/group | VF_36 (36 B) | 1 | yes |
| FX6   | true  | global metadata + 8 B/group | VF_32 (32 B) | 1 | no |
| FX7   | PLAUSIBLE (dual-sample) | 48 B/group | VF_32 (32 B) | 1 | no |

All seven share the universal group-array model (§1.1a): a `group_count` word, then that many group
records; the only structural difference is the vertex stride.

### 1.13 Per-channel header width and stride are MANDATORY (reconfirmed 2026-06-16, two-witness)

The leading `u32` is the group/tile count for **every** channel (branchless), **but the header width
and the vertex stride are PER-CHANNEL** — a single hard-coded FX stride or header size is a parser
bug. The VFS census (two-witness: per-channel loader read-path + black-box, zero residual on each
channel) pins the following:

| Ext  | Header        | Vertex format | Census files |
|------|---------------|---------------|-------------:|
| fx1  | 20 B group hdr | VF_36 (36 B) | 226 |
| fx2  | 20 B group hdr | VF_44 (44 B) | 595 |
| fx3  | 44 B group hdr | VF_36 (36 B) | 160 |
| fx4  | 48 B tile hdr  | VF_44 (44 B) | 1 |
| fx5  | 48 B tile hdr  | VF_36 (36 B) | 89 |
| fx6  | 32 B global hdr + VF_32 sub-chunks + 28 B footer | VF_32 (32 B) | 6 |
| fx7  | 52 B hdr (`vertex_count` @ +0x2C, `index_count` @ +0x30) | VF_32 (32 B) | 2 |

(The §§1.5–1.11 worked layouts are these same per-channel instances; the table above is the
authoritative stride/header index. Census file counts are corpus observations, not load-bearing
layout facts.)

---

## Section 2: Upper Terrain File  (`.up`)

| Format key | sample_verified |
|-----------|-----------------|
| `up_terrain` | `true` — 3 samples, exact size match, parser corroborated |

**Purpose:** Stores the triangle mesh of **overhanging terrain surfaces** (bridges, raised platforms,
overhangs) for one cell. The client tests player position against these triangles for collision at runtime.

> **`.up` / `.exd` are a SHARED, DISTINCT format — NOT FX group-array files (reconfirmed 2026-06-16).**
> Both `.up` and `.exd` use the **one** layout `count(u32) + count × 40-byte triangle` (3 × vec3 + 1
> trailing f32) and are decoded by the **same** record decoder. This is structurally **distinct** from
> the FX group-array model of §1; any reading that lumps `.exd`/`.up` in with the FX channels is
> **REFUTED**. VFS census (two-witness, zero residual): **`.up` 222 files, `.exd` 1 384 files**.

**Path pattern:** `data/map{MAP}/dat/d{MAP}x1{TX:04d}z1{TZ:04d}.up`

### 2.1 File layout

No magic number, no version field. The file begins directly with the triangle count.

**File header (4 bytes):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `triangle_count` | CONFIRMED — file size = 4 + `triangle_count` × 40 |

**Triangle record (40 bytes, repeated `triangle_count` times):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0x00 | 4 | f32 | `v1_x` | CONFIRMED |
| +0x04 | 4 | f32 | `v1_y` | CONFIRMED |
| +0x08 | 4 | f32 | `v1_z` | CONFIRMED |
| +0x0C | 4 | f32 | `v2_x` | CONFIRMED |
| +0x10 | 4 | f32 | `v2_y` | CONFIRMED |
| +0x14 | 4 | f32 | `v2_z` | CONFIRMED |
| +0x18 | 4 | f32 | `v3_x` | CONFIRMED |
| +0x1C | 4 | f32 | `v3_y` | CONFIRMED |
| +0x20 | 4 | f32 | `v3_z` | CONFIRMED |
| +0x24 | 4 | f32 | `plane_height` | CONFIRMED — an independent per-triangle scalar; see §2.2 |

Record stride: **40 bytes** (0x28).

**File-size formula:** `4 + triangle_count × 40`

### 2.2 Geometric notes

`plane_height` is an **independent per-triangle scalar**, not a redundant copy of vertex Y. In some
cells all three vertex Y values within a record equal `plane_height` (flat coplanar surface); in many
others `plane_height` differs from one or more vertex Y values (non-flat overhangs). An engineer must
treat `plane_height` as a separate stored value (likely a pre-computed height / elevation bound used
for spatial culling or collision range tests), not as derivable from the vertices.

### 2.3 Runtime in-memory expansion (informational — not serialised)

The loader derives a **72-byte in-memory element** per triangle:

| Slots | Source | Content |
|-------|--------|---------|
| [0..3] | computed | AABB: min_x, min_z, max_x, max_z |
| [4..6] | disk +0x00 | vertex 1: x, y, z |
| [7..9] | disk +0x0C | vertex 2: x, y, z |
| [10..12] | disk +0x18 | vertex 3: x, y, z |
| [13..16] | computed | Plane equation (Nx, Ny, Nz, D); N = normalize((v2-v1) × (v3-v1)), D = -dot(N, v1) |
| [17] | disk +0x24 | `plane_height` scalar |

---

## Section 3: Extra Terrain Collision File  (`.exd`)

| Format key | sample_verified |
|-----------|-----------------|
| `exd_terrain` | `true` — 3 samples, exact size match, parser corroborated |

**Purpose:** Stores supplementary 3D collision triangles that overlay or extend the base `.ted`
height-field for one cell. Provides flat platforms, ramps, or structural floors not well-expressed
by the height grid alone. Referenced from the per-cell `.map` scene descriptor under
`EXTRA_TERRAIN { DATAFILE ... }`.

The binary layout is **identical** to the `.up` format (Section 2). The same record-decoder
function is called for both `.exd` and `.up` files, and both use the identical 40-byte triangle
record structure with the same field layout.

**Path pattern:** `data/map{MAP}/dat/d{MAP}x{TX}z{TZ}.exd`
(Note: unlike `.up`, the raw cell coordinate digits are used directly in the filename — no `1`
prefix — as confirmed by observed filenames such as `d001x9997z10003.exd`.)

### 3.1 File layout

No magic number, no version field.

**File header (4 bytes):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `triangle_count` | CONFIRMED — file size = 4 + `triangle_count` × 40 |

**Triangle record (40 bytes, repeated `triangle_count` times):**

Identical to the `.up` triangle record — see Section 2.1. Field names and offsets are the same;
`plane_height` at record offset +0x24 carries the same independent-scalar semantics as in §2.2.

Record stride: **40 bytes** (0x28).

**File-size formula:** `4 + triangle_count × 40`

### 3.2 Comparison with `.up`

| Property | `.up` | `.exd` |
|----------|-------|--------|
| Binary layout | Identical | Identical |
| Scene section key | `UP_TERRAIN` | `EXTRA_TERRAIN` |
| Loader function | distinct (but same record decoder) | distinct (but same record decoder) |

---

## Section 4: Collision Polygon Vertex Cache  (`.sod.pre`)

| Format key | sample_verified |
|-----------|-----------------|
| `sod_pre` | `true` — 3 samples, exact size match, cross-verified with companion `.sod` data |

**Purpose:** Caches the XZ world-space vertex positions of the collision polygon(s) defined in
the companion `.sod` file. Where the `.sod` format stores collision geometry as slope-intercept
line segments, the `.sod.pre` stores the polygon corner points directly for faster spatial
lookups. This file appears to be written by the editor toolchain; no runtime loader was
identified in the client binary.

**Path pattern:** `data/map{MAP}/dat/d{MAP}x{TX}z{TZ}.sod.pre`

The double extension means: `.sod` is the parent format; `.pre` indicates a precomputed sidecar.

### 4.1 File layout

No magic number.

**File header (8 bytes):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | `version` | 1 | CONFIRMED (constant=1 in all 3 samples) |
| 0x04 | 4 | u32 | `vertex_count` | 3 or 4 | CONFIRMED — equals segment count in companion `.sod` |

**Vertex record (8 bytes, repeated `vertex_count` times):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0x00 | 4 | f32 | `world_x` | CONFIRMED — exact coordinate match with `.sod` segment endpoints |
| +0x04 | 4 | f32 | `world_z` | CONFIRMED — exact coordinate match with `.sod` segment endpoints |

Record stride: **8 bytes**.

**File-size formula:** `8 + vertex_count × 8`

### 4.2 Coordinate verification

All vertex XZ positions confirmed to lie within the cell's world-space bounding box
`[(cellX-10000)*1024, (cellX-10000)*1024+1024] × [(cellZ-10000)*1024, (cellZ-10000)*1024+1024]`.

---

## Section 5: Terrain Editor Post-Processed Snapshot  (`.ted.post`)

| Format key | sample_verified |
|-----------|-----------------|
| `ted_post` | `true` — 3 samples, all 46 987 bytes; layout matches `.ted` block structure |

**Purpose:** Written by the in-game terrain editor as a full-state snapshot of all five terrain
data blocks at the time of editor save. After writing this file, the editor patches only block 5
of the companion `.ted` file. The `.ted.post` therefore preserves the pre-patch state of all
blocks. The runtime client does **not** read `.ted.post`; the file is an editor artifact.

**Path pattern:** `data/map{MAP}/dat/d{MAP}x{TX}z{TZ}.ted.post`

The double extension means: `.ted` is the parent format; `.post` indicates post-processed editor output.

### 5.1 File layout

The binary layout is **identical to the `.ted` terrain file** — five contiguous fixed-size blocks
with no file-level header, no magic, and no version field. The block structure is:

| Block | Byte offset | Size (bytes) | Name | Confidence |
|------:|------------:|-------------:|------|------------|
| 1 | 0 | 16 900 | `height_map` | CONFIRMED |
| 2 | 16 900 | 12 675 | `normal_map` | CONFIRMED |
| 3 | 29 575 | 256 | texture index map | CONFIRMED |
| 4 | 29 831 | 256 | direction / orientation map | CONFIRMED |
| 5 | 30 087 | 16 900 | `diffuse_map` | CONFIRMED |

**Total file size:** 46 987 bytes (fixed, same as `.ted`).

For full block-level field details (grid dimensions, value encoding, normal decoding factor) see
`Docs/RE/formats/terrain.md` — the block layout is the same.

### 5.2 Relationship to `.ted`

```
The terrain editor's diffuse-save routine writes:
    .ted.post  <-- all 5 blocks (full snapshot at editor-save time)
    .ted        <-- only block 5, at byte offset 30087 (in-place patch of diffuse map)
```

The `.ted.post` allows the editor to revert or re-export the complete terrain state independently
of the live `.ted` the runtime reads.

---

## Section 5.5: FX Editor Sidecars  (`.fx1.pre` – `.fx7.pre`)

| Format key | sample_verified |
|-----------|-----------------|
| `fx_pre` | `true` (sidecar existence + leading group-count word) — editor-only; **NOT read by the client runtime** |

**Purpose:** Editor-toolchain sidecars that sit on disk beside the runtime FX layer files
(`.fx1`–`.fx7`). They belong to the same editor-sidecar family as `.sod.pre` (Section 4) and
`.ted.post` (Section 5): a precomputed/editor-state companion to a runtime asset.

**Path pattern:** `data/map{MAP}/dat/d{MAP}x1{TX:04d}z1{TZ:04d}.fx{N}.pre`
(the parent `.fx{N}` runtime file's path with a `.pre` suffix appended).

**Runtime status — DO NOT READ at runtime.** The runtime client opens **only** the `.fxN` file named
by the `DATAFILE` entry in the per-cell `.map` descriptor (§1.1b). The matching `.fxN.pre` is **never
opened by the runtime parser**. A runtime FX reader must **skip** any `.fx{N}.pre` file it encounters
during a directory scan, exactly as it skips `.sod.pre`, `.ted.post`, and the sky `.txt` siblings.

**Layout (observation only).** A `.fxN.pre` shares the **leading `u32` group count** with its runtime
`.fxN` counterpart, but its per-group header content and its overall file size **differ** from the
runtime file. The internal sidecar layout was **not** decoded (it is outside the runtime path and not
required for the client). Treat the body as **opaque editor data** — do not assume it matches the
runtime FX group layout of Section 1.

---

## Section 6: Directional / Ambient Light Keyframes  (`light%d.bin`)

| Format key | sample_verified |
|-----------|-----------------|
| `bin_light_dir` | `false` — no samples available; parser analysis only |

**Purpose:** Stores a 48-step day/night cycle of directional and ambient sky-light colours, plus
a per-step fog scalar, for one map. The client interpolates between adjacent time steps to produce
continuously changing lighting.

**Path pattern:** `data/sky/dat/light{MAP}.bin`

**File size:** exactly **5312 bytes** (0x14C0). Fixed-size, no count field.

**No magic number, no version field.** If the file is absent, the client uses hard-coded fallback
lighting values.

> **Sibling note:** `data/sky/dat/` also holds `light{MAP}.txt` text siblings with sizes that do
> **not** equal 5312 bytes. They are editor-source / text exports and are **not** parseable as this
> binary format; the binary reader should open only the `.bin` files.

### 6.1 Blob layout

The 5312-byte file is divided into three contiguous sections:

| Byte range | Size | Section | Description |
|:----------:|-----:|---------|-------------|
| 0x0000–0x08FF | 2304 | A — Directional | 48 keyframe slots × 48 bytes |
| 0x0900–0x092F | 48 | _gap / extra slot_ | Purpose UNVERIFIED (see Known Unknowns) |
| 0x0930–0x122F | 2304 | B — Ambient | 48 keyframe slots × 48 bytes |
| 0x1230–0x125F | 48 | _gap / extra slot_ | Purpose UNVERIFIED |
| 0x1260–0x131F | 192 | C — Fog scalar | 48 keyframe slots × 4 bytes |
| 0x1320–0x14BF | 416 | _trailing region_ | Not accessed by analysed functions; UNVERIFIED |

### 6.2 Section A — Directional light keyframe slot (48 bytes)

Slot `i` starts at byte offset `48 × i` within section A (absolute: `48 × i`).

| Slot offset | Size | Type | Field | Confidence |
|:-----------:|-----:|------|-------|------------|
| +0x00 | 4 | f32 | sun_colour[0] | CONFIRMED (written to live sun_colour field by time-update) |
| +0x04 | 4 | f32 | sun_colour[1] | CONFIRMED |
| +0x08 | 4 | f32 | sun_colour[2] | CONFIRMED |
| +0x0C | 4 | f32 | _unknown_0_ | UNVERIFIED (not read by main update path) |
| +0x10 | 4 | f32 | _unknown_1_ | UNVERIFIED |
| +0x14 | 4 | f32 | moon_colour[1] | CONFIRMED as read from current slot (index 5) |
| +0x18 | 4 | f32 | moon_colour[2] | CONFIRMED as read from current slot (index 6) |
| +0x1C | 4 | f32 | _unknown_2_ | UNVERIFIED |
| +0x20 | 4 | f32 | sky_colour[0] | CONFIRMED (written to third live colour field) |
| +0x24 | 4 | f32 | sky_colour[1] | CONFIRMED |
| +0x28 | 4 | f32 | sky_colour[2] | CONFIRMED |
| +0x2C | 4 | f32 | _unknown_3_ | UNVERIFIED |

**Phase-offset note:** `moon_colour[0]` is read from **slot `(i + 30) mod 48`** of section A
(not the current slot), producing a half-cycle phase offset. `moon_colour[1]` and `moon_colour[2]`
are read from the current slot at offsets +0x14 and +0x18. This asymmetry is confirmed by the
client time-update function.

**Cycle:** 48 steps spanning one full day/night cycle; interpolation period between adjacent slots
is 1800 ms.

### 6.3 Section B — Ambient light keyframe slot (48 bytes)

Structurally identical to section A. Slot `i` starts at absolute offset `0x0930 + 48 × i`.
The update function writes to separate `ambient_*` live fields in the lighting manager. The
same field positions (0–2, 5–6, 8–10) are read; offsets +0x0C, +0x10, +0x1C, +0x2C are not
read by the analysed function.

### 6.4 Section C — Fog scalar (4 bytes per slot)

Slot `i` at absolute offset `0x1260 + 4 × i`. One `f32` value per time step.

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x1260 + 4i | 4 | f32 | `fog_density` | MEDIUM — confirmed as a single float that conditionally triggers fog parameter update; `1.0` is the no-override sentinel |

---

## Section 7: Point Light Array  (`point_light%d.bin`)

| Format key | sample_verified |
|-----------|-----------------|
| `bin_light_point` | header partially corpus-confirmed (intensity scale as f32, count); record body parser-only |

**Purpose:** Stores a variable-count array of Direct3D point lights for one map, with a global
intensity scale applied to all lights.

**Path pattern:** `data/sky/dat/point_light{MAP}.bin`

**File size:** `8 + count × 60` bytes. No magic, no version.

> **Sibling note:** `point_light{MAP}.txt` text siblings coexist in `data/sky/dat/`; they are editor
> text exports, not this binary format. Open only the `.bin` files.

### 7.1 File header (8 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | f32 | `intensity_scale` | CONFIRMED — a **u32 read then reinterpreted as f32**; the loader treats the 4 bytes as a float colour multiplier (observed whole-number float values across the corpus). Store and use it as an f32, not an integer. |
| 0x04 | 4 | u32 | `count` | CONFIRMED — number of point-light entries |

### 7.2 Point-light record (60 bytes, repeated `count` times)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0x00 | 4 | f32 | `colour_group_1[0]` | CONFIRMED (scaled by `intensity_scale` at runtime) |
| +0x04 | 4 | f32 | `colour_group_1[1]` | CONFIRMED |
| +0x08 | 4 | f32 | `colour_group_1[2]` | CONFIRMED |
| +0x0C | 4 | f32 | `colour_group_2[0]` | CONFIRMED |
| +0x10 | 4 | f32 | `colour_group_2[1]` | CONFIRMED |
| +0x14 | 4 | f32 | `colour_group_2[2]` | CONFIRMED |
| +0x18 | 4 | f32 | `colour_group_3[0]` | CONFIRMED |
| +0x1C | 4 | f32 | `colour_group_3[1]` | CONFIRMED |
| +0x20 | 4 | f32 | `colour_group_3[2]` | CONFIRMED |
| +0x24 | 4 | f32 | _unknown_0_ | UNVERIFIED (possibly position X) |
| +0x28 | 4 | f32 | _unknown_1_ | UNVERIFIED (possibly position Y) |
| +0x2C | 4 | f32 | _unknown_2_ | UNVERIFIED (possibly position Z) |
| +0x30 | 4 | f32 | _unknown_3_ | UNVERIFIED (possibly light range) |
| +0x34 | 4 | u32 | `enabled_flag` | CONFIRMED — 0 = active; non-zero = skip this entry |
| +0x38 | 4 | u32 | _unknown_4_ | UNVERIFIED (possibly padding or light type) |

Record stride: **60 bytes**.

The three colour groups may correspond to D3D diffuse, specular, and ambient channels, or to three
separate light sources — this cannot be determined from parser evidence alone.

Up to 5 point lights from the array may be simultaneously active; the lighting manager maintains
5 per-slot state blocks of 184 bytes each.

---

## Section 8: Wind / Foliage-Sway Keyframes  (`wind%d.bin`)

| Format key | sample_verified |
|-----------|-----------------|
| `bin_light_wind` | `true` for header; `false` for entry body (observed samples are zero- or low-count) |

**Purpose:** Stores keyframe entries that drive foliage-sway animations for one map.

**Path pattern:** `data/sky/dat/wind{MAP}.bin`

**File size:** `8 + count × 24` bytes (or exactly 8 bytes when `count = 0`).

**No magic, no version.** Missing file or zero-count file produces no foliage sway.

> **Sibling note:** `wind{MAP}.txt` text siblings coexist in `data/sky/dat/`; they are editor text
> exports, not this binary format. Open only the `.bin` files.

### 8.1 File header (8 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `count` | CONFIRMED — zero-entry samples verify 8-byte header |
| 0x04 | 4 | u32 | `flag2` | CONFIRMED — non-zero activates foliage-sway curve seeding |

### 8.2 Wind keyframe record (24 bytes, repeated `count` times)

Six consecutive 4-byte values. Field [5] (offset +0x14) is the texture id; the remaining fields are
structurally inferred.

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0x00 | 4 | f32 | _unknown_0_ | UNVERIFIED (possibly time key or direction X) |
| +0x04 | 4 | f32 | _unknown_1_ | UNVERIFIED (possibly direction Y) |
| +0x08 | 4 | f32 | _unknown_2_ | UNVERIFIED (possibly direction Z) |
| +0x0C | 4 | f32 | _unknown_3_ | UNVERIFIED (possibly wind speed) |
| +0x10 | 4 | f32 | _unknown_4_ | UNVERIFIED (possibly frequency or phase) |
| +0x14 | 4 | u32 | `texture_id` | CONFIRMED — this word is a **texture id**, the argument the loader passes to the foliage-sway material lookup. The earlier `sway_seed` reading is REFUTED. |

Record stride: **24 bytes**.

---

## Known Unknowns

The following items are explicitly unresolved. An engineer must not assume a meaning for any of
these without further evidence.

1. **FX group-header semantics (all FX extensions):** Beyond the universal `group_count`,
   `render_state`, `vertex_count`, and `index_count` fields (§1.1a), most leading group-header words
   are read-but-not-consumed for layout. Their meanings (LOD distance, direction vector, sub-type,
   flags) are inferred, not confirmed. Treat them as opaque per-group metadata.

2. **`render_state` (group-relative +0x08) semantic:** Confirmed **variable** (not a constant), but
   what it selects — a render-state enum, a texture-binding index, or a blend-mode key — is not yet
   established. Read it per group; do not branch on a fixed value.

3. **FX3 extended group-header words (+0x0C–+0x20):** Several u32/f32 words ahead of the count pair.
   The float-decoding ones look like direction-vector components; the integer ones are unexplained.

4. **FX6 global metadata at 0x14 (=45) and 0x18 (=60):** Constant across samples.
   Candidates: grid dimension, group count, or rendering parameter. `version` (0x04) is dominant 1
   with one observed outlier 2.

5. **FX6 per-group trailing-block fields at +0x10 and +0x14:** Vary per group. Candidates: LOD
   distances, texture tile indices, rendering priority.

6. **FX7 group-header large floats (`unk_dist` and the two following floats):** PLAUSIBLE from two
   byte-identical samples; candidates are world-space bounding coordinates. The signed near-unit
   floats at +0x14 and +0x1C form a horizontal-plane unit-direction vector.

7. **FX4 per-group header leading 40 bytes:** Read but not consumed at load (candidates: transform /
   texture-id / flags). Single FX4 instance in the corpus → all per-field observations are
   single-sample.

7a. **Water render path for FX3 / FX5:** The channel identity is settled — FX3 and FX5 take the
    dedicated "water layer" build path (§1.4c). What that path does at render time (the water shader,
    how the per-group elevation/direction word in §§1.7–1.8 drives it) is **not** traced. DBG-PENDING —
    needs the render path or the live debugger; do not assert water render behaviour.

7b. **`.fx1.pre`–`.fx7.pre` editor-sidecar body (Section 5.5):** Editor-only companions to the runtime
    `.fxN` files; they share the leading `u32` group count but their per-group body and overall size
    differ from the runtime file, and the body was not decoded. The runtime parser must **skip** them
    (it opens only the `.map`-named `.fxN`); their internal layout is opaque editor data.

8. **`.up` / `.exd` `plane_height`:** Resolved as an **independent per-triangle scalar** that
   frequently differs from vertex Y (non-flat geometry is the norm in the full corpus). Its precise
   role (collision range bound vs. precomputed elevation) is still inferred.

9. **`.sod.pre` multiple solids per cell** and **runtime loading:** All samples are single-solid;
   no runtime loader found (likely editor-only).

10. **`light%d.bin` gaps at 0x0900 and 0x1230 (48 bytes each):** May be a 49th wrap-around
    interpolation slot, or alignment padding.

11. **`light%d.bin` trailing 416 bytes (0x1320–0x14BF):** Not accessed by any analysed function.

12. **`light%d.bin` four unread floats per slot (+0x0C, +0x10, +0x1C, +0x2C):** Present but not read
    by the main time-update path.

13. **`point_light%d.bin` record offsets +0x24–+0x30:** Most likely position (XYZ) and range; not
    confirmed.

14. **`point_light%d.bin` three colour groups:** Could be D3D diffuse/specular/ambient channels or
    three independent light sources.

15. **`wind%d.bin` entry fields [0–4]:** Field [5] is the texture id (§8.2); the remaining five
    floats' semantics are unverified (no non-zero corpus rows decoded this pass).

16. **sky/dat `.txt` siblings:** `light{N}.txt` / `point_light{N}.txt` / `wind{N}.txt` coexist with
    the `.bin` files but are editor text exports, not the binary format. The binary reader must skip
    them.

---

## Cross-references

- **Related formats:** `Docs/RE/formats/terrain.md` — base terrain formats (`.ted`, `.sod`, `.map`);
  the `.ted.post` block layout is identical to `.ted` block layout documented there.
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
- **Implementation target:** `Assets.Parsers` (layer 03.Storage.Assets). This file must be cited as
  `// spec: Docs/RE/formats/terrain_layers.md` on every offset reference in the parser.
  Conversion of vertex data to engine types is `Assets.Mapping`'s responsibility, not `Assets.Parsers`.
