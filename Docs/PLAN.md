# PLAN — CAMPAIGN 3 · `doida.exe`: Workflow · UI/UX · VFS — deep reverse → faithful client

> **This is the method/charter for the project's single active campaign.** It *specialises* the
> generic doctrine in [`CAMPAIGN_TEMPLATE.md`](CAMPAIGN_TEMPLATE.md) — read the template for the full
> hierarchy, the three command tiers, the concurrency ledger, the firewall, the standard
> W/P/E/T/R/C pipeline, and the ~50-agent fleet. This file does **not** repeat that doctrine; it
> records the campaign-specific decisions, the orchestrator→phase assignment, the cluster taxonomy,
> and the master deliverables. The live run record is [`ROADMAP.md`](ROADMAP.md).

---

## §0 — Charter

**Mission.** Bring the dead game (D.O. Online / *Martial Heroes*, 2003–2008) back to life as a
clean-room, fan-made revival. Two permanent north stars:

- **N1 — Reverse the original, live.** Drive the legacy 32-bit client `doida.exe` (and `Main.exe`)
  in IDA Pro 9.3 through the `ida` MCP, **static analysis** for the hypothesis and the **IDA
  debugger** (`?ext=dbg`) for ground-truth confirmation against the running client. Output is
  *understanding*, captured as neutral specs — never copied code.
- **N2 — Re-create it 1:1.** A faithful port of the client behaviour, wire protocol, asset chains
  and screens into the .NET 10 / C# 14 core + Godot 4.6.3-mono presentation layer.

**Legal basis.** EU Software Directive **2009/24/EC Art. 6** — decompilation is permitted solely to
achieve interoperability. The clean-room firewall (below, and `CAMPAIGN_TEMPLATE.md §4`) is the
mechanism that keeps the project inside that exception: dirty decompiler output never reaches shipped
code; only neutral specs cross.

**Where CAMPAIGN 3 stands on the recovered baseline.** The boot→login→PIN→char-select workflow and
the login/char-select UI are ~95% recovered; the VFS container and 16 format specs (23 extensions)
are confirmed. CAMPAIGN 3 closes the *remaining* gaps, deepens IDB legibility into the clusters
Campaign 2 deferred (UI/sound/combat/Lua/terrain), promotes everything to clean specs, then wires the
recovered behaviour into the client.

---

## §1 — Governing decisions

| # | Decision | Choice | Consequence |
|---|----------|--------|-------------|
| **G1** | Scope | **Reverse → Specs → Client (full pipeline)** | The campaign runs all the way from dirty recovery to wired C#/Godot — phases B/Dbg (recover), C (promote), D (annotate IDB), **E (engineer the client)**. |
| **G2** | History | **Fresh start** | `ROADMAP.md` tracks only CAMPAIGN 3. Prior Cycles 1–4 + Campaign 2 live in git history and `Docs/RE/journal.md` (the permanent provenance trail). |
| **G3** | Depth order | **Prioritised clusters** | Highest-leverage gaps first: workflow-spine (the 1/6 collision) → UI/HUD → VFS-internals → Lua → sound/combat/terrain. |
| **G4** | IDB lead | **CAMPAIGN 3 owns IDB naming** | It continues Campaign 2's annotation into new clusters and is the single source of truth for `names.yaml`. One IDB writer at a time. |

---

## §2 — The apparatus: orchestrator → phase assignment

Two orchestration levels max (Tier-1 main session → Tier-2 Orchestrator-Agent → Tier-3 worker). Each
phase below names its **Tier-2 owner**; the owner fans out the real Tier-3 workers from its roster
(see `.claude/KIT.md` for the rosters).

| Phase | Tier-2 owner(s) | Tier-3 workers fanned out |
|-------|-----------------|---------------------------|
| **0** Mandate & Pre-flight | *Tier-1 (main session)* | — |
| **A** Cartography refresh | `re-comprehension-orchestrator` (READONLY) | `re-static-analyst`, `ida-script-author` |
| **B** Deep comprehension | `re-comprehension-orchestrator` (READONLY, ≤3 IDA reads) | `re-static-` / `re-protocol-` / `re-crypto-` / `re-struct-cartographer` / `re-asset-format-` / `re-animation-analyst`, `ida-script-author` |
| **Dbg** Debugger confirmation | *Tier-1* via `/ida-debugger-drive` | — (drives the maintainer's live `?ext=dbg` session; **never** `dbg_start`) |
| **C** Reconciliation & Promotion **GATE** | *Tier-1* + `protocol-spec-author` + `asset-spec-author` | — |
| **D** IDA annotation (WRITE) | `re-annotation-orchestrator` | **one** `re-ida-annotator` at a time (serialized) |
| **E** Engineering wave | `network-stack-` + `assets-pipeline-` + `client-core-` + `godot-client-orchestrator` | their per-project engineers + `test-engineer` + reviewers |
| **T** Tooling (parallel) | `tooling-orchestrator` | `skill-author`, `agent-author`, `hook-author`, `tooling-auditor` |
| **R** Review & Hard Gates | `quality-gate-orchestrator` | `clean-room-auditor`, `architecture-guardian`, `csharp-reviewer`, `perf-reviewer`, `build-doctor`, `godot-render-reviewer`, `preservation-archivist`, `tooling-auditor` |
| **Z** Consolidation & Commit | *Tier-1* | `preservation-archivist` (pre-commit pass) |

The three Campaign-2 IDB agents (`re-comprehension-orchestrator`, `re-annotation-orchestrator`,
`re-ida-annotator`) and the `/ida-annotate-batch` skill are **reused as-is** for B and D.

---

## §3 — Firewall & concurrency invariants (specialised)

The full firewall and concurrency model is `CAMPAIGN_TEMPLATE.md §3–§4`. The bindings that matter for
this campaign:

- **Dirty → spec → clean.** Dirty-room analysts (B) write **only** under `Docs/RE/_dirty/` (gitignored,
  tainted). Spec-authors (C) **rewrite** — never copy — into the committed specs. Engineers (E) read
  only the clean specs. Every magic constant in C# cites `// spec: Docs/RE/...`.
- **Never paste pseudo-C.** No `sub_/loc_/_DWORD/__thiscall`/mangled names or image-range addresses in
  any committed file (this PLAN/ROADMAP included — clusters are referenced by canonical name; anchors
  live in `_dirty/` during Phase A).
- **IDA is one shared IDB.** Static reads fan out in **sub-waves of ≤3** READONLY analysts. **Writes
  are strictly serialized** — exactly one `re-ida-annotator` in flight in Phase D (G4).
- **Debugger discipline.** The maintainer F9-launches the client; the session connects on the
  debugger-extended endpoint `http://127.0.0.1:13337/mcp?ext=dbg`. **Never call `dbg_start`.** Runtime
  observations are dirty evidence promoted as *"verified under the IDA debugger"* (high confidence).
- **Tier-1-only shared files.** `Docs/RE/journal.md`, `Docs/RE/names.yaml`, `Docs/RE/opcodes.md`,
  `client_dir.cfg`, `.claude/settings.json`, `.mcp.json`, `CLAUDE.md` — never delegated to a worker.
- **One writer per path per wave** (the file-ownership ledger). Phase-E lanes are parallel only because
  their paths (layers 02/03/04/05) are disjoint.
- **Never commit originals** (`*.pak/*.vfs/*.exe/*.dll/*.pcapng/*.scr/*.mot/*.ted/*.bud/client *.png`,
  anything under `_dirty/` or `.godot/`). Index/metadata only.

---

## §4 — Phase pipeline

```
        ┌──────────────────────── T (tooling, parallel) ───────────────────────┐
0 ──► A ──► B ──► Dbg ──► C (HARD GATE) ──► D ──► E ──► R ──► Z (commit on request)
```

- **0 Mandate & Pre-flight** *(Tier-1)* — capture the mandate, green the tools (IDA `?ext=dbg`, build
  0/0, tests, VFS), record the `doida.exe` evidence baseline, confirm the single-writer rules.
- **A Cartography refresh** *(READONLY)* — re-map the spine, locate the new-cluster anchors.
- **B Deep comprehension** *(READONLY, ≤3 IDA)* — per-cluster dossiers + `names.proposed.yaml` +
  `comments.proposed.md` for lanes B1–B6 (§5).
- **Dbg Debugger confirmation** *(Tier-1)* — confirm the load-bearing hypotheses against the running
  client (the 1/6 routing, VFS open/read at a live pointer, login/PIN blob pre/post, HUD widget
  addresses, preview-camera matrices).
- **C Reconciliation & Promotion** *(HARD GATE)* — reconcile (`CONFLICT:` markers), gate the glossary,
  spec-authors rewrite into clean specs, firewall scan, `journal`+`names` update. **Nothing engineers
  until this passes.**
- **D IDA annotation** *(WRITE, serialized)* — apply the reconciled renames/comments/types into the IDB
  via `/ida-annotate-batch` (dry-run → apply), sync back to `names.yaml`.
- **E Engineering wave** *(parallel disjoint lanes)* — wire the recovered behaviour into layers 02–05.
- **T Tooling** *(parallel with A–E)* — deepen `vfs-inspect`, add the HUD-coordinate dump skill, refine
  the debugger-drive tooling.
- **R Review & Hard Gates** — `quality-gate-orchestrator` returns one PASS/FAIL.
- **Z Consolidation & Commit** *(Tier-1)* — update statuses, journal, names, memory; commit **only on
  explicit maintainer request**.

Default to **pipeline** (an item flows through stages without waiting for siblings); the only true
**barrier** is the Phase-C gate.

---

## §5 — Cluster taxonomy & priority (the B lanes)

Anchors are recovered in Phase A and held in `_dirty/` (address-free here, by firewall). Each lane is
one comprehension deliverable.

| Lane | Cluster | What it closes | Priority |
|------|---------|----------------|----------|
| **B1** | **workflow-spine-deep** | The boot→login→server-list→PIN→char-select→enter-world dispatch internals; **resolve the `1/6` opcode collision** (login-credential vs character-create, both 52 bytes) — the auth blocker. | 1 (critical) |
| **B2** | **ui-window-manager + in-game HUD** | The widget/window toolkit construction and the per-widget screen coordinates of the in-game HUD (inventory, buff bar, chat, minimap, stats panel); the char-select **Canvas3D preview camera** parameters. | 2 |
| **B3** | **vfs-assetio-deep** | The VFS open/lookup/read internals and parser families; the field semantics still UNVERIFIED in formats — `environment_bins` (fog/sky/water) and `actormotion.txt` col3–14. | 3 |
| **B4** | **lua-scripting** | The `game.lua` / `uiconfig.lua` entry points and config-key semantics (`vfsmode`, `launcher`, `debugmode`) and table layouts. | 4 |
| **B5** | **sound-effects + combat-timers** | The sound-table/effects dispatch and combat timing handlers Campaign 2 deferred; the queued scheduler now-ms split (debugger-confirmed). | 5 |
| **B6** | **terrain-stream** | The streaming-cell loader internals beyond the static `.ted/.map/.sod` layout already specced. | 6 (expand) |

Campaign 2 already understood + annotated the **network-dispatch · crypto-session · vfs-assetio ·
scene-machine · effects-render** spine — B builds outward from those named anchors, mostly resorbing
the `sub_xxxx` neighborhood rather than re-discovering the spine.

---

## §6 — Glossary & sync-back contract

1. Each B lane emits `names.proposed.yaml` (address → proposed canonical name + confidence H/M/L) and
   `comments.proposed.md` (neutral, no pseudo-C) under its `_dirty/` cluster folder.
2. **Phase C (Tier-1)** merges the lane proposals into a single gate-passed glossary, arbitrating
   conflicts with `CONFLICT:` markers, holding back low-confidence and out-of-cluster names. Neutrality
   gate: no `sub_/loc_/_DWORD/__thiscall`/mangling; quoted keys; no name→two-addresses; no collision
   with existing `names.yaml` canonical names.
3. **Phase D** applies the glossary via `/ida-annotate-batch` (dry-run → apply, idempotent), exactly
   one annotator at a time.
4. **Phase D/E sync-back (Tier-1)** pulls the live IDB names back into `Docs/RE/names.yaml` (the single
   address→name map of record) and appends a `journal.md` provenance entry (canonical names + counts,
   never pseudo-code).

---

## §7 — Master deliverables

**Specs (Phase C — promote/refine):**
- `Docs/RE/specs/client_workflow.md` / `client_workflow_master.md` — refine §4.4 (scene-machine /
  PIN keypad / scheduler now-ms); fold in the resolved `1/6` routing.
- `Docs/RE/specs/ui_hud.md` *(new)* — in-game HUD widget tree + per-widget coordinates; char-select
  preview camera.
- `Docs/RE/specs/vfs_internals.md` *(new)* — VFS open/lookup/read behaviour beyond the `pak.md`
  container layout.
- `Docs/RE/specs/lua_scripting.md` — `game.lua`/`uiconfig.lua` entry + config keys + table layouts.
- `Docs/RE/formats/*.md` — field-semantics fill-ins (`environment_bins`, `actormotion.txt` cols);
  the queued `formats/effects.md §E.2` and `crypto.md §6.5` refinements.
- `Docs/RE/opcodes.md` + `Docs/RE/packets/*.yaml` — the `1/6` disambiguation.

**IDB (Phase D):** new-cluster renames + comments + struct/enum types applied; `names.yaml` grown.

**Client (Phase E):**
- Layer 02 — login/PIN/server-list/char-select/enter-world packet structs + router (`1/6` resolved).
- Layer 03 — VFS internals + clarified parsers + CP949 data-table loaders.
- Layer 04 — Application use-cases for the workflow + HUD event channels.
- Layer 05 — boot→login→PIN→char-select→world front-end + in-game HUD + preview camera + atmosphere/
  water shaders.

**Tooling (Phase T):** deepened `vfs-inspect` subcommands; HUD-coordinate dump skill; `ida-debugger-
drive` refinements.

---

## §8 — Risk register (specialised)

| # | Risk | Mitigation |
|---|------|------------|
| **C3-R1** | The `1/6` collision can't be split statically. | Resolve it in Phase Dbg against the running client (breakpoint the dispatcher, observe routing by phase). If still ambiguous, request a `.pcapng` login capture. |
| **C3-R2** | Anti-debug / anti-cheat trips under the debugger. | Keep `ADVAPI32 Crypt*` / anti-cheat **out of scope**; the maintainer controls the live session; never `dbg_start`; if it trips, fall back to static + VFS-harness observation. |
| **C3-R3** | Two IDB/`names.yaml` writers collide. | One IDB writer at a time (G4); Phase D serialized to one `re-ida-annotator`; `names.yaml` Tier-1-only. |
| **C3-R4** | Pseudo-C leaks into a committed spec. | Spec-authors rewrite + self-scrub; Phase C neutrality gate; `clean-room-auditor` in Phase R. |
| **C3-R5** | Scope creep across B6/expand. | Prioritised order (G3); B6 is explicitly "expand" — defer if B1–B5 fill the cycle. |
| **C3-R6** | HUD/preview-camera coordinates wrong → mirrored/misplaced render. | Confirm coords in Phase Dbg and cross-check live via `godot-fidelity-check` in Phase R; honour world-negate-Z / mesh-negate-X. |
| **C3-R7** | IDA MCP unreachable. | `/ida-mcp-connect` preflight in Phase 0; STOP the dirty lanes if down; tooling/engineering lanes can still proceed. |

---

*Method maintained by the Top Orchestrator (main session). The run record is `ROADMAP.md`. Commit only
on explicit maintainer request.*
