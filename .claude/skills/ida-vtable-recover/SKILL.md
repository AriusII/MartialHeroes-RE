---
name: ida-vtable-recover
description: Use to resolve a C++ vtable in the legacy Martial Heroes client (Main.exe) — walk its function-pointer slots and describe the conceptual method behind each slot (ctor/dtor, virtual handler, getter) — into a NEUTRAL slot table under Docs/RE/_dirty/structs. The way to recover an object's polymorphic interface (e.g. an actor or asset-loader class hierarchy) before a spec-author writes the clean struct.
allowed-tools: Read Write
model: sonnet
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
   working class name (e.g. `CActor`).
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
