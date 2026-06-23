---
name: memory-curate
description: Use to tidy the Claude auto-memory store for this project — review MEMORY.md and the per-fact memory files, dedupe overlapping facts, fix broken [[links]], ensure each fact is its own file with correct frontmatter, and prune stale/superseded entries. Read-mostly hygiene so the memory index stays trustworthy and small.
allowed-tools: Read Write Edit Glob Grep
model: opus
effort: high
---

# memory-curate — keep the auto-memory store clean and trustworthy

Claude's auto-memory for this project lives at:

```
C:/Users/Arius/.claude/projects/C--Users-Arius-RiderProjects-MartialHeroes/memory/
  MEMORY.md          ← the index: one bullet per fact, linking to its file
  <fact-slug>.md     ← one file per durable fact, with frontmatter
```

The index is consulted at the start of sessions, so a bloated, contradictory, or dead-linked memory
store actively misleads future work. This skill performs careful **curation**: it is read-mostly,
makes only conservative, well-justified edits, and never invents facts.

## Principles

- **One fact, one file.** Each durable fact is its own `<slug>.md` with frontmatter, summarized by
  exactly one bullet in `MEMORY.md`. Do not merge unrelated facts into one file or split one fact
  across several.
- **The index mirrors the files.** Every `MEMORY.md` bullet links to an existing file; every fact
  file is linked from the index. No orphans, no dead links.
- **Newest truth wins.** When two entries conflict (e.g. an old "greenfield skeleton" note vs. a
  newer "full client built" note), keep the current one and either delete or explicitly mark the
  superseded one as historical. Note the project moves fast — implementation-state facts go stale.
- **Conservative edits.** When unsure whether a fact is still true, flag it for the user rather than
  deleting it. Memory loss is worse than a slightly stale note.
- **No secrets, no copyrighted content.** Memory must not store decompiler pseudo-code, raw asset
  bytes, addresses, or credentials. If found, strip it (and note the firewall reason).

## Steps

1. **Inventory.** Read `MEMORY.md` and every `<slug>.md` in the memory dir (use Glob on the dir).
   Build a mental table: bullet ↔ file ↔ topic ↔ last-meaningful-date.

2. **Check structure of each fact file.** It should have frontmatter (e.g. a title/slug and any
   dating the existing files use) followed by a focused body. Match the shape of the files already
   present — do not impose a new schema. Fix files missing frontmatter or with a slug that disagrees
   with their filename.

3. **Reconcile the index ↔ files:**
   - Every bullet in `MEMORY.md` must link to a file that exists. Repair or remove dead `[[links]]`
     / markdown links.
   - Every fact file must be referenced by exactly one bullet. Add a bullet for an orphan file;
     remove a duplicate bullet pointing at the same file.
   - Ensure link targets resolve to real filenames (watch for slug/filename drift).

4. **Dedupe.** Where two files cover the same fact, keep the better/newer one, fold any unique detail
   into it, and delete the redundant file + its bullet. Where one file mixes two distinct facts,
   propose splitting (do it only if clearly warranted).

5. **Prune stale entries.** Mark or remove entries that are clearly superseded (an obsolete
   implementation-state snapshot once a newer one exists). Prefer keeping the *latest* state note and
   demoting older ones to a short historical line — or delete with a clear rationale. When in doubt,
   leave it and flag it.

6. **Tighten the index bullets.** Each `MEMORY.md` bullet should be a one-line, scannable summary
   (topic — what it tells you), in the same terse style as the existing bullets. Keep the file small;
   the index earns its keep by being fast to skim.

7. **Report** what you changed: files merged/split/deleted, links fixed, entries pruned, and a list
   of anything you flagged for the user to decide rather than acting on.

## Decision heuristics

- **If two implementation-state notes conflict** (old "greenfield skeleton" vs newer "full client +
  Godot, 551 tests") → keep the newest, demote the older to a one-line historical note or delete with a
  rationale. The project moves fast; state facts go stale first.
- **If a fact is uncertain or possibly-still-true** → flag it for the user; do not delete. Memory loss is
  a one-way cost worse than a stale note.
- **If a fact file holds a decompiler identifier, an address, a raw asset/packet byte, or a credential**
  → strip it on sight and note the firewall reason; this is the one place curation rewrites content.
- **If a bullet and its file slug drift** → repair the link target to the real filename; never leave a
  dead `[[link]]`.

## Verify / Done when

- Every `MEMORY.md` bullet links to a file that exists; every fact file is referenced by exactly one
  bullet (no orphans, no dead links, no duplicate bullets).
- Each fact is one focused file with frontmatter whose slug matches its filename.
- No conflicting state notes remain (newest kept, older demoted/removed with rationale); uncertain facts
  are flagged, not deleted.
- No pseudo-code, address, raw bytes, or secret survives in any memory file.
- Only files under the project's memory dir were touched; a change report was returned.

## Pitfalls

- Never edit repo source, `Docs/RE/`, or `.claude/` config under the banner of "curation" — memory dir only.
- Never fabricate or "improve" a fact's claim; curation reorganizes and prunes, it does not author.
- Never delete on a hunch — only what is provably duplicated or superseded, and say why; everything else
  is flagged.
- Don't impose a new frontmatter schema; match the shape of the files already present.

> North star: a trustworthy, small memory index keeps every future session correctly oriented on the N1
> firewall and N2 fidelity state — bad memory actively misleads the work.

## Hard rules

- Touch ONLY files under the project's memory directory
  (`…/projects/C--Users-Arius-RiderProjects-MartialHeroes/memory/`). Never edit repo source,
  `Docs/RE/`, or `.claude/` config as part of curation.
- Never fabricate or "improve" a fact's content — curation reorganizes and prunes; it does not
  author new claims. If a fact seems wrong, flag it; do not silently rewrite it.
- Default to conservative. Deleting memory is a one-way loss — only delete what is provably
  duplicated or superseded, and say why. Everything uncertain gets flagged, not removed.
- Keep the firewall: no pseudo-code, addresses, raw asset/packet bytes, or secrets ever live in
  memory. Strip any that slipped in and note it.
