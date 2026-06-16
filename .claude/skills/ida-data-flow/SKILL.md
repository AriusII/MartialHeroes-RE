---
name: ida-data-flow
description: Use to trace how a value flows forward or backward from an instruction/operand in the legacy Martial Heroes client (Main.exe) — e.g. "where does this recv buffer go", "what feeds this length field", "what is this register at the call site". Drives the typed mcp__ida__trace_data_flow tool (falling back to a bundled intra-function def/use IDAPython snippet) and writes a neutral flow trace to Docs/RE/_dirty/static/. The way to follow a packet field or a parsed value through the code.
allowed-tools: Read Write
model: sonnet
effort: high
---

# ida-data-flow — forward/backward data-flow trace from an address

Given a starting address (and optionally an operand/register), this skill follows the data:
**forward** ("where does this value end up — which fields, calls, stores consume it") or **backward**
("what produced this value — which load, constant, or parameter feeds it"). It is the tool for
turning a single observed value — a received byte, a parsed length, a computed offset — into the
chain of instructions that produce or consume it, which is exactly what a packet/format spec needs.

All output is **dirty** (addresses derived directly from the copyrighted binary) and lands under
`Docs/RE/_dirty/static/`.

**Ground truth:** the chain is read FROM the binary, never inferred from memory or from "where a value
like this usually goes." Each step is a definition/use the tool actually found; an ambiguous step is
reported as ambiguous, never resolved by guess. The static path is the hypothesis a debugger read
confirms byte-for-byte (below). MCP down / wrong DB ⇒ STOP, never fabricate a flow step.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the Martial Heroes IDB open. If red, STOP and surface:
   `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the toolset at runtime.** List the `mcp__ida__*` tools. **Strongly prefer** the typed
   `mcp__ida__trace_data_flow` tool — it performs a real interprocedural flow trace and is far more
   accurate than anything a snippet can reconstruct. Note the supporting tools
   (`mcp__ida__disasm`, `mcp__ida__basic_blocks`, `mcp__ida__stack_frame`). If no data-flow tool
   exists, fall back to the bundled intra-function def/use snippet (single-function scope only).

## Steps

1. **Pin the start point.** Record the start EA (an instruction), the `DIRECTION` (`forward` |
   `backward`), and the value of interest — a register name (e.g. `eax`), a stack slot, or a
   specific operand index. Confirm the EA is inside a real function.
2. **Run the trace.**
   - **Typed path (preferred):** call `mcp__ida__trace_data_flow` with the start EA, direction, and
     the operand/register selector the tool expects. Capture the returned chain of definitions/uses
     (each step: EA, instruction mnemonic, the value's new location). Use `mcp__ida__disasm` /
     `mcp__ida__basic_blocks` to confirm any step you are unsure of.
   - **Snippet path (fallback):** read `${CLAUDE_SKILL_DIR}/scripts/data_flow.py`, set CONFIG
     (`START_EA`, `DIRECTION`, `REG` to follow, `MAX_STEPS`). It does a bounded **intra-function**
     def/use walk over the disassembly: backward it finds the nearest definitions of `REG`; forward
     it finds the uses and any move that propagates the value into another register/stack slot. It is
     deliberately conservative and stops at calls and function boundaries — it will not silently
     pretend to follow across functions.
3. **Note the boundaries.** Flag every point where the value (a) is stored to memory/a struct field,
   (b) is passed into a call (record which arg slot), (c) crosses a function boundary, or (d) is
   combined with another value. These boundaries are the interesting facts for a spec author.
4. **Save it.** The snippet best-effort-writes `Docs/RE/_dirty/static/dataflow.<slug>.md`; confirm
   the path, or save the trace yourself with the Write tool. For typed-tool output, write the step
   table by hand into the same location.
5. **Interpret, neutrally.** In your reply, describe the chain in words: where the value comes from
   or goes, the struct field offsets / call-arg slots it touches, and what that implies for the
   format/packet (hand off to `protocol-spec-author` / `asset-spec-author`, or to
   `ida-struct-apply` if it lands in an object field). Resolve `sub_…` names to proposed canonical
   names for `ida-naming-sync`; never rename here.

## Decision points

- **If tracing a packet/recv buffer** and the value is the post-cipher payload, the cleanest ground
  truth is dynamic: hand off to `ida-debugger-drive` — breakpoint just after decrypt in the
  maintainer's live F9 session and `dbg_read` the buffer (it reads through `PAGE_NOACCESS`) pre/post.
  Static traces the path; the debugger confirms the actual bytes and the length field's real value.
- **If the chain forks** (the value is combined or copied to two sinks), record each sink as its own
  step; do not collapse them.
- **If the value crosses a function boundary** with the fallback snippet, stop and continue as a
  separate trace (or use the typed `trace_data_flow`) — never fake interprocedural flow.

## Verify / Done when

- Every boundary (store-to-field, call-arg slot, function crossing, value combine) is flagged in the chain.
- A `dataflow.<slug>.md` exists under `_dirty/static/`; the reply states what the chain implies for the spec.
- No ambiguous step was silently resolved — ambiguity is reported, not guessed.

## Pitfalls (never)

- Never extend the intra-function snippet to pretend cross-function flow.
- Never resolve a value-laundering `xor reg,reg` or indirect address by guessing — report it.
- Never paste the disassembled body; the artifact is a step chain (EA + what happens to the value).

*North star N1: follows a packet field or parsed value to its sinks — the static hypothesis a debugger read then confirms byte-for-byte.*

## Hard rules

- Output goes ONLY to `Docs/RE/_dirty/static/`. Never write to a committed RE spec, `names.yaml`,
  `journal.md`, or any `0X.*` source folder / `.cs` / `.csproj` / `.slnx`.
- **Never decompile-to-C#** and never paste Hex-Rays pseudo-C into a reply or any file. Describe the
  flow as a chain of steps (EA + what happens to the value), not as a transcribed function body.
- Addresses appear only inside `_dirty/`. In replies, name struct-field offsets and call-arg slots
  in plain terms.
- The fallback snippet is **intra-function and conservative by design** — it stops at calls and
  function edges. Do not extend it to fake interprocedural flow; when the value crosses a boundary,
  say so and continue the trace as a separate step (or use the typed tool).
- Never invent a flow step. If the trace is ambiguous (e.g. a value-laundering `xor reg,reg`,
  indirect addressing), report the ambiguity rather than guessing.
