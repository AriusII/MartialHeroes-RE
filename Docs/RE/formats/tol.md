# Format: `.tol`  (per-area authoring grid — tool-side region/walk grid; NOT loaded by the shipped client)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed (optionally) by Assets.Parsers. Every offset an engineer cites must reference this file:
> `// spec: Docs/RE/formats/tol.md`

---

## Status

```
verification:   sample-verified   # 16-byte front header + width×height byte grid body, the
                                   #   16 + W×H size formula, and the front-origin == region.bin
                                   #   trailing-origin cross-match — all matched against real VFS samples
ida_reverified: 2026-06-21
ida_anchor:     263bd994
evidence:       [static-ida (absence proof), vfs-sample]
conflicts:      none-open
# The shipped client contains NO reference to the .tol extension — proven absent by an exhaustive
# byte scan of every segment. The layout below is recovered from VFS sample bytes plus a structural
# cross-match to the sibling region<NNN>.bin (whose loader IS in the binary). See region_grid.md for
# the runtime counterpart and the full region/zone subsystem.
```

> **⚠️ HEADLINE — the shipped client does NOT load `.tol`.** The legacy client references **no `.tol`
> path literal anywhere** (an exhaustive byte scan of `.text` / `.rdata` / `.data` / `.idata` / `.rsrc`
> for the `.tol` extension in ASCII, uppercase, and UTF-16LE returns **zero hits**). The map subsystem's
> complete per-area / per-cell file-path table enumerates every asset the engine opens
> (`map<NNN>.bin`, `regiontable<NNN>.bin`, `region<NNN>.bin`, `npc<NNN>.arr`, the per-cell
> `.ted`/`.ted.post`/`.sod`, the cell-list `d<NNN>.lst`, sky/effect/sound paths, and the `tool/…` editor
> variants) — and it contains **no `.tol`** entry and **no generic `%s.tol` builder**. `.tol` is therefore
> an **authoring / tool-side artifact** that rides inside the VFS but has **no runtime read-site**. For the
> C#/Godot port this format is **OPTIONAL / informational** — the runtime spatial data the client actually
> uses is `region<NNN>.bin` (the coarse region/zone grid — see `region_grid.md`) for zone/region lookup and
> `.sod` (XZ wall segments — see `sod.md`) for wall collision, **not** `.tol`. Do **not** wire a `.tol`
> loader expecting the engine to consume one.

---

## Identification

- **Extension:** `.tol`
- **Logical role:** a single per-area 2D grid of one-byte cell values — the **authoring / tool-side
  counterpart of the runtime `region<NNN>.bin`** region/zone grid. It is the fine-grained source grid the
  area-build toolchain emits; the runtime `region<NNN>.bin` is the repacked product of the same logical
  content (same four integers + a `width × height` byte grid, differing only in where the integers sit
  relative to the body — see "Relationship to `region<NNN>.bin`" below).
- **File path:** `data/map<NNN>/<NNN>.tol` (`<NNN>` is the area / map number). The file rides **inside
  `data/data.vfs`** for the maps that carry one; the extension was preserved by the VFS index, **not** by
  any path builder in the binary.
- **Magic / signature:** none — no magic value, no version field.
- **Endianness:** little-endian throughout (x86 client). All multi-byte fields decode sanely as LE.
- **Compression / encryption:** none observed — the body is a raw byte raster.
- **Cell type:** single unsigned byte (`u8`).

> **⚠️ No client loader exists.** Because the shipped client never opens a `.tol`, there is **no
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
| +0x00  | 4    | i32  | `originX` | World-space X origin (signed). Cross-matched to the same map's `region<NNN>.bin` trailing `originX`. | CONFIRMED (cross-match) |
| +0x04  | 4    | i32  | `originZ` | World-space Z origin (signed). Cross-matched to the same map's `region<NNN>.bin` trailing `originZ`. | CONFIRMED (cross-match) |
| +0x08  | 4    | u32  | `width`   | Grid columns (cells along X).                                         | HIGH (sample) |
| +0x0C  | 4    | u32  | `height`  | Grid rows (cells along Z).                                            | HIGH (sample) |

- The four header integers **lead** the file; the grid body follows immediately at `+0x10`. This **header
  arrangement is the only structural difference** from `region<NNN>.bin`, where the same four integers split
  around the body (`width`/`height` at the front, origins trailing the body) — see the relationship section.
- **Origin signedness.** The origin fields are signed 32-bit integers so an area whose authored extent
  begins at negative world coordinates is addressed correctly. (The runtime `region<NNN>.bin` indexer
  computes `(coord − origin)` with signed arithmetic; the `.tol` origins are the same values.)
- The sampled origins are multiples of 256 (the region-grid cell stride), consistent with the region grid
  being anchored on whole region cells.

### Body — the cell grid (`width × height` bytes)

| Region  | Size           | Type      | Field      | Notes                                                                 | Confidence |
|---------|----------------|-----------|------------|-----------------------------------------------------------------------|------------|
| +0x10.. | width × height | u8[h][w]  | `cellGrid` | Row-major raster (row stride = `width`). One unsigned byte per cell.   | HIGH (sample) |

- **Indexing:** `value = cellGrid[row * width + col]`, where `col` runs along X and `row` runs along Z
  (row-major, Z-major) — the same convention as the runtime region indexer
  (`index = (X − originX)/256 + (Z − originZ)/256 × width`).
- **Element stride:** 1 byte. The body is a flat raster bitmap, **not** a record array — there is no per-cell
  struct and no stride beyond the single byte.
- **Cell value = region-record INDEX (`0..31`).** The cell byte is a **region-record index** into the
  area's 32-slot region table (`regiontable<NNN>.bin`; `0` = none/no-region, `1..31` = a region record
  slot) — identical in meaning to the runtime `region<NNN>.bin` cell byte. An earlier reading of `.tol`
  alone observed only the values `0` and `1` and proposed a walkable/blocked **mask**; that mask reading is
  **REFUTED** — the runtime `region<NNN>.bin` corpus exercises the full `0..31` range across many maps,
  proving the cell byte is an INDEX, not a `0/1` mask. The `0`/`1`-only appearance was an artifact of the
  few `.tol` samples available, whose authored areas happened to use only region IDs `0` and `1`.
- **No magic / version / compression / encryption** in the body — raw bytes are the grid.

### Size derivation

- `total = 16 + width × height` (1 byte per cell) — sample-verified.
- World span along X = `width × 256` world units (the region grid uses a **256 world units per cell**
  stride); world span along Z = `height × 256`.
- The body length **must** equal `width × height` and reconciles with the on-disk file size as
  `16 + width × height`. A `.tol` whose declared `width × height` does not match `fileSize − 16` is
  malformed.

---

## Read algorithm (for a port that opts in — NOT client code)

The shipped client has no `.tol` read path. A port that chooses to consume `.tol`:

1. Read `i32 originX`, `i32 originZ`, `u32 width`, `u32 height` (little-endian) from the 16-byte front
   header.
2. Read `width × height` bytes into a row-major (Z-major) byte grid.
3. To map a world position `(X, Z)` to a cell: subtract the signed origin, divide each axis by the
   **256-unit** region cell stride, compute the row-major index `(X − originX)/256 + (Z − originZ)/256 ×
   width`, bounds-check against `width × height`, and read the cell byte as a **region-record index**.

There is no decode, transform, checksum, or validation step — the raw bytes are the grid. Note that for the
**runtime** game a port should read the equivalent values from `region<NNN>.bin` (the format the engine
actually loads), not from `.tol`.

> **⚠️ Do NOT confuse the region cell size with the terrain cell size.** The `.tol` / region grid cell is
> **256 world units**. This is **distinct** from the **terrain** cell, which is **1024 world units** on a
> 65×65 grid (see `terrain.md` and the coordinate conventions). Index `.tol` with the 256-unit stride,
> never the 1024-unit terrain stride.

---

## Relationship to `region<NNN>.bin` — same grid, two header arrangements

`.tol` (authoring) and `region<NNN>.bin` (runtime) encode the **same logical content**: four 32-bit
integers (origin X, origin Z, width, height) plus a `width × height` byte grid of region-record indices at
256 world units per cell. They differ **only in where the four integers sit relative to the grid body**.

| Aspect           | `.tol` (authoring)                                | `region<NNN>.bin` (runtime)                          |
|------------------|---------------------------------------------------|------------------------------------------------------|
| Read by client?  | **No** — never opened by the engine               | **Yes** — the shipped client loads this              |
| Origin placement | FRONT, leading a 16-byte header                   | END, trailing the grid body                          |
| Field order      | `originX, originZ, width, height, grid`            | `width, height, grid, originX, originZ`              |
| Total size       | `16 + W×H`                                         | `16 + W×H` (same total)                              |
| Grid size/meaning| identical (region-record index, row-major)        | identical                                            |
| Cell stride      | 256 world units                                   | 256 world units                                      |
| On disk          | inside `data.vfs` for the maps that carry one; never *opened* by the engine | loaded by name per active map area |
| Origins match?   | front origins **equal** the same map's runtime trailing origins (CONFIRMED cross-match) | trailing origins are the source of truth |

- The `.tol` front origins were cross-matched, byte-for-byte, against the same map's `region<NNN>.bin`
  trailing origins, which **confirms** that the leading two `.tol` integers are `originX` / `originZ`
  (resolving the earlier "header order proposed by analogy" doubt). A full grid-body byte-match was not
  performed; the size and origin agreement suffice for the layout claim.
- Treat them as **two formats**, not one: the tool-side source (`.tol`) and the runtime-repacked product
  (`region<NNN>.bin`). An engineer implementing the live game reads `region<NNN>.bin` (Layout A in
  `region_grid.md`), whose origins are parser-confirmed and signed.

---

## Linkages

- **JOIN KEY — the area id `<NNN>` + the shared (originX, originZ) anchor.** A `<NNN>.tol` describes the
  same area, anchored at the same world origin, as that area's `region<NNN>.bin` and the rest of the
  `data/map<NNN>/…` set; the origins are cross-confirmed equal.
- **Referenced BY the shipped client — NOTHING.** No `.tol` path literal is referenced by the engine
  (proven absent). The only thing that "references" a `.tol` is the VFS index that stores it.
- **It references — nothing external.** `.tol` is self-contained: the four header integers plus a raw byte
  grid. The cell bytes are region-record **indices** that (for the runtime equivalent) index
  `regiontable<NNN>.bin`.
- **Producer / consumer — the area-build / map-editor toolchain.** `.tol` is most plausibly emitted and
  consumed by the same authoring toolchain that produces the `tool/region/…` and `tool/mob/…` editor
  variants seen in the map path table; the runtime `region<NNN>.bin` is the repacked product the shipped
  client loads. (Producer/consumer identity is structural inference, not a binary read — no client read-site
  exists.)
- **Runtime equivalent the engine actually loads:** `region<NNN>.bin` (see `region_grid.md`), loaded once
  per active map area and queried by world position to answer "which region/zone is this point in?", driving
  spawn rules, gather points, sound zones, and movement / region-state / combat-mode validation.

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

1. **Cell-value polarity for region IDs `0` vs non-zero.** The byte is a region-record **index** (refuting
   the earlier walkable/blocked-mask reading); the per-region semantics (safe / PvP / closed) come from the
   indexed `regiontable<NNN>.bin` record's `zoneType`, not from the `.tol` byte itself. The mapping of
   `zoneType` to behaviour is documented in `region_grid.md`.
2. **Whether `width`/`height` are constant across all areas.** Sampled `.tol` files include both small
   (256×256) and large (2048×2048) grids — `width`/`height` are **per-area**, read from the header, not a
   global constant. Do not assume any fixed dimension.
3. **Endianness.** Assumed little-endian (x86 MSVC client); all fields decode sanely as LE. Not
   independently confirmed by a big-endian counter-sample (none exists for this platform).
4. **Full grid-body byte-match `.tol` ↔ `region<NNN>.bin`.** RESOLVED for the **origin** fields (front
   origins equal the runtime trailing origins, cross-matched); a complete body-vs-body byte comparison was
   not performed. The size and origin agreement suffice for the layout claim.
5. **Whether the engine ever consumes `.tol` directly.** None found — no `.tol` path literal is referenced
   by the loader. `.tol` is the tool/source artifact later repacked into the runtime `region<NNN>.bin`.

---

## Cross-references

| Format / file       | File                                | Relationship                                                                 |
|---------------------|-------------------------------------|------------------------------------------------------------------------------|
| `region_grid.md`    | `data/map<NNN>/region<NNN>.bin`     | The **runtime** counterpart — same logical grid, origins trailing the body; this is the format the shipped client actually loads. Also documents `regiontable<NNN>.bin` (the 32×48 region record table the cell byte indexes), the `zoneType` enum, and the combat-mode resolution rule. |
| `region_grid.md`    | `data/map<NNN>/regiontable<NNN>.bin`| The 32-slot × 48-byte region-properties table the `.tol` cell byte (a region index `0..31`) selects into. |
| `sod.md`            | `data/map<area>/dat/*.sod`          | The runtime **wall-collision** data the client actually uses (XZ wall segments) — distinct from any walkability reading of `.tol`. |
| `terrain.md`        | `data/map<NNN>/*.ted` etc.          | Per-cell terrain; the region/`.tol` grid (256-unit cell) is the **coarser** map-wide partition — do not conflate it with the 1024-unit / 65×65 terrain cell grid. |
| `npc_spawns.md`     | `data/map<NNN>/npc<NNN>.arr`        | Spawn placement in the same per-area set anchored at the shared (originX, originZ). |
| `pak.md`            | `data.inf` / `data/data.vfs`        | VFS container that holds the `.tol` files (which the engine never opens). |

### Subsystem specs

- `Docs/RE/specs/world_systems.md §16` — the region/zone gating subsystem (movement / combat / PvP gates,
  the server-authoritative active-region push, the per-cell sound grid) that consumes the runtime region
  grid the `.tol` is the authoring source of.

---

## Names flagged for names.yaml (orchestrator to record)

- Format: `tol` → "Per-area authoring region/walk grid (tool-side; NOT loaded by the shipped client)"
- Struct: `TolFile` (front-origin layout): `originX` (i32), `originZ` (i32), `width` (u32), `height` (u32),
  `cellGrid` (u8[height][width], row-major region-record indices)
- Constants: `TOL_HEADER_SIZE = 16`, region cell stride `= 256` world units (shared with `region<NNN>.bin`)
- Relation: `tol.origin == region<NNN>.bin.origin` (shared per-area anchor); `.tol` = authoring grid,
  `region<NNN>.bin` = runtime grid (same content, different header arrangement)
