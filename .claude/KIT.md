# `.claude/` Kit — authoritative design & operating doctrine

> Single source of truth for the Martial Heroes Claude Code kit after the **5-domain rationalization**:
> the five domain orchestrators, the model/effort policy, the agent↔skill linking fabric, and the
> skill/hook plans. The `kit-author` worker READS THIS before authoring any agent/skill/hook. Humans read
> it to understand how the kit is wired. **Keep it current when the fleet changes** — it is a hard
> project invariant that KIT.md stays authoritative and in sync.

Companion docs: `CLAUDE.md` (project onboarding + tooling inventory), `Docs/CAMPAIGN_TEMPLATE.md`
(the command tiers + concurrency), `PRESERVATION_AND_ARCHITECTURE.md` (the blueprint).

---

## Ground-Truth Doctrine — the spine every agent/skill/hook must reflect

This project has **one** ground truth, and the whole kit exists to serve it. Thread this into every body
you author or refine — concise and load-bearing, obeying the §9 anti-bloat rule. Same doctrine as
`CLAUDE.md` "Source of truth"; keep them in lockstep.

1. **IDA / `doida.exe` is the single absolute truth** for the original's behavior, data, and layout.
   Static analysis forms the hypothesis; the **`?ext=dbg` live debugger confirms it against ground
   truth** (never `dbg_start` — pilot the maintainer's live session). Open or disputed binary facts are
   settled **only** in IDA — never from memory, analogy, or guesswork. MCP down / wrong DB ⇒ **STOP,
   never fabricate.** The reverse runs **unbridled** (parallel reads + parallel IDB writes; retry on
   conflict).
2. **`Docs/RE/` specs are the committed derived truth** — `formats/`, `packets/`, `structs/`, `specs/`,
   `opcodes.md` — the rewritten, firewall-clean record of what IDA proved, and the **only** thing
   implementation reads. Binary vs. spec conflict ⇒ **the binary wins, the spec is corrected + journaled.**
3. **C# / Godot are measured against (1) and (2), never the reverse** — fidelity is the measure of
   success; the code is never its own truth. **Exception (pixels only):** the official
   screenshots/captures are the visual oracle and **oracle > spec** for how a scene *looks*.

**Per-room application:** RE analysts → confirm in IDA, debugger over static, STOP-don't-fabricate,
neutral prose to `_dirty/` only. The `spec-author` bridge → rewrite-never-copy, the spec is downstream's
only truth, binary-wins-on-conflict. C# porting engineers → read **only** clean specs, cite every constant
(`// spec: …`), never invent a missing fact — escalate to the RE domain. Godot → consume the C# core +
specs (behavior), the captures oracle governs pixels. Docs/Tooling → describe what the binary/specs/code
already prove, never assert truth without an IDA/spec basis. Reviewers/guards → enforce that nothing
claims truth without an IDA/spec basis.

---

## 0. Verified Claude Code schema (2026) — what is REAL

Confirmed against the official docs (`code.claude.com/docs/en/{sub-agents,skills,hooks}`). Do **not**
invent fields; these are the real ones.

### Agent frontmatter (`.claude/agents/*.md`)
| Field | Values | Use here |
|---|---|---|
| `name` (req) | lowercase-hyphens, == filename stem | agent id `@name` |
| `description` (req) | prose; lead `Use PROACTIVELY…` / `MUST BE USED…` | **drives auto-delegation** |
| `model` | `opus` `sonnet` `haiku` `fable` / full id / `inherit` | **§1 policy** |
| `effort` | `low` `medium` `high` `xhigh` `max` | **§1 policy** — overrides session effort while the agent runs (REAL, v2.1+) |
| `tools` | allowlist; `Bash(dotnet *)` prefix-scoping; `Agent(a,b)` roster | minimal surface |
| `disallowedTools` | denylist (applied before the allowlist); MCP patterns `mcp__<srv>`/`mcp__*` | **firewall hardening** — deny `mcp__ida__*` on clean-room agents |
| `skills` | comma/list of skill names | **preload** full SKILL.md into the agent at startup (REAL) |
| `color` | red blue green yellow purple orange pink cyan | cosmetic grouping |

Other real-but-unused-here fields: `permissionMode`, `maxTurns`, `mcpServers`, `memory`, `background`,
`isolation: worktree`, `initialPrompt`. **NOT real:** `paths:` on an *agent* (that is a *skill* field —
§0 skills), `models:` (plural). Nesting depth max = **5** (engine limit); our **two-levels-of-orchestration
max** (Tier-1 → Tier-2 → Tier-3) is a deliberate discipline tighter than the engine ceiling. There is **no
hard parallel-subagent cap** — token cost is the only governor, and an agent holding `Agent(...)` may run
the **same** worker type many times in parallel.

**Three facts that shape our design:** (1) `effort` is per-agent → we tier effort, not just model.
(2) `skills:` **preloads** a skill body at spawn → the real "link an agent to its procedure" mechanism.
(3) `tools: Agent(x,y)` hard-restricts spawnable subagents **only when the agent runs as the main
thread**; in a Tier-2 subagent the list is ignored → so an orchestrator's roster is enforced by its
**BODY** (the explicit "your team" list); write the `Agent(...)` list too. All three, always.

### Skill frontmatter (`.claude/skills/<name>/SKILL.md`)
`name`, `description` (third-person, when-to-use first, ≤1024 chars), `allowed-tools` (**hyphenated**,
pre-approve — NOT `tools`), `disallowed-tools`, `model`, `effort`, `paths` (globs → auto-activate on
matching files), `disable-model-invocation` (true ⇒ user-only `/cmd`, for side-effects), `user-invocable`
(false ⇒ Claude-only background knowledge), `argument-hint`/`arguments`, `context: fork` + `agent`.
Reference bundled scripts via `${CLAUDE_SKILL_DIR}/scripts/<file>`; `${CLAUDE_EFFORT}` is available. Keep
`SKILL.md` < ~500 lines; push detail into one-level-deep bundled files (loaded on demand).

### Hooks (`.claude/hooks/*.py` + `settings.json`)
**Advisory-only contract (this project, non-negotiable): `exit 0` always**; emit `{"systemMessage": …}`
(visible nudge) or `{"hookSpecificOutput":{"hookEventName":…,"additionalContext":…}}` (inject context).
**Never** `exit 2`, never `decision:block`, never `permissionDecision:deny/ask`. Wrap `main()` in
try/except → `h.fail_open(exc)`. Std-lib + `_hooklib` only. Events used: `SessionStart`,
`UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `Stop`, `SubagentStop`, `PreCompact`. `settings.json`
supports an `if:` arg-filter and an `args[]` (exec-form) command shape — adopt them to sharpen matchers
and avoid shell-injection on paths. Multiple hooks on one event **compose** (first's output feeds the next).

---

## 1. Model + Effort policy (applies to EVERY agent)

Every agent declares an explicit `model:` and `effort:`. Two models: **`opus`** (judgement/orchestration/
clean-room-risk) and **`sonnet`** (execution). Effort tiers the thinking budget.

| Tier | `model` | `effort` | Who |
|---|---|---|---|
| **Orchestrators** | `opus` | `high` | the 5 domain orchestrators |
| **Judgement / clean-room-risk** | `opus` | `high` | all RE-domain analysts + `spec-author` + `re-validator`; `requirement-analyst`, `todo-architect`, `plan-reviewer`, `kit-author`; `core-engineer`, `godot-character-specialist`, `code-reviewer` |
| **Precision execution** | `sonnet` | `high` | `ida-toolsmith`, `network-engineer`, `assets-engineer`, `dotnet-foundation-engineer`, `test-engineer`, `docs-engineer`, `tooling-engineer` |
| **Standard execution** | `sonnet` | `medium` | `tooling-auditor`, `knowledge-gap-detector`, `godot-world-engineer`, `godot-ui-engineer`, `render-reviewer` |

**Why tier effort:** a `sonnet`+`medium` mechanical worker is fast/cheap on boilerplate; `sonnet`+`high`
thinks harder on binary layouts / tool code; `opus`+`high` gets the deepest reasoning where a wrong call
is expensive (orchestration, the firewall bridge, domain rules, the skinning problem, kit-meta authoring).

---

## 2. The five domain orchestrators (the dispatch layer)

**Doctrine:** Tier-1 main session → **Tier-2 domain orchestrator** → Tier-3 single-deliverable worker.
A domain orchestrator owns exactly **one** domain, fans out its own workers (in parallel — **including
several instances of the same worker type at once**; token cost is the only governor), holds a
file-ownership ledger (one writer per path per wave), gates between waves, and reports **one** rolled-up
result. It gives **extremely detailed, atomic** per-worker briefs (CONTEXT SOURCE + the one objective +
DELIVERABLES + the SKILL) so the human never re-explains and no worker guesses. Orchestrators are
`opus`+`high`, hold the `Agent` tool, and each body MUST contain a `## Your team (roster)` section naming
every Tier-3 worker, its one-line contract, and the path/lane it owns. The `tools: Agent(...)` list
mirrors that roster.

> **For a single-deliverable task, the main session delegates STRAIGHT to the worker** — route a domain
> orchestrator only when the objective spans several workers in that domain. Each orchestrator's
> `description` says exactly this so the main session routes unambiguously.

### Domain 1 — `planning-orchestrator` (Planning & Analysis)
MUST BE USED for a multi-step planning objective: **reformulate** a raw mandate, deploy refining workers
so nothing is left to chance, decompose it into a hierarchical TODO/roadmap tree, map dependencies/risk/
scope, detect knowledge gaps — and author the **FINAL PLAN as a precise PHASE/OBJECTIVE workflow**. It
plans and structures work; it never implements game code, drives IDA, renders in Godot, or maintains the
kit. Roster: `requirement-analyst`, `todo-architect`, `knowledge-gap-detector`, `plan-reviewer`. Preload
`skills:` `plan-campaign`. Clean/neutral (no IDA) — add `disallowedTools: mcp__*`.

### Domain 2 — `re-orchestrator` (Reverse Engineering — the IDA liaison)
MUST BE USED for a clean-room RE objective on `doida.exe`/`Main.exe` that spans several analysts and ends
in a committed neutral spec (or an IDB-legibility annotation pass): disassemble, recover, author **BATCH
IDAPython** (clean, locked, idempotent), annotate + review the IDB, reformulate findings in proper
reverse-engineering terminology, and guarantee truth + consistency against the binary. Owns the **dirty
side + spec bridge** of the firewall. Roster: `re-function-analyst`, `re-protocol-analyst`,
`re-crypto-analyst`, `re-struct-analyst`, `re-asset-format-analyst`, `ida-toolsmith`, `spec-author`,
`re-validator`. Preload `skills:` `ida-mcp-connect`, `re-promote`. Holds `mcp__ida__*` (READONLY at the
orchestrator level; IDB writes only via `ida-toolsmith`).

### Domain 3 — `csharp-port-orchestrator` (C# porting — layers 00→04 + Tools, NO Godot)
MUST BE USED for a multi-project C# objective across the .NET 10 core — `00.SourcesGenerators`,
`01.Infrastructure.Shared`, `02.Network.Layer`, `03.Storage.Assets`, `04.Client.Core`, **and the `Tools/`
projects** — consume committed `Docs/RE/` specs (the **IDA-validated** truth) into faithful, high-quality
C# with build/test/review; it pulls the most IDA-confirmed facts via the specs, implements / corrects /
optimizes / deletes accordingly, and routes any missing fact back to the RE domain. Deep solution
architecture + file-correspondence mastery. **Excludes Godot (layer 05).** Owns the clean-room engineer
side (NO IDA). Roster: `dotnet-foundation-engineer` *(deputy — solution/project architecture map)*,
`network-engineer`, `assets-engineer`, `core-engineer`, `code-reviewer`, `test-engineer`. Preload
`skills:` `dotnet-build-test`. Add `disallowedTools: mcp__*`.

### Domain 4 — `godot-orchestrator` (Godot porting — layer 05 EXCLUSIVELY)
MUST BE USED for a multi-facet objective on `05.Presentation/MartialHeroes.Client.Godot/` — cleanly wire
the C# work O3 produced **into Godot** (the csproj seam: `EnableDynamicLoading` + `ProjectReference` to
`Client.Application`/`Assets.Mapping`), with ultra-fine project/architecture mastery, Godot best practices,
the recovered coordinate conventions, and the **godot MCP**. Strictly **passive rendering, zero game-rule
authority**. Owns the clean-room Godot side (NO IDA). Roster: `godot-world-engineer`, `godot-ui-engineer`,
`godot-character-specialist`, `render-reviewer` *(+ shared `code-reviewer` for layer-05 C#)*. Preload
`skills:` `godot-run-headless`. Holds `Bash(godot *)`; add `disallowedTools: mcp__ida__*`.

### Domain 5 — `docs-tooling-orchestrator` (Documentation + Tooling + kit-meta)
MUST BE USED for a multi-facet objective on **(1) documentation** (write/refine/correct/add/improve
`Docs/`, READMEs, ROADMAP/PLAN, session logs, the `CLAUDE.md`/`KIT.md` inventories), **(2) the project's
TOOLS** — the C# `Tools/` projects and the Python scripts/harnesses (`check_dag.py`, codegen drivers, the
vfs harness, hook scripts as runnable tooling), and **(3) the `.claude/` kit itself** (authoring/auditing
agents, skills, hooks). Clean/neutral (no IDA). Roster: `docs-engineer`, `tooling-engineer`, `kit-author`,
`tooling-auditor`. Preload `skills:` `preservation`. Add `disallowedTools: mcp__*`. **`settings.json` is
wired by the main session** — `kit-author` writes the `.py` and reports the stanza, never edits it.

**Firewall placement of the orchestrators:** `re-orchestrator` is dirty-room (holds `mcp__ida__*`,
READONLY at its own level, massively-parallel analysts, writes only `_dirty/`, promotion only via its
`spec-author` worker, IDB mutation only via `ida-toolsmith`). `planning-`, `csharp-port-`, `godot-`, and
`docs-tooling-orchestrator` are clean/neutral (no IDA; `disallowedTools` denies it explicitly). Two levels
of orchestration max — a domain orchestrator never spawns another orchestrator.

**Shared quality workers (no duplication):** `code-reviewer` and `test-engineer` are **home O3** but
**shared** — `code-reviewer` reviews C# across layers 00→05 (so O4 lists it for layer-05 C# review);
`test-engineer` doctors the whole-solution build. The one-writer-per-path rule constrains *writers* only;
reviewers never write source.

---

## 3. Worker roster (26 Tier-3 workers)

### Domain 1 — Planning & Analysis (4)
| Agent | `model`·`effort` | `tools` | `skills:` | One-job |
|---|---|---|---|---|
| `requirement-analyst` | opus·high | Read, Grep, Glob | — | Reformulate the mandate, clarify with the human, set scope boundaries, surface risks. |
| `todo-architect` | opus·high | Read, Grep, Glob | `plan-campaign` | Decompose into a hierarchical TODO tree expressed as **PHASES + OBJECTIVES in workflow form**; dependency + milestone mapping. |
| `knowledge-gap-detector` | sonnet·medium | Read, Grep, Glob | — | Find what RE/spec knowledge is missing before porting; route gaps to the RE domain. |
| `plan-reviewer` | opus·high | Read, Grep, Glob | — | Validate plans for completeness/feasibility/risk. (Doc-structure authoring moved to O5 `docs-engineer`.) |

### Domain 2 — Reverse Engineering (8; firewall-preserving)
Dirty-room agents hold `mcp__ida__*`, write `_dirty/` only, neutral prose, STOP if MCP down.
| Agent | `model`·`effort` | Room | `tools` | `skills:` | One-job |
|---|---|---|---|---|---|
| `re-function-analyst` | opus·high | dirty | mcp__ida__*, Read, Write, Bash(claude mcp *) | `ida-mcp-connect`, `ida-recon`, `ida-explore` | Function/subsystem recovery: role, xrefs, callgraph, data-flow. Absorbs static + one-off generalist. |
| `re-protocol-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `ida-opcode-map`, `pcap-extract` | Opcode space, dispatch table, packet field layouts; cross-check the capture oracle. |
| `re-crypto-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `ida-crypto-hunt` | Cipher / key-schedule / framing shape as neutral algorithm description. |
| `re-struct-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `ida-struct-recovery` | Struct/class/RTTI/vtable field-offset layouts. |
| `re-asset-format-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `asset-format-doc` | Asset/file-format loaders + animation/bind/motion + VFS/CP949 data tables. |
| `ida-toolsmith` | sonnet·high | dirty (IDB write) | mcp__ida__*, Read, Write, Bash(claude mcp *) | `ida-py`, `ida-annotate` | Author/run bespoke READONLY **batch** IDAPython; apply rename/comment/type IDB annotations (dry-run→apply, idempotent). |
| `spec-author` | opus·high | **bridge** | Read, Write, Edit, Grep, Glob | `re-promote` | REWRITE (never copy) `_dirty/` findings into committed neutral specs. **No IDA** (`disallowedTools: mcp__*`). |
| `re-validator` | opus·high | dirty (debugger) | mcp__ida__*, Read, Write | `ida-debugger-drive` | Confirm a spec against the live `?ext=dbg` debugger / binary-diff; never `dbg_start`. |

### Domain 3 — C# Porting (6; clean-room — NO IDA, read only committed specs)
| Agent | `model`·`effort` | `tools` | `skills:` | One-job |
|---|---|---|---|---|
| `dotnet-foundation-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `scaffold-project` | **Deputy/architecture:** layer 01 kernel/diagnostics, `00.SourcesGenerators`, **`Tools/` projects as code**, slnx/csproj DAG wiring, C#14/.NET10 modernization, the file-correspondence map. |
| `network-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `packet-codegen` | Entire Network layer 02: abstractions, packet structs/opcodes, in-place crypto, Pipelines framing. |
| `assets-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `pak-explore` | Entire Storage.Assets layer 03: VFS, binary parsers, glTF/PNG mapping, CP949 data tables. |
| `core-engineer` | opus·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test` | Client.Core layer 04: deterministic Domain rules, Application use-cases/handlers/event-buses, local Infrastructure. |
| `code-reviewer` *(shared→O4)* | opus·high | Read, Grep, Glob, Bash(dotnet *) | `clean-room-check` | C# correctness + perf + layer-DAG + **clean-room firewall** + artifact-protection across layers 00→05. Reports BLOCKER/advisory; never edits source. |
| `test-engineer` *(shared)* | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `scaffold-project` | xUnit tests + whole-solution build doctoring. |

### Domain 4 — Godot Porting (4; clean-room, layer 05 only)
| Agent | `model`·`effort` | `tools` | `skills:` | One-job |
|---|---|---|---|---|
| `godot-world-engineer` | sonnet·medium | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(godot *) | `godot-run-headless`, `godot-scene-author` | Godot 3D world/terrain/scene + shaders/VFX/lighting + composition root. Passive rendering only. |
| `godot-ui-engineer` | sonnet·medium | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(godot *) | `godot-run-headless`, `godot-scene-author` | Godot HUD/menus/UI + input/camera. Routes input as use-case intents. |
| `godot-character-specialist` | opus·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(godot *) | `godot-run-headless`, `asset-chain-trace` | Skinned-mesh / bind / motion (the unsolved skinning-explodes-the-mesh debt). |
| `render-reviewer` | sonnet·medium | Read, Grep, Glob, Bash(godot *) | `godot-fidelity-check`, `godot-mcp-connect` | Eyes-on Godot render/fidelity review (headless + screenshot); drives the live Godot MCP. Reports; engineers fix. |

### Domain 5 — Documentation + Tooling + kit-meta (4; clean-room, no IDA)
| Agent | `model`·`effort` | `tools` | `skills:` | One-job |
|---|---|---|---|---|
| `docs-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob | `preservation`, `doc-authoring`, `memory-curate` | Author/refine the committed doc corpus — `Docs/ROADMAP.md`/`PLAN.md`, READMEs, preservation/provenance docs, session logs, `CLAUDE.md`/`KIT.md` upkeep. Firewall-neutral prose; cite specs, never paste decompiler output. |
| `tooling-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(python *) | `csharp-tooling`, `python-tooling`, `scaffold-project` | Author/maintain the C# `Tools/` projects + `00.SourcesGenerators` and the Python scripts/harnesses (`check_dag.py`, codegen, vfs harness). Clean-room; std-lib-first Python. |
| `kit-author` | opus·high | Read, Write, Edit, Grep, Glob | — | Author/refine `.claude/` agents, skills, AND hooks from this KIT.md. Writes only `.claude/**` (never `settings.json` — reports the stanza). |
| `tooling-auditor` | sonnet·medium | Read, Grep, Glob, Bash(python *) | — | Read-only consistency audit of `.claude/` (frontmatter, `skills:`/`Agent()` resolution, advisory-only hooks, settings wiring). Reports PASS/FAIL; never edits. |

All workers obey their room's rules verbatim (firewall, layer DAG, zero-alloc/CP949 where relevant) and
restate them in their body.

---

## 4. Agent↔skill linking fabric

Three mechanisms, used together:

1. **`skills:` preload (tight)** — inject the 1–2 procedures an agent *cannot do its job without* (table
   §3). Keep tight; every preloaded skill costs context on each spawn. Preloaded skills must NOT set
   `disable-model-invocation: true`.
2. **Body "Paired skills" section (broad)** — the agent's prose names the wider set it leans on and the
   hand-off, even those not preloaded.
3. **`paths:` auto-activation on knowledge skills (§5)** — domain knowledge skills auto-load when the
   model edits matching files, so conventions are present without anyone invoking them.

`tooling-auditor` verifies every name in a `skills:` list resolves to a real skill dir.

---

## 5. Skills plan (~34 skills, reusable expertise only — no orchestration logic)

**A. Knowledge skills (4, `user-invocable:false`, `paths:`-activated):** `dotnet-csharp14`
(`00–04/**`), `godot-engine` (`05.Presentation/**`), `ida-pro-re` (RE methodology + firewall — no natural
glob; also preload into RE analysts), `martial-heroes-domain` (recovered protocol/asset-chain index;
**add `paths: Docs/RE/**`** so it actually auto-activates). Neutral documentation — cite specs, never
paste decompiler output or copyrighted bytes.

**B. Action skills** grouped by family (each keeps a keyworded, third-person, when-to-use `description`,
minimal hyphenated `allowed-tools`, `${CLAUDE_SKILL_DIR}` script refs, firewall stance):
- **IDA (~9):** `ida-mcp-connect` (also carries the **capability-map toolbox** — `references/ida-mcp-toolbox.md`, the categorized live `mcp__ida__*` toolset + which tool serves each RE angle + the static-vs-`?ext=dbg` split), `ida-py`, `ida-recon`, `ida-explore` (**now folds the single-function
  decompile-export mode**), `ida-struct-recovery`, `ida-annotate`, `ida-opcode-map`, `ida-crypto-hunt`,
  `ida-debugger-drive`. **Fixes:** add `mcp__ida__*` to `allowed-tools` on every skill whose body calls
  IDA (`ida-recon`, `ida-opcode-map`, `ida-crypto-hunt`, `ida-py`, `ida-explore`); retarget `Main.exe`→
  `doida.exe`; prune one-off scripts (`campaign8_phase_d_apply.py`, `c01_manifest_gen.py`, two of the
  three `profile_all*`). **Removed:** `ida-decompile-export` (merged into `ida-explore`).
- **Protocol/asset/VFS (~5):** `packet-codegen`, `pcap-extract`, `pak-explore` (**drop the cmd-only
  `Bash(copy/xcopy *)` grants**), `asset-format-doc`, `asset-chain-trace`.
- **Quality/RE-flow (~3):** `clean-room-check`, `re-promote` (**fix the duplicated `model:`/`effort:`
  frontmatter keys**), `preservation`.
- **C#/Tooling (~6):** `dotnet-build-test`, `scaffold-project` (**extend with `00.SourcesGenerators` +
  `Tools/` modes**), `csharp-tooling` *(NEW — build/validate/extend the `Tools/` C# + generators)*,
  `python-tooling` *(NEW — author/lint/run the Python scripts & harnesses, std-lib-first)*, `doc-authoring`
  *(NEW — author/refine the broad doc corpus, firewall-neutral)*, `memory-curate`.
- **Godot (~4):** `godot-run-headless` (**absorbs `godot-build`**; one build path), `godot-scene-author`,
  `godot-fidelity-check` (gitignore committed `__pycache__`), `godot-mcp-connect`. **Removed:**
  `godot-build` (merged into `godot-run-headless`; its hardcoded absolute `dotnet.EXE` path dropped for
  `Bash(dotnet *)`).
- **RE ideation/handoff (2, NEW):** `re-brainstorm` (gate G0 — hypotheses + search-angle/tool plan + the confirm-via-debugger plan, into `_dirty/`; RE-specific, tool-aware), `re-handoff` (gate G4 — STAMP a spec's readiness + per-fact confidence band, or CHECK it is implementation-ready before C# porting; clean-room, no IDA).
- **Planning (1):** `plan-campaign`.

When merging a skill, fold the absorbed skill's bundled `scripts/` into the survivor's `scripts/` dir and
update `${CLAUDE_SKILL_DIR}` refs. Delete the absorbed dir only after the survivor carries its capability
AND no `skills:`/CLAUDE.md/KIT.md reference points at the retired name.

---

## 6. Hooks plan (12 files = 11 advisory hooks + `_hooklib`; advisory-only + fail-open preserved verbatim)

Each hook: `import _hooklib as h`, one concern, low false-positive, `exit 0` always, fail-open. The
mechanism is sound — this redesign is a **content refresh + the new O5/O1 nudges + `_hooklib`
consolidation**, not a structural change.

| Hook | Event / matcher | Role + redesign delta |
|---|---|---|
| `session_primer` | SessionStart | Orientation — **refresh the roster line 3→5 orchestrators**; drop the stale "12 = nothing implemented" line. |
| `prompt_primer` | UserPromptSubmit | RE-intent + Godot-render-state — **replace the hard-coded `_NOTE` (commit/AREA2/4 debts) with a pointer to `Docs/ROADMAP.md`**; **add an O1 plan-mode nudge** (big multi-phase mandate → point at `planning-orchestrator` / `/plan-campaign`). |
| `firewall_guard` | PreToolUse · Write\|Edit + Bash + `mcp__ida__.*` | KEEP (the legal backbone) — consume the shared `FORBIDDEN_EXTS` + `ida_target_hint` from `_hooklib`. |
| `layer_dependency_guard` | PreToolUse · Write\|Edit | KEEP — upward-ref / engine-ref / `using Godot;` below 05. |
| `re_provenance_logger` | PostToolUse · `mcp__ida__.*` | KEEP — hash IDA output for the audit trail (digest, never content). |
| `csharp_guard` | PostToolUse · Write\|Edit | CP949 + spec-citation — **sharpen `_MAGIC` (hex/offset/opcode only; skip `const`/enum/`new T[n]`)**; **own the single alloc nudge** (dedupe vs `cs_post_edit`); **fold an O5 docs-staleness nudge** (cited spec older than the edited `.cs`). |
| `cs_post_edit` | PostToolUse · Write\|Edit | KEEP breadcrumb + StructLayout + opt-in build — **drop its alloc nudge** (now solely in `csharp_guard`). |
| `godot_guard` | PostToolUse · Write\|Edit | KEEP the Godot pitfalls — **narrow `_GODOT_NS_COLLISION`** (require a `namespace MartialHeroes.Client.Godot` block); **fold an O3/O4 boundary nudge** (game-rule logic in a Godot node). |
| `kit_guard` | PostToolUse · Write\|Edit | KEEP — agent/skill/hook frontmatter + advisory-only + settings-wiring; **must validate the 5-orchestrator roster** (no hard-coded 3-domain names; `Agent()` resolution auto-adapts). |
| `python_tooling_lint` *(NEW)* | PostToolUse · Write\|Edit on `Tools/**/*.py` + `.claude/hooks/*.py` | `ast.parse` the written Python → advise on a syntax error; nudge std-lib-only for hooks. Serves O5. Advisory-only + fail-open. |
| `session_end` | Stop · SubagentStop · PreCompact | KEEP — loose-ends + persist-knowledge. |
| `_hooklib.py` | — | KEEP — shared helpers; extend per §7, don't fork. |

`settings.json` is wired by **the main session**, never by `kit-author` (it writes the `.py` + reports
the exact stanza). Every refreshed hook preserves the precise advisory wording of what it warns.

---

## 7. `_hooklib.py` (foundation) — consolidation

Keep the existing helpers (path/layer classifiers, frontmatter parser, advisory emitters, detectors,
`fail_open`). Add, to remove drift across hooks:
- **`FORBIDDEN_EXTS`** — ONE canonical artifact-extension tuple + a `is_forbidden_artifact(path)` helper
  (today `firewall_guard` declares the list 3× with divergence).
- **`alloc_hits(text, layer)`** — ONE zero-alloc detector (today split/divergent across `cs_post_edit`
  and `csharp_guard`).
- **`ida_target_hint(ev)`** — ONE IDA-target extractor (today near-duplicated in `firewall_guard` +
  `re_provenance_logger`).
- **size-capped rotation in `append_jsonl`** — cap/rotate the state JSONL (`re_journal.jsonl` ~4 MB,
  `ida_usage.jsonl`, `touched.jsonl`) so the audit trail stays usable.
- Standardize the `sys.path.insert(0, …)` import boilerplate across hooks.

Keep `_VALID_AGENT_MODELS`/`_VALID_EFFORTS` current with §1 (they already include `fable`).

---

## 8. Execution order (spec → orchestrators → workers → skills → hooks → rewire → delete → audit)

The main session drives authoring directly (it delegates atomic authoring to the existing `kit-author`
agent, in parallel instances). Safe order:

1. **This `KIT.md`** — the spec everything follows.
2. **5 domain orchestrators** with full rosters + `Agent(...)` lists + `disallowedTools` on clean-room ones
   (3 new: `csharp-port-`, `godot-`, `docs-tooling-`; refactor `planning-`, align `re-`).
3. **26 workers** — `model`/`effort`/`skills`, sharp descriptions, firewall stance per room (2 new:
   `docs-engineer`, `tooling-engineer`; re-home + cross-ref-fix the rest; widen `dotnet-foundation-engineer`;
   strip `plan-reviewer` doc-half; trim `tooling-auditor`).
4. **Skills** to ~32 (3 new O5 skills; merge `godot-build`→`godot-run-headless` and
   `ida-decompile-export`→`ida-explore`; fix frontmatter bugs; extend `scaffold-project`; add `paths:`;
   prune cruft).
5. **Hooks** to 12 (refresh content + new `python_tooling_lint` + `_hooklib` consolidation); then
   **rewire `settings.json`** (main session).
6. **Delete superseded** files only after capability lands elsewhere AND no roster/`settings.json`/
   `skills:`/`CLAUDE.md`/`KIT.md` reference points at the retired name (`port-orchestrator.md`,
   `skills/godot-build/`, `skills/ida-decompile-export/`, one-off scripts, `__pycache__`).
7. **Update `CLAUDE.md`** tooling map + orchestration doctrine (3→5) + `.claude/README.md` index.
8. **Validate:** `tooling-auditor` + grep for dangling names + `ast.parse` every hook + confirm firewall
   placement (only `re-orchestrator`/RE analysts/`ida-toolsmith` hold `mcp__ida__*`).

**Invariants for every wave:** one writer per path; the clean-room firewall holds; the advisory-only/
fail-open hook rule holds; nobody edits `settings.json`/`.mcp.json`/`journal.md`/`names.yaml` except the
main session; no commits unless the human asks (branch first if on default).

---

## 9. Depth pass — the "mastery" layer

Every agent and skill should read like a **master of THIS project** wrote it: not just *what* to do, but
the **operating states**, the **decision heuristics**, the **done-criteria**, and the **anti-patterns**.

> **Anti-bloat rule (load-bearing):** ADD concrete, project-specific depth; never pad with generic filler,
> never dilute the firewall language. Terse and load-bearing — a master is concise. Target a worker at
> ~80–140 lines, an orchestrator at ~120–200.

### The two north stars (every role names how it serves at least one)
- **N1 — Clean-room RE of the ENTIRE legacy client** (`doida.exe`) via IDA Pro 9.3 + the MCP (static
  *and* the `?ext=dbg` debugger) + IDAPython, producing neutral committed specs. Total coverage; unbridled
  parallel reverse. *Static forms the hypothesis; the debugger confirms it.*
- **N2 — A faithful 1:1 re-creation** of the entire client, re-implemented fresh in .NET 10/C# and ported
  to Godot 4.6.3. Fidelity is the measure of success. When in doubt, match the original.

### Enrichment dimensions (add where a body lacks them)
1. **Operating states / the loop** (Analyst: preflight → scope → query → describe → *confirm via debugger*
   → record → escalate-or-done. Engineer: read spec → model data → implement zero-alloc → test →
   self-review citations → hand to reviewer. Orchestrator: intake → decompose → gated fan-out → reconcile
   → report.)
2. **Decision heuristics** — concrete "when X, do Y" rules unique to the role.
3. **Project mastery / gotchas** — the specific recovered facts and traps (per-family anchors below).
4. **Done-criteria & self-check** — a short `Done when:` checklist.
5. **Anti-patterns** — named failure modes ("never …").
6. **Hand-off map** — who it receives from / escalates to.
7. **North-star line** — one sentence naming how the role advances N1 and/or N2.

### Per-family mastery anchors
- **RE analysts / IDA skills:** the `?ext=dbg` debugger doctrine — *NEVER `dbg_start`*; pilot the live
  session via `dbg_gpregs`/`dbg_read` (reads through `PAGE_NOACCESS`)/`dbg_add_bp`/`dbg_continue`/
  `dbg_run_to`/`dbg_step_*`; static-hypothesis → debugger-confirm; **massively parallel reads + parallel
  IDB writes (no cap; retry on conflict)**; STOP if MCP down/wrong DB; neutral prose, addresses dirty-only.
- **`spec-author`:** rewrite-never-copy; neutrality self-scrub (strip every `sub_`/`loc_`/`_DWORD`/
  `__thiscall`/mangled/address); validate (`opcode-catalog` schema; packet `size:` == Σ field widths);
  journal the promotion. *N1→N2 bridge.*
- **RE gate chain (recover→confirm→port) + confidence ladder:** G0 `re-brainstorm` (attack plan) →
  G1 recover static into `_dirty/` → **G2 confirm end-to-end on the `?ext=dbg` debugger** (`re-validator`;
  every load-bearing fact; static is only a hypothesis until confirmed; never `dbg_start`) → G3
  `spec-author` promotes a neutral spec → G4 `re-handoff` STAMPs it implementation-ready → C# porting
  consumes it. A fact's confidence climbs *static-hypothesis → debugger/capture-confirmed → spec-promoted
  → implementation-ready*; `csharp-port-orchestrator` builds ONLY from implementation-ready specs and
  routes any static-only load-bearing fact back to G2. The `ida-mcp-connect` toolbox maps which
  `mcp__ida__*` tool serves each angle. **IDA is the strict truth; the specs + toolbox are the derived help.**
- **C# porting (00→04 + Tools):** opcodes `(major<<16)|minor`; 8-byte frame `[u32 size][u16 major][u16
  minor]`; rolling XOR/ROL cipher in-place on `Span<byte>`; LZ4 raw-block; source-gen opcode→handler
  switch; `[StructLayout(Pack=1)]`+`[InlineArray]`; no managed strings on the wire; the recovered chains
  (terrain `.ted`→`.map`→`bgtexture.txt`→`.dds` global under `map000`; skin `.skn`→`skin.txt`→tex; bind/
  idle `.bnd`/`.mot`; spawns `npc{tag}.arr` 28-byte/`mob{tag}.arr` 20-byte; collision `.sod` 2D XZ
  ray-parity); CP949; Parsers carry no rendering dep, Mapping is the only glTF/PNG bridge; Domain 100%
  deterministic + headless-testable, Application orchestrates only. **N2: byte-exact wire parity, faithful
  asset reproduction, behavior parity.** Pulls IDA-validated facts only via committed specs; routes gaps
  back to RE; never invents.
- **Godot (05):** passive rendering, zero authority; consume `Client.Application` channels + route input
  as use-case intents; pitfalls (`.tscn` script = property line; `global::Godot.*` collisions; never
  `GltfDocument.AppendFromBuffer` → build `ArrayMesh`; world negates Z, mesh `.skn` negates X; cells
  1024/65×65/spacing 16); the csproj seam (`EnableDynamicLoading`, ProjectReference to `Application`/
  `Assets.Mapping`); open debts (skinning explodes, NPC fallback-Y race, `EnvironmentNode` too dark, water
  unwired); verify via headless console + windowed screenshot. **N2: pixel-faithful 1:1 visuals.**
- **Docs/Tooling (O5):** documentation describes what IDA/specs/code already prove (never asserts truth);
  firewall-neutral prose (no decompiler artifacts, no copyrighted bytes); the C# `Tools/` projects build
  green on the DAG; Python is std-lib-first with the advisory-hook discipline (`exit 0`, fail-open);
  kit-meta follows THIS KIT.md; `settings.json` stays main-session-owned. **N1+N2: keeps the map honest
  and the tooling sharp.**
- **Reviewers / quality:** the exact invariant each guards; PASS/FAIL with `file:line`; separate BLOCKER
  from advisory; never edit source to make a check pass.
- **Orchestrators:** extremely-detailed atomic per-worker briefs; the file-ownership ledger; parallel
  fan-out (same worker type allowed N× at once); unbridled IDA fan-out (O2); a gate between waves;
  reconcile into ONE rolled-up result; two-levels-of-orchestration max.
