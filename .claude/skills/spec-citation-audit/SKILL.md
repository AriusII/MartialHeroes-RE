---
name: spec-citation-audit
description: Use to enforce the project's "every magic constant cites its spec" rule — scans committed layer C# for magic numeric literals (byte offsets, sizes, opcodes, hex constants) that lack a nearby '// spec: Docs/RE/...' citation, and reports file:line. The targeted citation checker that backs the clean-room provenance of every wire/asset constant.
allowed-tools: Read Grep Glob Bash(python *)
model: sonnet
effort: high
---

# spec-citation-audit — find magic constants missing a // spec: citation

The project rule (CLAUDE.md, the blueprint): **every magic numeric constant in C# must cite its
source spec** with a `// spec: Docs/RE/...` comment on the same line or just above. An offset, a
record size, an opcode value, or a struct field position with no citation is provenance-less — it
might have been eyeballed from the binary, which is exactly what the clean-room firewall forbids.

This skill is the **targeted citation auditor**. It is narrower than `clean-room-audit` (which also
hunts decompiler autonames and MSVC artifacts): here the single question is *"does this magic
number cite a spec?"*. It is **read-only** and reports `file:line` — it never edits code.

## What counts as a "magic constant" (and what is ignored)

Flagged when uncited:

- Hex literals `0x..` of two or more digits (`0x90`, `0x2C`, `0x1F4`) — classic byte offsets/sizes.
- Numeric array indexers and slices on a span/buffer: `[40]`, `[0x14]`, `Slice(112, 8)`, `+ 104`.
- Larger bare decimal literals (≥ a threshold, default 16) that look like sizes/offsets, e.g. `144`.

Ignored as benign (low false-positive design):

- `0`, `1`, `-1`, `2` and other tiny structural constants.
- Literals inside a `// comment` or string.
- Lines already carrying `// spec: Docs/RE/...` (on the line or within a few lines above).
- Common non-offset contexts: `[Theory]`/`[InlineData]` test attributes, enum `= N` members,
  `Version = "..."`, array-rank `new byte[n]` allocation sizes (heuristic).
- Anything under `obj/`, `bin/`, generated `*.g.cs`/`*.Designer.cs`, and the whole
  `Docs/RE/_dirty/` tree.

A citation is satisfied by `// spec: Docs/RE/<path>` appearing on the flagged line or within the
configurable lookback window above it (so a block of offsets under one `// spec:` header passes).

## Steps

1. **Scope.** Default target is every `**/*.cs` under the five numbered layer folders
   (`01.Infrastructure.Shared` … `05.Presentation`) plus `tests/`. Build output and generated files
   are skipped by the scanner. Pass `--root 02.Network.Layer` to scope to one layer/project.

2. **Run the bundled scanner:**

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/citation_scan.py --root .
   ```

   Useful flags:
   - `--format json` — machine-readable findings for further processing.
   - `--root <dir>` — scope to a single layer or project directory.
   - `--min-decimal <n>` — raise/lower the bare-decimal threshold (default 16).
   - `--lookback <n>` — how many lines above a literal a `// spec:` may sit (default 3).

3. **Triage hits with Grep.** For each finding, read the surrounding lines to confirm it is a true
   uncited magic offset versus a benign constant the heuristic missed (a colour channel, a percentage,
   a loop bound). Quote the offending line in the report.

4. **Report.** Summarize `N uncited constant(s) across M file(s)` then list
   `path:line — <literal> — uncited magic constant` with a one-line context for each. If clean,
   state the tree is fully cited and how many `.cs` files were scanned.

5. **Recommend, do not edit.** For each real hit, recommend adding `// spec: Docs/RE/<the spec>` next
   to (or above) the constant — pointing at the relevant `formats/`, `structs/`, `packets/`, or
   `specs/` file. If no spec exists for that constant yet, that is a deeper gap: the value needs a
   promoted spec first (see the `re-promote` / `asset-format-doc` / `opcode-catalog` skills).

## Decision heuristics

- **If the constant is a wire/asset offset or record size** (`0x2C`, `Slice(112, 8)`, the 28-byte
  `npc.arr` / 20-byte `mob.arr` records, the 8-byte frame header) → it MUST cite a `packets/`/`structs/`/
  `formats/` spec; an uncited one is a real hit. These are the constants the firewall most cares about.
- **If the constant is an opcode** (`(major<<16)|minor`, or a bare `0x07`/`142`-shaped minor) → it cites
  `Docs/RE/opcodes.md`; flag if uncited.
- **If the literal is a colour channel, percent, loop bound, enum `= N`, `[InlineData]` value, or
  `new byte[n]` allocation** → benign, false positive; the scanner already suppresses most, confirm with
  Grep and don't report.
- **If a real offset has NO committed spec to cite** → don't just recommend a citation; escalate: the
  value needs promotion first via `re-promote` / `asset-format-doc` / `opcode-catalog`.
- **If a block of offsets sits under one `// spec:` header** → all pass within `--lookback`; raise the
  window only if a legitimately grouped block trips.

## Verify / Done when

- Every `**/*.cs` under the five layers + `tests/` scanned (build/generated/`_dirty/` skipped), file
  count reported.
- Findings listed as `path:line — <literal> — uncited magic constant` with one-line context, each
  triaged with Grep against the benign set.
- A `N uncited constant(s) across M file(s)` summary present; a clean run states the tree is fully cited.
  No file edited.

## Pitfalls

- Never resolve a finding by reading `_dirty/` or IDA to confirm the number — the fix is always a
  citation to a *committed neutral spec*, never to dirty.
- Never insert the citation or change the constant yourself — that is the engineer's deliberate act.
- Don't flag tiny structural constants (`0`,`1`,`-1`,`2`) or literals inside comments/strings — that is
  noise, not provenance.
- A clean report proves citations exist, not that they point at the *correct* spec; pair with
  `clean-room-audit` and human review.

> North star N1: every wire/asset/opcode constant tracing to a committed neutral spec is what proves the
> C# was built from specs, not eyeballed from the binary — the citation half of the clean-room firewall.

## Hard rules

- Read-only. This skill reports; it never inserts citations or changes constants. Adding the
  citation is the engineer's deliberate act (and it must point at a real, committed spec).
- Never "resolve" a finding by reading `_dirty/` or IDA to confirm the number — that would itself
  breach the firewall. The fix is always a citation to a *committed neutral spec*, never to dirty.
- A clean report proves only that magic numbers are cited, not that the citations are correct. Pair
  with `clean-room-audit` (leakage smells) and human review for release gates.
