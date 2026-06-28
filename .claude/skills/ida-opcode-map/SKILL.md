---
name: ida-opcode-map
description: Use when you need to locate the packet dispatch table in the legacy Martial Heroes client (doida.exe; Main.exe historical) and recover the raw opcode -> handler-address map. Leads with mcp__ida__recipe_dispatch_scan; handles MSVC two-level index tables. Finds the large switch/jump table the network reader dispatches on, resolves each case to its handler function, and writes opcode/handler pairs to Docs/RE/_dirty/opcodes.raw.md. This is dirty-room recon only; promotion to the clean Docs/RE/opcodes.md is a separate step owned by the opcode-catalog skill.
allowed-tools: mcp__ida__*, Read, Write
model: sonnet
effort: high
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

## Ground truth

The opcode→handler map is read FROM the binary in IDA — never reconstructed from memory, a prior
build's table, or analogy to "how MMORPGs usually dispatch." A static map is a hypothesis the live
debugger confirms packet-by-packet (`ida-debugger-drive`); when static and a debugger hit disagree,
the debugger (the running client) wins. MCP down / wrong DB ⇒ STOP, never fabricate pairs.

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

2. **Lead with `recipe_dispatch_scan`.** Run `mcp__ida__recipe_dispatch_scan` first — it is the
   purpose-built dispatch finder: it locates switch/jump tables and function-pointer handler arrays,
   scores them by case count and recv-path reachability, and resolves each case to its handler. This
   is the PRIMARY path; fall back to the manual set (step 3) or the bundled snippet (step 4) only when
   the recipe returns nothing. Note `mcp__ida__microcode_calls` often resolves the computed dispatch
   target cleaner than a switch reconstruction.

3. **Manual fallback set (typed).** If the recipe is thin, work it by hand:
   `mcp__ida__list_indirect_calls` (every computed call/jump — the dispatch sites), `mcp__ida__insn_query`
   (find the opcode-read + index-compute instructions), `mcp__ida__basic_blocks` (the dispatcher's CFG
   around the jump), and `mcp__ida__find_xref_signatures` (a build-stable signature for the dispatcher
   so the table survives a re-export / build mismatch).
   - **MSVC two-level-switch caveat:** a large/sparse MSVC `switch` compiles to a u8 **index table**
     that indexes the real jump table — read BOTH the index (value/lowcase) table and the jump table or
     the opcodes mis-map. An if/else ladder or a function-pointer array returns **no** switch info at
     all — walk it as data (the indirect-call sites + the `.rdata` pointer array), not as a `switch`.

4. **Bundled snippet (last resort).** Only when steps 2–3 come up empty, read
   `${CLAUDE_SKILL_DIR}/scripts/find_dispatch.py` and run it via `mcp__ida__py_exec_file`. It
   enumerates switch constructs (`ida_nalt.get_switch_info`) plus a fallback `.rdata`/`.text`
   jump-table scan, scores candidates by recv-path reachability, resolves case targets, and emits the
   `RESULT_JSON` line (harness contract — see `ida-python-lib`) with the `opcode | handler_ea |
   handler_name | confidence` table.

5. **Capture the output.** The recipe/snippet report — if the IDA process can write files — saves
   directly to `Docs/RE/_dirty/opcodes.raw.md`. If file-write inside IDA is unavailable, copy the
   Markdown and use the Write tool to save it yourself to that exact path. Always preserve the header
   block (it records the candidate table EA, the dispatcher EA, opcode width, and base value — the
   provenance the spec-author needs).

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

## Decision points

- **If you find only a handful of cases**, you hit a sub-dispatcher — note `partial: true` and keep
  scanning for the master table (a real client has dozens: chat, movement, combat, inventory, login).
- **If the dispatcher is a function-pointer array** not a compiler `switch`, the `.rdata` scan still
  finds it — record the array EA and the opcode→index relationship in the header.
- **If a case→handler mapping is ambiguous statically** (computed index, virtual dispatch), confirm it
  dynamically: hand off to `ida-debugger-drive` — breakpoint the dispatcher in the maintainer's live
  F9 session, send/observe a known packet, and read which handler the opcode lands on. Recall the wire
  shape: 8-byte frame `[u32 size][u16 major][u16 minor]`, opcode `(major<<16)|minor`. Static forms the
  table; the debugger confirms the live routing.

## Verify / Done when

- The header records the dispatcher EA, candidate table EA, opcode width, and base value (the provenance).
- The table has dozens of `opcode | handler_ea | handler_name | confidence` rows, or is tagged `partial: true`.
- `Docs/RE/_dirty/opcodes.raw.md` is appended (not silently overwriting a richer prior run).
- No address or pseudo-C leaked outside `_dirty/`; `Docs/RE/opcodes.md` untouched.

## Pitfalls (never)

- Never fabricate opcode/handler pairs when the MCP is down — STOP and surface the connect hint.
- Never promote here — the clean catalog is `opcode-catalog`'s job.
- Never overwrite a richer prior run; add a dated section and let the spec-author reconcile.

*North star N1: recovers the opcode→handler backbone of Network.Protocol's routing — the table a debugger then confirms packet-by-packet.*

## Hard rules

- Never write addresses, raw pseudo-code, or this file's contents into any committed
  path. `Docs/RE/_dirty/` is the only legal destination.
- Never edit `Docs/RE/opcodes.md`, `packets/`, or `journal.md` from this skill.
- If the dispatcher uses a function-pointer array (handler table) instead of a compiler
  `switch`, the script's `.rdata` array scan still finds it — record the array EA and the
  opcode-to-index relationship in the header.
