---
name: ida-py
description: Use to run an ARBITRARY user-supplied IDAPython snippet against the live IDA Pro 9.3 database of the legacy Martial Heroes client (Main.exe) and capture its result — the escape hatch for any one-off analysis the fixed RE skills (ida-recon, ida-opcode-map, ida-crypto-hunt, ida-xref-map, ida-callgraph-map, ida-data-flow, ida-batch-analyze, ida-string-hunt, ida-struct-apply, ida-vtable-recover) do not cover. Wraps the snippet so it prints exactly one RESULT_JSON line and lands the result in Docs/RE/_dirty/queries/.
allowed-tools: Read Write
model: sonnet
---

# ida-py — run an arbitrary IDAPython snippet via the IDA MCP

This is the **general escape hatch**: when a question about the legacy client has no dedicated RE
skill, write a small IDAPython snippet, run it inside IDA through the MCP exec tool, and capture a
single machine-readable result line. Everything it touches is **dirty** (derived directly from the
copyrighted binary) and lands under `Docs/RE/_dirty/queries/`.

Prefer a fixed skill when one fits — `ida-recon` (census), `ida-opcode-map` (dispatcher),
`ida-crypto-hunt` (cipher), `ida-xref-map` / `ida-callgraph-map` / `ida-data-flow` (graph + flow),
`ida-batch-analyze` (subsystem), `ida-string-hunt` (strings), `ida-struct-apply` /
`ida-vtable-recover` (objects). Reach for `ida-py` only for the long tail those do not cover.

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
   you resolved as a proposed canonical name for `ida-naming-sync` — do not rename here.

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/queries/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#.** This skill must not emit Hex-Rays pseudo-C into a reply or any file.
  If the analysis needs to read one function closely, that is `ida-decompile-export`'s job — the
  output stays in the quarantine and is described in neutral prose.
- Keep `result` to metadata you explicitly gathered. Reading a handful of bytes/strings is fine;
  transcribing whole disassembly/pseudo-code listings is not.
- Edit only the CONFIG block and the `# === USER CODE ===` block of the template — leave the
  serialization boilerplate intact so every result line is reliably parseable.
- Never invent addresses, names, or byte values. If a call fails or returns partial data, report
  exactly what you got and stop.
