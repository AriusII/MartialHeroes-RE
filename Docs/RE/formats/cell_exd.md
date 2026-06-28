# Format: `.exd`  (per-cell extended data — EXTRA_TERRAIN walkable-surface triangle mesh sidecar)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers`. Every offset an engineer cites must reference this file.

---

## Re-verification banner (2026-06-27 — CYCLE 14 re-anchor, build f61f66a9)

| Attribute        | Value |
|------------------|-------|
| `verification`   | CYCLE 14 re-anchor (f61f66a9): 1 fact re-confirmed SAME, 0 corrected. Covered fact: `.exd` decoder (`Exd_DecodeTriangles`) is byte-identical to the `.up` decoder (`Up_DecodeTriangles`) — identical read sequence, same shared 40→72 expander, same element ctors; both use `u32 count + count × 40-byte` triangle records with the 72-byte runtime layout. Build-stable under the uniform +0x80 relocation. |
| `ida_reverified` | `2026-06-27` |
| `ida_anchor`     | `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` |
| `evidence`       | `[static-ida]` — both decoders re-confirmed at their relocated addresses under build f61f66a9 and verified to share the same expander callee. |
| `conflicts`      | None. |

---

## Verification banner

| Attribute | Value |
|-----------|-------|
| `verification` | `loader-VERIFIED` — the on-disk layout (u32 record count + 40-byte triangle record array, `size = 4 + count × 40` file-size formula, world-space XYZ verts, trailing scalar copied verbatim) is confirmed by the EXD triangle decoder identified in `doida.exe`. The runtime load path is: cell `.map` text parser encounters an `EXTRA_TERRAIN { DATAFILE <path>.exd }` block, opens the named file, and routes it to the EXD triangle decoder, which reads `record_count`, allocates `record_count` 72-byte runtime objects, and per record reads 40 bytes then expands them. The on-disk layout and decoder behavior are CONFIRMED; the semantic meaning of the trailing scalar field is OPEN (see §4). |
| `ida_reverified` | `2026-06-24` (CYCLE 12, IDB SHA 263bd994): prior "loader absent / tool-output / unused at runtime" verdict REFUTED — a runtime EXD triangle decoder is present and is reached name-driven via the `.map` EXTRA_TERRAIN token. The `exd` extension string is still absent from the binary (no hardcoded extension — the VFS path is taken from the DATAFILE token in the `.map` block). |
| `ida_anchor` | `263bd994` |
| `evidence` | `[static-ida, vfs-sample]` — EXD triangle decoder role confirmed by static analysis; on-disk layout cross-checked against two real samples (`data/map000/dat/d000x10000z9990.exd`: 590 records / 23 604 B; `data/map001/dat/d001x10000z10000.exd`: 395 records / 15 804 B; both satisfy `(size − 4) / count = 40` exactly) |
| `conflicts` | **Correction (anchor 263bd994):** prior doc verdict "there is no `.exd` loader or parser in the shipped client" is REFUTED. A runtime EXD triangle decoder is present; it is reached name-driven via the cell `.map` `EXTRA_TERRAIN { DATAFILE ... }` token, not via a hardcoded `exd` extension string (which is why an extension-string scan found zero hits — that was correct but the conclusion was wrong). The "tool output, not loaded at runtime" framing in prior §2, §5, §7, and §8 is removed. |

> **Correction record (anchor 263bd994).** The prior committed doc asserted "there is no `.exd` loader
> in the shipped client" and classed `.exd` as tool-generated / unused at runtime. **This is REFUTED.**
> The binary contains a runtime EXD triangle decoder that is invoked by the master cell `.map` parser
> whenever it encounters an `EXTRA_TERRAIN { DATAFILE ... }` block. The `exd` extension literal is
> absent from the binary because the file is opened by full VFS path taken from the `.map` DATAFILE
> token — the same name-driven pattern used by `.bud` / `.fx1`–`.fx7` siblings. The on-disk layout
> was already correct; only the loader-status verdict and its downstream framing needed correction.

---

## 1. Identification

- **Extension:** `.exd` — a per-cell sidecar that lives beside the other per-cell binary blobs in
  `data/map<area>/dat/`.
- **Logical path / name pattern:** `data/map<area>/dat/d<area>x<cellX>z<cellZ>.exd`, where `<area>` is
  the 3-digit map id and `<cellX>` / `<cellZ>` are the 5-digit cell tags — the **same cell base-path
  scheme** as the cell's `.ted` / `.sod` / `.map` / `.mud` / `.bud` siblings.
- **No magic / no version tag.** The file begins immediately with the record count; there is no
  signature or version field. (A 2-record file leads with the bytes `02 00 00 00`, which could be
  mistaken for a tiny magic, but the count interpretation holds across every sample, confirming it is
  a count.)
- **No compression, no encryption, no checksum, no transform.** Pure little-endian: `u32` count plus
  IEEE-754 `f32` data (x86 client). Values are stored as **final world-space coordinates** (not
  cell-local — see §6).
- **What it is:** a small **triangle mesh** — one record per triangle, three world-space XYZ vertices
  plus one trailing per-triangle scalar. The triangles are a connected, predominantly near-horizontal
  surface (see §6), consistent with a **walkable-surface / auxiliary floor mesh** for the cell.
  The EXD triangle decoder in the binary derives an XZ bounding box and a normalized plane equation
  per triangle at load time, confirming runtime use as a ground-height / spatial point-query surface
  (an auxiliary surface beyond the regular `.ted` heightfield grid).
- **Optional per cell.** `.exd` exists only for cells whose `.map` descriptor emits an `EXTRA_TERRAIN`
  block. In a representative cell directory (`map001/dat/`) 69 of 144 `.ted`-bearing cells carry an
  `.exd` (about 48 %). The fraction is area-dependent — roughly a third to a half. It is a per-cell
  opt-in, not part of the mandatory cell set.
- **Extension string absent from the binary.** A segment scan for the substring `exd` finds zero hits.
  The file is named entirely by the DATAFILE token in the `.map` block, not by a hardcoded per-extension
  format string — consistent with the name-driven open pattern used by `.bud` and `.fx1`–`.fx7`.

---

## 2. Loader / consumer status — loader VERIFIED

The EXD triangle decoder is present in the shipped client binary and is invoked at runtime.

**Load chain (confirmed by static analysis, IDB SHA 263bd994):**

1. The master cell `.map` text parser reads the cell descriptor block-by-block. When it encounters the
   `EXTRA_TERRAIN` keyword (the only literal `EXTRA_TERRAIN` string in the binary) it reads the
   `DATAFILE <full vfs path>` sub-token.
2. The full VFS path (e.g. `data/map001/dat/d001x10000z10001.exd`) is opened through the generic
   disk-file open router. No hardcoded `exd` extension is used — the path is taken verbatim from the
   `.map` text token.
3. The opened file is handed to the EXD triangle decoder (see §5 for its behavior).
4. The decoded triangle array is retained per-cell alongside the `.ted` heightfield and `.sod`
   collision data. The presence of a runtime-derived XZ bounding box and normalized plane equation
   per triangle confirms the decoded data is queried at runtime (ground-height / point-location use).

**The `exd` extension string is absent from the binary** because the file is reached name-driven — not
a deficiency in coverage but a design choice matching `.bud` / `.fx1`–`.fx7`. The earlier deduction
that "absent string → no consumer" was incorrect; the consumer exists and is confirmed.

**Practical consequence for the port:** a read path exists in the original binary that can be mirrored
faithfully. A faithful `.exd` parser in `Assets.Parsers` should follow the loader algorithm in §5.
The Godot / navigation consumer must be wired fresh (no `Assets.Parsers` output is auto-consumed by
the original render path — the decoded triangle data is geometry only).

---

## 3. On-disk layout (little-endian; offsets in bytes)

`size = 4 + record_count * 40`. Independently confirmed across both samples:
`(size − 4) % 40 == 0` and `(size − 4) / 40 == record_count`, with no trailing/footer bytes.

### Header (4 bytes)

| Offset | Size | Type | Field          | Notes                                                  |
|-------:|-----:|------|----------------|--------------------------------------------------------|
| `0x00` | 4    | u32  | `record_count` | Triangle count. The 40-byte triangle records follow immediately. The EXD decoder reads exactly 4 bytes here first. |

### Triangle record (stride = 40 bytes; exactly `record_count` records)

One record is **one triangle**: three vertices (each XYZ `f32`, world space) plus one trailing `f32`
scalar. The decoder reads each record as 40 bytes (0x28) into a scratch buffer and expands it into a
72-byte runtime object (see §5).

| Offset  | Size | Type | Field      | Notes                                              |
|--------:|-----:|------|------------|----------------------------------------------------|
| `+0x00` | 4    | f32  | `v0.x`     | vertex 0 — world X                                 |
| `+0x04` | 4    | f32  | `v0.y`     | vertex 0 — world Y (up; height)                    |
| `+0x08` | 4    | f32  | `v0.z`     | vertex 0 — world Z                                 |
| `+0x0C` | 4    | f32  | `v1.x`     | vertex 1 — world X                                 |
| `+0x10` | 4    | f32  | `v1.y`     | vertex 1 — world Y                                 |
| `+0x14` | 4    | f32  | `v1.z`     | vertex 1 — world Z                                 |
| `+0x18` | 4    | f32  | `v2.x`     | vertex 2 — world X                                 |
| `+0x1C` | 4    | f32  | `v2.y`     | vertex 2 — world Y                                 |
| `+0x20` | 4    | f32  | `v2.z`     | vertex 2 — world Z                                 |
| `+0x24` | 4    | f32  | `extra_h`  | per-triangle trailing scalar — stored verbatim in the runtime object, not used in bbox/plane construction; see §4 for characterization and open question. |

- **Record-count source:** the header `u32` at `0x00`, cross-checked by the file-size formula above.
- **Record stride:** 40 bytes — confirmed by the decoder's 0x28 per-record read and by both samples.
- **Endianness:** little-endian. The `f32` little-endian decode yields sane world coordinates; the
  byte-swapped (big-endian) decode yields garbage. Confirmed by sample bounds cross-checked against
  cell tags (§6).

---

## 4. The trailing scalar `extra_h` (`+0x24`) — characterization (meaning OPEN)

The EXD triangle decoder copies `extra_h` verbatim into the runtime object's last float slot and does
nothing else with it during the decode path. Its consumer is elsewhere in the runtime (a height or
clearance query, not the parse routine). That a consumer exists is confirmed by its storage; the
specific query that reads it has not been located statically.

Aggregated over both confirmed samples:

- **map000 sample (590 records):** roughly 302 / 590 records (≈ 51 %) have `extra_h == 0.0`; the
  rest are nonzero. Nonzero values are continuous floats (not integers, not enum-shaped); in some
  records the nonzero value equals the triangle's own Y, in others it does not. `extra_h` is an
  **independent** per-triangle float, not a derived copy of any vertex component.
- **map001 sample (395 records):** `extra_h` is a constant `≈ 166.328` across the leading records,
  matching the flat surface Y of those near-horizontal triangles — consistent with a "reference
  height for this surface layer" interpretation.

**Working hypothesis (OPEN):** a **second / alternate-layer reference height** for multi-level walk
surfaces — e.g. the ground height beneath an elevated bridge or overpass triangle — with `0` meaning
"single layer / none." Alternatives still to disprove: a blend / cost weight, a water level, or a
ceiling-clearance value.

**Provisional canonical name:** `alt_layer_height`. The `extra_h` field tag in this spec is an
authoring alias; implementations should use `altLayerHeight` until the semantic is confirmed.

**OPEN QUESTION** (see §8 #1): the consuming runtime query has not been statically located. The
loader confirms the field is preserved; its meaning must be settled by the runtime consumer.

---

## 5. Decoder behavior and read algorithm

### EXD triangle decoder (static analysis — high confidence)

The EXD triangle decoder is the routine the `EXTRA_TERRAIN` `.map` block hands its opened DATAFILE to.
Confirmed behavior:

1. **Reads 4 bytes** from the open file → `record_count` (u32 LE).
2. **Allocates** an array of `record_count` in-memory triangle objects, each **72 bytes (0x48)**
   (a C++ array-of-objects with per-element construction; the array is prefixed by the stored count).
3. **Loops `record_count` times.** Each iteration reads **40 bytes (0x28)** from the file into a
   scratch buffer and calls a per-record expander that fills one 72-byte runtime object. The in-memory
   write cursor advances 72 bytes per record.

**Per-record expander** (role: decode one EXD on-disk triangle record into runtime form):

- Copies the three on-disk vertices (disk `+0x00`, `+0x0C`, `+0x18`) into the runtime object's
  vertex slots (three XYZ triples).
- Copies the disk `+0x24` scalar verbatim into the runtime object's last float slot — no further
  processing in the expander.
- Derives an **axis-aligned XZ bounding box** of the three vertices from their X and Z components
  (Y is ignored for the bbox): stores `{minX, minZ, maxX, maxZ}` in the runtime object's first four
  floats.
- Calls a "plane from 3 points" helper that: builds two edge vectors (`v0 − v1`, `v2 − v1`), takes
  their cross product, normalizes the result to a unit normal, and stores `d = −dot(normal, v1)`.
  The plane equation `{nx, ny, nz, d}` is stored in the runtime object.

### In-memory 72-byte runtime object layout (derived at load; not on disk)

| Slot | Type | Content |
|-----:|------|---------|
| floats 0–3 | f32 × 4 | XZ bbox `{minX, minZ, maxX, maxZ}` |
| floats 4–7 | f32 × 4 | plane `{nx, ny, nz, d}` |
| floats 8–16 | f32 × 9 | vertices `v0`, `v1`, `v2` (XYZ × 3) |
| float 17 | f32 | `extra_h` scalar (copied verbatim) |

This runtime layout is informational only — it is not stored on disk.

### Byte-driven read algorithm (for `Assets.Parsers`)

1. Open the file by its full VFS name `data/map<area>/dat/d<area>x<cellX>z<cellZ>.exd`.
2. Read 4 bytes → `record_count` (u32 LE).
3. Validate: `file_size == 4 + record_count * 40`; reject the file if the formula fails.
4. Read `record_count * 40` bytes contiguously.
5. For each 40-byte record, decode 10 LE `f32`: vertices `v0`, `v1`, `v2` (XYZ) and `extra_h`.
6. Apply no decompression, decryption, checksum, or transform — values are already final world space.
7. Optionally derive bbox and plane per triangle to match the runtime object (see expander above).

---

## 6. Coordinate conventions

- Stored coordinates are **absolute world space**, NOT cell-local. Every triangle's X and Z fall
  inside the cell's world bounds derived from the cell tag: `[(cellX − 10000) × 1024 … +1024]` for X
  and `[(cellZ − 10000) × 1024 … +1024]` for Z (cells are 1024 units; see `terrain.md`). This is
  confirmed geometrically for both samples (cell tag `z9990` → world Z near `−10 × 1024`; cell tag
  `z10000` → world Z near `0`).
- **Y is the up axis (height).** Triangles are predominantly near-horizontal (small Y-span relative to
  XZ extent), consistent with a walkable floor surface. A minority are steeper (slopes / ramps).
- The triangles form a **connected mesh** (fan/strip topology: consecutive records frequently share
  vertices), i.e. a continuous triangulated surface, not independent quads.
- **Godot import transform:** apply the **world-geometry** transform, not the mesh-local one. World
  geometry negates Z (`Helpers/WorldCoordinates.ToGodot`: `(x, y, z) → (x, y, −z)`); the `.exd` X
  coordinate is world-space and is **not** subject to the `.skn` mesh-local X-negate.
- **Winding / front-face:** the EXD triangle decoder computes its plane normal via
  `cross(v0 − v1, v2 − v1)`, normalized. The front-face convention follows from the file's vertex
  order via that cross-product. The client trusts the file order to produce its plane normal.
  The exact winding sense (which face is "up" for the walkable surface) should be confirmed before
  using the normals for one-sided collision or backface culling — see §8 #2.

---

## 7. Linkages

### What references `.exd`

The cell `.map` text descriptor is the **only** thing that names an `.exd`. Its `EXTRA_TERRAIN` block
carries a `DATAFILE <full vfs path>` sub-token; the master `.map` parser opens that path and routes it
to the EXD triangle decoder. Join key = the cell tuple `(mapId, cellX, cellZ)` baked into the path,
the same scheme as `.ted` / `.sod` / `.map` / `.mud` / `.bud`.

The same master `.map` parser routes:
- `TERRAIN` → `.ted` loader (`terrain.md`)
- `UP_TERRAIN` → up-terrain triangle decoder (`terrain_layers.md`)
- `EXTRA_TERRAIN` → EXD triangle decoder (this doc)
- `BUILDING` → `.bud` loader (`mesh.md`)
- `FX1`–`FX7` → fx-group decoders
- `SOLID` → `.sod` collision loader (`sod.md`)

`.exd` is the `EXTRA_TERRAIN` sibling in that single cell `.map` parser dispatch.

### What `.exd` references

Nothing. `.exd` is leaf geometry (world-space triangles); it does not index any other asset.

### Sidecar set per cell

| File | Format | Role |
|------|--------|------|
| `.ted` | `terrain.md` | Heightfield — ground height grid |
| `.sod` | `sod.md` | 2D-XZ wall-collision segments |
| `.map` | `terrain.md §3` | Cell text descriptor (triggers `.exd` load) |
| `.mud` | `mud.md` | Ambient-sound grid |
| `.gad` | — | (gadget/FX; not yet fully specced) |
| `.bud` | `mesh.md` | Building mesh |
| `.fx1`–`.fx7` | — | FX group blobs |
| `.exd` | *this doc* | EXTRA_TERRAIN auxiliary triangle mesh |
| `.up` | `terrain_layers.md §2` | UP_TERRAIN auxiliary triangle mesh |

### Shared coordinate space

The `.exd` triangles are directly comparable to `.sod` wall segments and `.ted`-derived ground height;
all three share the same absolute world XZ coordinate frame. The natural consumer is the
cell-streaming and ground-height / collision-or-navigation subsystem.

---

## 8. Open questions / verification debt

1. **Meaning of `extra_h` (`+0x24`)** — alternate-layer reference height, cost/blend weight, water
   level, or ceiling clearance? The decoder preserves it verbatim; the runtime consumer that reads it
   has not been statically located. Working hypothesis: alternate-layer / multi-level surface reference
   height with `0 = none`. Must be settled by locating and tracing the runtime query (live debugger,
   or the consuming code path).
2. **Winding / front-face sense** — the plane normal is computed via `cross(v0 − v1, v2 − v1)`; the
   sign depends on the vertex winding in the file. Confirm which normal orientation is "up" before
   trusting normals for one-sided collision or backface culling.
3. **Header truly a bare `u32` count (no hidden version / flags)** — consistent across both samples.
   Re-confirm if a file with a semantically ambiguous (e.g. very small) count is found.

---

## 9. Cross-references

| Format | File | Relationship |
|--------|------|--------------|
| `terrain.md` | `data/map<area>/dat/*.ted` etc. | Cell coordinate model, cell origin biasing, separate ground-height source; the same `.map` parser dispatches both TERRAIN and EXTRA_TERRAIN |
| `sod.md` | `data/map<area>/dat/*.sod` | Per-cell 2D-XZ wall-collision sibling; shares world coordinate space |
| `terrain_layers.md` | UP_TERRAIN `.up` blobs | UP_TERRAIN sibling; also a 40-byte-per-record triangle mesh routed by the same `.map` parser |
| `mesh.md` | `data/map<area>/dat/*.bud` | `.bud` building mesh — another DATAFILE-routed sidecar |
| `mud.md` | `data/map<area>/dat/*.mud` | Ambient-sound grid sibling |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that holds `.exd` files |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`

---

## 10. Names flagged for names.yaml (orchestrator to record)

- Format: `exd` → "Cell EXTRA_TERRAIN Auxiliary Triangle Mesh"
- Structs: `ExdRecord` (40 bytes on disk): `v0x`, `v0y`, `v0z`, `v1x`, `v1y`, `v1z`, `v2x`, `v2y`,
  `v2z`, `altLayerHeight`; runtime `ExdTriangle` (72 bytes): `bboxMinX`, `bboxMinZ`, `bboxMaxX`,
  `bboxMaxZ`, `planeNx`, `planeNy`, `planeNz`, `planeD`, `v0` (xyz), `v1` (xyz), `v2` (xyz),
  `altLayerHeight`
- Constants: `EXD_RECORD_STRIDE = 40`, `EXD_RUNTIME_OBJECT_SIZE = 72`
