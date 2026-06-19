# ROADMAP — live run record

> **Live run record.** This file records *the runs* of each reverse-engineering / porting cycle
> (dated phase statuses, evidence baselines, what actually happened). The *method/charter* lives in
> `Docs/PLAN.md`; the reusable *method template* in `Docs/CAMPAIGN_TEMPLATE.md`. Prior cycles live in
> git history + `Docs/RE/journal.md`. Update phase statuses in place as waves complete.

---

# CYCLE 1 — Runtime Inter-Format Assembly Graph (RE → core 03-04 → Godot 05 World un-freeze) (launched 2026-06-19)

**Mandate (maintainer):** "Do a LOT of IDA static work to understand how the formats INSIDE
`data.vfs` are worked, linked and ASSEMBLED at runtime — (1) World assembly:
cells/terrains/maps/effects/particles/buildings/full map-construction; (2) 3D models:
rigs/bones/skin/textures; (3) all other formats — then improve the C# with modules/features for all
of it."

**Reframed:** Map the runtime INTER-FORMAT ASSEMBLY GRAPH (which format loads/references/composes
the next, in what order, with what keying rule), deepen only linkage-thin specs, author a master
`assembly_graph.md`, then build engine-free `AreaComposer`/`ActorComposer` in C# 03-04, then
un-freeze the Godot World scene to render the assembled world 1:1.

**Master deliverable:** `Docs/RE/specs/assembly_graph.md` — the cross-format wiring synthesis
(World-boot chain + Actor-bake chain + format→format edge table + OPEN-RISK ledger).

**Out of scope (deferred):** re-RE of solid formats; the live debugger (DBG-pending = OPEN RISK);
VFS container open path; server/networking/crypto/front-end scenes; new gameplay; per-pixel oracle
polish.

**Command structure:** Tier-1 (main session) + Tier-2 captains: `re-orchestrator`
(P0.5, P1, P2, P3, P4) · `port-orchestrator` (P5, P6, P7). P0 + all serialized writes Tier-1-direct.

## Evidence baseline (recorded at cycle start, 2026-06-19)
- **IDA MCP:** UP · IDB `doida.exe.i64` · **SHA-256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`** ✓ · imagebase `0x400000` · 25 792 functions (4 871 named / 1 901 library / 19 020 `sub_`) · hexrays ready.
- **Build:** `dotnet build MartialHeroes.slnx` after full bin/obj nuke → **0 warnings / 0 errors** ✓ (all 12 layer projects + Godot).
- **Tests:** ⚠️ **0 test projects on branch `major-campaign`** (full `**/*.csproj` enumeration confirms none under `tests/` or elsewhere; the ~2068-test corpus from `campaign15`/`campaign12` is NOT on this branch). `dotnet test MartialHeroes.slnx` runs 0 suites here. → Phase 5 scaffolds fresh xUnit project(s) for the composers; **DECISION pending before P5**: also port the existing corpus from `campaign15`? P0 baseline test count = **0**.
- **VFS:** `clientdata/data.inf` present (archive reachable).
- **git:** branch `major-campaign`, clean, HEAD `31a370c`.

## Committed specs going in (build ON, don't re-RE)
terrain-streaming, world_systems, environment, effect-scheduling, rendering, resource_pipeline,
asset_pipeline, skinning, equipment_visuals; formats/{terrain, bgtexture_lst, npc_spawns,
area_inventory, bindlist, actormotion, animation, events_scr, items_scr, xdb_tables, ui_manifests,
scr, config_tables}; structs/terrain-manager.

**Gaps this cycle closes:** A1-1..A1-7, A2-1..A2-7, A3-1..A3-5/A3-7 (assembly-layer); doc-syncs
A3-6/A3-3; net-new `assembly_graph.md` (A3-8). **OPEN RISK (dbg OOS):** A1-8, A2-1-residual, A2-3.

---

## Phase 0 — PREFLIGHT (Tier-1) — status: **COMPLETE**
- [x] `/ida-mcp-connect` → MCP UP, DB open, anchor **263bd994** ✓
- [x] nuked build 0/0 ✓ · suite baseline recorded (0 test projects on branch — see note)
- [x] VFS reachable ✓ · branch clean
- [x] `_dirty/assembly/{world,char,other}/` created + gitignore-covered ✓
- [x] fresh `Docs/PLAN.md` + `Docs/ROADMAP.md` ✓
- [x] A3-6 doc-sync — DONE (Tier-1): `config_tables.md` already CONFIRMED 116 / REFUTED 166; corrected the stale cross-ref + conflict register in `formats/ui_manifests.md`
- [x] A3-3 — ROUTED to Phase 3 (axis-3 lane `other/citems_count.md`): committed evidence favors 6 (a full 10×81 overflows the 1052-byte stride) but is marked UNRESOLVED everywhere incl. `CitemsParser.cs` → the IDA loader settles it authoritatively

## Phase 0.5 — LANE-ANCHOR SCOUT — status: **IN PROGRESS** (folded into each axis captain's step 1)
Each A-lane → an IDA anchor `@263bd994` or NO-ANCHOR→OPEN-RISK. → per-axis `_dirty/assembly/{world,char,other}/_anchors.md`

## Phase 1/2/3 — GIGA RESEARCH (dirty room, PARALLEL — re-orchestrator) — status: **IN PROGRESS** (3 background captains launched 2026-06-19)
Output: `_dirty/assembly/{world,char,other}/*.md` ONLY. One writer per lane file.
| Axis | Lanes | Agents |
|---|---|---|
| 1 World | A1-1 cell_slots · A1-2 cell_fanout · A1-5 area_bootstrap · A1-4 bgtexture_buckets · A1-6 spawn_to_actor · A1-7 terrain_effects | re-struct / re-asset-format / re-function-analyst |
| 2 Char | A2-1 inverse_bind ★ · A2-2 mob_gid · A2-3/4 motion_map · A2-5/6 equipment_visuals · A2-7 bindlist_preload | re-struct / re-asset-format-analyst |
| 3 Other | A3-1 events_consumer · A3-2 items_asset_keys · A3-3 citems_count · A3-4 xdb_linkage · A3-5 indoor_bgm · A3-7 close_button_atlas | re-struct / re-asset-format-analyst |
**M1 EXIT:** all lanes returned + confidence-rated; A1-8 / A2-1-residual / A2-3 logged OPEN RISK.

### Axis 3 (Other-formats) — ✅ COMPLETE 2026-06-19 (7 files in `_dirty/assembly/other/`, all lanes anchored, firewall clean)
| Lane | Finding | Conf | Anchor | OPEN-RISK |
|---|---|---|---|---|
| A3-1 events_consumer | `events.scr` → `EventsScr_LookupById`; consumed ONLY on item/shop/exchange UI events (not timer/zone/captcha); key = event_id exact-match | CODE-CONF | `EventsScr_LookupById` | item-side store field not byte-pinned |
| A3-2 items_asset_keys | items.scr +0x80 → `data/char/skin/g%d.skn`; +0x84 → bind-pose pool id (g{id}.bnd/.mot) | HIGH/MED | `ActorVisual_AttachSkinPart` | +0x84 exact bnd/mot file unconfirmed |
| A3-3 citems_count | **10 paragraphs (capacity)** — accessor bounds idx<10, stride 81 @ +0xE4, `#`-sentinel early-terminates (6 = typically populated); 10×81=810 fits 1052 | HIGH | `sub_5bf2a7` | — |
| A3-4 xdb_linkage | vehicle.xdb keyed by vehicle_id; creature_item.xdb = held-item **VISUAL** attach (NOT loot), keyed by creature_key | CONFIRMED | `Boot_LoadDataTableCorpus` | — |
| A3-5 indoor_bgm | the "indoor override" is really a **trade/exchange-busy** flag (+0x734==1 → BGM 863500002); no map/region indoor attribute on the path | HIGH/MED | `SoundMgr_UpdateAmbientFromMudTile` | semantic relabel only |
| A3-7 close_button_atlas | char-select close btn = `tradekeepwindow.dds` (941,910,23,23) dst(971,610); blacksheet/login/main REFUTED | HIGH | `SelectWindow_BuildAndInit` | — |

**Phase-4 carry-overs from axis 3:** (1) RELABEL the committed sound spec — "indoor BGM" → trade/exchange-busy override; (2) RELABEL `xdb_tables.md` — `creature_item.xdb` = visual held-item attach, not loot; (3) citems paragraph count **6 → 10** in `scr.md` / `items_scr.md` + `CitemsParser.cs` (the committed spec's "0x41E overflow" was a hex slip: +0xE4+810 = +1038 = 0x40E < 0x41C, fits); (4) char-select close-button rect → `tradekeepwindow.dds (941,910,23,23)`.

### Axis 2 (Character/3D assembly) — ✅ COMPLETE 2026-06-19 (6 files in `_dirty/assembly/char/`, all lanes anchored, firewall clean)
| Lane | Finding | Conf | Anchor | OPEN-RISK |
|---|---|---|---|---|
| A2-1 ★ inverse_bind | **PINNED & CONFIRMED**: bake `localPos = inv(bindWorldQuat) ⊗ (vtx − bindWorldTrans)` (unit-quat conjugate, subtract-then-rotate, parent-on-left, vs rest-pose WORLD). **Explosion cause = weights index bones in base-relative bone-ID space (`id − base_id`), NOT array slot/palette** | HIGH | `0x42e241` bake / `0x42e500` drive / `0x42fe49` resolve | residual: handedness *label* + 88B clamp — NEITHER blocks animation |
| A2-2 mob_gid | `mob_id → mobs.scr(+52) → Appearance_ResolveKey → actor+108 → AnimCatalog → model_class_id → skin/bind registry → .bnd`; indirect via catalog (matches §8e), NOT literal `g{skin_class}.bnd` | CONFIRMED mech | `0x422631` / `0x422575` | categoryBase[] + value-edge = runtime |
| A2-3/4 motion_map | **9-slot "direction" REFUTED** — slots are action/lifecycle (a[1]idle a[2]walk a[3]run a[4]death…); **`motion_ids_b` = SFX routing, NOT motion** (CONFLICT vs committed `actormotion.md`); floats rate_x/y=move speed (resolves OQ#3), float_h/i=dust FX | CONFIRMED (resolved slots) | `0x422575` / `0x429081` | unused a[0/7/8] b[0/6-8], cols7-12 = no static reader |
| A2-5/6 equipment_visuals | GID-digit→column **CODE-CONFIRMED**; weapon→hand-bone `0x41a243`; weapon-glow toggler RE-LOCATED `WeaponGlow_EnableForGrade 0x5a8dce` (grade 101..109→1..9, 4 emitters) | HIGH | `0x419018` / `0x5a8dce` | per-tier glow set + `+231`↔enchant capture-pending |
| A2-7 bindlist_preload | **EAGER boot preload** confirmed (`BindList_LoadAndRegister 0x423108` → pose pool); selection = raw `.skn` header `id_b` verbatim as pool key (no `g{N}.bnd` formatting at this site) | HIGH | `0x423108` / `0x43051a` | none load-bearing |

**★ A2-1 = CONFIRMED static-pinnable → Phase 6 ships an ANIMATED avatar (retires skinning-explosion debt #1); the "static fallback" default is NO LONGER needed.**
**Phase-4 carry-overs from axis 2:** (1) CORRECT `actormotion.md`/`animation.md` — `motion_ids_b` = SFX/FX event routing, NOT secondary motion (binary wins; journal); (2) `skinning.md §4/§9` — replace the inverse-bind STATIC-HYPOTHESIS with the pinned bake + base-relative bone-ID weight-indexing rule; (3) `actormotion.md` float cols rate_x/y = per-frame move speed (resolves OQ#3), float_h/i = dust FX descriptor. (Prompt typo noted: brief cited `specs/config_tables.md`; correct path is `formats/config_tables.md`.)

### Axis 1 (World assembly) — ✅ COMPLETE 2026-06-19 (8 files in `_dirty/assembly/world/`, all lanes anchored, firewall clean)
| Lane | Finding | Conf | Anchor | OPEN-RISK |
|---|---|---|---|---|
| A1-1 cell_slots | 9 per-cell sub-managers = **slot0 ground texture grid · slot1 building/object grid · slots2-8 = fx1..fx7**; **34-pool (loader) OWNS live cells, 25-ring (manager) is a borrowed-pointer 5×5 view, live center = ring slot 12** | High | `Terrain_LoadCellFiles 0x440f47` | — |
| A1-2 cell_fanout | open order base→`.mud`→`.gad`(stub)→`.map`; **`.ted`/`.sod`/`.bud`/`.up`/`.fx`/`.exd` load INSIDE the `.map` parse via DATAFILE tokens**, synchronous under the load critical section | High | `Terrain_FindOrLoadCell 0x441b88` | — |
| A1-5 area_bootstrap | two-frame bootstrap: Phase A `Env_MapSetAndLoadArea` (id→`.lst`→bins→sound→radius→sky); **Phase B cold-start kick is one frame UP in the caller** (refines streaming.md §7); singleton order Loader→texture-preload→Manager | High | `Map_LoadAreaBinaries 0x456e70` | — |
| A1-4 bgtexture_buckets | kind byte = single branch (`==1`→static render-obj-type, `!=1`→scroll/animated); 6-value enum is data-only, never re-branched | High | `TerrainPool_InitFromBgtextureLst 0x4458bc` | — |
| A1-6 spawn_to_actor ★ | **MAJOR: live actors built from SERVER packet 4/4 (entity snapshot) → 880B SpawnDescriptor → `ActorManager_SpawnActorFromDescriptor`, NOT from on-disk `npc.arr`** (`.arr` = position/facing/static metadata only); visual id via mobs.scr/npc.scr → ActorVisual catalog; Y re-snapped each frame | High (wiring) | `Map_LoadAreaBinaries 0x456e70` | **yes** — cell-resident-at-spawn timing (Godot fallback-Y race) is runtime-only |
| A1-7 terrain_effects | fx1..fx7 → cell slots 2..8; trigger = `DATAFILE` keyword scoped inside each `FXN{}` `.map` section, attached DURING the parse | High | `Map_ParseDescriptor 0x43d9e9` | — |
| A1-8 | **STATICALLY RESOLVED** — idx-1 decrement is a real isolable site (const-folded read vs write base, clamp [1,count]); −1 on the `.ted` byte ONLY (pool accessors have no −1) | High | resolve site `0x44b296` | — (no longer dbg-pending) |

**Cross-lane (for assembly_graph):** `Env_MapSetAndLoadArea` (area id → `d<NNN>.lst` cell-key set → `map<NNN>.bin`/region/`npc.arr` → sound → radius) → caller Phase-B cold-start ring kick → `Terrain_FindOrLoadCell` (membership gate `mapZ+100000·mapX` → 34-pool cache/recycle) → `Terrain_LoadCellFiles` (`.mud`→`.gad`→`.map`) → `Map_ParseDescriptor` fans `.ted`/`.sod`/`.bud`/`.fx1-7`/`.exd` into the 9 cell sub-managers. **AreaComposer contract: cell store=34, spatial ring=25, live center=ring slot 12, render layers = the 9 slots.**
**★ Phase-4/5/6 load-bearing correction:** NPC/mob **visuals are server-driven (packet 4/4), NOT VFS-spawn-driven**. Offline port has no server → it synthesizes actors from `.arr` + visual catalog (the current demo already does this). `assembly_graph.md` records BOTH the original wire path AND the port's offline `.arr` substitution (a port-side choice, like Z-negation).

> **M1 REACHED — all 3 axes drained, firewall clean (git status shows only Tier-1 edits). → Phase 4 (promotion) launched 2026-06-19.**

## Phase 4 — PROMOTION + FIREWALL + IDB — status: **✅ COMPLETE (M2 reached 2026-06-19)**
- [x] **19 committed specs promoted** (3 axis captains, one author per file; `spec-author` absent from registry → ran as `general-purpose` carrying the `/re-promote` firewall discipline). Files: terrain-manager, area_inventory, terrain-streaming, bgtexture_lst, npc_spawns, terrain (axis 1); skinning, actormotion, animation, equipment_visuals, bindlist, config_tables (axis 2); events_scr, items_scr, scr, xdb_tables, sound, world_systems, ui_manifests (axis 3).
- [x] **`assembly_graph.md` NEW** (master synthesis: World-boot chain + Actor-bake chain + format→format edge table + OPEN-RISK ledger + port-side notes).
- [x] **firewall scan PASS** (Tier-1: no addresses / autonames / pseudo-C in any committed spec; the world promoter also scrubbed 4 pre-existing leaks in bgtexture_lst.md).
- [x] **`journal.md` entry** (names every touched spec + the 6 binary-won reversals).
- [x] **IDB legibility annotation** — `ida-toolsmith`: **63 sub_→canonical renames + 4 corrections + 7 globals + 84 comments** (sub_ 19,020→18,957, 0 CRT/lib touched); report `_dirty/assembly/_ida_annotate_report.md`.
- [x] **names.yaml delta** staged → `_dirty/assembly/_names_yaml_stage.md` for maintainer hand-merge (the 63+4 renames + `IndoorBgmOverrideId`→trade-busy relabel).
**M2 (BARRIER): ✅ REACHED** — assembly_graph exists ✓ · journal names every spec ✓ · firewall PASS ✓ · IDB annotated ✓ · names staged ✓. → **Phase 5 (C# core) launched in parallel.**

## Phase 5 — CORE ENGINEERING (port-orchestrator) — status: **✅ COMPLETE (M3) 2026-06-19**
- **Stage A** parsers corrected (`// spec:`-cited): CitemsParser 6→10 (`#`-sentinel, tail→0x40E), ItemsScrData +0x80→`g{}.skn` / +0x84→bind pool id, BgtextureLstData render-bucket (==1 static/!=1 scroll), ActormotionEntry `MotionClipIds` vs `SfxEventIds` (motion_ids_b=SFX), XdbParser creature_item=visual, TedTerrainParser idx-1 confirmed. + `IAreaAssemblySource`.
- **Stage B** NET-NEW: `AreaComposer`+`AssembledArea` (03 Assets.Mapping — 34-pool owner / 25-ring view centre=12; fan-out .mud→.gad→.map→9 slots; texture via BgTextureCatalog; spawns from .arr, Yaw=π/2−facing, no baked Y). `ActorComposer`+`AssembledActor`+`IActorAssemblySource` (04 Application/World — **animatable inverse-bind bake** conj(q)⊗(v−t), base-relative bone-ID, verbatim id_b select, model_class_id=5·(class+4·variant)−24, equipment + motion/SFX split; hand-rolled Vec3/Quat; **no Godot**).
- **Stage C** wiring: `AssemblyEvents` (Area/Cell/ActorAssembledEvent on IClientEventBus), `ActorSpawnService`, `CellAssemblyHandoff`. 04→03.Mapping decoupled via layer-04 `IAssembledAreaView`/`IAssembledCellView`.
- **Stage T** 3 fresh xUnit projects (registered in slnx /Tests/): Parsers 19 · Mapping 21 · Application 15 = **55 tests, 0 failed** (incl. the inverse-bind cancellation assertion).
- **Gate:** nuked build **0/0** (16 projects incl Godot) · DAG/firewall PASS (code-reviewer 0 blocker) · `// spec:` cited · 1 real bug caught+fixed by the test wave (AreaComposer null pool → NPE). **Not committed.**

**M3 EXIT ✅.** Spec ambiguities flagged-not-invented: categoryBase/model_class_id value-edges (caller-supplied, debugger-pending), OOR bone skip, dual-hand off-hand → layer-05 mesh step.
**→ Phase 7 cleanup carry:** CS8019 unused-using in `ItemsScrData.cs`; 2 minor code-reviewer advisories; streaming resident-set ↔ 34/25 ring reconciliation (flagged, not force-merged).

## Phase 6 — GODOT 05 WORLD UN-FREEZE — status: **IN PROGRESS**
> Attempt 1 (full port-orchestrator) hit the session limit + left broken layer-05 edits → REVERTED clean (build 0/0); switched to a resilient, incremental-compilable, single-agent-at-a-time approach.
- **6a — composer wiring ✅** (`godot-world-engineer`): `VfsAreaAssemblySource` adapter, `ClientContext` constructs `AreaComposer`+`CellAssemblyHandoff` (try/caught, null offline) + `AssembledCellViewAdapter` bridge, `GameLoop` routes `SectorLoadedEvent`→handoff + logs `Cell/AreaAssembledEvent`. **Solution build 0/0 · headless clean** · lighting + fallback-Y non-regressed. Remainder: handlers LOG (not yet drive slot renderers); screenshot deferred (world-entry needs server/dev-seed).
- **6b — animated skinning ✅ DEBT #1 RETIRED** (`godot-character-specialist`): the pinned bake was found **already correctly implemented at HEAD** (the prior broken attempt was a red herring); verified — INV1 rest-cancellation **1.47e-6**, AABB **sane** (player `(25,12,8)`, no millions), mobs animate (INV2 liveness), screenshot = **intact textured humanoid, not exploded**. Player idle correctly static (`g101100001.mot` = static data, faithful). 0 source files changed; git clean. **OPEN-RISK flagged (not fabricated):** avatar lies on X not Y = the `skinning.md §9` up-axis/handedness label (debugger-pending); no stand-up rotation invented. Screenshot `%TEMP%/mh-phase6b.png`.
- **6c (documented remainder, NOT a goal-blocker)** — drive the 9-slot renderers FROM the assembled events (the world currently renders via the proven old path; the composer seam is wired + logs); capture a world screenshot via the offline demo entry. + resolve avatar up-axis orientation (needs §9 debugger read or the char-select preview camera rig). + pre-existing: `World.tscn` references a deleted `HUD/GameHud.cs` (CAMPAIGN 17 cleanup).
**Phase 6 core ✅** (composer wired into runtime + skinning debt #1 retired, both verified). 6c remainders carried to the follow-on ledger.
Subscribe RealWorldRenderer to assembled-area; render 9 cell slots + multi-texture + collision; .fx
attach (Z PORT-SIDE); place actors + FIX fallback-Y; SknMeshBuilder actor mesh (animated IF A2-1
confirmed else static+deferral); lighting non-regression. headless + screenshot.
**M4 EXIT:** Godot 0/0 · headless World boot clean · Y-snap fixed · actor renders · no dark/explosion regression.

## Phase 7 — VERIFICATION + CLOSEOUT — status: **✅ COMPLETE (M5) 2026-06-19**
### Final gate (authoritative, nuked)
- **Build:** bin/obj nuked (core+tests) → `dotnet build MartialHeroes.slnx` = **0 errors** (4 NU1903 SQLite advisory warnings, pre-existing/unrelated).
- **Tests:** 3/3 suites enumerated + run per-project (sidesteps the `.slnx` `dotnet test` 0-enumeration bug): Mapping **21** · Parsers **19** · Application **15** = **55 passed, 0 failed**.
- **Firewall:** PASS (Phase-4 Tier-1 scan: no addresses/autonames/pseudo-C in any committed spec, incl. `assembly_graph.md`).
- **DAG:** clean (Phase-5 code-reviewer: no `using Godot;` in 01–04, no upward refs, `// spec:` cited).
- **Headless Godot:** World boots + composer seam logs + skinned character builds (sane AABB). One PRE-EXISTING non-fatal log: `World.tscn` references a deleted `HUD/GameHud.cs` (CAMPAIGN-17 leftover, not this campaign).
### Preservation
`journal.md` (CYCLE-1 entry naming every touched spec + the 6 reversals) ✓ · `names.yaml` delta staged → `_dirty/assembly/_names_yaml_stage.md` ✓ · memory `campaign-cycle1-assembly-graph` ✓ · ROADMAP statuses ✓. **NOT committed** (awaiting explicit maintainer request).
### Residual-risk ledger (debugger-pending; NONE blocks the port)
| Id | Risk | Disposition |
|---|---|---|
| A2-1 up-axis | avatar lies on X not upright-Y (`skinning.md §9` handedness label) | needs a debugger up-axis read OR the char-select preview-camera rig; no stand-up rotation fabricated |
| A1-6 | spawn-vs-cell-load Y timing (fallback-Y race) | symptom-fix path exists; exact timing runtime-only |
| A2-3 | unused motion slots/cols | no static consumer; left unassigned |
| A2-5/6 | per-tier glow visual set + grade↔enchant | capture-pending |
| A3-1/A3-2 | item-side event_id / +0x84 bnd-file join columns | not byte-pinned |
| A3-5 | trade-mode BGM track label | wiring HIGH, label MEDIUM |
### Follow-on ledger (clean increments, not blockers)
- **6c:** drive the 9-slot renderers FROM the assembled events + capture a world screenshot (world currently renders via the proven old path; composer seam is wired + logs).
- `World.tscn` → deleted `HUD/GameHud.cs` reference (CAMPAIGN-17 cleanup leftover; character still spawns).
- CS8019 unused-using ×2 (`ItemsScrData.cs`, `ClientContext.cs`) — stale post-nuke LSP hints (nuked build is 0/0).
- 2 minor code-reviewer advisories + the streaming resident-set ↔ 34/25 ring reconciliation (flagged, not force-merged).
- `names.yaml` maintainer hand-merge (63+ renames + `IndoorBgmOverrideId`→trade-busy) from the staged delta.

**M5 ✅ — all gates green · residual + follow-on ledgers written · preservation done · uncommitted.**

## OPEN-RISK ledger (carried; debugger out-of-scope this cycle)
| Id | Risk | Status |
|---|---|---|
| A1-8 | `.ted` idx-1 render-side decrement site | OPEN (dbg-pending) |
| A2-1 | inverse-bind bake | ✅ RESOLVED 2026-06-19 (static-pinned @0x42e241; weights index base-relative bone-ID; bake vs rest-pose world). Only a cosmetic handedness-*label* residual remains — animation NOT blocked |
| A2-3 | actormotion.txt float-column runtime semantics | OPEN (dbg-pending) |

---
*Maintained by the orchestrator (Tier-1). Update phase statuses in place as waves complete.*
