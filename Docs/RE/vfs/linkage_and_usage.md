# VFS Linkage Layer and Subsystem Consumers

The virtual filesystem is not a flat bucket of files; it represents a structured graph where index manifests dynamically route raw binary blocks to specific game engine modules.

---

## 1. The Linkage Layer (Index Manifests)

The game client maps numeric resource identifiers to virtual VFS paths using a set of index tables (manifests). The core manifests are outlined below:

### A. Terrain Texturing (`bgtexture.lst`)
- **Linkage:** Terrain cells (`.ted`) contain 8-bit texture index bytes. 
- **Resolution:** The engine reads the binary `bgtexture.lst` under `data/map000/texture/`. Each 48-byte record in the `.lst` contains a `kind` flag (1 = static, other non-zero = scrolling, 0 = skipped) and a relative path. The engine maps the tile byte to the resolved `.dds` path.
- **Consumer:** `TerrainRenderer` module.

### B. Character Pipeline (`skin.txt` & `actormotion.txt`)
- **Linkage:** Spawners instantiate characters or mobs by a unique ID.
- **Resolution:**
  - `actormotion.txt` maps the ID to a character class/base skin.
  - `skin.txt` maps the class and outfit slots to specific skinned meshes (`.skn` in `data/char/skin/`) and skin textures (`.png` in `data/char/texNNNN/`).
  - Animation lists (`motlist.txt` / `bindlist.txt`) map motions to skeletal animations (`.mot` / `.bnd`).
- **Consumer:** `ActorManager` and `SkeletalAnimator`.

### C. Visual Effects (`xeffect.lst` & `bmplist.lst`)
- **Linkage:** Spells, weapon glows, and environmental particles request an effect index.
- **Resolution:** 
  - `xeffect.lst` maps numeric effect slots to descriptor files (`.xeff` under `data/effect/xeff/`).
  - `bmplist.lst` maps sprite frame indexes to effect textures (`.tga` under `data/effect/tex/`).
- **Consumer:** `EffectScheduler` and `ParticleRenderer`.

### D. User Interface Atlases (`uitex.txt`)
- **Linkage:** UI layouts (`.layout` / code builders) request a 4-digit texture ID.
- **Resolution:** `uitex.txt` maps the 4-digit ID to the exact relative path of a `.dds` atlas sheet under `data/ui/`.
- **Consumer:** `GUComponent` and `GUWindow` rendering systems.

### E. Localization Text (`msg.xdb`)
- **Linkage:** UI components and chat handler requests a unique u32 localization hash/id.
- **Resolution:** `msg.xdb` is a binary catalog of 516-byte records. It acts as the string index table mapping hashes directly to CP949 (Korean character set) strings.
- **Consumer:** `MsgXdbCatalog` and the UI text rendering engine.

---

## 2. Subsystem Consumers and Roles

Below is the mapping of which game modules request assets via `Diamond::CVFSManager` and how they consume them at runtime:

```
VFS READ PRIMITIVE (CVFSManager)
  │
  ├──► [SkeletalAnimator] ──────► Reads: .bnd, .mot
  │                               Role: Interpolates joint matrices and keyframes.
  │
  ├──► [ActorRenderer] ─────────► Reads: .skn, .png
  │                               Role: Skin-deform calculation (CPU) and vertex buffer upload.
  │
  ├──► [TerrainRenderer] ───────► Reads: .map, .ted, .sod, .exd, .up
  │                               Role: Renders heightfield grids, streams cells dynamically, 
  │                                     and populates collision vectors.
  │
  ├──► [D3DDeviceManager] ──────► Reads: .psh, .vsh, .dds, .tga, .bmp
  │                               Role: Assembles HLSL shader sources, binds textures to samplers.
  │
  ├──► [AudioEngine] ───────────► Reads: .ogg, sound tables (.bgm, .run, etc.)
  │                               Role: Feeds streamed buffers to DirectSound and coordinates footstep types.
  │
  └──► [ScriptHost / Tinker] ───► Reads: .lua, .scr, .xdb
                                  Role: Executes game options, loads item tables, and translates UI text.
```
