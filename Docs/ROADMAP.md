# Docs/ROADMAP.md — Live Campaign Run Record

> The dated, in-place record of every campaign run. The **method** is `Docs/CAMPAIGN_TEMPLATE.md`;
> the **active charter** is `Docs/PLAN.md`. Update phase/block statuses **in place** as waves land.
> Prior cycles live in git history + `Docs/RE/journal.md`.

---

# CAMPAIGN 11 — C# Excellence & Fidelity (core layers 01–04 + Godot 05) (launched 2026-06-16)

**Mandate (maintainer):** "With all the IDA Pro comprehension (the source of truth) and the now
re-verified `Docs/` SPEC behind us, focus on the C# — both the core projects AND Godot. (1) Delete
useless elements that should not be there. (2) Improve, correct, optimise the code so it is the
cleanest and TRUEST possible vs IDA / the spec. (3) Deploy lots of agents — use all needed agents and
skills. (4) Improve the plan/roadmap/campaign as needed. Make the C# part the cleanest, most
excellent, most optimised and functional possible."

**North stars:** N1 = total clean-room RE of `doida.exe` (DONE through C10 — specs are the truth);
**N2 = the faithful 1:1 re-implementation is now the focus** — the C# (core + Godot) must match the
re-verified specs exactly, be clean-room-pure, zero-alloc on hot paths, idiomatic C#14/.NET10, and
carry no cruft.

**Method:** the `Workflow` tool drives every phase as a massively-parallel fan-out (Ultracode).
Audit → adversarially verify → fix (one writer per project) → hard gates. Clean-room firewall, the
downward-only DAG, zero-alloc discipline, and the build/test/headless gates all hold throughout.

**Out of scope:** the game server; re-RE of already-verified specs (read them, do not re-derive);
live debugger/capture (flagged-pending facts stay pending).

## Phase 0 — Baseline & charter — **DONE 2026-06-16**
- [x] Authoritative gate: build **0 err / 1 pre-existing warn** (`RealWorldRenderer.cs:1058` CS8600) ·
  **1859 tests green** (10 suites).
- [x] Baseline commit **`b236830`** (banks campaign3 front-end WIP + C10 Stage-C BootFlow fix + kit/doc
  updates) → clean tree for the C# pass. Local `client_dir.cfg` left unstaged.
- [x] CAMPAIGN 11 charter recorded (this section).

## Phase 1 — Audit (Workflow, read-only, massively parallel) — **DONE**
`campaign11-csharp-audit` (`wf_4551aa3c-544`): 20 lanes → **170 findings** (2 critical + 23 high + 39
medium + 106 low). By category: fidelity 52, delete 42, test-gap 27, cleanroom 14, optimize 13, arch 9,
bug 7, modernize 6. Triaged + grouped by owning project into per-project briefs.

## Phase 2 — Verify & prioritise — **DONE (folded into the fix lanes)**
Verification embedded in each fix lane (adversarial re-confront-to-spec before editing). Baked Tier-1
decisions: VFS keep-mmap (port choice) + fix the real leak; defer the cross-project arch DAG refactor to
3b; defer the 3/14-vs-4/1 spawn ordering (debugger-pending). 33 findings rightly skipped (false positives,
test-infra needing a new csproj, dev-tool paths, debugger-pending) — all logged.

## Phase 3 — Fix (Workflow, one writer per project) — **DONE**
`campaign11-csharp-fix-3a` (`wf_e8f23be9-ea4`, 15 lanes) — **114 fixes applied**, 33 skipped, 13
cross-project ripples flagged. `godot-world-fx` re-run (effect diffuse tint, velocity Z-negate, demo-noise
delete). Reconciliation `campaign11-reconcile` (`wf_81832c4b-235`, 3 lanes): LobbyServerRecord re-model
per lobby.yaml, ZoneType.Unknown→Safe, CharacterClass renumber consumers. **Committed `707ce31`.**
### Phase 3b — arch DAG (maintainer: accept + document) — **DONE**
Removed the spurious `Diagnostics→Kernel` ref; ACCEPTED the 3 by-design downward edges
(`Application→Protocol/Crypto`, `Infrastructure→Parsers/Vfs`, `Godot→Infrastructure`) and documented them
in CLAUDE.md + `check_dag.py` INTENDED. `check_dag.py` now PASSES (24 core projects, acyclic, downward-only).

## Phase 4 — Hard gates — **DONE (GREEN)**
Authoritative nuke + `--no-incremental` build **0 err / 1 pre-existing warn**; **1944 tests green**, 0
failed, 0 skipped (was 1859; +2 new suites Network.Abstractions/Shared.Diagnostics, +85 tests). DAG PASS.
Firewall: the two committed `_dirty/` citations removed. (Headless/screenshot Godot re-verify = follow-up.)

## Phase 5 — Consolidate & commit — **IN PROGRESS**
Phase-3a milestone committed `707ce31`; Phase-3b arch commit next. Remaining: `names.yaml` sync (deferred
C10 ctor collisions); journal + memory; the residual follow-ups (GameHud "Unknown" pill dead chrome,
lobby ServerSelect wire-adapter, the deeper deferred items). Reconciled the stale CLAUDE.md skeleton claim
is a C10 item (still pending). Headless Godot screenshot-verify of the front-end fidelity fixes = follow-up.

---

# CAMPAIGN 10 — Total Client Comprehension & Doc Re-Verification (`doida.exe`) (launched 2026-06-16)

**Mandate (maintainer):** "Deploy lots of agents to deep-analyze the whole `doida.exe` client —
how it is constructed and boots, what it does, the management of functions/modules/scopes, every
scene (= window), with ultra-precise attention to the UI/UX (GUI) window construction, plus a deep
refinement of the `data.vfs` pipeline. Don't trust the current docs — IDA Pro 9.3 (MCP) is the source
of truth. Re-verify and rewrite the entire `Docs/RE/` to 100% certainty; then align the code."

**Master deliverable:** `Docs/RE/specs/client_architecture.md` — the top-level synthesis (entry →
init scopes → `GameState` scene machine → window framework → VFS/resource pipeline → frame loop),
cross-linking every re-verified subject spec.

**Out of scope (deferred):** the game server; live debugger / capture confirmation; blanket-naming
all ~19k unnamed functions.

**Command structure:** Tier-1 (main session) drives phase sequencing + gates + the Tier-1 serialized
files. Tier-2 `re-cleanroom-orchestrator` (per-block dirty→spec RE) and `re-annotation-orchestrator`
(Phase-D IDB writes). Research/engineering fan-outs run via the `Workflow` tool over the §13 fleet.

## Evidence baseline (Phase 0)
- **IDB:** `doida.exe` SHA `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`;
  imagebase `0x400000`; image `0x64d000`; **25,792 functions** (4,801 named / 1,901 lib /
  19,090 unnamed); entry `start` @ `0x66959c`; 4,800 strings. `names.yaml` pinned to this build.
- **Tool baseline (2026-06-16):** IDA MCP **UP** · build **0 err / 1 pre-existing warn**
  (`RealWorldRenderer.cs:1058` CS8600, Godot, not from this campaign) · tests **1855 green / 0 fail /
  0 skip** (10 suites) · VFS reachable.
- **Corpus to re-verify:** 37 `specs/` + 32 `formats/` + 10 `structs/` + ~80 `packets/` + `opcodes.md`.

## The "100% sure" gate — verification banner (added to every touched doc)
`verification: confirmed | sample-verified | static-hypothesis | capture/debugger-pending` +
`ida_reverified` + `ida_anchor: 263bd994` + `evidence` + `conflicts`. Phase-R fails any touched doc
without it. (Schema in `Docs/PLAN.md`.)

---

## Phase 0 — Mandate & Pre-flight — **DONE 2026-06-16**
- [x] `Docs/PLAN.md` (charter) + `Docs/ROADMAP.md` (this record) created.
- [x] Baseline captured: build 0/1(pre-existing) · 1855 tests green · IDA MCP UP.
- [x] `_dirty/campaign10/{A..G}/` + `glossary.yaml` scaffolded.
- Pre-existing baseline note: `RealWorldRenderer.cs:1058` CS8600 warning (carry; fix opportunistically in E).

---

## Phase W — Giga-research (dirty room, blocks A→G) — **IN PROGRESS**
Massively-parallel static-IDA + VFS lanes. One analyst lane per doc → `_dirty/campaign10/<block>/<lane>.md`.
Ledger: one writer per `_dirty` path. Each lane re-confronts the current doc to the IDB: marks every
claim/constant/offset confirmed / sample-verified / static-hypothesis / capture-pending; raises
`CONFLICT:` on any disagreement; surfaces what the doc MISSED.

### Block A — Boot & Runtime Construction ★ — **W+P DONE 2026-06-16 (firewall PASS); D deferred(batched)**
| # | Lane (re-verify) | Type | Agent | Deliverable | Status |
|---|---|---|---|---|---|
| A0 | `WinMain` entry + scene machine + init tiers (the spine) | IDA-S | re-static-analyst | `_dirty/campaign10/A/winmain_state_machine.md` | ✓ |
| A1 | `specs/client_runtime.md` (boot + runtime engine behaviour) | IDA-S | re-static-analyst | `_dirty/campaign10/A/client_runtime.md` | ✓ |
| A2 | `specs/game_loop.md` (per-frame ordering + timing) | IDA-S | re-static-analyst | `_dirty/campaign10/A/game_loop.md` | ✓ |
| A3 | `specs/intro_sequence.md` (boot/logo/intro sequence) | IDA-S | re-static-analyst | `_dirty/campaign10/A/intro_sequence.md` | ✓ |
| A4 | `specs/client_workflow.md` (scene transitions end-to-end) | IDA-S | re-static-analyst | `_dirty/campaign10/A/client_workflow.md` | ✓ |
| A5 | `specs/resource_pipeline.md` (boot-load worker / loading screen, init side) | IDA-S | re-static-analyst | `_dirty/campaign10/A/resource_pipeline.md` | ✓ |
| A6 | `structs/runtime_singletons.md` (global singletons / service slots / scopes) | IDA-S | re-struct-cartographer | `_dirty/campaign10/A/runtime_singletons.md` | ✓ |

**A — reconciled findings (the docs were ~90% accurate but had real, load-bearing errors):**
- **Scene machine = exactly 8 cases, GameState 0..7** (`WinMain` switch `cmp eax,7; ja default; jmp jpt[eax*4]`) — the docs' "0..8" is WRONG; the value **8 is a sub-state** (`GameState+4`), not a 9th case. `[confirmed]` (Tier-1 disasm of `0x5fe34a`).
- **Frame loop is software-capped at a FIXED 60 FPS** (QPC limiter; engine ctor seeds the rate field = `60.0f`, never overwritten) — the docs say "uncapped." `DISPLAY_FRAMERATE` has **no consumer reaching the throttle** (inert, static). Loop has **4 phases not 3**. (A1+A2 concur.)
- **VFS:** `data.inf` 24-byte header, **entry_count at header +0xC (+12)** not +8; opened **RANDOM_ACCESS** not SEQUENTIAL_SCAN. (A0+A6 concur; byte-witness deferred to Block C.)
- **Login sub-state 31 = PIN/second-password modal, NOT EULA**; keepalive = **C2S 2/112** not 2/10000; login credential = tab-separated **KEY-string secure-context** (account/password/PIN/host port) not literal 2/1; **no 4/3 BillingInfo** (4/1 is two-form). (A4 — wire-level items capture-pending → Block E.)
- **Intro:** Opening = **GameState 3**; alpha inits **250** (fade-out first) applied via **D3D TEXTUREFACTOR RS60** (not per-vertex); skip button **top-right**; second mouse-scrub path.
- **Singletons:** MainWindow (1464 B, 223 slots) is a **separate object** from the 16-B MainHandler hub — the doc conflates them; VFS state = **4 globals + 3-word progress block** (not 3 flat); undocumented **AppService** singleton (136 B); MainWindow **+0xBC** secondary vtable.
- **Resource:** boot worker INSTALLED in LoadHandler ctor but STARTED in the loading-window sub-init (ABOVE_NORMAL); progress is an **integer quotient → near-static bar**, completion driven by the thread-flag; OPENNING/SKIP resolved (`GetPrivateProfileIntA`).
- ~80 name proposals captured → `_dirty/campaign10/glossary.yaml` (block A). Notable rename: `0x5fe063` (mis-named `Diamond_OpeningWindow_ctor`) is the **window-manager registration / cleanup-push helper**; the real Opening ctor is `0x54581a`.
- **P (promotion): DONE** — 6 Block-A specs (`client_runtime`, `game_loop`, `intro_sequence`, `client_workflow`, `resource_pipeline`, `structs/runtime_singletons`) rewritten to 100% + verification banners; **Tier-1 firewall scan PASS** (no decompiler artifacts / image-range addresses leaked). Phase-D IDB annotation for A's cluster deferred to the batched annotation wave.

### Block B — Scene/Window State Machine & UI Framework ★ — **W+P DONE 2026-06-16 (firewall PASS); D deferred(batched)**
Covered `specs/ui_system`, `ui_hud_layout`, `input_ui`, `frontend_scenes`, `login`;
`structs/guwindow`, `gucomponent`; `formats/ui_manifests` + 3 deep `construct()` element walks
(LoginWindow 73 widgets, SelectWindow 279 elements, MainMaster HUD 178 slots).

**B — reconciled findings (the UI docs had real, load-bearing errors):**
- **GUComponent geometry was TRANSPOSED** (SEVERE): correct is **+0x1C=width, +0x20=height, +0x24=posX, +0x28=posY** (+0x14/+0x18 local, +0x2C/+0x30 world, +0x44 64B transform matrix, +0x0C tint+forced-alpha@+0x0F, +0x84 parent). **No sized ctor exists** (only a default zero-init; geometry via setters). Auto-hide timer pinned (+0x95/+0x98/+0x9C=3000ms/+0xA0). — would have mis-laid-out every Godot widget.
- **UI event taxonomy was WRONG**: full = **1=key-down, 2=key-up, 3=move, 4=press, 5=release, 6=click(synth, same-widget = click-vs-drag), 7=dbl-click, 8=wheel** (doc had {3,5,7,8}, mislabeled 5 as press). **DirectInput8 is the KEYBOARD path** (doc inverted it). Wheel delta at record +4. Recovered all prior-UNVERIFIED constants (dbl-click 300ms/2px, modifier bits).
- **UI layout is CODE-BAKED** — NO on-disk layout manifest. Each window's `BuildScene` (vtable slot 14 / +56) builds children with integer-literal coords via `Build*(tex,dstX,dstY,w,h,srcX,srcY,color)` (1:1 src/dst). Registries (uitex.txt) map id→path only.
- **NO EULA panel** (B9 construct walk supersedes a B7 inference): the msg ids 4001–4022 are the **server-list/channel row captions**.
- **Cross-block CONFLICT resolved (Tier-1):** A3's "login=4" REFUTED → **Login=GameState 1, Opening=3, char-select=4**; the Opening is **post-login** (login→load→opening→char-select→in-game). `intro_sequence.md` corrected accordingly.
- **MainMaster HUD = 3 routines** (docs blurred into 1): ctor (vtables+zeroed 223 slots, builds nothing) / `BuildAndRegisterPanels` (178 slots) / per-GameState reconfig (text/sound, no rects). Inventory **W=318 not 732**. ~150 named panel ctors de-anonymized.
- **`0x5fe063`** finally pinned: **SceneDisposeList_Push** (std::list teardown push), NOT a ctor and NOT window-manager attach (the manager is MainMaster's ~223-slot table). game.ver gate = **single u32 index-5 equality**. login sub-states 29/30/31 recovered (31 = PIN modal show, 32 = PIN poll). password field cap 129 (validate at submit).
- ~35 curated name proposals → `_dirty/campaign10/glossary.yaml` (block B). Full element tables in `_dirty/campaign10/B/construct_{login,select,hud}.md`.
- **P (promotion): DONE** — 8 Block-B specs (`structs/gucomponent`, `structs/guwindow`, `ui_system`, `ui_hud_layout`, `input_ui`, `frontend_scenes`, `login`, `formats/ui_manifests`) rewritten to 100% + verification banners; **Tier-1 firewall scan PASS** (zero decompiler artifacts / image-range addresses). The geometry-transposition + event-taxonomy fixes are load-bearing for the Godot UI port (Phase E). Phase-D annotation deferred to the batched wave.

### Block C — VFS / Asset I/O & Resource Pipeline ★ — **W+P DONE 2026-06-16 (firewall PASS, sample-verified); D deferred(batched)**
Covered `formats/pak`, `specs/vfs_overview`, `asset_pipeline`, `structs/terrain-manager` (read side).
The two-witness gate (IDB loader + real `data.inf`/`data.vfs` sample) delivered **sample-verified** results.

**C — reconciled findings:**
- **VFS `data.inf`/`data.vfs` header DECODED** (the doc said "magic cannot be inferred"): **24-byte header = magic `"VFS001"` (8B null-padded) + u32(=39, role TBD) + u32 entry_count + u32 blob-size**. The same header is **echoed at `data.vfs` offset 0** (entry-0 payload at offset 24). **Sample-verified.**
- **Entry-count +0xC RESOLVED both ways** (IDB stack-offset proof + `24+144×43347 = 6,241,992` = exact `data.inf` size); the `+0x08` reading is **refuted**. Vindicates Block A.
- **TOC entry = 144 B**: name[100], pad_100 (≈0; ~14/43347 nonzero = build-tool residue), dataOffset i64@104, dataSize i64@112 (low dword only), **3× FILETIME** (ctime/atime/mtime)@120/128/136 — NOT padding.
- **Storage is RAW** (no compression/encryption/codec flag) via **ReadFile-into-buffer, NOT mmap** (the only `MapViewOfFile` user is an unrelated anti-tamper self-check). One global read lock. Named class `CVFSManager`. Mount = `game.lua vfsmode` toggle.
- **terrain-manager.md conflated TWO singletons** → split into **TerrainLoader** (streamer; worker thread **DORMANT** after init; FIFO; **34-slot** pool; cell-key RB-tree) and **TerrainManager** (9 grid sub-objects; **25-slot** ring; 2 GFrustum; stream-radius clamp ≥15000→1000; map-option/region @+464/+468, NOT spawn coords). Cell = 24,712 B; key = `mapZ+100000*mapX`.
- **Loading bar is near-static** (normalized = bytes / 9,395,240 **integer**; bar px = `223*v/100`; fills only ~939 MB-in) — completion driven by the worker done-flag, **not** the bar (cross-confirms A5). The "~9.4 MB → 100%" framing was wrong.
- ~30 curated name proposals → glossary (block C). Full notes in `_dirty/campaign10/C/`.
- **P (promotion): DONE** — 4 Block-C specs (`formats/pak`, `vfs_overview`, `asset_pipeline`, `structs/terrain-manager`) rewritten to 100% with **sample-verified** banners; **Tier-1 firewall scan PASS** (zero artifacts). The `pak.md` header decode (`VFS001` magic, FILETIME TOC, RAW storage) and the terrain two-singleton split are the load-bearing corrections. Phase-D annotation deferred to the batched wave.

### Block D — Asset Format Corpus (two-witness) ★ — **W+P DONE 2026-06-16 (firewall PASS, sample-verified); D-annot deferred(batched)**
Re-confirmed every `formats/*.md` against the IDA loader AND a real VFS sample. The corpus **RE-CONFIRMS
sample-verified** on build `263bd994` (vindicates the campaign-8 two-witness work) with mostly minor drifts.

**D — reconciled findings (corrections, not rewrites):**
- **terrain:** `.ted` = 46987 B (5 reads, no header); texidx stored **RAW** (re-confirms the render-domain fix); **EXTRA_TERRAIN → `.exd`** (not `.ted`); `.map` WIDTH/HEIGHT/GRID/ORIGIN present-on-disk but **not parser-consumed**; `.mud` confirmed.
- **mesh/anim:** `.skn` **normal-first** verified, LenStr **4-byte** prefix (binary-mode bit); bindlist **string-sorted**, 349 entries; BANI drifts (name_len 10, frame_count 24); `+0x40/+0x64` int[9] arrays naming reconciled across animation/actormotion.
- **texture/effects:** no header test (both loaders feed D3DX); **effectscale.xdb REPLACES** the .xeff base-scale at parse; `.xeff` **9-byte** track header; particleEmitter.eff variable-length.
- **env/region:** region cell byte = **INDEX (0..31)** not a 0/1 mask; zoneType `{0,1,2,9}`; per-map = **`map<NNN>.bin` (520B)** not `mapsetting<NNN>.bin`; 3 `.tol` **inside** the VFS; fog always 204B; ambient floor 1.0 (white); all env-bin sizes byte-exact.
- **sound:** stride **48** DEFINITIVE (52 refuted by both witnesses); ~301 tables; two loader entry points.
- **items/config:** `items.csv` **NOT runtime-loaded** (authoring-only, now CONFIRMED); items.scr discriminator on-disk **+0xD2**; citems `+0` = **item_id** (not slot_index); **`.do` stride 116** confirmed (166 refuted); citems desc paragraphs **6-vs-10 UNRESOLVED** (carried open).
- **scr:** strides resolved by arithmetic (helps=48, dashs=199/28, userlevel=60/300); events.scr 520B/1848, 4 consumed fields; 6 new record counts.
- **xdb/mi:** msg.xdb 516B/2644 confirmed; **`mobinfo.mi` IS in the VFS** (`data/ui/mobinfo.mi`, 592B = 4+21×28) — the doc's "absent" verdict REVERTED (no client loader still holds); buff-icon spacing **25** not 27.
- **npc:** `npc.arr` 28B/559 records; spawn_type enum **0..11** (not {0,7}); `mob.arr` 20B has **no client loader** (client uses `mobs.scr`); map207 240B anomaly.
- **indices:** area count **60** (not 63), 2503 cells; game.ver 28B/7-u32 (index-5 compare); bgtexture.lst 48B, kind byte → 2 pools.
- ~28 curated loader name proposals → glossary (block D). Full notes in `_dirty/campaign10/D/`.
- **P (promotion): DONE** — 14 Block-D lanes rewrote the formats corpus to 100% with **sample-verified** banners; **Tier-1 firewall scan PASS** (zero real artifacts; the only grep hits were the field name `sub_effect_count` and the neutral label `unk_dist`, both legitimate). **★ MILESTONE: all four priority blocks A/B/C/D are now W+P+firewall complete.**

### Block E — Network / Protocol / Crypto — **W+P DONE 2026-06-16 (firewall PASS); D-annot deferred(batched)**
Covered `specs/network_dispatch`, `crypto`, `handlers`; `opcodes.md`; the C2S+S2C `packets/*.yaml`;
entity + item/skill/npc structs. Static + VFS only — packet field **VALUE** semantics stay capture-pending
(no wire this campaign); routing/sizes/offsets are control-flow **confirmed**.

**E — reconciled findings:**
- **Frame header RESOLVED = 8 B `[u32 size @0][u16 major @4][u16 minor @6]`** — the size is **u32** (settles the long-standing u16-vs-u32 question).
- **Crypto open-question #1 RESOLVED (open since June):** the **inbound path applies NO inverse cipher** — it is LZ4-decompress-only; the byte cipher has **exactly one xref** (the outbound send gate), a positive single-caller proof. `crypto.md` was otherwise **already correct** (3-round keyless cipher; FLINT-bignum handshake 0/0→1/4, PKCS#1 v1.5, XOR-whitened, does not key the cipher).
- **Major-3 opcode ladder CORRECTED** (matches Block A): **3/4 = SceneEntityUpdate, 3/7 = CharManageResult (8B), 3/14 = CharSpawnResponse (16B)**, 3/100 = generic action-result (no case 32). The doc/YAMLs had these swapped (incl. the misnamed `3-4_char_manage_result.yaml`).
- **Keepalive = TWO mechanisms**: the armed `(2,10000)`@20s compressed frame AND the C2S **2/112** 1-byte toggle (g_KeepaliveEnabled). Both real; on-wire cadence capture-pending. `opcodes.md` gains a 2/112 row.
- **Dispatch shape:** majors 1/3 inline switches; 4/5 **table-driven** (two 154-slot tables, base+1246/+1400, inert no-op default, minor≥154 undispatched, 4/500+4/50000 outside); major-0 = hardwired (0,0) handshake branch. Response ~99 / Push 65 slots.
- **Packet sizes/offsets confirmed** at send/handler sites (1/6=52, 1/9=40, 2/13=16, **2/28=12 fixed** not "var", 2/52=24+arrays, 5/13=40, **5/52 header offsets fixed** ActionCode@0x10/TargetCount@0x14, 3/1=3B+5×981B, 3/5=44B, 4/29=36B, 4/102=476B). Text length-prefix: 2/7 EXCLUDES NUL, 3/21 INCLUDES it. ~60 additional major-2 C2S senders found (coverage gap noted).
- **Structs:** Actor/SpawnDescriptor confirmed (local-player global, spatial index +0x3EC, equip table 20×16@+0xCC); skills.scr 1504+N×8 / 1508B obj; mobs.scr 488B / +0xF8 HP+=10 / +0x144==11 boss.
- ~35 curated network/crypto/handler name proposals → glossary (block E). Full notes in `_dirty/campaign10/E/`.
- **P (promotion): DONE** — 8 Block-E lanes (network_dispatch, crypto, handlers, `opcodes.md`, C2S+S2C packet YAMLs, entity + item/skill/npc structs) rewritten to 100%; **Tier-1 firewall scan PASS** after fixing one real leak (a Hex-Rays `_QWORD` type name in `structs/npc.md` → neutral "64-bit value (`u64`)"). The crypto OQ#1 resolution + the 3/4·3/7·3/14 opcode ladder are the load-bearing corrections. (Pre-existing minor: `journal.md` carries a `__thiscall` provenance mention from a prior campaign — append-only, left intact; flag for the Phase-R clean-room-auditor.)

### Block F — Gameplay Systems — **W+P DONE 2026-06-16 (12/12, firewall PASS); D-annot deferred(batched)**
Covered combat, skills, inventory_trade, equipment_visuals, progression, quests, npc_interaction,
chat, social, minimap, camera_movement, lua_scripting/lua-config, world_systems. Static + VFS only —
client-side routing/sizes/offsets/formulas **confirmed**; server-authored magnitudes + wire VALUE
semantics stay capture-pending.

**F — reconciled findings (largely confirmed; offsets re-pinned from prior-build drift):**
- **Combat:** melee = **C2S 2/52 slot 0xFF** (slot byte = `(stance!=1)-56`); cadence = **100ms×skill_cadence** (rec +1332), **550ms** lockout; 4/100 = 188B. Default basic-attack skill **121100050**. CONFLICT: the attack-flag clear is **4/13** (LocalPlayerStateSync) on this build, not 4/2 (capture-pending which arms vs releases). Controller offsets re-pinned (+136/+140 → +36/+40).
- **Skills:** the hotbar is **ONE 240×8B record array** (id@+0, points@+4) — resolves the old "two parallel arrays / second-int unverified" open question; skills.scr 1504+N×8 / 1508B obj; 4/102 = 476B snapshot.
- **Inventory/trade:** **THREE** item arrays (bag `40*(bag_count+3)` / equip 20 / a new 120-entry @lp+1260). Two offset fixes: 4/23 reads selector@+8 + reason@+9 + phase@+10 (all live); 4/25 phase@+8 / count@+0x18.
- **Equipment→visual:** off-hand node **flag = 1** (flag-2 = main-hand); slot-15 rebuild byte @+0x0C (16B) vs +0x0B (20B); both GID formulas confirmed.
- **Progression:** **the stat-EDITOR row labels were swapped** (correct: +376=STR/+380=DEX/+384=INT/+388=AGI/+392=CON) — BUT the **2/29 WIRE body (STR,INT,AGI,DEX,CON) is CORRECT and untouched**; 5/32 also broadcasts chat 10081; rank cap-25 per-level table.
- **Others (quests/npc/chat/minimap/camera/lua/world):** confirmed with offset re-pins; events.scr quest model; chat length-prefix rule (2/7 excl-NUL / 3/21 incl-NUL); 5 camera modes; region cell = INDEX (cross-confirms Block D).
- ~28 curated name proposals → glossary (block F). Full notes in `_dirty/campaign10/F/`.
- **P (promotion): DONE 12/12** (resumed after the session reset — the 5 stalled lanes completed). All 14 F specs rewritten to 100% + verification banners; **Tier-1 firewall scan PASS** (zero artifacts). Several F specs were found already-reconciled from a prior pass and only needed surgical residual fixes (e.g. camera FOV no-/2, the chat NUL off-by-one resolution). Phase-D annotation deferred to the batched wave.

### Block G — Rendering / Effects / Terrain / Skinning / Environment / Sound — **W+P DONE 2026-06-16 (6/6, firewall PASS); D-annot deferred(batched)**
Covered `specs/rendering`, `effects`+`effect-scheduling`, `terrain-streaming`, `environment`,
`skinning`, `sound` (behaviour/runtime; the on-disk formats are Block D). Notes in `_dirty/campaign10/G/`.

**G — reconciled findings (research done; promotion pending on resume):**
- **Rendering:** glow/bloom = 3 RTs, bright-extract is a **plain fixed-function copy (no PS, no threshold)**, one downscaled glow-blur, composite TEX1+TEX2 with code-uploaded c0/c1; cel/toon gated on the **offscreen** path (**TWO** cel pixel shaders dotoonshading.psh+2.psh). Anchor fixes: `0x61bd42` = device-step+**Present**+device-lost recovery (not "render frame"); real draw fork `0x61139E` → offscreen `0x6104cb` / direct `0x610f7c`. **Frame-cap reconciliation with Block A: the rate field is `engine+0x30` = `scene+48` (0x30 hex = 48 dec, SAME field), seeded 60.0f & never overwritten → effective fixed ~60, but mechanically a configurable per-scene rate** (QPC Sleep to 1/rate). Device-lost lifecycle (TestCooperativeLevel → Reset/Sleep) newly documented.
- **Effects:** keyframe sampler (sprite STEPPED, alpha/color/vel/size LERP, rotation SLERP); **passes 2/3/4 = diffuse R/G/B** confirmed at parse; effectscale.xdb **REPLACE at lazy-parse** (closes effects §14.9); **the campaign9c "sub-effect Z-negation" is PORT-SIDE only — the binary applies NO Z-negation** (treats anchor & offset uniformly); vertex diffuse pack order is **B,G,R,A** (load-bearing for on-screen colour); Euler keys are DEGREES. The **10001 timed-event** is a **sorted RB-tree** scene/connection deferred trigger (NOT an effect spawn) — distinct from the linear effect lists.
- **Terrain-streaming:** worker thread **DORMANT** (init clears keep_running); synchronous per-frame main-thread streaming; cell pool **34** vs spatial ring **25** (confirms Block C two-singleton split); cell key `mapZ+100000*mapX` + a **+10000 cell-index origin offset** the doc omitted; radius clamp ≥15000→1000; 5×5-vs-3×3 by radius>1000; 4-function ring (cold 3×3/5×5 + per-frame 3×3/5×5).
- **Environment / Skinning / Sound (G4/G5/G6):** research complete (`_dirty/campaign10/G/`); **skinning** (the load-bearing Godot avatar-explosion debt) consolidated — bind-pose / inverse-bind-baked-into-vertex / LBS deform math for the Phase-E fix; details to be folded in at G promotion.

**W EXIT (per block):** all lanes returned; confidence rated; conflicts flagged; promotion map drafted.

---

## Phase P — Promotion / rewrite to 100% + master synthesis — **DONE 2026-06-16**
- All 7 blocks promoted (one author per spec file, verification banner on each); **Tier-1 firewall scan PASS per block.**
- **Master synthesis `specs/client_architecture.md` written** (478 lines, 12 sections, firewall PASS) — the top-level vision: entry → init scopes → GameState 0..7 scene machine → Diamond UI framework → VFS/resource pipeline → frame loop → network/gameplay/render subsystems, cross-linking every re-verified subject spec.
- Tier-1 serialized: `opcodes.md` reconciled (Block-E lane); `journal.md` CAMPAIGN-10 provenance entry appended. **`names.yaml` merge** = the one remaining mechanical step (pull the ~195 live IDB names via `ida-naming-sync` + adjudicate the 6 ctor collisions) — do at Consolidation/commit.

## Phase D — IDB annotation (legibility) — **DONE 2026-06-16 (batched)**
`re-annotation-orchestrator` applied `_dirty/campaign10/glossary.yaml` to the live IDB (build `263bd994`,
SHA-gate passed) — 216 entries → **201 unique addresses** (183 functions + 18 globals): **113 renamed**
(sub_ → canonical), 82 already-canonical (CRT `start`/`g_GameState` left as-is), **201 repeatable comments
set**, **0 unresolved / 0 failures**. `sub_` count **19,090 → 19,020** (−70 made legible). IDB not committed.
- **⚖️ 6 cross-campaign name collisions — TIER-1 ADJUDICATION at the names.yaml sync:** the campaign
  re-identified the REAL address of 6 ctors and the prior-campaign holder still owns the unsuffixed name,
  so the C10 addresses got a non-destructive `_0`/`_2` suffix. Re-point + demote the stale holders:
  `Diamond_GUComponent__ctor` 0x615135 (vs prior 0x52db68), `Diamond_GUWindow__ctor` 0x61d852 (vs 0x61d71e),
  `Diamond_GULabel__ctor` 0x6162be (vs 0x61626c), `Diamond_GUTextbox__ctor` 0x616df7 (vs 0x616d90),
  `Diamond_GUCheckBox__ctor` 0x617d2e (vs 0x617cbc), `TerrainManager_GetSingleton` 0x445694 (vs 0x445890).
  The comments DID land on the correct C10 addresses, so the IDB reads correctly regardless.
- **Next:** `ida-naming-sync` pulls the 195 live IDB names → `Docs/RE/names.yaml` (Consolidation).

## Phase E — Engineering: align code to corrected specs — PENDING
Staged pipeline (contracts → components → integration), one engineer per project per wave. Only where
a spec changed. `test-engineer` coverage alongside.

## Phase T — Tooling (parallel) — PENDING
Fold scanners into `vfs-inspect`; register the missing orchestrator agent-types
(`godot-client-`, `network-stack-`, `assets-pipeline-`, `client-core-`, `tooling-`, `quality-gate-`);
new parsers for any newly-found format. `tooling-auditor` PASS.

## Phase R — Review + Fix + Hard Gates — PENDING
4 reviewers (render / C# / clean-room / architecture; + perf if hot paths) → fix wave → hard gates:
build 0/0 (`--no-incremental`) · tests green · firewall PASS · verification-banner audit · headless boot.

## Phase C — Consolidation — **IN PROGRESS 2026-06-16**
- [x] ROADMAP statuses updated in place (all blocks W+P+firewall; Phase D done).
- [x] `journal.md` — CAMPAIGN-10 provenance entry appended.
- [x] memory — `campaign10-doc-reverification.md` written + `MEMORY.md` index line.
- [ ] `names.yaml` — sync the ~195 live IDB names (`ida-naming-sync`) + adjudicate the 6 ctor collisions (do at commit).
- [ ] `preservation-archivist` pass + **commit ONLY on maintainer request** (targeted paths; never `_dirty/`, `.godot/`, or originals).

**DOCUMENTATION MILESTONE REACHED:** the user's core ask — re-understand all of `doida.exe` against IDA and
re-work every `Docs/RE/*.md` to 100% — is COMPLETE (7 blocks W+P+firewall, master synthesis, IDB annotated).
**Phase E (engineering) is the next stage** (align C#/Godot to the corrected specs; needs build/test/Godot gates).

### ⏸ RESUME ANCHOR — paused 2026-06-16 on a session limit (resets ~04:50 Europe/Paris)

**Where we are:** all 7 research blocks (W) are COMPLETE (A,B,C,D,E,F,G — every `_dirty/campaign10/*` note
written). Promotion (P) is done + **firewall PASS** for **A, B, C, D, E**; **F is 7/12 promoted**
(firewall scan still pending on those 7); **G is researched but not promoted**. The glossary
`_dirty/campaign10/glossary.yaml` holds ~190 reconciled name proposals across all 7 blocks.

**To resume (in order):**
1. **Finish Block-F promotion** — the 5 stalled lanes: `camera_movement`, `chat`+`social`,
   `lua_scripting`+`lua-config`, `minimap`, `world_systems` (resume `campaign10-block-f-promote` via
   `resumeFromRunId: wf_6c8bf579-1a7` so the 7 done lanes return cached). Then **firewall-scan all 12 F specs**.
2. **Promote Block G** (6 lanes from `_dirty/campaign10/G/`): `rendering`, `effects`+`effect-scheduling`,
   `terrain-streaming`, `environment`, `skinning`, `sound`. Then firewall-scan.
3. **Master synthesis** — write `Docs/RE/specs/client_architecture.md` (the top-level map: entry → init
   scopes → GameState 0..7 scene machine → Diamond GU* window framework → VFS/resource pipeline → frame
   loop → network/gameplay/render subsystems), cross-linking every re-verified subject spec.
4. **Tier-1 serialized post-promotion** — reconcile `opcodes.md` (final sort/dedup), merge the glossary's
   canonical names into `Docs/RE/names.yaml`, append the CAMPAIGN-10 `journal.md` provenance entry.
5. **Phase D — IDB annotation** (batched, now that all research is done): `re-annotation-orchestrator`
   → `re-ida-annotator` applies `_dirty/campaign10/glossary.yaml` to the live IDB via `/ida-annotate-batch`
   (dry-run → apply; unbridled; idempotent). Raises the named-function count on the construction/UI/VFS/net clusters.
6. **Phase E — engineering alignment** — only where a spec CHANGED: the load-bearing ones are the **GUComponent
   geometry transposition** (UI layout), the **skinning** math (avatar explosion), the **3/4·3/7·3/14 opcode
   ladder** + frame-header u32, the **VFS header** (`VFS001`/FILETIME), and the inbound-no-cipher fact.
   One engineer per project; staged; `test-engineer` alongside.
7. **Phase R — review + hard gates** — 4 reviewers + the **verification-banner audit** (every touched doc
   stamped) + build 0/0 (`--no-incremental`) + tests green + clean-room PASS. (Pre-existing minor for the
   clean-room-auditor: a `__thiscall` provenance mention in `journal.md`.)
8. **Phase C — consolidation** — finalize ROADMAP, journal, names.yaml, memory; **commit only on the
   maintainer's explicit request** (targeted paths).

**Do NOT restart from Phase 0** — the campaign is ~85% through W+P. Resume at step 1 above.

### ⚖️ PENDING MAINTAINER DECISION
(none yet)

— *Maintained by the orchestrator (Tier-1). Update block/phase statuses in place as waves complete.*
