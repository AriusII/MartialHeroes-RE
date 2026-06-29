---
name: ida-recon
description: Use when starting analysis of the legacy Martial Heroes client (doida.exe; Main.exe historical) and you need a baseline census — segments, imports, exports, entry points, strings, and named globals — before drilling into any single function. Leads with mcp__ida__survey_binary + the domain_* census family; tags subsystems via recipe_import_usage/recipe_string_to_code. Use first, before reading any function. Produces a pinned SHA-256 and a navigable map of the binary. Includes a STRING-HUNT mode that censuses the full string + import table and tags candidate subsystems (networking, asset I/O, crypto, UI, scripting, config) by string + import evidence — the fastest way to locate a subsystem's entry point before reading any function.
allowed-tools: mcp__ida__*, Read, Write
model: sonnet
effort: high
---

# ida-recon — baseline census of the legacy client

Produces the first map of the legacy 32-bit client (`doida.exe`; `Main.exe` is the historical
reference): segment table, import/export tables, entry points, the full string table, and named
(non-default) globals. The dump is the starting point every later RE session navigates from, and it
**pins the binary's SHA-256** so all downstream specs cite one exact build. Two modes:

- **Mode A — baseline census** (segments / imports / exports / entrypoints / strings / globals + SHA-256 pin).
- **Mode B — string census & subsystem tagging** (every string + import, bucketed by keyword into
  candidate subsystems, each tagged with its referencing function) — the fastest way to locate a
  subsystem's entry point.

All output is **dirty** (derived directly from the binary) and lands under `Docs/RE/_dirty/recon/`
(Mode A) / `Docs/RE/_dirty/static/` (Mode B). Nothing here is committed. Only a separately authored
neutral spec may cross the firewall into `Docs/RE/specs|formats|structs|opcodes.md`.

## Ground truth

IDA on the binary (`doida.exe`; `Main.exe` is the historical reference) is the **single absolute
truth** for the original — its segments, names, and layout are read FROM the DB, never from memory,
analogy, or a prior session's recollection. A blank/short/wrong census means the DB is wrong, not that
you should fill the gaps from guesswork: STOP and report.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run the `/ida-mcp-connect` skill and confirm it reports a live IDA Pro
   9.3 MCP server at `http://127.0.0.1:13337/mcp` with the target IDB open. If it is red, STOP and
   surface the hint: `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`. Do not
   fabricate a census from memory.
2. **Lead with `survey_binary`.** Call `mcp__ida__survey_binary` first — it is the one-shot baseline
   (segments / imports / entry points / counts / SHA) and the lead tool of any new investigation. Fill
   the gaps with the typed `domain_*` census family: `mcp__ida__domain_segments`,
   `mcp__ida__domain_imports`, `mcp__ida__domain_entry_points`, `mcp__ida__domain_strings`,
   `mcp__ida__list_globals`, `mcp__ida__list_funcs`, and `mcp__ida__func_profile` for per-function
   role summaries.
3. **Script-exec for the JSON dump.** When you want the bundled snippet's exact JSON shape, run it via
   `mcp__ida__py_exec_file` (multi-statement) / `mcp__ida__py_eval` (one-liner), persisting reusable
   probes with `mcp__ida__save_script` / `read_script` / `list_scripts`. Each probe emits exactly one
   `RESULT_JSON <json>` line per the harness contract — see the `ida-python-lib` skill. If no
   script-exec tool exists, assemble the same JSON shape by hand from the `domain_*` tools above.

## Mode A — baseline census (steps)

1. Read the bundled IDAPython snippet `${CLAUDE_SKILL_DIR}/scripts/recon_dump.py` (also reachable as
   `scripts/recon_dump.py` relative to this skill). It is real, runnable IDAPython using
   `idautils` / `idaapi` / `ida_*`.
2. Feed the snippet's **full source text** to `mcp__ida__py_exec_file`. The snippet writes nothing to
   disk by itself inside IDA's restricted context — instead it builds one JSON document and prints it
   to stdout as a single line prefixed with `RESULT_JSON ` (the harness contract — see `ida-python-lib`).
   Capture that line from the tool's return value.
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

## Mode B — string census & subsystem tagging

Strings are the cheapest, highest-signal map of an unknown binary: a `.pak`/`.vfs` path string sits
next to the asset loader, a `recv`/`WSAStartup` import next to the network reader, a `.lua` extension
next to the scripting bridge. This mode enumerates every string (and the imports), groups them by
keyword buckets, and lists the function that references each interesting string — so you jump straight
to the subsystem you need. It is the natural second move after Mode A, and the input to `ida-explore`.

1. **Lead with the recipes.** Before the snippet, run the purpose-built recipes:
   `mcp__ida__refresh_strings_cache` to warm the cache, then `mcp__ida__recipe_import_usage` (which
   imports anchor which functions) and `mcp__ida__recipe_string_to_code` (which strings tie to which
   code) to tag subsystems by import + string evidence in one shot. Supplement freeform queries with
   `mcp__ida__search_text` / `mcp__ida__find_regex` over the string/symbol space.
2. **Run the census snippet for the full table.** Read `${CLAUDE_SKILL_DIR}/scripts/string_hunt.py`
   (its classifier helper `${CLAUDE_SKILL_DIR}/scripts/lib_classify.py` ships alongside). Its CONFIG
   defines keyword **buckets** (network / asset_io / crypto / ui / scripting / config / debug) as
   keyword lists — add or tweak buckets for the question at hand. Run it via `mcp__ida__py_exec_file`.
   It enumerates ASCII + UTF-16 strings, classifies each into buckets by substring match, records the
   referencing function per matched string, and folds in the import table (so
   `recv`/`CreateFileA`/`ADVAPI32` land in their buckets too).
3. **Note the encoding.** Game text in this client is **CP949** (Korean). The snippet flags strings
   that are not clean ASCII so you do not misread mojibake; treat CP949 text as opaque data, never
   transcribe long runs of it.
4. **Read the buckets.** The output is a per-bucket table: matched string (truncated), its data EA,
   and the referencing function. Skim for the obvious anchors — a format extension, an error message,
   an API name — that pin a subsystem's entry point. *Decision: if a bucket is empty but the subsystem
   must exist (e.g. crypto with no keyword hits), pivot to import evidence (`WSAStartup`,
   `CryptAcquireContext`) or run `ida-py`'s `snippets/find_bitops_loops.py` — the cipher is often
   constant-driven with no strings. If a string has many referencers, it is a shared log/format string,
   not a subsystem anchor — prefer single-referencer strings as entry points.*
5. **Save it.** The snippet best-effort-writes `Docs/RE/_dirty/static/strings.<label>.md`; confirm the
   path or save the Markdown yourself. Keep the full table in `_dirty/` only, with the SHA-256 tag and
   `> DIRTY` banner.
6. **Hand off.** Summarize which subsystems are clearly present and the single best anchor
   string/function per subsystem, then feed those anchors to `ida-explore` (xref fan-in / cluster
   triage) or `ida-opcode-map` / `ida-crypto-hunt`. Resolve `sub_…` referencing functions to proposed
   canonical names for `ida-annotate`'s names-sync mode; never rename here.

## Decision points

- **If the SHA-256 differs from a prior recon** (a re-export, or you're on `doida.exe` not `Main.exe`),
  do NOT overwrite — write a new `recon-<sha8>.*` pair; every downstream spec cites one exact build.
- **If `strings` is suspiciously short** (a few dozen on a real MMORPG client), auto-analysis is
  unfinished or you're on the wrong/empty DB — STOP and tell the maintainer to let analysis settle.
- **If you spot recv/cipher/opcode-shaped markers** in the strings/imports (`recv`, `WSARecv`, format
  keys), don't chase them by reading bodies — run Mode B to tag the subsystem, then hand the anchors to
  `ida-explore` → `ida-opcode-map` / `ida-crypto-hunt`. Recon only maps; it never reads bodies.
- **If a Mode B substring bucket is ambiguous** (`key` matches both crypto and UI-key strings), verify
  the referencing function before declaring a subsystem present — a bucket is a hypothesis, not a finding.

## Verify / Done when

- `binary.sha256` is 64 hex chars; both `recon-<sha8>.json` and `.md` exist under `_dirty/recon/`
  with the `> DIRTY` banner + full SHA-256.
- Counts are non-trivial (dozens of segments/imports, hundreds+ of strings) — a real client, not a stub.
- No address, string, or symbol leaked outside `_dirty/`.

## Pitfalls (never)

- Never fabricate a census from memory when the MCP is red — STOP and surface the connect hint.
- Never decompile or paste pseudo-C — that is `ida-explore` (DECOMPILE-ONE mode). Recon is metadata only.
- Never reuse a stale `recon-<sha8>` filename for a different binary build.

*North star N1: recon is the first, build-pinned map every later static-hypothesis (then debugger-confirm) pass navigates from.*

## Hard rules

- **Do not** decompile or paste pseudo-C here — that is the job of `ida-explore` (DECOMPILE-ONE mode).
  This skill only enumerates metadata (names, addresses, strings, segment ranges).
- Output paths must always be inside `Docs/RE/_dirty/recon/` (Mode A) or `Docs/RE/_dirty/static/`
  (Mode B). Never write under `Docs/RE/specs|packets|formats|structs|opcodes.md|names.yaml` from this skill.
- Treat CP949/Korean game text as opaque data (Mode B): record short markers if useful, never
  transcribe long runs. String *contents* are dirty — they stay in `_dirty/`.
- Never invent addresses, string contents, or SHA-256 digits. If the MCP call fails or returns
  partial data, report exactly what you got and stop.
- The raw census is contaminated. Promotion to a committed spec is a separate, deliberate act
  performed by a spec-author who *rewrites* in neutral prose, never copies.
