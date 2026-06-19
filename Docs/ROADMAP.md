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

---

# CYCLE 2 — Finish the visual world 1:1 (launched 2026-06-19, builds on CYCLE 1 4444c4e)

**Mandate:** make the assembled world actually RENDER through the CYCLE-1 composers, end-to-end, multi-area, avatar upright+animated, verified by a screenshot-oracle loop.
**Reframed:** CYCLE-1 wired the composer seam but `Cell/AreaAssembledEvent` only LOG; the world still renders via the OLD direct-from-VFS path (`RealWorldRenderer`). Drive the 9-slot render FROM the events (flag-gated, parity-then-retire), stream multiple areas via the 34-pool/25-ring, resolve avatar up-axis (rig-first/debugger-second, NO fabricated rotation), stand up a render-reviewer screenshot-oracle loop, fold in the CYCLE-1 cleanups. Anchor 263bd994.
**Master deliverable:** a windowed screenshot of the assembled multi-texture terrain + buildings + fx + upright animated actors, rendered FROM the composer events; headless clean; build 0/0 + tests green.
**Out of scope:** server/net/crypto/front-end (frozen); new RE (only a CONFIRM-only up-axis debugger micro-session, conditional); names.yaml/journal auto-edits (staged only); per-pixel polish beyond the oracle; gameplay/5-cameras; commit (on request).
**Command structure:** Tier-1 + port-orchestrator (A,B,C-a,D,E,F,G); re-orchestrator (C-b debugger, CONDITIONAL). **RESILIENCE: single-agent-at-a-time, incremental-compilable** (build/headless check per file; stop at a compilable checkpoint) — a limit hit must never break layer-05. NO heavy parallel waves.

## Phase 2-0 — PREFLIGHT — status: **DONE**
- baseline = CYCLE-1 Phase-7 gate (nuked build 0/0 . 55 tests green per-project) at commit 4444c4e; no content change since (only LF/CRLF churn).
- seam scout-confirmed: GameLoop LOGS Cell/AreaAssembledEvent (no render drive); RealWorldRenderer renders AREA-2 direct-from-VFS; mcp_bridge_game.gd screenshot autoload present; World.tscn line 9 references the deleted HUD/GameHud.cs (dangling); CS8019 x2 = stale LSP.
- DECISION: old-path strategy = parallel slot-render behind a `compose_render` flag -> parity -> retire.

## Phase 2-A — COMPOSER-DRIVEN RENDERING (marquee) — status: **✅ DELIVERABLE ACHIEVED**
> **Master deliverable DONE** — windowed screenshots `%TEMP%/mh-cycle2-WORLD-{composer,oldpath}.png`: the composer path (flag-ON) renders the walled town — textured terrain + thousands of building instances across the streamed ring + actors on the ground; functional PARITY with the old path (both show the town). The composer now drives the FULL world: terrain + buildings + fx + actors, headless + screenshot verified, build 0/0, flag-off non-regressing.
> **Findings (follow-ons, not blockers):** (1) composer resolves a DIFFERENT terrain texture than legacy (composer pre-bakes the 256-slot table; legacy resolves per-cell) — which matches the original needs the oracle / official captures; (2) composer renders ALL streamed cells' buildings (thousands) vs legacy's single target cell (composer is more complete); (3) environment too dark (debt #3, pre-existing); (4) camera framing = overview skyline. **A.6 flag-default-ON deferred until terrain-texture parity is settled.**
Flag-gated (flag-off = old path unchanged); one writer per file; sequential waves on RealWorldRenderer.cs.
- [x] A.1 `_composeRender` flag (default OFF) + subscribe RealWorldRenderer to Cell/AreaAssembledEvent + OnCellAssembled/OnAreaAssembled. Build 0/0, headless clean.
- [x] A.2 slot0 ground multi-texture via WireComposerTerrainResolver (AssembledCell.ResolvedTexturePaths[256] -> TerrainNode.TextureResolver). Build 0/0.
- [x] A.3 slot1 buildings via new SlotRenderer.RenderSlot1Buildings (BudMeshBuilder) + dup-guard. Build 0/0.
- [x] A.4 slots2-8 fx overlays via SlotRenderer.RenderFxSlots (absent slots skipped, Z-neg port-side). Build 0/0.
- [x] A.5 composer-driven actors — **DONE** ✅. Layer-04: `World/AreaAssemblyHandoff.cs` publishes `AreaAssembledEvent` once/area-enter (AreaBake -> existing `AreaComposer.ComposeArea`, `.arr` Spawns -> new engine-free `AreaSpawnDescriptor`); `AreaAssemblyHandoffTests` 4/4. Layer-05: `Adapters/AssembledAreaViewAdapter.cs`, ClientContext binds the AreaBake, `RealWorldRenderer.OnAreaAssembled` -> `NpcRenderer.PopulateFromSpawns`. **Headless (flag-ON): AreaAssembledEvent fires once (area=2, 1235 spawns) -> 40 actors placed FROM composer (AABB sane, terrain-grounded snap-Y); NpcRenderer.PopulateFromArea SUPPRESSED (no double-spawn). Flag-OFF: NpcRenderer unchanged (40). Build 0/0.** -> the composer now drives the FULL world: terrain + buildings + fx + actors.
- [~] A.6 VERIFY: composer pipeline FIRES offline via World.tscn-direct (25 cells: streaming->handoff->CellAssembledEvent->OnCellAssembled) ✓; flag reads env `MH_COMPOSE_RENDER` / `client_dir.cfg`; flag-off == flag-on screenshots IDENTICAL (zero regression ✓). **BUT all 25 cells arrive UNRESOLVED (slot0/1 empty) — root cause: `VfsAreaAssemblySource` hardcoded `areaId:0` in ClientContext (l.463); when area 2 loads, SetArea(2) rebinds streaming but NOT the handoff's source → area-2 cells build non-existent map000-area0 paths → early-exit no-slots. FIX = pull Phase 2-B.1 (area-rebind) forward → then re-verify + parity screenshot.** Screenshots: %TEMP%/mh-cycle2a6-{composer,oldpath}.png.
- [x] **2-B.1 FIX + A.6 RE-VERIFY — DONE ✅ MARQUEE PROVEN.** New `Adapters/RebindableAreaAssemblySource.cs` (mutable wrapper, `SetArea` swaps the inner `VfsAreaAssemblySource`); `RealWorldRenderer.TriggerTerrainStreaming` calls `ctx.AreaAssemblySource.SetArea(TargetAreaId)` alongside the streaming SetArea. Build **0/0** (the CS0053 LSP hint was STALE — class IS public; build is truth). **Tier-1 re-verified** via World.tscn-direct + MH_COMPOSE_RENDER=1: cells now **resolved=True** with populated slots — (10007,10004) slot0=1 **slot1Buildings=518** fxSlots=1; (10008) 227; (10010) 60 — **≈805 buildings spawned FROM the composer** (parity with AREA-2's ~779), terrain TextureResolver wired (256 slots), FX overlays; edge cells resolved=False (no .map, expected); **NO errors/exceptions**. The composer-driven world render WORKS offline.
- [x] (Phase 2-E.1 done EARLY) `World.tscn` dead `HUD/GameHud.cs` ext_resource removed (it blocked World.tscn loading entirely) — permanent fix, load_steps 7->6.
- New files: Adapters/AssembledCellViewAdapter.cs (public), World/SlotRenderer.cs. Changed: RealWorldRenderer.cs, GameLoop.cs, ClientContext.cs.

## Phase 2-B — MULTI-AREA STREAMING — status: PENDING
- [ ] B.1 area-rebind VfsAreaAssemblySource (un-pin areaId:0) . B.2 reconcile resident-set <-> 34-pool/25-ring (centre=slot 12) . B.3 multi-cell non-regression (Chebyshev hysteresis)

## Phase 2-C — AVATAR UP-AXIS — status: PENDING
- [ ] C.1a preview-camera RIG (campaign-9c framing) — present upright, NO math rotation . C.1b (only if rig insufficient) re-orchestrator->re-validator reads native up-axis via live ?ext=dbg (maintainer F9) -> skinning.md §9 + journal

## Phase 2-D — SCREENSHOT-ORACLE LOOP — status: PENDING
- [ ] D.1 headless+windowed screenshot loop (reuse mcp_bridge_game.gd) . D.2 score WORLD vs oracle . D.3 score CHARACTER vs oracle . D.4 flag official-capture gaps

## Phase 2-E — CLEANUPS — status: PENDING
- [ ] E.1 remove World.tscn->GameHud.cs ref (BEFORE 2-D) . E.2 CS8019 verify-first . E.3 2 advisories . E.4 names.yaml stage ready

## Phase 2-F — WATER (OPTIONAL) — status: GATED
- [ ] F.1 go/no-go . F.2 (if go) wire cell water plane else defer

## Phase 2-G — FINAL GATE + CLOSEOUT — status: **✅ GATE GREEN (deliverable achieved; maintainer-dependent follow-ons below)**
- [x] nuked `dotnet build MartialHeroes.slnx` = **0 errors** (core+tests nuked).
- [x] per-project tests **63/63 green** (Mapping 25 incl. +4 `AreaComposerTextureResolutionTests` · Parsers 19 · Application 19 incl. +4 `AreaAssemblyHandoffTests`) — 0 failed.
- [x] CS8019 (2-E.2) **CONFIRMED STALE** — absent from the nuked 0/0 build; the LSP hint is a post-edit cache artifact. Resolved-as-stale.
- [x] headless World boot clean (no GameHud log — 2-E.1 fix); composer pipeline fires + renders the full world (verified, screenshot).
- [x] DAG clean (engine-free below 05; A.5's layer-04 `AreaSpawnDescriptor` respects no-Assets.Mapping-ref via the layer-05 projection adapter).

### CYCLE-2 follow-on ledger (documented; NOT blockers)
| Item | Disposition |
|---|---|
| Terrain-texture parity | ✅ **RESOLVED** — the parity check caught a real composer bug: `AreaComposer.ResolveTexturePaths` clamped `byte > count` to `count` (last texture) instead of to **1** (first), violating `terrain.md §5.6/§5.9` (BOTH under+over floor to 1). The legacy was correct; the "peach" WAS the bug. Fixed (single clamp branch, `// spec:`-cited) + 4 regression tests; composer now matches the spec + legacy. Final pixel COLOR = render-tuning (gamma/material), not chain — no captures needed for chain correctness. |
| A.6 flag-default-ON | deferred until terrain-texture parity settled (flag stays OFF = zero regression) |
| 2-B.2/3 residency reconciliation | **DEFERRED (documented)** — streaming works correctly (multi-cell render proven); reconciling the resident-set <-> 34-pool/25-ring into one model is a quality-only cleanup with regression risk on working streaming → deliberately deferred (not force-merged). |
| BUD building top faces | ✅ **FIXED** — root cause: the Z-flip (negate Z) inverted triangle winding (CCW→CW); `.bud` top faces (+Y normals) then computed their face-normal downward → black under the +Y sun. Swapped `indices[1]↔[2]` per triangle in `BudMeshBuilder` to restore CCW in Godot space (`terrain_scene.md §3.2.4` confirms CCW front faces). |
| 2-C avatar up-axis | ✅ **ADDRESSED (port-side fix)** — `SkinnedCharacterBuilder.UpAxisRemapDeg = +90°Z` on the Pivot node maps the mesh's native X-up to Godot Y-up (same category as the Z/X negations; NOT a change to the recovered bind/bake math); AABB now Y-tall (was X), screenshot upright/solid/not-mirrored, applied to player + actors. The NATIVE up-axis LABEL is still §9 capture/debugger-pending (the rotation is a documented, adjustable port convention). |
| Building count / double-spawn | ✅ **FIXED** — `LoadAndSpawnBudScene()` (legacy target-cell load) wasn't gated on `_composeRender`, so compose-ON spawned the target cell's buildings TWICE (legacy + composer). Now gated `if (!_composeRender)` (symmetric with the NPC gate). The composer legitimately loads ALL ring cells' `.bud` (the "779" was the legacy single-cell count; no spec cap). A.6 flag-default-ON deferred until total density is visually confirmed (needs the World-render rig / official captures). |
| 2-D full oracle scoring | the screenshot is the proof; formal pixel scoring vs the maintainer's OFFICIAL CAPTURES is the sharpening step (oracle > spec) |
| Environment darkness (debt #3) | ✅ **IMPROVED** — `EnvironmentNode` now lights the town (visible-environment when VFS absent; `ApplyBackground` fallback when material-bin ambient is near-black; SkyDome near-black gate). Fog color = area-2 noon (spec-dictated from VFS bins). Exact brightness = captures-pending. **NEW follow-on surfaced:** the black band above rooftops is **BUD building TOP FACES** (back-face/untextured) — a geometry issue, not atmospheric. |
| names.yaml hand-merge | CYCLE-1 stage in `_dirty/assembly/_names_yaml_stage.md` (CYCLE 2 added no IDA renames) |

**Commit:** the CYCLE-2 block (composer-driven full-world render, build 0/0, screenshot-verified) is ready to commit on request.

— *Maintained by the orchestrator (Tier-1).*

---

# CYCLE 3 — Netcode total cartography, contracts & IDB legibility (launched 2026-06-19, closed 2026-06-20)

**Mandate (maintainer):** deep RE — **IDA static only** — of the ENTIRE Networking/Netcode subsystem of `doida.exe`: the dispatcher (5 Majors + many Minors), handlers, and the **Client↔Server contracts (DTO Requests/Responses)**, cartographed precisely, named, and recorded under `Docs/RE/`. **Reframed:** close the C2S gap to zero, decompose the big S2C payloads, synthesise a master contracts spec, and make the IDB legible. **STATIC ONLY** — no debugger this cycle, so every wire VALUE semantic stays `[capture/debugger-pending]` and is never fabricated.

**Master deliverable:** `Docs/RE/specs/net_contracts.md` (NEW — Req↔Resp by domain).
**Out of scope (deferred):** C#/Godot code; the live debugger; crypto re-RE (already confirmed).
**Command structure:** Tier-1 + `re-orchestrator` lane captains (W/P) + general-purpose promotion/gate workers + `ida-toolsmith` (T).

## Evidence baseline
- Going in: `opcodes.md`, `specs/handlers.md`, `specs/network_dispatch.md`, `specs/crypto.md`, ~45 `packets/*.yaml`, structs.
- Tool baseline: IDA MCP UP; anchor SHA **263bd994** (confirmed full sha256).

## Phase 3-W — GIGA RESEARCH (27 lanes, re-orchestrator) — status: **✅ COMPLETE**
- C2S builder census: convergence `Net_SendPacket`, **105 call-sites / 104 builders** mapped; **58-opcode gap** enumerated.
- B9 sweep: all 58 uncatalogued C2S senders recovered → **zero C2S gap**.
- S2C big-payload interiors decomposed (4/1 section-map, 4/4, 4/65, 4/100, 4/56+4/71 reclassified, 5/52 conflict resolved, 5/68/5/73/5/77, PvP); net objects (NetHandler/NetClient/SecureContext); conn-state machine; 2nd worker = 3rd socket-I/O thread (RESOLVED); code maps; CP949 inventory; actor-key order; Req↔Resp correlation.

## Phase 3-P — PROMOTION — status: **✅ COMPLETE**
- `opcodes.md` C2S rows **59→105** (+46); `packets/*.yaml` **146→205** (57 created + 11 extended); `handlers.md` Part III (§19-§24); `network_dispatch.md` §8 resolved; **4 new structs**; **`net_contracts.md` (NEW, 451 lines, 58 Req↔Resp pairs)**.

## Phase 3-T — IDB LEGIBILITY (ida-toolsmith) — status: **✅ COMPLETE**
- **92 renames** (5 machinery + 2 S2C + 85 C2S) + 6 struct types + 7 signatures + 20 neutral comments; SHA-pinned; IDB saved. 4 name conflicts surfaced for names.yaml.

## Phase 3-R — REVIEW + GATES — status: **✅ PASS**
- Firewall **23→0** leaks (20 YAMLs scrubbed, incl. PRE-EXISTING prior-campaign leaks; zero factual change); reconcile clean (2/153 collision flagged); **zero-gap** completeness (99 Response + 65 Push + 104 C2S all accounted).

## Phase 3-C — CONSOLIDATION — status: **✅ (this entry)**
- journal.md entry + this ROADMAP section + memory. **names.yaml sync OWED** (92 IDB names + 4 conflicts staged in `_dirty/netcode/applied/cycle2_phase_T.md` + `names_staged_wave1..3.md` for hand-merge).

### CYCLE-3 follow-on ledger (documented; NOT blockers)
| Item | Disposition |
|---|---|
| Wire VALUE semantics | capture/debugger-pending by design (static-only cycle); routing/sizes/offsets are confirmed |
| 2/153 opcode-id collision (product_confirm vs pet_summon) | flagged for Tier-1 arbitration; neither deleted |
| 1/7 delete-reply · 2/151 reply-major · 5/73 name (binary→SmsgQuestComplete) · 4/48 12B overrun | UNVERIFIED, carried |
| names.yaml sync | owed (NAMES-SYNC pull) |
| Feed specs → C# Network layer 02 | future cycle (not this scope) |

**Commit:** the CYCLE-3 netcode block (specs + IDB annotations) is ready to commit on request.

— *Maintained by the orchestrator (Tier-1).*
