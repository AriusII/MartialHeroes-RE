---
name: ida-annotate
description: Use to WRITE legibility annotations back into the live Martial Heroes IDA database (doida.exe / Main.exe) — the one IDB-write applier, four modes. NAMES-SYNC syncs Docs/RE/names.yaml ⇄ the IDB (apply canonical names, pull analyst renames back). RENAME-BATCH proposes canonical names for sub_xxxx/loc_xxxx autoname noise but applies ONLY glossary-approved names. ANNOTATE-MANIFEST applies a campaign glossary slice (rename + neutral comment + optional struct/enum type) per cluster. STRUCT-APPLY recovers a C++ object layout from this+offset access patterns and declares the struct type onto the IDB. Always dry-run → apply only on explicit confirmation, idempotent, SHA-256-pinned, never touches CRT/runtime symbols, never writes pseudo-C. Drives mcp__ida__rename / declare_type / type_apply + bundled IDAPython; the only repo file written is a dirty applied-report.
allowed-tools: mcp__ida__* Read Write
model: sonnet
effort: high
---

# ida-annotate — apply name / comment / type / struct annotations to the IDB

One skill, four IDB-write modes. They share **one discipline** and differ only in *what* they write.
This is the legibility layer of clean-room RE: the binary becomes navigable in project names + neutral
interop comments + recovered layouts, while **nothing tainted ever crosses the firewall**.

| Mode | What it writes to the IDB | Source of truth | Bundled script | Dirty report |
|---|---|---|---|---|
| **NAMES-SYNC** | function/global renames, both directions | `Docs/RE/names.yaml` | `names_sync.py` | `_dirty/names-pulled-<sha8>.yaml` |
| **RENAME-BATCH** | glossary-approved renames only | `names.yaml` (+ proposals out) | `rename_batch.py` | `_dirty/names-proposed-<sha8>.md` |
| **ANNOTATE-MANIFEST** | rename + neutral comment + type, per cluster | gate-passed `_dirty/<campaign>/glossary.yaml` slice | `annotate_batch.py` (+ `campaign8_phase_d_apply.py`) | `_dirty/campaign2/applied/<cluster>.md` |
| **STRUCT-APPLY** | a declared struct/enum type onto `this` | observed `this+offset` access patterns | `struct_probe.py` | `_dirty/structs/<struct>.offsets.md` |

## The shared discipline (every mode obeys verbatim)

- **Firewall-safe by construction.** Renames + neutral comments + declared *layouts* are sanctioned
  IDB writes (a name map / an interop sentence / a struct shape is not pseudo-code). A *transcribed
  decompilation* is never written anywhere. Keep that distinction sharp.
- **Truth, not tidiness.** Every name/comment/type/offset must **reflect what the binary proves** —
  from the gate-passed glossary, the clean `names.yaml`, or an *observed* access pattern. Never invent
  a name/comment/offset to look neat; an unsure item stays a proposal for the maintainer to vet.
- **Always dry-run → apply only on explicit confirmation.** The first run is always `MODE="dry-run"`,
  producing a per-entry verdict diff (`apply` / `noop` / `skip-runtime` / `skip-missing` / `conflict`).
  Apply only after the user says go.
- **Idempotent.** A re-run reports `noop` for already-applied entries — never duplicates a comment or
  re-clobbers a name.
- **SHA-256 pinned.** The dry-run computes the live IDB's SHA-256 and confirms it matches
  `names.yaml`'s `binary.sha256`. **On mismatch, STOP before applying** — you would scatter
  names/comments onto the wrong build.
- **Never rename compiler-runtime symbols.** The scripts skip MSVC/CRT patterns (`__`, `_imp_`, `j_`,
  `?`/`??` C++ mangling, `_RTC_`, `__security_*`, `mainCRTStartup`, `std::`, `_acmdln`,
  `_CxxThrow…`, FLIRT/library-flagged). Never override.
- **Comments stay neutral.** Interop documentation only ("parses the 24-byte VFS index header") —
  **never** Hex-Rays pseudo-C, `sub_`/`loc_`/`_DWORD`/`__thiscall`, mangled names, or "paste into C#".
- **Unbridled writes.** IDB writes run **massively parallel** — there is **no one-writer-at-a-time
  cap** (`Docs/PLAN.md` §3). The safety model is the gate-passed source + dry-run + SHA-256 pin +
  idempotency, **not** serialization. If a call fails or conflicts, **retry it** rather than throttling.
- **Address keys stay quoted strings end-to-end** — never coerce `"0x004A1230"` to an int.
- **The only repo file any mode writes** is its dirty report (column above). Never write a committed
  RE spec, `Docs/RE/names.yaml`, `Docs/RE/journal.md`, the `glossary.yaml` merge point, or any `0X.*`
  source folder / `.cs` / `.csproj` / `.slnx`.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp?ext=dbg` with the Martial Heroes IDB (`doida.exe` / `Main.exe`) open.
   If red, STOP and surface: `claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`.
   Never fabricate output from memory.
2. **Discover the tool names at runtime.** `mcp__ida__*` names vary by build. List them and pick the
   script-execution tool (commonly `mcp__ida__py_exec_file` / `execute_script` / `run_python` /
   `py_eval`) for the dry-run/apply snippets, and the typed `mcp__ida__rename` / `declare_type` /
   `type_apply_batch` / `set_type` tools where a mode prefers them.
3. **Read the mode's source of truth** (column in the table) and note its `binary.sha256` for the pin.

## Mode A — NAMES-SYNC (`names.yaml` ⇄ IDB, bidirectional)

Keep the glossary and the IDB in agreement both directions: **apply** (yaml→IDB renames) and **pull**
(IDB→yaml, re-export analyst renames for the maintainer to merge).

1. Read `${CLAUDE_SKILL_DIR}/scripts/names_sync.py` (real IDAPython using `ida_name`/`idautils` + a
   minimal stdlib YAML reader — no PyYAML inside IDA). Inline `Docs/RE/names.yaml`'s `functions:` /
   `globals:` maps into the snippet's `NAMES_YAML = r"""…"""` block (inlining avoids assuming IDA can
   reach the repo path). Set `MODE="dry-run"`.
2. Run via the exec tool. It prints one `NAMES_JSON:` line: per-entry verdicts, the SHA-256, and the
   **pull candidates** (IDB symbols renamed-but-absent-from-yaml). Present counts + apply list +
   conflicts + pull candidates; confirm the SHA-256 pin.
3. **Apply** on go-ahead (`MODE="apply"`, or typed `mcp__ida__rename` per `apply` entry). **Pull** by
   writing the `pull_candidates` to `Docs/RE/_dirty/names-pulled-<sha8>.yaml` for the maintainer to
   merge by hand — **never** auto-edit `names.yaml` or `journal.md`.

## Mode B — RENAME-BATCH (clear autoname noise, apply only glossary names)

Speed up reading a binary full of `sub_004A1230` by proposing meaningful names from behavior — but
**only write a name into the IDB when that exact address→name mapping already exists in `names.yaml`**.

1. Gather rename candidates (a function set from `ida-explore`'s callgraph/batch modes, or autonamed
   functions in a range). Form each proposed name from observed behavior + evidence (a
   `.pak`-string-driven loader → `PakOpen`; a recv-adjacent bit loop → `WireDecrypt`). CamelCase,
   role-based, no addresses.
2. Read `${CLAUDE_SKILL_DIR}/scripts/rename_batch.py`, inline the `names.yaml` maps into `NAMES_YAML`,
   set `MODE="dry-run"`, run. Present per-verdict counts + apply list + conflicts; confirm the pin.
3. **Apply** only `apply`-verdict glossary names on go-ahead (snippet `MODE="apply"` or typed
   `mcp__ida__rename`). **Stage every *new* proposed name** (the ones NOT in `names.yaml`) to
   `Docs/RE/_dirty/names-proposed-<sha8>.md` (address → proposed-name + one-line rationale) for the
   maintainer to vet via Mode A and add to the glossary. Never write a new name straight into the IDB
   — that bypasses the glossary firewall.

## Mode C — ANNOTATE-MANIFEST (per-cluster rename + comment + type)

The mechanical engine behind the IDB annotation phase (`Docs/PLAN.md` §4 Phase D). Applies a
per-cluster **annotation manifest** — a slice of the reconciled campaign glossary pairing each address
with a **name**, a **neutral comment**, and an optional **struct/enum type**.

1. **Phase C gate must have passed.** Annotate only from the **reconciled**
   `Docs/RE/_dirty/<campaign>/glossary.yaml` — refuse `*.proposed.*` manifests. Pick ONE cluster
   (`network-dispatch`, `crypto-session`, `vfs-assetio`, …) and extract its slice: a map of
   `addresses → { name?, comment?, type? }`.
2. Read `${CLAUDE_SKILL_DIR}/scripts/annotate_batch.py` (real IDAPython using
   `ida_name`/`ida_funcs`/`ida_bytes`/`ida_typeinf` + a stdlib parser for the flat manifest shape;
   reuses the runtime-skip patterns from `names_sync.py`). Inline the cluster slice into the `MANIFEST`
   block (address keys stay quoted), set `CLUSTER` + `MODE="dry-run"`, run. For a large multi-cluster
   Phase-D apply, the bundled `${CLAUDE_SKILL_DIR}/scripts/campaign8_phase_d_apply.py` is the bulk
   applier variant.
3. Present the dry-run diff (per-verdict counts, the apply list of addr → name + comment + type, any
   conflicts); confirm the SHA-256 pin.
4. **Apply** on go-ahead (`MODE="apply"`): `set_name` + function/repeatable comment + (if declared) the
   struct/enum type per `apply` entry. If a `type` fails (struct not yet imported), apply name+comment
   and report the type failure rather than aborting the cluster. Stage the result to
   `Docs/RE/_dirty/campaign2/applied/<cluster>.md` (addr → {name, comment, type} + per-entry verdict,
   the SHA-256, a `> DIRTY — never commit` banner, a timestamp). The names-sync-back into `names.yaml`
   is Phase E (Mode A's pull path) — this mode does **not** edit `names.yaml`.

## Mode D — STRUCT-APPLY (recover a layout, declare the type)

When a struct is *not yet defined*, infer its layout from `this+offset` access patterns and declare
it onto the IDB so the disassembly reads `obj->field` instead of `*(this+0x1C)`. (Complements
`ida-struct-recovery`, which dumps an *existing* IDA struct/vtable.)

1. Pick the access window: a constructor, an init routine, or a hot method that works on `this` (note
   the register/stack slot — usually `ecx` in `__thiscall`, or arg0). Record a working struct name
   (`CActor`, `PakHeader`).
2. Read `${CLAUDE_SKILL_DIR}/scripts/struct_probe.py`, set CONFIG (`FUNCS` = functions to scan;
   `THIS_REG` = `ecx`; `STRUCT_NAME`), run via the exec tool. It walks each function's disassembly,
   finds `[this+0xNN]` accesses, and records per offset: access size (byte/word/dword/qword), read vs
   write, whether offset 0 is dereferenced+called (a **vtable pointer** signature), and the highest
   offset touched (a size lower bound). It emits a neutral offset table — **no pseudo-C**.
3. Interpret: mark offset 0 `vtable*` if `call [this+0]` / `mov eax,[this]; call [eax+..]` appears;
   give each offset a *candidate* type from size + use (pointer if dereferenced, counter if compared
   in a loop, float if FPU-used). Keep these as candidates, not facts. Save to
   `Docs/RE/_dirty/structs/<struct>.offsets.md` for a spec-author rewrite.
4. **(Optional) declare to the IDB** if it aids analysis: `mcp__ida__declare_type` (a C struct of the
   recovered fields) + `set_type` / `type_apply_batch` on `this` at the analyzed functions; re-run
   `read_struct` to confirm. Firewall-safe (a layout, not pseudo-code).

## Decision points (all modes)

- **A `conflict` verdict** (desired name already bound elsewhere) → do **not** force it; stage as a
  proposal and let the maintainer reconcile.
- **A `skip-missing`** (no item at address) → the manifest/glossary may be pinned to a different build;
  re-check the SHA-256.
- **An ambiguous offset size/meaning** (Mode D) or an ambiguous live caller → confirm dynamically:
  hand to `ida-debugger-drive` — breakpoint in the maintainer's live `?ext=dbg` F9 session and
  `dbg_read` the live instance/registers (reads through `PAGE_NOACCESS`). Static infers; the debugger confirms.
- **A `type` fails to apply** (Mode C/D) → apply the name+comment, report the type failure, continue
  the cluster.

## Verify / Done when

- A dry-run ran before any apply; the user gave explicit go-ahead; the IDB SHA-256 matched the pin (or
  the mismatch was surfaced and apply withheld).
- Every applied name/comment/type/offset traces to its source of truth (`names.yaml` / gate-passed
  glossary slice / observed access pattern); zero CRT/runtime symbols touched; conflicts skipped, not forced.
- A re-run reports `noop` for applied entries (idempotent); the mode's dirty report exists with banner
  + SHA-256; `names.yaml` / `journal.md` / committed specs untouched.

## Pitfalls (never)

- Never write a new/invented name, comment, type, or offset straight into the IDB — only the
  gate-passed glossary / `names.yaml` / an *observed* access pattern; everything else is a staged proposal.
- Never write a comment that is pseudo-C, a `sub_`/`_DWORD`/`__thiscall` token, mangled, or "paste into C#".
- Never override the CRT/FLIRT runtime-symbol skip, and never coerce `"0x004A1230"` to an int.
- Never apply across a SHA-256 mismatch — names/comments would land on the wrong addresses.
- Never serialize out of fear — concurrent IDB writers are allowed; retry a failed/conflicting call instead.
- Never let a per-entry failure silently drop — record it in the JSON and report every failure.

> **N1:** the legibility layer of clean-room RE — the IDB reads in project names, neutral interop
> comments, and recovered layouts, with no pseudo-C or non-glossary name ever crossing the firewall.

## Hard rules

- Apply only names/comments/types from the mode's source of truth; declared *layouts* are allowed, a
  *transcribed decompilation* is not. Always dry-run → apply only on explicit confirmation; idempotent.
- Never rename compiler-runtime symbols; never write pseudo-C anywhere; address keys stay quoted strings.
- The only repo file each mode writes is its dirty report; never touch committed specs, `names.yaml`,
  `journal.md`, the `glossary.yaml` merge point, or any `0X.*` source / `.cs` / `.csproj` / `.slnx`.
- MCP down / wrong DB / SHA-256 mismatch ⇒ STOP before applying; never fabricate the dry-run.
