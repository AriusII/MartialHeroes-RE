---
verification: confirmed
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory — VFS+catalogue/char-table strings cleanly relocated, 1 re-confirmed SAME, 0 corrected; prior 2026-06-21: CYCLE 8 mount->boot-corpus->six-chain re-confirmed; prior 2026-06-19
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
cycle: CYCLE 1 — Runtime Inter-Format Assembly Graph
---

# Spec: Assembly Graph — how the VFS formats wire into a World and an Actor at runtime

> **Master synthesis (the cross-format wiring).** The per-format specs say *what each format is*;
> this file says *how the runtime composes them into each other* — which format loads/references/
> composes the next, in what order, with what keying rule — to build a complete **World** and a
> complete **Actor** from a handful of VFS ids. It is an INDEX over the detailed specs: every edge
> cross-links the authoritative per-format spec. Recovered from `doida.exe` (anchor `263bd994`),
> static IDA only. NO addresses appear here by design — functions are named by their role.
>
> Scope corrections this synthesis carries (the binary won — see each per-format spec + `journal.md`):
> live actors are **server-driven (packet 4/4), not `.arr`-spawned**; `motion_ids_b` routes **SFX/FX,
> not motion**; `creature_item.xdb` is a **held-item visual**, not a loot table; the sound "indoor
> override" is a **trade/exchange-busy** flag, not a map attribute; `citems.scr` has **10** description
> paragraphs (capacity), not 6; the `.ted` idx-1 decrement is a **resolved finalize site**.

---

## 0 · The two chains at a glance

```
WORLD-BOOT CHAIN     area → cells → terrain mesh/heights → textures → buildings → effects
                          → spawns → collision → region/zone → sky/environment
ACTOR-BAKE CHAIN     class/mob id → skin mesh → skeleton (bind) → inverse-bind bake
                          → motion clips (+ SFX) → texture/material → equipment overlay
```

The World chain is **VFS+server driven** and re-runs per streamed cell; the Actor chain is **catalogue
driven** and re-runs per spawned actor. They meet where a spawned actor is placed onto a streamed
cell's ground height.

---

## 1 · World-boot chain (per area, then per cell)

Authoritative detail: `specs/terrain-streaming.md` (streaming), `formats/area_inventory.md`
(fan-out), `structs/terrain-manager.md` (the cell store/ring + 9 slots), `formats/terrain.md`
(`.ted`/`.map` + fx-attach), `formats/bgtexture_lst.md` (texture buckets), `formats/npc_spawns.md`
(spawns).

**Phase A — area load (the area orchestrator).** Area id →
- the area **cell-key set** is read from `d<NNN>.lst`; a cell key is the packed `mapZ + 100000·mapX`;
- the area binaries load: `map<NNN>.bin`, region table, region grid, and the on-disk `npc.arr` /
  `mob.arr` (position/facing/static metadata — **not** the live-actor source, see §1 spawns);
- the sound tables, the stream radius, and the sky/environment bins load.

**Phase B — cold start (one call frame UP, in the world-enter caller).** The spawn XZ is set and a
**cold-start ring kick** primes the streaming ring around the player. (This split refines
`terrain-streaming.md §7`: the orchestrator does the area load; the caller does the kick.)

**Per cell — find/load (the streaming gate).** For each cell in radius: a membership gate
(`mapZ + 100000·mapX`) checks the **34-slot loader pool** (the pool **owns** the live cells, with
cache/recycle); a miss acquires a pool slot and opens the cell files in this synchronous order:
`.mud` → `.gad` → `.map`. The **25-slot manager ring** is a *borrowed-pointer* moving 5×5
spatial view over the pool; the **live centre cell = ring slot 12**.

> **`.gad` is an intentional dead stub.** The loader opens the `.gad` path as part of the
> synchronous cell-open sequence, but no parser or consumer of its content was located in the client.
> This is a deliberate no-op: the file path is opened and then discarded without any data being read
> into a sub-manager. Do not implement a `.gad` decoder — the file slot is inert in the runtime.

**Per cell — the `.map` parse fans the sub-assets.** Inside the `.map` parse, `DATAFILE` tokens
scoped to each section pull `.ted` / `.sod` / `.bud` / `.up` / `.fx1`–`.fx7` / `.exd` and route each
into one of the **9 per-cell sub-manager slots** (`structs/terrain-manager.md`):

| Slot | Content | Fed by |
|---|---|---|
| 0 | ground texture grid | `.ted` TextureIndexGrid |
| 1 | building / object grid | `.bud` |
| 2–8 | fx1..fx7 overlays | `.fx1`–`.fx7` (attached during the `.map` parse) |

(Collision `.sod`, height/`.mud`, region and the texture grid are held in the cell's other
sub-objects; the build/render is a separate post-parse pass.)

**Per cell — finalize order (post-parse pass).** After the `.map` parse completes, the cell finalize
pass runs sub-manager setup in this order:

1. `.ted` ground-texture grid build
2. `.bud` building/object grid build
3. `.fx1`–`.fx7` per-channel cull-grid build (seven channels in extension order)
4. **`EXTRA_TERRAIN` → `.exd`** — extra collision triangle mesh finalize (see `formats/terrain_layers.md` §3)
5. **`UP_TERRAIN` → `.up`** — upper/overhanging collision triangle mesh finalize (see `formats/terrain_layers.md` §2)
6. `.sod` collision grid finalize

The `.exd` and `.up` formats share the same 40-byte triangle record decoder (`count(u32) + count × 40B`);
they are structurally distinct from the FX group-array channels and must not be confused with them.
See `formats/terrain_layers.md` §2/§3 for their on-disk layout. Vertical role: `.up` supplies the
**floor / minimum** surface (UP_TERRAIN), `.exd` supplies the **ceiling / maximum** surface (EXTRA_TERRAIN).

**Texture resolution (global under `map000`).** `.ted` `TextureIndexGrid` byte → **idx-1 finalize**
(the −1 applies to the `.ted` texture-index byte ONLY; pool/`intTexId` accessors have no −1; clamp
`[1,count]`) → `.map` `TERRAIN/BUILDING TEXTURES[idx-1].intTexId` → `bgtexture.lst[id]` → the
`.dds` under `data/map000/texture/<rel>` (textures are **global under `map000`** for every area).
The `bgtexture.lst` **kind byte** is a single branch: `==1` → static render-object type, `!=1` →
scroll/animated; the 6-value enum is data-only (no per-value jump table). Detail:
`formats/bgtexture_lst.md`.

**Spawns (server-driven; offline-port substitution).** Live NPC/mob actors are built from the
**server area-entity snapshot (packet 4/4)** → an **880-byte spawn descriptor** → the actor-spawn
routine. The on-disk `.arr` supplies **position / facing (yaw = π/2 − value) / static metadata
only**. Visual id resolves via `mobs.scr` / `npc.scr` → the actor-visual catalogue (→ the Actor
chain, §2). The ground **Y is re-sampled from the terrain each frame**. Detail:
`formats/npc_spawns.md`, `structs/spawn_descriptor.md`.
> **Port-side note:** the offline port has no server, so it **synthesises** actors from `.arr` +
> the visual catalogue (the current Godot demo already does this). The fallback-Y race (render
> debt #2) is the assembly-level consequence of streaming-vs-snapshot arrival order — an OPEN RISK
> (§5).

---

## 2 · Actor-bake chain (per spawned actor)

Authoritative detail: `specs/skinning.md` (skin/bind/inverse-bind), `formats/bindlist.md` (preload),
`formats/actormotion.md` + `formats/animation.md` (motion), `specs/equipment_visuals.md` (overlay),
`specs/config_tables.md` (`mobs.scr` visual keying), `formats/items_scr.md` (equipment asset keys).

**Identity → model class.**
- *Player:* `(class, variant)` → the appearance-slot id `5·(class + 4·variant) − 24 ∈ {1,11,16,26}`.
- *Mob:* `mob_id → mobs.scr (appearance/skin-class field) → the appearance-key resolver → the
  actor's anim-catalogue entry → model_class_id`. The skeleton is reached **indirectly via the
  catalogue**, NOT as a literal `g{skin_class}.bnd`.

**Skin mesh.** `.skn` (`IdA` → `data/char/skin.txt` → `tex_id` → `data/char/tex{512512|10241024|…}/{id}.png`).
For equipment parts, `items.scr +0x80` → `data/char/skin/g%d.skn` (the mesh selector, printf at
spawn). Mesh-local `.skn` geometry negates X.

**Skeleton (eager preload + verbatim key).** Every `.bnd` listed in `bindlist.txt` is **opened and
parsed at boot** into a pose pool (`formats/bindlist.md`). An actor selects its skeleton by using
its **`.skn` header `id_b` VERBATIM as the pool key** (`pose_pool[id_b]`) — no `g{N}.bnd` string
formatting at the select site. For equipment parts, `items.scr +0x84` → the bind-pose pool id.

**Inverse-bind bake (the skinning-explosion fix — `specs/skinning.md`).** Skin matrices are baked at
load as a **unit quaternion**, not a 4×4:
```
localPos = conj(bindWorldQuat) ⊗ (vtx − bindWorldTrans)
```
subtract-then-rotate, parent-on-left, against the **rest/bind WORLD** transform; normals rotate only.
**Root cause of the legacy mesh explosion: skin weights index bones in BASE-RELATIVE bone-ID space
(`bone_array[id − base_id]`), NOT array-slot / palette / track order.** With base-relative indexing
the avatar deforms correctly and **can be animated** (retires render debt #1).

**Motion (clips + SFX — `formats/actormotion.md`).** The `actormotion` catalogue record (at +0x40)
supplies:
- `motion_ids_a` → action→`.mot` clip table (a[1]=idle, a[2]=walk, a[3]=run, a[4]=death,
  a[5]=mount-idle, a[6]=combat-idle …) — the "9 directions" reading is **REFUTED** (action/lifecycle
  keyed, never facing);
- `motion_ids_b` → **SFX/FX event ids, NOT secondary motion** (binary-won correction vs the old spec);
- `rate_x` / `rate_y` → per-frame move speed; `float_h` / `float_i` → footfall dust-FX descriptor/scale.

**Material / equipment overlay (`specs/equipment_visuals.md`).** Per-part mesh recomposition under
the one skeleton: the equipment GID digit selects the part column (weapon slot-14 vs non-weapon
digit math, CODE-CONFIRMED); a weapon attaches to a hand bone (dual-hand off-hand flag); a
weapon-glow tier toggler maps grade 101..109 → 1..9 (4 emitters, tier flows by register).

---

## 3 · Format → format edge table

| From | Edge (how) | To | Keying rule | Detail spec |
|---|---|---|---|---|
| area id | `d<NNN>.lst` cell-key set | cells | `mapZ + 100000·mapX` | `area_inventory.md` |
| cell | synchronous open `.mud`→`.gad`→`.map` | cell sub-managers | pool membership gate | `area_inventory.md`, `terrain-streaming.md` |
| `.map` | DATAFILE tokens in sections | `.ted`/`.sod`/`.bud`/`.fx1-7`/`.exd` | section name → slot | `terrain.md`, `structs/terrain-manager.md` |
| `.ted` | TextureIndexGrid byte → idx-1 finalize | `.map` TEXTURES[idx-1] | byte − 1 (clamp [1,count]) | `terrain.md` |
| `.map` TEXTURES | `intTexId` | `bgtexture.lst[id]` | id index | `bgtexture_lst.md` |
| `bgtexture.lst` | kind byte (==1 static / !=1 scroll) + path | `map000/texture/*.dds` | global under map000 | `bgtexture_lst.md` |
| server packet 4/4 | 880B descriptor → spawn routine | actor | entity id | `npc_spawns.md`, `structs/spawn_descriptor.md` |
| `.arr` | position/facing/static metadata | actor placement | per-spawn (NOT live source) | `npc_spawns.md` |
| `mob_id` | `mobs.scr` appearance field → catalogue | model_class_id | appearance key | `config_tables.md`, `skinning.md` |
| model_class_id | skin/bind registry | `.skn` + skeleton | catalogue lookup | `skinning.md` |
| `.skn` `id_b` | verbatim pool key | `pose_pool[id_b]` (`.bnd`) | id_b | `bindlist.md`, `skinning.md` |
| `bindlist.txt` | eager boot preload | pose pool | listed `.bnd` names | `bindlist.md` |
| `.skn` `IdA` | `skin.txt` → `tex_id` | `tex{…}/{id}.png` | id chain | `skinning.md` |
| `items.scr` | `+0x80` → `g%d.skn`, `+0x84` → bind pool id | equipment part mesh+skeleton | item id | `items_scr.md` |
| `actormotion` (+0x40) | `motion_ids_a` / `_b` | `.mot` clips / SFX ids | action index | `actormotion.md` |
| `events.scr` | lookup-by-id (item/shop/exchange UI) | event record | event_id exact-match | `events_scr.md` |
| `vehicle.xdb` / `creature_item.xdb` | runtime linkage | mount visual / held-item visual | vehicle_id / creature_key | `xdb_tables.md` |
| trade-busy flag | forces BGM `863500002` over `.mud`→`.bgm` | sound | local-player busy state | `sound.md`, `world_systems.md` |

---

## 4 · Port-side notes (faithful 1:1 choices the port must encode)

- **Offline actor synthesis.** No server ⇒ the port builds actors from `.arr` + the visual catalogue
  instead of packet 4/4. Both paths are recorded; the port substitution is the faithful offline
  equivalent (cf. the current demo's 40 NPCs).
- **Coordinate conventions.** World geometry **negates Z** (`Helpers/WorldCoordinates.ToGodot`);
  mesh-local `.skn` geometry **negates X**; effect sub-offsets are Z-negated **port-side** (campaign
  10). Cells are 1024 units on a 65×65 grid, spacing 16.
- **AreaComposer contract (for Phase 5).** cell store = 34 (owner/recycle), spatial ring = 25 (moving
  5×5 view), live centre = ring slot 12, render layers = the 9 slots; per-cell open order is
  synchronous; sub-assets come from `.map` DATAFILE tokens.
- **ActorComposer contract (for Phase 5).** bind-agnostic emit is no longer forced — the inverse-bind
  bake is recovered, so the composer can emit the bake data for an **animated** avatar; it still must
  emit a valid actor descriptor (skin id, `id_b` skeleton key, motion ids, GID list) independent of
  the mesh-build step (which is layer-05).

---

## 5 · OPEN-RISK ledger (carried; debugger out-of-scope this cycle)

| Id | Risk | Severity | Disposition |
|---|---|---|---|
| A1-6 | spawn-vs-cell-load **timing** (the Godot fallback-Y race) | wiring HIGH / timing runtime-only | debugger-pending; Phase 6 fixes the symptom (snap Y after the cell is resident) |
| A2-1 | inverse-bind absolute handedness/up-axis **label** | cosmetic | does NOT block animation; one live bone read would settle the label |
| A2-3 | unused motion slots a[0/7/8], b[0/6-8], float_c..g | low | no static consumer; left UNASSIGNED, not guessed |
| A2-5/6 | per-tier weapon-glow visual set + grade↔raw-enchant | low | capture-pending |
| A3-1 | item-side `event_id` join column | low | not byte-pinned |
| A3-2 | exact `+0x84` `g{id}.bnd/.mot` file | low | not byte-pinned |
| A3-5 | "indoor" vs trade-busy **label** | wiring HIGH / label MEDIUM | relabel applied; trade-mode track inferred |

All residuals are narrow follow-ons; none blocks the C# composers (Phase 5) or the Godot world
un-freeze (Phase 6). If a debugger lane is ever opened, these route to `re-validator`.

---

## Cross-links
World: `specs/terrain-streaming.md` · `formats/area_inventory.md` · `structs/terrain-manager.md` ·
`formats/terrain.md` · `formats/bgtexture_lst.md` · `formats/npc_spawns.md` · `structs/spawn_descriptor.md`.
Actor: `specs/skinning.md` · `formats/bindlist.md` · `formats/actormotion.md` · `formats/animation.md` ·
`specs/equipment_visuals.md` · `specs/config_tables.md` · `formats/items_scr.md`.
Other: `formats/events_scr.md` · `formats/xdb_tables.md` · `specs/sound.md` · `specs/world_systems.md` ·
`formats/scr.md` · `formats/ui_manifests.md`.
Provenance: `Docs/RE/journal.md` · glossary `Docs/RE/names.yaml`.
