# ROADMAP — CAMPAIGN 3 · `doida.exe`: Workflow · UI/UX · VFS

> **Live run record for the project's single active campaign.** The *method* lives in
> [`PLAN.md`](PLAN.md); this file is the *record* — phase statuses updated **in place** as waves land.
> Fresh start by maintainer decision (G2): prior Cycles 1–4 + Campaign 2 live in git history and
> `Docs/RE/journal.md`. Generic doctrine: [`CAMPAIGN_TEMPLATE.md`](CAMPAIGN_TEMPLATE.md).
>
> **▶ RESUME ANCHOR (2026-06-14):** active campaign = **CAMPAIGN 7 — Re-anchor & Total IDB Legibility on
> the NEW `doida.exe` build** (see the section directly below). The prior IDA database crashed
> irrecoverably and was rebuilt from scratch on a **DIFFERENT build** (input sha256 `263bd994…6fd8ee` ≠ the
> old `names.yaml` pin `63fcaf8e…9eb9df`) — so **all prior addresses are stale** and Campaign 6's
> IDB-resident naming is gone. CAMPAIGN 6/4/3 are **paused**, their records preserved below for provenance.
> **The World scene remains FROZEN.** No commit yet.

---

## CAMPAIGN 7 — Re-anchor & Total IDB Legibility on the NEW `doida.exe` build (IDA-only)

**Mandate (maintainer, 2026-06-14):** the prior `doida.exe` IDB crashed unrecoverably; a fresh IDB was
rebuilt (auto-analysis + `decompile_all`). Work **EXCLUSIVELY in IDA Pro** (the `ida` MCP) — rename /
comment / type only, **never patch**. Re-establish full IDB legibility on the new build by **re-anchoring
the prior corpus by CONTENT** (the addresses no longer transfer), then comprehending the residual. Reuse
the Campaign-6 apparatus; deploy a large agent fleet (~80–100 deployments) with heavy IDAPython batching.
Method/charter: `PLAN.md` (CAMPAIGN 7) + plan file `~/.claude/plans/tr-s-bien-je-vais-melodic-sketch.md`.

**The decisive different-build fact (Phase 0, verified live this session):**
- New `doida.exe` · input **sha256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`**
  · md5 `a1437026…` · imagebase `0x400000` · `.text` `0x401000–0x71e000`.
- **≠ the old pin** `63fcaf8e…9eb9df`. **All 14 sampled prior-anchor addresses miss** (not function starts,
  unnamed). → **addresses do NOT transfer**; a blind `names.yaml`-by-address apply would mislabel ~3,417
  functions (forbidden). The fast "restore by address" path is dead.
- **Content DOES transfer** (verified): **459 RTTI `.?AV` class descriptors** (incl. exact `.?AVNetHandler`);
  signature strings (`items.scr`, `bgtexture`, `actormotion`, `skin.txt`, `bindlist`, `data.vfs`, `.ted`);
  library markers (`Vorbis`, `XTrap`, `Lua`, `D3DXMatrix`). Unpacked PE, hexrays ready.
- **Live census:** **25,791 functions** — 1,688 auto-named — 1,901 FLIRT library — **22,202 unnamed `sub_`
  = the work scope** — 4,800 strings — 6 segments.
- Reuse dictionary/oracle: the prior corpus (`names.yaml` 3,417 entries + campaign6 glossaries ~3,007) as a
  **content-anchored name dictionary** (addresses discarded) + the committed clean specs (≈37 specs / 32
  formats / 10 structs / 71 packets / 193 opcodes) as the comprehension oracle.

**Scale model:** ~80–100 deployments across ~6 waves; **≤3 concurrent IDA readers / exactly 1 serialized
IDB writer**. Depth = **deep / ROI-aware** (re-anchor + comprehend all high-value + bulk-tag lib noise; the
long leaf/thunk/STL tail is NOT chased — it plateaus ~3k, per Campaign 6). **Autonomous with ONE
checkpoint** before the first mass IDB write. Genuine **version-deltas** vs the committed specs are promoted
clean (Phase S). End-state: `names.yaml` re-pinned to `263bd994…`.

| Phase | Status |
|---|---|
| **0 Pre-flight & setup** | ✅ DONE — MCP UP; new SHA pinned & asserted ≠ old; census (22,202 `sub_`); `_dirty/campaign7/` tree created; apparatus inventoried. |
| **A Cartography & feature dumps** | ✅ DONE — RTTI harvest (431 classes) + profile_all over 23,811 (+const histogram) + string-xref/import indexes + callgraph (62,393 edges) → `_dirty/campaign7/`. |
| **T Tooling** | ✅ DONE — `content_reanchor.py` (string/RTTI/const matcher) + `cg_propagate.py` (BinDiff-style call-graph propagation) + filter/QA/census/build scripts in `_dirty/campaign7/tools/`. |
| **A′ Re-anchor matcher** | ✅ DONE — 728 HIGH (RTTI-deterministic) @ 0% FP / 56 audited. |
| **🔶 CHECKPOINT** | ✅ DONE — re-anchor report + dry-run (728/728 would-apply, 0 TRIPWIRE) presented; maintainer GO = "apply + Phase B spec-guided". |
| **D0 Anchor-apply (serialized WRITE)** | ✅ DONE — **721 applied** (7 `j_` thunks skipped), 0 fail, TRIPWIRE 0, read-back OK, IDB saved. |
| **B Comprehension / re-anchor (residual)** | ◑ AUTO-PHASE SATURATED — B1 call-graph propagation **+1,122** (margin≥0.16, audited, floor band demoted); MED-verify **+322** of 504 (ground-truth ctor check); ctor-QA fixed **11** wrong-class ctors; B2 expanded re-run = saturated (Panel-ctor blind spot). **Manual spine comprehension of ~1,057 unplaced names = open worklist.** |
| **C Reconcile HARD GATE** | ✅ DONE (rolling per-batch) — neutrality + adversarial audit gated every write; no batch applied without a clean dry-run + FP check. |
| **D Serialized annotation** | ✅ DONE (batches) — cumulative **≈2,176 re-anchored & applied**; named census ≈4,574; TRIPWIRE 0; exactly one writer throughout. |
| **S Spec-delta promotion** | ✅ DONE — 5-lane spec-author promotion (firewall PASS): opcodes.md (~120 S2C handler roles + 2 new opcodes), NEW specs/network_dispatch.md (S2C dispatch architecture + NetClient lifecycle), crypto.md (cipher/RSA reconfirmed + secure-ctx 0x2E20 + tab login), packets/5-53 (32B vitals/pair), resource_pipeline.md (VFS runtime). Deltas flagged: 5/28 position in-body, 4/143-4 observed. |
| **E Sync-back** | ✅ DONE — durability via NEW committed-tree **`Docs/RE/names.build2.yaml`** (re-pinned `263bd994…`, 2,591 fns + 19 globals, neutrality PASS); `journal.md` CAMPAIGN-7 entry appended. Old `names.yaml` left intact (curated); merge/re-pin decision surfaced to maintainer. |
| **R/Z Re-census & verify** | ✅ DONE — firewall PASS (0 pseudo-C in committed files, no `_dirty/` tracked); census recorded; ROADMAP updated. |

**FINAL STATE (2026-06-15):** the new-build IDB is legible at **≈4,685 user/FLIRT-named functions of 25,791** (≈2,467 canonical game + 848 library-tagged), all gains adversarially audited with **0 surviving structural FP** and **TRIPWIRE 0** throughout. Path: content re-anchor (721 RTTI + 1,122 call-graph propagation + 322 verified-MED + 11 ctor-fixes) → à-fond residual waves A1/B/C/D (+385 placed; **~509 mislabeled-library priors correctly REFUTED**) → **two ~20-agent fan-out Workflows: Wave E (+207, the full S2C handler family `Smsg*` + scene singletons) and Wave F (+138, highest-value unnamed game functions)**. **Durability + merge done:** `Docs/RE/names.yaml` re-pinned to `263bd994…` — **3,315 functions + 19 globals**, neutrality PASS; old build-1 corpus preserved in git history (8918ece). Remaining = the low-ROI tail (leaf/thunk/STL) + rtti-MED ctor variants (63) + optional spec-promotion (Phase S) of the recovered S2C/crypto/vfs interop knowledge. No commit yet (awaiting maintainer).

**16-cluster taxonomy** (reused from Campaign 6, re-clustered for the new build): C01 rtti-class-core · C02
net-transport · C03 net-dispatch · C04 crypto · C05 vfs-assetio · C06 asset-parsers · C07 anim-skinning ·
C08 render-pipeline · C09 scene-machine · C10 ui-toolkit · C11 ui-hud · C12 actor-combat · C13
world-systems · C14 lua-config · C15 sound-input · C16 misc-residual + LIB 3rd-party.

**Dirty namespace:** `Docs/RE/_dirty/campaign7/{rtti,profile,anchor,cartography,comprehension/<cluster>,glossary,applied,census,tools}/`.

---

## CAMPAIGN 6 — Total IDB Legibility: name/comment/type the whole `doida.exe` (IDA-only)

> ⏸ **PAUSED — superseded by CAMPAIGN 7.** Its IDB-resident naming was lost when the database crashed; the
> work survives as the `names.yaml` corpus + `_dirty/campaign6/` glossaries, which CAMPAIGN 7 re-anchors
> onto the new build. Record preserved below for provenance.

**Mandate (maintainer, 2026-06-14):** work **EXCLUSIVELY in IDA Pro 9.3** (the `ida` MCP) on `doida.exe`.
Deep-analyse the whole client; rename functions, globals, classes, struct fields, locals; add comments;
recover & apply struct/enum/RTTI types; consolidate the IDB into a workspace "au petits oignons". Deploy a
large agent fleet (~75 deployments) + heavy IDAPython batching. `doida.exe` is the source of truth.
Method/charter: `PLAN.md` (CAMPAIGN 6) + plan file `~/.claude/plans/dans-ce-plan-on-tranquil-panda.md`.

**Scale model (maintainer-chosen):** ~75 agent *deployments* across ~6 sequential waves; at any instant
**≤3 concurrent IDA readers / exactly 1 serialized IDB writer**. **Wave-by-wave with checkpoints.** Depth =
"complet pragmatique" (names + comments + struct/enum/RTTI **types + prototypes**; struct fields & call-site
args auto-named by type propagation; semantic locals only on the `complex` hot-set).

### Evidence baseline (Phase 0, live this session) — supersedes the stale "4,897 named" figure
- `doida.exe` · sha256 `63fcaf8e…9eb9df` (== `names.yaml` pin) · md5 `81634fe4…` · imagebase `0x400000`.
- **Live census:** 25,973 functions — **2,993 named** (~1,905 MSVC/CRT library, *never* renamed) —
  **≈ 21,075 anonymous `sub_` = the work scope** — 3,599 strings, 6 segments, MSVC RTTI present.
  (Prior campaigns' IDB writes are largely absent from the current i64; **21,075 `sub_` is the true
  denominator** — risk C6-R8 realised, so all coverage % measure against this live number.)
- Oracle = the committed clean specs (≈37 specs / 31 formats / 10 structs / 70+ packets). Every proposed
  name aligns with already-promoted terminology; it extends, never contradicts.

| Phase | Status |
|---|---|
| **0 Census / Pre-flight** | ✅ DONE — MCP UP on `?ext=dbg`; SHA == pin; denominator pinned at ≈ 21,075 `sub_`. |
| **A Cartography** (Pass 1 profile + Pass 2a RTTI → cluster into 16) | ✅ DONE — 22,273 unnamed profiled; **431 RTTI classes** (~1,290 vtable fns) recovered; call-graph (62,410 edges) dumped; partitioned into 16 clusters by anchor/RTTI/import/string seeds + call-graph propagation. C01=1,120 · net C02+C03=67 *(spine already named in C2)* · C11/C16 lumpy (refine before W4/W6). See `_dirty/campaign6/cartography/overview.md`. |
| **B Comprehend — W1: C01 rtti-class-core · C02 net-transport · C03 net-dispatch** | ✅ DONE — 3 lanes: C01 manifest **1,488** proposals (vtable fns + 615 ctors) + the GU 14-slot semantic map (onDraw/onEvent/onUpdate/hitTest/computeTransform/…) inherited by ~400 widgets + net remainder (mostly STL; the net spine was already named in C2). |
| **C Reconcile + firewall gate (W1 glossary slice)** | ✅ DONE — gate-passed `glossary.yaml` = **1,551** fns; **288** GU placeholders upgraded to behavioural names; 318 name dups resolved; **0 neutrality violations**. |
| **D Annotate IDB (W1, serialized dry-run → apply)** | ✅ DONE — dry-run reviewed → applied **1,537** names+comments; **0 failed**; 1 conflict + 13 runtime safely skipped; IDB saved. |
| **R/Z W1 — re-census + sync + checkpoint** | ◑ re-census PASS — confirmed **1,537** named; **TRIPWIRE = 0 library renamed**; `still_default`=14 (skips). **`journal.md` + `names.yaml` full sync DEFERRED to commit** (IDB is the live source of truth; concurrent writer touched journal). ⏸ awaiting go for **W2 (crypto/VFS/parsers)**. |
| **W2 — C04 crypto · C05 vfs · C06 parsers** | ✅ DONE — applied **277** names (cumulative **1,815** confirmed; TRIPWIRE 0 lib-renamed); structs recovered & staged for a type pass (secure-ctx 0x2E20, DiskFile 88B, VfsEntry 144B, GHTex 76B, game.ver, wind.bin). **KEY FINDING:** the unnamed mass is heavily **statically-linked 3rd-party libs** — FLINT bignum (~232, the RSA substrate, now tagged), CxImage codec (~400), Lua, zlib, Boost, XTrap, BugTrap. Cartography over-attributes them via call-graph propagation. |
| **L Library-ID + re-cluster** | ✅ DONE — **572** 3rd-party fns identified (CxImage/STL/FLINT/Boost/Lua/libjpeg/XTrap); **132** newly tagged `<Lib>__`; lib set excluded from game clusters. **Finding:** the big buckets (C11 ui-hud ~5,941 · C16 residual ~8,930) are GAME-code mis-clustering (UI propagation), **not** libs — handled by per-lane analyst flagging (proven W1/W2), not exclusion. Cumulative confirmed **1,938**; TRIPWIRE 0. |
| **W3 — C07 anim · C08 render · C09 scene** | ✅ DONE — applied **158** (C07 91: PoseNode/`AnimCatalog_ResolveModelClassId` + structs BndBone 72B/PoseNode 88B/SkinWeight36; C08 52 + 663 render leaves family-tagged for a later accessor pass; C09 19 + structs GameState/EngineView/TickScheduler + C2S opcodes 2/0x70,2/0x89,2/0). Cumulative **2,097**; TRIPWIRE 0. **Diminishing returns confirmed** — C09 was ~1,250/1,522 propagation noise. |
| **T Struct-type pass** | ✅ DONE — **12 structs** declared in the TIL (GameState/EngineView/PoseNode 88B/BndBone 72B/SkinWeight36/Diamond_GUComponent 208B/Diamond_DiskFile 88B/VfsEntry 144B/GHTex/SecureContext 0x2E20/RsaKeyPair/Bignum); **110 functions** this-typed (DiskFile 42·EngineView 22·GUComponent 21·PoseNode 15·BndBone 8·GameState 2); **4 globals** typed (g_GameState/g_EngineView/g_VfsTocBase/g_VfsTocCount). Decompiler field-propagation verified. 0 failures. |
| **W4 — C10 ui-toolkit · C11 ui-hud · C12 actor-combat** | ✅ DONE — applied **100** (C10 28: GUScroll/GUScrollEx + GUTextureList/GUCmdHandler structs; C11 49: chat slash-cmd interpreter + 1000×36B chat ring + 5×8 trade grid; C12 24: actor-sim + ActorBuffArray 30×12 + SkillActionRecord 1468B + **closed actor.md level@+0xBA**). Cumulative **2,197**; TRIPWIRE 0. Plateau confirmed (C11 = ~185 genuine of 5,941). **New 3rd-party lib found: libVorbis 1.3.2 (~115 fns @0x6dd000-0x6f2fff)** — to tag. |
| **Consolidation** | ✅ DONE — **2,110** names synced to `names.yaml` (cumulative ~2,513); CAMPAIGN 6 entry appended to `journal.md`. (W4's +100 pending a re-sync at commit.) |
| **W5 — C13 world · C14 lua · C15 sound + libVorbis** | ✅ DONE — applied **522** (C13 80 world/NPC/quest + ItemSlotRt/QuestTemplateRt; C14 31 lua_tinker glue + **391 LIB-Lua VM**; C15 20 sound/input + settings persistence) + **libVorbis 1.3.2 = 273** band-tagged. `names.yaml` re-synced (+895 → ~3,417 total); `journal.md` updated. |
| **Campaign 6: HIGH-VALUE NAMING COMPLETE** | ✅ ~**2,992** functions named/tagged (5 waves + library-ID + struct types: 12 structs / 110 this-typed / 4 globals); **TRIPWIRE 0** throughout. All 15 game clusters + 3rd-party libs (FLINT/CxImage/Lua/libVorbis/zlib/libjpeg/STL/Boost/XTrap) identified. **Remaining:** C16 residual ~8,900 low-value leaf/thunk/STL tail (optional bulk auto-name) + git commit (⚠ entangled tree — targeted only). |

**16-cluster map & coverage targets** (full table in the plan file): C01 rtti-class-core, C02 net-transport,
C03 net-dispatch (W1) → C04 crypto, C05 vfs-assetio, C06 asset-parsers (W2) → C07 anim-skinning, C08
render-pipeline, C09 scene-machine (W3) → C10 ui-toolkit, C11 ui-hud, C12 actor-combat (W4) → C13
world-systems, C14 lua-config, C15 sound-input (W5) → C16 misc-residual + locals (W6). Cumulative
function-name coverage targets: **W1 ~19% · W2 ~36% · W3 ~56% · W4 ~79% · W5 ~97% · W6 ~100%.**

**Dirty namespace:** `Docs/RE/_dirty/campaign6/{cartography,profile,rtti,comprehension/<cluster>,debugger,glossary.yaml,applied,census}/`.

---

## CAMPAIGN 4 — Front-End Fidelity: Login · PIN · Server-List · Char-Select (1:1 with the official client)

**Mandate (maintainer, 2026-06-14):** stop the World scene; refocus entirely on making the very first
scenes (Login → PIN/2nd-password → Server-List → Character-Select) **pixel-faithful to the official
client** (reference screenshots supplied). Pull everything from the VFS and `doida.exe` — understand how
the UI/UX, VFX and SFX are assembled/linked/used — and **rework the Godot front-end cleanly to match**.
Deploy many phases / orchestrators / agents in parallel.

| Phase | Status |
|---|---|
| **FE-1 Recovery** (doida scene composition + VFS textures + VFS audio/VFX) | ✅ DONE — 2 window classes + 1 modal; the widget-factory convention `(texId,X,Y,W,H,srcUV…)` cracked; atlas DDS inventory (`loginwindow.dds`, `password.dds`, `characwindow.dds`, `openning_scenario.dds`, …); BGM 920100200, UI SFX 861010101; the **red ribbon = the OpeningWindow intro crawl**, not a login effect; `.xeff` parser bug found. |
| **FE-2 Recovery (exact layout)** | ✅ DONE — the complete per-widget screen-rect + source-UV-rect dump (Login 74 widgets, PIN modal+keypad, Server-List rows, Char-Select slots/buttons); the SFX-event map (GUButton plays nothing; the owning window does); the ribbon mechanism (vertical crawl 30 px/s + slideshow); the front-end captions pulled from `msg.xdb`. |
| **FE-3 Promotion** | ✅ DONE — `frontend_scenes.md §11` (pixel-exact rebuild contract), new `formats/msg_xdb.md` + `specs/intro_sequence.md`, refined `sound.md §15` + `effects.md §A.15`. Firewall PASS (no addresses, no literal Korean — captions resolve from the VFS at runtime). The `.xeff` parser fixed (header 8→32B; char_select/zone_sel parse; +28 tests). |
| **FE-4 Faithful Godot rebuild** | ◑ IN PROGRESS — LoginScreen (12 faithful layers §11.2), PinModal (§11.3 Fisher-Yates keypad), ServerSelectScreen (§11.4), CharacterSelect (§11.5) rebuilt from the spec; `MsgXdbCatalog` (CP949) wired so labels read real text from `msg.xdb`. **Full build 0/0**; login screenshot now shows the real stone chrome + background art + gold buttons (major step up from the placeholder). **Next:** PIN/server-list/char-select screenshots; wire BGM 920100200 + SFX 861010101; the cursor; the front-end VFX (`zone_sel`/`char_select` xeff → Godot); the intro crawl; per-scene fidelity polish vs the official. |
| **FE-5 VFX/SFX + intro** | PENDING — front-end particle effects, audio, the OpeningWindow intro. |
| **FE-R Render-fidelity review** | PENDING — `godot-render-reviewer` screenshots vs the 4 official references. |

---

## Mandate (maintainer, verbatim)

> « On veux poursuivre les travaux pour faire les travaux qui sont de REVERSE le client de jeu
> "doida.exe" dans IDA et meme si on a besoin tu peux partir en "debugger" avec IDA. On veux donc
> poursuivre la gestion de le WORKFLOW, que ça soit le démarage du CLIENT de jeu, et faire attention
> à la partie UI/UX (GUI) et poursuivre proprement les configurations de la partie "UI" du jeu. et
> ensuite poursuivre la compréhension et ajustement des travaux pour lire le "VFS" et les fichiers
> (format de fichiers) à l'intérieur ainsi que leurs liaisons leurs fonctionnement. Il faut
> poursuivre très très fortement les configurations et aller vraiment plus loin, et donc poursuivre
> le reverse. »

**Theme:** deep reverse of the workflow / UI-UX / VFS gaps → clean specs → annotated IDB → wired
client. **Scope (G1):** Reverse + Specs + Client (full pipeline). **Lead (G4):** CAMPAIGN 3 owns IDB
naming. See `PLAN.md §1`.

---

## Evidence baseline (Phase 0)

- **Binary:** `doida.exe` · sha256 `63fcaf8e81a6…9eb9df` (== `names.yaml.binary.sha256`) · md5
  `81634fe4d6b0667ea23a184ab2a90e2e` · imagebase `0x400000` · image size `0x64d000` · 831 segments ·
  entry `start`.
- **Function census (post-Campaign-2):** 25,973 total — **4,897 named**, **21,076 unnamed (`sub_`)**,
  ~1,905 library. 3,904 strings.
- **`names.yaml` state:** **380** address→name mappings (361 functions + 19 globals) from Campaign 2's
  spine clusters. CAMPAIGN 3 grows this into the new clusters.
- **Recovered baseline:** boot→login→PIN→char-select workflow ~95%; login/char-select UI ~95%; VFS
  container + 16 format specs (23 extensions) confirmed. See `PLAN.md §0`.
- **Tools:** IDA MCP **UP** on the dbg-extended endpoint; `dotnet build` / `dotnet test` to be greened
  at pre-flight; VFS reachable via `Assets.Vfs`.

---

## Known blockers & open questions (carried into the waves)

| # | Item | Target phase | Note |
|---|------|--------------|------|
| OQ-1 | ~~Opcode `1/6` collision~~ — **RESOLVED in Phase A (static):** `1/6` is char-create only; the login credential is a distinct sub-opcode (PIN = its optional blob). The "collision" was a 52-byte size coincidence. | ~~B1/Dbg~~ → **C** | Only residual: protocol-spec lane re-checks the capture's major/minor bytes before rewriting the spec. |
| OQ-2 | **In-game HUD coordinates** — inventory/chat extracted in B2; **5 panels** (stats/minimap/party/skill/trade) blocked on a Phase-D `define_func` over the undefined HUD-build blob. | B2 ✅ partial → D → E(05) | Inventory W=732 right-anchored, chat 290×18; buff bar data-driven via `buff_icon_position.xdb`. |
| OQ-3 | **Char-select preview camera** — Canvas3D framing; B2 found **6 keyframes**, interpolation law UNVERIFIED. | B2 → **Dbg** → C | Needs the live debugger to confirm which keyframe shows + the easing. |
| OQ-4 | ~~`environment_bins` semantics~~ — **RESOLVED in B3:** fog 192B colour table, sky.box 20B vertex stride, 48-slot 86400 s day cycle; env hub = sky-colour-table singleton. | B3 ✅ → C | Water = render-pass concern (C5), no asset-IO loader. |
| OQ-5 | ~~`actormotion.txt` col3–14~~ — **RESOLVED in B3:** int/float column types authoritative; two 9-element directional sub-arrays; 15 fps X/Y rates. | B3 ✅ → C | New `formats/actormotion.md` in Phase C. |
| OQ-6 | **Lua pipeline** — `game.lua`/`uiconfig.lua` entry + config keys + table layouts. | **B4** → C | Phase A: Lua is **statically linked** (its `.rdata` "imports" are string literals). |
| OQ-7 | **Scheduler now-ms split** — Campaign-2 deferred; debugger lane recommended. | **B5** → Dbg | Lock the comment only after debugger confirm. |

---

## Phases

Each phase: ÉTAPES / OBJECTIFS / Tier-2 owner / done-when. Statuses updated in place.

### Phase 0 — Mandate & Pre-flight — **STATUS: ✅ DONE (2026-06-13)**
*Owner: Tier-1 (main session).*
- [x] Mandate captured (above).
- [x] IDA MCP green on `?ext=dbg`; `D:\IDAPro\doida.exe.i64` open, hexrays ready, imagebase `0x400000`,
  auto-analysis ready (`server_health` OK, uptime confirmed).
- [x] `dotnet build MartialHeroes.slnx` **0 warning / 0 error** (13.5s). `dotnet test` deferred to Phase R
  (no C# touched until Phase E).
- [ ] VFS reachable (`Assets.Vfs`) — verified on demand during Phase B3 / Phase E.
- [x] Evidence baseline recorded (above).
- [x] Single-writer rules confirmed (one IDB writer, `names.yaml`/`journal.md` Tier-1-only).
- [x] `_dirty/campaign3/{cartography,comprehension,debugger,applied}/` namespace — created by Phase A.
- **DONE-WHEN:** pre-flight checklist green; master deliverables named (`PLAN.md §7`). ✅

### Phase A — Cartography refresh — **STATUS: ✅ DONE (2026-06-13)**
*Owner: `re-cleanroom-orchestrator` (READONLY, bounded to dirty cartography — no spec promotion this phase).*
- Re-mapped the `doida.exe` spine from the Campaign-2 named anchors; located + tagged the B1–B6 anchors.
- 6 READONLY lanes in 2 sub-waves of 3 (`re-static-analyst` ×5 + `ida-script-author` ×1). No IDB writes.
- **Backlog:** B1 workflow-spine (HIGH, 3 lanes) · B2 ui-window+HUD (HIGH/MED, **heaviest**, 2 lanes) ·
  B3 vfs-assetio (HIGH, 2 lanes) · B4 lua (HIGH, 1 lane) · B5 sound+combat (HIGH, 2 lanes) · B6
  terrain-stream (HIGH, 1 lane). **Total ~9–10 Phase-B Tier-3 lanes.**
- **Headline — OQ-1 dissolved statically:** the `1/6` "collision" premise is **false per the binary**.
  `1/6` is **char-create only** (single emitter, single readable phase branch); the login credential is
  a **distinct sub-opcode** on the auth/secure path whose optional field is the PIN/second-password blob.
  "Both 52 bytes" is a size coincidence. *Caveat (→ Phase C):* the protocol-spec lane must re-check the
  motivating capture's actual major/minor bytes before any spec rewrite.
- **10 CONFLICT flags reconciled** (carried to Phase B/C) — e.g. a HUD-build blob is **undefined code**
  (needs `define_func` before coordinate extraction); a seed "VFS open worker" is an MSVC GS-cookie stub
  (drop it); Lua is **statically linked** (its `.rdata` "imports" are string literals); the SOUND_* tab
  table is a dev serializer, not a runtime parser; combat scheduler is a linear active-list walk.
- **Phase-Dbg targets reduced:** B5 now-ms deadline split (`Time_GetMs`→`timeGetTime`, confirm the gate
  live) and B2 char-select preview-camera matrices. B1 needs no debugger (only the capture re-check).
- **DONE-WHEN:** ✅ backlog + reconciled CONFLICT register under `_dirty/campaign3/cartography/`
  (`overview.md`, `cluster-backlog.md`, `b1`–`b6` lane files, `scripts/b4_lua_census.py`).

### Phase B — Deep comprehension (lanes B1–B6) — **STATUS: ✅ DONE (all 6, 2026-06-13)**
*Owner: `re-cleanroom-orchestrator` (READONLY, IDA sub-waves of ≤3). Two sub-waves: B1/B2/B3 (mandate
priorities) then B4/B5/B6. ~11 Tier-3 lanes; zero IDB writes; ≈410 manifest entries.*
- **B1 workflow-spine-deep — ✅ DONE.** Login = **sub-opcode 0x2B** (account len-prefixed + optional
  PIN/second-password + RSA PKCS#1 v1.5 17-byte plaintext; `account\tpassword\tPIN\thost:port`
  contract); PIN keypad → `BillingState+72`; scene sub-state table (connect→form→server-list→select
  →submit→enter-load); char-mgmt family (1/0 logout · 1/6 create 52B · 1/7 select · 1/9 enter 40B ·
  1/13 rename · 1/14 move). OQ-1 confirmed dissolved.
- **B2 ui-window-manager + HUD — ✅ DONE.** `MainWindow("MainMaster")` IS the window manager;
  GUComponent/GUPanel/GUWindow byte-offset field tables; HUD coords extracted (inventory W=732
  right-anchored, chat 290×18); **202 widget ctors named from RTTI**; C3 resolved (`buff_icon_position.xdb`).
  *Caveat:* 5 HUD panels' coords live in an undefined-code blob → blocked on a Phase-D `define_func`.
- **B3 vfs-assetio-deep — ✅ DONE.** 144-byte TOC stride pinned; **RAW/uncompressed verdict** (no
  codec); 3-way open-mode flag table; `actormotion.txt` col3–14 resolved (int/float types, directional
  sub-arrays); fog/sky/cloud/star formats tabulated. C4 GS-cookie false anchor dropped.
- **B4 lua-scripting — ✅ DONE.** One process-wide **statically-linked Lua 5.1.2 VM** (lazy open; the
  only C binding is `cpp_load`; no gameplay objects exposed); config globals `vfsmode`/`launcher`/
  `debugmode`. **N-B4-2 (load-bearing): Lua text tables decode as UTF-8, NOT CP949.**
- **B5 sound-effects + combat-timers — ✅ DONE.** Sound = OGG play-by-kind (2D/3D, distance²-culled);
  combat = one now-ms/frame → 4 linear-walk managers (NOT a heap), +48 start-gate vs +64 elapsed-origin
  reconciled, death FSM 11 states.
- **B6 terrain-stream — ✅ DONE. N-B6-2 (load-bearing): streaming is SYNCHRONOUS per-frame ring-shift;
  the async worker/FIFO is dormant compiled-in scaffolding.** Fixed cell ring (3×3≤1000/5×5>1000),
  25-object pool, `.lst` key = mapZ + 100000·mapX.
- **DONE-WHEN:** ✅ all 6 lanes — dossiers + `names.proposed` + `comments.proposed` under
  `_dirty/campaign3/comprehension/`; conflicts flagged (10 from A + 4 from B1–3 + 4 from B4–6).

### Phase Dbg — Debugger confirmation — **STATUS: ◑ IN PROGRESS (B1a ✅ 2026-06-13)**
*Owner: Tier-1 via the live `?ext=dbg` session (maintainer F9-launched; never `dbg_start`).*
- **B1a login 0x2B — ✅ CONFIRMED live** (`_dirty/campaign3/debugger/b1a-login-0x2b-confirm.md`):
  byte-exact 0x2B plaintext (sub-opcode + len-prefixed account + a7-gated len-prefixed PIN); **M = fixed
  17-byte zero-padded** RSA password field (OPEN ITEM resolved); builder is **__thiscall (ctx in ECX)**
  — a live correction; login string = `account\tpassword\tPIN\thost:port`. Credential VALUES not recorded.
- **Remaining (need server-driven progression / specific screens):** encrypt BP + RSA ciphertext framing
  `[u32 LE len][BE digits]` + header set to secure `1/4`; B1b 52-byte char-create; B1c port-10000 server-
  list worker threads; B2 char-select preview-camera matrices (OQ-3); B5 scheduler now-ms split (OQ-7,
  after B5 comprehension).
- **DONE-WHEN:** each load-bearing hypothesis marked *"verified under the IDA debugger"* in
  `_dirty/campaign3/debugger/`.

### Phase C — Reconciliation & Promotion — **STATUS: ✅ DONE (all batches, 2026-06-13/14 · HARD GATE PASS)**
*Owner: Tier-1 + `asset-spec-author` ×4 + `protocol-spec-author`. Every batch self-scrubbed; Tier-1
firewall grep found zero pseudo-C/addresses across all committed specs.*
- **UI/UX:** new `structs/gucomponent.md` + `structs/guwindow.md`; new `specs/ui_hud_layout.md`
  (inventory W=732, chat W=290×18, buff bar, **+ §3 the 5 panels' recovered coords** from the b2c
  define_func extraction); refined `specs/ui_system.md` §1.6–1.8 + `frontend_scenes.md` §3.5.
- **Protocol (B1, debugger-verified):** `opcodes.md` (1/6 resolved = char-create only; login = secure
  1/4 carrying 0x2B; 0/0 key-exchange 62B), new `packets/login.yaml` + `packets/cmsg_char_*.yaml` +
  `cmsg_logout.yaml`, new `specs/login.md`, refined `specs/crypto.md` + `login_flow.md`.
- **VFS/formats (B3):** refined `formats/pak.md` (144B TOC, RAW verdict), new `formats/actormotion.md`
  (OQ-5 resolved), new `formats/sky.md` (OQ-4 resolved), refined `specs/environment.md`.
- **Lua/sound (B4/B5):** new `specs/lua-config.md` (**N-B4-2 UTF-8 not CP949**), `specs/sound.md` §15.
- **Combat/terrain (B5/B6):** new `specs/effect-scheduling.md`, `specs/terrain-streaming.md`
  (**N-B6-2 synchronous**), `structs/terrain-manager.md`.
- **Full HUD sweep:** `ui_hud_layout.md` §5 — the complete in-game HUD (**152 placement sites** from the
  defined HUD-build routine, grouped by region + the 4 anchor conventions; screen-centred modals are the
  dominant idiom). Pixel resolution of screen-relative panels pending a known-resolution read.
- **Tier-1 owed before commit:** `journal.md` provenance entries + `names.yaml` sync for all promoted
  names (incl. the **1/14 move-vs-delete** reconciliation) — deferred to Phase Z.
- **DONE-WHEN:** ✅ firewall PASS; every promoted constant citable; engineers have specs.

### Phase D — IDA annotation (WRITE, serialized) — **STATUS: PENDING**
*Owner: `re-annotation-orchestrator` → one `re-ida-annotator` at a time.*
- `/ida-annotate-batch` dry-run → apply the reconciled renames/comments/types for the new clusters.
- Sync-back the live IDB names into `names.yaml`; append `journal.md` provenance.
- **DONE-WHEN:** apply-set applied 0-failed; `sub_` count down; `names.yaml` synced; firewall PASS.

### Phase E — Engineering wave (parallel disjoint lanes) — **STATUS: ◑ IN PROGRESS (multi-lane, 2026-06-13/14)**
*Owners (parallel, disjoint paths): `godot-ui-engineer` · `network-protocol-engineer` · `data-tables-engineer`.*
**Integration verified after the concurrent wave: full `dotnet build MartialHeroes.slnx` 0/0 · full
`dotnet test` GREEN (all 10 suites — 49+120+92+347+21+71+… , 0 failures).**
- **Godot UI/UX (05) — ◑ active (4 cycles).** Cycle 1: inventory **W=732 right-anchored**, ChatWindow
  reconciled, `HudPanelConfig.cs`. Cycle 2: buff bar made visible; World HUD renders with real assets
  (hotbar `musajung.dds`, minimap). Cycle 3: inventory grid populated (64 `texturelist.txt` icons; the
  9-digit item-id scan bug fixed), login captions fixed (4001–4022 are EULA not button labels), preview
  camera FOV 50°/near 5/far 15000. Cycle 4: **the 5 HUD panels implemented at recovered coords** —
  minimap top-right (screenW−135), stats (180,95) + 3 sub-panels, party/trade off-screen slide-in,
  skill bar (349,13) 9-slot; screenshot-verified. Login renders CP949 Korean from the real VFS.
- **Network.Protocol (02) — ✅ char-mgmt lane done.** `[Pack=1]`/`[InlineArray]` structs for the
  CharacterMgmt request family (1/0,1/6,1/7,1/9,1/13,1/14) + opcode router arms, all spec-cited; 92 tests
  green. `login.yaml` (var-length RSA carrier) deferred to a joint protocol+crypto lane.
- **Assets (03) — ✅ actormotion lane done.** Typed 136-byte `ActormotionEntry` record + parser +
  queryable catalogue, spec-cited; 347 tests green (compat shims kept layer-05 `NpcRenderer` working).
- **Network.Crypto (02) — ✅ login credential cipher done.** `CredentialPlaintext` (17-byte zero-padded
  M) + `LoginCredentialReply.Build` (full secure 1/4 payload: 0x2B pre-image + RSA `[u32 len][BE]` +
  whole-payload 0x29 whitening); RSA PKCS#1 v1.5 modexp + key-exchange parse already present; 85 tests
  green. Runtime server key + the layer-04 driver migration deferred (joint hand-off).
- **Assets.Parsers (03) — ✅ sky.box parser done.** `SkyBoxParser`/`SkyBoxData` (texture table + 20-byte
  vertex stride + u16 indices, caps 300/900); 16 new tests (363 total green).
- **Client.Infrastructure (04) — ✅ lua-config reader done.** `LuaConfigReader`/`LuaConfigRecord` (typed
  boot flags vfsmode/launcher/debugmode + DISPLAY_*; **UTF-8 decode per N-B4-2**; orthogonal to the
  SQLite settings store); 13 new tests green.
- **Godot char-select (05) — ✅ cycle 5.** `CharPreview3D` 6-keyframe orbit (FOV 50°/near 5/far 15000 +
  12 angle multipliers) with a manual pose selector; `CharacterSelectScreen` 6 pose buttons +
  Korean-named slots. Screenshot-verified (slots, Korean names, pose buttons). *Known debt: the
  SubViewport 3D preview renders dark — pre-existing skinning/WorldEnvironment debt, out of scope.*
- **Godot HUD (05) — cycle 6.** Key full-HUD elements from §5: `RightEdgeGaugePanel` (HP/MP, screenW−135
  @ Y200/250), `BottomActionBar` (centerX(1024)/screenH−60), `TopStatusBar` (full-width), and a reusable
  **`CenteredModal`** base (`center=(screen−size)/2`) + a representative `ConfirmDialog` (340×190 family).
  Screenshot-verified. The other ~79 centred modals derive from the helper.
- **Code hygiene — ✅.** `csharp-modernizer` swept the Campaign-3 C# (02/03/04): removed dead code +
  redundant usings, C#14 idiom polish; behaviour-preserving (wire layouts byte-identical); 1296 tests
  still green, 0 warnings.
- **Pending lanes:** the layer-04 login driver migration to the new credential API; Application
  use-cases for the full workflow; the sound/combat/terrain-streaming specs into engine code; the
  SubViewport char-preview env/skinning fix; a full-HUD Godot pass deriving all centred modals.
- **DONE-WHEN:** build 0/0; DAG downward-only; specs cited; tests green; Godot headless boot clean.
  *(full-solution build + test re-verified after each concurrent wave.)*

### Phase T — Tooling (parallel with A–E) — **STATUS: PENDING**
*Owner: `tooling-orchestrator`.*
- Deepen `vfs-inspect` subcommands; add a HUD-coordinate dump skill; refine `ida-debugger-drive`.
- **DONE-WHEN:** `tooling-auditor` PASS; smoke-tested; no copyrighted bytes.

### Phase R — Review & Hard Gates — **STATUS: PENDING**
*Owner: `quality-gate-orchestrator`.*
- Parallel read-only review: firewall, DAG, C# idioms, perf hot paths, build/test, render, 1:1 fidelity.
- **DONE-WHEN:** one PASS/FAIL verdict; blockers separated from advisories; gates green.

### Phase Z — Consolidation & Commit — **STATUS: PENDING**
*Owner: Tier-1.*
- Update ROADMAP statuses, `journal.md`, `names.yaml`, auto-memory; `preservation-archivist` pre-commit.
- **DONE-WHEN:** docs coherent; commit **only on explicit maintainer request** (branch first if on
  default).

---

*Maintained by the Top Orchestrator (main session). Update phase statuses in place as waves land.
Commit only on explicit maintainer request.*
