# Area Inventory — per-area data census of the reference VFS

> Clean-room neutral document. Promoted from dirty-room analyst notes under
> EU Software Directive 2009/24/EC Art. 6. No decompiler output, no binary addresses,
> no sample bytes. This file is a **data census** of the user-supplied reference VFS,
> not a binary format specification. It is the authoritative source for which assets
> are present (or absent) per area, and is the first reference an engineer should consult
> before writing area-load or streaming logic.

---

## Status block

| Attribute          | Value |
|--------------------|-------|
| `status`           | `SAMPLE-VERIFIED` — census performed by direct VFS file enumeration and byte-confirmed cell counts against `.lst` binary records |
| `sample_verified`  | `true` — file counts cross-checked against live VFS; `.lst` cell counts independently verified via the formula `cells = (file_size - 4) / 4` (see `terrain.md §1.2`) |
| `binary_analysed`  | none — this document requires no decompiler input; all observations are from VFS enumeration |
| `confidence`       | `SAMPLE-VERIFIED` = count confirmed directly from VFS. `PLAUSIBLE` = count or absence inferred by analogy without direct per-file inspection. `CODE-CONFIRMED` = presence/absence confirmed by client load-path logic documented in a related spec. |

---

## 1. Area index — registered areas in the reference VFS

The area manifest file (`data/map<NNN>/dat/d<NNN>.lst`) is the authoritative presence signal for
an area. An area is registered in the reference VFS if and only if its `.lst` file exists.

### 1.1 Area ID space and ranges

The reference VFS contains **63 registered areas**. Area IDs form three non-contiguous ranges;
no `.lst` files exist for IDs 48–99, 101–200, or 211–299.

| Range | Count | Character |
|-------|------:|-----------|
| 0 – 47 | 48 | Main world; contiguous; the open-world zones |
| 100 | 1 | Isolated training or tutorial zone |
| 201 – 210 | 10 | Instanced zones (instanced dungeon / aquatic flavours) |
| 300 | 1 | Special zone (arena or boss area; 16 cells) |

**SAMPLE-VERIFIED.** IDs 48–99, 101–200, 211–299 are confirmed absent.

### 1.2 Cell count methodology

For each area the authoritative cell count is derived from its `.lst` binary:

```
cell_count = (file_size_of_lst - 4) / 4
```

This formula is `CONFIRMED` (see `terrain.md §1.2`). The per-area file counts in §2 are
cross-checked against the VFS `.map` and `.ted` counts, which must equal the `.lst` cell
count for a well-formed area.

**Total registered cells across all 63 areas: approximately 2,505.**

---

## 2. Per-area file coverage table

### 2.1 Column key

| Column | Meaning |
|--------|---------|
| `LST` | Cell count from `.lst` manifest (authoritative) |
| `map` | `.map` scene descriptor files in `data/map<NNN>/dat/` |
| `ted` | `.ted` terrain geometry blobs |
| `sod` | `.sod` collision solid blobs |
| `bud` | `.bud` building mesh blobs |
| `mud` | `.mud` ambient-sound tile blobs |
| `exd` | `.exd` extended-detail blobs |
| `up` | `.up` upper-terrain / bridge blobs |

A dash in any cell means zero files of that type for the area.

All counts are SAMPLE-VERIFIED unless noted otherwise.

### 2.2 Full census table

| Area | LST | map | ted | sod | bud | mud | exd | up |
|-----:|----:|----:|----:|----:|----:|----:|----:|---:|
|    0 |   2 |   1 |   1 |   1 |   1 |   – |   1 |  – |
|    1 | 144 | 144 | 144 | 144 |  89 |  77 |  69 |  – |
|    2 |  51 |  52 |  52 |  52 |  38 |  36 |  32 |  – |
|    3 |  73 |  73 |  73 |  73 |  54 |  48 |  22 |  – |
|    4 |  53 |  53 |  53 |  53 |  53 |  27 |  44 |  – |
|    5 |  30 |  30 |  30 |  29 |  26 |  25 |  16 |  – |
|    6 |   1 |   1 |   1 |   1 |   1 |   – |   1 |  – |
|    7 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|    8 |  50 |  50 |  50 |  50 |  50 |  50 |  21 |  – |
|    9 |  64 |  64 |  64 |  64 |  64 |  64 |  20 |  – |
|   10 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|   11 |   9 |   9 |   9 |   9 |   6 |   6 |   6 |  6 |
|   12 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|   13 |  64 |  64 |  64 |  64 |  64 |  64 |  20 |  – |
|   14 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|   15 |  20 |  20 |  20 |  20 |  18 |  16 |  17 | 17 |
|   16 |  22 |  22 |  22 |  22 |  19 |  16 |  17 | 18 |
|   17 |  30 |  30 |  30 |  29 |  26 |  25 |  16 |  – |
|   18 |  30 |  30 |  30 |  29 |  26 |  25 |  16 |  – |
|   19 |  73 |  73 |  73 |  73 |  54 |   – |  22 |  – |
|   20 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   21 |  51 |  52 |  52 |  52 |  38 |  36 |  32 |  – |
|   22 |  22 |  22 |  22 |  22 |  19 |  16 |  17 | 18 |
|   23 |  20 |  20 |  20 |  20 |  18 |  16 |  17 | 17 |
|   24 |   9 |   9 |   9 |   9 |   6 |   – |   6 |  6 |
|   25 |  20 |  20 |  20 |  20 |  18 |  16 |  17 | 17 |
|   26 |  22 |  22 |  22 |  22 |  19 |  16 |  17 | 18 |
|   27 |  30 |  30 |  30 |  29 |  26 |  25 |  16 |  – |
|   28 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   29 |   6 |   6 |   6 |   6 |   6 |   – |   6 |  – |
|   30 |   6 |   6 |   6 |   6 |   6 |   – |   6 |  – |
|   31 |  20 |  20 |  20 |  20 |  18 |  16 |  17 | 17 |
|   32 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   33 |  30 |  30 |  30 |  29 |  26 |  25 |  16 |  – |
|   34 |  22 |  22 |  22 |  22 |  19 |  16 |  17 | 18 |
|   35 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|   36 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   37 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|   38 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   39 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   40 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   41 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   42 |  51 |  51 |  51 |  51 |  36 |   – |  28 |  – |
|   43 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|   44 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   45 | 101 | 101 | 101 | 101 | 101 | 101 |  61 |  – |
|   46 |  50 |  50 |  50 |  50 |  50 |   – |  21 |  – |
|   47 |  51 |  52 |  52 |  52 |  38 |  36 |  32 |  – |
|  100 |   1 |   1 |   1 |   1 |   1 |   1 |   1 |  – |
|  201 |   9 |   9 |   9 |   9 |   6 |   6 |   6 |  6 |
|  202 |   9 |   9 |   9 |   9 |   6 |   6 |   6 |  6 |
|  203 |  20 |  20 |  20 |  20 |  18 |  16 |  17 | 17 |
|  204 |  20 |  20 |  20 |  20 |  18 |  15 |  17 | 17 |
|  205 |  22 |  22 |  22 |  22 |  19 |  16 |  17 | 18 |
|  206 |   4 |   4 |   4 |   4 |   4 |   – |   4 |  3 |
|  207 |   4 |   4 |   4 |   4 |   4 |   – |   4 |  3 |
|  208 |   2 |   2 |   2 |   2 |   2 |   – |   2 |  – |
|  209 |   6 |   6 |   6 |   6 |   6 |   6 |   6 |  – |
|  210 |   6 |   6 |   6 |   6 |   6 |   6 |   6 |  – |
|  300 |  16 |  16 |  16 |  16 |  16 |   1 |  16 |  – |

Notes on reading this table:

- Where `map` equals `LST`, the cell set is complete for that blob type.
- A dash in the `mud` column means zero `.mud` files exist for the area (see §3.4 for the
  complete list and discussion).
- A dash in the `up` column means no multi-level upper-terrain geometry for that area (see §3.5).
- Area 0 LST says 2 but only 1 `.map`/`.ted`/`.sod`/`.bud` file exists — see anomaly §4.1.
- Areas 2, 21, 47 have one more `.map`/`.ted`/`.sod` than their LST count of 51 — see §4.2.

---

## 3. Missing and sparse file patterns

### 3.1 Cells where `.sod` is missing

In the large majority of areas the `.sod` count equals the `.map` count. Exactly five areas
have one fewer `.sod` file than `.map` files:

| Area | LST cells | sod files | Missing count |
|-----:|----------:|----------:|--------------:|
|    5 |        30 |        29 |             1 |
|   17 |        30 |        29 |             1 |
|   18 |        30 |        29 |             1 |
|   27 |        30 |        29 |             1 |
|   33 |        30 |        29 |             1 |

In each case exactly one cell that has a `.map` and `.ted` has no corresponding `.sod`. The
missing cell is most likely a sea or water-edge tile that carries no walkable solid geometry. The
specific cell key that is absent in each area has not been identified (see open question §6.7).
**SAMPLE-VERIFIED.**

### 3.2 Cells where `.bud` is absent (terrain-only cells)

Cells with no building or prop geometry have no `.bud` file; this is the normal case for
terrain-only edge cells. The deficit is largest in the areas with the most open outdoor terrain:

| Areas | LST cells | bud files | Deficit | Pattern |
|-------|----------:|----------:|--------:|---------|
| 1 | 144 | 89 | 55 | Large outdoor; many terrain-only cells |
| 2, 21, 47 | 51 each | 38 each | 13 each | Mid-size outdoor |
| 3, 19 | 73 each | 54 each | 19 each | Large outdoor |
| 11, 24, 201, 202 | 9 each | 6 each | 3 each | Small dungeon; 3 entry-corridor cells per area |
| 42 | 51 | 36 | 15 | Mid-size outdoor |

Absence of `.bud` is not an error. The cell-streaming path must treat a missing `.bud` as an
empty building section (see `terrain.md §8`). **SAMPLE-VERIFIED.**

### 3.3 `.exd` extended-detail file counts

The `.exd` count is consistently lower than the `.map` count across all areas; only a subset
of cells carry extended-detail geometry. The count varies between 61 (for large 101-cell areas)
and 1 (for single-cell areas). The ratio is not fixed. See `terrain.md §3.3` for the unresolved
question of which `.map` section keyword targets `.exd` blobs.

### 3.4 Areas with no `.mud` ambient-sound tiles

Exactly 20 areas carry zero `.mud` files:

```
0, 6, 19, 20, 24, 28, 29, 30, 32, 36, 38, 39, 40, 41, 42, 44, 46, 206, 207, 208
```

| Sub-group | Areas | Notes |
|-----------|-------|-------|
| Hub / single-cell | 0, 6 | Too small to warrant tile-level ambient sound |
| Large outdoor (50–73 cells) | 19, 20, 28, 32, 36, 38, 39, 40, 41, 44, 46 | Systematic gap; these areas were authored without `.mud` files and fall back to silence |
| Indoor/dungeon | 24, 29, 30 | Consistent with indoor areas that may use a different ambient mechanism |
| Small water-outdoor | 206, 207, 208 | 2–4 cells; no ambient tiles |

For areas that do carry `.mud` files, the count is always less than the `.map` count (e.g.
area 1: 77 mud files for 144 cells). The gap represents cells where no ambient sound was
authored. Parsers must treat absent `.mud` as silence, not as an error.
**SAMPLE-VERIFIED.**

Area 300 is a special case: 16 cells but only 1 `.mud` file (ambient sound for one cell only).
The identity of the single cell that carries a `.mud` has not been established.

Within areas that have `.mud` files, the `.mud` count is bounded above by the `.map` count.
No area has more `.mud` than `.map` files.

### 3.5 `.up` upper-terrain files — presence confirms multi-level geometry

Upper-terrain files are present in exactly **17 areas**:

```
11, 15, 16, 22, 23, 24, 25, 26, 31, 34, 201, 202, 203, 204, 205, 206, 207
```

Cross-referencing with `map_option` classification (see §5.1): all 17 are either
`water_enable = 1` (water-plane areas) or `indoor_flag = 1` (dungeon / instanced areas), or
both. This confirms that `.up` files provide walkable upper-floor and bridge geometry
exclusively in multi-level indoor and aquatic areas. No pure outdoor (sky-only) area has any
`.up` files. **SAMPLE-VERIFIED; classification is CODE-CONFIRMED via `environment_bins.md`.**

| Area class | `.up` present? | Implication |
|------------|:-------------:|-------------|
| Pure outdoor (sky-only, no water) | No | No multi-level floor geometry; skip `.up` load |
| Water-plane (`water_enable = 1`) | Yes | Water-surface platforms and bridges expected |
| Indoor / dungeon (`indoor_flag = 1`) | Yes | Upper floors and internal bridges expected |
| Water + indoor | Yes | Both aquatic and multi-level |

### 3.6 `.tol` walkability bitmaps — very sparse

Only three areas carry a `.tol` tile-obstacle bitmap file:

| Area | Grid size | File size | Notes |
|-----:|-----------|----------:|-------|
| 9 | 2048 × 2048 tiles | 4 MB + 16 B header | Full-resolution walkmap; standard outdoor |
| 13 | 2048 × 2048 tiles | 4 MB + 16 B header | Full-resolution walkmap; standard outdoor |
| 100 | 256 × 256 tiles | 64 KB + 16 B header | Tutorial / training zone |

All 60 other areas have no `.tol` file. **SAMPLE-VERIFIED.** See `misc_data.md §3` for the
`.tol` binary format. Pathfinding for areas without `.tol` must rely on `.sod` collision
geometry for movement blocking, or the server carries its own walkability data not present
in the client VFS. **PLAUSIBLE.**

---

## 4. Content anomalies

### 4.1 Area 0 — duplicate LST key

The `.lst` file for area 0 declares 2 cell entries, but both entries encode the same cell key
(`mapX = 10000, mapZ = 9990`). Only one `.map`/`.ted`/`.sod`/`.bud` file exists for that cell.
The second entry is a content-authoring artifact. The runtime skips any cell key not backed by
a `.map` file; the duplicate key is therefore harmless at runtime. A streaming implementation
must guard against processing the same cell key twice if it iterates the `.lst` linearly.
**SAMPLE-VERIFIED.**

### 4.2 Areas 2, 21, 47 — one extra `.map`/`.ted`/`.sod` cell

Each of these three areas reports 51 entries in its `.lst` but has 52 `.map`, `.ted`, and
`.sod` files. The extra file corresponds to a cell whose key does not appear in the `.lst`
manifest. The runtime would never stream that cell because it ignores cells absent from the
manifest. The extra cell is most likely a dead-content authoring artifact. The specific cell
key of the rogue file has not been identified (see open question §6.5).
**SAMPLE-VERIFIED (counts); cell identity UNVERIFIED.**

### 4.3 Zero-padded sky-bin filename duplicates for areas 15 and 16

For areas 15 and 16 the environment binary family has two naming variants in the VFS:

- Unpadded: `fog15.bin`, `light15.bin`, `map_option15.bin`, etc.
- Zero-padded: `fog015.bin`, `light015.bin`, `map_option015.bin`, etc.

The first 16 bytes of each pair are byte-identical (confirmed). The full-file comparison
across all affected bin types is not yet complete (see open question §6.6). Parsers should
prefer the unpadded form; the padded variants are duplicates. **SAMPLE-VERIFIED (first 16 bytes
only); full-file identity PLAUSIBLE.**

### 4.4 Area 207 `npc.arr` trailing anomaly

The file `npc207.arr` has a size of 240 bytes. At the standard 28-byte record stride this gives
`floor(240 / 28) = 8` complete records plus a 16-byte tail that does not fill a complete record.
The same pattern occurs in `npc000.arr` (area 0). Parsers applying `floor(file_size / 28)` as
the record count will silently ignore the tail, which is the correct behaviour per `npc_spawns.md`.
The origin of the 16-byte tail (authoring tool write error vs. a different record-size variant)
has not been determined. **SAMPLE-VERIFIED (file size); root cause UNVERIFIED.**

---

## 5. Spawn data coverage

### 5.1 `mob.arr` presence

55 of 63 areas have `mob<NNN>.arr` files; 8 areas have no mob spawn records:

```
Areas without mob.arr: 0 (hub), 6 (single-cell), 100 (tutorial),
                        201, 202, 203 (instanced, no-mob),
                        206, 207, 208 (water areas)
```

All mob records use a 28-byte stride; record count is `floor(file_size / 28)`.
See `npc_spawns.md` for the complete field layout.

Mob spawn density highlights (SAMPLE-VERIFIED):

| Area | Mob records | Cell count | Records per cell (approx.) |
|-----:|------------:|----------:|---------------------------:|
|   35 |      11,531 |       101 |                        114 |
|   14 |      11,500 |       101 |                        114 |
|   45 |      11,362 |       101 |                        112 |
|   43 |      11,360 |       101 |                        112 |
|   28 |      11,286 |        50 |                        226 |

Area 28 (50 cells, approximately 226 mob-record spawns per cell) is the densest outdoor zone
in the reference VFS.

### 5.2 `npc.arr` presence

61 of 63 areas have `npc<NNN>.arr` files; 2 areas have no NPC spawn records:

```
Areas without npc.arr: 11 (water + indoor dungeon), 14 (large outdoor mob zone)
```

Area 11 (`water_enable = 1, indoor_flag = 1`) has no vendor NPCs. Area 14 has 11,500 mob
records but no NPC vendors.

NPC-richest areas (SAMPLE-VERIFIED):

| Area | NPC records | Character |
|-----:|------------:|-----------|
|    1 |         137 | Main town hub (starting area) |
|    2 |          73 | Town |
|    4 |          64 | Town |

Areas 0 and 207 carry `npc.arr` files with a 16-byte tail past the last complete 28-byte record
(see anomaly §4.4); parsers must use `floor(file_size / 28)`.

---

## 6. Environment and sky binary family

### 6.1 Coverage by bin type

All 63 areas have the full set of mandatory sky/environment bins in `data/sky/dat/`. The
conditional (dome) bins are absent only for areas whose `map_option` flags suppress sky
rendering.

| Bin filename pattern | Present for all 63 areas | Notes |
|----------------------|:------------------------:|-------|
| `map_option%d.bin` | Yes | Always present; controls sky/water/indoor flags |
| `fog%d.bin` | Yes | Per-area fog definition |
| `light%d.bin` | Yes | Directional-light keyframes |
| `material%d.bin` | Yes | Sky material colour keyframes |
| `point_light%d.bin` | Yes | Per-area point-light array |
| `weather%d.bin` | Yes | Weather / precipitation parameters |
| `clouddome%d.bin` | **49** areas only | Absent for indoor / `indoor_flag = 1` areas |
| `stardome%d.bin` | **47** areas only | Absent for indoor and some outdoor-night-sky-disabled areas |
| `cloud_cycle%d.bin` | ~49 areas | Same pattern as `clouddome` |
| `wind%d.bin` | ~57 areas | Absent for a handful of areas; see §6.2 |

See `environment_bins.md` for the complete binary format of each file.
**SAMPLE-VERIFIED (presence/absence counts); field layouts in environment_bins.md.**

### 6.2 Areas missing cloud/star dome bins

Indoor and water+indoor areas suppress sky-dome rendering via `map_option.indoor_flag = 1`.
These areas have no `clouddome`, `stardome`, or `cloud_cycle` files; their absence is not an
error. Absence of dome files for any outdoor area would be anomalous.

Areas confirmed without dome bins (SAMPLE-VERIFIED):

```
clouddome absent: 11, 15, 16, 26, 29, 30, 34, 201, 202, 203, 204, 205, 209, 210
stardome absent : 11, 15, 16, 23, 25, 27, 28, 29, 30, 201, 202, 203, 204, 205, 209, 210
wind absent     : 23, 25, 28, 34 and portions of the 200-series
```

### 6.3 Area classification by `map_option` pattern

The `map_option%d.bin` file encodes a 10-field flag vector
`[water_enable, water_y, sky, star, cloud, lens_flare, sun, box, indoor_flag, reserved]`.
SAMPLE-VERIFIED classification by pattern:

| Pattern description | Areas |
|---|---|
| Standard outdoor (sky + star + cloud + lens + sun) | 0, 1, 2, 3, 6, 8, 9, 10, 12, 13, 14, 19, 21, 32, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47 |
| Outdoor, no lens flare | 4, 7 |
| Indoor dry dungeon (all sky flags off, indoor_flag = 1) | 5, 17, 20, 24, 27, 33 |
| Water Y = 300, indoor (water + indoor) | 11, 15, 16, 22, 23, 25, 26, 31, 34 |
| Water Y = 700, indoor | 29, 30, 209, 210 |
| Water Y = 1000, outdoor | 206, 207, 208 |
| Instanced 200-series (patterns vary) | 201, 202, 203, 204, 205 |
| Area 100 (training) | PLAUSIBLE standard outdoor; `map_option100.bin` not directly inspected |
| Area 300 (special) | Not classified; `map_option300.bin` not directly inspected |

This classification drives which renderer subsystems activate for an area. The
`indoor_flag = 1` value suppresses sky-dome loading (consistent with the absent dome bins
in §6.2). See `environment_bins.md §1` for the exact field offsets and flag semantics.

---

## 7. Sound table coverage

Five sound table files exist per area, all stored directly under `data/map<NNN>/` with a
fixed file size of 13,312 bytes each:

| Extension | Role | Count |
|-----------|------|------:|
| `.bgm` | Background music table | 60 (all areas with `.lst`) |
| `.bge` | Background ambient-loop table | 60 standard + 1 extra for area 0 (see §7.1) |
| `.run` | Running footstep table | 60 |
| `.wlk` | Walking footstep table | 60 |
| `.eff` | Effect / event sound table | 60 + 6 non-map files in `data/effect/obj/` |

Coverage is 100%: every area that has a `.lst` also has all five sound table files. Absent
sound tables would be a VFS corruption indicator. See `sound_tables.md` for the binary format.

### 7.1 Area 0 / area 1 dual `.bge`

Area 1 has two `.bge` files: `soundtable000.bge` (the area-1 table itself) and a
`soundtable001.bge` that carries shared ambient-loop sounds referenced across areas. This is
the only area in the VFS with a second `.bge` file. **SAMPLE-VERIFIED.**

---

## 8. Special-effect layer file coverage

FX layer blobs (`.fx1`–`.fx7`) are referenced per-cell from `.map` FX section `DATAFILE`
entries. Not every cell has all seven FX layers.

| Extension | Total files in VFS | Notes |
|-----------|-----------:|-------|
| `.fx1` | 226 | Common; spread across many areas |
| `.fx2` | 595 | Most widespread FX type |
| `.fx3` | 160 | Selective coverage |
| `.fx4` | 1 | Extremely rare; area 0 only |
| `.fx5` | 89 | Selective |
| `.fx6` | 6 | Very rare globally |
| `.fx7` | 2 | Rarest; only 2 files in entire VFS |

**SAMPLE-VERIFIED (total counts).** The cell-level breakdown (which specific cells carry each
FX layer) has not been catalogued. See `terrain.md §10` for the binary format of each FX type.

---

## 9. Fully complete areas

The 14 areas where every per-cell blob type (`.map`, `.ted`, `.sod`, `.bud`, `.mud`) all match
the LST cell count exactly, with no missing cells in any category:

```
7, 8, 9, 10, 12, 13, 14, 35, 37, 43, 45, 100, 209, 210
```

These are predominantly large (101-cell) outdoor zones, small instanced zones, and the
single-cell tutorial area (100). Area 100 is trivially complete. **SAMPLE-VERIFIED.**

---

## 10. Engineer guidance — area-load behaviour

This section translates the census findings into concrete decisions for streaming and
content-loading code. All entries reference a committed spec or this document by section.

| Decision | Guidance | Evidence |
|----------|----------|----------|
| Is an area registered? | Check for `data/map<NNN>/dat/d<NNN>.lst`. If absent, the area does not exist. | §1.1 |
| Enumerate cells for an area | Read the `.lst` binary; cell count = `(file_size - 4) / 4`. Deduplicate keys before use (area 0 has a duplicate). | §1.2, §4.1 |
| Missing `.bud` for a cell | Treat as terrain-only; do not raise an error. | §3.2, §9 |
| Missing `.mud` for a cell or area | Default to silence (music_group = 0, all ambient indices = 0). 20 areas carry no `.mud` at all. | §3.4 |
| Missing `.sod` for a cell | Treat as no collision geometry for that cell. 5 areas each have one cell without `.sod`. | §3.1 |
| Missing `.up` for an area | No multi-level floor geometry; skip the upper-terrain load path. All 46 pure outdoor areas are `.up`-free. | §3.5 |
| Missing dome bins | Not an error for indoor (`indoor_flag = 1`) or water+indoor areas; an outdoor area missing dome bins would be anomalous. | §6.2 |
| Missing `mob.arr` | Zero mob spawns; do not raise an error. 8 areas have no mob spawns. | §5.1 |
| Missing `npc.arr` | Zero NPC spawns; do not raise an error. Areas 11 and 14 have no NPC spawns. | §5.2 |
| Parsing `.arr` files with trailing bytes | Use `floor(file_size / 28)` as the record count; ignore any partial trailing record. Areas 0 and 207 have 16-byte tails. | §4.4, npc_spawns.md |
| Walkability | Only areas 9, 13, 100 have `.tol` bitmaps. All others must use `.sod` collision geometry. | §3.6 |
| Sound tables | All 5 sound table files are present for every area with a `.lst`. Load unconditionally at area switch. | §7 |
| Zero-padded sky-bin names (areas 15 and 16) | Prefer the unpadded form (e.g. `fog15.bin`). The padded variant (`fog015.bin`) is an identical duplicate. | §4.3 |

---

## 11. Open questions

1. **Area 0 duplicate LST key behaviour:** the runtime encounters two identical cell keys for
   area 0. Does the streaming system silently de-duplicate, attempt to load the same cell twice,
   or assert? The answer determines whether a streaming implementation needs an explicit
   de-duplication step. **UNVERIFIED.**

2. **`.mud` absent in large outdoor areas (19, 20, 28, 32, 36, 38–41, 44, 46):** these are
   full-featured 50-cell outdoor areas but carry no `.mud` files. Is this an intentional
   design decision (silent ambient is the correct experience) or an authoring gap? Affects the
   ambient-sound fallback behaviour specification. **UNVERIFIED.**

3. **Area 300 single `.mud` cell:** 16 cells but only 1 `.mud` file. Which cell has it and what
   is area 300 (arena, boss chamber)? The identity of the one sound-enabled cell and the
   classification of area 300 are both unconfirmed. **UNVERIFIED.**

4. **Area 207 `npc.arr` 16-byte tail:** the tail is the same length as the anomaly in area 0's
   `npc.arr`. Whether this is a common authoring-tool write error (writing a 16-byte sentinel
   terminator) or a distinct record-size variant in some areas is not yet determined.
   **UNVERIFIED.**

5. **Areas 2, 21, 47 — identity of the extra `.map` cell:** 52 files but only 51 LST entries.
   Which cell key is the extra file and is it live content or dead authoring debris? A targeted
   VFS path listing filtered to those areas would identify the rogue cell path. **UNVERIFIED.**

6. **Zero-padded sky-bin duplicate full-file comparison (areas 15 and 16):** the first 16 bytes
   of `fog15.bin` and `fog015.bin` are byte-identical. Whether the complete files are identical
   across every affected bin type and for both area 15 and area 16 has not been verified beyond
   the first 16 bytes. **PARTIALLY VERIFIED.**

7. **`.sod` missing cell identity (areas 5, 17, 18, 27, 33):** which specific cell key is
   missing `.sod` in each of these five areas? The cell is most likely a sea-edge or water tile
   with no solid geometry, but the key has not been looked up. **UNVERIFIED.**

8. **Area 100 `map_option` pattern:** the tutorial zone has a `.tol` walkability bitmap
   (unusual) but its `map_option100.bin` pattern was not directly inspected. Its indoor vs
   outdoor classification and its exact role (tutorial, lobby, transition zone) are inferred
   from context but not confirmed. **PLAUSIBLE.**

9. **Instanced area pairs (201/202, 209/210):** areas 201 and 202 have identical file counts;
   so do 209 and 210. Are these symmetric mirror instances (same geometry, different spawn
   tables) or independent areas that happen to have the same cell count? Cross-checking cell
   keys between paired areas would clarify. **UNVERIFIED.**

10. **FX4/FX6/FX7 cell locality:** `.fx4` (1 file), `.fx6` (6 files), and `.fx7` (2 files)
    appear in extremely few cells globally. Whether these are exclusive to area 0 or distributed
    across small clusters of cells in other areas is not confirmed. **UNVERIFIED.**

---

## 12. Cross-references

- **Per-cell binary formats:** `Docs/RE/formats/terrain.md` (`.lst`, `.map`, `.ted`, `.mud`,
  `.bud`, `.up`, `.exd`, `.sod`, `.fx1`–`.fx7`)
- **Environment / sky bins:** `Docs/RE/formats/environment_bins.md` (`map_option`, `fog`,
  `material`, `stardome`, `clouddome`, `cloud_cycle`, `weather`)
- **Directional light, point-light, wind bins:** `Docs/RE/formats/terrain_layers.md §6–8`
- **Spawn records:** `Docs/RE/formats/npc_spawns.md` (`.arr` format; 28-byte record stride)
- **Walkability bitmap:** `Docs/RE/formats/misc_data.md §3` (`.tol` format)
- **Sound table format:** `Docs/RE/formats/sound_tables.md`
- **VFS container:** `Docs/RE/formats/pak.md`
- **Glossary:** `Docs/RE/names.yaml`
- **Provenance:** `Docs/RE/journal.md`
