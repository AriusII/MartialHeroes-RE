---
name: ida-explore
description: Use to NAVIGATE and TRIAGE the legacy Martial Heroes client (Main.exe / doida.exe) in IDA before AND including the deep read of a single function — five modes in one skill. XREF mode maps every cross-reference to a string/global/constant/function ("who reaches this"); CALLGRAPH mode builds a bounded callers+callees graph around a target ("how this function sits in its subsystem"); DATA-FLOW mode traces a value forward/backward from an instruction ("where does this recv buffer go / what feeds this length field"); BATCH mode profiles a whole candidate subsystem of related functions and emits a per-function role summary; DECOMPILE-ONE mode exports exactly ONE function's raw Hex-Rays pseudo-C plus its callers/callees into the dirty quarantine for an analyst to describe in neutral prose. MICROCODE mode (microcode_text/microcode_calls) resolves indirect/virtual dispatch cleaner than pseudocode. Drives the typed mcp__ida__* navigation/decompile tools (xref_query/xrefs_to/callgraph/callees_recursive/callers_recursive/trace_data_flow/analyze_batch/func_profile/microcode_calls/decompile), falling back to bundled IDAPython snippets, and writes neutral maps (and dirty pseudo-C, DECOMPILE-ONE only) to Docs/RE/_dirty/. The fast way to find the few functions worth reading closely — then to read one of them.
allowed-tools: mcp__ida__*, Read, Write
model: sonnet
effort: high
---

# ida-explore — navigate & triage the legacy client, then deep-read one function

One skill, five modes. Modes A–D turn a recognizable anchor (a string, a global, a constant, a
function, an observed value, a function cluster) into the **small set of functions worth reading
closely** — so you never decompile twenty functions to find the three that matter. Mode E
(DECOMPILE-ONE) then **reads exactly one** of those functions into the quarantine.

| Mode | Question it answers | Anchor | Bundled snippet | Dirty dir |
|---|---|---|---|---|
| **XREF** | "who reaches this marker?" (fan-in) | string / global / const / function | `xref_map.py` | `_dirty/queries/` |
| **CALLGRAPH** | "how does this function sit in its subsystem?" | one function | `callgraph_map.py` | `_dirty/static/` |
| **DATA-FLOW** | "where does this value go / what feeds it?" | instruction + operand/reg | `data_flow.py` | `_dirty/static/` |
| **BATCH** | "what does this cluster of functions do?" | a set/range of functions | `batch_analyze.py` (+ `profile_all*.py` sweeps) | `_dirty/static/` |
| **DECOMPILE-ONE** | "what does THIS one function actually do?" | one function | `decompile_one.py` | `_dirty/functions/` |

All output is **dirty** — addresses derived directly from the copyrighted binary — and lands only
under `Docs/RE/_dirty/`. Nothing here is committed; promotion to clean specs is a separate step.
**DECOMPILE-ONE is the only mode that emits verbatim pseudo-C** (the others emit structure only):
its `_dirty/functions/<name>.dirty.md` is the most contaminated artifact this skill produces — read,
never transcribed, and never echoed into a reply.

**Ground truth:** every edge, xref, flow step, and metric is read FROM the IDA query — never from
memory, analogy, or a guess at "how a subsystem is usually wired." A static map is a *hypothesis*
about who *can* reach/call/feed a target; which caller/edge/value *actually* fires at runtime is
confirmed in the live `?ext=dbg` debugger (`ida-debugger-drive`). MCP down / wrong DB ⇒ STOP, never
fabricate an xref, edge, flow step, or metric.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp?ext=dbg` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. Prefer the **typed** navigation
   tools when present; fall back to the bundled snippet via the script-exec tool
   (`mcp__ida__py_exec_file` / `py_eval` / `run_script`, persisting reusable probes with
   `save_script` / `read_script` / `list_scripts`) only when no typed tool covers the mode. Each probe
   emits one `RESULT_JSON <json>` line per the harness contract — see the `ida-python-lib` skill.

## Mode A — XREF (map every reference to a target)

Turn a recognizable marker (an error string, a config key, a magic constant, a known import, a
named global/function) into the full set of functions that touch it.

1. **Resolve the anchor to an address.** A name → resolve it (typed `get_name_ea` equivalent /
   `entity_query` / `list_globals`). A literal string → find it first (typed `find` / `get_string`,
   or the **ida-recon** string-hunt mode) and use its data EA. A raw constant → immediate-operand
   references (`CONST` mode). Record the resolved EA.
2. **Walk the references.**
   - **Typed (preferred):** `mcp__ida__xref_query` / `xrefs_to` on the EA for every code/data xref;
     `trace_data_flow` from the EA for a deeper "who ultimately feeds/consumes this"; `xrefs_to_field`
     for a struct-field marker.
   - **Snippet:** read `${CLAUDE_SKILL_DIR}/scripts/xref_map.py`, set CONFIG (`TARGET` = name or
     `0x…`; `MODE` = `auto` | `code` | `data` | `const`), run via the exec tool. It enumerates every
     xref, attributes each to its containing function, tags read/write/call/jump, and groups by caller.
3. **Assemble the map.** Per referencing function: name (canonical if known, else `sub_<ea>`), EA,
   touch count, reference kind. Note clusters (all refs in one subsystem).
4. **Save** to `Docs/RE/_dirty/queries/xref_map.<slug>.md` (snippet best-effort-writes it; else use
   Write). In your reply describe the fan-in in words and name the primary consumer + the next read.

**Decide:** a **hub** anchor (logger, allocator, singleton touched by hundreds) is noise → narrow to
write-xrefs only or one segment. A name with **multiple EAs** (overloads) → record all, pick the
recv/dispatch-path one as primary. **Ambiguous which caller fires at runtime** → `ida-debugger-drive`.

## Mode B — CALLGRAPH (bounded graph around one function)

Situate a function in its subsystem: walk **up** (callers, recursively) and **down** (callees) to a
bounded depth, render an edge list + a short per-node role description.

1. **Resolve the target** to a function start EA (typed `func_query` / `entity_query`, or the
   snippet resolver). Confirm it is a real function, not CRT/library code.
2. **Walk the graph, bounded.**
   - **Typed (preferred):** `mcp__ida__callgraph` / `mcp__ida__call_hierarchy` with `direction`
     (callers/callees/both) + `depth`; `mcp__ida__callees_recursive` / `mcp__ida__callers_recursive`
     for the bounded recursive fan-out/fan-in, and `mcp__ida__reaches` to answer "can A reach B?".
     If only `callees` + `xrefs_to` exist, BFS by hand (callees via `callees`, callers via `xrefs_to`
     filtered to call refs). For runtime-confirmed edges, `mcp__ida__trace_calls` (via `re-validator`).
   - **Snippet:** read `${CLAUDE_SKILL_DIR}/scripts/callgraph_map.py`, set CONFIG (`TARGET`;
     `DIRECTION` = `both` | `callers` | `callees`; `MAX_DEPTH`; `MAX_NODES`). It BFS-walks, dedups,
     caps node count, and prints an edge table + per-node summary (name, EA, in/out degree, lib flag).
3. **Keep it bounded.** Default `MAX_DEPTH` 2 and a `MAX_NODES` cap (~200). A hub (`malloc`, a logger)
   dominates with noise → narrow direction or lower depth; prune any subtree that crosses into the
   CRT/RTL.
4. **Save** to `Docs/RE/_dirty/static/callgraph.<slug>.md`. In your reply: which node is the
   subsystem entry point, which are leaf helpers, where the graph crosses into another subsystem, and
   the next function to decompile.

**Decide:** a **hub** → drop `MAX_DEPTH` to 1, pick one direction. A **leaf** → widen
`DIRECTION=callers` to find the feature's real entry. A **data-dependent edge** (virtual/indirect
call) → the static graph shows all candidates; confirm the live one via `ida-debugger-drive`.

## Mode C — DATA-FLOW (forward/backward trace from an address)

Follow a single observed value — a received byte, a parsed length, a computed offset — to the chain
of instructions that produce or consume it (exactly what a packet/format spec needs).

1. **Pin the start point.** Record the start EA, `DIRECTION` (`forward` | `backward`), and the value
   of interest — a register (`eax`), a stack slot, or an operand index. Confirm the EA is in a real
   function.
2. **Run the trace.**
   - **Typed (strongly preferred):** `mcp__ida__trace_data_flow` with start EA + direction + the
     operand/register selector — a real **interprocedural** trace, far more accurate than a snippet.
     Confirm any uncertain step with `disasm` / `basic_blocks`.
   - **Snippet:** read `${CLAUDE_SKILL_DIR}/scripts/data_flow.py`, set CONFIG (`START_EA`, `DIRECTION`,
     `REG`, `MAX_STEPS`). It does a bounded **intra-function** def/use walk — backward finds nearest
     definitions of `REG`, forward finds uses + propagating moves. It is conservative and **stops at
     calls and function boundaries**; it never fakes cross-function flow.
3. **Note the boundaries.** Flag every point where the value is (a) stored to memory/a struct field,
   (b) passed into a call (which arg slot), (c) crosses a function boundary, or (d) combined with
   another value — these are the facts a spec author needs.
4. **Save** to `Docs/RE/_dirty/static/dataflow.<slug>.md`. In your reply describe the chain in words
   (origin/sink, struct-field offsets, call-arg slots) and what it implies for the format/packet
   (hand to `spec-author`, or `ida-annotate` if it lands in an object field).

**Decide:** tracing a **post-cipher recv buffer** → the cleanest ground truth is dynamic; hand to
`ida-debugger-drive` to breakpoint just after decrypt and `dbg_read` the buffer pre/post. A **fork**
(value copied to two sinks) → record each sink as its own step. A **boundary crossing** with the
fallback snippet → stop, continue as a separate trace; never fake interprocedural flow.

## Mode D — BATCH (multi-function subsystem triage)

When recon / a callgraph / an xref fan-in hands you a *cluster* that looks like one subsystem,
profile them all at once and emit a per-function role summary — so you decompile only the 1–3 that
matter.

1. **Assemble the function set** as names or `0x…` addresses (from a callgraph cluster, an xref
   fan-in, an address range, or a string-evidence group). Give the subsystem a working label
   (`net_reader`, `pak_loader`, `actor_anim`).
2. **Run the batch analysis.**
   - **Typed (preferred):** `mcp__ida__analyze_batch` / `analyze_component` / `func_profile` with the
     set. Capture per function: name/EA, in/out call degree, size, notable imports/strings, role hint.
   - **Snippet:** read `${CLAUDE_SKILL_DIR}/scripts/batch_analyze.py`, set CONFIG (`TARGETS` list, OR
     `RANGE_START`/`RANGE_END` to sweep a range; `LABEL`). It computes per-function metrics
     (instruction + basic-block count, callers, callees, distinct imports/strings, loop density) and a
     per-function evidence list — metrics and references only, never bodies. For a binary-wide profiling
     sweep, the bundled `${CLAUDE_SKILL_DIR}/scripts/profile_all.py`, `profile_all_p1.py`, and
     `profile_all_sweep.py` are the broader survey variants.
3. **Tag roles, neutrally.** One-line role guess per function ("string-table loader", "fixed-size
   record parser", "dispatch fan-out", "leaf math helper") — explicitly hypotheses to confirm.
4. **Save** to `Docs/RE/_dirty/static/batch.<label>.md` (metrics table + per-function evidence/role).
   In your reply, rank the 1–3 most worth a deep read and point the next analyst at them
   (Mode E / DECOMPILE-ONE to read, Mode C to trace, or a specialist analyst).

**Decide:** a set **> ~30 functions** → split into batches; a sweep over hundreds dilutes guesses and
risks dumping the binary. A function with **recv/cipher/opcode** import evidence → flag as the
cluster's hot node, deep-read first. **Tied hypotheses** → which one actually runs is dynamic; hand
the candidate set to `ida-debugger-drive` for a breakpoint census. **Under an orchestrator**, fan out
**massively in parallel** — no `~3` sub-wave cap; IDB writes may run in parallel too (retry any
failed/conflicting call).

## Mode E — DECOMPILE-ONE (read exactly one function into the quarantine)

Once A–D have isolated the 1–3 functions that matter, read **one** of them: export its raw Hex-Rays
pseudo-C plus callers/callees/local types to `_dirty/functions/<name>.dirty.md` for a spec-author to
**describe in neutral prose** (never transcribe). This is the static-hypothesis half of clean-room
RE; the live `?ext=dbg` debugger confirms the layout before any spec asserts it.

> [!IMPORTANT]
> **The DECOMPILE-ONE artifact is CONTAMINATED** — verbatim copyright-tainted decompiler pseudo-C.
> It may live ONLY under `Docs/RE/_dirty/functions/`. NEVER commit it, paste it into C#, copy it into
> `Docs/RE/specs|packets|formats|structs|opcodes.md`, or echo any token of it into a reply. The only
> thing that crosses the firewall is a from-scratch neutral note a spec-author authors separately.

1. **Pick the target** — a canonical name from `Docs/RE/names.yaml` (prefer it; it survives rebases)
   or an address `0x…`. If the name is not yet applied to the DB, fall back to the address. **One
   function per invocation** — to map a call tree, re-run per discovered callee from Mode B.
2. **Run the export.**
   - **Typed (preferred for the body):** if `mcp__ida__decompile` exists, use it for the pseudo-C, but
     still gather xrefs + the SHA-256 (run the snippet, or `xrefs_to` / `callees`) — the dirty file
     needs callers, callees, and the build tag. To cite a finding precisely by line, pair
     `mcp__ida__map_ea_to_pseudocode` (EA → pseudocode line) and `mcp__ida__map_pseudocode_line_to_eas`
     (pseudocode line → EAs) so the analyst's neutral note references the exact line↔EA correspondence
     without copying the body.
   - **Snippet:** read `${CLAUDE_SKILL_DIR}/scripts/decompile_one.py`, set `TARGET` (name in quotes,
     or address as an int literal `0x004A1230`), run via the exec tool. It resolves the target,
     decompiles, collects xrefs to/from with names, captures prototype + local-var types, computes the
     input SHA-256, and prints one `DECOMP_JSON:` line. Capture that line.
3. **Write the dirty file.** Parse the JSON; create `Docs/RE/_dirty/functions/` if absent; write
   `<name>.dirty.md` (`<name>` = canonical name, else `sub_<addr>`). It MUST begin with this banner
   verbatim, then the content:

   ```
   > DIRTY — verbatim Hex-Rays pseudo-C from the legacy client (sha256 <full-sha>). COPYRIGHT-TAINTED.
   > Never commit. Never copy into C# or into Docs/RE/specs|packets|formats|structs|opcodes.md.
   > A spec-author must REWRITE behavior in neutral prose; this file is read-only reference.
   ```

   Then sections: `## Target` (name + address + prototype), `## Callers (xrefs to)`,
   `## Callees (xrefs from)`, `## Local types`, and a fenced ```` ```c ```` block with the raw body.
4. **Report** — resolved address, caller/callee counts, the SHA-256, and the dirty path; remind that
   promotion needs a fresh neutral note by a spec-author. **No pseudo-C in the reply, only counts.**

**Decide:** a body heavy on `*(_DWORD*)(this + N)` field accesses → shape is best confirmed live; flag
it for `ida-debugger-drive` (`dbg_add_bp` at the EA, then `dbg_gpregs` / `dbg_read`; never `dbg_start`).
**Garbled decompilation** (bad stack analysis, missing/wrong inferred prototype) → fix the
prototype/struct first (or fall back to disassembly); never transcribe garbled pseudo-C as fact.

## Mode F — MICROCODE (resolve indirect / virtual dispatch)

When a function dispatches through a register or a vtable slot (exactly what the protocol/opcode and
the actor virtual-call paths do), pseudo-C often shows an opaque `(*(funcptr))(...)`. The Hex-Rays
**microcode** exposes the resolved target cleaner than pseudocode does.

1. **Read the microcode.** `mcp__ida__microcode_text` for the function's microcode listing — far more
   explicit about computed call targets and register dataflow than the C view.
2. **Enumerate the calls.** `mcp__ida__microcode_calls` lists every call site with its resolved (or
   candidate) target — pair it with `mcp__ida__list_indirect_calls` to catch the dispatch sites a
   plain callgraph misses. This is the cleanest static read of an opcode→handler or virtual dispatch.
3. **Confirm the live target.** A static candidate set is a hypothesis; which target actually fires is
   dynamic — hand the candidates to `ida-debugger-drive` (`re-validator`). Microcode is dirty like
   pseudo-C: keep any verbatim listing in `_dirty/`, never in a reply or committed file.

## Verify / Done when

- The mode's artifact exists under the right `_dirty/` dir (`queries/` for XREF; `static/` for
  CALLGRAPH / DATA-FLOW / BATCH; `functions/` for DECOMPILE-ONE) with the expected shape (xref table /
  edge+node summary / step chain / metrics table / banner+four-sections+fenced pseudo-C).
- Every entry carries an EA and the mode's tag (reference-kind / in-out degree / boundary / role).
- The walk/sweep stayed **bounded**; ambiguity is reported as ambiguous, never silently resolved.
  DECOMPILE-ONE exported **exactly one** function and its file leads with the verbatim DIRTY banner;
  the JSON resolved a real address (not 0) and the SHA-256 matches the pinned build.
- The reply names the primary consumer / entry point / chain-implication / 1–3 deep-read targets (or,
  for DECOMPILE-ONE, the resolved address + caller/callee counts + SHA + path), and points at the next
  skill — and contains **no** pseudo-C, only counts and the path.
- No address or `sub_`/`loc_` symbol leaked outside `_dirty/`.

## Pitfalls (never)

- Never invent an xref, edge, flow step, metric, or pseudo-C line — a partial/failed call is reported as partial, not guessed.
- In modes A–D never paste a function body or disassembly — they produce structure (xrefs / edges / a step chain / metrics), not pseudo-C. Only **DECOMPILE-ONE** writes pseudo-C, and only into `_dirty/functions/` — never into a reply or any other file.
- Never bulk-dump a call tree with DECOMPILE-ONE — one function per invocation; re-run per discovered callee. A mass dump is both contaminating and unreviewable.
- Never emit an **unbounded** graph or sweep — always cap depth/nodes/range.
- Never extend the intra-function data-flow snippet to fake cross-function flow.
- Never treat a static fan-in / virtual edge / role guess / inferred prototype as the runtime truth — confirm in the debugger when it matters.

*North star N1: turns a marker, a function, a value, or a cluster into the exact few functions worth reading — the entry to every static-hypothesis pass the debugger then confirms.*

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/queries/` (XREF), `Docs/RE/_dirty/static/` (CALLGRAPH /
  DATA-FLOW / BATCH), or `Docs/RE/_dirty/functions/` (DECOMPILE-ONE). Never write to a committed RE
  spec, `names.yaml`, `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#** and never paste Hex-Rays pseudo-C into a reply. Modes A–D map *which*
  functions / *how* they connect / *where* a value goes / *what* a cluster does — not their bodies;
  DECOMPILE-ONE captures one body, but ONLY into a `_dirty/functions/` file an analyst reads — its
  promotion is a separate, deliberate clean-room rewrite by a spec-author, never done from this skill.
- Addresses appear only inside `_dirty/`. In replies, prefer canonical names + counts/role labels;
  use a bare address only when no name exists. Resolve `sub_…` names to *proposed* canonical names for
  `ida-annotate`'s names-sync mode; never rename here.
- MCP down / wrong DB / a failed or partial call ⇒ report exactly what you got and STOP. Never fabricate.
