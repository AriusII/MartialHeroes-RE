# Format: authoring sidecars  (`.pre` family + `.post` — content-pipeline files the runtime NEVER opens)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers` — but only as a **negative** instruction: build NO runtime parser
> for these families. Every offset an engineer cites must reference this file or the cross-referenced
> detail sections.

<!--
verification: sample-verified (census + .sod.pre / .ted.post layout against the real VFS); the
              .fx<N>.pre vertex stride remains static-hypothesis (arithmetic ambiguity)
ida_reverified: 2026-06-16
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: none
-->

> **status: sample_verified** — census + `.sod.pre` polygon-list layout + `.ted.post` byte count
> re-confirmed against the real VFS mount (43 347 entries) on build `263bd994`. The
> `.fx<N>.pre` extended vertex stride is the only item still static-hypothesis (see §Known
> unknowns). LIVE/DEAD verdicts unchanged: both families are authoring/editor-only and the runtime
> never opens them.

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
3. **The literal strings `.pre` and `.post` do not drive any runtime load path** (corroborated by
   the loader analysis behind `terrain.md` §3.2).

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
mass-object records). A sampled `*.bud.pre` shares the base `.bud` header and first-record fields
(same object count, type, texId, vertex count), differing only slightly in vertex float values — a
**re-saved variant, not a patch**. Full block layout: see `terrain.md` §8. Confidence: HIGH.

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

---

## `.post` family — full drop-in `.ted` (editor workspace copy)

`*.ted.post` is a **full, drop-in copy of its companion `.ted` cell** in the identical 46 987-byte
five-block layout — no wrapper header, no version prefix, no delta/patch framing; offset 0 is the
first heightmap float exactly as in a base `.ted`. The `.post` suffix marks a workspace copy written
by the in-game terrain editor. 852 instances in the VFS, all exactly 46 987 bytes; every sampled
pair is byte-for-byte identical to its companion `.ted`. The runtime never reads it.

This family is already documented in full; **see `terrain.md` §5.10 and §16.1, and
`terrain_layers.md` §5** for the block layout and census. It is reproduced here only as part of the
sidecar index — no new layout facts.

**Known unknown (`.post`):** whether any `.post` in this VFS snapshot ever diverges in content from
its companion `.ted` is UNVERIFIED (the editor may copy then patch, or the snapshot predates any
edit). This does not affect the format verdict — the layout is identical regardless, and the runtime
ignores it either way.

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
