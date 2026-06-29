---
name: ida-mcp-connect
description: Use proactively before ANY IDA work and whenever mcp__ida__* tools fail; verifies server liveness, that the correct IDB SHA is loaded, and which endpoint (base vs ?ext=dbg) is active. Probes the local IDA Pro 9.3 MCP server (127.0.0.1:13337), reports UP/DOWN, enumerates the live mcp__ida__* toolset, and refuses to let analysis proceed until the server is reachable with the correct database open.
allowed-tools: Bash(python *), Bash(claude *), Read, mcp__ida__server_health, mcp__ida__domain_database_info
model: sonnet
effort: medium
---

# ida-mcp-connect

The legacy Martial Heroes client (`Main.exe`, 32-bit MSVC PE) is analyzed in **IDA Pro 9.3**,
which exposes an MCP server at `http://127.0.0.1:13337/mcp`. All RE skills/agents reach IDA through
`mcp__ida__*` tools — but those tools only exist when (a) IDA is running with the database open and
(b) the server is registered with Claude Code. This skill is the mandatory preflight: never start
recon, decompilation, opcode mapping, or crypto tracing without a green check here.

The exact `mcp__ida__*` tool names depend on the running IDA build, so this skill discovers them at
runtime rather than assuming a fixed set. The prized capability is arbitrary-IDAPython execution via
`mcp__ida__py_eval` (one-liner) / `mcp__ida__py_exec_file` (multi-statement harness) /
`mcp__ida__run_script` (with `save_script` / `read_script` / `list_scripts` to persist reusable
probes in the DB); typed tools (`decompile` / `rename` / `xrefs` / `list_strings`) are the fallback.

**This is the ground-truth gate.** IDA / `doida.exe` is the single absolute truth for the original's
behavior, data, and layout — everything downstream confirms its hypotheses *in the binary*, never from
memory or analogy. So no RE proceeds until this skill confirms (a) the MCP is UP, (b) the **correct,
non-empty** Martial Heroes IDB is loaded (right SHA-256, not another instance), and (c) ideally the
`?ext=dbg` debugger toolset is present for live confirmation. Down / wrong / empty ⇒ **STOP and report
— never fabricate `mcp__ida__*` output.** Prefer `?ext=dbg` always: it is a superset (static + `dbg_*`
from one connection).

## Steps

1. **Probe the socket.** Run the bundled stdlib probe:

   ```
   python ${CLAUDE_SKILL_DIR}/scripts/check_ida.py
   ```

   It opens a TCP connection to `127.0.0.1:13337` and prints `IDA MCP: UP` or `IDA MCP: DOWN`,
   exiting `0` when up and `1` when down. A relative invocation (`python scripts/check_ida.py` from
   the skill folder) works too.

2. **If DOWN — stop here.** The probe prints remediation; relay it:
   - Ensure IDA Pro 9.3 is open with the Martial Heroes database (`Main.exe.i64`) loaded and its
     MCP/server plugin started (the server only listens while a database is open).
   - If IDA is open but the tools are absent from Claude Code, the server is not registered. Run:
     ```
     claude mcp add --transport http ida http://127.0.0.1:13337/mcp
     ```
     then restart the Claude Code session so the `mcp__ida__*` tools load.
   **Do NOT proceed to any analysis while DOWN.** Reverse engineering with a dead server produces
   silent garbage. Hand back to the user with the exact fix.

3. **If UP — call `server_health`.** Run `mcp__ida__server_health`: it reports whether the server is
   up and Hex-Rays/analysis are ready (`auto_analysis_ready` / `hexrays_ready` / `strings_cache`).
   If `auto_analysis_ready` is false, recovery results are unreliable — wait/warn. If `hexrays_ready`
   is false, decompile calls will fail until warm-up. A reachable socket alone is NOT a loaded IDB.

4. **Confirm the CORRECT IDB via `domain_database_info`.** Run `mcp__ida__domain_database_info`: it
   returns the open database's `idb_path` / `module` / `imagebase` and — critically — the input file
   **SHA-256**. Confirm `module`/`idb_path` is the `doida.exe` client (not `Main.exe`, an empty, or
   another instance) and **capture the SHA-256 to pin** — every downstream spec/journal cites this one
   build. If it looks wrong or empty, STOP and warn before any analysis.

5. **Detect the endpoint (base vs `?ext=dbg`).** Enumerate the live `mcp__ida__*` toolset and check
   for the debugger tools `mcp__ida__dbg_status` / `mcp__ida__dbg_threads`:
   - **Present** ⇒ the session is on the `?ext=dbg` superset (static + dynamic from one connection) —
     the preferred state.
   - **Absent** ⇒ the session is on the base (static-only) endpoint. Re-register on `?ext=dbg` and
     restart so ground-truth debugger confirmation is available:
     ```
     claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"
     ```
   Also classify the rest of the toolset so downstream skills know how to call IDA:
   - **Script-exec** (`mcp__ida__py_eval` / `py_exec_file` / `run_script` + `save_script` /
     `read_script` / `list_scripts`) — bundled IDAPython snippets run through these.
   - **Typed** (`decompile`, `rename`, `xrefs_to`, `domain_strings`, `func_query`, …) — the fallback.

6. **Warm the DB.** Call `mcp__ida__refresh_strings_cache` once so the first string-driven query in
   the session (recon / opcode / crypto hunts all lean on strings) is not paying a cold-cache cost.

7. **Point the analyst at the CAPABILITY MAP.** Before any recovery call, read the bundled toolbox
   reference so the right tool is chosen for the task (and the firewall lanes are respected):

   ```
   Read ${CLAUDE_SKILL_DIR}/references/ida-mcp-toolbox.md
   ```

   It is a neutral, categorized map of the live `mcp__ida__*` families (PREFLIGHT · SURVEY/RECON ·
   SEARCH · NAV/XREF/FLOW · DECOMPILE/DISASM · TYPES/STRUCTS · IDB-WRITE · IDAPYTHON · DEBUGGER ·
   DIFF) plus a "which tool for which RE task" table and the G0→G4 gate chain. **`survey_binary` is the
   FIRST recovery call of any new investigation.**

8. **Green light.** Only when the socket is UP, `server_health` confirms a sane non-empty `doida.exe`
   IDB, and the toolbox map is loaded: report "IDA session ready", note `module` + function count +
   whether `dbg_*` is present (`?ext=dbg`), and hand off to the requested RE workflow.

## Verify / Done when

- The probe printed `IDA MCP: UP`, the `mcp__ida__*` tools resolve, and `server_health` (or a one-liner)
  confirmed a sane, non-empty IDB whose `module`/`idb_path` is the `doida.exe` client (not another
  instance), with `auto_analysis_ready` true.
- The available tool category (script-exec / debugger / typed) is reported, and whether `dbg_*` is
  present (i.e. on `?ext=dbg`).
- The capability map (`references/ida-mcp-toolbox.md`) was read before recovery began, so the right
  tool family is chosen and the firewall lanes (decompile→`_dirty/`, IDB-write→`ida-toolsmith`,
  `dbg_*`→`re-validator`) are respected.
- If anything failed, the exact remediation was handed back and **no** analysis was allowed to start.

## Pitfalls

- **Never** fabricate `mcp__ida__*` results when DOWN — refusing is the correct, safe behavior.
- A reachable socket is not a loaded IDB — always confirm the database, or downstream skills produce
  silent garbage.
- Do not settle for the base endpoint when debugger confirmation is needed — missing `dbg_*` means
  re-register on `?ext=dbg`.

> **N1:** this preflight is the gate that keeps clean-room RE honest — no recon, decompile, or
> debugger confirmation proceeds without a live, correct IDA session.

## Hard rules

- This skill never reads or writes RE specs and never analyzes code itself — it is connectivity only.
- Never fabricate `mcp__ida__*` results when the server is DOWN; refusing is the correct behavior.
- Treat everything pulled from IDA as **dirty** — it lands under `Docs/RE/_dirty/` (gitignored),
  never in committed specs or C#. Promotion across the firewall is a separate, deliberate rewrite.
