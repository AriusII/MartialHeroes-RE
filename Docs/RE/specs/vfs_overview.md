---
status: confirmed
sample_verified: yes        # tree, counts and extension census come from a real archive enumeration (43,347 entries, 49 extensions)
subsystems: [vfs_structure, extension_census, manifest_linkage]
networked: false
encoding_note: All in-game text is CP949 (MS-949 code page), not UTF-8.
---

# VFS Overview — directory tree, extension census, manifest linkage

> Clean-room neutral spec. Promoted from a black-box enumeration of a real client VFS (no decompiler,
> no addresses). No sample bytes are reproduced — only structure, counts, names, and linkage.
>
> **Scope.** The shape of the mounted asset tree: the top-level directory layout, what each directory
> holds, the per-extension census (count + purpose + owning format spec), the manifest/index files
> that stitch assets together, and the explicit list of still-unspecced (gap) extensions.
>
> **Out of scope / cross-references.** The `.inf`/`.vfs` container byte layout and the open-mode
> dispatch belong to `formats/pak.md`. Loader selection, the GHTex cache, the linkage *chains* and the
> bulk loader belong to `specs/asset_pipeline.md`. Per-format byte layouts belong to the individual
> `formats/*.md` docs cited per row below.

---

## 0. Provenance and totals

Enumerated via the project's VFS harness over a real `data.inf` + `data/data.vfs` pair (the same
reference archive `formats/pak.md` validates byte-exact). Totals (SAMPLE-VERIFIED):

- **43,347** entries
- **49** distinct extensions
- **60** map areas (`map000`–`map047`, `map100`, `map201`–`map210`, `map300`)
- All virtual paths begin with the single root prefix `data/`.

---

## 1. Top-level directory tree

```
data/
├── char/             Character assets (skin, skeleton, motion, textures) + manifest .txt files
├── cursor/           Mouse cursor graphics (.dds) + a binary version record
├── effect/           Particle/visual effects: .xeff descriptors, .tga textures, shape geometries, manifests
├── item/             Item meshes (.skn), item icon/texture atlases (.dds), item effect sprites
├── map000/           GLOBAL terrain data: the universal texture repository (all areas) + area-0 cell data
├── map001/ … map047/ Per-area terrain cell data + spawns + sound tables
├── map100/           Special/instanced area
├── map201/ … map210/ Special/instanced areas
├── map300/           Special area
├── script/           Game logic: items, quests, skills, mobs, NPCs, map config, caption DB, Lua config
├── script_newserver/ New-server item/NPC overrides (items.scr + npcs.scr only)
├── shader/           D3D9 HLSL shader source (.psh pixel, .vsh vertex)
├── sound/            OGG audio: 2d/ (music/UI) and 3d/ (world/combat)
└── ui/               UI atlas textures (.dds), icon sub-directories, minimap bitmaps, the uitex manifest
```

---

## 2. Per-directory inventory (counts SAMPLE-VERIFIED)

### `data/char/` — ~6,551 entries
Character model pipeline. Root manifests (`actormotion.txt`, `skin.txt`, `bindlist.txt`,
`motlist.txt`, `skinlist.txt`, `emoticon.txt`, `sameemoticon.txt`, per-resolution `texNNNlist.txt`)
plus four asset sub-dirs:

| Path | Approx count | Contents | Format spec |
|---|---|---|---|
| `data/char/bind/` | ~350 `.bnd` | Bind-pose skeletons, `g{IdB}.bnd` | `formats/mesh.md` |
| `data/char/mot/` | ~3,892 `.mot` | Skeletal animation clips, `g{skin_class}{motion}.mot` | `formats/animation.md` |
| `data/char/skin/` | ~1,271 `.skn` | Skinned meshes, `g{skin_class}{variant}.skn` | `formats/mesh.md` |
| `data/char/tex10241024/` | ~498 `.png` | 1024×1024 skin textures | `formats/texture.md` |
| `data/char/tex512512/` | ~397 `.png` | 512×512 skin textures | `formats/texture.md` |
| `data/char/tex256512/` | ~62 `.png` | 256×512 skin textures | `formats/texture.md` |
| `data/char/tex256256/` | ~75 `.png` | 256×256 skin textures | `formats/texture.md` |

The texture resolution tier is encoded in the **directory name**; the skin chain resolves a numeric
tex_id to a filename within the matching resolution dir.

### `data/cursor/` — ~12 entries
Mouse cursors (`stand.dds`, `battle.dds`, animated `hand-jap-0N.dds`, …), two CP949 chat/word-filter
text lists (`curse.txt`, `cursechat.txt`), and `game.ver` (a 28-byte binary version record — **GAP**).

### `data/effect/` — ~8,905 entries
All visual effect data. Root manifests: `bmplist.txt`/`bmplist.lst` (effect-texture id → `.tga`),
`xeffect.txt`/`xeffect.lst` (effect catalogue), `xobj.lst` (effect-mesh inventory),
`itemjointeff.txt` and `itemswordlight.txt` (equipment → attachment effects). Sub-dirs:

| Path | Approx count | Contents | Format spec |
|---|---|---|---|
| `data/effect/map/` | ~3,810 `.bmp` | Per-cell lightmap bitmaps `d{area}x{cx}z{cz}.bmp` | `formats/texture.md` |
| `data/effect/obj/` | 5 `.eff` | Primitive shape geometries | `formats/effects.md #shape` |
| `data/effect/particle/` | 1 `.eff` | Master particle-emitter template | `formats/effects.md` |
| `data/effect/tex/` | ~1,464 `.tga` | Effect particle textures (id-referenced) | `formats/texture.md` |
| `data/effect/texture/` | ~1,458 `.tga` + index | Alternate effect texture dir (+ a binary index mirror) | `formats/texture.md` |
| `data/effect/xeff/` | ~3,586 `.xeff` | Particle effect descriptors | `formats/effects.md §A` |
| `data/effect/xobj/` | ~32 `.xobj` | ASCII static mesh objects | `formats/mesh.md` |

### `data/item/` — ~2,885 entries
Item meshes and textures. `data/item/skin/` (~1,518 `.skn`, `gi{item_class}{variant}.skn`),
`data/item/texture/` (~1,337 `.dds`), `data/item/effect/` (~27 `.dds` glow sprite sheets). Note: a
pair of stray developer-tool PE executables were accidentally bundled — see §6 (excluded, not a game
format).

### `data/map000/` — ~1,274 entries — the GLOBAL area
Holds (1) the **universal terrain texture repository** under `data/map000/texture/` shared by **all**
areas, (2) area-0 cell data under `data/map000/dat/`, and (3) per-area environment binaries
(`map000.bin`, `light0.bin`, `material0.bin`), spawn arrays, region tables, and the five sound tables.
Environment binaries are owned by `formats/environment_bins.md`; spawns by `formats/npc_spawns.md`;
region/misc by `formats/misc_data.md`; sound tables by `formats/sound_tables.md`.

The texture repository (`data/map000/texture/`) is keyed by the terrain texture index — see the
**`bgtexture.lst` CONFLICT** in §4.

### `data/map001/` … `data/map300/` — ~18,861 entries (60 areas)
Each area follows the same schema. Per-area root files: `mapNNN.bin` (flags), `mobNNN.arr` /
`npcNNN.arr` (spawns), `regionNNN.bin` / `regiontableNNN.bin`, the five `soundtableNNN.{bgm,bge,eff,
run,wlk}`, and (3 areas only) a large `NNN.tol`. Per-cell data lives under `data/mapNNN/dat/` named
`d{aaa}x{cx}z{cz}.<ext>`:

| Per-cell ext | Purpose | Format spec |
|---|---|---|
| `.bud` | Static scene graph (buildings, objects) | `formats/terrain_scene.md` |
| `.ted` | Terrain heights + texture index grid (fixed 46,987 B) | `formats/terrain.md` |
| `.map` | Terrain/scene descriptor (texture/section table) | `formats/terrain.md` |
| `.sod` | 2D XZ collision wall segments | `formats/terrain.md` |
| `.exd` | Collision triangle layer (EXD) | `formats/terrain_layers.md` |
| `.fx1`–`.fx7` | Terrain overlay layers | `formats/terrain_layers.md` |
| `.up` | Collision triangle layer (UP variant) | `formats/terrain_layers.md` |
| `.lst` | Per-area cell manifest (u32 count + cell keys), one per `dat/` dir | `formats/area_inventory.md` |
| `.mud` | Fixed 32,768-B per-cell binary (mostly zero in sample) | **GAP** |
| `.bud.pre` / `.sod.pre` | Pre-patch deltas (newer areas only) | **GAP** |
| `.ted.post` | Post-patch `.ted` (same 46,987-B stride) | **GAP** |

The `.pre`/`.post` deltas appear **only** in the newer areas (≈016–046); older areas lack them. This
suggests a patch/layering mechanism (base cell + pre-applied + post-applied), unverified in detail.

### `data/script/` — ~75 entries
Game-logic tables and caption data. A large set of binary `.scr` tables (items, mobs, NPCs, skills,
quests, products, upgrades, …), `.xdb` tables (the caption DB `msg.xdb` plus several small ones), a
few CP949 `.txt`/`.csv` tables, Lua config (`config.lua`, `display.lua`, `uiconfig.lua`), and
per-class `.do` stance tables. Partially specced (`formats/misc_data.md`, `formats/config_tables.md`,
`formats/msg_xdb.md`, `formats/ui_manifests.md`); most bulk `.scr` remain **GAP** (see §5).

### `data/script_newserver/` — 2 entries
`items.scr` + `npcs.scr` overrides applied on the "new server" login path.

### `data/shader/` — ~10 entries
D3D9 HLSL **source** (ASCII, not compiled bytecode): cel-shading vsh/psh, final composite, power-of-2
blend pixel shaders. Owned by `formats/shaders.md`.

### `data/sound/` — ~2,108 entries
`data/sound/2d/` (~178 `.ogg`, non-positional music/UI) and `data/sound/3d/` (~1,930 `.ogg` + 1 `.wav`,
positional world/combat). Filenames are numeric codes resolved from the per-area sound tables. Owned
by `formats/sound_tables.md`.

### `data/ui/` — ~2,588 entries
Root: ~216 named UI atlas `.dds` (one per UI system) + the `uitex.txt` manifest. Sub-dirs for dice,
face portraits, guild icons (~2,249), minimap, mode icons, product, skill icons, upgrade. One unique
`mobinfo.mi` panel-layout binary (**GAP**). UI manifests owned by `formats/ui_manifests.md`.

---

## 3. Manifest / index files — the linkage layer

These "keystone" files stitch the VFS into a coherent asset graph. (The full resolution *chains* —
how a consumer request walks these to an on-disk file — are documented in
`specs/asset_pipeline.md §3`.)

| File | Format | Key → Value | Consumer |
|---|---|---|---|
| `data/char/skin.txt` | Tab-delimited (~1,352 rows) | IdA/IdB/class/variant → tex_id pair | Skin renderer → `data/char/tex{res}/{id}.png` |
| `data/char/actormotion.txt` | Tab-delimited (~1,084 rows) | mob_id / skin_class → motion-code columns | Mob/PC spawner → bind/skin/mot assets |
| `data/char/bindlist.txt` | Newline list | → `g{IdB}.bnd` filenames | Boot preloader (bind-pose pool) |
| `data/char/motlist.txt` | Newline list | → `.mot` filenames | Boot preloader (motion map) |
| `data/char/skinlist.txt` | Newline list | → `.skn` filenames | Boot preloader |
| `data/map000/texture/bgtexture.lst` | **Binary** (u32 count + 48-B records) | terrain texture index → relative DDS name | Terrain renderer (**runtime path** — see §4 CONFLICT) |
| `data/map000/texture/bgtexture.txt` | Text mirror | — | Authoring source for the `.lst` above (not the runtime path) |
| `data/effect/xeffect.lst` | Binary (count + records) | effect slot → `.xeff` name | Effect system (runtime form) |
| `data/effect/xeffect.txt` | Text mirror | — | Authoring source |
| `data/effect/bmplist.lst` / `.txt` | Binary / text | texture id → `.tga` stem | Particle renderer |
| `data/effect/itemjointeff.txt` | Tab-delimited (~18,580 rows) | equip slot → attachment effect code | Equipment renderer |
| `data/ui/uitex.txt` | Structured CP949 text | 4-digit tex_id → VFS `.dds` path | UI renderer |
| `data/script/msg.xdb` | Binary (516-B records) | u32 caption id → CP949 string | All UI text (`MsgXdbCatalog`) |
| `data/script/mapsetting.scr` | Binary (84-B records, ~52 zones) | zone id → name + XZ bounds | Map selector |
| Per-area `soundtableNNN.{bgm,bge,eff,run,wlk}` | Binary, fixed stride | cell grid index → sound id | Sound system → `data/sound/{2d,3d}/{id}.ogg` |
| Per-area `mobNNN.arr` | Binary, 20-B records | spawn record → pos + mob_id | Mob spawner |
| Per-area `npcNNN.arr` | Binary, 28-B records | spawn record → pos + npc_id + facing | NPC spawner |
| `data/char/emoticon.txt` | Tab-delimited (~47 rows) | emote id → name + motion ids | Emote system |

---

## 4. CONFLICT — `bgtexture.lst` (binary) is the runtime index; `bgtexture.txt` is an authoring mirror

The runtime terrain-texture index the client actually loads is the **binary `bgtexture.lst`** under
`data/map000/texture/`, **not** the text `bgtexture.txt`:

- `bgtexture.lst` layout: a u32 `count`, then `count` records of **48 bytes** each — record byte[0] is
  a **kind** selector (1 = animated texture options, ≥ 2 = static, 0 = slot skipped), bytes[1..] are a
  NUL-terminated **relative name**. The loader resolves each to `data/map000/texture/<rel>.dds` and
  builds an **index-keyed** terrain texture pool (distinct from the name-keyed texture cache).
- `bgtexture.txt` is the text **authoring mirror** of the same index; it is not what the loader reads
  at runtime.
- **Action:** the terrain spec (`formats/terrain.md`) and any terrain consumer must **follow the
  `.lst` binary**, not the `.txt`. The end-resolved per-texture DDS path is unchanged; only the
  intermediary index file name/format differs. This CONFLICT is also recorded in `formats/pak.md` and
  the loader chain in `specs/asset_pipeline.md §3 (chain B)`. (Several other manifests follow the same
  pattern — `.lst` binary is the runtime form, `.txt` is the authoring mirror: `xeffect`, `bmplist`.)

The off-by-one between the cell's terrain index byte and the 0-based pool slot is **UNVERIFIED** (it
likely lives in the `.map` authoring layer / render-time index call, not in the `.lst` loader).

---

## 5. Extension census (49 extensions) — owning spec / gap status

| Extension | Approx count | Purpose | Spec / GAP |
|---|---|---|---|
| `.arr` | 110 | Spawn arrays (NPC 28-B / mob 20-B records) | `formats/npc_spawns.md` |
| `.bge` `.bgm` `.run` `.wlk` `.eff` | 60–61 each | Per-area sound tables (+ `.eff` also effect shapes) | `formats/sound_tables.md` |
| `.bin` | ~955 | Environment / region binaries (light, material, fog, regiontable, …) | `formats/environment_bins.md`, `misc_data.md` |
| `.bmp` | ~3,801 | Per-cell lightmap DIBs | `formats/texture.md` |
| `.bnd` | ~349 | Bind-pose skeletons | `formats/mesh.md` |
| `.bud` | ~2,296 | Static-object cell scene graph | `formats/terrain_scene.md` |
| `.csv` | 1 | Item database (CP949 comma-delimited) | `formats/config_tables.md` |
| `.dds` | ~5,180 | DirectDraw Surface textures | `formats/texture.md` |
| `.do` | ~17 | Per-class stance/icon tables (116-B records) | `formats/ui_manifests.md §2.7` |
| `.exd` | ~1,384 | Collision triangle layer (EXD) | `formats/terrain_layers.md` |
| `.fx1`–`.fx7` | mixed | Terrain overlay layers | `formats/terrain_layers.md` |
| `.lst` | ~65 | Binary index mirrors of text manifests | `formats/area_inventory.md` |
| `.map` | ~2,505 | Terrain cell descriptor | `formats/terrain.md` |
| `.mot` | ~3,891 | Skeletal animation clips | `formats/animation.md` |
| `.ogg` | ~2,107 | OGG Vorbis audio | `formats/sound_tables.md` |
| `.png` | ~1,005 | PNG character-skin textures | `formats/texture.md` |
| `.psh` `.vsh` | 9 / 1 | D3D9 HLSL shader source | `formats/shaders.md` |
| `.sc` | 1 | Dialog descriptor (`discript.sc`, 68-B records) | `formats/misc_data.md §discript` |
| `.skn` | ~2,786 | Skinned meshes | `formats/mesh.md` |
| `.sod` | ~2,500 | 2D XZ collision walls | `formats/terrain.md` |
| `.ted` | ~2,505 | Terrain cell (heights + texture index grid) | `formats/terrain.md` |
| `.tga` | ~1,531 | Effect particle textures | `formats/texture.md` |
| `.txt` | ~619 | CP949 text tables (manifests + data) | partial `formats/misc_data.md`; many **GAP** |
| `.up` | ~222 | Collision triangle layer (UP) | `formats/terrain_layers.md` |
| `.wav` | 1 | Single WAV audio | (incidental) |
| `.xdb` | 6 | Caption DB + small tables | `formats/msg_xdb.md` (caption); small ones **GAP** |
| `.xeff` | ~3,584 | Particle effect descriptors | `formats/effects.md §A` |
| `.xobj` | ~30 | ASCII static mesh objects | `formats/mesh.md` |
| `.scr` | ~44 | Binary game-logic tables | partial `formats/misc_data.md`; bulk **GAP** |
| **`.mud`** | ~1,578 | Fixed 32,768-B per-cell binary (mostly zero) | **GAP** |
| **`.pre`** | ~2,014 | Pre-patch cell deltas (`.bud.pre`, `.sod.pre`) | **GAP** |
| **`.post`** | ~852 | Post-patch `.ted` deltas (same stride) | **GAP** |
| **`.tol`** | 3 | Large area-wide binary (~4 MB; areas 009/013/100) | **GAP** |
| **`.ver`** | 2 | 28-B version record | **GAP** |
| **`.mi`** | 1 | Mob-info panel layout (`mobinfo.mi`) | **GAP** |
| **`.lua`** | 3 | Lua config scripts | **GAP** (key→effect mapping undocumented) |
| `.ion` / `.exe` | 1 / 2 | Stray folder-description file / dev-tool PE executables | **excluded — not game formats** |

(Extension list is the 49 distinct extensions confirmed by enumeration; counts are SAMPLE-VERIFIED
approximations from the reference archive.)

---

## 6. Format gaps (no committed `formats/*.md` yet)

| Gap | Pattern | Notes / priority |
|---|---|---|
| `.mud` | `d{aaa}x{cx}z{cz}.mud` (~1,578, 32,768 B, all-zero sample) | Possibly a water/mud height grid; needs a non-zero sample |
| `.pre` | `*.bud.pre`, `*.sod.pre` (~2,014, newer areas only) | Patch/delta overlays; stride unknown |
| `.post` | `*.ted.post` (~852, 46,987 B = `.ted` stride) | May be a full post-patch `.ted` replacement |
| `.tol` | `009.tol`, `013.tol`, `100.tol` (~4 MB each) | Area-wide large binary; 3 instances only |
| `.scr` (bulk) | `items.scr`, `mobs.scr`, `skills.scr`, etc. | The big game-logic tables; high priority; only mapsetting/quest/npc partially specced |
| `.xdb` (small) | `actor_size.xdb`, `buff_icon_position.xdb`, `creature_item.xdb`, `effectscale.xdb`, `vehicle.xdb` | Small fixed-record tables; relatively easy |
| `.ver` | `data/cursor/game.ver` | 28 B (7 × u32); likely client version fields |
| `.mi` | `data/ui/mobinfo.mi` | 592-B mob-info panel layout; unique format |
| `.lua` | `config.lua`, `display.lua`, `uiconfig.lua` | Readable Lua; config key→effect mapping undocumented |
| `.txt` (bulk) | `angerlevel.txt`, `eventuser.txt`, `product.txt`, `curse*.txt` | Small CP949 text tables without specs |
| Stray | `descript.ion`, `listmaker2.exe` | Not game formats — document as intentional exclusions |

No IDA witness was used for these gap fillings; single-sample inferences (`.fx4`, all-zero `.mud`) are
flagged. The `.mud`/`.pre`/`.post`/`.tol`/bulk-`.scr`/small-`.xdb` families are the open follow-ups.

---

## 7. Cross-references

- `formats/pak.md` — container byte layout, open-mode dispatch, `vfsmode` toggle, `bgtexture.lst`
  CONFLICT.
- `specs/asset_pipeline.md` — loader dispatch verdict, GHTex cache, linkage chains, bulk loader.
- `specs/resource_pipeline.md` — runtime resource pipeline, terrain streaming, subsystem caches,
  per-area census detail.
- Per-format docs: `formats/{terrain,terrain_layers,terrain_scene,mesh,animation,texture,effects,`
  `sound_tables,npc_spawns,environment_bins,misc_data,config_tables,msg_xdb,ui_manifests,shaders,`
  `actormotion,sky,area_inventory}.md`.
- Proposed canonical names to flag for `names.yaml`: `bgtexture_index` (the `.lst` runtime index),
  `effect_catalogue` (`xeffect.lst`), `bmplist_index` (`bmplist.lst`), `uitex_manifest` (`uitex.txt`),
  `caption_catalogue` (`msg.xdb`). Glossary is orchestrator-owned; flagged, not edited.
- Provenance: see `Docs/RE/journal.md`.
