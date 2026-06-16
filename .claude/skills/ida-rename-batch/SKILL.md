---
name: ida-rename-batch
description: Use to clear the sub_xxxx / loc_xxxx autoname noise in the legacy Martial Heroes IDB — proposes canonical names for unnamed functions from their behavior, but APPLIES (via mcp__ida__rename) ONLY names already present in Docs/RE/names.yaml. Everything else is emitted as a proposal for ida-naming-sync / a maintainer to add to names.yaml. Always dry-runs first; never invents a name straight into the database.
allowed-tools: Read Write
model: sonnet
effort: medium
---

# ida-rename-batch — propose names, apply only the glossary-approved ones

Reading a binary full of `sub_004A1230` is slow. This skill speeds it up by proposing meaningful
names for autonamed functions/globals from their behavior and evidence — but it keeps the firewall's
naming discipline strict: it **only writes a name into the IDB when that exact address→name mapping
already exists in `Docs/RE/names.yaml`** (the clean, committed glossary). Every other proposal is
written to a `_dirty/` staging file for a maintainer (via `ida-naming-sync`) to vet and add to the
glossary. The glossary stays the single source of truth; the IDB is just kept in sync with it.

`names.yaml` is clean (a name map, not pseudo-code), so applying it to the IDB is firewall-safe.
Proposing a *new* name is fine; *writing a new name straight into the database* is not — that path
must go through the glossary.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. You need the script-exec tool
   (`mcp__ida__py_exec_file` / `execute_script` / `run_python`) for the dry-run report, and the typed
   `mcp__ida__rename` tool to apply glossary names.
3. **Read `Docs/RE/names.yaml`.** Note its `functions:` / `globals:` maps (keyed by `"0x<addr>"`
   strings) and its `binary.sha256`. The applied names come **only** from these maps.

## Steps

1. **Gather rename candidates.** Decide the scope: a specific function set you just analyzed (from
   `ida-callgraph-map` / `ida-batch-analyze`), or all autonamed user functions in a range. For each,
   form a proposed canonical name from its observed behavior + evidence (e.g. a `.pak`-string-driven
   loader → `PakOpen`; a recv-adjacent bit loop → `WireDecrypt`). Names are CamelCase, role-based,
   no addresses.
2. **Dry-run (always first).** Read `${CLAUDE_SKILL_DIR}/scripts/rename_batch.py`. Inline the
   `functions:`/`globals:` maps from `names.yaml` into its `NAMES_YAML` block (so IDA need not reach
   the repo path), set `MODE = "dry-run"`, and run it via the exec tool. For every glossary entry it
   reports the current IDB name at that address, the desired name, and a verdict: `apply` (will
   rename), `noop` (already matches), `skip-runtime` (CRT/compiler symbol — never touched),
   `skip-missing` (no item at address), or `conflict` (name already used elsewhere). It also computes
   the binary SHA-256.
3. **Present the dry-run.** Show counts per verdict, the apply list, and any conflicts. Confirm the
   IDB SHA-256 matches `names.yaml`'s `binary.sha256` (warn loudly if not — wrong build).
4. **Apply ONLY glossary names, on explicit go-ahead.** Either re-run the snippet with
   `MODE = "apply"`, or apply each `apply`-verdict entry via the typed `mcp__ida__rename` tool. Skip
   runtime symbols and conflicts. Never rename an address whose target name is not in the glossary.
   *Decision: IDB writes now run **in parallel** — no need to confirm exclusivity; just apply, and
   **retry** any call that fails or conflicts. If the dry-run shows a `conflict`, do NOT force it
   — the desired name is already bound elsewhere; stage it as a proposal and let the maintainer
   reconcile. If the SHA-256 mismatches `names.yaml`, STOP — you are about to name the wrong build.*
5. **Stage the proposals.** Write every *new* proposed name (the ones NOT in `names.yaml`) to
   `Docs/RE/_dirty/names-proposed-<sha8>.md` as an address→proposed-name table with a one-line
   rationale each. Tell the maintainer to vet these via `ida-naming-sync` and add the good ones to
   `names.yaml` (and journal it). Do **not** edit `names.yaml` yourself.
6. **Report.** Per-verdict counts, applied count, the SHA-256, and the staged-proposals path.

## Verify / Done when

- A dry-run ran before any apply; the user gave explicit go-ahead; the IDB SHA-256 matched
  `names.yaml`'s `binary.sha256`.
- Every applied name was already in `names.yaml`; every *new* name went to the `_dirty/` proposals
  file, not the IDB; zero CRT/runtime symbols were touched; conflicts were skipped, not forced.
- A re-run reports `noop` for the just-applied entries (idempotent), and the proposals path is reported
  for `ida-naming-sync` follow-up.

## Pitfalls

- **Never** write a new name straight into the IDB — that path bypasses the glossary firewall; new
  names are *proposals* under `_dirty/` only.
- **Concurrent writers against the IDB are now allowed** — fan out freely; the safety model is the
  glossary firewall + dry-run + idempotency, not serialization. Retry a failed/conflicting call rather
  than throttling back.
- Do not coerce `"0x004A1230"` to an int, and do not override the CRT/FLIRT skip — those symbols are
  off-limits.
- Do not apply against a mismatched SHA-256 build — you would scatter names onto wrong addresses.

> **N1:** keeping the IDB in sync with the clean glossary makes the binary legible for clean-room RE
> without ever letting a non-glossary name (or any pseudo-C) cross the firewall.

## Hard rules

- **Apply only names already in `Docs/RE/names.yaml`.** New names are *proposals* → staged under
  `_dirty/` for the glossary, never written straight into the IDB.
- **Always dry-run before apply.** Never apply without explicit user confirmation.
- **Never rename compiler-runtime symbols** (`__`, `_imp_`, `j_`, `?`/`??` C++ mangling, `_RTC_`,
  `__security_*`, `mainCRTStartup`, `std::`, FLIRT-flagged library functions). The snippet skips
  these; do not override.
- The only repo file this skill writes is the `_dirty/` staging file. Never write to a committed
  spec, `names.yaml`, `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- Address keys stay quoted strings end-to-end; never coerce `"0x004A1230"` to an int.
- Never invent an address. On any per-entry failure, record it and continue; report all failures.
