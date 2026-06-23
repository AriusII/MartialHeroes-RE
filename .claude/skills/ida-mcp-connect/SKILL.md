---
name: ida-mcp-connect
description: Use before any IDA reverse-engineering session, or when mcp__ida__* tools are unavailable / failing. Probes the local IDA Pro 9.3 MCP server (127.0.0.1:13337), reports UP/DOWN, enumerates the live mcp__ida__* toolset, and refuses to let analysis proceed until the server is reachable with a database open.
allowed-tools: Bash(python *) Bash(claude *) Read
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
runtime rather than assuming a fixed set. The prized capability is an arbitrary-IDAPython execution
tool (often named like `execute_script` / `run_python` / `eval`); typed tools
(`decompile` / `rename` / `xrefs` / `list_strings`) are the fallback.

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

3. **If UP — enumerate the live toolset.** List the `mcp__ida__*` tools currently exposed to this
   session (from the tool manifest / system reminders). Classify them:
   - **Script-exec** (preferred): any tool that runs arbitrary IDAPython — look for names containing
     `execute`, `script`, `run`, `python`, or `eval`. Bundled IDAPython snippets from other RE
     skills are meant to run through this.
   - **Debugger** (`mcp__ida__dbg_*`): present only on the `?ext=dbg` endpoint. *Decision: if the
     `dbg_*` tools are ABSENT, the session is on the base endpoint — re-register on `?ext=dbg`
     (`claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`) and restart, so
     ground-truth confirmation is available. The `?ext=dbg` endpoint is a superset; prefer it always.*
   - **Typed fallbacks**: `decompile`, `rename`/`set_name`, `xrefs`, `list_strings`,
     `get_function`, etc. Use these when no script-exec tool is present.
   Report which category is available so downstream skills know how to call IDA.

4. **Confirm a database is actually loaded.** A reachable socket is not the same as a loaded IDB.
   If a script-exec tool exists, run a one-liner sanity check (e.g. read `idaapi.get_root_filename()`
   / count functions via `idautils.Functions`) to confirm the open database is the Martial Heroes
   client and not an empty/other IDA instance. If a typed tool exists instead, call the cheapest one
   (e.g. list a few strings) to confirm responses are real. If the database looks wrong or empty,
   warn the user before continuing.

5. **`server_health` — the structured UP report.** When a `server_health` tool exists, call it: it
   returns `idb_path` / `module` / `imagebase` / `auto_analysis_ready` / `hexrays_ready` /
   `strings_cache`. This is the authoritative confirmation that the **correct, non-empty** Martial
   Heroes IDB is loaded (`module`/`idb_path` is the `doida.exe` client) and that analysis is ready.
   If `auto_analysis_ready` is false, recovery results are unreliable — wait/warn. If `hexrays_ready`
   is false, decompile calls will fail until warm-up.

6. **`server_warmup` (optional, recommended).** If the report shows Hex-Rays uninitialized or caches
   cold, call `server_warmup` (init Hex-Rays / build caches) once so the first heavy
   decompile/xref/string query in the session is not paying cold-start cost. Skip if already warm.

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
