---
name: ida-struct-recovery
description: Use when you need the field layout or vtable of a legacy object from the Martial Heroes Main.exe — for example a packet struct, an entity/actor class, or an asset header — so a spec-author can write a clean offset table. Dumps member offset/size/type and vtable slots to the dirty quarantine, plus a neutral .h-style offset table that is safe to promote.
allowed-tools: Read Write
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
2. **Discover the exec tool name at runtime.** `mcp__ida__*` names vary by build. List them and
   pick the script-execution tool (e.g. `mcp__ida__execute_script` / `mcp__ida__run_python` /
   `mcp__ida__eval`). Fall back to typed `mcp__ida__*` struct/type tools only if no exec tool
   exists.

## Inputs

- A **target**, one of:
  - a **struct/type name** registered in the IDB's local types (e.g. `Actor`, `MsgMovePlayer`), or
  - a **vtable address** (e.g. `0x006C1A40`) to recover a class by its method table, or
  - a **global instance address** whose first DWORD is a vtable pointer (the snippet follows it).

## Steps

1. Read the bundled snippet `${CLAUDE_SKILL_DIR}/scripts/struct_dump.py` (also
   `scripts/struct_dump.py`). It is real, runnable IDAPython using `ida_typeinf` / `ida_struct` /
   `ida_bytes`.
2. Edit the `TARGET` / `TARGET_KIND` lines at the top of the source before sending it: set
   `TARGET_KIND` to `"type"`, `"vtable"`, or `"instance"`, and `TARGET` to the name or address.
3. Feed the edited source to the discovered MCP exec tool. The snippet:
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

## Decision points

- **If the target is a polymorphic class** (`__thiscall`, `this` in `ECX`), vtable recovery is the way
  in — resolve each slot to its function name. **If it's a plain struct/header**, members suffice.
- **If a member's type is unknown**, emit a raw byte span (`uint8_t pad_0x14[4];`) in the `.offsets.h`
  rather than guessing a type.
- **If field semantics are uncertain** (is offset 0x1C a length or a flags word?), that's a runtime
  question: hand off to `ida-debugger-drive` — breakpoint a method in the maintainer's F9 session and
  `dbg_read` the live instance to read the actual field values. Static gives the layout; the debugger
  confirms the meaning.

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
