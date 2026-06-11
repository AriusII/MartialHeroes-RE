---
name: ida-recon
description: Use when starting analysis of the legacy Martial Heroes Main.exe and you need a baseline census — segments, imports, exports, entry points, strings, and named globals — before drilling into any single function. Produces a pinned SHA-256 and a navigable map of the binary.
allowed-tools: Read Write
model: sonnet
---

# ida-recon — baseline census of the legacy client

Produces the first map of the legacy 32-bit `Main.exe`: segment table, import/export
tables, entry points, the full string table, and named (non-default) globals. The dump is the
starting point every later RE session navigates from, and it **pins the binary's SHA-256** so all
downstream specs cite one exact build.

All output is **dirty** (derived directly from the binary) and lands under
`Docs/RE/_dirty/recon/`. Nothing here is committed. Only a separately authored neutral spec may
cross the firewall into `Docs/RE/specs|formats|structs|opcodes.md`.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run the `/ida-mcp-connect` skill and confirm it reports a live IDA Pro
   9.3 MCP server at `http://127.0.0.1:13337/mcp` with the target IDB open. If it is red, STOP and
   surface the hint: `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`. Do not
   fabricate a census from memory.
2. **Discover the exec tool name at runtime.** The exact `mcp__ida__*` tool names depend on the
   running build. List the available `mcp__ida__*` tools and pick the script-execution tool
   (commonly named like `mcp__ida__execute_script`, `mcp__ida__run_python`, or `mcp__ida__eval`).
   You will hand it the bundled IDAPython snippet's source as a string. If no exec tool exists,
   fall back to typed tools (`list_segments` / `list_imports` / `list_strings` / `list_names`) and
   assemble the same JSON shape by hand.

## Steps

1. Read the bundled IDAPython snippet `${CLAUDE_SKILL_DIR}/scripts/recon_dump.py` (also reachable as
   `scripts/recon_dump.py` relative to this skill). It is real, runnable IDAPython using
   `idautils` / `idaapi` / `ida_*`.
2. Feed the snippet's **full source text** to the discovered MCP script-exec tool. The snippet
   writes nothing to disk by itself inside IDA's restricted context — instead it builds one JSON
   document and prints it to stdout as a single line prefixed with `RECON_JSON:`. Capture that line
   from the tool's return value.
3. Parse the JSON. Confirm it contains `binary.sha256` (64 hex chars), `segments`, `imports`,
   `exports`, `entrypoints`, `strings`, and `globals`. If `sha256` is empty the snippet could not
   read the input file path — re-run with the input file available to IDA.
4. **Tag every output with the SHA-256.** Create `Docs/RE/_dirty/recon/` if absent. Write:
   - `Docs/RE/_dirty/recon/recon-<sha8>.json` — the raw JSON document (`<sha8>` = first 8 hex of
     the SHA-256), and
   - `Docs/RE/_dirty/recon/recon-<sha8>.md` — a human-readable Markdown summary built from the JSON
     (counts table, segment list, suspicious/interesting strings, named globals). Begin the file
     with a `> DIRTY — derived from Main.exe; never commit; do not copy into specs.` banner and the
     full SHA-256.
5. Report back to the user: the full SHA-256, segment count, import/export counts, string count,
   global count, and the two output paths. Note whether `Docs/RE/names.yaml`'s `binary.sha256` is
   empty so a maintainer/spec-author can pin it.

## Hard rules

- **Do not** decompile or paste pseudo-C here — that is the `ida-decompile-export` skill's job.
  This skill only enumerates metadata (names, addresses, strings, segment ranges).
- Output paths must always be inside `Docs/RE/_dirty/recon/`. Never write under
  `Docs/RE/specs|packets|formats|structs|opcodes.md|names.yaml` from this skill.
- Never invent addresses, string contents, or SHA-256 digits. If the MCP call fails or returns
  partial data, report exactly what you got and stop.
- The raw census is contaminated. Promotion to a committed spec is a separate, deliberate act
  performed by a spec-author who *rewrites* in neutral prose, never copies.
