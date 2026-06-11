---
name: ida-decompile-export
description: Use when you need to understand exactly ONE function's behavior in the legacy Martial Heroes Main.exe to author or refine a spec — for example a packet handler, a crypto routine, or an asset parser. Exports that function's raw Hex-Rays pseudo-C plus its callers/callees to the dirty quarantine for an analyst to read, then describe in neutral prose.
allowed-tools: Read Write
model: sonnet
---

# ida-decompile-export — decompile ONE function into the dirty quarantine

Decompiles a single function (by canonical name or address) from the legacy 32-bit `Main.exe`,
together with its cross-references (callers and callees) and local-variable/type context. The raw
Hex-Rays pseudo-C is written to `Docs/RE/_dirty/functions/<name>.dirty.md`.

> [!IMPORTANT]
> **The output of this skill is CONTAMINATED.** It is verbatim decompiler pseudo-code and is
> copyright-tainted derived work. It may live ONLY under `Docs/RE/_dirty/`. It must NEVER be
> committed, pasted into C#, or copied into `Docs/RE/specs|packets|formats|structs|opcodes.md`.
> The ONLY thing that may cross the clean-room firewall is a **separately authored, neutral
> behavior note** that a spec-author writes *from scratch* in plain language — describing what the
> function does and its data layout, never how the decompiler phrased it. This skill does not
> produce that note; it only produces the dirty source an analyst reads.

## Preconditions (do these first, in order)

1. **MCP must be green.** Run `/ida-mcp-connect` and confirm a live IDA Pro 9.3 MCP server at
   `http://127.0.0.1:13337/mcp` with the IDB open and the Hex-Rays decompiler available. If red,
   STOP and surface: `claude mcp add --transport http ida http://127.0.0.1:13337/mcp`.
2. **Discover the exec tool name at runtime.** `mcp__ida__*` names vary by build. List the
   `mcp__ida__*` tools and pick the script-execution tool (e.g. `mcp__ida__execute_script` /
   `mcp__ida__run_python` / `mcp__ida__eval`). If a typed `mcp__ida__decompile` /
   `mcp__ida__decompile_function` tool exists you may use it for the body, but still run the
   bundled snippet to gather xrefs and the SHA-256 tag.

## Inputs

- A **target**: either a canonical function name from `Docs/RE/names.yaml` (e.g.
  `RecvPacketDispatch`) or an address string (e.g. `0x004A1230`). Prefer the name; it survives
  rebases. Read `Docs/RE/names.yaml` to resolve the address if you only have a name and the name
  has not yet been applied to the DB (in that case fall back to the address from names.yaml).

## Steps

1. Read the bundled IDAPython snippet `${CLAUDE_SKILL_DIR}/scripts/decompile_one.py` (also
   `scripts/decompile_one.py`). It is real, runnable IDAPython using `ida_hexrays` + `idautils`.
2. Set the target at the top of the source you send: replace the `TARGET = ...` line with the
   user's name or address before handing the source to the MCP exec tool. (Pass the function name
   in quotes, or the address as an int literal like `0x004A1230`.)
3. Feed the edited source to the discovered MCP script-exec tool. The snippet:
   - resolves the target to an address and function,
   - runs the Hex-Rays decompiler on it,
   - collects xrefs **to** the function (callers) and **from** it (callees), with names,
   - captures the function's prototype / local-variable types,
   - computes the binary SHA-256, and
   - prints one JSON line prefixed `DECOMP_JSON:`.
   Capture that line from the tool's return value.
4. Parse the JSON. Create `Docs/RE/_dirty/functions/` if absent. Write
   `Docs/RE/_dirty/functions/<name>.dirty.md` where `<name>` is the canonical name (or
   `sub_<addr>` if unnamed). The file MUST begin with this banner verbatim, then the content:

   ```
   > DIRTY — verbatim Hex-Rays pseudo-C from Main.exe (sha256 <full-sha>). COPYRIGHT-TAINTED.
   > Never commit. Never copy into C# or into Docs/RE/specs|packets|formats|structs|opcodes.md.
   > A spec-author must REWRITE behavior in neutral prose; this file is read-only reference.
   ```

   Then sections: `## Target` (name + address + prototype), `## Callers (xrefs to)`,
   `## Callees (xrefs from)`, `## Local types`, and a fenced ```` ```c ```` block with the raw
   pseudo-C body.
5. Report: the resolved address, caller/callee counts, the SHA-256, and the dirty output path.
   Remind the user that promotion requires a fresh neutral note authored by a spec-author.

## Hard rules

- Output path is ALWAYS under `Docs/RE/_dirty/functions/`. Never anywhere else.
- Do NOT summarize the function's logic into a committed spec from this skill — that is a separate,
  deliberate clean-room rewrite step. Stay in the dirty room.
- One function per invocation. To map a call tree, run repeatedly using the discovered callee
  names; do not bulk-dump the whole binary.
- Never invent pseudo-code, addresses, or xrefs. If decompilation fails (e.g. no Hex-Rays license,
  bad target), report the exact error and stop.
