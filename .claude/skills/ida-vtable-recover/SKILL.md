---
name: ida-vtable-recover
description: Use to resolve a C++ vtable in the legacy Martial Heroes client (Main.exe) — walk its function-pointer slots and describe the conceptual method behind each slot (ctor/dtor, virtual handler, getter) — into a NEUTRAL slot table under Docs/RE/_dirty/structs. The way to recover an object's polymorphic interface (e.g. an actor or asset-loader class hierarchy) before a spec-author writes the clean struct.
allowed-tools: Read Write
model: sonnet
effort: high
---

# ida-vtable-recover — resolve a vtable into a neutral slot table

A C++ class's virtual interface lives in its vtable: a contiguous array of function pointers at a
fixed data address, with the object's first field (`+0`) pointing at it. This skill takes a vtable
address (or the object/constructor that installs it), walks each slot, and produces a **neutral slot
table** — slot index, slot's data EA, target function EA/name, and a one-line conceptual description
of what that slot does (constructor/destructor, an `Update`/`Render`-style virtual, a getter). The
table lands under `Docs/RE/_dirty/structs/` for a spec-author to rewrite into a clean
`Docs/RE/structs/*.md`.

This pairs with `ida-struct-apply` (which recovers data-field offsets) — together they give the whole
object: fields + virtual interface.

All output is **dirty** (addresses derived directly from the copyrighted binary) and lands under
`Docs/RE/_dirty/structs/`.

Every slot in the table must **reflect a pointer actually read from the IDB** — the binary is the
single truth for the polymorphic interface, so never list a phantom slot or label a role you did not
read. The slot table and its role tags are hypotheses; a live debugger read of the object (or reading
the slot's body) confirms them. If the MCP is down or the vtable won't resolve, **STOP and report —
never invent a slot or a target.**

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. The script-exec tool
   (`mcp__ida__py_exec_file` / `execute_script` / `run_python`) drives the bundled walker. Typed
   helpers (`mcp__ida__get_bytes`, `mcp__ida__read_struct`, `mcp__ida__xrefs_to`,
   `mcp__ida__decompile` for a *single* slot you must understand) are supporting tools.

## Steps

1. **Locate the vtable.** Either you already have its data EA, or you start from the
   constructor/object: the vtable address is the value moved into `[this+0]` near the top of the
   constructor (look for `mov dword ptr [ecx], offset ??_7Class@@...`). Note the data EA and a
   working class name (e.g. `CActor`). *If only the object instance is in hand (e.g. caught live in
   the debugger), read the dword at `[obj+0]` to get the vtable EA — `dbg_read` reads through
   `PAGE_NOACCESS` against the running client; never `dbg_start` (the maintainer F9-launches).* If the
   constructor moves several offsets into `[this+N]`, the one at `+0` is the primary vtable; secondary
   ones at `+N` are multiple-inheritance/base subobject vtables — recover each as its own table.
2. **Walk the slots.** Read `${CLAUDE_SKILL_DIR}/scripts/vtable_recover.py`, set CONFIG (`VTABLE_EA`
   = the table's data EA, OR `CTOR` = the constructor name/address to auto-find the table;
   `CLASS_NAME`; `MAX_SLOTS`). Run it via the exec tool. It reads consecutive pointer-sized slots
   from `VTABLE_EA`, stops at the first non-code / cross-referenced boundary (the next vtable or
   data), and for each slot records: slot index, slot EA, target function EA + name, the target's
   in-degree, and a heuristic role tag (destructor if it matches the `~`/`vector deleting
   destructor` pattern, getter if tiny, virtual handler otherwise).
3. **Describe each slot, neutrally.** Turn the heuristic tags into a one-line conceptual description
   per slot ("slot 0: destructor", "slot 3: per-frame update", "slot 7: serialize-to-buffer"). These
   are hypotheses; confirm the important ones by reading the single slot's behavior
   (`ida-decompile-export`) before a spec author promotes them.
4. **Save it.** The walker best-effort-writes `Docs/RE/_dirty/structs/<class>.vtable.md`; confirm the
   path or save the table yourself. It stays in `_dirty/` — promotion to a clean
   `Docs/RE/structs/*.md` is a spec-author's deliberate rewrite.
5. **Hand off.** In your reply, summarize the interface in words (slot count, which slots are
   ctor/dtor, the notable virtuals) and point to the `_dirty/` table. Resolve `sub_…` slot targets
   to proposed canonical names for `ida-naming-sync`; never rename here.

## Verify / Done when

- The walk stopped at a natural boundary (next named vtable, a data label, or a non-code slot) — not
  at `MAX_SLOTS` (if it hit the cap, the table is likely longer; raise the cap and re-walk).
- Every slot has an EA, a target, an in-degree, and a one-line **hypothesised** role — and the
  important roles are spot-confirmed by reading the slot (`ida-decompile-export`), not left as guesses.
- The slot table is written under `Docs/RE/_dirty/structs/<class>.vtable.md` with the SHA-256 tag and
  the `> DIRTY` banner; no address leaked into the reply.

## Pitfalls

- **Never** treat a heuristic role tag as confirmed truth — a "getter" by size can be an inlined
  accessor with side effects; phrase roles as hypotheses until read.
- **Never** run past the table boundary into adjacent RTTI/data and report phantom slots — bound with
  `MAX_SLOTS` and stop at the first non-code/cross-referenced slot.
- Do not confuse a base-subobject vtable (offset `+N`) with the primary (`+0`); mixing them mislabels
  the polymorphic interface.
- **Never** rename slot targets here or transcribe a slot's pseudo-C — names go through the glossary,
  bodies stay in `ida-decompile-export`'s quarantine.

> **N1:** recovering the polymorphic interface as a neutral slot table is the static half of clean-room
> RE — the hypothesis a spec-author promotes and the debugger can confirm against the live object.

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/structs/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#** and never paste Hex-Rays pseudo-C into a reply or any file. The artifact
  is a **slot table** (index / slot EA / target / role), not function bodies. Reading one slot
  closely is `ida-decompile-export`'s job.
- Slot roles are heuristic hypotheses — phrase them as such; a spec author confirms before
  promotion. Never invent a slot or a target you did not read from the table.
- Bound the walk (`MAX_SLOTS`) and stop at the natural table boundary. Do not run off into adjacent
  data.
- Addresses appear only inside `_dirty/`. In replies, describe the interface structurally.
