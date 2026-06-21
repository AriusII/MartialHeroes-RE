# Format: `.sod.pre` (per-cell precomputed collision outline; `.pre`/`.post` cell-sidecar family)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers` only as a **negative** instruction: the shipped runtime never opens
> a `.pre`/`.post` sidecar, so build NO runtime parser for them. This file documents the
> `.sod.pre` on-disk layout and its relationship to the runtime `.sod` collision blob it shadows.
> C# that touches (or deliberately skips) these files MUST cite `// spec: Docs/RE/formats/cell_pre.md`.

<!--
verification: sample-verified (.sod.pre polygon-list layout + the shadow .sod SolidRecord/QuadRecord
              layout, cross-checked against real VFS cell samples and the legacy .sod read path);
              the QuadRecord +32 / +40 authoring scalars remain UNVERIFIED (runtime never reads them).
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: none — reconciles with formats/authoring_sidecars.md (family verdict + census) and
           refines the shadow .sod SolidRecord/QuadRecord layout flagged in formats/terrain.md §11/§16.
source-format: derived from Docs/RE/_dirty/formats/cell_pre.raw.md (dirty-room RE of doida.exe).
-->

> **status: sample-verified** — `.sod.pre` polygon-list layout and the shadow `.sod` runtime layout
> (SolidRecord 108 B + per-solid QuadRecord 48 B + XZ quadtree) are matched against real VFS cell
> samples and against the legacy `.sod` read path. The "tool output, never loaded at runtime" verdict
> is the same as `authoring_sidecars.md` and is re-confirmed three independent ways (below).

---

## 1. Identification

- **Extension:** `.sod.pre` — a per-cell sidecar that shadows the runtime `.sod` collision blob.
- **Family:** `.sod.pre` is one member of a whole **`.pre` / `.post` precomputed-sidecar family**
  that lives beside the per-cell binary blobs in `data/map<area>/dat/`. Within one cell directory the
  observed pairs are:
  - `.sod`  + `.sod.pre`   — collision
  - `.ted`  + `.ted.post`  — terrain grid (note: `.post`, not `.pre`)
  - `.bud`  + `.bud.pre`   — buildings / mesh
  - `.fx1`…`.fx7` + `.fx<N>.pre` — FX object groups
  - `.exd`, `.map` have NO sidecar.
- **No magic / no header tag.** `.sod.pre` is self-describing by leading counts; pure little-endian
  IEEE-754 floats and `u32` counts (x86 client). No compression, no encryption, no transform.
- **What it is:** the precomputed simplified **2D-XZ polygon outline** of each `.sod` solid — one
  vertex ring per solid. It is the authoring/preprocessor source (or a re-export) that the offline
  content tool compiled into the runtime `.sod`'s edge-quad + quadtree representation.

> **The runtime client NEVER opens a `.sod.pre`** (or any `.pre`/`.post`). See §5. For the family-wide
> verdict, census, and the `.bud.pre` / `.fx<N>.pre` / `.ted.post` siblings, this spec defers to
> **`Docs/RE/formats/authoring_sidecars.md`** (the family index); this file is the `.sod.pre`-focused
> detail spec plus the `.sod.pre` ↔ `.sod` relationship.

---

## 2. On-disk layout — `.sod.pre`

Little-endian. No magic. Counts are leading and self-describing. Coordinates are world-space XZ in
cell space (no Y is stored — matches the runtime `.sod` horizontal-plane collision model).

### 2.1 File header

| Offset | Size | Type | Field       | Notes |
|-------:|-----:|------|-------------|-------|
| 0x00   | 4    | u32  | `polyCount` | Number of outline polygons that follow (≈ one per `.sod` solid). |

### 2.2 Polygon record (repeats `polyCount` times, variable length)

| Offset (in record) | Size        | Type      | Field        | Notes |
|-------------------:|------------:|-----------|--------------|-------|
| +0                 | 4           | u32       | `vertCount`  | Vertices in this polygon ring. |
| +4                 | 8 × `vertCount` | f32[2] × n | `outlineVerts` | Sequence of `{ f32 X, f32 Z }` world-XZ points. |

- **Per-polygon stride:** `4 + 8 × vertCount` bytes (the XZ pair is 8 bytes).
- **File size formula:** `4 + Σ over polygons ( 4 + 8 × vertCount_i )`. For a uniform-vertex-count
  file this collapses to `4 + polyCount × (4 + 8 × vertCount)`.
- **Sampled vertex counts** are small polygons (triangles and quads dominate; the family census in
  `authoring_sidecars.md` records corner counts up to 7). Files with uniform vertex counts and files
  that mix shapes both occur.
- **Sample cross-checks** (real VFS cells, parse-to-EOF exact):
  - A small `.sod.pre` of a single triangle: `polyCount = 1`, `vertCount = 3`, three XZ pairs. Its XZ
    bounding box equals the shadow `.sod`'s SolidRecord AABB exactly.
  - A large `.sod.pre`: `polyCount = 326`, mixed `vertCount` of 3/4, parses exactly to end of file.

> The single-polygon case (`polyCount == 1`) is the n=1 slice of this general format. Where older
> notes (`terrain_layers.md` §4) read the leading `u32` as a "version" and the second as a vertex
> count, the leading value is in fact `polyCount` (1 in those samples) and the second is that single
> polygon's `vertCount`; this general multi-polygon form supersedes that reading.

---

## 3. The shadow `.sod` (runtime collision blob this `.pre` precedes)

`.sod.pre` is a derivation of `.sod`, so the `.sod` runtime layout is documented here for the
relationship to make sense. (The base `.sod` is also covered in `terrain.md` §11/§16; the layout
below refines the SolidRecord / QuadRecord field tables.)

### 3.1 `.sod` file structure

| Offset | Size | Type | Field        | Notes |
|-------:|-----:|------|--------------|-------|
| 0x00   | 4    | u32  | `solidCount` | Number of solids. |

Then `solidCount × 108-byte SolidRecord` as one flat block, followed by a per-solid tail:
`u32 quadCount` then `quadCount × 48-byte QuadRecord`, repeated for each solid in order.

### 3.2 SolidRecord (108 B / 0x6C) — on-disk meaningful fields

Only the AABB is meaningful on disk; the remainder of the record is in-memory scratch and is zero (or
ignored authoring junk) on disk.

| Offset (in record) | Size | Type | Field      | Notes |
|-------------------:|-----:|------|------------|-------|
| +0   | 4  | f32 | `aabbMinX` | Solid AABB min X. |
| +4   | 4  | f32 | `aabbMinZ` | Solid AABB min Z. |
| +8   | 4  | f32 | `aabbMaxX` | Solid AABB max X. |
| +12  | 4  | f32 | `aabbMaxZ` | Solid AABB max Z. |
| +16…+59 | 44 | — | (scratch) | Zero on disk. In memory: quadtree child pointers, the XZ split center, and flags filled in during load. |
| +60  | 4  | u32 | `quadCount` (in-memory) | On disk this slot is inside the zero region; the authoritative `quadCount` is the separate `u32` at the start of the per-solid tail, which the loader writes back into this slot in memory. |
| +64  | 4  | ptr | `quadArray` (in-memory) | Pointer to the allocated quad array; on disk this slot holds ignored authoring junk that the loader overwrites. |

### 3.3 QuadRecord (48 B / 0x30) — per-edge collision quad

The runtime reads ONLY the corner floats; the two trailing scalars are authoring metadata that no
runtime field-read touches.

| Offset (in record) | Size | Type | Field   | Notes |
|-------------------:|-----:|------|---------|-------|
| +0   | 4  | f32 | `aX`     | Corner / edge point A.X. |
| +4   | 4  | f32 | `aZ`     | A.Z. |
| +8   | 4  | f32 | `bX`     | Point B.X. |
| +12  | 4  | f32 | `bZ`     | B.Z. |
| +16  | 4  | f32 | `cX`     | Point C.X (second edge pair). |
| +20  | 4  | f32 | `cZ`     | C.Z. |
| +24  | 4  | f32 | `dX`     | Point D.X. |
| +28  | 4  | f32 | `dZ`     | D.Z. |
| +32  | 4  | f32 | `edgeScalar0` | Small value (authoring metadata — possible edge slope/normal). **UNVERIFIED**; never read at runtime. |
| +36  | 4  | u32 | `pad0`   | Always 0 in samples. |
| +40  | 4  | f32 | `edgeScalar1` | Large value (authoring metadata — possible squared length / plane constant). **UNVERIFIED**; never read at runtime. |
| +44  | 4  | u32 | `pad1`   | Always 0 in samples. |

- **Sample cross-check:** a 1-solid `.sod` (AABB equal to the matching `.sod.pre` triangle's bounds)
  has `quadCount = 3`, and the three QuadRecords' corner floats are permutations of the same three
  outline vertices found in the `.sod.pre`. A large `.sod` of 320 solids parses exactly to EOF.

---

## 4. Read algorithm

### 4.1 `.sod.pre` (our parser — the client has no decoder for it)

```
u32 polyCount
for p in 0 .. polyCount-1:
    u32 vertCount
    for v in 0 .. vertCount-1:
        f32 X
        f32 Z
```

All values little-endian; no transform, no decompression, no validation magic. Each polygon is a
closed XZ ring (no Y). Stop exactly at EOF (`4 + Σ(4 + 8·vertCount_i)`).

### 4.2 `.sod` (the runtime path, for reference)

1. Read `u32 solidCount`.
2. Allocate `SolidRecord[solidCount]` (108 B each, zeroed) and read `108 × solidCount` bytes in one go.
3. For each solid in order:
   - Read `u32 quadCount`; write it into the SolidRecord (`+60`).
   - Allocate `48 × quadCount` bytes for the quad array (`+64`) and read them in one go.
   - Build an XZ quadtree over the solid's quads (recursive partition; leaf cap 16) using the
     SolidRecord AABB and the computed split center.
4. **Validation:** essentially none — no magic, no count clamp. The blob is trusted as authored.
5. Runtime collision query = 2D-XZ ray-parity (point-in-polygon / segment crossing) walking the
   quadtree, consistent with the documented `.sod` "2D XZ wall segments" collision model.

---

## 5. Why the runtime never loads `.sod.pre` (the family verdict)

Proven three independent ways (binary-won):

1. **No `.pre` (and no `%s.pre`) format string exists anywhere in the client binary.** `.post` appears
   only inside the single `.ted.post` literal used by the dev exporter (below).
2. **The VFS open router uses the path verbatim.** It chooses mounted-VFS-blob vs. raw-OS-file by
   access mode only — there is no extension rewrite, no `.pre`/`.post` fallback, no suffix
   concatenation anywhere in the router or its callers. A `.pre`/`.post` file can only ever be opened
   if a caller passes that exact string, and no runtime caller does.
3. **The `.map` cell descriptor names only base files.** The cell descriptor parser walks blocks
   (`TERRAIN | EXTRA_TERRAIN | UP_TERRAIN | BUILDING | FX1…FX7 | SOLID`), each with a `DATAFILE <path>`
   token, and dispatches the `SOLID` block's `DATAFILE` (a base `.sod`) to the runtime `.sod` reader.
   No `DATAFILE` line ever names a `.pre` or a `.post`.

**The smoking gun for the family:** the only code that touches a sidecar is a **dev-tool / map-editor
exporter that WRITES `.ted.post`** — it builds the path from a `…d%sx%dz%d.ted.post` format string and
opens that file for writing. By the same pipeline, the `.sod.pre` / `.bud.pre` / `.fx<N>.pre` files are
produced by that family of offline authoring tools and are never consumed by the shipped client.

> **Engineering rule:** `Assets.Parsers` resolves the base extension only and ignores `.pre`/`.post`
> entirely. Build collision from the runtime `.sod` (the ground truth the client collides against).
> `.sod.pre` MAY be used as a convenience nav/debug outline, but it is NOT authoritative geometry.

---

## 6. Linkages

- **Join key:** the cell key `d<area>x<X>z<Z>` joins every per-cell blob (`.ted` / `.exd` / `.up` /
  `.bud` / `.fx<N>` / `.sod`) and their `.pre` / `.post` sidecars within `data/map<area>/dat/`. Cells
  that exist in an area are enumerated by `data/map<area>/dat/d<area>.lst` (a `u32` count followed by
  `count × u32` cell keys, built into an in-memory cell index); per-cell `.sod` / `.ted` blobs load on
  demand as the cell is streamed.
- **Parent / reference:** the cell `.map` descriptor's `SOLID` block `DATAFILE` token names the base
  `.sod` path. `.sod.pre` is referenced by **nothing** in the runtime — it is an unreferenced sidecar.
- **What `.sod` references:** nothing external; pure geometry. Coordinates are absolute world XZ; a
  cell's world origin is `((X − 10000) × 1024, (Z − 10000) × 1024)` with a 1024-unit span (the
  standard cell-streaming origin).
- **Consumer / manager:** the runtime `.sod` reader builds the in-memory solid + XZ quadtree owned by
  the terrain/cell manager object; collision and ground queries walk that quadtree. `.sod.pre` has no
  consumer in the shipped client.
- **`.sod.pre` ↔ `.sod` relationship (the headline finding):** `.sod.pre` is the precomputed
  simplified polygon outline (one XZ vertex ring per solid) that the offline tool compiled into the
  `.sod`'s edge-quad + quadtree collision form. The counts are close but not strictly equal
  (`polyCount ≈ solidCount`, e.g. 326 vs. 320), so `.pre` is a **separate authoring artifact, not a
  1:1 mirror** of the runtime solids (merged/extra rings are possible). The two formats differ in
  stride and record structure — two stages of one pipeline, not a conflict.

---

## 7. Known unknowns

- **QuadRecord `edgeScalar0` (+32) and `edgeScalar1` (+40) meaning** — small vs. large scalar
  (e.g. edge slope/normal vs. squared length / plane constant). The runtime never reads them, so only
  an authoring-tool RE or a statistical fit over many samples could settle it. Marked authoring
  metadata, UNVERIFIED.
- **QuadRecord corner semantics** — whether the four corner floats are two directed edges (A–B and
  C–D) or one quad winding (A, B, C, D). Samples show the two pairs are reverse-ordered duplicates of
  the same two points, suggesting a single edge stored in both directions; confirming requires the
  collision-query consumer's logic.
- **`.sod.pre` polygon-shape semantics** — which corner count maps to which collision primitive is
  UNVERIFIED. Not needed for the verdict (the runtime ignores the file).
- **`.pre` producer-side semantics** — whether a `.pre` is the source the build consumes or a backup
  of a prior version is not recoverable from the client binary; static evidence only establishes that
  the runtime ignores it.

None of these affects `Assets.Parsers`: no runtime parser is built for the `.pre`/`.post` family.

---

## 8. Cross-references

- **Family index / verdict / census:** `Docs/RE/formats/authoring_sidecars.md` — the whole
  `.pre`/`.post` family (`.bud.pre`, `.fx<N>.pre`, `.ted.post`), the full VFS census, and the
  "never loaded at runtime" rule. This file is the `.sod.pre`-focused detail spec.
- **Base per-cell assets & `.map` `DATAFILE` resolution:** `Docs/RE/formats/terrain.md` (base `.sod`
  §11/§16, `.bud`, `.fx<N>`, `.ted.post` block layout) and `Docs/RE/formats/terrain_layers.md`
  (FX-layer blobs, the `.sod.pre` single-polygon n=1 slice §4).
- **Cell enumeration:** `Docs/RE/formats/area_inventory.md` (the `d<area>.lst` cell index).
