---
name: ida-opcode-map
description: Use when you need to locate the packet dispatch table in the legacy Martial Heroes client (Main.exe) and recover the raw opcode -> handler-address map. Finds the large switch/jump table the network reader dispatches on, resolves each case to its handler function, and writes opcode/handler pairs to Docs/RE/_dirty/opcodes.raw.md. This is dirty-room recon only; promotion to the clean Docs/RE/opcodes.md is a separate step owned by the opcode-catalog skill.
allowed-tools: 'Read Write'
---

# ida-opcode-map — recover the packet dispatch table

The legacy client routes every inbound packet through a single dispatcher: read a
1- or 2-byte opcode off the decrypted buffer, then a `switch`/jump table selects the
handler. Recovering that table gives us the master opcode -> handler map, which is the
backbone of `Network.Protocol`'s source-generated routing.

This skill is **dirty-room only**. Its output (`Docs/RE/_dirty/opcodes.raw.md`) carries
IDA addresses and is gitignored. The clean catalog `Docs/RE/opcodes.md` (no addresses)
is produced separately by the **opcode-catalog** skill, which rewrites these findings
into neutral prose. Do not edit `Docs/RE/opcodes.md` from here.

## Preconditions

- IDA Pro 9.3 is open on the legacy client with auto-analysis finished.
- The IDA MCP server is reachable at `http://127.0.0.1:13337/mcp`, registered with
  Claude Code as `ida` (tools namespaced `mcp__ida__*`). If it is not registered, tell
  the user to run:
  `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`

## Steps

1. **Check connectivity.** List the available `mcp__ida__*` tools. If none resolve or
   every call errors, the server is down: stop and report the `claude mcp add` hint
   above. Do not fabricate results.

2. **Pick the execution path.** Prefer a script-exec tool (its name varies by build —
   look for something like `mcp__ida__execute_script`, `mcp__ida__run_python`, or
   `mcp__ida__eval`). If only typed tools exist (decompile / list_functions / xrefs /
   get_strings), fall back to step 5.

3. **Run the bundled finder.** Read `${CLAUDE_SKILL_DIR}/scripts/find_dispatch.py`, then
   paste its full source into the script-exec tool to run it inside IDA. The script:
   - enumerates indirect-jump / switch constructs via `idaapi.get_switch_info`, plus a
     fallback scan for dense jump-table arrays in `.rdata`/`.text`;
   - scores each candidate by case count, density, and whether its dispatch function
     is reachable from the socket-recv path (string/import heuristics for `recv`,
     `WSARecv`, `ProcessPacket`-like names);
   - resolves every case target to a function start and, where possible, a name;
   - emits a Markdown table of `opcode | handler_ea | handler_name | confidence`.

4. **Capture the output.** The script prints the report to IDA's output window AND, if
   the IDA process can write files, saves it directly to
   `Docs/RE/_dirty/opcodes.raw.md`. If file-write inside IDA is unavailable, copy the
   printed Markdown and use the Write tool to save it yourself to that exact path.
   Always preserve the header block the script emits (it records the candidate table EA,
   the dispatcher EA, opcode width, and base value — the provenance the spec-author needs).

5. **Typed-tool fallback (no script exec).** Using only typed tools:
   - find the recv path: search strings/imports for `recv`/`WSARecv`; walk xrefs to the
     function that reads the opcode field;
   - in that function, identify the jump/switch; list the case targets;
   - decompile each handler just far enough to name it; record `opcode | handler_ea |
     handler_name`. This is slower and lossier — prefer the script.

6. **Sanity-check before saving.** A real dispatch table for this client should have
   dozens of cases (chat, movement, combat, inventory, login...). If you found only a
   handful, you likely hit a sub-dispatcher (e.g. a category's secondary switch).
   Record it, note `partial: true` in the header, and keep scanning for the master table.

7. **Write the dirty report** to `Docs/RE/_dirty/opcodes.raw.md` (create parent dirs if
   needed). Append, never silently overwrite a richer prior run — if one exists, add a
   new dated section and let the spec-author reconcile.

8. **Hand off.** State plainly: "Raw map written to Docs/RE/_dirty/opcodes.raw.md;
   promotion to Docs/RE/opcodes.md is the opcode-catalog skill's job." Do not promote
   here.

## Hard rules

- Never write addresses, raw pseudo-code, or this file's contents into any committed
  path. `Docs/RE/_dirty/` is the only legal destination.
- Never edit `Docs/RE/opcodes.md`, `packets/`, or `journal.md` from this skill.
- If the dispatcher uses a function-pointer array (handler table) instead of a compiler
  `switch`, the script's `.rdata` array scan still finds it — record the array EA and the
  opcode-to-index relationship in the header.
