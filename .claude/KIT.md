# `.claude/` Kit — authoritative design & operating doctrine

> Single source of truth for the Martial Heroes Claude Code kit: the orchestrator fleet, the
> model/effort policy, the agent↔skill linking fabric, and the skill/hook plans. Worker agents
> (`agent-author`, `skill-author`, `hook-author`) READ THIS before authoring. Humans read it to
> understand how the kit is wired. Keep it current when the fleet changes.

Companion docs: `CLAUDE.md` (project onboarding + tooling inventory), `Docs/CAMPAIGN_TEMPLATE.md`
(the three command tiers + concurrency), `PRESERVATION_AND_ARCHITECTURE.md` (the blueprint).

---

## Ground-Truth Doctrine — the spine every agent/skill/hook must reflect

This project has **one** ground truth, and the whole kit exists to serve it. Thread this into every
body you author or refine — concise and load-bearing, obeying the §9 anti-bloat rule. This is the same
doctrine as `CLAUDE.md` "Source of truth — the Ground-Truth Doctrine"; keep them in lockstep.

1. **IDA / `doida.exe` is the single absolute truth** for the original's behavior, data, and layout.
   Static analysis forms the hypothesis; the **`?ext=dbg` live debugger confirms it against ground
   truth** (never `dbg_start` — pilot the maintainer's live session). Open or disputed binary facts are
   settled **only** in IDA — never from memory, analogy, or guesswork. MCP down / wrong DB ⇒ **STOP,
   never fabricate.** The reverse runs **unbridled** (parallel reads + parallel IDB writes; retry on
   conflict).
2. **`Docs/RE/` specs are the committed derived truth** — `formats/`, `packets/`, `structs/`, `specs/`,
   `opcodes.md` — the rewritten, firewall-clean record of what IDA proved, and the **only** thing
   implementation reads. Binary vs. spec conflict ⇒ **the binary wins, the spec is corrected + journaled.**
   These four trees are the load-bearing, hyper-important knowledge base.
3. **C# / Godot are measured against (1) and (2), never the reverse** — fidelity is the measure of
   success; the code is never its own truth. **Exception (pixels only):** the official
   screenshots/captures are the visual oracle and **oracle > spec** for how a scene *looks*
   (CAMPAIGN 9c/12).

**Per-room application** (what each family's bodies must say): RE analysts → confirm in IDA, debugger
over static, STOP-don't-fabricate, neutral prose to `_dirty/` only. Spec-authors → rewrite-never-copy,
the spec is downstream's only truth, binary-wins-on-conflict. Engineers → read **only** clean specs,
cite every constant (`// spec: …`), never invent a missing fact — escalate to RE. Godot → specs govern
behavior, the captures oracle governs pixels. Reviewers/guards → enforce that nothing claims truth
without an IDA/spec basis.

---

## 0. Verified Claude Code schema (2026) — what is REAL

Confirmed against the official docs (`code.claude.com/docs/en/{sub-agents,skills,hooks}`). Do **not**
invent fields the blueprint imagined; these are the real ones.

### Agent frontmatter (`.claude/agents/*.md`)
| Field | Values | Use here |
|---|---|---|
| `name` (req) | lowercase-hyphens, == filename stem | agent id `@name` |
| `description` (req) | prose; lead `Use PROACTIVELY…` / `MUST BE USED…` | **drives auto-delegation** |
| `model` | `opus` `sonnet` `haiku` `fable` / full id / `inherit` | **§1 policy** |
| `effort` | `low` `medium` `high` `xhigh` `max` (model-dependent) | **§1 policy** — overrides session effort while the agent runs |
| `tools` | allowlist; comma/space/YAML-list; `Bash(dotnet *)` prefix-scoping; `Agent(a,b)` | minimal surface; `Agent(...)` roster |
| `skills` | comma/list of skill names | **preload** full SKILL.md into the agent at startup — the agent↔skill link |
| `disallowedTools` | denylist | rarely needed |
| `color` | red blue green yellow purple orange pink cyan | cosmetic grouping |
| `permissionMode`,`maxTurns`,`memory`,`isolation`,`background` | — | not used unless a role needs it |

**Two facts that shape our design:**
1. `effort` **is supported per-agent** → we tier effort, not just model.
2. `skills:` **preloads** a skill's body into the agent's context at spawn → this is the real
   "link an agent to its procedure so you never re-explain it" mechanism.
3. `tools: Agent(x, y)` **hard-restricts** which subagents can be spawned **only when the agent runs as
   the main thread** (`claude --agent`). In a Tier-2 subagent the parenthesized list is *ignored*.
   → So an orchestrator's roster is enforced by its **BODY** (the explicit "your team" list), and the
   `Agent(...)` list is written too (documents intent + enforces if ever run as main). Both, always.

### Skill frontmatter (`.claude/skills/<name>/SKILL.md`)
`name`, `description` (keyworded, when-to-use first), `allowed-tools` (**hyphenated**, pre-approve —
NOT `tools`), `disallowed-tools`, `model`, `effort`, `paths` (globs → auto-activate on matching files),
`disable-model-invocation` (true ⇒ user-only `/cmd`), `user-invocable` (false ⇒ Claude-only background
knowledge), `context: fork` + `agent:` (run skill as a forked subagent), `argument-hint`, `arguments`.
Reference bundled scripts via `${CLAUDE_SKILL_DIR}/scripts/<file>`. Keep `SKILL.md` < ~500 lines.

### Hooks (`.claude/hooks/*.py` + `settings.json`)
Advisory-only contract (this project): **`exit 0` always**; emit `{"systemMessage": …}` (visible
nudge) or `{"hookSpecificOutput":{"hookEventName":…,"additionalContext":…}}` (inject context, esp.
`SessionStart`/`UserPromptSubmit`). **Never** `exit 2`, never `decision:block`, never
`permissionDecision:deny/ask`. Wrap `main()` in try/except → `h.fail_open(exc)`. Std-lib + `_hooklib`
only. Events we use: `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `Stop`,
`SubagentStop`, `PreCompact`. New events now available to us: `SubagentStart`, `SessionEnd`,
`PostToolUseFailure`.

---

## 1. Model + Effort policy (applies to EVERY agent)

Every agent declares an explicit `model:` and `effort:`. No agent is left implicit. Two models only
(per the maintainer): **`opus`** (judgement) and **`sonnet`** (execution) — plus the existing
`haiku`/`sonnet` mechanical workers stay as-is unless listed. Effort tiers the *thinking budget*.

| Tier | `model` | `effort` | Who |
|---|---|---|---|
| **Orchestrators** | `opus` | `high` | all `*-orchestrator` (Tier-2) |
| **Judgement / clean-room-risk** | `opus` | `high` | RE analysts (`re-*-analyst`, `re-analyst`*, `ida-script-author`†), spec-authors (`protocol-spec-author`, `asset-spec-author`), high-risk engineers (`network-crypto-`, `network-protocol-`, `domain-`, `application-`, `godot-skinning-specialist`), meta-authors (`agent-/skill-/hook-author`), `clean-room-auditor`, `perf-reviewer` |
| **Precision execution** | `sonnet` | `high` | `assets-parser-engineer`, `assets-mapping-engineer`, `dotnet-engineer`*, `csharp-modernizer`*, `csharp-reviewer`, `test-engineer` |
| **Standard execution** | `sonnet` | `medium` | `kernel-`, `network-abstractions-`, `network-transport-`, `assets-vfs-`, `client-infrastructure-`, `godot-presentation-`, `godot-ui-`, `godot-input-`, `godot-shader-specialist`*, `data-tables-engineer`*, `vfs-data-analyst`, `architecture-guardian`, `build-doctor`, `preservation-archivist`, `godot-render-reviewer`, `godot-mcp-operator`, `tooling-auditor` |

`*` = new agent (§3).  `†` `ida-script-author` keeps `model: sonnet` historically but is judgement-ish;
set it `sonnet` + `effort: high` (toolsmith precision) — the one exception to the opus rule in its row.
`re-ida-annotator` stays `sonnet` + `effort: medium` (mechanical applier). The two Campaign-2
orchestrators are already `opus`; add `effort: high`.

**Why tier effort, not just model:** a `sonnet`+`medium` mechanical engineer is fast and cheap on
boilerplate; a `sonnet`+`high` precision engineer thinks harder on binary layouts; an `opus`+`high`
orchestrator/analyst gets the deepest reasoning where a wrong call is expensive. Under ultracode the
session is `xhigh`; an explicit `medium` on a mechanical worker is an intentional, cost-saving
down-shift — that is desired.

---

## 2. Orchestrator fleet (the dispatch layer)

**Doctrine (from `CLAUDE.md` + `CAMPAIGN_TEMPLATE.md` §2–3):** Tier-1 main session → **Tier-2
Orchestrator-Agent** → Tier-3 single-deliverable worker. Two levels max; a Tier-2 never spawns another
Tier-2. One writer per path per wave (file-ownership ledger). Build/test/firewall gate between waves.
Orchestrators are `opus`+`effort: high`, hold the `Agent` tool, and give **extremely detailed,
atomic objectives** to workers (the maintainer's explicit ask: the orchestrator does the briefing so
the human never re-explains).

### Tier-0 — the campaign planner (`agent-campaign-plan-orchestrator`)

Above the dispatch layer sits a **planner**, not a dispatcher. `agent-campaign-plan-orchestrator`
(model `opus`, effort `high`, skill `plan-campaign`) is the kit's **Phase-0 specialist**: it works
*with* Claude Code plan mode to take a user mandate, **deeply understand and REFORMULATE it** (optimize,
restructure, make exhaustive — with the user in the loop), scout read-only context, decompose it into
the `CAMPAIGN_TEMPLATE` Phase/Objective/Sub-objective hierarchy, and **pre-wire the routing** below:
which Tier-2 lane captain + which agents + which skills each phase needs. Its deliverable is an
approve-ready **campaign plan** (incl. a paste-ready ROADMAP `# CYCLE N` section).

**It PLANS the routing; it does NOT execute it.** Two-levels-max is preserved because the planner
*names* the lane captains for **Tier-1 (the main session)** to invoke **after the user approves the
plan** — the planner never spawns a Tier-2/lane orchestrator, and never calls `ExitPlanMode` (the main
session presents the plan). The planner may spawn only **READ-ONLY Tier-3 scouts** for context
(`Explore`, `Plan`, `re-analyst` READONLY-IDA, `vfs-data-analyst` READONLY-VFS). The "russian-doll"
(planner → lane captain → agent → skill) lives in the **plan + the linking fabric**, never in a 3-deep
runtime spawn tree.

**Campaign routing map** (what the plan pre-wires; all captains already exist — the planner reuses, it
does not mint new ones):

| Lane | Tier-2 captain(s) | Representative Tier-3 workers | Key skills |
|---|---|---|---|
| **RE (dirty→spec)** | `re-cleanroom-orchestrator` | `re-*-analyst`, `re-struct-cartographer`, `re-analyst`, `ida-script-author`, `vfs-data-analyst` → bridge `protocol-spec-author`, `asset-spec-author` | `ida-mcp-connect`, `ida-*`, `re-promote` |
| **IDA (IDB legibility)** | `re-comprehension-orchestrator` + `re-annotation-orchestrator` | `re-ida-annotator` | `ida-annotate-batch`, `ida-naming-sync` |
| **C#** | `network-stack-` (02) · `assets-pipeline-` (03) · `client-core-orchestrator` (04) | `network-*`, `assets-*`, `domain-`, `application-`, `client-infrastructure-`, `data-tables-`, `dotnet-engineer`, `csharp-modernizer/reviewer`, `test-engineer` | `dotnet-build-test`, `packet-codegen`, `vfs-inspect` |
| **Godot** | `godot-client-orchestrator` (05) | `godot-presentation/ui/input-engineer`, `godot-skinning/shader-specialist`, `godot-render-reviewer`, `godot-mcp-operator` | `godot-run-headless`, `godot-screenshot`, `godot-fidelity-check`, `asset-chain-trace` |
| **Quality / Kit** | `quality-gate-orchestrator` · `tooling-orchestrator` | reviewers / meta-authors | `clean-room-firewall-check` |

**Every orchestrator body MUST contain a `## Your team (roster)` section** that names each Tier-3
worker it dispatches, that worker's one-line contract, and which lane/path it owns — so dispatch is
unambiguous and harmonious. The `tools: Agent(...)` list mirrors that roster.

### Existing (refine only — keep semantics; add `effort: high`, tighten roster section)
- `re-comprehension-orchestrator` (Campaign-2, READONLY comprehension)
- `re-annotation-orchestrator` (Campaign-2, parallel IDB WRITE — fans out writers concurrently, no serialization cap)

### New (7) — one per real work lane. `model: opus`, `effort: high`, `tools: Agent(<roster>), Read, Write, Grep, Glob[, scoped Bash]`.

| Orchestrator | `description` trigger (MUST BE USED for…) | Roster (Tier-3 workers) | Preload `skills:` |
|---|---|---|---|
| **`re-cleanroom-orchestrator`** | a general dirty→spec RE objective on `Main.exe`/`doida.exe` that spans several analysts then promotion (recover a subsystem end-to-end: find → describe → promote to a clean spec). NOT the Campaign-2 annotation campaign. | `re-static-analyst`, `re-protocol-analyst`, `re-crypto-analyst`, `re-struct-cartographer`, `re-asset-format-analyst`, `re-animation-analyst`, `re-analyst`, `ida-script-author`, `vfs-data-analyst` → bridge: `protocol-spec-author`, `asset-spec-author` | `ida-mcp-connect`, `re-promote` |
| **`network-stack-orchestrator`** | a multi-project objective across the Network layer (02): abstractions + packet structs/opcodes + cipher + Pipelines framing, with perf/clean review. | `network-abstractions-engineer`, `network-protocol-engineer`, `network-crypto-engineer`, `network-transport-engineer`, `perf-reviewer`, `csharp-reviewer`, `test-engineer` | `dotnet-build-test` |
| **`assets-pipeline-orchestrator`** | a multi-project objective across Storage.Assets (03): VFS + binary parsers + glTF/PNG mapping + CP949 data tables, end-to-end from `.pak` bytes to modern formats. | `assets-vfs-engineer`, `assets-parser-engineer`, `assets-mapping-engineer`, `data-tables-engineer`, `vfs-data-analyst`, `asset-spec-author`, `test-engineer` | `dotnet-build-test`, `vfs-inspect` |
| **`client-core-orchestrator`** | a multi-project objective across Client.Core (04): deterministic Domain rules + Application use-cases/handlers/event-buses + local Infrastructure, headless-testable. | `domain-engineer`, `application-engineer`, `client-infrastructure-engineer`, `dotnet-engineer`, `csharp-modernizer`, `csharp-reviewer`, `test-engineer` | `dotnet-build-test` |
| **`godot-client-orchestrator`** | a multi-facet objective in the Godot client (05): 3D world + HUD/menus + input/camera + skinning + shaders/VFX, with eyes-on render review. | `godot-presentation-engineer`, `godot-ui-engineer`, `godot-input-engineer`, `godot-skinning-specialist`, `godot-shader-specialist`, `godot-render-reviewer`, `godot-mcp-operator` | `godot-run-headless`, `godot-screenshot`, `godot-mcp-connect` |
| **`tooling-orchestrator`** | a multi-file objective on the `.claude/` kit itself: design/extend/refine the agents, skills, and hooks fleet, then audit consistency. (This very campaign's shape.) | `agent-author`, `skill-author`, `hook-author`, `tooling-auditor` | — |
| **`quality-gate-orchestrator`** | a cross-cutting validation pass before a commit/milestone: clean-room firewall + layer DAG + C#/perf review + build + provenance, rolled into one PASS/FAIL verdict. | `clean-room-auditor`, `architecture-guardian`, `csharp-reviewer`, `perf-reviewer`, `build-doctor`, `preservation-archivist`, `tooling-auditor`, `godot-render-reviewer` | `clean-room-firewall-check` |

**Disjoint, sharp descriptions** so the main session routes unambiguously: each says *MUST BE USED for
[its lane] when the objective spans multiple [workers]*, and *for a single-deliverable task, delegate
directly to the worker instead of this orchestrator*. That keeps single tasks going straight to a
worker and only multi-lane objectives going through an orchestrator.

**Firewall placement of orchestrators:** `re-cleanroom-orchestrator` is dirty-room (holds
`mcp__ida__*`, READONLY, **massively parallel analysts (no sub-wave cap)**, writes only `_dirty/`, promotion only via its spec-author
workers). All others are clean-room/neutral (no IDA). `quality-gate` and `tooling` are read-mostly.

---

## 3. New worker agents (5)

| Agent | Room | `model`+`effort` | `tools` | `skills:` | One-job description |
|---|---|---|---|---|---|
| **`dotnet-engineer`** | clean-room engineer (generalist) | sonnet · high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `wire-references` | Cross-layer .NET tasks that don't belong to one project's specialist: small features touching 2+ core projects, csproj/reference wiring, slnx upkeep, glue code. Respects the downward DAG; never `using Godot;` below 05. |
| **`csharp-modernizer`** | quality (read+refactor) | sonnet · high | Read, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test` | Refactor existing core C# to C# 14 / .NET 10 idioms — nullability, collection expressions, `readonly record struct` IDs, primary ctors, `ref struct`/`Span` hot-path hygiene — WITHOUT changing behavior. Pairs with `csharp-reviewer` (it flags, this fixes). |
| **`data-tables-engineer`** | clean-room engineer | sonnet · medium | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `vfs-inspect`, `vfs-data-format`, `dotnet-build-test` | Turn legacy CP949 text tables (`items.csv`, `skin.txt`, `actormotion.txt`, `bgtexture.txt`, …) into typed C# catalogues/loaders in Assets/Domain, from the committed `Docs/RE/formats/` spec. CP949 provider always registered. Leans on `vfs-data-analyst` for any unspec'd format. |
| **`godot-shader-specialist`** | presentation (layer 05) | sonnet · medium | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(godot *) | `godot-run-headless`, `godot-screenshot` | Godot 4.6 shaders / post-process / VFX — water, atmosphere/lighting (the too-dark `EnvironmentNode`), combat FX, material tuning. Passive presentation only; verifies via the headless-screenshot loop. Complements presentation/ui/input/skinning. |
| **`re-analyst`** | dirty-room analyst (generalist) | opus · medium | mcp__ida__*, Read, Write, Bash(claude mcp *) | `ida-mcp-connect`, `ida-xref-map`, `ida-py` | Small, one-off IDA questions that don't warrant a specialist or a full cluster: a single xref walk, confirm/refute one hypothesis, identify one function's role. Writes only `_dirty/`; neutral prose; STOP if MCP down. Escalates big jobs to the specialist analyst. |

All five obey their room's rules verbatim (firewall, layer DAG, zero-alloc/CP949 where relevant) and
restate them in their body.

---

## 4. Agent↔skill linking fabric

Three linking mechanisms, used together:

1. **`skills:` preload (tight)** — inject the 1–2 procedures an agent *cannot do its job without*, so
   it never rediscovers them. Per-agent preload sets (keep tight — every preloaded skill costs context
   on each spawn):

   - **Godot engineers** → `godot-run-headless`, `godot-screenshot` (presentation/ui/skinning/shader);
     `godot-coordinate-check` for input/skinning.
   - **RE analysts** → `ida-mcp-connect` + their one core skill (`re-protocol-analyst`→`ida-opcode-map`;
     `re-crypto-analyst`→`ida-crypto-hunt`; `re-static-analyst`→`ida-recon`;
     `re-struct-cartographer`→`ida-struct-recovery`; `re-asset-format-analyst`→`ida-batch-analyze`;
     `re-animation-analyst`→`ida-data-flow`; `re-analyst`→`ida-xref-map`,`ida-py`;
     `ida-script-author`→`ida-py`).
   - **Spec-authors** → `re-promote` (`protocol-spec-author` also `opcode-catalog`;
     `asset-spec-author` also `asset-format-doc`).
   - **Network.Protocol engineer** → `packet-codegen`, `opcode-catalog`.
   - **Assets.Parsers engineer** → `asset-format-doc`. **Assets.Vfs** → `vfs-inspect`.
     **data-tables-engineer** → `vfs-data-format`, `vfs-inspect`.
   - **Reviewers/quality** → their enforcement skill (`clean-room-auditor`→`clean-room-audit`;
     `architecture-guardian`→`wire-references`; `test-engineer`→`add-test-project`;
     `preservation-archivist`→`re-session-log`,`preservation-readme`;
     `godot-render-reviewer`→`godot-run-headless`,`godot-screenshot`;
     `godot-mcp-operator`→`godot-mcp-connect`).
   - **Most standard engineers** → at most `dotnet-build-test` (or none; it's discoverable on demand).
   - **Meta-authors / orchestrators** → none beyond §2's table.

2. **Body "Paired skills" section (broad)** — the agent's prose names the wider set of skills it leans
   on and the hand-off, even those not preloaded. (Already the house style; keep it, expand it.)

3. **`paths:` auto-activation on skills (§5)** — domain *knowledge* skills auto-load when the model
   edits matching files, so the conventions are present without anyone invoking them.

Preloaded skills must NOT have `disable-model-invocation: true` (ours don't). Verify with
`tooling-auditor` that every name in a `skills:` list resolves to a real skill dir.

---

## 5. Skills plan (modernize all + add knowledge layer)

**A. Modernize the 46 existing** (passe complète): confirm `name`/`description` lead with the trigger;
set explicit `model:`+`effort:` where it sharpens behavior (mechanical → `sonnet`/`medium`,
judgement → `opus`/`high`, leave fast ones `haiku`); confirm `allowed-tools` is the minimal hyphenated
surface and `Bash(...)` is prefix-scoped; verify bundled scripts resolve via `${CLAUDE_SKILL_DIR}`;
firewall stance intact (dirty → `_dirty/` only; clean-side refuses `_dirty/`). Add `paths:` to the
clean-side action skills where a file-path trigger helps (e.g. `packet-codegen` → `Docs/RE/packets/**`,
`02.Network.Layer/**`).

**B. NEW knowledge skills (4)** — `user-invocable: false`, `disable-model-invocation` left default so
Claude auto-loads them; `paths:` where the domain is file-bound. These hold the *methodology/conventions*
that today live only in prose, so they surface automatically:

| Skill | `paths:` | Content (conventions, NOT a procedure) |
|---|---|---|
| `dotnet-csharp14` | `01.Infrastructure.Shared/**`, `02.Network.Layer/**`, `03.Storage.Assets/**`, `04.Client.Core/**` | C# 14 / .NET 10 house rules: `readonly record struct` IDs, `[StructLayout(Pack=1)]`+`[InlineArray]` wire/asset structs, zero-alloc `Span`/`ReadOnlyMemory` hot paths (no LINQ/closures/boxing), CP949 provider, source-gen `[LoggerMessage]`, downward-only layer DAG. |
| `godot-engine` | `05.Presentation/**` | Godot 4.6.3-mono pitfalls: `.tscn` script = property line (not header attr); `global::Godot.{Input,Environment,Time}` namespace collisions; never `GltfDocument.AppendFromBuffer` (build `ArrayMesh`); coordinate conventions (world negates Z, mesh `.skn` negates X; cells 1024, 65×65, spacing 16); the headless-verify + screenshot loop; passive-rendering rule (zero game authority). |
| `ida-pro-re` | — (RE is not file-path-bound) | IDA Pro 9.3 + MCP methodology for `Main.exe`/`doida.exe`: the `?ext=dbg` endpoint, static-forms-hypothesis / debugger-confirms doctrine, **unbridled fan-out (parallel read analysts + parallel IDB writes, no caps)**, and the clean-room firewall (dirty→`_dirty/` only, never transcribe Hex-Rays, neutral prose, STOP if MCP down). `user-invocable: false`. |
| `martial-heroes-domain` | — | Game/技术 knowledge that recurs: opcodes are `(major<<16)|minor`, 8-byte frame header `[u32 size][u16 major][u16 minor]`, rolling XOR/ROL cipher shape, LZ4 raw-block, all text CP949, the recovered asset chains (terrain `.ted`→`.map`→`bgtexture.txt`→`.dds`; skin `.skn`→`skin.txt`→tex; bind/idle `.bnd`/`.mot`; spawns `.arr`; collision `.sod`). `user-invocable: false`. Mirrors the recovered facts in `CLAUDE.md`/specs — points at them, never duplicates copyrighted data. |

Knowledge skills are **neutral documentation** — they cite the committed specs, never paste decompiler
output or copyrighted bytes.

---

## 6. Hooks plan (refine + consolidate + meta-guards)

**A. Refine the 24 existing** — confirm each is advisory-only/fail-open, `import _hooklib as h`, one
concern, low false-positive. No behavior regressions.

**B. Consolidate** the PreCompact double-fire: `precompact_note.py` and `memory_persist_nudge.py` both
fire on `PreCompact`. Fold the clean-room-flush line into `memory_persist_nudge` (it already branches on
event) and **remove `precompact_note.py` from the PreCompact wiring** (or delete the file if fully
subsumed). One reminder at compaction, not two.

**C. NEW meta-hooks (advisory)** that keep THIS kit self-consistent — high value given the kit's size:

| Hook | Event / matcher | Advises when… |
|---|---|---|
| `agent_md_guard.py` | PostToolUse · `Write\|Edit\|MultiEdit` (file under `.claude/agents/`) | a written agent `.md` is missing `model:`/`effort:`, uses an invalid model (not `opus`/`sonnet`/`haiku`/`fable`/id/`inherit`), uses a bogus field (`allowed-tools:` on an agent, `claude-3-*` id), or an orchestrator lacks a roster — points at §1/§2 of this file. |
| `skill_md_guard.py` | PostToolUse · `Write\|Edit\|MultiEdit` (file under `.claude/skills/`) | a SKILL.md uses `tools:` instead of `allowed-tools:`, references a `scripts/` file that doesn't exist, or omits a when-to-use trigger in `description`. |
| `hook_advisory_guard.py` | PostToolUse · `Write\|Edit\|MultiEdit` (file under `.claude/hooks/`, `*.py`) | a hook contains `sys.exit(2)`, a `permissionDecision` of `deny`/`ask`, or `"decision": "block"` — i.e. it could BLOCK, violating the advisory-only rule. The one guard that protects the guard rules. |
| `settings_wiring_nudge.py` | PostToolUse · `Write\|Edit\|MultiEdit` (file == `.claude/settings.json`) | reminds to verify every wired hook path exists and every hook file is wired (run `tooling-auditor`). |

**D. Optionally** a `SubagentStart` primer that reminds a freshly-spawned dirty-room analyst of the
firewall + MCP status — only if it proves useful; do not over-instrument.

All new hooks go through `_hooklib` (extend it, §7) and are wired by the **orchestrator/me**, never by
`hook-author` (it only writes the `.py` + reports the exact `settings.json` stanza).

---

## 7. `_hooklib.py` additions (foundation — done first)

Add (std-lib only, fail-open, simple returns):
- `is_agent_md(path)` / `is_skill_md(path)` / `is_hook_py(path)` / `is_claude_settings(path)` — path
  classifiers for `.claude/` files.
- `parse_frontmatter(text)` → `dict` of the leading `---`…`---` YAML (tiny hand parser; std-lib has no
  yaml — parse `key: value` + simple lists; return `{}` on anything fancy). Used by the meta-guards.
- `agent_frontmatter_issues(text)` → `list[str]` of advisory problems (missing model/effort, bad model
  value, `allowed-tools` present, `claude-3-*` id).
- `skill_frontmatter_issues(text)` → `list[str]` (uses `tools:` not `allowed-tools:`, missing
  description trigger).
- `hook_can_block(text)` → `bool` (regex for `sys.exit(2)` / `"decision"\s*:\s*"block"` /
  `permissionDecision"\s*:\s*"(deny|ask)`).

---

## 8. Execution plan (phased waves; commit per phase, only on request)

1. **Phase 1 — foundations (main session):** this `KIT.md`; extend `_hooklib.py` (§7); update the 3
   meta-authors (`agent-/skill-/hook-author`) to teach `effort:`+`skills:`, the §1 policy, and the §2
   roster doctrine — so they propagate correctness into every file they then touch.
2. **Phase 2 — orchestrators (Workflow · `agent-author` workers):** author the 7 new orchestrators;
   refine the 2 Campaign-2 ones (add `effort: high`, tighten roster section). Disjoint files.
3. **Phase 3 — worker agents (Workflow · `agent-author`):** apply `model`+`effort`+`skills` to all
   existing workers; sharpen delegation descriptions; create the 5 new agents (§3). Disjoint files.
4. **Phase 4 — skills (Workflow · `skill-author`):** modernize the 46; add the 4 knowledge skills;
   add `paths:` triggers; wire `${CLAUDE_SKILL_DIR}`. Disjoint dirs.
5. **Phase 5 — hooks (Workflow · `hook-author`) + wiring (me):** refine + consolidate + the 4
   meta-guards; then I wire `settings.json`.
6. **Phase 6 — integration & validation (me + reviewers):** update `CLAUDE.md` tooling map + a
   `.claude/README.md` index; refresh auto-memory; run `tooling-auditor` + `csharp-reviewer` +
   `architecture-guardian`; `python -c ast.parse` every hook; commit per phase on the maintainer's go.

**Invariants for every wave:** one writer per path; meta-authors write only their family
(`agents/`, `skills/`, `hooks/`); nobody edits `settings.json`/`.mcp.json`/`journal.md`/`names.yaml`
except me; the clean-room firewall and the advisory-only/fail-open hook rules hold; Campaign-2's two
orchestrators are refined in place, never broken (a second session may be using them).

---

## 9. Depth pass — the "mastery" layer (enrichment dimensions)

Every agent and skill should read like a **master of THIS project** wrote it: not just *what* to do,
but the **states** of the work, the **decision heuristics**, the project's **hard-won mastery**, the
**done-criteria**, and the **anti-patterns**. Enrich each body with the dimensions below **where
missing**.

> **Anti-bloat rule (load-bearing):** these bodies are already good. ADD concrete, project-specific
> depth; never pad with generic filler, never restructure or weaken existing strong content, never
> dilute the firewall language. If a dimension is already covered, leave it. Terse and load-bearing —
> a master is concise. Net additions are focused (typically a short `## Operating states` loop, a few
> decision heuristics, a `Done when:` checklist, an `## Anti-patterns` list, and a one-line north-star
> note), not a rewrite.

### The two north stars (every role names how it serves at least one)
- **N1 — Clean-room RE of the ENTIRE legacy client** (`doida.exe`, with `Main.exe` as historical
  reference) via **IDA Pro 9.3 + the MCP** (static *and* the `?ext=dbg` debugger) + **IDAPython**,
  producing neutral committed specs. **Goal: total coverage — leave no subsystem un-mapped**, and run
  the reverse **unbridled / massively parallel** (no read cap, no one-writer rule on the IDB).
  *Static forms the hypothesis; the debugger confirms it against ground truth.*
- **N2 — A faithful 1:1 re-creation** of the **entire** original game client, re-implemented fresh in .NET 10/C#
  and ported to **Godot 4.6.3**. **Fidelity to the original** — wire protocol, asset chains, visuals,
  behavior — *is the measure of success*. When in doubt, match the original.

### Enrichment dimensions (add where a body lacks them)
1. **Operating states / the loop** — the explicit phases the role cycles through, with each phase's
   entry/exit. (Analyst: preflight → scope → query → describe → *confirm via debugger* → record →
   escalate-or-done. Engineer: read spec → model the data → implement (zero-alloc) → test → self-review
   citations → hand to reviewer. Orchestrator: intake → decompose → gated fan-out → reconcile → report.)
2. **Decision heuristics** — concrete "when X, do Y" rules *unique to the role* (not generic advice).
3. **Project mastery / gotchas** — the specific recovered facts and traps the role hits (per-family
   anchors below).
4. **Done-criteria & self-check** — a short `Done when:` checklist: how the role knows it is finished
   AND correct.
5. **Anti-patterns** — the role's common failure modes, each named ("never …").
6. **Hand-off map** — who it receives from / escalates to (make explicit if implicit).
7. **North-star line** — one sentence naming how the role advances N1 and/or N2.

### Per-family mastery anchors (the concrete facts to weave in)
- **RE analysts / IDA skills:** the **`?ext=dbg` debugger doctrine** — *NEVER call `dbg_start`* (the
  maintainer F9-launches the client; you **pilot the live session** via `dbg_gpregs` / `dbg_read`
  (reads through `PAGE_NOACCESS`) / `dbg_add_bp` / `dbg_continue` / `dbg_run_to` / `dbg_step_*`);
  static-hypothesis → debugger-confirm; IDAPython through the MCP exec tool (its name varies by build —
  discover at runtime); **fan out massively in parallel — read analysts AND IDB writes run unbridled (no `~3` cap, no one-writer rule); retry failed/conflicting calls**; **STOP if MCP down
  or wrong/empty DB**; neutral prose, addresses dirty-only.
- **Spec-authors:** rewrite-never-copy; the neutrality **self-scrub** (strip every
  `sub_`/`loc_`/`_DWORD`/`__thiscall`/mangled/image-range address); validate (`opcode-catalog` schema;
  packet `size:` == Σ field widths); journal the promotion. *N1→N2 bridge.*
- **Network (02):** opcodes `(major<<16)|minor`; 8-byte frame `[u32 size][u16 major][u16 minor]`;
  rolling **XOR/ROL** cipher in-place on `Span<byte>`; LZ4 raw-block; source-gen opcode→handler switch
  with `OnUnhandled` fallback (0/0, 3/1, 3/7, 3/4, 3/6, 3/23); `[StructLayout(Pack=1)]`+`[InlineArray]`;
  no managed strings on the wire. **N2: byte-exact wire parity with the original.**
- **Assets (03) / data-tables:** the recovered chains — terrain `.ted`→`.map`→`bgtexture.txt`→`.dds`
  (global under `map000`); skin `.skn` IdA→`skin.txt`→tex; bind/idle IdB→`.bnd` / `actormotion.txt`
  →`.mot`; spawns `npc{tag}.arr` (28-byte) / `mob{tag}.arr` (20-byte); collision `.sod` (2D XZ
  ray-parity); ground via `.ted` bilinear; CP949 text; Parsers carry **no rendering dep**, Mapping is
  the **only** glTF/PNG bridge; memory-mapped VFS `ReadOnlyMemory<byte>` slices. **N2: faithful asset
  reproduction.**
- **Core (04):** Domain is **100% deterministic**, headless xUnit-testable, references only
  Shared.Kernel; Application **orchestrates only** (use-cases + handlers + `Channels` event buses, no
  game-rule math, no rendering, no transport); Infrastructure = local SQLite/config/macros. **N2:
  behavior parity with the original rules.**
- **Godot (05):** **passive rendering, zero authority** (subscribe to Application channels, route input
  as use-case *intents*); pitfalls (`.tscn` script = property line; `global::Godot.*` collisions; never
  `GltfDocument.AppendFromBuffer` → build `ArrayMesh`; world negates Z, mesh `.skn` negates X; cells
  1024 / 65×65 / spacing 16); the open debts (skinning explodes the mesh, NPC fallback-Y race,
  `EnvironmentNode` too dark, water unwired); verify via the **headless console + windowed screenshot
  autoload** loop. **N2: pixel-faithful 1:1 visuals.**
- **Reviewers / quality:** the exact invariant each guards; PASS/FAIL with `file:line`; separate
  **BLOCKER** from advisory; never edit source to make a check pass.
- **Orchestrators:** extremely-detailed **atomic** per-worker briefs (CONTEXT SOURCE + atomic objective
  + DELIVERABLES + SKILL); the file-ownership ledger; **unbridled IDA fan-out (parallel reads + parallel IDB writes)**; a gate between waves;
  reconcile into **ONE** rolled-up result; two-levels-of-orchestration max.

### New high-value skills (toward the north stars) — authored in the depth pass
- **`ida-debugger-drive`** (N1) — the **live-debugger piloting loop**: *never* `dbg_start`; the
  maintainer F9-launches and you pilot via `dbg_*` (read regs/memory through `PAGE_NOACCESS`, set
  breakpoints, continue, run-to) to capture **ground truth** (registers, memory, packet buffers
  pre/post-cipher) into `_dirty/`. The "debugger confirms the static hypothesis" half of N1. Pairs with
  the static analysts and `ida-crypto-hunt` / `ida-opcode-map`.
- **`godot-fidelity-check`** (N2) — verify the Godot client renders/behaves **1:1** vs the original: the
  headless + screenshot loop, the asset-chain reproduction, the coordinate conventions, checked
  side-by-side against the recovered facts; reports concrete fidelity gaps (visual, coordinate,
  material, behavior).
- **`asset-chain-trace`** (N2) — walk a given asset id through its **recovered mapping chain**
  (terrain / skin / bind / mot / spawn / collision) to the on-disk VFS file, citing the chain spec — a
  concrete reproduction & debugging aid for the 1:1 port.
