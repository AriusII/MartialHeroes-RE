---
name: clean-room-audit
description: Use to scan the C# source tree for clean-room leakage smells — decompiler-derived identifiers (sub_/loc_/dword_), MSVC artifacts (__thiscall, _DWORD, mangled names), and uncited magic byte-offset literals that should reference a Docs/RE spec. Produces a read-only file:line report graded by severity; it never edits code.
allowed-tools: Read Grep Glob Bash(python *)
model: opus
effort: high
---

# clean-room-audit

Detect telltale signs that decompiler output leaked into the new C# codebase, violating the
project's clean-room firewall (the legal backbone under EU Software Directive 2009/24/EC, Art. 6).
The new client must be reimplemented from neutral specs in `Docs/RE/`, never transcribed from
IDA/Hex-Rays. This skill is the smell test for that rule.

It is **read-only**: it reports `file:line — smell — severity` and never modifies a file. Fixing is
a human/engineer decision, because some hits are false positives (e.g. a legitimately named
`subProcess`).

## What it flags

| Smell | Pattern (illustrative) | Severity | Why it matters |
|---|---|---|---|
| Hex-Rays autoname | `sub_4A1230`, `loc_4012F0`, `dword_6F0040`, `byte_…`, `word_…`, `unk_…`, `off_…` | high | Pasted decompiler identifiers — direct firewall breach. |
| MSVC pseudo-types | `_DWORD`, `_BYTE`, `_QWORD`, `__int64`, `__thiscall`, `__fastcall`, `__cdecl`, `LODWORD`, `HIDWORD` | high | Hex-Rays type/calling-convention artifacts; not idiomatic C#. |
| Mangled MSVC name | `?Method@Class@@…`, `@@QAE`, `@@YA` substrings | high | Raw mangled symbols copied from the binary. |
| `a1`/`v12` arg names | identifiers like `\ba[0-9]+\b`, `\bv[0-9]+\b` in source | medium | Decompiler's default variable names left in place. |
| Uncited magic offset | a byte-offset literal (`0x14`, `[0x2C]`, `Slice(40, 4)`, `+ 0x18`) on a line/region with no nearby `// spec:` citation | medium | Magic offsets MUST cite `Docs/RE/...`; uncited ones may be eyeballed from the binary. |

Note `0xNN` constants are normal in crypto/protocol code — the smell is the **absence of a spec
citation**, not the constant itself. The scanner only escalates an offset literal when no
`// spec: Docs/RE/...` comment appears on the same line or within a few lines above it.

## Steps

1. **Scope the sweep.** Target every `**/*.cs` under the source tree (the five numbered layer
   folders). Exclude generated/build output: `**/obj/**`, `**/bin/**`, and `*.g.cs` /
   `*.Designer.cs` / source-generator output. Never scan `Docs/RE/_dirty/` (it is allowed to be
   dirty, and is gitignored anyway).

2. **Run the bundled scanner:**

   ```
   python ${CLAUDE_SKILL_DIR}/scripts/leak_scan.py --root . --format text
   ```

   Add `--format json` for machine-readable output, or pass explicit paths to scope to a single
   project (e.g. `--root 02.Network.Layer`). The script is stdlib-only and walks the tree itself.

3. **Corroborate with Grep when needed.** For any high-severity hit, use Grep to read the
   surrounding context and confirm it is a genuine leak versus a false positive (a method named
   `Subtract`, a field `offsetTable`, etc.). Quote the offending line in the report.

4. **Emit the report.** One section per severity (high first). For each finding print
   `path:line — <smell label> — <severity>` and a one-line rationale. Summarize counts at the top
   (`N high, M medium`). If zero findings: state the tree is clean and name how many `.cs` files
   were scanned.

5. **Recommend, do not edit.** For high-severity leaks, recommend the engineer reimplement the
   region from the cited `Docs/RE/` spec (and add the `// spec:` citation). For uncited offsets,
   recommend adding the missing `// spec: Docs/RE/...` reference. This skill never touches the file.

## Decision heuristics

- **If a hit is a Hex-Rays autoname or MSVC pseudo-type** (`sub_`, `_DWORD`, `__thiscall`, a mangled
  `@@` name) → grade **high / BLOCKER**: these almost never occur innocently in idiomatic C# and are a
  direct firewall breach. Recommend a from-spec reimplement, not a rename.
- **If a hit is `a1`/`v12`-shaped** → grade medium/advisory: confirm with Grep it is a real decompiler
  variable, not a legit field (`a1Channel`, a viewmodel `v2`). When the surrounding code reads
  spec-derived and well-named, it is a false positive — say so.
- **If a hit is an uncited offset** → medium: distinguish a genuine wire/asset offset (escalate, needs
  `// spec:`) from a benign constant (colour channel, percent, loop bound). Defer the deep offset sweep
  to `spec-citation-audit`; here just surface the smell.
- **If zero high-severity hits but many uncited offsets** → the tree is leak-free but provenance-thin;
  point the caller at `spec-citation-audit` rather than over-grading here.

## Verify / Done when

- Every `**/*.cs` under the five numbered layers scanned (obj/bin/generated/`_dirty/` excluded), file
  count reported.
- Each finding is `path:line — smell — severity` with a one-line rationale; high-severity hits were
  corroborated with Grep against false positives.
- A top-line `N high, M medium` summary is present; a zero-finding run states the tree is clean and how
  many files were scanned. No file was edited.

## Pitfalls

- Never "verify" a hit by opening `_dirty/` or IDA — that read itself crosses the firewall.
- Never edit source to silence a smell; this skill reports, the engineer fixes.
- Never treat a clean report as legal proof — it only excludes *these* smells; pair with
  `clean-room-firewall-check` and human review for a release gate.
- Don't over-flag: a method named `Subtract`, a field `offsetTable`, or a test `v2` are false positives
  — read context before grading high.

> North star N1: this is the firewall's smell test — it keeps decompiler output from leaking into the
> shipped C# so the EU Art. 6 clean-room basis holds.

## Hard rules

- Read-only. Never Edit/Write source. The deliverable is a report.
- Do not read anything under `_dirty/` to "verify" a hit — that would itself cross the firewall.
- A clean report is not proof of innocence, only absence of these specific smells; pair with the
  human `clean-room-firewall-check` (CI) and auditor review for release gates.
