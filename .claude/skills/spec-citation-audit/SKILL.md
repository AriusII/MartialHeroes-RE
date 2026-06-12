---
name: spec-citation-audit
description: Use to enforce the project's "every magic constant cites its spec" rule — scans committed layer C# for magic numeric literals (byte offsets, sizes, opcodes, hex constants) that lack a nearby '// spec: Docs/RE/...' citation, and reports file:line. The targeted citation checker that backs the clean-room provenance of every wire/asset constant.
allowed-tools: Read Grep Glob Bash(python *)
model: sonnet
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

## Hard rules

- Read-only. This skill reports; it never inserts citations or changes constants. Adding the
  citation is the engineer's deliberate act (and it must point at a real, committed spec).
- Never "resolve" a finding by reading `_dirty/` or IDA to confirm the number — that would itself
  breach the firewall. The fix is always a citation to a *committed neutral spec*, never to dirty.
- A clean report proves only that magic numbers are cited, not that the citations are correct. Pair
  with `clean-room-audit` (leakage smells) and human review for release gates.
