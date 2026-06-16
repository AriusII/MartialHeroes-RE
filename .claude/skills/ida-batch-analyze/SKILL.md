---
name: ida-batch-analyze
description: Use to analyze a whole candidate subsystem of the legacy Martial Heroes client (Main.exe) in one pass — a set of related functions (a network reader cluster, an asset-loader family, a UI module) — and produce a neutral per-function role summary. Drives the typed mcp__ida__analyze_batch / mcp__ida__analyze_component / mcp__ida__func_profile tools (falling back to a bundled per-function metrics IDAPython snippet) and writes the summary to Docs/RE/_dirty/static/. The way to triage many functions at once before deep-reading any.
allowed-tools: Read Write
model: sonnet
effort: high
---

# ida-batch-analyze — multi-function subsystem triage

When a recon pass (`ida-recon`), a call graph (`ida-callgraph-map`), or an xref map (`ida-xref-map`)
hands you a *cluster* of functions that look like one subsystem, this skill analyzes them all at once
and emits a per-function role summary — call/xref degree, string/import evidence, size, and a
one-line plain-English guess at each function's purpose. It is the triage step that decides which one
or two functions are worth a close read (`ida-decompile-export`), so you do not decompile twenty
functions to find the three that matter.

All output is **dirty** (addresses derived directly from the copyrighted binary) and lands under
`Docs/RE/_dirty/static/`.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. Prefer the batch/component
   tools when present — names vary by build but look like `mcp__ida__analyze_batch`,
   `mcp__ida__analyze_component`, `mcp__ida__func_profile`, `mcp__ida__survey_binary`. If none
   exist, use the script-exec tool with the bundled snippet.

## Steps

1. **Assemble the function set.** Gather the candidate functions as a list of names or `0x…`
   addresses — from a callgraph cluster, an xref fan-in, an address range, or a string-evidence
   group (`ida-string-hunt`). Give the subsystem a working label (e.g. `net_reader`,
   `pak_loader`, `actor_anim`).
2. **Run the batch analysis.**
   - **Typed path (preferred):** call `mcp__ida__analyze_batch` (or `analyze_component`) with the
     function set. Capture, per function: name/EA, in/out call degree, size, notable imports/strings
     referenced, and any role hint the tool returns.
   - **Snippet path (fallback):** read `${CLAUDE_SKILL_DIR}/scripts/batch_analyze.py`, set CONFIG
     (`TARGETS` list of names/addresses, OR `RANGE_START`/`RANGE_END` to sweep an address range;
     `LABEL`). It computes per-function metrics — instruction count, basic-block count, callers,
     callees, distinct imports called, distinct strings referenced, loop density — and emits a table
     plus a per-function evidence list. It deliberately collects *metrics and references*, never
     function bodies.
3. **Tag roles, neutrally.** From the metrics + evidence, write a one-line role guess per function
   ("string-table loader", "fixed-size record parser", "dispatch fan-out", "leaf math helper"). Be
   explicit that these are hypotheses to confirm, not facts.
4. **Save it.** The snippet best-effort-writes `Docs/RE/_dirty/static/batch.<label>.md`; confirm the
   path or save the Markdown yourself. Include the metrics table and the per-function evidence/role.
5. **Rank and hand off.** In your reply, name the 1–3 functions most worth a deep read and why, and
   point the next analyst at them (`ida-decompile-export` to read, `ida-data-flow` to trace, or a
   specialist analyst). Resolve `sub_…` names to proposed canonical names for `ida-naming-sync`;
   never rename here.

## Decision points

- **If the set exceeds ~30 functions**, split into batches — a sweep over hundreds dilutes the role
  guesses and risks dumping the binary. Narrow the range or process in passes.
- **If a function references recv/cipher/opcode markers** (high import degree on socket/crypto APIs),
  flag it as the cluster's likely hot node — it's the one to deep-read first.
- **If two role hypotheses tie on static metrics**, the tiebreaker is dynamic: which one actually runs
  on the path under test. Hand the candidate set to `ida-debugger-drive` for a breakpoint census in
  the maintainer's F9 session before committing analyst time to a deep read.
- **When fanned out under an orchestrator**, fan out **massively in parallel** — there is no `~3` sub-wave cap; IDB writes may run in parallel too (retry any failed/conflicting call).

## Verify / Done when

- The metrics table + per-function evidence/role exist in `batch.<label>.md` under `_dirty/static/`.
- Each role is phrased as a hypothesis, not a fact; the reply ranks the 1–3 worth a deep read with why.
- No address leaked outside `_dirty/`; the sweep stayed bounded.

## Pitfalls (never)

- Never dump the whole binary or an unbounded range — triage is targeted.
- Never present a role guess as confirmed; it's a hypothesis a deep read (then debugger) settles.
- Never paste any function body; this skill collects metrics + references only.

*North star N1: triages many functions to the few worth reading — the static hypothesis the deep read and debugger then confirm.*

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/static/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#** and never paste Hex-Rays pseudo-C into a reply or any file. This skill
  produces metrics + evidence + role *hypotheses*, not transcribed code. Confirming a role by reading
  the body is `ida-decompile-export`'s job.
- Addresses appear only inside `_dirty/`. In replies, prefer canonical names and role labels.
- Bound the sweep: if a range covers hundreds of functions, narrow it or process in batches. Never
  dump the whole binary. If a call fails or returns partial data, report exactly what you got.
- Role tags are explicitly hypotheses — phrase them as such; a spec author confirms before promotion.
