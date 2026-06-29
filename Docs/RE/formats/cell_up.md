# Format: `.up`  (per-cell upper-terrain triangle surface — "UP_TERRAIN"; `.exd` shares this format)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by `Assets.Parsers`. Every offset an engineer cites must reference this file.
> C# parsers MUST cite `// spec: Docs/RE/formats/cell_up.md` on every magic constant / offset.

<!--
verification: parser + sample — full on-disk population (222 .up, 1384 .exd, 1606 files total, 0
              size mismatches) confirms the file-size formula 4 + 40×count. The triangle-decoder
              read-path (count → allocate → 40-byte per-record read → 40→72 expansion) is
              confirmed by static analysis of the cell-loader. The per-triangle attribute at +0x24
              is parser-confirmed as a single f32 copied verbatim into the runtime triangle and NOT
              consumed by the AABB or plane math; its semantic meaning remains UNVERIFIED.
ida_anchor: 263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee
evidence: [static-ida, vfs-sample]
conflicts: refines the +0x24 / "byte 36" field reading in formats/terrain.md §9.2 — that field is an
           independent per-triangle attribute, NOT a "surface Y-level": the earlier "10th float == Y"
           reading was a flat-cell coincidence (an elevated cell has varying per-vertex Y while +0x24
           is 0.0). Otherwise consistent with terrain.md §9 (40-byte triangle record, world-space).
source-format: derived from Docs/RE/_dirty/formats/cell_up.raw.md (dirty-room RE of doida.exe).
-->

---

## Re-verification banner (2026-06-27 — CYCLE 14 re-anchor, build f61f66a9)

| Attribute        | Value |
|------------------|-------|
| `verification`   | CYCLE 14 re-anchor (f61f66a9): 1 fact re-confirmed SAME, 0 corrected. Covered fact: `.up` format is `u32 triangleCount` + `triangleCount × 40-byte` triangle records (3 × vec3 + trailing f32); decoder allocates 72-byte runtime triangles and expands 40→72 (XZ-AABB + vertices + plane + scalar); file-size formula `4 + 40×count`; the `.exd` decoder is byte-identical to the `.up` decoder (same shared expander, same element ctors). Build-stable under the uniform +0x80 relocation. |
| `deep-cartography deepening` | 2026-06-29 (static-only, anchor f61f66a9) — UP layer (cell+20436) confirmed: no static read site other than the two parsers (build) and ctor/dtor (construct/destroy); no query/render site references the UP offset; built and freed but no located runtime consumer. `attr` at +0x24 (runtime +68) also unread by the located EXD ground consumer. Open item #3 tightened. |
| `ida_reverified` | `2026-06-27` |
| `ida_anchor`     | `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` |
| `evidence`       | `[static-ida]` — triangle decoder and shared 40→72 expander re-confirmed at relocated addresses under build f61f66a9. |
| `conflicts`      | None. |

---

## Re-verification banner (CYCLE 11 — full-population census + parser confirmation)

| Attribute        | Value |
|------------------|-------|
| `verification`   | `parser + sample` — full on-disk population confirmed; decoder read-path confirmed by static analysis |
| `ida_reverified` | 2026-06-24 |
| `ida_anchor`     | `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` |
| `evidence`       | `[static-ida, vfs-sample]` — witness 1 = static trace of the cell-loader triangle decoder (count read, allocation with overflow guard, 40-byte per-record read, 40→72 converter, plane builder); witness 2 = full on-disk population scan |
| `conflicts`      | None. This pass adds confirmations only; no prior value or claim was contradicted. |

**Census (CYCLE 11, witness 2 — full `data.vfs` population):** **222 `.up`** files + **1384 `.exd`**
files = **1606 files total**. The formula `fileSize = 4 + 40 × triangleCount` held for 100 % of both
populations (0 size mismatches). The `.up` and `.exd` decoders are byte-identical functions in the
client (same instructions; distinction is only the destination slot on the cell object) — CONFIRMED.

**Parser facts confirmed this pass (witness 1):** (a) single `u32` header word; (b) 40-byte per-record
read, stride literal in the decoder; (c) runtime allocation of `72 × count` bytes with a saturating
overflow guard — the count itself is not otherwise bounds-checked; (d) 40→72 converter copies the three
disk vertices verbatim, copies `+0x24` f32 verbatim as the tail `attr`, computes XZ AABB (min/max of
v0/v1/v2 over X and Z only; Y not bounded), and builds a normalized plane
(`edge1 = v0 − v1`, `edge2 = v2 − v1`, `normal = normalize(cross(edge1, edge2))`,
`d = −dot(normal, v1)`); normalize also serves as the degenerate-triangle guard.

---

> **status: parser + sample** — the header (`u32 triangleCount`), the 40-byte triangle record stride,
> the file-size formula (`4 + 40 × triangleCount`), the world-space coordinates, and the 40→72 runtime
> expansion (AABB + plane) are confirmed by both the static decoder trace and the full on-disk population.
> The format and decoder are **byte-identical** to the `.exd` sibling ("EXTRA_TERRAIN"); everything below
> applies verbatim to both.

---

## 1. Identification

- **Extension:** `.up` — the cell's **upper-terrain / overlay** triangle surface ("UP_TERRAIN").
  The `.exd` sibling ("EXTRA_TERRAIN") uses the **same on-disk format and the same decoder**.
- **Found in:** `.pak` / VFS, logical path pattern `data/map<area>/dat/d<area>x<cellX>z<cellZ>.up`
  (and `…​.exd`) — shares the cell base path with the cell's `.ted` / `.map` / `.mud` / `.gad` / `.bud`
  / `.sod`, differing only by extension. One `.up` (and at most one `.exd`) per terrain cell.
- **Role:** a per-cell supplementary **up-facing walkable / ground-height triangle surface** stored as
  a flat triangle soup in **world coordinates**. It supplements the single-valued `.ted` height grid
  with geometry a heightfield cannot express (overhangs, bridges, sloped platforms), and is expanded
  at load into a runtime triangle carrying a 2D-XZ bounding box and a plane, for a fast "what is the
  ground Y at this XZ?" lookup.
- **Magic / signature:** **none.** Headerless — no file-level magic, no version field, no checksum, no
  compression, no encryption. — confidence: CONFIRMED
- **Endianness:** little-endian throughout (x86 client; raw IEEE-754 f32 and `u32`). — confidence: CONFIRMED
- **Not extension-driven.** The strings `.up`, `%s.up`, `.exd` (and `.bud` / `.sod`) do **not** appear
  anywhere in the client binary. These sidecar filenames are read as **data tokens** from the cell
  `.map` descriptor text (see Linkages), so a string-only hunt for the format fails — the loader is
  keyword-driven, not extension-driven. — confidence: CONFIRMED

---

## 2. On-disk layout

Little-endian. No magic, no version. **File size = `4 + 40 × triangleCount`** — verified across the
full `.up` population on disk and across the `.exd` sibling.

### 2.1 Header

| Offset | Size | Type | Field | Notes |
|-------:|-----:|------|-------|-------|
| 0x00 | 4 | u32 | `triangleCount` | Number of triangle records that follow. The only header word. |

### 2.2 Triangle record — stride **40 bytes (0x28)**, repeats `triangleCount` times

| Offset (in record) | Size | Type | Field | Notes | Confidence |
|-------------------:|-----:|------|-------|-------|------------|
| +0x00 | 12 | f32 × 3 | `v0` = (x, y, z) | World-space vertex 0. | parser + sample |
| +0x0C | 12 | f32 × 3 | `v1` = (x, y, z) | World-space vertex 1. | parser + sample |
| +0x18 | 12 | f32 × 3 | `v2` = (x, y, z) | World-space vertex 2. | parser + sample |
| +0x24 | 4 | f32 | `attr` | Per-triangle attribute. Copied verbatim into the runtime triangle; **not** used by the plane/AABB math. Mostly `0.0`; sometimes a small float repeated across runs of consecutive triangles (looks like a per-region flag/value). **Meaning UNVERIFIED** — see Known unknowns. | sample (meaning unverified) |

- **Record stride:** 40 bytes (`0x28`), read as a literal in the decoder. — CONFIRMED
- **Count source:** the leading `u32` header word. — CONFIRMED
- **Total size:** `4 + 40 × triangleCount`. — CONFIRMED
- **No transform:** no decryption, no compression, no checksum — raw little-endian floats copied
  straight through. — CONFIRMED

> **Coordinates are absolute WORLD space**, not cell-local. In a witnessed cell the three vertices lie
> inside the cell's world bounds `X ∈ [(cellX−10000)·1024 .. +1024]`, `Z ∈ [(cellZ−10000)·1024 .. +1024]`
> — the same cell origin model as `terrain.md` / `sod.md`. In a flat cell all per-vertex Y are equal; in
> an elevated cell the per-vertex Y varies over a wide range, confirming `.up` is a true 3D surface, not
> a flat plane. — confidence: sample-verified

> **`attr` is independent of Y (correction to `terrain.md` §9.2).** Because flat cells have a constant
> Y and `attr` is often `0.0`, an earlier reading treated the `+0x24` / "byte 36" field as a surface
> Y-level. An elevated sample disproves this: there the per-vertex Y values vary while `attr` is `0.0`.
> The `+0x24` field is therefore an **independent per-triangle attribute**, not a height. This spec is
> the authoritative reading for that field.

---

## 3. Read algorithm (raw bytes → runtime)

1. **Read `u32 triangleCount`** (4 bytes).
2. **Allocate the runtime triangle array** — room for `triangleCount` 72-byte runtime triangles (plus a
   leading length word). The allocation size (`72 × triangleCount`) is overflow-guarded (saturates on
   overflow); the count itself is **not** otherwise bounds-checked.
3. **For each triangle `i` in `0 .. triangleCount−1`:**
   - Read the 40-byte on-disk record (3 vertices + `attr`).
   - Expand it into the i-th 72-byte runtime triangle via the 40→72 transform (§3.1).
4. Done.

There is no magic, no version field, and no checksum to validate; the only data validation is the
degenerate-triangle guard inside the plane builder (§3.1).

### 3.1 The 40→72 runtime expansion (math, not code)

Each 40-byte disk record expands into a **72-byte runtime triangle** = `float[18]`, laid out so a query
can do an XZ point-locate followed by a plane-evaluate for the ground Y:

| Runtime offset | Size | Field | Source |
|---------------:|-----:|-------|--------|
| +0x00 | 16 | XZ AABB = `minX, minZ, maxX, maxZ` | min/max of v0/v1/v2 over X and Z only |
| +0x10 | 12 | `v0` (x, y, z) | disk +0x00 (verbatim) |
| +0x1C | 12 | `v1` (x, y, z) | disk +0x0C (verbatim) |
| +0x28 | 12 | `v2` (x, y, z) | disk +0x18 (verbatim) |
| +0x34 | 12 | plane normal (unit) | computed (cross of two edges, normalized) |
| +0x40 | 4 | plane `d` | `−dot(normal, v1)` |
| +0x44 | 4 | `attr` | disk +0x24 (verbatim) |

- **XZ AABB:** `minX = min(v0.x, v1.x, v2.x)`, `maxX = max(...)`, `minZ = min(v0.z, v1.z, v2.z)`,
  `maxZ = max(...)`. **X and Z only** — Y is not bounded (consistent with an XZ point-locate then a
  plane-eval for Y).
- **Plane:** `edge1 = v0 − v1`, `edge2 = v2 − v1`, `normal = normalize(cross(edge1, edge2))`;
  `d = −dot(normal, v1)`. This is the standard plane `normal · p + d = 0`. The normalize step is the
  degenerate-triangle guard: a zero-length (degenerate) normal aborts the build for that triangle.

---

## 4. Linkages

### 4.1 Referenced BY — the cell `.map` descriptor (the JOIN KEY)

The per-cell **`.map` descriptor** (CP949 keyword + `{ }` block text) names the `.up` file. Its
top-level section keywords are `TERRAIN`, `EXTRA_TERRAIN`, `UP_TERRAIN`, `BUILDING`, `FX1…FX7`, `SOLID`;
inner keywords include `DATAFILE`, `TEXTURES`, dimension/grid keywords, and the braces.

- The **`UP_TERRAIN` block** holds a `DATAFILE <path>` line whose `<path>` is the `.up` file; the
  parser opens that path and invokes the triangle decoder. The **`EXTRA_TERRAIN` block** does the same
  for the `.exd` file through the identical decoder.
- **JOIN KEY = the `DATAFILE` path string inside the `UP_TERRAIN` block** (and the `EXTRA_TERRAIN`
  block for `.exd`) of the cell's `.map`. `.up` is therefore paired with its `.ted` / `.map` / `.bud` /
  `.sod` / `.exd` siblings as one cell descriptor's sub-files.
- `DATAFILE` always names a **base** extension (`… .up`, `… .exd`, never a `.pre` / `.post`); the VFS
  open router resolves the literal path with no extension rewriting. — CONFIRMED
- There are **two byte-identical `.map` parsers** (a VFS-path parser and a loose-disk twin) with the
  same grammar and the same `UP_TERRAIN → triangle-decoder` wiring; a dispatcher chooses between them.

> **Caveat (witnessed):** a `.map` file may live under one map folder while its `DATAFILE` paths point
> at another (e.g. a file stored under `map<A>/dat/` whose `DATAFILE` lines reference `map<B>/dat/`).
> The `DATAFILE` path string is authoritative; the cell is keyed by the `x<cellX>z<cellZ>` coordinates
> in the base name.

### 4.2 Cell base-name builder (how the `.map` itself is located)

The cell streamer builds the cell base path `data/map<area>/dat/d<area>x<cellX>z<cellZ>` (area split
into three digits, then `x{cellX}z{cellZ}`), loads the three fixed companions `.mud` / `.gad` / `.map`,
and the `.map` parse then resolves `.up` / `.exd` / `.bud` / `.sod` by their `DATAFILE` strings. World
units: cell origin `((cellX − 10000)·1024, (cellZ − 10000)·1024)`, cell span 1024 (the same cell model
as `terrain.md` / `sod.md`).

### 4.3 What `.up` references

**Nothing external.** `.up` is a terminal leaf: pure geometry plus the per-triangle `attr`. Coordinates
are already world-space, so no external transform table is needed.

### 4.4 Runtime consumer / manager

- The decoded triangle array is stored on the cell/terrain object during the `.map` descriptor parse
  (the `.up` array and the `.exd` array occupy two distinct slots on that object). The parse runs inside
  the cell-load chain (cell-descriptor load ← cell-file streaming ← cell acquire/find-or-load).
- **Intended runtime use** (inferred from the runtime triangle = XZ-AABB + plane + `attr`): an **XZ
  point-locate then plane-evaluate** to obtain the supplementary ground Y on the upper-terrain surface —
  a height/ground query for overhang/bridge geometry beyond the `.ted` heightfield. STATIC BOUND
  TIGHTENED (2026-06-29): a static scan for the UP layer offset (cell+20436) found **no query or
  render site** — only the two `.map` parsers (build) and the cell ctor/dtor (construct/destroy)
  reference this offset. The EXD layer (cell+16332) IS queried by the ground raycast leaf; the UP
  layer is not. [debugger-confirm D2]: whether a live session ever dereferences the UP layer via a
  pointer cached elsewhere; if not, UP is load-and-hold-only dead geometry in the shipped client.

---

## 5. Coordinate convention (port note)

Geometry is in **absolute world XZ/Y**, native-client space. The Godot world-render path **negates Z**
(`(x, y, z) → (x, y, −z)` — see `terrain.md` and the coordinate conventions in CLAUDE.md). A faithful
port must consume `.up` geometry in the **same** convention it renders terrain and resolves `.sod`
collision (apply the identical Z negation consistently), or the upper-terrain surface will not line up
with the rendered world. The `.up` winding is `cross(v0 − v1, v2 − v1)`; whether the surface is intended
one-sided is unverified.

---

## 6. Known unknowns

1. **`attr` (disk +0x24) meaning.** `0.0` in most triangles; small floats repeated across runs of
   consecutive triangles in elevated cells → possibly a per-region / per-surface tag or a bias value.
   Settling it needs a consumer trace or more samples. — UNVERIFIED
2. **Surface sidedness / winding intent.** The plane normal follows `cross(v0 − v1, v2 − v1)`; whether
   the surface is meant to be one-sided is unverified.
3. **The per-frame ground-Y query for `.up`** — STATIC BOUND TIGHTENED (2026-06-29): no static
   read site referencing the UP layer offset (cell+20436) was found outside the parsers and
   ctor/dtor. The EXD layer IS the located supplementary ground surface (queried by
   `BuildBasisVectorsFromTwoPoints` after the `.ted` subtile test); UP is not. [debugger-confirm
   D2]: whether the UP layer is ever dereferenced at runtime via a cached pointer. If a live
   attach confirms no runtime read, UP is load-and-hold-only dead geometry in the shipped client.
4. **`.up` vs `.exd` semantic difference.** Both are supplementary triangle surfaces sharing the exact
   format and decoder ("UP_TERRAIN" vs "EXTRA_TERRAIN"); the precise gameplay/render distinction between
   them is not pinned.

---

## 7. Cross-references

| Format | File | Relationship |
|--------|------|--------------|
| `terrain.md` | `data/map<area>/dat/*` | Cell coordinate model, `.map` descriptor grammar, and the §9 `.up`/`.exd` summary this spec details (and corrects on the +0x24 field) |
| `sod.md` | `data/map<area>/dat/*.sod` | Sibling per-cell collision surface (2D-XZ walls); same cell base path and world-coordinate model |
| `authoring_sidecars.md` | `*.pre` / `*.post` | The authoring-sidecar family; confirms `.up`/`.exd` carry NO `.pre`/`.post` and `DATAFILE` names base extensions only |
| `pak.md` | `data.inf` / `data/data.vfs` | VFS container that delivers `.up` / `.exd` files |

- **Glossary:** see `Docs/RE/names.yaml`
- **Provenance:** see `Docs/RE/journal.md`

---

## 8. Names flagged for names.yaml (orchestrator to record)

- Format: `up` → "Cell Upper-Terrain Triangle Surface (UP_TERRAIN)"; `exd` → byte-identical
  "Cell Extra-Terrain Triangle Surface (EXTRA_TERRAIN)".
- Struct: on-disk `UpTriangleRecord` (40 bytes): `v0`, `v1`, `v2` (each f32×3), `attr` (f32).
  Runtime `UpTriangle` (72 bytes): XZ-AABB, `v0`/`v1`/`v2`, plane `normal`, plane `d`, `attr`.
- Constants: `UP_RECORD_STRIDE = 40`, `UP_RUNTIME_TRIANGLE_STRIDE = 72`.
