---
name: ida-struct-recovery
description: Use when you need the field layout or vtable of a legacy object from the Martial Heroes client (Main.exe / doida.exe) — for example a packet struct, an entity/actor class, or an asset header — so a spec-author can write a clean offset table. Seeds prototypes with infer_types, detects vtables via classify_pointer, and confirms layouts against live instances via read_struct_live. Dumps member offset/size/type and vtable slots to the dirty quarantine, plus a neutral .h-style offset table that is safe to promote. Includes a dedicated VTABLE-WALK mode that resolves a C++ vtable from its data EA or its installing constructor, role-tags each function-pointer slot (ctor/dtor, per-frame virtual, getter, serialize), and harvests RTTI — the way to recover a polymorphic interface (an actor or asset-loader class hierarchy) before the clean struct is written.
allowed-tools: mcp__ida__*, Read, Write
model: sonnet
effort: high
---

# ida-struct-recovery — recover field & vtable layouts of a legacy object

Dumps a legacy structure or class from the 32-bit `Main.exe`: every member's
**offset / size / type / name**, and — for a polymorphic class — its **vtable slots** (slot index,
address, and the function name at each slot). Targets are 2004-era MSVC `__thiscall` objects whose
`this` arrives in `ECX`, so vtable recovery is how class shapes are discovered.

Every offset, size, and slot dumped must **reflect what the binary proves** — the layout is read from
the IDB, never inferred to look tidy. The IDB is the single absolute truth for the object's shape; a
static layout is the hypothesis, a live debugger read confirms field meaning. If the MCP is down or a
target won't resolve, **STOP and report the exact error — never invent an offset or a slot.**

This skill works in **two modes**:

- **Mode A — struct/vtable dump** (`struct_dump.py`): resolve a type / vtable / instance and dump
  members (offset/size/type/name) and vtable slots, producing both a dirty detail file and a neutral
  promotable `.offsets.h`.
- **Mode B — dedicated vtable walk + RTTI** (`vtable_recover.py` + RTTI harvest): walk a vtable from
  its data EA or its installing constructor, role-tag each slot, and harvest RTTI — the richer path
  for recovering a polymorphic class interface and hierarchy.

This skill produces **two** kinds of output:

1. A **dirty** dump under `Docs/RE/_dirty/structs/` — the full member/vtable detail with addresses
   and any decompiler-derived type names. Contaminated; never committed.
2. A **neutral, promotable `.h`-style offset table** also written under `Docs/RE/_dirty/structs/`
   (suffix `.offsets.h`). It is pure layout (offset, size, primitive type, neutral field name) with
   **no addresses, no pseudo-code, no source-symbol leakage** — the shape a spec-author can rewrite
   into `Docs/RE/structs/*.md` with minimal risk. It is written to `_dirty/` first so a human
   makes the explicit decision to promote; the skill itself never writes into committed paths.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Prefer the typed struct/type tools; script-exec is the fallback.** Lead with the typed family:
   `mcp__ida__read_struct` (static decode of a type at an EA) and `mcp__ida__read_struct_live` (decode
   the live layout at a real `this` pointer in the maintainer's `?ext=dbg` session — via `re-validator`);
   `mcp__ida__type_inspect` / `mcp__ida__type_query` / `mcp__ida__search_structs` /
   `mcp__ida__domain_type_layout` to inspect known types; `mcp__ida__infer_types` to seed a prototype
   from access patterns; `mcp__ida__classify_pointer` to detect a vtable pointer; `mcp__ida__xrefs_to_field`
   (+ `mcp__ida__watch_field` at runtime) to discover who reads/writes each field; and
   `mcp__ida__lvar_usage` / `mcp__ida__stack_frame` for stack-local structs. Run the bundled IDAPython
   only when no typed tool covers the need, via `mcp__ida__py_exec_file` / `py_eval` / `run_script`
   (persist reusable probes with `save_script` / `read_script` / `list_scripts`; one `RESULT_JSON` line
   per probe — see `ida-python-lib`).

## Inputs

- A **target**, one of:
  - a **struct/type name** registered in the IDB's local types (e.g. `Actor`, `MsgMovePlayer`), or
  - a **vtable address** (e.g. `0x006C1A40`) to recover a class by its method table, or
  - a **global instance address** whose first DWORD is a vtable pointer (the snippet follows it).

## The `this+offset` recovery workflow (how a layout is actually built)

A 2004-era `__thiscall` object has no declared type at first — you recover it from how the code uses
it. The loop: (1) harvest every `[base+off]` read/write against the object pointer plus the access
**width** (byte/word/dword/float) and direction (read vs write) — `mcp__ida__xrefs_to_field` once a
partial type exists, or `mcp__ida__infer_types` to seed one from raw access; (2) cross-reference the
**constructor** (the function that writes the vtable into `[this+0]` and initializes the early fields)
to fix sizes and zero-init defaults; (3) apply the partial type and **re-decompile** (`force_recompile`)
so the next still-unknown offset surfaces with a meaningful access; (4) repeat until the layout closes.
Unknown spans become raw byte padding, never guessed types. Field *meaning* (is `+0x1C` a length or a
flags word?) is confirmed live via `read_struct_live` / `watch_field` through `re-validator`.

## Mode A — struct/vtable dump (steps)

1. Read the bundled snippet `${CLAUDE_SKILL_DIR}/scripts/struct_dump.py` (also
   `scripts/struct_dump.py`). It is real, runnable IDAPython using `ida_typeinf` / `ida_struct` /
   `ida_bytes`.
2. Edit the `TARGET` / `TARGET_KIND` lines at the top of the source before sending it: set
   `TARGET_KIND` to `"type"`, `"vtable"`, or `"instance"`, and `TARGET` to the name or address.
3. Feed the edited source to `mcp__ida__py_exec_file`. The snippet:
   - resolves the target,
   - walks members (offset, byte size, declared type, name) for a struct/type,
   - walks vtable slots (index, function address, function name) for a vtable/instance,
   - computes the binary SHA-256, and
   - prints one JSON line prefixed `STRUCT_JSON:`.
   Capture that line.
4. Parse the JSON. Create `Docs/RE/_dirty/structs/` if absent. Write **two** files, both tagged
   with the full SHA-256:
   - `Docs/RE/_dirty/structs/<name>-<sha8>.dirty.md` — the full detail (members table with
     addresses/types, vtable slot table with addresses and symbol names). Begin with:
     ```
     > DIRTY — struct/vtable dump from Main.exe (sha256 <full-sha>). Contains addresses &
     > decompiler-derived symbols. Never commit. Promote only via a rewritten neutral spec.
     ```
   - `Docs/RE/_dirty/structs/<name>-<sha8>.offsets.h` — a C-header-style layout with ONLY
     `offset / size / primitive-type / neutral-field-name` (strip addresses and original symbol
     names; use neutral names like `field_0x10` when unknown). Prefix it with a one-line comment:
     `/* PROMOTABLE LAYOUT — no addresses, no pseudo-code. Spec-author: rewrite into Docs/RE/structs/<name>.md. sha256 <full-sha> */`
5. Report: the target, member count, vtable slot count, the SHA-256, and both output paths. State
   clearly that `.offsets.h` is the promotion candidate but a spec-author must still review and
   rewrite it into `Docs/RE/structs/` (and log it in `journal.md`).

## Mode B — dedicated vtable walk + RTTI

For a polymorphic class, the dedicated walker recovers the virtual interface more richly than Mode A's
slot dump: it discovers the table from the constructor, role-tags each slot, and bounds the walk at the
natural table boundary. It pairs with `ida-annotate`'s struct-apply mode (data-field offsets) —
together they give the whole object: fields + virtual interface.

1. **Locate the vtable.** Either you already have its data EA, or you start from the
   constructor/object: the vtable address is the value moved into `[this+0]` near the top of the
   constructor (`mov dword ptr [ecx], offset ??_7Class@@…`). Note the data EA and a working class name
   (e.g. `CActor`). *If only the object instance is in hand (e.g. caught live in the debugger), read
   the dword at `[obj+0]` to get the vtable EA — `dbg_read` reads through `PAGE_NOACCESS` against the
   running client; never `dbg_start` (the maintainer F9-launches).* If the constructor moves several
   offsets into `[this+N]`, the one at `+0` is the primary vtable; secondary ones at `+N` are
   multiple-inheritance/base-subobject vtables — recover each as its own table.
2. **Walk the slots.** Read `${CLAUDE_SKILL_DIR}/scripts/vtable_recover.py`, set CONFIG (`VTABLE_EA` =
   the table's data EA, OR `CTOR` = the constructor name/address to auto-find the table; `CLASS_NAME`;
   `MAX_SLOTS`). Run it via `mcp__ida__py_exec_file`. It reads consecutive pointer-sized slots from `VTABLE_EA`,
   stops at the first non-code / cross-referenced boundary, and for each slot records: slot index, slot
   EA, target function EA + name, the target's in-degree, and a heuristic role tag (destructor for the
   `~`/`vector deleting destructor` pattern, getter if tiny, virtual handler otherwise).
3. **Describe each slot, neutrally.** Turn the heuristic tags into a one-line conceptual description
   per slot ("slot 0: destructor", "slot 3: per-frame update", "slot 7: serialize-to-buffer"). These
   are hypotheses; confirm the important ones by reading the single slot's behavior
   (`ida-explore` DECOMPILE-ONE mode) before a spec-author promotes them.
4. **(Optional) harvest RTTI / class hierarchy.** Lead with `mcp__ida__module_hierarchy` for the
   static RTTI class tree and `mcp__ida__hierarchy_runtime_overlay` (via `re-validator`) to map observed
   runtime construction onto that tree. For a wider sweep the bundled
   `${CLAUDE_SKILL_DIR}/scripts/rtti_harvest.py` enumerates RTTI complete-object-locators / type
   descriptors / base-class arrays, and `${CLAUDE_SKILL_DIR}/scripts/c01_manifest_gen.py` builds a
   class/vtable manifest from that harvest — both for cartography passes over the object model.
5. **Save it.** The walker best-effort-writes `Docs/RE/_dirty/structs/<class>.vtable.md` (SHA-256 tag
   + `> DIRTY` banner); confirm the path or save it yourself. It stays in `_dirty/` — promotion to a
   clean `Docs/RE/structs/*.md` is a spec-author's deliberate rewrite. Resolve `sub_…` slot targets to
   proposed canonical names for `ida-annotate`'s names-sync mode; never rename here.

## Decision points

- **If the target is a polymorphic class** (`__thiscall`, `this` in `ECX`), vtable recovery is the way
  in — resolve each slot to its function name. **If it's a plain struct/header**, members suffice.
- **If a member's type is unknown**, emit a raw byte span (`uint8_t pad_0x14[4];`) in the `.offsets.h`
  rather than guessing a type.
- **If field semantics are uncertain** (is offset 0x1C a length or a flags word?), that's a runtime
  question: hand off to `ida-debugger-drive` — breakpoint a method in the maintainer's F9 session and
  `dbg_read` the live instance to read the actual field values. Static gives the layout; the debugger
  confirms the meaning.
- **If a Mode B vtable walk hits `MAX_SLOTS`** rather than a natural boundary (next named vtable / data
  label / non-code slot), the table is likely longer — raise the cap and re-walk; never report phantom
  slots run off into adjacent RTTI/data. Don't confuse a base-subobject vtable (`+N`) with the primary (`+0`).

## Verify / Done when

- Both files exist under `_dirty/structs/`: the `.dirty.md` (with `> DIRTY` banner + full SHA-256) and
  the `.offsets.h` (PROMOTABLE comment, no addresses, no symbol names, no pseudo-C).
- Member/vtable counts are reported; one object per invocation.
- The `.offsets.h` is pure layout — verify it contains no `0x…` address and no original symbol name.

## Pitfalls (never)

- Never let an address, vtable function name, or pseudo-C into the `.offsets.h`.
- Never invent an offset, size, or slot name — report the resolve error and stop.
- Never write directly into `Docs/RE/structs/` — promotion is a separate human-reviewed act.

*North star N1: recovers a legacy object's shape as a promotable offset table — the static layout a debugger read confirms field-by-field.*

## Hard rules

- Both outputs go under `Docs/RE/_dirty/structs/`. This skill NEVER writes directly into
  `Docs/RE/structs/` or any other committed path — promotion is a separate human-reviewed act.
- The `.offsets.h` must not contain addresses, vtable function names, or pseudo-C. If a field type
  is unknown, emit a raw byte span (`uint8_t pad_0x14[4];`) rather than guessing.
- Never invent offsets, sizes, or slot names. If the type/vtable cannot be resolved, report the
  exact error and stop.
- One object per invocation.
