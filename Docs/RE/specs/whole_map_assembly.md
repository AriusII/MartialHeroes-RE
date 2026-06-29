---
status: code-confirmed
sample_verified: false   # static comprehension; live debugger confirmation deferred
subsystems: [resource_pipeline, world_systems, assets]
ida_reverified: 2026-06-27
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
verification: confirmed (IDB SHA 263bd994, 2026-06-26) — all five edges corroborate already-committed
              specs; no contradictions found; see §addendum-2026-06-26 for confirmed facts,
              corrections, open edges, and port guidance
              # CYCLE 14 (2026-06-27, IDB SHA f61f66a9): confirmatory re-anchor — subsystem cleanly
              # relocated, 1 re-confirmed SAME. Prior reverified: 2026-06-26.
              # open_item (pre-existing, NOT a build delta): addendum §5 / port-guidance step 2 phrase
              # bgtexture.lst as '76-byte records' — 76 is the runtime in-memory pool-entry stride; on-disk
              # record size is 48 bytes (terrain.md/bgtexture_lst.md already say 48-byte on-disk). Flagged
              # for spec-author cleanup; no fact change in this pass.
              # 2026-06-28 (IDB SHA f61f66a9): spec-author promotion — live world-entry chain,
              # two-object world-manager model (TerrainManager + TerrainLoader), runtime streaming
              # policy, region/zone system, per-area binary layouts, scene wiring, and area teardown
              # added; see §addendum-2026-06-28. region<NNN>.bin purpose resolved (zone-id grid).
conflicts: none-open
---

# Spec: Whole-Map Offline Assembly — Cell Enumeration, Placement, and Texture Pool

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room runtime comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. Consumed by
> `Assets.Vfs` / `Assets.Parsers` (cell enumeration + asset fan-out) and `Assets.Mapping`
> (offline whole-map build, identity placement, engine-boundary coordinate transform).
> Citing engineers: `// spec: Docs/RE/specs/whole_map_assembly.md`.
>
> **Authoritative cross-references:** `Docs/RE/formats/terrain.md` (cell geometry, bgtexture.lst pool,
> §1.2 manifest, §1.4 cell origin, §4 texture pool, §5.2 baked vertex positions, §5.4/§5.7 height/UV),
> `Docs/RE/formats/area_inventory.md` §1A (manifest, key formula, per-cell open order, per-area
> binaries), `Docs/RE/specs/terrain-streaming.md` §3 (cell-key membership gate).
>
> **Re-verification banner.** `ida_reverified: 2026-06-27` (prior: 2026-06-26), `ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963` (CYCLE 14 re-anchor: confirmatory — 1 re-confirmed SAME),
> `evidence: [static-ida]`. Verification status: **confirmed** for all five edges documented below.
> See addendum for corrections and open edges. Open_item (pre-existing): addendum §5 / port-guidance step 2 phrase '76-byte records' — runtime stride, not on-disk size; flagged for spec-author cleanup.
> 2026-06-28 spec-author promotion: live world-entry chain, two-object world-manager model, runtime streaming policy, region/zone system, and area-binary layouts added; see §addendum-2026-06-28.

---

## Addendum 2026-06-26 — whole-map-assembly (deep-dive confirmation)

This addendum records the first deep-dive confirmation pass against `doida.exe` (IDB SHA 263bd994)
for the offline whole-map assembly question: how to enumerate all cells, place them in world space,
and resolve their textures without the runtime streaming ring. All five edges corroborate
already-committed specs; no contradictions were found.

### Confirmed facts

**1. The `d<NNN>.lst` manifest is the authoritative full cell enumeration for a map.**
The manifest-load routine consumes the path format `data/map<NNN>/dat/d<NNN>.lst`. The file is
header-less: a 4-byte u32 cell count followed immediately by that many 32-bit cell keys. The loader
reads the count, allocates `count * 4` bytes, reads all keys, and inserts each key raw into the
area's ordered cell-key set. Cell count is derivable as `(file_size - 4) / 4`. No count ceiling is
enforced — count 0 is accepted as an empty loop, and arbitrarily large counts are accepted without
rejection. This is distinct from the `bgtexture.lst` loader, which explicitly rejects count 0 or
count >= 2000. See `Docs/RE/formats/terrain.md §1.2` and `Docs/RE/formats/area_inventory.md §1A.2`.

**2. Cell key construction and inversion are exact integer divmod.**
The key formula is `key = mapZ + 100000 * mapX`, where the Z component occupies the low part and
the X component the high part. Because `mapZ = cellZ_raw + 10000` stays in the range `[0, 100000)`,
the inversion is exact: `mapX = key / 100000`, `mapZ = key % 100000` (integer divmod, no carry).
The client itself never inverts a key — it stores keys raw and recomputes the key from `(mapX, mapZ)`
for the membership gate. The inversion is therefore purely the arithmetic inverse of the construction,
used only by an offline or viewer tool. See `Docs/RE/formats/terrain.md §1.2`.

**3. Per-cell open order is fixed: `.mud` → `.gad` → `.map`, with sub-assets routed from `.map`.**
The per-cell loader takes `(mapX, mapZ, areaId)`, formats the base path
`data/map<NNN>/dat/d<NNN>x<mapX>z<mapZ>` (NNN = areaId as three digits), then opens `<base>.mud`,
`<base>.gad`, and `<base>.map` in that order. `.gad` is a no-op stub. The `.map` file is a CP949
keyword text descriptor and is the last mandatory open; the sub-assets `.ted`, `.bud`, `.sod`,
`.up`, `.exd`, and `.fx1`–`.fx7` are opened inside the `.map` parse via per-section `DATAFILE`
tokens: the section keyword routes to a decoder (`TERRAIN` → `.ted` five-block loader, `BUILDING`
→ `.bud`, `UP_TERRAIN` → `.up` + `EXTRA_TERRAIN` → `.exd` 40-byte-triangle decoder,
`FX1`–`FX7` → fx decoders, `SOLID` → `.sod`). See `Docs/RE/formats/terrain.md §3.1` and `§3.2`.

For a render-only viewer: `.map` and `.ted` are mandatory for ground geometry. `.bud` is optional
and per-cell (many cells lack it — empty buildings is not an error). `.up` / `.exd` are optional
(multi-level and extra terrain, present in a subset of areas and cells). `.fx1`–`.fx7` are optional
effect layers. `.sod` (collision), `.mud` (sound), and `.gad` (stub) are not needed for a static
visual viewer.

**4. All cells share one absolute world space; geometry is baked absolute with no per-cell transform.**
The `.ted` geometry loader folds the cell origin into every vertex: `world_X = col * 16 + (mapX - 10000) * 1024`,
`world_Z = row * 16 + (mapZ - 10000) * 1024`, `world_Y = block-1 height f32` (no scale). The grid
is 65 × 65 vertices at 16-unit spacing across a 1024-unit cell. Vertex normals decode as signed byte
divided by 127.0, with the Y (up) normal component always positive, confirming Y is the up axis. The
per-cell loader independently bakes the same cell axis-aligned bounding box using the identity
`(mapX - 10000) * 1024` to `+ 1024` bounds. An offline viewer adds all cell meshes into a shared
scene with no per-cell translate, rotate, or scale. See `Docs/RE/formats/terrain.md §5.2` and `§5.4`,
and the `WorldCoordinates` Y-up convention.

**5. There is a single global terrain texture pool loaded once from `data/map000/texture/bgtexture.lst`.**
The pool-init routine hardcodes the path `data/map000/texture/bgtexture.lst`. It reads a 4-byte
count (rejected if 0 or >= 2000), then 76-byte records, resolving each texture path as
`data/map000/texture/<name>.dds`. Each entry is dispatched as static or non-static by a kind byte
(kind == 1 = static). This single pool is shared by all areas and all cells. See
`Docs/RE/formats/bgtexture_lst.md` and `Docs/RE/formats/terrain.md §4`.

---

### Corrections to prior framing

**CORRECTION — texture pool framing (building textures):**
A map does not have a separate per-cell building texture pool. There is exactly one global texture
pool (`bgtexture.lst` under `data/map000`) shared by terrain and buildings. What is per-cell (and
per-section) is an indirection list, not a pool: each `.map` section block (`TERRAIN`, `BUILDING`,
`FX1`–`FX7`) carries its own `TEXTURES{}` list that maps a section-local 1-based selector to a
global pool index (`intTexId`). Terrain tiles index the `TERRAIN` list (via the `.ted` block-3 byte);
building meshes index the `BUILDING` list (via the `.bud` `tex_id`); both lists then resolve into the
single global pool. Correct framing: one global pool + per-cell/per-section index lists, not two
separate pools.

**CORRECTION — `.mud` open order and tolerability:**
`.mud` is opened first (before `.gad` and `.map`), and its load result is checked, but a missing
`.mud` is tolerated silently — it is not a hard error. Twenty areas ship with zero `.mud` files and
still render. For an offline visual viewer, `.mud` and the `.gad` stub are irrelevant and should be
skipped entirely.

**CORRECTION — missing `terrain_scene.md`:**
The path `terrain_scene.md` referenced in passing by `terrain.md` and `area_inventory.md` does not
exist in the committed corpus. The committed authorities for the questions it would address are
`Docs/RE/specs/terrain-streaming.md`, `Docs/RE/formats/area_inventory.md §1A`,
`Docs/RE/formats/terrain.md`, and `Docs/RE/formats/bgtexture_lst.md`. Use those instead.

---

### Still-open edges

**`map<NNN>.bin` field layout (out of scope for placement):**
`map<NNN>.bin` is not a cell-placement input — cell placement derives entirely from `(mapX, mapZ)`
→ baked `.ted` vertex positions; neither the per-cell nor `.ted` loaders consult any per-map
descriptor for placement. `map<NNN>.bin` is a fixed 520-byte per-area block opened by the area
orchestrator before any cell streams (area metadata: spawn, region, and option data, per
`Docs/RE/formats/area_inventory.md §1A.3`). Its exact internal field layout was not re-walked
this pass (out of scope for placement). A whole-map viewer does not need it to assemble or
place cells.

**Quad triangulation diagonal and UV-flip interaction (partial):**
The `.ted` block-4 direction byte's two LSBs are confirmed as S-flip (0x01) and T-flip (0x02) for
UV orientation. Whether a flip combination also re-selects the two-triangle quad-split diagonal
(versus only re-orienting the texture) is not fully settled — see `Docs/RE/formats/terrain.md §5.7`.
The ground-height sampler uses per-triangle plane interpolation keyed off that diagonal (confirmed in
CYCLE 12). For a pure visual mesh viewer this only affects the diagonal split direction of each quad;
it does not affect cell placement.

**Cell-set anomalies a whole-map loader must tolerate:**
Area 0's `.lst` contains a duplicate key (two entries for the same key) — deduplicate the key set
before building geometry. Areas 2, 21, and 47 each have one extra `.map` / `.ted` / `.sod` cell
whose key is not present in the `.lst` — a key-driven loader simply never loads it, which is
correct. A linear `.lst` iterator must guard against loading the same cell twice. These anomalies
are cataloged in `Docs/RE/formats/area_inventory.md §4`.

---

### Port guidance

Whole-map offline loader (load ALL cells, not the streaming ring) — implemented in
`Assets.Vfs` / `Assets.Parsers` + `Assets.Mapping`:

1. **Enumerate cells.** Open `data/map<NNN>/dat/d<NNN>.lst`. Read the u32 count; read that many u32
   keys; deduplicate the key set. Invert each key with `mapX = key / 100000`,
   `mapZ = key % 100000` (integer divmod; sound for all live cells). Do not validate the count
   against a ceiling — the client does not.

2. **Global texture pool (once per session).** Load `data/map000/texture/bgtexture.lst` (count guard
   1–1999; 76-byte records) into one global pool keyed by pool index. Resolve paths via the
   `bgtexture.txt` companion (col0 index → col2 relative path → `data/map000/texture/<relpath>.dds`).
   This pool is shared by terrain and buildings for every area.

3. **Per cell, for every key in the deduplicated set.**
   - Open `<base>.map` only (skip `.mud` and `.gad`).
   - Parse sections. For `TERRAIN`: open the `DATAFILE` `.ted`; build the cell mesh by baking
     absolute world positions per vertex: `X = col * 16 + (mapX - 10000) * 1024`,
     `Z = row * 16 + (mapZ - 10000) * 1024`, `Y = block-1 height f32` (no scale); normals =
     block-2 i8 / 127.0 (Y-up); tile texture = `global_pool[ TERRAIN_TEXTURES[ (texidx_byte
     clamped to [1, count]) - 1 ].intTexId ]`.
   - For `BUILDING` (optional): open the `DATAFILE` `.bud`; resolve its `tex_id` via
     `global_pool[ BUILDING_TEXTURES[ tex_id - 1 ].intTexId ]`. A missing `.bud` is an
     empty-buildings cell, not an error.
   - `UP_TERRAIN` / `EXTRA_TERRAIN` (optional): decode through the 40-byte-triangle decoder.
   - `FX1`–`FX7`: optional effect layers.

4. **Place at identity.** Vertex positions are already absolute world-space. Add all cell meshes
   into one shared scene with no per-cell translate, rotate, or scale and no recentering. Apply the
   project's world convention at the engine boundary only (`WorldCoordinates.ToGodot` negates Z:
   `(x, y, z) → (x, y, -z)`). Y is up.

5. **Not needed for static visual render.** `map<NNN>.bin`, `.lst`-only unloaded cells, the `.gad`
   stub, `.mud`, and `.sod` are not required to render the static whole map.

**Handoff:** the five confirmed edges all corroborate already-committed specs with no contradictions.
A spec-author should promote the new "offline whole-map assembly" subsection (load-all-cells, identity
placement, key inversion, deduplication) as a new subsection in `Docs/RE/formats/area_inventory.md §1A`
or `Docs/RE/formats/terrain.md §1.4`, plus the framing correction that building textures share the
single global `bgtexture.lst` pool via a per-section index list, not a separate pool. After promotion,
hand off to `assets-engineer` (`Assets.Vfs` / `Assets.Parsers`) and `godot-world-engineer` for the
viewer implementation.

---

## Addendum 2026-06-28 — Live world-entry chain, world-manager model, streaming policy, region system

This addendum extends the offline-assembly focus of §addendum-2026-06-26 with the live runtime: how
the client enters and assembles a map area during active play, the two-object world-manager
architecture, the runtime cell-streaming policy, the zone/region system, and the scene-graph wiring.
All facts are `[static-confirmed]` from IDA analysis of `doida.exe` (IDB SHA f61f66a9) except items
explicitly marked `[debugger-confirm]` or `[static-hypothesis]`.

### §1 Overview

The offline load-all path (§addendum-2026-06-26) enumerates every key in `d<NNN>.lst` and loads
every cell once into a flat list, without a streaming ring. The live runtime uses a complementary
approach: a **fixed 34-cell pool** and a **streaming ring** (3×3 or 5×5 depending on graphics
quality) centred on the local player. Two cooperating singletons manage the world:

- **`TerrainManager`** (outer) — owns the 5×5 active cell-pointer grid, the stream radius, two
  culling frustums, the nine geometry and effect-layer builders, and the texture pool.
- **`TerrainLoader`** (inner / cell-streamer) — owns the fixed 34-cell pool, the focus-cell
  coordinates, and the cell-key membership set populated by the `d<NNN>.lst` manifest.

An async worker-thread streamer (`Terrain_StreamWorkerThread`) is constructed but intentionally left
dormant: the keep-running flag is cleared during initialisation. All cell disk-loads therefore happen
synchronously on the main thread, causing a brief frame hitch when the player crosses a cell
boundary. `[debugger-confirm]`

### §2 Live world-entry orchestration

#### §2.1 Entry points into `Env_MapSetAndLoadArea`

Four call sites drive the world-entry sequence:

| Caller | Trigger |
|---|---|
| `Map_SelectAreaByDateTime` | Explicit area identifier with calendar/time-of-day clock seed |
| `SelectWindow_BuildScene` | Character-select scene construction |
| *(unrecovered scene/state transition function)* | Scene or game-state transition path |
| `LocalMapTable_LoadAreaByMapId` | Network-driven map change (enter-world or warp) |

#### §2.2 `Env_MapSetAndLoadArea(areaId, t1, t2, t3)` — ordered world-entry sequence

1. Reject `areaId < 0`; abort.
2. Write the new area identifier to the global current-area variable (`g_EnvCurrentAreaId`).
3. Call `MapSetting_GetRecordByMapId(areaId)` to populate `g_CurrentMapSettingRecord`. If
   `areaId ≠ 0` and no record is found, abort.
4. Format the 3-digit area string NNN from the hundreds, tens, and units digits of `areaId`
   (e.g. area 42 → `"042"`). This NNN is substituted in every `map<NNN>` path for the remainder
   of this sequence.
5. Call `AreaInventory_LoadLst(TerrainLoader, NNN)` — load `data/map<NNN>/dat/d<NNN>.lst` into the
   loader's cell-key membership set. Abort on failure.
6. Call `EnvTime_Set` to seed the world clock from `(t1, t2, t3)` — sets lighting and sky timing
   only; does not select the map.
7. Reset environment state flags for the new area (stardome enable, cloud-dome enable,
   map-specific option flags).
8. Call `MapOption_LoadBin()` to load the per-area map-option blob into `g_MapOptionBlock`.
9. Call **`Map_LoadAreaBinaries()`** — load the four per-area binary files (see §4).
10. Apply lens-flare enable/disable from a map-option flag.
11. Call `SoundTable_LoadFiveTables()` to load area sound tables.
12. Retrieve the `TerrainManager` singleton and configure it for this area: apply the loaded
    map-option flags and invoke `TerrainManager_SelectStreamRadiusByQuality` to set the stream
    radius, ring size, and the coupled culling and fog parameters (see §5).
13. Call `Weather_InitFromBin`, `SkySystem_Init`, and `Wind_LoadMapData` to initialise weather,
    sky, and wind for the new area.

#### §2.3 `Map_SelectAreaByDateTime` — calendar wrapper

This entry point computes a clock seed from a calendar date before calling
`Env_MapSetAndLoadArea`. It calculates a day-index from year, month, and day (30 days/month,
12 months/year) and a second-index from hour and minute. It validates that month < 13, day < 31,
hour < 24, and minute < 60. The `areaId` is passed through unchanged — **the date computation is
purely a clock seed; it does not select the map.** This confirms the offline-assembly framing that
date and map identity are independent parameters.

### §3 Two-object world-manager architecture

#### §3.1 `TerrainManager` (outer singleton)

Constructed by `TerrainManager_ctor`. Holds the active rendering state and drives the streaming
ring. Key fields:

| Byte offset | Field | Notes |
|---|---|---|
| +0 | Pointer to inner `TerrainLoader` | Stored at construction |
| +4 | Terrain texture pool owner | Initialised by `TerrainPool_InitFromBgtextureLst` from `bgtexture.lst` |
| +8 | Ground geometry builder (~1.3 MB allocation) | Initialised in constructor |
| +12 | Building geometry builder (~240 KB allocation) | Initialised in constructor |
| +16..+40 | Effect layer managers Fx1–Fx7 (7 slots, varied allocations ~38–50 KB each) | Initialised in constructor |
| +44..+143 | 25-slot active cell-pointer grid (5×5 flat array); center slot at +92 | Zeroed in constructor; ring functions index by column and row offset from center |
| +144 | Current area identifier | Set during ring operations; passed to `Terrain_LoadCellFiles` |
| +148 | First culling frustum (GPolytope, terrain draw-cull) | Rebuilt in `Terrain_SetStreamRadius` |
| +252..+268 | Cached camera parameters fed to the first culling frustum | Read by `Terrain_SetStreamRadius` |
| +356 | Stream radius (float) | Written by `Terrain_SetStreamRadius`; > 1000 selects 5×5 ring, ≤ 1000 selects 3×3 |
| +360 | Second culling frustum (GPolytope) | Initialised in constructor |
| +464 | Map-option flag A | Set by area-configuration; read by `Terrain_TickCellsAroundPlayer` |
| +468 | Map-option radius override | Set by area-configuration; read by `TerrainManager_SelectStreamRadiusByQuality` |
| +472 | Grid-mode flag (0 = high/5×5, 1 = medium or low/3×3) | Written by radius-quality selection; constructor default: 1 |
| +476 | Cells-per-side hint (5 for high quality, 4 otherwise) | Written by radius-quality selection; constructor default: 4 |

#### §3.2 `TerrainLoader` (inner singleton / cell-streamer)

Constructed by `TerrainLoader_ctor`, initialised by `TerrainLoader_init`. Stored at
`TerrainManager+0`. The function that constructs and returns this singleton was initially annotated
in the IDB under another subsystem's name; the correct canonical name is `TerrainLoader_GetSingleton`
— flagged for `names.yaml` update to the RE domain.

| Byte offset | Field | Notes |
|---|---|---|
| +0 | Worker thread descriptor (`Diamond::Thread`) | Thread function: `Terrain_StreamWorkerThread` |
| +12 | Keep-running flag (byte) | Set 1 in constructor; cleared to 0 in `TerrainLoader_init` — thread remains dormant |
| +16 | Stream FIFO queue | Initialised in constructor |
| +28 | Load event handle | Created by `CreateEventA` in `TerrainLoader_init` |
| +32 | Cell pool count | Fixed value: **34** |
| +36 | Cell pool base pointer (34 × `TerrainCell*`) | Populated by init loop; iterated by `Terrain_FindFreeCellSlot` and `TerrainManager_MarkDistantCellsForEvict` |
| +176 | Focus cell map-X | Written by `SetFocusCell` |
| +180 | Focus cell map-Z | Written by `SetFocusCell` |
| +184 | Cell-key membership set root (`std::set`) | Filled by `AreaInventory_LoadLst`; checked by `Terrain_FindOrLoadCell` as the membership gate |
| +188 | Cell-key membership set end sentinel | Paired with root at +184 |

**Pool sizing rationale:** 34 = 25 (maximum active 5×5 window) + 9 spare — sufficient headroom to
load a new edge row or column before evicting the departing cells during a one-cell ring shift.

### §4 Per-area binary layouts — `Map_LoadAreaBinaries`

`Map_LoadAreaBinaries` opens four per-area binary files by substituting NNN into static path
templates. Each binary load is preceded by `Area_FreeGeometryAndNpcArr` to release any prior area
data. Any open or read failure logs an error and aborts the entire area load.

| File template | Fixed size | Destination | Notes |
|---|---|---|---|
| `map<NNN>.bin` | 520 bytes | Per-area map metadata block | Area metadata (spawns, region parameters, option data); internal field schema: open item — see §10.1 |
| `regiontable<NNN>.bin` | 1536 bytes (32 × 48) | `g_RegionTable` | 32 zone records × 48 bytes; post-load zone-anchor unpack (see §6.2) |
| `region<NNN>.bin` | variable | `g_RegionGridBuffer` | Layout: u32 width, u32 height, (width × height) bytes zone-index grid, i32 originX, i32 originZ |
| `npc<NNN>.arr` | variable (28-byte records) | `g_NpcSpawnArray` | 28-byte NPC spawn records; record 0 is a zeroed sentinel; parallel index array at `g_NpcArrIndex` |

**`npc<NNN>.arr` record count:** `fileSize / 28` (integer division) + 1 for the sentinel at
index 0. The processing loop begins at index 1; the sentinel is never treated as a live spawn.

**No `mob<NNN>.arr` at runtime:** mob spawns arrive via network packets.
`mob<tag>.arr` files are authoring-side records only and are not loaded at runtime.

### §5 Runtime streaming and sector policy

#### §5.1 Quality → radius → ring size

`TerrainManager_SelectStreamRadiusByQuality` reads a quality value from a settings singleton
(quality field at offset +5 within that singleton; values: 1 = high, 2 = medium, other = low;
`[debugger-confirm]` for the UI path and exact semantics) and selects:

| Condition | Stream radius (units) | Active ring | Grid-mode / cells-per-side flags |
|---|---|---|---|
| Map-option radius override active | override value | > 1000 → 5×5; ≤ 1000 → 3×3 | 1 / 4 |
| Quality = high (setting 1) | **1800** | **5×5** (radius > 1000) | 0 / 5 |
| Quality = medium (setting 2) | **1000** | **3×3** (radius ≤ 1000) | 1 / 4 |
| Quality = low (all other settings) | **600** | **3×3** | 1 / 4 |

#### §5.2 Radius coupling — stream, cull, and fog

`Terrain_SetStreamRadius` writes the radius into `TerrainManager+356`, clamps it to a maximum of
1000 if the supplied value is ≥ 15000 (a far-plane guard), then:
- Rebuilds the terrain culling frustum (`GPolytope_BuildSixPlanes`) using the clamped radius as the
  far-plane distance.
- Sets the fog far distance to match (`Fog_ApplyForViewDistance`).

**Stream radius, terrain-cull far plane, and fog far distance are a single coupled value.** One call
to `Terrain_SetStreamRadius` sets all three simultaneously.

#### §5.3 Ring recenter and eviction algorithm

`TerrainManager_RingShift3x3_PerFrame` (3×3 path, assertion at `terrainmanager.cpp:443`) and
`TerrainManager_ShiftRingByOneCell` (5×5 path, assertion at `terrainmanager.cpp:631`) implement the
ring scroll as follows:

1. Compute the player's cell coordinates from world XZ by integer division by 1024 (with
   floor-toward-negative-infinity correction for negative coordinates), then add 10000 to obtain
   map-space identifiers. This matches the `(mapX − 10000) × 1024` bake used in §addendum-2026-06-26.
2. Compute the difference between the player's cell and the ring's center cell. If zero in both
   axes, return immediately. Otherwise **assert that the difference is at most ±1 in each axis** —
   guaranteed because the recenter runs every player-move frame.
3. Call `SetFocusCell(TerrainLoader, newCenterX, newCenterZ)` to update focus coordinates.
4. For each new edge cell entering the ring: call `Terrain_FindOrLoadCell` (membership-gate check →
   fast-path lookup via `Terrain_FindLoadedCellByCoords` → slot acquisition via
   `Terrain_AcquireSlotAndLoadCell` if not already loaded), then
   `TerrainCell_AddRefAllLayerTextures`.
5. Rotate the cell-pointer grid (shuffle the 25-slot flat array); call
   `TerrainCell_FlushDirtySubtiles` on departing edge cells.
6. Rebuild active texture pools: `GroundTexturePool_RebuildActiveList` and equivalent rebuilds for
   Fx1–Fx7.
7. Call `TerrainManager_MarkDistantCellsForEvict`: iterate all loaded pool cells; for any cell
   whose map-X or map-Z distance from the focus exceeds ±2, clear its in-use byte
   (cell+24708 = 0) making it a free pool slot. **Eviction keep-window: ±2 cells in each axis
   (a 5×5 eviction box).**
8. Update ambient sound from the center cell's world-Z coordinate.

**Synchronous load:** `Terrain_AcquireSlotAndLoadCell` enters a critical section, calls
`Terrain_FindFreeCellSlot` (logs `"terrain empty error"` if all 34 pool slots are in use), calls
`Terrain_LoadCellFiles`, sets the in-use byte to 1, then exits the critical section. Because the
keep-running flag was cleared in init, the async worker never runs — these loads are synchronous on
the main thread (frame hitch on cell crossing). `[debugger-confirm]`

### §6 Region / zone system

#### §6.1 `region<NNN>.bin` — world-to-zone-index grid

`region<NNN>.bin` encodes a world→zone-id lookup grid at **256-unit resolution** (a 4×4 sub-grid
per 1024-unit terrain cell). This is the safe-zone / PK / combat-mode data — **not collision**
(the `.sod` files handle collision; see `Docs/RE/formats/terrain.md`).

Binary layout:

| Field | Type | Notes |
|---|---|---|
| width | u32 | Grid columns |
| height | u32 | Grid rows |
| grid data | `width × height` bytes | One byte per grid cell = zone index 0..31 |
| originX | i32 (signed) | World X coordinate of the grid's (0, 0) corner |
| originZ | i32 (signed) | World Z coordinate of the grid's (0, 0) corner |

**`RegionGrid_LookupIdByWorldXZ(worldX, worldZ)` lookup formula:**
`index = (worldX − originX) / 256 + (worldZ − originZ) / 256 × width`
Returns `grid[index]` as an unsigned byte — a zone index 0..31, **not** a 0/1 collision flag.
Lookup is bounds-checked against the total grid cell count. Origins are signed; grid is row-major.

#### §6.2 `regiontable<NNN>.bin` — zone record table

32 records × 48 bytes = 1536 bytes total.

| Field within record | Confirmed content |
|---|---|
| Byte +40 (0x28 from record start) | Zone type; observed values: {0, 1, 2, 9} |
| Remaining fields | Uncharted `[static-hypothesis]` — likely zone center/extent, name identifier, flags |

**`RegionTable_GetRecord(idx)`** returns a pointer to record `idx` (idx < 32) within `g_RegionTable`
(stride 48 bytes). A minimap-specific variant `RegionTable_GetRecord_Minimap` is used for
map-overlay rendering.

**Zone-anchor unpack (post-load):** after loading the 1536-byte region table, a loop extracts a
16-entry zone-anchor array. For each of the 16 anchor slots, two consecutive float values at a
fixed offset within the loaded data are read and stored as a float3 `(x, 0.0, z)` with byte stride
12 into a 192-byte zone-anchor block (16 × 12 bytes). These zone anchors serve as minimap and
region-marker positions.

#### §6.3 Combat-mode / safe-zone lookup chain

`Region_ResolveCombatMode` (called from the player update path) implements the full chain:
world `(X, Z)` → `region<NNN>.bin` grid (256-unit cells) → zone index 0..31 →
`regiontable<NNN>.bin` 48-byte record → zone type → safe-zone / PK / combat-mode decision.

### §7 `TerrainCell` object layout

Each `TerrainCell` is constructed by `TerrainCell_ctor` and populated by `Terrain_LoadCellFiles`.
Allocation size: `new(0x6088)` = **24712 bytes**.

| Byte offset | Field | Notes |
|---|---|---|
| +16252 (0x3F7C) | Map-X identifier | = 10000 + cellX |
| +16256 (0x3F80) | Map-Z identifier | = 10000 + cellZ |
| +16260 (0x3F84) | Area identifier | Owning area |
| +16264 (0x3F88) | World AABB X0 (float) | `(mapX − 10000) × 1024` |
| +16268 (0x3F8C) | World AABB Z0 (float) | `(mapZ − 10000) × 1024`; also the ambient-sound Z parameter |
| +16272 (0x3F90) | World AABB X1 (float) | X0 + 1024 |
| +16276 (0x3F94) | World AABB Z1 (float) | Z0 + 1024 |
| +16280..+16292 | Second AABB copy (X0, Z0, X1, Z1) | Identical baked copy |
| +16296 | Ground texture-usage grid (terrain slot 0) | Reset by ground-texture grid initialiser |
| +16300 | Mass-object (building) geometry grid (slot 1) | — |
| +16304..+16328 | Effect layer grids Fx1–Fx7 (slots 2–8) | — |
| +16332 | Extra-decal layer 16×16 subtile grid | Reset by extra-decal grid initialiser |
| +20436 | Up-terrain layer 16×16 subtile grid | Reset by up-layer grid initialiser |
| +24540 | GAD buffer slot (stub) | No content written |
| +24544 | MUD grid buffer (32768 bytes) | Loaded by `Mud_LoadGrid` |
| +24708 (0x6084) | In-use / active byte | 1 = loaded and active; 0 = free or evictable |

**Cell path template (base):** `data/map<NNN>/dat/d<NNN>x<mapX>z<mapZ>`.
Files opened in fixed order: `<base>.mud` → `<base>.gad` (no-op stub) → `<base>.map`.

### §8 Scene wiring and per-frame drivers

#### §8.1 `Renderer_InstallWorldRenderPasses`

Called by `MainHandler_BuildInGameSceneGraph` during in-game scene setup. This routine:
- Caches the `TerrainManager` singleton (and other world-subsystem singletons) into the
  render-globals block, making them accessible to all render passes.
- Registers the terrain render-pass callback (`RenderPass_WorldTerrainAndBuildings`) into the
  scene object's render-pass slot array.

The terrain subsystem is only wired into the active render graph after this call.

#### §8.2 Per-frame update hooks

Two distinct per-frame hooks drive the terrain runtime:

| Hook function | Called from | Role |
|---|---|---|
| `TerrainManager_RingShift3x3_PerFrame` / `TerrainManager_ShiftRingByOneCell` | `Actor_LocalPlayerUpdateAndRecenterRing` ← `ActorManager__VFunc_02` (player update loop) | When the local player moves into an adjacent cell, scroll the ring: load new edge cells, rotate the cell-pointer grid, flush departing cells, rebuild active texture pools, evict distant cells, update ambient sound. |
| `Terrain_TickCellsAroundPlayer` | `RenderPass_WorldTerrainAndBuildings` (the terrain render pass) | Per frame, walk a ring around the player at 64-unit steps; for each currently-loaded cell in the 3×3 window, touch its ground and object subtile grids (sub-tile streaming and LOD resolution). |

The ring recenter executes inline on the **main thread** during the player-movement update (only
when the actor is the local player and is mid-move). The subtile touch executes during the terrain
render pass, also on the main thread.

### §9 Area teardown — `Area_FreeGeometryAndNpcArr`

Called at the start of every `Map_LoadAreaBinaries` invocation and on any load failure. Performs:
- Zeroes the per-area map metadata block (520 bytes).
- Zeroes the zone-anchor float3 array (192 bytes).
- Zeroes `g_RegionTable` (1536 bytes).
- Frees `g_RegionGridBuffer` (the `region<NNN>.bin` zone-index grid buffer).
- Zeroes the region-grid origin and dimension block (24 bytes).
- Frees `g_NpcSpawnArray` and `g_NpcArrIndex`.

**Cell objects are NOT freed here.** The 34-cell pool in `TerrainLoader` persists for the process
lifetime. Cells are recycled by clearing their in-use byte via `TerrainManager_MarkDistantCellsForEvict`.

### §10 Open items from this addendum

**§10.1 `map<NNN>.bin` internal field schema** `[open]`
The 520-byte per-area metadata block is loaded into a global fixed buffer and read by consumers at
specific byte offsets. The per-field layout (spawn counts, region parameters, option flags, and
related metadata) has not been walked in detail. A consumer xref walk is required.

**§10.2 `regiontable<NNN>.bin` full record schema** `[static-hypothesis]`
Confirmed: 32 records × 48 bytes; zone-type field at record byte +40 with values {0, 1, 2, 9};
16-entry zone-anchor unpack. Remaining per-record fields (likely zone center/extent, name identifier,
flags) require a per-field decode pass.

**§10.3 Graphics-quality global** `[debugger-confirm]`
The quality value is read from a settings singleton at field offset +5 (values: 1 = high,
2 = medium, other = low). The settings UI path and live confirmation of exact value semantics are
deferred to the live debugger.

**§10.4 Live streaming confirmation** `[debugger-confirm]`
Runtime confirmation required: actual stream radii (1800 / 1000 / 600) as applied on the
maintainer's machine, the 5×5-vs-3×3 branch actually taken, and direct observation that the async
worker thread is never scheduled.

**§10.5 Synchronous cell-load hitch** `[debugger-confirm]`
Static analysis indicates cell loads run synchronously on the main thread during the player-movement
update. A live session crossing a cell boundary would confirm the hitch and allow it to be observed
and timed.

**§10.6 `Terrain_FindLoadedCellByCoords` fast-path** `[low-priority]`
The in-ring fast-path lookup (cell-coords → active grid slot match, before pool-slot acquisition)
was not walked in detail. Low impact on correctness.

### §11 Confidence and resolution of prior items

All facts in §§1–9 are `[static-confirmed]` except items flagged in §10. The two-object manager
model, the 34-cell pool, the quality → radius → ring policy, the ±2 eviction keep-window, the
region grid (256-unit / zone-index semantics), and the world-entry chain are all directly read from
`doida.exe` (IDB SHA f61f66a9).

**Resolved:**
- `region<NNN>.bin` purpose: fully documented in §6. The file was undocumented in
  §addendum-2026-06-26 (not flagged as a still-open item but simply uncovered).
- Cell-key membership gate consistency: `Terrain_FindOrLoadCell` checks the key-set filled by
  `AreaInventory_LoadLst`, confirming the §addendum-2026-06-26 observation that cells with keys
  absent from the `.lst` are silently never loaded.
- Cell-set anomalies (area 0 duplicate key, areas 2/21/47 extra cells): consistent — the
  membership-gate architecture fully explains both anomaly types.

**Still open from §addendum-2026-06-26:**
- `map<NNN>.bin` internal field schema: still open (see §10.1 above).
- Quad-triangulation diagonal and UV-flip interaction: unrelated to this addendum; still open.
  See `Docs/RE/formats/terrain.md §5.7`.
