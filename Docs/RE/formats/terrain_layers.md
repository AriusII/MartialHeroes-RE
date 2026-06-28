# Format: terrain layer sidecars  (per-cell terrain overlay and lighting files)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
> Related base terrain formats are in `Docs/RE/formats/terrain.md`.

---

## Re-verification banner (2026-06-27 — CYCLE 14 re-anchor, build f61f66a9)

| Attribute        | Value |
|------------------|-------|
| `verification`   | CYCLE 14 re-anchor (f61f66a9): 1 fact re-confirmed SAME, 1 corrected. SAME: `.fx7` is a uniform group-array with a 48-byte (0x30) group header, `vertex_count@+0x28`, `index_count@+0x2C`, VF_32 vertices (32 B, no colour), u16 index block, two-vertex-copy memcpy; the 48-byte (not 52-byte) header reconfirmed (§1.10/§1.13). CORRECTED: `.fx6` is a clean uniform group-array — `u32 group_count` followed by that many group records each carrying a single 36-byte (0x24) header with `vertex_count@+0x1C` and `index_count@+0x20`, VF_32 vertices, u16 indices, and the standard two-vertex-copy memcpy. There is NO global-metadata prefix and NO 28-byte trailing subchunk. The prior §1.9 "worked layout" (global metadata + 8-byte per-group header + trailing block) is REFUTED by the actual `Fx6_DecodeGroups` decoder and has been reconciled to the uniform group-array model in §1.9 below. §1.4a/§1.13 were already correct and are unchanged. |
| `ida_reverified` | `2026-06-27` |
| `ida_anchor`     | `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` |
| `evidence`       | `[static-ida]` — `Fx6_DecodeGroups` and `Fx7_DecodeGroups` re-confirmed at relocated addresses under build f61f66a9. Fx6: reads `u32 group_count`, then per group reads 36 bytes (0x24), extracts `vertex_count` at group-relative +0x1C and `index_count` at +0x20, reads `vertex_count × 32` VF_32 bytes and `index_count × 2` u16 bytes, with two operator-new vertex buffers + memcpy — no extra reads before or between groups. Decoder shape identical to `.fx7` except header width (36 B vs 48 B). |
| `conflicts`      | §1.9 corrected (binary-won). The "global metadata" prefix at file offsets 0x04–0x1C, the "8-byte per-group header", and the "28-byte trailing block" documented in the prior §1.9 worked layout are all REFUTED. §1.12 FX6 summary row and §1.13 stride table updated to match. |

---

## Re-verification banner (2026-06-24, ANALYZE re-walk)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `mixed` — see per-format rows. The **FX `.fx1`–`.fx7` group-array on-disk format is CODE-CONFIRMED** from the seven file decoders (per-channel header width, vc/ic offsets, vertex stride, 1-based `texture_index` at group `+0x00`; the `.fx4` "flat tile" framing is retired) — §1.4a/§1.4b, re-walked 2026-06-21. The **`.fx2` and `.fx7` decoders were re-walked byte-for-byte** this pass against three on-disk VFS samples (`.fx2`, `.fx1`, `.fx7`) — all parse to **exactly zero residual** (§1.4a/§1.6/§1.10 confirmed). The **`.fxN` load path / `.map` linkages** (§1.1b), the **exact 1-based texture-register remap** (§1.4b), the **FX3/FX5 = water channel identity** (§1.4c, render path DBG-PENDING), and the **`.fx1.pre`–`.fx7.pre` editor sidecars** (§5.5, editor-only) hold as previously recorded. The `.up`/`.exd` 40-byte triangle record and the light/wind/point_light layouts were **not** re-dumped this pass and hold at their committed tiers. **`light%d.bin` upgraded to sample-verified 2026-06-24 (sky_environment_spec archive pass); `point_light%d.bin` record body upgraded to CONFIRMED; `wind%d.bin` header semantics refined — see `environment_bins.md §9`, §12, §13 for the authoritative sample-verified / loader-resolved details.** |
| `ida_reverified` | `2026-06-24` (ANALYZE re-walk: FX2 decoder byte-for-byte re-confirmed 20 B group-hdr/VF_44; FX7 decoder byte-for-byte re-confirmed 48 B group-hdr/VF_32, vc@+0x28, ic@+0x2C; three on-disk samples decode to exactly zero residual — §1.4a/§1.6/§1.10/§1.13. Wording tightened in §1.4d. FX7 sample_verified upgraded from PLAUSIBLE to CONFIRMED. FX1 §1.5 group-header table field labels corrected to match the authoritative §1.1a table. `light%d.bin` / `point_light%d.bin` / `wind%d.bin` sky_environment_spec archive pass 2026-06-24 — sample-verified corrections and full record body promotions via `environment_bins.md §9/§12/§13`. Prior: `2026-06-22` CYCLE 11 deepen: fx7 header re-confirmed 48 B; fx6 uniform group-array; prior `2026-06-21`: per-channel decoders re-walked; `.fx2` byte-exact sample; prior `2026-06-16`: routing re-confirm.) |
| `ida_anchor`     | `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` |
| `readiness`      | IMPLEMENTATION-READY for the C# rebuild (control-flow-confirmed against IDB SHA 263bd994…); items explicitly tagged debugger-pending / capture-pending / RD-* are NON-blocking runtime residuals to confirm later. |
| `evidence`       | `[static-ida, vfs-sample]` — routing/dispatch from the located runtime `.map` parser + per-channel FX decoders (witness 1) + VFS census file-count corroboration + three on-disk samples (`.fx1`, `.fx2`, `.fx7`) each parsing to exactly zero residual (witness 2, 2026-06-24) |
| `conflicts`      | None. Routing re-confirmed with no drift: (1) **`.up` and `.exd` are ONE shared, distinct format** — `count(u32) + count × 40-byte triangle`, decoded by the same record decoder; `EXTRA_TERRAIN → .exd`, `UP_TERRAIN → .up`. This is structurally **distinct** from the FX group-array model of §1 — any reading that lumps `.exd`/`.up` in with the FX channels is REFUTED. (2) Each FX channel `.fx1`–`.fx7` has its **own** group decoder and its **own** per-channel texture register; the per-channel header width + vertex stride (§1.13) are mandatory. (3) Each FX channel's texture index is **1-based with a `< 1 \|\| > max` guard** (corroborated by per-channel `fx<N> texture index(%d) < 1 \|\| > max(%d)` client error strings) — confirming the shared building/terrain 1-based + clamp texture-index convention without needing the render path. |

**Census this pass (witness 2, full VFS mount, routing-level corroboration):** `.up` 222 files,
`.exd` 1 384 files (both 40-byte-triangle format, zero residual); FX channel counts in the same
ballpark as §1.13's table. File counts are corpus observations, not load-bearing layout facts.

---

## Status block

| Attribute         | Value |
|-------------------|-------|
| `status`          | `mixed` — see per-format status rows below |
| `sample_verified` | `true` for `.fx1`–`.fx7`, `.up`, `.exd`, `.sod.pre`, `.ted.post`, `wind%d.bin` header, `light%d.bin` (5312-byte corpus, 61 files confirmed — `environment_bins.md §9`), `point_light%d.bin` record body (CONFIRMED via sample + `.txt` — `environment_bins.md §13`), and the `.fx{N}.pre` sidecar's existence + leading group-count word; `.fx4` structure CONFIRMED-from-loader (flat group array; internal header fields sample-unverified). `.fx{N}.pre` body is **editor-only and not decoded** (§5.5). |
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
| 6       | `light%d.bin` | Per map       | Directional and ambient light keyframe table (5312 B, 48 keyframes). **See `Docs/RE/formats/environment_bins.md §9` for the authoritative sample-verified field map and apply-path corrections.** |
| 7       | `point_light%d.bin` | Per map | Dynamic point-light pool (8-byte header + `count × 60` B records). **See `Docs/RE/formats/environment_bins.md §13` for the full authoritative spec; §7 below is the structural summary.** |
| 8       | `wind%d.bin` | Per map        | Foliage-sway / wind keyframe array (8 + `count × 24` B). **See `Docs/RE/formats/environment_bins.md §12` for loader-resolved corrections; §8 below is the structural summary.** |

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
| `fx_layer` | `true` for `.fx1`–`.fx3`, `.fx5`, `.fx6`; `.fx7` CONFIRMED (decoder re-walked + zero-residual sample, 2026-06-24); `.fx4` structure CONFIRMED-from-loader (flat group array) |

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
  constructor) but differ in header width (36 B group-hdr for fx6 vs 48 B for fx7) and in the offsets
  of their `vertex_count` / `index_count` fields (§1.13 is authoritative; the C# parser follows §1.13).
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

### 1.4d fx1–fx7 overlay channels — CYCLE 11 deepen

This subsection records additional structural facts confirmed in CYCLE 11. Do not duplicate the
per-channel header/stride table (§1.4a) or the texture-register remap (§1.4b) already covered above.

**Seven overlay channels, two designated as water.** There are exactly seven overlay channels,
fx1 through fx7. Channels fx3 and fx5 are the dedicated **water channels** — each receives its own
distinct grid-build pass at load time (§1.4c). The remaining five channels (fx1, fx2, fx4, fx6, fx7)
take the generic build path.

**Per-vertex UVs stored on disk; no generated or scrolled UV pass at load.** Each channel is an
independent indexed triangle-list mesh (the universal group-array model of §1.1a). Per-vertex UV
coordinates are **stored in the on-disk vertex buffer** for every FX channel (the UV field layout is
part of the VF_32 / VF_36 / VF_44 vertex formats in §1.2). There is **no generated-UV or
scrolled-UV pass at load** for any channel — unlike the terrain base mesh which generates patch UVs
procedurally, the FX overlay channels carry their UVs directly. Any UV animation (water ripple /
scroll) is a **per-frame vertex mutation at draw time**, not a load-time UV bake.

**16×16 spatial cull grid per channel.** After decoding, the per-cell grid-build pass buckets each
decoded group into a **16×16 (256-cell) spatial cull grid** laid over the cell's 1024×1024 footprint
(64-unit patches per cell). Each of the seven channels maintains its **own** cull grid; grid entries
carry per-channel vertical-extent bounds (minimum and maximum Y for each bucket) for view-frustum
culling.

**Two vertex copies per group.** The loader maintains **two vertex copies** for each group: an
immutable **source copy** (the on-disk vertex data as read) and a **working copy** that is a
**load-time duplicate of the source** (populated by `memcpy` at parse time, not re-read from
disk). The working copy is the per-frame-mutable buffer, mutated at draw time (for water-ripple /
scroll on the animated fx3/fx5 channels, and potentially other animated overlay effects); the
source copy is the immutable reset baseline. Implementations must preserve both copies.

**Texture index is 1-based into the per-channel register (clamp-to-1).** The group's `texture_index`
(group `+0x00`) is a **1-based** index into that channel's own texture register (populated from the
`TEXTURES { slot id }` block in the per-cell `.map` descriptor, §1.4b). The build pass clamps any
out-of-range index (below 1 or above the register count) to **1**. This remap is per-channel: two
channels never share a texture register.

**GPU draw bucket, blend mode, and draw order — DEBUGGER-PENDING.** The static load path ends at
the cull-grid insertion. The actual **GPU draw bucket** (which render pass or render queue the
overlay groups land in), the **blend mode** (alpha blend, additive, masked, etc.), and the **draw
order** of overlay channels relative to the terrain base mesh are **not statically resolvable** from
the file-decoder and grid-builder sites alone: no Direct3D device-API strings or render-state-setting
calls are reachable from the load anchors. These must be confirmed via the live debugger (a breakpoint
on the render-path entry) — mark as **DEBUGGER-PENDING** and do not assume a blend mode in the Godot
port until confirmed.

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
| +0x00 | 4 | u32 | `texture_index` | **1-based** index into this channel's per-channel texture register (§1.4b). Commonly observed as 1 in single-texture groups. | CODE-CONFIRMED (§1.1a) |
| +0x04 | 4 | u32 | _inter_header_word_ | Read into group struct; not consumed for parsing. | UNVERIFIED (mostly 0) |
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
| +0x00 | 4 | u32 | `texture_index` | **1-based** index into this channel's per-channel texture register (§1.4b). | CODE-CONFIRMED (§1.1a) |
| +0x04 | 4 | u32 | _inter_header_word_ | Read into group struct; not consumed for parsing. | UNVERIFIED (mostly 0) |
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

> **CORRECTED (2026-06-27, CYCLE 14 re-anchor, binary-won).** The prior "worked layout" for `.fx6`
> (global metadata prefix at 0x04–0x1C + 8-byte per-group header + 28-byte trailing block on
> non-final groups) is **REFUTED** by the actual `Fx6_DecodeGroups` decoder. That decoder is
> branchless: it reads `u32 group_count`, then for each group reads **one 36-byte block** as the
> group header (no global prefix, no inter-group trailing block). §1.4a and §1.13 (which already
> stated 36 B group-hdr / vc@+0x1C / ic@+0x20) are correct and unchanged; only this §1.9 body
> was inconsistent and is now reconciled.

**Status:** CONFIRMED — uniform group-array model (§1.1a); decoder re-walked under build f61f66a9.
**Semantic:** Collection of 3D prop or object mesh groups. Each group is an independent mesh.
FX6 vertices have no per-vertex colour field (VF_32), distinguishing them from the colour-bearing
FX1–FX5 channels.

**File layout:**

```
FX6_File = group_count (u32) + group_count × [ group header (36 B) + VertexData + IndexData ]
```

The decoder is branchless: the leading `u32` is the group count and the same 36-byte group-header
read runs for every group. There is no file-level metadata block after `group_count` and no
trailing block appended to any group.

**File-level header (4 bytes):**

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `group_count` | CONFIRMED (decoder loops this many times) |

**Per-group header (36 bytes / 0x24; offsets group-relative):**

The 36-byte block is read as a single unit. The decoder extracts only `vertex_count` and
`index_count`; all other bytes in the block are read into the group struct but not consumed for
parsing. Semantics of the remaining words are UNVERIFIED.

| Group-rel offset | Size | Type | Field | Confidence |
|-----------------:|-----:|------|-------|------------|
| +0x00 | 4 | u32 | `texture_index` | **1-based** index into this channel's per-channel texture register (§1.4b). CODE-CONFIRMED (§1.1a) |
| +0x04 .. +0x18 | 20 | — | _inter-header words_ | Read into group struct; not consumed for layout. Semantics UNVERIFIED. |
| +0x1C | 4 | u32 | `vertex_count` | Sizes the VF_32 vertex block (`vertex_count × 32`). CONFIRMED |
| +0x20 | 4 | u32 | `index_count` | Sizes the u16 index block (`index_count × 2`). CONFIRMED |

**VertexData (per group):** `vertex_count × 32` bytes (VF_32 — position 12 B + normal 12 B + UV0 8 B; no per-vertex colour). CONFIRMED.

**IndexData (per group):** `index_count × 2` bytes (u16). CONFIRMED.

**Two vertex copies per group.** The loader allocates two vertex buffers and copies the source into the working buffer at load time (same two-copy pattern as all other FX channels — §1.4d).

**File-size formula:** `4 + Σ over groups (36 + vertex_count × 32 + index_count × 2)`.

**Group AABB.** After reading vertex and index blocks the loader calls the per-group AABB finalizer (also called by `.fx7` — the same shared finalize routine) to compute the spatial bounds of each group. This is in-memory only, not on disk.

### 1.10 FX7 Format  (`.fx7`)

**Status:** CONFIRMED — group-array model (§1.1a); decoder re-walked byte-for-byte 2026-06-24; on-disk sample decodes to exactly zero residual.
**Semantic:** Terrain overlay mesh using the **48-byte group header** (shared leading field layout
with the FX5 group header, §1.8) but the **VF_32** vertex format (no per-vertex colour). Observed
files are a single group with a large vertex and index count.

**File layout:**

```
FX7_File = group_count (u32) + group_count × [ group header (48 B) + VertexData + IndexData ]
```

The group header is a 48-byte block, placing `vertex_count` at group-relative +0x28 and
`index_count` at +0x2C. The vertex stride differs from FX5 (VF_32 instead of VF_36).
**The 48-byte header width is §1.13 authoritative (re-confirmed CYCLE 11 and 2026-06-24); the C# parser follows §1.13.**

**Group header (per group — 48 B; offsets group-relative; re-confirmed CYCLE 11):**

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
| +0x28 | 4 | u32 | `vertex_count` | large | CONFIRMED (zero-residual sample, 2026-06-24) |
| +0x2C | 4 | u32 | `index_count` | large; divisible by 3 | CONFIRMED (zero-residual sample, 2026-06-24) |

**VertexData (per group):** `vertex_count × 32` bytes (**VF_32** — position 12 B + normal 12 B +
UV0 8 B; **no per-vertex colour field**). Same VF_32 used by FX6 (§1.2), not FX5's VF_36.

**IndexData (per group):** `index_count × 2` bytes (u16). Plain triangle list (`index_count`
divisible by 3).

**File-size formula:** `4 + 48 + vertex_count × 32 + index_count × 2` (single group). All observed
samples satisfy it exactly with zero residual bytes (re-confirmed 2026-06-24). The 48-byte
group-header width is §1.13 authoritative (vc @ group-relative +0x28, ic @ +0x2C); the C# parser
follows §1.13.

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
| FX6   | CONFIRMED (decoder re-walked, build f61f66a9) | 36 B/group | VF_32 (32 B) | 1 | no |
| FX7   | CONFIRMED (decoder re-walked + zero-residual sample, 2026-06-24) | 48 B/group | VF_32 (32 B) | 1 | no |

All seven share the universal group-array model (§1.1a): a `group_count` word, then that many group
records; the only structural difference is the vertex stride.

### 1.13 Per-channel header width and stride are MANDATORY (reconfirmed 2026-06-16 two-witness; fx7 header width re-confirmed CYCLE 11 → 48 B, vc@+0x28, ic@+0x2C)

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
| fx6  | 36 B group hdr (`vertex_count` @ +0x1C, `index_count` @ +0x20; no global metadata prefix) | VF_32 (32 B) | 6 |
| fx7  | 48 B group hdr (`vertex_count` @ +0x28, `index_count` @ +0x2C) | VF_32 (32 B) | 2 |

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
| `bin_light_dir` | `true` — 61 files, all exactly 5312 bytes (sky_environment_spec archive pass, 2026-06-24). **Authoritative sample-verified spec in `Docs/RE/formats/environment_bins.md §9`.** The layout below is the structural summary; `environment_bins.md §9` carries the confirmed field map, apply-path facts, and loader corrections. |

> **⚠️ Parser action required.** The field names in §6.2 and §6.3 below use the earlier parser-analysis
> names (`sun_colour`, `moon_colour`, `sky_colour`). The sample-verified view from
> `environment_bins.md §9.2` recasts each 48-byte slot as **three `float4` colour groups** (`color_A`
> at +0x00 = primary diffuse/ambient, `color_B` at +0x10 = secondary specular, `color_C` at +0x20 =
> present-but-**unread**). The sun/moon names below remain a best-effort parser interpretation and are
> not contradicted by sample bytes, but `color_A`/`color_B`/`color_C` is the confirmed structural view.
> **`color_C` (+0x20) has NO read-site and must NOT be fed to the lighting math.** See
> `environment_bins.md §9.2` (LOADER-RESOLVED).
>
> **Loader is a verbatim slurp.** The `light%d.bin` loader performs a single 5312-byte opaque slurp
> into a memory block and stops — it does not parse fields at load time. Fields below describe how
> the downstream CONSUMER interprets the blob at runtime, not what the loader reads.
> (Two witnesses: the loader's single bounded read of 5312 bytes + the constant 5312-byte size across
> 61 sampled files.) See `environment_bins.md §9.0`.
>
> **Synth fallback on absent file:** the light loader synthesises a 48-keyframe colour ramp (scale
> 80.0; per-keyframe `intensity = kf_idx × 0.04`, `colour = 1.0 − intensity`) plus the static
> fallback direction `(−7, 7, 20)` and returns success — it never hard-fails on a missing file.
> See `environment_bins.md §9.5`.

**Purpose:** Stores a 48-step day/night cycle of directional and ambient sky-light colours, plus
per-step fog-distance scalars, for one map. The client interpolates between adjacent time steps to
produce continuously changing lighting.

**Path pattern:** `data/sky/dat/light{MAP}.bin`

**File size:** exactly **5312 bytes** (0x14C0). Fixed-size, no count field.

**No magic number, no version field.** If the file is absent, the client synthesises a fallback
ramp and returns success (see note above).

> **Sibling note:** `data/sky/dat/` also holds `light{MAP}.txt` text siblings. They are editor-source
> / text exports and are **not** parseable as this binary format. The authoring `.txt` and the `.bin`
> colour tables diverge for some areas (notably area 0); the `.bin` is authoritative.
> See `environment_bins.md §11.7`.

### 6.1 Blob layout

The 5312-byte file is divided into six identified regions (revised from the earlier three-section
reading — see `environment_bins.md §9.1` for the sample-verified authority):

| Byte range | Size | Region | Description | Confidence |
|:----------:|-----:|--------|-------------|------------|
| 0x0000–0x08FF | 2304 | A — Directional light | 48 keyframes × 48 bytes | CONFIRMED |
| 0x0900–0x092F | 48   | Gap A | All zeros in sampled data; likely wrap-around interpolation slot or alignment pad | SAMPLE-VERIFIED (all zeros) |
| 0x0930–0x122F | 2304 | B — Ambient light | 48 keyframes × 48 bytes | CONFIRMED |
| 0x1230–0x125F | 48   | Gap B | All zeros; same interpretation as Gap A | SAMPLE-VERIFIED (all zeros) |
| 0x1260–0x131F | 192  | C — Fog-distance scalar | 48 keyframes × 1 f32; world-unit fog scalar `s` driving LINEAR fog range `= s × 3.0`. **NOT a normalised density and NOT fog colour** — fog colour is `fog%d.bin`'s domain. | CONFIRMED |
| 0x1320–0x13DF | 192  | D — Secondary fog scalar | 48 keyframes × 1 f32; values near 0.0 in all sampled areas (haze intensity) | SAMPLE-VERIFIED |
| 0x13E0–0x14A7 | 200  | E — Reserved f32 array | All zeros in all sampled data | SAMPLE-VERIFIED (all zeros) |
| 0x14A8–0x14AF | 8    | Padding | All zeros | SAMPLE-VERIFIED (all zeros) |
| 0x14B0–0x14BF | 16   | Fallback light | f32[4]: scale (1.0), dir_X (−7.0), dir_Y (7.0), dir_Z (20.0) | CONFIRMED |

Total: 2304 + 48 + 2304 + 48 + 192 + 192 + 200 + 8 + 16 = 5312 bytes.

> **Section C correction.** The earlier §6.4 description of section C as "fog_density" with 1.0 as a
> "no-override sentinel" is **REVISED**. Section C is the per-keyframe **world-unit fog scalar `s`**
> (sample range approximately 8–43 world units) that drives the LINEAR fog range (`range = s × 3.0`,
> `near-scale = 1.0 / s`, fog enabled when `s > 0`) — see `environment_bins.md §9.3`. The static
> `fog%d.bin` `start_dist` / `end_dist` are overwritten by this per-frame derivation each tick.

### 6.2 Section A — Directional light keyframe slot (48 bytes)

Slot `i` starts at byte offset `48 × i` within section A (absolute: `48 × i`).

**Structural view (sample-verified, `environment_bins.md §9.2`):** each 48-byte slot is three
`f32[4]` colour groups. Only the first two groups are consumed at runtime; the third is unread.

| Slot offset | Size | Type | Structural field | Parser interpretation | Confidence |
|:-----------:|-----:|------|------------------|-----------------------|------------|
| +0x00 | 16 | f32[4] | `color_A` (RGBA) | `sun_colour[0..2]` at +0x00..+0x08; alpha at +0x0C | CONFIRMED (color_A consumed as primary diffuse) |
| +0x10 | 16 | f32[4] | `color_B` (RGBA) | `moon_colour[1]` at +0x14, `moon_colour[2]` at +0x18 in the parser reading; remainder unverified | CODE-CONFIRMED (color_B consumed as secondary specular) |
| +0x20 | 16 | f32[4] | `color_C` (RGBA) | `sky_colour[0..2]` in the earlier parser reading — **but `color_C` has NO read-site and is unconsumed at runtime**. All zeros in all sampled data. Do NOT feed to the lighting math. | LOADER-RESOLVED (no read-site; unread) |

**Phase-offset note (parser-analysis, CODE-CONFIRMED):** `moon_colour[0]` is read from **slot
`(i + 30) mod 48`** of section A (not the current slot), producing a half-cycle phase offset.
`moon_colour[1]` and `moon_colour[2]` are read from the current slot at offsets +0x14 and +0x18.
This asymmetry is confirmed by the client time-update function.

**All fields are float32 in the [0, 1] domain** — applied directly to the lighting math (no /255).
See `environment_bins.md §10.1` for the colour-domain distinction between these float light colours
and the byte-encoded fog/sky-material colour tables.

**Cycle:** 48 steps spanning one full day/night cycle; interpolation period between adjacent slots
is 1800 ms.

### 6.3 Section B — Ambient light keyframe slot (48 bytes)

Structurally identical to section A (three `f32[4]` colour groups per slot). Slot `i` starts at
absolute offset `0x0930 + 48 × i`. `color_A` at +0x00 is the ambient primary (CONSUMED but gated
at runtime by the global ambient multiplier `K_ambient`, which is CONFIRMED 0.0 with no writer in
the shipping binary — so the per-keyframe ambient contributes nothing to the device). `color_C`
at +0x20 is present-but-unread (LOADER-RESOLVED, all zeros in samples). See `environment_bins.md
§9.2` and §10.4 for the confirmed `K_ambient = 0.0` and its consequence for the ambient floor.

### 6.4 Section C — Fog-distance scalar (4 bytes per slot)

Slot `i` at absolute offset `0x1260 + 4 × i`. One `f32` value per time step.

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x1260 + 4i | 4 | f32 | `fog_scalar_s` | CONFIRMED — a world-unit fog scalar `s`; when `s > 0` the runtime sets LINEAR fog range `= s × 3.0`, near-scale `= 1.0 / s`. Sample range approximately 8–43 world units. **The earlier "fog_density / 1.0 sentinel" reading is revised.** See `environment_bins.md §9.3`. |

Section D (secondary fog scalar, 0x1320–0x13DF, 48 × f32) and sections E + padding (0x13E0–0x14AF,
all zeros in samples) and the fallback light vector (0x14B0–0x14BF: f32[4] = scale 1.0, dir_X −7.0,
dir_Y 7.0, dir_Z 20.0) are documented in `environment_bins.md §9.1` and §9.4 — consult those
sections for the confirmed field details. The fallback direction `(−7, 7, 20)` is the **only** sun
direction the client uses; the day/night cycle does not rotate the sun (§10.6 there).

---

## Section 7: Point Light Array  (`point_light%d.bin`)

| Format key | sample_verified |
|-----------|-----------------|
| `bin_light_point` | CONFIRMED — header (8 bytes), record layout (60 bytes), stride, count, and all record fields. **Authoritative full spec in `Docs/RE/formats/environment_bins.md §13`.** The field table below is the confirmed structural summary; consult `environment_bins.md §13` for the 5-nearest runtime selection model, load-order, known unknowns, and C# contract note. |

> **⚠️ Corrections from the earlier revision.**
> - Header field `intensity_scale` (0x00) is **renamed** to `proximity_radius`. It is confirmed as the
>   proximity-selection threshold (the 5-nearest XZ selection compares against it), not a colour
>   multiplier — see `environment_bins.md §13.1`. Whether it also acts as a brightness multiplier is
>   DEBUGGER-PENDING.
> - Record fields +0x24..+0x38 are now **CONFIRMED** (no longer UNVERIFIED) — see §7.2 below.
> - Record field +0x34 (`enabled_flag`) is **relabelled** to `unknown_34`; it has no confirmed read-site
>   in any static path (DEBUGGER-PENDING).
> - Record field +0x38 is `type_flag`; value `== 1` gates the per-tick flicker pass.
> - The colour group labels `colour_group_1/2/3` are refined: group 1 (+0x00) is `color_diffuse`,
>   the confirmed primary diffuse. Groups 2 and 3 (+0x0C, +0x18) are confirmed as colour triplets
>   but their D3D channel roles (specular, ambient) are DEBUGGER-PENDING.

**Purpose:** Stores a variable-count pool of Direct3D point lights for one map. The runtime selects
the **5 player-nearest** lights from the pool each tick using a log-space XZ proximity test against
the header `proximity_radius`.

**Path pattern:** `data/sky/dat/point_light{MAP}.bin`

**File size:** `8 + count × 60` bytes. No magic, no version. Absent file → loader returns 0 and the
world runs without dynamic point lights (fully optional).

> **Sibling note:** `point_light{MAP}.txt` text siblings coexist in `data/sky/dat/`; they are editor
> text exports. Cross-referencing the `.txt` against `.bin` sample bytes CONFIRMED the full record
> layout (see `environment_bins.md §13`). Open only the `.bin` files at runtime.

### 7.1 File header (8 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | f32 | `proximity_radius` | CONFIRMED as f32 — functionally proven as the proximity-selection threshold in the 5-nearest XZ selection. Whether it also scales brightness is DEBUGGER-PENDING. (Was previously named `intensity_scale` — that label is REVISED.) |
| 0x04 | 4 | i32 | `record_count` | CONFIRMED — number of point-light records |

### 7.2 Point-light record (60 bytes, repeated `record_count` times)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0x00 | 12 | f32[3] | `color_diffuse` RGB | CONFIRMED primary diffuse — confirmed via sample + `.txt` cross-reference (AMBIENT RGB in the `.txt` maps here). Scaled at runtime by a master-dim scalar on the light manager. |
| +0x0C | 12 | f32[3] | `color_b` RGB | CONFIRMED as a colour triplet (DIFFUSE RGB in the `.txt`). D3D channel role (specular, ambient) is DEBUGGER-PENDING. |
| +0x18 | 12 | f32[3] | `color_c` RGB | CONFIRMED as a colour triplet (SPECULAR RGB in the `.txt`). D3D channel role is DEBUGGER-PENDING. |
| +0x24 | 4 | f32 | `position_x` | CONFIRMED — world-space X. Used in the per-tick XZ proximity test vs player position. |
| +0x28 | 4 | f32 | `position_y` | CONFIRMED — world-space Y. |
| +0x2C | 4 | f32 | `position_z` | CONFIRMED — world-space Z. Used in the per-tick XZ proximity test vs player position. |
| +0x30 | 4 | f32 | `range` | CONFIRMED — D3D9 point-light attenuation radius. Feeds `Range`, `Attenuation1`, and `Attenuation2` in the D3D9 light-apply routine. NOT a brightness scalar. |
| +0x34 | 4 | ? | `unknown_34` | No read-site confirmed in any static path. Meaning is DEBUGGER-PENDING. (Was previously called `enabled_flag` — that reading is REVISED; `0 = active / non-zero = skip` was a parser guess, not a confirmed consumer.) |
| +0x38 | 4 | i32 | `type_flag` | CONFIRMED — value `== 1` gates the per-tick flicker pass (`FlickerAnimRange`) that animates the light's range. Other values are DEBUGGER-PENDING. (Was previously called `_unknown_4_`.) |

Record stride: **60 bytes**.

Up to **5** point lights from the pool are simultaneously active; the runtime selects the
5 player-nearest by log-space XZ proximity and binds them into 5 D3D9 light slots.

**Colour confirmation source.** The `.txt` sibling schema:
```
AMBIENT rgb | DIFFUSE rgb | SPECULAR rgb | X | Y | Z | Range | Always | Swing
```
maps field +0x00 to AMBIENT (= `color_diffuse` here; NOTE the `.txt` labels invert the D3D sense
— the confirmed diffuse is what the `.txt` calls "AMBIENT"), field +0x0C to DIFFUSE (= `color_b`),
and field +0x18 to SPECULAR (= `color_c`). The `Always` field (+0x34) and `Swing` field (+0x38)
resolve: `Always` = `unknown_34` (no confirmed reader in static analysis); `Swing` = `type_flag`
(gates flicker). See `environment_bins.md §13` for the full loader-confirmed spec.

---

## Section 8: Wind / Foliage-Sway Keyframes  (`wind%d.bin`)

| Format key | sample_verified |
|-----------|-----------------|
| `bin_light_wind` | `true` — 57-file corpus, sizes all equal `8 + record_count × 24`. **Authoritative loader-resolved / sample-verified details in `Docs/RE/formats/environment_bins.md §12`.** The layout below is confirmed; consult §12 for the `source_flag` semantics, `tex_id` join key, proposed record field roles, and the FAIL-ON-MISSING exception. |

> **⚠️ Corrections from the earlier revision.**
> - Header field `flag2` (0x04) is **renamed** to `source_flag`. It **gates per-record texture binding
>   only**, not foliage-sway on/off — see `environment_bins.md §12.1`. It is 0 in all 57 sampled files.
> - **`wind%d.bin` is FAIL-ON-MISSING** (the exception to the family-wide sibling-tolerance rule). An
>   absent file fails the area load; the empty 8-byte form is the minimum required. See §12.4 there.
>   The earlier "Missing file produces no foliage sway" gloss is REVISED — a missing file fails the
>   area load; a *zero-count* file is fine and produces no sway.

**Purpose:** Stores keyframe entries that drive foliage-sway animations for one map.

**Path pattern:** `data/sky/dat/wind{MAP}.bin`

**File size:** `8 + record_count × 24` bytes (or exactly 8 bytes when `record_count = 0`).

**No magic, no version.** Every shipping area ships at least the empty 8-byte form.

> **Sibling note:** `wind{MAP}.txt` text siblings coexist in `data/sky/dat/`; they are editor text
> exports, not this binary format. Open only the `.bin` files.

### 8.1 File header (8 bytes)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| 0x00 | 4 | u32 | `record_count` | CONFIRMED — zero-entry samples verify 8-byte header; the empty 8-byte form is the valid minimum. |
| 0x04 | 4 | u32 | `source_flag` | CONFIRMED — **gates per-record texture-binding loop only** (not wind on/off). Non-zero → loader walks records and registers each `tex_id` (+0x14) as a `data/sky/texture/wind<id>.dds` texture. Always 0 in all 57 corpus files; the texture-binding path is effectively dead data in shipping. (Was previously labelled `flag2` with an incorrect "foliage-sway curve seeding" gloss.) |

### 8.2 Wind keyframe record (24 bytes, repeated `record_count` times)

| Offset | Size | Type | Field | Confidence |
|-------:|-----:|------|-------|------------|
| +0x00 | 4 | f32 | `_pad0` | SAMPLE-VERIFIED (always 0.0 in populated samples) |
| +0x04 | 4 | f32 | `speed` (proposed) | PROPOSED — observed `3.0, 3.0, 4.0, 6.0`; correlates with per-emitter speed defaults in the wind-object constructor. |
| +0x08 | 4 | f32 | `_pad2` | SAMPLE-VERIFIED (always 0.0 in populated samples) |
| +0x0C | 4 | f32 | `coord` (proposed) | PROPOSED — observed `0.0, 1024.0, 512.0, 1536.0` (multiples of 512; 1024 = MH cell unit); candidate world coordinate / lane offset. |
| +0x10 | 4 | f32 | `scale` (proposed) | PROPOSED — observed `1.0, 1.0, 1.5, 1.0`; correlates with scale default in constructor. |
| +0x14 | 4 | u32 | `tex_id` | CONFIRMED — texture id integer (validity check `>= 1`); joins to `data/sky/texture/wind<tex_id>.dds`. Always 0 in all corpus samples (because `source_flag = 0` everywhere). |

Record stride: **24 bytes**. The loader slurps the full `record_count × 24`-byte block verbatim;
consumer-side field roles (+0x04/+0x0C/+0x10 = speed/coord/scale) are PROPOSED from observed values
and constructor defaults, not yet confirmed from the per-frame foliage-sway draw consumer.

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

4. **FX6 per-group header inter-header words (+0x04–+0x18, 20 bytes):** The 36-byte per-group
   header contains `texture_index` at +0x00, `vertex_count` at +0x1C, and `index_count` at +0x20;
   the 20 bytes between them are read into the group struct but not consumed for layout. Observed
   sample constants (45, 60, and the dominant-1 word) now fall inside this inter-header region.
   Their semantics (grid dimension, LOD distance, rendering parameter) remain UNVERIFIED. The prior
   framing as "global metadata prefix" and "per-group trailing block" is REFUTED — these words live
   inside each group's 36-byte header, not in separate file-level or inter-group blocks.

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

10. **`light%d.bin` gaps at 0x0900 and 0x1230 (48 bytes each) — PARTIALLY RESOLVED:** Both are
    all-zero in all sampled data. Likely wrap-around interpolation slots or alignment padding.
    See `environment_bins.md` Known Unknown #3.

11. **`light%d.bin` trailing region — REVISED:** The 416-byte trailing region of the earlier reading
    is now recategorised as four named regions: section D (secondary fog scalar, 48 × f32,
    0x1320–0x13DF), section E (reserved f32 array, all-zero, 0x13E0–0x14A7), padding (8 B), and
    the fallback light vector `(scale 1.0, dir_X −7.0, dir_Y 7.0, dir_Z 20.0)` at 0x14B0–0x14BF.
    See §6.1 and `environment_bins.md §9.1`.

12. **`light%d.bin` colour group structure — REVISED:** The four "unread floats" at +0x0C, +0x10,
    +0x1C, +0x2C are part of the `color_B` group (+0x10..+0x1C) and the `color_C` group
    (+0x20..+0x2C). `color_C` is present-but-unread (LOADER-RESOLVED); `color_B` is consumed as
    the specular colour. See §6.2 and `environment_bins.md §9.2`.

13. **`point_light%d.bin` record offsets +0x24–+0x30 — CONFIRMED:** XYZ world position at
    +0x24/+0x28/+0x2C and D3D9 attenuation range at +0x30. See §7.2 and `environment_bins.md §13`.

14. **`point_light%d.bin` three colour groups — PARTIALLY RESOLVED:** Group 1 (+0x00) = primary
    diffuse CONFIRMED. Groups 2 and 3 (+0x0C, +0x18) are confirmed as colour triplets; their exact
    D3D9 channel roles (specular, ambient) are DEBUGGER-PENDING. See `environment_bins.md §13.6`.

15. **`wind%d.bin` entry fields [0–4] — REVISED:** Field [5] (+0x14) is the `tex_id` (CONFIRMED);
    fields +0x00 and +0x08 are SAMPLE-VERIFIED as pad (always 0.0); +0x04 is PROPOSED `speed`,
    +0x0C is PROPOSED world coordinate / lane offset, +0x10 is PROPOSED `scale`. See §8.2 and
    `environment_bins.md §12.3`.

16. **sky/dat `.txt` siblings:** `light{N}.txt` / `point_light{N}.txt` / `wind{N}.txt` coexist with
    the `.bin` files but are editor text exports, not the binary format. The binary reader must open
    only the `.bin` files. Note: the `.txt` siblings are authoritative field-order oracles used to
    cross-confirm the `.bin` field tables (see §7.2 and `environment_bins.md §12/§13`).

---

## Cross-references

- **Related formats:**
  - `Docs/RE/formats/terrain.md` — base terrain formats (`.ted`, `.sod`, `.map`); the `.ted.post`
    block layout is identical to `.ted` block layout documented there.
  - `Docs/RE/formats/environment_bins.md` — **authoritative sample-verified / loader-resolved specs**
    for the full `data/sky/dat/` environment binary family. This document's §§9, 12, and 13 are the
    authoritative sources for `light%d.bin`, `wind%d.bin`, and `point_light%d.bin` respectively,
    superseding the parser-only entries in §§6–8 of this document where they conflict. Also covers
    `map_option%d.bin`, `fog%d.bin`, `material%d.bin`, `stardome%d.bin`, `clouddome%d.bin`,
    `cloud_cycle%d.bin`, and `weather%d.bin`.
  - `Docs/RE/specs/environment.md` — runtime lighting/fog math, ambient gate, brightness slider.
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
- **Implementation target:** `Assets.Parsers` (layer 03.Storage.Assets). This file must be cited as
  `// spec: Docs/RE/formats/terrain_layers.md` on every offset reference in the FX/UP/EXD/SOD/TED
  parser. For the sky/dat binaries (§§6–8) cite `// spec: Docs/RE/formats/environment_bins.md §9`,
  `§12`, or `§13` as appropriate — those sections are the authoritative specs.
  Conversion of vertex data to engine types is `Assets.Mapping`'s responsibility, not `Assets.Parsers`.

---

## 15. `.fx7` Terrain Effect Layer — Cycle 15

> `ida_reverified: 2026-06-27` (CYCLE 15 — `.fx7` format mapped from loader analysis)

### 15.1 Overview

`.fx7` is the **terrain-cell effect layer 7** (terrain slot 8), paralleling `.fx6` (slot 7).
Both formats are structurally identical and differ only in their terrain slot assignment.
The format stores **groups of textured effect meshes** spatially indexed into a 16×16 cell
tile grid at 64 world-unit resolution.

Configuration string literals confirm the format name and purpose:
- `"fx7"` — format identifier string
- `"fx7 terrain settings"` — settings-block label string
- `"fx7 texture index"` — texture-index error-string prefix

### 15.2 File Layout

```
File:
  [u32]   groupCount
  [Group] × groupCount   (see §15.3 for per-group layout)
```

### 15.3 Group Header (48 bytes, read directly from disk)

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 12 | f32[3] | `origin` | Group world-space origin XYZ |
| +0x0C | 12 | f32[3] | `extent` | Group bounding extent XYZ |
| +0x18 | 4 | u32 | `group_type` | Group category / material category |
| +0x1C | 4 | u32 | `group_flags` | Render flags |
| +0x20 | 4 | u32 | `texture_id` | Source texture slot index |
| +0x24 | 4 | u32 | `_pad` | Reserved / alignment |
| +0x28 | 4 | u32 | `vertex_count` | Number of VF_32 vertices |
| +0x2C | 4 | u32 | `index_count` | Number of u16 indices |

> **Note:** The first 40 bytes (`+0x00..+0x27`) are read as a block but only `vertex_count`
> and `index_count` (at `+0x28`/`+0x2C`) are explicitly dereferenced by `Fx7_DecodeGroups`.
> The origin/extent/type/flags layout above is inferred from the 48-byte total and the
> known AABB re-computation step that follows.

After the header, vertex and index data follow immediately:
```
  [VF_32] × vertex_count    (32 bytes each — see §15.5)
  [u16]   × index_count     (triangle indices)
```

### 15.4 In-Memory Group Record (112 bytes = 0x70)

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| +0x00 | 48 | — | `file_header` | Direct copy of 48-byte disk header |
| +0x30 | 4 | ptr | `vtx_buf_a` | Primary vertex heap buffer |
| +0x34 | 4 | ptr | `idx_buf` | u16 index heap buffer |
| +0x38 | 4 | ptr | `vtx_buf_b` | Secondary vertex buffer (double-buffered) |
| +0x3C | 12 | f32[3] | `aabb_min` | Bounding box minimum XYZ (set by `Geometry_RecomputeAABB_AndBudget`) |
| +0x48 | 12 | f32[3] | `aabb_max` | Bounding box maximum XYZ (set by `Geometry_RecomputeAABB_AndBudget`) |
| +0x58 | 4 | u32 | `vtx_byte_size` | = 32 × vertex_count (cached) |
| +0x68 | 4 | u32 | `lod_budget` | Budget tier in units (set by `Geometry_RecomputeAABB_AndBudget`) |

**Double-buffering:** Both `vtx_buf_a` and `vtx_buf_b` are allocated with the same
size (`32 × vertex_count + 4`) and `vtx_buf_b` is initialized as a `memcpy` of
`vtx_buf_a`. This supports GPU double-buffer upload.

### 15.5 Vertex Format — VF_32 (identical to `.bud` and `.fx6`)

| Offset | Type | Field |
|-------:|------|-------|
| +0x00 | f32 | `pos_x` |
| +0x04 | f32 | `pos_y` |
| +0x08 | f32 | `pos_z` |
| +0x0C | f32 | `norm_x` |
| +0x10 | f32 | `norm_y` |
| +0x14 | f32 | `norm_z` |
| +0x18 | f32 | `tex_u` |
| +0x1C | f32 | `tex_v` |

Stride: **32 bytes** (`D3DFVF_XYZ | D3DFVF_NORMAL | D3DFVF_TEX1`).

### 15.6 Texture Registry (per terrain slot)

Up to **32 texture IDs** can be registered per slot via `Fx7Section_AddTextureId`.

Texture registry fields within the in-memory terrain-slot object:

| Slot-relative offset | Size  | Field       | Notes                               |
|---------------------:|------:|-------------|-------------------------------------|
| +0x404 (1028)        | 128 B | `tex_ids`   | `u32[32]` — registered texture IDs |
| +0x424 (1060)        | 4 B   | `tex_count` | `u32` — current count (max 32)     |

Texture IDs from the file are validated at cell-build time: `1 ≤ texIdx ≤ tex_count`.
Out-of-range IDs are clamped to `1` with an `ErrorLog_WriteFormatted` warning.

### 15.7 Spatial Grid (16×16 × 64 world-unit tiles)

`Map_BuildCellFxLayerFx7` bins each group into the 16×16 cell grid:
- Grid tile size: **64 world units**
- Grid dimensions: **16 × 16 = 256 tiles per cell**
- AABB overlap test: group's min/max Y against tile's min/max Y accumulator

Per-tile BSS accumulators:
- `g_TileMaxY[256]` — per-tile max-Y (float), initialised to −∞ at grid reset
- `g_TileMinY[256]` — per-tile min-Y (float), initialised to +∞ at grid reset


