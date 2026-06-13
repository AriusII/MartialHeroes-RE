---
name: ida-struct-apply
description: Use to recover the in-memory layout of a C++ object in the legacy Martial Heroes client (Main.exe) — field offsets, sizes, and the vtable pointer — into a NEUTRAL offset table under Docs/RE/_dirty/structs. Reconstructs the struct from member-access patterns (this+offset reads/writes) and applies it to the IDB via mcp__ida__declare_type / mcp__ida__type_apply_batch so the disassembly reads structurally. Offset table only — never Hex-Rays pseudo-C.
allowed-tools: Read Write
model: sonnet
effort: high
---

# ida-struct-apply — recover a C++ object layout into a neutral offset table

Given a constructor, an allocation site, or any function that works on `this`, this skill recovers the
object's layout — which byte offset holds which field, how big each field is, and where the vtable
pointer sits — by observing the `this+offset` access patterns. The deliverable is a **neutral offset
table** (offset / size / access kind / candidate type) under `Docs/RE/_dirty/structs/`, which a
spec-author can rewrite into a clean `Docs/RE/structs/*.md`. Optionally it applies the recovered
struct type back to the IDB so the disassembly reads `obj->field` instead of `*(this+0x1C)`.

This complements `ida-struct-recovery` (which dumps an existing IDA struct/vtable). Use
**ida-struct-apply** when the struct is *not yet defined* and you must infer it from access patterns,
then optionally declare it.

All output is **dirty** (addresses + offsets derived directly from the copyrighted binary) and lands
under `Docs/RE/_dirty/structs/`.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. For *recovery* use the
   script-exec tool with the bundled probe. For *applying* a recovered type, prefer typed tools —
   names vary by build but look like `mcp__ida__declare_type`, `mcp__ida__type_apply_batch`,
   `mcp__ida__set_type`, `mcp__ida__read_struct`, `mcp__ida__stack_frame`.

## Steps

1. **Pick the access window.** Identify the function(s) that operate on the object: a constructor,
   an init routine, or a hot method. Note the register/stack slot that holds `this` at entry (usually
   `ecx` in `__thiscall`, or arg0). Record a working struct name (e.g. `CActor`, `PakHeader`).
2. **Recover the layout.** Read `${CLAUDE_SKILL_DIR}/scripts/struct_probe.py`, set CONFIG
   (`FUNCS` = the function names/addresses to scan; `THIS_REG` = `ecx` by default; `STRUCT_NAME`).
   Run it via the exec tool. It walks the disassembly of each function, finds memory accesses based at
   `this` (`[this+0xNN]` style), and records for each offset: the access size (byte/word/dword/qword
   inferred from the operand), whether it is read/written, whether offset 0 is dereferenced+called
   (a **vtable pointer** signature), and the highest offset touched (a lower bound on the object
   size). It emits a neutral offset table — **no pseudo-C**.
3. **Interpret the table.** Mark offset 0 as `vtable*` if a `call [this+0]`/`mov eax,[this]; call
   [eax+..]` pattern appears. Give each offset a candidate type from its size + use (pointer if
   dereferenced, counter if compared in a loop, float if used by FPU ops). Keep these as candidates,
   not facts.
4. **Save it.** The probe best-effort-writes `Docs/RE/_dirty/structs/<struct>.offsets.md`; confirm
   the path or save the table yourself. This stays in `_dirty/` — promotion to a clean
   `Docs/RE/structs/*.md` is a spec-author's deliberate rewrite.
5. **(Optional) Apply to the IDB.** Only if it helps further analysis, declare the recovered struct
   with `mcp__ida__declare_type` (a C struct of the recovered fields) and apply it to the `this`
   pointer at the analyzed functions with `mcp__ida__set_type` / `mcp__ida__type_apply_batch`. This
   is firewall-safe (it is a layout, not pseudo-code) and makes the disassembly readable. Re-run
   `read_struct` to confirm. Note that this edits the IDB, not the repo.
6. **Hand off.** In your reply, describe the layout in words (n fields, vtable at 0, notable
   pointer/count/float fields, inferred size) and point to the `_dirty/` table for a spec-author.
   Resolve `sub_…` names to proposed canonical names for `ida-naming-sync`; never rename here.

## Decision points

- **If a `call [this+0]` / `mov eax,[this]; call [eax+..]` pattern appears**, mark offset 0 as
  `vtable*`. **If no such pattern**, treat it as a plain struct (offset 0 is a real field).
- **If you choose to apply the type to the IDB (step 5)** and you're fanned out under an orchestrator,
  the IDB write is **strictly serialized** — exactly one writer at a time; never apply concurrently
  with another analyst. A declared C struct (layout) is firewall-safe; a transcribed decompilation is not.
- **If an offset's size or meaning is ambiguous** from access patterns alone, confirm it dynamically:
  hand off to `ida-debugger-drive` — breakpoint a method in the maintainer's live F9 session and
  `dbg_read` the live instance to size and read the field. Static infers the layout; the debugger confirms it.

## Verify / Done when

- The `<struct>.offsets.md` exists under `_dirty/structs/` with offset / size / access / candidate type,
  every type marked as a candidate (not a fact), no pseudo-C.
- If applied to the IDB, `read_struct` confirms the declared type took and the disassembly reads `obj->field`.
- No address leaked outside `_dirty/`; the reply describes the layout structurally.

## Pitfalls (never)

- Never invent an offset you did not observe in an access pattern.
- Never apply an IDB type concurrently with another analyst — writes are serialized.
- Never paste a Hex-Rays body; a declared layout type is allowed, a transcribed decompilation is not.

*North star N1: infers an undefined object's layout from access patterns into a promotable table — the static shape a live debugger read confirms.*

## Hard rules

- The recovery output goes ONLY to `Docs/RE/_dirty/structs/`. Never write to a committed RE spec,
  `names.yaml`, `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#** and never paste Hex-Rays pseudo-C into a reply or any file. The artifact
  is an **offset table** (offset / size / access / candidate type), not a function body.
- A declared C struct type *applied to the IDB* is allowed (it is a layout). A *transcribed
  decompilation* is not. Keep the distinction sharp.
- Offsets/sizes are candidates inferred from access patterns — phrase them as such; a spec author
  confirms before promotion. Never invent an offset you did not observe.
- Addresses appear only inside `_dirty/`. In replies, describe the layout structurally.
