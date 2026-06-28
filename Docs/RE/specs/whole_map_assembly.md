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
