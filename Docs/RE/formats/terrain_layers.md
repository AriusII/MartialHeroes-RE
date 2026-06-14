# Format: terrain layer sidecars  (per-cell terrain overlay and lighting files)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> Related base terrain formats are in `Docs/RE/formats/terrain.md`.

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `mixed` — see per-format status rows below |
| `sample_verified` | `true` for `.fx1`–`.fx6`, `.up`, `.exd`, `.sod.pre`, `.ted.post`, `wind%d.bin` header; `false` for `light%d.bin`, `point_light%d.bin`; `.fx7` size-formula PLAUSIBLE (dual-sample); `.fx4` UNVERIFIED (single-sample) |
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

| Format key | sample_verified |
|-----------|-----------------|
| `fx_layer` | `true` for `.fx1`–`.fx3`, `.fx5`, `.fx6`; `.fx7` PLAUSIBLE (dual-sample); `.fx4` UNVERIFIED (single-sample) |

**Path pattern:** `data/map{MAP}/dat/d{MAP}x1{TX:04d}z1{TZ:04d}.fx{N}` where `N` is 1–7.

### 1.2 Vertex formats (on-disk)

Three on-disk vertex formats are used. The applicable format is determined by the layer index,
not by a flag inside the file.

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

**Status:** CONFIRMED — 3 sample files, exact size match.
**Semantic:** Single-triangle terrain overlay. Each mesh group contains one triangle (3 vertices,
3 indices). Vertex format: VF_36.

**File layout:**

```
FX1_File = Header (24 bytes) + VertexData + IndexData
```

**Header (24 bytes):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | `type_tag` | 1 | CONFIRMED (constant across all samples) |
| 0x04 | 4 | u32 | _unknown_1_ | 1 | UNVERIFIED (constant=1) |
| 0x08 | 4 | u32 | _unknown_2_ | 0 | UNVERIFIED (constant=0; possibly flags or padding) |
| 0x0C | 4 | u32 | `render_state` | 15 (0x0F) | UNVERIFIED semantic; value 15 shared with FX2 |
| 0x10 | 4 | u32 | `mesh_count` | varies | CONFIRMED — count of mesh groups |
| 0x14 | 4 | u32 | `index_count` | varies | CONFIRMED — total u16 indices across all groups |

**VertexData:** `mesh_count × 36` bytes (VF_36). One vertex per mesh group in observed samples.

**IndexData:** `index_count × 2` bytes (u16).

**File-size formula:** `24 + mesh_count × 36 + index_count × 2`

Verified: 3 samples each = 138 bytes = 24 + 3 × 36 + 3 × 2.

### 1.6 FX2 Format  (`.fx2`)

**Status:** CONFIRMED — 3 sample files, exact size match.
**Semantic:** Same role as FX1 but with a second UV channel (UV1). Used for lightmap or secondary
texture blending. Vertex format: VF_44.

**File layout:** identical structure to FX1 (24-byte header with the same field layout). The
`render_state` field at `0x0C` is also 15 (0x0F), same as FX1.

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00–0x14 | — | — | Same header fields as FX1 | CONFIRMED |

**VertexData:** `mesh_count × 44` bytes (VF_44). The extra 8 bytes per vertex are `f32 U1, f32 V1`.

**IndexData:** `index_count × 2` bytes (u16).

**File-size formula:** `24 + mesh_count × 44 + index_count × 2`

Verified: 3 samples each = 162 bytes = 24 + 3 × 44 + 3 × 2.

### 1.7 FX3 Format  (`.fx3`)

**Status:** CONFIRMED — 3 sample files, exact size match.
**Semantic:** Quad terrain overlay (4 vertices, 6 indices = 2 triangles). Vertex format: VF_36.
Uses a 48-byte header — 24 bytes longer than FX1/FX2 — with additional constant fields whose
semantics are not confirmed.

**File layout:**

```
FX3_File = Header (48 bytes) + VertexData + IndexData
```

**Header (48 bytes):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | `type_tag` | 1 | CONFIRMED (constant=1) |
| 0x04 | 4 | u32 | _unknown_1_ | 1 | UNVERIFIED (constant=1) |
| 0x08 | 4 | u32 | _unknown_2_ | 0 | UNVERIFIED (constant=0) |
| 0x0C | 4 | u32 | `render_state` | 5 | UNVERIFIED — differs from FX1/FX2 value of 15 |
| 0x10 | 4 | u32 | _unknown_3_ | 0 | UNVERIFIED (constant=0) |
| 0x14 | 4 | f32 | _unknown_4_ | 1.0 | UNVERIFIED (constant=1.0; possibly a scale factor) |
| 0x18 | 4 | u32 | _unknown_5_ | 0 | UNVERIFIED (constant=0) |
| 0x1C | 4 | u32 | _unknown_6_ | 5 | UNVERIFIED (constant=5) |
| 0x20 | 4 | u32 | _unknown_7_ | 5 | UNVERIFIED (constant=5) |
| 0x24 | 4 | u32 | _unknown_8_ | 0 | UNVERIFIED (constant=0) |
| 0x28 | 4 | u32 | `mesh_count` | varies | CONFIRMED — count of mesh groups |
| 0x2C | 4 | u32 | `index_count` | varies | CONFIRMED — total u16 indices |

All bytes from `0x00` to `0x2F` are **constant** across all three samples; only the vertex
coordinates in VertexData differ.

**VertexData:** `mesh_count × 36` bytes (VF_36).

**IndexData:** `index_count × 2` bytes (u16). Observed: `[0, 1, 2, 1, 3, 2]` (standard quad split).

**File-size formula:** `48 + mesh_count × 36 + index_count × 2`

Verified: 3 samples each = 204 bytes = 48 + 4 × 36 + 6 × 2.

### 1.8 FX5 Format  (`.fx5`)

**Status:** CONFIRMED for single-section files; PARTIAL CONFLICT for multi-section layout — see
Known Unknowns below.
**Semantic:** Multi-section terrain overlay mesh with per-section direction metadata. Each section
has a 40-byte metadata header and a sub-chunk header encoding its own vertex and index counts.
Sections may represent different LOD levels or independently-textured geometry groups.
Vertex format: VF_36.

**File layout:**

```
FX5_File = Section[0] + Section[1] + ... + Section[N-1]
```

The total number of sections `N` is not stored in the file; it is derived from accumulated sizes.

**Section layout:**

```
Section = Section_Header (40 bytes) + SubChunk_Header (12 bytes) + VertexData + IndexData
```

**Section_Header (40 bytes):**

| Offset | Size | Type | Field | Observed values | Confidence |
|-------:|-----:|------|-------|-----------------|------------|
| 0x00 | 4 | u32 | `section_type` | 1, 2, 3 (ascending per section) | UNVERIFIED semantic |
| 0x04 | 4 | u32 | _unknown_1_ | 1..3 | UNVERIFIED (possibly section index or LOD level) |
| 0x08 | 4 | u32 | _unknown_2_ | 300, 400, 450 | UNVERIFIED (possibly LOD distance or tile parameter) |
| 0x0C | 4 | f32 | `direction_x` | fractional | CONFIRMED as a float; semantic UNVERIFIED |
| 0x10 | 4 | f32 | `direction_y` | fractional | CONFIRMED as a float; semantic UNVERIFIED |
| 0x14 | 4 | f32 | `direction_z` | fractional | CONFIRMED as a float; semantic UNVERIFIED |
| 0x18 | 4 | u32 | _unknown_3_ | 30, 50 | UNVERIFIED |
| 0x1C | 4 | u32 | _unknown_4_ | 2 | UNVERIFIED (constant=2 in all samples) |
| 0x20 | 4 | u32 | _unknown_5_ | 1 | UNVERIFIED (constant=1) |
| 0x24 | 4 | u32 | _unknown_6_ | 1 | UNVERIFIED (constant=1) |

The direction vector at offsets `0x0C`–`0x17` is an oblique unit vector per section. Its purpose
is unconfirmed; candidates include slope normal for the section geometry, LOD orientation, or
an instancing direction.

**SubChunk_Header (12 bytes — confirmed for section 0; see Known Unknowns for later sections):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | _unknown_flags_ | UNVERIFIED (constant=1 in all observed section-0 sub-chunks) |
| 0x04 | 4 | u32 | `vert_count` | CONFIRMED |
| 0x08 | 4 | u32 | `idx_count` | CONFIRMED |

**VertexData:** `vert_count × 36` bytes (VF_36). Fractional normals indicate slope geometry.

**IndexData:** `idx_count × 2` bytes (u16). Triangle list.

**File-size formula (single-section):** `52 + vert_count × 36 + idx_count × 2`

Verified: one sample = 880 bytes = 52 + 20 × 36 + 54 × 2.

**Multi-section note:** A two-section sample (1420 bytes) has been observed but the exact byte
boundary between the section-0 sub-chunk header and the section-1 section-header is unresolved
(see Known Unknowns, item 1).

### 1.9 FX6 Format  (`.fx6`)

**Status:** CONFIRMED — 3 sample files, all byte-identical, exact size match.
**Semantic:** Collection of 3D prop or object mesh instances. Each sub-chunk is an independent
small box-like mesh. Unlike FX1–FX5, FX6 vertices have no per-vertex colour field (VF_32) and
use normals consistent with 45-degree box faces rather than terrain slopes.

**File layout:**

```
FX6_File = GlobalHeader (32 bytes) + SubChunk[0..N-2] (736 bytes each) + SubChunk[N-1] (708 bytes)
```

All sub-chunks have the same internal structure; the final sub-chunk is 28 bytes shorter because
it carries no footer.

**GlobalHeader (32 bytes):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | `sub_chunk_count` | 40 | CONFIRMED (constant across all 3 files) |
| 0x04 | 4 | u32 | _version_ | 1 | UNVERIFIED (constant=1) |
| 0x08 | 4 | u32 | _unknown_1_ | 0 | UNVERIFIED (constant=0) |
| 0x0C | 4 | u32 | _unknown_2_ | 0 | UNVERIFIED (constant=0) |
| 0x10 | 4 | f32 | _unknown_3_ | 1.0 | UNVERIFIED (possibly global scale; constant=1.0) |
| 0x14 | 4 | u32 | _unknown_4_ | 45 | UNVERIFIED (constant=45; possibly tile count or grid dimension) |
| 0x18 | 4 | u32 | _unknown_5_ | 60 | UNVERIFIED (constant=60) |
| 0x1C | 4 | u32 | _unknown_6_ | 0 | UNVERIFIED (constant=0) |

**Sub-chunk layout:**

```
SubChunk = SubChunk_Header (8 bytes) + VertexData + IndexData + Footer (28 bytes)
FinalSubChunk = SubChunk_Header (8 bytes) + VertexData + IndexData   [no footer]
```

**SubChunk_Header (8 bytes):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | `vert_count` | 20 | CONFIRMED (constant=20 in all sub-chunks) |
| 0x04 | 4 | u32 | `idx_count` | 30 | CONFIRMED (constant=30 in all sub-chunks) |

**VertexData:** `vert_count × 32` bytes (VF_32). Box/prop geometry with 45-degree normals.

**IndexData:** `idx_count × 2` bytes (u16). Five quads expressed as 10 triangles.

**Footer (28 bytes, non-final sub-chunks only):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | _unknown_a_ | 1 | UNVERIFIED (constant=1) |
| 0x04 | 4 | u32 | _unknown_b_ | 0 | UNVERIFIED (constant=0) |
| 0x08 | 4 | u32 | _unknown_c_ | 0 | UNVERIFIED (constant=0) |
| 0x0C | 4 | f32 | _unknown_d_ | 1.0 | UNVERIFIED (possibly per-sub-chunk scale) |
| 0x10 | 4 | u32 | _unknown_e_ | varies | UNVERIFIED (observed: 10, 40, 45, 50, …) |
| 0x14 | 4 | u32 | _unknown_f_ | varies | UNVERIFIED (observed: 30, 60; often 60) |
| 0x18 | 4 | u32 | _unknown_g_ | 0 | UNVERIFIED (constant=0) |

The varying values at footer offsets `0x10` and `0x14` differ per sub-chunk and may encode LOD
distances, texture tile indices, or rendering priority parameters.

**File-size formula:**
`32 + (sub_chunk_count - 1) × 736 + 708`

Verified: 3 files = 29 444 bytes = 32 + 39 × 736 + 708.

**Per-sub-chunk verification:** `8 + 20 × 32 + 30 × 2 + 28 = 8 + 640 + 60 + 28 = 736`; final = `8 + 640 + 60 = 708`.

### 1.10 FX7 Format  (`.fx7`)

**Status:** PLAUSIBLE — 2 sample files (dual-sample), byte-identical in size, size formula exact for both.
**Semantic:** Single-section terrain overlay mesh using the **FX5-style section header** but the
**VF_32** vertex format (no per-vertex colour). Where FX5 carries a per-vertex colour and groups
1–3 sections, FX7 (in both observed samples) is a single section with a high vertex count and a
plain position/normal/UV0 vertex. The two observed files are identical in size, consistent with the
same cell tile reused across two map areas.

**File layout:**

```
FX7_File = Section_Header (40 bytes) + SubChunk_Header (12 bytes) + VertexData + IndexData
```

A single section; the 52-byte header is the same FX5 section + sub-chunk structure (`terrain_layers.md` §1.8).

**Section_Header (40 bytes):**

| Offset | Size | Type | Field | Observed values | Confidence |
|-------:|-----:|------|-------|-----------------|------------|
| 0x00 | 4 | u32 | `section_type` | 1 | DUAL-SAMPLE (constant=1; matches FX5) |
| 0x04 | 4 | u32 | _unknown_1_ | 2 | DUAL-SAMPLE (within FX5's 1–3 range) |
| 0x08 | 4 | f32 | `unk_dist` | large float (thousands) | DUAL-SAMPLE — **a large f32, NOT a small integer LOD distance.** FX5 stores 300/400/450 here as a u32; FX7 stores a large float (candidate world-space bounding coordinate). Do not treat this as the FX5 LOD-distance field. |
| 0x0C | 4 | f32 | _unknown_2_ | fractional | DUAL-SAMPLE — candidate bounding coordinate / dimension |
| 0x10 | 4 | f32 | _unknown_3_ | large float | DUAL-SAMPLE — candidate bounding coordinate |
| 0x14 | 4 | f32 | _unknown_4_ | small signed fractional | DUAL-SAMPLE — candidate direction / normal component |
| 0x18 | 4 | u32/f32 | _unknown_5_ | 0 | DUAL-SAMPLE (zero) |
| 0x1C | 4 | f32 | _unknown_6_ | near-zero fractional | DUAL-SAMPLE |
| 0x20 | 4 | u32 | _unknown_7_ | 1 | DUAL-SAMPLE (constant=1; matches FX5 _unknown_5_) |
| 0x24 | 4 | u32 | _unknown_8_ | 0 | DUAL-SAMPLE (constant=0; matches FX5 _unknown_6_) |

**SubChunk_Header (12 bytes):**

| Offset | Size | Type | Field | Observed values | Confidence |
|-------:|-----:|------|-------|-----------------|------------|
| 0x28 | 4 | u32 | _unknown_flags_ | 0 | DUAL-SAMPLE (FX5 had 1 here; FX7 had 0) |
| 0x2C | 4 | u32 | `vert_count` | large (hundreds–thousand) | DUAL-SAMPLE |
| 0x30 | 4 | u32 | `idx_count` | large; divisible by 3 | DUAL-SAMPLE |

**VertexData:** `vert_count × 32` bytes (**VF_32** — position 12 B + normal 12 B + UV0 8 B; **no
per-vertex colour field**). This is the same VF_32 used by FX6 (`terrain_layers.md` §1.2), not FX5's VF_36.

**IndexData:** `idx_count × 2` bytes (u16). Plain triangle list (`idx_count` divisible by 3).

**File-size formula:** `52 + vert_count × 32 + idx_count × 2`

Verified: both samples satisfy the formula exactly with zero residual bytes (a 52-byte header + a
VF_32 vertex block + a u16 index block account for the whole file in both cases).

### 1.11 FX4 Format  (`.fx4`)

**Status:** UNVERIFIED — 1 sample file only (single-sample). The section-0 size formula is exact,
but the file's two-section structure and the section-1 header boundary are ambiguous at one sample.
Treat the entire FX4 layout as UNVERIFIED.

**Semantic:** Two-section terrain overlay mesh using the **VF_44** vertex format (position + normal +
RGBA colour + UV0 + UV1 — the same vertex format as FX2, `terrain_layers.md` §1.2). The first
section parses cleanly as an FX5-style section (40-byte section header + 12-byte sub-chunk header +
VF_44 vertex block + u16 index block). A second section of geometry follows it. Where FX5 uses VF_36
and FX2 is single-section, FX4 combines FX2's two-UV-channel VF_44 vertex with a multi-section container.

**File layout (single observed sample):**

```
FX4_File = Section[0] + Section[1]
```

**Section 0 header (52 bytes — 40-byte section header + 12-byte sub-chunk header):**

| Offset | Size | Type | Field | Observed value | Confidence |
|-------:|-----:|------|-------|----------------|------------|
| 0x00 | 4 | u32 | `section_type` | 2 | SINGLE-SAMPLE (FX5 stores ascending 1/2/3 per section) |
| 0x04 | 4 | u32 | _unknown_1_ | 3 | SINGLE-SAMPLE |
| 0x08 | 4 | u32 | _unknown_2_ | 1 | SINGLE-SAMPLE (matches FX5) |
| 0x0C | 4 | u32 | `render_state` | 200 | SINGLE-SAMPLE — differs from FX5's 300–450 |
| 0x10 | 4 | f32 | `direction_x` | fractional | SINGLE-SAMPLE — candidate direction vector |
| 0x14 | 4 | f32 | `direction_y` | fractional | SINGLE-SAMPLE |
| 0x18 | 4 | f32 | `direction_z` | near-zero | SINGLE-SAMPLE |
| 0x1C | 4 | u32 | _unknown_3_ | 40 | SINGLE-SAMPLE (FX5 had 30/50) |
| 0x20 | 4 | u32 | _unknown_4_ | 2 | SINGLE-SAMPLE (matches FX5) |
| 0x24 | 4 | u32 | _unknown_5_ | 0 | SINGLE-SAMPLE (matches FX5) |
| 0x28 | 4 | u32 | _unknown_flags_ | 2 | SINGLE-SAMPLE (FX5 had 1) |
| 0x2C | 4 | u32 | `vert_count` | small (tens) | SINGLE-SAMPLE |
| 0x30 | 4 | u32 | `idx_count` | small; divisible by 3 | SINGLE-SAMPLE |

**Section 0 VertexData:** `vert_count × 44` bytes (**VF_44**). **IndexData:** `idx_count × 2` bytes (u16).

**Section 0 size formula (exact):** `52 + vert_count × 44 + idx_count × 2`. The first section
accounts for the leading bytes of the file with zero residual.

**Section 1 (UNVERIFIED boundary):** a second section of geometry follows section 0. Its leading word
is zero (a null `section_type`, unlike section 0's value 2), so the section-header / sub-chunk split
for section 1 cannot be pinned at one sample. Two readings both reconcile the remaining byte count
with the same geometry (a VF_44 vertex block + a u16 index block of the same counts as section 0):

- **Reading A:** 40-byte section header + 8-byte sub-chunk header, then VF_44 vertices + u16 indices.
- **Reading B:** 36-byte section header + 12-byte sub-chunk header, then VF_44 vertices + u16 indices.

Both give a 44-byte vertex stride consistent with section 0. **The exact section-1 header boundary is
UNVERIFIED** — an engineer must not assume Reading A or Reading B. The total file is fully
accounted for by section 0 (formula above) plus a section-1 geometry block of matching vertex/index
counts; only the internal split of section 1's header is ambiguous.

### 1.12 FX layer summary table

| Layer | sample_verified | Header size | Vertex format | UV channels | Per-vertex colour |
|-------|-----------------|------------:|---------------|:-----------:|:-----------------:|
| FX1   | true  | 24 B | VF_36 (36 B) | 1 | yes |
| FX2   | true  | 24 B | VF_44 (44 B) | 2 | yes |
| FX3   | true  | 48 B | VF_36 (36 B) | 1 | yes |
| FX4   | UNVERIFIED (single-sample) | 40 B section + 12 B sub (s0); s1 boundary ambiguous | VF_44 (44 B) | 2 | yes |
| FX5   | true (partial) | 40 B section + 12 B sub | VF_36 (36 B) | 1 | yes |
| FX6   | true  | 32 B global + 8 B/chunk | VF_32 (32 B) | 1 | no |
| FX7   | PLAUSIBLE (dual-sample) | 40 B section + 12 B sub | VF_32 (32 B) | 1 | no |

---

## Section 2: Upper Terrain File  (`.up`)

| Format key | sample_verified |
|-----------|-----------------|
| `up_terrain` | `true` — 3 samples, exact size match, parser corroborated |

**Purpose:** Stores the triangle mesh of **overhanging terrain surfaces** (bridges, raised platforms,
overhangs) for one cell. All triangles are coplanar (share a single Y elevation). The client tests
player position against these triangles for collision at runtime.

**Path pattern:** `data/map{MAP}/dat/d{MAP}x1{TX:04d}z1{TZ:04d}.up`

### 2.1 File layout

No magic number, no version field. The file begins directly with the triangle count.

**File header (4 bytes):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `triangle_count` | CONFIRMED — observed value 12; file size = 4 + `triangle_count` × 40 |

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
| +0x24 | 4 | f32 | `plane_height` | CONFIRMED — equals vertex Y in all sampled records; stored separately for quick range tests |

Record stride: **40 bytes** (0x28).

**File-size formula:** `4 + triangle_count × 40`

Verified: 3 samples = 484 bytes = 4 + 12 × 40.

### 2.2 Geometric notes

In all sampled cells all three vertex Y values within each record equal `plane_height`, indicating
a **flat coplanar** upper terrain surface. Whether `plane_height` can differ from vertex Y on
non-flat overhangs is unverified.

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
| 0x00 | 4 | u32 | `triangle_count` | CONFIRMED — observed value 2; file size = 4 + `triangle_count` × 40 |

**Triangle record (40 bytes, repeated `triangle_count` times):**

Identical to the `.up` triangle record — see Section 2.1. Field names and offsets are the same;
`plane_height` at record offset +0x24 carries the same semantics.

Record stride: **40 bytes** (0x28).

**File-size formula:** `4 + triangle_count × 40`

Verified: 3 samples = 84 bytes = 4 + 2 × 40.

### 3.2 Comparison with `.up`

| Property | `.up` | `.exd` |
|----------|-------|--------|
| Binary layout | Identical | Identical |
| Scene section key | `UP_TERRAIN` | `EXTRA_TERRAIN` |
| Observed triangle count | 12 | 2 |
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

Verified: `8 + 4 × 8 = 40` (two samples); `8 + 3 × 8 = 32` (one sample).

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
| `bin_light_point` | `false` — no samples available; parser analysis only |

**Purpose:** Stores a variable-count array of Direct3D point lights for one map, with a global
intensity scale applied to all lights.

**Path pattern:** `data/sky/dat/point_light{MAP}.bin`

**File size:** `8 + count × 60` bytes. No magic, no version.

### 7.1 File header (8 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `intensity_scale` | CONFIRMED — loaded as global colour multiplier |
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
separate light sources — this cannot be determined from parser evidence alone (no samples).

Up to 5 point lights from the array may be simultaneously active; the lighting manager maintains
5 per-slot state blocks of 184 bytes each.

---

## Section 8: Wind / Foliage-Sway Keyframes  (`wind%d.bin`)

| Format key | sample_verified |
|-----------|-----------------|
| `bin_light_wind` | `true` for header; `false` for entry body (all 3 samples are zero-entry stubs) |

**Purpose:** Stores keyframe entries that drive foliage-sway animations for one map.

**Path pattern:** `data/sky/dat/wind{MAP}.bin`

**File size:** `8 + count × 24` bytes (or exactly 8 bytes when `count = 0`).

**No magic, no version.** Missing file or zero-count file produces no foliage sway.

### 8.1 File header (8 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `count` | CONFIRMED — 3 zero-entry samples verify 8-byte header |
| 0x04 | 4 | u32 | `flag2` | CONFIRMED — non-zero activates foliage-sway curve seeding |

### 8.2 Wind keyframe record (24 bytes, repeated `count` times)

Six consecutive `f32` values. Only field [5] (offset +20) is confirmed as accessed; fields [0–4]
are structurally inferred.

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0x00 | 4 | f32 | _unknown_0_ | UNVERIFIED (possibly time key or direction X) |
| +0x04 | 4 | f32 | _unknown_1_ | UNVERIFIED (possibly direction Y) |
| +0x08 | 4 | f32 | _unknown_2_ | UNVERIFIED (possibly direction Z) |
| +0x0C | 4 | f32 | _unknown_3_ | UNVERIFIED (possibly wind speed) |
| +0x10 | 4 | f32 | _unknown_4_ | UNVERIFIED (possibly frequency or phase) |
| +0x14 | 4 | f32 | `sway_seed` | MEDIUM — confirmed as argument to foliage-sway seeding call |

Record stride: **24 bytes**.

---

## Known Unknowns

The following items are explicitly unresolved. An engineer must not assume a meaning for any of
these without further evidence.

1. **FX5 multi-section sub-chunk header boundary:** In a two-section sample, section 1 appears
   to have a sub-chunk header that is 8 bytes rather than 12 bytes (the first `flags` dword may
   be absorbed into the section header structure for sections beyond section 0). The exact byte
   boundary is unresolved. Mark all FX5 multi-section parsing beyond the section-0 sub-chunk as
   **UNVERIFIED**.

2. **FX1/FX2 header field at 0x0C (`render_state` = 15):** Meaning of the value 15 is not
   confirmed. Could be a vertex-format selector, a render-state enum, or a texture-binding index.

3. **FX3 header extended block (0x10–0x27):** Eight u32/f32 fields, all constant across 3 samples.
   The cluster of value-5 fields (0x0C, 0x1C, 0x20) and the 1.0 float (0x14) are not understood.

4. **FX6 GlobalHeader fields at 0x14 (=45) and 0x18 (=60):** Constant across all 3 files.
   Candidates: grid dimension, tile count, or rendering parameter.

5. **FX6 sub-chunk footer fields at +0x10 and +0x14:** Vary per sub-chunk. Candidates: LOD
   distances, texture tile indices, rendering priority. No pattern established.

6. **FX7 format (§1.10):** PLAUSIBLE from two byte-identical samples. The 52-byte FX5-style
   header + VF_32 vertex block + u16 index block reconciles both files exactly. The nine
   section-header fields (especially `unk_dist` at 0x08, a large f32 rather than the FX5
   u32 LOD distance) are unresolved in semantics. No IDA cross-check yet.
7. **FX4 format (§1.11):** UNVERIFIED from a single sample. Section 0 parses exactly
   (52-byte header + VF_44 vertices + u16 indices); the **section-1 header boundary is
   ambiguous** (Reading A 40+8 vs. Reading B 36+12) — do not assume either. The whole
   two-section interpretation rests on one file. No IDA cross-check yet.

7. **`.up` `plane_height` vs vertex Y for non-flat geometry:** All sampled upper terrain
   triangles are flat (all vertex Y = plane_height). The field may diverge for non-flat overhangs.

8. **`.exd` `triangle_count` > 2:** Only value 2 observed. No upper bound established.

9. **`.exd` `height_bound` field for non-flat triangles:** In all samples the extra float equals
   vertex Y (flat geometry). Whether it stores a height bound, a pre-negated plane constant, or
   a surface type for sloped triangles is unresolvable from current samples.

10. **`.sod.pre` multiple solids per cell:** All samples represent single-solid cells. Whether the
    file stores multiple polygon vertex lists with delimiters for multi-solid cells is unverified.

11. **`.sod.pre` runtime loading:** No game-runtime loader was found. The file may be editor-only.

12. **`.ted.post` texture index block (block 3) reference target:** Whether the 1-based index
    references the global `bgtexture.lst` pool or the per-tile `TEXTURES{}` list in `.map` is
    unresolvable from available samples.

13. **`light%d.bin` gaps at 0x0900 and 0x1230 (48 bytes each):** May be a 49th wrap-around
    interpolation slot, or alignment padding between sections.

14. **`light%d.bin` trailing 416 bytes (0x1320–0x14BF):** Not accessed by any analysed function.
    May encode sky-colour gradients, additional atmospheric parameters, or editor-reserved space.

15. **`light%d.bin` four unread floats per slot (+0x0C, +0x10, +0x1C, +0x2C):** Present in the
    blob but not read by the main time-update path. Possibly light direction angles or editor data.

16. **`point_light%d.bin` record offsets +0x24–+0x30:** Most likely position (XYZ) and range, but
    no samples exist to confirm.

17. **`point_light%d.bin` three colour groups:** Could be D3D diffuse/specular/ambient channels
    or three independent light sources.

18. **`wind%d.bin` entry fields [0–4]:** No non-zero samples; semantics entirely unverified.

---

## Cross-references

- **Related formats:** `Docs/RE/formats/terrain.md` — base terrain formats (`.ted`, `.sod`, `.map`);
  the `.ted.post` block layout is identical to `.ted` block layout documented there.
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
- **Implementation target:** `Assets.Parsers` (layer 03.Storage.Assets). This file must be cited as
  `// spec: Docs/RE/formats/terrain_layers.md` on every offset reference in the parser.
  Conversion of vertex data to engine types is `Assets.Mapping`'s responsibility, not `Assets.Parsers`.
