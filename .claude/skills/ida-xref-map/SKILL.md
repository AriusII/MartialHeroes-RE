---
name: ida-xref-map
description: Use to follow ALL cross-references to a string, global, constant, or function in the legacy Martial Heroes client (Main.exe) and produce a neutral call-path map — "who reaches this, from where". Drives the typed mcp__ida__xref_query / mcp__ida__xrefs_to / mcp__ida__trace_data_flow tools (falling back to a bundled IDAPython snippet) and writes the reference map to Docs/RE/_dirty/queries/. The fast way to find the code that consumes a given marker.
allowed-tools: Read Write
model: sonnet
---

# ida-xref-map — map every reference to a target

Given a single anchor — a literal string, a named global/static, a constant value, or a function —
this skill enumerates every cross-reference and the function each reference lives in, so you can see
the full fan-in ("everything that touches this"). It is how you turn a recognizable marker (an error
string, a config key, a magic constant, a known import) into the set of functions worth reading.

All output is **dirty** (addresses derived directly from the copyrighted binary) and lands under
`Docs/RE/_dirty/queries/`.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. Prefer the typed xref tools
   when present — names vary by build but look like `mcp__ida__xref_query`, `mcp__ida__xrefs_to`,
   `mcp__ida__xrefs_to_field`, and `mcp__ida__trace_data_flow`. If no typed xref tool exists, use
   the script-exec tool (`mcp__ida__py_exec_file` / `execute_script` / `run_python`) with the
   bundled snippet.

## Steps

1. **Resolve the anchor to an address.** If you were given a name, resolve it (typed `get_name_ea`
   equivalent, or `mcp__ida__entity_query` / `mcp__ida__list_globals`). If a literal string, find it
   first (typed `mcp__ida__find` / `mcp__ida__get_string`, or the `ida-string-hunt` skill) and use
   its data address. If a raw constant, you want immediate-operand references — note that and use the
   snippet's `CONST` mode. Record the resolved EA.
2. **Walk the references.**
   - **Typed path (preferred):** call `mcp__ida__xref_query` / `mcp__ida__xrefs_to` on the EA to get
     every code/data xref. For a deeper "who ultimately feeds/consumes this value" view, call
     `mcp__ida__trace_data_flow` from the EA — it follows the value through moves, not just the first
     hop. For a struct-field marker use `mcp__ida__xrefs_to_field`.
   - **Snippet path (fallback):** read `${CLAUDE_SKILL_DIR}/scripts/xref_map.py`, set its CONFIG
     (`TARGET` = name or `0x…` address; `MODE` = `auto` | `code` | `data` | `const`), and run it via
     the exec tool. It enumerates every xref, attributes each to its containing function, tags
     read/write/call/jump where the instruction makes it clear, and groups by caller function.
3. **Assemble the map.** For each referencing function: its name (canonical if known, else
   `sub_<ea>`), its EA, how many times it touches the anchor, and the reference kind. Note any
   obvious clusters (e.g. all references concentrated in one subsystem).
4. **Save it.** The snippet best-effort-writes `Docs/RE/_dirty/queries/xref_map.<slug>.md`; confirm
   the path, or save the Markdown yourself with the Write tool. For typed-tool results, write a
   `Docs/RE/_dirty/queries/xref_map.<slug>.md` table by hand.
5. **Interpret, neutrally.** In your reply, describe the fan-in in words — how many functions
   reference the anchor, which look like the primary consumer, and the suggested next function to
   read closely (hand off to `ida-callgraph-map` or `ida-decompile-export`). Resolve `sub_…` names
   to proposed canonical names for `ida-naming-sync`; never rename here.

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/queries/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#** and never paste Hex-Rays pseudo-C into a reply or any file. This skill
  maps *which* functions reference a target; reading one closely is `ida-decompile-export`'s job.
- Addresses appear only inside `_dirty/`. In replies, prefer canonical names and counts; use a bare
  address only when no name exists.
- Never invent xrefs or addresses. If a call fails or returns partial data, report exactly what you
  got and stop.
