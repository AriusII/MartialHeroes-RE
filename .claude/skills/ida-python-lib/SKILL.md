---
name: ida-python-lib
description: Curated, idempotent IDAPython snippet library and harness conventions for reverse-engineering the Martial Heroes client (doida.exe; Main.exe historical) through the IDA MCP. Use whenever authoring or running IDAPython against the live IDB — provides the RESULT_JSON single-line harness contract, the safe_rename/safe_cmt idempotency pattern, and ready snippets (enumerate functions skipping CRT/thunks, walk xrefs, decode MSVC switch/dispatch tables incl. two-level index, recover struct layout from this+offset access, detect XOR/ROL/ROR crypto loops and S-box constant tables, batch rename/comment). Run snippets via mcp__ida__py_eval / py_exec_file / run_script and emit exactly one RESULT_JSON line to Docs/RE/_dirty/queries/. Pairs with ida-py (escape hatch), ida-explore, ida-struct-recovery, ida-crypto-hunt, ida-annotate.
allowed-tools: mcp__ida__py_eval, mcp__ida__py_exec_file, mcp__ida__run_script, mcp__ida__save_script, mcp__ida__read_script, mcp__ida__list_scripts, Read, Write
model: sonnet
effort: high
---

# ida-python-lib — IDAPython harness + curated snippet catalogue

This skill is the **shared library** the IDA-facing skills/agents draw on when they need to author or
run IDAPython against the live IDB: the **RESULT_JSON harness contract**, the **idempotency contract**,
a **catalogue of ready snippets** (each emitting neutral data, never transcribing target code), and the
**anti-patterns** that cost real RE time. It is method + reusable code, not a one-shot procedure —
`ida-py` is the freeform escape hatch; this skill is the curated, reviewed core those one-offs build on.

**Tool routing.** Run snippets via `mcp__ida__py_eval` (a single expression), `mcp__ida__py_exec_file`
(a multi-statement snippet/harness), or `mcp__ida__run_script` — and **persist** a reusable probe in
the DB with `mcp__ida__save_script` / `mcp__ida__read_script` / `mcp__ida__list_scripts` so it is not
re-pasted each session. (The legacy `eval`/`execute_script`/`run_python` names do not exist on this
build.)

Clean-room firewall: this role writes ONLY to `Docs/RE/_dirty/` (gitignored). It NEVER pastes Hex-Rays
pseudo-C, `sub_`/`loc_` autonames, `_DWORD`/`_BYTE`, `__thiscall`/`__fastcall`, mangled names, or raw
addresses into any committed file or C#. Findings cross the firewall only as neutral prose/offset
tables, and only via `spec-author`. If the IDA MCP is down or the wrong/empty IDB is loaded, STOP and
report — never fabricate IDA output.

## 1. The RESULT_JSON harness contract

Every probe prints **exactly one** line beginning `RESULT_JSON ` followed by `json.dumps(...)` of a
single result object — nothing else on stdout that a parser would confuse for it. Rules:

- **One line per probe.** A second `RESULT_JSON` line makes the result ambiguous; collapse everything
  into one object.
- **EAs are hex strings, not ints.** Encode every address as `"0x004A1230"`; never emit a bare int and
  never coerce an address string back to int (truncation/precision loss on round-trip).
- **Output lands in `Docs/RE/_dirty/queries/`.** The harness best-effort-writes the JSON there with a
  descriptive slug; addresses are stripped before any promotion to a committed spec.
- **Metadata only.** The result carries counts, hex-address strings, names, offsets, sizes, and the
  handful of bytes/strings you deliberately read — **never** whole-function disassembly or pseudo-C.

The `emit()` helper (snippet 1.7) is the canonical implementation; the freeform template lives in the
`ida-py` skill (`scripts/ida_py_template.py`). On exception the harness prints
`RESULT_JSON {"ok": false, "error": "..."}` — read the error and fix the snippet, never guess around it.

## 2. The idempotency contract (for any write-shaped snippet)

Write-shaped snippets feed `ida-annotate` / `ida-toolsmith` (the only IDB writers) — but the pattern is
authored here:

- **Re-runnable to a noop.** Gate every write on "is it already the desired value?"; a re-run reports
  `noop`, never a duplicate comment or a re-clobbered name.
- **Dry-run → apply.** First pass emits a JSON diff (`apply` / `noop` / `skip` / `conflict`); apply
  only on explicit confirmation.
- **Glossary-gated names.** A name is written only if its exact `address → name` mapping exists in
  `Docs/RE/names.yaml`; everything else is staged as a proposal.
- **Never touch runtime symbols.** Skip `FUNC_LIB` / `FUNC_THUNK` flagged functions and `_`-prefixed /
  MSVC-CRT / FLIRT-library names.
- **Modern namespaces.** Prefer `ida_funcs`/`ida_name`/`ida_bytes`/`ida_typeinf`/`idautils` over legacy
  `idc`/`idaapi` shims.

## 3. Snippet catalogue

Each entry: purpose, the IDAPython shape, and the RESULT_JSON it emits. Snippets EMIT neutral data —
they never transcribe target code into a spec.

### 3.1 Enumerate functions (skip CRT/thunk/lib)
Walk `idautils.Functions()`; for each `ida_funcs.get_func(ea)` skip `flags & (FUNC_LIB|FUNC_THUNK)` and
`_`-prefixed names. For a function's true byte extent use `idautils.Chunks(ea)` (sum chunk ranges) —
**never trust `f.size()` / `f.end_ea`** for chunked/non-contiguous functions.
Emit: `{"ok":true,"count":N,"funcs":[{"ea":"0x..","name":"..","chunks":k,"bytes":n}, ...]}`.

### 3.2 Walk xrefs
For a target EA, separate **code** refs (`idautils.CodeRefsTo`) from **data** refs
(`idautils.DataRefsTo`), and resolve each caller to its containing function. For "who calls X" build the
unique caller-function set.
Emit: `{"ok":true,"target":"0x..","code_refs":[..],"data_refs":[..],"callers":["0x..",..]}`.

### 3.3 Decode switch/dispatch
First try `ida_nalt.get_switch_info(ea)` at the indirect-jump site. **MSVC two-level caveat:** large or
sparse MSVC switches compile to a `u8` index table that indexes the *real* jump table — read the
value/lowcase index table or the opcodes mis-map. **Fallbacks that return no switch info:** a
function-pointer **array** (walk it as data — read N consecutive pointer-width slots) and an
**if/else ladder** (walk the compare-chain). `microcode_calls` often resolves the dispatch cleaner than
pseudocode.
Emit: `{"ok":true,"kind":"switch|fnptr_array|ifelse","cases":[{"sel":N,"handler":"0x.."}, ...]}`.

### 3.4 Recover struct from this+offset access
Scan the access window (ctor / init / hot method working on `this` in `ecx` for `__thiscall`, or arg0).
For each `[this+0xNN]` access record: offset, access **width** (b/w/d/q), **read vs write**, and whether
offset 0 is dereferenced-and-called (a **vtable pointer** signature). Highest offset touched is a size
lower bound. Cross-ref the constructor to confirm field initialization order.
Emit: `{"ok":true,"struct":"..","size_min":N,"fields":[{"off":"0x1c","width":4,"rw":"r","vtable":false}, ...]}`.

### 3.5 Crypto-shaped loop + S-box detection
Two signals fused: (a) **bit-op density** — loops dense in `xor`/`shl`/`shr`/`rol`/`ror`/`and`/`or` over
a byte/dword stream; (b) **magic constants** — SHA1 `0x5A827999`/`0x6ED9EBA1`, MD5/SHA init
`0x67452301`, TEA `0x9E3779B9`, CRC32 `0xEDB88320`, plus an RC4 256-twin-loop shape; (c) **S-box
tables** — 256-byte (or 256-dword) read-only arrays indexed by a byte. Highest signal: **anchor on the
socket-recv path** and walk outward, so you catch the decrypt actually applied to wire data.
Emit: `{"ok":true,"candidates":[{"ea":"0x..","bitop_density":0.0,"const_hits":[".."],"sbox":false}, ...]}`.

### 3.6 `safe_rename` / `safe_cmt` (idempotent batch)
`safe_rename(ea, name)`: skip if current name already equals `name` (`noop`); skip CRT/thunk/lib;
otherwise `ida_name.set_name(ea, name, SN_CHECK)` and report `apply`. `safe_cmt(ea, text)`: read the
existing comment; if it already contains `text`, `noop`; else append (never clobber). Both gate on the
glossary and accumulate a per-entry verdict list.
Emit: `{"ok":true,"applied":k,"noop":m,"skipped":[{"ea":"0x..","reason":".."}, ...]}`.

### 3.7 `emit()` RESULT_JSON helper
```python
import json
def emit(obj):
    obj.setdefault("ok", True)
    print("RESULT_JSON " + json.dumps(obj, separators=(",", ":")))
```
Wrap the probe body in `try/except` and `emit({"ok": False, "error": repr(e)})` on failure so the line
is always present and parseable.

## 4. Anti-patterns (never)
- **Non-idempotent writes** — duplicating a comment or re-clobbering a name on re-run.
- **Renaming CRT/thunks/library** functions, or any `_`-prefixed / FLIRT-flagged symbol.
- **Trusting `get_switch_info` for every dispatch** — MSVC two-level index tables, fn-ptr arrays, and
  if/else ladders need the data-walk fallbacks (3.3).
- **`f.end_ea` / `f.size()` on chunked functions** — use `idautils.Chunks`.
- **Fabricating output when the MCP is down** or the wrong/empty IDB is loaded — STOP and report.
- **Bare-int EAs** in RESULT_JSON, or transcribing whole disassembly/pseudo-C into the result.

## 5. Firewall note
Snippets emit **neutral data** (counts, offsets, hex-address strings, deliberately-read bytes) to
`Docs/RE/_dirty/queries/`. Pseudo-C, autonames, and raw addresses **never** cross to a committed spec —
promotion is a rewrite by `spec-author`, never a paste. This SKILL file is itself committed, so it
carries no RE fact, no address, and no copyrighted byte.
