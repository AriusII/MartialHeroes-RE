# Spec: Asset Linkages — how the data.vfs formats connect into working subsystems

> Clean-room spec. Neutral, firewall-clean synthesis — NO sample bytes, NO addresses, NO
> decompiler pseudo-code. Implementation (Assets.Vfs / Assets.Parsers / Assets.Mapping +
> Client.Application world loaders) reads only specs like this one.
>
> verification: cross-format synthesis — derived from the per-format clean specs under
>               `Docs/RE/formats/` and the firewall-clean findings promoted from the dirty-room
>               re-verification pass. Every individual link below is documented (and
>               sample/parser-verified) in its source format spec; this file is the JOIN map that
>               stitches them into the six working subsystems.
> ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory — six asset-join subsystems / msg.xdb / bgtexture strings cleanly relocated, 1 re-confirmed SAME, 0 corrected; prior 2026-06-21: CYCLE 8 six-chain coherence re-confirmed, world-entry replay closed in resource_pipeline.md
> ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> evidence: [static-ida, vfs-sample] (per-format), [cross-format-synthesis] (this doc)
> conflicts: none unresolved. Where a hint in the synthesis brief disagreed with the binary (e.g.
>            ".ion descriptor", ".sod.pre as a runtime input"), the binary wins and the correction
>            is recorded inline.
> consolidated: 2026-06-29   # absorbed Docs/RE/vfs/linkage_and_usage.md; all five index manifests
>               (bgtexture.lst §5, skin.txt/actormotion.txt §2, xeffect.lst/bmplist.lst §3,
>               uitex.txt §4.1, msg.xdb §4.2) confirmed present; unanchored consumer topology
>               folded §10 as static-hypothesis/unverified at f61f66a9

---

## 0. How to read this document

This spec answers one question: **given the raw files inside `data/data.vfs`, how do they reference
one another, by which join key, and which runtime builder assembles them into a working subsystem?**

It is organized into six subsystems:

1. The per-cell terrain bundle (`data/map<NNN>/dat/d<NNN>x<X>z<Z>.*`)
2. The character chain (`.skn` ↔ skin tables ↔ `.bnd` ↔ `.mot` ↔ actor tables)
3. The effects chain (`xeffect.lst` → `.xeff` → `.xobj` / effect textures)
4. The UI chain (`uitex` → `.dds`, `msg.xdb`, `.do`, `.mi`)
5. The global texture pool (`bgtexture.lst` → `data/map000/texture/*.dds`)
6. The sound chain (per-area sound tables → `data/sound/{2d,3d}/<id>.ogg`)

Each link is written as **source format → referenced format/table — JOIN KEY — runtime builder**.

### Universal conventions (true across every chain below)

- **Endianness** is little-endian everywhere; no binary asset carries a magic/version header except
  the `.xeff` anti-magic guard (rejects a leading `XEFF`) and the dead `BANI` `.mot` variant.
- **Path tokens** are CP949/ASCII; all game text is CP949. Fixed-width name records are NUL-terminated
  and zero-padded to the field width.
- **The VFS is the chokepoint.** Every loader opens by lowercased path through the same VFS find-and-read
  routine (binary-searched TOC; see `formats/pak.md`, `specs/vfs_overview.md`). When the VFS is not
  mounted, the identical loaders fall back to loose disk. No loader appends or rewrites a file extension
  — a file is opened only if some caller passes its exact path string.
- **Cell world coordinates.** A terrain cell `(mapX, mapZ)` is biased at 10000; its world origin is
  `((mapX − 10000)·1024, (mapZ − 10000)·1024)` and one cell spans 1024 world units (65×65 vertex grid,
  16-unit spacing). All cell geometry (`.ted/.exd/.up/.bud/.sod`) stores **absolute world** coordinates.
- **The "−1" base-offset rule.** Texture index joins are 1-based: a stored index `N` resolves to list
  slot `N − 1`. This applies to `.ted` texidx, the `.bud` per-object tex_id, and (one level up) the
  per-cell TEXTURES list value into the global `bgtexture` pool.
- **Godot port coordinate caveat (port concern, not the original's).** World geometry negates Z
  (`WorldCoordinates.ToGodot`: `(x,y,z)→(x,y,-z)`); mesh-local `.skn` geometry negates X. Collision
  (`.sod`) and supplementary surfaces (`.up`/`.exd`) must use the same world-Z negation as terrain so
  walls and floors line up. These are conversions applied by the port; the original solves everything in
  native world space.

### Cross-cutting fact — authoring sidecars that the client NEVER loads

Several extensions present in the VFS are **tool/preprocessor output that the shipped client never
opens** (no path literal, no extension rewrite reaches them). They must NOT have a runtime parser wired
in `Assets.Parsers`; document them only as preservation/informational. The full inventory:

| Sidecar | Shadows / source of | Status |
|---|---|---|
| `.ted.post` | the cell `.ted` (post-processed, baked-lighting twin; identical 5-block layout) | written by the terrain editor; never read |
| `.sod.pre` | the cell `.sod` (precomputed 2D-XZ polygon outline of each solid) | authoring outline; never read |
| `.bud.pre` | the cell `.bud` (pre-bake mesh source) | authoring; never read |
| `.fx<n>.pre` | the cell `.fx<n>` effect layers | authoring; never read |
| `.exd` | (no sidecar — but **no loader exists in this build**; loaded only via `.map`, see §1) | see §1.4 |
| `.tol` | per-area fine 256×256 walk grid; shares origin with `region.bin` | tool-side; never read |
| `descript.ion` | art-source texture descriptor beside the effect `.tga` files | tool metadata; never read |
| `.mi` (`mobinfo.mi`) | mob-info table | packed-but-dead; never read |
| `mob<NNN>.arr` | monster spawn array (20-byte records) | only a `tool/mob/` literal exists; no client loader |
| `bgtexture.txt` / `bmplist.txt` / sky `*.txt` (e.g. `wind<N>.txt`) | the text twins of the loaded `.lst`/`.bin` | authoring sidecars; never read (the `.lst`/`.bin` is the runtime artifact) |
| `actor_size.xdb` | small `.xdb` table | path constant present but **never passed to a loader** — fully dead |

The corollary: where a `.txt` twin and a `.lst`/`.bin` exist for the same data, **the binary one is the
runtime artifact**. The one important exception is the character text tables (`skin.txt`,
`actormotion.txt`, `bindlist.txt`, `motlist.txt`, …), which ARE loaded (§2) — those have real loaders.

---

## 1. The per-cell TERRAIN BUNDLE

**Files:** `data/map<NNN>/dat/d<NNN>x<X>z<Z>.{map, ted, exd, up, bud, fx1..fx7, sod, mud, gad}`
plus the per-area cell manifest `data/map<NNN>/dat/d<NNN>.lst`, and the authoring sidecars
`.ted.post / .sod.pre / .bud.pre / .fx<n>.pre`.

**Source format specs:** `formats/terrain.md` (`.ted`/`.map`), `formats/terrain_scene.md` (`.bud`),
`formats/cell_exd.md`, `formats/cell_up.md`, `formats/sod.md`, `formats/mud.md`, `formats/authoring_sidecars.md`,
`formats/bgtexture_lst.md`, `specs/terrain-streaming.md`,
`specs/area_inventory.md` / `formats/area_inventory.md`.

### 1.1 The cell key — the join that ties every cell file together

Every cell file shares one **join key**: the cell tuple `(mapId, cellX, cellZ)`, encoded into the
filename base `d<NNN>x<X>z<Z>`. The terrain streamer builds this base path once and loads the cell's
sub-files from it; the `.map` then names the rest by their `DATAFILE` tokens.

### 1.2 The area cell manifest — `d<NNN>.lst`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `d<NNN>.lst` (u32 count + u32 cell-key array) | the set of cells that exist in the area | the opaque u32 **cell-key** (per the `.ted`/`.lst` formula `key = cellZ + 100000·cellX`) | the area-load path (`Env_MapSetAndLoadArea` → area-inventory `.lst` loader) |

`d<NNN>.lst` is a flat u32-count header followed by an array of u32 cell-keys, no validation. It is the
**resident-cell set** for the area: it declares *which* cells the area owns; the streamer pages each
cell's geometry in/out on demand around the camera. (See `specs/terrain-streaming.md` for the ring
buffer / page-in mechanics.)

### 1.3 The cell assembler — `.map` is the entry point

The per-cell **`.map`** is plain CP949 TEXT (CRLF + TAB), section-keyword grammar. It is the descriptor
that names and routes every other cell file. The terrain streamer loads the three fixed sidecars
directly from the base path **in order** — `.mud`, then `.gad`, then `.map` — then the `.map` parser
walks its sections and opens each section's `DATAFILE`, dispatching to that format's decoder. After all
sections parse, a **finalize tail** runs the per-section runtime builders.

| `.map` section | `DATAFILE` target | Decoder | TEXTURES list? |
|---|---|---|---|
| `TERRAIN` | `.ted` | terrain-geometry loader (5 fixed blocks) | yes — per-cell TERRAIN texture list |
| `EXTRA_TERRAIN` | `.exd` | triangle-surface decoder (40-byte records) | (usually none) |
| `UP_TERRAIN` | `.up` | triangle-surface decoder (same format as `.exd`) | (usually none) |
| `BUILDING` | `.bud` | building-mesh loader | yes — per-cell BUILDING texture list (cap 128) |
| `FX1`..`FX7` | `.fx1`..`.fx7` | effect/water layer decoders (FX3/FX5 = water) | yes — per-FX texture list |
| `SOLID` | `.sod` | collision-blob loader | (none) |

**The `.map` TEXTURES line grammar** (same on all sections): each data line is `intFlag intTexId
"<artist/path>"`. The first int is only a positivity guard (`>0` = data line, else end-of-block); the
**second int (`intTexId`) is the value registered** into that section's per-cell list, in registration
order (slot 0 first); the quoted artist path is consumed and discarded. So a section builds an ordered
per-cell list of global `bgtexture` pool ids.

**Geometry directives** (`WIDTH/HEIGHT/GRID/ORIGIN/MAX_HEIGHTFILED/MIN_HEIGHTFILED`) appear in the
`TERRAIN` block on disk but are **NOT consumed** by the runtime `.map` parser — the 65×65 / 16-unit grid
is hard-baked in the `.ted` loader.

### 1.4 The `.exd` / `.up` caveat (binary-won)

Important and easy to get wrong: in this build there is **no dedicated loader keyed on the `.exd` /
`.up` / `.bud` / `.fx<n>` extension strings** — those extensions do not appear in the binary at all.
The cell sub-files are loaded **name-driven**, by the path token the `.map` `DATAFILE` line supplies.
`.up` (UP_TERRAIN) and `.exd` (EXTRA_TERRAIN) share a **byte-identical format and decoder**:
`u32 triangle_count` + `count × {3 world-space XYZ f32 vertices + 1 trailing attribute f32}` (40-byte
record). Each is expanded at load into a runtime triangle carrying an XZ AABB + a normalized plane for
ground-Y point-location over overhang/bridge/ramp geometry the single-valued `.ted` heightfield cannot
express. (`.exd` is sparse — present only for cells that need it.)

### 1.5 The per-cell terrain → global texture join (the load-bearing chain)

This is the central terrain linkage. The `.ted` per-patch texture-index byte resolves through the
per-cell `.map` list into the global pool:

```
.ted  block-3 texidx byte (1-based, per 16×16 patch)
   →  .map TERRAIN TEXTURES[texidx − 1].intTexId        (per-cell registration-order list)
   →  bgtexture.lst[intTexId − 1]  /  bgtexture.txt[intTexId]   (GLOBAL pool, under map000)
   →  data/map000/texture/<relPath>.dds
```

Building geometry uses the identical chain via the BUILDING list: the `.bud` per-object `tex_id` is a
1-based index into the per-cell BUILDING TEXTURES list, whose value is itself a 1-based index into the
global `bgtexture` pool. (Full pool detail in §5.) The `.ted` finalize routine is the only place the
`−1` and the `[1,count]` clamp are applied; the raw byte is stored verbatim by the geometry loader.

### 1.6 Per-cell file roles (what each blob carries)

| File | Carries | Join into the cell |
|---|---|---|
| `.ted` | heightmap (f32) + vertex normals (i8/127) + per-patch texidx & direction grids + per-vertex diffuse RGBA (stored ×2, loaded ×0.5) | `.map` `TERRAIN DATAFILE`; texidx → per-cell TERRAIN list (§1.5) |
| `.map` | the section/DATAFILE/TEXTURES descriptor | the cell assembler; names all other sub-files |
| `.exd` / `.up` | supplementary walkable triangle surfaces (world XYZ + plane) | `.map` `EXTRA_TERRAIN` / `UP_TERRAIN DATAFILE` |
| `.bud` | building/mass-object mesh (FVF XYZ\|NORMAL\|TEX1, u16 indices) | `.map` `BUILDING DATAFILE`; per-object tex_id → BUILDING list |
| `.fx1`..`.fx7` | effect / water layers (FX3, FX5 = water) | `.map` `FX<n> DATAFILE`; per-FX texture list |
| `.sod` | 2D-XZ wall collision (see §1.7) | `.map` `SOLID DATAFILE` |
| `.mud` | per-cell ambient-sound zone grid (see §6) | fixed sidecar loaded directly (not via `.map`) |
| `.gad` | (loader is a no-op stub; purpose unknown) | fixed sidecar loaded directly |

### 1.7 Collision — `.sod`

`.sod` is the cell's collision: `u32 solidCount` + a flat array of 108-byte SolidRecords (each a world-XZ
AABB) + per-solid `(u32 quadCount + 48-byte QuadRecord array)`. Each QuadRecord is a single **2D-XZ wall
segment** stored as a line `z = m·x + b` (with a vertical-axis special-case flag), bounded by an AABB and
two endpoints. At load the collision loader builds an in-memory XZ **quadtree** per solid (this is the
"precomputed" acceleration — it is built at load, NOT read from `.sod.pre`). The move/slide resolver
sweeps a movement segment against the quadtree to find the nearest wall crossing; ground HEIGHT is
separate (`.ted` bilinear, supplemented by `.up`/`.exd` plane-eval).

> Binary-won corrections vs the synthesis brief: (a) `.sod` collision is line/segment intersection, NOT
> classic ray-parity point-in-polygon; (b) **`.sod.pre` is NOT a runtime input** — it is an authoring
> outline the client never opens. Rebuild collision from `.sod`. (See `formats/sod.md`, `formats/authoring_sidecars.md`.)

### 1.8 Cell builder summary

The single runtime builder for the whole bundle is the **terrain cell streamer**
(`Terrain_AcquireSlotAndLoadCell → Terrain_LoadCellFiles → Map_LoadCellDescriptor`): it computes cell
world bounds, resets the cell's grid slots, loads `.mud`/`.gad`/`.map`, the `.map` parser opens and
decodes every `DATAFILE`, then the finalize tail runs the per-section grid builders (ground grid,
mass-object/building grid, FX/water layers, collision quadtree). Area switching (`Env_MapSetAndLoadArea`)
loads `d<NNN>.lst` (resident-cell set) plus the per-area binaries (`map<NNN>.bin`,
`regiontable<NNN>.bin`, `region<NNN>.bin`, `npc<NNN>.arr`) and the per-area sound tables (§6) and
sky/wind/weather `.bin`s.

### 1.9 Spawns (area-level, adjacent to the bundle)

`npc<NNN>.arr` (28-byte records: `u16 mob_id`, world X/Z f32, facing f32, u32 spawn_type, inert tail)
is loaded as the last stage of the per-area binary load. Its **join key is `mob_id`** into the
mob/NPC template tables (`mobs.scr`/`npc.scr`) which carry the visual id that drives the §2 character
chain. The `.arr` is a lookup/metadata table (facing override for NPC-kind actors, id/region lookups,
elite multiplier via `spawn_type == 7`); **live actors arrive via the server area-entity snapshot**
(S2C 4/4 → ActorManager), not by iterating the `.arr`. `mob<NNN>.arr` (20-byte) has no client loader.

---

## 2. The CHARACTER chain

**Files:** `data/char/skin/g<id>.skn`, `data/item/skin/gi<id>.skn`, `data/char/bind/g<id>.bnd`,
`data/char/mot/<name>.mot`, and the text index tables `data/char/skin.txt`,
`data/char/actormotion.txt`, `data/char/bindlist.txt`, `data/char/motlist.txt`,
`data/char/skinlist.txt` / `data/item/skinlist.txt`.

**Source format specs:** `formats/skn.md`, `formats/mesh.md` (`.bnd`), `formats/animation.md` (`.mot`),
`formats/actormotion.md`, `formats/bindlist.md`, `specs/skinning.md`, `specs/equipment_visuals.md`.

### 2.1 The hub — `CharManifest_LoadAll`

At boot a single char-asset manifest loader pulls in the whole chain, in order: `bindlist.txt`
(→ every `.bnd`), `motlist.txt` (→ every `.mot` header), then `skin.txt`, `actormotion.txt`,
`emoticon.txt`, `userjoint.txt`, `gmmapmove.txt`. These text tables are the **index side** of the chain;
the binary `.skn`/`.bnd`/`.mot` are the geometry/animation side. (Note: `motlist.txt`/`bindlist.txt`/
`skinlist.txt` are `.txt`, not `.lst`, despite the name.)

### 2.2 `.skn` → texture (the IdA / skin-texture join)

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `.skn` header `id_a` | `data/char/skin.txt` → `tex_id` → `data/char/tex{512512\|10241024\|…}/<id>.png` | **`id_a`** (= the file's numeric suffix for item skins) | the char/item skin parser; the texture-id registry built from `tex{…}list.txt` |

The `.skn` is a count-prefixed mesh container (header `id_a`, `id_b`, LenStr name; then face section
36-byte records of 3 corners × `{u32 vertex_index, f32 u, f32 v}`; vertex section 24-byte records of
`{normal vec3, position vec3}`; influence/weight section 12-byte records of
`{u32 vertex_index, u32 bone_id, f32 weight}`). The render path dedups corners by
`(vertex_index, u, v)` and builds a 32-byte render vertex; the char path drops weights `< 0.01`,
normalizes per-vertex to 1.0, and picks a major bone.

> Binary-won correction vs the old `skn.md` wording: the influence/corner `+0` field is a **plain u32
> vertex index** (index-to-index match), NOT a "vertex position key / float-bit compare". Already folded
> into `formats/skn.md`.

### 2.3 `.skn` → skeleton (the IdB / bind join)

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `.skn` header `id_b` | the registered bind-pose whose `.bnd` `actor_id == id_b` | **`id_b` == `.bnd actor_id`** (used VERBATIM as the integer pose-pool key — there is NO literal `g<id_b>.bnd` filename rule) | the char skin loader resolves the pose; `InverseBind_BakeSkinInfluences` bakes the inverse-bind |

`id_b == 0` ⇒ rigid (no skeleton; item skins). `id_b` is a sparse `actor_id` (1..8892), NOT a dense
0..348 index. The `.bnd` is a bind-pose skeleton: header `actor_id` + LenStr name + `u32 bone_count`
(only the low byte is used → ≤255 bones), then 36-byte bone records `{u8 self_id, u8 parent_id,
3×f32 local translation, 4×f32 local quaternion XYZW (W last)}`. The loader links bones by `parent_id`
(root sentinel = self_id == parent_id == 0) and composes bind-world transforms by hierarchical
pre-multiply (`child_world = parent_world ∘ child_local`).

`.skn` weight `bone_id` resolves to a `.bnd` bone via `bone_array[bone_id − base_id]` where `base_id`
is the first bone's `self_id`.

### 2.4 The skeleton preload — `bindlist.txt` → `.bnd`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `data/char/bindlist.txt` (line = `.bnd` stem) | `data/char/bind/<stem>.bnd` | stem → path (prefix `data/char/bind/`); pool keyed by `actor_id` | `BindList_LoadAndRegister` eagerly preloads EVERY `.bnd` into the global pose pool at boot |

The four playable-class skeletons resolved by skin appearance are `data/char/bind/g{1..4}.bnd`
(Musa/Salsu/Dosa/Monk); the AnimCatalog keys by the appearance-slot `IdB = 5·(class + 4·variant) − 24`.
There is no `g11.bnd`/`g16.bnd`. (See `specs/skinning.md`, `CLAUDE.md`.)

### 2.5 `.mot` → skeleton (the animation join)

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `.mot` keyframe track `bone_id` (descriptor low byte) | the skeleton bone `self_id` of the `.bnd` resolved via the same `id_b` | `bone_id == .bnd self_id` (resolved `id − base_id`; runtime bone stride differs from disk) | the mixer deforms the resolved skeleton per frame |
| `.mot` header `id_b` | the clip registry (which skeleton-set a clip belongs to) | `id_b` (CharVisualRegistry, by-set index) | clip register |
| `.mot` header `id_a` | the motion-manager clip index | `id_a` (by-clip index) | clip register |

A `.mot` is `{id_a, id_b, LenStr name, frame_count}` then (full load) `u32 track_count` and per track
`{u32 descriptor (low byte = bone_id), u32 key_count, key_count × 28-byte keyframes}`; a keyframe is
7×f32 = `{translation XYZ, quaternion XYZW}` at 10 fps (`duration = frame_count × 0.1`). Clips are
registered from `motlist.txt` (prefix `data/char/mot/`); there is **no `g<id>.mot` path printf** — clips
resolve by id through the registry, never a formatted filename. The loader builds **two** indices:
by `id_b` (CharVisualRegistry) and by `id_a` (motion manager). (The `BANI`-magic `.mot` variant is dead
data — no magic branch in the shipped client — but its track body is the same standard layout.)

### 2.6 `.mot` selection — `actormotion.txt`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `actormotion.txt` motion-id slot (e.g. idle) | the `.mot` whose header `id_b == that motion id` | **the actormotion motion-id == the `.mot` `id_b`** | the actor visual / animation mixer |

`actormotion.txt` is a 136-byte record table; the row carries `skin_class`, frame-count slots, derived
per-frame-rate fields, and two 9-element int arrays of motion ids (`motion_ids_a[9]` and
`motion_ids_b[9]`). The runtime **idle motion = `motion_ids_a[1]` = column 16** (record `+0x44`).

> Binary-won correction (CYCLE 7): idle = **column 16 (`+0x44`), NOT column 15 (`+0x40`)** — column 15
> is statically DEAD. Already in `formats/actormotion.md`; ensure the C# loader matches.

### 2.7 Mob → skin (the full mob visual chain)

| Source | → Reference | Join key |
|---|---|---|
| `npc<NNN>.arr` `mob_id` | mob/NPC template (`mobs.scr`/`npc.scr`) → visual catalog id | `mob_id` |
| visual catalog id → `actormotion.txt` col1 | `actormotion.txt` col2 = `skin_class` | actormotion row key |
| `skin_class` | the `.skn` whose **`id_b == skin_class`** | `skin_class == .skn id_b` |
| then the `.skn` resolves its skeleton via `id_b` and its texture via `id_a` (§2.2 / §2.3) | | |

The skeleton resolves through the same AnimCatalog/`IdB` lookup as §2.4 — NOT a literal
`g<skin_class>.bnd`.

---

## 3. The EFFECTS chain

**Files:** `data/effect/xeffect.lst`, `data/effect/xeff/<name>.xeff`, `data/effect/xobj.lst`,
`data/effect/xobj/<name>.xobj`, `data/effect/bmplist.lst`, `data/effect/texture/<name>.tga`,
`data/effect/particle/particleEmitter.eff`, `data/script/effectscale.xdb`, and the joint/sword-light
text tables.

**Source format specs:** `formats/effects.md` (`.xeff`), `formats/xobj.md`, `formats/ion.md` (records
`.ion` as dead art metadata + the real `bmplist.lst`), `formats/xdb_tables.md` (`effectscale.xdb`),
`specs/effects.md`, `specs/effect-scheduling.md`.

> Binary-won correction vs the synthesis brief: there is **no `.ion` runtime format**. `descript.ion`
> is art-source metadata the client never reads. The real effect-texture descriptor is the binary
> `bmplist.lst`. The brief's "`.ion` → effect textures" link should be read as "`bmplist.lst` → effect
> textures."

### 3.1 Effect boot order

One boot orchestrator (`EffectManager_LoadBmplistAndManifests`) loads the effect manifests in a fixed
order: `bmplist.lst` (texture name pool) → `xobj.lst` (mesh objects) → `xeffect.lst` (effect
descriptors) → effect-cache prime → the joint/sword-light `.txt` tables.

### 3.2 Effect descriptor registry — `xeffect.lst` → `.xeff`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `xeffect.lst` (u32 count + 30-byte name records) | `data/effect/xeff/<name>.xeff` | name → path (prefix `data/effect/xeff/`) | the XEffect manager list loader |
| each `.xeff` header `effect_id` | the runtime spawn-by-id RB-tree | **`effect_id`** (sourced from inside the `.xeff` file, NOT from the `.lst`) | RB-tree insert (FIRST-WINS on duplicate id) |

A `.xeff` is `{u32 effect_id (rejected if == "XEFF"), u32 sub_effect_count}` then per element a 24-byte
fixed head (`emitter_type, resource_id, anim_flag, tex_count, …`) + a `tex_count × 64-byte` texture-name
table + four count-prefixed f32 curve arrays + a 9-byte track header + a static or animated keyframe
body. Spawns look an effect up by `effect_id`, never by filename.

### 3.3 Effect element → texture

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `.xeff` element 64-byte texture name | `data/effect/texture/<name>.tga` via the `bmplist.lst` pool | the texture **name** (binary-searched in the bmplist pool) | the effect-texture name lookup → GHTex handle |

`bmplist.lst` = `u32 count` + `count × 30-byte name records`; each name builds
`data/effect/texture/<name>` and a GHTex texture handle, pool-indexed by record order. (Its `bmplist.txt`
twin is an authoring sidecar.)

### 3.4 Effect element → mesh (`.xeff` → `.xobj`) and → GPU particle

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `.xeff` element with `emitter_type == 1` and `resource_id < 10000` | the shared mesh table built from `xobj.lst` → `data/effect/xobj/<name>.xobj` | `resource_id` → `xobj.lst` **slot_index** (= runtime XObj array index) | `XObjManager_LoadXobjLst` builds the array; per-file `XObj_LoadFromFile` |
| `.xeff` element with `resource_id >= 10000` | `data/effect/particle/particleEmitter.eff` entry map | `resource_id` (raw equality, no −10000 subtraction) | the GPU particle path |

`xobj.lst` = `u32 count` + `count × {u32 slot_index, 30-byte name}` (34-byte stride); the **slot_index is
the runtime id**: the parsed mesh is stored at `xobjArray[slot_index]`, and effect descriptors reference
a mesh by that integer. The `.xobj` itself is **plain ASCII text** (one token per line): a discarded
leading marker, `tri_count`, `3·tri_count` u16 index tokens, `vert_count`, then per vertex 8 floats
(`pos.xyz`, a discarded normal triplet, `u`, `v` stored as `1.0 − v`); the runtime vertex is FVF
XYZ\|DIFFUSE\|TEX1 with a default opaque diffuse.

> Open item (DBG-pending): the exact `.xeff` body field that stores the `.xobj` slot_index for a mesh
> emitter is not yet pinned statically; trace the CoreXEffect activate path. (`formats/xobj.md` §4.)

### 3.5 Effect scale override — `effectscale.xdb`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `data/script/effectscale.xdb` (8-byte records: `u32 effect_key`, `f32 scale`) | the `.xeff` being parsed | **`effect_key == .xeff effect_id`** | at `.xeff` parse the looked-up scale OVERWRITES (not stacks) the descriptor base scale |

---

## 4. The UI chain

**Files:** `data/ui/<name>.dds` (selected by the `uitex` registry), `data/script/msg.xdb`,
`data/script/<table>.do` (e.g. `emoticon.do`, the per-class stance `.do`, `errorinfo.do`/`msginfo.do`),
`data/ui/mobinfo.mi`, plus the `.xdb` UI-position tables (`buff_icon_position.xdb`).

**Source format specs:** `formats/ui_manifests.md` (uitex + `.do`), `formats/msg_xdb.md` /
`formats/xdb_tables.md`, `formats/mi.md`, `formats/texture.md`, `specs/ui_system.md`,
`specs/ui_hud_layout.md`.

### 4.1 UI texture registry — `uitex` → `.dds`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| the `uitex` registry (built from `uitex.txt`) | `data/ui/<name>.dds` (and other containers) | a numeric **UI tex id** | the texture create wrapper (raw bytes → D3DX9 in-memory create; auto-detects DDS/PNG/BMP/TGA by magic) |

UI panels resolve a texture by **hard-coded UI tex id** (e.g. id 27 = `data/ui/emoticon.dds`,
id 3 = the skill-window chrome atlas) and blit fixed atlas sub-rects from it.

### 4.2 Captions — `msg.xdb`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `data/script/msg.xdb` (516-byte records: `u32 msg_id` + 512-byte CP949 string) | a localized caption / printf template | **`msg_id`** | loaded once on the login-scene transition; consumed wherever a caption is fetched by id |

`msg.xdb` strings carry printf tokens (`%s`, `%d`, …) and are the source of UI labels and the HUD
info-panel text (the info panel is message-catalogue + hard-coded-layout driven).

### 4.3 Stance/icon records — `.do`

`.do` is **not one format** — it is an extension reused by several fixed-stride binary record tables
under `data/script/`, each EOF-driven (no count field), copied verbatim into a heap node and inserted
into one or more in-memory maps keyed by a record field. Disambiguate **by filename + loader**, never by
extension:

| `.do` table | Record stride | Role | Join out |
|---|---:|---|---|
| per-class skill stance (`musajung.do` + 11 siblings) | 116 B | per-skill icon coords | UI stance/skill icon atlas |
| `emoticon.do` | 40 B | emoticon picker grid | see below |
| `errorinfo.do` / `msginfo.do` | 108 B | message/label record tables | panel scene-builders |

**`emoticon.do` linkages** (representative of the `.do` pattern):

| Source | → Reference | Join key |
|---|---|---|
| `emoticon.do` glyph src `(glyphSrcX, glyphSrcY)` (23×23) | `data/ui/emoticon.dds` (uitex id 27) | UI tex id **27** (hard-coded) |
| `emoticon.do` label src `(labelSrcX, labelSrcY)` (87×13) | the name-strip sprite on `emoticon.dds` | UI tex id **27** |
| `emoticon.do` backplate/frame chrome | the skill-window chrome atlas | UI tex id **3** |
| `emoticon.do` `emoteCode` (`+0x0C`) | the chat / MainWindow dispatch on click | dispatched to MainWindow |
| boot → `emoticon.do` | loaded once in the data-table corpus thread | indexed by `id` and by `index` |

### 4.4 UI position tables — `buff_icon_position.xdb`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `data/script/buff_icon_position.xdb` (12-byte records: `u32 buff_id`, `u32 sprite_x`, `u32 sprite_y`) | a pixel origin on the buff-icon sprite sheet | **`buff_id`** | the buff/status UI panels (every panel that draws a buff/status strip) |

### 4.5 `mobinfo.mi` (dead)

`data/ui/mobinfo.mi` (`u32 count` + 28-byte records of 7 u32) has **no client loader** — there is no
`.mi` path literal in the binary. Its `+4`/`+8` fields read as `msg.xdb`-band catalogue ids, but the
file is never opened; the runtime mob-info text comes from the message-catalogue info-panel formatter,
not from `.mi`. Document as preservation-only; do not wire a parser.

---

## 5. The GLOBAL TEXTURE POOL

**Files:** `data/map000/texture/bgtexture.lst` (binary; `bgtexture.txt` is its dead text twin) →
`data/map000/texture/<relpath>.dds`.

**Source format specs:** `formats/bgtexture_lst.md`, `formats/texture.md`, `formats/terrain.md`.

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `bgtexture.lst` (u32 count, validated `1..1999`; `count × 48-byte records` = `u8 kind` + `char[47]` relpath) | `data/map000/texture/<relpath>.dds` | the **0-based record index** (= `intTexId − 1`; the `.map` TEXTURES value is 1-based) | the terrain texture pool (`TerrainPool_InitFromBgtextureLst`, once at TerrainManager construction) → GHTex scheduler pool → `GHTex_Load` (D3DX create) |

The pool is **GLOBAL and lives under `map000`** for every area — the `data/map000/texture/` prefix is
hard-coded, never per-area substituted. The on-disk record is 48 bytes; the runtime per-entry object is
76 bytes plus a parallel 1-byte `kind` array (`kind == 1` = static render object, else
scroll/animated). The pool is the **final join target of the `.ted`/`.map` terrain texture chain** (§1.5)
and of the `.bud`/BUILDING chain.

### 5.1 The full terrain texture join (restated as one path)

```
.ted texidx byte (1-based)
  → .map <SECTION> TEXTURES[texidx − 1].intTexId   (per-cell, registration order; TERRAIN or BUILDING)
  → bgtexture.lst[intTexId − 1]                     (global pool, map000)  ── JOIN KEY = intTexId (1-based)
  → data/map000/texture/<relpath>.dds              → GHTex handle (lazily D3DX-created by the pool)
```

(`bgtexture.txt` mirrors the `.lst` index → relpath and is the robust text index for tooling, but the
runtime reads the `.lst`.)

---

## 6. The SOUND chain

**Files:** per-area sound tables `data/map<NNN>/soundtable<NNN>.{bgm, bge, eff, run, wlk}`, the per-cell
`data/map<NNN>/dat/d<NNN>x<X>z<Z>.mud` zone grid, and the leaf audio
`data/sound/{2d,3d}/<id>.ogg`.

**Source format specs:** `formats/sound_tables.md`, `formats/mud.md`, `specs/sound.md`.

### 6.1 The two-stage join — `.mud` tile → sound table → `.ogg`

| Source | → Reference | Join key | Builder |
|---|---|---|---|
| `.mud` tile byte under the local player | the per-area sound table row | the tile byte value used **directly as a 0-based row index** | the per-frame ambient driver (`SoundMgr_UpdateAmbientFromMudTile`) |
| sound-table record `sound_entry_id` (`+0x00`) | `data/sound/{2d\|3d}/<id>.ogg` | **`sound_entry_id`** | the OGG factory (`<dir>/<id>.ogg`; the 2d/3d directory is chosen by play CATEGORY, not by the file) |

`.mud` is a headerless 32768-byte grid (64×64 tiles × 8 bytes, 16-unit tiles spanning the 1024-unit
cell). Each 8-byte tile carries `{unread, unread, bgmZoneId, bgeAmbientId0, bgeAmbientId1, effId0,
effId1, effId2}` — i.e. **byte +2 = BGM index, +3/+4 = BGE indices, +5/+6/+7 = EFF indices**. A `0` tile
value = silence; a missing `.mud` falls back to an all-zero default tile (total silence). The driver
samples the tile under the player, looks each non-zero index up in the matching per-area sound table,
and starts/stops the music/ambient/3D-positional sounds.

### 6.2 The per-area sound tables

Each `soundtable<NNN>.{bgm,bge,eff,run,wlk}` is a fixed 0x3000 (12288)-byte record block read into a
global = **256 records × 48 bytes** (a trailing 1024-byte `u32[256]` present-flag array is loader-ignored
authoring metadata). A record is `{u32 sound_entry_id (0 = null sentinel), u8[24] tod_enable (per
hour-of-day gate), f32 weight}` and, for EFF only, `{f32 pos_x, (+0x24 non-coordinate), f32 pos_z,
f32 radius}` (the EFF Y is taken from the player position, not the record). The five extensions map to
the `.mud` tile bytes: BGM/BGE (2D) and EFF (3D positional); `wlk`/`run` are footstep tables (their
`.mud` index source `+0`/`+1` is unverified — those tile bytes were all-zero in samples).

### 6.3 Sound builder summary

The per-area tables load on area enter (`Env_MapSetAndLoadArea` → the five-extension table loader,
clearing/filling the five globals). The per-frame ambient driver joins the `.mud` tile → table row →
`<id>.ogg`. A separate actor-event SFX router plays 3D event sounds (footsteps/voices) using the same
record id space under `data/sound/3d/`. The join key throughout is the **`sound_entry_id`** → leaf OGG.

---

## 7. Master join-key index (quick reference)

| Subsystem | Producer file | Join key | Resolves to | Builder |
|---|---|---|---|---|
| Terrain | `.ted` texidx (1-based) | `texidx → .map list → intTexId` | `bgtexture.lst[intTexId−1]` → `map000/texture/*.dds` | terrain cell streamer + texture pool |
| Terrain | `.map` `DATAFILE` token | the literal cell path | `.ted/.exd/.up/.bud/.fx*/.sod` | `.map` parser (cell assembler) |
| Terrain | `d<NNN>.lst` cell-key | `cellZ + 100000·cellX` | the area's resident cell set | area-load path |
| Collision | `.sod` SOLID `DATAFILE` | cell tuple | per-cell wall quadtree | collision loader; move/slide resolver |
| Character | `.skn id_a` | `id_a` | `skin.txt → tex_id → char/tex*/<id>.png` | skin parser + texture-id registry |
| Character | `.skn id_b` | `id_b == .bnd actor_id` (verbatim key) | the bind pose | skin loader + inverse-bind bake |
| Character | `bindlist.txt` stem | stem → path | `char/bind/<stem>.bnd` | boot bind preload (pose pool) |
| Character | `.mot bone_id` | `== .bnd self_id` (−base_id) | a skeleton bone | animation mixer |
| Character | `actormotion` motion id (idle = col16) | `== .mot id_b` | a `.mot` clip | actor visual / mixer |
| Character | `npc.arr mob_id` / mob template | `mob_id → skin_class` | `.skn` with `id_b == skin_class` | mob visual chain |
| Effects | `xeffect.lst` name | name → path | `effect/xeff/<name>.xeff` | XEffect manager |
| Effects | `.xeff effect_id` | `effect_id` | spawn RB-tree; `effectscale.xdb[effect_id]` | RB-tree; scale override at parse |
| Effects | `.xeff` element tex name | name | `bmplist.lst` pool → `effect/texture/<name>.tga` | effect-texture lookup → GHTex |
| Effects | `.xeff` element `resource_id` (`<10000`) | `xobj.lst slot_index` | `effect/xobj/<name>.xobj` | XObjManager array |
| UI | `uitex` id | numeric tex id | `data/ui/<name>.dds` | texture create wrapper |
| UI | `msg.xdb msg_id` | `msg_id` | 512-byte CP949 caption | msg loader; caption fetch |
| UI | `emoticon.do` glyph/label src | UI tex id 27 | `data/ui/emoticon.dds` sub-rects | emoticon panel builder |
| UI | `buff_icon_position.xdb buff_id` | `buff_id` | buff-icon sprite (x,y) | buff/status panels |
| Texture | `bgtexture.lst` index | `intTexId − 1` | `map000/texture/<relpath>.dds` | terrain texture pool → GHTex |
| Sound | `.mud` tile byte | direct 0-based row index | per-area sound-table record | ambient driver |
| Sound | sound-table `sound_entry_id` | `sound_entry_id` | `data/sound/{2d\|3d}/<id>.ogg` | OGG factory |

---

## 8. Corrections recorded by this synthesis (binary-won over the brief)

1. **`.ion` is not a runtime format.** `descript.ion` is dead art metadata; the real effect-texture
   descriptor is `bmplist.lst`. (§3, `formats/ion.md`.)
2. **`.sod.pre` is not a runtime input.** Collision is rebuilt from `.sod` (the quadtree is built at
   load, not read from a sidecar); `.sod` collision is segment-line intersection, not ray-parity
   point-in-polygon. (§1.7, `formats/sod.md`, `formats/authoring_sidecars.md`.)
3. **`.ted.post` / `.bud.pre` / `.fx<n>.pre` / `.tol` / `.mi` / `mob<NNN>.arr` are tool/authoring
   output the client never opens** — no runtime parser. (§0, §1, §4.5, `formats/authoring_sidecars.md`,
   `formats/tol.md`, `formats/mi.md`, `formats/npc_spawns.md`.)
4. **`.exd`/`.up`/`.bud`/`.fx<n>` are loaded name-driven via the `.map` `DATAFILE` token**, not by a
   hardcoded extension; `.up` and `.exd` share one format and one decoder. (§1.3, §1.4.)
5. **Idle motion = `actormotion.txt` column 16 (`+0x44`)**, not column 15 (dead). (§2.6.)
6. **`.skn` influence/corner `+0` is a plain u32 vertex index**, not a position/float-bit key. (§2.2.)
7. **`actor_size.xdb` is fully dead** — its path constant is never passed to a loader. (§0.)
8. **`motlist.txt` / `bindlist.txt` / `skinlist.txt` are `.txt`, not `.lst`**, and ARE loaded (unlike
   the sky `*.txt` / `bgtexture.txt` / `bmplist.txt` dead twins). (§0, §2.1.)

---

## 9. Cross-references (the source format specs this synthesis joins)

- Terrain bundle: `formats/terrain.md`, `formats/terrain_scene.md`, `formats/cell_exd.md`,
  `formats/cell_up.md`, `formats/sod.md`, `formats/mud.md`, `formats/authoring_sidecars.md`,
  `formats/bgtexture_lst.md`, `formats/area_inventory.md`,
  `specs/terrain-streaming.md`.
- Character chain: `formats/skn.md`, `formats/mesh.md`, `formats/animation.md`,
  `formats/actormotion.md`, `formats/bindlist.md`, `specs/skinning.md`, `specs/equipment_visuals.md`.
- Effects chain: `formats/effects.md`, `formats/xobj.md`, `formats/ion.md`, `formats/xdb_tables.md`,
  `specs/effects.md`, `specs/effect-scheduling.md`.
- UI chain: `formats/ui_manifests.md`, `formats/msg_xdb.md`, `formats/xdb_tables.md`, `formats/mi.md`,
  `formats/texture.md`, `specs/ui_system.md`, `specs/ui_hud_layout.md`.
- Global texture pool: `formats/bgtexture_lst.md`, `formats/texture.md`.
- Sound chain: `formats/sound_tables.md`, `formats/mud.md`, `specs/sound.md`.
- Spawns: `formats/npc_spawns.md`. Manifests: `formats/manifests_lst.md` (the binary `.lst` family).
- VFS container: `formats/pak.md`, `specs/vfs_overview.md`.

---

## 10. VFS subsystem consumers (runtime topology)

> **Provenance:** static-hypothesis / unverified at f61f66a9. Folded from `Docs/RE/vfs/linkage_and_usage.md`
> which carried no IDB anchor. Module-level names below are organizational approximations pending
> debugger confirmation of the exact class/symbol names. The per-function canonical names used in
> §1–§6 are the higher-confidence tier.

The table below maps major runtime modules to the VFS file types they consume and their role in
the asset graph. All reads pass through the common VFS find-and-read routine (binary-searched TOC;
see `formats/pak.md`, `specs/vfs_overview.md`); the join-key chains in §1–§6 detail how each module
resolves its asset paths from the five index manifests.

| Runtime module | File types consumed | Role |
|---|---|---|
| Skeletal animator | `.bnd`, `.mot` | interpolates joint matrices and keyframe tracks |
| Actor renderer | `.skn`, `.png` | skin-deform (CPU) and vertex buffer upload |
| Terrain renderer | `.map`, `.ted`, `.sod`, `.exd`, `.up` | heightfield grid streaming, cell paging, collision quadtree population |
| D3D device manager | `.psh`, `.vsh`, `.dds`, `.tga`, `.bmp` | shader source assembly, texture-to-sampler binding |
| Audio engine | `.ogg`, sound tables (`.bgm`/`.bge`/`.eff`/`.run`/`.wlk`) | DirectSound buffer streaming, footstep type coordination |
| Script / data host | `.lua`, `.scr`, `.xdb` | game option execution, item table loading, UI text translation |
