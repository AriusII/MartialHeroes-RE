---
name: re-brainstorm
description: Use when starting to reverse a Martial Heroes subsystem or facing an open RE question on doida.exe — to generate hypotheses, plan the search angles, pick the right IDA MCP tools for each, and design the confirm-via-debugger plan BEFORE deep-reading. Produces a prioritized attack plan into Docs/RE/_dirty/.
allowed-tools: mcp__ida__*, Read, Write
model: opus
effort: high
---

# re-brainstorm — design the attack BEFORE deep-reading (gate G0)

The cheapest way to waste an hour in IDA is to start decompiling the first function that looks relevant.
This skill runs **gate G0** of the RE pipeline (G0 BRAINSTORM → G1 RECOVER → G2 CONFIRM → G3 PROMOTE →
G4 READINESS): turn an open RE question on `doida.exe` into a **prioritized, tool-aware attack plan** —
competing hypotheses, the search angles that discriminate between them, the exact IDA MCP tool for each
angle, the cheapest-first ordering, and the list of load-bearing facts that MUST be debugger-confirmed.
It is **not** the generic superpowers brainstorming skill: this is RE-specific and IDA-tool-aware, and it
ends in a written plan, not a chat.

The plan is **dirty** (it names anchors, addresses, hypotheses derived from the binary) and lands at
`Docs/RE/_dirty/<subsystem>/attack-plan.md`. Nothing here is committed; promotion is `spec-author`'s job.

## Ground truth

IDA on `doida.exe` is the **single absolute truth**. The plan is a hypothesis-ranking device, not a
finding — every angle is a *question to ask the binary*, never an answer remembered from "how MMORPGs
usually work." The IDA-MCP toolbox (`/ida-mcp-connect`) is the **map** that helps you query the truth
efficiently; the committed specs are the **derived** help — neither outranks the binary. MCP down / wrong
DB ⇒ **STOP and report**, never fabricate a plan around invented anchors.

## Preconditions

- **MCP green.** Run `/ida-mcp-connect`; confirm a live IDA Pro 9.3 server at
  `http://127.0.0.1:13337/mcp?ext=dbg` with the target IDB open. Red ⇒ STOP and surface
  `claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`.
- A light recon is allowed here (cheap survey calls to anchor hypotheses) — but **no deep body reads**;
  that is G1's `ida-explore` / the analysts' job. G0 plans the reads; it does not perform them.
- Check `Docs/ROADMAP.md` + `Docs/RE/journal.md` first: if the subsystem is already settled, say so and
  scope the plan to the genuinely-open delta — never re-RE what specs already pin (per the "don't re-RE"
  doctrine).

## Procedure

### 1. RESTATE — and define "understood end-to-end"
Restate the RE question in one sentence, then list **what must be pinned for this subsystem to be
implementation-ready** — the concrete artifacts, not vague "understand it":
- which **opcodes** (major/minor) and their **packet field layouts** (offset · width · type · endianness);
- which **struct offsets** / vtable slots / RTTI names;
- which **branches / state transitions** decide behavior (the conditions, not just the call);
- which **constants / tables / S-boxes / file-format magics** drive parsing.
This checklist becomes the plan's "done = all confirmed" target and the G4 readiness criteria.

### 2. HYPOTHESES — generate 2–4 competing ones
Force genuine alternatives the search will *discriminate between*, e.g. "opcode is 1-byte vs 2-byte (the
`(major<<16)|minor` shape)"; "cipher is per-packet XOR vs a stream keyed at handshake"; "the loader
mmaps the whole `.pak` vs streams records." Each hypothesis must name **what evidence would confirm or
kill it** — a hypothesis you can't disprove is not yet a plan.

### 3. SEARCH ANGLES — and the IDA tool for each
For each hypothesis, list the angle(s) that yield the discriminating evidence and the matching
`mcp__ida__*` tool (discover exact tool names at runtime via `/ida-mcp-connect` — names vary by build):

| Angle | What it finds | IDA MCP tool(s) |
|---|---|---|
| **String-hunt** | format extensions, error/log strings, asset paths next to their loader | `survey_binary`, `get_string`, `find_regex` (CP949 game text = opaque) |
| **Import-hunt** | `recv`/`WSARecv`/`CreateFileA`/`CryptAcquireContext` next to the subsystem | `imports_query` |
| **Dispatch / jump table** | the opcode `switch` / handler-pointer array | `find`, `xref_query`, `insn_query` |
| **Constant tables / S-boxes** | cipher key schedule, magic numbers, lookup tables | `find_bytes`, `get_bytes` |
| **Xref-out from a known anchor** | callers/callees of a pinned entry point; the subsystem cluster | `xrefs_to`, `xref_query`, `callgraph`, `callees` |
| **Data-flow on a buffer/length** | how a decrypted buffer / size field flows to the parser | `trace_data_flow` |
| **RTTI / vtable** | class identity + method slots for a struct/object | `search_structs`, `read_struct` |

Prefer **single-referencer** strings/imports as entry anchors (shared log strings are noise). If a
subsystem must exist but a keyword bucket is empty (classic for crypto), pivot to import + constant-table
evidence — the cipher is often string-less and constant-driven.

### 4. ORDER — cheapest, highest-signal first
Rank the angles so the early calls eliminate the most hypotheses per token. General order of yield:
string-hunt + import-hunt (cheapest map) → xref-out from the best anchor → dispatch/constant scans →
data-flow / RTTI (deeper, only once an anchor is fixed). Mark each angle's expected outcome ("if this
finds N dense cases → H1; if a pointer array → H2") so G1 knows what each result means before it runs.

### 5. CONFIRM PLAN — what MUST hit the live debugger
Static analysis forms the hypothesis; the **`?ext=dbg` debugger confirms it against ground truth.** List
every **load-bearing** fact — the ones implementation will encode byte-for-byte — and mark it
`debugger-confirm` or `capture-confirm`:
- a packet's exact field offsets/widths and the opcode value on the wire → confirm by breakpointing the
  dispatcher in the maintainer's live session and observing a known packet (`re-validator` /
  `ida-debugger-drive`);
- a cipher's key/rounds → confirm by reading the buffer before/after decrypt at runtime;
- a struct offset read on a hot path → confirm by reading the live struct at a breakpoint;
- a rendered-scene detail → mark `capture-confirm` (oracle > spec for pixels).
**NEVER `dbg_start`** — the maintainer F9-launches the client; the plan only says *what to confirm and
where to breakpoint*, and hands execution to `re-validator`. Facts that are static-decidable (a constant
table, a fixed file magic) need no debugger — say so, to keep the confirm budget on what actually varies
at runtime.

### 6. WRITE the plan
Write `Docs/RE/_dirty/<subsystem>/attack-plan.md` (create dirs). Begin with
`> DIRTY — derived from doida.exe; never commit; do not copy into specs.` and the IDB SHA-256 (from recon
/ `/ida-mcp-connect`). Sections: **Question · Done-when (the pin checklist) · Hypotheses · Ordered angles
(+ tool + expected outcome) · Confirm plan (load-bearing → debugger/capture) · Risks**. Keep it neutral
prose; addresses/anchors stay inside this file.

## Confidence ladder (label every fact the plan will produce)

`static-hypothesis` → `debugger-confirmed | capture-confirmed` → `spec-promoted` → `implementation-ready`.
The plan's job is to map each "done-when" artifact onto this ladder and name the call that advances it.

## Decision points

- **Subsystem already in the specs?** Scope the plan to the open delta only; cite the existing spec; don't
  re-derive settled facts.
- **No string/import anchor surfaces?** That IS a finding — the subsystem is constant-driven; pivot the
  plan to `find_bytes` / `trace_data_flow` from the recv or file-read path, not more string-hunting.
- **A hypothesis has no disproving test?** It's not ready — refine it until an IDA call can kill it.
- **Two angles cost the same but one is debugger-only?** Prefer the static angle first (forms the
  hypothesis cheaply); reserve the debugger for the load-bearing confirm in step 5.

## Verify / Done when

- `Docs/RE/_dirty/<subsystem>/attack-plan.md` exists with the `> DIRTY` banner + IDB SHA-256.
- Each "done-when" artifact maps to ≥1 ordered angle (with its IDA tool) and a confirm label.
- 2–4 genuinely-competing, disprovable hypotheses; the angles are ordered cheapest-highest-signal first.
- Every load-bearing fact is marked `debugger-confirm` / `capture-confirm`, with **no** `dbg_start`.
- No address, string, or anchor leaked outside `_dirty/`.

## Pitfalls (never)

- Never deep-read bodies here — G0 plans; G1 (`ida-explore` / analysts) reads. Don't decompile.
- Never write a plan from memory/analogy when the MCP is red — STOP and surface the connect hint.
- Never call `dbg_start` — pilot the maintainer's live `?ext=dbg` session via `re-validator`.
- Never let the plan assert a *finding* — it lists questions and the calls that answer them, nothing more.
- Never paste pseudo-C / mangled names / `_DWORD` into the plan; addresses stay in `_dirty/`.

*North star N1: G0 makes the unbridled reverse aim before it fires — the right tool on the right anchor,
static-hypothesis first, debugger-confirm for everything load-bearing — so the fan-out runs wide and lands true.*

## Hard rules

- Output path is **only** `Docs/RE/_dirty/<subsystem>/`. Never write under `specs|packets|formats|structs|
  opcodes.md|names.yaml|journal.md`.
- The plan is dirty and contaminated; promotion to a committed spec is `spec-author`'s separate, deliberate
  rewrite — never copy this file across the firewall.
- Discover `mcp__ida__*` tool names at runtime; if a planned tool is absent, name the typed-tool fallback
  in the plan rather than fabricating.
