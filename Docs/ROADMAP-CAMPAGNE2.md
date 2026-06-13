# CAMPAIGN 2 — Make `doida.exe` legible (launched 2026-06-13)

> **Run record for Campaign 2.** The *method* lives in `Docs/PLAN-CAMPAGNE2.md`; this file is the
> *record* — phase statuses updated **in place** as waves land. This file is **separate from**
> `Docs/ROADMAP.md` (Cycle 4's tracker) and must not modify it.

**Mandate (maintainer, verbatim):**
> « Préparer toute une CAMPAGNE, avec énormément de PHASES READONLY ou WRITE, et dans IDA faire du
> RENAME (+ commentaires). Des Orchestrators Agents qui déploient des sub-agents eux-mêmes : (1) en
> READONLY pour comprendre finement comment fonctionne le client `doida.exe` — chaque pseudo-code,
> ses références, ses appels, son usage, son utilité — afin de guider les futurs agents en WRITE ;
> (2) en WRITE pour renommer et commenter dans IDA (y compris exécuter du Python IDA, y compris les
> renommages qui propagent sur des éléments réutilisés), en se basant sur les recherches READONLY. »

**Theme:** comprehension → annotation. **Product:** a fully understood + renamed + commented IDB,
a `names.yaml` populated from it, and provenance. **No C# this campaign.**

**Locked decisions:** D1 Direct + sync-back · D2 new Orchestrator-Agents · D3 prioritized subsystems
· D4 lead naming for everyone (Cycle 4 consumes the annotated IDB). See `PLAN-CAMPAGNE2.md §1`.

---

## Evidence baseline (Phase 0, captured 2026-06-13)

- **Binary:** `doida.exe` · sha256 `63fcaf8e81a6…9eb9df` (✅ == `names.yaml.binary.sha256`) · md5
  `81634fe4d6b0667ea23a184ab2a90e2e` · imagebase `0x400000` · image size `0x64d000` · 831 segments.
- **Function census:** **25,973 total** — **2,790 named**, **1,905 library**, **21,278 unnamed
  (`sub_xxxx`)**. **3,904 strings.** Entry points: `start @0x6691ec`.
- **`names.yaml` state:** `functions: {}` and `globals: {}` are **EMPTY** — the 2,790 IDB names were
  applied directly in the database and never pulled back. **Phase E sync-back will be the first
  population of the address→name maps.**
- **Tool baseline:** IDA MCP **UP** (`doida.exe.i64`, hexrays ready) on the dbg-extended endpoint.
  Engineering build/test not relevant this campaign (no C#).

> **Legibility target:** drive the 21,278 `sub_xxxx` down — prioritized clusters first (§ backlog).

---

## Phase 0 — Mandate & Pre-flight — **STATUS: ✅ DONE (2026-06-13)**
- [x] Mandate captured (above).
- [x] IDA MCP health OK; SHA-256 confirmed == `names.yaml`.
- [x] Baseline census recorded.
- [x] `Docs/RE/_dirty/campaign2/{cartography,comprehension,applied}/` namespace created (+ README).
- [x] Apparatus authored + `tooling-auditor` PASS (Phase T).

## Phase T — Tooling (parallel with A–E) — **STATUS: ✅ DONE (2026-06-13)**
| # | Lane | Agent | Deliverable | Verified by |
|---|------|-------|-------------|-------------|
| T1 | `re-comprehension-orchestrator` | `agent-author` | `.claude/agents/re-comprehension-orchestrator.md` | ✅ auditor PASS |
| T2 | `re-annotation-orchestrator` | `agent-author` | `.claude/agents/re-annotation-orchestrator.md` | ✅ auditor PASS |
| T3 | `re-ida-annotator` | `agent-author` | `.claude/agents/re-ida-annotator.md` | ✅ auditor PASS |
| T4 | `/ida-annotate-batch` skill | `skill-author` | `.claude/skills/ida-annotate-batch/SKILL.md` + `scripts/annotate_batch.py` | ✅ auditor PASS |
**T STATUS:** ✅ all 4 artifacts authored; `tooling-auditor` PASS (frontmatter valid, names unique,
tool allowlists correctly stratified: orchestrators hold `Agent`, the worker does not; write-IDA
tools only on the annotation tier). Advisory: `CLAUDE.md` tooling-count line is stale (41 agents /
46 skills / 24 hooks on disk vs "27/23/10" stated) — pre-existing drift, left for the maintainer.

## Phase A — Cartography (READONLY macro) — **STATUS: ✅ DONE (2026-06-13)**
Driver: one `re-comprehension-orchestrator` (Opus 4.8, single-agent macro pass). Output written to
`_dirty/campaign2/cartography/overview.md` + `cluster-backlog.md`.
**A EXIT:** ✅ every prioritized cluster (1–5) has a defined function-set, lane breakdown, confidence;
expand surface located + tagged.
**Headline:** the network/crypto/VFS **spine is already named AND richly commented** — Phase B is
mostly **resorbing the `sub_xxxx` neighborhood** around confirmed anchors, not re-discovering the
spine. Census refined: 25,973 total · 21,278 `sub_xxxx` · 4,695 non-`sub_` named. Two largest unnamed
masses: effects `0x497000+` (~1,120 `sub_`) and render `0x60C000+` (~1,090 `sub_`).

### Cluster backlog (refined by Phase A — anchors are `_dirty/`-only addresses)
| Prio | Cluster | Anchor (addr · label) | B lanes | ≈ fns / `sub_` | Conf | Phase-B status |
|------|---------|------------------------|---------|----------------|------|----------------|
| 1 | **network-dispatch** | `0x5f6725 NetHandler_DispatchGamePacket` (major/minor switch + two 154-slot jump tables) | 5 | ~350 / ~210 | HIGH | ⏳ next |
| 2 | **crypto-session** | `0x63e563 Cipher_XorRolEncrypt` (XOR/ROL, header plaintext) | 3 | ~70–90 / ~55 | HIGH | PENDING |
| 3 | **vfs-assetio** | `0x60a5ab VFS_OpenArchive` (data.inf 144B TOC + data/data.vfs) | 3 | ~80–120 | HIGH | PENDING |
| 4 | **scene-machine** | `~0x5fe000 WinMain` + 3 `Diamond_*_BuildScene` | 4 | ~150–250 | MED-HIGH | PENDING |
| 5 | **effects-render** | `0x4a5dc9 XEffect_tickAndDispatch` + Diamond render pipeline | 6 (depth-capped) | ~2,000+ / ~2,200 `sub_` | HIGH/MED | PENDING |
| 6+ | **expand** | UI · sound · combat · Lua · terrain · config-crypto · patcher (located + tagged) | TBD | remainder of 21,278 | MED | PENDING |

**Phase-A `CONFLICT:` flags carried into Phase B:** `sub_41CADC` (xref 2094, heuristic "dispatcher")
is the **msg-string catalogue lookup**, NOT the packet dispatcher — do NOT seed cluster 1 on it.
Scene-machine lane scoped to state-wiring to avoid UI/effects double-ownership. ADVAPI32 `Crypt*` =
`ConfigCryptoApi`/anti-cheat, out of scope for cluster 2 (anti-debug risk C2-R8).

## Phase B — Deep comprehension (READONLY, per cluster) — **STATUS: ✅ DONE (5/5 clusters, 2026-06-13)**
Per cluster: `re-comprehension-orchestrator` (Opus 4.8) fans out Tier-3 analysts (IDA sub-waves of 3).
Output: `_dirty/campaign2/comprehension/<cluster>/*.md` + `names.proposed.yaml` + `comments.proposed.md`.
**B EXIT:** cluster critical functions understood; manifests complete; conflicts flagged.
**Note:** clusters run **sequentially** — the single IDB caps IDA at ≤3 concurrent consumers, so two
comprehension orchestrators cannot run at once.

| Cluster | Lanes | Proposed names | Conf | Status |
|---------|-------|----------------|------|--------|
| network-dispatch | 5 (2 sub-waves; 0 dead) | 53 in-cluster (18H/16M/19L) + ~52 deferred + ~75 routed out | HIGH | ✅ DONE 2026-06-13 |
| crypto-session | 3 (1 sub-wave; 0 dead) | 38 fns: 30H/3M/2L | HIGH | ✅ DONE 2026-06-13 |
| vfs-assetio | 3 (1 sub-wave; 0 dead) | 60 fns (41 in/12 unproven/7 out) + 11 globals | HIGH | ✅ DONE 2026-06-13 — pak.md + resource_pipeline.md CONFIRMED; fixes: TOC base `0x8C3ECC`, IDB "+8"→"+12" |
| scene-machine | 4 (2 sub-waves; 0 dead) | ~95 fns; 57 symbols 39H/18M/0L | HIGH | ✅ DONE 2026-06-13 — refines client_workflow §4.4; PIN keypad found; scheduler now-ms split → debugger lane |
| effects-render | 6 (2 sub-waves; 0 dead) | ~100 addrs 78H/20M/3L (+deferred deep internals) | HIGH/MED | ✅ DONE 2026-06-13 — resolved several OQ-EFX; corrected formats/effects.md §E.2 (particleEmitter.eff variable-length); SwordLight ribbon still OPEN |

**network-dispatch headline names:** `NetHandler_DispatchGamePacket`, `NetHandler_HandleNetworkEvent`
(cmd-102 control vs cmd-100 data), `NetClient_RecvWorkerThread`/`_StartNetworkEngine`/`_Disconnect`,
`NetConn_Shutdown`/`_SetLastErrorString`, `NetSocket_ConstructSlot`, the `NetPacketDeque_*` queue
family, and the 5/52 `SkillActionList_*`/`SkillActionRing_*` container. Conflicts resolved (guest/member
ctor ownership; `_fseeki64` CRT shim mis-aliased; shared string-buffer ctor named by role).

## Phase C — Reconciliation & glossary (Tier-1 gate) — **STATUS: ✅ DONE (2026-06-13)**
Merged 5 `names.proposed.yaml` → `_dirty/campaign2/glossary.yaml`. Apply-set **380** (361 fns + 19
globals, high+med). 6 conflicts arbitrated by Tier-1 (cmd-102 `NetHandler_HandleNetworkEvent`;
generic `CriticalSection_Enter/Leave`; `GHTexCache_FlushAll`; `Singleton_GetCameraConfig`;
`ThreadSlot_SetPriority` dup → held `0x648ae2`). Holdbacks: ~100 low_confidence, 78 out_of_cluster,
10 deferred ranges.
**C EXIT (HARD GATE):** ✅ neutrality PASS (no `sub_/loc_/_DWORD/__thiscall`/mangling; quoted keys;
no dup name→2 addrs; no collision with the 309 existing `names.yaml` canonical names).

## Phase D — IDA annotation (WRITE, serialized) — **STATUS: ✅ DONE (2026-06-13)**
`re-annotation-orchestrator` drove **one `re-ida-annotator` at a time**, `/ida-annotate-batch`
(dry-run → apply, idempotent) per cluster. Maintainer pre-cleared IDB writes. **380/380 applied, 0
failed.** Census: `sub_` **21,278 → 21,076 (−202)**, named **4,695 → 4,897 (+202)**; the other 178
canonicalized already-named fns. Fixups applied (pak `+8`→`+12`; `sub_41CADC`→MessageDB; VirtualProtect
togglers; shadow-projection REFINE). Reports: `_dirty/campaign2/applied/<cluster>.md`.

| Cluster | Entries | Renamed | Noop | Overwrite | Failed |
|---------|--------:|--------:|-----:|----------:|-------:|
| network-dispatch | 61 | 60 | 0 | 0 | 0 |
| crypto-session | 32 | 19 | 13 | 1 | 0 |
| vfs-assetio | 72 | 28 | 39 | 4 | 0 |
| scene-machine | 66 | 46 | 20 | 0 | 0 |
| effects-render | 149 | 56 | 66 | 27 | 0 |

## Phase E — Sync-back · Verify · Provenance (Tier-1) — **STATUS: ✅ DONE (2026-06-13)**
- [x] Pull-back → merged **380 entries** into `names.yaml` (`functions:` 361 + `globals:` 19 — FIRST
  population; the maps were empty). Sorted, quoted keys, cluster-tagged neutral notes.
- [x] Pull-back confirmed 380/380 addresses read the glossary canonical name in the live IDB (Phase D).
- [x] Firewall PASS: `names.yaml` grepped clean (no `sub_/loc_/dword_/_DWORD/__thiscall`); the sync agent
  **caught & sanitized 7 leaking `dword_` tokens** in comments before write; nothing under `_dirty/`
  tracked; no originals staged; `.gitignore` intact.
- [x] `journal.md` provenance entry appended (5 clusters by canonical name + counts; no pseudo-code).
- [x] Roadmap + memory (`campaign2-ida-legibility`) updated.
**E STATUS:** ✅ DONE. Committed artifacts ready for maintainer: `Docs/RE/names.yaml`,
`Docs/RE/journal.md`, `Docs/ROADMAP-CAMPAGNE2.md`, `Docs/PLAN-CAMPAGNE2.md`, and the 3 new agents +
`/ida-annotate-batch` skill under `.claude/`. **No commit made — awaiting maintainer request.**

---

## ✅ CAMPAIGN 2 — RUN COMPLETE (2026-06-13)
First full pass done end-to-end (0·A·B·C·D·E·T). The 5 priority spine clusters are understood,
renamed, and commented in the live IDB; `names.yaml` now carries 380 address→name mappings.
**Result:** IDB `sub_` 21,278 → 21,076 (−202), named 4,695 → 4,897 (+202), 380 neutral comments set.
**Next-cycle candidates (expand surface, all PENDING):** UI/widget toolkit, sound, combat/gameplay
handlers, Lua surface, terrain — plus the deferred effects-render deep internals (particle math,
SwordLight ribbon §14.8 still OPEN) and the spec REFINEMENTs queued for a spec-author
(client_workflow §4.4, formats/effects.md §E.2, crypto.md §6.5). One debugger lane recommended for
the scheduler now-ms split before its comment is locked.

### ⚖️ PENDING MAINTAINER DECISION — Cycle-4 coordination
Campaign 2 leads IDB naming (D4) and shares `names.yaml` / the single IDB with the active Cycle 4
session. **Rule:** one writer of `names.yaml` and one IDB writer at a time. When both sessions are
live, the maintainer sequences Campaign-2 Phase D/E writes against any Cycle-4 IDB/names.yaml write.

---

*Maintained by the Top Orchestrator (main session). Update phase statuses in place as waves land.
Commit only on explicit maintainer request.*
