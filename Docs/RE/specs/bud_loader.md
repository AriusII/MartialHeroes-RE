---
verification: static-IDA-only (no debugger pass, no .bud sample byte-walk); IDB SHA f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
status: partial — load chain confirmed; debugger-confirm items open (see §9)
sample_verified: false
subsystems: [building_loader, mass_object_grid, wind_sway]
networked: false
deep_cartography_pass: 2026-06-29 — centroid formula corrected; family-2 amp direction confirmed DIVIDE; §6 function renamed BudObject_InitSwayBuffer; §5 Y-array claim updated; grid query path (MassObjectGrid_SampleCellMaxHeight) added as §5.2; §7 extended with weather/time gate and per-frame cull details; §8.4 updated; §9 open items updated.
---

# `.bud` Building Loader — Clean-Room Specification

> Clean-room neutral spec. Promoted from dirty-room analyst notes under EU Software Directive
> 2009/24/EC Art. 6. No decompiler pseudo-code, no binary virtual addresses, no decompiler
> identifiers. Every behaviour described below is expressed in the spec-author's own words and
> tables, based on mechanically neutral analyst annotations.
>
> **Scope.** This spec documents the full **runtime load chain** for `.bud` (building / mass-object)
> files: path resolution from the cell `.map` descriptor, VFS open and read, blob decode into a
> `BudObject` array, per-object AABB and cull-budget computation, registration into the 16×16 mass-
> object grid with texture-index remapping, and wind-sway buffer initialisation. The draw path is
> summarised only to confirm that no Direct3D buffer is created at load time.
>
> **Out of scope / cross-references.** The on-disk `.bud` byte format (header, vertex stride, index
> stride, field order) is owned by `Docs/RE/formats/mesh.md §.bud`. The 116-byte runtime
> `BudObject` layout is owned by `Docs/RE/structs/bud_object.md`. The VFS container format is
> owned by `Docs/RE/formats/pak.md`; the VFS runtime open/find/read machinery is owned by
> `Docs/RE/specs/resource_pipeline.md §1`. The global background-texture chain (`bgtexture.lst`
> slot → DDS file) is owned by `Docs/RE/formats/bgtexture_lst.md` and
> `Docs/RE/specs/resource_pipeline.md §3B`. The cell `.map` descriptor token grammar is owned by
> `Docs/RE/formats/terrain_scene.md`. The mass-object grid cell and TerrainManager slot layout is
> owned by `Docs/RE/structs/terrain-manager.md`.

---

## Status and verification banner

Evidence grades used throughout:

- **(CODE-CONFIRMED)** — behaviour or constant recovered directly from binary control-flow; safe to implement.
- **(PLAUSIBLE)** — single-source behavioural inference; implement but mark tunable.
- **(UNVERIFIED)** — hypothesis only; do **not** hard-code.
- **[debugger-confirm]** — loader-derived, not sample- or debugger-witnessed; implement as shown but treat as tunable until confirmed.

---

# 1. Path resolution — (CODE-CONFIRMED)

`.bud` files are not looked up by a numeric id table. Resolution is purely by path string:

1. The cell descriptor parser (`Map_ParseDescriptor`) reads the cell text `.map` file token-by-token.
   On the `BUILDING` section keyword it enters the building sub-block; on the keyword `DATAFILE` it
   reads the next whitespace-delimited token as the `.bud` VFS path string.
2. The parser constructs a `DiskFile` stream object and opens it via the VFS chokepoint
   (`DiskFile_OpenByValue`, mode `1` = read, in-memory-VFS variant) on that path string.
3. It tests the stream for readiness. On failure it logs an error and aborts the entire cell
   descriptor load — the building file is mandatory for a cell that declares a `BUILDING` block.
4. On success the open stream is passed to the blob loader (`Bud_LoadBuildingBlob`); after the
   loader returns the stream is closed and destroyed.

`.bud` is one of 12 section types dispatched by `Map_ParseDescriptor` on section keyword (alongside
`TERRAIN`, `EXTRA_TERRAIN`, `UP_TERRAIN`, `FX1`..`FX7`, and `SOLID`). All 12 open their respective
data files through the identical VFS chokepoint. A loose-disk variant of `Map_ParseDescriptor` exists
for non-VFS environments; the blob-decode path is shared. Cell descriptor entry and orchestration is
driven by `Map_LoadCellDescriptor`.

---

# 2. VFS open and read path — (CODE-CONFIRMED)

The VFS chokepoint (`DiskFile_OpenByValue`) selects one of three I/O backends based on the mount
state and mode flag, matching the general router documented in `specs/resource_pipeline.md §1`:

| VFS mounted | Mode bit 2 (`& 4`) | Backend chosen |
|---|---|---|
| Yes | Clear (mode `1`) | **In-memory VFS slice** — `Vfs_FindAndReadEntry` resolves the path and returns an entry descriptor; the `DiskFile` stream reads out of the in-memory blob. |
| Yes | Set | **Streamed pack** — opens the physical archive file, locates the entry offset via `Vfs_FindEntry`, positions the file pointer. (Not the `.bud` path; documented for completeness.) |
| No | Either | **Loose OS file** — plain file open on the path. |

The `.bud` open always uses mode `1` (bit 2 clear), so it follows the in-memory VFS path. The entry
lookup (`Vfs_FindAndReadEntry`) returns a blob slice; success requires the entry's data pointer to be
non-null.

Every field read in the blob decoder routes through a single read dispatcher (`DiskFile_ReadVirtual`).
The dispatcher invokes a positioning/readiness hook on the stream, then branches on the stream's
mode state. In binary in-memory VFS mode (`DiskFile_ReadBytesClampedToEof`) it copies `N` bytes from
the current position in the entry slice, clamping the copy count to the remaining entry size so it
never over-reads the entry boundary. Bytes are copied verbatim — binary mode, no text transformation.

---

# 3. Blob decode — `Bud_LoadBuildingBlob` — (CODE-CONFIRMED)

The blob loader reads the entire `.bud` into a heap-allocated `BudObject` array. Decode order:

1. **Stream readiness check.** Gate the entire parse on the stream's readiness query returning true.
2. **`object_count`** — read u32 (4 bytes) into the loader record.
3. **`BudObject` array allocation** (if `object_count > 0`):
   - Allocate `116 × object_count + 4` bytes.
   - Write `object_count` into the 4-byte count prefix; element 0 begins immediately after it.
   - Run the array constructor over all `object_count` elements (stride 116, default constructor).
   - Store the element-0 pointer into the loader record.
4. **Per-object loop** (`object_count` iterations, advancing 116 bytes per step):
   1. **`type`** — read 1 byte into `BudObject +52`.
   2. **`tex_id`** — read u32 (4 bytes) into `BudObject +0`. At this stage the value is the
      `.bud`-local 1-based texture index; it is remapped to a global `bgtexture.lst` slot in §5.
   3. **`vertex_count`** — read u32 (4 bytes) into `BudObject +8`.
   4. **Vertex buffer** — allocate `32 × vertex_count + 4` bytes (count-prefixed constructor array).
      Store the element-0 pointer into `BudObject +4`. Bulk-read `32 × vertex_count` bytes straight
      into that array in one read call.
   5. **Soft vertex-count warn** — if `vertex_count > 3072`, log a warning carrying the count and
      the first vertex's X and Z coordinates. **Warning only: load continues; no clamp, no reject.**
   6. **`index_count`** — read u32 (4 bytes) into `BudObject +16`.
   7. **Index buffer** — allocate `2 × index_count` bytes (plain heap allocation, **no count
      prefix**). Store the pointer into `BudObject +12`. Bulk-read `2 × index_count` bytes.
   8. Call `BudObject_ComputeAABBAndBudget` (§4).
5. Return success.

### 3.1 Allocation asymmetry — parser trap

The vertex buffer uses a count-prefixed array allocation (`32 × N + 4`); the stored pointer points
**past** the 4-byte count cookie to element 0. The index buffer uses a plain heap allocation
(`2 × N`) with **no count prefix**; the stored pointer is at the allocation start. A re-implementation
that assumes a count prefix on the index array will be off by 4 bytes. The vertex-array element count
lives in its cookie; the index count is kept in `BudObject +16`.

---

# 4. Per-object AABB and cull budget — `BudObject_ComputeAABBAndBudget` — (CODE-CONFIRMED)

Called once per object immediately after the index buffer is read.

1. Set `BudObject +48` (`vertex_byte_size`) = `32 × vertex_count`.
2. If `vertex_count == 0` or the vertex buffer is null, seed the AABB with an inverted (inside-out)
   sentinel via `Geometry_InitInvertedAABB`, then skip the per-vertex loop.
3. Otherwise seed AABB min and max from the first vertex's XYZ (first 3 floats of each 32-byte
   vertex record), then iterate the remaining vertices expanding the per-axis min and max.
4. **Degenerate-axis fix** — on any axis where `min == max`, add `2.0` to `max`.
5. Compute the cull metric:
   `metric = max( int(diagonal(aabb_max, aabb_min) × 0.6), int(aabb_max.Y − aabb_min.Y) )`
   where `diagonal` is the 3D bounding-box diagonal length.
   Select `BudObject +64` (`budget`) by tier:

   | metric | squared cull budget |
   |---|---|
   | < 8 | 90 000 |
   | < 16 | 250 000 |
   | < 32 | 1 000 000 |
   | < 64 | 2 250 000 |
   | ≥ 64 | 3 240 000 |

The budget is a **squared** distance threshold used by the frame-cull path; the draw dispatcher
compares the object's `camera_dist_sq` against this value before issuing a draw.

---

# 5. Mass-object grid registration — `Map_BuildMassObjectGrid` — (CODE-CONFIRMED)

Called by `Map_LoadCellDescriptor` as a **separate post-parse pass** immediately after
`Map_ParseDescriptor` returns. Registration is not done inside the blob loader.

1. **Grid-node array** — allocate `12 × object_count` bytes for a flat array of 12-byte grid nodes
   (layout: `BudObject*` at byte +0, zero at +4, zero at +8). Store the array base in the loader
   record.
2. **Grid bin loop** — for each of the `object_count` objects, walk a **16 × 16 grid of 64-unit
   cells** (256 cells covering the cell's 1024 × 1024-unit footprint). For each grid cell, test AABB
   overlap on the XZ plane:

   - `originX ≤ aabb_max.X` and `aabb_min.X ≤ originX + 64`
   - `originZ ≤ aabb_max.Z` and `aabb_min.Z ≤ originZ + 64`

   On overlap, `MassObjectGridCell_PushObjectRef` appends the object's grid-node to that cell's
   growable reference list. The per-grid-cell **max-Y** table is raised to `aabb_max.Y` and the
   per-grid-cell **min-Y** table is lowered to `aabb_min.Y` (two global 256-float arrays written
   at grid-build and reset at grid-reset). **Note:** the actual runtime height query
   (`MassObjectGrid_SampleCellMaxHeight`, see §5.2) samples per-object plane equations directly
   and does not read these two global arrays. No confirmed reader of these arrays was found in the
   complete mass-object cluster of build f61f66a9; they may be vestigial or consumed by an
   unexamined coarse-height or shadow path.

3. **Texture remap** — validate the object's `.bud`-local 1-based `tex_id`: if `tex_id < 1` or
   `tex_id > building_tex_count`, log `"mass texture index(%d) < 1 || > max(%d) terrain[%d,%d]"`
   and clamp `tex_id := 1`. Then overwrite `BudObject +0` in place:

   `tex_id := building_tex_table[tex_id]`

   where `building_tex_table` is the cell's BUILDING TEXTURES local→global table (§5.1). After this
   pass `tex_id` holds the global `bgtexture.lst` slot.

4. Call `BudObject_InitSwayBuffer` (§6) for each object.

## 5.1 BUILDING TEXTURES table — (CODE-CONFIRMED)

Populated during descriptor parse: inside the `.map`
`BUILDING { TEXTURES { <localId> <globalId> … } }` sub-block, each pair calls
`BuildingSection_AddTextureId`, which appends `globalId` into the loader record's texture table.
The table is hard-capped at **128 entries**; over-cap logs
`"MassTerrain::setTextureId over index"`. The grid-build remap reads the table as
`building_tex_table[local]` (1-based local index) → the corresponding global `bgtexture.lst` slot.

The per-object on-disk `tex_id` therefore indexes a cell-local list; that list maps to global
`bgtexture.lst` slots, which resolve to `data/map000/texture/<name>.dds`. This is the same
texture chain documented in `Docs/RE/formats/mesh.md §.bud` and `Docs/RE/formats/bgtexture_lst.md`.

## 5.2 Mass-object grid height query — `MassObjectGrid_SampleCellMaxHeight` — (CODE-CONFIRMED)

The grid is queried by **cell-index lookup + per-object plane sampling**, NOT by a
distance-squared spatial search (the only `dist_sq` in the system is the per-frame draw cull in
the draw-master, which is a brute-force per-object XZ test, not a grid query).

**Entry path.** A stepped XZ sampler converts a world XZ position to a cell index: it subtracts
the cell origin (local X and Z base coordinates), rejects positions outside `[−0.001, 1024.001]`,
then computes `cellX = clamp(int(localX × 0.015625), 0, 15)` and `cellZ` likewise (0.015625 =
1/64 — the reciprocal of the 64-unit grid-cell size). Up to 2 XZ-nudge retries are attempted
(`+0.1 × step` X, `+0.08 × step` Z) before the query fails.

**`MassObjectGrid_SampleCellMaxHeight`.** Given the cell indices, the cell record is located at
`grid_base + 256 × cellX + 16 × cellZ` (16-byte cell record: object-reference count at cell+8,
object-reference array pointer at cell+12). The function iterates the cell's object references,
calls `MassObject_SamplePlaneHeightXZ` per referenced object, and returns the **maximum** plane
height at the query XZ position (sentinel −FLT_MAX = no hit). This is the building-walk
ground-height sampler used for entity placement over mass objects.

---

# 6. Wind-sway buffer initialisation — `BudObject_InitSwayBuffer` — (CODE-CONFIRMED; per-frame oscillator formula now static-confirmed — see §9.1)

Called once per object at the end of the grid-registration pass, after the texture remap.

1. Query a **per-texture anim-class byte** from the `TerrainManager` singleton, indexed by the
   object's (now global) `tex_id`. Store it in `BudObject +70` (`anim_class`).

2. **Sway family 1** (`anim_class` 10..14):
   - Compute `sway_amp` (`BudObject +92`) from a vertical vertex extent. A special 9-vertex path
     uses specific vertex Y values; the general (> 5-vertex) path uses two vertex Y coordinates.
   - Set `sway_vfactor` (`BudObject +96`): `0.5` in the 9-vertex path (or `ratio × 0.7` for a
     sub-variant); approximately `0.3` in the general path.
   - Compute `centroid` (`BudObject +104..+112`) as `0.5 × (vtx[0].normal + vtx[2].normal)`
     componentwise — half the sum of the normal-slot vectors (bytes 12..23 of the 32-byte
     per-vertex record) of vertices 0 and 2 in the raw source vertex buffer. **Correction from
     prior revision:** this is NOT 0.5 × the AABB midpoint (corrected 2026-06-29).
   - Divide `sway_amp` by `2 << (anim_class − 10)`.
   - Allocate a full-size scratch vertex buffer (`32 × vertex_count + 4`, count-prefixed, same shape
     as the source vertex buffer) into `BudObject +72` (`anim_vertex_buffer`). Seed it by `memcpy`
     of the source vertex buffer.

3. **Sway family 2** (`anim_class` 20..24):
   - `sway_amp` = `distance(aabb_max, aabb_min) × 0.005`, clamped to `2.0`.
   - **Divide** `sway_amp` by `2 << (anim_class − 20)` (confirmed DIVIDE — same direction as family 1; resolves prior [debugger-confirm]).
   - Allocate and seed scratch buffer the same way as family 1.

4. **All other classes** (e.g. `anim_class 1`): static object. No scratch buffer; `BudObject +72`
   stays null.

The scratch buffer is rebuilt each frame by the draw dispatcher's three animator variants. The
oscillator is a **linear triangle-wave ping-pong** (no trigonometry); per-variant displacement
axes and sound-reactivity details are fully static-confirmed — see
`Docs/RE/structs/bud_object.md §Wind-sway animation system`. The only residual debugger need is
three runtime scalar values (accumulated env-time scalar, live sound-source array contents, tick
counter state) to validate the formula against actual vertex output. Load-time (`BudObject_InitSwayBuffer`)
only sets `anim_class`, `sway_amp`, `sway_vfactor`, `centroid`, and allocates/seeds the scratch
buffer; the oscillator state fields (`sway_phase`, `sway_dir`, `sway_min`, `sway_max`) are driven
by the per-frame animators.

---

# 7. Draw path — summary (out-of-load scope) — (CODE-CONFIRMED)

`BuildingSection_DrawIndexed` binds the resolved texture pool entry to texture stage 0 and calls
`IDirect3DDevice9::DrawIndexedPrimitiveUP` with:

- Primitive type: `D3DPT_TRIANGLELIST` (4)
- Index format: `D3DFMT_INDEX16` (101)
- Vertex stride: 32 bytes
- Vertex and index data: **user-pointer** (system-memory heap arrays, batched into a per-section
  staging buffer at draw time)

This confirms that the load-time arrays are plain system memory and there is **no `CreateVertexBuffer`
or `CreateIndexBuffer`** call anywhere in the load chain. The FVF is `0x112`
(`D3DFVF_XYZ | D3DFVF_NORMAL | D3DFVF_TEX1`) — per `Docs/RE/formats/mesh.md §.bud`; not
re-derived here.

## 7.1 Foliage draw-master gate: weather/time and env-time accumulator — (CODE-CONFIRMED)

`Foliage_DrawMaster` controls all sway animation. Before invoking any sway animator each frame:

1. A main gate checks a `BattleController` presence flag; if absent, no foliage draw occurs at all.
2. The weather/time-of-day state is read from field **+24** of the environment singleton:
   - **State 3:** sway is frozen — the env-time accumulator is not advanced; no sway animator runs.
   - **State 1:** sway-family-1 (class 10–14) objects invoke `BudObject_SwayAnimate_A`; sway-family-2
     (class 20–24) objects invoke `BudObject_SwayAnimate_C`.
   - **Any other non-3 state:** sway-family-1 objects invoke `BudObject_SwayAnimate_B`; sway-family-2
     objects receive a plain copy (no oscillation).
3. An env-time accumulator is advanced by the engine's per-frame env-time delta. Sway animators are
   invoked only when the accumulator reaches a threshold of **50** (integer compare). The time-scalar
   `a3` passed to the animators equals the accumulated value × **1e-6**. The accumulator resets to 0
   after each sway pass. Between sway passes, only the per-frame XZ cull step (§7.2) runs.

The integer-to-weather-condition mapping for the state field is not yet resolved (see §9.2 item 4).

## 7.2 Per-frame XZ cull — (CODE-CONFIRMED)

Each frame, for every object on the sway render lists (regardless of the env-time accumulator state):

```
mid     = (aabb_min + aabb_max) × 0.5
dist_sq = (mid.x − cam.x)² + (mid.z − cam.z)²     // XZ plane only; Y ignored
```

`dist_sq` is written to `BudObject +56`. `culled_flag` (+61) is set to 1 when `dist_sq > budget`,
0 otherwise. The draw batchers read `+61` and skip culled objects. No other BudObject byte is
written by this cull path in the foliage/mass-object cluster (see `structs/bud_object.md §Open
items` for the status of `flag_60` at +60). Camera XZ is read from the render-globals structure
each frame.

---

# 8. Constants and parameter tables

## 8.1 Decode parameters

| Parameter | Value | Source |
|---|---|---|
| `BudObject` in-memory stride | 116 bytes | loader array constructor |
| `BudObject` array count prefix | 4 bytes ahead of element 0 | loader allocation |
| On-disk vertex stride | 32 bytes | per-object vertex bulk-read |
| On-disk index element | 2 bytes (u16) | per-object index bulk-read |
| Soft vertex-count warn threshold | > 3072 | warn-only, load continues |
| Vertex buffer allocation | `32 × N + 4` (count-prefixed array) | has count prefix |
| Index buffer allocation | `2 × N` (plain heap) | **no** count prefix |

## 8.2 Grid and geometry parameters

| Parameter | Value | Source |
|---|---|---|
| Mass-object grid dimensions | 16 × 16 = 256 cells | grid registration |
| Grid cell size | 64 units | AABB overlap test step |
| Grid-node stride | 12 bytes (`BudObject*`, 0, 0) | grid-node array |
| Per-grid-cell Y tables | two global 256-float arrays (minY, maxY) | written at grid-build/reset; no confirmed reader in mass-object cluster (see §5) |
| BUILDING TEXTURES hard cap | 128 | `BuildingSection_AddTextureId` guard |
| Degenerate-AABB axis fix | +2.0 on any collapsed axis | AABB compute |

## 8.3 Cull budget tiers (squared distance threshold)

| AABB metric | Squared cull budget |
|---|---|
| < 8 | 90 000 |
| < 16 | 250 000 |
| < 32 | 1 000 000 |
| < 64 | 2 250 000 |
| ≥ 64 | 3 240 000 |

AABB metric = `max( int(diagonal × 0.6), int(height) )`.

## 8.4 Wind-sway parameters

| Parameter | Value | Source |
|---|---|---|
| Sway family 1 anim classes | 10..14 | `BudObject_BuildLodBuffer` |
| Sway family 2 anim classes | 20..24 | `BudObject_BuildLodBuffer` |
| Sway vfactor — family 1 general | ≈ 0.3 | load-time set |
| Sway vfactor — family 1 9-vertex | 0.5 (or `ratio × 0.7` sub-variant) | load-time set |
| Sway amp scale — family 1 | divide by `2 << (anim_class − 10)` | load-time set |
| Sway amp scale — family 2 | **divide** by `2 << (anim_class − 20)` (confirmed DIVIDE — same direction as family 1) | load-time set |
| Scratch buffer allocation | `32 × vertex_count + 4` (count-prefixed) | mirrors source buffer |
| Env-time accumulator threshold | 50 (integer compare) | sway pass trigger |
| Sway time-scalar `a3` | `accumulator × 1e-6` | supplied by `Foliage_DrawMaster` |
| Weather state 3 | freezes sway (accumulator not advanced) | env-singleton field +24 |
| Weather state 1 | family-1 → `SwayAnimate_A`; family-2 → `SwayAnimate_C` | env-singleton field +24 |
| Other non-3 weather state | family-1 → `SwayAnimate_B`; family-2 → plain copy | env-singleton field +24 |

## 8.5 Draw parameters

| Parameter | Value | Source |
|---|---|---|
| Primitive type | `D3DPT_TRIANGLELIST` (4) | `BuildingSection_DrawIndexed` |
| Index format | `D3DFMT_INDEX16` (101) | `BuildingSection_DrawIndexed` |
| Vertex stride | 32 bytes | `BuildingSection_DrawIndexed` |
| FVF | `0x112` (`D3DFVF_XYZ \| D3DFVF_NORMAL \| D3DFVF_TEX1`) | `Docs/RE/formats/mesh.md §.bud` |

## 8.6 Loader record fields (BuildingLoader / MassTerrain) set at load

| Byte offset | Role |
|---|---|
| +4 | `BudObject` array base (pointer to element 0) |
| +8 | Grid-node array base (12-byte nodes) |
| +12 | `object_count` |
| +4112 + 4i | BUILDING TEXTURES local→global table entry i (0-based; cap 128) |
| +4624 | BUILDING TEXTURES entry count |

The loader record (BuildingLoader / MassTerrain object) is accessed from the cell/terrain context.
The authoritative cross-section layout belongs to `Docs/RE/structs/terrain-manager.md`.

---

# 9. Resolved facts and open items

## 9.1 Resolved (CODE-CONFIRMED, static IDA)

- Path resolution: `.map` `BUILDING DATAFILE <path>` string → VFS entry lookup; no numeric id table.
- VFS open: `DiskFile_OpenByValue` → `Vfs_FindAndReadEntry`; in-memory-VFS slice, binary verbatim,
  EOF-clamped via `DiskFile_ReadBytesClampedToEof`.
- Full per-object decode order and field destinations (§3), including vertex/index allocation
  asymmetry (§3.1).
- Per-object AABB computation and tiered cull-budget selection (§4).
- Grid registration: 16 × 16 / 64-unit grid, tex local→global remap (§5).
- BUILDING TEXTURES table: populated from `.map`, remapped to global `bgtexture.lst` slot at grid
  build (§5.1).
- Grid height query path: cell-index lookup → `MassObjectGrid_SampleCellMaxHeight` → per-object
  plane max-height; NOT a distance-squared spatial search (§5.2).
- Wind-sway structure: anim-class query, family 1 / family 2 branch, scratch buffer allocation and
  seed (§6). `BudObject_InitSwayBuffer` is the canonical function name (renamed from prior
  `BudObject_BuildLodBuffer`).
- Centroid formula: `0.5 × (vtx[0].normal + vtx[2].normal)` componentwise — NOT AABB midpoint
  (corrected 2026-06-29).
- Family-2 sway amp direction: confirmed **DIVIDE** by `2 << (anim_class − 20)`, same as family 1.
- Sway oscillator type: linear triangle-wave ping-pong; no trigonometric call in any variant (§6).
- Weather/time gate and env-time accumulator: env-singleton +24 drives routing; threshold 50,
  scale 1e-6; state 3 freezes; state 1 = A+C variants; other = B+copy (§7.1).
- Per-frame XZ cull: writes `dist_sq` (+56) and `culled_flag` (+61) only (§7.2).
- No Direct3D vertex or index buffer created at load; draw via `DrawIndexedPrimitiveUP`,
  `D3DFMT_INDEX16`, `D3DPT_TRIANGLELIST`, stride 32 (§7).

## 9.2 Open / [debugger-confirm]

1. **No `.bud` sample byte-walked** — `object_count`, the per-object vertex field layout (position /
   normal / UV ordering within each 32-byte record), and the `type`-byte value domain are
   loader-derived, not sample-confirmed. Pull a foliage-bearing cell's `.bud` from the VFS and walk
   it to corroborate. (Authoritative on-disk layout: `Docs/RE/formats/mesh.md §.bud`.)
2. **`type` byte (`BudObject +52`) semantics** — read and stored at load; no load-time branch on it;
   consumer subsystem not identified. Likely a mesh sub-class or collision tag; confirm via the draw
   or collision path.
3. **UV convention** — no v-flip is evident in the building draw path (unlike `.skn` / `.xobj`);
   confirm against a real `.bud` sample that UVs are already in D3D top-left convention.
   (`Docs/RE/formats/mesh.md §.bud` open question.)
4. **Winding after world Z-negation** — the Godot importer must confirm index winding after the
   `(x, y, z) → (x, y, −z)` world coordinate transform. (`Docs/RE/formats/mesh.md §Coordinate
   handedness`.)
5. **Weather state integer → named condition mapping `[debugger-confirm]`** — env-singleton +24
   field values 3, 1, and other non-3 each produce distinct sway behaviour (frozen, A+C, B+copy
   respectively). The in-game weather or time-of-day condition that corresponds to each integer value
   requires a live read under known game conditions.
6. **Runtime sway scalars `[debugger-confirm]` (tightened)** — the oscillator and per-variant
   displacement formulas are static-confirmed. Residual: three runtime scalar values needed to
   reproduce exact vertex output at a given frame: (a) gate scalar `a3`; (b) live
   `AmbientSoundManager` source-array contents; (c) global per-family tick counter state.
   (`Docs/RE/structs/bud_object.md §Open items item 3`.)
7. **Per-cell Y arrays read-site** — the two global 256-float arrays (minY / maxY) written at
   grid-build have no confirmed reader in the complete mass-object cluster. The actual height query
   (`MassObjectGrid_SampleCellMaxHeight`) samples per-object plane equations. Find a reader or
   classify as vestigial. (`Docs/RE/specs/bud_loader.md §5.2`.)
