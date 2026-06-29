# Docs/RE — Corpus Index

Engine target: `doida.exe` (Martial Heroes / D.O. Online, Direct3D 9 fixed-function pipeline).
Date: 2026-06-29. Total indexed: 164 files. Organized by subsystem (9 domains), then by file
extension, then by runtime struct class name. Every link is a relative path from this file.

---

## 1. By Subsystem

### 1.1 Networking & Protocol

| File | Role |
|---|---|
| [opcodes.md](opcodes.md) | Authoritative C2S/S2C opcode catalogue: major/minor pairs, direction, body size, canonical name |
| [specs/network_dispatch.md](specs/network_dispatch.md) | Master receive dispatcher, handler-table installers, slot maps, connection lifecycle, three worker threads |
| [specs/net_contracts.md](specs/net_contracts.md) | C2S↔S2C request/response pairing by feature domain; capture-pending register R-01..R-45 |
| [specs/handlers.md](specs/handlers.md) | Per-opcode (major,minor) inbound handler behaviour catalogue, §1–§24 sweep |
| [specs/connection_topology.md](specs/connection_topology.md) | Three socket subsystems, opcode-socket ownership, lobby→game server-address handoff, XTrap relay |
| [specs/login_flow.md](specs/login_flow.md) | End-to-end login→char-select→enter-game opcode ladder, server-list 8-byte record, credentials |
| [specs/world_entry.md](specs/world_entry.md) | Enter-world lifecycle: 1/9→3/5→4/1→4/4 opcode ladder, single in-flight latch, area cold-start |
| [specs/world_exit.md](specs/world_exit.md) | Logout and leave-world teardown: two mutually exclusive exit opcodes, shared tail, death/respawn flow |
| [structs/net_client.md](structs/net_client.md) | NetClient connection-owner singleton field layout (~82 KB object, embedded connection sub-object) |
| [structs/net_handler.md](structs/net_handler.md) | NetHandler S2C dispatch singleton: 5×880-byte sub-struct array, two 154-slot handler tables |
| [structs/net_packet_bodies.md](structs/net_packet_bodies.md) | C-struct offset tables for large S2C packet-body DTOs: guild, char-list, item, quest, party, skills, npc |
| [structs/secure_context.md](structs/secure_context.md) | SecureContext 11,808-byte handshake/crypto context with packet buffer and bignum key material |

### 1.2 Cryptography & Security / Anti-cheat

| File | Role |
|---|---|
| [specs/crypto.md](specs/crypto.md) | Wire byte-cipher (keyless stateless), LZ4 compression, RSA PKCS#1 v1.5 session handshake, FLINT++ RSA substrate |
| [specs/anticheat.md](specs/anticheat.md) | Three-tier anti-cheat (GGGProtect/GProtect/GXProtect), monitor thread, IAT snapshots, PEB debugger check, XTrap DLL integration |

### 1.3 VFS & Containers

| File | Role |
|---|---|
| [formats/pak.md](formats/pak.md) | .inf/.vfs VFS archive container: 24-byte header, 144-byte TOC records, 43,347 entries, no compression |
| [specs/vfs_overview.md](specs/vfs_overview.md) | VFS directory tree, 49-extension census (43,347 entries), manifest linkage, CVFSManager I/O runtime |
| [specs/vfs_loader_dispatch.md](specs/vfs_loader_dispatch.md) | VFS read-to-decode-to-build dispatch path: open router, three I/O backends, per-consumer decoder fan-out |
| [specs/asset_linkages.md](specs/asset_linkages.md) | Six VFS subsystem linkage JOIN maps: terrain, character, effects, UI, global texture pool, sound chains |
| [specs/asset_pipeline.md](specs/asset_pipeline.md) | Loader-dispatch verdict (no magic-byte sniff), GHTex named-texture cache, inter-asset linkage chains, bulk boot loader |
| [specs/resource_pipeline.md](specs/resource_pipeline.md) | Full runtime resource pipeline: VFS single open-chokepoint, boot loader, terrain streaming, per-subsystem caches |
| [vfs/vfs_master_manual.md](vfs/vfs_master_manual.md) | Consolidated VFS specification — TAINTED §2 (raw virtual addresses); deferred remediation; §1/§3/§4 partially superseded by pak.md and vfs_loader_dispatch.md |
| [vfs/README.md](vfs/README.md) | vfs/ sub-directory navigation anchor; links updated to absorbing canonical specs |

### 1.4 Asset Formats

**Terrain & world geometry**

| File | Role |
|---|---|
| [formats/terrain.md](formats/terrain.md) | Hub spec: .lst/.map/.ted core formats, height fields, 65×65 cell grid (1,024 wu spacing), texture index chain |
| [formats/terrain_layers.md](formats/terrain_layers.md) | Per-cell overlay sidecars: .fx1–.fx7 group-arrays, lighting bins, 40-byte triangle layers |
| [formats/terrain_scene.md](formats/terrain_scene.md) | Per-cell static-object placement: .map scene descriptor and .bud building-geometry blob with render-class dispatch |
| [formats/sod.md](formats/sod.md) | .sod per-cell wall-collision segments (SolidRecord 108 B, QuadRecord 48 B, slope/intercept line form) |
| [formats/cell_up.md](formats/cell_up.md) | .up upper-terrain walkable triangle surface (u32 count + count×40-byte records) |
| [formats/cell_exd.md](formats/cell_exd.md) | .exd extra-terrain walkable-surface triangle mesh (byte-identical decoder to .up; only semantic role differs) |
| [formats/bgtexture_lst.md](formats/bgtexture_lst.md) | Binary terrain/effect texture index: 1-byte kind + 47-byte relpath per 48-byte record; global under map000 |
| [formats/authoring_sidecars.md](formats/authoring_sidecars.md) | Master index of .pre/.post cell-level editor sidecars — runtime never opens them |
| [formats/area_inventory.md](formats/area_inventory.md) | VFS census: 60 registered areas, 2,503 total cells, per-area presence of every cell-file type |
| [formats/region_grid.md](formats/region_grid.md) | Runtime region\<NNN\>.bin region/zone ID grid and authoring .tol counterpart, map\<NNN\>.bin mode record |
| [formats/tol.md](formats/tol.md) | .tol authoring-only fine-resolution boolean grid (4 wu/cell; NOT loaded by shipped client) |

**Characters & animation**

| File | Role |
|---|---|
| [formats/mesh.md](formats/mesh.md) | Overview: .xobj ASCII mesh, .skn skinned mesh, .bnd bind-pose skeleton, .bud building mesh, .map cell descriptor |
| [formats/skn.md](formats/skn.md) | .skn skinned mesh on-disk layout (header + face/vertex/influence sections); CYCLE-7-refined primary reference |
| [formats/animation.md](formats/animation.md) | .mot skeletal animation clip format and runtime animation mixer model |
| [formats/actormotion.md](formats/actormotion.md) | Per-actor motion/sound/effect table: 136-byte record, 33 columns, 301 actors; idle motion at column 16 |
| [formats/bindlist.md](formats/bindlist.md) | Skeleton registry: bindlist.txt with 349 .bnd entries keyed by actor_id from .bnd header (not g{N}.bnd rule) |
| [formats/npc_spawns.md](formats/npc_spawns.md) | .arr NPC/monster spawn arrays (28-byte npc records, 20-byte mob records, headerless) |

**Effects & audio**

| File | Role |
|---|---|
| [formats/effects.md](formats/effects.md) | .xeff/.eff visual-effects subsystem: particle emitters, primitive shapes, GPU particles, effectscale.xdb, render pipeline |
| [formats/sound_tables.md](formats/sound_tables.md) | .eff/.wlk/.run/.bgm/.bge per-map sound event and music schedule tables (256×48 B records, 13,312 B files) |
| [formats/mud.md](formats/mud.md) | .mud per-cell ambient-sound zone grid (64×64 tiles × 8 bytes, headerless, fixed 32,768 B) |
| [formats/xobj.md](formats/xobj.md) | .xobj ASCII indexed-triangle-list primitive mesh + xobj.lst binary index manifest (4+34-byte records, 32 entries) |
| [formats/ion.md](formats/ion.md) | descript.ion effect-texture source descriptor — art-tool artifact NOT loaded by shipping client (runtime uses bmplist.lst) |

**Environment & sky**

| File | Role |
|---|---|
| [formats/environment_bins.md](formats/environment_bins.md) | Per-area sky/fog/dome/weather binary file families (map_option%d.bin, fog%d.bin, stardome/clouddome/weather/rain series) |
| [formats/sky.md](formats/sky.md) | .box sky-dome geometry (absent from VFS) + .bin family parser view + D3D9 render-pass sequence |
| [formats/shaders.md](formats/shaders.md) | .psh/.vsh D3D9 shader assembly text files + cel/glow offscreen post-process pipeline (5 shaders, 3 RTs) |
| [formats/texture.md](formats/texture.md) | DDS/TGA/BMP/PNG image formats — no custom texture format; all detection delegated to D3DX9 |

**Configuration & data tables**

| File | Role |
|---|---|
| [formats/config_tables.md](formats/config_tables.md) | Omnibus .scr/.do/.ini/.xdb configuration catalogue (~50 client data asset files) |
| [formats/scr.md](formats/scr.md) | .scr binary fixed-stride record-table container format and small-table field survey |
| [formats/items_scr.md](formats/items_scr.md) | items.scr (variable-stride, 90,937 records) and citems.scr (fixed 1,052-byte stride, 512 records) |
| [formats/events_scr.md](formats/events_scr.md) | events.scr (520-byte records, 1,848 rows) and autoquestion_cl.scr anti-bot table (92-byte records) |
| [formats/items_csv.md](formats/items_csv.md) | items.csv flat comma-delimited item table — dev/authoring export only, NOT loaded by shipping client |
| [formats/game_ver.md](formats/game_ver.md) | game.ver binary client version stamp (variable-length u32LE list; min 7 elements; two confirmed runtime roles) |
| [formats/mi.md](formats/mi.md) | .mi mob-info data file (present in VFS, 21×28-byte records; NO client loader confirmed) |
| [formats/xdb_tables.md](formats/xdb_tables.md) | Five small .xdb flat-array tables (effectscale 8 B, buff_icon_position 12 B, vehicle 52 B, creature_item 48 B; actor_size DEAD) |
| [formats/misc_data.md](formats/misc_data.md) | Multi-format hub: .xdb variants, mobinfo.mi (DEAD), .tol, descript.ion, msg.xdb, mapsetting.scr, regiontableNNN.bin |
| [formats/msg_xdb.md](formats/msg_xdb.md) | msg.xdb CP949 caption string catalogue (516-byte records, 2,644 entries) |
| [formats/text_tables.md](formats/text_tables.md) | Bulk CP949 .txt data tables inside VFS (619-file census, tokenizer rules, per-table column specs) |

**UI assets & scripts**

| File | Role |
|---|---|
| [formats/ui_manifests.md](formats/ui_manifests.md) | UI manifest text files (uitex.txt, skillicon.txt, crestlist.txt, texturelist.txt + .do family) |
| [formats/lua.md](formats/lua.md) | .lua files — Lua 5.1.2 source text (PUC-Rio embedded interpreter); five shipped scripts documented |
| [formats/macro_file.md](formats/macro_file.md) | .mhm (Martial Heroes Macro) — project-owned format; NOT reverse-engineered from doida.exe |

### 1.5 3D Engine & Rendering

| File | Role |
|---|---|
| [specs/rendering.md](specs/rendering.md) | D3D9 per-frame draw loop, render-state cache, glow/bloom 3-RT chain, char-select 3D preview, shadow projection |
| [specs/render_pipeline.md](specs/render_pipeline.md) | Frame orchestration, multi-view driver loop, per-view two-phase processing, render-pass installer, scene-graph traversal |
| [specs/post_processing.md](specs/post_processing.md) | Cel/glow post-process chain: ten-slot glow_chain COM array, five shader-handle slots, RT sizing, extract/blur pass recipes |
| [specs/character_rendering.md](specs/character_rendering.md) | Per-actor cel/toon draw path, VS/PS constant registers, status-tint palette (9 states), blob shadow, glow/PowerShader chain |
| [specs/skinning.md](specs/skinning.md) | CPU linear-blend skinning pipeline: vertex deform chain, inverse-bind bake, bone hierarchy, .mot keyframe sampling, Cal3D class catalog |
| [specs/transparency_sort.md](specs/transparency_sort.md) | Alpha-pass draw ordering: definitive no-depth-sort verdict, three-tier ordering mechanism |
| [specs/occlusion_culling.md](specs/occlusion_culling.md) | Definitive negative — no occlusion culling; three draw-set visibility regimes: frustum/cell-ring/distance |
| [specs/nameplate_render.md](specs/nameplate_render.md) | World-anchored 2D overhead overlays (nameplates, HP/MP bars, status icons) and 3D billboard floating digit glyphs |
| [specs/screen_effects.md](specs/screen_effects.md) | Resolved-negative: no general full-screen post-process subsystem; per-character status-tint palette attributed |
| [specs/paperdoll_render.md](specs/paperdoll_render.md) | Character preview in UI panels — definitive negative on private RTT; HUD ortho placement and char-select world-space path |
| [specs/actor_lod.md](specs/actor_lod.md) | Draw-distance cull (1,000 wu XZ), frustum bounding-sphere cull, skinning-quality LOD bands; no billboard/polygon LOD |
| [specs/weather_render.md](specs/weather_render.md) | Rain and snow precipitation particle systems: geometry, fall simulation, quality alpha, D3D9 render-pass recipe |
| [specs/texture_upload.md](specs/texture_upload.md) | DDS/image-bytes to GHTex to IDirect3DTexture9 upload pipeline: lazy-load, eviction, per-caller variants; GHTex vtable detail |
| [specs/font_glyph_render.md](specs/font_glyph_render.md) | FontTable singleton (15 ID3DXFont faces), shared text draw entry, CP949 column helpers, GDI image-bake path |
| [specs/minimap_render.md](specs/minimap_render.md) | Minimap rendering method: baked 2D GUI blit confirmed on all three surfaces (NOT render-to-texture) |
| [structs/drawable_geometry.md](structs/drawable_geometry.md) | Diamond render-engine drawable leaf hierarchy: GObject, GDrawable, GGeometry, StaticSkin, Skin, SwordLightEffect |
| [structs/dynamic_vertex_buffers.md](structs/dynamic_vertex_buffers.md) | Three D3D9 geometry-submission paths: user-pointer UP draws, shared GParticleBuffer pool, per-instance dynamic VB/IB |
| [structs/cull_pipeline.md](structs/cull_pipeline.md) | GCull / GCullPipeline / GStatsCull frustum-cull pipeline class hierarchy and field tables |
| [structs/render_state.md](structs/render_state.md) | GRenderState — 18-slot polymorphic hierarchy (alpha, blend, cull, depth, material, fog, texture, etc.) with D3D9 RS keys |
| [structs/renderer_device.md](structs/renderer_device.md) | GHRenderer D3D9 device wrapper singleton (~179 KB static allocation): present-params, camera-basis cache, post-process COM slots |
| [structs/render_driver.md](structs/render_driver.md) | EngineSceneMachine frame-driver struct: 56-byte field layout with embedded view-controller and frame-rate bookkeeping |
| [structs/gview.md](structs/gview.md) | Diamond::GView per-view render object: 308-byte layout with camera, cull pipeline, render-pass callbacks, frame-driver wiring |
| [structs/perspective_camera.md](structs/perspective_camera.md) | GPerspectiveCamera: 184-byte projection-only camera with near/far, 6-plane frustum, FOV, aspect, device back-pointer |
| [structs/particle_emitter.md](structs/particle_emitter.md) | GPU particle emitter runtime object graph: ParticleEffect (60 B), GParticleBuffer (248 B), GParticle_State (32 B), GParticle_Render (20 B) |
| [structs/shadow_projector.md](structs/shadow_projector.md) | ShadowProjector singleton: 316-byte static global with shadow-map RT path and blob-quad fallback |
| [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) | GObject/GNode/GGroup/GScene/GTransform/GSwitch/GGeode/GViewPlatform/GLight RTTI-confirmed hierarchy with full offset tables |
| [structs/texture_manager.md](structs/texture_manager.md) | GTextureManager / GTexture / FrameTickScheduler cache container; GHTex vtable detail (absorbed from ghtex.md) |
| [structs/skybox.md](structs/skybox.md) | EnvSky_Manager runtime object layouts: singleton (~704 B), SkyBoxMesh (644 B embedded), MoonBillboard, SunBillboard, StarDome, CloudDome |

### 1.6 World & Scenes

| File | Role |
|---|---|
| [specs/environment.md](specs/environment.md) | Per-area sky/lighting hub, day/night keyframe LUT, fog pipeline, point-light runtime, display.lua brightness scalars |
| [specs/terrain-streaming.md](specs/terrain-streaming.md) | Player-centered cell streaming ring, per-frame load/cull, GPU terrain render lane with shadow projector |
| [specs/whole_map_assembly.md](specs/whole_map_assembly.md) | Offline cell enumeration, world-space placement, texture pool, live world-entry chain, two-object world-manager model |
| [specs/entity_placement.md](specs/entity_placement.md) | Actor ground-height sampling (per-triangle plane interpolation), static object placement, wind-sway FX layers |
| [specs/assembly_graph.md](specs/assembly_graph.md) | Master cross-format wiring: world-boot chain (area→cells→terrain/tex/buildings/effects/spawns) and actor-bake chain |
| [specs/bud_loader.md](specs/bud_loader.md) | .bud building runtime load chain: path resolution, VFS open/read, blob decode into BudObject array, AABB/cull-budget |
| [specs/terrain_decals.md](specs/terrain_decals.md) | Ground-overlay and gameplay-decal systems: resolved-negative + actor blob-shadow quad fully recovered |
| [specs/water.md](specs/water.md) | Water rendering: resolved-negative — no water renderer, geometry, RTT pass, or asset loader exists in binary |
| [specs/effects.md](specs/effects.md) | XEffect runtime instantiation, class hierarchy, pool families, spawn pathways, per-frame tick, bone attachment, SwordLightEffect |
| [specs/effect-scheduling.md](specs/effect-scheduling.md) | Per-frame tick spine, four-manager fan-out, deadline-arm primitive, 10,001 sorted-event queue, death/respawn FSM scheduling |
| [specs/sound.md](specs/sound.md) | Runtime audio engine: DirectSound device, OggVorbis codec, SoundManager families, ambient driver, BGM/SFX router |
| [specs/physics_collision.md](specs/physics_collision.md) | Terrain raycasting quadtree and geometric intersection algorithms (AABB/sphere/triangle) — TAINTED SEVERE; deferred remediation pass required |
| [scenes/ingame_composition.md](scenes/ingame_composition.md) | In-game scene composition: 2D HUD over 3D world, single backbuffer two-phase model, render bucket order |
| [scenes/scene_state_machine.md](scenes/scene_state_machine.md) | Cross-cutting scene/game FSM: 8-case WinMain dispatch, 0→1→2→3/4→4→5 ordering, per-state construction/teardown |
| [structs/terrain-manager.md](structs/terrain-manager.md) | TerrainLoader + TerrainManager field offset tables: 34-slot cell pool and 25-slot borrowed-pointer ring |
| [structs/terrain_cell.md](structs/terrain_cell.md) | TerrainCell: 24,712-byte per-cell runtime object with spatial-acceleration grids and 9 layer pointer slots |
| [structs/bud_object.md](structs/bud_object.md) | BudObject 116-byte runtime mass-object/foliage instance; corrects mesh.md LOD-block misidentification |
| [structs/environment_light_scene.md](structs/environment_light_scene.md) | EnvironmentLightScene hub: GLight sub-object 184-byte layout, D3DLIGHT9 embed, eight GLight sub-objects, full hub offset table |
| [structs/anim_runtime.md](structs/anim_runtime.md) | Animation runtime cluster: CoreAnimation, AnimMixer, ActorAnimationCycle, ActorAnimationAction, AnimCatalog record layouts |

### 1.7 Gameplay

| File | Role |
|---|---|
| [specs/combat.md](specs/combat.md) | Client-side combat-stat aggregation pipeline, in-world attack loop, server-authoritative damage model |
| [specs/skills.md](specs/skills.md) | Skill cast pipeline, effect dispatch, buff model, skills.scr field layout |
| [specs/skill_trees.md](specs/skill_trees.md) | Skill tree model: GlobalCategory pages, prerequisite edges, rank-tier axis, trainer NPC gate, learn/hotbar-bind opcodes; respec confirmed absent |
| [specs/buffs.md](specs/buffs.md) | Per-actor buff/debuff: 30-slot table (12 B/slot), 4,000 ms periodic tick, buff-id effect-kind dispatch, HUD mirror |
| [specs/progression.md](specs/progression.md) | Character progression: XP gain, level-up, rank-XP, stat-allocation editor, skill-point counter, six wire channels |
| [specs/quests.md](specs/quests.md) | Quest and NPC dialog: opcode routing, C2S 2/28, S2C 5/68 and 5/73, quest-log window, eligibility evaluator, data tables |
| [specs/npc_interaction.md](specs/npc_interaction.md) | NPC click-router, KIND→panel dispatch, shop/repair/storage service panels, C2S opcode map |
| [specs/inventory_trade.md](specs/inventory_trade.md) | Inventory/equipment/shop/item-upgrade/player-trade/ground-item: C2S opcodes, slot model, bag arrays, validator gates |
| [specs/crafting.md](specs/crafting.md) | Single production/crafting subsystem: products.scr recipe table, ingredient/result layout, C2S commit and S2C result opcodes |
| [specs/mail.md](specs/mail.md) | Mail/delivery-inbox (C2S 2/71, S2C 4/70) and carrier-pigeon send (C2S 2/70): 8-slot inbox model |
| [specs/pvp.md](specs/pvp.md) | PvP axes: fame, public-peace, PK mode toggle, two-side brood-war faction; two confirmed absences |
| [specs/chat.md](specs/chat.md) | Chat: C2S (2:7) single-sender + (3:21) dispatcher, channel codes {0,1,2,3,4,6,7,9,13,15}, 1,000-line ring, overhead speech bubbles |
| [specs/pets.md](specs/pets.md) | Confirmed-absent pet/companion/summon system; reclassification of PetPanel, creature_item, and summon seeds |
| [specs/social.md](specs/social.md) | Social wire protocol: chat channels, whisper, party, guild, combined FATE friend/block/relation system |
| [specs/world_systems.md](specs/world_systems.md) | In-world gameplay systems master index: server-authoritative model, opcode dispatch tables, regional systems, confirmed-absent subsystems |
| [specs/equipment_visuals.md](specs/equipment_visuals.md) | Equip-change per-part rebuild, GID formulas, part-slot table, weapon bone-attach mechanism, dual-hand discriminator, weapon-glow tier toggler |
| [specs/equipment_attach_render.md](specs/equipment_attach_render.md) | Per-frame weapon rigid-follow compose, hand-bone resolution table, SwordLightEffect ribbon, JointXEffect slot iteration (draw-time side) |
| [specs/minimap.md](specs/minimap.md) | Minimap & world map: HUD radar, full-screen map, tiled big-map, world→pixel projection, per-cell tile streaming |
| [structs/actor.md](structs/actor.md) | In-world entity (Actor) runtime object layout: embedded SpawnDescriptor, vitals/position/lifecycle/buff/equip fields |
| [structs/stats.md](structs/stats.md) | Vital stats max-HP/max-MP formula: three-stage derivation from primary stats, equipment, set-bonus, level-base, class table |
| [structs/item.md](structs/item.md) | Item wire/runtime structures: ItemSlotRecord (16-byte), EquipTable, bag soft cap, upgrade constants, item-actor template offsets |
| [structs/skill.md](structs/skill.md) | Skill subsystem: skills.scr catalog (variable-stride record), hotbar tables (240 slots), wire packets, AoE shape modes |
| [structs/quest_record.md](structs/quest_record.md) | Quest template record from quests.scr: single 4,960-byte stride with eligibility gates, objective sub-array, high-region gate |
| [structs/npc.md](structs/npc.md) | NPC/monster: mobs.scr 488-byte template, npc.arr 28-byte spawn record, npc.scr 404-byte interaction, npcs.scr relationship table |
| [structs/spawn_descriptor.md](structs/spawn_descriptor.md) | SpawnDescriptor 880-byte field layout: equipment table (20×16), model-class inputs, HP qword, carried by 5/3 and 3/1 |

### 1.8 UI & Frontend

| File | Role |
|---|---|
| [specs/ui_system.md](specs/ui_system.md) | GU widget toolkit: class hierarchy, vtable contract, render path, input/capture, font system, screen layouts, 178-slot HUD roster, 9-state scene lifecycle |
| [specs/ui_hud_layout.md](specs/ui_hud_layout.md) | In-game HUD panel placement for MainMaster 223-slot service table; build vs reconfigure routine split; slot roster |
| [specs/ui_event_dispatch.md](specs/ui_event_dispatch.md) | GU widget event dispatch FSM: hit-test, hover enter/leave latch, press/release click-capture, synthetic CLICK generation |
| [specs/input_ui.md](specs/input_ui.md) | Input dispatch: WndProc mouse path, DirectInput8 keyboard thread, cross-thread ring buffer, UI tree hit-test and chain-of-responsibility |
| [specs/gui_framework.md](specs/gui_framework.md) | Diamond UI 2D C++ object model: GUComponent/GUPanel/GUButton/GULabel/GUWindow memory layout, alpha-fade rendering, text draw |
| [specs/frontend_scenes.md](specs/frontend_scenes.md) | Complete front-end scene flow at UI/control/flow level: login, server-select, char-select/create/delete/rename, enter-world handoff |
| [specs/frontend_layout_tables.md](specs/frontend_layout_tables.md) | Hard-coded pixel geometry oracle for login/PIN/server-list/load/opening screens; supersedes approximate coords in frontend_scenes.md |
| [specs/character_creation.md](specs/character_creation.md) | Character creation request (1/6, 52-byte body), point-buy model (5 pts, floor 10), class remap, @BLANK@ sentinel |
| [specs/login.md](specs/login.md) | Login scene state machine, 73-widget layout, PIN modal scramble, lobby port-10000 handshake, credential-string contract |
| [specs/intro_sequence.md](specs/intro_sequence.md) | Post-login OpeningWindow intro: scenario crawl animation, four-panel slideshow FSM, DDS textures, alpha crossfade |
| [specs/cash_shop_browser.md](specs/cash_shop_browser.md) | Cash Shop embedded IE/ActiveX OLE container: CWebContainer / CWebEventSink COM layout, WndProc message handling, navigation |
| [structs/gucomponent.md](structs/gucomponent.md) | GUComponent / GUPanel base UI-widget byte-offset layout: 13/14-slot vtable hierarchy, geometry, alpha, auto-hide, child-vector |
| [structs/guwindow.md](structs/guwindow.md) | GUWindow multiple-inheritance layout: primary chain + CmdHandler MI sub-object + embedded GView; MainWindow/LoginWindow sizes |
| [scenes/login.md](scenes/login.md) | Login scene dossier (engine state 1): LoginWindow, 73-widget tree, 6-substate curtain sub-FSM, credential/PIN flow, RSA handshake |
| [scenes/opening.md](scenes/opening.md) | Opening scene dossier (engine state 3): COpeningWindow, two GU children, scenario crawl, 4-banner slideshow FSM, SKIP gate |
| [scenes/charselect.md](scenes/charselect.md) | Character-Select scene dossier (engine state 4): SelectWindow, 127-widget tree, 5-slot 880-byte descriptor model, 3D preview scene |
| [scenes/ingame.md](scenes/ingame.md) | In-game HUD cartography (engine state 5): MainWindow, 178-slot panel array, all RTTI classes resolved, toggle keymap |
| [scenes/gu_2d_framework.md](scenes/gu_2d_framework.md) | Shared Diamond::GU* 2D framework mechanics: texture handle resolution, BuildImageComponent, three alignment modes, 15-slot HANGUL font table |
| [scenes/frontend_ui_components.md](scenes/frontend_ui_components.md) | Cross-scene 2D GUI index for all six engine states: widget groups, shared vocabulary, 2D-over-3D composition, global lifetime |

### 1.9 Client Lifecycle

| File | Role |
|---|---|
| [specs/client_workflow.md](specs/client_workflow.md) | Master end-to-end client specification: executive overview, boot/init, frame loop, scene machine, module interconnection matrix |
| [specs/client_runtime.md](specs/client_runtime.md) | Runtime engine subsystems in depth: boot timeline, frame loop, sound model, UI widget tree, render pipeline, scene lifecycle, world scene |
| [specs/game_loop.md](specs/game_loop.md) | Main loop bootstrap order, 4-phase per-frame loop, software 60 FPS cap, logic/render decoupling, VFS mount, D3D device lifecycle |
| [specs/camera_movement.md](specs/camera_movement.md) | Camera view modes (Third/First/Static/Gamble/Event/Select via C++ polymorphism) and client-side movement/collision pipeline (65° FOV) |
| [specs/lua_scripting.md](specs/lua_scripting.md) | Lua scripting subsystem: VM identity Lua 5.1.2, lua_tinker binding, cpp_load global, host-pulls-from-scripts control direction |
| [specs/lua-config.md](specs/lua-config.md) | Lua config & string-table engine: config-script set, named integer/string globals, boot flags, getTableString/getTableStringByID API |
| [specs/binary_coverage_map.md](specs/binary_coverage_map.md) | doida.exe coverage map: ~25,792 total functions, ~5,110 named at IDB anchor f61f66a9; per-subsystem coverage tier |
| [scenes/init.md](scenes/init.md) | Init scene dossier (engine state 0): one-time bootstrap, VFS mount, D3D9 device/window bring-up, 15 font slots; transitions to Login |
| [scenes/load.md](scenes/load.md) | Load scene dossier (engine state 2): 48-file ordered boot corpus, ABOVE_NORMAL background worker, 9,395,240 progress denominator |
| [structs/runtime_singletons.md](structs/runtime_singletons.md) | Runtime singletons catalog (19+ Meyers singletons, key field maps, construction order) plus binary module map |
| [README.md](README.md) | Clean-room firewall doctrine and RE knowledge base directory layout |

---

## 2. By File Format / Extension

| Extension | Doc |
|---|---|
| .arr (spawn arrays) | [formats/npc_spawns.md](formats/npc_spawns.md) |
| .bge (BGM-effect event table) | [formats/sound_tables.md](formats/sound_tables.md) |
| .bgm (background music schedule) | [formats/sound_tables.md](formats/sound_tables.md) |
| .bin (map_option / fog / dome / rain / weather families) | [formats/environment_bins.md](formats/environment_bins.md) |
| .bin (region\<NNN\>.bin zone ID grid) | [formats/region_grid.md](formats/region_grid.md) |
| .bmp (runtime image) | [formats/texture.md](formats/texture.md) |
| .bnd (bind-pose skeleton) | [formats/mesh.md](formats/mesh.md), [formats/bindlist.md](formats/bindlist.md) |
| .box (sky-dome geometry) | [formats/sky.md](formats/sky.md) |
| .bud (building / foliage mesh blob) | [formats/terrain_scene.md](formats/terrain_scene.md), [specs/bud_loader.md](specs/bud_loader.md) |
| .csv (items — authoring only) | [formats/items_csv.md](formats/items_csv.md) |
| .dds (DirectDraw Surface) | [formats/texture.md](formats/texture.md) |
| .do (config/stance/emoticon/errorinfo family) | [formats/config_tables.md](formats/config_tables.md), [formats/ui_manifests.md](formats/ui_manifests.md) |
| .eff (visual effect definition) | [formats/effects.md](formats/effects.md) |
| .eff (sound event table — same extension, distinct role) | [formats/sound_tables.md](formats/sound_tables.md) |
| .exd (extra walkable surface) | [formats/cell_exd.md](formats/cell_exd.md) |
| .fx1–.fx7 (terrain overlay group-arrays) | [formats/terrain_layers.md](formats/terrain_layers.md) |
| .inf (VFS index file) | [formats/pak.md](formats/pak.md) |
| .ini (config) | [formats/config_tables.md](formats/config_tables.md) |
| .ion (descript.ion sidecar) | [formats/ion.md](formats/ion.md) |
| .lst (bgtexture binary index) | [formats/bgtexture_lst.md](formats/bgtexture_lst.md) |
| .lst (area cell list) | [formats/terrain.md](formats/terrain.md) |
| .lua (Lua 5.1.2 source) | [formats/lua.md](formats/lua.md) |
| .map (cell descriptor / scene descriptor) | [formats/terrain_scene.md](formats/terrain_scene.md) |
| .mhm (project macro format) | [formats/macro_file.md](formats/macro_file.md) |
| .mi (mob-info data) | [formats/mi.md](formats/mi.md) |
| .mot (skeletal animation clip) | [formats/animation.md](formats/animation.md) |
| .mud (ambient sound zone grid) | [formats/mud.md](formats/mud.md) |
| .pak / .vfs (VFS archive) | [formats/pak.md](formats/pak.md) |
| .png (character textures) | [formats/texture.md](formats/texture.md) |
| .post / .ted.post (editor sidecar — runtime ignored) | [formats/authoring_sidecars.md](formats/authoring_sidecars.md) |
| .pre / .bud.pre / .sod.pre (editor sidecar — runtime ignored) | [formats/authoring_sidecars.md](formats/authoring_sidecars.md) |
| .psh (D3D9 pixel shader assembly) | [formats/shaders.md](formats/shaders.md) |
| .run (run-cycle sound table) | [formats/sound_tables.md](formats/sound_tables.md) |
| .scr (binary fixed-stride record table) | [formats/scr.md](formats/scr.md) |
| .skn (skinned mesh) | [formats/skn.md](formats/skn.md) |
| .sod (wall-collision segment file) | [formats/sod.md](formats/sod.md) |
| .ted (terrain height data) | [formats/terrain.md](formats/terrain.md) |
| .tga (Targa image) | [formats/texture.md](formats/texture.md) |
| .tol (authoring boolean grid — not runtime) | [formats/tol.md](formats/tol.md) |
| .txt (bulk CP949 data tables inside VFS) | [formats/text_tables.md](formats/text_tables.md) |
| .up (upper walkable surface) | [formats/cell_up.md](formats/cell_up.md) |
| .vfs (VFS archive payload) | [formats/pak.md](formats/pak.md) |
| .vsh (D3D9 vertex shader assembly) | [formats/shaders.md](formats/shaders.md) |
| .wlk (walk-cycle sound table) | [formats/sound_tables.md](formats/sound_tables.md) |
| .xdb (flat-array config tables) | [formats/xdb_tables.md](formats/xdb_tables.md), [formats/misc_data.md](formats/misc_data.md) |
| .xeff (visual effect definition — XML variant) | [formats/effects.md](formats/effects.md) |
| .xobj (ASCII indexed-triangle mesh) | [formats/xobj.md](formats/xobj.md) |

---

## 3. By Runtime Struct

Alphabetical by class/struct name. Where a struct was absorbed into another file (e.g. GHTex into texture_manager.md), the absorbing file is shown.

| Struct / Class | Doc |
|---|---|
| Actor | [structs/actor.md](structs/actor.md) |
| ActorAnimationAction | [structs/anim_runtime.md](structs/anim_runtime.md) |
| ActorAnimationCycle | [structs/anim_runtime.md](structs/anim_runtime.md) |
| AnimCatalog | [structs/anim_runtime.md](structs/anim_runtime.md) |
| AnimMixer | [structs/anim_runtime.md](structs/anim_runtime.md) |
| BudObject | [structs/bud_object.md](structs/bud_object.md) |
| CloudDome | [structs/skybox.md](structs/skybox.md) |
| CoreAnimation | [structs/anim_runtime.md](structs/anim_runtime.md) |
| CWebContainer | [specs/cash_shop_browser.md](specs/cash_shop_browser.md) |
| CWebEventSink | [specs/cash_shop_browser.md](specs/cash_shop_browser.md) |
| EngineSceneMachine | [structs/render_driver.md](structs/render_driver.md) |
| EnvironmentLightScene | [structs/environment_light_scene.md](structs/environment_light_scene.md) |
| EnvSky_Manager | [structs/skybox.md](structs/skybox.md) |
| FrameTickScheduler | [structs/texture_manager.md](structs/texture_manager.md) |
| GCull | [structs/cull_pipeline.md](structs/cull_pipeline.md) |
| GCullPipeline | [structs/cull_pipeline.md](structs/cull_pipeline.md) |
| GDrawable | [structs/drawable_geometry.md](structs/drawable_geometry.md) |
| GGeode | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| GGeometry | [structs/drawable_geometry.md](structs/drawable_geometry.md) |
| GGroup | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| GHRenderer | [structs/renderer_device.md](structs/renderer_device.md) |
| GHTex | [structs/texture_manager.md](structs/texture_manager.md) |
| GLight | [structs/environment_light_scene.md](structs/environment_light_scene.md) |
| GNode | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| GObject (drawable leaf) | [structs/drawable_geometry.md](structs/drawable_geometry.md) |
| GObject (scene-graph node) | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| GParticle_Render | [structs/particle_emitter.md](structs/particle_emitter.md) |
| GParticle_State | [structs/particle_emitter.md](structs/particle_emitter.md) |
| GParticleBuffer | [structs/particle_emitter.md](structs/particle_emitter.md) |
| GPerspectiveCamera | [structs/perspective_camera.md](structs/perspective_camera.md) |
| GRenderState | [structs/render_state.md](structs/render_state.md) |
| GScene | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| GStatsCull | [structs/cull_pipeline.md](structs/cull_pipeline.md) |
| GSwitch | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| GTexture | [structs/texture_manager.md](structs/texture_manager.md) |
| GTextureManager | [structs/texture_manager.md](structs/texture_manager.md) |
| GTransform | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| GUButton | [structs/gucomponent.md](structs/gucomponent.md) |
| GUComponent | [structs/gucomponent.md](structs/gucomponent.md) |
| GULabel | [structs/gucomponent.md](structs/gucomponent.md) |
| GUPanel | [structs/gucomponent.md](structs/gucomponent.md) |
| GUWindow | [structs/guwindow.md](structs/guwindow.md) |
| GView | [structs/gview.md](structs/gview.md) |
| GViewPlatform | [structs/scene_graph_nodes.md](structs/scene_graph_nodes.md) |
| ItemSlotRecord | [structs/item.md](structs/item.md) |
| MoonBillboard | [structs/skybox.md](structs/skybox.md) |
| NetClient | [structs/net_client.md](structs/net_client.md) |
| NetHandler | [structs/net_handler.md](structs/net_handler.md) |
| ParticleEffect | [structs/particle_emitter.md](structs/particle_emitter.md) |
| SecureContext | [structs/secure_context.md](structs/secure_context.md) |
| ShadowProjector | [structs/shadow_projector.md](structs/shadow_projector.md) |
| Skin | [structs/drawable_geometry.md](structs/drawable_geometry.md) |
| SkyBoxMesh | [structs/skybox.md](structs/skybox.md) |
| SpawnDescriptor | [structs/spawn_descriptor.md](structs/spawn_descriptor.md) |
| StarDome | [structs/skybox.md](structs/skybox.md) |
| StaticSkin | [structs/drawable_geometry.md](structs/drawable_geometry.md) |
| SunBillboard | [structs/skybox.md](structs/skybox.md) |
| SwordLightEffect | [structs/drawable_geometry.md](structs/drawable_geometry.md) |
| TerrainCell | [structs/terrain_cell.md](structs/terrain_cell.md) |
| TerrainLoader | [structs/terrain-manager.md](structs/terrain-manager.md) |
| TerrainManager | [structs/terrain-manager.md](structs/terrain-manager.md) |

---

## 4. Definitive Negatives (what the client does NOT have)

Features explicitly investigated and confirmed absent from the binary:

- **No water renderer** — no water geometry, RTT pass, or asset loader exists. See [specs/water.md](specs/water.md).
- **No gameplay ground-decals** — only actor blob-shadow quad (recovered). No ground-painted texture channels for effects or map markers. See [specs/terrain_decals.md](specs/terrain_decals.md).
- **No depth-sort on alpha pass** — three-tier ordering mechanism confirmed; no sort by camera distance. See [specs/transparency_sort.md](specs/transparency_sort.md).
- **No occlusion culling / portal / PVS / hardware occlusion query** — draw-set visibility uses frustum/cell-ring/distance only. See [specs/occlusion_culling.md](specs/occlusion_culling.md).
- **No paperdoll render-to-texture** — character preview in UI panels does not use a private RTT; HUD ortho placement or world-space path is used instead. See [specs/paperdoll_render.md](specs/paperdoll_render.md).
- **Minimap is a pre-baked BMP blit** — confirmed NOT 3D render-to-texture on all three surfaces. See [specs/minimap_render.md](specs/minimap_render.md).
- **No actor mesh-LOD / impostor** — only draw-distance distance cull and skinning-quality LOD bands exist; no polygon reduction or billboard substitution. See [specs/actor_lod.md](specs/actor_lod.md).
- **World geometry is fixed-function; cel/glow shaders drive post-process only** — .psh/.vsh shaders operate on the offscreen cel/glow chain; terrain, static objects, and the sky are rendered via the D3D9 fixed-function pipeline. See [formats/shaders.md](formats/shaders.md) and [specs/post_processing.md](specs/post_processing.md).
- **No pet / companion / summon system** — PetPanel reclassified as couple/partner window; creature_item is a cosmetic prop; summon is an item particle. See [specs/pets.md](specs/pets.md).
- **No respec** — skill-tree respec is confirmed absent. See [specs/skill_trees.md](specs/skill_trees.md).

---

## 5. Provenance & Firewall

All committed specs are clean-room derived from the `doida.exe` IDB (SHA prefix `f61f66a9`), rewritten in neutral English prose — no Hex-Rays pseudo-C, no raw addresses, no decompiler autonames. Each spec carries a `verification:` banner pinned to the IDB build.

- `packets/` holds the wire-field YAML specs; the opcode catalogue is in [opcodes.md](opcodes.md).
- `names.yaml` (canonical glossary) and `journal.md` (provenance audit trail) are orchestrator-owned — never edited by authoring agents.
- `_dirty/` is gitignored — decompiler-room output; never surfaces in a committed doc.
- Two files remain tainted and await a dedicated remediation pass before their content is fully absorbed: [specs/physics_collision.md](specs/physics_collision.md) (SEVERE) and [vfs/vfs_master_manual.md](vfs/vfs_master_manual.md) (§2 only).
- Legal basis: EU Software Directive 2009/24/EC Art. 6 (decompilation for interoperability). The clean-room firewall is the mechanism that keeps this corpus lawful.
