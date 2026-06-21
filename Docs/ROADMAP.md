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
| 0 | Checkpoint & Baseline | main session | build 0-err + headless + checkpoint commit | **IN PROGRESS** |
| 1 | RE static — Truth pass | `re-orchestrator` | specs re-pinned + clean-room-check + deferred register | pending |
| 2 | C# core alignment to specs | `port-orchestrator` | nuke build 0-err + headless + code-reviewer | pending |
| 3 | C# cleanup & de-noising | `port-orchestrator` | nuke build 0-err + headless + clean-room-check + reviewer PASS | pending |
| 4 | Godot 05 surgical refactor | `port-orchestrator` | godot-build + headless + screenshot + render-reviewer | pending |
| 5 | Integration, verify & docs | main session | build 0-err + headless live + docs synced + commit | pending |

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

## Resume pointer

Phase 0 complete → **dispatch Phase 1 to `re-orchestrator`** (static-only Truth pass: 1A scene/Window FSM,
1B GUI/`::Gu`/`::Components`, 1C VFS assembly & lifecycle, 1D networking confirm, 1E IDB legibility). Then
Phases 2→3 (C# align→clean via `port-orchestrator`), Phase 4 (Godot surgical), Phase 5 (integration).
Gate every phase with: nuke `bin/obj` → `dotnet build` 0-err + headless boot.
