---
name: clean-room-firewall-check
description: Use in CI or as a pre-commit gate to verify the clean-room firewall held. Fails nonzero if any committed/staged file lives under Docs/RE/_dirty/, if any C# under a numbered layer folder cites a _dirty/ path, or if a changed Docs/RE spec has no matching mention in journal.md. Read-only plus git status; designed for exit-code-driven hooks.
allowed-tools: Read Grep Glob Bash(git *) Bash(python *)
model: sonnet
effort: high
---

# clean-room-firewall-check

Enforce the three invariants that keep the Martial Heroes project legally clean (EU Software
Directive 2009/24/EC, Art. 6). Unlike `clean-room-audit` (a heuristic smell report), this is a
**pass/fail gate**: it returns a nonzero exit code so it can sit in a pre-commit hook or CI step and
block the commit/merge. It is read-only apart from running `git`.

## The three invariants

1. **The quarantine stays out of git.** No tracked or staged file path may contain `Docs/RE/_dirty/`.
   That subtree is gitignored; if something there is tracked/staged, the firewall has been breached
   (someone `git add -f`'d tainted material). Also fail on staged copyrighted originals
   (`*.pak`, `*.pcapng`, `*.tsv`, `*.exe`, `*.dll`) for the same reason.

2. **Clean-room C# never points at the dirty room.** No `.cs` file under a numbered layer folder
   (`01.Infrastructure.Shared` … `05.Presentation`) may reference a `_dirty/` path — not in a
   `// spec:` citation, not in a string, not in a comment. Engineers cite committed specs
   (`Docs/RE/packets/...`), never quarantine paths.

3. **Spec changes are journaled.** Every changed/added committed spec under `Docs/RE/`
   (`packets/*.yaml`, `formats/*.md`, `structs/*.md`, `specs/*.md`, `opcodes.md`, `names.yaml`) must
   have a matching mention in `Docs/RE/journal.md` — either the spec's path/filename appears in the
   journal, or `journal.md` itself is part of the same change set. This is the provenance link that
   backs the Art. 6 audit trail.

## Steps

1. **Determine the change set.** In a pre-commit context use the staged set
   (`git diff --cached --name-only --diff-filter=ACMR`); in CI compare against the merge base
   (`git diff --name-only origin/master...HEAD`) or scan tracked files. Pass the mode to the script
   via `--mode staged` (default) or `--mode tracked`.

2. **Run the bundled gate:**

   ```
   python ${CLAUDE_SKILL_DIR}/scripts/firewall_check.py --mode staged
   ```

   The script shells out to `git` (stdlib `subprocess`) for the file lists, applies the three
   invariants, prints a verdict per invariant, and exits `0` on pass / `1` on any violation. Use
   `--mode tracked` for a full-repo audit in CI.

3. **Report violations precisely.** For each failure print which invariant tripped and the offending
   path(s):
   - invariant 1: the tracked/staged `_dirty/` or originals path — instruct `git rm --cached` it and
     confirm `.gitignore` still covers it.
   - invariant 2: `file:line` of the `_dirty/` reference in C# — instruct the engineer to recite the
     committed spec instead.
   - invariant 3: the spec path that changed without a journal mention — instruct running the
     `re-session-log` skill to add the provenance entry before committing.

4. **Exit semantics for hooks.** Surface the script's exit code unchanged: `0` lets the commit/merge
   proceed; `1` blocks it. Do not "fix and continue" automatically — a firewall breach needs a human
   decision. As a pre-commit hook, a typical `.git/hooks/pre-commit` runs the script and aborts the
   commit on nonzero.

## BLOCKER vs advisory

All three invariants are **BLOCKERs** — this skill is a hard gate, unlike `clean-room-audit` (advisory
smell report). Exit `1` means the commit/merge must not proceed until a human resolves it. Severity ladder:

- **Invariant 1 (tracked/staged `_dirty/` or originals)** — highest: tainted material or a copyrighted
  binary is about to enter git history. `git rm --cached` and confirm `.gitignore` coverage.
- **Invariant 2 (`.cs` cites a `_dirty/` path)** — a clean-room file points at the quarantine; recite
  the committed spec (`Docs/RE/packets/...`) instead.
- **Invariant 3 (spec changed without a journal mention)** — provenance gap; run `re-session-log`.

## Decision heuristics

- **If invariant 1 trips on a copyrighted original** (`*.pak`/`*.exe`/`*.dll`/`*.pcapng`/`*.tsv`) → never
  suggest a `.gitignore` tweak alone; the file must be `git rm --cached`'d AND the pattern verified.
- **If invariant 3 trips but `journal.md` is in the same change set** → PASS that spec; the provenance
  link is satisfied by the journal moving together with the spec.
- **If run in CI vs pre-commit** → use `--mode tracked` for a full-repo merge-base audit, `--mode staged`
  (default) for the local gate; never silently switch modes.

## Verify / Done when

- All three invariants printed with a per-invariant verdict; the script's exit code surfaced unchanged
  (`0` pass / `1` block).
- Every violation names the exact offending path (or `file:line` for invariant 2) and the fix action.
- No file was staged, committed, or edited to make the check pass; nothing under `_dirty/` was opened.

## Pitfalls

- Never auto-resolve a breach (`git rm`, edit, re-stage) and continue — a firewall trip is a human
  decision; only report.
- Never open or print `_dirty/` contents to "investigate" — the check works on paths and committed
  C#/spec/journal text only.
- Don't downgrade the exit code in a hook wrapper — a swallowed `1` defeats the gate.
- A green result asserts only these three invariants; deeper review (`clean-room-audit`, human auditor)
  is still required for release.

> North star N1: this is the firewall's hard gate — it stops tainted `_dirty/` material, copyrighted
> originals, and unprovenanced specs from ever entering committed history, keeping the Art. 6 basis intact.

## Hard rules

- Read-only except for `git` queries; never stage, commit, or edit files to make the check pass.
- Never open or print the contents of anything under `_dirty/` — the check works on paths and the
  committed C#/spec/journal text only.
- A green result asserts only these three invariants held; deeper review (`clean-room-audit`, human
  auditor) is still required for release.
