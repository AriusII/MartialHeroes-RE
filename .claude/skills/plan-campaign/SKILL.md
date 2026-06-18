---
name: plan-campaign
description: Use to turn a Martial Heroes user mandate into a structured, approve-ready CAMPAIGN PLAN. Produces a deeply reformulated request (optimize, restructure, make exhaustive) + a phase/objective/sub-objective decomposition (CAMPAIGN_TEMPLATE hierarchy) + the routing map (which Tier-2 lane orchestrator + agents + skills per phase) + a paste-ready ROADMAP `# CYCLE N` cycle section + preflight checklist + risk register. The procedure `agent-campaign-plan-orchestrator` runs; also the `/plan-campaign` command. Clean-side only — never calls IDA, never reads `_dirty/`.
allowed-tools: Read, Grep, Glob
model: opus
effort: high
---

# plan-campaign — mandate → approve-ready CAMPAIGN PLAN

Turn a user mandate into a complete, ready-to-approve **campaign plan**: a deeply reformulated
request, a `CAMPAIGN_TEMPLATE` phase/objective/sub-objective decomposition, a pre-wired **routing
map** (which Tier-2 lane captain + which Tier-3 workers + which skills + which specs per phase), a
paste-ready `Docs/ROADMAP.md` `# CYCLE N` section, a `§6.2` preflight checklist, and a risk register.
This is a **reasoning + reading** procedure — no scripts, no writes to repo files. The planner
**emits** the plan; **Tier-1 executes** it after approval by invoking the named lane orchestrators.

## Ground truth

The plan is grounded in **`doida.exe` (IDA) + the committed `Docs/RE/` specs** — the derived truth
engineers read. This skill is **clean-side**: it never calls IDA, never opens the debugger, and
**never reads `Docs/RE/_dirty/`**. Any binary reality-check the plan needs is **delegated by the
planner to a READONLY `re-analyst` scout** (and VFS sample checks to `vfs-data-analyst`), never done
here. If a fact cannot be settled from the committed specs and a scout cannot confirm it (MCP down /
wrong DB), the plan **records it as an open risk** — it is never fabricated.

## The procedure (the loop)

```
intake the mandate
  → deep-understand
    → REFORMULATE (optimize, restructure, make exhaustive) + CONFIRM the reframed mandate WITH the user
      → preflight (CAMPAIGN_TEMPLATE §6.2)  +  READONLY scouting (re-analyst / vfs-data-analyst)
        → decompose into Phase ▸ Objective ▸ Sub-objective (CAMPAIGN_TEMPLATE §1)
          → pre-wire routing (which captain + workers + skills + specs per phase)
            → assemble the 7-part campaign plan
```

### Step 1 — Intake & deep-understand

Capture the mandate **verbatim** and the "why now". Read the live state to ground it:
`Docs/PLAN.md` (charter/method), `Docs/ROADMAP.md` (last cycle, resume anchor), the relevant
`Docs/RE/specs|formats|structs|packets/` and `Docs/RE/opcodes.md`, and `CLAUDE.md` (layers, current
state, debts). Identify the subsystem family the mandate touches and what evidence already exists.

### Step 2 — REFORMULATE + confirm with the user (the headline)

Turn a vague ask into a **sharp, exhaustive, well-scoped** mandate. Method:

- **Keep both forms.** Record the verbatim original *and* the reframed mandate side by side
  (`CAMPAIGN_TEMPLATE` §0.1 / §6.1). Never silently rewrite the user's words.
- **Surface implicit goals/constraints.** Name the unstated outcomes (fidelity targets, the engine-free
  invariant, CP949, zero-alloc paths, the clean-room firewall, "oracle > spec" for pixels) the ask
  actually implies.
- **State explicit out-of-scope.** Write a non-empty out-of-scope list (e.g. "server deferred";
  "World scene frozen") so scope creep across a wide fan-out can't burn the cycle (risk R6).
- **Restructure for execution.** Split a fuzzy "make X better" into the concrete capabilities that, if
  delivered, satisfy it — phrased as outcomes, not tasks.
- **Ask only the load-bearing questions.** A short list of sharp clarifiers whose answers change the
  decomposition or routing (which build? which area? confirm against a capture?). Then **confirm the
  reframed mandate WITH the user before decomposing** — the reframe is approved input, not a surprise.

### Step 3 — Preflight & READONLY scouting

Run the **§6.2 pre-flight checklist** (reproduced below) so the plan ships with a verified tool
baseline and a clean ledger. Where a phase depends on an unconfirmed binary/VFS fact, the planner may
spawn a **READONLY Tier-3 scout** — `Explore` (codebase breadth), `Plan` (architecture), `re-analyst`
(READONLY IDA ground-truth, never `dbg_start`), `vfs-data-analyst` (READONLY VFS sample) — to ground
the plan. Scouts read only; they write nothing.

### Step 4 — Decompose (Campaign ▸ Phase ▸ Objective ▸ Sub-objective)

Apply the `CAMPAIGN_TEMPLATE` §1 containment tree. The cardinal rule (§1.2):

> **One sub-objective = one deliverable = one owning agent = one writable path.**

If two deliverables touch one file, they are the same sub-objective (merge) or different waves
(sequence). Never a sub-objective with two writers. Map the work to the W▸P▸E▸T▸R▸C phases (§5): not
every campaign needs all six — a pure C#-fidelity cycle may skip W/P; a pure RE cycle is W/P heavy.

### Step 5 — Pre-wire routing

For each phase, name its **Tier-2 captain**, the key **Tier-3 workers**, the **skills** they fire, and
the **specs** they read (or `_dirty/` notes they produce). Use the routing decision map below.

### Step 6 — Assemble the campaign plan

Emit the 7-part deliverable (template below). The planner returns it; it does not write `ROADMAP.md`
and does not dispatch the captains.

## Routing decision map (the russian-doll)

Pre-wire each phase to an **existing** Tier-2 captain (no new lane orchestrators — flag a missing
specialist as an open item, minted later via `kit-author`). "Route a phase here when…" heuristics:

| Domain | Tier-2 captain | Representative Tier-3 workers | Key skills | Route here when… |
|---|---|---|---|---|
| **RE (dirty→spec + IDB legibility)** | `re-orchestrator` | `re-function/protocol/crypto/struct/asset-format-analyst`, `ida-toolsmith` (IDB annotate) → bridge `spec-author`; confirm `re-validator` | `ida-mcp-connect`, `ida-explore`, `ida-annotate`, `re-promote` | a behavior/format/opcode/struct is **not yet in the committed specs** and must be recovered from the binary then promoted, OR the **IDB itself** needs legible names/comments/types |
| **C#/Godot Porting** | `port-orchestrator` | `network-engineer` (02), `assets-engineer` (03), `core-engineer` (04), `dotnet-foundation-engineer` (01+cross), `godot-world/ui-engineer`, `godot-character-specialist`, `code-reviewer`, `test-engineer`, `render-reviewer` | `dotnet-build-test`, `packet-codegen`, `pak-explore`, `godot-run-headless`, `godot-fidelity-check`, `clean-room-check` | the deliverable is **engine-free core code** (layers 01–04) or **layer-05 presentation/fidelity**, read from committed specs |
| **Planning / Kit** | `planning-orchestrator` | `requirement-analyst`, `todo-architect`, `knowledge-gap-detector`, `plan-reviewer`, `kit-author`, `tooling-auditor` | `plan-campaign`, `clean-room-check` | the phase is **planning/decomposition**, a **knowledge-gap** sweep, a **gate** (build/test/firewall/DAG via `code-reviewer`/`tooling-auditor`), or it changes the **`.claude/` kit** itself |

**Two-levels-max (hard).** Tier-1 → Tier-2 → Tier-3; a Tier-2 never spawns another Tier-2
(`CAMPAIGN_TEMPLATE` §2.2). The plan **names** the lane captains for **Tier-1 to invoke after
approval** — the planner never dispatches them and spawns **only READONLY Tier-3 scouts**. Every phase
respects the **file-ownership ledger**: one writer per path per wave (§3.1); shared files
(`journal.md`, `names.yaml`, `settings.json`, `.mcp.json`) are Tier-1-serialized, never delegated.

## The campaign-plan output template (7 parts)

1. **Mandate — verbatim + reframed.** The user's exact words and the optimized, exhaustive,
   well-scoped reframe (confirmed in Step 2), plus the "why now".
2. **Scope & out-of-scope.** One-paragraph in-scope; an explicit, non-empty out-of-scope list.
3. **Phase / Objective / Sub-objective tables.** Per phase, an objective→sub-objective table tagged
   with its pipeline letter — **W** (research) · **P** (promotion) · **E** (engineering) · **T**
   (tooling) · **R** (review/gate) · **C** (consolidation) — one deliverable + one owner + one path
   per sub-objective.
4. **Routing table.** Phase → Tier-2 captain → key Tier-3 workers → skills fired → specs read /
   `_dirty/` notes produced. (The map above, specialized to this campaign.)
5. **Paste-ready ROADMAP `# CYCLE N` section.** The `§14` Cycle Skeleton below, every `<placeholder>`
   filled, for Tier-1 to paste into `Docs/ROADMAP.md` on approval.
6. **Preflight checklist.** The `§6.2` boxes (below), each marked verified / open / N-A for this cycle.
7. **Risk register.** Notable risks (write collisions, scope creep, MCP-down, stale-build cache,
   spec/oracle conflict) with mitigations; any unconfirmed fact that scouting could not settle is a
   listed open risk, never an assumption.

## Paste-ready ROADMAP cycle section (CAMPAIGN_TEMPLATE §14)

Fill every `<placeholder>`; drop phases the campaign doesn't need (e.g. W/P for a pure C# cycle):

```markdown
# CYCLE <N> — <theme: which subsystem(s) this cycle conquers> (launched <YYYY-MM-DD>)

**Mandate (maintainer):** "<verbatim quote>". **Reframed:** <the optimized, exhaustive reframe>.
<one paragraph: what this cycle attacks and the end state it targets>.

**Master deliverable:** `Docs/RE/specs/<theme>.md` — <the authoritative synthesis, if any>.
**Out of scope (deferred):** <explicit non-goals>.
**Command structure:** Top Orchestrator + Tier-2 captains for: <phases that get one>. Others direct.

## Evidence baseline
- Relevant committed specs going in: <list>.
- Known gaps this cycle closes: <list>.
- Tool baseline verified: IDA MCP <UP/DOWN> · build <0/0> · tests <green> · VFS <reachable>.

## Phase <N>-W1 — GIGA RESEARCH (dirty room, <K> lanes) — driven by re-orchestrator
Output: `Docs/RE/_dirty/<area>/*.md` ONLY. Ledger: one writer per `_dirty/<area>/<lane>.md`.
| # | Lane (sub-objective) | Type | Agent | Question | Deliverable | Conf |
|---|----------------------|------|-------|----------|-------------|------|
| 1 | <slug> | IDA/VFS | <agent> | <question> | _dirty/<area>/<lane>.md | — |
**W1 EXIT:** critical-path lanes returned + quorum; confidence rated; conflicts flagged.

## Phase <N>-W2 — PROMOTION (spec-authors, 1 file each)
| New/updated spec | Source lane(s) | Author | Unblocks |
|---|---|---|---|
| specs/<subject>.md | lane <n> | <author> | <eng lane> |
Master synthesis written LAST (barrier). Tier-1 post-promotion (serialized): firewall scan · opcode
reconcile · names.yaml · journal.md.

## Phase <N>-E — ENGINEERING (clean room) — driven by <C# / Godot captain>
One engineer per project per stage. Stage A contracts → Stage B components → Stage C integration; ∥ tests.
**E EXIT:** build 0/0 · DAG clean · constants cited (`// spec: …`) · tests green · headless boot clean.

## Phase <N>-T — TOOLING (parallel with W/P/E)
| # | Lane | Agent | Deliverable |
|---|------|-------|-------------|
| T1 | <new skill / agent / parser> | <agent> | <deliverable> |

## Phase <N>-R — REVIEW + FIX + GATES — driven by quality-gate-orchestrator
4 read-only reviewers (render / C# / clean-room / architecture) → confirm → fix wave (one scope each) → gates.
**FINAL GATE:** build 0/0 (--no-incremental) · all suites green · firewall PASS.

## Phase <N>-C — CONSOLIDATION
ROADMAP statuses in place · journal.md · names.yaml · memory · preservation. Commit ONLY on request.

— *Maintained by the orchestrator. Update phase statuses in place as waves complete.*
```

## Preflight checklist (CAMPAIGN_TEMPLATE §6.2)

- [ ] `/ida-mcp-connect` → IDA MCP reachable, DB open, on `…/mcp?ext=dbg` (`mcp__ida__dbg_*` present) — IDA lanes only.
- [ ] `/godot-mcp-connect` → editor (9600) + game (9601) UP — interactive Godot work only.
- [ ] `dotnet build MartialHeroes.slnx` → 0 err / 0 warn (clean start line).
- [ ] `dotnet test MartialHeroes.slnx` → all suites green (known-good baseline; build cache is unreliable — nuke bin/obj for an authoritative verdict).
- [ ] VFS reachable (`clientdata/` present, or `MH_CLIENT_DIR` / `client_dir.cfg` resolves).
- [ ] `Docs/RE/_dirty/` exists and is gitignored.
- [ ] `git status` clean (or a dedicated branch created if on `master`).
- [ ] Command structure decided: which phases Tier-1 drives directly vs. delegates to a Tier-2 captain.

## Done when (the plan is approve-ready)

- [ ] Mandate captured **verbatim** and **reframed** (optimized, exhaustive, well-scoped) and the reframe **confirmed with the user**.
- [ ] Scope + a **non-empty** out-of-scope list written.
- [ ] Every phase decomposed to Objective ▸ Sub-objective with **one deliverable / one owner / one path** each (W·P·E·T·R·C tagged).
- [ ] **Every phase routed** to an existing Tier-2 captain with named Tier-3 workers, the skills they fire, and the specs they read (`_dirty/` notes for RE phases).
- [ ] Paste-ready ROADMAP `# CYCLE N` section drafted (every placeholder filled).
- [ ] Preflight checklist filled; risk register captured (every unconfirmed fact listed as an open risk).
- [ ] Two-levels-max honored: captains are **named for Tier-1 to invoke**, not dispatched; only READONLY scouts were spawned.

## Hard rules

- **Clean-side only.** Never call IDA, never open the debugger, **never read `Docs/RE/_dirty/`**.
  Binary/VFS reality-checks are delegated to READONLY scouts (`re-function-analyst` / `re-asset-format-analyst`); a
  fact a scout can't confirm is an **open risk**, never fabricated (MCP down / wrong DB ⇒ STOP-don't-invent).
- **Plan, don't dispatch.** Emit routing; **Tier-1 invokes the lane captains after approval**. Spawn
  **only READONLY Tier-3 scouts** — never a Tier-2/lane orchestrator (two-levels-max, `CAMPAIGN_TEMPLATE` §2.2).
- **Reuse, don't duplicate.** No new lane orchestrators; a missing specialist is **flagged as an open
  item** (minted later via `kit-author`), not invented in the plan.
- **Reference, don't copy.** Point at `CAMPAIGN_TEMPLATE.md` / `PLAN.md` / `ROADMAP.md` rather than
  duplicating them. No Hex-Rays pseudo-C, no addresses, no copyrighted data in the plan.
- **No repo writes here.** This skill reads and reasons; it returns the plan as its report. The
  planner materializes nothing — Tier-1 pastes the ROADMAP section on approval and never edits the
  orchestrator-owned `journal.md` / `names.yaml` / `settings.json` / `.mcp.json` from the plan.
```