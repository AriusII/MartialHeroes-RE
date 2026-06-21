# Format: `.exd`  (per-cell extended data — walkable-surface triangle mesh sidecar)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers`. Every offset an engineer cites must reference this file.
> C# that reads (or deliberately skips) `.exd` MUST cite `// spec: Docs/RE/formats/cell_exd.md`.

<!--
verification: sample-verified (header u32 count + 40-byte triangle-record array, the
              `size = 4 + count*40` file-size formula, the world-space XZ bounds match against the
              cell tag, and the per-record 10×f32 decode — all matched against the full set of real
              VFS `.exd` cell samples). The runtime client contains NO `.exd` loader, so the on-disk
              layout is sample-verified but loader-UNVERIFIED, and the trailing scalar's MEANING is
              UNVERIFIED.
ida_anchor: 263bd994
evidence: [static-ida, vfs-sample]
conflicts: none on the on-disk layout. One standing note: the `exd` extension string is ABSENT from
           the shipped client binary — see §2 (`.exd` is loaded by a name-driven path, like the other
           cell sidecars, not by a hardcoded per-extension string), consistent with
           formats/authoring_sidecars.md and formats/cell_pre.md.
source-format: derived from Docs/RE/_dirty/formats/cell_exd.raw.md (dirty-room RE of doida.exe).
-->

> **status: sample-verified (layout) / loader-absent (no in-binary consumer).** The container shape
> (`u32 record_count` + a flat array of 40-byte triangle records) and every spatial field are matched
> against the full set of real VFS `.exd` cell samples. The shipped client carries **no `.exd`
> parser** — the layout is recovered from on-disk bytes, not from a read path, so it cannot be pinned
> to a loader the way `.sod` / `.ted` are, and the meaning of the per-triangle trailing scalar (§4)
> remains an open question.

---

## 1. Identification

- **Extension:** `.exd` — a per-cell sidecar that lives beside the other per-cell binary blobs in
  `data/map<area>/dat/`.
- **Logical path / name pattern:** `data/map<area>/dat/d<area>x<cellX>z<cellZ>.exd`, where `<area>` is
  the 3-digit map id and `<cellX>` / `<cellZ>` are the 5-digit cell tags — the **same cell base-path
  scheme** as the cell's `.ted` / `.sod` / `.map` / `.mud` / `.gad` siblings.
- **No magic / no version tag.** The file begins immediately with the record count; there is no
  signature or version field. (A 2-record file leads with the bytes `02 00 00 00`, which could be
  mistaken for a tiny magic, but the count interpretation holds across every sample, confirming it is
  a count.)
- **No compression, no encryption, no checksum, no transform.** Pure little-endian: `u32` count plus
  IEEE-754 `f32` data (x86 client). Values are stored as **final world-space coordinates** (not
  cell-local — see §6).
- **What it is:** a small **triangle mesh** — one record per triangle, three world-space XYZ vertices
  plus one trailing per-triangle scalar. The triangles are a connected, predominantly near-horizontal
  surface (see §6), consistent with a **walkable-surface / floor mesh** for the cell.
- **Optional per cell.** `.exd` is present **only** for cells that need it: in a representative cell
  directory roughly a third of the cells that have `.ted` / `.sod` / `.map` carry an `.exd`. The
  walk-surface mesh is a per-cell opt-in, not part of the mandatory cell set.

---

## 2. Loader / consumer status — ABSENT in the shipped client

- **There is no `.exd` loader or parser in the shipped client binary.** The substring `exd` does not
  occur anywhere in the binary (exhaustive segment scan: zero hits for `exd`, `.exd`, and related
  affixes). Nothing in the runtime opens, reads, or references an `.exd` file.
- This matches the broader cell-sidecar situation (see `formats/authoring_sidecars.md`,
  `formats/cell_pre.md`): the per-cell binary blobs that *are* loaded (`.mud` / `.gad` / `.map` via
  the cell base-path builder; `.ted`, `.ted.post`, `.lst`, `.sod`) are reached either by a small set
  of **hardcoded** cell-path/extension format strings, or — for blobs like `.bud`, `.fx1`…`.fx7`, and
  `.exd` — by a **name-driven** path (a base path concatenated with a relative filename taken from a
  list/descriptor). `.exd` has **no** dedicated extension slot in the cell path-format table.
- **Most plausible origin:** `.exd` is a **tool-generated / exported sidecar**. The binary contains
  the twin pattern of a dev-tool routine that *writes* `.ted` / `.ted.post` back to disk; an analogous
  offline exporter most likely produced `.exd`. It is consumed by an external editor or a companion
  module, or it is simply unused data in this client build.
- **Practical consequence for the port:** there is no original read path to mirror byte-for-byte. The
  read algorithm in §5 is the byte-driven decode any faithful reader must perform; the field at §4 has
  no in-binary consumer to confirm its meaning. If `.exd` is adopted as the walkable-surface / nav
  source in the Godot port, the consumer must be authored fresh (candidate input to ground / collision
  / navigation), not transcribed.

---

## 3. On-disk layout (little-endian; offsets in bytes)

`size = 4 + record_count * 40`. Independently confirmed across every sample: `(size - 4) % 40 == 0`
and `(size - 4) / 40 == record_count`, with no trailing/footer bytes.

### Header (4 bytes)

| Offset | Size | Type | Field          | Notes                                                  |
|-------:|-----:|------|----------------|--------------------------------------------------------|
| `0x00` | 4    | u32  | `record_count` | Triangle count. The 40-byte triangle records follow.   |

### Triangle record (stride = 40 bytes; exactly `record_count` records)

One record is **one triangle**: three vertices (each XYZ `f32`, world space) plus one trailing
`f32` scalar.

| Offset | Size | Type | Field      | Notes                                              |
|-------:|-----:|------|------------|----------------------------------------------------|
| `+0x00`| 4    | f32  | `v0.x`     | vertex 0 — world X                                 |
| `+0x04`| 4    | f32  | `v0.y`     | vertex 0 — world Y (up; height)                    |
| `+0x08`| 4    | f32  | `v0.z`     | vertex 0 — world Z                                 |
| `+0x0C`| 4    | f32  | `v1.x`     | vertex 1 — world X                                 |
| `+0x10`| 4    | f32  | `v1.y`     | vertex 1 — world Y                                 |
| `+0x14`| 4    | f32  | `v1.z`     | vertex 1 — world Z                                 |
| `+0x18`| 4    | f32  | `v2.x`     | vertex 2 — world X                                 |
| `+0x1C`| 4    | f32  | `v2.y`     | vertex 2 — world Y                                 |
| `+0x20`| 4    | f32  | `v2.z`     | vertex 2 — world Z                                 |
| `+0x24`| 4    | f32  | `extra_h`  | per-triangle scalar — see §4 (meaning UNVERIFIED). |

- **Record-count source:** the header `u32` at `0x00`, cross-checked by the file-size formula above.
- **Record stride:** 40 bytes (confirmed across every sample).
- **Endianness:** little-endian. The `f32` little-endian decode yields sane world coordinates; the
  byte-swapped (big-endian) decode yields garbage.

---

## 4. The trailing scalar `extra_h` (`+0x24`) — characterization (meaning UNVERIFIED)

Aggregated over the full sample set:

- The **majority** of triangles (roughly two thirds) store exactly `0.0`; the remaining third are
  nonzero.
- Nonzero values are **continuous floats** (observed range roughly `0.5` … `313`), **none**
  integer-valued — so it is not a flag / enum / id stored as a float.
- It is **not** the triangle's own Y. In one cell with hundreds of triangles, about half had
  `extra_h` equal to the (flat) triangle's Y while the other half had `extra_h == 0` despite the
  triangle carrying a real, varying Y. So `extra_h` is an **independent height-like value**, present
  (nonzero) for some triangles and absent (`0`) for others.

**Working hypothesis (sample-inferred; MUST be confirmed by the consuming tool/engine, which is not
in the shipped client):** a **second / alternate-layer reference height** for multi-level walk
surfaces — e.g. the ground height beneath an elevated bridge/overpass triangle — with `0` meaning
"single layer / none". Alternatives still to disprove: a blend/cost weight, a water level, or a
ceiling-clearance value. **OPEN QUESTION** (see §8). Provisional canonical name: `alt_layer_height`.

---

## 5. Read algorithm (reconstructed; bytes → runtime structures)

No in-binary parser exists, so this is the byte-driven decode any faithful reader must perform. It is
the universal `u32 count + fixed-stride records` idiom shared by the sibling cell blobs (the `.lst`
cell-list loader and the `.sod` / `.ted` blobs), all of which open through the VFS file API:

1. Open the file by its full VFS name `data/map<area>/dat/d<area>x<cellX>z<cellZ>.exd`.
2. Read 4 bytes → `record_count` (`u32` LE).
3. Allocate `record_count` triangles; read `record_count * 40` bytes contiguously.
4. For each 40-byte record, decode 10 LE `f32`: a triangle of `(v0, v1, v2)` world-space XYZ plus
   `extra_h`.
5. Apply no decompression, decryption, checksum, or transform — the values are already final
   world-space coordinates (see §6 for the Godot import transform).

---

## 6. Coordinate conventions

- Stored coordinates are **absolute world space**, NOT cell-local. Every triangle's X and Z fall
  inside the cell's world bounds derived from the cell tag — `[(cellX − 10000)·1024 … +1024]` for X
  and `[(cellZ − 10000)·1024 … +1024]` for Z (cells are 1024 units; see the terrain/cell-streaming
  specs). This is the JOIN that pairs an `.exd` to its cell (see §7).
- **Y is the up axis (height).** Triangles are predominantly near-horizontal (small Y-span relative to
  XZ extent) → a walkable floor surface; a minority are steeper (slopes / ramps).
- The triangles form a **connected mesh** (fans/strips: consecutive records share two vertices), i.e.
  a continuous triangulated surface, not independent quads. A 2-triangle file is just the degenerate
  small case (one flat quad at a single Y level).
- **Godot import transform:** apply the **world-geometry** transform, not the mesh-local one. World
  geometry negates Z (`Helpers/WorldCoordinates.ToGodot`: `(x, y, z) → (x, y, -z)`); the `.exd` X-sign
  is world space and is **not** subject to the `.skn` mesh-local X-negate.
- **Handedness / winding: NOT yet determined** (no in-binary consumer to confirm front-face). Confirm
  winding before trusting computed normals or one-sided collision (see §8).

---

## 7. Linkages

- **Join key = the filename cell tag `(mapId, cellX, cellZ)`.** The name pattern
  `d<area>x<cellX>z<cellZ>.exd` is identical to the cell's `.ted` / `.sod` / `.map` / `.mud`
  siblings, and the join is **proven geometrically**: every `.exd` triangle's X and Z fall inside the
  world bounds the cell loader computes from that same cell tag (§6). An `.exd` is therefore bound to
  its cell by the same `(mapId, cellX, cellZ)` key as the rest of the cell `dat/` set.
- **Sidecar set per cell:** `.ted` (heightfield / terrain — `formats/terrain.md`), `.sod` (2D-XZ wall
  segments / collision — `formats/sod.md`), `.map` (cell descriptor), `.mud` (`formats/mud.md`),
  `.gad`, `.bud` (mesh — `formats/mesh.md`), `.fx1`…`.fx7` (effects), and `.exd`.
- **Sparse presence:** `.exd` exists only for the subset of cells that need the walk-surface mesh
  (roughly a third of the cells that carry `.ted` / `.sod` / `.map` in a representative directory). It
  is optional per cell.
- **Shared coordinate space:** the triangles are directly comparable to `.sod` walls and `.ted`-derived
  ground height, so the natural consumer is the cell-streaming / collision-or-navigation subsystem.
- **Builder / factory / manager:** **none in the shipped client** (no loader exists — see §2). If the
  Godot port adopts `.exd` as the walkable-surface / nav source, the consumer must be wired fresh in
  `Assets.Parsers` (parse) plus the world / nav subsystem (consume).

---

## 8. Open questions / verification debt

1. **Meaning of `extra_h` (`+0x24`)** — alternate-layer reference height, cost/blend weight, water
   level, or ceiling clearance? Cannot be settled from the shipped client (no consumer). Would need
   the producing tool, or a build that actually reads `.exd`.
2. **Winding / front-face** — undetermined; confirm before using `.exd` triangles for normals or
   one-sided collision.
3. **Loader genuinely absent vs. living in a companion module** — within the shipped client it is
   conclusively absent (no `exd` bytes at all). If a separate editor/tool binary becomes available it
   should be checked there.
4. **Header is truly a bare `u32` count** (no hidden version / flags) — consistent across the whole
   sample set; re-confirm if a file with a semantically ambiguous (e.g. very small) count is found
   elsewhere.

---

## 9. Cross-references

- `formats/sod.md` — per-cell 2D-XZ wall-collision segments (sibling collision blob).
- `formats/terrain.md` — `.ted` heightfield / cell terrain grid (ground height).
- `formats/mud.md` — `.mud` cell descriptor / mesh sidecar.
- `formats/cell_pre.md`, `formats/authoring_sidecars.md` — the per-cell `.pre` / `.post` /
  tool-exported sidecar family and the "tool output, not loaded at runtime" verdict that `.exd`
  shares.
