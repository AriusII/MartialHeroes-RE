# KIT.md — `.claude/` kit design contract

## §0 Purpose

`KIT.md` is the authoritative design doc for the `.claude/` Claude Code kit of the
Martial Heroes clean-room RE revival. Read it before authoring or refining any
agent, skill, or hook. It is the contract `CLAUDE.md` points to. Disk reality wins
over prose: this kit currently materializes `.claude/agents/` (32 after this
overhaul), `.claude/skills/` (35 after this overhaul), and `settings.local.json`.
The advisory hook layer described in `CLAUDE.md` is **planned, not yet on disk** —
treat hooks as a future addition, never as an existing dependency.

This kit serves one mission: the total reverse-engineering of `doida.exe` and its
faithful 1:1 re-creation on Godot 4.6.3-mono. Every agent, skill, and convention
below exists to keep that reverse running wide and hard while the clean-room
firewall keeps it lawful (EU Software Directive 2009/24/EC Art. 6 — decompilation
permitted solely to achieve interoperability).

## §1 The five-orchestrator design (Tier model)

Orchestration is capped at two levels: **Tier-1** (main session) → **Tier-2** (one
domain orchestrator) → **Tier-3** (single-deliverable worker). A Tier-2
orchestrator never spawns another Tier-2. The depth cap is enforced mechanically:
only the five domain orchestrators hold the `Agent(...)` tool (scoped to their own
roster); every Tier-3 worker omits `Agent` entirely and therefore cannot spawn.

| Tier-2 orchestrator | Domain | Spawns (Tier-3 roster) |
|---|---|---|
| `planning-orchestrator` | Planning / decomposition (clean, no IDA) | requirement-analyst, todo-architect, knowledge-gap-detector, plan-reviewer |
| `re-orchestrator` | Clean-room RE / IDA liaison | re-function-analyst, re-protocol-analyst, re-crypto-analyst, re-struct-analyst, re-asset-format-analyst, ida-python-engineer, ida-toolsmith, re-validator, spec-author |
| `csharp-port-orchestrator` | C# layers 00→04 + Tools | dotnet-foundation-engineer, network-engineer, assets-engineer, core-engineer, code-reviewer, test-engineer |
| `godot-orchestrator` | Layer 05 (Godot) | godot-world-engineer, godot-ui-engineer, godot-character-specialist, render-reviewer, code-reviewer, test-engineer |
| `docs-tooling-orchestrator` | Docs + tooling + kit | docs-engineer, tooling-engineer, kit-author, tooling-auditor |

For a single-deliverable task, Tier-1 delegates straight to the worker. Route
through an orchestrator only for multi-worker objectives. Each orchestrator owns a
file-ownership ledger (one writer per path per wave) and returns one rolled-up
result.

## §2 model + effort policy

| Role class | `model` | `effort` |
|---|---|---|
| Domain orchestrators (judgement/decomposition/reconciliation) | `opus` | `high` |
| Judgement workers (game-rule, struct/proto recovery, crypto, character/skinning, reviewers) | `opus` | `high` |
| Mechanical execution workers (network/assets/test/docs/tooling, godot world/ui, ida-toolsmith, ida-python-engineer) | `sonnet` | `high` for precision-sensitive, `medium` for mechanical |

Resolution order at runtime: env `CLAUDE_CODE_SUBAGENT_MODEL` → per-invocation
param → frontmatter `model` → main conversation. Every agent declares BOTH
`model:` and `effort:` explicitly — no inheritance defaults.

## §3 Agent ↔ skill linkage fabric

Two linking mechanisms plus one inverse:

1. **`skills:` preload (agent → skill).** Injects the full skill body into the
   worker at startup. Use for the domain knowledge a worker always needs. A skill
   with `disable-model-invocation: true` cannot be preloaded.
2. **`paths:` knowledge skills (file → skill).** A `user-invocable: false` skill
   with a `paths:` glob auto-surfaces as background convention when matching files
   are touched — no invocation, not in the `/` menu. The kit's four knowledge
   skills: `dotnet-csharp14` (`01.*/**`, `02.*/**`, `03.*/**`, `04.*/**`),
   `godot-engine` (`05.Presentation/**`), `martial-heroes-domain` (`Docs/RE/**`),
   `ida-pro-re` (`Docs/RE/_dirty/**`, `Docs/RE/**`).
3. **`context: fork` (skill → agent, inverse).** A skill runs its body as a task
   inside a chosen subagent type. Not currently used in this kit; reserved.

A worker without a `skills:` preload can still invoke any project skill via the
`Skill` tool — preload only what the worker needs at all times.

### Linkage table (agent → `skills:` preloads, post-overhaul)

| Agent | `skills:` preloads |
|---|---|
| `planning-orchestrator` | plan-campaign |
| `requirement-analyst` | plan-campaign |
| `todo-architect` | plan-campaign |
| `knowledge-gap-detector` | — |
| `plan-reviewer` | — |
| `re-orchestrator` | ida-mcp-connect, re-promote, re-brainstorm, ida-python-lib |
| `re-function-analyst` | ida-mcp-connect, ida-recon, ida-explore, re-brainstorm, ida-pro-re, ida-python-lib |
| `re-protocol-analyst` | ida-mcp-connect, ida-opcode-map, pcap-extract, ida-pro-re, ida-python-lib |
| `re-crypto-analyst` | ida-mcp-connect, ida-crypto-hunt, ida-pro-re, ida-python-lib |
| `re-struct-analyst` | ida-mcp-connect, ida-struct-recovery, ida-pro-re, ida-python-lib |
| `re-asset-format-analyst` | ida-mcp-connect, asset-format-doc, ida-pro-re, ida-python-lib, pak-explore |
| `ida-python-engineer` | ida-mcp-connect, ida-python-lib, ida-py, ida-pro-re |
| `ida-toolsmith` | ida-py, ida-annotate, ida-mcp-connect, ida-python-lib |
| `re-validator` | ida-debugger-drive, ida-mcp-connect, ida-pro-re |
| `spec-author` | re-promote, re-handoff, clean-room-check, ida-pro-re |
| `csharp-port-orchestrator` | dotnet-build-test |
| `dotnet-foundation-engineer` | scaffold-project, csharp-tooling, dotnet-csharp14 |
| `network-engineer` | packet-codegen, dotnet-csharp14 |
| `assets-engineer` | pak-explore, dotnet-csharp14 |
| `core-engineer` | dotnet-csharp14 |
| `code-reviewer` | clean-room-check |
| `test-engineer` | dotnet-build-test |
| `godot-orchestrator` | godot-run-headless |
| `godot-world-engineer` | godot-scene-author, godot-engine |
| `godot-ui-engineer` | godot-scene-author, godot-engine |
| `godot-character-specialist` | asset-chain-trace, godot-engine |
| `render-reviewer` | godot-fidelity-check, godot-mcp-connect |
| `docs-tooling-orchestrator` | preservation |
| `docs-engineer` | preservation, doc-authoring, memory-curate |
| `tooling-engineer` | python-tooling, csharp-tooling |
| `kit-author` | doc-authoring |
| `tooling-auditor` | clean-room-check |

(The table mirrors the final state after the kit overhaul. The per-file frontmatter
is the source of truth; this table is the at-a-glance map.)

## §4 Clean-room firewall mechanism

A strict **dirty → spec → engineer** pipeline keeps decompiler output out of the
shipped code:

1. **Dirty-room RE** (IDA analysts) writes **only** to `Docs/RE/_dirty/` —
   gitignored, tainted, never shipped.
2. **`spec-author` rewrites** (never copies) those findings into committed, neutral
   specs. It is the **sole firewall crossing**, the only agent that writes the
   committed spec trees, via the `re-promote` step:
   - `Docs/RE/opcodes.md` — opcode catalogue (no addresses)
   - `Docs/RE/packets/*.yaml` — wire-field specs
   - `Docs/RE/formats/*.md` — asset/file formats
   - `Docs/RE/structs/*.md` — struct offset tables
   - `Docs/RE/specs/*.md` — subsystem behaviour
3. **Port agents read only the committed specs.** They never touch `_dirty/` or
   IDA.

**Never paste into any committed file or any C#:** Hex-Rays / IDA pseudo-C,
`sub_xxxx`/`loc_xxxx` autonames, `_DWORD`/`_BYTE`/`_QWORD`, `__thiscall`/
`__fastcall`/`__stdcall`, mangled names, raw addresses, or any copyrighted byte
sequence from the original.

**What IS clean prose (safe to promote):**
- An **offset table** — field name, byte offset, width, type, meaning — derived
  from observed layout but written fresh.
- An **algorithm described in words and math** — "for each byte, XOR with the
  running key then rotate left by 3" — never the transcribed loop.
- An **opcode catalogue** — opcode id, neutral name, direction, size, status —
  with no addresses.

`Docs/RE/journal.md` (provenance audit trail) and `Docs/RE/names.yaml` (canonical
glossary) are **orchestrator-owned** — authoring agents do not edit them.

## §5 Modern IDA MCP toolset capability map

The IDA MCP `ida` is the absolute ground-truth authority on `doida.exe`. The
toolset splits into twelve buckets. The base endpoint exposes buckets 1–7 and
10–12 (static); the `?ext=dbg` superset adds buckets 8–9 (dynamic). The committed
`.mcp.json` registers `?ext=dbg`. If the `dbg_*` tools are absent the session is on
the wrong (base) endpoint — re-register with
`claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`.
**`dbg_start` is FORBIDDEN by project policy** — the maintainer F9-launches the
client; agents pilot the live session only.

### Bucket 1 — Connection / health
| Tool | Purpose |
|---|---|
| `mcp__ida__server_health` | Is the MCP server up and responsive |
| `mcp__ida__domain_database_info` | Which IDB is open, analyzed state, SHA to pin |

### Bucket 2 — Static recon & census
| Tool | Purpose |
|---|---|
| `mcp__ida__survey_binary` | One-shot baseline census of the whole binary |
| `mcp__ida__domain_segments` / `mcp__ida__memory_map` | Segment / memory layout |
| `mcp__ida__domain_entry_points` | Entry points |
| `mcp__ida__domain_imports` / `mcp__ida__imports` / `mcp__ida__imports_query` | Import table |
| `mcp__ida__domain_functions` / `mcp__ida__list_funcs` / `mcp__ida__lookup_funcs` / `mcp__ida__func_query` | Function enumeration / lookup |
| `mcp__ida__list_globals` / `mcp__ida__get_global_value` | Named globals + values |
| `mcp__ida__entity_query` | Unified entity lookup |
| `mcp__ida__domain_strings` / `mcp__ida__search_text` / `mcp__ida__refresh_strings_cache` | String census / search / cache warm |
| `mcp__ida__find` / `mcp__ida__find_bytes` / `mcp__ida__find_regex` | Byte / pattern search |
| `mcp__ida__get_bytes` / `mcp__ida__get_int` / `mcp__ida__get_string` / `mcp__ida__read_cstring` | Static memory reads |
| `mcp__ida__func_profile` / `mcp__ida__function_skeleton` | Per-function role summary / skeleton |
| `mcp__ida__module_hierarchy` | Module / namespace tree |
| `mcp__ida__analyze_component` / `mcp__ida__analyze_function` / `mcp__ida__analyze_batch` | Subsystem / function analysis |
| `mcp__ida__export_funcs` | Bulk function export |
| `mcp__ida__disasm` | Disassembly |
| `mcp__ida__basic_blocks` | Basic-block CFG |
| `mcp__ida__insn_query` | Instruction-level query |
| `mcp__ida__search_docs` | Search the MCP's own docs |

### Bucket 3 — Decompile / pseudocode / microcode (DIRTY — output never crosses the firewall)
| Tool | Purpose |
|---|---|
| `mcp__ida__decompile` | Hex-Rays pseudo-C (dirty) |
| `mcp__ida__domain_function_details` / `mcp__ida__domain_function_pseudocode` | Function detail / pseudocode |
| `mcp__ida__pseudocode_query` | Query within pseudocode |
| `mcp__ida__map_ea_to_pseudocode` / `mcp__ida__map_pseudocode_line_to_eas` | Line ↔ EA mapping |
| `mcp__ida__microcode_text` | Microcode listing |
| `mcp__ida__microcode_calls` | Call resolution via microcode (indirect/virtual) |
| `mcp__ida__force_recompile` | Force re-decompilation |

### Bucket 4 — Xrefs / callgraph / data-flow
| Tool | Purpose |
|---|---|
| `mcp__ida__xref_query` / `mcp__ida__xrefs_to` / `mcp__ida__domain_xrefs` | Cross-references |
| `mcp__ida__xrefs_to_field` | Field-level xrefs |
| `mcp__ida__data_refs` | Data references |
| `mcp__ida__call_hierarchy` / `mcp__ida__callgraph` | Call hierarchy / graph |
| `mcp__ida__callees` / `mcp__ida__callees_recursive` / `mcp__ida__callers_recursive` | Callee / caller closures |
| `mcp__ida__list_indirect_calls` | Indirect-call sites |
| `mcp__ida__trace_data_flow` | Forward/backward value flow |
| `mcp__ida__reaches` | Reachability between EAs |
| `mcp__ida__lvar_usage` | Local-variable usage |

### Bucket 5 — Types / structs / enums / vtables
| Tool | Purpose |
|---|---|
| `mcp__ida__declare_type` | Declare a C type |
| `mcp__ida__struct_member_edit` | Edit struct member |
| `mcp__ida__enum_upsert` | Create/update enum |
| `mcp__ida__read_struct` | Static struct decode |
| `mcp__ida__search_structs` / `mcp__ida__type_query` / `mcp__ida__type_inspect` | Type search / inspection |
| `mcp__ida__domain_types` / `mcp__ida__domain_type_layout` | Type catalogue / layout |
| `mcp__ida__infer_types` | Seed prototypes |
| `mcp__ida__classify_pointer` | Vtable / pointer classification |
| `mcp__ida__type_apply_batch` | Batch type application |
| `mcp__ida__add_til` / `mcp__ida__list_tils` | Type libraries |
| `mcp__ida__stack_frame` / `mcp__ida__declare_stack` / `mcp__ida__delete_stack` / `mcp__ida__rename_stack` | Stack-frame layout |
| `mcp__ida__int_convert` | Integer representation convert |

### Bucket 6 — IDB legibility WRITES
| Tool | Purpose |
|---|---|
| `mcp__ida__rename` | Rename a symbol |
| `mcp__ida__set_comments` / `mcp__ida__append_comments` | Set/append comments |
| `mcp__ida__set_type` | Apply a type |
| `mcp__ida__set_lvar` | Rename/type a local |
| `mcp__ida__set_op_type` | Operand type |
| `mcp__ida__make_data` / `mcp__ida__define_code` / `mcp__ida__define_func` | Define data / code / function |
| `mcp__ida__undefine` | Undefine |
| `mcp__ida__add_bookmark` | Bookmark |
| `mcp__ida__idb_save` | Persist the IDB |

### Bucket 7 — IDAPython
| Tool | Purpose |
|---|---|
| `mcp__ida__py_eval` | One-line expression |
| `mcp__ida__py_exec_file` | Multi-statement snippet / harness |
| `mcp__ida__run_script` | Run a stored script |
| `mcp__ida__save_script` / `mcp__ida__read_script` / `mcp__ida__list_scripts` | Persist reusable probes in-DB |

### Bucket 8 — Dynamic debugger (`?ext=dbg` only)
| Tool | Purpose |
|---|---|
| `mcp__ida__dbg_status` | Debugger state |
| `mcp__ida__dbg_add_bp` / `mcp__ida__dbg_delete_bp` / `mcp__ida__dbg_toggle_bp` / `mcp__ida__dbg_bps` | Breakpoints |
| `mcp__ida__dbg_set_bp_condition` / `mcp__ida__dbg_set_bp_hit_count` | BP conditions / hit count |
| `mcp__ida__dbg_continue` / `mcp__ida__dbg_run_to` | Continue / run-to |
| `mcp__ida__dbg_step_into` / `mcp__ida__dbg_step_over` / `mcp__ida__dbg_step_out` | Stepping |
| `mcp__ida__dbg_regs` / `mcp__ida__dbg_regs_all` / `mcp__ida__dbg_regs_named` / `mcp__ida__dbg_gpregs` (+ `_remote`) | Register reads |
| `mcp__ida__dbg_set_reg` | Set a register |
| `mcp__ida__dbg_read` / `mcp__ida__dbg_write` | Live memory read/write |
| `mcp__ida__dbg_stacktrace` | Stack trace |
| `mcp__ida__dbg_threads` / `mcp__ida__dbg_select_thread` | Thread list / select |
| `mcp__ida__exception_config` | Exception handling config |

**FORBIDDEN: `mcp__ida__dbg_start`, `mcp__ida__dbg_attach`, `mcp__ida__dbg_detach`,
`mcp__ida__dbg_exit` — maintainer-only; agents never spawn/kill the session.**

### Bucket 9 — Dynamic instrumentation / probes / tracing (`?ext=dbg` only)
| Tool | Purpose |
|---|---|
| `mcp__ida__probe_add` / `mcp__ida__probe_arm` / `mcp__ida__probe_clear` / `mcp__ida__probe_list` | Lightweight EA probes |
| `mcp__ida__probe_api_call` | Log a specific API's calls + args |
| `mcp__ida__probe_net` | Auto-capture every socket send/recv |
| `mcp__ida__probe_drain` / `mcp__ida__probe_stats` | Harvest / stats |
| `mcp__ida__trace_calls` / `mcp__ida__trace_summary` | Call tracing / summary |
| `mcp__ida__watch_field` / `mcp__ida__watch_region` | Catch who writes a member/region |
| `mcp__ida__run_until` | Advance to an interesting state |
| `mcp__ida__appcall` / `mcp__ida__appcall_inspect` | Invoke the client's own functions on captured data |
| `mcp__ida__memory_scan` | Find a live buffer/key in RAM |
| `mcp__ida__read_struct_live` | Decode a struct at a live pointer |
| `mcp__ida__hierarchy_runtime_overlay` | Map runtime calls onto the static graph |
| `mcp__ida__stop_context` | Snapshot runtime state |

### Bucket 10 — Recipes / autopilot / journaling / snapshots / diffs
| Tool | Purpose |
|---|---|
| `mcp__ida__autopilot_run` | Guided multi-step recipe |
| `mcp__ida__recipe_dispatch_scan` | Find opcode→handler dispatch tables |
| `mcp__ida__recipe_crypto_candidates` | Crypto candidate detector |
| `mcp__ida__recipe_function_report` | Per-function structured report |
| `mcp__ida__recipe_import_usage` | Import-usage subsystem tagging |
| `mcp__ida__recipe_string_to_code` | String → referencing code |
| `mcp__ida__find_xref_signatures` | Xref-based signatures |
| `mcp__ida__journal_note` / `mcp__ida__journal_history` / `mcp__ida__journal_config` | In-IDB provenance journal |
| `mcp__ida__snapshot_save` / `mcp__ida__snapshot_list` / `mcp__ida__snapshot_restore` / `mcp__ida__snapshot_delete` / `mcp__ida__snapshot_diff` | IDB snapshots |
| `mcp__ida__diff_buffers` / `mcp__ida__diff_before_after` | Buffer / before-after diffs |

### Bucket 11 — Patching (read-only project — requires explicit human approval)
| Tool | Purpose |
|---|---|
| `mcp__ida__patch` / `mcp__ida__patch_asm` | Patch bytes / assembly |
| `mcp__ida__revert_patch` | Revert a patch |
| `mcp__ida__list_patches` | List patches |

### Bucket 12 — Signatures (build-mismatch cure)
| Tool | Purpose |
|---|---|
| `mcp__ida__make_signature` | Make a signature |
| `mcp__ida__make_signature_for_function` | Per-function signature |
| `mcp__ida__make_signature_for_range` | Range signature |
| `mcp__ida__find_xref_signatures` | Xref-based signatures |

## §6 IDB legibility & provenance discipline

- **Snapshot before bulk write:** `mcp__ida__snapshot_save` → batch the writes →
  `mcp__ida__snapshot_diff` / `mcp__ida__diff_before_after` to confirm exactly what
  changed → `mcp__ida__idb_save`. `mcp__ida__snapshot_restore` rolls back a bad
  sweep.
- **Idempotent dry-run → apply:** every write batch must converge to a noop on
  re-run. Produce a dry-run JSON diff, apply only on explicit confirmation.
- **Glossary-gated names:** apply only names approved in `Docs/RE/names.yaml`;
  never invent a name on the fly into the IDB.
- **Paired journaling:** every write wave records an in-IDB `mcp__ida__journal_note`
  paired with an entry in the committed `Docs/RE/journal.md`.
- **SHA-256 banners:** each committed spec carries a `verification:` banner pinned
  to the current IDB SHA captured from `mcp__ida__domain_database_info`.
- **Build-mismatch cure:** the CYCLE-14 re-anchor showed addresses drift between
  builds; never trust a hard-coded EA across a rebuild. `mcp__ida__make_signature*`
  re-locates a function by pattern when the IDB is re-anchored.

## §7 RESULT_JSON harness convention

Every IDAPython probe prints **exactly one** line: `RESULT_JSON ` followed by a
single `json.dumps(...)` object. Every EA is hex-encoded as a string (never a raw
int). Output lands in `Docs/RE/_dirty/queries/`; addresses are stripped before any
promotion to a committed spec. Tool routing:

- `mcp__ida__py_eval` — one-line expression probe.
- `mcp__ida__py_exec_file` — multi-statement snippet / harness.
- `mcp__ida__run_script` (+ `mcp__ida__save_script` / `mcp__ida__read_script` /
  `mcp__ida__list_scripts`) — persist a reusable probe in the DB instead of
  re-pasting it.

The curated snippet catalogue and the `emit()` helper live in the `ida-python-lib`
skill.

## §8 Authoring checklists

### Agent file checklist
- [ ] Frontmatter field order: `name`, `description`, `model`, `effort`, `tools`,
      `disallowedTools` (if any), `skills` (if any), `color` (optional).
- [ ] `description` is third-person: what it does + an explicit trigger. RE agents
      add the proactive "Use proactively" / "MUST BE USED" phrase.
- [ ] Boundaries stated so auto-delegation routes correctly (what it does NOT do).
- [ ] `model` + `effort` BOTH declared per §2; no inheritance.
- [ ] `tools` / `disallowedTools` delimiter is comma-space (`A, B, C`).
- [ ] Tier-3 workers omit `Agent`; only the five orchestrators hold it (scoped).
- [ ] Dirty-room RE agents carry the F0 firewall clause verbatim; READONLY analysts
      carry the RW-DENY `disallowedTools` block.
- [ ] `skills:` preloads only what the worker needs at all times.
- [ ] C# engineers/reviewers cite the spec in the spec/journal/PR — **NEVER as a
      C# comment** (project mandate: zero comments in `.cs`). See D-SPEC below.
- [ ] Hook-dependent mandates are conditional on `.claude/hooks/` existing (it does
      not yet). See D-HOOKS below.

### Skill file checklist
- [ ] Frontmatter field order: `name`, `description`, `allowed-tools`, `model`,
      `effort`, then `user-invocable` / `paths` / `disable-model-invocation`.
- [ ] `description` is third-person with an explicit trigger.
- [ ] `allowed-tools` delimiter is comma-space.
- [ ] MCP tools referenced fully qualified (`mcp__ida__*` / `mcp__godot__*`); no
      stale names (`eval`/`execute_script`/`run_python` were removed).
- [ ] Knowledge skills set `user-invocable: false` + a `paths:` glob.
- [ ] Dirty-room RE skills carry the F0 firewall clause and the RESULT_JSON note.

### D-SPEC (binding)
The standing mandate "NO comments in `.cs` (incl. `// spec:`)" WINS. Citations live
in the spec/journal, never in C#. Every C# engineer agent, both reviewers, and
`csharp-port-orchestrator` say: *"Cite the source spec in the spec/journal, NEVER
as a C# comment — C# files carry zero comments (project mandate)."* `code-reviewer`
flags ANY comment in `.cs`, and verifies each magic constant traces to a committed
spec via the journal/PR text (out-of-band, not an inline comment).

### D-HOOKS (binding)
`.claude/hooks/` does NOT exist on disk. Every reference to hooks is
planned/aspirational ("planned advisory hooks, not yet materialized"). Agents that
audit/maintain hooks (`tooling-auditor`, `kit-author`, `docs-tooling-orchestrator`,
`docs-engineer`) make their hook-dependent mandates conditional ("when
`.claude/hooks/` exists") so they no-op cleanly.

## §9 Roster inventory

### Agents (32)
| Agent | model | effort | Role |
|---|---|---|---|
| `planning-orchestrator` | opus | high | Plan-mode reformulate → decompose → routed FINAL PLAN |
| `requirement-analyst` | opus | high | Reformulate / scope / risk |
| `todo-architect` | opus | high | Decompose into phase/objective tree + deps |
| `knowledge-gap-detector` | sonnet | medium | Route RE/spec knowledge gaps |
| `plan-reviewer` | opus | high | Validate plans |
| `re-orchestrator` | opus | high | Clean-room RE / IDA liaison |
| `re-function-analyst` | opus | high | Map & read individual functions (READONLY) |
| `re-protocol-analyst` | opus | high | Packet/opcode/dispatch recovery (READONLY) |
| `re-crypto-analyst` | opus | high | Cipher / key-schedule recovery (READONLY) |
| `re-struct-analyst` | opus | high | Struct/vtable/RTTI recovery (READONLY) |
| `re-asset-format-analyst` | opus | high | Asset/format/VFS recovery (READONLY) |
| `ida-python-engineer` | sonnet | medium | Read-only IDAPython census/extraction |
| `ida-toolsmith` | sonnet | high | The only IDB-write agent |
| `re-validator` | opus | high | Dynamic confirmation vs `?ext=dbg` (G2 gate) |
| `spec-author` | opus | high | The only committed-spec writer (firewall crossing) |
| `csharp-port-orchestrator` | opus | high | C# layers 00→04 + Tools |
| `dotnet-foundation-engineer` | sonnet | high | Layer 01 + generators + Tools + slnx/csproj |
| `network-engineer` | sonnet | high | Layer 02 wire/protocol/crypto |
| `assets-engineer` | sonnet | high | Layer 03 VFS / parsers / mapping |
| `core-engineer` | opus | high | Layer 04 game-state / formulas |
| `code-reviewer` | opus | high | Correctness + perf + DAG + firewall |
| `test-engineer` | sonnet | high | xUnit + whole-solution build |
| `godot-orchestrator` | opus | high | Layer 05 (Godot) |
| `godot-world-engineer` | sonnet | high | Terrain / world / shaders |
| `godot-ui-engineer` | sonnet | high | HUD / menus / input / camera |
| `godot-character-specialist` | opus | high | Skinning / bind / motion |
| `render-reviewer` | sonnet | medium | Godot fidelity (live MCP) |
| `docs-tooling-orchestrator` | opus | high | Docs + tooling + kit |
| `docs-engineer` | sonnet | medium | Committed doc corpus, firewall-neutral |
| `tooling-engineer` | sonnet | medium | C# Tools + generators + Python harnesses |
| `kit-author` | opus | high | Author `.claude/` agents + skills (+ planned hooks) |
| `tooling-auditor` | sonnet | medium | Read-only kit audit |

### Skills (35)
| Skill | model | effort | Role |
|---|---|---|---|
| `ida-mcp-connect` | sonnet | medium | Probe IDA MCP, verify SHA + endpoint |
| `ida-recon` | sonnet | high | Baseline binary census + STRING-HUNT |
| `ida-explore` | sonnet | high | Xref/callgraph/data-flow/batch/decompile-one/microcode |
| `ida-opcode-map` | sonnet | high | Dispatch-table → opcode/handler map |
| `ida-crypto-hunt` | sonnet | high | Cipher / key-schedule recovery |
| `ida-struct-recovery` | sonnet | high | Struct / vtable / RTTI recovery |
| `ida-annotate` | sonnet | high | The IDB-write applier |
| `ida-debugger-drive` | sonnet | high | Pilot the live `?ext=dbg` session |
| `ida-py` | sonnet | medium | Arbitrary IDAPython escape hatch |
| `ida-python-lib` | sonnet | high | Curated IDAPython snippet/harness library |
| `ida-pro-re` | — | — | RE methodology + firewall (knowledge, `paths:`) |
| `re-brainstorm` | — | — | RE ideation / attack plan (G0) |
| `pcap-extract` | — | — | Capture oracle: extract + field-diff |
| `packet-codegen` | — | — | Wire structs + opcode catalogue from specs |
| `pak-explore` | — | — | Archive / VFS inspection (no payload export) |
| `asset-format-doc` | — | — | Scaffold a new format spec + hexdump |
| `asset-chain-trace` | — | — | Walk an asset id to its VFS file |
| `scaffold-project` | — | — | Scaffold layers/generators/Tools/tests + wiring |
| `dotnet-build-test` | — | — | Canonical build/test invocations |
| `csharp-tooling` | — | — | Build/validate/extend the Tools projects |
| `godot-run-headless` | — | — | Build layer 05 + headless/screenshot |
| `godot-scene-author` | — | — | Author/repair `.tscn` + wiring |
| `godot-mcp-connect` | — | — | Probe the Godot MCP bridge |
| `godot-fidelity-check` | — | — | Verify 1:1 render fidelity |
| `doc-authoring` | — | — | Broad firewall-neutral doc corpus |
| `python-tooling` | — | — | Std-lib Python scripts/harnesses |
| `preservation` | — | — | READMEs + provenance + session logs |
| `clean-room-check` | — | — | Firewall + citation audit |
| `re-promote` | — | — | dirty → committed spec promotion |
| `re-handoff` | — | — | IDA→C# readiness gate (G4) |
| `re-brainstorm` is listed once; `memory-curate` | — | — | Tidy the auto-memory store |
| `plan-campaign` | — | — | Mandate → approve-ready campaign plan |
| `dotnet-csharp14` | — | — | C#14/.NET10 conventions (knowledge, `paths:`) |
| `godot-engine` | — | — | Godot 4.6 pitfalls + coords (knowledge, `paths:`) |
| `martial-heroes-domain` | — | — | Recovered chains index (knowledge, `paths:`) |

(The skill count is 35 post-overhaul, the new addition being `ida-python-lib`. The
four knowledge skills carry `user-invocable: false` + a `paths:` glob and no
model/effort; per-file frontmatter is the source of truth.)
