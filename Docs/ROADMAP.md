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
