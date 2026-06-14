# ROADMAP — CAMPAIGN 3 · `doida.exe`: Workflow · UI/UX · VFS

> **Live run record for the project's single active campaign.** The *method* lives in
> [`PLAN.md`](PLAN.md); this file is the *record* — phase statuses updated **in place** as waves land.
> Fresh start by maintainer decision (G2): prior Cycles 1–4 + Campaign 2 live in git history and
> `Docs/RE/journal.md`. Generic doctrine: [`CAMPAIGN_TEMPLATE.md`](CAMPAIGN_TEMPLATE.md).
>
> **▶ RESUME ANCHOR (2026-06-14):** pivoted to **CAMPAIGN 4 — Front-End Fidelity** (see the section
> directly below). **The World scene is FROZEN** by maintainer decision; CAMPAIGN 3's broader lanes are
> paused (their record is preserved below for provenance). No commit yet (continue-then-commit-later).

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
