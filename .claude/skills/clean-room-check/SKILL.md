---
name: clean-room-check
description: Use to AUDIT the clean-room firewall (the legal backbone under EU Software Directive 2009/24/EC Art. 6) — one read-only auditor, three modes. LEAK-SCAN smells out decompiler-derived identifiers (sub_/loc_/dword_), MSVC artifacts (__thiscall, _DWORD, mangled @@ names), and a1/v12 autoname variables leaked into committed C#. CITATION-AUDIT enforces "every magic constant cites its // spec: Docs/RE/..." — flags uncited byte offsets / sizes / opcodes / hex literals. FIREWALL-GATE is the exit-code pass/fail CI/pre-commit gate (no tracked _dirty/ or originals, no C# citing a _dirty/ path, every changed spec journaled). Read-only file:line / pass-fail reports graded by severity; it NEVER edits code and never opens _dirty/.
allowed-tools: Read Grep Glob Bash(git *) Bash(python *)
model: opus
effort: high
---

# clean-room-check — audit the clean-room firewall (read-only)

One skill, three audit modes that together protect the project's legal backbone: the new client is
reimplemented **only** from the neutral specs in `Docs/RE/` (the derived truth of what IDA proved
about `doida.exe`), never transcribed from IDA/Hex-Rays. These modes are the smell test, the
provenance check, and the hard gate that keep that firewall intact.

| Mode | Question | Output | Bundled script |
|---|---|---|---|
| **LEAK-SCAN** | "did decompiler output leak into the C#?" | `file:line — smell — severity` report | `leak_scan.py` |
| **CITATION-AUDIT** | "does every magic constant cite its spec?" | `file:line — literal — uncited` report | `citation_scan.py` |
| **FIREWALL-GATE** | "did the firewall hold for this change set?" | per-invariant verdict + **exit 0/1** | `firewall_check.py` |

**All three are read-only** — they report; the engineer fixes. Two share a hard rule: **never open or
read anything under `_dirty/`** to "verify" a hit — that read would itself cross the firewall. The
fix is always a from-spec reimplement or a citation to a *committed* spec, never a peek at the dirty
room or IDA.

## Mode A — LEAK-SCAN (decompiler-leakage smell report)

Detect telltale signs that raw decompiler output reached the shipped C#.

### What it flags

| Smell | Pattern (illustrative) | Severity | Why it matters |
|---|---|---|---|
| Hex-Rays autoname | `sub_4A1230`, `loc_4012F0`, `dword_6F0040`, `byte_…`, `word_…`, `unk_…`, `off_…` | high | Pasted decompiler identifiers — direct firewall breach. |
| MSVC pseudo-types | `_DWORD`, `_BYTE`, `_QWORD`, `__int64`, `__thiscall`, `__fastcall`, `__cdecl`, `LODWORD`, `HIDWORD` | high | Hex-Rays type/calling-convention artifacts; not idiomatic C#. |
| Mangled MSVC name | `?Method@Class@@…`, `@@QAE`, `@@YA` substrings | high | Raw mangled symbols copied from the binary. |
| `a1`/`v12` arg names | `\ba[0-9]+\b`, `\bv[0-9]+\b` in source | medium | Decompiler default variable names left in place. |
| Uncited magic offset | a byte-offset literal (`0x14`, `[0x2C]`, `Slice(40, 4)`, `+ 0x18`) with no nearby `// spec:` | medium | Magic offsets MUST cite `Docs/RE/…`; uncited ones may be eyeballed. |

`0xNN` constants are normal in crypto/protocol code — the smell is the **absence of a spec citation**,
not the constant itself (the deep offset sweep is Mode B).

### Steps

1. **Scope.** Every `**/*.cs` under the five numbered layer folders. Exclude `**/obj/**`, `**/bin/**`,
   `*.g.cs` / `*.Designer.cs` / source-generator output. **Never** scan `Docs/RE/_dirty/`.
2. **Run:** `python ${CLAUDE_SKILL_DIR}/scripts/leak_scan.py --root . --format text` (add
   `--format json`, or `--root 02.Network.Layer` to scope one project; stdlib-only, walks the tree itself).
3. **Corroborate high hits with Grep** — confirm a genuine leak vs. a false positive (`Subtract`,
   `offsetTable`, a viewmodel `v2`). Quote the offending line.
4. **Report** one section per severity (high first): `path:line — <smell> — <severity>` + one-line
   rationale; top-line `N high, M medium`. Zero findings ⇒ state the tree is clean + file count.
5. **Recommend, do not edit.** High leaks → reimplement from the cited `Docs/RE/` spec (+ add `// spec:`);
   uncited offsets → add the missing citation (or defer to Mode B).

### Heuristics

- `sub_`/`_DWORD`/`__thiscall`/`@@`-mangled → **high / BLOCKER**: reimplement from spec, not rename.
- `a1`/`v12`-shaped → medium/advisory: confirm with Grep it is a real decompiler variable, not `a1Channel`.
- Uncited offset → medium: distinguish a genuine wire/asset offset from a benign constant; defer the deep
  sweep to Mode B.
- Zero high hits but many uncited offsets ⇒ leak-free but provenance-thin → run Mode B.

## Mode B — CITATION-AUDIT (magic constants missing `// spec:`)

Enforce the rule: **every magic numeric constant in C# cites its source spec** with `// spec:
Docs/RE/…` on the same line or just above. An uncited offset/size/opcode is provenance-less — it may
have been eyeballed from the binary, exactly what the firewall forbids. Narrower than Mode A: the
single question is *"does this magic number cite a spec?"*.

### What counts (and what is ignored)

Flagged when uncited: hex literals `0x..` of ≥2 digits (`0x90`, `0x2C`, `0x1F4`); numeric indexers /
slices (`[40]`, `[0x14]`, `Slice(112, 8)`, `+ 104`); larger bare decimals (≥ threshold, default 16).
Ignored: `0`/`1`/`-1`/`2` and tiny structural constants; literals in comments/strings; lines already
under a `// spec: Docs/RE/…` (within the lookback window); `[Theory]`/`[InlineData]`, enum `= N`,
`Version = "…"`, `new byte[n]` allocations; all of `obj/`, `bin/`, generated, and `_dirty/`.

### Steps

1. **Scope.** Every `**/*.cs` under the five layers + `tests/` (build/generated skipped). `--root
   02.Network.Layer` scopes to one project.
2. **Run:** `python ${CLAUDE_SKILL_DIR}/scripts/citation_scan.py --root .` — flags: `--format json`,
   `--root <dir>`, `--min-decimal <n>` (default 16), `--lookback <n>` (default 3 lines above a literal).
3. **Triage hits with Grep** — true uncited offset vs. a benign constant the heuristic missed. Quote the line.
4. **Report** `N uncited constant(s) across M file(s)`, then `path:line — <literal> — uncited magic
   constant` + context. Clean ⇒ fully-cited + file count.
5. **Recommend, do not edit.** Add `// spec: Docs/RE/<spec>` (a `formats/`/`structs/`/`packets/`/`specs/`
   file). If **no spec exists yet** for that constant, escalate: it needs promotion first via
   `re-promote` / `asset-format-doc` / `packet-codegen` (opcode-catalog mode).

### Heuristics

- A wire/asset offset or record size (`0x2C`, `Slice(112, 8)`, the 28-byte `npc.arr` / 20-byte
  `mob.arr` records, the 8-byte frame header) → MUST cite `packets/`/`structs/`/`formats/`; uncited = real hit.
- An opcode (`(major<<16)|minor`, or a bare minor) → cites `Docs/RE/opcodes.md`.
- A colour channel / percent / loop bound / enum `= N` / `[InlineData]` / `new byte[n]` → benign FP.
- A real offset with **no committed spec to cite** → escalate to promotion, don't just recommend a citation.

## Mode C — FIREWALL-GATE (exit-code pass/fail for CI / pre-commit)

The hard gate (returns nonzero so it can block a commit/merge). Read-only apart from running `git`.

### The three invariants (all BLOCKERs)

1. **The quarantine stays out of git.** No tracked/staged path contains `Docs/RE/_dirty/`; also fail on
   staged copyrighted originals (`*.pak`, `*.pcapng`, `*.tsv`, `*.exe`, `*.dll`). Fix: `git rm --cached`
   + confirm `.gitignore` coverage.
2. **Clean-room C# never points at the dirty room.** No `.cs` under a numbered layer folder references a
   `_dirty/` path (citation, string, or comment). Fix: recite the committed spec (`Docs/RE/packets/…`).
3. **Spec changes are journaled.** Every changed/added committed spec under `Docs/RE/` (`packets/*.yaml`,
   `formats/*.md`, `structs/*.md`, `specs/*.md`, `opcodes.md`, `names.yaml`) has a matching mention in
   `Docs/RE/journal.md` (or `journal.md` is in the same change set). Fix: run `preservation`
   (session-log mode) to add the provenance entry.

### Steps

1. **Determine the change set.** Pre-commit ⇒ staged (`git diff --cached --name-only --diff-filter=ACMR`);
   CI ⇒ merge-base / tracked. Pass `--mode staged` (default) or `--mode tracked`.
2. **Run:** `python ${CLAUDE_SKILL_DIR}/scripts/firewall_check.py --mode staged` — shells out to `git`
   (stdlib `subprocess`), applies the three invariants, prints a verdict per invariant, exits `0` pass /
   `1` on any violation.
3. **Report violations precisely** (per invariant: the offending path, or `file:line` for invariant 2,
   + the fix action).
4. **Exit semantics for hooks.** Surface the script's exit code **unchanged** — `0` proceeds, `1` blocks.
   Never auto-fix and continue; a breach is a human decision. Never downgrade a swallowed `1`.

### Heuristics

- Invariant 1 on a copyrighted original → `git rm --cached` AND verify the pattern, never a `.gitignore`
  tweak alone.
- Invariant 3 but `journal.md` is in the same change set → PASS (the provenance link moves with the spec).
- CI vs pre-commit → `--mode tracked` for a full-repo merge-base audit, `--mode staged` for the local gate;
  never silently switch modes.

## Verify / Done when

- The requested mode ran over the right scope (layers for A/B; the change set for C), file/scope count reported.
- Mode A/B: each finding is `path:line — … — severity/uncited` with a one-line rationale; high hits
  corroborated with Grep; a top-line summary present; a clean run names the file count.
- Mode C: all three invariants printed with a per-invariant verdict; the exit code surfaced **unchanged**.
- No file was edited, staged, or committed to make any check pass; **nothing under `_dirty/` was opened**.

## Pitfalls (never)

- Never "verify" a hit by opening `_dirty/` or IDA — that read itself crosses the firewall.
- Never edit/stage/commit to silence a smell or pass the gate — report only; the engineer fixes.
- Never auto-resolve a gate breach (`git rm`, edit, re-stage) and continue — a firewall trip is a human decision.
- Never downgrade the gate's exit code in a hook wrapper — a swallowed `1` defeats it.
- Don't over-flag: `Subtract`, `offsetTable`, a test `v2`, colour channels, percents, loop bounds are FPs — read context.
- A clean/green result excludes only *these* checks; pair the modes (and human review) for a release gate.

> North star N1: this is the firewall's three-layered guard — the smell test, the citation check, and
> the hard gate that keep decompiler output, uncited constants, tainted `_dirty/` material, and
> copyrighted originals out of the shipped C# and git history, so the EU Art. 6 clean-room basis holds.

## Hard rules

- Read-only across all modes (Mode C may run `git` queries only). Never Edit/Write source; never stage,
  commit, or edit files to make a check pass. The deliverable is a report / an exit code.
- Never open or print anything under `_dirty/` — every mode works on paths and committed C#/spec/journal text only.
- A clean/green result asserts only the checks it ran; it is not legal proof. Pair the modes with each
  other and with human auditor review for release gates.
