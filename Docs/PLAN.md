# Docs/PLAN.md — Active Campaign Charter & Method

> **What this file is.** The *charter* of the currently-active campaign: its mandate, north stars,
> scope, and method. The **method** is `Docs/CAMPAIGN_TEMPLATE.md` (the reusable W▸P▸E▸T▸R▸C pipeline +
> 3-tier orchestration + clean-room firewall). The **live run record** — every phase status, dated,
> updated in place — is `Docs/ROADMAP.md`. When this charter and disk reality disagree, disk reality
> wins, then fix the charter.

---

## ACTIVE: CAMPAIGN 13 — Zero-Trust Ground-Truth Rebuild (*make the client work*)

**Launched:** 2026-06-16 · **Branch:** `campaign12` (continues; the campaign3→master merge was PR #1)
· **IDB anchor:** `doida.exe` SHA `263bd994…` (IDA MCP UP, Hex-Rays ready — the ground truth).

### Why this campaign exists — the correction
Campaigns 11–12 made a **methodological error**: they treated the existing C#/Godot code as the
*baseline to patch*, and measured success with `build 0/0 + 1944 tests green`. That gate is
**circular** — the tests assert what the code already does, so wrong code with matching tests reads
as "all green". (Proof: C12 had to *rewrite* tests — chat `3/21`, `DisplayFramerate`, `Flag`,
`BillingState` — because they froze the old wrong behaviour.) *Self-consistent* was mistaken for
*faithful to the original*.

### Mandate (maintainer, verbatim intent)
**Zero trust in the current C#, Godot render, and tests.** Work *as if rebuilding from zero*, driven
only by ground truth: **IDA (the source of truth)** + the **C10-re-verified `Docs/RE/` specs** + the
**official client captures** (visual oracle). Do **all the implementations possible** and **make the
Martial Heroes client actually function** on Godot + the C# project split. Deploy a large agent fleet;
use every agent and skill needed. Query IDA directly whenever a behaviour is in doubt.

### Verification hierarchy (the only sources of truth)
| Source | Status | Role |
|---|---|---|
| **IDA** — `doida.exe` `263bd994` | **absolute truth** | what the real client does, op-by-op |
| **Docs/RE specs** (C10-re-verified) | truth (clean relay of IDA) | what the C# must implement |
| **Official client captures** | visual oracle | what each scene must look like |
| current **C# / Godot code** | **0 trust** — suspect until re-derived | — |
| current **xUnit tests** | **0 trust** — many freeze wrong behaviour | rewritten *from spec* |
| current **Godot render** | **0 trust** — never self-validating | compared to the oracle only |

### The hard anti-circularity rule
A subsystem is **done only when its behaviour is re-derived from an explicit IDA address or `spec:`
citation** — *the existing code is never cited as evidence for itself.* Tests are re-derived from the
spec. Where code already matches ground truth it stays, but it is now **verified**, not trusted.
"Refaire à 0" = re-derive everything from ground truth and rebuild wherever it diverges — not blind
retyping of code that is already provably correct.

### North stars
- **N1** — total clean-room RE of `doida.exe` via IDA (broad already; this campaign *consumes* it as truth).
- **N2 (the active driver)** — a **functioning** 1:1 client on Godot + the C# layers. **Success = the
  client runs end-to-end and behaves/looks like the original**, not "tests green".

### What "make it work" means (no live server exists → client-side fidelity is the target)
1. **The scene spine flows**: `WinMain` `switch(GameState 0..7)` → Boot→Login→PIN→ServerList→
   CharSelect→(Create)→World — each scene built per spec and matching its capture.
2. **Character skinning/animation works** — the headline broken thing (mesh explodes / static pose).
3. **World renders correctly** — terrain/buildings/NPC placement/effects/env/water vs specs + captures.
4. **Wire layer byte-exact vs IDA** — protocol structs / opcode routing / crypto (server-ready; verified
   statically + against capture-derived vectors, not against its own tests).
5. **Client systems behave per spec** — input/camera/HUD/chat/inventory/minimap/sound.

### Scope — 8 subsystem lanes (priority-ordered; ★ = first/deepest)
1. **Scene spine / game loop** ★ — `game_loop`, `client_runtime`, `intro_sequence`, `frontend_scenes`
2. **Skinning / animation** ★ — `skinning`, `animation`, `actormotion`, `bindlist`, `mesh`
3. **World render** ★ — `rendering`, `terrain*`, `environment`, `effects`, `effect-scheduling`, `sky`
4. **Front-end scenes vs captures** — `frontend_scenes`, `login`, `ui_manifests`, `ui_system`
5. **Wire: protocol/opcodes/crypto** — `network_dispatch`, `opcodes.md`, `packets/*.yaml`, `crypto`, `handlers`
6. **Asset/VFS pipeline & parsers** — `pak`, `vfs_overview`, `asset_pipeline`, `resource_pipeline`, all `formats/*`
7. **HUD & UI systems** — `ui_hud_layout`, `input_ui`, `minimap`, `chat`, `inventory_trade`, `social`
8. **Domain/gameplay & data tables** — `combat`, `skills`, `progression`, `quests`, `equipment_visuals`, `config_tables`

### Decisions (carried)
- **Evidence:** IDA static + VFS observation + official captures. No live debugger this campaign —
  debugger-pending facts stay flagged-pending, never silently asserted.
- **VFS** stays memory-mapped (documented zero-copy port choice). Arch DAG = accept+document (C11 Phase-3b).
- **Commit** only on explicit request, **targeted paths**, branch `campaign12` — never `_dirty/`,
  `.godot/`, `client_dir.cfg`, or any original asset/binary.

### Out of scope (deferred)
- The game **server** (keep core engine-free for a future `Server.Console`).
- Live **debugger / packet-capture** confirmation (flagged-pending).
- Re-RE of already-verified specs (read them; do not re-derive — unless an audit lane finds one *wrong*).
- Blanket-naming the ~19k unnamed IDB functions.

### Phase pipeline (gated; method detail in `Docs/CAMPAIGN_TEMPLATE.md`)
0. **Charter & honest baseline** — this charter + ROADMAP; nuke→`--no-incremental` build + test to
   *observe* the real baseline (the diff target, not "correct"); windowed screenshot every scene;
   scaffold `_dirty/campaign13/`.
1. **Ground-truth divergence audit (W)** — massively-parallel read-only `Workflow` fan-out, one lane per
   subsystem; each re-derives behaviour/values from IDA + spec and writes a **citation-backed divergence
   ledger** with severity (breaks-function / wrong-fidelity / cosmetic). Lanes 1–3 first/deepest.
2. **Rebuild to ground truth (E)** — staged, one writer per project/wave: rebuild divergent code to match
   IDA/spec; **rewrite tests from the spec** (`// spec:` citation, never old behaviour).
3. **Verify against the original (V)** — headless boot end-to-end through the spine; windowed screenshot
   per scene vs the official captures; wire/format byte-values re-confirmed vs IDA; skinning driven to
   animate. ∥ IDB legibility (annotate re-read clusters; stage `names.yaml`).
4. **Review + hard gates + consolidate (R/C)** — parallel reviewers → fix → build 0/0 (`--no-incremental`),
   spec-conformance suites green, firewall PASS, functional+visual checklist; ROADMAP/journal/memory;
   commit on request.

### Run order
P0 → P1 (audit, lanes 1–3 first) → P2 (rebuild) → P3 (verify) → P4 (gates). **Multi-session:**
`Docs/ROADMAP.md` tracks lane/phase status in place so any session resumes cleanly.

---

*Method = `Docs/CAMPAIGN_TEMPLATE.md`. Record = `Docs/ROADMAP.md`. Ground truth for layers/conventions
/legal basis = `CLAUDE.md` + `PRESERVATION_AND_ARCHITECTURE.md`. Prior campaigns (incl. C10/C11/C12
charters) live in git history + `Docs/RE/journal.md`.*
