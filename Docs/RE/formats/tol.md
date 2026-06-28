# Format: `.tol`  (per-area authoring grid — tool-side fine-resolution boolean grid; NOT loaded by the shipped client)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed (optionally) by Assets.Parsers. Every offset an engineer cites must reference this file:
> `// spec: Docs/RE/formats/tol.md`

---

## Status

```
verification:   sample-verified   # 16-byte front header + width×height byte grid body, the
                                   #   16 + W×H size formula, the front-origin == region.bin
                                   #   trailing-origin cross-match, the 0/1-only cell value, and
                                   #   the 64×-finer resolution vs region.bin — all matched against
                                   #   real VFS samples (map009/013/100) by byte-exact arithmetic.
ida_reverified: 2026-06-27
ida_anchor:     f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence:       [static-ida (absence proof), vfs-sample]
conflicts:      CORRECTED (2026-06-24) — two prior doc errors overturned by byte evidence:
                  (1) cell values: the doc previously asserted region-record index 0..31 and
                      refuted a 0/1 mask reading; byte data show .tol cells are 0/1 only across
                      both the 2048² and 256² samples (a mask reading; the 0..31 range lives
                      in region<NNN>.bin, NOT in .tol). Semantic of 0 vs 1 remains unknown.
                  (2) relationship: the doc previously stated .tol and region<NNN>.bin encode
                      "the same logical content … same width×height byte grid"; the grids
                      differ in resolution by 64× per axis (.tol = 4 wu/cell; region.bin =
                      256 wu/cell) over the same world extent — they share only the origin
                      anchor. The "same grid, two header arrangements" framing is withdrawn.
# The shipped client contains NO reference to the .tol extension — proven absent by an exhaustive
# byte scan of every segment. The layout below is recovered from VFS sample bytes plus a structural
# cross-match to the sibling region<NNN>.bin (whose loader IS in the binary). See region_grid.md for
# the runtime counterpart and the full region/zone subsystem.
```

> **HEADLINE — the shipped client does NOT load `.tol`.** The legacy client references **no `.tol`
> path literal anywhere** (an exhaustive byte scan of `.text` / `.rdata` / `.data` / `.idata` / `.rsrc`
> for the `.tol` extension in ASCII, uppercase, and UTF-16LE returns **zero hits**). The map subsystem's
> complete per-area / per-cell file-path table enumerates every asset the engine opens
> (`map<NNN>.bin`, `regiontable<NNN>.bin`, `region<NNN>.bin`, `npc<NNN>.arr`, the per-cell
> `.ted`/`.sod`, the cell-list `d<NNN>.lst`, sky/effect/sound paths, and the `tool/…` editor
> variants) — and it contains **no `.tol`** entry and **no generic `%s.tol` builder**. `.tol` is therefore
> an **authoring / tool-side artifact** that rides inside the VFS but has **no runtime read-site**. For the
> C#/Godot port this format is **OPTIONAL / informational** — the runtime spatial data the client actually
> uses is `region<NNN>.bin` (the coarse region/zone grid — see `region_grid.md`) for zone/region lookup and
> `.sod` (XZ wall segments — see `sod.md`) for wall collision, **not** `.tol`. Do **not** wire a `.tol`
> loader expecting the engine to consume one.

---

## Identification

- **Extension:** `.tol`
- **Logical role:** a single per-area 2D grid of one-byte cell values — a **fine-resolution boolean
  grid** (cells are `0` or `1` only) used by the area-build toolchain. It is an authoring / tool-side
  artifact for the same world extent as the runtime `region<NNN>.bin`, but at **64× the linear
  resolution** (4 world units per cell vs region.bin's 256 world units per cell). The two files share
  only their world-space origin anchor and the area id; they are not the same grid at different header
  arrangements. See "Relationship to `region<NNN>.bin`" below.
- **File path:** `data/map<NNN>/<NNN>.tol` (`<NNN>` is the area / map number). The file rides **inside
  `data/data.vfs`** for the maps that carry one; the extension was preserved by the VFS index, **not** by
  any path builder in the binary.
- **Magic / signature:** none — no magic value, no version field.
- **Endianness:** little-endian throughout (x86 client). All multi-byte fields decode sanely as LE.
- **Compression / encryption:** none observed — the body is a raw byte raster.
- **Cell type:** single unsigned byte (`u8`), values observed: `0` and `1` only (boolean).

> **No client loader exists.** Because the shipped client never opens a `.tol`, there is **no
> client-side read path** to describe — the layout below is recovered from VFS sample bytes plus a
> structural cross-match to the sibling `region<NNN>.bin`, whose loader IS in the binary. Any read
> algorithm here is for a **port that chooses** to consume `.tol`, never a transcription of client code.

---

## On-disk layout

A `.tol` file is a **16-byte front header** (four 32-bit integers) followed by a `width × height` raster
body of one byte per cell. Total size = `16 + width × height`.

```
+0x00            originX (i32, signed)
+0x04            originZ (i32, signed)
+0x08            width   (u32)
+0x0C            height  (u32)
+0x10            cellGrid : width × height bytes, row-major
total size     = 16 + (width × height)
```

### Header (16 bytes)

| Offset | Size | Type | Field     | Notes                                                                 | Confidence |
|-------:|-----:|------|-----------|-----------------------------------------------------------------------|------------|
| +0x00  | 4    | i32  | `originX` | World-space X origin (signed). Cross-matched byte-for-byte to the same map's `region<NNN>.bin` trailing `originX` (3 areas). | CONFIRMED (cross-match) |
| +0x04  | 4    | i32  | `originZ` | World-space Z origin (signed). Cross-matched byte-for-byte to the same map's `region<NNN>.bin` trailing `originZ` (3 areas). | CONFIRMED (cross-match) |
| +0x08  | 4    | u32  | `width`   | Grid columns (cells along X). Per-area — do not assume a fixed dimension. | HIGH (sample) |
| +0x0C  | 4    | u32  | `height`  | Grid rows (cells along Z). Per-area; equals `width` in both samples (square grid, not guaranteed). | HIGH (sample) |

- The four header integers **lead** the file; the grid body follows immediately at `+0x10`. This is the
  opposite field-placement convention from `region<NNN>.bin`, where `width`/`height` lead but the origin
  pair trails the body — see "Relationship to `region<NNN>.bin`" below.
- **Origin signedness.** The origin fields are signed 32-bit integers; a map whose authored extent
  begins at negative world coordinates is addressed correctly. The sampled origins are multiples of 256.
- The sampled origins are multiples of 256 world units, consistent with both `.tol` and region.bin being
  anchored on whole region-grid cells.

### Body — the cell grid (`width × height` bytes)

| Region  | Size           | Type      | Field      | Notes                                                                 | Confidence |
|---------|----------------|-----------|------------|-----------------------------------------------------------------------|------------|
| +0x10.. | width × height | u8[h][w]  | `cellGrid` | Row-major raster (row stride = `width`). One unsigned byte per cell.   | HIGH (sample) |

- **Indexing:** `value = cellGrid[row * width + col]`, where `col` runs along X and `row` runs along Z
  (row-major, Z-major).
- **Element stride:** 1 byte. The body is a flat raster bitmap, **not** a record array.
- **Cell value — 0/1 boolean (CORRECTED).** Byte evidence from both the 2048×2048 sample (map009/013)
  and the independent 256×256 sample (map100) shows `.tol` cells contain **only the values `0` and
  `1`**. This is a **boolean grid**, not a region-record index grid. The earlier doc claim that `.tol`
  cells equal the region-record index range `0..31` and that a mask reading was "REFUTED" is
  **overturned by the byte data** — the multi-valued `0..31` range lives in `region<NNN>.bin`, not
  in `.tol`. The **semantic meaning** of `0` vs `1` (walkable/blocked, in-bounds/out-of-bounds,
  authored/empty, or another authoring flag) is a **known unknown**: no client read-site exists to
  reveal it, and the two samples do not vary enough to resolve it. Do not hard-code a semantic
  interpretation.
- **No magic / version / compression / encryption** in the body — raw bytes are the grid.

### Size derivation

- `total = 16 + width × height` (1 byte per cell) — sample-verified across both sample sizes.
- World span along X = `width × 4` world units (`.tol` cell stride = 4 wu — see "Resolution" below);
  world span along Z = `height × 4`. This span equals the same map's `region<NNN>.bin` extent
  (`region_width × 256` wu), because `.tol` is 64× finer over the same world coverage.
- The body length **must** equal `width × height` and reconciles with the on-disk file size as
  `16 + width × height`. A `.tol` whose declared `width × height` does not match `fileSize − 16` is
  malformed.

### Observed samples

| File                  | File size | originX | originZ | width | height | Cell values |
|-----------------------|----------:|--------:|--------:|------:|-------:|-------------|
| `data/map009/009.tol` | 4,194,320 | 8192    | 57344   | 2048  | 2048   | 0 and 1 only |
| `data/map013/013.tol` | 4,194,320 | 8192    | 57344   | 2048  | 2048   | 0 and 1 only (byte-identical to map009) |
| `data/map100/100.tol` | 65,552    | 23552   | 55296   | 256   | 256    | 0 and 1 only |

Note: `map009/009.tol` and `map013/013.tol` are byte-identical (same content under two area numbers).
`map100/100.tol` provides the independent second data point.

---

## Resolution and relationship to `region<NNN>.bin`

### Cell strides — three distinct grids, three strides

Do not conflate the three grid resolutions present in the per-area data set:

| Grid                   | Cell stride   | Typical grid size | Doc |
|------------------------|:-------------:|:-----------------:|-----|
| `.ted` terrain         | 1024 wu/cell  | 65×65             | `terrain.md` |
| `region<NNN>.bin`      | 256 wu/cell   | 32×32 (typical)   | `region_grid.md` |
| `.tol`                 | 4 wu/cell     | 2048×2048 (typical) | this doc |

The `.tol` cell stride of **4 world units** is derived from the confirmed world-span equality:
`tol_width × 4 == region_width × 256` (verified on both samples: 2048×4 = 8192 = 32×256; 256×4 = 1024 = 4×256).

### Relationship to `region<NNN>.bin` — shared origin anchor only (CORRECTED)

`.tol` and `region<NNN>.bin` are **not** the same grid re-headered. They share the world-space origin
anchor and the area id; everything else differs:

| Aspect              | `.tol` (authoring)                          | `region<NNN>.bin` (runtime)                    |
|---------------------|---------------------------------------------|------------------------------------------------|
| Read by client?     | **No** — never opened by the engine         | **Yes** — the shipped client loads this        |
| Cell stride         | **4 world units** (fine resolution)         | **256 world units** (coarse resolution)        |
| Grid size (map009)  | 2048 × 2048                                 | 32 × 32                                        |
| Linear ratio        | 64× finer per axis over the same world span | —                                              |
| Cell semantics      | **0/1 boolean** (meaning unknown)           | **region-record INDEX 0..31**                  |
| Origin placement    | FRONT, leading a 16-byte header             | END, trailing the grid body                    |
| Field order         | `originX, originZ, width, height, grid`     | `width, height, grid, originX, originZ`        |
| Total size          | `16 + W×H`                                  | `16 + W×H` (same total for the header bytes; body size differs because W×H differs) |
| Origins match?      | front origins **equal** the same map's runtime trailing origins (CONFIRMED, 3 areas, byte-exact) | trailing origins are the source of truth |

The earlier "same grid, two header arrangements" framing in this doc is **withdrawn**. The shared-origin
confirmation (re-confirmed here) is correct; the body-identity claim is not.

---

## Read algorithm (for a port that opts in — NOT client code)

The shipped client has no `.tol` read path. A port that chooses to consume `.tol`:

1. Read `i32 originX`, `i32 originZ`, `u32 width`, `u32 height` (little-endian) from the 16-byte front
   header.
2. Validate `fileSize == 16 + width × height`; reject malformed files.
3. Read `width × height` bytes into a row-major (Z-major) byte grid.
4. To map a world position `(X, Z)` to a cell: subtract the signed origin, divide each axis by the
   **4-unit** `.tol` cell stride, compute the row-major index `(X − originX)/4 + (Z − originZ)/4 ×
   width`, bounds-check against `width × height`, and read the cell byte (`0` or `1`).

There is no decode, transform, checksum, or validation step — the raw bytes are the grid. Use the
**4-wu** stride for `.tol`; use the **256-wu** stride only for `region<NNN>.bin`; use the
**1024-wu** stride only for `.ted` terrain cells. Never mix them.

> For the **runtime** game a port should read zone/region data from `region<NNN>.bin` (the format the
> engine actually loads — see `region_grid.md`), not from `.tol`.

---

## Linkages

- **JOIN KEY — the area id `<NNN>` + the shared (originX, originZ) anchor.** A `<NNN>.tol` describes the
  same area, anchored at the same world origin, as that area's `region<NNN>.bin` and the rest of the
  `data/map<NNN>/…` set; the origins are cross-confirmed equal (byte-exact, 3 areas).
- **Referenced BY the shipped client — NOTHING.** No `.tol` path literal is referenced by the engine
  (proven absent). The only thing that "references" a `.tol` is the VFS index that stores it.
- **It references — nothing external.** `.tol` is self-contained: the four header integers plus a raw byte
  grid.
- **Producer / consumer — the area-build / map-editor toolchain.** `.tol` is most plausibly emitted by
  the same authoring toolchain that produces the `tool/region/…` and `tool/mob/…` editor variants seen in
  the map path table. (Producer/consumer identity is structural inference — no client read-site exists.)
- **Runtime equivalent the engine actually loads:** `region<NNN>.bin` (see `region_grid.md`), loaded once
  per active map area and queried by world position to answer "which region/zone is this point in?",
  driving spawn rules, gather points, sound zones, and movement / region-state / combat-mode validation.

---

## Coordinate convention (port note)

The grid is anchored in **absolute world XZ**, native-client space: X (east/west) and Z (north/south) are
the horizontal plane; Y (vertical) is not part of the grid. The Godot world-render path **negates Z**
(`(x, y, z) → (x, y, −z)` — see `terrain.md` and CLAUDE.md). A port that uses `.tol` (or the runtime
`region<NNN>.bin`) for spatial lookup must apply the same Z negation it renders terrain in, or the grid will
not line up with the rendered world.

---

## Known unknowns

The following remain unresolved and must not be guessed at during implementation:

1. **Semantic meaning of cell value `0` vs `1`.** The `.tol` body is a boolean grid (`0`/`1` only,
   confirmed across two independent area sizes). What `0` and `1` mean — walkable/blocked,
   in-bounds/out-of-bounds, authored/empty, or another authoring flag — is **unknown**: no client
   read-site exists and the two samples do not vary enough to resolve the polarity. Do not hard-code a
   semantic.
2. **Whether `width`/`height` are always equal (square grid).** Both samples are square, but this is not
   guaranteed by the format. Read both from the header.
3. **Whether the cell stride is always exactly 4 wu.** Derived consistently across both samples (4 wu =
   span / tol_width, where span = region_width × 256). Treat as HIGH-confidence but not formally proven
   by a third independent area with a different resolution.
4. **Whether the engine ever consumed `.tol` directly in an earlier build.** None found — no `.tol` path
   literal is referenced in `doida.exe`. A different (earlier/later) client build is unverified.

---

## Cross-references

| Format / file       | File                                | Relationship                                                                 |
|---------------------|-------------------------------------|------------------------------------------------------------------------------|
| `region_grid.md`    | `data/map<NNN>/region<NNN>.bin`     | The **runtime** counterpart — coarse 256-wu cell, region-record INDEX `0..31`; loaded by the shipped client. Shares only the world-space origin anchor with `.tol`. |
| `region_grid.md`    | `data/map<NNN>/regiontable<NNN>.bin`| The 32-slot × 48-byte region-properties table whose cell indices the runtime `region<NNN>.bin` grid selects into (not `.tol` — `.tol` cells are boolean, not region indices). |
| `sod.md`            | `data/map<area>/dat/*.sod`          | The runtime **wall-collision** data the client actually uses (XZ wall segments). |
| `terrain.md`        | `data/map<NNN>/*.ted` etc.          | Per-cell terrain (1024-wu cell, 65×65 grid) — the coarsest per-area grid. Do not conflate the three cell strides (1024 / 256 / 4 wu). |
| `npc_spawns.md`     | `data/map<NNN>/npc<NNN>.arr`        | Spawn placement in the same per-area set anchored at the shared (originX, originZ). |
| `pak.md`            | `data.inf` / `data/data.vfs`        | VFS container that holds the `.tol` files (which the engine never opens). |

### Subsystem specs

- `Docs/RE/specs/world_systems.md §16` — the region/zone gating subsystem (movement / combat / PvP gates,
  the server-authoritative active-region push, the per-cell sound grid) that consumes the runtime region
  grid the `.tol` is the authoring source of.

---

## Names flagged for names.yaml (orchestrator to record)

- Format: `tol` → "Per-area authoring boolean grid (tool-side, fine 4-wu resolution; NOT loaded by the shipped client)"
- Struct: `TolFile` (front-origin layout): `originX` (i32), `originZ` (i32), `width` (u32), `height` (u32),
  `cellGrid` (u8[height][width], row-major boolean 0/1 values)
- Constants: `TOL_HEADER_SIZE = 16`, `.tol` cell stride `= 4` world units
- Relation: `tol.originX == region<NNN>.bin.originX` and `tol.originZ == region<NNN>.bin.originZ`
  (shared per-area world anchor; confirmed byte-exact on 3 areas); `.tol` = fine 4-wu boolean grid,
  `region<NNN>.bin` = coarse 256-wu region-index grid (different resolution, different cell semantics)
