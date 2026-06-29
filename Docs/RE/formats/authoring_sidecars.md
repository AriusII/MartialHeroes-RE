# Format: authoring sidecars  (`.pre` family + `.post` — content-pipeline files the runtime NEVER opens)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers` — but only as a **negative** instruction: build NO runtime parser
> for these families. Every offset an engineer cites must reference this file or the cross-referenced
> detail sections.

## Re-verification banner (2026-06-27 — CYCLE 14 re-anchor, confirmatory)

| Attribute        | Value |
|------------------|-------|
| `ida_reverified` | `2026-06-27` |
| `ida_anchor`     | `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` |
| `verification`   | `sample-verified` — CYCLE 14 re-anchor (f61f66a9): confirmatory — subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected. All prior verification findings from 2026-06-24 remain valid. |
| `evidence`       | `[static-ida]` |
| `conflicts`      | None. |
| `consolidation`  | `2026-06-29` — `cell_post.md` and `cell_pre.md` absorbed into master: Block-5 diffuse-RGBA characterisation, 2.0-on-export detail, `+12` bud.pre vertex-tag offset, and `Sod_CompileOutline` algorithm folded; no layout values altered; no prior cross-refs to those files existed in master. |

---

## Re-verification banner (2026-06-24 — sidecar string / save-protocol pass)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `sample-verified` — census + `.sod.pre` polygon-list layout + `.ted.post` byte count re-confirmed against the real VFS mount (43 347 entries); `.bud.pre` vertex-reorder mechanism confirmed against a same-cell sample pair; `.ted.post` copy-then-patch save protocol confirmed (IDA static path trace); `.pre` confirmed absent from binary string table. |
| `ida_reverified` | `2026-06-24` |
| `ida_anchor`     | `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` |
| `evidence`       | `[static-ida, vfs-sample]` — witness 1 = IDA static string-table search (`.pre` = zero hits; `.ted.post` path-builder present as write-only export target, never a read path) + dev-tool exporter call chain (`Ted_ExportCell_Entry` → cell serialiser — full `.ted.post` write then in-place `.ted` grid-block patch at byte offset 30 087); witness 2 = same-cell on-disk sample pair (`.ted` / `.ted.post` `cmp` clean; `.bud` / `.bud.pre` differ by vertex order and one per-vertex tag byte; `.sod.pre` 40 B = exact size formula) |
| `conflicts`      | None. Prior "Known unknown (`.post`)" — "the editor may copy then patch" — resolved: confirmed as full write to `.ted.post` followed by in-place grid-block patch to the live `.ted` at byte offset 30 087. No value in the layout tables changed. |

---

## Purpose of this doc

The legacy content pipeline leaves two families of per-cell files in the VFS that the **shipped
runtime client never opens**: the `.pre` family (`*.sod.pre`, `*.bud.pre`, `*.fx<N>.pre`) and the
`*.ted.post` editor sidecar. They are build-time / editor artefacts only.

This doc is the **single index** of those families so an `Assets.Parsers` engineer knows, at a
glance, **not to write a runtime parser for them**. Full byte-level layouts (where decoded) live in
the detail sections cross-referenced below; this doc consolidates the verdict, the census, and the
deep-pass layout facts, and does not duplicate the per-format block tables.

---

## KEY ENGINEERING RULE (read this first)

**NO runtime parser is needed for any `.pre` file or any `.post` file.** The shipped client resolves
every per-cell asset through the **base** extension named on a `.map` `DATAFILE` line and never
touches a sidecar.

The decisive evidence (CONFIRMED, multi-source):

1. **No `.map` `DATAFILE` line ever names a `.pre` or a `.post`** — verified across a representative
   set of `.map` cells, **including the single cell that owns the only `.fx7.pre` instance**
   (`d019x10008z10016`, whose `DATAFILE` lines name `.ted` / `.exd` / `.fx2` / `.fx7` / `.bud` — no
   `.fx7.pre`). The only `DATAFILE` extensions seen are base extensions (`.ted`, `.exd`, `.up`,
   `.fx1`…`.fx7`, `.bud`, `.sod`).
2. **The VFS open router does no extension rewriting / fallback / base-pre-post selection.** It only
   chooses mounted-VFS-blob vs. raw-OS-file by access mode. There is no code path that prefers a
   `.pre` or `.post` over its base file.
3. **`.pre` is entirely absent from the client binary.** A full string-table search of `doida.exe`
   finds zero occurrences of the literal `.pre` (or `sod.pre`, `bud.pre`, `%s.pre`). The only
   `"pre"` substring hits are unrelated English words. The extension is not produced or consumed by
   any code path in the runtime — it is a pure external map-editor byproduct. **`.ted.post` does
   appear as a path-builder string in the binary, but only as the output target of the dev-tool
   terrain-cell exporter** (the function reached via `Ted_ExportCell_Entry`) — a write-only export
   path, never a read or load path. The cell-list loader (`d%s.lst` path, per-area cell-key table)
   does not reference `.ted.post` at all.

Therefore: an engineer wiring `Assets.Parsers` resolves the base extension only and **ignores
`.pre`/`.post` entirely**. When documenting why these extensions are skipped, cite
`// spec: Docs/RE/formats/authoring_sidecars.md`.

---

## Census (full VFS mount, 43 347 entries — SAMPLE-VERIFIED)

| Family       | Count | Per-base breakdown                                                                 |
|--------------|------:|-----------------------------------------------------------------------------------|
| `.pre` total | 2 014 | `.sod` 848 · `.bud` 757 · `.fx2` 170 · `.fx1` 127 · `.fx3` 82 · `.fx5` 29 · `.fx7` 1 (no `.fx4`/`.fx6` `.pre` observed) |
| `.post` total | 852  | all `*.ted.post`; every entry exactly 46 987 bytes (= a full `.ted`)               |

Every count above was re-matched **exactly** against the VFS mount on build `263bd994` (`.pre`
2 014, `.post` 852, and the full per-base breakdown), with **zero drift** from the prior campaign.
The `.post` family additionally re-confirms by aggregate byte volume: the VFS reports 40 032 924
bytes for the `.post` extension, and 40 032 924 / 852 = 46 987.0 exactly — every `.post` entry is a
full-size `.ted`.

`.pre` sizes range from tiny (a 40-byte `.sod.pre`, a 195-byte `.bud.pre`) to 800 KB+ — consistent
with **full files of varying content**, not fixed-size patch headers. No compression, no encryption;
raw little-endian throughout (x86 client).

---

## `.pre` family — full standalone files in their embedded base format

A `.pre` file is a **complete, self-contained file in the on-disk format named before `.pre`** (e.g.
`*.bud.pre` is a complete `.bud`, `*.fx<N>.pre` a complete `.fx<N>`, `*.sod.pre` a `.sod`-family
file). It is **not** an offset+length+replacement-byte patch — there is no delta grammar anywhere.
It is a pre-processed / source capture kept beside the shipped base file during the build pipeline.

### `*.bud.pre` — full `.bud`

Same on-disk layout as the base `.bud` building-mesh blob (`u32 objectCount`, then packed
mass-object records). A sampled `*.bud.pre` shares the base `.bud` leading header bytes exactly
(same packed flags, same per-record object count, type, texId, and vertex count), and the trailing
triangle-index list length is identical. The difference is: the per-vertex records appear in a **different order** between the
two files (the vertex sequence is reversed/re-sorted by the save step), and **one per-vertex tag
byte** changes value between the pre-save and post-save versions (the tag byte resides at offset
`+12` within the per-vertex stride). The triangle-index list is
correspondingly re-mapped to the new vertex order. This is a **pre-save snapshot paired with the
post-save shipped file** — not a patch, not a delta. Full block layout: see `terrain.md` §8.
Confidence: HIGH (same-cell sample pair + prior census).

### `*.fx<N>.pre` — extended `.fx<N>` (24-byte record header + wider vertex stride)

`*.fx<N>.pre` is in the same logical family as the base `.fx<N>` (same 16-byte file header — same
`typeCount` / `field3`; same XYZ vertex coordinates for the same cell), but the **per-record body is
extended** relative to the shipped base. Confirmed across `.fx1`, `.fx2`, `.fx3`, `.fx5`, `.fx7` via
one header dump each. (Base `.fx<N>` layout: see `terrain.md` §10 and `terrain_layers.md`.)

Two extensions over the base record:

- **A 24-byte per-record header (6 × f32) precedes `vertexCount` + `indexCount`.** In the inspected
  sample the six floats read as a `(0, 1, 0, 0, 0, 0)` pattern — plausibly a per-record transform
  row, weight vector, or scale/bias tuple. The base `.fx<N>` record has no such block. **Meaning
  UNVERIFIED** (not determinable from the byte values alone).
- **A wider per-vertex stride than the base 36 bytes.** XYZ coordinates match the base for the same
  cell, so this is the same geometry with extra per-vertex storage (likely source UV or normal data).
  The exact `.pre` vertex stride is **provisionally 44 bytes** but **SAMPLE-UNVERIFIED** — the
  arithmetic is ambiguous at the vertex/index boundary in the single small sample (a 4-byte field
  straddles that boundary). A harness that matches `vertex[2..N]` XYZ across all vertices would
  pin it down.

Two corroborating observations (both characterise the family as a content-build product, not a
runtime asset):

- For one cell, `*.fx1.pre` and `*.fx2.pre` are **byte-for-byte identical** — the build tool wrote
  the same pre-processed mesh into both slots, unaware of the fx1/fx2 layer distinction at the
  authoring stage.
- The single `*.fx7.pre` has `typeCount = 2` while its base `.fx7` has `typeCount = 1`, and is
  ~3× the base size — i.e. **different content** (more layer records preserved in the authoring
  source than in the shipped base), not merely a different encoding of the same content. Confirms a
  full standalone file, not a patch.

> Note vs. `terrain.md` §16.2: that section flagged `.fx<N>.pre` internals as "not byte-inspected
> (expected full base format)". This deep pass inspected them and found the **extended** record body
> above — `.fx<N>.pre` is the same family as the base but **wider**, not byte-identical to it.

### `*.sod.pre` — LEAN polygon list (VERIFIED across 848 files)

`*.sod.pre` is **not** the runtime `.sod` 108-byte `SolidRecord` (+ 48-byte `QuadRecord`) layout of
`terrain.md` §11 / §16. It is a **lean variable-polygon XZ point list** — the authoring source the
build compiles into the runtime `.sod`. The two formats are incompatible in stride and record
structure; this is two stages of the same pipeline, not a conflict.

**Layout (little-endian; VERIFIED across 848 files by size-distribution analysis + header dumps):**

| Element                | Type     | Notes                                                                      | Confidence |
|------------------------|----------|----------------------------------------------------------------------------|------------|
| `polyCount`            | u32 LE   | Number of polygons that follow.                                            | VERIFIED   |
| per polygon: `vertexCount` | u32 LE | Corners in this polygon. Observed values: 3, 4, 5, 6, 7 (tri…heptagon).  | VERIFIED   |
| per polygon: `corners` | (f32 X, f32 Z) × `vertexCount` | XZ pair per corner. **No Y is stored** — 2D XZ plane, matching the runtime `.sod` horizontal-plane collision model. | VERIFIED |

- **Record count source:** `polyCount` (the leading u32).
- **Per-polygon stride:** variable — `4 + vertexCount × 8` bytes (the corner pair is 8 bytes).
- **File-size formula:** `4 + Σ over polygons (4 + vertexCount_i × 8)`. For a uniform-vertex-count
  file this collapses to `4 + polyCount × (4 + vertexCount × 8)`. Spot-checked exact across tri,
  quad, pentagon, hexagon, heptagon files and across mixed-polygon files.

**Re-verification (build `263bd994`, two distinct files):** the formula was re-confirmed on the real
VFS against both ends of the size range:
- A **40-byte** all-quad file: `polyCount = 1`, the single polygon's `vertexCount = 4`, followed by
  4 XZ corner pairs (32 bytes). `4 + 4 + 32 = 40` — exact.
- A **1 048-byte** all-quad file: `polyCount = 29`, first polygon `vertexCount = 4`.
  `4 + 29 × (4 + 4×8) = 4 + 29 × 36 = 1 048` — exact.

These bracket the n=1 and the multi-polygon cases and re-pin `polyCount` as the leading u32, the
per-polygon `vertexCount` u32, and the 8-byte (X,Z) corner pair with no Y stored.

Files with uniform vertex counts and files with mixed counts both occur; small files tend to be
uniform (all-quads or all-tris), larger files mix shapes.

> Note vs. `terrain_layers.md` §4: that section documents the **single-polygon special case** as an
> 8-byte header (`u32 version = 1`, `u32 vertex_count`) followed by XZ pairs. That is the
> `polyCount == 1` instance of the general format above — the leading u32 read there as `version` is
> in fact `polyCount` (always 1 in those three samples), and the second u32 is that single polygon's
> `vertexCount`. The general multi-polygon form (this section, 848 files) supersedes the
> single-polygon reading; treat `terrain_layers.md` §4 as the n=1 slice.

**Polygon-shape semantics** (which corner count maps to which collision primitive) are UNVERIFIED;
not needed for the sidecar verdict.

### `.sod.pre` → `.sod` compilation algorithm (`Sod_CompileOutline`)

The offline compiler function `Sod_CompileOutline` builds the runtime `.sod` from a `.sod.pre`
source. Algorithm recovered from offline-tool output verification (not from the runtime `doida.exe`
path); anchored at `f61f66a9` via sample-pair math confirmation.

1. **Polygon read:** Reads `polyCount` and parses the variable list of XZ coordinate rings.
2. **AABB computation:** For each polygon, derives the axis-aligned bounding box
   (`aabbMinX` / `aabbMinZ` / `aabbMaxX` / `aabbMaxZ`) from the corner coordinates and stores it
   in the `SolidRecord` fields at offsets `+0x00`–`+0x0F` of the 108-byte record
   (see `terrain.md §11` for the full `SolidRecord` layout).
3. **Double-sided wall generation:** For each edge from corner A to corner B in the polygon vertex
   sequence, instantiates a `QuadRecord` (48-byte) with corners A, B, B, A — the pairing encodes a
   bidirectional flat wall. Computes and stores two edge scalars:
   - **`edgeScalar0`** (offset `+0x20`): a slope coefficient derived from the edge direction.
   - **`edgeScalar1`** (offset `+0x28`): squared edge length
     L² = (B_x − A_x)² + (B_z − A_z)².
4. **Output:** Serialises the packed `SolidRecord` array followed by the `QuadRecord` arrays to the
   runtime `.sod` file (see `terrain.md §11` for the 108-byte + 48-byte layouts).

---

## `.post` family — full drop-in `.ted` (editor workspace copy)

`*.ted.post` is a **full, drop-in copy of its companion `.ted` cell** in the identical 46 987-byte
five-block layout — no wrapper header, no version prefix, no delta/patch framing; offset 0 is the
first heightmap float exactly as in a base `.ted`. The `.post` suffix marks a workspace copy written
by the in-game terrain editor. 852 instances in the VFS, all exactly 46 987 bytes; every sampled
pair is byte-for-byte identical to its companion `.ted`. The runtime never reads it.

The block layout is documented in full in **`terrain.md` §5.10 and §16.1** and
**`terrain_layers.md` §5**. It is reproduced here as part of the sidecar index; see the save
protocol below for two additional details (Block 5 RGBA characterisation, 2.0-on-export multiplier)
derived from offline-tool analysis at `f61f66a9`.

**Save protocol (CONFIRMED, IDA static).** The dev-tool terrain-cell exporter (reached via
`Ted_ExportCell_Entry`) implements a copy-then-patch protocol:

1. It re-packs the 65×65 heightfield, normals, texture slots, orientation flags, and diffuse RGBA
   lightmap values (multiplied by **2.0** on export) into a flat 46 987-byte working buffer
   (the five-block `.ted` layout — see `terrain.md §5.10`).
2. It writes a **full, fresh five-block `.ted` to the `.ted.post` path** (`"wb"` open — complete
   file, not a patch).
3. It then opens the **live `.ted` path in `"rb+"` mode, seeks to byte offset 30 087, and overwrites
   only the diffuse RGBA block in place** (Block 5 — 65 × 65 × u8[4] = 16 900 bytes) — leaving the
   other four blocks untouched.

When the same grid data is written to both targets (the typical committed-state scenario), the two
files end byte-identical — which is exactly what the on-disk `cmp` of all sampled pairs confirms.
This resolves the prior known unknown ("the editor may copy then patch"): it demonstrably does both.
The fixed in-place patch offset of 30 087 is the grid-block start in the five-block layout (see
`terrain.md` §5.3). The runtime never reads `.ted.post`; only the dev-tool export chain writes it.

---

## Known unknowns

- **`.fx<N>.pre` exact vertex stride** — provisionally 44 bytes (vs. base 36); SAMPLE-UNVERIFIED
  (arithmetic ambiguous at the vertex/index boundary in the single small sample).
- **`.fx<N>.pre` 24-byte per-record header meaning** — the `(0, 1, 0, 0, 0, 0)` float pattern could
  be a transform row, weight vector, or scale/bias tuple; UNVERIFIED.
- **`.fx7.pre` `typeCount = 2` vs. base `typeCount = 1`** — which records the build step culled is
  not visible from the bytes alone; not blocking (the runtime ignores the file).
- **`.sod.pre` polygon-shape semantics** — the corner counts (3–7) are known; their mapping to
  runtime collision primitives is UNVERIFIED. Not needed for the verdict.
- **`.pre` producer-side semantics** — whether a `.pre` is the source the build consumes or a backup
  of a prior version is not recoverable from the client binary (the producer is a content tool not
  present in it). Static evidence only establishes that the runtime ignores it.

None of these unknowns affects `Assets.Parsers`: no runtime parser is built for either family.

---

## Cross-references

- **Terrain / per-cell asset chain & base parsers:** `Docs/RE/formats/terrain.md` — `.map`
  `DATAFILE` resolution (§3.2), base `.bud` (§8), base `.fx<N>` (§10), base `.sod` (§11), and the
  consolidated sidecar section (§16); `*.ted.post` block layout (§5.10).
- **FX-layer & `.sod.pre` detail:** `Docs/RE/formats/terrain_layers.md` — base FX layer blobs, the
  `.sod.pre` single-polygon (n=1) table (§4), and `*.ted.post` blocks (§5).
- **Canonical names:** see `Docs/RE/names.yaml` (proposed: `pre`, `post`, `sod_pre_polygon_list`,
  `fxN_pre_record_extra`).
- **Provenance:** see `Docs/RE/journal.md` (entry for this spec is appended separately).
