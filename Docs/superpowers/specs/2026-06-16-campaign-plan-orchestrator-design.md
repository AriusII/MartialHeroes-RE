# Design — `agent-campaign-plan-orchestrator` + `plan-campaign` skill

> Date: 2026-06-16 · Branch: campaign12 · Author: maintainer + Claude (brainstormed).
> Status: APPROVED — this is the implementation brief for the meta-author fan-out.

## 1. Goal

Give the Martial Heroes kit a **Tier-0 campaign planner** that works *with* Claude Code's plan
mode. It takes a user mandate, **deeply understands and REFORMULATES it** (optimize, restructure,
make exhaustive — with the user in the loop), scouts read-only context so the plan is well-grounded,
decomposes it into the `CAMPAIGN_TEMPLATE` phase/objective hierarchy, and **pre-wires the routing**:
which existing Tier-2 lane orchestrator + which agents + which skills each phase needs. Output is a
ready-to-approve **campaign plan**. Once approved, the **main session (Tier-1) executes** by invoking
the named lane orchestrators — the planner never dispatches them itself.

The "russian-doll" (planner → lane captain → agent → skill) lives in **the plan and the linking
fabric**, never in a 3-deep runtime spawn tree.

## 2. Load-bearing constraints (from CLAUDE.md / KIT.md / CAMPAIGN_TEMPLATE.md)

- **Two levels of orchestration MAX** (Tier-1 → Tier-2 → Tier-3). A Tier-2 never spawns another
  Tier-2 (`CAMPAIGN_TEMPLATE` §2.2). ⇒ the planner is a **Tier-0 PLANNER**, not a dispatcher: it
  emits routing; Tier-1 invokes the lane orchestrators after approval. The planner may spawn **only
  READ-ONLY Tier-3 scouts** for context, never a lane/Tier-2 orchestrator.
- **Plan mode is read-only.** ⇒ the planner **returns** the campaign plan as its structured report
  (incl. a paste-ready ROADMAP cycle section); the **main session** presents it via ExitPlanMode and,
  on approval, materializes the ROADMAP + executes. The planner never calls ExitPlanMode and never
  needs to write repo files (it may write a scratch draft only when NOT in plan mode).
- **Ground-Truth Doctrine.** The plan is grounded in `doida.exe` (IDA) + the committed `Docs/RE/`
  specs. Any IDA reality-check during planning is **READONLY** (`re-analyst`), never fabricated; if
  the MCP is down/wrong-DB, STOP and note the fact as an open risk — do not invent. Each phase in the
  plan cites which specs it reads / which subsystem in the binary it depends on.
- **Reuse, don't duplicate.** The 4 lanes already have captains. No new lane orchestrators.

## 3. What we BUILD vs REUSE vs ENRICH

- **NEW (2 files):**
  - `.claude/agents/agent-campaign-plan-orchestrator.md` (the planner).
  - `.claude/skills/plan-campaign/SKILL.md` (the planning procedure + ROADMAP template + routing map).
- **REUSE (zero behavior change):** all 9 lane/quality orchestrators + every Tier-3 worker + their
  skills. The planner *names* them; it does not modify them.
- **ENRICH (doc/linking only):** `.claude/KIT.md` (new "Campaign routing map" section + Tier-0 note),
  `CLAUDE.md` (orchestration doctrine + tooling map), and thin `skills:`/roster lines on a lane
  captain only if genuinely missing.
- **YAGNI guard:** create NO speculative lane agents. If planning a real campaign reveals a missing
  specialist, the planner FLAGS it as an open item (minted later via `agent-author`).

## 4. The campaign routing map (the russian-doll)

| Lane | Tier-2 captain(s) the planner routes to | Representative Tier-3 workers | Key skills |
|---|---|---|---|
| **RE (dirty→spec)** | `re-cleanroom-orchestrator` | `re-static/protocol/crypto-analyst`, `re-struct-cartographer`, `re-asset-format/animation-analyst`, `re-analyst`, `ida-script-author`, `vfs-data-analyst` → bridge `protocol-spec-author`, `asset-spec-author` | `ida-mcp-connect`, `ida-*`, `re-promote` |
| **IDA (IDB legibility)** | `re-comprehension-orchestrator` (READONLY comprehension) + `re-annotation-orchestrator` (parallel IDB write) | `re-ida-annotator` | `ida-annotate-batch`, `ida-naming-sync` |
| **C#** | `network-stack-orchestrator` (02) · `assets-pipeline-orchestrator` (03) · `client-core-orchestrator` (04) | `network-*`, `assets-*`, `domain-`, `application-`, `client-infrastructure-`, `data-tables-`, `dotnet-engineer`, `csharp-modernizer/reviewer`, `test-engineer` | `dotnet-build-test`, `packet-codegen`, `vfs-inspect` |
| **Godot** | `godot-client-orchestrator` (05) | `godot-presentation/ui/input-engineer`, `godot-skinning/shader-specialist`, `godot-render-reviewer`, `godot-mcp-operator` | `godot-run-headless`, `godot-screenshot`, `godot-fidelity-check`, `asset-chain-trace` |
| **Quality / Kit** | `quality-gate-orchestrator` (pre-commit) · `tooling-orchestrator` (the `.claude/` kit) | reviewers / meta-authors | `clean-room-firewall-check` |

## 5. `agent-campaign-plan-orchestrator` — spec

**Frontmatter:** `name: agent-campaign-plan-orchestrator`; `model: opus`; `effort: high`;
`tools: Read, Grep, Glob, Write, Agent(Explore, Plan, re-analyst, vfs-data-analyst), Bash(claude mcp *), Bash(git status*)`;
`skills: plan-campaign, ida-mcp-connect`; `color: purple`.
`description` leads with `Use PROACTIVELY when the user asks to plan a campaign / a multi-phase
objective / "prepare a plan" / enters plan mode for a big initiative` … `It PLANS the routing; it
does NOT dispatch the lane orchestrators (Tier-1 executes after approval). For a single-deliverable
task go straight to the worker; to execute an already-planned phase go to the lane orchestrator.`

**Body must contain (house style — mirror `re-cleanroom-orchestrator`):**
- **Ground-Truth Doctrine** section (plan grounded in IDA + specs; READONLY scouting; STOP-don't-fabricate).
- **Tier-0 placement** — plans routing, does NOT dispatch lane orchestrators; two-levels-max preserved;
  may spawn only READ-ONLY Tier-3 scouts; how it hands off to Tier-1.
- **Plan-mode integration** — returns the plan; main session presents via ExitPlanMode; never calls
  ExitPlanMode; no repo writes in plan mode.
- **Reformulation discipline (the headline feature)** — deep-understand → reformulate (optimize,
  restructure, make exhaustive) → **confirm the reframed mandate WITH the user** via sharp questions
  before decomposing. Capture the verbatim original mandate + the reframed one (per `CAMPAIGN_TEMPLATE`
  §0.1/§6.1).
- **Operating states (the loop):** `intake → deep-understand → reformulate+confirm → preflight & read-only
  scouting → decompose (phase/objective/sub-objective) → pre-wire routing → emit plan`.
- **Routing map (roster)** = §4 table, with "when to route to each captain" heuristics + each captain's
  key workers + skills.
- **Read-only scout team:** `Explore` (codebase breadth), `Plan` (architecture), `re-analyst`
  (READONLY IDA ground-truth check — never `dbg_start`), `vfs-data-analyst` (READONLY VFS sample check).
- **The campaign-plan deliverable format:** (1) verbatim mandate + reframed mandate; (2) scope +
  out-of-scope; (3) phase/objective/sub-objective tables (W·P·E·T·R·C); (4) the routing table (phase →
  captain → workers → skills → specs read); (5) a paste-ready ROADMAP `# CYCLE N` cycle-section draft
  (§14); (6) preflight checklist; (7) risk register.
- **Anti-patterns / Done-when / North-star / Hard rules** — incl. "never spawn a lane orchestrator or
  any Tier-2; only READ-ONLY scouts", "never call ExitPlanMode", "never commit", "don't edit
  orchestrator-owned files (`journal.md`, `names.yaml`, `settings.json`, `.mcp.json`)".

## 6. `plan-campaign` skill — spec

**Frontmatter:** `name: plan-campaign`; `description` leads with when-to-use (turn a mandate into a
structured, approve-ready campaign plan + routing map + ROADMAP cycle section; the procedure the
planner runs; also a `/plan-campaign` command); `allowed-tools: Read, Grep, Glob` (clean-side, no IDA,
no `_dirty/`); `model: opus`, `effort: high`.

**Body encodes:** the Phase-0 procedure (`CAMPAIGN_TEMPLATE` §0/§6), the reformulation method, the
routing decision map (§4), the §14 Cycle-Skeleton ROADMAP template, the preflight checklist, and
done-criteria. Clean-room neutral — references `CAMPAIGN_TEMPLATE`/`PLAN.md`/`ROADMAP.md`, duplicates
no copyrighted data, never reads `_dirty/`, never calls IDA.

## 7. Validation

`tooling-auditor` after authoring: frontmatter valid (model/effort; agents use `tools:`, skills use
`allowed-tools:`), the planner's roster names only real agents, `skills:`/`Agent(...)` resolve, no
dup names, doctrine reflected, no clean-room leakage. Then update KIT.md + CLAUDE.md and re-audit.
