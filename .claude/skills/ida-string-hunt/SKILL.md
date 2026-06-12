---
name: ida-string-hunt
description: Use to census the string table of the legacy Martial Heroes client (Main.exe) and tag candidate subsystems by string + import evidence — the fastest way to locate networking, asset I/O, crypto, UI, scripting, and config code before reading any function. Drives the typed mcp__ida__find / mcp__ida__get_string / mcp__ida__imports tools (falling back to a bundled census IDAPython snippet) and writes a tagged string map to Docs/RE/_dirty/static/.
allowed-tools: Read Write
model: sonnet
---

# ida-string-hunt — string census + subsystem tagging

Strings are the cheapest, highest-signal map of an unknown binary: a `.pak` path string sits next to
the asset loader, a `recv`/`WSAStartup` import next to the network reader, a `.lua` extension next to
the scripting bridge. This skill enumerates every string (and the imports), groups them by keyword
buckets, and lists the function that references each interesting string — so you can jump straight to
the subsystem you need. It is the natural first move after `ida-recon`, and the input to
`ida-xref-map` / `ida-batch-analyze`.

All output is **dirty** (string contents + addresses derived directly from the copyrighted binary)
and lands under `Docs/RE/_dirty/static/`.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. Typed string/import tools
   (`mcp__ida__find`, `mcp__ida__get_string`, `mcp__ida__imports`, `mcp__ida__find_regex`) can serve
   targeted look-ups, but a **full** tagged census is easiest with the script-exec tool plus the
   bundled snippet. Pick the exec tool (`mcp__ida__py_exec_file` / `execute_script` / `run_python`).

## Steps

1. **Run the census.** Read `${CLAUDE_SKILL_DIR}/scripts/string_hunt.py`. Its CONFIG defines keyword
   **buckets** (network / asset_io / crypto / ui / scripting / config / debug) as keyword lists — add
   or tweak buckets for the question at hand. Run it via the exec tool. It enumerates ASCII + UTF-16
   strings, classifies each into buckets by substring match, records the referencing function for
   each matched string, and folds in the import table (so `recv`/`CreateFileA`/`ADVAPI32` land in
   their buckets too).
2. **Note the encoding.** Game text in this client is **CP949** (Korean). The snippet flags strings
   that are not clean ASCII so you do not misread mojibake; treat CP949 text as data, never transcribe
   long runs of it.
3. **Read the buckets.** The output is a per-bucket table: matched string (truncated), its data EA,
   and the function that references it. Skim for the obvious anchors — a format extension, an error
   message, an API name — that pin a subsystem's entry point.
4. **Save it.** The snippet best-effort-writes `Docs/RE/_dirty/static/strings.<label>.md`; confirm
   the path or save the Markdown yourself. Keep the full table in `_dirty/` only.
5. **Hand off.** In your reply, summarize which subsystems are clearly present and the single best
   anchor string/function per subsystem. Feed those anchors to `ida-xref-map` (fan-in to the
   consumer) or `ida-batch-analyze` (triage the cluster). Resolve `sub_…` referencing functions to
   proposed canonical names for `ida-naming-sync`; never rename here.

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/static/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#.** A string census is metadata (string text + address + referencing
  function), not function bodies. Do not paste pseudo-C anywhere.
- Treat CP949/Korean game text as opaque data: record short markers if useful, never transcribe long
  runs. String *contents* are dirty — they stay in `_dirty/`.
- Addresses appear only inside `_dirty/`. In replies, name subsystems and anchor functions, not raw
  addresses.
- Never invent strings or references. If a call fails or returns partial data, report exactly what
  you got and stop.
