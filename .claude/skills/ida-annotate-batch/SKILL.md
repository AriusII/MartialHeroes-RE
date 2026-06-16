---
name: ida-annotate-batch
description: Use when you want to apply a Campaign 2 annotation manifest to the live IDA database — batch-annotate a cluster: rename functions/globals + add neutral comments + (optionally) apply struct/enum types, all from a slice of Docs/RE/_dirty/campaign2/glossary.yaml. Triggers on "apply renames+comments to the IDB", "annotate a cluster in IDA for Campaign 2", "batch comment + rename + type". Dry-runs a per-entry verdict diff first, applies ONLY on explicit confirmation, and is idempotent (re-run → noop for already-applied entries).
allowed-tools: Read Write
model: sonnet
effort: medium
---

# ida-annotate-batch — atomically annotate one Campaign 2 cluster in the IDB

A clean **extension** of the `ida-rename-batch` / `ida-naming-sync` pattern: where those apply only
glossary *names*, this skill applies a per-cluster **annotation manifest** — a slice of the campaign
glossary `Docs/RE/_dirty/<campaign>/glossary.yaml` — that pairs each address with a **name**, a
**neutral comment**, and an optional **struct/enum type**. It is the mechanical engine behind the IDB
annotation phase (`Docs/PLAN.md` §4 Phase D / §6 glossary & sync-back): for one cluster, one atomic, re-runnable
IDAPython apply — **dry-run first, apply only on explicit user confirmation, idempotent**.

Renames and comments derived from a clean role-word glossary are firewall-safe (the same posture as
`ida-naming-sync`: writing neutral names into the IDB is sanctioned). The IDB is the only thing
mutated; the only repo file written is a **dirty applied-report** under `_dirty/campaign2/applied/`.
Comments are **neutral interop documentation** — never Hex-Rays pseudo-C, never mangled names.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp?ext=dbg` with the Martial Heroes IDB (`doida.exe` / `Main.exe`) open.
   If red, STOP and surface:
   `claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`. Never fabricate
   output from memory.
2. **Discover the exec tool name at runtime.** The exact `mcp__ida__*` names depend on the running
   build. List the available `mcp__ida__*` tools and pick the script-execution tool (commonly named
   like `mcp__ida__py_exec_file`, `mcp__ida__execute_script`, `mcp__ida__run_python`, or
   `mcp__ida__py_eval`). You hand it the bundled script's full source text.
3. **Phase C gate must have passed.** This skill annotates from the **reconciled** campaign glossary
   only. Read the cluster's slice of `Docs/RE/_dirty/campaign2/glossary.yaml` (the entries for the
   one cluster you are applying). Refuse to annotate from `*.proposed.*` manifests — only the
   gate-passed glossary.
4. **Read `Docs/RE/names.yaml`** to note its `binary.sha256` — the dry-run confirms the live IDB's
   SHA-256 matches this pinned build before any apply.

## Steps

1. **Pick the cluster.** Choose ONE cluster from the `PLAN.md` §5 cluster backlog (e.g.
   `network-dispatch`, `crypto-session`, `vfs-assetio`). Annotation runs **unbridled** — fan out as many
   annotators as the IDA MCP server sustains; there is **no one-annotator-at-a-time cap** (`Docs/PLAN.md`
   §3). Extract that cluster's slice from
   `Docs/RE/_dirty/<campaign>/glossary.yaml`: a map of `addresses → { name?, comment?, type? }`.
2. **Read the bundled script** `${CLAUDE_SKILL_DIR}/scripts/annotate_batch.py` (also reachable as
   `scripts/annotate_batch.py`). It is real, runnable IDAPython using `ida_name` / `ida_funcs` /
   `ida_bytes` / `ida_typeinf` and a tiny stdlib parser for the flat manifest shape (no PyYAML inside
   IDA). It reuses the MSVC/CRT runtime-skip patterns from `names_sync.py`.
3. **Inline the manifest + set dry-run.** In the script's `# === CONFIG ===` block, replace the
   `MANIFEST` block with the cluster slice (address keys stay quoted strings — never coerce
   `"0x004A1230"` to an int). Set `CLUSTER` to the cluster name and `MODE = "dry-run"`.
4. **Run via the discovered exec tool.** Paste the full filled-in source. The script emits exactly
   one line beginning `RESULT_JSON:` — per-entry verdicts and counts. In `dry-run` it computes, for
   every manifest entry: the current IDB name/comment at that address, the desired name/comment/type,
   and a verdict — `apply` (will annotate), `noop` (name + comment already match), `skip-runtime`
   (CRT/compiler symbol — never touched), `skip-missing` (no item at address), or `conflict` (desired
   name already used at another address). It also computes the binary SHA-256. Capture the JSON after
   the prefix.
5. **Present the dry-run diff.** Show counts per verdict (apply / noop / skip-runtime / skip-missing /
   conflict), the **apply list** (addr → name + comment + type), and any **conflicts**. Confirm the
   IDB SHA-256 matches `names.yaml`'s `binary.sha256` — warn loudly if it differs (the manifest may be
   pinned to a different build).
6. **Apply ONLY on explicit user go-ahead.** Re-send the same source with `MODE = "apply"`. It
   performs each `apply`-verdict entry: `set_name` (rename), function/repeatable comment, and — if a
   `type` was declared — applies the struct/enum type. It re-skips runtime symbols and conflicts and
   returns the same JSON shape with `applied` counts and any per-entry failures. Idempotent: a second
   run reports `noop` for everything already applied. *Decision: annotators run **in parallel** — there is
   no one-writer cap (`Docs/PLAN.md` §3); if a rename/comment/type call fails or conflicts, **retry it**
   rather than serializing. If a `type` fails to
   apply (struct not yet imported into the IDB), apply name+comment and report the type failure rather
   than aborting the whole cluster. On any SHA-256 mismatch vs `names.yaml`, STOP before applying.*
7. **Stage the applied report.** Write the apply result to
   `Docs/RE/_dirty/campaign2/applied/<cluster>.md` — an address → {name, comment, type} table with
   the per-entry verdict/result, the IDB SHA-256, a `> DIRTY — never commit` banner, and a generated
   timestamp. This is the ONLY repo file the skill writes.
8. **Report.** Per-verdict counts, applied/failed counts, the SHA-256, and the applied-report path.
   Note that the sync-back of these names into `Docs/RE/names.yaml` is Phase E (Tier-1, via
   `ida-naming-sync`'s pull path) — this skill does **not** edit `names.yaml`.

## Verify / Done when

- The manifest came from the gate-passed `glossary.yaml` slice (never a `*.proposed.*`); dry-run ran;
  user confirmed; IDB SHA-256 matched the pin.
- Exactly one cluster was applied this invocation (other annotators may run in parallel against the same
  IDB); every name/comment/type traces to a manifest entry; zero CRT/runtime symbols touched; conflicts
  skipped (not forced).
- A re-run reports `noop` for the cluster (idempotent); the applied-report exists at
  `Docs/RE/_dirty/campaign2/applied/<cluster>.md` with banner + SHA-256; `names.yaml`/`journal.md`
  untouched.

## Pitfalls

- **Never** annotate from a `*.proposed.*` manifest or invent a name/comment/type — only the
  reconciled, gate-passed glossary slice.
- **Two annotators may write the same IDB in parallel** — that is now allowed; the safety model is the
  gate-passed glossary slice + dry-run + idempotency, **not** serialization. Retry a failed/conflicting
  call rather than throttling back.
- **Never** write a comment that is pseudo-C, a `sub_`/`_DWORD`/`__thiscall` token, or "paste into C#"
  — comments are neutral interop documentation only.
- Do not edit the `glossary.yaml` merge point, `names.yaml`, or `journal.md` — the only file you write
  is the dirty applied-report.

> **N1:** batch annotation makes the legacy IDB legible for clean-room comprehension — neutral names
> and interop comments only, so the binary becomes navigable without anything tainted crossing.

## Hard rules

- **Apply only names/comments/types from the manifest.** Never invent a name, a comment, or a type —
  every annotation comes from the gate-passed `glossary.yaml` slice you were given.
- **Always dry-run before apply. Never apply without the user's explicit confirmation.** The first
  run is always `MODE = "dry-run"`.
- **Idempotent.** Re-running over an already-annotated cluster must produce `noop` for matching
  entries and never duplicate a comment or re-clobber a name.
- **Never rename compiler-runtime symbols.** The script skips MSVC/CRT patterns (`__`, `_imp_`, `j_`,
  `?`/`??` C++ mangling, `_RTC_`, `__security_*`, `mainCRTStartup`, `std::`, `_acmdln`,
  `_CxxThrow…`, FLIRT/library-flagged functions). Do not override.
- **Comments stay neutral.** IDB comments are interop documentation ("parses the 24-byte VFS index
  header") — **never** Hex-Rays pseudo-C, `sub_xxxx`/`loc_xxxx`/`_DWORD`/`__thiscall`, mangled names,
  or "paste this into C#".
- **The only repo file this skill writes** is the dirty applied-report under
  `Docs/RE/_dirty/campaign2/applied/`. Never write a committed RE spec, `Docs/RE/names.yaml`,
  `Docs/RE/journal.md`, the `glossary.yaml` merge point, or any `0X.*` source folder / `.cs` /
  `.csproj` / `.slnx`.
- **Address keys stay quoted strings end-to-end** — never coerce `"0x004A1230"` to an int when
  echoing them back into the report.
- **On any per-entry failure, record it in the JSON and continue.** Report all failures to the user;
  never silently drop one.
