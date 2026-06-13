---
name: ida-callgraph-map
description: Use to build a bounded call graph around a target function in the legacy Martial Heroes client (Main.exe) — its callers and callees out to a chosen depth — and render it as a neutral prose/edge map. Drives the typed mcp__ida__callgraph / mcp__ida__callees / mcp__ida__xrefs_to tools (falling back to a bundled IDAPython snippet) and writes the map to Docs/RE/_dirty/static/. The way to understand how a function sits inside a subsystem before reading it.
allowed-tools: Read Write
model: sonnet
effort: high
---

# ida-callgraph-map — bounded call graph around a target

Given one function, this skill walks the call graph **up** (who calls it, who calls them) and
**down** (what it calls, recursively) to a bounded depth, and renders the result as an edge list
plus a short prose description of each node's apparent role. It is how you situate a function within
its subsystem — distinguishing leaf helpers from orchestrators, finding the real entry point of a
feature, and choosing the next function worth reading closely.

All output is **dirty** (addresses derived directly from the copyrighted binary) and lands under
`Docs/RE/_dirty/static/`.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. Prefer the typed call-graph
   tools when present — names vary by build but look like `mcp__ida__callgraph`,
   `mcp__ida__callees`, `mcp__ida__func_query`, `mcp__ida__xrefs_to`. If none exist, use the
   script-exec tool with the bundled snippet.

## Steps

1. **Resolve the target.** Take a function name or `0x…` address. Resolve to a function start EA
   (typed `mcp__ida__func_query` / `entity_query`, or the snippet's resolver). Confirm the EA sits
   inside a real function, not library/CRT code.
2. **Walk the graph, bounded.**
   - **Typed path (preferred):** call `mcp__ida__callgraph` on the target with the desired
     `direction` (callers / callees / both) and `depth`. If only `mcp__ida__callees` +
     `mcp__ida__xrefs_to` exist, BFS by hand: callees via `callees`, callers via `xrefs_to` filtered
     to call references.
   - **Snippet path (fallback):** read `${CLAUDE_SKILL_DIR}/scripts/callgraph_map.py`, set CONFIG
     (`TARGET` = name or address; `DIRECTION` = `both` | `callers` | `callees`; `MAX_DEPTH`;
     `MAX_NODES` cap), and run it via the exec tool. It does a bounded BFS, dedups, caps node count
     so huge hubs don't explode, and prints an edge table plus a per-node summary (name, EA, in/out
     degree, library-vs-user flag).
3. **Keep it bounded.** Default `MAX_DEPTH` 2 and a `MAX_NODES` cap (e.g. 200). If the target is a
   hub (e.g. `malloc`, a logging helper), the graph will be dominated by noise — narrow the
   direction or lower the depth rather than dumping thousands of edges.
4. **Save it.** The snippet best-effort-writes `Docs/RE/_dirty/static/callgraph.<slug>.md`; confirm
   the path or save the Markdown yourself. Include both the edge table and the node summary.
5. **Interpret, neutrally.** In your reply, describe the shape in words: which node looks like the
   subsystem entry point, which are leaf helpers, where the graph crosses into another subsystem.
   Resolve `sub_…` names to proposed canonical names for `ida-naming-sync`; never rename here. Point
   the next analyst at the function worth decompiling (`ida-decompile-export`) or the data flow worth
   tracing (`ida-data-flow`).

## Decision points

- **If the target is a hub** (high out-degree, library-ish), drop `MAX_DEPTH` to 1 and pick one
  direction; a 200-node dump teaches nothing. **If it's a leaf**, widen callers (`DIRECTION=callers`)
  to find the feature's real entry point.
- **If the graph crosses into the CRT/library** (`malloc`, `memcpy`, RTL), prune that subtree — those
  edges are noise for subsystem mapping.
- **If which callee actually runs is data-dependent** (a virtual call, a dispatch through a pointer),
  the static graph shows all candidates but not the live one: hand off to `ida-debugger-drive` to
  breakpoint and read the real target in the maintainer's F9 session.

## Verify / Done when

- The walk is bounded (depth + node cap honored); the map has both an edge table and a per-node summary.
- A `callgraph.<slug>.md` exists under `_dirty/static/`; the reply names the apparent entry point + next read.
- No address leaked outside `_dirty/`.

## Pitfalls (never)

- Never emit an unbounded graph — always cap depth and nodes.
- Never paste node bodies; a call graph is edges + names, not pseudo-C.
- Never assume a virtual/indirect edge is the runtime one without a debugger confirmation.

*North star N1: situates a function in its subsystem so the next static read (then debugger confirm) targets the right node.*

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/static/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#** and never paste Hex-Rays pseudo-C into a reply or any file. A call graph
  is structure (edges + names), not function bodies. Reading a node closely is
  `ida-decompile-export`'s job.
- Addresses appear only inside `_dirty/`. In replies, prefer canonical names; use a bare address
  only when no name exists.
- Always bound the walk (depth + node cap). Never emit an unbounded graph. If a call fails or
  returns partial data, report exactly what you got and stop.
