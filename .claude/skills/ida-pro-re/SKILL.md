---
name: ida-pro-re
description: Use as background methodology whenever the work touches IDA Pro / the IDA MCP, reverse-engineering Main.exe or doida.exe, dirty-room analysis, the Docs/RE/_dirty pipeline, or the clean-room firewall — opcodes, packet/struct/format recovery, decompiler output, pseudo-C, or promoting RE findings to committed specs. Carries the dirty→spec→engineer conventions and the legal firewall; not a runnable procedure.
user-invocable: false
---

# ida-pro-re — RE methodology & clean-room firewall (conventions)

Background knowledge for IDA Pro 9.3 + MCP reverse-engineering of `Main.exe` / `doida.exe`. This is
**method, not a procedure** — it injects the conventions that keep RE work *legal* and *firewalled*.
Authoritative sources (read them; do not re-derive): `CLAUDE.md` → "Clean-Room Firewall (`Docs/RE/`)"
and "Reverse-Engineering & MCP Tooling"; `Docs/CAMPAIGN_TEMPLATE.md` (§0.3 / §4.4 firewall, §2–3
tiers/concurrency); `Docs/RE/README.md`.

## Legal basis (why the firewall is non-negotiable)
- Decompilation is permitted **solely for interoperability** — EU Software Directive **2009/24/EC
  Art. 6**. The dirty→spec→engineer separation is what keeps that lawful. Break the separation and the
  legal basis is gone.

## The dirty → spec → engineer pipeline (one-way)
1. **Dirty-room (IDA analysts):** write findings **ONLY** to `Docs/RE/_dirty/…` — gitignored, tainted,
   never shipped. Record facts in **neutral prose** (names, offsets, sizes, shapes, behaviour).
2. **Spec-authors:** **REWRITE — never copy** — `_dirty/` findings into committed neutral specs:
   `Docs/RE/opcodes.md` (no addresses), `Docs/RE/packets/*.yaml`, `Docs/RE/formats/*.md`,
   `Docs/RE/structs/*.md`, `Docs/RE/specs/*.md`.
3. **Implementation engineers:** read **only** the clean specs. They never open `_dirty/` or IDA, and
   every magic constant / byte offset in C# cites its spec (`// spec: Docs/RE/...`).

## Never transcribe decompiler output
- **No Hex-Rays pseudo-C** in any committed file or any C#: no `sub_xxxx`, `loc_xxxx`, `_DWORD`/`_BYTE`,
  `__thiscall`/`__fastcall`/`__cdecl`, `*(_DWORD*)…`, `a1`/`v12` autonames, or mangled MSVC symbols.
- Describe **behaviour and layout in neutral words**, then **re-implement fresh** from the spec.
- This SKILL file is itself committed → it stays neutral: no pseudo-C, no raw addresses, no copyrighted
  bytes. Add no new RE fact beyond what the cited committed docs already state.

## MCP usage
- Prefer the **debugger-extended endpoint**: `http://127.0.0.1:13337/mcp?ext=dbg` (superset — static +
  `dbg_*` tools from one connection). If `mcp__ida__dbg_*` tools are absent, the session is on the wrong
  (base) endpoint; re-register on `?ext=dbg`. Tool names are deferred — **discover `mcp__ida__*` at
  runtime**. Verify with `/ida-mcp-connect` before any RE work.
- **Static analysis forms the hypothesis; the IDA debugger confirms it against ground truth** (run the
  real client, breakpoint, read registers/memory, step). Both modes are dirty-room — findings cross the
  firewall only as neutral prose.
- Never drive the debugger destructively from a worker session: do not `dbg_start` unless explicitly
  piloting a live session the maintainer opened.

## Concurrency — UNBRIDLED (no caps)
- **Fan out HARD.** Read analysts run **massively parallel** and IDB **writes run in parallel too** —
  there is **no `~3` sub-wave cap and no one-writer-at-a-time rule**. The mission is to reverse the
  **entire** `doida.exe` (every subsystem, opcode, format, behavior) for a 1:1 Godot re-creation, so
  push as wide and fast as the IDA MCP server sustains.
- The only real ceiling is the live MCP server's own throughput; if a call fails or conflicts, **retry
  it** rather than throttling back. The firewall, dry-run→apply, and idempotency safety all still hold.

## STOP conditions (never fabricate)
- STOP and report if the IDA MCP is **down**, or the **wrong / empty database** is loaded, or the
  `dbg_*` toolset is missing when needed. **Never invent or guess IDA output** to keep moving.

## Self-check before writing anything
- Output path is under `Docs/RE/_dirty/` (dirty-room) — never a committed spec, `0X.*` source folder,
  `.cs`/`.csproj`/`.slnx`, `journal.md`, or `names.yaml`.
- No pseudo-C, no autonames, no addresses, no copyrighted bytes leave the dirty room.
- Promotion to a committed spec is a **rewrite**, done by a spec-author — not a paste.
