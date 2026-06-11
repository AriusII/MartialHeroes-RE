---
name: ida-script-runner
description: Use for ad-hoc reverse-engineering queries against the legacy Martial Heroes client (Main.exe) that no fixed skill covers — e.g. "who calls function X", "what touches this global", "find crypto-shaped XOR/ROL/ROR loops", "find S-box-like constant tables", "where is this string referenced". This is the explicit run-arbitrary-IDAPython-to-go-faster skill: pick the closest bundled snippet, set its CONFIG, run it through the IDA MCP exec tool, and drop results in Docs/RE/_dirty/queries/.
allowed-tools: 'Read Write'
---

# ida-script-runner — ad-hoc IDAPython queries

When a question about the legacy client has no dedicated skill (ida-opcode-map for the
dispatcher, ida-crypto-hunt for the cipher), reach for this. It ships a small library of
real, parameterized IDAPython snippets you run inside IDA via the MCP exec tool. Results
are dirty-room recon and go to `Docs/RE/_dirty/queries/`.

## When to use which snippet

| Question | Snippet |
|---|---|
| Who calls function X (by name or address)? | `callers_of.py` |
| What functions read/write global/static G? | `touches_global.py` |
| Where are the crypto-shaped bit-twiddling loops (XOR/ROL/ROR/shift)? | `find_bitops_loops.py` |
| Where are the S-box / lookup-table-like constant byte/dword arrays? | `find_const_tables.py` |
| Where is string "..." used, and from what function? | `string_xref.py` |

For full cipher recovery, prefer **ida-crypto-hunt** (it fuses bitop + recv-xref +
const-table into one report). Use the individual snippets here for narrower probes.

## Preconditions

- IDA Pro 9.3 open on the legacy client, auto-analysis finished.
- IDA MCP server at `http://127.0.0.1:13337/mcp`, registered with Claude Code as `ida`
  (`mcp__ida__*`). If absent: `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.

## Steps

1. **Check connectivity.** List `mcp__ida__*` tools. If none resolve, the server is down —
   report the `claude mcp add` hint and stop.

2. **Pick the snippet** from the table above. All live under
   `${CLAUDE_SKILL_DIR}/scripts/snippets/`.

3. **Read it and set CONFIG.** Each snippet has a `# === CONFIG ===` block at the top with
   plainly named variables (e.g. `TARGET = "CNetwork::Recv"` or `GLOBAL_EA = 0x00ABCDEF`).
   Edit those values to your query — never hand-edit the logic below the config block.
   Accept either a symbol name or a hex address where a snippet says so.

4. **Run via the IDA MCP exec tool.** Prefer the script-exec tool (name varies by build:
   `mcp__ida__execute_script` / `mcp__ida__run_python` / `mcp__ida__eval`). Paste the full
   edited snippet source. Each snippet prints a Markdown result block and attempts a
   best-effort write to `Docs/RE/_dirty/queries/<snippet>.<slug>.md`.

5. **Save the result.** If the in-IDA write succeeded, confirm the path. Otherwise copy the
   printed Markdown and use the Write tool to save it under `Docs/RE/_dirty/queries/` with a
   descriptive name, e.g. `callers_of.CNetwork_Recv.md`, `bitops.recv_region.md`.

6. **Interpret, neutrally.** In your reply, summarize what was found in plain language
   (counts, candidate addresses, shapes). Do NOT paste decompiled pseudo-code into your
   reply or any committed file — describe behavior. Addresses are allowed only inside
   `Docs/RE/_dirty/`.

## Snippet reference

- **callers_of.py** — resolves `TARGET` (name or EA), lists every code xref to it with the
  calling function name/EA, and flags direct `call` vs. data reference.
- **touches_global.py** — resolves `GLOBAL_EA`/`GLOBAL_NAME`, lists every function with a
  data xref, tagged read vs. write where the instruction makes it clear.
- **find_bitops_loops.py** — scans functions for loops dense in `xor`/`rol`/`ror`/`shl`/
  `shr`/`and`/`add` with a per-byte stride; reports the tightest cipher-shaped loops with
  an instruction-mix fingerprint. Restrict scope with `SCOPE_FUNC` (optional).
- **find_const_tables.py** — finds 256-entry byte tables and 256-entry dword tables (classic
  S-box / CRC / permutation shapes), reports EA, element size, entropy hint, and whether the
  table is a permutation of 0..255.
- **string_xref.py** — finds strings matching `NEEDLE` (substring, case-insensitive) and
  lists each reference with the referencing function.

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/queries/`. Never write to committed RE specs or C#.
- Never paste Hex-Rays pseudo-code into a reply or any file. Behavior in words, addresses
  only inside `_dirty/`.
- Edit only the CONFIG block of a snippet, never the analysis logic, so results stay
  reproducible and reviewable.
