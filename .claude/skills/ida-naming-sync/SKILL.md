---
name: ida-naming-sync
description: Use when you want the canonical names in Docs/RE/names.yaml applied to the live IDA database (so functions/globals read by their project names), and to pull the IDB's current analyst-renamed symbols back into the glossary. Dry-runs a diff before writing, and never touches compiler-runtime symbols.
allowed-tools: Read Write
model: sonnet
effort: medium
---

# ida-naming-sync — sync Docs/RE/names.yaml ⇄ the IDA database

Keeps the project glossary `Docs/RE/names.yaml` and the live IDA Pro 9.3 database in agreement, in
both directions:

- **Apply (yaml → IDB):** rename functions and globals in the IDB to the canonical names recorded
  in `names.yaml` (keyed by address).
- **Pull (IDB → yaml):** re-export the IDB's current analyst-renamed symbols so the maintainer can
  fold new names back into `names.yaml`.

`names.yaml` is the project's clean, committed glossary (a name map, NOT pseudo-code), so applying
it to the IDB and reading symbol names back is firewall-safe. **Address keys are strings** (e.g.
`"0x004A1230"`) so YAML never coerces them. The skill always shows a **dry-run diff first** and
never renames compiler-runtime symbols.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the exec tool name at runtime.** `mcp__ida__*` names vary by build. List them and
   pick the script-execution tool (e.g. `mcp__ida__execute_script` / `mcp__ida__run_python` /
   `mcp__ida__eval`). Fall back to a typed `mcp__ida__rename` tool only if no exec tool exists.
3. **Read `Docs/RE/names.yaml`** to confirm it exists and note its current `binary.sha256`. If
   that field is empty, run `/ida-recon` first to pin the build, or proceed and let this skill
   report the IDB's SHA-256 so a maintainer can fill it.

## Steps

1. Read the bundled snippet `${CLAUDE_SKILL_DIR}/scripts/names_sync.py` (also
   `scripts/names_sync.py`). It is real, runnable IDAPython using `ida_name` / `idautils` and a
   minimal stdlib YAML reader (no pip — the snippet parses the simple, flat names.yaml shape
   itself; it does not depend on PyYAML being present inside IDA).
2. **Provide the glossary to the snippet.** Read `Docs/RE/names.yaml` with the Read tool and inline
   its `functions:` and `globals:` maps into the snippet by replacing the `NAMES_YAML = r"""..."""`
   block at the top with the file's exact text. (Inlining avoids assuming IDA can reach the repo
   path.) Set `MODE = "dry-run"` for the first pass.
3. Feed the edited source to the discovered MCP exec tool. In `dry-run` the snippet computes, for
   every `functions`/`globals` entry: current IDB name at that address, the desired name, and a
   verdict — `apply` (will rename), `noop` (already matches), `skip-runtime` (compiler/runtime
   symbol, never touched), `skip-missing` (no item at address), or `conflict` (name already used
   elsewhere). It also lists IDB symbols that are renamed-but-absent-from-yaml (pull candidates).
   It computes the binary SHA-256 and prints one JSON line prefixed `NAMES_JSON:`. Capture it.
4. **Present the dry-run diff** to the user: counts per verdict, the apply list, conflicts, and the
   pull candidates. Confirm the IDB SHA-256 matches `names.yaml`'s `binary.sha256` (warn loudly if
   it differs — the glossary may be pinned to a different build).
5. **Apply only on explicit go-ahead.** Re-send the snippet with `MODE = "apply"`. It performs the
   `apply`-verdict renames (functions + globals), re-skips runtime symbols and conflicts, and
   returns the same JSON shape with `applied` counts and any failures. *Decision: IDB writes run **in
   parallel** now — a concurrent `ida-rename-batch`/`ida-annotate-batch` run is fine; just apply and
   **retry** any failed/conflicting call. If the SHA-256 differs from `names.yaml`'s pin, do NOT apply (the glossary targets a different
   build) — report and stop. Treat `conflict` verdicts as maintainer-reconcile, never force.*
6. **Pull back.** From the `pull_candidates` in the JSON, write the new/changed names into a dirty
   staging file `Docs/RE/_dirty/names-pulled-<sha8>.yaml` (function/global address → current name).
   Do NOT edit `Docs/RE/names.yaml` automatically — leave a clear note telling the maintainer to
   review the staged pulls and merge the wanted ones into the committed glossary by hand (and add a
   `journal.md` entry).
7. Report: per-verdict counts, applied counts (if any), the SHA-256, and the staged-pull path.

## Verify / Done when

- A dry-run diff was shown before any apply; the user confirmed; the IDB SHA-256 matched the glossary
  pin (or the mismatch was surfaced and apply withheld).
- Apply (yaml→IDB) touched only `apply`-verdict entries and zero CRT/runtime symbols; conflicts were
  skipped. Pull (IDB→yaml) candidates were staged to `Docs/RE/_dirty/names-pulled-<sha8>.yaml`, **not**
  merged into `names.yaml` automatically.
- The committed glossary and `journal.md` were left untouched by the skill; the maintainer hand-off
  note is present.

## Pitfalls

- **Never** auto-edit `Docs/RE/names.yaml` or `journal.md` — pulls are staged for human review only.
- **Never** run concurrently with another IDB writer — single-writer is the rule.
- Do not coerce address keys to ints when writing YAML back, and never override the CRT/FLIRT skip.
- Do not apply across a SHA-256 mismatch — names would land on the wrong addresses.

> **N1:** bidirectional name sync is the legibility layer of clean-room RE — the IDB reads in project
> names and analyst discoveries flow back to the glossary, with no pseudo-C ever crossing.

## Hard rules

- **Never rename compiler-runtime symbols.** The snippet skips names matching MSVC/CRT patterns
  (`__`, `_imp_`, `j_`, `??`/`?` C++ mangling, `_RTC_`, `_CxxThrowException`, `__security_*`,
  `mainCRTStartup`, `_acmdln`, `std::`, names inside library/FLIRT-flagged functions). Do not
  override this.
- Always dry-run before apply. Never apply without the user's explicit confirmation.
- This skill is allowed to WRITE to the IDB (renames) — that is intentional and firewall-safe
  because the glossary is clean. It must NOT write decompiler output anywhere, and the only file it
  writes in the repo is the dirty staging file under `Docs/RE/_dirty/`.
- Never invent addresses or names. On any per-entry failure, record it in the JSON and continue;
  report all failures to the user.
- Address keys stay quoted strings end-to-end; never coerce `"0x004A1230"` to an int when writing
  YAML back.
