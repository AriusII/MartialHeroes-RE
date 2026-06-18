# `.claude/` Kit — authoritative design & operating doctrine

> Single source of truth for the Martial Heroes Claude Code kit after the **3-domain rationalization**:
> the three domain orchestrators, the model/effort policy, the agent↔skill linking fabric, and the
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
only truth, binary-wins-on-conflict. Porting engineers → read **only** clean specs, cite every constant
(`// spec: …`), never invent a missing fact — escalate to the RE domain. Godot → specs govern behavior,
the captures oracle governs pixels. Reviewers/guards → enforce that nothing claims truth without an
IDA/spec basis.

---

## 0. Verified Claude Code schema (2026) — what is REAL

Confirmed against the official docs (`code.claude.com/docs/en/{sub-agents,skills,hooks}`). Do **not**
invent fields; these are the real ones.

### Agent frontmatter (`.claude/agents/*.md`)
| Field | Values | Use here |
|---|---|---|
| `name` (req) | lowercase-hyphens, == filename stem | agent id `@name` |
| `description` (req) | prose; lead `Use PROACTIVELY…` / `MUST BE USED…` | **drives auto-delegation** |
| `model` | `opus` `sonnet` `haiku` / id / `inherit` | **§1 policy** |
| `effort` | `low` `medium` `high` `xhigh` `max` | **§1 policy** — overrides session effort while the agent runs |
| `tools` | allowlist; `Bash(dotnet *)` prefix-scoping; `Agent(a,b)` roster | minimal surface |
| `skills` | comma/list of skill names | **preload** full SKILL.md into the agent at startup |
| `color` | red blue green yellow purple orange pink cyan | cosmetic grouping |

**Two facts that shape our design:** (1) `effort` is per-agent → we tier effort, not just model.
(2) `skills:` **preloads** a skill body into the agent's context at spawn → the real "link an agent to
its procedure" mechanism. (3) `tools: Agent(x,y)` hard-restricts spawnable subagents **only when the
agent runs as the main thread**; in a Tier-2 subagent the list is ignored → so an orchestrator's roster
is enforced by its **BODY** (the explicit "your team" list); write the `Agent(...)` list too. Both, always.

### Skill frontmatter (`.claude/skills/<name>/SKILL.md`)
`name`, `description` (keyworded, when-to-use first), `allowed-tools` (**hyphenated**, pre-approve — NOT
`tools`), `model`, `effort`, `paths` (globs → auto-activate on matching files),
`disable-model-invocation` (true ⇒ user-only `/cmd`), `user-invocable` (false ⇒ Claude-only background
knowledge). Reference bundled scripts via `${CLAUDE_SKILL_DIR}/scripts/<file>`. Keep `SKILL.md` < ~500 lines.

### Hooks (`.claude/hooks/*.py` + `settings.json`)
**Advisory-only contract (this project, non-negotiable): `exit 0` always**; emit `{"systemMessage": …}`
(visible nudge) or `{"hookSpecificOutput":{"hookEventName":…,"additionalContext":…}}` (inject context).
**Never** `exit 2`, never `decision:block`, never `permissionDecision:deny/ask`. Wrap `main()` in
try/except → `h.fail_open(exc)`. Std-lib + `_hooklib` only. Events: `SessionStart`, `UserPromptSubmit`,
`PreToolUse`, `PostToolUse`, `Stop`, `SubagentStop`, `PreCompact`.

---

## 1. Model + Effort policy (applies to EVERY agent)

Every agent declares an explicit `model:` and `effort:`. Two models: **`opus`** (judgement/orchestration/
clean-room-risk) and **`sonnet`** (execution). Effort tiers the thinking budget.

| Tier | `model` | `effort` | Who |
|---|---|---|---|
| **Orchestrators** | `opus` | `high` | the 3 domain orchestrators |
| **Judgement / clean-room-risk** | `opus` | `high` | all RE-domain analysts + `spec-author` + `re-validator`; `requirement-analyst`, `todo-architect`, `plan-reviewer`, `kit-author`; `core-engineer`, `godot-character-specialist`, `code-reviewer` |
| **Precision execution** | `sonnet` | `high` | `ida-toolsmith`, `network-engineer`, `assets-engineer`, `dotnet-foundation-engineer`, `test-engineer` |
| **Standard execution** | `sonnet` | `medium` | `tooling-auditor`, `knowledge-gap-detector`, `godot-world-engineer`, `godot-ui-engineer`, `render-reviewer` |

**Why tier effort:** a `sonnet`+`medium` mechanical worker is fast/cheap on boilerplate; `sonnet`+`high`
thinks harder on binary layouts; `opus`+`high` gets the deepest reasoning where a wrong call is expensive
(orchestration, the firewall bridge, domain rules, the skinning problem).

---

## 2. The three domain orchestrators (the dispatch layer)

**Doctrine:** Tier-1 main session → **Tier-2 domain orchestrator** → Tier-3 single-deliverable worker.
A domain orchestrator owns one of the three domains, fans out its own workers, holds a file-ownership
ledger (one writer per path per wave), gates between waves, and reports **one** rolled-up result. It
gives **extremely detailed, atomic** per-worker briefs (CONTEXT SOURCE + the one objective + DELIVERABLES
+ the SKILL) so the human never re-explains and no worker guesses. Orchestrators are `opus`+`high`, hold
the `Agent` tool, and each body MUST contain a `## Your team (roster)` section naming every Tier-3 worker,
its one-line contract, and the path/lane it owns. The `tools: Agent(...)` list mirrors that roster.

> **For a single-deliverable task, the main session delegates STRAIGHT to the worker** — route a domain
> orchestrator only when the objective spans several workers in that domain. Each orchestrator's
> `description` says exactly this so the main session routes unambiguously.

### Domain 1 — `planning-orchestrator` (Planning & Analysis + kit-meta)
MUST BE USED for a multi-step planning objective: reformulate a mandate, decompose it into a hierarchical
TODO/roadmap tree, map dependencies/risk/scope — **and** for designing/refining the `.claude/` kit itself
(agents, skills, hooks). It plans and structures work; it does not implement game code.
Roster: `requirement-analyst`, `todo-architect`, `knowledge-gap-detector`, `plan-reviewer`, `kit-author`,
`tooling-auditor`. Preload `skills:` `plan-campaign`.

### Domain 2 — `re-orchestrator` (Reverse Engineering)
MUST BE USED for a clean-room RE objective on `doida.exe`/`Main.exe` that spans several analysts and ends
in a committed neutral spec (or an IDB-legibility annotation pass). Owns the **dirty side + spec bridge**
of the firewall. Roster: `re-function-analyst`, `re-protocol-analyst`, `re-crypto-analyst`,
`re-struct-analyst`, `re-asset-format-analyst`, `ida-toolsmith`, `spec-author`, `re-validator`.
Preload `skills:` `ida-mcp-connect`, `re-promote`. Holds `mcp__ida__*` (READONLY at the orchestrator
level; IDB writes only via `ida-toolsmith`).

### Domain 3 — `port-orchestrator` (C#/Godot Porting + validation)
MUST BE USED for a multi-project porting objective across the .NET 10 core (layers 01–04) and/or the
Godot client (05): consume committed specs → faithful C#/Godot, with build/test/review. Owns the
**clean-room engineer side** (NO IDA). Roster: `network-engineer`, `assets-engineer`, `core-engineer`,
`dotnet-foundation-engineer`, `godot-world-engineer`, `godot-ui-engineer`, `godot-character-specialist`,
`code-reviewer`, `test-engineer`, `render-reviewer`. Preload `skills:` `dotnet-build-test`.

**Firewall placement of the orchestrators:** `re-orchestrator` is dirty-room (holds `mcp__ida__*`,
READONLY at its own level, massively-parallel analysts, writes only `_dirty/`, promotion only via its
`spec-author` worker, IDB mutation only via `ida-toolsmith`). `planning-orchestrator` and
`port-orchestrator` are clean/neutral (no IDA). Two levels of orchestration max — a domain orchestrator
never spawns another orchestrator.

---

## 3. Worker roster (24 Tier-3 workers)

### Domain 1 — Planning & Analysis (6)
| Agent | `model`·`effort` | `tools` | `skills:` | One-job |
|---|---|---|---|---|
| `requirement-analyst` | opus·high | Read, Grep, Glob | — | Reformulate the mandate, clarify with the human, set scope boundaries, surface risks. |
| `todo-architect` | opus·high | Read, Grep, Glob | `plan-campaign` | Functional+technical decomposition into a hierarchical TODO tree; dependency + milestone mapping. |
| `knowledge-gap-detector` | sonnet·medium | Read, Grep, Glob | — | Find what RE/spec knowledge is missing before porting; route gaps to the RE domain. |
| `plan-reviewer` | opus·high | Read, Grep, Glob | `preservation` | Validate plans for completeness/feasibility; structure docs; session logs/READMEs. |
| `kit-author` | opus·high | Read, Write, Edit, Grep, Glob | — | Author/refine `.claude/` agents, skills, AND hooks from this KIT.md. Writes only `.claude/**` (never `settings.json`). |
| `tooling-auditor` | sonnet·medium | Read, Grep, Glob, Bash(python *) | — | Read-only consistency audit of `.claude/` (frontmatter, wiring, advisory-only hooks). Reports; never edits. |

### Domain 2 — Reverse Engineering (8; firewall-preserving)
Dirty-room agents hold `mcp__ida__*`, write `_dirty/` only, neutral prose, STOP if MCP down.
| Agent | `model`·`effort` | Room | `tools` | `skills:` | One-job |
|---|---|---|---|---|---|
| `re-function-analyst` | opus·high | dirty | mcp__ida__*, Read, Write, Bash(claude mcp *) | `ida-mcp-connect`, `ida-explore` | Function/subsystem recovery: role, xrefs, callgraph, data-flow. Absorbs the old static + one-off generalist. |
| `re-protocol-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `ida-opcode-map`, `pcap-extract` | Opcode space, dispatch table, packet field layouts; cross-check the capture oracle. |
| `re-crypto-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `ida-crypto-hunt` | Cipher / key-schedule / framing shape as neutral algorithm description. |
| `re-struct-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `ida-struct-recovery` | Struct/class/RTTI/vtable field-offset layouts. |
| `re-asset-format-analyst` | opus·high | dirty | mcp__ida__*, Read, Write | `ida-mcp-connect`, `asset-format-doc` | Asset/file-format loaders + animation/bind/motion + VFS/CP949 data tables. |
| `ida-toolsmith` | sonnet·high | dirty (IDB write) | mcp__ida__*, Read, Write, Bash(claude mcp *) | `ida-py`, `ida-annotate` | Author/run bespoke READONLY IDAPython; apply rename/comment/type IDB annotations (dry-run→apply, idempotent). |
| `spec-author` | opus·high | **bridge** | Read, Write, Edit, Grep, Glob | `re-promote` | REWRITE (never copy) `_dirty/` findings into committed neutral specs (`opcodes.md`, `packets/`, `formats/`, `structs/`, `specs/`). **No IDA.** |
| `re-validator` | opus·high | dirty (debugger) | mcp__ida__*, Read, Write | `ida-debugger-drive` | Confirm a spec against the live `?ext=dbg` debugger / binary-diff; never `dbg_start`. |

### Domain 3 — C#/Godot Porting (10; clean-room — NO IDA, read only committed specs)
| Agent | `model`·`effort` | `tools` | `skills:` | One-job |
|---|---|---|---|---|
| `network-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `packet-codegen` | Entire Network layer 02: abstractions, packet structs/opcodes, in-place crypto, Pipelines framing. |
| `assets-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `pak-explore` | Entire Storage.Assets layer 03: VFS, binary parsers, glTF/PNG mapping, CP949 data tables. |
| `core-engineer` | opus·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test` | Client.Core layer 04: deterministic Domain rules, Application use-cases/handlers/event-buses, local Infrastructure. |
| `dotnet-foundation-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test`, `scaffold-project` | Layer 01 kernel/diagnostics, cross-layer glue, slnx/csproj wiring, C#14/.NET10 modernization. |
| `godot-world-engineer` | sonnet·medium | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(godot *) | `godot-run-headless`, `godot-scene-author` | Godot 3D world/terrain/scene + shaders/VFX/lighting. Passive rendering only. |
| `godot-ui-engineer` | sonnet·medium | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(godot *) | `godot-run-headless`, `godot-scene-author` | Godot HUD/menus/UI + input/camera. Routes input as use-case intents. |
| `godot-character-specialist` | opus·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *), Bash(godot *) | `godot-run-headless`, `asset-chain-trace` | Skinned-mesh / bind / motion (the unsolved skinning-explodes-the-mesh debt). |
| `code-reviewer` | opus·high | Read, Grep, Glob, Bash(dotnet *) | `clean-room-check` | C# correctness + perf + layer-DAG + **clean-room firewall** + artifact-protection. Reports BLOCKER/advisory; never edits source. |
| `test-engineer` | sonnet·high | Read, Write, Edit, Grep, Glob, Bash(dotnet *) | `dotnet-build-test` | xUnit tests + build doctoring across the 10 test projects. |
| `render-reviewer` | sonnet·medium | Read, Grep, Glob, Bash(godot *) | `godot-fidelity-check`, `godot-mcp-connect` | Eyes-on Godot render/fidelity review (headless + screenshot); drives the live Godot MCP. Reports; engineers fix. |

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

## 5. Skills plan (31 survivors, reusable expertise only — no orchestration logic)

**A. Knowledge skills (4, `user-invocable:false`, `paths:`-activated):** `dotnet-csharp14`
(`01–04/**`), `godot-engine` (`05.Presentation/**`), `ida-pro-re` (RE methodology + firewall),
`martial-heroes-domain` (recovered protocol/asset-chain index). Neutral documentation — cite specs,
never paste decompiler output or copyrighted bytes.

**B. Action skills (~22)** grouped by family (each keeps a keyworded when-to-use `description`, minimal
hyphenated `allowed-tools`, `${CLAUDE_SKILL_DIR}` script refs, firewall stance):
- **IDA (~10):** `ida-mcp-connect`, `ida-py` (+`ida-script-runner`), `ida-recon` (+`ida-string-hunt`),
  `ida-explore` (`ida-xref-map`+`ida-callgraph-map`+`ida-data-flow`+`ida-batch-analyze`),
  `ida-struct-recovery` (+`ida-vtable-recover`), `ida-annotate`
  (`ida-struct-apply`+`ida-rename-batch`+`ida-annotate-batch`+`ida-naming-sync`), `ida-opcode-map`,
  `ida-crypto-hunt`, `ida-decompile-export`, `ida-debugger-drive`.
- **Protocol/asset/VFS (~5):** `packet-codegen` (+`opcode-catalog`), `pcap-extract` (+`packet-diff`),
  `pak-explore` (+`vfs-inspect`+`vfs-data-format`), `asset-format-doc`, `asset-chain-trace`.
- **Quality/RE-flow (~3):** `clean-room-check` (`clean-room-audit`+`clean-room-firewall-check`+`spec-citation-audit`),
  `re-promote` (+`re-workspace-init`), `preservation` (`preservation-readme`+`re-session-log`).
- **C#/Godot (~8):** `dotnet-build-test`, `scaffold-project` (`new-layer-project`+`add-test-project`+`wire-references`),
  `godot-build`, `godot-run-headless` (+`godot-screenshot`), `godot-scene-author`
  (+`godot-csproj-bootstrap`+`godot-coordinate-check`), `godot-fidelity-check` (+`godot-asset-preview`),
  `godot-mcp-connect`, `memory-curate`.
- **Planning (1):** `plan-campaign`.

When merging a skill, fold the absorbed skill's bundled `scripts/` into the survivor's `scripts/` dir and
update `${CLAUDE_SKILL_DIR}` refs. Delete the absorbed dir only after the survivor carries its capability.

---

## 6. Hooks plan (11 files = 10 hooks + `_hooklib`; advisory-only + fail-open preserved verbatim)

Each hook: `import _hooklib as h`, one concern, low false-positive, `exit 0` always, fail-open. Merged
hooks dispatch internally on event/path.

| Hook | Event / matcher | Merges / role |
|---|---|---|
| `session_primer` | SessionStart | KEEP — orientation context |
| `prompt_primer` | UserPromptSubmit | `re_intent_primer` + `godot_render_state_primer` |
| `firewall_guard` | PreToolUse · Write\|Edit\|MultiEdit + Bash | `clean_room_guard` + `protect_artifacts` + `git_commit_guard` + `git_add_dirty_guard` + `ida_provenance_guard` |
| `layer_dependency_guard` | PreToolUse · Write\|Edit\|MultiEdit | KEEP — upward-ref / `using Godot;` below 05 |
| `re_provenance_logger` | PostToolUse · `mcp__ida__.*` | KEEP — hash IDA output for the audit trail |
| `csharp_guard` | PostToolUse · Write\|Edit\|MultiEdit | `cp949_nudge` + `unsafe_alloc_nudge` + `spec_citation_guard` + `test_after_core_edit` |
| `cs_post_edit` | PostToolUse · Write\|Edit\|MultiEdit | KEEP — zero-alloc/StructLayout nudge + opt-in build (`MH_BUILD_ON_EDIT=1`) |
| `godot_guard` | PostToolUse · Write\|Edit\|MultiEdit | `godot_tscn_guard` + `godot_namespace_guard` + `gltf_crash_guard` + `godot_uid_nudge` + `coordinate_convention_nudge` |
| `kit_guard` | PostToolUse · Write\|Edit\|MultiEdit | `agent_md_guard` + `skill_md_guard` + `hook_advisory_guard` + `settings_wiring_nudge` |
| `session_end` | Stop · SubagentStop · PreCompact | `stop_loose_ends` + `memory_persist_nudge` |
| `_hooklib.py` | — | KEEP — shared helpers; extend, don't fork |

`settings.json` is wired by **me/the main session**, never by `kit-author` (it writes the `.py` + reports
the exact stanza). Every merged hook preserves the precise advisory wording of what it absorbed.

---

## 7. `_hooklib.py` (foundation)

Keep the existing helpers (path classifiers, frontmatter parser, advisory emitters, `fail_open`). When a
merge needs a new shared check, add it here (std-lib only, fail-open, simple returns) rather than
duplicating logic across the merged hooks.

---

## 8. Execution order (create-new → rewire → delete-old → audit)

The main session drives authoring directly (the old meta-authors are themselves being replaced). Safe order:

1. **This `KIT.md`** — the spec everything follows.
2. **3 domain orchestrators** with full rosters + `Agent(...)` lists.
3. **24 workers** (6 Planning + 8 RE + 10 Porting) — `model`/`effort`/`skills`, sharp descriptions,
   firewall stance per room.
4. **Skills** consolidated to 31 (merge bodies + scripts, keep knowledge layer + `paths:`).
5. **Hooks** consolidated to 11 (10 + `_hooklib`) via `_hooklib`; then **rewire `settings.json`** (main session).
6. **Delete superseded** files only after capability lands elsewhere AND no roster/`settings.json`/
   `skills:`/`CLAUDE.md` reference points at the retired name.
7. **Update `CLAUDE.md`** tooling map + `.claude/README.md` index.
8. **Validate:** `tooling-auditor` + grep for dangling names + `ast.parse` every hook + confirm firewall
   placement (no Porting agent has `mcp__ida__*`).

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
- **Network (02):** opcodes `(major<<16)|minor`; 8-byte frame `[u32 size][u16 major][u16 minor]`; rolling
  XOR/ROL cipher in-place on `Span<byte>`; LZ4 raw-block; source-gen opcode→handler switch; `[StructLayout
  (Pack=1)]`+`[InlineArray]`; no managed strings on the wire. **N2: byte-exact wire parity.**
- **Assets (03):** the recovered chains — terrain `.ted`→`.map`→`bgtexture.txt`→`.dds` (global under
  `map000`); skin `.skn` IdA→`skin.txt`→tex; bind/idle `.bnd`/`.mot`; spawns `npc{tag}.arr` (28-byte)/
  `mob{tag}.arr` (20-byte); collision `.sod` (2D XZ ray-parity); ground via `.ted` bilinear; CP949 text;
  Parsers carry no rendering dep, Mapping is the only glTF/PNG bridge. **N2: faithful asset reproduction.**
- **Core (04):** Domain is 100% deterministic, headless-testable, references only Shared.Kernel;
  Application orchestrates only (use-cases + handlers + `Channels` buses, no game math, no rendering, no
  transport). **N2: behavior parity.**
- **Godot (05):** passive rendering, zero authority; pitfalls (`.tscn` script = property line;
  `global::Godot.*` collisions; never `GltfDocument.AppendFromBuffer` → build `ArrayMesh`; world negates
  Z, mesh `.skn` negates X; cells 1024/65×65/spacing 16); open debts (skinning explodes, NPC fallback-Y
  race, `EnvironmentNode` too dark, water unwired); verify via headless console + windowed screenshot.
  **N2: pixel-faithful 1:1 visuals.**
- **Reviewers / quality:** the exact invariant each guards; PASS/FAIL with `file:line`; separate BLOCKER
  from advisory; never edit source to make a check pass.
- **Orchestrators:** extremely-detailed atomic per-worker briefs; the file-ownership ledger; unbridled IDA
  fan-out; a gate between waves; reconcile into ONE rolled-up result; two-levels-of-orchestration max.
