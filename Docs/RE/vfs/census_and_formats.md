# VFS Sub-Asset Census and Format Directory

An enumeration of the reference archive reveals **43,347** entries using **49** distinct file extensions. Below is a categorized census mapping each file extension to its purpose, parser logic, and reverse-engineering specification.

---

## 1. Terrain & World Map Formats

These files compose the game's terrain grid, cell structures, static scene graphs, and collision data. They are stored under `data/mapNNN/dat/` and follow the naming schema `d{area}x{cx}z{cz}.<ext>`.

| Extension | Approx. Count | Purpose / Content | Target Spec |
|:---|:---|:---|:---|
| `.map` | ~2,505 | Cell master descriptor; defines textures, overlays, and building instances. | [terrain_scene.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/terrain_scene.md) |
| `.ted` | ~2,505 | Heightfield data grid + per-tile texture index grid (Fixed 46,987 bytes). | [terrain.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/terrain.md) |
| `.bud` | ~2,296 | Cell building scene graph; contains static meshes and vertex records. | [terrain_scene.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/terrain_scene.md) |
| `.sod` | ~2,500 | 2D collision walls mapping XZ boundary lines. | [sod.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/sod.md) |
| `.exd` | ~1,384 | Collision triangle layer; extra terrain details. | [cell_exd.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/cell_exd.md) |
| `.up` | ~222 | Upper collision triangle layer. | [cell_up.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/cell_up.md) |
| `.fx1`–`.fx7` | ~2,000 | Terrain effect and overlay layers (decoration meshes). | [terrain_layers.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/terrain_layers.md) |
| `.mud` | ~1,578 | Fixed 32,768-byte tile map (Possible mud/water heights; mostly zero). | [mud.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/mud.md) |
| `.pre` | ~2,014 | Pre-patch cell deltas (`.bud.pre`, `.sod.pre`; newer areas only). | [cell_pre.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/cell_pre.md) |
| `.post` | ~852 | Post-patch `.ted` heightfield replacements. | [cell_post.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/cell_post.md) |
| `.tol` | 3 | Large area-wide object coordinate lists (~4 MB; areas 009, 013, 100). | [tol.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/tol.md) |

---

## 2. Character & Animation Formats

Assets stored under `data/char/` managing the skeletal animation pipeline.

| Extension | Approx. Count | Purpose / Content | Target Spec |
|:---|:---|:---|:---|
| `.skn` | ~1,271 | Skinned rigid and weighted meshes for characters/monsters. | [skn.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/skn.md) |
| `.bnd` | ~350 | Skeletal joints and default bind poses. | [mesh.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/mesh.md) |
| `.mot` | ~3,892 | Skeletal animation clips matching character motions. | [animation.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/animation.md) |

---

## 3. General Meshes & Particle Effects

General-purpose visual assets, textures, and descriptors.

| Extension | Approx. Count | Purpose / Content | Target Spec |
|:---|:---|:---|:---|
| `.xobj` | ~32 | White-space tokenized ASCII static triangle meshes. | [xobj.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/xobj.md) |
| `.eff` | 6 | Basic 3D shapes and particle emitter templates. | [effects.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/effects.md) |
| `.xeff` | ~3,586 | Binary particle effect descriptors and behaviors. | [effects.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/effects.md) |
| `.ion` | 1 | Scene object mapping helper. | [ion.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/ion.md) |

---

## 4. Script & Data Tables

These files represent the configuration databases of the game client, defining items, stats, quests, and localized captions.

| Extension | Approx. Count | Purpose / Content | Target Spec |
|:---|:---|:---|:---|
| `.scr` | ~44 | Binary configuration databases (e.g. `items.scr`, `npcs.scr`, `skills.scr`). | [scr.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/scr.md) |
| `.xdb` | 6 | Multilingual localization strings and small structured tables (e.g. `msg.xdb`). | [xdb_tables.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/xdb_tables.md) |
| `.do` | ~17 | Class-specific combat stance and interface coordinates. | [config_tables.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/config_tables.md) |
| `.lua` | 3 | Configuration scripts for client options, screen layouts, and tutorial loops. | [lua.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/lua.md) |

---

## 5. Audio Formats

Sound effects and background music stored under `data/sound/`.

| Extension | Approx. Count | Purpose / Content | Target Spec |
|:---|:---|:---|:---|
| `.ogg` | ~2,107 | Streamed Ogg Vorbis audio files (2D music and 3D environment clips). | [sound_tables.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/sound_tables.md) |
| `.bgm`/`.bge` | 60 | Per-area audio tables matching coordinates to background audio. | [sound_tables.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/sound_tables.md) |
| `.run`/`.wlk`/`.eff` | 60 | Per-area coordinate tables matching terrain to footstep and movement audio. | [sound_tables.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/sound_tables.md) |

---

## 6. Textures & Image Formats

2D image assets for environments, character skins, effects, and interfaces.

| Extension | Approx. Count | Purpose / Content | Target Spec |
|:---|:---|:---|:---|
| `.dds` | ~5,180 | DirectDraw Surface textures (UI, environment textures, icon sheets). | [texture.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/texture.md) |
| `.png` | ~1,005 | Skin texture mapping files for characters and monsters. | [texture.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/texture.md) |
| `.bmp` | ~3,801 | Raw terrain lightmaps and diagnostic panels. | [texture.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/texture.md) |
| `.tga` | ~1,531 | Targa texture files used heavily by particle effects. | [texture.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/formats/texture.md) |
