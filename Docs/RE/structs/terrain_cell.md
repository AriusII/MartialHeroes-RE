---
verification: static-ida only (IDB SHA f61f66a9, 2026-06-28); no debugger confirmation — open items listed at the end
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [world_systems, resource_pipeline]
conflicts: none-open
wave11_deepdive: 2026-06-28 — EXD/UP internals fully mapped (size structurally derived, bucket/triangle tables recovered); collision node (148 B) fully decoded; slot-1/FX layer internals decomposed; .ted 5-block field offsets pinned; texture-id index framing corrected (base-0, selector − 1)
deep_cartography_pass: 2026-06-29 (static-only, anchor f61f66a9) — colour_a@+27 non-write confirmed (C13); eviction Chebyshev-2 confirmed (C17); EXD ground consumer locates only v0/v1/v2 — plane@+52 and scalar@+68 not read (C7); UP layer has no located static reader (C8); plane formula confirmed (C10); open items D1–D5 tightened.
---

# Structs: TerrainCell — Field Offset Tables

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets are byte offsets from the start of the respective object** —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> The streaming lifecycle is in `specs/terrain-streaming.md`; the containing loader/manager layouts
> are in `structs/terrain-manager.md`; asset-file formats (.ted / .map / .sod) are in
> `formats/terrain.md`. Citing engineers: `// spec: Docs/RE/structs/terrain_cell.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow + operand analysis,
> corroborated across multiple sites; `[static-hypothesis]` = gap-derived or seen only at zero-init,
> not independently re-isolated this pass (do not assume); `[sample-verified]` = additionally matched
> against a real VFS sample — **does not apply here** (in-memory heap layout, not a packed-file
> format). No item in this spec is debugger-confirmed (static-only pass); a live attach during a cell
> load would settle the open items listed at the end.

---

## Object identity

A **TerrainCell** is the per-cell runtime object for one 1024×1024-unit terrain tile — a 65×65-vertex,
16×16-patch grid. It is allocated as a **24,712-byte** (0x6088) heap object and placed in the
**34-slot pool** owned by `TerrainLoader` (see `structs/terrain-manager.md` §Pool ownership). Pool
slots are recycled by clearing the in-use byte (+24708); they are never freed during play.

The 24,712-byte object does **not** store renderable vertex data. It holds:

- spatial-acceleration data over the 16×16 patches: per-patch AABBs, centroids, visibility/active
  masks, per-frame camera-distance LOD scalars, and a 341-node bounding-sphere quadtree;
- cell map identity and two world-AABB copies;
- **9 heap-pointer layer slots** (+16296..+16328): slot 0 = ground geometry buffer, slot 1 =
  building/mass-object grid, slots 2–8 = FX1–FX7 effect layers;
- two embedded sub-tile grids (EXD extra-decal at +16332, UP multi-level terrain at +20436);
- a GAD stub pointer (+24540), a MUD sound/material grid pointer (+24544), and an **embedded .sod
  collision object** (+24548, 160 bytes);
- an in-use/active byte (+24708) that drives pool eviction.

The actual ground mesh (positions, normals, vertex colour, two UV sets) lives in the **slot-0 ground
geometry buffer** — a separate 294,416-byte (0x47E10) heap object pointed to by `cell+16296`.

---

## TerrainCell — full field table (24,712 bytes)

**Construction order in `TerrainCell_ctor`:**
1. `TerrainCell_InitGridArrays` — array-constructs the three acceleration grids; resets masks and AABB.
2. In-place construction of embedded members: second world-AABB copy (+16280), EXD sub-tile grid
   (+16332, `ExdLayer_InitSubtileGrid16x16`), UP sub-tile grid (+20436), GAD slot (+24540), MUD slot
   (+24544), collision object (+24548).
3. Zeroes map identity (+16252 / +16256).
4. Heap-allocates and stores the 9 layer sub-objects at +16296..+16328 (see Layer slots table below).

**Destruction order in `TerrainCell_dtor`:** destroys and frees the 9 heap layer sub-objects
(null-fills each pointer), then destroys embedded members in reverse construction order, then calls
`TerrainCell_FreeGridArrays`.

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0 | 256 | u8[16][16] | `active_patch_mask` | Per-patch active/acquired mask; 1 = subtile layer refs acquired. Reset by `TerrainCell_ResetGroundTexUsageGrid`; toggled per-frame by `RefreshSubtileObjectsAndLOD`. Row-major: index = `col + 16·row`. | confirmed |
| +256 | 256 | u8[16][16] | `visible_patch_mask` | Per-patch visible-this-frame mask; set by the quadtree cull pass each frame. Index = `col + 16·(row+16)` (base +256). | confirmed |
| +512 | 4 | u32 | `visible_patch_count` | Count of patches the cull marked visible this frame. | confirmed |
| +516 | 6,144 | float[256][6] | `patch_aabb_grid` | 256 per-patch AABBs, 24 bytes each: `(minX,minY,minZ,maxX,maxY,maxZ)`. Array-constructed by `TerrainCell_InitGridArrays` (`RaySegment_ResetMaxDistance` per entry); filled from each subtile during `Ted_BuildCellGroundGrid`. Read by quadtree leaves. | confirmed |
| +6,660 | 1,024 | float[256] | `patch_dist_sq` | Per-patch squared distance to camera (LOD selector). Written each frame by `RefreshSubtileObjectsAndLOD`: `|cameraPos − patchCentroid|²`. Index = `col + 16·row`. | confirmed |
| +7,684 | 3,072 | float[256][3] | `patch_centroid` | Per-patch centroid float3 (12 bytes each). Built by `Ted_BuildCellGroundGrid` from each subtile AABB midpoint. | confirmed |
| +10,756 | 5,456 | BoundingSphere[341] | `quadtree_nodes` | 341-node bounding-sphere quadtree: 16 bytes per node `(centerX,centerY,centerZ,radius)`. 341 = 1+4+16+64+256 (depth-4 complete quadtree over the 16×16 patches). Built by `TerrainCell_BuildSubtileQuadtreeBounds`; leaves wrap the +516 patch AABBs; internal nodes merge child spheres; root = node 0. | confirmed |
| +16,212 | 16 | BoundingSphere | `cell_root_sphere` | Mirror of quadtree node 0: center float3 + radius. Zeroed at construction; populated as the out-parameter of `TerrainCell_BuildSubtileQuadtreeBounds`. | confirmed |
| +16,228 | 24 | float[6] | `cell_aabb_local` | Cell local AABB `(minX,minY,minZ,maxX,maxY,maxZ)`. minX/minZ from subtile[0], maxX/maxZ from subtile[255], minY/maxY from slot-0 global height extremes. This pointer is passed to every layer-build call. | confirmed |
| +16,252 | 4 | i32 | `map_x` | Map-X identifier = 10000 + cellX. Written by `Terrain_LoadCellFiles`. | confirmed |
| +16,256 | 4 | i32 | `map_z` | Map-Z identifier = 10000 + cellZ. | confirmed |
| +16,260 | 4 | i32 | `area_id` | Owning area identifier (the NNN area). | confirmed |
| +16,264 | 4 | float | `world_x0` | World AABB left edge: `(mapX − 10000) × 1024`. | confirmed |
| +16,268 | 4 | float | `world_z0` | World AABB near edge: `(mapZ − 10000) × 1024`; also passed as the ambient-sound Z parameter. | confirmed |
| +16,272 | 4 | float | `world_x1` | World AABB right edge: `world_x0 + 1024`. | confirmed |
| +16,276 | 4 | float | `world_z1` | World AABB far edge: `world_z0 + 1024`. | confirmed |
| +16,280 | 16 | float[4] | `world_aabb_copy` | Second copy of the world AABB (X0,Z0,X1,Z1), constructed in place; values baked identically by `Terrain_LoadCellFiles`. | confirmed |
| +16,296 | 4 | ptr | `layer_slot_0` | Slot 0 — **ground geometry buffer** → 294,416-byte heap object. See Ground Geometry Buffer table. | confirmed |
| +16,300 | 4 | ptr | `layer_slot_1` | Slot 1 — **building/mass-object grid** → 4,628-byte heap object; initialised by `TerrainCell_InitMassObjectGrid`, populated by `Bud_LoadBuildingBlob`. | confirmed |
| +16,304 | 4 | ptr | `layer_slot_2` | Slot 2 — **FX1 effect layer** → 4,244-byte heap object. | confirmed |
| +16,308 | 4 | ptr | `layer_slot_3` | Slot 3 — FX2 effect layer. | confirmed |
| +16,312 | 4 | ptr | `layer_slot_4` | Slot 4 — FX3 effect layer. | confirmed |
| +16,316 | 4 | ptr | `layer_slot_5` | Slot 5 — FX4 effect layer. | confirmed |
| +16,320 | 4 | ptr | `layer_slot_6` | Slot 6 — FX5 effect layer. | confirmed |
| +16,324 | 4 | ptr | `layer_slot_7` | Slot 7 — FX6 effect layer. | confirmed |
| +16,328 | 4 | ptr | `layer_slot_8` | Slot 8 — FX7 effect layer. | confirmed |
| +16,332 | 4,104 | embedded | `exd_layer` | **EXD (extra-decal) sub-tile layer**, embedded 16×16 grid. Constructed by `ExdLayer_InitSubtileGrid16x16`; populated from the `.map` `EXTRA_TERRAIN` section via `Exd_DecodeTriangles`. Layout: 8-byte head (`triangle_records` ptr + `triangle_count`) + 256×16-byte bucket grid + flat triangle-record array. **Queried by the ground-height raycast leaf** (`BuildBasisVectorsFromTwoPoints`) after the `.ted` subtile test — the EXD layer is the supplementary ground surface. See "EXD and UP layer internals" section. | confirmed (size structurally derived: 8 + 256×16) |
| +20,436 | 4,104 | embedded | `up_layer` | **UP (multi-level/up-terrain) sub-tile layer**, embedded 16×16 grid. Constructed in place with init symmetric to `ExdLayer_InitSubtileGrid16x16`; populated from the `.map` `UP_TERRAIN` section via `Up_DecodeTriangles`. Decoder and in-memory layout are byte-identical to `exd_layer`. **No static read site references this offset outside the two `.map` parsers (build) and the cell ctor/dtor (construct/destroy)** — the UP layer is built and freed but has no located query path. [debugger-confirm]: whether any non-static path (e.g. via cached pointer) dereferences this layer at runtime. See "EXD and UP layer internals" section. | confirmed (size structurally derived: 8 + 256×16) |
| +24,540 | 4 | ptr | `gad_slot` | **GAD stub pointer**. Constructed in place; `Gad_LoadStub` is a no-op — slot is always empty at runtime. | confirmed |
| +24,544 | 4 | ptr | `mud_grid_ptr` | **MUD sound/material grid pointer** → 32,768-byte heap grid. Loaded by `Mud_LoadGrid`; a missing `.mud` file is tolerated. | confirmed |
| +24,548 | 160 | embedded | `sod_collision` | **Embedded .sod collision object** (see Collision Object table). Constructed in place; loaded by `Sod_LoadCollisionBlob` from the `.map` `SOLID` section; destroyed with the containing cell. | confirmed |
| +24,708 | 1 (+3 pad) | u8 | `in_use` | Pool occupancy flag: 1 = loaded and active, 0 = free/evictable. Set under a file-scope critical section after a successful load; cleared by `TerrainManager_MarkDistantCellsForEvict` when `|focusMapX − cellMapX| > 2 OR |focusMapZ − cellMapZ| > 2` (Chebyshev distance > 2 — keeps a 5×5 active ring; map identity read from `cell+16252`/`cell+16256`). Also the recycle predicate scanned by the slot allocator. | confirmed |

**Total:** +24,708 + 4 = **24,712 bytes (0x6088)**. ✔

---

## Layer slots — sizes and build functions

| Slot | Cell offset | Heap size | Initialiser | Loader / `.map` section |
|-----:|------------:|----------:|-------------|-------------------------|
| 0 | +16,296 | 294,416 B | `TerrainCell_InitGroundSubtileGrid` | `Ted_LoadGeometryBlob` / `TERRAIN` + `TEXTURES{}` |
| 1 | +16,300 | 4,628 B | `TerrainCell_InitMassObjectGrid` | `Bud_LoadBuildingBlob` / `BUILDING` |
| 2 | +16,304 | 4,244 B | (FX1 init) | `Fx1_DecodeGroups` + `Fx1Section_AddTextureId` / `FX1` |
| 3 | +16,308 | 4,244 B | (FX2 init) | `Fx2_DecodeGroups` + `Fx2Section_AddTextureId` / `FX2` |
| 4 | +16,312 | 4,244 B | (FX3 init) | `Fx3_DecodeGroups` + `Fx3Section_AddTextureId` / `FX3` |
| 5 | +16,316 | 4,244 B | (FX4 init) | `Fx4_DecodeGroups` + `Fx4Section_AddTextureId` / `FX4` |
| 6 | +16,320 | 4,244 B | (FX5 init) | `Fx5_DecodeGroups` + `Fx5Section_AddTextureId` / `FX5` |
| 7 | +16,324 | 4,244 B | (FX6 init) | `Fx6_DecodeGroups` + `Fx6Section_AddTextureId` / `FX6` |
| 8 | +16,328 | 4,244 B | (FX7 init) | `Fx7_DecodeGroups` + `Fx7Section_AddTextureId` / `FX7` |

---

## Slot-1 building/mass-object grid (4,628 bytes)

Pointed to by `cell+16,300`. Initialised by `TerrainCell_InitMassObjectGrid`, populated by `Bud_LoadBuildingBlob` from the `.map` `BUILDING` section. Offsets from the buffer base.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 4 | ptr | `owner_cell` | Back-pointer to the owning TerrainCell. | confirmed |
| +4 | 4 | ptr | `building_array` | Pointer to flat array of 116-byte BudObject records. Set by `Bud_LoadBuildingBlob`. | confirmed |
| +8 | 4 | u32 | (zeroed header) | Zeroed at construction. | confirmed |
| +12 | 4 | u32 | `building_count` | Count of BudObject records. | confirmed |
| +16 | 4,096 | bucket[256] | `patch_building_grid` | 16×16 per-patch building-index lists (256 × 16-byte growable-vector entries, reserved cap 128). Each bucket holds u32 indices into `building_array`. | confirmed |
| +4,112 | 512 | u32[128] | `building_tex_list` | Per-cell building texture-id list. Appended by `BuildingSection_AddTextureId`; `list[0]` is the first appended id (base-0 storage); capacity = 128 entries. | confirmed |
| +4,624 | 4 | u32 | `building_tex_count` | Count of entries populated in `building_tex_list`. | confirmed |

**Total:** +4,624 + 4 = **4,628 bytes**. ✔

### BudObject (116 bytes)

One per building object; allocated as a flat array by `Bud_LoadBuildingBlob`.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 4 | u32 | `tex_id` | Global bgtexture pool index for this building object. | confirmed |
| +4 | 4 | ptr | vertex array | Pointer to 32-byte vertex records. | confirmed |
| +8 | 4 | u32 | `vertex_count` | Vertex count (a warning is logged if > 0xC00). | confirmed |
| +12 | 4 | ptr | index array | Pointer to u16 triangle index records. | confirmed |
| +16 | 4 | u32 | `index_count` | Triangle index count. | confirmed |
| +52 | 1 | u8 | `type_byte` | Object type byte from the file. | confirmed |
| +20..+51, +53..+115 | — | — | AABB / budget fields | Filled by `BudObject_ComputeAABBAndBudget`; individual field offsets not mapped here (separate mass-object spec). | static-hypothesis |

---

## FX layer container (4,244 bytes, slots 2–8)

Layout is identical for all seven FX layers (FX1–FX7) pointed to by `cell+16,304..+16,328`. Offsets from the buffer base.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 4 | ptr | `owner_cell` | Back-pointer to the owning TerrainCell. | confirmed |
| +4 | 4 | ptr | `group_array` | Pointer to flat array of 72-byte FX group records. Set by `Fx{n}_DecodeGroups`. | confirmed |
| +8 | 4 | u32 | (zeroed header) | Zeroed at construction. | confirmed |
| +12 | 4 | u32 | `group_count` | Count of FX group records. | confirmed |
| +16 | 4,096 | bucket[256] | `patch_fx_grid` | 16×16 per-patch FX-group-index lists (256 × 16-byte growable-vector entries, **reserved cap 32**). Each bucket holds u32 indices into `group_array`. | confirmed |
| +4,112 | 128 | u32[32] | `fx_tex_list` | Per-layer FX texture-id list. Appended by `Fx{n}Section_AddTextureId`; `list[0]` is the first appended id (base-0); **hard cap = 32 entries** (fixed-size array; over-index triggers an error log). | confirmed |
| +4,240 | 4 | u32 | `fx_tex_count` | Count of entries populated in `fx_tex_list`. | confirmed |

**Total:** +4,240 + 4 = **4,244 bytes**. ✔

### FX group record (72 bytes)

One per effect group; allocated as a flat array by `Fx{n}_DecodeGroups`. Leading u32 of the FX blob = group count; each group is decoded from a 20-byte file header.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 12 | — | header (3 dwords) | Not individually decoded. | confirmed (size) |
| +12 | 4 | u32 | `vertex_count` | Vertex count for this group. | confirmed |
| +16 | 4 | u32 | `index_count` | Triangle index count. | confirmed |
| +20 | 4 | ptr | vertex array A | Pointer to 36-byte vertex records (VF_36 format). | confirmed |
| +24 | 4 | ptr | index array | Pointer to u16 × `index_count` triangle indices. | confirmed |
| +28 | 4 | ptr | vertex array B | 36-byte copy of vertex array A (memcpy). | confirmed |
| +32..+59 | 28 | — | (not decoded) | Remaining fields not individually decoded. | static-hypothesis |
| +60 | 4 | u32 | `vertex_byte_size` | = 36 × `vertex_count`. | confirmed |
| +64..+71 | 8 | — | (not decoded) | Trailing bytes not individually decoded. | static-hypothesis |

---

## Ground geometry buffer — slot-0 sub-object (294,416 bytes)

Pointed to by `cell+16296`. Initialised by `TerrainCell_InitGroundSubtileGrid`, filled by
`Ted_LoadGeometryBlob`, finalized by `Ted_ResolvePatchTextures` and `Ted_BuildCellGroundGrid`. All
offsets from the buffer base.

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0 | 4 | ptr | `owner_cell` | Back-pointer to the owning TerrainCell. | confirmed |
| +4 | 3,072 | entry[256] | `patch_subtile_grid` | 16×16 patch-to-subtile grid: 256 entries × 12 bytes = `{ TerrainGroundSubtile* subtile, u32, u32 }`. Row-major (16 cols × 16 rows). The two trailing u32s are zeroed at init and not written by any load-path function (`TerrainCell_InitGroundSubtileGrid`, `Ted_LoadGeometryBlob`, `Ted_ResolvePatchTextures`, `Ted_BuildCellGroundGrid`). [debugger-confirm]: whether `RefreshSubtileObjectsAndLOD` or the raycast path ever writes them. | confirmed |
| +3,076 | 290,816 | TerrainGroundSubtile[256] | `subtile_array` | 256 patch geometry objects, 1,136 bytes each (see TerrainGroundSubtile table). | confirmed |
| +293,892 | 512 | u32[128] | `terrain_tex_list` | Per-cell TERRAIN texture-id list: maps a 1-based `.ted` patch selector to its global bgtexture pool index. Appended in `.map` order by `TileTerrain_SetTextureId`; `list[0]` is the first appended id (base-0 storage); resolved as `terrain_tex_list[selector − 1]`; capacity = 128 entries. | confirmed |
| +294,404 | 4 | u32 | `terrain_tex_count` | Count of entries populated in `terrain_tex_list`. | confirmed |
| +294,408 | 4 | float | `cell_height_min` | Minimum vertex height (Y) over all patches in this cell. | confirmed |
| +294,412 | 4 | float | `cell_height_max` | Maximum vertex height (Y) over all patches. | confirmed |

**Total:** +294,412 + 4 = **294,416 bytes (0x47E10)**. ✔

---

## TerrainGroundSubtile (1,136 bytes)

One per patch (256 per cell); constructed in place within `subtile_array` by
`TerrainGroundSubtile_ctor`. A patch covers a 5×5 vertex grid (4×4 quads). All offsets from the
subtile base.

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0 | 4 | u32 | `tex_selector` | Texture selector. `Ted_LoadGeometryBlob` writes the raw 1-based `.ted` patch byte here. `Ted_ResolvePatchTextures` then clamps to [1, `terrain_tex_count`] and overwrites with `terrain_tex_list[selector − 1]` — the global bgtexture pool index. After finalize this field is the render-ready texture handle. | confirmed |
| +4 | 1,100 | TerrainVertex[25] | `vertices` | 5×5 vertex grid, 25 × 44 bytes (see TerrainVertex table). Quad corners are vertex indices 0, 4, 20, 24 (at subtile +4, +180, +884, +1,060). | confirmed |
| +1,104 | 24 | float[6] | `patch_aabb` | Patch AABB `(minX,minY,minZ,maxX,maxY,maxZ)`. Filled while baking vertices in `Ted_LoadGeometryBlob`; copied to `patch_aabb_grid` (+516 in the cell) during `Ted_BuildCellGroundGrid`. | confirmed |
| +1,132 | 1 | u8 | `lod_split_flag` | 1 when the patch height range exceeds 8.0 units — use the full 4×4×2-triangle shared index list; 0 = flat patch, raycast as a single 2-triangle quad from the 4 corner vertices only. | confirmed |
| +1,133 | 1 | u8 | `direction_byte` | Directional flags from `.ted` block-4: bit 0 = S/U flip, bit 1 = T/V flip (UV orientation). See `formats/terrain.md §5.7`. | confirmed |
| +1,134 | 2 | (pad) | — | Alignment padding. `.ted` decoder writes exactly +0..+1,133; these 2 bytes are not written by the load path. | confirmed |

**Total:** 1,136 bytes. ✔

### Shared static triangle index buffer

`TerrainGroundSubtile_ctor` builds, once (guarded by a per-process flag), a single 16-bit triangle
index array in module-level globals describing the 4×4-quad × 2-triangle topology. Every patch in
every cell reuses this shared buffer — it is **not** per-cell or per-subtile. The `lod_split_flag`
at +1,132 selects between the full topology and the 2-triangle degenerate quad.

---

## TerrainVertex (44 bytes)

Embedded as an array of 25 within each `TerrainGroundSubtile` at offset +4.

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0 | 4 | float | `pos_x` | Absolute world X: `col × 16 + (mapX − 10000) × 1024`. | confirmed |
| +4 | 4 | float | `pos_y` | Height; from `.ted` block-1 f32, no scale applied. | confirmed |
| +8 | 4 | float | `pos_z` | Absolute world Z: `row × 16 + (mapZ − 10000) × 1024`. | confirmed |
| +12 | 4 | float | `normal_x` | `.ted` block-2 signed byte ÷ 127.0. | confirmed |
| +16 | 4 | float | `normal_y` | Same encoding; always positive (Y-up convention). | confirmed |
| +20 | 4 | float | `normal_z` | Same encoding. | confirmed |
| +24 | 1 | u8 | `colour_b` | Vertex colour B = `.ted` block-5 byte × 0.5. | confirmed |
| +25 | 1 | u8 | `colour_g` | Vertex colour G; same encoding. | confirmed |
| +26 | 1 | u8 | `colour_r` | Vertex colour R; same encoding. | confirmed |
| +27 | 1 | u8 | `colour_a` | Alpha / pad byte. CONFIRMED: the `.ted` loader does NOT read the on-disk diffuse A byte (disk +3) and does NOT write to vertex +27; the ctor-default value is retained. [debugger-confirm]: actual ctor-default byte value (expected 0 from zeroed-memory allocation). | confirmed |
| +28 | 4 | float | `uv0_u` | UV set 0 — U (0 / 0.25 / … / 1.0 across 5 columns; S-flipped per direction bit 0). | confirmed |
| +32 | 4 | float | `uv0_v` | UV set 0 — V (0 / 0.25 / … / 1.0 down 5 rows; T-flipped per direction bit 1). | confirmed |
| +36 | 4 | float | `uv1_u` | UV set 1 — U (copy of `uv0_u`; second texture stage). | confirmed |
| +40 | 4 | float | `uv1_v` | UV set 1 — V (copy of `uv0_v`). | confirmed |

**Total:** 44 bytes. ✔

---

## EXD and UP layer internals (4,104 bytes each)

Both `exd_layer` (+16,332) and `up_layer` (+20,436) share a byte-identical decoder and in-memory layout. EXD is populated from the `.map` `EXTRA_TERRAIN` section via `Exd_DecodeTriangles`; UP from `UP_TERRAIN` via `Up_DecodeTriangles`. Both decoders expand 40-byte on-disk triangle records into 72-byte in-memory records via `UpExd_ExpandTriangle40to72`. Offsets are within the respective embedded sub-object.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 4 | ptr | `triangle_records` | Pointer to flat array of 72-byte in-memory triangle records. | confirmed |
| +4 | 4 | u32 | `triangle_count` | Count of triangle records in the array. | confirmed |
| +8 | 4,096 | bucket[256] | `patch_index_grid` | 16×16 per-patch index lists (256 × 16-byte growable-vector entries). Index = `col + 16·row`. Each bucket holds u32 indices into `triangle_records` for triangles overlapping that patch. Reserved to capacity 64 entries at construction. | confirmed |

**Total:** 8 + 4,096 = **4,104 bytes**. ✔

### Per-patch bucket (16 bytes)

An MSVC growable-vector of `u32` triangle indices, seeded to capacity 64 at construction.

| Byte offset | Size | Type | Field |
|------------:|-----:|------|-------|
| +0 | 4 | u32 | reserved (not initialised by the bucket constructor) |
| +4 | 4 | ptr | `begin` |
| +8 | 4 | ptr | `size_end` (count = (`size_end` − `begin`) ÷ 4) |
| +12 | 4 | ptr | `capacity_end` |

### On-disk triangle record (40 bytes)

Read sequentially from the `.map` `EXTRA_TERRAIN` / `UP_TERRAIN` blob.

| Byte offset | Size | Type | Field | Confidence |
|------------:|-----:|------|-------|------------|
| +0 | 12 | f32[3] | vertex 0 (x, y, z) | confirmed |
| +12 | 12 | f32[3] | vertex 1 (x, y, z) | confirmed |
| +24 | 12 | f32[3] | vertex 2 (x, y, z) | confirmed |
| +36 | 4 | f32 | scalar param (semantic unresolved; see open items) | confirmed (presence); [debugger-confirm] (semantic) |

### In-memory triangle record (72 bytes)

Expanded by `UpExd_ExpandTriangle40to72` from the 40-byte on-disk record.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 4 | f32 | XZ-AABB min X | `min(v0.x, v1.x, v2.x)` | confirmed |
| +4 | 4 | f32 | XZ-AABB min Z | `min(v0.z, v1.z, v2.z)` | confirmed |
| +8 | 4 | f32 | XZ-AABB max X | `max(v0.x, v1.x, v2.x)` | confirmed |
| +12 | 4 | f32 | XZ-AABB max Z | `max(v0.z, v1.z, v2.z)` | confirmed |
| +16 | 12 | f32[3] | vertex 0 (x, y, z) | Copied from disk +0. | confirmed |
| +28 | 12 | f32[3] | vertex 1 (x, y, z) | Copied from disk +12. | confirmed |
| +40 | 12 | f32[3] | vertex 2 (x, y, z) | Copied from disk +24. | confirmed |
| +52 | 16 | f32[4] | plane (nx, ny, nz, d) | `Geom_PlaneFromTriangle`: normalised cross of two edges; `d = −dot(normal, v1)`; degenerate triangle → plane zeroed. | confirmed |
| +68 | 4 | f32 | scalar param | Copied verbatim from disk +36. STATIC BOUND TIGHTENED: the located EXD ground consumer (`BuildBasisVectorsFromTwoPoints`) reads ONLY v0/v1/v2 (+16/+28/+40) and does NOT read the precomputed plane@+52 nor this scalar@+68. No static reader of +68 exists in the located terrain ground/query path. [debugger-confirm]: whether any non-terrain runtime path (navigation / clearance / multi-layer query) ever reads this field; if not, it is authoring-only/dead. | confirmed (presence); [debugger-confirm] (semantic) |

---

## Embedded collision object — .sod (160 bytes, at cell+24548)

Constructed in place at cell+24548; loaded by `Sod_LoadCollisionBlob` (routed from the `.map`
`SOLID` section); destroyed with the containing cell. Offsets are relative to the collision-object
base (= `cell+24548`).

| Byte offset | Size | Type | Field | Notes / evidence | Confidence |
|------------:|-----:|------|-------|------------------|------------|
| +0 | 148 | node | `root_node` | Solid-quadtree root node (see node field table below). Holds root/child pointers, XZ AABB, split planes, 16-slot leaf array, and the 16×16 acceleration-grid pointer. Node size proven: heap child nodes are allocated as 148-byte objects. | confirmed |
| +148 | 4 | ptr | `solid_array` | Pointer to heap-allocated solid-record array; each record is 108 bytes. | confirmed |
| +152 | 4 | u32 | `solid_count` | Count of solid records. | confirmed |
| +156 | 1 (+3 pad) | u8 | `has_collision` | 1 if any solid record was loaded; 0 for cells with no collision geometry. | confirmed |

**Total:** 160 bytes. ✔

**.sod binary shape (from `Sod_LoadCollisionBlob`):** `u32 solidCount`, then `solidCount × 108-byte`
SolidRecord; each SolidRecord reads its own `u32 quadCount` then `quadCount × 48-byte` QuadRecord
(quad array heap-allocated per solid; ptr at solid+64, count at solid+60). Collision is embedded
**in** the TerrainCell object, not in a separate global manager. Cross-reference:
`formats/terrain.md` (.sod collision section); `specs/whole_map_assembly.md §7` (high-offset layout
context).

### Solid-quadtree node (148 bytes)

Applies to both the embedded root node and all heap-allocated child nodes. Offsets are within the node (= within the collision object for the root). The root node's `root_ptr` is self-referential; all child nodes also point to the root.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 4 | ptr | `root_ptr` | Pointer to the root node (root = self; children also point at the root). | confirmed |
| +4 | 16 | ptr[4] | `child_nodes` | 4 child node pointers (quadrant children); null until split. | confirmed |
| +20 | 4 | u32 | `leaf_flag` | 1 = leaf-pending (has not yet been split); 0 = internal node after split. | confirmed |
| +24 | 4 | u32 | `depth` | Depth level; child = parent depth + 1. | confirmed |
| +28 | 16 | f32[4] | `node_aabb` | Node XZ AABB: (minX, minZ, maxX, maxZ). | confirmed |
| +44 | 4 | f32 | `split_x` | Split centre X = (minX + maxX) / 2. | confirmed |
| +48 | 4 | f32 | `split_z` | Split centre Z = (minZ + maxZ) / 2. | confirmed |
| +52 | 4 | ptr | `parent_ptr` | Pointer to parent node; null for the root. | confirmed |
| +56 | 4 | u32 | `leaf_count` | Current count of leaf entries; pre-incremented on each append. A count ≥ 16 triggers a split. | confirmed |
| +60 | 64 | ptr[16] | `leaf_array` | Up to 16 pointers to SolidRecord entries covering this node. | confirmed |
| +124 | 4 | ptr | `accel_grid_ptr` | Pointer to the 1,056-byte uniform 16×16 acceleration grid (root node only; set by `Sod_BuildSolidQuadtree`). | confirmed |
| +128 | 20 | — | (tail / reserved) | Within the 148-byte allocation, beyond the leaf array and acceleration-grid pointer. Part of the leaf-bucket structure. [debugger-confirm]: contents. | static-hypothesis |

**Total:** 148 bytes. ✔

### Uniform 16×16 acceleration grid (1,056 bytes, root node only)

Allocated by `Sod_BuildSolidQuadtree` and pointed to by `root_node+124`. Offsets within the grid object.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 4 | ptr | `root_node_ptr` | Back-pointer to the root quadtree node. | confirmed |
| +4 | 4 | ptr | `name_ptr` | Optional name string pointer; null for the terrain collision root. | confirmed |
| +8 | 4 | — | (not decoded) | 4-byte gap; not individually resolved. | static-hypothesis |
| +12 | 4 | u32 | `node_counter` | Incremented as child nodes register with the grid. | confirmed |
| +16 | 1,024 | ptr[256] | `cell_grid` | 16×16 grid of pointers, each → the covering quadtree node. Index = `col + 16·row`. | confirmed |
| +1,040 | 4 | f32 | `inv_cell_size_x` | 1 / cellSizeX (precomputed reciprocal). | confirmed |
| +1,044 | 4 | f32 | `inv_cell_size_z` | 1 / cellSizeZ. | confirmed |
| +1,048 | 4 | f32 | `cell_size_x` | (maxX − minX) / 16. | confirmed |
| +1,052 | 4 | f32 | `cell_size_z` | (maxZ − minZ) / 16. | confirmed |

**Total:** 1,056 bytes. ✔

### SolidRecord (108 bytes)

One per solid; laid out as a flat array at `solid_array`. `Sod_LoadCollisionBlob` reads each 108-byte block from disk, then reads `quad_count` and allocates the quad array.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 16 | f32[4] | `aabb` | XZ AABB (minX, minZ, maxX, maxZ); used by the quadtree insert. | confirmed |
| +16 | 44 | — | (not decoded) | Other solid params read flat from disk; individual fields not mapped here. | static-hypothesis |
| +60 | 4 | u32 | `quad_count` | Count of QuadRecords for this solid. | confirmed |
| +64 | 4 | ptr | `quad_array` | Pointer to heap-allocated array of 48-byte QuadRecords. | confirmed |
| +68 | 40 | — | (not decoded) | Remainder read flat from disk; not individually decoded. | static-hypothesis |

**Total:** 108 bytes. ✔

### QuadRecord (48 bytes)

One per quad; allocated as a flat array per solid. Read flat (48 × `quad_count`) from disk. Individual interior fields not decoded in this pass.

| Byte offset | Size | Type | Field | Notes | Confidence |
|------------:|-----:|------|-------|-------|------------|
| +0 | 48 | — | (flat from disk) | Full 48-byte block; outer size confirmed (48 × `quad_count`). Interior geometry/plane/material fields not individually decoded; a `.sod` consumer walk is needed. | confirmed (size); [debugger-confirm] (fields) |

---

## Build sequence

### 1. Entry — `Terrain_LoadCellFiles(cell, mapX, mapZ, areaId)`

- Writes map identity (+16252 / +16256 / +16260) and bakes both world-AABB copies
  (+16264..+16292 from `(mapIndex − 10000) × 1024`, span +1024).
- Resets per-patch masks, the +516 patch-AABB grid, and the reset entry of every layer sub-object
  (slots 0–8, EXD, UP) via `TerrainCell_ResetGroundTexUsageGrid`.
- Formats the base path `data/map<NNN>/dat/d<NNN>x<mapX>z<mapZ>` (NNN = 3-digit area id) and
  opens in order: `.mud` (`Mud_LoadGrid` → 32,768-byte grid at +24544; missing file tolerated),
  `.gad` (`Gad_LoadStub`, no-op), `.map` (`Map_LoadCellDescriptor`, mandatory).

### 2. `.map` open — `Map_LoadCellDescriptor`

Branches on whether the file is VFS-mounted or loose-disk; in both cases parses the `.map` text
descriptor (CP949 keyword format) and routes each section keyword to a decoder:

| Section keyword | Decoder(s) | Target |
|-----------------|-----------|--------|
| `TERRAIN` / `TEXTURES` | `Ted_LoadGeometryBlob` / `TileTerrain_SetTextureId` | Slot 0 + tex list |
| `BUILDING` | `Bud_LoadBuildingBlob` + `BuildingSection_AddTextureId` | Slot 1 |
| `UP_TERRAIN` | `Up_DecodeTriangles` | +20,436 UP layer |
| `EXTRA_TERRAIN` | `Exd_DecodeTriangles` | +16,332 EXD layer |
| `FX1`..`FX7` | `Fx{n}_DecodeGroups` + `Fx{n}Section_AddTextureId` | Slots 2–8 |
| `SOLID` | `Sod_LoadCollisionBlob` | +24,548 collision object |

### 3. `.ted` decode — `Ted_LoadGeometryBlob`

Reads 5 header-less binary blocks (total 46,987 bytes) into process-scope scratch globals:

| Block | Content | Size |
|-------|---------|-----:|
| 1 | Height grid: 65×65 f32 | 16,900 B |
| 2 | Normal grid: 65×65 signed-byte triplets | 12,675 B |
| 3 | Patch texture index: 16×16 u8 | 256 B |
| 4 | Patch direction: 16×16 u8 | 256 B |
| 5 | Vertex colour: 65×65 u8×4 (BGRA) | 16,900 B |

Then iterates 16×16 patches × 5×5 vertices, baking absolute world positions, normals (signed byte ÷
127), colour (× 0.5), two UV sets (with S/T flips from the direction byte), and the raw per-patch
texidx + direction + LOD split flag — all written into the slot-0 subtile array. Tracks per-patch
and per-cell min/max height (slot-0 +294,408 / +294,412). LOD derivation: `lod_split_flag` = 1 if
`patch_height_max − patch_height_min > 8.0`.

### 4. Finalize tail (within `Map_LoadCellDescriptor`, after all section decoders)

1. `Ted_ResolvePatchTextures(slot0)` — per patch: clamp `tex_selector` to [1, `terrain_tex_count`];
   overwrite with `terrain_tex_list[selector − 1]` (the global bgtexture pool index).
2. `Ted_BuildCellGroundGrid(cell, slot0)` — copy each subtile AABB into `patch_aabb_grid` (+516),
   compute centroids into `patch_centroid` (+7,684), derive cell AABB (+16,228: XZ from corner
   subtiles, Y from slot-0 height extremes).
3. Build the remaining layer grids from the cell AABB pointer (+16,228): mass-object grid (slot 1),
   the seven FX layer builds (slots 2–8), and the EXD (+16,332) and UP (+20,436) layer builds.
4. `TerrainCell_BuildSubtileQuadtreeBounds` — builds the 341-node bounding-sphere quadtree
   (+10,756) over the +516 patch AABBs; writes the root sphere to +16,212.

### 5. Activation

`in_use` (+24,708) is set to 1 under a file-scope critical section after a successful load. Cell
loads are synchronous on the main thread; the async streaming worker is dormant — see
`specs/terrain-streaming.md §2` and `structs/terrain-manager.md §Dormant-worker apparatus`.

---

## Per-frame lifecycle

`RefreshSubtileObjectsAndLOD`:
- Descends the quadtree (+10,756) to cull patches against the view frustum; writes
  `visible_patch_mask` (+256) and `visible_patch_count` (+512); toggles `active_patch_mask` (+0);
  acquires or releases layer sub-tile texture references.
- Writes `patch_dist_sq` (+6,660) as `|cameraPos − patchCentroid|²` per patch (LOD selection).

Ground-height and pick queries descend the quadtree via `TerrainCell_RaycastQuadtreeGroundHit` to
locate the candidate patch, then perform per-triangle plane interpolation within the slot-0 subtile
vertices. See `formats/terrain.md §5.4a` for the triangle-plane math.

---

## Texture-id resolution chain

1. `.ted` block-3 byte → raw 1-based selector, written to `TerrainGroundSubtile.tex_selector`.
2. `Ted_ResolvePatchTextures` clamps selector to [1, `terrain_tex_count`] and overwrites with
   `terrain_tex_list[selector − 1]` — the **global bgtexture pool index**.
3. Full chain (bgtexture pool index → `bgtexture.LST` entry → on-disk texture path) is in
   `formats/terrain.md` and summarised in `CLAUDE.md §Terrain texture`.

---

## Quick-reference

- **Object size:** 24,712 bytes (0x6088); pool of 34 in `TerrainLoader`.
- **Active/visible patch masks:** +0 (256 B), +256 (256 B); count +512.
- **Patch AABB grid:** +516, 256 × 24 B.
- **Per-patch distance² (LOD):** +6,660, 256 × 4 B.
- **Patch centroids:** +7,684, 256 × 12 B.
- **Bounding-sphere quadtree (341 nodes):** +10,756, 5,456 B.
- **Cell root sphere:** +16,212, 16 B.
- **Cell local AABB:** +16,228, 24 B.
- **Map identity:** mapX +16,252 / mapZ +16,256 / areaId +16,260.
- **World AABB copy 1:** +16,264..+16,276 (4 × float).
- **World AABB copy 2:** +16,280, 16 B.
- **Layer pointer array (9 slots):** +16,296..+16,328.
- **EXD embedded layer:** +16,332, 4,104 B (8-byte head + 256×16-byte bucket grid).
- **UP embedded layer:** +20,436, 4,104 B (byte-identical layout to EXD).
- **GAD stub ptr:** +24,540. **MUD grid ptr:** +24,544.
- **Embedded .sod collision:** +24,548, 160 B.
- **In-use byte:** +24,708.

---

## Open items — [debugger-confirm] (static bounds tightened by wave-11 deep-dive)

Previous items 1–4 (EXD/UP field layout; +1,134 padding; collision-object header; slot-1/FX internal layouts) are **CLOSED** by wave-11 static analysis. Remaining items:

1. **`patch_subtile_grid` trailing u32s** (per-entry +4 and +8, two u32s per 12-byte entry). Proven zeroed at init and not written by any load-path function (`TerrainCell_InitGroundSubtileGrid`, `Ted_LoadGeometryBlob`, `Ted_ResolvePatchTextures`, `Ted_BuildCellGroundGrid`). [debugger-confirm]: whether `RefreshSubtileObjectsAndLOD` or the raycast path writes them at runtime; if not, treat as permanently reserved.
2. **`TerrainVertex.colour_a` (+27) ctor default.** `.ted` decoder does not copy the diffuse A channel to this byte. [debugger-confirm]: what value `TerrainGroundSubtile_ctor` leaves here (expected 0/untouched memory).
3. **EXD triangle record scalar at in-memory +68 (disk +36).** STATIC BOUND TIGHTENED: the
   located EXD ground consumer (`BuildBasisVectorsFromTwoPoints`) reads ONLY v0/v1/v2 and does
   NOT read plane@+52 nor scalar@+68. No static reader of +68 exists in any located ground/query
   path. The **UP layer** (cell+20436) has no located static reader at all — only parsers (build)
   and ctor/dtor (construct/destroy) reference that offset.
   [debugger-confirm D1]: whether any non-terrain path (navigation, clearance, multi-layer height
   query) reads EXD scalar@+68 at runtime; if never, it can be declared authoring-only/dead.
   [debugger-confirm D2]: whether a live session ever dereferences the UP layer via a pointer
   cached elsewhere (if not, UP is load-and-hold-only dead geometry in the shipped client).
4. **SolidRecord interior (+16..+59, +68..+107) and QuadRecord interior (+0..+47).** Outer sizes and the AABB/count/ptr fields are confirmed; the remaining bytes are read flat from disk. [debugger-confirm] or `.sod`-consumer walk needed to label individual fields.
5. **Collision node tail +128..+147 (20 bytes).** Within the 148-byte node, beyond the 16-slot leaf array (+60..+123) and the acceleration-grid pointer (+124). Static bound: part of the leaf-bucket structure. [debugger-confirm]: whether these are additional bucket capacity/state or padding.

---

## Cross-references

- Streaming lifecycle (pool eviction, ring rotation): `specs/terrain-streaming.md`.
- TerrainLoader / TerrainManager layouts (pool + ring): `structs/terrain-manager.md`.
- Asset-file formats (.ted / .map / .sod / .mud): `formats/terrain.md`.
- bgtexture pool (global texture index): `formats/terrain.md` + `CLAUDE.md §Terrain texture`.
- Area cell census + per-cell fan-out: `formats/area_inventory.md`.
- Whole-map scene assembly (high-offset cell layout context): `specs/whole_map_assembly.md §7`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
