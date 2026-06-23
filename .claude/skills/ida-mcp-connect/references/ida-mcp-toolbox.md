# IDA MCP toolbox — neutral capability map

A categorized map of the live `mcp__ida__*` toolset: what each family is **for** and which RE task it
serves. **Neutral by construction** — no binary specifics, no addresses, no names. The toolbox is a
**map that helps you query the truth efficiently**; it does **not** outrank the binary. IDA on
`doida.exe` is the single strict truth — confirm open facts there, never from memory or analogy.

Discover the exact names at runtime (the manifest depends on the running IDA build); the families below
are stable. `?ext=dbg` is a **superset** (static + `dbg_*`); prefer it always.

---

## PREFLIGHT
| Tool | For |
|---|---|
| `server_health` | The structured UP report: `idb_path`/`module`/`imagebase`/`auto_analysis_ready`/`hexrays_ready`/`strings_cache`. Confirms the right, non-empty `doida.exe` IDB is loaded and ready. |
| `server_warmup` | Init Hex-Rays / build caches once so the first heavy query isn't cold. |

## SURVEY / RECON — *where am I, what is here*
| Tool | For |
|---|---|
| `survey_binary` | **FIRST call of any new investigation:** file meta, segments, entry points, top strings + functions, imports, callgraph summary. Orients the whole attack. |
| `list_funcs` / `list_globals` | Enumerate the function / global-data inventory. |
| `imports` / `imports_query` | What the client links against (the API surface — frames the recovery hypotheses). |
| `export_funcs` | Exported entry points. |
| `func_profile` / `lookup_funcs` | Profile a function (size/blocks/callers) or resolve by name/pattern. |

## SEARCH — *find the thing*
| Tool | For |
|---|---|
| `find` / `find_bytes` / `find_regex` | Locate by text / byte pattern / regex (signatures, magic, dispatch markers). |
| `search_structs` | Find a struct/type by shape or name. |
| `get_string` / `get_bytes` / `get_int` / `get_global_value` | Read a literal value at a known site (string, bytes, integer, initialized global). |
| `int_convert` | Reinterpret/convert an integer (endianness, signedness, base). |

## NAVIGATION / XREF / FLOW — *how does control & data move*
| Tool | For |
|---|---|
| `xref_query` / `xrefs_to` / `xrefs_to_field` | Who references this code/data/struct-field — the spine of subsystem mapping. |
| `callgraph` / `callees` | Caller/callee graph around a function. |
| `basic_blocks` | CFG of one function (branch structure). |
| `trace_data_flow` | Follow a value through the function (def-use). |
| `func_query` / `insn_query` / `entity_query` | Structured queries over functions / instructions / entities (e.g. find a dispatch switch). |
| `list_instances` / `select_instance` | Multiple matches → list and pick. |

## DECOMPILE / DISASM — **DIRTY-ROOM OUTPUT ONLY**
Hex-Rays pseudo-C and disassembly are **tainted**: they land **only** under `Docs/RE/_dirty/` (gitignored),
**never** in a committed file or any C#. Never transcribe `sub_`/`loc_`/`_DWORD`/`__thiscall`/mangled names
across the firewall — the clean crossing is `spec-author` rewriting into neutral specs.
| Tool | For |
|---|---|
| `decompile` | Hex-Rays pseudo-C for one function → `_dirty/` only. |
| `disasm` | Raw disassembly of a range. |
| `analyze_function` / `analyze_component` | Auto-analysis summary of a function / a related cluster. |
| `analyze_batch` | Bulk analysis across many targets (fan out wide; retry on conflict). |

## TYPES / STRUCTS / ENUMS — *recover the layout*
| Tool | For |
|---|---|
| `read_struct` / `type_inspect` / `type_query` | Read a struct's field-offset layout / inspect a type / query the type DB. |
| `infer_types` | Let IDA infer types for a function/site (a hypothesis to confirm). |
| `declare_type` / `set_type` / `type_apply_batch` | Declare/apply a type (recovered layout) to the IDB. |
| `enum_upsert` | Create/update an enum (e.g. recovered opcode/flag set). |
| `stack_frame` / `declare_stack` / `delete_stack` | Inspect/declare/clear a function's stack frame. |
| `define_code` / `define_func` / `undefine` | Mark bytes as code/function, or undefine. |

## IDB ANNOTATION / WRITE — **`ida-toolsmith` ONLY**
IDB-mutating tools are idempotent and run **dry-run → apply** via `ida-toolsmith` (the only IDB-write
agent). They improve IDB legibility; they do **not** cross the firewall by themselves.
| Tool | For |
|---|---|
| `rename` | Rename a function/global/local to a recovered, neutral name. |
| `set_comments` / `append_comments` | Set/append IDB comments (provenance, intent). |
| `set_type` / `declare_type` / `type_apply_batch` | Apply recovered types (also listed above). |
| `enum_upsert` | Persist a recovered enum. |
| `idb_save` | Persist the IDB. |
| `patch` / `patch_asm` / `put_int` | **MUTATE CODE/DATA — avoid** unless explicitly patching the binary; never as part of recovery. |

## IDAPYTHON — *the escape hatch*
| Tool | For |
|---|---|
| `py_eval` / `py_exec_file` | Run arbitrary IDAPython when no typed tool fits. Std-lib only inside IDA's restricted context; use a `RESULT_JSON` harness to return structured results. Output is dirty. |

## DEBUGGER — `?ext=dbg` superset, **`re-validator` ONLY, NEVER `dbg_start`**
The debugger **confirms** a static hypothesis against ground truth on the maintainer's live session
(maintainer F9-launches; you pilot). **Never** `dbg_start`. `dbg_read` reads through `PAGE_NOACCESS`.
| Tool | For |
|---|---|
| `dbg_gpregs` / `dbg_regs*` / `dbg_regs_named` | Read register state at a stop. |
| `dbg_read` / `dbg_write` | Read (through `PAGE_NOACCESS`) / write process memory. |
| `dbg_add_bp` / `dbg_toggle_bp` / `dbg_delete_bp` / `dbg_bps` | Manage breakpoints. |
| `dbg_continue` / `dbg_run_to` / `dbg_step_into` / `dbg_step_over` | Drive execution. |
| `dbg_stacktrace` | Inspect the call stack at a stop. |
| `dbg_exit` | Detach/end the debug session (never `dbg_start`). |

## DIFF / VERIFY
| Tool | For |
|---|---|
| `diff_before_after` | Compare IDB/analysis state before vs after a change (verify an annotation/recovery pass). |

---

## Which tool for which RE task
| RE task | Lane |
|---|---|
| Orient on a new subsystem | `survey_binary` **first**, then `list_funcs`/`imports_query`. |
| Find a dispatch / opcode table | `survey_binary` → `find`/`find_regex` → `xref_query`/`insn_query` to map the switch. |
| Read ONE function | `ida-explore` decompile-one mode → `decompile` → **`_dirty/` only**. |
| Trace who calls/uses something | `xrefs_to` / `xrefs_to_field` / `callgraph` / `trace_data_flow`. |
| Recover a struct layout | `read_struct` / `type_inspect` → `declare_type` (apply via `ida-toolsmith`). |
| Read a literal/global value | `get_string` / `get_bytes` / `get_int` / `get_global_value`. |
| Annotate the IDB (rename/comment/type) | `ida-toolsmith` only, dry-run → apply, idempotent → `idb_save`. |
| Confirm a fact LIVE | `dbg_*` via `re-validator` (never `dbg_start`). |
| Anything no typed tool fits | `py_eval` / `py_exec_file` (RESULT_JSON harness; output dirty). |

## Static forms the hypothesis · the debugger confirms
Static analysis (SURVEY/SEARCH/XREF/DECOMPILE/TYPES) **proposes** what the client does. The `?ext=dbg`
debugger (`dbg_*`, via `re-validator`) **confirms** it against the running binary — registers, memory,
breakpoints, stepping. A load-bearing fact is not settled until it is debugger-confirmed (or
capture-confirmed, for pixels). Captures are the visual oracle; the binary is the behavioral truth.

## Recover → confirm → port gate chain
- **G0 BRAINSTORM** — attack plan; pick the lane(s) above.
- **G1 RECOVER** — static recovery into `_dirty/` (SURVEY/SEARCH/XREF/DECOMPILE/TYPES). *Confidence: static-hypothesis.*
- **G2 CONFIRM** — end-to-end via the `?ext=dbg` debugger (`re-validator`) for every load-bearing fact. *Confidence: debugger-confirmed / capture-confirmed.*
- **G3 PROMOTE** — `spec-author` rewrites (never copies) into committed neutral `Docs/RE/` specs. *Confidence: spec-promoted.*
- **G4 READINESS** — stamp implementation-ready (banner pinned to IDB SHA). *Confidence: implementation-ready.*
- **then** C# porting consumes the clean spec — never `_dirty/`, never IDA.

## STOP rules (non-negotiable)
- MCP **DOWN** → STOP and report. Never fabricate `mcp__ida__*` output.
- **Wrong / empty DB** (`server_health` `module`/`idb_path` is not the `doida.exe` client, or
  `auto_analysis_ready` false) → STOP and report; recovery on the wrong DB is silent garbage.
- Addresses, `sub_`/`loc_`/`_DWORD`/`__thiscall`/mangled names, and pseudo-C **never** escape into a
  committed file — `_dirty/` only; the clean crossing is `spec-author`.
