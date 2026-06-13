# ROADMAP — CAMPAIGN 3 · `doida.exe`: Workflow · UI/UX · VFS

> **Live run record for the project's single active campaign.** The *method* lives in
> [`PLAN.md`](PLAN.md); this file is the *record* — phase statuses updated **in place** as waves land.
> Fresh start by maintainer decision (G2): prior Cycles 1–4 + Campaign 2 live in git history and
> `Docs/RE/journal.md`. Generic doctrine: [`CAMPAIGN_TEMPLATE.md`](CAMPAIGN_TEMPLATE.md).
>
> **▶ RESUME ANCHOR:** Phase 0 (Mandate & Pre-flight) — campaign launched **2026-06-13**.

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

### Phase B — Deep comprehension (lanes B1–B6) — **STATUS: ◑ B1–B3 ✅ DONE · B4–B6 PENDING (2026-06-13)**
*Owner: `re-cleanroom-orchestrator` (READONLY, IDA sub-waves of ≤3). Clusters run sequentially.
First sub-wave = B1/B2/B3 (the mandate priorities). 7 Tier-3 lanes in 3 sub-waves; zero IDB writes;
≈377 manifest entries (~240 HIGH).*
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
- **B4 lua-scripting · B5 sound-effects+combat-timers · B6 terrain-stream — PENDING** (second sub-wave).
- **4 new conflicts** for Phase-C arbitration (incl. a **mandatory self-scrub** note: b1b/b2a dossiers
  carry raw pseudo-C/RTTI fragments confined to `_dirty/` — strip before any committed spec).
- **DONE-WHEN (B1–B3):** ✅ dossiers + `names.proposed.yaml` + `comments.proposed.md` under
  `_dirty/campaign3/comprehension/{b1-…,b2-…,b3-…}/`; conflicts flagged.

### Phase Dbg — Debugger confirmation — **STATUS: PENDING**
*Owner: Tier-1 via `/ida-debugger-drive` (maintainer F9-launches; never `dbg_start`).*
- Confirm against the running client: `1/6` routing by phase (OQ-1), VFS open/read at a live pointer,
  login/PIN blob pre/post, HUD widget live addresses (OQ-2), preview-camera matrices (OQ-3), scheduler
  now-ms split (OQ-7).
- **DONE-WHEN:** each load-bearing hypothesis marked *"verified under the IDA debugger"* (high
  confidence) in `_dirty/campaign3/debugger/`.

### Phase C — Reconciliation & Promotion — **STATUS: PENDING · HARD GATE**
*Owner: Tier-1 + `protocol-spec-author` + `asset-spec-author`.*
- Reconcile lane proposals → gate-passed glossary (`CONFLICT:` arbitration; neutrality gate).
- Promote/refine specs (`PLAN.md §7`): `client_workflow` §4.4 + `1/6`; new `ui_hud.md`; new
  `vfs_internals.md`; `lua_scripting.md`; formats field-semantics; `opcodes.md`/`packets/*.yaml`.
- Firewall scan; `journal.md` + `names.yaml` updated (Tier-1 only).
- **DONE-WHEN:** firewall PASS; every promoted constant citable; engineers have specs to read.

### Phase D — IDA annotation (WRITE, serialized) — **STATUS: PENDING**
*Owner: `re-annotation-orchestrator` → one `re-ida-annotator` at a time.*
- `/ida-annotate-batch` dry-run → apply the reconciled renames/comments/types for the new clusters.
- Sync-back the live IDB names into `names.yaml`; append `journal.md` provenance.
- **DONE-WHEN:** apply-set applied 0-failed; `sub_` count down; `names.yaml` synced; firewall PASS.

### Phase E — Engineering wave (parallel disjoint lanes) — **STATUS: PENDING**
*Owners: `network-stack-` · `assets-pipeline-` · `client-core-` · `godot-client-orchestrator`.*
- **Network (02):** login/PIN/server-list/char-select/enter-world structs + source-gen router; `1/6`
  resolved in `Network.Protocol`.
- **Assets (03):** VFS internals + clarified parsers + CP949 data-table loaders (actormotion, environment).
- **Core (04):** Application use-cases for the workflow + HUD event channels.
- **Godot (05):** boot→login→PIN→char-select→world front-end; in-game HUD per recovered coords; preview
  camera; atmosphere/water shaders.
- **DONE-WHEN:** build 0/0; DAG downward-only; specs cited; tests green; Godot headless boot clean.

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
