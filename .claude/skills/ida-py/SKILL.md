---
name: ida-py
description: Use to run an ARBITRARY user-supplied IDAPython snippet against the live IDA Pro 9.3 database of the legacy Martial Heroes client (doida.exe; Main.exe historical) and capture its result — the escape hatch for any one-off RE query the fixed RE skills (ida-recon, ida-opcode-map, ida-crypto-hunt, ida-explore, ida-struct-recovery, ida-annotate) do not cover. Either fill the RESULT_JSON harness template for a freeform probe, or pick one of the bundled parameterized snippets ("who calls X", "what touches this global", "find crypto-shaped XOR/ROL/ROR loops", "find S-box-like constant tables", "where is this string referenced") and set its CONFIG. Wraps the snippet so it prints exactly one RESULT_JSON line and lands the result in Docs/RE/_dirty/queries/.
allowed-tools: mcp__ida__*, Read, Write
model: sonnet
effort: medium
---

# ida-py — run an arbitrary IDAPython snippet via the IDA MCP

This is the **general escape hatch**: when a question about the legacy client has no dedicated RE
skill, run IDAPython inside IDA through the MCP exec tool and capture a single machine-readable result
line. Two ways in — a **freeform harness template** for a one-off probe, and a **library of bundled
parameterized snippets** for the most common ad-hoc queries (callers-of, touches-global, crypto-shaped
bit-op loops, S-box-like const tables, string xrefs). Everything it touches is **dirty** (derived
directly from the copyrighted binary) and lands under `Docs/RE/_dirty/queries/`.

**Ground truth:** the result is authoritative **only** when the snippet ran against the correct,
populated Martial Heroes IDB (confirmed by `/ida-mcp-connect`). A snippet answers an open question
**from the binary** — never patch a gap from memory or analogy; a static hit is a **hypothesis** the
debugger confirms. If the MCP is down, the DB is wrong/empty, or a call returns partial data, **STOP
and report exactly what you got — never invent an address, name, or byte value to keep moving.**

Prefer a fixed skill when one fits — `ida-recon` (census + strings), `ida-opcode-map` (dispatcher),
`ida-crypto-hunt` (cipher), `ida-explore` (xref / callgraph / data-flow / subsystem-batch),
`ida-struct-recovery` (structs / vtables), `ida-annotate` (IDB writes — rename / comment / type).
Reach for `ida-py` only for the long tail those do not cover.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`. Never fabricate output from
   memory.
2. **Discover the exec tool name at runtime.** The exact `mcp__ida__*` names depend on the running
   build. List the available `mcp__ida__*` tools and pick the script-execution tool (commonly named
   like `mcp__ida__py_exec_file`, `mcp__ida__execute_script`, `mcp__ida__run_python`, or
   `mcp__ida__py_eval`). You will hand it the snippet's full source text. If only a tiny
   eval-expression tool exists, keep the snippet to a single expression; otherwise use the file/exec
   variant.

## Steps

1. **Read the harness** `${CLAUDE_SKILL_DIR}/scripts/ida_py_template.py` (also reachable as
   `scripts/ida_py_template.py`). It is a ready-to-fill IDAPython template: a `# === USER CODE ===`
   block where you place the snippet, a `result` dict you populate, and boilerplate that serializes
   `result` to one stdout line prefixed `RESULT_JSON:` and best-effort-writes it to
   `Docs/RE/_dirty/queries/`. The template never copies decompiler text — it returns metadata
   (addresses, names, counts, byte values you explicitly read).
2. **Fill the template.** Replace the `# === USER CODE ===` block with the analysis. Set
   `OUT_SLUG` at the top of the CONFIG block to a short, descriptive name for the query (e.g.
   `recv_buffer_size`, `font_atlas_loader`). Assign findings into the `result` dict — keep values
   JSON-serializable (numbers, hex-address strings, names, short string snippets you deliberately
   read). Do **not** dump whole functions of pseudo-C into `result`.
3. **Run via the discovered MCP exec tool.** Paste the full filled-in source. Capture the single
   line beginning `RESULT_JSON:` from the tool's return value and parse the JSON after the prefix.
   If the snippet raised, the harness prints `RESULT_JSON:{"ok": false, "error": "..."}` — read the
   error and fix the snippet rather than guessing.
4. **Save the result.** If the in-IDA best-effort write succeeded, confirm the path
   (`Docs/RE/_dirty/queries/ida_py.<slug>.json`). Otherwise use the Write tool to save the parsed
   JSON there yourself, plus a short `.md` sibling summarizing the finding in plain English.
5. **Interpret, neutrally.** In your reply, summarize what was found in words (counts, candidate
   addresses, observed shapes). Addresses are allowed only inside `_dirty/`. Flag any `sub_…` name
   you resolved as a proposed canonical name for `ida-annotate`'s names-sync mode — do not rename
   here. *Decision: if the question is really one of the named subsystems (dispatcher, cipher, xref
   graph, strings, struct, vtable), STOP and use that fixed skill — `ida-py` is only for the long tail.
   If the snippet needs to write into the IDB, it doesn't belong here (use `ida-annotate` — which now
   runs unbridled/parallel). If a fact needs live confirmation, hand the candidate EA to the debugger
   (`dbg_add_bp`/`dbg_read`; never `dbg_start`).*

## Bundled snippet library (the common ad-hoc probes)

For the recurring "no fixed skill, but a standard shape" questions, a small library of real,
parameterized IDAPython snippets lives under `${CLAUDE_SKILL_DIR}/scripts/snippets/`. Pick the closest,
set **only** its `# === CONFIG ===` block (never hand-edit the analysis logic below it — divergent
logic makes results unreviewable and irreproducible), and run it through the MCP exec tool. Each prints
a Markdown result block and best-effort-writes to `Docs/RE/_dirty/queries/<snippet>.<slug>.md`.

| Question | Snippet |
|---|---|
| Who calls function X (by name or address)? | `snippets/callers_of.py` |
| What functions read/write global/static G? | `snippets/touches_global.py` |
| Where are the crypto-shaped bit-twiddling loops (XOR/ROL/ROR/shift)? | `snippets/find_bitops_loops.py` |
| Where are the S-box / lookup-table-like constant byte/dword arrays? | `snippets/find_const_tables.py` |
| Where is string "..." used, and from what function? | `snippets/string_xref.py` |

Additional cartography/RTTI sweep snippets ship alongside (`snippets/module_cartography.py`,
`snippets/open_caller_census.py`, `snippets/cg_propagate_matcher.py`, `snippets/rtti_audit_wave.py`,
`snippets/rtti_place_wave.py`) for wider one-off surveys. **Escalation:** a `find_bitops_loops` /
`find_const_tables` hit that looks like the real cipher/S-box → `ida-crypto-hunt` (it fuses the
evidence); a probe that fans across a wide subsystem → `ida-explore`'s batch mode; a hot static hit
that needs proof → the debugger (`dbg_add_bp` at the EA, `dbg_read`/`dbg_gpregs`; never `dbg_start`).

## Verify / Done when

- The harness printed exactly one `RESULT_JSON:` line and it parsed (`ok: true`); on `ok: false` the
  error was read and the snippet fixed, not guessed around.
- `result` holds only metadata you explicitly gathered (addresses, names, counts, short byte/string
  reads) — no whole-function pseudo-C; output is under `Docs/RE/_dirty/queries/` with a slug.
- The serialization boilerplate was left intact (only CONFIG + USER CODE edited).

## Pitfalls

- **Never** dump disassembly or pseudo-C into `result` or the reply — reading one function closely is
  the quarantined job of `ida-explore` (DECOMPILE-ONE mode).
- Do not reinvent a fixed skill's wheel here — `ida-py` is the escape hatch, not the default.
- Do not edit the serialization boilerplate — a malformed `RESULT_JSON:` line is unparseable and the
  finding is lost.

> **N1:** the escape hatch keeps clean-room RE unblocked on the long tail of one-off questions while
> still landing every byte read in the `_dirty/` quarantine.

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/queries/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#.** This skill must not emit Hex-Rays pseudo-C into a reply or any file.
  If the analysis needs to read one function closely, that is the job of `ida-explore`
  (DECOMPILE-ONE mode) — the output stays in the quarantine and is described in neutral prose.
- Keep `result` to metadata you explicitly gathered. Reading a handful of bytes/strings is fine;
  transcribing whole disassembly/pseudo-code listings is not.
- Edit only the CONFIG block and the `# === USER CODE ===` block of the template — leave the
  serialization boilerplate intact so every result line is reliably parseable.
- Never invent addresses, names, or byte values. If a call fails or returns partial data, report
  exactly what you got and stop.
