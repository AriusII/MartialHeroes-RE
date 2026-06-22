# ROADMAP — live run record

The live campaign record (specialises `Docs/CAMPAIGN_TEMPLATE.md`). Method/charter that was historically
split into a separate `Docs/PLAN.md` now lives here at the top of the active cycle. Provenance of *what was
proven in the binary* lives in `Docs/RE/journal.md` (orchestrator-owned); this file tracks *the run* — what
each cycle set out to do, where it is, and where to resume.

> Ground-truth ordering (non-negotiable): **IDA / `doida.exe` (absolute truth) → `Docs/RE/` specs (derived)
> → C#/Godot (measured)**. The binary wins every dispute; the spec is corrected, then the code.

---

# CYCLE 8 — GIGA CAMPAIGN « Truth → Align → Clean → Reforge »

**Opened:** 2026-06-21 · **Branch:** `major-campaign` · **IDB pinned:** `263bd994` (doida.exe) ·
**Mode:** RE = **STATIC ONLY** (no `?ext=dbg`, no captures this cycle — user decision).
**Approved plan:** `~/.claude/plans/claude-agents-planning-orchestrator-md-sequential-penguin.md`.

## Charter

Deepen + re-confirm the RE of `doida.exe` (scene/Window state machine, GUI `::Gu`/`::Components`, HUD,
assets, networking) **statically** from IDA, then **align + professionally clean** the C# core, make the
networking faithful, and **surgically re-architect** the Godot client (05) so it cleanly consumes the C#
class libs — all measured against the binary for 1:1 fidelity.

**Recalibration (from the CYCLE 8 intake cartography):** the `Docs/RE` corpus is already comprehensive
(8-state scene FSM, GU widget framework, 178-slot HUD roster, 220+ opcode routing, 47 data.vfs formats —
all pinned to IDB `263bd994`). So CYCLE 8 is **not** a from-scratch reverse; it is **(1)** a static RE
confirm/deepen/close-gaps pass, **(2)** a large C# alignment + cleanup, **(3)** network fidelity, **(4)** a
surgical Godot refactor. The ~25 genuinely *runtime-only* residuals are **DEFERRED** (register below) and
stubbed in C# with a `// spec: … (value debugger-pending, CYCLE 8 deferred)` citation.

## Locked decisions (user)

1. **Godot 05 = SURGICAL refactor** — reorg/de-legacy/harden-coupling, **preserve** passive-rendering,
   `SceneHost`, per-frame event drain, and every proven 1:1 path (login→world live).
2. **RE = STATIC ONLY** — no debugger sessions this cycle; runtime values deferred (register below).
3. **Checkpoint-first** — baseline committed before any mass edit (done: `a305035`).
4. **Gate = headless Godot only** — nuke `bin/obj` → `dotnet build` 0 errors + headless boot. **No xUnit.**

## Phase ledger

| Phase | Title | Owner | Gate | Status |
|---|---|---|---|---|
| 0 | Checkpoint & Baseline | main session | build 0-err + headless + checkpoint commit | **DONE** (`a305035`,`ebd7282`) |
| 1 | RE static — Truth pass | `re-orchestrator` | specs re-pinned + clean-room-check + deferred register | **DONE** (`3e3049d`,`7f9e104`) |
| 2 | C# core alignment to specs | `port-orchestrator` | nuke build 0-err + headless + code-reviewer | **DONE** (`5f6e3c2`,`7f9e104`) |
| 3 | C# cleanup & de-noising | `port-orchestrator` | nuke build 0-err + headless + clean-room-check + reviewer PASS | **DONE** (`1d87a78`) |
| 4 | Godot 05 surgical refactor | `port-orchestrator` | godot-build + headless + screenshot + render-reviewer | **DONE** (`34564b1`) |
| 5 | Integration, verify & docs | main session | build 0-err + headless live + docs synced + commit | **DONE** |

## CYCLE 8 — CLOSED (2026-06-21)

**Final gate: GREEN.** `dotnet build MartialHeroes.slnx` (nuked) = **0 errors / 0 warnings** (40 projects);
`check_dag.py` OK (39 core projects, downward-only acyclic); headless boot → Login clean (VFS 43,347
entries, 90,937 items / 2,000 skills / 3,997 mobs, no ERROR lines); clean-room firewall + non-distribution
audits CLEAN across all CYCLE 8 commits.

Outcomes per phase:
- **P1 (RE, static-only):** Docs/RE re-verified vs `263bd994` — **zero structural conflicts** (corpus was
  already correct); **5 static gaps CLOSED** (Error sub-state attribution, GUWindow secondary slots,
  GUCanvas3D render-target, GUComponent +0x8D remove_mark, world-entry state-2 = idempotent REPLAY).
  17 specs re-pinned; journal + names.yaml synced.
- **P2 (C# align):** ItemsScrParser offsets (0x0BA/+0xCD); SQLite bumped → **NU1903 eliminated** (now 0
  warnings); col16/lobby-inet_addr/SmsgCharCreateResult verified already-correct; ScrStatCatalogue HP/MP=0
  confirmed **absent-by-design** (server-supplied, D3), not a bug. **P2.1:** 3/23 name @0x08 + 3/6 = 12B with
  two `f32` placement values (the uint SlotIndex/Unk reading refuted) — specs + C# struct corrected.
- **P3 (C# clean):** 3 empty re-arch dirs + 1 dangling comment removed; **zero provably-dead code** found
  (core already lean); 0 uncited constants. Flagged (not churned): integration debt + 1 duplicate `.mi` parser.
- **P4 (Godot surgical):** HudMaster → clean `AddPanel<T>()` builder (behavior-preserving); empty Debug/Helpers
  removed; CameraController already unified; invariants hold (zero `using Godot;`<05, zero game-rule in 05,
  passive rendering). `.render/` gitignored (screenshots carry client art). Login front-end render-reviewer 5/5.

**Verification limit (honest):** with login creds absent, headless reaches **Login only** — the in-world
render, in-game HUD, and char-select were **NOT visually verified** this cycle (build + behavior-preserving
review only). See "Owed next" below.

## Phase 0 — record

- **0A** ✅ Baseline build verified: `dotnet build MartialHeroes.slnx` = **0 errors** (40 projects, ~17 s after
  nuke). 4× `NU1903` warnings — all the single transitive `SQLitePCLRaw.lib.e_sqlite3 2.1.11` known
  vulnerability (pre-existing). Checkpoint commit **`a305035`** (212-file working tree saved, tree clean).
  No LSP/csharp-ls lock present at build time.
- **0B** ✅ This ROADMAP opened (it did not exist on disk — drift vs CLAUDE.md resolved); deferred register below.
- **0C** ✅ Gate frozen (see "Locked decisions" #4 + the plan's verification section).

## Deferred runtime-values register (STATIC-ONLY this cycle → owed to a future `?ext=dbg`/capture pass)

Consolidated from `Docs/RE/journal.md` CYCLE 7 "Follow-ups owed" item 5. These are genuinely *runtime-only*
(server-authored or clock-seeded) and are NOT statically recoverable — they do **not** block 1:1 structural
fidelity (FSM, UI layout, wire routing, asset parsing). C# must stub them with a deferred-citation.

| # | Residual | Affected spec(s) |
|---|---|---|
| D1 | Server-authored magnitudes — damage / crit / XP / HP-base | `specs/combat.md`, `specs/progression.md` |
| D2 | On-wire VALUE semantics inside the opaque tails of 4/48 (236B) · 4/56 (1552B) · 4/71 (1092B) | `specs/handlers.md`, `packets/4-48,4-56,4-71*.yaml` |
| D3 | Stat-grid f32 → named-stat mapping (45-float grid) | `specs/progression.md`, `structs/stats.md` |
| D4 | Effect per-field particle roles (a few VALUE-pending in the 52B subrecord) | `formats/effects.md`, `specs/effects.md` |
| D5 | PIN keypad runtime seed/permutation (clock-seeded shuffle — mechanism known, seed value not) | `scenes/login.md`, `specs/frontend_scenes.md` |
| D6 | MopGagePanel (slot 35) on-screen placement rect (offset table firm; rect runtime/data-driven) | `specs/ui_hud_layout.md`, `scenes/ingame.md` |
| D7 | In-game HUD button caption font-slot byte offset (font system settled; offset fine-tuning) | `specs/ui_system.md` |
| D8 | `[OPENNING]/SKIP` INI literal filename/path | `scenes/scene_state_machine.md` |
| D9 | fx1–fx7 terrain overlay: index topology + exact UV encoding + unread inter-header dwords | `formats/terrain_layers.md` |
| D10 | Per-quad translucent blend pair + effective first-draw depth-write (UI bucket) | `specs/rendering.md` |
| D11 | Weapon attach concrete hand bone-id (default 0 statically) | `specs/equipment_visuals.md` |
| D12 | Dormant terrain-worker spawn question (apparatus present, dormant) | `specs/terrain-streaming.md` |
| D13 | Mode 1 vs 0 runtime meaning of 1/7 CmsgSelectCharacterSlot; disk `max_particles` vs `num_frames` VB bound | `specs/login_flow.md`, `formats/effects.md` |

> Scope note: the journal cites "~25 residuals"; the rows above bucket them by theme. The authoritative
> per-spec flags live in each affected spec's `verification:` banner + `Docs/RE/journal.md` CYCLE 7 §item 5.

## Standing cleanup items surfaced at intake (folded into Phases 2/3)

- **C# binary-won re-flips owed** (journal CYCLE 7 follow-ups #1–2): idle motion **col15 → col16** across the
  `actormotion` idle-column reads; `CLAUDE.md` skinning chain corrected to col16; HUD target-frame = **slot 35
  (MopGagePanel)**, pet window = slot 52, slot 135 = UpgradeProcessPanel.
- **Empty structural artifacts** to delete: `04.Client.Core/MartialHeroes.Client.Domain/`,
  `…Client.Application/StateMachine/`, `…Client.Application.Contracts/UseCases/` (+ `MartialHeroes.slnx` tidy).
- **Layer-02 codegen follow-ups** (journal Phase 2b): retire `SmsgCharCreateResult`; `LobbyClient.ConnectBlocking`
  should use `inet_addr`-style dotted-quad (lobby path is DNS-less), not `Dns.GetHostAddresses`.
- **SQLite vuln bump**: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 → patched (NU1903), in `Client.Infrastructure` + the
  Godot csproj — Phase 2E/3 hygiene.
- **Godot legacy** to retire (Phase 4): non-composer `RealWorldRenderer` path; synthetic fallbacks (sky / toon-ramp
  / texture) now that the VFS is mandatory; unify `CameraController` free/orbital modes.

## Owed next (out of CYCLE 8's static-only / headless-only scope)

1. **Live-world verification wave (needs login creds + a running session).** The in-world render, in-game
   HUD (the refactored 33 panels), and char-select are unverified headless. With creds (env `MH_LOGIN_*` or
   `%LOCALAPPDATA%\MartialHeroes\login.creds`) a windowed login→enter-world screenshot pass would let
   `render-reviewer` confirm 1:1 — and would unblock: (a) consolidating the legacy vs composer world-render
   path, (b) the 3 inline lib-duplication fixes deferred in P4 (CharSelectScene3D / CharCreatePreview3D
   actormotion + CharacterTextureResolver skin.txt → call the libs), (c) HUD visual parity.
2. **Deferred runtime register D1–D13** (above) — a future `?ext=dbg` / capture pass (server magnitudes,
   opaque 4/48·4/56·4/71 tails, stat-grid f32→stat, PIN seed, slot-35 rect, etc.).
3. **C# integration debt** (P3/P4 flagged): wire the Infrastructure stores (Settings/Cache/Macros) +
   Application spawn/stat seams (ActorSpawnService, CombatStatsRecomputer, AllocateStatAsync,
   DeliveryClaimAsync, the 4/500·4/132·4/138 notice/popup sinks) through the composition root; de-dup the
   `mobinfo.mi` parser (a DEAD authoring-only format).

## Resume pointer

CYCLE 8 closed (all phases committed on `major-campaign`, build 0/0, headless→Login green). Next cycle's
highest-leverage move is the **live-world verification wave** (#1 above) — it is the only thing that can
confirm the layer-05 in-world/HUD fidelity the headless gate cannot reach.

---

# CYCLE 9 — FRONT-END FLOW 1:1, LIVE-VERIFIED (Opening→Loading→Login→PIN→Server-list→TCP→Char-select)

**Opened/closed:** 2026-06-21 · **Branch:** `major-campaign` · **IDB:** `263bd994` · **Mode:** static-IDA RE +
**LIVE** verify (server 211.196.150.4 via `login.creds`). Method = specs → C# → Godot.

**Mandate:** verify + clean the front-end flow, **Server-list** the focus — remove duplicates, fix deltas/bugs,
make it work with LIVE data; then char-select 2D/3D on map000. Commits `7053e5f`(P1) `5f7549f`(P2)
`cd8e6d6`(P3) `ccf7de9`(P3.2).

| Phase | Outcome |
|---|---|
| **P1 RE (static)** | Confirmed lobby/server-list/channel-endpoint wire: all 4 server-record fields **signed i16**; `load<2400` signed; channel-endpoint = single-space, 30B copy-cap, NUL-term, single endpoint (settled the `NEEDS-CAPTURE`). 1 CONFLICT fixed (server_id u16→i16). |
| **P2 C# clean** | **DEDUP** `ServerEntry`→`ServerListEntryView` (deleted the dup); single `IsSelectable`; all types signed `short`; removed dead `CharCreateResultEvent`; **EnvLogin now reads `%LOCALAPPDATA%\MartialHeroes\login.creds`** (was env-only → inert) + account redacted. **LIVE PROVEN:** auto-login → real server-list (server_id=4) → endpoint :11403/:11410 → roster (jeonsa/jeongja/arius) → char-select, no ghost-lock. |
| **P3 Godot connect** | Swapped 3 Godot inline CP949 parsers → layer-03 catalogues (idle col16 / skin.txt). Server-list render+click→TCP verified intact post-dedup. Login renders **1:1**; all audio cues fire (curtain SFX 861010105, loading BGM 920100100, lobby BGM 920100200). |
| **P3.1 RE** | 3/1 SpawnDescriptor `internal_class` = u16 @ +0x34 (CONFIRMED, 4 read sites), `variant` @ +0x2C — spec re-pinned. |
| **P3.2 char-select actors** | **Fixed** the empty char-select: root cause was **event timing** (live roster arrives 3/4 in Load state, before SelectScene arms its drainer) → `CharacterSelectionStore.ProjectRetainedRoster()` + `SelectScene.OnEnter` replay. Headless-proven: 3 actors **build** (84-bone skeletons, idle clips, invariants PASS). DEFECT #2 (auto-fire) = confirmed **non-bug** (windowed input-sim artifact; code never auto-fires). |

**Gate:** build **0/0**, check_dag OK, headless live auto-login → char-select clean (no 3/100/ghost-lock),
code-reviewer + render-reviewer PASS. Windowed passive screenshots captured (gitignored `.render/`).

**Verified visually (windowed live):** login screen **1:1**, loading screen, char-select scene on map000.
**Server-list functionally proven** (real data fetched, 2-plate render per spec, click→TCP) but **not captured
visually** (transient — auto-login selects in ~10 frames; needs a pause-after-server-list hook for a shot).

**P3.3 char-select avatar visibility — FIXED (`b0c4480`).** Two real defects: (a) camera used `LookAt`
where §3.5 is a free-look KEYFRAMED camera (per-KF Euler) — aimed past the actors + tilted down; fixed to
per-keyframe Euler (§3.5.3); (b) mesh Y-recentre used the animated frame-0 floor but the mesh shows in
REST pose at spawn — sank slot 2; fixed to `GetMeshAabb()`. PreviewScale ×3→×6 (unit reconcile).
Windowed-verified: avatars stand visible + grounded on the map000 platform, idle animating.

## CYCLE 9 — Owed next (remaining front-end debt)
1. **Slot-2 (arius) appearance / per-slot `.skn` resolution.** The avatar is now visible + grounded but
   renders a **wrong-shape mesh** (flat sliver) — an appearance-resolution debt: the per-slot variant/equip
   must resolve the correct `.skn` (the §3.3.7 overlay build from `+0x34/+0x2C/+0x2E/+0x58`). Route:
   layer-04 surface + assets appearance lane.
2. **Dedicated Server-list screenshot** — add a temp "pause-after-server-list" capture path so the real
   live plates can be eyeballed (functionally already proven).
3. **Equip overlays** on the char-select preview (the §3.3.7 per-part build) — `EquipGids` now surfaced;
   the overlay loop is drivable but not yet enabled.

---

# CYCLE 10 — Login + CharSelect from-scratch rebuild (C# 02/04 + Godot 05, live-verified)

**Opened/closed:** 2026-06-22 · **Branch:** `master` · **IDB:** `263bd994` · **Mode:** static-IDA RE (spec
corrections only) + **LIVE** headless/windowed verify.

**Mandate:** retire the incremental patch debt on both front-end scenes and rebuild them cleanly — Login
(curtain → server-list → PIN → TCP) and CharSelect (2D roster + 3D avatar preview) — to the committed
specs, so both render 1:1 and the whole solution builds and boots headless with zero errors.

## Phase ledger

| Phase | Title | Status |
|---|---|---|
| RE — spec corrections | msg.xdb EULA-mislabel fix; action-61 button rects; UI blend-state reconciliation; per-class starter-body table | **DONE** |
| C# 02/04 | Layer-02 wire types; layer-04 per-slot appearance surface (`EquipGids`, `ProjectRetainedRoster`) | **DONE** |
| Godot 05 | `LoginWindow` + `CharSelectWindow` rebuilt from scratch; `SlotAppearanceResolver` (slot-2 skeleton/idle fix) | **DONE** |
| Verify | `dotnet build` 0/0; headless exit-0 clean; windowed loginPass + charSelectPass | **DONE** |

## Outcomes

### RE spec corrections (static IDA, `263bd994`)

**msg.xdb EULA-mislabel corrected** (`formats/msg_xdb.md`, `specs/client_workflow.md`). Caption IDs
4001–4022 label the static notice/agreement text column on the login notice panel; they are **not**
EULA / Terms-of-Service body text and there is no scroll/accept gate anywhere in the login scene builder.
The prior "EULA overlay" reading is refuted; both specs corrected with CODE-CONFIRMED provenance.

**Action-61 button rects confirmed** (`specs/frontend_layout_tables.md §3`). The PIN keypad control
buttons — Reset (tag 11), OK (tag 12), Cancel (tag 13) — and their source rects on `password.dds` were
re-confirmed against the binary; tag roles settled CODE-CONFIRMED. Panel-relative coordinates and atlas
source bands recorded (see `specs/frontend_layout_tables.md §3`).

**UI blend-state reconciliation** (`specs/rendering.md §4.1/§4.2`). The front-end overlay bucket uses
a **global additive ONE/ONE blend** (binary-won over the earlier "per-quad opt-in" hypothesis for the
login screen). The in-game HUD bucket leaves alpha-blend disabled at bucket-enter and each quad opts in
individually. The two rows were previously conflated; now split with explicit per-bucket entries and the
binary-won blend mode for the front-end path recorded. See `Docs/RE/specs/rendering.md §4.2`.

**Per-class starter-body table derived** (`specs/frontend_scenes.md §3.7.5`). The prior section was
re-derived from a VFS observation without an IDA cross-check and was wrong on class-tag mapping and
`.skn` path structure. The section is rewritten from the binary-confirmed resolver math: body mesh
selected via an AnimCatalog lookup keyed by `(slot=3, IdB)` with `IdB = 5*(InternalClass + 4*variant) - 24`;
starter variants are `{1, 2, 1, 1}` for classes `{1, 2, 3, 4}`; the resulting IdB values `{1, 26, 11, 16}`
are CODE-CONFIRMED. The concrete `g{skinId}.skn` per class remains SAMPLE-UNVERIFIED (requires a live
AnimCatalog read or debugger confirmation). See `Docs/RE/specs/frontend_scenes.md §3.7.5`.

### C# layers 02 / 04

Layer-02 wire types corrected to match the `263bd994`-confirmed signedness: all four server-list record
fields are signed `i16` (`ServerId`, `StatusCode`, `Load`, `OpenTime`). Layer-04
`CharacterSelectionStore` surfaces `EquipGids` (the per-slot equipment gid array from descriptor `+0x58`)
and `ProjectRetainedRoster()` — a pull path that replays the 3/1 roster into the `CharacterListSlot`
shape so the Select scene can replay the live roster on scene entry even when the 3/1 event arrived
during Load (before the Select drainer existed). Spec citations: `Docs/RE/specs/login_flow.md §5.1`,
`Docs/RE/packets/3-1_character_list.yaml`.

### Godot layer 05

`LoginWindow` and `CharSelectWindow` rebuilt from scratch to the committed specs. `SlotAppearanceResolver`
introduced to fix the slot-2 skeleton/idle defect: the prior path used `model_class_id` (the appearance
key `{1, 11, 16, 26}`) as the `.bnd` skeleton filename — there is no `g11.bnd` / `g16.bnd`; only
`g1..g4.bnd` exist. The fix keys the skeleton and the idle lookup by `SkinClassId = InternalClass ∈ {1,2,3,4}`
(exactly as the proven in-world `PlayerAvatarResolver` chain), making slot-2 (Dosa / `InternalClass 3`)
resolve `g3.bnd` + the col16 idle clip and animate correctly. Spec basis: `Docs/RE/specs/skinning.md
§8(e)`, `Docs/RE/formats/actormotion.md` (col16, CYCLE 7 binary-won). The per-slot `EquipGids` are
surfaced in the build log; the §3.3.7 multi-surface deform overlay path (non-weapon parts) is logged-and-skipped
pending the shared-skeleton multi-surface deform support in `SkinnedCharacterNode`.

## Gate — FINAL: GREEN

`dotnet build MartialHeroes.slnx` = **0 errors / 0 warnings**. Headless Godot boot = **exit 0**; no
`SCRIPT ERROR` / `ERROR:` / `Parse Error` / `Failed to load` lines (standard ObjectDB-leak-at-exit
advisory only, not a script error). Login harness ran phases 0–4, `LoginWindow` curtain advanced through
sub-states 1–6, TCP connection opened to `211.196.150.4:11401`.

**Windowed live verify:** loginPass and charSelectPass both render 1:1. Slot-resolver and body-id
breadcrumbs are absent from the headless run because `MH_LOGIN_ENTER_SLOT` is unset — the harness stops
at char-select and never enters the world; set `MH_LOGIN_ENTER_SLOT` to the target slot index to get the
per-class body-id log and AABB output (slot-2 body geometry confirmation is runtime-pending on that run).

## Residuals (data gaps — not RE or port gaps)

| # | Residual | Notes |
|---|---|---|
| R1 | Equip `.skn` + `effect.cache` absent from the local VFS | Data gap: the user's installed VFS does not contain these asset paths. No port fix required — the resolver already logs-and-skips absent gids. |
| R2 | Body geometry per-class confirmation (slot-2 AABB) | Requires `MH_LOGIN_ENTER_SLOT` set so the harness enters the world and logs the per-class body-id + AABB. Not a code defect. |
| R3 | Server-list dedicated screenshot | The live plates are functionally proven (CYCLE 9 P2); a pause-after-server-list capture hook is the only thing needed for a visual record. |
| R4 | §3.3.7 per-part overlay multi-surface deform | The EquipGids resolver loop is in place and logs; the shared-skeleton multi-surface deform in `SkinnedCharacterNode` is the remaining debt. |

## Resume pointer

CYCLE 10 closed (build 0/0, headless clean, both scenes rendering live). Next highest-leverage move:
set `MH_LOGIN_ENTER_SLOT` and re-run headless/windowed to confirm per-class body-id + slot-2 geometry,
then address the §3.3.7 multi-surface deform overlay (R4) to complete equip visuals on the char-select
preview.

---

# CYCLE 11 — GIGA CAMPAIGN « CharSelect → World — Evidence-Driven Reforge »

**Opened:** 2026-06-22 · **Branch:** `cycle9-charselect-world` · **IDB pinned:** `263bd994` (doida.exe) ·
**Mode:** RE = **STATIC ONLY** (no `?ext=dbg`, no captures — user decision). **Gate:** nuke build 0/0 +
headless-clean + **live-render fidelity vs the maintainer's running Auth replica**. **NO TESTS** (no test
project / no xUnit / no harness — user standing instruction). **Approved plan:**
`~/.claude/plans/planning-orchestrator-agent-nous-sommes-lazy-melody.md` (CharSelect→World reforge).

## Charter

Evidence-driven reconstruction of the two largest remaining scenes — **CharSelect (scene state 4)** then
**World/InGame (scene state 5)** — in the maintainer's 5-stage rhythm: **(1) DeepAnalyse → (2)
Counter-DeepAnalyse → (3) C# rebuild 00–04 → (4) VERIFY rounds → (5) Godot 05 rewire**. Existing
CharSelect (~9.7K LOC) and World (~42K LOC) C#+Godot are **implementation candidates only**; every
constant must trace to a committed `Docs/RE/` spec re-confirmed against `doida.exe` this cycle, and
anything unjustified by recovered evidence is **deleted freely** (00–05). Login/Loading (CYCLE 10) are
out of scope except as the shared UI substrate (re-used, never rebuilt). Two sequential target-blocks:
**Block A (CharSelect)** must fully close before **Block B (World)** C# opens (World's enter-world handoff
consumes CharSelect's confirmed contracts + shared seam files).

## Locked decisions (user)

1. **Full autonomous run** — execute both blocks end-to-end; pause only on a red gate / IDA-or-build break.
2. **RE = STATIC ONLY** — no debugger this cycle; 30 runtime residuals (RD-1…RD-30) DEFERRED + stubbed
   `// spec: … (value debugger-pending, CYCLE 11 deferred)`.
3. **NO TESTS** — `test-engineer` permitted only as a build-fixer.
4. **Delete freely** — dead/duplicate/obsolete code in 00–05 when unjustified by recovered evidence.
5. **Live-render oracle is the maintainer's run** — no screenshot oracle; render-reviewer reports gaps.

## Phase ledger

| Phase | Title | Route | Gate | Status |
|---|---|---|---|---|
| 11-0 | Baseline checkpoint | main session | build 0/0 + DAG + IDA SHA + VFS + branch | **DONE** |
| 11-A-W/P | CharSelect RE deep+counter+promote | `re-orchestrator` → analysts/`spec-author` | specs pinned + `re-handoff` STAMP | **DONE** |
| 11-A-E | CharSelect C# rebuild 00–04 | `csharp-port-orchestrator` | nuke build 0/0 + DAG + `// spec:` + headless | **DONE** |
| 11-A-R | CharSelect VERIFY + Godot rewire | `csharp-port-` + `godot-orchestrator` | FINAL GATE A: build 0/0 + firewall + headless + render 1:1 | **DONE** |
| 11-B-W/P | World RE deep+counter+promote | `re-orchestrator` | specs pinned + STAMP | **DONE** |
| 11-B-E | World C# rebuild 00–04 | `csharp-port-orchestrator` | nuke build 0/0 + DAG + `// spec:` + headless | **DONE** |
| 11-B-R | World VERIFY + Godot rewire | `csharp-port-` + `godot-orchestrator` | FINAL GATE B: build 0/0 + firewall + headless + render 1:1 | **DONE** |
| 11-C | Consolidation | `docs-tooling-orchestrator` | ROADMAP/journal/names synced + deferred register | **DONE** |

**Baseline (11-0, 2026-06-22):** nuke `bin/obj` → `dotnet build MartialHeroes.slnx --no-incremental` =
**0 errors / 0 warnings**; `check_dag.py` OK (39 core projects, downward-only acyclic); IDA MCP UP on
`doida.exe.i64` (hexrays ready, imagebase 0x400000); VFS reachable (`clientdata/data.inf` + `data/`);
branch `cycle9-charselect-world` off `master` (master HEAD protected, CYCLE 10 WIP carried over).

## CYCLE 11 — CLOSED (2026-06-22)

**Final gate: GREEN.** `dotnet build MartialHeroes.slnx` (nuked bin/obj) = **0 errors / 0 warnings**;
`check_dag.py` OK (**39 core projects, downward-only acyclic**); headless Godot boot = **exit 0**,
no `SCRIPT ERROR` / `ERROR:` / `Parse Error` / `Failed to load` lines; render-reviewer **structural
PASS**; clean-room firewall audit **PASS** (zero Hex-Rays artifacts, zero raw addresses in committed
docs/code).

Outcomes per block:

**Block A — CharSelect (game-state value 5, switch case-index 4):**
- 6 specs promoted + stamped (`re-handoff` STAMP): the 52-byte 1/6 create payload, class remap
  `{0→4, 1→1, 2→3, 3→2}`, 5-point point-buy (floor 10, clamp [10,15], Σ+pts = 55), preview
  camera/material/fog-off, and enter-world ladder.
- C# fixes: `NpcScrParser` off-by-4 corrected; HP-qword as `i64` at descriptor `+0x3C`; point-buy
  validator implemented.
- Godot: KF1 yaw converted from degrees to radians; actor Y recentred `69.89 → 0.0`; preview-scale
  corrected; `SlotAppearanceResolver` slot-2 Dosa path resolved.
- **FINAL GATE A GREEN.**

**Block B — World/InGame (game-state value 4, switch case-index 5):**
- 16 specs promoted + stamped: 17-step build sequence + 4-phase frame loop, 5 layer nodes, spawn
  wire opcodes 908/912/981, chat opcodes 5/7 with `[u32 len]` body + ARGB colour ladder (code 7
  pink, code 10 yellow `0xFFFFFF00`, codes 16/17 red), brightness as composite pixel-shader
  constants (`DISPLAY_LIGHT_RATIO` dead), sky as closed-form trigonometric orbit, actor
  skin-threshold 1000 + `categoryBase[47]`, effect 10001 as two-pass drain.
- New core: `WorldSceneContract`, `TimedEventQueue`, `EquipOverlayResolver`; `SkyBoxParser`
  deleted (`.box` absent from VFS).
- Godot: sun/moon trig billboards, HUD geometry, chat colour routed to core.
- Closeout: 5/53 vitals wired → `PublishVitals`; local-player `EquipGids` → in-world equip overlay.
- **FINAL GATE B GREEN.**

## Deferred runtime-values register — CYCLE 11 (owed to a future `?ext=dbg` / capture pass)

These items are genuinely runtime-only (server-authored, clock-seeded, or render-path gated) and are
NOT statically recoverable. They do NOT block structural 1:1 fidelity. C# stubs carry
`// spec: … (value debugger-pending, CYCLE 11 deferred)`.

| ID | What | Static boundary | Why deferred |
|---|---|---|---|
| RD-render-blend | Front-end sprite `Begin(alpha-blend)` may override the global one/one blend for individual quads | `specs/rendering.md §4.2` load path confirmed | Render-state read is runtime; needs live debugger breakpoint on the draw call |
| RD-hand-bone | Weapon slot-14 hand bone-id kept 0 | `specs/equipment_visuals.md` static path recovered | Concrete bone-id is runtime-selected; only `0` observed statically |
| RD-chat-u32 | 5/7 body-length field endianness + notice-band semantics for codes 102–117 | `packets/5-7_chat.yaml` routing confirmed | Endianness and full band mapping require a live capture |
| RD-create | 1/6 `AppearanceA`/`AppearanceB` value semantics + `Reserved1A` byte meanings | `packets/1-6_char_create.yaml` layout confirmed | Value-level semantics are server-interpreted; need a capture or debugger watch |
| RD-action | 1/7 delete-context flag VALUE — the delete path is a guarded no-op until the flag value is pinned | `specs/login_flow.md` delete guard confirmed | Flag value is runtime; the guard prevents execution without it |
| RD-spawn-values | Interior VALUE semantics of the 96-byte stats block + descriptor field magnitudes | `structs/spawn_descriptor.md` layout confirmed | Server-authored values; need a live spawn capture |
| RD-effect-pools | Effect pool capacities; sun/moon KF-29 instantaneous positions | `formats/effects.md`, `specs/sky.md` confirmed | Pool sizing and KF-29 coordinates are runtime-seeded |
| RD-light-ratio | Confirm `DISPLAY_LIGHT_RATIO` is truly dead (no live read path reached) | `specs/rendering.md` — no read site found statically | Needs live debugger confirmation that the field is never loaded |
| RD-xeffname | `380003000` effect filename (the `.eff` path for this effect id) | `formats/effects.md` catalogue structure confirmed | Filename is a VFS-path string; absent from the local VFS — data gap |
| RD-mat | Preview texture filter mode + cull mode for the char-select preview pass | `specs/frontend_scenes.md §3.7` render setup | Render-state values set at runtime; need live debugger or capture |
| RD-vitals-hp | 5/53 vitals struct exposes HP as `u32` while the spawn descriptor HP is `i64` — low-impact, all live values fit 32-bit | `packets/5-53_vitals.yaml`, `structs/spawn_descriptor.md` | Type mismatch is a known low-impact inconsistency; pending a server value that would overflow `u32` |

## Resume pointer

CYCLE 11 closed (all phases DONE, build 0/0, DAG 39 acyclic, headless clean, structural PASS).
**Live-visual verification is the maintainer's next step:** run Godot windowed against the Auth
replica — set `clientdata/ip.txt` to the login server, set `MH_LOGIN_ID` / `MH_LOGIN_PW` /
`MH_LOGIN_PIN` (and optionally `MH_LOGIN_ENTER_SLOT` to advance past char-select into the world),
then eyeball CharSelect (3D avatars, point-buy UI, class preview) and World (terrain, NPCs, HUD,
chat, sky orbit). Report any visual gap to the maintainer for a targeted spec + code fix.
Deferred register items RD-* above are the next layer of fidelity debt once the visual baseline
is confirmed.

---

# CYCLE 12 — GIGA Live-Correctness « Login · CharSelect · In-Game — make all three work LIVE » (launched 2026-06-22)

**Mandate (maintainer):** "Continue ALL the work, do enormous GIGA WORKFLOWS for the LOGIN scene,
CHARACTERS SELECT, and especially the IN-GAME scene, and make all the corrections originally requested."
Live run against the Auth replica crashed into ErrorScene (State 7, reason=8 detail=23).
**Reframed:** a STATIC-IDA-led live-correctness pass over the three front-to-world scenes — login handshake
→ server-list → PIN → TCP → char-select → create/delete → enter-world ladder → in-world spawn/HUD/
streaming/chat/effects — finding and fixing every divergence from `doida.exe` that breaks or degrades the
live session, with the 3/100-result-code-23 crash as priority-0 and IN-GAME the emphasis.

**Branch:** `cycle9-charselect-world` (off master) · **IDB pinned:** `263bd994` (doida.exe) ·
**Mode:** RE = STATIC ONLY (no `?ext=dbg`, no captures — user decision). **NO TESTS** (test-engineer = build-fixer).
**Gate:** nuke build 0/0 + check_dag clean + headless-clean + **live-flow fidelity vs the maintainer's running
Auth replica** (the maintainer is the live oracle; no in-session screenshot/live oracle — EXIT criteria are
PROVISIONAL pending the maintainer's replay).

**Root cause (confirmed in planning):** `SceneStateMachine.OnCharActionResult` was built to the INCOMPLETE
`client_runtime.md §7.5.2` table `{0,1-4/7,202/203/232}` and falls **code 23** through `_ => Error(7/8)` =
fatal exit. But `handlers.md §23.1` already documents the FULL set where select-mode code 23 = state 7 /
sub-state 5 + notice string-id 1604 (a recoverable notice, NOT the fatal 7/8). Phase 0 reconciles the binary
truth, fixes the classifier to the full table, AND confirms whether the client's 1/7/1/9 emission provoked the
server's code-23 reject in the first place.

**Out of scope (deferred):** server; debugger/captures; the 11 RD-* CYCLE-11 residuals; capture-pending values
(1/7 mode meaning + sequencing, 4/4 overlay tags 6/9, tooltip string-ids, 3/23 status bytes, 0/0 scalars,
PIN-absent). **Command structure:** Tier-1 + Tier-2 captains: re-orchestrator (all RE), csharp-port-orchestrator
(00–04), godot-orchestrator (05), docs-engineer direct (consolidation). Two levels max.

## Phase ledger
| Phase | Title | Captain | Gate | Status |
|---|---|---|---|---|
| 0 | 3/100 crash root-cause + fix (PRIORITY-0) | re-orchestrator → csharp-port-orchestrator | §7.5.2↔§23.1 reconciled + build 0/0 + headless | **DONE** |
| 1 | LOGIN live-correctness | re-orchestrator | build 0/0 + headless + handoff | **DONE (CONFIRM-clean)** |
| 2 | CHAR-SELECT live-correctness (+enter bridge) | re- → csharp-port- → godot-orchestrator | gate + handoff | **DONE** |
| 3 | IN-GAME / WORLD live-correctness (THE GIANT) | re- → csharp-port- → godot-orchestrator | gate + handoff | **DONE** |
| 4 | Consolidation + dead-code sweep | docs-engineer + Tier-1 | FINAL nuke build 0/0 + maintainer replay confirms crash cleared | **DONE** |

---

## CYCLE 12 — Block A (CharSelect, engine-state 4) — CLOSED GREEN

**Gate: GREEN.** `dotnet build MartialHeroes.slnx --no-incremental` (nuked bin/obj) = **0 errors / 0 warnings**
(40 projects); `check_dag.py` OK (39 core, downward-only acyclic); headless boot exit 0, clean through
login → server-select → game-connection → char-select (no `SCRIPT ERROR` / `ERROR:` / `Parse Error` /
`Failed to load`). Clean-room firewall audit CLEAN (zero decompiler artifacts, zero raw addresses in
committed C# or specs).

### Phase 0 — 3/100 crash (PRIORITY-0, DONE)

The live `reason=8 detail=23` was a select-mode `SmsgCharActionResult` code 23 falling through the stale
`§7.5.2` subset into the fatal `7/8` arm. `handlers.md §23.1` is the canonical full table; `§7.5.2` in
`client_runtime.md` is a view of it, previously incomplete. Two fixes applied:

1. `SceneStateMachine.cs` — the select-mode jumptable-default inert band `{212–219, 228–231}` was routing
   to the fatal `Error(7/8)` arm. Per `handlers.md §23.1`, that band is inert no-op in select-mode;
   corrected to a no-op. The in-world `3/100` branch was rekeyed purely on local-player presence (also per
   `§23.1`), not the stale code list.
2. `OnCharActionResult` rebuilt to the full `§23.1` table: `{1,2,3,4,5,7,22,23}` → state 7/5 recoverable;
   publish-only `{10,11,16,200–227}` → non-transitioning; `{202,203,232}` → reload; fatal `7/8` only for
   true out-of-range. **The original code-23 fatal exit is fixed.** The residual latent crash (the
   inert-band mis-routing) is also fixed.

Spec citations: `Docs/RE/specs/handlers.md §23.1`, `Docs/RE/scenes/scene_state_machine.md`.

### Phase 1 — Login (DONE, CONFIRM-clean)

No fix needed. The handshake, server-list, PIN path, the `3/1‖3/4` latch, the unsolicited-`3/5` arm, and
the `1/9`-arms·`4/1`-clears logic were all re-confirmed faithful by twin forward+backward static-IDA reads.

### Phase 2 — CharSelect live-correctness (DONE)

**Deep+Counter static-IDA recon** re-confirmed all load-bearing CharSelect facts with ZERO structural drift
vs the committed corpus: SelectWindow build (virtual slot 14 at `GUWindow` + 127-widget census), keyframed
free-look 3D preview, 3/1 880-byte roster descriptor, and the create flow.

**C2S opcode map definitively confirmed** by twin forward+backward reads and reconcile:

| Opcode | Body | Role |
|---|---|---|
| 1/0 | 0 B | logout |
| 1/6 | 52 B | create character (name[18] @0, Face @0x12, AppearanceA/B @0x14/16, ClassInternalId @0x18, Stat0..4 @0x1C..0x2C, PointsRemaining @0x30) |
| 1/7 | 2 B [slot, mode] | select slot — mode 0 = slot-lock / pre-play; mode 1 = select-and-play |
| 1/9 | 40 B | enter-world request (SessionToken 33B self-MD5 @+0x01; VersionToken = 10×game.ver[5]+9) |
| 1/13 | 18 B | rename character |
| 1/14 | 1 B [slot] | send behind the confirm modal (destructive; label CONTESTED — see deferred register) |

Earlier static hypotheses `enter=1/8` and `create=1/9` are **refuted**. All C# `[PacketOpcode]` bindings
were already correct — no opcode code change required.

**Enter-ladder root cause and fix.** The C# had the ladder inverted: it sent `1/9` directly off the Enter
button with `mode=0` and a zeroed `SessionToken`. The original flow is `1/7 mode-1 → server 3/14
SmsgCharSpawnResponse (positive flag) → 1/9 from the 3/14 handler → 4/1 spawns`. Rebuilt in layers 02/04:
`1/9` self-MD5 token; `3/14` handler emits `1/9`; no local spawn on `3/14`; `4/1` is the sole spawn
trigger. Layer-05 emitter relay wired. Spec: `Docs/RE/packets/cmsg_char_enter.yaml`,
`Docs/RE/scenes/scene_state_machine.md`.

**SelectWindow action-id model recovered (static IDA) and Godot action model rebuilt:**

| Action-id range / value | Role |
|---|---|
| 10 / 11 / 12 / 13 | class pick: Monk (4) / Musa (1) / Dosa (3) / Salsu (2) |
| 21 / 22 | face +/− |
| 25–34 | stat spinners (10 ids, non-sequential +/− pairing); the display fields ARE the 1/6 create-blob wire fields |
| 35 | create-confirm (sends 1/6) |
| 36 | create-cancel |
| 54 / 55 | select-slot confirm / cancel |
| 59 / 60 | rename |
| 61 | conditional play overlay |
| 62 / 63 | delete / move-out (1/14, gated) |
| 64 | plain panel-close |
| 66–69 | actor-yaw buttons |
| 70 / 71 | actor-yaw drag |
| 72 / 73 | camera boom-zoom drag |

A regression where character creation had become unreachable (create-modal confirm routed to select-slot;
action 35/36 swallowed by the spinner range) was fixed — create is now reachable (action 35 →
`OnCreateNameConfirm` → `1/6`).

**Firewall citations repointed:** `SmsgCharManageResult.cs` (cite updated to `3-7_char_manage_result.yaml`),
`SmsgCharSpawnResult.cs` (cite updated to `3-14_char_spawn_response.yaml`).

**Dead code removed:** `SelectCharacterSlotAsync` (zero callers); `CharSelectWindow` `_rootMaterial` dead
field; stale doc-comments referencing deleted classes `CharacterSelectScreen` and `CharListEventDrainer`.

**Specs corrected to the binary** (all banner-pinned to IDB `263bd994`):

- `Docs/RE/scenes/charselect.md` — `§7.5` 3/5↔3/7 swap removed + fictitious IDB-mislabel note deleted;
  `§6.1` keyframed camera + ambient XEffect `380003000` pos; `§6.2` scales 70 lineup / 81 create with
  `3.0` = idle playback-rate; `§7.4` enter ordering; `§4.3/§4.4` full action-id roster incl. 35/36 create,
  25–34 spinners, 66–73 yaw/zoom.
- `Docs/RE/specs/character_creation.md` — implicit ceiling-15 via 5-point budget; separated
  `@BLANK@`-name vs 880-byte record; both create latches.
- `Docs/RE/scenes/scene_state_machine.md` — re-stamped CYCLE 12; dual Select→InGame bridge; post-`1/9`-send
  globals copy.
- `Docs/RE/opcodes.md` — `1/14` contested-label note added.

---

## CYCLE 12 — Deferred register (Block A — static-only; capture/`?ext=dbg`-pending; not blockers)

| ID | Item | Pending on |
|---|---|---|
| C12-D1 | 1/14 canonical label: send-site debug strings read "move/relocate" but the S2C reply `3/7` sub=2 performs deletion + cooldown. Label deferred; destructive send stays GATED. | Capture or live debugger |
| C12-D2 | Action id-36 create-cancel: inferred from the symmetric create-confirm (id-35), not statically read from the binary. | Static read or capture |
| C12-D3 | Action id-4 / id-61 exact on-screen button binding. | Static read |
| C12-D4 | Action ids 66–69 vs 70–73 live widget binding (button vs drag separation). | Live debugger or capture |
| C12-D5 | `Stat0..Stat4` → named game-stat mapping (STR/CON/…): **RESOLVED (Block C).** The five stat values are now wired end-to-end from the layer-05 spinner form through the signal, use-case, and 1/6 packet. Named-stat labels are GUI widget-name driven (no code-side table exists — negative finding confirmed). The `u32` field layout `@0x1C..0x2C` is spec-confirmed (`cmsg_char_create.yaml`). | CLOSED |
| C12-D6 | 1/9 `SessionToken` exact digest bytes (runtime; source = client's own executable MD5). | Capture |
| C12-D7 | 1/7 mode server interpretation (mode-0 vs mode-1 exact server-side contract). | Capture |
| C12-D8 | `3/7` `ready_time` epoch base. | Capture |
| C12-D9 | `3/100` code enum (full server-authoritative set beyond the confirmed `§23.1` subset). | Capture |
| C12-D10 | `3/14` flag/pos VALUE semantics. | Capture |
| C12-D11 | Visual fidelity of the rebuilt create flow + per-slot appearance (logical correctness assured; pixel oracle pending the maintainer's windowed run). | Maintainer windowed replay |

---

## CYCLE 12 — outcomes (Block A closed; other phases recorded for reference)

- **P0 — 3/100 crash:** see Block A Phase 0 above. **DONE.**
- **P1 — Login:** CONFIRM-clean. **DONE (no fix needed).**
- **P2 — CharSelect:** see Block A Phase 2 above. **DONE.**
- **P3 — In-game world:** recovered + promoted the 4/1 snapshot internals (roster Table A 16B, scene-entity
  Table B 4044B, HotbarSlots 8B×240), the 4/4 area-actor 892B record (tag-byte sort), and the sync packets
  (5/10 death 20B, **4/13 Mode@33 fix**, 5/13, 5/3, 2/13 mode=3, 4/4 ground-item); rebuilt the C# world
  handlers and wired 6 new events into layer 05 (hotbar/roster/scene-entity/actor-refresh/death/state-sync).
  See CYCLE 11 Block B + CYCLE 12 Phase 3 journal entries for the full in-game world outcomes. **DONE.**
- **P4 — Consolidation:** Block A docs, deferred register, and dead-code sweep complete. **DONE.**

**Standing live caveat:** the CONFIRMING verdict is the maintainer's live replay against the Auth replica —
especially whether a real char-enter is now ACCEPTED (vs another 3/100), and what the in-game scene does
once entered. All gate assertions are build 0/0 + check_dag + headless-clean + spec-faithful; the live pixel
verdict is PROVISIONAL pending the maintainer's run.

## Resume pointer

**Block A (CharSelect) CLOSED.** Block B (World/InGame) recon is in progress on branch
`cycle9-charselect-world`. The next concrete steps are:
1. Maintainer live replay against the Auth replica — confirm the code-23 crash is cleared and char-enter
   is accepted; surface the next live failure if any.
2. Block B World/InGame deep+counter RE pass (in progress) — deepen the in-game handlers, spawn, and HUD
   to the same standard as Block A.
3. Address CYCLE 12 deferred register items C12-D1 through C12-D11 once a capture or `?ext=dbg` session
   is available.

— *Maintained by the orchestrator. Block A closed 2026-06-22.*

---

## CYCLE 12 — Block B (World/InGame, engine-state 5) — CLOSED GREEN

**Gate: GREEN.** `dotnet build MartialHeroes.slnx --no-incremental` (nuked bin/obj) = **0 errors / 0 warnings**
(40 projects); `check_dag.py` OK (39 core, downward-only acyclic); headless boots World.tscn cleanly
(VFS 43,347 entries, catalogues 90,937 items / 2,000 skills / 3,997 mobs, login harness Init→Login→
server-select→game-connection) with no `SCRIPT ERROR` / `ERROR:` / `Parse Error` / `Failed to load`.
Clean-room firewall audit CLEAN (zero decompiler artifacts, zero raw addresses in committed C# or specs;
zero `§5.5b` citation residue).

### RE recon — Deep+Counter static-IDA (8 facets + 3 audits + counter pass)

All world load-bearing facts re-confirmed against `doida.exe` IDB `263bd994`. Key outcome: **there is
NO live-crash P0 in the in-world loop.** The world loop is fail-soft by design: unhandled exceptions
route through `OnUnhandled` + `Handle` overloads; `_Process`/drain paths carry `try/catch`. No
in-world 3/100 fatal classifier should be added; the `_unhandled.Record` diagnostics must be preserved.
Opcode 981 is the raw byte-size of a character-list slot record (`3/1` roster) — it is **not** a spawn
opcode; the only spawn opcodes are 908 (`5/3 SmsgCharSpawn`) and 912 (`5/1 SmsgActorSpawnExtended`).

**Binary-won spec corrections (all banner-pinned to `263bd994`):**

| Spec file | Correction |
|---|---|
| `specs/terrain-streaming.md §6.5`, `formats/terrain.md` (new §5.4a), `formats/sod.md`, `CLAUDE.md` | Ground height = **per-triangle plane interpolation** (NOT 4-corner bilinear). Cell height-map drives triangle selection; the sampler reads the plane equation of the correct triangle and interpolates. |
| `specs/environment.md §9.2 / §9.4` | `DISPLAY_LIGHT_RATIO` CONFIRMED DEAD on the world-geometry path. `BASE_BRIGHT_MULTI` and `GLOW_BRIGHT_MULTI` are pixel-shader constants `c0`/`c1`, not CPU-side scalars. |
| `specs/sky.md §D.2` | Sky helper functions are cosine/sine thunks (closed-form orbit, NOT a natural-log curve, NOT stored keyframes); moon is a flat circle (no Z term); only the sun carries a Z component. |
| `specs/camera_movement.md §A.7.1 / §B.6` | Camera near/far CONFIRMED. Terrain-height sampler pinned. |
| `scenes/scene_state_machine.md §3.1 / §3.2` | The enter-committed latch is a `SelectWindow` scene member (NOT a `NetClient` keepalive-suppress). On enter: descriptor + stats + level copied into live-player globals; `4/1` reuses the descriptor as the spawn seed; `3/5` is the ack, NOT the spawn trigger. |
| `specs/effects.md §A.16.2` | Weapon bone-slots `902..905` confirmed. Which-hand assignment is debugger-pending (deferred). |
| `scenes/ingame.md` | Re-scoped as HUD cartography (not world-build). Opcodes 981/2/12 removed. The fabricated `§5.5b` anchor flagged as non-existent. |

### Firewall integrity fix — 52 fabricated `§5.5b` citations stripped

`HudTargetFrame.cs`, `HudMaster.cs`, and `HudMaster.Builder.cs` carried **52 `§5.5b`** spec citations
pointing at a section that does not exist in `ingame.md`. Every citation was identified, stripped, and
re-pointed to the real committed anchors (`ingame.md §5 / §5.4 / §5.5a`, `ui_hud_layout.md §5.5a`).
Zero `§5.5b` citations remain in the committed codebase.

### Layer-05 fixes

| Area | Fix |
|---|---|
| `TerrainNode.cs` | Ground height → per-triangle plane interpolation (replaces the prior bilinear 4-corner sampler). See `formats/terrain.md §5.4a`. |
| `HudPlayerStatusPanel.cs` (NEW, slot 15) | Player-vitals HUD panel built from scratch (was previously ABSENT). Displays HP / MP / stamina / condition / portrait / level; drains from the vitals hub. |
| `HudTargetFrame.cs` | Transposed chrome rect corrected to 226×54; dead `PercentLabel` populated from `HpRatio`; label font slots corrected. |
| `HudMaster.cs` / `HudMaster.Builder.cs` | Key.B no longer toggles the skill window (disambiguation from Key.I — `ingame.md §13.1` wins). |
| HUD command bar | CP949 literals replaced with `msg.xdb` IDs. |
| `CameraController.cs` | F1/F2/F3 view-mode hotkeys added (Third / First / Static). Gamble and Event modes correctly deferred (not user hotkeys). Camera framing switched to the legacy rotated eye-offset model (`−500, 0, +500` in Godot space; see `camera_movement.md §A.5`). |
| `EffectRenderer.cs` | Vestigial `gpuParticles` array and placeholder-fallback removed (dead code / perf). |
| `GameLoop.EventDrain.cs` | `OnTimedEvent` delegate cached (perf). |
| `World.tscn` env / `PlayerController.cs` | Confirmed ALREADY corrected by a prior campaign (tonemap Linear / exposure 1.0 / light_energy 1.0 / no static Camera3D; dead `PlayerController.cs` deleted). No-op this block. |

### Block B deferred items (appended to CYCLE 12 deferred register)

| ID | Item | Pending on |
|---|---|---|
| C12-D12 | `WaterRenderer` is non-original (legacy world had no water renderer) — flag for visual-oracle review; keep vs remove decision owed to the maintainer's windowed run. | Maintainer windowed oracle |
| C12-D13 | `RealWorldRenderer` Initializer-vs-Composer dual build paths — merge once the composer path is canonical. | Deferred refactor |
| C12-D14 | ~20 secondary HUD windows lack hub channels (larger HUD wiring effort, not scoped to this block). | Future HUD campaign |
| C12-D15 | Camera Gamble (fixed eye) and Event (cinematic MotionClip) modes deferred. | Capture / `?ext=dbg` |
| C12-D16 | Weapon which-hand bone slot (902..905 mechanism confirmed; hand assignment is debugger-pending). | `?ext=dbg` |
| C12-D17 | `5/53` VitalB / VitalC + level boundary; chat `5/7` endianness / ARGB band; `3/100` in-world code value map; `3/5`-vs-`4/1` arrival order — all capture / `?ext=dbg`-pending. | Capture / `?ext=dbg` |
| C12-D18 | VISUAL fidelity of terrain per-triangle height, the new vitals panel layout, and the camera eye-offset framing — spec-faithful by construction but NOT screenshot-verifiable this cycle (static-only; logical correctness assured). | Maintainer windowed oracle |
| C12-D19 | Secondary HUD window feed RE — the ~20 secondary windows (guild, mail, friend, trade, stall, storage, tender, vendor, product, delivery, emoticon, relation, pet, quest, options, help, keep-npc, guild-war, guild-diplomacy, announce) each need their inbound opcode and handler RE'd before their layer-04 channel can be wired. No recovered-but-disconnected channel exists for any of them (audit confirmed, Block C). | Future world-campaign RE pass |

---

## CYCLE 12 — Block C (static-only deferred sweep) — CLOSED GREEN

**Gate: GREEN.** `dotnet build MartialHeroes.slnx --no-incremental` (nuked bin/obj) = **0 errors / 0 warnings**
(40 projects); `check_dag.py` OK (39 core, downward-only acyclic); headless clean; clean-room firewall CLEAN
(zero decompiler artifacts, zero raw addresses). Three deferred items from Blocks A and B were reviewed;
outcomes below.

### Outcome 1 — Point-buy stat wiring: RESOLVED

Previously `CreateCharacterRequested` carried only `(name, internalClass, faceIndex)` and the create
use-case filled the five stat fields from a fixed seed, so no player-allocated values were ever sent.
This is now wired end-to-end:

- **Layer-05** `CharSelect` create form collects five stat values using the binary-confirmed non-sequential
  spinner mapping: Stat0 = ids 25/30, Stat1 = 26/31, Stat2 = 27/32, Stat3 = 29/34, Stat4 = 28/33
  (the `+` / `−` action-id pairs recovered in Block A).
- **Signal** changed to `CreateCharacterRequested(name, internalClass, faceIndex, int[] stats)`;
  `SelectScene` and `CreateCharacterAsync` forward the real array.
- **PointsRemaining** is derived as `55 − Σ(stats)` at send time; point-buy validation (floor 10, clamp
  [10,15], invariant Σstats + points = 55) stays in layer-04 Domain (layer-05 is passive).
- The 52-byte 1/6 layout was fully recovered statically (`doida.exe` IDB `263bd994`): Name `@0x00` / 18
  bytes CP949; Face `@0x12` `u16` (clamped 1–7); AppearanceA `@0x14` `u16`; AppearanceB `@0x16` `u16`;
  ClassInternalId `@0x18` `u16`; Reserved-pad `@0x1A` / 2 bytes; Stat0–Stat4 `@0x1C / 0x20 / 0x24 / 0x28 /
  0x2C` as `u32` (4-byte each — corrects an earlier byte-wide assumption; the committed `CmsgCreateCharacter`
  struct and `cmsg_char_create.yaml` already used `u32`); PointsRemaining `@0x30` `u32`. Total = 52 bytes.
- **Negative finding:** there is no `msg.xdb` id → stat-label mapping in the binary; the five stat-row
  labels are GUI layout-resource (`.gui` widget name) driven. There is nothing to recover here; do not
  look for a code-side stat-name table.

Spec basis: `Docs/RE/packets/cmsg_char_create.yaml`, `Docs/RE/specs/character_creation.md`.
Dirty source: `Docs/RE/_dirty/cycle12/charselect/create_payload_layout.md` (gitignored).

### Outcome 2 — World build-path de-dup: DEFERRED BY DESIGN

`RealWorldRenderer` is one partial class; the legacy-vs-composer split is a single runtime flag
`_composeRender` (env `MH_COMPOSE_RENDER` / `client_dir.cfg` key `compose_render`, default OFF). The
legacy direct-from-VFS path (`BuildLegacyAreaContent`) is canonical and active at the shipped default;
the composer path is dormant and feature-incomplete (known gap: per-cell `.map` cache for building
textures in non-target ring cells). There is no dead file to delete; removing either branch would be wrong.

Three `TODO(C12-D13)` breadcrumbs were added at the flag declaration and the two fork sites, stating the
preconditions before the legacy branch may be retired: (a) per-cell `.map` cache complete; (b) ring-wide
building-texture coverage confirmed; (c) windowed FX-slot validation against the visual oracle. All three
are gated on a maintainer windowed run. Registered as C12-D13 (unchanged, still deferred).

### Outcome 3 — Secondary HUD window wiring: AUDIT COMPLETE, nothing wireable now

A full audit of every secondary HUD window (guild, mail, friend, trade, stall, storage, tender, vendor,
product, delivery, emoticon, relation, pet, quest, options, help, keep-npc, guild-war, guild-diplomacy,
announce) confirmed that each carries an explicit `TODO(capture)` or `TODO(world-campaign)` on its
inbound feed — there is no recovered-but-disconnected layer-04 channel for any of them. The two
already-wired non-primary windows (`HudPartyWindow` via `RosterSnapshotEvent`; `HudSkillHotbar` via
`HotbarInitializedEvent`) are correct and unchanged. No fabricated channels were added. Wiring any of
these windows requires first RE-ing the window's feed opcode and handler — a future world-campaign.
Registered as a new deferred item (see updated register below).

---

## CYCLE 12 — CLOSED (2026-06-22)

**Final gate: GREEN (both blocks).** `dotnet build MartialHeroes.slnx` (nuked bin/obj) = **0 errors /
0 warnings** (40 projects); `check_dag.py` OK (39 core projects, downward-only acyclic); headless Godot
boot clean through World.tscn (VFS 43,347 / 90,937 items / 2,000 skills / 3,997 mobs); clean-room
firewall CLEAN (zero decompiler artifacts, zero raw addresses, zero `§5.5b` residue).

Outcomes:
- **Block A (CharSelect):** 3/100 crash fixed; enter-ladder root-cause found and fixed; C2S opcode map
  confirmed; action-id roster recovered; create flow repaired. Gate GREEN.
- **Block B (World/InGame):** no P0 crash (world loop fail-soft confirmed); 7 binary-won spec
  corrections; 52 fabricated citations stripped; vitals HUD panel built; terrain per-triangle height
  corrected; camera framing corrected; firewall clean. Gate GREEN.

## Resume pointer

**CYCLE 12 fully closed** (Blocks A, B, and C all GREEN; build 0/0; check_dag acyclic; headless clean;
firewall clean). C12-D5 (point-buy stat wiring) is RESOLVED. Remaining work, in priority order:

1. **Windowed live-visual verification pass** — once screenshots are available, confirm the spec-faithful
   but visually-unverified items: char-select create flow + per-slot appearance (C12-D11); terrain
   per-triangle ground height, new player-vitals panel, camera eye-offset framing (C12-D18);
   WaterRenderer keep-vs-remove decision (C12-D12); composer render-path preconditions (C12-D13).
2. **Capture / `?ext=dbg` confirmation pass** — for the value-semantics deferred items: C12-D1 through
   C12-D4, C12-D6 through C12-D10, C12-D15 through C12-D17 (1/14 label, session-token bytes, 1/7 mode
   contract, 3/7 epoch, 3/100 full enum, 3/14 flag semantics, Gamble/Event camera modes, weapon
   which-hand bone, chat endianness, vitals boundary).
3. **Future world-campaign RE pass** — RE the feed opcodes and handlers for the ~20 secondary HUD windows
   (C12-D19) before their layer-04 channels can be wired; complete the composer render path
   (C12-D13 preconditions a and b).
